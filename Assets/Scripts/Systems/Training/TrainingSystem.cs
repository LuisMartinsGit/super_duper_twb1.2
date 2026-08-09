// File: Assets/Scripts/Systems/Training/TrainingSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Research;
using TheWaningBorder.Core.Multiplayer;

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
            public int SpawnCount; // Feraldis spawns 2 units/battalions at once
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TrainingState>();
        }

        public void OnUpdate(ref SystemState state)
        {

            float dt = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Track how many pop slots were consumed by spawns THIS frame
            // to prevent multiple barracks spawning past the cap in the same frame
            var spawnedPopThisFrame = new NativeHashMap<int, int>(8, Allocator.Temp);

            // Collect spawn requests during iteration — execute AFTER loop to avoid structural changes
            var deferredSpawns = new NativeList<DeferredSpawn>(4, Allocator.Temp);

            // ═══════════ Phase 1: Process timers, collect spawn requests ═══════════
            // Exclude BatchTrainingTag entities — those are handled by BatchTrainingSystem
            // Exclude AgeUpState entities — Hall can't train during age-up
            foreach (var (ts, entity) in SystemAPI
                         .Query<RefRW<TrainingState>>()
                         .WithNone<UnderConstruction, BatchTrainingTag, AgeUpState>()
                         .WithNone<BuildingUpgrading>()
                         .WithEntityAccess())
            {
                var queue = state.EntityManager.GetBuffer<TrainQueueItem>(entity);

                // Start training if idle and queue has items
                if (ts.ValueRO.Busy == 0)
                {
                    if (queue.Length == 0) continue;

                    var unitId = queue[0].UnitId.ToString();
                    if (!TechCatalog.TryGetUnit(unitId, out var udef))
                    {
                        // Unknown unit - remove from queue
                        queue.RemoveAt(0);
                        continue;
                    }

                    // Start training
                    float trainingTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;

                    // Feraldis culture: 1.75x training time (compensated by 2x spawn output)
                    var buildingFaction = state.EntityManager.GetComponentData<FactionTag>(entity).Value;
                    if (FactionColors.GetFactionCulture(buildingFaction) == Cultures.Feraldis)
                        trainingTime *= 1.75f;

                    // Sect of War: military units train -15/-25/-35% faster
                    // (Lv I/II/III). (task-063 phase 2d / phase 4 scaling)
                    bool isMilitary = UnitFactory.GetUnitClass(unitId) == UnitClass.Melee
                                   || UnitFactory.GetUnitClass(unitId) == UnitClass.Ranged
                                   || UnitFactory.GetUnitClass(unitId) == UnitClass.Siege;
                    if (isMilitary)
                    {
                        byte warLevel = SectQuery.LevelOf(state.EntityManager, buildingFaction,
                            SectConfig.War, SectLeverKind.Passive);
                        if (warLevel > 0)
                            trainingTime *= WarSectCostHelper.TrainTimeMultiplierFor(warLevel);
                    }

                    // Building upgrade: cultured Hall/Barracks train faster.
                    // Multiplier is 1.0 at lvl 0 and shrinks per level.
                    if (state.EntityManager.HasComponent<BuildingUpgradeState>(entity))
                    {
                        byte upLevel = state.EntityManager.GetComponentData<BuildingUpgradeState>(entity).Level;
                        trainingTime *= TheWaningBorder.Core.Settings.BuildingUpgradeConfig
                            .TrainTimeMultiplier[upLevel];
                    }

                    // Conscription (Age 0 Barracks tech): +20% training speed
                    // at the Barracks — time / 1.2.
                    if (state.EntityManager.HasComponent<BarracksTag>(entity))
                    {
                        var research = TheWaningBorder.Economy.FactionResearchState.Instance;
                        if (research != null && research.HasResearched(buildingFaction, "Conscription"))
                            trainingTime /= 1.2f;
                    }

                    // King Lexor respawn tax: +15% training time per prior death.
                    if (TheWaningBorder.Abilities.HeroTrainLimit.IsKingLexorId(unitId))
                        trainingTime *= TheWaningBorder.Abilities.HeroTrainLimit.RespawnTrainMult(buildingFaction);

                    ts.ValueRW.Busy = 1;
                    ts.ValueRW.Remaining = trainingTime;
                    ts.ValueRW.Total = trainingTime;
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

                        // Sect units never get the Feraldis double-spawn.
                        bool isSectUnit = unitId.StartsWith("Sect_");

                        // Feraldis culture: spawn 2 units at once (1.75x cost already paid at queue time)
                        byte factionCulture = FactionColors.GetFactionCulture(faction);
                        int spawnCount = (factionCulture == Cultures.Feraldis && !isSectUnit) ? 2 : 1;
                        requiredPop *= spawnCount;

                        // Include units already spawned this frame in the capacity check
                        int facKey = (int)faction;
                        spawnedPopThisFrame.TryGetValue(facKey, out int extraSpawned);

                        if (HasPopulationCapacityWithExtra(ref state, faction, requiredPop, extraSpawned))
                        {
                            // Remove queue item and reset state (no structural changes here)
                            queue.RemoveAt(0);
                            ts.ValueRW.Busy = 0;
                            ts.ValueRW.Remaining = 0f;
                            ts.ValueRW.Total = 0f;

                            // Defer spawn until after iteration completes
                            deferredSpawns.Add(new DeferredSpawn
                            {
                                Building = entity,
                                UnitId = new FixedString64Bytes(unitId),
                                SpawnCount = spawnCount
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
                for (int s = 0; s < deferredSpawns[i].SpawnCount; s++)
                {
                    SpawnUnit(ref state, ecb, deferredSpawns[i].Building, deferredSpawns[i].UnitId.ToString());
                }
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
            // Spawn outside the building's inflated blocked footprint (BuildingSize cells +
            // 1 cell padding from PassabilityBuildingSync) with extra clearance for the unit.
            float buildingHalf = 2f;
            if (em.HasComponent<BuildingSize>(building))
            {
                var bs = em.GetComponentData<BuildingSize>(building);
                buildingHalf = math.max(bs.Width, bs.Height) * 0.5f;
            }
            float exitOffset = buildingHalf + 4f;
            float3 spawnPos = transform.Position + new float3(exitOffset, 0, exitOffset);

            // Find empty position near the building to avoid overlap
            float spawnRadius = 0.5f;
            float3 finalPos = SpawnPlacementHelper.FindEmptyPosition(
                spawnPos,
                spawnRadius,
                em,
                maxAttempts: 16
            );

            // Check if building has a rally point to move to after spawning.
            // RallyPoint.TargetEntity is an optional follow-up target — when
            // it's a resource node and the freshly-spawned unit is a miner,
            // we'll issue a Gather command instead of a plain move.
            float3 rallyTarget = float3.zero;
            bool hasRally = false;
            Entity rallyTargetEntity = Entity.Null;
            if (em.HasComponent<RallyPoint>(building))
            {
                var rally = em.GetComponentData<RallyPoint>(building);
                if (rally.Has != 0)
                {
                    rallyTarget = rally.Position;
                    hasRally = true;
                    rallyTargetEntity = rally.TargetEntity;
                }
            }

            // All units spawn as individual entities via the centralized
            // UnitFactory. (Battalions removed — every trained unit is a
            // standalone, fully-pathfinding unit.)
            Entity unit = UnitFactory.Create(em, unitId, finalPos, faction);

            // Apply all completed tech effects to the newly spawned unit
            TechEffectSystem.ApplyCompletedTechEffects(em, unit, faction);
            // task-063 phase 1: SectEffectSystem.ApplySectEffectsToUnit removed.

            // Issue move command to rally point if one is set
            if (hasRally)
            {
                // Resource rally — point miners straight at the deposit so
                // they auto-gather without any further player input.
                bool issuedGather = false;
                if (rallyTargetEntity != Entity.Null && em.Exists(rallyTargetEntity)
                    && em.HasComponent<MinerTag>(unit)
                    && (em.HasComponent<IronMineTag>(rallyTargetEntity)
                        || em.HasComponent<VeilstoneOutcroppingTag>(rallyTargetEntity)
                        || em.HasComponent<VeilsteelDepositTag>(rallyTargetEntity)))
                {
                    TheWaningBorder.Core.Commands.CommandRouter.IssueGather(
                        em, unit, rallyTargetEntity,
                        TheWaningBorder.Core.Commands.CommandSource.LocalPlayer);
                    issuedGather = true;
                }

                if (!issuedGather)
                {
                    // RALLY SCATTER (jitter fix, 2026-07-12): every trainee
                    // used to get the SAME exact rally point. Arrival needs
                    // 0.5 m of that exact point, separation holds later
                    // arrivals ~1.5 m off the unit already parked there —
                    // unsatisfiable, so trainee #2+ orbited the gather point
                    // forever. Give each unit its own slot on a golden-angle
                    // spiral around the rally instead. Deterministic: derived
                    // from the unit's NetworkId (assigned in lockstep order).
                    float3 slotTarget = rallyTarget;
                    if (em.HasComponent<NetworkedEntity>(unit))
                    {
                        uint id = (uint)em.GetComponentData<NetworkedEntity>(unit).NetworkId;
                        // Golden-angle spiral: ~2.4 rad per step, radius grows
                        // every full turn — 1.5 m ring spacing matches the
                        // steering lattice (SeparationRadius).
                        float angle = id * 2.39996f;
                        float radius = 1.5f + 1.5f * ((id % 9u) / 3u); // 1.5 / 3.0 / 4.5
                        slotTarget += new float3(
                            math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                    }

                    if (!em.HasComponent<DesiredDestination>(unit))
                        em.AddComponentData(unit, new DesiredDestination { Position = slotTarget, Has = 1 });
                        else
                            em.SetComponentData(unit, new DesiredDestination { Position = slotTarget, Has = 1 });

                    if (!em.HasComponent<GuardPoint>(unit))
                        em.AddComponentData(unit, new GuardPoint { Position = slotTarget, Has = 1 });
                        else
                            em.SetComponentData(unit, new GuardPoint { Position = slotTarget, Has = 1 });
                }
            }

        }

    }
}