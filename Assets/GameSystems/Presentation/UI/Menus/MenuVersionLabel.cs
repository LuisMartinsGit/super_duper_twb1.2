// MenuVersionLabel.cs
// Drives the main menu's version label from the real build version.

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Diagnostics;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Sets the menu's "Version" label from <see cref="Application.version"/>.
    ///
    /// The label used to be a hand-typed string baked into MainMenu.unity, and
    /// the project's real version (Player Settings > Bundle Version) is a
    /// separate field. Two places to edit means they drift — and the one that
    /// matters is the one nobody sees, because the match logs testers send back
    /// record <c>Application.version</c>, not the label. A log that says
    /// "0.1.0" when the tester was clearly on a later build is worse than
    /// useless.
    ///
    /// Now there is one source of truth: bump the Bundle Version and both the
    /// menu and every Summary.txt follow.
    ///
    /// Alongside it sits <see cref="BuildFingerprint"/>, an eight-character
    /// hash of the files on disk. The version answers "which release is this",
    /// the fingerprint answers "is this byte-for-byte the same build as
    /// yours" — which the version cannot, since nothing stops two different
    /// builds carrying the same hand-typed number.
    ///
    /// Same static scene-hook shape as ShipGateMenuTrim — the main menu is
    /// authored and wired in the editor, and injected controllers have
    /// clobbered that wiring before, so this only ever writes one string.
    /// </summary>
    public static class MenuVersionLabel
    {
        /// <summary>Name of the label object inside the Synty menu prefab.</summary>
        private const string LabelObjectName = "Version";

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

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(t.name, LabelObjectName,
                            System.StringComparison.OrdinalIgnoreCase)) continue;

                    // GetComponentInChildren, not GetComponent: in the Synty
                    // menu prefab "Version" is a layout container holding only
                    // a RectTransform and a CanvasGroup, and the TMP component
                    // sits on a nested-prefab child. Asking the container alone
                    // returned null, so this silently wrote nothing and the
                    // hand-typed placeholder survived into every build.
                    var label = t.GetComponentInChildren<TMP_Text>(true);
                    if (label == null) continue;

                    label.text = $"v{Application.version}  {BuildFingerprint.Short}";
                    return;
                }
            }
        }
    }
}
