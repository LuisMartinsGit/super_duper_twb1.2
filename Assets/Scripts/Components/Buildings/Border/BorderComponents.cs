// BorderComponents.cs
// Components specific to the Veilstone enemy faction
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;
using Unity.Mathematics;

// ==================== Veilstone Entity Tags ====================

/// <summary>Marks an entity as belonging to the Border faction.</summary>
public struct BorderTag : IComponentData { }

/// <summary>Identifies main Veilstone hives.</summary>
public struct BorderMainNodeTag : IComponentData { }

// (Sub-node types removed 2026-08-11 — the curse has exactly two structures:
// the Large node (BorderMainNodeTag, the corner well / verb objective) and
// the small node (SmallNodeTag in VeilComponents.cs, the pocket anchor).)

// ==================== Veilstone Node Systems ====================

/// <summary>
/// Attached to any Veilstone node (main or sub) that spreads the border.
/// </summary>
public struct BorderNode : IComponentData
{
    public float SpreadRadius;      // World radius (territory radius)
    public byte Enabled;
}

/// <summary>
/// Runtime state for veilstone spread progression.
/// Tracks the expanding ring wavefront per-node (used by BorderSpreadSystem).
/// Separated from BorderNode to keep config fields distinct from runtime state.
/// </summary>
public struct BorderSpreadState : IComponentData
{
    public float TickTimer;         // Accumulated time since last spread tick
    public float CurrentRingRadius; // Current outer edge of the spread wavefront
}

/// <summary>
/// Per-node level derived from BorderSpreadState.CurrentRingRadius.
/// Level 1 (radius 0-5):  Fast spread, only Crystallings — easy to farm.
/// Level 2 (radius 5-10): Moderate spread, Veilstingers unlocked.
/// Level 3 (radius 10+):  Slow spread, Godsplinters unlocked — dangerous.
/// </summary>
public struct BorderNodeLevel : IComponentData
{
    public int Value; // 1, 2, or 3

    /// <summary>Compute level from current spread radius.</summary>
    public static int FromRadius(float radius)
    {
        if (radius >= 10f) return 3;
        if (radius >= 5f) return 2;
        return 1;
    }
}

// ==================== Veilstone AI State ====================

// ==================== Veilstone Unit / Resource ====================

/// <summary>Marks an entity as a Border faction unit.</summary>
public struct BorderUnitTag : IComponentData { }

/// <summary>
/// Veilstone resource cost for building or spawning.
/// </summary>
public struct VeilstoneWorth : IComponentData
{
    public int BuildCost;
}

// ==================== Border Ground ====================

/// <summary>
/// Links a border ground entity back to its parent veilstone node.
/// </summary>
public struct OwnerNode : IComponentData
{
    public Entity Value;
}

// ==================== VeilstoneOutcropping Components ====================

/// <summary>
/// Marker tag for creature outcroppings (mineable for veilstone).
/// </summary>
public struct VeilstoneOutcroppingTag : IComponentData { }

/// <summary>
/// Marks a veilstone main node OR sub-node as a "secondary" border location.
/// Legacy: these were spawned by the retired patch-conversion mechanic
/// (the potential-veilstone pool and its distribution were removed — veilstone
/// is now a fixed map resource, exactly like iron). The tag's behaviours remain
/// live for any tagged entity: 1 Religion Point (FactionReligionPoints) to
/// the acting faction on pacification / conversion / destruction (instead of
/// the normal Glow pickup), and NO respawn after destruction
/// (NodeStateReversionSystem skips the Destroyed→Active regrowth).
/// </summary>
public struct SecondaryBorderLocationTag : IComponentData { }

/// <summary>
/// Veilstone resource state for a veilstone-node entity (legacy "VeilstoneOutcropping" name —
/// the entity is a static veilstone node that exists until fully mined).
/// Behaves exactly like an iron deposit: fixed amount seeded at map start,
/// no decay, no refill, gone when depleted. Adjacent nodes merge on creation
/// via <see cref="TheWaningBorder.Entities.VeilstoneOutcropping.CreateOrMerge"/>.
/// </summary>
public struct VeilstoneOutcroppingState : IComponentData
{
    /// <summary>Veilstone remaining in this node.</summary>
    public int RemainingVeilstone;

    /// <summary>Initial veilstone amount (for UI display).</summary>
    public int MaxVeilstone;

    /// <summary>1 = fully harvested, 0 = still has veilstone.</summary>
    public byte Depleted;
}

// ==================== Veilstone Unit States ====================

