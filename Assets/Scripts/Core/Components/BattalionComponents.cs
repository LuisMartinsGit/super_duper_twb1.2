// BattalionComponents.cs
// ECS components for the BFME2-style battalion system
// Location: Assets/Scripts/Core/Components/BattalionComponents.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
    public int Columns;               // 5
    public int Rows;                  // 3
    public float Spacing;             // 1.5f (meters between slots)
    public float FollowSpeed;         // 8f (lerp speed for members)
    public float LeashDistance;       // 10f (teleport threshold)
    public FixedString64Bytes UnitId; // e.g. "Swordsman" for display
    public byte RowMirrored;          // 1 = rows are mirrored (BFME2 about-face)
    public float3 FormationForward;   // The formation's effective forward direction
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

// ==================== Battalion Stance ====================

/// <summary>
/// Battalion stance controlling engagement behavior.
/// </summary>
public enum BattalionStance : byte
{
    Default = 0,    // Auto-acquire within LOS, pursue up to 25 units from guard point, then return
    Defensive = 1,  // No auto-acquire; return fire only if attacker is within attack range
    Aggressive = 2  // Auto-acquire and pursue without distance limit (existing behavior)
}

/// <summary>
/// Attached to the battalion leader. Controls how members engage enemies.
/// Members do NOT carry this -- they look up their leader's stance via BattalionMemberData.Leader.
/// </summary>
public struct BattalionStanceData : IComponentData
{
    public BattalionStance Value;
}

/// <summary>
/// Set on a unit when it takes damage. Records the attacking entity.
/// Used by TargetingSystem to implement Defensive stance return-fire behavior.
/// Cleared each frame by TargetingSystem after processing.
/// </summary>
public struct LastAttackerEntity : IComponentData
{
    public Entity Value;
}

// ==================== Battalion Formation Utility ====================

/// <summary>
/// Shared helper for computing formation slot offsets.
/// Centres the formation on the leader entity (both X and Z axes).
/// </summary>
public static class BattalionFormation
{
    public static float3 ComputeSlotOffset(int col, int row, int cols, int rows, float spacing)
    {
        return new float3(
            (col - (cols - 1) * 0.5f) * spacing,
            0f,
            -(row - (rows - 1) * 0.5f) * spacing
        );
    }
}
