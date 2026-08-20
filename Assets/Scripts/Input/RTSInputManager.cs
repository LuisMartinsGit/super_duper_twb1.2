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

        // Cached query — CreateEntityQuery per click leaks into the world's
        // query registry.
        private static readonly Unity.Entities.ComponentType[] VeilFieldQueryTypes =
            { Unity.Entities.ComponentType.ReadOnly<VeilField>() };
        private TheWaningBorder.Core.CachedEntityQuery _veilFieldQuery;
        // Cached — CycleIdleBuilders created an undisposed query on every
        // 'b' press (UnfreezeAllQueues disposes its own, so it is fine).
        private static readonly Unity.Entities.ComponentType[] IdleBuilderQueryTypes =
        {
            Unity.Entities.ComponentType.ReadOnly<UnitTag>(),
            Unity.Entities.ComponentType.ReadOnly<CanBuild>(),
            Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
            Unity.Entities.ComponentType.ReadOnly<LocalTransform>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _idleBuilderQuery;

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

        /// <summary>
        /// Active formation shape for group orders (AoE4 set: Box / Line /
        /// Wedge / Staggered). Persists across orders; X cycles it.
        /// </summary>
        public static FormationShape CurrentFormationShape { get; private set; } = FormationShape.Box;

        // Set by the formations UI; consumed in Update so the re-slot runs
        // on the manager instance (mirrors the X-key cycle).
        private static FormationShape _requestedShape;
        private static bool _shapeRequested;

        /// <summary>UI entry point: set the formation shape (and re-slot the
        /// current selection) on the next input tick.</summary>
        public static void RequestFormationShape(FormationShape shape)
        {
            _requestedShape = shape;
            _shapeRequested = true;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>Set in Awake so the pause menu can drive the Esc cascade
        /// (see <see cref="CancelModesOrSelection"/>).</summary>
        private static RTSInputManager _instance;

        void Awake()
        {
            _instance = this;
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;

            // Ensure ControlGroupSystem exists
            if (FindFirstObjectByType<ControlGroupSystem>() == null)
                gameObject.AddComponent<ControlGroupSystem>();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// One step of the Esc cascade: cancel attack-move / patrol aiming,
        /// else drop the selection. Returns true when something was cancelled.
        ///
        /// Esc used to be handled here directly, but this manager stops
        /// processing hotkeys the moment the pointer is over any uGUI element
        /// (ShouldBlockInput) — which is most of the screen once the HUD is
        /// up, and ALL of it once the pause menu is open. PauseMenuPanel owns
        /// the key now and calls down into this; there is exactly one Esc
        /// cascade and it works wherever the cursor happens to be.
        /// </summary>
        public static bool CancelModesOrSelection()
        {
            var self = _instance;
            if (self != null && (self._attackMoveMode || self._patrolMode))
            {
                self._attackMoveMode = false;
                self._patrolMode = false;
                return true;
            }
            if (SelectionSystem.CurrentSelection != null
                && SelectionSystem.CurrentSelection.Count > 0)
            {
                SelectionSystem.ClearSelection();
                return true;
            }
            return false;
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

            // InGameMenuPanel ESC handling removed with the old UI (2026-07-17);
            // the final uGUI owns the pause menu.

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
            // Pointer over a uGUI element (the minimap RawImage is the main
            // one in this stack): the element handles its own clicks — a
            // right-click on the minimap must issue the MINIMAP move order,
            // not ALSO raycast the world behind the HUD and issue a second
            // conflicting command.
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return true;

            // One-frame suppression (after GUI button clicks)
            if (BuilderCommandPanel.SuppressClicksThisFrame)
            {
                BuilderCommandPanel.SuppressClicksThisFrame = false;
                return true;
            }

            // Old IMGUI panel guards (EntityActionPanel / EntityInfoPanel /
            // SpellPanel / CultureChoicePopup) removed with the old UI
            // (2026-07-17); the EventSystem check above covers the final uGUI.

            // Block while aiming an ability with the ground-target ring
            // (sect powers / Reliquary abilities) — GroundTargeting owns the
            // mouse until cast or cancel.
            if (TheWaningBorder.UI.HUD.GroundTargeting.IsActive)
                return true;

            // Block during building placement
            if (BuilderCommandPanel.IsPlacingBuilding)
                return true;

            return false;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // HOTKEYS
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleHotkeys()
        {
            // ESC is owned by PauseMenuPanel, which runs the whole cascade
            // (placement / targeting / planning / culture menu / this
            // manager's modes and selection, then the pause menu) from a
            // component that is never gated by pointer-over-UI. See
            // CancelModesOrSelection above.

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

            // X - Cycle formation shape (Box → Line → Wedge → Staggered).
            // AoE4 semantics: changing formation re-slots the selection
            // immediately, even when standing still.
            if (UnityEngine.Input.GetKeyDown(KeyCode.X))
            {
                CurrentFormationShape = (FormationShape)(((byte)CurrentFormationShape + 1) % 4);
                Debug.Log($"[Formation] {CurrentFormationShape}");
                ReSlotSelectionInPlace();
            }

            // UI-driven formation change (FormationsPanelBinder) — applied
            // here so the re-slot runs with this instance's state, exactly
            // like the X-key path.
            if (_shapeRequested)
            {
                _shapeRequested = false;
                if (CurrentFormationShape != _requestedShape)
                {
                    CurrentFormationShape = _requestedShape;
                    ReSlotSelectionInPlace();
                }
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

            // ── Right-click on THE VEIL: selected miners dig the crust at
            //    the closest crusted vertex (Astroneer-style — the sheet
            //    itself is the deposit). DISABLED while the veil is
            //    influence-only (VeilCrustConstants.CrustPhysical false):
            //    veilstone comes from discrete deposits, and routing miners
            //    into the reforming crust stranded and killed them. ──
            if (TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                && !_attackMoveMode && !_patrolMode && TryGetVeilClickVertex(out float3 veilVertex))
            {
                if (IssueGatherVeilCommands(veilVertex))
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
            // veilstone / iron deposit and walk away.
            if (targetType == TargetType.Resource && HasOnlyOwnedBuildings())
            {
                SetRallyPoints(clickWorld, target);
                return;
            }

            var capabilities = DetermineCapabilities();

            switch (targetType)
            {
                case TargetType.Enemy:
                    // Scholar + Active veilstone main node → Purify ritual.
                    // Falls through to Attack if the scholar isn't selected
                    // or the node is no longer Active (Cleansed/Converted/
                    // Destroyed nodes don't accept purification).
                    if (capabilities.CanPurify && IsActiveBorderMainNode(target))
                    {
                        IssuePurifyCommands(target);
                        break;
                    }
                    // Corruptor + living veilstone main node → Corruption.
                    // Feraldis cracks a well open rather than claiming it.
                    if (capabilities.CanCorrupt && IsActiveBorderMainNode(target))
                    {
                        IssueCorruptCommands(target);
                        break;
                    }
                    // Acolyte + Active veilstone main node → Conversion ritual.
                    if (capabilities.CanConvertNode && IsActiveBorderMainNode(target))
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
            // Overpass-bridge decks are roads, not fortifications: ANY
            // movable unit (cavalry, siege, workers included) may cross.
            // Wall ramparts keep the AoE4 foot-unit garrison rule.
            bool isOverpassDeck = TheWaningBorder.World.Terrain.BridgeSurface
                .TryGetDeckHeight(wallTopPoint.x, wallTopPoint.z, out _);

            var units = new List<Entity>();
            foreach (var e in SelectionSystem.CurrentSelection)
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
        /// True when the right-click target is a veilstone main node currently
        /// in the Active state — the only state that accepts Purification.
        /// </summary>
        private bool IsActiveBorderMainNode(Entity target)
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
        /// Issue IssueCorrupt on every Corruptor in the current selection.
        /// Same one-target semantics as IssuePurifyCommands.
        /// </summary>
        private void IssueCorruptCommands(Entity node)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
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
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;

                CommandRouter.IssueGather(_em, e, resourceNode, CommandSource.LocalPlayer);
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

        // AoE4-style formation move: layout, rank layering, cohesion gate,
        // group speed and the persistent virtual-leader group all live in
        // FormationMoveCommandHelper / FormationGroupSystem — the input
        // layer only collects the selection and picks the formation shape.
        private void IssueFormationMove(float3 clickWorld)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationMove(_em, units, clickWorld,
                CurrentFormationShape, CommandSource.LocalPlayer);
        }

        /// <summary>
        /// AoE4: selecting a new formation rearranges the units immediately,
        /// even when standing still — re-issue a formation move to the
        /// selection's own centroid.
        /// </summary>
        private void ReSlotSelectionInPlace()
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
                CurrentFormationShape, CommandSource.LocalPlayer);
        }

        /// <summary>Deduplicated owned, movable (non-building) selection.</summary>
        private List<Entity> CollectOwnedMovableSelection()
        {
            var units = new List<Entity>();
            var added = new HashSet<Entity>();
            foreach (var e in SelectionSystem.CurrentSelection)
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
        private void UnfreezeAllQueues()
        {
            var query = _em.CreateEntityQuery(typeof(CommandQueueFrozen));
            if (!query.IsEmpty)
                _em.RemoveComponent<CommandQueueFrozen>(query);
            query.Dispose();
        }

        // Formation attack-move: same layout/travel machinery as the plain
        // formation move; members auto-engage en route (AoE4 behavior) and
        // detach from the group the moment they acquire a target.
        private void IssueAttackMoveFormation(float3 clickWorld)
        {
            var units = CollectOwnedMovableSelection();
            if (units.Count == 0) return;
            CommandRouter.IssueFormationAttackMove(_em, units, clickWorld,
                CurrentFormationShape, CommandSource.LocalPlayer);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TARGET TYPE DETECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private enum TargetType { Ground, Enemy, FriendlyUnit, FriendlyBuilding, Resource }

        private TargetType DetermineTargetType(Entity target)
        {
            if (target == Entity.Null || !_em.Exists(target))
                return TargetType.Ground;

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
            var query = _idleBuilderQuery.Get(_em, IdleBuilderQueryTypes);
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
            /// <summary>Feraldis Corruptor selected — right-click a well to crack it open.</summary>
            public bool CanCorrupt;
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
        
        // ═══════════════════════════════════════════════════════════════════════
        // RAYCASTING
        // ═══════════════════════════════════════════════════════════════════════
        
        private Entity RaycastPickEntity()
        {
            var cam = Camera.main;
            if (!cam) return Entity.Null;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            // RaycastAll + nearest-valid (mirrors SelectionSystem): stray
            // colliders that don't resolve to an entity must not swallow
            // right-click targets behind them.
            var hits = Physics.RaycastAll(ray, 1000f, clickMask);
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].distance >= bestDist) continue;
                var current = hits[i].collider.transform;
                while (current != null)
                {
                    var link = current.GetComponent<EntityReference>();
                    if (link != null && _em.Exists(link.Entity))
                    {
                        best = link.Entity;
                        bestDist = hits[i].distance;
                        break;
                    }
                    current = current.parent;
                }
            }
            return best;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // VEIL DIGGING (mine the curse sheet directly)
        // ═══════════════════════════════════════════════════════════════════════

        // Snap radius from the clicked point on the crust mesh to the
        // nearest diggable (crusted) vertex of the VeilField grid.
        private const float VeilVertexSnapRadius = 10f;

        /// <summary>
        /// True when the cursor points at the Veil's continuous crust mesh
        /// and no entity collider sits closer to the camera (units standing
        /// on crust must stay clickable). Outputs the closest crusted
        /// VeilField vertex to the hit — the spot the miner will pick at.
        /// </summary>
        private bool TryGetVeilClickVertex(out float3 vertex)
        {
            vertex = float3.zero;
            var cam = Camera.main;
            if (!cam) return false;

            // Need the field to test whether the clicked ground is crusted.
            var fieldQuery = _veilFieldQuery.Get(_em, VeilFieldQueryTypes);
            if (fieldQuery.IsEmpty) return false;
            var field = fieldQuery.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 1000f, clickMask);

            float veilDist = float.MaxValue;
            float entityDist = float.MaxValue;
            Vector3 veilPoint = default;
            for (int i = 0; i < hits.Length; i++)
            {
                // Does this collider resolve to an entity (unit/building)? Those
                // outrank the crust — clicking a unit standing on crust targets it.
                bool isEntity = false;
                var current = hits[i].collider.transform;
                while (current != null)
                {
                    var link = current.GetComponent<EntityReference>();
                    if (link != null && _em.Exists(link.Entity))
                    {
                        entityDist = math.min(entityDist, hits[i].distance);
                        isEntity = true;
                        break;
                    }
                    current = current.parent;
                }
                if (isEntity) continue;

                // Non-entity hit (terrain / ground mesh): it's a Veil click when
                // the ground under the cursor is crusted. The crystals are now
                // colliderless GPU instances, so the ray falls through to the
                // ground and we read the field there.
                if (field.SaturationAt(hits[i].point) >= VeilField.CrustThreshold
                    && hits[i].distance < veilDist)
                {
                    veilDist = hits[i].distance;
                    veilPoint = hits[i].point;
                }
            }
            if (veilDist == float.MaxValue || entityDist < veilDist) return false;

            // Snap to the closest crusted vertex of the field grid.
            return TheWaningBorder.Core.Commands.Types.VeilMiningUtil
                .TryFindCrustVertex(in field, (float3)veilPoint, VeilVertexSnapRadius, out vertex);
        }

        /// <summary>
        /// Send every selected owned miner to dig the Veil at the clicked
        /// vertex. Returns false if the selection holds no miners (the
        /// click then falls through to a plain move).
        /// </summary>
        private bool IssueGatherVeilCommands(float3 vertex)
        {
            bool any = false;
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;

                CommandRouter.IssueGatherVeil(_em, e, vertex, CommandSource.LocalPlayer);
                any = true;
            }
            return any;
        }

        private bool TryGetClickPoint(out float3 point)
        {
            point = float3.zero;
            var cam = Camera.main;
            if (!cam) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, clickMask))
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

            int2 cell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(point);
            bool impassable = cell.x != int.MinValue
                && !TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell);

            if (!underwater && !impassable)
                return point;

            TheWaningBorder.Systems.Navigation.NavGridQuery.SnapToWalkable(
                point, out float3 snapped, out bool ok);
            if (!ok)
                return point; // no walkable cell within snap radius — leave as-is

            snapped.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(snapped.x, snapped.z);
            return snapped;
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