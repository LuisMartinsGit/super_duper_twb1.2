// Alliances.cs
// The faction relationship table. Canonical spec: docs/Design/Teams.md

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

    // ── Peace with the curse (docs/Design/Age_1_Runai.md §5.2) ──────────
    //
    // The one way a player faction stops being the curse's enemy: the Runai
    // Voice of the Veil. A bit per player slot (0..7); the curse is allied
    // with a faction whose bit is set, in every test below. Simulation state,
    // written by RunaiSanctuarySystem from research on every peer.
    private static byte _cursePeace;

    /// <summary>True when <paramref name="faction"/> is at peace with the curse.</summary>
    public static bool AtPeaceWithCurse(Faction faction)
    {
        int i = (int)faction;
        return i >= 0 && i < 8 && (_cursePeace & (1 << i)) != 0;
    }

    public static void SetCursePeace(Faction faction, bool peace)
    {
        int i = (int)faction;
        if (i < 0 || i >= 8) return;
        byte next = peace ? (byte)(_cursePeace | (1 << i)) : (byte)(_cursePeace & ~(1 << i));
        if (next == _cursePeace) return;
        _cursePeace = next;
        PublishShared();
    }

    public static void ClearCursePeace()
    {
        if (_cursePeace == 0) return;
        _cursePeace = 0;
        PublishShared();
    }

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
        /// <summary>Bit per player faction at peace with the curse.</summary>
        public byte CursePeace;
    }

    private static readonly Unity.Burst.SharedStatic<TeamTable> _shared =
        Unity.Burst.SharedStatic<TeamTable>.GetOrCreate<TeamTableContext, TeamTableKey>();

    private static void PublishShared()
    {
        var t = new TeamTable();
        for (int i = 0; i < MaxFactions; i++) t.Teams.Add(_teamOf[i]);
        t.CursePeace = _cursePeace;
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
        if (a == Faction.Border || b == Faction.Border)
        {
            int p = a == Faction.Border ? (int)b : (int)a;
            return p < 0 || p >= 8 || (_shared.Data.CursePeace & (1 << p)) == 0;
        }

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
        if (a == Faction.Border) return AtPeaceWithCurse(b);
        if (b == Faction.Border) return AtPeaceWithCurse(a);

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
