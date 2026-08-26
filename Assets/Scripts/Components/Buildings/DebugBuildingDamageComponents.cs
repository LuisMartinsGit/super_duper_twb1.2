// DebugBuildingDamageComponents.cs
// ECS components lifted out of DebugBuildingDamageSystem.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;

/// <summary>
/// Marks a building to be steadily damaged by <c>DebugBuildingDamageSystem</c>.
/// Only the BuildingDamageTest scenario stamps this, so the system is inert in
/// normal play. <see cref="Accumulator"/> carries the fractional HP between
/// frames (Health is integer).
/// </summary>
public struct DebugBuildingDamageTarget : IComponentData
{
    public float Accumulator;
}
