// ProjectileComponents.cs
// Projectile components for ranged combat

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
/// Overrides the default arc height for a single projectile so siege-class
/// shots (Godsplinter, future trebuchet-class units) can lob high parabolic
/// shots across long range. ProjectileSystem reads this when computing the
/// Bezier control point; absent → falls back to the global ArcHeight cap.
/// </summary>
public struct HighArcProjectile : IComponentData
{
    /// <summary>
    /// Peak arc height as a fraction of horizontal travel distance.
    /// 0.3 means the apex sits at 30 % of horizontalDist above the midpoint —
    /// a 60 m shot peaks ~18 m high.
    /// </summary>
    public float ArcFraction;
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

/// <summary>
/// Marks a projectile as a catapult stone (shooter had CatapultTag).
/// ProjectileVisualSystem renders it with the Synty FX_Catapult effect —
/// launch burst, smoking arc in flight, and an impact blast on death.
/// </summary>
public struct CatapultShotTag : IComponentData { }