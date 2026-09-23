// WallGateComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Marks a wall instance that has been upgraded to a gate (friendly-only passage).</summary>
public struct WallGateTag : IComponentData { }

/// <summary>
/// Tracks gate open/close state. Gates auto-open when friendly units are nearby
/// and close when no friendlies are in proximity.
/// </summary>
public struct WallGateState : IComponentData
{
    /// <summary>1 = open (passable for friendlies), 0 = closed (blocked for all).</summary>
    public byte IsOpen;
    /// <summary>Countdown timer for next proximity check.</summary>
    public float RecheckTimer;
}

/// <summary>
/// Marks a wall instance that participates in a multi-instance gate region
/// (AlanthorWall.GateRegionSpan = 3 modules x 3 m = ~9 m gatehouse, not the
/// legacy single-instance gate). All
/// region members carry this tag and a shared <see cref="WallGateGroup"/>
/// with the same Leader entity. Read by
/// <c>WallGatePassabilitySystem</c> to widen friendly-detect radius
/// (3.0 → 6.0) so all region cells open in unison when a battalion approaches.
/// (task-109 phase 5)
/// </summary>
public struct WallGateRegionTag : IComponentData { }

/// <summary>
/// Shared group identifier for the instances composing a gate region.
/// Every member carries the same <see cref="Leader"/> entity reference
/// (typically the centre / focus instance picked at conversion time).
/// <c>em.Exists(Leader)</c> doubles as the membership check — no
/// separate int counter is needed and IDs stay deterministic across
/// peers without coordinating a wire-side counter.
/// (task-109 phase 5)
/// </summary>
public struct WallGateGroup : IComponentData
{
    /// <summary>The focus / centre instance. Acts as the deterministic
    /// group identifier; siblings carry the same value.</summary>
    public Entity Leader;
}

/// <summary>
/// Player override on a gate (docs/Design/Age_1_Alanthor.md § Opening and
/// closing it). Default (component absent, or Sealed == 0) is AUTO: the
/// gate opens for friendlies within the region radius and closes behind
/// them. Sealed == 1 keeps it shut even to its own faction — a pathing
/// decision, not a defensive one. Flipped only through the
/// <c>SetGateLock</c> lockstep order, so every peer seals the same gate on
/// the same tick.
/// </summary>
public struct WallGateLock : IComponentData
{
    /// <summary>1 = shut to everyone, including friendlies.</summary>
    public byte Sealed;
}

/// <summary>
/// How wide the gatehouse is, in metres along the wall. A gate replaces
/// AlanthorWall.GateRegionSpan curtain modules with ONE entity, and the two
/// modules beside the converted one are destroyed — this is the span that
/// entity now covers, so the swept curtain mesh knows how much to leave
/// open and the visual knows how long to build the gatehouse.
/// </summary>
public struct WallGateSpan : IComponentData
{
    public float Metres;
}
