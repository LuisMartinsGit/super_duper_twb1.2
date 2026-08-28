// RegionSeeder.cs
// EDITOR-ONLY: give maps their regions.
//   Waning Border > Maps > Seed Regions For Open Scene
//   Waning Border > Maps > Seed Regions For ALL Maps
//
// Every map needs regions now. docs/Design/Regions.md §3 gates construction on
// region ownership for every culture from Age 0, so a map with no
// RegionSeedMarkers has no legal build space at all. Twin Spans, Hollow Table
// and Sundered Crown were authored before regions existed.
//
// Two guarantees, in this order:
//
//   1. ONE REGION PER PLAYER START, never shared. Regions.md §5 makes this a map
//      VALIDITY condition, not a preference: two starts in one region would hand
//      both players the same Age 0 build space and put them inside each other's
//      opening. A seed is placed exactly on each PlayerStartMarker, so the
//      nearest-seed partition cannot put two starts in one region.
//
//   2. The rest of the passable area is covered by FARTHEST-POINT sampling —
//      each new seed goes at the passable point furthest from every seed placed
//      so far. That spreads regions evenly without the clumping random
//      placement gives, and it is deterministic, so re-running produces the
//      same map rather than reshuffling territory under a playtest.
//
// The result is a STARTING POINT to adjust by hand, not a finished map. The
// generator cannot know that a particular ridge should divide two regions —
// and on the three pre-region maps the start areas were never sized for Age 0
// confinement, so they need a playtest pass (Regions.md §6.5).

