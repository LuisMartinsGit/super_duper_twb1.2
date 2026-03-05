// File: Assets/Scripts/Systems/Economy/CaravanDeathSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Handles caravan death: loot distribution and escort cleanup.
    ///
    /// Runs BEFORE DeathSystem so it can process caravan-specific logic
    /// before the entity is destroyed.
    ///
    /// On caravan death:
    /// 1. Credits the killer's faction with 50% of the caravan's current cargo
    /// 2. Sets the escort's health to 0 (escort dies with caravan)
    /// 3. Decrements the origin TradeHub's active caravan count
    ///
    /// Escort death does NOT kill the caravan (escorts are expendable guards).
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
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================
            // Process dead caravans
            // =============================================================
            foreach (var (health, caravan, lastDamager, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<CaravanState>, RefRO<LastDamagedByFaction>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // --- Loot: credit killer's faction with 50% of cargo ---
                int lootAmount = (int)(caravan.ValueRO.CurrentCargo * DeathLootFraction);
                if (lootAmount > 0)
                {
                    Faction killerFaction = lastDamager.ValueRO.Value;
                    FactionEconomy.Add(em, killerFaction, Cost.Of(supplies: lootAmount));
                }

                // --- Kill escort ---
                Entity escort = caravan.ValueRO.EscortEntity;
                if (em.Exists(escort) && em.HasComponent<Health>(escort))
                {
                    var escortHealth = em.GetComponentData<Health>(escort);
                    if (escortHealth.Value > 0)
                    {
                        escortHealth.Value = 0;
                        ecb.SetComponent(escort, escortHealth);
                    }
                }

                // --- Decrement active caravan count on origin TradeHub ---
                Entity origin = caravan.ValueRO.Origin;
                if (em.Exists(origin) && em.HasComponent<TradeRoute>(origin))
                {
                    var route = em.GetComponentData<TradeRoute>(origin);
                    route.ActiveCaravans = math.max(0, route.ActiveCaravans - 1);
                    ecb.SetComponent(origin, route);
                }

                // DeathSystem will handle actual entity destruction
            }
        }
    }
}
