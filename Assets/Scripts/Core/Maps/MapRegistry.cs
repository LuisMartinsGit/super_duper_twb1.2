// MapRegistry.cs
// Authoritative list of playable maps. The skirmish / multiplayer lobby
// dropdowns read this list; bootstrap scene-gates accept any entry's
// SceneName as a valid gameplay scene; GameBootstrap reads IsProcedural
// to decide whether to spin up ProceduralTerrain or leave the scene's
// baked-in Unity Terrain alone.
//
// The list is discovered at runtime from Build Settings: every scene whose
// path lives under Assets/GameData/Scenes/Maps/ is a playable map, and the
// FOLDER name (not the scene file name) is the lobby display name.
//
// To register a new map:
//   1. Create Assets/GameData/Scenes/Maps/<Map Name>/ and put the .unity
//      scene inside — the folder name becomes the lobby display name.
//      Build Settings registration is AUTOMATIC (MapSceneSync, 2026-08-05):
//      the editor appends any new map/scenario scene the moment it appears
//      on disk and drops stale entries. The FIRST map entry in Build
//      Settings stays the lobby default — reorder there to change it.
//   2. Place markers (PlayerStartMarker, IronPatchMarker, etc.) in the
//      scene if you want hand-authored spawn positions. Without markers
//      the game falls back to procedural placement, which assumes a
//      ProceduralTerrain — so non-procedural maps should always have at
//      least PlayerStartMarkers.

