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
    Veilsteel = 2,   // veilstone + iron → veilsteel
    ArmsExport = 3,  // veilsteel → iron
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
