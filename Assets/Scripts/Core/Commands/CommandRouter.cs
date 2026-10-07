// CommandRouter.cs
// Unified command routing system for local player, remote player, and AI

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Localization;

using TheWaningBorder.Core;
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

        #region Cached queries

        // CreateEntityQuery registers a new query with the world on every call.
        // None of these three were disposed, so every equipment upgrade, god
        // power and building-cap check leaked one into the registry — and a
        // bloated registry slows every later query AND every structural
        // change. See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] EquipTierTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadWrite<FactionEquipmentTier>(),
        };
        static CachedEntityQuery _equipTierQuery;

        static readonly ComponentType[] GodPowerTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadWrite<GodPowerState>(),
        };
        static CachedEntityQuery _godPowerQuery;

        /// <summary>One cached "tagged T owned by a faction" query per T —
        /// statics in a generic class are per constructed type, which is
        /// exactly the lifetime a generic helper needs.</summary>
        static class TaggedFaction<T> where T : unmanaged, IComponentData
        {
            public static readonly ComponentType[] Types =
                { ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>() };
            public static CachedEntityQuery Query;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Enable detailed logging of all commands (useful for debugging sync issues)
        /// </summary>
        public static bool LogCommands = false;

        // Fix #235: the nested `CommandSource` enum was removed. The canonical
        // definition lives in CommandSource.cs at the namespace level
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
            if (ShouldDropCommand(source)) return;
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;

            // Gated BEFORE the string is built: per-unit lines are off by
            // default (AILogger.asset, logUnitCommands).
            if (source == CommandSource.AI && TheWaningBorder.AI.AILogger.LogsUnitCommands
                && em.HasComponent<FactionTag>(unit))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(unit).Value, "CMD",
                    $"move -> ({destination.x:0},{destination.z:0})");

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
            if (ShouldDropCommand(source)) return;
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;
            if (target == Entity.Null || !em.Exists(target)) return;

            // Gated BEFORE the string is built: per-unit lines are off by
            // default (AILogger.asset, logUnitCommands).
            if (source == CommandSource.AI && TheWaningBorder.AI.AILogger.LogsUnitCommands
                && em.HasComponent<FactionTag>(unit))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(unit).Value, "CMD",
                    $"attack {TheWaningBorder.Entities.BuildingIds.Of(target, em) ?? "unit"} " +
                    (em.HasComponent<Unity.Transforms.LocalTransform>(target)
                        ? $"at ({em.GetComponentData<Unity.Transforms.LocalTransform>(target).Position.x:0},{em.GetComponentData<Unity.Transforms.LocalTransform>(target).Position.z:0})"
                        : ""));

            // A WELL IS NOT A TARGET UNTIL IT IS CRACKED (2026-09-08).
            //
            // Verb wells are Feraldis-only attack targets (2026-08-04): Age 0
            // and Alanthor/Runai factions can never attack a well — their
            // verbs are Purify / Pacify. Only the Feraldis culture breaks
            // wells by force.
            //
            // But FERALDIS DOES NOT BREAK ONE WITH ARROWS EITHER. Its verb is
            // the Iconoclast's corruption ritual, and the ritual is what opens
            // the well to arms: CorruptionRitualSystem strips NodeNoAutoAcquire
            // when the channel completes. Until that happens an army ordered
            // onto a well is ordered at something the ritual has not yet made
            // breakable, so the order is refused for Feraldis exactly as it is
            // for everyone else — the difference is that Feraldis has a way to
            // change the answer. docs/Design/Curse_And_Shardroot.md
            if (em.HasComponent<BorderMainNodeTag>(target))
            {
                var attackerFaction = em.HasComponent<FactionTag>(unit)
                    ? em.GetComponentData<FactionTag>(unit).Value : Faction.Blue;
                bool feraldis = FactionColors.GetFactionCulture(attackerFaction) == Cultures.Feraldis;
                if (!feraldis)
                {
                    if (source == CommandSource.LocalPlayer)
                        SimSignals.Notify(
                            Loc.T("The well resists all arms — only Feraldis may break it"));
                    return;
                }
                if (em.HasComponent<NodeNoAutoAcquire>(target))
                {
                    if (source == CommandSource.LocalPlayer)
                        SimSignals.Notify(
                            Loc.T("The well is sealed — an Iconoclast must crack it open first"));
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
            if (ShouldDropCommand(source)) return;
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;

            // Gated BEFORE the string is built: per-unit lines are off by
            // default (AILogger.asset, logUnitCommands).
            if (source == CommandSource.AI && TheWaningBorder.AI.AILogger.LogsUnitCommands
                && em.HasComponent<FactionTag>(unit))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(unit).Value, "CMD",
                    $"attack-move -> ({destination.x:0},{destination.z:0})");

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
            if (ShouldDropCommand(source)) return;
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
            if (ShouldDropCommand(source)) return;
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
            if (ShouldDropCommand(source)) return;
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
            if (ShouldDropCommand(source)) return;
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
        /// Issue a build command to a worker unit.
        /// </summary>
        public static void IssueBuild(EntityManager em, Entity worker, Entity targetBuilding,
            string buildingId, float3 position, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (worker == Entity.Null || !em.Exists(worker)) return;
            if (IsBlockedByNotControllable(em, worker, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueBuildForLockstep(em, worker, targetBuilding, buildingId, position);
            }
            else
            {
                BuildCommandHelper.Execute(em, worker, targetBuilding, buildingId, position);
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
            if (ShouldDropCommand(source)) return;
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
        // CONVERT COMMANDS (Worker → Berserker at Fiendstone Keep)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a convert command to a worker unit targeting a Fiendstone Keep.
        /// </summary>
        public static void IssueConvert(EntityManager em, Entity worker, Entity keep,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (worker == Entity.Null || !em.Exists(worker)) return;
            if (IsBlockedByNotControllable(em, worker, source)) return;
            if (keep == Entity.Null || !em.Exists(keep)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueConvertForLockstep(em, worker, keep);
            }
            else
            {
                ConvertCommandHelper.Execute(em, worker, keep);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // REPAIR COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a repair command to a worker unit targeting a damaged building.
        /// </summary>
        public static void IssueRepair(EntityManager em, Entity worker, Entity building,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (worker == Entity.Null || !em.Exists(worker)) return;
            if (IsBlockedByNotControllable(em, worker, source)) return;
            if (building == Entity.Null || !em.Exists(building)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueRepairForLockstep(em, worker, building);
            }
            else
            {
                RepairCommandHelper.Execute(em, worker, building);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // RALLY POINT COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Set rally point for a building. <paramref name="targetEntity"/>
        /// is an optional follow-up target (e.g. a resource node) that
        /// post-spawn handlers may use — TrainingSystem auto-issues a
        /// gather command on workers when this points at an iron / veilstone
        /// deposit. Pass Entity.Null for plain "walk here" rallies.
        /// </summary>
        public static void SetRallyPoint(EntityManager em, Entity building, float3 position,
            Entity targetEntity = default,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (building == Entity.Null || !em.Exists(building)) return;

            // A resource rally is anchored to the NODE'S CELL CENTRE, not the
            // pixel that was clicked. The click lands anywhere on the node's
            // 2 m collider, so a raw click point put the marker off-centre and
            // the guide line pointed at the node's edge. Snapping here rather
            // than at the call site keeps every caller — click router, AI, any
            // future UI — consistent, and the lockstep payload carries the
            // snapped position too (it cannot replicate TargetEntity).
            if (ResourceNodeQuery.TryGetCellCentre(em, targetEntity, out var nodeCentre))
                position = nodeCentre;

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
            if (ShouldDropCommand(source)) return false;
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
            var q = _equipTierQuery.Get(em, EquipTierTypes);
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
            // Muster Yard (War): per-battalion upgrades cost 50% less while one
            // stands. Non-stacking, and read from world state that is identical
            // on every peer, so the debit stays deterministic under lockstep.
            cost = TheWaningBorder.Economy.MusterYardDiscount.Apply(em, faction, cost);
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Research))
                return false;

            switch (unitClass)
            {
                case UnitClass.Melee:   tiers.Melee   = targetTier; break;
                case UnitClass.Ranged:  tiers.Ranged  = targetTier; break;
                case UnitClass.Siege:   tiers.Siege   = targetTier; break;
                case UnitClass.Magic:   tiers.Magic   = targetTier; break;
                case UnitClass.Support: tiers.Support = targetTier; break;
                default: return false;  // Economy / Worker / Scout don't take equipment
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
            if (ShouldDropCommand(source)) return false;
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
            var q = _godPowerQuery.Get(em, GodPowerTypes);
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
            if (ShouldDropCommand(source)) return;
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
        /// docs/Design/Age_1_Feraldis.md § Corruptor). Mirrors IssuePurify,
        /// opcode included (Corrupt = 38): the direct write used to exist on
        /// the issuing peer alone (docs/Multiplayer_Desync_Sweep_2026-08-16.md).
        /// </summary>
        public static void IssueCorrupt(EntityManager em, Entity corruptor, Entity node,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (corruptor == Entity.Null || !em.Exists(corruptor)) return;
            if (node == Entity.Null || !em.Exists(node)) return;
            if (IsBlockedByNotControllable(em, corruptor, source)) return;
            if (!em.HasComponent<CorruptorTag>(corruptor)) return;
            if (!em.HasComponent<BorderMainNodeTag>(node)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueCorruptForLockstep(em, corruptor, node);
                return;
            }
            IssueCorruptDirect(em, corruptor, node);
        }

        /// <summary>Execute IssueCorrupt on this peer. Used by LockstepManager dispatch.</summary>
        public static void IssueCorruptDirect(EntityManager em, Entity corruptor, Entity node)
        {
            if (!em.Exists(corruptor) || !em.Exists(node)) return;
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

            // The well is the Scholar's new post (docs/Design/Stances.md §2):
            // guard from the stand point beside it, so the moment the rite
            // ends (or breaks) return-to-guard does not walk it back to
            // wherever it was trained. PurificationRitualSystem re-plants it
            // on the exact spot where the channel starts.
            if (em.HasComponent<Unity.Transforms.LocalTransform>(scholar) && em.HasComponent<Unity.Transforms.LocalTransform>(node))
            {
                var stand = TheWaningBorder.Systems.Border.RitualApproach.StandPoint(
                    em.GetComponentData<Unity.Transforms.LocalTransform>(node).Position,
                    em.GetComponentData<Unity.Transforms.LocalTransform>(scholar).Position);
                var gp = new GuardPoint { Position = stand, Has = 1 };
                if (em.HasComponent<GuardPoint>(scholar)) em.SetComponentData(scholar, gp);
                else em.AddComponentData(scholar, gp);
            }
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
            if (ShouldDropCommand(source)) return;
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
            if (ShouldDropCommand(source)) return;
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
        /// <param name="slot">UnitAbilities slot to fire, or -1 for the unit's
        /// first ready Active. Heroes carry several actives and must name one
        /// (docs/Design/Heroes.md §2); every other unit has one and passes -1.
        /// Note the -1 is passed EXPLICITLY: AbilityActivated.Slot is an int
        /// whose default is 0, which would mean "fire slot 0".</param>
        public static void IssueAbilityDirect(EntityManager em, Entity unit, Entity target,
            int slot = -1)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            // Enableable, pre-added disabled on units (TransientState.cs).
            TransientState.Set(em, unit, new AbilityActivated { Target = target, Slot = slot });
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
            => IssueUnitAbility(em, unit, target, null, source);

        /// <summary>Cast a NAMED ability slot. See the slot note on
        /// IssueAbilityDirect.</summary>
        public static bool IssueUnitAbilitySlot(EntityManager em, Entity unit, int slot,
            Entity target = default, float3? aimPoint = null,
            CommandSource source = CommandSource.LocalPlayer)
            => IssueUnitAbility(em, unit, target, aimPoint, source, slot);

        /// <summary>
        /// Cast with an optional AIMED ground point, for Area abilities the
        /// player pointed at with the targeting ring (Use Celestar). The point
        /// is stamped on the caster and read when the effects fire — the
        /// pipeline's Entity target cannot express "that patch of empty map".
        /// </summary>
        public static bool IssueUnitAbility(EntityManager em, Entity unit, Entity target,
            float3? aimPoint,
            CommandSource source = CommandSource.LocalPlayer,
            int slot = -1)
        {
            if (ShouldDropCommand(source)) return false;
            if (!em.Exists(unit)) return false;
            if (IsBlockedByNotControllable(em, unit, source)) return false;
            if (!em.HasComponent<TheWaningBorder.Abilities.UnitAbilities>(unit)) return false;
            if (!TheWaningBorder.Abilities.AbilityQuery.HasReadyActiveAbility(em, unit)) return false;

            // Under lockstep the aim rides the command and is stamped when it
            // executes, on every peer. Stamping it here would write it on the
            // issuing machine only: the other peers would reveal somewhere
            // else, and the component add is a structural change on one
            // world alone.
            if (ShouldQueueForLockstep(source))
            {
                QueueAbilityForLockstep(em, unit, target, slot, aimPoint);
                return true;
            }
            StampAbilityAim(em, unit, aimPoint);
            IssueAbilityDirect(em, unit, target, slot);
            return true;
        }

        /// <summary>
        /// Stamp (or clear) the aimed ground point BEFORE the cast starts, so a
        /// previous cast's point can never leak into an unaimed one. Called on
        /// the direct path and by the lockstep executor, never at issue time
        /// under lockstep.
        /// </summary>
        public static void StampAbilityAim(EntityManager em, Entity unit, float3? aimPoint)
        {
            if (!em.Exists(unit)) return;
            if (aimPoint.HasValue)
            {
                var aim = new TheWaningBorder.Abilities.AbilityAimPoint { Position = aimPoint.Value };
                if (em.HasComponent<TheWaningBorder.Abilities.AbilityAimPoint>(unit))
                    em.SetComponentData(unit, aim);
                else
                    em.AddComponentData(unit, aim);
            }
            else if (em.HasComponent<TheWaningBorder.Abilities.AbilityAimPoint>(unit))
            {
                em.RemoveComponent<TheWaningBorder.Abilities.AbilityAimPoint>(unit);
            }
        }

        /// <summary>Bit set in an Ability command's SecondaryTargetId when
        /// TargetPosition carries an aimed ground point. The low bits keep the
        /// slot+1 encoding, so an unaimed command decodes exactly as before.</summary>
        public const int AbilityAimFlag = 1 << 20;

        // ═══════════════════════════════════════════════════════════════
        // TRAIN COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a train command to queue a unit at a building.
        /// </summary>
        /// <param name="revival">Which revival the player is paying for when
        /// this unit is a fallen hero (docs/Design/Heroes.md §4). None for
        /// every ordinary train, and for a hero who has never died.
        /// The MODE has to cross the wire — the two options charge different
        /// prices and return different levels, so a peer that guessed would
        /// fork both the bank and the hero.</param>
        public static void IssueTrain(EntityManager em, Entity building, string unitId,
            CommandSource source = CommandSource.LocalPlayer,
            TheWaningBorder.Abilities.HeroRevivalMode revival
                = TheWaningBorder.Abilities.HeroRevivalMode.None)
        {
            if (ShouldDropCommand(source)) return;
            if (building == Entity.Null || !em.Exists(building)) return;

            if (source == CommandSource.LocalPlayer && em.HasComponent<FactionTag>(building))
                TheWaningBorder.AI.AILogger.LogPlayer(
                    em.GetComponentData<FactionTag>(building).Value, "TRAIN", unitId);
            else if (source == CommandSource.AI && em.HasComponent<FactionTag>(building))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(building).Value, "CMD", $"train {unitId}");

            // Authoritative level gate. Local-player path surfaces the
            // failure as a notification so the click feels intentional;
            // AI / network paths drop silently (the issuer either has
            // its own gating or shouldn't issue an invalid order in the
            // first place).
            if (!CanTrainAtBuilding(em, building, unitId, out int requiredLevel, out string buildingDisplay))
            {
                if (source == CommandSource.LocalPlayer)
                {
                    SimSignals.Notify(
                        string.Format(Loc.T("Requires Lv {0} {1}"), requiredLevel, buildingDisplay));
                }
                return;
            }

            if (ShouldQueueForLockstep(source))
            {
                QueueTrainForLockstep(em, building, unitId, revival);
            }
            else
            {
                TrainCommandDirect(em, building, unitId, revival);
            }
        }

        /// <summary>
        /// Queue a technology on a building's research queue. The COST is
        /// charged inside ResearchCommandDirect — the executor that runs on
        /// EVERY peer — never at the issue site. Spending at the issue site
        /// debited the issuing peer's bank only, and the faction banks are
        /// folded into the desync checksum, so the first research of any
        /// multiplayer match desynced it (docs/Multiplayer_LAN_Readiness.md).
        /// Callers keep a CanAfford check for UX gating only.
        /// </summary>
        public static void IssueResearch(EntityManager em, Entity building, string techId,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (building == Entity.Null || !em.Exists(building)) return;
            if (string.IsNullOrEmpty(techId)) return;
            if (!em.HasBuffer<ProductionQueueItem>(building)) return;

            if (source == CommandSource.LocalPlayer && em.HasComponent<FactionTag>(building))
                TheWaningBorder.AI.AILogger.LogPlayer(
                    em.GetComponentData<FactionTag>(building).Value, "RESEARCH", techId);
            else if (source == CommandSource.AI && em.HasComponent<FactionTag>(building))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(building).Value, "CMD", $"research {techId}");

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
        /// Start a building level-up. UpgradeBuildingCommandHelper.Execute
        /// (the caller) VALIDATES for UX feedback; the cost is spent inside
        /// ApplyDirect so the debit lands on every peer alongside the state
        /// mutation (docs/Multiplayer_LAN_Readiness.md).
        /// </summary>
        public static void IssueBuildingUpgrade(EntityManager em, Entity building,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
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
        /// Start the Hall's age-up with the chosen culture. Cost is spent in
        /// AgeUpCommandDirect on EVERY peer — never by the caller (popup /
        /// AI keep a CanAfford check for UX/decision gating only). Issuer-
        /// side spends forked the banks and with them the desync checksum
        /// (docs/Multiplayer_LAN_Readiness.md).
        /// </summary>
        public static void IssueAgeUp(EntityManager em, Entity hall, byte culture,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (hall == Entity.Null || !em.Exists(hall)) return;

            if (source == CommandSource.LocalPlayer && em.HasComponent<FactionTag>(hall))
                TheWaningBorder.AI.AILogger.LogPlayer(
                    em.GetComponentData<FactionTag>(hall).Value, "AGEUP", $"culture {culture}");
            else if (source == CommandSource.AI && em.HasComponent<FactionTag>(hall))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(hall).Value, "CMD", $"age-up culture {culture}");

            if (ShouldQueueForLockstep(source))
                QueueAgeUpForLockstep(em, hall, culture);
            else
                AgeUpCommandDirect(em, hall, culture);
        }

        /// <summary>Apply the age-up on this peer. Duration is recomputed
        /// locally; re-entry safe (no-op when already ageing — checked
        /// BEFORE the spend so a duplicate command cannot double-charge).
        /// Validates affordability and SPENDS here so every peer debits the
        /// same bank; a short bank rejects the whole command identically
        /// everywhere (no partial effects, no culture registration).</summary>
        public static void AgeUpCommandDirect(EntityManager em, Entity hall, byte culture)
        {
            // Entry is LOUD (2026-08-31): the whole path from issue to era-2
            // went dark across five batches; every branch of it reports now.
            UnityEngine.Debug.Log($"[AgeUp] direct: hall={hall.Index} culture={culture} " +
                $"exists={em.Exists(hall)} ageing={(em.Exists(hall) && em.HasComponent<AgeUpState>(hall))}");
            if (!em.Exists(hall)) return;
            if (em.HasComponent<AgeUpState>(hall)) return;

            if (em.HasComponent<FactionTag>(hall))
            {
                var faction = em.GetComponentData<FactionTag>(hall).Value;
                if (!TheWaningBorder.Economy.FactionEconomy.Spend(
                        em, faction, CultureConfig.AgeUpCost, TheWaningBorder.Economy.SpendCategory.AgeUp))
                {
                    // NEVER silent (2026-08-31): this drop is invisible to
                    // the issuer — the check-then-playback gap means the
                    // bank can be raided between the caller's CanAfford and
                    // this spend, and five batches of "why is nobody era 2"
                    // traced to exactly here.
                    UnityEngine.Debug.Log(
                        $"[AgeUp] {faction} DROPPED — bank short at playback " +
                        $"(cost {CultureConfig.AgeUpCost.Supplies}s/" +
                        $"{CultureConfig.AgeUpCost.Iron}i/" +
                        $"{CultureConfig.AgeUpCost.Veilstone}v).");
                    return;
                }
                FactionColors.SetFactionCulture(faction, culture);
            }

            float duration = CultureConfig.AgeUpDuration;
            em.AddComponentData(hall, new AgeUpState
            {
                Culture   = culture,
                Duration  = duration,
                Remaining = duration,
            });
        }

        /// <summary>
        /// Stamp a chapel build slot for an adopted sect. RP + material spend
        /// happen in SectAdoptionCommandDirect on EVERY peer — the issuer
        /// only VALIDATES (SectAdoption.ValidateAdoption) so the click can be
        /// rejected with feedback. Spending at the issue site debited one
        /// peer's bank and RP pool only (docs/Multiplayer_LAN_Readiness.md).
        /// </summary>
        public static void IssueSectAdoption(EntityManager em, Entity temple, string sectId,
            int preferredSlot, float buildTime, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (temple == Entity.Null || !em.Exists(temple)) return;
            if (string.IsNullOrEmpty(sectId)) return;

            if (ShouldQueueForLockstep(source))
                QueueSectAdoptionForLockstep(em, temple, sectId, preferredSlot, buildTime);
            else
                SectAdoptionCommandDirect(em, temple, sectId, preferredSlot, buildTime);
        }

        /// <summary>Apply the chapel-slot stamp on this peer. Prefers the
        /// targeted slot, falls back to the first free one; no-ops when the
        /// sect is already building or complete (replay safety). Validates
        /// and SPENDS the RP + chapel material cost here — both live in
        /// replicated ECS state on the bank entity, so charging in the
        /// executor keeps every peer's bank (and the desync checksum)
        /// aligned. A failed spend rejects the whole command identically on
        /// every peer: no slot stamp, no partial effects.</summary>
        public static void SectAdoptionCommandDirect(EntityManager em, Entity temple,
            string sectId, int preferredSlot, float buildTime)
        {
            if (!em.Exists(temple) || !em.HasBuffer<TempleChapelSlot>(temple)) return;
            if (!em.HasComponent<FactionTag>(temple)) return;

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

            var faction = em.GetComponentData<FactionTag>(temple).Value;
            if (!TheWaningBorder.Economy.SectAdoption.TrySpendAdoptionCosts(em, faction, sectId))
                return;

            // Re-fetch the buffer: the spend wrote other components on the
            // bank entity and a stale buffer handle would throw.
            slots = em.GetBuffer<TempleChapelSlot>(temple);
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
        /// direct path and by LockstepManager dispatch. Validates
        /// affordability and SPENDS here so single-player and every
        /// multiplayer peer debit the same bank at the same tick; rejects
        /// the whole command identically everywhere when the bank is short
        /// (no partial effects).</summary>
        public static void ResearchCommandDirect(EntityManager em, Entity building, string techId)
        {
            if (!em.Exists(building)) return;
            if (!em.HasBuffer<ProductionQueueItem>(building)) return;
            if (string.IsNullOrEmpty(techId)) return;

            // The queue needs its clock. A host that carries the buffer but no
            // ProductionState (every Wall Hub raised before 2026-09-27) would
            // take the money and never start — back-fill it here, the same
            // way UpgradeBuildingCommand does for a level-up.
            if (!em.HasComponent<ProductionState>(building))
                em.AddComponentData(building, default(ProductionState));

            // Cost comes from the shared TechCatalog — identical data on all
            // peers, so the debit is deterministic. A tech missing from the
            // catalog enqueues free (same lenient fallback the old UI spend
            // path had for a zero-cost button).
            if (em.HasComponent<FactionTag>(building))
            {
                var faction = em.GetComponentData<FactionTag>(building).Value;

                // One-shot per faction: a tech already researched, or already
                // waiting in ANY of this faction's queues, is refused BEFORE
                // the spend. Without this a double-click, a second selected
                // host or a replayed AI order charged the price twice for one
                // effect (the second copy was later dropped at the head with
                // no refund). Reads only replicated sim state, so every peer
                // refuses the same command.
                var research = TheWaningBorder.Economy.FactionResearchState.Instance;
                if (research != null && research.HasResearched(faction, techId)) return;
                if (IsResearchQueued(em, faction, techId, out _, out _)) return;

                var cost = ResearchCost(faction, techId);
                if (!cost.IsZero
                    && !TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Research))
                    return;
            }

            var queue = em.GetBuffer<ProductionQueueItem>(building);
            queue.Add(new ProductionQueueItem
            {
                Kind = ProductionKind.Research,
                Id   = new Unity.Collections.FixedString64Bytes(techId),
            });
        }

        static readonly ComponentType[] QT_ResearchHosts =
        {
            ComponentType.ReadOnly<ProductionQueueItem>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_ResearchHosts;

        /// <summary>
        /// Whether <paramref name="techId"/> sits in any of
        /// <paramref name="faction"/>'s production queues (running or
        /// waiting). <paramref name="host"/> / <paramref name="slot"/> name
        /// where; when several hosts carry it the lowest entity index wins, so
        /// every caller (and every peer) names the same one. The runtime twin
        /// of the UI's IsTechQueued, used by the research executor's
        /// duplicate guard and by the wall-level lock.
        /// </summary>
        public static bool IsResearchQueued(EntityManager em, Faction faction, string techId,
            out Entity host, out int slot)
        {
            host = Entity.Null;
            slot = -1;
            if (string.IsNullOrEmpty(techId)) return false;

            var q = QC_ResearchHosts.Get(em, QT_ResearchHosts);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            var id = new Unity.Collections.FixedString64Bytes(techId);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (host != Entity.Null && ents[i].Index >= host.Index) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i], true);
                for (int b = 0; b < buf.Length; b++)
                {
                    if (buf[b].Kind != ProductionKind.Research) continue;
                    if (!buf[b].Id.Equals(id)) continue;
                    host = ents[i];
                    slot = b;
                    break;
                }
            }
            return host != Entity.Null;
        }

        /// <summary>
        /// What a technology costs this faction right now — catalog price
        /// through the Royal Index (Antiquity) discount.
        ///
        /// ONE formula, because two would drift: the charge in
        /// <see cref="ResearchCommandDirect"/> and the refund in
        /// CancelProductionCommandHelper must agree to the last unit,
        /// including the integer truncation. Zero for an unknown or free tech.
        /// </summary>
        public static Cost ResearchCost(Faction faction, string techId)
        {
            if (string.IsNullOrEmpty(techId)) return default;
            if (!TechCatalog.TryGetTechnology(techId, out var techDef)) return default;
            if (techDef == null || techDef.cost == null) return default;

            float mult = TheWaningBorder.Economy.SectResearchEffects
                .ResearchCostMultiplier(faction);
            return Cost.Of(
                supplies:  (int)(techDef.cost.Supplies  * mult),
                iron:      (int)(techDef.cost.Iron      * mult),
                veilstone: (int)(techDef.cost.Veilstone * mult),
                veilsteel: (int)(techDef.cost.Veilsteel * mult));
        }

        /// <summary>
        /// Training is a kind of production now (2026-09-07), so this is
        /// <see cref="IssueCancelProduction"/> under the name its callers
        /// (the MP harness) still use.
        /// </summary>
        public static void IssueCancelTrain(EntityManager em, Entity building, int slotIndex,
            CommandSource source = CommandSource.LocalPlayer)
            => IssueCancelProduction(em, building, slotIndex, source);

        /// <summary>
        /// Cancel one entry of a building's production queue — a unit, a
        /// technology or a level-up — and refund it. Cost is charged at
        /// enqueue time, so every slot has to be cancellable.
        /// </summary>
        public static void IssueCancelProduction(EntityManager em, Entity building, int slotIndex,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (building == Entity.Null || !em.Exists(building)) return;
            if (!em.HasBuffer<ProductionQueueItem>(building)) return;
            if (IsBlockedByNotControllable(em, building, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueCancelProductionForLockstep(em, building, slotIndex);
            }
            else
            {
                Types.CancelProductionCommandHelper.Execute(em, building, slotIndex);
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
            if (ShouldDropCommand(source)) return;
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
            if (ShouldDropCommand(source)) return;
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

        // ═══════════════════════════════════════════════════════════════
        // CHARGED WALL-UPGRADE / KEEP-WING ENTRY POINTS
        // ═══════════════════════════════════════════════════════════════
        // IssueWallUpgrade / IssueKeepWing and their *Direct executors live
        // in CommandRouter.Replication2026.cs and stamp the timer only —
        // their COST used to be paid at the UI click site, i.e. on the
        // issuing peer alone, which forked the banks and with them the
        // desync checksum (docs/Multiplayer_LAN_Readiness.md). These
        // wrappers keep the spend beside the mutation: the *ChargedDirect
        // methods validate + spend + stamp, and they are what the SP direct
        // path AND LockstepManager.ExecuteCommand both call, so every peer
        // debits the same bank at the same tick.

        /// <summary>
        /// Wall instance upgrade with the cost charged in the executor.
        /// Multiplayer queues through the existing WallUpgrade opcode; the
        /// spend then happens inside WallUpgradeChargedDirect when the
        /// command replays on every peer.
        /// </summary>
        public static void IssueWallUpgradeCharged(EntityManager em, Entity wall,
            int upgradeType, float duration, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (wall == Entity.Null || !em.Exists(wall)) return;

            if (ShouldQueueForLockstep(source))
            {
                IssueWallUpgrade(em, wall, upgradeType, duration, source);
                return;
            }
            WallUpgradeChargedDirect(em, wall, upgradeType, duration);
        }

        /// <summary>
        /// Validate + SPEND + stamp for a wall instance upgrade. Runs on
        /// every peer (LockstepManager dispatches through here). Mirrors
        /// WallUpgradeDirect's no-op guards BEFORE the spend so a duplicate
        /// or replayed command can never double-charge; a short bank rejects
        /// the whole command identically everywhere.
        /// </summary>
        public static bool WallUpgradeChargedDirect(EntityManager em, Entity wall,
            int upgradeType, float duration)
        {
            if (wall == Entity.Null || !em.Exists(wall)) return false;
            if (em.HasComponent<WallUpgradeState>(wall)) return false;   // already upgrading
            if (!em.HasComponent<FactionTag>(wall)) return false;

            // Type 1 (tower) costs a wall tower; type 3 (hub — the cell becomes
            // a hub and the segment splits there) costs a hub; 4 / 5 cost the
            // matching emplacement; gates convert through
            // ConvertSegmentToGate, which carries its own executor-side spend.
            // Unknown types stamp free rather than guessing a price.
            var cost = upgradeType switch
            {
                1 => TheWaningBorder.Data.BuildCosts.Get("Alanthor_WallTower"),
                3 => TheWaningBorder.Data.BuildCosts.Get(TheWaningBorder.Entities.AlanthorWall.HubIdFor(
                         TheWaningBorder.Entities.AlanthorWall.IsPalisade(em, wall))),
                4 => TheWaningBorder.Data.BuildCosts.Get("Alanthor_BallistaEmplacement"),
                5 => TheWaningBorder.Data.BuildCosts.Get("Alanthor_TrebuchetEmplacement"),
                _ => default,
            };
            // The wall lock: no piece changes while a wall level researches.
            if (WallsLockedForUpgrade(em, em.GetComponentData<FactionTag>(wall).Value))
                return false;
            if (upgradeType == 3 && !TheWaningBorder.Entities.AlanthorWall.CanConvertInstanceToHub(em, wall))
                return false;
            // Branching a wall raises a hub of ITS kind, so it needs that
            // kind to be buildable: Alanthor and Runai cannot grow a palisade
            // after age-up (docs/Design/Age_0.md § Palisade).
            if (upgradeType == 3 && !TheWaningBorder.Entities.WallTiers.CanBuild(em,
                    em.GetComponentData<FactionTag>(wall).Value,
                    TheWaningBorder.Entities.AlanthorWall.IsPalisade(em, wall)))
                return false;
            // The placement rule is re-checked HERE, not just in the UI: it is
            // what stops two peers from disagreeing about whether a fitting
            // was legal, and what stops a stale panel from studding a wall
            // (docs/Design/Age_1_Alanthor.md § What a module may become).
            if (upgradeType == 1 && !TheWaningBorder.Entities.AlanthorWall.CanConvertToTower(em, wall))
                return false;
            if ((upgradeType == 4 || upgradeType == 5)
                && !TheWaningBorder.Entities.AlanthorWall.CanConvertToEmplacement(em, wall, trebuchet: upgradeType == 5))
                return false;
            var faction = em.GetComponentData<FactionTag>(wall).Value;
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Upgrades))
                return false;

            WallUpgradeDirect(em, wall, upgradeType, duration);
            return true;
        }

        /// <summary>
        /// THE WALL LOCK, as the executors apply it: true while a wall level
        /// (Battlements / Shielded Ramparts) is queued or researching anywhere
        /// for <paramref name="faction"/>. Every wall-changing executor —
        /// tower / hub / emplacement conversion, gate conversion, extending
        /// from a standing hub, a drawn wall that attaches to a standing hub
        /// or cell — refuses on it BEFORE its spend, so a stale panel or a
        /// command already in flight cannot slip through, and every peer
        /// refuses identically. Cancelling the research lifts it.
        /// docs/Design/Age_1_Alanthor.md § The four wall levels
        /// </summary>
        public static bool WallsLockedForUpgrade(EntityManager em, Faction faction)
            => TheWaningBorder.Entities.WallTiers.LevelResearchActive(em, faction);

        /// <summary>
        /// Fiendstone Keep wing construction with the cost charged in the
        /// executor. Same shape as IssueWallUpgradeCharged.
        /// </summary>
        public static void IssueKeepWingCharged(EntityManager em, Entity keep,
            byte wing, float duration, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (keep == Entity.Null || !em.Exists(keep)) return;

            if (ShouldQueueForLockstep(source))
            {
                IssueKeepWing(em, keep, wing, duration, source);
                return;
            }
            KeepWingChargedDirect(em, keep, wing, duration);
        }

        /// <summary>
        /// Validate + SPEND + stamp for a Keep wing. Runs on every peer.
        /// Re-checks the wing rules (one build at a time, each wing type
        /// once, three wings max) BEFORE the spend so a replayed duplicate
        /// cannot double-charge.
        /// </summary>
        public static bool KeepWingChargedDirect(EntityManager em, Entity keep,
            byte wing, float duration)
        {
            if (keep == Entity.Null || !em.Exists(keep)) return false;
            if (!em.HasComponent<KeepWings>(keep)) return false;
            if (em.HasComponent<KeepWingConstruction>(keep)) return false;   // one at a time
            if (!em.HasComponent<FactionTag>(keep)) return false;

            var wingType = (KeepWingType)wing;
            var wings = em.GetComponentData<KeepWings>(keep);
            if (wings.Count >= TheWaningBorder.Core.Settings.KeepWingConfig.MaxWings
                || wings.Has(wingType))
                return false;

            var faction = em.GetComponentData<FactionTag>(keep).Value;
            var cost = TheWaningBorder.Core.Settings.KeepWingConfig.CostOf(wingType);
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Buildings))
                return false;

            KeepWingDirect(em, keep, wing, duration);
            return true;
        }

        /// <summary>
        /// Authoritative check: can this <paramref name="unitId"/> be queued
        /// at this <paramref name="building"/> right now? Reads
        /// <see cref="BuildingUpgradeState"/> and compares to the unit's
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
            if (em.HasComponent<HallTag>(e))            return "Fortress";
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

        // ─── Production-queue cap ────────────────────────────────────────
        // ONE buffer per building holds its units, research and level-ups, in
        // the order they were queued (ProductionQueueComponents). 16 is the
        // cap, the roster HUD area shows all 16 slots, and every kind of
        // order shares them.
        public const int MaxProductionQueue = 16;

        /// <summary>UNITS queued on this building. The AI's per-building
        /// train cap counts these; the hard cap counts everything.</summary>
        public static int GetTrainQueueLength(EntityManager em, Entity building)
        {
            if (!em.HasBuffer<ProductionQueueItem>(building)) return 0;
            var q = em.GetBuffer<ProductionQueueItem>(building);
            int n = 0;
            for (int i = 0; i < q.Length; i++)
                if (q[i].Kind == ProductionKind.Train) n++;
            return n;
        }

        /// <summary>Everything queued on this building, whatever its kind.</summary>
        public static int GetProductionQueueLength(EntityManager em, Entity building)
        {
            if (!em.HasBuffer<ProductionQueueItem>(building)) return 0;
            return em.GetBuffer<ProductionQueueItem>(building).Length;
        }

        /// <summary>
        /// True when this building's production queue is at the cap. UI / AI
        /// / command paths should consult this before adding another order.
        /// </summary>
        public static bool IsProductionQueueFull(EntityManager em, Entity building)
        {
            return GetProductionQueueLength(em, building) >= MaxProductionQueue;
        }

        /// <summary>
        /// The train EXECUTOR — reached by the single-player direct path AND
        /// by LockstepManager.ExecuteCommand on every peer. Public so the
        /// lockstep dispatch shares this exact validation + spend path.
        /// Validates, then SPENDS the unit cost here — never at the issue
        /// site. Issuer-side spends debited one peer's bank only, and the
        /// faction banks are folded into the desync checksum, so the first
        /// purchase of any multiplayer match desynced it
        /// (docs/Multiplayer_LAN_Readiness.md).
        /// </summary>
        public static void TrainCommandDirect(EntityManager em, Entity building, string unitId,
            TheWaningBorder.Abilities.HeroRevivalMode revival
                = TheWaningBorder.Abilities.HeroRevivalMode.None)
        {
            if (!em.HasBuffer<ProductionQueueItem>(building))
            {
                // Silent-drop instrumentation (2026-08-04, "sect unit button
                // doesn't work" hunt): a train order landing on a building
                // with no queue is a wiring bug — say so instead of eating
                // the click.
                TWBLog.Log($"[CommandRouter] TRAIN '{unitId}' DROPPED — target " +
                           $"building has no ProductionQueueItem buffer.");
                return;
            }

            // Notifications are presentation-only, so gate them on the local
            // player's faction — this method now also replays REMOTE
            // players' commands via lockstep, and their rejections are not
            // this screen's business.
            bool notifyLocal = em.HasComponent<FactionTag>(building)
                && em.GetComponentData<FactionTag>(building).Value == GameSettings.LocalPlayerFaction;

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
                if (notifyLocal)
                    SimSignals.Notify(Loc.T("King Lexor already serves your realm"));
                return;
            }
            // Same gate for the Ledger: one automaton per player.
            if (TheWaningBorder.Abilities.HeroTrainLimit.IsLedgerId(unitId) &&
                em.HasComponent<FactionTag>(building) &&
                TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedLedger(em, em.GetComponentData<FactionTag>(building).Value))
            {
                if (notifyLocal)
                    SimSignals.Notify(Loc.T("Your court already employs a Ledger"));
                return;
            }
            // SECT HEROES (docs/Design/Religion.md §4): one per sect, and the
            // first recruit costs Religion Points on top of its resources.
            int heroRp = 0;
            string heroSect = TheWaningBorder.Systems.Sect.SectHeroes.SectIdForUnit(unitId);
            if (heroSect != null && em.HasComponent<FactionTag>(building))
            {
                var heroFaction = em.GetComponentData<FactionTag>(building).Value;
                if (TheWaningBorder.Systems.Sect.SectHeroes.HasLiveOrQueued(em, heroFaction, unitId))
                {
                    if (notifyLocal)
                        SimSignals.Notify(Loc.T("This sect's hero already serves you"));
                    return;
                }
                heroRp = TheWaningBorder.Systems.Sect.SectHeroes.RpDue(em, heroFaction, heroSect);
                if (!TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, heroFaction, heroRp))
                {
                    if (notifyLocal)
                        SimSignals.NotifyError(Loc.T("Not enough Religion Points"));
                    return;
                }
            }
            // Reject when combined production queue would exceed the cap.
            if (IsProductionQueueFull(em, building))
            {
                if (notifyLocal)
                    SimSignals.Notify(Loc.T("Production queue full"));
                return;
            }

            // SPEND — after every reject gate so a refused order never
            // touches the bank, before the enqueue so a short bank rejects
            // the whole command with no partial effects. The cost formula
            // (base catalog cost through War's military discount, which
            // reads replicated sect state) is deterministic on every peer.
            // CancelProductionCommandHelper refunds through the same formula.
            // Call to Arms (War) cuts the price of everything this building
            // trains while it stands. Deterministic on every peer - the boon is
            // replicated sect state like the passive discount - and recorded on
            // the queue item so the cancel refund matches what was charged.
            float boonMult = TheWaningBorder.Economy.WarSectCostHelper
                .TrainingBoonCostMultiplier(em, building);
            if (em.HasComponent<FactionTag>(building))
            {
                var trainFaction = em.GetComponentData<FactionTag>(building).Value;

                // Full Honours costs more the more he was worth (Heroes.md
                // §4). Folded into the SAME multiplier the queue item records,
                // so CancelProductionCommandHelper refunds exactly what was
                // taken — a separately-applied surcharge would have refunded
                // the base price and quietly paid the player to cancel.
                boonMult *= TheWaningBorder.Abilities.HeroRevival
                    .PriceMultiplier(trainFaction, revival);
                var cost = TheWaningBorder.Economy.WarSectCostHelper.MilitaryDiscount(
                    em, trainFaction, unitId,
                    TheWaningBorder.Data.UnitCosts.Get(unitId));
                cost = TheWaningBorder.Economy.WarSectCostHelper.ApplyPaidMultiplier(cost, boonMult);
                if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, trainFaction, cost, TheWaningBorder.Economy.SpendCategory.Units))
                {
                    if (notifyLocal)
                        SimSignals.NotifyError(Loc.T("Not enough resources"));
                    return;
                }
                if (heroRp > 0)
                    TheWaningBorder.Economy.FactionReligionPointsHelper.TrySpend(em, trainFaction, heroRp);
            }

            // Behind whatever the building is already making — a unit
            // queued after a research waits for the research, which is the
            // one queue the player asked for.
            // Level carries the hero's return level for a revival (0 for
            // everything else). Read by TrainingSystem when the unit spawns.
            byte returnLevel = 0;
            if (revival != TheWaningBorder.Abilities.HeroRevivalMode.None
                && em.HasComponent<FactionTag>(building))
            {
                returnLevel = TheWaningBorder.Abilities.HeroRevival.ReturnLevel(
                    em.GetComponentData<FactionTag>(building).Value, revival);
            }

            em.GetBuffer<ProductionQueueItem>(building).Add(new ProductionQueueItem
            {
                Kind               = ProductionKind.Train,
                Id                 = new Unity.Collections.FixedString64Bytes(unitId),
                PaidCostMultiplier = boonMult,
                Level              = returnLevel,
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // PLACE BUILDING COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Count a faction's buildings of one tag type, completed and
        /// under construction alike — the sect buildings all need the same
        /// query with a different tag.</summary>
        private static int CountFactionBuildings<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = TaggedFaction<T>.Query.Get(em, TaggedFaction<T>.Types);
            using var facs = query.ToComponentDataArray<FactionTag>(
                Unity.Collections.Allocator.Temp);
            int count = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) count++;
            return count;
        }

        /// <summary>
        /// True when this faction may still place one more of this building.
        /// Callers with a spend-then-place flow should check this BEFORE
        /// spending — IssuePlaceBuilding also rejects, but by then the
        /// resources are already gone.
        /// </summary>
        public static bool CanPlaceBuilding(EntityManager em, string buildingId, Faction faction)
        {
            return !SectBuildingCapReached(em, buildingId, faction);
        }

        /// <summary>
        /// True when this faction is already at its cap for a SECT building.
        /// Every sect building is capped at SectBuilding.CapPerFaction (5) —
        /// docs/Design/Sects.md section 1. Non-sect ids answer false.
        /// </summary>
        private static bool SectBuildingCapReached(EntityManager em, string buildingId, Faction faction)
        {
            int cap = TheWaningBorder.Entities.SectBuilding.CapPerFaction
                      - TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, buildingId);
            switch (buildingId)
            {
                case "Sect_Reliquary":   return CountFactionBuildings<ReliquaryTag>(em, faction)   >= cap;
                case "Sect_MendingHall": return CountFactionBuildings<MendingHallTag>(em, faction) >= cap;
                case "Sect_Stonehold":   return CountFactionBuildings<StoneholdTag>(em, faction)   >= cap;
                case "Sect_MusterYard":  return CountFactionBuildings<MusterYardTag>(em, faction)  >= cap;
                case "Sect_Veilworks":   return CountFactionBuildings<VeilworksTag>(em, faction)   >= cap;
                default: return false;
            }
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
            => IssuePlaceBuilding(em, buildingId, position, faction, Entity.Null, out created, source);

        /// <summary>
        /// The router's own placement gates, in order, with the REASON the
        /// first failing one refused: wall-mount-only, the per-faction caps,
        /// the territory gate (including the Hall's adjacency rule), the
        /// extractor node gate, one Hall per territory, and — for a Hall — the
        /// named worker standing within range of the site.
        ///
        /// Public so the placement ghost asks THIS rather than a copy of it:
        /// the preview and the router cannot disagree about a click.
        /// <paramref name="position"/> comes back snapped onto the extractor's
        /// node when the building is one (the position that will be queued).
        /// Reads replicated state only.
        /// </summary>
        public static TheWaningBorder.World.Regions.PlacementRefusal CheckPlaceBuilding(
            EntityManager em, string buildingId, ref float3 position, Faction faction, Entity worker)
        {
            const TheWaningBorder.World.Regions.PlacementRefusal Ok =
                TheWaningBorder.World.Regions.PlacementRefusal.None;

            // Emplacements are WALL-MOUNT ONLY (2026-09-25): the free-standing
            // platforms are no longer placeable by anyone.
            if (IsWallMountOnlyBuilding(buildingId))
                return TheWaningBorder.World.Regions.PlacementRefusal.UnknownBuilding;

            // Sect buildings, 5 per faction. Rejected here so callers with a
            // spend-then-place flow (AI TryBuildOnce) see created == Null and
            // refund cleanly; the UI normally hides the button first.
            if (!CanPlaceBuilding(em, buildingId, faction))
                return TheWaningBorder.World.Regions.PlacementRefusal.CapReached;

            // TERRITORY GATE (docs/Design/Regions.md §2 + §6). You build in the
            // ground you hold; a claim structure is the one thing that may go
            // on Natural ground, because planting it is how ground is taken —
            // and only on ground ADJACENT to a territory you already hold.
            //
            // Enforced HERE and not only in the placement UI: the UI paints the
            // preview red, but the AI, the lockstep replay and any future
            // command source come through this function, and a rule only the
            // local player's mouse obeys is not a rule. Same spend-then-place
            // contract as the caps above — a rejected placement refunds.
            var territory = TheWaningBorder.World.Regions.TerritoryOwnership.TerritoryRefusal(
                em, faction, buildingId, position.x, position.z);
            if (territory != Ok) return territory;

            // EVERY EXTRACTOR STANDS ON ITS OWN NODE, one per node
            // (docs/Design/Regions.md §4): Gatherer's Hut on a supply site,
            // Mine on iron, Veilstone Mine on a veilstone outcropping. The node
            // count is what limits how many a territory supports, so there is
            // no separate cap.
            //
            // SNAP FIRST, then gate. An extractor sits ON its node, not near
            // it, so the click only has to name the node — the building lands
            // over its cells. This is the right place for it and not the
            // factory: the factory's ECB overload has no EntityManager to find
            // a node with, and everything below queues the SNAPPED position
            // into the lockstep command, so every peer replays the same cell
            // instead of re-deriving it from its own node query.
            if (TheWaningBorder.World.Regions.TerritoryOwnership.TrySnapToNode(
                    em, buildingId, position, out var onNode))
                position = onNode;

            if (!TheWaningBorder.World.Regions.TerritoryOwnership.OnFreeNodeFor(
                    em, buildingId, position.x, position.z))
                return TheWaningBorder.World.Regions.PlacementRefusal.OffNode;

            // VEILSTONE (docs/Design/Veilstone_Economy.md §3). Alanthor do not
            // mine it (the iron Mine is still theirs); a Veilstone Mine needs an INACTIVE outcrop (a cursed one
            // is the curse's, a depleted one is spent); a Trading Outpost needs
            // an uncursed outcrop beside it that no other Outpost serves. Here,
            // not only in the ghost, so the AI and the lockstep replay obey it.
            if (!TheWaningBorder.World.Regions.TerritoryOwnership.MayBuildMine(em, faction, buildingId))
                return TheWaningBorder.World.Regions.PlacementRefusal.WrongCulture;
            if (buildingId == "VeilstoneMine")
            {
                if (TheWaningBorder.Systems.Economy.VeilstoneNodeStateSystem.TryGetOutcropAt(
                        em, position.x, position.z, 4f, out _, out var kind)
                    && kind != VeilstoneNodeKind.Inactive)
                    return TheWaningBorder.World.Regions.PlacementRefusal.OutcropUnavailable;
            }
            // The Trading Outpost stands on a SIDE SLOT of an uncursed outcrop
            // — Inactive or Depleted (a spent outcrop trades as well as a
            // fresh one), up to four posts per outcrop (2026-10-04).
            if (buildingId == TheWaningBorder.Entities.TradingOutpost.BuildingId)
            {
                if (!TheWaningBorder.Entities.TradingOutpost.TryGetOutcropOf(
                        em, position.x, position.z, out var outcrop, out _, out byte side)
                    || side == TheWaningBorder.Entities.TradingOutpost.OnOutcrop)
                    return TheWaningBorder.World.Regions.PlacementRefusal.NoOutcropNearby;
                if (TheWaningBorder.Systems.Economy.VeilstoneNodeStateSystem.KindOf(em, outcrop)
                    == VeilstoneNodeKind.Cursed)
                    return TheWaningBorder.World.Regions.PlacementRefusal.OutcropUnavailable;
            }

            // The faction's own plans reserve their tiles
            // (docs/Design/Planned_Buildings.md).
            if (TheWaningBorder.Entities.PlannedBuildings.UsesPlan(buildingId)
                && TheWaningBorder.Entities.PlannedBuildings.OverlapsOwnPlan(
                       em, faction, BuildGrid.Snap(position, buildingId),
                       BuildingSizeConfig.GetSize(buildingId)))
                return TheWaningBorder.World.Regions.PlacementRefusal.Overlap;

            // One Fortress per territory (Territory_Claims.md §4). A second
            // locks nothing the first does not already lock.
            if (buildingId == "Fortress"
                && TheWaningBorder.World.Regions.TerritoryOwnership.FortressCapReached(
                       em, position.x, position.z))
                return TheWaningBorder.World.Regions.PlacementRefusal.FortressAlreadyHere;

            // THE WORKER HAS TO BE THERE (Regions.md §2, 2026-09-26). A claim
            // is made on the ground, by a worker standing on it — not dropped
            // across the map from the home base. Within range of the site AND
            // inside the territory it claims (2026-09-27) — not reaching over
            // the border from the faction's own ground.
            if (TheWaningBorder.World.Regions.TerritoryOwnership.NeedsWorkerNearby(buildingId))
            {
                var near = TheWaningBorder.World.Regions.TerritoryOwnership.CheckHallWorker(
                    em, faction, worker, position.x, position.z);
                if (near != Ok) return near;
            }
            return Ok;
        }

        /// <summary>
        /// Place-building with the WORKER that makes the placement. Only a
        /// Hall reads it (it must stand within
        /// <see cref="TheWaningBorder.World.Regions.TerritoryOwnership.HallWorkerRange"/>
        /// of the site); every other building ignores it. In lockstep the
        /// worker's NetworkId rides the command's TargetEntityId, and the
        /// executor re-checks it at the execution tick.
        /// </summary>
        public static bool IssuePlaceBuilding(EntityManager em, string buildingId, float3 position,
            Faction faction, Entity worker, out Entity created,
            CommandSource source = CommandSource.LocalPlayer, float yawDegrees = 0f)
        {
            created = Entity.Null;
            if (ShouldDropCommand(source)) return false;

            if (CheckPlaceBuilding(em, buildingId, ref position, faction, worker)
                != TheWaningBorder.World.Regions.PlacementRefusal.None)
                return false;

            // The AI turns what it builds, so its bases are not rows of
            // identical buildings all facing south.
            if (source == CommandSource.AI && yawDegrees == 0f)
                yawDegrees = AiPlacementYaw(buildingId, position);

            if (source == CommandSource.LocalPlayer)
                TheWaningBorder.AI.AILogger.LogPlayer(faction, "BUILD",
                    $"{buildingId} at ({position.x:0},{position.z:0})");
            else if (source == CommandSource.AI)
                TheWaningBorder.AI.AILogger.Log(faction, "CMD",
                    $"place {buildingId} at ({position.x:0},{position.z:0})");

            if (ShouldQueueForLockstep(source))
            {
                // TargetEntityId carries the WORKER's NetworkId (0 = none).
                // It was an unused field on this command, so older commands
                // decode as "no worker" — accepted for every building except
                // a Hall, which the executor refuses without one.
                int workerId = worker != Entity.Null && em.Exists(worker)
                                && em.HasComponent<NetworkedEntity>(worker)
                    ? em.GetComponentData<NetworkedEntity>(worker).NetworkId
                    : 0;
                var cmd = new LockstepCommand
                {
                    Type = LockstepCommandType.PlaceBuilding,
                    BuildingId = buildingId,
                    TargetPosition = position,
                    EntityNetworkId = (int)faction, // Carry faction in EntityNetworkId
                    TargetEntityId = workerId,
                    // The placement ghost's rotation, in tenths of a degree
                    // (0 = unrotated, which is also how older commands decode).
                    SecondaryTargetId = YawToWire(yawDegrees),
                };
                LockstepServiceLocator.Instance.QueueCommand(cmd);
                return true; // Queued — caller must NOT create entity locally
            }
            else
            {
                // Single player — create immediately
                created = PlaceBuildingDirect(em, buildingId, position, faction, worker,
                                              YawFromWire(YawToWire(yawDegrees)));
                return false; // Created locally — caller can proceed
            }
        }

        /// <summary>
        /// <see cref="PlaceBuildingDirect(EntityManager, string, float3, Faction, Entity)"/>
        /// with no worker. Every building but a Hall places exactly as before;
        /// a Hall is refused (it needs its worker on site).
        /// </summary>
        public static Entity PlaceBuildingDirect(EntityManager em, string buildingId, float3 position, Faction faction)
            => PlaceBuildingDirect(em, buildingId, position, faction, Entity.Null);

        /// <summary>
        /// The HALL's execution-tick re-check (Regions.md §2). A queued claim
        /// executes ticks after it was issued, and on a remote peer the issue
        /// gates never ran, so the rules that depend on a changing world are
        /// asked again here, against replicated state: the territory gate with
        /// its adjacency rule, one Hall per territory, and the named worker
        /// alive, owned, a worker, within range, and inside the site's
        /// territory. Ownership is re-derived
        /// first so every peer answers from the same live Halls rather than
        /// from whenever its own income tick last ran.
        /// </summary>
        private static TheWaningBorder.World.Regions.PlacementRefusal CheckClaimAtExecution(
            EntityManager em, string buildingId, float3 position, Faction faction, Entity worker)
        {
            // Territory_Claims.md §5: EVERY placement is re-checked against
            // ownership at the tick it executes. Ground changes hands on the
            // meter between issue and execution, and on a remote peer the
            // issue gates never ran — a building must never land on ground
            // its faction lost in the meantime.
            if (TheWaningBorder.World.Regions.RegionMap.Ready)
                TheWaningBorder.World.Regions.TerritoryOwnership.Recompute(em);
            var r = TheWaningBorder.World.Regions.TerritoryOwnership.TerritoryRefusal(
                em, faction, buildingId, position.x, position.z);
            if (r != TheWaningBorder.World.Regions.PlacementRefusal.None) return r;
            if (buildingId == "Fortress"
                && TheWaningBorder.World.Regions.TerritoryOwnership.FortressCapReached(em, position.x, position.z))
                return TheWaningBorder.World.Regions.PlacementRefusal.FortressAlreadyHere;
            // In range AND standing inside the territory being claimed.
            if (TheWaningBorder.World.Regions.TerritoryOwnership.NeedsWorkerNearby(buildingId))
                return TheWaningBorder.World.Regions.TerritoryOwnership.CheckHallWorker(
                    em, faction, worker, position.x, position.z);
            return TheWaningBorder.World.Regions.PlacementRefusal.None;
        }

        /// <summary>Set by <see cref="PlaceBuildingDirect(EntityManager, string, float3, Faction, Entity)"/>
        /// when it refuses a CLAIM at execution: the rule it broke, so the
        /// issuing client can tell its player. None after every other call.</summary>
        public static TheWaningBorder.World.Regions.PlacementRefusal LastPlacementRefusal { get; private set; }

        /// <summary>
        /// Execute building placement: create entity, mark under construction, set HP to 1.
        /// Called by lockstep ExecuteCommand on all clients, or directly in singleplayer.
        /// Validates affordability and SPENDS here — never at the issue site
        /// (UI panel / AI). Issuer-side spends debited one peer's bank only
        /// while the banks feed the desync checksum
        /// (docs/Multiplayer_LAN_Readiness.md). Returns Entity.Null when the
        /// bank cannot pay, identically on every peer — no entity, no
        /// partial effects.
        /// </summary>
        /// <summary>
        /// True when <paramref name="faction"/> may not place this landmark:
        /// it already has one (built or under construction), it already has a
        /// culture, or the landmark's culture is not in this build (the demo
        /// ships Alanthor only). Replicated state only — same verdict on
        /// every lockstep peer.
        /// </summary>
        public static bool LandmarkRefused(EntityManager em, string buildingId, Faction faction)
        {
            if (TheWaningBorder.Entities.BuildingFactory.GetFactionChoiceBuilding(em, faction) != null) return true;
            if (CultureConfig.GetCompletedCulture(em, faction) != Cultures.None) return true;
            byte culture = TheWaningBorder.Systems.Work.LandmarkAgeUp.CultureOf(buildingId);
            return culture == Cultures.None || CultureConfig.IsComingSoon(culture);
        }

        /// <summary>The placement yaw on the wire: tenths of a degree,
        /// normalised to [0, 3600). Both paths round-trip through it, so single
        /// player and every lockstep peer build the identical rotation.</summary>
        public static int YawToWire(float yawDegrees)
        {
            int t = (int)math.round(yawDegrees * 10f) % 3600;
            return t < 0 ? t + 3600 : t;
        }

        public static float YawFromWire(int tenths) => tenths / 10f;

        /// <summary>
        /// The rotation the AI gives a building it places, for visual variety.
        /// Right angles only, so the footprint the placement checks validated
        /// is still the one the building covers: a square footprint may face
        /// any of the four ways, a rectangular one only its two lengthwise
        /// ways. Picked from the site and the id, not a random stream — the
        /// same site always gets the same facing, and nothing here touches
        /// state another peer would have to reproduce (the yaw rides the
        /// placement order).
        /// </summary>
        private static float AiPlacementYaw(string buildingId, float3 position)
        {
            var size = BuildingSizeConfig.GetSize(buildingId);
            int idHash = 17;
            if (buildingId != null)
                for (int i = 0; i < buildingId.Length; i++) idHash = idHash * 31 + buildingId[i];
            uint h = math.hash(new int3((int)math.round(position.x), (int)math.round(position.z), idHash));
            return size.x == size.y ? (h % 4u) * 90f : (h % 2u) * 180f;
        }

        public static Entity PlaceBuildingDirect(EntityManager em, string buildingId, float3 position,
            Faction faction, Entity worker, float yawDegrees = 0f)
        {
            LastPlacementRefusal = TheWaningBorder.World.Regions.PlacementRefusal.None;

            // Wall-mount-only ids never place, on any peer — refused before
            // the spend, like the collision check below.
            if (IsWallMountOnlyBuilding(buildingId)) return Entity.Null;

            // A CLAIM is re-checked against the world as it is NOW, before the
            // spend, identically on every peer (see CheckClaimAtExecution).
            var claim = CheckClaimAtExecution(em, buildingId, position, faction, worker);
            if (claim != TheWaningBorder.World.Regions.PlacementRefusal.None)
            {
                LastPlacementRefusal = claim;
                UnityEngine.Debug.LogWarning(
                    $"[CommandRouter] Refused {buildingId} for {faction} at " +
                    $"({position.x:F1},{position.z:F1}) — {claim}.");
                return Entity.Null;
            }

            // ONE LANDMARK PER FACTION (Age_0.md § Age-up by landmark). The
            // panel and the AI check this before issuing, but only the
            // executor sees two queued placements land in order — the second
            // must be refused here, on every peer, before the spend. A faction
            // that already has a culture never builds another, and a landmark
            // whose culture the build does not ship is refused outright.
            if (TheWaningBorder.Entities.BuildingFactory.IsChoiceBuilding(buildingId)
                && LandmarkRefused(em, buildingId, faction))
            {
                LastPlacementRefusal = TheWaningBorder.World.Regions.PlacementRefusal.CapReached;
                UnityEngine.Debug.LogWarning(
                    $"[CommandRouter] Refused {buildingId} for {faction} — landmark already placed, " +
                    "culture already chosen, or culture unavailable.");
                return Entity.Null;
            }

            // COLLISION, LAST LINE. The issue site validated a CANDIDATE
            // position; this is the only place that knows the position the
            // building will actually occupy, because BuildingFactory.Create
            // snaps to the build grid on the way in. Between those two points
            // the position can move by up to a cell, the world can change
            // (a queued lockstep command executes ticks after it was issued),
            // and on a remote peer the validation never ran at all — which is
            // how towers ended up standing inside the Hall (2026-08-18).
            // Geometry only, and refused BEFORE the spend so a rejected
            // placement costs nothing.
            float3 snappedPos = BuildGrid.Snap(position, buildingId);
            if (BuildCommandHelper.OverlapsExistingBuilding(
                    em, snappedPos, BuildingSizeConfig.GetSize(buildingId)))
            {
                UnityEngine.Debug.LogWarning(
                    $"[CommandRouter] Refused {buildingId} for {faction} at " +
                    $"({snappedPos.x:F1},{snappedPos.z:F1}) — footprint overlaps an existing building.");
                return Entity.Null;
            }

            // ONE PLAN PER TILE, PER PLAYER (docs/Design/Planned_Buildings.md):
            // a faction's own plans reserve their cells against its own later
            // orders. Another faction's plan never blocks — two opposing plans
            // may share a spot; the first to break ground keeps it.
            if (TheWaningBorder.Entities.PlannedBuildings.OverlapsOwnPlan(
                    em, faction, snappedPos, BuildingSizeConfig.GetSize(buildingId)))
            {
                LastPlacementRefusal = TheWaningBorder.World.Regions.PlacementRefusal.Overlap;
                return Entity.Null;
            }

            // Nothing on a resource node but its own extractor (Build_Grid.md
            // §3) — the same geometry-only kind of invariant as the overlap
            // above, from replicated node positions, so every peer agrees.
            BuildCommandHelper.FootprintAabb(snappedPos, BuildingSizeConfig.GetSize(buildingId),
                out float2 nodeMin, out float2 nodeMax);
            if (TheWaningBorder.Entities.ResourceNodeSite.OverlapsNode(em, nodeMin, nodeMax,
                    TheWaningBorder.World.Regions.TerritoryOwnership.NodeStoodOnBy(buildingId)))
            {
                LastPlacementRefusal = TheWaningBorder.World.Regions.PlacementRefusal.OnResourceNode;
                UnityEngine.Debug.LogWarning(
                    $"[CommandRouter] Refused {buildingId} for {faction} at " +
                    $"({snappedPos.x:F1},{snappedPos.z:F1}) — footprint covers a resource node.");
                return Entity.Null;
            }

            // BuildCosts is synced from the shared TechTree on every peer, so
            // the debit is deterministic. BuildCosts.For folds in the
            // faction-dependent parts — the Hall's escalation (Regions.md §2,
            // counted from the live Halls at THIS tick) and Deep Foundations —
            // from replicated state only. An id missing from the table places
            // free — the same lenient fallback the old panel spend had.
            // THE TEMPLE COSTS A RELIGION POINT (docs/Design/Religion.md §2),
            // and a faction raises one. Checked before either spend so a
            // refused Temple costs nothing; the RP is taken only once the
            // resources went through, so no peer ever pays one without the
            // other.
            bool temple = buildingId == "TempleOfRidan";
            int templeRp = 0;
            if (temple)
            {
                templeRp = TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp;
                if (TheWaningBorder.Entities.BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(em, faction) > 0
                    || TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, buildingId) > 0
                    || !TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction, templeRp))
                {
                    LastPlacementRefusal = TheWaningBorder.World.Regions.PlacementRefusal.CapReached;
                    return Entity.Null;
                }
            }

            // PER-FACTION CAPS (BuildingDef.maxPerFaction — Houses 20,
            // docs/Design/Age_0.md § Hut). Plans count, so queued orders
            // cannot sneak past it.
            if (TheWaningBorder.Entities.BuildingFactory.AtFactionCap(em, faction, buildingId))
            {
                LastPlacementRefusal = TheWaningBorder.World.Regions.PlacementRefusal.CapReached;
                return Entity.Null;
            }

            // Position-aware: a Trading Outpost's price climbs with every post
            // already beside its outcrop (BuildCosts.For, 2026-10-04).
            var cost = TheWaningBorder.Data.BuildCosts.For(em, faction, buildingId, snappedPos);
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Buildings))
                return Entity.Null;
            if (temple)
                TheWaningBorder.Economy.FactionReligionPointsHelper.TrySpend(em, faction, templeRp);

            // A worker-raised building starts as a PLAN: paid, owner-only, no
            // footprint in the world. PlannedBuildingSystem breaks ground when
            // a worker arrives (docs/Design/Planned_Buildings.md).
            if (TheWaningBorder.Entities.PlannedBuildings.UsesPlan(buildingId))
                return TheWaningBorder.Entities.PlannedBuildings.Create(
                    em, buildingId, position, faction, yawDegrees, cost, temple ? templeRp : 0);

            return CreateConstructionSite(em, buildingId, position, faction, yawDegrees, cost);
        }

        /// <summary>
        /// The world re-check a plan gets when a worker breaks ground: the
        /// ground is still held, no real building and no resource node now
        /// covers the footprint. Replicated state only.
        /// </summary>
        public static TheWaningBorder.World.Regions.PlacementRefusal CheckBreakGround(
            EntityManager em, string buildingId, float3 position, Faction faction, Entity worker)
        {
            var claim = CheckClaimAtExecution(em, buildingId, position, faction, worker);
            if (claim != TheWaningBorder.World.Regions.PlacementRefusal.None) return claim;
            var size = BuildingSizeConfig.GetSize(buildingId);
            if (BuildCommandHelper.OverlapsExistingBuilding(em, position, size))
                return TheWaningBorder.World.Regions.PlacementRefusal.Overlap;
            BuildCommandHelper.FootprintAabb(position, size, out float2 nodeMin, out float2 nodeMax);
            if (TheWaningBorder.Entities.ResourceNodeSite.OverlapsNode(em, nodeMin, nodeMax,
                    TheWaningBorder.World.Regions.TerritoryOwnership.NodeStoodOnBy(buildingId)))
                return TheWaningBorder.World.Regions.PlacementRefusal.OnResourceNode;
            return TheWaningBorder.World.Regions.PlacementRefusal.None;
        }

        /// <summary>
        /// Raise the real under-construction site, already PAID
        /// (<paramref name="cost"/> is recorded for refunds). Shared by a direct
        /// placement and by a plan breaking ground.
        /// </summary>
        public static Entity CreateConstructionSite(EntityManager em, string buildingId, float3 position,
            Faction faction, float yawDegrees, Cost cost)
        {
            Entity building = TheWaningBorder.Entities.BuildingFactory.Create(em, buildingId, position, faction);

            // THE PLACEMENT ROTATION (2026-09-30). Applied here, by the
            // executor, on every peer — it used to be written by the click
            // handler after the fact, on the single-player path only, so a
            // lockstep placement always stood unrotated whatever the ghost
            // showed. Factories create every building facing identity.
            if (yawDegrees != 0f && building != Entity.Null && em.Exists(building)
                && em.HasComponent<Unity.Transforms.LocalTransform>(building))
            {
                var lt = em.GetComponentData<Unity.Transforms.LocalTransform>(building);
                lt.Rotation = quaternion.RotateY(math.radians(yawDegrees));
                em.SetComponentData(building, lt);
            }

            // Remember what was charged, so a refund pays back THIS price and
            // not whatever the faction's next Hall would cost by then.
            if (building != Entity.Null && em.Exists(building))
            {
                if (em.HasComponent<PaidBuildCost>(building))
                    em.SetComponentData(building, new PaidBuildCost { Value = cost });
                else
                    em.AddComponentData(building, new PaidBuildCost { Value = cost });
            }

            // Mark as under construction
            float buildTime = GetBuildTime(buildingId);
            if (!em.HasComponent<UnderConstruction>(building))
                em.AddComponentData(building, new UnderConstruction { Progress = 0f, Total = buildTime });
                else
                    em.SetComponentData(building, new UnderConstruction { Progress = 0f, Total = buildTime });

            // Set HP to 1 during construction; the site completes to the def's
            // own Max. No sect research scales building HP any more - Renewal
            // sells Field Hospital where Mason's Charter used to sit.
            if (em.HasComponent<Health>(building))
            {
                var hp = em.GetComponentData<Health>(building);
                em.SetComponentData(building, new Health { Value = 1, Max = hp.Max });
            }

            // Landmarks (Vault / Keep) self-construct with no
            // worker over 90 s (design: Age_0.md § Special buildings).
            // Workers can still be sent to accelerate — each contributes
            // +25 % build rate in BuildingConstructionSystem, so 4 workers
            // halve the time. Deterministic across lockstep peers (this
            // method runs on every client).
            if (TheWaningBorder.Entities.BuildingFactory.IsChoiceBuilding(buildingId)
                && !em.HasComponent<AutoConstructTag>(building))
            {
                em.AddComponent<AutoConstructTag>(building);
            }

            // A worker-placed Fortress (one per territory, Territory_Claims.md
            // §4) inherits the faction's current culture so culture-driven
            // queries that pick "the first capital" stay consistent —
            // EntityActionExtractor and CultureChoicePopup both read
            // FactionProgress off whichever capital they hit first.
            // Fortress.Create stamps Culture=None unconditionally, so we
            // override here. FactionColors.GetFactionCulture is deterministic
            // across lockstep peers (set by AgeUpSystem during tick replay),
            // so this works for both single-player and multiplayer paths.
            // After age-up it is also named the Fortress, as the starting
            // capital was (the Shelter is the Age 0 name).
            if (buildingId == "Fortress" && em.HasComponent<FactionProgress>(building))
            {
                byte culture = FactionColors.GetFactionCulture(faction);
                em.SetComponentData(building, new FactionProgress { Culture = culture });
                if (culture != Cultures.None)
                    TheWaningBorder.Systems.Work.AgeUpSystem.TransformCapitalForCulture(em, building, culture);
            }

            return building;
        }

        /// <summary>
        /// Seconds to raise <paramref name="buildingId"/>: its SO's buildTime,
        /// and nothing else (2026-10-03, unification item 34). The id switch
        /// that used to stand behind a zero buildTime is gone — every catalog
        /// building now authors its time, and the TechCatalog audit names any
        /// asset that does not. The one remaining constant is for ids with NO
        /// BuildingDefSO at all (the code-seeded chapels).
        /// </summary>
        private static float GetBuildTime(string buildingId)
        {
            if (TechCatalog.HasBuildingSO(buildingId))
                return TechCatalog.Building(buildingId).buildTime;
            return CodeSeededBuildTime;
        }

        /// <summary>Build time of an id with no BuildingDefSO (the chapels) —
        /// the old switch's default, kept until those ids get assets.</summary>
        private const float CodeSeededBuildTime = 30f;

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

        /// <summary>
        /// True when this peer must DROP the command entirely. AI brains run
        /// their think loops on every peer, but only the HOST may inject AI
        /// commands into the lockstep stream — ShouldQueueForLockstep answers
        /// false for AI on a client, and the old fallthrough then executed
        /// the command directly on that peer alone, forking the simulation
        /// (docs/Multiplayer_LAN_Readiness.md). Every Issue* method checks
        /// this BEFORE the queue/direct branch. Single-player and host
        /// behaviour are unchanged (this only answers true on a non-host
        /// multiplayer peer, for AI-sourced commands).
        /// </summary>
        public static bool ShouldDropCommand(CommandSource source)
        {
            if (source != CommandSource.AI) return false;
            if (!GameSettings.IsMultiplayer) return false;

            var lockstep = LockstepServiceLocator.Instance;
            if (lockstep == null || !lockstep.IsSimulationRunning) return false;

            return !lockstep.IsHost;
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
            TransientState.Clear<Types.MoveCommand>(em, unit);
            TransientState.Clear<Types.AttackCommand>(em, unit);
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
            TransientState.Clear<UserMoveOrder>(em, unit);
            TransientState.Clear<AttackMoveTag>(em, unit);
            TransientState.Clear<Types.AttackMoveCommand>(em, unit);
            if (em.HasComponent<PatrolTag>(unit))
                em.RemoveComponent<PatrolTag>(unit);
            if (em.HasComponent<PatrolAgent>(unit))
                em.RemoveComponent<PatrolAgent>(unit);
            if (em.HasComponent<Types.PatrolCommand>(unit))
                em.RemoveComponent<Types.PatrolCommand>(unit);
            if (em.HasBuffer<PatrolWaypoint>(unit))
                em.GetBuffer<PatrolWaypoint>(unit).Clear();
            // HoldPositionTag is NOT cleared here any more: it marks the Hold
            // STANCE, and a stance is a mode that survives Stop and every other
            // order (docs/Design/Stances.md §1). Only choosing another stance
            // (StanceCommandHelper) removes it — which also keeps an emplaced
            // engine, bolted to its platform, holding for ever.
            TransientState.Clear<AbilityActivated>(em, unit);
            TransientState.Clear<CommandQueueActive>(em, unit);
            if (em.HasBuffer<QueuedCommand>(unit))
                em.GetBuffer<QueuedCommand>(unit).Clear();
            TransientState.Clear<QueuedMoveStep>(em, unit);
            if (em.HasComponent<QueuedAttackTarget>(unit))
                em.RemoveComponent<QueuedAttackTarget>(unit);
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
            TransientState.Clear<FormationMemberState>(em, unit);
            TransientState.Clear<FormationSpeedOverride>(em, unit);
            // Out of the formation for good, so forget the slot too —
            // otherwise a later formation order would put this unit back
            // into a rank it has long since left.
            TransientState.Clear<FormationSlotMemory>(em, unit);
        }
    }
}