// CrucibleComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Alanthor metal processing building. UI label is "Forge".</summary>
public struct SmelterTag : IComponentData { }

/// <summary>Alanthor advanced construction building.</summary>
public struct CrucibleTag : IComponentData { }

/// <summary>
/// Local resource storage for Alanthor Smelter (Forge).
/// Iron and Crystal are delivered by miners and converted into Veilsteel.
/// Every 5 seconds: 5 Iron + 3 Crystal → 1 Veilsteel (added to faction bank).
/// </summary>
public struct ForgeStorage : IComponentData
{
    public int Iron;
    public int Crystal;
    public int MaxIron;            // 100
    public int MaxCrystal;         // 50
    public float ConversionTimer;  // Ticks every 5 seconds
}
