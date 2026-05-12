// MatchPhase.cs
// Spec §1: the match progresses through two phases per faction:
//   - Timeless Age: faction has not committed to a culture
//                   (FactionProgress.Culture == Cultures.None,
//                    FactionEra.Value == 1, no AgeUpState completed).
//                   PVE-heavy phase: curse spreads, players prepare.
//   - Culture Progression Age: faction has committed to a culture via
//                   AgeUpSystem (Culture != None, Era >= 2). Asymmetric
//                   mechanics activate over a 2-minute transition window.
//
// No new components needed — the existing AgeUpSystem already drives the
// transition (sets FactionProgress.Culture + FactionEra on Hall + bank).
// This helper just centralises the "is X faction in Culture Progression?"
// check so callers don't have to remember which field to read.
//
// Faction-specific 2-minute transitions are already wired (Alanthor's
// GathererHut self-destruct timer, etc.) — kept in AgeUpSystem.
//
// Location: Assets/Scripts/Core/Settings/

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Core.Settings
{
    public static class MatchPhase
    {
        /// <summary>
        /// True if the faction has committed to a culture (post-age-up).
        /// Reads FactionProgress.Culture from the faction's Hall — Culture
        /// transitions atomically when AgeUpSystem completes.
        /// </summary>
        public static bool IsCultureProgression(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progress = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                return progress[i].Culture != Cultures.None;
            }
            return false;
        }

        /// <summary>
        /// Inverse: true if the faction is still in the Timeless Age (PVE phase).
        /// </summary>
        public static bool IsTimeless(EntityManager em, Faction faction)
            => !IsCultureProgression(em, faction);

        /// <summary>
        /// True if the faction is mid-transition (AgeUpState present on its Hall).
        /// Some systems gate behavior during the channel — e.g. Hall can't train
        /// during age-up.
        /// </summary>
        public static bool IsTransitioning(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<AgeUpState>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value == faction) return true;
            }
            return false;
        }
    }
}
