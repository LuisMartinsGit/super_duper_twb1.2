using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.EventSystems;
using TheWaningBorder.Input;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.Core.Commands;

namespace TheWaningBorder.World.Minimap
{
    /// <summary>
    /// Flat (no cameras) FoW-aware minimap rendered into a Texture2D and shown in the lower-right UI.
    /// Blips are colored per-faction using FactionColors.Get(faction).
    /// - Enemy/neutral UNITS: drawn only when VISIBLE.
    /// - Enemy/neutral BUILDINGS: drawn when VISIBLE (solid) or REVEALED (ghost).
    /// - Player-owned always drawn (solid).
    ///
    /// Features:
    /// - Ground texture background sampled from terrain height and slope.
    /// - White rectangle showing the main camera's ground footprint on the minimap.
    /// - Left-click anywhere on the minimap to snap the camera there.
    /// - Right-click on the minimap to issue move orders to selected units.
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public sealed class MinimapRenderer : MonoBehaviour
    {
        [Header("Placement")]
        public int sizePixels = 256;
        public Vector2 offsetBR = new Vector2(20, 20);

        [Header("Map")]
        public Vector2 worldMin = new Vector2(-125, -125);
        public Vector2 worldMax = new Vector2(125, 125);
        public int samples = 128;

        [Header("FOW + Factions")]
        public Faction humanFaction = Faction.Blue;

        [Header("Ground Colors")]
        public Color colGrass = new Color(0.30f, 0.50f, 0.18f, 1f);
        public Color colDirt = new Color(0.45f, 0.35f, 0.20f, 1f);
        public Color colSand = new Color(0.70f, 0.62f, 0.42f, 1f);
        public Color colRock = new Color(0.40f, 0.38f, 0.35f, 1f);
        public Color colSnow = new Color(0.90f, 0.92f, 0.95f, 1f);
        public Color colWater = new Color(0.10f, 0.15f, 0.25f, 1f);

        [Header("Blip Radii")]
        public int unitRadiusPx = 2;
        public int buildingRadiusPx = 3;

        [Header("Update")]
        public float refreshInterval = 0.1f;

        [Header("Camera Snap on Click")]
        public bool logClicks = false;

        // UI
        private RawImage _raw;
        private RectTransform _rawRect;
        private Texture2D _tex;

        // Camera view lines (4 edges)
        private Image[] _viewLines;

        // Buffers
        private Color[] _bgBuffer;
        private Color[] _frame;
        private bool _bgBuilt;

        // ECS
        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _unitsQ;
        private EntityQuery _buildingsQ;
        private EntityQuery _obstaclesQ;
        private EntityQuery _ironDepositsQ;

        // FoW
        private FogOfWarManager _fow;

        // Terrain
        private ProceduralTerrain _terrain;
        private int _noiseOffsetX;
        private int _noiseOffsetY;

        private float _timer;

        void Awake()
        {
            // Ensure EventSystem for click handling
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.hideFlags = HideFlags.DontSave;
            }

            _fow = FindObjectOfType<FogOfWarManager>();
            if (_fow != null)
            {
                worldMin = _fow.WorldMin;
                worldMax = _fow.WorldMax;
                humanFaction = _fow.HumanFaction;
            }
            else
            {
                // Adapt to map size when FoW is disabled
                int half = GameSettings.MapHalfSize;
                worldMin = new Vector2(-half, -half);
                worldMax = new Vector2(half, half);
            }

            if (GameSettings.IsMultiplayer)
            {
                humanFaction = GameSettings.LocalPlayerFaction;
            }

            _terrain = ProceduralTerrain.Instance;
            _noiseOffsetX = GameSettings.SpawnSeed % 10000;
            _noiseOffsetY = (GameSettings.SpawnSeed * 7) % 10000;

            samples = Mathf.Clamp(samples, 64, Mathf.Min(512, sizePixels));

            _tex = new Texture2D(sizePixels, sizePixels, TextureFormat.RGBA32, false, false);
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Point;

            EnsureCanvasAndImage();

            _bgBuffer = new Color[samples * samples];
            _frame = new Color[sizePixels * sizePixels];

            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            _em = _world.EntityManager;

            _unitsQ = _em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            _buildingsQ = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            _obstaclesQ = _em.CreateEntityQuery(
                ComponentType.ReadOnly<ObstacleTag>(),
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>());

            _ironDepositsQ = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TheWaningBorder.AI.IronMineTag>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;

            // Build ground texture once (terrain doesn't change)
            if (!_bgBuilt)
            {
                BuildGroundBackground();
                _bgBuilt = true;
            }

            BlitBackgroundToFrame();

            // Apply FoW dimming on top of ground texture (only if FoW enabled)
            if (GameSettings.FogOfWarEnabled)
                ApplyFogOfWarDimming();

            DrawBlips();

            _tex.SetPixels(_frame);
            _tex.Apply(false, false);
        }

