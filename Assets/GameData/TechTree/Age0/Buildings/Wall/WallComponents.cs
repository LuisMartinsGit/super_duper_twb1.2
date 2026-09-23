// WallComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Defensive wall segment (generic tag for all wall entities).</summary>
public struct WallTag : IComponentData { }

/// <summary>Marks a wall hub (connection point / tower between wall segments).</summary>
public struct WallHubTag : IComponentData { }

/// <summary>Marks a wall segment (the connector between two hubs).</summary>
public struct WallSegmentTag : IComponentData { }

/// <summary>Links a wall segment to its two hub endpoints.</summary>
public struct WallConnection : IComponentData
{
    public Entity HubA;
    public Entity HubB;
    /// <summary>
    /// The hub centres at creation. Walls fall in sections
    /// (docs/Design/Age_1_Alanthor.md), so a segment outlives its hubs and
    /// HubA / HubB may point at dead entities; the positions let
    /// <c>AlanthorWall.AdoptOrphanedSegments</c> re-attach it to a hub
    /// rebuilt on the same footprint instead of letting a second segment be
    /// laid on top of its standing cells.
    /// </summary>
    public float3 PosA;
    public float3 PosB;
}

/// <summary>
/// Buffer element tracking connections from a wall hub to other hubs.
/// Each entry records the connected hub and the wall segment entity between them.
/// </summary>
public struct WallHubLink : IBufferElementData
{
    public Entity ConnectedHub;
    public Entity Segment;
}

/// <summary>
/// LEGACY (mechanic removed 2026-07-06): marked the virtual entities that
/// granted supplies income from enclosed wall polygons. The producing
/// system (WallEnclosureIncomeSystem) was deleted — fortifications now
/// project territory on the influence map instead (PlayerInfluenceMap /
/// InfluenceMapSystem; docs/Design/Overview.md § The influence map).
/// The type stays so existing read-side queries keep compiling; no entity
/// ever carries it any more.
/// </summary>
public struct WallEnclosureIncomeTag : IComponentData
{
    public byte FactionIndex;
}

/// <summary>
/// LEGACY (see <see cref="WallEnclosureIncomeTag"/>): XZ vertices of a wall
/// enclosure polygon. No longer produced.
/// </summary>
public struct WallEnclosureVertex : IBufferElementData
{
    public float2 Position;
}

/// <summary>Marks a small wall piece entity (one cell of wall between hubs).</summary>
public struct WallInstanceTag : IComponentData { }

/// <summary>Links a wall instance back to its parent segment entity.</summary>
public struct WallInstanceParent : IComponentData
{
    public Entity Segment;
}

/// <summary>Buffer on segment entities listing all child wall instance
/// entities, in order along the segment.</summary>
public struct WallInstanceRef : IBufferElementData
{
    public Entity Instance;
    /// <summary>Where the cell stands. Kept here because the entry outlives
    /// the cell: the swept mesh opens a DEAD cell's span, and a split
    /// segment (cell converted to a hub) re-derives each cell's place on the
    /// new curve from this rather than from a spacing that no longer holds.</summary>
    public float3 Position;
}

/// <summary>
/// The drawn curve a curved segment follows, hub centre to hub centre
/// (docs/Design/Age_1_Alanthor.md § Drawing walls). The presentation sweeps
/// ONE continuous mesh along it; the segment's instances are invisible sim
/// cells (HP, passability, gate / tower conversion) spaced along the same
/// arc at AlanthorWall.InstanceSpacing from AlanthorWall.HubInset.
/// </summary>
public struct WallCurvePoint : IBufferElementData
{
    public Unity.Mathematics.float3 Position;
}

/// <summary>Marks an instance that is a cell of a curved segment: it has a
/// pick collider but no module visual of its own.</summary>
public struct WallCurveCellTag : IComponentData { }

/// <summary>
/// Active upgrade timer on a wall instance. Added when upgrade starts, removed on completion.
/// UpgradeType: 1 = Tower, 2 = Gate, 3 = Hub (the segment splits at the cell —
/// AlanthorWall.ConvertInstanceToHub).
/// </summary>
public struct WallUpgradeState : IComponentData
{
    public byte UpgradeType;  // 1 = Tower, 2 = Gate, 3 = Hub
    public float Duration;
    public float Remaining;
}

/// <summary>
/// Optional segment-level pointer to the last-clicked instance — stored
/// on the SEGMENT entity, references the wall INSTANCE the player
/// right-clicked. Used by the gate-conversion command (Phase 6) to pick
/// the centre of the 5-region. If absent, the helper falls back to the
/// segment midpoint (deterministic via the <see cref="WallInstanceRef"/>
/// buffer index = length / 2).
/// (task-109 phase 5)
/// </summary>
public struct WallSegmentFocus : IComponentData
{
    public Entity Instance;
}

/// <summary>
/// Active segment-level upgrade timer. Attached to the SEGMENT entity
/// (not an instance) when the player commits to a Convert-to-Gate.
/// Distinct from the per-instance <see cref="WallUpgradeState"/> which
/// still drives the Convert-to-Tower (single-instance) path. On
/// completion, <c>WallUpgradeSystem</c>'s segment-level loop tags the
/// centre instances (AlanthorWall.GateRegionSpan = 3) with <see cref="WallGateRegionTag"/> +
/// <see cref="WallGateGroup"/> + <see cref="WallGateTag"/>.
/// (task-109 phase 5)
/// </summary>
public struct WallSegmentUpgradeState : IComponentData
{
    /// <summary>2 = Gate (currently the only segment-level upgrade type).</summary>
    public byte UpgradeType;
    /// <summary>The instance the player right-clicked. May be Entity.Null —
    /// in that case the segment midpoint is used.</summary>
    public Entity FocusInstance;
    /// <summary>Total upgrade time in seconds (Phase 1 canonical: 8.0f).</summary>
    public float Total;
    /// <summary>Countdown timer; ticked by <c>WallUpgradeSystem</c>.</summary>
    public float Remaining;
}

/// <summary>
/// Which of the three wall levels this piece is clad at
/// (docs/Design/Age_1_Alanthor.md § Wall levels): 1 palisade, 2 crude
/// stone, 3 reinforced. Carried by every wall entity — hub, segment, cell
/// and gate — so the sim and the visuals read ONE value instead of asking
/// the research state on a hot path. Absent means level 1.
/// </summary>
public struct WallTier : IComponentData
{
    public byte Level;
}

/// <summary>
/// A garrison slot on a reinforced curtain module (level 3 only, two per
/// module). <see cref="Occupant"/> is Entity.Null while the slot is free;
/// the occupant itself is disabled and hidden inside the wall, exactly as
/// a building garrison works.
/// </summary>
public struct WallGarrisonSlot : IBufferElementData
{
    public Entity Occupant;
}

/// <summary>
/// On a unit that is currently inside a wall module. Holds the module so
/// the unit can be put back beside it on release, and so the module's
/// death can take its garrison with it.
/// </summary>
public struct WallGarrisonedIn : IComponentData
{
    public Entity Module;
}
