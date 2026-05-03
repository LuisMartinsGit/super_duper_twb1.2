// TechEffectSystem.cs
// Applies stat modifiers from researched technologies to faction entities.
// MonoBehaviour singleton - subscribes to FactionResearchState.OnTechCompleted.
// Location: Assets/Scripts/Systems/Research/TechEffectSystem.cs

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Research
{
    /// <summary>
    /// Listens for research completion events and applies the technology's stat
    /// modifiers to all existing entities of the researching faction.
    ///
    /// Uses MonoBehaviour (not ECS ISystem) because it needs managed singleton
    /// access (FactionResearchState, TechTreeDB) and event subscription.
    ///
    /// Newly spawned units receive tech effects at spawn time via TrainingSystem.
    /// </summary>
    public class TechEffectSystem : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        void OnEnable()
        {
            var researchState = FactionResearchState.Instance;
            if (researchState != null)
            {
                researchState.OnTechCompleted += OnTechCompleted;
                _subscribed = true;
            }
        }

        void OnDisable()
        {
            var researchState = FactionResearchState.Instance;
            if (researchState != null)
            {
                researchState.OnTechCompleted -= OnTechCompleted;
            }
        }

        /// <summary>
        /// Late-subscribe: FactionResearchState may initialize after this component.
        /// Check each frame until subscribed, then stop checking.
        /// </summary>
        private bool _subscribed;

        void Update()
        {
            if (_subscribed) return;

            var researchState = FactionResearchState.Instance;
            if (researchState != null)
            {
                researchState.OnTechCompleted += OnTechCompleted;
                _subscribed = true;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // EVENT HANDLER
        // ═══════════════════════════════════════════════════════════════

        private void OnTechCompleted(Faction faction, string techId)
        {
            if (TechTreeDB.Instance == null) return;

            // Sect-tech bridge: previously SetTechFlag had zero callers in the
            // codebase, so all 12 sect technologies were silently inert —
            // research completed and fired this event, but the sect-flag
            // multipliers (RegenPerSecond, SpellCooldownReduction, MagicDamage,
            // WallIncomeFromTech) never applied. Tech*Effects.HasAnyEffect on
            // sect techs is false because sect bonuses live in FactionSectState
            // multipliers, not the tech's effects block — so this gate has to
            // run BEFORE the HasAnyEffect early-return below. (task-057 F-1)
            if (SectConfig.IsSectTech(techId))
            {
                var sectId = SectConfig.GetSectIdForTechId(techId);
                FactionSectState.Instance?.SetTechFlag(faction, sectId);
                SectEffectSystem.Instance?.RecalculateAllPassives(faction);
            }

            var tech = TechTreeDB.Instance.GetTechnology(techId);
            if (tech == null || tech.effects == null || !tech.effects.HasAnyEffect)
            {
                return;
            }


            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var effects = tech.effects;

            // Apply gather speed and carry capacity to miners
            if (effects.gatherSpeedMult != 0f || effects.carryCapacityBonus != 0)
            {
                ApplyMinerEffects(em, faction, effects);
            }

            // Apply melee attack speed to melee combat units
            if (effects.meleeAttackSpeedMult != 0f)
            {
                ApplyMeleeAttackSpeedEffect(em, faction, effects.meleeAttackSpeedMult);
            }

            // Apply melee defense bonus
            if (effects.meleeDefenseAdd != 0)
            {
                ApplyMeleeDefenseEffect(em, faction, effects.meleeDefenseAdd);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // EFFECT APPLICATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply gather speed multiplier and carry capacity bonus to all faction miners.
        /// </summary>
        private static void ApplyMinerEffects(EntityManager em, Faction faction, TechEffects effects)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<MinerState>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var minerStates = query.ToComponentDataArray<MinerState>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var miner = minerStates[i];

                if (effects.gatherSpeedMult != 0f)
                    miner.GatherSpeedMultiplier *= effects.gatherSpeedMult;

                if (effects.carryCapacityBonus != 0)
                    miner.CarryCapacityBonus += effects.carryCapacityBonus;

                em.SetComponentData(entities[i], miner);
                count++;
            }

        }

        /// <summary>
        /// Apply melee attack speed multiplier to all faction melee units.
        /// Divides AttackCooldown.Cooldown by the multiplier (shorter cooldown = faster attacks).
        /// </summary>
        private static void ApplyMeleeAttackSpeedEffect(EntityManager em, Faction faction, float multiplier)
        {
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
                if (damageTypes[i].Value != DamageType.Melee) continue;

                var cd = cooldowns[i];
                cd.Cooldown /= multiplier;
                em.SetComponentData(entities[i], cd);
                count++;
            }

        }

        /// <summary>
        /// Apply melee defense bonus to all faction units with a Defense component.
        /// </summary>
        private static void ApplyMeleeDefenseEffect(EntityManager em, Faction faction, int bonus)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Defense>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var defenses = query.ToComponentDataArray<Defense>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                // Only apply to units (not buildings) - units have UnitTag
                if (!em.HasComponent<UnitTag>(entities[i])) continue;

                var def = defenses[i];
                def.Melee += bonus;
                em.SetComponentData(entities[i], def);
                count++;
            }

        }

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API (for TrainingSystem to apply effects at spawn)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply all completed tech effects to a newly spawned unit.
        /// Called by TrainingSystem after spawning a unit.
        /// </summary>
        public static void ApplyCompletedTechEffects(EntityManager em, Entity unit, Faction faction)
        {
            var researchState = FactionResearchState.Instance;
            if (researchState == null || TechTreeDB.Instance == null) return;

            var completedTechs = researchState.GetCompletedTechs(faction);
            if (completedTechs.Count == 0) return;

            bool hasMinerState = em.HasComponent<MinerState>(unit);
            bool hasAttackCooldown = em.HasComponent<AttackCooldown>(unit);
            bool hasDefense = em.HasComponent<Defense>(unit);
            bool hasDamageType = em.HasComponent<DamageTypeData>(unit);
            bool isMelee = hasDamageType && em.GetComponentData<DamageTypeData>(unit).Value == DamageType.Melee;

            foreach (var techId in completedTechs)
            {
                var tech = TechTreeDB.Instance.GetTechnology(techId);
                if (tech?.effects == null || !tech.effects.HasAnyEffect) continue;

                var effects = tech.effects;

                // Miner effects
                if (hasMinerState)
                {
                    if (effects.gatherSpeedMult != 0f || effects.carryCapacityBonus != 0)
                    {
                        var miner = em.GetComponentData<MinerState>(unit);
                        if (effects.gatherSpeedMult != 0f)
                            miner.GatherSpeedMultiplier *= effects.gatherSpeedMult;
                        if (effects.carryCapacityBonus != 0)
                            miner.CarryCapacityBonus += effects.carryCapacityBonus;
                        em.SetComponentData(unit, miner);
                    }
                }

                // Melee attack speed
                if (hasAttackCooldown && isMelee && effects.meleeAttackSpeedMult != 0f)
                {
                    var cd = em.GetComponentData<AttackCooldown>(unit);
                    cd.Cooldown /= effects.meleeAttackSpeedMult;
                    em.SetComponentData(unit, cd);
                }

                // Melee defense
                if (hasDefense && em.HasComponent<UnitTag>(unit) && effects.meleeDefenseAdd != 0)
                {
                    var def = em.GetComponentData<Defense>(unit);
                    def.Melee += effects.meleeDefenseAdd;
                    em.SetComponentData(unit, def);
                }
            }
        }
    }
}
