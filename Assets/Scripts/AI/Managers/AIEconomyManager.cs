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

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Unity.Transforms.TransformSystemGroup))]
    public partial struct AIEconomyManager : ISystem
    {
        private const float MINE_CHECK_INTERVAL = 5.0f;
        private const int TARGET_MINERS_PER_MINE = 3;
        private const int MAX_MINERS = 9; // Cap: 3 mines × 3 miners each in early game
        private const int MIN_SUPPLIES_THRESHOLD = 200;
        private const int TARGET_GATHERERS_HUTS = 3;
        private const int CRYSTAL_FOR_CHOICE_BUILDING = 100;
        private const float CHOICE_BUILDING_CHECK_INTERVAL = 15.0f;
        private const float VAULT_CHECK_INTERVAL = 30.0f;
        private const float SMELTER_CHECK_INTERVAL = 15.0f;
        private const int VAULT_DEPOSIT_AMOUNT = 200;
        private const int VAULT_SURPLUS_THRESHOLD = 500;
        private const int SMELTER_TARGET_MINERS = 2;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (brain, economyState, resourceReqs, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AIEconomyState>, DynamicBuffer<ResourceRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var economy = economyState.ValueRW;
                Faction faction = brain.ValueRO.Owner;

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
                }

                // 4-7. Barracks, military, crystal hunting — handled by AIMilitaryManager + AICrystalHuntBehavior

                // 8. Choice building (requires crystal from killed crystallings)
                CheckChoiceBuildingNeeds(ref state, brain.ValueRO, ecb);

                // 9. Age up (requires completed choice building + resources)
                CheckAgeUp(ref state, brain.ValueRO, ecb);

                // 10. Vault management — deposit surplus resources for interest (Alanthor)
                if (time >= economy.LastVaultCheck + VAULT_CHECK_INTERVAL)
                {
                    economy.LastVaultCheck = time;
                    ManageVaults(ref state, faction);
                }

                // 11. Smelter management — assign idle miners to supply forges (Alanthor)
                if (time >= economy.LastSmelterCheck + SMELTER_CHECK_INTERVAL)
                {
                    economy.LastSmelterCheck = time;
                    ManageSmelters(ref state, faction);
                }

                ProcessResourceRequests(ref state, faction, resourceReqs);

                // CRITICAL: Write back the modified economy state
                // Without this, LastMineAssignmentCheck etc. revert to 0 every frame
                economyState.ValueRW = economy;
            }

            ecb.Playback(em);
            ecb.Dispose();
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
            economy.DesiredMiners = math.min(mineCount * TARGET_MINERS_PER_MINE, MAX_MINERS);

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

                economy.NeedsMoreSupplyIncome = resources.ValueRO.Supplies < MIN_SUPPLIES_THRESHOLD ? (byte)1 : (byte)0;
                economy.NeedsMoreIronIncome = resources.ValueRO.Iron < 100 ? (byte)1 : (byte)0;

                // Always want GathererHuts — keep at base target in early game
                // Don't escalate to 5 early: that drains supplies needed for miners/barracks
                economy.DesiredGatherersHuts = TARGET_GATHERERS_HUTS;

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

            if (crystalAmount < CRYSTAL_FOR_CHOICE_BUILDING) return;

            // Check no pending choice building request
            foreach (var (brainComp, buildReqs) in SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>())
            {
                if (brainComp.ValueRO.Owner != faction) continue;

                for (int i = 0; i < buildReqs.Length; i++)
                {
                    string bt = buildReqs[i].BuildingType.ToString();
                    if (bt == "TempleOfRidan" || bt == "VaultOfAlmierra" || bt == "FiendstoneKeep")
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
                AIPersonality.Defensive => "TempleOfRidan",
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

                UnityEngine.Debug.Log($"[AIEconomyManager] {faction} requesting choice building: {buildingId}");
                break;
            }
        }

        /// <summary>
        /// Checks if the AI faction should age-up to Era 2.
        /// Requires: a completed choice building, enough resources, and not already aged-up.
        /// Uses ECB for structural changes to avoid invalidating iterators.
        /// </summary>
        private void CheckAgeUp(ref SystemState state, AIBrain brain, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;
            Faction faction = brain.Owner;

            // Find the Hall for this faction (need FactionProgress)
            Entity hallEntity = Entity.Null;
            byte currentCulture = Cultures.None;

            foreach (var (factionTag, progress, buildingTag, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionProgress>, RefRO<BuildingTag>>()
                .WithAll<HallTag>()
                .WithNone<UnderConstruction>()
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

            // Choose culture based on personality
            byte culture = brain.Personality switch
            {
                AIPersonality.Aggressive => Cultures.Feraldis,
                AIPersonality.Rush => Cultures.Feraldis,
                AIPersonality.Defensive => Cultures.Runai,
                AIPersonality.Economic => Cultures.Alanthor,
                _ => Cultures.Runai // Balanced
            };

            // Set FactionProgress.Culture on the Hall (non-structural write, safe)
            var progress_val = em.GetComponentData<FactionProgress>(hallEntity);
            progress_val.Culture = culture;
            em.SetComponentData(hallEntity, progress_val);

            // Scale the Hall 1.3x (non-structural write, safe)
            var lt = em.GetComponentData<LocalTransform>(hallEntity);
            lt.Scale = 1.3f;
            em.SetComponentData(hallEntity, lt);

            // Culture-specific effects — use ECB for structural changes
            if (culture == Cultures.Alanthor)
            {
                // Start 2-minute self-destruct countdown on all faction GathererHuts
                foreach (var (ghTag, ghFaction, ghEntity) in
                    SystemAPI.Query<RefRO<GathererHutTag>, RefRO<FactionTag>>()
                    .WithNone<UnderConstruction, SelfDestructTimer>()
                    .WithEntityAccess())
                {
                    if (ghFaction.ValueRO.Value != faction) continue;
                    ecb.AddComponent(ghEntity, new SelfDestructTimer
                    {
                        TimeRemaining = 120f,
                        RefundPaid = 0
                    });
                }
            }

            AILogger.Log(faction, "ECONOMY", $"=== AGED UP to Era 2 — culture: {CultureConfig.GetName(culture)} ===");
            UnityEngine.Debug.Log($"[AIEconomyManager] {faction} aged up to Era 2 — culture: {CultureConfig.GetName(culture)}");
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
                if (resources.Supplies > VAULT_SURPLUS_THRESHOLD && resources.Supplies > bestSurplus)
                { bestType = 1; bestSurplus = resources.Supplies; }
                if (resources.Iron > VAULT_SURPLUS_THRESHOLD && resources.Iron > bestSurplus)
                { bestType = 2; bestSurplus = resources.Iron; }
                if (resources.Crystal > VAULT_SURPLUS_THRESHOLD && resources.Crystal > bestSurplus)
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
            if (available <= VAULT_SURPLUS_THRESHOLD) return;
            int depositAmount = math.min(VAULT_DEPOSIT_AMOUNT, available - VAULT_SURPLUS_THRESHOLD);
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
        private void ManageSmelters(ref SystemState state, Faction faction)
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

            if (assignedSuppliers >= SMELTER_TARGET_MINERS)
            {
                AILogger.Log(faction, "ECONOMY",
                    $"Smelter already has {assignedSuppliers}/{SMELTER_TARGET_MINERS} supply miners");
                return;
            }

            int needed = SMELTER_TARGET_MINERS - assignedSuppliers;

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

                // Reset miner state
                var ms = em.GetComponentData<MinerState>(miner);
                ms.State = MinerWorkState.Idle;
                ms.AssignedDeposit = Entity.Null;
                ms.DropoffTarget = Entity.Null;
                em.SetComponentData(miner, ms);

                // Assign forge supply order (same pattern as RTSInputManager)
                if (em.HasComponent<ForgeSupplyOrder>(miner))
                {
                    em.SetComponentData(miner, new ForgeSupplyOrder
                    {
                        Forge = smelterEntity,
                        ResourceType = 0,
                        Phase = 0
                    });
                }
                else
                {
                    em.AddComponentData(miner, new ForgeSupplyOrder
                    {
                        Forge = smelterEntity,
                        ResourceType = 0,
                        Phase = 0
                    });
                }
            }

            idleMiners.Dispose();

            if (toAssign > 0)
            {
                AILogger.Log(faction, "ECONOMY",
                    $"Assigned {toAssign} idle miners to supply smelter ({assignedSuppliers + toAssign}/{SMELTER_TARGET_MINERS})");
            }
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

            var random = new Unity.Mathematics.Random(
                (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction * 137 + 7));

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

            var random = new Unity.Mathematics.Random(
                (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction * 53));
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