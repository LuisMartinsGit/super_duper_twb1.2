// BattalionSyncSystem.cs
// Orchestrates per-frame battalion updates: formation slot reassignment,
// combat target state, and member movement toward slots or enemies.
//
// Slot assignments are STICKY — each member keeps its Column/Row between frames.
// Greedy nearest-slot reassignment only runs when:
//   1. The formation direction changes significantly (> 30°) — handles about-face
//   2. NeedsReassignment flag is set (new move command issued)
//   3. LastAssignmentRot is uninitialized
//
// This prevents chaotic per-frame reassignment that caused semicircle spreading
// and teleporting near obstacles.
//
// Obstacle handling: members whose slot is unreachable (blocked by passability)
// path toward the leader using flow fields. They rejoin formation once close.
//
// Fix #218: the reassignment logic and combat state computation used to live
// inline in OnUpdate, making this file 795 lines with 5+ concerns mixed together.
// Both have been extracted to sibling helper files:
//   - BattalionFormationHelpers.cs : slot reassignment (§ 3)
//   - BattalionCombatHelpers.cs    : target cleanup, center-of-mass,
//                                    encirclement check, per-member targets
//                                    (§ 3a/3b/3c)
// This file now owns: the persistent scratch caches, the outer per-leader
// loop, the reassignment gating (§ 0-2), and the movement phases (§ 4/5a/5b).
//
// Location: Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Movement
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct BattalionSyncSystem : ISystem
    {
        // Reassign slots when formation rotates more than this (radians ≈ 30°)
        private const float ReassignAngleThreshold = 0.52f;

        // Initial capacity for cached NativeArrays (grows if a battalion exceeds this)
        private const int InitialMaxMembers = 128;
        private const int InitialMaxSlots   = 128;

        // Persistent cached arrays — reused across frames to avoid per-frame allocation
        private NativeArray<Entity> _members;
        private NativeArray<float3> _memberPos;
        private NativeArray<bool>   _memberAlive;
        private NativeArray<float3> _targetPositions;
        private NativeArray<float>  _memberDistances;
        private NativeArray<bool>   _followsLeader;
        private NativeArray<float3> _slotWorldPositions;
        private NativeArray<int>    _slotAssignment;
        private NativeArray<bool>   _memberUsed;

        private int _memberCapacity;
        private int _slotCapacity;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattalionLeader>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _memberCapacity = InitialMaxMembers;
            _slotCapacity   = InitialMaxSlots;

            _members          = new NativeArray<Entity>(InitialMaxMembers, Allocator.Persistent);
            _memberPos        = new NativeArray<float3>(InitialMaxMembers, Allocator.Persistent);
            _memberAlive      = new NativeArray<bool>(InitialMaxMembers, Allocator.Persistent);
            _targetPositions  = new NativeArray<float3>(InitialMaxMembers, Allocator.Persistent);
            _memberDistances  = new NativeArray<float>(InitialMaxMembers, Allocator.Persistent);
            _followsLeader    = new NativeArray<bool>(InitialMaxMembers, Allocator.Persistent);
            _slotWorldPositions = new NativeArray<float3>(InitialMaxSlots, Allocator.Persistent);
            _slotAssignment     = new NativeArray<int>(InitialMaxSlots, Allocator.Persistent);
            _memberUsed         = new NativeArray<bool>(InitialMaxMembers, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_members.IsCreated)          _members.Dispose();
            if (_memberPos.IsCreated)        _memberPos.Dispose();
            if (_memberAlive.IsCreated)      _memberAlive.Dispose();
            if (_targetPositions.IsCreated)  _targetPositions.Dispose();
            if (_memberDistances.IsCreated)  _memberDistances.Dispose();
            if (_followsLeader.IsCreated)    _followsLeader.Dispose();
            if (_slotWorldPositions.IsCreated) _slotWorldPositions.Dispose();
            if (_slotAssignment.IsCreated)     _slotAssignment.Dispose();
            if (_memberUsed.IsCreated)         _memberUsed.Dispose();
        }

        /// <summary>
        /// Ensure member-sized arrays have at least the given capacity.
        /// Disposes old arrays and allocates new ones only when growth is needed.
        /// </summary>
        private void EnsureMemberCapacity(int needed)
        {
            if (needed <= _memberCapacity) return;
            int newCap = math.max(needed, _memberCapacity * 2);

            if (_members.IsCreated)         _members.Dispose();
            if (_memberPos.IsCreated)       _memberPos.Dispose();
            if (_memberAlive.IsCreated)     _memberAlive.Dispose();
            if (_targetPositions.IsCreated) _targetPositions.Dispose();
            if (_memberDistances.IsCreated) _memberDistances.Dispose();
            if (_followsLeader.IsCreated)   _followsLeader.Dispose();
            if (_memberUsed.IsCreated)      _memberUsed.Dispose();

            _members         = new NativeArray<Entity>(newCap, Allocator.Persistent);
            _memberPos       = new NativeArray<float3>(newCap, Allocator.Persistent);
            _memberAlive     = new NativeArray<bool>(newCap, Allocator.Persistent);
            _targetPositions = new NativeArray<float3>(newCap, Allocator.Persistent);
            _memberDistances = new NativeArray<float>(newCap, Allocator.Persistent);
            _followsLeader   = new NativeArray<bool>(newCap, Allocator.Persistent);
            _memberUsed      = new NativeArray<bool>(newCap, Allocator.Persistent);

            _memberCapacity = newCap;
        }

        /// <summary>
        /// Ensure slot-sized arrays have at least the given capacity.
        /// </summary>
        private void EnsureSlotCapacity(int needed)
        {
            if (needed <= _slotCapacity) return;
            int newCap = math.max(needed, _slotCapacity * 2);

            if (_slotWorldPositions.IsCreated) _slotWorldPositions.Dispose();
            if (_slotAssignment.IsCreated)     _slotAssignment.Dispose();

            _slotWorldPositions = new NativeArray<float3>(newCap, Allocator.Persistent);
            _slotAssignment     = new NativeArray<int>(newCap, Allocator.Persistent);

            _slotCapacity = newCap;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            // PR3 — battalion members trust the leader's navmesh route.
            // Local slot positioning has no grid query; the per-member
            // movement integrator skips passability checks too.
            PassabilityGrid passGrid = null;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // ── BFME2-style arrival alignment snapshot ──
            // Snapshot every idle (DesiredDestination.Has==0) battalion leader
            // up-front. The per-leader loop below uses this to detect overlap
            // when its leader has just arrived, and shifts the new arrival's
            // anchor to a clear adjacent spot. This is the BFME2 "snap to
            // align" feel — dampening alone (PR #290) prevented the shake
            // but left formations stacked on top of each other.
            var alignSnaps = new NativeList<AlignmentSnap>(32, Allocator.Temp);
            foreach (var (otherLeader, otherXf, otherEntity) in SystemAPI
                .Query<RefRO<BattalionLeader>, RefRO<LocalTransform>>()
                .WithAll<BattalionTag>()
                .WithEntityAccess())
            {
                bool otherMoving = false;
                if (em.HasComponent<DesiredDestination>(otherEntity))
                    otherMoving = em.GetComponentData<DesiredDestination>(otherEntity).Has != 0;
                if (otherMoving) continue;
                var obl = otherLeader.ValueRO;
                float r = math.max(obl.Columns, obl.Rows) * obl.Spacing * 0.5f;
                alignSnaps.Add(new AlignmentSnap
                {
                    Entity = otherEntity,
                    Position = otherXf.ValueRO.Position,
                    Radius = r,
                });
            }

            foreach (var (leader, leaderXf, entity) in SystemAPI
                .Query<RefRW<BattalionLeader>, RefRO<LocalTransform>>()
                .WithAll<BattalionTag>()
                .WithEntityAccess())
            {
                if (!em.HasBuffer<BattalionMember>(entity)) continue;

                var buffer = em.GetBuffer<BattalionMember>(entity);
                var bl = leader.ValueRO;
                float3 leaderPos = leaderXf.ValueRO.Position;
                quaternion leaderRot = leaderXf.ValueRO.Rotation;

                int cols = bl.Columns;
                int rows = bl.Rows;
                float spacing = bl.Spacing;
                int slotCount = cols * rows;
                int memberCount = buffer.Length;

                // ── 0. Update FormationRot only while actively moving ──
                bool isMoving = false;
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    var dd = em.GetComponentData<DesiredDestination>(entity);
                    if (dd.Has != 0)
                    {
                        float3 toDest = dd.Position - leaderPos;
                        toDest.y = 0;
                        if (math.lengthsq(toDest) > 1f)
                        {
                            isMoving = true;
                            leader.ValueRW.FormationRot = leaderRot;
                        }
                    }
                    else if (bl.HasDestinationRot != 0)
                    {
                        leader.ValueRW.FormationRot = bl.DestinationRot;
                        leader.ValueRW.HasDestinationRot = 0;
                    }
                }
                else if (bl.HasDestinationRot != 0)
                {
                    leader.ValueRW.FormationRot = bl.DestinationRot;
                    leader.ValueRW.HasDestinationRot = 0;
                }

                // Handle uninitialized FormationRot
                quaternion formationRot = leader.ValueRO.FormationRot;
                if (math.lengthsq(formationRot.value) < 0.001f)
                {
                    formationRot = leaderRot;
                    leader.ValueRW.FormationRot = formationRot;
                }

                // ── BFME2-style arrival alignment ──
                // On the first frame after a battalion arrives at its
                // destination, check the snapshot for any other idle
                // battalion whose formation footprint overlaps. If found,
                // shift this leader's anchor away from the conflict so
                // the two formations sit alongside instead of on top.
                // Members re-target their slots automatically next frame
                // because slot offsets are computed in leader-local space.
                if (isMoving)
                {
                    if (em.HasComponent<BattalionAlignmentState>(entity))
                        em.SetComponentData(entity, new BattalionAlignmentState { AlignedSinceArrival = 0 });
                }
                else
                {
                    bool aligned = em.HasComponent<BattalionAlignmentState>(entity)
                        && em.GetComponentData<BattalionAlignmentState>(entity).AlignedSinceArrival != 0;
                    if (!aligned)
                    {
                        float myRadius = math.max(cols, rows) * spacing * 0.5f;
                        float3 shift = float3.zero;
                        int conflictCount = 0;
                        for (int s = 0; s < alignSnaps.Length; s++)
                        {
                            var snap = alignSnaps[s];
                            if (snap.Entity == entity) continue;
                            float3 diff = leaderPos - snap.Position;
                            diff.y = 0f;
                            float distSq = math.lengthsq(diff);
                            float minDist = myRadius + snap.Radius;
                            if (distSq < minDist * minDist && distSq > 0.0001f)
                            {
                                float dist = math.sqrt(distSq);
                                float overlap = minDist - dist + 0.5f; // small buffer
                                shift += (diff / dist) * overlap;
                                conflictCount++;
                            }
                        }
                        if (conflictCount > 0)
                        {
                            shift /= conflictCount;
                            float3 newLeaderPos = leaderPos + shift;
                            // Snap the shifted anchor onto the navmesh so
                            // we don't land on a building / unreachable tile.
                            var nmm = NavMeshManager.Instance;
                            if (nmm != null)
                                newLeaderPos = nmm.SnapToNavMesh(
                                    new UnityEngine.Vector3(newLeaderPos.x, newLeaderPos.y, newLeaderPos.z), 5f);
                            var newXf = LocalTransform.FromPositionRotationScale(
                                newLeaderPos, leaderXf.ValueRO.Rotation, leaderXf.ValueRO.Scale);
                            em.SetComponentData(entity, newXf);
                            leaderPos = newLeaderPos;
                        }
                        if (em.HasComponent<BattalionAlignmentState>(entity))
                            em.SetComponentData(entity, new BattalionAlignmentState { AlignedSinceArrival = 1 });
                        else
                            em.AddComponentData(entity, new BattalionAlignmentState { AlignedSinceArrival = 1 });
                    }
                }

                // ── 1. Decide whether to run greedy reassignment ──
                bool runReassignment = false;

                // Check if LastAssignmentRot is uninitialized
                quaternion lastAssignRot = bl.LastAssignmentRot;
                if (math.lengthsq(lastAssignRot.value) < 0.001f)
                {
                    runReassignment = true;
                }
                // Check explicit flag (new move command)
                else if (bl.NeedsReassignment != 0)
                {
                    runReassignment = true;
                    leader.ValueRW.NeedsReassignment = 0;
                }
                // Check if formation direction changed significantly
                {
                    float3 oldFwd = math.mul(lastAssignRot, new float3(0, 0, 1));
                    float3 newFwd = math.mul(formationRot, new float3(0, 0, 1));
                    oldFwd.y = 0; newFwd.y = 0;
                    oldFwd = math.normalizesafe(oldFwd);
                    newFwd = math.normalizesafe(newFwd);
                    float angle = math.acos(math.clamp(math.dot(oldFwd, newFwd), -1f, 1f));
                    if (angle > ReassignAngleThreshold)
                        runReassignment = true;
                }

                // ── 2. Collect living members and their current positions ──
                EnsureMemberCapacity(memberCount);
                EnsureSlotCapacity(slotCount);
                int aliveCount = 0;

                for (int i = 0; i < memberCount; i++)
                {
                    var m = buffer[i].Value;
                    _members[i] = m;
                    if (em.Exists(m) && em.HasComponent<LocalTransform>(m) && em.HasComponent<BattalionMemberData>(m))
                    {
                        _memberPos[i] = em.GetComponentData<LocalTransform>(m).Position;
                        _memberAlive[i] = true;
                        aliveCount++;
                    }
                    else
                    {
                        _memberPos[i] = float3.zero;
                        _memberAlive[i] = false;
                    }
                }

                // ── 3. Greedy nearest-slot assignment (only when needed) ──
                // Delegated to BattalionFormationHelpers (Fix #218).
                if (runReassignment && aliveCount > 0)
                {
                    BattalionFormationHelpers.ReassignSlots(
                        em, leaderPos, formationRot, cols, rows, spacing,
                        memberCount, _members, _memberPos, _memberAlive,
                        _slotWorldPositions, _slotAssignment, _memberUsed);
                    leader.ValueRW.LastAssignmentRot = formationRot;
                }

                // ── 3a. Clear stale leader target + BattalionAttackTarget ──
                // Delegated to BattalionCombatHelpers (Fix #218).
                BattalionCombatHelpers.ClearStaleLeaderTarget(em, ecb, entity);

                // ── 3b / 3c. Combat state: own center, encirclement check,
                // per-member target assignment. Delegated to BattalionCombatHelpers.
                var combatState = BattalionCombatHelpers.UpdateCombatState(
                    em, entity, leaderPos,
                    memberCount, _members, _memberPos, _memberAlive, aliveCount);

                bool isRangedBattalion = combatState.IsRangedBattalion;
                bool inEncircleRange   = combatState.InEncircleRange;
                bool inFiringRange     = combatState.InFiringRange;
                bool battalionInCombat = combatState.BattalionInCombat;

                // ── 4. Compute per-member target positions ──
                float maxDist = 0f;

                for (int i = 0; i < memberCount; i++)
                {
                    if (!_memberAlive[i])
                    {
                        _memberDistances[i] = -1f;
                        continue;
                    }

                    // Members with a combat target are released from formation when in engage range
                    // Melee: released at encircle range (they pathfind to enemy)
                    // Ranged: released at firing range (they stay put and shoot)
                    bool memberInEngageRange = isRangedBattalion ? inFiringRange : inEncircleRange;
                    if (memberInEngageRange && em.HasComponent<Target>(_members[i]))
                    {
                        var mt = em.GetComponentData<Target>(_members[i]);
                        if (mt.Value != Entity.Null && em.Exists(mt.Value)
                            && em.HasComponent<Health>(mt.Value)
                            && em.GetComponentData<Health>(mt.Value).Value > 0)
                        {
                            _memberDistances[i] = -1f; // Released for combat movement in step 5a
                            continue;
                        }
                    }

                    // Formation mode: use existing Column/Row from BattalionMemberData
                    float3 target;
                    {
                        var md = em.GetComponentData<BattalionMemberData>(_members[i]);
                        float3 localOffset = BattalionFormation.ComputeSlotOffset(md.Column, md.Row, cols, rows, spacing);
                        target = leaderPos + math.mul(formationRot, localOffset);
                    }

                    // Tier C — BFME2 horde pathing.
                    // Slot validity is radius-aware: a slot is invalid for a
                    // member if its body would clip an obstacle there OR the
                    // straight-line path from current pos to slot would clip
                    // one along the way. Per-member radius lookup so siege
                    // pieces and infantry get correct clearance.
                    float memberRadiusForCheck = em.HasComponent<Radius>(_members[i])
                        ? em.GetComponentData<Radius>(_members[i]).Value : 0.5f;

                    bool slotBlocked = passGrid != null
                        && !passGrid.IsPassableForRadius(target, memberRadiusForCheck);

                    if (!slotBlocked && passGrid != null)
                    {
                        float3 toSlot = target - _memberPos[i];
                        toSlot.y = 0;
                        float slotDist = math.length(toSlot);
                        if (slotDist > spacing)
                        {
                            // Sampled geometric LOS — same code path StringPull
                            // uses for A* simplification. Catches the case
                            // where slot + current pos are both clear but
                            // the line between them clips a building corner.
                            if (!passGrid.HasClearLineOfSight(_memberPos[i], target, memberRadiusForCheck))
                                slotBlocked = true;
                        }
                    }

                    // Check if member is too far from leader (separated by obstacle)
                    float3 toLeader = _memberPos[i] - leaderPos;
                    toLeader.y = 0;
                    float formationRadius = math.max(cols, rows) * spacing;
                    bool tooFar = math.lengthsq(toLeader) > formationRadius * formationRadius * 4f;

                    if (slotBlocked || tooFar)
                    {
                        // Can't reach slot — follow the leader via flow field
                        target = leaderPos;
                        _followsLeader[i] = true;
                    }
                    else
                    {
                        _followsLeader[i] = false;
                    }

                    _targetPositions[i] = target;

                    float3 diff = target - _memberPos[i];
                    diff.y = 0f;
                    float dist = math.length(diff);
                    _memberDistances[i] = dist;
                    if (dist > maxDist) maxDist = dist;
                }

                // Leader speed fallback
                float leaderSpeed = bl.FollowSpeed;
                if (em.HasComponent<MoveSpeed>(entity))
                {
                    float s = em.GetComponentData<MoveSpeed>(entity).Value;
                    if (s > 0f) leaderSpeed = s;
                }

                // ── 5a. Move combat members toward their enemy targets ──
                // These members were released from formation (distances set to -1).
                // BattalionSyncSystem must move them since MovementSystem excludes battalion members.
                // Ranged members do NOT chase — they stay at their position and RangedCombatSystem fires.
                if (battalionInCombat && !isRangedBattalion)
                {
                    for (int i = 0; i < memberCount; i++)
                    {
                        if (!_memberAlive[i]) continue;
                        if (_memberDistances[i] >= 0f) continue; // In formation, handled below

                        var member = _members[i];
                        if (!em.HasComponent<Target>(member)) continue;
                        var mt = em.GetComponentData<Target>(member);
                        if (mt.Value == Entity.Null || !em.Exists(mt.Value)) continue;
                        if (!em.HasComponent<LocalTransform>(mt.Value)) continue;

                        float3 enemyPos = em.GetComponentData<LocalTransform>(mt.Value).Position;
                        var memberXf = em.GetComponentData<LocalTransform>(member);
                        float3 toEnemy = enemyPos - memberXf.Position;
                        toEnemy.y = 0;
                        float distToEnemy = math.length(toEnemy);

                        // Determine stop range: ranged units stop at firing range, melee at melee range
                        float stopRange;
                        bool isRanged = em.HasComponent<ArcherTag>(member);
                        if (isRanged && em.HasComponent<ArcherState>(member))
                        {
                            var archerState = em.GetComponentData<ArcherState>(member);
                            stopRange = archerState.MaxRange > 0 ? archerState.MaxRange - 2f : 23f;
                        }
                        else
                        {
                            stopRange = 1.5f;
                            if (em.HasComponent<Radius>(mt.Value))
                                stopRange += em.GetComponentData<Radius>(mt.Value).Value;
                        }
                        if (distToEnemy <= stopRange) continue;

                        float memberSpeed = leaderSpeed;
                        if (em.HasComponent<MoveSpeed>(member))
                        {
                            float ms = em.GetComponentData<MoveSpeed>(member).Value;
                            if (ms > 0f) memberSpeed = ms;
                        }

                        // PR3 — direct-line toward enemy. Battalion-wide
                        // routing is owned by the leader's navmesh path; the
                        // member just steers locally toward its target.
                        float3 dir = math.normalizesafe(toEnemy);
                        float step = math.min(memberSpeed * dt, distToEnemy);
                        float3 newPos = memberXf.Position + dir * step;

                        newPos.y = TerrainUtility.GetHeight(newPos.x, newPos.z);

                        // Face toward enemy
                        quaternion faceRot = math.lengthsq(toEnemy) > 0.01f
                            ? quaternion.LookRotationSafe(math.normalizesafe(toEnemy), new float3(0, 1, 0))
                            : memberXf.Rotation;

                        em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                            newPos, faceRot, memberXf.Scale));
                    }
                }

                // ── 5b. Move formation members toward their slot positions ──
                for (int i = 0; i < memberCount; i++)
                {
                    if (_memberDistances[i] < 0f) continue; // Dead or in combat

                    var member = _members[i];
                    var memberXf = em.GetComponentData<LocalTransform>(member);
                    float3 target = _targetPositions[i];
                    float dist = _memberDistances[i];

                    // Strip stale DesiredDestination for formation members
                    if (em.HasComponent<DesiredDestination>(member))
                        ecb.RemoveComponent<DesiredDestination>(member);

                    // Per-member collision radius — defaults to a half-cell if
                    // no Radius component (matches the old behaviour).
                    float memberRadius = em.HasComponent<Radius>(member)
                        ? em.GetComponentData<Radius>(member).Value : 0.5f;

                    // Slot-validity guard: if the formation slot itself sits on
                    // a building cell or wedged into a corner the member can't
                    // physically occupy, swap to follow-leader behaviour for
                    // this frame. Otherwise the member would walk straight at
                    // the bad slot and pile up on the building edge.
                    // (Nav-clearance fix #4.)
                    bool slotValid = passGrid == null
                        || passGrid.IsPassableForRadius(target, memberRadius);
                    if (!slotValid) target = leaderPos;

                    float3 newPos;

                    if (dist < 0.01f)
                    {
                        newPos = target;
                    }
                    else
                    {
                        float memberSpeed = leaderSpeed;
                        if (em.HasComponent<MoveSpeed>(member))
                        {
                            float ms = em.GetComponentData<MoveSpeed>(member).Value;
                            if (ms > 0f) memberSpeed = ms;
                        }

                        // PR3 — direct-line steering toward slot or leader.
                        // Battalion-wide routing is owned by the leader's
                        // navmesh path; the formation passability is
                        // implicitly correct because the leader's path
                        // routes around obstacles.
                        float3 dir = math.normalizesafe(target - memberXf.Position);

                        float speed;
                        if (_followsLeader[i] || !slotValid)
                        {
                            // Slot unreachable — chase the leader's position.
                            speed = memberSpeed;
                        }
                        else
                        {
                            // In formation — ratio-based speed so everyone arrives together.
                            float ratio = dist / math.max(maxDist, 0.01f);
                            speed = memberSpeed * math.clamp(ratio, 0.15f, 1.0f);
                        }

                        if (dist > spacing * 3f) speed = memberSpeed * 2f;

                        float maxStep = memberSpeed * 2f * dt;
                        float step = math.min(speed * dt, math.min(dist, maxStep));
                        newPos = memberXf.Position + dir * step;
                    }

                    newPos.y = TerrainUtility.GetHeight(newPos.x, newPos.z);

                    em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                        newPos, formationRot, memberXf.Scale));
                }
            }

            alignSnaps.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────
        // BFME2 ALIGNMENT SUPPORT
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// One snapshot row used by the alignment pass — an idle leader's
        /// world position + formation radius. Filled at the top of OnUpdate
        /// before the per-leader iteration so each leader can scan its
        /// peers without nested SystemAPI queries.
        /// </summary>
        private struct AlignmentSnap
        {
            public Entity Entity;
            public float3 Position;
            public float Radius;
        }
    }
}
