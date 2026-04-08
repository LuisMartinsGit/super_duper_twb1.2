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
            if (FindObjectOfType<ControlGroupSystem>() == null)
                gameObject.AddComponent<ControlGroupSystem>();
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;
            
            // Refresh EntityManager if needed
            if (_em.Equals(default(EntityManager)))
                _em = _world.EntityManager;

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

            // Sync to RTSInput static accessor so SelectionRings can read it
            RTSInput.SetHovered(CurrentHover);
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // RIGHT-CLICK COMMAND HANDLING
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleRightClick()
        {
            if (!UnityEngine.Input.GetMouseButtonDown(1)) return;

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

            foreach (var e in selection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;
                if (_em.HasComponent<BattalionMemberData>(e)) continue; // Commands go to leader only

                CommandRouter.IssueStop(_em, e, CommandSource.LocalPlayer);
            }
        }

        private void IssueHoldPositionToSelection()
        {
            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            foreach (var e in selection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;
                if (_em.HasComponent<BattalionMemberData>(e)) continue; // Commands go to leader only

                CommandRouter.IssueHoldPosition(_em, e, CommandSource.LocalPlayer);
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
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue; // Buildings can't attack-move
                if (_em.HasComponent<BattalionMemberData>(e)) continue; // Commands go to leader only

                CommandRouter.IssueAttack(_em, e, target, CommandSource.LocalPlayer);
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
            foreach (var e in SelectionSystem.CurrentSelection)
            {
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BuildingTag>(e)) continue;
                if (_em.HasComponent<BattalionMemberData>(e)) continue; // Commands go to leader only

                CommandRouter.IssuePatrol(_em, e, destination, CommandSource.LocalPlayer);
            }
        }

        private void IssueFormationMove(float3 clickWorld)
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

            // Issue moves — all units move at the slowest speed (BFME2 group move)
            for (int i = 0; i < count; i++)
            {
                CommandRouter.IssueMove(_em, units[i], slots[i], CommandSource.LocalPlayer);

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
            var selection = SelectionSystem.CurrentSelection;
            foreach (var e in selection)
            {
                if (!_em.Exists(e) || _em.HasComponent<BuildingTag>(e)) continue;
                if (!IsOwnedByLocalPlayer(e)) continue;
                if (_em.HasComponent<BattalionMemberData>(e)) continue;

                // If unit has no current destination, issue move directly
                bool hasDest = _em.HasComponent<DesiredDestination>(e)
                    && _em.GetComponentData<DesiredDestination>(e).Has != 0;
                bool hasQueue = _em.HasComponent<CommandQueueActive>(e);

                if (!hasDest && !hasQueue)
                {
                    CommandRouter.IssueMove(_em, e, clickWorld, CommandSource.LocalPlayer);
                    continue;
                }

                // Append to command queue
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
            }
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