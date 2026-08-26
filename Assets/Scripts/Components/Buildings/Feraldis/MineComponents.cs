// File: Assets/GameData/TechTree/Buildings/Feraldis/Mine/MineComponents.cs
// Canon: docs/Design/Age_0.md § Mine (2026-08-05 rev.4).

using Unity.Entities;

/// <summary>
/// A Mine — placed beside an ore patch, it works every iron and veilstone
/// rock within <c>MineConstants.PatchRadius</c> with NO workers at all.
///
/// The trade is deliberate: a Mine yields less per second than the same
/// nodes worked by hand, but it **never depletes them**. Hand-mining is the
/// fast, finite option; a Mine is the slow, permanent one. It is also the
/// only way Feraldis touches ore, since Feraldis Workers cannot gather.
/// </summary>
public struct MineTag : IComponentData { }

/// <summary>Per-mine extraction bookkeeping.</summary>
public struct MineState : IComponentData
{
    /// <summary>Iron-bearing nodes in range, refreshed on a slow rescan.</summary>
    public int IronNodes;

    /// <summary>Veilstone-bearing nodes in range.</summary>
    public int VeilstoneNodes;

    /// <summary>Seconds until the next payout tick.</summary>
    public float TickTimer;

    /// <summary>Seconds until the next node rescan (nodes can be destroyed,
    /// and veilstone outcroppings precipitate in and out over a match).</summary>
    public float RescanTimer;

    /// <summary>Fractional carry so slow per-node rates are not lost to
    /// integer resource writes.</summary>
    public float IronPurse;
    public float VeilstonePurse;

    /// <summary>Diagnostic latch: 0 = never reported, 1 = reported. The
    /// 2026-08-06 match had two completed Mines per Feraldis AI and iron
    /// still ended at 0-6, with no way to tell from the logs whether the
    /// Mines never finished building or simply found no nodes in range.
    /// MineIncomeSystem now says which, once, per Mine.</summary>
    public byte Reported;
}
