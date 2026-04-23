// BatchTrainingSystem.cs
// Handles batch training for Feraldis Longhouse (5 units at once with discounts)
// Location: Assets/Scripts/Systems/Training/BatchTrainingSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Research;

namespace TheWaningBorder.Systems.Training
{
    /// <summary>
    /// Batch training system for Feraldis Longhouse buildings.
    ///
    /// The Longhouse batch-trains 5 units of the same type simultaneously:
    /// - 10% time discount: totalTime = singleTime * 0.9 (NOT singleTime * 5)
    /// - All 5 units spawn at once when training completes
    /// - Cost discount (5%) is handled at queue time by the UI
    ///
    /// This system handles ONLY entities with BatchTrainingTag.
    /// The regular TrainingSystem excludes BatchTrainingTag entities.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BatchTrainingSystem : ISystem
    {
        /// <summary>Holds data for batch spawn deferred until after query iteration.</summary>
        private struct DeferredBatchSpawn
        {
            public Entity Building;
            public FixedString64Bytes UnitId;
            public int Count;
        }

        /// <summary>Number of units spawned per batch.</summary>
        private const int BatchSize = 5;

        /// <summary>Training time multiplier (10% discount).</summary>
        private const float TimeMultiplier = 0.9f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BatchTrainingTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var db = TechTreeDB.Instance;
            if (db == null) return;

            float dt = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Track pop consumed this frame to avoid over-spawning
            var spawnedPopThisFrame = new NativeHashMap<int, int>(8, Allocator.Temp);

            // Collect deferred batch spawns
            var deferredSpawns = new NativeList<DeferredBatchSpawn>(4, Allocator.Temp);

            // ========== Phase 1: Process timers, collect batch spawn requests ==========
            foreach (var (ts, entity) in SystemAPI
                         .Query<RefRW<TrainingState>>()
                         .WithAll<BatchTrainingTag>()
                         .WithNone<UnderConstruction>()
                         .WithEntityAccess())
            {
                var queue = state.EntityManager.GetBuffer<TrainQueueItem>(entity);

                // Start training if idle and queue has items
                if (ts.ValueRO.Busy == 0)
                {
                    if (queue.Length == 0) continue;

                    var unitId = queue[0].UnitId.ToString();
                    if (!db.TryGetUnit(unitId, out var udef))
                    {
                        queue.RemoveAt(0);
                        continue;
                    }

                    // Start training with 10% time discount
                    float trainingTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;
                    float batchTime = trainingTime * TimeMultiplier;

                    ts.ValueRW.Busy = 1;
                    ts.ValueRW.Remaining = batchTime;
                }
                else
                {
                    // Tick training timer
                    ts.ValueRW.Remaining -= dt;

                    if (ts.ValueRO.Remaining <= 0f && queue.Length > 0)
                    {
                        // Training complete - check population for all 5 units
                        var unitId = queue[0].UnitId.ToString();
                        var em = state.EntityManager;
                        var faction = em.GetComponentData<FactionTag>(entity).Value;
                        int perUnitPop = PopulationHelper.GetUnitPopulationCost(unitId);
                        int totalPopNeeded = perUnitPop * BatchSize;

                        // Include units already spawned this frame
                        int facKey = (int)faction;
                        spawnedPopThisFrame.TryGetValue(facKey, out int extraSpawned);

                        if (HasPopulationCapacity(ref state, faction, totalPopNeeded, extraSpawned))
                        {
                            // Remove queue item and reset state
                            queue.RemoveAt(0);
                            ts.ValueRW.Busy = 0;
                            ts.ValueRW.Remaining = 0f;

                            // Defer batch spawn
                            deferredSpawns.Add(new DeferredBatchSpawn
                            {
                                Building = entity,
                                UnitId = new FixedString64Bytes(unitId),
                                Count = BatchSize
                            });

                            // Track pop consumed
                            spawnedPopThisFrame[facKey] = extraSpawned + totalPopNeeded;
                        }
                        else
                        {
                            // Not enough pop - wait (keep Busy=1, Remaining=0)
                            ts.ValueRW.Remaining = 0f;
                        }
                    }
                }
            }

            // ========== Phase 2: Spawn batch units AFTER iteration ==========
            for (int i = 0; i < deferredSpawns.Length; i++)
            {
                var spawn = deferredSpawns[i];
                SpawnBatch(ref state, ecb, spawn.Building, spawn.UnitId.ToString(), spawn.Count);
            }

            deferredSpawns.Dispose();
            spawnedPopThisFrame.Dispose();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Check if faction has enough population capacity for the batch,
        /// accounting for units already spawned this frame.
        /// </summary>
        private bool HasPopulationCapacity(ref SystemState state, Faction faction, int requiredPop, int extraSpawnedThisFrame)
        {
            foreach (var (tag, pop) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionPopulation>>())
            {
                if (tag.ValueRO.Value == faction)
                {
                    return (pop.ValueRO.Current + extraSpawnedThisFrame + requiredPop) <= pop.ValueRO.Max;
                }
            }
            return true;
        }

        /// <summary>
        /// Spawns a batch of units around the building.
        /// Uses SpawnPlacementHelper to find non-overlapping positions.
        /// </summary>
        private static void SpawnBatch(ref SystemState state, EntityCommandBuffer ecb,
            Entity building, string unitId, int count)
        {
            var em = state.EntityManager;
            var transform = em.GetComponentData<LocalTransform>(building);
            var faction = em.GetComponentData<FactionTag>(building).Value;

            // Get rally point
            float3 rallyTarget = float3.zero;
            bool hasRally = false;
            if (em.HasComponent<RallyPoint>(building))
            {
                var rally = em.GetComponentData<RallyPoint>(building);
                if (rally.Has != 0)
                {
                    rallyTarget = rally.Position;
                    hasRally = true;
                }
            }

            for (int i = 0; i < count; i++)
            {
                // Offset spawn position slightly for each unit in the batch
                float angle = (i / (float)count) * math.PI * 2f;
                float3 offset = new float3(math.cos(angle) * 1.5f, 0, math.sin(angle) * 1.5f);
                float3 spawnPos = transform.Position + new float3(1.6f, 0, 1.6f) + offset;

                float3 finalPos = SpawnPlacementHelper.FindEmptyPosition(
                    spawnPos, 0.5f, em, maxAttempts: 16);

                // Create unit via the same factory switch. UnitFactory already
                // applies the TechTreeDB stats at creation time (Fix #243: the
                // redundant ecb.SetComponent overwrites previously done here
                // raced against any factory-specific adjustments and dropped
                // them, so they have been removed).
                Entity unit = SpawnSingleUnit(em, unitId, finalPos, faction);

                // Apply completed tech effects
                TechEffectSystem.ApplyCompletedTechEffects(em, unit, faction);

                // Issue move to rally point
                if (hasRally)
                {
                    if (!em.HasComponent<DesiredDestination>(unit))
                        em.AddComponentData(unit, new DesiredDestination { Position = rallyTarget, Has = 1 });
                        else
                            em.SetComponentData(unit, new DesiredDestination { Position = rallyTarget, Has = 1 });

                    if (!em.HasComponent<GuardPoint>(unit))
                        em.AddComponentData(unit, new GuardPoint { Position = rallyTarget, Has = 1 });
                        else
                            em.SetComponentData(unit, new GuardPoint { Position = rallyTarget, Has = 1 });
                }
            }

        }

        /// <summary>
        /// Create a single unit from its ID string using centralized UnitFactory.
        /// </summary>
        private static Entity SpawnSingleUnit(EntityManager em, string unitId, float3 position, Faction faction)
        {
            return UnitFactory.Create(em, unitId, position, faction);
        }
    }
}
