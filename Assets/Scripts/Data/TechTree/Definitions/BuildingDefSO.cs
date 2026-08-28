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
        [Header("Identity")]
        public string id;
        public string displayName;
        [Tooltip("production | military | economic | defensive")]
        public string role;
        [Tooltip("Human-readable description (UI tooltips / design reference).")]
        [TextArea(2, 5)]
        public string description;

        [Header("Core Stats")]
        public float hp = 1000f;
        [Tooltip("structure_human | structure_feraldis | ...")]
        public string armorType = "structure_human";
        public DefenseBlock defense = new DefenseBlock();

        [Header("Spatial")]
        [Tooltip("Building footprint radius")]
        public float radius = 1.6f;
        [Tooltip("Vision range")]
        public float lineOfSight = 20f;

        [Header("Construction")]
        [Tooltip("Seconds to construct. 0 = the construction system's own default.")]
        public float buildTime;

        [Header("Population & Income")]
        [Tooltip("Population headroom this building grants its owner (Hall 20, Hut 10).")]
        public int populationProvided;
        [Tooltip("Supplies credited to the owner every Supplies Interval seconds.")]
        public float suppliesPerTick;
        [Tooltip("Seconds between supply ticks. 0 = generates no supplies.")]
        public float suppliesInterval;

        [Header("Storage (Smelter)")]
        public int maxIron;
        public int maxVeilstone;

        [Header("Curtain Segments (Alanthor wall only)")]
        [Tooltip("HP of one curtain segment between two hubs. The hub itself uses HP above.")]
        public float segmentHp;
        [Tooltip("Line of sight of one curtain segment.")]
        public float segmentLineOfSight;

        [Header("Capabilities")]
        [Tooltip("Unit IDs this building can train")]
        public string[] trains;
        [Tooltip("Technology IDs this building can research")]
        public string[] research;

        [Header("Era Gating")]
        [Tooltip("Minimum era required to build (0 = no restriction)")]
        public int minEra;

        [Header("Economy")]
        public CostBlock cost = new CostBlock();

        [Header("Tags (AoE4-style — targets for bonus damage)")]
        [Tooltip("Tags this building HAS — e.g. Building. Siege units' 'bonus vs Building' matches these.")]
        public string[] tags = new[] { "Building" };

        [Header("Building Attack (ranged auto-fire)")]
        [Tooltip("The building's own attack. For a leveled building the per-level attack overrides this.")]
        public BuildingAttack attack = new BuildingAttack();

        [Header("Level Ladder")]
        [Tooltip("Per-level trains / available upgrades / attack. Empty = single-level building.")]
        public List<BuildingLevel> levels = new List<BuildingLevel>();

        [Header("Unit Upgrade Pool")]
        [Tooltip("Upgrade defs (stat deltas) referenced by level.availableUpgrades.")]
        public List<UnitUpgrade> unitUpgrades = new List<UnitUpgrade>();

        [Header("Authoring / Presentation")]
        [Tooltip("Visual prefab for this building (kept in this entity's GameData folder). " +
                 "Null = cube placeholder at runtime.")]
        public GameObject prefab;
        [Tooltip("The ECS PresentationId this building spawns with — links the runtime entity " +
                 "to this SO/prefab (see BuildingFactory.GetPresentationId).")]
        public int presentationId;
        [Tooltip("(Legacy) string path to the prefab; superseded by the prefab ref above.")]
        public string prefabPath;
        [Tooltip("Buildings this can upgrade / transform into (e.g. the three cultured forms at " +
                 "age-up: Alanthor / Runai / Feraldis). Empty = none.")]
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
