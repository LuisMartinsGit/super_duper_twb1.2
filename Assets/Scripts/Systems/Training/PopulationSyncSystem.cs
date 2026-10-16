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
    ///
    /// PERF (2026-10-16): Burst-compiled. Everything here was already
    /// Burst-safe (unmanaged components, a NativeHashMap, HasComponent), but
    /// the class carried no [BurstCompile], so two managed loops summed every
    /// building and every unit on every tick. Integer sums in the same order:
    /// result-identical.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TrainingSystem))]
    public partial struct PopulationSyncSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FactionPopulation>();
        }

        [BurstCompile]
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

            // Sum PopulationCost from living units. Disabled ones count:
            // a unit garrisoned inside a reinforced wall is absorbed — it
            // stops matching ordinary queries — and must still occupy its
            // population slot, or garrisoning would be a free pop refund.
            // docs/Design/Age_1_Alanthor.md § Garrison slots.
            foreach (var (popCost, factionTag) in SystemAPI
                .Query<RefRO<PopulationCost>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities))
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

                // Runai / Feraldis override: pop max is always 200
                if (em.HasComponent<RunaiPopOverride>(entity) || em.HasComponent<FeraldisPopOverride>(entity))
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
