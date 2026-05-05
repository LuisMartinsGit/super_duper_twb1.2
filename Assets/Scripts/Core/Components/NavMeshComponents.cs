// NavMeshComponents.cs
// ECS components for the navmesh-based pathing path (Tier-4 / Option B,
// PR2). Units that have these store the waypoint corridor returned by
// NavMesh.CalculatePath; MovementSystem walks the corridor instead of
// reading a flow-field direction.
//
// Lives in global namespace per the project's ECS convention.
//
// Location: Assets/Scripts/Core/Components/NavMeshComponents.cs

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Per-unit navmesh path state. Companion to a DynamicBuffer&lt;NavMeshWaypoint&gt;
/// that holds the actual corner positions.
///
/// LastRequestedGoal lets the request system detect that the unit's
/// DesiredDestination has moved and we need a fresh path. CurrentWaypoint
/// is incremented by MovementSystem as the unit walks the corridor.
/// </summary>
public struct NavMeshPathfollowState : IComponentData
{
    public float3 LastRequestedGoal;
    public int CurrentWaypoint;
    public byte HasPath;          // 1 = waypoint buffer is valid
    public byte RequestPending;   // 1 = waiting for the request system to fill the buffer
}

/// <summary>
/// One corner of a NavMesh-computed path. Buffer is rewritten in place
/// by NavMeshPathRequestSystem each time a fresh path is computed.
/// </summary>
public struct NavMeshWaypoint : IBufferElementData
{
    public float3 Position;
}
