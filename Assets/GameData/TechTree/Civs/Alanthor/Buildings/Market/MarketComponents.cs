// MarketComponents.cs
// The Alanthor Market's ECS vocabulary (docs/Design/Age_1_Alanthor.md § The
// Market; Unification decision 67).

using Unity.Entities;

/// <summary>Marks the Alanthor Market (one per player).</summary>
public struct MarketTag : IComponentData { }

/// <summary>
/// THE SHARED PRICES (decision 67, 2026-11-16). One entity for the whole
/// match, created by the first trade: what one LOT of each resource is worth
/// in veilstone, in HUNDREDTHS of a veilstone. Every faction's Market reads
/// and moves the same two numbers — a faction that sells supplies makes them
/// cheaper for everyone. A buy raises a price, a sale lowers it, and
/// MarketSystem lets both drift back to the Market SO's starting price. On an
/// entity so it rides a save snapshot and dies with the match.
/// </summary>
public struct MarketPrices : IComponentData
{
    public int SuppliesPrice;
    public int IronPrice;
}
