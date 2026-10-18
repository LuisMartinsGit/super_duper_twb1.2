// CleanseComponents.cs
// The Litharch's Cleanse channel (Territory_Claims.md §6.9.7, 2026-10-18).
// Pre-added on every Litharch with Active = 0, so starting and ending a
// channel is a plain write, never a structural change. LitharchCleanse
// starts and ends it; CurseGroundSystem reads it to clear the ground.

using Unity.Entities;
using Unity.Mathematics;

/// <summary>A running Cleanse: the circle at <see cref="Center"/> is kept
/// free of cursed ground out to <see cref="Radius"/>, which grows at a
/// constant area per second up to <see cref="MaxRadius"/>.</summary>
public struct CleanseChannel : IComponentData
{
    public byte Active;
    public float3 Center;
    /// <summary>The starting circle (the card's Radius): cleared at once, and
    /// the ground inside it heals allies while the channel holds.</summary>
    public float StartRadius;
    /// <summary>The cleared radius now.</summary>
    public float Radius;
    public float MaxRadius;
    /// <summary>m² of ground reclaimed a second.</summary>
    public float AreaPerSecond;
    /// <summary>Width of the falloff band outside <see cref="Radius"/>.</summary>
    public float Falloff;
    public float HealPerSecond;
    public float HealAccum;
    public float Elapsed;
    /// <summary>The ability's slot and cooldown: the cooldown is held at full
    /// while the channel runs, so it counts from the moment it ends.</summary>
    public int Slot;
    public float Cooldown;
    /// <summary>HP when last checked: any drop means the Litharch was hit.</summary>
    public int LastHealth;
    // The orders live when the channel began; a different one ends it.
    public byte HadMove;
    public float3 MoveDestination;
    public byte HadAttack;
    public Entity AttackTarget;
}
