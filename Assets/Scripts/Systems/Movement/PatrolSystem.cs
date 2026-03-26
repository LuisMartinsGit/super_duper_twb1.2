// File: Assets/Scripts/Systems/Movement/PatrolSystem.cs
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Handles patrol waypoint cycling for units with PatrolTag.
    ///
    /// When a patrolling unit reaches its current waypoint (DesiredDestination.Has == 0),
    /// this system advances to the next waypoint in the PatrolWaypoint buffer and sets
    /// a new DesiredDestination so the unit keeps moving back and forth.
    ///
    /// Runs after MovementSystem (which clears DesiredDestination on arrival)
    /// and before TargetingSystem (which handles auto-targeting for patrol units).
    ///
    /// PatrolTag units are treated like AttackMoveTag units by TargetingSystem:
    /// they auto-acquire enemies within LOS while moving. After combat ends,
    /// TargetingSystem's return-to-guard logic resumes movement toward the
    /// current patrol waypoint via GuardPoint.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    [UpdateBefore(typeof(Combat.TargetingSystem))]
    public partial struct PatrolSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (patrol, dd, entity) in SystemAPI
                .Query<RefRW<PatrolAgent>, RefRO<DesiredDestination>>()
                .WithAll<PatrolTag, UnitTag>()
                .WithEntityAccess())
            {
                // Only advance when the unit has arrived at its current waypoint
                if (dd.ValueRO.Has != 0) continue;

                // Skip if unit is in combat (has an active target)
                if (em.HasComponent<Target>(entity))
                {
                    var target = em.GetComponentData<Target>(entity);
                    if (target.Value != Entity.Null) continue;
                }

                // Get waypoint buffer
                if (!em.HasBuffer<PatrolWaypoint>(entity)) continue;
                var waypoints = em.GetBuffer<PatrolWaypoint>(entity);
                if (waypoints.Length < 2) continue;

                // Advance to next waypoint (ping-pong: 0 -> 1 -> 0 -> 1 ...)
                int currentIndex = patrol.ValueRO.Index;
                int nextIndex = (currentIndex + 1) % waypoints.Length;
                patrol.ValueRW.Index = nextIndex;

                float3 nextPos = waypoints[nextIndex].Position;

                // Set new destination
                ecb.SetComponent(entity, new DesiredDestination
                {
                    Position = nextPos,
                    Has = 1
                });

                // Update guard point to current waypoint so after-combat return
                // sends the unit back to its patrol path
                if (em.HasComponent<GuardPoint>(entity))
                {
                    ecb.SetComponent(entity, new GuardPoint
                    {
                        Position = nextPos,
                        Has = 1
                    });
                }
            }
        }
    }
}
