// NodeStateComponents.cs
// Node state machine for The Border design — every main node lives in one of
// four states (Active / Cleansed / Converted / Destroyed). Non-Active states
// revert to Active over time: the map "wants" to be Active.
//
// See spec §9 (Node State Machine) and §8 (Victory Conditions).
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;

// ==================== Node State Machine ====================

/// <summary>
/// Veilstone main node states. Mirrors design spec §9.
/// Every non-Active state is temporary; the reversion/regrowth system
/// returns the node to Active when its timer expires.
/// </summary>
public enum NodeState : byte
{
    /// <summary>Default. Spawns border waves; available for any ritual.</summary>
    Active = 0,
    /// <summary>Alanthor-claimed. Border recedes. Reverts to Active over time.</summary>
    Cleansed = 1,
    /// <summary>Runai-claimed. Persists as Runai infrastructure. Reverts to Active over time.</summary>
    Converted = 2,
    /// <summary>Feraldis-killed. Inert. Regrows to Active after the regrowth timer expires.</summary>
    Destroyed = 3,
}

/// <summary>
/// Per-node state component on every veilstone main node.
/// Drives reversion, victory progress, border spread enable/disable.
/// </summary>
public struct BorderNodeState : IComponentData
{
    /// <summary>Current state. Default Active on spawn.</summary>
    public NodeState State;

    /// <summary>Cultures.* byte of the culture that imposed the current state. 0 = Cultures.None (Active).</summary>
    public byte OwnerCulture;

    /// <summary>Faction (player) that imposed the current state. Faction.Border for Active.</summary>
    public Faction OwnerFaction;

    /// <summary>Seconds spent in current state. Used by reversion/regrowth.</summary>
    public float StateTimer;
}

/// <summary>
/// Marker tag added to nodes in the Destroyed state. DeathSystem excludes
/// dormant entities from its dead-entity scan, so the node persists at 0 HP
/// until the regrowth timer expires and revives it. The tag is removed on
/// regrowth.
/// </summary>
public struct NodeDormant : IComponentData { }

/// <summary>
/// Marks a destroyed node in its REBUILD phase (destruction rework 2026-07):
/// after lying as rubble for NodeRubbleTime, the node spends NodeRebuildTime
/// reconstructing before returning to Active. Present only during that build
/// window; NodeRubbleVisualSystem reads it (plus BorderNodeState.StateTimer)
/// to drive the reforming visual. The node stays Destroyed + NodeDormant
/// throughout so it is inert and untargetable while rebuilding.
/// </summary>
public struct NodeRebuilding : IComponentData { }

// ==================== Node Untargetability (spec refinement v2) ====================

/// <summary>
/// Tag marking a veilstone node as untargetable. TargetingSystem's enemy
/// query excludes entities carrying this tag — units won't auto-acquire the
/// node, AI won't path to it, right-click attacks no-op.
///
/// Destruction rework (2026-07): NodeTargetabilitySystem sets this on every
/// node that is NOT Active (Destroyed rubble / rebuilding / Cleansed /
/// Converted) and clears it while Active, so an Active node can be brought to
/// 0 HP by normal combat while inert husks are left alone.
/// </summary>
public struct NodeUntargetable : IComponentData { }

/// <summary>
/// LEGACY — old invulnerability snapshot, kept for backward compatibility
/// with BorderMainNode.Create archetype. NodeInvulnerabilitySystem has been
/// deleted; this struct is unused at runtime but its archetype slot stays so
/// existing factories don't have to drop it from their CreateEntity calls.
/// Safe to remove in a follow-up cleanup pass.
/// </summary>
public struct NodeInvulnerabilityState : IComponentData
{
    public int LastObservedHealth;
}

// ==================== Node Victory Tracking ====================

/// <summary>
/// Singleton component tracking per-culture node-victory progress.
/// Lives on a single entity created in BorderNodeBootstrap.
///
/// Alanthor / Runai win when their hold-timer reaches NodeVictoryHoldTime
/// (5 min by spec). The timer ticks only while every main node is claimed by
/// that culture's preferred state and resets the moment any node falls out.
///
/// Feraldis wins instantly when the last-active node is destroyed AND the
/// killing blow was dealt by a Feraldis-aligned faction.
/// </summary>
public struct NodeVictoryState : IComponentData
{
    /// <summary>Seconds Alanthor has held an all-Cleansed map. 0 = not currently holding.</summary>
    public float AlanthorHoldTimer;

    /// <summary>Seconds Runai has held an all-Converted map. 0 = not currently holding.</summary>
    public float RunaiHoldTimer;

    /// <summary>Faction.Border if not yet destroyed; otherwise the faction of the most-recent destroyer.</summary>
    public Faction LastDestroyerFaction;

    /// <summary>Cultures.* of the most-recent destroyer (Cultures.None until set).</summary>
    public byte LastDestroyerCulture;

    /// <summary>1 = node victory already fired; suppress further triggers.</summary>
    public byte VictoryFired;

    // ── Curse & Shardroot canon (well domination) ─────────────────────
    /// <summary>Faction currently holding an all-Cleansed map (candidate
    /// for the purify-domination win). Faction.Border = none.</summary>
    public Faction CleansedCandidate;
    /// <summary>Faction currently holding an all-Converted map.</summary>
    public Faction ConvertedCandidate;
    /// <summary>Faction last broadcast at match point (N−1 wells claimed).
    /// Faction.Border = nobody currently announced.</summary>
    public Faction MatchPointFaction;
}

// ==================== The Waking (well dormancy) ====================

/// <summary>
/// PER-WELL dormancy tag (docs/Design/Curse_And_Shardroot.md §2.8, added
/// 2026-08-07). Present = this well is asleep and pumps nothing;
/// VeilFieldSystem's feeder query excludes it outright. Removed permanently
/// the moment a ritualist starts a verb channel on it.
///
/// The three-phase match this produces:
///
///   EARLY  — every well dormant. The map does not creep. No curse at all
///            beyond what players make themselves.
///   MID    — players mine veilstone patches dry; the last bud of each patch
///            wakes a blight pocket (§2.7). That curse is LOCAL and KILLABLE,
///            and the crust it pushes out pays veilstone back as it recedes.
///            Players farm a curse they chose to create.
///   LATE   — the verb victory needs wells, and touching a well wakes it. The
///            real curse arrives one well at a time, in the order players
///            reach for it.
///
/// Waking is per-well and not global on purpose. It makes the ORDER you claim
/// wells a real decision — each one you touch arms that region for the rest of
/// the match — and it makes the curse WEAPONISABLE: waking the well on your
/// rival's doorstep costs them ground even if you never finish the verb there.
/// A global switch would have made the first poke a coin flip that dooms
/// everyone equally, with no geography to it.
///
/// Trigger is channel START, not completion: an attempt that gets interrupted
/// has still woken the well, so there is no safe probe and no take-backs.
///
/// Wells respawned by BorderExtinctionSystem come back dormant — a new well
/// nobody has touched yet is asleep, same as one at match start.
/// </summary>
public struct WellDormant : IComponentData { }
