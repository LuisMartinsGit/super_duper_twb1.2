// AIEconomyManager.cs
// Manages AI economy: miners, gatherers huts, resource allocation
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;

namespace TheWaningBorder.AI
{
    [DisableAutoCreation] // Replaced by SimpleAISystem.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Unity.Transforms.TransformSystemGroup))]
    public partial struct AIEconomyManager : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        // Fix #215: deferred gather assignments. GatherCommandHelper.Execute
        // performs structural changes (ClearAllCommands + SetupGather), which
        // is unsafe while the outer OnUpdate foreach is still iterating faction
        // brains. We collect the assignments during iteration and drain them
        // after the foreach completes.
        private struct DeferredGather
        {
            public Entity Miner;
            public Entity Target;
            public Entity Dropoff;
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
            var deferredGathers = new NativeList<DeferredGather>(Allocator.Temp);

            foreach (var (brain, economyState, stratState, resourceReqs, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AIEconomyState>, RefRO<AIStrategyState>, DynamicBuffer<ResourceRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var economy = economyState.ValueRW;
                Faction faction = brain.ValueRO.Owner;
                var strategy = stratState.ValueRO.Current;

                // Strategy adjusts economy targets
                economy.DesiredGatherersHuts = strategy switch
                {
                    AIStrategy.Rush => 2,       // Minimal economy
                    AIStrategy.EcoBoom => 6,    // Heavy economy
                    AIStrategy.TechRush => 3,   // Moderate
                    AIStrategy.Aggressive => 4, // Balanced
                    AIStrategy.Defensive => 4,  // Stable
                    _ => AITuning.TargetGatherersHuts
                };
                economy.DesiredMiners = strategy switch
                {
                    AIStrategy.Rush => 2,
                    AIStrategy.EcoBoom => math.min(8, AITuning.MaxMiners),
                    AIStrategy.TechRush => 4,
                    AIStrategy.Aggressive => 5,
                    AIStrategy.Defensive => 5,
                    _ => 4
                };

                CheckEconomyNeeds(ref state, faction, ref economy);

                // === BUILD ORDER ===
                // 1. GathererHuts FIRST — always build up to target (don't wait for low supplies)
                if (economy.ActiveGatherersHuts < economy.DesiredGatherersHuts)
                {
                    AILogger.Log(faction, "ECONOMY", $"Need more GathererHuts ({economy.ActiveGatherersHuts} < {economy.DesiredGatherersHuts}), requesting build");
                    RequestGatherersHut(ref state, faction, ecb);
                }

                // 2. Scouts — handled by AIScoutingBehavior (trains from Hall)

                // 3. Miners — only after at least 1 GathererHut is being built/completed
                if (time >= economy.LastMineAssignmentCheck + economy.MineCheckInterval)
                {
                    economy.LastMineAssignmentCheck = time;

                    // Log resource snapshot on mine check interval (not every frame)
                    foreach (var (fTag, res) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionResources>>())
                    {
                        if (fTag.ValueRO.Value == faction)
                        {
                            AILogger.Log(faction, "ECONOMY",
                                $"Resources — Supplies:{res.ValueRO.Supplies} Iron:{res.ValueRO.Iron} Crystal:{res.ValueRO.Crystal} Veilsteel:{res.ValueRO.Veilsteel}");
                            break;
                        }
                    }

                    AILogger.Log(faction, "ECONOMY",
                        $"GathererHuts: {economy.ActiveGatherersHuts}/{economy.DesiredGatherersHuts}, Miners: {economy.AssignedMiners}/{economy.DesiredMiners}");

                    bool hasGathererHut = false;
                    foreach (var (ghTag, fTag) in SystemAPI.Query<RefRO<GathererHutTag>, RefRO<FactionTag>>())
                    {
                        if (fTag.ValueRO.Value == faction) { hasGathererHut = true; break; }
                    }

                    if (hasGathererHut)
                    {
                        UpdateMineAssignments(ref state, faction, ref economy, ecb);
                    }
                    else
                    {
                        AILogger.Log(faction, "ECONOMY", "Skipping miner check — no GathererHut exists yet");
                    }

                    // Crystal cadaver mining — send idle miners to harvest crystal from corpses
                    AssignMinersToCadavers(ref state, faction, deferredGathers);
                }

                // 4-7. Barracks, military, crystal hunting — handled by AIMilitaryManager + AICrystalHuntBehavior

                // 8. Choice building (requires crystal from killed crystallings)
                CheckChoiceBuildingNeeds(ref state, brain.ValueRO, ecb);

                // 9. Age up (requires completed choice building + resources)
                CheckAgeUp(ref state, brain.ValueRO, strategy, ecb);

                // 9b. Queue culture-specific buildings after age-up
                QueueCultureBuildings(ref state, brain.ValueRO, ecb);

                // 10. Vault management — deposit surplus resources for interest (Alanthor)
                if (time >= economy.LastVaultCheck + AITuning.VaultCheckInterval)
                {
                    economy.LastVaultCheck = time;
                    ManageVaults(ref state, faction);
                }

                // 11. Smelter management — assign idle miners to supply forges (Alanthor)
                if (time >= economy.LastSmelterCheck + AITuning.SmelterCheckInterval)
                {
                    economy.LastSmelterCheck = time;
                    ManageSmelters(ref state, faction, ecb);
                }

                ProcessResourceRequests(ref state, faction, resourceReqs);

                // CRITICAL: Write back the modified economy state
                // Without this, LastMineAssignmentCheck etc. revert to 0 every frame
                economyState.ValueRW = economy;
            }

            ecb.Playback(em);
            ecb.Dispose();

            // Drain deferred gather assignments after the outer SystemAPI
            // iteration has fully closed. GatherCommandHelper.Execute performs
            // structural changes, so it must run post-iteration (see #215).
            for (int i = 0; i < deferredGathers.Length; i++)
            {
                var g = deferredGathers[i];
                GatherCommandHelper.Execute(em, g.Miner, g.Target, g.Dropoff);
            }
            deferredGathers.Dispose();
        }

