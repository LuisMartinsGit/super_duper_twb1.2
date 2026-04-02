// File: Assets/Scripts/Systems/Economy/VaultInterestSystem.cs
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
            var em = state.EntityManager;

            foreach (var (vault, entity) in SystemAPI
                .Query<RefRW<VaultStorage>>()
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

                    // Apply sect vault interest multiplier
                    if (FactionSectState.Instance != null && em.HasComponent<FactionTag>(entity))
                    {
                        var vFaction = em.GetComponentData<FactionTag>(entity).Value;
                        rate *= FactionSectState.Instance.GetMultipliers(vFaction).VaultInterest;
                    }

                    vault.ValueRW.StoredAmount += vault.ValueRO.StoredAmount * rate * dt / 60f;
                }
            }
        }
    }
}
