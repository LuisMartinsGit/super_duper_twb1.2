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
//   5. Combat takes a member OUT OF RANK, not out of the group: a member
//      that auto-acquires a target fights at its own speed and rejoins its
//      slot when the target is gone (an explicit attack order detaches it
//      for good). Arrival settles members into their slots one by one; the
//      group lives until the last has settled or the settle timeout runs out,
//      which is frozen while anyone is fighting; while more than
//      engagedHoldFraction of the roster fights, the leader holds for them
//      (docs/Design/Navigation_And_Formations.md §2.9-2.10).
//   6. A blocked leader pivots in place or steps along the flow; only a
//      genuine stall releases the group, and it releases members to their
//      own final slots rather than faking arrival.
//
// BURST (perf pass 2026-10-15). The whole per-group pass is
// FormationGroupJob, a Burst IJob Run() on the main thread — single-threaded,
// so groups are processed in exactly the old order (query chunk order) and a
// later group sees every write an earlier one made, as before. What used to
// need the EntityManager now goes through lookups inside the job:
//   * Detach / speed-override toggles are ENABLE-BIT flips through RW
//     lookups (never structural — the units carry the pre-add).
//   * The two genuinely STRUCTURAL operations are deferred to the main
//     thread after the job, applied in the order the job recorded them:
//     destroying a finished group, and the one-time AddComponent of
//     FormationSpeedOverride on a member spawned without the pre-add. (The
//     latter used to be added before the steer loop and so got its command
//     speed the same tick; it now starts at the group speed and is steered
//     from the next tick — only units outside the factory pre-add, e.g.
//     scenario husks, ever take that path.)
//   * The config SO and UnitSpeedModifiers' managed inputs are resolved on
//     the main thread before the job (Burst-safe Lookups).
//
// Determinism: reads only sim state, fixed iteration order (query chunk
// order + member buffer order), integer Bresenham LOS, no wall-clock and
// no randomness; Burst FloatMode.Deterministic. The goal-field integration
// it samples runs synchronously in GoalFlowFieldSystem earlier in the tick.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFollowSystem))]
    [UpdateBefore(typeof(SteeringSystem))]
    public partial struct FormationGroupSystem : ISystem
    {
        private EntityQuery _groupQuery;

        // ── Per-member lookups ─────────────────────────────────────────
        private ComponentLookup<LocalTransform> _xform;
        private ComponentLookup<DeathAnimationState> _dying;
        private ComponentLookup<FormationMemberState> _member;
        private ComponentLookup<Target> _target;
        private ComponentLookup<UnitEngagement> _engagement;
        private ComponentLookup<DesiredDestination> _dest;
        private ComponentLookup<MoveSpeed> _moveSpeed;
        private ComponentLookup<FormationSpeedOverride> _speedOverride;
        private ComponentLookup<FlowDesiredDir> _flowDir;
        private ComponentLookup<FormationGroup> _group;
        private BufferLookup<FormationMember> _members;
        private EntityStorageInfoLookup _exists;
        private UnitSpeedModifiers.Lookups _speedMods;

        // Stand-ins for the optional goal-field cache: a job field must
        // always hold allocated containers.
        private NativeHashMap<GoalFlowKey, int> _dummySlotIndex;
        private NativeArray<GoalFlowSlot> _dummySlots;
        private NativeArray<byte> _dummyBytes;

        public void OnCreate(ref SystemState state)
        {
            _groupQuery = state.GetEntityQuery(ComponentType.ReadWrite<FormationGroup>());
            state.RequireForUpdate(_groupQuery);
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();

            _xform = state.GetComponentLookup<LocalTransform>(true);
            _dying = state.GetComponentLookup<DeathAnimationState>(true);
            _member = state.GetComponentLookup<FormationMemberState>(false);
            _target = state.GetComponentLookup<Target>(true);
            _engagement = state.GetComponentLookup<UnitEngagement>(true);
            _dest = state.GetComponentLookup<DesiredDestination>(false);
            _moveSpeed = state.GetComponentLookup<MoveSpeed>(true);
            _speedOverride = state.GetComponentLookup<FormationSpeedOverride>(false);
            _flowDir = state.GetComponentLookup<FlowDesiredDir>(false);
            _group = state.GetComponentLookup<FormationGroup>(false);
            _members = state.GetBufferLookup<FormationMember>(false);
            _exists = state.GetEntityStorageInfoLookup();
            _speedMods = UnitSpeedModifiers.Lookups.Create(ref state);

            _dummySlotIndex = new NativeHashMap<GoalFlowKey, int>(1, Allocator.Persistent);
            _dummySlots = new NativeArray<GoalFlowSlot>(1, Allocator.Persistent);
            _dummyBytes = new NativeArray<byte>(1, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            _speedMods.Dispose();
            if (_dummySlotIndex.IsCreated) _dummySlotIndex.Dispose();
            if (_dummySlots.IsCreated) _dummySlots.Dispose();
            if (_dummyBytes.IsCreated) _dummyBytes.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Main-thread management pass; make sure the flow jobs that
            // write FlowDesiredDir this tick are not still in flight.
            state.CompleteDependency();

            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            var cfg = FormationGroupSystemConfig.I;

            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var cost = SystemAPI.GetSingleton<NavCostField>();
            // Flags is allocated with Cost by the nav bootstrap; requiring it
            // too keeps the job from ever indexing a stand-in array.
            if (!cost.Cost.IsCreated || !cost.Flags.IsCreated) return;

            bool hasGoalCache = SystemAPI.TryGetSingleton<GoalFlowFieldCache>(out var goalCache);
            bool hasDirTable = SystemAPI.TryGetSingleton<DirectionTableSingleton>(out var dirTable);
            // The old code tested SlotIndex.IsCreated before any use; fold it
            // into the flag so the job only ever sees allocated containers.
            bool useGoalField = hasGoalCache && hasDirTable && goalCache.SlotIndex.IsCreated
                && goalCache.Slots.IsCreated && goalCache.DirPool.IsCreated;

            _xform.Update(ref state);
            _dying.Update(ref state);
            _member.Update(ref state);
            _target.Update(ref state);
            _engagement.Update(ref state);
            _dest.Update(ref state);
            _moveSpeed.Update(ref state);
            _speedOverride.Update(ref state);
            _flowDir.Update(ref state);
            _group.Update(ref state);
            _members.Update(ref state);
            _exists.Update(ref state);
            _speedMods.Update(ref state);

            var groups = _groupQuery.ToEntityArray(Allocator.TempJob);
            var destroyGroups = new NativeList<Entity>(4, Allocator.TempJob);
            var addOverride = new NativeList<Entity>(4, Allocator.TempJob);
            var addOverrideValue = new NativeList<float>(4, Allocator.TempJob);

            new FormationGroupJob
            {
                Groups = groups,
                Dt = dt,
                SettleTimeout = cfg != null ? cfg.settleTimeoutSeconds : 0f,
                HoldFraction = cfg != null ? cfg.engagedHoldFraction : 1f,
                ProgressEps = cfg != null ? cfg.tetherProgressEpsilon : 0f,
                StallHeadingTolerance = cfg != null ? cfg.stallHeadingToleranceRadians : 0f,
                Grid = grid,
                Cost = cost.Cost,
                CostFlags = cost.Flags,
                UseGoalField = useGoalField,
                GoalSlotIndex = useGoalField ? goalCache.SlotIndex : _dummySlotIndex,
                GoalSlots = useGoalField ? goalCache.Slots : _dummySlots,
                GoalDirPool = useGoalField ? goalCache.DirPool : _dummyBytes,
                DirTable = useGoalField ? dirTable.Table : default,
                Xform = _xform,
                Dying = _dying,
                Member = _member,
                TargetL = _target,
                Engagement = _engagement,
                Dest = _dest,
                MoveSpeedL = _moveSpeed,
                SpeedOverride = _speedOverride,
                FlowDir = _flowDir,
                Group = _group,
                Members = _members,
                Exists = _exists,
                SpeedMods = _speedMods,
                DestroyGroups = destroyGroups,
                AddOverride = addOverride,
                AddOverrideValue = addOverrideValue,
            }.Run();

            // ── Deferred structural changes, in recorded order. ──
            for (int i = 0; i < addOverride.Length; i++)
            {
                var u = addOverride[i];
                if (em.Exists(u))
                    TransientState.Set(em, u, new FormationSpeedOverride { Value = addOverrideValue[i] });
            }
            for (int i = 0; i < destroyGroups.Length; i++)
                if (em.Exists(destroyGroups[i]))
                    em.DestroyEntity(destroyGroups[i]);

            groups.Dispose();
            destroyGroups.Dispose();
            addOverride.Dispose();
            addOverrideValue.Dispose();
        }
    }

    /// <summary>
    /// Every formation group, in query order, on one thread (see the file
    /// header). The body is the pre-Burst OnUpdate loop with EntityManager
    /// calls replaced by lookup reads/writes and structural changes recorded.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct FormationGroupJob : IJob
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
        /// </summary>
        private const float SpotCorrectionGain = 2f;

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
        /// 90-degree corner takes. Half speed is the floor.
        /// </summary>
        private const float WheelSpeedFloor = 0.5f;

        [ReadOnly] public NativeArray<Entity> Groups;
        public float Dt;
        public float SettleTimeout;
        public float HoldFraction;
        public float ProgressEps;
        public float StallHeadingTolerance;

        public NavGridSingleton Grid;
        [ReadOnly] public NativeArray<byte> Cost;
        [ReadOnly] public NativeArray<byte> CostFlags;

        public bool UseGoalField;
        [ReadOnly] public NativeHashMap<GoalFlowKey, int> GoalSlotIndex;
        [ReadOnly] public NativeArray<GoalFlowSlot> GoalSlots;
        [ReadOnly] public NativeArray<byte> GoalDirPool;
        [ReadOnly] public BlobAssetReference<DirectionTableBlob> DirTable;

        [ReadOnly] public ComponentLookup<LocalTransform> Xform;
        [ReadOnly] public ComponentLookup<DeathAnimationState> Dying;
        public ComponentLookup<FormationMemberState> Member;
        [ReadOnly] public ComponentLookup<Target> TargetL;
        [ReadOnly] public ComponentLookup<UnitEngagement> Engagement;
        public ComponentLookup<DesiredDestination> Dest;
        [ReadOnly] public ComponentLookup<MoveSpeed> MoveSpeedL;
        public ComponentLookup<FormationSpeedOverride> SpeedOverride;
        public ComponentLookup<FlowDesiredDir> FlowDir;
        public ComponentLookup<FormationGroup> Group;
        public BufferLookup<FormationMember> Members;
        [ReadOnly] public EntityStorageInfoLookup Exists;
        public UnitSpeedModifiers.Lookups SpeedMods;

        public NativeList<Entity> DestroyGroups;
        public NativeList<Entity> AddOverride;
        public NativeList<float> AddOverrideValue;

        public void Execute()
        {
            for (int gi = 0; gi < Groups.Length; gi++)
                ProcessGroup(Groups[gi]);
        }

        private void ProcessGroup(Entity groupEntity)
        {
            if (!Group.HasComponent(groupEntity) || !Members.HasBuffer(groupEntity)) return;
            var g = Group[groupEntity];

            // ── Prune / detach members (decide on a snapshot, then apply). ──
            var bufferRO = Members[groupEntity];
            var snapshot = new NativeArray<FormationMember>(bufferRO.Length, Allocator.Temp);
            bufferRO.AsNativeArray().CopyTo(snapshot);

            var keep = new NativeList<FormationMember>(snapshot.Length, Allocator.Temp);
            var toDetach = new NativeList<Entity>(snapshot.Length, Allocator.Temp);
            // Members stepping out of rank to fight lose the group speed
            // (they fight at their own).
            var toUnspeed = new NativeList<Entity>(snapshot.Length, Allocator.Temp);
            // Each kept member's UnitSpeedModifiers.Multiplier, parallel
            // to `keep` (NaN = not computed). The steer pass reuses it:
            // nothing between the two passes touches any of its inputs.
            var keepMult = new NativeList<float>(snapshot.Length, Allocator.Temp);
            // Whether the roster differs from the buffer (a drop or an
            // Engaged flip) — see the buffer write below.
            bool rosterChanged = false;
            float slowest = float.MaxValue;

            // Worst member->spot DISTANCE this tick, in any direction,
            // against the PRE-advance leader pose, and who holds it. ONE
            // metric drives the ease, the fuse's progress test AND the
            // fuse's victim (see the tether block below).
            float3 preRight = math.cross(new float3(0f, 1f, 0f), g.Facing);
            float maxOffset = 0f;
            Entity worstOffender = Entity.Null;
            // Members out of rank fighting an auto-acquired target.
            int engagedCount = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                var m = snapshot[i];
                var u = m.Unit;

                if (u == Entity.Null || !Exists.Exists(u)) continue;
                if (Active(ref Dying, u)) { toDetach.Add(u); continue; }

                // Re-ordered into another group / individually: the
                // member state no longer points here — just drop it.
                if (!Active(ref Member, u) || Member[u].Group != groupEntity)
                    continue;

                // -- Combat takes the member out of RANK, not out of the
                // group. An AUTO-acquired target keeps the member on the
                // roster; an explicit attack order detaches it for good.
                Entity fightTarget = TargetL.HasComponent(u) ? TargetL[u].Value : Entity.Null;
                if (fightTarget != Entity.Null)
                {
                    bool auto = Active(ref Engagement, u)
                        && Engagement[u].AutoTarget == fightTarget;
                    if (!auto) { toDetach.Add(u); continue; }

                    if (m.Engaged == 0) rosterChanged = true;
                    m.Engaged = 1;
                    engagedCount++;
                    keep.Add(m);
                    keepMult.Add(float.NaN);
                    if (Active(ref SpeedOverride, u)) toUnspeed.Add(u);
                    continue;
                }

                if (m.Engaged != 0)
                {
                    // The fight is over and the group still exists: walk
                    // back to the slot and fall in again.
                    m.Engaged = 0;
                    rosterChanged = true;
                    if (Dest.HasComponent(u))
                        Dest[u] = new DesiredDestination { Position = m.SlotWorld, Has = 1 };
                    keep.Add(m);
                    keepMult.Add(float.NaN);
                    continue;
                }

                bool hasDest = Dest.HasComponent(u) && Dest[u].Has != 0;
                if (!hasDest)
                {
                    // Order finished (settled into the final slot) or
                    // cancelled by stuck recovery.
                    toDetach.Add(u);
                    continue;
                }

                keep.Add(m);
                keepMult.Add(float.NaN);
                if (MoveSpeedL.HasComponent(u))
                {
                    // EFFECTIVE speed: base times the member's own slows and
                    // hastes, exactly as the integrator will apply them. A
                    // member that cannot move at all (Fortified) does not
                    // set the pace.
                    float mult = UnitSpeedModifiers.Multiplier(ref SpeedMods, u);
                    keepMult[keepMult.Length - 1] = mult;
                    float sp = MoveSpeedL[u].Value * mult;
                    if (sp > 0f && sp < slowest) slowest = sp;
                }

                if (Xform.HasComponent(u))
                {
                    // OUT OF POSITION, in any direction (progress, victim and
                    // ease are all this one number).
                    float3 sp0 = g.LeaderPos + preRight * m.Slot.x + g.Facing * m.Slot.y;
                    float3 p0 = Xform[u].Position;
                    float lx = sp0.x - p0.x, lz = sp0.z - p0.z;
                    float offset = math.sqrt(lx * lx + lz * lz);
                    if (offset > maxOffset) { maxOffset = offset; worstOffender = u; }
                }
            }
            if (keep.Length != snapshot.Length) rosterChanged = true;
            snapshot.Dispose();

            for (int i = 0; i < toDetach.Length; i++)
                Detach(toDetach[i]);
            toDetach.Dispose();
            for (int i = 0; i < toUnspeed.Length; i++)
                if (Exists.Exists(toUnspeed[i]))
                    ClearEnabled(ref SpeedOverride, toUnspeed[i]);
            toUnspeed.Dispose();

            if (keep.Length == 0)
            {
                keep.Dispose();
                keepMult.Dispose();
                DestroyGroups.Add(groupEntity);
                return;
            }

            // Rewritten only when the roster changed (otherwise `keep` IS the
            // buffer element for element and the rewrite was a no-op copy).
            var buffer = Members[groupEntity];
            if (rosterChanged)
            {
                buffer.Clear();
                for (int i = 0; i < keep.Length; i++) buffer.Add(keep[i]);
            }

            if (slowest > 0f && slowest != float.MaxValue)
                g.GroupSpeed = slowest;

            // ── Leader tether ──────────────────────────────────────────
            // The virtual leader pays none of the costs its members pay, so at
            // equal nominal speed it always pulls ahead. While anybody is out
            // of formation the leader walks at 90%; HYSTERESIS so a single
            // threshold inside the formation's own noise floor cannot latch.
            bool easing = g.Easing != 0;
            if (maxOffset > OutOfFormationEngage) easing = true;
            else if (maxOffset <= OutOfFormationRelease) easing = false;
            g.Easing = (byte)(easing ? 1 : 0);

            float lagScale = easing ? OutOfFormationLeaderSpeed : 1f;

            // ── Hold for the fight. ────────────────────────────────────
            // While MORE than engagedHoldFraction of the roster is engaged
            // the leader holds where it is, the fuse and stall counters
            // pause, and the settle timer is frozen while ANY member is
            // engaged.
            bool holdForFight = engagedCount > 0
                && engagedCount > HoldFraction * keep.Length;

            // ── Tether fuse. ───────────────────────────────────────────
            // Only a group FAILING to close up counts toward the release
            // fuse; it runs only while the leader is easing on the march.
            Entity pendingDrop = Entity.Null;
            if (!easing || holdForFight || g.State != FormationGroup.StateMoving)
            {
                g.TetherTicks = 0;
                g.BestLag = float.MaxValue;
            }
            else if (maxOffset < g.BestLag - ProgressEps)
            {
                g.BestLag = maxOffset;
                g.TetherTicks = 0;
            }
            else
            {
                g.TetherTicks = (byte)math.min(g.TetherTicks + 1, 255);
                if (g.TetherTicks >= FormationGroup.TetherReleaseTicks
                    && worstOffender != Entity.Null
                    && maxOffset > OutOfFormationEngage)
                {
                    // One wedged member would otherwise hold the whole group
                    // at 90% for the rest of the march. Drop it (design §2.4
                    // outlier rule).
                    pendingDrop = worstOffender;
                    g.TetherTicks = 0;
                    g.BestLag = float.MaxValue;
                    lagScale = 1f;
                }
            }

            // ── Advance the virtual leader. ──
            // What the leader ACTUALLY did this tick, published for the
            // member steering below (a spot's velocity is this linear
            // velocity plus this rotation applied to the arm out to it).
            float3 leaderVel = float3.zero;
            float appliedOmega = 0f;
            bool genuineStall = false;
            if (g.State == FormationGroup.StateMoving && !holdForFight)
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
                    float3 dir = ResolveLeaderDir(g.LeaderPos, g.Destination, g.FactionIdx);
                    bool hasDir = math.lengthsq(dir) > 1e-6f;

                    // ── WHEEL, DO NOT CRAB. ── A formation moves along its OWN
                    // facing and turns at a rate its outer flank can follow,
                    // so the shape rotates rigidly about the leader. The turn
                    // is only COMMITTED once its step is known to be legal
                    // (or replaced by a pivot).
                    float turnScale = 1f;
                    float err = 0f;
                    float wheelTurn = 0f;
                    float3 wheelFacing = g.Facing;
                    if (hasDir)
                    {
                        float cosErr = math.clamp(
                            dir.x * g.Facing.x + dir.z * g.Facing.z, -1f, 1f);
                        float sinErr = g.Facing.x * dir.z - g.Facing.z * dir.x;
                        err = math.atan2(sinErr, cosErr);

                        // The turn rate the formation can actually hold.
                        float omega = FlankTurnRate(in g,
                            g.GroupSpeed * (FormationGroup.CatchUpMultiplier - 1f));

                        if (math.abs(err) > FormationGroup.WheelSnapAngle)
                        {
                            // Too sharp to wheel: re-form on the new bearing
                            // instead (a jump by design, committed at once).
                            g.Facing = math.normalizesafe(dir, g.Facing);
                            wheelFacing = g.Facing;
                            err = 0f;
                            cosErr = 1f;
                        }
                        else
                        {
                            wheelTurn = math.clamp(err, -omega * Dt, omega * Dt);
                            wheelFacing = Rotate(g.Facing, wheelTurn);
                        }

                        // Slow down through the turn (cos(error), floored).
                        turnScale = math.max(WheelSpeedFloor, cosErr);
                    }

                    float destDist = math.sqrt(destDistSq);
                    float stepLen = math.min(
                        g.GroupSpeed * lagScale * turnScale * Dt, destDist);
                    float3 next = g.LeaderPos + wheelFacing * stepLen;

                    // A leader that starts inside a blocked cell must be
                    // allowed to walk out of it.
                    bool leaderInBlockedCell = !IsLeaderCellPassable(g.LeaderPos, g.FactionIdx);

                    if (leaderInBlockedCell || IsLeaderCellPassable(next, g.FactionIdx))
                    {
                        g.Facing = wheelFacing;
                        g.LeaderPos = next;
                        g.StallTicks = 0;
                        if (Dt > 1e-6f)
                        {
                            leaderVel = wheelFacing * (stepLen / Dt);
                            appliedOmega = wheelTurn / Dt;
                        }
                    }
                    else if (hasDir && math.abs(err) > StallHeadingTolerance)
                    {
                        // ── BLOCKED MID-TURN: PIVOT IN PLACE. ── With no
                        // forward motion a member's whole catch-up speed is
                        // free for the sideways drag; angular progress, not a
                        // stall.
                        float pivot = FlankTurnRate(in g,
                            g.GroupSpeed * FormationGroup.CatchUpMultiplier);
                        float turn = math.clamp(err, -pivot * Dt, pivot * Dt);
                        g.Facing = Rotate(g.Facing, turn);
                        if (Dt > 1e-6f) appliedOmega = turn / Dt;
                    }
                    else
                    {
                        // Facing (nearly) on the flow, yet the cell ahead is
                        // blocked: step along the FLOW itself.
                        float3 flowNext = g.LeaderPos + dir * stepLen;
                        if (hasDir && IsLeaderCellPassable(flowNext, g.FactionIdx))
                        {
                            g.Facing = wheelFacing;
                            g.LeaderPos = flowNext;
                            g.StallTicks = 0;
                            if (Dt > 1e-6f)
                            {
                                leaderVel = dir * (stepLen / Dt);
                                appliedOmega = wheelTurn / Dt;
                            }
                        }
                        else
                        {
                            // Genuinely blocked this tick. A leader stuck for
                            // good RELEASES the group (below).
                            g.StallTicks = (byte)math.min(g.StallTicks + 1, 255);
                            if (g.StallTicks >= FormationGroup.StallReleaseTicks)
                                genuineStall = true;
                        }
                    }
                }
            }

            if (genuineStall)
            {
                // Every member walks on to its own FINAL slot on its own goal
                // flow. Members that are fighting keep their chase.
                var roster = Members[groupEntity];
                var released = new NativeArray<FormationMember>(roster.Length, Allocator.Temp);
                roster.AsNativeArray().CopyTo(released);
                for (int i = 0; i < released.Length; i++)
                {
                    var ru = released[i].Unit;
                    if (!Exists.Exists(ru)) continue;
                    if (released[i].Engaged == 0 && Dest.HasComponent(ru))
                        Dest[ru] = new DesiredDestination
                        {
                            Position = released[i].SlotWorld,
                            Has = 1,
                        };
                    Detach(ru);
                }
                released.Dispose();
                keep.Dispose();
                keepMult.Dispose();
                DestroyGroups.Add(groupEntity);
                return;
            }

            // ── Arrival SETTLES the group; it does not dissolve it. ─────
            // Each member keeps its membership until it settles on its own
            // slot; a settle timeout (frozen while anyone fights) releases
            // whoever is left.
            bool arrivedState = g.State == FormationGroup.StateArrived;
            if (arrivedState)
            {
                if (engagedCount == 0) g.SettleTime += Dt;
                if (g.SettleTime >= SettleTimeout)
                {
                    var settling = Members[groupEntity];
                    var settled = new NativeArray<Entity>(settling.Length, Allocator.Temp);
                    for (int i = 0; i < settling.Length; i++) settled[i] = settling[i].Unit;
                    for (int i = 0; i < settled.Length; i++)
                        if (Exists.Exists(settled[i])) Detach(settled[i]);
                    settled.Dispose();
                    keep.Dispose();
                    keepMult.Dispose();
                    DestroyGroups.Add(groupEntity);
                    return;
                }
            }

            // ── Steer members to their moving spots. ──
            float3 right = math.cross(new float3(0f, 1f, 0f), g.Facing);
            float catchUpSpeed = g.GroupSpeed * FormationGroup.CatchUpMultiplier;

            // RESTORE SPEED CONTROL BEFORE THE STEER LOOP. The integrator
            // strips FormationSpeedOverride the moment a unit satisfies
            // arrival at its slot mid-march; a member without it runs at its
            // OWN speed and the formation loses every lever over it.
            // Units carry the pre-add, so this is an enable-bit flip; an
            // entity without it is recorded for a structural add after the
            // job (see the file header).
            {
                var pre = Members[groupEntity];
                for (int i = 0; i < pre.Length; i++)
                {
                    var pu = pre[i].Unit;
                    if (pre[i].Engaged != 0) continue;   // fighting at its own speed
                    if (!Exists.Exists(pu) || Active(ref SpeedOverride, pu)) continue;
                    if (SpeedOverride.HasComponent(pu))
                    {
                        SpeedOverride[pu] = new FormationSpeedOverride { Value = g.GroupSpeed };
                        SpeedOverride.SetComponentEnabled(pu, true);
                    }
                    else
                    {
                        AddOverride.Add(pu);
                        AddOverrideValue.Add(g.GroupSpeed);
                    }
                }
            }

            buffer = Members[groupEntity];
            for (int i = 0; i < buffer.Length; i++)
            {
                var u = buffer[i].Unit;
                if (buffer[i].Engaged != 0) continue;   // out of rank, fighting

                if (!Xform.HasComponent(u) || !Active(ref FlowDir, u)) continue;

                float2 s = buffer[i].Slot;
                // Settling: aim at the member's own FINAL slot, which is what
                // its DesiredDestination (and so the integrator's arrival
                // test) names.
                float3 spot = arrivedState
                    ? buffer[i].SlotWorld
                    : g.LeaderPos + right * s.x + g.Facing * s.y;
                float3 pos = Xform[u].Position;
                float3 toSpot = spot - pos;
                toSpot.y = 0f;

                // ── A MEMBER TRACKS ITS SPOT'S VELOCITY, NOT ITS POSITION. ──
                // The spot's velocity is the leader's own step plus the tick's
                // rotation applied to the arm from leader to spot; command
                // that plus a proportional pull toward the spot, and the error
                // decays as exp(-SpotCorrectionGain * t) with no overshoot.
                float3 arm = spot - g.LeaderPos;
                arm.y = 0f;
                float3 spotVel = leaderVel
                    + appliedOmega * new float3(-arm.z, 0f, arm.x);

                float3 want = spotVel + toSpot * SpotCorrectionGain;
                float wantSpeed = math.length(want);
                bool catching = wantSpeed > g.GroupSpeed;

                if (HasLineOfSight(pos, spot, g.FactionIdx))
                {
                    FlowDir[u] = new FlowDesiredDir
                    {
                        Value = math.normalizesafe(want, g.Facing),
                        HasValue = 1,
                    };
                }
                else
                {
                    // No LOS to the spot: fall back to the unit's own goal
                    // flow toward its final slot (already in FlowDesiredDir),
                    // at catch-up speed so it can rejoin.
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

                // Capped at the catch-up ceiling and pre-divided by the
                // member's own speed multiplier (the integrator multiplies the
                // override by it again).
                if (Active(ref SpeedOverride, u))
                {
                    float cmdSpeed = math.min(wantSpeed, catchUpSpeed);
                    // Reuse the prune pass's value when it computed one
                    // (buffer index == keep index).
                    float mult = i < keepMult.Length && !math.isnan(keepMult[i])
                        ? keepMult[i]
                        : UnitSpeedModifiers.Multiplier(ref SpeedMods, u);
                    if (mult > 0.05f) cmdSpeed /= mult;
                    SpeedOverride[u] = new FormationSpeedOverride { Value = cmdSpeed };
                }
            }

            Group[groupEntity] = g;
            keep.Dispose();
            keepMult.Dispose();

            if (pendingDrop != Entity.Null) Detach(pendingDrop);
        }

        /// <summary>The fastest rotation (rad/s) the formation's outer slot can
        /// follow when <paramref name="sidewaysBudget"/> m/s of a member's
        /// speed is free for the sideways drag, clamped to the group's turn
        /// band.</summary>
        private static float FlankTurnRate(in FormationGroup g, float sidewaysBudget)
        {
            float omega = g.Radius > 0.5f
                ? sidewaysBudget / g.Radius
                : FormationGroup.MaxTurnRate;
            return math.clamp(omega, FormationGroup.MinTurnRate, FormationGroup.MaxTurnRate);
        }

        /// <summary>Rotate a unit XZ heading by <paramref name="angle"/> rad
        /// (counter-clockwise seen from above, matching the err sign).</summary>
        private static float3 Rotate(float3 facing, float angle)
        {
            float sn = math.sin(angle), cs = math.cos(angle);
            float3 f = new float3(
                facing.x * cs - facing.z * sn, 0f,
                facing.x * sn + facing.z * cs);
            f = math.normalizesafe(f, facing);
            return math.lengthsq(f) > 1e-6f ? f : facing;
        }

        /// <summary>Detach a unit from formation travel: it keeps whatever
        /// order it is executing, at its own speed. Enable-bit flips only.</summary>
        private void Detach(Entity u)
        {
            ClearEnabled(ref Member, u);
            ClearEnabled(ref SpeedOverride, u);
        }

        private static bool Active<T>(ref ComponentLookup<T> lookup, Entity e)
            where T : unmanaged, IComponentData, IEnableableComponent
            => lookup.HasComponent(e) && lookup.IsComponentEnabled(e);

        /// <summary>TransientState.Clear(em, e): disable when present.</summary>
        private static void ClearEnabled<T>(ref ComponentLookup<T> lookup, Entity e)
            where T : unmanaged, IComponentData, IEnableableComponent
        {
            if (lookup.HasComponent(e))
                lookup.SetComponentEnabled(e, false);
        }

        /// <summary>
        /// Leader direction, mirroring FlowFollowSystem's source order:
        /// LOS bearing → whole-map goal field → direct bearing.
        /// </summary>
        private float3 ResolveLeaderDir(float3 from, float3 dest, byte factionIdx)
        {
            float3 direct = dest - from;
            direct.y = 0f;
            float lenSq = math.lengthsq(direct);
            if (lenSq <= 1e-8f) return float3.zero;
            direct *= math.rsqrt(lenSq);

            if (HasLineOfSight(from, dest, factionIdx))
                return direct;

            if (UseGoalField)
            {
                int quant = GoalFlowQuant.CellsPerBucket(Grid.CellSize);
                int gx = math.clamp((int)math.floor((dest.x - Grid.Origin.x) / Grid.CellSize), 0, Grid.Width - 1);
                int gz = math.clamp((int)math.floor((dest.z - Grid.Origin.z) / Grid.CellSize), 0, Grid.Height - 1);
                int lx = (int)math.floor((from.x - Grid.Origin.x) / Grid.CellSize);
                int lz = (int)math.floor((from.z - Grid.Origin.z) / Grid.CellSize);
                if (lx >= 0 && lx < Grid.Width && lz >= 0 && lz < Grid.Height)
                {
                    bool goalOnDeck = Cost[gz * Grid.Width + gx] == NavCostField.CostBridgeDeckOnly;
                    for (byte variant = 0; variant <= 1; variant++)
                    {
                        if (variant == GoalFlowKey.VariantGround && goalOnDeck) continue;
                        var key = new GoalFlowKey
                        {
                            GoalCell = new int2(gx / quant, gz / quant),
                            FactionIdx = factionIdx,
                            Variant = variant,
                        };
                        if (!GoalSlotIndex.TryGetValue(key, out int slot)) continue;
                        var meta = GoalSlots[slot];
                        // Valid == 1 only: a slot at 2 is being integrated on
                        // worker threads RIGHT NOW (GoalFlowFieldSystem's
                        // detached batch), so its bytes depend on job timing.
                        if (meta.Valid != 1) continue;
                        byte d = GoalDirPool[meta.DirOffset + lz * Grid.Width + lx];
                        // NoDirection AND NotCovered both mean "no field
                        // answer here" — the direct bearing covers the gap.
                        if (d >= NavFlowConstants.NotCovered) continue;
                        ref var dirs = ref DirTable.Value.Dirs;
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
        private bool IsLeaderCellPassable(float3 pos, byte factionIdx)
        {
            int x = (int)math.floor((pos.x - Grid.Origin.x) / Grid.CellSize);
            int z = (int)math.floor((pos.z - Grid.Origin.z) / Grid.CellSize);
            if (x < 0 || x >= Grid.Width || z < 0 || z >= Grid.Height) return false;
            int idx = z * Grid.Width + x;
            byte c = Cost[idx];
            if (c == NavCostField.CostImpassable) return false;
            if (c == NavCostField.CostBridgeDeckOnly) return false;
            if (c == NavCostField.CostConditional)
                return (byte)(CostFlags[idx] & NavCostField.FlagOwnerMask) == factionIdx;
            return true;
        }

        // Integer Bresenham over the cost grid — the same walkability rules
        // as FlowFollowSystem.SampleGoalFlowJob.HasLineOfSight (walls block;
        // gates block unless owned; deck-only strips break the shortcut).
        private bool HasLineOfSight(float3 from, float3 to, byte selfFactionIdx)
        {
            int x0 = (int)math.floor((from.x - Grid.Origin.x) / Grid.CellSize);
            int z0 = (int)math.floor((from.z - Grid.Origin.z) / Grid.CellSize);
            int x1 = (int)math.floor((to.x - Grid.Origin.x) / Grid.CellSize);
            int z1 = (int)math.floor((to.z - Grid.Origin.z) / Grid.CellSize);

            if (x0 < 0 || x0 >= Grid.Width || z0 < 0 || z0 >= Grid.Height) return false;
            if (x1 < 0 || x1 >= Grid.Width || z1 < 0 || z1 >= Grid.Height) return false;

            int dx = math.abs(x1 - x0);
            int dz = math.abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int x = x0;
            int z = z0;

            int maxSteps = Grid.Width + Grid.Height;
            for (int step = 0; step < maxSteps; step++)
            {
                int idx = z * Grid.Width + x;
                byte c = Cost[idx];
                if (c == NavCostField.CostImpassable) return false;
                if (c == NavCostField.CostConditional)
                {
                    byte ownerIdx = (byte)(CostFlags[idx] & NavCostField.FlagOwnerMask);
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
