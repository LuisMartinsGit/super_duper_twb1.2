// EquipmentComponents.cs
// Per-faction equipment tier system. Faction researches equipment tiers
// (Base → Iron → Veilstone → Veilsteel → Glow per spec §4) and the upgrade
// applies to every unit of the relevant class. Stacks multiplicatively
// with UnitRank (the per-unit veterancy system) — a Lv3 unit with Veilstone
// equipment gets BOTH multipliers.
//
// Spec §4.1: tier progression. Per-tier magical effects (shield bar at
// Veilstone, duplicate squad at Veilsteel, revive at Glow) are wired by
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
    Veilstone = 2,
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

// ==================== Per-tier magical effects (spec §4.2-§4.4) ====================

/// <summary>
/// Veilstone+ tier shield bar — a second HP layer that absorbs damage
/// before it touches Health. Regenerates out-of-combat. Spec §4.2:
/// "Veilstone tier: Shield bar (second HP layer) + better stats" — applied
/// universally across unit classes for MVP (the spec illustrates only
/// spearman example).
///
/// Cap scales with tier: Veilstone < Veilsteel < Glow.
/// </summary>
public struct ShieldBar : IComponentData
{
    /// <summary>Current shield HP (0 = depleted, regenerating).</summary>
    public int Current;

    /// <summary>Max shield HP at the unit's current tier.</summary>
    public int Max;

    /// <summary>
    /// Health.Value snapshot from the previous frame. Used to detect new
    /// damage and route it through the shield. Mirrors the stamp pattern
    /// used by UnitRankSystem.
    /// </summary>
    public int LastObservedHealth;

    /// <summary>Seconds since this shield last absorbed damage (drives regen gate).</summary>
    public float RegenDelayTimer;
}

/// <summary>
/// Per-unit cooldown for the Glow-tier on-death revive (spec §4.2 Glow
/// tier: "Revive lost battalion members on cooldown"). When TimeRemaining
/// is 0, the next time the unit's Health drops to 0 it pops back at half
/// max HP and the cooldown resets to GlowReviveCooldownSec. While
/// TimeRemaining > 0, lethal damage falls through to DeathSystem +
/// GlowWeaponDropSystem normally.
/// </summary>
public struct GlowReviveCooldown : IComponentData
{
    public float TimeRemaining;
}

/// <summary>
/// Spec §4.3 Veilstone-tier siege: "Aura granting shields to nearby allies."
/// Stamped on siege units that have at least Veilstone tier. The aura system
/// (SiegeShieldAuraSystem) reads this radius + boost amount each tick and
/// adds AuraShieldBoost to friendly units in range.
/// </summary>
public struct SiegeShieldAura : IComponentData
{
    public float Radius;
    public int BonusShield;
}

/// <summary>
/// Transient boost added to a unit standing inside a SiegeShieldAura.
/// Drives a temporary ShieldBar.Max bump while inside the radius; removed
/// on the next tick if the unit leaves. The ShieldBarSystem honors the
/// new Max immediately (clamping Current up to it on the next regen tick).
/// </summary>
public struct AuraShieldBoost : IComponentData
{
    public int Amount;
}

/// <summary>
/// Spec §4.4 Veilstone-tier hero: "Cooldown-based phase shield."
/// Auto-stamped on Magic/Support-class units at Veilstone+ tier. While
/// ChargeReadyTimer == 0, the next damage hit is partially absorbed
/// (reduction % below). After absorbing, the timer resets to BaseCooldown.
/// </summary>
public struct HeroPhaseShield : IComponentData
{
    public float ChargeReadyTimer;    // 0 = ready to absorb; > 0 = recharging
    public float BaseCooldown;        // seconds between absorbs
    public float ReductionPercent;    // 0.0 - 1.0 (e.g. 0.5 = absorb 50% of one hit)
    public int LastObservedHealth;    // for damage detection
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
