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

            // Ore-bearing GROUND is painted per patch (its own terrain layer,
            // never the curse's). Reset first so a reloaded match doesn't
            // inherit the previous map's seams.
            VeilstonePatchGround.Clear();

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

        /// <summary>
        /// Spawn a patch as a SOLID BLOCK of build cells — no gaps. Geometry
        /// (and the reasoning behind it) lives in the shared
        /// <see cref="ResourcePatchFill"/>; this only creates the nodes and
        /// registers the ground the patch covers for the terrain painter.
        /// </summary>
        private static void SpawnVeilstoneOutcroppingPatch(EntityManager em, float3 center,
            int nodeCount, int veilstonePerNode, float spread, bool raggedEdge,
            ref Unity.Mathematics.Random random)
        {
            if (nodeCount <= 0) return;

            // ONE node per marker, holding what the whole patch used to
            // (docs/Design/Regions.md §4, "Nodes, not patches"). The marker's
            // node count and spread now read as "how much" and "how big"
            // rather than "how many" and "how far apart" — nothing stands on a
            // deposit any more, so the scatter bought nothing and cost a
            // hundred-odd entities and blocked cells per patch.
            VeilstoneOutcropping.Create(em, center, nodeCount * veilstonePerNode);

            // The painted ground is the node's own footprint now rather than a
            // block of cells, so 1 keeps Register's units honest.
            VeilstonePatchGround.Register(center, 1);
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

        /// <summary>Marker-driven random-cluster spawn — now a gapless block
        /// with a seed-varied outer edge. The old free scatter used
        /// CreateOrMerge, so overlapping picks silently merged into fewer,
        /// fatter nodes; the block places exactly one node per cell.</summary>
        private static void SpawnVeilstoneOutcroppingRandom(EntityManager em, float3 center,
            int nodeCount, int veilstonePerNode, float spread, ref Unity.Mathematics.Random random)
            => SpawnVeilstoneOutcroppingPatch(em, center, nodeCount, veilstonePerNode,
                spread, raggedEdge: true, ref random);

        /// <summary>Marker-driven hex-grid spawn — gapless block, fixed
        /// (most symmetric) cell order.</summary>
        private static void SpawnVeilstoneOutcroppingHex(EntityManager em, float3 center,
            int nodeCount, int veilstonePerNode, float spread, ref Unity.Mathematics.Random random)
            => SpawnVeilstoneOutcroppingPatch(em, center, nodeCount, veilstonePerNode,
                spread, raggedEdge: false, ref random);

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

    }
}
