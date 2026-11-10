// NodeCollapse.cs
// A SPENT NODE COLLAPSES (docs/Design/Territory_Claims.md § 11.3, 2026-10-18).
//
// Every resource node holds a finite reserve; TerritoryIncomeSystem draws each
// unit a slot pays from it, at the slot's full rate. On the tick a node's
// reserve reaches zero it is removed from the map, and every building working
// it collapses with it:
//
//   * a supply node      -> the Gatherer's Hut on it (Guild / Work Camp /
//                           Trading Post); its 2026-10-19 Depleted exception
//                           is withdrawn (decision 45)
//   * an iron node       -> the Mine on it
//   * a veilstone outcrop-> the Veilstone Mine or Runai Sanctuary on it
//   * EVERY kind         -> every Alanthor Trading Outpost on its side slots
//                           (matched by the node position the post keeps;
//                           any node kind since 2026-11-09, decision 42)
//
// "On it" is the income rule's own: within TerritoryIncomeSystem's
// MineToNodeRange, and this node is the building's NEAREST node of that kind.
// Collapse is destruction — Health -> 0, DeathSystem destroys, no refund. The
// node's obstacle cell is unblocked and its entity destroyed (the
// presentation layer drops views of entities that no longer exist).
//
// TRADE HOLDS THE LAST UNIT (2026-11-10, decision 45): a node with a finished
// Trading Outpost beside it is never spent. The income tick stops drawing at
// HeldReserve; here the node is tagged NodeTradeHeld (extractors no longer snap
// to it), its extractor collapses and its posts keep paying. When the last post
// beside a held node falls, the node collapses on the next tick.
//
// Lockstep: called from the income tick (SimCadence clock), walking queries
// in chunk order, reading replicated reserves only — every peer collapses the
// same nodes on the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.World
{
    public static class NodeCollapse
    {
        /// <summary>The income rule's "built on it" range (TerritoryIncomeSystem
        /// MineToNodeRange): an extractor this close to a node works it.</summary>
        private const float WorkRange = 12f;
        /// <summary>A Trading Outpost's stored outcrop position matches a node
        /// this close (the post keeps the outcrop's exact centre).</summary>
        private const float OutpostMatch = 1f;

        private const int KindSupply = 0, KindIron = 1, KindVeilstone = 2;

        /// <summary>Buildings collapsed so far (any owner), for the log line.</summary>
        private static int _killed;
        /// <summary>A node with a finished Trading Outpost beside it is held at
        /// this many units (Territory_Claims.md § 11.3, 2026-11-10): extraction
        /// stops there and the node stays for its posts.</summary>
        public const float HeldReserve = 1f;
        private const float HeldEpsilon = 1e-3f;

        static readonly ComponentType[] QT_FinishedPosts = { ComponentType.ReadOnly<TradingOutpostSite>(), ComponentType.ReadOnly<TradingOutpostTag>(), ComponentType.Exclude<UnderConstruction>() };
        static CachedEntityQuery QC_FinishedPosts;
        static readonly ComponentType[] QT_AnyPosts = { ComponentType.ReadOnly<TradingOutpostSite>(), ComponentType.ReadOnly<TradingOutpostTag>() };
        static CachedEntityQuery QC_AnyPosts;

        /// <summary>Does any Trading Outpost — finished or still rising — stand
        /// beside the node at p? A node whose grace ran out waits for a post
        /// under construction (decision 50).</summary>
        public static bool HasAnyPostBeside(EntityManager em, float3 p)
        {
            var q = QC_AnyPosts.Get(em, QT_AnyPosts);
            if (q.IsEmptyIgnoreFilter) return false;
            using var sites = q.ToComponentDataArray<TradingOutpostSite>(Allocator.Temp);
            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i].Side == TheWaningBorder.Entities.TradingOutpost.OnOutcrop) continue;
                float dx = sites[i].OutcropX - p.x, dz = sites[i].OutcropZ - p.z;
                if (dx * dx + dz * dz <= OutpostMatch * OutpostMatch) return true;
            }
            return false;
        }

        /// <summary>Does a finished Trading Outpost (any owner) stand beside the
        /// node at p? The income census's own test (finished posts only).</summary>
        public static bool HasPostBeside(EntityManager em, float3 p)
        {
            var q = QC_FinishedPosts.Get(em, QT_FinishedPosts);
            if (q.IsEmptyIgnoreFilter) return false;
            using var sites = q.ToComponentDataArray<TradingOutpostSite>(Allocator.Temp);
            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i].Side == TheWaningBorder.Entities.TradingOutpost.OnOutcrop) continue;
                float dx = sites[i].OutcropX - p.x, dz = sites[i].OutcropZ - p.z;
                if (dx * dx + dz * dz <= OutpostMatch * OutpostMatch) return true;
            }
            return false;
        }

        private static string KindName(int kind)
            => kind == KindSupply ? "supply" : kind == KindIron ? "iron" : "veilstone";

        static readonly ComponentType[][] QT_Nodes =
        {
            new[] { ComponentType.ReadOnly<SupplyNodeTag>(), ComponentType.ReadOnly<NodeReserve>(), ComponentType.ReadOnly<LocalTransform>() },
            new[] { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<NodeReserve>(), ComponentType.ReadOnly<LocalTransform>() },
            new[] { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<NodeReserve>(), ComponentType.ReadOnly<LocalTransform>() },
        };
        static readonly CachedEntityQuery[] QC_Nodes = new CachedEntityQuery[3];

        static readonly ComponentType[] QT_Huts = { ComponentType.ReadOnly<GathererHutTag>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.Exclude<RaiderCampTag>() };
        static readonly ComponentType[] QT_Mines = { ComponentType.ReadOnly<MineTag>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>() };
        static readonly ComponentType[] QT_VeilMines = { ComponentType.ReadOnly<VeilstoneMineTag>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>() };
        static readonly ComponentType[] QT_Sanctuaries = { ComponentType.ReadOnly<RunaiSanctuaryTag>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>() };
        static readonly ComponentType[] QT_Outposts = { ComponentType.ReadOnly<TradingOutpostTag>(), ComponentType.ReadOnly<TradingOutpostSite>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Huts, QC_Mines, QC_VeilMines, QC_Sanctuaries, QC_Outposts;

        /// <summary>Collapse every node whose reserve is spent, with what works it.</summary>
        public static void CollapseSpent(EntityManager em)
        {
            for (int kind = 0; kind < 3; kind++)
            {
                var q = QC_Nodes[kind].Get(em, QT_Nodes[kind]);
                if (q.IsEmptyIgnoreFilter) continue;
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var res = q.ToComponentDataArray<NodeReserve>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                // Nodes at their last unit, collected first: the structural
                // changes come after the scan. TRADE HOLDS THE LAST UNIT
                // (2026-11-10, decision 45): a node with a finished Trading
                // Outpost beside it stops at HeldReserve — it stays, its
                // extractor collapses, its posts keep paying. A node with no
                // post collapses (at zero, or at once when the last post of a
                // held node falls).
                // THE GRACE WINDOW (2026-11-10, decision 50): a node that runs
                // dry with no post beside it is not destroyed at once. Its
                // extractor collapses and the node stays alive for
                // depletedGraceSeconds, accepting Trading Outposts (and no
                // extractor). A finished post in that time holds it like any
                // other; at the end of the window it collapses — unless a post
                // is still rising beside it, which it waits for.
                var spent = new NativeList<int>(Allocator.Temp);
                var held = new NativeList<int>(Allocator.Temp);
                var graceStart = new NativeList<int>(Allocator.Temp);
                float now = SimClock.Now;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (res[i].Initial <= 0f || res[i].Remaining > HeldReserve + HeldEpsilon) continue;
                    var e = ents[i];
                    bool wasHeld = em.HasComponent<NodeTradeHeld>(e);
                    bool inGrace = em.HasComponent<NodeDepletedGrace>(e);
                    bool post = HasPostBeside(em, xfs[i].Position);
                    if (post && (res[i].Remaining > 0f || inGrace || wasHeld)) { held.Add(i); continue; }
                    if (wasHeld) { spent.Add(i); continue; }   // its last post fell
                    if (!inGrace) { graceStart.Add(i); continue; }
                    if (now < em.GetComponentData<NodeDepletedGrace>(e).Until) continue;
                    if (HasAnyPostBeside(em, xfs[i].Position)) continue;   // a post is rising: wait for it
                    spent.Add(i);
                }
                float grace = TerritoryIncomeSystemConfig.I != null ? TerritoryIncomeSystemConfig.I.depletedGraceSeconds : 0f;
                for (int s = 0; s < graceStart.Length; s++)
                {
                    int i = graceStart[s];
                    float3 p = xfs[i].Position;
                    if (grace <= 0f) { spent.Add(i); continue; }
                    em.AddComponentData(ents[i], new NodeDepletedGrace { Until = now + grace });
                    int before = _killed;
                    int collapsed = CollapseWorkers(em, kind, p, xfs);
                    UnityEngine.Debug.Log($"[NodeCollapse] {KindName(kind)} node at ({p.x:0},{p.z:0}) ran dry — " +
                                          $"{_killed - before} extractor(s) collapsed, {grace:0}s to raise a Trading Outpost");
                    NotifyGrace(kind, p, collapsed, grace);
                }
                graceStart.Dispose();
                for (int s = 0; s < held.Length; s++)
                {
                    int i = held[s];
                    float3 p = xfs[i].Position;
                    if (!em.HasComponent<NodeTradeHeld>(ents[i])) em.AddComponent<NodeTradeHeld>(ents[i]);
                    if (em.HasComponent<NodeDepletedGrace>(ents[i])) em.RemoveComponent<NodeDepletedGrace>(ents[i]);
                    int before = _killed;
                    int collapsed = CollapseWorkers(em, kind, p, xfs);
                    if (_killed == before) continue;
                    UnityEngine.Debug.Log($"[NodeCollapse] {KindName(kind)} node at ({p.x:0},{p.z:0}) held by trade — " +
                                          $"{_killed - before} extractor(s) collapsed, the node stays for its posts");
                    Notify(kind, p, collapsed, heldByTrade: true);
                }
                for (int s = 0; s < spent.Length; s++)
                {
                    int i = spent[s];
                    float3 p = xfs[i].Position;
                    int before = _killed;
                    int collapsed = CollapseWorkers(em, kind, p, xfs);
                    collapsed += CollapsePosts(em, p);
                    UnityEngine.Debug.Log($"[NodeCollapse] {KindName(kind)} node at ({p.x:0},{p.z:0}) ran dry — " +
                                          $"{_killed - before} building(s) collapsed");
                    Notify(kind, p, collapsed, heldByTrade: false);
                    PassabilityGrid.Instance?.UnblockObstacle(p, BuildGrid.ResourceNodeBlockRadius);
                    em.DestroyEntity(ents[i]);
                }
                held.Dispose();
                spent.Dispose();
            }
        }

        /// <summary>Health -> 0 on every building working the node at p; the
        /// local player's count is returned for the notice.</summary>
        private static int CollapseWorkers(EntityManager em, int kind, float3 p, NativeArray<LocalTransform> nodeXfs)
        {
            int mine = 0;
            // The Gatherer's Hut collapses like every other extractor (decision 45:
            // its Depleted exception is withdrawn, the surveys it protected are gone).
            if (kind == KindSupply) mine += CollapseNear(em, QC_Huts.Get(em, QT_Huts), p, nodeXfs);
            else if (kind == KindIron) mine += CollapseNear(em, QC_Mines.Get(em, QT_Mines), p, nodeXfs);
            else
            {
                mine += CollapseNear(em, QC_VeilMines.Get(em, QT_VeilMines), p, nodeXfs);
                mine += CollapseNear(em, QC_Sanctuaries.Get(em, QT_Sanctuaries), p, nodeXfs);
            }
            return mine;
        }

        /// <summary>Every Trading Outpost on the spent node's side slots falls
        /// with it (Veilstone_Economy.md §3.1): matched by the node position the
        /// post keeps. Returns the local player's count.</summary>
        private static int CollapsePosts(EntityManager em, float3 p)
        {
            var q = QC_Outposts.Get(em, QT_Outposts);
            if (q.IsEmptyIgnoreFilter) return 0;
            int mine = 0;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var sites = q.ToComponentDataArray<TradingOutpostSite>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                float dx = sites[i].OutcropX - p.x, dz = sites[i].OutcropZ - p.z;
                if (dx * dx + dz * dz > OutpostMatch * OutpostMatch) continue;
                mine += Kill(em, ents[i]);
            }
            return mine;
        }

        /// <summary>Kill every building of the query within WorkRange of p
        /// whose nearest node of this kind is the one at p.</summary>
        private static int CollapseNear(EntityManager em, EntityQuery q, float3 p, NativeArray<LocalTransform> nodeXfs)
        {
            if (q.IsEmptyIgnoreFilter) return 0;
            int mine = 0;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float r2 = WorkRange * WorkRange;
            for (int i = 0; i < ents.Length; i++)
            {
                float3 b = xfs[i].Position;
                float d2 = math.distancesq(b.xz, p.xz);
                if (d2 > r2) continue;
                bool nearest = true;
                for (int n = 0; n < nodeXfs.Length && nearest; n++)
                {
                    float3 o = nodeXfs[n].Position;
                    if (o.x == p.x && o.z == p.z) continue;
                    if (math.distancesq(b.xz, o.xz) < d2) nearest = false;
                }
                if (nearest) mine += Kill(em, ents[i]);
            }
            return mine;
        }

        private static int Kill(EntityManager em, Entity e)
        {
            var hp = em.GetComponentData<Health>(e);
            if (hp.Value <= 0) return 0;
            hp.Value = 0;
            em.SetComponentData(e, hp);
            _killed++;
            bool local = !GameSettings.IsSpectating
                         && em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;
            return local ? 1 : 0;
        }

        /// <summary>Tell the local player when a node in their ground runs dry.
        /// Presentation only.</summary>
        private static void NotifyGrace(int kind, float3 p, int localCollapsed, float grace)
        {
            int t = RegionMap.Ready ? RegionMap.RegionAt(p.x, p.z) : -1;
            bool ours = !GameSettings.IsSpectating && t >= 0
                        && TerritoryOwnership.Ready
                        && TerritoryOwnership.OwnerOf(t) == (int)GameSettings.LocalPlayerFaction;
            if (!ours && localCollapsed == 0) return;
            var L = (System.Func<string, string>)TheWaningBorder.Core.Localization.Loc.T;
            string what = kind == KindSupply ? L("A supply node") : kind == KindIron ? L("An iron node") : L("A veilstone outcrop");
            string where = t >= 0 ? RegionMap.NameOf(t) : L("the wilds");
            SimSignals.Notify(string.Format(
                L("{0} in {1} ran dry — raise a Trading Outpost beside it within {2} minutes or it is lost"),
                what, where, (int)System.Math.Round(grace / 60f)));
        }

        private static void Notify(int kind, float3 p, int localCollapsed, bool heldByTrade)
        {
            int t = RegionMap.Ready ? RegionMap.RegionAt(p.x, p.z) : -1;
            bool ours = !GameSettings.IsSpectating && t >= 0
                        && TerritoryOwnership.Ready
                        && TerritoryOwnership.OwnerOf(t) == (int)GameSettings.LocalPlayerFaction;
            if (!ours && localCollapsed == 0) return;
            var L = (System.Func<string, string>)TheWaningBorder.Core.Localization.Loc.T;
            string what = kind == KindSupply ? L("A supply node") : kind == KindIron ? L("An iron node") : L("A veilstone outcrop");
            string where = t >= 0 ? RegionMap.NameOf(t) : L("the wilds");
            SimSignals.Notify(heldByTrade
                ? string.Format(L("{0} in {1} is worked out — its Trading Outposts keep it, the extractor collapsed"), what, where)
                : localCollapsed > 0
                    ? string.Format(L("{0} in {1} ran dry — the buildings working it collapsed"), what, where)
                    : string.Format(L("{0} in {1} ran dry"), what, where));
        }
    }
}

/// <summary>A node that ran dry with no Trading Outpost beside it: alive
/// until <see cref="Until"/> (sim seconds), accepting posts and no extractor
/// (Territory_Claims.md § 11.3, decision 50).</summary>
public struct NodeDepletedGrace : IComponentData { public float Until; }

/// <summary>A node held at its last unit by the Trading Outposts beside it
/// (Territory_Claims.md § 11.3, 2026-11-10). No extractor may be placed on it.</summary>
public struct NodeTradeHeld : IComponentData { }
