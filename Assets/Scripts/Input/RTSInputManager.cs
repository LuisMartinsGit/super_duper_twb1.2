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

            // D - Set battalion stance to Aggressive (BFME2 layout)
            if (UnityEngine.Input.GetKeyDown(KeyCode.D))
            {
                IssueStanceToSelection(BattalionStance.Aggressive);
            }

            // F - Set battalion stance to Default / Standard (BFME2 layout)
            if (UnityEngine.Input.GetKeyDown(KeyCode.F))
            {
                IssueStanceToSelection(BattalionStance.Default);
            }

            // G - Set battalion stance to Defensive (BFME2 layout)
            if (UnityEngine.Input.GetKeyDown(KeyCode.G))
            {
                IssueStanceToSelection(BattalionStance.Defensive);
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
            // Drag-to-preview formation: when the user has held right-mouse and
            // dragged, FormationDragPreview takes over. Skip the instant move.
            if (TheWaningBorder.UI.HUD.FormationDragPreview.SuppressNextRightClick)
            {
                TheWaningBorder.UI.HUD.FormationDragPreview.SuppressNextRightClick = false;
                return;
            }

            // Fire on mouse-up so drag-preview can intercept hold gestures.
            // A short click+release acts like the previous instant right-click.
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
                    if (_em.HasComponent<BattalionMemberData>(e)) continue;
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
                SetRallyPoints(clickWorld);
                return;
            }

            var capabilities = DetermineCapabilities();

            switch (targetType)
            {
                case TargetType.Enemy:
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
                    // If miners are selected and click is near a deposit, gather instead of move
                    if (capabilities.CanGather)
                    {
                        Entity nearbyDeposit = FindNearestResourceNearClick(clickWorld);
                        if (nearbyDeposit != Entity.Null)
                        {
                            IssueGatherCommands(nearbyDeposit);
                            break;
                        }
                    }
                    IssueFormationMove(clickWorld);
                    break;
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // COMMAND ISSUANCE
        // ═══════════════════════════════════════════════════════════════════════
        
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

                Entity unit = ResolveBattalionLeader(e);
                if (unit == Entity.Null) continue;
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

                Entity unit = ResolveBattalionLeader(e);
                if (unit == Entity.Null) continue;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssueHoldPosition(_em, unit, CommandSource.LocalPlayer);
            }
        }

        private void IssueStanceToSelection(BattalionStance stance)
        {
            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            // Track leaders already processed to avoid duplicates
            var processed = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                // Resolve to battalion leader
                Entity leader = Entity.Null;
                if (_em.HasComponent<BattalionLeader>(e))
                {
                    leader = e;
                }
                else if (_em.HasComponent<BattalionMemberData>(e))
                {
                    leader = _em.GetComponentData<BattalionMemberData>(e).Leader;
                }

                if (leader == Entity.Null || !_em.Exists(leader)) continue;
                if (processed.Contains(leader)) continue;
                processed.Add(leader);

                CommandRouter.IssueStanceChange(_em, leader, stance, CommandSource.LocalPlayer);
            }
        }

        private void SetRallyPoints(float3 position)
        {
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<BuildingTag>(e)) continue;

                CommandRouter.SetRallyPoint(_em, e, position, CommandSource.LocalPlayer);

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

                Entity unit = ResolveBattalionLeader(e);
                if (unit == Entity.Null) continue;
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

                Entity unit = ResolveBattalionLeader(e);
                if (unit == Entity.Null) continue;
                if (!issued.Add(unit)) continue;

                CommandRouter.IssuePatrol(_em, unit, destination, CommandSource.LocalPlayer);
            }
        }

        private void IssueFormationMove(float3 clickWorld)
        {
            var selection = SelectionSystem.CurrentSelection;

            // Collect movable units with their positions and speeds (only owned units)
            var units = new List<Entity>();
            var positions = new List<float3>();
            var speeds = new List<float>();
            var addedLeaders = new HashSet<Entity>();

            foreach (var e in selection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e))
                    continue;
                if (!IsOwnedByLocalPlayer(e))
                    continue;

                // Resolve battalion members to their leader (dedup)
                Entity unit = ResolveBattalionLeader(e);
                if (unit == Entity.Null) continue;
                if (!addedLeaders.Add(unit)) continue;

                units.Add(unit);
                positions.Add(_em.HasComponent<LocalTransform>(unit)
                    ? _em.GetComponentData<LocalTransform>(unit).Position
                    : float3.zero);
                speeds.Add(_em.HasComponent<MoveSpeed>(unit)
                    ? _em.GetComponentData<MoveSpeed>(unit).Value
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
                // Click is on top of selection — keep current camera-forward fallback
                var cam0 = Camera.main;
                Vector3 cf = cam0
                    ? Vector3.ProjectOnPlane(cam0.transform.forward, Vector3.up).normalized
                    : Vector3.forward;
                moveDir = new float3(cf.x, 0f, cf.z);
            }
            moveDir = math.normalize(moveDir);

            // Right vector lies in the horizontal plane, perpendicular to moveDir
            float3 rightDir = math.cross(new float3(0f, 1f, 0f), moveDir);

            // ── Per-unit footprint (width × depth) ──
            float[] widths = new float[count];
            float[] depths = new float[count];
            float maxBattalionWidth = formationSpacing;
            float maxBattalionDepth = formationSpacing;
            for (int i = 0; i < count; i++)
            {
                if (_em.HasComponent<BattalionLeader>(units[i]))
                {
                    var bl = _em.GetComponentData<BattalionLeader>(units[i]);
                    widths[i] = bl.Columns * bl.Spacing + 1.5f;
                    depths[i] = bl.Rows * bl.Spacing + 1.5f;
                }
                else
                {
                    widths[i] = formationSpacing;
                    depths[i] = formationSpacing;
                }
                if (widths[i] > maxBattalionWidth) maxBattalionWidth = widths[i];
                if (depths[i] > maxBattalionDepth) maxBattalionDepth = depths[i];
            }

            // ── Square-ish grid layout ──
            // cols = ceil(sqrt(N)), rows = ceil(N/cols). Front rows fill first
            // so the back row may be partial and gets centered.
            //   N=2 → 2×1 (front=2)
            //   N=3 → 2×2 (front=2, back=1 centered)
            //   N=5 → 3×2 (front=3, back=2)
            //   N=6 → 3×2 (front=3, back=3)
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));
            int[] rowCount = new int[rows];
            int remainingForLayout = count;
            for (int r = 0; r < rows; r++)
            {
                rowCount[r] = Mathf.Min(cols, remainingForLayout);
                remainingForLayout -= rowCount[r];
            }

            // Build slot world positions. Slot index = sequential (front-to-back, left-to-right).
            var slots = new float3[count];
            int[] slotRow = new int[count];
            int[] slotCol = new int[count];   // col within its row
            int[] slotsInRow = new int[count]; // total cols in this slot's row
            int slotIdx = 0;
            for (int r = 0; r < rows; r++)
            {
                int rc = rowCount[r];
                float rowWidth = rc * maxBattalionWidth;
                float startOffset = -rowWidth * 0.5f + maxBattalionWidth * 0.5f;
                for (int c = 0; c < rc; c++)
                {
                    float lateralOffset = startOffset + c * maxBattalionWidth;
                    slots[slotIdx] = clickWorld
                                   + rightDir * lateralOffset
                                   - moveDir * (r * maxBattalionDepth);
                    slotRow[slotIdx] = r;
                    slotCol[slotIdx] = c;
                    slotsInRow[slotIdx] = rc;
                    slotIdx++;
                }
            }

            // ── Categorize slots and battalions by tactical role ──
            //   Back   = back rows (ranged / siege / support / magic)
            //   Wing   = leftmost / rightmost slot of the front row (cavalry)
            //   Front  = front-row interior (melee)
            // Role values are deliberately gapped so role-mismatch dominates distance.
            const float ROLE_PENALTY = 1_000_000f;
            int[] slotRole = new int[count];
            for (int s = 0; s < count; s++)
            {
                if (slotRow[s] > 0) slotRole[s] = 2;                            // Back
                else if (slotsInRow[s] > 1
                         && (slotCol[s] == 0 || slotCol[s] == slotsInRow[s] - 1))
                    slotRole[s] = 1;                                            // Wing
                else slotRole[s] = 0;                                           // Front
            }

            int[] leaderRole = new int[count];
            for (int l = 0; l < count; l++)
                leaderRole[l] = ClassifyLeaderRole(units[l]);

            // ── Greedy assignment with role-mismatch penalty ──
            // Distance is the tiebreaker WITHIN a role; role match wins overall.
            int[] leaderToSlot = new int[count];
            bool[] slotUsed = new bool[count];
            for (int i = 0; i < count; i++) leaderToSlot[i] = -1;

            var pairs = new List<(int leader, int slot, float cost)>(count * count);
            for (int l = 0; l < count; l++)
            for (int s = 0; s < count; s++)
            {
                float3 d = slots[s] - positions[l];
                d.y = 0f;
                float cost = math.lengthsq(d);
                if (slotRole[s] != leaderRole[l]) cost += ROLE_PENALTY;
                pairs.Add((l, s, cost));
            }
            pairs.Sort((a, b) => a.cost.CompareTo(b.cost));

            int assigned = 0;
            for (int p = 0; p < pairs.Count && assigned < count; p++)
            {
                var pair = pairs[p];
                if (leaderToSlot[pair.leader] != -1) continue;
                if (slotUsed[pair.slot]) continue;
                leaderToSlot[pair.leader] = pair.slot;
                slotUsed[pair.slot] = true;
                assigned++;
            }

            // Compute distances + slowest speed using the assigned mapping
            float slowestSpeed = float.MaxValue;
            float maxDist = 0f;
            for (int i = 0; i < count; i++)
            {
                int sIdx = leaderToSlot[i];
                if (sIdx < 0) sIdx = i; // fallback (shouldn't happen)
                float3 to = slots[sIdx] - positions[i];
                to.y = 0f;
                float d = math.length(to);
                if (speeds[i] > 0 && speeds[i] < slowestSpeed) slowestSpeed = speeds[i];
                if (d > maxDist) maxDist = d;
            }

            if (slowestSpeed <= 0f || slowestSpeed == float.MaxValue)
                slowestSpeed = 3.5f;

            // Each battalion should face the original move direction, not its
            // own slot's direction (so all formations face the same way after
            // arrival, side-by-side in a tidy line).
            quaternion sharedFacing = quaternion.LookRotationSafe(moveDir, new float3(0f, 1f, 0f));

            // Issue moves — all units move at the slowest speed (BFME2 group move)
            for (int i = 0; i < count; i++)
            {
                int sIdx = leaderToSlot[i];
                if (sIdx < 0) sIdx = i;
                CommandRouter.IssueMove(_em, units[i], slots[sIdx], CommandSource.LocalPlayer);

                // Override DestinationRot so battalions face the move direction,
                // not the angle from their start to their slot. MoveCommandHelper
                // sets DestinationRot from delta; we replace it with the shared
                // facing for a clean side-by-side line.
                if (_em.HasComponent<BattalionLeader>(units[i]))
                {
                    var bl = _em.GetComponentData<BattalionLeader>(units[i]);
                    bl.DestinationRot = sharedFacing;
                    bl.HasDestinationRot = 1;
                    bl.NeedsReassignment = 1;
                    _em.SetComponentData(units[i], bl);
                }

                if (_em.HasComponent<FormationSpeedOverride>(units[i]))
                    _em.SetComponentData(units[i], new FormationSpeedOverride { Value = slowestSpeed });
                else
                    _em.AddComponentData(units[i], new FormationSpeedOverride { Value = slowestSpeed });
            }
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
                if (_em.HasComponent<BattalionMemberData>(e)) continue;

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
                // Battalion members follow formation automatically; only route to leader
                if (_em.HasComponent<BattalionMemberData>(e))
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
        // CAPABILITY DETECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private struct UnitCapabilities
        {
            public bool CanAttack;
            public bool CanGather;
            public bool CanHeal;
            public bool CanBuildRepair;
        }

        private UnitCapabilities DetermineCapabilities()
        {
            var caps = new UnitCapabilities();

            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;

                // Can attack if has Damage component or is a battalion leader
                if (_em.HasComponent<Damage>(e) || _em.HasComponent<BattalionLeader>(e))
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
        /// Classify a leader (or individual unit) into a formation role:
        ///   0 = Front  (melee — front-row interior)
        ///   1 = Wing   (cavalry — front-row edges)
        ///   2 = Back   (ranged / siege / support / magic — back rows)
        /// Looks at the first alive battalion member or the unit itself.
        /// </summary>
        private int ClassifyLeaderRole(Entity unit)
        {
            // Helper to read a single entity's role
            int FromEntity(Entity e)
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

            // Battalion leader: classify by first alive member
            if (_em.HasComponent<BattalionLeader>(unit) && _em.HasBuffer<BattalionMember>(unit))
            {
                var members = _em.GetBuffer<BattalionMember>(unit);
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i].Value;
                    if (m == Entity.Null || !_em.Exists(m)) continue;
                    if (_em.HasComponent<Health>(m)
                        && _em.GetComponentData<Health>(m).Value <= 0) continue;
                    return FromEntity(m);
                }
            }
            return FromEntity(unit);
        }

        /// <summary>
        /// If entity is a battalion member, resolve to its leader.
        /// Returns the entity unchanged if it's not a member.
        /// Returns Entity.Null if the leader doesn't exist.
        /// </summary>
        private Entity ResolveBattalionLeader(Entity e)
        {
            if (!_em.HasComponent<BattalionMemberData>(e)) return e;
            var memberData = _em.GetComponentData<BattalionMemberData>(e);
            if (memberData.Leader == Entity.Null || !_em.Exists(memberData.Leader))
                return Entity.Null;
            return memberData.Leader;
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

        /// <summary>
        /// Searches for the nearest non-depleted resource deposit (iron mine or cadaver)
        /// near the click position. Uses the first selected miner's LineOfSight radius
        /// as the search range, or 30 units as a fallback.
        /// </summary>
        private Entity FindNearestResourceNearClick(float3 clickPos)
        {
            // Determine search radius from the first selected miner's LOS
            float searchRadius = 30f;
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (!_em.HasComponent<MinerTag>(e)) continue;

                if (_em.HasComponent<LineOfSight>(e))
                    searchRadius = _em.GetComponentData<LineOfSight>(e).Radius;
                break;
            }

            return GatherCommandHelper.FindNearestDepositNearPosition(_em, clickPos, searchRadius);
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