/// <summary>
/// State for the Veilstinger veilstone unit - dual-target ranged attacker.
/// </summary>
public struct VeilstingerState : IComponentData
{
    public Entity Target1;
    public Entity Target2;
    public float AimTimer;
    public float AimTimeRequired;
    public float CooldownTimer;
    public float MinRange;
    public float MaxRange;
    /// <summary>Seconds between shots, fed from the unit's SO (attackCooldown).
    /// 0 = fall back to VeilstingerCombatSystem's built-in constant.</summary>
    public float FireCooldown;
    public byte IsFiring;
    /// <summary>0 = next shot from left gun, 1 = next shot from right gun. Toggles on each fire.</summary>
    public byte NextGun;
}

/// <summary>
/// State for the Godsplinter veilstone unit - siege unit with laser and siege modes.
/// </summary>
public struct GodsplinterState : IComponentData
{
    public float LaserCooldownTimer;
    public float SiegeCooldownTimer;
    public float SiegeRange;
    public float LaserRange;
    /// <summary>Seconds between laser volleys, fed from the unit's SO (attackCooldown).
    /// 0 = fall back to BorderConstants.GodsplinterFireCooldown.</summary>
    public float LaserCooldown;
    /// <summary>Seconds between close-range siege attacks, fed from the unit's SO
    /// (siegeCooldown). 0 = the system's built-in constant.</summary>
    public float SiegeCooldown;
    /// <summary>Splash radius of the AoE bombard, fed from the unit's SO (aoeRadius).
    /// 0 = BorderConstants.GodsplinterAoeRadius.</summary>
    public float AoeRadius;
    public int LaserMaxTargets;
    public byte IsSieging;
}

// ==================== Veilstone Sub-Node Auras ====================

// ==================== Border Ground Recession ====================

// ==================== Laser Projectile ====================

/// <summary>
/// Marks a projectile as a laser beam instead of an arrow.
/// ProjectileVisualSystem uses this to render a laser visual
/// (glowing beam) instead of the default arrow model.
/// </summary>
public struct LaserProjectileTag : IComponentData { }

/// <summary>
/// Marks a projectile as a Veilstinger missile — picks the small arcane
/// missile visual and triggers the impact-explosion VFX on destruction.
/// Veilstinger projectiles use the Bezier arrow path (arched, auto-hit),
/// not the laser straight-line path.
/// </summary>
public struct VeilstingerProjectileTag : IComponentData { }

/// <summary>
/// Marks a projectile as a Godsplinter laser — picks the mega arcane
/// missile visual. Coexists with LaserProjectileTag (Godsplinter uses
/// the straight-line laser path; this tag just swaps the visual).
/// </summary>
public struct GodsplinterProjectileTag : IComponentData { }

// ==================== Veilstone Buff / Debuff ====================

/// <summary>
/// Applied to veilstone-allied units within an Enforcement aura radius.
/// Combat systems use these values to boost damage, defense, and speed.
/// Removed when the unit leaves the aura radius.
/// </summary>
public struct BorderBuff : IComponentData
{
    public float DefBonus;
    public float AttBonus;
    public float SpeedBonus;
}

/// <summary>
/// Applied to enemy (non-White) units within a Suppression aura radius.
/// Combat systems use these values to penalise damage, defense, and speed.
/// Removed when the unit leaves the aura radius.
/// </summary>
public struct BorderDebuff : IComponentData
{
    public float DefPenalty;
    public float AttPenalty;
    public float SpeedPenalty;
}

public struct BorderExtinctionState : IComponentData { public byte IsExtinct; public float RespawnTimer; public byte HasEverExisted; }

/// <summary>
/// Marks a border unit as part of an active wave. While present, the AI
/// keeps re-issuing DesiredDestination toward Target so the unit resumes
/// the march after killing anything that gets in the way. Cleared when
/// the unit reaches the target zone.
/// </summary>
public struct BorderWaveOrder : IComponentData
{
    public float3 Target;
    public int WaveNumber;
}

// ==================== Per-node army model (BorderArmyAISystem) ====================

/// <summary>Which of a node's two army slots a border unit belongs to.</summary>
public enum BorderArmyRoleType : byte
{
    Defend = 0,
    Attack = 1,
}

/// <summary>
/// Tags a border unit with the slot it was trained into. Combines with the
/// unit's <see cref="OwnerNode"/> (its owning main node) to place it in exactly
/// one node-and-role group for BorderHordeSystem (attackers march; defenders hold).
/// </summary>
public struct BorderArmyRole : IComponentData
{
    public BorderArmyRoleType Role;
}

