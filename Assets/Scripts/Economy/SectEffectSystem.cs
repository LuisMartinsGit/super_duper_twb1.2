// SectEffectSystem.cs
// Applies sect passive effects to faction entities on adoption and temple level-up
// Part of: Economy/

using UnityEngine;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Listens for sect adoption events and applies entity-level effects
    /// (melee damage, line of sight, vault interest, etc.) to existing entities.
    ///
    /// MonoBehaviour singleton (pattern: TechEffectSystem).
    /// Multiplier-based effects are stored on FactionSectState and queried by systems.
    /// Entity-level effects (Damage, LineOfSight, VaultStorage) are applied directly here.
    ///
    /// Newly spawned units receive sect effects via ApplySectEffectsToUnit() at spawn time.
    /// </summary>
    public class SectEffectSystem : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════

        public static SectEffectSystem Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        private bool _subscribed;

        void OnEnable()
        {
            var sectState = FactionSectState.Instance;
            if (sectState != null)
            {
                sectState.OnSectAdopted += OnSectAdopted;
                _subscribed = true;
                Debug.Log("[SectEffectSystem] Subscribed to OnSectAdopted");
            }
        }

        void OnDisable()
        {
            var sectState = FactionSectState.Instance;
            if (sectState != null)
            {
                sectState.OnSectAdopted -= OnSectAdopted;
            }
        }

        void Update()
        {
            if (_subscribed) return;

            var sectState = FactionSectState.Instance;
            if (sectState != null)
            {
                sectState.OnSectAdopted += OnSectAdopted;
                _subscribed = true;
                Debug.Log("[SectEffectSystem] Late-subscribed to OnSectAdopted");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENT HANDLER
        // ═══════════════════════════════════════════════════════════════════

        private void OnSectAdopted(Faction faction, string sectId)
        {
            Debug.Log($"[SectEffectSystem] Applying effects for {SectConfig.GetDisplayName(sectId)} to {faction}...");

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            // Apply entity-level effects based on the adopted sect
            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            var mults = sectState.GetMultipliers(faction);

            // Apply effects that modify entity components directly
            ApplyEntityEffects(em, faction, sectId, mults);

            // Check for new synergy activations
            foreach (var pair in SectConfig.SynergyPairs)
            {
                // Only apply synergy if both sects are adopted and the newly adopted sect is one of them
                if ((sectId == pair.SectA || sectId == pair.SectB) &&
                    sectState.HasAdopted(faction, pair.SectA) &&
                    sectState.HasAdopted(faction, pair.SectB))
                {
                    ApplySynergyEntityEffects(em, faction, pair, mults);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // ENTITY-LEVEL EFFECT APPLICATION
        // ═══════════════════════════════════════════════════════════════════

        private void ApplyEntityEffects(EntityManager em, Faction faction, string sectId,
            FactionSectState.SectMultipliers mults)
        {
            switch (sectId)
            {
                case SectConfig.EmberAsh:
                    // Apply melee damage bonus to all faction melee units
                    ApplyMeleeDamageToExisting(em, faction, mults.MeleeDamage);
                    break;

                case SectConfig.VeiledMemory:
                    // Apply fog vision bonus to all faction units
                    ApplyLineOfSightToExisting(em, faction, mults.FogVisionBonus);
                    break;

                case SectConfig.QuietVault:
                    // Apply vault interest bonus to faction vaults
                    ApplyVaultInterestToExisting(em, faction, mults.VaultInterest);
                    break;

                case SectConfig.MirrorRite:
                    // Apply ranged accuracy bonus to faction ranged units
                    ApplyRangedAccuracyToExisting(em, faction, mults.RangedAccuracy);
                    break;
            }
        }

        private void ApplySynergyEntityEffects(EntityManager em, Faction faction,
            SectConfig.SynergyPair pair, FactionSectState.SectMultipliers mults)
        {
            switch (pair.BonusType)
            {
                case "BuildingHP":
                    ApplyBuildingHPToExisting(em, faction, mults.BuildingHP);
                    break;
                case "RangedDamage":
                    ApplyRangedDamageToExisting(em, faction, mults.RangedDamage);
                    break;
                case "AttackSpeed":
                    ApplyAttackSpeedToExisting(em, faction, mults.AttackSpeed);
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPECIFIC EFFECT METHODS
        // ═══════════════════════════════════════════════════════════════════

        private static void ApplyMeleeDamageToExisting(EntityManager em, Faction faction, float multiplier)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<DamageTypeData>(),
                ComponentType.ReadWrite<Damage>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var damageTypes = query.ToComponentDataArray<DamageTypeData>(Allocator.Temp);
            using var damages = query.ToComponentDataArray<Damage>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (damageTypes[i].Value != DamageType.Melee) continue;

                var dmg = damages[i];
                dmg.Value = (int)(dmg.Value * multiplier);
                em.SetComponentData(entities[i], dmg);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied melee damage x{multiplier:F2} to {count} units");
        }

        private static void ApplyLineOfSightToExisting(EntityManager em, Faction faction, float bonusPercent)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadWrite<LineOfSight>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var losData = query.ToComponentDataArray<LineOfSight>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var los = losData[i];
                los.Radius *= (1.0f + bonusPercent);
                em.SetComponentData(entities[i], los);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied LOS bonus +{bonusPercent * 100:F0}% to {count} units");
        }

        private static void ApplyVaultInterestToExisting(EntityManager em, Faction faction, float multiplier)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<VaultTag>(),
                ComponentType.ReadWrite<VaultStorage>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var vaults = query.ToComponentDataArray<VaultStorage>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var vault = vaults[i];
                vault.InterestRate *= multiplier;
                em.SetComponentData(entities[i], vault);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied vault interest x{multiplier:F2} to {count} vaults");
        }

        private static void ApplyRangedAccuracyToExisting(EntityManager em, Faction faction, float bonusPercent)
        {
            // Ranged accuracy is implemented as reduced attack cooldown for ranged units
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<DamageTypeData>(),
                ComponentType.ReadWrite<AttackCooldown>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var damageTypes = query.ToComponentDataArray<DamageTypeData>(Allocator.Temp);
            using var cooldowns = query.ToComponentDataArray<AttackCooldown>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (damageTypes[i].Value != DamageType.Ranged) continue;

                var cd = cooldowns[i];
                cd.Cooldown *= (1.0f - bonusPercent); // Reduce cooldown = faster attacks
                em.SetComponentData(entities[i], cd);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied ranged accuracy -{bonusPercent * 100:F0}% cooldown to {count} ranged units");
        }

        private static void ApplyBuildingHPToExisting(EntityManager em, Faction faction, float multiplier)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadWrite<Health>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var hp = healths[i];
                int newMax = (int)(hp.Max * multiplier);
                int hpDiff = newMax - hp.Max;
                hp.Max = newMax;
                hp.Value += hpDiff; // Also increase current HP proportionally
                em.SetComponentData(entities[i], hp);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied building HP x{multiplier:F2} to {count} buildings");
        }

        private static void ApplyRangedDamageToExisting(EntityManager em, Faction faction, float multiplier)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<DamageTypeData>(),
                ComponentType.ReadWrite<Damage>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var damageTypes = query.ToComponentDataArray<DamageTypeData>(Allocator.Temp);
            using var damages = query.ToComponentDataArray<Damage>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (damageTypes[i].Value != DamageType.Ranged) continue;

                var dmg = damages[i];
                dmg.Value = (int)(dmg.Value * multiplier);
                em.SetComponentData(entities[i], dmg);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied ranged damage x{multiplier:F2} to {count} ranged units");
        }

        private static void ApplyAttackSpeedToExisting(EntityManager em, Faction faction, float multiplier)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadWrite<AttackCooldown>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var cooldowns = query.ToComponentDataArray<AttackCooldown>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var cd = cooldowns[i];
                cd.Cooldown /= multiplier; // Higher multiplier = shorter cooldown = faster
                em.SetComponentData(entities[i], cd);
                count++;
            }

            Debug.Log($"[SectEffectSystem] Applied attack speed x{multiplier:F2} to {count} units");
        }

        // ═══════════════════════════════════════════════════════════════════
        // RECALCULATION ON TEMPLE LEVEL-UP
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recalculate and re-apply all sect effects for a faction.
        /// Called when temple level changes (scaling multiplier updates).
        /// </summary>
        public void RecalculateAllPassives(Faction faction)
        {
            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            // Recompute multipliers with new temple scaling
            sectState.RecomputeMultipliers(faction);
            var mults = sectState.GetMultipliers(faction);

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Re-apply entity-level effects with updated scaling
            // Note: We apply the full multiplier (not incremental) because
            // entity values may have changed since original adoption
            foreach (var sectId in sectState.GetAdoptedSects(faction))
            {
                ApplyEntityEffects(em, faction, sectId, mults);
            }

            Debug.Log($"[SectEffectSystem] Recalculated all passives for {faction} " +
                      $"(adopted: {sectState.GetAdoptedCount(faction)} sects)");
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPAWN-TIME APPLICATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply all active sect effects to a newly spawned unit.
        /// Called by TrainingSystem or TechEffectSystem at spawn time.
        /// </summary>
        public static void ApplySectEffectsToUnit(EntityManager em, Entity unit, Faction faction)
        {
            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            var adopted = sectState.GetAdoptedSects(faction);
            if (adopted.Count == 0) return;

            var mults = sectState.GetMultipliers(faction);

            // Apply melee damage bonus
            if (mults.MeleeDamage > 1.0f && em.HasComponent<DamageTypeData>(unit) && em.HasComponent<Damage>(unit))
            {
                var dmgType = em.GetComponentData<DamageTypeData>(unit);
                if (dmgType.Value == DamageType.Melee)
                {
                    var dmg = em.GetComponentData<Damage>(unit);
                    dmg.Value = (int)(dmg.Value * mults.MeleeDamage);
                    em.SetComponentData(unit, dmg);
                }
            }

            // Apply ranged damage bonus
            if (mults.RangedDamage > 1.0f && em.HasComponent<DamageTypeData>(unit) && em.HasComponent<Damage>(unit))
            {
                var dmgType = em.GetComponentData<DamageTypeData>(unit);
                if (dmgType.Value == DamageType.Ranged)
                {
                    var dmg = em.GetComponentData<Damage>(unit);
                    dmg.Value = (int)(dmg.Value * mults.RangedDamage);
                    em.SetComponentData(unit, dmg);
                }
            }

            // Apply LOS bonus
            if (mults.FogVisionBonus > 0f && em.HasComponent<LineOfSight>(unit))
            {
                var los = em.GetComponentData<LineOfSight>(unit);
                los.Radius *= (1.0f + mults.FogVisionBonus);
                em.SetComponentData(unit, los);
            }

            // Apply attack speed bonus
            if (mults.AttackSpeed > 1.0f && em.HasComponent<AttackCooldown>(unit))
            {
                var cd = em.GetComponentData<AttackCooldown>(unit);
                cd.Cooldown /= mults.AttackSpeed;
                em.SetComponentData(unit, cd);
            }

            // Apply ranged accuracy (reduced cooldown for ranged)
            if (mults.RangedAccuracy > 0f && em.HasComponent<DamageTypeData>(unit) &&
                em.HasComponent<AttackCooldown>(unit))
            {
                var dmgType = em.GetComponentData<DamageTypeData>(unit);
                if (dmgType.Value == DamageType.Ranged)
                {
                    var cd = em.GetComponentData<AttackCooldown>(unit);
                    cd.Cooldown *= (1.0f - mults.RangedAccuracy);
                    em.SetComponentData(unit, cd);
                }
            }
        }
    }
}
