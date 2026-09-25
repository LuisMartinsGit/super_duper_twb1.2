// WorldClickInput.cs
// The right mouse button: where the click landed, what it landed on, and
// which order that becomes for the current selection.
// Part of: Input/ — split out of RTSInputManager.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Core.Commands.Issuing;
using Nav = TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Resolves a right-click into a target and hands it to the order layer.
    ///
    /// The split with <see cref="SelectionOrders"/> is the one the input
    /// assembly is built around: everything here is SCREEN work — a ray, a
    /// collider, a nav cell, which modifier was held — and every decision
    /// about what a unit may do with that target lives in Runtime. This class
    /// asks "what did they point at?", never "may these units attack it?".
    /// </summary>
    public sealed class WorldClickInput
    {
        private readonly EntityManager _em;
        private readonly SelectionOrders _orders;
        private readonly InputModes _modes;
        private readonly LayerMask _clickMask;

        private WorldClickInputConfig _cfg;
        private WorldClickInputConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<WorldClickInputConfig>());

        public WorldClickInput(EntityManager em, SelectionOrders orders,
                               InputModes modes, LayerMask clickMask)
        {
            _em = em;
            _orders = orders;
            _modes = modes;
            _clickMask = clickMask;
        }

        #region Right-click routing

        public void Tick()
        {
            // God-power targeting hook removed alongside GodPowerHUD —
            // sect Fire buttons in ReligionHUD don't use a mouse-targeting
            // mode (they fire at a fixed target / self position).

            // Right-click issues the move/attack on release.
            if (!UnityEngine.Input.GetMouseButtonUp(1)) return;

            if (Cfg == null) return;   // the error is already logged by Require

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            // Clean dead entities from selection
            SelectionSystem.CleanSelection();

            // Only issue commands if at least one selected entity belongs to the local player
            if (!_orders.HasAnyOwnedEntity())
                return;

            if (!TryGetClickPoint(out float3 clickWorld)) return;

            // ── Planning mode intercept: queue into plan list instead of executing ──
            if (PlanningModeOverlay.IsActive)
            {
                var cmdType = QueuedCommandType.Move;
                if (_modes.AttackMove) cmdType = QueuedCommandType.AttackMove;
                else if (_modes.Patrol) cmdType = QueuedCommandType.Patrol;
                _modes.Disarm();

                foreach (var e in selection)
                {
                    if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                    if (!_orders.IsOwnedByLocalPlayer(e)) continue;
                    PlanningModeOverlay.AddPlan(e, cmdType, clickWorld);
                }
                return;
            }

            // ── Shift+Right-Click: queue waypoint instead of replacing command ──
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (shift && !_modes.AnyArmed)
            {
                _orders.QueueWaypointForSelection(clickWorld);
                return;
            }

            // ── Right-click on the WALL TOP (rampart): order selected units
            //    onto the wall. They route to the nearest access point, climb,
            //    and move freely on the rampart. ──
            if (!_modes.AnyArmed && TryGetRampartClick(out float3 rampartPoint))
            {
                _orders.IssueWallTopMove(rampartPoint);
                return;
            }

            // ── Right-click on THE VEIL: selected miners dig the crust at
            //    the closest crusted vertex (Astroneer-style — the sheet
            //    itself is the deposit). DISABLED while the veil is
            //    influence-only (VeilCrustConstants.CrustPhysical false):
            //    veilstone comes from discrete deposits, and routing miners
            //    into the reforming crust stranded and killed them. ──
            // Determine target and issue appropriate command
            var target = ScreenPick.EntityUnderMouse(_clickMask, _em);
            var targetType = _orders.DetermineTargetType(target);

            // Attack-move mode: A + right-click
            if (_modes.AttackMove)
            {
                _modes.Disarm();
                var amCaps = _orders.DetermineCapabilities();

                if (targetType == SelectionOrders.TargetType.Enemy && amCaps.CanAttack)
                {
                    // Clicking enemy in attack-move mode issues normal attack
                    _orders.IssueAttackCommands(target);
                }
                else if (targetType == SelectionOrders.TargetType.Ground || targetType == SelectionOrders.TargetType.FriendlyUnit
                         || targetType == SelectionOrders.TargetType.FriendlyBuilding || targetType == SelectionOrders.TargetType.Resource
                         || targetType == SelectionOrders.TargetType.Pickup)
                {
                    // Clicking ground (or non-enemy) issues attack-move formation
                    _orders.IssueAttackMoveFormation(clickWorld);
                }
                return;
            }

            // Patrol mode: P + right-click
            if (_modes.Patrol)
            {
                _modes.Disarm();
                if (targetType == SelectionOrders.TargetType.Enemy)
                {
                    var pCaps = _orders.DetermineCapabilities();
                    if (pCaps.CanAttack)
                        _orders.IssueAttackCommands(target);
                }
                else
                {
                    _orders.IssuePatrolCommands(clickWorld);
                }
                return;
            }

            // If ONLY owned buildings are selected and right-clicking ground, set rally point
            if (targetType == SelectionOrders.TargetType.Ground && _orders.HasOnlyOwnedBuildings())
            {
                _orders.SetRallyPoints(clickWorld, Entity.Null);
                return;
            }

            // Same flow but with a resource as the rally target — newly
            // trained miners auto-gather it on spawn (TrainingSystem reads
            // RallyPoint.TargetEntity). Lets the player point a Hall at a
            // veilstone / iron deposit and walk away.
            if (targetType == SelectionOrders.TargetType.Resource && _orders.HasOnlyOwnedBuildings())
            {
                _orders.SetRallyPoints(clickWorld, target);
                return;
            }

            var capabilities = _orders.DetermineCapabilities();

            switch (targetType)
            {
                case SelectionOrders.TargetType.Enemy:
                    // Scholar + Active veilstone main node → Purify ritual.
                    // Falls through to Attack if the scholar is not selected
                    // or the node is no longer Active (Cleansed/Converted/
                    // Destroyed nodes don't accept purification).
                    if (capabilities.CanPurify && _orders.IsActiveBorderMainNode(target))
                    {
                        _orders.IssuePurifyCommands(target);
                        break;
                    }
                    // Corruptor + living veilstone main node → Corruption.
                    // Feraldis cracks a well open rather than claiming it.
                    if (capabilities.CanCorrupt && _orders.IsActiveBorderMainNode(target))
                    {
                        _orders.IssueCorruptCommands(target);
                        break;
                    }
                    // Acolyte + Active veilstone main node → Conversion ritual.
                    if (capabilities.CanConvertNode && _orders.IsActiveBorderMainNode(target))
                    {
                        _orders.IssueConvertNodeCommands(target);
                        break;
                    }
                    if (capabilities.CanAttack)
                        _orders.IssueAttackCommands(target);
                    break;

                case SelectionOrders.TargetType.FriendlyUnit:
                    if (capabilities.CanHeal)
                        _orders.IssueHealCommands(target);
                    else
                        _orders.IssueFormationMove(clickWorld);
                    break;

                case SelectionOrders.TargetType.FriendlyBuilding:
                    // A REINFORCED wall has two slots per module: the men walk
                    // into it rather than onto it
                    // (docs/Design/Age_1_Alanthor.md § Garrison slots). Tried
                    // first, because a level-3 wall is also a WallTag and would
                    // otherwise take the wall-top order below.
                    if (_orders.TryGarrisonWall(target)) break;

                    // AoE4: right-click your own wall -> foot units garrison it
                    // (route to stairs, climb, spread along the top). Segments
                    // are data-only; skip those and under-construction walls.
                    if (_em.HasComponent<WallTag>(target)
                        && !_em.HasComponent<WallSegmentTag>(target)
                        && !_em.HasComponent<UnderConstruction>(target)
                        && _em.HasComponent<LocalTransform>(target))
                    {
                        var wp = _em.GetComponentData<LocalTransform>(target).Position;
                        _orders.IssueWallTopMove(new float3(wp.x, Nav.LayerTransitionSystem.DeckY, wp.z));
                        break;
                    }
                    if (capabilities.CanBuildRepair && _em.HasComponent<UnderConstruction>(target))
                        _orders.IssueBuildCommands(target);
                    else if (capabilities.CanBuildRepair && _orders.IsBuildingDamaged(target))
                        _orders.IssueRepairCommands(target);
                    else if (capabilities.CanGather && _em.HasComponent<FiendstoneKeepTag>(target)
                             && !_em.HasComponent<UnderConstruction>(target))
                        _orders.IssueConvertCommands(target);
                    else
                        _orders.IssueFormationMove(clickWorld);
                    break;

                case SelectionOrders.TargetType.Resource:
                    // Nothing gathers any more (Regions.md §4), so a resource
                    // is just a thing standing in the world: walk to it.
                    _orders.IssueFormationMove(clickWorld);
                    break;

                case SelectionOrders.TargetType.Pickup:
                    // The Shardroot: walk onto it, the attunement does the rest.
                    _orders.IssuePickupMove(target);
                    break;

                case SelectionOrders.TargetType.Ground:
                default:
                    // Ground clicks are always moves. Gather requires clicking
                    // the deposit entity itself (handled by SelectionOrders.TargetType.Resource);
                    // snapping nearby ground clicks to a gather order would make
                    // it impossible to move workers to positions near a node.
                    _orders.IssueFormationMove(clickWorld);
                    break;
            }
        }

        #endregion

        #region Click point resolution

        // True when the cursor is pointing at a WALL TOP. Collider-independent:
        // projects the cursor ray onto the deck plane (y = DeckY) and checks
        // whether that cell is walkable on the wall-deck layer. This makes the
        // wall top clickable even though it has no dedicated top collider — the
        // player just points at the wall and we test the deck-plane hit cell.
        private bool TryGetRampartClick(out float3 rampartPoint)
        {
            rampartPoint = float3.zero;
            var cam = TheWaningBorder.Core.PresentationState.GameplayCamera;
            if (!cam) return false;

            float deckY = Nav.LayerTransitionSystem.DeckY;
            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Mathf.Abs(ray.direction.y) < Cfg.rampartRayEpsilon) return false;
            float t = (deckY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false; // deck plane is behind the camera

            Vector3 hit = ray.origin + ray.direction * t;
            var cell = Nav.NavGridQuery.WorldToCellInt2(new float3(hit.x, deckY, hit.z));
            if (cell.x == int.MinValue) return false;
            // Only a real wall-top cell is passable on the deck layer.
            if (!Nav.NavGridQuery.IsCellPassable(cell, Cfg.wallDeckLayer))
                return false;

            float3 c = Nav.NavGridQuery.GetCellWorldCenter(cell);
            rampartPoint = new float3(c.x, deckY, c.z);
            return true;
        }

        /// <summary>Where on the ground the cursor is pointing, snapped to
        /// somewhere a unit can actually stand.</summary>
        private bool TryGetClickPoint(out float3 point)
        {
            point = float3.zero;
            var cam = TheWaningBorder.Core.PresentationState.GameplayCamera;
            if (!cam) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Cfg.clickRayLength, _clickMask))
                return false;

            point = hit.point;
            point = SnapDestinationOffImpassable(point);
            return true;
        }

        /// <summary>
        /// Underwater / impassable spots aren't valid move destinations. The
        /// water plane has no collider, so a click over water hits the terrain
        /// bed underneath — if that point is below the water surface, or on any
        /// cell the nav cost field marks impassable (deep water + over-budget
        /// mountain slope), snap the destination to the nearest walkable cell
        /// so units route to the closest reachable point instead of trying to
        /// path into the lake / up a cliff. Land clicks pass through unchanged.
        /// </summary>
        private static float3 SnapDestinationOffImpassable(float3 point)
        {
            var water = TheWaningBorder.World.Terrain.WaterPlane.Instance;
            bool underwater = water != null && point.y < water.waterLevel;

            int2 cell = Nav.NavGridQuery.WorldToCellInt2(point);
            bool impassable = cell.x != int.MinValue && !Nav.NavGridQuery.IsCellPassable(cell);

            if (!underwater && !impassable)
                return point;

            Nav.NavGridQuery.SnapToWalkable(point, out float3 snapped, out bool ok);
            if (!ok)
                return point; // no walkable cell within snap radius — leave as-is

            snapped.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(snapped.x, snapped.z);
            return snapped;
        }

        #endregion
    }
}
