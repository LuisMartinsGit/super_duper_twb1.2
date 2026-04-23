// TradeComponents.cs
// Shared components for the Runai trade system.
// Trade-network-specific components are in RunaiTradeComponents.cs.

using Unity.Entities;

// ==================== Trading Post (Building) ====================

/// <summary>Marker tag for Trading Post buildings. Runai-exclusive, max 10 per faction.</summary>
public struct TradingPostTag : IComponentData { }

// ==================== Trader Unit ====================

/// <summary>Marker tag for trader (caravan) units. Uncontrollable auto-trade units.</summary>
public struct CaravanTag : IComponentData { }

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

// ==================== Caravan Follower ====================

/// <summary>Marker tag for patrol units that follow caravans instead of fixed waypoints.</summary>
public struct CaravanFollowerTag : IComponentData { }

// ==================== Not Controllable ====================

/// <summary>
/// Marker tag for auto-controlled units (caravans, trade patrols) that ignore player orders.
/// CommandRouter checks this to block LocalPlayer commands.
/// </summary>
public struct NotControllableTag : IComponentData { }

// ==================== Trade Patrol Unit ====================

/// <summary>
/// Links a patrol unit to its lane's two endpoints.
/// Patrol units are uncontrollable and walk between trade network nodes.
/// </summary>
public struct TradePatrolData : IComponentData
{
    /// <summary>First trade node endpoint.</summary>
    public Entity PostA;

    /// <summary>Second trade node endpoint.</summary>
    public Entity PostB;
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
