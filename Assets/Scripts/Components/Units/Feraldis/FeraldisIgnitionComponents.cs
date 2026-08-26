// FeraldisIgnitionComponents.cs
// ECS components lifted out of FeraldisIgnition.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;

/// <summary>
/// Declares that this unit's shots ignite bloodsoaked ground. Copied onto
/// the projectile at fire time so a shot outlives its shooter.
/// </summary>
public struct IgnitesBlood : IComponentData
{
    public float Radius;
    public float DamagePerSecond;
    public float Duration;
}

/// <summary>Projectile visual marker — renders as the Synty catapult fire
/// effect scaled way down (a hurled fireball, not a boulder).</summary>
public struct FirethrowerShotTag : IComponentData { }
