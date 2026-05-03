// AIMilitaryManager.cs
// Requests resources and barracks, trains units, creates armies and scouts
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.Core;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.AI
{
    [DisableAutoCreation] // Replaced by SimpleAISystem.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIBuildingManager))]
    public partial struct AIMilitaryManager : ISystem
    {
        private const float RECRUITMENT_CHECK_INTERVAL = 5.0f;
        private const int MIN_ARMY_SIZE = 3;
        private const int MAX_ARMY_SIZE = 16;
        private const int TARGET_BARRACKS = 4;
        private const int MAX_HUTS = 10;
        private const int POP_HEADROOM = 2; // Request hut when this close to cap

        private NativeHashMap<int, int> _nextArmyId;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
            _nextArmyId = new NativeHashMap<int, int>(8, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_nextArmyId.IsCreated)
                _nextArmyId.Dispose();
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

            foreach (var (brain, militaryState, stratState, recruitReqs, resourceReqs, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AIMilitaryState>, RefRO<AIStrategyState>,
                    DynamicBuffer<RecruitmentRequest>, DynamicBuffer<ResourceRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var state_val = militaryState.ValueRW;
                var strategy = stratState.ValueRO.Current;

                if (time >= state_val.LastRecruitmentCheck + state_val.RecruitmentCheckInterval)
                {
                    state_val.LastRecruitmentCheck = time;
                    ManageMilitary(ref state, brain.ValueRO.Owner, ref state_val, strategy, recruitReqs, resourceReqs, ecb);
                    ProcessRecruitmentRequests(ref state, brain.ValueRO.Owner, recruitReqs, ecb);
                    OrganizeArmies(ref state, brain.ValueRO.Owner, ref state_val, ecb);
                }

                // CRITICAL: Write back the modified military state
                // Without this, LastRecruitmentCheck resets to 0 every frame
                militaryState.ValueRW = state_val;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        private void ManageMilitary(ref SystemState state, Faction faction,
            ref AIMilitaryState militaryState, AIStrategy strategy,
            DynamicBuffer<RecruitmentRequest> recruitReqs,
            DynamicBuffer<ResourceRequest> resourceReqs, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;
            CountMilitaryUnits(ref state, faction, ref militaryState);
            CountQueuedMilitary(ref state, faction, ref militaryState);

            // Log pop + military state on interval (not every frame)
            int popCur = 0, popMx = 0;
            foreach (var (t, p) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionPopulation>>())
            {
                if (t.ValueRO.Value == faction) { popCur = p.ValueRO.Current; popMx = p.ValueRO.Max; break; }
            }
            AILogger.Log(faction, "MILITARY",
                $"Pop:{popCur}/{popMx} — Soldiers:{militaryState.TotalSoldiers}(+{militaryState.QueuedSoldiers}q) Archers:{militaryState.TotalArchers}(+{militaryState.QueuedArchers}q) Siege:{militaryState.TotalSiegeUnits}(+{militaryState.QueuedSiegeUnits}q)");

            // Count military training buildings (barracks + culture-specific)
            int barracksCount = 0;
            int completeBarracks = 0;
            foreach (var (trainingState, factionTag, entity) in
                SystemAPI.Query<RefRO<TrainingState>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                bool isMilitary = em.HasComponent<BarracksTag>(entity) ||
                    em.HasComponent<GarrisonTag>(entity) ||
                    em.HasComponent<LonghouseTag>(entity) ||
                    em.HasComponent<SiegeWorkshopTag>(entity) ||
                    em.HasComponent<SiegeYardTag>(entity) ||
                    em.HasComponent<FerSiegeYardTag>(entity) ||
                    em.HasComponent<RoyalStableTag>(entity) ||
                    em.HasComponent<BazaarTag>(entity);

                if (isMilitary)
                {
                    barracksCount++;
                    if (!em.HasComponent<UnderConstruction>(entity))
                        completeBarracks++;
                }
            }
            militaryState.ActiveBarracks = barracksCount;
            AILogger.Log(faction, "MILITARY", $"Military buildings: {completeBarracks} complete, {barracksCount - completeBarracks} building");

            // Build order step 4: only request barracks after having miners
            bool hasMiners = false;
            foreach (var (minerTag, fTag) in SystemAPI.Query<RefRO<MinerTag>, RefRO<FactionTag>>())
            {
                if (fTag.ValueRO.Value == faction) { hasMiners = true; break; }
            }

            // Rush strategy: build barracks immediately, don't wait for miners
            bool needsMinersFirst = strategy != AIStrategy.Rush;
            if (barracksCount < TARGET_BARRACKS && (hasMiners || !needsMinersFirst))
            {
                AILogger.Log(faction, "MILITARY", $"Need barracks ({barracksCount} < {TARGET_BARRACKS}), requesting build [strategy:{strategy}]");
                RequestBarracks(ref state, faction);
            }
            else if (barracksCount < TARGET_BARRACKS && !hasMiners && needsMinersFirst)
            {
                AILogger.Log(faction, "MILITARY", "Barracks request deferred — no miners yet");
            }

            // Check if population is near cap — request Huts for more housing
            CheckPopulationNeeds(ref state, faction);

            // Only request military units once at least 1 barracks is fully built
            if (completeBarracks > 0)
            {
                DetermineMilitaryNeeds(ref state, faction, strategy, ref militaryState, recruitReqs);
            }
            else
            {
                AILogger.Log(faction, "MILITARY", "Military recruitment deferred — no complete barracks");
            }

            RequestMilitaryResources(ref state, faction, militaryState, resourceReqs);
        }

        private void CountMilitaryUnits(ref SystemState state, Faction faction, ref AIMilitaryState militaryState)
        {
            militaryState.TotalSoldiers = 0;
            militaryState.TotalArchers = 0;
            militaryState.TotalSiegeUnits = 0;

            // Count battalion leaders as 1 unit each, skip battalion members
            // (a trained Swordsman/Archer spawns as 1 leader + 15 members — count as 1)
            foreach (var (unitTag, factionTag) in
                SystemAPI.Query<RefRO<UnitTag>, RefRO<FactionTag>>()
                .WithNone<BattalionMemberData>())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                switch (unitTag.ValueRO.Class)
                {
                    case UnitClass.Melee:
                        militaryState.TotalSoldiers++;
                        break;
                    case UnitClass.Ranged:
                        militaryState.TotalArchers++;
                        break;
                    case UnitClass.Siege:
                        militaryState.TotalSiegeUnits++;
                        break;
                }
            }
        }

        private void CountQueuedMilitary(ref SystemState state, Faction faction, ref AIMilitaryState militaryState)
        {
            int queuedSoldiers = 0;
            int queuedArchers = 0;
            int queuedSiege = 0;

            foreach (var (factionTag, trainQueue) in SystemAPI.Query<RefRO<FactionTag>, DynamicBuffer<TrainQueueItem>>())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                for (int i = 0; i < trainQueue.Length; i++)
                {
                    var item = trainQueue[i];
                    if (item.UnitId.Equals("Swordsman"))
                        queuedSoldiers++;
                    else if (item.UnitId.Equals("Archer"))
                        queuedArchers++;
                    else if (item.UnitId.Equals("Catapult"))
                        queuedSiege++;
                }
            }

            militaryState.QueuedSoldiers = queuedSoldiers;
            militaryState.QueuedArchers = queuedArchers;
            militaryState.QueuedSiegeUnits = queuedSiege;
        }

        private void DetermineMilitaryNeeds(ref SystemState state, Faction faction,
            AIStrategy strategy, ref AIMilitaryState militaryState, DynamicBuffer<RecruitmentRequest> recruitReqs)
        {
            // Get AI personality for composition preferences
            AIPersonality personality = AIPersonality.Balanced;
            foreach (var brain in SystemAPI.Query<RefRO<AIBrain>>())
            {
                if (brain.ValueRO.Owner == faction)
                {
                    personality = brain.ValueRO.Personality;
                    break;
                }
            }

            // Clear stale military recruitment requests — only count live + in-training-queue
            // Pending requests that couldn't be afforded should NOT count as "committed"
            for (int i = recruitReqs.Length - 1; i >= 0; i--)
            {
                var r = recruitReqs[i];
                if (r.UnitType == UnitClass.Melee || r.UnitType == UnitClass.Ranged || r.UnitType == UnitClass.Siege)
                    recruitReqs.RemoveAt(i);
            }

            // Only count live units + training queue — NOT pending requests
            int totalSoldiers = militaryState.TotalSoldiers + militaryState.QueuedSoldiers;
            int totalArchers = militaryState.TotalArchers + militaryState.QueuedArchers;
            int totalSiege = militaryState.TotalSiegeUnits + militaryState.QueuedSiegeUnits;
            int totalMilitary = totalSoldiers + totalArchers + totalSiege;

            AILogger.Log(faction, "MILITARY",
                $"Military needs — live+queue:{totalMilitary} (soldiers:{totalSoldiers} archers:{totalArchers} siege:{totalSiege})");

            // Strategy-aware recruitment: Rush trains heavily early,
            // Eco Boom delays, Defensive builds standing army, etc.
            {
                // How many to request per cycle depends on strategy
                int needed = strategy switch
                {
                    AIStrategy.Rush => 4,       // Fast production
                    AIStrategy.EcoBoom => totalMilitary < 3 ? 1 : 2, // Minimal early, ramp later
                    AIStrategy.TechRush => 2,   // Moderate — save resources for tech
                    AIStrategy.Aggressive => 3, // Continuous pressure
                    AIStrategy.Defensive => 2,  // Standing army, steady build
                    _ => math.max(2, MIN_ARMY_SIZE)
                };

                AILogger.Log(faction, "MILITARY",
                    $"Strategy:{strategy} — requesting {needed} units (total:{totalMilitary})");

                // Determine composition based on strategy
                int soldiers, archers, siege;
                switch (strategy)
                {
                    case AIStrategy.Rush:
                        // Rush: all swords early, mix later
                        soldiers = (int)(needed * 0.8f);
                        archers = needed - soldiers;
                        siege = 0;
                        break;
                    case AIStrategy.Defensive:
                        // Defensive: archer-heavy, some melee screen
                        soldiers = (int)(needed * 0.3f);
                        archers = (int)(needed * 0.5f);
                        siege = needed - soldiers - archers;
                        break;
                    case AIStrategy.Aggressive:
                        // Aggressive: balanced with siege
                        soldiers = (int)(needed * 0.5f);
                        archers = (int)(needed * 0.3f);
                        siege = needed - soldiers - archers;
                        break;
                    case AIStrategy.TechRush:
                        // Tech: mostly archers (ranged advantage from upgrades)
                        soldiers = (int)(needed * 0.3f);
                        archers = (int)(needed * 0.5f);
                        siege = needed - soldiers - archers;
                        break;
                    default: // EcoBoom, fallback
                        soldiers = (int)(needed * 0.4f);
                        archers = (int)(needed * 0.4f);
                        siege = needed - soldiers - archers;
                        break;
                }

                // Ensure we always request at least 1 of something
                if (soldiers == 0 && archers == 0 && siege == 0)
                    soldiers = 1;

                if (soldiers > 0)
                {
                    recruitReqs.Add(new RecruitmentRequest
                    {
                        UnitType = UnitClass.Melee,
                        Quantity = soldiers,
                        Priority = 5,
                        RequestingManager = Entity.Null
                    });
                }

                if (archers > 0)
                {
                    recruitReqs.Add(new RecruitmentRequest
                    {
                        UnitType = UnitClass.Ranged,
                        Quantity = archers,
                        Priority = 5,
                        RequestingManager = Entity.Null
                    });
                }
            }
        }

        private void ProcessRecruitmentRequests(ref SystemState state, Faction faction,
            DynamicBuffer<RecruitmentRequest> recruitReqs, EntityCommandBuffer ecb)
        {
            if (recruitReqs.Length == 0) return;

            var em = state.EntityManager;

            // Check population capacity — skip recruitment if at cap
            int popCurrent = 0, popMax = 0;
            foreach (var (tag, pop) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionPopulation>>())
            {
                if (tag.ValueRO.Value == faction)
                {
                    popCurrent = pop.ValueRO.Current;
                    popMax = pop.ValueRO.Max;
                    break;
                }
            }

            // Count units in training queues as committed population
            // (they'll spawn soon and consume pop slots — don't over-recruit)
            int trainingPop = 0;
            foreach (var (fTag, trainQueue) in SystemAPI.Query<RefRO<FactionTag>, DynamicBuffer<TrainQueueItem>>())
            {
                if (fTag.ValueRO.Value == faction)
                    trainingPop += trainQueue.Length;
            }

            int popAvailable = popMax - popCurrent - trainingPop;
            if (popAvailable < 0) popAvailable = 0;

            // (Pop state logged on ManageMilitary interval, not every frame)

            // Read faction culture for unit selection
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

            // Collect training buildings — allow busy buildings (AI queues like a player)
            // Queue length is checked when adding items, not here
            // Include barracks AND culture-specific military buildings (Garrison, Longhouse, SiegeWorkshop, etc.)
            var availableBarracks = new NativeList<Entity>(Allocator.Temp);
            var availableHalls = new NativeList<Entity>(Allocator.Temp);

            foreach (var (trainingState, factionTag, entity) in
                SystemAPI.Query<RefRO<TrainingState>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                // Include any building with a training queue that is a military producer
                bool isMilitary = em.HasComponent<BarracksTag>(entity) ||
                    em.HasComponent<GarrisonTag>(entity) ||
                    em.HasComponent<LonghouseTag>(entity) ||
                    em.HasComponent<SiegeWorkshopTag>(entity) ||
                    em.HasComponent<SiegeYardTag>(entity) ||
                    em.HasComponent<FerSiegeYardTag>(entity) ||
                    em.HasComponent<RoyalStableTag>(entity) ||
                    em.HasComponent<BazaarTag>(entity);

                if (isMilitary)
                    availableBarracks.Add(entity);
            }

            foreach (var (buildingTag, trainingState, factionTag, entity) in
                SystemAPI.Query<RefRO<BuildingTag>, RefRO<TrainingState>, RefRO<FactionTag>>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction &&
                    buildingTag.ValueRO.IsBase == 1)
                    availableHalls.Add(entity);
            }

            // Sort by priority (descending) and track which get fulfilled
            var sortedReqs = new NativeList<RecruitmentRequest>(Allocator.Temp);
            var fulfilled = new NativeList<byte>(Allocator.Temp);
            for (int i = 0; i < recruitReqs.Length; i++)
            {
                sortedReqs.Add(recruitReqs[i]);
                fulfilled.Add(0);
            }
            sortedReqs.Sort(new RecruitmentRequestComparer());

            for (int i = 0; i < sortedReqs.Length; i++)
            {
                var req = sortedReqs[i];
                if (req.Quantity <= 0) { fulfilled[i] = 1; continue; }

                bool useHall = req.UnitType == UnitClass.Economy || req.UnitType == UnitClass.Miner || req.UnitType == UnitClass.Scout;
                var availableBuildings = useHall ? availableHalls : availableBarracks;

                if (availableBuildings.Length == 0)
                    continue; // Keep for next frame

                if (popAvailable <= 0)
                    break; // No pop room — stop all recruitment

                // Pick first available building but DON'T remove it — allow multiple
                // request types (miners + builders) to share the same Hall
                Entity building = availableBuildings[0];

                if (em.HasBuffer<TrainQueueItem>(building))
                {
                    var queue = em.GetBuffer<TrainQueueItem>(building);

                    // Don't over-stuff the queue — cap at 5 items per building
                    if (queue.Length >= 5)
                        continue;

                    string unitId = GetCultureUnitIdForClass(req.UnitType, culture);
                    bool anyQueued = false;
                    int queuedCount = 0;

                    for (int j = 0; j < req.Quantity && j < 3; j++)
                    {
                        if (popAvailable <= 0) break;
                        if (queue.Length >= 5) break; // Respect queue cap

                        // Deduct unit cost (same as player UI does)
                        Cost unitCost = GetUnitCost(unitId);
                        if (!unitCost.IsZero && !FactionEconomy.Spend(em, faction, unitCost))
                        {
                            AILogger.Log(faction, "RECRUIT", $"  [{i}] {unitId} — CAN'T AFFORD (cost: {unitCost.Supplies}s/{unitCost.Iron}i)");
                            break; // Can't afford — stop queuing this type
                        }

                        queue.Add(new TrainQueueItem { UnitId = unitId });
                        popAvailable--;
                        anyQueued = true;
                        queuedCount++;
                    }

                    if (anyQueued)
                    {
                        fulfilled[i] = 1;
                        AILogger.Log(faction, "RECRUIT", $"  [{i}] Queued {queuedCount}x {unitId} (pri={req.Priority}), pop remaining={popAvailable}");
                    }
                }
            }

            // Only remove fulfilled requests — keep unfulfilled ones for next frame
            // (e.g., military requests waiting for barracks to be built)
            recruitReqs.Clear();
            int keptCount = 0;
            for (int i = 0; i < sortedReqs.Length; i++)
            {
                if (fulfilled[i] == 0)
                {
                    recruitReqs.Add(sortedReqs[i]);
                    keptCount++;
                }
            }
            // Unfulfilled requests retained for next frame

            availableBarracks.Dispose();
            availableHalls.Dispose();
            sortedReqs.Dispose();
            fulfilled.Dispose();
        }

        private string GetUnitIdForClass(UnitClass unitClass)
        {
            return unitClass switch
            {
                UnitClass.Melee => "Swordsman",
                UnitClass.Ranged => "Archer",
                UnitClass.Siege => "Catapult",
                UnitClass.Economy => "Builder",
                UnitClass.Miner => "Miner",
                UnitClass.Scout => "Scout",
                _ => "Swordsman"
            };
        }

        /// <summary>
        /// Get culture-specific unit ID for a unit class. Falls back to Era 1 units.
        /// </summary>
        private string GetCultureUnitIdForClass(UnitClass unitClass, byte culture)
        {
            if (culture == Cultures.None)
                return GetUnitIdForClass(unitClass);

            return (unitClass, culture) switch
            {
                // Runai culture units
                (UnitClass.Melee, Cultures.Runai)  => "Runai_Spearman",
                (UnitClass.Ranged, Cultures.Runai) => "Runai_Skirmisher",
                (UnitClass.Siege, Cultures.Runai)  => "Runai_Catapult",

                // Alanthor culture units
                (UnitClass.Melee, Cultures.Alanthor)  => "Alanthor_Sentinel",
                (UnitClass.Ranged, Cultures.Alanthor) => "Alanthor_Crossbowman",
                (UnitClass.Siege, Cultures.Alanthor)  => "Alanthor_Ballista",

                // Feraldis culture units
                (UnitClass.Melee, Cultures.Feraldis)  => "Feraldis_WarboarRider",
                (UnitClass.Ranged, Cultures.Feraldis) => "Feraldis_Hunter",
                (UnitClass.Siege, Cultures.Feraldis)  => "Feraldis_SiegeRam",

                // Non-combat units don't change with culture
                _ => GetUnitIdForClass(unitClass)
            };
        }

        /// <summary>
        /// Look up unit cost from TechTreeDB so AI pays the same price as the player.
        /// </summary>
        private static Cost GetUnitCost(string unitId)
        {
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit(unitId, out var udef))
            {
                return Cost.Of(
                    supplies: udef.cost.Supplies,
                    iron: udef.cost.Iron,
                    crystal: udef.cost.Crystal,
                    veilsteel: udef.cost.Veilsteel,
                    glow: udef.cost.Glow
                );
            }
            // Fallback costs if TechTreeDB not available
            return unitId switch
            {
                "Swordsman" => Cost.Of(supplies: 60, iron: 20),
                "Archer" => Cost.Of(supplies: 40, iron: 40),
                "Builder" => Cost.Of(supplies: 50),
                "Miner" => Cost.Of(supplies: 50),
                "Scout" => Cost.Of(supplies: 30),
                _ => Cost.Of(supplies: 50)
            };
        }

        private void OrganizeArmies(ref SystemState state, Faction faction,
            ref AIMilitaryState militaryState, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;

            // Count existing armies
            int armyCount = 0;
            foreach (var army in SystemAPI.Query<RefRO<AIArmy>>())
            {
                if (army.ValueRO.Owner == faction)
                    armyCount++;
            }

            militaryState.ArmiesCount = armyCount;

            // Find unassigned military units
            var unassigned = new NativeList<Entity>(Allocator.Temp);

            foreach (var (unitTag, factionTag, entity) in
                SystemAPI.Query<RefRO<UnitTag>, RefRO<FactionTag>>()
                .WithNone<ArmyTag, BattalionMemberData>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;

                if (unitTag.ValueRO.Class == UnitClass.Melee ||
                    unitTag.ValueRO.Class == UnitClass.Ranged ||
                    unitTag.ValueRO.Class == UnitClass.Siege)
                {
                    unassigned.Add(entity);
                }
            }

            AILogger.Log(faction, "MILITARY", $"Armies:{armyCount}, unassigned military:{unassigned.Length} (need {MIN_ARMY_SIZE} to form)");

            // Create new army if enough unassigned units
            if (unassigned.Length >= MIN_ARMY_SIZE)
            {
                AILogger.Log(faction, "MILITARY", $"Forming new army — {unassigned.Length} unassigned units available (need {MIN_ARMY_SIZE})");
                int armyId = GetNextArmyId(faction);

                var armyEntity = ecb.CreateEntity();
                ecb.AddComponent(armyEntity, new AIArmy
                {
                    ArmyId = armyId,
                    Owner = faction,
                    MissionEntity = Entity.Null,
                    Position = float3.zero,
                    TotalStrength = 0,
                    IsEngaging = 0,
                    IsRetreating = 0
                });
                ecb.AddComponent(armyEntity, new FactionTag { Value = faction });
                var armyUnits = ecb.AddBuffer<ArmyUnit>(armyEntity);

                int assigned = 0;
                for (int i = 0; i < unassigned.Length && assigned < MAX_ARMY_SIZE; i++)
                {
                    Entity unit = unassigned[i];

                    int strength = 1;
                    if (em.HasComponent<BattalionLeader>(unit) && em.HasBuffer<BattalionMember>(unit))
                    {
                        // Battalion leader strength = sum of member damage values
                        var members = em.GetBuffer<BattalionMember>(unit);
                        for (int m = 0; m < members.Length; m++)
                        {
                            if (members[m].Value != Entity.Null && em.Exists(members[m].Value)
                                && em.HasComponent<Damage>(members[m].Value))
                                strength += em.GetComponentData<Damage>(members[m].Value).Value;
                        }
                    }
                    else if (em.HasComponent<Damage>(unit))
                        strength = em.GetComponentData<Damage>(unit).Value;

                    armyUnits.Add(new ArmyUnit { Unit = unit, Strength = strength });

                    ecb.AddComponent(unit, new ArmyTag { ArmyId = armyId, ArmyEntity = armyEntity });
                    assigned++;
                }

            }

            unassigned.Dispose();
        }

        private void RequestBarracks(ref SystemState state, Faction faction)
        {
            foreach (var (brain, buildReqs, entity) in
                SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.Owner != faction) continue;

                bool exists = false;
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].BuildingType.Equals("Barracks"))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    float3 location = FindBarracksLocation(ref state, faction);

                    buildReqs.Add(new BuildRequest
                    {
                        BuildingType = "Barracks",
                        DesiredPosition = location,
                        Priority = 6,
                        Assigned = 0,
                        AssignedBuilder = Entity.Null
                    });
                }

                break;
            }
        }

        private float3 FindBarracksLocation(ref SystemState state, Faction faction)
        {
            foreach (var (factionTag, transform, buildingTag) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && buildingTag.ValueRO.IsBase == 1)
                {
                    return transform.ValueRO.Position + new float3(10, 0, -5);
                }
            }

            return new float3(15, 0, 15);
        }

        private void RequestMilitaryResources(ref SystemState state, Faction faction,
            AIMilitaryState militaryState, DynamicBuffer<ResourceRequest> resourceReqs)
        {
            int neededSupplies = militaryState.TotalSoldiers * 20;
            int neededIron = militaryState.TotalArchers * 15;

            if (neededSupplies > 0 || neededIron > 0)
            {
                resourceReqs.Add(new ResourceRequest
                {
                    Supplies = neededSupplies,
                    Iron = neededIron,
                    Crystal = 0,
                    Veilsteel = 0,
                    Glow = 0,
                    Priority = 5,
                    Requester = Entity.Null,
                    Approved = 0
                });
            }
        }

        private void CheckPopulationNeeds(ref SystemState state, Faction faction)
        {
            int popCurrent = 0, popMax = 0;
            foreach (var (tag, pop) in SystemAPI.Query<RefRO<FactionTag>, RefRO<FactionPopulation>>())
            {
                if (tag.ValueRO.Value == faction)
                {
                    popCurrent = pop.ValueRO.Current;
                    popMax = pop.ValueRO.Max;
                    break;
                }
            }

            if (popCurrent < popMax - POP_HEADROOM) return; // Still have room

            AILogger.Log(faction, "MILITARY", $"Pop near cap ({popCurrent}/{popMax}), checking if need Hut");

            // Count existing huts
            int hutCount = 0;
            foreach (var (hutTag, factionTag) in SystemAPI.Query<RefRO<HutTag>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == faction)
                    hutCount++;
            }

            if (hutCount >= MAX_HUTS)
            {
                AILogger.Log(faction, "MILITARY", $"Already at max huts ({hutCount}/{MAX_HUTS})");
                return;
            }

            AILogger.Log(faction, "MILITARY", $"Requesting Hut (have {hutCount}/{MAX_HUTS})");
            RequestHut(ref state, faction);
        }

        private void RequestHut(ref SystemState state, Faction faction)
        {
            foreach (var (brain, buildReqs, entity) in
                SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<BuildRequest>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.Owner != faction) continue;

                // Check if a Hut request already exists
                bool exists = false;
                for (int i = 0; i < buildReqs.Length; i++)
                {
                    if (buildReqs[i].BuildingType.Equals("Hut"))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    float3 location = FindHutLocation(ref state, faction);

                    buildReqs.Add(new BuildRequest
                    {
                        BuildingType = "Hut",
                        DesiredPosition = location,
                        Priority = 7,
                        Assigned = 0,
                        AssignedBuilder = Entity.Null
                    });
                }

                break;
            }
        }

        private float3 FindHutLocation(ref SystemState state, Faction faction)
        {
            foreach (var (factionTag, transform, buildingTag) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && buildingTag.ValueRO.IsBase == 1)
                {
                    // Fix #230: Unity.Mathematics.Random with seed 0 produces
                    // all zeros. At ElapsedTime==0 for Faction.Blue (index 0)
                    // this expression was 0. Guard by forcing 1 on underflow.
                    uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction);
                    if (seed == 0) seed = 1;
                    var random = new Unity.Mathematics.Random(seed);
                    float angle = random.NextFloat(0, math.PI * 2);
                    float distance = random.NextFloat(10, 20);
                    return transform.ValueRO.Position + new float3(
                        math.cos(angle) * distance, 0, math.sin(angle) * distance);
                }
            }
            return new float3(10, 0, 10);
        }

        private int GetNextArmyId(Faction faction)
        {
            int factionInt = (int)faction;
            if (!_nextArmyId.TryGetValue(factionInt, out int id))
            {
                id = 0;
            }
            _nextArmyId[factionInt] = id + 1;
            return id;
        }
    }

    struct RecruitmentRequestComparer : IComparer<RecruitmentRequest>
    {
        public int Compare(RecruitmentRequest a, RecruitmentRequest b)
        {
            return b.Priority.CompareTo(a.Priority);
        }
    }
}