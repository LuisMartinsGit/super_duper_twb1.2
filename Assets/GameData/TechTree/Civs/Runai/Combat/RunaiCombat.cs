// RunaiCombat.cs
// The Runai trainer specials (docs/Design/Age_1_Runai.md §7.3-7.5) as one
// pass over the Runai units, every tickSeconds of sim time:
//
//   Road March      every Runai unit near one of its Waystations moves faster
//   Escort Duty     Road Wardens near one of their caravans take less damage
//   Remounts        cavalry heal once they have gone a while without losing HP
//   Parthian Shot   Horse Archers gain FiresOnMove
//   Feigned Retreat wounded cavalry ride faster
//   Wagon Laager    a War Wagon standing still gains armour
//
// The timed effects ride SpellBuff (TransientState) with a lifetime a little
// longer than the pass, merged with whatever an ability already put there.
// Stat ladders (weapons, Stride, veterancy, plating, Desert Cloaks) are plain
// effectsList techs and need nothing here.
//
// STAGGERED BY FACTION (2026-10-15 perf). The pass walked every Runai unit of
// every faction on one tick in fifteen — ~7 ms on that tick late game. Each
// faction now keeps its OWN tickSeconds clock, phased k/8 of a period apart
// at match start, so the factions' passes land on different ticks and each
// unit is still visited every tickSeconds with that clock's elapsed step.
// Deterministic (sim time, epoch-reset per match); a faction's pass moves by
// a fraction of a period, so buffs land on a different tick than before. The
// per-unit tech lookups are read once per faction per pass, and the Road
// Warden test compares the FixedString instead of allocating a string per
// unit per pass.
//
// PERF (2026-10-16). The eight clocks are phased 1/16 s apart, so a pass ran
// on about every other tick, and each one made ~10 EntityManager calls per
// unit of the due faction plus a scan of EVERY faction's Waystations and
// caravans per unit. Now: a pass whose due factions own no Runai unit stops
// after one bulk copy; the per-unit reads and writes go through
// ComponentLookups; the Waystation / caravan proximity tests scan only the
// unit's own faction's sites (Near only ever matched its own faction, and
// "any within radius" does not depend on order); and the rare SpellBuff add
// on an entity that never carried one is queued with the FiresOnMove adds.
// Result-identical.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Runai
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RunaiCombat : SystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            // Runai ships only in edition C (docs/Design/Alpha_Build.md §4). Off
            // elsewhere: its configs are stripped from A/B builds, and running
            // it there threw every tick, in the menu and in every match.
            if (!GameEdition.Allows(Cultures.Runai)) { Enabled = false; return; }
            _lkHealth = GetComponentLookup<Health>();
            _lkClock = GetComponentLookup<RunaiUnitClock>();
            _lkXf = GetComponentLookup<LocalTransform>(true);
            _lkBuff = GetComponentLookup<SpellBuff>();
            _lkCavalry = GetComponentLookup<CavalryTag>(true);
            _lkCaravan = GetComponentLookup<CaravanTag>(true);
            _lkHorseArcher = GetComponentLookup<HorseArcherTag>(true);
            _lkFires = GetComponentLookup<FiresOnMove>(true);
            _lkTypeId = GetComponentLookup<UnitTypeId>(true);
            _lkWagon = GetComponentLookup<WarWagonTag>(true);
        }

        private static RunaiCombatConfig _cfg;
        public static RunaiCombatConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RunaiCombatConfig>());

        // Per-faction clocks (player factions 0..7).
        private const int Factions = 8;
        private readonly float[] _acc = new float[Factions];
        private readonly float[] _step = new float[Factions];
        private int _accEpoch = -1;

        private struct Techs
        {
            public bool RoadMarch, Remounts, FeignedRetreat, ParthianShot, EscortDuty, WagonLaager;
        }
        private readonly Techs[] _techs = new Techs[Factions];

        private static readonly FixedString64Bytes RoadWardenId = "Runai_RoadWarden";

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<RunaiUnitTag>(), ComponentType.ReadWrite<RunaiUnitClock>(),
            ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Waystations =
        {
            ComponentType.ReadOnly<HutTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<UnderConstruction>(),
            ComponentType.Exclude<GathererHutTag>(),
        };
        static CachedEntityQuery QC_Waystations;

        static readonly ComponentType[] QT_Caravans =
        {
            ComponentType.ReadOnly<RunaiCaravan>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Caravans;

        protected override void OnUpdate()
        {
            float interval = math.max(0.1f, Cfg.tickSeconds);
            float dt = SystemAPI.Time.DeltaTime;
            if (_accEpoch != SimCadence.Epoch)
            {
                // New match: phase faction k k/8 of a period in, so the eight
                // clocks fire on different ticks from the first second on.
                _accEpoch = SimCadence.Epoch;
                for (int f = 0; f < Factions; f++) _acc[f] = interval * f / Factions;
            }
            bool any = false;
            for (int f = 0; f < Factions; f++)
            {
                _acc[f] += dt;
                _step[f] = 0f;
                if (_acc[f] < interval) continue;
                _step[f] = _acc[f];      // reset-style, as DueStep
                _acc[f] = 0f;
                any = true;
            }
            if (!any) return;
            var em = EntityManager;
            var uq = QC_Units.Get(em, QT_Units);
            if (uq.IsEmptyIgnoreFilter) return;

            for (int f = 0; f < Factions; f++)
            {
                if (_step[f] <= 0f) continue;
                var fac = (Faction)f;
                _techs[f] = new Techs
                {
                    RoadMarch = RunaiTradeSystem.Researched(fac, "Runai_RoadMarch"),
                    Remounts = RunaiTradeSystem.Researched(fac, "Runai_Remounts"),
                    FeignedRetreat = RunaiTradeSystem.Researched(fac, "Runai_FeignedRetreat"),
                    ParthianShot = RunaiTradeSystem.Researched(fac, "Runai_ParthianShot"),
                    EscortDuty = RunaiTradeSystem.Researched(fac, "Runai_EscortDuty"),
                    WagonLaager = RunaiTradeSystem.Researched(fac, "Runai_WagonLaager"),
                };
            }

            using var ents = uq.ToEntityArray(Allocator.Temp);
            using var ufacs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            uint present = 0;
            for (int i = 0; i < ufacs.Length; i++)
            {
                int fi = (int)ufacs[i].Value;
                if (fi >= 0 && fi < Factions && _step[fi] > 0f) present |= 1u << fi;
            }
            if (present == 0) return;   // no due faction has a Runai unit

            var trade = RunaiTradeSystem.Cfg;
            CollectSites(em, present);

            CompleteDependency();
            _lkHealth.Update(this); _lkClock.Update(this); _lkXf.Update(this); _lkBuff.Update(this);
            _lkCavalry.Update(this); _lkCaravan.Update(this); _lkHorseArcher.Update(this);
            _lkFires.Update(this); _lkTypeId.Update(this); _lkWagon.Update(this);

            var addFires = new NativeList<Entity>(Allocator.Temp);
            _deferBuff.Clear();
            for (int i = 0; i < ents.Length; i++)
            {
                var f = ufacs[i].Value;
                int fi = (int)f;
                if (fi < 0 || fi >= Factions || _step[fi] <= 0f) continue;   // not this faction's tick
                float step = _step[fi];
                float life = step + 0.25f;
                ref readonly var tech = ref _techs[fi];
                var e = ents[i];
                var hp = _lkHealth[e];
                if (hp.Value <= 0) continue;
                var p = _lkXf[e].Position;
                var clock = _lkClock[e];

                clock.Still = math.distancesq(p.xz, clock.LastPos.xz) < 0.04f ? clock.Still + step : 0f;
                clock.Calm = hp.Value < clock.LastHp ? 0f : clock.Calm + step;
                clock.LastPos = p;

                bool cavalry = _lkCavalry.HasComponent(e);
                float speed = 1f, taken = 1f, armor = 0f;

                // Road March — caravans get the Waystation bonus in RunaiTradeSystem.
                if (tech.RoadMarch && !_lkCaravan.HasComponent(e)
                    && Near(_ws[fi], p, trade.waystationRadius))
                    speed *= 1f + trade.waystationSpeedPct / 100f;

                if (cavalry && tech.Remounts
                    && clock.Calm >= Cfg.remountCalmSeconds && hp.Value < hp.Max)
                {
                    hp.Value = math.min(hp.Max, hp.Value + math.max(1, (int)math.round(Cfg.remountHealPerSecond * step)));
                    _lkHealth[e] = hp;
                }

                if (cavalry && tech.FeignedRetreat
                    && hp.Value < hp.Max * Cfg.feignedRetreatHpFraction)
                    speed *= Cfg.feignedRetreatSpeedMultiplier;

                if (tech.ParthianShot && _lkHorseArcher.HasComponent(e) && !_lkFires.HasComponent(e))
                    addFires.Add(e);

                if (tech.EscortDuty && _lkTypeId.TryGetComponent(e, out var tid)
                    && tid.Value == RoadWardenId
                    && Near(_cv[fi], p, Cfg.escortDutyRadius))
                    taken = Cfg.escortDutyDamageTaken;

                if (tech.WagonLaager && _lkWagon.HasComponent(e)
                    && clock.Still >= Cfg.laagerStillSeconds)
                    armor = Cfg.laagerArmorBonus;

                clock.LastHp = hp.Value;
                _lkClock[e] = clock;

                if (speed == 1f && taken == 1f && armor == 0f) continue;
                var b = TransientState.Active(ref _lkBuff, e) ? _lkBuff[e] : default;
                if (speed != 1f) b.SpeedMultiplier = math.max(b.SpeedMultiplier, speed);
                if (taken != 1f) b.DamageTakenMultiplier = b.DamageTakenMultiplier > 0f
                    ? math.min(b.DamageTakenMultiplier, taken) : taken;
                if (armor != 0f) b.ArmorBonus = math.max(b.ArmorBonus, armor);
                b.TimeRemaining = math.max(b.TimeRemaining, life);
                if (_lkBuff.HasComponent(e)) { _lkBuff[e] = b; _lkBuff.SetComponentEnabled(e, true); }
                else _deferBuff.Add((e, b));
            }
            for (int i = 0; i < _deferBuff.Count; i++) TransientState.Set(em, _deferBuff[i].e, _deferBuff[i].b);
            _deferBuff.Clear();
            for (int i = 0; i < addFires.Length; i++) em.AddComponent<FiresOnMove>(addFires[i]);
            addFires.Dispose();
        }

        // ── per-pass state (2026-10-16 perf) ───────────────────────────
        private ComponentLookup<Health> _lkHealth;
        private ComponentLookup<RunaiUnitClock> _lkClock;
        private ComponentLookup<LocalTransform> _lkXf;
        private ComponentLookup<SpellBuff> _lkBuff;
        private ComponentLookup<CavalryTag> _lkCavalry;
        private ComponentLookup<CaravanTag> _lkCaravan;
        private ComponentLookup<HorseArcherTag> _lkHorseArcher;
        private ComponentLookup<FiresOnMove> _lkFires;
        private ComponentLookup<UnitTypeId> _lkTypeId;
        private ComponentLookup<WarWagonTag> _lkWagon;
        private readonly System.Collections.Generic.List<(Entity e, SpellBuff b)> _deferBuff = new();

        /// <summary>Waystation / caravan positions (XZ) per faction, filled
        /// for the due factions of this pass only.</summary>
        private readonly System.Collections.Generic.List<float2>[] _ws = NewLists();
        private readonly System.Collections.Generic.List<float2>[] _cv = NewLists();

        private static System.Collections.Generic.List<float2>[] NewLists()
        {
            var a = new System.Collections.Generic.List<float2>[Factions];
            for (int i = 0; i < Factions; i++) a[i] = new System.Collections.Generic.List<float2>();
            return a;
        }

        private void CollectSites(EntityManager em, uint due)
        {
            for (int i = 0; i < Factions; i++) { _ws[i].Clear(); _cv[i].Clear(); }
            Fill(QC_Waystations.Get(em, QT_Waystations), _ws, due);
            Fill(QC_Caravans.Get(em, QT_Caravans), _cv, due);
        }

        private static void Fill(EntityQuery q, System.Collections.Generic.List<float2>[] into, uint due)
        {
            if (q.IsEmptyIgnoreFilter) return;
            using var at = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < at.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= Factions || (due & (1u << fi)) == 0) continue;
                into[fi].Add(at[i].Position.xz);
            }
        }

        private static bool Near(System.Collections.Generic.List<float2> at, float3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < at.Count; i++)
                if (math.distancesq(at[i], p.xz) <= r2) return true;
            return false;
        }
    }
}
