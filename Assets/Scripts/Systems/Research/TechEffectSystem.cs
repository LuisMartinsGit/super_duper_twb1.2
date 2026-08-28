// TechEffectSystem.cs
// Applies stat modifiers from researched technologies to faction entities.
// MonoBehaviour singleton - subscribes to FactionResearchState.OnTechCompleted.

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
    public partial class TechEffectSystem : MonoBehaviour
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
            if (!TechCatalog.IsReady) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // ── Behaviour techs (no stat-effects block; wired by id) ────────
            switch (techId)
            {
                case "WarriorPriests":
                    ApplyWarriorPriests(em, faction);
                    break;
                case "ReinforcedWalls":
                    ApplyReinforcedWalls(em, faction);
                    break;
                case "MasonGuild":
                    ApplyMasonGuild(em, faction);
                    break;
                case "ScoutingCelestarii":
                    ApplyScoutingCelestarii(em, faction);
                    break;
                case "ArmedScouts":
                    ApplyArmedScouts(em, faction);
                    break;
                case "RetaliatoryMeasures":
                    ApplyRetaliatoryMeasures(em, faction);
                    break;
                case "WarHorn":
                    GrantCavalryAbility(em, faction, "War Horn");
                    break;
                case "FullGallop":
                    GrantCavalryAbility(em, faction, "Full Gallop");
                    break;
                case "Charge":
                    GrantPassiveToUnits(em, faction, AlanthorPassiveTarget.GarrisonInfantry,
                        (e) => AddOrSet(em, e, new TheWaningBorder.Abilities.FirstStrike
                        { Pct = 30f, Ready = 1 }));
                    break;
                case "ShieldWall":
                    GrantPassiveToUnits(em, faction, AlanthorPassiveTarget.GarrisonInfantry,
                        (e) => AddOrSet(em, e, new TheWaningBorder.Abilities.ShieldWallState
                        { Pct = 30f }));
                    break;
                case "DeployStakes":
                    GrantPassiveToUnits(em, faction, AlanthorPassiveTarget.Archers,
                        (e) => AddOrSet(em, e, new TheWaningBorder.Abilities.StakesState
                        { Pct = 50f }));
                    break;
                case "SiegeScreens":
                    GrantPassiveToUnits(em, faction, AlanthorPassiveTarget.Siege,
                        (e) => AddOrSet(em, e, new TheWaningBorder.Abilities.SiegeScreens
                        { Pct = 50f }));
                    break;
                case "FieldHospital":
                    GrantLitharchFieldHospital(em, faction);
                    break;
                // RangingShot and ChoreographedVolleys are player-triggered actives,
                // not stamped state: see AlanthorActiveHelper.
                case "RangingShot":
                case "ChoreographedVolleys":
                    break;
                // The Gatherer's Hut Guild "survey" (resource) and
                // "reinforcement" (auto-repair / slow / stop) techs are read
                // live from FactionResearchState by TerritoryIncomeSystem (the
                // survey ladders scale a territory's deposit trickle) and
                // GathererHutReinforcementSystem — no one-shot application here.
                // The Shrine heal ladder (HeightenedMasses/PiousMasses/
                // FervoredMasses), the Vault banking techs, Conscription, and
                // the Keep emplacements are read live from
                // FactionResearchState by their owning systems — no one-shot
                // application needed here.
            }

            var tech = TechCatalog.GetTechnology(techId);
            if (tech == null) return;

            // ── Generic target/op/stat effects (calculator model, Wave 2) ──
            // Runs regardless of the legacy six-field block; a tech with an
            // empty/absent effectsList is a harmless no-op here (ability
            // techs like Charge/WarHorn wait for their behavior wiring).
            ApplyGenericEffects(em, tech, faction);

            if (tech.effects == null || !tech.effects.HasAnyEffect)
            {
                return;
            }

            var effects = tech.effects;

            // Apply gather speed to miners
            if (effects.gatherSpeedMult != 0f)
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

            // Flat damage bumps per damage type (Stone Weapons / Stone-Tipped Arrows)
            if (effects.meleeDamageAdd != 0)
            {
                ApplyDamageAddEffect(em, faction, DamageType.Melee, effects.meleeDamageAdd);
            }
            if (effects.rangedDamageAdd != 0)
            {
                ApplyDamageAddEffect(em, faction, DamageType.Ranged, effects.rangedDamageAdd);
            }

            // Archer range multiplier (Fletching)
            if (effects.archerRangeMult != 0f)
            {
                ApplyArcherRangeEffect(em, faction, effects.archerRangeMult);
            }
        }

    }
}
