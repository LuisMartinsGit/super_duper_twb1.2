// ShardrootComponents.cs
// The Shardroot — the One-Ring-style power artifact of the Curse &
// Shardroot design (docs/Design/Curse_And_Shardroot.md §3). One well per
// match secretly hosts it; the FIRST player to apply their culture's verb
// to that well receives it. It then rides the existing Shardroot pickup /
// carrier / Temple-storage machinery as a single, persistent, tagged
// quantum:
//
//   pickup → a HERO carries it, Ascended (docs/Design/Heroes.md §4; explodes if
//   killed with it, may set it down safely) → or the Temple (enshrine: sect
//   cooldowns and the army empowered, the ascension countdown, and the Temple
//   detonates on death).
//
// Global namespace per project ECS-component convention.

using Unity.Entities;

/// <summary>
/// Marks whatever currently embodies the Shardroot: the ground pickup,
/// the carrying hero, or the enshrining Temple. Exactly one entity in the world carries this tag once the
/// artifact is found. Minimap renders it as a beacon visible to all.
/// </summary>
public struct ShardrootTag : IComponentData { }

/// <summary>
/// THE ENSHRINE ORDER (2026-10-20, Curse_And_Shardroot.md § 3.1d): the
/// player right-clicked their own finished Temple of Ridan with the Shardroot's
/// bearer selected. The bearer walks to the Temple and, once within
/// BorderConstants.ShardrootDepositRadius of its wall, enshrines the artifact
/// (ShardrootCarrySystem). Dropped when the bearer is ordered
/// elsewhere, stops short, loses the artifact, or the Temple is lost; a
/// faction that cannot pay waits at the Temple (§ 3.1c) with the order kept.
/// Issued as LockstepCommandType.EnshrineShardroot.
/// </summary>
public struct ShardrootEnshrineOrder : IComponentData
{
    public Entity Temple;
    /// <summary>Seconds the bearer has stood still short of the Temple.</summary>
    public float StuckSeconds;
    /// <summary>1 once the "cannot pay" notice was shown for this order.</summary>
    public byte PriceNoticed;
}

/// <summary>
/// The Maw backstop's visible artifact (§3): a display-only entity standing
/// in the host well once it reaches Maw maturity unverbed. NOT a pickup and
/// NOT a ShardrootTag holder -- nobody can take it; verbing the well
/// (ShardrootSystem.TryAward) removes it and drops the real pickup.
/// </summary>
public struct ShardrootEmbedded : IComponentData
{
    public Entity Well;
}

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
    /// Faction.Border = unheld (on the ground, undiscovered, or carried by
    /// a curse unit). Read by the curse's hunt-the-holder bias (§3.1 "the
    /// curse wants it back").</summary>
    public Faction HolderFaction;
    /// <summary>Where the holder stands, valid while HolderFaction is not
    /// Faction.Border.</summary>
    public Unity.Mathematics.float3 HolderPos;

    /// <summary>THE MAW BACKSTOP (§3): sim seconds the current host well has
    /// spent Wild (NodeState.Active) while the artifact is still inside it.
    /// Reaching BorderSettings.shardrootMawSeconds is "Maw maturity".
    /// Reset when the host is re-chosen.</summary>
    public float HostWildSeconds;
    /// <summary>1 once the host has reached Maw maturity unverbed: the
    /// Shardroot is visibly embedded in it (ShardrootEmbedded marker +
    /// minimap beacon). Still claimed by verbing that well.</summary>
    public byte Embedded;
    /// <summary>SimCadence.Epoch the singleton was created in, so a state
    /// that outlived its match is reset rather than read. 0 = authored by a
    /// scenario, never reset.</summary>
    public int MatchEpoch;

    /// <summary>THE DESCENT (§3.1c, 2026-10-09): religion points earned by
    /// every player so far, and how many make the Shardroot descend. Written
    /// by CurseTerritorySystem; read by the UI and the AI.</summary>
    public int DescentEarned;
    public int DescentNeeded;

    /// <summary>THE ASCENSION (§3.1c): the faction whose Temple enshrines the
    /// artifact (Faction.Border = none), sim seconds its countdown has run,
    /// and 1 once it ran out with the Temple standing — EliminationSystem
    /// then retires every faction hostile to it.</summary>
    public Faction AscensionFaction;
    public float AscensionElapsed;
    public byte AscensionDone;
    /// <summary>Seconds-left mark of the last countdown warning (-1 = none
    /// yet; CountdownWarnings). Named for its old meaning, whole minutes, and
    /// kept so saved games still load.</summary>
    public int AscensionNoticeMinutes;

    /// <summary>Shardroot quanta the artifact embodies — drives god-power
    /// scaling and the Temple detonation magnitude via existing paths.</summary>
    public const int ShardrootPower = 12;
}
