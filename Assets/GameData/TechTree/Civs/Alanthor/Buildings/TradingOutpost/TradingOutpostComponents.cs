// TradingOutpostComponents.cs
// Components for the Alanthor Trading Outpost (docs/Design/Veilstone_Economy.md §3.1).

using Unity.Entities;

/// <summary>Marks an Alanthor Trading Outpost.</summary>
public struct TradingOutpostTag : IComponentData { }

/// <summary>The trades an Outpost can run. Hold is the default (2026-10-11: a
/// new post trades nothing until the player assigns it a trade); Forge and Sell
/// are unlocked by research at the Outpost.</summary>
public enum TradeRecipe : byte
{
    /// <summary>Supplies + iron → veilstone.</summary>
    BuyVeilstone = 0,
    /// <summary>Veilstone → veilsteel (research: Veilsteel Forging).</summary>
    ForgeVeilsteel = 1,
    /// <summary>Veilsteel → iron + supplies (research: Veilsteel Export).</summary>
    SellVeilsteel = 2,
    /// <summary>Trades nothing: the post stands idle, spending and earning
    /// nothing, until set to a trade again (Veilstone_Economy.md §3.1,
    /// 2026-10-05). Always available.</summary>
    Hold = 3,
    /// <summary>Supplies → iron (2026-11-04). Always available: the answer to
    /// an iron drought when supplies pile up.</summary>
    BuyIron = 4,
    /// <summary>Veilsteel → supplies (research: Veilsteel Export).</summary>
    SellSupplies = 5,
}

/// <summary>
/// TRADE ROUTES (2026-11-04, Veilstone_Economy.md §3.1): a post runs up to its
/// level's <c>tradeRoutes</c> routes at once (L1 one, L2 two, L3 four), and the
/// SAME trade may fill several of them: each repeat runs the trade once more.
/// Kept as a count per recipe in <see cref="TradingOutpostMode.Counts"/>, three
/// bits each. <see cref="TradingOutpostMode.Recipe"/> stays the FIRST route, for
/// every reader that wants one recipe. Hold is "no route".
/// </summary>
public static class TradeRoutes
{
    public const int Count = 6;
    private const int BitsPer = 3;
    private const uint FieldMask = 7u;
    /// <summary>Every bit a counts value may use.</summary>
    public const uint AllCountsMask = (1u << (Count * BitsPer)) - 1u;
    public static readonly TradeRecipe[] All =
    {
        TradeRecipe.BuyVeilstone, TradeRecipe.ForgeVeilsteel, TradeRecipe.SellVeilsteel,
        TradeRecipe.BuyIron, TradeRecipe.SellSupplies,
    };
    public static int CountIn(uint counts, TradeRecipe r)
        => r == TradeRecipe.Hold || (int)r >= Count ? 0 : (int)((counts >> ((int)r * BitsPer)) & FieldMask);
    public static uint With(uint counts, TradeRecipe r, int n)
    {
        if (r == TradeRecipe.Hold || (int)r >= Count) return counts;
        int shift = (int)r * BitsPer;
        return (counts & ~(FieldMask << shift)) | (((uint)n & FieldMask) << shift);
    }
    public static uint CountsOf(in TradingOutpostMode m) => m.Counts != 0 ? m.Counts : With(0u, m.Recipe, 1);
    public static bool Has(uint counts, TradeRecipe r) => CountIn(counts, r) > 0;
    public static int Total(uint counts)
    {
        int n = 0;
        for (int i = 0; i < All.Length; i++) n += CountIn(counts, All[i]);
        return n;
    }
    public static TradeRecipe First(uint counts)
    {
        for (int i = 0; i < All.Length; i++) if (Has(counts, All[i])) return All[i];
        return TradeRecipe.Hold;
    }
    public static TradingOutpostMode ModeOf(uint counts)
        => new TradingOutpostMode { Recipe = First(counts), Counts = counts };
}

/// <summary>
/// Which trades the Outpost runs. Changed only by the SetOutpostMode lockstep
/// order, so every peer runs the same routes.
/// </summary>
public struct TradingOutpostMode : IComponentData
{
    /// <summary>The first route (Hold when none) — the one-recipe view.</summary>
    public TradeRecipe Recipe;
    /// <summary>How many of the post's routes run each recipe (TradeRoutes);
    /// 0 reads as "just Recipe, once", so a fresh mode works.</summary>
    public uint Counts;
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
