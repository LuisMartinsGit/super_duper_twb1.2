// TradeComponents.cs
// Components for the Runai trading post chain system
// Place in: Assets/Scripts/Core/Components/Trade/

using Unity.Entities;

// ==================== Trading Post (Building) ====================

/// <summary>Marker tag for Trading Post buildings. Runai-exclusive, max 10 per faction.</summary>
public struct TradingPostTag : IComponentData { }

/// <summary>
/// Data for a trading post in the sequential chain.
/// Posts are numbered in build order. Traders traverse the chain 1→2→...→N→reverse.
/// </summary>
public struct TradingPostData : IComponentData
{
    /// <summary>1-based sequential number assigned at construction. Gaps stay on destruction.</summary>
    public int PostNumber;
}

// ==================== Trade Lane (on lower-numbered post of each pair) ====================

/// <summary>
/// Tracks the lane between this post and the next-higher-numbered post.
/// Manages trader spawning and patrol unit count for this lane segment.
/// </summary>
public struct TradeLane : IComponentData
{
    /// <summary>The trading post with the next-higher PostNumber</summary>
    public Entity NextPost;

    /// <summary>Number of active traders on this lane (max 2)</summary>
    public int ActiveTraders;

    /// <summary>Countdown timer for spawning the 2nd trader (starts at 240s)</summary>
    public float SecondTraderTimer;

    /// <summary>Number of patrol units spawned for this lane (target: 5)</summary>
    public int PatrolUnitsSpawned;

    /// <summary>1 = NextPost still exists, 0 = invalid</summary>
    public byte LaneValid;
}

// ==================== Trader Unit ====================

/// <summary>Marker tag for trader (caravan) units. Uncontrollable auto-trade units.</summary>
public struct CaravanTag : IComponentData { }

/// <summary>
/// State for a trader traversing the trading post chain.
/// Traders travel Post 1→2→...→N then reverse N→...→2→1, depositing cargo at each stop.
/// </summary>
public struct TraderState : IComponentData
{
    /// <summary>The trading post the trader is currently heading toward</summary>
    public Entity CurrentDestPost;

    /// <summary>Supplies currently being carried</summary>
    public float CurrentCargo;

    /// <summary>Max supplies per leg, calculated from distance between posts</summary>
    public float MaxCargo;

    /// <summary>1 = ascending post numbers (1→2→3), 0 = descending (3→2→1)</summary>
    public byte IsForward;

    /// <summary>PostNumber of the destination post (for quick lookup)</summary>
    public int DestPostNumber;

    /// <summary>The lane entity this trader belongs to (for ActiveTraders bookkeeping)</summary>
    public Entity OwnerLanePost;
}

// ==================== Trade Patrol Unit ====================

/// <summary>
/// Links a patrol unit to its lane's two endpoints.
/// Patrol units are uncontrollable and walk between the two posts.
/// </summary>
public struct TradePatrolData : IComponentData
{
    /// <summary>Lower-numbered trading post of the lane</summary>
    public Entity PostA;

    /// <summary>Higher-numbered trading post of the lane</summary>
    public Entity PostB;
}

// ==================== Trade Upgrades (stub for future) ====================

/// <summary>
/// Faction-wide trade upgrades. Stub component for future implementation.
/// </summary>
public struct TradeUpgrades : IComponentData
{
    /// <summary>0-3, each level +15% trade income</summary>
    public byte IncomeBoost;

    /// <summary>0-3, each level +30 HP and +0.5 speed to traders</summary>
    public byte TraderToughness;

    /// <summary>0 or 1, enables arrows on trading posts and armed traders</summary>
    public byte ArmedTrade;
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
