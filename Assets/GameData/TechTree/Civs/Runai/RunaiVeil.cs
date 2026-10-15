// RunaiVeil.cs
// Where each faction stands on the Sanctuary curse ladder
// (docs/Design/Age_1_Runai.md §5.2), as one byte per faction slot that the
// shared combat and curse code can read without knowing about the Runai:
//
//   0  nothing
//   1  Veiled Pilgrims   the curse ignores caravans, trader-warriors, workers
//   2  Hushed Veil       the curse notices Runai units only up close; no waves
//   3  Communion         Runai units regenerate on curse-held ground
//   4  Veilbound         curse units near Sanctuaries join the faction
//   5  Voice of the Veil peace with the curse (Alliances)
//
// Written once a second by RunaiSanctuarySystem from research state — which
// is lockstep simulation state — so every peer reads the same table. The
// tier table lives in a SharedStatic because TargetingSystem's acquire scan is
// Burst-compiled and Burst cannot read a managed static. A new match epoch
// clears it the first time the system writes.

using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

public static class RunaiVeil
{
    public const byte VeiledPilgrims = 1;
    public const byte HushedVeil = 2;
    public const byte Communion = 3;
    public const byte Veilbound = 4;
    public const byte VoiceOfTheVeil = 5;

    public struct Table
    {
        public FixedList32Bytes<byte> Tiers;
        /// <summary>Scan-radius multiplier the curse uses against a Hushed faction.</summary>
        public float HushedScanMultiplier;
    }

    private struct TableContext { }
    private struct TableKey { }
    private static readonly SharedStatic<Table> _shared = SharedStatic<Table>.GetOrCreate<TableContext, TableKey>();

    /// <summary>Scan-radius multiplier against a Hushed faction (Burst-safe read).</summary>
    public static float HushedScanMultiplier => _shared.Data.HushedScanMultiplier;

    /// <summary>The faction's ladder tier. Burst-safe.</summary>
    public static byte TierOf(Faction f) => TierOf((int)f);

    /// <summary>The faction slot's ladder tier. Burst-safe.</summary>
    public static byte TierOf(int factionIndex)
    {
        var t = _shared.Data.Tiers;
        return factionIndex >= 0 && factionIndex < t.Length ? t[factionIndex] : (byte)0;
    }

    /// <summary>Publish every slot's tier and the Hushed multiplier (managed, main thread).</summary>
    public static void Publish(byte[] tiers, float hushedScanMultiplier)
    {
        RunaiVeilNodes.ReSync();
        var t = new Table { HushedScanMultiplier = hushedScanMultiplier };
        for (int i = 0; i < tiers.Length && i < Alliances.MaxFactions; i++) t.Tiers.Add(tiers[i]);
        _shared.Data = t;
    }

    internal static void Clear() => _shared.Data = default;
}

/// <summary>The managed half of the Runai curse ladder: the ladder's tech ids,
/// the per-match reset and the sleeping curse nodes. Kept out of
/// <see cref="RunaiVeil"/> because Burst compiles that class's static
/// constructor and cannot hold managed arrays.</summary>
public static class RunaiVeilNodes
{
    /// <summary>The techs in ladder order; index + 1 is the tier.</summary>
    public static readonly string[] Ladder =
    {
        "Runai_VeiledPilgrims", "Runai_HushedVeil", "Runai_Communion",
        "Runai_Veilbound", "Runai_VoiceOfTheVeil",
    };

    private static int _epoch = -1;

    // ── Sleeping curse nodes ────────────────────────────────────────────
    // Positions of Sanctuaries standing on a Cursed outcrop (XZ). A garrison
    // never rises at a node within SleepRadius of one. Managed: read only by
    // the managed CurseTerritorySystem.

    private static float2[] _sleeping = new float2[0];
    private static int _sleepingCount;
    public const float SleepRadius = 4f;

    public static void SetSleepingNodes(System.Collections.Generic.List<float2> at)
    {
        ReSync();
        if (_sleeping.Length < at.Count) _sleeping = new float2[math.max(8, at.Count)];
        for (int i = 0; i < at.Count; i++) _sleeping[i] = at[i];
        _sleepingCount = at.Count;
    }

    /// <summary>True when a Sanctuary sleeps the curse node at this point.</summary>
    public static bool SleepsAt(float3 node)
    {
        if (_epoch != SimCadence.Epoch) return false;
        float r2 = SleepRadius * SleepRadius;
        for (int i = 0; i < _sleepingCount; i++)
            if (math.distancesq(_sleeping[i], node.xz) <= r2) return true;
        return false;
    }

    internal static void ReSync()
    {
        if (_epoch == SimCadence.Epoch) return;
        _epoch = SimCadence.Epoch;
        _sleepingCount = 0;
        RunaiVeil.Clear();
        Alliances.ClearCursePeace();
    }
}
