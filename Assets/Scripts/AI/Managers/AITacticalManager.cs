// AITacticalManager.cs
// Manages armies to fulfill missions, assigns armies to missions, executes attack/defend orders
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using System.Collections.Generic;  // For IComparer<T>

namespace TheWaningBorder.AI
{
    [DisableAutoCreation] // Replaced by SimpleAISystem.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIMissionManager))]
    public partial struct AITacticalManager : ISystem
    {
        private const float TACTICAL_UPDATE_INTERVAL = 1.0f;
        private const float ENGAGEMENT_RANGE = 25f;
        private const float ENTER_ENGAGEMENT_RANGE = 20f;
        private const float EXIT_ENGAGEMENT_RANGE = 30f;
        private const float FORMATION_SPACING = 3f;

        private enum DeferredCommandType : byte { Move, Attack }

        private struct DeferredCommand
        {
            public DeferredCommandType Type;
            public Entity Unit;
            public float3 Destination;  // For Move
            public Entity Target;       // For Attack
        }

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

            // Collect all commands to issue AFTER iteration completes
            var deferredCommands = new NativeList<DeferredCommand>(Allocator.Temp);

            foreach (var (brain, tacticalState, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AITacticalState>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                var state_val = tacticalState.ValueRW;

                if (time >= state_val.LastTacticalUpdate + state_val.TacticalUpdateInterval)
                {
                    state_val.LastTacticalUpdate = time;

                    AssignArmiesToMissions(ref state, brain.ValueRO.Owner, ecb);
                    ManageArmies(ref state, brain.ValueRO.Owner, ref state_val, ref deferredCommands);
                }

                // CRITICAL: Write back the modified tactical state
                // Without this, LastTacticalUpdate resets to 0 every frame
                tacticalState.ValueRW = state_val;
            }

            ecb.Playback(em);
            ecb.Dispose();

            // Issue all deferred commands now that iteration is complete
            for (int i = 0; i < deferredCommands.Length; i++)
            {
                var cmd = deferredCommands[i];
                switch (cmd.Type)
                {
                    case DeferredCommandType.Move:
                        AICommandAdapter.IssueMove(em, cmd.Unit, cmd.Destination);
                        break;
                    case DeferredCommandType.Attack:
                        AICommandAdapter.IssueAttack(em, cmd.Unit, cmd.Target);
                        break;
                }
            }

            deferredCommands.Dispose();
        }

        private void AssignArmiesToMissions(ref SystemState state, Faction faction, EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;
            var missions = new NativeList<(Entity entity, AIMission mission)>(Allocator.Temp);

            foreach (var (mission, factionTag, entity) in
                SystemAPI.Query<RefRO<AIMission>, RefRO<FactionTag>>().WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction &&
                    (mission.ValueRO.Status == MissionStatus.Pending || mission.ValueRO.Status == MissionStatus.Active))
                {
                    missions.Add((entity, mission.ValueRO));
                }
            }

            missions.Sort(new MissionPriorityComparer());

            foreach (var (army, entity) in SystemAPI.Query<RefRW<AIArmy>>().WithEntityAccess())
            {
                if (army.ValueRO.Owner != faction) continue;
                if (army.ValueRO.MissionEntity != Entity.Null && em.Exists(army.ValueRO.MissionEntity))
                    continue;

                AILogger.Log(faction, "TACTICAL", $"Unassigned army (str={army.ValueRO.TotalStrength}), looking for mission among {missions.Length} active missions");

                for (int i = 0; i < missions.Length; i++)
                {
                    var m = missions[i];
                    if (m.mission.AssignedStrength < m.mission.RequiredStrength)
                    {
                        AILogger.Log(faction, "TACTICAL",
                            $"  Assigning army to {m.mission.Type} mission (assigned:{m.mission.AssignedStrength}/{m.mission.RequiredStrength})");

                        var armyVal = army.ValueRW;
                        armyVal.MissionEntity = m.entity;
                        army.ValueRW = armyVal;

                        var missionData = m.mission;
                        missionData.AssignedStrength += army.ValueRO.TotalStrength;
                        missionData.Status = MissionStatus.Active;

                        em.SetComponentData(m.entity, missionData);

                        if (em.HasBuffer<AssignedArmy>(m.entity))
                        {
                            var assignedArmies = em.GetBuffer<AssignedArmy>(m.entity);
                            assignedArmies.Add(new AssignedArmy
                            {
                                ArmyEntity = entity,
                                Strength = army.ValueRO.TotalStrength
                            });
                        }

                        break;
                    }
                }
            }

