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

namespace TheWaningBorder.Systems.Movement
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct BattalionSyncSystem : ISystem
    {
        // Reassign slots when formation rotates more than this (radians ≈ 30°)
        private const float ReassignAngleThreshold = 0.52f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattalionLeader>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
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
                var members     = new NativeArray<Entity>(memberCount, Allocator.Temp);
                var memberPos   = new NativeArray<float3>(memberCount, Allocator.Temp);
                var memberAlive = new NativeArray<bool>(memberCount, Allocator.Temp);
                int aliveCount = 0;

                for (int i = 0; i < memberCount; i++)
                {
                    var m = buffer[i].Value;
                    members[i] = m;
                    if (em.Exists(m) && em.HasComponent<LocalTransform>(m) && em.HasComponent<BattalionMemberData>(m))
                    {
                        memberPos[i] = em.GetComponentData<LocalTransform>(m).Position;
                        memberAlive[i] = true;
                        aliveCount++;
                    }
                    else
                    {
                        memberPos[i] = float3.zero;
                        memberAlive[i] = false;
                    }
                }

                // ── 3. Greedy nearest-slot assignment (only when needed) ──
                if (runReassignment && aliveCount > 0)
                {
                    // Compute slot world positions for assignment
                    var slotWorldPositions = new NativeArray<float3>(slotCount, Allocator.Temp);
                    for (int row = 0; row < rows; row++)
                    {
                        for (int col = 0; col < cols; col++)
                        {
                            int idx = row * cols + col;
                            float3 localOffset = BattalionFormation.ComputeSlotOffset(col, row, cols, rows, spacing);
                            slotWorldPositions[idx] = leaderPos + math.mul(formationRot, localOffset);
                        }
                    }

                    var slotAssignment = new NativeArray<int>(slotCount, Allocator.Temp);
                    var memberUsed = new NativeArray<bool>(memberCount, Allocator.Temp);

                    for (int s = 0; s < slotCount; s++)
                        slotAssignment[s] = -1;

                    // Greedy front-to-back: for each slot, find closest alive unassigned member
                    for (int s = 0; s < slotCount; s++)
                    {
                        float3 slotPos = slotWorldPositions[s];
                        float bestDist = float.MaxValue;
                        int bestMember = -1;

                        for (int m = 0; m < memberCount; m++)
                        {
                            if (!memberAlive[m] || memberUsed[m]) continue;

                            float3 diff = slotPos - memberPos[m];
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
                            slotAssignment[s] = bestMember;
                            memberUsed[bestMember] = true;
                        }
                    }

                    // Write new assignments onto member components
                    for (int s = 0; s < slotCount; s++)
                    {
                        int mi = slotAssignment[s];
                        if (mi < 0) continue;

                        int newCol = s % cols;
                        int newRow = s / cols;

                        var md = em.GetComponentData<BattalionMemberData>(members[mi]);
                        if (md.Column != newCol || md.Row != newRow)
                        {
                            md.Column = newCol;
                            md.Row = newRow;
                            em.SetComponentData(members[mi], md);
                        }
                    }

                    // Record the rotation used for this assignment
                    leader.ValueRW.LastAssignmentRot = formationRot;

                    slotWorldPositions.Dispose();
                    slotAssignment.Dispose();
                    memberUsed.Dispose();
                }

                // ── 4. Compute per-member target positions using sticky Column/Row ──
                var targetPositions = new NativeArray<float3>(memberCount, Allocator.Temp);
                var memberDistances = new NativeArray<float>(memberCount, Allocator.Temp);
                var followsLeader  = new NativeArray<bool>(memberCount, Allocator.Temp);
                float maxDist = 0f;

                for (int i = 0; i < memberCount; i++)
                {
                    if (!memberAlive[i])
                    {
                        memberDistances[i] = -1f;
                        continue;
                    }

                    // Use existing Column/Row from BattalionMemberData (sticky assignment)
                    var md = em.GetComponentData<BattalionMemberData>(members[i]);
                    float3 localOffset = BattalionFormation.ComputeSlotOffset(md.Column, md.Row, cols, rows, spacing);
                    float3 target = leaderPos + math.mul(formationRot, localOffset);

                    // Check if the slot is reachable (passable terrain)
                    bool slotBlocked = passGrid != null && !passGrid.IsPassable(target);

                    // Check if the PATH from member to slot crosses impassable cells.
                    // Even if the slot itself is passable (between trees), the member
                    // may need to traverse through blocked cells to reach it.
                    if (!slotBlocked && passGrid != null)
                    {
                        float3 toSlot = target - memberPos[i];
                        toSlot.y = 0;
                        float slotDist = math.length(toSlot);
                        if (slotDist > spacing)
                        {
                            float3 slotDir = toSlot / slotDist;
                            float cellSize = passGrid.CellSize;
                            float checkDist = slotDist;
                            for (float d = cellSize; d <= checkDist; d += cellSize)
                            {
                                float3 checkPos = memberPos[i] + slotDir * d;
                                if (!passGrid.IsPassable(checkPos))
                                {
                                    slotBlocked = true;
                                    break;
                                }
                            }
                        }
                    }

                    // Check if member is too far from leader (separated by obstacle)
                    float3 toLeader = memberPos[i] - leaderPos;
                    toLeader.y = 0;
                    float formationRadius = math.max(cols, rows) * spacing;
                    bool tooFar = math.lengthsq(toLeader) > formationRadius * formationRadius * 4f;

                    if (slotBlocked || tooFar)
                    {
                        // Can't reach slot — follow the leader via flow field
                        target = leaderPos;
                        followsLeader[i] = true;
                    }
                    else
                    {
                        followsLeader[i] = false;
                    }

                    targetPositions[i] = target;

                    float3 diff = target - memberPos[i];
                    diff.y = 0f;
                    float dist = math.length(diff);
                    memberDistances[i] = dist;
                    if (dist > maxDist) maxDist = dist;
                }

                // Leader speed fallback
                float leaderSpeed = bl.FollowSpeed;
                if (em.HasComponent<MoveSpeed>(entity))
                {
                    float s = em.GetComponentData<MoveSpeed>(entity).Value;
                    if (s > 0f) leaderSpeed = s;
                }

                // ── 5. Move each member toward its target ──
                for (int i = 0; i < memberCount; i++)
                {
                    if (memberDistances[i] < 0f) continue;

                    var member = members[i];
                    var memberXf = em.GetComponentData<LocalTransform>(member);
                    float3 target = targetPositions[i];
                    float dist = memberDistances[i];

                    // Strip stale DesiredDestination
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
                        if (followsLeader[i])
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

                // Cleanup
                members.Dispose();
                memberPos.Dispose();
                memberAlive.Dispose();
                targetPositions.Dispose();
                memberDistances.Dispose();
                followsLeader.Dispose();
            }
        }
    }
}
