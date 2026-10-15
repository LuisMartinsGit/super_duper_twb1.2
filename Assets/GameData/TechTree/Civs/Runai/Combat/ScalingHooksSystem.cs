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

        private static void Scan(EntityManager em)
        {
            var research = FactionResearchState.Instance;
            if (research == null) return;
            var q = QC_Climbers.Get(em, QT_Climbers);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var order = new NativeList<Entity>(ents.Length, Allocator.Temp);
            for (int i = 0; i < ents.Length; i++) order.Add(ents[i]);
            for (int i = 1; i < order.Length; i++)
            {
                var k = order[i]; int j = i - 1;
                while (j >= 0 && order[j].Index > k.Index) { order[j + 1] = order[j]; j--; }
                order[j + 1] = k;
            }

            for (int i = 0; i < order.Length; i++)
            {
                var e = order[i];
                var cls = em.GetComponentData<UnitTag>(e).Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged) continue;
                if (em.HasComponent<LayerTraversalState>(e)) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                var f = em.GetComponentData<FactionTag>(e).Value;
                if (!research.HasResearched(f, TechId)) continue;

                byte layer = em.HasComponent<NavLayerIndex>(e) ? em.GetComponentData<NavLayerIndex>(e).Layer : (byte)0;
                var pos = em.GetComponentData<LocalTransform>(e).Position;
                byte want = Wants(em, e, layer);
                if (want == layer) continue;
                if (!NearestCell(pos, want, f, out var to)) continue;

                if (!em.HasComponent<NavLayerIndex>(e)) em.AddComponentData(e, new NavLayerIndex { Layer = layer });
                em.AddComponentData(e, new LayerTraversalState
                {
                    InProgress = 1, FromLayer = layer, ToLayer = want, PortalId = HookPortal,
                    Progress = 0f, StartPos = pos, EndPos = to,
                });
            }
            order.Dispose();
        }

        /// <summary>The layer the unit wants to be on, from its target or its
        /// move order; its current layer when neither says.</summary>
        private static byte Wants(EntityManager em, Entity e, byte layer)
        {
            if (em.HasComponent<Target>(e))
            {
                var t = em.GetComponentData<Target>(e).Value;
                if (t != Entity.Null && em.Exists(t) && em.HasComponent<LocalTransform>(t))
                {
                    if (em.HasComponent<WallTag>(t) || em.HasComponent<WallSegmentTag>(t)
                        || em.HasComponent<WallInstanceTag>(t))
                        return NavLayerIndex.LayerRampart;
                    return em.HasComponent<NavLayerIndex>(t) ? em.GetComponentData<NavLayerIndex>(t).Layer : NavLayerIndex.LayerGround;
                }
            }
            if (em.HasComponent<DesiredDestination>(e))
            {
                var dd = em.GetComponentData<DesiredDestination>(e);
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
