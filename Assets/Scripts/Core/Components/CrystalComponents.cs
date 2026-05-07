// CrystalComponents.cs
// Components specific to the Crystal enemy faction
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;
using Unity.Mathematics;

// ==================== Crystal Entity Tags ====================

/// <summary>Marks an entity as belonging to the Crystal faction.</summary>
public struct CrystalTag : IComponentData { }

/// <summary>Marks ground as corrupted by Crystal spread.</summary>
public struct CursedGroundTag : IComponentData { }

/// <summary>Identifies main Crystal hives.</summary>
public struct CrystalMainNodeTag : IComponentData { }

/// <summary>Sub-node types for Crystal structures.</summary>
public enum CrystalSubNodeType : byte
{
    Resource = 0,
    Enforcement = 1,
    Suppression = 2,
    Restoration = 3,
    Turret = 4
}

/// <summary>Identifies Crystal sub-nodes with their type.</summary>
public struct CrystalSubNodeTag : IComponentData
{
    public CrystalSubNodeType Type;
}

// ==================== Crystal Node Systems ====================

/// <summary>
/// Attached to any Crystal node (main or sub) that spreads the curse.
/// </summary>
public struct CrystalNode : IComponentData
{
    public float SpreadRadius;      // World radius (territory radius)
    public byte Enabled;
}

/// <summary>
/// Runtime state for crystal spread progression.
/// Tracks the expanding ring wavefront per-node (used by CrystalSpreadSystem).
/// Separated from CrystalNode to keep config fields distinct from runtime state.
/// </summary>
public struct CrystalSpreadState : IComponentData
{
    public float TickTimer;         // Accumulated time since last spread tick
    public float CurrentRingRadius; // Current outer edge of the spread wavefront
}

/// <summary>
/// Per-node level derived from CrystalSpreadState.CurrentRingRadius.
/// Level 1 (radius 0-5):  Fast spread, only Crystallings — easy to farm.
/// Level 2 (radius 5-10): Moderate spread, Veilstingers unlocked.
/// Level 3 (radius 10+):  Slow spread, Godsplinters unlocked — dangerous.
/// </summary>
public struct CrystalNodeLevel : IComponentData
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

// ==================== Crystal AI State ====================

/// <summary>
/// Tracks the Crystal faction AI state for a main node.
/// Currently drives BuildTimer (next-build cooldown) and ExpansionTimer
/// (post-build cooldown before another expansion attempt). HarassTimer and
/// UnitSpawnTimer were removed in task-062 Q-24 — they were written but
/// never read. Phase is legacy but kept for backwards compat.
/// </summary>
public struct CrystalAIState : IComponentData
{
    public float BuildTimer;
    public float ExpansionTimer;
    public byte Phase; // Kept for backwards compat but driven by CrystalNodeLevel
}

// ==================== Crystal Unit / Resource ====================

/// <summary>Marks an entity as a Crystal faction unit.</summary>
public struct CrystalUnitTag : IComponentData { }

/// <summary>
/// Crystal resource cost for building or spawning.
/// </summary>
public struct CrystalResourceValue : IComponentData
{
    public int BuildCost;
}

// ==================== Cursed Ground ====================

/// <summary>
/// Damage-over-time applied by cursed ground to non-crystal units.
/// Attached to each cursed ground entity.
/// </summary>
public struct CursedGroundDPS : IComponentData
{
    public float DamagePerSecond; // DPS to non-crystal units standing on this tile
    public float EffectRadius;    // Effect radius of this ground patch
}

/// <summary>
/// Links a cursed ground entity back to its parent crystal node.
/// </summary>
public struct OwnerNode : IComponentData
{
    public Entity Value;
}

// ==================== Cadaver Components ====================

/// <summary>
/// Marker tag for creature cadavers (mineable for crystal).
/// </summary>
public struct CadaverTag : IComponentData { }

/// <summary>
/// Crystal resource state for a crystal-node entity (legacy "Cadaver" name —
/// the type is named after creature deaths but the entity is a static crystal
/// node that exists until fully mined). Nodes do NOT decay; they persist
/// until depletion. Adjacent nodes merge on creation via
/// <see cref="TheWaningBorder.Entities.Cadaver.CreateOrMerge"/>.
/// </summary>
public struct CadaverState : IComponentData
{
    /// <summary>Crystal remaining in this node.</summary>
    public int RemainingCrystal;

    /// <summary>Initial crystal amount (for UI display).</summary>
    public int MaxCrystal;

    /// <summary>1 = fully harvested, 0 = still has crystal.</summary>
    public byte Depleted;
}

// ==================== Crystal Unit States ====================

/// <summary>
/// State for the Veilstinger crystal unit - dual-target ranged attacker.
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
    public byte IsFiring;
}

/// <summary>
/// State for the Godsplinter crystal unit - siege unit with laser and siege modes.
/// </summary>
public struct GodsplinterState : IComponentData
{
    public float LaserCooldownTimer;
    public float SiegeCooldownTimer;
    public float SiegeRange;
    public float LaserRange;
    public int LaserMaxTargets;
    public byte IsSieging;
}

// ==================== Crystal Sub-Node Auras ====================

/// <summary>
/// Enforcement aura: buffs nearby crystal allies.
/// </summary>
public struct EnforcementAura : IComponentData
{
    public float Radius;
    public float DefBonus;
    public float AttBonus;
    public float SpeedBonus;
}

/// <summary>
/// Suppression aura: debuffs nearby enemies.
/// </summary>
public struct SuppressionAura : IComponentData
{
    public float Radius;
    public float DefPenalty;
    public float AttPenalty;
    public float SpeedPenalty;
}

/// <summary>
/// Restoration aura: heals nearby crystal allies over time.
/// </summary>
public struct RestorationAura : IComponentData
{
    public float Radius;
    public float HealPerSecond;
    public float HealTimer;
}

// ==================== Cursed Ground Recession ====================

/// <summary>
/// Applied to cursed ground tiles whose owner node has been destroyed.
/// The tile will fade out and be destroyed over time.
/// </summary>
public struct CursedGroundReceding : IComponentData
{
    /// <summary>Seconds remaining before this tile is destroyed.</summary>
    public float TimeRemaining;
}

// ==================== Laser Projectile ====================

/// <summary>
/// Marks a projectile as a laser beam instead of an arrow.
/// ProjectileVisualSystem uses this to render a laser visual
/// (glowing beam) instead of the default arrow model.
/// </summary>
public struct LaserProjectileTag : IComponentData { }

// ==================== Crystal Buff / Debuff ====================

/// <summary>
/// Applied to crystal-allied units within an Enforcement aura radius.
/// Combat systems use these values to boost damage, defense, and speed.
/// Removed when the unit leaves the aura radius.
/// </summary>
public struct CrystalBuff : IComponentData
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
public struct CrystalDebuff : IComponentData
{
    public float DefPenalty;
    public float AttPenalty;
    public float SpeedPenalty;
}

public struct CrystalCadaverLifetime : IComponentData { public float TimeRemaining; }
public struct CrystalExtinctionState : IComponentData { public byte IsExtinct; public float RespawnTimer; public byte HasEverExisted; }
public struct CrystalWaveState : IComponentData { public float WaveTimer; public float WaveInterval; public int WaveNumber; }
public struct CrystalTrainingState : IComponentData { public byte TrainingUnitType; public float TimeRemaining; public float TotalTime; }
public struct CrystalAutoBuild : IComponentData { public float TimeRemaining; public float TotalTime; }
