// File: Assets/Scripts/UI/HUD/MinimapUI.cs
// Flat FoW-aware minimap with camera view rectangle and click-to-move

using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.EventSystems;
using TheWaningBorder.Input;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.Systems.Visibility;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Flat (no cameras) FoW-aware minimap rendered into a Texture2D.
    /// 
    /// Features:
    /// - Blips colored per-faction using FactionColors
    /// - Enemy units visible only when in line of sight
    /// - Enemy buildings visible when revealed (ghost) or visible (solid)
    /// - White rectangle showing camera frustum
    /// - Click to move camera
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public sealed class MinimapUI : MonoBehaviour
    {
        [Header("Placement")]
        public int sizePixels = 256;
        public Vector2 offsetBR = new Vector2(20, 20);

        [Header("Map Bounds")]
        public Vector2 worldMin = new Vector2(-125, -125);
        public Vector2 worldMax = new Vector2(125, 125);
        public int samples = 128;

        [Header("FOW + Factions")]
        public Faction humanFaction = Faction.Blue;

        [Header("Background Colors")]
        public Color colUnseen = new Color(0f, 0f, 0f, 1f);
        public Color colRevealed = new Color(0.18f, 0.18f, 0.18f, 1f);
        public Color colVisible = new Color(0.35f, 0.35f, 0.35f, 1f);

        [Header("Blip Radii")]
        public int unitRadiusPx = 2;
        public int buildingRadiusPx = 3;

        [Header("Update")]
        public float refreshInterval = 0.1f;

        [Header("Camera Snap on Click")]
        public RTSCameraRig cameraRig;
        public bool logClicks = false;

        // Fallback: direct CameraController reference (in case RTSCameraRig not in scene)
        private CameraController _cameraController;

        // Fix #222: cached Camera.main reference
        private Camera _cachedCamera;

        // UI
        private RawImage _raw;
        private RectTransform _rawRect;
        private Texture2D _tex;
        private Image[] _viewLines;

        // Buffers
        private Color[] _bgBuffer;
        private Color[] _frame;

        // ECS
        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _unitsQ;
        private EntityQuery _buildingsQ;

        // FoW
        private FogOfWarManager _fow;
        private float _timer;

        void Awake()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.hideFlags = HideFlags.DontSave;
            }

            _fow = FindFirstObjectByType<FogOfWarManager>();
            if (_fow != null)
            {
                worldMin = _fow.WorldMin;
                worldMax = _fow.WorldMax;
                humanFaction = _fow.HumanFaction;
            }

            if (GameSettings.IsMultiplayer)
                humanFaction = GameSettings.LocalPlayerFaction;
        }

        void Start()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
            {
                _em = _world.EntityManager;
                _unitsQ = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>());

                _buildingsQ = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<BuildingTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>());
            }

            CreateUI();
            _bgBuffer = new Color[samples * samples];
            _frame = new Color[samples * samples];
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= refreshInterval)
            {
                _timer = 0f;
                RenderFrame();
            }

            UpdateCameraViewRect();
        }

        private void CreateUI()
        {
            // Create canvas
            var canvasGo = new GameObject("MinimapCanvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Create minimap image
            var rawGo = new GameObject("MinimapImage");
            rawGo.transform.SetParent(canvasGo.transform, false);

            _raw = rawGo.AddComponent<RawImage>();
            _rawRect = _raw.rectTransform;
            _rawRect.anchorMin = new Vector2(1, 0);
            _rawRect.anchorMax = new Vector2(1, 0);
            _rawRect.pivot = new Vector2(1, 0);
            _rawRect.sizeDelta = new Vector2(sizePixels, sizePixels);
            _rawRect.anchoredPosition = new Vector2(-offsetBR.x, offsetBR.y);

            _tex = new Texture2D(samples, samples, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _raw.texture = _tex;

            // Golden border frame around minimap
            var borderGo = new GameObject("MinimapBorder", typeof(Image));
            borderGo.transform.SetParent(_rawRect, false);
            var borderImage = borderGo.GetComponent<Image>();
            borderImage.color = new Color(0.83f, 0.66f, 0.26f, 0.9f);
            borderImage.raycastTarget = false;
            var borderRt = borderImage.rectTransform;
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = new Vector2(-3, -3);
            borderRt.offsetMax = new Vector2(3, 3);
            borderGo.transform.SetAsFirstSibling();

            // Add click handler
            var proxy = rawGo.AddComponent<MinimapClickProxy>();
            proxy.owner = this;

            // Create camera view lines
            CreateViewLines(canvasGo.transform);
        }

        private void CreateViewLines(Transform parent)
        {
            _viewLines = new Image[4];

            for (int i = 0; i < 4; i++)
            {
                var lineGo = new GameObject($"ViewLine_{i}");
                lineGo.transform.SetParent(_rawRect, false);

                var lineImg = lineGo.AddComponent<Image>();
                lineImg.color = new Color(1f, 1f, 1f, 0.8f);

                var lineRect = lineImg.rectTransform;
                lineRect.sizeDelta = new Vector2(2f, 0f);
                lineRect.anchorMin = new Vector2(1, 0);
                lineRect.anchorMax = new Vector2(1, 0);
                lineRect.pivot = new Vector2(0, 0.5f);

                _viewLines[i] = lineImg;
            }
        }

        private void RenderFrame()
        {
            if (_world == null || !_world.IsCreated) return;
            if (_fow == null) _fow = FindFirstObjectByType<FogOfWarManager>();

            // Fill background based on fog state
            for (int y = 0; y < samples; y++)
            {
                for (int x = 0; x < samples; x++)
                {
                    float wx = Mathf.Lerp(worldMin.x, worldMax.x, x / (float)samples);
                    float wz = Mathf.Lerp(worldMin.y, worldMax.y, y / (float)samples);
                    var pos = new float3(wx, 0, wz);

                    Color col = colUnseen;
                    if (_fow != null)
                    {
                        if (FogOfWarSystem.IsVisibleToFaction(humanFaction, pos))
                            col = colVisible;
                        else if (FogOfWarSystem.IsRevealedToFaction(humanFaction, pos))
                            col = colRevealed;
                    }
                    else
                    {
                        col = colVisible;
                    }

                    _bgBuffer[y * samples + x] = col;
                }
            }

            System.Array.Copy(_bgBuffer, _frame, _bgBuffer.Length);

            // Draw units
            DrawEntities(_unitsQ, unitRadiusPx, isUnit: true);
            // Draw buildings
            DrawEntities(_buildingsQ, buildingRadiusPx, isUnit: false);

            _tex.SetPixels(_frame);
            _tex.Apply();
        }

        private void DrawEntities(EntityQuery query, int radius, bool isUnit)
        {
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var pos = transforms[i].Position;
                var fac = factions[i].Value;

                // Visibility check
                bool mine = (fac == humanFaction);
                if (!mine)
                {
                    if (isUnit)
                    {
                        if (!FogOfWarSystem.IsVisibleToFaction(humanFaction, pos))
                            continue;
                    }
                    else
                    {
                        if (!FogOfWarSystem.IsRevealedToFaction(humanFaction, pos))
                            continue;
                    }
                }

                // World to pixel
                float u = (pos.x - worldMin.x) / (worldMax.x - worldMin.x);
                float v = (pos.z - worldMin.y) / (worldMax.y - worldMin.y);
                int px = Mathf.RoundToInt(u * samples);
                int py = Mathf.RoundToInt(v * samples);

                Color blipColor = FactionColors.Get(fac);

                // Ghost for revealed but not visible buildings
                if (!mine && !isUnit && !FogOfWarSystem.IsVisibleToFaction(humanFaction, pos))
                    blipColor.a = 0.5f;

                DrawBlip(px, py, radius, blipColor);
            }
        }

        private void DrawBlip(int cx, int cy, int r, Color color)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= samples || y < 0 || y >= samples) continue;
                    if (dx * dx + dy * dy > r * r) continue;

                    _frame[y * samples + x] = color;
                }
            }
        }

        private void UpdateCameraViewRect()
        {
            if (_viewLines == null || _rawRect == null) return;
            // Fix #222: cache Camera.main reference
            var main = _cachedCamera != null ? _cachedCamera : (_cachedCamera = Camera.main);
            if (!main) return;

            Vector3 p00 = RayToGround(main, new Vector2(0f, 0f));
            Vector3 p10 = RayToGround(main, new Vector2(1f, 0f));
            Vector3 p11 = RayToGround(main, new Vector2(1f, 1f));
            Vector3 p01 = RayToGround(main, new Vector2(0f, 1f));

            Vector2 px00 = WorldToMinimapPixel(p00);
            Vector2 px10 = WorldToMinimapPixel(p10);
            Vector2 px11 = WorldToMinimapPixel(p11);
            Vector2 px01 = WorldToMinimapPixel(p01);

            DrawLine(0, px00, px10);
            DrawLine(1, px10, px11);
            DrawLine(2, px11, px01);
            DrawLine(3, px01, px00);
        }

        private Vector3 RayToGround(Camera cam, Vector2 viewport)
        {
            var ray = cam.ViewportPointToRay(viewport);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float dist))
                return ray.GetPoint(dist);
            return Vector3.zero;
        }

        private Vector2 WorldToMinimapPixel(Vector3 worldPos)
        {
            float u = (worldPos.x - worldMin.x) / (worldMax.x - worldMin.x);
            float v = (worldPos.z - worldMin.y) / (worldMax.y - worldMin.y);
            return new Vector2(u * sizePixels, v * sizePixels);
        }

        private void DrawLine(int index, Vector2 from, Vector2 to)
        {
            var line = _viewLines[index];
            var rect = line.rectTransform;

            Vector2 dir = to - from;
            float length = dir.magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            rect.anchoredPosition = from - new Vector2(sizePixels, 0);
            rect.sizeDelta = new Vector2(length, 2f);
            rect.localRotation = Quaternion.Euler(0, 0, angle);
        }

        internal void HandleClick(PointerEventData eventData)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(_rawRect, eventData.position, eventData.pressEventCamera))
                return;

            // Find camera controller (try RTSCameraRig first, fallback to CameraController)
            if (cameraRig == null && _cameraController == null)
            {
                cameraRig = FindFirstObjectByType<RTSCameraRig>();
                if (cameraRig != null)
                {
                    worldMin = cameraRig.worldMin;
                    worldMax = cameraRig.worldMax;
                }
                else
                {
                    _cameraController = FindFirstObjectByType<CameraController>();
                    if (_cameraController != null)
                    {
                        worldMin = _cameraController.worldMin;
                        worldMax = _cameraController.worldMax;
                    }
                }
            }

            if (cameraRig == null && _cameraController == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rawRect, eventData.position, eventData.pressEventCamera, out Vector2 local);

            Rect rect = _rawRect.rect;
            Vector2 bottomLeftPx = local + Vector2.Scale(rect.size, _rawRect.pivot);
            float u = Mathf.Clamp01(bottomLeftPx.x / Mathf.Max(1e-6f, rect.width));
            float v = Mathf.Clamp01(bottomLeftPx.y / Mathf.Max(1e-6f, rect.height));

            float targetX = Mathf.Lerp(worldMin.x, worldMax.x, u);
            float targetZ = Mathf.Lerp(worldMin.y, worldMax.y, v);

            var targetPos = new Vector3(targetX, 0f, targetZ);

            // Earlier this was a missing-brace bug: the `_cameraController` call
            // ran unconditionally after the if, NRE'ing whenever cameraRig was
            // the active controller (since _cameraController stays null in that
            // path). The audit (task-062 Q-53) misread the path as dead code.
            if (cameraRig != null)
                cameraRig.MoveToPosition(targetPos, instant: false);
            else
                _cameraController.MoveToPositionSmooth(targetPos, 0.5f);
        }

        private sealed class MinimapClickProxy : MonoBehaviour, IPointerClickHandler
        {
            public MinimapUI owner;
            public void OnPointerClick(PointerEventData eventData) => owner?.HandleClick(eventData);
        }
    }
}