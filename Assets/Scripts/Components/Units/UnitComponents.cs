// UnitComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Added to units when health reaches 0 to delay destruction for death animation.
/// DeathSystem adds this instead of immediately destroying the entity.
/// After Timer expires, entity is destroyed.
/// </summary>
// Enableable, pre-added disabled at spawn — see the archetype-churn note on
// Target. WithNone<DeathAnimationState> queries treat disabled as absent, so
// every "skip corpses" query keeps its meaning unchanged.
public struct DeathAnimationState : IComponentData, IEnableableComponent
{
    public float Timer; // Seconds remaining before entity destruction
}

public enum UnitClass : byte
{
    Melee = 0,
    Ranged = 1,
    Siege = 2,
    Support = 3,
    Magic = 4,
    Economy = 5,
    Worker = 6,
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
/// The exact unit id string the entity was created with ("Archer",
/// "Alanthor_Cataphract", ...). Stamped by UnitFactory.Create so the generic
/// tech-effects engine can match "unit:X" targets against live entities.
/// PresentationId deliberately cannot serve here — PIDs select the VISUAL and
/// are shared across unit types.
/// </summary>
public struct UnitTypeId : IComponentData
{
    public FixedString64Bytes Value;
}

/// <summary>Marker tag for Cavalry units (mounted). Used for anti-cavalry bonus detection.</summary>
public struct CavalryTag : IComponentData { }

/// <summary>Marker tag for Siege units (anti-structure specialists).</summary>
public struct SiegeTag : IComponentData { }

/// <summary>Marker tag for catapult-style siege units (Runai_Catapult,
/// Alanthor_Catapult): RangedCombatSystem gives their shots CatapultShotTag
/// (FX_Catapult visuals, AOE burst, no ballista-style pierce).</summary>
public struct CatapultTag : IComponentData { }

// PopulationCost -> TheWaningBorder.Economy.PopulationCost (Economy/FactionPopulation.cs)
// Use: using TheWaningBorder.Economy;


/// <summary>
/// Marks a worker as a NON-COMBATANT. TargetingSystem's auto-acquire and
/// return-to-guard passes skip it, so workers never wander off to pick
/// fights on their own.
///
/// This is a tag rather than the old `WithNone&lt;CanBuild&gt;` filter because
/// Feraldis Workers are light infantry that ALSO build: they keep CanBuild
/// and lose this tag, so they fight like any other unit
/// (FeraldisCultureRetrofitSystem).
/// </summary>
public struct PassiveWorkerTag : IComponentData { }

/// <summary>
/// Marks a unit that cannot be healed (e.g. Berserker).
/// Litharchs will skip this unit in auto-search and explicit heal commands will fail.
/// </summary>
public struct UnhealableTag : IComponentData { }

// ===================================================================
// PARKED — Runai / Feraldis / Sect / Era-2-shared content not yet
// broken into per-culture/per-entity files (mirrors the parked SOs).
// ===================================================================

/// <summary>Unique sect-specific unit type.</summary>
public struct SectUniqueUnitTag : IComponentData { }

/// <summary>Marker tag for Berserker units (converted from workers at Fiendstone Keep).</summary>
public struct BerserkerTag : IComponentData { }
