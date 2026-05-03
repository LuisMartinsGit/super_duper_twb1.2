// CadaverDecaySystem.cs
// Decays unmined cadavers so kills no miner picked up don't pile up forever.
// Location: Assets/Scripts/Systems/Crystal/CadaverDecaySystem.cs
// (task-062 Q-22)

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Counts down <see cref="CadaverState.DecayTimer"/> on every cadaver.
    /// CrystalMiningSystem resets the timer to <see cref="Entities.Cadaver.DecayLifetimeSeconds"/>
    /// every gather action, so an actively-mined cadaver never decays.
    /// Cadavers whose timer reaches zero are destroyed.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CadaverDecaySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CadaverState>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (cadaver, entity) in SystemAPI.Query<RefRW<CadaverState>>()
                .WithAll<CadaverTag>()
                .WithEntityAccess())
            {
                // Already-depleted cadavers are destroyed by CrystalMiningSystem;
                // skip them here to avoid double-destroy via ECB.
                if (cadaver.ValueRO.Depleted != 0) continue;

                cadaver.ValueRW.DecayTimer -= dt;
                if (cadaver.ValueRO.DecayTimer <= 0f)
                    ecb.DestroyEntity(entity);
            }
        }
    }
}
