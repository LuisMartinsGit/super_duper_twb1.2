// Applies compound interest to resources stored in the Vault of Almiérra.
// Rate: the SO's interestPerMinute (VaultOfAlmierra.asset), scaled by the
// level SO's interestMultiplier. Vault locks for 3 minutes after each
// deposit/withdraw.

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
                    // 100% per minute (base: the Vault SO's interestPerMinute).
                    if (research != null)
                    {
                        var f = faction.ValueRO.Value;
                        if (research.HasResearched(f, "SovereignBonds")) rate = 1.00f;
                        else if (research.HasResearched(f, "MerchantCharters")) rate = 0.75f;
                        else if (research.HasResearched(f, "Coffers")) rate = 0.50f;
                    }

                    // The Vault's level scales its yield by the level SO's
                    // interestMultiplier (VaultOfAlmierra_Lvl1..3: x1 / x1.5 /
                    // x2). The base rate applies from L1 — the code ladder
                    // this replaced paid x1.5 at L1 and x2 at both L2 and L3.
                    // A culture with no authored Vault levels (Runai,
                    // Feraldis, or none yet) earns the base rate.
                    if (state.EntityManager.HasComponent<BuildingUpgradeState>(entity))
                    {
                        byte lv = state.EntityManager.GetComponentData<BuildingUpgradeState>(entity).Level;
                        var levelDef = TheWaningBorder.Core.Settings.BuildingUpgradeConfig.LevelDef(
                            state.EntityManager, faction.ValueRO.Value, "VaultOfAlmierra", lv);
                        if (levelDef != null) rate *= levelDef.interestMultiplier;
                    }

                    // task-063 phase 1: sect VaultInterest multiplier removed with the
                    // FactionSectState bridge. Phase 2 reintroduces sect levers.

                    vault.ValueRW.StoredAmount += vault.ValueRO.StoredAmount * rate * dt / 60f;
                }
            }
        }
    }
}
