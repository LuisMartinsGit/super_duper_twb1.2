// LitharchComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Marks a unit that can heal other units (e.g. Litharch).
/// Defined here in global namespace so Unity ECS source generator can find it.
/// </summary>
public struct CanHeal : IComponentData
{
    public float HealRate;     // HP per second
    public float HealRange;    // Max distance to target
}

/// <summary>
/// Marker tag for Litharch healer units.
/// </summary>
public struct LitharchTag : IComponentData { }

/// <summary>
/// Litharch healer state tracking.
/// </summary>
public struct LitharchState : IComponentData
{
    /// <summary>Current unit being healed</summary>
    public Entity HealTarget;

    /// <summary>Time accumulator for healing ticks</summary>
    public float HealTimer;

    /// <summary>1 if actively healing, 0 otherwise</summary>
    public byte IsHealing;

    /// <summary>Timer for searching for new heal targets</summary>
    public float SearchTimer;

    /// <summary>1 when HealTarget came from an explicit heal ORDER (player or
    /// AI right-click) rather than the idle auto-search. An ordered heal is
    /// pursued on every stance, Hold included (docs/Design/Stances.md §5).</summary>
    public byte Ordered;

    /// <summary>Counts down to the next nearby-enemy check.</summary>
    public float ThreatTimer;

    /// <summary>1 while the Litharch is stepping away from an enemy that got
    /// too close — healing does not move it until the step ends, i.e. until
    /// it reaches <see cref="StepTarget"/> or <see cref="StepTimer"/> runs out.</summary>
    public byte SteppingAway;

    /// <summary>Where the running step-away is headed.</summary>
    public float3 StepTarget;

    /// <summary>Time left (s) on the running step-away — a safety bound for a
    /// step whose destination is never reached (blocked, pushed).</summary>
    public float StepTimer;
}