            missions.Dispose();
        }

        private void ManageArmies(ref SystemState state, Faction faction,
            ref AITacticalState tacticalState, ref NativeList<DeferredCommand> deferredCommands)
        {
            var em = state.EntityManager;
            int armyCount = 0;

            foreach (var (army, armyUnits, entity) in
                SystemAPI.Query<RefRW<AIArmy>, DynamicBuffer<ArmyUnit>>().WithEntityAccess())
            {
                if (army.ValueRO.Owner != faction) continue;
                armyCount++;

                UpdateArmyPosition(ref state, ref army.ValueRW, armyUnits);
                UpdateArmyStrength(ref state, ref army.ValueRW, armyUnits);

                bool hasMission = army.ValueRO.MissionEntity != Entity.Null && em.Exists(army.ValueRO.MissionEntity);
                AILogger.Log(faction, "TACTICAL",
                    $"Army #{army.ValueRO.ArmyId}: {armyUnits.Length} units, str={army.ValueRO.TotalStrength}, hasMission={hasMission}");

                if (hasMission)
                {
                    var mission = em.GetComponentData<AIMission>(army.ValueRO.MissionEntity);
                    AILogger.Log(faction, "TACTICAL",
                        $"  Executing {mission.Type} mission — target=({mission.TargetPosition.x:F0},{mission.TargetPosition.z:F0}), dist={Unity.Mathematics.math.distance(army.ValueRO.Position, mission.TargetPosition):F0}");
                    ExecuteMission(ref state, army.ValueRW, mission, armyUnits, ref deferredCommands);
                }
            }

            tacticalState.ManagedArmies = armyCount;
        }

        private void UpdateArmyPosition(ref SystemState state, ref AIArmy army, DynamicBuffer<ArmyUnit> units)
        {
            var em = state.EntityManager;
            float3 sum = float3.zero;
            int count = 0;

            for (int i = 0; i < units.Length; i++)
            {
                if (!em.Exists(units[i].Unit)) continue;
                if (!em.HasComponent<LocalTransform>(units[i].Unit)) continue;

                sum += em.GetComponentData<LocalTransform>(units[i].Unit).Position;
                count++;
            }

            if (count > 0)
                army.Position = sum / count;
        }

        private void UpdateArmyStrength(ref SystemState state, ref AIArmy army, DynamicBuffer<ArmyUnit> units)
        {
            var em = state.EntityManager;
            int strength = 0;

            for (int i = units.Length - 1; i >= 0; i--)
            {
                if (!em.Exists(units[i].Unit))
                {
                    units.RemoveAt(i);
                    continue;
                }

                strength += units[i].Strength;
            }

            army.TotalStrength = strength;
        }

        private void ExecuteMission(ref SystemState state, AIArmy army, AIMission mission,
            DynamicBuffer<ArmyUnit> armyUnits, ref NativeList<DeferredCommand> deferredCommands)
        {
            switch (mission.Type)
            {
                case MissionType.Attack:
                    ExecuteAttackMission(ref state, army, mission, armyUnits, ref deferredCommands);
                    break;
                case MissionType.Defend:
                    ExecuteDefendMission(ref state, army, mission, armyUnits, ref deferredCommands);
                    break;
                case MissionType.Raid:
                    ExecuteRaidMission(ref state, army, mission, armyUnits, ref deferredCommands);
                    break;
                case MissionType.Expand:
                    ExecuteExpansionMission(ref state, army, mission, armyUnits, ref deferredCommands);
                    break;
            }
        }

        private void ExecuteAttackMission(ref SystemState state, AIArmy army, AIMission mission,
            DynamicBuffer<ArmyUnit> armyUnits, ref NativeList<DeferredCommand> deferredCommands)
        {
            float distToTarget = math.distance(army.Position, mission.TargetPosition);
            bool isEngaging = army.IsEngaging == 1;
            float effectiveRange = isEngaging ? EXIT_ENGAGEMENT_RANGE : ENTER_ENGAGEMENT_RANGE;

            if (distToTarget > effectiveRange)
            {
                if (!isEngaging)
                {
                    CollectArmyMoves(ref state, armyUnits, mission.TargetPosition, ref deferredCommands);
                }
            }
            else
            {
                CollectEngageCommands(ref state, army, mission.TargetFaction, armyUnits, ref deferredCommands);
            }
        }

