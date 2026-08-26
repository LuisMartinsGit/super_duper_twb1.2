// File: Assets/GameData/TechTree/Units/Feraldis/FeraldisUnitComponents.cs
// Culture-wide (set-level) components for Feraldis units.
// Canon: docs/Design/Age_1_Feraldis.md — "Blood, Frenzy & War Totems".
//
// Feraldis is the fire-and-blood culture: its units frenzy on bloodsoaked
// ground, its mid-tier bleeds whole clumps of enemies at once, its elite
// refuses to fall for five more seconds, and its special unit converts
// enemy fire into the blood everything else feeds on.
//
// Per-entity tags stay here (rather than in <Entity>/<Entity>Components.cs)
// because every one of them is read by the culture-wide systems that sit
// beside this file.

using Unity.Entities;

// ==================== Culture membership ====================

/// <summary>
/// Stamped on every unit created by a Feraldis unit factory. The single
/// eligibility test for <see cref="BloodFrenzy"/> — cheaper and more
/// explicit than re-deriving the owner's culture from its Hall each pulse.
/// </summary>
public struct FeraldisUnitTag : IComponentData { }

// ==================== Frenzy on blood ====================

/// <summary>
/// Active blood-frenzy on a Feraldis unit standing on bloodsoaked ground.
/// Stamped and refreshed by BloodFrenzySystem while the unit is over
/// blood; it lingers briefly after the unit steps off so a fight at the
/// ragged edge of a pool does not strobe the buff on and off.
///
/// Consumed by MeleeCombatSystem / RangedCombatSystem via
/// CombatDamageHelper.GetFrenzyDamageMult / GetFrenzyCooldownMult.
/// </summary>
public struct BloodFrenzy : IComponentData
{
    /// <summary>Seconds of buff left once the unit leaves the blood.</summary>
    public float Remaining;
}

// ==================== Bleeding (Bloodletter) ====================

/// <summary>
/// Bleeding damage-over-time inflicted by the Bloodletter's whirl. Ticked
/// by BleedingSystem, which routes damage through Health (never a direct
/// destroy) so the unit-death contract holds and the kill still splats
/// blood normally.
///
/// Refreshed — not stacked — on each new hit: re-applying resets
/// Remaining and keeps the higher DPS.
/// </summary>
public struct Bleeding : IComponentData
{
    public float DamagePerSecond;
    public float Remaining;

    /// <summary>Faction credited with a bleed-out kill (pillage / stats).</summary>
    public Faction Source;

    /// <summary>Sub-second accumulator so fractional DPS is not lost to
    /// integer Health writes.</summary>
    public float Accumulator;
}

// ==================== Per-unit markers ====================

/// <summary>Feraldis line infantry — the Age 0 Spearman chassis traded
/// toward aggression (less HP, more attack).</summary>
public struct FeraldisSpearmanTag : IComponentData { }

/// <summary>
/// A Worker converted to its Feraldis form: build-only (mining stripped),
/// but with real light-infantry stats AND real combat behaviour — the
/// retrofit strips <see cref="PassiveWorkerTag"/>, so unlike every other
/// culture's builders these auto-acquire, hold a guard point and fight.
/// Also the "already retrofitted" latch for FeraldisCultureRetrofitSystem.
/// </summary>
public struct FeraldisWorkerTag : IComponentData { }

/// <summary>
/// A surplus Feraldis Worker that has been sent to war (the age-up reward:
/// a free, weak rush army). Latch so the AI conscripts each worker once
/// rather than re-ordering the whole crew every think tick.
/// </summary>
public struct ConscriptedTag : IComponentData { }

/// <summary>
/// A Scout converted to its Feraldis form: ordinary vision instead of the
/// scout-sight ramp, paired with an eagle that carries the real sight.
/// </summary>
public struct FeraldisScoutTag : IComponentData { }

/// <summary>
/// Bloodletter — fast, fragile, and it does not single-target: every
/// swing hits everything hostile inside <see cref="WhirlAttack.Radius"/>
/// and leaves <see cref="Bleeding"/> on all of it.
/// </summary>
public struct BloodletterTag : IComponentData { }

/// <summary>
/// The whirl itself. Present on the Bloodletter; BloodletterWhirlSystem
/// converts each of its normal melee swings into an area strike.
/// </summary>
public struct WhirlAttack : IComponentData
{
    public float Radius;
    public float BleedDamagePerSecond;
    public float BleedDuration;
}

/// <summary>
/// Suicidal — runs at the enemy soaking ranged fire, then detonates.
/// It has no normal attack; SuicidalDetonationSystem arms it on arrival
/// and fires the blast when it dies from ANY cause, so enemy fire is
/// converted into a Feraldis blood pool either way.
/// </summary>
public struct SuicidalTag : IComponentData { }

/// <summary>Detonation payload for the Suicidal.</summary>
public struct SuicideCharge : IComponentData
{
    /// <summary>Distance to an enemy at which the unit self-detonates.</summary>
    public float TriggerRadius;

    /// <summary>Blast radius (enemies only).</summary>
    public float BlastRadius;

    /// <summary>Blast damage before armor/defense.</summary>
    public int BlastDamage;

    /// <summary>Blood splat amount (0..1) fed to BloodMap.AddBlood. Applied
    /// as several overlapping splats so the pool reads far larger than a
    /// normal death stain.</summary>
    public float BloodAmount;
}

/// <summary>Set the frame a Suicidal's blast has been resolved, so a unit
/// that detonates on arrival cannot also detonate when its corpse is
/// processed.</summary>
public struct SuicideSpent : IComponentData { }

// ==================== Berserker Death Frenzy ====================

/// <summary>
/// The Berserker's last stand. Stamped by BerserkerDeathFrenzySystem the
/// moment lethal damage lands: HP locks at 1, the unit cannot die, and it
/// gains a large attack and move-speed bonus. When Remaining hits zero the
/// system zeroes Health and lets DeathSystem take it — the corpse splats
/// blood like any other, feeding the ground its allies fight on.
/// </summary>
public struct DeathFrenzyState : IComponentData
{
    public float Remaining;
}

/// <summary>Once-per-life latch — a Berserker that has already burned its
/// Death Frenzy dies normally the next time it is brought down.</summary>
public struct DeathFrenzySpent : IComponentData { }
