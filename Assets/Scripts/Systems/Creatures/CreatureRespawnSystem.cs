// File: Assets/Scripts/Systems/Creatures/CreatureRespawnSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Counts down creature spawn point timers and respawns creatures
    /// at their original home positions after the timer expires.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureRespawnSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureSpawnPoint>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (spawnPoint, entity) in SystemAPI
                .Query<RefRW<CreatureSpawnPoint>>()
                .WithEntityAccess())
            {
                ref var sp = ref spawnPoint.ValueRW;
                sp.RespawnTimer -= dt;

                if (sp.RespawnTimer <= 0f)
                {
                    // Respawn creature at home position
                    Creature.Create(ecb, sp.Position);

                    // Remove the spawn point entity
                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