        void LateUpdate()
        {
            if (_fow != null)
            {
                worldMin = _fow.WorldMin;
                worldMax = _fow.WorldMax;
                humanFaction = _fow.HumanFaction;
            }

            if (GameSettings.IsMultiplayer)
            {
                humanFaction = GameSettings.LocalPlayerFaction;
            }

            UpdateCameraViewRect();
        }

        #region Ground Texture Background

        /// <summary>
        /// Build the minimap background from terrain height and slope data.
        /// Colors match the splatmap zones: water, beach, grass, dirt, rock, snow.
        /// </summary>
        private void BuildGroundBackground()
        {
            float minX = worldMin.x, minZ = worldMin.y;
            float maxX = worldMax.x, maxZ = worldMax.y;

            if (_terrain == null)
                _terrain = ProceduralTerrain.Instance;

            for (int y = 0; y < samples; y++)
            {
                float vz = Mathf.Lerp(minZ, maxZ, (y + 0.5f) / samples);
                for (int x = 0; x < samples; x++)
                {
                    float vx = Mathf.Lerp(minX, maxX, (x + 0.5f) / samples);
                    _bgBuffer[y * samples + x] = SampleGroundColor(vx, vz, x, y);
                }
            }
        }

        /// <summary>
        /// Sample the ground color at a world position using terrain height and slope.
        /// </summary>
        private Color SampleGroundColor(float worldX, float worldZ, int sampleX, int sampleY)
        {
            float waterLevel = _terrain != null ? _terrain.waterHeight : 20f;

            // Sample terrain height
            float h = TerrainUtility.GetHeight(worldX, worldZ);

            // Estimate slope from neighboring height samples
            float step = (worldMax.x - worldMin.x) / samples;
            float hL = TerrainUtility.GetHeight(worldX - step, worldZ);
            float hR = TerrainUtility.GetHeight(worldX + step, worldZ);
            float hD = TerrainUtility.GetHeight(worldX, worldZ - step);
            float hU = TerrainUtility.GetHeight(worldX, worldZ + step);
            float dX = (hR - hL) / (step * 2f);
            float dZ = (hU - hD) / (step * 2f);
            float slope = Mathf.Sqrt(dX * dX + dZ * dZ);
            // Normalize slope roughly (0 = flat, ~1 = very steep)
            slope = Mathf.Clamp01(slope / 3f);

            // Normalized UV for noise
            float u = (float)sampleX / samples;
            float v = (float)sampleY / samples;

            // === WATER ===
            if (h < waterLevel)
            {
                // Shallow water near shore: sandy transition
                float shallowZone = waterLevel - h;
                if (shallowZone < 0.5f)
                {
                    float t = shallowZone / 0.5f;
                    return Color.Lerp(colSand, colWater, t);
                }
                return colWater;
            }

            // === BEACH ===
            if (h < waterLevel + 3f)
            {
                float beachT = Mathf.InverseLerp(waterLevel, waterLevel + 3f, h);
                beachT = beachT * beachT; // ease-in
                return Color.Lerp(colSand, colGrass, beachT);
            }

            // === LAND — color by height + slope ===
            Color ground;

            if (h < 40f)
            {
                // Low land — green grass with woodland patches
                ground = colGrass;

                float woodNoise = Mathf.PerlinNoise(u * 8f + _noiseOffsetX, v * 8f + _noiseOffsetY);
                float patchNoise = Mathf.PerlinNoise(u * 20f + _noiseOffsetX * 0.5f, v * 20f + _noiseOffsetY * 0.5f);

                if (woodNoise > 0.5f)
                {
                    float amount = (woodNoise - 0.5f) * 2f;
                    ground = Color.Lerp(ground, colDirt, amount * 0.5f);
                }
                if (patchNoise > 0.7f)
                {
                    float amount = (patchNoise - 0.7f) * 3.3f;
                    ground = Color.Lerp(ground, colDirt, amount * 0.3f);
                }
            }
            else if (h < 55f)
            {
                // Mid land (hills) — grass/dirt blend transitioning to rock
                float hillT = Mathf.InverseLerp(40f, 55f, h);
                ground = Color.Lerp(colGrass, colDirt, hillT * 0.6f);

                // Woodland patches
                float woodNoise = Mathf.PerlinNoise(u * 8f + _noiseOffsetX, v * 8f + _noiseOffsetY);
                if (woodNoise > 0.45f)
                {
                    float amount = (woodNoise - 0.45f) * 2f;
                    ground = Color.Lerp(ground, colDirt, amount * 0.3f);
                }
            }
            else if (h < 70f)
            {
                // High land — rock/dirt blend
                float highT = Mathf.InverseLerp(55f, 70f, h);
                ground = Color.Lerp(colDirt, colRock, highT);
            }
            else
            {
                // Mountain peaks — rock with snow tint
                float snowT = Mathf.InverseLerp(70f, 85f, h);
                ground = Color.Lerp(colRock, colSnow, snowT * 0.7f);
            }

            // Steep slopes get rocky override
            if (slope > 0.25f)
            {
                float cliffT = Mathf.InverseLerp(0.25f, 0.6f, slope);
                cliffT = Mathf.Clamp01(cliffT);
                ground = Color.Lerp(ground, colRock, cliffT * 0.7f);
            }

            return ground;
        }

