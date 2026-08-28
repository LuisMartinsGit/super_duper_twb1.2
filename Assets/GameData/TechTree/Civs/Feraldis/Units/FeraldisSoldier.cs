// One definition of "a Feraldis soldier", shared by the warpath and the
// marching-influence systems.
//
// WHY THIS ISN'T JUST FeraldisUnitTag: that tag is stamped by the Feraldis
// unit FACTORIES, so it only covers Feraldis-SPECIFIC unit types. A Feraldis
// player's Spearmen and Archers come from the SHARED Age 0 roster and carry
// no such tag — as do Litharchs and every sect unit. Keying the culture's
// two territory mechanics on the tag meant most of a Feraldis army was
// inert: it neither claimed ground nor cleared curse.
//
// The right question is not "was this unit built by a Feraldis factory" but
// "is this a soldier belonging to a Feraldis faction". That is what this
// answers.

using Unity.Entities;

namespace TheWaningBorder.Systems.Border
{
    public static class FeraldisSoldier
    {
        /// <summary>
        /// True for any combat unit owned by a faction whose age-up to
        /// Feraldis has COMPLETED, plus Plunderers (raiders are economy-class
        /// but are absolutely out on the map doing this work).
        ///
        /// Excludes a worker still on build duty — a conscripted one counts,
        /// a builder pottering around the base does not. Free home ground is
        /// Alanthor's identity, not Feraldis's.
        /// </summary>
        public static bool Is(EntityManager em, Entity e, Faction owner)
        {
            if (CultureConfig.GetCompletedCulture(em, owner) != Cultures.Feraldis) return false;
            if (em.HasComponent<DeathAnimationState>(e)) return false;

            // Builders on build duty claim and clear nothing.
            if (em.HasComponent<FeraldisWorkerTag>(e)
                && !em.HasComponent<ConscriptedTag>(e)) return false;

            // Raiders count even though they are Economy class.
            if (em.HasComponent<PlundererTag>(e)) return true;

            if (!em.HasComponent<UnitTag>(e)) return false;
            var cls = em.GetComponentData<UnitTag>(e).Class;
            return cls == UnitClass.Melee || cls == UnitClass.Ranged
                || cls == UnitClass.Siege || cls == UnitClass.Magic
                || cls == UnitClass.Support
                // A conscripted worker is Economy class but is a soldier now.
                || em.HasComponent<ConscriptedTag>(e);
        }
    }
}
