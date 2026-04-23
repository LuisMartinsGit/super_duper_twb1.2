// AIScoutingBehavior.cs
// Implements zone-based exploration system for systematic map scouting
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIMilitaryManager))]
    public partial struct AIScoutingBehavior : ISystem
    {
        private const float SCOUT_UPDATE_INTERVAL = 2.0f;
        private const int DESIRED_SCOUTS = 2;
        private const float SIGHTING_EXPIRY_TIME = 30.0f;
        private const float MAP_EXPLORATION_RADIUS = 100f;

        // Exploration zone settings
        private const int ZONES_PER_AXIS = 5;
        private const float ZONE_SIZE = 60f;
        private const float ZONE_ARRIVAL_DISTANCE = 15f;
        private const float ZONE_REVISIT_TIME = 120f;
        private const float MIN_ASSIGNMENT_DURATION = 5f;
        private const float MERGE_DISTANCE = 20f;

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
            double elapsedTime = SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (brain, scoutingState, scoutAssignments, explorationZones, sightings, sharedKnowledge, entity)
                     in SystemAPI.Query<RefRO<AIBrain>, RefRW<AIScoutingState>,
                         DynamicBuffer<ScoutAssignment>, DynamicBuffer<ExplorationZone>,
                         DynamicBuffer<EnemySighting>, RefRW<AISharedKnowledge>>()
                     .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var state_val = scoutingState.ValueRW;

                if (explorationZones.Length == 0)
                {
                    InitializeExplorationZones(ref state, brain.ValueRO.Owner, explorationZones);
                }

                if (time >= state_val.LastScoutUpdate + state_val.ScoutUpdateInterval)
                {
                    state_val.LastScoutUpdate = time;

                    UpdateScoutAssignments(ref state, brain.ValueRO.Owner, ref state_val, scoutAssignments, ecb);
                    ScanForEnemies(ref state, brain.ValueRO.Owner, sightings, elapsedTime);
                    CleanupOldSightings(sightings, elapsedTime);
                    UpdateSharedKnowledge(ref state, brain.ValueRO.Owner, sightings, ref sharedKnowledge.ValueRW);
                    CalculateMapExploration(ref state, brain.ValueRO.Owner, ref state_val, explorationZones);
                }

                UpdateZoneVisits(ref state, brain.ValueRO.Owner, scoutAssignments, explorationZones, elapsedTime);
                AssignScoutPatrols(ref state, brain.ValueRO.Owner, scoutAssignments, explorationZones, ecb, elapsedTime);

                scoutingState.ValueRW = state_val;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        private void InitializeExplorationZones(ref SystemState state, Faction faction,
            DynamicBuffer<ExplorationZone> zones)
        {
            float3 basePos = GetBasePosition(ref state, faction);

            float halfMapSize = (ZONES_PER_AXIS * ZONE_SIZE) / 2f;
            float3 gridStart = basePos + new float3(-halfMapSize, 0, -halfMapSize);

            for (int x = 0; x < ZONES_PER_AXIS; x++)
            {
                for (int z = 0; z < ZONES_PER_AXIS; z++)
                {
                    float3 zoneCenter = gridStart + new float3(
                        x * ZONE_SIZE + ZONE_SIZE / 2f,
                        0,
                        z * ZONE_SIZE + ZONE_SIZE / 2f
                    );

                    zones.Add(new ExplorationZone
                    {
                        CenterPosition = zoneCenter,
                        LastVisitedTime = 0,
                        IsExplored = 0,
                        VisitCount = 0
                    });
                }
            }

        }

        private void UpdateScoutAssignments(ref SystemState state, Faction faction,
            ref AIScoutingState scoutingState, DynamicBuffer<ScoutAssignment> assignments,
            EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;

            // Clean up dead scout entries
            for (int i = assignments.Length - 1; i >= 0; i--)
            {
                if (!em.Exists(assignments[i].ScoutUnit))
                    assignments.RemoveAt(i);
            }

            // Register any scout units that aren't yet in the assignments buffer
            foreach (var (unitTag, factionTag, entity) in
                     SystemAPI.Query<RefRO<UnitTag>, RefRO<FactionTag>>()
                     .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                if (unitTag.ValueRO.Class != UnitClass.Scout) continue;

                bool alreadyRegistered = false;
                for (int i = 0; i < assignments.Length; i++)
                {
                    if (assignments[i].ScoutUnit == entity)
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (!alreadyRegistered)
                {
                    AILogger.Log(faction, "SCOUT", $"Registering new scout unit for patrol");
                    assignments.Add(new ScoutAssignment
                    {
                        ScoutUnit = entity,
                        TargetArea = float3.zero,
                        IsActive = 0,
                        AssignedZoneIndex = -1,
                        AssignmentTime = 0,
                        DistanceToTarget = 0
                    });
                }
            }

            int activeScouts = 0;
            for (int i = 0; i < assignments.Length; i++)
            {
                if (assignments[i].IsActive == 1)
                    activeScouts++;
            }

            scoutingState.ActiveScouts = activeScouts;
            scoutingState.DesiredScouts = DESIRED_SCOUTS;

            AILogger.Log(faction, "SCOUT", $"Scouts: {assignments.Length} registered, {activeScouts} active, {DESIRED_SCOUTS} desired");

            if (activeScouts < DESIRED_SCOUTS && assignments.Length < DESIRED_SCOUTS)
            {
                RequestScouts(ref state, faction, DESIRED_SCOUTS - assignments.Length);
            }
        }

        private void RequestScouts(ref SystemState state, Faction faction, int count)
        {
            // Count ALL scouts: alive units + training queue + pending recruitment
            int totalScouts = 0;

            // Alive scout units
            foreach (var (unitTag, fTag) in SystemAPI.Query<RefRO<UnitTag>, RefRO<FactionTag>>())
            {
                if (fTag.ValueRO.Value == faction && unitTag.ValueRO.Class == UnitClass.Scout)
                    totalScouts++;
            }

            // Scouts in training queues
            foreach (var (fTag, trainQueue) in SystemAPI.Query<RefRO<FactionTag>, DynamicBuffer<TrainQueueItem>>())
            {
                if (fTag.ValueRO.Value != faction) continue;
                for (int i = 0; i < trainQueue.Length; i++)
                {
                    if (trainQueue[i].UnitId.Equals("Scout"))
                        totalScouts++;
                }
            }

            if (totalScouts >= DESIRED_SCOUTS) return; // Already have enough

            foreach (var (brain, recruitReqs, entity) in
                     SystemAPI.Query<RefRO<AIBrain>, DynamicBuffer<RecruitmentRequest>>()
                     .WithEntityAccess())
            {
                if (brain.ValueRO.Owner != faction) continue;

                // Count pending scout recruitment requests
                for (int i = 0; i < recruitReqs.Length; i++)
                {
                    if (recruitReqs[i].UnitType == UnitClass.Scout)
                        totalScouts += recruitReqs[i].Quantity;
                }

                if (totalScouts >= DESIRED_SCOUTS) break; // Already have enough

                int needed = DESIRED_SCOUTS - totalScouts;
                recruitReqs.Add(new RecruitmentRequest
                {
                    UnitType = UnitClass.Scout,
                    Quantity = needed,
                    Priority = 6,
                    RequestingManager = entity
                });

                break;
            }
        }

        private void AssignScoutPatrols(ref SystemState state, Faction faction,
            DynamicBuffer<ScoutAssignment> assignments, DynamicBuffer<ExplorationZone> zones,
            EntityCommandBuffer ecb, double elapsedTime)
        {
            var em = state.EntityManager;

            for (int i = 0; i < assignments.Length; i++)
            {
                var assignment = assignments[i];
                if (!em.Exists(assignment.ScoutUnit)) continue;

                bool needsNewTarget = false;

                if (assignment.AssignedZoneIndex < 0)
                {
                    needsNewTarget = true;
                }
                else if (assignment.DistanceToTarget < ZONE_ARRIVAL_DISTANCE)
                {
                    double timeSinceAssignment = elapsedTime - assignment.AssignmentTime;
                    if (timeSinceAssignment > MIN_ASSIGNMENT_DURATION)
                    {
                        needsNewTarget = true;
                    }
                }
                else if (em.HasComponent<DesiredDestination>(assignment.ScoutUnit))
                {
                    var dest = em.GetComponentData<DesiredDestination>(assignment.ScoutUnit);
                    if (dest.Has == 0)
                    {
                        double timeSinceAssignment = elapsedTime - assignment.AssignmentTime;
                        if (timeSinceAssignment > MIN_ASSIGNMENT_DURATION)
                        {
                            needsNewTarget = true;
                        }
                    }
                }

                if (needsNewTarget)
                {
                    int bestZoneIndex = FindLeastExploredZone(zones, assignments, elapsedTime);

                    if (bestZoneIndex >= 0)
                    {
                        var targetZone = zones[bestZoneIndex];
                        var scoutPos = em.GetComponentData<LocalTransform>(assignment.ScoutUnit).Position;

                        assignment.TargetArea = targetZone.CenterPosition;
                        assignment.IsActive = 1;
                        assignment.AssignedZoneIndex = bestZoneIndex;
                        assignment.AssignmentTime = elapsedTime;
                        assignment.DistanceToTarget = math.distance(scoutPos, targetZone.CenterPosition);
                        assignments[i] = assignment;

                        ecb.SetComponent(assignment.ScoutUnit, new DesiredDestination
                        {
                            Position = targetZone.CenterPosition,
                            Has = 1
                        });

                    }
                }
            }
        }

        private void UpdateZoneVisits(ref SystemState state, Faction faction,
            DynamicBuffer<ScoutAssignment> assignments, DynamicBuffer<ExplorationZone> zones,
            double elapsedTime)
        {
            var em = state.EntityManager;

            for (int i = 0; i < assignments.Length; i++)
            {
                var assignment = assignments[i];
                if (!em.Exists(assignment.ScoutUnit)) continue;
                if (assignment.AssignedZoneIndex < 0 || assignment.AssignedZoneIndex >= zones.Length) continue;

                var transform = em.GetComponentData<LocalTransform>(assignment.ScoutUnit);
                var zone = zones[assignment.AssignedZoneIndex];

                float distToZone = math.distance(transform.Position, zone.CenterPosition);

                assignment.DistanceToTarget = distToZone;
                assignments[i] = assignment;

                if (distToZone < ZONE_ARRIVAL_DISTANCE)
                {
                    if (zone.IsExplored == 0 || elapsedTime - zone.LastVisitedTime > 1f)
                    {
                        zone.LastVisitedTime = (float)elapsedTime;
                        zone.IsExplored = 1;
                        zone.VisitCount++;
                        zones[assignment.AssignedZoneIndex] = zone;

                    }
                }
            }
        }

        private int FindLeastExploredZone(DynamicBuffer<ExplorationZone> zones,
            DynamicBuffer<ScoutAssignment> assignments, double currentTime)
        {
            if (zones.Length == 0) return -1;

            int bestZone = -1;
            double oldestTime = double.MaxValue;
            byte foundUnexplored = 0;

            for (int i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];

                bool alreadyAssigned = false;
                for (int j = 0; j < assignments.Length; j++)
                {
                    if (assignments[j].AssignedZoneIndex == i && assignments[j].IsActive == 1)
                    {
                        alreadyAssigned = true;
                        break;
                    }
                }
                if (alreadyAssigned) continue;

                if (zone.IsExplored == 0)
                {
                    if (foundUnexplored == 0)
                    {
                        bestZone = i;
                        foundUnexplored = 1;
                    }
                    else if (bestZone < 0)
                    {
                        bestZone = i;
                    }
                }
                else if (foundUnexplored == 0)
                {
                    double timeSinceVisit = currentTime - zone.LastVisitedTime;

                    if (timeSinceVisit > ZONE_REVISIT_TIME)
                    {
                        if (zone.LastVisitedTime < oldestTime)
                        {
                            oldestTime = zone.LastVisitedTime;
                            bestZone = i;
                        }
                    }
                }
            }

            if (bestZone < 0)
            {
                for (int i = 0; i < zones.Length; i++)
                {
                    var zone = zones[i];

                    bool alreadyAssigned = false;
                    for (int j = 0; j < assignments.Length; j++)
                    {
                        if (assignments[j].AssignedZoneIndex == i && assignments[j].IsActive == 1)
                        {
                            alreadyAssigned = true;
                            break;
                        }
                    }
                    if (alreadyAssigned) continue;

                    if (zone.LastVisitedTime < oldestTime)
                    {
                        oldestTime = zone.LastVisitedTime;
                        bestZone = i;
                    }
                }
            }

            return bestZone;
        }

        private void ScanForEnemies(ref SystemState state, Faction faction,
            DynamicBuffer<EnemySighting> sightings, double elapsedTime)
        {
            var em = state.EntityManager;

            for (int i = 0; i < sightings.Length; i++)
            {
                var sighting = sightings[i];
                if (elapsedTime - sighting.TimeStamp > SCOUT_UPDATE_INTERVAL)
                {
                    sighting.EstimatedStrength = 0;
                    sightings[i] = sighting;
                }
            }

            var observerPositions = new NativeList<float3>(Allocator.Temp);
            var observerRadii = new NativeList<float>(Allocator.Temp);

            foreach (var (factionTag, transform, los) in
                     SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<LineOfSight>>())
            {
                if (factionTag.ValueRO.Value == faction)
                {
                    observerPositions.Add(transform.ValueRO.Position);
                    observerRadii.Add(los.ValueRO.Radius);
                }
            }

            // Scan enemy PLAYER units only — crystal faction (identified by CrystalTag) is
            // handled by AICrystalHuntBehavior as a defensive farm, not an attack mission target.
            foreach (var (enemyFaction, transform, health, entity) in
                     SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<Health>>()
                     .WithAll<UnitTag>()
                     .WithEntityAccess())
            {
                if (enemyFaction.ValueRO.Value == faction) continue;
                if (em.HasComponent<CrystalTag>(entity)) continue; // Skip crystal faction units
                if (health.ValueRO.Value <= 0) continue;

                bool canSee = false;
                for (int i = 0; i < observerPositions.Length; i++)
                {
                    float dist = math.distance(observerPositions[i], transform.ValueRO.Position);
                    if (dist <= observerRadii[i])
                    {
                        canSee = true;
                        break;
                    }
                }

                if (canSee)
                {
                    int sightingIndex = FindOrCreateSighting(sightings, enemyFaction.ValueRO.Value,
                        transform.ValueRO.Position, elapsedTime, false);

                    if (sightingIndex >= 0)
                    {
                        var sighting = sightings[sightingIndex];
                        // Estimate combat power from health + damage
                        int estimatedPower = health.ValueRO.Max;
                        if (state.EntityManager.HasComponent<Damage>(entity))
                            estimatedPower += state.EntityManager.GetComponentData<Damage>(entity).Value * 5;
                        sighting.EstimatedStrength += estimatedPower;
                        sightings[sightingIndex] = sighting;
                    }
                }
            }

            foreach (var (enemyFaction, transform, building, entity) in
                     SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>()
                     .WithEntityAccess())
            {
                if (enemyFaction.ValueRO.Value == faction) continue;
                if (em.HasComponent<CrystalTag>(entity)) continue; // Skip crystal faction buildings

                bool canSee = false;
                for (int i = 0; i < observerPositions.Length; i++)
                {
                    float dist = math.distance(observerPositions[i], transform.ValueRO.Position);
                    if (dist <= observerRadii[i])
                    {
                        canSee = true;
                        break;
                    }
                }

                if (canSee)
                {
                    int sightingIndex = FindOrCreateSighting(sightings, enemyFaction.ValueRO.Value,
                        transform.ValueRO.Position, elapsedTime, true);

                    if (sightingIndex >= 0)
                    {
                        var sighting = sightings[sightingIndex];
                        sighting.EstimatedStrength += 50;
                        sightings[sightingIndex] = sighting;
                    }
                }
            }

            observerPositions.Dispose();
            observerRadii.Dispose();

            // Log sighting summary
            if (sightings.Length > 0)
            {
                int totalStr = 0;
                for (int i = 0; i < sightings.Length; i++)
                    totalStr += sightings[i].EstimatedStrength;
                AILogger.Log(faction, "SCOUT", $"Sightings: {sightings.Length} contacts, total estimated strength: {totalStr}");
            }
        }

        private int FindOrCreateSighting(DynamicBuffer<EnemySighting> sightings,
            Faction enemyFaction, float3 position, double elapsedTime, bool isBase)
        {
            for (int i = 0; i < sightings.Length; i++)
            {
                var s = sightings[i];
                if (s.EnemyFaction == enemyFaction && s.IsBase == (isBase ? (byte)1 : (byte)0))
                {
                    float dist = math.distance(s.Position, position);
                    if (dist < MERGE_DISTANCE)
                    {
                        s.Position = position;
                        s.TimeStamp = elapsedTime;
                        sightings[i] = s;
                        return i;
                    }
                }
            }

            sightings.Add(new EnemySighting
            {
                EnemyFaction = enemyFaction,
                Position = position,
                TimeStamp = elapsedTime,
                EstimatedStrength = 0,
                IsBase = isBase ? (byte)1 : (byte)0
            });

            return sightings.Length - 1;
        }

        private void CleanupOldSightings(DynamicBuffer<EnemySighting> sightings, double elapsedTime)
        {
            for (int i = sightings.Length - 1; i >= 0; i--)
            {
                if (elapsedTime - sightings[i].TimeStamp > SIGHTING_EXPIRY_TIME)
                    sightings.RemoveAt(i);
            }
        }

        private void UpdateSharedKnowledge(ref SystemState state, Faction faction,
            DynamicBuffer<EnemySighting> sightings, ref AISharedKnowledge knowledge)
        {
            int enemyBasesSpotted = 0;
            int enemyArmiesSpotted = 0;

            for (int i = 0; i < sightings.Length; i++)
            {
                if (sightings[i].EstimatedStrength > 0)
                {
                    if (sightings[i].IsBase == 1)
                        enemyBasesSpotted++;
                        enemyArmiesSpotted++;
                }
            }

            knowledge.EnemyBasesSpotted = enemyBasesSpotted;
            knowledge.EnemyArmiesSpotted = enemyArmiesSpotted;
        }

        private void CalculateMapExploration(ref SystemState state, Faction faction,
            ref AIScoutingState scoutingState, DynamicBuffer<ExplorationZone> zones)
        {
            if (zones.Length == 0)
            {
                scoutingState.MapExplorationPercent = 0f;
                return;
            }

            int exploredCount = 0;
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i].IsExplored == 1)
                    exploredCount++;
            }

            scoutingState.MapExplorationPercent = (exploredCount / (float)zones.Length) * 100f;
        }

        private float3 GetBasePosition(ref SystemState state, Faction faction)
        {
            // Fix #229: must filter by IsBase == 1 to match the Hall, not just
            // any building owned by the faction. Previously this returned the
            // first building (often a Hut or GathererHut) which meant scout
            // exploration zones were centred on an arbitrary building instead
            // of the actual base, causing scouts to cover the wrong area.
            foreach (var (factionTag, transform, building) in
                     SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && building.ValueRO.IsBase == 1)
                    return transform.ValueRO.Position;
            }
            return float3.zero;
        }
    }
}