        private void ExecuteDefendMission(ref SystemState state, AIArmy army, AIMission mission,
            DynamicBuffer<ArmyUnit> armyUnits, ref NativeList<DeferredCommand> deferredCommands)
        {
            float distToDefensePoint = math.distance(army.Position, mission.TargetPosition);

            const float DEFEND_RETURN_DISTANCE = 15f;
            const float DEFEND_HOLD_DISTANCE = 12f;

            bool isHolding = distToDefensePoint <= DEFEND_HOLD_DISTANCE;

            if (distToDefensePoint > DEFEND_RETURN_DISTANCE)
            {
                CollectArmyMoves(ref state, armyUnits, mission.TargetPosition, ref deferredCommands);
            }
            else
            {
                Entity nearestEnemy = FindNearestEnemy(ref state, army.Position, army.Owner, 30f);

                if (nearestEnemy != Entity.Null)
                {
                    CollectEngageCommands(ref state, army, GetEnemyFaction(ref state, nearestEnemy), armyUnits, ref deferredCommands);
                }
                else if (!isHolding)
                {
                    CollectArmyMoves(ref state, armyUnits, mission.TargetPosition, ref deferredCommands);
                }
            }
        }

        private void ExecuteRaidMission(ref SystemState state, AIArmy army,
            AIMission mission, DynamicBuffer<ArmyUnit> armyUnits, ref NativeList<DeferredCommand> deferredCommands)
        {
            float distToTarget = math.distance(army.Position, mission.TargetPosition);

            if (distToTarget > 10f)
            {
                CollectArmyMoves(ref state, armyUnits, mission.TargetPosition, ref deferredCommands);
            }
            else
            {
                CollectEngageCommands(ref state, army, mission.TargetFaction, armyUnits, ref deferredCommands);

                if (army.TotalStrength < mission.RequiredStrength * 0.5f)
                {
                    float3 basePos = GetBasePosition(ref state, army.Owner);
                    CollectArmyMoves(ref state, armyUnits, basePos, ref deferredCommands);
                }
            }
        }

        private void ExecuteExpansionMission(ref SystemState state, AIArmy army, AIMission mission,
            DynamicBuffer<ArmyUnit> armyUnits, ref NativeList<DeferredCommand> deferredCommands)
        {
            float distToTarget = math.distance(army.Position, mission.TargetPosition);

            if (distToTarget > 10f)
            {
                CollectArmyMoves(ref state, armyUnits, mission.TargetPosition, ref deferredCommands);
            }
            else
            {
                CollectArmyMoves(ref state, armyUnits, mission.TargetPosition, ref deferredCommands);

                Entity nearestEnemy = FindNearestEnemy(ref state, army.Position, army.Owner, 25f);
                if (nearestEnemy != Entity.Null)
                {
                    CollectEngageCommands(ref state, army, GetEnemyFaction(ref state, nearestEnemy), armyUnits, ref deferredCommands);
                }
            }
        }

        /// <summary>
        /// Collect move commands for all units in an army (deferred - no structural changes).
        /// Computes formation positions matching AICommandAdapter.MoveArmy logic.
        /// </summary>
        private void CollectArmyMoves(ref SystemState state, DynamicBuffer<ArmyUnit> armyUnits,
            float3 destination, ref NativeList<DeferredCommand> deferredCommands)
        {
            var em = state.EntityManager;

            // Count valid units
            int count = 0;
            for (int i = 0; i < armyUnits.Length; i++)
            {
                if (armyUnits[i].Unit != Entity.Null && em.Exists(armyUnits[i].Unit))
                    count++;
            }
            if (count == 0) return;

            int cols = (int)math.ceil(math.sqrt(count));
            int row = 0, col = 0;

            for (int i = 0; i < armyUnits.Length; i++)
            {
                if (armyUnits[i].Unit == Entity.Null || !em.Exists(armyUnits[i].Unit)) continue;

                float3 offset = new float3(
                    (col - cols / 2f) * FORMATION_SPACING,
                    0,
                    (row - cols / 2f) * FORMATION_SPACING
                );

                deferredCommands.Add(new DeferredCommand
                {
                    Type = DeferredCommandType.Move,
                    Unit = armyUnits[i].Unit,
                    Destination = destination + offset
                });

                col++;
                if (col >= cols) { col = 0; row++; }
            }
        }

