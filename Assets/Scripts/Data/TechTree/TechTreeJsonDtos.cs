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
using System.Collections.Generic;
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
        public float healsPerSecond;
        public float attackCooldown;
        public int minBuildingLevel;
        public string trajectory;
        public float projectileSpeed;

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
                healsPerSecond = healsPerSecond,
                attackCooldown = attackCooldown,
                minBuildingLevel = minBuildingLevel,
                trajectory     = trajectory ?? "",
                projectileSpeed = projectileSpeed,
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
        public TechEffectEntryJson[] effectsList;
        public string culture;               // "Alanthor" etc.; empty = all cultures
        public int minBuildingLevel;         // gate on the research host's BuildingUpgradeState.Level

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
                culture       = culture ?? "",
                minBuildingLevel = minBuildingLevel,
                effectsList   = ToEffectsList(effectsList),
            };
            return tech;
        }

        static List<TechEffectEntry> ToEffectsList(TechEffectEntryJson[] entries)
        {
            if (entries == null || entries.Length == 0) return null;
            var list = new List<TechEffectEntry>(entries.Length);
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.target) || string.IsNullOrEmpty(e.stat)) continue;
                list.Add(new TechEffectEntry
                {
                    Target = e.target,
                    Stat   = e.stat,
                    Op     = string.IsNullOrEmpty(e.op) ? "Add" : e.op,
                    Value  = e.value,
                });
            }
            return list.Count > 0 ? list : null;
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
        public int Veilstone;
        public int Veilsteel;
        // Glow is an item/pickup, not a build cost — intentionally not parsed.

        public CostBlock ToBlock() => new CostBlock
        {
            Supplies  = Supplies,
            Iron      = Iron,
            Veilstone   = Veilstone,
            Veilsteel = Veilsteel,
        };
    }

    /// <summary>
    /// One generic target/op/stat effect entry (calculator model, Wave 2).
    /// Mirrors the JSON "effectsList" element shape exactly for JsonUtility.
    /// </summary>
    [Serializable]
    internal class TechEffectEntryJson
    {
        public string target;    // "type:Melee" | "type:Ranged" | "type:Cavalry" | "type:Siege" | "unit:<Id>"
        public string stat;      // "Hp" | "Damage" | "Speed" | "DefenseAll" | "AttackRange" | "AttackCooldown" | "LineOfSight"
        public string op;        // "Add" | "Pct"
        public float value;
    }

    [Serializable]
    internal class TechEffectsJson
    {
        public float gatherSpeedMult;
        public float meleeAttackSpeedMult;
        public int meleeDefenseAdd;
        public int meleeDamageAdd;
        public int rangedDamageAdd;
        public float archerRangeMult;

        public TechEffects ToEffects()
        {
            var e = new TechEffects
            {
                gatherSpeedMult       = gatherSpeedMult,
                meleeAttackSpeedMult  = meleeAttackSpeedMult,
                meleeDefenseAdd       = meleeDefenseAdd,
                meleeDamageAdd        = meleeDamageAdd,
                rangedDamageAdd       = rangedDamageAdd,
                archerRangeMult       = archerRangeMult,
            };
            return e.HasAnyEffect ? e : null;
        }
    }
}
