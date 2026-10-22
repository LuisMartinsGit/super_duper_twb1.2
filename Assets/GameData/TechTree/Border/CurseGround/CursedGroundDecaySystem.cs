// CursedGroundDecaySystem.cs
// BUILDINGS ON CURSED GROUND DECAY (Territory_Claims.md §6.9.9, 2026-10-22).
// A finished player building standing on cursed ground (CurseGroundSystem)
//   * loses dotPercentPerSecond of its max HP every second (unattributed —
//     nobody is credited with the kill when it falls; DeathSystem removes it),
//   * takes damageTakenMultiplier times every hit (AbilityDamageHooks),
//   * works at workSpeedMultiplier: training, research and extraction slow
//     down (TrainingSystem, ProductionQueueSystem, TerritoryIncomeSystem).
// Exempt: the Cleansing Ward (it stands on the curse by design), the Runai
// Sanctuary (it stands on cursed outcrops with Consecrated Ground), sites and
// plans (not finished), and the curse's own structures.
// Every number is CursedGroundDecaySystem.asset's.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>The one test the hooks share: is this building decaying?</summary>
    public static class CursedGroundDecay
    {
        public static bool OnCursedGround(EntityManager em, Entity e)
        {
            if (e == Entity.Null || !em.Exists(e)) return false;
            if (!em.HasComponent<BuildingTag>(e) || !em.HasComponent<LocalTransform>(e)) return false;
            if (em.HasComponent<CleanseWardTag>(e) || em.HasComponent<RunaiSanctuaryTag>(e)) return false;
            if (em.HasComponent<BorderTag>(e)) return false;
            if (em.HasComponent<UnderConstruction>(e) || em.HasComponent<PlannedBuilding>(e)) return false;
            if (em.HasComponent<FactionTag>(e) && em.GetComponentData<FactionTag>(e).Value == Faction.Border) return false;
            var p = em.GetComponentData<LocalTransform>(e).Position;
            return CurseGroundSystem.IsCursed(p.x, p.z);
        }

        /// <summary>Multiplier on the time a decaying building takes to train,
        /// research or extract: 1 / workSpeedMultiplier (1 when clean).</summary>
        public static float WorkSpeed(EntityManager em, Entity building)
        {
            var cfg = CursedGroundDecaySystemConfig.I;
            if (cfg == null || !OnCursedGround(em, building)) return 1f;
            return math.max(0.05f, cfg.workSpeedMultiplier);
        }

        /// <summary>Multiplier on every hit a decaying building takes (1 when clean).</summary>
        public static float IncomingMultiplier(EntityManager em, Entity building)
        {
            var cfg = CursedGroundDecaySystemConfig.I;
            if (cfg == null || !OnCursedGround(em, building)) return 1f;
            return math.max(0.05f, cfg.damageTakenMultiplier);
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CurseGroundSystem))]
    public partial class CursedGroundDecaySystem : SystemBase
    {
        private SimCadence.Periodic _cadence;
        private EntityQuery _buildings;

        protected override void OnCreate()
        {
            _buildings = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, FactionTag, LocalTransform>()
                .WithAllRW<Health>()
                .WithNone<UnderConstruction, PlannedBuilding, CleanseWardTag>()
                .WithNone<RunaiSanctuaryTag, BorderTag>()
                .Build(this);
            RequireForUpdate(_buildings);
        }

        protected override void OnUpdate()
        {
            if (!CurseGroundSystem.Ready) return;
            var cfg = CursedGroundDecaySystemConfig.I;
            if (cfg == null || cfg.dotPercentPerSecond <= 0f) return;
            float interval = math.max(0.25f, cfg.tickSeconds);
            float tick = _cadence.DueStep(World.Time.DeltaTime, interval);
            if (tick <= 0f) return;

            var em = EntityManager;
            using var ents = _buildings.ToEntityArray(Allocator.Temp);
            using var facs = _buildings.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _buildings.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue;
                var p = xfs[i].Position;
                if (!CurseGroundSystem.IsCursed(p.x, p.z)) continue;
                var hp = em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0) continue;
                int loss = (int)math.max(1f, hp.Max * cfg.dotPercentPerSecond / 100f * tick);
                hp.Value = math.max(0, hp.Value - loss);
                em.SetComponentData(ents[i], hp);
            }
        }
    }
}
