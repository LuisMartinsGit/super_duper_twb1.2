// HutConversionSystem.cs
// Ticks GathererHutConverting timers and replaces the hut with the chosen
// target (Wall Hub or Watch Tower) on completion.
// Location: Assets/GameData/TechTree/Buildings/Age 0/Hut/HutConversionSystem.cs
//
// Phase 2 of task-wall-system-bfme2-rework-109. The 5-second timer is
// authoritative — when it expires we snapshot the hut's world position +
// faction, destroy the hut entity, and spawn the target via the existing
// factories (AlanthorWall.CreateHub for the Wall Hub branch, BuildingFactory
// .Create("Alanthor_Tower", …) for the Watch Tower branch).
//
// The two new entities spawn fully-built (no UnderConstruction) so the
// conversion reads as a one-shot transformation rather than a build order.
// This matches the canonical design (Phase 1 / Age_1_Alanthor.md: "no builder
// required").

using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HutConversionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // Gate the system off when no hut is converting — avoids the
            // foreach scan on every tick during normal play.
            state.RequireForUpdate<GathererHutConverting>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot completed conversions first; structural changes
            // (DestroyEntity, factory CreateEntity) inside the foreach
            // would invalidate the query iterator.
            var completed = new NativeList<Entity>(Allocator.Temp);

            foreach (var (conv, entity) in SystemAPI
                .Query<RefRW<GathererHutConverting>>()
                .WithEntityAccess())
            {
                conv.ValueRW.Remaining -= dt;
                if (conv.ValueRO.Remaining <= 0f)
                    completed.Add(entity);
            }

            for (int i = 0; i < completed.Length; i++)
            {
                Entity hut = completed[i];
                if (!em.Exists(hut)) continue;
                if (!em.HasComponent<GathererHutConverting>(hut)) continue;

                // A hut that died mid-conversion belongs to DeathSystem — a
                // sync destroy here races its EndSimulation buffer ("entity
                // does not exist" at playback). The conversion simply fails.
                if (em.HasComponent<BuildingCollapseState>(hut)) continue;
                if (em.HasComponent<Health>(hut) &&
                    em.GetComponentData<Health>(hut).Value <= 0) continue;

                var conv = em.GetComponentData<GathererHutConverting>(hut);

                // Snapshot position + faction BEFORE we destroy the hut —
                // factories read transform data from arguments, not the
                // hut's components.
                var pos = em.GetComponentData<LocalTransform>(hut).Position;
                Faction faction = Faction.Blue;
                if (em.HasComponent<FactionTag>(hut))
                    faction = em.GetComponentData<FactionTag>(hut).Value;

                em.DestroyEntity(hut);

                if (conv.Target == HutConversionTarget.WallHub)
                {
                    AlanthorWall.CreateHub(em, pos, faction);
                }
                else if (conv.Target == HutConversionTarget.WatchTower)
                {
                    BuildingFactory.Create(em, "Alanthor_Tower", pos, faction);
                }
                // Target.None falls through — the hut is gone, nothing
                // spawns. Should not happen since ConvertHutCommandHelper
                // validates the target before adding GathererHutConverting.
            }

            completed.Dispose();
        }
    }
}
