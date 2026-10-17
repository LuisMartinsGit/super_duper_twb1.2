// MapBuilderFiendstonePass.cs
// EDITOR-ONLY: build "Fiendstone Pass" — the 1024 m, 6-player cliff-shelf map.
//   Waning Border > Maps > Build Fiendstone Pass (1024m, 6 players)
//
// THE DESIGN (docs/Design/Maps/Fiendstone_Pass.md, 2026-10-16): a river
// across the middle with three fords; on each bank three low territories,
// two plateaus joined by a mountain pass, and three starts on cliff shelves
// above them. Every step between levels is a CLIFF (a 4.5 m drop, steeper
// than the 45-degree walk limit) except at the ramps — 24 m chokes where
// archers on the shelf above shoot down on whoever climbs. Fourteen small
// rich pockets, each one ramp in: eight neutral, one beside each start.
// The south bank is the north bank turned 180 degrees, exactly.
//
// THE LAYOUT IS DATA. tools/maps/fiendstone_pass/layout.py authors it and
// writes heights.r16, the masks and layout.json beside itself; this builder
// only turns those files into a scene, so re-shaping the map is an edit to
// the Python and a rebuild, never a hand-sculpt.
//
//   * terrain heights: heights.r16 (1025 x 1025, south row first)
//   * NoWalk paint: nowalk.png — mountains and forest. Cliffs need no paint;
//     their slope blocks them (PassabilityGrid.MaxWalkableSlope).
//   * the river: a WaterPlane; nothing below it walks. The fords are raised
//     just above it — walkable, and unclaimable because no territory outline
//     covers the river (RegionMap: a fully authored map's uncovered ground is
//     no region).
//   * forest: trees planted solid inside forest.png, inside or across
//     territories — NEVER a territory of its own (Regions.md § Region kinds,
//     2026-10-16).
//   * territories: one RegionSeedMarker each, with its authored outline,
//     kind and resource type (Territory_Claims.md §11). Mountains and the
//     river are covered by no outline, which is what makes them unclaimable.
//
// Re-runnable: overwrites the scene and its TerrainData in place.

