// SelectionSystem.cs
// Handles entity selection (click, double-click, and box select)
// Part of: Input/

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.Terrain;

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
namespace TheWaningBorder.Input
{
    /// <summary>
    /// Manages entity selection for the local player.
    /// Supports single-click, double-click (select all of same type), and drag box selection.
    /// Double-click selects all on-screen units of the same UnitClass.
    /// Ctrl+double-click selects all units of that type map-wide.
    ///
    /// Static access: SelectionSystem.CurrentSelection
    /// </summary>
    public class SelectionSystem : MonoBehaviour
    {
        #region Configuration

        [SerializeField] private LayerMask clickMask = ~0;
        [SerializeField] private float minDragSize = 4f; // Minimum pixels for box select


        #endregion

        #region Static Access

        private static SelectionSystem _instance;

        /// <summary>
        /// Currently selected entities (owned by local player).
        /// </summary>
        public static List<Entity> CurrentSelection => _instance?._selection;

        /// <summary>
        /// Clear the current selection.
        /// </summary>
        public static void ClearSelection()
        {
            _instance?._selection.Clear();
        }

        /// <summary>
        /// Remove dead/destroyed entities from selection.
        /// </summary>
        public static void CleanSelection()
        {
            _instance?.CleanSelectionInternal();
        }

        /// <summary>
        /// Add an entity to the selection.
        /// </summary>
        public static void AddToSelection(Entity entity)
        {
            if (_instance != null && !_instance._selection.Contains(entity))
                _instance._selection.Add(entity);
        }

        /// <summary>
        /// Remove an entity from the selection.
        /// </summary>
        public static void RemoveFromSelection(Entity entity)
        {
            _instance?._selection.Remove(entity);
        }

        #endregion

        #region State

        private EntityWorld _world;
        private EntityManager _em;

        // Cached queries — CreateEntityQuery here leaked one query into the
        // world's registry on EVERY double-click (SelectAllOfType) and EVERY
        // box-select drag, which is the decay pattern Core/CachedEntityQuery
        // was written to kill.
        private static readonly ComponentType[] UnitSelectQueryTypes =
        {
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<UnitTag>(),
        };
        private static readonly ComponentType[] AnySelectQueryTypes =
        {
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _unitSelectQuery;
        private TheWaningBorder.Core.CachedEntityQuery _anySelectQuery;
        private readonly List<Entity> _selection = new();

        // Drag-select state (screen space, origin = bottom-left)
        private Vector3 _dragStartScreen;
        private bool _isDragging;
        private Rect _dragScreenRect;

        /// <summary>
        /// The drag rectangle in SCREEN space, and whether it is live. Published
        /// for SelectionBoxBinder to draw; this class no longer draws anything.
        /// </summary>
        public static Rect DragRect { get; private set; }
        public static bool DragActive { get; private set; }

        // GUI textures

        // Double-click detection

        private SelectionSystemConfig _cfg;
        private SelectionSystemConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<SelectionSystemConfig>());
        private float _lastClickTime = -1f;
        private UnitClass _lastClickedClass;
        private bool _lastClickWasUnit;

        #endregion

        #region Lifecycle

        void Awake()
        {
            // Publish the selection list DOWNWARD once. _selection is a
            // readonly List mutated in place, so the reference stays valid
            // for the life of the instance -- in-world displays can read it
            // without naming the input layer. Publishing from the property
            // getter instead would only work while something else happened
            // to be calling it.
            TheWaningBorder.Core.PresentationState.Selection = _selection;
            _instance = this;

            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            _em = _world.EntityManager;

            // Create textures for selection box
        }

        void OnDestroy()
        {
            _instance = null;
        }

        void Update()
        {
            // Observer perspective: the view faction follows the selection's
            // owner every frame (null with nothing selected = see everything).
            // Runs even while selection input is UI-blocked so the view never
            // lapses while mousing over the HUD.
            UpdateObserverViewFaction();

            // Block selection during UI interactions
            if (ShouldBlockSelection())
            {
                _isDragging = false;
                return;
            }

            CleanSelectionInternal();
            HandleSelection();
        }

        #endregion

        #region Input Blocking

        private bool ShouldBlockSelection()
        {
            // Block if suppressed by GUI
            if (BuilderCommandPanel.SuppressClicksThisFrame)
                return true;

            // Final game UI (uGUI): standard EventSystem hover check covers
            // every authored panel, current and future.
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return true;

            // Old IMGUI panel guards (EntityInfoPanel / EntityActionPanel /
            // SpellPanel / CultureChoicePopup) removed with the old UI
            // (2026-07-17); the EventSystem check above covers the final uGUI.

            // Block during building placement
            if (TheWaningBorder.Core.PresentationState.PlacingBuilding)
                return true;

            return false;
        }

