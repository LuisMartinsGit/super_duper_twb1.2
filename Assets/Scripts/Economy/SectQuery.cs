// SectQuery.cs
// Read API for sect-effect systems. Every system that wants to gate behaviour
// on a faction having adopted a sect at a given lever level goes through here
// (rather than poking SectAdoptionState directly), so the query path stays
// uniform and the SectAdoption / SectAdoptionState internals can evolve
// without touching every consumer.
//
// task-063 phase 2b.
//
// Location: Assets/Scripts/Economy/SectQuery.cs

using Unity.Entities;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Static helpers for reading per-faction sect adoption state.
    /// Non-mutating — adoption / upgrade transitions go through SectAdoption.
    /// </summary>
    public static class SectQuery
    {
        /// <summary>
        /// True if <paramref name="faction"/> has <paramref name="sectId"/> adopted
        /// AND the given <paramref name="lever"/> is at <paramref name="minLevel"/> or higher.
        /// minLevel 1 is the default (any adopted sect grants Lv I on every lever).
        /// </summary>
        public static bool IsAdoptedAtLeast(
            EntityManager em, Faction faction, string sectId,
            SectLeverKind lever, byte minLevel = 1)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return false;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<SectAdoptionState>(bank)) return false;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            var sect  = state.Get(idx);
            if (!sect.IsAdopted) return false;

            return sect.LevelOf(lever) >= minLevel;
        }

        /// <summary>
        /// Returns the level (0/1/2/3) of a sect's lever for a faction.
        /// 0 means either not adopted or lever not bought yet.
        /// </summary>
        public static byte LevelOf(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return 0;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return 0;
            if (!em.HasComponent<SectAdoptionState>(bank)) return 0;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            return state.Get(idx).LevelOf(lever);
        }

        /// <summary>
        /// True if the faction has the sect adopted at all (any level on any lever).
        /// Cheap pre-filter for systems that want to skip the more expensive
        /// per-lever lookup on factions that haven't even adopted the sect.
        /// </summary>
        public static bool IsAdopted(EntityManager em, Faction faction, string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return false;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<SectAdoptionState>(bank)) return false;

            return em.GetComponentData<SectAdoptionState>(bank).Get(idx).IsAdopted;
        }
    }
}
