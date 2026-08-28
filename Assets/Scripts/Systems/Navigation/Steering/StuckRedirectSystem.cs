// StuckRedirectSystem.cs
// PROGRESS-based stuck detection + redirection (2026-07-12).
//
// The integrator's own stuck escalation catches units that stop moving at a
// contact point. It cannot catch the other failure mode the worker trace
// exposed: units ORBITING a destination they can never reach — walking tight
// circles at full speed because a footprint / crust finger / prop blocks the
// final approach. Those units move plenty; they just never get CLOSER.
//
// So this system tracks, per unit with an active DesiredDestination, the BEST
// distance-to-destination achieved so far. Motion that does not improve that
// best for StuckSeconds is a proven no-progress loop, and the unit REDIRECTS
// by job:
//
//   * Veil digger (GatherVeilCommand)  -> re-target the nearest crusted
//     vertex from where the unit actually stands (a different, reachable
//     face); if that resolves to the same blocked spot, drop the command so
//     the AI economy managers reassign the worker.
//   * Deposit miner (MinerState)       -> unassign + Idle. AI miners
//     auto-find a new deposit; a player miner idles visibly (it provably
//     could not reach the ordered deposit).
//   * Builder (BuildCommand/BuildOrder/RepairOrder) and plain movers -> first
//     a DETOUR: a short perpendicular leg so the flow field is re-sampled
//     from a different cell (routes around the blocker in the common case);
//     after MaxSoftKicks failed detours, cancel the order — stopped beats
//     orbiting forever.
//   * Combat chaser (Target set)       -> clear Target/AttackCommand; the
//     next targeting pass picks something reachable.
//
// Determinism: fixed evaluation cadence, integer/entity-order state only,
// detour side chosen by entity index parity — no wall-clock, no RNG.
// Structural changes are collected during iteration and applied after.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

/// <summary>Per-unit no-progress bookkeeping (global namespace per the
/// project's ECS component convention). Added lazily by
/// <see cref="TheWaningBorder.Systems.Navigation.StuckRedirectSystem"/>.</summary>
public struct StuckTracker : IComponentData
{
    public float3 TrackedDest;   // destination this record is measuring
    public float BestDist;       // closest we have ever been to it (XZ)
    public float NoProgressTime; // seconds since BestDist last improved
    public byte SoftKicks;       // detours already spent on this destination
    /// <summary>1 when the NEXT destination change is one this system caused
    /// (a detour leg), so the change must not be mistaken for a fresh order
    /// and refund the detour budget. Without it MaxSoftKicks was unreachable:
    /// a detour sets BestDist = MaxValue, the very next evaluation therefore
    /// registered "progress", and progress zeroed SoftKicks — so the cancel
    /// branch was dead code and a unit could detour forever.</summary>
    public byte DetourPending;
}

/// <summary>
/// Stamped on a unit whose movement order was given up on — resolved as
/// CROWDED ARRIVAL, or cancelled outright after its detour budget ran out.
/// It records the guard point that was abandoned.
///
/// <see cref="TheWaningBorder.Systems.Combat.TargetingSystem"/>'s
/// return-to-guard pass skips a unit while its GuardPoint still names this
/// position. Without that, the two systems formed a closed loop with no exit:
/// stuck recovery ends an orbit by clearing DesiredDestination and stripping
/// UserMoveOrder / AttackMoveTag, which is EXACTLY the state return-to-guard
/// reads as "idle unit away from its post" — so the leash re-issued the very
/// destination the recovery had just proven unreachable, roughly every 2.5 s,
/// forever. That was the reported "units circle at the target location".
///
/// Position-keyed rather than timed on purpose: any new order moves the guard
/// point, which re-arms the leash automatically with no expiry to tune and no
/// slow oscillation once the timer lapses.
/// </summary>
public struct GuardSuppressed : IComponentData
{
    public float3 Point;

    /// <summary>How far the guard point must move before the leash re-arms.</summary>
    public const float Epsilon = 0.5f;
}

/// <summary>Stamped on a DEPOSIT entity when a miner provably could not
/// reach it (no-progress redirect fired). The miner pickers (AI allocator
/// + depletion auto-find) skip marked nodes until <see cref="Until"/>, so
/// the economy layer stops bouncing workers against the same blocked node
/// forever — the redirect used to unassign the miner only for the AI to
/// re-issue the exact same unreachable node, an infinite circling loop.
/// Sim-time based, so it self-heals: when the blocking structure is gone,
/// the mark expires and the node is minable again.</summary>
public struct UnreachableMark : IComponentData
{
    public double Until;
}

