// SmelterComponents.cs
// Components for the Runai Veilsteel Foundry, which carries SmelterTag. The
// Alanthor Smelter (Forge) that the tag was named for is removed
// (2026-10-03). All types are in the global namespace (single assembly), so
// location is organizational only.

using Unity.Entities;

/// <summary>Marks the Runai Veilsteel Foundry (BuildingIds maps it to
/// "Runai_VeilsteelFoundry").</summary>
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
