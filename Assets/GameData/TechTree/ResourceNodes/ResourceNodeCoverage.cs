// ResourceNodeCoverage.cs
// The ore half of the node-quota rule (docs/Design/Regions.md §4):
// EVERY territory carries at least one ore node — iron, veilstone or
// veilsteel — and never more than four; every home carries veilstone, and
// half the map does.
//
// AUTHORED BY HAND (2026-09-11). Resource nodes are placed by the map
// author, and only by the map author. This pass used to seed whatever a
// territory was short of, from its seed outward — which is how Hollow
// Table's central territory grew an iron node on top of its well and two
// supply nodes around it that nobody placed. It now AUDITS: every shortfall
// is logged, naming the territory, so the author sees exactly what the
// quota wants and decides. Nothing is spawned.
//
// Set-level on purpose: it counts across all three node kinds, so it lives
// one level above the per-node folders. Runs after the three ore bootstraps
// and before the supply pass (SpawnDelayHelper), so every authored marker
// is already on the ground when it counts.
//
// The only runtime seeding left in the game is the fallback for maps that
// author NO resource markers at all (procedural fixtures, scenario stubs) —
// see the per-node bootstraps' SpawnFallbackPatches. An authored map gets
// what its author placed.

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Bootstrap
{
    public static class ResourceNodeCoverage
    {
        /// <summary>Ore nodes every territory is guaranteed.</summary>
        public const int MinOreNodesPerTerritory = 1;

        /// <summary>Ore nodes a territory may carry at most.</summary>
        public const int MaxOreNodesPerTerritory = 4;

        /// <summary>Share of all territories that should carry veilstone
        /// (Regions.md §3).</summary>
        private const float VeilstoneCoverageFraction = 0.5f;

        /// <summary>
        /// True when the map author placed resource markers of any kind. Such
        /// a map gets exactly what was placed; only a map with none at all is
        /// filled by the procedural fallbacks.
        /// </summary>
        public static bool MapAuthorsResources =>
            MapMarkerRegistry.HasIronMarkers
            || MapMarkerRegistry.HasVeilstoneMarkers
            || MapMarkerRegistry.HasVeilsteelMarkers
            || MapMarkerRegistry.HasSupplyNodes;

        /// <summary>
        /// How close to a well's centre a runtime-seeded node may not stand.
        /// The well's own build footprint is 12x12 cells and it spreads haze
        /// this far within the first minute; a node inside that ring is
        /// unreachable ground dressed as an economy, which is how Hollow
        /// Table's central territory grew an iron node ON its well. Read from
        /// the curse's own constant so the two cannot drift apart.
        /// </summary>
        private static float WellClearRadius =>
            TheWaningBorder.Core.Config.BorderConstants.MainNodeSpreadRadius;

        /// <summary>
        /// Does this territory hold an authored well? Such a territory is
        /// the map author's to fill: the fallback passes leave it alone
        /// rather than ringing the well with ore nobody placed.
        /// </summary>
        public static bool IsWellTerritory(int territory)
        {
            if (territory == RegionMap.None || !RegionMap.Ready) return false;
            var wells = MapMarkerRegistry.BorderNodes;
            for (int i = 0; i < wells.Count; i++)
            {
                var w = wells[i];
                if (w == null) continue;
                var p = w.transform.position;
                if (RegionMap.RegionAt(p.x, p.z) == territory) return true;
            }
            return false;
        }

        /// <summary>Is this world point inside a well's footprint + haze
        /// ring? Same question as <see cref="IsWellTerritory"/> asks of a
        /// whole territory, for the ring search that places one node.</summary>
        public static bool OnWellFootprint(float x, float z)
        {
            var wells = MapMarkerRegistry.BorderNodes;
            for (int i = 0; i < wells.Count; i++)
            {
                var w = wells[i];
                if (w == null) continue;
                var p = w.transform.position;
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz <= WellClearRadius * WellClearRadius) return true;
            }
            return false;
        }

        /// <summary>
        /// Audit the one-ore-per-territory rule. Logs every territory below
        /// the minimum and every one above the maximum; spawns nothing.
        /// </summary>
        public static void GuaranteeTerritoryOre()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!RegionMap.Ready || RegionMap.Count == 0) return;

            var counts = OreNodeCounts(em);
            int under = 0, over = 0;
            for (int r = 0; r < counts.Length; r++)
            {
                if (!ClaimableTerritory(r)) continue;
                if (counts[r] < MinOreNodesPerTerritory)
                {
                    under++;
                    Debug.LogWarning($"[ResourceNodeCoverage] territory {r} ({RegionMap.NameOf(r)}) " +
                                     "has no ore node — the quota wants at least one (Regions.md §4). " +
                                     "Author one; nothing is seeded at runtime.");
                }
                else if (counts[r] > MaxOreNodesPerTerritory)
                {
                    over++;
                    Debug.LogWarning($"[ResourceNodeCoverage] territory {r} ({RegionMap.NameOf(r)}) " +
                                     $"carries {counts[r]} ore nodes — the quota caps at {MaxOreNodesPerTerritory}.");
                }
            }
            Debug.Log($"[ResourceNodeCoverage] ore audit: {counts.Length} territories, " +
                      $"{under} under quota, {over} over cap (authored only, nothing seeded).");
        }

        /// <summary>
        /// Audit the veilstone rule: every home territory carries veilstone
        /// and half the map does. Logs shortfalls; spawns nothing.
        /// </summary>
        public static void GuaranteeVeilstoneCoverage()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!RegionMap.Ready || RegionMap.Count == 0) return;

            var veilstone = new int[RegionMap.Count];
            Accumulate<VeilstoneOutcroppingTag>(em, veilstone);

            var homes = new List<int>();
            var hallQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = hallQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    int r = RegionMap.NearestRegion(xfs[i].Position.x, xfs[i].Position.z);
                    if (r != RegionMap.None && !homes.Contains(r)) homes.Add(r);
                }
            hallQ.Dispose();
            homes.Sort();

            int homesMissing = 0;
            for (int i = 0; i < homes.Count; i++)
            {
                int r = homes[i];
                if (veilstone[r] > 0) continue;
                homesMissing++;
                Debug.LogWarning($"[ResourceNodeCoverage] HOME territory {r} ({RegionMap.NameOf(r)}) " +
                                 "carries no veilstone — every home must (Regions.md §3). Author one.");
            }

            int have = 0;
            for (int r = 0; r < veilstone.Length; r++) if (veilstone[r] > 0) have++;
            int want = Mathf.CeilToInt(RegionMap.Count * VeilstoneCoverageFraction);
            if (have < want)
                Debug.LogWarning($"[ResourceNodeCoverage] veilstone covers {have}/{RegionMap.Count} " +
                                 $"territories; the rule wants {want}. Author more, or accept it.");

            Debug.Log($"[ResourceNodeCoverage] veilstone coverage: {have}/{RegionMap.Count} " +
                      $"territories carry veilstone (target {want}), {homes.Count} home(s), " +
                      $"{homesMissing} without (authored only, nothing seeded).");
        }

        /// <summary>Ore nodes per region, all three kinds together.</summary>
        public static int[] OreNodeCounts(EntityManager em)
        {
            var counts = new int[RegionMap.Count];
            Accumulate<IronMineTag>(em, counts);
            Accumulate<VeilstoneOutcroppingTag>(em, counts);
            Accumulate<VeilsteelDepositTag>(em, counts);
            return counts;
        }

        private static void Accumulate<TNode>(EntityManager em, int[] counts)
            where TNode : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TNode>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    int r = RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z);
                    if (r >= 0 && r < counts.Length) counts[r]++;
                }
            q.Dispose();
        }

        /// <summary>Water, mountain and obstacle regions hold nothing and are
        /// not audited.</summary>
        private static bool ClaimableTerritory(int region) =>
            !RegionMap.KindBlocks(RegionMap.KindOf(region));
    }
}
