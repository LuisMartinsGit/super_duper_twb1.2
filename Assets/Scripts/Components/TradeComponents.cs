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

// ==================== Caravan Follower ====================

// ==================== Not Controllable ====================

/// <summary>
/// Marker tag for auto-controlled units (caravans, trade patrols) that ignore player orders.
/// CommandRouter checks this to block LocalPlayer commands.
/// </summary>
public struct NotControllableTag : IComponentData { }

// ==================== Trade Patrol Unit ====================

// ==================== Kill Credit Tracking ====================

/// <summary>
/// Tracks which faction last dealt damage to this entity.
/// Used by CaravanDeathSystem to credit the killer's faction with loot.
/// </summary>
public struct LastDamagedByFaction : IComponentData
{
    public Faction Value;
}

/// <summary>
/// Set on a unit when it takes damage. Records the attacking entity.
/// Read by the Sect kill-credit systems (Antiquity tally, Justice mark,
/// Veneration fervor), BorderMainNode, and the combat UI. Written by
/// CombatDamageHelper / ProjectileSystem on each hit; cleared by
/// TargetingSystem when the attacker no longer exists.
/// (Relocated here when the battalion system was removed.)
/// </summary>
public struct LastAttackerEntity : IComponentData
{
    public Entity Value;
}

/// <summary>
/// Running total of the damage this entity has DEALT over the whole match.
/// Added lazily on an entity's first landed hit, never reset, never decayed —
/// it is a life ledger, not a combat timer.
///
/// The Wrath sect's "Spite" is the only reader: it pools this value across
/// every enemy in the cast area and splits the total back over them
/// (docs/Design/Sects.md). Stored as an int and only ever incremented by
/// integer amounts, so two lockstep peers accumulate bit-identical ledgers.
/// </summary>
public struct DamageDealtTotal : IComponentData
{
    public int Value;
}
