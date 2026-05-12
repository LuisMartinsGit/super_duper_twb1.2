// RitualComponents.cs
// Ritual + Glow components shared by Alanthor's Purification, Feraldis's
// Violent Extraction, and Runai's Conversion rituals (spec §5). The state
// machine for the node itself lives in NodeStateComponents.cs.
//
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;
using Unity.Mathematics;

// ==================== Ritualist markers ====================

/// <summary>
/// Marker for Alanthor's scholar — the ritualist that performs the
/// Purification ritual. Vulnerable channeling unit (spec §11 item 1).
/// </summary>
public struct ScholarTag : IComponentData { }

// ==================== Ritual state ====================

/// <summary>
/// Ritual identity. Each faction's ritual has different defensive behavior
/// from the targeted node (spec §5.5: Runai's ritual is the hardest because
/// the node fights enslavement harder than destruction).
/// </summary>
public enum RitualKind : byte
{
    Purification = 0,       // Alanthor — node becomes Cleansed
    ViolentExtraction = 1,  // Feraldis  — node becomes Destroyed
    Conversion = 2,         // Runai     — node becomes Converted
}

/// <summary>
/// On a ritualist while channeling. Drives the channel timer; ritual
/// systems consult this every frame. Removed by the ritual system when
/// the channel completes, is interrupted, or the ritualist dies.
/// </summary>
public struct RitualState : IComponentData
{
    /// <summary>Kind of ritual being channeled (sets the resulting node state).</summary>
    public RitualKind Kind;

    /// <summary>The target crystal main node.</summary>
    public Entity TargetNode;

    /// <summary>Seconds channeled so far.</summary>
    public float Progress;

    /// <summary>Total channel duration. Snapshot at channel start so a
    /// tunable constant change mid-ritual doesn't jump the progress bar.</summary>
    public float TotalDuration;
}

/// <summary>
/// On a crystal main node while a ritual is being performed on it. Used by
/// UI and by the curse-defense system to spawn defensive waves at the
/// ritualist. Cleared when the ritual ends (complete, cancel, ritualist
/// dies). At most one active ritual per node — first claim wins.
/// </summary>
public struct ActiveRitualOnNode : IComponentData
{
    /// <summary>The ritualist performing the ritual.</summary>
    public Entity Ritualist;

    /// <summary>Kind of ritual (mirrors RitualState.Kind for read-only access on node side).</summary>
    public RitualKind Kind;

    /// <summary>Faction of the ritualist (for victory attribution + visuals).</summary>
    public Faction RitualistFaction;

    /// <summary>Cultures.* of the ritualist (drives state transition + victory).</summary>
    public byte RitualistCulture;

    /// <summary>
    /// Seconds remaining until the next defensive spawn. The ritual-defense
    /// system ticks this down; on reaching 0 it spawns a defender pointed at
    /// the ritualist and resets the timer based on current ritual progress
    /// (interval shrinks as progress increases — "increasingly intense").
    /// </summary>
    public float DefenseSpawnTimer;

    /// <summary>Total defenders this ritual has spawned (caps per-ritual swarm size).</summary>
    public int DefendersSpawned;
}

/// <summary>
/// Pending purify order on a scholar — emitted by CommandRouter.IssuePurify.
/// PurificationRitualSystem consumes it: moves the scholar to within ritual
/// range, then promotes the order to a RitualState. Cleared when the ritual
/// starts, is canceled by another command, or the scholar dies.
/// </summary>
public struct PurifyCommand : IComponentData
{
    /// <summary>The crystal main node to purify.</summary>
    public Entity TargetNode;
}

// ==================== Glow ====================

/// <summary>
/// Marker for a free-floating Glow pickup spawned at the end of a ritual.
/// Carry / deposit / intercept mechanics are a follow-up slice.
/// </summary>
public struct GlowPickupTag : IComponentData { }

/// <summary>
/// Per-pickup state. Pickup window counts down — if no one claims within
/// the window, the Glow despawns (spec §4.5: 30-60s pickup window). Future
/// extension: Carrier entity ref + attunement timer.
/// </summary>
public struct GlowPickupState : IComponentData
{
    /// <summary>How much Glow is in this pickup (delivered to the carrier's faction on deposit).</summary>
    public int Amount;

    /// <summary>Seconds remaining before the pickup despawns if uncarried.</summary>
    public float TimeRemaining;

    /// <summary>RitualKind that produced this pickup (for visuals / faction-bias scoring).</summary>
    public RitualKind Source;
}
