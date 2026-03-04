// ResourceTickSystem.cs
// ECS system that applies passive resource income from buildings
// Part of: Economy/

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Applies passive resource income from buildings to faction banks.
    ///
    /// Supplies use per-building discrete ticks (e.g., Hall: 50 every 15s, GathererHut: 25 every 10s).
    /// Other resources (Iron, Crystal, Veilsteel, Glow) still tick once per game-second.
    ///
    /// Only completed buildings contribute (those without UnderConstruction component).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ResourceTickSystem : ISystem
    {
        private int _lastWholeSecondGlobally;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _lastWholeSecondGlobally = (int)math.floor(state.WorldUnmanaged.Time.ElapsedTime);

            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<FactionTag, FactionResources, ResourceTickState>()
                .Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var nowWhole = (int)math.floor(state.WorldUnmanaged.Time.ElapsedTime);

            // =================================================================
            // SUPPLIES: Per-building discrete ticks
            // =================================================================
            var suppliesPerFaction = new NativeParallelHashMap<byte, int>(16, Allocator.Temp);

            foreach (var (tag, income) in
                SystemAPI.Query<RefRO<FactionTag>, RefRW<SuppliesIncome>>()
                    .WithNone<UnderConstruction>())
            {
                if (income.ValueRO.Interval <= 0f || income.ValueRO.PerTick <= 0f) continue;

                income.ValueRW.Elapsed += dt;

                if (income.ValueRO.Elapsed >= income.ValueRO.Interval)
                {
                    int ticks = (int)(income.ValueRO.Elapsed / income.ValueRO.Interval);
                    income.ValueRW.Elapsed -= ticks * income.ValueRO.Interval;

                    int amount = (int)(income.ValueRO.PerTick * ticks);
                    if (amount <= 0) continue;

                    var key = (byte)tag.ValueRO.Value;
                    if (suppliesPerFaction.TryGetValue(key, out var existing))
                        suppliesPerFaction[key] = existing + amount;
                    else
                        suppliesPerFaction.TryAdd(key, amount);
                }
            }

            // Apply supplies to faction banks
            if (!suppliesPerFaction.IsEmpty)
            {
                foreach (var (tag, bank) in
                    SystemAPI.Query<RefRO<FactionTag>, RefRW<FactionResources>>())
                {
                    var facKey = (byte)tag.ValueRO.Value;
                    if (suppliesPerFaction.TryGetValue(facKey, out var supplies))
                        bank.ValueRW.Supplies += supplies;
                }
            }
            suppliesPerFaction.Dispose();

            // =================================================================
            // OTHER RESOURCES: Per-second accumulation (unchanged)
            // =================================================================
            if (nowWhole == _lastWholeSecondGlobally)
                return;

            var deltaSeconds = math.max(0, nowWhole - _lastWholeSecondGlobally);
            _lastWholeSecondGlobally = nowWhole;

            var perFactionIncome = new NativeParallelHashMap<byte, OtherIncomeAccumulator>(16, Allocator.Temp);

            CollectIronIncome(ref state, ref perFactionIncome);
            CollectCrystalIncome(ref state, ref perFactionIncome);
            CollectVeilsteelIncome(ref state, ref perFactionIncome);
            CollectGlowIncome(ref state, ref perFactionIncome);

            if (!perFactionIncome.IsEmpty)
            {
                foreach (var (tag, bank, tick) in
                    SystemAPI.Query<RefRO<FactionTag>, RefRW<FactionResources>, RefRW<ResourceTickState>>())
                {
                    int missed = math.max(0, nowWhole - tick.ValueRO.LastWholeSecond);
                    if (missed <= 0) continue;

                    var facKey = (byte)tag.ValueRO.Value;
                    if (perFactionIncome.TryGetValue(facKey, out var income))
                    {
                        var resources = bank.ValueRO;
                        resources.Iron += income.Iron * missed;
                        resources.Crystal += income.Crystal * missed;
                        resources.Veilsteel += income.Veilsteel * missed;
                        resources.Glow += income.Glow * missed;
                        bank.ValueRW = resources;
                    }

                    tick.ValueRW.LastWholeSecond = nowWhole;
                }
            }
            else
            {
                // Still update tick state even if no other income
                foreach (var tick in SystemAPI.Query<RefRW<ResourceTickState>>())
                    tick.ValueRW.LastWholeSecond = nowWhole;
            }

            perFactionIncome.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INCOME COLLECTION METHODS (non-supplies)
        // ═══════════════════════════════════════════════════════════════════════

        private void CollectIronIncome(ref SystemState state,
            ref NativeParallelHashMap<byte, OtherIncomeAccumulator> perFactionIncome)
        {
            foreach (var (tag, income) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<IronIncome>>()
                    .WithNone<UnderConstruction>())
            {
                int perSecond = income.ValueRO.PerMinute / 60;
                if (perSecond <= 0) continue;

                var key = (byte)tag.ValueRO.Value;
                if (perFactionIncome.TryGetValue(key, out var existing))
                {
                    existing.Iron += perSecond;
                    perFactionIncome[key] = existing;
                }
                else
                {
                    perFactionIncome.TryAdd(key, new OtherIncomeAccumulator { Iron = perSecond });
                }
            }
        }

        private void CollectCrystalIncome(ref SystemState state,
            ref NativeParallelHashMap<byte, OtherIncomeAccumulator> perFactionIncome)
        {
            foreach (var (tag, income) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<CrystalIncome>>()
                    .WithNone<UnderConstruction>())
            {
                int perSecond = income.ValueRO.PerMinute / 60;
                if (perSecond <= 0) continue;

                var key = (byte)tag.ValueRO.Value;
                if (perFactionIncome.TryGetValue(key, out var existing))
                {
                    existing.Crystal += perSecond;
                    perFactionIncome[key] = existing;
                }
                else
                {
                    perFactionIncome.TryAdd(key, new OtherIncomeAccumulator { Crystal = perSecond });
                }
            }
        }

        private void CollectVeilsteelIncome(ref SystemState state,
            ref NativeParallelHashMap<byte, OtherIncomeAccumulator> perFactionIncome)
        {
            foreach (var (tag, income) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<VeilsteelIncome>>()
                    .WithNone<UnderConstruction>())
            {
                int perSecond = income.ValueRO.PerMinute / 60;
                if (perSecond <= 0) continue;

                var key = (byte)tag.ValueRO.Value;
                if (perFactionIncome.TryGetValue(key, out var existing))
                {
                    existing.Veilsteel += perSecond;
                    perFactionIncome[key] = existing;
                }
                else
                {
                    perFactionIncome.TryAdd(key, new OtherIncomeAccumulator { Veilsteel = perSecond });
                }
            }
        }

        private void CollectGlowIncome(ref SystemState state,
            ref NativeParallelHashMap<byte, OtherIncomeAccumulator> perFactionIncome)
        {
            foreach (var (tag, income) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<GlowIncome>>()
                    .WithNone<UnderConstruction>())
            {
                int perSecond = income.ValueRO.PerMinute / 60;
                if (perSecond <= 0) continue;

                var key = (byte)tag.ValueRO.Value;
                if (perFactionIncome.TryGetValue(key, out var existing))
                {
                    existing.Glow += perSecond;
                    perFactionIncome[key] = existing;
                }
                else
                {
                    perFactionIncome.TryAdd(key, new OtherIncomeAccumulator { Glow = perSecond });
                }
            }
        }

        private struct OtherIncomeAccumulator
        {
            public int Iron;
            public int Crystal;
            public int Veilsteel;
            public int Glow;
        }
    }
}
