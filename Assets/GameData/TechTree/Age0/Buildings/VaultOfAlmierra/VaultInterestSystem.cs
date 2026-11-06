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
            // The cap lives on the Vault SO (never null).
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

                // THE HARD CAP (2026-10-07, developer: "a 10 minute hard cap
                // on the vault"): a deposit earns for at most the SO's
                // interestMaxSeconds, then nothing until it is withdrawn and
                // deposited again.
                if (vaultDef.interestMaxSeconds > 0f)
                {
                    if (vault.ValueRO.EarningSeconds >= vaultDef.interestMaxSeconds) continue;
                    vault.ValueRW.EarningSeconds += dt;
                }

                float rate = EffectiveRate(state.EntityManager, entity,
                    faction.ValueRO.Value, vault.ValueRO.InterestRate);

                // task-063 phase 1: sect VaultInterest multiplier removed with the
                // FactionSectState bridge. Phase 2 reintroduces sect levers.

                // Simple interest: the earning principal times the rate, per
                // minute, added to the payout and never to the principal.
                vault.ValueRW.StoredAmount += earning * rate * dt / 60f;
            }
        }

        /// <summary>
        /// The rate a Vault really pays per minute. The highest researched
        /// banking grade REPLACES the base with the Vault SO's rate for that
        /// grade, then the Vault's level scales it by the level SO's
        /// interestMultiplier (a culture with no authored levels earns it
        /// unscaled). The panel reads this too, so what it says is what is paid.
        /// </summary>
        public static float EffectiveRate(EntityManager em, Entity vault, Faction faction, float baseRate)
        {
            float rate = baseRate;
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            var def = TechCatalog.Building("VaultOfAlmierra");
            if (research != null)
            {
                if (research.HasResearched(faction, "SovereignBonds")) rate = def.sovereignBondsRate;
                else if (research.HasResearched(faction, "MerchantCharters")) rate = def.merchantChartersRate;
                else if (research.HasResearched(faction, "Coffers")) rate = def.coffersRate;
            }
            if (em.HasComponent<BuildingUpgradeState>(vault))
            {
                byte lv = em.GetComponentData<BuildingUpgradeState>(vault).Level;
                var levelDef = TheWaningBorder.Core.Settings.BuildingUpgradeConfig.LevelDef(
                    em, faction, "VaultOfAlmierra", lv);
                if (levelDef != null) rate *= levelDef.interestMultiplier;
            }
            return rate;
        }
    }
}
