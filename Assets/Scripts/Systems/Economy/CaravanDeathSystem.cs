// File: Assets/Scripts/Systems/Economy/CaravanDeathSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Handles trader death: loot distribution and lane bookkeeping.
    ///
    /// Runs BEFORE DeathSystem so it can process trader-specific logic
    /// before the entity is destroyed.
    ///
    /// On trader death:
    /// 1. Credits the killer's faction with 50% of the trader's current cargo
    /// 2. Decrements the lane's ActiveTraders count
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
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================
            // Process dead traders (new TraderState system)
            // =============================================================
            foreach (var (health, trader, lastDamager, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<TraderState>, RefRO<LastDamagedByFaction>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // --- Loot: credit killer's faction with 50% of cargo ---
                int lootAmount = (int)(trader.ValueRO.CurrentCargo * DeathLootFraction);
                if (lootAmount > 0)
                {
                    Faction killerFaction = lastDamager.ValueRO.Value;
                    FactionEconomy.Add(em, killerFaction, Cost.Of(supplies: lootAmount));
                }

                // --- Decrement active trader count on lane ---
                Entity lanePost = trader.ValueRO.OwnerLanePost;
                if (em.Exists(lanePost) && em.HasComponent<TradeLane>(lanePost))
                {
                    var lane = em.GetComponentData<TradeLane>(lanePost);
                    lane.ActiveTraders = math.max(0, lane.ActiveTraders - 1);
                    // Reset second trader timer if we lost a trader so a replacement can spawn
                    if (lane.ActiveTraders < 2)
                        lane.SecondTraderTimer = 60f; // Respawn replacement in 1 minute
                    em.SetComponentData(lanePost, lane);
                }

                // DeathSystem will handle actual entity destruction
            }

            // Old CaravanState system removed — all traders now use TraderState
        }
    }
}
