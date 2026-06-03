// UnitDefSO.cs
// ScriptableObject authoring asset for a unit's base stats.
// Part of: Data/TechTree/Definitions/
//
// This is the editable, on-the-fly tuning source for unit stats. One .asset per
// unit lives under Assets/GameData/TechTree/Units/ and is referenced by the
// TechTreeCatalog. At load (and on each TryGetUnit while in catalog mode)
// TechTreeDB projects these fields into the runtime UnitDef the rest of the game
// already consumes, so editing a field in the Inspector — even during Play mode —
// changes the stats of the next-spawned unit, and the edit persists past Play mode.
//
// NOTE: fields mirror UnitDef exactly EXCEPT "name" is renamed "displayName" here,
// because ScriptableObject already defines a sealed `name` property.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    [CreateAssetMenu(fileName = "Unit_", menuName = "Waning Border/Unit Def", order = 0)]
    public class UnitDefSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [Tooltip("melee | ranged | support | siege | magic")]
        public string unitClass;

        [Header("Core Stats")]
        public float hp = 100f;
        public float speed = 5f;
        [Tooltip("Seconds to train")]
        public float trainingTime = 5f;

        [Header("Combat")]
        public float damage = 10f;
        [Tooltip("melee | ranged | siege | magic | true")]
        public string damageType = "melee";
        [Tooltip("infantry | infantry_heavy | ranged | cavalry | structure | structure_human")]
        public string armorType = "infantry";
        public DefenseBlock defense = new DefenseBlock();
        public float attackCooldown;

        [Header("Range & Vision")]
        public float attackRange = 1.5f;
        public float minAttackRange;
        public float lineOfSight = 20f;

        [Header("Economy")]
        public CostBlock cost = new CostBlock();

        [Header("Progression Gating")]
        [Tooltip("Minimum trainer-building level required (0/1 = available immediately)")]
        public int minBuildingLevel;

        [Header("Support Roles")]
        public float buildSpeed;
        public float gatheringSpeed;
        public int carryCapacity;
        public float healsPerSecond;

        [Header("Tags & Bonus Damage (AoE4-style)")]
        [Tooltip("Tags this unit HAS — others' bonus damage targets these (Infantry, Cavalry, " +
                 "Ranged, Siege, Heavy, Light, Building, ...).")]
        public string[] tags;
        [Tooltip("Flat bonus damage vs target tags (added after armor; ignores armor).")]
        public List<DamageBonus> bonusVsTags = new List<DamageBonus>();

        /// <summary>Build a fresh runtime UnitDef from this asset.</summary>
        public UnitDef ToDef()
        {
            var def = new UnitDef();
            ApplyTo(def);
            return def;
        }

        /// <summary>
        /// Copy this asset's fields into an existing UnitDef in place (no allocation).
        /// Used by TechTreeDB to refresh the cached def so live Inspector edits apply
        /// to the next-spawned unit.
        /// </summary>
        public void ApplyTo(UnitDef def)
        {
            def.id             = id;
            def.name           = string.IsNullOrEmpty(displayName) ? id : displayName;
            def.unitClass      = unitClass ?? "";
            def.hp             = hp;
            def.speed          = speed;
            def.trainingTime   = trainingTime;
            def.damage         = damage;
            def.damageType     = string.IsNullOrEmpty(damageType) ? "melee" : damageType;
            def.armorType      = string.IsNullOrEmpty(armorType) ? "infantry" : armorType;
            def.defense        = CloneDefense(defense);
            def.attackCooldown = attackCooldown;
            def.attackRange    = attackRange;
            def.minAttackRange = minAttackRange;
            def.lineOfSight    = lineOfSight;
            def.cost           = CloneCost(cost);
            def.minBuildingLevel = minBuildingLevel;
            def.buildSpeed     = buildSpeed;
            def.gatheringSpeed = gatheringSpeed;
            def.carryCapacity  = carryCapacity;
            def.healsPerSecond = healsPerSecond;
            def.tags           = tags == null ? System.Array.Empty<string>() : (string[])tags.Clone();
            def.bonusVsTags    = bonusVsTags;   // read-only at runtime -> reference copy
        }

        /// <summary>Populate this asset's fields from a runtime UnitDef (used by the generator).</summary>
        public void CopyFrom(UnitDef def)
        {
            id             = def.id;
            displayName    = def.name;
            unitClass      = def.unitClass;
            hp             = def.hp;
            speed          = def.speed;
            trainingTime   = def.trainingTime;
            damage         = def.damage;
            damageType     = def.damageType;
            armorType      = def.armorType;
            defense        = CloneDefense(def.defense);
            attackCooldown = def.attackCooldown;
            attackRange    = def.attackRange;
            minAttackRange = def.minAttackRange;
            lineOfSight    = def.lineOfSight;
            cost           = CloneCost(def.cost);
            minBuildingLevel = def.minBuildingLevel;
            buildSpeed     = def.buildSpeed;
            gatheringSpeed = def.gatheringSpeed;
            carryCapacity  = def.carryCapacity;
            healsPerSecond = def.healsPerSecond;
            tags           = def.tags == null ? System.Array.Empty<string>() : (string[])def.tags.Clone();
            bonusVsTags    = def.bonusVsTags ?? new List<DamageBonus>();
        }

        internal static DefenseBlock CloneDefense(DefenseBlock d) => d == null
            ? new DefenseBlock()
            : new DefenseBlock { melee = d.melee, ranged = d.ranged, siege = d.siege, magic = d.magic };

        internal static CostBlock CloneCost(CostBlock c) => c == null
            ? new CostBlock()
            : new CostBlock { Supplies = c.Supplies, Iron = c.Iron, Crystal = c.Crystal, Veilsteel = c.Veilsteel };
    }
}