        private void UpdateMineAssignments(ref SystemState state, Faction faction,
            ref AIEconomyState economy, EntityCommandBuffer ecb)
        {
            // Count iron deposits on the map
            int mineCount = 0;
            foreach (var ironTag in SystemAPI.Query<RefRO<IronMineTag>>())
                mineCount++;

            // Count EXISTING miners belonging to this faction
            int currentMiners = 0;
            foreach (var (minerTag, factionTag) in SystemAPI.Query<RefRO<MinerTag>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == faction)
                    currentMiners++;
            }

            // Count miners in training queue (not yet spawned)
            int queuedMiners = 0;
            foreach (var (factionTag, trainQueue) in SystemAPI.Query<RefRO<FactionTag>, DynamicBuffer<TrainQueueItem>>())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                for (int i = 0; i < trainQueue.Length; i++)
                {
                    if (trainQueue[i].UnitId.Equals("Miner"))
                        queuedMiners++;
                }
            }

            // Count pending miner recruitment requests
            foreach (var (brainComp, recruitReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<RecruitmentRequest>>())
            {
                if (brainComp.ValueRO.Owner != faction) continue;
                for (int i = 0; i < recruitReqs.Length; i++)
                {
                    if (recruitReqs[i].UnitType == UnitClass.Miner)
                        queuedMiners += recruitReqs[i].Quantity;
                }
                break;
            }

            economy.AssignedMiners = currentMiners;
            economy.DesiredMiners = math.min(mineCount * AITuning.TargetMinersPerMine, AITuning.MaxMiners);

            // MiningSystem auto-finds deposits for AI miners when they're idle,
            // so we just need to ensure enough miners are trained.
            int totalMiners = currentMiners + queuedMiners;
            AILogger.Log(faction, "ECONOMY",
                $"Mine check — deposits:{mineCount}, currentMiners:{currentMiners}, queuedMiners:{queuedMiners}, totalMiners:{totalMiners}, desired:{economy.DesiredMiners}");

            if (totalMiners < economy.DesiredMiners)
            {
                int minersNeeded = math.min(economy.DesiredMiners - totalMiners, 3);
                AILogger.Log(faction, "ECONOMY", $"Requesting {minersNeeded} more miners");
                RequestMiners(ref state, faction, minersNeeded, ecb);
            }
        }

        private void CheckEconomyNeeds(ref SystemState state, Faction faction, ref AIEconomyState economy)
        {
            foreach (var (factionTag, resources) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionResources>>())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                economy.NeedsMoreSupplyIncome = resources.ValueRO.Supplies < AITuning.MinSuppliesThreshold ? (byte)1 : (byte)0;
                economy.NeedsMoreIronIncome = resources.ValueRO.Iron < 100 ? (byte)1 : (byte)0;

                // Always want GathererHuts — keep at base target in early game
                // Don't escalate to 5 early: that drains supplies needed for miners/barracks
                economy.DesiredGatherersHuts = AITuning.TargetGatherersHuts;

                break;
            }

