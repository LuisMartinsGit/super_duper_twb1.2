// AntiquityComponents.cs
// ECS components for the Sect of Antiquity's full mechanic set
// (task-063 spec — "the holy librarians", intel & enemy shutdown):
//   * Recall the Codex (active power): CodexFrozen.
//   * Timed fog reveals (active/scry): SectRevealMarker.
//   * The Lorekeeper (unit lever): LorekeeperTag + StealthRevealed.
//   * The Reliquary (building lever): its tag/state + factory live with the
//     building at Buildings/Sects/Reliquary/.
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
