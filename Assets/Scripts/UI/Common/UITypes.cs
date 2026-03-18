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
    
    // Resource generation (for buildings)
    public bool HasResourceGeneration;
    public float? SuppliesPerMinute;
    public int? IronPerMinute;
    public int? CrystalPerMinute;
    public int? VeilsteelPerMinute;
    public int? GlowPerMinute;

    // Miner info
    public bool HasMinerInfo;
    public int MinerCurrentLoad;
    public int MinerMaxCarry;
    public string MinerResourceType;      // "Iron" or "Crystal"
    public string MinerExtractionRate;    // e.g. "10 iron / 2s" or "1 crystal / 1.5s"
    public string MinerState;             // e.g. "Idle", "Gathering", "Returning"

    // Resource deposit info (iron mines, cadavers)
    public bool HasResourceInfo;
    public int ResourceRemaining;
    public int ResourceMax;
    public string ResourceTypeName;       // "Iron" or "Crystal"
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
        BattalionStance
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