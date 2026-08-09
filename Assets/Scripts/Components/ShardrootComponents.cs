// ShardrootComponents.cs
// The Shardroot — the One-Ring-style power artifact of the Curse &
// Shardroot design (docs/Design/Curse_And_Shardroot.md §3). One well per
// match secretly hosts it; the FIRST player to apply their culture's verb
// to that well receives it. It then rides the existing Glow pickup /
// carrier / Temple-storage machinery as a single, persistent, tagged
// quantum:
//
//   pickup (attunement claim) → carrier unit (minimap-visible, drops on
//   death) → Hall (awaken the Shardbound Hero) OR Temple (enshrine: god
//   powers + sect cooldowns amplified via the existing GlowStored paths,
//   and the Temple detonates its stockpile on death — volatility for free).
//
// Global namespace per project ECS-component convention.
// Location: Assets/Scripts/Components/ShardrootComponents.cs

using Unity.Entities;

/// <summary>
/// Marks whatever currently embodies the Shardroot: the ground pickup,
/// the carrying unit, the awakened Shardbound Hero, or the enshrining
/// Temple. Exactly one entity in the world carries this tag once the
/// artifact is found. Minimap renders it as a beacon visible to all.
/// </summary>
public struct ShardrootTag : IComponentData { }

/// <summary>
/// The awakened Shardbound Hero (Hall delivery). Blocks the Temple
/// auto-deposit (no backsies — the Hall choice is locked until the hero
/// dies and the artifact drops from the detonation).
/// </summary>
public struct ShardboundHeroTag : IComponentData { }

/// <summary>Singleton tracking the artifact's match state.</summary>
public struct ShardrootState : IComponentData
{
    /// <summary>The seeded host well (chosen deterministically once the
    /// Border main nodes exist).</summary>
    public Entity HostNode;
    public byte HostChosen;
    /// <summary>1 once the artifact has entered play (first verb on the
    /// host well).</summary>
    public byte Found;
    /// <summary>Faction currently holding it (carrier / hero / temple).
    /// Faction.Border = unheld (on the ground or undiscovered).</summary>
    public Faction HolderFaction;

    /// <summary>Glow quanta the artifact embodies — drives god-power
    /// scaling and the Temple detonation magnitude via existing paths.</summary>
    public const int ShardrootPower = 12;
    /// <summary>Carrier-to-own-Hall distance that awakens the hero.</summary>
    public const float HallDeliverRadius = 12f;
}
