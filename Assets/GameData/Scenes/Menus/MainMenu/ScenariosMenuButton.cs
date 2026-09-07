// ScenariosMenuButton.cs
// Makes the main menu's Scenarios entry open the Scenarios scene.

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Points the blue menu's Scenarios entry at ScenariosMenu.unity.
    ///
    /// Same hook, same reasons, as <see cref="SkirmishMenuButton"/>: the entry
    /// lives inside the blue menu's Synty prefab instance, where authored
    /// onClick edits do not survive into the scene, so the click is owned
    /// here. Every authored call is switched OFF first — that also retires the
    /// old behaviour, which was SetActive on MainMenu's own Panel_Scenarios /
    /// Popup_Scenarios. Both were deleted when the browser became its own
    /// scene (2026-09-07); a UnityEvent silently skips a call whose target is
    /// gone, so without this the entry would simply do nothing.
    ///
    /// The destination is read from the MenuNav_Scenarios object's
    /// MenuSceneLink so it stays visible and editable in the Inspector, and
    /// falls back to <see cref="TheWaningBorder.Core.SceneNames.Scenarios"/>.
    ///
    /// ShipGateMenuTrim hides this entry in the player build (the scenario
    /// scenes are not shipped); the wiring is harmless there and is what the
    /// editor uses every day.
    /// </summary>
    public static class ScenariosMenuButton
    {
        private const string ItemName = "Menu_Item_Scenarios";
        private const string NavName = "MenuNav_Scenarios";

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
                    if (button == null || !IsScenariosEntry(button)) continue;

                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => SceneManager.LoadScene(target));
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[ScenariosMenuButton] Wired {wired} Scenarios entr" +
                          (wired == 1 ? "y" : "ies") + $" to load '{target}'.");
            else
                Debug.LogWarning("[ScenariosMenuButton] No Scenarios entry found in the main " +
                                 "menu — the entry's name and label may both have changed, " +
                                 "and the button will do nothing.");
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
            return TheWaningBorder.Core.SceneNames.Scenarios;
        }

        private static bool IsScenariosEntry(Button button)
        {
            if (button.gameObject.name == ItemName) return true;

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;

                // Exact match only, English and the Portuguese render — the
                // browser's own title is "SCENARIOS" too, but it is never in
                // this scene, and "SCENARIO" (singular) is a different label.
                string t = text.text.Trim().ToUpperInvariant();
                if (t == "SCENARIOS" || t == "CENÁRIOS") return true;
            }
            return false;
        }
    }
}
