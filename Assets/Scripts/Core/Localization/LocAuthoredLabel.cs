// LocAuthoredLabel.cs
// Runtime localization for labels whose English text is AUTHORED into a
// scene or prefab (MainMenu.unity, the GameUI panel prefabs) rather than
// set from code.

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Core.Localization
{
    /// <summary>
    /// Attached at runtime to every authored TMP label (see
    /// <see cref="LocAuthored"/>). Remembers the authored English string,
    /// renders it through Loc.T, and re-renders when the language changes.
    ///
    /// The overwrite guard matters: many authored labels are placeholders a
    /// binder replaces at runtime ("Archer", "999/999"). Once code has
    /// written something different from what this component last applied,
    /// the label belongs to that code — re-applying the authored string on
    /// a language switch would clobber live UI state, so the component goes
    /// dormant instead. Code-driven labels get their translation at their
    /// own call sites via Loc.T.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocAuthoredLabel : MonoBehaviour
    {
        private TMP_Text _label;
        private string _authored;
        private string _lastApplied;
        private bool _initialized;

        private void Awake()
        {
            _label = GetComponent<TMP_Text>();
            if (_label == null) { Destroy(this); return; }
            _authored = _label.text;
            _initialized = true;
            Apply();
        }

        private void OnEnable()
        {
            Loc.LanguageChanged += Apply;
            if (_initialized) Apply();
        }

        private void OnDisable()
        {
            Loc.LanguageChanged -= Apply;
        }

        private void Apply()
        {
            if (!_initialized || _label == null) return;
            if (string.IsNullOrEmpty(_authored)) return;

            // Dormant once a binder has taken the label over.
            if (_lastApplied != null && _label.text != _lastApplied) return;
            if (_lastApplied == null && _label.text != _authored) return;

            string wanted = Loc.T(_authored);
            _label.text = wanted;
            _lastApplied = wanted;
        }
    }

    /// <summary>
    /// Sweeps authored TMP labels. GameUIManager calls
    /// <see cref="Localize"/> on every authored panel it instantiates; the
    /// scene hook below covers scene-authored labels (the main menu).
    /// </summary>
    public static class LocAuthored
    {
        /// <summary>Attach the localizer to every TMP label under root, inactive included.</summary>
        public static void Localize(GameObject root)
        {
            if (root == null) return;
            var labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].GetComponent<LocAuthoredLabel>() == null)
                    labels[i].gameObject.AddComponent<LocAuthoredLabel>();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            var active = SceneManager.GetActiveScene();
            if (active.IsValid()) LocalizeScene(active);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => LocalizeScene(scene);

        private static void LocalizeScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) Localize(roots[i]);
        }
    }
}
