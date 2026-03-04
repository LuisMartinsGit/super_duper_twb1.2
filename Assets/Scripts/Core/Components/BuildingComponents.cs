// BuildingComponents.cs
// Components specific to building entities
// Place in: Assets/Scripts/Core/Components/Building/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ==================== Building Identity ====================

/// <summary>
/// Identifies an entity as a building.
/// IsBase = 1 for main bases/outposts.
/// </summary>
public struct BuildingTag : IComponentData
{
    public byte IsBase; // 1 for Hall/main base/outpost
}

// ==================== Era 1 Building Tags ====================
/// <summary>Main base/hall building marker.</summary>
public struct HallTag : IComponentData { }
/// <summary>Resource collection building.</summary>
public struct GathererHutTag : IComponentData { }
/// <summary>Tracks farm placement order for priority-based income (first-come-first-served).</summary>
public struct FarmBuildOrder : IComponentData { public int Value; }

/// <summary>Population housing building.</summary>
public struct HutTag : IComponentData { }

/// <summary>Military training building.</summary>
public struct BarracksTag : IComponentData { }

/// <summary>Siege/advanced unit training building.</summary>
public struct WorkshopTag : IComponentData { }

/// <summary>Resource storage building.</summary>
public struct DepotTag : IComponentData { }

/// <summary>Religious/support building.</summary>
public struct TempleTag : IComponentData { }

/// <summary>Defensive wall segment (generic tag for all wall entities).</summary>
public struct WallTag : IComponentData { }

/// <summary>Marks a wall hub (connection point / tower between wall segments).</summary>
public struct WallHubTag : IComponentData { }

/// <summary>Marks a wall segment (the connector between two hubs).</summary>
public struct WallSegmentTag : IComponentData { }

/// <summary>Links a wall segment to its two hub endpoints.</summary>
public struct WallConnection : IComponentData
{
    public Entity HubA;
    public Entity HubB;
}

/// <summary>
/// Buffer element tracking connections from a wall hub to other hubs.
/// Each entry records the connected hub and the wall segment entity between them.
/// </summary>
public struct WallHubLink : IBufferElementData
{
    public Entity ConnectedHub;
    public Entity Segment;
}

/// <summary>
/// Marks a virtual entity that provides supplies income from an enclosed wall polygon.
/// Created/destroyed dynamically by WallEnclosureIncomeSystem.
/// </summary>
public struct WallEnclosureIncomeTag : IComponentData
{
    public byte FactionIndex;
}

/// <summary>Resource vault building.</summary>
public struct VaultTag : IComponentData { }

/// <summary>Feraldis defensive keep building.</summary>
public struct FiendstoneKeepTag : IComponentData { }

/// <summary>Marker for the 3 mutually exclusive choice buildings (Shrine, Vault, Keep). Build limit: 1.</summary>
public struct ChoiceBuildingTag : IComponentData { }

// ==================== Era 2 - Runai Culture Buildings ====================

/// <summary>Runai expansion base.</summary>
public struct OutpostTag : IComponentData { }

/// <summary>Runai trade building.</summary>
public struct TradeHubTag : IComponentData { }

// ==================== Era 2 - Alanthor Culture Buildings ====================

/// <summary>Alanthor metal processing building.</summary>
public struct SmelterTag : IComponentData { }

/// <summary>Alanthor advanced construction building.</summary>
public struct CrucibleTag : IComponentData { }

// ==================== Era 2 - Feraldis Culture Buildings ====================

/// <summary>Feraldis hunting building.</summary>
public struct HuntingLodgeTag : IComponentData { }

/// <summary>Feraldis lumber building.</summary>
public struct LoggingStationTag : IComponentData { }

/// <summary>Feraldis weapon forge building.</summary>
public struct WarbrandFoundryTag : IComponentData { }

// ==================== Sect Buildings ====================

/// <summary>Small religious building for sects.</summary>
public struct ChapelSmallTag : IComponentData { }

/// <summary>Large religious building for sects.</summary>
public struct ChapelLargeTag : IComponentData { }

/// <summary>Unique sect-specific building.</summary>
public struct SectUniqueBuildingTag : IComponentData { }

/// <summary>Unique sect-specific unit type.</summary>
public struct SectUniqueUnitTag : IComponentData { }

