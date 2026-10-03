// Processes worker-to-berserker conversion at Fiendstone Keep.
// Workers with ConvertCommand walk to the Keep and are destroyed/replaced with Berserkers.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Training
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BerserkerConversionSystem : ISystem
    {
        private const float ConversionRange = 3f;

        private struct DeferredConversion
        {
            public Entity Worker;
            public float3 Position;
            public Faction Faction;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ConvertCommand>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var conversions = new NativeList<DeferredConversion>(4, Allocator.Temp);

            // Dead or dying workers are DeathSystem's property — converting one
            // sync-destroys an entity that DeathSystem's EndSimulation buffer
            // already holds commands for, which throws at playback.
            foreach (var (convertCmd, transform, factionTag, entity) in SystemAPI
                         .Query<RefRO<ConvertCommand>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<WorkerTag>()
                         .WithNone<DeathAnimationState>()
                         .WithEntityAccess())
            {
                if (em.HasComponent<Health>(entity) &&
                    em.GetComponentData<Health>(entity).Value <= 0)
                    continue;

                var keep = convertCmd.ValueRO.TargetKeep;

                // Validate keep still exists
                if (!em.Exists(keep) || !em.HasComponent<FiendstoneKeepTag>(keep))
                {
                    ecb.RemoveComponent<ConvertCommand>(entity);
                    continue;
                }

                var keepPos = em.GetComponentData<LocalTransform>(keep).Position;
                var workerPos = transform.ValueRO.Position;
                float dist = math.distance(
                    new float2(workerPos.x, workerPos.z),
                    new float2(keepPos.x, keepPos.z));

                if (dist <= ConversionRange)
                {
                    // In range — queue conversion (deferred to avoid structural changes during iteration)
                    conversions.Add(new DeferredConversion
                    {
                        Worker = entity,
                        Position = workerPos,
                        Faction = factionTag.ValueRO.Value
                    });
                }
                else
                {
                    // Not in range — move toward keep
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        var dest = em.GetComponentData<DesiredDestination>(entity);
                        if (dest.Has == 0)
                        {
                            ecb.SetComponent(entity, new DesiredDestination
                            {
                                Position = keepPos,
                                Has = 1
                            });
                        }
                    }
                }
            }

            // Execute deferred conversions (structural changes safe outside iteration)
            for (int i = 0; i < conversions.Length; i++)
            {
                var conv = conversions[i];

                // Destroy the worker
                ecb.DestroyEntity(conv.Worker);

                // Spawn berserker at the same position
                Berserker.Create(ecb, conv.Position, conv.Faction);

            }

            conversions.Dispose();
            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
