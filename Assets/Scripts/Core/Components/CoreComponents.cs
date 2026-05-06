// CoreComponents.cs
// Fundamental components shared across all entity types
// Place in: Assets/Scripts/Core/Components/Core/

using Unity.Entities;
using Unity.Mathematics;

// ==================== Enums ====================

public enum Faction : byte
{
    Blue = 0,
    Red = 1,
    Green = 2,
    Yellow = 3,
    Purple = 4,
    Orange = 5,
    Teal = 6,
    White = 7,
    /// <summary>
    /// Crystal Curse — environmental hostile faction, hostile to all
    /// player factions. NOT a player slot; falls outside the 0..7 player
    /// color range so it's never picked up by lobby/AI slot iteration
    /// (LobbyConfig.Slots and player-color tables are length 8).
    /// </summary>
    Curse = 8,
}

public static class Cultures
{
    public const byte None = 0;
    public const byte Runai = 1;
    public const byte Alanthor = 2;
    public const byte Feraldis = 3;
}

// ==================== Core Identity Components ====================

/// <summary>
/// Identifies which faction an entity belongs to.
/// </summary>
public struct FactionTag : IComponentData
{
    public Faction Value;
}

/// <summary>
/// Tracks the cultural progress of a faction (for Era 2+ content).
/// </summary>
public struct FactionProgress : IComponentData
{
    public byte Culture;
}

/// <summary>
/// Links an ECS entity to its visual representation in the presentation layer.
/// </summary>
public struct PresentationId : IComponentData
{
    public int Id;
}

// ==================== Common Stats ====================

/// <summary>
/// Health points for units and buildings.
/// </summary>
public struct Health : IComponentData
{
    public int Value;
    public int Max;
}

/// <summary>
/// Movement speed for mobile entities.
/// </summary>
public struct MoveSpeed : IComponentData
{
    public float Value;
}

/// <summary>
/// Physical radius for collision detection and unit spacing.
/// </summary>
public struct Radius : IComponentData
{
    public float Value;
}

/// <summary>
/// Vision range for fog of war and target acquisition.
/// </summary>
public struct LineOfSight : IComponentData
{
    public float Radius;
}

// ==================== Movement & Navigation ====================

/// <summary>
/// Target position for pathfinding/movement.
/// </summary>
public struct DesiredDestination : IComponentData
{
    public float3 Position;
    public byte Has; // 0 = no destination, 1 = has destination
}

/// <summary>
/// Marker tag indicating the unit has an active user-issued move order.
/// Prevents auto-targeting systems from overriding player commands.
/// </summary>
public struct UserMoveOrder : IComponentData { }

/// <summary>
/// Marker tag for units executing an attack-move command.
/// Units with this tag auto-acquire targets while moving.
/// </summary>
public struct AttackMoveTag : IComponentData { }

/// <summary>
/// Temporary speed override for formation movement.
/// When present, MovementSystem uses this speed instead of MoveSpeed.
/// Removed when destination is reached.
/// </summary>
public struct FormationSpeedOverride : IComponentData
{
    public float Value;
}

/// <summary>
/// Position where a unit should return after combat or when idle.
/// </summary>
public struct GuardPoint : IComponentData
{
    public float3 Position;
    public byte Has; // 0/1
}

/// <summary>
/// Per-unit smoothed movement direction for flow-field smoothing.
/// Prevents cell-boundary oscillation by lerping toward the raw flow field direction.
/// </summary>
public struct SmoothedDirection : IComponentData
{
    public float3 Value;
}

/// <summary>
/// Tracks consecutive frames where movement was blocked by passability or slope.
/// Used by MovementSystem to detect stuck units and try alternate directions.
/// </summary>
public struct StuckState : IComponentData
{
    /// <summary>Consecutive frames where movement was blocked.</summary>
    public byte Counter;
    /// <summary>Which perpendicular attempt was last tried (0=none, 1=left, 2=right).</summary>
    public byte LastAttempt;
}

/// <summary>
/// Caches the last snapped flow field destination index to avoid calling
/// FlowFieldManager.RequestFlowField every frame when the destination hasn't changed.
/// Also caches the terrain height for the current cell to reduce TerrainUtility.GetHeight calls.
/// </summary>
public struct MovementCache : IComponentData
{
    /// <summary>Last destination position that was sent to FlowFieldManager.</summary>
    public float3 LastDestination;
    /// <summary>Snapped destination cell index returned by the last successful RequestFlowField.</summary>
    public int LastSnappedDest;
    /// <summary>Grid cell the unit was in for the last terrain height sample.</summary>
    public int2 LastHeightCell;
    /// <summary>Cached terrain height for LastHeightCell.</summary>
    public float CachedHeight;
    /// <summary>
    /// FlowFieldManager.GridVersion at the time the cached flow-field lookup
    /// was acquired. When the live grid version diverges, the cached field is
    /// stale and the unit must re-request even if its destination is unchanged.
    /// </summary>
    public int LastGridVersion;
}

/// <summary>
/// Rally point for newly trained units.
/// </summary>
public struct RallyPoint : IComponentData
{
    public float3 Position;
    public byte Has;
}

// ==================== Hold Position ====================

/// <summary>
/// Marker tag for units in hold position mode.
/// Units with this tag attack enemies within range but do NOT chase or move to pursue.
/// Cleared when any new command is issued (move, attack, gather, etc.).
/// </summary>
public struct HoldPositionTag : IComponentData { }

// ==================== Patrol System ====================

/// <summary>
/// Marker tag for units executing a patrol command.
/// Units with this tag auto-acquire targets while patrolling (like AttackMoveTag).
/// </summary>
public struct PatrolTag : IComponentData { }

/// <summary>
/// A single waypoint in a patrol route.
/// </summary>
public struct PatrolWaypoint : IBufferElementData
{
    public float3 Position;
    public float WaitSeconds; // Optional pause at this waypoint
}

/// <summary>
/// Per-unit patrol state tracking.
/// </summary>
public struct PatrolAgent : IComponentData
{
    public int Index;      // Current waypoint index
    public byte Loop;      // 1 = loop, 0 = stop at end
    public float WaitTimer; // Countdown while waiting at a waypoint
}