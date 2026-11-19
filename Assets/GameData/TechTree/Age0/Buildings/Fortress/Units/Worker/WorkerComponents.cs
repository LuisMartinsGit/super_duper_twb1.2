// WorkerComponents.cs
// Build + mining capability components for the unified Worker unit.
// All types are in the global namespace (single assembly), so location
// is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Build order assigned to a worker unit.
/// </summary>
public struct BuildOrder : IComponentData, IEnableableComponent
{
    public Entity Site; // Building entity being constructed
    /// <summary>Where this builder stands to work (BuilderStand). A fresh
    /// order (<c>new BuildOrder { Site = x }</c>) starts with none.</summary>
    public WorkStand Stand;
}

/// <summary>
/// A builder's own spot beside the footprint it works on (2026-11-18,
/// docs/Design/Age_0.md § Worker): one slot on a ring just outside the
/// footprint's edge, latched for the job so the walk target never moves, and
/// given up for the next free slot when the worker makes no progress toward
/// it. Carried inside <see cref="BuildOrder"/> and <see cref="RepairOrder"/>;
/// read and written only by <c>BuilderStand</c>.
/// </summary>
public struct WorkStand
{
    /// <summary>The spot (world), valid while <see cref="HasSpot"/> is 1.</summary>
    public float3 Spot;
    public byte HasSpot;
    /// <summary>Which slot of the site's ring <see cref="Spot"/> is.</summary>
    public byte SlotIndex;
    /// <summary>Slots already given up on for this job (bit per slot).</summary>
    public ulong Tried;
    /// <summary>Closest the worker has come to <see cref="Spot"/>.</summary>
    public float BestDist;
    /// <summary>Seconds near the spot without getting closer.</summary>
    public float Stall;
}

/// <summary>
/// A build the worker should start once it finishes its current one.
///
/// BuildCommand and BuildOrder are both SINGLE components, so every new build
/// assignment overwrote the previous one: placing several buildings in a row
/// left the worker with only the LAST, and every earlier foundation sat
/// untouched forever. The LOS auto-chain hid this whenever the sites happened
/// to be close together, which is why it read as "only far-apart builds are
/// ignored" — proximity was doing the queueing by accident.
///
/// A buffer makes the plan explicit and order-preserving.
/// </summary>
[UnityEngine.Scripting.Preserve]
public struct QueuedBuildSite : IBufferElementData
{
    public Unity.Collections.FixedString64Bytes BuildingId;
    public Unity.Mathematics.float3 Position;
    /// <summary>The already-created site entity, or Entity.Null if the
    /// building has not been placed yet (multiplayer lockstep ordering).</summary>
    public Entity TargetBuilding;
}

/// <summary>
/// Repair order assigned to a worker unit.
/// Worker walks to damaged building and repairs it, consuming resources.
/// Cost = (missingHP / maxHP) * originalBuildCost * 1.2 penalty.
/// </summary>
public struct RepairOrder : IComponentData, IEnableableComponent
{
    public Entity Site;          // Building entity being repaired
    public byte CostPaid;        // unused since repairs are paid per HP (2026-11-17)
    public int TargetHP;         // HP to repair to (max HP)
    public int StartHP;          // HP when repair started (for cost calculation)
    public float Carry;          // fraction of an HP owed, kept between ticks
    public float CostCarryS, CostCarryI, CostCarryV;   // fractions of a resource owed for repaired HP
    public WorkStand Stand;      // where this repairer stands (BuilderStand)
}

/// <summary>Marks a unit as capable of constructing buildings, with its work
/// rates from its unit SO (Worker.asset: buildSpeed, repairSpeed).</summary>
public struct CanBuild : IComponentData
{
    public bool Value;
    /// <summary>Construction progress (seconds of build time) this unit adds
    /// per second.</summary>
    public float BuildRate;
    /// <summary>HP this unit restores per second when repairing.</summary>
    public float RepairRate;
}

/// <summary>Marker tag for units that can mine (every Worker carries it).</summary>
public struct WorkerTag : IComponentData { }

/// <summary>
/// Worker activity enumeration. Mined resources are credited straight to
/// the faction bank on every gather tick — there is no carry/dropoff loop.
/// </summary>
public enum WorkerActivity : byte
{
    Idle = 0,
    MovingToDeposit = 1,
    Gathering = 2
}

/// <summary>
/// Mining behavior and state tracking.
/// </summary>
public struct WorkerState : IComponentData
{
    public Entity AssignedDeposit;   // Which deposit to mine
    public float GatherTimer;        // Time accumulator for gathering
    public WorkerActivity State;     // Current work state
    public byte GatheringResource;   // 0=Iron, 1=Veilstone

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
