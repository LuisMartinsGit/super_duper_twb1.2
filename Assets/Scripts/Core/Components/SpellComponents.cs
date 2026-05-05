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
