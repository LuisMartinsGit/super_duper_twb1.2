// AIBuildingManager.cs
// Controls builders, processes build requests, manages construction
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    [DisableAutoCreation] // Replaced by SimpleAISystem.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIEconomyManager))]
    public partial struct AIBuildingManager : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Fix #244: in multiplayer, only the host runs AI. Clients receive
            // AI commands via lockstep replay. Without this gate, both peers
            // run AI independently with different ElapsedTime clocks, causing
            // immediate desync at tick 0.
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (brain, buildingState, buildReqs, entity)
                     in SystemAPI.Query<RefRO<AIBrain>, RefRW<AIBuildingState>, DynamicBuffer<BuildRequest>>()
                                  .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var state_val = buildingState.ValueRW;

                if (time >= state_val.LastBuildCheck + state_val.BuildCheckInterval)
                {
                    state_val.LastBuildCheck = time;
                    ManageBuilders(ref state, brain.ValueRO.Owner, ref state_val, ecb);
                    QueueCultureBuildings(ref state, brain.ValueRO.Owner, buildReqs);
                }

                ProcessBuildRequests(ref state, brain.ValueRO.Owner, buildReqs, ecb);

                state_val.QueuedConstructions = 0;
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].Assigned == 0)
                        state_val.QueuedConstructions++;
                }

                buildingState.ValueRW = state_val;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        private void ManageBuilders(ref SystemState state, Faction faction,
            ref AIBuildingState buildingState, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;

            int builderCount = 0;
            foreach (var (canBuild, factionTag) in SystemAPI.Query<RefRO<CanBuild>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == faction && canBuild.ValueRO.Value)
                    builderCount++;
            }

            buildingState.ActiveBuilders = builderCount;

            int queueSize = buildingState.QueuedConstructions;
            if (queueSize > 3)
                buildingState.DesiredBuilders = math.min(AITuning.MaxBuilders, AITuning.TargetBuilders + 1);
                buildingState.DesiredBuilders = AITuning.TargetBuilders;

            int queuedBuilders = CountQueuedBuilders(ref state, faction);
            int totalBuilders = builderCount + queuedBuilders;

            AILogger.Log(faction, "BUILDING",
                $"Builders: {builderCount} active, {queuedBuilders} queued, {totalBuilders}/{buildingState.DesiredBuilders} desired. Pending builds: {queueSize}");

            if (totalBuilders < buildingState.DesiredBuilders)
            {
                int needed = buildingState.DesiredBuilders - totalBuilders;
                AILogger.Log(faction, "BUILDING", $"Requesting {needed} more builders");
                RequestBuilders(ref state, faction, needed);
            }

            AssignIdleBuildersToTasks(ref state, faction, ecb);
        }

        private int CountQueuedBuilders(ref SystemState state, Faction faction)
        {
            int count = 0;

            foreach (var (brain, recruitReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<RecruitmentRequest>>())
            {
                if (brain.ValueRO.Owner != faction) continue;

                for (int i = 0; i < recruitReqs.Length; i++)
                {
                    if (recruitReqs[i].UnitType == UnitClass.Economy)
                        count += recruitReqs[i].Quantity;
                }
            }

            foreach (var (factionTag, trainQueue) in SystemAPI.Query<RefRO<FactionTag>, DynamicBuffer<TrainQueueItem>>())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                for (int i = 0; i < trainQueue.Length; i++)
                {
                    if (trainQueue[i].UnitId == "Builder")
                        count++;
                }
            }

            return count;
        }

        private void ProcessBuildRequests(ref SystemState state, Faction faction,
            DynamicBuffer<BuildRequest> buildReqs, EntityCommandBuffer ecb)
        {
            if (buildReqs.Length == 0) return;

            var em = state.EntityManager;

            for (int i = 0; i < buildReqs.Length; i++)
            {
                var req = buildReqs[i];
                if (req.Assigned == 1) continue;

                if (!CanAffordBuilding(ref state, faction, req.BuildingType))
                    continue;

                Entity builder = FindAvailableBuilder(ref state, faction);
                if (builder == Entity.Null)
                    continue;

                AILogger.Log(faction, "BUILDING", $"  Starting construction: {req.BuildingType} pri={req.Priority} at ({req.DesiredPosition.x:F0},{req.DesiredPosition.z:F0})");
                StartConstruction(ref state, faction, req, builder, ecb);

                req.Assigned = 1;
                req.AssignedBuilder = builder;
                buildReqs[i] = req;
            }

            for (int i = buildReqs.Length - 1; i >= 0; i--)
            {
                var req = buildReqs[i];
                if (req.Assigned == 1 && !em.Exists(req.AssignedBuilder))
                {
                    AILogger.Log(faction, "BUILDING", $"  Removing completed/orphaned request: {req.BuildingType}");
                    buildReqs.RemoveAt(i);
                }
            }
        }

        private bool CanAffordBuilding(ref SystemState state, Faction faction, FixedString64Bytes buildingType)
        {
            foreach (var (factionTag, resources) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionResources>>())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                if (TechTreeDB.Instance != null &&
                    TechTreeDB.Instance.TryGetBuilding(buildingType.ToString(), out var buildingDef))
                {
                    return resources.ValueRO.Supplies >= buildingDef.cost.Supplies &&
                           resources.ValueRO.Iron >= buildingDef.cost.Iron &&
                           resources.ValueRO.Crystal >= buildingDef.cost.Crystal;
                }

                // TechTreeDB unavailable — fall back to static BuildCosts table
                if (BuildCosts.TryGet(buildingType.ToString(), out var fallbackCost))
                {
                    return resources.ValueRO.Supplies >= fallbackCost.Supplies &&
                           resources.ValueRO.Iron >= fallbackCost.Iron &&
                           resources.ValueRO.Crystal >= fallbackCost.Crystal;
                }

                // Unknown building — deny construction
                return false;
            }

            return false;
        }

        private Entity FindAvailableBuilder(ref SystemState state, Faction faction)
        {
            foreach (var (canBuild, factionTag, entity) in
                     SystemAPI.Query<RefRO<CanBuild>, RefRO<FactionTag>>()
                              .WithNone<BuildOrder>()
                              .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction && canBuild.ValueRO.Value)
                    return entity;
            }

            return Entity.Null;
        }

        private void StartConstruction(ref SystemState state, Faction faction, BuildRequest req,
            Entity builder, EntityCommandBuffer ecb)
        {
            DeductBuildingCost(ref state, faction, req.BuildingType);

            Entity buildingEntity = CreateBuilding(ref state, faction, req.BuildingType, req.DesiredPosition, ecb);

            if (buildingEntity == Entity.Null)
                return;

            ecb.AddComponent(builder, new BuildOrder { Site = buildingEntity });
        }

        private void DeductBuildingCost(ref SystemState state, Faction faction, FixedString64Bytes buildingType)
        {
            foreach (var (factionTag, resources, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRW<FactionResources>>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                var res = resources.ValueRW;

                if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding(buildingType.ToString(), out var buildingDef))
                {
                    res.Supplies -= buildingDef.cost.Supplies;
                    res.Iron -= buildingDef.cost.Iron;
                    res.Crystal -= buildingDef.cost.Crystal;
                    res.Veilsteel -= buildingDef.cost.Veilsteel;
                    res.Glow -= buildingDef.cost.Glow;
                }
                else if (BuildCosts.TryGet(buildingType.ToString(), out var fallbackCost))
                {
                    res.Supplies -= fallbackCost.Supplies;
                    res.Iron -= fallbackCost.Iron;
                    res.Crystal -= fallbackCost.Crystal;
                    res.Veilsteel -= fallbackCost.Veilsteel;
                    res.Glow -= fallbackCost.Glow;
                }

                break;
            }
        }

        private Entity CreateBuilding(ref SystemState state, Faction faction,
            FixedString64Bytes buildingType, float3 position, EntityCommandBuffer ecb)
        {
            // Snap to grid (rect-aware for even/odd building dimensions)
            var snapGrid = PassabilityGrid.Instance;
            if (snapGrid != null)
            {
                var buildSize = BuildingSizeConfig.GetSize(buildingType.ToString());
                position = snapGrid.SnapToGridRect(position, buildSize);
            }

            // Delegate to BuildingFactory — single source of truth for building creation.
            // The factory handles stats from TechTreeDB, building-specific components,
            // and all Era 1 + culture buildings.
            var buildingId = buildingType.ToString();
            Entity building = BuildingFactory.Create(ecb, buildingId, position, faction);

            if (building == Entity.Null)
                return Entity.Null;

            // Mark as under construction so builders must complete it
            // (mirrors the player path in BuilderCommandPanel.SpawnSelectedBuilding)
            float buildTime = GetBuildTime(buildingId);
            ecb.AddComponent(building, new UnderConstruction { Progress = 0f, Total = buildTime });

            // Set initial scale to 0.3f so buildings don't appear full-size before first construction tick
            ecb.SetComponent(building, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 0.3f));

            // Set HP to 1 during construction (restored to max on completion)
            int maxHP = GetDefaultMaxHP(buildingId);
            ecb.SetComponent(building, new Health { Value = 1, Max = maxHP });

            return building;
        }

        /// <summary>
        /// Returns the build time for a building type.
        /// Values match those in BuilderCommandPanel.SpawnSelectedBuilding.
        /// </summary>
        private static float GetBuildTime(string buildingId)
        {
            return buildingId switch
            {
                "Hall"                   => 30f,
                "Hut"                    => 15f,
                "GatherersHut"           => 20f,
                "Barracks"               => 30f,
                "ShrineOfAhridan"        => 40f,
                "TempleOfRidan"          => 50f,
                "VaultOfAlmierra"        => 40f,
                "FiendstoneKeep"         => 40f,
                "Alanthor_Smelter"       => 30f,
                "Runai_Outpost"          => 25f,
                "Runai_TradeHub"         => 30f,
                "ThessarasBazaar"        => 40f,
                "Runai_SiegeWorkshop"    => 35f,
                "Alanthor_Tower"         => 25f,
                "Alanthor_Garrison"      => 30f,
                "Alanthor_Stable"        => 35f,
                "Alanthor_SiegeYard"     => 35f,
                "Feraldis_HuntingLodge"  => 25f,
                "Feraldis_LoggingStation" => 25f,
                "Feraldis_Longhouse"     => 30f,
                "Feraldis_Tower"         => 25f,
                "Feraldis_SiegeYard"     => 35f,
                _                        => 25f  // Reasonable default
            };
        }

        /// <summary>
        /// Returns the default max HP for a building type.
        /// Uses TechTreeDB when available, otherwise falls back to hardcoded defaults
        /// matching the values in BuildingFactory.
        /// </summary>
        private static int GetDefaultMaxHP(string buildingId)
        {
            // Prefer TechTreeDB (authoritative source)
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding(buildingId, out var def))
            {
                if (def.hp > 0) return (int)def.hp;
            }

            // Fallback defaults matching BuildingFactory hardcoded values
            return buildingId switch
            {
                "Hall"                   => 2400,
                "Hut"                    => 600,
                "GatherersHut"           => 800,
                "Barracks"               => 600,
                "ShrineOfAhridan"        => 800,
                "TempleOfRidan"          => 1500,
                "VaultOfAlmierra"        => 1200,
                "FiendstoneKeep"         => 2000,
                "Alanthor_Smelter"       => 1000,
                "Runai_Outpost"          => 900,
                "Runai_TradeHub"         => 1200,
                "ThessarasBazaar"        => 2700,
                "Runai_SiegeWorkshop"    => 1100,
                "Alanthor_Tower"         => 950,
                "Alanthor_Garrison"      => 1500,
                "Alanthor_Stable"        => 1300,
                "Alanthor_SiegeYard"     => 1100,
                "Feraldis_HuntingLodge"  => 1000,
                "Feraldis_LoggingStation" => 1000,
                "Feraldis_Longhouse"     => 1400,
                "Feraldis_Tower"         => 900,
                "Feraldis_SiegeYard"     => 1200,
                _                        => 1000  // Reasonable default
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ERA 2+ CULTURE BUILDING LOGIC
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Culture-specific build orders. Each entry is queued in priority order;
        /// buildings that already exist or are pending in the queue are skipped.
        /// Duplicate entries allow the AI to build multiple copies (e.g., towers).
        /// </summary>
        private static readonly string[] RunaiBuildOrder = {
            "Runai_Outpost", "Runai_TradeHub", "ThessarasBazaar",
            "Runai_SiegeWorkshop"
        };
        private static readonly string[] AlanthorBuildOrder = {
            "KingsCourt", "Alanthor_Garrison", "Alanthor_Tower",
            "Alanthor_Stable", "Alanthor_SiegeYard", "Alanthor_Tower"
        };
        private static readonly string[] FeraldisBuildOrder = {
            "Feraldis_Longhouse", "Feraldis_HuntingLodge", "Feraldis_Tower",
            "Feraldis_Foundry", "Feraldis_SiegeYard", "Feraldis_Tower"
        };

        /// <summary>
        /// After age-up to Era 2+, queue culture-specific buildings one at a time.
        /// Skips buildings that already exist, are under construction, or are
        /// pending in the build queue. Only queues the next needed building per
        /// tick to avoid overwhelming builders.
        /// </summary>
        private void QueueCultureBuildings(ref SystemState state, Faction faction,
            DynamicBuffer<BuildRequest> buildReqs)
        {
            var em = state.EntityManager;

            // 1. Check era — must be era 2+
            int era = 1;
            if (FactionEconomy.TryGetBank(em, faction, out var bankEntity) &&
                em.HasComponent<FactionEra>(bankEntity))
            {
                era = em.GetComponentData<FactionEra>(bankEntity).Value;
            }
            if (era < 2) return;

            // 2. Read faction culture from FactionProgress on the Hall entity
            byte culture = Cultures.None;
            foreach (var (fTag, progress) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionProgress>>()
                .WithAll<HallTag>())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    culture = progress.ValueRO.Culture;
                    break;
                }
            }
            if (culture == Cultures.None) return;

            // 3. Select culture-specific build order
            string[] buildOrder = culture switch
            {
                Cultures.Runai => RunaiBuildOrder,
                Cultures.Alanthor => AlanthorBuildOrder,
                Cultures.Feraldis => FeraldisBuildOrder,
                _ => null
            };
            if (buildOrder == null) return;

            // 4. Find the Hall position for build placement
            float3 hallPos = float3.zero;
            bool foundHall = false;
            foreach (var (fTag, lt) in SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<HallTag>()
                .WithNone<UnderConstruction>())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    hallPos = lt.ValueRO.Position;
                    foundHall = true;
                    break;
                }
            }
            if (!foundHall) return;

            // 5. Walk the build order and queue the first needed building
            foreach (string buildingId in buildOrder)
            {
                // Skip if already pending in build queue
                bool alreadyPending = false;
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].BuildingType.Equals(buildingId) && buildReqs[i].Assigned == 0)
                    {
                        alreadyPending = true;
                        break;
                    }
                }
                if (alreadyPending) continue;

                // Count existing instances (built + under construction)
                int existingCount = CountFactionBuildings(ref state, faction, buildingId);

                // Allow duplicates for buildings that appear multiple times in the order
                int targetCount = 0;
                foreach (string b in buildOrder)
                {
                    if (b == buildingId) targetCount++;
                }
                if (existingCount >= targetCount) continue;

                // Check affordability via CanAffordBuilding (uses TechTreeDB + BuildCosts fallback)
                FixedString64Bytes buildingTypeFixed = new FixedString64Bytes(buildingId);
                if (!CanAffordBuilding(ref state, faction, buildingTypeFixed))
                    continue;

                // Calculate build position: random offset 15-25 units from Hall.
                // Fix #230: guard against seed 0 (produces all-zero sequence).
                uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction * 53 + existingCount * 17);
                if (seed == 0) seed = 1;
                var random = new Unity.Mathematics.Random(seed);
                float angle = random.NextFloat(0, math.PI * 2);
                float distance = random.NextFloat(15f, 25f);
                float3 buildPos = hallPos + new float3(
                    math.cos(angle) * distance,
                    0,
                    math.sin(angle) * distance);

                buildReqs.Add(new BuildRequest
                {
                    BuildingType = buildingTypeFixed,
                    DesiredPosition = buildPos,
                    Priority = 5,
                    Assigned = 0,
                    AssignedBuilder = Entity.Null
                });

                AILogger.Log(faction, "BUILDING",
                    $"Era 2: Queued culture building {buildingId} ({existingCount}/{targetCount} existing)");

                // Only queue one culture building per tick to avoid overwhelming builders
                return;
            }
        }

        /// <summary>
        /// Count how many instances of a building type a faction owns (built + under construction).
        /// Uses PresentationId to match building type via BuildingFactory.
        /// </summary>
        private int CountFactionBuildings(ref SystemState state, Faction faction, string buildingId)
        {
            int count = 0;
            int pid = BuildingFactory.GetPresentationId(buildingId);

            foreach (var (fTag, presId) in SystemAPI.Query<RefRO<FactionTag>, RefRO<PresentationId>>()
                .WithAll<BuildingTag>())
            {
                if (fTag.ValueRO.Value == faction && presId.ValueRO.Id == pid)
                    count++;
            }

            return count;
        }

        private void AssignIdleBuildersToTasks(ref SystemState state, Faction faction, EntityCommandBuffer ecb)
        {
            var idleBuilders = new NativeList<Entity>(Allocator.Temp);

            foreach (var (canBuild, factionTag, entity) in
                     SystemAPI.Query<RefRO<CanBuild>, RefRO<FactionTag>>()
                              .WithNone<BuildOrder>()
                              .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction && canBuild.ValueRO.Value)
                    idleBuilders.Add(entity);
            }

            if (idleBuilders.Length > 0)
                AILogger.Log(faction, "BUILDING", $"Assigning {idleBuilders.Length} idle builders to unfinished structures");

            foreach (var (underConstruction, factionTag, entity) in
                     SystemAPI.Query<RefRO<UnderConstruction>, RefRO<FactionTag>>()
                              .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                if (idleBuilders.Length == 0) break;

                bool hasBuilder = false;
                foreach (var (buildOrder, _) in SystemAPI.Query<RefRO<BuildOrder>, RefRO<FactionTag>>())
                {
                    if (buildOrder.ValueRO.Site == entity)
                    {
                        hasBuilder = true;
                        break;
                    }
                }

                if (!hasBuilder)
                {
                    Entity builder = idleBuilders[0];
                    idleBuilders.RemoveAt(0);

                    ecb.AddComponent(builder, new BuildOrder { Site = entity });
                    AILogger.Log(faction, "BUILDING", $"  Assigned idle builder to unfinished structure");
                }
            }

            idleBuilders.Dispose();
        }

        private void RequestBuilders(ref SystemState state, Faction faction, int count)
        {
            foreach (var (brain, recruitReqs, entity) in
                     SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<RecruitmentRequest>>()
                              .WithEntityAccess())
            {
                if (brain.ValueRO.Owner != faction) continue;

                recruitReqs.Add(new RecruitmentRequest
                {
                    UnitType = UnitClass.Economy,
                    Quantity = count,
                    Priority = 7,
                    RequestingManager = entity
                });

                break;
            }
        }
    }
}