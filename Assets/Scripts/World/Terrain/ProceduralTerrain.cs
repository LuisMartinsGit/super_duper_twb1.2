// ProceduralTerrain.cs
// Continental Map Generator with Realistic Terrain Features
// - Single large landmass with organic coastlines (domain warping)
// - Rolling hills, mountain ridges, beaches, cliffs
// - Player spawn zones flattened for fair base areas
// - Natural choke points between players via mountain ridges
// Location: Assets/Scripts/World/Terrain/ProceduralTerrain.cs

using UnityEngine;
using System.Collections.Generic;

namespace TheWaningBorder.World.Terrain
{
    /// <summary>
    /// Generates realistic continental maps with layered procedural noise.
    /// Players spawn in flattened clearings connected by hills and mountain ridges.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ProceduralTerrain : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // WORLD SETTINGS
        // ═══════════════════════════════════════════════════════════════════════

        [Header("World (derived from GameSettings)")]
        public Vector2 worldMin = new(-256, -256);
        public Vector2 worldMax = new(256, 256);
        public int heightmapRes = 1025;
        public int controlTexRes = 1024;
        public float maxHeight = 100f;
        public int seed = 12345;

        [Header("Height Levels (in world units)")]
        [Tooltip("Water surface height")]
        public float waterHeight = 20f;

        // ═══════════════════════════════════════════════════════════════════════
        // CONTINENTAL NOISE SETTINGS
        // ═══════════════════════════════════════════════════════════════════════

        [Header("Continental Shape")]
        [Tooltip("Base continent noise frequency (lower = larger landmasses)")]
        public float continentScale = 0.008f;
        [Tooltip("FBM octaves for base terrain")]
        public int continentOctaves = 5;
        [Tooltip("Amplitude decay per octave")]
        public float continentPersistence = 0.5f;
        [Tooltip("Threshold below which terrain is ocean (0-1)")]
        public float oceanThreshold = 0.35f;
        [Tooltip("Width of beach transition above ocean threshold")]
        public float beachWidth = 0.07f;

        [Header("Domain Warping (organic coastlines)")]
        [Tooltip("How much to distort coastlines (world units)")]
        public float warpStrength = 60f;
        [Tooltip("Frequency of warp noise")]
        public float warpScale = 0.005f;

        [Header("Hills")]
        [Tooltip("Frequency of rolling hills noise")]
        public float hillScale = 0.025f;
        [Tooltip("World-unit height of hills")]
        public float hillAmplitude = 15f;

        [Header("Mountains")]
        [Tooltip("Frequency of mountain ridge noise")]
        public float mountainScale = 0.012f;
        [Tooltip("World-unit height of mountain peaks")]
        public float mountainAmplitude = 35f;
        [Tooltip("Only add mountains where base land value > this (0-1)")]
        [Range(0.3f, 0.7f)]
        public float mountainThreshold = 0.55f;

        [Header("Spawn Zones")]
        [Tooltip("Radius of flattened spawn area (world units)")]
        public float spawnFlattenRadius = 40f;
        [Tooltip("Target height for spawn zones (world units)")]
        public float spawnTargetHeight = 30f;
        [Tooltip("Transition width from flat to natural terrain")]
        public float spawnBlendRadius = 20f;
        [Tooltip("How far player spawns are from center (fraction of map half)")]
        [Range(0.3f, 0.7f)]
        public float spawnDistance = 0.50f;

        [Header("Edge Falloff")]
        [Tooltip("Fraction of map half used as ocean border (0-1)")]
        [Range(0.1f, 0.5f)]
        public float edgeBorderFraction = 0.30f;

        [Header("Terrain Layers (auto-generated if null)")]
        public TerrainLayer sand;
        public TerrainLayer grass;
        public TerrainLayer dirt;
        public TerrainLayer rock;
        public TerrainLayer snow;
        public TerrainLayer curse;
        public TerrainLayer forestFloor;
        // Image-map exclusive layers — distinct procedural look so the player
        // reads sea-bed vs beach and cliff-face vs mountain even though both
        // pairs use the same height tier or splatmap channel for queries.
        public TerrainLayer seaBed;
        public TerrainLayer cliffStone;

        [Header("Texture Settings")]
        public int textureResolution = 512;
        public float textureTiling = 15f;

        // ═══════════════════════════════════════════════════════════════════════
        // RUNTIME DATA
        // ═══════════════════════════════════════════════════════════════════════

        private UnityEngine.Terrain _terrain;
        private TerrainData _data;
        private System.Random _rng;
        private float _noiseOffsetX;
        private float _noiseOffsetY;

        /// <summary>
        /// Player region data for spawn system integration.
        /// Kept as IslandInfo for API compatibility with PlayerSpawnSystem.
        /// </summary>
        public struct IslandInfo
        {
            public Vector2 Center;
            public float Radius;
            public bool IsMainland;      // Legacy - kept for compatibility
            public bool IsPlayerIsland;  // True if this is a player spawn region
            public int PlayerIndex;      // Which player owns this region (-1 for neutral)
        }

        private List<IslandInfo> _islands = new();

        /// <summary>
        /// Get all player regions (for spawn positioning).
        /// </summary>
        public IReadOnlyList<IslandInfo> Islands => _islands;

        /// <summary>
        /// Singleton instance for easy access.
        /// </summary>
        public static ProceduralTerrain Instance { get; private set; }

        /// <summary>
        /// Flatten a square region of the terrain heightmap to the height sampled
        /// at the region's centre. Called when a building is placed so the model
        /// sits on level ground. <paramref name="halfExtent"/> is the half-side
        /// length in world units (so a 4x4-cell building uses halfExtent = 2).
        /// </summary>
        public void FlattenAt(Vector3 worldCenter, float halfExtent)
        {
            if (_terrain == null || _data == null) return;
            if (halfExtent <= 0f) return;

            int res = _data.heightmapResolution;
            float terrainPosX = _terrain.transform.position.x;
            float terrainPosZ = _terrain.transform.position.z;
            float terrainSizeX = _data.size.x;
            float terrainSizeZ = _data.size.z;
            float terrainSizeY = _data.size.y;
            if (terrainSizeY <= 0f) return;

            // Target normalized height = current sample at the centre.
            float centerHeight = _terrain.SampleHeight(worldCenter);
            float targetNorm = Mathf.Clamp01(centerHeight / terrainSizeY);

            // Convert world XZ rect to heightmap pixel rect.
            float minNormX = ((worldCenter.x - halfExtent) - terrainPosX) / terrainSizeX;
            float maxNormX = ((worldCenter.x + halfExtent) - terrainPosX) / terrainSizeX;
            float minNormZ = ((worldCenter.z - halfExtent) - terrainPosZ) / terrainSizeZ;
            float maxNormZ = ((worldCenter.z + halfExtent) - terrainPosZ) / terrainSizeZ;

            int minX = Mathf.Clamp(Mathf.FloorToInt(minNormX * (res - 1)), 0, res - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt (maxNormX * (res - 1)), 0, res - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt(minNormZ * (res - 1)), 0, res - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt (maxNormZ * (res - 1)), 0, res - 1);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0) return;

            float[,] heights = new float[height, width];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    heights[z, x] = targetNorm;
            _data.SetHeights(minX, minZ, heights);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════════

        // True once the heavy generation coroutine has finished filling the
        // heightmap, splatmap, and layers. SpawnDelayHelper / other systems
        // gate on this via TerrainUtility.IsReady(). False during the early
        // frames while the loading-screen is on-screen.
        public static bool IsGenerationComplete { get; private set; }

        /// <summary>
        /// Marks the procedural-generation gate as already complete for a
        /// scene that ships with its own baked Unity Terrain (e.g. MapMagic
        /// output on a hand-authored map). Call this in place of creating a
        /// ProceduralTerrain component — SpawnDelayHelper's
        /// TerrainUtility.IsReady() wait loop will fall through immediately
        /// so the rest of the bootstrap pipeline can proceed.
        /// </summary>
        public static void MarkExternalTerrainReady()
        {
            IsGenerationComplete = true;
        }

        void Awake()
        {
            // FAST path only — anything that takes more than ~1 ms must move
            // to GenerateRoutine(). Earlier this method ran heightmap + 1
            // smoothing pass + 25 erosion iterations + 7 texture layers + 7
            // normal maps in one synchronous shot, all triggered from
            // GameBootstrap.OnSceneLoadedHandler → InitializeWorld(). That
            // froze the main thread for 5-10 s on the activation frame
            // BEFORE the LoadingScreen overlay ever painted — the player
            // saw a frozen lobby until the bootstrap finished.
            IsGenerationComplete = false;
            Instance = this;

            // Derive bounds from GameSettings
            int half = Mathf.Max(16, GameSettings.MapHalfSize);
            worldMin = new Vector2(-half, -half);
            worldMax = new Vector2(half, half);

            // Use seed from GameSettings if available
            if (GameSettings.SpawnSeed != 0)
                seed = GameSettings.SpawnSeed;

            _rng = new System.Random(seed);
            _noiseOffsetX = _rng.Next(0, 100000);
            _noiseOffsetY = _rng.Next(0, 100000);

            // TerrainData + Terrain GameObject — fast (no heightmap yet).
            var size = new Vector3(worldMax.x - worldMin.x, maxHeight, worldMax.y - worldMin.y);
            _data = new TerrainData();
            _data.heightmapResolution = heightmapRes;
            _data.alphamapResolution = controlTexRes;
            _data.baseMapResolution = controlTexRes;
            _data.size = size;

            var go = UnityEngine.Terrain.CreateTerrainGameObject(_data);
            go.name = "ProcTerrain";
            go.transform.position = new Vector3(worldMin.x, 0, worldMin.y);
            _terrain = go.GetComponent<UnityEngine.Terrain>();
            _terrain.drawInstanced = true;

            // Tree rendering settings — without these the GPU-instanced
            // forest patches stay invisible regardless of how many
            // TreeInstance entries we push to TerrainData.
            _terrain.drawTreesAndFoliage = true;
            _terrain.treeDistance = 800f;         // hard cull beyond this; default 5000 is overkill
            _terrain.treeBillboardDistance = 250f; // switch to billboards past this
            _terrain.treeCrossFadeLength = 50f;
            _terrain.treeMaximumFullLODCount = 200; // full-LOD nearest 200 trees per terrain

            waterHeight = 2.5f;
        }

        void Start()
        {
            // Kick the heavy generation off Start so it lands on a frame
            // AFTER OnSceneLoadedHandler returned and the loading screen has
            // had at least one Update/OnGUI cycle to paint.
            StartCoroutine(GenerateRoutine());
        }

        private System.Collections.IEnumerator GenerateRoutine()
        {
            // Yield once so the loading screen paints with whatever status
            // it had before the scene activated.
            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Generating heightmap…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.45f);
            yield return null;

