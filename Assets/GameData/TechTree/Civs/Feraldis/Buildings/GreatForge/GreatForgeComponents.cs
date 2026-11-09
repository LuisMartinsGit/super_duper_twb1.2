// GreatForgeComponents.cs
// docs/Design/Age_1_Feraldis.md §4 (2026-10-12).

using Unity.Entities;

/// <summary>The Feraldis Great Forge — one per faction.</summary>
public struct GreatForgeTag : IComponentData { }

/// <summary>What a bay is making. Hold = idle (spends and earns nothing).</summary>
public enum ForgeRecipe : byte
{
    Hold = 0,
    Soulforge = 1,   // slaves → veilstone
    Smelt = 2,       // iron → veilstone (the Veilsteel bay until 2026-11-09, decision 42)
    ArmsExport = 3,  // veilstone → iron
}

/// <summary>
/// One bay of the Great Forge. Up to GreatForgeSystem.asset maxBays, each
/// bought at a rising price, each with its own recipe and level (1-3).
/// </summary>
public struct ForgeBay : IBufferElementData
{
    public ForgeRecipe Recipe;
    public byte Level;
    /// <summary>Fractional progress carried between cycles (inputs consumed / outputs owed).</summary>
    public float Progress;
}
