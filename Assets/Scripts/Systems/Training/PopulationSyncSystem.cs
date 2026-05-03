// File: Assets/Scripts/Systems/Training/PopulationSyncSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Training
{
    /// <summary>
    /// Keeps FactionPopulation.Max and .Current in sync each frame.
    ///
    /// Max  = sum of PopulationProvider.Amount from completed buildings (no UnderConstruction)
    /// Current = sum of PopulationCost.Amount from all living units
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TrainingSystem))]
    public partial struct PopulationSyncSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FactionPopulation>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Temp maps: faction -> sum
            var maxPop = new NativeHashMap<int, int>(8, Allocator.Temp);
            var curPop = new NativeHashMap<int, int>(8, Allocator.Temp);

            // Sum PopulationProvider from completed buildings
            foreach (var (provider, factionTag) in SystemAPI
                .Query<RefRO<PopulationProvider>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>())
            {
                int fac = (int)factionTag.ValueRO.Value;
                maxPop.TryGetValue(fac, out int existing);
                maxPop[fac] = existing + provider.ValueRO.Amount;
            }

            // Sum PopulationCost from living units
            foreach (var (popCost, factionTag) in SystemAPI
                .Query<RefRO<PopulationCost>, RefRO<FactionTag>>()
                .WithAll<UnitTag>())
            {
                int fac = (int)factionTag.ValueRO.Value;
                curPop.TryGetValue(fac, out int existing);
                curPop[fac] = existing + popCost.ValueRO.Amount;
            }

            // Write results to FactionPopulation bank entities
            foreach (var (pop, factionTag, entity) in SystemAPI
                .Query<RefRW<FactionPopulation>, RefRO<FactionTag>>()
                .WithEntityAccess())
            {
                int fac = (int)factionTag.ValueRO.Value;

                maxPop.TryGetValue(fac, out int totalMax);
                curPop.TryGetValue(fac, out int totalCur);

                // Runai override: pop max is always 200
                if (em.HasComponent<RunaiPopOverride>(entity))
                    totalMax = FactionPopulation.AbsoluteMax;

                // Clamp max to absolute cap
                if (totalMax > FactionPopulation.AbsoluteMax)
                    totalMax = FactionPopulation.AbsoluteMax;

                pop.ValueRW.Max = totalMax;
                pop.ValueRW.Current = totalCur;
            }

            maxPop.Dispose();
            curPop.Dispose();
        }
    }
}
