// TacticalQuery.cs
// Reusable spatial strength queries for AI decisions (AI plan M1).
// Brute-force over a cached unit snapshot query — called a handful of times
// per AI think tick (>= 0.5 s cadence), so O(N units) per call is fine.
//
// Location: Assets/Scripts/AI/TacticalQuery.cs

using Unity.Collections;
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

        private static int StrengthInRadius(EntityManager em, float3 pos, float radius, Faction faction, bool matchFaction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);

            float r2 = radius * radius;
            int sum = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                // "same" now means "on my side" — own faction or an ally — so
                // ally scans find teammates and enemy scans skip them.
                // docs/Design/Teams.md
                bool same = Alliances.AreAllied(faction, facs[i].Value);
                if (matchFaction != same) continue;
                if (hps[i].Value <= 0) continue;
                // Feraldis Plunderers are economy, not army — see
                // IntelSystem.Classify. Counting them inflated both threat
                // assessments and own-strength reads.
                if (em.HasComponent<PlundererTag>(ents[i])) continue;
                float dx = xfs[i].Position.x - pos.x;
                float dz = xfs[i].Position.z - pos.z;
                if (dx * dx + dz * dz > r2) continue;
                int dmg = em.HasComponent<Damage>(ents[i]) ? em.GetComponentData<Damage>(ents[i]).Value : 0;
                sum += math.max(0, dmg * 2 + hps[i].Value / 10);
            }
            return sum;
        }
    }
}
