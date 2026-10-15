// FeraldisSiegeComponents.cs
// The Engine Works rigs (docs/Design/Age_1_Feraldis.md §7.5, 2026-10-12).

using Unity.Entities;
using Unity.Mathematics;

/// <summary>Ram Rig — a fragile explosive on wheels.</summary>
public struct RamRigTag : IComponentData { }
/// <summary>The Ram Rig has already blown (latch).</summary>
public struct RamRigSpent : IComponentData { }

/// <summary>Harpoon Rig — its shots leave a barbed-wire line.</summary>
public struct HarpoonRigTag : IComponentData { }
/// <summary>Corpse Engine — its impacts leave blood pools.</summary>
public struct CorpseEngineTag : IComponentData { }
/// <summary>War Rig — carries infantry, fires on the move.</summary>
public struct WarRigTag : IComponentData { }

/// <summary>An infantry unit riding inside a War Rig.</summary>
public struct WarRigPassenger : IBufferElementData { public Entity Unit; }
/// <summary>On a passenger: the rig it rides and where that rig last was (for unloading after the rig dies).</summary>
public struct RidingWarRig : IComponentData { public Entity Rig; public float3 LastRigPosition; }

/// <summary>On a Corpse Engine projectile.</summary>
public struct CorpseShot : IComponentData { }
/// <summary>On a Harpoon Rig projectile: where the wire starts.</summary>
public struct HarpoonShot : IComponentData { public float3 From; public Entity Shooter; }

/// <summary>A barbed-wire line hazard: hurts and bleeds ANY unit crossing it, friend or foe.</summary>
public struct BarbedWire : IComponentData
{
    public float3 From;
    public float3 To;
    public float Remaining;
    /// <summary>Kill credit only — the wire does not care whose side you are on.</summary>
    public Faction Source;
    public float Carry;
}
