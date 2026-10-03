// VeilstoneNodeState.cs
// The three states of a veilstone outcrop (docs/Design/Veilstone_Economy.md §2).

using Unity.Entities;

/// <summary>Which of its three states a veilstone outcrop is in.</summary>
public enum VeilstoneNodeKind : byte
{
    /// <summary>Ordinary, uncursed crystal: Mines go on it, Trading Outposts beside it.</summary>
    Inactive = 0,
    /// <summary>A curse node stands on it.</summary>
    Cursed = 1,
    /// <summary>Mined out: pays nothing until the curse takes and refills it.</summary>
    Depleted = 2,
}

/// <summary>
/// The outcrop's current state. DERIVED, never authored: VeilstoneNodeStateSystem
/// recomputes it from whether a live curse node stands on the outcrop and from
/// its NodeReserve, so it cannot disagree with either. Readers (placement,
/// income, the Trading Outpost, the UI) read this rather than re-deriving.
/// </summary>
public struct VeilstoneNodeState : IComponentData
{
    public VeilstoneNodeKind Kind;
}
