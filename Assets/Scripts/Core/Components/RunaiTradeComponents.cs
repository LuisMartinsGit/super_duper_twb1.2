// RunaiTradeComponents.cs
// Components for the Runai trade network system (TradeHubs, Bazaars, Hall)
// Replaces the old TradingPost chain system.

using Unity.Entities;
using Unity.Mathematics;

// ==================== Trade Network Node ====================

/// <summary>
/// Marker for buildings participating in the Runai trade network.
/// Added dynamically to completed TradeHubs, Bazaars, and Halls of Runai factions.
/// Traders and patrols use this tag to find valid destinations.
/// </summary>
public struct TradeNodeTag : IComponentData { }

// ==================== TradeHub Spawner ====================

/// <summary>
/// Spawner state on TradeHub buildings. Manages trader and patrol generation.
/// Each TradeHub generates 1 trader every 30s (faction max 30) and 1 patrol every 20s.
/// </summary>
public struct TradeHubSpawner : IComponentData
{
    /// <summary>Countdown to next trader spawn (resets to 30s).</summary>
    public float TraderTimer;

    /// <summary>Countdown to next patrol spawn (resets to 20s).</summary>
    public float PatrolTimer;

    /// <summary>Number of traders this hub has spawned (for patrol cap: 5 * TradersSpawned).</summary>
    public int TradersSpawned;

    /// <summary>Number of active patrol units spawned by this hub.</summary>
    public int PatrolsSpawned;
}

// ==================== Trade Node Patrol Spawner ====================

/// <summary>
/// Patrol-only spawner for Hall and Bazaar buildings in Runai trade network.
/// These buildings don't spawn traders but do generate patrol soldiers.
/// </summary>
public struct TradeNodePatrolSpawner : IComponentData
{
    /// <summary>Countdown to next patrol spawn (resets to 20s).</summary>
    public float PatrolTimer;

    /// <summary>Number of active patrol units spawned by this building.</summary>
    public int PatrolsSpawned;

    /// <summary>Maximum patrols this building can have (default 5).</summary>
    public int PatrolCap;
}

// ==================== Runai Trader State ====================

/// <summary>
/// State for Runai traders using random-destination, distance-based resource generation.
/// Replaces the old chain-based TraderState.
/// Traders accumulate supplies (1 per 2 distance) and crystal (1 per 15 distance) while moving.
/// </summary>
public struct RunaiTraderState : IComponentData
{
    /// <summary>The trade node entity the trader is heading toward.</summary>
    public Entity CurrentDest;

    /// <summary>Fractional supply accumulator (deposit integer part on arrival).</summary>
    public float AccumulatedSupplies;

    /// <summary>Fractional crystal accumulator (deposit integer part on arrival).</summary>
    public float AccumulatedCrystal;

    /// <summary>Position at start of frame, used for distance calculation.</summary>
    public float3 PreviousPosition;
}

// ==================== Runai Population Override ====================

/// <summary>
/// Tag on faction bank entity indicating Runai's 200-pop override.
/// When present, PopulationSyncSystem sets Max to 200 regardless of housing.
/// Added by AgeUpSystem on Runai culture selection.
/// </summary>
public struct RunaiPopOverride : IComponentData { }

// ==================== Bazaar Wagon (Packed Form) ====================

/// <summary>Marker for the wagon unit form of a packed Thessara's Bazaar.</summary>
public struct BazaarWagonTag : IComponentData { }

/// <summary>
/// State data for a packed Bazaar wagon, used to restore the building on unpack.
/// </summary>
public struct BazaarWagonState : IComponentData
{
    /// <summary>The Bazaar's max HP (for proportional HP transfer on unpack).</summary>
    public int OriginalMaxHP;
}

// ==================== Pack/Unpack Commands ====================

/// <summary>Command to pack a Bazaar building into a wagon unit. Consumed by BazaarPackSystem.</summary>
public struct BazaarPackCommand : IComponentData { }

/// <summary>Command to unpack a wagon unit back into a Bazaar building. Consumed by BazaarPackSystem.</summary>
public struct BazaarUnpackCommand : IComponentData { }
