// ScalingHooksSystem.cs
// Scaling Hooks (docs/Design/Age_1_Runai.md §6.5): with the tech, Runai
// INFANTRY climb onto a wall-walk anywhere along it — no stair, no gate —
// and drop back down the same way.
//
// A climb is an ordinary LayerTraversalState with PortalId = -1, so the
// integrator stands aside exactly as it does for a portal traversal;
// LayerTransitionSystem ignores PortalId -1 and this system animates it.
//
// A unit on the ground climbs when it stands beside a rampart cell and what
// it wants is up there: its target stands on a wall-walk, its target is a
// wall piece, or its move order ends on a rampart cell. A unit on a
// wall-walk drops down when its target is on the ground, or its move order
// ends on the ground. Units are walked in entity order; every peer agrees.
//
// PERF (2026-10-16). The scan insertion-sorted EVERY Runai infantry unit of
// every faction (O(n^2) on chunk order) and then made ~8 EntityManager calls
// per unit before it even asked whether the faction had the tech. Now: the
// tech is read once per faction and a scan with no faction holding it does
// nothing; the class / health / faction filters run on bulk chunk copies;
// the survivors are sorted with an O(n log n) sort (unique keys, so the same
// order); Wants reads through ComponentLookups; and the two component adds
// are queued until after the walk. Deferring them changes nothing the walk
// reads: a unit only ever reads its OWN LayerTraversalState, and a target
// that would have just received NavLayerIndex gets Layer = its current
// layer, which for a unit without one is LayerGround (0) — exactly what
// "no NavLayerIndex" answers. Result-identical.