        #endregion

        #region Selection Handling

        private void HandleSelection()
        {
            // Start drag on left mouse down
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                _isDragging = true;
                _dragStartScreen = UnityEngine.Input.mousePosition;
                _dragScreenRect = new Rect(_dragStartScreen.x, _dragStartScreen.y, 0, 0);
                DragRect = _dragScreenRect;
                DragActive = false;
            }

            // Update drag rect while dragging
            if (_isDragging)
            {
                _dragScreenRect = MakeScreenRect(_dragStartScreen, UnityEngine.Input.mousePosition);
                DragRect = _dragScreenRect;
                DragActive = _isDragging
                    && (_dragScreenRect.width > minDragSize || _dragScreenRect.height > minDragSize);
            }

            // Complete selection on left mouse up
            if (_isDragging && UnityEngine.Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
                DragActive = false;

                // Small drag = click select, large drag = box select
                if (_dragScreenRect.width < minDragSize || _dragScreenRect.height < minDragSize)
                {
                    ClickSelect();
                }
                else
                {
                    BoxSelect(_dragScreenRect);
                }
            }
        }

        #endregion

        #region Click Selection

        private void ClickSelect()
        {
            var e = ScreenPick.EntityUnderMouse(clickMask, _em);
            float now = Time.time;

            // Check for double-click on a unit of the same type
            bool dblClickSelectable = (e != Entity.Null && _em.Exists(e))
                && IsSelectableByPlayer(e);
            if (dblClickSelectable && _em.HasComponent<UnitTag>(e) && IsOwnedByPlayer(e))
            {
                var clickedClass = _em.GetComponentData<UnitTag>(e).Class;

                if (_lastClickWasUnit
                    && clickedClass == _lastClickedClass
                    && (now - _lastClickTime) < Cfg.doubleClickThreshold)
                {
                    // Double-click detected: select all units of this type
                    bool mapWide = UnityEngine.Input.GetKey(KeyCode.LeftControl)
                                || UnityEngine.Input.GetKey(KeyCode.RightControl);
                    SelectAllOfType(clickedClass, mapWide);

                    // Reset so a third click doesn't re-trigger
                    _lastClickWasUnit = false;
                    _lastClickTime = -1f;
                    return;
                }

                // Record this click for potential double-click
                _lastClickTime = now;
                _lastClickedClass = clickedClass;
                _lastClickWasUnit = true;
            }
            else
            {
                // Clicked something that isn't an owned unit — reset tracking
                _lastClickWasUnit = false;
                _lastClickTime = -1f;
            }

            // Normal single-click selection
            _selection.Clear();

            bool selectable = (e != Entity.Null && _em.Exists(e)) && IsSelectableByPlayer(e);
            if (selectable)
                _selection.Add(e);
        }

        #endregion

        #region Double-click Select All of Type

        /// <summary>
        /// Selects all player-owned units of the given UnitClass.
        /// If mapWide is false, only units currently visible on screen are selected.
        /// If mapWide is true (Ctrl+double-click), all units of that type are selected regardless of screen position.
        /// </summary>
        private void SelectAllOfType(UnitClass unitClass, bool mapWide)
        {
            var cam = Camera.main;
            if (!cam && !mapWide) return;

            _selection.Clear();

            var query = _unitSelectQuery.Get(_em, UnitSelectQueryTypes);
            var ents = query.ToEntityArray(Allocator.Temp);

            float screenW = Screen.width;
            float screenH = Screen.height;

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByPlayer(e)) continue;
                if (_em.GetComponentData<UnitTag>(e).Class != unitClass) continue;

                if (!mapWide)
                {
                    // Check that the unit is on screen
                    var pos = _em.GetComponentData<LocalTransform>(e).Position;
                    Vector3 screenPos = cam.WorldToScreenPoint(new Vector3(pos.x, pos.y, pos.z));

                    // Behind camera or outside viewport
                    if (screenPos.z <= 0f) continue;
                    if (screenPos.x < 0f || screenPos.x > screenW) continue;
                    if (screenPos.y < 0f || screenPos.y > screenH) continue;
                }

