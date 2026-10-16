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

            var trade = RunaiTradeSystem.Cfg;
            using var ws = QC_Waystations.Get(em, QT_Waystations).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var wsf = QC_Waystations.Get(em, QT_Waystations).ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var cv = QC_Caravans.Get(em, QT_Caravans).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var cvf = QC_Caravans.Get(em, QT_Caravans).ToComponentDataArray<FactionTag>(Allocator.Temp);

            using var ents = uq.ToEntityArray(Allocator.Temp);
            using var ufacs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var addFires = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var f = ufacs[i].Value;
                int fi = (int)f;
                if (fi < 0 || fi >= Factions || _step[fi] <= 0f) continue;   // not this faction's tick
                float step = _step[fi];
                float life = step + 0.25f;
                ref readonly var tech = ref _techs[fi];
                var e = ents[i];
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                var clock = em.GetComponentData<RunaiUnitClock>(e);

                clock.Still = math.distancesq(p.xz, clock.LastPos.xz) < 0.04f ? clock.Still + step : 0f;
                clock.Calm = hp.Value < clock.LastHp ? 0f : clock.Calm + step;
                clock.LastPos = p;

                bool cavalry = em.HasComponent<CavalryTag>(e);
                float speed = 1f, taken = 1f, armor = 0f;

                // Road March — caravans get the Waystation bonus in RunaiTradeSystem.
                if (tech.RoadMarch && !em.HasComponent<CaravanTag>(e)
                    && Near(ws, wsf, f, p, trade.waystationRadius))
                    speed *= 1f + trade.waystationSpeedPct / 100f;

                if (cavalry && tech.Remounts
                    && clock.Calm >= Cfg.remountCalmSeconds && hp.Value < hp.Max)
                {
                    hp.Value = math.min(hp.Max, hp.Value + math.max(1, (int)math.round(Cfg.remountHealPerSecond * step)));
                    em.SetComponentData(e, hp);
                }

                if (cavalry && tech.FeignedRetreat
                    && hp.Value < hp.Max * Cfg.feignedRetreatHpFraction)
                    speed *= Cfg.feignedRetreatSpeedMultiplier;

                if (tech.ParthianShot && em.HasComponent<HorseArcherTag>(e) && !em.HasComponent<FiresOnMove>(e))
                    addFires.Add(e);

                if (tech.EscortDuty && em.HasComponent<UnitTypeId>(e)
                    && em.GetComponentData<UnitTypeId>(e).Value == RoadWardenId
                    && Near(cv, cvf, f, p, Cfg.escortDutyRadius))
                    taken = Cfg.escortDutyDamageTaken;

                if (tech.WagonLaager && em.HasComponent<WarWagonTag>(e)
                    && clock.Still >= Cfg.laagerStillSeconds)
                    armor = Cfg.laagerArmorBonus;

                clock.LastHp = hp.Value;
                em.SetComponentData(e, clock);

                if (speed == 1f && taken == 1f && armor == 0f) continue;
                var b = TransientState.Active<SpellBuff>(em, e) ? em.GetComponentData<SpellBuff>(e) : default;
                if (speed != 1f) b.SpeedMultiplier = math.max(b.SpeedMultiplier, speed);
                if (taken != 1f) b.DamageTakenMultiplier = b.DamageTakenMultiplier > 0f
                    ? math.min(b.DamageTakenMultiplier, taken) : taken;
                if (armor != 0f) b.ArmorBonus = math.max(b.ArmorBonus, armor);
                b.TimeRemaining = math.max(b.TimeRemaining, life);
                TransientState.Set(em, e, b);
            }
            for (int i = 0; i < addFires.Length; i++) em.AddComponent<FiresOnMove>(addFires[i]);
            addFires.Dispose();
        }

        private static bool Near(NativeArray<LocalTransform> at, NativeArray<FactionTag> facs, Faction f,
                                 float3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < at.Length; i++)
                if (facs[i].Value == f && math.distancesq(at[i].Position.xz, p.xz) <= r2) return true;
            return false;
        }
    }
}
