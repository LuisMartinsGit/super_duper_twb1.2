// BatteringRamComponents.cs
// All types are in the global namespace (single assembly), so location is
// organizational only — co-located with the unit that introduced them.

using Unity.Entities;

/// <summary>
/// Marker: this attacker may ONLY fight buildings (walls, towers, keeps...).
/// TargetingSystem never auto-acquires a non-building target for holders, and
/// MeleeCombatSystem refuses to swing at — and drops — a non-building target
/// even when force-ordered. First user: the Alanthor Battering Ram.
/// </summary>
public struct BuildingsOnlyAttacker : IComponentData { }