                _selection.Add(e);
            }

            ents.Dispose();
        }

        #endregion

        #region Box Selection

        private void BoxSelect(Rect screenRect)
        {
            var cam = Camera.main;
            if (!cam) return;

            _selection.Clear();

            var query = _anySelectQuery.Get(_em, AnySelectQueryTypes);
            var ents = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByPlayer(e)) continue; // Box select only own units

                // Skip uncontrollable units (caravans, trade patrols, Feraldis Raiders)
                if (_em.HasComponent<NotControllableTag>(e)) continue;

                // Project entity bounds to screen
                Bounds worldBounds = ComputeEntityWorldBounds(e);
                Rect screenBounds = ProjectWorldBoundsToScreenRect(cam, worldBounds);

                if (screenBounds.width <= 0f || screenBounds.height <= 0f)
                    continue;

                // Check overlap
                if (screenRect.Overlaps(screenBounds, true))
                {
                    _selection.Add(e);
                }
            }

            ents.Dispose();

            // Post-filter: prioritize units over buildings, military over economic
            FilterBoxSelection();
        }

        /// <summary>
        /// Filter box selection results:
        /// 1. If mix of units and buildings → keep only units
        /// 2. If mix of military and economic units → keep only military
        /// </summary>
        private void FilterBoxSelection()
        {
            if (_selection.Count <= 1) return;

            var units = new List<Entity>();
            var buildings = new List<Entity>();

            foreach (var e in _selection)
            {
                if (_em.HasComponent<UnitTag>(e)) units.Add(e);
                else if (_em.HasComponent<BuildingTag>(e)) buildings.Add(e);
            }

            // Prioritize units over buildings
            if (units.Count > 0 && buildings.Count > 0)
            {
                _selection.Clear();
                _selection.AddRange(units);
            }

            // Among units, prioritize military over economic — gated on the
            // SmartMilitaryDrag toggle. When OFF, mixed-unit drags keep
            // every entity the rectangle covered.
            // Ctrl (or Alt) held = "select literally everything in the box",
            // the genre convention. The military-wins rule is right by default
            // — you drag over your base to grab the army, not the miners — but
            // it had NO override, so a player who wanted the workers simply
            // could not get them whenever a single soldier stood in the
            // rectangle. That is the reported bug: the rule itself is fine,
            // the absence of an escape hatch was not.
            bool selectEverything =
                UnityEngine.Input.GetKey(KeyCode.LeftControl)
                || UnityEngine.Input.GetKey(KeyCode.RightControl)
                || UnityEngine.Input.GetKey(KeyCode.LeftAlt)
                || UnityEngine.Input.GetKey(KeyCode.RightAlt);

            if (units.Count > 1 && GameSettings.SmartMilitaryDrag && !selectEverything)
            {
                var military = new List<Entity>();
                var economic = new List<Entity>();

                foreach (var e in _selection)
                {
                    if (!_em.HasComponent<UnitTag>(e)) continue;
                    var cls = _em.GetComponentData<UnitTag>(e).Class;
                    // The original code was missing an `else` here, so every
                    // economic unit was also added to `military` and the
                    // priority filter never dropped them. (task-060 F-1
                    // claimed a fix that didn't actually land — fixed now.)
                    if (cls == UnitClass.Economy || cls == UnitClass.Miner)
                        economic.Add(e);
                    else
                        military.Add(e);
                }

                if (military.Count > 0 && economic.Count > 0)
                {
                    _selection.Clear();
                    _selection.AddRange(military);
                }
            }
        }

        #endregion

        #region Selection Validation

        private bool IsSelectableByPlayer(Entity e)
        {
            if (!_em.Exists(e)) return false;

            // Resource deposits are always selectable
            if (_em.HasComponent<IronMineTag>(e) || _em.HasComponent<VeilstoneOutcroppingTag>(e)
                || _em.HasComponent<VeilsteelDepositTag>(e))
                return true;

            // Must have faction tag
            if (!_em.HasComponent<FactionTag>(e))
                return false;

            // Must be a unit or building
            if (!_em.HasComponent<UnitTag>(e) && !_em.HasComponent<BuildingTag>(e))
                return false;

            // A dying/dead unit is a corpse for the rest of its death animation:
            // it can't be (re)selected. DeathSystem tags it with DeathAnimationState
            // and zeroes its movement on death; the navigation integrator already
            // excludes DeathAnimationState so it never moves again.
            if (_em.HasComponent<UnitTag>(e) && IsDeadOrDying(e))
                return false;

            // If the raycast hit the entity's GameObject, it is visible on screen
            // (FogVisibilitySyncSystem already hides invisible entities by deactivating GOs).
            // No need for a redundant fog check here — the raycast itself is the visibility proof.
            // Fix #237: removed unreachable `return false` that followed this line.
            return true;
        }

        private bool IsOwnedByPlayer(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e))
                return false;
            // ViewFactionOrLocal == LocalPlayerFaction in normal play. For an
            // observer it is the faction being viewed, so box-select and
            // select-all-of-type operate on that player's units.
            if (_em.GetComponentData<FactionTag>(e).Value != GameSettings.ViewFactionOrLocal)
                return false;
            if (!_em.HasComponent<UnitTag>(e) && !_em.HasComponent<BuildingTag>(e))
                return false;
            return true;
        }

        /// <summary>
        /// Observer mode only: publish the owner of the current selection as
        /// the faction whose perspective the fog, minimap and HUD render.
        /// Null (nothing owned selected) = the observer sees everything.
        /// </summary>
        private void UpdateObserverViewFaction()
        {
            if (!GameSettings.IsObserver) return;
            Faction? view = null;
            for (int i = 0; i < _selection.Count; i++)
            {
                var e = _selection[i];
                if (!_em.Exists(e) || !_em.HasComponent<FactionTag>(e)) continue;
                if (!_em.HasComponent<UnitTag>(e) && !_em.HasComponent<BuildingTag>(e)) continue;
                view = _em.GetComponentData<FactionTag>(e).Value;
                break;
            }
            GameSettings.ObserverViewFaction = view;
        }

        private void CleanSelectionInternal()
        {
            for (int i = _selection.Count - 1; i >= 0; i--)
            {
                var sel = _selection[i];
                // Drop entities that are gone, or units that have just died — a
                // dying unit lingers ~2 s while its death animation plays, and a
                // corpse must not stay selected (so it can't receive commands).
                if (!_em.Exists(sel) ||
                    (_em.HasComponent<UnitTag>(sel) && IsDeadOrDying(sel)))
                    _selection.RemoveAt(i);
            }
        }

        /// <summary>
        /// True once a unit has died: it has a running death animation
        /// (<see cref="DeathAnimationState"/>) or its health has hit zero. Used to
        /// keep corpses unselectable and out of the active selection.
        /// </summary>
        private bool IsDeadOrDying(Entity e)
        {
            if (_em.HasComponent<DeathAnimationState>(e)
                && _em.IsComponentEnabled<DeathAnimationState>(e)) return true;
            if (_em.HasComponent<Health>(e) && _em.GetComponentData<Health>(e).Value <= 0)
                return true;
            return false;
        }

        #endregion

        #region Bounds Calculation

        private Bounds ComputeEntityWorldBounds(Entity e)
        {
            if (!_em.HasComponent<LocalTransform>(e))
                return new Bounds(Vector3.zero, Vector3.zero);

            var pos = _em.GetComponentData<LocalTransform>(e).Position;
            float radius = 0.5f;

            if (_em.HasComponent<Radius>(e))
                radius = _em.GetComponentData<Radius>(e).Value;

            // Use terrain height for Y — the ECS entity Y may be stale since
            // MovementSystem only moves in XZ. The visual is always at terrain height
            // (synced by PresentationSpawnSystem.SyncTransforms), so bounds must match.
            float terrainY = TerrainUtility.GetHeight(pos.x, pos.z);
            Vector3 center = new Vector3(pos.x, terrainY, pos.z);
            Vector3 size = new Vector3(radius * 2, radius * 2, radius * 2);

            return new Bounds(center, size);
        }

        private Rect ProjectWorldBoundsToScreenRect(Camera cam, Bounds wb)
        {
            // Get 8 corners of the bounding box
            Vector3 c = wb.center;
            Vector3 e = wb.extents;

            Vector3[] corners = new Vector3[8]
            {
                c + new Vector3(-e.x, -e.y, -e.z),
                c + new Vector3( e.x, -e.y, -e.z),
                c + new Vector3(-e.x,  e.y, -e.z),
                c + new Vector3( e.x,  e.y, -e.z),
                c + new Vector3(-e.x, -e.y,  e.z),
                c + new Vector3( e.x, -e.y,  e.z),
                c + new Vector3(-e.x,  e.y,  e.z),
                c + new Vector3( e.x,  e.y,  e.z),
            };

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(corners[i]);
                if (sp.z < 0) continue; // Behind camera

                minX = Mathf.Min(minX, sp.x);
                maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, sp.y);
                maxY = Mathf.Max(maxY, sp.y);
            }

            if (minX > maxX || minY > maxY)
                return new Rect(0, 0, 0, 0);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        #endregion

        #region Raycasting


        #endregion

        #region Utility Methods

        /// <summary>
        /// Create a screen-space rect from two points (handles any drag direction).
        /// </summary>
        private Rect MakeScreenRect(Vector3 start, Vector3 end)
        {
            float x = Mathf.Min(start.x, end.x);
            float y = Mathf.Min(start.y, end.y);
            float w = Mathf.Abs(end.x - start.x);
            float h = Mathf.Abs(end.y - start.y);
            return new Rect(x, y, w, h);
        }


        #endregion

    }
}