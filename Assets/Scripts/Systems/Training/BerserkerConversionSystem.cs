// File: Assets/Scripts/Systems/Training/BerserkerConversionSystem.cs
// Processes miner-to-berserker conversion at Fiendstone Keep.
// Miners with ConvertCommand walk to the Keep and are destroyed/replaced with Berserkers.

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
            public Entity Miner;
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

            foreach (var (convertCmd, transform, factionTag, entity) in SystemAPI
                         .Query<RefRO<ConvertCommand>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<MinerTag>()
                         .WithEntityAccess())
            {
                var keep = convertCmd.ValueRO.TargetKeep;

                // Validate keep still exists
                if (!em.Exists(keep) || !em.HasComponent<FiendstoneKeepTag>(keep))
                {
                    ecb.RemoveComponent<ConvertCommand>(entity);
                    continue;
                }

                var keepPos = em.GetComponentData<LocalTransform>(keep).Position;
                var minerPos = transform.ValueRO.Position;
                float dist = math.distance(
                    new float2(minerPos.x, minerPos.z),
                    new float2(keepPos.x, keepPos.z));

                if (dist <= ConversionRange)
                {
                    // In range — queue conversion (deferred to avoid structural changes during iteration)
                    conversions.Add(new DeferredConversion
                    {
                        Miner = entity,
                        Position = minerPos,
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

                // Destroy the miner
                ecb.DestroyEntity(conv.Miner);

                // Spawn berserker at the same position
                Berserker.Create(ecb, conv.Position, conv.Faction);

                UnityEngine.Debug.Log($"Converted Miner to Berserker for {conv.Faction} at {conv.Position}");
            }

            conversions.Dispose();
            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
