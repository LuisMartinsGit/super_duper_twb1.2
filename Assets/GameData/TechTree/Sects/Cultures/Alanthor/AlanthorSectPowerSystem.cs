// AlanthorSectPowerSystem.cs
// The timed state the 2026-10-15 Alanthor powers leave behind
// (docs/Design/Sects.md §4):
//
//   Ward Discharge  the ward waits; each hostile approach detonates one charge
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
        /// <summary>How long a death stays revivable (The Great Rewind's window).</summary>
        public const double DeathLogSeconds = 60.0;

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

        // PERF (2026-10-16), all result-identical:
        //  * Emptiness guards on the three timed markers. BountyMarked,
        //    OvertimeBoost and WardDischargeCharge are enableable and
        //    pre-added DISABLED, so each RefRW foreach walked (and stamped
        //    changed) every chunk carrying them on every tick although almost
        //    never one is live. IsEmpty honours enable bits.
        //  * The death scan is CHANGE-DRIVEN. A unit can only newly be at
        //    <= 0 HP in a chunk whose Health was written, whose DeathAnimation
        //    bit moved, or that gained entities since the last pass; every
        //    other chunk holds exactly the rows it held then, all of which
        //    were seen alive. Whenever the last pass DID find a dead unit (it
        //    may still be at 0 now), or a ward pulse wrote Health inside this
        //    system (stamped with our own version, invisible to the next
        //    pass's filter), or a new match began, the next pass scans every
        //    chunk exactly as before. The rows are read in place (no five
        //    column copies) and visited in the same chunk order.
        private EntityQuery _anyBounty, _anyOvertime, _anyWard;
        private bool _fullDeathScan = true;
        private int _deathEpoch = int.MinValue;

        protected override void OnCreate()
        {
            var log = EntityManager.CreateEntity(typeof(DeathLogTag));
            EntityManager.AddBuffer<RecentDeath>(log);
#if UNITY_EDITOR
            EntityManager.SetName(log, "DeathLog");
#endif
            _anyBounty = GetEntityQuery(ComponentType.ReadOnly<BountyMarked>());
            _anyOvertime = GetEntityQuery(ComponentType.ReadOnly<OvertimeBoost>());
            _anyWard = GetEntityQuery(ComponentType.ReadOnly<WardDischargeCharge>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            Now = SystemAPI.Time.ElapsedTime;

            if (_deathEpoch != SimCadence.Epoch)
            {
                _deathEpoch = SimCadence.Epoch;
                _fullDeathScan = true;
            }

            LogDeathsAndPayBounties(em);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (!_anyBounty.IsEmpty) TickBounties(em, ecb, dt);
            if (!_anyOvertime.IsEmpty) TickOvertime(em, ecb, dt);
            if (!_anyWard.IsEmpty && TickWards(em, ecb, dt)) _fullDeathScan = true;
            ecb.Playback(em);
            ecb.Dispose();
        }

        // ── Deaths: the Revive log and the Bounty payout ────────────────────

        private void LogDeathsAndPayBounties(EntityManager em)
        {
            var q = QC_Dying.Get(em, QT_Dying);
            if (q.IsEmpty) { _fullDeathScan = false; return; }

            bool full = _fullDeathScan;
            uint since = LastSystemVersion;
            var entH = GetEntityTypeHandle();
            var hpH = GetComponentTypeHandle<Health>(true);
            var idH = GetComponentTypeHandle<UnitTypeId>(true);
            var facH = GetComponentTypeHandle<FactionTag>(true);
            var trH = GetComponentTypeHandle<LocalTransform>(true);
            var deathH = GetComponentTypeHandle<DeathAnimationState>(true);

            // The dead rows, in the order ToEntityArray used to give them.
            var deadE = new NativeList<Entity>(4, Allocator.Temp);
            var deadId = new NativeList<UnitTypeId>(4, Allocator.Temp);
            var deadFac = new NativeList<FactionTag>(4, Allocator.Temp);
            var deadTr = new NativeList<LocalTransform>(4, Allocator.Temp);

            q.CompleteDependency();
            using (var chunks = q.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    bool hasDeath = chunk.Has(ref deathH);
                    if (!full
                        && !chunk.DidChange(ref hpH, since)
                        && !chunk.DidOrderChange(since)
                        && !(hasDeath && chunk.DidChange(ref deathH, since)))
                        continue;

                    var hps = chunk.GetNativeArray(ref hpH);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (hps[i].Value > 0) continue;
                        // Exclude<DeathAnimationState>: an enabled bit drops the row.
                        if (hasDeath && chunk.IsComponentEnabled(ref deathH, i)) continue;
                        deadE.Add(chunk.GetNativeArray(entH)[i]);
                        deadId.Add(chunk.GetNativeArray(ref idH)[i]);
                        deadFac.Add(chunk.GetNativeArray(ref facH)[i]);
                        deadTr.Add(chunk.GetNativeArray(ref trH)[i]);
                    }
                }
            }

            _fullDeathScan = deadE.Length > 0;
            if (deadE.Length == 0)
            {
                deadE.Dispose(); deadId.Dispose(); deadFac.Dispose(); deadTr.Dispose();
                TrimDeathLog(em);
                return;
            }

            Entity logEntity = Entity.Null;
            foreach (var (_, e) in SystemAPI.Query<RefRO<DeathLogTag>>().WithEntityAccess())
            { logEntity = e; break; }

            for (int i = 0; i < deadE.Length; i++)
            {
                var e = deadE[i];

                // Bounty: paid once, then cleared so the corpse cannot pay
                // twice — half the unit's cost, to the caster (decision 59).
                if (TransientState.Active<BountyMarked>(em, e))
                {
                    var mark = em.GetComponentData<BountyMarked>(e);
                    var pay = ProsperityBounty.Payout(TechCatalog.Unit(deadId[i].Value.ToString()));
                    if (!pay.IsZero) FactionEconomy.Add(em, mark.ByFaction, pay, IncomeSource.Loot);
                    TransientState.Clear<BountyMarked>(em, e);
                }

                // The Revive log is NOT written here any more: DeathSystem
                // calls NoteUnitDeath for every unit it actually kills.
            }

            deadE.Dispose(); deadId.Dispose(); deadFac.Dispose(); deadTr.Dispose();
            if (logEntity != Entity.Null) TrimLog(em, logEntity);
        }

        /// <summary>The expiry half of the death log, for a tick with no
        /// deaths (it ran on every tick the query was non-empty).</summary>
        private void TrimDeathLog(EntityManager em)
        {
            foreach (var (_, e) in SystemAPI.Query<RefRO<DeathLogTag>>().WithEntityAccess())
            { TrimLog(em, e); return; }
        }

        static readonly ComponentType[] QT_ReviveLog =
        {
            ComponentType.ReadOnly<DeathLogTag>(),
            ComponentType.ReadWrite<RecentDeath>(),
        };
        static CachedEntityQuery QC_ReviveLog;

        /// <summary>
        /// Record one unit death for Revive (and The Great Rewind). Called by
        /// DeathSystem for every unit it kills, at the one point that is after
        /// EVERY damage source in the tick. This system's own scan used to
        /// write the log, but it only carries [UpdateBefore(DeathSystem)]: a
        /// unit killed by anything sorted between the two (a sect strike, a
        /// burn, a bleed, cursed ground) was a corpse before the next scan and
        /// was never logged, and a unit held at 1 HP by an interceptor was
        /// logged although it lived. Units only, never heroes.
        /// <paramref name="time"/> is the simulation clock of the tick.
        /// </summary>
        public static void NoteUnitDeath(EntityManager em, Entity dead, double time)
        {
            if (!em.HasComponent<UnitTag>(dead) || !em.HasComponent<UnitTypeId>(dead)
                || !em.HasComponent<FactionTag>(dead) || !em.HasComponent<LocalTransform>(dead)) return;
            if (em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(dead)) return;
            if (em.HasComponent<SectHeroTag>(dead)) return;
            var q = QC_ReviveLog.Get(em, QT_ReviveLog);
            if (q.IsEmptyIgnoreFilter) return;
            var log = em.GetBuffer<RecentDeath>(q.GetSingletonEntity());
            log.Add(new RecentDeath
            {
                UnitId = em.GetComponentData<UnitTypeId>(dead).Value,
                Position = em.GetComponentData<LocalTransform>(dead).Position,
                Time = time,
                Faction = em.GetComponentData<FactionTag>(dead).Value,
            });
        }

        private static void TrimLog(EntityManager em, Entity logEntity)
        {
            var log = em.GetBuffer<RecentDeath>(logEntity);
            int drop = 0;
            while (drop < log.Length && Now - log[drop].Time > DeathLogSeconds) drop++;
            if (drop > 0) log.RemoveRange(0, drop);
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

        /// <summary>True when a pulse fired (it wrote Health inside this
        /// system - see the PERF note on the death scan).</summary>
        private bool TickWards(EntityManager em, EntityCommandBuffer ecb, float dt)
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
                if (ward.ValueRO.Timer > 0f) ward.ValueRW.Timer -= dt;
                // Out of time (unused charges vanish) or out of charges.
                if (ward.ValueRO.Lifetime <= 0f || ward.ValueRO.ChargesLeft <= 0)
                {
                    ecb.SetComponentEnabled<WardDischargeCharge>(e, false);
                    continue;
                }
                // The gap after a detonation runs whether or not anyone is near.
                if (ward.ValueRO.Timer > 0f) continue;
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

                if (!enemyNear) continue;
                // One charge, one detonation.
                ward.ValueRW.ChargesLeft--;
                ward.ValueRW.Timer = SectLeverEffects.WardDetonationGap;
                fire.Add(e);
            }

            // Detonations after the query: SpellDamage writes other entities.
            bool pulsed = false;
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
                pulsed = true;
            }
            fire.Dispose();
            if (loaded) { units.Dispose(); utr.Dispose(); ufa.Dispose(); uhp.Dispose(); }
            return pulsed;
        }
    }
}
