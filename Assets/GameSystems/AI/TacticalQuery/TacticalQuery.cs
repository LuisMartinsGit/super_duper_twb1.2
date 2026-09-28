// TacticalQuery.cs
// Reusable spatial strength queries for AI decisions (AI plan M1).
// Served from AIStrengthMap — an incrementally refreshed, spatially hashed
// snapshot — because "a handful of calls per think" turned out to be hundreds
// (one per sighting, per building, per mission), each copying every unit.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    public static class TacticalQuery
    {

        /// <summary>
        /// Heuristic combat strength of a single entity: damage-weighted with a
        /// survivability term. Zero-damage units (scouts, healers pre-tech)
        /// contribute only their bulk.
        /// </summary>
        public static int UnitStrength(EntityManager em, Entity e)
        {
            int dmg = 0, hp = 0;
            if (em.HasComponent<Damage>(e)) dmg = em.GetComponentData<Damage>(e).Value;
            if (em.HasComponent<Health>(e)) hp = em.GetComponentData<Health>(e).Value;
            return math.max(0, dmg * 2 + hp / 10);
        }

        /// <summary>Total strength of <paramref name="of"/>'s combat-capable
        /// units within <paramref name="radius"/> of <paramref name="pos"/>.</summary>
        public static int FactionStrengthInRadius(EntityManager em, Faction of, float3 pos, float radius)
            => StrengthInRadius(em, pos, radius, of, matchFaction: true);

        /// <summary>Total strength of every faction EXCEPT <paramref name="notOf"/>
        /// (border included) within the radius. The "how bad is it here for me" number.</summary>
        public static int EnemyStrengthInRadius(EntityManager em, Faction notOf, float3 pos, float radius)
            => StrengthInRadius(em, pos, radius, notOf, matchFaction: false);

        /// <summary>
        /// Reads the incremental <see cref="AIStrengthMap"/> (refreshed about
        /// every five seconds, operator direction 2026-09-25) instead of
        /// copying every unit out of the world per call. Same scale, same
        /// side rule, same plunderer exclusion as the per-call scan it
        /// replaced — only up to one refresh stale.
        /// </summary>
        private static int StrengthInRadius(EntityManager em, float3 pos, float radius, Faction faction, bool matchFaction)
            => AIStrengthMap.StrengthInRadius(em, pos, radius, faction, matchFaction);
    }
}