            // Count existing GathererHuts (includes under construction)
            economy.ActiveGatherersHuts = 0;
            foreach (var (gathererTag, factionTag) in SystemAPI.Query<RefRO<GathererHutTag>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == faction)
                    economy.ActiveGatherersHuts++;
            }

            // Also count pending (unassigned) GathererHut build requests so we don't spam
            foreach (var (brainComp, buildReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>())
            {
                if (brainComp.ValueRO.Owner != faction) continue;
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].BuildingType.Equals("GatherersHut") && buildReqs[i].Assigned == 0)
                        economy.ActiveGatherersHuts++;
                }
                break;
            }
        }

        private void RequestGatherersHut(ref SystemState state, Faction faction, EntityCommandBuffer ecb)
        {
            foreach (var (brain, buildReqs, entity) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.Owner != faction) continue;

                // Don't queue another if there's already a pending GatherersHut request
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].BuildingType.Equals("GatherersHut") && buildReqs[i].Assigned == 0)
                    {
                        AILogger.Log(faction, "ECONOMY", "GathererHut request skipped — already pending in build queue");
                        return;
                    }
                }

                float3 buildLocation = FindGatherersHutLocation(ref state, faction);

                buildReqs.Add(new BuildRequest
                {
                    BuildingType = "GatherersHut",
                    DesiredPosition = buildLocation,
                    Priority = 8,
                    Assigned = 0,
                    AssignedBuilder = Entity.Null
                });

                AILogger.Log(faction, "ECONOMY", $"Queued GathererHut build request at ({buildLocation.x:F0},{buildLocation.z:F0})");
                break;
            }
        }

        private void RequestMiners(ref SystemState state, Faction faction, int count, EntityCommandBuffer ecb)
        {
            foreach (var (brain, recruitReqs, entity) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<RecruitmentRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.Owner != faction) continue;

                recruitReqs.Add(new RecruitmentRequest
                {
                    UnitType = UnitClass.Miner,
                    Quantity = count,
                    Priority = 7, // Same as builders — don't starve builder training
                    RequestingManager = entity
                });

                break;
            }
        }

        private void ProcessResourceRequests(ref SystemState state, Faction faction,
            DynamicBuffer<ResourceRequest> requests)
        {
            if (requests.Length == 0) return;

            var em = state.EntityManager;

            FactionResources availableResources = default;
            Entity bankEntity = Entity.Null;

            foreach (var (factionTag, resources, entity) in SystemAPI.Query<RefRO<FactionTag>, RefRW<FactionResources>>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction)
                {
                    availableResources = resources.ValueRO;
                    bankEntity = entity;
                    break;
                }
            }

            if (bankEntity == Entity.Null) return;

            var sortedRequests = new NativeList<ResourceRequest>(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
                sortedRequests.Add(requests[i]);
            sortedRequests.Sort(new ResourceRequestComparer());

            for (int i = 0; i < sortedRequests.Length; i++)
            {
                var req = sortedRequests[i];
                if (req.Approved == 1) continue;

                if (availableResources.Supplies >= req.Supplies &&
                    availableResources.Iron >= req.Iron &&
                    availableResources.Crystal >= req.Crystal)
                {
                    availableResources.Supplies -= req.Supplies;
                    availableResources.Iron -= req.Iron;
                    availableResources.Crystal -= req.Crystal;

                    for (int j = 0; j < requests.Length; j++)
                    {
                        if (requests[j].Requester == req.Requester && requests[j].Priority == req.Priority)
                        {
                            req.Approved = 1;
                            requests[j] = req;
                            break;
                        }
                    }
                }
            }

            em.SetComponentData(bankEntity, availableResources);
            sortedRequests.Dispose();
        }

        private void CheckChoiceBuildingNeeds(ref SystemState state, AIBrain brain, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;
            Faction faction = brain.Owner;

            // Check if faction already has a choice building
            string existing = BuildingFactory.GetFactionChoiceBuilding(em, faction);
            if (existing != null) return;

            // Check if faction has enough crystal
            int crystalAmount = 0;
            foreach (var (factionTag, resources) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionResources>>())
            {
                if (factionTag.ValueRO.Value == faction)
                {
                    crystalAmount = resources.ValueRO.Crystal;
                    break;
                }
            }

            if (crystalAmount < AITuning.CrystalForChoiceBuilding) return;

            // Check no pending choice building request
            foreach (var (brainComp, buildReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>())
            {
                if (brainComp.ValueRO.Owner != faction) continue;

                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (BuildingFactory.IsChoiceBuilding(buildReqs[i].BuildingType.ToString()))
                        return; // Already pending
                }

                break;
            }

            AILogger.Log(faction, "ECONOMY", $"Have {crystalAmount} crystal — requesting choice building");

            // Pick choice building based on personality
            string chosenBuilding = brain.Personality switch
            {
                AIPersonality.Aggressive => "FiendstoneKeep",
                AIPersonality.Rush => "FiendstoneKeep",
                AIPersonality.Defensive => "ShrineOfAhridan",
                AIPersonality.Economic => "VaultOfAlmierra",
                _ => "VaultOfAlmierra" // Balanced
            };

            RequestChoiceBuilding(ref state, faction, chosenBuilding);
        }

        private void RequestChoiceBuilding(ref SystemState state, Faction faction, string buildingId)
        {
            foreach (var (brain, buildReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>())
            {
                if (brain.ValueRO.Owner != faction) continue;

                float3 buildLocation = FindBuildLocation(ref state, faction, buildingId);

                buildReqs.Add(new BuildRequest
                {
                    BuildingType = buildingId,
                    DesiredPosition = buildLocation,
                    Priority = 4,
                    Assigned = 0,
                    AssignedBuilder = Entity.Null
                });

                break;
            }
        }

        /// <summary>
        /// Checks if the AI faction should age-up to Era 2.
        /// Requires: a completed choice building, enough resources, and not already aged-up.
        /// Uses ECB for structural changes to avoid invalidating iterators.
        /// </summary>
        private void CheckAgeUp(ref SystemState state, AIBrain brain, AIStrategy strategy, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;
            Faction faction = brain.Owner;

            // Find the Hall for this faction (need FactionProgress)
            Entity hallEntity = Entity.Null;
            byte currentCulture = Cultures.None;

            foreach (var (factionTag, progress, buildingTag, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionProgress>, RefRO<BuildingTag>>()
                .WithAll<HallTag>()
                .WithNone<UnderConstruction, AgeUpState>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction)
                {
                    hallEntity = entity;
                    currentCulture = progress.ValueRO.Culture;
                    break;
                }
            }

            if (hallEntity == Entity.Null) return;
            if (currentCulture != Cultures.None) return; // Already aged-up

            // Check for a COMPLETED choice building (not under construction)
            bool hasChoiceBuilding = false;
            foreach (var (choiceTag, factionTag, entity) in
                SystemAPI.Query<RefRO<ChoiceBuildingTag>, RefRO<FactionTag>>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction)
                {
                    hasChoiceBuilding = true;
                    break;
                }
            }

            if (!hasChoiceBuilding) return;

            AILogger.Log(faction, "ECONOMY", "Choice building complete — checking age-up affordability");

            // Check affordability
            if (!FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost))
            {
                AILogger.Log(faction, "ECONOMY", "Age-up blocked: can't afford age-up cost");
                return;
            }

            // Spend resources
            if (!FactionEconomy.Spend(em, faction, CultureConfig.AgeUpCost)) return;

            // Choose culture based on current strategy (not personality)
            byte culture = strategy switch
            {
                AIStrategy.Rush => Cultures.Feraldis,        // Fast training, blood totems
                AIStrategy.EcoBoom => Cultures.Runai,        // Trade routes, income scaling
                AIStrategy.TechRush => Cultures.Alanthor,    // Smelters, walls protect tech
                AIStrategy.Aggressive => Cultures.Feraldis,  // Totems buff army
                AIStrategy.Defensive => Cultures.Alanthor,   // Walls, strong defense
                _ => Cultures.Runai
            };

            AILogger.Log(brain.Owner, "STRATEGY",
                $"Age-up culture selection: {strategy} → culture {culture}");

            // Add AgeUpState timer to the Hall — completion handled by AgeUpSystem
            float duration = CultureConfig.AgeUpDuration;
            ecb.AddComponent(hallEntity, new AgeUpState
            {
                Culture = culture,
                Duration = duration,
                Remaining = duration
            });

            // Register culture with FactionColors so other systems can query it
            FactionColors.SetFactionCulture(faction, culture);

            AILogger.Log(faction, "ECONOMY", $"=== STARTED AGE-UP to Era 2 — culture: {CultureConfig.GetName(culture)} ({duration}s) ===");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ERA 2 CULTURE BUILDING EXPANSION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Alanthor-specific wall expansion, gated on era+culture. The generic
        /// culture-building queuing used to live here too, but it was a verbatim
        /// duplicate of AIBuildingManager.QueueCultureBuildings and the two
        /// would fight over the build queue — see #214. The Alanthor wall
        /// logic stays here because it is unique to this system.
        /// </summary>
        private void QueueCultureBuildings(ref SystemState state, AIBrain brain, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;
            Faction faction = brain.Owner;

            // Read faction culture from the Hall's FactionProgress
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

            if (culture != Cultures.Alanthor) return; // Only Alanthor builds walls here

            // Check era — must be era 2+
            int era = 1;
            if (FactionEconomy.TryGetBank(em, faction, out var bankEntity) &&
                em.HasComponent<FactionEra>(bankEntity))
            {
                era = em.GetComponentData<FactionEra>(bankEntity).Value;
            }
            if (era < 2) return;

            // Find brain entity for build queue access
            DynamicBuffer<BuildRequest> buildReqs = default;
            bool foundBrain = false;
            foreach (var (brainComp, reqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>())
            {
                if (brainComp.ValueRO.Owner == faction)
                {
                    buildReqs = reqs;
                    foundBrain = true;
                    break;
                }
            }
            if (!foundBrain) return;

            BuildAlanthorWalls(ref state, faction, buildReqs);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ALANTHOR WALL PLACEMENT
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Number of wall hubs to place in a defensive perimeter.</summary>
        private const int WallPerimeterHubs = 6;
        /// <summary>Distance from Hall for the wall perimeter.</summary>
        private const float WallPerimeterRadius = 18f;

        /// <summary>
        /// Alanthor AI: build a defensive wall perimeter around the base.
        /// Places hubs in a hexagonal ring around the Hall, then connects them with segments.
        /// Only queues one hub at a time; segments are created automatically when adjacent hubs exist.
        /// </summary>
        private void BuildAlanthorWalls(ref SystemState state, Faction faction,
            DynamicBuffer<BuildRequest> buildReqs)
        {
            var em = state.EntityManager;

            // Don't queue walls if there's already a pending wall request
            for (int i = 0; i < buildReqs.Length; i++)
            {
                if (buildReqs[i].BuildingType.Equals("Alanthor_Wall") && buildReqs[i].Assigned == 0)
                    return;
            }

            // Find Hall position (base center)
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

            // Count existing wall hubs
            int existingHubs = 0;
            var hubPositions = new Unity.Collections.NativeList<float3>(Allocator.Temp);
            foreach (var (fTag, lt) in SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<WallHubTag>())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    existingHubs++;
                    hubPositions.Add(lt.ValueRO.Position);
                }
            }

            if (existingHubs >= WallPerimeterHubs)
            {
                // All hubs placed — check if segments need connecting
                ConnectUnlinkedWallHubs(ref state, faction);
                hubPositions.Dispose();
                return;
            }

            // Check affordability (hub cost)
            if (!BuildCosts.TryGet("Alanthor_Wall", out var wallCost))
            {
                hubPositions.Dispose();
                return;
            }
            if (!FactionEconomy.CanAfford(em, faction, wallCost))
            {
                hubPositions.Dispose();
                return;
            }

            // Calculate next hub position on the perimeter ring
            int hubIndex = existingHubs;
            float angle = hubIndex * (2f * math.PI / WallPerimeterHubs);
            float3 hubPos = hallPos + new float3(
                math.cos(angle) * WallPerimeterRadius,
                0f,
                math.sin(angle) * WallPerimeterRadius);
            hubPos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(hubPos.x, hubPos.z);

            buildReqs.Add(new BuildRequest
            {
                BuildingType = "Alanthor_Wall",
                DesiredPosition = hubPos,
                Priority = 4,
                Assigned = 0,
                AssignedBuilder = Entity.Null
            });

            AILogger.Log(faction, "ECONOMY",
                $"Alanthor: Queued wall hub {hubIndex + 1}/{WallPerimeterHubs} at ({hubPos.x:F0},{hubPos.z:F0})");

            hubPositions.Dispose();
        }

        /// <summary>
        /// Find adjacent wall hubs that aren't connected by segments and create segments between them.
        /// This closes the wall perimeter so it generates income.
        /// </summary>
        private void ConnectUnlinkedWallHubs(ref SystemState state, Faction faction)
        {
            var em = state.EntityManager;

            // Gather all wall hubs for this faction
            var hubs = new Unity.Collections.NativeList<Entity>(Allocator.Temp);
            var hubPos = new Unity.Collections.NativeList<float3>(Allocator.Temp);

            foreach (var (fTag, lt, entity) in SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<WallHubTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    hubs.Add(entity);
                    hubPos.Add(lt.ValueRO.Position);
                }
            }

            // For each hub, check if it's connected to the next hub in the ring
            // (connection order: 0→1→2→...→N-1→0)
            for (int i = 0; i < hubs.Length; i++)
            {
                int next = (i + 1) % hubs.Length;
                Entity hubA = hubs[i];
                Entity hubB = hubs[next];

                // Check if already connected
                if (!em.HasBuffer<WallHubLink>(hubA)) continue;
                var links = em.GetBuffer<WallHubLink>(hubA);
                bool connected = false;
                for (int j = 0; j < links.Length; j++)
                {
                    if (links[j].ConnectedHub == hubB)
                    {
                        connected = true;
                        break;
                    }
                }

                if (!connected)
                {
                    // Check affordability for segment
                    if (!BuildCosts.TryGet("Alanthor_Wall", out var segCost)) continue;
                    if (!FactionEconomy.Spend(em, faction, segCost)) continue;

                    AlanthorWall.CreateSegment(em, hubA, hubB, faction);
                    AILogger.Log(faction, "ECONOMY",
                        $"Alanthor: Created wall segment connecting hub {i} → {next}");

                    // Only one segment per tick
                    break;
                }
            }

            hubs.Dispose();
            hubPos.Dispose();
        }

        /// <summary>
        /// Count how many instances of a building type a faction owns (built + under construction).
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

        /// <summary>
        /// Manages vault deposits for AI factions.
        /// Finds a completed vault, picks the highest-surplus resource type, and deposits excess.
        /// VaultStorage.ResourceType: 0=None, 1=Supplies, 2=Iron, 3=Crystal, 4=Veilsteel, 5=Glow.
        /// </summary>
        private void ManageVaults(ref SystemState state, Faction faction)
        {
            var em = state.EntityManager;

            // Find this faction's completed vault
            Entity vaultEntity = Entity.Null;
            VaultStorage vaultData = default;

            foreach (var (vault, fTag, entity) in SystemAPI
                .Query<RefRO<VaultStorage>, RefRO<FactionTag>>()
                .WithAll<VaultTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    vaultEntity = entity;
                    vaultData = vault.ValueRO;
                    break;
                }
            }

            if (vaultEntity == Entity.Null) return;

            // Skip if vault is locked
            if (vaultData.LockTimer > 0f) return;

            // Get faction resources
            FactionResources resources = default;
            foreach (var (fTag, res) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionResources>>())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    resources = res.ValueRO;
                    break;
                }
            }

            // If vault already has a resource type set, deposit more of the same
            int resourceType = vaultData.ResourceType;

            if (resourceType == 0)
            {
                // Pick the resource type with highest surplus above threshold
                int bestType = 0;
                int bestSurplus = 0;

                // 1=Supplies, 2=Iron, 3=Crystal
                if (resources.Supplies > AITuning.VaultSurplusThreshold && resources.Supplies > bestSurplus)
                { bestType = 1; bestSurplus = resources.Supplies; }
                if (resources.Iron > AITuning.VaultSurplusThreshold && resources.Iron > bestSurplus)
                { bestType = 2; bestSurplus = resources.Iron; }
                if (resources.Crystal > AITuning.VaultSurplusThreshold && resources.Crystal > bestSurplus)
                { bestType = 3; bestSurplus = resources.Crystal; }

                if (bestType == 0) return; // No surplus worth depositing
                resourceType = bestType;
            }

            // Check if we can afford the deposit
            int available = resourceType switch
            {
                1 => resources.Supplies,
                2 => resources.Iron,
                3 => resources.Crystal,
                4 => resources.Veilsteel,
                5 => resources.Glow,
                _ => 0
            };

            // Only deposit if we have surplus above threshold
            if (available <= AITuning.VaultSurplusThreshold) return;
            int depositAmount = math.min(AITuning.VaultDepositAmount, available - AITuning.VaultSurplusThreshold);
            if (depositAmount <= 0) return;

            // Spend from faction bank
            Cost cost = resourceType switch
            {
                1 => Cost.Of(supplies: depositAmount),
                2 => Cost.Of(iron: depositAmount),
                3 => Cost.Of(crystal: depositAmount),
                4 => Cost.Of(veilsteel: depositAmount),
                5 => Cost.Of(glow: depositAmount),
                _ => default
            };

            if (!FactionEconomy.Spend(em, faction, cost)) return;

            // Deposit into vault
            vaultData.ResourceType = resourceType;
            vaultData.StoredAmount += depositAmount;
            vaultData.LockTimer = vaultData.LockDuration;
            em.SetComponentData(vaultEntity, vaultData);

            AILogger.Log(faction, "ECONOMY",
                $"Vault deposit: {depositAmount} of type {resourceType}, total stored: {(int)vaultData.StoredAmount}");
        }

        /// <summary>
        /// Manages smelter supply for AI factions.
        /// Finds idle miners and assigns ForgeSupplyOrders to supply the faction's smelter.
        /// </summary>
        // Fix #215: takes an EntityCommandBuffer from the caller so structural
        // changes (adding ForgeSupplyOrder to idle miners) are deferred to a
        // safe playback point instead of mutating the archetype of an entity
        // while the outer AIEconomyManager.OnUpdate foreach is still iterating
        // faction brains via SystemAPI.Query.
        private void ManageSmelters(ref SystemState state, Faction faction, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;

            // Find this faction's completed smelter with ForgeStorage
            Entity smelterEntity = Entity.Null;

            foreach (var (forge, fTag, entity) in SystemAPI
                .Query<RefRO<ForgeStorage>, RefRO<FactionTag>>()
                .WithAll<SmelterTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    smelterEntity = entity;
                    break;
                }
            }

            if (smelterEntity == Entity.Null) return;

            // Count miners already supplying this smelter
            int assignedSuppliers = 0;
            foreach (var (supplyOrder, fTag) in SystemAPI
                .Query<RefRO<ForgeSupplyOrder>, RefRO<FactionTag>>()
                .WithAll<MinerTag>())
            {
                if (fTag.ValueRO.Value == faction)
                    assignedSuppliers++;
            }

            if (assignedSuppliers >= AITuning.SmelterTargetMiners)
            {
                AILogger.Log(faction, "ECONOMY",
                    $"Smelter already has {assignedSuppliers}/{AITuning.SmelterTargetMiners} supply miners");
                return;
            }

            int needed = AITuning.SmelterTargetMiners - assignedSuppliers;

            // Find idle miners of this faction (no ForgeSupplyOrder, idle state, no build order)
            var idleMiners = new NativeList<Entity>(Allocator.Temp);

            foreach (var (minerState, fTag, entity) in SystemAPI
                .Query<RefRO<MinerState>, RefRO<FactionTag>>()
                .WithAll<MinerTag>()
                .WithNone<ForgeSupplyOrder, BuildOrder>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value != faction) continue;
                if (minerState.ValueRO.State == MinerWorkState.Idle)
                    idleMiners.Add(entity);
            }

            int toAssign = math.min(needed, idleMiners.Length);

            for (int i = 0; i < toAssign; i++)
            {
                Entity miner = idleMiners[i];

                // Reset miner state — pure data write on an existing component,
                // no archetype change, so the direct EntityManager path is safe
                // even during an outer SystemAPI iteration.
                var ms = em.GetComponentData<MinerState>(miner);
                ms.State = MinerWorkState.Idle;
                ms.AssignedDeposit = Entity.Null;
                ms.DropoffTarget = Entity.Null;
                em.SetComponentData(miner, ms);

                // Assign forge supply order via ECB — adding the component when
                // the miner doesn't already have it is a structural change and
                // must not be done inline against the EntityManager while
                // queries are still iterating upstream.
                var forgeOrder = new ForgeSupplyOrder
                {
                    Forge = smelterEntity,
                    ResourceType = 0,
                    Phase = 0
                };
                if (em.HasComponent<ForgeSupplyOrder>(miner))
                    ecb.SetComponent(miner, forgeOrder);
                    else
                        ecb.AddComponent(miner, forgeOrder);
            }

            idleMiners.Dispose();

            if (toAssign > 0)
            {
                AILogger.Log(faction, "ECONOMY",
                    $"Assigned {toAssign} idle miners to supply smelter ({assignedSuppliers + toAssign}/{AITuning.SmelterTargetMiners})");
            }
        }

        /// <summary>
        /// Proactively sends idle AI miners to harvest crystal from cadavers (dead crystal creatures).
        /// MiningSystem auto-finds cadavers within SearchRadius (50f), but cadavers may be far away.
        /// This method searches the whole map for non-depleted cadavers and assigns idle miners.
        /// </summary>
        // Fix #215: accepts a deferredGathers list instead of calling
        // GatherCommandHelper.Execute inline. The helper performs structural
        // changes that would invalidate the enclosing SystemAPI query iterators.
        private void AssignMinersToCadavers(ref SystemState state, Faction faction,
            NativeList<DeferredGather> deferredGathers)
        {
            var em = state.EntityManager;

            // Find non-depleted cadavers on the map
            var cadaverList = new NativeList<Entity>(Allocator.Temp);
            var cadaverPositions = new NativeList<float3>(Allocator.Temp);

            foreach (var (cadaverState, transform, entity) in SystemAPI
                .Query<RefRO<CadaverState>, RefRO<LocalTransform>>()
                .WithAll<CadaverTag>()
                .WithEntityAccess())
            {
                if (cadaverState.ValueRO.Depleted == 1) continue;
                if (cadaverState.ValueRO.RemainingCrystal <= 0) continue;
                cadaverList.Add(entity);
                cadaverPositions.Add(transform.ValueRO.Position);
            }

            if (cadaverList.Length == 0)
            {
                cadaverList.Dispose();
                cadaverPositions.Dispose();
                return;
            }

            // Find idle miners of this faction (not already gathering, no build/forge orders)
            var idleMiners = new NativeList<Entity>(Allocator.Temp);
            var minerPositions = new NativeList<float3>(Allocator.Temp);

            foreach (var (minerState, fTag, transform, entity) in SystemAPI
                .Query<RefRO<MinerState>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<MinerTag>()
                .WithNone<ForgeSupplyOrder, BuildOrder, GatherCommand>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value != faction) continue;
                if (minerState.ValueRO.State != MinerWorkState.Idle) continue;
                idleMiners.Add(entity);
                minerPositions.Add(transform.ValueRO.Position);
            }

            if (idleMiners.Length == 0)
            {
                cadaverList.Dispose();
                cadaverPositions.Dispose();
                idleMiners.Dispose();
                minerPositions.Dispose();
                return;
            }

            // Find dropoff location (nearest Hall or GathererHut)
            Entity dropoff = Entity.Null;
            float3 basePos = float3.zero;
            foreach (var (fTag, transform, entity) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<HallTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value == faction)
                {
                    dropoff = entity;
                    basePos = transform.ValueRO.Position;
                    break;
                }
            }

            if (dropoff == Entity.Null)
            {
                cadaverList.Dispose();
                cadaverPositions.Dispose();
                idleMiners.Dispose();
                minerPositions.Dispose();
                return;
            }

            // Assign up to 2 idle miners to the nearest cadaver
            int assigned = 0;
            for (int m = 0; m < idleMiners.Length && assigned < 2; m++)
            {
                // Find nearest cadaver to this miner
                float bestDist = float.MaxValue;
                int bestIdx = -1;
                for (int c = 0; c < cadaverList.Length; c++)
                {
                    float dist = math.distance(minerPositions[m], cadaverPositions[c]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = c;
                    }
                }

                if (bestIdx < 0) continue;

                deferredGathers.Add(new DeferredGather
                {
                    Miner = idleMiners[m],
                    Target = cadaverList[bestIdx],
                    Dropoff = dropoff
                });
                assigned++;

                AILogger.Log(faction, "ECONOMY",
                    $"Assigned miner to cadaver (crystal) at ({cadaverPositions[bestIdx].x:F0},{cadaverPositions[bestIdx].z:F0}), dist={bestDist:F0}");
            }

            cadaverList.Dispose();
            cadaverPositions.Dispose();
            idleMiners.Dispose();
            minerPositions.Dispose();
        }

        /// <summary>
        /// Find a build location for GathererHuts, ensuring good spacing.
        /// GatherRadius = 15f, so huts need ≥25 unit separation for decent yield.
        /// Tries random positions and picks the best-spaced one.
        /// </summary>
        private float3 FindGatherersHutLocation(ref SystemState state, Faction faction)
        {
            const float MIN_HUT_SPACING = 25f; // Must be > GatherRadius(15) to minimize overlap
            const float PLACE_DIST_MIN = 20f;
            const float PLACE_DIST_MAX = 55f;
            const int MAX_ATTEMPTS = 20;

            // Find base position
            float3 basePos = new float3(0, 0, 0);
            foreach (var (factionTag, transform, buildingTag) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && buildingTag.ValueRO.IsBase == 1)
                {
                    basePos = transform.ValueRO.Position;
                    break;
                }
            }

            // Collect all existing GathererHut positions for this faction (includes under construction)
            var hutPositions = new NativeList<float3>(Allocator.Temp);

            foreach (var (gathererTag, factionTag, transform) in
                SystemAPI.Query<RefRO<GathererHutTag>, RefRO<FactionTag>, RefRO<LocalTransform>>())
            {
                if (factionTag.ValueRO.Value == faction)
                    hutPositions.Add(transform.ValueRO.Position);
            }

            // Also include pending build request positions for GathererHuts
            foreach (var (brainComp, buildReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>())
            {
                if (brainComp.ValueRO.Owner != faction) continue;
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].BuildingType.Equals("GatherersHut"))
                        hutPositions.Add(buildReqs[i].DesiredPosition);
                }
                break;
            }

            // Fix #230: guard against seed 0 (produces all-zero sequence).
            uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction * 137 + 7);
            if (seed == 0) seed = 1;
            var random = new Unity.Mathematics.Random(seed);

            float3 bestPos = basePos + new float3(30, 0, 0); // fallback
            float bestMinDist = 0f;

            for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                float distance = random.NextFloat(PLACE_DIST_MIN, PLACE_DIST_MAX);

                float3 candidate = basePos + new float3(
                    math.cos(angle) * distance,
                    0,
                    math.sin(angle) * distance
                );

                // If no existing huts, accept first candidate
                if (hutPositions.Length == 0)
                {
                    bestPos = candidate;
                    break;
                }

                // Find minimum distance to any existing/pending hut
                float minDist = float.MaxValue;
                for (int i = 0; i < hutPositions.Length; i++)
                {
                    float d = math.distance(candidate, hutPositions[i]);
                    if (d < minDist) minDist = d;
                }

                // Well-spaced — accept immediately
                if (minDist >= MIN_HUT_SPACING)
                {
                    bestPos = candidate;
                    break;
                }

                // Track best candidate so far (furthest from nearest hut)
                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    bestPos = candidate;
                }
            }

            hutPositions.Dispose();
            return bestPos;
        }

        /// <summary>
        /// Generic build location for non-GathererHut buildings.
        /// Places at a random offset from the faction's base.
        /// </summary>
        private float3 FindBuildLocation(ref SystemState state, Faction faction, FixedString64Bytes buildingType)
        {
            float3 basePos = new float3(0, 0, 0);
            bool foundBase = false;

            foreach (var (factionTag, transform, buildingTag) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && buildingTag.ValueRO.IsBase == 1)
                {
                    basePos = transform.ValueRO.Position;
                    foundBase = true;
                    break;
                }
            }

            if (!foundBase)
                return new float3(10, 0, 10);

            // Fix #230: guard against seed 0 (produces all-zero sequence).
            uint seedFbl = (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction * 53);
            if (seedFbl == 0) seedFbl = 1;
            var random = new Unity.Mathematics.Random(seedFbl);
            float angle = random.NextFloat(0, math.PI * 2);
            float distance = random.NextFloat(15, 25);

            return basePos + new float3(
                math.cos(angle) * distance,
                0,
                math.sin(angle) * distance
            );
        }
    }

    struct ResourceRequestComparer : IComparer<ResourceRequest>
    {
        public int Compare(ResourceRequest a, ResourceRequest b)
        {
            return b.Priority.CompareTo(a.Priority);
        }
    }
}