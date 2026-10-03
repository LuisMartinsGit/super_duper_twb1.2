// Applies a building level: bump BuildingUpgradeState.Level and recompute
// scaled stats from BASE values (NOT current — that way any reapply is
// idempotent).
//
// NO LONGER AN ECS SYSTEM. It used to tick BuildingUpgrading itself, which
// made level-ups a second, private timer alongside research. Both are queued
// items on one buffer now and ProductionQueueSystem owns the single clock —
// it stamps BuildingUpgrading when the item starts, drives its Progress, and
// calls CompleteUpgrade below when the timer runs out. The name is kept
// because five callers outside this file reach for
// BuildingUpgradeSystem.ApplyLevel, and because "System" in this codebase
// means a domain's behaviour rather than an ECS base class (see CLAUDE.md,
// Systems/Core/VictoryConditionSystem).
//
// Per-building specials:
//   - Hall: BuildingRangedAttack.MaxTargets follows BuildingUpgradeConfig.HallMaxTargets[level].
//   - Barracks: gains BuildingRangedAttack at level 3 (component is added
//       fresh — base cooldown captured here, not in the command helper, since
//       Barracks has no attack at level 0).
//   - Hut: PopulationProvider.Amount = base + HutBonusPop[level].
//
// The instant paths (BuildingCultureAutoLevelSystem, BuildingLevelOneSeedSystem,
// StartAgePromoter, ScenarioSetup) call ApplyLevel DIRECTLY and deliberately
// skip CompleteUpgrade: an age-up auto-level is not a Feraldis House finishing
// an upgrade, and must not spawn its raiders.

