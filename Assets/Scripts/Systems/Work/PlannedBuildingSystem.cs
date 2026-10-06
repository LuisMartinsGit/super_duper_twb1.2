// PlannedBuildingSystem.cs
// Turns PLANS into construction sites when a worker arrives
// (docs/Design/Planned_Buildings.md).
//
// A worker's BuildCommand names either the plan itself (single player, the
// AI, a Build order that resolved its target) or Entity.Null with the site's
// position (a lockstep Build issued in the same tick as the placement). Both
// resolve to the faction's OWN plan there — never another player's. Once the
// worker stands within build range the plan BREAKS GROUND
// (PlannedBuildings.BreakGround): the real under-construction site appears,
// the worker's order is re-pointed at it, and BuildCommandSystem — which runs
// right after — turns it into a BuildOrder on the same tick.
//
// Plans nobody is heading for (placed with no worker selected, or whose
// workers were pulled off) are adopted by an idle worker of the same faction
// within reach, like an abandoned foundation.
//
// Simulation, structural changes applied after each scan, query order only —
// every lockstep peer breaks ground on the same plan on the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Work
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BuildCommandSystem))]
    public partial class PlannedBuildingSystem : SystemBase
    {
        /// <summary>Same reach BuildCommandSystem uses to start building.</summary>
        private const float BuildRange = 4f;
        /// <summary>How far from a Build order's position its plan may stand.</summary>
        private const float ResolveRadius = 8f;
        /// <summary>How far an idle worker walks to adopt an unattended plan.</summary>
        private const float AdoptRadius = 30f;
        private const float AdoptInterval = 1f;

        private SimCadence.Periodic _adopt;

        static readonly ComponentType[] QT_Plans =
        {
            ComponentType.ReadOnly<PlannedBuilding>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Plans;

        protected override void OnCreate()
        {
            RequireForUpdate<PlannedBuilding>();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var retarget = new NativeList<Entity>(Allocator.Temp);
            var retargetTo = new NativeList<Entity>(Allocator.Temp);
            var breakPlan = new NativeList<Entity>(Allocator.Temp);
            var breakWorker = new NativeList<Entity>(Allocator.Temp);

            foreach (var (xf, cmd, fac, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<BuildCommand>, RefRO<FactionTag>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                var target = cmd.ValueRO.TargetBuilding;
                Entity plan = Entity.Null;
                if (target != Entity.Null && em.Exists(target) && em.HasComponent<PlannedBuilding>(target))
                {
                    // A plan of ANOTHER faction is never this worker's to raise.
                    if (em.GetComponentData<FactionTag>(target).Value != fac.ValueRO.Value) continue;
                    plan = target;
                }
                else if (target == Entity.Null || !em.Exists(target))
                {
                    plan = PlannedBuildings.FindOwnPlanNear(em, fac.ValueRO.Value,
                        cmd.ValueRO.Position, ResolveRadius);
                    if (plan == Entity.Null) continue;
                    retarget.Add(entity);
                    retargetTo.Add(plan);
                }
                else continue;   // a real site — BuildCommandSystem's job

                if (TargetGeometry.SurfaceDistXZ(em, xf.ValueRO.Position, plan) > BuildRange) continue;
                // A refused plan retries on its own cadence (PlannedBuildings.BreakGround).
                if (em.GetComponentData<PlannedBuilding>(plan).NextBreakGroundAt > SimClock.Now) continue;
                if (breakPlan.Contains(plan)) continue;
                breakPlan.Add(plan);
                breakWorker.Add(entity);
            }

            for (int i = 0; i < retarget.Length; i++)
            {
                if (!em.Exists(retarget[i]) || !em.HasComponent<BuildCommand>(retarget[i])) continue;
                var c = em.GetComponentData<BuildCommand>(retarget[i]);
                c.TargetBuilding = retargetTo[i];
                em.SetComponentData(retarget[i], c);
            }
            for (int i = 0; i < breakPlan.Length; i++)
                PlannedBuildings.BreakGround(em, breakPlan[i], breakWorker[i]);

            retarget.Dispose(); retargetTo.Dispose(); breakPlan.Dispose(); breakWorker.Dispose();

            if (_adopt.Due(SystemAPI.Time.DeltaTime, AdoptInterval)) AdoptUnattended(em);
        }

        /// <summary>Send the nearest idle same-faction worker to every plan
        /// that no worker is headed for (current order or queue).</summary>
        private void AdoptUnattended(EntityManager em)
        {
            var q = QC_Plans.Get(em, QT_Plans);
            using var plans = q.ToEntityArray(Allocator.Temp);
            if (plans.Length == 0) return;
            using var planFacs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var planXfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var attended = new NativeHashSet<Entity>(plans.Length, Allocator.Temp);
            var idle = new NativeList<Entity>(Allocator.Temp);
            var idlePos = new NativeList<float3>(Allocator.Temp);
            var idleFac = new NativeList<Faction>(Allocator.Temp);

            foreach (var (xf, fac, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                if (em.HasComponent<BuildCommand>(entity))
                {
                    attended.Add(em.GetComponentData<BuildCommand>(entity).TargetBuilding);
                    continue;
                }
                bool queuedAny = false;
                if (em.HasBuffer<QueuedBuildSite>(entity))
                {
                    var buf = em.GetBuffer<QueuedBuildSite>(entity);
                    for (int k = 0; k < buf.Length; k++) { attended.Add(buf[k].TargetBuilding); queuedAny = true; }
                }
                if (queuedAny) continue;
                if (em.HasComponent<BuildOrder>(entity) || em.HasComponent<RepairOrder>(entity)) continue;
                if (em.HasComponent<UserMoveOrder>(entity) && em.IsComponentEnabled<UserMoveOrder>(entity)) continue;
                idle.Add(entity);
                idlePos.Add(xf.ValueRO.Position);
                idleFac.Add(fac.ValueRO.Value);
            }

            float r2 = AdoptRadius * AdoptRadius;
            for (int p = 0; p < plans.Length; p++)
            {
                if (attended.Contains(plans[p])) continue;
                int best = -1;
                float bestD = r2;
                for (int i = 0; i < idle.Length; i++)
                {
                    if (idle[i] == Entity.Null || idleFac[i] != planFacs[p].Value) continue;
                    float d = math.distancesq(idlePos[i].xz, planXfs[p].Position.xz);
                    if (d <= bestD) { bestD = d; best = i; }
                }
                if (best < 0) continue;
                BuildCommandHelper.Execute(em, idle[best], plans[p],
                    PlannedBuildings.IdOf(em, plans[p]), planXfs[p].Position);
                idle[best] = Entity.Null;
            }

            attended.Dispose(); idle.Dispose(); idlePos.Dispose(); idleFac.Dispose();
        }
    }
}
