// BattalionSyncSystem.cs
// Moves battalion members toward their formation slot positions each frame.
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
            var passGrid = PassabilityGrid.Instance;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

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
                else
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
                if (runReassignment && aliveCount > 0)
                {
                    // Compute slot world positions for assignment (reuse cached array)
                    for (int row = 0; row < rows; row++)
                    {
                        for (int col = 0; col < cols; col++)
                        {
                            int idx = row * cols + col;
                            float3 localOffset = BattalionFormation.ComputeSlotOffset(col, row, cols, rows, spacing);
                            _slotWorldPositions[idx] = leaderPos + math.mul(formationRot, localOffset);
                        }
                    }

                    for (int s = 0; s < slotCount; s++)
                        _slotAssignment[s] = -1;
                    for (int m = 0; m < memberCount; m++)
                        _memberUsed[m] = false;

                    // Greedy front-to-back: for each slot, find closest alive unassigned member
                    for (int s = 0; s < slotCount; s++)
                    {
                        float3 slotPos = _slotWorldPositions[s];
                        float bestDist = float.MaxValue;
                        int bestMember = -1;

                        for (int m = 0; m < memberCount; m++)
                        {
                            if (!_memberAlive[m] || _memberUsed[m]) continue;

                            float3 diff = slotPos - _memberPos[m];
                            diff.y = 0f;
                            float d = math.lengthsq(diff);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestMember = m;
                            }
                        }

                        if (bestMember >= 0)
                        {
                            _slotAssignment[s] = bestMember;
                            _memberUsed[bestMember] = true;
                        }
                    }

                    // Write new assignments onto member components
                    for (int s = 0; s < slotCount; s++)
                    {
                        int mi = _slotAssignment[s];
                        if (mi < 0) continue;

                        int newCol = s % cols;
                        int newRow = s / cols;

                        var md = em.GetComponentData<BattalionMemberData>(_members[mi]);
                        if (md.Column != newCol || md.Row != newRow)
                        {
                            md.Column = newCol;
                            md.Row = newRow;
                            em.SetComponentData(_members[mi], md);
                        }
                    }

                    // Record the rotation used for this assignment
                    leader.ValueRW.LastAssignmentRot = formationRot;
                }

                // ── 3a. Clear stale leader target + BattalionAttackTarget ──
                if (em.HasComponent<Target>(entity))
                {
                    var lt = em.GetComponentData<Target>(entity);
                    if (lt.Value != Entity.Null)
                    {
                        bool targetGone = !em.Exists(lt.Value)
                            || !em.HasComponent<Health>(lt.Value)
                            || em.GetComponentData<Health>(lt.Value).Value <= 0;
                        if (targetGone)
                        {
                            // Try to reassign leader target to next living enemy from same battalion
                            bool reassigned = false;
                            if (em.HasComponent<BattalionAttackTarget>(entity))
                            {
                                var bat = em.GetComponentData<BattalionAttackTarget>(entity);
                                if (bat.EnemyLeader != Entity.Null && em.Exists(bat.EnemyLeader)
                                    && em.HasBuffer<BattalionMember>(bat.EnemyLeader))
                                {
                                    var enemyBuf = em.GetBuffer<BattalionMember>(bat.EnemyLeader);
                                    for (int ei = 0; ei < enemyBuf.Length; ei++)
                                    {
                                        var em2 = enemyBuf[ei].Value;
                                        if (em2 != Entity.Null && em.Exists(em2)
                                            && em.HasComponent<Health>(em2)
                                            && em.GetComponentData<Health>(em2).Value > 0)
                                        {
                                            em.SetComponentData(entity, new Target { Value = em2 });
                                            if (em.HasComponent<AttackCommand>(entity))
                                                em.SetComponentData(entity, new AttackCommand { Target = em2 });
                                            reassigned = true;
                                            break;
                                        }
                                    }
                                    if (!reassigned)
                                    {
                                        // All enemies from target battalion dead — clear tracking
                                        ecb.RemoveComponent<BattalionAttackTarget>(entity);
                                    }
                                }
                                else
                                {
                                    ecb.RemoveComponent<BattalionAttackTarget>(entity);
                                }
                            }

                            if (!reassigned)
                            {
                                em.SetComponentData(entity, new Target { Value = Entity.Null });
                                if (em.HasComponent<AttackCommand>(entity))
                                    ecb.RemoveComponent<AttackCommand>(entity);
                            }
                        }
                    }
                }

                // ── 3b. Compute own battalion center of mass ──
                float3 ownCenter = leaderPos;
                if (aliveCount > 0)
                {
                    float3 sum = float3.zero;
                    for (int i = 0; i < memberCount; i++)
                        if (_memberAlive[i]) sum += _memberPos[i];
                    ownCenter = sum / aliveCount;
                }

                // ── 3c. Check encirclement distance using battalion centers ──
                // Engagement is battalion-level: when the geometric centers of two
                // battalions are within EncircleDistance, per-member targets are assigned.
                const float EncircleDistance = 7f;
                bool inEncircleRange = false;
                bool battalionInCombat = false;

                if (em.HasComponent<Target>(entity))
                {
                    var lt = em.GetComponentData<Target>(entity);
                    if (lt.Value != Entity.Null && em.Exists(lt.Value)
                        && em.HasComponent<Health>(lt.Value)
                        && em.GetComponentData<Health>(lt.Value).Value > 0)
                    {
                        battalionInCombat = true;

                        // Compute enemy battalion center of mass
                        float3 enemyCenter = em.GetComponentData<LocalTransform>(lt.Value).Position;
                        if (em.HasComponent<BattalionAttackTarget>(entity))
                        {
                            var bat = em.GetComponentData<BattalionAttackTarget>(entity);
                            if (bat.EnemyLeader != Entity.Null && em.Exists(bat.EnemyLeader)
                                && em.HasBuffer<BattalionMember>(bat.EnemyLeader))
                            {
                                var eBuf = em.GetBuffer<BattalionMember>(bat.EnemyLeader);
                                float3 eSum = float3.zero;
                                int eCnt = 0;
                                for (int ei = 0; ei < eBuf.Length; ei++)
                                {
                                    var e2 = eBuf[ei].Value;
                                    if (e2 != Entity.Null && em.Exists(e2) && em.HasComponent<LocalTransform>(e2)
                                        && em.HasComponent<Health>(e2) && em.GetComponentData<Health>(e2).Value > 0)
                                    {
                                        eSum += em.GetComponentData<LocalTransform>(e2).Position;
                                        eCnt++;
                                    }
                                }
                                if (eCnt > 0) enemyCenter = eSum / eCnt;
                            }
                        }

                        float centerDist = math.length(new float2(ownCenter.x - enemyCenter.x, ownCenter.z - enemyCenter.z));
                        inEncircleRange = centerDist < EncircleDistance;

                        // Track enemy movement: update leader destination to follow the enemy battalion
                        if (!inEncircleRange && em.HasComponent<DesiredDestination>(entity))
                        {
                            em.SetComponentData(entity, new DesiredDestination { Position = enemyCenter, Has = 1 });
                        }
                        // Stop marching when in encircle range — combat takes over
                        else if (inEncircleRange && em.HasComponent<DesiredDestination>(entity))
                        {
                            em.SetComponentData(entity, new DesiredDestination { Has = 0 });
                        }

                        // If in encircle range, assign per-member targets from enemy battalion
                        if (inEncircleRange && em.HasComponent<BattalionAttackTarget>(entity))
                        {
                            var bat = em.GetComponentData<BattalionAttackTarget>(entity);
                            if (bat.EnemyLeader != Entity.Null && em.Exists(bat.EnemyLeader)
                                && em.HasBuffer<BattalionMember>(bat.EnemyLeader))
                            {
                                var enemyBuf = em.GetBuffer<BattalionMember>(bat.EnemyLeader);
                                for (int i = 0; i < memberCount; i++)
                                {
                                    if (!_memberAlive[i]) continue;
                                    // Skip members with living targets
                                    if (em.HasComponent<Target>(_members[i]))
                                    {
                                        var curTgt = em.GetComponentData<Target>(_members[i]);
                                        if (curTgt.Value != Entity.Null && em.Exists(curTgt.Value)
                                            && em.HasComponent<Health>(curTgt.Value)
                                            && em.GetComponentData<Health>(curTgt.Value).Value > 0)
                                            continue;
                                    }
                                    // Find nearest living enemy from enemy battalion
                                    Entity best = Entity.Null;
                                    float bestD = float.MaxValue;
                                    for (int ei = 0; ei < enemyBuf.Length; ei++)
                                    {
                                        var enemy = enemyBuf[ei].Value;
                                        if (enemy == Entity.Null || !em.Exists(enemy)) continue;
                                        if (!em.HasComponent<Health>(enemy) || em.GetComponentData<Health>(enemy).Value <= 0) continue;
                                        if (!em.HasComponent<LocalTransform>(enemy)) continue;
                                        float3 ePos = em.GetComponentData<LocalTransform>(enemy).Position;
                                        float3 diff = ePos - _memberPos[i];
                                        diff.y = 0;
                                        float d = math.lengthsq(diff);
                                        if (d < bestD) { bestD = d; best = enemy; }
                                    }
                                    if (best != Entity.Null)
                                        em.SetComponentData(_members[i], new Target { Value = best });
                                }
                            }
                        }
                    }
                }

                // ── 4. Compute per-member target positions ──
                float maxDist = 0f;

                for (int i = 0; i < memberCount; i++)
                {
                    if (!_memberAlive[i])
                    {
                        _memberDistances[i] = -1f;
                        continue;
                    }

                    // Members with a combat target in encircle range are released from formation
                    if (inEncircleRange && em.HasComponent<Target>(_members[i]))
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

                    // Check if the slot is reachable (passable terrain)
                    bool slotBlocked = passGrid != null && !passGrid.IsPassable(target);

                    // Check if the PATH from member to slot crosses impassable cells.
                    // Even if the slot itself is passable (between trees), the member
                    // may need to traverse through blocked cells to reach it.
                    if (!slotBlocked && passGrid != null)
                    {
                        float3 toSlot = target - _memberPos[i];
                        toSlot.y = 0;
                        float slotDist = math.length(toSlot);
                        if (slotDist > spacing)
                        {
                            float3 slotDir = toSlot / slotDist;
                            float cellSize = passGrid.CellSize;
                            float checkDist = slotDist;
                            for (float d = cellSize; d <= checkDist; d += cellSize)
                            {
                                float3 checkPos = _memberPos[i] + slotDir * d;
                                if (!passGrid.IsPassable(checkPos))
                                {
                                    slotBlocked = true;
                                    break;
                                }
                            }
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
                if (battalionInCombat)
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

                        float3 dir = math.normalizesafe(toEnemy);

                        // Use flow field for pathfinding around obstacles and allies
                        dir = FlowFieldMovementHelper.GetDirection(
                            memberXf.Position, enemyPos, dir, distToEnemy);

                        float step = math.min(memberSpeed * dt, distToEnemy);
                        float3 newPos = memberXf.Position + dir * step;

                        if (passGrid != null && !passGrid.IsPassable(newPos))
                        {
                            // Try alternate direction
                            float3 altDir = FlowFieldMovementHelper.GetDirection(
                                memberXf.Position, enemyPos, -dir, distToEnemy);
                            float3 altPos = memberXf.Position + altDir * step;
                            if (passGrid.IsPassable(altPos))
                                newPos = altPos;
                            else
                                newPos = memberXf.Position;
                        }

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

                        float3 dir = math.normalizesafe(target - memberXf.Position);

                        float speed;
                        if (_followsLeader[i])
                        {
                            // Following leader around obstacle — use flow field
                            dir = FlowFieldMovementHelper.GetDirection(
                                memberXf.Position, target, dir, dist);
                            speed = memberSpeed;
                        }
                        else
                        {
                            // In formation — ratio-based speed so everyone arrives together
                            float ratio = dist / math.max(maxDist, 0.01f);
                            speed = memberSpeed * math.clamp(ratio, 0.15f, 1.0f);
                        }

                        // Stray detection: if member is far from target, boost to 2x speed
                        if (dist > spacing * 3f)
                            speed = memberSpeed * 2f;

                        // Hard cap: never move more than 2x unit speed per frame
                        float maxStep = memberSpeed * 2f * dt;
                        float step = math.min(speed * dt, math.min(dist, maxStep));
                        newPos = memberXf.Position + dir * step;

                        // Passability check: don't walk into impassable cells
                        if (passGrid != null && !passGrid.IsPassable(newPos))
                        {
                            // Blocked — try to path toward leader using flow field instead
                            float3 ffDir = FlowFieldMovementHelper.GetDirection(
                                memberXf.Position, leaderPos, dir, dist);
                            float3 altPos = memberXf.Position + ffDir * step;
                            if (passGrid.IsPassable(altPos))
                                newPos = altPos;
                            else
                                newPos = memberXf.Position; // truly stuck, don't move
                        }
                    }

                    newPos.y = TerrainUtility.GetHeight(newPos.x, newPos.z);

                    em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                        newPos, formationRot, memberXf.Scale));
                }
            }
        }
    }
}
