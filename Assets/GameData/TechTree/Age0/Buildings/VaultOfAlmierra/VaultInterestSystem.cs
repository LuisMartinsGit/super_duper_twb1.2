// Pays SIMPLE interest on the resources stored in the Vault of Almiérra
// (docs/Design/Age_0.md § Vault, Unification decision 41, 2026-10-05).
//
// The yield is computed on the PRINCIPAL (VaultStorage.Principal: the stored
// amount as of the last deposit), capped at the Vault SO's
// interestPrincipalCap, and never on the interest already earned — so the
// payout grows linearly, not exponentially. The compounding this replaced
// turned 2,645 iron into 7.5 million in eleven minutes.
//
// Rate: the SO's interestPerMinute (VaultOfAlmierra.asset), replaced by the
// SO's coffersRate / merchantChartersRate / sovereignBondsRate once that
// banking grade is researched (highest wins), then scaled by the level SO's
// interestMultiplier. Vault locks for 3 minutes after each deposit/withdraw.

using Unity.Entities;
using Unity.Mathematics;
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

            // The cap and the banking-grade rates live on the Vault SO; the
            // three banking techs all research at the Vault of Almierra, so
            // its def is the one place to read them (never null).
            var vaultDef = TechCatalog.Building("VaultOfAlmierra");

            foreach (var (vault, faction, entity) in SystemAPI
                .Query<RefRW<VaultStorage>, RefRO<FactionTag>>()
                .WithAll<VaultTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                // Tick lock timer
                if (vault.ValueRO.LockTimer > 0f)
                    vault.ValueRW.LockTimer -= dt;

                if (vault.ValueRO.ResourceType <= 0 || vault.ValueRO.StoredAmount <= 0f)
                    continue;

                // Only the principal earns, and only up to the cap. Stored
                // amount above the cap, and every unit of interest already
                // paid, earns nothing.
                float earning = math.min(vault.ValueRO.Principal, vaultDef.interestPrincipalCap);
                if (earning <= 0f) continue;

                float rate = vault.ValueRO.InterestRate;

                // Banking-grade tech ladder (Age 0 design): the highest
                // researched grade REPLACES the active interest rate with the
                // Vault SO's rate for that grade.
                if (research != null)
                {
                    var f = faction.ValueRO.Value;
                    if (research.HasResearched(f, "SovereignBonds")) rate = vaultDef.sovereignBondsRate;
                    else if (research.HasResearched(f, "MerchantCharters")) rate = vaultDef.merchantChartersRate;
                    else if (research.HasResearched(f, "Coffers")) rate = vaultDef.coffersRate;
                }

                // The Vault's level scales its yield by the level SO's
                // interestMultiplier (VaultOfAlmierra_Lvl1..3). The base rate
                // applies from L1. A culture with no authored Vault levels
                // (Runai, Feraldis, or none yet) earns the base rate.
                if (state.EntityManager.HasComponent<BuildingUpgradeState>(entity))
                {
                    byte lv = state.EntityManager.GetComponentData<BuildingUpgradeState>(entity).Level;
                    var levelDef = TheWaningBorder.Core.Settings.BuildingUpgradeConfig.LevelDef(
                        state.EntityManager, faction.ValueRO.Value, "VaultOfAlmierra", lv);
                    if (levelDef != null) rate *= levelDef.interestMultiplier;
                }

                // task-063 phase 1: sect VaultInterest multiplier removed with the
                // FactionSectState bridge. Phase 2 reintroduces sect levers.

                // Simple interest: the earning principal times the rate, per
                // minute, added to the payout and never to the principal.
                vault.ValueRW.StoredAmount += earning * rate * dt / 60f;
            }
        }
    }
}
