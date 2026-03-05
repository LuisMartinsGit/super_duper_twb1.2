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
using TheWaningBorder.UI.Panels;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.Terrain;

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
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        
        [Header("Selection")]
        [SerializeField] private LayerMask clickMask = ~0;
        [SerializeField] private float minDragSize = 4f; // Minimum pixels for box select
        
        [Header("Visual")]
        [SerializeField] private Color boxColor = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        [SerializeField] private Color boxBorderColor = new Color(0.3f, 1f, 0.3f, 0.8f);
        
        // ═══════════════════════════════════════════════════════════════════════
        // STATIC ACCESS
        // ═══════════════════════════════════════════════════════════════════════
        
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
        
        // ═══════════════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private EntityWorld _world;
        private EntityManager _em;
        private readonly List<Entity> _selection = new();
        
        // Drag-select state (screen space, origin = bottom-left)
        private Vector3 _dragStartScreen;
        private bool _isDragging;
        private Rect _dragScreenRect;
        
        // GUI textures
        private Texture2D _boxTexture;
        private Texture2D _borderTexture;

        // Double-click detection
        private const float DoubleClickThreshold = 0.3f;
        private float _lastClickTime = -1f;
        private UnitClass _lastClickedClass;
        private bool _lastClickWasUnit;
        
        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════
        
        void Awake()
        {
            _instance = this;
            
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;
            
            // Create textures for selection box
            _boxTexture = MakeSolidTexture(boxColor);
            _borderTexture = MakeSolidTexture(boxBorderColor);
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            
            if (_boxTexture != null)
                Destroy(_boxTexture);
            if (_borderTexture != null)
                Destroy(_borderTexture);
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;
            
            // Refresh EntityManager if needed
            if (_em.Equals(default(EntityManager)))
                _em = _world.EntityManager;

            // Block selection during UI interactions
            if (ShouldBlockSelection())
            {
                _isDragging = false;
                return;
            }

            CleanSelectionInternal();
            HandleSelection();
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // INPUT BLOCKING
        // ═══════════════════════════════════════════════════════════════════════
        
        private bool ShouldBlockSelection()
        {
            // Block if suppressed by GUI
            if (BuilderCommandPanel.SuppressClicksThisFrame)
                return true;

            // Block if mouse is over UI panels
            if (EntityInfoPanel.IsPointerOver() || EntityActionPanel.IsPointerOver())
                return true;

            // Block during building placement
            if (BuilderCommandPanel.IsPlacingBuilding)
                return true;

            return false;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // SELECTION HANDLING
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleSelection()
        {
            // Start drag on left mouse down
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                _isDragging = true;
                _dragStartScreen = UnityEngine.Input.mousePosition;
                _dragScreenRect = new Rect(_dragStartScreen.x, _dragStartScreen.y, 0, 0);
            }

            // Update drag rect while dragging
            if (_isDragging)
            {
                _dragScreenRect = MakeScreenRect(_dragStartScreen, UnityEngine.Input.mousePosition);
            }

            // Complete selection on left mouse up
            if (_isDragging && UnityEngine.Input.GetMouseButtonUp(0))
            {
                _isDragging = false;

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
        
        // ═══════════════════════════════════════════════════════════════════════
        // CLICK SELECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private void ClickSelect()
        {
            var e = RaycastPickEntity();
            float now = Time.time;

            // Check for double-click on a unit of the same type
            if (e != Entity.Null && _em.Exists(e) && IsSelectableByPlayer(e)
                && _em.HasComponent<UnitTag>(e) && IsOwnedByPlayer(e))
            {
                var clickedClass = _em.GetComponentData<UnitTag>(e).Class;

                if (_lastClickWasUnit
                    && clickedClass == _lastClickedClass
                    && (now - _lastClickTime) < DoubleClickThreshold)
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

            if (e != Entity.Null && _em.Exists(e) && IsSelectableByPlayer(e))
            {
                _selection.Add(e);
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // DOUBLE-CLICK SELECT ALL OF TYPE
        // ═══════════════════════════════════════════════════════════════════════

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

            var query = _em.CreateEntityQuery(typeof(LocalTransform), typeof(FactionTag), typeof(UnitTag));
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

        // ═══════════════════════════════════════════════════════════════════════
        // BOX SELECTION
        // ═══════════════════════════════════════════════════════════════════════
        
        private void BoxSelect(Rect screenRect)
        {
            var cam = Camera.main;
            if (!cam) return;

            _selection.Clear();

            var query = _em.CreateEntityQuery(typeof(LocalTransform), typeof(FactionTag));
            var ents = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!_em.Exists(e)) continue;
                if (!IsOwnedByPlayer(e)) continue; // Box select only own units

                // Project entity bounds to screen
                Bounds worldBounds = ComputeEntityWorldBounds(e);
                Rect screenBounds = ProjectWorldBoundsToScreenRect(cam, worldBounds);

                if (screenBounds.width <= 0f || screenBounds.height <= 0f)
                    continue;

                // Check overlap
                if (screenRect.Overlaps(screenBounds, true))
                    _selection.Add(e);
            }

            ents.Dispose();
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // SELECTION VALIDATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private bool IsSelectableByPlayer(Entity e)
        {
            // Resource deposits (iron mines, crystal cadavers) are always selectable if visible
            if (_em.HasComponent<IronMineTag>(e) || _em.HasComponent<CadaverTag>(e))
            {
                if (_em.HasComponent<LocalTransform>(e))
                {
                    var pos = _em.GetComponentData<LocalTransform>(e).Position;
                    return FogOfWarSystem.IsVisibleToFaction(GameSettings.LocalPlayerFaction, pos);
                }
                return false;
            }

            // Must have faction tag
            if (!_em.HasComponent<FactionTag>(e))
                return false;

            // Must be a unit or building
            if (!_em.HasComponent<UnitTag>(e) && !_em.HasComponent<BuildingTag>(e))
                return false;

            // Own units are always selectable
            if (_em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction)
                return true;

            // Enemy units/buildings are selectable if visible through fog of war
            if (_em.HasComponent<LocalTransform>(e))
            {
                var pos = _em.GetComponentData<LocalTransform>(e).Position;
                return FogOfWarSystem.IsVisibleToFaction(GameSettings.LocalPlayerFaction, pos);
            }

            return false;
        }
        
        private bool IsOwnedByPlayer(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e))
                return false;
            if (_em.GetComponentData<FactionTag>(e).Value != GameSettings.LocalPlayerFaction)
                return false;
            if (!_em.HasComponent<UnitTag>(e) && !_em.HasComponent<BuildingTag>(e))
                return false;
            return true;
        }

        private void CleanSelectionInternal()
        {
            for (int i = _selection.Count - 1; i >= 0; i--)
            {
                if (!_em.Exists(_selection[i]))
                    _selection.RemoveAt(i);
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // BOUNDS CALCULATION
        // ═══════════════════════════════════════════════════════════════════════
        
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
                var go = hit.collider.gameObject;
                
                // Check for EntityReference component
                var link = go.GetComponent<EntityReference>();
                if (link != null && _em.Exists(link.Entity))
                    return link.Entity;
                
                // Check parent
                if (go.transform.parent != null)
                {
                    link = go.transform.parent.GetComponent<EntityReference>();
                    if (link != null && _em.Exists(link.Entity))
                        return link.Entity;
                }
            }
            return Entity.Null;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // UTILITY METHODS
        // ═══════════════════════════════════════════════════════════════════════
        
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
        
        private Texture2D MakeSolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // GUI DRAWING
        // ═══════════════════════════════════════════════════════════════════════

        void OnGUI()
        {
            // Draw selection rectangle while dragging
            if (_isDragging && (_dragScreenRect.width > minDragSize || _dragScreenRect.height > minDragSize))
            {
                // Convert to GUI coordinates (origin at top-left)
                Rect guiRect = new Rect(
                    _dragScreenRect.x,
                    Screen.height - _dragScreenRect.y - _dragScreenRect.height,
                    _dragScreenRect.width,
                    _dragScreenRect.height
                );
                
                // Draw filled box
                GUI.DrawTexture(guiRect, _boxTexture);
                
                // Draw border
                float borderWidth = 2f;
                GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, guiRect.width, borderWidth), _borderTexture); // Top
                GUI.DrawTexture(new Rect(guiRect.x, guiRect.yMax - borderWidth, guiRect.width, borderWidth), _borderTexture); // Bottom
                GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, borderWidth, guiRect.height), _borderTexture); // Left
                GUI.DrawTexture(new Rect(guiRect.xMax - borderWidth, guiRect.y, borderWidth, guiRect.height), _borderTexture); // Right
            }
        }
    }
}