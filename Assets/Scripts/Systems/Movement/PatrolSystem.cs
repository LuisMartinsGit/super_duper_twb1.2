// File: Assets/Scripts/Systems/Movement/PatrolSystem.cs
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Handles patrol waypoint cycling for units with PatrolTag.
    ///
    /// When a patrolling unit reaches its current waypoint (DesiredDestination.Has == 0),
    /// this system advances to the next waypoint in the PatrolWaypoint buffer and sets
    /// a new DesiredDestination so the unit keeps moving back and forth.
    ///
    /// task-112 M4: UpdateAfter migrated from MovementSystem (deleted) to
    /// UnitIntegratorSystem. Behaviour unchanged: still runs after the
    /// per-tick movement integration (so DesiredDestination.Has == 0 on
    /// arrival is observable) and before TargetingSystem.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UnitIntegratorSystem))]
    [UpdateBefore(typeof(Combat.TargetingSystem))]
    public partial struct PatrolSystem : ISystem
    {
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
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
