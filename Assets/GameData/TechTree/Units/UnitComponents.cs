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
public struct DeathAnimationState : IComponentData
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

/// <summary>
/// Veteran rank for military units (1..5 per the design spec). Lv 1 is the
/// default for newly-trained units; Lv 2-5 are bought via UnitRankCommand
/// at the cost gates Supplies / Veilstone / Veilsteel / Glow respectively.
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
/// return-to-guard passes skip it, so builders never wander off to pick
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

/// <summary>
/// Tags a unit as belonging to an army group.
/// ArmyId of -1 indicates a scout (unassigned).
/// </summary>
public struct ArmyTag : IComponentData
{
    public int ArmyId;
    public Entity ArmyEntity;  // Add this field
}

// ===================================================================
// PARKED — Runai / Feraldis / Sect / Era-2-shared content not yet
// broken into per-culture/per-entity files (mirrors the parked SOs).
// ===================================================================

/// <summary>Unique sect-specific unit type.</summary>
public struct SectUniqueUnitTag : IComponentData { }

/// <summary>Marker tag for Berserker units (converted from miners at Fiendstone Keep).</summary>
public struct BerserkerTag : IComponentData { }
