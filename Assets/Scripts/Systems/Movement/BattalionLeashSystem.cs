// BattalionLeashSystem.cs
// Teleports the (invisible) battalion leader to the average position of its
// alive members when they drift too far apart, instead of teleporting each
// member back to its slot — which used to be visible as soldiers snapping
// across the screen.
// Location: Assets/Scripts/Systems/Movement/BattalionLeashSystem.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Runs after BattalionSyncSystem. If the leader has drifted further than
    /// LeashDistance from the centre of its members, snaps the leader back to
    /// the members' average position. The leader is invisible, so this is
    /// imperceptible — whereas snapping members back was visible teleporting.
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
                .Query<RefRO<BattalionLeader>, RefRW<LocalTransform>>()
                .WithAll<BattalionTag>()
                .WithEntityAccess())
            {
                if (!em.HasBuffer<BattalionMember>(entity)) continue;

                var buffer = em.GetBuffer<BattalionMember>(entity);
                var bl = leader.ValueRO;
                float3 leaderPos = leaderXf.ValueRO.Position;
                float leashSq = bl.LeashDistance * bl.LeashDistance;

                // Average position of all alive members in this battalion
                float3 sum = float3.zero;
                int alive = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    var m = buffer[i].Value;
                    if (m == Entity.Null || !em.Exists(m)) continue;
                    if (!em.HasComponent<LocalTransform>(m)) continue;
                    if (em.HasComponent<Health>(m)
                        && em.GetComponentData<Health>(m).Value <= 0) continue;

                    sum += em.GetComponentData<LocalTransform>(m).Position;
                    alive++;
                }
                if (alive == 0) continue;

                float3 avgPos = sum / alive;

                // Leader has drifted too far from the cluster — snap it to the cluster.
                if (math.distancesq(leaderPos, avgPos) > leashSq)
                {
                    avgPos.y = TerrainUtility.GetHeight(avgPos.x, avgPos.z);
                    var xf = leaderXf.ValueRW;
                    xf.Position = avgPos;
                    leaderXf.ValueRW = xf;
                }
            }
        }
    }
}
