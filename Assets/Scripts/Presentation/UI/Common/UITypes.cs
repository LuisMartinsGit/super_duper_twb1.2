using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Core;

namespace TheWaningBorder.UI
{
/// <summary>
/// Display information for an entity in the UI.
/// </summary>

public struct EntityDisplayInfo
{
    // Identity
    public string Name;
    public string Type;
    public string Description;
    public Texture2D Portrait;
    public string Faction;
    
    // Health (nullable - not all entities have health)
    public int? CurrentHealth;
    public int? MaxHealth;
    
    // Combat stats (nullable)
    public bool HasCombatStats;
    public int? Attack;
    public int? Defense;
    public float? Speed;

    // Per-type defense breakdown (nullable — present when the entity has a
    // Defense component). Defense above stays the melee value for the
    // legacy single-cell consumers (web HUD Selection.jsx).
    public int? DefenseMelee;
    public int? DefenseRanged;
    public int? DefenseSiege;
    public int? DefenseMagic;

    // Extended combat detail for the selection stats panel (2026-07-18).
    public float? AttackCooldown;   // seconds between attacks
    public float? RangeMin;         // null for melee attackers
    public float? RangeMax;         // null for melee attackers
    public string DamageTypeName;   // Melee / Ranged / Siege / Magic / True; null when no attack
    public string ArmorTypeName;    // readable ArmorType; "Structure" for buildings without one
    public string BonusVsText;      // "+15 vs Cavalry, +10 vs Building"; null when none
    public float? SightRadius;      // LineOfSight.Radius
    
    // Resource generation (for buildings)
    public bool HasResourceGeneration;
    public float? SuppliesPerMinute;
    public int? IronPerMinute;
    public int? VeilstonePerMinute;
    public int? VeilsteelPerMinute;
    public int? GlowPerMinute;

    /// <summary>
    /// Name of the territory a Hall claims, when this entity is one. The Hall
    /// is where a territory states what it yields (docs/Design/Regions.md §4),
    /// and the yield fields above carry those numbers; this labels them so the
    /// player knows WHICH ground they are reading. Null for everything else.
    /// </summary>
    public string TerritoryName;

    /// <summary>
    /// The unit's Power number — combat output per resource invested, ~100 for
    /// a par unit (docs/Design/Unit_Power.md). Derived entirely from stats the
    /// unit already has, so it can never disagree with the rest of this panel.
    /// Null for buildings and for units the metric does not measure (a Scout, a
    /// Ledger: no combat or support output at all).
    /// </summary>
    public float? PowerRating;

    // Miner info
    public bool HasMinerInfo;
    public string MinerResourceType;      // "Iron" or "Veilstone"
    public string MinerExtractionRate;    // e.g. "1 iron / 2s" or "1 veilstone / 1.5s"
    public string MinerState;             // e.g. "Idle", "Gathering"

    // Resource deposit info (iron mines, outcroppings)
    public bool HasResourceInfo;
    public int ResourceRemaining;
    public int ResourceMax;
    public string ResourceTypeName;       // "Iron" or "Veilstone"

    // ─── task-108 phase 1 additions ────────────────────────────────────────
    /// <summary>
    /// Coarse classification — "unit", "building", or "resource". Drives JSX
    /// conditional rendering (collapse the speed cell for buildings, route
    /// resource nodes to the depletion bar, etc.). Falls back to "unit" if
    /// the extractor can't tag the entity.
    /// </summary>
    public string EntityKind;

    /// <summary>
    /// Per-minute supplies yield for buildings carrying SuppliesIncome
    /// (Hall trickle, GathererHut yield). Null for entities without a
    /// SuppliesIncome component or for non-buildings.
    /// </summary>
    public float? YieldPerMinute;

    /// <summary>
    /// Capacity of the training queue (matches CommandRouter.MaxProductionQueue).
    /// Null when the entity has no TrainingState.
    /// </summary>
    public int? QueueCapacity;

    /// <summary>
    /// Snapshot of the training queue. Always exactly <see cref="QueueCapacity"/>
    /// long when populated, with empty trailing slots marked Populated=false.
    /// Null when the entity has no TrainingState.
    /// </summary>
    public EntityQueueSlot[] Queue;
}

/// <summary>
/// One slot in a building's training queue snapshot (task-108 phase 1).
/// Carries the unit id, the refund cost (full unit cost, mirroring the
/// existing IMGUI CancelQueueItem behaviour), and per-slot progress data
/// — Progress and IsInProduction are only meaningful for slot 0 when the
/// building's TrainingState.Busy == 1.
/// </summary>
public struct EntityQueueSlot
{
    public bool Populated;
    public string UnitId;
    public string DisplayName;
    public int RefundSupplies;
    public int RefundIron;
    public int RefundVeilstone;
    public int RefundVeilsteel;
    public int RefundGlow;
    public float Progress;
    public bool IsInProduction;
}

    /// <summary>
    /// Action panel information for an entity.
    /// </summary>
    public struct EntityActionInfo
    {
        public ActionType Type;
        public List<ActionButton> Actions;
        public TrainingInfo? TrainingState;
        public ResearchInfo? ResearchState;
    }

    /// <summary>
    /// Types of action panels.
    /// </summary>
    public enum ActionType
    {
        None,
        BuildingPlacement,
        UnitTraining,
        VaultManagement,
        UnitTrainingAndResearch,
        TempleUpgrade,
        WallInstanceUpgrade,
        BazaarWagonUnpack,
        // task-109 phase 2 — per-hut age-up choice (Wall Hub / Watch Tower).
        // Surfaces two large ActionButton items on Alanthor-owned Gatherer's
        // Huts tagged with GathererHutAgeUpChoice. While the hut is mid-
        // conversion (GathererHutConverting present) the same ActionType is
        // emitted with no actions and a progress payload on EntityActionInfo.
        GathererHutAgeUpChoice,
        /// <summary>
        /// Per-hub "Build Wall" action surfaced on a completed wall hub of
        /// the local faction. Clicking enters a hub-anchored placement mode
        /// (BuilderCommandPanel.TriggerHubBuildWall) that drops a new hub +
        /// auto-connecting segment, both self-constructing in 30 s with no
        /// builder dispatch. The first hub itself is still placed via a
        /// builder using the regular BuildingPlacement path.
        /// </summary>
        HubBuildWall
    }

    /// <summary>
    /// A button in the action panel.
    /// </summary>
    public struct ActionButton
    {
        public string Id;
        public string Label;
        public string Tooltip;
        public Cost Cost;
        public bool Enabled;
        public Texture2D Icon;
        public bool CanAfford;  // ADD THIS
    }

    /// <summary>
    /// Training queue information.
    /// </summary>
public struct TrainingInfo
{
    public string UnitId;
    public float Progress;
    public float Total;
    public int QueuePosition;
    public string CurrentUnitId;
    public float TimeRemaining;
    public string[] Queue;           // Queue of unit IDs (excludes currently training)
    public int QueueCapacity;        // Total items in buffer (including currently training)

    // Computed property for convenience
        public bool IsTraining;       // Set when constructing the struct
}

    /// <summary>
    /// Research queue information for the action panel.
    /// </summary>
    public struct ResearchInfo
    {
        public string CurrentTechId;
        public string CurrentTechName;
        public float Progress;         // 0..1
        public float Total;
        public float TimeRemaining;
        public string[] Queue;
        public bool IsResearching;
    }

}