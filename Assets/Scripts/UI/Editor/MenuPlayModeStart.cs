// Editor-only tool inside the single runtime asmdef: the Editor/ folder
// convention does NOT apply within an .asmdef, so without this guard the
// file is compiled into PLAYER builds and fails them (UnityEditor missing).
#if UNITY_EDITOR
// MenuPlayModeStart.cs (editor-only)
// OFF by default: pressing Play runs the CURRENTLY-OPEN scene (Unity's normal
// behaviour). It clears any Play-Mode-start-scene override so nothing forces you
// into the menu.
//
// Optional: turn ON  Tools > Waning Border > Boot into Menu on Play  if you ever
// want Play to always boot into the main-menu scene first (like a build).
//
// -- The bug this file used to cause (fixed 2026-08-19) -------------------
// MenuSceneGuid pointed at Synty's SAMPLE scene,
// Assets/Synty/InterfaceFantasyMenus/Samples/Scenes/
// 05_Demo_FantasyMenus_Screen_MainMenu_02.unity, instead of the game's
// MainMenu.unity. That is a uniquely nasty thing to get wrong, because
// MainMenu.unity was BUILT from the same Synty prefabs - the same
// Screen_FantasyMenus_MainMenu_02, the same three title labels, the same
// section break - so the demo scene is visually indistinguishable from the real
// main menu. What you saw was: press Play on any scene, land on what looks like
// the main menu, and none of the buttons work.
//
// They did not work because every runtime hook that wires that menu gates on
// the SCENE NAME - SkirmishMenuButton, ShipGateMenuTrim, MenuQuitButton,
// MenuSettingsButton, MenuVersionLabel, TutorialMenuItem all open with
//     if (scene.name != MainMenuBootstrap.MenuSceneName) return;
// and the sample scene is called "05_Demo_FantasyMenus_Screen_MainMenu_02".
// So every hook bailed, no onClick was ever attached, and the entries kept
// Synty's demo wiring, which does nothing outside Synty's demo.
//
// Three things changed so this cannot repeat silently:
//   1. the GUID is the game's MainMenu.unity;
//   2. the override is ANNOUNCED in the Console whenever it is active, naming
//      the scene, instead of being an invisible per-machine setting;
//   3. a target that is not in Build Settings is called out as suspicious -
//      that alone would have flagged the sample scene, which is not shipped.
//
// The toggle lives in EditorPrefs, which is per-machine and invisible to git:
// nothing in the repo can tell you it is on, which is why (2) exists. Anyone
// who had it stuck on the sample scene gets it cleared once, automatically.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    [InitializeOnLoad]
    internal static class MenuPlayModeStart
    {
        private const string MenuPath = "Tools/Waning Border/Boot into Menu on Play";
        private const string PrefKey = "TWB.BootIntoMenuOnPlay";

        /// <summary>One-shot migration latch. The preference is per-machine, so
        /// fixing the GUID in the repo does nothing for an editor that already
        /// has the toggle on - it would just start booting the right scene
        /// without the user ever having asked for a boot scene at all. Clearing
        /// it once puts everyone back on Unity's default behaviour.</summary>
        private const string DemoResetKey = "TWB.BootIntoMenuOnPlay.ClearedDemoOverride";

        /// <summary>GUID of Assets/GameData/Scenes/Menus/MainMenu/MainMenu.unity
        /// - the GAME's main menu. Do not point this at anything under
        /// Assets/Synty/Samples; see the header.</summary>
        private const string MenuSceneGuid = "97eb3b31053f90d43b3dc6f1c58afa0c";

        static MenuPlayModeStart()
        {
            // Defer until the AssetDatabase is ready after a domain reload.
            EditorApplication.delayCall += Apply;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false); // OFF by default — Play runs the open scene
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        private static void Apply()
        {
            ClearStaleDemoOverrideOnce();

            if (Enabled)
            {
                string path = AssetDatabase.GUIDToAssetPath(MenuSceneGuid);
                var scene = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

                if (scene == null)
                {
                    Debug.LogWarning("[MenuPlayModeStart] Main-menu scene not found by GUID — " +
                                     "Play will run the open scene. Update MenuSceneGuid if the " +
                                     "scene moved.");
                }
                else
                {
                    // Loud on purpose. An invisible per-machine setting that
                    // overrides which scene Play runs is exactly the kind of
                    // thing that costs an afternoon; it should say so.
                    Debug.Log($"[MenuPlayModeStart] 'Boot into Menu on Play' is ON — Play will " +
                              $"start '{scene.name}' ({path}), NOT the scene you have open. " +
                              $"Turn it off under {MenuPath}.");

                    if (!InBuildSettings(path))
                        Debug.LogWarning($"[MenuPlayModeStart] '{path}' is not in Build Settings. " +
                                         "A boot scene that does not ship is almost certainly the " +
                                         "wrong target — the game's runtime menu hooks gate on the " +
                                         "scene NAME, so a look-alike scene loads with every " +
                                         "button dead.");
                }

                EditorSceneManager.playModeStartScene = scene;
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }

            Menu.SetChecked(MenuPath, Enabled);
        }

        /// <summary>
        /// Turn the toggle off once on any editor that still carries it from
        /// when it pointed at Synty's sample scene. Runs exactly once per
        /// machine and says so, rather than silently changing a preference.
        /// </summary>
        private static void ClearStaleDemoOverrideOnce()
        {
            if (EditorPrefs.GetBool(DemoResetKey, false)) return;
            EditorPrefs.SetBool(DemoResetKey, true);

            if (!Enabled) return;

            Enabled = false;
            EditorSceneManager.playModeStartScene = null;
            Debug.Log("[MenuPlayModeStart] 'Boot into Menu on Play' was on and pointing at " +
                      "Synty's SAMPLE main-menu scene, which looks identical to the real one but " +
                      "has none of the game's button wiring. Turned it off — Play now runs the " +
                      "scene you have open. Re-enable it under " + MenuPath + " if you want it; " +
                      "it targets the game's MainMenu.unity now.");
        }

        private static bool InBuildSettings(string scenePath)
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path == scenePath) return true;
            return false;
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}

#endif // UNITY_EDITOR