using System.Collections.Generic;
using System.IO;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderFiendstonePass
    {
        private const string MapName = "Fiendstone Pass";
        private const string SceneName = "FiendstonePass";
        private const string Folder = "Assets/GameData/Scenes/Maps/Fiendstone Pass";
        private const string LayoutFolder = "tools/maps/fiendstone_pass";
        private const string TerrainMatPath = "Assets/Resources/TWBTerrain.mat";

        private const int AlphaRes = 1024;   // 1 m per texel, as Veilmarch
        private const int DetailRes = 512;

        // ── layout.json ─────────────────────────────────────────────────────
        [System.Serializable] private class RegionData
        {
            public string code, name, kind, resources;
            public int level;
            public float seedX, seedZ;
            public float[] shape;   // x0, z0, x1, z1, ...
        }
        [System.Serializable] private class StartData
        {
            public string faction, region;
            public float x, z;
        }
        [System.Serializable] private class LayoutData
        {
            public string mapName;
            public int size, heightRes;
            public float maxHeight, waterLevel, waterMinZ, waterMaxZ;
            public float[] levelHeights;
            public RegionData[] regions;
            public StartData[] starts;
        }

        private static LayoutData _layout;
        private static float[] _heights;            // [z * res + x], south row first, world metres
        private static int _res;
        private static bool[] _forest, _fringe, _noWalk, _dirt;
        private static int _maskW, _maskH;

        // ── entry points ────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Build Fiendstone Pass (1024m, 6 players)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(MapName,
                    $"Generate {MapName}?\n\n" +
                    "  1024 x 1024 m, 6 players, cliff shelves over a river\n" +
                    $"  Layout from {LayoutFolder}/ (run layout.py first)\n\n" +
                    $"Overwrites {SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            BuildInternal();
        }

        /// <summary>Batch / bridge entry: build, then bake the lobby assets.</summary>
        [MenuItem("Waning Border/Maps/Build + Bake Fiendstone Pass")]
        public static void BuildAndBake()
        {
            if (!BuildInternal()) return;
            MapGenKit.BakeLobbyAssets(MapName);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        private static bool BuildInternal()
        {
            if (!LoadLayout()) return false;

            MapAssetFolders.Ensure(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // No gameplay scene may ship a camera (the rig builds its own).
            foreach (var stray in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            var terrain = MapGenKit.BuildTerrain(new MapGenKit.TerrainSpec
            {
                MapFolder = Folder,
                SceneName = SceneName,
                Size = _layout.size,
                HeightmapRes = _res,
                AlphamapRes = AlphaRes,
                DetailRes = DetailRes,
                MaxHeight = _layout.maxHeight,
                Height = HeightAt,
            });
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
            if (mat != null) terrain.materialTemplate = mat;
            else Debug.LogWarning($"[{MapName}] {TerrainMatPath} not found — the culture / blood / curse overlays will not render.");

            MapGenKit.BuildLighting();

            MapGenKit.PaintGround(terrain.terrainData, new MapGenKit.PaintSpec
            {
                MapFolder = Folder,
                Size = _layout.size,
                NoWalk = (x, z) => Mask(_noWalk, x, z) ? 1f : 0f,
                Dirt = (x, z) => Mask(_dirt, x, z) ? 0.85f : 0f,
                // The top shelf sits at 23 m and must read as ground, not rock:
                // rock is the cliff faces (steepness) and the mountains above it.
                RockSteepFrom = 30f, RockSteepTo = 45f,
                RockHeightFrom = 26f, RockHeightTo = 30f,
            });

            MapGenKit.ScatterFlora(terrain, new MapGenKit.FloraSpec
            {
                MapFolder = Folder,
                Size = _layout.size,
                Seed = 0xF1E2,
                // Attempts, not instances: acceptance is the forest mask (planted
                // nearly solid) plus a thin scatter of rocks along the mountain
                // feet. ~4% of the map is forest, so this lands ~5-6k props.
                TreeCount = 160000,
                TreeScale = 0.5f,
                GrassScale = 2.0f,
                DetailDensity = 2,
                CanPlant = CanPlant,
                // Forest grows on every level up to the top shelf (23 m); only
                // the mountains get rocks.
                RockAboveHeight = 27f,
                RockAboveSteep = 50f,
            });

            var water = new GameObject("Water");
            water.AddComponent<TheWaningBorder.World.Terrain.WaterPlane>().Initialize(
                new Vector2(-_layout.size * 0.5f, _layout.waterMinZ),
                new Vector2(_layout.size * 0.5f, _layout.waterMaxZ),
                _layout.waterLevel);

            int bad = PlaceMarkers();

            EditorSceneManager.MarkSceneDirty(scene);
            string scenePath = $"{Folder}/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool registered = MapGenKit.RegisterInBuildSettings(scenePath);
            if (!MapRegistry.ShouldShip(scenePath))
                Debug.LogError($"[{MapName}] NOT IN THE SHIP GATE — add \"{SceneName}\" to MapRegistry.ShippingMapScenes.");
            if (bad > 0) Debug.LogError($"[{MapName}] {bad} validation problem(s) — see Console.");
            Debug.Log($"[{MapName}] built: {_layout.regions.Length} territories, {_layout.starts.Length} starts.");
            MapGenKit.ReportLobbyReadiness(MapName, MapName, Folder, scenePath, registered);
            return true;
        }

        // ── layout files ────────────────────────────────────────────────────

        private static bool LoadLayout()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), LayoutFolder);
            string json = Path.Combine(dir, "layout.json");
            string r16 = Path.Combine(dir, "heights.r16");
            if (!File.Exists(json) || !File.Exists(r16))
            {
                Debug.LogError($"[{MapName}] layout files missing in {dir} — run layout.py first.");
                return false;
            }
            _layout = JsonUtility.FromJson<LayoutData>(File.ReadAllText(json));
            _res = _layout.heightRes;

            var bytes = File.ReadAllBytes(r16);
            if (bytes.Length != _res * _res * 2)
            {
                Debug.LogError($"[{MapName}] heights.r16 is {bytes.Length} bytes, expected {_res * _res * 2}.");
                return false;
            }
            _heights = new float[_res * _res];
            for (int i = 0; i < _heights.Length; i++)
                _heights[i] = (bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 65535f * _layout.maxHeight;

            _noWalk = LoadMask(dir, "nowalk.png");
            _forest = LoadMask(dir, "forest.png");
            _dirt = LoadMask(dir, "dirt.png");
            _fringe = LoadMask(dir, "fringe.png");
            return _noWalk != null && _forest != null && _dirt != null && _fringe != null;
        }

        /// <summary>A mask PNG, north at the top as layout.py writes it. Texture
        /// rows count from the BOTTOM, so pixel y is already the world z index.</summary>
        private static bool[] LoadMask(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) { Debug.LogError($"[{MapName}] missing {path}"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            _maskW = tex.width; _maskH = tex.height;
            var px = tex.GetPixels32();
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].r > 127;
            Object.DestroyImmediate(tex);
            return m;
        }

        private static float HeightAt(float wx, float wz)
        {
            float half = _layout.size * 0.5f;
            int xi = Mathf.Clamp(Mathf.RoundToInt(wx + half), 0, _res - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt(wz + half), 0, _res - 1);
            return _heights[zi * _res + xi];
        }

        private static bool Mask(bool[] m, float wx, float wz)
        {
            float half = _layout.size * 0.5f;
            int xi = Mathf.Clamp(Mathf.RoundToInt(wx + half), 0, _maskW - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt(wz + half), 0, _maskH - 1);
            return m[zi * _maskW + xi];
        }

        private static bool CanPlant(float wx, float wz)
        {
            if (Mask(_forest, wx, wz)) return true;
            if (!Mask(_fringe, wx, wz)) return false;
            // a thin scatter of rocks along the mountain feet
            uint hsh = (uint)(Mathf.FloorToInt(wx) * 73856093) ^ (uint)(Mathf.FloorToInt(wz) * 19349663);
            return (hsh % 100) < 18;
        }

        // ── markers ─────────────────────────────────────────────────────────

        private static int PlaceMarkers()
        {
            int bad = 0;
            var startsRoot = new GameObject("PlayerStarts").transform;
            for (int i = 0; i < _layout.starts.Length; i++)
            {
                var s = _layout.starts[i];
                if (!System.Enum.TryParse(s.faction, out Faction f))
                {
                    Debug.LogError($"[{MapName}] unknown faction '{s.faction}'"); bad++; continue;
                }
                var go = NewMarker($"P{i + 1} Start ({s.faction}) - {s.region}", s.x, s.z, startsRoot);
                go.AddComponent<PlayerStartMarker>().Faction = f;
            }

            var regionRoot = new GameObject("Regions").transform;
            for (int i = 0; i < _layout.regions.Length; i++)
            {
                var r = _layout.regions[i];
                var go = NewMarker($"Region {i:00} - {r.name}", r.seedX, r.seedZ, regionRoot);
                var m = go.AddComponent<RegionSeedMarker>();
                m.RegionName = r.name;
                if (!System.Enum.TryParse(r.kind, out m.Kind)) { Debug.LogError($"[{MapName}] bad kind '{r.kind}' on {r.name}"); bad++; }
                if (!System.Enum.TryParse(r.resources, out m.Resources)) { Debug.LogError($"[{MapName}] bad resources '{r.resources}' on {r.name}"); bad++; }
                var shape = new Vector2[r.shape.Length / 2];
                for (int k = 0; k < shape.Length; k++) shape[k] = new Vector2(r.shape[2 * k], r.shape[2 * k + 1]);
                m.Shape = shape;
                if (shape.Length < 3) { Debug.LogError($"[{MapName}] {r.name} has no outline"); bad++; }

                // the seed must stand on its own level, not in a wall
                float want = r.level >= 0 ? LevelHeight(r.level) : 0f;
                float got = HeightAt(r.seedX, r.seedZ);
                if (Mathf.Abs(got - want) > 1.5f)
                {
                    Debug.LogError($"[{MapName}] {r.name}: seed at {got:0.0} m, level {r.level} is {want:0.0} m");
                    bad++;
                }
            }
            return bad;
        }

        private static float LevelHeight(int level) => _layout.levelHeights[level];

        private static GameObject NewMarker(string name, float x, float z, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, HeightAt(x, z), z);
            return go;
        }
    }
}
