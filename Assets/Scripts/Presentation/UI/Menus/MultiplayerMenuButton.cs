// MultiplayerMenuButton.cs
// Makes the main menu's Multiplayer entry open the Multiplayer scene.
// Location: Assets/Scripts/UI/Menus/MultiplayerMenuButton.cs

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Points the blue menu's Multiplayer entry at MultiplayerMenu.unity.
    ///
    /// Same hook, same reasons, as <see cref="SkirmishMenuButton"/>: the entry
    /// lives inside the blue menu's Synty prefab instance, and UnityEventTools
    /// edits to that Button do not survive into the scene — so the click is
    /// owned here rather than authored. Every authored call is switched OFF
    /// first, which also disposes of the old behaviour: the entry used to
    /// SetActive MainMenu's own Panel_Multiplayer.
    ///
    /// That in-scene panel is now legacy and nothing opens it. It is left in
    /// MainMenu.unity rather than deleted, but do not expect it to work: the
    /// lobby flow moved its HOST button onto the browse pane, and MainMenu's
    /// copy of that pane never got one — MultiplayerPanel opens straight into
    /// browsing, so from the old panel there would be no way to host at all.
    /// </summary>
    public static class MultiplayerMenuButton
    {
        private const string ItemName = "Menu_Item_Multiplayer";

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
                    if (button == null || !IsMultiplayerEntry(button)) continue;

                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(
                        () => SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Multiplayer));
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[MultiplayerMenuButton] Wired {wired} Multiplayer entr" +
                          (wired == 1 ? "y" : "ies") +
                          $" to load '{TheWaningBorder.Core.SceneNames.Multiplayer}'.");
            else
                Debug.LogWarning("[MultiplayerMenuButton] No Multiplayer entry found in the " +
                                 "main menu — the entry's name and label may both have changed, " +
                                 "and the button will do nothing.");
        }

        private static bool IsMultiplayerEntry(Button button)
        {
            if (button.gameObject.name == ItemName) return true;

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;

                // Exact match only, English and the Portuguese render — the
                // lobby screen's own title is "MULTIPLAYER - <name>", and a
                // substring test would claim that too.
                string t = text.text.Trim().ToUpperInvariant();
                if (t == "MULTIPLAYER" || t == "MULTIJOGADOR") return true;
            }
            return false;
        }
    }
}
