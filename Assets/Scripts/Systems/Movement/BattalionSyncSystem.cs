// BattalionSyncSystem.cs
// Moves battalion members toward their formation slot positions each frame.
// On direction change the formation grid rotates instantly and members are
// reassigned to whichever slot is closest (front-to-back greedy), so nobody
// crosses paths.  This replicates the BFME2 about-face behaviour.
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
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattalionLeader>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (leader, leaderXf, entity) in SystemAPI
                .Query<RefRO<BattalionLeader>, RefRO<LocalTransform>>()
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

                // ── 1. Compute all slot world positions (using leader rotation directly) ──
                var slotWorldPositions = new NativeArray<float3>(slotCount, Allocator.Temp);
                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        int idx = row * cols + col;
                        float3 localOffset = BattalionFormation.ComputeSlotOffset(col, row, cols, rows, spacing);
                        slotWorldPositions[idx] = leaderPos + math.mul(leaderRot, localOffset);
                    }
                }

                // ── 2. Collect living members and their current positions ──
                var members      = new NativeArray<Entity>(memberCount, Allocator.Temp);
                var memberPos    = new NativeArray<float3>(memberCount, Allocator.Temp);
                var memberAlive  = new NativeArray<bool>(memberCount, Allocator.Temp);
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

                // ── 3. Greedy nearest-slot assignment (front row first) ──
                // For each slot (iterated row 0 → last row, i.e. front-to-back),
                // pick the closest unassigned living member.
                var slotAssignment = new NativeArray<int>(slotCount, Allocator.Temp); // slotIdx → memberIdx
                var memberUsed     = new NativeArray<bool>(memberCount, Allocator.Temp);

                for (int s = 0; s < slotCount; s++)
                    slotAssignment[s] = -1;

                int slotsToFill = math.min(slotCount, aliveCount);

                for (int s = 0; s < slotCount && slotsToFill > 0; s++)
                {
                    float3 slotPos = slotWorldPositions[s];
                    float bestDist = float.MaxValue;
                    int bestMember = -1;

                    for (int m = 0; m < memberCount; m++)
                    {
                        if (!memberAlive[m] || memberUsed[m]) continue;

                        float3 diff = slotPos - memberPos[m];
                        diff.y = 0f;
                        float d = math.lengthsq(diff); // squared is fine for comparison
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
                        slotsToFill--;
                    }
                }

                // ── 4. Write new slot assignments onto member components ──
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

                // ── 5. Compute per-member target positions and max distance ──
                var targetPositions  = new NativeArray<float3>(memberCount, Allocator.Temp);
                var memberDistances  = new NativeArray<float>(memberCount, Allocator.Temp);
                float maxDist = 0f;

                for (int i = 0; i < memberCount; i++)
                {
                    if (!memberAlive[i])
                    {
                        memberDistances[i] = -1f;
                        continue;
                    }

                    var md = em.GetComponentData<BattalionMemberData>(members[i]);
                    float3 localOffset = BattalionFormation.ComputeSlotOffset(md.Column, md.Row, cols, rows, spacing);
                    float3 slotWorld = leaderPos + math.mul(leaderRot, localOffset);
                    targetPositions[i] = slotWorld;

                    float3 diff = slotWorld - memberPos[i];
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

                // ── 6. Move each member toward its assigned slot ──
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

                        float ratio = dist / math.max(maxDist, 0.01f);
                        float scaledSpeed = memberSpeed * math.max(ratio, 0.15f);
                        float step = math.min(scaledSpeed * dt, dist);
                        float3 dir = math.normalizesafe(target - memberXf.Position);
                        newPos = memberXf.Position + dir * step;
                    }

                    newPos.y = TerrainUtility.GetHeight(newPos.x, newPos.z);

                    em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                        newPos, leaderRot, memberXf.Scale));
                }

                // Cleanup
                slotWorldPositions.Dispose();
                members.Dispose();
                memberPos.Dispose();
                memberAlive.Dispose();
                slotAssignment.Dispose();
                memberUsed.Dispose();
                targetPositions.Dispose();
                memberDistances.Dispose();
            }
        }
    }
}
