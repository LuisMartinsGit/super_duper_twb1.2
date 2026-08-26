// OverpassBridgeComponents.cs
// Overpass bridges: BFME2-gate-style dual-level structures — the deck is
// walkable ON TOP (nav layer 1, like wall ramparts) while the ground
// UNDERNEATH stays walkable for through-traffic. Contrast with the legacy
// BridgeSurface-over-NoWalk bridges, whose ground is impassable and which
// live entirely on layer 0 as deck-only cost cells.
//
// See docs/Design/Navigation_And_Formations.md §5. Stamped into the nav
// cost field by CostFieldStampSystem (deck → layer-1 walkable, ramp ends →
// climb-access cells on both layers); ramp entities are ungated access
// points for LayeredMoveSystem, so any unit — any faction — can path up
// one ramp, across the deck, and down the other side, or simply walk
// underneath at ground level.
//
// Global namespace per project ECS-component convention.

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// One overpass span. Start/End are the deck end centers on the XZ plane
/// (y ignored — the deck rides at the rampart deck height, see
/// LayerTransitionSystem.DeckY, or at the BridgeSurface mesh height when
/// one is present). Width is the full deck width in meters.
/// </summary>
public struct OverpassBridge : IComponentData
{
    public float3 Start;
    public float3 End;
    public float Width;

    /// <summary>Radius (m) of the climb-access disc stamped at each deck
    /// end — the ramp footprint units use to mount/dismount.</summary>
    public const float RampRadius = 2.5f;
}

/// <summary>
/// Marker on the two ramp entities of an overpass (one per deck end).
/// LayeredMoveSystem treats these as UNGATED layer access points — usable
/// by every faction, exactly like wall breach ramps.
/// </summary>
public struct OverpassRampTag : IComponentData { }
