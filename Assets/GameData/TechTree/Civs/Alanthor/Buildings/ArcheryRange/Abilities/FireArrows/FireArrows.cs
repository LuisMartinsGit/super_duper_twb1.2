// FireArrows.cs — the Archery Range's Fire Arrows research (decision 112,
// docs/Design/Combat_Pacing.md § Buildings): once researched, the arrows of
// the faction's Archers and Longbowmen land on a BUILDING as siege damage —
// the building's siege armour answers instead of its ranged armour, and the
// one Fortress takes them at its siege share.
//
// Walls stay out (the Wall Rule: an archer never targets a wall piece), and
// so does the Crossbowman. An arrow whose shooter died in flight is a plain
// arrow. Read live from FactionResearchState where the arrow LANDS
// (ProjectileSystem), so it is the same on every lockstep peer and needs no
// stamped state on the units.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    public static class FireArrows
    {
        public const string TechId = "FireArrows";

        private static readonly FixedString64Bytes ArcherId = "Alanthor_Archer";
        private static readonly FixedString64Bytes LongbowmanId = "Alanthor_Longbowman";

        /// <summary>The damage type this shot lands as on <paramref name="target"/>:
        /// Siege for a researched Archer's / Longbowman's arrow on a building,
        /// otherwise <paramref name="type"/> unchanged.</summary>
        public static DamageType TypeOnImpact(EntityManager em, Entity shooter, Entity target, DamageType type)
        {
            if (type != DamageType.Ranged) return type;
            if (shooter == Entity.Null || !em.Exists(shooter)) return type;
            if (!em.HasComponent<BuildingTag>(target) || em.HasComponent<WallTag>(target)) return type;
            if (!em.HasComponent<UnitTypeId>(shooter) || !em.HasComponent<FactionTag>(shooter)) return type;

            var id = em.GetComponentData<UnitTypeId>(shooter).Value;
            if (id != ArcherId && id != LongbowmanId) return type;

            var rs = FactionResearchState.Instance;
            if (rs == null || !rs.HasResearched(em.GetComponentData<FactionTag>(shooter).Value, TechId)) return type;
            return DamageType.Siege;
        }
    }
}
