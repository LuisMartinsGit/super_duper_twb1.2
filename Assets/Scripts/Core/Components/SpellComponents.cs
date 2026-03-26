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
