// GatherersHutComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Resource collection building.</summary>
public struct GathererHutTag : IComponentData { }

/// <summary>Tracks farm placement order for priority-based income (first-come-first-served).</summary>
public struct FarmBuildOrder : IComponentData { public int Value; }

/// <summary>
/// Cooldown state for the Guild's low-HP defensive casts. TWO independent
/// wards, each a one-shot burst on its own 90-second cooldown, NOT a
/// continuous aura: Veilstone Walls fires the SLOW burst at 75% HP or
/// lower; Veilsteel Pylons fires the STOP burst at 50% HP or lower. Each
/// field is the earliest <c>SystemAPI.Time.ElapsedTime</c> at which that
/// ward may cast again. Added lazily by
/// <c>GathererHutReinforcementSystem</c> the first time a hut casts.
/// </summary>
public struct GathererHutWardState : IComponentData
{
    public double NextSlowCastAt;
    public double NextStopCastAt;
}

/// <summary>
/// Countdown timer for automatic building destruction with resource refund.
/// Added to GathererHuts when player chooses Alanthor culture.
/// </summary>
public struct SelfDestructTimer : IComponentData
{
    public float TimeRemaining;  // Seconds until destruction
    public float Duration;       // Original duration (for progress bar display)
    public byte RefundPaid;      // 1 = resources already refunded
}

/// <summary>
/// Marker added by <see cref="TheWaningBorder.Systems.Work.AgeUpSystem"/> to
/// every Alanthor-owned Gatherer's Hut at age-up. Presence of this tag means
/// "the hut is awaiting a player choice: convert to Wall Hub, or to Watch
/// Tower". The selection panel surfaces two large action cells; clicking one
/// fires <see cref="TheWaningBorder.Core.Commands.Types.ConvertHutCommand"/>,
/// which charges the conversion cost, removes this marker, and adds
/// <see cref="GathererHutConverting"/>.
/// </summary>
public struct GathererHutAgeUpChoice : IComponentData { }

/// <summary>
/// Active 5-second conversion timer on a Gatherer's Hut. While present the
/// hut is locked-in to a destination (no cancel in v1 — task-109 Phase 1
/// canonicalised this). When <see cref="Remaining"/> reaches zero,
/// <c>HutConversionSystem</c> destroys the hut and spawns the chosen target.
/// </summary>
public struct GathererHutConverting : IComponentData
{
    public HutConversionTarget Target;
    public float Remaining;
    public float Total;
}

/// <summary>
/// Conversion destination for the Gatherer's Hut age-up choice.
/// Byte-sized so it round-trips through the lockstep wire (packed into
/// LockstepCommand.TargetEntityId).
/// </summary>
public enum HutConversionTarget : byte
{
    None = 0,
    WallHub = 1,
    WatchTower = 2,
}
