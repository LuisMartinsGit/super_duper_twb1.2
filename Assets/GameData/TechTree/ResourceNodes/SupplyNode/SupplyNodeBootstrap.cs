// SupplyNodeBootstrap.cs
// Spawns the map's supply nodes from SupplyNodeMarker scene markers, then
// tops every territory up to its quota.
//
// A supply node is the ONLY place a Gatherer's Hut can stand, and since the
// node-quota rule it also scales the territory's base supply tick
// (docs/Design/Regions.md §4) — so the count is a balance guarantee, not a
// cosmetic one: EVERY territory carries 2 supply nodes, and a HOME territory
// (one holding a player start) carries 4. Authored markers are honoured
// first and only the shortfall is seeded, the same contract the veilsteel
// coverage pass established, so a hand-built map keeps its composition and
// an unauthored map is still playable.

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public static class SupplyNodeBootstrap
    {
        /// <summary>
        /// Supply nodes every territory is guaranteed. This is the number of
        /// Gatherer's Huts it will support AND its base-tick multiplier, so it
        /// is a balance figure, not a cosmetic one (Regions.md §4 node quotas).
        /// </summary>
        public const int NodesPerTerritory = 2;

        /// <summary>Supply nodes a HOME territory is guaranteed — the
        /// "large territory where a player spawns" half of the quota rule.</summary>
        public const int NodesPerHomeTerritory = 4;

        /// <summary>How far from its seed a topped-up node is scattered.</summary>
        private const float TopUpSpread = 26f;

        /// <summary>Nodes closer than this to an existing supply node are
        /// rejected: the hut gate snaps within 4 m, so two nodes nearly on top
        /// of each other read as one buildable spot with a phantom twin.</summary>
        private const float MinNodeSeparation = 10f;

        public static void SpawnSupplyNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            int authored = 0;
            var markers = MapMarkerRegistry.SupplyNodes;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                var p = m.WorldPosition;
                // Snap Y at spawn. MapMarker.SnapToTerrain only moves the
                // GIZMO — WorldPosition is the raw transform — so a marker
                // authored at Y=0 (which is how they are seeded) would put
                // its node under the ground.
                float y = TerrainUtility.GetHeight(p.x, p.z);
                SupplyNode.Create(em, new float3(p.x, y, p.z));
                authored++;
            }

            int topped = TopUpTerritories(em);
            TWBLog.Log($"[SupplyNodeBootstrap] {authored} authored supply node(s), " +
                       $"{topped} topped up across {RegionMap.Count} territories.");
        }

        /// <summary>
        /// Bring every territory up to its quota: <see cref="NodesPerTerritory"/>,
        /// or <see cref="NodesPerHomeTerritory"/> where a player start sits.
        /// Deterministic — regions walked in index order, bearings derived from
        /// the region index — so every lockstep peer lays out the identical map.
        /// A top-up, never a trim: an authored surplus is a map author's choice.
        /// </summary>
        private static int TopUpTerritories(EntityManager em)
        {
            if (!RegionMap.Ready || RegionMap.Count == 0) return 0;

            // What each territory already has, and where, so the top-up can
            // keep its distance from authored nodes.
            var have = new List<float2>[RegionMap.Count];
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SupplyNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    var p = xfs[i].Position;
                    int r = RegionMap.RegionAt(p.x, p.z);
                    if (r == RegionMap.None) continue;
                    (have[r] ??= new List<float2>()).Add(new float2(p.x, p.z));
                }
            q.Dispose();

            var homes = HomeRegions(em);

            int placed = 0;
            for (int r = 0; r < RegionMap.Count; r++)
            {
                int want = homes.Contains(r) ? NodesPerHomeTerritory : NodesPerTerritory;
                var existing = have[r] ??= new List<float2>();
                if (existing.Count >= want) continue;

                Vector2 seed = RegionMap.SeedOf(r);
                // More attempts than the shortfall: a bearing can fail on
                // water, a border sliver or a crowded spot, and quota is the
                // point of the pass.
                int attempts = want * 4;
                for (int attempt = 0; attempt < attempts && existing.Count < want; attempt++)
                {
                    // Deterministic bearing: spread evenly over ALL attempts
                    // (divide by the attempt count, not the quota, or the
                    // retries land on the exact bearings that just failed),
                    // offset per region so neighbouring territories do not
                    // line their nodes up.
                    float angle = (attempt / (float)attempts + r * 0.37f) * Mathf.PI * 2f;
                    float x = seed.x + Mathf.Cos(angle) * TopUpSpread;
                    float z = seed.y + Mathf.Sin(angle) * TopUpSpread;

                    // Walk toward the seed until the partition agrees — the
                    // boundary is domain-warped, so raw ring arithmetic can
                    // land a node next door, where it would hand its huts and
                    // its base-tick share to the neighbour. The seed itself is
                    // inside its own region by definition.
                    if (!TrySeatInRegion(r, ref x, ref z)) continue;

                    bool crowded = false;
                    for (int i = 0; i < existing.Count && !crowded; i++)
                    {
                        float dx = existing[i].x - x, dz = existing[i].y - z;
                        crowded = dx * dx + dz * dz
                                  < MinNodeSeparation * MinNodeSeparation;
                    }
                    if (crowded) continue;

                    float y = TerrainUtility.GetHeight(x, z);
                    SupplyNode.Create(em, new float3(x, y, z));
                    existing.Add(new float2(x, z));
                    placed++;
                }

                if (existing.Count < want)
                    Debug.LogWarning($"[SupplyNodeBootstrap] territory {r} " +
                                     $"({RegionMap.NameOf(r)}) holds {existing.Count}/{want} " +
                                     "supply nodes — no seatable ground for the rest.");
            }
            return placed;
        }

        /// <summary>Step a candidate toward its region's seed until it sits
        /// inside the region on standable ground. False when even the walk
        /// finds nothing usable.</summary>
        private static bool TrySeatInRegion(int region, ref float x, ref float z)
        {
            Vector2 seed = RegionMap.SeedOf(region);
            float startX = x, startZ = z;
            var grid = PassabilityGrid.Instance;

            for (int step = 0; step <= 12; step++)
            {
                float t = step / 12f;
                float px = Mathf.Lerp(startX, seed.x, t);
                float pz = Mathf.Lerp(startZ, seed.y, t);
                if (RegionMap.RegionAt(px, pz) != region) continue;
                if (grid != null)
                {
                    // Same passability idiom the veilsteel top-up uses, so the
                    // two passes agree about what counts as standable ground.
                    var cell = grid.WorldToCell(new float3(px, 0f, pz));
                    if (cell.x < 0 || cell.x >= grid.Width
                        || cell.y < 0 || cell.y >= grid.Height) continue;
                    if (grid.GetCell(cell) != PassabilityGrid.Passable) continue;
                }
                x = px;
                z = pz;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Territories that hold a player start — the "large" territories of
        /// the quota rule. Marker-driven (map data, identical for every peer
        /// and every match size); on a markerless map the spawned Halls are
        /// the same fact by other means, since this runs after faction spawn.
        /// </summary>
        private static HashSet<int> HomeRegions(EntityManager em)
        {
            var homes = new HashSet<int>();

            var starts = MapMarkerRegistry.PlayerStarts;
            if (starts.Count > 0)
            {
                for (int i = 0; i < starts.Count; i++)
                {
                    if (starts[i] == null) continue;
                    var p = starts[i].transform.position;
                    int r = RegionMap.RegionAt(p.x, p.z);
                    if (r == RegionMap.None) r = RegionMap.NearestRegion(p.x, p.z);
                    if (r != RegionMap.None) homes.Add(r);
                }
                return homes;
            }

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    var p = xfs[i].Position;
                    int r = RegionMap.NearestRegion(p.x, p.z);
                    if (r != RegionMap.None) homes.Add(r);
                }
            q.Dispose();
            return homes;
        }
    }
}
