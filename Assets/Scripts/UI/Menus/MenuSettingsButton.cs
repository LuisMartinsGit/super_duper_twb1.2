// MenuSettingsButton.cs
// Makes the main menu's Settings entry actually open the options panel.
// Location: Assets/Scripts/UI/Menus/MenuSettingsButton.cs

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Wires the main menu's Settings entry to <see cref="OptionsMenuUI"/>.
    ///
    /// The authored menu never had a settings panel: the Synty scene contains
    /// no such pane, and the entry — like Quit before MenuQuitButton — carries
    /// no onClick, so clicking it showed nothing. OptionsMenuUI itself has
    /// been orphaned since the IMGUI MainMenuUI was deleted (2026-07-16); it
    /// still works, nothing created it. This hook spawns it on demand and
    /// toggles it from the menu entry.
    ///
    /// Matched primarily by GameObject NAME ("Menu_Item_Settings" — unlike
    /// Quit, this entry is canonically named), with the visible label as a
    /// fallback. Label matching accepts the Portuguese renders too, because
    /// LocAuthoredLabel may have translated the authored text before this
    /// hook runs.
    ///
    /// Same static scene-hook shape as MenuQuitButton / ShipGateMenuTrim: no
    /// injected controller, no scene edit.
    /// </summary>
    public static class MenuSettingsButton
    {
        private const string ItemName = "Menu_Item_Settings";

        private static GameObject _options;

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

            int wired = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (button == null) continue;
                    if (!IsSettingsEntry(button)) continue;

                    // Same trap MenuQuitButton documents: a duplicated entry
                    // can carry another item's persistent call, and
                    // RemoveAllListeners only drops runtime listeners.
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(Toggle);
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[MenuSettingsButton] Wired {wired} Settings button(s) to the options panel.");
            else
                Debug.LogWarning("[MenuSettingsButton] No Settings button found in the main menu — "
                                 + "the entry's name and label may both have changed.");
        }

        private static bool IsSettingsEntry(Button button)
        {
            if (button.gameObject.name == ItemName) return true;

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;

                string t = text.text.Trim().ToUpperInvariant();
                // Exact match only, English and Portuguese renders.
                if (t == "SETTINGS" || t == "OPTIONS" || t == "DEFINIÇÕES" || t == "OPÇÕES")
                    return true;
            }
            return false;
        }

        private static void Toggle()
        {
            // Scene-local on purpose: the object dies with the menu scene and
            // is rebuilt on the next click, so no stale state survives into a
            // match. Unity's overloaded == treats the destroyed case as null.
            if (_options == null)
            {
                _options = new GameObject("OptionsMenu(Runtime)");
                var ui = _options.AddComponent<OptionsMenuUI>();
                ui.OnBackPressed += () => { if (_options != null) _options.SetActive(false); };
                return;   // AddComponent leaves it active; panel is now open
            }

            _options.SetActive(!_options.activeSelf);
        }
    }
}
