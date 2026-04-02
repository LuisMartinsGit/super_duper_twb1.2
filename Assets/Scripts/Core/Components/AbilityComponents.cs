// AbilityComponents.cs
// ECS components for sect unit abilities
// Location: Assets/Scripts/Core/Components/AbilityComponents.cs

using Unity.Entities;

// ==================== Ability ID Enum ====================

/// <summary>
/// Ability IDs for the 12 sect unit abilities.
/// Each sect unique unit has exactly one ability.
/// </summary>
public enum AbilityId : byte
{
    None = 0,
    RapidMend = 1,      // ScarGuard self-heal
    ArcanePulse = 2,    // GolemAutark AOE magic
    Fortify = 3,        // StoneWarden armor+immobile
    Dispel = 4,         // ArchivistAdept strip buffs
    Sanction = 5,       // FlameWarden root
    Safeguard = 6,      // VaultKeeper armor aura
    MirrorShield = 7,   // GlassmarkArcanist reflect
    Condemn = 8,        // Judicator mark for bonus dmg
    Ignite = 9,         // Ashblade fire attacks
    WarCry = 10,        // Brandbreaker AOE slow
    ChainBind = 11,     // Chaincaster root
    VoidStrike = 12     // Nullblade bonus dmg
}

// ==================== Core Ability Components ====================

/// <summary>
/// Attached to every sect unit at spawn. Defines which ability the unit has
/// and tracks its cooldown state.
/// </summary>
public struct UnitAbility : IComponentData
{
    /// <summary>Which ability this unit has</summary>
    public AbilityId Id;

    /// <summary>Total cooldown duration in seconds</summary>
    public float CooldownDuration;

    /// <summary>Seconds remaining before ability can be used again (0 = ready)</summary>
    public float CooldownRemaining;

    /// <summary>Cast range in units (0 = self-cast)</summary>
    public float Range;
}

/// <summary>
/// Transient one-frame command tag. Added when ability fires, consumed by UnitAbilitySystem.
/// </summary>
public struct AbilityActivated : IComponentData
{
    /// <summary>Target entity (Entity.Null for self-cast abilities)</summary>
    public Entity Target;
}

// ==================== Ability Effect Components ====================

/// <summary>
/// Judicator's Condemn mark - target takes bonus damage from all sources.
/// Ticked down and removed by UnitAbilitySystem.
/// </summary>
public struct Condemned : IComponentData
{
    /// <summary>Damage multiplier (1.25 = +25% damage taken)</summary>
    public float DamageMultiplier;

    /// <summary>Seconds remaining</summary>
    public float TimeRemaining;
}

/// <summary>
/// StoneWarden's Fortify - self-immobilization + armor bonus.
/// Ticked down and removed by UnitAbilitySystem.
/// </summary>
public struct Fortified : IComponentData
{
    /// <summary>Flat armor bonus added to all defense types</summary>
    public float ArmorBonus;

    /// <summary>Seconds remaining</summary>
    public float TimeRemaining;
}

/// <summary>
/// Ashblade's Ignite - fire damage on next N attacks.
/// Decremented per attack by combat systems, removed when attacks run out.
/// </summary>
public struct IgniteBuff : IComponentData
{
    /// <summary>Number of attacks remaining with bonus fire damage</summary>
    public int AttacksRemaining;

    /// <summary>Bonus damage per attack</summary>
    public float BonusDamage;
}

/// <summary>
/// Nullblade's VoidStrike - next-attack bonus damage.
/// Consumed by combat system on next hit.
/// </summary>
public struct VoidStrikeBuff : IComponentData
{
    /// <summary>Bonus damage on next attack</summary>
    public float BonusDamage;

    /// <summary>Bonus damage vs Crystal-buffed targets</summary>
    public float BonusVsCrystal;
}

/// <summary>
/// ScarGuard's RapidMend - heal over time effect.
/// Ticked and removed by UnitAbilitySystem.
/// </summary>
public struct HealOverTime : IComponentData
{
    /// <summary>Total healing to deliver over the full duration</summary>
    public float TotalHealing;

    /// <summary>Total duration in seconds</summary>
    public float Duration;

    /// <summary>Seconds elapsed so far</summary>
    public float Elapsed;
}

// ==================== Spell Effect Components ====================

/// <summary>
/// Marks a summoned unit that despawns after a timer expires.
/// </summary>
public struct SummonedUnit : IComponentData
{
    public float DespawnTimer;
}

/// <summary>
/// Area-of-effect burning ground damage over time.
/// </summary>
public struct BurningGround : IComponentData
{
    public float DPS;
    public float TimeRemaining;
    public float Radius;
}

/// <summary>
/// Mind-controlled entity - temporarily fights for a different faction.
/// </summary>
public struct MindControlled : IComponentData
{
    public Faction OriginalFaction;
    public float TimeRemaining;
}

/// <summary>
/// Stealthed entity - invisible to enemies for a duration.
/// </summary>
public struct StealthTag : IComponentData
{
    public float TimeRemaining;
}
