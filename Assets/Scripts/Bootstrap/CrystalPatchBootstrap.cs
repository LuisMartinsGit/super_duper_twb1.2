// File: Assets/Scripts/Bootstrap/CrystalPatchBootstrap.cs
// Spawns mineable crystal cadavers as patches near each player and scattered
// across the map, so AI / players have a starting crystal source without
// having to fight Crystallings first. Used in addition to (or in place of)
// CrystalNodeBootstrap depending on map mode.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns cadaver-based crystal patches at game start.
    ///
    /// Layout per player:
    /// - 1 NEAR patch close to the Hall (within NearPatchMinDist..MaxDist)
    /// - <see cref="ScatteredPatchesPerPlayer"/> patches scattered across the map
    ///
    /// Each patch = a small cluster of cadavers with crystal in them. Mineable
    /// by Miners via GatherCommand on a cadaver (CrystalMiningSystem handles
    /// the gathering loop). Independent of CrystalNodeBootstrap (which spawns
    /// the curse main nodes that grow Crystallings).
    /// </summary>
    public static class CrystalPatchBootstrap
    {
        // Crystal amount per node. 30 = ~45s of mining at 1 crystal/1.5s for
        // one miner. NEAR patch totals 900 (30 nodes × 30 crystal — see
        // SpawnNearPatch); scattered patches use DefaultOutcropsPerPatch.
        private const int CrystalPerOutcrop = 30;
        private const int DefaultOutcropsPerPatch = 3;

        /// <summary>
        /// Number of nodes in the NEAR patch (one patch per player, close to
        /// the Hall). Sized to read as a "large field" of crystals rather
        /// than a handful — 30 × 30 = 900 starter crystal per player.
        /// </summary>
        private const int NearPatchOutcrops = 30;
        private const int NearPatchTotalCrystal = NearPatchOutcrops * CrystalPerOutcrop;

        // Cluster geometry — radius of the spawn disc around the patch
        // centre. Near patches pack 30 nodes into a 7 m disc with the
        // crystals scaled 6× their authored size, so adjacent clusters
        // heavily overlap and form a single dense crystalline mass.
        // Scattered patches stay tight at 5 m for their 3-node mini-deposits.
        private const float NearPatchSpread = 7f;
        private const float ScatteredPatchSpread = 5f;

        // NEAR patch (one per player)
        private const float NearPatchMinDist = 22f; // outside Hall footprint
        private const float NearPatchMaxDist = 32f;

        // SCATTERED patches. Bumped 2 → 5 — paired with the iron-deposit
        // bump for the same player-feedback "more deposits" reason.
        private const int ScatteredPatchesPerPlayer = 5;
        private const float ScatteredMinDistFromPlayer = 50f;
        private const float MinDistBetweenPatchCenters = 24f;

        // Heightmap constraints (only enforced when NOT FlatTestMap)
        private const float MinHeight = 23f;
        private const float MaxHeight = 85f;
        private const float MaxSlope = 0.6f;

        public static void SpawnCrystalPatches()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xCEED));

            // Hand-authored maps: CrystalPatchMarkers in the scene replace
            // the procedural near + scattered placement entirely.
            if (MapMarkerRegistry.HasCrystalMarkers)
            {
                SpawnCrystalFromMarkers(em, ref random);
                return;
            }

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.85f;

            var patchCenters = new Unity.Collections.NativeList<float3>(
                playerPositions.Length * (1 + ScatteredPatchesPerPlayer),
                Unity.Collections.Allocator.Temp);

            // 1. NEAR patches — one per player. Sum to NearPatchTotalCrystal (800).
            for (int p = 0; p < playerPositions.Length; p++)
            {
                float3 center = PickNearPatchCenter(playerPositions[p], ref random);
                SpawnNearPatch(em, center, ref random);
                patchCenters.Add(center);
            }

            // 2. SCATTERED patches — N per player, gated by distance + terrain.
            int scatteredCount = playerPositions.Length * ScatteredPatchesPerPlayer;
            for (int i = 0; i < scatteredCount; i++)
            {
                if (TryFindScatteredPatchCenter(ref random, spawnRange,
                        playerPositions, patchCenters, out float3 center))
                {
                    SpawnScatteredPatch(em, center, ref random);
                    patchCenters.Add(center);
                }
            }

            // Clear forest macro-cell ObstacleTag entities (and their
            // passability footprint) so units can reach every node in the
            // patch. Near patches use the larger NearPatchSpread; scattered
            // patches stay tight. We use the larger radius across all
            // centres rather than splitting the query — over-clearing a few
            // metres around scattered patches is harmless, and one
            // EntityQuery is cheaper than two.
            ClearObstaclesAroundPoints(em, patchCenters, NearPatchSpread + 2f);

            patchCenters.Dispose();
        }

        /// <summary>
        /// Marker-driven path: spawn one crystal patch per CrystalPatchMarker
        /// in the scene, honouring its NodeCount / CrystalPerNode / Spread /
        /// Layout. Skips slope / reachability / distance gates — markers are
        /// authoritative. Still clears forest macro-cell obstacles around the
        /// patch so units can reach every node.
        /// </summary>
        private static void SpawnCrystalFromMarkers(EntityManager em, ref Unity.Mathematics.Random random)
        {
            var markers = MapMarkerRegistry.CrystalPatches;
            var centers = new Unity.Collections.NativeList<float3>(
                Mathf.Max(1, markers.Count), Unity.Collections.Allocator.Temp);

            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                var p = m.WorldPosition;
                float y = TerrainUtility.GetHeight(p.x, p.z);
                float3 center = new float3(p.x, y, p.z);

                if (m.Layout == PatchLayout.HexGrid)
                    SpawnCrystalPatchHex(em, center, m.NodeCount, m.CrystalPerNode, m.Spread, ref random);
                else
                    SpawnCrystalPatchRandom(em, center, m.NodeCount, m.CrystalPerNode, m.Spread, ref random);

                centers.Add(center);
            }

            // Clear forest / rock obstacles around each patch so the nodes
            // are reachable. Use the marker's spread + a small margin.
            ClearObstaclesAroundPoints(em, centers, NearPatchSpread + 2f);
            centers.Dispose();
        }

        /// <summary>Marker-driven hex-grid spawn; mirrors SpawnNearPatch.</summary>
        private static void SpawnCrystalPatchHex(EntityManager em, float3 center,
            int nodeCount, int crystalPerNode, float spread, ref Unity.Mathematics.Random random)
        {
            int rings = nodeCount <= 7  ? 1 :
                        nodeCount <= 19 ? 2 :
                        nodeCount <= 37 ? 3 : 4;
            float spacing = spread / Mathf.Max(1, rings);
            float jitter  = spacing * 0.30f;

            var slots = new Unity.Collections.NativeList<float2>(64, Unity.Collections.Allocator.Temp);
            GenerateHexSlots(rings, spacing, slots);

            for (int i = slots.Length - 1; i > 0; i--)
            {
                int j = random.NextInt(0, i + 1);
                float2 tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
            }

            int placeCount = math.min(nodeCount, slots.Length);
            for (int i = 0; i < placeCount; i++)
            {
                float2 slot = slots[i];
                float jx = random.NextFloat(-jitter, jitter);
                float jz = random.NextFloat(-jitter, jitter);
                float x = center.x + slot.x + jx;
                float z = center.z + slot.y + jz;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.Create(em, new float3(x, y, z), crystalPerNode);
            }

            slots.Dispose();
        }

        /// <summary>Marker-driven random-cluster spawn; mirrors SpawnScatteredPatch.</summary>
        private static void SpawnCrystalPatchRandom(EntityManager em, float3 center,
            int nodeCount, int crystalPerNode, float spread, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, spread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.CreateOrMerge(em, new float3(x, y, z), crystalPerNode);
            }
        }

        // Destroy ObstacleTag entities (forest macro cells, rocks) and
        // unblock the matching passability cells anywhere within
        // <paramref name="clearRadius"/> of <paramref name="points"/>.
        //
        // IMPORTANT: this excludes resource deposits — CadaverTag (the
        // crystal nodes we just spawned, which now carry ObstacleTag so
        // they carve the navmesh) and IronMineTag (iron deposits, ditto).
        // Without that filter the bootstrap deleted its own freshly-spawned
        // crystals on the line after creating them, and could clip iron
        // deposits that happened to land within clearRadius of a patch
        // centre.
        private static void ClearObstaclesAroundPoints(
            EntityManager em,
            Unity.Collections.NativeList<float3> points,
            float clearRadius)
        {
            var query = em.CreateEntityQuery(
                new Unity.Entities.EntityQueryDesc
                {
                    All = new[]
                    {
                        Unity.Entities.ComponentType.ReadOnly<ObstacleTag>(),
                        Unity.Entities.ComponentType.ReadOnly<LocalTransform>(),
                        Unity.Entities.ComponentType.ReadOnly<Radius>(),
                    },
                    None = new[]
                    {
                        Unity.Entities.ComponentType.ReadOnly<CadaverTag>(),
                        Unity.Entities.ComponentType.ReadOnly<IronMineTag>(),
                    },
                });
            using var ents = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var trs  = query.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            using var rds  = query.ToComponentDataArray<Radius>(Unity.Collections.Allocator.Temp);

            var grid = PassabilityGrid.Instance;
            var toDestroy = new Unity.Collections.NativeList<Entity>(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                float3 p = trs[i].Position;
                float r = rds[i].Value;
                for (int c = 0; c < points.Length; c++)
                {
                    if (math.distance(p, points[c]) <= clearRadius + r)
                    {
                        toDestroy.Add(ents[i]);
                        if (grid != null) grid.UnblockObstacle(p, r + 1f);
                        break;
                    }
                }
            }

            for (int i = 0; i < toDestroy.Length; i++)
                em.DestroyEntity(toDestroy[i]);
            toDestroy.Dispose();
        }

        // Axial-coordinate neighbour directions for hex-grid traversal.
        // Walked in this order they trace each ring once around the centre.
        private static readonly int[,] HexDirs = new int[,]
        {
            {  1,  0 }, {  1, -1 }, {  0, -1 },
            { -1,  0 }, { -1,  1 }, {  0,  1 }
        };

        /// <summary>
        /// Near-patch spawn — 30 distinct nodes placed on a hex grid with
        /// per-cell jitter, so the patch is evenly packed but doesn't read
        /// as a uniform hexagonal field. Uses Cadaver.Create (not
        /// CreateOrMerge) so every grid cell yields its own node.
        /// </summary>
        private static void SpawnNearPatch(EntityManager em, float3 center, ref Unity.Mathematics.Random random)
        {
            // 3 rings = 1 + 6 + 12 + 18 = 37 candidate slots. We take the
            // first NearPatchOutcrops after shuffling, giving the patch a
            // few natural "holes" plus a randomised silhouette. Spacing is
            // chosen so the outermost ring sits at ≈NearPatchSpread.
            const int Rings = 3;
            float spacing = NearPatchSpread / Rings;
            float jitter  = spacing * 0.30f; // ±30% — disguises the grid

            var slots = new Unity.Collections.NativeList<float2>(40, Unity.Collections.Allocator.Temp);
            GenerateHexSlots(Rings, spacing, slots);

            // Fisher–Yates shuffle.
            for (int i = slots.Length - 1; i > 0; i--)
            {
                int j = random.NextInt(0, i + 1);
                float2 tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
            }

            int placeCount = math.min(NearPatchOutcrops, slots.Length);
            for (int i = 0; i < placeCount; i++)
            {
                float2 slot = slots[i];
                float jx = random.NextFloat(-jitter, jitter);
                float jz = random.NextFloat(-jitter, jitter);
                float x = center.x + slot.x + jx;
                float z = center.z + slot.y + jz;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.Create(em, new float3(x, y, z), CrystalPerOutcrop);
            }

            slots.Dispose();
        }

        /// <summary>
        /// Fills <paramref name="output"/> with the cartesian positions of
        /// every cell in a hex grid of <paramref name="maxRings"/> rings,
        /// starting from the centre cell (ring 0). Output is centred on the
        /// origin — the caller offsets to the patch position.
        /// </summary>
        private static void GenerateHexSlots(
            int maxRings,
            float spacing,
            Unity.Collections.NativeList<float2> output)
        {
            output.Add(float2.zero);
            const float SQRT3_OVER_2 = 0.8660254f;

            for (int ring = 1; ring <= maxRings; ring++)
            {
                // Start at the (q=-ring, r=ring) corner; walking the six
                // axial-neighbour directions visits every cell in the ring.
                int q = -ring;
                int r = ring;
                for (int side = 0; side < 6; side++)
                {
                    for (int step = 0; step < ring; step++)
                    {
                        float x = spacing * (q + r * 0.5f);
                        float z = spacing * r * SQRT3_OVER_2;
                        output.Add(new float2(x, z));
                        q += HexDirs[side, 0];
                        r += HexDirs[side, 1];
                    }
                }
            }
        }

        /// <summary>
        /// Scattered-patch spawn — uses DefaultOutcropsPerPatch (currently 3).
        /// Total per scattered patch = DefaultOutcropsPerPatch × CrystalPerOutcrop.
        /// </summary>
        private static void SpawnScatteredPatch(EntityManager em, float3 center, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < DefaultOutcropsPerPatch; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, ScatteredPatchSpread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.CreateOrMerge(em, new float3(x, y, z), CrystalPerOutcrop);
            }
        }

        private static float3 PickNearPatchCenter(float3 player, ref Unity.Mathematics.Random random)
        {
            // Retry up to 16 times if the candidate lands on terrain the
            // passability layer considers blocked. Falls back to the last
            // sample so we never skip a player's near patch.
            var passGrid = PassabilityGrid.Instance;
            float3 last = float3.zero;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(NearPatchMinDist, NearPatchMaxDist);
                float x = player.x + math.cos(angle) * dist;
                float z = player.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                last = new float3(x, y, z);
                if (passGrid == null || passGrid.IsReachableByAllPlayersForRadius(last, NearPatchSpread + 1f))
                    return last;
            }
            return last;
        }

        private static bool TryFindScatteredPatchCenter(
            ref Unity.Mathematics.Random random,
            float spawnRange,
            float3[] playerPositions,
            Unity.Collections.NativeList<float3> patchCenters,
            out float3 result)
        {
            result = float3.zero;
            bool isFlat = GameSettings.FlatTestMap;
            var terrain = ProceduralTerrain.Instance;

            for (int attempt = 0; attempt < 40; attempt++)
            {
                float x = random.NextFloat(-spawnRange, spawnRange);
                float z = random.NextFloat(-spawnRange, spawnRange);
                float y = TerrainUtility.GetHeight(x, z);
                float3 candidate = new float3(x, y, z);

                if (!isFlat)
                {
                    if (terrain != null && terrain.IsInWater(new Vector3(x, y, z))) continue;
                    if (y < MinHeight || y > MaxHeight) continue;

                    float step = 2f;
                    float hL = TerrainUtility.GetHeight(x - step, z);
                    float hR = TerrainUtility.GetHeight(x + step, z);
                    float hD = TerrainUtility.GetHeight(x, z - step);
                    float hU = TerrainUtility.GetHeight(x, z + step);
                    float dX = (hR - hL) / (step * 2f);
                    float dZ = (hU - hD) / (step * 2f);
                    if (math.sqrt(dX * dX + dZ * dZ) > MaxSlope) continue;

                    // Reachability check — patch centre must sit in the
                    // connected region every player can reach so no patch
                    // ends up walled off from any starting hall.
                    var passGrid = PassabilityGrid.Instance;
                    if (passGrid != null && !passGrid.IsReachableByAllPlayersForRadius(candidate, ScatteredPatchSpread + 1f))
                        continue;
                }

                bool tooCloseToPlayer = false;
                for (int p = 0; p < playerPositions.Length; p++)
                {
                    if (math.distance(candidate, playerPositions[p]) < ScatteredMinDistFromPlayer)
                    { tooCloseToPlayer = true; break; }
                }
                if (tooCloseToPlayer) continue;

                bool tooCloseToPatch = false;
                for (int o = 0; o < patchCenters.Length; o++)
                {
                    if (math.distance(candidate, patchCenters[o]) < MinDistBetweenPatchCenters)
                    { tooCloseToPatch = true; break; }
                }
                if (tooCloseToPatch) continue;

                result = candidate;
                return true;
            }

            return false;
        }

        private static float3[] GetPlayerPositions(EntityManager em)
        {
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            using var hallTransforms = hallQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);

            if (hallTransforms.Length > 0)
            {
                var positions = new float3[hallTransforms.Length];
                for (int i = 0; i < hallTransforms.Length; i++)
                    positions[i] = hallTransforms[i].Position;
                return positions;
            }

            int playerCount = GameSettings.TotalPlayers;
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.5f;
            var fallback = new float3[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                float angle = (i / (float)playerCount) * math.PI * 2f;
                fallback[i] = new float3(
                    math.cos(angle) * spawnRadius, 0f, math.sin(angle) * spawnRadius);
            }
            return fallback;
        }
    }
}
