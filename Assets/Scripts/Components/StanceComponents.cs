// StanceComponents.cs
// Unit stances and the per-unit engagement record behind the chase leash.
// docs/Design/Stances.md is canonical for the rules these carry.
//
// Global namespace, per the project's ECS component convention.

using Unity.Entities;
using Unity.Mathematics;

/// <summary>The three unit stances (docs/Design/Stances.md §1). The byte
/// values cross the lockstep wire in SetStance's TargetEntityId, so they are
/// fixed — append, never renumber.</summary>
public enum UnitStanceMode : byte
{
    Aggressive = 0,
    Defensive = 1,
    Hold = 2,
}

/// <summary>
/// A unit's stance — a MODE, not an order: it survives every order including
/// Stop. Stamped on every unit by UnitStanceSystem with its faction's default
/// (humans Defensive, AI Aggressive).
///
/// Hold is ALSO marked by <see cref="HoldPositionTag"/>, which half the combat
/// code (and the Silence vigil) already keys on; <c>StanceCommandHelper.Apply</c>
/// keeps the two in step. The tag wins when they disagree (a scenario that adds
/// the tag directly is on Hold).
/// </summary>
public struct UnitStance : IComponentData
{
    public UnitStanceMode Value;
}

/// <summary>
/// Per-unit engagement bookkeeping owned by TargetingSystem.
///
/// <see cref="AutoTarget"/> is what makes a target AUTOMATIC: the current
/// Target is leashed only while it equals the entity recorded here, so a
/// player's (or the AI's) explicit attack order — which installs a Target
/// without touching this — is never leashed. AttackCommandHelper clears it.
/// </summary>
public struct UnitEngagement : IComponentData
{
    /// <summary>The target TargetingSystem acquired on its own; Null when the
    /// current target (if any) was ordered.</summary>
    public Entity AutoTarget;
    /// <summary>Where the leash is measured from: the guard point for an idle
    /// unit, the unit's own position at acquisition for an attack-mover or a
    /// patroller.</summary>
    public float3 Anchor;
    /// <summary>Leash length for the current auto engagement (m).</summary>
    public float Leash;
    /// <summary>Health last tick — a drop is how "was I hit" is detected
    /// without every damage path having to stamp a time.</summary>
    public int LastHealth;
    /// <summary>TargetingSystem clock at the last health drop.</summary>
    public float HitAt;
    /// <summary>No auto-acquisition before this TargetingSystem clock value
    /// (set when a leash breaks).</summary>
    public float NextAcquireAt;
    /// <summary>Acquisition stagger phase (0..3), from the network id so it
    /// is the same on every peer.</summary>
    public byte ScanPhase;
}

/// <summary>
/// A FIXED MOUNT: auto-fires at any hostile inside its own attack reach
/// whatever its stance, and never moves for any target — ordered or not
/// (docs/Design/Stances.md §1a). An emplaced engine (EmplacedEngineTag) is
/// always treated as one without carrying this tag; scenarios that need a
/// planted shooter (the Hold stance itself is passive) add it directly.
/// </summary>
public struct StationaryAutoFire : IComponentData { }

/// <summary>
/// Stance tunables, published by UnitStanceSystem from UnitStanceSystem.asset
/// so the Bursted TargetingSystem can read them. Singleton.
/// </summary>
public struct StanceSettings : IComponentData
{
    public float AggressiveLeash;
    public float RetaliationWindow;
    public float LeashReacquireCooldown;
    public float UnderFireWindow;
}
