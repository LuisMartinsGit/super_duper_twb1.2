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

/// <summary>
/// Who is going to build this plan (docs/Design/Planned_Buildings.md §6).
/// A plan exists only as an entry in some live worker's build order or
/// queue; PlannedBuildingSystem's ownership sweep keeps this up to date and
/// cancels (refunding) a plan nobody holds. A separate component rather than
/// fields on PlannedBuilding so a game saved before the rule still loads —
/// a plan without it is treated as one that HAD a worker and lost it.
/// </summary>
public struct PlanOwnership : IComponentData
{
    /// <summary>1 once any worker has held the plan. A never-held plan (a
    /// lever placement, an AI placement with no free hands) is offered to an
    /// idle worker before it is cancelled; a held-then-lost one is not.</summary>
    public byte EverOwned;
    /// <summary>1 while the sweep finds no worker holding it.</summary>
    public byte Unowned;
    /// <summary>Sim time the current unowned stretch began.</summary>
    public float UnownedSince;
}
