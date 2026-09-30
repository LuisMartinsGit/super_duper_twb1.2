// CommandQueueComponents.cs
// Components for multi-waypoint command queuing and planning mode

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Type of queued command for the command queue system.
/// </summary>
public enum QueuedCommandType : byte
{
    Move = 0,
    AttackMove = 1,
    Patrol = 2,
    // Targeted steps (2026-09-29): Shift+right-click queues what was CLICKED.
    Attack = 3,   // an enemy unit or building
    Build = 4,    // a friendly construction site (workers)
    Repair = 5,   // a damaged friendly building (workers)
    Heal = 6,     // a wounded friendly unit (healers)
}

/// <summary>
/// A single queued command in an entity's command queue.
/// Used by Shift+right-click waypoint queuing and planning mode.
/// </summary>
public struct QueuedCommand : IBufferElementData
{
    public QueuedCommandType Type;
    public float3 TargetPosition;
    public Entity TargetEntity;

    /// <summary>Formation shape (FormationShape as a byte) for a grouped step.</summary>
    public byte Shape;

    /// <summary>
    /// Formation group of a Move / AttackMove / Patrol step (0 = none). Every
    /// unit of one Shift+click shares the id; CommandQueueSystem releases the
    /// step only when all of them are ready, and issues it as ONE formation
    /// move, so a queued route is marched in formation (2026-09-29). Assigned
    /// by the issuer and carried in the lockstep payload, so every peer groups
    /// identically.
    /// </summary>
    public int Group;
}

/// <summary>
/// Marker: the unit is walking a QUEUED plain-move step. Waypoints outweigh
/// the Aggressive stance (2026-09-29): like a player's plain move, it does not
/// auto-acquire until the queue is done — a route that fights at every
/// waypoint is an attack-move, which the queue can hold explicitly.
/// </summary>
public struct QueuedMoveStep : IComponentData { }

/// <summary>Limits of the per-unit command queue.</summary>
public static class CommandQueueLimits
{
    /// <summary>
    /// A unit holds at most this many queued actions (2026-09-29) — humans and
    /// AI alike. Enforced where every peer appends
    /// (CommandRouter.QueuedWaypointDirect), so a full queue drops the extra
    /// order identically everywhere.
    /// </summary>
    public const int MaxQueuedCommands = 15;
}

/// <summary>
/// Marker tag: entity is currently draining its command queue.
/// CommandQueueSystem processes the next command when the current one completes.
/// </summary>
public struct CommandQueueActive : IComponentData { }

/// <summary>
/// Marker tag: entity's command queue is paused. CommandQueueSystem ignores
/// frozen entities, so queued commands accumulate without executing.
/// Added while the user holds Shift to build up a queue; removed on release
/// so the queue starts draining.
/// </summary>
public struct CommandQueueFrozen : IComponentData { }
