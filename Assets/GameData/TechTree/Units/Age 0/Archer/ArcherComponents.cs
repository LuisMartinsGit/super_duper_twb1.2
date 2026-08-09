// ArcherComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Marker tag for Archer units.</summary>
public struct ArcherTag : IComponentData { }

/// <summary>
/// Archer-specific combat state tracking.
/// </summary>
public struct ArcherState : IComponentData
{
    public float AimTimer;           // Time spent aiming at current target
    public float AimTimeRequired;    // How long to aim before firing
    public float CooldownTimer;      // Time until can fire again
    public float MinRange;           // Minimum attack range
    public float MaxRange;           // Maximum attack range
    public byte IsRetreating;        // 1 if backing away from too-close enemy
    public byte IsFiring;            // 1 when actively firing

    /// <summary>Shot trajectory (see ShotTrajectory): 0 = low parabola
    /// (default archer arc), 1 = flat straight line (crossbow), 2 = high
    /// parabola (longbow). Fed from the unit SO's trajectory field.</summary>
    public byte Trajectory;

    /// <summary>Projectile speed override (m/s). 0 = the combat system's
    /// default arrow speed. Crossbow bolts set this high for a fast, flat
    /// shot. Fed from the unit SO's projectileSpeed field.</summary>
    public float ProjectileSpeed;
}

/// <summary>ArcherState.Trajectory values.</summary>
public static class ShotTrajectory
{
    public const byte Low = 0;   // default capped arc (shortbow)
    public const byte Flat = 1;  // straight line (crossbow)
    public const byte High = 2;  // tall parabola (longbow)

    /// <summary>Parse the unit SO's trajectory string ("low"/"flat"/"high").</summary>
    public static byte Parse(string s) => s switch
    {
        "flat" => Flat,
        "high" => High,
        _ => Low,
    };
}

/// <summary>
/// Arrow projectile physics data.
/// </summary>
public struct ArrowProjectile : IComponentData
{
    public float3 Velocity;      // Current velocity vector
    public float Gravity;        // Gravity constant (typically -9.81)
    public Entity Shooter;       // Who shot it (for friendly fire checking)
    public bool IsParabolic;     // false = horizontal, true = parabolic arc
}
