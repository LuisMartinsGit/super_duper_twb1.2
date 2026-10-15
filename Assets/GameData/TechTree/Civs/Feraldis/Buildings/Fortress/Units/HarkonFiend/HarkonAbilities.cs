// HarkonAbilities.cs
// Harkon Fiend's four abilities (docs/Design/Age_1_Feraldis.md §8,
// Heroes.md §6). Called by AbilityEffectExecutor for the FeraldisWarcry /
// FeraldisLeap / FeraldisRaze / FeraldisBloodOath effect kinds; every number
// is on the ability SO (radius, duration, cooldown, range) or FeraldisCombat.asset.

using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    public static class HarkonAbilities
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Buildings;

        /// <summary>Warcry: every allied Feraldis unit in the radius frenzies as if on blood.</summary>
        public static void Warcry(EntityManager em, Entity caster, AbilityCard card, float duration)
        {
            ForAllies(em, caster, card.Radius, u =>
            {
                if (!em.HasComponent<FeraldisUnitTag>(u)) return;
                var bf = new BloodFrenzy { Remaining = duration };
                if (em.HasComponent<BloodFrenzy>(u))
                {
                    var cur = em.GetComponentData<BloodFrenzy>(u);
                    cur.Remaining = math.max(cur.Remaining, duration);
                    em.SetComponentData(u, cur);
                }
                else em.AddComponentData(u, bf);
            });
        }

        /// <summary>Blood Oath: every allied unit in the radius cannot die for the duration.</summary>
        public static void BloodOath(EntityManager em, Entity caster, AbilityCard card, float duration)
        {
            ForAllies(em, caster, card.Radius, u =>
            {
                var cling = new LifeCling { Floor = 1, TimeRemaining = duration };
                if (em.HasComponent<LifeCling>(u)) { em.SetComponentData(u, cling); em.SetComponentEnabled<LifeCling>(u, true); }
                else em.AddComponentData(u, cling);
            });
        }

        /// <summary>Leap: jump to the aimed point (clamped) and slow every enemy where he lands.</summary>
        public static void Leap(EntityManager em, Entity caster)
        {
            if (!em.HasComponent<AbilityAimPoint>(caster) || !em.HasComponent<LocalTransform>(caster)) return;
            var c = FeraldisCombat.Cfg;
            var xf = em.GetComponentData<LocalTransform>(caster);
            float3 aim = em.GetComponentData<AbilityAimPoint>(caster).Position;
            float3 d = aim - xf.Position; d.y = 0f;
            float len = math.length(d);
            if (len > c.leapMaxDistance && len > 0.01f) aim = xf.Position + d / len * c.leapMaxDistance;
            aim.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(aim.x, aim.z);
            xf.Position = aim;
            em.SetComponentData(caster, xf);
            if (em.HasComponent<DesiredDestination>(caster))
                em.SetComponentData(caster, new DesiredDestination { Has = 0 });

            var fac = em.GetComponentData<FactionTag>(caster).Value;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            float r2 = c.leapSlowRadius * c.leapSlowRadius;
            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(fac, facs[i].Value)) continue;
                if (math.distancesq(xfs[i].Position.xz, aim.xz) > r2) continue;
                var slow = new SpellDebuff { SpeedReduction = c.leapSlow, TimeRemaining = c.leapSlowSeconds };
                if (em.HasComponent<SpellDebuff>(ents[i])) { em.SetComponentData(ents[i], slow); em.SetComponentEnabled<SpellDebuff>(ents[i], true); }
                else em.AddComponentData(ents[i], slow);
            }
        }

        /// <summary>
        /// Raze: the hostile building nearest the aimed point (wall pieces and
        /// curse nodes included) is destroyed outright; a capital-class
        /// building loses a share of its max HP instead. The last hit is the
        /// caster's, so the raze bounty / curse-node bounty pay as usual.
        /// The one non-siege effect that may destroy a wall (Combat_Pacing.md).
        /// </summary>
        public static void Raze(EntityManager em, Entity caster)
        {
            if (!em.HasComponent<AbilityAimPoint>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            var c = FeraldisCombat.Cfg;
            float3 aim = em.GetComponentData<AbilityAimPoint>(caster).Position;
            var fac = em.GetComponentData<FactionTag>(caster).Value;

            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            Entity best = Entity.Null; float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(fac, facs[i].Value)) continue;
                float d = TargetGeometry.SurfaceDistXZ(em, aim, ents[i]);
                if (d > c.razePickRadius || d >= bestD) continue;
                best = ents[i]; bestD = d;
            }
            if (best == Entity.Null) return;

            var hp = em.GetComponentData<Health>(best);
            if (hp.Value <= 0) return;
            if (em.HasComponent<LastDamagedByFaction>(best))
            {
                em.SetComponentData(best, new LastDamagedByFaction { Value = fac });
                em.SetComponentEnabled<LastDamagedByFaction>(best, true);
            }
            else em.AddComponentData(best, new LastDamagedByFaction { Value = fac });
            if (em.HasComponent<LastAttackerEntity>(best))
            {
                em.SetComponentData(best, new LastAttackerEntity { Value = caster });
                em.SetComponentEnabled<LastAttackerEntity>(best, true);
            }

            hp.Value = IsCapitalClass(em, best)
                ? math.max(0, hp.Value - Mathf.CeilToInt(hp.Max * c.razeCapitalShare))
                : 0;
            em.SetComponentData(best, hp);
        }

        /// <summary>Buildings Raze only wounds: the capital, the landmarks, the Great Forge, the Temple / Altar.</summary>
        public static bool IsCapitalClass(EntityManager em, Entity b)
            => em.HasComponent<HallTag>(b) || em.HasComponent<FortressTag>(b)
               || em.HasComponent<VaultTag>(b) || em.HasComponent<FiendstoneKeepTag>(b)
               || em.HasComponent<GreatForgeTag>(b) || em.HasComponent<TempleOfRidanTag>(b);

        private static void ForAllies(EntityManager em, Entity caster, float radius, System.Action<Entity> act)
        {
            if (radius <= 0f || !em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            float3 at = em.GetComponentData<LocalTransform>(caster).Position;
            var fac = em.GetComponentData<FactionTag>(caster).Value;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            float r2 = radius * radius;
            var hits = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == fac && math.distancesq(xfs[i].Position.xz, at.xz) <= r2) hits.Add(ents[i]);
            for (int i = 0; i < hits.Length; i++) act(hits[i]);
            hits.Dispose();
        }
    }
}
