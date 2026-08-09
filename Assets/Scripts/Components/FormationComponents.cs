// FormationComponents.cs
// AoE4-style persistent formation groups (see docs/Design/Navigation_And_Formations.md
// and docs/Research/AoE4_Navigation_Study.md §2-3).
//
// A formation group is a lightweight entity holding a VIRTUAL LEADER that
// follows the flow toward the group's destination. Formation spots are laid
// out around the leader; each member steers to its moving spot
// (FormationGroupSystem overrides FlowDesiredDir before SteeringSystem
// blends local avoidance on top). A member with no line of sight to its
// spot falls back to its own goal flow toward its FINAL slot destination —
// exactly the AoE4 rule (GDC 2022, slide 32).
//
// Groups are created by FormationMoveCommandHelper (single-player / direct
// execution path). In lockstep multiplayer the router falls back to
// per-unit slot moves (see CommandRouter.Formation.cs) so no new network
// command type is needed.
//
// All components live in the global namespace per project convention.
// Location: Assets/Scripts/Components/FormationComponents.cs

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Formation arrangements, mirroring AoE4's shipped set. Box is the
/// default (~2:1 width:depth); Staggered doubles spacing and offsets
/// alternate ranks so no unit stands directly behind another.
/// </summary>
public enum FormationShape : byte
{
    Box = 0,
    Line = 1,
    Wedge = 2,
    Staggered = 3,
}

/// <summary>
/// Group state component on the formation-group entity. The virtual
/// leader is not a unit — no UnitTag, no transform — so it never appears
/// in selection, targeting, steering or the spatial hash.
/// </summary>
public struct FormationGroup : IComponentData
{
    /// <summary>Virtual leader position (XZ plane; y unused).</summary>
    public float3 LeaderPos;
    /// <summary>Group destination (walkable-snapped click point).</summary>
    public float3 Destination;
    /// <summary>Unit-length travel direction the layout is built around.
    /// Frozen at its last value on arrival.</summary>
    public float3 Facing;
    /// <summary>Slowest member's speed — every member travels at this
    /// (AoE4 group-speed rule). Recomputed as members detach.</summary>
    public float GroupSpeed;
    /// <summary>Faction of the members (goal-flow fields are per-faction).</summary>
    public byte FactionIdx;
    public FormationShape Shape;
    /// <summary>0 = Moving, 1 = Arrived (spots frozen at the destination).</summary>
    public byte State;
    /// <summary>Consecutive ticks the leader has been unable to step
    /// (blocked cell). At <see cref="StallReleaseTicks"/> the group flips
    /// to Arrived so members finish on their own flow instead of hovering
    /// around a stuck leader.</summary>
    public byte StallTicks;

    /// <summary>Leader-stall release threshold (ticks).</summary>
    public const byte StallReleaseTicks = 120;

    public const byte StateMoving = 0;
    public const byte StateArrived = 1;

    /// <summary>Only units within this range of the group centroid at order
    /// time travel as a formation (AoE4 "a few tiles" cohesion gate);
    /// outliers path independently to their slot.</summary>
    public const float CohesionRadius = 12f;
    /// <summary>Catch-up speed bonus for members behind their spot
    /// (AoE4 Season Four value: +40%).</summary>
    public const float CatchUpMultiplier = 1.4f;
    /// <summary>How far behind its spot a member must be before the
    /// catch-up bonus kicks in.</summary>
    public const float CatchUpTriggerDistance = 1.5f;
    /// <summary>Leader arrival radius (matches the integrator's
    /// StopDistance so members settle exactly once).</summary>
    public const float ArriveDistance = 0.5f;
}

/// <summary>
/// One member slot on the group entity. Slot is a leader-local offset:
/// x = along the formation's right axis, y = along facing (0 for the
/// front rank, negative for ranks behind the leader).
/// </summary>
[InternalBufferCapacity(0)]
public struct FormationMember : IBufferElementData
{
    public Entity Unit;
    public float2 Slot;
}

/// <summary>
/// Per-unit back-reference to the formation group the unit travels with.
/// Removed when the unit detaches (combat, new individual order, arrival).
/// </summary>
public struct FormationMemberState : IComponentData
{
    public Entity Group;
    public float2 Slot;
}
