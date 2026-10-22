// CleanseComponents.cs
// The Cleansing Ward (Territory_Claims.md §6.9.7, 2026-10-22): the building a
// Litharch's Cleanse plants. CleanseWard creates it, CleanseWardSystem pulses
// it, CleanseWardVfx / CleanseSfx show and sound each pulse. (It replaced the
// 2026-10-18 CleanseChannel the Litharch used to carry and stand still for.)

using Unity.Entities;

/// <summary>Marks a Cleansing Ward. Curse units prefer it as a target; it
/// never decays on cursed ground.</summary>
public struct CleanseWardTag : IComponentData { }

/// <summary>A ward's circle: cleared out to <see cref="Radius"/> on every
/// pulse, growing by <see cref="AreaPerPulse"/> m² a pulse to
/// <see cref="MaxRadius"/>; <see cref="Falloff"/> m beyond it clear by chance.
/// <see cref="PulseCount"/> is what the presentation plays its aura on.</summary>
public struct CleanseWardState : IComponentData
{
    public float Radius;
    public float MaxRadius;
    public float AreaPerPulse;
    public float Falloff;
    public float PulseTimer;
    public int PulseCount;
}
