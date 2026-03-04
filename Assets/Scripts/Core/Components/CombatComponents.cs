// CombatComponents.cs
// Components for combat, targeting, and damage systems
// Place in: Assets/Scripts/Core/Components/Combat/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ==================== Basic Combat Stats ====================

/// <summary>
/// Base damage output of an entity.
/// </summary>
public struct Damage : IComponentData
{
    public int Value;
}

/// <summary>
/// Attack range for ranged and melee units.
/// </summary>
public struct AttackRange : IComponentData
{
    public float Value;
}

/// <summary>
/// Minimum attack range (prevents firing at too-close targets).
/// </summary>
public struct MinAttackRange : IComponentData
{
    public float Value;
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

// ==================== Mission Components ====================

/// <summary>
/// Attack mission order for units/armies.
/// </summary>
public struct MissionAttack : IComponentData
{
    public float3 TargetPos;
    public Entity PrimaryTarget; // Target base/unit if known
}

/// <summary>
/// Defense mission order for units/armies.
/// </summary>
public struct MissionDefend : IComponentData
{
    public float3 AreaCenter;
    public float Radius;
}

/// <summary>
/// Scouting mission order.
/// </summary>
public struct MissionScout : IComponentData
{
    public float3 TargetPos;
}

// ==================== Scouting & Intel ====================

/// <summary>
/// Intelligence gathered by scouts about enemy positions.
/// </summary>
public struct ScoutIntel : IBufferElementData
{
    public float3 Pos;
    public float LastSeenTime;
    public byte IsEnemyBase; // 0/1
}

/// <summary>
/// Exploration frontier for scout pathfinding.
/// </summary>
public struct ExploreFrontier : IBufferElementData
{
    public float3 WorldPos;
    public float Score; // Higher = more attractive to explore
}