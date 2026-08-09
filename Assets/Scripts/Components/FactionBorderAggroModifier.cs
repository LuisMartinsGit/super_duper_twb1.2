// File: Assets/Scripts/Core/Components/FactionBorderAggroModifier.cs
// task-078 Phase 1: per-faction modifier on Border wave aggro probability.
// Runai's Runai_BorderNeutrality research at Trader's Hall reduces this by 0.20
// per tier (clamped to 1.0 max reduction).

using Unity.Entities;

/// <summary>
/// Per-faction component on the bank entity: the multiplicative reduction the
/// Border wave target picker applies to the random roll for this
/// faction's units. 0.0 = baseline aggro (no reduction); 0.6 = 60% less likely
/// to be picked. Set by ResearchSystem when Runai_BorderNeutrality lands.
/// </summary>
public struct FactionBorderAggroModifier : IComponentData
{
    /// <summary>Fractional reduction in aggro probability (0.0 = none, 0.6 = -60%).</summary>
    public float Reduction;
}
