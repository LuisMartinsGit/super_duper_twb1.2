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
            if (!TechCatalog.IsReady) return;

            // task-063 phase 1: sect-tech bridge deleted. The redesigned sect
            // system does not bridge tech research to sects — chapels are the
            // only adoption mechanism. Old IsSectTech / SetTechFlag /
            // RecalculateAllPassives chain is gone.

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
                // live from FactionResearchState by GathererHutIncomeSystem and
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

        // ═══════════════════════════════════════════════════════════════
        // EFFECT APPLICATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply gather speed multiplier to all faction miners.
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

        /// <summary>
        /// Flat damage bonus for all faction units of the given damage type
        /// (Stone Weapons: melee +2; Stone-Tipped Arrows: ranged +2). Interim
        /// faction-wide application until the per-battalion upgrade pattern
        /// ships.
        /// </summary>
        private static void ApplyDamageAddEffect(EntityManager em, Faction faction, DamageType dmgType, int bonus)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<DamageTypeData>(),
                ComponentType.ReadWrite<Damage>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var damageTypes = query.ToComponentDataArray<DamageTypeData>(Allocator.Temp);
            using var damages = query.ToComponentDataArray<Damage>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (damageTypes[i].Value != dmgType) continue;
                // Only affect units that actually fight (skip disarmed healers).
                if (damages[i].Value <= 0) continue;

                var dmg = damages[i];
                dmg.Value += bonus;
                em.SetComponentData(entities[i], dmg);
            }
        }

        /// <summary>Fletching: multiply every faction Archer's max range.</summary>
        private static void ApplyArcherRangeEffect(EntityManager em, Faction faction, float mult)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<ArcherState>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var states = query.ToComponentDataArray<ArcherState>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                var s = states[i];
                s.MaxRange *= mult;
                em.SetComponentData(entities[i], s);
            }
        }

        /// <summary>
        /// Warrior Priests: existing faction Litharchs gain their melee attack
        /// (6 damage / 1.5 s per the Age 0 design). Litharchs already carry
        /// Damage(0) + AttackCooldown, so this is a value write, not a
        /// structural change. New Litharchs get the same treatment at spawn
        /// via ApplyCompletedTechEffects.
        /// </summary>
        private static void ApplyWarriorPriests(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LitharchTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Damage>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                GrantLitharchAttack(em, entities[i]);
            }
        }

        internal const int WarriorPriestDamage = 6;
        internal const float WarriorPriestCooldown = 1.5f;

        private static void GrantLitharchAttack(EntityManager em, Entity litharch)
        {
            if (em.HasComponent<Damage>(litharch))
            {
                var dmg = em.GetComponentData<Damage>(litharch);
                if (dmg.Value < WarriorPriestDamage)
                {
                    dmg.Value = WarriorPriestDamage;
                    em.SetComponentData(litharch, dmg);
                }
            }
            if (em.HasComponent<AttackCooldown>(litharch))
            {
                var cd = em.GetComponentData<AttackCooldown>(litharch);
                cd.Cooldown = WarriorPriestCooldown;
                em.SetComponentData(litharch, cd);
            }
        }

        /// <summary>
        /// Reinforced Walls: +20% Max HP on the faction's Fiendstone Keeps
        /// (current HP scales proportionally). Keeps built AFTER the research
        /// get the bonus at creation (BuildingFactory checks the research).
        /// </summary>
        private static void ApplyReinforcedWalls(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FiendstoneKeepTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                var hp = healths[i];
                hp.Max = (int)(hp.Max * 1.2f);
                hp.Value = (int)(hp.Value * 1.2f);
                em.SetComponentData(entities[i], hp);
            }
        }

        /// <summary>Alanthor Mason Guild — +30% HP on ALL of the faction's
        /// buildings (design 2026-07: tree value wins over the old +15%).</summary>
        private static void ApplyMasonGuild(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                var hp = healths[i];
                hp.Max = (int)(hp.Max * 1.30f);
                hp.Value = (int)(hp.Value * 1.30f);
                em.SetComponentData(entities[i], hp);
            }
        }

        /// <summary>Field Hospital — grant the deploy ability to every existing
        /// Litharch of the faction (new Litharchs pick it up at spawn).</summary>
        private static void GrantLitharchFieldHospital(EntityManager em, Faction faction)
        {
            int idx = TheWaningBorder.Abilities.AbilityCatalog.IndexOf("Deploy Field Hospital");
            if (idx < 0) return;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LitharchTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                TheWaningBorder.Abilities.AbilityAssignment.AddAbility(em, entities[i], idx);
            }
        }

        /// <summary>Which roster an Alanthor combat passive applies to.</summary>
        private enum AlanthorPassiveTarget { GarrisonInfantry, Archers, Siege }

        /// <summary>
        /// Stamp a passive component on every existing unit of the faction that
        /// belongs to the given roster. New units get the same component at spawn
        /// via the research check in their factory. Rosters are matched by
        /// UnitTypeId so a tech only touches the units the calculator lists.
        /// </summary>
        private static void GrantPassiveToUnits(EntityManager em, Faction faction,
            AlanthorPassiveTarget roster, System.Action<Entity> stamp)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<UnitTypeId>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (!MatchesRoster(em.GetComponentData<UnitTypeId>(entities[i]).Value.ToString(), roster)) continue;
                stamp(entities[i]);
            }
        }

        /// <summary>Roster membership by unit id (the calculator's lists).</summary>
        internal static bool MatchesRoster(string unitId, int rosterKind)
            => MatchesRoster(unitId, (AlanthorPassiveTarget)rosterKind);

        private static bool MatchesRoster(string unitId, AlanthorPassiveTarget roster)
        {
            switch (roster)
            {
                case AlanthorPassiveTarget.GarrisonInfantry:
                    return unitId == "Spearman" || unitId == "Alanthor_Swordsman"
                        || unitId == "Alanthor_Nobleman" || unitId == "Alanthor_Sentinel";
                case AlanthorPassiveTarget.Archers:
                    return unitId == "Archer" || unitId == "Alanthor_Crossbowman"
                        || unitId == "Alanthor_Longbowman";
                case AlanthorPassiveTarget.Siege:
                    return unitId == "Alanthor_Ballista" || unitId == "Alanthor_BatteringRam"
                        || unitId == "Alanthor_Trebuchet";
                default:
                    return false;
            }
        }

        private static void AddOrSet<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }

        /// <summary>Royal Stable horn techs — grant an ability to every existing
        /// cavalry unit of the faction (new cavalry picks it up at spawn via the
        /// research check in the Outrider / Cataphract factories). Cavalry is
        /// identified by ArmorType.Cavalry, the same convention the charge
        /// mechanic and King's Call use.</summary>
        private static void GrantCavalryAbility(EntityManager em, Faction faction, string abilityName)
        {
            int idx = TheWaningBorder.Abilities.AbilityCatalog.IndexOf(abilityName);
            if (idx < 0) return;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<ArmorTypeData>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (em.GetComponentData<ArmorTypeData>(entities[i]).Value != ArmorType.Cavalry) continue;
                TheWaningBorder.Abilities.AbilityAssignment.AddAbility(em, entities[i], idx);
            }
        }

        /// <summary>Scouting Celestarii — grant the Use Celestar reveal ability to
        /// every existing faction Scout (new Scouts pick it up at spawn via the
        /// research check in Scout.Create).</summary>
        private static void ApplyScoutingCelestarii(EntityManager em, Faction faction)
        {
            int idx = TheWaningBorder.Abilities.AbilityCatalog.IndexOf("Use Celestar");
            if (idx < 0) return;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TheWaningBorder.Abilities.ScoutSightState>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                TheWaningBorder.Abilities.AbilityAssignment.AddAbility(em, entities[i], idx);
            }
        }

        /// <summary>Armed scouts — arm every existing faction Scout with its SO
        /// melee damage (new Scouts pick it up at spawn via the research check
        /// in Scout.Create). Until researched Scouts spawn with Damage 0, which
        /// the TargetingSystem short-circuit keeps out of combat entirely.</summary>
        private static void ApplyArmedScouts(EntityManager em, Faction faction)
        {
            int damage = 2; // design-doc default; SO wins when authored
            if (TechCatalog.TryGetUnit("Scout", out var def) && def.damage > 0)
                damage = (int)def.damage;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TheWaningBorder.Abilities.ScoutSightState>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (em.HasComponent<Damage>(entities[i]))
                    em.SetComponentData(entities[i], new Damage { Value = damage });
            }
        }

        /// <summary>Retaliatory measures — the faction's existing Houses gain an
        /// auto-fire arrow attack (BuildingRangedAttack). Houses built after the
        /// research pick it up at spawn in Hut.Create.</summary>
        private const float RetaliatoryRange = 12f;
        private const int RetaliatoryDamage = 12;
        private const float RetaliatoryCooldown = 2.5f;

        private static void ApplyRetaliatoryMeasures(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HutTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var house = entities[i];
                if (em.HasComponent<BuildingRangedAttack>(house))
                {
                    var atk = em.GetComponentData<BuildingRangedAttack>(house);
                    atk.Range = RetaliatoryRange;
                    atk.Damage = RetaliatoryDamage;
                    atk.Cooldown = RetaliatoryCooldown;
                    atk.MaxTargets = 1;
                    em.SetComponentData(house, atk);
                }
                else
                {
                    em.AddComponentData(house, new BuildingRangedAttack
                    {
                        Range = RetaliatoryRange,
                        Damage = RetaliatoryDamage,
                        Cooldown = RetaliatoryCooldown,
                        Timer = 0f,
                        MaxTargets = 1,
                    });
                }
                if (!em.HasComponent<DamageTypeData>(house))
                    em.AddComponentData(house, new DamageTypeData { Value = DamageType.Ranged });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GENERIC EFFECTS ENGINE (target/op/stat model, Wave 2)
        // ═══════════════════════════════════════════════════════════════
        //
        // Ordering caveat: percentage effects bake into live component values
        // (no baseline recompute); they stack multiplicatively with the
        // UnitRank/EquipmentTier diff layers in whatever order they land.
        // This matches the existing engine's behavior — do NOT build a
        // recompute model here.

        /// <summary>
        /// Faction-wide sweep on tech completion: apply every matching
        /// effectsList entry to every existing unit of the faction.
        /// Empty/absent lists are a harmless no-op (ability-unlock techs).
        /// </summary>
        public static void ApplyGenericEffects(EntityManager em, TechnologyDef def, Faction faction)
        {
            if (def?.effectsList == null || def.effectsList.Count == 0) return;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                ApplyGenericEffectsToEntity(em, entities[i], def);
            }
        }

        /// <summary>
        /// Per-entity variant: apply every matching effectsList entry of ONE
        /// tech to ONE unit (spawn path via ApplyCompletedTechEffects).
        /// </summary>
        private static void ApplyGenericEffectsToEntity(EntityManager em, Entity unit, TechnologyDef def)
        {
            var list = def.effectsList;
            for (int i = 0; i < list.Count; i++)
            {
                var fx = list[i];
                if (fx == null) continue;
                if (!MatchesTarget(em, unit, fx.Target)) continue;
                ApplyStatWrite(em, unit, fx.Stat, fx.Op, fx.Value);
            }
        }

        /// <summary>
        /// Target matcher for one effectsList entry.
        /// "type:Melee"/"type:Ranged" match by DamageTypeData on units;
        /// "type:Cavalry" matches ArmorType.Cavalry (the King's Call
        /// convention — Outrider/Cataphract deal Melee damage);
        /// "type:Siege" matches UnitTag.Class; "unit:X" matches the
        /// UnitTypeId stamp with Alanthor_ prefix aliasing tolerance.
        /// </summary>
        private static bool MatchesTarget(EntityManager em, Entity unit, string target)
        {
            if (string.IsNullOrEmpty(target)) return false;

            if (target.StartsWith("type:", System.StringComparison.Ordinal))
            {
                switch (target.Substring(5))
                {
                    case "Melee":
                        return em.HasComponent<UnitTag>(unit)
                            && em.HasComponent<DamageTypeData>(unit)
                            && em.GetComponentData<DamageTypeData>(unit).Value == DamageType.Melee;
                    case "Ranged":
                        return em.HasComponent<UnitTag>(unit)
                            && em.HasComponent<DamageTypeData>(unit)
                            && em.GetComponentData<DamageTypeData>(unit).Value == DamageType.Ranged;
                    case "Cavalry":
                        return em.HasComponent<ArmorTypeData>(unit)
                            && em.GetComponentData<ArmorTypeData>(unit).Value == ArmorType.Cavalry;
                    case "Siege":
                        return em.HasComponent<UnitTag>(unit)
                            && em.GetComponentData<UnitTag>(unit).Class == UnitClass.Siege;
                    default:
                        return false;
                }
            }

            if (target.StartsWith("unit:", System.StringComparison.Ordinal))
            {
                if (!em.HasComponent<UnitTypeId>(unit)) return false;
                string wanted = target.Substring(5);
                string actual = em.GetComponentData<UnitTypeId>(unit).Value.ToString();
                // Id aliasing tolerance: "unit:Swordsman" hits
                // "Alanthor_Swordsman" and vice versa.
                return actual == wanted
                    || actual == "Alanthor_" + wanted
                    || wanted == "Alanthor_" + actual;
            }

            return false;
        }

        /// <summary>Floor for tech-modified attack cooldowns.</summary>
        private const float MinAttackCooldown = 0.1f;

        /// <summary>
        /// Destructive in-place stat write for one effectsList entry.
        /// Components the entity lacks are silently skipped. Ops:
        /// "Add" (+= Value), "Pct" (*= 1 + Value/100).
        /// </summary>
        private static void ApplyStatWrite(EntityManager em, Entity unit, string stat, string op, float v)
        {
            bool pct = op == "Pct";
            float mult = 1f + v / 100f;

            switch (stat)
            {
                case "Hp":
                {
                    if (!em.HasComponent<Health>(unit)) return;
                    var hp = em.GetComponentData<Health>(unit);
                    if (pct)
                    {
                        hp.Max = Mathf.RoundToInt(hp.Max * mult);
                        hp.Value = Mathf.RoundToInt(hp.Value * mult);
                    }
                    else
                    {
                        // Bump current and max together so the HP ratio stays sane.
                        hp.Max += (int)v;
                        hp.Value += (int)v;
                    }
                    if (hp.Max < 1) hp.Max = 1;
                    if (hp.Value > hp.Max) hp.Value = hp.Max;
                    em.SetComponentData(unit, hp);
                    return;
                }
                case "Damage":
                {
                    if (!em.HasComponent<Damage>(unit)) return;
                    var dmg = em.GetComponentData<Damage>(unit);
                    // Disarmed-unit convention (see ApplyDamageAddEffect):
                    // never arm a Damage<=0 unit through a stat tech.
                    if (dmg.Value <= 0) return;
                    dmg.Value = pct ? Mathf.RoundToInt(dmg.Value * mult) : dmg.Value + (int)v;
                    em.SetComponentData(unit, dmg);
                    return;
                }
                case "Speed":
                {
                    if (!em.HasComponent<MoveSpeed>(unit)) return;
                    var spd = em.GetComponentData<MoveSpeed>(unit);
                    spd.Value = pct ? spd.Value * mult : spd.Value + v;
                    em.SetComponentData(unit, spd);
                    return;
                }
                case "DefenseAll":
                {
                    if (!em.HasComponent<Defense>(unit)) return;
                    var def = em.GetComponentData<Defense>(unit);
                    if (pct)
                    {
                        def.Melee = Mathf.RoundToInt(def.Melee * mult);
                        def.Ranged = Mathf.RoundToInt(def.Ranged * mult);
                        def.Siege = Mathf.RoundToInt(def.Siege * mult);
                        def.Magic = Mathf.RoundToInt(def.Magic * mult);
                    }
                    else
                    {
                        def.Melee += (int)v;
                        def.Ranged += (int)v;
                        def.Siege += (int)v;
                        def.Magic += (int)v;
                    }
                    em.SetComponentData(unit, def);
                    return;
                }
                case "AttackRange":
                {
                    // Only entities with ArcherState carry an explicit range
                    // field (MaxRange) — that includes ranged siege engines
                    // like the Ballista. Melee reach is derived from body
                    // Radius (edge-aware combat), so melee units (e.g. the
                    // Battering Ram) have no range field and are skipped.
                    if (!em.HasComponent<ArcherState>(unit)) return;
                    var ast = em.GetComponentData<ArcherState>(unit);
                    ast.MaxRange = pct ? ast.MaxRange * mult : ast.MaxRange + v;
                    em.SetComponentData(unit, ast);
                    return;
                }
                case "AttackCooldown":
                {
                    if (!em.HasComponent<AttackCooldown>(unit)) return;
                    var cd = em.GetComponentData<AttackCooldown>(unit);
                    // Pct: NEGATIVE v shrinks the cooldown (-30 => *0.7).
                    cd.Cooldown = pct ? cd.Cooldown * mult : cd.Cooldown + v;
                    if (cd.Cooldown < MinAttackCooldown) cd.Cooldown = MinAttackCooldown;
                    em.SetComponentData(unit, cd);
                    return;
                }
                case "LineOfSight":
                {
                    if (!em.HasComponent<LineOfSight>(unit)) return;
                    var los = em.GetComponentData<LineOfSight>(unit);
                    los.Radius = pct ? los.Radius * mult : los.Radius + v;
                    em.SetComponentData(unit, los);
                    return;
                }
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
            if (researchState == null || !TechCatalog.IsReady) return;

            var completedTechs = researchState.GetCompletedTechs(faction);
            if (completedTechs.Count == 0) return;

            bool hasMinerState = em.HasComponent<MinerState>(unit);
            bool hasAttackCooldown = em.HasComponent<AttackCooldown>(unit);
            bool hasDefense = em.HasComponent<Defense>(unit);
            bool hasDamage = em.HasComponent<Damage>(unit);
            bool hasArcherState = em.HasComponent<ArcherState>(unit);
            bool hasDamageType = em.HasComponent<DamageTypeData>(unit);
            bool isMelee = hasDamageType && em.GetComponentData<DamageTypeData>(unit).Value == DamageType.Melee;
            bool isRanged = hasDamageType && em.GetComponentData<DamageTypeData>(unit).Value == DamageType.Ranged;

            foreach (var techId in completedTechs)
            {
                // Behaviour techs by id (no effects block).
                if (techId == "WarriorPriests" && em.HasComponent<LitharchTag>(unit))
                {
                    GrantLitharchAttack(em, unit);
                    continue;
                }

                var tech = TechCatalog.GetTechnology(techId);
                if (tech == null) continue;

                // Generic target/op/stat effects (calculator model, Wave 2).
                if (tech.effectsList != null && tech.effectsList.Count > 0)
                    ApplyGenericEffectsToEntity(em, unit, tech);

                if (tech.effects == null || !tech.effects.HasAnyEffect) continue;

                var effects = tech.effects;

                // Flat damage bumps (skip disarmed units, e.g. Litharch pre-WarriorPriests)
                if (hasDamage && effects.meleeDamageAdd != 0 && isMelee)
                {
                    var dmg = em.GetComponentData<Damage>(unit);
                    if (dmg.Value > 0) { dmg.Value += effects.meleeDamageAdd; em.SetComponentData(unit, dmg); }
                }
                if (hasDamage && effects.rangedDamageAdd != 0 && isRanged)
                {
                    var dmg = em.GetComponentData<Damage>(unit);
                    if (dmg.Value > 0) { dmg.Value += effects.rangedDamageAdd; em.SetComponentData(unit, dmg); }
                }

                // Archer range (Fletching)
                if (hasArcherState && effects.archerRangeMult != 0f)
                {
                    var ast = em.GetComponentData<ArcherState>(unit);
                    ast.MaxRange *= effects.archerRangeMult;
                    em.SetComponentData(unit, ast);
                }

                // Miner effects
                if (hasMinerState && effects.gatherSpeedMult != 0f)
                {
                    var miner = em.GetComponentData<MinerState>(unit);
                    miner.GatherSpeedMultiplier *= effects.gatherSpeedMult;
                    em.SetComponentData(unit, miner);
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
