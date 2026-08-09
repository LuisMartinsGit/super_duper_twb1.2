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
    // Fix #224: CommandRouter is split across partial files.
    // The ~280 lines of Queue*ForLockstep boilerplate live in
    // CommandRouter.LockstepQueue.cs to keep this file focused on the
    // public Issue* API, routing decisions, and direct helpers.
    public static partial class CommandRouter
    {
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Enable detailed logging of all commands (useful for debugging sync issues)
        /// </summary>
        public static bool LogCommands = false;

        // Fix #235: the nested `CommandSource` enum was removed. The canonical
        // definition lives in ICommand.cs at the namespace level
        // (TheWaningBorder.Core.Commands.CommandSource). Both enums had
        // identical members and any reference that disambiguated with
        // `CommandRouter.CommandSource.X` was migrated to `CommandSource.X`.

        /// <summary>
        /// Returns true if the entity has NotControllableTag and the command source is LocalPlayer.
        /// Auto-controlled units (caravans, trade patrols) ignore player orders.
        /// </summary>
        private static bool IsBlockedByNotControllable(EntityManager em, Entity unit, CommandSource source)
        {
            if (source != CommandSource.LocalPlayer) return false;
            if (unit == Entity.Null || !em.Exists(unit)) return false;
            return em.HasComponent<NotControllableTag>(unit);
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
            if (IsBlockedByNotControllable(em, unit, source)) return;

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
            if (IsBlockedByNotControllable(em, unit, source)) return;
            if (target == Entity.Null || !em.Exists(target)) return;

            // Verb wells are FERALDIS-ONLY attack targets (2026-08-04): Age 0
            // and Alanthor/Runai factions can never attack a well — their
            // verbs are Purify / Pacify. Only the Feraldis culture breaks
            // wells by force.
            if (em.HasComponent<BorderMainNodeTag>(target))
            {
                var attackerFaction = em.HasComponent<FactionTag>(unit)
                    ? em.GetComponentData<FactionTag>(unit).Value : Faction.Blue;
                if (FactionColors.GetFactionCulture(attackerFaction) != Cultures.Feraldis)
                {
                    if (source == CommandSource.LocalPlayer)
                        TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                            "The well resists all arms — only Feraldis may break it");
                    return;
                }
            }

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
            if (IsBlockedByNotControllable(em, unit, source)) return;

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
            if (IsBlockedByNotControllable(em, unit, source)) return;

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
            if (IsBlockedByNotControllable(em, unit, source)) return;

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
            if (IsBlockedByNotControllable(em, unit, source)) return;

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
        // LAYERED (GROUND / WALL-TOP) MOVE COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Move a unit to <paramref name="dest"/> on layer
        /// <paramref name="targetLayer"/> (0 = Ground, 1 = Rampart). If the
        /// unit isn't already on that layer it routes to the nearest wall
        /// access point (gate / stair), LERPs across, then moves freely on the
        /// target layer. See <see cref="LayeredMoveSystem"/>.
        /// </summary>
        public static void IssueLayeredMove(EntityManager em, Entity unit, float3 dest,
            byte targetLayer, CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;
            if (em.HasComponent<BuildingTag>(unit)) return;

            // This is the DEFAULT right-click move path (RTSInputManager), so
            // it must replicate like IssueMove — without this gate every
            // ordinary move executed locally only and multiplayer peers
            // watched two unrelated games.
            if (ShouldQueueForLockstep(source))
            {
                QueueLayeredMoveForLockstep(em, unit, dest, targetLayer);
                return;
            }

            ExecuteLayeredMoveDirect(em, unit, dest, targetLayer);
        }

        private static void ExecuteLayeredMoveDirect(EntityManager em, Entity unit, float3 dest,
            byte targetLayer)
        {
            CommandHelper.ClearAllCommands(em, unit);

            var order = new LayeredMoveOrder
            {
                FinalDest = dest,
                TargetLayer = targetLayer,
                Phase = 0,
                Progress = 0f,
            };
            if (em.HasComponent<LayeredMoveOrder>(unit))
                em.SetComponentData(unit, order);
            else
                em.AddComponentData(unit, order);
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
            if (IsBlockedByNotControllable(em, builder, source)) return;

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
        public static void IssueGather(EntityManager em, Entity miner, Entity resource,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (miner == Entity.Null || !em.Exists(miner)) return;
            if (IsBlockedByNotControllable(em, miner, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueGatherForLockstep(em, miner, resource);
            }
            else
            {
                GatherCommandHelper.Execute(em, miner, resource);
            }
        }

        /// <summary>
        /// Issue a dig-the-Veil command (position-targeted veilstone
        /// gathering from the curse sheet — there is no resource entity).
        /// </summary>
        public static void IssueGatherVeil(EntityManager em, Entity miner, float3 site,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (miner == Entity.Null || !em.Exists(miner)) return;
            if (IsBlockedByNotControllable(em, miner, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueGatherVeilForLockstep(em, miner, site);
            }
            else
            {
                GatherVeilCommandHelper.Execute(em, miner, site);
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
            if (IsBlockedByNotControllable(em, healer, source)) return;
            if (target == Entity.Null || !em.Exists(target)) return;

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
            if (IsBlockedByNotControllable(em, miner, source)) return;
            if (keep == Entity.Null || !em.Exists(keep)) return;

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
            if (IsBlockedByNotControllable(em, builder, source)) return;
            if (building == Entity.Null || !em.Exists(building)) return;

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
        /// Set rally point for a building. <paramref name="targetEntity"/>
        /// is an optional follow-up target (e.g. a resource node) that
        /// post-spawn handlers may use — TrainingSystem auto-issues a
        /// gather command on miners when this points at an iron / veilstone
        /// deposit. Pass Entity.Null for plain "walk here" rallies.
        /// </summary>
        public static void SetRallyPoint(EntityManager em, Entity building, float3 position,
            Entity targetEntity = default,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;

            if (ShouldQueueForLockstep(source))
            {
                // Lockstep queue currently doesn't replicate targetEntity —
                // single-player sets it directly; multiplayer falls back to
                // a position-only rally. Networked target sync can be added
                // later by extending the lockstep payload.
                QueueRallyPointForLockstep(em, building, position);
            }
            else
            {
                SetRallyPointDirect(em, building, position, targetEntity);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // EQUIPMENT TIER UPGRADE COMMANDS (faction-wide tier research)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Upgrade a faction's equipment tier for a unit class. Adjacent
        /// tier moves only (Base→Iron→Veilstone→Veilsteel→Glow). Costs are
        /// spent immediately from the faction bank; the new tier applies
        /// to all current and future units of that class on the next
        /// EquipmentTierSystem tick.
        ///
        /// Returns true if the upgrade applied; false if the move was
        /// non-adjacent, the bank couldn't pay, or the faction has no
        /// FactionEquipmentTier component yet.
        ///
        /// Multiplayer lockstep wiring for this command is a follow-up —
        /// the LockstepCommand schema needs a payload variant. For now,
        /// singleplayer + AI execute directly; multiplayer logs and drops.
        /// </summary>
        public static bool IssueEquipmentUpgrade(EntityManager em, Faction faction,
            UnitClass unitClass, EquipmentTier targetTier,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldQueueForLockstep(source))
            {
                QueueEquipmentUpgradeForLockstep(faction, unitClass, targetTier);
                return true;
            }
            return IssueEquipmentUpgradeDirect(em, faction, unitClass, targetTier);
        }

        /// <summary>Execute IssueEquipmentUpgrade on this peer. Used by LockstepManager dispatch.</summary>
        public static bool IssueEquipmentUpgradeDirect(EntityManager em, Faction faction,
            UnitClass unitClass, EquipmentTier targetTier)
        {
            // Find the faction's tier entity (created in EconomyBootstrap).
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<FactionEquipmentTier>());
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            Entity tierEntity = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value == faction) { tierEntity = ents[i]; break; }
            }
            if (tierEntity == Entity.Null) return false;

            var tiers = em.GetComponentData<FactionEquipmentTier>(tierEntity);
            EquipmentTier current = tiers.Get(unitClass);

            // Adjacent moves only — no skipping Iron → Veilsteel.
            if ((byte)targetTier != (byte)current + 1) return false;

            var cost = TheWaningBorder.Core.Settings.EquipmentTierConfig.UpgradeCost(current, targetTier);
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost))
                return false;

            switch (unitClass)
            {
                case UnitClass.Melee:   tiers.Melee   = targetTier; break;
                case UnitClass.Ranged:  tiers.Ranged  = targetTier; break;
                case UnitClass.Siege:   tiers.Siege   = targetTier; break;
                case UnitClass.Magic:   tiers.Magic   = targetTier; break;
                case UnitClass.Support: tiers.Support = targetTier; break;
                default: return false;  // Economy / Miner / Scout don't take equipment
            }
            em.SetComponentData(tierEntity, tiers);
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // GOD POWER COMMANDS (spec §6.2 + refinement #6)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cast the faction's god power at a target world position. No Glow
        /// is spent — the cooldown is reduced by the Glow currently stored
        /// in the faction's Temple of Ridan (cooldown = base × 0.8^stored).
        ///
        /// Returns false if the faction has no GodPowerState (pre-Era?),
        /// the power is still on cooldown, or the source is queued for
        /// lockstep (multiplayer wiring is a follow-up).
        /// </summary>
        public static bool IssueGodPower(EntityManager em, Faction caster,
            Unity.Mathematics.float3 targetPosition,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldQueueForLockstep(source))
            {
                QueueGodPowerForLockstep(caster, targetPosition);
                return true;  // queued — executor on every peer fires IssueGodPowerDirect
            }
            return IssueGodPowerDirect(em, caster, targetPosition);
        }

        /// <summary>
        /// Execute the god-power cast on this peer. Public so LockstepManager
        /// can dispatch through it after deserializing a queued command.
        /// </summary>
        public static bool IssueGodPowerDirect(EntityManager em, Faction caster,
            Unity.Mathematics.float3 targetPosition)
        {
            // Find the faction's bank entity.
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<GodPowerState>());
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            Entity bank = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value == caster) { bank = ents[i]; break; }
            }
            if (bank == Entity.Null) return false;

            var gps = em.GetComponentData<GodPowerState>(bank);
            if (gps.CooldownRemaining > 0f) return false;

            // Queue a pending cast on the bank — GodPowerCastSystem resolves it.
            if (em.HasComponent<PendingGodPowerCast>(bank))
                em.SetComponentData(bank, new PendingGodPowerCast
                {
                    Caster = caster,
                    TargetPosition = targetPosition,
                });
            else
                em.AddComponentData(bank, new PendingGodPowerCast
                {
                    Caster = caster,
                    TargetPosition = targetPosition,
                });

            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // PURIFY COMMANDS (Alanthor — scholar channels purification ritual on a veilstone node)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a Purify ritual command on a scholar targeting a veilstone main node.
        /// PurificationRitualSystem will move the scholar to within RitualRange,
        /// then channel for PurificationChannelTime seconds, then cleanse the node
        /// and spawn a Glow pickup.
        /// </summary>
        public static void IssuePurify(EntityManager em, Entity scholar, Entity node,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (scholar == Entity.Null || !em.Exists(scholar)) return;
            if (node == Entity.Null || !em.Exists(node)) return;
            if (IsBlockedByNotControllable(em, scholar, source)) return;
            if (!em.HasComponent<ScholarTag>(scholar)) return;
            if (!em.HasComponent<BorderMainNodeTag>(node)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueuePurifyForLockstep(em, scholar, node);
                return;
            }
            IssuePurifyDirect(em, scholar, node);
        }

        /// <summary>
        /// Send a Feraldis Corruptor to crack a well open (the Feraldis verb —
        /// docs/Design/Age_1_Feraldis.md § Corruptor). Mirrors IssuePurify.
        ///
        /// NOTE: not yet lockstep-replicated — it takes the direct path on
        /// every peer. Purify/Convert have opcodes; Corrupt needs one before
        /// multiplayer (see CommandRouter.LockstepQueue.cs + LockstepTypes).
        /// </summary>
        public static void IssueCorrupt(EntityManager em, Entity corruptor, Entity node,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (corruptor == Entity.Null || !em.Exists(corruptor)) return;
            if (node == Entity.Null || !em.Exists(node)) return;
            if (IsBlockedByNotControllable(em, corruptor, source)) return;
            if (!em.HasComponent<CorruptorTag>(corruptor)) return;
            if (!em.HasComponent<BorderMainNodeTag>(node)) return;

            CommandHelper.ClearAllCommands(em, corruptor);
            if (em.HasComponent<CorruptCommand>(corruptor))
                em.SetComponentData(corruptor, new CorruptCommand { TargetNode = node });
            else
                em.AddComponentData(corruptor, new CorruptCommand { TargetNode = node });
        }

        /// <summary>Execute IssuePurify on this peer. Used by LockstepManager dispatch.</summary>
        public static void IssuePurifyDirect(EntityManager em, Entity scholar, Entity node)
        {
            if (!em.Exists(scholar) || !em.Exists(node)) return;
            if (!em.HasComponent<ScholarTag>(scholar)) return;
            if (!em.HasComponent<BorderMainNodeTag>(node)) return;

            CommandHelper.ClearAllCommands(em, scholar);
            if (em.HasComponent<PurifyCommand>(scholar))
                em.SetComponentData(scholar, new PurifyCommand { TargetNode = node });
            else
                em.AddComponentData(scholar, new PurifyCommand { TargetNode = node });
        }

        /// <summary>
        /// Issue a Convert ritual command on an acolyte targeting a veilstone main
        /// node. ConversionRitualSystem channels for ConversionChannelTime (45s)
        /// against the node's heightened border defense, then transitions the node
        /// to Converted and flips nearby defenders to the acolyte's faction.
        /// </summary>
        public static void IssueConvertNode(EntityManager em, Entity acolyte, Entity node,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (acolyte == Entity.Null || !em.Exists(acolyte)) return;
            if (node == Entity.Null || !em.Exists(node)) return;
            if (IsBlockedByNotControllable(em, acolyte, source)) return;
            if (!em.HasComponent<AcolyteTag>(acolyte)) return;
            if (!em.HasComponent<BorderMainNodeTag>(node)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueConvertNodeForLockstep(em, acolyte, node);
                return;
            }
            IssueConvertNodeDirect(em, acolyte, node);
        }

        /// <summary>Execute IssueConvertNode on this peer. Used by LockstepManager dispatch.</summary>
        public static void IssueConvertNodeDirect(EntityManager em, Entity acolyte, Entity node)
        {
            if (!em.Exists(acolyte) || !em.Exists(node)) return;
            if (!em.HasComponent<AcolyteTag>(acolyte)) return;
            if (!em.HasComponent<BorderMainNodeTag>(node)) return;

            CommandHelper.ClearAllCommands(em, acolyte);
            if (em.HasComponent<ConvertNodeCommand>(acolyte))
                em.SetComponentData(acolyte, new ConvertNodeCommand { TargetNode = node });
            else
                em.AddComponentData(acolyte, new ConvertNodeCommand { TargetNode = node });
        }

        // ═══════════════════════════════════════════════════════════════
        // ABILITY COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue an ability command to a unit.
        /// </summary>
        public static void IssueAbility(EntityManager em, Entity unit, Entity target,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (!em.Exists(unit)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;
            if (!em.HasComponent<UnitAbility>(unit)) return;

            var ability = em.GetComponentData<UnitAbility>(unit);
            if (ability.CooldownRemaining > 0f) return;

            // For targeted abilities, validate target
            if (ability.Range > 0f && target != Entity.Null)
            {
                if (!em.Exists(target)) return;
            }

            if (ShouldQueueForLockstep(source))
            {
                QueueAbilityForLockstep(em, unit, target);
                return;
            }

            IssueAbilityDirect(em, unit, target);
        }

        /// <summary>
        /// Apply the ability immediately on this peer.
        /// public to mirror PlaceBuildingDirect / TrainCommandDirect (post-lockstep
        /// helpers).
        /// </summary>
        public static void IssueAbilityDirect(EntityManager em, Entity unit, Entity target)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (em.HasComponent<AbilityActivated>(unit))
                em.SetComponentData(unit, new AbilityActivated { Target = target });
            else
                em.AddComponentData(unit, new AbilityActivated { Target = target });
        }

        /// <summary>
        /// Fire a data-driven ability (new AbilityCatalog system — units carrying
        /// <c>UnitAbilities</c>, e.g. King Lexor's Liquid Courage, the Scout's Use
        /// Celestar). Distinct from IssueAbility, which drives the legacy sect-unit
        /// <c>UnitAbility</c> component. AbilityLifecycleSystem picks the unit's
        /// first ready Active ability. Returns false when it can't fire (no ability,
        /// on cooldown, not controllable).
        /// </summary>
        public static bool IssueUnitAbility(EntityManager em, Entity unit, Entity target = default,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (!em.Exists(unit)) return false;
            if (IsBlockedByNotControllable(em, unit, source)) return false;
            if (!em.HasComponent<TheWaningBorder.Abilities.UnitAbilities>(unit)) return false;
            if (!TheWaningBorder.Abilities.AbilityQuery.HasReadyActiveAbility(em, unit)) return false;

            if (ShouldQueueForLockstep(source))
            {
                QueueAbilityForLockstep(em, unit, target);
                return true;
            }
            IssueAbilityDirect(em, unit, target);
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // TRAIN COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a train command to queue a unit at a building.
        /// </summary>
        public static void IssueTrain(EntityManager em, Entity building, string unitId,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;

            if (source == CommandSource.LocalPlayer && em.HasComponent<FactionTag>(building))
                TheWaningBorder.AI.AILogger.LogPlayer(
                    em.GetComponentData<FactionTag>(building).Value, "TRAIN", unitId);

            // Authoritative level gate. Local-player path surfaces the
            // failure as a notification so the click feels intentional;
            // AI / network paths drop silently (the issuer either has
            // its own gating or shouldn't issue an invalid order in the
            // first place).
            if (!CanTrainAtBuilding(em, building, unitId, out int requiredLevel, out string buildingDisplay))
            {
                if (source == CommandSource.LocalPlayer)
                {
                    TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                        $"Requires Lv {requiredLevel} {buildingDisplay}");
                }
                return;
            }

            if (ShouldQueueForLockstep(source))
            {
                QueueTrainForLockstep(em, building, unitId);
            }
            else
            {
                TrainCommandDirect(em, building, unitId);
            }
        }

        /// <summary>
        /// Queue a technology on a building's research queue. The COST is the
        /// caller's business (the UI spends before issuing, mirroring the
        /// training flow) — this routes only the enqueue, so the research
        /// replicates to every peer in multiplayer instead of finishing on
        /// the issuer's screen alone.
        /// </summary>
        public static void IssueResearch(EntityManager em, Entity building, string techId,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;
            if (string.IsNullOrEmpty(techId)) return;
            if (!em.HasBuffer<ResearchQueueItem>(building)) return;

            if (source == CommandSource.LocalPlayer && em.HasComponent<FactionTag>(building))
                TheWaningBorder.AI.AILogger.LogPlayer(
                    em.GetComponentData<FactionTag>(building).Value, "RESEARCH", techId);

            if (ShouldQueueForLockstep(source))
            {
                QueueResearchForLockstep(em, building, techId);
            }
            else
            {
                ResearchCommandDirect(em, building, techId);
            }
        }

        /// <summary>
        /// Start a building level-up. Validation and cost live in
        /// UpgradeBuildingCommandHelper.Execute (the caller); this routes the
        /// state mutation so it lands on every peer in multiplayer.
        /// </summary>
        public static void IssueBuildingUpgrade(EntityManager em, Entity building,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueBuildingUpgradeForLockstep(em, building);
            }
            else
            {
                Types.UpgradeBuildingCommandHelper.ApplyDirect(em, building);
            }
        }

        /// <summary>
        /// Start the Hall's age-up with the chosen culture. Cost is spent by
        /// the caller (popup / AI); this routes the AgeUpState stamp and the
        /// culture registration so every peer sees the era advance.
        /// </summary>
        public static void IssueAgeUp(EntityManager em, Entity hall, byte culture,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (hall == Entity.Null || !em.Exists(hall)) return;

            if (source == CommandSource.LocalPlayer && em.HasComponent<FactionTag>(hall))
                TheWaningBorder.AI.AILogger.LogPlayer(
                    em.GetComponentData<FactionTag>(hall).Value, "AGEUP", $"culture {culture}");

            if (ShouldQueueForLockstep(source))
                QueueAgeUpForLockstep(em, hall, culture);
            else
                AgeUpCommandDirect(em, hall, culture);
        }

        /// <summary>Apply the age-up on this peer. Duration is recomputed
        /// locally; re-entry safe (no-op when already ageing).</summary>
        public static void AgeUpCommandDirect(EntityManager em, Entity hall, byte culture)
        {
            if (!em.Exists(hall)) return;

            if (em.HasComponent<FactionTag>(hall))
                FactionColors.SetFactionCulture(em.GetComponentData<FactionTag>(hall).Value, culture);

            if (!em.HasComponent<AgeUpState>(hall))
            {
                float duration = CultureConfig.AgeUpDuration;
                em.AddComponentData(hall, new AgeUpState
                {
                    Culture   = culture,
                    Duration  = duration,
                    Remaining = duration,
                });
            }
        }

        /// <summary>
        /// Start a Temple of Ridan level upgrade. Cost is spent by the
        /// caller; target level and duration are recomputed on each peer.
        /// </summary>
        public static void IssueTempleUpgrade(EntityManager em, Entity temple,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (temple == Entity.Null || !em.Exists(temple)) return;

            if (ShouldQueueForLockstep(source))
                QueueTempleUpgradeForLockstep(em, temple);
            else
                TempleUpgradeCommandDirect(em, temple);
        }

        /// <summary>Apply the temple upgrade stamp on this peer. Re-entry
        /// safe (no-op when already upgrading).</summary>
        public static void TempleUpgradeCommandDirect(EntityManager em, Entity temple)
        {
            if (!em.Exists(temple)) return;
            if (!em.HasComponent<TempleLevel>(temple)) return;
            if (em.HasComponent<TempleUpgradeState>(temple)) return;

            int level = em.GetComponentData<TempleLevel>(temple).Level;
            float duration = TempleLevelConfig.GetUpgradeDuration(level);
            em.AddComponentData(temple, new TempleUpgradeState
            {
                TargetLevel = level + 1,
                Duration    = duration,
                Remaining   = duration,
            });
        }

        /// <summary>
        /// Stamp a chapel build slot for an adopted sect. RP + material spend
        /// happen in SectAdoption.TryStartAdoption on the issuing peer; this
        /// routes only the slot stamp so the chapel rises on every peer.
        /// </summary>
        public static void IssueSectAdoption(EntityManager em, Entity temple, string sectId,
            int preferredSlot, float buildTime, CommandSource source = CommandSource.LocalPlayer)
        {
            if (temple == Entity.Null || !em.Exists(temple)) return;
            if (string.IsNullOrEmpty(sectId)) return;

            if (ShouldQueueForLockstep(source))
                QueueSectAdoptionForLockstep(em, temple, sectId, preferredSlot, buildTime);
            else
                SectAdoptionCommandDirect(em, temple, sectId, preferredSlot, buildTime);
        }

        /// <summary>Apply the chapel-slot stamp on this peer. Prefers the
        /// targeted slot, falls back to the first free one; no-ops when the
        /// sect is already building or complete (replay safety).</summary>
        public static void SectAdoptionCommandDirect(EntityManager em, Entity temple,
            string sectId, int preferredSlot, float buildTime)
        {
            if (!em.Exists(temple) || !em.HasBuffer<TempleChapelSlot>(temple)) return;

            var slots = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].State != 0 && slots[i].SectId == sectId) return;
            }

            int idx = preferredSlot;
            if (idx < 0 || idx >= slots.Length || slots[idx].State != 0)
            {
                idx = -1;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].State == 0) { idx = i; break; }
                }
            }
            if (idx < 0) return;

            slots[idx] = new TempleChapelSlot
            {
                Chapel        = Entity.Null,
                SectId        = new Unity.Collections.FixedString64Bytes(sectId),
                State         = 1,
                BuildProgress = 0f,
                BuildTime     = buildTime,
            };
        }

        /// <summary>Append to the research queue on this peer. Used by the
        /// direct path and by LockstepManager dispatch.</summary>
        public static void ResearchCommandDirect(EntityManager em, Entity building, string techId)
        {
            if (!em.HasBuffer<ResearchQueueItem>(building)) return;
            var queue = em.GetBuffer<ResearchQueueItem>(building);
            queue.Add(new ResearchQueueItem
            {
                TechId = new Unity.Collections.FixedString64Bytes(techId)
            });
        }

        /// <summary>
        /// Cancel a training queue slot on a building, refunding the unit's
        /// base cost to the building's faction. Slot 0 (the in-production
        /// entry) is cancellable too — the helper clears
        /// <see cref="TrainingState"/> timers so TrainingSystem promotes the
        /// new slot 0 cleanly on the next tick.
        /// </summary>
        public static void IssueCancelTrain(EntityManager em, Entity building, int slotIndex,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;
            if (!em.HasComponent<TrainingState>(building)) return;
            if (IsBlockedByNotControllable(em, building, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueCancelTrainForLockstep(em, building, slotIndex);
            }
            else
            {
                CancelTrainCommandHelper.Execute(em, building, slotIndex);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CONVERT HUT COMMANDS (Alanthor age-up choice — task-109 phase 2)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a "convert this Gatherer's Hut" command. Only meaningful on
        /// a hut carrying <see cref="GathererHutAgeUpChoice"/> (added by
        /// <c>AgeUpSystem</c> when an Alanthor-cultured faction ages up).
        /// Routes through lockstep in multiplayer; executes the helper
        /// directly in singleplayer.
        /// </summary>
        public static void IssueConvertHut(EntityManager em, Entity hut,
            HutConversionTarget target, CommandSource source = CommandSource.LocalPlayer)
        {
            if (hut == Entity.Null || !em.Exists(hut)) return;
            if (!em.HasComponent<GathererHutAgeUpChoice>(hut)) return;
            if (IsBlockedByNotControllable(em, hut, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueConvertHutForLockstep(em, hut, target);
            }
            else
            {
                ConvertHutCommandHelper.Execute(em, hut, target);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CONVERT SEGMENT TO GATE COMMANDS (task-109 phase 6)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a "convert this wall segment to a 5-wide gate" command.
        /// Only meaningful on an entity carrying <see cref="WallSegmentTag"/>
        /// with a <see cref="WallInstanceRef"/> buffer. The conversion takes
        /// 8 seconds and costs 80 supplies flat (Phase 1 canonical). The
        /// <paramref name="focusInstance"/> is the wall instance the player
        /// clicked — it acts as the centre of the resulting 5-wide gate
        /// region. Pass <see cref="Entity.Null"/> to use the segment
        /// midpoint instead.
        /// </summary>
        public static void IssueConvertSegmentToGate(EntityManager em, Entity segment,
            Entity focusInstance, CommandSource source = CommandSource.LocalPlayer)
        {
            if (segment == Entity.Null || !em.Exists(segment)) return;
            if (!em.HasComponent<WallSegmentTag>(segment)) return;
            // Idempotent: don't double-charge if the conversion is already running.
            if (em.HasComponent<WallSegmentUpgradeState>(segment)) return;
            if (IsBlockedByNotControllable(em, segment, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueConvertSegmentToGateForLockstep(em, segment, focusInstance);
            }
            else
            {
                ConvertSegmentToGateCommandHelper.Execute(em, segment, focusInstance);
            }
        }

        /// <summary>
        /// Authoritative check: can this <paramref name="unitId"/> be queued
        /// at this <paramref name="building"/> right now? Reads
        /// <see cref="BuildingUpgradeState"/> (or <c>TempleLevel</c> for
        /// Temple of Ridan) and compares to the unit's
        /// <c>minBuildingLevel</c> from TechTreeDB. Returns true (with
        /// <paramref name="requiredLevel"/>=1 and the building's display
        /// name) when no gate applies — caller doesn't need to special-case
        /// non-gated units.
        /// </summary>
        public static bool CanTrainAtBuilding(EntityManager em, Entity building, string unitId,
            out int requiredLevel, out string buildingDisplay)
        {
            requiredLevel = 1;
            buildingDisplay = "Building";

            if (!TechCatalog.IsReady) return true;
            if (!TechCatalog.TryGetUnit(unitId, out var unit)) return true;

            int minLv = unit.minBuildingLevel < 1 ? 1 : unit.minBuildingLevel;
            requiredLevel = minLv;
            if (minLv <= 1) return true; // no gate to enforce

            int currentLevel = 1;
            if (em.HasComponent<BuildingUpgradeState>(building))
            {
                int lv = em.GetComponentData<BuildingUpgradeState>(building).Level;
                if (lv > currentLevel) currentLevel = lv;
            }
            if (em.HasComponent<TempleLevel>(building))
            {
                int lv = em.GetComponentData<TempleLevel>(building).Level;
                if (lv > currentLevel) currentLevel = lv;
            }

            // Resolve a nice display name for the notification — prefer the
            // TechTreeDB building name, fall back to the trainer's tag.
            string trainerId = ResolveBuildingIdForTrainer(em, building);
            if (!string.IsNullOrEmpty(trainerId)
                && TechCatalog.TryGetBuilding(trainerId, out var bdef)
                && !string.IsNullOrEmpty(bdef.name))
            {
                buildingDisplay = bdef.name;
            }
            else if (!string.IsNullOrEmpty(trainerId))
            {
                buildingDisplay = trainerId;
            }

            return currentLevel >= minLv;
        }

        // Match the trainer's Tag back to a string id usable for
        // TechTreeDB lookups. Mirrors the chain UpgradeBuildingCommand
        // uses, plus the culture-specific trainers.
        private static string ResolveBuildingIdForTrainer(EntityManager em, Entity e)
        {
            if (em.HasComponent<HallTag>(e))            return "Hall";
            if (em.HasComponent<BarracksTag>(e))        return "Barracks";
            if (em.HasComponent<ArcheryRangeTag>(e))    return "ArcheryRange";
            if (em.HasComponent<RoyalStableTag>(e))     return "Alanthor_RoyalStable";
            if (em.HasComponent<SiegeYardTag>(e))       return "Alanthor_SiegeYard";
            if (em.HasComponent<LonghouseTag>(e))       return "Feraldis_Longhouse";
            if (em.HasComponent<FerSiegeYardTag>(e))    return "Feraldis_SiegeYard";
            if (em.HasComponent<PastureTag>(e))         return "Feraldis_Pasture";
            if (em.HasComponent<BazaarTag>(e))          return "ThessarasBazaar";
            if (em.HasComponent<SiegeWorkshopTag>(e))   return "Runai_SiegeWorkshop";
            if (em.HasComponent<TempleOfRidanTag>(e))   return "TempleOfRidan";
            return string.Empty;
        }

        // ─── Production-queue cap (combined train + research) ────────────
        // A single building queues both unit-training and research orders
        // through separate buffers, but the player perceives them as one
        // production queue. 5 is the cap: train and research orders share it.
        public const int MaxProductionQueue = 5;

        public static int GetTrainQueueLength(EntityManager em, Entity building)
        {
            if (!em.HasBuffer<TrainQueueItem>(building)) return 0;
            return em.GetBuffer<TrainQueueItem>(building).Length;
        }

        public static int GetResearchQueueLength(EntityManager em, Entity building)
        {
            if (!em.HasBuffer<ResearchQueueItem>(building)) return 0;
            return em.GetBuffer<ResearchQueueItem>(building).Length;
        }

        /// <summary>
        /// True when this building's combined train + research queue is at the
        /// cap. UI / AI / command paths should consult this before adding
        /// another order.
        /// </summary>
        public static bool IsProductionQueueFull(EntityManager em, Entity building)
        {
            return GetTrainQueueLength(em, building) + GetResearchQueueLength(em, building)
                   >= MaxProductionQueue;
        }

        private static void TrainCommandDirect(EntityManager em, Entity building, string unitId)
        {
            if (!em.HasBuffer<TrainQueueItem>(building))
            {
                // Silent-drop instrumentation (2026-08-04, "sect unit button
                // doesn't work" hunt): a train order landing on a building
                // with no queue is a wiring bug — say so instead of eating
                // the click (and the player's already-spent cost).
                TWBLog.Log($"[CommandRouter] TRAIN '{unitId}' DROPPED — target " +
                           $"building has no TrainQueueItem buffer.");
                return;
            }
            // Belt-and-suspenders: IssueTrain already filters above, but
            // direct callers (post-lockstep apply, scripted spawns) hit
            // this path bypass-style. Silent drop on level mismatch since
            // the originating context already surfaced the failure.
            if (!CanTrainAtBuilding(em, building, unitId, out _, out _)) return;
            // One-per-player hero gate: only one live/queued King Lexor per faction.
            if (TheWaningBorder.Abilities.HeroTrainLimit.IsKingLexorId(unitId) &&
                em.HasComponent<FactionTag>(building) &&
                TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedKingLexor(em, em.GetComponentData<FactionTag>(building).Value))
            {
                TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify("King Lexor already serves your realm");
                return;
            }
            // Same gate for the Ledger: one automaton per player.
            if (TheWaningBorder.Abilities.HeroTrainLimit.IsLedgerId(unitId) &&
                em.HasComponent<FactionTag>(building) &&
                TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedLedger(em, em.GetComponentData<FactionTag>(building).Value))
            {
                TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify("Your court already employs a Ledger");
                return;
            }
            // Reject when combined production queue would exceed the cap.
            if (IsProductionQueueFull(em, building))
            {
                TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify("Production queue full");
                return;
            }
            var queue = em.GetBuffer<TrainQueueItem>(building);
            queue.Add(new TrainQueueItem { UnitId = new Unity.Collections.FixedString64Bytes(unitId) });
        }

        // ═══════════════════════════════════════════════════════════════
        // PLACE BUILDING COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Smelter (Forge) build cap per faction. Raised from 1 to 5
        /// (endgame completeness pass) — enforced here at the single
        /// replicated entry point so UI, AI and lockstep peers all agree.
        /// The build-menu gate (EntityExtractors.Buildings SmelterCap)
        /// mirrors this value.</summary>
        public const int MaxSmeltersPerFaction = 5;

        /// <summary>Count this faction's Smelters (completed AND under
        /// construction) for the placement cap check.</summary>
        private static int CountFactionSmelters(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<SmelterTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var facs = query.ToComponentDataArray<FactionTag>(
                Unity.Collections.Allocator.Temp);
            int count = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) count++;
            return count;
        }

        /// <summary>
        /// Issue a place-building command. Creates the building on all clients via lockstep.
        /// Returns true if the command was queued (multiplayer) or executed (singleplayer).
        /// In multiplayer, the caller must NOT create the building locally — lockstep will do it.
        /// </summary>
        public static bool IssuePlaceBuilding(EntityManager em, string buildingId, float3 position,
            Faction faction, CommandSource source = CommandSource.LocalPlayer)
        {
            return IssuePlaceBuilding(em, buildingId, position, faction, out _, source);
        }

        /// <summary>
        /// Place-building overload that hands back the created entity on the
        /// direct path (<paramref name="created"/> is Entity.Null when the
        /// command was queued — it will exist on every peer two ticks later).
        /// Lets callers like the AI keep their dispatch/rollback logic in
        /// single-player while replicating correctly in multiplayer.
        /// </summary>
        public static bool IssuePlaceBuilding(EntityManager em, string buildingId, float3 position,
            Faction faction, out Entity created, CommandSource source = CommandSource.LocalPlayer)
        {
            created = Entity.Null;

            // Smelter cap (5 per faction). Rejected here so callers with a
            // spend-then-place flow (AI TryBuildOnce) see created == Null and
            // refund cleanly; the UI normally hides the button first.
            if (buildingId == "Alanthor_Smelter"
                && CountFactionSmelters(em, faction) >= MaxSmeltersPerFaction)
                return false;

            if (source == CommandSource.LocalPlayer)
                TheWaningBorder.AI.AILogger.LogPlayer(faction, "BUILD",
                    $"{buildingId} at ({position.x:0},{position.z:0})");

            if (ShouldQueueForLockstep(source))
            {
                var cmd = new LockstepCommand
                {
                    Type = LockstepCommandType.PlaceBuilding,
                    BuildingId = buildingId,
                    TargetPosition = position,
                    EntityNetworkId = (int)faction // Carry faction in EntityNetworkId
                };
                LockstepServiceLocator.Instance.QueueCommand(cmd);
                return true; // Queued — caller must NOT create entity locally
            }
            else
            {
                // Single player — create immediately
                created = PlaceBuildingDirect(em, buildingId, position, faction);
                return false; // Created locally — caller can proceed
            }
        }

        /// <summary>
        /// Execute building placement: create entity, mark under construction, set HP to 1.
        /// Called by lockstep ExecuteCommand on all clients, or directly in singleplayer.
        /// </summary>
        public static Entity PlaceBuildingDirect(EntityManager em, string buildingId, float3 position, Faction faction)
        {
            Entity building = TheWaningBorder.Entities.BuildingFactory.Create(em, buildingId, position, faction);

            // Mark as under construction
            float buildTime = GetBuildTime(buildingId);
            if (!em.HasComponent<UnderConstruction>(building))
                em.AddComponentData(building, new UnderConstruction { Progress = 0f, Total = buildTime });
                else
                    em.SetComponentData(building, new UnderConstruction { Progress = 0f, Total = buildTime });

            // Set HP to 1 during construction
            if (em.HasComponent<Health>(building))
            {
                var hp = em.GetComponentData<Health>(building);
                em.SetComponentData(building, new Health { Value = 1, Max = hp.Max });
            }

            // Choice buildings (Shrine / Vault / Keep) self-construct with no
            // builder over 90 s (design: Age_0.md § Special buildings).
            // Builders can still be sent to accelerate — each contributes
            // +25 % build rate in BuildingConstructionSystem, so 4 workers
            // halve the time. Deterministic across lockstep peers (this
            // method runs on every client).
            if (TheWaningBorder.Entities.BuildingFactory.IsChoiceBuilding(buildingId)
                && !em.HasComponent<AutoConstructTag>(building))
            {
                em.AddComponent<AutoConstructTag>(building);
            }

            // Builder-placed Halls (post-age-up expansion, capped at 6) inherit
            // the faction's current culture so culture-driven queries that
            // pick "the first hall" stay consistent — EntityActionExtractor and
            // CultureChoicePopup both read FactionProgress off whichever Hall
            // they hit first. Hall.Create stamps Culture=None unconditionally,
            // so we override here. FactionColors.GetFactionCulture is
            // deterministic across lockstep peers (set by AgeUpSystem during
            // tick replay), so this works for both single-player and
            // multiplayer paths.
            if (buildingId == "Hall" && em.HasComponent<FactionProgress>(building))
            {
                byte culture = FactionColors.GetFactionCulture(faction);
                em.SetComponentData(building, new FactionProgress { Culture = culture });
            }

            return building;
        }

        private static float GetBuildTime(string buildingId)
        {
            return buildingId switch
            {
                "Hut" => 15f,
                "GatherersHut" => 20f,
                "Hall" => 50f,
                "Barracks" or "ArcheryRange" => 30f,
                "TempleOfRidan" => 40f,
                // Choice buildings: 90 s self-build (no builder needed —
                // AutoConstructTag is added in PlaceBuildingDirect).
                // "ShrineOfAhridan" is the legacy pre-rename id alias.
                "ShrineOfRidan" or "ShrineOfAhridan"
                    or "VaultOfAlmierra" or "FiendstoneKeep" => 90f,
                "Alanthor_Smelter" => 30f,
                "Alanthor_RoyalStable" => 30f,
                "Alanthor_Tower" or "Feraldis_HuntingLodge" or "Feraldis_LoggingStation"
                    or "Feraldis_Tower" or "Runai_Outpost" => 25f,
                "Feraldis_WarTotem" => 15f,
                "Feraldis_Pasture" => 30f,
                "Mine" => 25f,
                "Feraldis_Longhouse" or "Runai_TradeHub" => 30f,
                "Alanthor_SiegeYard" or "Runai_SiegeWorkshop"
                    or "Feraldis_SiegeYard" => 35f,
                "ThessarasBazaar" => 40f,
                _ => 30f
            };
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
        // LOCKSTEP QUEUE METHODS — moved to CommandRouter.LockstepQueue.cs
        // (Fix #224). Both partial files share `GetNetworkId` above and the
        // direct-execution helpers below.
        // ═══════════════════════════════════════════════════════════════

        private static void SetRallyPointDirect(EntityManager em, Entity building, float3 position,
            Entity targetEntity = default)
        {
            if (!em.HasComponent<RallyPoint>(building))
                em.AddComponent<RallyPoint>(building);
            em.SetComponentData(building, new RallyPoint
            {
                Position     = position,
                Has          = 1,
                TargetEntity = targetEntity,
            });
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
            if (em.HasComponent<Types.GatherVeilCommand>(unit))
                em.RemoveComponent<Types.GatherVeilCommand>(unit);
            if (em.HasComponent<Types.BuildCommand>(unit))
                em.RemoveComponent<Types.BuildCommand>(unit);
            if (em.HasComponent<BuildOrder>(unit))
                em.RemoveComponent<BuildOrder>(unit);
            if (em.HasComponent<RepairOrder>(unit))
                em.RemoveComponent<RepairOrder>(unit);
            if (em.HasComponent<Types.HealCommand>(unit))
                em.RemoveComponent<Types.HealCommand>(unit);
            // Clear Litharch healing state (healing system uses LitharchState, not HealCommand)
            if (em.HasComponent<LitharchState>(unit))
            {
                var ls = em.GetComponentData<LitharchState>(unit);
                if (ls.IsHealing != 0)
                {
                    ls.HealTarget = Entity.Null;
                    ls.IsHealing = 0;
                    em.SetComponentData(unit, ls);
                }
            }
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
            if (em.HasComponent<AbilityActivated>(unit))
                em.RemoveComponent<AbilityActivated>(unit);
            if (em.HasComponent<CommandQueueActive>(unit))
                em.RemoveComponent<CommandQueueActive>(unit);
            if (em.HasBuffer<QueuedCommand>(unit))
                em.GetBuffer<QueuedCommand>(unit).Clear();
            // Cancel a pending or in-progress ritual when any other command
            // is issued. PurificationRitualSystem / ConversionRitualSystem
            // also clear ActiveRitualOnNode on the targeted node when they
            // observe the command removed.
            if (em.HasComponent<PurifyCommand>(unit))
                em.RemoveComponent<PurifyCommand>(unit);
            if (em.HasComponent<ConvertNodeCommand>(unit))
                em.RemoveComponent<ConvertNodeCommand>(unit);
            if (em.HasComponent<RitualState>(unit))
                em.RemoveComponent<RitualState>(unit);
            // Formation travel state: Stop (or any full reset) detaches the
            // unit from its group and drops the group-speed override so the
            // next order runs at the unit's own speed.
            if (em.HasComponent<FormationMemberState>(unit))
                em.RemoveComponent<FormationMemberState>(unit);
            if (em.HasComponent<FormationSpeedOverride>(unit))
                em.RemoveComponent<FormationSpeedOverride>(unit);
        }
    }
}