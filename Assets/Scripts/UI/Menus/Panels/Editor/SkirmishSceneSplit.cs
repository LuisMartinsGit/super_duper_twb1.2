// SkirmishSceneSplit.cs (editor-only)
// ONE-TIME surgery: lifts Panel_Skirmish out of MainMenu.unity into a scene of
// its own, SkirmishMenu.unity, and rewires the two screens to each other.
//
// Run: Tools > Waning Border > Menu > Split Skirmish Into Its Own Scene.
// The tool saves both scenes itself and refuses to run twice (it will not
// overwrite an existing SkirmishMenu.unity).
//
// What the new scene gets:
//   EventSystem   copied from MainMenu, so input behaves identically
//   Main Camera   copied for its AudioListener (see below)
//   UI_Canvas     copied whole - same CanvasScaler reference, same render mode -
//                 then pruned to two children
//     SPR_Screenshot   the same southood.png backdrop, same rect
//     Panel_Skirmish   moved across intact and switched ON (it was the blue
//                      menu's job to switch it on; now the scene load is)
//
// Navigation:
//   blue menu -> SkirmishMenuButton, a runtime hook. This tool DOES create the
//                MenuNav_Skirmish object that names the destination scene, but
//                it does not wire the click: Menu_Item_Skirmish lives inside the
//                blue menu's Synty prefab instance, and UnityEventTools edits to
//                that Button were not written to the scene when this ran. Its
//                authored onClick is still the old SetActive on the panel that
//                moved out, which now targets nothing; the hook switches every
//                authored call off before adding its own.
//   back      -> SkirmishPanel wires CANCEL to LoadScene(MainMenu) in code.
//
// ONE DELIBERATE VISUAL DIFFERENCE. The blue menu is not copied across, so the
// main-menu entries no longer ghost faintly through the panel's dark overlay.
// Everything else is pixel-identical. Copying it over would have kept that
// ghost, but it would also have put a live, un-ship-gated main menu behind the
// skirmish screen: ShipGateMenuTrim only trims the MainMenu scene, so entries
// the player build hides (Campaign, Scenarios, Load Game) would have shown
// through here.
//
// Music does not restart across the jump: MusicManager treats any non-gameplay
// scene as menu, and CrossfadeTo returns early when the requested clip is
// already the one playing.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.EditorTools
{
    internal static class SkirmishSceneSplit
    {
        private const string MenuPath =
            "Tools/Waning Border/Menu/Split Skirmish Into Its Own Scene";

        private const string MainScenePath =
            "Assets/GameData/Scenes/Menus/MainMenu/MainMenu.unity";
        private const string NewSceneFolder =
            "Assets/GameData/Scenes/Menus/SkirmishMenu";
        private const string NewScenePath =
            NewSceneFolder + "/SkirmishMenu.unity";

        private const string CanvasName = "UI_Canvas";
        private const string BackdropName = "SPR_Screenshot";
        private const string PanelName = "Panel_Skirmish";
        private const string EventSystemName = "EventSystem";
        private const string CameraName = "Main Camera";
        private const string NavName = "MenuNav_Skirmish";

        [MenuItem(MenuPath)]
        private static void Split()
        {
            if (File.Exists(NewScenePath))
            {
                EditorUtility.DisplayDialog("Split Skirmish Scene",
                    NewScenePath + " already exists. The split has already run - " +
                    "delete that scene first if you want it rebuilt.", "OK");
                return;
            }

            var main = SceneManager.GetActiveScene();
            if (main.path != MainScenePath)
            {
                EditorUtility.DisplayDialog("Split Skirmish Scene",
                    "Open " + MainScenePath + " and make it the active scene first.", "OK");
                return;
            }

            // Unsaved edits would be silently discarded by the scene juggling
            // below, and the chrome pass's work lives exactly there.
            if (main.isDirty &&
                !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { main }))
                return;

            var canvas = FindInScene(main, CanvasName);
            var panel = FindInScene(main, PanelName);
            var events = FindInScene(main, EventSystemName);
            var camera = FindInScene(main, CameraName);
            if (canvas == null || panel == null)
            {
                EditorUtility.DisplayDialog("Split Skirmish Scene",
                    $"Could not find both '{CanvasName}' and '{PanelName}' in the open " +
                    "scene. Has the menu been restructured?", "OK");
                return;
            }

            // ── Build the new scene ──────────────────────────────────────
            Directory.CreateDirectory(NewSceneFolder);
            var skirmish = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            if (events != null) CopyRootInto(events, skirmish);

            // The camera comes along for the AudioListener that rides on it.
            // The canvas is Screen Space - Overlay and needs no camera to
            // render, but a scene with no listener plays no sound at all, and
            // MusicManager's menu track would go silent the moment this screen
            // opened. Copying the whole camera also carries its clear colour,
            // so any letterboxing beside the backdrop stays the colour it is
            // in MainMenu.
            if (camera != null) CopyRootInto(camera, skirmish);

            var canvasCopy = CopyRootInto(canvas, skirmish);
            PruneCanvas(canvasCopy);

            var panelCopy = canvasCopy.transform.Find(PanelName);
            if (panelCopy == null)
            {
                EditorSceneManager.CloseScene(skirmish, true);
                EditorUtility.DisplayDialog("Split Skirmish Scene",
                    "The copied canvas lost its panel during the prune. Nothing was " +
                    "changed - the new scene has been discarded.", "OK");
                return;
            }
            // The blue menu used to switch this on; the scene load does now.
            panelCopy.gameObject.SetActive(true);

            EditorSceneManager.MarkSceneDirty(skirmish);
            EditorSceneManager.SaveScene(skirmish, NewScenePath);
            // The folder was made with Directory.CreateDirectory, so neither it
            // nor the scene exists as far as the asset database is concerned
            // until this - and RegisterInBuildSettings below adds the path.
            AssetDatabase.Refresh();

            // ── MainMenu side ────────────────────────────────────────────
            Undo.DestroyObjectImmediate(panel.gameObject);

            var nav = new GameObject(NavName, typeof(MenuSceneLink));
            Undo.RegisterCreatedObjectUndo(nav, "Split Skirmish Scene");
            SceneManager.MoveGameObjectToScene(nav, main);
            nav.GetComponent<MenuSceneLink>().SceneName = MainMenuBootstrap.SkirmishSceneName;

            EditorSceneManager.MarkSceneDirty(main);
            EditorSceneManager.SaveScene(main);

            RegisterInBuildSettings();

            // Leave the editor on MainMenu alone rather than with two menu
            // scenes (and two EventSystems) loaded at once.
            EditorSceneManager.CloseScene(skirmish, true);

            Debug.Log($"[SkirmishSceneSplit] {PanelName} now lives in {NewScenePath}, and " +
                      $"{NavName} names it for SkirmishMenuButton, which wires the blue " +
                      "menu's Skirmish entry at runtime. Both scenes saved and registered " +
                      "in Build Settings.");
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>Copy a root GameObject into another scene, keeping its name
        /// (Instantiate appends "(Clone)").</summary>
        private static GameObject CopyRootInto(GameObject source, Scene target)
        {
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            SceneManager.MoveGameObjectToScene(copy, target);
            return copy;
        }

        /// <summary>Strip the copied canvas down to backdrop + panel.</summary>
        private static void PruneCanvas(GameObject canvasCopy)
        {
            var doomed = new List<GameObject>();
            foreach (Transform child in canvasCopy.transform)
            {
                if (child.name == BackdropName || child.name == PanelName) continue;
                doomed.Add(child.gameObject);
            }
            // Collected first: destroying while enumerating the Transform
            // reshuffles the child indices underneath the iterator.
            foreach (var go in doomed) Object.DestroyImmediate(go);
        }

        /// <summary>Add the new scene to Build Settings, right after MainMenu so
        /// scene 0 stays the boot scene.</summary>
        private static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
                if (Normalize(scenes[i].path) == NewScenePath) return;

            int at = scenes.Count;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (Normalize(scenes[i].path) != MainScenePath) continue;
                at = i + 1;
                break;
            }

            scenes.Insert(at, new EditorBuildSettingsScene(NewScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            // Safe to insert here: MapSceneSync only prunes and appends paths
            // under the map / scenario roots, and leaves everything else alone.
        }

        private static string Normalize(string path) =>
            string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            }
            return null;
        }
    }
}
#endif
