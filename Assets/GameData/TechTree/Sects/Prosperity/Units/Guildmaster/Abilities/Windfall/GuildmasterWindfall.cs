// GuildmasterWindfall.cs
// The Guildmaster's level-10 game ender, WINDFALL (docs/Design/Sects.md
// § Sect of Prosperity, docs/Design/Heroes.md, decision 47, 2026-11-10): for
// the card's duration, everything that dies within its radius of the
// Guildmaster — hostile or his own side's, units and buildings alike, the
// Guildmaster himself included — pays his player the card's Value % of its
// cost (FactionEconomy, IncomeSource.Loot). Duration, radius and share are on
// Windfall.asset beside this file.
//
// The death hook is the one AlanthorSectPowerSystem uses: after combat and
// before DeathSystem, an entity at 0 HP that has no death animation or
// collapse yet is dying THIS tick, and DeathSystem gives it one at the end of
// the tick, so each death is seen exactly once. Life Cling and the Feraldis
// moment-of-death rules keep their entity alive, so those pay nothing.
// Sim state only, in query order; integer shares, so every lockstep peer pays
// the same.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

/// <summary>Windfall running on a Guildmaster: deaths within Radius of him pay
/// Payee Percent % of their cost until TimeRemaining runs out.</summary>
public struct WindfallActive : IComponentData
{
    public float TimeRemaining;
    public float Radius;
    public int Percent;
    public Faction Payee;
}

namespace TheWaningBorder.Systems.Sect
{
    public static class GuildmasterWindfall
    {
        /// <summary>Start (or restart) Windfall on the caster.</summary>
        public static void Begin(EntityManager em, Entity caster, AbilityCard card, float percent, float duration)
        {
            if (!em.HasComponent<FactionTag>(caster) || duration <= 0f || card.Radius <= 0f) return;
            var me = em.GetComponentData<FactionTag>(caster).Value;
            var w = new WindfallActive
            {
                TimeRemaining = duration,
                Radius = card.Radius,
                Percent = math.max(0, (int)math.round(percent)),
                Payee = me,
            };
            if (em.HasComponent<WindfallActive>(caster)) em.SetComponentData(caster, w);
            else em.AddComponentData(caster, w);
            SimSignals.Announce(string.Format(Loc.T("{0} calls a WINDFALL — every death near the Guildmaster pays!"), me));
            if (em.HasComponent<LocalTransform>(caster))
                SimSignals.Ping(em.GetComponentData<LocalTransform>(caster).Position, SimPingKind.Combat, 20f, big: true);
        }

        /// <summary>What <paramref name="e"/> cost, from its SO: a unit by its
        /// UnitTypeId, a building by its id. Zero when neither resolves.</summary>
        public static Cost CostOf(EntityManager em, Entity e)
        {
            TheWaningBorder.Data.CostBlock c = null;
            if (em.HasComponent<UnitTypeId>(e))
            {
                if (TechCatalog.TryGetUnit(em.GetComponentData<UnitTypeId>(e).Value.ToString(), out var u) && u != null)
                    c = u.cost;
            }
            else if (em.HasComponent<BuildingTag>(e))
            {
                string id = TheWaningBorder.Entities.BuildingIds.Of(e, em);
                if (!string.IsNullOrEmpty(id) && TechCatalog.TryGetBuilding(id, out var b) && b != null)
                    c = b.cost;
            }
            return c == null ? default : Cost.Of(c.Supplies, c.Iron, c.Veilstone);
        }

        /// <summary><paramref name="percent"/> % of a cost, rounded down, in integers.</summary>
        public static Cost Share(Cost c, int percent) => Cost.Of(
            supplies: c.Supplies * percent / 100,
            iron: c.Iron * percent / 100,
            veilstone: c.Veilstone * percent / 100);
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class GuildmasterWindfallSystem : SystemBase
    {
        static readonly ComponentType[] QT_Dying =
        {
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<DeathAnimationState>(),
            ComponentType.Exclude<BuildingCollapseState>(),
            ComponentType.Exclude<NodeDormant>(),
        };
        static CachedEntityQuery QC_Dying;

        private EntityQuery _active;

        protected override void OnCreate()
        {
            _active = GetEntityQuery(ComponentType.ReadOnly<WindfallActive>());
        }

        protected override void OnUpdate()
        {
            if (_active.IsEmpty) return;
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // The running windfalls, in query order.
            using var casters = _active.ToEntityArray(Allocator.Temp);
            var centre = new NativeList<float3>(casters.Length, Allocator.Temp);
            var r2 = new NativeList<float>(casters.Length, Allocator.Temp);
            var pct = new NativeList<int>(casters.Length, Allocator.Temp);
            var payee = new NativeList<Faction>(casters.Length, Allocator.Temp);
            var ended = new NativeList<Entity>(2, Allocator.Temp);
            for (int i = 0; i < casters.Length; i++)
            {
                var c = casters[i];
                var w = em.GetComponentData<WindfallActive>(c);
                // His own death is paid on the tick he falls; after that the
                // windfall is over (it is "near the Guildmaster").
                bool gone = TransientState.Active<DeathAnimationState>(em, c);
                w.TimeRemaining -= dt;
                if (gone || w.TimeRemaining <= 0f) ended.Add(c);
                else em.SetComponentData(c, w);
                if (gone) continue;
                centre.Add(em.GetComponentData<LocalTransform>(c).Position);
                r2.Add(w.Radius * w.Radius);
                pct.Add(w.Percent);
                payee.Add(w.Payee);
            }

            if (centre.Length > 0)
            {
                var q = QC_Dying.Get(em, QT_Dying);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (hps[i].Value > 0) continue;
                    var e = ents[i];
                    if (TransientState.Active<LifeCling>(em, e)) continue;
                    if (FeraldisDeathInterceptor.WantsIntercept(em, e)) continue;
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    Cost cost = default;
                    bool priced = false;
                    for (int w = 0; w < centre.Length; w++)
                    {
                        if (math.distancesq(p.xz, centre[w].xz) > r2[w]) continue;
                        if (!priced) { cost = GuildmasterWindfall.CostOf(em, e); priced = true; }
                        var pay = GuildmasterWindfall.Share(cost, pct[w]);
                        if (!pay.IsZero) FactionEconomy.Add(em, payee[w], pay, IncomeSource.Loot);
                    }
                }
            }

            for (int i = 0; i < ended.Length; i++)
                if (em.Exists(ended[i]) && em.HasComponent<WindfallActive>(ended[i]))
                    em.RemoveComponent<WindfallActive>(ended[i]);

            centre.Dispose(); r2.Dispose(); pct.Dispose(); payee.Dispose(); ended.Dispose();
        }
    }
}
