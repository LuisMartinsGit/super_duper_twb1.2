// File: Assets/GameData/TechTree/Buildings/Feraldis/RaiderCamp/RaiderCampComponents.cs
// Canon: docs/Design/Age_1_Feraldis.md § Raider Camp.

using Unity.Entities;

/// <summary>
/// A Feraldis Raider Camp — the Age 0 Gatherer's Hut after age-up. Same
/// building entity; this tag switches its behaviour from gathering to
/// producing Plunderers. Its passive supply drip is suppressed
/// (GathererHutIncomeSystem skips tagged huts): Feraldis income comes from
/// what its raiders steal, not from harvesting.
/// </summary>
public struct RaiderCampTag : IComponentData
{
    /// <summary>Seconds until the next Plunderer.</summary>
    public float SpawnTimer;
}
