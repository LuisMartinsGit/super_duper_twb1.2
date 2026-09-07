// MenuSettingsButton.cs
// Makes the main menu's Settings entry open the Settings scene.

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Points the blue menu's Settings entry at SettingsMenu.unity.
    ///
    /// The Settings screen was, until 2026-09-07, an OptionsPanel prefab
    /// instance sitting inactive inside MainMenu.unity that this hook toggled
    /// with SetActive — the last screen still living inside the main menu
    /// scene, and the only one drawn in a look of its own rather than the
    /// Skirmish scene's. It is its own scene now, built from the Skirmish
    /// scene's parts by MenuSceneBuilder, and this hook loads it exactly as
    /// <see cref="SkirmishMenuButton"/> loads the skirmish screen.
    ///
    /// Matched primarily by GameObject NAME ("Menu_Item_Settings"), with the
    /// visible label as a fallback; label matching accepts the Portuguese
    /// renders too, because LocAuthoredLabel may have translated the authored
    /// text before this hook runs. The destination is read from the
    /// MenuNav_Settings object's MenuSceneLink so it stays visible in the
    /// Inspector, falling back to
    /// <see cref="TheWaningBorder.Core.SceneNames.Settings"/>.
    /// </summary>
    public static class MenuSettingsButton
    {
        private const string ItemName = "Menu_Item_Settings";
        private const string NavName = "MenuNav_Settings";

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

            string target = ResolveTargetScene(scene);
            int wired = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (button == null || !IsSettingsEntry(button)) continue;

                    // Same trap MenuQuitButton documents: a duplicated entry
                    // can carry another item's persistent call, and
                    // RemoveAllListeners only drops runtime listeners.
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => SceneManager.LoadScene(target));
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[MenuSettingsButton] Wired {wired} Settings button(s) to load '{target}'.");
            else
                Debug.LogWarning("[MenuSettingsButton] No Settings button found in the main menu — "
                                 + "the entry's name and label may both have changed.");
        }

        private static string ResolveTargetScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var link in root.GetComponentsInChildren<MenuSceneLink>(true))
                {
                    if (link == null || link.gameObject.name != NavName) continue;
                    if (!string.IsNullOrWhiteSpace(link.SceneName)) return link.SceneName;
                }
            }
            return TheWaningBorder.Core.SceneNames.Settings;
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
    }
}
