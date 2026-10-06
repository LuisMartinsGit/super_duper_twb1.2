// BuildingDef.cs
// Runtime building definition, projected from its BuildingDefSO
// Part of: Data/TechTree/Definitions/

using System;
using System.Collections.Generic;

namespace TheWaningBorder.Data
{
    /// <summary>
    /// Defines a building type's base stats, training capabilities, and research options.
    /// Projected from its SO by TechCatalog (the SO is the only source).
    /// </summary>
    [Serializable]
    public class BuildingDef
    {
        // ==================== Identity ====================
        public string id;
        public string name;
        public string role;             // e.g., "production", "military", "economic", "defensive"
        
        // ==================== Core Stats ====================
        public float hp;
        public string armorType;        // e.g., "structure_human", "structure_feraldis"
        public DefenseBlock defense;
        
        // ==================== Spatial ====================
        public float radius;            // building footprint radius
        public float lineOfSight;       // vision range
        /// <summary>Footprint in 2 m BUILD CELLS (x = width, y = depth),
        /// docs/Design/Build_Grid.md. BuildingSizeConfig reads it; it was that
        /// class's own id switch until 2026-10-03 (unification item 34).</summary>
        public UnityEngine.Vector2Int footprintCells;

        // ==================== Construction ====================
        /// <summary>Seconds to construct. 0 = the construction system's own default.</summary>
        public float buildTime;

        // ==================== Population & Income ====================
        /// <summary>Population headroom this building grants its owner (Hall 20, Hut 10).</summary>
        public int populationProvided;
        /// <summary>Supplies credited to the owner every <see cref="suppliesInterval"/> seconds.</summary>
        public float suppliesPerTick;
        /// <summary>Seconds between supply ticks. 0 = building generates no supplies.</summary>
        public float suppliesInterval;
        /// <summary>What a resource SLOT pays per minute with this building on
        /// it, by level (index 0 = level 1). Only the extractors (Gatherer's
        /// Hut, Mine, Veilstone Mine) carry it; a culture's level SO may
        /// override a rung (BuildingLevelDefSO.slotIncomePerMinute).
        /// TerritoryIncomeSystem reads it.</summary>
        public float[] slotIncomePerMinute;
        /// <summary>SIMPLE interest the stored PRINCIPAL earns per minute
        /// (0.05 = 5 %), paid on at most <see cref="interestPrincipalCap"/> of
        /// it. The Vault of Almierra only; its levels scale the rate
        /// (BuildingLevelDefSO.interestMultiplier) and the banking techs
        /// replace it (<see cref="coffersRate"/> and siblings).
        /// VaultInterestSystem reads them. Simple, capped interest since
        /// 2026-10-05 (decision 41) — compounding was a money printer.</summary>
        public float interestPerMinute;
        /// <summary>The most stored principal that earns interest. Anything
        /// stored above it earns nothing.</summary>
        public float interestPrincipalCap;
        /// <summary>Interest per minute once Coffers is researched (replaces
        /// <see cref="interestPerMinute"/>).</summary>
        public float coffersRate;
        /// <summary>Interest per minute once Merchant Charters is researched.</summary>
        public float merchantChartersRate;
        /// <summary>Interest per minute once Sovereign Bonds is researched.</summary>
        public float sovereignBondsRate;
        /// <summary>The stone-wall level (WallTiers) a wall must stand at for
        /// this building to be mounted on it. The emplacements only.</summary>
        public int minWallLevel;
        /// <summary>Foot units this building can hold (WallGarrisonSlot). The
        /// Watch Tower only.</summary>
        public int garrisonSlots;
        /// <summary>Extra targets per volley each garrisoned unit adds to the
        /// building's own attack.</summary>
        public int garrisonArrowsPerOccupant;
        /// <summary>Most a faction may own at once, plans and sites included.
        /// 0 = unlimited. Enforced by <c>BuildingFactory.AtFactionCap</c>.</summary>
        public int maxPerFaction;

        // ==================== Storage ====================
        public int maxIron;
        public int maxVeilstone;

        // ==================== Curtain Segments (Alanthor wall) ====================
        /// <summary>HP of one curtain segment between two hubs. The hub itself uses
        /// <see cref="hp"/>. Only the wall uses these; 0 elsewhere.</summary>
        public float segmentHp;
        /// <summary>Line of sight of one curtain segment.</summary>
        public float segmentLineOfSight;
        
        // ==================== Capabilities ====================
        public string[] trains;         // unit IDs this building can train
        public string[] research;       // technology IDs this building can research
        
        // ==================== Era Gating ====================
        /// <summary>Minimum era required to build (0 = no restriction)</summary>
        public int minEra;

        // ==================== Economy ====================
        public CostBlock cost;

        // ==================== Authoring / Presentation ====================
        /// <summary>Human-readable description (UI tooltips, design reference).</summary>
        public string description;
        /// <summary>Asset/Resources path to this building's prefab (authoring hook).</summary>
        public string prefabPath;
        /// <summary>Buildings this can upgrade / transform into (e.g. the three cultured forms
        /// at age-up: Alanthor / Runai / Feraldis). Empty = none.</summary>
        public string[] canUpgradeTo;

        /// <summary>Tags this building HAS (AoE4-style bonus-damage targets, e.g. "Building").</summary>
        public string[] tags;

        // ==================== Level Ladder / Attack ====================
        /// <summary>The building's own ranged auto-fire attack (base / non-leveled buildings).</summary>
        public BuildingAttack attack;
        /// <summary>Per-level config (trains / upgrades / attack). Empty = single-level building.</summary>
        public List<BuildingLevel> levels;
        /// <summary>Pool of unit-upgrade defs referenced by BuildingLevel.availableUpgrades.</summary>
        public List<UnitUpgrade> unitUpgrades;

        // ==================== Helpers ====================
        
        /// <summary>
        /// Returns true if this building can train units.
        /// </summary>
        public bool CanTrain => trains != null && trains.Length > 0;
        
        /// <summary>
        /// Returns true if this building can research technologies.
        /// </summary>
        public bool CanResearch => research != null && research.Length > 0;
        
        /// <summary>
        /// Returns true if this is a military production building.
        /// </summary>
        public bool IsMilitaryProduction => string.Equals(role, "military", StringComparison.OrdinalIgnoreCase) && CanTrain;
        
        /// <summary>
        /// Returns true if this is an economic building.
        /// </summary>
        public bool IsEconomic => string.Equals(role, "economic", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Returns true if this is a defensive structure.
        /// </summary>
        public bool IsDefensive => string.Equals(role, "defensive", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Check if this building can train a specific unit type.
        /// </summary>
        public bool CanTrainUnit(string unitId)
        {
            if (trains == null) return false;
            foreach (var id in trains)
            {
                if (string.Equals(id, unitId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Check if this building can research a specific technology.
        /// </summary>
        public bool CanResearchTech(string techId)
        {
            if (research == null) return false;
            foreach (var id in research)
            {
                if (string.Equals(id, techId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}