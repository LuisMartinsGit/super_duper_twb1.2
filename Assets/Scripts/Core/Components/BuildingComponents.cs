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

/// <summary>
/// Tracks the current level of a Temple building (1-4).
/// Level 1 = on build, Level 2-4 = upgrades that advance era.
/// </summary>
public struct TempleLevel : IComponentData
{
    public int Level; // 1-4
}

/// <summary>
/// Active upgrade state for a Temple. Added when upgrade starts, removed on completion.
/// TempleUpgradeSystem ticks Remaining each frame; on completion it sets TempleLevel,
/// updates FactionEra, grants RP, and removes this component.
/// </summary>
public struct TempleUpgradeState : IComponentData
{
    public int TargetLevel;   // Level being upgraded to
    public float Duration;    // Total upgrade time in seconds
    public float Remaining;   // Time left
}

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

/// <summary>
/// Buffer element storing the XZ vertices of a wall enclosure polygon.
/// Added to enclosure income entities by WallEnclosureIncomeSystem so that
/// GathererHutIncomeSystem can do point-in-polygon tests without re-walking the hub graph.
/// </summary>
public struct WallEnclosureVertex : IBufferElementData
{
    public float2 Position;
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

/// <summary>Runai mobile HQ. Unique per player. +40 pop, dual training queue.</summary>
public struct BazaarTag : IComponentData { }

/// <summary>Runai siege unit training building.</summary>
public struct SiegeWorkshopTag : IComponentData { }

// ==================== Era 2 - Alanthor Culture Buildings ====================

/// <summary>Alanthor metal processing building.</summary>
public struct SmelterTag : IComponentData { }

/// <summary>Alanthor advanced construction building.</summary>
public struct CrucibleTag : IComponentData { }

/// <summary>Alanthor ranged defensive tower. Garrison 4.</summary>
public struct WatchTowerTag : IComponentData { }

/// <summary>Alanthor military training building. +8 pop. Trains Sentinel+Crossbowman.</summary>
public struct GarrisonTag : IComponentData { }

/// <summary>Alanthor cavalry training building. Trains Cataphract.</summary>
public struct RoyalStableTag : IComponentData { }

/// <summary>Alanthor siege unit training building. Trains Ballista.</summary>
public struct SiegeYardTag : IComponentData { }

// ==================== Era 2 - Feraldis Culture Buildings ====================

/// <summary>Feraldis hunting building.</summary>
public struct HuntingLodgeTag : IComponentData { }

/// <summary>Feraldis lumber building.</summary>
public struct LoggingStationTag : IComponentData { }

/// <summary>Feraldis weapon forge building.</summary>
public struct WarbrandFoundryTag : IComponentData { }

/// <summary>Feraldis batch training longhouse. Has BatchTrainingTag.</summary>
public struct LonghouseTag : IComponentData { }

/// <summary>Marker for buildings that batch-train units (e.g., Feraldis Longhouse).</summary>
public struct BatchTrainingTag : IComponentData { }

/// <summary>Feraldis ranged defensive totem tower.</summary>
public struct TotemTowerTag : IComponentData { }

/// <summary>Feraldis siege unit training building. Trains Siege Ram.</summary>
public struct FerSiegeYardTag : IComponentData { }

// ==================== Sect Buildings ====================

/// <summary>Small religious building for sects.</summary>
public struct ChapelSmallTag : IComponentData { }

/// <summary>Large religious building for sects.</summary>
public struct ChapelLargeTag : IComponentData { }

/// <summary>
/// Chapel building tag — generic across all 12 sects.
/// SectId identifies which sect this chapel belongs to (e.g., "Sect_Renewal").
/// Chapels train sect-unique units and research sect technologies.
/// </summary>
public struct ChapelTag : IComponentData
{
    public FixedString64Bytes SectId;
}

/// <summary>Unique sect-specific building.</summary>
public struct SectUniqueBuildingTag : IComponentData { }

/// <summary>Unique sect-specific unit type.</summary>
public struct SectUniqueUnitTag : IComponentData { }

// ==================== Temple Chapel Slot System ====================

/// <summary>
/// Buffer element on Temple entities tracking each of its 7 chapel build slots.
/// Slot 0 is at the top (north), arranged clockwise in a circle.
/// </summary>
public struct TempleChapelSlot : IBufferElementData
{
    /// <summary>Chapel entity (Entity.Null if empty or still building).</summary>
    public Entity Chapel;
    /// <summary>Sect ID (empty string if slot is unused).</summary>
    public FixedString64Bytes SectId;
    /// <summary>0 = empty, 1 = building, 2 = complete.</summary>
    public byte State;
    /// <summary>Elapsed build time (increments from 0 to BuildTime).</summary>
    public float BuildProgress;
    /// <summary>Total build time in seconds.</summary>
    public float BuildTime;
}

/// <summary>
/// Added to chapel entities built via temple slots.
/// Links the chapel back to its parent temple and identifies which slot it occupies.
/// Used by cascade destruction: when temple dies, all chapels with TempleOwner die too.
/// </summary>
public struct TempleOwner : IComponentData
{
    /// <summary>The temple entity this chapel belongs to.</summary>
    public Entity Temple;
    /// <summary>Slot index (0-6) in the parent temple's TempleChapelSlot buffer.</summary>
    public int SlotIndex;
}

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
    public int Melee;
    public int Ranged;
    public int Siege;
    public int Magic;
}

/// <summary>
/// Defensive stats for buildings and units.
/// Each field reduces incoming damage of that type via diminishing-returns formula:
///   reduction = defense / (defense + 100)
/// </summary>
public struct Defense : IComponentData
{
    public int Melee;
    public int Ranged;
    public int Siege;
    public int Magic;
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

// ==================== Building Size (Grid-Aligned) ====================

/// <summary>
/// Grid-aligned rectangular size for buildings.
/// Width (X-axis) and Height (Z-axis) in whole grid cells (1m each).
/// Max dimension: 5. Minimum: 1.
/// Buildings with this component use AABB collision instead of circle collision.
/// The Radius component is kept for backward compatibility (set to max(Width,Height)/2).
/// </summary>
public struct BuildingSize : IComponentData
{
    /// <summary>Width in grid cells along the X axis (1-5).</summary>
    public int Width;
    /// <summary>Height in grid cells along the Z axis (1-5).</summary>
    public int Height;
}

// ==================== Terrain Obstacles ====================

/// <summary>
/// Marks an entity as a terrain obstacle (forest, rocks) that blocks unit movement.
/// Pushed by UnitSeparationSystem like buildings, but not included in building queries.
/// </summary>
public struct ObstacleTag : IComponentData { }

// ==================== Age-Up Timer ====================

/// <summary>
/// Active age-up timer on a Hall entity.
/// While present, the Hall is transitioning to Era 2.
/// Training is blocked and a progress bar is shown in the UI.
/// Removed by AgeUpSystem when Remaining reaches 0.
/// </summary>
public struct AgeUpState : IComponentData
{
    public byte Culture;      // Selected culture (Cultures enum value)
    public float Duration;    // Total time for age-up
    public float Remaining;   // Time left
}

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