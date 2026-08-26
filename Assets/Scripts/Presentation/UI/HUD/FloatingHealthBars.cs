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

        // Resource-depletion bar smoothing (task-108 Phase 5). Persists the
        // last-rendered fill ratio per resource-node entity so the amber bar
        // lerps toward the simulation's discrete tick value instead of
        // snapping. Renderer-only state — never written to the ECS world, so
        // multiplayer determinism is unaffected.
        private readonly Dictionary<Entity, float> _lastFill = new();
        // Stale-entry pruning happens once every N frames (cheap walk).
        private int _pruneFrameCounter;
        private const int PruneEveryNFrames = 180;  // ~3s at 60fps

        // Transparent background for the depletion bar — no visible track,
        // just outline + amber fill per design spec §6.
        private static readonly Color TransparentBg = new Color(0f, 0f, 0f, 0f);

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
            if (hovered != Entity.Null && _em.Exists(hovered) && HasDrawableBar(hovered))
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
                    if (!HasDrawableBar(e)) continue;

                    DrawBarForEntity(cam, e, isSelected: true);
                    _drawn.Add(e);
                }
            }

            // Hide pool items that weren't claimed this frame.
            for (int i = _activeCount; i < _pool.Count; i++)
                _pool[i].SetActive(false);

            // Periodic stale-entry prune for the depletion-bar lerp cache —
            // resource nodes get destroyed when depleted, and we don't want
            // _lastFill to grow forever.
            if (++_pruneFrameCounter >= PruneEveryNFrames)
            {
                _pruneFrameCounter = 0;
                PruneStaleFillEntries();
            }
        }

        private void PruneStaleFillEntries()
        {
            if (_lastFill.Count == 0) return;
            // Two-pass: collect stale keys, then remove. Avoids mutating the
            // dictionary while enumerating.
            List<Entity> stale = null;
            foreach (var kvp in _lastFill)
            {
                var e = kvp.Key;
                if (!_em.Exists(e) ||
                    (!_em.HasComponent<IronMineTag>(e) && !_em.HasComponent<VeilstoneOutcroppingTag>(e)
                     && !_em.HasComponent<VeilsteelDepositTag>(e)))
                {
                    (stale ??= new List<Entity>()).Add(e);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                    _lastFill.Remove(stale[i]);
            }
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

        private bool HasDrawableBar(Entity e)
        {
            // Resource nodes draw an amber depletion bar instead of a Health bar
            // and don't carry the Health component, so the standard Health gate
            // must let them through.
            if (_em.HasComponent<Health>(e)) return true;
            if (_em.HasComponent<IronMineTag>(e)) return true;
            if (_em.HasComponent<VeilstoneOutcroppingTag>(e)) return true;
            if (_em.HasComponent<VeilsteelDepositTag>(e)) return true;
            return false;
        }

        private void DrawBarForEntity(Camera cam, Entity e, bool isSelected = false, bool isHovered = false)
        {
            if (!_em.HasComponent<LocalTransform>(e)) return;

            // Resource nodes (iron deposits, veilstone outcroppings) render an amber
            // depletion bar instead of the standard Health bar. They don't carry
            // Health at all — HasDrawableBar lets them through the gate above.
            bool isIronDeposit = _em.HasComponent<IronMineTag>(e);
            // Veilsteel nodes share IronDepositState, so they take the iron path.
            bool isVeilsteelDeposit = _em.HasComponent<VeilsteelDepositTag>(e);
            bool isVeilstoneOutcropping     = _em.HasComponent<VeilstoneOutcroppingTag>(e);
            if (isIronDeposit || isVeilsteelDeposit || isVeilstoneOutcropping)
            {
                DrawResourceDepletionBar(cam, e, isIronDeposit || isVeilsteelDeposit);
                return;
            }

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

        /// <summary>
        /// Renders a single amber bar above a resource node showing
        /// remaining / initial. No background track (transparent fill behind
        /// the amber portion), just the standard dark outline + amber fill —
        /// visually distinct from the green/amber/red health bar which sits
        /// on a dark track.
        /// </summary>
        private void DrawResourceDepletionBar(Camera cam, Entity e, bool isIron)
        {
            int remaining, max;
            if (isIron)
            {
                var s = _em.GetComponentData<IronDepositState>(e);
                remaining = s.RemainingIron;
                // Pre-task-108 saves may carry InitialIron == 0; fall back to
                // RemainingIron so the bar renders full instead of empty.
                max = s.InitialIron > 0 ? s.InitialIron : remaining;
            }
            else
            {
                var s = _em.GetComponentData<VeilstoneOutcroppingState>(e);
                remaining = s.RemainingVeilstone;
                max = s.MaxVeilstone > 0 ? s.MaxVeilstone : remaining;
            }

            float targetFill = max > 0 ? Mathf.Clamp01((float)remaining / max) : 0f;

            // Snap to the simulation's live value. An earlier draft lerped
            // toward targetFill at 3.0/sec for visual smoothing, but the cache
            // only updates when the entity is hovered/selected — so re-hover
            // after a long gap played a visible catch-up animation as the bar
            // chased the now-stale cached value down to truth. Snapping reads
            // the live RemainingIron/InitialIron each draw; mining tick
            // changes are small enough per frame that no smoothing is needed.
            float lerpedFill = targetFill;
            _lastFill[e] = targetFill;

            var pos = _em.GetComponentData<LocalTransform>(e).Position;
            // Resource nodes are world-objects (closer to buildings than units)
            // so use the building y-offset for headroom above the model.
            float yOff = buildingYOffset;
            Vector3 worldPos = new Vector3(pos.x, pos.y + yOff, pos.z);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0) return;  // behind camera

            Color fillColor = WorldOverlayPalette.ResourceDepletion;

            var bar = GetOrAllocate(_activeCount++);
            bar.SetGeometry(screenPos.x, screenPos.y, barWidth, barHeight, barBorder);
            // Transparent background = no visible track, just the dark border
            // outline + the amber fill portion.
            bar.SetColors(TransparentBg, BorderColor, fillColor);
            bar.SetFill(lerpedFill);
            bar.SetActive(true);
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
