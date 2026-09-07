// CrystallingComponents.cs
// The Crystalling's own vocabulary. Co-located with its factory and its pack
// system, per the entity-folder rule.

using Unity.Entities;

/// <summary>Marks a Crystalling — the only curse unit whose damage scales
/// with how many of its kind stand beside it.</summary>
public struct CrystallingTag : IComponentData { }

/// <summary>
/// How many Crystallings (this one included) stand within
/// <c>BorderSettingsSO.crystallingPackRadius</c>. Refreshed by
/// CrystallingPackSystem on its own cadence; combat reads the BorderBuff the
/// same system derives from it, so this is here for the UI and the log.
/// </summary>
public struct CrystallingPack : IComponentData
{
    public int Size;
}
