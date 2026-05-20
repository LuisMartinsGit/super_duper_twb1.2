// File: Assets/Scripts/UI/HUD/FloatingHealthBars.cs
// Renders floating health bars above hovered and selected entities.
//
// Was IMGUI (OnGUI) — Unity draws IMGUI on top of every ScreenSpaceOverlay
// canvas, which put bars in front of the CEF web HUD. Now drives a pool of
// UGUI Image components on a dedicated Canvas at sortingOrder 50 so bars
// always sit BEHIND the HUD chrome (CEF canvas is at sortingOrder 100) and
// in front of the 3D scene.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.UI.Common;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Draws floating health bars above entities that are hovered or selected.
    /// </summary>
    [DefaultExecutionOrder(910)]
    public class FloatingHealthBars : MonoBehaviour
    {
        [Header("Bar Dimensions")]
        [SerializeField] private float barWidth = 60f;
        [SerializeField] private float barHeight = 6f;
        [SerializeField] private float barBorder = 1f;
        [SerializeField] private float yOffsetAboveEntity = 1.8f;
        [SerializeField] private float buildingYOffset = 3.2f;

        [Header("Canvas")]
        // CEF canvas is at sortingOrder 100. 50 puts bars under it but well
        // above the world (3D scene) and most other canvases.
        [SerializeField] private int canvasSortingOrder = 50;

        private EntityWorld _world;
        private EntityManager _em;
        private Camera _cachedCamera;

        // Bar chrome — sourced from WorldOverlayPalette so HP bars share the
        // jade panel palette.
        private static readonly Color BgColor = new Color(
            WorldOverlayPalette.PanelEdge.r, WorldOverlayPalette.PanelEdge.g,
            WorldOverlayPalette.PanelEdge.b, 0.85f);
        private static readonly Color BorderColor = WorldOverlayPalette.InlayShadow;

        // UGUI rendering — a pool of bar widgets reparented under a private
        // canvas. Each widget has three nested rect transforms: outer border,
        // background fill, foreground (ratio) fill.
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private readonly List<BarWidget> _pool = new();
        private int _activeCount;
        private readonly HashSet<Entity> _drawn = new();

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;
            EnsureCanvas();
        }

        void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("FloatingHealthBars-Canvas",
                typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = canvasSortingOrder;
            _canvasRect = (RectTransform)go.transform;

            // Constant-pixel-size scaling — bar dimensions are in screen
            // pixels (matches the legacy IMGUI behaviour). If the rest of
            // the HUD ever moves to ScaleWithScreenSize at a reference res,
            // mirror it here for consistent sizing.
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world != null && _world.IsCreated) _em = _world.EntityManager;
            }
            if (_em.Equals(default(EntityManager))) { HideAll(); return; }

            var cam = _cachedCamera != null ? _cachedCamera : (_cachedCamera = Camera.main);
            if (cam == null) { HideAll(); return; }

            _activeCount = 0;
            _drawn.Clear();

            var hovered = RTSInput.HoveredEntity;
            if (hovered != Entity.Null && _em.Exists(hovered) && _em.HasComponent<Health>(hovered))
            {
                if (ShouldShowBar(hovered))
                {
                    DrawBarForEntity(cam, hovered, isHovered: true);
                    _drawn.Add(hovered);
                }
            }

            var selection = RTSInput.CurrentSelection;
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    var e = selection[i];
                    if (_drawn.Contains(e)) continue;
                    if (!_em.Exists(e)) continue;
                    if (!_em.HasComponent<Health>(e)) continue;

                    DrawBarForEntity(cam, e, isSelected: true);
                    _drawn.Add(e);
                }
            }

            // Hide pool items that weren't claimed this frame.
            for (int i = _activeCount; i < _pool.Count; i++)
                _pool[i].SetActive(false);
        }

        private void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++) _pool[i].SetActive(false);
        }

        private bool ShouldShowBar(Entity e)
        {
            // FogVisibilitySyncSystem deactivates invisible entities, so any
            // hover-raycast hit is visible by construction.
            return _em.Exists(e);
        }

        private void DrawBarForEntity(Camera cam, Entity e, bool isSelected = false, bool isHovered = false)
        {
            if (!_em.HasComponent<LocalTransform>(e)) return;
            if (_em.HasComponent<BattalionLeader>(e)) return;  // invisible dummy HP
            if (_em.HasComponent<BattalionMemberData>(e) && !isSelected && !isHovered) return;

            var hp = _em.GetComponentData<Health>(e);
            if (hp.Max <= 0) return;

            var pos = _em.GetComponentData<LocalTransform>(e).Position;
            bool isBuilding = _em.HasComponent<BuildingTag>(e);
            float yOff = isBuilding ? buildingYOffset : yOffsetAboveEntity;

            Vector3 worldPos = new Vector3(pos.x, pos.y + yOff, pos.z);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0) return;  // behind camera

            float ratio = Mathf.Clamp01((float)hp.Value / hp.Max);
            Color fillColor = ratio > 0.5f  ? WorldOverlayPalette.HealthFull :
                              ratio > 0.25f ? WorldOverlayPalette.HealthMid  :
                                              WorldOverlayPalette.HealthLow;

            var bar = GetOrAllocate(_activeCount++);
            // UGUI uses bottom-left origin in screen space (matches WorldToScreenPoint).
            bar.SetGeometry(screenPos.x, screenPos.y, barWidth, barHeight, barBorder);
            bar.SetColors(BgColor, BorderColor, fillColor);
            bar.SetFill(ratio);
            bar.SetActive(true);

            // Action progress bar — buildings that are actively training a
            // unit OR upgrading. White bar directly under the HP bar so the
            // player reads "this building is producing something" from
            // across the map. Upgrade takes precedence over training (they
            // can't run simultaneously; BuildingUpgrading freezes training).
            if (isBuilding)
            {
                float progress = -1f;
                if (_em.HasComponent<BuildingUpgrading>(e))
                {
                    var up = _em.GetComponentData<BuildingUpgrading>(e);
                    if (up.Total > 0f)
                        progress = Mathf.Clamp01(up.Progress / up.Total);
                }
                else if (_em.HasComponent<TrainingState>(e))
                {
                    var ts = _em.GetComponentData<TrainingState>(e);
                    if (ts.Busy == 1 && ts.Total > 0f)
                        progress = Mathf.Clamp01((ts.Total - ts.Remaining) / ts.Total);
                }

                if (progress >= 0f)
                {
                    float gap = 2f;
                    float py = screenPos.y - barHeight - gap;
                    var pbar = GetOrAllocate(_activeCount++);
                    pbar.SetGeometry(screenPos.x, py, barWidth, barHeight, barBorder);
                    pbar.SetColors(BgColor, BorderColor, Color.white);
                    pbar.SetFill(progress);
                    pbar.SetActive(true);
                }
            }
        }

        private BarWidget GetOrAllocate(int index)
        {
            while (_pool.Count <= index)
            {
                _pool.Add(BarWidget.Create(_canvasRect));
            }
            return _pool[index];
        }

        /// <summary>
        /// A single floating bar — three nested UGUI Images: border, bg, fill.
        /// All positioned in screen-space (anchorMin=anchorMax=(0,0)) so the
        /// pixel-accurate IMGUI placement carries over unchanged.
        /// </summary>
        private sealed class BarWidget
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Border, Bg, Fill;

            public static BarWidget Create(RectTransform parent)
            {
                var go = new GameObject("HpBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
                var w = new BarWidget
                {
                    Root = go,
                    Rect = (RectTransform)go.transform,
                    Border = go.GetComponent<Image>(),
                };
                AnchorBottomLeft(w.Rect);
                w.Border.raycastTarget = false;

                var bg = NewChild(go.transform, "Bg");
                w.Bg = bg.AddComponent<Image>();
                w.Bg.raycastTarget = false;

                var fill = NewChild(go.transform, "Fill");
                w.Fill = fill.AddComponent<Image>();
                w.Fill.raycastTarget = false;

                return w;
            }

            private static GameObject NewChild(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(parent, false);
                AnchorBottomLeft((RectTransform)go.transform);
                return go;
            }

            private static void AnchorBottomLeft(RectTransform r)
            {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.zero;
                r.pivot = Vector2.zero;
            }

            public void SetActive(bool on) { if (Root.activeSelf != on) Root.SetActive(on); }

            public void SetGeometry(float screenX, float screenY, float w, float h, float border)
            {
                // Screen-space pixel position; bar is centred on (screenX, screenY).
                float bx = screenX - w * 0.5f - border;
                float by = screenY - h * 0.5f - border;
                Rect.anchoredPosition = new Vector2(bx, by);
                Rect.sizeDelta = new Vector2(w + border * 2, h + border * 2);

                ((RectTransform)Bg.transform).anchoredPosition  = new Vector2(border, border);
                ((RectTransform)Bg.transform).sizeDelta         = new Vector2(w, h);

                ((RectTransform)Fill.transform).anchoredPosition = new Vector2(border, border);
                ((RectTransform)Fill.transform).sizeDelta        = new Vector2(w, h);
                _lastInnerWidth = w;
            }

            private float _lastInnerWidth;

            public void SetColors(Color bg, Color border, Color fill)
            {
                Border.color = border;
                Bg.color = bg;
                Fill.color = fill;
            }

            public void SetFill(float ratio)
            {
                var s = ((RectTransform)Fill.transform).sizeDelta;
                ((RectTransform)Fill.transform).sizeDelta = new Vector2(_lastInnerWidth * ratio, s.y);
            }
        }
    }
}
