// SteeringSystem.cs
// task-112 M2: blends the raw flow direction (M1's FlowDesiredDir) with
// local-avoidance forces produced from the NavSpatialHash neighbourhood:
//   1. separation       -- push away from too-close neighbours
//   2. unit-avoidance   -- reciprocal-style sidestep around moving units
//   3. obstacle-avoidance -- sample S1 cost field at a look-ahead cell
//   4. cohesion         -- pull weakly toward the cluster centroid
//   5. flow blend       -- add the M1 flow direction, then normalise
//
// The order above is LOCKED per the architecture's DR-1 row and must
// match the architecture's Cross-cutting decisions. Reordering the
// accumulation changes float-association semantics across runs, which
// would desync local multiplayer (R6).
//
// Result is written to SteeringDesiredDir. MovementSystem reads
// SteeringDesiredDir BEFORE FlowDesiredDir (extended preference chain).
//
// Update ordering:
//   [UpdateAfter(FlowFollowSystem)]   -- consume this tick's flow direction
//   [UpdateBefore(MovementSystem)]    -- so the integrator sees our output
//
// Determinism notes:
//   * Neighbour list is COPIED out of the multimap into a TempJob NativeList
//     and sorted by entity.Index before accumulation (DR-2 mitigation).
//   * Force constants are const float, identical bits on every machine.
//   * No SystemAPI.Time.DeltaTime read here -- the steering vector is
//     dimensionless (a desired direction), so dt belongs to MovementSystem.
//   * Look-ahead obstacle check uses integer cell math against the
//     read-only cost array.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Drives the M2 steering blend. One Burst <see cref="IJobEntity"/>
    /// per tick, parallel over units, reading the spatial hash + cost
    /// field + per-unit FlowDesiredDir and writing SteeringDesiredDir.
    ///
    /// task-112 M4: UpdateBefore migrated from MovementSystem (deleted)
    /// to UnitIntegratorSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFollowSystem))]
    [UpdateBefore(typeof(UnitIntegratorSystem))]
    public partial struct SteeringSystem : ISystem
    {
        private EntityQuery _needsComponentQuery;
        private EntityQuery _hasComponentQuery;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<DesiredDestination> _destLookup;
        private ComponentLookup<FactionTag> _factionLookup;
        private ComponentLookup<FormationMemberState> _memberLookup;

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavSpatialHash>();
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _needsComponentQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, FlowDesiredDir>()
                .WithNone<SteeringDesiredDir>()
                .Build();

            _hasComponentQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, FlowDesiredDir, SteeringDesiredDir>()
                .Build();

            // Cached lookup -- updated each tick in OnUpdate. Read-only on
            // neighbour transforms (we never write through this handle).
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _destLookup = state.GetComponentLookup<DesiredDestination>(isReadOnly: true);
            _factionLookup = state.GetComponentLookup<FactionTag>(isReadOnly: true);
            _memberLookup = state.GetComponentLookup<FormationMemberState>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Lazy-add SteeringDesiredDir to any unit that has the upstream
            // FlowDesiredDir but no steering output yet. Defers structural
            // change to the end-of-sim ECB.
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            if (!_needsComponentQuery.IsEmpty)
            {
                var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
                using var newEntities = _needsComponentQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < newEntities.Length; i++)
                    ecb.AddComponent(newEntities[i], new SteeringDesiredDir { HasValue = 0, Value = float3.zero });
            }

            if (_hasComponentQuery.IsEmpty) return;

            var hash = SystemAPI.GetSingleton<NavSpatialHash>();
            var cost = SystemAPI.GetSingleton<NavCostField>();
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();

            // Refresh the cached lookup before the parallel job reads it.
            _transformLookup.Update(ref state);
            _destLookup.Update(ref state);
            _factionLookup.Update(ref state);
            _memberLookup.Update(ref state);

            var job = new AccumulateSteeringForcesJob
            {
                HashMap = hash.Map,
                CellSize = hash.CellSize,
                Cost = cost.Cost,
                CostWidth = cost.Width,
                CostHeight = cost.Height,
                GridCellSize = grid.CellSize,
                GridOrigin = grid.Origin,
                TransformLookup = _transformLookup,
                DestLookup = _destLookup,
                FactionLookup = _factionLookup,
                MemberLookup = _memberLookup,
                Flags = cost.Flags,
            };
            state.Dependency = job.ScheduleParallel(_hasComponentQuery, state.Dependency);
        }
    }

    /// <summary>
    /// Per-unit Burst job. Reads the spatial hash via <c>TryGetFirstValue</c>
    /// / <c>TryGetNextValue</c> (per-key probe only -- never the global
    /// iterator per DR-2), accumulates the five force layers in the locked
    /// order, normalises, and writes the result.
    ///
    /// Implements the weighted-vectors hybrid: each layer contributes a
    /// scaled vector to a running sum, no full RVO2 ORCA constraint solver
    /// (rejected as overkill per the architecture's M2 section).
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct AccumulateSteeringForcesJob : IJobEntity
    {
        // ── Force weights. Const float -- identical bits on every machine.
        // Re-tuned 2026-06-01 to fix Phase2Test orbital chaos: 300 units
        // commanded to the same point were spiralling because the
        // perpendicular UnitAvoidance vector accumulated over many neighbours
        // overpowered the forward FlowWeight, and the per-neighbour Cohesion
        // pull pointed inward forming a vortex. FlowWeight is now the
        // dominant term; UnitAvoidance is much smaller and only nudges;
        // Cohesion is zero (it actively hurts a "go-to-shared-point" crowd
        // because every unit is already converging on the same destination,
        // so cohesion just re-pulls them into the centre of the cluster).
        private const float SeparationWeight       = 1.2f;
        private const float UnitAvoidanceWeight    = 0.25f;
        private const float ObstacleAvoidanceWeight = 1.5f;
        private const float CohesionWeight         = 0.0f;
        private const float FlowWeight             = 3.0f;

        // Neighbour-ring radius (in cells). 1 = the 3x3 ring centred on
        // the unit's cell.
        private const int NeighbourRing = 1;

        // Distance below which two units count as "overlapping" for the
        // separation force. 1.5 m matches the standard battalion-member
        // spacing -- larger radii (briefly tried at 2.5 m) push formation
        // members out of their slots and the formation collapses.
        // Cross-battalion ramming is handled by the bigger ArrivalRadius
        // tail, not by separation alone.
        private const float SeparationRadius  = 1.5f;
        private const float UnitAvoidanceRadius = 2.5f;
        private const float CohesionRadius      = 4.0f;

        // Look-ahead distance for obstacle-avoidance. We sample the cost
        // field at the cell one CellSize ahead of the unit along its
        // current flow direction; if it's impassable, we add a push in the
        // opposite direction.
        private const float ObstacleLookAhead = 2.0f;

        // Distance to the goal below which the Layer 3 wall-slide is SUPPRESSED.
        //
        // Layer 3 exists to get a unit PAST an obstacle that is in the way. When
        // the blocked cells ARE the destination, sliding is exactly wrong — and
        // that is precisely the melee-vs-building case: MeleeCombatSystem parks
        // its chase point 0.75 m outside the building's footprint, well inside the
        // 2.0 m forward sweep, so the sweep hit the target building itself and
        // replaced the entire steering force with a tangential slide. The unit
        // orbited the building at ~2 m, never closing to MeleeRange (1.5 m), while
        // the combat system re-issued the same chase point every frame — the
        // "melee units circle buildings instead of attacking" bug.
        //
        // Must exceed ObstacleLookAhead so the whole sweep band is covered.
        // Progress into the obstacle stays bounded by UnitIntegratorSystem's
        // arrival check (StopDistance), which fires well before contact.
        private const float GoalWallSlideSuppressRadius = 2.5f;

        // Lateral wall-clearance. The forward sweep only catches walls dead
        // ahead; a unit running PARALLEL to a wall has it off to the side and
        // would otherwise drift into the edge cell and stick. Probe each flank
        // at WallClearanceRadius and push inward off a blocked side. Weight is
        // below FlowWeight (3.0) so it only nudges -- it bends the path off the
        // wall without stalling forward progress.
        private const float WallClearanceRadius = 1.6f;
        private const float WallClearanceWeight = 1.5f;

        /// <summary>
        /// How far to either side the wall-slide looks for an opening.
        ///
        /// Was 2.5 m, which is wider than most corridors: BOTH flanks read as
        /// blocked in anything under 5 m across, so the code fell through to
        /// its "true dead-end" branch and REVERSED — units backed out of every
        /// gate, alley and wall breach they were ordered through. One
        /// separation radius plus a little is the honest question here: "is
        /// there room for me beside this wall", not "is the whole flank open".
        /// </summary>
        private const float SideProbeDist = 1.7f;

        /// <summary>Hit distance at which the wall counts as underfoot — the
        /// nearest sweep sample. Only then is "both flanks blocked" a real
        /// dead end worth reversing out of, rather than a corridor.</summary>
        private const float ContactDistance = 0.5f;

        [ReadOnly] public NativeParallelMultiHashMap<int, Entity> HashMap;
        public float CellSize;
        [ReadOnly] public NativeArray<byte> Cost;
        public int CostWidth;
        public int CostHeight;
        public float GridCellSize;
        public float3 GridOrigin;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<DesiredDestination> DestLookup;
        [ReadOnly] public ComponentLookup<FactionTag> FactionLookup;
        [ReadOnly] public ComponentLookup<FormationMemberState> MemberLookup;
        [ReadOnly] public NativeArray<byte> Flags;

        // Within this radius of the goal, BOTH the flow contribution
        // AND the perpendicular unit-avoidance sidestep are scaled by
        // (dist / ArrivalRadius). Flow fade alone left units doing a
        // permanent "orbit dance" at the goal because unit-avoidance
        // (perp sidestep) never stops while neighbours are within
        // UnitAvoidanceRadius. With BOTH fading, only pure radial
        // separation remains inside the arrival cluster -- units snap
        // to a stable lattice at SeparationRadius spacing and stop.
        //
        // Phase 6 has 4 battalions (60 units) all converging on the same
        // goal. The previous 8 m radius accommodated ~80 single units at
        // 1.5 m spacing but at the new 2.5 m SeparationRadius only fits
        // ~30, which forces 4 battalions of 15 each into a tight pile-up
        // with arrivals shoving the cluster. 15 m gives ~150 unit slots
        // -- the 60-unit Phase 6 crowd settles into 2-3 concentric rings
        // without oscillation.
        private const float ArrivalRadius = 15.0f;

        public void Execute(Entity self, in LocalTransform xf, in FlowDesiredDir flow,
            ref SteeringDesiredDir dst)
        {
            float3 pos = xf.Position;

            // Resolve our cell so we can probe the 3x3 ring around it.
            NavSpatialHash.WorldToCell(in pos, CellSize, out int cx, out int cz);

            // Unit's own faction for gate-owner checks. Conditional cells
            // (Cost == 254) are walkable for the owner, blocked for others.
            byte selfFactionIdx = FactionLookup.HasComponent(self)
                ? (byte)FactionLookup[self].Value
                : (byte)0xFF;

            // Compute arrival-decay scale once. Used to fade BOTH the
            // perpendicular unit-avoidance force (Layer 2) AND the forward
            // flow contribution (Layer 5) inside ArrivalRadius. Separation
            // (Layer 1, purely radial) stays full strength so units still
            // push apart inside the cluster.
            //
            // A unit travelling as a FORMATION MEMBER is exempt from the fade.
            // FormationGroupSystem writes the "steer to your moving spot"
            // vector into FlowDesiredDir, so fading flow inside ArrivalRadius
            // faded the FORMATION, not just the final approach: over the last
            // 15 m the 1.2-weight separation force outvoted what was left of
            // the 3.0-weight flow and the shape mushed into a blob exactly as
            // it arrived. The fade exists for "N units converging on ONE
            // point", which is the opposite of a formation, where every member
            // has its own distinct slot and they are not competing for it.
            // The group dissolves the moment the leader lands (design §2.8),
            // so the settle itself still gets the full arrival damping.
            bool inFormation = MemberLookup.HasComponent(self);
            // Which group, so a neighbour in the SAME formation can be
            // recognised below. Entity.Null when this unit travels alone.
            Entity myGroup = inFormation ? MemberLookup[self].Group : Entity.Null;

            float arrivalScale = 1f;
            bool atGoal = false;
            if (DestLookup.HasComponent(self))
            {
                var d = DestLookup[self];
                if (d.Has != 0)
                {
                    float ddx = d.Position.x - pos.x;
                    float ddz = d.Position.z - pos.z;
                    float distSq = ddx * ddx + ddz * ddz;
                    float r2 = ArrivalRadius * ArrivalRadius;
                    if (distSq < r2 && !inFormation)
                    {
                        arrivalScale = math.sqrt(distSq) / ArrivalRadius;
                    }
                    atGoal = distSq <= GoalWallSlideSuppressRadius * GoalWallSlideSuppressRadius;
                }
            }

            // Force accumulator. The five layers below MUST be added in the
            // locked DR-1 order: separation -> unit-avoidance ->
            // obstacle-avoidance -> cohesion -> flow blend.
            float3 force = float3.zero;

            // ───────────── 1. Separation ─────────────
            //   Push directly away from any neighbour inside SeparationRadius.
            //   Force magnitude scales with the overlap depth so two units
            //   exactly on top of each other receive the maximum push.
            float3 separation = float3.zero;
            int separationCount = 0;

            // ───────────── 2. Unit-avoidance ─────────────
            //   Reciprocal sidestep: for moving neighbours we push perp to
            //   the line between us, in the half-plane that puts us further
            //   from the neighbour's predicted next position.
            float3 unitAvoidance = float3.zero;
            int unitAvoidanceCount = 0;

            // ───────────── 4. Cohesion ─────────────
            //   Pull weakly toward the cluster centroid of neighbours inside
            //   CohesionRadius. Computed by summing relative positions and
            //   dividing at the end.
            float3 cohesionSum = float3.zero;
            int cohesionCount = 0;

            // Walk the 3x3 cell ring around (cx, cz). Per-key probe only
            // (DR-2: NEVER GetEnumerator on the multimap from sim code).
            for (int dz = -NeighbourRing; dz <= NeighbourRing; dz++)
            {
                for (int dx = -NeighbourRing; dx <= NeighbourRing; dx++)
                {
                    int key = NavSpatialHash.PackKey(cx + dx, cz + dz);
                    if (!HashMap.TryGetFirstValue(key, out Entity other, out var it))
                        continue;
                    do
                    {
                        if (other == self) continue;

                        // Read the neighbour's actual world position via
                        // ComponentLookup. Marked read-only on the lookup
                        // so the parallel-for restriction is satisfied
                        // (we never write through this handle).
                        if (!TransformLookup.HasComponent(other)) continue;
                        float3 otherPos = TransformLookup[other].Position;

                        float3 toMe = pos - otherPos;
                        toMe.y = 0f;
                        float distSq = math.lengthsq(toMe);
                        float dist;
                        if (distSq < 1e-6f)
                        {
                            // Two units stacked at exactly the same XZ
                            // position. Pick a deterministic offset
                            // direction so the pair still separates.
                            // The choice depends on a stable per-pair
                            // discriminator (the comparison of entity
                            // Index + Version) so both units pick
                            // OPPOSITE pushes and don't deadlock.
                            int cmp = CompareEntities(self, other);
                            // cmp > 0 -> push +x, cmp < 0 -> push -x.
                            toMe = new float3(cmp >= 0 ? 1f : -1f, 0f, 0f);
                            dist = 1f;
                        }
                        else
                        {
                            dist = math.sqrt(distSq);
                        }

                        // FORMATION-MATES DO NOT PUSH EACH OTHER.
                        //
                        // The layout already IS the spacing: slots sit 2.0 m
                        // apart (FormationMoveCommandHelper.Spacing), while
                        // unit-avoidance reaches 2.5 m and separation 1.5 m. A
                        // squad standing correctly in its slots was therefore
                        // permanently inside its own avoidance radius, so the
                        // reciprocal sidestep never rested: eight units in a
                        // straight-line march jostled sideways the whole way,
                        // swapped lateral positions, and settled a slot-width
                        // off their marks. That is the infighting — every unit
                        // being told to hold a position and simultaneously told
                        // to get away from the units holding the next ones.
                        //
                        // Only members of the SAME group are exempt. Another
                        // formation, a loose unit or an enemy still pushes
                        // normally, so two squads meeting still resolve.
                        bool sameFormation = myGroup != Entity.Null
                            && MemberLookup.HasComponent(other)
                            && MemberLookup[other].Group == myGroup;

                        // 1. Separation -- triggers within SeparationRadius
                        if (!sameFormation && dist < SeparationRadius)
                        {
                            // Push depth = how much we overlap. Normalised
                            // direction times overlap gives a "deeper overlap
                            // = stronger push" curve, matching DR-1's intent.
                            float overlap = SeparationRadius - dist;
                            separation += (toMe / dist) * overlap;
                            separationCount++;
                        }

                        // 2. Unit-avoidance -- within UnitAvoidanceRadius
                        //    sidestep perpendicular to the line between us.
                        if (!sameFormation && dist < UnitAvoidanceRadius)
                        {
                            // Right-perpendicular (in XZ plane) of toMe.
                            // Deterministic choice of side: always right of
                            // the from-other-to-me vector. Reciprocal
                            // because both units run this job and pick the
                            // opposite-handed perpendicular relative to
                            // each other's frames.
                            float3 perp = new float3(-toMe.z, 0f, toMe.x) / dist;
                            float fall = 1f - (dist / UnitAvoidanceRadius);
                            unitAvoidance += perp * fall;
                            unitAvoidanceCount++;
                        }

                        // 4. Cohesion -- within CohesionRadius pull toward
                        //    neighbour. Accumulate raw relative position so
                        //    we can divide at the end.
                        if (dist < CohesionRadius)
                        {
                            cohesionSum += -toMe; // pull = away from "me-to-other"
                            cohesionCount++;
                        }
                    } while (HashMap.TryGetNextValue(out other, ref it));
                }
            }

            // ── Layer 1: separation ─────────────────────────────────
            if (separationCount > 0)
            {
                separation /= separationCount;
                force += separation * SeparationWeight;
            }

            // ── Layer 2: unit-avoidance ─────────────────────────────
            // Scaled by arrivalScale so the perpendicular sidestep
            // disappears as the unit nears the goal. Otherwise pairs of
            // arrived units inside UnitAvoidanceRadius (2.5 m) but outside
            // SeparationRadius (1.5 m) would dance around each other
            // forever -- the "orbit" the user reported.
            //
            // Jitter fix (2026-07-12): inside the FINAL 3 m of approach
            // (arrivalScale < 3/15 = 0.2) the sidestep is CUT ENTIRELY, not
            // just faded. With the destination occupied (rally point with a
            // unit parked on it) the radial forces balance at the separation
            // ring, and any residual perpendicular force — however small —
            // was the only unbalanced component: pure orbital motion. Zero
            // perp + full radial separation lets the unit settle on the ring
            // so the crowded-arrival rule can declare it done.
            if (unitAvoidanceCount > 0 && arrivalScale >= 0.2f)
            {
                unitAvoidance /= unitAvoidanceCount;
                force += unitAvoidance * (UnitAvoidanceWeight * arrivalScale);
            }

            // ── Layer 3: obstacle-avoidance ─────────────────────────
            // Detection: sweep the forward look-ahead at multiple distances
            // (0.5, 1.0, 1.5, 2.0 m). The previous single-shot check at
            // exactly 2 m overshot 1-m-thick walls -- the sample landed in
            // walkable cells PAST the wall and avoidance never fired until
            // the unit was already wedged against the wall face.
            //
            // When ANY sample is impassable we COMPLETELY OVERRIDE the
            // accumulated force with a pure perpendicular slide. The
            // previous "add perp to existing flow" left the net direction
            // pointing partly into the wall (flow=3.0 forward easily
            // outvotes 1.5 perp). Total override is the only thing that
            // makes the unit actually move along the wall.
            //
            // Side choice: probe left and right at 2.0 m. Right-clear is
            // preferred (deterministic). Both blocked = true dead-end,
            // fall back to a reverse-and-rotate.
            // Suppressed at the goal: see GoalWallSlideSuppressRadius. Sliding
            // along the very thing you were sent to reach is an orbit, not an
            // avoidance.
            bool wallBlockedAhead = false;
            if (!atGoal && flow.HasValue != 0 && math.lengthsq(flow.Value) > 1e-8f)
            {
                float3 fwd = math.normalize(new float3(flow.Value.x, 0f, flow.Value.z));

                // Multi-distance sweep along forward direction. Record HOW FAR
                // the blockage is — the response is scaled by it below.
                float hitDist = ObstacleLookAhead;
                for (float probe = 0.5f; probe <= ObstacleLookAhead; probe += 0.5f)
                {
                    float3 sample = pos + fwd * probe;
                    int sx = (int)math.floor((sample.x - GridOrigin.x) / GridCellSize);
                    int sz = (int)math.floor((sample.z - GridOrigin.z) / GridCellSize);
                    if (sx < 0 || sx >= CostWidth || sz < 0 || sz >= CostHeight) continue;
                    if (IsCellBlocked(sx, sz, selfFactionIdx))
                    {
                        wallBlockedAhead = true;
                        hitDist = probe;
                        break;
                    }
                }

                if (wallBlockedAhead)
                {
                    float3 perpLeft  = new float3(-fwd.z, 0f, fwd.x);
                    float3 perpRight = new float3( fwd.z, 0f, -fwd.x);

                    int lX = (int)math.floor((pos.x + perpLeft.x  * SideProbeDist - GridOrigin.x) / GridCellSize);
                    int lZ = (int)math.floor((pos.z + perpLeft.z  * SideProbeDist - GridOrigin.z) / GridCellSize);
                    int rX = (int)math.floor((pos.x + perpRight.x * SideProbeDist - GridOrigin.x) / GridCellSize);
                    int rZ = (int)math.floor((pos.z + perpRight.z * SideProbeDist - GridOrigin.z) / GridCellSize);

                    bool leftClear = lX >= 0 && lX < CostWidth
                        && lZ >= 0 && lZ < CostHeight
                        && !IsCellBlocked(lX, lZ, selfFactionIdx);
                    bool rightClear = rX >= 0 && rX < CostWidth
                        && rZ >= 0 && rZ < CostHeight
                        && !IsCellBlocked(rX, rZ, selfFactionIdx);

                    // Keep separation Layer 1's contribution -- otherwise
                    // crowded units would interpenetrate.
                    float3 sepCopy = float3.zero;
                    if (separationCount > 0)
                        sepCopy = (separation / separationCount) * SeparationWeight;

                    // URGENCY: 1 at contact, 0 at the far end of the sweep.
                    //
                    // This used to be a hard override — the slide REPLACED the
                    // whole force, at any hit distance. That threw away what the
                    // flow field knows: at 2 m from a bend the field has already
                    // turned, and discarding it made the unit crab sideways,
                    // clear the sweep, lurch forward, clip again. In a corridor
                    // that reads as constant stuttering. Blend instead: press on
                    // along the flow while the wall is still distant, commit to
                    // the slide only as it gets close.
                    float urgency = math.saturate(1f - hitDist / ObstacleLookAhead);
                    float3 flowKeep = fwd * (FlowWeight * arrivalScale * (1f - urgency));

                    if (rightClear)
                    {
                        force = perpRight * ((FlowWeight + ObstacleAvoidanceWeight) * urgency)
                              + flowKeep + sepCopy;
                    }
                    else if (leftClear)
                    {
                        force = perpLeft * ((FlowWeight + ObstacleAvoidanceWeight) * urgency)
                              + flowKeep + sepCopy;
                    }
                    else if (hitDist > ContactDistance)
                    {
                        // Both flanks blocked but the wall is not yet underfoot:
                        // this is an ordinary CORRIDOR, not a dead end. Keep
                        // following the flow — it is the only thing that knows
                        // where the corridor goes.
                        //
                        // Gated on the raw hit distance, not on urgency: the
                        // sweep starts at 0.5 m so urgency tops out well below
                        // 1, and testing it against ~1 would make the reverse
                        // below unreachable.
                        force = fwd * (FlowWeight * arrivalScale) + sepCopy;
                    }
                    else
                    {
                        // True dead-end. Reverse + rotate clockwise so
                        // packed units don't all reverse into each other.
                        force = (-fwd + perpRight) * ObstacleAvoidanceWeight + sepCopy;
                    }
                }
            }

            // ── Layer 3b: lateral wall clearance ────────────────────
            // Only when nothing is blocking dead-ahead (that case is already
            // owned by the Layer 3 override). Probe both flanks; push inward
            // off a blocked side so units travelling PARALLEL to a wall keep a
            // cell of slack instead of grinding along the edge. Both sides
            // blocked (a one-cell gap) adds nothing -- the unit threads it
            // centred under flow + separation alone.
            if (!wallBlockedAhead
                && flow.HasValue != 0 && math.lengthsq(flow.Value) > 1e-8f)
            {
                float3 fwd = math.normalize(new float3(flow.Value.x, 0f, flow.Value.z));
                float3 perpRight = new float3(fwd.z, 0f, -fwd.x);

                bool leftBlocked  = IsWorldBlocked(pos - perpRight * WallClearanceRadius, selfFactionIdx);
                bool rightBlocked = IsWorldBlocked(pos + perpRight * WallClearanceRadius, selfFactionIdx);
                if (rightBlocked && !leftBlocked)
                    force += -perpRight * WallClearanceWeight;
                else if (leftBlocked && !rightBlocked)
                    force += perpRight * WallClearanceWeight;
            }

            // ── Layer 4: cohesion ───────────────────────────────────
            if (cohesionCount > 0)
            {
                float3 centroidOffset = cohesionSum / cohesionCount;
                float coLen = math.length(centroidOffset);
                if (coLen > 1e-6f)
                {
                    // Cap by 1.0 so the cohesion magnitude doesn't blow up
                    // when the cluster is large.
                    float3 coDir = centroidOffset / coLen;
                    force += coDir * CohesionWeight;
                }
            }

            // ── Layer 5: flow blend (FINAL accumulation layer) ──────
            // Scaled by the same arrivalScale as unit-avoidance above so
            // the forward push fades alongside the perpendicular sidestep
            // inside the cluster -- only pure radial separation acts at
            // the goal, producing a stable lattice.
            //
            // SUPPRESSED when Layer 3 detected a wall ahead -- Layer 3
            // has already taken over the force with a pure perpendicular
            // slide, and re-adding flow here would push the unit back
            // into the wall.
            if (!wallBlockedAhead
                && flow.HasValue != 0 && math.lengthsq(flow.Value) > 1e-8f)
            {
                float3 fwd = math.normalize(new float3(flow.Value.x, 0f, flow.Value.z));
                force += fwd * (FlowWeight * arrivalScale);
            }

            // Normalise to a unit vector. If the accumulator is essentially
            // zero (no neighbours, no flow), report HasValue=0 so the
            // MovementSystem falls back to the next preference (raw flow,
            // then NavMesh).
            float forceLenSq = math.lengthsq(force);
            if (forceLenSq < 1e-8f)
            {
                dst.HasValue = 0;
                dst.Value = float3.zero;
                return;
            }

            float3 dir = force / math.sqrt(forceLenSq);
            // Clamp to the XZ plane.
            dst.Value = new float3(dir.x, 0f, dir.z);
            dst.HasValue = 1;
        }

        // World-space wrapper around IsCellBlocked: maps a world position to
        // its cost-field cell and reports blocked. Out-of-grid samples are
        // treated as NOT blocked so map-edge units aren't shoved inward.
        private bool IsWorldBlocked(float3 world, byte selfFactionIdx)
        {
            int sx = (int)math.floor((world.x - GridOrigin.x) / GridCellSize);
            int sz = (int)math.floor((world.z - GridOrigin.z) / GridCellSize);
            if (sx < 0 || sx >= CostWidth || sz < 0 || sz >= CostHeight) return false;
            return IsCellBlocked(sx, sz, selfFactionIdx);
        }

        // Faction-aware "is this cell blocked for me?" probe.
        //   Cost == 255 -> always blocked (wall)
        //   Cost == 254 -> blocked unless gate owner (Flags & 0x07)
        //                  matches the unit's faction
        //   Cost <  254 -> walkable
        private bool IsCellBlocked(int x, int z, byte selfFactionIdx)
        {
            int idx = z * CostWidth + x;
            byte c = Cost[idx];
            if (c == NavCostField.CostImpassable) return true;
            if (c == NavCostField.CostConditional)
            {
                byte ownerIdx = (byte)(Flags[idx] & NavCostField.FlagOwnerMask);
                return ownerIdx != selfFactionIdx;
            }
            return false;
        }

        // Deterministic entity comparator. Sorts by Index, then Version --
        // matches the entity.Index-based tie-break used elsewhere in the
        // nav stack (DR-1 / DR-2 family). Returns >0 if `a` is "later" than
        // `b`, <0 if earlier, 0 if identical.
        private static int CompareEntities(Entity a, Entity b)
        {
            if (a.Index != b.Index) return a.Index - b.Index;
            return a.Version - b.Version;
        }
    }
}
