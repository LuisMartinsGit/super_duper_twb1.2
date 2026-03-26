// AIDefenseBehavior.cs
// Reactive defense system - responds to threats approaching the base
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Handles reactive defense behavior for AI factions.
    /// Detects threats approaching the base and rallies defense forces.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIScoutingBehavior))]
    public partial struct AIDefenseBehavior : ISystem
    {
        private const float DEFENSE_CHECK_INTERVAL = 1.0f;
        private const float THREAT_DETECTION_RADIUS = 50f;
        private const float EMERGENCY_RADIUS = 25f;
        private const float RALLY_DISTANCE = 10f;

        private double _lastCheckTime;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
            _lastCheckTime = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            double currentTime = SystemAPI.Time.ElapsedTime;
            if (currentTime - _lastCheckTime < DEFENSE_CHECK_INTERVAL)
                return;
            _lastCheckTime = currentTime;

            float time = (float)currentTime;
            var em = state.EntityManager;

            // Collect all move commands to issue AFTER iteration completes
            var deferredMoves = new NativeList<DeferredMove>(Allocator.Temp);

            foreach (var (brain, sharedKnowledge, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AISharedKnowledge>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                float3 basePos = GetBasePosition(ref state, brain.ValueRO.Owner);
                if (basePos.Equals(float3.zero)) continue;

                // Detect threats near base
                var threats = DetectThreats(ref state, brain.ValueRO.Owner, basePos);

                if (threats.Length > 0)
                {
                    // Calculate total threat level
                    int totalThreat = 0;
                    float3 avgThreatPos = float3.zero;
                    float closestDist = float.MaxValue;

                    for (int i = 0; i < threats.Length; i++)
                    {
                        totalThreat += threats[i].Strength;
                        avgThreatPos += threats[i].Position;

                        float dist = math.distance(basePos, threats[i].Position);
                        if (dist < closestDist)
                            closestDist = dist;
                    }

                    avgThreatPos /= threats.Length;

                    // Update shared knowledge
                    var knowledge = sharedKnowledge.ValueRW;
                    knowledge.EnemyLastKnownPosition = avgThreatPos;
                    knowledge.EnemyLastSeenTime = SystemAPI.Time.ElapsedTime;
                    knowledge.EnemyEstimatedStrength = totalThreat;

                    // Emergency response if threat is very close
                    if (closestDist < EMERGENCY_RADIUS)
                    {
                        CollectEmergencyDefenders(ref state, brain.ValueRO.Owner, avgThreatPos, ref deferredMoves);
                    }
                    // Standard defensive rally
                    else if (closestDist < THREAT_DETECTION_RADIUS)
                    {
                        CollectRallyDefenders(ref state, brain.ValueRO.Owner, basePos, avgThreatPos, ref deferredMoves);
                    }
                }

                threats.Dispose();
            }

            // Issue all deferred move commands now that iteration is complete
            for (int i = 0; i < deferredMoves.Length; i++)
            {
                AICommandAdapter.IssueMove(em, deferredMoves[i].Unit, deferredMoves[i].Destination);
            }

            deferredMoves.Dispose();
        }

        private struct ThreatInfo
        {
            public Entity Entity;
            public float3 Position;
            public int Strength;
            public Faction Faction;
        }

        private struct DeferredMove
        {
            public Entity Unit;
            public float3 Destination;
        }

        private NativeList<ThreatInfo> DetectThreats(ref SystemState state, Faction myFaction, float3 basePos)
        {
            var threats = new NativeList<ThreatInfo>(Allocator.Temp);

            // Detect enemy units near base
            foreach (var (factionTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == myFaction) continue;

                float dist = math.distance(basePos, transform.ValueRO.Position);
                if (dist <= THREAT_DETECTION_RADIUS)
                {
                    int strength = 1;

                    // Get combat power if available
                    var em = state.EntityManager;
                    if (em.HasComponent<CombatPower>(entity))
                    {
                        strength = em.GetComponentData<CombatPower>(entity).Value;
                    }
                    else if (em.HasComponent<Damage>(entity))
                    {
                        strength = em.GetComponentData<Damage>(entity).Value;
                    }

                    threats.Add(new ThreatInfo
                    {
                        Entity = entity,
                        Position = transform.ValueRO.Position,
                        Strength = strength,
                        Faction = factionTag.ValueRO.Value
                    });
                }
            }

            return threats;
        }

        private void CollectEmergencyDefenders(ref SystemState state, Faction faction,
            float3 threatPos, ref NativeList<DeferredMove> deferredMoves)
        {
            var em = state.EntityManager;
            float3 basePos = GetBasePosition(ref state, faction);

            Debug.Log($"[AIDefenseBehavior] {faction} EMERGENCY DEFENSE triggered! Threat at {threatPos}");

            float3 interceptPos = math.lerp(basePos, threatPos, 0.3f);

            foreach (var (factionTag, unitTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<UnitTag>, RefRO<LocalTransform>>()
                .WithNone<MinerTag>()            // Don't pull miners from mining
                .WithNone<CanBuild>()            // Don't pull builders from construction
                .WithNone<BattalionMemberData>() // Members follow their leader, not individual commands
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                if (unitTag.ValueRO.Class == UnitClass.Scout) continue; // Scouts keep exploring
                // Battalion leaders don't have Damage — check members for combat capability
                if (!em.HasComponent<Damage>(entity) && !em.HasComponent<BattalionLeader>(entity)) continue;

                deferredMoves.Add(new DeferredMove { Unit = entity, Destination = interceptPos });
            }
        }

        private void CollectRallyDefenders(ref SystemState state, Faction faction,
            float3 basePos, float3 threatPos, ref NativeList<DeferredMove> deferredMoves)
        {
            var em = state.EntityManager;

            // Calculate rally point (between base and threat, closer to base)
            float3 rallyPoint = math.lerp(basePos, threatPos, 0.25f);

            foreach (var (factionTag, unitTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<UnitTag>, RefRO<LocalTransform>>()
                .WithNone<ArmyTag>()             // Not already in an army
                .WithNone<MinerTag>()            // Don't pull miners from mining
                .WithNone<CanBuild>()            // Don't pull builders from construction
                .WithNone<BattalionMemberData>() // Members follow their leader, not individual commands
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                if (unitTag.ValueRO.Class == UnitClass.Scout) continue; // Scouts keep exploring
                if (!em.HasComponent<Damage>(entity) && !em.HasComponent<BattalionLeader>(entity)) continue;

                // Check if unit is idle (no current target or destination)
                bool isIdle = true;

                if (em.HasComponent<Target>(entity))
                {
                    var target = em.GetComponentData<Target>(entity);
                    if (target.Value != Entity.Null && em.Exists(target.Value))
                        isIdle = false;
                }

                if (isIdle && em.HasComponent<DesiredDestination>(entity))
                {
                    var dest = em.GetComponentData<DesiredDestination>(entity);
                    if (dest.Has == 1)
                        isIdle = false;
                }

                if (isIdle)
                {
                    float distToRally = math.distance(transform.ValueRO.Position, rallyPoint);
                    if (distToRally > RALLY_DISTANCE)
                    {
                        deferredMoves.Add(new DeferredMove { Unit = entity, Destination = rallyPoint });
                    }
                }
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
    }
}