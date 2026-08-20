// MapSceneSync.cs
// EDITOR-ONLY: keeps Build Settings synced to the map/scenario folders on
// disk (user request 2026-08-05: "maps list should be updated automatically
// when a new folder is added to Maps"). MapRegistry discovers playable maps
// from the Build Settings scene list — before this, dropping a new map
// folder into Assets/GameData/Scenes/Maps/ silently did nothing until
// someone remembered File > Build Settings.
//
// Two triggers, one sync:
//   * AssetPostprocessor — fires the moment a .unity is imported, moved or
//     deleted under a managed root (new map folder, renamed map, cleanup).
//   * InitializeOnLoad sweep — catches folders added while Unity was closed.
//
// Rules:
//   * Entries OUTSIDE the managed roots (MainMenu etc.) are never touched.
//   * Existing entries keep their ORDER — the first map scene in the list
//     is the lobby's default map, and that stays a deliberate choice.
//   * New on-disk scenes are appended at the end, enabled.
//   * Managed-root entries whose scene file is gone are removed.
//   * Managed-root entries the ship gate excludes are removed, and excluded
//     scenes are never appended (MapRegistry.ShouldShip). Without this the
//     auto-sync would happily re-add every map the build is meant to drop —
//     the gate has to live HERE, not only in Build Settings, or the next
//     domain reload undoes it.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef (TheWaningBorder.Runtime) with no separate editor assembly; the
// Editor/ folder name alone does not exclude it from player builds.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public sealed class MapSceneSync : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (TouchesManagedScene(imported) || TouchesManagedScene(deleted)
                || TouchesManagedScene(moved) || TouchesManagedScene(movedFrom))
                EditorApplication.delayCall += Sync;
        }

        [InitializeOnLoadMethod]
        private static void SweepOnLoad()
        {
            EditorApplication.delayCall += Sync;
        }

        private static bool TouchesManagedScene(string[] paths)
        {
            if (paths == null) return false;
            for (int i = 0; i < paths.Length; i++)
                if (paths[i].EndsWith(".unity") && IsManaged(Normalize(paths[i])))
                    return true;
            return false;
        }

        private static string Normalize(string p) => p.Replace('\\', '/');

        private static bool IsManaged(string path)
        {
            if (path.StartsWith(MapRegistry.MapsRoot)) return true;
            return IsScenario(path);
        }

        private static bool IsScenario(string path)
        {
            for (int i = 0; i < MapRegistry.ScenarioRoots.Length; i++)
                if (path.StartsWith(MapRegistry.ScenarioRoots[i])) return true;
            return false;
        }

        /// <summary>
        /// Whether a managed scene belongs in the EDITOR's Build Settings
        /// list. Editor play mode can only load scenes that are listed, so
        /// scenario scenes must stay in — the whole ship-gate design promised
        /// "fully playable in the editor", but gating them out of the list
        /// broke every scenario launch in play mode (2026-08-17: the
        /// Scenarios menu failed with "scene not in build profile"). Player
        /// builds still exclude them via <see cref="FilterScenesForBuild"/>.
        /// Non-shipping MAPS stay gated here on purpose: the lobby dropdown
        /// reads this list, and the gate is what keeps them unselectable.
        /// </summary>
        private static bool AllowedInEditorList(string path)
            => IsScenario(path) || MapRegistry.ShouldShip(path);

        /// <summary>
        /// Build-time half of the scenario gate: the editor list keeps
        /// scenario scenes (see <see cref="AllowedInEditorList"/>), so the
        /// exclusion from PLAYER builds happens here, on the Build button's
        /// scene list. The shipped menu hides the Scenarios entry anyway
        /// (ShipGateMenuTrim); this keeps the scenes' bytes out of the build
        /// as Alpha_Build.md promises.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterBuildFilter()
        {
            try
            {
                BuildPlayerWindow.RegisterBuildPlayerHandler(FilterScenesForBuild);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MapSceneSync] Could not register the build-time " +
                                 $"scenario filter ({e.Message}) — a player build would " +
                                 "include scenario scenes.");
            }
        }

        private static void FilterScenesForBuild(BuildPlayerOptions options)
        {
            if (!MapRegistry.ShipScenarios && options.scenes != null)
            {
                var kept = options.scenes.Where(s => !IsScenario(Normalize(s))).ToArray();
                if (kept.Length != options.scenes.Length)
                    Debug.Log($"[MapSceneSync] Player build: {options.scenes.Length - kept.Length} " +
                              "scenario scene(s) filtered out (ship gate).");
                options.scenes = kept;
            }
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }

        private static void Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Every scene on disk under the managed roots.
            var onDisk = new List<string>();
            CollectScenes(MapRegistry.MapsRoot, onDisk);
            for (int i = 0; i < MapRegistry.ScenarioRoots.Length; i++)
                CollectScenes(MapRegistry.ScenarioRoots[i], onDisk);
            onDisk.Sort(System.StringComparer.OrdinalIgnoreCase);

            var current = EditorBuildSettings.scenes;
            var kept = new List<EditorBuildSettingsScene>(current.Length + onDisk.Count);
            var listed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            int removed = 0;

            // Pass 1: keep everything in order; drop managed entries whose
            // scene no longer exists on disk or that the ship gate excludes.
            foreach (var s in current)
            {
                string path = Normalize(s.path);
                if (IsManaged(path)
                    && (!File.Exists(path) || !AllowedInEditorList(path)))
                {
                    removed++;
                    continue;
                }
                kept.Add(s);
                listed.Add(path);
            }

            // Pass 2: append newly discovered scenes the gate allows, enabled.
            int added = 0;
            foreach (var path in onDisk)
            {
                if (listed.Contains(path)) continue;
                if (!AllowedInEditorList(path)) continue;
                kept.Add(new EditorBuildSettingsScene(path, true));
                added++;
            }

            if (added == 0 && removed == 0) return;

            EditorBuildSettings.scenes = kept.ToArray();
            Debug.Log($"[MapSceneSync] Build Settings synced: +{added} scene(s), " +
                      $"-{removed} stale/gated. Maps under {MapRegistry.MapsRoot} appear " +
                      "in the lobby automatically; the FIRST map entry stays the default. " +
                      $"Ship gate: maps=[{string.Join(", ", MapRegistry.ShippingMapScenes)}]" +
                      $"{(MapRegistry.ShipAllMaps ? " (ALL)" : "")}, " +
                      $"scenarios={(MapRegistry.ShipScenarios ? "shipped" : "excluded")}.");
        }

        private static void CollectScenes(string root, List<string> into)
        {
            string dir = root.TrimEnd('/');
            if (!Directory.Exists(dir)) return;
            into.AddRange(Directory
                .GetFiles(dir, "*.unity", SearchOption.AllDirectories)
                .Select(Normalize));
        }
    }
}
#endif
