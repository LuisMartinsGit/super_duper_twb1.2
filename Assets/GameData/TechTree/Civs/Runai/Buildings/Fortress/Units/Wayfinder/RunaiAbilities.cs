// RunaiAbilities.cs
// The Wayfinder's four abilities and the Caravan Master's Scatter
// (docs/Design/Age_1_Runai.md §6.1, §8). Called by AbilityEffectExecutor for
// the Runai* effect kinds; radius, duration and cooldown are on the ability
// SO, the rest on RunaiTradeSystem.asset.

using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Runai
{
    public static class RunaiAbilities
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Markets =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(), ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_Markets;

        /// <summary>Wayfinding: allies near the Wayfinder move faster (value = percent).</summary>
        public static void Wayfinding(EntityManager em, Entity caster, AbilityCard card, float duration)
        {
            float mult = 1f + card.EffectValue(AbilityEffectKind.RunaiWayfinding) / 100f;
            ForAllies(em, caster, card.Radius, u =>
            {
                var b = TransientState.Active<SpellBuff>(em, u) ? em.GetComponentData<SpellBuff>(u) : default;
                b.SpeedMultiplier = math.max(b.SpeedMultiplier, mult);
                b.TimeRemaining = math.max(b.TimeRemaining, duration);
                TransientState.Set(em, u, b);
            });
        }

        /// <summary>Mirage: conjures decoy riders around the Wayfinder (value = count).</summary>
        public static void Mirage(EntityManager em, Entity caster, AbilityCard card, float duration)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            var at = em.GetComponentData<LocalTransform>(caster).Position;
            var f = em.GetComponentData<FactionTag>(caster).Value;
            int n = math.max(1, (int)card.EffectValue(AbilityEffectKind.RunaiMirage));
            float r = math.max(2f, card.Radius);
            for (int i = 0; i < n; i++)
            {
                float a = i * (math.PI * 2f / n);
                var p = new float3(at.x + math.cos(a) * r, at.y, at.z + math.sin(a) * r);
                p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                var e = UnitFactory.Create(em, RunaiMirageRider.Id, p, f);
                em.AddComponentData(e, new TemporarySummon { TimeToLive = math.max(1f, duration) });
            }
        }

        /// <summary>Caravan of Swords: every caravan of the faction is armed and toughened.</summary>
        public static void CaravanOfSwords(EntityManager em, Entity caster, float duration)
        {
            if (!em.HasComponent<FactionTag>(caster)) return;
            RunaiTradeSystem.ArmCaravans(em, em.GetComponentData<FactionTag>(caster).Value, math.max(1f, duration));
        }

        /// <summary>
        /// Exodus: after its cast, every Runai unit near the Wayfinder (him
        /// included) is carried to the faction's market nearest him — a
        /// Fortress or Thessara's Crossing.
        /// </summary>
        public static void Exodus(EntityManager em, Entity caster, AbilityCard card)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            var at = em.GetComponentData<LocalTransform>(caster).Position;
            var f = em.GetComponentData<FactionTag>(caster).Value;

            var mq = QC_Markets.Get(em, QT_Markets);
            using var ments = mq.ToEntityArray(Allocator.Temp);
            using var mfacs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var mxfs = mq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float best = float.MaxValue; float3 to = default; bool found = false; float tr = 4f;
            int bestIndex = int.MaxValue;
            for (int i = 0; i < ments.Length; i++)
            {
                if (mfacs[i].Value != f) continue;
                var e = ments[i];
                if (!em.HasComponent<HallTag>(e) && !em.HasComponent<FortressTag>(e)
                    && !em.HasComponent<ThessarasCrossingTag>(e)) continue;
                float d = math.distancesq(mxfs[i].Position.xz, at.xz);
                // Ties go to the lower entity index, so every peer picks the same market.
                if (d < best || (d == best && e.Index < bestIndex))
                {
                    best = d; to = mxfs[i].Position; found = true; bestIndex = e.Index;
                    tr = (em.HasComponent<Radius>(e) ? em.GetComponentData<Radius>(e).Value : 3f) + 4f;
                }
            }
            if (!found) return;

            int k = 0;
            ForAllies(em, caster, card.Radius, u =>
            {
                if (em.HasComponent<BuildingTag>(u)) return;
                float a = k++ * 2.39996f;
                float rr = tr + (k / 12) * 2.5f;
                var p = new float3(to.x + math.cos(a) * rr, 0f, to.z + math.sin(a) * rr);
                p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                var xf = em.GetComponentData<LocalTransform>(u);
                xf.Position = p;
                em.SetComponentData(u, xf);
                if (em.HasComponent<DesiredDestination>(u))
                    em.SetComponentData(u, new DesiredDestination { Position = p, Has = 0 });
                if (em.HasComponent<Target>(u)) em.SetComponentData(u, new Target { Value = Entity.Null });
            });
        }

        /// <summary>Scatter (the Caravan Master): every caravan in the radius runs to the nearest market.</summary>
        public static void Scatter(EntityManager em, Entity caster, AbilityCard card)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            RunaiTradeSystem.Scatter(em, em.GetComponentData<FactionTag>(caster).Value,
                em.GetComponentData<LocalTransform>(caster).Position, card.Radius);
        }

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
