// MapBuilderMirrorMarches.cs
// EDITOR-ONLY: generate "Mirror Marches" — the 1024 m, 4-player FAIR map.
//   Waning Border > Maps > Build Mirror Marches (1024m, 4 players)
//
// THE DESIGN (2026-10-05): a test bed where the seat cannot decide the match.
//   * 512 x 512 build cells (1024 m), mirrored across BOTH axes. Height,
//     territory shapes and territory types are mirror-symmetric by
//     construction, and TerritoryResources detects the symmetry and copies
//     one quadrant's nodes to the other three, so every seat opens on the
//     same ground.
//   * Territories are authored RECTANGLES (RegionSeedMarker.Shape), not
//     Voronoi cells. Along each edge: 128 | 64 64 64 64 | 128 cells. Each
//     player starts in a 128 x 128-cell corner territory; every other
//     territory is a 64 x 64-cell square — 4 homes + 48 squares.
//   * The curse holds the 4 central squares (Veilstone rich: they start
//     cursed). No territory is Empty. Per quadrant: Start, 2 Normal,
//     4 Normal + veilstone, 2 Normal + iron, 2 Iron rich, 1 Sanctum, and
//     the curse square.
//   * Open ground: no ridges, no forests, no field flora. Trees grow only on
//     the raised rim band, which is impassable anyway — a tree is an
//     impassable cell, and an unmirrored scatter would break the symmetry.
//
// Re-runnable: overwrites the terrain asset and scene in place.

