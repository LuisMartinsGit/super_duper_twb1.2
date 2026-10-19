// PlannedBuildingSystem.cs
// Turns PLANS into construction sites when a worker arrives, keeps every
// worker's build queue moving, and makes sure no plan outlives the worker
// that was going to build it (docs/Design/Planned_Buildings.md §1, §6).
//
// A worker's BuildCommand names either the plan itself (bound when the order
// was executed — BuildCommandHelper.Execute → PlannedBuildings
// .ResolveOrderTarget) or, rarely, Entity.Null with the site's position (a
// wall hub; a Build whose placement was refused). A Null target is resolved
// to the faction's OWN plan of that building whose footprint covers the spot
// — never another player's, never a neighbouring plan. Once the worker stands
// within build range the plan BREAKS GROUND (PlannedBuildings.BreakGround):
// the real under-construction site appears, the worker's order is re-pointed
// at it, and BuildCommandSystem — which runs right after — turns it into a
// BuildOrder on the same tick.
//
// QUEUES RESUME HERE (2026-10-19). A worker that finished or lost its current
// job used to resume its queue only from BuildingConstructionSystem's
// adoption scan — and that system returns early unless SOME worker in the
// world holds an active BuildOrder. A worker finishing the last build in
// progress anywhere therefore stood idle with its whole queue: the chained
// plans stayed white previews for ever, and because the worker still HAD a
// queue it was never counted idle enough to adopt them either. The resume
// pass below runs every tick, plans or no plans.
//
// THE OWNERSHIP SWEEP (§6). Once a second every plan is checked against the
// live workers' build orders and queues. A plan nobody holds is cancelled and
// refunded — after a short grace for one that lost its worker, or after an
// adoption attempt and a longer grace for one that never had a worker (a
// lever placement, an AI placement with no free hands).
//
// Simulation, structural changes applied after each scan, query order only —
// every lockstep peer breaks ground, resumes, adopts and cancels the same
// plans on the same tick.

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
        /// <summary>Ownership sweep cadence.</summary>
        private const float SweepInterval = 1f;
        /// <summary>A plan that HAD a worker and lost it (death, a stuck plan
        /// from an older save) is cancelled after this. Engine timing — it
        /// only has to outlast the sweep cadence.</summary>
        private const float LostOwnerGraceSeconds = 2f;
        /// <summary>A plan that NEVER had a worker is offered to idle workers
        /// for this long before it is cancelled. Engine timing.</summary>
        private const float NeverOwnedGraceSeconds = 10f;

        // Field name kept from the old adoption pass: saved games serialise
        // system fields by name.
        private SimCadence.Periodic _adopt;

        static readonly ComponentType[] QT_Plans =
        {
            ComponentType.ReadOnly<PlannedBuilding>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Plans;

        protected override void OnUpdate()
        {
            var em = EntityManager;

            ResumeQueues(em);

            if (QC_Plans.Get(em, QT_Plans).IsEmptyIgnoreFilter) return;

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
                    // The plan of THIS building at THIS spot — not the nearest
                    // plan within a radius, which bound chained orders to their
                    // neighbours' plans and wall-hub orders to any plan nearby.
                    plan = PlannedBuildings.ResolveOrderTarget(em, entity,
                        cmd.ValueRO.BuildingId.ToString(), cmd.ValueRO.Position);
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
                if (!em.Exists(retarget[i]) || !TransientState.Active<BuildCommand>(em, retarget[i])) continue;
                var c = em.GetComponentData<BuildCommand>(retarget[i]);
                c.TargetBuilding = retargetTo[i];
                em.SetComponentData(retarget[i], c);
            }
            for (int i = 0; i < breakPlan.Length; i++)
                PlannedBuildings.BreakGround(em, breakPlan[i], breakWorker[i]);

            retarget.Dispose(); retargetTo.Dispose(); breakPlan.Dispose(); breakWorker.Dispose();

            if (_adopt.Due(SystemAPI.Time.DeltaTime, SweepInterval)) SweepOwnership(em);
        }

        /// <summary>
        /// Start the next queued build of every live worker that has a queue
        /// and no current build, repair or construction job. Runs every tick:
        /// see the file header for why this cannot live in the gated
        /// BuildingConstructionSystem.
        /// </summary>
        private void ResumeQueues(EntityManager em)
        {
            var resume = new NativeList<Entity>(Allocator.Temp);
            foreach (var (queue, entity) in SystemAPI
                .Query<DynamicBuffer<QueuedBuildSite>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                if (queue.Length == 0) continue;
                if (TransientState.Active<BuildCommand>(em, entity)
                    || TransientState.Active<BuildOrder>(em, entity)
                    || TransientState.Active<RepairOrder>(em, entity)) continue;
                resume.Add(entity);
            }
            for (int i = 0; i < resume.Length; i++)
                if (PlannedBuildings.IsLiveWorker(em, resume[i]))
                    BuildCommandHelper.TryStartNextQueued(em, resume[i]);
            resume.Dispose();
        }

        /// <summary>
        /// Planned_Buildings.md §6: bind any order still naming only a spot,
        /// mark which plans a live worker holds, offer never-held plans to an
        /// idle worker, and cancel (refunding) the plans nobody holds once
        /// their grace is up.
        /// </summary>
        private void SweepOwnership(EntityManager em)
        {
            var q = QC_Plans.Get(em, QT_Plans);
            using var plans = q.ToEntityArray(Allocator.Temp);
            if (plans.Length == 0) return;
            using var planFacs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var planXfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // ── Every builder, in query order ──
            var workers = new NativeList<Entity>(Allocator.Temp);
            var workerPos = new NativeList<float3>(Allocator.Temp);
            var workerFac = new NativeList<Faction>(Allocator.Temp);
            foreach (var (xf, fac, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                workers.Add(entity);
                workerPos.Add(xf.ValueRO.Position);
                workerFac.Add(fac.ValueRO.Value);
            }

            var held = new NativeHashSet<Entity>(plans.Length, Allocator.Temp);
            var idle = new NativeList<int>(Allocator.Temp);
            for (int w = 0; w < workers.Length; w++)
            {
                var e = workers[w];
                if (!PlannedBuildings.IsLiveWorker(em, e)) continue;
                bool any = false;

                if (TransientState.Active<BuildCommand>(em, e))
                {
                    any = true;
                    var cmd = em.GetComponentData<BuildCommand>(e);
                    if (cmd.TargetBuilding == Entity.Null)
                    {
                        var bound = PlannedBuildings.ResolveOrderTarget(em, e, cmd.BuildingId.ToString(), cmd.Position);
                        if (bound != Entity.Null) { cmd.TargetBuilding = bound; em.SetComponentData(e, cmd); }
                    }
                    if (PlannedBuildings.IsPlan(em, cmd.TargetBuilding)
                        && em.GetComponentData<FactionTag>(cmd.TargetBuilding).Value == workerFac[w])
                        held.Add(cmd.TargetBuilding);
                }
                if (em.HasBuffer<QueuedBuildSite>(e))
                {
                    var buf = em.GetBuffer<QueuedBuildSite>(e);
                    for (int k = 0; k < buf.Length; k++)
                    {
                        any = true;
                        var entry = buf[k];
                        if (entry.TargetBuilding == Entity.Null)
                        {
                            var bound = PlannedBuildings.ResolveOrderTarget(em, e,
                                entry.BuildingId.ToString(), entry.Position);
                            if (bound != Entity.Null) { entry.TargetBuilding = bound; buf[k] = entry; }
                        }
                        if (PlannedBuildings.IsPlan(em, entry.TargetBuilding)
                            && em.GetComponentData<FactionTag>(entry.TargetBuilding).Value == workerFac[w])
                            held.Add(entry.TargetBuilding);
                    }
                }
                if (any) continue;
                if (TransientState.Active<BuildOrder>(em, e) || TransientState.Active<RepairOrder>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                idle.Add(w);
            }

            // ── Every plan ──
            float now = SimClock.Now;
            var addOwnership = new NativeList<Entity>(Allocator.Temp);
            var addOwnershipValue = new NativeList<PlanOwnership>(Allocator.Temp);
            var adoptPlan = new NativeList<Entity>(Allocator.Temp);
            var adoptWorker = new NativeList<Entity>(Allocator.Temp);
            var cancelPlan = new NativeList<Entity>(Allocator.Temp);
            var cancelNever = new NativeList<bool>(Allocator.Temp);

            for (int p = 0; p < plans.Length; p++)
            {
                var plan = plans[p];
                bool has = em.HasComponent<PlanOwnership>(plan);
                // A plan from a game saved before the rule: it had a worker
                // once, or it would not be stuck.
                var po = has ? em.GetComponentData<PlanOwnership>(plan)
                             : new PlanOwnership { EverOwned = 1 };
                var before = po;

                if (held.Contains(plan))
                {
                    po.EverOwned = 1;
                    po.Unowned = 0;
                }
                else
                {
                    bool adopted = false;
                    if (po.EverOwned == 0)
                    {
                        int best = -1;
                        float bestD = float.MaxValue;
                        for (int i = 0; i < idle.Length; i++)
                        {
                            int w = idle[i];
                            if (w < 0 || workerFac[w] != planFacs[p].Value) continue;
                            float d = math.distancesq(workerPos[w].xz, planXfs[p].Position.xz);
                            if (d < bestD) { bestD = d; best = i; }
                        }
                        if (best >= 0)
                        {
                            adoptPlan.Add(plan);
                            adoptWorker.Add(workers[idle[best]]);
                            idle[best] = -1;
                            po.EverOwned = 1;
                            po.Unowned = 0;
                            adopted = true;
                        }
                    }
                    if (!adopted)
                    {
                        if (po.Unowned == 0) { po.Unowned = 1; po.UnownedSince = now; }
                        else if (now - po.UnownedSince >= (po.EverOwned == 0
                                     ? NeverOwnedGraceSeconds : LostOwnerGraceSeconds))
                        {
                            cancelPlan.Add(plan);
                            cancelNever.Add(po.EverOwned == 0);
                            continue;
                        }
                    }
                }

                if (!has) { addOwnership.Add(plan); addOwnershipValue.Add(po); }
                else if (po.EverOwned != before.EverOwned || po.Unowned != before.Unowned
                         || po.UnownedSince != before.UnownedSince)
                    em.SetComponentData(plan, po);
            }

            // ── Structural changes, after the scans ──
            for (int i = 0; i < addOwnership.Length; i++)
                if (em.Exists(addOwnership[i]))
                    em.AddComponentData(addOwnership[i], addOwnershipValue[i]);
            for (int i = 0; i < adoptPlan.Length; i++)
            {
                if (!PlannedBuildings.IsPlan(em, adoptPlan[i])) continue;
                BuildCommandHelper.Execute(em, adoptWorker[i], adoptPlan[i],
                    PlannedBuildings.IdOf(em, adoptPlan[i]),
                    em.GetComponentData<LocalTransform>(adoptPlan[i]).Position);
            }
            for (int i = 0; i < cancelPlan.Length; i++)
            {
                if (!PlannedBuildings.IsPlan(em, cancelPlan[i])) continue;
                PlannedBuildings.Cancel(em, cancelPlan[i], refund: true,
                    reason: TheWaningBorder.Core.Localization.Loc.T(cancelNever[i]
                        ? "no worker was free to build it"
                        : "the worker who was to build it is gone"));
            }

            workers.Dispose(); workerPos.Dispose(); workerFac.Dispose();
            held.Dispose(); idle.Dispose();
            addOwnership.Dispose(); addOwnershipValue.Dispose();
            adoptPlan.Dispose(); adoptWorker.Dispose();
            cancelPlan.Dispose(); cancelNever.Dispose();
        }
    }
}
