// BattalionLeashSystem.cs
// Teleports members that fall too far behind to their slot position
// Location: Assets/Scripts/Systems/Movement/BattalionLeashSystem.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Runs after BattalionSyncSystem. For each member, if the distance to
    /// its computed slot position exceeds the leash distance, teleport the
    /// member directly to the slot. This handles sharp turns, combat
    /// displacement, and any situation where lerp cannot keep up.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BattalionSyncSystem))]
    public partial struct BattalionLeashSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattalionLeader>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

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
                float leashSq = bl.LeashDistance * bl.LeashDistance;

                for (int i = 0; i < buffer.Length; i++)
                {
                    var member = buffer[i].Value;
                    if (!em.Exists(member)) continue;
                    if (!em.HasComponent<LocalTransform>(member)) continue;
                    if (!em.HasComponent<BattalionMemberData>(member)) continue;

                    var memberData = em.GetComponentData<BattalionMemberData>(member);
                    var memberXf = em.GetComponentData<LocalTransform>(member);

                    // Compute slot position (same formula as BattalionSyncSystem)
                    float3 localOffset = new float3(
                        (memberData.Column - (bl.Columns - 1) * 0.5f) * bl.Spacing,
                        0f,
                        -(memberData.Row * bl.Spacing)
                    );
                    float3 slotWorldPos = leaderPos + math.mul(leaderRot, localOffset);
                    slotWorldPos.y = TerrainUtility.GetHeight(slotWorldPos.x, slotWorldPos.z);

                    // Check distance; teleport if beyond leash
                    float distSq = math.distancesq(memberXf.Position, slotWorldPos);
                    if (distSq > leashSq)
                    {
                        em.SetComponentData(member, LocalTransform.FromPositionRotationScale(
                            slotWorldPos, leaderRot, memberXf.Scale));
                    }
                }
            }
        }
    }
}