using System.Collections.Generic;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ResourceType = TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderMirrorMarches
    {
        private const string MapName = "Mirror Marches";
        private const string SceneName = "MirrorMarches";
        private const string Folder = "Assets/GameData/Scenes/Maps/Mirror Marches";
        private const string TerrainMatPath = "Assets/Resources/TWBTerrain.mat";

        // ── dimensions ──────────────────────────────────────────────────────
        private const float MapMetres = 1024f;
        private const float Half = MapMetres * 0.5f;
        private const float MaxHeight = 60f;
        private const int HeightRes = 1025;     // 1 m per texel; texel 512 sits on the axis
        private const int AlphaRes = 1024;      // 1 m per texel (NoWalk per 2 m cell)
        private const int DetailRes = 512;

        // Heights against PassabilityGrid/RegionMap thresholds (Water 4 m,
        // Mountain 24 m).
        private const float PlainY = 8f;
        private const float RimY = 40f;
        // The rim is kept thin: the edge squares are only 128 m deep.
        private const float RimMetres = 10f;
        private const float RimRamp = 14f;

        // Territory edges along one axis, in metres from the map's west edge:
        // 256 | 128 x4 | 256 (128 | 64 x4 | 128 build cells).
        private static readonly float[] Bands = { 0f, 256f, 384f, 512f, 640f, 768f, 1024f };

        private static readonly Faction[] StartFactions =
            { Faction.Blue, Faction.Red, Faction.Green, Faction.Yellow };

        // ── entry points ────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Build Mirror Marches (1024m, 4 players)")]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog(MapName,
                    $"Generate {MapName}?\n\n" +
                    "  1024 x 1024 m, 4 players, mirrored on both axes\n" +
                    "  128-cell corner homes, 64-cell squares, curse in the centre 4\n\n" +
                    $"Overwrites {SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            BuildInternal();
        }

        /// <summary>Batch entry: build the map, then bake its lobby assets.</summary>
        public static void BuildAndBake()
        {
            BuildInternal();
            MapGenKit.BakeLobbyAssets(MapName);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        private static void BuildInternal()
        {
            MapAssetFolders.Ensure(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // No gameplay scene ships a camera (the rig builds its own).
            foreach (var stray in Object.FindObjectsByType<Camera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            var terrain = MapGenKit.BuildTerrain(new MapGenKit.TerrainSpec
            {
                MapFolder = Folder,
                SceneName = SceneName,
                Size = (int)MapMetres,
                HeightmapRes = HeightRes,
                AlphamapRes = AlphaRes,
                DetailRes = DetailRes,
                MaxHeight = MaxHeight,
                Height = HeightAt,
            });

            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
            if (mat != null) terrain.materialTemplate = mat;
            else Debug.LogWarning($"[{MapName}] {TerrainMatPath} not found — overlays will not render.");

            MapGenKit.BuildLighting();
            PlaceMarkers();
            AssignLayers(terrain.terrainData);

            MapGenKit.ScatterFlora(terrain, new MapGenKit.FloraSpec
            {
                MapFolder = Folder,
                Size = (int)MapMetres,
                Seed = 0x3A1F,
                TreeCount = 1600,
                TreeScale = 0.5f,
                GrassScale = 2.0f,
                DetailDensity = 2,
                CanPlant = CanPlant,
            });

            int bad = Validate();

            EditorSceneManager.MarkSceneDirty(scene);
            string scenePath = $"{Folder}/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool registered = MapGenKit.RegisterInBuildSettings(scenePath);
            if (!MapRegistry.ShouldShip(scenePath))
                Debug.LogError($"[{MapName}] NOT IN THE SHIP GATE — add \"{SceneName}\" to " +
                               "MapRegistry.ShippingMapScenes or MapSceneSync strips it again.");
            if (bad > 0)
                Debug.LogError($"[{MapName}] {bad} validation problem(s) — see Console.");

            MapGenKit.ReportLobbyReadiness(MapName, MapName, Folder, scenePath, registered);
        }

        // ── terrain ─────────────────────────────────────────────────────────

        /// <summary>Ground height at world (wx, wz). A function of |x| and |z|
        /// only, so the ground is mirrored across both axes exactly.</summary>
        private static float HeightAt(float wx, float wz)
        {
            float ax = Mathf.Abs(wx), az = Mathf.Abs(wz);
            float y = Mathf.Max(PlainY + Noise(ax, az), 7.0f);
            float rim = RimMask(ax, az);
            if (rim > 0f) y = Mathf.Lerp(y, RimY, MapGenKit.SmoothStep(rim));
            return y;
        }

        private static float RimMask(float ax, float az)
        {
            float inner = Half - RimMetres - RimRamp;
            float d = Mathf.Max(ax - inner, az - inner, 0f);
            return Mathf.Clamp01(d / RimRamp);
        }

        /// <summary>Gentle relief, read in mirrored coordinates.</summary>
        private static float Noise(float ax, float az)
        {
            float nx = ax / MapMetres, nz = az / MapMetres;
            float a = Mathf.PerlinNoise(nx * 9f + 11.7f, nz * 9f + 3.1f) - 0.5f;
            float b = Mathf.PerlinNoise(nx * 29f + 5.2f, nz * 29f + 9.4f) - 0.5f;
            return a * 2.4f + b * 0.8f;
        }

        private static TerrainLayer LayerFrom(string sampleName, string niceName, float tile)
        {
            var src = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                $"Assets/MISC/TerrainSampleAssets/TerrainLayers/{sampleName}.terrainlayer");
            if (src == null)
            {
                Debug.LogWarning($"[{MapName}] sample layer {sampleName} missing.");
                return null;
            }
            string path = $"{Folder}/{niceName}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = src.diffuseTexture;
            layer.normalMapTexture = null;
            layer.maskMapTexture = null;
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = Vector2.zero;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>Two grasses on broad noise, dry patches and heather, rock on
        /// the rim — all read in mirrored coordinates, so the ground reads
        /// the same from every seat. NoWalk (the asset NAME is the contract
        /// PassabilityGrid looks for) is painted only by the flora pass.</summary>
        private static void AssignLayers(TerrainData data)
        {
            var grassA = LayerFrom("Grass_A_TerrainLayer", "GrassA", 13f);
            var grassB = LayerFrom("Grass_B_TerrainLayer", "GrassB", 11f);
            var dry = LayerFrom("Grass_Dry_TerrainLayer", "GrassDry", 12f);
            var heath = LayerFrom("Heather_TerrainLayer", "Heather", 9f);
            var rock = LayerFrom("Rock_TerrainLayer", "Rock", 22f);
            var noWalk = LayerFrom("Rock_TerrainLayer", "NoWalk", 27f);

            var list = new List<TerrainLayer>();
            foreach (var l in new[] { grassA, grassB, dry, heath, rock, noWalk })
                if (l != null) list.Add(l);
            if (list.Count == 0) { Debug.LogError($"[{MapName}] no terrain layers built."); return; }
            data.terrainLayers = list.ToArray();

            int iA = list.IndexOf(grassA), iB = list.IndexOf(grassB),
                iDry = list.IndexOf(dry), iHth = list.IndexOf(heath), iRck = list.IndexOf(rock);
            int lc = list.Count;
            var map = new float[AlphaRes, AlphaRes, lc];
            var w = new float[lc];

            for (int z = 0; z < AlphaRes; z++)
            {
                float wz = -Half + (z + 0.5f) * (MapMetres / AlphaRes);
                for (int x = 0; x < AlphaRes; x++)
                {
                    float wx = -Half + (x + 0.5f) * (MapMetres / AlphaRes);
                    float ax = Mathf.Abs(wx), az = Mathf.Abs(wz);
                    float h = HeightAt(wx, wz);
                    for (int l = 0; l < lc; l++) w[l] = 0f;

                    float nA = Mathf.PerlinNoise(ax * 0.011f + 31.7f, az * 0.011f + 11.2f);
                    float blend = nA * nA * (3f - 2f * nA);
                    if (iA >= 0) w[iA] = 1f - blend;
                    if (iB >= 0) w[iB] = blend;
                    float nD = Mathf.PerlinNoise(ax * 0.005f + 77f, az * 0.005f + 41f);
                    if (iDry >= 0 && nD > 0.56f) w[iDry] = Mathf.InverseLerp(0.56f, 0.78f, nD) * 0.9f;
                    float nH = Mathf.PerlinNoise(ax * 0.028f + 5f, az * 0.028f + 91f);
                    if (iHth >= 0 && nH > 0.7f) w[iHth] = Mathf.InverseLerp(0.7f, 0.92f, nH) * 0.55f;
                    if (iRck >= 0 && h > 16f) w[iRck] = Mathf.InverseLerp(16f, 24f, h) * 1.8f;

                    float sum = 0f;
                    for (int l = 0; l < lc; l++) sum += w[l];
                    if (sum <= 0f) { if (iA >= 0) w[iA] = sum = 1f; }
                    for (int l = 0; l < lc; l++) map[z, x, l] = w[l] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        /// <summary>Trees only on the raised rim, which is impassable already.</summary>
        private static bool CanPlant(float wx, float wz)
            => RimMask(Mathf.Abs(wx), Mathf.Abs(wz)) > 0.6f;

        // ── markers ─────────────────────────────────────────────────────────

        /// <summary>
        /// The type of the square at band (i, j) of the 6 x 6 band grid, read
        /// in one quadrant: (qi, qj) are 0 at the map edge and 3 at the centre
        /// (band 0 is the 256 m home band, split into two 128 m squares for
        /// the edge strips; qi/qj count 128 m steps from the edge).
        /// </summary>
        private static ResourceType TypeOf(int qi, int qj)
        {
            // Diagonal-symmetric within the quadrant too: (a, b) == (b, a).
            int a = Mathf.Min(qi, qj), b = Mathf.Max(qi, qj);
            if (a == 3 && b == 3) return ResourceType.VeilstoneRich;   // the curse: centre 4
            if (a == 2 && b == 2) return ResourceType.Sanctum;
            if (a == 2 && b == 3) return ResourceType.NormalVeilstone;
            // Edge strips beside a home (a in 0..1, b in 2..3).
            if (b == 2) return a == 0 ? ResourceType.Normal : ResourceType.NormalVeilstone;
            return a == 0 ? ResourceType.IronRich : ResourceType.NormalIron;   // b == 3, on the mirror axis
        }

        private static void PlaceMarkers()
        {
            var startsRoot = new GameObject("PlayerStarts").transform;
            var regionRoot = new GameObject("Regions").transform;
            int idx = 0;

            // The four homes, 256 m corners. Starts at each corner square's
            // centre; mirrored by construction.
            var corners = new[]
            {
                (new Vector2(-1f, -1f), "Southwest"), (new Vector2(1f, -1f), "Southeast"),
                (new Vector2(1f, 1f), "Northeast"), (new Vector2(-1f, 1f), "Northwest"),
            };
            for (int c = 0; c < 4; c++)
            {
                var (sgn, name) = corners[c];
                var centre = new Vector2(sgn.x * (Half - 128f), sgn.y * (Half - 128f));
                var go = NewMarker($"P{c + 1} Start ({StartFactions[c]}) - {name}", centre, startsRoot);
                go.AddComponent<PlayerStartMarker>().Faction = StartFactions[c];

                float x0 = sgn.x < 0 ? -Half : Half - 256f, z0 = sgn.y < 0 ? -Half : Half - 256f;
                NewSeed(regionRoot, ref idx, centre, $"{name} Home", Rect(x0, z0, 256f, 256f),
                        ResourceType.Start, RegionSeedMarker.RegionKind.PlayerStart);
            }

            // Every 128 m square outside the homes. A 128 m step grid over the
            // whole map (8 x 8); the 2 x 2 blocks in each corner are the homes.
            for (int gz = 0; gz < 8; gz++)
                for (int gx = 0; gx < 8; gx++)
                {
                    bool homeX = gx < 2 || gx > 5, homeZ = gz < 2 || gz > 5;
                    if (homeX && homeZ) continue;
                    int qi = gx < 4 ? gx : 7 - gx;     // 0 at the edge, 3 at the centre
                    int qj = gz < 4 ? gz : 7 - gz;
                    float x0 = -Half + gx * 128f, z0 = -Half + gz * 128f;
                    var centre = new Vector2(x0 + 64f, z0 + 64f);
                    var type = TypeOf(qi, qj);
                    string label = type switch
                    {
                        ResourceType.VeilstoneRich => "Heart",
                        ResourceType.Sanctum => "Sanctum",
                        ResourceType.IronRich => "Ironfield",
                        ResourceType.NormalIron => "Iron March",
                        ResourceType.NormalVeilstone => "Veil March",
                        _ => "March",
                    };
                    NewSeed(regionRoot, ref idx, centre, $"{label} {(char)('A' + gx)}{gz + 1}",
                            Rect(x0, z0, 128f, 128f), type, RegionSeedMarker.RegionKind.Normal);
                }
        }

        /// <summary>A rectangle outline, counter-clockwise from its SW corner.</summary>
        private static Vector2[] Rect(float x0, float z0, float w, float h)
            => new[] { new Vector2(x0, z0), new Vector2(x0 + w, z0),
                       new Vector2(x0 + w, z0 + h), new Vector2(x0, z0 + h) };

        private static void NewSeed(Transform parent, ref int index, Vector2 p, string name,
            Vector2[] shape, ResourceType type, RegionSeedMarker.RegionKind kind)
        {
            var go = NewMarker($"Region {index:00} - {name}", p, parent);
            var seed = go.AddComponent<RegionSeedMarker>();
            seed.RegionName = name;
            seed.Shape = shape;
            seed.Resources = type;
            seed.Kind = kind;
            index++;
        }

        private static GameObject NewMarker(string name, Vector2 p, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, HeightAt(p.x, p.y), p.y);
            return go;
        }

        // ── validation ──────────────────────────────────────────────────────

        /// <summary>The layout rules, checked rather than assumed: 52
        /// territories that tile the map exactly, every type mirrored across
        /// both axes, no Empty, the curse on exactly the centre four, and
        /// every start on standable ground.</summary>
        private static int Validate()
        {
            int bad = 0;
            var seeds = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            if (seeds.Length != 52)
            { Debug.LogError($"[{MapName}] {seeds.Length} territories, want 52."); bad++; }

            float area = 0f;
            var byCentre = new Dictionary<Vector2Int, ResourceType>();
            int curse = 0;
            foreach (var s in seeds)
            {
                var r = s.Shape;
                area += (r[2].x - r[0].x) * (r[2].y - r[0].y);
                var c = new Vector2Int(Mathf.RoundToInt((r[0].x + r[2].x) * 0.5f),
                                       Mathf.RoundToInt((r[0].y + r[2].y) * 0.5f));
                byCentre[c] = s.Resources;
                if (s.Resources == ResourceType.Empty || s.Resources == ResourceType.Auto)
                { Debug.LogError($"[{MapName}] {s.name} is {s.Resources}."); bad++; }
                if (s.Resources == ResourceType.VeilstoneRich)
                {
                    curse++;
                    if (c.x * c.x + c.y * c.y > 2 * 64 * 64 + 1)
                    { Debug.LogError($"[{MapName}] curse territory {s.name} is not central."); bad++; }
                }
            }
            if (Mathf.Abs(area - MapMetres * MapMetres) > 1f)
            { Debug.LogError($"[{MapName}] territories cover {area:0} m², map is {MapMetres * MapMetres:0}."); bad++; }
            if (curse != 4)
            { Debug.LogError($"[{MapName}] {curse} curse territories, want 4."); bad++; }
            foreach (var kv in byCentre)
            {
                var c = kv.Key;
                foreach (var m in new[] { new Vector2Int(-c.x, c.y), new Vector2Int(c.x, -c.y), new Vector2Int(-c.x, -c.y) })
                    if (!byCentre.TryGetValue(m, out var t) || t != kv.Value)
                    { Debug.LogError($"[{MapName}] territory at {c} has no {kv.Value} mirror at {m}."); bad++; }
            }
            foreach (var st in Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None))
            {
                float y = HeightAt(st.transform.position.x, st.transform.position.z);
                if (y <= 4f + 2.85f || y >= 24f - 2.85f)
                { Debug.LogError($"[{MapName}] {st.name} on unstandable ground (y={y:0.0})."); bad++; }
            }
            if (bad == 0) Debug.Log($"[{MapName}] layout validated: 52 territories, mirrored, curse centre 4.");
            return bad;
        }
    }
}
