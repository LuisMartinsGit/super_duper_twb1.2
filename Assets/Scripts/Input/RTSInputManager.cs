// RTSInputManager.cs
// Core input handler - routes all player commands through CommandRouter
// Part of: Input/

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Handles player input and routes commands through CommandRouter.
    /// Works with SelectionSystem for entity selection.
    /// 
    /// Responsibilities:
    /// - Right-click command handling (move, attack, gather, heal)
    /// - Rally point setting
    /// - Formation movement
    /// - Input blocking when UI is active
    /// </summary>
    public class RTSInputManager : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        
        [Header("Raycasting")]
        [SerializeField] private LayerMask clickMask = ~0;
        
        [Header("Formation")]
        [SerializeField] private float formationSpacing = 2.0f;
        
        // ═══════════════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private EntityWorld _world;
        private EntityManager _em;
        private bool _attackMoveMode = false;
        private bool _patrolMode = false;
        // Tracks Shift state across frames so we can detect the down→up
        // transition and unfreeze accumulated queues at that moment.
        private bool _shiftWasHeld = false;

        /// <summary>
        /// Currently hovered entity (for UI highlighting).
        /// </summary>
        public static Entity CurrentHover { get; private set; }
        
        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════
        
        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;

            // Ensure ControlGroupSystem exists
            if (FindFirstObjectByType<ControlGroupSystem>() == null)
                gameObject.AddComponent<ControlGroupSystem>();
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;

            // Refresh EntityManager if needed
            if (_em.Equals(default(EntityManager)))
                _em = _world.EntityManager;

            // Detect Shift release independent of UI/blocking guards: if the
            // user lets go of Shift while their cursor happens to be over a
            // panel, queues must still resume — otherwise the frozen tag
            // would persist forever and the units would never move.
            bool shiftHeldNow = UnityEngine.Input.GetKey(KeyCode.LeftShift)
                              || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (_shiftWasHeld && !shiftHeldNow)
                UnfreezeAllQueues();
            _shiftWasHeld = shiftHeldNow;

            // Allow ESC to close menu even when other input is blocked
            // (but not during building placement -- let BuilderCommandPanel handle ESC there)
            if (InGameMenuPanel.IsOpen && !BuilderCommandPanel.IsPlacingBuilding
                && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                InGameMenuPanel.Close();
                return;
            }

            // Block input during UI interactions or building placement
            if (ShouldBlockInput())
                return;

            // Update hover state (always allowed, even for observers)
            UpdateHover();

            // Observer mode: block all commands but allow hover/selection
            if (GameSettings.IsObserver)
                return;

            // Handle hotkeys
            HandleHotkeys();

            // Handle right-click commands
            HandleRightClick();
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // INPUT BLOCKING
        // ═══════════════════════════════════════════════════════════════════════
        
        private bool ShouldBlockInput()
        {
            // Block all input when in-game menu is open
            if (InGameMenuPanel.IsOpen)
                return true;

            // One-frame suppression (after GUI button clicks)
            if (BuilderCommandPanel.SuppressClicksThisFrame)
            {
                BuilderCommandPanel.SuppressClicksThisFrame = false;
                return true;
            }

            // Web HUD: while the pointer hovers an interactive HTML element
            // (button / panel / sidebar handle), block game-world input so the
            // same click doesn't ALSO deselect units or fire orders. CEF runs
            // in a separate process so its event capture can't suppress
            // Unity's input on its own — we mirror the state explicitly via
            // the `hud:capture` bridge topic.
            if (TheWaningBorder.UI.Web.HudWebController.IsPointerOverWebHud)
                return true;

            // Block if mouse is over UI panels
            if (EntityActionPanel.IsPointerOver() || EntityInfoPanel.IsPointerOver())
                return true;

            // Block if mouse is over spell panel
            if (SpellPanel.IsPointerOverPanel)
                return true;

            // Block if culture choice popup is visible (modal dialog)
            if (CultureChoicePopup.IsVisible)
                return true;

            // Block during building placement
            if (BuilderCommandPanel.IsPlacingBuilding)
                return true;

            // Block if in-game menu is open
            if (InGameMenuPanel.IsOpen)
                return true;

            return false;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // HOTKEYS
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleHotkeys()
        {
            // ESC - cascading: close menu > cancel modes > clear selection > open menu
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                if (InGameMenuPanel.IsOpen)
                {
                    InGameMenuPanel.Toggle();
                }
                else if (PlanningModeOverlay.IsActive)
                {
                    PlanningModeOverlay.Cancel();
                }
                else if (_attackMoveMode || _patrolMode)
                {
                    _attackMoveMode = false;
                    _patrolMode = false;
                }
                else if (SelectionSystem.CurrentSelection != null && SelectionSystem.CurrentSelection.Count > 0)
                {
                    SelectionSystem.ClearSelection();
                }
                else
                {
                    InGameMenuPanel.Toggle();
                }
            }

            // A - Enter attack-move mode
            if (UnityEngine.Input.GetKeyDown(KeyCode.A))
            {
                _attackMoveMode = true;
                _patrolMode = false;
            }

            // P - Enter patrol mode
            if (UnityEngine.Input.GetKeyDown(KeyCode.P))
            {
                _patrolMode = true;
                _attackMoveMode = false;
            }

            // S - Stop all selected units
            if (UnityEngine.Input.GetKeyDown(KeyCode.S))
            {
                _attackMoveMode = false;
                _patrolMode = false;
                IssueStopToSelection();
            }

            // H - Hold position for all selected units
            if (UnityEngine.Input.GetKeyDown(KeyCode.H))
            {
                _attackMoveMode = false;
                _patrolMode = false;
                IssueHoldPositionToSelection();
            }

            // B - Cycle through idle builders (workers with no BuildOrder/RepairOrder).
            // Each press selects the next idle builder of the local player faction
            // and centers the camera on it.
            if (UnityEngine.Input.GetKeyDown(KeyCode.B))
            {
                CycleIdleBuilders();
            }

            // Z - Toggle planning mode (BFME2); Enter also executes
            if (UnityEngine.Input.GetKeyDown(KeyCode.Z))
            {
                if (PlanningModeOverlay.IsActive)
                    PlanningModeOverlay.ExecuteAll(_em);
                else
                    PlanningModeOverlay.Toggle();
            }
            if (PlanningModeOverlay.IsActive && UnityEngine.Input.GetKeyDown(KeyCode.Return))
            {
                PlanningModeOverlay.ExecuteAll(_em);
            }

            // Control groups (1-9)
            for (int i = 0; i < 9; i++)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl)
                             || UnityEngine.Input.GetKey(KeyCode.RightControl);
                    bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift)
                              || UnityEngine.Input.GetKey(KeyCode.RightShift);

                    if (ctrl)
                        ControlGroupSystem.AssignGroup(i);
                    else if (shift)
                        ControlGroupSystem.AddToGroup(i);
                    else
                        ControlGroupSystem.HandleRecallOrCenter(i);

                    break;
                }
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // HOVER DETECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private void UpdateHover()
        {
            var hovered = RaycastPickEntity();
            CurrentHover = (_em.Exists(hovered)) ? hovered : Entity.Null;

            RTSInput.SetHovered(CurrentHover);
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // RIGHT-CLICK COMMAND HANDLING
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleRightClick()
        {
            // God-power targeting hook removed alongside GodPowerHUD —
            // sect Fire buttons in ReligionHUD don't use a mouse-targeting
            // mode (they fire at a fixed target / self position).

            // Right-click issues the move/attack on release.
            if (!UnityEngine.Input.GetMouseButtonUp(1)) return;

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            // Clean dead entities from selection
            SelectionSystem.CleanSelection();

            // Only issue commands if at least one selected entity belongs to the local player
            if (!HasAnyOwnedEntity())
                return;

            if (!TryGetClickPoint(out float3 clickWorld)) return;

            // ── Planning mode intercept: queue into plan list instead of executing ──
            if (PlanningModeOverlay.IsActive)
            {
                var target0 = RaycastPickEntity();
                var targetType0 = DetermineTargetType(target0);
                var cmdType = QueuedCommandType.Move;
                if (_attackMoveMode) { cmdType = QueuedCommandType.AttackMove; _attackMoveMode = false; }
                else if (_patrolMode) { cmdType = QueuedCommandType.Patrol; _patrolMode = false; }

                foreach (var e in selection)
                {
                    if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                    if (!IsOwnedByLocalPlayer(e)) continue;
                    PlanningModeOverlay.AddPlan(e, cmdType, clickWorld);
                }
                return;
            }

            // ── Shift+Right-Click: queue waypoint instead of replacing command ──
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (shift && !_attackMoveMode && !_patrolMode)
            {
                QueueWaypointForSelection(clickWorld);
                return;
            }

            // ── Right-click on the WALL TOP (rampart): order selected units
            //    onto the wall. They route to the nearest access point, climb,
            //    and move freely on the rampart. ──
            if (!_attackMoveMode && !_patrolMode && TryGetRampartClick(out float3 rampartPoint))
            {
                IssueWallTopMove(rampartPoint);
                return;
            }

            // Determine target and issue appropriate command
            var target = RaycastPickEntity();
            var targetType = DetermineTargetType(target);

            // Attack-move mode: A + right-click
            if (_attackMoveMode)
            {
                _attackMoveMode = false;
                var amCaps = DetermineCapabilities();

                if (targetType == TargetType.Enemy && amCaps.CanAttack)
                {
                    // Clicking enemy in attack-move mode issues normal attack
                    IssueAttackCommands(target);
                }
                else if (targetType == TargetType.Ground || targetType == TargetType.FriendlyUnit
                         || targetType == TargetType.FriendlyBuilding || targetType == TargetType.Resource)
                {
                    // Clicking ground (or non-enemy) issues attack-move formation
                    IssueAttackMoveFormation(clickWorld);
                }
                return;
            }

            // Patrol mode: P + right-click
            if (_patrolMode)
            {
                _patrolMode = false;
                if (targetType == TargetType.Enemy)
                {
                    var pCaps = DetermineCapabilities();
                    if (pCaps.CanAttack)
                        IssueAttackCommands(target);
                }
                else
                {
                    IssuePatrolCommands(clickWorld);
                }
                return;
            }

            // If ONLY owned buildings are selected and right-clicking ground, set rally point
            if (targetType == TargetType.Ground && HasOnlyOwnedBuildings())
            {
                SetRallyPoints(clickWorld, Entity.Null);
                return;
            }

            // Same flow but with a resource as the rally target — newly
            // trained miners auto-gather it on spawn (TrainingSystem reads
            // RallyPoint.TargetEntity). Lets the player point a Hall at a
            // crystal / iron deposit and walk away.
            if (targetType == TargetType.Resource && HasOnlyOwnedBuildings())
            {
                SetRallyPoints(clickWorld, target);
                return;
            }

            var capabilities = DetermineCapabilities();

            switch (targetType)
            {
                case TargetType.Enemy:
                    // Scholar + Active crystal main node → Purify ritual.
                    // Falls through to Attack if the scholar isn't selected
                    // or the node is no longer Active (Cleansed/Converted/
                    // Destroyed nodes don't accept purification).
                    if (capabilities.CanPurify && IsActiveCrystalMainNode(target))
                    {
                        IssuePurifyCommands(target);
                        break;
                    }
                    // Acolyte + Active crystal main node → Conversion ritual.
                    if (capabilities.CanConvertNode && IsActiveCrystalMainNode(target))
                    {
                        IssueConvertNodeCommands(target);
                        break;
                    }
                    if (capabilities.CanAttack)
                        IssueAttackCommands(target);
                    break;

                case TargetType.FriendlyUnit:
                    if (capabilities.CanHeal)
                        IssueHealCommands(target);
                    else
                        IssueFormationMove(clickWorld);
                    break;

                case TargetType.FriendlyBuilding:
                    // AoE4: right-click your own wall -> foot units garrison it
                    // (route to stairs, climb, spread along the top). Segments
                    // are data-only; skip those and under-construction walls.
                    if (_em.HasComponent<WallTag>(target)
                        && !_em.HasComponent<WallSegmentTag>(target)
                        && !_em.HasComponent<UnderConstruction>(target)
                        && _em.HasComponent<LocalTransform>(target))
                    {
                        var wp = _em.GetComponentData<LocalTransform>(target).Position;
                        IssueWallTopMove(new float3(wp.x,
                            TheWaningBorder.Systems.Navigation.LayerTransitionSystem.DeckY, wp.z));
                        break;
                    }
                    if (capabilities.CanBuildRepair && _em.HasComponent<UnderConstruction>(target))
                        IssueBuildCommands(target);
                    else if (capabilities.CanBuildRepair && IsBuildingDamaged(target))
                        IssueRepairCommands(target);
                    else if (capabilities.CanGather && _em.HasComponent<FiendstoneKeepTag>(target)
                             && !_em.HasComponent<UnderConstruction>(target))
                        IssueConvertCommands(target);
                    else if (capabilities.CanGather && _em.HasComponent<SmelterTag>(target)
                             && !_em.HasComponent<UnderConstruction>(target))
                        IssueForgeSupply(target);
                    else if (capabilities.CanGather && IsDropOffPoint(target))
                        IssueMinerDropOff(target);
                    else
                        IssueFormationMove(clickWorld);
                    break;

                case TargetType.Resource:
                    if (capabilities.CanGather)
                        IssueGatherCommands(target);
                    else
                        IssueFormationMove(clickWorld);
                    break;

                case TargetType.Ground:
                default:
                    // Ground clicks are always moves. Gather requires clicking
                    // the deposit entity itself (handled by TargetType.Resource);
                    // snapping nearby ground clicks to a gather order would make
                    // it impossible to move workers to positions near a node.
                    IssueFormationMove(clickWorld);
                    break;
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // COMMAND ISSUANCE
        // ═══════════════════════════════════════════════════════════════════════
        
        // True when the cursor is pointing at a WALL TOP. Collider-independent:
        // projects the cursor ray onto the deck plane (y = DeckY) and checks
        // whether that cell is walkable on the wall-deck layer. This makes the
        // wall top clickable even though it has no dedicated top collider — the
        // player just points at the wall and we test the deck-plane hit cell.
        private bool TryGetRampartClick(out float3 rampartPoint)
        {
            rampartPoint = float3.zero;
            var cam = Camera.main;
            if (!cam) return false;

            float deckY = TheWaningBorder.Systems.Navigation.LayerTransitionSystem.DeckY;
            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (deckY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false; // deck plane is behind the camera

            Vector3 hit = ray.origin + ray.direction * t;
            var cell = TheWaningBorder.Systems.Navigation.NavGridQuery
                .WorldToCellInt2(new float3(hit.x, deckY, hit.z));
            if (cell.x == int.MinValue) return false;
            // Wall-deck layer == 1; only a real wall-top cell is passable there.
            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell, 1))
                return false;

            float3 c = TheWaningBorder.Systems.Navigation.NavGridQuery.GetCellWorldCenter(cell);
            rampartPoint = new float3(c.x, deckY, c.z);
            return true;
        }

        // AoE4 rule: only foot units (infantry / ranged) garrison walls —
        // never cavalry, siege, villagers/builders/miners, or buildings.
        private bool CanGarrisonWall(Entity e)
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
        private void IssueWallTopMove(float3 wallTopPoint)
        {
            var units = new List<Entity>();
            foreach (var e in SelectionSystem.CurrentSelection)
                if (CanGarrisonWall(e)) units.Add(e);

            int n = units.Count;
            if (n == 0) return;

            float3 along = WallAlongAxis(wallTopPoint);
            for (int i = 0; i < n; i++)
            {
                float off = (i - (n - 1) * 0.5f) * formationSpacing;
                float3 dest = wallTopPoint + along * off;
                CommandRouter.IssueLayeredMove(_em, units[i], dest,
                    NavLayerIndex.LayerRampart, CommandSource.LocalPlayer);
            }
        }

        private void IssueStopToSelection()
        {
            var selection = SelectionSystem.CurrentSelection;
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

        private void IssueHoldPositionToSelection()
        {
            var selection = SelectionSystem.CurrentSelection;
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

        private void SetRallyPoints(float3 position, Entity targetEntity)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<BuildingTag>(e)) continue;

                CommandRouter.SetRallyPoint(_em, e, position, targetEntity, CommandSource.LocalPlayer);
            }
        }

        private void IssueAttackCommands(Entity target)
        {
            var issued = new HashSet<Entity>();
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue; // Deduplicate leader commands

                CommandRouter.IssueAttack(_em, unit, target, CommandSource.LocalPlayer);
            }
        }

        private void IssueHealCommands(Entity target)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!CanHeal(e)) continue;

                CommandRouter.IssueHeal(_em, e, target, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// True when the right-click target is a crystal main node currently
        /// in the Active state — the only state that accepts Purification.
        /// </summary>
        private bool IsActiveCrystalMainNode(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target)) return false;
            if (!_em.HasComponent<CrystalMainNodeTag>(target)) return false;
            if (!_em.HasComponent<CrystalNodeState>(target)) return false;
            return _em.GetComponentData<CrystalNodeState>(target).State == NodeState.Active;
        }

        /// <summary>
        /// Issue IssuePurify on every scholar in the current selection
        /// targeting the same node. Non-scholars in the selection ignore the
        /// click (they don't fall back to Attack from here — the right-click
        /// handler treated the click as a Purify intent).
        /// </summary>
        private void IssuePurifyCommands(Entity node)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<ScholarTag>(e)) continue;

                CommandRouter.IssuePurify(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        /// <summary>
        /// Issue IssueConvertNode on every acolyte in the current selection.
        /// Same one-target semantics as IssuePurifyCommands.
        /// </summary>
        private void IssueConvertNodeCommands(Entity node)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<AcolyteTag>(e)) continue;

                CommandRouter.IssueConvertNode(_em, e, node, CommandSource.LocalPlayer);
            }
        }

        private void IssueConvertCommands(Entity keep)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;

                CommandRouter.IssueConvert(_em, e, keep, CommandSource.LocalPlayer);
            }
        }

        private void IssueGatherCommands(Entity resourceNode)
        {
            Entity depositLocation = FindNearestGatherersHut();

            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;

                CommandRouter.IssueGather(_em, e, resourceNode, depositLocation, CommandSource.LocalPlayer);
            }
        }

        private void IssueBuildCommands(Entity targetBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(targetBuilding)) return;
            var buildPos = _em.GetComponentData<LocalTransform>(targetBuilding).Position;

            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CanBuild>(e)) continue;

                CommandRouter.IssueBuild(_em, e, targetBuilding, "", buildPos,
                    CommandSource.LocalPlayer);
            }
        }

        private void IssueRepairCommands(Entity targetBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(targetBuilding)) return;

            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<CanBuild>(e)) continue;

                CommandRouter.IssueRepair(_em, e, targetBuilding,
                    CommandSource.LocalPlayer);
            }
        }

        private bool IsBuildingDamaged(Entity building)
        {
            if (!_em.HasComponent<Health>(building)) return false;
            var hp = _em.GetComponentData<Health>(building);
            return hp.Value < hp.Max;
        }

        /// <summary>
        /// Returns true if the building is a valid resource drop-off point (Hall or GathererHut, completed).
        /// </summary>
        private bool IsDropOffPoint(Entity building)
        {
            if (_em.HasComponent<UnderConstruction>(building)) return false;
            return _em.HasComponent<HallTag>(building) || _em.HasComponent<GathererHutTag>(building);
        }

        /// <summary>
        /// Orders selected miners to return to the target drop-off building and deposit resources.
        /// Crystal miners switch to ReturningToBase; iron miners move to the building.
        /// </summary>
        private void IssueMinerDropOff(Entity dropOffBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(dropOffBuilding)) return;
            var dropOffPos = _em.GetComponentData<LocalTransform>(dropOffBuilding).Position;

            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;
                if (!_em.HasComponent<MinerState>(e)) continue;

                var miner = _em.GetComponentData<MinerState>(e);

                // Set dropoff target and switch to returning state
                miner.DropoffTarget = dropOffBuilding;
                miner.State = MinerWorkState.ReturningToBase;
                _em.SetComponentData(e, miner);

                // Clear UserMoveOrder so mining systems don't interrupt the dropoff
                if (_em.HasComponent<UserMoveOrder>(e))
                    _em.RemoveComponent<UserMoveOrder>(e);

                // Clear GatherCommand if pending
                if (_em.HasComponent<GatherCommand>(e))
                    _em.RemoveComponent<GatherCommand>(e);

                // Move to the drop-off building
                if (_em.HasComponent<DesiredDestination>(e))
                {
                    _em.SetComponentData(e, new DesiredDestination
                    {
                        Position = dropOffPos,
                        Has = 1
                    });
                }
                else
                {
                    _em.AddComponentData(e, new DesiredDestination
                    {
                        Position = dropOffPos,
                        Has = 1
                    });
                }
            }
        }

        /// <summary>
        /// Assigns selected miners to supply a Smelter (Forge) with iron and crystal.
        /// Miners will pick up resources from nearest Hall/GathererHut and deliver to forge.
        /// </summary>
        private void IssueForgeSupply(Entity smelter)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;
                if (!_em.HasComponent<MinerState>(e)) continue;

                // Clear any existing mining orders
                if (_em.HasComponent<UserMoveOrder>(e))
                    _em.RemoveComponent<UserMoveOrder>(e);
                if (_em.HasComponent<GatherCommand>(e))
                    _em.RemoveComponent<GatherCommand>(e);

                // Reset miner state
                var miner = _em.GetComponentData<MinerState>(e);
                miner.State = MinerWorkState.Idle;
                miner.AssignedDeposit = Entity.Null;
                miner.DropoffTarget = Entity.Null;
                _em.SetComponentData(e, miner);

                // Assign forge supply order
                if (_em.HasComponent<ForgeSupplyOrder>(e))
                {
                    _em.SetComponentData(e, new ForgeSupplyOrder
                    {
                        Forge = smelter,
                        ResourceType = 0,
                        Phase = 0
                    });
                }
                else
                {
                    _em.AddComponentData(e, new ForgeSupplyOrder
                    {
                        Forge = smelter,
                        ResourceType = 0,
                        Phase = 0
                    });
                }
            }
        }

        private void IssuePatrolCommands(float3 destination)
        {
            var issued = new HashSet<Entity>();
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;

                Entity unit = e;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssuePatrol(_em, unit, destination, CommandSource.LocalPlayer);
            }
        }

        private void IssueFormationMove(float3 clickWorld)
        {
            var selection = SelectionSystem.CurrentSelection;

            // Collect movable owned units with their positions and speeds.
            var units = new List<Entity>();
            var positions = new List<float3>();
            var speeds = new List<float>();
            var added = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e))
                    continue;
                if (!IsOwnedByLocalPlayer(e))
                    continue;
                if (!added.Add(e)) continue;

                units.Add(e);
                positions.Add(_em.HasComponent<LocalTransform>(e)
                    ? _em.GetComponentData<LocalTransform>(e).Position
                    : float3.zero);
                speeds.Add(_em.HasComponent<MoveSpeed>(e)
                    ? _em.GetComponentData<MoveSpeed>(e).Value
                    : 3.5f);
            }

            int count = units.Count;
            if (count == 0) return;

            // ── Move direction = from selection centroid to click target ──
            float3 centroid = float3.zero;
            for (int i = 0; i < count; i++) centroid += positions[i];
            centroid /= count;

            float3 moveDir = clickWorld - centroid;
            moveDir.y = 0f;
            if (math.lengthsq(moveDir) < 0.01f)
            {
                var cam0 = Camera.main;
                Vector3 cf = cam0
                    ? Vector3.ProjectOnPlane(cam0.transform.forward, Vector3.up).normalized
                    : Vector3.forward;
                moveDir = new float3(cf.x, 0f, cf.z);
            }
            moveDir = math.normalize(moveDir);
            float3 rightDir = math.cross(new float3(0f, 1f, 0f), moveDir);

            // ── Square-ish grid layout: cols = ceil(sqrt(N)), front rows fill
            // first so a partial back row is centred. Slot spacing is the
            // configured formationSpacing (all units are individual now, so a
            // uniform slot pitch — no per-battalion footprint). ──
            float slotPitch = formationSpacing;
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));
            int[] rowCount = new int[rows];
            int remaining = count;
            for (int r = 0; r < rows; r++)
            {
                rowCount[r] = Mathf.Min(cols, remaining);
                remaining -= rowCount[r];
            }

            var slots = new float3[count];
            int[] slotRow = new int[count];
            int[] slotCol = new int[count];
            int[] slotsInRow = new int[count];
            int slotIdx = 0;
            for (int r = 0; r < rows; r++)
            {
                int rc = rowCount[r];
                float rowWidth = rc * slotPitch;
                float startOffset = -rowWidth * 0.5f + slotPitch * 0.5f;
                for (int c = 0; c < rc; c++)
                {
                    float lateralOffset = startOffset + c * slotPitch;
                    slots[slotIdx] = clickWorld
                                   + rightDir * lateralOffset
                                   - moveDir * (r * slotPitch);
                    slotRow[slotIdx] = r;
                    slotCol[slotIdx] = c;
                    slotsInRow[slotIdx] = rc;
                    slotIdx++;
                }
            }

            // ── Role of each slot: front-row interior = Front, front-row edges
            // = Wing, back rows = Back. Role values gapped so role mismatch
            // dominates distance in the assignment cost. ──
            const float ROLE_PENALTY = 1_000_000f;
            int[] slotRole = new int[count];
            for (int s = 0; s < count; s++)
            {
                if (slotRow[s] > 0) slotRole[s] = 2;                       // Back
                else if (slotsInRow[s] > 1
                         && (slotCol[s] == 0 || slotCol[s] == slotsInRow[s] - 1))
                    slotRole[s] = 1;                                       // Wing
                else slotRole[s] = 0;                                      // Front
            }

            int[] unitRole = new int[count];
            for (int i = 0; i < count; i++) unitRole[i] = ClassifyUnitRole(units[i]);

            // ── Greedy assignment: role match wins overall, distance is the
            // tie-break within a role. ──
            int[] unitToSlot = new int[count];
            bool[] slotUsed = new bool[count];
            for (int i = 0; i < count; i++) unitToSlot[i] = -1;

            var pairs = new List<(int unit, int slot, float cost)>(count * count);
            for (int u = 0; u < count; u++)
            for (int s = 0; s < count; s++)
            {
                float3 d = slots[s] - positions[u];
                d.y = 0f;
                float cost = math.lengthsq(d);
                if (slotRole[s] != unitRole[u]) cost += ROLE_PENALTY;
                pairs.Add((u, s, cost));
            }
            pairs.Sort((a, b) => a.cost.CompareTo(b.cost));

            int assigned = 0;
            for (int p = 0; p < pairs.Count && assigned < count; p++)
            {
                var pair = pairs[p];
                if (unitToSlot[pair.unit] != -1) continue;
                if (slotUsed[pair.slot]) continue;
                unitToSlot[pair.unit] = pair.slot;
                slotUsed[pair.slot] = true;
                assigned++;
            }

            // Slowest speed across the group so everyone advances together.
            float slowestSpeed = float.MaxValue;
            for (int i = 0; i < count; i++)
                if (speeds[i] > 0 && speeds[i] < slowestSpeed) slowestSpeed = speeds[i];
            if (slowestSpeed <= 0f || slowestSpeed == float.MaxValue)
                slowestSpeed = 3.5f;

            // ── Issue per-unit moves to assigned slots. Each unit pathfinds
            // independently (cost-field collision); the slot just gives it a
            // distinct destination so the group lands in formation. ──
            for (int i = 0; i < count; i++)
            {
                int sIdx = unitToSlot[i];
                if (sIdx < 0) sIdx = i;

                // Units currently on the rampart descend via the layered move
                // (route to an access point, climb down, then move on the
                // ground); ground units take the normal move.
                bool onRampart = _em.HasComponent<NavLayerIndex>(units[i])
                    && _em.GetComponentData<NavLayerIndex>(units[i]).Layer == NavLayerIndex.LayerRampart;
                if (onRampart)
                {
                    CommandRouter.IssueLayeredMove(_em, units[i], slots[sIdx],
                        0, CommandSource.LocalPlayer);
                    continue;
                }

                CommandRouter.IssueMove(_em, units[i], slots[sIdx], CommandSource.LocalPlayer);

                if (_em.HasComponent<FormationSpeedOverride>(units[i]))
                    _em.SetComponentData(units[i], new FormationSpeedOverride { Value = slowestSpeed });
                else
                    _em.AddComponentData(units[i], new FormationSpeedOverride { Value = slowestSpeed });
            }
        }

        /// <summary>
        /// Classify a unit into a formation role:
        ///   0 = Front (melee — front-row interior)
        ///   1 = Wing  (cavalry — front-row edges)
        ///   2 = Back  (ranged / siege / support / magic — back rows)
        /// </summary>
        private int ClassifyUnitRole(Entity e)
        {
            if (_em.HasComponent<CavalryTag>(e)) return 1; // Wing
            if (_em.HasComponent<UnitTag>(e))
            {
                var c = _em.GetComponentData<UnitTag>(e).Class;
                if (c == UnitClass.Ranged || c == UnitClass.Siege
                    || c == UnitClass.Support || c == UnitClass.Magic)
                    return 2; // Back
                if (c == UnitClass.Melee) return 0; // Front
            }
            return 0; // default to Front
        }

        /// <summary>
        /// Shift+right-click: queue a move waypoint on each selected unit instead of replacing their current command.
        /// </summary>
        private void QueueWaypointForSelection(float3 clickWorld)
        {
            // Called only while Shift is held. Every waypoint is appended to
            // the command queue and the entity is marked CommandQueueFrozen,
            // so CommandQueueSystem will not pop the next command until Shift
            // is released (UnfreezeAllQueues clears the tag).
            var selection = SelectionSystem.CurrentSelection;
            foreach (var e in selection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                if (!_em.HasBuffer<QueuedCommand>(e))
                    _em.AddBuffer<QueuedCommand>(e);
                _em.GetBuffer<QueuedCommand>(e).Add(new QueuedCommand
                {
                    Type = QueuedCommandType.Move,
                    TargetPosition = clickWorld,
                    TargetEntity = Entity.Null
                });

                if (!_em.HasComponent<CommandQueueActive>(e))
                    _em.AddComponent<CommandQueueActive>(e);
                if (!_em.HasComponent<CommandQueueFrozen>(e))
                    _em.AddComponent<CommandQueueFrozen>(e);
            }
        }

        // Strips CommandQueueFrozen from every entity that carries it. Called
        // on the frame Shift transitions from held → released, so any queues
        // built up during the hold start draining on the next CommandQueueSystem tick.
        private void UnfreezeAllQueues()
        {
            var query = _em.CreateEntityQuery(typeof(CommandQueueFrozen));
            if (!query.IsEmpty)
                _em.RemoveComponent<CommandQueueFrozen>(query);
            query.Dispose();
        }

        private void IssueAttackMoveFormation(float3 clickWorld)
        {
            var selection = SelectionSystem.CurrentSelection;

            // Collect movable units with their positions and speeds (only owned units)
            var units = new List<Entity>();
            var positions = new List<float3>();
            var speeds = new List<float>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e))
                    continue;
                if (!IsOwnedByLocalPlayer(e))
                    continue;

                units.Add(e);
                positions.Add(_em.HasComponent<LocalTransform>(e)
                    ? _em.GetComponentData<LocalTransform>(e).Position
                    : float3.zero);
                speeds.Add(_em.HasComponent<MoveSpeed>(e)
                    ? _em.GetComponentData<MoveSpeed>(e).Value
                    : 3.5f);
            }

            int count = units.Count;
            if (count == 0) return;

            // Calculate formation grid
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt((float)count / cols);

            // Get camera-relative directions
            var cam = Camera.main;
            Vector3 camForward = cam
                ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, camForward).normalized;

            float3 forward = new float3(camForward.x, camForward.y, camForward.z);
            float3 rightF3 = new float3(right.x, right.y, right.z);

            // Top-left of formation
            float3 topLeft = clickWorld
                - rightF3 * ((cols - 1) * formationSpacing * 0.5f)
                + forward * ((rows - 1) * formationSpacing * 0.5f);

            // Calculate slots and find slowest speed / max distance
            var slots = new float3[count];
            var dists = new float[count];
            float slowestSpeed = float.MaxValue;
            float maxDist = 0f;

            for (int i = 0; i < count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                slots[i] = topLeft + rightF3 * (col * formationSpacing) - forward * (row * formationSpacing);

                float3 to = slots[i] - positions[i];
                to.y = 0;
                dists[i] = math.length(to);

                if (speeds[i] > 0 && speeds[i] < slowestSpeed)
                    slowestSpeed = speeds[i];
                if (dists[i] > maxDist)
                    maxDist = dists[i];
            }

            if (slowestSpeed <= 0f || slowestSpeed == float.MaxValue)
                slowestSpeed = 3.5f;

            // Arrival time = how long the slowest unit takes to cover the max distance
            float arrivalTime = maxDist / slowestSpeed;

            // Issue attack-move with formation speed overrides for synchronized arrival
            for (int i = 0; i < count; i++)
            {
                CommandRouter.IssueAttackMove(_em, units[i], slots[i], CommandSource.LocalPlayer);

                // All units move at slowest speed (BFME2 group move)
                if (_em.HasComponent<FormationSpeedOverride>(units[i]))
                    _em.SetComponentData(units[i], new FormationSpeedOverride { Value = slowestSpeed });
                else
                    _em.AddComponentData(units[i], new FormationSpeedOverride { Value = slowestSpeed });
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TARGET TYPE DETECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private enum TargetType { Ground, Enemy, FriendlyUnit, FriendlyBuilding, Resource }

        private TargetType DetermineTargetType(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target))
                return TargetType.Ground;

            // Check if it's a resource node (iron mine or crystal node)
            if (_em.HasComponent<IronMineTag>(target))
                return TargetType.Resource;
            if (_em.HasComponent<CadaverTag>(target))
                return TargetType.Resource;

            // Check faction
            if (!_em.HasComponent<FactionTag>(target))
                return TargetType.Ground;

            var targetFaction = _em.GetComponentData<FactionTag>(target).Value;

            if (targetFaction != GameSettings.LocalPlayerFaction)
                return TargetType.Enemy;

            if (_em.HasComponent<UnitTag>(target))
                return TargetType.FriendlyUnit;

            if (_em.HasComponent<BuildingTag>(target))
                return TargetType.FriendlyBuilding;

            return TargetType.Ground;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // BUILDER CYCLE
        // ═══════════════════════════════════════════════════════════════════════

        // Last-cycled builder, so subsequent presses advance through the list
        // instead of re-selecting the same unit.
        private static int _builderCycleIndex = -1;

        /// <summary>
        /// Selects the next idle builder of the local player faction (and
        /// centers the camera on it). An "idle" builder is one with the
        /// CanBuild component and no active BuildOrder or RepairOrder.
        /// Press repeatedly to cycle. (Spec: 'b' cycles through idle builders.)
        /// </summary>
        private void CycleIdleBuilders()
        {
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            var idle = new List<Entity>();
            foreach (var e in entities)
            {
                if (_em.GetComponentData<FactionTag>(e).Value != GameSettings.LocalPlayerFaction)
                    continue;
                if (_em.HasComponent<BuildOrder>(e)) continue;
                if (_em.HasComponent<RepairOrder>(e)) continue;
                idle.Add(e);
            }

            if (idle.Count == 0) return;

            _builderCycleIndex = (_builderCycleIndex + 1) % idle.Count;
            var pick = idle[_builderCycleIndex];

            SelectionSystem.ClearSelection();
            SelectionSystem.AddToSelection(pick);

            var pos = _em.GetComponentData<LocalTransform>(pick).Position;
            GameCamera.FocusOn(new Vector3(pos.x, pos.y, pos.z));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CAPABILITY DETECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private struct UnitCapabilities
        {
            public bool CanAttack;
            public bool CanGather;
            public bool CanHeal;
            public bool CanBuildRepair;
            public bool CanPurify;
            public bool CanConvertNode;
        }

        private UnitCapabilities DetermineCapabilities()
        {
            var caps = new UnitCapabilities();

            foreach (var e in SelectionSystem.CurrentSelection)
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

                // Scholar can channel Purification on Active crystal main nodes.
                if (_em.HasComponent<ScholarTag>(e))
                    caps.CanPurify = true;

                // Acolyte can channel Conversion on Active crystal main nodes.
                if (_em.HasComponent<AcolyteTag>(e))
                    caps.CanConvertNode = true;
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
        private bool IsOwnedByLocalPlayer(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e)) return false;
            return _em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;
        }

        /// <summary>
        /// Returns true if at least one selected entity belongs to the local player.
        /// </summary>
        private bool HasAnyOwnedEntity()
        {
            foreach (var e in SelectionSystem.CurrentSelection)
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
        private bool HasOnlyOwnedBuildings()
        {
            bool foundOwned = false;
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                foundOwned = true;
                if (!_em.HasComponent<BuildingTag>(e))
                    return false;
            }
            return foundOwned;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // UTILITY METHODS
        // ═══════════════════════════════════════════════════════════════════════
        
        private Entity FindNearestGatherersHut()
        {
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            // Get average position of selected miners
            float3 avgPos = float3.zero;
            int count = 0;
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (_em.Exists(e) && _em.HasComponent<LocalTransform>(e))
                {
                    avgPos += _em.GetComponentData<LocalTransform>(e).Position;
                    count++;
                }
            }
            if (count > 0) avgPos /= count;

            // Find nearest gatherer's hut
            var query = _em.CreateEntityQuery(typeof(GathererHutTag), typeof(LocalTransform), typeof(FactionTag));
            var ents = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!_em.Exists(e)) continue;
                if (_em.GetComponentData<FactionTag>(e).Value != GameSettings.LocalPlayerFaction) continue;

                var pos = _em.GetComponentData<LocalTransform>(e).Position;
                float dist = math.distance(avgPos, pos);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = e;
                }
            }

            ents.Dispose();
            return nearest;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RAYCASTING
        // ═══════════════════════════════════════════════════════════════════════
        
        private Entity RaycastPickEntity()
        {
            var cam = Camera.main;
            if (!cam) return Entity.Null;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, clickMask))
            {
                // Walk up the hierarchy to find EntityReference (buildings/units
                // may have colliders on deeply nested children)
                var current = hit.collider.transform;
                while (current != null)
                {
                    var link = current.GetComponent<EntityReference>();
                    if (link != null && _em.Exists(link.Entity))
                        return link.Entity;
                    current = current.parent;
                }
            }
            return Entity.Null;
        }

        private bool TryGetClickPoint(out float3 point)
        {
            point = float3.zero;
            var cam = Camera.main;
            if (!cam) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, clickMask))
            {
                point = hit.point;
                return true;
            }
            return false;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // DEBUG GUI
        // ═══════════════════════════════════════════════════════════════════════

        void OnGUI()
        {
            // Mode indicators as centered banner at top of screen
            if (_attackMoveMode || _patrolMode)
            {
                string modeText = _attackMoveMode ? "ATTACK-MOVE MODE" : "PATROL MODE";
                float bannerW = 250f;
                float bannerH = 30f;
                float bannerX = (Screen.width - bannerW) * 0.5f;
                float bannerY = 50f;

                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(new Rect(bannerX, bannerY, bannerW, bannerH), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.85f, 0.3f);
                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 14
                };
                style.normal.textColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(bannerX, bannerY, bannerW, bannerH), modeText, style);
                GUI.color = Color.white;
            }
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // HELPER COMPONENT
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Links a GameObject to an ECS Entity.
    /// Attach to visual representations of entities.
    /// </summary>
    public class EntityReference : MonoBehaviour
    {
        public Entity Entity;
    }
}