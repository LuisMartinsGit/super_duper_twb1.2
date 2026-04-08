// CommandRouter.LockstepQueue.cs
// Partial class extension holding the Queue*ForLockstep boilerplate.
// Location: Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs
//
// Fix #224: CommandRouter.cs used to be 943 lines. The LOCKSTEP QUEUE METHODS
// section (14 nearly-identical Queue*ForLockstep helpers, ~280 lines) was
// boilerplate that followed a template for each command type. It lives here
// as a partial so the main file can focus on the public Issue* API, the
// routing decisions, and the direct-execution helpers.
//
// All methods here are `private static` so they remain callable only from the
// other CommandRouter partial file. GetNetworkId + direct-execution helpers
// (SetRallyPointDirect, TrainCommandDirect, IssueAbilityDirect) stay in the
// main file because they are consumed by both the routing layer and the
// queue layer below.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
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

        private static void QueueTrainForLockstep(EntityManager em, Entity building, string unitId)
        {
            int buildingId = GetNetworkId(em, building);

            if (buildingId <= 0)
            {
                TrainCommandDirect(em, building, unitId);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Train,
                EntityNetworkId = buildingId,
                BuildingId = unitId // Reuse BuildingId field to carry the unit type
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueAbilityForLockstep(EntityManager em, Entity unit, Entity target)
        {
            int unitId = GetNetworkId(em, unit);
            int targetId = target != Entity.Null ? GetNetworkId(em, target) : 0;

            if (unitId <= 0)
            {
                Debug.LogWarning($"[CommandRouter] Entity {unit.Index} has no network ID, executing locally");
                IssueAbilityDirect(em, unit, target);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Ability,
                EntityNetworkId = unitId,
                TargetEntityId = targetId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }
    }
}
