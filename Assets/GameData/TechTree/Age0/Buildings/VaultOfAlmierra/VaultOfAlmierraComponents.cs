// VaultOfAlmierraComponents.cs
// All types are in the global namespace (single assembly), so location is
// organizational only.

using Unity.Entities;

/// <summary>Resource vault building.</summary>
public struct VaultTag : IComponentData { }

/// <summary>
/// THE VAULT'S FOUR BARS (docs/Design/Age_0.md § Vault of Almiérra; decisions
/// 46, 60 and 85). Each bar stores iron or veilstone (the last bar supplies
/// too) — whichever is first paid into it — up to its vaultBarCapacity at the Vault's level, and a FULL
/// bar pays its vaultBarIncomePerMinute of that resource (VaultBarSystem). A
/// withdrawal returns exactly what was paid in and empties every bar.
/// </summary>
public struct VaultStorage : IComponentData
{
    public const int Bars = 4;

    /// <summary>Paid into each bar.</summary>
    public int Fill0, Fill1, Fill2, Fill3;
    /// <summary>The resource each bar holds (1 supplies, 2 iron, 3 veilstone; 0 = the
    /// bar has not started). Set by its first deposit, kept until a
    /// withdrawal empties it.</summary>
    public int Res0, Res1, Res2, Res3;
    /// <summary>What a withdrawal pays back, by the resource it was paid in.</summary>
    public int PaidSupplies, PaidIron, PaidVeilstone;
    /// <summary>The trickle owed, in sixtieths (VaultBarSystem).</summary>
    public int CarrySupplies, CarryIron, CarryVeilstone;

    public int Fill(int i) => i switch { 0 => Fill0, 1 => Fill1, 2 => Fill2, _ => Fill3 };

    public void SetFill(int i, int v)
    {
        switch (i) { case 0: Fill0 = v; break; case 1: Fill1 = v; break; case 2: Fill2 = v; break; default: Fill3 = v; break; }
    }

    public int Res(int i) => i switch { 0 => Res0, 1 => Res1, 2 => Res2, _ => Res3 };

    public void SetRes(int i, int v)
    {
        switch (i) { case 0: Res0 = v; break; case 1: Res1 = v; break; case 2: Res2 = v; break; default: Res3 = v; break; }
    }

    public int Paid => PaidSupplies + PaidIron + PaidVeilstone;
}
