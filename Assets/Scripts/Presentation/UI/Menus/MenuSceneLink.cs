// MenuSceneLink.cs
// One menu screen jumping to another. Drop it on a GameObject in a menu scene,
// name the target scene, and wire a Button's onClick to Open() in the Inspector.
// Location: Assets/Scripts/UI/Menus/MenuSceneLink.cs

using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Loads another menu scene on demand.
    ///
    /// The menu screens used to be panels inside MainMenu.unity, opened and
    /// closed with SetActive on onClick entries authored in the Inspector. The
    /// Skirmish screen is its own scene now, so the blue menu's entry needs a
    /// call it can still make from the Inspector - a cross-scene object
    /// reference is not something a UnityEvent can hold.
    ///
    /// Deliberately a component with authored wiring rather than another static
    /// scene hook: the menu is authored in the editor (see MainMenuBootstrap),
    /// and a link you can see and follow in the Inspector beats one that only
    /// exists at runtime. MenuQuitButton and MenuSettingsButton are static hooks
    /// because the entries they fix carry NO authored onClick at all.
    ///
    /// A plain LoadScene, not LoadingScreen.Show: menu screens are cheap, and a
    /// loading screen between two menus would be a visible change of behaviour.
    /// </summary>
    public sealed class MenuSceneLink : MonoBehaviour
    {
        [Tooltip("Scene name as listed in Build Settings, e.g. \"SkirmishMenu\".")]
        public string SceneName;

        /// <summary>Wired to a Button's onClick in the Inspector.</summary>
        public void Open()
        {
            if (string.IsNullOrWhiteSpace(SceneName))
            {
                Debug.LogError($"[MenuSceneLink] '{name}' has no SceneName set; " +
                               "nothing to load.", this);
                return;
            }

            SceneManager.LoadScene(SceneName);
        }
    }
}
