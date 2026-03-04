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

// ==================== Archer State ====================

/// <summary>
/// Archer-specific combat state tracking.
/// </summary>
public struct ArcherState : IComponentData
{
    public Entity CurrentTarget;
    public float AimTimer;           // Time spent aiming at current target
    public float AimTimeRequired;    // How long to aim before firing
    public float CooldownTimer;      // Time until can fire again
    public float MinRange;           // Minimum attack range
    public float MaxRange;           // Maximum attack range
    public float HeightRangeMod;     // Range bonus/penalty per unit height difference
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

/// <summary>
/// Visual cleanup timer for landed arrows.
/// </summary>
public struct ArrowLanded : IComponentData
{
    public float TimeLeft; // Seconds until arrow visual is removed
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
}

/// <summary>
/// Target mine for a miner unit.
/// </summary>
public struct MiningTarget : IComponentData
{
    public Entity Mine;
    public float3 TargetPosition;
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