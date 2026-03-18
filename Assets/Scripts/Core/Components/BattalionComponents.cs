// BattalionComponents.cs
// ECS components for the BFME2-style battalion system
// Location: Assets/Scripts/Core/Components/BattalionComponents.cs

using Unity.Collections;
using Unity.Entities;

// ==================== Battalion Components ====================

/// <summary>
/// Marker tag for any entity that is part of a battalion (leader or member).
/// </summary>
public struct BattalionTag : IComponentData { }

/// <summary>
/// Attached to the invisible leader entity. Stores formation config.
/// The leader pathfinds via MovementSystem; members lerp to slot positions.
/// </summary>
public struct BattalionLeader : IComponentData
{
    public int Columns;               // 3
    public int Rows;                  // 5
    public float Spacing;             // 1.5f (meters between slots)
    public float FollowSpeed;         // 8f (lerp speed for members)
    public float LeashDistance;       // 10f (teleport threshold)
    public FixedString64Bytes UnitId; // e.g. "Swordsman" for display
}

/// <summary>
/// Buffer element on the leader -- references each member entity.
/// </summary>
[InternalBufferCapacity(16)]
public struct BattalionMember : IBufferElementData
{
    public Entity Value;
}

/// <summary>
/// Attached to each visible member entity -- links back to leader.
/// Members do NOT use MovementSystem, flow fields, or pathfinding.
/// BattalionSyncSystem directly lerps their position toward formation slots.
/// </summary>
public struct BattalionMemberData : IComponentData
{
    public Entity Leader;
    public int Column;  // 0..Columns-1
    public int Row;     // 0..Rows-1
}
