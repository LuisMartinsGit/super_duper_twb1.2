// The Feraldis verb: CORRUPT a well, then break it while the curse defends.
// Canon: docs/Design/Age_1_Feraldis.md § Corruptor.
//
// Alanthor purifies a well and holds it. Runai pacifies and holds it.
// Feraldis does neither — it cracks the well open and kills it. The
// Corruptor's channel does not claim anything; it strips the well's
// protection for a fixed window, and the ARMY has to do the rest before the
// window closes.

using Unity.Entities;

/// <summary>Marks the Feraldis religious unit (formerly the Iconoclast).
/// Carried alongside <c>IconoclastTag</c> for reference stability.</summary>
public struct CorruptorTag : IComponentData { }

/// <summary>Player/AI order: go corrupt this well.</summary>
public struct CorruptCommand : IComponentData
{
    public Entity TargetNode;
}

/// <summary>
/// A well cracked open by a Corruptor. While present the well can be
/// damaged AND auto-acquired, and the curse spawns defenders at it.
/// When it expires the well seals again, undamaged progress kept.
/// </summary>
public struct WellCorrupted : IComponentData
{
    /// <summary>Seconds of vulnerability left.</summary>
    public float Remaining;

    /// <summary>Who cracked it — used for kill attribution and to aim the
    /// defenders at the right army.</summary>
    public Faction Corruptor;

    /// <summary>Seconds until the next defender wave.</summary>
    public float WaveTimer;

    /// <summary>Defenders spawned so far for this corruption (capped).</summary>
    public int DefendersSpawned;

    /// <summary>Total window length, so the wave cadence can ramp with
    /// elapsed fraction.</summary>
    public float TotalSeconds;

    /// <summary>
    /// Well health at the previous tick. While the well is LOSING health the
    /// timer is held — an assault that is actually landing damage gets the
    /// time it needs, and only an abandoned corruption runs out.
    ///
    /// Without this the window was a hard 60 s against 4000 HP, so the well
    /// resealed mid-assault every time and the Feraldis victory condition
    /// was effectively unreachable.
    /// </summary>
    public int LastHealth;

    /// <summary>Seconds of held-open time used so far, so a trickle of chip
    /// damage cannot keep a well cracked forever.</summary>
    public float HeldSeconds;
}

/// <summary>
/// Present on every well that must NOT be auto-acquired by ordinary target
/// scanning. TargetingSystem excludes this instead of excluding
/// BorderMainNodeTag outright, so a CORRUPTED well (which has the tag
/// removed) can finally be swarmed by an army that is simply attack-moving
/// onto it — the old query excluded wells unconditionally, which meant a
/// "vulnerable" well would still have been ignored by everything except a
/// hand-issued attack order on each individual unit.
/// </summary>
public struct NodeNoAutoAcquire : IComponentData { }
