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
        public string damageType = "ranged";
        public float range = 22f;
        public float cooldown = 1.5f;
        public int maxTargets = 1;
    }

    /// <summary>
    /// AoE4-style bonus damage vs a target tag. Added after flat armor subtraction and
    /// ignores armor (e.g. a Ballista with +30 vs "Building").
    /// </summary>
    [Serializable]
    public class DamageBonus
    {
        public string vsTag;
        public float amount;
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
        public UpgradeEffectKind kind = UpgradeEffectKind.BuffStat;

        public string unit;

        public UnitStat stat;
        public float amount;

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
        public string requires;

        public List<UpgradeEffect> effects = new List<UpgradeEffect>();

        public CostBlock cost = new CostBlock();
        public float researchTime;
    }

    /// <summary>One level of a building's ladder.</summary>
    [Serializable]
    public class BuildingLevel
    {
        public int level;
        public string variantName;
        public string culture;
        public float trainSpeedBonusPct;
        public string[] trains;
        public string[] availableUpgrades;
        public BuildingAttack attack = new BuildingAttack();
    }
}