namespace TheWaningBorder.Systems.Navigation
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class StuckRedirectSystem : SystemBase
    {
        /// <summary>Evaluation cadence — progress is judged in coarse steps
        /// so noise from separation jitter never counts as progress.</summary>
        private const float EvalInterval = 0.5f;
        /// <summary>A unit must beat its best distance by this much for the
        /// step to count as progress (orbiting oscillates less than this).</summary>
        private const float MinProgress = 0.6f;
        /// <summary>No-progress time that declares the unit stuck.</summary>
        private const float StuckSeconds = 5f;
        /// <summary>Within this range no-progress is judged on the short
        /// fuse and resolves as CROWDED ARRIVAL for plain movers (the
        /// destination is occupied; physics forbids the 0.5 m arrival).
        ///
        /// Public because TargetingSystem's GuardReturnThreshold is derived
        /// from it: the leash MUST NOT fire inside the band this system calls
        /// "arrived", or the two systems disagree about what arrival means and
        /// fight each other over the unit forever.</summary>
        public const float ArrivalSkip = 3f;
        /// <summary>No-progress fuse when already near the goal.</summary>
        private const float NearGoalStuckSeconds = 2.5f;
        /// <summary>Length of a perpendicular detour leg.</summary>
        private const float DetourDist = 6f;
        /// <summary>Failed detours per destination before the order drops.</summary>
        private const byte MaxSoftKicks = 2;
        /// <summary>Search radius for re-targeting a veil digger.</summary>
        private const float VeilRetargetRadius = 16f;
        /// <summary>How long an unreachable deposit stays skipped by the
        /// miner pickers. Long enough that workers stop orbiting it, short
        /// enough to retry after the map changes (blocker razed, crust
        /// receded).</summary>
        private const float UnreachableMarkSeconds = 45f;

        private SimCadence.Periodic _acc;
        private EntityQuery _needQuery;

        protected override void OnCreate()
        {
            _needQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, DesiredDestination>()
                .WithNone<StuckTracker>()
                .Build();
        }

        protected override void OnUpdate()
        {
            // Lazy-add trackers (deferred structural change, same pattern as
            // FlowFollowSystem's component bootstrap).
            var needQuery = _needQuery;
            if (!needQuery.IsEmpty)
            {
                var bootEcb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                    .CreateCommandBuffer(World.Unmanaged);
                using var ents = needQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                    bootEcb.AddComponent(ents[i], new StuckTracker { BestDist = float.MaxValue });
            }

            float step = _acc.DueStep(SystemAPI.Time.DeltaTime, EvalInterval);
            if (step <= 0f) return;

            var em = EntityManager;

            // Pass 1: measure progress; collect stuck units. No structural
            // changes inside the iteration.
            var stuck = new NativeList<Entity>(16, Allocator.Temp);
            var nearStuck = new NativeList<Entity>(16, Allocator.Temp);

            foreach (var (tracker, xf, dd, entity) in SystemAPI
                .Query<RefRW<StuckTracker>, RefRO<LocalTransform>, RefRO<DesiredDestination>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                ref var t = ref tracker.ValueRW;

                if (dd.ValueRO.Has == 0)
                {
                    // No live intent — reset so the next order starts clean.
                    t.BestDist = float.MaxValue;
                    t.NoProgressTime = 0f;
                    t.SoftKicks = 0;
                    continue;
                }

                float3 dest = dd.ValueRO.Position;

                // New destination (issued order, chase update, detour) —
                // restart the measurement. A detour is part of the SAME rescue
                // attempt and must KEEP its SoftKicks; only a destination
                // change we did not cause is a fresh order deserving a fresh
                // detour budget. (Command systems that re-issue the same site
                // every tick don't trip this at all — the position matches.)
                float ddx = dest.x - t.TrackedDest.x;
                float ddz = dest.z - t.TrackedDest.z;
                if (ddx * ddx + ddz * ddz > 1f)
                {
                    t.TrackedDest = dest;
                    t.BestDist = float.MaxValue;
                    t.NoProgressTime = 0f;
                    if (t.DetourPending != 0) t.DetourPending = 0;
                    else t.SoftKicks = 0;
                }

                float dx = dest.x - xf.ValueRO.Position.x;
                float dz = dest.z - xf.ValueRO.Position.z;
                float dist = math.sqrt(dx * dx + dz * dz);

                if (dist < t.BestDist - MinProgress)
                {
                    // Genuine progress — closer than we have ever been.
                    // SoftKicks is deliberately NOT refunded here: the reset
                    // above owns that, keyed on where the destination change
                    // came from. Refunding on progress made the budget
                    // unspendable, because the first evaluation after any
                    // destination change always looks like progress (BestDist
                    // was just reset to MaxValue).
                    t.BestDist = dist;
                    t.NoProgressTime = 0f;
                    continue;
                }

                // CROWDED ARRIVAL (rally jitter fix, 2026-07-12): near the
                // goal, no-progress means something is PARKED on the exact
                // destination — separation physically forbids ever reaching
                // the 0.5 m arrival window, so the order can never complete.
                // Short fuse here (the unit is already where it needs to be);
                // plain movers get declared ARRIVED in pass 2, work-command
                // holders get their normal redirect (e.g. a digger re-aims
                // at a free crust face).
                bool nearGoal = dist <= ArrivalSkip;

                t.NoProgressTime += step;
                float fuse = nearGoal ? NearGoalStuckSeconds : StuckSeconds;
                if (t.NoProgressTime >= fuse)
                {
                    t.NoProgressTime = 0f;
                    t.BestDist = float.MaxValue; // fresh measurement post-redirect
                    if (nearGoal) nearStuck.Add(entity);
                    else stuck.Add(entity);
                }
            }

            // Pass 2: redirect (structural changes allowed now). VeilField +
            // sim time fetched once here — SystemAPI is not available in
            // plain helper methods.
            bool hasVeil = SystemAPI.TryGetSingleton<VeilField>(out var veilField);
            double now = SystemAPI.Time.ElapsedTime;

            for (int i = 0; i < stuck.Length; i++)
                Redirect(em, stuck[i], hasVeil, veilField, now);

            // Pass 2b: near-goal stuck. Units carrying a WORK intent take the
            // normal redirect (retarget / unassign). Plain movers are DONE:
            // they are as close to the ordered point as physics allows —
            // declare arrival instead of orbiting the occupant forever.
            for (int i = 0; i < nearStuck.Length; i++)
            {
                Entity e = nearStuck[i];
                if (!em.Exists(e)) continue;

                // A MINER that stalled within arm's reach of its deposit is
                // almost always just contending for a stand slot with another
                // worker — not facing an unreachable node. The full miner
                // redirect is far too heavy for that: it unassigns the worker
                // AND marks the deposit unreachable for 45 s, so the worker
                // visibly gives up a step from the ore.
                //
                // Clear only the DESTINATION and keep the assignment. The
                // mining systems' own re-pick then puts it on a free slot on
                // another side of the node (see MiningReach). If that fails
                // too, the worker goes Idle by its own route and the next fuse
                // — the far-from-goal one — still has the harsh path.
                if (em.HasComponent<MinerState>(e)
                    && em.GetComponentData<MinerState>(e).State == MinerWorkState.MovingToDeposit)
                {
                    ClearDest(em, e);
                    continue;
                }

                bool hasWork = em.HasComponent<BuildCommand>(e)
                    || em.HasComponent<BuildOrder>(e)
                    || em.HasComponent<RepairOrder>(e)
                    || (em.HasComponent<Target>(e)
                        && em.GetComponentData<Target>(e).Value != Entity.Null);

                if (hasWork)
                {
                    Redirect(em, e, hasVeil, veilField, now);
                    continue;
                }

                // Crowded arrival: close enough + provably can't get closer.
                if (em.HasComponent<UserMoveOrder>(e)) em.RemoveComponent<UserMoveOrder>(e);
                if (em.HasComponent<AttackMoveTag>(e)) em.RemoveComponent<AttackMoveTag>(e);
                ClearDest(em, e);
                // ...and tell the leash so, or return-to-guard reads the state
                // we just produced as "idle unit off its post" and marches the
                // unit straight back into the pile-up. See GuardSuppressed.
                SuppressGuard(em, e);
                var t2 = em.GetComponentData<StuckTracker>(e);
                t2.SoftKicks = 0;
                em.SetComponentData(e, t2);
            }

            stuck.Dispose();
            nearStuck.Dispose();
        }

        // ─────────────────────────────────────────────────────────────

        private void Redirect(EntityManager em, Entity entity, bool hasVeil, VeilField field, double now)
        {
            if (!em.Exists(entity)) return;
            float3 pos = em.GetComponentData<LocalTransform>(entity).Position;

            // The veil-digger retarget and the deposit-miner unassign lived
            // here. Both are unreachable now that worker gathering is gone
            // (docs/Design/Regions.md §4): nothing issues GatherVeilCommand and
            // MinerState never leaves Idle, so neither branch could ever be
            // entered. Removed rather than left as dead weight -- but note
            // ClearMiner survives, it is still called from the stuck path above.

            // ── Combat chaser: drop the unreachable target, re-acquire ──
            if (em.HasComponent<Target>(entity)
                && em.GetComponentData<Target>(entity).Value != Entity.Null)
            {
                em.SetComponentData(entity, new Target { Value = Entity.Null });
                if (em.HasComponent<AttackCommand>(entity))
                    em.RemoveComponent<AttackCommand>(entity);
                ClearDest(em, entity);
                return;
            }

            // ── Builder / plain mover: detour first, cancel after ──
            var tracker = em.GetComponentData<StuckTracker>(entity);
            if (tracker.SoftKicks < MaxSoftKicks)
            {
                tracker.SoftKicks++;
                // Mark the destination change below as OURS so pass 1 keeps
                // the budget we just spent instead of refunding it.
                tracker.DetourPending = 1;
                em.SetComponentData(entity, tracker);

                // Perpendicular leg — deterministic side from entity index.
                // Re-approaching from a different cell gives the flow field
                // (and the LOS check) a different answer than the orbit line.
                float3 dest = em.HasComponent<DesiredDestination>(entity)
                    ? em.GetComponentData<DesiredDestination>(entity).Position : pos;
                float dx = dest.x - pos.x, dz = dest.z - pos.z;
                float len = math.sqrt(dx * dx + dz * dz);
                if (len < 0.01f) { ClearDest(em, entity); return; }
                float inv = 1f / len;
                float side = (entity.Index & 1) == 0 ? 1f : -1f;
                // Perp of (dx,dz) is (-dz,dx); step back slightly too so the
                // detour leaves the contact/orbit ring.
                float3 detour = pos + new float3(
                    (-dz * inv * side - dx * inv * 0.3f) * DetourDist,
                    0f,
                    (dx * inv * side - dz * inv * 0.3f) * DetourDist);
                SetDest(em, entity, detour);
                return;
            }

            // Detours spent — cancel the order outright. AI managers see an
            // idle worker and reassign; a player unit stops visibly instead
            // of orbiting forever.
            if (em.HasComponent<BuildCommand>(entity)) em.RemoveComponent<BuildCommand>(entity);
            if (em.HasComponent<BuildOrder>(entity)) em.RemoveComponent<BuildOrder>(entity);
            if (em.HasComponent<RepairOrder>(entity)) em.RemoveComponent<RepairOrder>(entity);
            if (em.HasComponent<UserMoveOrder>(entity)) em.RemoveComponent<UserMoveOrder>(entity);
            if (em.HasComponent<AttackMoveTag>(entity)) em.RemoveComponent<AttackMoveTag>(entity);
            ClearDest(em, entity);
            // The order is abandoned for good — the leash must not resurrect
            // it on the next tick (that is what made the cancel pointless even
            // once it became reachable).
            SuppressGuard(em, entity);

            var reset = em.GetComponentData<StuckTracker>(entity);
            reset.SoftKicks = 0;
            reset.DetourPending = 0;
            em.SetComponentData(entity, reset);
        }

        /// <summary>Record the guard point this unit has given up reaching, so
        /// TargetingSystem's return-to-guard leaves it alone until it gets a
        /// genuinely new order (which moves the guard point). See
        /// <see cref="GuardSuppressed"/>.</summary>
        private static void SuppressGuard(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<GuardPoint>(entity)) return;
            var gp = em.GetComponentData<GuardPoint>(entity);
            if (gp.Has == 0) return;

            var mark = new GuardSuppressed { Point = gp.Position };
            if (em.HasComponent<GuardSuppressed>(entity))
                em.SetComponentData(entity, mark);
            else
                em.AddComponentData(entity, mark);
        }

        private static void ClearMiner(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<MinerState>(entity)) return;
            var ms = em.GetComponentData<MinerState>(entity);
            ms.State = MinerWorkState.Idle;
            ms.AssignedDeposit = Entity.Null;
            em.SetComponentData(entity, ms);
        }

        private static void SetDest(EntityManager em, Entity entity, float3 dest)
        {
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Position = dest, Has = 1 });
        }

        private static void ClearDest(EntityManager em, Entity entity)
        {
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Has = 0 });
        }
    }
}
