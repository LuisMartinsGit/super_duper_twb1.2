// BuildingComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;


/// <summary>
/// Identifies an entity as a building.
/// IsBase = 1 for main bases/outposts.
/// </summary>
public struct BuildingTag : IComponentData
{
    public byte IsBase; // 1 for Hall/main base/outpost
}

/// <summary>
/// Added to buildings when health reaches 0 to delay destruction for collapse animation.
/// BuildingEffectSystem handles the visual collapse; DeathSystem destroys when Timer expires.
/// </summary>
public struct BuildingCollapseState : IComponentData
{
    public float Timer; // Seconds remaining before entity destruction
}

/// <summary>
/// Building construction parameters.
/// </summary>
public struct Buildable : IComponentData
{
    public float BuildTimeSeconds; // Total construction time
}

/// <summary>
/// Active construction progress tracking. <c>LastProgressHp</c> snapshots the
/// HP the construction tick raised the building to last frame; the next tick
/// applies the *delta* to <c>Health.Value</c> so combat damage taken between
/// ticks isn't erased by the construction-set. (task-062 Q-23)
/// </summary>
public struct UnderConstruction : IComponentData
{
    public float Progress; // Current progress (0 to Total)
    public float Total;    // Total required construction work
    public int LastProgressHp; // HP value last assigned by the construction tick (Q-23)
}

/// <summary>
/// Marker added to a building that should self-construct without a builder.
/// AutoConstructionSystem ticks <see cref="UnderConstruction.Progress"/> at
/// 1.0 progress / real second on entities carrying this tag, so the build
/// completes after <c>Total</c> seconds with no idle-builder dispatch.
///
/// Currently used by the per-hub "Build Wall" action: a selected wall hub
/// surfaces an action that places a connected hub (and its segment + wall
/// instances) at no builder cost, with a 30 s self-build timer. The first
/// hub is still placed via a builder and uses the normal
/// BuildingConstructionSystem path.
/// </summary>
public struct AutoConstructTag : IComponentData { }

/// <summary>
/// Tracks the wall-clock time of the most recent damage taken by a building.
/// Written by <c>CombatDamageHelper.TrackLastDamager</c>; read by sect systems
/// that need an "out of combat" signal (e.g. Renewal's auto-repair, which
/// only ticks once SecondsSinceLastDamage &gt;= OutOfCombatThreshold).
/// (task-063 phase 2c)
/// </summary>
public struct BuildingDamageState : IComponentData
{
    /// <summary>Seconds-since-world-start when this building was last damaged.
    /// Matches <c>SystemAPI.Time.ElapsedTime</c>.</summary>
    public double LastDamagedAt;
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

/// <summary>
/// Current training state of a building.
/// </summary>
public struct TrainingState : IComponentData
{
    public byte Busy;       // 0 = idle, 1 = training
    public float Remaining; // Seconds until current unit completes
    public float Total;     // Seconds the current training started with — UI uses
                            // (Total - Remaining) / Total to render the progress
                            // bar below the building's health bar.
}

/// <summary>
/// Queue item for unit training.
/// </summary>
public struct TrainQueueItem : IBufferElementData
{
    public FixedString64Bytes UnitId;
}

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

/// <summary>
/// Marks an entity as a terrain obstacle (forest, rocks) that blocks unit movement.
/// Pushed by UnitSeparationSystem like buildings, but not included in building queries.
/// </summary>
public struct ObstacleTag : IComponentData { }

// ===================================================================
// PARKED — Runai / Feraldis / Sect / Era-2-shared content not yet
// broken into per-culture/per-entity files (mirrors the parked SOs).
// ===================================================================

/// <summary>Siege/advanced unit training building.</summary>
public struct WorkshopTag : IComponentData { }

/// <summary>Resource storage building.</summary>
public struct DepotTag : IComponentData { }

/// <summary>Temple of Ridan — available to ALL cultures at Era 2, houses sect system with 8 expansion slots.</summary>
public struct TempleOfRidanTag : IComponentData { }

/// <summary>Legacy alias — kept for backward compatibility in queries.</summary>
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

/// <summary>Runai expansion base.</summary>
public struct OutpostTag : IComponentData { }

/// <summary>Runai trade building.</summary>
public struct TradeHubTag : IComponentData { }

/// <summary>Runai mobile HQ. Unique per player. +40 pop, dual training queue.</summary>
public struct BazaarTag : IComponentData { }

/// <summary>Runai siege unit training building.</summary>
public struct SiegeWorkshopTag : IComponentData { }

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

/// <summary>Small religious building for sects.</summary>
public struct ChapelSmallTag : IComponentData { }

/// <summary>Large religious building for sects.</summary>
public struct ChapelLargeTag : IComponentData { }

/// <summary>
/// Chapel building tag — generic across all 12 sects.
/// SectId identifies which sect this chapel belongs to (e.g., "Sect_Antiquity"
/// in the task-063 roster). Chapels are the adoption marker + per-sect lever
/// upgrade host. TODO(task-063 phase 2): kept for reuse — Phase 2 chapel
/// creators will tag chapels with this and call SectAdoption.OnChapelCompleted.
/// </summary>
public struct ChapelTag : IComponentData
{
    public FixedString64Bytes SectId;
}

/// <summary>Unique sect-specific building.</summary>
public struct SectUniqueBuildingTag : IComponentData { }

/// <summary>
/// Buffer element on Temple entities tracking each of its 8 chapel build slots.
/// Slots arranged in a circle around the temple (BFME2-style expansion plots).
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
    /// <summary>
    /// 0 = no glow, 1 = a glow is allocated to this shrine. When 1, the
    /// sect's god-power cooldown is halved on each fire (refinement: opt-in
    /// religion, glow allocated to shrines halves recharge time of that
    /// sect's god power, no stacking). Glow units come from the Temple's
    /// GlowStored at allocation time and stay locked until deallocated.
    /// </summary>
    public byte GlowAllocated;
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
