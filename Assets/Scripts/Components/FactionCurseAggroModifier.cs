// File: Assets/Scripts/Core/Components/FactionCurseAggroModifier.cs
// task-078 Phase 1: per-faction modifier on Crystal-Curse wave aggro probability.
// Runai's Runai_CrystalNeutrality research at Trader's Hall reduces this by 0.20
// per tier (clamped to 1.0 max reduction).

using Unity.Entities;

/// <summary>
/// Per-faction component on the bank entity: the multiplicative reduction the
/// Crystal-Curse wave target picker applies to the random roll for this
/// faction's units. 0.0 = baseline aggro (no reduction); 0.6 = 60% less likely
/// to be picked. Set by ResearchSystem when Runai_CrystalNeutrality lands.
/// </summary>
public struct FactionCurseAggroModifier : IComponentData
{
    /// <summary>Fractional reduction in aggro probability (0.0 = none, 0.6 = -60%).</summary>
    public float Reduction;
}
