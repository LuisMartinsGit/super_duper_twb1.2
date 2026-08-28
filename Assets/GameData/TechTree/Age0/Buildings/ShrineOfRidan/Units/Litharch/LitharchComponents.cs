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
}
