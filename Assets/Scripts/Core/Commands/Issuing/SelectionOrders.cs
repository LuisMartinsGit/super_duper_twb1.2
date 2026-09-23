// SelectionOrders.cs
// The order-issuing layer: what a click MEANS for the current selection.
//
// Lifted out of RTSInputManager (Presentation) because none of it is input.
// It reads no mouse and no screen: the input layer decides THAT an order was
// given and what it was aimed at, then hands the target here. What a unit is
// able to do, who owns it, and which command that becomes is simulation
// knowledge, and it routes through CommandRouter, which lives here too.
//
// Runtime must not know about SelectionSystem, so the selection arrives as a
// delegate supplied by the input layer.

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Core.Commands.Issuing
{
    /// <summary>
    /// Turns "the player aimed this at that" into commands on the selection.
    /// One instance is owned by the input layer.
    /// </summary>
    public sealed class SelectionOrders
    {
        // Cached: this runs on every Shift release, and CreateEntityQuery
        // matches every archetype in the world. See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] FrozenTypes = { typeof(CommandQueueFrozen) };
        TheWaningBorder.Core.CachedEntityQuery _frozenQuery;

        private readonly EntityManager _em;
        private readonly Func<List<Entity>> _selection;

        /// <summary>Metres between slots in a formation move.</summary>
        public float FormationSpacing;

        /// <summary>Shape a formation move arranges itself into.</summary>
        public FormationShape Shape;

        public SelectionOrders(EntityManager em, Func<List<Entity>> selection)
        {
            _em = em;
            _selection = selection;
        }

        private List<Entity> CurrentSelection => _selection();

        /// <summary>
        /// Send every OWNED unit in a selection to a point. Static because the
        /// minimap issues this without owning a SelectionOrders instance — it
        /// hands over the world and the selection, the same contract the input
        /// layer uses. What counts as "a unit I may order" is decided here,
        /// not in the panel that was clicked.
        /// </summary>
        public static void IssueMoveToOwnedUnits(EntityManager em, List<Entity> selection,
                                                 float3 destination, Faction faction)
        {
            if (selection == null) return;

            foreach (var entity in selection)
            {
                if (!em.Exists(entity)) continue;
                if (!em.HasComponent<UnitTag>(entity)) continue;
                if (!em.HasComponent<FactionTag>(entity)) continue;
                if (em.GetComponentData<FactionTag>(entity).Value != faction) continue;
                CommandRouter.IssueMove(em, entity, destination);
            }
        }

        public bool CanGarrisonWall(Entity e)
        {
            if (!_em.Exists(e) || !IsOwnedByLocalPlayer(e)) return false;
            if (_em.HasComponent<BuildingTag>(e) || !_em.HasComponent<UnitTag>(e)) return false;
            if (_em.HasComponent<CanBuild>(e) || _em.HasComponent<MinerTag>(e)) return false;
            if (_em.HasComponent<CavalryTag>(e)) return false;
            var cls = _em.GetComponentData<UnitTag>(e).Class;
            if (cls == UnitClass.Siege || cls == UnitClass.Scout) return false;
            return true;
        }

        // Detect the wall's "along" axis at a deck cell by sampling which
        // direction (X vs Z) has more contiguous wall-deck cells. Used to
        // spread garrisoning units along the wall (the debug-wall cubes have
        // identity rotation, so we can't read orientation off the transform).
        private float3 WallAlongAxis(float3 wallTopPoint)
        {
            var cell = TheWaningBorder.Systems.Navigation.NavGridQuery
                .WorldToCellInt2(wallTopPoint);
            if (cell.x == int.MinValue) return new float3(1f, 0f, 0f);
            int xRun = 0, zRun = 0;
            for (int k = 1; k <= 4; k++)
            {
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x + k, cell.y), 1)) xRun++;
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x - k, cell.y), 1)) xRun++;
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x, cell.y + k), 1)) zRun++;
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x, cell.y - k), 1)) zRun++;
            }
            return xRun >= zRun ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f);
        }

        // Order selected FOOT units onto the wall top, spread along the wall
        // around the clicked point. LayeredMoveSystem routes each to the
        // nearest friendly access (tower/gate) or a breach ramp, LERPs it up,
        // then it walks the deck to its slot.
        public void IssueWallTopMove(float3 wallTopPoint)
        {
            // Overpass-bridge decks are roads, not fortifications: ANY
            // movable unit (cavalry, siege, workers included) may cross.
            // Wall ramparts keep the AoE4 foot-unit garrison rule.
            bool isOverpassDeck = TheWaningBorder.World.Terrain.BridgeSurface
                .TryGetDeckHeight(wallTopPoint.x, wallTopPoint.z, out _);

            var units = new List<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (isOverpassDeck)
                {
                    if (_em.Exists(e) && IsOwnedByLocalPlayer(e)
                        && !_em.HasComponent<BuildingTag>(e)
                        && _em.HasComponent<UnitTag>(e))
                        units.Add(e);
                }
                else if (CanGarrisonWall(e)) units.Add(e);
            }

            int n = units.Count;
            if (n == 0) return;

            float3 along = WallAlongAxis(wallTopPoint);
            for (int i = 0; i < n; i++)
            {
                float off = (i - (n - 1) * 0.5f) * FormationSpacing;
                float3 dest = wallTopPoint + along * off;
                CommandRouter.IssueLayeredMove(_em, units[i], dest,
                    NavLayerIndex.LayerRampart, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Put the selection's foot units into a reinforced wall
        /// (docs/Design/Age_1_Alanthor.md § Garrison slots). Each unit takes
        /// the nearest module with a free slot, starting from the one the
        /// player clicked, so a right-click on a manned module spills into
        /// its neighbours instead of failing. Returns false when the target
        /// is not a garrisonable wall or nobody in the selection can take a
        /// slot — the caller then falls through to its usual handling.
        /// </summary>
        public bool TryGarrisonWall(Entity target)
        {
            if (!_em.Exists(target)) return false;
            if (!_em.HasBuffer<WallGarrisonSlot>(target)) return false;
            if (_em.HasComponent<UnderConstruction>(target)) return false;
            if (!_em.HasComponent<LocalTransform>(target)) return false;

            var faction = GameSettings.LocalPlayerFaction;
            if (!_em.HasComponent<FactionTag>(target)
                || _em.GetComponentData<FactionTag>(target).Value != faction) return false;

            float3 at = _em.GetComponentData<LocalTransform>(target).Position;
            bool any = false;
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || !IsOwnedByLocalPlayer(e)) continue;
                if (!TheWaningBorder.Entities.WallGarrison.IsFootUnit(_em, e)) continue;

                // The clicked module first; then the nearest one with room.
                Entity module = TheWaningBorder.Entities.WallGarrison.HasFreeSlot(_em, target)
                    ? target
                    : CommandRouter.FindGarrisonModuleNear(_em, at, faction, WallGarrisonSpill);
                if (module == Entity.Null) break;

                CommandRouter.IssueGarrisonWall(_em, e, module, CommandSource.LocalPlayer);
                any = true;
            }
            return any;
        }

        /// <summary>How far a garrison order spills along the wall when the
        /// clicked module is full, metres.</summary>
        private const float WallGarrisonSpill = 24f;

        public void IssueStopToSelection()
        {
            var selection = CurrentSelection;
            if (selection == null || selection.Count == 0) return;
            var issued = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssueStop(_em, unit, CommandSource.LocalPlayer);
            }
        }

        public void IssueHoldPositionToSelection()
        {
            var selection = CurrentSelection;
            if (selection == null || selection.Count == 0) return;
            var issued = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssueHoldPosition(_em, unit, CommandSource.LocalPlayer);
            }
        }

        public void SetRallyPoints(float3 position, Entity targetEntity)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<BuildingTag>(e)) continue;

                CommandRouter.SetRallyPoint(_em, e, position, targetEntity, CommandSource.LocalPlayer);
            }
        }

        public void IssueAttackCommands(Entity target)
        {
            var issued = new HashSet<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue; // Deduplicate leader commands

                CommandRouter.IssueAttack(_em, unit, target, CommandSource.LocalPlayer);
            }
        }

        public void IssueHealCommands(Entity target)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!CanHeal(e)) continue;

                CommandRouter.IssueHeal(_em, e, target, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// True when the right-click target is a veilstone main node currently
        /// in the Active state — the only state that accepts Purification.
        /// </summary>
        public bool IsActiveBorderMainNode(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target)) return false;
            if (!_em.HasComponent<BorderMainNodeTag>(target)) return false;
            if (!_em.HasComponent<BorderNodeState>(target)) return false;
            return _em.GetComponentData<BorderNodeState>(target).State == NodeState.Active;
        }

        /// <summary>
        /// Issue IssuePurify on every scholar in the current selection
        /// targeting the same node. Non-scholars in the selection ignore the
        /// click (they don't fall back to Attack from here — the right-click
        /// handler treated the click as a Purify intent).
        /// </summary>
        public void IssuePurifyCommands(Entity node)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<ScholarTag>(e)) continue;

                CommandRouter.IssuePurify(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Issue IssueCorrupt on every Corruptor in the current selection.
        /// Same one-target semantics as IssuePurifyCommands.
        /// </summary>
        public void IssueCorruptCommands(Entity node)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CorruptorTag>(e)) continue;

                CommandRouter.IssueCorrupt(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Issue IssueConvertNode on every acolyte in the current selection.
        /// Same one-target semantics as IssuePurifyCommands.
        /// </summary>
        public void IssueConvertNodeCommands(Entity node)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<AcolyteTag>(e)) continue;

                CommandRouter.IssueConvertNode(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        public void IssueConvertCommands(Entity keep)
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;

                CommandRouter.IssueConvert(_em, e, keep, CommandSource.LocalPlayer);
            }
        }

        public void IssueBuildCommands(Entity targetBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(targetBuilding)) return;
            var buildPos = _em.GetComponentData<LocalTransform>(targetBuilding).Position;

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CanBuild>(e)) continue;

                CommandRouter.IssueBuild(_em, e, targetBuilding, "", buildPos,
                    CommandSource.LocalPlayer);
            }
        }

        public void IssueRepairCommands(Entity targetBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(targetBuilding)) return;

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CanBuild>(e)) continue;

                CommandRouter.IssueRepair(_em, e, targetBuilding,
                    CommandSource.LocalPlayer);
            }
        }

        public bool IsBuildingDamaged(Entity building)
        {
            if (!_em.HasComponent<Health>(building)) return false;
            var hp = _em.GetComponentData<Health>(building);
            return hp.Value < hp.Max;
        }

        public void IssuePatrolCommands(float3 destination)
        {
            var issued = new HashSet<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssuePatrol(_em, unit, destination, CommandSource.LocalPlayer);
            }
        }

        // AoE4-style formation move: layout, rank layering, cohesion gate,
        // group speed and the persistent virtual-leader group all live in
        // FormationMoveCommandHelper / FormationGroupSystem — the input
        // layer only collects the selection and picks the formation shape.
        /// <summary>
        /// Walk the selection onto a Shardroot pickup. Claiming is the
        /// 20 s attunement in ShardrootCarrySystem, which starts on its own
        /// once a unit stands within ShardrootPickupRadius; a hero in range
        /// is preferred as the attuner, so ordering King Lexor onto it makes
        /// him the carrier even with an escort at his side.
        /// </summary>
        public void IssuePickupMove(Entity pickup)
        {
            if (pickup == Entity.Null || !_em.Exists(pickup)
                || !_em.HasComponent<LocalTransform>(pickup)) return;
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            float3 at = _em.GetComponentData<LocalTransform>(pickup).Position;
            CommandRouter.IssueFormationMove(_em, units, at, Shape, CommandSource.LocalPlayer);
        }

        public void IssueFormationMove(float3 clickWorld)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationMove(_em, units, clickWorld,
                Shape, CommandSource.LocalPlayer);
        }

        /// <summary>
        /// AoE4: selecting a new formation rearranges the units immediately,
        /// even when standing still — re-issue a formation move to the
        /// selection's own centroid.
        /// </summary>
        public void ReSlotSelectionInPlace()
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count < 2) return;

            float3 centroid = float3.zero;
            int n = 0;
            foreach (var e in units)
            {
                if (!_em.HasComponent<LocalTransform>(e)) continue;
                centroid += _em.GetComponentData<LocalTransform>(e).Position;
                n++;
            }
            if (n == 0) return;
            centroid /= n;

            CommandRouter.IssueFormationMove(_em, units, centroid,
                Shape, CommandSource.LocalPlayer);
        }

        /// <summary>Deduplicated owned, movable (non-building) selection.</summary>
        private List<Entity> CollectOwnedMovableSelection()
        {
            var units = new List<Entity>();
            var added = new HashSet<Entity>();
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!added.Add(e)) continue;
                units.Add(e);
            }
            return units;
        }

        /// <summary>
        /// Shift+right-click: queue a move waypoint on each selected unit instead of replacing their current command.
        /// </summary>
        public void QueueWaypointForSelection(float3 clickWorld)
        {
            // Called only while Shift is held. Every waypoint is appended to
            // the command queue and the entity is marked CommandQueueFrozen,
            // so CommandQueueSystem will not pop the next command until Shift
            // is released (UnfreezeAllQueues clears the tag).
            var selection = CurrentSelection;
            foreach (var e in selection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                // Through the router. Ordinary moves have replicated for a
                // long time; the SHIFT-queued variant never did, so a queued
                // march existed only on the machine that drew it and the other
                // peer's copy of those units simply stood there.
                // docs/Multiplayer_LAN_Readiness.md
                CommandRouter.IssueQueuedWaypoint(_em, e, QueuedCommandType.Move,
                    clickWorld, Entity.Null, CommandSource.LocalPlayer);

                // CommandQueueActive now rides QueuedWaypointDirect itself, so
                // every peer that applies the waypoint also activates the
                // queue. The FREEZE stays local-input-only and is therefore
                // single-player only: a frozen queue on one peer and a
                // draining queue on the other is a position fork. In MP the
                // queue simply starts draining immediately on all peers.
                if (!GameSettings.IsMultiplayer && !_em.HasComponent<CommandQueueFrozen>(e))
                    _em.AddComponent<CommandQueueFrozen>(e);
            }
        }

        // Strips CommandQueueFrozen from every entity that carries it. Called
        // on the frame Shift transitions from held → released, so any queues
        // built up during the hold start draining on the next CommandQueueSystem tick.
        public void UnfreezeAllQueues()
        {
            var query = _frozenQuery.Get(_em, FrozenTypes);
            if (!query.IsEmpty)
                _em.RemoveComponent<CommandQueueFrozen>(query);
        }

        // Formation attack-move: same layout/travel machinery as the plain
        // formation move; members auto-engage en route (AoE4 behavior) and
        // detach from the group the moment they acquire a target.
        public void IssueAttackMoveFormation(float3 clickWorld)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationAttackMove(_em, units, clickWorld,
                Shape, CommandSource.LocalPlayer);
        }



        public enum TargetType { Ground, Enemy, FriendlyUnit, FriendlyBuilding, Resource, Pickup }

        public TargetType DetermineTargetType(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target))
                return TargetType.Ground;

            // A Shardroot pickup on the ground. Checked before the faction
            // test: the pickup carries FactionTag = Border (neutral until
            // claimed), which the hostility test read as an ENEMY with no
            // health -- so right-clicking the artifact produced an attack
            // order that went nowhere (2026-09-15).
            if (_em.HasComponent<ShardrootPickupTag>(target))
                return TargetType.Pickup;

            // Check if it's a resource node (iron mine, veilstone node, or veilsteel node)
            if (_em.HasComponent<IronMineTag>(target))
                return TargetType.Resource;
            if (_em.HasComponent<VeilstoneOutcroppingTag>(target))
                return TargetType.Resource;
            if (_em.HasComponent<VeilsteelDepositTag>(target))
                return TargetType.Resource;

            // Check faction
            if (!_em.HasComponent<FactionTag>(target))
                return TargetType.Ground;

            var targetFaction = _em.GetComponentData<FactionTag>(target).Value;

            // Allies read as FRIENDLY, not enemy: right-clicking a teammate
            // must offer heal / support intent, never an attack order.
            // docs/Design/Teams.md
            if (Alliances.AreHostile(GameSettings.LocalPlayerFaction, targetFaction))
                return TargetType.Enemy;

            if (_em.HasComponent<UnitTag>(target))
                return TargetType.FriendlyUnit;

            if (_em.HasComponent<BuildingTag>(target))
                return TargetType.FriendlyBuilding;

            return TargetType.Ground;
        }



        public struct UnitCapabilities
        {
            public bool CanAttack;
            public bool CanGather;
            public bool CanHeal;
            public bool CanBuildRepair;
            public bool CanPurify;
            /// <summary>Feraldis Corruptor selected — right-click a well to crack it open.</summary>
            public bool CanCorrupt;
            public bool CanConvertNode;
        }

        public UnitCapabilities DetermineCapabilities()
        {
            var caps = new UnitCapabilities();

            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                // Can attack if it has a Damage component
                if (_em.HasComponent<Damage>(e))
                    caps.CanAttack = true;

                // Can gather if is a miner
                if (_em.HasComponent<MinerTag>(e))
                    caps.CanGather = true;

                // Can heal if has heal capability (Litharch, etc.)
                if (CanHeal(e))
                    caps.CanHeal = true;

                // Can build/repair if is a builder
                if (_em.HasComponent<CanBuild>(e))
                    caps.CanBuildRepair = true;

                // Scholar can channel Purification on Active veilstone main nodes.
                if (_em.HasComponent<ScholarTag>(e))
                    caps.CanPurify = true;

                // Acolyte can channel Conversion on Active veilstone main nodes.
                if (_em.HasComponent<AcolyteTag>(e))
                    caps.CanConvertNode = true;

                // Feraldis Corruptor channels corruption on a living well.
                if (_em.HasComponent<CorruptorTag>(e))
                    caps.CanCorrupt = true;
            }

            return caps;
        }

        private bool CanHeal(Entity e)
        {
            // Check for healer tag or component
            // Litharch units can heal
            return _em.HasComponent<LitharchTag>(e);
        }

        /// <summary>
        /// Returns true if the entity belongs to the local player's faction.
        /// </summary>
        public bool IsOwnedByLocalPlayer(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e)) return false;
            return _em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;
        }

        /// <summary>
        /// Returns true if at least one selected entity belongs to the local player.
        /// </summary>
        public bool HasAnyOwnedEntity()
        {
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (IsOwnedByLocalPlayer(e)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if all owned entities in the selection are buildings.
        /// Used to determine if rally point setting should be triggered.
        /// </summary>
        public bool HasOnlyOwnedBuildings()
        {
            bool foundOwned = false;
            foreach (var e in CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                foundOwned = true;
                if (!_em.HasComponent<BuildingTag>(e))
                    return false;
            }
            return foundOwned;
        }

    }
}
