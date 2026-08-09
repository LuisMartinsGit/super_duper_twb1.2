// File: Assets/Scripts/Bootstrap/VeilstoneOutcroppingBootstrap.cs
// Spawns mineable veilstone outcroppings as patches from VeilstoneOutcroppingMarker
// components placed in the hand-authored map scene, so AI / players have a
// starting veilstone source without having to fight Crystallings first.

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
    /// Spawns outcropping-based veilstone patches at game start from scene markers.
    /// Each patch = a cluster of outcroppings with veilstone in them, mineable by
    /// Miners via GatherCommand (VeilstoneMiningSystem handles the gathering
    /// loop). Independent of BorderNodeBootstrap (which spawns the border main
    /// nodes that grow Crystallings).
    /// </summary>
    public static class VeilstoneOutcroppingBootstrap
    {
        // Clear radius (+ margin) used to free forest / rock obstacles around a
        // spawned patch so units can reach every node.
        private const float PatchClearRadius = 7f;

        public static void SpawnVeilstoneOutcroppings()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xCEED));

            // §2.5b rev.3: MAP PATCHES are the mining base (mining them rolls
            // corruption — that's the curse interaction); precipitation and
            // pocket residue layer on top. Markerless skirmish maps MUST
            // self-provision starter patches — removing the fallback
            // (2026-08-03) left zero veilstone sources on such maps, and the
            // 70-veilstone choice buildings are non-skippable build-order
            // gates: every AI froze with banked iron and no progress
            // (2026-08-04 playtest). Authored markers still take precedence.
            if (MapMarkerRegistry.HasVeilstoneMarkers)
            {
                SpawnVeilstoneFromMarkers(em, ref random);
            }
            else
            {
                TWBLog.Log("[VeilstoneOutcroppingBootstrap] no VeilstoneOutcroppingMarker — " +
                           "spawning fallback patches near bases + scattered (markerless map).");
                SpawnFallbackPatches(em, ref random);
            }
        }

        /// <summary>
        /// Spawn one veilstone patch per VeilstoneOutcroppingMarker in the scene,
        /// honouring its NodeCount / VeilstonePerNode / Spread / Layout. Clears
        /// forest macro-cell obstacles around the patch so units can reach
        /// every node.
        /// </summary>
        private static void SpawnVeilstoneFromMarkers(EntityManager em, ref Unity.Mathematics.Random random)
        {
            var markers = MapMarkerRegistry.VeilstoneOutcroppings;
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
                    SpawnVeilstoneOutcroppingHex(em, center, m.NodeCount, m.VeilstonePerNode, m.Spread, ref random);
                else
                    SpawnVeilstoneOutcroppingRandom(em, center, m.NodeCount, m.VeilstonePerNode, m.Spread, ref random);

                centers.Add(center);
            }

            ClearObstaclesAroundPoints(em, centers, PatchClearRadius + 2f);
            centers.Dispose();
        }

        /// <summary>Marker-driven hex-grid spawn.</summary>
        private static void SpawnVeilstoneOutcroppingHex(EntityManager em, float3 center,
            int nodeCount, int veilstonePerNode, float spread, ref Unity.Mathematics.Random random)
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
                VeilstoneOutcropping.Create(em, new float3(x, y, z), veilstonePerNode);
            }

            slots.Dispose();
        }

        /// <summary>
        /// Markerless fallback (2026-08-03): one patch near every faction
        /// Hall plus a handful scattered across the playable area, mirroring
        /// how iron is always available. Same node factory / gathering logic.
        /// </summary>
        private static void SpawnFallbackPatches(EntityManager em, ref Unity.Mathematics.Random random)
        {
            const int NodesPerPatch = 5;
            const int VeilstonePerNode = 200;
            const float PatchSpread = 5f;
            const int ScatteredPatches = 6;

            var centers = new Unity.Collections.NativeList<float3>(16, Unity.Collections.Allocator.Temp);

            // Near-base patches: one per Hall, 22-30m out at a random bearing.
            var hallQuery = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<HallTag>(),
                Unity.Entities.ComponentType.ReadOnly<LocalTransform>());
            using (var halls = hallQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < halls.Length; i++)
                {
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float dist = random.NextFloat(22f, 30f);
                    float x = halls[i].Position.x + math.cos(angle) * dist;
                    float z = halls[i].Position.z + math.sin(angle) * dist;
                    TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
                    x = math.clamp(x, bMin.x + 10f, bMax.x - 10f);
                    z = math.clamp(z, bMin.y + 10f, bMax.y - 10f);
                    centers.Add(new float3(x, TerrainUtility.GetHeight(x, z), z));
                }
            }

            // Scattered patches across the playable area.
            {
                TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
                for (int i = 0; i < ScatteredPatches; i++)
                {
                    float x = random.NextFloat(bMin.x + 15f, bMax.x - 15f);
                    float z = random.NextFloat(bMin.y + 15f, bMax.y - 15f);
                    centers.Add(new float3(x, TerrainUtility.GetHeight(x, z), z));
                }
            }

            ClearObstaclesAroundPoints(em, centers, PatchClearRadius + 2f);
            for (int i = 0; i < centers.Length; i++)
                SpawnVeilstoneOutcroppingRandom(em, centers[i],
                    NodesPerPatch, VeilstonePerNode, PatchSpread, ref random);

            TWBLog.Log($"[VeilstoneOutcroppingBootstrap] fallback: {centers.Length} patches " +
                       $"({NodesPerPatch} nodes x {VeilstonePerNode} veilstone).");
            centers.Dispose();
        }

        /// <summary>Marker-driven random-cluster spawn.</summary>
        private static void SpawnVeilstoneOutcroppingRandom(EntityManager em, float3 center,
            int nodeCount, int veilstonePerNode, float spread, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, spread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                VeilstoneOutcropping.CreateOrMerge(em, new float3(x, y, z), veilstonePerNode);
            }
        }

        // Destroy ObstacleTag entities (forest macro cells, rocks) and
        // unblock the matching passability cells anywhere within
        // <paramref name="clearRadius"/> of <paramref name="points"/>.
        //
        // IMPORTANT: this excludes resource deposits — VeilstoneOutcroppingTag (the
        // veilstone nodes we just spawned, which now carry ObstacleTag so
        // they carve the navmesh) and IronMineTag (iron deposits, ditto).
        // Without that filter the bootstrap deleted its own freshly-spawned
        // veilstone on the line after creating them, and could clip iron
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
                        Unity.Entities.ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
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
    }
}
