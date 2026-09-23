// BuildingDefSO.cs
// ScriptableObject authoring asset for a building's base stats.
// Part of: Data/TechTree/Definitions/
//
// Editable, on-the-fly tuning source for building stats (see UnitDefSO for the
// full rationale). One .asset per building under Assets/GameData/TechTree/Buildings/,
// referenced by the TechTreeCatalog, projected into the runtime BuildingDef by
// TechTreeDB.
//
// NOTE: "name" is renamed "displayName" (ScriptableObject already defines `name`).

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    [CreateAssetMenu(fileName = "Building_", menuName = "Waning Border/Building Def", order = 1)]
    public class BuildingDefSO : ScriptableObject
    {
        public string id;
        public string displayName;
        public string role;
        [TextArea(2, 5)]
        public string description;

        public float hp = 1000f;
        public string armorType = "structure_human";
        public DefenseBlock defense = new DefenseBlock();

        public float radius = 1.6f;
        public float lineOfSight = 20f;

        public float buildTime;

        public int populationProvided;
        public float suppliesPerTick;
        public float suppliesInterval;

        public int maxIron;
        public int maxVeilstone;

        public float segmentHp;
        public float segmentLineOfSight;

        public string[] trains;
        public string[] research;

        public int minEra;

        public CostBlock cost = new CostBlock();

        public string[] tags = new[] { "Building" };

        public BuildingAttack attack = new BuildingAttack();

        public List<BuildingLevel> levels = new List<BuildingLevel>();

        public List<UnitUpgrade> unitUpgrades = new List<UnitUpgrade>();

        public GameObject prefab;
        public int presentationId;
        public string prefabPath;
        public string[] canUpgradeTo;


        /// <summary>Build a fresh runtime BuildingDef from this asset.</summary>
        public BuildingDef ToDef()
        {
            var def = new BuildingDef();
            ApplyTo(def);
            return def;
        }

        /// <summary>Copy this asset's fields into an existing BuildingDef in place (no allocation).</summary>
        public void ApplyTo(BuildingDef def)
        {
            def.id          = id;
            def.name        = string.IsNullOrEmpty(displayName) ? id : displayName;
            def.role        = role ?? "";
            def.description  = description ?? "";
            def.prefabPath   = prefabPath ?? "";
            def.canUpgradeTo = CloneArray(canUpgradeTo);
            def.tags         = tags == null ? System.Array.Empty<string>() : (string[])tags.Clone();
            // Authoring data is read-only at runtime, so reference-copy (no deep clone).
            def.attack       = attack;
            def.levels       = levels;
            def.unitUpgrades = unitUpgrades;
            def.hp          = hp;
            def.armorType   = string.IsNullOrEmpty(armorType) ? "structure_human" : armorType;
            def.defense     = UnitDefSO.CloneDefense(defense);
            def.radius      = radius;
            def.lineOfSight = lineOfSight;
            def.buildTime   = buildTime;
            def.populationProvided = populationProvided;
            def.suppliesPerTick    = suppliesPerTick;
            def.suppliesInterval   = suppliesInterval;
            def.maxIron            = maxIron;
            def.maxVeilstone       = maxVeilstone;
            def.segmentHp          = segmentHp;
            def.segmentLineOfSight = segmentLineOfSight;
            def.trains      = CloneArray(trains);
            def.research    = CloneArray(research);
            def.minEra      = minEra;
            def.cost        = UnitDefSO.CloneCost(cost);
        }

        /// <summary>Populate this asset's fields from a runtime BuildingDef (used by the generator).</summary>
        public void CopyFrom(BuildingDef def)
        {
            id          = def.id;
            displayName = def.name;
            role        = def.role;
            description  = def.description;
            prefabPath   = def.prefabPath;
            canUpgradeTo = CloneArray(def.canUpgradeTo);
            tags         = def.tags == null ? new[] { "Building" } : (string[])def.tags.Clone();
            attack       = def.attack ?? new BuildingAttack();
            levels       = def.levels ?? new List<BuildingLevel>();
            unitUpgrades = def.unitUpgrades ?? new List<UnitUpgrade>();
            hp          = def.hp;
            armorType   = def.armorType;
            defense     = UnitDefSO.CloneDefense(def.defense);
            radius      = def.radius;
            lineOfSight = def.lineOfSight;
            buildTime   = def.buildTime;
            populationProvided = def.populationProvided;
            suppliesPerTick    = def.suppliesPerTick;
            suppliesInterval   = def.suppliesInterval;
            maxIron            = def.maxIron;
            maxVeilstone       = def.maxVeilstone;
            segmentHp          = def.segmentHp;
            segmentLineOfSight = def.segmentLineOfSight;
            trains      = CloneArray(def.trains);
            research    = CloneArray(def.research);
            minEra      = def.minEra;
            cost        = UnitDefSO.CloneCost(def.cost);
        }

        static string[] CloneArray(string[] a) =>
            a == null ? System.Array.Empty<string>() : (string[])a.Clone();
    }
}
