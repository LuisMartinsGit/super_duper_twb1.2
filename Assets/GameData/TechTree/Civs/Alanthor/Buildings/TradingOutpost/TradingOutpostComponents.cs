// TradingOutpostComponents.cs
// Components for the Alanthor Trading Outpost (docs/Design/Veilstone_Economy.md §3.1).

using Unity.Entities;

/// <summary>Marks an Alanthor Trading Outpost.</summary>
public struct TradingOutpostTag : IComponentData { }

/// <summary>The three trades an Outpost can run. Buy is the default; the other
/// two are unlocked by research at the Outpost.</summary>
public enum TradeRecipe : byte
{
    /// <summary>Supplies + iron → veilstone.</summary>
    BuyVeilstone = 0,
    /// <summary>Veilstone → veilsteel (research: Veilsteel Forging).</summary>
    ForgeVeilsteel = 1,
    /// <summary>Veilsteel → iron + supplies (research: Veilsteel Export).</summary>
    SellVeilsteel = 2,
}

/// <summary>
/// Which trade the Outpost runs. Changed only by the SetOutpostMode lockstep
/// order, so every peer runs the same recipe.
/// </summary>
public struct TradingOutpostMode : IComponentData
{
    public TradeRecipe Recipe;
}

/// <summary>
/// Fractional carry of the per-minute recipe, per resource, so a discounted
/// 27.5-a-cycle input is charged exactly over a minute instead of being rounded
/// every cycle. In = what is still owed to be spent, Out = still owed to be paid.
/// </summary>
public struct TradingOutpostCarry : IComponentData
{
    public float InSupplies, InIron, InVeilstone, InVeilsteel;
    public float OutSupplies, OutIron, OutVeilstone, OutVeilsteel;
}

/// <summary>
/// Which veilstone outcrop this Outpost trades at, and which of its four
/// sides it stands on (docs/Design/Veilstone_Economy.md §3.1, 2026-10-04).
/// The outcrop is kept by POSITION, not by Entity, so the link is the same
/// on every lockstep peer and survives anything that re-creates the node
/// entity in place.
///
/// <see cref="RampIndex"/> is the post's place in its outcrop's cost ramp:
/// how many other posts already stood beside that outcrop when this one
/// broke ground (0 = the first). Its level-ups are priced on it.
/// </summary>
public struct TradingOutpostSite : IComponentData
{
    public float OutcropX, OutcropZ;
    /// <summary>0 north (+z), 1 east (+x), 2 south (-z), 3 west (-x);
    /// 255 = standing on the outcrop itself (an age-up conversion that found
    /// no free side).</summary>
    public byte Side;
    public byte RampIndex;
}
