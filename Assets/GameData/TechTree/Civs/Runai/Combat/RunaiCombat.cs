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
        private static RunaiCombatConfig _cfg;
        public static RunaiCombatConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RunaiCombatConfig>());

        private SimCadence.Periodic _tick;

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
            float step = _tick.DueStep(SystemAPI.Time.DeltaTime, math.max(0.1f, Cfg.tickSeconds));
            if (step <= 0f) return;
            var em = EntityManager;
            var uq = QC_Units.Get(em, QT_Units);
            if (uq.IsEmptyIgnoreFilter) return;

            var trade = RunaiTradeSystem.Cfg;
            using var ws = QC_Waystations.Get(em, QT_Waystations).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var wsf = QC_Waystations.Get(em, QT_Waystations).ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var cv = QC_Caravans.Get(em, QT_Caravans).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var cvf = QC_Caravans.Get(em, QT_Caravans).ToComponentDataArray<FactionTag>(Allocator.Temp);

            using var ents = uq.ToEntityArray(Allocator.Temp);
            var addFires = new NativeList<Entity>(Allocator.Temp);
            float life = step + 0.25f;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                var f = em.GetComponentData<FactionTag>(e).Value;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                var clock = em.GetComponentData<RunaiUnitClock>(e);

                clock.Still = math.distancesq(p.xz, clock.LastPos.xz) < 0.04f ? clock.Still + step : 0f;
                clock.Calm = hp.Value < clock.LastHp ? 0f : clock.Calm + step;
                clock.LastPos = p;

                bool cavalry = em.HasComponent<CavalryTag>(e);
                float speed = 1f, taken = 1f, armor = 0f;

                // Road March — caravans get the Waystation bonus in RunaiTradeSystem.
                if (!em.HasComponent<CaravanTag>(e) && RunaiTradeSystem.Researched(f, "Runai_RoadMarch")
                    && Near(ws, wsf, f, p, trade.waystationRadius))
                    speed *= 1f + trade.waystationSpeedPct / 100f;

                if (cavalry && RunaiTradeSystem.Researched(f, "Runai_Remounts")
                    && clock.Calm >= Cfg.remountCalmSeconds && hp.Value < hp.Max)
                {
                    hp.Value = math.min(hp.Max, hp.Value + math.max(1, (int)math.round(Cfg.remountHealPerSecond * step)));
                    em.SetComponentData(e, hp);
                }

                if (cavalry && RunaiTradeSystem.Researched(f, "Runai_FeignedRetreat")
                    && hp.Value < hp.Max * Cfg.feignedRetreatHpFraction)
                    speed *= Cfg.feignedRetreatSpeedMultiplier;

                if (em.HasComponent<HorseArcherTag>(e) && !em.HasComponent<FiresOnMove>(e)
                    && RunaiTradeSystem.Researched(f, "Runai_ParthianShot"))
                    addFires.Add(e);

                if (em.HasComponent<UnitTypeId>(e)
                    && em.GetComponentData<UnitTypeId>(e).Value.ToString() == "Runai_RoadWarden"
                    && RunaiTradeSystem.Researched(f, "Runai_EscortDuty")
                    && Near(cv, cvf, f, p, Cfg.escortDutyRadius))
                    taken = Cfg.escortDutyDamageTaken;

                if (em.HasComponent<WarWagonTag>(e) && RunaiTradeSystem.Researched(f, "Runai_WagonLaager")
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
