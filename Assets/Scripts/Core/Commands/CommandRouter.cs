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
        public static void IssueGather(EntityManager em, Entity miner, Entity resource, Entity deposit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (miner == Entity.Null || !em.Exists(miner)) return;
            if (IsBlockedByNotControllable(em, miner, source)) return;

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
        /// gather command on miners when this points at an iron / crystal
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
            if (IsBlockedByNotControllable(em, leader, source)) return;
            if (!em.HasComponent<BattalionStanceData>(leader)) return;

            em.SetComponentData(leader, new BattalionStanceData { Value = stance });
        }

        // ═══════════════════════════════════════════════════════════════
        // EQUIPMENT TIER UPGRADE COMMANDS (faction-wide tier research)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Upgrade a faction's equipment tier for a unit class. Adjacent
        /// tier moves only (Base→Iron→Crystal→Veilsteel→Glow). Costs are
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
                UnityEngine.Debug.LogWarning(
                    "[CommandRouter.IssueEquipmentUpgrade] not yet replicated through lockstep");
                return false;
            }

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
                UnityEngine.Debug.LogWarning(
                    "[CommandRouter.IssueGodPower] not yet replicated through lockstep");
                return false;
            }

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
        // PURIFY COMMANDS (Alanthor — scholar channels purification ritual on a crystal node)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a Purify ritual command on a scholar targeting a crystal main node.
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
            if (!em.HasComponent<CrystalMainNodeTag>(node)) return;

            // Multiplayer lockstep wiring for Purify is a follow-up slice —
            // the LockstepCommand schema needs a payload variant. For now,
            // singleplayer + AI execute directly; multiplayer drops on the floor
            // with a log so the omission is visible during testing.
            if (ShouldQueueForLockstep(source))
            {
                UnityEngine.Debug.LogWarning(
                    "[CommandRouter.IssuePurify] not yet replicated through lockstep — command ignored in multiplayer");
                return;
            }

            // Clear conflicting commands and attach the order. The ritual
            // system handles the rest (approach, channel, complete).
            CommandHelper.ClearAllCommands(em, scholar);
            if (em.HasComponent<PurifyCommand>(scholar))
                em.SetComponentData(scholar, new PurifyCommand { TargetNode = node });
            else
                em.AddComponentData(scholar, new PurifyCommand { TargetNode = node });
        }

        /// <summary>
        /// Issue a Convert ritual command on an acolyte targeting a crystal main
        /// node. ConversionRitualSystem channels for ConversionChannelTime (45s)
        /// against the node's heightened curse defense, then transitions the node
        /// to Converted and flips nearby defenders to the acolyte's faction.
        /// </summary>
        public static void IssueConvertNode(EntityManager em, Entity acolyte, Entity node,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (acolyte == Entity.Null || !em.Exists(acolyte)) return;
            if (node == Entity.Null || !em.Exists(node)) return;
            if (IsBlockedByNotControllable(em, acolyte, source)) return;
            if (!em.HasComponent<AcolyteTag>(acolyte)) return;
            if (!em.HasComponent<CrystalMainNodeTag>(node)) return;

            if (ShouldQueueForLockstep(source))
            {
                UnityEngine.Debug.LogWarning(
                    "[CommandRouter.IssueConvertNode] not yet replicated through lockstep — command ignored in multiplayer");
                return;
            }

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

            if (ShouldQueueForLockstep(source))
            {
                QueueTrainForLockstep(em, building, unitId);
            }
            else
            {
                TrainCommandDirect(em, building, unitId);
            }
        }

        private static void TrainCommandDirect(EntityManager em, Entity building, string unitId)
        {
            if (!em.HasBuffer<TrainQueueItem>(building)) return;
            var queue = em.GetBuffer<TrainQueueItem>(building);
            queue.Add(new TrainQueueItem { UnitId = new Unity.Collections.FixedString64Bytes(unitId) });
        }

        // ═══════════════════════════════════════════════════════════════
        // PLACE BUILDING COMMANDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Issue a place-building command. Creates the building on all clients via lockstep.
        /// Returns true if the command was queued (multiplayer) or executed (singleplayer).
        /// In multiplayer, the caller must NOT create the building locally — lockstep will do it.
        /// </summary>
        public static bool IssuePlaceBuilding(EntityManager em, string buildingId, float3 position,
            Faction faction, CommandSource source = CommandSource.LocalPlayer)
        {

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
                PlaceBuildingDirect(em, buildingId, position, faction);
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

            return building;
        }

        private static float GetBuildTime(string buildingId)
        {
            return buildingId switch
            {
                "Hut" => 15f,
                "GatherersHut" => 20f,
                "Barracks" => 30f,
                "TempleOfRidan" or "VaultOfAlmierra" or "FiendstoneKeep" => 40f,
                "Alanthor_Smelter" or "Alanthor_PracticeRange" => 30f,
                "Alanthor_Tower" or "Feraldis_HuntingLodge" or "Feraldis_LoggingStation"
                    or "Feraldis_Tower" or "Runai_Outpost" => 25f,
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
        }
    }
}