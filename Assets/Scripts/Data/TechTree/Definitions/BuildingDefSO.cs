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
        [Tooltip("Asset or Resources path to this building's prefab. Point this at the correct prefab.")]
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
            // Authoring data is read-only at runtime, so reference-copy (no deep clone).
            def.attack       = attack;
            def.levels       = levels;
            def.unitUpgrades = unitUpgrades;
            def.hp          = hp;
            def.armorType   = string.IsNullOrEmpty(armorType) ? "structure_human" : armorType;
            def.defense     = UnitDefSO.CloneDefense(defense);
            def.radius      = radius;
            def.lineOfSight = lineOfSight;
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
            attack       = def.attack ?? new BuildingAttack();
            levels       = def.levels ?? new List<BuildingLevel>();
            unitUpgrades = def.unitUpgrades ?? new List<UnitUpgrade>();
            hp          = def.hp;
            armorType   = def.armorType;
            defense     = UnitDefSO.CloneDefense(def.defense);
            radius      = def.radius;
            lineOfSight = def.lineOfSight;
            trains      = CloneArray(def.trains);
            research    = CloneArray(def.research);
            minEra      = def.minEra;
            cost        = UnitDefSO.CloneCost(def.cost);
        }

        static string[] CloneArray(string[] a) =>
            a == null ? System.Array.Empty<string>() : (string[])a.Clone();
    }
}
