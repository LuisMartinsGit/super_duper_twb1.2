// SectQuery.cs
// Read API for sect-effect systems. Every system that wants to gate behaviour
// on a faction having adopted a sect at a given lever level goes through here
// (rather than poking SectAdoptionState directly), so the query path stays
// uniform and the SectAdoption / SectAdoptionState internals can evolve
// without touching every consumer.
//
// task-063 phase 2b.

using Unity.Collections;
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
        /// Does this faction have a STANDING (completed) Temple of Ridan?
        ///
        /// Design rule: a sect's PASSIVE lever is live only while the Temple
        /// stands — raze the Temple and every adopted sect's passive goes
        /// quiet until it is rebuilt. Actives and adoption itself are NOT
        /// gated this way, which is why the check sits on the Passive branch
        /// of LevelOf rather than on IsAdopted.
        /// </summary>
        public static bool HasStandingTemple(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<TempleOfRidanTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                },
                None = new[] { ComponentType.ReadOnly<UnderConstruction>() },
            });
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<Health>(ents[i])
                    && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                return true;
            }
            return false;
        }

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

            // Passives sleep while the Temple is down (see HasStandingTemple).
            if (lever == SectLeverKind.Passive && !HasStandingTemple(em, faction)) return false;

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

            if (lever == SectLeverKind.Passive && !HasStandingTemple(em, faction)) return 0;

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

        /// <summary>
        /// The sect's POWER level (1/2/3) for this faction — what its three
        /// actives scale on. Earned by adoption timing, not bought: it counts
        /// the Temple upgrades that happened while the sect was already
        /// adopted (docs/Design/Sects.md section 3).
        ///
        /// Returns 1, not 0, for an adopted sect whose stored level is still
        /// zero. Saves written before the PowerLevel field existed load with a
        /// zero there, and a level-0 power would silently do nothing; treating
        /// it as Lv I degrades those sects rather than breaking them.
        /// </summary>
        public static byte PowerLevelOf(EntityManager em, Faction faction, string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return 0;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return 0;
            if (!em.HasComponent<SectAdoptionState>(bank)) return 0;

            var sect = em.GetComponentData<SectAdoptionState>(bank).Get(idx);
            if (!sect.IsAdopted) return 0;
            return sect.PowerLevel < 1 ? (byte)1 : sect.PowerLevel > 3 ? (byte)3 : sect.PowerLevel;
        }
    }
}
