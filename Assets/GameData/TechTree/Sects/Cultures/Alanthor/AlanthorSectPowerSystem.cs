// AlanthorSectPowerSystem.cs
// The timed state the 2026-10-15 Alanthor powers leave behind
// (docs/Design/Sects.md §4):
//
//   Ward Discharge  the armed charge waits, fires its pulses at enemies in range
//   Overtime        the building boost runs out
//   Bounty          a marked unit that dies inside the window pays its cost
//   Revive          every unit death is logged briefly so it can be undone
//
// Runs after combat and before DeathSystem (the FeraldisRaidIncomeSystem
// slot), so each death is seen exactly once, before the corpse is handed to
// the death animation. Sim time and replicated state only.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class AlanthorSectPowerSystem : SystemBase
    {
        /// <summary>How long a death stays revivable (the longest Revive window).</summary>
        public const double DeathLogSeconds = 20.0;

        /// <summary>Sim time of the current tick, for Revive's window.</summary>
        public static double Now { get; private set; }

        static readonly ComponentType[] QT_Dying =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<DeathAnimationState>(),
        };
        static CachedEntityQuery QC_Dying;

        static readonly ComponentType[] QT_Hostiles =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Hostiles;

        protected override void OnCreate()
        {
            var log = EntityManager.CreateEntity(typeof(DeathLogTag));
            EntityManager.AddBuffer<RecentDeath>(log);
#if UNITY_EDITOR
            EntityManager.SetName(log, "DeathLog");
#endif
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            Now = SystemAPI.Time.ElapsedTime;

            LogDeathsAndPayBounties(em);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            TickBounties(em, ecb, dt);
            TickOvertime(em, ecb, dt);
            TickWards(em, ecb, dt);
            ecb.Playback(em);
            ecb.Dispose();
        }

        // ── Deaths: the Revive log and the Bounty payout ────────────────────

        private void LogDeathsAndPayBounties(EntityManager em)
        {
            var q = QC_Dying.Get(em, QT_Dying);
            if (q.IsEmpty) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var ids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity logEntity = Entity.Null;
            foreach (var (_, e) in SystemAPI.Query<RefRO<DeathLogTag>>().WithEntityAccess())
            { logEntity = e; break; }

            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value > 0) continue;
                var e = ents[i];

                // Bounty: paid once, then cleared so the corpse cannot pay twice.
                if (TransientState.Active<BountyMarked>(em, e))
                {
                    var mark = em.GetComponentData<BountyMarked>(e);
                    var def = TechCatalog.Unit(ids[i].Value.ToString());
                    if (def != null && def.cost != null)
                        FactionEconomy.Add(em, mark.ByFaction, Cost.Of(def.cost.Supplies, def.cost.Iron,
                            def.cost.Veilstone, def.cost.Veilsteel), IncomeSource.Loot);
                    TransientState.Clear<BountyMarked>(em, e);
                }

                // Revive log: units only, never heroes.
                if (logEntity == Entity.Null) continue;
                if (em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(e)) continue;
                if (em.HasComponent<SectHeroTag>(e)) continue;
                var log = em.GetBuffer<RecentDeath>(logEntity);
                // A unit seen dying twice (still at <= 0 next tick) logs once.
                bool seen = false;
                for (int j = log.Length - 1; j >= 0 && j >= log.Length - 64; j--)
                    if (log[j].Time == Now && log[j].UnitId == ids[i].Value
                        && math.all(log[j].Position == trs[i].Position)) { seen = true; break; }
                if (seen) continue;
                log.Add(new RecentDeath
                {
                    UnitId = ids[i].Value,
                    Position = trs[i].Position,
                    Time = Now,
                    Faction = facs[i].Value,
                });
            }

            if (logEntity != Entity.Null)
            {
                var log = em.GetBuffer<RecentDeath>(logEntity);
                int drop = 0;
                while (drop < log.Length && Now - log[drop].Time > DeathLogSeconds) drop++;
                if (drop > 0) log.RemoveRange(0, drop);
            }
        }

        private void TickBounties(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (mark, e) in SystemAPI.Query<RefRW<BountyMarked>>().WithEntityAccess())
            {
                mark.ValueRW.TimeRemaining -= dt;
                if (mark.ValueRO.TimeRemaining <= 0f) ecb.SetComponentEnabled<BountyMarked>(e, false);
            }
        }

        private void TickOvertime(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (boost, e) in SystemAPI.Query<RefRW<OvertimeBoost>>().WithEntityAccess())
            {
                boost.ValueRW.TimeRemaining -= dt;
                if (boost.ValueRO.TimeRemaining <= 0f) ecb.SetComponentEnabled<OvertimeBoost>(e, false);
            }
        }

        // ── Ward Discharge ──────────────────────────────────────────────────

        private void TickWards(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            var hq = QC_Hostiles.Get(em, QT_Hostiles);
            NativeArray<Entity> units = default;
            NativeArray<LocalTransform> utr = default;
            NativeArray<FactionTag> ufa = default;
            NativeArray<Health> uhp = default;
            bool loaded = false;

            var fire = new NativeList<Entity>(4, Allocator.Temp);
            foreach (var (ward, tr, fac, e) in SystemAPI
                         .Query<RefRW<WardDischargeCharge>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithEntityAccess())
            {
                ward.ValueRW.Lifetime -= dt;
                if (ward.ValueRO.Lifetime <= 0f || ward.ValueRO.PulsesLeft <= 0)
                {
                    fire.Add(Entity.Null);   // placeholder keeps indices aligned
                    ecb.SetComponentEnabled<WardDischargeCharge>(e, false);
                    continue;
                }
                if (!loaded)
                {
                    units = hq.ToEntityArray(Allocator.Temp);
                    utr = hq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                    ufa = hq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                    uhp = hq.ToComponentDataArray<Health>(Allocator.Temp);
                    loaded = true;
                }
                bool enemyNear = false;
                float r2 = ward.ValueRO.TriggerRadius * ward.ValueRO.TriggerRadius;
                for (int i = 0; i < units.Length && !enemyNear; i++)
                    enemyNear = uhp[i].Value > 0 && Alliances.AreHostile(fac.ValueRO.Value, ufa[i].Value)
                                && math.distancesq(utr[i].Position.xz, tr.ValueRO.Position.xz) <= r2;

                if (!enemyNear) { ward.ValueRW.Firing = 0; ward.ValueRW.Timer = 0f; continue; }
                if (ward.ValueRO.Firing == 0) { ward.ValueRW.Firing = 1; ward.ValueRW.Timer = 0f; }
                ward.ValueRW.Timer -= dt;
                if (ward.ValueRO.Timer > 0f) continue;
                ward.ValueRW.Timer = ward.ValueRO.Interval;
                ward.ValueRW.PulsesLeft--;
                fire.Add(e);
            }

            // Pulses after the query: SpellDamage writes other entities.
            for (int f = 0; f < fire.Length; f++)
            {
                var e = fire[f];
                if (e == Entity.Null || !em.Exists(e)) continue;
                var ward = em.GetComponentData<WardDischargeCharge>(e);
                var at = em.GetComponentData<LocalTransform>(e).Position;
                var owner = em.GetComponentData<FactionTag>(e).Value;
                float r2 = ward.TriggerRadius * ward.TriggerRadius;
                for (int i = 0; i < units.Length; i++)
                {
                    if (uhp[i].Value <= 0 || !Alliances.AreHostile(owner, ufa[i].Value)) continue;
                    if (math.distancesq(utr[i].Position.xz, at.xz) > r2) continue;
                    SpellDamage.Apply(em, units[i], (int)math.round(ward.Damage), DamageType.Magic, owner);
                }
                TheWaningBorder.Rendering.SectPowerVfx.SpawnTelegraph(at, ward.TriggerRadius, 0.4f);
            }
            fire.Dispose();
            if (loaded) { units.Dispose(); utr.Dispose(); ufa.Dispose(); uhp.Dispose(); }
        }
    }
}
