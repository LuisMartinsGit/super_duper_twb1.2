// AIMissionManager.cs
// Creates strategic missions based on scouting intel and personality
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIScoutingBehavior))]
    public partial struct AIMissionManager : ISystem
    {
        private const float MISSION_UPDATE_INTERVAL = 4.0f;
        private const int MIN_DEFENSE_STRENGTH = 3;
        private const int MIN_ATTACK_STRENGTH = 5;

        private NativeHashMap<int, int> _nextMissionId;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
            _nextMissionId = new NativeHashMap<int, int>(8, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_nextMissionId.IsCreated)
                _nextMissionId.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (brain, missionState, sightings, sharedKnowledge, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AIMissionState>,
                    DynamicBuffer<EnemySighting>, RefRW<AISharedKnowledge>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var state_val = missionState.ValueRW;

                if (time >= state_val.LastMissionUpdate + state_val.MissionUpdateInterval)
                {
                    state_val.LastMissionUpdate = time;

                    EvaluateStrategicSituation(ref state, brain.ValueRO, sightings, sharedKnowledge.ValueRO);
                    CreateMissions(ref state, brain.ValueRO, sightings, sharedKnowledge.ValueRO, ecb);
                }

                // CRITICAL: Write back the modified mission state
                // Without this, LastMissionUpdate resets to 0 every frame
                missionState.ValueRW = state_val;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        private void EvaluateStrategicSituation(ref SystemState state, AIBrain brain,
            DynamicBuffer<EnemySighting> sightings, AISharedKnowledge knowledge)
        {
            int enemyStrength = knowledge.EnemyEstimatedStrength;
            int ownStrength = knowledge.OwnMilitaryStrength;

            if (sightings.Length > 0)
            {
                float strengthRatio = ownStrength > 0 ? (float)enemyStrength / ownStrength : 1f;
            }
        }

        private void CreateMissions(ref SystemState state, AIBrain brain,
            DynamicBuffer<EnemySighting> sightings, AISharedKnowledge knowledge,
            EntityCommandBuffer ecb)
        {
            int availableStrength = GetAvailableArmyStrength(ref state, brain.Owner);

            // Count existing armies
            int armyCount = 0;
            foreach (var army in SystemAPI.Query<RefRO<AIArmy>>())
            {
                if (army.ValueRO.Owner == brain.Owner) armyCount++;
            }

            // Count existing missions
            int activeMissions = 0;
            foreach (var (mission, fTag) in SystemAPI.Query<RefRO<AIMission>, RefRO<FactionTag>>())
            {
                if (fTag.ValueRO.Value == brain.Owner && mission.ValueRO.Status != MissionStatus.Completed)
                    activeMissions++;
            }

            AILogger.Log(brain.Owner, "MISSION",
                $"Armies:{armyCount} strength:{availableStrength}, sightings:{sightings.Length}, activeMissions:{activeMissions}, personality:{brain.Personality}");

            CreateDefenseMission(ref state, brain, availableStrength, ecb);

            // Attack when we have enough strength — lower threshold for aggressive AI
            int attackThreshold = MIN_ATTACK_STRENGTH;
            if (brain.Personality == AIPersonality.Balanced || brain.Personality == AIPersonality.Defensive)
                attackThreshold = MIN_ATTACK_STRENGTH * 2;

            if (availableStrength >= attackThreshold && sightings.Length > 0)
            {
                AILogger.Log(brain.Owner, "MISSION",
                    $"Creating attack mission — strength:{availableStrength} >= threshold:{attackThreshold}, sightings:{sightings.Length}");
                CreateAttackMission(ref state, brain, sightings, availableStrength, ecb);
            }
            else if (sightings.Length == 0 && availableStrength >= attackThreshold)
            {
                AILogger.Log(brain.Owner, "MISSION", "Attack blocked — no enemy sightings");
            }
            else if (availableStrength < attackThreshold)
            {
                AILogger.Log(brain.Owner, "MISSION",
                    $"Attack blocked — strength:{availableStrength} < threshold:{attackThreshold}");
            }

            if (brain.Personality == AIPersonality.Aggressive || brain.Personality == AIPersonality.Rush)
            {
                CreateRaidMissions(ref state, brain, sightings, availableStrength, ecb);
            }

            if (knowledge.OwnEconomicStrength > 1000)
            {
                CreateExpansionMission(ref state, brain, ecb);
            }
        }

        private int GetAvailableArmyStrength(ref SystemState state, Faction faction)
        {
            int strength = 0;

            foreach (var (army, entity) in SystemAPI.Query<RefRO<AIArmy>>().WithEntityAccess())
            {
                if (army.ValueRO.Owner == faction)
                    strength += army.ValueRO.TotalStrength;
            }

            return strength;
        }

        private void CreateDefenseMission(ref SystemState state, AIBrain brain, int availableStrength,
            EntityCommandBuffer ecb)
        {
            bool exists = false;
            foreach (var (mission, factionTag) in SystemAPI.Query<RefRO<AIMission>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == brain.Owner &&
                    mission.ValueRO.Type == MissionType.Defend &&
                    mission.ValueRO.Status != MissionStatus.Completed)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                float3 basePos = GetBasePosition(ref state, brain.Owner);

                int missionId = GetNextMissionId(brain.Owner);
                var missionEntity = ecb.CreateEntity();

                ecb.AddComponent(missionEntity, new AIMission
                {
                    MissionId = missionId,
                    Type = MissionType.Defend,
                    Status = MissionStatus.Active,
                    TargetPosition = basePos,
                    TargetFaction = brain.Owner,
                    RequiredStrength = MIN_DEFENSE_STRENGTH,
                    AssignedStrength = 0,
                    CreatedTime = SystemAPI.Time.ElapsedTime,
                    LastUpdateTime = SystemAPI.Time.ElapsedTime,
                    Priority = 10
                });

                ecb.AddComponent(missionEntity, new FactionTag { Value = brain.Owner });
                ecb.AddBuffer<AssignedArmy>(missionEntity);
            }
        }

        private void CreateAttackMission(ref SystemState state, AIBrain brain,
            DynamicBuffer<EnemySighting> sightings, int availableStrength, EntityCommandBuffer ecb)
        {
            EnemySighting bestTarget = default;
            int highestPriority = 0;

            for (int i = 0; i < sightings.Length; i++)
            {
                var sighting = sightings[i];
                int priority = sighting.EstimatedStrength;

                if (sighting.IsBase == 1)
                    priority += 50;

                if (priority > highestPriority)
                {
                    highestPriority = priority;
                    bestTarget = sighting;
                }
            }

            if (highestPriority == 0) return;

            bool missionExists = false;
            foreach (var (mission, factionTag) in SystemAPI.Query<RefRO<AIMission>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == brain.Owner &&
                    mission.ValueRO.Type == MissionType.Attack &&
                    mission.ValueRO.Status != MissionStatus.Completed)
                {
                    float dist = math.distance(mission.ValueRO.TargetPosition, bestTarget.Position);
                    if (dist < 20f)
                    {
                        missionExists = true;
                        break;
                    }
                }
            }

            if (!missionExists)
            {
                int missionId = GetNextMissionId(brain.Owner);
                var missionEntity = ecb.CreateEntity();

                ecb.AddComponent(missionEntity, new AIMission
                {
                    MissionId = missionId,
                    Type = MissionType.Attack,
                    Status = MissionStatus.Pending,
                    TargetPosition = bestTarget.Position,
                    TargetFaction = bestTarget.EnemyFaction,
                    RequiredStrength = math.max(bestTarget.EstimatedStrength * 2, MIN_ATTACK_STRENGTH),
                    AssignedStrength = 0,
                    CreatedTime = SystemAPI.Time.ElapsedTime,
                    LastUpdateTime = SystemAPI.Time.ElapsedTime,
                    Priority = bestTarget.IsBase == 1 ? 8 : 6
                });

                ecb.AddComponent(missionEntity, new FactionTag { Value = brain.Owner });
                ecb.AddBuffer<AssignedArmy>(missionEntity);
            }
        }

        private void CreateRaidMissions(ref SystemState state, AIBrain brain,
            DynamicBuffer<EnemySighting> sightings, int availableStrength, EntityCommandBuffer ecb)
        {
            for (int i = 0; i < sightings.Length; i++)
            {
                var sighting = sightings[i];
                if (sighting.IsBase == 0) continue;
                if (sighting.EstimatedStrength > 30) continue;

                bool exists = false;
                foreach (var (mission, factionTag) in SystemAPI.Query<RefRO<AIMission>, RefRO<FactionTag>>())
                {
                    if (factionTag.ValueRO.Value == brain.Owner &&
                        mission.ValueRO.Type == MissionType.Raid)
                    {
                        float dist = math.distance(mission.ValueRO.TargetPosition, sighting.Position);
                        if (dist < 15f)
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (!exists && availableStrength >= 10)
                {
                    int missionId = GetNextMissionId(brain.Owner);
                    var missionEntity = ecb.CreateEntity();

                    ecb.AddComponent(missionEntity, new AIMission
                    {
                        MissionId = missionId,
                        Type = MissionType.Raid,
                        Status = MissionStatus.Pending,
                        TargetPosition = sighting.Position,
                        TargetFaction = sighting.EnemyFaction,
                        RequiredStrength = 10,
                        AssignedStrength = 0,
                        CreatedTime = SystemAPI.Time.ElapsedTime,
                        LastUpdateTime = SystemAPI.Time.ElapsedTime,
                        Priority = 4
                    });

                    ecb.AddComponent(missionEntity, new FactionTag { Value = brain.Owner });
                    ecb.AddBuffer<AssignedArmy>(missionEntity);

                    break;
                }
            }
        }

        private void CreateExpansionMission(ref SystemState state, AIBrain brain, EntityCommandBuffer ecb)
        {
            bool exists = false;
            foreach (var (mission, factionTag) in SystemAPI.Query<RefRO<AIMission>, RefRO<FactionTag>>())
            {
                if (factionTag.ValueRO.Value == brain.Owner &&
                    mission.ValueRO.Type == MissionType.Expand &&
                    mission.ValueRO.Status != MissionStatus.Completed)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                float3 basePos = GetBasePosition(ref state, brain.Owner);
                float3 expansionPos = basePos + new float3(60, 0, 60);

                int missionId = GetNextMissionId(brain.Owner);
                var missionEntity = ecb.CreateEntity();

                ecb.AddComponent(missionEntity, new AIMission
                {
                    MissionId = missionId,
                    Type = MissionType.Expand,
                    Status = MissionStatus.Pending,
                    TargetPosition = expansionPos,
                    TargetFaction = brain.Owner,
                    RequiredStrength = 15,
                    AssignedStrength = 0,
                    CreatedTime = SystemAPI.Time.ElapsedTime,
                    LastUpdateTime = SystemAPI.Time.ElapsedTime,
                    Priority = 5
                });

                ecb.AddComponent(missionEntity, new FactionTag { Value = brain.Owner });
                ecb.AddBuffer<AssignedArmy>(missionEntity);
            }
        }

        private float3 GetBasePosition(ref SystemState state, Faction faction)
        {
            foreach (var (factionTag, transform, building) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && building.ValueRO.IsBase == 1)
                    return transform.ValueRO.Position;
            }
            return float3.zero;
        }

        private int GetNextMissionId(Faction faction)
        {
            int factionInt = (int)faction;
            if (!_nextMissionId.TryGetValue(factionInt, out int id))
            {
                id = 0;
            }
            _nextMissionId[factionInt] = id + 1;
            return id;
        }
    }
}