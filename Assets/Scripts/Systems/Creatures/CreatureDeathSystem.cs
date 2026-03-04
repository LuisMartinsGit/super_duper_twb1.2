// File: Assets/Scripts/Systems/Creatures/CreatureDeathSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Intercepts creature deaths before DeathSystem destroys them.
    /// Spawns a cadaver entity at the creature's position and creates
    /// a spawn point entity for respawning after 3 minutes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct CreatureDeathSystem : ISystem
    {
        private const float RespawnTime = 180f; // 3 minutes

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, transform, creatureState, damage, moveSpeed, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<CreatureState>,
                       RefRO<Damage>, RefRO<MoveSpeed>>()
                .WithAll<CreatureTag>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                var pos = transform.ValueRO.Position;

                // Spawn cadaver at creature's death position
                Cadaver.Create(ecb, pos);

                // Create spawn point for respawning
                var spawnPointEntity = ecb.CreateEntity();
                ecb.AddComponent(spawnPointEntity, new CreatureSpawnPoint
                {
                    Position = creatureState.ValueRO.HomePosition,
                    RespawnTimer = RespawnTime,
                    CreatureHP = health.ValueRO.Max,
                    CreatureDamage = damage.ValueRO.Value,
                    CreatureSpeed = moveSpeed.ValueRO.Value
                });
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
