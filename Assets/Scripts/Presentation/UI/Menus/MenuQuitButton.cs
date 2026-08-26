// MenuQuitButton.cs
// Makes the main menu's Quit entry actually quit.

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Wires the main menu's Quit entry to <see cref="Application.Quit"/>.
    ///
    /// The menu is authored entirely in the editor, and only three of its
    /// buttons were ever given an onClick (Skirmish, Multiplayer, Scenarios) —
    /// Quit had none, so clicking it did nothing. The only Application.Quit in
    /// the project is the in-game pause menu's.
    ///
    /// Matched by LABEL rather than by object name on purpose: the entry is not
    /// named "Menu_Item_Quit" like its siblings (it keeps the Synty prefab's
    /// own naming), so a name lookup would silently miss it — which is exactly
    /// the class of failure that left it dead in the first place. The visible
    /// word on the button is the reliable identifier.
    ///
    /// Same static scene-hook shape as ShipGateMenuTrim / MenuVersionLabel: no
    /// injected controller, no scene edit, so the hand-authored wiring is safe.
    /// </summary>
    public static class MenuQuitButton
    {
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

            int wired = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (button == null) continue;
                    if (!IsQuitLabel(button)) continue;

                    // Switch off any inherited Inspector wiring first. If this
                    // entry was ever duplicated from another menu item it would
                    // carry that item's persistent call (e.g. open Skirmish),
                    // and RemoveAllListeners only drops RUNTIME listeners —
                    // the same trap TutorialMenuItem documents.
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(Quit);
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[MenuQuitButton] Wired {wired} Quit button(s) to Application.Quit.");
            else
                Debug.LogWarning("[MenuQuitButton] No Quit button found in the main menu — "
                                 + "the entry's label may have been renamed.");
        }

        /// <summary>True when the button's visible text reads Quit or Exit.</summary>
        private static bool IsQuitLabel(Button button)
        {
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;

                string t = text.text.Trim().ToUpperInvariant();
                // Exact match only: "QUIT TO MAIN MENU" or similar must not be
                // caught by a Contains check. The Portuguese renders are
                // accepted too — LocAuthoredLabel may have already translated
                // the authored "Quit" before this hook runs, and the visible
                // word is the only identifier this wiring has.
                if (t == "QUIT" || t == "EXIT" || t == "QUIT GAME" || t == "EXIT GAME"
                    || t == "SAIR" || t == "SAIR DO JOGO")
                    return true;
            }
            return false;
        }

        private static void Quit()
        {
            Debug.Log("[MenuQuitButton] Quit requested.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
