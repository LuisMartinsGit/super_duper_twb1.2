// FormationGroupSystem.cs
// AoE4-style formation travel (GDC 2022 "Pathing in Age of Empires IV",
// slide 32; docs/Design/Navigation_And_Formations.md §2):
//
//   1. The group's VIRTUAL LEADER follows the flow toward the destination
//      (LOS bearing on open ground, whole-map goal-field direction around
//      blockers) at the group speed (slowest member).
//   2. Formation SPOTS ride around the leader (leader-local offsets, laid
//      out perpendicular to the travel direction).
//   3. Each member steers to its moving spot: this system overrides the
//      member's FlowDesiredDir before SteeringSystem blends separation /
//      avoidance on top. A member with NO line of sight to its spot falls
//      back to its own goal flow toward its final slot destination.
//   4. Members behind their spot get the +40% catch-up speed; members in
//      place march at the group speed.
//   5. Combat dissolves membership (a unit that acquires a Target leaves
//      the group and fights at its own speed). Arrival settles members
//      into their spots and the group dissolves.
//
// Runs on the main thread (group counts are tiny — one entity per active
// group order); per-member work is O(members) with O(grid-ray) LOS checks.
//
// Determinism: reads only sim state, fixed iteration order (query chunk
// order + member buffer order), integer Bresenham LOS, no wall-clock and
// no randomness. The goal-field integration it samples runs synchronously
// in GoalFlowFieldSystem earlier in the tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFollowSystem))]
    [UpdateBefore(typeof(SteeringSystem))]
    public partial struct FormationGroupSystem : ISystem
    {
        /// <summary>A member this close to its spot counts as "in place"
        /// and simply marches with the leader.
        ///
        /// 0.6 -> 0.15 (2026-08-28). This is the formation's slop: inside it a
        /// member stops correcting toward its slot and just marches, so 0.6 m
        /// let every unit ride two-thirds of a metre off its mark and hold it
        /// there for the whole march — and, since the group dissolves on
        /// arrival, stop there too. Eight units each parked somewhere in a
        /// 0.6 m disc is not a formation, it is a cluster; the shape has to be
        /// tight enough that "in formation" and "arrived" look the same.
        /// Correcting a 0.15 m error costs nothing now that formation-mates no
        /// longer shove each other out of position (see SteeringSystem).</summary>
        private const float InPlaceDistance = 0.15f;

        /// <summary>
        /// How hard a member is pulled back onto its spot, per metre of error
        /// (1/s). The member is commanded its SPOT'S velocity plus this times
        /// the offset, so the error decays as exp(-gain * t) — about a third of
        /// a metre left after half a second, and no overshoot to orbit around.
        ///
        /// Replaces a ladder of four speed tiers plus a lateral-correction gain
        /// plus a don't-steer-backwards rule, all of which were approximations
        /// to this one law: behind adds speed, ahead removes it, abeam angles
        /// the heading.
        /// </summary>
        private const float SpotCorrectionGain = 2f;

        /// <summary>
        /// How far behind its spot a member has to be to count as OUT OF
        /// FORMATION. Set to the in-place tolerance, so there is NO GAP between
        /// "not in position" and "the leader eases off".
        ///
        /// It was the catch-up trigger (1.5 m), which left a dead band no
        /// mechanism could close. Inside it a member is past the in-place
        /// tolerance — so it steers at its spot rather than marching with the
        /// leader — but it is not yet sprinting, and the leader is not yet
        /// slowing. Member and spot therefore both travel at exactly the group
        /// speed and the gap is frozen: the squad marches permanently a few
        /// tens of centimetres shy of its formation, with nothing in the system
        /// able to take up the slack.
        ///
        /// At this threshold the loop always closes. Any member outside 0.15 m
        /// drops the leader to 90%, so the member gains on its spot at a tenth
        /// of the group speed — about half a second to recover a slot-width —
        /// and full speed resumes the moment everyone is back in place.
        ///
        /// The 0.9/1.0 toggle around the threshold is deliberate and does not
        /// need hysteresis: it acts on the leader's SPEED, which is integrated
        /// into a position, so a toggling input still produces smooth motion.
        /// The visible result is a formation that holds its shape and a leader
        /// that averages a few per cent below full speed while it does.
        /// </summary>
        /// <summary>Offset at which the leader STARTS easing. Three times the
        /// in-place tolerance: comfortably outside the ~0.18 m the formation
        /// settles at, so ordinary marching never trips it.</summary>
        private const float OutOfFormationEngage = InPlaceDistance * 3f;

        /// <summary>Offset at which it STOPS easing. Above the settle band, or
        /// the ease latches on and never lets go — which is exactly what a
        /// single threshold did.</summary>
        private const float OutOfFormationRelease = InPlaceDistance * 1.5f;

        /// <summary>Leader speed while anybody is out of formation. Enough to
        /// let a catching-up member gain, small enough that a formation is
        /// never visibly punished for one straggler.</summary>
        private const float OutOfFormationLeaderSpeed = 0.9f;

        /// <summary>
        /// Slowest the leader may travel while wheeling, as a fraction of the
        /// group speed. Forward speed eases by cos(heading error) to buy the
        /// outer flank the arc length it needs — but cos reaches zero at a
        /// right angle, which would park the whole army for the five seconds a
        /// 90-degree corner takes. Half speed is the floor: measured against
        /// the flank budget a 15-unit block needs 3.2 m/s of a 7.0 m/s ceiling
        /// to hold its slots through a 90-degree turn at half speed, so the
        /// shape survives and the formation keeps moving through the corner
        /// instead of stopping to negotiate it.
        /// </summary>
        private const float WheelSpeedFloor = 0.5f;

        private EntityQuery _groupQuery;

        public void OnCreate(ref SystemState state)
        {
            _groupQuery = state.GetEntityQuery(ComponentType.ReadWrite<FormationGroup>());
            state.RequireForUpdate(_groupQuery);
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Main-thread management pass; make sure the flow jobs that
            // write FlowDesiredDir this tick are not still in flight.
            state.CompleteDependency();

            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var cost = SystemAPI.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated) return;

            bool hasGoalCache = SystemAPI.TryGetSingleton<GoalFlowFieldCache>(out var goalCache);
            bool hasDirTable = SystemAPI.TryGetSingleton<DirectionTableSingleton>(out var dirTable);

            using var groups = _groupQuery.ToEntityArray(Allocator.Temp);
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var groupEntity = groups[gi];
                var g = em.GetComponentData<FormationGroup>(groupEntity);

                // ── Prune / detach members (two-phase: decide on a
                // snapshot, THEN apply structural changes — Detach removes
                // components, which would invalidate a live buffer). ──
                var bufferRO = em.GetBuffer<FormationMember>(groupEntity);
                var snapshot = new NativeArray<FormationMember>(bufferRO.Length, Allocator.Temp);
                bufferRO.AsNativeArray().CopyTo(snapshot);

                var keep = new NativeList<FormationMember>(snapshot.Length, Allocator.Temp);
                var toDetach = new NativeList<Entity>(snapshot.Length, Allocator.Temp);
                float slowest = float.MaxValue;

                // Worst member lag against the PRE-advance leader pose. Drives
                // the leader tether below: the leader may only travel as fast
                // as the formation it is leading can actually follow.
                float3 preRight = math.cross(new float3(0f, 1f, 0f), g.Facing);
                float maxLag = 0f;
                // Worst member->spot DISTANCE this tick, in any direction.
                float maxOffset = 0f;
                Entity worstLaggard = Entity.Null;

                for (int i = 0; i < snapshot.Length; i++)
                {
                    var m = snapshot[i];
                    var u = m.Unit;

                    if (u == Entity.Null || !em.Exists(u)) continue;
                    if (em.HasComponent<DeathAnimationState>(u)) { toDetach.Add(u); continue; }

                    // Re-ordered into another group / individually: the
                    // member state no longer points here — just drop it.
                    if (!em.HasComponent<FormationMemberState>(u)
                        || em.GetComponentData<FormationMemberState>(u).Group != groupEntity)
                        continue;

                    // Combat dissolves the formation (AoE4): the unit
                    // fights individually at its own speed.
                    if (em.HasComponent<Target>(u)
                        && em.GetComponentData<Target>(u).Value != Entity.Null)
                    {
                        toDetach.Add(u);
                        continue;
                    }

                    bool hasDest = em.HasComponent<DesiredDestination>(u)
                        && em.GetComponentData<DesiredDestination>(u).Has != 0;

                    if (!hasDest)
                    {
                        // Order finished (settled into the final slot) or
                        // cancelled by stuck recovery — either way the unit
                        // is done travelling with the group.
                        toDetach.Add(u);
                        continue;
                    }

                    keep.Add(m);
                    if (em.HasComponent<MoveSpeed>(u))
                    {
                        float sp = em.GetComponentData<MoveSpeed>(u).Value;
                        if (sp > 0f && sp < slowest) slowest = sp;
                    }

                    if (em.HasComponent<LocalTransform>(u))
                    {
                        float3 sp0 = g.LeaderPos + preRight * m.Slot.x + g.Facing * m.Slot.y;
                        float3 p0 = em.GetComponentData<LocalTransform>(u).Position;
                        // LAG IS BEHIND-NESS, not distance. Measured along the
                        // travel direction and clamped at zero, so only members
                        // the leader is actually leaving behind hold it back.
                        //
                        // As raw distance it also counted members AHEAD of their
                        // spot and members merely offset SIDEWAYS from it — and
                        // on the tick an order is issued that is most of the
                        // group, because the spots appear around the centroid.
                        // lagScale reaches 0 at 6 m of lag, so a block of ten
                        // deeper than that pinned the leader at a standstill
                        // until the whole formation had closed up on the
                        // centroid. That is the "form up before you move" the
                        // player sees, and under a stream of kite orders it
                        // meant the group never departed at all.
                        float lx = sp0.x - p0.x, lz = sp0.z - p0.z;
                        float lag = lx * g.Facing.x + lz * g.Facing.z;
                        if (lag > maxLag) { maxLag = lag; worstLaggard = u; }

                        // OUT OF POSITION IS NOT THE SAME QUESTION AS BEHIND.
                        // The release fuse above wants behind-ness — it is
                        // deciding whether the leader is leaving somebody
                        // behind. "Should the leader ease off so the shape can
                        // tighten" wants the honest distance, because a member
                        // that is 40 cm off to the SIDE is just as out of
                        // formation and reads as zero behind-ness. Slowing for
                        // it is what buys it the spare speed to slide back into
                        // its column; without this the squad marches
                        // permanently a little askew and nothing corrects it.
                        float offset = math.sqrt(lx * lx + lz * lz);
                        if (offset > maxOffset) maxOffset = offset;
                    }
                }
                snapshot.Dispose();

                for (int i = 0; i < toDetach.Length; i++)
                    Detach(em, toDetach[i]);
                toDetach.Dispose();

                if (keep.Length == 0)
                {
                    keep.Dispose();
                    em.DestroyEntity(groupEntity);
                    continue;
                }

                var buffer = em.GetBuffer<FormationMember>(groupEntity);
                if (keep.Length != buffer.Length)
                {
                    buffer.Clear();
                    for (int i = 0; i < keep.Length; i++) buffer.Add(keep[i]);
                }

                if (slowest > 0f && slowest != float.MaxValue)
                    g.GroupSpeed = slowest;

                // ── Leader tether ──────────────────────────────────────────
                // The virtual leader pays none of the costs its members pay:
                // no separation, no obstacle slide, no turn-rate clamp, and —
                // crucially — no terrain cost and no BorderDebuff.SpeedPenalty,
                // all of which UnitIntegratorSystem DOES apply to the members.
                // At equal nominal speed the leader therefore always pulls
                // ahead, the members lose line of sight to their spots, fall
                // back to their own goal flow, and the formation stops being a
                // formation. Scale the leader's step by how far the group has
                // actually fallen behind. This also removes the need for a
                // separate "wait while we form up" rule: on tick 1 members can
                // be a whole CohesionRadius from their spots, so the leader
                // starts slow and accelerates as the shape comes together.
                // ONE STEP, NOT A RAMP: while anybody is out of formation the
                // leader walks at 90%, and the moment nobody is it walks at
                // full speed.
                //
                // This replaces a ramp that scaled from 1 down to 0 across
                // 3-6 m of lag. That ramp existed because members used to start
                // a long way from their spots — the slot block hung a full
                // pitch behind the leader, so even a correctly-formed squad had
                // every member 2 m out on tick one and the leader had to crawl
                // while they closed. With the block centred (see
                // FormationMoveCommandHelper) a formed squad starts ON its
                // spots, so the ramp was solving a problem that no longer
                // exists — and solving it by stalling the leader outright,
                // which is what made a group look like it was refusing to move.
                //
                // A flat 10% is enough because a lagging member is already
                // running the +40% catch-up: it closes at roughly half the
                // group's speed, which recovers a slot-width in about a second.
                // The pathological case — a member that genuinely cannot keep
                // up — is not this rule's job; TetherReleaseTicks drops the
                // worst laggard once the lag stops improving, so the formation
                // heals instead of being held hostage.
                // HYSTERESIS, because a single threshold sat inside the
                // formation's own noise floor.
                //
                // Measured over a 20 s run: the squad settles with a median
                // offset of 0.09 m and a worst-member offset pinned at
                // 0.178 m — stable, not oscillating — against a 0.15 m
                // trigger. So the ease never released, every member was
                // commanded 0.9x nominal for the entire march, and the group
                // moved 10% slower than it should have. Achieved speed tracked
                // commanded speed exactly (ratio 1.000), so nothing downstream
                // was at fault: the formation was simply asking for less.
                //
                // The 3 cm that pinned it is a measurement disagreement, not a
                // real displacement — this pass measures the spot with the
                // facing from BEFORE the leader's turn, while the steering
                // below uses the facing from after it. Chasing that to zero is
                // the wrong fix; a control threshold has to sit outside the
                // noise it is measuring, not exactly on it.
                //
                // Engage well clear of the settle band, release comfortably
                // above it, so an ordinary march runs at full speed and a
                // genuine straggler still slows the leader until it is back.
                bool easing = g.Easing != 0;
                if (maxOffset > OutOfFormationEngage) easing = true;
                else if (maxOffset <= OutOfFormationRelease) easing = false;
                g.Easing = (byte)(easing ? 1 : 0);

                float lagScale = easing ? OutOfFormationLeaderSpeed : 1f;

                // Only a group that is FAILING to close up counts toward the
                // release fuse — a formation still forming has a large lag that
                // is steadily shrinking, and must not be torn apart for it.
                Entity pendingDrop = Entity.Null;
                if (maxLag < g.BestLag - FormationGroup.TetherProgressEpsilon)
                {
                    g.BestLag = maxLag;
                    g.TetherTicks = 0;
                }
                else if (lagScale <= 0.01f)
                {
                    g.TetherTicks = (byte)math.min(g.TetherTicks + 1, 255);
                    if (g.TetherTicks >= FormationGroup.TetherReleaseTicks
                        && worstLaggard != Entity.Null)
                    {
                        // One wedged member would otherwise freeze the whole
                        // group at a standstill. Drop it; it finishes to its
                        // own slot independently (design §2.4 outlier rule).
                        pendingDrop = worstLaggard;
                        g.TetherTicks = 0;
                        g.BestLag = float.MaxValue;
                        lagScale = 1f;
                    }
                }

                // ── Advance the virtual leader. ──
                // What the leader ACTUALLY did this tick, published for the
                // member steering below: every spot rides on the leader, so a
                // spot's velocity is exactly this linear step plus this
                // rotation applied to the arm out to it.
                float leaderLinSpeed = 0f;
                float appliedOmega = 0f;
                if (g.State == FormationGroup.StateMoving)
                {
                    float3 toDest = g.Destination - g.LeaderPos;
                    toDest.y = 0f;
                    float destDistSq = math.lengthsq(toDest);

                    if (destDistSq <= FormationGroup.ArriveDistance * FormationGroup.ArriveDistance)
                    {
                        g.LeaderPos = new float3(g.Destination.x, 0f, g.Destination.z);
                        g.State = FormationGroup.StateArrived;
                    }
                    else
                    {
                        float3 dir = ResolveLeaderDir(in grid, in cost, hasGoalCache, in goalCache,
                            hasDirTable, in dirTable, g.LeaderPos, g.Destination, g.FactionIdx);

                        // ── WHEEL, DO NOT CRAB. ──
                        //
                        // The leader used to step along the raw flow direction
                        // while its facing lerped toward that direction
                        // separately. Those are two different headings, so for
                        // the whole of a turn the lattice pointed one way and
                        // the group travelled another: the formation slid
                        // SIDEWAYS across the ground, dragging every spot
                        // laterally through the units standing in them. That is
                        // the corner mess. It never converged either, because
                        // the lerp rate was a flat 4/s — a 45-degree corner in
                        // about a fifth of a second, which asks a unit ten
                        // metres out on the flank for roughly 39 m/s to hold
                        // its slot.
                        //
                        // A formation moves along its OWN facing, and turns at a
                        // rate its outer flank can physically follow. The two
                        // are the same heading by construction, so the shape
                        // rotates rigidly about the leader and every member
                        // walks a clean arc: the inner file short and slow, the
                        // outer file long and fast, exactly as ranks wheel.
                        float turnScale = 1f;
                        if (math.lengthsq(dir) > 1e-6f)
                        {
                            float cosErr = math.clamp(
                                dir.x * g.Facing.x + dir.z * g.Facing.z, -1f, 1f);
                            float sinErr = g.Facing.x * dir.z - g.Facing.z * dir.x;
                            float err = math.atan2(sinErr, cosErr);

                            // The turn rate the formation can actually hold: a
                            // member has (CatchUpMultiplier - 1) of its speed
                            // spare, and the outer slot is dragged omega*Radius
                            // sideways. Wide armies wheel slowly; that is not a
                            // limitation to tune away, it is what keeps the
                            // flank attached to the formation.
                            float headroom = g.GroupSpeed
                                * (FormationGroup.CatchUpMultiplier - 1f);
                            float omega = g.Radius > 0.5f
                                ? headroom / g.Radius
                                : FormationGroup.MaxTurnRate;
                            omega = math.clamp(omega, FormationGroup.MinTurnRate,
                                FormationGroup.MaxTurnRate);

                            if (math.abs(err) > FormationGroup.WheelSnapAngle)
                            {
                                // Too sharp to wheel: re-form on the new
                                // bearing instead. See WheelSnapAngle — an
                                // about-face pivoted at flank-limited rate is
                                // ten seconds of an army turning on the spot
                                // while it is being shot at.
                                g.Facing = math.normalizesafe(dir, g.Facing);
                                cosErr = 1f;
                            }
                            else
                            {
                                float turn = math.clamp(err, -omega * dt, omega * dt);
                                if (dt > 1e-6f) appliedOmega = turn / dt;

                                float sn = math.sin(turn), cs = math.cos(turn);
                                float3 f = new float3(
                                    g.Facing.x * cs - g.Facing.z * sn, 0f,
                                    g.Facing.x * sn + g.Facing.z * cs);
                                f = math.normalizesafe(f, g.Facing);
                                if (math.lengthsq(f) > 1e-6f) g.Facing = f;
                            }

                            // Slow down through the turn. A body of troops that
                            // keeps full speed into a corner throws its outer
                            // flank; cutting forward speed by cos(error) buys
                            // the flank the arc length it needs, and at a right
                            // angle or worse the group simply pivots.
                            turnScale = math.max(WheelSpeedFloor, cosErr);
                        }

                        float destDist = math.sqrt(destDistSq);
                        float stepLen = math.min(
                            g.GroupSpeed * lagScale * turnScale * dt, destDist);
                        float3 next = g.LeaderPos + g.Facing * stepLen;

                        if (IsLeaderCellPassable(in grid, in cost, next, g.FactionIdx))
                        {
                            g.LeaderPos = next;
                            g.StallTicks = 0;
                            if (dt > 1e-6f) leaderLinSpeed = stepLen / dt;
                        }
                        else
                        {
                            // Held this tick, so the spots are not translating
                            // either; drop the rotation too rather than
                            // sweeping the lattice around a stationary leader.
                            appliedOmega = 0f;

                            // Blocked: hold this tick; the goal field routes
                            // the leader around the blocker on following
                            // ticks. A leader stuck for good releases the
                            // group so members finish on their own flow
                            // instead of hovering around a dead spot layout.
                            g.StallTicks = (byte)math.min(g.StallTicks + 1, 255);
                            if (g.StallTicks >= FormationGroup.StallReleaseTicks)
                                g.State = FormationGroup.StateArrived;
                        }
                    }
                }

                // ── Arrival DISSOLVES the group (design §2.8). ─────────────
                // The leader has reached the destination, so every member's
                // own DesiredDestination — its final slot — already IS the
                // frozen spot. Keeping the group alive past this point kept
                // FormationMemberState / FormationSpeedOverride on units that
                // the system no longer steers, which (a) leaked the group
                // entity whenever a member could not close the last 0.5 m, and
                // (b) held SteeringSystem's formation exemption open during the
                // settle, when the arrival damping is exactly what's wanted.
                if (g.State == FormationGroup.StateArrived)
                {
                    var settling = em.GetBuffer<FormationMember>(groupEntity);
                    var settled = new NativeArray<Entity>(settling.Length, Allocator.Temp);
                    for (int i = 0; i < settling.Length; i++) settled[i] = settling[i].Unit;
                    for (int i = 0; i < settled.Length; i++) Detach(em, settled[i]);
                    settled.Dispose();
                    keep.Dispose();
                    em.DestroyEntity(groupEntity);
                    continue;
                }

                // ── Steer members to their moving spots. ──
                float3 right = math.cross(new float3(0f, 1f, 0f), g.Facing);
                float catchUpSpeed = g.GroupSpeed * FormationGroup.CatchUpMultiplier;

                // RESTORE SPEED CONTROL BEFORE THE STEER LOOP, not inside it.
                //
                // The integrator strips FormationSpeedOverride the moment a
                // unit satisfies arrival at its slot — which happens routinely
                // mid-march — and the write below is a plain Set. A member that
                // lost the component therefore fell back to its OWN MoveSpeed
                // and the formation lost every lever it has over it: no group
                // speed, no catch-up, no ahead-of-spot easing.
                //
                // For a mixed army that is not subtle. Archers move at 5.2 and
                // Spearmen at 5.0, so a released archer runs 4% faster than the
                // shape it belongs to, walks past its own spot, and — forbidden
                // from steering backwards — keeps going straight through the
                // shield wall it is meant to stand behind.
                //
                // AddComponent is a STRUCTURAL CHANGE and invalidates the
                // DynamicBuffer handle, so it cannot happen inside the loop
                // that reads and writes `buffer`. Restoring first means the
                // loop below only ever Sets, and the handle stays valid.
                {
                    var pre = em.GetBuffer<FormationMember>(groupEntity);
                    var missing = new NativeList<Entity>(pre.Length, Allocator.Temp);
                    for (int i = 0; i < pre.Length; i++)
                    {
                        var pu = pre[i].Unit;
                        if (em.Exists(pu) && !em.HasComponent<FormationSpeedOverride>(pu))
                            missing.Add(pu);
                    }
                    for (int i = 0; i < missing.Length; i++)
                        em.AddComponentData(missing[i],
                            new FormationSpeedOverride { Value = g.GroupSpeed });
                    missing.Dispose();
                }

                buffer = em.GetBuffer<FormationMember>(groupEntity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    var u = buffer[i].Unit;

                    if (!em.HasComponent<LocalTransform>(u)
                        || !em.HasComponent<FlowDesiredDir>(u)) continue;

                    float2 s = buffer[i].Slot;
                    float3 spot = g.LeaderPos + right * s.x + g.Facing * s.y;
                    float3 pos = em.GetComponentData<LocalTransform>(u).Position;
                    float3 toSpot = spot - pos;
                    toSpot.y = 0f;

                    // ── A MEMBER TRACKS ITS SPOT'S VELOCITY, NOT ITS
                    //    POSITION. ──
                    //
                    // Every version of this until now aimed the member AT its
                    // spot and picked a speed off a ladder of cases. Pure
                    // pursuit only works while the target moves the way the
                    // pursuer does — true for the body of a formation marching
                    // in a straight line, false for everything else.
                    //
                    // A wing sits abeam the leader, so in a wheel its spot
                    // travels almost entirely SIDEWAYS. Told to drive along the
                    // formation's heading at full speed whenever it was inside
                    // the in-place band, a wing rider left its spot at once,
                    // got yanked back by the seek, re-entered the band, drove
                    // off along the heading again — and, unable to reverse
                    // inside the integrator's turn-rate clamp, traced that
                    // cycle out as a circle. The cataphracts rode in loops on
                    // every corner. Outside the band it was no better: pure
                    // pursuit of a target moving crosswise orbits it.
                    //
                    // The spot's velocity is known exactly — the leader's own
                    // step, plus the tick's rotation applied to the arm from
                    // leader to spot. Command that, plus a proportional pull
                    // toward the spot, and the error decays as
                    // exp(-SpotCorrectionGain * t) with no overshoot to orbit
                    // around.
                    //
                    // It also subsumes the whole ladder. Behind, toSpot points
                    // forward and adds speed; ahead, it points back and removes
                    // it; abeam, it angles the heading. Catch-up, the
                    // ahead-of-spot ease and the don't-steer-backwards rule
                    // were three faces of this one law, each approximated
                    // separately and each with its own failure mode.
                    float3 arm = spot - g.LeaderPos;
                    arm.y = 0f;
                    float3 spotVel = g.Facing * leaderLinSpeed
                        + appliedOmega * new float3(-arm.z, 0f, arm.x);

                    float3 want = spotVel + toSpot * SpotCorrectionGain;
                    float wantSpeed = math.length(want);
                    bool catching = wantSpeed > g.GroupSpeed;

                    if (HasLineOfSight(in grid, in cost, pos, spot, g.FactionIdx))
                    {
                        em.SetComponentData(u, new FlowDesiredDir
                        {
                            Value = math.normalizesafe(want, g.Facing),
                            HasValue = 1,
                        });
                    }
                    else
                    {
                        // No LOS to the spot (blocker between): fall back to
                        // the unit's own goal flow toward its final slot
                        // destination (already in FlowDesiredDir), at catch-up
                        // speed so it can rejoin once it clears the blocker.
                        wantSpeed = catchUpSpeed;
                        catching = true;
                    }

                    byte catchByte = (byte)(catching ? 1 : 0);
                    if (buffer[i].CatchingUp != catchByte)
                    {
                        var m = buffer[i];
                        m.CatchingUp = catchByte;
                        buffer[i] = m;
                    }

                    // Always present by here — the pre-pass above restores it
                    // for any member the integrator released. Capped at the
                    // catch-up ceiling, so a correction can never ask a unit
                    // for more speed than it has.
                    if (em.HasComponent<FormationSpeedOverride>(u))
                        em.SetComponentData(u, new FormationSpeedOverride
                        {
                            Value = math.min(wantSpeed, catchUpSpeed),
                        });
                }

                em.SetComponentData(groupEntity, g);
                keep.Dispose();

                // Structural change last: Detach removes components, which
                // would invalidate the member buffer held above.
                if (pendingDrop != Entity.Null) Detach(em, pendingDrop);
            }
        }

        /// <summary>Detach a unit from formation travel: it keeps whatever
        /// order it is executing, at its own speed.</summary>
        private static void Detach(EntityManager em, Entity u)
        {
            if (em.HasComponent<FormationMemberState>(u))
                em.RemoveComponent<FormationMemberState>(u);
            if (em.HasComponent<FormationSpeedOverride>(u))
                em.RemoveComponent<FormationSpeedOverride>(u);
        }

        /// <summary>
        /// Leader direction, mirroring FlowFollowSystem's source order:
        /// LOS bearing → whole-map goal field → direct bearing.
        /// </summary>
        private static float3 ResolveLeaderDir(in NavGridSingleton grid, in NavCostField cost,
            bool hasCache, in GoalFlowFieldCache cache, bool hasTable,
            in DirectionTableSingleton table, float3 from, float3 dest, byte factionIdx)
        {
            float3 direct = dest - from;
            direct.y = 0f;
            float lenSq = math.lengthsq(direct);
            if (lenSq <= 1e-8f) return float3.zero;
            direct *= math.rsqrt(lenSq);

            if (HasLineOfSight(in grid, in cost, from, dest, factionIdx))
                return direct;

            if (hasCache && hasTable && cache.SlotIndex.IsCreated)
            {
                int quant = GoalFlowQuant.CellsPerBucket(grid.CellSize);
                int gx = math.clamp((int)math.floor((dest.x - grid.Origin.x) / grid.CellSize), 0, grid.Width - 1);
                int gz = math.clamp((int)math.floor((dest.z - grid.Origin.z) / grid.CellSize), 0, grid.Height - 1);
                int lx = (int)math.floor((from.x - grid.Origin.x) / grid.CellSize);
                int lz = (int)math.floor((from.z - grid.Origin.z) / grid.CellSize);
                if (lx >= 0 && lx < grid.Width && lz >= 0 && lz < grid.Height)
                {
                    bool goalOnDeck = cost.Cost[gz * grid.Width + gx] == NavCostField.CostBridgeDeckOnly;
                    for (byte variant = 0; variant <= 1; variant++)
                    {
                        if (variant == GoalFlowKey.VariantGround && goalOnDeck) continue;
                        var key = new GoalFlowKey
                        {
                            GoalCell = new int2(gx / quant, gz / quant),
                            FactionIdx = factionIdx,
                            Variant = variant,
                        };
                        if (!cache.SlotIndex.TryGetValue(key, out int slot)) continue;
                        var meta = cache.Slots[slot];
                        if (meta.Valid == 0) continue;
                        byte d = cache.DirPool[meta.DirOffset + lz * grid.Width + lx];
                        if (d == NavFlowConstants.NoDirection) continue;
                        ref var dirs = ref table.Table.Value.Dirs;
                        float2 v = dirs[d];
                        return new float3(v.x, 0f, v.y);
                    }
                }
            }

            return direct;
        }

        /// <summary>Layer-0 passability for the virtual leader — walls block,
        /// gates admit the group's own faction, bridge deck-only strips are
        /// not enterable at ground level.</summary>
        private static bool IsLeaderCellPassable(in NavGridSingleton grid, in NavCostField cost,
            float3 pos, byte factionIdx)
        {
            int x = (int)math.floor((pos.x - grid.Origin.x) / grid.CellSize);
            int z = (int)math.floor((pos.z - grid.Origin.z) / grid.CellSize);
            if (x < 0 || x >= grid.Width || z < 0 || z >= grid.Height) return false;
            int idx = z * grid.Width + x;
            byte c = cost.Cost[idx];
            if (c == NavCostField.CostImpassable) return false;
            if (c == NavCostField.CostBridgeDeckOnly) return false;
            if (c == NavCostField.CostConditional)
                return (byte)(cost.Flags[idx] & NavCostField.FlagOwnerMask) == factionIdx;
            return true;
        }

        // Integer Bresenham over the cost grid — the same walkability rules
        // as FlowFollowSystem.SampleGoalFlowJob.HasLineOfSight (walls block;
        // gates block unless owned; deck-only strips break the shortcut).
        private static bool HasLineOfSight(in NavGridSingleton grid, in NavCostField cost,
            float3 from, float3 to, byte selfFactionIdx)
        {
            int x0 = (int)math.floor((from.x - grid.Origin.x) / grid.CellSize);
            int z0 = (int)math.floor((from.z - grid.Origin.z) / grid.CellSize);
            int x1 = (int)math.floor((to.x - grid.Origin.x) / grid.CellSize);
            int z1 = (int)math.floor((to.z - grid.Origin.z) / grid.CellSize);

            if (x0 < 0 || x0 >= grid.Width || z0 < 0 || z0 >= grid.Height) return false;
            if (x1 < 0 || x1 >= grid.Width || z1 < 0 || z1 >= grid.Height) return false;

            int dx = math.abs(x1 - x0);
            int dz = math.abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int x = x0;
            int z = z0;

            int maxSteps = grid.Width + grid.Height;
            for (int step = 0; step < maxSteps; step++)
            {
                int idx = z * grid.Width + x;
                byte c = cost.Cost[idx];
                if (c == NavCostField.CostImpassable) return false;
                if (c == NavCostField.CostConditional)
                {
                    byte ownerIdx = (byte)(cost.Flags[idx] & NavCostField.FlagOwnerMask);
                    // Owner or ally. docs/Design/Teams.md
                    if (!Alliances.AreAlliedBurst(ownerIdx, selfFactionIdx)) return false;
                }
                if (c == NavCostField.CostBridgeDeckOnly) return false;
                if (x == x1 && z == z1) return true;
                int e2 = err * 2;
                if (e2 > -dz) { err -= dz; x += sx; }
                if (e2 < dx) { err += dx; z += sz; }
            }
            return false;
        }
    }
}
