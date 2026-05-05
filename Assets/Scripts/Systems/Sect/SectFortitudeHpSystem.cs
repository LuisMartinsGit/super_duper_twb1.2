// SectFortitudeHpSystem.cs
// Implements Fortitude's Lv I "Veiled Stone" passive (walls/towers +12% HP).
// Mirrors SectWitnessVisionSystem's stamp-and-apply pattern: scans walls
// (WallTag) and towers (WatchTowerTag, TotemTowerTag, WallTowerTag) of
// Fortitude-adopted factions that don't yet carry FortitudeHpApplied, and
// multiplies both Health.Max and Health.Value by the Lv I factor (×1.12).
//
// Tower range +0.5 (the second half of the Lv I lever) is deferred — tower
// fire range is read in BuildingCombatSystem and will need a per-faction
// scalar. Phase 4 / Phase 5.
//
// task-063 phase 2d.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectFortitudeHpSystem : ISystem
    {
        // Per-level HP multiplier. Phase 4 lever upgrades read AppliedLevel
        // and apply the diff (factorAt(new) / factorAt(old)) — see below.
        public static float MultiplierFor(byte level) => level switch
        {
            2 => 1.25f,
            3 => 1.40f,
            _ => 1.12f,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Pass 1: brand-new walls/towers that haven't received any HP bonus yet.
            var pendingNewStamps = new NativeList<Entity>(8, Allocator.Temp);
            var pendingNewLevels = new NativeList<byte>(8, Allocator.Temp);

            foreach (var (health, faction, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<FortitudeHpApplied>()
                .WithEntityAccess())
            {
                // Only apply to walls + towers — the Lv I lever specifies them.
                bool isWallOrTower =
                    em.HasComponent<WallTag>(entity)
                 || em.HasComponent<WallHubTag>(entity)
                 || em.HasComponent<WallInstanceTag>(entity)
                 || em.HasComponent<WallTowerTag>(entity)
                 || em.HasComponent<WatchTowerTag>(entity)
                 || em.HasComponent<TotemTowerTag>(entity);
                if (!isWallOrTower) continue;

                byte level = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    SectConfig.Fortitude, SectLeverKind.Passive);
                if (level == 0) continue;

                float mult = MultiplierFor(level);
                var hp = health.ValueRO;
                health.ValueRW.Max   = (int)(hp.Max   * mult);
                health.ValueRW.Value = (int)(hp.Value * mult);

                pendingNewStamps.Add(entity);
                pendingNewLevels.Add(level);
            }

            for (int i = 0; i < pendingNewStamps.Length; i++)
            {
                if (em.Exists(pendingNewStamps[i]))
                    em.AddComponentData(pendingNewStamps[i],
                        new FortitudeHpApplied { AppliedLevel = pendingNewLevels[i] });
            }
            pendingNewStamps.Dispose();
            pendingNewLevels.Dispose();

            // Pass 2: already-stamped buildings whose faction's lever level
            // has since increased. Apply the diff factor to bring them up
            // to date and bump AppliedLevel. (task-063 phase 4)
            foreach (var (health, faction, applied, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<FactionTag>, RefRW<FortitudeHpApplied>>()
                .WithAll<BuildingTag>()
                .WithEntityAccess())
            {
                byte currentLevel = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    SectConfig.Fortitude, SectLeverKind.Passive);
                byte appliedLevel = applied.ValueRO.AppliedLevel;
                if (currentLevel <= appliedLevel) continue;

                float diffMult = MultiplierFor(currentLevel) / MultiplierFor(appliedLevel);
                var hp = health.ValueRO;
                health.ValueRW.Max   = (int)(hp.Max   * diffMult);
                health.ValueRW.Value = (int)(hp.Value * diffMult);
                applied.ValueRW.AppliedLevel = currentLevel;
            }
        }
    }
}
