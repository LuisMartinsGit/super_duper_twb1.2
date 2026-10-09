// LoadGameMenuButton.cs
// Makes the main menu's Load Game entry open the Saved Games / Replays screen.

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Points the blue menu's Load Game entry at <see cref="SavedGamesScreen"/>.
    /// Same static scene-hook shape as SkirmishMenuButton: the entry lives in
    /// the Synty prefab instance, so the hook switches every authored call off
    /// and adds its own rather than relying on a scene edit.
    /// docs/Design/Replays_And_Saves.md
    /// </summary>
    public static class LoadGameMenuButton
    {
        private const string ItemName = "Menu_Item_LoadGame";

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
                    if (button == null || !IsLoadGameEntry(button)) continue;
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => SavedGamesScreen.Open());
                    button.interactable = true;
                    wired++;
                }
            }

            if (wired == 0)
                Debug.LogWarning("[LoadGameMenuButton] No Load Game entry found in the main menu — " +
                                 "saved games and replays have no way in.");
        }

        private static bool IsLoadGameEntry(Button button)
        {
            if (button.gameObject.name == ItemName) return true;
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;
                string t = text.text.Trim().ToUpperInvariant();
                if (t == "LOAD GAME" || t == "CARREGAR JOGO") return true;
            }
            return false;
        }
    }
}
