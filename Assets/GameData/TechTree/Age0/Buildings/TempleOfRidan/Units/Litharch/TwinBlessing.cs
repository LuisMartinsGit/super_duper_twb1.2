// TwinBlessing.cs — the Litharch's tier-3 skill (decision 77, Age_0.md §
// Litharch upgrades): every heal also mends the most-wounded OTHER ally within
// the Litharch's heal range, for the same amount. Called from the heal tick in
// LitharchHealingSystem; deterministic (lowest HP share first, ties to the
// lower entity index).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    public static class TwinBlessing
    {
        public const string TechId = "TwinBlessing";

        public static bool Researched(Faction faction)
        {
            var rs = FactionResearchState.Instance;
            return rs != null && rs.HasResearched(faction, TechId);
        }

        public static void HealSecond(EntityManager em, Entity healer, Entity first, float3 at,
            Faction faction, float range, int amount,
            NativeArray<Entity> cands, NativeArray<LocalTransform> candXf, NativeArray<FactionTag> candFaction)
        {
            float r2 = range * range;
            Entity best = Entity.Null;
            float bestShare = float.MaxValue;
            for (int i = 0; i < cands.Length; i++)
            {
                var e = cands[i];
                if (e == healer || e == first) continue;
                if (!Alliances.AreAllied(faction, candFaction[i].Value)) continue;
                float dx = candXf[i].Position.x - at.x, dz = candXf[i].Position.z - at.z;
                if (dx * dx + dz * dz > r2) continue;
                if (!em.Exists(e) || em.HasComponent<UnhealableTag>(e)) continue;
                var h = em.GetComponentData<Health>(e);
                if (h.Value <= 0 || h.Value >= h.Max || h.Max <= 0) continue;
                float share = h.Value / (float)h.Max;
                if (share < bestShare || (share == bestShare && e.Index < best.Index))
                { bestShare = share; best = e; }
            }
            if (best == Entity.Null) return;
            var hp = em.GetComponentData<Health>(best);
            hp.Value = math.min(hp.Max, hp.Value + amount);
            em.SetComponentData(best, hp);
        }
    }
}
