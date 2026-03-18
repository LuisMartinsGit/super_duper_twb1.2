// BattalionSyncSystem.cs
// Lerps battalion members toward their formation slot positions each frame
// Location: Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs

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
    /// For each leader entity:
    ///   - Read leader position and rotation
    ///   - For each member in buffer: compute world-space slot from leader transform + formation offset
    ///   - Lerp member position toward slot, snap Y to terrain, copy leader rotation
    ///
    /// CRITICAL: Members do NOT have DesiredDestination, do NOT use flow fields.
    /// Movement is pure position lerp in this system only.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct BattalionSyncSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattalionLeader>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

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
                float lerpRate = bl.FollowSpeed * dt;

                for (int i = 0; i < buffer.Length; i++)
                {
                    var member = buffer[i].Value;
                    if (!em.Exists(member)) continue;
                    if (!em.HasComponent<LocalTransform>(member)) continue;
                    if (!em.HasComponent<BattalionMemberData>(member)) continue;

                    var memberData = em.GetComponentData<BattalionMemberData>(member);
                    var memberXf = em.GetComponentData<LocalTransform>(member);

                    // Compute world-space slot position from leader transform + formation offset
                    float3 localOffset = new float3(
                        (memberData.Column - (bl.Columns - 1) * 0.5f) * bl.Spacing,
                        0f,
                        -(memberData.Row * bl.Spacing)
                    );
                    float3 slotWorldPos = leaderPos + math.mul(leaderRot, localOffset);

                    // Lerp member position toward slot
                    float3 newPos = math.lerp(memberXf.Position, slotWorldPos, math.saturate(lerpRate));

                    // Snap Y to terrain height
                    newPos.y = TerrainUtility.GetHeight(newPos.x, newPos.z);

                    // Update member transform: position + leader rotation
                    em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                        newPos, leaderRot, memberXf.Scale));
                }
            }
        }
    }
}
