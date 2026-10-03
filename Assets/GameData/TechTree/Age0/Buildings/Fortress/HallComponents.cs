// HallComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Main base/hall building marker.</summary>
public struct HallTag : IComponentData { }

/// <summary>
/// Active age-up timer on a Hall entity.
/// While present, the Hall is transitioning to Era 2.
/// Training is blocked and a progress bar is shown in the UI.
/// Removed by AgeUpSystem when Remaining reaches 0.
/// </summary>
public struct AgeUpState : IComponentData
{
    public byte Culture;      // Selected culture (Cultures enum value)
    public float Duration;    // Total time for age-up
    public float Remaining;   // Time left
}
