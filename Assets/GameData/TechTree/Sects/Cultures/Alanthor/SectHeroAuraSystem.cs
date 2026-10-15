// SectHeroAuraSystem.cs
// The Alanthor sect heroes' continuous effects and the timed state their
// abilities leave behind (docs/Design/Sects.md §4):
//
//   Catalogued   (Archivist L1)    slowed enemies near it take +20% damage
//   Recall trail (Archivist L6)    where it stood, sampled every 0.5 s
//   Cauterize    (Surgeon L1)      allies near it are immune to damage over time
//   Called In    (Guildmaster L1)  enemies under half HP near it are slowed 20%
//   Lancet       (Surgeon L6)      the bleed, and no healing while it lasts
//   Challenge    (Warden L6)       taunted units keep attacking the Warden
//   Stake        (Guildmaster L3)  the extractor pays double while it holds
//
// Auras refresh short transients every pulse; leaving the radius lets them
// lapse. Sim time and replicated state only.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SectHeroAuraSystem : SystemBase
    {
        public const float AuraRadius = 15f;          // SectRadius.Medium
        public const float CataloguedBonus = 0.20f;
        public const float CalledInSlow = 0.20f;
        private const float PulseSeconds = 0.5f;
        private const float Hold = 0.75f;

        private float _pulse;
        private float _bleedAccum;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            TickLanced(em, ecb, dt);
            TickTaunted(em, ecb, dt);
            TickStakes(em, ecb, dt);
            TickShort<CatalogedMark>(em, ecb, dt);
            TickShortCauterized(em, ecb, dt);
            ecb.Playback(em);
            ecb.Dispose();

            _pulse += dt;
            if (_pulse < PulseSeconds) return;
            _pulse = 0f;

            int antiquity = SectConfig.IndexOf(SectConfig.Antiquity);
            int renewal = SectConfig.IndexOf(SectConfig.Renewal);
            int prosperity = SectConfig.IndexOf(SectConfig.Prosperity);

            // Heroes on the field: copy out first, the auras write other entities.
            var heroes = new NativeList<Entity>(4, Allocator.Temp);
            foreach (var (_, e) in SystemAPI.Query<RefRO<SectHeroTag>>().WithEntityAccess()) heroes.Add(e);

            for (int h = 0; h < heroes.Length; h++)
            {
                var hero = heroes[h];
                if (!em.Exists(hero) || em.GetComponentData<Health>(hero).Value <= 0) continue;
                int sect = em.GetComponentData<SectHeroTag>(hero).SectIndex;
                var pos = em.GetComponentData<LocalTransform>(hero).Position;
                var fac = em.GetComponentData<FactionTag>(hero).Value;

                if (sect == antiquity)
                {
                    if (em.HasComponent<ArchivistTrail>(hero))
                    {
                        var t = em.GetComponentData<ArchivistTrail>(hero);
                        SectHeroAbilities.PushTrail(ref t, pos);
                        em.SetComponentData(hero, t);
                    }
                    using var enemies = SectHeroAbilities.UnitsNear(em, pos, AuraRadius, fac, hostile: true);
                    for (int i = 0; i < enemies.Length; i++)
                        if (AlanthorSectUnitTraits.IsSlowed(em, enemies[i]))
                            TransientState.Set(em, enemies[i], new CatalogedMark
                            {
                                Bonus = CataloguedBonus, TimeRemaining = Hold, ByFaction = fac,
                            });
                }
                else if (sect == renewal)
                {
                    using var allies = SectHeroAbilities.UnitsNear(em, pos, AuraRadius, fac, hostile: false);
                    for (int i = 0; i < allies.Length; i++)
                        TransientState.Set(em, allies[i], new Cauterized { TimeRemaining = Hold });
                }
                else if (sect == prosperity)
                {
                    using var enemies = SectHeroAbilities.UnitsNear(em, pos, AuraRadius, fac, hostile: true);
                    for (int i = 0; i < enemies.Length; i++)
                    {
                        var hp = em.GetComponentData<Health>(enemies[i]);
                        if (hp.Max <= 0 || hp.Value * 2 >= hp.Max) continue;
                        var d = TransientState.Active<SpellDebuff>(em, enemies[i])
                            ? em.GetComponentData<SpellDebuff>(enemies[i]) : default;
                        d.SpeedReduction = math.max(d.SpeedReduction, CalledInSlow);
                        d.TimeRemaining = math.max(d.TimeRemaining, Hold);
                        TransientState.Set(em, enemies[i], d);
                    }
                }
                // The Warden's Drillmaster is read by StoredStrikeChargeSystem.
            }
            heroes.Dispose();
        }

        private void TickShort<T>(EntityManager em, EntityCommandBuffer ecb, float dt) where T : unmanaged, IComponentData, IEnableableComponent
        {
            // CatalogedMark only — kept generic for the next timed mark.
            foreach (var (mark, e) in SystemAPI.Query<RefRW<CatalogedMark>>().WithEntityAccess())
            {
                mark.ValueRW.TimeRemaining -= dt;
                if (mark.ValueRO.TimeRemaining <= 0f) ecb.SetComponentEnabled<CatalogedMark>(e, false);
            }
        }

        private void TickShortCauterized(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (c, e) in SystemAPI.Query<RefRW<Cauterized>>().WithEntityAccess())
            {
                c.ValueRW.TimeRemaining -= dt;
                if (c.ValueRO.TimeRemaining <= 0f) ecb.SetComponentEnabled<Cauterized>(e, false);
            }
        }

        private void TickLanced(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            var hits = new NativeList<Entity>(4, Allocator.Temp);
            var dmg = new NativeList<int>(4, Allocator.Temp);
            var src = new NativeList<Faction>(4, Allocator.Temp);
            foreach (var (lance, hp, e) in SystemAPI.Query<RefRW<Lanced>, RefRW<Health>>().WithEntityAccess())
            {
                // No healing while it lasts: take back anything above the mark.
                if (hp.ValueRO.Value > lance.ValueRO.LastHp)
                    hp.ValueRW.Value = lance.ValueRO.LastHp;
                lance.ValueRW.TimeRemaining -= dt;
                lance.ValueRW.Accum += lance.ValueRO.DamagePerSecond * dt;
                int whole = (int)lance.ValueRO.Accum;
                if (whole > 0)
                {
                    lance.ValueRW.Accum -= whole;
                    hits.Add(e); dmg.Add(whole); src.Add(lance.ValueRO.Source);
                }
                if (lance.ValueRO.TimeRemaining <= 0f) ecb.SetComponentEnabled<Lanced>(e, false);
            }
            for (int i = 0; i < hits.Length; i++)
            {
                if (!em.Exists(hits[i])) continue;
                // A bleed is damage over time: Cauterize stops it like any other.
                int amount = DamageOverTime.ScaleTick(em, hits[i], false, dmg[i]);
                if (amount > 0) SpellDamage.Apply(em, hits[i], amount, DamageType.True, src[i]);
                if (TransientState.Active<Lanced>(em, hits[i]))
                {
                    var l = em.GetComponentData<Lanced>(hits[i]);
                    l.LastHp = em.GetComponentData<Health>(hits[i]).Value;
                    em.SetComponentData(hits[i], l);
                }
            }
            hits.Dispose(); dmg.Dispose(); src.Dispose();
        }

        private void TickTaunted(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (taunt, target, e) in SystemAPI.Query<RefRW<Taunted>, RefRW<Target>>().WithEntityAccess())
            {
                taunt.ValueRW.TimeRemaining -= dt;
                var taunter = taunt.ValueRO.Taunter;
                bool alive = taunter != Entity.Null && em.Exists(taunter)
                             && em.HasComponent<Health>(taunter) && em.GetComponentData<Health>(taunter).Value > 0;
                if (taunt.ValueRO.TimeRemaining <= 0f || !alive)
                {
                    ecb.SetComponentEnabled<Taunted>(e, false);
                    continue;
                }
                target.ValueRW.Value = taunter;
            }
        }

        private void TickStakes(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (stake, hp, tr) in SystemAPI.Query<RefRW<StakeChannel>, RefRO<Health>, RefRO<LocalTransform>>())
            {
                if (stake.ValueRO.TimeRemaining <= 0f) continue;
                stake.ValueRW.TimeRemaining -= dt;
                var node = stake.ValueRO.Node;
                bool broken = hp.ValueRO.Value < stake.ValueRO.HealthAtStart
                              || node == Entity.Null || !em.Exists(node)
                              || math.distancesq(em.GetComponentData<LocalTransform>(node).Position.xz,
                                                 tr.ValueRO.Position.xz)
                                 > SectHeroAbilities.StakeRange * SectHeroAbilities.StakeRange * 4f;
                if (broken) { stake.ValueRW.TimeRemaining = 0f; continue; }
                // Double pay through the same per-building multiplier Overtime uses.
                TransientState.Set(em, node, new OvertimeBoost { Multiplier = 2f, TimeRemaining = Hold });
            }
        }
    }
}
