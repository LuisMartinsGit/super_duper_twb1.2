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
/// Forge (Smelter) storage — ALL LEGACY. The Forge generated veilsteel from
/// nothing until 2026-10-01; veilsteel is now made only by trade
/// (docs/Design/Veilstone_Economy.md — the Trading Outpost's forge mode), so
/// nothing reads or writes these fields. Kept so archetypes and queries keyed
/// on ForgeStorage stay valid.
/// </summary>
public struct ForgeStorage : IComponentData
{
    public int Iron;               // legacy, unused (always 0)
    public int Veilstone;          // legacy, unused (always 0)
    public int MaxIron;            // legacy, unused
    public int MaxVeilstone;       // legacy, unused
    public float ConversionTimer;  // legacy, unused
}
