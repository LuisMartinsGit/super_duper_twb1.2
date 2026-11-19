// AbilityCooldownScale.cs — a researched multiplier on every ability cooldown
// one unit is charged (decision 77: the Litharch's Swift Rites). Written by the
// research "AbilityCooldown" stat (TechEffectSystem.Generic), read by
// AbilityLifecycleSystem.SetCooldown. Global namespace, like the other ECS
// components, so the research system in Scripts/ reaches it without a using.

using Unity.Entities;

public struct AbilityCooldownScale : IComponentData
{
    /// <summary>Cooldown multiplier (1 = unchanged, 0.75 = a quarter shorter).</summary>
    public float Mult;
}
