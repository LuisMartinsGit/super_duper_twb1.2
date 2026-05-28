// File: Assets/Scripts/Bootstrap/IronDepositBootstrap.cs
// Spawns iron ore as patches (clusters) instead of single scattered deposits.
// Each player gets one patch close to their Hall plus several patches scattered
// across the map. Replaces the earlier "scatter N deposits with min-distance
// from players" loop, which formed an obvious ring around each spawn.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns iron deposits in patches. Each patch = a tight cluster of N
    /// deposits players can mine without ferrying miners across the map.
    ///
    /// Layout per player:
    /// - 1 NEAR patch close to the Hall (within NearPatchMinDist..MaxDist)
    /// - <see cref="ScatteredPatchesPerPlayer"/> patches scattered across the
    ///   map, with min-distance gates so patches don't overlap and stay out of
    ///   spawn footprints.
    /// </summary>
    public static class IronDepositBootstrap
    {
        // Presentation ID (must match PresentationSpawnSystem)
        public const int IronDepositPresentationId = 402;

        // Per-deposit settings. Iron-per-deposit was dropped 500 → 50 to match
        // the new "many small nodes per patch" model (mirrors crystal's
        // 200 → 30 shift). Total per NEAR patch is now 30 × 50 = 1500.
        private const float DepositRadius = 1.5f;
        private const int IronPerDeposit = 50;

        // Patch settings — near and scattered now diverge:
        //   NEAR  : 30 deposits on a hex grid with jitter (dense field).
        //   SCAT. : 3 deposits in a tight random cluster (mini-deposit).
        private const int NearPatchDeposits = 30;
        private const int ScatteredDepositsPerPatch = 3;
        private const float NearPatchSpread = 7f;          // hex-grid disc, 3 rings
        private const float ScatteredPatchSpread = 4f;     // random tight cluster

        // NEAR patch (one per player)
        private const float NearPatchMinDist = 22f;        // outside Hall footprint (~20u)
        private const float NearPatchMaxDist = 32f;        // close enough to mine without long walks

        // SCATTERED patches. Bumped 4 → 7 — players reported the map felt
        // iron-scarce and wanted more deposits scattered around.
        private const int ScatteredPatchesPerPlayer = 7;
        private const float ScatteredMinDistFromPlayer = 50f;
        private const float MinDistBetweenPatchCenters = 24f;

        // Heightmap constraints (only enforced when NOT FlatTestMap)
        private const float MinHeight = 23f;               // above shoreline
        private const float MaxHeight = 85f;
        private const float MaxSlope = 0.6f;               // not on cliffs

        public static void SpawnIronDeposits()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xDEAD));

            // Hand-authored maps: IronPatchMarkers in the scene replace the
            // procedural near + scattered placement entirely. We still seed
            // RNG from SpawnSeed so the jitter / shuffle inside each patch
            // is deterministic across re-loads.
            if (MapMarkerRegistry.HasIronMarkers)
            {
                SpawnIronFromMarkers(em, ref random);
                return;
            }

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.85f;

            // Track placed patch centers so scattered patches don't overlap them
            // (or each other) and form visible clusters across the map.
            var patchCenters = new Unity.Collections.NativeList<float3>(
                playerPositions.Length * (1 + ScatteredPatchesPerPlayer),
                Unity.Collections.Allocator.Temp);

            // 1. NEAR patches — one per player. Always succeed (we picked the
            //    direction from the player ourselves; no validation step).
            for (int p = 0; p < playerPositions.Length; p++)
            {
                float3 center = PickNearPatchCenter(playerPositions[p], ref random);
                SpawnNearIronPatch(em, center, ref random);
                patchCenters.Add(center);
            }

            // 2. SCATTERED patches — N per player, gated by distance and terrain.
            int scatteredCount = playerPositions.Length * ScatteredPatchesPerPlayer;
            for (int i = 0; i < scatteredCount; i++)
            {
                if (TryFindScatteredPatchCenter(ref random, spawnRange,
                        playerPositions, patchCenters, out float3 center))
                {
                    SpawnScatteredIronPatch(em, center, ref random);
                    patchCenters.Add(center);
                }
            }

            patchCenters.Dispose();
        }

        /// <summary>
        /// Marker-driven path: spawn one iron patch per IronPatchMarker in
        /// the scene, honouring its DepositCount, Spread, and Layout. Skips
        /// the player-distance / slope / reachability gates the procedural
        /// path enforces — when you place a marker, you're telling us "spawn
        /// it here, full stop."
        /// </summary>
        private static void SpawnIronFromMarkers(EntityManager em, ref Unity.Mathematics.Random random)
        {
            var markers = MapMarkerRegistry.IronPatches;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                var p = m.WorldPosition;
                float y = TerrainUtility.GetHeight(p.x, p.z);
                float3 center = new float3(p.x, y, p.z);

                if (m.Layout == PatchLayout.HexGrid)
                    SpawnIronPatchHex(em, center, m.DepositCount, m.Spread, ref random);
                else
                    SpawnIronPatchRandom(em, center, m.DepositCount, m.Spread, ref random);
            }
        }

        /// <summary>
        /// Parameterised hex-grid spawn for marker-driven patches. Mirrors
        /// <see cref="SpawnNearIronPatch"/> but takes deposit count + spread
        /// from the marker instead of the file-level NEAR constants.
        /// </summary>
        private static void SpawnIronPatchHex(EntityManager em, float3 center,
            int depositCount, float spread, ref Unity.Mathematics.Random random)
        {
            // Pick a ring count large enough to fit depositCount with some
            // shuffle headroom. 1+6+12+18+24 = 61 slots at 4 rings, plenty
            // for typical patch sizes.
            int rings = depositCount <= 7  ? 1 :
                        depositCount <= 19 ? 2 :
                        depositCount <= 37 ? 3 : 4;
            float spacing = spread / Mathf.Max(1, rings);
            float jitter  = spacing * 0.30f;

            var slots = new Unity.Collections.NativeList<float2>(64, Unity.Collections.Allocator.Temp);
            GenerateHexSlots(rings, spacing, slots);

            // Fisher-Yates shuffle.
            for (int i = slots.Length - 1; i > 0; i--)
            {
                int j = random.NextInt(0, i + 1);
                float2 tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
            }

            int placeCount = math.min(depositCount, slots.Length);
            for (int i = 0; i < placeCount; i++)
            {
                float2 slot = slots[i];
                float jx = random.NextFloat(-jitter, jitter);
                float jz = random.NextFloat(-jitter, jitter);
                float x = center.x + slot.x + jx;
                float z = center.z + slot.y + jz;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }

            slots.Dispose();
        }

        /// <summary>
        /// Parameterised random-cluster spawn for marker-driven patches.
        /// Mirrors <see cref="SpawnScatteredIronPatch"/>.
        /// </summary>
        private static void SpawnIronPatchRandom(EntityManager em, float3 center,
            int depositCount, float spread, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < depositCount; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, spread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }
        }

        // Axial-coordinate neighbour directions for hex-grid traversal.
        // Walked in this order they trace each ring once around the centre.
        // Same algorithm CrystalPatchBootstrap uses for its near patch.
        private static readonly int[,] HexDirs = new int[,]
        {
            {  1,  0 }, {  1, -1 }, {  0, -1 },
            { -1,  0 }, { -1,  1 }, {  0,  1 }
        };

        /// <summary>
        /// Near-patch spawn — 30 deposits placed on a hex grid with per-cell
        /// jitter, so the patch is evenly packed but doesn't read as a
        /// uniform hexagonal field. Mirrors CrystalPatchBootstrap.SpawnNearPatch.
        /// </summary>
        private static void SpawnNearIronPatch(EntityManager em, float3 center, ref Unity.Mathematics.Random random)
        {
            // 3 rings = 1 + 6 + 12 + 18 = 37 candidate slots. We take the
            // first NearPatchDeposits after shuffling, giving the patch a
            // few natural "holes" plus a randomised silhouette.
            const int Rings = 3;
            float spacing = NearPatchSpread / Rings;
            float jitter  = spacing * 0.30f;

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

            int placeCount = math.min(NearPatchDeposits, slots.Length);
            for (int i = 0; i < placeCount; i++)
            {
                float2 slot = slots[i];
                float jx = random.NextFloat(-jitter, jitter);
                float jz = random.NextFloat(-jitter, jitter);
                float x = center.x + slot.x + jx;
                float z = center.z + slot.y + jz;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }

            slots.Dispose();
        }

        /// <summary>
        /// Scattered-patch spawn — small random cluster of deposits within
        /// ScatteredPatchSpread of the centre. Unchanged from the previous
        /// "tight cluster" behaviour, only the per-deposit yield has dropped.
        /// </summary>
        private static void SpawnScatteredIronPatch(EntityManager em, float3 center, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < ScatteredDepositsPerPatch; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, ScatteredPatchSpread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }
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
        /// Pick a random direction from <paramref name="player"/> at a random
        /// distance in [NearPatchMinDist, NearPatchMaxDist]. Retries up to 16
        /// times if the candidate lands on terrain the passability layer
        /// considers blocked (mountains can now appear close to the spawn ring
        /// on small maps) — falls back to the last sample if every attempt
        /// fails so we never skip a player's near patch.
        /// </summary>
        private static float3 PickNearPatchCenter(float3 player, ref Unity.Mathematics.Random random)
        {
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

        /// <summary>
        /// Pick a random position on the map for a scattered patch, ensuring
        /// minimum distance from all players and from already-placed patches.
        /// On non-flat maps, also rejects water and out-of-band heights.
        /// </summary>
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

                    // Slope check (skip cliffs).
                    float step = 2f;
                    float hL = TerrainUtility.GetHeight(x - step, z);
                    float hR = TerrainUtility.GetHeight(x + step, z);
                    float hD = TerrainUtility.GetHeight(x, z - step);
                    float hU = TerrainUtility.GetHeight(x, z + step);
                    float dX = (hR - hL) / (step * 2f);
                    float dZ = (hU - hD) / (step * 2f);
                    if (math.sqrt(dX * dX + dZ * dZ) > MaxSlope) continue;

                    // Reachability check — deposit must sit in the connected
                    // region every player can reach (rejects mountain pockets,
                    // cliff tops, islands the hall is fenced off from).
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

        private static Entity CreateIronDepositEntity(EntityManager em, float3 position)
        {
            var entity = em.CreateEntity(
                typeof(IronMineTag),
                // ObstacleTag — units route around the deposit on the
                // passability grid AND the deposit's footprint is fed into
                // NavMeshManager so pathfinding can't try to clip through it.
                typeof(ObstacleTag),
                typeof(IronDepositState),
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            // Visual is scaled +30 % from the prefab, so use the larger
            // collision radius (1.5 m × 1.3) for both passability and navmesh.
            em.SetComponentData(entity, new Radius { Value = DepositRadius * 1.3f });
            em.SetComponentData(entity, new PresentationId { Id = IronDepositPresentationId });
            em.SetComponentData(entity, new IronDepositState
            {
                RemainingIron = IronPerDeposit,
                InitialIron = IronPerDeposit,
                Depleted = 0
            });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Block the passability grid cells under the deposit footprint
            // so flow-fields and movement steer around it. NavMeshManager
            // picks up the ObstacleTag entity in its SyncBuildings pass and
            // carves a matching box source out of the navmesh.
            var grid = PassabilityGrid.Instance;
            if (grid != null)
                grid.BlockObstacle(position, DepositRadius * 1.3f);

            return entity;
        }

        /// <summary>
        /// Get player positions from existing Halls, or estimate from spawn layout.
        /// </summary>
        private static float3[] GetPlayerPositions(EntityManager em)
        {
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            if (hallTransforms.Length > 0)
            {
                var positions = new float3[hallTransforms.Length];
                for (int i = 0; i < hallTransforms.Length; i++)
                    positions[i] = hallTransforms[i].Position;
                return positions;
            }

            // Fallback: estimate from player count
            int playerCount = GameSettings.TotalPlayers;
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.5f;
            var fallback = new float3[playerCount];

            for (int i = 0; i < playerCount; i++)
            {
                float angle = (i / (float)playerCount) * math.PI * 2f;
                fallback[i] = new float3(
                    math.cos(angle) * spawnRadius,
                    0f,
                    math.sin(angle) * spawnRadius
                );
            }

            return fallback;
        }
    }
}
