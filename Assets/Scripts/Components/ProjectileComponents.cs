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

    /// <summary>1 = fly from Start's own height (a building's muzzle); 0 = from
    /// chest height above the terrain under Start.</summary>
    public byte KeepStartHeight;
}

/// <summary>
/// One shot of a building's volley waiting for its random departure time
/// (docs/Design/Combat_Pacing.md § Building volleys). A plain entity with no
/// transform or projectile data, so nothing draws or hits with it;
/// BuildingCombatSystem turns it into a projectile when it falls due.
/// </summary>
public struct PendingBuildingShot : IComponentData
{
    public float DueTime;
    public Entity Shooter;
    public Entity Target;
    /// <summary>Where to aim if the target is gone by the time this departs.</summary>
    public float3 LastTargetPos;
    public Faction Faction;
    public int Damage;
    public DamageType DmgType;
    public byte IsLaser;
    public byte IsBallistaBolt;
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
/// Bezier control point; absent → falls back to ProjectileSystem's default fraction.
/// </summary>
public struct HighArcProjectile : IComponentData
{
    /// <summary>
    /// Bezier control point height as a fraction of horizontal travel distance.
    /// The flight peaks at half of it: 0.3 puts the apex at 15 % of horizontalDist
    /// above the midpoint, so a 60 m shot peaks ~9 m high.
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

/// <summary>
/// A BALLISTA bolt — from the mobile Ballista, a wall-mounted Emplaced
/// Ballista, a Keep's Ballista emplacement or a Watch Tower's L3 bolt.
/// ProjectileVisualSystem renders it as the large procedural bolt
/// (BallistaBoltVisual) instead of a scaled-up arrow.
/// </summary>
public struct BallistaBoltTag : IComponentData { }

/// <summary>
/// A TREBUCHET stone — from the mobile Trebuchet, a wall-mounted Emplaced
/// Trebuchet or a Keep's Trebuchet emplacement. Rendered per entity as the
/// Synty siege rock (SM_Wep_Rock_01), so the stone IS the damage carrier.
/// </summary>
public struct TrebuchetStoneTag : IComponentData { }

/// <summary>Shooter marker: this unit fires BallistaBoltTag shots.</summary>
public struct BallistaShooterTag : IComponentData { }

/// <summary>Shooter marker: this unit fires TrebuchetStoneTag shots.</summary>
public struct TrebuchetShooterTag : IComponentData { }