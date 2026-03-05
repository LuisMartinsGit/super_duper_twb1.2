// TradeComponents.cs
// Components for the Runai trade route system
// Place in: Assets/Scripts/Core/Components/Trade/

using Unity.Entities;

// ==================== Trade Route (on TradeHub buildings) ====================

/// <summary>
/// Defines a trade route from a TradeHub to its nearest same-faction Outpost.
/// Attached to TradeHub entities after construction completes.
/// Route income is proportional to distance: longer routes = more profit.
/// </summary>
public struct TradeRoute : IComponentData
{
    /// <summary>The target Outpost entity for this route</summary>
    public Entity OutpostEntity;

    /// <summary>Distance between TradeHub and Outpost positions</summary>
    public float RouteLength;

    /// <summary>Number of caravans currently active on this route (max 3)</summary>
    public int ActiveCaravans;

    /// <summary>Countdown timer until next caravan spawns (resets to 22s)</summary>
    public float SpawnTimer;

    /// <summary>1 = valid route found, 0 = no same-faction Outpost available</summary>
    public byte RouteValid;
}

// ==================== Caravan Unit ====================

/// <summary>Marker tag for caravan units. Caravans are uncontrollable auto-trade units.</summary>
public struct CaravanTag : IComponentData { }

/// <summary>
/// State data for a caravan traveling a trade route.
/// Caravans shuttle between a TradeHub (origin) and Outpost (destination),
/// depositing Supplies on arrival at the Outpost.
/// </summary>
public struct CaravanState : IComponentData
{
    /// <summary>TradeHub entity where this caravan originates</summary>
    public Entity Origin;

    /// <summary>Outpost entity where this caravan delivers cargo</summary>
    public Entity Destination;

    /// <summary>Supplies currently being carried</summary>
    public float CurrentCargo;

    /// <summary>Max supplies per trip, calculated from route length + tariff</summary>
    public float MaxCargo;

    /// <summary>0 = traveling to Outpost, 1 = returning to TradeHub</summary>
    public byte IsReturning;

    /// <summary>The escort unit entity paired with this caravan</summary>
    public Entity EscortEntity;
}

// ==================== Caravan Escort Unit ====================

/// <summary>Marker tag for caravan escort units. Auto-follows and defends its caravan.</summary>
public struct CaravanEscortTag : IComponentData { }

/// <summary>
/// Links an escort unit to its caravan. Escort follows the caravan
/// and auto-attacks nearby enemies. Dies when its caravan dies.
/// </summary>
public struct CaravanEscort : IComponentData
{
    /// <summary>The caravan entity this escort is protecting</summary>
    public Entity CaravanEntity;
}

// ==================== Kill Credit Tracking ====================

/// <summary>
/// Tracks which faction last dealt damage to this entity.
/// Used by CaravanDeathSystem to credit the killer's faction with loot.
/// </summary>
public struct LastDamagedByFaction : IComponentData
{
    public Faction Value;
}
