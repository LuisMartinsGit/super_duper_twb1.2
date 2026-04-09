// TechTreeJsonDtos.cs
// Intermediate serializable DTOs that mirror TechTree.json field names exactly.
// Used by TechTreeDB as deserialization targets for JsonUtility, then converted
// to the strongly-typed UnitDef / BuildingDef / TechnologyDef / SectDef types.
//
// Fix #220: the previous TechTreeDB did hand-rolled string parsing (IndexOf /
// Substring / FindMatchingBrace / per-field ParseString / ParseFloat etc.),
// 847 lines total, brittle against any JSON whitespace or nesting change.
// This file is the shim that lets us delegate field-level parsing to
// UnityEngine.JsonUtility while keeping the ID-indexed lookup API.
//
// Why not just mark the existing UnitDef/BuildingDef/TechnologyDef as the
// deserialization targets directly? Three field-name mismatches prevent it:
//   1. UnitDef.unitClass   vs JSON "class"         (C# reserved word)
//   2. BuildingDef.defense vs JSON "baseDefense"   (different key for buildings)
//   3. TechnologyDef.prerequisites vs JSON "requires"
// The DTOs here match the JSON names; converters below map to the runtime types.

using System;
using UnityEngine;

namespace TheWaningBorder.Data
{
    // ═══════════════════════════════════════════════════════════════════════
    // ROOT + GLOBALS
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    internal class TechTreeRootJson
    {
        public string faction;
        public string[] resources;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BUILDING
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    internal class BuildingJson
    {
        public string id;
        public string name;
        public string role;
        public float hp;
        public string armorType;
        public DefenseJson baseDefense;      // JSON uses "baseDefense" for buildings
        public float lineOfSight;
        public float radius;
        public string[] trains;
        public string[] research;
        public CostJson cost;
        public int minEra;

        public BuildingDef ToDef(string overrideId = null)
        {
            return new BuildingDef
            {
                id          = overrideId ?? id,
                name        = string.IsNullOrEmpty(name) ? (overrideId ?? id) : name,
                role        = role ?? "",
                hp          = hp > 0 ? hp : 1000f,
                armorType   = string.IsNullOrEmpty(armorType) ? "structure_human" : armorType,
                lineOfSight = lineOfSight > 0 ? lineOfSight : 20f,
                radius      = radius > 0 ? radius : 1.6f,
                defense     = baseDefense != null ? baseDefense.ToBlock() : new DefenseBlock(),
                trains      = trains ?? Array.Empty<string>(),
                research    = research ?? Array.Empty<string>(),
                cost        = cost != null ? cost.ToBlock() : new CostBlock(),
                minEra      = minEra,
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UNIT
    // ═══════════════════════════════════════════════════════════════════════
    //
    // NOTE: the JSON field "class" is handled by pre-processing the slice
    // before deserialization (rename "class": -> "unitClass":) so the DTO
    // below can use a normal C# identifier.

    [Serializable]
    internal class UnitJson
    {
        public string id;
        public string name;
        public string unitClass;             // populated by pre-processing from "class"
        public float hp;
        public float speed;
        public float trainingTime;
        public float damage;
        public float attackRange;
        public float minAttackRange;
        public float lineOfSight;
        public string armorType;
        public string damageType;
        public DefenseJson defense;
        public CostJson cost;
        public float buildSpeed;
        public float gatheringSpeed;
        public int carryCapacity;
        public float healsPerSecond;
        public float attackCooldown;

        public UnitDef ToDef(string overrideId = null, string overrideName = null,
            float defaultHp = 100, float defaultSpeed = 5, float defaultDamage = 10,
            float defaultAttackRange = 1.5f, float defaultMinRange = 0,
            float defaultLoS = 20, float defaultTrainingTime = 5,
            string defaultArmorType = "infantry", string defaultDamageType = "melee")
        {
            return new UnitDef
            {
                id             = overrideId ?? id,
                name           = overrideName ?? (string.IsNullOrEmpty(name) ? (overrideId ?? id) : name),
                unitClass      = unitClass ?? "",
                hp             = hp > 0 ? hp : defaultHp,
                speed          = speed > 0 ? speed : defaultSpeed,
                trainingTime   = trainingTime > 0 ? trainingTime : defaultTrainingTime,
                damage         = damage > 0 ? damage : defaultDamage,
                attackRange    = attackRange > 0 ? attackRange : defaultAttackRange,
                minAttackRange = minAttackRange > 0 ? minAttackRange : defaultMinRange,
                lineOfSight    = lineOfSight > 0 ? lineOfSight : defaultLoS,
                armorType      = string.IsNullOrEmpty(armorType) ? defaultArmorType : armorType,
                damageType     = string.IsNullOrEmpty(damageType) ? defaultDamageType : damageType,
                defense        = defense != null ? defense.ToBlock() : new DefenseBlock(),
                cost           = cost != null ? cost.ToBlock() : new CostBlock(),
                buildSpeed     = buildSpeed,
                gatheringSpeed = gatheringSpeed,
                carryCapacity  = carryCapacity,
                healsPerSecond = healsPerSecond,
                attackCooldown = attackCooldown,
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TECHNOLOGY
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    internal class TechnologyJson
    {
        public string id;
        public string name;
        public string effect;
        public string desc;
        public string role;
        public float researchTime;
        public string researchAt;
        public CostJson cost;
        public string[] requires;            // JSON key is "requires" (not "prerequisites")
        public TechEffectsJson effects;

        public TechnologyDef ToDef(string overrideId = null, float defaultResearchTime = 30)
        {
            var tech = new TechnologyDef
            {
                id            = overrideId ?? id,
                name          = string.IsNullOrEmpty(name) ? (overrideId ?? id) : name,
                effect        = effect ?? "",
                desc          = desc ?? "",
                role          = role ?? "",
                researchTime  = researchTime > 0 ? researchTime : defaultResearchTime,
                researchAt    = researchAt ?? "",
                cost          = cost != null ? cost.ToBlock() : new CostBlock(),
                prerequisites = requires ?? Array.Empty<string>(),
                effects       = effects != null ? effects.ToEffects() : null,
            };
            return tech;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SECT
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    internal class SectJson
    {
        public string id;
        public string order;
        public string affinity;
        public UnitJson unit;
        public TechnologyJson tech;

        public SectDef ToDef()
        {
            return new SectDef
            {
                id       = id ?? "",
                order    = order ?? "",
                affinity = affinity ?? "",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SUB-BLOCKS
    // ═══════════════════════════════════════════════════════════════════════

    [Serializable]
    internal class DefenseJson
    {
        public int melee;
        public int ranged;
        public int siege;
        public int magic;

        public DefenseBlock ToBlock() => new DefenseBlock
        {
            melee  = melee,
            ranged = ranged,
            siege  = siege,
            magic  = magic,
        };
    }

    [Serializable]
    internal class CostJson
    {
        public int Supplies;
        public int Iron;
        public int Crystal;
        public int Veilsteel;
        public int Glow;

        public CostBlock ToBlock() => new CostBlock
        {
            Supplies  = Supplies,
            Iron      = Iron,
            Crystal   = Crystal,
            Veilsteel = Veilsteel,
            Glow      = Glow,
        };
    }

    [Serializable]
    internal class TechEffectsJson
    {
        public float gatherSpeedMult;
        public int carryCapacityBonus;
        public float meleeAttackSpeedMult;
        public int meleeDefenseAdd;

        public TechEffects ToEffects()
        {
            var e = new TechEffects
            {
                gatherSpeedMult       = gatherSpeedMult,
                carryCapacityBonus    = carryCapacityBonus,
                meleeAttackSpeedMult  = meleeAttackSpeedMult,
                meleeDefenseAdd       = meleeDefenseAdd,
            };
            return e.HasAnyEffect ? e : null;
        }
    }
}
