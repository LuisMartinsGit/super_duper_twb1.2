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

    /// <summary>1 while the leader is easing for a member out of formation.
    /// LATCHED, because the ease threshold sits inside the formation's own
    /// steady-state noise: the offset the group settles at and the offset that
    /// triggers the ease were within 3 cm of each other, so the leader stuck at
    /// 90% forever and the squad simply moved slower than it should.</summary>
    public byte Easing;

    /// <summary>
    /// Distance from the leader to the OUTERMOST slot, in metres. Taken from
    /// the layout when the group is created.
    ///
    /// This is what makes a turn physically honest. While the lattice rotates
    /// at omega rad/s, the slot on the far flank is dragged omega * Radius m/s
    /// SIDEWAYS on top of the group's forward speed. A member has only
    /// (CatchUpMultiplier - 1) of its speed spare, so the fastest wheel the
    /// formation can actually hold is that headroom divided by this radius. A
    /// wide army wheels slowly, a small one turns briskly, and neither
    /// outruns its own flank.
    /// </summary>
    public float Radius;

    /// <summary>Ceiling on the wheel rate (rad/s), for a formation small
    /// enough that its radius would allow an absurd one. A quarter-turn in
    /// about a second is as fast as a body of troops can look while doing
    /// it.</summary>
    public const float MaxTurnRate = 1.2f;

    /// <summary>
    /// Floor on the wheel rate (rad/s). An ANTI-DEADLOCK GUARD ONLY — it must
    /// never be large enough to override the flank budget above.
    ///
    /// It was 0.25, chosen when a formation was infantry and 6.4 m across. Add
    /// a siege train and the army is 15 m deep and its group speed drops to the
    /// catapult's 3.0, so the headroom falls to 1.2 m/s while the floor demands
    /// 0.25 * 15.24 = 3.8 m/s of sideways motion from the rearmost engine —
    /// three times what it has. The floor was quietly asking for a turn the
    /// formation could not physically make, and the units that could not make
    /// it are exactly the ones at the ends.
    ///
    /// A deep, slow army wheeling slowly is not a bug to tune away. The leader
    /// keeps its forward speed through the turn (scaled by cos), so the
    /// formation sweeps a wide arc rather than stalling.
    /// </summary>
    public const float MinTurnRate = 0.05f;


    /// <summary>
    /// Heading error (rad) beyond which the formation stops trying to wheel
    /// and simply RE-FORMS on the new bearing. About 100 degrees.
    ///
    /// Wheeling is the right answer for a corner and the wrong one for an
    /// about-face. The turn rate is bounded by what the outer flank can
    /// follow, so a fifteen-unit army needs ten seconds to come through 180
    /// degrees — and it is barely translating while it does, because forward
    /// speed scales with cos(error). A player ordering a retreat would watch
    /// their army pivot on the spot with the enemy walking into it, which is
    /// the kiting failure this whole system was built to fix.
    ///
    /// Past this angle, snapping the facing and letting the members walk to
    /// their new spots IS the honest animation: an about-face is a re-form,
    /// not a wheel. The slot memory means they re-form into the same ranks
    /// rather than scrambling for new ones.
    /// </summary>
    public const float WheelSnapAngle = 1.75f;

    /// <summary>Leader-stall release threshold (ticks).</summary>
    public const byte StallReleaseTicks = 120;

    /// <summary>
    /// RETIRED 2026-08-28, kept only so the TetherTicks docs above still read.
    ///
    /// This was the distance at which the leader began scaling its step down,
    /// reaching a dead stop at twice this value. The scaling existed because
    /// the leader is a point mass — no separation, no obstacle slide, no
    /// turn-rate clamp, no terrain or BorderDebuff penalty — so at equal
    /// nominal speed it always outruns the units it leads.
    ///
    /// That is still true, but the ramp is not how it is handled any more: the
    /// leader now takes a flat 10% cut while ANY member is out of formation
    /// (FormationGroupSystem.OutOfFormationLeaderSpeed). The ramp was tuned
    /// around members starting a long way from their spots, which was itself a
    /// bug — the slot block hung a pitch behind the leader, so a correctly
    /// formed squad began every order 2 m out of position and the leader
    /// stalled while it "formed up" on ground it was already standing on.
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

/// <summary>
/// The slot a unit holds, REMEMBERED ACROSS ORDERS.
///
/// FormationMemberState is removed the moment a group dissolves — on arrival,
/// on combat, on any plain move — so it cannot answer "where did this unit
/// stand last time". Without an answer, every new order re-derived the whole
/// assignment from live positions measured along the NEW travel axis: turn an
/// army 45 degrees and every unit's along/lateral ordering changes, so every
/// unit is handed a different slot and the army trades places to reach it.
/// That is not a formation turning, it is a formation dissolving and
/// re-forming on a new heading, which is exactly what a corner looked like.
///
/// Held by INDEX, not by offset. The offsets themselves are re-derived per
/// order and nudged by ResolveSlot onto standable ground, so they differ by
/// centimetres between two identical orders and an offset match never fired.
/// The index is exact, and LayoutKey guards it: lose a unit, change shape, and
/// the blocks resize, the key changes, and the assignment falls through to a
/// clean rebuild rather than reusing indices that now mean something else.
///
/// Survives Detach on purpose. It is cleared by the commands that genuinely
/// take a unit out of formation (plain move, attack-move, CommandRouter).
/// </summary>
public struct FormationSlotMemory : IComponentData
{
    /// <summary>Index into the layout's slot list.</summary>
    public int Slot;
    /// <summary>Identifies the layout the index belongs to.</summary>
    public uint LayoutKey;
}
