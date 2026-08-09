// ShipGateMenuTrim.cs
// Hides main-menu entries whose content the ship gate keeps out of the build
// (2026-08-09).
//
// Why: the Scenarios browser launches scenario scenes by name, and
// MapRegistry.ShipScenarios == false keeps every scenario scene OUT of Build
// Settings. In a player build those loads would fail and strand the player on
// the menu, so the entry has to go with them. In the editor the scenes are all
// still on disk and load fine, so the entry stays — this trims the shipped
// player only.
//
// Deliberately a static scene hook rather than an injected controller: the
// main menu is authored and wired entirely in the editor, and past auto-mounted
// menu controllers clobbered that wiring (see MainMenuBootstrap). This only
// ever flips one GameObject inactive.

using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Maps;

namespace TheWaningBorder.UI.Menus
{
    public static class ShipGateMenuTrim
    {
        /// <summary>Blue-menu item that opens the Scenarios browser.</summary>
        private const string ScenariosMenuItem = "Menu_Item_Scenarios";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MainMenuBootstrap.MenuSceneName) return;

            // Scenario scenes ship => nothing to trim.
            if (MapRegistry.ShipScenarios) return;

#if UNITY_EDITOR
            // Editor play mode still has every scenario scene on disk, and
            // developers need the browser. Only the built player loses it.
            return;
#else
            var item = FindInScene(scene, ScenariosMenuItem);
            if (item == null) return;
            item.SetActive(false);
            Debug.Log($"[ShipGateMenuTrim] Hid \"{ScenariosMenuItem}\" — scenario "
                      + "scenes are excluded from this build (MapRegistry.ShipScenarios).");
#endif
        }

        /// <summary>Depth-first search for a named object, inactive ones included.</summary>
        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                        return t.gameObject;
            }
            return null;
        }
    }
}
