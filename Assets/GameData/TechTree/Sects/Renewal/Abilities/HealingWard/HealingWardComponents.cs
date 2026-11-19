// HealingWardComponents.cs
// The Healing Ward (docs/Design/Sects.md, Renewal, 2026-11-18): the one-cell
// building a Litharch plants once the Sect of Renewal's research is bought.
// HealingWard creates it, HealingWardSystem pulses it, RenewalVfx keeps its healing aura looping. (It replaced the
// 2026-10 Field Hospital, a two-minute tent.)

using Unity.Entities;

/// <summary>Marks a Healing Ward.</summary>
public struct HealingWardTag : IComponentData { }

