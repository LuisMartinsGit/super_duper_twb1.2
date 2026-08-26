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
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Maps;

namespace TheWaningBorder.UI.Menus
{
    public static class ShipGateMenuTrim
    {
        /// <summary>Blue-menu item that opens the Scenarios browser.</summary>
        private const string ScenariosMenuItem = "Menu_Item_Scenarios";

        /// <summary>
        /// ALPHA TEST TRIM (2026-08-13). Menu entries hidden from the shipped
        /// player because the feature behind them is not ready to put in front
        /// of a tester:
        ///   Campaign    — not implemented
        ///   Scenarios   — dev harness; its scenes are not in the build anyway
        ///   Load Game   — save/load is incomplete, and a broken load reads as
        ///                 a lost session to the tester
        ///
        /// Multiplayer UNHIDDEN 2026-08-16: the lockstep pair survived a
        /// two-editor match after the determinism sweep
        /// (docs/Multiplayer_Desync_Sweep_2026-08-16.md), so LAN play goes to
        /// testers. Discovery is LAN-broadcast + probe; internet play needs
        /// direct IP + port forwarding and is not advertised.
        ///
        /// Reversing this is deleting the entries from this array. It only
        /// affects the built player — the editor keeps the full menu, so
        /// development is unchanged (see the UNITY_EDITOR guard below).
        /// docs/Design/Alpha_Build.md
        /// </summary>
        private static readonly string[] AlphaHiddenMenuItems =
        {
            "Menu_Item_Campaign",
            "Menu_Item_Scenarios",
            "Menu_Item_LoadGame",
        };

        /// <summary>
        /// Flip to true to see the trimmed menu in EDITOR play mode, so the
        /// shipped menu can be checked without making a build. Left false so
        /// day-to-day development keeps every entry.
        ///
        /// static readonly rather than const on purpose — a compile-time
        /// constant folds into its `if` and makes the other branch produce
        /// CS0162 unreachable-code warnings (same rationale as
        /// MapRegistry.ShipScenarios).
        /// </summary>
        private static readonly bool PreviewAlphaTrimInEditor = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != TheWaningBorder.Core.SceneNames.Menu) return;

#if UNITY_EDITOR
            // Editor play mode still has every scenario scene on disk, and
            // developers need the full menu. Only the built player is trimmed —
            // unless you are deliberately previewing the shipped menu.
            if (!PreviewAlphaTrimInEditor) return;
#endif
            // Scenarios was already hidden by the ship gate whenever its scenes
            // are excluded (MapRegistry.ShipScenarios); the alpha trim hides it
            // either way, so the two rules simply agree here.
            foreach (string itemName in AlphaHiddenMenuItems)
            {
                var item = FindInScene(scene, itemName);
                if (item == null) continue;
                item.SetActive(false);
            }
            FixNavigationAroundHiddenItems(scene);

            Debug.Log("[ShipGateMenuTrim] Alpha menu trim applied — hid "
                      + string.Join(", ", AlphaHiddenMenuItems));
        }

        /// <summary>
        /// Repair keyboard / gamepad navigation after entries are hidden.
        ///
        /// The menu items are authored with EXPLICIT navigation
        /// (Navigation.Mode.Explicit + hand-set SelectOnUp / SelectOnDown), and
        /// those links point at the very entries the trim disables — so arrow
        /// keys and a gamepad stick walked into a hidden item and dead-ended.
        ///
        /// Switching the survivors to AUTOMATIC is the fix rather than
        /// re-linking the chain by hand: Unity resolves automatic navigation at
        /// selection time from on-screen position and skips anything inactive
        /// or non-interactable. That makes it correct for whatever set of
        /// entries happens to be visible, including the Tutorial item that
        /// TutorialMenuItem injects at runtime — and it stays correct if the
        /// hidden list changes later, with no second thing to keep in step.
        ///
        /// Order-independent by design: if this pass runs first, the Tutorial
        /// clone inherits Automatic from the Skirmish item it is cloned from;
        /// if the injection runs first, this pass finds and fixes the clone
        /// directly. Either way the chain ends up consistent.
        /// </summary>
        private static void FixNavigationAroundHiddenItems(Scene scene)
        {
            int fixedCount = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
                {
                    if (selectable == null) continue;
                    if (!selectable.name.StartsWith(MenuItemPrefix,
                            System.StringComparison.OrdinalIgnoreCase)) continue;

                    var nav = selectable.navigation;
                    if (nav.mode == Navigation.Mode.Automatic) continue;

                    nav.mode = Navigation.Mode.Automatic;
                    selectable.navigation = nav;
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                Debug.Log($"[ShipGateMenuTrim] Switched {fixedCount} menu item(s) to automatic "
                          + "navigation so keyboard/gamepad skips the hidden entries.");
        }

        /// <summary>Naming convention every blue-menu entry follows.</summary>
        private const string MenuItemPrefix = "Menu_Item_";

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
