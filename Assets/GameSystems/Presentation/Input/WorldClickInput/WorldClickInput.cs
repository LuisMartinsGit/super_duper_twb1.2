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
using TheWaningBorder.Core.Localization;
using TheWaningBorder.UI.Ingame;
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

            // Right-DRAG (2026-10-15): a press on open ground arms a facing
            // drag; past the pixel threshold it shows the ghost formation.
            if (UnityEngine.Input.GetMouseButtonDown(1)) BeginPress();
            if (_press.Tracked && UnityEngine.Input.GetMouseButton(1)) UpdateDrag();

            // Right-click issues the move/attack on release.
            if (!UnityEngine.Input.GetMouseButtonUp(1)) return;

            // Read the gesture, then forget it — whatever happens below, the
            // press is over and the ghost goes away.
            bool dragged = _press.Tracked && _press.Dragging && _press.HasFacing;
            float3 dragCentre = _press.World;
            float3 dragFacing = _press.Facing;
            bool dragAttackMove = _press.AttackMove;
            ResetPress();

            if (Cfg == null) return;   // the error is already logged by Require

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            // Clean dead entities from selection
            SelectionSystem.CleanSelection();

            // Only issue commands if at least one selected entity belongs to the local player
            if (!_orders.HasAnyOwnedEntity())
                return;

            // ── The drag order: a formation move centred on the PRESS point,
            //    arriving facing along the drag. Only a press that would have
            //    been a plain ground move (or an armed attack-move) gets here —
            //    BeginPress decided that — so every context order is untouched.
            //    Shift at release means "queue", which carries no facing:
            //    fall through to the ordinary queued click. ──
            bool shiftNow = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (dragged && !shiftNow)
            {
                if (dragAttackMove && _modes.AttackMove)
                {
                    _modes.Disarm();
                    _orders.IssueAttackMoveFormation(dragCentre, dragFacing);
                }
                else
                {
                    _orders.IssueFormationMove(dragCentre, dragFacing);
                }
                return;
            }

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

            // ── Shift+Right-Click: QUEUE what is clicked (2026-09-29) — an
            //    enemy is a queued attack, a site a queued build / repair, a
            //    wounded ally a queued heal, ground a queued move; an armed
            //    attack-move or patrol queues as that. ──
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (shift)
            {
                var qTarget = ScreenPick.EntityUnderMouse(_clickMask, _em);
                var qType = _orders.DetermineTargetType(qTarget);
                var armed = _modes.AttackMove ? QueuedCommandType.AttackMove
                          : _modes.Patrol ? QueuedCommandType.Patrol
                          : QueuedCommandType.Move;
                _modes.Disarm();
                _orders.QueueOrderForSelection(clickWorld, qTarget, qType, armed);
                return;
            }

            // Determine target and issue appropriate command. Picked FIRST:
            // the rampart test below projects onto the deck plane, and every
            // faction's walls are walkable on that layer, so testing it first
            // swallowed right-clicks on enemy walls and on anything whose
            // screen ray crossed a wall deck on the way.
            var target = ScreenPick.EntityUnderMouse(_clickMask, _em);
            var targetType = _orders.DetermineTargetType(target);

            // ── Right-click on the WALL TOP (rampart): order selected units
            //    onto the wall. They route to the nearest access point, climb,
            //    and move freely on the rampart. Only when the cursor is on
            //    nothing or on a friendly wall, only onto an own/allied deck
            //    (or an overpass bridge), and only if someone took the order —
            //    otherwise the click is handled as usual below. ──
            if (!_modes.AnyArmed
                && RampartClickAllowedOn(target, targetType)
                && TryGetRampartClick(out float3 rampartPoint)
                && _orders.IsFriendlyRampartDeck(rampartPoint)
                && _orders.IssueWallTopMove(rampartPoint) > 0)
            {
                return;
            }

            // ── Right-click on THE VEIL: selected workers dig the crust at
            //    the closest crusted vertex (Astroneer-style — the sheet
            //    itself is the deposit). DISABLED while the veil is
            //    influence-only (VeilCrustConstants.CrustPhysical false):
            //    veilstone comes from discrete deposits, and routing workers
            //    into the reforming crust stranded and killed them. ──

            // Attack-move mode: A + right-click
            if (_modes.AttackMove)
            {
                _modes.Disarm();
                var amCaps = _orders.DetermineCapabilities();

                if (targetType == SelectionOrders.TargetType.Enemy
                    && (amCaps.CanAttack || amCaps.CanDirectBuildingFire))
                {
                    // Clicking enemy in attack-move mode issues normal attack
                    AttackEnemy(target, amCaps);
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
                    if (pCaps.CanAttack || pCaps.CanDirectBuildingFire)
                        AttackEnemy(target, pCaps);
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
            // trained workers auto-gather it on spawn (TrainingSystem reads
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
                    // Scholar + Active OR rubble (Destroyed) well → Purify
                    // ritual. Falls through to Attack if the scholar is not
                    // selected or another culture already claimed the well
                    // (Cleansed/Converted don't accept purification).
                    if (capabilities.CanPurify && _orders.IsPurifiableBorderMainNode(target))
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
                    if (capabilities.CanAttack || capabilities.CanDirectBuildingFire)
                        AttackEnemy(target, capabilities);
                    break;

                case SelectionOrders.TargetType.FriendlyUnit:
                    // Feraldis: infantry right-clicking a War Rig board it.
                    if (_orders.TryFeraldisOrder(target)) break;
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

                    // Feraldis: staff an extractor / the Forge, Melt Down at the
                    // Forge, sacrifice at the Blood Altar (Age_1_Feraldis.md).
                    if (_orders.TryFeraldisOrder(target)) break;

                    // AoE4: right-click your own wall -> foot units garrison it
                    // (route to stairs, climb, spread along the top). Segments
                    // are data-only; skip those and under-construction walls.
                    if (_em.HasComponent<WallTag>(target)
                        && !_em.HasComponent<WallSegmentTag>(target)
                        && !_em.HasComponent<UnderConstruction>(target)
                        && _em.HasComponent<LocalTransform>(target))
                    {
                        var wp = _em.GetComponentData<LocalTransform>(target).Position;
                        if (_orders.IssueWallTopMove(new float3(wp.x, Nav.LayerTransitionSystem.DeckY, wp.z)) > 0)
                            break;
                        // Nobody here may stand on a wall (workers, cavalry,
                        // siege): fall through so they can still repair it.
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

        /// <summary>
        /// Right-click on an enemy: units attack it, and owned shooting
        /// buildings in the selection take it as their forced target
        /// (docs/Design/Combat_Pacing.md § Directed building fire).
        ///
        /// A wall piece with nothing siege in the selection is refused HERE,
        /// with a notice, instead of issuing an order the combat system would
        /// silently drop (§ The Wall Rule). Whether a unit or a building deals
        /// siege is a Runtime fact, read off the capabilities.
        /// </summary>
        private void AttackEnemy(Entity target, SelectionOrders.UnitCapabilities caps)
        {
            if (_orders.IsWallPiece(target) && !caps.CanAttackWalls && !caps.CanDirectSiegeFire)
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Only siege can damage walls"));
                return;
            }

            int directed = 0;
            if (caps.CanDirectBuildingFire)
                directed = _orders.IssueBuildingAttackCommands(target);
            if (caps.CanAttack)
                _orders.IssueAttackCommands(target);

            // Only buildings were asked and every one refused (an untargetable
            // or veiled target): say so instead of doing nothing.
            if (caps.CanDirectBuildingFire && directed == 0 && !caps.CanAttack)
                PlayerNotificationSystem.NotifyError(Loc.T("Cannot fire on that target"));
        }

        /// <summary>
        /// Whether the thing under the cursor leaves room for a wall-top
        /// order: nothing at all (the deck plane is the only thing there), or
        /// a wall piece that is not hostile. An enemy wall, or any other
        /// entity, is a real target and takes the normal right-click.
        /// </summary>
        private bool RampartClickAllowedOn(Entity target, SelectionOrders.TargetType targetType)
        {
            if (target == Entity.Null || !_em.Exists(target)) return true;
            return targetType == SelectionOrders.TargetType.FriendlyBuilding
                   && _orders.IsWallPiece(target);
        }

        #endregion

        #region Right-drag facing

        // ── RIGHT-DRAG ORIENTS THE FORMATION (2026-10-15). ──
        //
        // Press on open ground with units selected, drag, release: the army
        // forms up centred on the PRESS point, facing along the drag, and the
        // ghost shows exactly where each unit will stand while the button is
        // held. The ghost is FormationMoveCommandHelper.BuildPlan itself (via
        // SelectionOrders.PreviewFormationSlots), never a copy of the layout.
        //
        // A press that does not travel Cfg.dragThresholdPixels is a plain
        // right-click and routes exactly as before; so does any press that is
        // NOT a ground move (an enemy, a building, a rally, a wall top, a
        // queued Shift-click, planning mode, patrol). The camera does not use
        // the right button (it pans on the middle one), so nothing competes
        // for the drag.

        private struct PressState
        {
            public bool Tracked;      // this press may become a facing drag
            public bool Dragging;     // the cursor passed the pixel threshold
            public bool AttackMove;   // attack-move was armed at the press
            public bool HasFacing;    // the drag has a usable direction yet
            public Vector2 Screen;    // where the press landed on screen
            public float3 World;      // ... and on the ground: the formation centre
            public float3 Facing;     // press -> cursor, unit length (XZ)
            public float3 PreviewFacing;
            public bool HavePreview;
        }

        private PressState _press;
        private readonly System.Collections.Generic.List<float3> _previewSlots
            = new System.Collections.Generic.List<float3>();
        private FormationDragPreview _preview;

        /// <summary>
        /// Called by the input pump BEFORE its UI-block guard: a drag whose
        /// button came up while the pointer was over the HUD (so
        /// <see cref="Tick"/> never saw the release) is dropped, and its ghost
        /// with it — the same thing a plain click released over the HUD does.
        /// </summary>
        public void CancelDragIfReleased()
        {
            if (!_press.Tracked) return;
            if (UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButtonUp(1)) return;
            ResetPress();
        }

        private void BeginPress()
        {
            ResetPress();
            if (Cfg == null) return;
            if (PlanningModeOverlay.IsActive) return;
            if (_modes.Patrol) return;
            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift)) return;
            if (!_orders.HasOwnedMovableUnit()) return;

            // Only OPEN GROUND drags: the same pick the release would make.
            var target = ScreenPick.EntityUnderMouse(_clickMask, _em);
            var targetType = _orders.DetermineTargetType(target);
            if (targetType != SelectionOrders.TargetType.Ground) return;

            // A friendly wall top under the cursor is a wall-top order, not a
            // ground move (same test, same order as the release path).
            if (!_modes.AnyArmed
                && RampartClickAllowedOn(target, targetType)
                && TryGetRampartClick(out float3 rampartPoint)
                && _orders.IsFriendlyRampartDeck(rampartPoint))
                return;

            if (!TryGetClickPoint(out float3 at)) return;

            _press.Tracked = true;
            _press.AttackMove = _modes.AttackMove;
            _press.Screen = UnityEngine.Input.mousePosition;
            _press.World = at;
        }

        private void UpdateDrag()
        {
            if (!_press.Dragging)
            {
                Vector2 travelled = (Vector2)UnityEngine.Input.mousePosition - _press.Screen;
                float thr = Cfg.dragThresholdPixels;
                if (travelled.sqrMagnitude < thr * thr) return;
                _press.Dragging = true;
            }

            if (!TryGetClickPoint(out float3 cursor)) return;
            float3 d = cursor - _press.World;
            d.y = 0f;
            // Cursor still on the press point (a steep camera, a tiny drag):
            // keep the last direction rather than snapping to an arbitrary one.
            if (math.lengthsq(d) > 0.04f)
            {
                _press.Facing = math.normalize(d);
                _press.HasFacing = true;
            }
            if (!_press.HasFacing) return;

            // Rebuild the ghost layout only when the direction has turned
            // enough to matter — BuildPlan allocates, the arrow does not.
            float cosRefresh = math.cos(math.radians(Cfg.previewRefreshDegrees));
            if (!_press.HavePreview || math.dot(_press.Facing, _press.PreviewFacing) < cosRefresh)
            {
                _press.HavePreview = _orders.PreviewFormationSlots(_press.World, _press.Facing, _previewSlots);
                _press.PreviewFacing = _press.Facing;
            }
            if (!_press.HavePreview) return;

            if (_preview == null) _preview = new FormationDragPreview();
            _preview.Show(_previewSlots, _press.World, cursor, Cfg);
        }

        private void ResetPress()
        {
            _press = default;
            _preview?.Hide();
        }

        /// <summary>
        /// The ghost of a right-drag: one flat disc per formation slot and an
        /// arrow from the press point along the drag. Pooled — discs are
        /// created once, reused for every later drag, and only hidden between
        /// drags.
        /// </summary>
        private sealed class FormationDragPreview
        {
            private GameObject _root;
            private Material _slotMat;
            private Material _arrowMat;
            private LineRenderer _arrow;
            private readonly System.Collections.Generic.List<GameObject> _discs
                = new System.Collections.Generic.List<GameObject>();
            private int _shown;

            public void Show(System.Collections.Generic.List<float3> slots, float3 centre,
                             float3 cursor, WorldClickInputConfig cfg)
            {
                Ensure(cfg);
                if (!_root.activeSelf) _root.SetActive(true);

                float y = cfg.previewYOffset;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (i >= _discs.Count) _discs.Add(MakeDisc(cfg));
                    var disc = _discs[i];
                    if (!disc.activeSelf) disc.SetActive(true);
                    float3 s = slots[i];
                    disc.transform.position = new Vector3(s.x,
                        TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(s.x, s.z) + y, s.z);
                }
                for (int i = slots.Count; i < _shown; i++)
                    if (i < _discs.Count) _discs[i].SetActive(false);
                _shown = slots.Count;

                // Arrow: press point -> cursor, with a two-wing head.
                Vector3 a = new Vector3(centre.x, 0f, centre.z);
                Vector3 b = new Vector3(cursor.x, 0f, cursor.z);
                Vector3 dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-3f) { _arrow.enabled = false; return; }
                dir /= len;
                Vector3 side = new Vector3(dir.z, 0f, -dir.x);
                float head = Mathf.Min(1.5f, len * 0.35f);
                Vector3 wingL = b - dir * head + side * head * 0.6f;
                Vector3 wingR = b - dir * head - side * head * 0.6f;
                _arrow.enabled = true;
                _arrow.SetPosition(0, Lift(a, y));
                _arrow.SetPosition(1, Lift(b, y));
                _arrow.SetPosition(2, Lift(wingL, y));
                _arrow.SetPosition(3, Lift(b, y));
                _arrow.SetPosition(4, Lift(wingR, y));
            }

            public void Hide()
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
            }

            private static Vector3 Lift(Vector3 p, float y)
                => new Vector3(p.x, TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z) + y, p.z);

            private void Ensure(WorldClickInputConfig cfg)
            {
                if (_root != null) return;
                _root = new GameObject("FormationDragPreview");
                _slotMat = MakeTransparent(cfg.previewSlotColor);
                _arrowMat = MakeTransparent(cfg.previewArrowColor);

                var arrowGo = new GameObject("FacingArrow");
                arrowGo.transform.SetParent(_root.transform, false);
                _arrow = arrowGo.AddComponent<LineRenderer>();
                _arrow.sharedMaterial = _arrowMat;
                _arrow.startColor = _arrow.endColor = cfg.previewArrowColor;
                _arrow.startWidth = _arrow.endWidth = cfg.previewArrowWidth;
                _arrow.useWorldSpace = true;
                _arrow.positionCount = 5;
                _arrow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _arrow.receiveShadows = false;
            }

            private GameObject MakeDisc(WorldClickInputConfig cfg)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "GhostSlot";
                disc.transform.SetParent(_root.transform, false);
                disc.transform.localScale = new Vector3(cfg.previewSlotSize, 0.02f, cfg.previewSlotSize);
                var col = disc.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);   // must never catch a click ray
                var r = disc.GetComponent<Renderer>();
                r.sharedMaterial = _slotMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                return disc;
            }

            private static Material MakeTransparent(Color color)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Sprites/Default");
                var mat = new Material(shader);
                if (mat.HasProperty("_Surface"))
                {
                    // URP Unlit, transparent: the property alone does not
                    // switch the blend state, so set it the way the material
                    // inspector would.
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                mat.renderQueue = 3000;
                return mat;
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

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Mathf.Abs(ray.direction.y) < Cfg.rampartRayEpsilon) return false;

            // The wall-walk follows the ground under the wall
            // (LayerTransitionSystem.DeckYAt), so there is no single plane to
            // hit: start from the ground-level deck under the camera's aim and
            // settle onto the surface in a few steps — terrain under a wall is
            // gentle, so this converges at once.
            float deckY = Nav.LayerTransitionSystem.DeckYAt(ray.origin.x, ray.origin.z);
            Vector3 hit = default;
            for (int i = 0; i < 4; i++)
            {
                float t = (deckY - ray.origin.y) / ray.direction.y;
                if (t <= 0f) return false; // deck surface is behind the camera
                hit = ray.origin + ray.direction * t;
                deckY = Nav.LayerTransitionSystem.DeckYAt(hit.x, hit.z);
            }
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
            bool underwater = water != null && point.y < water.WalkLine;

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
