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

    /// <summary>Damage type of the projectile (for damage modifier lookup)</summary>
    public DamageType DmgType;
}

/// <summary>
/// Marks a projectile as dealing area-of-effect damage on impact.
/// All enemies within Radius of the impact point take splash damage.
/// </summary>
public struct AOEProjectile : IComponentData
{
    /// <summary>Splash damage radius in world units</summary>
    public float Radius;
}

/// <summary>
/// Added to units (e.g. Catapult) whose projectiles should deal AOE damage.
/// RangedCombatSystem copies this to spawned projectiles as AOEProjectile.
/// </summary>
public struct AOEShooterData : IComponentData
{
    /// <summary>Splash damage radius copied to projectiles</summary>
    public float Radius;
}

/// <summary>
/// Marks a projectile (e.g. Ballista bolt) as piercing — it continues through
/// targets on its trajectory instead of stopping at the first hit.
/// </summary>
public struct PiercingProjectile : IComponentData
{
    /// <summary>How many targets remain before the bolt stops (0 = infinite)</summary>
    public int RemainingPierces;
}