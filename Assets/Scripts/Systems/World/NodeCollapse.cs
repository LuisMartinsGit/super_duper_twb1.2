// NodeCollapse.cs
// A SPENT NODE COLLAPSES (docs/Design/Territory_Claims.md § 11.3, 2026-10-18).
//
// Every resource node holds a finite reserve; TerritoryIncomeSystem draws each
// unit a slot pays from it, at the slot's full rate. On the tick a node's
// reserve reaches zero it is removed from the map, and every building working
// it collapses with it:
//
//   * a supply node      -> the Gatherer's Hut on it
//   * an iron node       -> the Mine on it
//   * a veilstone outcrop-> the Veilstone Mine or Runai Sanctuary on it, and
//                           every Alanthor Trading Outpost on its side slots
//                           (matched by the outcrop position the post keeps)
//
// "On it" is the income rule's own: within TerritoryIncomeSystem's
// MineToNodeRange, and this node is the building's NEAREST node of that kind.
// Collapse is destruction — Health -> 0, DeathSystem destroys, no refund. The
// node's obstacle cell is unblocked and its entity destroyed (the
// presentation layer drops views of entities that no longer exist).
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
                // spent nodes of this kind, collected first: the structural
                // changes come after the scan
                var spent = new NativeList<int>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                    if (res[i].Initial > 0f && res[i].Remaining <= 0f) spent.Add(i);
                for (int s = 0; s < spent.Length; s++)
                {
                    int i = spent[s];
                    float3 p = xfs[i].Position;
                    int before = _killed;
                    int collapsed = CollapseWorkers(em, kind, p, xfs);
                    UnityEngine.Debug.Log($"[NodeCollapse] {(kind == KindSupply ? "supply" : kind == KindIron ? "iron" : "veilstone")} " +
                                          $"node at ({p.x:0},{p.z:0}) ran dry — {_killed - before} building(s) collapsed");
                    Notify(kind, p, collapsed);
                    PassabilityGrid.Instance?.UnblockObstacle(p, BuildGrid.ResourceNodeBlockRadius);
                    em.DestroyEntity(ents[i]);
                }
                spent.Dispose();
            }
        }

        /// <summary>Health -> 0 on every building working the node at p; the
        /// local player's count is returned for the notice.</summary>
        private static int CollapseWorkers(EntityManager em, int kind, float3 p, NativeArray<LocalTransform> nodeXfs)
        {
            int mine = 0;
            if (kind == KindSupply) mine += CollapseNear(em, QC_Huts.Get(em, QT_Huts), p, nodeXfs);
            else if (kind == KindIron) mine += CollapseNear(em, QC_Mines.Get(em, QT_Mines), p, nodeXfs);
            else
            {
                mine += CollapseNear(em, QC_VeilMines.Get(em, QT_VeilMines), p, nodeXfs);
                mine += CollapseNear(em, QC_Sanctuaries.Get(em, QT_Sanctuaries), p, nodeXfs);
                var q = QC_Outposts.Get(em, QT_Outposts);
                if (!q.IsEmptyIgnoreFilter)
                {
                    using var ents = q.ToEntityArray(Allocator.Temp);
                    using var sites = q.ToComponentDataArray<TradingOutpostSite>(Allocator.Temp);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        float dx = sites[i].OutcropX - p.x, dz = sites[i].OutcropZ - p.z;
                        if (dx * dx + dz * dz > OutpostMatch * OutpostMatch) continue;
                        mine += Kill(em, ents[i]);
                    }
                }
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
        private static void Notify(int kind, float3 p, int localCollapsed)
        {
            int t = RegionMap.Ready ? RegionMap.RegionAt(p.x, p.z) : -1;
            bool ours = !GameSettings.IsSpectating && t >= 0
                        && TerritoryOwnership.Ready
                        && TerritoryOwnership.OwnerOf(t) == (int)GameSettings.LocalPlayerFaction;
            if (!ours && localCollapsed == 0) return;
            var L = (System.Func<string, string>)TheWaningBorder.Core.Localization.Loc.T;
            string what = kind == KindSupply ? L("A supply node") : kind == KindIron ? L("An iron node") : L("A veilstone outcrop");
            string where = t >= 0 ? RegionMap.NameOf(t) : L("the wilds");
            SimSignals.Notify(localCollapsed > 0
                ? string.Format(L("{0} in {1} ran dry — the buildings working it collapsed"), what, where)
                : string.Format(L("{0} in {1} ran dry"), what, where));
        }
    }
}
