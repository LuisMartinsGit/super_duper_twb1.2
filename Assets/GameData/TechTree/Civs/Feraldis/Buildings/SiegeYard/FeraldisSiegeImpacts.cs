// FeraldisSiegeImpacts.cs
// What a Feraldis siege shot leaves where it lands (ProjectileSystem calls
// OnImpact for every projectile that finishes):
//   Corpse Engine — a blood pool (frenzy ground, a totem site)
//   Harpoon Rig   — a barbed-wire line from the rig to the impact point
// docs/Design/Age_1_Feraldis.md §7.5.

using TheWaningBorder.Influence;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    public static class FeraldisSiegeImpacts
    {
        public static void OnImpact(EntityManager em, EntityCommandBuffer ecb, Entity projectile, float3 at, Faction source)
        {
            if (projectile == Entity.Null || !em.Exists(projectile)) return;

            if (em.HasComponent<CorpseShot>(projectile) && BloodMap.Ready)
            {
                var c = FeraldisCombat.Cfg;
                Vector3 p = at;
                BloodMap.AddBlood(p, c.corpseBlood);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    BloodMap.AddBlood(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * c.corpseBloodRing, c.corpseBlood);
                }
            }

            if (em.HasComponent<HarpoonShot>(projectile))
            {
                var shot = em.GetComponentData<HarpoonShot>(projectile);
                var c = FeraldisCombat.Cfg;
                float life = c.wireSeconds * (FeraldisCombat.Researched(source, "Feraldis_BarbedCoils") ? c.barbedCoilsMultiplier : 1f);
                var wire = ecb.CreateEntity();
                ecb.AddComponent(wire, new BarbedWire { From = shot.From, To = at, Remaining = life, Source = source });
                ecb.AddComponent(wire, LocalTransform.FromPosition((shot.From + at) * 0.5f));
            }
        }
    }
}
