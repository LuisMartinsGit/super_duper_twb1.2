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
    /// <summary>Consecutive ticks the leader has been held at a standstill by
    /// the tether (see <see cref="LeaderTetherDistance"/>) WITHOUT the group
    /// closing up. At <see cref="TetherReleaseTicks"/> the worst laggard is
    /// detached so one wedged unit can't freeze the whole formation.</summary>
    public byte TetherTicks;
    /// <summary>Smallest worst-member lag seen so far this leg. Any genuine
    /// improvement resets <see cref="TetherTicks"/>, so a group that is still
    /// forming up (legitimately large lag, steadily shrinking) is never
    /// mistaken for a wedged one. Initialised to float.MaxValue.</summary>
    public float BestLag;

    /// <summary>Leader-stall release threshold (ticks).</summary>
    public const byte StallReleaseTicks = 120;

    /// <summary>
    /// How far the worst-placed member may fall behind its spot before the
    /// virtual leader starts slowing down for it. The leader is a point mass:
    /// it pays no separation, no obstacle slide, no turn-rate clamp and no
    /// terrain / BorderDebuff speed penalty, so at equal nominal speed it
    /// ALWAYS outruns the units it is supposed to lead — most visibly through
    /// veil crust and during form-up, where members start up to
    /// <see cref="CohesionRadius"/> away from their spots. Scaling the
    /// leader's step by the group's lag is what actually holds the shape.
    /// </summary>
    public const float LeaderTetherDistance = 3f;
    /// <summary>Ticks the leader may sit fully tethered with NO improvement
    /// in the worst lag before the laggard is dropped from the group.</summary>
    public const byte TetherReleaseTicks = 120;
    /// <summary>Lag improvement that counts as the group closing up.</summary>
    public const float TetherProgressEpsilon = 0.25f;

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
    /// <summary>1 while this member is running the +40% catch-up speed.
    /// Latched, because catch-up needs HYSTERESIS: with a single threshold a
    /// member sitting exactly at <see cref="FormationGroup.CatchUpTriggerDistance"/>
    /// dropped back to group speed — the same speed its spot travels at — so
    /// its closing velocity was zero and it rode a permanent 1.5 m lag. It now
    /// engages above the trigger distance and releases only once actually back
    /// in place.</summary>
    public byte CatchingUp;
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
