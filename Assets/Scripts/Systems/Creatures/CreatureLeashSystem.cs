// CreatureLeashSystem.cs
// Creatures disengage and return to their camp when they stray too far.
// Prevents crystallings from chasing players across the entire map.
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Leash system for hostile creatures (crystallings).
    /// When a creature strays beyond LEASH_DISTANCE from its HomePosition,
    /// it drops its target, resets its guard point, and walks back home.
    /// The TargetingSystem's MaxGuardDistance then prevents re-acquisition
    /// until the creature is back near its camp.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureLeashSystem : ISystem
    {
        private const float LEASH_DISTANCE = 35f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (creature, transform, entity) in
                SystemAPI.Query<RefRO<CreatureState>, RefRO<LocalTransform>>()
                .WithAll<CreatureTag>()
                .WithEntityAccess())
            {
                float distFromHome = math.distance(transform.ValueRO.Position, creature.ValueRO.HomePosition);
                if (distFromHome <= LEASH_DISTANCE) continue;

                // Too far from camp — disengage and return home

                // Remove combat target so creature stops chasing
                if (em.HasComponent<Target>(entity))
                    ecb.RemoveComponent<Target>(entity);

                // Remove attack command if any
                if (em.HasComponent<AttackCommand>(entity))
                    ecb.RemoveComponent<AttackCommand>(entity);

                // Reset guard point to home so TargetingSystem won't
                // auto-acquire enemies far from camp
                if (em.HasComponent<GuardPoint>(entity))
                {
                    ecb.SetComponent(entity, new GuardPoint
                    {
                        Position = creature.ValueRO.HomePosition,
                        Has = 1
                    });
                }

                // Walk back home
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    ecb.SetComponent(entity, new DesiredDestination
                    {
                        Position = creature.ValueRO.HomePosition,
                        Has = 1
                    });
                }
                else
                {
                    ecb.AddComponent(entity, new DesiredDestination
                    {
                        Position = creature.ValueRO.HomePosition,
                        Has = 1
                    });
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