        private void BlitBackgroundToFrame()
        {
            int W = sizePixels, H = sizePixels, S = samples;
            for (int y = 0; y < H; y++)
            {
                int sy = (int)((y / (float)H) * S);
                if (sy >= S) sy = S - 1;
                for (int x = 0; x < W; x++)
                {
                    int sx = (int)((x / (float)W) * S);
                    if (sx >= S) sx = S - 1;
                    _frame[y * W + x] = _bgBuffer[sy * S + sx];
                }
            }
        }

        /// <summary>
        /// Dim areas not visible or revealed by fog of war.
        /// </summary>
        private void ApplyFogOfWarDimming()
        {
            float minX = worldMin.x, minZ = worldMin.y;
            float maxX = worldMax.x, maxZ = worldMax.y;
            int W = sizePixels;

            for (int y = 0; y < sizePixels; y++)
            {
                float vz = Mathf.Lerp(minZ, maxZ, (y + 0.5f) / sizePixels);
                for (int x = 0; x < sizePixels; x++)
                {
                    float vx = Mathf.Lerp(minX, maxX, (x + 0.5f) / sizePixels);
                    float3 pos = new float3(vx, 0f, vz);

                    bool vis = FogOfWarSystem.IsVisibleToFaction(humanFaction, pos);
                    if (vis) continue;

                    bool rev = FogOfWarSystem.IsRevealedToFaction(humanFaction, pos);
                    int idx = y * W + x;
                    Color c = _frame[idx];

                    if (rev)
                    {
                        c.r *= 0.5f; c.g *= 0.5f; c.b *= 0.5f;
                    }
                    else
                    {
                        c.r *= 0.15f; c.g *= 0.15f; c.b *= 0.15f;
                    }

                    _frame[idx] = c;
                }
            }
        }

        #endregion

        #region Blips

        private void DrawBlips()
        {
            using (var ents = _unitsQ.ToEntityArray(Allocator.Temp))
            using (var facs = _unitsQ.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = _unitsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var pos = xfs[i].Position;
                    Faction fac = facs[i].Value;
                    bool mine = fac == humanFaction;

                    bool show = mine || FogOfWarSystem.IsVisibleToFaction(humanFaction, pos);
                    if (!show) continue;

                    Color c = FactionColors.Get(fac);
                    int2 p = WorldToPixel(pos);
                    DrawDisc(p.x, p.y, unitRadiusPx, c);
                }
            }

