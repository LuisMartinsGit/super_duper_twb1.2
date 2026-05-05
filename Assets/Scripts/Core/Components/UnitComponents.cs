// UnitComponents.cs
// Components specific to unit entities (soldiers, workers, etc.)
// Place in: Assets/Scripts/Core/Components/Unit/

using Unity.Entities;
using Unity.Mathematics;

// ==================== Unit Classification ====================

public enum UnitClass : byte
{
    Melee = 0,
    Ranged = 1,
    Siege = 2,
    Support = 3,
    Magic = 4,
    Economy = 5,
    Miner = 6,
    Scout = 7
}

/// <summary>
/// Identifies an entity as a unit with a specific class.
/// </summary>
public struct UnitTag : IComponentData
{
    public UnitClass Class;
}

/// <summary>
/// Veteran rank for military units (1..5 per the design spec). Lv 1 is the
/// default for newly-trained units; Lv 2-5 are bought via UnitRankCommand
/// at the cost gates Supplies / Crystal / Veilsteel / Glow respectively.
///
/// Per-level stat scaling (UnitRankConfig.MultiplierFor):
///   Lv 1: 1.00× attack/defense/LOS (base)
///   Lv 2: 1.10× attack/defense
///   Lv 3: 1.15× attack/defense, 1.20× LOS
///   Lv 4: 1.20× attack/defense/LOS + Lv4 HP regen + small AOE on death
///   Lv 5: 1.25× attack/defense/LOS + Lv5 push-back AOE on death + GlowAbility
///
/// Stamp-and-apply pattern: UnitRankSystem reads UnitRankApplied to compute
/// the diff factor (stats[new]/stats[applied]) and updates the stamp.
/// (Audit fix #1)
/// </summary>
public struct UnitRank : IComponentData
{
    public byte Value; // 1..5
}

/// <summary>
/// Last-applied rank stamp for diff scaling.
/// </summary>
public struct UnitRankApplied : IComponentData
{
    public byte Value;
}

/// <summary>
/// Lv 5 GlowAbility — when Active is non-zero, the unit is in the 6-second
/// burst window (fast HP regen mirrored into SpellBuff). Cooldown counts
/// down between casts. Stamped lazily on first activation.
/// </summary>
public struct GlowAbilityState : IComponentData
{
    public float ActiveRemaining;   // Seconds left in the burst (0 = not active)
    public float CooldownRemaining; // Seconds until castable again (0 = ready)
}

/// <summary>
/// Pickup entity dropped when a Lv 2+ veteran unit dies. Carries the
/// cumulative resources the unit consumed during its rank-ups. Any unit
/// of any faction that walks within <see cref="PickupRadius"/> credits
/// the pile to its faction and destroys the pile. Self-despawns after
/// <see cref="Lifetime"/> seconds if not collected.
/// </summary>
public struct UpgradePile : IComponentData
{
    public TheWaningBorder.Core.Cost Drop;
    public float Lifetime;
    public float PickupRadius;
}

// ==================== Unit Type Tags ====================

/// <summary>Marks a unit as capable of constructing buildings.</summary>
public struct CanBuild : IComponentData
{
    public bool Value;
}

/// <summary>Marker tag for Archer units.</summary>
public struct ArcherTag : IComponentData { }

/// <summary>Marker tag for Berserker units (converted from miners at Fiendstone Keep).</summary>
public struct BerserkerTag : IComponentData { }

/// <summary>Marker tag for Cavalry units (mounted). Used for anti-cavalry bonus detection.</summary>
public struct CavalryTag : IComponentData { }

/// <summary>Marker tag for Siege units (anti-structure specialists).</summary>
public struct SiegeTag : IComponentData { }

/// <summary>Marker tag for Spearman units (anti-cavalry bonus).</summary>
public struct SpearmanTag : IComponentData { }

// ==================== Archer State ====================

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

// ==================== Miner Components ====================

/// <summary>Marker tag for Miner units.</summary>
public struct MinerTag : IComponentData { }

/// <summary>
/// Miner work state enumeration.
/// </summary>
public enum MinerWorkState : byte
{
    Idle = 0,
    MovingToDeposit = 1,
    Gathering = 2,
    ReturningToBase = 3
}

/// <summary>
/// Miner behavior and state tracking.
/// </summary>
public struct MinerState : IComponentData
{
    public Entity AssignedDeposit;   // Which deposit to mine
    public int CurrentLoad;          // Resources currently carrying
    public float GatherTimer;        // Time accumulator for gathering
    public MinerWorkState State;     // Current work state
    public byte GatheringResource;   // 0=Iron, 1=Crystal
    public Entity DropoffTarget;     // Hall/GathererHut to return crystal to

    // ---- Tech-modified stats ----
    /// <summary>
    /// Multiplier for gather speed (default 1.0). Higher = faster gathering.
    /// Modified by researched technologies (e.g. ImprovedTools gives 1.15).
    /// Stacks multiplicatively across multiple techs.
    /// </summary>
    public float GatherSpeedMultiplier;

    /// <summary>
    /// Flat bonus added to max carry capacity (default 0).
    /// Modified by researched technologies (e.g. StorageCarts gives +10).
    /// </summary>
    public int CarryCapacityBonus;
}

// ==================== Forge Supply ====================

/// <summary>
/// Assigned to miners supplying a Smelter (Forge) building.
/// Miner picks up iron or crystal from GathererHut/Hall and delivers to forge.
/// </summary>
public struct ForgeSupplyOrder : IComponentData
{
    public Entity Forge;
    public byte ResourceType; // 0=Iron, 1=Crystal
    public byte Phase;        // 0=GoingToPickup, 1=DeliveringToForge
}

// ==================== Population System ====================
// PopulationCost -> TheWaningBorder.Economy.PopulationCost (Economy/FactionPopulation.cs)
// Use: using TheWaningBorder.Economy;

// ==================== Heal Components ====================

/// <summary>
/// Marks a unit that cannot be healed (e.g. Berserker).
/// Litharchs will skip this unit in auto-search and explicit heal commands will fail.
/// </summary>
public struct UnhealableTag : IComponentData { }

/// <summary>
/// Marks a unit that can heal other units (e.g. Litharch).
/// Defined here in global namespace so Unity ECS source generator can find it.
/// </summary>
public struct CanHeal : IComponentData
{
    public float HealRate;     // HP per second
    public float HealRange;    // Max distance to target
}

// ==================== Litharch Components ====================

/// <summary>
/// Marker tag for Litharch healer units.
/// </summary>
public struct LitharchTag : IComponentData { }

/// <summary>
/// Litharch healer state tracking.
/// </summary>
public struct LitharchState : IComponentData
{
    /// <summary>Current unit being healed</summary>
    public Entity HealTarget;

    /// <summary>Time accumulator for healing ticks</summary>
    public float HealTimer;

    /// <summary>1 if actively healing, 0 otherwise</summary>
    public byte IsHealing;

    /// <summary>Timer for searching for new heal targets</summary>
    public float SearchTimer;
}

// ==================== Army System ====================

/// <summary>
/// Tags a unit as belonging to an army group.
/// ArmyId of -1 indicates a scout (unassigned).
/// </summary>
public struct ArmyTag : IComponentData
{
    public int ArmyId;
    public Entity ArmyEntity;  // Add this field
}