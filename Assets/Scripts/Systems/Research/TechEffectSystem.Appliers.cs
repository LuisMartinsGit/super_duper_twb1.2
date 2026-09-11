// TechEffectSystem.Appliers.cs
// Per-tech effect appliers -- one method per hand-written technology effect.
// Partial of TechEffectSystem.cs -- split 2026-08-12 for readability.

using UnityEngine;
using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Research
{
    public partial class TechEffectSystem : MonoBehaviour
    {
        static readonly ComponentType[] QT_HutTagFactionTag =
        {
            ComponentType.ReadOnly<HutTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_HutTagFactionTag;
        static readonly ComponentType[] QT_ScoutSightStateFactionTag =
        {
            ComponentType.ReadOnly<TheWaningBorder.Abilities.ScoutSightState>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_ScoutSightStateFactionTag;
        static readonly ComponentType[] QT_UnitTagArmorTypeDataFactionTag =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<ArmorTypeData>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_UnitTagArmorTypeDataFactionTag;
        static readonly ComponentType[] QT_UnitTagUnitTypeIdFactionTag =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_UnitTagUnitTypeIdFactionTag;
        static readonly ComponentType[] QT_LitharchTagFactionTag =
        {
            ComponentType.ReadOnly<LitharchTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_LitharchTagFactionTag;
        static readonly ComponentType[] QT_BuildingTagFactionTagHealth =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTagHealth;
        static readonly ComponentType[] QT_FiendstoneKeepTagFactionTagHealth =
        {
            ComponentType.ReadOnly<FiendstoneKeepTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_FiendstoneKeepTagFactionTagHealth;
        static readonly ComponentType[] QT_LitharchTagFactionTagDamage =
        {
            ComponentType.ReadOnly<LitharchTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Damage>(),
        };
        static CachedEntityQuery QC_LitharchTagFactionTagDamage;
        static readonly ComponentType[] QT_FactionTagArcherState =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<ArcherState>(),
        };
        static CachedEntityQuery QC_FactionTagArcherState;
        static readonly ComponentType[] QT_UnitTagFactionTagDamageTypeDataDamage =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<DamageTypeData>(),
            ComponentType.ReadOnly<Damage>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagDamageTypeDataDamage;
        static readonly ComponentType[] QT_FactionTagDefense =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Defense>(),
        };
        static CachedEntityQuery QC_FactionTagDefense;
        static readonly ComponentType[] QT_FactionTagDamageTypeDataAttackCooldown =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<DamageTypeData>(),
            ComponentType.ReadOnly<AttackCooldown>(),
        };
        static CachedEntityQuery QC_FactionTagDamageTypeDataAttackCooldown;

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_MinerTagFactionTagMinerState =
        {
            ComponentType.ReadOnly<MinerTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<MinerState>(),
        };
        static CachedEntityQuery QC_MinerTagFactionTagMinerState;

        #endregion
        // ═══════════════════════════════════════════════════════════════
        // EFFECT APPLICATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply gather speed multiplier to all faction miners.
        /// </summary>
        private static void ApplyMinerEffects(EntityManager em, Faction faction, TechEffects effects)
        {
            var query = QC_MinerTagFactionTagMinerState.Get(em, QT_MinerTagFactionTagMinerState);

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
            var query = QC_FactionTagDamageTypeDataAttackCooldown.Get(em, QT_FactionTagDamageTypeDataAttackCooldown);

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
            var query = QC_FactionTagDefense.Get(em, QT_FactionTagDefense);

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
            var query = QC_UnitTagFactionTagDamageTypeDataDamage.Get(em, QT_UnitTagFactionTagDamageTypeDataDamage);

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
            var query = QC_FactionTagArcherState.Get(em, QT_FactionTagArcherState);

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
            var query = QC_LitharchTagFactionTagDamage.Get(em, QT_LitharchTagFactionTagDamage);

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
            var query = QC_FiendstoneKeepTagFactionTagHealth.Get(em, QT_FiendstoneKeepTagFactionTagHealth);

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
            var query = QC_BuildingTagFactionTagHealth.Get(em, QT_BuildingTagFactionTagHealth);
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

        /// <summary>Field Hospital (Sect of Renewal research, bought at the
        /// Mending Hall) — grant the deploy ability to every existing Litharch
        /// of the faction (new Litharchs pick it up at spawn).</summary>
        private static void GrantLitharchFieldHospital(EntityManager em, Faction faction)
        {
            int idx = TheWaningBorder.Abilities.AbilityCatalog.IndexOf("Deploy Field Hospital");
            if (idx < 0) return;
            var query = QC_LitharchTagFactionTag.Get(em, QT_LitharchTagFactionTag);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                TheWaningBorder.Abilities.AbilityAssignment.AddAbility(em, entities[i], idx);
            }
        }

        /// <summary>Which roster an Alanthor combat passive applies to.</summary>
        private enum AlanthorPassiveTarget { GarrisonInfantry, Archers, Siege, Cavalry }

        /// <summary>
        /// Stamp a passive component on every existing unit of the faction that
        /// belongs to the given roster. New units get the same component at spawn
        /// via the research check in their factory. Rosters are matched by
        /// UnitTypeId so a tech only touches the units the calculator lists.
        /// </summary>
        private static void GrantPassiveToUnits(EntityManager em, Faction faction,
            AlanthorPassiveTarget roster, System.Action<Entity> stamp)
        {
            var query = QC_UnitTagUnitTypeIdFactionTag.Get(em, QT_UnitTagUnitTypeIdFactionTag);
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
                    return IdIs(unitId, "Spearman") || IdIs(unitId, "Swordsman")
                        || IdIs(unitId, "Nobleman") || IdIs(unitId, "Sentinel");
                case AlanthorPassiveTarget.Archers:
                    return IdIs(unitId, "Archer") || IdIs(unitId, "Crossbowman")
                        || IdIs(unitId, "Longbowman");
                case AlanthorPassiveTarget.Siege:
                    return IdIs(unitId, "Ballista") || IdIs(unitId, "BatteringRam")
                        || IdIs(unitId, "Trebuchet");
                case AlanthorPassiveTarget.Cavalry:
                    return IdIs(unitId, "Outrider") || IdIs(unitId, "Cataphract");
                default:
                    return false;
            }
        }

        /// <summary>
        /// Unit-id comparison with the SAME `Alanthor_` aliasing tolerance the
        /// generic effects engine has (MatchesTarget in TechEffectSystem.Generic).
        ///
        /// The two matchers disagreeing was a silent bug: the Archer's authored
        /// id is `Alanthor_Archer`, so the old literal `unitId == "Archer"` test
        /// never matched and the base Archer was the one unit in the roster that
        /// never received Deploy Stakes — while the veterancy techs, which route
        /// through the aliasing matcher, hit it correctly. Every roster entry is
        /// written unprefixed here and aliased at the comparison, so an id that
        /// gains or loses the culture prefix cannot break a roster again.
        /// </summary>
        private static bool IdIs(string actual, string wanted)
            => actual == wanted
            || actual == "Alanthor_" + wanted
            || wanted == "Alanthor_" + actual;

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
            var query = QC_UnitTagArmorTypeDataFactionTag.Get(em, QT_UnitTagArmorTypeDataFactionTag);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (em.GetComponentData<ArmorTypeData>(entities[i]).Value != ArmorType.Cavalry) continue;
                TheWaningBorder.Abilities.AbilityAssignment.AddAbility(em, entities[i], idx);
            }
        }

        /// <summary>
        /// Archery Range unit actives — grant an ability to every existing
        /// RANGED unit of the faction (new ones pick it up at spawn via
        /// AlanthorActiveHelper.ApplySpawnPassives). Selected on
        /// DamageType.Ranged, the same test the ranged damage ladder uses.
        /// </summary>
        private static void GrantRangedAbility(EntityManager em, Faction faction, string abilityName)
        {
            int idx = TheWaningBorder.Abilities.AbilityCatalog.IndexOf(abilityName);
            if (idx < 0) return;
            var query = QC_UnitTagFactionTagDamageTypeDataDamage.Get(em, QT_UnitTagFactionTagDamageTypeDataDamage);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var damageTypes = query.ToComponentDataArray<DamageTypeData>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (damageTypes[i].Value != DamageType.Ranged) continue;
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
            var query = QC_ScoutSightStateFactionTag.Get(em, QT_ScoutSightStateFactionTag);
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

            var query = QC_ScoutSightStateFactionTag.Get(em, QT_ScoutSightStateFactionTag);
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
            var query = QC_HutTagFactionTag.Get(em, QT_HutTagFactionTag);
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
    }
}