using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Runai
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LayerTransitionSystem))]
    public partial class ScalingHooksSystem : SystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            // Runai ships only in edition C (docs/Design/Alpha_Build.md §4). Off
            // elsewhere: its configs are stripped from A/B builds, and running
            // it there threw every tick, in the menu and in every match.
            if (!GameEdition.Allows(Cultures.Runai)) { Enabled = false; return; }
            _lkTarget = GetComponentLookup<Target>(true);
            _lkFaction = GetComponentLookup<FactionTag>(true);
            _lkXf = GetComponentLookup<LocalTransform>(true);
            _lkLayer = GetComponentLookup<NavLayerIndex>(true);
            _lkDD = GetComponentLookup<DesiredDestination>(true);
            _lkWall = GetComponentLookup<WallTag>(true);
            _lkWallSeg = GetComponentLookup<WallSegmentTag>(true);
            _lkWallInst = GetComponentLookup<WallInstanceTag>(true);
            _lkTraversal = GetComponentLookup<LayerTraversalState>(true);
        }

        public const string TechId = "Runai_ScalingHooks";
        public const int HookPortal = -1;
        // Engine-side motion constants, like LayerTransitionSystem's.
        private const float ClimbRate = 0.6666667f;   // 1.5 s a climb
        private const float Reach = 2.5f;              // metres to the cell it climbs to
        private const int SearchCells = 3;

        private SimCadence.Periodic _scan;

        static readonly ComponentType[] QT_Climbers =
        {
            ComponentType.ReadOnly<RunaiUnitTag>(), ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.Exclude<CavalryTag>(), ComponentType.Exclude<SiegeTag>(),
            ComponentType.Exclude<CaravanTag>(), ComponentType.Exclude<FlyingTag>(),
            ComponentType.Exclude<BuildingTag>(),
        };
        static CachedEntityQuery QC_Climbers;

        static readonly ComponentType[] QT_Climbing =
        {
            ComponentType.ReadWrite<LayerTraversalState>(), ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadOnly<RunaiUnitTag>(),
        };
        static CachedEntityQuery QC_Climbing;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            Animate(em, dt);
            if (_scan.Due(dt, 0.25f)) Scan(em);
        }

        private static void Animate(EntityManager em, float dt)
        {
            var q = QC_Climbing.Get(em, QT_Climbing);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var ts = em.GetComponentData<LayerTraversalState>(e);
                if (ts.PortalId != HookPortal) continue;
                ts.Progress = math.min(1f, ts.Progress + ClimbRate * dt);
                if (ts.Progress >= 0.5f)
                {
                    var nli = em.GetComponentData<NavLayerIndex>(e);
                    if (nli.Layer != ts.ToLayer) { nli.Layer = ts.ToLayer; em.SetComponentData(e, nli); }
                }
                var xf = em.GetComponentData<LocalTransform>(e);
                xf.Position = math.lerp(ts.StartPos, ts.EndPos, ts.Progress);
                em.SetComponentData(e, xf);
                if (ts.Progress >= 1f) em.RemoveComponent<LayerTraversalState>(e);
                else em.SetComponentData(e, ts);
            }
        }

        private ComponentLookup<Target> _lkTarget;
        private ComponentLookup<FactionTag> _lkFaction;
        private ComponentLookup<LocalTransform> _lkXf;
        private ComponentLookup<NavLayerIndex> _lkLayer;
        private ComponentLookup<DesiredDestination> _lkDD;
        private ComponentLookup<WallTag> _lkWall;
        private ComponentLookup<WallSegmentTag> _lkWallSeg;
        private ComponentLookup<WallInstanceTag> _lkWallInst;
        private ComponentLookup<LayerTraversalState> _lkTraversal;

        private struct Climb { public Entity E; public bool AddLayer; public LayerTraversalState S; public byte Layer; }

        private void Scan(EntityManager em)
        {
            var research = FactionResearchState.Instance;
            if (research == null) return;
            var q = QC_Climbers.Get(em, QT_Climbers);
            if (q.IsEmptyIgnoreFilter) return;

            // The tech, once per faction: no holder, no scan.
            ulong hooks = 0;
            for (int fi = 0; fi < 64; fi++)
                if (research.HasResearched((Faction)fi, TechId)) hooks |= 1UL << fi;
            if (hooks == 0) return;

            CompleteDependency();
            var order = new NativeList<Entity>(Allocator.Temp);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var hps = q.ToComponentDataArray<Health>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var cls = tags[i].Class;
                    if (cls != UnitClass.Melee && cls != UnitClass.Ranged) continue;
                    if (hps[i].Value <= 0) continue;
                    int fi = (int)facs[i].Value;
                    if (fi < 0 || fi >= 64 || (hooks & (1UL << fi)) == 0) continue;
                    order.Add(ents[i]);
                }
            }
            if (order.Length == 0) { order.Dispose(); return; }
            order.Sort(new ByIndex());

            _lkTarget.Update(this); _lkXf.Update(this); _lkLayer.Update(this); _lkDD.Update(this);
            _lkWall.Update(this); _lkWallSeg.Update(this); _lkWallInst.Update(this); _lkTraversal.Update(this);
            _lkFaction.Update(this);

            var climbs = new NativeList<Climb>(Allocator.Temp);
            for (int i = 0; i < order.Length; i++)
            {
                var e = order[i];
                if (_lkTraversal.HasComponent(e)) continue;
                var f = _lkFaction[e].Value;

                bool hasLayer = _lkLayer.TryGetComponent(e, out var nli);
                byte layer = hasLayer ? nli.Layer : (byte)0;
                var pos = _lkXf[e].Position;
                byte want = Wants(e, layer);
                if (want == layer) continue;
                if (!NearestCell(pos, want, f, out var to)) continue;

                climbs.Add(new Climb
                {
                    E = e, AddLayer = !hasLayer, Layer = layer,
                    S = new LayerTraversalState
                    {
                        InProgress = 1, FromLayer = layer, ToLayer = want, PortalId = HookPortal,
                        Progress = 0f, StartPos = pos, EndPos = to,
                    },
                });
            }
            order.Dispose();

            for (int i = 0; i < climbs.Length; i++)
            {
                var c = climbs[i];
                if (c.AddLayer && !em.HasComponent<NavLayerIndex>(c.E)) em.AddComponentData(c.E, new NavLayerIndex { Layer = c.Layer });
                em.AddComponentData(c.E, c.S);
            }
            climbs.Dispose();
        }

        private struct ByIndex : System.Collections.Generic.IComparer<Entity>
        {
            public int Compare(Entity a, Entity b) => a.Index.CompareTo(b.Index);
        }

        /// <summary>The layer the unit wants to be on, from its target or its
        /// move order; its current layer when neither says.</summary>
        private byte Wants(Entity e, byte layer)
        {
            if (_lkTarget.TryGetComponent(e, out var tc))
            {
                var t = tc.Value;
                if (t != Entity.Null && _lkXf.HasComponent(t))
                {
                    if (_lkWall.HasComponent(t) || _lkWallSeg.HasComponent(t) || _lkWallInst.HasComponent(t))
                        return NavLayerIndex.LayerRampart;
                    return _lkLayer.TryGetComponent(t, out var tl) ? tl.Layer : NavLayerIndex.LayerGround;
                }
            }
            if (_lkDD.TryGetComponent(e, out var dd))
            {
                if (dd.Has == 0) return layer;
                var cell = NavGridQuery.WorldToCellInt2(dd.Position);
                if (cell.x == int.MinValue) return layer;
                bool rampart = NavGridQuery.IsCellPassable(cell, NavLayerIndex.LayerRampart);
                bool ground = NavGridQuery.IsCellPassable(cell, NavLayerIndex.LayerGround);
                if (rampart && !ground) return NavLayerIndex.LayerRampart;
                if (ground && !rampart) return NavLayerIndex.LayerGround;
            }
            return layer;
        }

        /// <summary>The nearest cell within reach that is walkable on
        /// <paramref name="layer"/> (and, going up, not on the ground).
        /// Ties break by scan order, the same on every peer.</summary>
        private static bool NearestCell(float3 pos, byte layer, Faction f, out float3 to)
        {
            to = default;
            var c = NavGridQuery.WorldToCellInt2(pos);
            if (c.x == int.MinValue) return false;
            float best = Reach * Reach;
            bool found = false;
            for (int dz = -SearchCells; dz <= SearchCells; dz++)
            for (int dx = -SearchCells; dx <= SearchCells; dx++)
            {
                var cell = new int2(c.x + dx, c.y + dz);
                if (!NavGridQuery.IsCellPassableFor(cell, layer, (byte)f)) continue;
                if (layer == NavLayerIndex.LayerRampart && NavGridQuery.IsCellPassable(cell, NavLayerIndex.LayerGround)) continue;
                var w = NavGridQuery.CellCenter(cell);
                float d = math.distancesq(w.xz, pos.xz);
                if (d >= best) continue;
                best = d; found = true;
                float y = layer == NavLayerIndex.LayerRampart
                    ? LayerTransitionSystem.DeckYAt(w.x, w.z)
                    : TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(w.x, w.z);
                to = new float3(w.x, y, w.z);
            }
            return found;
        }
    }
}
