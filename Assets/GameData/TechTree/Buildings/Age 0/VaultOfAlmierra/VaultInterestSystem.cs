// Applies compound interest to resources stored in the Vault of Almiérra.
// Rate: 3% per minute. Vault locks for 3 minutes after each deposit/withdraw.

using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VaultInterestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VaultStorage>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;

            foreach (var (vault, faction, entity) in SystemAPI
                .Query<RefRW<VaultStorage>, RefRO<FactionTag>>()
                .WithAll<VaultTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                // Tick lock timer
                if (vault.ValueRO.LockTimer > 0f)
                    vault.ValueRW.LockTimer -= dt;

                // Apply compound interest if vault has resources
                if (vault.ValueRO.ResourceType > 0 && vault.ValueRO.StoredAmount > 0f)
                {
                    // Continuous compounding: amount *= e^(rate * dt / 60)
                    // Simplified: amount += amount * rate * dt / 60
                    float rate = vault.ValueRO.InterestRate;

                    // Banking-grade tech ladder (Age 0 design): the highest
                    // researched grade REPLACES the active interest rate —
                    // Coffers 50%, Merchant Charters 75%, Sovereign Bonds
                    // 100% per minute (base 25% from the factory).
                    if (research != null)
                    {
                        var f = faction.ValueRO.Value;
                        if (research.HasResearched(f, "SovereignBonds")) rate = 1.00f;
                        else if (research.HasResearched(f, "MerchantCharters")) rate = 0.75f;
                        else if (research.HasResearched(f, "Coffers")) rate = 0.50f;
                    }

                    // Vault simple upgrade (design 2026-07-04): dramatically
                    // increases interest yields — x1.5 at L2, x2 at L3.
                    if (state.EntityManager.HasComponent<BuildingUpgradeState>(entity))
                    {
                        int lv = state.EntityManager.GetComponentData<BuildingUpgradeState>(entity).Level;
                        if (lv >= 2) rate *= 2f;
                        else if (lv == 1) rate *= 1.5f;
                    }

                    // task-063 phase 1: sect VaultInterest multiplier removed with the
                    // FactionSectState bridge. Phase 2 reintroduces sect levers.

                    vault.ValueRW.StoredAmount += vault.ValueRO.StoredAmount * rate * dt / 60f;
                }
            }
        }
    }
}
