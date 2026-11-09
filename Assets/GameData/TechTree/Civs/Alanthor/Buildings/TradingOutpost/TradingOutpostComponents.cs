// TradingOutpostComponents.cs
// Components for the Alanthor Trading Outpost (docs/Design/Veilstone_Economy.md §3.1,
// rewritten 2026-11-09, decision 42): a post beside a resource node that trickles
// that node's resource without ever drawing on it. No trades, no routes.

using Unity.Entities;

/// <summary>Marks an Alanthor Trading Outpost.</summary>
public struct TradingOutpostTag : IComponentData { }

/// <summary>
/// Fractional carry of the per-minute trickle, per resource, so a 7.5-a-minute
/// rate is paid exactly over a minute instead of being rounded every cycle.
/// </summary>
public struct TradingOutpostCarry : IComponentData
{
    public float Supplies, Iron, Veilstone;
}

/// <summary>
/// Which resource node this Outpost stands beside, and on which of its four
/// sides (docs/Design/Veilstone_Economy.md §3.1). The node is kept by POSITION,
/// not by Entity, so the link is the same on every lockstep peer.
/// (The fields keep their "Outcrop" names from when only veilstone outcrops
/// took posts; since 2026-11-09 the node may be a supply, iron or veilstone node.)
///
/// <see cref="RampIndex"/> is the post's place in its node's cost ramp: how
/// many other posts already stood beside that node when this one broke ground
/// (0 = the first).
/// </summary>
public struct TradingOutpostSite : IComponentData
{
    public float OutcropX, OutcropZ;
    /// <summary>0 north (+z), 1 east (+x), 2 south (-z), 3 west (-x);
    /// 255 = no side (a post that matched no node).</summary>
    public byte Side;
    public byte RampIndex;
}

/// <summary>
/// STOP EXTRACTING (docs/Design/Veilstone_Economy.md §3.5, 2026-11-09). On an
/// extractor (the Gatherer's Hut line, the Mine, the Veilstone Mine): it draws
/// nothing from its node and pays no slot income, but stands, holds, locks and
/// keeps paying its surveys. Toggled by the SetExtractorStopped lockstep order.
/// </summary>
public struct ExtractorStopped : IComponentData { }
