// CombatComponents.cs
// Components for combat, targeting, and damage systems
// Place in: Assets/Scripts/Core/Components/Combat/

using Unity.Entities;

// ==================== Basic Combat Stats ====================

/// <summary>
/// Base damage output of an entity.
/// </summary>
public struct Damage : IComponentData
{
    public int Value;
}

/// <summary>
/// Attack speed cooldown management.
/// </summary>
public struct AttackCooldown : IComponentData
{
    public float Cooldown;  // Seconds between attacks
    public float Timer;     // Current countdown timer
}

// ==================== Targeting System ====================

/// <summary>
/// Current combat target.
/// </summary>
public struct Target : IComponentData
{
    public Entity Value; // Entity.Null if no target
}

// ==================== Damage & Armor Type System ====================

/// <summary>
/// Categorizes a unit's or building's outgoing damage.
/// Used by CombatModifiers for damage-type vs armor-type modifier lookups.
/// </summary>
public enum DamageType : byte
{
    Melee  = 0,
    Ranged = 1,
    Siege  = 2,
    Magic  = 3,
    True   = 4
}

/// <summary>
/// Categorizes a unit's or building's incoming damage resistance profile.
/// Used by CombatModifiers for damage-type vs armor-type modifier lookups.
/// </summary>
public enum ArmorType : byte
{
    InfantryLight  = 0,
    InfantryHeavy  = 1,
    Ranged         = 2,
    Cavalry        = 3,
    Structure      = 4,
    StructureHuman = 5
}

/// <summary>
/// Tags an entity with its outgoing damage type.
/// Default: Melee if component is absent.
/// </summary>
public struct DamageTypeData : IComponentData
{
    public DamageType Value;
}

/// <summary>
/// Tags an entity with its armor type for incoming damage calculations.
/// Default: InfantryLight if component is absent.
/// </summary>
public struct ArmorTypeData : IComponentData
{
    public ArmorType Value;
}

// ==================== Command Components ====================
// Command types consolidated into TheWaningBorder.Core.Commands.Types namespace.
// See: Core/Commands/CommandTypes/AttackCommand.cs, BuildCommand.cs, GatherCommand.cs, HealCommand.cs
// Use: using TheWaningBorder.Core.Commands.Types;