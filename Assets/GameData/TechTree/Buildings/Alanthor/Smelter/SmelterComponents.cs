// SmelterComponents.cs
// Components for the Alanthor Smelter (Forge, id Alanthor_Smelter).
// The Crucible was deleted (calculator consolidation 2026-08) — the Smelter
// absorbed its veilsteel-engine role via the Lv1-3 upgrade ladder. All types
// are in the global namespace (single assembly), so location is
// organizational only.

using Unity.Entities;

/// <summary>Alanthor metal processing building. UI label is "Forge".</summary>
public struct SmelterTag : IComponentData { }

/// <summary>
/// Forge (Smelter) production state. The Forge passively generates veilsteel
/// (ForgeConversionSystem: 1/2/3 per 10 s at building Lv1/2/3, no inputs) —
/// only ConversionTimer is live. The Iron/Veilstone storage fields are
/// legacy from the removed supply-chain conversion and stay at 0; kept so
/// existing archetypes and queries keyed on ForgeStorage stay valid.
/// </summary>
public struct ForgeStorage : IComponentData
{
    public int Iron;               // legacy, unused (always 0)
    public int Veilstone;          // legacy, unused (always 0)
    public int MaxIron;            // legacy, unused
    public int MaxVeilstone;       // legacy, unused
    public float ConversionTimer;  // accumulates toward ForgeConversionSystem.GenerationInterval
}
