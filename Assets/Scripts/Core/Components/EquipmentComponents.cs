// EquipmentComponents.cs
// Per-faction equipment tier system. Faction researches equipment tiers
// (Base → Iron → Crystal → Veilsteel → Glow per spec §4) and the upgrade
// applies to every unit of the relevant class. Stacks multiplicatively
// with UnitRank (the per-unit veterancy system) — a Lv3 unit with Crystal
// equipment gets BOTH multipliers.
//
// Spec §4.1: tier progression. Per-tier magical effects (shield bar at
// Crystal, duplicate squad at Veilsteel, revive at Glow) are wired by
// separate ability systems and tag-checked off the tier value.
//
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;

// ==================== Equipment Tiers ====================

/// <summary>
/// Equipment tier (spec §4.1). Each upgrade is a faction-wide research
/// applied per unit class. The Glow tier triggers the unique drop-on-death
/// behavior (only Glow-tier equipment drops, spec §4.5).
/// </summary>
public enum EquipmentTier : byte
{
    Base = 0,
    Iron = 1,
    Crystal = 2,
    Veilsteel = 3,
    Glow = 4,
}

/// <summary>
/// Per-faction equipment tier matrix. One entity per faction holds the
/// current tier for each unit class. Researched via
/// CommandRouter.IssueEquipmentUpgrade.
///
/// Five class slots cover the spec's three example unit-types (infantry,
/// siege, heroes) plus ranged and magic for completeness. Workers /
/// scouts don't get equipment tiers — they're economy roles, not
/// battlefield equipment paths.
/// </summary>
public struct FactionEquipmentTier : IComponentData
{
    public EquipmentTier Melee;
    public EquipmentTier Ranged;
    public EquipmentTier Siege;
    public EquipmentTier Magic;
    public EquipmentTier Support;

    /// <summary>
    /// Look up the current tier for a unit class. Returns Base for classes
    /// that don't carry equipment (Economy, Miner, Scout).
    /// </summary>
    public EquipmentTier Get(UnitClass cls) => cls switch
    {
        UnitClass.Melee   => Melee,
        UnitClass.Ranged  => Ranged,
        UnitClass.Siege   => Siege,
        UnitClass.Magic   => Magic,
        UnitClass.Support => Support,
        _ => EquipmentTier.Base,
    };
}

/// <summary>
/// Last-applied equipment tier stamp on a unit. Mirrors the UnitRankApplied
/// stamp-and-apply pattern: EquipmentTierSystem reads this to compute the
/// stat-diff factor when the faction's tier for the unit's class moves up,
/// then updates the stamp.
/// </summary>
public struct UnitEquipmentApplied : IComponentData
{
    public EquipmentTier Value;
}

/// <summary>
/// Per-unit override of the equipment tier. When present, this takes
/// precedence over the faction-wide FactionEquipmentTier value for the
/// unit's class. Set when a unit attunes to a dropped Glow weapon (spec
/// §4.5) — a Glow weapon claim upgrades the carrier above their faction's
/// general research level.
/// </summary>
public struct UnitTierOverride : IComponentData
{
    public EquipmentTier Value;
}

// ==================== Glow Weapon Drop (spec §4.5) ====================

/// <summary>
/// Marker for a free-floating Glow weapon dropped when a Glow-tier unit
/// dies. Only Glow-tier equipment drops on death (earlier tiers do not).
/// </summary>
public struct GlowWeaponTag : IComponentData { }

/// <summary>
/// Per-weapon state. Pickup window counts down; attunement requires a
/// qualifying unit (Veilsteel-tier or higher) to stand within
/// GlowWeaponClaimRadius for GlowWeaponAttunementTime uninterrupted.
/// If the current attuner moves out of range or dies, the progress
/// resets and another in-range qualifier can take over.
/// </summary>
public struct GlowWeaponState : IComponentData
{
    /// <summary>Unit class this weapon is for (e.g. Melee, Ranged, Siege, Magic).</summary>
    public UnitClass Class;

    /// <summary>Seconds remaining before despawn if uncarried.</summary>
    public float TimeRemaining;

    /// <summary>Entity currently attuning to this weapon (Entity.Null when no attuner).</summary>
    public Unity.Entities.Entity Attuner;

    /// <summary>Seconds the current attuner has spent within radius.</summary>
    public float AttunementProgress;
}
