// Alliances.cs
// The faction relationship table. Canonical spec: docs/Design/Teams.md
// Location: Assets/Scripts/Core/Settings/Alliances.cs

/// <summary>
/// The single authority on whether two factions are hostile.
///
/// Before teams, hostility was expressed everywhere as a raw
/// <c>factionA == factionB</c> comparison. That is no longer a valid test:
/// use <see cref="AreHostile"/> for "may I shoot this" and
/// <see cref="AreAllied"/> for "may I heal / buff / open my gate for this".
///
/// The table is a flat faction-indexed byte array so Burst jobs and ECS
/// systems can read it without touching managed lobby state. It is written
/// once at match start (<see cref="ApplyFromLobby"/>) and read-only after.
/// </summary>
public static class Alliances
{
    /// <summary>Faction slots 0..7 plus Border at 8.</summary>
    public const int MaxFactions = 9;

    /// <summary>Team 0 means "no team" — a faction that fights alone and is
    /// hostile to everyone, including other unteamed factions.</summary>
    public const byte NoTeam = 0;

    /// <summary>How many distinct teams the lobby offers, beyond "no team".</summary>
    public const byte MaxTeams = 4;

    // faction index -> team index. All-zero == everyone unteamed == free-for-all,
    // which is the pre-teams behaviour and the default.
    private static readonly byte[] _teamOf = new byte[MaxFactions];

    /// <summary>Reset every faction to "no team" (free-for-all).</summary>
    public static void Clear()
    {
        for (int i = 0; i < MaxFactions; i++) _teamOf[i] = NoTeam;
        PublishShared();
    }

    /// <summary>Assign one faction to a team. 0 clears it to "no team".</summary>
    public static void SetTeam(Faction faction, byte team)
    {
        int i = (int)faction;
        if (i < 0 || i >= MaxFactions) return;
        _teamOf[i] = team > MaxTeams ? NoTeam : team;
        PublishShared();
    }

    // ── Burst-visible mirror ────────────────────────────────────────────
    //
    // Jobs (the nav flow-field gate checks in particular) cannot read a
    // managed static array, and threading a NativeArray field through every
    // job that needs a hostility verdict would touch a lot of delicate nav
    // code. A SharedStatic gives Burst direct access to the same table.

    // SharedStatic.GetOrCreate needs two non-static types purely as identity
    // keys; a static class cannot be used as a type argument.
    private struct TeamTableContext { }
    private struct TeamTableKey { }

    /// <summary>Blittable mirror of the team table, readable from Burst.</summary>
    public struct TeamTable
    {
        public Unity.Collections.FixedList32Bytes<byte> Teams;
    }

    private static readonly Unity.Burst.SharedStatic<TeamTable> _shared =
        Unity.Burst.SharedStatic<TeamTable>.GetOrCreate<TeamTableContext, TeamTableKey>();

    private static void PublishShared()
    {
        var t = new TeamTable();
        for (int i = 0; i < MaxFactions; i++) t.Teams.Add(_teamOf[i]);
        _shared.Data = t;
    }

    /// <summary>
    /// Burst-safe hostility test. Same rules as <see cref="AreHostile"/>,
    /// reading the SharedStatic mirror instead of the managed array. Falls
    /// back to plain "different faction == hostile" if the mirror has not been
    /// published yet, which is the pre-teams behaviour.
    /// </summary>
    public static bool AreHostileBurst(Faction a, Faction b)
    {
        if (a == b) return false;                                 // includes Border vs Border
        if (a == Faction.Border || b == Faction.Border) return true;

        var teams = _shared.Data.Teams;
        int ia = (int)a, ib = (int)b;
        if (ia < 0 || ia >= teams.Length || ib < 0 || ib >= teams.Length) return true;

        byte ta = teams[ia];
        if (ta == NoTeam) return true;
        return ta != teams[ib];
    }

    /// <summary>Burst-safe complement of <see cref="AreHostileBurst"/>.</summary>
    public static bool AreAlliedBurst(Faction a, Faction b) => !AreHostileBurst(a, b);

    /// <summary>Burst-safe ally test on raw faction INDICES, for the nav cost
    /// field's packed owner bits.</summary>
    public static bool AreAlliedBurst(byte factionIdxA, byte factionIdxB)
        => AreAlliedBurst((Faction)factionIdxA, (Faction)factionIdxB);

    /// <summary>The team a faction belongs to, 0 for none.</summary>
    public static byte TeamOf(Faction faction)
    {
        int i = (int)faction;
        return (i < 0 || i >= MaxFactions) ? NoTeam : _teamOf[i];
    }

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> are on the same
    /// side.
    ///
    /// Self-identity is checked FIRST and deliberately: the curse is allied
    /// with itself and with nothing else. Testing for Border before self would
    /// make Faction.Border hostile to Faction.Border and set the horde fighting
    /// itself.
    /// </summary>
    public static bool AreAllied(Faction a, Faction b)
    {
        if (a == b) return true;                                  // includes Border vs Border
        if (a == Faction.Border || b == Faction.Border) return false;

        byte ta = TeamOf(a);
        if (ta == NoTeam) return false;      // unteamed: allied with nobody but self
        return ta == TeamOf(b);
    }

    /// <summary>
    /// True when <paramref name="a"/> may damage <paramref name="b"/>. The
    /// exact complement of <see cref="AreAllied"/>, expressed separately
    /// because it is the reading almost every call site actually wants.
    /// </summary>
    public static bool AreHostile(Faction a, Faction b) => !AreAllied(a, b);

    /// <summary>
    /// Copy the lobby's per-slot team assignments into the table. Called once
    /// at match start, before any system reads the table.
    /// </summary>
    public static void ApplyFromLobby()
    {
        Clear();
        var slots = TheWaningBorder.Core.Config.LobbyConfig.Slots;
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null) continue;
            if (slot.Type == TheWaningBorder.Core.Config.SlotType.Empty) continue;
            SetTeam(slot.Faction, slot.TeamIndex);
        }
    }

    /// <summary>
    /// Snapshot of the table for Burst / job code, which cannot call into
    /// managed statics. Index by <c>(int)Faction</c>.
    /// </summary>
    public static void CopyTo(byte[] dest)
    {
        if (dest == null || dest.Length < MaxFactions) return;
        for (int i = 0; i < MaxFactions; i++) dest[i] = _teamOf[i];
    }

    /// <summary>
    /// Burst-safe hostility test against a snapshot taken with
    /// <see cref="CopyTo"/>. Same rules as <see cref="AreHostile"/>.
    /// </summary>
    public static bool AreHostile(in Unity.Collections.NativeArray<byte> teamTable,
                                  Faction a, Faction b)
    {
        if (a == b) return false;                                 // includes Border vs Border
        if (a == Faction.Border || b == Faction.Border) return true;

        int ia = (int)a, ib = (int)b;
        if (ia < 0 || ia >= teamTable.Length || ib < 0 || ib >= teamTable.Length)
            return true;

        byte ta = teamTable[ia];
        if (ta == NoTeam) return true;
        return ta != teamTable[ib];
    }
}
