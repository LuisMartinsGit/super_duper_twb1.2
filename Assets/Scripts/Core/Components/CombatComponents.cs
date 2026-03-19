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

// ==================== Command Components ====================
// Command types consolidated into TheWaningBorder.Core.Commands.Types namespace.
// See: Core/Commands/CommandTypes/AttackCommand.cs, BuildCommand.cs, GatherCommand.cs, HealCommand.cs
// Use: using TheWaningBorder.Core.Commands.Types;