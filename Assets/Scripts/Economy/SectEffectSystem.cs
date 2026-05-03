// SectEffectSystem.cs
// Applies sect passive effects to faction entities on adoption and temple level-up
// Part of: Economy/

using System.Collections.Generic;
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
        // APPLIED MULTIPLIER TRACKING (Fix #198)
        // ═══════════════════════════════════════════════════════════════════
        //
        // Tracks the multipliers last applied to each faction's population, so
        // that subsequent adoptions / temple level-ups can apply only the DELTA
        // between the previously-applied state and the new target state.
        //
        // Before this fix, OnSectAdopted and RecalculateAllPassives multiplied
        // current component values by the full new multiplier, causing
        // exponential stat compounding on every temple level-up.
        //
        // With delta logic: delta = new / old; apply that once. If old=1.0
        // (first application) the delta equals the full new mult, matching the
        // original behaviour. On subsequent calls (second sect adopted, temple
        // level-up, etc.) only the incremental change is applied.
        //
        // Per-faction state lives only in this singleton — it is recomputed
        // from scratch if the world is recreated, because the entity-side
        // values are also reset at that point.
        private readonly Dictionary<Faction, FactionSectState.SectMultipliers> _appliedMults
            = new Dictionary<Faction, FactionSectState.SectMultipliers>();

        private FactionSectState.SectMultipliers GetApplied(Faction f)
            => _appliedMults.TryGetValue(f, out var m)
                ? m
                : FactionSectState.SectMultipliers.Default;

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
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENT HANDLER
        // ═══════════════════════════════════════════════════════════════════

        private void OnSectAdopted(Faction faction, string sectId)
        {

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            // Delta application: compute the full new target multipliers (including
            // any synergy bonuses activated by this adoption — FactionSectState.
            // RecomputeMultipliers already folds synergy into the returned struct),
            // then apply only the delta from what is currently on the population.
            var oldMults = GetApplied(faction);
            var newMults = sectState.GetMultipliers(faction);

            ApplyMultiplierDelta(em, faction, oldMults, newMults);
            _appliedMults[faction] = newMults;
        }

        // ═══════════════════════════════════════════════════════════════════
        // ENTITY-LEVEL EFFECT APPLICATION (delta-based)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply the field-level delta between two multiplier sets to all affected
        /// faction entities. Only fields that actually changed are visited. This
        /// replaces the previous "multiply by full current mult" approach, which
        /// compounded stats on every temple level-up.
        /// </summary>
        private static void ApplyMultiplierDelta(EntityManager em, Faction faction,
            FactionSectState.SectMultipliers oldM, FactionSectState.SectMultipliers newM)
        {
            // Multiplicative fields (default 1.0) — delta = new / old
            if (!Mathf.Approximately(oldM.MeleeDamage, newM.MeleeDamage))
                ApplyMeleeDamageDelta(em, faction, oldM.MeleeDamage, newM.MeleeDamage);

            if (!Mathf.Approximately(oldM.RangedDamage, newM.RangedDamage))
                ApplyRangedDamageDelta(em, faction, oldM.RangedDamage, newM.RangedDamage);

            if (!Mathf.Approximately(oldM.VaultInterest, newM.VaultInterest))
                ApplyVaultInterestDelta(em, faction, oldM.VaultInterest, newM.VaultInterest);

            if (!Mathf.Approximately(oldM.BuildingHP, newM.BuildingHP))
                ApplyBuildingHpDelta(em, faction, oldM.BuildingHP, newM.BuildingHP);

            if (!Mathf.Approximately(oldM.AttackSpeed, newM.AttackSpeed))
                ApplyAttackSpeedDelta(em, faction, oldM.AttackSpeed, newM.AttackSpeed);

            // Additive fields (default 0.0) — delta factor computed inside helper
            if (!Mathf.Approximately(oldM.FogVisionBonus, newM.FogVisionBonus))
                ApplyLosBonusDelta(em, faction, oldM.FogVisionBonus, newM.FogVisionBonus);

            if (!Mathf.Approximately(oldM.RangedAccuracy, newM.RangedAccuracy))
                ApplyRangedAccuracyDelta(em, faction, oldM.RangedAccuracy, newM.RangedAccuracy);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPECIFIC EFFECT METHODS
        // ═══════════════════════════════════════════════════════════════════

        // Melee damage: delta factor = new / old
        private static void ApplyMeleeDamageDelta(EntityManager em, Faction faction, float oldMult, float newMult)
        {
            float delta = newMult / oldMult;
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
                dmg.Value = (int)(dmg.Value * delta);
                em.SetComponentData(entities[i], dmg);
                count++;
            }

        }

        // LOS bonus is additive on top of base (stored value = base * (1 + bonus))
        // delta factor = (1 + new) / (1 + old)
        private static void ApplyLosBonusDelta(EntityManager em, Faction faction, float oldBonus, float newBonus)
        {
            float delta = (1.0f + newBonus) / (1.0f + oldBonus);
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
                los.Radius *= delta;
                em.SetComponentData(entities[i], los);
                count++;
            }

        }

        // Vault interest: delta factor = new / old
        private static void ApplyVaultInterestDelta(EntityManager em, Faction faction, float oldMult, float newMult)
        {
            float delta = newMult / oldMult;
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
                vault.InterestRate *= delta;
                em.SetComponentData(entities[i], vault);
                count++;
            }

        }

        // Ranged accuracy is additive bonus that multiplies cooldown by (1 - bonus).
        // Stored cooldown = base * (1 - bonus). Delta factor = (1 - new) / (1 - old).
        private static void ApplyRangedAccuracyDelta(EntityManager em, Faction faction, float oldBonus, float newBonus)
        {
            float delta = (1.0f - newBonus) / (1.0f - oldBonus);
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
                cd.Cooldown *= delta;
                em.SetComponentData(entities[i], cd);
                count++;
            }

        }

        // Building HP: delta factor = new / old. Adjusts Max and Value proportionally.
        private static void ApplyBuildingHpDelta(EntityManager em, Faction faction, float oldMult, float newMult)
        {
            float delta = newMult / oldMult;
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
                int newMax = (int)(hp.Max * delta);
                int hpDiff = newMax - hp.Max;
                hp.Max = newMax;
                hp.Value += hpDiff; // Also increase current HP proportionally
                em.SetComponentData(entities[i], hp);
                count++;
            }

        }

        // Ranged damage: delta factor = new / old
        private static void ApplyRangedDamageDelta(EntityManager em, Faction faction, float oldMult, float newMult)
        {
            float delta = newMult / oldMult;
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
                dmg.Value = (int)(dmg.Value * delta);
                em.SetComponentData(entities[i], dmg);
                count++;
            }

        }

        // Attack speed: cooldown = base / speed. Delta cooldown factor = old / new.
        private static void ApplyAttackSpeedDelta(EntityManager em, Faction faction, float oldMult, float newMult)
        {
            float delta = oldMult / newMult; // inverse — higher speed = shorter cooldown
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
                cd.Cooldown *= delta;
                em.SetComponentData(entities[i], cd);
                count++;
            }

        }

        // ═══════════════════════════════════════════════════════════════════
        // RECALCULATION ON TEMPLE LEVEL-UP
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recalculate and re-apply all sect effects for a faction.
        /// Called when temple level changes (scaling multiplier updates).
        ///
        /// Uses the same delta-based application as OnSectAdopted: computes the
        /// difference between the currently-applied multipliers and the new
        /// target, then applies only that delta. This prevents the exponential
        /// compounding that used to happen when the previous implementation
        /// re-multiplied already-boosted stats by the full new multiplier on
        /// every temple level-up (issue #198).
        /// </summary>
        public void RecalculateAllPassives(Faction faction)
        {
            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            // Recompute multipliers with new temple scaling
            sectState.RecomputeMultipliers(faction);

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var oldMults = GetApplied(faction);
            var newMults = sectState.GetMultipliers(faction);

            ApplyMultiplierDelta(em, faction, oldMults, newMults);
            _appliedMults[faction] = newMults;

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
