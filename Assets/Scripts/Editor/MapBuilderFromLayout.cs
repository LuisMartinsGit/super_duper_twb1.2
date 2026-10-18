// MapBuilderFromLayout.cs
// EDITOR-ONLY: turn a layout.py's output into a map scene. Shared by every
// map whose layout is DATA (2026-10-17): Fiendstone Pass, Yiel Lymwérra.
//
// tools/maps/<map>/layout.py authors the map and writes, beside itself:
//   * heights.r16  — terrain heights (heightRes², south row first, 16-bit
//                    over layout.json maxHeight)
//   * mask PNGs    — north at the top; which ones a map has is its own business,
//                    the map's builder names them in its LayoutMapSpec
//   * layout.json  — size, levels, water, territory outlines + kinds +
//                    resource types (+ pinCurse), player starts
// and the map's builder (MapBuilderFiendstonePass, MapBuilderYielLymwerra)
// only says how to paint and plant from those masks. Re-shaping a map is an
// edit to its Python and a rebuild, never a hand-sculpt.
//
// What this builds, for every such map:
//   * the terrain, from heights.r16, with TWBTerrain.mat
//   * ground paint and flora, from the map's PaintSpec / FloraSpec
//   * a WaterPlane over the layout's water band, with its wadeDepth, wearing
//     the map's water material (copied ONCE from the Houidisoft ocean
//     material, then hand-tuned data)
//   * one PlayerStartMarker per start and one RegionSeedMarker per territory,
//     with its authored outline, kind, resource type and curse pin; every
//     seed is checked to stand on its own level
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
    /// <summary>What one layout-driven map needs that the others do not.</summary>
    public sealed class LayoutMapSpec
    {
        public string MapName;          // the map FOLDER's name, which names the MapInfo / lobby assets
        /// <summary>The lobby's name for the map, when it differs from the
        /// folder name (an accent the asset paths leave out). Null keeps
        /// MapInfo's own.</summary>
        public string DisplayName;
        public string SceneName;        // scene file name, no extension
        public string Folder;           // Assets/GameData/Scenes/Maps/<Map>
        public string LayoutFolder;     // tools/maps/<map>, project-relative
        public string[] BoolMasks;      // mask PNGs loaded as on/off
        public string[] GrayMasks;      // mask PNGs loaded as 0..1
        public System.Func<LayoutMap, MapGenKit.PaintSpec> Paint;
        public System.Func<LayoutMap, MapGenKit.FloraSpec> Flora;
        /// <summary>The scene water's material, beside the scene.</summary>
        public string WaterMaterialName;
        /// <summary>Starting values for a FRESH copy of the ocean material;
        /// never re-applied once the asset exists (it is tuned by hand).</summary>
        public System.Action<Material> InitWaterMaterial;
    }

    /// <summary>A loaded layout: heights, masks and layout.json.</summary>
    public sealed class LayoutMap
    {
        [System.Serializable] public class RegionData
        {
            public string code, name, kind, resources;
            public int level;
            public bool pinCurse;
            public float seedX, seedZ;
            public float[] shape;   // x0, z0, x1, z1, ...
        }
        [System.Serializable] public class StartData
        {
            public string faction, region;
            public float x, z;
        }
        [System.Serializable] public class LayoutData
        {
            public string mapName;
            /// <summary>size = east-west metres; sizeZ = north-south, 0 when
            /// the map is square (every map before 2026-10-17).</summary>
            public int size, sizeZ, heightRes;
            public float maxHeight, waterLevel, wadeDepth, waterMinZ, waterMaxZ;
            public float[] levelHeights, rollHeights;
            public RegionData[] regions;
            public StartData[] starts;
        }

        public LayoutData Data;
        private float[] _heights;            // [z * res + x], south row first, world metres
        private int _res;
        private readonly Dictionary<string, bool[]> _bool = new Dictionary<string, bool[]>();
        private readonly Dictionary<string, float[]> _gray = new Dictionary<string, float[]>();
        private int _maskW, _maskH;

        public int Size => Data.size;
        public int SizeZ => Data.sizeZ > 0 ? Data.sizeZ : Data.size;

        /// <summary>The heightmap is square (heightRes per side) whatever the
        /// map's shape, so its samples are size/(res-1) apart along x and
        /// sizeZ/(res-1) along z.</summary>
        public float HeightAt(float wx, float wz)
        {
            int xi = Mathf.Clamp(Mathf.RoundToInt((wx + Size * 0.5f) / Size * (_res - 1)), 0, _res - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt((wz + SizeZ * 0.5f) / SizeZ * (_res - 1)), 0, _res - 1);
            return _heights[zi * _res + xi];
        }

        public bool Mask(string file, float wx, float wz) => _bool[file][Index(wx, wz)];

        public float Gray(string file, float wx, float wz) => _gray[file][Index(wx, wz)];

        /// <summary>Masks are 1 m per pixel: (size+1) x (sizeZ+1).</summary>
        private int Index(float wx, float wz)
        {
            int xi = Mathf.Clamp(Mathf.RoundToInt(wx + Size * 0.5f), 0, _maskW - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt(wz + SizeZ * 0.5f), 0, _maskH - 1);
            return zi * _maskW + xi;
        }

        public static LayoutMap Load(LayoutMapSpec spec)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), spec.LayoutFolder);
            string json = Path.Combine(dir, "layout.json");
            string r16 = Path.Combine(dir, "heights.r16");
            if (!File.Exists(json) || !File.Exists(r16))
            {
                Debug.LogError($"[{spec.MapName}] layout files missing in {dir} — run layout.py first.");
                return null;
            }
            var map = new LayoutMap { Data = JsonUtility.FromJson<LayoutData>(File.ReadAllText(json)) };
            map._res = map.Data.heightRes;

            var bytes = File.ReadAllBytes(r16);
            if (bytes.Length != map._res * map._res * 2)
            {
                Debug.LogError($"[{spec.MapName}] heights.r16 is {bytes.Length} bytes, expected {map._res * map._res * 2}.");
                return null;
            }
            map._heights = new float[map._res * map._res];
            for (int i = 0; i < map._heights.Length; i++)
                map._heights[i] = (bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 65535f * map.Data.maxHeight;

            foreach (var f in spec.BoolMasks)
            {
                var px = map.LoadPixels(spec, dir, f);
                if (px == null) return null;
                var m = new bool[px.Length];
                for (int i = 0; i < px.Length; i++) m[i] = px[i].r > 127;
                map._bool[f] = m;
            }
            foreach (var f in spec.GrayMasks)
            {
                var px = map.LoadPixels(spec, dir, f);
                if (px == null) return null;
                var m = new float[px.Length];
                for (int i = 0; i < px.Length; i++) m[i] = px[i].r / 255f;
                map._gray[f] = m;
            }
            return map;
        }

        /// <summary>A mask PNG, north at the top as layout.py writes it. Texture
        /// rows count from the BOTTOM, so pixel y is already the world z index.</summary>
        private Color32[] LoadPixels(LayoutMapSpec spec, string dir, string file)
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) { Debug.LogError($"[{spec.MapName}] missing {path}"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            _maskW = tex.width; _maskH = tex.height;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }
    }

    public static class MapBuilderFromLayout
    {
        private const string TerrainMatPath = "Assets/Resources/TWBTerrain.mat";
        private const string ToolWaterMatPath =
            "Assets/Tools/Houidisoft technology/One Click Add Water -Stylized Water Shader/Resources/water.mat";

        // Paint and grass resolutions follow the map's longest side: 1 m per
        // paint texel and 2 m per grass texel along it (1024 / 512 on a
        // 1024 m map, as Veilmarch), so a small map is not painted with
        // sixteen times the grass per square metre of a large one.
        private static int AlphaRes(LayoutMap map) => Mathf.NextPowerOfTwo(Mathf.Max(map.Size, map.SizeZ));
        private static int DetailRes(LayoutMap map) => AlphaRes(map) / 2;

        /// <summary>Build the scene. False (and logged) when the layout files
        /// are missing or malformed.</summary>
        public static bool Build(LayoutMapSpec spec)
        {
            var map = LayoutMap.Load(spec);
            if (map == null) return false;
            var layout = map.Data;

            MapAssetFolders.Ensure(spec.Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // No gameplay scene may ship a camera (the rig builds its own).
            foreach (var stray in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            var terrain = MapGenKit.BuildTerrain(new MapGenKit.TerrainSpec
            {
                MapFolder = spec.Folder,
                SceneName = spec.SceneName,
                Size = layout.size,
                SizeZ = map.SizeZ,
                HeightmapRes = layout.heightRes,
                AlphamapRes = AlphaRes(map),
                DetailRes = DetailRes(map),
                MaxHeight = layout.maxHeight,
                Height = map.HeightAt,
            });
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
            if (mat != null) terrain.materialTemplate = mat;
            else Debug.LogWarning($"[{spec.MapName}] {TerrainMatPath} not found — the culture / blood / curse overlays will not render.");

            MapGenKit.BuildLighting();
            MapGenKit.PaintGround(terrain.terrainData, spec.Paint(map));
            MapGenKit.ScatterFlora(terrain, spec.Flora(map));

            var water = new GameObject("Water");
            var plane = water.AddComponent<TheWaningBorder.World.Terrain.WaterPlane>();
            plane.wadeDepth = layout.wadeDepth;
            plane.Initialize(
                new Vector2(-layout.size * 0.5f, layout.waterMinZ),
                new Vector2(layout.size * 0.5f, layout.waterMaxZ),
                layout.waterLevel);
            ApplyWaterMaterial(spec, water);

            int bad = PlaceMarkers(spec, map);

            EditorSceneManager.MarkSceneDirty(scene);
            string scenePath = $"{spec.Folder}/{spec.SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool registered = MapGenKit.RegisterInBuildSettings(scenePath);
            if (!MapRegistry.ShouldShip(scenePath))
                Debug.LogError($"[{spec.MapName}] NOT IN THE SHIP GATE — add \"{spec.SceneName}\" to MapRegistry.ShippingMapScenes.");
            if (bad > 0) Debug.LogError($"[{spec.MapName}] {bad} validation problem(s) — see Console.");
            Debug.Log($"[{spec.MapName}] built: {layout.regions.Length} territories, {layout.starts.Length} starts.");
            MapGenKit.ReportLobbyReadiness(spec.MapName, spec.MapName, spec.Folder, scenePath, registered);
            return true;
        }

        /// <summary>Build, then bake the lobby assets (batch / bridge entry).</summary>
        public static void BuildAndBake(LayoutMapSpec spec)
        {
            if (!Build(spec)) return;
            MapGenKit.BakeLobbyAssets(spec.MapName);
            if (!string.IsNullOrEmpty(spec.DisplayName))
            {
                var info = AssetDatabase.LoadAssetAtPath<MapInfo>($"{spec.Folder}/{spec.MapName} MapInfo.asset");
                if (info == null) Debug.LogError($"[{spec.MapName}] no MapInfo to name \"{spec.DisplayName}\"");
                else { info.DisplayName = spec.DisplayName; EditorUtility.SetDirty(info); }
            }
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Re-assign the water material in the existing scene, without
        /// rebuilding it.</summary>
        public static void ApplyWaterToScene(LayoutMapSpec spec)
        {
            string scenePath = $"{spec.Folder}/{spec.SceneName}.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var plane = Object.FindFirstObjectByType<TheWaningBorder.World.Terrain.WaterPlane>();
            if (plane == null) { Debug.LogError($"[{spec.MapName}] no WaterPlane in " + scenePath); return; }
            ApplyWaterMaterial(spec, plane.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{spec.MapName}] water material applied: {spec.Folder}/{spec.WaterMaterialName}.mat");
        }

        // The WaterPlane stays (pathing and build placement read its level);
        // only its material is swapped for the Houidisoft "One Click Add
        // Water" ocean Shader Graph. The map's copy is DATA: made from the
        // tool's water.mat ONCE, with the map's starting values, then tuned by
        // hand — a rebuild only reassigns it.
        private static void ApplyWaterMaterial(LayoutMapSpec spec, GameObject water)
        {
            string path = $"{spec.Folder}/{spec.WaterMaterialName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (!AssetDatabase.CopyAsset(ToolWaterMatPath, path))
                {
                    Debug.LogError($"[{spec.MapName}] could not copy " + ToolWaterMatPath);
                    return;
                }
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                spec.InitWaterMaterial?.Invoke(mat);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }
            var mr = water.GetComponent<MeshRenderer>();
            if (mr == null) { Debug.LogError($"[{spec.MapName}] the WaterPlane has no renderer"); return; }
            mr.sharedMaterial = mat;
        }

        // ── markers ─────────────────────────────────────────────────────────

        private static int PlaceMarkers(LayoutMapSpec spec, LayoutMap map)
        {
            var layout = map.Data;
            int bad = 0;
            var startsRoot = new GameObject("PlayerStarts").transform;
            for (int i = 0; i < layout.starts.Length; i++)
            {
                var s = layout.starts[i];
                if (!System.Enum.TryParse(s.faction, out Faction f))
                {
                    Debug.LogError($"[{spec.MapName}] unknown faction '{s.faction}'"); bad++; continue;
                }
                var go = NewMarker(map, $"P{i + 1} Start ({s.faction}) - {s.region}", s.x, s.z, startsRoot);
                go.AddComponent<PlayerStartMarker>().Faction = f;
            }

            var regionRoot = new GameObject("Regions").transform;
            for (int i = 0; i < layout.regions.Length; i++)
            {
                var r = layout.regions[i];
                var go = NewMarker(map, $"Region {i:00} - {r.name}", r.seedX, r.seedZ, regionRoot);
                var m = go.AddComponent<RegionSeedMarker>();
                m.RegionName = r.name;
                m.CurseStaysHere = r.pinCurse;
                if (!System.Enum.TryParse(r.kind, out m.Kind)) { Debug.LogError($"[{spec.MapName}] bad kind '{r.kind}' on {r.name}"); bad++; }
                if (!System.Enum.TryParse(r.resources, out m.Resources)) { Debug.LogError($"[{spec.MapName}] bad resources '{r.resources}' on {r.name}"); bad++; }
                if (r.pinCurse && m.Resources != RegionSeedMarker.ResourceType.VeilstoneRich)
                {
                    Debug.LogError($"[{spec.MapName}] {r.name} pins the curse but is not Veilstone rich"); bad++;
                }
                var shape = new Vector2[r.shape.Length / 2];
                for (int k = 0; k < shape.Length; k++) shape[k] = new Vector2(r.shape[2 * k], r.shape[2 * k + 1]);
                m.Shape = shape;
                if (shape.Length < 3) { Debug.LogError($"[{spec.MapName}] {r.name} has no outline"); bad++; }

                // the seed must stand on its own level, not in a wall. The
                // level's rolling ground only ever RISES, by up to its
                // rollHeights entry (layout.json), so that much above is fine.
                float want = r.level >= 0 ? layout.levelHeights[r.level] : 0f;
                float got = map.HeightAt(r.seedX, r.seedZ);
                float roll = layout.rollHeights != null && r.level >= 0 && r.level < layout.rollHeights.Length
                    ? layout.rollHeights[r.level] : 0f;
                if (got < want - 1.5f || got > want + roll + 1.5f)
                {
                    Debug.LogError($"[{spec.MapName}] {r.name}: seed at {got:0.0} m, level {r.level} is {want:0.0} m");
                    bad++;
                }
            }
            return bad;
        }

        private static GameObject NewMarker(LayoutMap map, string name, float x, float z, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, map.HeightAt(x, z), z);
            return go;
        }
    }
}
