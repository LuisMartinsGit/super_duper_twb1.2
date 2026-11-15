// MarketComponents.cs
// The Alanthor Market's ECS vocabulary (docs/Design/Age_1_Alanthor.md § The
// Market; Unification decision 55 (d)).

using Unity.Entities;

/// <summary>Marks the Alanthor Market (one per player).</summary>
public struct MarketTag : IComponentData { }

/// <summary>
/// What the Market buys and its carry. The rates are the player's setting
/// (the SetMarketRate lockstep command, CommandRouter.SetMarketRateDirect),
/// in whole units a minute, multiples of the Market SO's rateStep, 0 up to
/// its maxRatePerMinute. The carries are units owed in SIXTIETHS: MarketSystem
/// adds the rate every second and buys the whole units. On the entity, so the
/// setting rides a save snapshot and dies with the Market.
/// </summary>
public struct MarketRates : IComponentData
{
    public int IronPerMinute;
    public int VeilstonePerMinute;
    public int IronSixtieths;
    public int VeilstoneSixtieths;
    /// <summary>Surplus mode (decision 59, 2026-11-15): while AutoIron is 1,
    /// every second the Market also turns every supply above ReserveSupplies
    /// into iron at its fixed price, after the set rates are bought. Set by
    /// the SetMarketSurplus lockstep command (Market.SetSurplus).</summary>
    public int ReserveSupplies;
    public byte AutoIron;
}
