// File: Assets/Scripts/Systems/Creatures/CreatureWanderSystem.cs
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Makes creatures periodically wander to random positions near their home.
    /// Creatures stay within WanderRadius of their HomePosition.
    /// Only activates when the creature has no combat target and isn't already moving.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureWanderSystem : ISystem
    {
        private uint _randomSeed;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureTag>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _randomSeed = 12345;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            _randomSeed += 1;
            var random = new Random(_randomSeed);

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (creatureState, transform, entity) in SystemAPI
                .Query<RefRW<CreatureState>, RefRO<LocalTransform>>()
                .WithAll<CreatureTag>()
                .WithNone<Target>()
                .WithNone<AttackCommand>()
                .WithEntityAccess())
            {
                ref var creature = ref creatureState.ValueRW;

                // Skip if already moving to a destination
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    var dd = em.GetComponentData<DesiredDestination>(entity);
                    if (dd.Has != 0)
                    {
                        // Still count down timer while moving so wander resumes naturally
                        creature.WanderTimer -= dt;
                        continue;
                    }
                }

                // Count down wander timer
                creature.WanderTimer -= dt;

                if (creature.WanderTimer <= 0f)
                {
                    // Pick a random position within wander radius of home
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float dist = random.NextFloat(2f, creature.WanderRadius);
                    float3 wanderTarget = creature.HomePosition + new float3(
                        math.cos(angle) * dist,
                        0f,
                        math.sin(angle) * dist
                    );

                    // Set Y to match terrain (approximate - use home Y if no terrain query)
                    wanderTarget.y = creature.HomePosition.y;

                    // Set destination using ECB to avoid structural changes during iteration
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = wanderTarget,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = wanderTarget,
                            Has = 1
                        });
                    }

                    // Reset timer with some randomness
                    creature.WanderTimer = creature.WanderInterval + random.NextFloat(-2f, 2f);
                }
            }
        }
    }
}
