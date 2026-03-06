// File: Assets/Scripts/Systems/Training/TrainingSystem.cs
using Unity.Burst;
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
    /// Unified training system that processes unit production for all buildings.
    ///
    /// Training workflow:
    /// 1. UI adds TrainQueueItem to building's buffer (cost paid at queue time)
    /// 2. System starts training first item when building is idle
    /// 3. Timer counts down based on unit's trainingTime from TechTreeDB
    /// 4. When complete, checks population capacity before spawning
    /// 5. Unit spawns at rally point (or default position near building)
    ///
    /// Works with: Hall, Barracks, and any building with TrainingState + TrainQueueItem buffer
    /// </summary>
    // NOTE: No [BurstCompile] — this system uses managed types
    // (TechTreeDB, String, Debug.Log) that are incompatible with Burst.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TrainingSystem : ISystem
    {
        /// <summary>
        /// Holds data for a unit spawn that must be deferred until after query iteration.
        /// Structural changes (entity creation, AddComponent) cannot happen during iteration.
        /// </summary>
        private struct DeferredSpawn
        {
            public Entity Building;
            public FixedString64Bytes UnitId;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TrainingState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var db = TechTreeDB.Instance;
            if (db == null) return;

            float dt = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Track how many pop slots were consumed by spawns THIS frame
            // to prevent multiple barracks spawning past the cap in the same frame
            var spawnedPopThisFrame = new NativeHashMap<int, int>(8, Allocator.Temp);

            // Collect spawn requests during iteration — execute AFTER loop to avoid structural changes
            var deferredSpawns = new NativeList<DeferredSpawn>(4, Allocator.Temp);

            // ═══════════ Phase 1: Process timers, collect spawn requests ═══════════
            // Exclude BatchTrainingTag entities — those are handled by BatchTrainingSystem
            foreach (var (ts, entity) in SystemAPI
                         .Query<RefRW<TrainingState>>()
                         .WithNone<UnderConstruction, BatchTrainingTag>()
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
                        // Unknown unit - remove from queue
                        queue.RemoveAt(0);
                        UnityEngine.Debug.LogWarning($"Unknown unit ID in training queue: {unitId}");
                        continue;
                    }

                    // Start training
                    float trainingTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;
                    ts.ValueRW.Busy = 1;
                    ts.ValueRW.Remaining = trainingTime;
                }
                else
                {
                    // Tick training timer
                    ts.ValueRW.Remaining -= dt;

                    if (ts.ValueRW.Remaining <= 0f && queue.Length > 0)
                    {
                        // Training complete - check population before spawning
                        var unitId = queue[0].UnitId.ToString();
                        var em = state.EntityManager;
                        var faction = em.GetComponentData<FactionTag>(entity).Value;
                        int requiredPop = PopulationHelper.GetUnitPopulationCost(unitId);

                        // Include units already spawned this frame in the capacity check
                        int facKey = (int)faction;
                        spawnedPopThisFrame.TryGetValue(facKey, out int extraSpawned);

                        if (HasPopulationCapacityWithExtra(ref state, faction, requiredPop, extraSpawned))
                        {
                            // Remove queue item and reset state (no structural changes here)
                            queue.RemoveAt(0);
                            ts.ValueRW.Busy = 0;
                            ts.ValueRW.Remaining = 0f;

                            // Defer spawn until after iteration completes
                            deferredSpawns.Add(new DeferredSpawn
                            {
                                Building = entity,
                                UnitId = new FixedString64Bytes(unitId)
                            });

                            // Track the pop consumed this frame
                            spawnedPopThisFrame[facKey] = extraSpawned + requiredPop;
                        }
                        else
                        {
                            // Not enough population - keep training state active, retry next frame
                            // Don't reset Busy or Remaining so the unit spawns immediately when pop frees up
                            ts.ValueRW.Remaining = 0f;
                            // Busy stays 1 - training is done, just waiting for pop capacity
                        }
                    }
                }
            }

            // ═══════════ Phase 2: Spawn units AFTER iteration (structural changes safe) ═══════════
            for (int i = 0; i < deferredSpawns.Length; i++)
            {
                SpawnUnit(ref state, ecb, deferredSpawns[i].Building, deferredSpawns[i].UnitId.ToString());
            }

            deferredSpawns.Dispose();
            spawnedPopThisFrame.Dispose();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Check if faction has enough population capacity for the unit,
        /// accounting for units already spawned this frame.
        /// </summary>
        private bool HasPopulationCapacityWithExtra(ref SystemState state, Faction faction, int requiredPop, int extraSpawnedThisFrame)
        {
            foreach (var (tag, pop) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionPopulation>>())
            {
                if (tag.ValueRO.Value == faction)
                {
                    return (pop.ValueRO.Current + extraSpawnedThisFrame + requiredPop) <= pop.ValueRO.Max;
                }
            }
            // No population tracking found - allow by default
            return true;
        }

        /// <summary>
        /// Spawns a unit from its ID. Cost already paid when queued.
        /// </summary>
        private static void SpawnUnit(ref SystemState state, EntityCommandBuffer ecb, Entity building, string unitId)
        {
            var em = state.EntityManager;
            var transform = em.GetComponentData<LocalTransform>(building);
            var faction = em.GetComponentData<FactionTag>(building).Value;

            // Always spawn near the building, then move to rally point
            float3 spawnPos = transform.Position + new float3(1.6f, 0, 1.6f);

            // Find empty position near the building to avoid overlap
            float spawnRadius = 0.5f;
            float3 finalPos = SpawnPlacementHelper.FindEmptyPosition(
                spawnPos,
                spawnRadius,
                em,
                maxAttempts: 16
            );

            // Check if building has a rally point to move to after spawning
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

            // Create unit via centralized UnitFactory (handles all unit types including culture units)
            Entity unit = UnitFactory.Create(em, unitId, finalPos, faction);

            // Apply all completed tech effects to the newly spawned unit
            TechEffectSystem.ApplyCompletedTechEffects(em, unit, faction);

            // Issue move command to rally point if one is set
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

            UnityEngine.Debug.Log($"Spawned {unitId} for {faction} at {finalPos}");
        }
    }
}