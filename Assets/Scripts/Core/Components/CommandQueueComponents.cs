// CommandQueueComponents.cs
// Components for multi-waypoint command queuing and planning mode
// Location: Assets/Scripts/Core/Components/CommandQueueComponents.cs

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Type of queued command for the command queue system.
/// </summary>
public enum QueuedCommandType : byte
{
    Move = 0,
    AttackMove = 1,
    Patrol = 2
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
}

/// <summary>
/// Marker tag: entity is currently draining its command queue.
/// CommandQueueSystem processes the next command when the current one completes.
/// </summary>
public struct CommandQueueActive : IComponentData { }
