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

/// <summary>
/// Writ of Attainder's evidence: how many units of each faction THIS unit has
/// killed. One counter per player faction, because the question the power asks
/// is "how many of MINE have you killed" and a unit that has cut through three
/// armies owes each of them separately.
///
/// It is stamped by SectAntiquityTallySystem on any killer whose victim
/// belonged to an Antiquity player — the same pass that keeps the Passive's
/// per-class tally, so the sect's two halves read one book. A unit no
/// Antiquity player has lost anything to never carries this at all.
/// </summary>
public struct AttainderLedger : IComponentData
{
    // Byte per faction rather than an array: IComponentData cannot hold a
    // managed array, and Faction has eight players (Blue=0 .. White=7).
    public byte K0, K1, K2, K3, K4, K5, K6, K7;

    public byte Against(Faction f) => (int)f switch
    {
        0 => K0, 1 => K1, 2 => K2, 3 => K3,
        4 => K4, 5 => K5, 6 => K6, 7 => K7,
        _ => 0,
    };

    /// <summary>Caps at 250 — a byte's worth of grievance is already more
    /// than any real match produces, and the damage is uncapped without it.</summary>
    public void Record(Faction f)
    {
        switch ((int)f)
        {
            case 0: if (K0 < 250) K0++; break;
            case 1: if (K1 < 250) K1++; break;
            case 2: if (K2 < 250) K2++; break;
            case 3: if (K3 < 250) K3++; break;
            case 4: if (K4 < 250) K4++; break;
            case 5: if (K5 < 250) K5++; break;
            case 6: if (K6 < 250) K6++; break;
            case 7: if (K7 < 250) K7++; break;
        }
    }
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
