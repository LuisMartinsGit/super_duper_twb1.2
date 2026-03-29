// PathComponents.cs
// ECS component for A* per-unit pathfinding.
// Location: Assets/Scripts/Core/Components/PathComponents.cs

using Unity.Entities;

/// <summary>
/// Tracks which waypoint the unit is currently heading toward
/// in its A* path stored in AStarPathStore.
/// </summary>
public struct AStarPathIndex : IComponentData
{
    /// <summary>Index of the current waypoint in the path (0-based).</summary>
    public int CurrentWaypoint;

    /// <summary>Key into AStarPathStore's path dictionary.</summary>
    public int PathId;
}
