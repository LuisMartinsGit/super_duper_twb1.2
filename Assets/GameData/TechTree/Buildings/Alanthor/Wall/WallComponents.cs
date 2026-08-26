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

/// <summary>Buffer on segment entities listing all child wall instance entities.</summary>
public struct WallInstanceRef : IBufferElementData
{
    public Entity Instance;
}

/// <summary>
/// Active upgrade timer on a wall instance. Added when upgrade starts, removed on completion.
/// UpgradeType: 1 = Tower, 2 = Gate.
/// </summary>
public struct WallUpgradeState : IComponentData
{
    public byte UpgradeType;  // 1 = Tower, 2 = Gate
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
