// LandmarkAgeUp.cs
// THE AGE-UP IS THE LANDMARK (docs/Design/Age_0.md § Age-up by landmark,
// 2026-09-29). There is no age-up research and no culture dialog any more:
// the moment a landmark FINISHES construction, its faction ages up to the
// landmark's culture.
//
//   Vault of Almierra   -> Alanthor
//   Fiendstone Keep     -> Feraldis
//   (Thessara's Crossing -> Runai, once the building exists)
//
// Called from BOTH construction-completion paths (a worker's finishing tick
// in BuildingConstructionSystem, the self-build tick in AutoConstructionSystem).
// Only one of them can fire per site — each removes UnderConstruction first.
//
// It does not run the age-up itself: it stamps a zero-length AgeUpState on the
// faction's capital, and AgeUpSystem completes it on its next update with the
// exact effect block the old research used (culture, era, huts, walls, visuals).
// Completion is simulation state reached identically on every lockstep peer,
// so no command is needed and nothing is charged here — the landmark's build
// cost IS the age-up price.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Work
{
    public static class LandmarkAgeUp
    {
        static readonly ComponentType[] QT_Capitals =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        static CachedEntityQuery QC_Capitals;

        /// <summary>The culture a landmark ages its faction into, or
        /// Cultures.None when the entity is not a landmark.</summary>
        public static byte CultureOf(EntityManager em, Entity building)
        {
            if (em.HasComponent<VaultTag>(building)) return Cultures.Alanthor;
            if (em.HasComponent<FiendstoneKeepTag>(building)) return Cultures.Feraldis;
            return Cultures.None;
        }

        /// <summary>The culture a landmark id ages into (UI / AI side).</summary>
        public static byte CultureOf(string buildingId) => buildingId switch
        {
            "VaultOfAlmierra" => Cultures.Alanthor,
            "FiendstoneKeep"  => Cultures.Feraldis,
            _                 => Cultures.None,
        };

        /// <summary>
        /// A building just finished construction. If it is a landmark, start
        /// its faction's age-up. Idempotent: a faction that already has a
        /// culture, or already carries an AgeUpState, is left alone.
        /// </summary>
        public static void OnConstructionComplete(EntityManager em, Entity building)
        {
            if (!em.HasComponent<ChoiceBuildingTag>(building)) return;
            if (!em.HasComponent<FactionTag>(building)) return;

            byte culture = CultureOf(em, building);
            if (culture == Cultures.None) return;

            var faction = em.GetComponentData<FactionTag>(building).Value;
            var capital = CapitalOf(em, faction);
            if (capital == Entity.Null) return;
            if (em.GetComponentData<FactionProgress>(capital).Culture != Cultures.None) return;
            if (em.HasComponent<AgeUpState>(capital)) return;

            em.AddComponentData(capital, new AgeUpState
            {
                Culture = culture,
                Duration = 0f,
                Remaining = 0f,
            });
        }

        /// <summary>
        /// The faction's capital: its starting Fortress if it still stands,
        /// else its lowest-index Hall-tagged building carrying FactionProgress.
        /// Entity indices are allocated identically on every lockstep peer,
        /// so the pick is deterministic.
        /// </summary>
        public static Entity CapitalOf(EntityManager em, Faction faction)
        {
            var q = QC_Capitals.Get(em, QT_Capitals);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            Entity best = Entity.Null;
            bool bestIsFortress = false;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                bool fortress = em.HasComponent<FortressTag>(ents[i]);
                if (best == Entity.Null
                    || (fortress && !bestIsFortress)
                    || (fortress == bestIsFortress && ents[i].Index < best.Index))
                {
                    best = ents[i];
                    bestIsFortress = fortress;
                }
            }
            return best;
        }
    }
}
