// AntiquityComponents.cs
// ECS components for the Sect of Antiquity's full mechanic set
// (task-063 spec — "the holy librarians", intel & enemy shutdown):
//   * Recall the Codex (active power): CodexFrozen.
//   * Timed fog reveals (active/scry): SectRevealMarker.
//   * The Lorekeeper (unit lever): LorekeeperTag + StealthRevealed.
//   * The Reliquary (building lever): ReliquaryTag + ReliquaryState.
// All in the global namespace per project convention.

using Unity.Entities;

/// <summary>
/// Recall the Codex: while present on a unit, its attack and ability
/// cooldowns do NOT recover (combat systems skip their cooldown ticks).
/// Stamped by the Antiquity active power / Reliquary lockout.
/// </summary>
public struct CodexFrozen : IComponentData
{
    public float TimeRemaining;
}

/// <summary>
/// Timed fog-of-war reveal: an invisible entity carrying FactionTag +
/// LocalTransform + LineOfSight — FogOfWarSystem stamps its vision like any
/// unit's. SectRevealTickSystem destroys it when the timer runs out.
/// </summary>
public struct SectRevealMarker : IComponentData
{
    public float TimeRemaining;
}

/// <summary>Marker for the Lorekeeper (Antiquity unit lever).</summary>
public struct LorekeeperTag : IComponentData { }

/// <summary>
/// Stamped on a stealthed enemy inside a Lorekeeper's detection radius —
/// TargetingSystem treats the unit as visible while this holds.
/// </summary>
public struct StealthRevealed : IComponentData
{
    public float TimeRemaining;
}

/// <summary>Marker for The Reliquary (Antiquity building lever).</summary>
public struct ReliquaryTag : IComponentData { }

/// <summary>
/// Per-Reliquary ability cooldowns. Ability availability is level-gated
/// (Building lever): Lv I = Scry only; Lv II = all three; Lv III = base
/// cooldowns -30% and garrison effects doubled. A Lorekeeper standing
/// within garrison range further multiplies cooldown recovery.
/// </summary>
public struct ReliquaryState : IComponentData
{
    public float ScryCooldown;      // remaining seconds (0 = ready)
    public float LockoutCooldown;
    public float VisionCooldown;
}
