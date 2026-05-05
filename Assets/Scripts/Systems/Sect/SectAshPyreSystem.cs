// SectAshPyreSystem.cs
// Implements Ash's Lv I "Pyre's Promise" passive: when a unit of an Ash-
// adopted faction dies, it leaves a small burning-ground patch at its
// death position — a low-DPS, short-lived AoE that damages enemies who
// walk through it. Mirrors PillageSystem's death-event hook (runs before
// DeathSystem with the WithNone marker so each death fires exactly once).
//
// Lv I tuning (overridden per-level by SectAshPyreSystem.GetTuning when
// task-063 phase 4 lands):
//   - DPS:      4
//   - Duration: 3s
//   - Radius:   2m
//
// task-063 phase 2f.
//
// Location: Assets/Scripts/Systems/Sect/SectAshPyreSystem.cs

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

            foreach (var (health, transform, faction, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Faction victimFaction = faction.ValueRO.Value;
                byte level = SectQuery.LevelOf(em, victimFaction,
                    SectConfig.Ash, SectLeverKind.Passive);
                if (level == 0) continue;

                GetTuning(level, out float dps, out float duration, out float radius);

                pyreSpawns.Add(transform.ValueRO.Position);
                pyreFactions.Add(victimFaction);
                pyreDps.Add(dps);
                pyreRadius.Add(radius);
                pyreDuration.Add(duration);
            }

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