        /// <summary>
        /// Collect attack commands for army units against prioritized targets (deferred).
        /// </summary>
        private void CollectEngageCommands(ref SystemState state, AIArmy army, Faction enemyFaction,
            DynamicBuffer<ArmyUnit> armyUnits, ref NativeList<DeferredCommand> deferredCommands)
        {
            var em = state.EntityManager;
            var targets = FindEnemyTargets(ref state, army.Position, enemyFaction, ENGAGEMENT_RANGE);

            if (targets.Length == 0)
            {
                targets.Dispose();
                return;
            }

            PrioritizeTargets(ref state, targets);

            int targetIdx = 0;
            for (int i = 0; i < armyUnits.Length; i++)
            {
                if (!em.Exists(armyUnits[i].Unit)) continue;

                if (targetIdx < targets.Length)
                {
                    bool needsNewTarget = true;

                    if (em.HasComponent<Target>(armyUnits[i].Unit))
                    {
                        var currentTarget = em.GetComponentData<Target>(armyUnits[i].Unit);
                        if (currentTarget.Value != Entity.Null && em.Exists(currentTarget.Value))
                        {
                            needsNewTarget = false;
                        }
                    }

                    if (needsNewTarget)
                    {
                        deferredCommands.Add(new DeferredCommand
                        {
                            Type = DeferredCommandType.Attack,
                            Unit = armyUnits[i].Unit,
                            Target = targets[targetIdx].Entity
                        });
                    }

                    targetIdx = (targetIdx + 1) % targets.Length;
                }
            }

            targets.Dispose();
        }

        private NativeList<(Entity Entity, float3 Position, int Priority)> FindEnemyTargets(
            ref SystemState state, float3 position, Faction enemyFaction, float range)
        {
            var targets = new NativeList<(Entity, float3, int)>(Allocator.Temp);
            var em = state.EntityManager;

            foreach (var (factionTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != enemyFaction) continue;

                float dist = math.distance(position, transform.ValueRO.Position);
                if (dist <= range)
                {
                    targets.Add((entity, transform.ValueRO.Position, 1));
                }
            }

            foreach (var (factionTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<BuildingTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != enemyFaction) continue;

                float dist = math.distance(position, transform.ValueRO.Position);
                if (dist <= range)
                {
                    targets.Add((entity, transform.ValueRO.Position, 0));
                }
            }

            return targets;
        }

        private void PrioritizeTargets(ref SystemState state, NativeList<(Entity Entity, float3 Position, int Priority)> targets)
        {
            // Sort by priority (higher first)
            targets.Sort(new TargetPriorityComparer());
        }

        private Entity FindNearestEnemy(ref SystemState state, float3 position, Faction myFaction, float range)
        {
            Entity nearest = Entity.Null;
            float nearestDist = range;

            foreach (var (factionTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == myFaction) continue;

                float dist = math.distance(position, transform.ValueRO.Position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = entity;
                }
            }

            return nearest;
        }

        private Faction GetEnemyFaction(ref SystemState state, Entity enemy)
        {
            var em = state.EntityManager;
            if (em.HasComponent<FactionTag>(enemy))
                return em.GetComponentData<FactionTag>(enemy).Value;
            return Faction.Red;
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

    struct MissionPriorityComparer : IComparer<(Entity entity, AIMission mission)>
    {
        public int Compare((Entity entity, AIMission mission) a, (Entity entity, AIMission mission) b)
        {
            return b.mission.Priority.CompareTo(a.mission.Priority);
        }
    }

    struct TargetPriorityComparer : IComparer<(Entity Entity, float3 Position, int Priority)>
    {
        public int Compare((Entity Entity, float3 Position, int Priority) a,
            (Entity Entity, float3 Position, int Priority) b)
        {
            return b.Priority.CompareTo(a.Priority);
        }
    }
}