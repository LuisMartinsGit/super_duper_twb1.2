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
        // Lv I factor. Phase 4: 1.25× / 1.40× for Lv II / Lv III.
        private const float HpMultiplierLv1 = 1.12f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            var pendingStamps = new NativeList<Entity>(8, Allocator.Temp);

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

                if (!SectQuery.IsAdoptedAtLeast(em, faction.ValueRO.Value,
                        SectConfig.Fortitude, SectLeverKind.Passive)) continue;

                var hp = health.ValueRO;
                int newMax = (int)(hp.Max * HpMultiplierLv1);
                int newVal = (int)(hp.Value * HpMultiplierLv1);
                health.ValueRW.Max = newMax;
                health.ValueRW.Value = newVal;

                pendingStamps.Add(entity);
            }

            for (int i = 0; i < pendingStamps.Length; i++)
            {
                if (em.Exists(pendingStamps[i]))
                    em.AddComponentData(pendingStamps[i], new FortitudeHpApplied { AppliedLevel = 1 });
            }
            pendingStamps.Dispose();
        }
    }
}
