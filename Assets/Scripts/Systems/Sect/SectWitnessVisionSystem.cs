// SectWitnessVisionSystem.cs
// Implements Witness's Lv I "All-Seeing" passive (scouts +25% vision range).
//
// Scans Scout-class units that don't yet carry WitnessVisionApplied and,
// for those whose faction has Witness adopted, multiplies LineOfSight.Radius
// by the Lv I factor and stamps WitnessVisionApplied to mark them as
// already-bonused. This handles BOTH paths uniformly:
//   - Scouts existing at adoption time: stamped on the next tick.
//   - Scouts trained AFTER adoption: stamped when they enter the query.
//
// Lv II / Lv III scaling lives in Phase 4 — when the lever level upgrades,
// a diff system will apply the (newFactor - oldFactor) delta to scouts
// already stamped at the prior level. The AppliedLevel byte on the stamp
// is what makes that diff math possible.
//
// task-063 phase 2c.
//
// Location: Assets/Scripts/Systems/Sect/SectWitnessVisionSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectWitnessVisionSystem : ISystem
    {
        // Lv I factor. Phase 4: 1.50× / 1.75× for Lv II / Lv III.
        private const float VisionMultiplierLv1 = 1.25f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Collect scouts that need the bonus stamped — defer the actual
            // archetype mutation (AddComponent) until after the foreach.
            var pendingStamps = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var (unit, faction, los, entity) in SystemAPI
                .Query<RefRO<UnitTag>, RefRO<FactionTag>, RefRW<LineOfSight>>()
                .WithNone<WitnessVisionApplied>()
                .WithEntityAccess())
            {
                if (unit.ValueRO.Class != UnitClass.Scout) continue;
                if (!SectQuery.IsAdoptedAtLeast(em, faction.ValueRO.Value,
                        SectConfig.Witness, SectLeverKind.Passive)) continue;

                los.ValueRW.Radius *= VisionMultiplierLv1;
                pendingStamps.Add(entity);
            }

            for (int i = 0; i < pendingStamps.Length; i++)
            {
                if (em.Exists(pendingStamps[i]))
                    em.AddComponentData(pendingStamps[i], new WitnessVisionApplied { AppliedLevel = 1 });
            }
            pendingStamps.Dispose();
        }
    }
}
