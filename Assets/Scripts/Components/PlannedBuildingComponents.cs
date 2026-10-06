// PlannedBuildingComponents.cs
// A PLANNED building (docs/Design/Planned_Buildings.md): what a build order
// creates before any worker has broken ground.

using Unity.Collections;
using Unity.Entities;

/// <summary>
/// A building that has been ordered but not started. Deliberately carries NO
/// BuildingTag, Health or UnderConstruction: everything that reacts to a
/// building — combat, territory locks, pathing, income, fog intel, the
/// minimap — ignores a plan by construction. It is a white preview its owner
/// sees, a reservation on its owner's tiles, and nothing else.
///
/// Paid in full when planned (PaidBuildCost on the same entity), refunded in
/// full when cancelled. PlannedBuildingSystem turns it into the real
/// under-construction site the moment a worker arrives.
/// </summary>
public struct PlannedBuilding : IComponentData
{
    public FixedString64Bytes BuildingId;
    /// <summary>Placement yaw, degrees (already round-tripped through the wire).</summary>
    public float Yaw;
    /// <summary>Religion Points paid with it (the Temple), refunded with it.</summary>
    public int PaidReligion;
    /// <summary>Sim time the world re-check first refused to break ground
    /// here (0 = never refused). The plan waits out a grace period before it
    /// is cancelled (PlannedBuildings.BreakGround).</summary>
    public float RefusedSince;
    /// <summary>Sim time of the next break-ground attempt while refused.</summary>
    public float NextBreakGroundAt;
}
