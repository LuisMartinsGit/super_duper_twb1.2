// SpellComponents.cs
// ECS components for spell buff/debuff effects
// Location: Assets/Scripts/Core/Components/SpellComponents.cs

using Unity.Entities;

// ==================== Spell Buff/Debuff Components ====================

/// <summary>
/// Temporary buff applied by a spell. Ticked down by SpellBuffSystem.
/// Removed automatically when TimeRemaining reaches 0.
/// </summary>
public struct SpellBuff : IComponentData
{
    /// <summary>Flat armor bonus added to all defense types</summary>
    public float ArmorBonus;

    /// <summary>Damage multiplier (1.25 = +25% damage)</summary>
    public float DamageMultiplier;

    /// <summary>Speed multiplier (1.25 = +25% speed)</summary>
    public float SpeedMultiplier;

    /// <summary>Fraction of damage reflected back to attacker (0.25 = 25%)</summary>
    public float DamageReflect;

    /// <summary>Seconds remaining for this buff</summary>
    public float TimeRemaining;
}

/// <summary>
/// Temporary debuff applied by a spell. Ticked down by SpellBuffSystem.
/// Removed automatically when TimeRemaining reaches 0.
/// </summary>
public struct SpellDebuff : IComponentData
{
    /// <summary>Speed reduction as a fraction (0.30 = -30% speed)</summary>
    public float SpeedReduction;

    /// <summary>Supplies drained per second from the entity's faction</summary>
    public float SuppliesDrainPerSecond;

    /// <summary>Seconds remaining for this debuff</summary>
    public float TimeRemaining;
}

/// <summary>
/// Makes an entity temporarily invulnerable (takes no damage).
/// Applied by LockdownVault spell. Removed when TimeRemaining reaches 0.
/// </summary>
public struct Invulnerable : IComponentData
{
    public float TimeRemaining;
}

/// <summary>
/// Fortitude's "Veiled Stone" HP-bonus stamp (task-063 sect Lv I+).
/// Stamped on a wall, wall hub, or tower entity once its <c>Health.Max</c>
/// (and current <c>Value</c>, by the same fraction) has been multiplied
/// by Fortitude's per-level HP factor. <c>AppliedLevel</c> records the
/// level the bonus was applied at so Phase 4 lever upgrades can apply
/// the diff (factorAtNewLevel / factorAtOldLevel) without re-scaling.
/// </summary>
public struct FortitudeHpApplied : IComponentData
{
    public byte AppliedLevel; // 1/2/3 — matches SectQuery.LevelOf
}

/// <summary>
/// Witness's "All-Seeing" vision-bonus stamp (task-063 sect Lv I+).
/// Stamped on a Scout entity once its LineOfSight.Radius has been multiplied
/// by Witness's per-level vision factor. <c>AppliedLevel</c> records the
/// level the bonus was applied at — when Phase 4 lever upgrades land, the
/// diff between AppliedLevel and the current lever level is applied.
/// </summary>
public struct WitnessVisionApplied : IComponentData
{
    public byte AppliedLevel; // 1/2/3 — matches SectQuery.LevelOf
}

/// <summary>
/// Justice's "Marked for Sentence" mark (task-063 sect Lv I).
/// Applied to an enemy unit when it kills one of your units, IF the killed
/// unit's faction has Justice adopted. While active:
///   - The marked unit is visible through fog of war to the marker faction.
///   - It takes +10% damage (Lv I — scales to +20%/+30% in Phase 4) from
///     units of the marker faction.
/// Self-removed by SpellBuffSystem when TimeRemaining hits 0.
/// </summary>
public struct MarkedForSentence : IComponentData
{
    /// <summary>Faction that placed the mark (the avenger).</summary>
    public Faction MarkerFaction;

    /// <summary>Bonus damage multiplier added when the marker faction attacks (e.g. 0.10 = +10%).</summary>
    public float DamageBonus;

