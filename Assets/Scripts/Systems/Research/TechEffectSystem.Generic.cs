// TechEffectSystem.Generic.cs
// The generic target/op/stat effects engine (Wave 2 data-driven techs).
// Partial of TechEffectSystem.cs -- split 2026-08-12 for readability.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Research
{
    public partial class TechEffectSystem : MonoBehaviour
    {
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
