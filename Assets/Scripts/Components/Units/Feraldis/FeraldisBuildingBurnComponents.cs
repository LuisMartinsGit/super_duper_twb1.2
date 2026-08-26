// FeraldisBuildingBurnComponents.cs
// ECS components lifted out of FeraldisBuildingBurn.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;

/// <summary>Declares that this unit's hits leave enemy BUILDINGS burning.</summary>
public struct InflictsBuildingBurn : IComponentData
{
    public float DamagePerSecond;
    public float Duration;
}

/// <summary>An enemy structure currently burning from a Raider strike.</summary>
public struct BuildingBurn : IComponentData
{
    public float DamagePerSecond;
    public float Remaining;
    public Faction Source;
    public float Accumulator;
}
