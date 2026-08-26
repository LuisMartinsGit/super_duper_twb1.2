// SkirmishMenuButton.cs
// Makes the main menu's Skirmish entry open the Skirmish scene.
// Location: Assets/Scripts/UI/Menus/SkirmishMenuButton.cs

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Points the blue menu's Skirmish entry at SkirmishMenu.unity.
    ///
    /// The entry's authored onClick was SetActive(true) on Panel_Skirmish, back
    /// when the screen was a panel in this same scene. Splitting it into its own
    /// scene deleted that panel, which left the call pointing at nothing — a
    /// UnityEvent silently skips a call whose target is null, so the button went
    /// dead rather than erroring.
    ///
    /// Re-authoring the call was tried first and did not stick: the entry lives
    /// inside the blue menu's Synty prefab instance, and the UnityEventTools
    /// edits SkirmishSceneSplit made to it were not written to the scene. This
    /// hook is the mechanism that owns the click, and it works regardless of
    /// what the prefab instance carries - it switches every authored call OFF
    /// before adding its own, so the dead SetActive cannot fire either.
    ///
    /// Same static scene-hook shape as MenuQuitButton / MenuSettingsButton /
    /// ShipGateMenuTrim: no injected controller, no scene edit.
    ///
    /// The destination is read from the MenuNav_Skirmish object's MenuSceneLink
    /// so it stays visible and editable in the Inspector, falling back to
    /// <see cref="TheWaningBorder.Core.SceneNames.Skirmish"/> if that object is gone.
    /// </summary>
    public static class SkirmishMenuButton
    {
        private const string ItemName = "Menu_Item_Skirmish";
        private const string NavName = "MenuNav_Skirmish";

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
                    if (button == null) continue;
                    if (!IsSkirmishEntry(button)) continue;

                    // Switch the authored calls off rather than trying to
                    // remove them: RemoveAllListeners only drops runtime
                    // listeners, and the persistent one here is a dead
                    // SetActive left over from the panel that used to live in
                    // this scene.
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => SceneManager.LoadScene(target));
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[SkirmishMenuButton] Wired {wired} Skirmish entr" +
                          (wired == 1 ? "y" : "ies") + $" to load '{target}'.");
            else
                Debug.LogWarning("[SkirmishMenuButton] No Skirmish entry found in the main " +
                                 "menu — the entry's name and label may both have changed, " +
                                 "and the button will do nothing.");
        }

        /// <summary>Scene name from MenuNav_Skirmish, else the bootstrap constant.</summary>
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
            return TheWaningBorder.Core.SceneNames.Skirmish;
        }

        private static bool IsSkirmishEntry(Button button)
        {
            if (button.gameObject.name == ItemName) return true;

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;

                // Exact match only, English and the Portuguese render — a
                // substring test would also claim "SKIRMISH VS AI".
                string t = text.text.Trim().ToUpperInvariant();
                if (t == "SKIRMISH" || t == "ESCARAMUÇA") return true;
            }
            return false;
        }
    }
}
