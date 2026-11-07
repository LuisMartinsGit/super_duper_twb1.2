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
        /// Exodus, the Wayfinder's level-10 game ender (Heroes.md §3): after its
        /// cast the whole army, wherever it stands, crosses the map to the aimed
        /// point. Workers and caravans stay at their work.
        /// </summary>
        public static void Exodus(EntityManager em, Entity caster, AbilityCard card)
        {
            if (!em.HasComponent<AbilityAimPoint>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            var to = em.GetComponentData<AbilityAimPoint>(caster).Position;
            int k = 0;
            ForAllies(em, caster, WholeMap, u =>
            {
                if (em.HasComponent<BuildingTag>(u) || em.HasComponent<CaravanTag>(u)) return;
                var cls = em.GetComponentData<UnitTag>(u).Class;
                if (cls == UnitClass.Worker || cls == UnitClass.Economy) return;
                float a = k++ * 2.39996f;
                float rr = card.Radius * 0.5f + (k / 12) * 2.5f;
                var want = new float3(to.x + math.cos(a) * rr, 0f, to.z + math.sin(a) * rr);
                // Onto walkable ground, or the unit stays where it is.
                TheWaningBorder.Systems.Navigation.NavGridQuery.SnapToWalkable(want, out float3 p, out bool ok);
                if (!ok) return;
                p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                var xf = em.GetComponentData<LocalTransform>(u);
                xf.Position = p;
                em.SetComponentData(u, xf);
                if (em.HasComponent<DesiredDestination>(u))
                    em.SetComponentData(u, new DesiredDestination { Position = p, Has = 0 });
                if (em.HasComponent<Target>(u)) em.SetComponentData(u, new Target { Value = Entity.Null });
            });
            TheWaningBorder.Core.SimSignals.Announce(string.Format(TheWaningBorder.Core.Localization.Loc.T(
                "{0} calls the EXODUS — its army crosses the map!"), em.GetComponentData<FactionTag>(caster).Value));
        }

        private const float WholeMap = 100000f;

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
