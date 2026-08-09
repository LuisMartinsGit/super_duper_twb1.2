// WorkerComponents.cs
// Build + mining capability components for the unified Worker unit.
// All types are in the global namespace (single assembly), so location
// is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Build order assigned to a builder unit.
/// </summary>
public struct BuildOrder : IComponentData
{
    public Entity Site; // Building entity being constructed
}

/// <summary>
/// Repair order assigned to a builder unit.
/// Builder walks to damaged building and repairs it, consuming resources.
/// Cost = (missingHP / maxHP) * originalBuildCost * 1.2 penalty.
/// </summary>
public struct RepairOrder : IComponentData
{
    public Entity Site;          // Building entity being repaired
    public byte CostPaid;        // 1 = resources already deducted, 0 = not yet
    public int TargetHP;         // HP to repair to (max HP)
    public int StartHP;          // HP when repair started (for cost calculation)
}

/// <summary>Marks a unit as capable of constructing buildings.</summary>
public struct CanBuild : IComponentData
{
    public bool Value;
}

/// <summary>Marker tag for units that can mine (every Worker carries it).</summary>
public struct MinerTag : IComponentData { }

/// <summary>
/// Miner work state enumeration. Mined resources are credited straight to
/// the faction bank on every gather tick — there is no carry/dropoff loop.
/// </summary>
public enum MinerWorkState : byte
{
    Idle = 0,
    MovingToDeposit = 1,
    Gathering = 2
}

/// <summary>
/// Mining behavior and state tracking.
/// </summary>
public struct MinerState : IComponentData
{
    public Entity AssignedDeposit;   // Which deposit to mine
    public float GatherTimer;        // Time accumulator for gathering
    public MinerWorkState State;     // Current work state
    public byte GatheringResource;   // 0=Iron, 1=Veilstone, 2=Veilsteel

    // Last known position of a depleted node — used to auto-find a same-type
    // replacement within AutoFindRadius after the original entity is destroyed.
    public float3 LastDepositPos;

    // ---- Tech-modified stats ----
    /// <summary>
    /// Multiplier for gather speed (default 1.0). Higher = faster gathering.
    /// Modified by researched technologies (e.g. StoneTools gives 1.15).
    /// Stacks multiplicatively across multiple techs.
    /// </summary>
    public float GatherSpeedMultiplier;
}
