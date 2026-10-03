// CurseNodeSeeding.cs
// WHERE THE CURSE STARTS (docs/Design/Territory_Claims.md §6.4, 2026-09-29).
//
// There are no wells any more. At match start the curse raises
// `initialNodes` curse nodes on RANDOM RESOURCE NODES — supply, iron,
// veilstone or veilsteel — with two rules:
//
//   * never in a player's start territory;
//   * FAIRNESS: every player's graph distance (RegionMap.AreAdjacent hops)
//     to their nearest curse node differs by at most one. A draw that breaks
//     it is redrawn (deterministically, from the match seed); if no draw in
//     the budget satisfies it, the fairest draw found is used.
//
// A curse node is the destructible SmallNode. Its territory is claimed for
// the curse on the claim system's first tick (TerritoryClaimSystem seeds
// ownership from Fortresses and curse nodes), and CurseTerritorySystem fields
// its garrison from it.
//
// Also used by the RESEED rule (§6.5): a curse left with no node at all
// raises one more under the same rules after `reseedSeconds`.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Border
{
    public static class CurseNodeSeeding
    {
        /// <summary>Redraws tried before settling for the fairest one.</summary>
        private const int DrawBudget = 64;

        private struct Candidate
        {
            public int Territory;
            public float3 Position;
            public int EntityIndex;
        }

        /// <summary>
        /// VEILSTONE-RICH TERRITORIES START CURSED (docs/Design/Territory_Claims.md
        /// §9): a curse node rises on every veilstone outcrop in each of them.
        /// Returns how many were raised (0 when the map has no such territory).
        /// </summary>
        public static int CurseVeilstoneRich(EntityManager em)
        {
            var rich = TheWaningBorder.World.Regions.TerritoryResources.TerritoriesOf(
                TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType.VeilstoneRich);
            if (rich.Count == 0) return 0;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                                         ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
            using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
            q.Dispose();
            int raised = 0;
            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                if (!rich.Contains(RegionMap.NearestRegion(p.x, p.z))) continue;
                TheWaningBorder.Entities.SmallNode.Create(em, p);
                raised++;
            }
            return raised;
        }

        /// <summary>
        /// Raise the match's initial curse nodes. <paramref name="starts"/>
        /// are the players' base positions (their start territories are
        /// excluded and fairness is measured from them). Returns the number
        /// of nodes raised.
        /// </summary>
        public static int SeedInitialNodes(EntityManager em, IReadOnlyList<float3> starts,
                                           int count, uint seed)
        {
            if (!RegionMap.Ready || count <= 0) return 0;

            var startTerritories = StartTerritories(starts);
            var candidates = CollectCandidates(em, startTerritories, null);
            if (candidates.Count == 0) return 0;

            // Candidate territories, one entry per territory (a territory is
            // chosen, then one of its nodes).
            var territories = new List<int>();
            foreach (var c in candidates)
                if (!territories.Contains(c.Territory)) territories.Add(c.Territory);
            territories.Sort();
            count = math.min(count, territories.Count);

            var hops = new List<int[]>();
            foreach (int st in startTerritories) hops.Add(HopsFrom(st));

            var rng = new Unity.Mathematics.Random(seed | 1u);
            List<int> best = null;
            int bestSpread = int.MaxValue;
            for (int attempt = 0; attempt < DrawBudget && bestSpread > 1; attempt++)
            {
                var pick = DrawDistinct(ref rng, territories, count);
                int spread = Spread(hops, pick);
                if (spread < bestSpread) { bestSpread = spread; best = pick; }
            }

            int raised = 0;
            foreach (int t in best)
            {
                var node = NodeIn(candidates, t, ref rng);
                TheWaningBorder.Entities.SmallNode.Create(em, node);
                raised++;
            }
            UnityEngine.Debug.Log($"[CurseNodeSeeding] {raised} curse node(s) raised; " +
                $"distance spread across players = {bestSpread} hop(s).");
            return raised;
        }

        /// <summary>
        /// The reseed rule (§6.5): one node on a random eligible resource node
        /// outside every player's start territory, preferring territories no
        /// player holds. Uses the caller's RNG so the draw stays on the
        /// system's seeded stream. Returns the node position, or false.
        /// </summary>
        public static bool TryReseedOne(EntityManager em, IReadOnlyList<int> excludedTerritories,
                                        ref Unity.Mathematics.Random rng, out float3 position)
        {
            position = default;
            var excluded = new HashSet<int>(excludedTerritories);
            var candidates = CollectCandidates(em, excluded, TerritoryOwnership.Natural);
            if (candidates.Count == 0)
                candidates = CollectCandidates(em, excluded, null);
            if (candidates.Count == 0) return false;
            var c = candidates[rng.NextInt(0, candidates.Count)];
            TheWaningBorder.Entities.SmallNode.Create(em, c.Position);
            position = c.Position;
            return true;
        }

        // ── candidates ──────────────────────────────────────────────────

        private static HashSet<int> StartTerritories(IReadOnlyList<float3> starts)
        {
            var set = new HashSet<int>();
            if (starts == null) return set;
            for (int i = 0; i < starts.Count; i++)
            {
                int t = RegionMap.NearestRegion(starts[i].x, starts[i].z);
                if (t != RegionMap.None) set.Add(t);
            }
            return set;
        }

        /// <summary>Every resource node outside the excluded territories,
        /// optionally only in territories with the given owner, sorted by
        /// entity index so every peer walks them in the same order.</summary>
        private static List<Candidate> CollectCandidates(EntityManager em, HashSet<int> excluded,
                                                        int? requiredOwner)
        {
            var list = new List<Candidate>();
            Collect<SupplyNodeTag>(em, excluded, requiredOwner, list);
            Collect<IronMineTag>(em, excluded, requiredOwner, list);
            Collect<VeilstoneOutcroppingTag>(em, excluded, requiredOwner, list);
            Collect<VeilsteelDepositTag>(em, excluded, requiredOwner, list);
            list.Sort((a, b) => a.EntityIndex.CompareTo(b.EntityIndex));
            return list;
        }

        private static void Collect<T>(EntityManager em, HashSet<int> excluded, int? requiredOwner,
                                       List<Candidate> into) where T : unmanaged, IComponentData
        {
            var q = TerritoryOwnership.TagWithTransform(em, ComponentType.ReadOnly<T>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None) continue;
                if (excluded.Contains(t)) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(t))) continue;
                if (requiredOwner.HasValue && TerritoryOwnership.OwnerOf(t) != requiredOwner.Value) continue;
                into.Add(new Candidate { Territory = t, Position = p, EntityIndex = ents[i].Index });
            }
        }

        private static float3 NodeIn(List<Candidate> candidates, int territory,
                                     ref Unity.Mathematics.Random rng)
        {
            var mine = new List<float3>();
            foreach (var c in candidates) if (c.Territory == territory) mine.Add(c.Position);
            return mine[rng.NextInt(0, mine.Count)];
        }

        // ── fairness ────────────────────────────────────────────────────

        private static List<int> DrawDistinct(ref Unity.Mathematics.Random rng, List<int> pool, int count)
        {
            var bag = new List<int>(pool);
            var pick = new List<int>(count);
            for (int i = 0; i < count && bag.Count > 0; i++)
            {
                int k = rng.NextInt(0, bag.Count);
                pick.Add(bag[k]);
                bag.RemoveAt(k);
            }
            return pick;
        }

        /// <summary>Max minus min, over players, of the hop distance to that
        /// player's nearest curse node. 0 on a map with fewer than two starts.</summary>
        private static int Spread(List<int[]> hops, List<int> pick)
        {
            if (hops.Count < 2) return 0;
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var h in hops)
            {
                int nearest = int.MaxValue;
                foreach (int t in pick) nearest = math.min(nearest, h[t]);
                lo = math.min(lo, nearest);
                hi = math.max(hi, nearest);
            }
            return hi - lo;
        }

        /// <summary>Breadth-first hop count from one territory to every other
        /// over the border graph. Unreachable territories read as a large
        /// number rather than infinity, so the spread stays comparable.</summary>
        private static int[] HopsFrom(int start)
        {
            int n = RegionMap.Count;
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = 1000;
            var queue = new Queue<int>();
            dist[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int a = queue.Dequeue();
                for (int b = 0; b < n; b++)
                {
                    if (dist[b] <= dist[a] + 1) continue;
                    if (!RegionMap.AreAdjacent(a, b)) continue;
                    dist[b] = dist[a] + 1;
                    queue.Enqueue(b);
                }
            }
            return dist;
        }
    }
}
