// BattalionSyncSystem.cs
// Moves battalion members toward their formation slot positions each frame
// using speed-aware constant-velocity movement so all members converge together.
// Location: Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Per-frame system that moves battalion members toward their formation slots.
    /// Runs AFTER MovementSystem so the leader position is already updated.
    ///
    /// Two-pass algorithm:
    ///   Pass 1 — compute each member's slot and distance; find the max distance.
    ///   Pass 2 — ratio-based speed scaling: each member moves at its own speed
    ///            scaled by (dist / maxDist).  Members closer to their slot move
    ///            slower and arrive first; members farther away move at full speed.
    ///            On a new command, ahead members naturally slow while behind
    ///            members catch up.
    ///
    /// CRITICAL: Members do NOT have DesiredDestination, do NOT use flow fields.
    /// Movement is pure position step in this system only.
    /// </summary>
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

            // ECB for deferred structural changes — avoids invalidating the
            // SystemAPI.Query iteration when stripping stale DesiredDestination.
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

                int count = buffer.Length;
                var slotPositions = new NativeArray<float3>(count, Allocator.Temp);
                var memberDistances = new NativeArray<float>(count, Allocator.Temp);

                // ── Pass 1: compute slot positions and find max distance ──
                float maxDist = 0f;

                for (int i = 0; i < count; i++)
                {
                    var member = buffer[i].Value;
                    if (!em.Exists(member) ||
                        !em.HasComponent<LocalTransform>(member) ||
                        !em.HasComponent<BattalionMemberData>(member))
                    {
                        slotPositions[i] = float3.zero;
                        memberDistances[i] = -1f; // sentinel: skip
                        continue;
                    }

                    var memberData = em.GetComponentData<BattalionMemberData>(member);

                    // Centered offset via shared helper
                    float3 localOffset = BattalionFormation.ComputeSlotOffset(
                        memberData.Column, memberData.Row, bl.Columns, bl.Rows, bl.Spacing);
                    float3 slotWorldPos = leaderPos + math.mul(leaderRot, localOffset);
                    slotPositions[i] = slotWorldPos;

                    var memberXf = em.GetComponentData<LocalTransform>(member);
                    float3 diff = slotWorldPos - memberXf.Position;
                    diff.y = 0f; // ignore vertical for distance calc
                    float dist = math.length(diff);
                    memberDistances[i] = dist;
                    if (dist > maxDist) maxDist = dist;
                }

                // Leader speed for arrival-time calculation
                float leaderSpeed = bl.FollowSpeed; // fallback
                if (em.HasComponent<MoveSpeed>(entity))
                {
                    float s = em.GetComponentData<MoveSpeed>(entity).Value;
                    if (s > 0f) leaderSpeed = s;
                }

                // ── Pass 2: move each member toward its slot — ratio-based speed ──
                for (int i = 0; i < count; i++)
                {
                    if (memberDistances[i] < 0f) continue; // invalid member

                    var member = buffer[i].Value;
                    var memberXf = em.GetComponentData<LocalTransform>(member);
                    float3 slotWorldPos = slotPositions[i];
                    float dist = memberDistances[i];

                    // Safety: strip DesiredDestination if combat system added one (deferred via ECB)
                    if (em.HasComponent<DesiredDestination>(member))
                        ecb.RemoveComponent<DesiredDestination>(member);

                    float3 newPos;

                    if (dist < 0.01f)
                    {
                        // Already at slot — snap and wait for others
                        newPos = slotWorldPos;
                    }
                    else
                    {
                        // Each member moves at its own MoveSpeed, scaled by distance ratio
                        float memberSpeed = leaderSpeed;
                        if (em.HasComponent<MoveSpeed>(member))
                        {
                            float ms = em.GetComponentData<MoveSpeed>(member).Value;
                            if (ms > 0f) memberSpeed = ms;
                        }

                        // ratio: close members move slowly, far members move at full speed.
                        // On a new command, ahead members (small dist) naturally slow
                        // while behind members (large dist) catch up.
                        float ratio = dist / math.max(maxDist, 0.01f);
                        float scaledSpeed = memberSpeed * math.max(ratio, 0.15f);

                        float step = math.min(scaledSpeed * dt, dist);
                        float3 dir = math.normalizesafe(slotWorldPos - memberXf.Position);
                        newPos = memberXf.Position + dir * step;
                    }

                    // Snap Y to terrain height
                    newPos.y = TerrainUtility.GetHeight(newPos.x, newPos.z);

                    // Update member transform: position + leader rotation
                    em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                        newPos, leaderRot, memberXf.Scale));
                }

                slotPositions.Dispose();
                memberDistances.Dispose();
            }
        }
    }
}
