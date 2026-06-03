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

    /// <summary>A researchable upgrade that modifies unit stats and/or unlocks an ability.</summary>
    [Serializable]
    public class UnitUpgrade
    {
        public string id;
        public string displayName;
        [TextArea(1, 2)] public string description;
        [Tooltip("Upgrade id that must be researched first (chain). Empty = no prerequisite.")]
        public string requires;
        [Tooltip("Unit ids this upgrade modifies.")]
        public string[] appliesTo;

        [Header("Stat deltas (added to the unit)")]
        public float addHp;
        public float addLineOfSight;
        public float addAttackRange;
        public float addDamage;
        [Tooltip("Rate-of-fire bonus, percent (30 = +30%).")]
        public float rateOfFireBonusPct;
        [Tooltip("Ability id unlocked on affected units (e.g. DeployStakes). Empty = none.")]
        public string unlocksAbility;

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