using Unity.Entities;
using TheWaningBorder.Core;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Buildings
{
    public static class BuildingUpgradeSystem
    {
        // Barracks — when it gains its first attack at level 3, use these.
        // Mirrors Hall's stats from Hall.cs (Range 20, Damage 12, Cooldown 2.5s).
        // Barracks is closer to a watchtower than a keep — same range/cooldown,
        // slightly lower damage so it's not a strict Hall upgrade.
        private const float BarracksAttackRange    = 18f;
        private const int   BarracksAttackDamage   = 8;
        private const float BarracksAttackCooldown = 2.5f;

        /// <summary>
        /// A queued level-up finished: apply the level and fire the effects
        /// that belong to FINISHING one. Called only by ProductionQueueSystem.
        /// </summary>
        public static void CompleteUpgrade(EntityManager em, Entity building, byte level)
        {
            if (!em.Exists(building)) return;

            ApplyLevel(em, building, level);

            // task-066 Phase 3: Feraldis House upgrade ticks spawn raiders
            // (2 at L2, 3 at L3). The initial L1 build is handled in
            // BuildingConstructionSystem.CompleteConstruction.
            if (em.HasComponent<HutTag>(building) && em.HasComponent<FactionTag>(building))
            {
                var fac = em.GetComponentData<FactionTag>(building).Value;
                if (FactionColors.GetFactionCulture(fac) == Cultures.Feraldis && level >= 2)
                    SpawnFeraldisRaiders(em, building, fac, count: level);
            }

            // Debug log so the upgrade pipeline is visible during playtesting.
            // Identifies the building type by tag — same resolution the command
            // helper uses.
            if (em.HasComponent<FactionTag>(building))
            {
                string id = em.HasComponent<HallTag>(building)     ? "Hall"
                         :  em.HasComponent<BarracksTag>(building) ? "Barracks"
                         :  em.HasComponent<HutTag>(building)      ? "Hut"
                         :                                           "Building";
                var fac = em.GetComponentData<FactionTag>(building).Value;
                TWBLog.Log($"[Upgrade] {fac} {id} → L{level}");
            }
        }

        /// <summary>
        /// Set BuildingUpgradeState.Level = <paramref name="level"/> and
        /// recompute scaled stats from BASE values. Always idempotent —
        /// calling with the same level twice produces the same result.
        /// </summary>
        public static void ApplyLevel(EntityManager em, Entity building, byte level)
        {
            if (!em.HasComponent<BuildingUpgradeState>(building)) return;

            // Every per-level table is MaxLevel + 1 long: a level past the top
            // is a caller bug, but never an out-of-range crash.
            if (level > BuildingUpgradeConfig.MaxLevel)
            {
                UnityEngine.Debug.LogWarning($"[Upgrade] level {level} past MaxLevel {BuildingUpgradeConfig.MaxLevel} — clamped");
                level = BuildingUpgradeConfig.MaxLevel;
            }

            var ups = em.GetComponentData<BuildingUpgradeState>(building);
            ups.Level = level;
            em.SetComponentData(building, ups);

            // Debug.Log, not TWBLog, ON PURPOSE: this is the ONLY write point
            // for a building's level, and a 2026-08-17 report claimed "all
            // buildings of one type share their level" — a claim the per-
            // entity code cannot explain. One tagged line per level write
            // (a handful per match; the age-up auto-L1 wave legitimately
            // prints one per building) survives into player-build console
            // capture, so the next report shows exactly how many buildings
            // wrote a level and from where. TWBLog compiles out of builds.
            UnityEngine.Debug.Log(
                $"[Upgrade] entity {building.Index} -> L{level}"
                + (em.HasComponent<FactionTag>(building)
                    ? $" ({em.GetComponentData<FactionTag>(building).Value})" : ""));

            // The level's numbers: its BuildingLevelDefSO when the owner's
            // culture authors one (Alanthor), else the code tables.
            byte culture = em.HasComponent<FactionTag>(building)
                ? CultureConfig.GetCompletedCulture(em, em.GetComponentData<FactionTag>(building).Value)
                : Cultures.None;
            string ladderId = TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper
                .ResolveBuildingId(em, building);
            var stats = BuildingUpgradeConfig.StatsFor(culture, ladderId, level,
                ups.BasePopulationProvider, em.HasComponent<HutTag>(building), em.HasComponent<HallTag>(building));

            // Health: scale Max from base, scale current proportionally so
            // the visual HP bar stays at the same percentage. (Mid-combat
            // upgrades don't suddenly heal or kill the building.)
            if (em.HasComponent<Health>(building) && ups.BaseHpMax > 0)
            {
                int newMax = (int)(ups.BaseHpMax * stats.HpMultiplier);
                var hp = em.GetComponentData<Health>(building);
                float pct = hp.Max > 0 ? (float)hp.Value / hp.Max : 1f;
                hp.Max = newMax;
                hp.Value = Unity.Mathematics.math.clamp((int)(newMax * pct), 0, newMax);
                em.SetComponentData(building, hp);
            }

            // Attack: cooldown scales by AttackCooldownMultiplier. Hall +
            // Barracks-at-lvl-3 also pull from HallMaxTargets where applicable.
            ApplyAttackChanges(em, building, level, ups.BaseAttackCooldown, stats);

            // Population (the House ladder): absolute per level.
            if (stats.Population >= 0 && em.HasComponent<PopulationProvider>(building))
            {
                var pp = em.GetComponentData<PopulationProvider>(building);
                pp.Amount = stats.Population;
                em.SetComponentData(building, pp);
            }
        }

        /// <summary>
        /// Spawn N Feraldis Raider units near a House (Phase 3 of task-066).
        /// Uses the same offset pattern as BuildingConstructionSystem.SpawnFeraldisRaidersAtHouse
        /// so raiders fan out rather than stacking on the building footprint.
        /// </summary>
        private static void SpawnFeraldisRaiders(EntityManager em, Entity house, Faction faction, int count)
        {
            if (!em.HasComponent<LocalTransform>(house)) return;
            Unity.Mathematics.float3 housePos = em.GetComponentData<LocalTransform>(house).Position;
            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)Unity.Mathematics.math.max(count, 1)) * Unity.Mathematics.math.PI * 2f;
                var offset = new Unity.Mathematics.float3(
                    Unity.Mathematics.math.cos(angle) * 1.5f,
                    0f,
                    Unity.Mathematics.math.sin(angle) * 1.5f);
                TheWaningBorder.Entities.FeraldisRaider.CreateUncontrolled(em, housePos + offset, faction);
            }
        }

        private static void ApplyAttackChanges(EntityManager em, Entity building,
            byte level, float baseAttackCooldown, BuildingUpgradeConfig.LevelStats stats)
        {
            bool isHall     = em.HasComponent<HallTag>(building);
            bool isBarracks = em.HasComponent<BarracksTag>(building);

            if (isHall && em.HasComponent<BuildingRangedAttack>(building))
            {
                // Already attacks — scale cooldown, set MaxTargets per level.
                var atk = em.GetComponentData<BuildingRangedAttack>(building);
                atk.Cooldown = baseAttackCooldown * stats.AttackCooldownMultiplier;
                if (stats.MaxTargets > 0) atk.MaxTargets = stats.MaxTargets;
                em.SetComponentData(building, atk);
            }
            // A level SO authors the Barracks' level-3 arrows itself
            // (ApplyAuthoredLevel below); the constants are the code-table path.
            else if (isBarracks && level >= 3 && stats.Def == null)
            {
                // L3 Barracks gains a ranged attack. Apply the L3 attack-rate
                // multiplier to its cooldown for symmetry with Hall — a fully
                // upgraded Barracks fires at the same cadence as a fully
                // upgraded Hall.
                float scaledCooldown = BarracksAttackCooldown * stats.AttackCooldownMultiplier;

                // Gain ranged attack on first arrival at lvl 3. Idempotent —
                // we set the same fields whether the component is new or old.
                if (!em.HasComponent<BuildingRangedAttack>(building))
                {
                    em.AddComponentData(building, new BuildingRangedAttack
                    {
                        Range      = BarracksAttackRange,
                        Damage     = BarracksAttackDamage,
                        Cooldown   = scaledCooldown,
                        Timer      = 0f,
                        MaxTargets = 1,
                    });
                    // Barracks is normally not a damager — give it the standard
                    // ranged damage type so the projectile path treats arrows correctly.
                    if (!em.HasComponent<DamageTypeData>(building))
                        em.AddComponentData(building, new DamageTypeData { Value = DamageType.Ranged });
                }
                else
                {
                    var atk = em.GetComponentData<BuildingRangedAttack>(building);
                    atk.Range      = BarracksAttackRange;
                    atk.Damage     = BarracksAttackDamage;
                    atk.Cooldown   = scaledCooldown;
                    atk.MaxTargets = 1;
                    em.SetComponentData(building, atk);
                }
            }
            // Hut + Barracks below lvl 3 — no attack changes.

            ApplyAuthoredLevel(em, building, level, stats.Def);
        }

        /// <summary>
        /// A level AUTHORED on the building's SO (BuildingDef.levels) — every
        /// stat from data, absolute per level so re-applying is idempotent.
        /// Today the Watch Tower's ladder (Age_1_Alanthor.md § Watch Tower
        /// levels): range, fire rate and line of sight rise each level; the
        /// last adds targets and a ballista bolt (BuildingSiegeShot).
        /// Buildings with no authored level entry are untouched.
        /// </summary>
        private static void ApplyAuthoredLevel(EntityManager em, Entity building, byte level,
            TheWaningBorder.Data.BuildingLevelDefSO levelDef)
        {
            // The level SO first (it may GRANT an attack the building lacked —
            // the Garrison's level-3 arrows), else the BuildingDef.levels entry
            // of a culture still on the old model.
            TheWaningBorder.Data.BuildingAttack attack = null;
            float lineOfSight = 0f;
            if (levelDef != null)
            {
                attack = levelDef.attack;
                lineOfSight = levelDef.lineOfSight;
            }
            else
            {
                if (!em.HasComponent<BuildingRangedAttack>(building)) return;
                string id = TheWaningBorder.Entities.BuildingIds.Of(building, em);
                if (string.IsNullOrEmpty(id) || !TechCatalog.TryGetBuilding(id, out var def)
                    || def?.levels == null) return;
                foreach (var l in def.levels)
                    if (l != null && l.level == level) { attack = l.attack; lineOfSight = l.lineOfSight; break; }
            }

            if (lineOfSight > 0f && em.HasComponent<LineOfSight>(building))
                em.SetComponentData(building, new LineOfSight { Radius = lineOfSight });

            if (attack == null || !attack.enabled) return;

            var atk = em.HasComponent<BuildingRangedAttack>(building)
                ? em.GetComponentData<BuildingRangedAttack>(building) : default;
            atk.Range = attack.range;
            atk.Damage = (int)attack.damage;
            atk.Cooldown = attack.cooldown;
            atk.MaxTargets = attack.maxTargets;
            if (em.HasComponent<BuildingRangedAttack>(building)) em.SetComponentData(building, atk);
            else
            {
                em.AddComponentData(building, atk);
                if (!em.HasComponent<DamageTypeData>(building))
                    em.AddComponentData(building, new DamageTypeData { Value = DamageType.Ranged });
            }

            if (attack.siegeShotDamage > 0)
            {
                var shot = new BuildingSiegeShot { Damage = attack.siegeShotDamage };
                if (em.HasComponent<BuildingSiegeShot>(building)) em.SetComponentData(building, shot);
                else em.AddComponentData(building, shot);
            }
            else if (em.HasComponent<BuildingSiegeShot>(building))
                em.RemoveComponent<BuildingSiegeShot>(building);
        }
    }
}
