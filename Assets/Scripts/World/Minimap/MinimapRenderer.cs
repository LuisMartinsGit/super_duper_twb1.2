using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.EventSystems;
using TheWaningBorder.Input;
using TheWaningBorder.UI.Common;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.Bootstrap;
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
        // Static overrides applied during Awake. GameBootstrap sets these
        // before AddComponent<MinimapRenderer>() to inscribe the legacy
        // minimap inside the web HUD's diamond frame. nulled = use defaults.
        public static int? OverrideSizePixels;
        public static Vector2? OverrideOffsetBR;
        public static int? OverrideCanvasSortingOrder;
        // When set, EnsureCanvasAndImage always makes its own canvas rather
        // than re-parenting to the first canvas it finds (which can be the
        // CEF browser's canvas when the web HUD is active).
        public static bool ForceDedicatedCanvas;

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
        // Stamped over any cell PassabilityGrid considers TerrainBlocked so
        // the player can see at a glance where units can't path. The 0.7
        // blend means biome colour still shows through faintly — readable
        // as "rocky impassable mountain" or "deep water" rather than a
        // solid grey shape that hides the underlying terrain.
        [Header("Passability Overlay")]
        public Color colImpassable = new Color(0.18f, 0.16f, 0.14f, 1f);
        [Range(0f, 1f)] public float impassableBlend = 0.7f;

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
        private EntityQuery _ritualSitesQ;
        private EntityQuery _glowPickupsQ;

        // FoW
        private FogOfWarManager _fow;

        // Terrain
        private ProceduralTerrain _terrain;
        private int _noiseOffsetX;
        private int _noiseOffsetY;

        private float _timer;

        void Awake()
        {
            // Apply runtime overrides (web HUD inscribes the legacy minimap
            // inside its diamond frame; sets these before AddComponent).
            if (OverrideSizePixels.HasValue) sizePixels = OverrideSizePixels.Value;
            if (OverrideOffsetBR.HasValue)   offsetBR   = OverrideOffsetBR.Value;

            // Ensure exactly one EventSystem for click handling. Two
            // separate scripts (this one + MinimapUI) used to create their
            // own and FindFirstObjectByType() can return null inside the
            // same Awake-frame for other freshly-instantiated EventSystems,
            // so the scene easily ended up with 4-6 of them after a couple
            // of bootstrap iterations. Centralise the find-or-create through
            // a helper that also kills duplicates.
            UIEventSystemBootstrap.EnsureSingle();

            _fow = FindFirstObjectByType<FogOfWarManager>();
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

            EnsureECSQueries();
        }

        /// <summary>
        /// (Re)build the EntityQueries against the current ECS world. Safe to
        /// call repeatedly. Required because this MonoBehaviour is
        /// DontDestroyOnLoad: when the player returns to the main menu the ECS
        /// world is disposed, the cached _world / _em / _unitsQ become invalid,
        /// and the next game's Update() crashes inside ToEntityArray with NRE.
        /// Update() calls this whenever it detects a stale or disposed world.
        /// </summary>
        private void EnsureECSQueries()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                _world = null;
                return;
            }

            // Same world as last init — queries are still valid.
            if (ReferenceEquals(_world, world)) return;

            _world = world;
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
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            // Ritual broadcast markers (spec §5.1) + Glow pickup markers.
            // Both visible to all players regardless of fog of war — the spec
            // is explicit that rituals are universally locatable.
            _ritualSitesQ = _em.CreateEntityQuery(
                ComponentType.ReadOnly<ActiveRitualOnNode>(),
                ComponentType.ReadOnly<LocalTransform>());
            _glowPickupsQ = _em.CreateEntityQuery(
                ComponentType.ReadOnly<GlowPickupTag>(),
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

            // ECS world may have been recreated since last frame (e.g., player
            // bounced through the main menu). Re-init queries against the live
            // world before touching them. Skip this frame entirely if no world.
            EnsureECSQueries();
            if (_world == null) return;

            // Build ground texture once the passability grid is ready so the
            // impassable overlay paints with the rest of the biome colours.
            // If the grid isn't baked yet we still build (so the minimap is
            // visible) but defer marking it "built" — the next frame retries
            // and stamps the overlay in.
            if (!_bgBuilt)
            {
                BuildGroundBackground();
                var pg = PassabilityGrid.Instance;
                _bgBuilt = pg != null && pg.Cells.IsCreated;
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

            // Dark foliage green for forest areas on the minimap
            Color forestGround = new Color(0.12f, 0.28f, 0.08f, 1f);

            var passGrid = PassabilityGrid.Instance;

            for (int y = 0; y < samples; y++)
            {
                float vz = Mathf.Lerp(minZ, maxZ, (y + 0.5f) / samples);
                for (int x = 0; x < samples; x++)
                {
                    float vx = Mathf.Lerp(minX, maxX, (x + 0.5f) / samples);
                    Color col = SampleGroundColor(vx, vz, x, y);

                    // Overlay forest areas: blend to dark green within forest radii
                    foreach (var (center, radius) in ObstacleBootstrap.ForestPositions)
                    {
                        float dx = vx - center.x;
                        float dz = vz - center.z;
                        float distSq = dx * dx + dz * dz;
                        float rSq = radius * radius;
                        if (distSq < rSq)
                        {
                            // Smooth blend: fully green at center, fade at edges
                            float t = 1f - (distSq / rSq);
                            col = Color.Lerp(col, forestGround, t * 0.85f);
                            break;
                        }
                    }

                    // Passability stamp — anything pathing considers blocked
                    // (mountain interior, water, very steep cliff) is darkened
                    // toward colImpassable so the player can read at a glance
                    // where units cannot go. Skipped if the grid isn't built
                    // yet so the early-game minimap doesn't flicker.
                    if (passGrid != null && passGrid.Cells.IsCreated)
                    {
                        var cell = passGrid.WorldToCell(new float3(vx, 0f, vz));
                        if (cell.x >= 0 && cell.x < passGrid.Width &&
                            cell.y >= 0 && cell.y < passGrid.Height)
                        {
                            byte v = passGrid.Cells[cell.y * passGrid.Width + cell.x];
                            if (v == PassabilityGrid.TerrainBlocked)
                                col = Color.Lerp(col, colImpassable, impassableBlend);
                        }
                    }

                    _bgBuffer[y * samples + x] = col;
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
            // Height tiers match the current heightmap: PlainY=8, hills cap
            // ~20m, mountain peaks ~30m. Earlier thresholds (40/55/70) were
            // sized for a long-retired 90m-tall heightmap and painted the
            // entire map as grass.
            Color ground;

            if (h < 12f)
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
            else if (h < 20f)
            {
                // Hill belt — grass/dirt blend.
                float hillT = Mathf.InverseLerp(12f, 20f, h);
                ground = Color.Lerp(colGrass, colDirt, hillT * 0.7f);
            }
            else if (h < 25f)
            {
                // Foothill / mountain skirt — dirt giving way to rock.
                float highT = Mathf.InverseLerp(20f, 25f, h);
                ground = Color.Lerp(colDirt, colRock, highT);
            }
            else
            {
                // Mountain proper — rock with a snow tint above 28m.
                float snowT = Mathf.InverseLerp(25f, 30f, h);
                ground = Color.Lerp(colRock, colSnow, snowT * 0.7f);
            }

            // Cliff override at moderate-or-steep slope.
            if (slope > 0.30f)
            {
                float cliffT = Mathf.InverseLerp(0.30f, 0.65f, slope);
                cliffT = Mathf.Clamp01(cliffT);
                ground = Color.Lerp(ground, colRock, cliffT * 0.8f);
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
        ///
        /// Fix #236: sample the FoW at half resolution (stride=2) and reuse
        /// each sample for the 2x2 pixel block it covers. Fog queries went
        /// from sizePixels^2 (65,536 at 256px) to sizePixels^2/4 (16,384)
        /// per refresh — a 75% reduction. Visual quality is unchanged at
        /// minimap scale because fog dimming is already a coarse-grained
        /// 3-state classification (visible / revealed / hidden).
        /// </summary>
        private void ApplyFogOfWarDimming()
        {
            float minX = worldMin.x, minZ = worldMin.y;
            float maxX = worldMax.x, maxZ = worldMax.y;
            int W = sizePixels;
            const int stride = 2;

            for (int y = 0; y < sizePixels; y += stride)
            {
                float vz = Mathf.Lerp(minZ, maxZ, (y + 0.5f) / sizePixels);
                for (int x = 0; x < sizePixels; x += stride)
                {
                    float vx = Mathf.Lerp(minX, maxX, (x + 0.5f) / sizePixels);
                    float3 pos = new float3(vx, 0f, vz);

                    bool vis = FogOfWarSystem.IsVisibleToFaction(humanFaction, pos);
                    if (vis) continue;

                    bool rev = FogOfWarSystem.IsRevealedToFaction(humanFaction, pos);
                    float mult = rev ? 0.5f : 0.15f;

                    // Apply the dimming multiplier to the 2x2 pixel block.
                    int yEnd = math.min(y + stride, sizePixels);
                    int xEnd = math.min(x + stride, sizePixels);
                    for (int by = y; by < yEnd; by++)
                    {
                        for (int bx = x; bx < xEnd; bx++)
                        {
                            int idx = by * W + bx;
                            Color c = _frame[idx];
                            c.r *= mult; c.g *= mult; c.b *= mult;
                            _frame[idx] = c;
                        }
                    }
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

            // Draw obstacle blips (rocks = grey) — always visible
            // Forests are shown as areas on the background, not as blips.
            // Individual tree entities have ObstacleTag but no PresentationId, so
            // _obstaclesQ (which requires PresentationId) only matches rocks.
            using (var xfs = _obstaclesQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                Color rockColor = new Color(0.38f, 0.36f, 0.33f);

                for (int i = 0; i < xfs.Length; i++)
                {
                    int2 p = WorldToPixel(xfs[i].Position);
                    DrawDisc(p.x, p.y, 2, rockColor);
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

            // Ritual broadcast markers (spec §5.1: rituals are visible to all
            // players, regardless of fog of war). Color matches RitualBeamSystem's
            // beam tint so the minimap blip + world beam read as the same event.
            using (var actives = _ritualSitesQ.ToComponentDataArray<ActiveRitualOnNode>(Allocator.Temp))
            using (var xfs = _ritualSitesQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < actives.Length; i++)
                {
                    Color c = actives[i].Kind switch
                    {
                        RitualKind.Conversion        => new Color(0.45f, 1.00f, 0.55f),
                        RitualKind.ViolentExtraction => new Color(1.00f, 0.45f, 0.20f),
                        _                            => new Color(0.65f, 0.95f, 1.00f),
                    };
                    int2 p = WorldToPixel(xfs[i].Position);
                    DrawDisc(p.x, p.y, 4, c);
                }
            }

            // Glow pickups on the field — gold blip, also fog-ignorant
            // (spec §4.5 + refinement #4: the claim must be visible to all).
            using (var xfs = _glowPickupsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                Color glowColor = new Color(1.00f, 0.85f, 0.30f);
                for (int i = 0; i < xfs.Length; i++)
                {
                    int2 p = WorldToPixel(xfs[i].Position);
                    DrawDisc(p.x, p.y, 3, glowColor);
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

            // Returns pixel coords in the view-line's anchor-relative space.
            // Legacy path: lines are anchored at (1, 0) → x ∈ [-w, 0], y ∈ [0, h].
            // Web-HUD path: lines are anchored at (0.5, 0.5) → x ∈ [-w/2, w/2],
            //               y ∈ [-h/2, h/2] (centered).
            if (ForceDedicatedCanvas)
            {
                return new Vector2(u * w - w * 0.5f, v * h - h * 0.5f);
            }
            return new Vector2(-(w - u * w), v * h);
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

            // ScreenPointToLocalPointInRectangle returns a point in the rect's
            // local frame whose range depends on the pivot. Convert to [0..1]
            // using the rect's pivot so it works for both legacy (pivot=(1,0))
            // and web-HUD (pivot=(0.5, 0.5)) placements.
            //   local.x ∈ [-pivot.x * w, (1 - pivot.x) * w]
            //   local.y ∈ [-pivot.y * h, (1 - pivot.y) * h]
            Vector2 pivot = _rawRect.pivot;
            float u = (local.x / w) + pivot.x;
            float v = (local.y / h) + pivot.y;

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

            GameCamera.FocusOn(new Vector3(worldX, 0, worldZ), instant: true);
        }

        /// <summary>
        /// Handle right-click: issue move orders to selected units at the clicked world position.
        /// </summary>
        internal void HandleRightClick(PointerEventData eventData)
        {
            if (!TryGetWorldPosition(eventData, out float worldX, out float worldZ)) return;

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
            Canvas canvas = null;
            if (!ForceDedicatedCanvas)
            {
                // Reuse whatever Canvas the scene already has — original behaviour.
                canvas = FindFirstObjectByType<Canvas>();
            }
            if (canvas == null)
            {
                var cGo = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = cGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = OverrideCanvasSortingOrder ?? 100;

                // When the web HUD is driving placement, match CEF's
                // CanvasScaler so the minimap stays inscribed in the diamond
                // at every screen resolution. Otherwise leave the scaler at
                // its default (ConstantPixelSize) so legacy positioning stays.
                if (ForceDedicatedCanvas)
                {
                    var scaler = cGo.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                }
            }
            else if (OverrideCanvasSortingOrder.HasValue)
            {
                canvas.sortingOrder = OverrideCanvasSortingOrder.Value;
            }

            var rawGo = new GameObject("MinimapRaw", typeof(RawImage));
            rawGo.transform.SetParent(canvas.transform, false);

            _raw = rawGo.GetComponent<RawImage>();
            _raw.texture = _tex;

            _rawRect = _raw.rectTransform;
            _rawRect.anchorMin = new Vector2(1, 0);
            _rawRect.anchorMax = new Vector2(1, 0);
            _rawRect.sizeDelta = new Vector2(sizePixels, sizePixels);
            if (ForceDedicatedCanvas)
            {
                // Web-HUD path: rotate the square 45° so it fits the diamond
                // frame painted by CEF. Pivot at center so rotation is around
                // the minimap's middle; anchored position picks the diamond
                // center using offsetBR as the inset from the screen corner.
                _rawRect.pivot = new Vector2(0.5f, 0.5f);
                _rawRect.anchoredPosition = new Vector2(-offsetBR.x, offsetBR.y);
                _rawRect.localRotation = Quaternion.Euler(0, 0, 45f);
            }
            else
            {
                // Legacy bottom-right corner placement, axis-aligned.
                _rawRect.pivot = new Vector2(1, 0);
                _rawRect.anchoredPosition = new Vector2(-offsetBR.x, offsetBR.y);
            }

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
                // Anchor to the parent's bottom-right (legacy) or center (web
                // HUD) so WorldToMinimapPixel's two coordinate spaces line up.
                Vector2 anchor = ForceDedicatedCanvas
                    ? new Vector2(0.5f, 0.5f)
                    : new Vector2(1f, 0f);
                lineRect.anchorMin = anchor;
                lineRect.anchorMax = anchor;
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

            // Right-click → move-order; left-click → camera snap. Earlier
            // missing braces here meant HandleLeftClick ran on EVERY click,
            // so every right-click move order also yanked the camera to the
            // click point. PR #245's task-059 F-1 fix landed but a later merge
            // re-introduced the missing braces — properly fixed now with the
            // explicit if/else. (task-062 Q-51)
            if (eventData.button == PointerEventData.InputButton.Right)
                minimap.HandleRightClick(eventData);
            else
                minimap.HandleLeftClick(eventData);
        }
    }
}