            // Legacy polar fallback for older queries that haven't migrated
            // to the new region set.
            GeneratePlayerRegions();
            yield return null;

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Building procedural map…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.48f);
            yield return null;

            // ProceduralMapGen.Generate is still one synchronous block — it
            // covers heightmap + smoothing + erosion + 7 texture layers + 7
            // normal maps. The yields above guarantee the loading screen is
            // on-screen before this fires; the freeze that follows is brief
            // because the player at least sees "Building procedural map…
            // 48 %" instead of a frozen lobby.
            var result = TheWaningBorder.World.Maps.ProceduralMapGen.Generate(
                GameSettings.MapArchetype, GameSettings.SpawnSeed,
                Mathf.Max(2, GameSettings.TotalPlayers),
                _data, waterHeight, worldMin, worldMax);

            if (!result.success)
            {
                Debug.LogError($"[ProceduralTerrain] ProceduralMapGen failed: {result.failureReason}. " +
                               "Falling back to legacy noise continental.");
                GenerateTerrainLayers();
                GenerateHeightmap();
                PaintSplatmaps();
            }
            else
            {
                SyncIslandsFromRegions(result.regions);
                // ProceduralMapGen.BuildLayers ships 7 procedural layers and
                // has no curse layer of its own. PaintCursedGround needs the
                // curse layer present in terrainData.terrainLayers at runtime
                // — append it here so CrystalSpreadSystem can paint cursed
                // ground on the new map gen output the same way it did on
                // the legacy splatmap path.
                EnsureCurseLayerAppended();
            }
            yield return null;

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Pouring water plane…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.52f);
            yield return null;
            CreateWaterPlane();

            // Forests are driven by ObstacleBootstrap.SpawnObstacles, which
            // calls PaintTreesOnBrownGround on this terrain after the splat
            // is built. That path samples the L_FOREST splat layer, plants
            // GPU-instanced terrain trees wherever the brown ground reads,
            // blocks the matching PassabilityGrid cells, and returns macro
            // cell centres that ObstacleBootstrap turns into ObstacleTag
            // entities for NavMeshManager. Nothing to do here.
            yield return null;

            IsGenerationComplete = true;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // WATER PLANE CREATION
        // ═══════════════════════════════════════════════════════════════════════

        void CreateWaterPlane()
        {
            // Create water GameObject
            var waterGO = new GameObject("WaterPlane");
            waterGO.transform.SetParent(transform);

            var water = waterGO.AddComponent<WaterPlane>();
            water.waterLevel = waterHeight;

            // AoE4-style water settings
            // Flow animation (gentle, drifting)
            water.flowSpeed = 0.06f;
            // flowStrength is the max wave amplitude (max(wave1+wave2) ≈ 1.5),
            // so 0.13 keeps the visible wave height around 0.2 units.
            water.flowStrength = 0.13f;

            // Surface detail
            // rippleScale = wave frequency; higher value → tighter, smaller waves.
            water.rippleScale = 0.15f;
            water.rippleSpeed = 0.4f;
            water.bumpiness = 0.35f;

            // Foam settings
            water.foamScale = 0.07f;
            water.foamThreshold = 0.55f;
            water.foamIntensity = 1.2f;

            // Specular (subtle for RTS readability)
            water.specularPower = 64f;
            water.specularIntensity = 0.35f;

            // AoE4-style colors (depth-based)
            water.shallowColor = new Color(0.30f, 0.60f, 0.70f, 0.6f);
            water.deepColor = new Color(0.08f, 0.22f, 0.35f, 0.95f);
            water.foamColor = new Color(0.95f, 0.98f, 1f, 0.9f);

            // Initialize with world bounds
            water.Initialize(worldMin, worldMax, waterHeight);

        }

        // ═══════════════════════════════════════════════════════════════════════
        // PROCEDURAL TEXTURE GENERATION
        // ═══════════════════════════════════════════════════════════════════════

        void GenerateTerrainLayers()
        {
            float mapSize = worldMax.x - worldMin.x;
            float tileSize = mapSize / textureTiling;

            // Use seed-based offset for texture generation (in case main offsets not set)
            var texRng = new System.Random(seed + 999);
            float texOffsetX = texRng.Next(0, 10000);
            float texOffsetY = texRng.Next(0, 10000);

            if (sand == null)
                sand = CreateTerrainLayer("Sand", GenerateSandTexture(texOffsetX, texOffsetY), tileSize);

            if (grass == null)
                grass = CreateTerrainLayer("Grass", GenerateGrassTexture(texOffsetX, texOffsetY), tileSize);

            if (dirt == null)
                dirt = CreateTerrainLayer("Dirt", GenerateDirtTexture(texOffsetX, texOffsetY), tileSize);

            if (rock == null)
                rock = CreateTerrainLayer("Rock", GenerateRockTexture(texOffsetX, texOffsetY), tileSize);

            if (snow == null)
                snow = CreateTerrainLayer("Snow", GenerateSnowTexture(texOffsetX, texOffsetY), tileSize);

            if (curse == null)
            {
                // Try to load the authored Crystal_ground texture set from
                // Assets/Resources/Crystal_ground/. Falls back to the procedural
                // curse texture if any of the textures are missing.
                curse = TryBuildCurseLayerFromResources(tileSize);
                if (curse == null)
                {
                    curse = CreateTerrainLayer("Curse", GenerateCurseTexture(texOffsetX, texOffsetY), tileSize);
                    curse.metallic = 0.7f;    // Reflective crystalline surface
                    curse.smoothness = 0.85f; // Glassy sheen
                }
            }

            if (forestFloor == null)
                forestFloor = CreateTerrainLayer("ForestFloor", GenerateForestFloorTexture(texOffsetX, texOffsetY), tileSize * 0.5f);

            if (seaBed == null)
                seaBed = CreateTerrainLayer("SeaBed", GenerateSeaBedTexture(texOffsetX, texOffsetY), tileSize * 0.65f);

            if (cliffStone == null)
                cliffStone = CreateTerrainLayer("CliffStone", GenerateCliffStoneTexture(texOffsetX, texOffsetY), tileSize * 0.85f);
        }

        // Underwater silt — bluish-gray with darker mottles and bright bands
        // of pale sand for ripple highlights. Distinct from Beach so the
        // shoreline reads.
        Texture2D GenerateSeaBedTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);
            var deepSilt = new Color(0.18f, 0.24f, 0.28f);   // wet shaded silt
            var midSilt  = new Color(0.32f, 0.38f, 0.40f);   // mid silt
            var palePeb  = new Color(0.55f, 0.58f, 0.55f);   // pale pebbles in ripples
            var darkSpot = new Color(0.10f, 0.14f, 0.18f);   // sunk debris
            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;
                    // FBM grain
                    float n = Mathf.PerlinNoise(u * 32 + offsetX, v * 32 + offsetY) * 0.55f
                            + Mathf.PerlinNoise(u * 110 + offsetX, v * 110 + offsetY) * 0.30f
                            + Mathf.PerlinNoise(u * 260 + offsetX, v * 260 + offsetY) * 0.15f;
                    Color c = Color.Lerp(deepSilt, midSilt, n);
                    // Stripe ripples — sin in V with a noisy phase so they don't read as bands.
                    float ripplePhase = Mathf.PerlinNoise(u * 6 + offsetX, v * 6 + offsetY) * 6f;
                    float ripple = Mathf.Sin(v * 90f + ripplePhase) * 0.5f + 0.5f;
                    if (ripple > 0.7f)
                        c = Color.Lerp(c, palePeb, (ripple - 0.7f) * 1.4f);
                    // Occasional dark debris spots
                    float spot = Mathf.PerlinNoise(u * 70 + offsetY * 2, v * 70 + offsetX * 2);
                    if (spot > 0.78f)
                        c = Color.Lerp(c, darkSpot, (spot - 0.78f) * 4f);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // Vertical-band cliff face — banded strata in warm grey/brown so the
        // sharp drop reads as a cliff cross-section, not just darker rock.
        Texture2D GenerateCliffStoneTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);
            var darkBand  = new Color(0.22f, 0.18f, 0.15f);
            var midBand   = new Color(0.42f, 0.36f, 0.30f);
            var lightBand = new Color(0.60f, 0.54f, 0.46f);
            var crackLine = new Color(0.10f, 0.08f, 0.06f);
            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;
                    // Stratified band noise — low frequency along Y, more variance along X.
                    float strataPhase = Mathf.PerlinNoise(u * 3.5f + offsetX, v * 1.2f + offsetY) * 4f;
                    float strata = Mathf.PerlinNoise(u * 5f, v * 45f + strataPhase);
                    Color c = strata < 0.5f
                        ? Color.Lerp(darkBand, midBand, strata * 2f)
                        : Color.Lerp(midBand, lightBand, (strata - 0.5f) * 2f);
                    // High-frequency rock grain
                    float grain = Mathf.PerlinNoise(u * 220 + offsetX, v * 220 + offsetY);
                    c = Color.Lerp(c, c * 0.85f, grain * 0.35f);
                    // Vertical cracks
                    float crack = Mathf.PerlinNoise(u * 18 + offsetY * 3, v * 4 + offsetX * 3);
                    if (crack > 0.82f)
                        c = Color.Lerp(c, crackLine, (crack - 0.82f) * 4f);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Texture2D GenerateForestFloorTexture(float offsetX, float offsetY)
        {
            // Brown leafy forest floor — fallen needles + dirt + moss
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);

            var darkLeaf = new Color(0.16f, 0.10f, 0.04f);   // wet soil
            var midLeaf  = new Color(0.32f, 0.20f, 0.08f);   // brown leaves
            var lightLeaf= new Color(0.48f, 0.34f, 0.14f);   // dry needles
            var moss     = new Color(0.18f, 0.26f, 0.10f);   // patches of green moss
            var twig     = new Color(0.12f, 0.07f, 0.03f);   // dark twigs

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Base FBM grain — leafy texture
                    float n = Mathf.PerlinNoise(u * 50 + offsetX, v * 50 + offsetY) * 0.5f
                            + Mathf.PerlinNoise(u * 120 + offsetX, v * 120 + offsetY) * 0.3f
                            + Mathf.PerlinNoise(u * 280 + offsetX, v * 280 + offsetY) * 0.2f;

                    Color c = Color.Lerp(darkLeaf, midLeaf, n);
                    c = Color.Lerp(c, lightLeaf, Mathf.Pow(n, 2.5f));

                    // Sparse moss patches
                    float mossN = Mathf.PerlinNoise(u * 18 + offsetX * 2, v * 18 + offsetY * 2);
                    if (mossN > 0.65f)
                        c = Color.Lerp(c, moss, (mossN - 0.65f) * 2.5f);

                    // High-frequency twig specks
                    float twigN = Mathf.PerlinNoise(u * 400 + offsetY * 3, v * 400 + offsetX * 3);
                    if (twigN > 0.85f)
                        c = Color.Lerp(c, twig, (twigN - 0.85f) * 5f);

                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        TerrainLayer CreateTerrainLayer(string name, Texture2D diffuse, float tileSize)
        {
            // Generate normal map from diffuse
            var normalMap = GenerateNormalMap(diffuse);

            var layer = new TerrainLayer
            {
                diffuseTexture = diffuse,
                normalMapTexture = normalMap,
                tileSize = new Vector2(tileSize, tileSize),
                tileOffset = Vector2.zero,
                normalScale = 1.0f
            };
            return layer;
        }

        // Appends the curse layer to terrainData.terrainLayers when the new
        // ProceduralMapGen pipeline has just rebuilt the layer set without
        // one. Also grows the alphamap depth so CrystalSpreadSystem's
        // PaintCursedGround can find the curse index at runtime.
        void EnsureCurseLayerAppended()
        {
            if (_data == null) return;

            if (curse == null)
            {
                float mapSize = worldMax.x - worldMin.x;
                float tileSize = mapSize / textureTiling;
                curse = TryBuildCurseLayerFromResources(tileSize);
                if (curse == null)
                {
                    curse = CreateTerrainLayer("Curse", GenerateCurseTexture(0f, 0f), tileSize);
                    curse.metallic = 0.7f;
                    curse.smoothness = 0.85f;
                }
            }
            if (curse == null) return;

            var layers = _data.terrainLayers;
            if (layers != null)
            {
                for (int i = 0; i < layers.Length; i++)
                    if (layers[i] == curse) return; // already present
            }
            int oldLen = layers == null ? 0 : layers.Length;
            var newLayers = new TerrainLayer[oldLen + 1];
            for (int i = 0; i < oldLen; i++) newLayers[i] = layers[i];
            newLayers[oldLen] = curse;
            _data.terrainLayers = newLayers;

            // Grow the alphamap depth to match. ProceduralSplat sized the
            // splat array to alphamapLayers at build time (7); after the
            // assignment above alphamapLayers is now 8 so the existing
            // alphamap implicitly has the curse layer at weight 0 across
            // the whole map. Force-refresh by re-reading + re-writing so
            // the GPU upload picks up the new layer slot.
            int alphaRes = _data.alphamapResolution;
            var oldSplat = _data.GetAlphamaps(0, 0, alphaRes, alphaRes);
            int oldDepth = oldSplat.GetLength(2);
            int newDepth = newLayers.Length;
            if (newDepth > oldDepth)
            {
                var newSplat = new float[alphaRes, alphaRes, newDepth];
                for (int z = 0; z < alphaRes; z++)
                    for (int x = 0; x < alphaRes; x++)
                        for (int l = 0; l < oldDepth; l++)
                            newSplat[z, x, l] = oldSplat[z, x, l];
                _data.SetAlphamaps(0, 0, newSplat);
            }
        }

        // ─── Curse ground from authored Crystal_ground textures ──────────────
        //
        // Loads the 6-texture set from Resources/Crystal_ground/, bakes a
        // purple→dark-green tint into the diffuse using the heightmap to
        // drive the gradient (deep purple in crevices, dark green on peaks),
        // packs the URP mask map (R=Metallic, G=AO, B=Height, A=Smoothness),
        // and cranks normalScale so the surface reads as very jagged.
        TerrainLayer TryBuildCurseLayerFromResources(float tileSize)
        {
            var color    = Resources.Load<Texture2D>("Crystal_ground/Crystal_ground_color");
            var normal   = Resources.Load<Texture2D>("Crystal_ground/Crystal_ground_normal_opengl");
            var heightTx = Resources.Load<Texture2D>("Crystal_ground/Crystal_ground_height");
            var rough    = Resources.Load<Texture2D>("Crystal_ground/Crystal_ground_roughness");
            var ao       = Resources.Load<Texture2D>("Crystal_ground/Crystal_ground_ambient_oclusion");

            if (color == null || normal == null || heightTx == null)
            {
                Debug.LogWarning("[ProceduralTerrain] Crystal_ground textures missing — " +
                                 $"color={(color != null)} normal={(normal != null)} " +
                                 $"height={(heightTx != null)}. Falling back to procedural curse layer.");
                return null;
            }
            Debug.Log($"[ProceduralTerrain] Loaded Crystal_ground textures " +
                      $"(color={color.width}×{color.height}, normal={normal.width}×{normal.height}, " +
                      $"height={heightTx.width}×{heightTx.height}). Baking tinted curse layer…");

            // Use the source diffuse as-is. Earlier passes baked tints
            // (purple, then dark green) but the user reverted to the
            // texture's authored colour. The heightmap-derived AO/highlight
            // is applied as a much subtler modulation so the surface keeps
            // its source palette but still reads as relief.
            var tintedDiffuse = BakeShadedCurseDiffuse(color, heightTx);

            // Packed mask map — Unity URP terrain convention.
            var maskMap = BakeCurseMaskMap(heightTx, rough, ao);

            // Tile this layer ~4× denser than the other layers so the texture
            // pattern reads as a tight crystalline crust instead of stretched
            // smears. Same texture, smaller world footprint per tile.
            float curseTile = tileSize * 0.25f;

            var layer = new TerrainLayer
            {
                diffuseTexture = tintedDiffuse,
                normalMapTexture = normal,
                maskMapTexture = maskMap,
                tileSize = new Vector2(curseTile, curseTile),
                tileOffset = Vector2.zero,
                // Crank normal scale aggressively — primary lever for the
                // "very jagged" look since URP terrain doesn't do parallax.
                // 9.0 is well past Unity's nominal "extreme" — the diffuse
                // bake also fakes deep AO/highlight, so the surface reads
                // as genuinely chiselled even though there's no real
                // displacement.
                normalScale = 9.0f,
                metallic = 0.7f,
                smoothness = 0.85f,
                // Pull height to the top of the remap so this layer wins
                // height-based blending at peaks (URP terrain uses mask map
                // B channel for height-blend between layers).
                maskMapRemapMin = new Vector4(0f, 0f, 0f, 0f),
                maskMapRemapMax = new Vector4(0.7f, 1f, 1f, 0.95f),
            };
            return layer;
        }

        Texture2D BakeShadedCurseDiffuse(Texture2D srcColor, Texture2D heightSrc)
        {
            var src = ReadablePixels(srcColor, out int sw, out int sh);
            var hgt = ReadablePixels(heightSrc, out int hw, out int hh);

            // Use the source colour as-is. Apply only a gentle heightmap-
            // based AO/highlight to approximate relief; no chrominance
            // override. Crevices darken slightly, peaks brighten slightly.
            var output = new Color32[sw * sh];
            for (int y = 0; y < sh; y++)
            {
                int hy = (y * hh) / sh;
                for (int x = 0; x < sw; x++)
                {
                    int hx = (x * hw) / sw;
                    int hi = hy * hw + hx;
                    int si = y * sw + x;

                    float ht = hgt[hi].r / 255f;
                    var s = src[si];

                    //   ht 0.0 → 0.65× (gentle AO shadow)
                    //   ht 1.0 → 1.20× (gentle rim highlight)
                    float heightShade = 0.65f + ht * 0.55f;

                    float r = Mathf.Clamp01((s.r / 255f) * heightShade);
                    float g = Mathf.Clamp01((s.g / 255f) * heightShade);
                    float b = Mathf.Clamp01((s.b / 255f) * heightShade);

                    output[si] = new Color32(
                        (byte)(r * 255f),
                        (byte)(g * 255f),
                        (byte)(b * 255f),
                        255);
                }
            }

            var tex = new Texture2D(sw, sh, TextureFormat.RGBA32, true, false);
            tex.SetPixels32(output);
            tex.Apply(updateMipmaps: true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "Curse_TintedDiffuse";
            return tex;
        }

        Texture2D BakeCurseMaskMap(Texture2D heightSrc, Texture2D roughSrc, Texture2D aoSrc)
        {
            var hgt = ReadablePixels(heightSrc, out int hw, out int hh);
            // Use the heightmap resolution as the mask-map resolution.
            int mw = hw, mh = hh;

            // roughness / AO are optional; substitute neutral defaults.
            Color32[] rough = roughSrc != null ? ReadablePixels(roughSrc, out int rw, out int rh) : null;
            int rwOut = roughSrc != null ? roughSrc.width : 0;
            int rhOut = roughSrc != null ? roughSrc.height : 0;
            Color32[] ao  = aoSrc != null ? ReadablePixels(aoSrc, out int aw, out int ah) : null;
            int awOut = aoSrc != null ? aoSrc.width : 0;
            int ahOut = aoSrc != null ? aoSrc.height : 0;

            var output = new Color32[mw * mh];
            for (int y = 0; y < mh; y++)
            {
                for (int x = 0; x < mw; x++)
                {
                    int i = y * mw + x;
                    byte hVal = hgt[i].r;
                    byte rVal = (rough != null) ? rough[((y * rhOut) / mh) * rwOut + ((x * rwOut) / mw)].r : (byte)128;
                    byte aVal = (ao    != null) ? ao   [((y * ahOut) / mh) * awOut + ((x * awOut) / mw)].r : (byte)255;
                    byte smoothness = (byte)(255 - rVal);
                    output[i] = new Color32(180, aVal, hVal, smoothness); // metallic≈0.7
                }
            }

            var tex = new Texture2D(mw, mh, TextureFormat.RGBA32, true, true);
            tex.SetPixels32(output);
            tex.Apply(updateMipmaps: true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "Curse_MaskMap";
            return tex;
        }

        // GetPixels32 throws if the texture isn't import-set readable. Blit
        // through a RenderTexture as a fallback so we don't depend on import
        // settings for the Crystal_ground assets.
        static Color32[] ReadablePixels(Texture2D src, out int w, out int h)
        {
            w = src.width;
            h = src.height;
            if (src.isReadable)
                return src.GetPixels32();

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tmp.Apply();
            var pixels = tmp.GetPixels32();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(tmp);
            return pixels;
        }

        Texture2D GenerateNormalMap(Texture2D source)
        {
            int width = source.width;
            int height = source.height;
            var normalMap = new Texture2D(width, height, TextureFormat.RGB24, true);

            float strength = 2.0f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Sample heights using grayscale
                    float left = source.GetPixel((x - 1 + width) % width, y).grayscale;
                    float right = source.GetPixel((x + 1) % width, y).grayscale;
                    float up = source.GetPixel(x, (y + 1) % height).grayscale;
                    float down = source.GetPixel(x, (y - 1 + height) % height).grayscale;

                    // Calculate normal
                    float dx = (left - right) * strength;
                    float dy = (down - up) * strength;

                    Vector3 normal = new Vector3(dx, dy, 1.0f).normalized;

                    // Convert to color (0-1 range)
                    Color c = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f
                    );
                    normalMap.SetPixel(x, y, c);
                }
            }

            normalMap.Apply();
            normalMap.wrapMode = TextureWrapMode.Repeat;
            normalMap.filterMode = FilterMode.Bilinear;
            return normalMap;
        }

        Texture2D GenerateSandTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);

            // Realistic beach sand colors
            var baseSand = new Color(0.82f, 0.75f, 0.58f);
            var wetSand = new Color(0.68f, 0.60f, 0.45f);
            var lightSand = new Color(0.90f, 0.85f, 0.68f);
            var shellBits = new Color(0.95f, 0.92f, 0.85f);
            var darkGrain = new Color(0.65f, 0.58f, 0.42f);

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Multi-octave noise for sand grain variation
                    float noise = Mathf.PerlinNoise(u * 50 + offsetX, v * 50 + offsetY) * 0.4f;
                    noise += Mathf.PerlinNoise(u * 100 + offsetX, v * 100 + offsetY) * 0.35f;
                    noise += Mathf.PerlinNoise(u * 200 + offsetX, v * 200 + offsetY) * 0.25f;

                    Color c = Color.Lerp(wetSand, lightSand, noise);

                    // Subtle ripple patterns (wind-formed)
                    float ripple = Mathf.Sin(u * 30 + Mathf.PerlinNoise(v * 8, offsetX) * 4f);
                    ripple = ripple * 0.5f + 0.5f;
                    ripple = Mathf.Pow(ripple, 3) * 0.08f;
                    c = Color.Lerp(c, lightSand, ripple);

                    // Occasional darker grains
                    float darkGrains = Mathf.PerlinNoise(u * 300 + offsetY, v * 300 + offsetX);
                    if (darkGrains > 0.78f)
                    {
                        c = Color.Lerp(c, darkGrain, (darkGrains - 0.78f) * 2f);
                    }

                    // Small shell/white bits
                    float shells = Mathf.PerlinNoise(u * 250 + offsetX * 2, v * 250 + offsetY * 2);
                    if (shells > 0.88f)
                    {
                        c = Color.Lerp(c, shellBits, (shells - 0.88f) * 4f);
                    }

                    c = Color.Lerp(baseSand, c, 0.7f);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Texture2D GenerateGrassTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);

            // Realistic grass colors - green with yellow/brown variation
            var baseGreen = new Color(0.35f, 0.55f, 0.18f);
            var darkGreen = new Color(0.22f, 0.42f, 0.12f);
            var yellowGreen = new Color(0.55f, 0.62f, 0.22f);
            var dryYellow = new Color(0.65f, 0.58f, 0.28f);
            var brownPatch = new Color(0.45f, 0.38f, 0.22f);

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Base grass variation
                    float noise = Mathf.PerlinNoise(u * 35 + offsetX, v * 35 + offsetY) * 0.4f;
                    noise += Mathf.PerlinNoise(u * 70 + offsetX, v * 70 + offsetY) * 0.35f;
                    noise += Mathf.PerlinNoise(u * 140 + offsetX, v * 140 + offsetY) * 0.25f;

                    // Grass blade direction streaks
                    float streaks = Mathf.PerlinNoise(u * 8 + offsetY, v * 100 + offsetX);
                    streaks = Mathf.Pow(streaks, 2) * 0.3f;

                    // Start with base green
                    Color c = Color.Lerp(darkGreen, baseGreen, noise);

                    // Add yellow-green patches (healthy sun-exposed grass)
                    float yellowNoise = Mathf.PerlinNoise(u * 12 + offsetX + 100, v * 12 + offsetY + 100);
                    if (yellowNoise > 0.5f)
                    {
                        float yellowAmount = (yellowNoise - 0.5f) * 2f;
                        c = Color.Lerp(c, yellowGreen, yellowAmount * 0.5f);
                    }

                    // Add dry patches
                    float dryNoise = Mathf.PerlinNoise(u * 8 + offsetX + 200, v * 8 + offsetY + 200);
                    if (dryNoise > 0.65f)
                    {
                        float dryAmount = (dryNoise - 0.65f) * 2.5f;
                        c = Color.Lerp(c, dryYellow, dryAmount * 0.4f);
                    }

                    // Add occasional brown/bare patches (for woodland floor feel)
                    float brownNoise = Mathf.PerlinNoise(u * 6 + offsetX + 300, v * 6 + offsetY + 300);
                    if (brownNoise > 0.72f)
                    {
                        float brownAmount = (brownNoise - 0.72f) * 3.5f;
                        c = Color.Lerp(c, brownPatch, brownAmount * 0.5f);
                    }

                    // Apply streaks for grass blade texture
                    c = Color.Lerp(c, yellowGreen, streaks * 0.15f);

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Texture2D GenerateDirtTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);

            // Woodland floor colors - rich browns with organic variation
            var baseBrown = new Color(0.42f, 0.32f, 0.22f);
            var darkBrown = new Color(0.28f, 0.20f, 0.12f);
            var lightBrown = new Color(0.52f, 0.42f, 0.28f);
            var leafLitter = new Color(0.48f, 0.38f, 0.18f);
            var darkLoam = new Color(0.22f, 0.16f, 0.10f);
            var twigColor = new Color(0.38f, 0.28f, 0.16f);

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Base soil variation
                    float noise = Mathf.PerlinNoise(u * 30 + offsetX, v * 30 + offsetY) * 0.5f;
                    noise += Mathf.PerlinNoise(u * 60 + offsetX, v * 60 + offsetY) * 0.3f;
                    noise += Mathf.PerlinNoise(u * 120 + offsetX, v * 120 + offsetY) * 0.2f;

                    // Start with base brown
                    Color c = Color.Lerp(darkBrown, lightBrown, noise);

                    // Add leaf litter patches
                    float leafNoise = Mathf.PerlinNoise(u * 15 + offsetX + 50, v * 15 + offsetY + 50);
                    if (leafNoise > 0.45f)
                    {
                        float leafAmount = (leafNoise - 0.45f) * 1.8f;
                        c = Color.Lerp(c, leafLitter, leafAmount * 0.4f);
                    }

                    // Add dark rich loam patches
                    float loamNoise = Mathf.PerlinNoise(u * 10 + offsetX + 150, v * 10 + offsetY + 150);
                    if (loamNoise > 0.6f)
                    {
                        float loamAmount = (loamNoise - 0.6f) * 2.5f;
                        c = Color.Lerp(c, darkLoam, loamAmount * 0.5f);
                    }

                    // Add small twig/debris details
                    float debrisNoise = Mathf.PerlinNoise(u * 80 + offsetY, v * 80 + offsetX);
                    if (debrisNoise > 0.75f)
                    {
                        c = Color.Lerp(c, twigColor, (debrisNoise - 0.75f) * 2f);
                    }

                    // Small pebble highlights
                    float pebbles = Mathf.PerlinNoise(u * 150 + offsetY * 2, v * 150 + offsetX * 2);
                    if (pebbles > 0.82f)
                    {
                        c = Color.Lerp(c, lightBrown * 1.2f, (pebbles - 0.82f) * 3f);
                    }

                    c = Color.Lerp(baseBrown, c, 0.85f);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Texture2D GenerateRockTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);

            // Realistic rock colors
            var baseGray = new Color(0.45f, 0.43f, 0.40f);
            var darkGray = new Color(0.28f, 0.26f, 0.24f);
            var lightGray = new Color(0.62f, 0.60f, 0.55f);
            var warmGray = new Color(0.50f, 0.45f, 0.38f);
            var coolGray = new Color(0.38f, 0.42f, 0.45f);
            var mossTint = new Color(0.35f, 0.42f, 0.32f);

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Base rock variation - large scale
                    float largeNoise = Mathf.PerlinNoise(u * 8 + offsetX, v * 8 + offsetY);

                    // Medium detail
                    float medNoise = Mathf.PerlinNoise(u * 25 + offsetX, v * 25 + offsetY) * 0.4f;
                    medNoise += Mathf.PerlinNoise(u * 50 + offsetX, v * 50 + offsetY) * 0.35f;
                    medNoise += Mathf.PerlinNoise(u * 100 + offsetX, v * 100 + offsetY) * 0.25f;

                    // Cracks and crevices - multiple directions
                    float crack1 = Mathf.PerlinNoise(u * 60 + offsetY * 2, v * 15 + offsetX);
                    crack1 = 1f - Mathf.Abs(crack1 * 2f - 1f);
                    crack1 = Mathf.Pow(crack1, 8f);

                    float crack2 = Mathf.PerlinNoise(u * 20 + offsetX * 2, v * 55 + offsetY);
                    crack2 = 1f - Mathf.Abs(crack2 * 2f - 1f);
                    crack2 = Mathf.Pow(crack2, 8f);

                    float cracks = Mathf.Max(crack1, crack2);

                    // Start with base color variation
                    Color c = Color.Lerp(darkGray, lightGray, medNoise);

                    // Add warm/cool variation based on large noise
                    c = Color.Lerp(c, warmGray, largeNoise * 0.3f);
                    c = Color.Lerp(c, coolGray, (1f - largeNoise) * 0.2f);

                    // Apply cracks (darken)
                    c = Color.Lerp(c, darkGray * 0.6f, cracks * 0.7f);

                    // Occasional moss in crevices
                    float mossNoise = Mathf.PerlinNoise(u * 12 + offsetX + 200, v * 12 + offsetY + 200);
                    if (mossNoise > 0.6f && medNoise < 0.4f)
                    {
                        float mossAmount = (mossNoise - 0.6f) * 2f * (0.4f - medNoise) * 2f;
                        c = Color.Lerp(c, mossTint, mossAmount * 0.4f);
                    }

                    // Surface highlights
                    float highlight = Mathf.PerlinNoise(u * 150 + offsetY, v * 150 + offsetX);
                    if (highlight > 0.8f)
                    {
                        c = Color.Lerp(c, lightGray * 1.15f, (highlight - 0.8f) * 2f);
                    }

                    c = Color.Lerp(baseGray, c, 0.85f);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Texture2D GenerateSnowTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);
            var baseColor = new Color(0.92f, 0.95f, 0.98f);
            var shadowColor = new Color(0.78f, 0.85f, 0.95f);
            var brightColor = new Color(0.98f, 0.99f, 1.0f);
            var blueTint = new Color(0.85f, 0.90f, 0.98f);

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Soft snow drifts
                    float noise = Mathf.PerlinNoise(u * 20 + offsetX, v * 20 + offsetY) * 0.4f;
                    noise += Mathf.PerlinNoise(u * 45 + offsetX, v * 45 + offsetY) * 0.35f;
                    noise += Mathf.PerlinNoise(u * 100 + offsetX, v * 100 + offsetY) * 0.25f;

                    // Sparkle effect
                    float sparkle = Mathf.PerlinNoise(u * 300 + offsetY, v * 300 + offsetX);
                    sparkle = sparkle > 0.85f ? (sparkle - 0.85f) * 6f : 0f;

                    // Wind-blown patterns
                    float wind = Mathf.PerlinNoise(u * 8 + offsetX, v * 30 + offsetY);
                    wind = Mathf.Pow(wind, 2) * 0.15f;

                    Color c = Color.Lerp(shadowColor, brightColor, noise);
                    c = Color.Lerp(c, blueTint, wind);
                    c = Color.Lerp(c, brightColor, sparkle);
                    c = Color.Lerp(baseColor, c, 0.6f);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        Texture2D GenerateCurseTexture(float offsetX, float offsetY)
        {
            var tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGB24, true);

            // Bright purple, reflective — like a glassy crystalline veil
            var baseViolet    = new Color(0.25f, 0.08f, 0.35f);
            var brightPurple  = new Color(0.55f, 0.15f, 0.70f);
            var hotPink       = new Color(0.70f, 0.20f, 0.55f);
            var deepVoid      = new Color(0.10f, 0.03f, 0.18f);
            var crystalGlint  = new Color(0.80f, 0.40f, 0.90f);

            for (int y = 0; y < textureResolution; y++)
            {
                for (int x = 0; x < textureResolution; x++)
                {
                    float u = x / (float)textureResolution;
                    float v = y / (float)textureResolution;

                    // Base swirl
                    float n = Mathf.PerlinNoise(u * 25 + offsetX, v * 25 + offsetY) * 0.5f
                            + Mathf.PerlinNoise(u * 50 + offsetX, v * 50 + offsetY) * 0.3f
                            + Mathf.PerlinNoise(u * 100 + offsetX, v * 100 + offsetY) * 0.2f;

                    Color c = Color.Lerp(baseViolet, brightPurple, n);

                    // Hot pink streaks (veins)
                    float vein = Mathf.PerlinNoise(u * 60 + offsetY, v * 60 + offsetX);
                    vein = Mathf.Pow(Mathf.Abs(vein * 2f - 1f), 4f);
                    c = Color.Lerp(c, hotPink, vein * 0.4f);

                    // Deep void pools
                    float voidN = Mathf.PerlinNoise(u * 15 + offsetX * 2, v * 15 + offsetY * 2);
                    if (voidN < 0.35f)
                        c = Color.Lerp(c, deepVoid, (0.35f - voidN) * 1.5f);

                    // Crystal glint specks (reflective highlights baked into albedo)
                    float glint = Mathf.PerlinNoise(u * 300 + offsetY * 3, v * 300 + offsetX * 3);
                    if (glint > 0.88f)
                        c = Color.Lerp(c, crystalGlint, (glint - 0.88f) * 6f);

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CURSED GROUND TERRAIN PAINTING
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Paint cursed ground texture onto the terrain splatmap at a world position.
        /// Called by PresentationSpawnSystem when a cursed ground entity is detected.
        /// </summary>
        /// <summary>
        /// Paints cursed ground on the terrain splatmap with organic, noise-distorted edges.
        /// Adjacent curse patches blend seamlessly into continuous coverage.
        ///
        /// Edge distortion uses multi-octave Perlin noise keyed to world position,
        /// so overlapping circles merge into one continuous organic shape rather
        /// than looking like a collection of perfect circles.
        /// </summary>
        public void PaintCursedGround(float worldX, float worldZ, float radius)
        {
            if (_terrain == null || _data == null) return;

            var layers = _data.terrainLayers;
            int curseIndex = IndexOf(layers, curse);
            if (curseIndex < 0) return;

            int res = _data.alphamapResolution;
            int layerCount = layers.Length;

            float terrainPosX = _terrain.transform.position.x;
            float terrainPosZ = _terrain.transform.position.z;
            float terrainSizeX = _data.size.x;
            float terrainSizeZ = _data.size.z;

            float normX = (worldX - terrainPosX) / terrainSizeX;
            float normZ = (worldZ - terrainPosZ) / terrainSizeZ;

            int centerPixelX = Mathf.RoundToInt(normX * res);
            int centerPixelZ = Mathf.RoundToInt(normZ * res);
            // Extend paint region by 30% to accommodate noise bulges
            int pixelRadius = Mathf.CeilToInt((radius * 1.3f / terrainSizeX) * res);

            int minX = Mathf.Max(0, centerPixelX - pixelRadius);
            int maxX = Mathf.Min(res - 1, centerPixelX + pixelRadius);
            int minZ = Mathf.Max(0, centerPixelZ - pixelRadius);
            int maxZ = Mathf.Min(res - 1, centerPixelZ + pixelRadius);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0) return;

            float[,,] splat = _data.GetAlphamaps(minX, minZ, width, height);

            // Noise parameters for organic edge distortion
            // Use world-space coordinates so adjacent patches share the same noise field
            // and blend into one continuous organic shape
            const float noiseScale1 = 0.15f;  // Large scale waviness
            const float noiseScale2 = 0.35f;  // Medium detail
            const float noiseScale3 = 0.7f;   // Fine tendrils
            const float noiseStrength = 0.35f; // How much the radius varies (±35%)

            float basePixelRadius = (radius / terrainSizeX) * res;
            float terrainCellSize = terrainSizeX / res;

            // Paint area is oversized (1.3x) so noise tendrils extend beyond the ring.
            // Extra world-space noise offsets make each node's shape unique but still
            // tile across overlapping nodes (shared world-space noise field).
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = (minX + x) - centerPixelX;
                    float dz = (minZ + z) - centerPixelZ;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);

                    // World-space pixel position for noise sampling (shared field)
                    float worldPixelX = (minX + x) * terrainCellSize + terrainPosX;
                    float worldPixelZ = (minZ + z) * terrainCellSize + terrainPosZ;

                    // ── DOMAIN WARPING: perturb the sample point with a low-frequency
                    //    noise field. Creates swirling, non-circular bulges and tendrils.
                    float warpScale = 0.08f;
                    float warpStrength = radius * 0.5f; // warp grows with blob size
                    float wx = Mathf.PerlinNoise(worldPixelX * warpScale + 55f, worldPixelZ * warpScale + 77f) - 0.5f;
                    float wz = Mathf.PerlinNoise(worldPixelX * warpScale + 211f, worldPixelZ * warpScale + 13f) - 0.5f;
                    float sampleX = worldPixelX + wx * warpStrength;
                    float sampleZ = worldPixelZ + wz * warpStrength;

                    // Multi-octave FBM on the warped coordinates (high contrast for
                    // strong tendril shapes instead of round bulges)
                    float noise = Mathf.PerlinNoise(sampleX * noiseScale1 + 100f, sampleZ * noiseScale1 + 100f) * 0.55f
                                + Mathf.PerlinNoise(sampleX * noiseScale2 + 200f, sampleZ * noiseScale2 + 200f) * 0.3f
                                + Mathf.PerlinNoise(sampleX * noiseScale3 + 300f, sampleZ * noiseScale3 + 300f) * 0.15f;
                    noise = Mathf.Pow(noise, 1.4f); // sharpen — deeper indentations
                    float noiseOffset = (noise - 0.5f) * 2f * noiseStrength;

                    // Compute local distorted radius. We also add a secondary edge-biased
                    // perturbation so lobes and pseudopods extend past the base radius.
                    float tendril = Mathf.PerlinNoise(worldPixelX * 0.05f + 400f, worldPixelZ * 0.05f + 500f);
                    tendril = Mathf.Pow(tendril, 3f); // rare but strong bulges
                    float localRadius = basePixelRadius * (1f + noiseOffset + tendril * 0.4f);
                    if (localRadius < 1f) localRadius = 1f;

                    if (dist > localRadius) continue;

                    // Hard edge: every cell inside the noise-distorted radius
                    // gets full curse weight, every cell outside gets none.
                    // No smoothstep / falloff. The organic shape still comes
                    // from the noise-warped localRadius above; the edge
                    // itself is binary.
                    float curseWeight = 1f;

                    // Blend: take the max so overlapping patches merge smoothly
                    float existingCurse = splat[z, x, curseIndex];
                    float newCurse = Mathf.Max(existingCurse, curseWeight);
                    float curseIncrease = newCurse - existingCurse;

                    if (curseIncrease <= 0.001f) continue;

                    // Reduce all other layers proportionally to maintain sum = 1
                    float otherTotal = 0f;
                    for (int l = 0; l < layerCount; l++)
                    {
                        if (l != curseIndex) otherTotal += splat[z, x, l];
                    }

                    if (otherTotal > 0.001f)
                    {
                        float scaleFactor = (1f - newCurse) / otherTotal;
                        for (int l = 0; l < layerCount; l++)
                        {
                            if (l != curseIndex)
                                splat[z, x, l] *= scaleFactor;
                        }
                    }

                    splat[z, x, curseIndex] = newCurse;
                }
            }

            _data.SetAlphamaps(minX, minZ, splat);
        }

        /// <summary>
        /// Removes cursed ground paint from the terrain splatmap at a world position.
        /// Called when cursed ground tiles recede after their owner node is destroyed.
        /// Redistributes the curse weight back to the other terrain layers proportionally.
        /// </summary>
        public void UnpaintCursedGround(float worldX, float worldZ, float radius)
        {
            if (_terrain == null || _data == null) return;

            var layers = _data.terrainLayers;
            int curseIndex = IndexOf(layers, curse);
            if (curseIndex < 0) return;

            int res = _data.alphamapResolution;
            int layerCount = layers.Length;

            float terrainPosX = _terrain.transform.position.x;
            float terrainPosZ = _terrain.transform.position.z;
            float terrainSizeX = _data.size.x;
            float terrainSizeZ = _data.size.z;

            float normX = (worldX - terrainPosX) / terrainSizeX;
            float normZ = (worldZ - terrainPosZ) / terrainSizeZ;

            int centerPixelX = Mathf.RoundToInt(normX * res);
            int centerPixelZ = Mathf.RoundToInt(normZ * res);
            int pixelRadius = Mathf.CeilToInt((radius * 1.3f / terrainSizeX) * res);

            int minX = Mathf.Max(0, centerPixelX - pixelRadius);
            int maxX = Mathf.Min(res - 1, centerPixelX + pixelRadius);
            int minZ = Mathf.Max(0, centerPixelZ - pixelRadius);
            int maxZ = Mathf.Min(res - 1, centerPixelZ + pixelRadius);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0) return;

            float[,,] splat = _data.GetAlphamaps(minX, minZ, width, height);

            float basePixelRadius = (radius / terrainSizeX) * res;

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = (minX + x) - centerPixelX;
                    float dz = (minZ + z) - centerPixelZ;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);

                    if (dist > basePixelRadius * 1.3f) continue;

                    float existingCurse = splat[z, x, curseIndex];
                    if (existingCurse < 0.01f) continue;

                    // Falloff: remove more at center, less at edge
                    float t = 1f - dist / (basePixelRadius * 1.3f);
                    t = Mathf.Clamp01(t);
                    float removeAmount = existingCurse * t;

                    float newCurse = existingCurse - removeAmount;
                    if (newCurse < 0.01f) newCurse = 0f;

                    // Redistribute removed curse weight to other layers proportionally
                    float freed = existingCurse - newCurse;
                    float otherTotal = 0f;
                    for (int l = 0; l < layerCount; l++)
                    {
                        if (l != curseIndex) otherTotal += splat[z, x, l];
                    }

                    if (otherTotal > 0.001f)
                    {
                        float scaleFactor = (otherTotal + freed) / otherTotal;
                        for (int l = 0; l < layerCount; l++)
                        {
                            if (l != curseIndex)
                                splat[z, x, l] *= scaleFactor;
                        }
                    }
                    else
                    {
                        // No other layers present — assign freed weight to grass (index 1)
                        int grassIndex = IndexOf(layers, grass);
                        if (grassIndex >= 0)
                            splat[z, x, grassIndex] = freed;
                    }

                    splat[z, x, curseIndex] = newCurse;
                }
            }

            _data.SetAlphamaps(minX, minZ, splat);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NOISE HELPER FUNCTIONS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fractal Brownian Motion — layered Perlin noise for natural terrain shapes.
        /// Returns a value roughly in the 0-1 range.
        /// </summary>
        float FBM(float x, float y, int octaves, float persistence, float scale)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = scale;
            float maxValue = 0f;

            for (int i = 0; i < octaves; i++)
            {
                value += Mathf.PerlinNoise(
                    x * frequency + _noiseOffsetX + i * 31.7f,
                    y * frequency + _noiseOffsetY + i * 47.3f
                ) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }

            return value / maxValue;
        }

        /// <summary>
        /// Ridge noise — creates sharp mountain ridge lines.
        /// Uses abs-inverted noise: 1 - |noise * 2 - 1|
        /// Returns a value in the 0-1 range with sharp peaks.
        /// </summary>
        float RidgeNoise(float x, float y, int octaves, float scale)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = scale;
            float maxValue = 0f;
            float weight = 1f;

            for (int i = 0; i < octaves; i++)
            {
                float n = Mathf.PerlinNoise(
                    x * frequency + _noiseOffsetX + i * 73.1f + 5000f,
                    y * frequency + _noiseOffsetY + i * 91.7f + 5000f
                );
                // Ridge transform: peaks where noise crosses 0.5
                n = 1f - Mathf.Abs(n * 2f - 1f);
                // Square for sharper ridges
                n = n * n;
                // Weight by previous octave for detail in valleys
                n *= weight;
                weight = Mathf.Clamp01(n * 1.2f);

                value += n * amplitude;
                maxValue += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return value / maxValue;
        }

        /// <summary>
        /// Domain warp — offsets sample coordinates by noise for organic distortion.
        /// Modifies x and y in place.
        /// </summary>
        void DomainWarp(ref float x, ref float y, float strength, float warpFreq)
        {
            float warpX = Mathf.PerlinNoise(
                x * warpFreq + _noiseOffsetX + 1000f,
                y * warpFreq + _noiseOffsetY + 1000f
            ) * 2f - 1f;
            float warpY = Mathf.PerlinNoise(
                x * warpFreq + _noiseOffsetX + 2000f,
                y * warpFreq + _noiseOffsetY + 2000f
            ) * 2f - 1f;

            x += warpX * strength;
            y += warpY * strength;
        }

        /// <summary>
        /// Edge falloff — smooth circular fade toward map boundaries.
        /// Returns 1.0 at center, 0.0 at edges.
        /// </summary>
        float EdgeFalloff(float worldX, float worldZ, float mapHalf, float borderWidth)
        {
            // Use max of absolute coordinates for square-ish falloff
            float edgeX = Mathf.Abs(worldX) / mapHalf;
            float edgeZ = Mathf.Abs(worldZ) / mapHalf;
            float edgeDist = Mathf.Max(edgeX, edgeZ);

            // Start fading at (1 - borderWidth), fully gone at 1.0
            float fadeStart = 1f - borderWidth;
            if (edgeDist <= fadeStart)
                return 1f;
            if (edgeDist >= 1f)
                return 0f;

            float t = (edgeDist - fadeStart) / borderWidth;
            // Smoothstep for gradual falloff
            t = t * t * (3f - 2f * t);
            return 1f - t;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PLAYER REGION GENERATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compute player spawn positions and store as IslandInfo for API compatibility.
        /// No actual circular islands — the continent provides all land.
        /// </summary>
        /// <summary>
        /// Rebuild <see cref="_islands"/> from the PlayerStart regions placed
        /// by ProceduralMapGen so spawn / island queries return the same
        /// coordinates the heightmap actually flattened.
        /// </summary>
        void SyncIslandsFromRegions(TheWaningBorder.World.Maps.MapRegionSet set)
        {
            if (set == null) return;
            var starts = new List<TheWaningBorder.World.Maps.MapRegion>();
            foreach (var r in set.WithTag(TheWaningBorder.World.Maps.RegionTag.PlayerStart))
                starts.Add(r);
            if (starts.Count == 0) return;

            _islands.Clear();
            for (int i = 0; i < starts.Count; i++)
            {
                _islands.Add(new IslandInfo
                {
                    Center = starts[i].center,
                    Radius = starts[i].radiusMeters,
                    IsMainland = true,
                    IsPlayerIsland = true,
                    PlayerIndex = i,
                });
            }
        }

        void GeneratePlayerRegions()
        {
            _islands.Clear();
            float mapHalf = (worldMax.x - worldMin.x) * 0.5f;

            int playerCount = Mathf.Max(2, GameSettings.TotalPlayers);
            float playerDist = mapHalf * spawnDistance;

            // Adjust spawn distance for higher player counts
            if (playerCount > 4)
                playerDist = mapHalf * (spawnDistance - 0.05f);
            if (playerCount > 6)
                playerDist = mapHalf * (spawnDistance - 0.08f);

            float angleStep = 360f / playerCount;
            float startAngle = _rng.Next(0, 360);


            for (int i = 0; i < playerCount; i++)
            {
                float angle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
                Vector2 center = new Vector2(
                    Mathf.Cos(angle) * playerDist,
                    Mathf.Sin(angle) * playerDist
                );

                _islands.Add(new IslandInfo
                {
                    Center = center,
                    Radius = spawnFlattenRadius,
                    IsMainland = true,
                    IsPlayerIsland = true,
                    PlayerIndex = i
                });

            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HEIGHTMAP GENERATION (Continental with layered noise)
        // ═══════════════════════════════════════════════════════════════════════

        void GenerateHeightmap()
        {
            int res = _data.heightmapResolution;
            float[,] heights = new float[res, res];

            // Flat test map: every cell at the spawn height. Slope = 0
            // everywhere so PassabilityGrid marks the whole map walkable, and
            // the water plane (at waterHeight=20) sits below the terrain
            // (spawnTargetHeight=30) where it can't be seen. Used for isolating
            // AI/pathfinding tests from terrain-noise variables.
            if (GameSettings.FlatTestMap)
            {
                float h = spawnTargetHeight / maxHeight;
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                        heights[y, x] = h;
                _data.SetHeights(0, 0, heights);
                return;
            }

            float mapSizeX = worldMax.x - worldMin.x;
            float mapSizeZ = worldMax.y - worldMin.y;
            float mapHalf = mapSizeX * 0.5f;

            // Normalized height references
            float waterNorm = waterHeight / maxHeight;


            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);

                    // Convert to world position
                    float worldX = worldMin.x + u * mapSizeX;
                    float worldZ = worldMin.y + v * mapSizeZ;

                    // === STEP 1: Domain warp for organic coastlines ===
                    float warpedX = worldX;
                    float warpedZ = worldZ;
                    DomainWarp(ref warpedX, ref warpedZ, warpStrength, warpScale);

                    // === STEP 2: Base continent shape (FBM) ===
                    float baseLand = FBM(warpedX, warpedZ, continentOctaves, continentPersistence, continentScale);

                    // === STEP 3: Edge falloff — ocean at map borders ===
                    float edge = EdgeFalloff(worldX, worldZ, mapHalf, edgeBorderFraction);
                    baseLand *= edge;

                    // === STEP 4: Boost land around spawn zones ===
                    // Ensures player bases are always on land
                    foreach (var region in _islands)
                    {
                        if (!region.IsPlayerIsland) continue;
                        float dist = Vector2.Distance(new Vector2(worldX, worldZ), region.Center);
                        float spawnInfluence = 1f - Mathf.Clamp01(dist / (spawnFlattenRadius + spawnBlendRadius + 30f));
                        spawnInfluence = spawnInfluence * spawnInfluence * (3f - 2f * spawnInfluence); // smoothstep
                        // Boost baseLand to ensure it's above ocean threshold near spawns
                        float boostTarget = oceanThreshold + beachWidth + 0.15f;
                        baseLand = Mathf.Max(baseLand, Mathf.Lerp(baseLand, boostTarget, spawnInfluence));
                    }

                    // === STEP 5: Compute height based on baseLand value ===
                    float height;

                    if (baseLand < oceanThreshold)
                    {
                        // Ocean — gradual depth variation
                        float oceanDepth = Mathf.InverseLerp(0f, oceanThreshold, baseLand);
                        // Sea floor ranges from 5 to 15 world units (normalized)
                        float seaFloorLow = 5f / maxHeight;
                        float seaFloorHigh = 15f / maxHeight;
                        height = Mathf.Lerp(seaFloorLow, seaFloorHigh, oceanDepth);
                    }
                    else if (baseLand < oceanThreshold + beachWidth)
                    {
                        // Beach transition — smooth rise from water to low land
                        float beachT = Mathf.InverseLerp(oceanThreshold, oceanThreshold + beachWidth, baseLand);
                        beachT = beachT * beachT * (3f - 2f * beachT); // smoothstep
                        float beachLow = waterHeight / maxHeight;
                        float beachHigh = 23f / maxHeight;
                        height = Mathf.Lerp(beachLow, beachHigh, beachT);
                    }
                    else
                    {
                        // Land — base height + hills + mountains
                        float landBase = 25f; // world units — low plains start

                        // How far into land we are (0 = coast, 1 = deep interior)
                        float landDepth = Mathf.InverseLerp(oceanThreshold + beachWidth, 1f, baseLand);

                        // Rolling hills — gentle FBM
                        float hills = FBM(worldX, worldZ, 3, 0.5f, hillScale);
                        hills = (hills - 0.5f) * 2f; // center around 0, range approx -1 to 1
                        float hillHeight = hills * hillAmplitude * Mathf.Clamp01(landDepth * 1.5f);

                        // Mountains — ridge noise, only in deep interior
                        float mountainMask = Mathf.InverseLerp(mountainThreshold, mountainThreshold + 0.15f, baseLand);
                        mountainMask = mountainMask * mountainMask; // ease-in for gradual mountain onset
                        float ridges = RidgeNoise(worldX, worldZ, 4, mountainScale);
                        float mountainHeight = ridges * mountainAmplitude * mountainMask;

                        // Combine
                        float totalHeight = landBase + hillHeight + mountainHeight;

                        // Normalize to terrain 0-1 range
                        height = totalHeight / maxHeight;
                    }

                    // === STEP 6: Spawn zone flattening ===
                    float spawnFlatten = 0f;
                    foreach (var region in _islands)
                    {
                        if (!region.IsPlayerIsland) continue;
                        float dist = Vector2.Distance(new Vector2(worldX, worldZ), region.Center);
                        if (dist < spawnFlattenRadius + spawnBlendRadius)
                        {
                            float influence;
                            if (dist < spawnFlattenRadius)
                            {
                                influence = 1f;
                            }
                            else
                            {
                                float t = (dist - spawnFlattenRadius) / spawnBlendRadius;
                                t = t * t * (3f - 2f * t); // smoothstep
                                influence = 1f - t;
                            }
                            spawnFlatten = Mathf.Max(spawnFlatten, influence);
                        }
                    }

                    if (spawnFlatten > 0f)
                    {
                        float targetNorm = spawnTargetHeight / maxHeight;
                        height = Mathf.Lerp(height, targetNorm, spawnFlatten);
                    }

                    heights[y, x] = Mathf.Clamp01(height);
                }
            }

            // === SMOOTHING PASS: average with neighbors to reduce harsh transitions ===
            int smoothPasses = 2;
            for (int pass = 0; pass < smoothPasses; pass++)
            {
                float[,] smoothed = new float[res, res];
                for (int sy = 0; sy < res; sy++)
                {
                    for (int sx = 0; sx < res; sx++)
                    {
                        float sum = heights[sy, sx] * 4f; // center weight
                        float count = 4f;

                        // Sample neighbors
                        if (sx > 0)     { sum += heights[sy, sx - 1]; count += 1f; }
                        if (sx < res-1) { sum += heights[sy, sx + 1]; count += 1f; }
                        if (sy > 0)     { sum += heights[sy - 1, sx]; count += 1f; }
                        if (sy < res-1) { sum += heights[sy + 1, sx]; count += 1f; }
                        // Diagonals (half weight)
                        if (sx > 0 && sy > 0)         { sum += heights[sy-1, sx-1] * 0.5f; count += 0.5f; }
                        if (sx < res-1 && sy > 0)     { sum += heights[sy-1, sx+1] * 0.5f; count += 0.5f; }
                        if (sx > 0 && sy < res-1)     { sum += heights[sy+1, sx-1] * 0.5f; count += 0.5f; }
                        if (sx < res-1 && sy < res-1) { sum += heights[sy+1, sx+1] * 0.5f; count += 0.5f; }

                        smoothed[sy, sx] = sum / count;
                    }
                }
                heights = smoothed;
            }

            _data.SetHeights(0, 0, heights);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SPLATMAP PAINTING
        // ═══════════════════════════════════════════════════════════════════════

        void PaintSplatmaps()
        {
            var layers = BuildLayerArray();
            _data.terrainLayers = layers;

            int res = _data.alphamapResolution;
            int layerCount = layers.Length;

            if (layerCount == 0) return;

            float[,,] splat = new float[res, res, layerCount];

            int iSand = IndexOf(layers, sand);
            int iGrass = IndexOf(layers, grass);
            int iDirt = IndexOf(layers, dirt);
            int iRock = IndexOf(layers, rock);
            int iSnow = IndexOf(layers, snow);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);

                    // Get height in world units
                    float heightUnits = _data.GetInterpolatedHeight(u, v);

                    // Get slope (0 = flat, 1 = vertical)
                    Vector3 normal = _data.GetInterpolatedNormal(u, v);
                    float slope = 1f - normal.y;

                    // Noise for natural variation
                    float patchNoise = Mathf.PerlinNoise(u * 20f + _noiseOffsetX, v * 20f + _noiseOffsetY);
                    float woodlandNoise = Mathf.PerlinNoise(u * 8f + _noiseOffsetX * 2f, v * 8f + _noiseOffsetY * 2f);

                    float wSand = 0f, wGrass = 0f, wDirt = 0f, wRock = 0f, wSnow = 0f;

                    // === HEIGHT + SLOPE BASED TEXTURING ===

                    if (heightUnits < waterHeight - 1f)
                    {
                        // Underwater = sand
                        wSand = 1f;
                    }
                    else if (heightUnits < waterHeight + 3f)
                    {
                        // Beach zone — sand to grass transition
                        float beachT = Mathf.InverseLerp(waterHeight - 1f, waterHeight + 3f, heightUnits);
                        wSand = 1f - beachT * 0.7f;
                        wGrass = beachT * 0.5f;
                        wDirt = beachT * 0.2f;
                    }
                    else if (heightUnits < 40f)
                    {
                        // Low land — grassy plains
                        if (slope < 0.15f)
                        {
                            // Flat — mostly grass with dirt/woodland patches
                            wGrass = 0.75f;

                            if (woodlandNoise > 0.5f)
                            {
                                float woodlandAmount = (woodlandNoise - 0.5f) * 2f;
                                wDirt = woodlandAmount * 0.45f;
                                wGrass -= woodlandAmount * 0.35f;
                            }

                            if (patchNoise > 0.7f)
                            {
                                float patchAmount = (patchNoise - 0.7f) * 2f;
                                wDirt += patchAmount * 0.25f;
                                wGrass -= patchAmount * 0.15f;
                            }
                        }
                        else if (slope < 0.35f)
                        {
                            // Mild slope — grass/dirt blend
                            float t = Mathf.InverseLerp(0.15f, 0.35f, slope);
                            wGrass = 0.6f * (1f - t);
                            wDirt = 0.3f + t * 0.3f;
                            wRock = t * 0.3f;
                        }
                        else
                        {
                            // Steep slope — rocky
                            float t = Mathf.InverseLerp(0.35f, 0.6f, slope);
                            wDirt = 0.3f * (1f - t);
                            wRock = 0.7f + t * 0.3f;
                        }
                    }
                    else if (heightUnits < 55f)
                    {
                        // Mid land — hills, more dirt and rock
                        if (slope < 0.25f)
                        {
                            // Gentle hillside — grass/dirt
                            float hillT = Mathf.InverseLerp(40f, 55f, heightUnits);
                            wGrass = 0.55f * (1f - hillT * 0.4f);
                            wDirt = 0.25f + hillT * 0.2f;
                            wRock = hillT * 0.15f;

                            // Woodland patches
                            if (woodlandNoise > 0.45f)
                            {
                                float amount = (woodlandNoise - 0.45f) * 2f;
                                wDirt += amount * 0.3f;
                                wGrass -= amount * 0.2f;
                            }
                        }
                        else
                        {
                            // Steep hillside — rock/dirt
                            float t = Mathf.InverseLerp(0.25f, 0.5f, slope);
                            wGrass = 0.15f * (1f - t);
                            wDirt = 0.35f * (1f - t);
                            wRock = 0.5f + t * 0.5f;
                        }
                    }
                    else if (heightUnits < 70f)
                    {
                        // High land — rocky highlands
                        float highT = Mathf.InverseLerp(55f, 70f, heightUnits);
                        if (slope < 0.3f)
                        {
                            wGrass = 0.15f * (1f - highT);
                            wDirt = 0.25f * (1f - highT);
                            wRock = 0.6f + highT * 0.3f;
                        }
                        else
                        {
                            // Cliff face
                            wRock = 0.9f + highT * 0.1f;
                            wDirt = 0.1f * (1f - highT);
                        }
                    }
                    else
                    {
                        // Mountain peaks — rock with snow
                        float snowT = Mathf.InverseLerp(70f, 85f, heightUnits);
                        wRock = 0.8f * (1f - snowT * 0.5f);
                        wSnow = snowT * 0.7f;

                        // More snow on flat surfaces
                        if (slope < 0.2f)
                        {
                            wSnow += 0.2f;
                            wRock -= 0.15f;
                        }
                    }

                    // Global cliff override — any very steep slope gets rock
                    if (slope > 0.45f)
                    {
                        float cliffT = Mathf.InverseLerp(0.45f, 0.7f, slope);
                        cliffT = Mathf.Clamp01(cliffT);
                        wSand *= (1f - cliffT);
                        wGrass *= (1f - cliffT);
                        wDirt *= (1f - cliffT * 0.7f);
                        wRock += cliffT * 0.8f;
                    }

                    // Normalize weights
                    float sum = wSand + wGrass + wDirt + wRock + wSnow + 0.0001f;

                    if (iSand >= 0) splat[y, x, iSand] = wSand / sum;
                    if (iGrass >= 0) splat[y, x, iGrass] = wGrass / sum;
                    if (iDirt >= 0) splat[y, x, iDirt] = wDirt / sum;
                    if (iRock >= 0) splat[y, x, iRock] = wRock / sum;
                    if (iSnow >= 0) splat[y, x, iSnow] = wSnow / sum;
                }
            }

            _data.SetAlphamaps(0, 0, splat);
        }

        TerrainLayer[] BuildLayerArray()
        {
            // Splat layer order. Kept stable so existing materials / saved
            // alphamaps line up. Indices: sand=0, grass=1, dirt=2, rock=3,
            // snow=4, curse=5, forest=6, seabed=7, cliff=8.
            var list = new List<TerrainLayer>();
            if (sand != null) list.Add(sand);
            if (grass != null) list.Add(grass);
            if (dirt != null) list.Add(dirt);
            if (rock != null) list.Add(rock);
            if (snow != null) list.Add(snow);
            if (curse != null) list.Add(curse);
            if (forestFloor != null) list.Add(forestFloor);
            if (seaBed != null) list.Add(seaBed);
            if (cliffStone != null) list.Add(cliffStone);
            return list.ToArray();
        }

        static int IndexOf(TerrainLayer[] arr, TerrainLayer layer)
        {
            if (layer == null || arr == null) return -1;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == layer) return i;
            return -1;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC UTILITY METHODS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if a world position is on land (above sea level).
        /// </summary>
        public bool IsOnLand(Vector3 worldPos)
        {
            if (_terrain == null || _data == null) return true;
            float height = _terrain.SampleHeight(worldPos);
            return height > waterHeight;
        }

        /// <summary>
        /// Check if a world position is in water.
        /// </summary>
        public bool IsInWater(Vector3 worldPos)
        {
            return !IsOnLand(worldPos);
        }

        /// <summary>
        /// Get the nearest player region to a world position.
        /// </summary>
        public IslandInfo? GetNearestIsland(Vector3 worldPos)
        {
            if (_islands.Count == 0) return null;

            Vector2 pos2D = new Vector2(worldPos.x, worldPos.z);
            IslandInfo nearest = _islands[0];
            float minDist = float.MaxValue;

            foreach (var island in _islands)
            {
                float dist = Vector2.Distance(pos2D, island.Center);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = island;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Get the region assigned to a specific player.
        /// </summary>
        public IslandInfo? GetPlayerIsland(int playerIndex)
        {
            foreach (var island in _islands)
            {
                if (island.IsPlayerIsland && island.PlayerIndex == playerIndex)
                    return island;
            }
            return null;
        }

        /// <summary>
        /// Get a valid spawn position in a specific player region.
        /// </summary>
        public Vector3 GetSpawnPositionOnIsland(int islandIndex)
        {
            if (islandIndex < 0 || islandIndex >= _islands.Count)
                return Vector3.zero;

            var island = _islands[islandIndex];

            // Find a position toward the center of the spawn region
            float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
            float offsetDist = island.Radius * 0.3f;

            Vector2 offset = new Vector2(
                Mathf.Cos(angle) * offsetDist,
                Mathf.Sin(angle) * offsetDist
            );

            Vector2 spawnPos2D = island.Center + offset;
            float height = TerrainUtility.GetHeight(spawnPos2D.x, spawnPos2D.y);

            return new Vector3(spawnPos2D.x, height, spawnPos2D.y);
        }

        /// <summary>
        /// Get spawn positions — each player spawns at their designated region center.
        /// Continental map: guaranteed flat areas at spawn points.
        /// </summary>
        public Vector3[] GetMultiplayerSpawnPositions(int playerCount)
        {
            var positions = new Vector3[playerCount];

            // Each player spawns at their designated region
            for (int i = 0; i < playerCount; i++)
            {
                var playerRegion = GetPlayerIsland(i);

                if (playerRegion.HasValue)
                {
                    var region = playerRegion.Value;
                    // Spawn at region center (flattened, safe location)
                    float y = TerrainUtility.GetHeight(region.Center.x, region.Center.y);
                    positions[i] = new Vector3(region.Center.x, y, region.Center.y);

                }
                else
                {
                    // Fallback: distribute around map center
                    float radius = (worldMax.x - worldMin.x) * 0.4f;
                    float angle = i * Mathf.PI * 2f / playerCount;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    float y = TerrainUtility.GetHeight(x, z);
                    positions[i] = new Vector3(x, y, z);

                }
            }

            return positions;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // FOREST PATCHES (Unity terrain trees — GPU instanced)
        // ═══════════════════════════════════════════════════════════════════════
        //
        // ObstacleBootstrap drives forest placement by calling
        // PaintTreesOnBrownGround() on this terrain. That walks the
        // L_FOREST splat at ForestTreeSpacing, plants Unity terrain trees
        // wherever the splat reads brown ground, blocks the matching
        // PassabilityGrid cells, and returns macro-cell centres. The
        // caller then emits one ObstacleTag entity per macro centre so
        // NavMeshManager carves matching boxes out of the navmesh.
        //
        // Tree prefabs are lazy-loaded once from
        // Resources/Prefabs/Nature/Trees and registered as
        // TerrainData.treePrototypes.
        //
        // Unity terrain trees are GPU-instanced and billboarded at distance
        // by the terrain renderer, so even ~1000 trees per map cost less than
        // a few hundred individual MeshRenderers.

        // World-space spacing between candidate tree slots. Dialled back
        // from 0.7 m (tightly-packed canopy) to 1.6 m so brown-ground areas
        // get readable forest cover without overcrowding. Trees still
        // overlap slightly visually thanks to the per-prefab canopy width.
        private const float ForestTreeSpacing = 1.6f;
        // Forest splatmap layer index — must match ProceduralSplat.L_FOREST.
        private const int ForestSplatLayer = 3;
        // L_FOREST weight above which we plant a tree at the sample point.
        // Splat fades from 0 → 1 across the forestMask smoothstep band; 0.30
        // catches the dense interior plus the immediate fringe.
        private const float ForestSplatThreshold = 0.30f;
        // Macro-cell size for NavMesh obstacle entities. Each macro cell
        // that contains at least one painted tree becomes a single
        // ObstacleTag entity at its centre — NavMeshManager carves a
        // matching Box source so units refuse to path through brown
        // ground. 5 m gives ~50 × 50 cells per map (~2500 total), of
        // which only the brown-ground subset emits entities (~hundreds).
        private const float ForestMacroCellSize = 5f;

        // Tree prototypes registered on TerrainData. Indexed in the order
        // returned by LoadForestTreePrefabs() so AddTreeInstance can pick a
        // prototype-index by hash.
        private bool _forestProtosReady;

        public void EnsureForestTreePrototypes()
        {
            if (_forestProtosReady) return;
            if (_data == null) return;

            var prefabs = LoadForestTreePrefabs();
            if (prefabs == null || prefabs.Length == 0)
            {
                Debug.LogError("[ProceduralTerrain] No tree prefabs found in Resources/Prefabs/Nature/Trees");
                _forestProtosReady = true; // give up — don't spam this path
                return;
            }

            var protos = new TreePrototype[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                protos[i] = new TreePrototype
                {
                    prefab = prefabs[i],
                    bendFactor = 0f, // no wind on these — they're static decoration
                };
            }
            _data.treePrototypes = protos;
            _data.RefreshPrototypes();
            _forestProtosReady = true;
        }

        /// <summary>
        /// Paint Unity terrain trees on every brown-ground cell of the
        /// splatmap. Replaces the older "forest disc" approach with a
        /// mask-driven painter: wherever the L_FOREST splat weight is
        /// above <see cref="ForestSplatThreshold"/>, plant a tree.
        ///
        /// Returns the centres of macro cells that ended up with at least
        /// one tree — the caller (ObstacleBootstrap) emits one ObstacleTag
        /// entity per centre so NavMeshManager carves matching boxes out
        /// of the navmesh. Same call also stamps every painted cell as
        /// ObstacleBlocked in PassabilityGrid so footprints stay impassable
        /// even on the cell-based building-placement path.
        /// </summary>
        public List<Vector3> PaintTreesOnBrownGround()
        {
            var macroCenters = new List<Vector3>(256);
            if (_terrain == null || _data == null) return macroCenters;
            EnsureForestTreePrototypes();
            if (!_forestProtosReady) return macroCenters;
            int protoCount = _data.treePrototypes.Length;
            if (protoCount == 0) return macroCenters;

            float terrainOriginX = _terrain.transform.position.x;
            float terrainOriginZ = _terrain.transform.position.z;
            float sizeX = _data.size.x;
            float sizeZ = _data.size.z;

            // Pull the alphamap once. Sampling per-cell from the cached
            // array is far cheaper than calling terrainData.GetAlphamaps
            // per tree slot.
            int alphaRes = _data.alphamapResolution;
            int layerCount = _data.alphamapLayers;
            if (ForestSplatLayer >= layerCount)
            {
                Debug.LogWarning($"[ProceduralTerrain] PaintTreesOnBrownGround: L_FOREST layer {ForestSplatLayer} not in alphamap ({layerCount} layers)");
                return macroCenters;
            }
            float[,,] splat = _data.GetAlphamaps(0, 0, alphaRes, alphaRes);

            var passGrid = PassabilityGrid.Instance;

            // Snapshot existing tree instances so we can append.
            var existing = _data.treeInstances;
            int existingCount = existing != null ? existing.Length : 0;
            var added = new List<TreeInstance>(2048);

            // Macro-cell occupancy — one bool per ForestMacroCellSize-sized
            // cell of the map. Flip true the first time any tree lands in
            // a cell; the centre gets emitted once at the end.
            int macroW = Mathf.CeilToInt(sizeX / ForestMacroCellSize);
            int macroH = Mathf.CeilToInt(sizeZ / ForestMacroCellSize);
            var macroOccupied = new bool[macroW * macroH];

            // Step in world-space across the terrain rect. The grid uses a
            // checkerboard half-offset on alternating rows so trees don't
            // align in straight visual lines.
            int placed = 0;
            uint seedHash = unchecked((uint)seed * 0x9E3779B9u);
            int rowIdx = 0;
            for (float wz = terrainOriginZ + ForestTreeSpacing * 0.5f;
                 wz < terrainOriginZ + sizeZ;
                 wz += ForestTreeSpacing, rowIdx++)
            {
                float xOffset = (rowIdx & 1) == 0 ? 0f : ForestTreeSpacing * 0.5f;
                for (float wx = terrainOriginX + ForestTreeSpacing * 0.5f + xOffset;
                     wx < terrainOriginX + sizeX;
                     wx += ForestTreeSpacing)
                {
                    // Per-cell deterministic hash for jitter + variant.
                    uint h = seedHash
                        ^ unchecked((uint)Mathf.RoundToInt(wx * 374.0f))
                        ^ unchecked((uint)Mathf.RoundToInt(wz * 727.0f));
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;

                    float jx = ((h & 0xFFFF) / 65535f - 0.5f) * ForestTreeSpacing * 0.40f;
                    float jz = (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * ForestTreeSpacing * 0.40f;
                    float jwx = wx + jx;
                    float jwz = wz + jz;

                    // Sample L_FOREST weight at the jittered world position.
                    int ax = Mathf.Clamp(Mathf.FloorToInt((jwx - terrainOriginX) / sizeX * alphaRes), 0, alphaRes - 1);
                    int az = Mathf.Clamp(Mathf.FloorToInt((jwz - terrainOriginZ) / sizeZ * alphaRes), 0, alphaRes - 1);
                    float forestWeight = splat[az, ax, ForestSplatLayer];
                    if (forestWeight < ForestSplatThreshold) continue;

                    // Skip if below water (forest mask SHOULD already exclude
                    // this, but defensive check).
                    float wy = TerrainUtility.GetHeight(jwx, jwz);
                    if (wy < waterHeight + 0.5f) continue;

                    // Convert to terrain-normalised position for TreeInstance.
                    float u = (jwx - terrainOriginX) / sizeX;
                    float v = (jwz - terrainOriginZ) / sizeZ;
                    if (u < 0f || u > 1f || v < 0f || v > 1f) continue;

                    int proto = (int)((h >> 5) % (uint)protoCount);
                    float scale = Mathf.Lerp(0.85f, 1.25f, ((h >> 11) & 0xFFF) / 4095f);

                    added.Add(new TreeInstance
                    {
                        position = new Vector3(u, 0f, v),
                        prototypeIndex = proto,
                        widthScale = scale,
                        heightScale = scale,
                        color = Color.white,
                        lightmapColor = Color.white,
                    });
                    placed++;

                    // Mark a small block of PassabilityGrid cells around the
                    // tree so units can't squeeze between trunks.
                    if (passGrid != null)
                        passGrid.BlockObstacle(new Vector3(jwx, wy, jwz), Mathf.Max(0.8f, passGrid.CellSize));

                    // Mark the macro cell so the caller emits an ObstacleTag
                    // entity for it.
                    int mx = Mathf.Clamp(Mathf.FloorToInt((jwx - terrainOriginX) / ForestMacroCellSize), 0, macroW - 1);
                    int mz = Mathf.Clamp(Mathf.FloorToInt((jwz - terrainOriginZ) / ForestMacroCellSize), 0, macroH - 1);
                    macroOccupied[mz * macroW + mx] = true;
                }
            }

            if (added.Count == 0)
            {
                Debug.LogWarning("[ProceduralTerrain] PaintTreesOnBrownGround placed 0 trees — splat L_FOREST coverage is empty or threshold too high");
                return macroCenters;
            }

            // Flush the combined instances back in one call.
            var combined = new TreeInstance[existingCount + added.Count];
            if (existingCount > 0) System.Array.Copy(existing, combined, existingCount);
            for (int i = 0; i < added.Count; i++) combined[existingCount + i] = added[i];
            _data.SetTreeInstances(combined, snapToHeightmap: true);

            // Emit one centre per occupied macro cell.
            for (int mz = 0; mz < macroH; mz++)
            {
                float cz = terrainOriginZ + (mz + 0.5f) * ForestMacroCellSize;
                for (int mx = 0; mx < macroW; mx++)
                {
                    if (!macroOccupied[mz * macroW + mx]) continue;
                    float cx = terrainOriginX + (mx + 0.5f) * ForestMacroCellSize;
                    macroCenters.Add(new Vector3(cx, TerrainUtility.GetHeight(cx, cz), cz));
                }
            }

            Debug.Log($"[ProceduralTerrain] PaintTreesOnBrownGround → {placed} trees, {macroCenters.Count} macro cells (spacing {ForestTreeSpacing} m, threshold {ForestSplatThreshold})");
            return macroCenters;
        }

        /// <summary>Macro-cell size used by PaintTreesOnBrownGround. Exposed
        /// so the caller can size the matching NavMesh box obstacles.</summary>
        public float ForestMacroCellSizeMeters => ForestMacroCellSize;

        // Tree prefabs come from Resources/Prefabs/Nature/Trees. The folder
        // is hand-curated — load every .prefab in it so dropping in more
        // variants Just Works without code changes.
        private GameObject[] _forestTreePrefabs;
        private GameObject[] LoadForestTreePrefabs()
        {
            if (_forestTreePrefabs != null && _forestTreePrefabs.Length > 0)
                return _forestTreePrefabs;

            var loaded = Resources.LoadAll<GameObject>("Prefabs/Nature/Trees");
            if (loaded == null || loaded.Length == 0) return null;
            _forestTreePrefabs = loaded;
            return _forestTreePrefabs;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LEGACY ORGANIC TREE PLACEMENT (deprecated — kept for fallback only)
        // ═══════════════════════════════════════════════════════════════════════

        [Header("Trees")]
        [Tooltip("Enable noise-based realistic tree placement across terrain")]
        public bool placeTrees = true;
        [Tooltip("Perlin noise scale for tree placement (lower = larger forest patches)")]
        public float treeNoiseScale = 0.012f;
        [Tooltip("Noise threshold above which trees spawn (0.45-0.55 dense, 0.6+ sparse)")]
        public float treeDensityThreshold = 0.45f;
        [Tooltip("Grid spacing in world units between tree candidate points (smaller = denser)")]
        public float treeGridSpacing = 2.0f;
        [Tooltip("Maximum slope (delta height per meter) for tree placement")]
        public float treeMaxSlope = 0.6f;
        [Tooltip("Minimum height above water for trees (skips beach zone)")]
        public float treeMinHeightAboveWater = 4f;

        private GameObject _treeRoot;
        private Transform _treesParent;

        /// <summary>
        /// Public entry point to place realistic trees across the terrain using
        /// noise-based forest placement with the Spruce_008 prefab. Uses direct
        /// Instantiate + StaticBatchingUtility (Unity Terrain Tree system does
        /// not work with the URP/Lit shader the prefab uses).
        /// </summary>
        public void PlaceTerrainTrees(Vector3[] playerSpawnPositions)
        {
            if (!placeTrees) return;
            if (GameSettings.FlatTestMap) return; // flat test map → no decorative trees
            if (_treeRoot != null) return; // already placed

            // Paint forest floor splatmap first so the brown leafy texture is
            // visible beneath the trees. Uses the same noise field as placement.
            PaintForestFloorSplat(playerSpawnPositions);

            // Load all tree prefabs for variety — dev/editor path first, then Resources fallback
            var treePrefabs = LoadTreePrefabs();
            if (treePrefabs == null || treePrefabs.Length == 0)
            {
                Debug.LogError("[ProceduralTerrain] No tree prefabs could be loaded — check path to Spruce_008.prefab or copy it to Assets/Resources/Trees/");
                return;
            }

            _treeRoot = new GameObject("TerrainTrees");
            _treesParent = _treeRoot.transform;

            int worldSize = (int)_data.size.x;
            float halfSize = worldSize * 0.5f;

            // Player spawn exclusion: inner clear (no trees), outer fade (sparse)
            const float innerClear = 30f;
            const float outerFade = 70f;

            int placed = 0;
            int candidates = 0;
            int rejNoise = 0, rejSpawn = 0, rejWater = 0, rejSlope = 0;
            var spawnList = new List<GameObject>();

            for (float z = -halfSize; z < halfSize; z += treeGridSpacing)
            {
                for (float x = -halfSize; x < halfSize; x += treeGridSpacing)
                {
                    candidates++;
                    // Deterministic hash for jitter (same seed always produces same forest)
                    uint hash = (uint)((int)(x * 7919) ^ (int)(z * 31) ^ GameSettings.SpawnSeed);
                    hash ^= hash >> 13; hash *= 0x5bd1e995; hash ^= hash >> 15;
                    float jitterX = ((hash & 0xFFFF) / 65535f - 0.5f) * treeGridSpacing * 0.7f;
                    float jitterZ = (((hash >> 16) & 0xFFFF) / 65535f - 0.5f) * treeGridSpacing * 0.7f;
                    float wx = x + jitterX;
                    float wz = z + jitterZ;

                    // 3-octave FBM noise for organic forest shapes
                    float n = Mathf.PerlinNoise(wx * treeNoiseScale + 13.7f, wz * treeNoiseScale + 91.3f) * 0.5f
                            + Mathf.PerlinNoise(wx * treeNoiseScale * 2.1f + 217f, wz * treeNoiseScale * 2.1f + 301f) * 0.3f
                            + Mathf.PerlinNoise(wx * treeNoiseScale * 4.7f + 41f, wz * treeNoiseScale * 4.7f + 173f) * 0.2f;

                    // Raise threshold near player spawns (quadratic falloff)
                    float threshold = treeDensityThreshold;
                    if (playerSpawnPositions != null)
                    {
                        foreach (var spawn in playerSpawnPositions)
                        {
                            float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(spawn.x, spawn.z));
                            if (d < innerClear) { threshold = 10f; break; }  // impossible threshold = no trees
                            if (d < innerClear + outerFade)
                            {
                                float t = (d - innerClear) / outerFade;
                                threshold = Mathf.Lerp(10f, treeDensityThreshold, t * t);
                            }
                        }
                    }

                    if (n < threshold)
                    {
                        if (threshold < 5f) rejNoise++; // ignore the impossible-threshold spawn-zone case
                        else rejSpawn++;
                        continue;
                    }

                    // Skip water + beach + steep slopes. Beach zone goes up to
                    // ~waterHeight+3 (sandy shore) and we add a small buffer so
                    // forests don't bleed onto the sand.
                    float y = TerrainUtility.GetHeight(wx, wz);
                    if (y < waterHeight + treeMinHeightAboveWater) { rejWater++; continue; }
                    float yN = TerrainUtility.GetHeight(wx, wz + 2f);
                    float yE = TerrainUtility.GetHeight(wx + 2f, wz);
                    float slope = Mathf.Max(Mathf.Abs(yN - y), Mathf.Abs(yE - y)) / 2f;
                    if (slope > treeMaxSlope) { rejSlope++; continue; }

                    // Pick a prefab variant from the loaded array
                    var treePrefab = treePrefabs[(int)(hash >> 4) % treePrefabs.Length];

                    // Instantiate tree with variation
                    var tree = Object.Instantiate(treePrefab, new Vector3(wx, y, wz),
                        Quaternion.Euler(0, ((hash >> 8) & 0xFFFF) / 65535f * 360f, 0),
                        _treesParent);

                    // Scale variation: 0.15 - 0.7 (smaller than prefab default)
                    float scaleHash = (((hash >> 20) & 0xFFF) / 4095f);
                    float scale = Mathf.Lerp(0.15f, 0.7f, scaleHash);
                    tree.transform.localScale = Vector3.one * scale;

                    // Tint variation via MaterialPropertyBlock (dark green to olive/brown)
                    ApplyTreeTint(tree, scaleHash);

                    tree.isStatic = true;
                    spawnList.Add(tree);
                    placed++;
                }
            }

            // Batch all trees into fewer draw calls
            if (spawnList.Count > 0)
                StaticBatchingUtility.Combine(spawnList.ToArray(), _treeRoot);

            Debug.Log($"[ProceduralTerrain] Trees: placed {placed} / {candidates} candidates " +
                      $"(rejected: noise {rejNoise}, spawnzone {rejSpawn}, water {rejWater}, slope {rejSlope}) " +
                      $"on {worldSize}x{worldSize} terrain | waterHeight={waterHeight} threshold={treeDensityThreshold}");
        }

        /// <summary>
        /// Paints the forest-floor terrain layer wherever the same noise field
        /// used for tree placement exceeds the threshold. Produces a smooth brown
        /// leafy ground beneath the forests.
        /// </summary>
        private void PaintForestFloorSplat(Vector3[] playerSpawnPositions)
        {
            if (_terrain == null || _data == null || forestFloor == null) return;

            var layers = _data.terrainLayers;
            int forestIdx = IndexOf(layers, forestFloor);
            if (forestIdx < 0) return;

            int res = _data.alphamapResolution;
            int layerCount = layers.Length;
            float terrainPosX = _terrain.transform.position.x;
            float terrainPosZ = _terrain.transform.position.z;
            float sizeX = _data.size.x;
            float sizeZ = _data.size.z;

            const float innerClear = 30f;
            const float outerFade = 70f;

            float[,,] splat = _data.GetAlphamaps(0, 0, res, res);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    // World-space position of this alphamap pixel
                    float wx = (x / (float)res) * sizeX + terrainPosX;
                    float wz = (y / (float)res) * sizeZ + terrainPosZ;

                    // Skip beach + water (matches tree placement filter)
                    float h = TerrainUtility.GetHeight(wx, wz);
                    if (h < waterHeight + treeMinHeightAboveWater) continue;

                    // Same 3-octave FBM noise as tree placement
                    float n = Mathf.PerlinNoise(wx * treeNoiseScale + 13.7f, wz * treeNoiseScale + 91.3f) * 0.5f
                            + Mathf.PerlinNoise(wx * treeNoiseScale * 2.1f + 217f, wz * treeNoiseScale * 2.1f + 301f) * 0.3f
                            + Mathf.PerlinNoise(wx * treeNoiseScale * 4.7f + 41f, wz * treeNoiseScale * 4.7f + 173f) * 0.2f;

                    // Player spawn exclusion (matches tree placement)
                    float threshold = treeDensityThreshold;
                    if (playerSpawnPositions != null)
                    {
                        for (int i = 0; i < playerSpawnPositions.Length; i++)
                        {
                            var spawn = playerSpawnPositions[i];
                            float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(spawn.x, spawn.z));
                            if (d < innerClear) { threshold = 10f; break; }
                            if (d < innerClear + outerFade)
                            {
                                float t = (d - innerClear) / outerFade;
                                threshold = Mathf.Lerp(10f, treeDensityThreshold, t * t);
                            }
                        }
                    }

                    if (n < threshold) continue;

                    // Smooth weight: 0 at threshold, 1 well above
                    float weight = Mathf.Clamp01((n - threshold) / 0.15f);
                    weight = weight * weight * (3f - 2f * weight); // smoothstep

                    // Apply forest-floor weight, scale down other layers proportionally
                    float existing = splat[y, x, forestIdx];
                    float newVal = Mathf.Max(existing, weight);
                    if (newVal - existing < 0.001f) continue;

                    float otherTotal = 0f;
                    for (int l = 0; l < layerCount; l++)
                        if (l != forestIdx) otherTotal += splat[y, x, l];

                    if (otherTotal > 0.001f)
                    {
                        float scale = (1f - newVal) / otherTotal;
                        for (int l = 0; l < layerCount; l++)
                            if (l != forestIdx) splat[y, x, l] *= scale;
                    }
                    splat[y, x, forestIdx] = newVal;
                }
            }

            _data.SetAlphamaps(0, 0, splat);
        }

        // Cache loaded prefabs so we don't reload every instantiation
        private GameObject[] _treePrefabCache;

        private GameObject[] LoadTreePrefabs()
        {
            if (_treePrefabCache != null && _treePrefabCache.Length > 0)
                return _treePrefabCache;

            var list = new List<GameObject>();
            string[] paths = {
                "Assets/Happy Little Trees - Free nature pack by Nebula/Prefabs/Trees/Spruce/Spruce_008.prefab",
                "Assets/Happy Little Trees - Free nature pack by Nebula/Prefabs/Trees/Spruce/SpruceClutter_009.prefab",
            };

            #if UNITY_EDITOR
            foreach (var p in paths)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (prefab != null) list.Add(prefab);
                else Debug.LogWarning($"[ProceduralTerrain] Could not load tree prefab at: {p}");
            }
            #endif

            // Resources fallback (needs prefabs in Assets/Resources/Trees/)
            if (list.Count == 0)
            {
                var r1 = Resources.Load<GameObject>("Trees/Spruce_008");
                if (r1 != null) list.Add(r1);
            }

            _treePrefabCache = list.ToArray();
            Debug.Log($"[ProceduralTerrain] Loaded {_treePrefabCache.Length} tree prefabs");
            return _treePrefabCache;
        }

        private GameObject LoadTreePrefab()
        {
            var prefabs = LoadTreePrefabs();
            return prefabs.Length > 0 ? prefabs[0] : null;
        }

        // Lazy-init: MaterialPropertyBlock cannot be constructed in a static
        // field initializer because the type initializer runs during AddComponent,
        // which Unity forbids. Initialized on first use in ApplyTreeTint.
        private static MaterialPropertyBlock _treeMpb;
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private void ApplyTreeTint(GameObject tree, float variation)
        {
            if (_treeMpb == null) _treeMpb = new MaterialPropertyBlock();

            // Palette: dark green -> olive -> brown
            Color[] palette = {
                new Color(0.15f, 0.30f, 0.12f), // dark green
                new Color(0.22f, 0.35f, 0.15f), // mid green
                new Color(0.30f, 0.32f, 0.14f), // olive
                new Color(0.35f, 0.25f, 0.12f), // brown-green
                new Color(0.20f, 0.28f, 0.10f), // forest green
            };
            Color tint = palette[Mathf.FloorToInt(variation * palette.Length) % palette.Length];

            foreach (var r in tree.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(_treeMpb);
                _treeMpb.SetColor(_baseColorId, tint);
                r.SetPropertyBlock(_treeMpb);
            }
        }

        public void ClearTerrainTrees()
        {
            if (_treeRoot != null) Destroy(_treeRoot);
            _treeRoot = null;
        }
    }
}