using System.Collections.Generic;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class RegionSeeder
    {
        // Mirrors PassabilityGrid's thresholds. Duplicated deliberately: that
        // class is runtime and its constants are private, and an editor tool
        // guessing differently would seed regions on ground the game treats as
        // impassable. If those numbers move, move these.
        private const float WaterHeight = 4f;
        private const float MountainHeight = 24f;

        /// <summary>Candidate grid resolution for the farthest-point search.</summary>
        private const int SampleRes = 96;

        // ── open scene ──────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Seed Regions For Open Scene")]
        public static void SeedOpenScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) ||
                !scene.path.Replace('\\', '/').StartsWith(MapRegistry.MapsRoot))
            {
                EditorUtility.DisplayDialog("Region Seeder",
                    $"The open scene is not a map scene.\n\nOpen a scene under\n{MapRegistry.MapsRoot} first.",
                    "OK");
                return;
            }

            int n = Seed(scene, interactive: true);
            if (n < 0) return;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorUtility.DisplayDialog("Region Seeder",
                $"{n} region(s) placed in \"{scene.name}\".\n\n" +
                "Adjust the seeds by hand, then:\n" +
                "Waning Border > Maps > Bake Map Info From Open Scene",
                "OK");
        }

        // ── every map ───────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Seed Regions For ALL Maps")]
        public static void SeedAllMaps()
        {
            var scenePaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { MapRegistry.MapsRoot.TrimEnd('/') }))
                scenePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            scenePaths.Sort(System.StringComparer.OrdinalIgnoreCase);

            if (scenePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Region Seeder", "No map scenes found.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Region Seeder",
                    $"Seed regions in ALL {scenePaths.Count} map scene(s)?\n\n" +
                    string.Join("\n", scenePaths.ConvertAll(System.IO.Path.GetFileNameWithoutExtension)) +
                    "\n\nEach scene is opened, seeded and SAVED. Existing region seeds are replaced.\n\n" +
                    "Seeds are a starting point — adjust them by hand afterwards.",
                    "Seed All", "Cancel"))
                return;

            // Anything unsaved in the current scene would be lost when the loop
            // opens the first map, so ask before touching it.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var report = new List<string>();
            foreach (var path in scenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int n = Seed(scene, interactive: false);
                if (n < 0) { report.Add($"  {scene.name}: SKIPPED (see Console)"); continue; }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.Add($"  {scene.name}: {n} region(s)");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[RegionSeeder] All maps seeded:\n" + string.Join("\n", report));
            EditorUtility.DisplayDialog("Region Seeder",
                "Done:\n\n" + string.Join("\n", report) +
                "\n\nAdjust seeds by hand, then re-bake each map's Map Info.",
                "OK");
        }

        // ── the work ────────────────────────────────────────────────────────

        /// <summary>
        /// Seed one open scene. Returns the region count, or -1 when the scene
        /// cannot be seeded (no terrain, no player starts, or the user cancelled).
        /// </summary>
        private static int Seed(Scene scene, bool interactive)
        {
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null || terrain.terrainData == null)
            {
                Fail(interactive, $"[RegionSeeder] {scene.name}: no Terrain in the scene.");
                return -1;
            }

            var starts = Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None);
            if (starts.Length == 0)
            {
                Fail(interactive, $"[RegionSeeder] {scene.name}: no PlayerStartMarkers. " +
                                  "Every player start must own a region (Regions.md §5), " +
                                  "so the seeder needs them placed first.");
                return -1;
            }

            var existing = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            int total = SuggestRegionCount(terrain, starts.Length);

            if (interactive && !EditorUtility.DisplayDialog("Region Seeder",
                    $"Seed regions for \"{scene.name}\"?\n\n" +
                    $"  Player starts : {starts.Length} (one region each, guaranteed)\n" +
                    $"  Total regions : {total}\n" +
                    $"  Map size      : {terrain.terrainData.size.x:0} x {terrain.terrainData.size.z:0} m\n\n" +
                    (existing.Length > 0
                        ? $"REPLACES the {existing.Length} RegionSeedMarker(s) already in this scene.\n\n"
                        : "") +
                    "Seeds are a starting point — adjust them by hand afterwards.",
                    "Seed", "Cancel"))
                return -1;

            foreach (var e in existing)
                if (e != null) Object.DestroyImmediate(e.gameObject);

            var root = GameObject.Find("Regions") ?? new GameObject("Regions");

            var placed = new List<Vector3>();
            int index = 0;

            // 1. One seed exactly on each player start.
            System.Array.Sort(starts, MapMarkerRegistry.ComparePlayerStarts);
            foreach (var s in starts)
            {
                var p = s.transform.position;
                Create(root.transform, index++, p, $"{s.Faction} Home");
                placed.Add(p);
            }

            // 2. Farthest-point fill over passable ground.
            var candidates = PassableCandidates(terrain);
            if (candidates.Count == 0)
            {
                Debug.LogWarning($"[RegionSeeder] {scene.name}: no passable sample points — " +
                                 "only the player-start regions were placed.");
            }
            else
            {
                while (index < total)
                {
                    Vector3 best = default;
                    float bestDist = -1f;
                    foreach (var c in candidates)
                    {
                        float nearest = float.MaxValue;
                        for (int i = 0; i < placed.Count; i++)
                        {
                            float d = (c.x - placed[i].x) * (c.x - placed[i].x)
                                    + (c.z - placed[i].z) * (c.z - placed[i].z);
                            if (d < nearest) nearest = d;
                        }
                        if (nearest > bestDist) { bestDist = nearest; best = c; }
                    }
                    if (bestDist < 0f) break;
                    Create(root.transform, index++, best, "");
                    placed.Add(best);
                }
            }

            Debug.Log($"[RegionSeeder] {scene.name}: {index} region(s) — " +
                      $"{starts.Length} home + {index - starts.Length} field.");
            return index;
        }

        private static void Fail(bool interactive, string message)
        {
            Debug.LogWarning(message);
            if (interactive) EditorUtility.DisplayDialog("Region Seeder", message, "OK");
        }

        /// <summary>
        /// Territory count scales with PLAYERS, not with map area.
        ///
        /// It is the number of territories per player that decides whether the
        /// claim game has enough moves in it, and under docs/Design/Regions.md
        /// §2 the starting territory is also the box an Age 0 player is confined
        /// to. Authored reference: 512 x 512 m, 6 players, ~25 territories.
        ///
        /// An earlier pass held region SIZE constant (~3,120 m² from Sundered
        /// Crown) and scaled count with area. That was calibrated on one small
        /// map and gave 65 territories on a 512 m map — past readable, and past
        /// the point where any single flip matters.
        /// </summary>
        private static int SuggestRegionCount(Terrain terrain, int playerCount)
            => Mathf.Clamp(playerCount * 4 + 1, 5, 40);

        /// <summary>
        /// Sample points on walkable ground: between the water line and the
        /// mountain line, inset from the map edge so a seed never lands on the
        /// rim wall.
        /// </summary>
        private static List<Vector3> PassableCandidates(Terrain terrain)
        {
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            var size = data.size;
            var list = new List<Vector3>(SampleRes * SampleRes / 2);

            for (int z = 1; z < SampleRes - 1; z++)
            {
                float nz = z / (float)(SampleRes - 1);
                for (int x = 1; x < SampleRes - 1; x++)
                {
                    float nx = x / (float)(SampleRes - 1);
                    float y = data.GetInterpolatedHeight(nx, nz) + origin.y;
                    if (y <= WaterHeight || y >= MountainHeight) continue;
                    list.Add(new Vector3(origin.x + nx * size.x, y, origin.z + nz * size.z));
                }
            }
            return list;
        }

        private static void Create(Transform parent, int index, Vector3 pos, string name)
        {
            var go = new GameObject($"Region {index:00}{(string.IsNullOrEmpty(name) ? "" : " — " + name)}");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.AddComponent<RegionSeedMarker>().RegionName = name;
        }
    }
}
