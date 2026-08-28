// MapInfoBaker.cs
// EDITOR-ONLY tool: bake a map's MapInfo asset from the open map scene.
//   Waning Border > Maps > Bake Map Info From Open Scene
// Fills SceneName, PlayerCount (from PlayerStartMarkers), the normalized
// marker positions (player starts / iron / veilstone / veilsteel / curse
// nodes), captures a top-down orthographic thumbnail PNG into the map
// folder, and registers the asset in the Resources MapInfoIndex.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef (TheWaningBorder.Runtime) with no separate editor assembly; the
// Editor/ folder name alone does not exclude it from player builds.
//
// Idempotent: re-baking overwrites positions/count/thumbnail in place and
// preserves hand-written DisplayName / SizeTag / Description.

using System.IO;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapInfoBaker
    {
        private const string IndexPath = "Assets/UI/Resources/MapInfoIndex.asset";
        private const int ThumbnailSize = 512;

        [MenuItem("Waning Border/Maps/Bake Map Info From Open Scene")]
        public static void Bake()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || !scene.path.StartsWith(MapRegistry.MapsRoot))
            {
                EditorUtility.DisplayDialog("Map Info Baker",
                    $"The open scene is not a map scene.\n\nOpen a scene under\n{MapRegistry.MapsRoot} first.",
                    "OK");
                return;
            }

            string folder = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            string mapName = Path.GetFileName(folder);

            // Find (or create) the MapInfo asset in the map folder.
            MapInfo info = null;
            foreach (string guid in AssetDatabase.FindAssets("t:MapInfo", new[] { folder }))
            {
                info = AssetDatabase.LoadAssetAtPath<MapInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info != null) break;
            }
            if (info == null)
            {
                info = ScriptableObject.CreateInstance<MapInfo>();
                AssetDatabase.CreateAsset(info, $"{folder}/{mapName} MapInfo.asset");
            }

            info.SceneName = scene.name;
            if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = mapName;

            GetMapBounds(out Vector3 min, out Vector3 size);

            var starts = Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None);
            // Bake in the SAME canonical order the runtime registry uses.
            // PlayerSlot.StartIndex indexes into MapInfo.PlayerStarts in the
            // lobby and into MapMarkerRegistry.PlayerStarts at spawn time; if
            // these two orders disagreed, choosing a start position on the
            // lobby minimap would drop you at a different marker.
            // docs/Design/Lobby_Setup.md
            System.Array.Sort(starts, MapMarkerRegistry.ComparePlayerStarts);
            if (starts.Length > 0)
            {
                info.PlayerCount = Mathf.Clamp(starts.Length, 2, 8);
            }
            else
            {
                // Do NOT leave PlayerCount at whatever it was. A fresh MapInfo
                // defaults to 8 (MapInfo.cs), so baking a map before its start
                // markers are placed used to ship "8 players" on a map with
                // zero starts -- the lobby then offered 8 slots, and every
                // faction fell through to procedural placement.
                Debug.LogError($"[MapInfoBaker] \"{mapName}\" has NO PlayerStartMarkers. " +
                               $"PlayerCount is left at {info.PlayerCount} and is almost " +
                               "certainly wrong. Place start markers and re-bake.");
            }
            info.PlayerStarts = Normalize(starts, min, size);

            // Parallel faction array so a chosen start resolves to a marker by
            // identity, not by array position.
            info.PlayerStartFactions = new Faction[starts.Length];
            for (int i = 0; i < starts.Length; i++)
                info.PlayerStartFactions[i] = starts[i] != null ? starts[i].Faction : default;
            info.IronDeposits = Normalize(
                Object.FindObjectsByType<IronPatchMarker>(FindObjectsSortMode.None), min, size);
            info.VeilstoneNodes = Normalize(
                Object.FindObjectsByType<VeilstoneOutcroppingMarker>(FindObjectsSortMode.None), min, size);
            info.VeilsteelNodes = Normalize(
                Object.FindObjectsByType<VeilsteelDepositMarker>(FindObjectsSortMode.None), min, size);
            info.CurseNodes = Normalize(
                Object.FindObjectsByType<BorderNodeMarker>(FindObjectsSortMode.None), min, size);
            info.SupplyNodes = Normalize(
                Object.FindObjectsByType<SupplyNodeMarker>(FindObjectsSortMode.None), min, size);

            // Region seeds, in the same canonical order the runtime registry
            // uses -- the index is the region id.
            var regions = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            System.Array.Sort(regions, (a, b) =>
            {
                int n = string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
                if (n != 0) return n;
                var pa = a.transform.position; var pb = b.transform.position;
                int x = pa.x.CompareTo(pb.x);
                return x != 0 ? x : pa.z.CompareTo(pb.z);
            });
            info.RegionSeeds = Normalize(regions, min, size);
            info.RegionNames = new string[regions.Length];
            for (int i = 0; i < regions.Length; i++)
                info.RegionNames[i] = regions[i] != null ? regions[i].RegionName : "";
            if (regions.Length == 0)
                Debug.LogWarning($"[MapInfoBaker] \"{mapName}\" has NO RegionSeedMarkers. Under " +
                                 "docs/Design/Regions.md a map with no regions grants no build " +
                                 "space at all. Run Waning Border > Maps > Seed Regions For Open Scene.");

            info.Thumbnail = CaptureThumbnail(folder, mapName, min, size, regions);

            EditorUtility.SetDirty(info);
            RegisterInIndex(info);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MapInfoBaker] Baked \"{mapName}\": {info.PlayerCount} players, " +
                      $"{info.PlayerStarts.Length} starts, {info.IronDeposits.Length} iron, " +
                      $"{info.VeilstoneNodes.Length} veilstone, {info.VeilsteelNodes.Length} veilsteel, " +
                      $"{info.SupplyNodes.Length} supply, " +
                      $"{info.CurseNodes.Length} curse nodes, thumbnail " +
                      (info.Thumbnail != null ? "captured." : "FAILED."), info);
        }

        // Terrain bounds when a terrain exists, otherwise the padded bounds
        // of all placed markers.
        private static void GetMapBounds(out Vector3 min, out Vector3 size)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                min = terrain.GetPosition();
                size = terrain.terrainData.size;
                return;
            }

            var markers = Object.FindObjectsByType<MapMarker>(FindObjectsSortMode.None);
            if (markers.Length == 0)
            {
                min = new Vector3(-256f, 0f, -256f);
                size = new Vector3(512f, 100f, 512f);
                return;
            }

            var b = new Bounds(markers[0].transform.position, Vector3.zero);
            foreach (var m in markers) b.Encapsulate(m.transform.position);
            b.Expand(80f);
            min = b.min;
            size = b.size;
            if (size.y < 50f) size.y = 50f;
        }

        private static Vector2[] Normalize(MapMarker[] markers, Vector3 min, Vector3 size)
        {
            var result = new Vector2[markers.Length];
            for (int i = 0; i < markers.Length; i++)
            {
                Vector3 p = markers[i].WorldPosition;
                result[i] = new Vector2(
                    Mathf.Clamp01((p.x - min.x) / Mathf.Max(1f, size.x)),
                    Mathf.Clamp01((p.z - min.z) / Mathf.Max(1f, size.z)));
            }
            return result;
        }

        // Top-down orthographic capture of the whole map, saved as a PNG in
        // the map folder and imported as the thumbnail texture.
        /// <summary>
        /// Burn the region partition into the thumbnail.
        ///
        /// Baked into the PNG rather than overlaid at runtime because the
        /// thumbnail is used in several places (lobby preview, map list) and the
        /// partition is static -- drawing it once here means every consumer gets
        /// it free and none needs to know what a region is.
        ///
        /// Goes through RegionMap rather than computing nearest-seed inline.
        /// That is not tidiness: RegionMap domain-warps the query so boundaries
        /// wander instead of ruling straight, and a second copy of the maths
        /// here would draw a DIFFERENT border from the one the terrain and
        /// minimap show. One partition, three views, one implementation.
        /// </summary>
        private static void DrawRegionLattice(Texture2D tex, Vector3 min, Vector3 size,
                                              RegionSeedMarker[] regions)
        {
            if (regions == null || regions.Length < 2) return;

            // The baker runs with no match world, so install the partition here.
            var seeds = new Vector2[regions.Length];
            var names = new string[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                var p = regions[i].transform.position;
                seeds[i] = new Vector2(p.x, p.z);
                names[i] = regions[i].RegionName;
            }
            TheWaningBorder.World.Regions.RegionMap.Configure(seeds, names);

            // ~1.5 px wide, in metres so it does not thin out on a large map.
            float width = Mathf.Max(1f, size.x / ThumbnailSize * 1.5f);
            var line = new Color(0.92f, 0.88f, 0.72f);   // parchment on dark terrain
            var px = tex.GetPixels();

            for (int y = 0; y < ThumbnailSize; y++)
            {
                float wz = min.z + (y + 0.5f) / ThumbnailSize * size.z;
                int row = y * ThumbnailSize;
                for (int x = 0; x < ThumbnailSize; x++)
                {
                    float wx = min.x + (x + 0.5f) / ThumbnailSize * size.x;
                    float e = TheWaningBorder.World.Regions.RegionMap.EdgeStrengthAt(wx, wz, width);
                    if (e <= 0.15f) continue;
                    int i2 = row + x;
                    px[i2] = Color.Lerp(px[i2], line, e * 0.75f);
                }
            }
            tex.SetPixels(px);
        }

        private static Texture2D CaptureThumbnail(string folder, string mapName, Vector3 min, Vector3 size,
                                                  RegionSeedMarker[] regions)
        {
            var go = new GameObject("~MapInfoBakerCamera");
            RenderTexture rt = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.aspect = 1f;
                cam.orthographicSize = Mathf.Max(size.x, size.z) * 0.5f;
                cam.transform.position = new Vector3(
                    min.x + size.x * 0.5f,
                    min.y + size.y + 100f,
                    min.z + size.z * 0.5f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.nearClipPlane = 1f;
                cam.farClipPlane = size.y + 400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.031f, 0.047f, 0.059f);

                rt = new RenderTexture(ThumbnailSize, ThumbnailSize, 24);

                // URP does not support Camera.Render(); go through the SRP
                // render-request path, falling back for built-in.
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(ThumbnailSize, ThumbnailSize, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ThumbnailSize, ThumbnailSize), 0, 0);
                RenderTexture.active = prev;

                DrawRegionLattice(tex, min, size, regions);
                tex.Apply();

                string pngPath = $"{folder}/{mapName} Thumbnail.png";
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(pngPath);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            }
            finally
            {
                if (rt != null)
                {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                Object.DestroyImmediate(go);
            }
        }

        private static void RegisterInIndex(MapInfo info)
        {
            var index = AssetDatabase.LoadAssetAtPath<MapInfoIndex>(IndexPath);
            if (index == null)
            {
                // The index lives in a Resources folder so MapInfoIndex.For
                // can Resources.Load it at runtime — but nothing guarantees
                // that folder exists. On a project where no index has ever
                // been baked, Assets/UI/Resources is simply absent and
                // CreateAsset fails with "Creating asset at path ... failed",
                // taking the whole bake (and any caller) down with it.
                MapAssetFolders.Ensure(System.IO.Path.GetDirectoryName(IndexPath)
                                           .Replace('\\', '/'));
                index = ScriptableObject.CreateInstance<MapInfoIndex>();
                AssetDatabase.CreateAsset(index, IndexPath);
            }
            if (index.Maps == null) index.Maps = new MapInfo[0];

            foreach (var existing in index.Maps)
                if (existing == info) return;

            ArrayUtility.Add(ref index.Maps, info);
            EditorUtility.SetDirty(index);
        }
    }
}
