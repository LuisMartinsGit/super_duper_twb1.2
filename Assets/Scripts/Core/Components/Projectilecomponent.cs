// ProjectileComponents.cs
// Projectile components for ranged combat
// Location: Assets/Scripts/Core/Components/Combat/ProjectileComponents.cs

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Core projectile data for arrow flight and damage calculation.
/// Used by ProjectileSystem for Bezier curve trajectories.
/// </summary>
public struct Projectile : IComponentData
{
    /// <summary>Starting position of the projectile</summary>
    public float3 Start;
    
    /// <summary>Target end position (may update if target moves)</summary>
    public float3 End;
    
    /// <summary>Game time when projectile was spawned</summary>
    public double StartTime;
    
    /// <summary>Expected flight duration in seconds</summary>
    public float FlightTime;
    
    /// <summary>Damage to deal on hit</summary>
    public int Damage;
    
    /// <summary>Target entity (for homing/tracking)</summary>
    public Entity Target;
    
    /// <summary>Faction that fired the projectile (for friendly fire)</summary>
    public Faction Faction;
}