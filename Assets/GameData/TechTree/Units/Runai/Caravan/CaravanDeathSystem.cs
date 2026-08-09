// File: Assets/GameData/TechTree/Units/Runai/Caravan/CaravanDeathSystem.cs
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

            // Process dead traders (RunaiTraderState system).
            //
            // Earlier this query had no death-marker exclusion. DeathSystem
            // keeps the trader entity alive ~2s for animation, so the loop
            // hit the same dead trader every frame and called FactionEconomy.Add
            // on EACH frame: a trader carrying 20 supplies + 5 veilstone paid
            // ~1200 supplies + ~300 veilstone over 2s instead of the intended
            // 10 + 2. The `[UpdateBefore(DeathSystem)]` on this system only
            // gates the FIRST frame of death — subsequent frames still see
            // the entity. WithNone on the death markers caps the payout to
            // exactly one frame. (task-056 F-2)
            foreach (var (health, trader, lastDamager, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<RunaiTraderState>, RefRO<LastDamagedByFaction>>()
                .WithAll<CaravanTag>()
                .WithNone<DeathAnimationState, BuildingCollapseState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // Loot: credit killer's faction with 50% of accumulated resources
                int lootSupplies = (int)(trader.ValueRO.AccumulatedSupplies * DeathLootFraction);
                int lootVeilstone = (int)(trader.ValueRO.AccumulatedVeilstone * DeathLootFraction);

                if (lootSupplies > 0 || lootVeilstone > 0)
                {
                    Faction killerFaction = lastDamager.ValueRO.Value;
                    // Design §1.9: cargo only drops for Feraldis killers; Alanthor,
                    // Runai friendly-fire, and Border PvE destroy it.
                    if (FactionColors.GetFactionCulture(killerFaction) == Cultures.Feraldis)
                    {
                        FactionEconomy.Add(em, killerFaction, Cost.Of(supplies: lootSupplies, veilstone: lootVeilstone));
                    }
                }

                // DeathSystem will handle actual entity destruction
            }
        }
    }
}