            using (var ents = _buildingsQ.ToEntityArray(Allocator.Temp))
            using (var facs = _buildingsQ.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = _buildingsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var pos = xfs[i].Position;
                    Faction fac = facs[i].Value;
                    bool mine = fac == humanFaction;

                    bool vis = FogOfWarSystem.IsVisibleToFaction(humanFaction, pos);
                    bool rev = vis || FogOfWarSystem.IsRevealedToFaction(humanFaction, pos);
                    if (!mine && !rev) continue;

                    Color baseCol = FactionColors.Get(fac);
                    Color c = vis ? baseCol : FactionColors.Ghost(baseCol, 0.5f);
                    int2 p = WorldToPixel(pos);
                    DrawDisc(p.x, p.y, buildingRadiusPx, c);
                }
            }

            // Draw obstacle blips (forests = dark green, rocks = grey) — always visible
            using (var xfs = _obstaclesQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var pids = _obstaclesQ.ToComponentDataArray<PresentationId>(Allocator.Temp))
            {
                Color forestColor = new Color(0.12f, 0.30f, 0.08f);
                Color rockColor = new Color(0.38f, 0.36f, 0.33f);

                for (int i = 0; i < xfs.Length; i++)
                {
                    var pos = xfs[i].Position;
                    Color c = pids[i].Id == 400 ? forestColor : rockColor;
                    int radius = pids[i].Id == 400 ? 3 : 2;
                    int2 p = WorldToPixel(pos);
                    DrawDisc(p.x, p.y, radius, c);
                }
            }

            // Draw iron deposit blips (rusty orange) — always visible
            using (var xfs = _ironDepositsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                Color ironColor = new Color(0.55f, 0.32f, 0.15f);

                for (int i = 0; i < xfs.Length; i++)
                {
                    var pos = xfs[i].Position;
                    int2 p = WorldToPixel(pos);
                    DrawDisc(p.x, p.y, 2, ironColor);
                }
            }
        }

        private int2 WorldToPixel(float3 pos)
        {
            float u = Mathf.InverseLerp(worldMin.x, worldMax.x, pos.x);
            float v = Mathf.InverseLerp(worldMin.y, worldMax.y, pos.z);
            int px = Mathf.Clamp(Mathf.FloorToInt(u * sizePixels), 0, sizePixels - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(v * sizePixels), 0, sizePixels - 1);
            return new int2(px, py);
        }

        private void DrawDisc(int cx, int cy, int r, Color col)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int yy = cy + dy;
                if (yy < 0 || yy >= sizePixels) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = cx + dx;
                    if (xx < 0 || xx >= sizePixels) continue;
                    if (dx * dx + dy * dy <= r2)
                        _frame[yy * sizePixels + xx] = col;
                }
            }
        }

        #endregion

        #region Camera View Rectangle

        private void UpdateCameraViewRect()
        {
            if (_viewLines == null || _rawRect == null) return;
            var main = Camera.main;
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

        private Vector2 WorldToMinimapPixel(Vector3 worldPos)
        {
            float u = Mathf.InverseLerp(worldMin.x, worldMax.x, worldPos.x);
            float v = Mathf.InverseLerp(worldMin.y, worldMax.y, worldPos.z);

            float w = _rawRect.rect.width;
            float h = _rawRect.rect.height;

            float pixelX = -(w - u * w);
            float pixelY = v * h;

            return new Vector2(pixelX, pixelY);
        }

        private void DrawLine(int lineIndex, Vector2 start, Vector2 end)
        {
            if (lineIndex < 0 || lineIndex >= _viewLines.Length) return;

            var lineRect = _viewLines[lineIndex].rectTransform;
            Vector2 diff = end - start;
            float length = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            lineRect.anchoredPosition = start;
            lineRect.sizeDelta = new Vector2(length, 2f);
            lineRect.localRotation = Quaternion.Euler(0, 0, angle);
        }

        private static Vector3 RayToGround(Camera cam, Vector2 viewport01)
        {
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            Ray r = cam.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0f));
            if (ground.Raycast(r, out float t)) return r.GetPoint(t);
            Vector3 p = r.origin + r.direction * 1000f;
            return new Vector3(p.x, 0f, p.z);
        }

        #endregion

        #region Click Handling

        /// <summary>
        /// Convert a screen-space click to world coordinates on the minimap.
        /// </summary>
        private bool TryGetWorldPosition(PointerEventData eventData, out float worldX, out float worldZ)
        {
            worldX = 0f;
            worldZ = 0f;
            if (_rawRect == null) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rawRect, eventData.position, eventData.pressEventCamera, out Vector2 local);

            float w = _rawRect.rect.width;
            float h = _rawRect.rect.height;

            float u = (local.x + w) / w;
            float v = local.y / h;

            worldX = Mathf.Lerp(worldMin.x, worldMax.x, u);
            worldZ = Mathf.Lerp(worldMin.y, worldMax.y, v);

            worldX = Mathf.Clamp(worldX, worldMin.x, worldMax.x);
            worldZ = Mathf.Clamp(worldZ, worldMin.y, worldMax.y);

            return true;
        }

        /// <summary>
        /// Handle left-click: center camera on clicked position.
        /// </summary>
        internal void HandleLeftClick(PointerEventData eventData)
        {
            if (!TryGetWorldPosition(eventData, out float worldX, out float worldZ)) return;

            if (logClicks)
                Debug.Log($"[Minimap] Left Click -> Camera({worldX:F1}, {worldZ:F1})");

            GameCamera.FocusOn(new Vector3(worldX, 0, worldZ), instant: true);
        }

        /// <summary>
        /// Handle right-click: issue move orders to selected units at the clicked world position.
        /// </summary>
        internal void HandleRightClick(PointerEventData eventData)
        {
            if (!TryGetWorldPosition(eventData, out float worldX, out float worldZ)) return;

            if (logClicks)
                Debug.Log($"[Minimap] Right Click -> Move({worldX:F1}, {worldZ:F1})");

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            float3 destination = new float3(worldX, TerrainUtility.GetHeight(worldX, worldZ), worldZ);

            foreach (var entity in selection)
            {
                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<UnitTag>(entity)) continue;
                if (!_em.HasComponent<FactionTag>(entity)) continue;

                var fac = _em.GetComponentData<FactionTag>(entity);
                if (fac.Value != humanFaction) continue;

                CommandRouter.IssueMove(_em, entity, destination);
            }
        }

        /// <summary>
        /// Legacy API - kept for backward compatibility.
        /// </summary>
        internal void HandleClick(PointerEventData eventData)
        {
            HandleLeftClick(eventData);
        }

        #endregion

        #region UI Setup

        private void EnsureCanvasAndImage()
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var cGo = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = cGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
            }

            var rawGo = new GameObject("MinimapRaw", typeof(RawImage));
            rawGo.transform.SetParent(canvas.transform, false);

            _raw = rawGo.GetComponent<RawImage>();
            _raw.texture = _tex;

            _rawRect = _raw.rectTransform;
            _rawRect.anchorMin = new Vector2(1, 0);
            _rawRect.anchorMax = new Vector2(1, 0);
            _rawRect.pivot = new Vector2(1, 0);
            _rawRect.anchoredPosition = new Vector2(-offsetBR.x, offsetBR.y);
            _rawRect.sizeDelta = new Vector2(sizePixels, sizePixels);

            // Add click handler that supports left + right clicks
            var proxy = rawGo.AddComponent<MinimapClickProxy>();
            proxy.minimap = this;

            // Create view lines for camera rectangle
            _viewLines = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var lineGo = new GameObject($"ViewLine{i}", typeof(Image));
                lineGo.transform.SetParent(_rawRect, false);

                var lineImg = lineGo.GetComponent<Image>();
                lineImg.color = Color.white;
                lineImg.raycastTarget = false;

                var lineRect = lineImg.rectTransform;
                lineRect.anchorMin = new Vector2(1, 0);
                lineRect.anchorMax = new Vector2(1, 0);
                lineRect.pivot = new Vector2(0, 0.5f);

                _viewLines[i] = lineImg;
            }
        }

        #endregion
    }

    /// <summary>
    /// Proxy component to forward UI clicks to the minimap.
    /// Supports left-click (camera snap) and right-click (move orders).
    /// </summary>
    public class MinimapClickProxy : MonoBehaviour, IPointerClickHandler
    {
        public MinimapRenderer minimap;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (minimap == null) return;

            if (eventData.button == PointerEventData.InputButton.Right)
                minimap.HandleRightClick(eventData);
            else
                minimap.HandleLeftClick(eventData);
        }
    }
}
