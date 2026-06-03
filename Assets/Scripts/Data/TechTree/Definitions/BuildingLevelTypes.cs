// BuildingLevelTypes.cs
// Serializable data types that let a BuildingDefSO express a multi-level ladder:
// per-level trainable units / available upgrades / building ranged attack, plus a
// pool of unit-upgrade definitions (stat deltas) referenced by each level.
// Part of: Data/TechTree/Definitions/
//
// Example (Archery Range): lvl 0 pre-culture trains Archers; on Alanthor age-up it
// becomes the "Practice Range" lvl 1 (trains 10% faster), lvl 2 adds Crossbowmen + a
// building ranged attack, lvl 3 adds Longbowmen + double-target attack. Unit upgrades
// (Seasoned -> Veteran -> Elite, Arrow volley -> shower, Deploy stakes) live in the
// shared pool and are unlocked per level via BuildingLevel.availableUpgrades.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    /// <summary>A building's own auto-fire ranged attack. enabled=false = no attack.</summary>
    [Serializable]
    public class BuildingAttack
    {
        public bool enabled;
        public float damage = 12f;
        [Tooltip("ranged | siege | magic")]
        public string damageType = "ranged";
        public float range = 22f;
        [Tooltip("Seconds between volleys.")]
        public float cooldown = 1.5f;
        [Tooltip("Enemies hit per volley. 1 = single target, 2 = double-targeting (lvl 3).")]
        public int maxTargets = 1;
    }

    /// <summary>The kind of thing an UpgradeEffect does.</summary>
    public enum UpgradeEffectKind
    {
        BuffStat = 0,        // add `amount` to `stat` on `unit`
        EnableAbility = 1,   // enable `ability` on `unit`
    }

    /// <summary>A unit stat an upgrade can buff.</summary>
    public enum UnitStat
    {
        Hp = 0,
        LineOfSight = 1,
        AttackRange = 2,
        Damage = 3,
        RateOfFirePercent = 4,   // percent bonus (30 = +30% fire rate)
        Speed = 5,
        AttackCooldown = 6,
        CarryCapacity = 7,
        Defense = 8,             // flat defense (all damage types)
        MoveSpeedPercent = 9,    // percent bonus to move speed (5 = +5%)
    }

    /// <summary>
    /// One editable effect of an upgrade. Either "Buff [stat] [amount] [unit]" or
    /// "Enable [ability] [unit]". An upgrade may carry several of these.
    /// </summary>
    [Serializable]
    public class UpgradeEffect
    {
        [Tooltip("Buff a stat, or enable an ability.")]
        public UpgradeEffectKind kind = UpgradeEffectKind.BuffStat;

        [Tooltip("Unit id this effect targets.")]
        public string unit;

        [Header("Buff Stat (kind = BuffStat)")]
        public UnitStat stat;
        [Tooltip("Amount added to the stat (RateOfFirePercent is a percent, e.g. 30 = +30%).")]
        public float amount;

        [Header("Enable Ability (kind = EnableAbility)")]
        [Tooltip("Ability id to enable on the unit.")]
        public string ability;
    }

    /// <summary>
    /// A researchable upgrade, defined as a list of editable effects (buffs / ability
    /// unlocks). Referenced from a level's availableUpgrades by id.
    /// </summary>
    [Serializable]
    public class UnitUpgrade
    {
        public string id;
        public string displayName;
        [TextArea(1, 2)] public string description;
        [Tooltip("Upgrade id that must be researched first (chain). Empty = no prerequisite.")]
        public string requires;

        [Tooltip("Effects applied when researched. Add Buff and/or Enable-Ability entries.")]
        public List<UpgradeEffect> effects = new List<UpgradeEffect>();

        [Header("Research")]
        public CostBlock cost = new CostBlock();
        public float researchTime;
    }

    /// <summary>One level of a building's ladder.</summary>
    [Serializable]
    public class BuildingLevel
    {
        [Tooltip("0 = base / pre-culture, 1..3 = upgraded forms.")]
        public int level;
        [Tooltip("Display/variant name at this level (e.g. 'Practice Range').")]
        public string variantName;
        [Tooltip("Culture this level belongs to (e.g. 'Alanthor'). Empty = pre-culture.")]
        public string culture;
        [Tooltip("Passive: units train this percent faster at this level (10 = +10%).")]
        public float trainSpeedBonusPct;
        [Tooltip("Unit ids trainable at this level.")]
        public string[] trains;
        [Tooltip("UnitUpgrade ids available to research at this level (ids into BuildingDefSO.unitUpgrades).")]
        public string[] availableUpgrades;
        [Tooltip("This level's building ranged attack (enabled=false = none).")]
        public BuildingAttack attack = new BuildingAttack();
    }
}