using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Core.Maps
{
    /// <summary>One entry in the playable-map list.</summary>
    public readonly struct MapEntry
    {
        /// <summary>Label shown in the lobby dropdown.</summary>
        public readonly string DisplayName;

        /// <summary>Scene name as it appears in Build Settings (no path, no .unity).</summary>
        public readonly string SceneName;

        /// <summary>
        /// True → GameBootstrap creates ProceduralTerrain and lets it generate
        /// a noise-driven heightmap. False → the scene already contains a
        /// baked Unity Terrain (e.g. MapMagic output) and ProceduralTerrain
        /// is skipped entirely.
        /// </summary>
        public readonly bool IsProcedural;

        public MapEntry(string display, string scene, bool procedural)
        {
            DisplayName = display;
            SceneName = scene;
            IsProcedural = procedural;
        }
    }

    public static class MapRegistry
    {
        /// <summary>Asset folder whose sub-folders define the playable maps.</summary>
        public const string MapsRoot = "Assets/GameData/Scenes/Maps/";

        /// <summary>Asset folders holding per-scenario gameplay scenes. These are
        /// NOT lobby maps (they never appear in the skirmish/MP dropdowns) but
        /// they ARE gameplay scenes, so GameBootstrap must accept them. Both the
        /// legacy enum-based location and the data-driven per-folder location
        /// (Assets/GameData/Scenarios/&lt;Name&gt;/) count.</summary>
        public static readonly string[] ScenarioRoots =
        {
            "Assets/GameData/Scenarios/",
            "Assets/GameData/Scenes/Scenarios/",
        };

        // ── Ship gate (2026-08-09) ─────────────────────────────────────────
        // The first public build ships ONE map and no dev scenarios. Every
        // other map/scenario scene STAYS in the project and stays fully
        // playable in the editor — it is only kept out of Build Settings, so
        // it neither reaches the lobby nor gets exported into the player.
        //
        // Build Settings is the single enforcement point: MapSceneSync
        // applies this gate on every domain reload, which is also what stops
        // the auto-sync from silently re-adding the excluded scenes. Nothing
        // filters at runtime, so whatever is listed is what ships.
        //
        // To go back to shipping everything: flip ShipAllMaps and
        // ShipScenarios to true. Nothing else needs to change.

        // static readonly, not const: as compile-time constants these fold
        // into their `if` sites and every guarded branch turns into a fresh
        // CS0162 "unreachable code" warning.

        /// <summary>False → only <see cref="ShippingMapScenes"/> reach Build Settings.</summary>
        public static readonly bool ShipAllMaps = false;

        /// <summary>False → no scenario scene reaches Build Settings.</summary>
        public static readonly bool ShipScenarios = false;

        /// <summary>
        /// Scene file names (no path, no .unity) of the maps that ship.
        ///
        /// This list is also the only thing that makes a map SELECTABLE AT
        /// ALL — the lobby dropdown reads Build Settings, and MapSceneSync
        /// drops any managed scene the gate excludes on every domain reload.
        /// A new map therefore has to be listed here or it cannot be played,
        /// editor included. To cut a map from the first public build without
        /// deleting it, take its name back out of this array.
        /// </summary>
        public static readonly string[] ShippingMapScenes =
        {
            "SunderedCrown",   // 4P free-for-all
            "HollowTable",     // 1v1 duel, one central well
            "TwinSpans",       // 3v3 river, two crossings, four bridgehead wells
            "SunderedReach",   // 3P, 704 m (4x Twin Spans' area), 10 regions
            "Veilmarch",       // 4P, 1024 m open field, curse-only centre, 21 regions
        };

        /// <summary>
        /// True if a scene belongs in Build Settings under the current ship
        /// gate. Scenes outside the managed roots (MainMenu, GameUI, …) are
        /// not the gate's business and always pass.
        /// </summary>
        public static bool ShouldShip(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;
            string path = scenePath.Replace('\\', '/');

            for (int i = 0; i < ScenarioRoots.Length; i++)
                if (path.StartsWith(ScenarioRoots[i])) return ShipScenarios;

            if (!path.StartsWith(MapsRoot)) return true;
            if (ShipAllMaps) return true;

            string scene = System.IO.Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < ShippingMapScenes.Length; i++)
                if (string.Equals(scene, ShippingMapScenes[i],
                                  System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static List<MapEntry> _maps;
        private static HashSet<string> _scenarioScenes;

        /// <summary>
        /// Playable maps in Build Settings order — index 0 (the first map
        /// scene listed in Build Settings) is the default selection, so the
        /// default is chosen deliberately there, not by folder name. Built
        /// lazily from the Build Settings scene list — any scene under
        /// <see cref="MapsRoot"/> counts, named after its folder.
        /// </summary>
        public static IReadOnlyList<MapEntry> Maps
        {
            get
            {
                if (_maps == null) BuildList();
                return _maps;
            }
        }

        public static MapEntry Default => Maps[0];

        private static void BuildList()
        {
            _maps = new List<MapEntry>();
            _scenarioScenes = new HashSet<string>();

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < sceneCount; i++)
            {
                // Full asset path, e.g. "Assets/GameData/Scenes/Maps/Fiendstone Pass/FiendstonePass.unity".
                // Available in player builds too, so this works outside the editor.
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                // Normalise like ShouldShip does. The two used to disagree:
                // ShouldShip replaced backslashes and this did not, so a
                // backslashed path could pass the ship gate and still fail the
                // MapsRoot prefix test here, hiding the map from the lobby.
                path = path.Replace('\\', '/');

                bool isMap = path.StartsWith(MapsRoot);
                bool isScenario = IsUnderScenarioRoot(path);
                if (!isMap && !isScenario) continue;

#if UNITY_EDITOR
                // Build Settings can hold stale entries for scenes deleted
                // from disk. Loading one no-ops with an error and leaves the
                // player stranded in the lobby scene — skip them. (In player
                // builds every listed scene exists by construction.)
                if (!System.IO.File.Exists(path))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[MapRegistry] Build Settings references \"{path}\" but the scene file " +
                        "no longer exists — skipping. Remove the stale entry via " +
                        "File → Build Settings.");
                    continue;
                }
#endif

                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                // Scenario scenes are gameplay scenes but not lobby maps.
                if (isScenario)
                {
                    _scenarioScenes.Add(sceneName);
                    continue;
                }

                // Display name = first folder under MapsRoot; a scene sitting
                // directly in MapsRoot falls back to its file name.
                string relative = path.Substring(MapsRoot.Length);
                int slash = relative.IndexOf('/');
                string display = slash > 0 ? relative.Substring(0, slash) : sceneName;

                // Procedural generation has been removed; only hand-authored maps remain.
                _maps.Add(new MapEntry(display, sceneName, procedural: false));
            }

            if (_maps.Count == 0)
            {
                UnityEngine.Debug.LogError(
                    $"[MapRegistry] No scenes under {MapsRoot} found in Build Settings — " +
                    "the lobby map list is empty. Add at least one map scene to " +
                    "File → Build Settings → Scenes in Build.");
                // Keep Default/IndexOf safe with a placeholder pointing at the
                // map the build is supposed to ship.
                _maps.Add(new MapEntry("Sundered Crown", "SunderedCrown", procedural: false));
            }

#if UNITY_EDITOR
            WarnAboutUnregisteredMapFolders();
#endif
        }

        private static bool IsUnderScenarioRoot(string path)
        {
            for (int i = 0; i < ScenarioRoots.Length; i++)
                if (path.StartsWith(ScenarioRoots[i])) return true;
            return false;
        }

        /// <summary>True if the given scene is one of the registered playable
        /// maps. Bootstrap scene-name gates use this to decide whether to
        /// run their "we're in a game" branch.</summary>
        public static bool IsGameplayScene(string sceneName)
        {
            var maps = Maps; // also builds _scenarioScenes
            for (int i = 0; i < maps.Count; i++)
                if (maps[i].SceneName == sceneName) return true;
            return _scenarioScenes.Contains(sceneName);
        }

        /// <summary>Find the entry for the given scene name, or the default
        /// entry if the scene isn't registered.</summary>
        public static MapEntry GetEntry(string sceneName)
        {
            var maps = Maps;
            for (int i = 0; i < maps.Count; i++)
                if (maps[i].SceneName == sceneName) return maps[i];
            return Default;
        }

        public static int IndexOf(string sceneName)
        {
            var maps = Maps;
            for (int i = 0; i < maps.Count; i++)
                if (maps[i].SceneName == sceneName) return i;
            return 0;
        }

        /// <summary>
        /// Authoritative check for "should procedural systems (ProceduralTerrain,
        /// ObstacleBootstrap, the procedural splat layers) run for this scene?"
        ///
        /// Returns true ONLY when both:
        ///   1. The scene's registry entry says IsProcedural, AND
        ///   2. There is no Unity Terrain already in the scene.
        ///
        /// Rule 2 protects against Unicode normalization issues where the
        /// scene name has accented characters (yielLymwérra etc.) and the
        /// registry string match silently fails — the baked Terrain is a more
        /// reliable "the designer hand-authored this map" signal than the
        /// string lookup.
        /// </summary>
        public static bool ShouldRunProceduralGeneration(string sceneName)
        {
            var entry = GetEntry(sceneName);
            if (!entry.IsProcedural) return false;
            // Even if the registry says procedural, a baked Terrain in the
            // scene overrides — never run procedural on top of one.
            return UnityEngine.Terrain.activeTerrain == null;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: flag map folders on disk whose scene never
        /// made it into Build Settings — those maps silently miss the lobby.</summary>
        private static void WarnAboutUnregisteredMapFolders()
        {
            string root = MapsRoot.TrimEnd('/');
            if (!System.IO.Directory.Exists(root)) return;

            foreach (string dir in System.IO.Directory.GetDirectories(root))
            {
                string folderName = System.IO.Path.GetFileName(dir);
                bool registered = false;
                for (int i = 0; i < _maps.Count; i++)
                    if (_maps[i].DisplayName == folderName) { registered = true; break; }
                if (registered) continue;

                if (System.IO.Directory.GetFiles(dir, "*.unity").Length > 0)
                    UnityEngine.Debug.LogWarning(
                        $"[MapRegistry] Map folder \"{folderName}\" has a scene that is not in " +
                        "Build Settings — it will not appear in the lobby map list. Add it via " +
                        "File → Build Settings → Scenes in Build.");
            }
        }
#endif
    }
}
