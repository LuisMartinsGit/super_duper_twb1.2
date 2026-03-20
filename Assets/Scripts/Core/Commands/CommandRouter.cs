// CommandRouter.cs
// Unified command routing system for local player, remote player, and AI
// Location: Assets/Scripts/Core/Commands/CommandRouter.cs

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    /// <summary>
    /// CommandRouter is the SINGLE ENTRY POINT for all game commands.
    /// 
    /// Whether commands come from:
    /// - Local player (RTSInput, UI panels)
    /// - Remote player (network/lockstep)
    /// - AI (AITacticalManager, AIEconomyManager, etc.)
    /// 
    /// They ALL flow through here. This ensures:
    /// 1. Consistent behavior across all command sources
    /// 2. Proper multiplayer synchronization when needed
    /// 3. Easy debugging (single point to log all commands)
    /// 4. Clean separation of concerns
    /// 
    /// USAGE:
    /// - For player input: CommandRouter.IssueMove(entity, destination)
    /// - For AI: CommandRouter.IssueMove(entity, destination, CommandSource.AI)
    /// - The router handles whether to execute immediately or queue for lockstep
    /// </summary>
    public static class CommandRouter
    {
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Enable detailed logging of all commands (useful for debugging sync issues)
        /// </summary>
        public static bool LogCommands = false;

        /// <summary>
        /// Command source enum for routing decisions
        /// </summary>
        public enum CommandSource
        {
            LocalPlayer,
            RemotePlayer,
            AI,
            System
        }

        // ═══════════════════════════════════════════════════════════════
        // MOVEMENT COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a move command to a unit.
        /// </summary>
        public static void IssueMove(EntityManager em, Entity unit, float3 destination, 
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Move: {unit.Index} -> {destination} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueMoveForLockstep(em, unit, destination);
            }
            else
            {
                MoveCommandHelper.Execute(em, unit, destination);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ATTACK COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue an attack command to a unit.
        /// </summary>
        public static void IssueAttack(EntityManager em, Entity unit, Entity target,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (target == Entity.Null || !em.Exists(target)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Attack: {unit.Index} -> {target.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueAttackForLockstep(em, unit, target);
            }
            else
            {
                AttackCommandHelper.Execute(em, unit, target);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ATTACK-MOVE COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue an attack-move command to a unit.
        /// Unit moves toward destination while auto-engaging enemies along the way.
        /// </summary>
        public static void IssueAttackMove(EntityManager em, Entity unit, float3 destination,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] AttackMove: {unit.Index} -> {destination} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueAttackMoveForLockstep(em, unit, destination);
            }
            else
            {
                AttackMoveCommandHelper.Execute(em, unit, destination);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PATROL COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a patrol command to a unit.
        /// Unit patrols back and forth between its current position and the destination,
        /// auto-engaging enemies along the way.
        /// </summary>
        public static void IssuePatrol(EntityManager em, Entity unit, float3 destination,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Patrol: {unit.Index} -> {destination} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueuePatrolForLockstep(em, unit, destination);
            }
            else
            {
                PatrolCommandHelper.Execute(em, unit, destination);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // STOP COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a stop command to a unit.
        /// </summary>
        public static void IssueStop(EntityManager em, Entity unit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Stop: {unit.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueStopForLockstep(em, unit);
            }
            else
            {
                CommandHelper.ClearAllCommands(em, unit);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HOLD POSITION COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a hold position command to a unit.
        /// Unit stops and attacks enemies in range but does not chase.
        /// </summary>
        public static void IssueHoldPosition(EntityManager em, Entity unit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] HoldPosition: {unit.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueHoldPositionForLockstep(em, unit);
            }
            else
            {
                HoldPositionCommandHelper.Execute(em, unit);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // BUILD COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a build command to a builder unit.
        /// </summary>
        public static void IssueBuild(EntityManager em, Entity builder, Entity targetBuilding,
            string buildingId, float3 position, CommandSource source = CommandSource.LocalPlayer)
        {
            if (builder == Entity.Null || !em.Exists(builder)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Build: {builder.Index} -> {buildingId} at {position} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueBuildForLockstep(em, builder, targetBuilding, buildingId, position);
            }
            else
            {
                BuildCommandHelper.Execute(em, builder, targetBuilding, buildingId, position);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GATHER COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a gather command to a miner unit.
        /// </summary>
        public static void IssueGather(EntityManager em, Entity miner, Entity resource, Entity deposit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (miner == Entity.Null || !em.Exists(miner)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Gather: {miner.Index} -> Resource:{resource.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueGatherForLockstep(em, miner, resource, deposit);
            }
            else
            {
                GatherCommandHelper.Execute(em, miner, resource, deposit);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HEAL COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a heal command to a healer unit.
        /// </summary>
        public static void IssueHeal(EntityManager em, Entity healer, Entity target,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (healer == Entity.Null || !em.Exists(healer)) return;
            if (target == Entity.Null || !em.Exists(target)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Heal: {healer.Index} -> {target.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueHealForLockstep(em, healer, target);
            }
            else
            {
                HealCommandHelper.Execute(em, healer, target);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CONVERT COMMANDS (Miner → Berserker at Fiendstone Keep)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a convert command to a miner unit targeting a Fiendstone Keep.
        /// </summary>
        public static void IssueConvert(EntityManager em, Entity miner, Entity keep,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (miner == Entity.Null || !em.Exists(miner)) return;
            if (keep == Entity.Null || !em.Exists(keep)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Convert: {miner.Index} -> Keep:{keep.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueConvertForLockstep(em, miner, keep);
            }
            else
            {
                ConvertCommandHelper.Execute(em, miner, keep);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // REPAIR COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a repair command to a builder unit targeting a damaged building.
        /// </summary>
        public static void IssueRepair(EntityManager em, Entity builder, Entity building,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (builder == Entity.Null || !em.Exists(builder)) return;
            if (building == Entity.Null || !em.Exists(building)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] Repair: {builder.Index} -> {building.Index} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueRepairForLockstep(em, builder, building);
            }
            else
            {
                RepairCommandHelper.Execute(em, builder, building);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // RALLY POINT COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Set rally point for a building.
        /// </summary>
        public static void SetRallyPoint(EntityManager em, Entity building, float3 position,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] SetRally: {building.Index} -> {position} (Source: {source})");

            if (ShouldQueueForLockstep(source))
            {
                QueueRallyPointForLockstep(em, building, position);
            }
            else
            {
                SetRallyPointDirect(em, building, position);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // BATTALION STANCE COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Set the stance on a battalion leader entity.
        /// Stance controls how battalion members engage enemies.
        /// </summary>
        public static void IssueStanceChange(EntityManager em, Entity leader,
            BattalionStance stance, CommandSource source = CommandSource.LocalPlayer)
        {
            if (leader == Entity.Null || !em.Exists(leader)) return;
            if (!em.HasComponent<BattalionStanceData>(leader)) return;

            if (LogCommands)
                Debug.Log($"[CommandRouter] StanceChange: {leader.Index} -> {stance} (Source: {source})");

            em.SetComponentData(leader, new BattalionStanceData { Value = stance });
        }

        // ═══════════════════════════════════════════════════════════════
        // INTERNAL ROUTING LOGIC
        // ═══════════════════════════════════════════════════════════════

        private static bool ShouldQueueForLockstep(CommandSource source)
        {
            // Only queue if in multiplayer with active lockstep
            if (!GameSettings.IsMultiplayer) return false;
            
            var lockstep = LockstepServiceLocator.Instance;
            if (lockstep == null || !lockstep.IsSimulationRunning)
                return false;

            return source switch
            {
                CommandSource.LocalPlayer => true,
                CommandSource.AI => lockstep.IsHost, // Only host queues AI commands
                CommandSource.RemotePlayer => false, // Already synchronized
                CommandSource.System => false,       // Deterministic - execute immediately
                _ => false
            };
        }

        private static int GetNetworkId(EntityManager em, Entity entity)
        {
            if (entity == Entity.Null || !em.Exists(entity)) return -1;
            if (!em.HasComponent<NetworkedEntity>(entity)) return -1;
            return em.GetComponentData<NetworkedEntity>(entity).NetworkId;
        }

        // ═══════════════════════════════════════════════════════════════
        // LOCKSTEP QUEUE METHODS
        // ═══════════════════════════════════════════════════════════════

        private static void QueueMoveForLockstep(EntityManager em, Entity unit, float3 destination)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                Debug.LogWarning($"[CommandRouter] Entity {unit.Index} has no network ID, executing locally");
                MoveCommandHelper.Execute(em, unit, destination);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Move,
                EntityNetworkId = networkId,
                TargetPosition = destination
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueAttackForLockstep(EntityManager em, Entity unit, Entity target)
        {
            int unitId = GetNetworkId(em, unit);
            int targetId = GetNetworkId(em, target);

            if (unitId <= 0 || targetId <= 0)
            {
                Debug.LogWarning($"[CommandRouter] Missing network IDs for attack, executing locally");
                AttackCommandHelper.Execute(em, unit, target);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Attack,
                EntityNetworkId = unitId,
                TargetEntityId = targetId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueAttackMoveForLockstep(EntityManager em, Entity unit, float3 destination)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                Debug.LogWarning($"[CommandRouter] Entity {unit.Index} has no network ID, executing locally");
                AttackMoveCommandHelper.Execute(em, unit, destination);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.AttackMove,
                EntityNetworkId = networkId,
                TargetPosition = destination
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueStopForLockstep(EntityManager em, Entity unit)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                CommandHelper.ClearAllCommands(em, unit);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Stop,
                EntityNetworkId = networkId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueHoldPositionForLockstep(EntityManager em, Entity unit)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                HoldPositionCommandHelper.Execute(em, unit);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.HoldPosition,
                EntityNetworkId = networkId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueBuildForLockstep(EntityManager em, Entity builder, Entity targetBuilding,
            string buildingId, float3 position)
        {
            int builderId = GetNetworkId(em, builder);
            int targetId = targetBuilding != Entity.Null ? GetNetworkId(em, targetBuilding) : 0;

            if (builderId <= 0)
            {
                BuildCommandHelper.Execute(em, builder, targetBuilding, buildingId, position);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Build,
                EntityNetworkId = builderId,
                TargetEntityId = targetId,
                TargetPosition = position,
                BuildingId = buildingId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueGatherForLockstep(EntityManager em, Entity miner, Entity resource, Entity deposit)
        {
            int minerId = GetNetworkId(em, miner);
            int resourceId = GetNetworkId(em, resource);
            int depositId = deposit != Entity.Null ? GetNetworkId(em, deposit) : 0;

            if (minerId <= 0)
            {
                GatherCommandHelper.Execute(em, miner, resource, deposit);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Gather,
                EntityNetworkId = minerId,
                TargetEntityId = resourceId,
                SecondaryTargetId = depositId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueHealForLockstep(EntityManager em, Entity healer, Entity target)
        {
            int healerId = GetNetworkId(em, healer);
            int targetId = GetNetworkId(em, target);

            if (healerId <= 0 || targetId <= 0)
            {
                HealCommandHelper.Execute(em, healer, target);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Heal,
                EntityNetworkId = healerId,
                TargetEntityId = targetId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueRallyPointForLockstep(EntityManager em, Entity building, float3 position)
        {
            int buildingId = GetNetworkId(em, building);

            if (buildingId <= 0)
            {
                SetRallyPointDirect(em, building, position);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.SetRally,
                EntityNetworkId = buildingId,
                TargetPosition = position
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueRepairForLockstep(EntityManager em, Entity builder, Entity building)
        {
            int builderId = GetNetworkId(em, builder);
            int buildingId = GetNetworkId(em, building);

            if (builderId <= 0 || buildingId <= 0)
            {
                RepairCommandHelper.Execute(em, builder, building);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Repair,
                EntityNetworkId = builderId,
                TargetEntityId = buildingId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueuePatrolForLockstep(EntityManager em, Entity unit, float3 destination)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                Debug.LogWarning($"[CommandRouter] Entity {unit.Index} has no network ID, executing locally");
                PatrolCommandHelper.Execute(em, unit, destination);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Patrol,
                EntityNetworkId = networkId,
                TargetPosition = destination
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueConvertForLockstep(EntityManager em, Entity miner, Entity keep)
        {
            int minerId = GetNetworkId(em, miner);
            int keepId = GetNetworkId(em, keep);

            if (minerId <= 0 || keepId <= 0)
            {
                ConvertCommandHelper.Execute(em, miner, keep);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Convert,
                EntityNetworkId = minerId,
                TargetEntityId = keepId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void SetRallyPointDirect(EntityManager em, Entity building, float3 position)
        {
            if (!em.HasComponent<RallyPoint>(building))
                em.AddComponent<RallyPoint>(building);
            em.SetComponentData(building, new RallyPoint { Position = position, Has = 1 });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SHARED COMMAND HELPER
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Shared utility methods for command execution
    /// </summary>
    public static class CommandHelper
    {
        /// <summary>
        /// Clears all command components from a unit
        /// </summary>
        public static void ClearAllCommands(EntityManager em, Entity unit)
        {
            if (em.HasComponent<Types.MoveCommand>(unit))
                em.RemoveComponent<Types.MoveCommand>(unit);
            if (em.HasComponent<Types.AttackCommand>(unit))
                em.RemoveComponent<Types.AttackCommand>(unit);
            if (em.HasComponent<Types.GatherCommand>(unit))
                em.RemoveComponent<Types.GatherCommand>(unit);
            if (em.HasComponent<Types.BuildCommand>(unit))
                em.RemoveComponent<Types.BuildCommand>(unit);
            if (em.HasComponent<BuildOrder>(unit))
                em.RemoveComponent<BuildOrder>(unit);
            if (em.HasComponent<RepairOrder>(unit))
                em.RemoveComponent<RepairOrder>(unit);
            if (em.HasComponent<Types.HealCommand>(unit))
                em.RemoveComponent<Types.HealCommand>(unit);
            if (em.HasComponent<Types.ConvertCommand>(unit))
                em.RemoveComponent<Types.ConvertCommand>(unit);
            if (em.HasComponent<DesiredDestination>(unit))
                em.SetComponentData(unit, new DesiredDestination { Has = 0 });
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);
            if (em.HasComponent<AttackMoveTag>(unit))
                em.RemoveComponent<AttackMoveTag>(unit);
            if (em.HasComponent<Types.AttackMoveCommand>(unit))
                em.RemoveComponent<Types.AttackMoveCommand>(unit);
            if (em.HasComponent<PatrolTag>(unit))
                em.RemoveComponent<PatrolTag>(unit);
            if (em.HasComponent<PatrolAgent>(unit))
                em.RemoveComponent<PatrolAgent>(unit);
            if (em.HasComponent<Types.PatrolCommand>(unit))
                em.RemoveComponent<Types.PatrolCommand>(unit);
            if (em.HasBuffer<PatrolWaypoint>(unit))
                em.GetBuffer<PatrolWaypoint>(unit).Clear();
            if (em.HasComponent<HoldPositionTag>(unit))
                em.RemoveComponent<HoldPositionTag>(unit);
            if (em.HasComponent<CommandQueueActive>(unit))
                em.RemoveComponent<CommandQueueActive>(unit);
            if (em.HasBuffer<QueuedCommand>(unit))
                em.GetBuffer<QueuedCommand>(unit).Clear();
        }
    }
}