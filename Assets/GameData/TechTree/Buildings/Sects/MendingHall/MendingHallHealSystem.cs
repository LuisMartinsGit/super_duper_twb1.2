// File: Assets/GameData/TechTree/Buildings/Sects/MendingHall/MendingHallHealSystem.cs
// The Mending Hall's effect: "damaged units that walk inside heal over time"
// (docs/Design/Sects.md section 4, Sect of Renewal).
//
// "Inside" is read as "within the building's footprint", which is what the
// player sees: the unit walks onto the hall's tiles and starts mending. The
// footprint is the same BuildingSize the placement grid and the cost field
// use, so there is no second notion of the building's extent to keep in sync.
//
// This is the BUILDING's effect and is separate from the sect's passive
// (SectRenewalAutoRepairSystem, which mends BUILDINGS out of combat). Both can
// be live at once; they never touch the same entity.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MendingHallHealSystem : ISystem
    {
        /// <summary>Fraction of max HP restored per second to a unit inside.</summary>
        private const float HealFractionPerSecond = 0.04f;

        /// <summary>Half-second tick. Healing 200 units against 5 halls every
        /// frame is pure waste when the effect is measured in HP per second.</summary>
        private const float TickInterval = 0.5f;

        private SimCadence.Periodic _accum;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MendingHallTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float elapsed = _accum.DueStep(SystemAPI.Time.DeltaTime, TickInterval);
            if (elapsed <= 0f) return;

            var em = state.EntityManager;

            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<MendingHallTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var halls = hallQuery.ToEntityArray(Allocator.Temp);
            if (halls.Length == 0) return;

            // Only completed halls mend. A foundation is a building site.
            var live = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < halls.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(halls[i])) continue;
                if (em.HasComponent<Health>(halls[i])
                    && em.GetComponentData<Health>(halls[i]).Value <= 0) continue;
                // Heavy Bureaucracy (Antiquity) stops a building producing
                // anything at all - healing included.
                if (em.HasComponent<SectShutdown>(halls[i])) continue;
                live.Add(halls[i]);
            }
            if (live.Length == 0) { live.Dispose(); return; }

            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var units = unitQuery.ToEntityArray(Allocator.Temp);

            for (int u = 0; u < units.Length; u++)
            {
                var unit = units[u];
                var hp = em.GetComponentData<Health>(unit);
                if (hp.Value <= 0 || hp.Value >= hp.Max) continue;

                float3 up = em.GetComponentData<LocalTransform>(unit).Position;
                var unitFaction = em.GetComponentData<FactionTag>(unit).Value;

                for (int h = 0; h < live.Length; h++)
                {
                    var hall = live[h];
                    // Allies are welcome inside; enemies are not
                    // (docs/Design/Teams.md - Alliances is the only valid test).
                    if (!Alliances.AreAllied(
                            em.GetComponentData<FactionTag>(hall).Value, unitFaction)) continue;

                    float3 hpos = em.GetComponentData<LocalTransform>(hall).Position;
                    float halfW = 4f, halfH = 4f;
                    if (em.HasComponent<BuildingSize>(hall))
                    {
                        var size = em.GetComponentData<BuildingSize>(hall);
                        halfW = size.Width * 0.5f;
                        halfH = size.Height * 0.5f;
                    }
                    if (math.abs(up.x - hpos.x) > halfW) continue;
                    if (math.abs(up.z - hpos.z) > halfH) continue;

                    int heal = (int)math.ceil(hp.Max * HealFractionPerSecond * elapsed);
                    hp.Value = math.min(hp.Max, hp.Value + heal);
                    em.SetComponentData(unit, hp);
                    break;  // one hall mends it; standing in two is not twice as good
                }
            }

            live.Dispose();
        }
    }
}
