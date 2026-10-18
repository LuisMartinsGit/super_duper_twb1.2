// SectAshPyreSystem.cs
// Implements Ash's Lv I "Pyre's Promise" passive: when a unit of an Ash-
// adopted faction dies, it leaves a small burning-ground patch at its
// death position — a low-DPS, short-lived AoE that burns whoever walks
// through it. Fire is OWNERLESS (docs/Design/Fire.md §3): the patch burns the
// dead unit's comrades and allies too; the FactionTag stamped on it is kill
// credit only, BurningGroundSystem never filters on it. Mirrors PillageSystem's death-event hook (runs before
// DeathSystem with the WithNone marker so each death fires exactly once).
//
// Lv I tuning (overridden per-level by SectAshPyreSystem.GetTuning when
// task-063 phase 4 lands):
//   - DPS:      4
//   - Duration: 3s
//   - Radius:   2m
//
// task-063 phase 2f.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectAshPyreSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            var pyreSpawns = new NativeList<float3>(Allocator.Temp);
            var pyreFactions = new NativeList<Faction>(Allocator.Temp);
            var pyreDps = new NativeList<float>(Allocator.Temp);
            var pyreRadius = new NativeList<float>(Allocator.Temp);
            var pyreDuration = new NativeList<float>(Allocator.Temp);

            // PERF (2026-10-16): the dying-unit scan is a Burst pass over the
            // Health column (it was a managed per-unit loop over every unit,
            // every tick), and LevelOf — a string lookup, a bank read and a
            // sect-building walk — is asked once per faction per tick instead
            // of once per corpse. Same rows in the same chunk order, same
            // answers: result-identical.
            var deadPos = new NativeList<float3>(8, Allocator.TempJob);   // TempJob: a job writes it (Temp is refused even for .Run)
            var deadFac = new NativeList<Faction>(8, Allocator.TempJob);
            new DyingUnitScanJob { Positions = deadPos, Factions = deadFac }.Run();

            if (deadPos.Length > 0)
            {
                var levels = new NativeArray<byte>(256, Allocator.Temp);
                for (int i = 0; i < levels.Length; i++) levels[i] = 255;
                for (int i = 0; i < deadPos.Length; i++)
                {
                    Faction victimFaction = deadFac[i];
                    int fi = (byte)victimFaction;
                    if (levels[fi] == 255)
                        levels[fi] = SectQuery.LevelOf(em, victimFaction,
                            SectConfig.Ash, SectLeverKind.Passive);
                    byte level = levels[fi];
                    if (level == 0) continue;

                    GetTuning(level, out float dps, out float duration, out float radius);

                    pyreSpawns.Add(deadPos[i]);
                    pyreFactions.Add(victimFaction);
                    pyreDps.Add(dps);
                    pyreRadius.Add(radius);
                    pyreDuration.Add(duration);
                }
                levels.Dispose();
            }
            deadPos.Dispose();
            deadFac.Dispose();

            for (int i = 0; i < pyreSpawns.Length; i++)
            {
                var pyre = em.CreateEntity(
                    typeof(BurningGround),
                    typeof(LocalTransform),
                    typeof(FactionTag));
                em.SetComponentData(pyre, new BurningGround
                {
                    DPS = pyreDps[i],
                    TimeRemaining = pyreDuration[i],
                    Radius = pyreRadius[i],
                });
                em.SetComponentData(pyre, LocalTransform.FromPositionRotationScale(
                    pyreSpawns[i], quaternion.identity, 1f));
                em.SetComponentData(pyre, new FactionTag { Value = pyreFactions[i] });
            }

            pyreSpawns.Dispose();
            pyreFactions.Dispose();
            pyreDps.Dispose();
            pyreRadius.Dispose();
            pyreDuration.Dispose();
        }

        /// <summary>Units at or below 0 HP not yet handed to the death
        /// animation, in chunk order — the rows the old foreach visited.</summary>
        [BurstCompile]
        [WithAll(typeof(UnitTag))]
        [WithNone(typeof(DeathAnimationState))]
        private partial struct DyingUnitScanJob : IJobEntity
        {
            public NativeList<float3> Positions;
            public NativeList<Faction> Factions;

            void Execute(in Health health, in LocalTransform transform, in FactionTag faction)
            {
                if (health.Value > 0) return;
                Positions.Add(transform.Position);
                Factions.Add(faction.Value);
            }
        }

        /// <summary>
        /// Per-level tuning. Lv I/II/III scale both potency and area.
        /// (task-063 phase 4)
        /// </summary>
        public static void GetTuning(byte level, out float dps, out float duration, out float radius)
        {
            switch (level)
            {
                case 2:  dps = 7f; duration = 4f; radius = 2.5f; return;
                case 3:  dps = 11f; duration = 5f; radius = 3f;  return;
                default: dps = 4f; duration = 3f; radius = 2f;   return;
            }
        }
    }
}
