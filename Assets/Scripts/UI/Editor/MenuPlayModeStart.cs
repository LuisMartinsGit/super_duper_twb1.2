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
// want Play to always boot into the Synty main-menu scene first (like a build).

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

        // GUID of Assets/Synty/InterfaceFantasyMenus/Samples/Scenes/
        // 05_Demo_FantasyMenus_Screen_MainMenu_02.unity
        private const string MenuSceneGuid = "3780ed27d217e6540a5fcd65434ef342";

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
            if (Enabled)
            {
                string path = AssetDatabase.GUIDToAssetPath(MenuSceneGuid);
                var scene = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

                if (scene == null)
                    Debug.LogWarning("[MenuPlayModeStart] Synty menu scene not found by GUID — " +
                                     "Play will run the open scene. Update MenuSceneGuid if the scene moved.");

                EditorSceneManager.playModeStartScene = scene;
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }

            Menu.SetChecked(MenuPath, Enabled);
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
