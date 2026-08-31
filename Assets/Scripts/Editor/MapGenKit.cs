// MapGenKit.cs
// EDITOR-ONLY: the map-agnostic half of a procedural map generator.
//
// SunderedCrownGenerator proved the shape of a generated map — sculpt a
// heightmap from one function, derive the NoWalk paint from that SAME
// function, scatter Synty flora, drop markers, register in Build Settings,
// bake the lobby assets. Everything in that list except the height function
// and the marker layout is identical for every map, so it lives here and
// the per-map generators carry only their design.
//
// (SunderedCrownGenerator deliberately keeps its own copies: it authors the
// one map the build currently ships, and re-pointing it at shared code
// would silently re-cut a shipped terrain. New maps use this kit.)
//
// THE THREE RULES A GENERATED MAP MUST RESPECT
//   1. NoWalk is the ONLY deliberate wall. PassabilityGrid scans the
//      terrain layer palette for an asset whose name contains "nowalk"
//      (case-insensitive) and blocks any cell painted >= 0.5 REGARDLESS of
//      slope. Asset data is identical on every client, so it is
//      lockstep-safe. Never paint it where players must walk.
//   2. The paint is derived from the height function, not hand-authored,
//      so the wall and the mountain that draws it can never drift apart.
//   3. Registration is not cosmetic. The lobby list comes from Build
//      Settings via MapRegistry, so a map that fails to register is a map
//      that does not exist — register BEFORE the thumbnail bakes, and let
//      the bakes fail non-fatally.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef with no separate editor assembly — the Editor/ folder name alone
// does not exclude it from player builds.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapGenKit
    {
        /// <summary>A world-space scalar field sampled at (x, z).</summary>
        public delegate float Field(float wx, float wz);

        /// <summary>Folder holding the shared Substance ground textures every
        /// generated map paints with.</summary>
        public const string SharedRoot = "Assets/GameData/Scenes/Maps/Shared";

        private const string SyntyEnv =
            "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments";

        // ════════════════════════════════════════════════════════════════
        // TERRAIN
        // ════════════════════════════════════════════════════════════════

        public sealed class TerrainSpec
        {
            /// <summary>Asset folder the TerrainData is written into.</summary>
            public string MapFolder;
            /// <summary>Scene file name — names the TerrainData asset.</summary>
            public string SceneName;
            /// <summary>Playable square, in metres. The terrain is centred on
            /// the world origin so map centre == (0, 0), which every marker
            /// calculation, the AI's hall-anchored placement rings and
            /// TerrainUtility.GetPlayableBounds all assume.</summary>
            public int Size = 256;
            /// <summary>MUST be 2^n + 1 — Unity silently rounds to the nearest
            /// valid value, and a rounded resolution no longer matches the
            /// array SetHeights is handed.</summary>
            public int HeightmapRes = 257;
            public int AlphamapRes = 256;
            public int DetailRes = 128;
            /// <summary>World height for a normalized heightmap value of 1.</summary>
            public float MaxHeight = 120f;
            /// <summary>Ground elevation in world metres at (x, z).</summary>
            public Field Height;
        }

        public static Terrain BuildTerrain(TerrainSpec spec)
        {
            int n = spec.HeightmapRes - 1;
            if (n < 32 || (n & (n - 1)) != 0)
                throw new System.ArgumentException(
                    $"heightmapResolution {spec.HeightmapRes} is not 2^n+1. Unity rounds it, " +
                    "and SetHeights then throws (or silently writes a mismatched heightmap).");

            var data = new TerrainData
            {
                heightmapResolution = spec.HeightmapRes,
                alphamapResolution = spec.AlphamapRes,
                baseMapResolution = 1024,
            };
            data.SetDetailResolution(spec.DetailRes, 16);
            data.size = new Vector3(spec.Size, spec.MaxHeight, spec.Size);

            // THE WIND. These four are the entire sway mechanism for terrain
            // detail — a WindZone does NOT drive grass (it only affects trees
            // with wind-aware shaders), so generated maps deliberately have no
            // WindZone. Values sit above Unity's 0.5 defaults because motion
            // has to be exaggerated to read at all from an RTS camera 15-80
            // units up: what looks like a gale from 2 m is a ripple from 60 m.
            data.wavingGrassStrength = 0.70f;
            data.wavingGrassAmount = 0.60f;
            data.wavingGrassSpeed = 0.55f;
            data.wavingGrassTint = new Color(0.86f, 0.90f, 0.80f, 1f);

            data.SetHeights(0, 0, SculptHeights(spec));

            string dataPath = $"{spec.MapFolder}/{spec.SceneName} TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-spec.Size / 2f, 0f, -spec.Size / 2f);
            go.isStatic = true;

            var terrain = go.GetComponent<Terrain>();
            // 3 -> 8 (2026-08-31 perf pass): pixel error is the terrain
            // mesh LOD. At 3 a 1024 m map tessellates near-fully to the
            // horizon; 8 halves the triangle load with no visible change at
            // RTS camera height.
            terrain.heightmapPixelError = 8f;
            // Distances trimmed (2026-08-31 perf pass): the far half of a
            // big map must not cost full-detail rendering — "empty parts
            // should not weigh".
            terrain.basemapDistance = 220f;
            terrain.treeDistance = 260f;
            terrain.treeBillboardDistance = 120f;
            // Detail draw distance is measured from the CAMERA, which zooms to
            // 80 units (CameraController.maxZoom) looking down at an angle — so
            // ground across the screen sits well past 140 m. 250 is the ceiling.
            terrain.detailObjectDistance = 110f;
            terrain.detailObjectDensity = 0.80f;
            terrain.drawInstanced = true;
            return terrain;
        }

        private static float[,] SculptHeights(TerrainSpec spec)
        {
            int res = spec.HeightmapRes;
            var h = new float[res, res];
            float half = spec.Size / 2f;
            float step = spec.Size / (float)(res - 1);

            for (int zi = 0; zi < res; zi++)
            {
                float wz = -half + zi * step;
                for (int xi = 0; xi < res; xi++)
                {
                    float wx = -half + xi * step;
                    h[zi, xi] = Mathf.Clamp01(spec.Height(wx, wz) / spec.MaxHeight);
                }
            }
            return h;
        }

        // ════════════════════════════════════════════════════════════════
        // GROUND PAINT
        // ════════════════════════════════════════════════════════════════

        public sealed class PaintSpec
        {
            public string MapFolder;
            public int Size;

            /// <summary>How strongly this point is WALLED, 0..1. Derive it from
            /// the same function that sculpted the mountains — the surviving
            /// weight after normalization is exactly this value, and
            /// PassabilityGrid blocks at >= 0.5.</summary>
            public Field NoWalk;

            /// <summary>Bare-ground mask, 0..1 — objectives, roads, riverbed.
            /// Painted before meadow/grass so it reads at a glance from the
            /// RTS camera.</summary>
            public Field Dirt;

            /// <summary>Steepness band (degrees) over which rock takes over.</summary>
            public float RockSteepFrom = 18f;
            public float RockSteepTo = 34f;

            /// <summary>Elevation band (world metres) over which rock takes over,
            /// so a massif reads as one mass with its scree feet.</summary>
            public float RockHeightFrom = 15f;
            public float RockHeightTo = 26f;
        }

        /// <summary>Layer slots in the palette every generated map uses.</summary>
        public struct Palette
        {
            public int Grass, Meadow, Rock, Dirt, NoWalk;
            public int Count;
        }

        public static Palette PaintGround(TerrainData data, PaintSpec spec)
        {
            var layers = new List<TerrainLayer>();
            var grass = MakeLayer(spec.MapFolder, "GrassSubstance001_COMPILED", "Grass", 24f);
            var meadow = MakeLayer(spec.MapFolder, "GrassSubstance002_COMPILED", "Meadow", 30f);
            var rock = MakeLayer(spec.MapFolder, "RockSubstance003_COMPILED", "Rock", 34f);
            var dirt = MakeLayer(spec.MapFolder, "GroundSubstance002_COMPILED", "Dirt", 20f);
            // THE BLOCKING LAYER. The asset NAME is the contract —
            // PassabilityGrid.LoadNoWalkMask looks for "nowalk" and nothing
            // else, so renaming this asset silently unblocks every wall on the
            // map. It wears the same rock texture at a coarser tile, so wall
            // and scree read as one massif in game while staying tellable
            // apart in the terrain palette.
            var noWalk = MakeLayer(spec.MapFolder, "RockSubstance003_COMPILED", "NoWalk", 26f);

            foreach (var l in new[] { grass, meadow, rock, dirt, noWalk })
                if (l != null) layers.Add(l);

            var p = new Palette { Count = layers.Count };
            if (layers.Count == 0)
            {
                Debug.LogWarning($"[MapGenKit] No terrain layers could be built from {SharedRoot} — " +
                                 "the terrain will render untextured. Check that the Shared " +
                                 "substance folders still contain *_basecolor.tga files.");
                return p;
            }
            data.terrainLayers = layers.ToArray();

            p.Grass = 0;
            p.Meadow = layers.Count > 1 ? 1 : 0;
            p.Rock = layers.Count > 2 ? 2 : 0;
            p.Dirt = layers.Count > 3 ? 3 : p.Grass;
            p.NoWalk = layers.Count > 4 ? 4 : -1;

            if (p.NoWalk < 0)
                Debug.LogError("[MapGenKit] The NoWalk layer could not be built, so nothing on this " +
                               "map WILL BLOCK — every wall is open ground and the layout's premise " +
                               $"is gone. Check that {SharedRoot}/RockSubstance003_COMPILED_graph_0 " +
                               "still has its basecolor texture.");

            int res = data.alphamapResolution;
            var map = new float[res, res, layers.Count];
            float half = spec.Size / 2f;

            for (int z = 0; z < res; z++)
            {
                float wz = -half + (z / (float)(res - 1)) * spec.Size;
                for (int x = 0; x < res; x++)
                {
                    float wx = -half + (x / (float)(res - 1)) * spec.Size;

                    float nx = (x + 0.5f) / res;
                    float nz = (z + 0.5f) / res;
                    float steep = data.GetSteepness(nx, nz);           // degrees
                    float height = data.GetInterpolatedHeight(nx, nz); // world metres

                    var w = new float[layers.Count];

                    // NoWalk is computed FIRST and every other layer is scaled
                    // into what remains, so after normalization the NoWalk
                    // weight is EXACTLY this value. That matters: dilution
                    // would drop cells under the 0.5 block threshold and the
                    // wall would block in patches — the worst possible failure,
                    // because it looks fine and plays broken.
                    float noWalkW = p.NoWalk >= 0 && spec.NoWalk != null
                        ? Mathf.Clamp01(spec.NoWalk(wx, wz))
                        : 0f;
                    float rest = 1f - noWalkW;

                    float rockW = Mathf.InverseLerp(spec.RockSteepFrom, spec.RockSteepTo, steep);
                    rockW = Mathf.Max(rockW,
                        Mathf.InverseLerp(spec.RockHeightFrom, spec.RockHeightTo, height));

                    float dirtW = (1f - rockW) *
                                  (spec.Dirt != null ? Mathf.Clamp01(spec.Dirt(wx, wz)) : 0f);

                    float meadowW = (1f - rockW) * (1f - dirtW) * Mathf.Clamp01(
                        Mathf.PerlinNoise(wx * 0.010f + 13.7f, wz * 0.010f + 4.2f) * 1.6f - 0.45f);

                    float grassW = Mathf.Max(0f, 1f - rockW - dirtW - meadowW);

                    float other = rockW + dirtW + meadowW + grassW;
                    float k = other > 0.0001f ? rest / other : 0f;

                    if (p.NoWalk >= 0) w[p.NoWalk] += noWalkW;
                    w[p.Rock] += rockW * k;
                    w[p.Dirt] += dirtW * k;
                    w[p.Meadow] += meadowW * k;
                    w[p.Grass] += grassW * k;

                    float sum = 0f;
                    for (int i = 0; i < w.Length; i++) sum += w[i];
                    if (sum <= 0.0001f) { w[p.Grass] = 1f; sum = 1f; }
                    for (int i = 0; i < w.Length; i++) map[z, x, i] = w[i] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
            return p;
        }

        /// <summary>
        /// Build (or rebuild) a TerrainLayer asset from one of the Shared
        /// substance folders. Returns null when the source textures are
        /// missing rather than throwing, so a partial Shared folder degrades
        /// to fewer layers instead of failing the whole generate.
        /// </summary>
        public static TerrainLayer MakeLayer(string mapFolder, string substance,
                                             string niceName, float tile)
        {
            string dir = $"{SharedRoot}/{substance}_graph_0";
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{substance}_basecolor.tga");
            if (diffuse == null) return null;
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{substance}_normal.tga");

            string path = $"{mapFolder}/{niceName}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = diffuse;
            layer.normalMapTexture = normal;
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = Vector2.zero;

            ApplyMatteSurface(layer);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>
        /// KILL THE SPARKLE. Symptom: ground "covered in diamonds". With no
        /// mask map assigned the terrain shader takes smoothness from the
        /// DIFFUSE TEXTURE'S ALPHA, not from the m_Smoothness scalar — and
        /// these Substance basecolor TGAs carry no meaningful alpha (it
        /// samples as 1.0), so smoothness lands between "wet" and "chrome" and
        /// every normal-map wrinkle catches the sun as a pinpoint highlight.
        /// Remapping the diffuse ALPHA range to zero forces alpha-derived
        /// smoothness to 0 whatever the texture holds, via public API rather
        /// than poking the private m_SmoothnessSource field.
        /// </summary>
        public static void ApplyMatteSurface(TerrainLayer layer)
        {
            var min = layer.diffuseRemapMin;
            var max = layer.diffuseRemapMax;

            // A layer that was never configured can carry an all-zero max
            // remap; writing that back renders the ground black. Treat a
            // degenerate RGB remap as identity and otherwise leave the
            // author's colour grading alone — we are only here for alpha.
            if (max.x <= 0f && max.y <= 0f && max.z <= 0f)
                max = new Vector4(1f, 1f, 1f, max.w);

            layer.diffuseRemapMin = new Vector4(min.x, min.y, min.z, 0f);
            layer.diffuseRemapMax = new Vector4(max.x, max.y, max.z, 0f); // alpha -> 0

            layer.smoothness = 0f;
            layer.metallic = 0f;
            layer.specular = Color.black;

            // Half-strength normals: even at smoothness 0 a 2K normal tiled
            // every ~24 m puts many texels inside one screen pixel at RTS
            // altitude, which shimmers as the camera moves.
            layer.normalScale = 0.5f;
        }

        // ════════════════════════════════════════════════════════════════
        // FLORA
        // ════════════════════════════════════════════════════════════════

        public sealed class FloraSpec
        {
            public string MapFolder;
            public int Size;
            public int Seed = 0x5CC0;

            /// <summary>Total tree/rock instances attempted. Scale with AREA,
            /// not with the linear size, or a smaller map ends up denser.</summary>
            public int TreeCount = 650;

            /// <summary>Overall tree size multiplier.</summary>
            public float TreeScale = 0.5f;
            /// <summary>Height as a fraction of width. Synty trees are modelled
            /// tall for close-up use and read as stretched from an RTS camera;
            /// 0.5 gives a squat canopy that still covers ground.</summary>
            public float TreeHeightRatio = 0.5f;
            public float GrassScale = 2.0f;
            public int DetailDensity = 4;
            public float GrassCutoff = 0.50f;

            /// <summary>False → keep this ground clear (build plateaus,
            /// objectives, crossings). A base ringed with trees or a well you
            /// cannot see over is a worse map, however good the screenshot.</summary>
            public System.Func<float, float, bool> CanPlant;

            /// <summary>Ground counted as "high" for the rock-vs-tree choice.</summary>
            public float RockAboveHeight = 18f;
            public float RockAboveSteep = 18f;
        }

        public static void ScatterFlora(Terrain terrain, FloraSpec spec)
        {
            var data = terrain.terrainData;

            var treePrefabs = FindPrefabs(new[]
            {
                "SM_Env_Tree_", "SM_Env_Big_Tree_01", "SM_Env_Pine_", "SM_Env_Dead_Tree_",
            }, 14);
            // Bushes and boulders ride in the TREE layer, not the detail layer:
            // they are prop-scale meshes, and the detail layer is reserved for
            // terrain grass (which must stay textured quads to catch the wind).
            // Terrain trees also give distance culling and billboarding free.
            var rockPrefabs = FindPrefabs(new[]
            {
                "SM_Env_Rock_", "SM_Env_Boulder_", "SM_Env_Bush_",
            }, 12);

            var protoPrefabs = new List<GameObject>();
            protoPrefabs.AddRange(treePrefabs);
            protoPrefabs.AddRange(rockPrefabs);
            if (protoPrefabs.Count == 0)
            {
                Debug.LogWarning($"[MapGenKit] No Synty environment prefabs found under {SyntyEnv} — " +
                                 "the map generates without trees or rocks.");
            }
            else
            {
                // Unity validates tree/detail prototypes on ASSIGNMENT and
                // throws on anything it dislikes. Trees and grass are attempted
                // independently — losing the grass should not also lose the
                // forest.
                try
                {
                    data.treePrototypes = BuildTreePrototypes(protoPrefabs);
                    data.SetTreeInstances(
                        BuildTrees(data, spec, treePrefabs.Count, protoPrefabs,
                                   out var occupiedCells), true);

                    // Trees and rocks follow the same rule as every other
                    // ground-occupying thing: one build cell each, impassable.
                    // They are terrain tree INSTANCES, not entities, so the
                    // block is expressed through the NoWalk layer that
                    // PassabilityGrid already reads. docs/Design/Build_Grid.md
                    PaintNoWalkAtCells(data, spec, occupiedCells);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[MapGenKit] Tree pass failed, continuing without trees. " +
                                     $"{e.GetType().Name}: {e.Message}");
                }
            }

            try { PaintDetails(data, spec); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MapGenKit] Grass pass failed, continuing without ground cover. " +
                                 $"{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Edge of one build cell, in metres. Mirrors
        /// <c>BuildGrid.CellSize</c>; MapGenKit is editor-side map authoring and
        /// deliberately does not take a runtime dependency for one constant.
        /// </summary>
        private const float GridCellSize = 2f;

        /// <summary>
        /// Horizontal extent of a prefab's renderers at scale 1, used to fit a
        /// prop to its cell. Returns 0 when nothing measurable is found, in
        /// which case the caller leaves the prop's scale alone.
        /// </summary>
        private static float MeasurePrefabWidth(GameObject prefab)
        {
            if (prefab == null) return 0f;

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return 0f;

            bool any = false;
            Bounds b = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                var mf = renderers[i].GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                // sharedMesh.bounds is in the child's local space; fold in the
                // child's transform relative to the prefab root so an offset
                // child does not read as a huge prop.
                var local = mf.sharedMesh.bounds;
                var m = renderers[i].transform.localToWorldMatrix;
                var centre = m.MultiplyPoint3x4(local.center);
                var ext = m.MultiplyVector(local.extents);
                var world = new Bounds(centre,
                    new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z)) * 2f);

                if (!any) { b = world; any = true; }
                else b.Encapsulate(world);
            }
            if (!any) return 0f;

            return Mathf.Max(b.size.x, b.size.z);
        }

        /// <summary>
        /// Force the NoWalk layer to full weight on every cell a prop occupies,
        /// so trees and rocks block movement. PassabilityGrid reads this layer
        /// (weight >= 0.5) as terrain-blocked; nothing else about props reaches
        /// the sim, since they are terrain instances rather than entities.
        /// </summary>
        private static void PaintNoWalkAtCells(TerrainData data, FloraSpec spec,
                                               HashSet<(int, int)> cells)
        {
            if (cells == null || cells.Count == 0) return;

            int noWalkIdx = FindNoWalkLayer(data);
            if (noWalkIdx < 0)
            {
                Debug.LogWarning("[MapGenKit] No 'NoWalk' terrain layer — trees and rocks " +
                                 "will render but will NOT block movement.");
                return;
            }

            int aw = data.alphamapWidth, ah = data.alphamapHeight, layers = data.alphamapLayers;
            var maps = data.GetAlphamaps(0, 0, aw, ah);
            float half = spec.Size / 2f;

            foreach (var (cx, cz) in cells)
            {
                // Cell world rect -> normalized -> alphamap texel range.
                float x0 = cx * GridCellSize, x1 = x0 + GridCellSize;
                float z0 = cz * GridCellSize, z1 = z0 + GridCellSize;

                int ax0 = Mathf.Clamp(Mathf.FloorToInt((x0 + half) / spec.Size * aw), 0, aw - 1);
                int ax1 = Mathf.Clamp(Mathf.CeilToInt((x1 + half) / spec.Size * aw) - 1, 0, aw - 1);
                int az0 = Mathf.Clamp(Mathf.FloorToInt((z0 + half) / spec.Size * ah), 0, ah - 1);
                int az1 = Mathf.Clamp(Mathf.CeilToInt((z1 + half) / spec.Size * ah) - 1, 0, ah - 1);

                for (int az = az0; az <= az1; az++)
                    for (int ax = ax0; ax <= ax1; ax++)
                    {
                        // Alphamaps are [z, x, layer] and must sum to 1.
                        for (int l = 0; l < layers; l++)
                            maps[az, ax, l] = (l == noWalkIdx) ? 1f : 0f;
                    }
            }

            data.SetAlphamaps(0, 0, maps);
            Debug.Log($"[MapGenKit] NoWalk painted under {cells.Count} prop cells " +
                      $"(layer {noWalkIdx}).");
        }

        /// <summary>Index of the NoWalk terrain layer, matching the
        /// case-insensitive name scan PassabilityGrid.LoadNoWalkMask uses.
        /// </summary>
        private static int FindNoWalkLayer(TerrainData data)
        {
            var layers = data.terrainLayers;
            if (layers == null) return -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null || layers[i].name == null) continue;
                if (layers[i].name.ToLowerInvariant().Contains("nowalk")) return i;
            }
            return -1;
        }

        private static TreePrototype[] BuildTreePrototypes(List<GameObject> prefabs)
        {
            var protos = new TreePrototype[prefabs.Count];
            for (int i = 0; i < prefabs.Count; i++)
                protos[i] = new TreePrototype { prefab = prefabs[i], bendFactor = 0f };
            return protos;
        }

        /// <summary>
        /// Scatter trees and rocks onto the build grid: each instance is
        /// quantised to one 2 m cell centre, at most one instance per cell, and
        /// scaled so its canopy fills that cell. <paramref name="occupiedCells"/>
        /// comes back so the caller can paint those cells impassable.
        /// docs/Design/Build_Grid.md
        /// </summary>
        private static TreeInstance[] BuildTrees(TerrainData data, FloraSpec spec,
                                                 int treeProtoCount, List<GameObject> protoPrefabs,
                                                 out HashSet<(int, int)> occupiedCells)
        {
            int totalProtos = protoPrefabs.Count;
            var rng = new System.Random(spec.Seed);
            var list = new List<TreeInstance>(spec.TreeCount);
            occupiedCells = new HashSet<(int, int)>();
            float half = spec.Size / 2f;

            // Per-prototype world width at scale 1, so each prop can be scaled
            // to its cell instead of to an arbitrary shared multiplier.
            var protoWidth = new float[totalProtos];
            for (int i = 0; i < totalProtos; i++)
                protoWidth[i] = MeasurePrefabWidth(protoPrefabs[i]);

            for (int i = 0; i < spec.TreeCount; i++)
            {
                float rawX = (float)(rng.NextDouble() * spec.Size - half);
                float rawZ = (float)(rng.NextDouble() * spec.Size - half);

                // Quantise to the containing build cell's centre.
                int cellX = Mathf.FloorToInt(rawX / GridCellSize);
                int cellZ = Mathf.FloorToInt(rawZ / GridCellSize);
                if (!occupiedCells.Add((cellX, cellZ))) continue;   // one per cell

                float wx = cellX * GridCellSize + GridCellSize * 0.5f;
                float wz = cellZ * GridCellSize + GridCellSize * 0.5f;

                // Re-test plantability at the SNAPPED point — the snap can move
                // an instance up to a metre, potentially onto a plateau or an
                // objective the spec means to keep clear.
                if (wx < -half || wx > half || wz < -half || wz > half) continue;
                if (spec.CanPlant != null && !spec.CanPlant(wx, wz)) continue;

                Sample(data, spec.Size, wx, wz, out float steep, out float height);

                // Rocks go on the high steep ground, trees on the low flats, so
                // walls read as scree and plains as woodland.
                bool wantRock = steep > spec.RockAboveSteep || height > spec.RockAboveHeight;
                int proto;
                if (wantRock && totalProtos > treeProtoCount)
                    proto = treeProtoCount + rng.Next(totalProtos - treeProtoCount);
                else if (treeProtoCount > 0)
                    proto = rng.Next(treeProtoCount);
                else
                    proto = rng.Next(totalProtos);

                // Scale to the cell: the prop's canopy spans one build cell,
                // with a little jitter so a forest does not read as a grid of
                // clones. TreeScale stays a global authoring knob on top.
                float cellFit = protoWidth[proto] > 0.01f
                    ? GridCellSize / protoWidth[proto]
                    : 1f;
                float jitter = 0.85f + (float)rng.NextDouble() * 0.25f;
                float scale = cellFit * jitter * spec.TreeScale;
                list.Add(new TreeInstance
                {
                    position = new Vector3((wx + half) / spec.Size, 0f, (wz + half) / spec.Size),
                    prototypeIndex = proto,
                    // Two separate knobs: TreeScale sets overall size,
                    // TreeHeightRatio keeps the squat silhouette. Changing one
                    // never disturbs the other.
                    widthScale = scale,
                    heightScale = scale * spec.TreeHeightRatio,
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            return list.ToArray();
        }

        /// <summary>
        /// Unity terrain grass: alpha-cut quads rendered by the waving-grass
        /// shader and animated by the TerrainData wavingGrass* parameters. No
        /// mesh prototypes and no WindZone involved — this is the terrain
        /// system's own grass, the only ground cover that actually moves.
        /// </summary>
        private static void PaintDetails(TerrainData data, FloraSpec spec)
        {
            var grassTex = BuildGrassTexture(spec.MapFolder, spec.Seed);
            if (grassTex == null)
            {
                Debug.LogWarning("[MapGenKit] Grass texture could not be written — skipping cover.");
                return;
            }

            // Two prototypes off one texture: a short common tuft and a
            // slightly taller, yellower, rarer one. Cheaper than two textures
            // and enough variation that the field does not read as a stamp.
            var protos = new[]
            {
                new DetailPrototype
                {
                    usePrototypeMesh = false,          // NOT a mesh — terrain grass
                    prototypeTexture = grassTex,
                    // DetailRenderMode.Grass builds crossed quads and runs the
                    // waving shader. GrassBillboard would also wave but always
                    // faces the camera, which spins visibly when an RTS view
                    // rotates.
                    renderMode = DetailRenderMode.Grass,
                    useInstancing = false,             // invalid outside VertexLit
                    minWidth = 0.55f * spec.GrassScale, maxWidth = 0.95f * spec.GrassScale,
                    minHeight = 0.40f * spec.GrassScale, maxHeight = 0.62f * spec.GrassScale,
                    noiseSpread = 14f,
                    healthyColor = new Color(0.72f, 0.84f, 0.52f, 1f),
                    dryColor = new Color(0.80f, 0.80f, 0.46f, 1f),
                },
                new DetailPrototype
                {
                    usePrototypeMesh = false,
                    prototypeTexture = grassTex,
                    renderMode = DetailRenderMode.Grass,
                    useInstancing = false,
                    minWidth = 0.45f * spec.GrassScale, maxWidth = 0.75f * spec.GrassScale,
                    minHeight = 0.48f * spec.GrassScale, maxHeight = 0.78f * spec.GrassScale,
                    noiseSpread = 22f,
                    healthyColor = new Color(0.66f, 0.78f, 0.44f, 1f),
                    dryColor = new Color(0.86f, 0.82f, 0.50f, 1f),
                },
            };
            data.detailPrototypes = protos;

            int res = data.detailResolution;
            float half = spec.Size / 2f;

            for (int p = 0; p < protos.Length; p++)
            {
                var layer = new int[res, res];
                float freq = 0.02f + p * 0.006f;
                for (int z = 0; z < res; z++)
                {
                    float wz = -half + (z / (float)(res - 1)) * spec.Size;
                    for (int x = 0; x < res; x++)
                    {
                        float wx = -half + (x / (float)(res - 1)) * spec.Size;
                        if (spec.CanPlant != null && !spec.CanPlant(wx, wz)) continue;

                        Sample(data, spec.Size, wx, wz, out float steep, out _);
                        if (steep > 20f) continue;              // no grass on the crags

                        // Sparse by intent. The cutoff leaves roughly the top
                        // third of the noise field as grass, so the plain is
                        // patchy meadow rather than a lawn — and sparse is also
                        // what pays for the wind, since waving grass cannot be
                        // GPU-instanced.
                        float nse = Mathf.PerlinNoise(wx * freq + p * 31.4f, wz * freq + p * 17.1f);
                        if (nse < spec.GrassCutoff) continue;
                        layer[z, x] = Mathf.RoundToInt(
                            spec.DetailDensity * (nse - spec.GrassCutoff) / (1f - spec.GrassCutoff));
                    }
                }
                data.SetDetailLayer(0, 0, p, layer);
            }
        }

        private static void Sample(TerrainData data, int size, float wx, float wz,
                                   out float steepness, out float height)
        {
            float half = size / 2f;
            float nx = Mathf.Clamp01((wx + half) / size);
            float nz = Mathf.Clamp01((wz + half) / size);
            steepness = data.GetSteepness(nx, nz);
            height = data.GetInterpolatedHeight(nx, nz);
        }

        /// <summary>
        /// Draw the grass blade sheet the terrain detail system needs.
        ///
        /// Generated rather than sourced because the project has no grass
        /// billboard art: the Shared "GrassSubstance" maps are seamless GROUND
        /// textures with no alpha, and Synty's grass is mesh prefabs. Unity's
        /// grass renderer needs an alpha-cut blade sheet, so one is written
        /// into the map folder.
        ///
        /// Blades are drawn bottom-anchored and rooted in a narrow band at the
        /// bottom centre, fanning out toward their tips — a TUFT, not a
        /// scatter. Each rendered quad is the WHOLE texture, so blades rooted
        /// at random x across the full width produce a few thin lines smeared
        /// over half a metre, which is invisible at RTS camera height.
        /// </summary>
        public static Texture2D BuildGrassTexture(string mapFolder, int seed)
        {
            const int W = 256, H = 256;
            const int Blades = 18;

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.4f, 0.55f, 0.25f, 0f);

            var rng = new System.Random(seed ^ 0x6A55);
            for (int b = 0; b < Blades; b++)
            {
                float baseX = W * 0.5f + (float)(rng.NextDouble() - 0.5) * 46f;
                float tipDx = (float)(rng.NextDouble() - 0.5) * 190f;   // fan out to fill
                float height = H * (0.62f + (float)rng.NextDouble() * 0.36f);
                float halfW = 5.0f + (float)rng.NextDouble() * 4.0f;
                float hue = 0.30f + (float)rng.NextDouble() * 0.16f;

                int steps = Mathf.CeilToInt(height);
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;                 // 0 root -> 1 tip
                    float y = t * height;
                    float x = baseX + tipDx * t * t;            // quadratic lean = curve
                    float w = halfW * (1f - t * 0.92f);         // taper to a point
                    if (w < 0.5f) w = 0.5f;

                    float shade = 0.55f + 0.45f * t;
                    var col = new Color(hue * 0.72f * shade, (0.42f + hue * 0.55f) * shade,
                                        0.20f * shade, 1f);

                    int y0 = Mathf.RoundToInt(y);
                    if (y0 < 0 || y0 >= H) continue;
                    int xs = Mathf.FloorToInt(x - w), xe = Mathf.CeilToInt(x + w);
                    for (int xi = xs; xi <= xe; xi++)
                    {
                        if (xi < 0 || xi >= W) continue;
                        float d = Mathf.Abs(xi - x);
                        float a = Mathf.Clamp01((w - d) + 0.5f);
                        if (a <= 0f) continue;
                        int idx = y0 * W + xi;
                        if (a > px[idx].a) px[idx] = new Color(col.r, col.g, col.b, a);
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply();

            string path = $"{mapFolder}/GrassBlades.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static List<GameObject> FindPrefabs(string[] namePrefixes, int max)
        {
            var found = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(SyntyEnv)) return found;

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { SyntyEnv });
            foreach (var guid in guids)
            {
                if (found.Count >= max) break;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                foreach (var prefix in namePrefixes)
                {
                    if (!file.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) found.Add(go);
                    break;
                }
            }
            return found;
        }

        // ════════════════════════════════════════════════════════════════
        // MARKERS
        // ════════════════════════════════════════════════════════════════

        /// <summary>Create a marker and drop it onto the terrain surface.</summary>
        public static T Marker<T>(GameObject parent, string name, Vector3 pos) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            pos.y = SampleHeight(pos.x, pos.z);
            go.transform.position = pos;
            return go.AddComponent<T>();
        }

        public static float SampleHeight(float x, float z)
        {
            var t = Terrain.activeTerrain;
            if (t == null) return 0f;
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        // ════════════════════════════════════════════════════════════════
        // LIGHTING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// GameBootstrap adds a DayNightCycle at runtime when none exists and
        /// will drive this light, so these values are the editor-preview look
        /// rather than the final in-match lighting.
        /// </summary>
        public static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.957f, 0.882f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(46f, 138f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.53f, 0.60f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.22f, 0.20f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0016f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.72f);
        }

        // ════════════════════════════════════════════════════════════════
        // REGISTRATION + LOBBY BAKES
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Put the scene in Build Settings, enabled. This — and only this —
        /// is what makes a map appear in the skirmish lobby: MapRegistry
        /// builds its list from the Build Settings scenes under MapsRoot.
        ///
        /// MapSceneSync normally does it via EditorApplication.delayCall on
        /// import, but delayCall has not fired yet while we are still inside
        /// the generate, so we do it inline and let MapSceneSync no-op later.
        ///
        /// NOTE: the ship gate wins on the next domain reload — a scene not in
        /// MapRegistry.ShippingMapScenes is dropped again by MapSceneSync.
        /// <see cref="ReportLobbyReadiness"/> says so out loud.
        /// </summary>
        public static bool RegisterInBuildSettings(string scenePath)
        {
            scenePath = scenePath.Replace('\\', '/');
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (!string.Equals(scenes[i].path.Replace('\\', '/'), scenePath,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (scenes[i].enabled) return true;
                scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return true;
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        /// <summary>
        /// Bake the lobby thumbnail, marker overlay and stand-alone slot PNG.
        /// Both bakes are non-fatal for the same reason: a map with no
        /// thumbnail is playable, a map that failed to register is not.
        /// </summary>
        public static void BakeLobbyAssets(string logTag)
        {
            try { MapInfoBaker.Bake(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[{logTag}] MapInfo bake failed — the map is still playable and " +
                                 $"in the lobby, but without a thumbnail or player-count. " +
                                 $"{e.GetType().Name}: {e.Message}");
            }
            try { MapLobbyImageBaker.Bake(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[{logTag}] Lobby slot image failed. " +
                                 $"{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Report exactly what a player will see, in plain terms. The lobby
        /// reads MapRegistry, which reads Build Settings, which MapSceneSync
        /// populates from disk under the ship gate — four hops where a silent
        /// miss just means the map never appears.
        /// </summary>
        public static void ReportLobbyReadiness(string logTag, string mapName, string mapFolder,
                                                string scenePath, bool inBuildSettings)
        {
            var info = AssetDatabase.LoadAssetAtPath<MapInfo>($"{mapFolder}/{mapName} MapInfo.asset");
            string thumb = info != null && info.Thumbnail != null ? "yes" : "NO";
            string lobbyPng = File.Exists($"{mapFolder}/{mapName} Lobby.png") ? "yes" : "NO";
            bool gated = !MapRegistry.ShouldShip(scenePath);

            string warn = gated
                ? "\n  WARNING: the SHIP GATE excludes this scene — MapSceneSync will drop it from " +
                  "Build Settings on the next domain reload and the map will vanish from the lobby. " +
                  "Add its scene name to MapRegistry.ShippingMapScenes (or set ShipAllMaps = true)."
                : "";

            Debug.Log($"[{logTag}] LOBBY READY: \"{mapName}\"\n" +
                      $"  scene             {scenePath}\n" +
                      $"  in Build Settings {inBuildSettings}\n" +
                      $"  passes ship gate  {!gated}\n" +
                      $"  MapInfo asset     {(info != null ? "yes" : "NO")}\n" +
                      $"  player count      {(info != null ? info.PlayerCount.ToString() : "?")}\n" +
                      $"  thumbnail         {thumb}\n" +
                      $"  lobby slot image  {lobbyPng}\n" +
                      "The skirmish dropdown reads Build Settings at runtime, so the entry appears " +
                      "the next time the menu scene loads." + warn);
        }

        // ════════════════════════════════════════════════════════════════
        // SHAPING HELPERS
        // ════════════════════════════════════════════════════════════════

        public static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Distance from (wx, wz) to (px, pz).</summary>
        public static float Dist(float wx, float wz, float px, float pz)
        {
            float dx = wx - px, dz = wz - pz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// A flat-topped dome: full <paramref name="height"/> inside
        /// <paramref name="flatRadius"/>, smoothstepping to 0 at
        /// <paramref name="radius"/>. Returns 0..height.
        ///
        /// Peak gradient is 1.5 * height / (radius - flatRadius) — keep that
        /// under PassabilityGrid.MaxWalkableSlope (1.0) for anything players
        /// must walk on, and well over it for anything meant to read as cliff.
        /// </summary>
        public static float Dome(float d, float flatRadius, float radius, float height)
        {
            if (d >= radius) return 0f;
            if (d <= flatRadius) return height;
            return height * SmoothStep(Mathf.InverseLerp(radius, flatRadius, d));
        }
    }
}
#endif