// ==================== Construction System ====================

/// <summary>
/// Building construction parameters.
/// </summary>
public struct Buildable : IComponentData
{
    public float BuildTimeSeconds; // Total construction time
}

/// <summary>
/// Active construction progress tracking.
/// </summary>
public struct UnderConstruction : IComponentData
{
    public float Progress; // Current progress (0 to Total)
    public float Total;    // Total required construction work
}

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

/// <summary>
/// Stores defense values to apply when construction completes.
/// </summary>
public struct DeferredDefense : IComponentData
{
    public float Melee;
    public float Ranged;
    public float Siege;
    public float Magic;
}

/// <summary>
/// Defensive stats for completed buildings.
/// </summary>
public struct Defense : IComponentData
{
    public float Melee;
    public float Ranged;
    public float Siege;
    public float Magic;
}

// ==================== Training System ====================

/// <summary>
/// Current training state of a building.
/// </summary>
public struct TrainingState : IComponentData
{
    public byte Busy;       // 0 = idle, 1 = training
    public float Remaining; // Seconds until current unit completes
}

/// <summary>
/// Queue item for unit training.
/// </summary>
public struct TrainQueueItem : IBufferElementData
{
    public FixedString64Bytes UnitId;
}

// Legacy production system (consider deprecating in favor of TrainingState)
public struct ProductionQueue : IBufferElementData
{
    public UnitClass Class;
}

public struct ProductionState : IComponentData
{
    public float Timer;        // Time left to finish current item (<=0 means idle)
    public float BaseTime;     // Base production time per unit
    public UnitClass CurrentClass;
}

// ==================== Building Combat ====================

/// <summary>
/// Ranged attack capability for buildings (towers, halls, keeps).
/// Buildings auto-target enemies within range.
/// </summary>
public struct BuildingRangedAttack : IComponentData
{
    public float Range;
    public int Damage;
    public float Cooldown;
    public float Timer;
    public int MaxTargets; // How many enemies can be targeted simultaneously
}

// ==================== Vault System ====================

/// <summary>
/// Resource storage with compound interest for Vault of Almiérra.
/// Only one resource type at a time. Locked after deposit/withdraw.
/// </summary>
public struct VaultStorage : IComponentData
{
    /// <summary>0=None, 1=Supplies, 2=Iron, 3=Crystal, 4=Veilsteel, 5=Glow</summary>
    public int ResourceType;
    public float StoredAmount;
    public float InterestRate;   // Per minute (0.03 = 3%)
    public float LockTimer;      // Remaining lock seconds (0 = unlocked)
    public float LockDuration;   // Seconds to lock after deposit/withdraw (180 = 3 min)
}

// ==================== Terrain Obstacles ====================

/// <summary>
/// Marks an entity as a terrain obstacle (forest, rocks) that blocks unit movement.
/// Pushed by UnitSeparationSystem like buildings, but not included in building queries.
/// </summary>
public struct ObstacleTag : IComponentData { }

// ==================== Self-Destruct ====================

/// <summary>
/// Countdown timer for automatic building destruction with resource refund.
/// Added to GathererHuts when player chooses Alanthor culture.
/// </summary>
public struct SelfDestructTimer : IComponentData
{
    public float TimeRemaining;  // Seconds until destruction
    public byte RefundPaid;      // 1 = resources already refunded
}

// ==================== Forge / Smelter Storage ====================

/// <summary>
/// Local resource storage for Alanthor Smelter (Forge).
/// Iron and Crystal are delivered by miners and converted into Veilsteel.
/// Every 5 seconds: 5 Iron + 3 Crystal → 1 Veilsteel (added to faction bank).
/// </summary>
public struct ForgeStorage : IComponentData
{
    public int Iron;
    public int Crystal;
    public int MaxIron;            // 100
    public int MaxCrystal;         // 50
    public float ConversionTimer;  // Ticks every 5 seconds
}

// ==================== Economy Components ====================
// PopulationProvider -> TheWaningBorder.Economy.PopulationProvider (Economy/FactionPopulation.cs)
// SuppliesIncome -> TheWaningBorder.Economy.SuppliesIncome (Economy/FactionResources.cs)
// Use: using TheWaningBorder.Economy;