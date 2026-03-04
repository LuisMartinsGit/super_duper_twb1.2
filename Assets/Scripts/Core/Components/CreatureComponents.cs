// CreatureComponents.cs
// Components for hostile NPC creatures and their cadavers
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;
using Unity.Mathematics;

// ==================== Creature Components ====================

/// <summary>
/// Marker tag for hostile NPC creatures.
/// Creatures use Faction.White and are hostile to all player factions.
/// </summary>
public struct CreatureTag : IComponentData { }

/// <summary>
/// Creature behavior state for wandering near home position.
/// </summary>
public struct CreatureState : IComponentData
{
    /// <summary>Original spawn position - creature returns here and wanders nearby.</summary>
    public float3 HomePosition;

    /// <summary>Countdown timer until next wander movement.</summary>
    public float WanderTimer;

    /// <summary>Seconds between wander movements.</summary>
    public float WanderInterval;

    /// <summary>Maximum distance from home the creature will wander.</summary>
    public float WanderRadius;
}

// ==================== Cadaver Components ====================

/// <summary>
/// Marker tag for creature cadavers (mineable for crystal).
/// </summary>
public struct CadaverTag : IComponentData { }

/// <summary>
/// Crystal resource state for a cadaver entity.
/// Miners extract crystal from cadavers similar to iron from deposits.
/// </summary>
public struct CadaverState : IComponentData
{
    /// <summary>Crystal remaining in this cadaver.</summary>
    public int RemainingCrystal;

    /// <summary>1 = fully harvested, 0 = still has crystal.</summary>
    public byte Depleted;
}

// ==================== Respawn Components ====================

/// <summary>
/// Tracks a creature spawn point for respawning after death.
/// Created when a creature dies, destroyed when the creature respawns.
/// </summary>
public struct CreatureSpawnPoint : IComponentData
{
    /// <summary>World position to respawn the creature at.</summary>
    public float3 Position;

    /// <summary>Countdown timer in seconds until respawn (starts at 180 = 3 minutes).</summary>
    public float RespawnTimer;

    /// <summary>HP of the creature to respawn.</summary>
    public int CreatureHP;

    /// <summary>Damage of the creature to respawn.</summary>
    public int CreatureDamage;

    /// <summary>Speed of the creature to respawn.</summary>
    public float CreatureSpeed;
}
