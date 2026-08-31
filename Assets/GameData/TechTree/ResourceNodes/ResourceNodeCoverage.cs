// ResourceNodeCoverage.cs
// The ore half of the node-quota rule (docs/Design/Regions.md §4):
// EVERY territory carries at least one ore node — iron, veilstone or
// veilsteel — and never more than four.
//
// Set-level on purpose: it counts across all three node kinds, so it lives
// one level above the per-node folders, next to ResourcePatchFill. Runs
// after the three ore bootstraps and before the supply pass
// (SpawnDelayHelper), so authored markers, fallbacks and the veilsteel
// coverage top-up are all already on the ground and only a genuine
// shortfall is filled.
//
// The generic min-ore top-up is IRON. VEILSTONE has its own coverage rule
// now (Regions.md §3, 2026-08-31 — it superseded the old centre-ring
// exclusivity): every starter territory MUST carry a veilstone outcropping,
// and 50% of ALL territories carry one, because veilstone is both the army
// economy and the curse's food — the curse only conquers veilstone ground.
// GuaranteeVeilstoneCoverage runs before the iron pass so a region that just
// gained veilstone no longer needs the iron fallback.

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public static class ResourceNodeCoverage
    {
        /// <summary>Ore nodes every territory is guaranteed.</summary>
        public const int MinOreNodesPerTerritory = 1;

        /// <summary>Ore nodes a territory may carry at most. An authored map
        /// is trusted (a trim would fight its author); the runtime top-up
        /// passes stop at this line instead.</summary>
        public const int MaxOreNodesPerTerritory = 4;

        /// <summary>Payload of a topped-up iron node, in marker DepositCount
        /// units — matches the 24 the generators author for a small
        /// territory's iron (24 x 50 = 1,200 iron).</summary>
        private const int TopUpDepositCount = 24;

        /// <summary>
        /// Give every territory its guaranteed ore node. Deterministic:
        /// regions walked in index order, candidates ringed around the seed,
        /// so every lockstep peer fills the identical shortfall.
        /// </summary>
        public static void GuaranteeTerritoryOre()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!RegionMap.Ready || RegionMap.Count == 0) return;

            var counts = OreNodeCounts(em);
            int added = 0;
            for (int r = 0; r < counts.Length; r++)
            {
                if (counts[r] >= MinOreNodesPerTerritory) continue;
                var seed = RegionMap.SeedOf(r);
                if (TrySpawnIronInRegion(em, r, seed.x, seed.y)) added++;
                else
                    Debug.LogWarning($"[ResourceNodeCoverage] territory {r} " +
                                     $"({RegionMap.NameOf(r)}) has no ore node and no " +
                                     "seatable ground to put one on.");
            }
            if (added > 0)
                Debug.Log($"[ResourceNodeCoverage] topped up {added} territor" +
                           $"{(added == 1 ? "y" : "ies")} with an iron node " +
                           "(node-quota rule).");
        }

        /// <summary>Veilstone a coverage-pass outcropping holds — matches the
        /// node factory's default patch worth.</summary>
        private const int VeilstoneTopUpAmount = 300;

        /// <summary>Fraction of ALL territories that must carry veilstone
        /// (Regions.md §3, 2026-08-31).</summary>
        private const float VeilstoneCoverageFraction = 0.5f;

        /// <summary>
        /// The veilstone placement rules (Regions.md §3, 2026-08-31):
        /// every starter territory gets a veilstone outcropping, and the map
        /// is topped up until half of all territories carry one. Authored
        /// markers are honoured — only the shortfall is filled. Deterministic:
        /// homes in hall order, the fill drawn from a sorted candidate list
        /// with the match-seeded RNG, so lockstep peers agree.
        /// </summary>
        public static void GuaranteeVeilstoneCoverage()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!RegionMap.Ready || RegionMap.Count == 0) return;

            var veilstone = new int[RegionMap.Count];
            Accumulate<VeilstoneOutcroppingTag>(em, veilstone);
            var ore = OreNodeCounts(em);

            int added = 0;

            // 1. HOMES — every territory with a starting Hall MUST have one.
            //    The mandate outranks the four-ore cap; generators author two
            //    iron per home, so in practice it never collides.
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

            for (int i = 0; i < homes.Count; i++)
            {
                int r = homes[i];
                if (veilstone[r] > 0) continue;
                if (TrySpawnVeilstoneInRegion(em, r))
                {
                    veilstone[r]++;
                    ore[r]++;
                    added++;
                }
                else
                    Debug.LogWarning($"[ResourceNodeCoverage] HOME territory {r} " +
                                     $"({RegionMap.NameOf(r)}) must carry veilstone but has " +
                                     "no seatable ground for it.");
            }

            // 2. THE HALF-THE-MAP RULE. Candidates in sorted order, drawn with
            //    the seeded RNG so the spread is organic but identical on
            //    every peer.
            int have = 0;
            for (int r = 0; r < veilstone.Length; r++) if (veilstone[r] > 0) have++;
            int want = Mathf.CeilToInt(RegionMap.Count * VeilstoneCoverageFraction);

            if (have < want)
            {
                var rng = new Unity.Mathematics.Random(
                    (uint)(GameSettings.SpawnSeed ^ 0x5EED5) | 1u);
                var candidates = new List<int>();
                for (int r = 0; r < RegionMap.Count; r++)
                    if (veilstone[r] == 0 && ore[r] < MaxOreNodesPerTerritory)
                        candidates.Add(r);
                candidates.Sort();

                while (have < want && candidates.Count > 0)
                {
                    int idx = rng.NextInt(0, candidates.Count);
                    int r = candidates[idx];
                    candidates.RemoveAt(idx);
                    if (!TrySpawnVeilstoneInRegion(em, r)) continue;
                    veilstone[r]++;
                    ore[r]++;
                    have++;
                    added++;
                }
            }

            Debug.Log($"[ResourceNodeCoverage] veilstone coverage: {have}/{RegionMap.Count} " +
                       $"territories carry veilstone (target {want}), {added} node(s) added, " +
                       $"{homes.Count} home(s) guaranteed.");
        }

        private static bool TrySpawnVeilstoneInRegion(EntityManager em, int region)
        {
            var seed = RegionMap.SeedOf(region);
            if (!TrySeatInRegion(region, seed.x, seed.y, out float3 pos)) return false;
            VeilstoneOutcropping.Create(em, pos, VeilstoneTopUpAmount);
            VeilstonePatchGround.Register(pos, 1);
            return true;
        }

        /// <summary>Ore nodes (iron + veilstone + veilsteel) per territory.
        /// Shared with the veilsteel coverage pass, so both passes count the
        /// same way and neither can push a region past the cap.</summary>
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

        /// <summary>Place an iron node on standable ground inside the region,
        /// ringing outward from its seed. Region containment is checked per
        /// candidate — a node over the border would pay the neighbour.</summary>
        private static bool TrySpawnIronInRegion(EntityManager em, int region,
            float x, float z)
        {
            if (!TrySeatInRegion(region, x, z, out float3 pos)) return false;
            IronDepositBootstrap.SpawnQuotaNode(em, pos, TopUpDepositCount);
            return true;
        }

        /// <summary>Standable ground inside the region, ringing outward from
        /// the given point. Shared by the iron and veilstone passes so both
        /// seat nodes by the same rule.</summary>
        private static bool TrySeatInRegion(int region, float x, float z, out float3 pos)
        {
            pos = default;
            var grid = PassabilityGrid.Instance;
            for (float ring = 0f; ring <= 48f; ring += 8f)
            {
                int samples = ring <= 0.01f ? 1 : 8;
                for (int i = 0; i < samples; i++)
                {
                    float a = i * (Mathf.PI * 2f / samples);
                    float px = x + Mathf.Cos(a) * ring;
                    float pz = z + Mathf.Sin(a) * ring;
                    if (RegionMap.RegionAt(px, pz) != region) continue;
                    if (grid != null)
                    {
                        var cell = grid.WorldToCell(new float3(px, 0f, pz));
                        if (cell.x < 0 || cell.x >= grid.Width
                            || cell.y < 0 || cell.y >= grid.Height) continue;
                        if (grid.GetCell(cell) != PassabilityGrid.Passable) continue;
                    }
                    pos = new float3(px, TerrainUtility.GetHeight(px, pz), pz);
                    return true;
                }
            }
            return false;
        }
    }
}
