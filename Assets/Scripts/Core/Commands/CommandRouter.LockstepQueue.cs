// CommandRouter.LockstepQueue.cs
// Partial class extension holding the Queue*ForLockstep boilerplate.
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

        private static void QueueLayeredMoveForLockstep(EntityManager em, Entity unit,
            float3 destination, byte targetLayer)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                ExecuteLayeredMoveDirect(em, unit, destination, targetLayer);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.LayeredMove,
                EntityNetworkId = networkId,
                TargetPosition = destination,
                // The layer byte rides the spare target-entity field.
                TargetEntityId = targetLayer
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueAgeUpForLockstep(EntityManager em, Entity hall, byte culture)
        {
            int networkId = GetNetworkId(em, hall);
            if (networkId <= 0)
            {
                AgeUpCommandDirect(em, hall, culture);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.AgeUp,
                EntityNetworkId = networkId,
                TargetEntityId = culture
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueTempleUpgradeForLockstep(EntityManager em, Entity temple)
        {
            int networkId = GetNetworkId(em, temple);
            if (networkId <= 0)
            {
                TempleUpgradeCommandDirect(em, temple);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.TempleUpgrade,
                EntityNetworkId = networkId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueSectAdoptionForLockstep(EntityManager em, Entity temple,
            string sectId, int preferredSlot, float buildTime)
        {
            int networkId = GetNetworkId(em, temple);
            if (networkId <= 0)
            {
                SectAdoptionCommandDirect(em, temple, sectId, preferredSlot, buildTime);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.SectAdopt,
                EntityNetworkId = networkId,
                TargetEntityId = preferredSlot,
                BuildingId = sectId,
                // Build time rides the spare position field.
                TargetPosition = new float3(buildTime, 0f, 0f)
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueBuildingUpgradeForLockstep(EntityManager em, Entity building)
        {
            int networkId = GetNetworkId(em, building);
            if (networkId <= 0)
            {
                Types.UpgradeBuildingCommandHelper.ApplyDirect(em, building);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.BuildingUpgrade,
                EntityNetworkId = networkId
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueResearchForLockstep(EntityManager em, Entity building, string techId)
        {
            int networkId = GetNetworkId(em, building);
            if (networkId <= 0)
            {
                ResearchCommandDirect(em, building, techId);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Research,
                EntityNetworkId = networkId,
                BuildingId = techId // tech id rides the string field
            };
            LockstepServiceLocator.Instance.QueueCommand(cmd);
        }

        private static void QueueAttackForLockstep(EntityManager em, Entity unit, Entity target)
        {
            int unitId = GetNetworkId(em, unit);
            int targetId = GetNetworkId(em, target);

            if (unitId <= 0 || targetId <= 0)
            {
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

        // The HutConversionTarget byte enum packs into the existing int
        // TargetEntityId field (0=None, 1=WallHub, 2=WatchTower). No schema
        // change — ints round-trip exactly on serialize/deserialize.
        // (task-109 phase 2 / AD-2)
        private static void QueueConvertHutForLockstep(EntityManager em, Entity hut, HutConversionTarget target)
        {
            int hutId = GetNetworkId(em, hut);
            if (hutId <= 0)
            {
                ConvertHutCommandHelper.Execute(em, hut, target);
                return;
            }

            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.ConvertHut,
                EntityNetworkId = hutId,
                TargetEntityId = (int)(byte)target,
            });
        }

        // task-109 phase 6: segment-id rides in EntityNetworkId and the
        // optional focus-instance network id rides in TargetEntityId
        // (0 = no focus, use the segment midpoint). Both fields are int —
        // no schema change. Segments and instances both carry NetworkedEntity
        // (added in AlanthorWall.CreateSegment / CreateInstance, Phase 4).
        private static void QueueConvertSegmentToGateForLockstep(EntityManager em, Entity segment, Entity focusInstance)
        {
            int segId = GetNetworkId(em, segment);
            if (segId <= 0)
            {
                // No network identity — singleplayer / pre-lockstep path.
                ConvertSegmentToGateCommandHelper.Execute(em, segment, focusInstance);
                return;
            }

            int focusId = focusInstance != Entity.Null
                ? GetNetworkId(em, focusInstance)
                : 0;
            // focusId < 0 means the instance lacks a NetworkedEntity (older
            // bootstrap path); pass 0 to fall back to the segment midpoint
            // on the executing peer rather than scarier behaviour.
            if (focusId < 0) focusId = 0;

            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.ConvertSegmentToGate,
                EntityNetworkId = segId,
                TargetEntityId = focusId,
            });
        }

        // The slot index rides in the existing int TargetEntityId field —
        // there is no float-format risk in the Serialize/Deserialize path
        // (ints round-trip exactly) and no schema bump is needed.
        private static void QueueCancelTrainForLockstep(EntityManager em, Entity building, int slotIndex)
        {
            int buildingId = GetNetworkId(em, building);

            if (buildingId <= 0)
            {
                CancelTrainCommandHelper.Execute(em, building, slotIndex);
                return;
            }

            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.CancelTrain,
                EntityNetworkId = buildingId,
                TargetEntityId = slotIndex,
            });
        }

        private static void QueueAbilityForLockstep(EntityManager em, Entity unit, Entity target)
        {
            int unitId = GetNetworkId(em, unit);
            int targetId = target != Entity.Null ? GetNetworkId(em, target) : 0;

            if (unitId <= 0)
            {
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

        // ─── Spec-implementation commands (slice 29) ──────────────────

        private static void QueuePurifyForLockstep(EntityManager em, Entity scholar, Entity node)
        {
            int scholarId = GetNetworkId(em, scholar);
            int nodeId = GetNetworkId(em, node);
            if (scholarId <= 0 || nodeId <= 0)
            {
                IssuePurifyDirect(em, scholar, node);
                return;
            }
            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.Purify,
                EntityNetworkId = scholarId,
                TargetEntityId = nodeId,
            });
        }

        private static void QueueCorruptForLockstep(EntityManager em, Entity corruptor, Entity node)
        {
            int corruptorId = GetNetworkId(em, corruptor);
            int nodeId = GetNetworkId(em, node);
            if (corruptorId <= 0 || nodeId <= 0)
            {
                IssueCorruptDirect(em, corruptor, node);
                return;
            }
            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.Corrupt,
                EntityNetworkId = corruptorId,
                TargetEntityId = nodeId,
            });
        }

        private static void QueueConvertNodeForLockstep(EntityManager em, Entity acolyte, Entity node)
        {
            int acolyteId = GetNetworkId(em, acolyte);
            int nodeId = GetNetworkId(em, node);
            if (acolyteId <= 0 || nodeId <= 0)
            {
                IssueConvertNodeDirect(em, acolyte, node);
                return;
            }
            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.ConvertNode,
                EntityNetworkId = acolyteId,
                TargetEntityId = nodeId,
            });
        }

        /// <summary>
        /// Equipment-upgrade payload packs the faction (EntityNetworkId),
        /// unit class (TargetEntityId low byte), and target tier (TargetEntityId
        /// high byte). BuildingId remains empty for this command.
        /// </summary>
        private static void QueueEquipmentUpgradeForLockstep(Faction faction,
            UnitClass unitClass, EquipmentTier targetTier)
        {
            int packed = ((byte)unitClass) | (((byte)targetTier) << 8);
            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.EquipmentUpgrade,
                EntityNetworkId = (int)faction,
                TargetEntityId = packed,
            });
        }

        /// <summary>
        /// God-power payload uses EntityNetworkId for the caster faction
        /// (Faction is a byte) and TargetPosition for the world-space cast
        /// point. There is no entity to dereference; the resolver looks up
        /// the faction's bank by FactionTag.
        /// </summary>
        private static void QueueGodPowerForLockstep(Faction caster, float3 targetPosition)
        {
            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.GodPower,
                EntityNetworkId = (int)caster,
                TargetPosition = targetPosition,
            });
        }
    }
}
