// VaultOfAlmierraComponents.cs
// All types are in the global namespace (single assembly), so location is
// organizational only.

using Unity.Entities;

/// <summary>Resource vault building.</summary>
public struct VaultTag : IComponentData { }

/// <summary>
/// THE VAULT'S FOUR BARS (docs/Design/Age_0.md § Vault of Almiérra,
/// Unification decision 46, 2026-11-10). Resources paid in fill the bars in
/// order, any resource one for one, up to the Vault SO's vaultBarCapacity for
/// that bar (each harder to fill than the last). Every filled SUB-BAR raises
/// the income of the resource its bar boosts by its share of the SO's
/// vaultBarBonus (vaultSubBars per bar), so a bar pays as it fills. A withdrawal returns exactly what was paid in and empties
/// every bar. Replaces the capped simple interest of decision 41.
/// </summary>
public struct VaultStorage : IComponentData
{
    public const int Bars = 4;

    /// <summary>Paid into each bar.</summary>
    public int Fill0, Fill1, Fill2, Fill3;
    /// <summary>The resource each bar boosts (1 supplies, 2 iron, 3 veilstone;
    /// 0 = the bar has not started). Chosen when the bar starts filling,
    /// fixed until a withdrawal empties it.</summary>
    /// <summary>What a withdrawal pays back, by the resource it was paid in.</summary>
    public int PaidSupplies, PaidIron, PaidVeilstone;

    public int Fill(int i) => i switch { 0 => Fill0, 1 => Fill1, 2 => Fill2, _ => Fill3 };

    public void SetFill(int i, int v)
    {
        switch (i) { case 0: Fill0 = v; break; case 1: Fill1 = v; break; case 2: Fill2 = v; break; default: Fill3 = v; break; }
    }


    public int Paid => PaidSupplies + PaidIron + PaidVeilstone;
}
