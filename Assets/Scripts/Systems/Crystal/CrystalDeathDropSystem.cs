// File: Assets/Scripts/Systems/Crystal/CrystalDeathDropSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Intercepts crystal entity deaths (units and buildings) before DeathSystem
    /// destroys them. Spawns a mineable loot pile (Cadaver) at the death position
    /// containing 50% of the entity's build cost as crystal.
    ///
    /// Any entity with both Health &lt;= 0 and CrystalResourceValue gets a cadaver
    /// spawned at its position. The cadaver amount = BuildCost / 2.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct CrystalDeathDropSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalResourceValue>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, transform, resourceValue, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<CrystalResourceValue>>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                var pos = transform.ValueRO.Position;
                int lootAmount = resourceValue.ValueRO.BuildCost / 2;

                if (lootAmount > 0)
                {
                    Cadaver.Create(ecb, pos, lootAmount);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
