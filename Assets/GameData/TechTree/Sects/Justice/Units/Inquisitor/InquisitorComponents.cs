// InquisitorComponents.cs
// ECS components for the Justice sect's Inquisitor. (Antiquity's sect-unit
// components live in AntiquityComponents.cs.) Global namespace, matching
// the project's component convention.

using Unity.Entities;

/// <summary>Marker: this unit is a Justice Inquisitor (support caster).</summary>
public struct InquisitorTag : IComponentData { }

/// <summary>
/// Inquisitor cleanse timer. When CleanseCooldown reaches 0 and a nearby
/// ally carries a debuff (CodexFrozen today), InquisitorCleanseSystem
/// strips it and resets the timer.
/// </summary>
public struct InquisitorState : IComponentData
{
    public float CleanseCooldown;
}
