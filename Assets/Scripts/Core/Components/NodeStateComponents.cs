// NodeStateComponents.cs
// Node state machine for Crystal Curse design — every main node lives in one of
// four states (Active / Cleansed / Converted / Destroyed). Non-Active states
// revert to Active over time: the map "wants" to be Active.
//
// See spec §9 (Node State Machine) and §8 (Victory Conditions).
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;

// ==================== Node State Machine ====================

/// <summary>
/// Crystal main node states. Mirrors design spec §9.
/// Every non-Active state is temporary; the reversion/regrowth system
/// returns the node to Active when its timer expires.
/// </summary>
public enum NodeState : byte
{
    /// <summary>Default. Spawns curse waves; available for any ritual.</summary>
    Active = 0,
    /// <summary>Alanthor-claimed. Curse recedes. Reverts to Active over time.</summary>
    Cleansed = 1,
    /// <summary>Runai-claimed. Persists as Runai infrastructure. Reverts to Active over time.</summary>
    Converted = 2,
    /// <summary>Feraldis-killed. Inert. Regrows to Active after the regrowth timer expires.</summary>
    Destroyed = 3,
}

/// <summary>
/// Per-node state component on every crystal main node.
/// Drives reversion, victory progress, curse spread enable/disable.
/// </summary>
public struct CrystalNodeState : IComponentData
{
    /// <summary>Current state. Default Active on spawn.</summary>
    public NodeState State;

    /// <summary>Cultures.* byte of the culture that imposed the current state. 0 = Cultures.None (Active).</summary>
    public byte OwnerCulture;

    /// <summary>Faction (player) that imposed the current state. Faction.Curse for Active.</summary>
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

// ==================== Node Untargetability (spec refinement v2) ====================

/// <summary>
/// Tag added by IconoclastAuraSystem to mark a crystal node as untargetable.
/// TargetingSystem's enemy query excludes entities carrying this tag — units
/// won't auto-acquire the node, AI won't path to it, right-click attacks no-op.
///
/// Removed when an IconoclastTag unit is within IconoclastAuraRadius of the
/// node; restored when no Iconoclast is in range. This is the AI-friendly
/// replacement for the old NodeInvulnerabilityState refund-per-frame approach.
/// </summary>
public struct NodeUntargetable : IComponentData { }

/// <summary>
/// LEGACY — old invulnerability snapshot, kept for backward compatibility
/// with CrystalMainNode.Create archetype. NodeInvulnerabilitySystem has been
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
/// Lives on a single entity created in CrystalNodeBootstrap.
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

    /// <summary>Faction.Curse if not yet destroyed; otherwise the faction of the most-recent destroyer.</summary>
    public Faction LastDestroyerFaction;

    /// <summary>Cultures.* of the most-recent destroyer (Cultures.None until set).</summary>
    public byte LastDestroyerCulture;

    /// <summary>1 = node victory already fired; suppress further triggers.</summary>
    public byte VictoryFired;
}
