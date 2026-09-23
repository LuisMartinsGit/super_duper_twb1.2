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

        /// <summary>
        /// THE FALLBACK THAT FORKED THE SIM (2026-09-13). Every Queue*ForLockstep
        /// helper below used to answer "this entity has no network id yet" by
        /// executing the order LOCALLY and returning. In single-player that is
        /// harmless. Under lockstep it is a desync by construction: the host
        /// applies the order, no command ever reaches the wire, and every
        /// client stays still. Three forks in two days were exactly this shape
        /// -- a worker at its guard post, moved on the host alone, with no
        /// command referencing it -- and the component roster finally named
        /// it: a <c>RepairOrder</c> on the host only, at tick 61694, on an
        /// entity whose target building had been placed moments earlier and
        /// did not yet carry its network id.
        ///
        /// In lockstep an un-networked entity cannot be replicated, so the only
        /// safe answer is to DROP the order and say so; the issuer (usually the
        /// AI) retries next think, by which time the id is there. Outside
        /// lockstep the old local execution is still correct.
        /// </summary>
        private static bool MayExecuteLocally(EntityManager em, Entity e, string what, Entity other = default)
        {
            if (!GameSettings.IsMultiplayer) return true;
            var ls = LockstepServiceLocator.Instance;
            if (ls == null || !ls.IsSimulationRunning) return true;
            // Name BOTH sides of a two-entity order and say which one lacks the
            // id: the AI repair loop retries a dropped order every think, and
            // "entity 335:29" says nothing about which factory forgot to
            // stamp NetworkedEntity.
            string who = Describe(em, e);
            if (other != default) who += " -> " + Describe(em, other);
            UnityEngine.Debug.LogWarning(
                $"[CommandRouter] DROPPED {what} for {who} -- no network id, " +
                "cannot replicate under lockstep (executing it locally would fork the sim).");
            return false;
        }

        private static string Describe(EntityManager em, Entity e)
        {
            if (e == Entity.Null || !em.Exists(e)) return $"{e.Index}:{e.Version}(gone)";
            string name = em.HasComponent<DisplayName>(e)
                ? em.GetComponentData<DisplayName>(e).Value.ToString() : "?";
            int id = GetNetworkId(em, e);
            if (id > 0) return $"{name} {e.Index}:{e.Version} net={id}";
            // The un-networked one is the one we are hunting: list its
            // components so the creator that forgot the stamp can be found
            // from the log alone.
            using var types = em.GetComponentTypes(e, Unity.Collections.Allocator.Temp);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < types.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(types[i].GetManagedType()?.Name ?? "?");
            }
            return $"{name} {e.Index}:{e.Version} net={id} [{sb}]";
        }
        // ═══════════════════════════════════════════════════════════════
        // LOCKSTEP QUEUE METHODS
        // ═══════════════════════════════════════════════════════════════

        private static void QueueMoveForLockstep(EntityManager em, Entity unit, float3 destination)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                if (!MayExecuteLocally(em, unit, "Move")) return;
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


        /// <summary>Un-networked target inside an ACTIVE lockstep match:
        /// executing locally is a GUARANTEED desync (the issuing peer mutates
        /// state no other peer will), so refuse loudly instead (2026-09-04,
        /// MP harness catch #9 — a sect-conjured Watch Tower without
        /// NetworkedEntity took the ApplyDirect fallback on the host alone
        /// and forked the banks). The fallback below each guard is the
        /// SINGLE-PLAYER path, where direct execution is the correct and
        /// only behavior.</summary>
        private static bool RefuseUnnetworkedInLockstep(string what)
        {
            if (!LockstepServiceLocator.IsActive) return false;
            UnityEngine.Debug.LogError(
                "[CommandRouter] " + what + ": target has no NetworkId - refusing " +
                "local-only execution in a lockstep match (would desync). The entity " +
                "was created outside the networked factory dispatch; fix that creation path.");
            return true;
        }

        private static void QueueLayeredMoveForLockstep(EntityManager em, Entity unit,
            float3 destination, byte targetLayer)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                if (RefuseUnnetworkedInLockstep("LayeredMove")) return;
                if (!MayExecuteLocally(em, unit, "LayeredMove")) return;
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
                if (RefuseUnnetworkedInLockstep("AgeUp")) return;
                if (!MayExecuteLocally(em, hall, "AgeUp")) return;
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
                if (RefuseUnnetworkedInLockstep("TempleUpgrade")) return;
                if (!MayExecuteLocally(em, temple, "TempleUpgrade")) return;
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
                if (RefuseUnnetworkedInLockstep("SectAdoption")) return;
                if (!MayExecuteLocally(em, temple, "SectAdoption")) return;
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
                if (RefuseUnnetworkedInLockstep("BuildingUpgrade")) return;
                if (!MayExecuteLocally(em, building, "UpgradeBuilding")) return;
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
                if (RefuseUnnetworkedInLockstep("Research")) return;
                if (!MayExecuteLocally(em, building, "Research")) return;
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
                if (!MayExecuteLocally(em, unit, "Attack", target)) return;
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
                if (!MayExecuteLocally(em, unit, "AttackMove")) return;
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
                if (!MayExecuteLocally(em, unit, "ClearAlls")) return;
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
                if (!MayExecuteLocally(em, unit, "HoldPosition")) return;
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
                if (!MayExecuteLocally(em, builder, "Build")) return;
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
                if (!MayExecuteLocally(em, healer, "Heal", target)) return;
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
                if (!MayExecuteLocally(em, building, "SetRallyPoint")) return;
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
                if (!MayExecuteLocally(em, builder, "Repair", building)) return;
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
                if (!MayExecuteLocally(em, unit, "Patrol")) return;
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
                if (!MayExecuteLocally(em, miner, "Convert", keep)) return;
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

        /// <param name="revival">Hero revival mode, riding in TargetEntityId
        /// — free on a Train command (EntityNetworkId holds the building,
        /// BuildingId the unit type) and already serialized. It MUST cross the
        /// wire: the two modes charge different prices and hand back different
        /// levels, so a peer that assumed None would fork the bank.</param>
        private static void QueueTrainForLockstep(EntityManager em, Entity building, string unitId,
            TheWaningBorder.Abilities.HeroRevivalMode revival
                = TheWaningBorder.Abilities.HeroRevivalMode.None)
        {
            int buildingId = GetNetworkId(em, building);

            if (buildingId <= 0)
            {
                if (!MayExecuteLocally(em, building, "Train")) return;
                TrainCommandDirect(em, building, unitId, revival);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Train,
                EntityNetworkId = buildingId,
                BuildingId = unitId, // Reuse BuildingId field to carry the unit type
                TargetEntityId = (int)revival
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
                if (!MayExecuteLocally(em, hut, "ConvertHut")) return;
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
                if (!MayExecuteLocally(em, segment, "ConvertSegmentToGate")) return;
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

        /// <summary>The slot index rides in the existing int TargetEntityId
        /// field — there is no float-format risk in the Serialize/Deserialize
        /// path (ints round-trip exactly) and no schema bump is needed.</summary>
        private static void QueueCancelProductionForLockstep(EntityManager em, Entity building, int slotIndex)
        {
            int buildingId = GetNetworkId(em, building);

            if (buildingId <= 0)
            {
                if (!MayExecuteLocally(em, building, "CancelProduction")) return;
                CancelProductionCommandHelper.Execute(em, building, slotIndex);
                return;
            }

            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.CancelProduction,
                EntityNetworkId = buildingId,
                TargetEntityId = slotIndex,
            });
        }

        /// <param name="slot">The ability slot the player named, or -1 for
        /// "first ready active".
        ///
        /// THIS HAS TO CROSS THE WIRE. A hero carries several actives
        /// (docs/Design/Heroes.md §2), so "first ready" resolves against each
        /// peer's own cooldown state — two peers can pick DIFFERENT abilities
        /// for the same click and fork the simulation. It rides in
        /// SecondaryTargetId, which is already serialized and is unused by
        /// ability commands (it carries the deposit on gather commands),
        /// encoded as slot+1 so the field's natural 0 still decodes to -1.</param>
        private static void QueueAbilityForLockstep(EntityManager em, Entity unit, Entity target,
            int slot = -1)
        {
            int unitId = GetNetworkId(em, unit);
            int targetId = target != Entity.Null ? GetNetworkId(em, target) : 0;

            if (unitId <= 0)
            {
                if (!MayExecuteLocally(em, unit, "IssueAbility")) return;
                IssueAbilityDirect(em, unit, target, slot);
                return;
            }

            var cmd = new LockstepCommand
            {
                Type = LockstepCommandType.Ability,
                EntityNetworkId = unitId,
                TargetEntityId = targetId,
                SecondaryTargetId = slot + 1
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
                if (!MayExecuteLocally(em, scholar, "IssuePurify", node)) return;
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
                if (!MayExecuteLocally(em, corruptor, "IssueCorrupt", node)) return;
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
                if (!MayExecuteLocally(em, acolyte, "IssueConvertNode", node)) return;
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
