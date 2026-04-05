// File: Assets/Scripts/Systems/Economy/CaravanDeathSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Handles trader death: loot distribution.
    ///
    /// Runs BEFORE DeathSystem so it can process trader-specific logic
    /// before the entity is destroyed.
    ///
    /// On trader death:
    /// 1. Credits the killer's faction with 50% of accumulated resources
    ///
    /// DeathSystem handles actual entity destruction.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct CaravanDeathSystem : ISystem
    {
        private const float DeathLootFraction = 0.5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Process dead traders (RunaiTraderState system)
            foreach (var (health, trader, lastDamager, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<RunaiTraderState>, RefRO<LastDamagedByFaction>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // Loot: credit killer's faction with 50% of accumulated resources
                int lootSupplies = (int)(trader.ValueRO.AccumulatedSupplies * DeathLootFraction);
                int lootCrystal = (int)(trader.ValueRO.AccumulatedCrystal * DeathLootFraction);

                if (lootSupplies > 0 || lootCrystal > 0)
                {
                    Faction killerFaction = lastDamager.ValueRO.Value;
                    FactionEconomy.Add(em, killerFaction, Cost.Of(supplies: lootSupplies, crystal: lootCrystal));
                }

                // DeathSystem will handle actual entity destruction
            }
        }
    }
}