    /// <summary>Seconds remaining on the mark.</summary>
    public float TimeRemaining;
}

/// <summary>
/// Veneration's "Fervor" passive (task-063 sect Lv I).
/// On every kill the killer unit gains a stack of +damage / +attack-speed
/// for a few seconds, refreshed on each kill. The stack count is capped to
/// keep the multiplier finite. Lv I/II/III scale the per-stack values:
///   Lv I  : +3% / +3%, 3s, kept by SectVenerationFervorSystem
///   Lv II : +5% / +5%, 3s   (Phase 4)
///   Lv III: +5% / +5% / +5% move, 4s  (Phase 4)
/// Removed by SpellBuffSystem when TimeRemaining hits 0.
/// </summary>
public struct VenerationFervor : IComponentData
{
    /// <summary>Number of kills currently stacked on this unit.</summary>
    public byte Stacks;

    /// <summary>Seconds remaining before the stack expires (refreshed each kill).</summary>
    public float TimeRemaining;
}

/// <summary>
/// Antiquity's "Tally of the Lost" passive (task-063 sect Lv I).
/// Per-attacker, per-victim-class kill counter — each kill of a given
/// <see cref="UnitClass"/> grants the killer +1% damage on future hits
/// against that class, capped per-class. Lv I cap is 10 kills (so the
/// max bonus per class is +10%).
///
/// Layout: one byte per UnitClass (0..7) for an 8-byte total — a
/// DynamicBuffer would have been overkill since the class count is fixed
/// and tiny. Phase 4 raises the cap and per-kill bonus.
///
/// Stamped lazily by SectAntiquityTallySystem the first time the unit
/// makes a relevant kill; consumed by CombatDamageHelper on every hit.
/// </summary>
public struct AntiquityKills : IComponentData
{
    public byte Melee;
    public byte Ranged;
    public byte Siege;
    public byte Support;
    public byte Magic;
    public byte Economy;
    public byte Miner;
    public byte Scout;
}

/// <summary>
/// Silence's "Steadfast Vigil" passive (task-063 sect Lv I).
/// Tracks how long this unit has been holding position (HoldPositionTag).
/// Refreshed every frame by SectSilenceVigilSystem while the tag is
/// present; removed (along with <see cref="SilenceVigilArmor"/>) the
/// frame the tag drops, so the bonus fades the instant the unit moves.
/// </summary>
public struct SilenceVigilState : IComponentData
{
    /// <summary>Seconds spent in HoldPosition stance.</summary>
    public float TimeInStance;
}

/// <summary>
/// Active armor bump from Silence's Steadfast Vigil. Consumed by
/// CombatDamageHelper.GetSpellBuffArmorBonus alongside SpellBuff.ArmorBonus
/// so the matrix damage formula sees the buff. Lives behind a dedicated
/// component (rather than SpellBuff) so it can be cleared cleanly when
/// the unit moves — SpellBuff would leak its longest-running field.
/// </summary>
public struct SilenceVigilArmor : IComponentData
{
    public int Bonus;
}

/// <summary>
/// Feraldis blood-pool entity (task-063 phase 3). Spawned at unit death
/// positions when a Feraldis-cultured faction has reached Age II+. Self-
/// decays via TimeRemaining; SectBloodPoolEffectSystem ticks the timer
/// and destroys the entity at zero. Inhabited Feraldis units (any unit
/// of a Feraldis Age II+ faction standing inside the pool's radius)
/// get stamped with <see cref="InBloodPool"/> for the frame.
/// </summary>
public struct BloodPool : IComponentData
{
    public float Radius;
    public float TimeRemaining;
}

/// <summary>
/// Per-frame marker stamped on a unit currently inside any BloodPool.
/// Read by CombatDamageHelper to layer the in-pool damage bonus on top
/// of Wrath's Lv I HP-missing bonus. Removed the next frame the unit
/// is no longer in a pool.
/// </summary>
public struct InBloodPool : IComponentData { }
