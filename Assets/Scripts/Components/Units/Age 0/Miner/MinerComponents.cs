// MinerComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

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

    // Last known position of a depleted node — used to auto-find a same-type
    // replacement within AutoFindRadius after the original entity is destroyed.
    public float3 LastDepositPos;

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
