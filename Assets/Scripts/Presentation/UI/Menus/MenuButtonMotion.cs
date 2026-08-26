// MenuButtonMotion.cs
// Hover and press motion for menu buttons, added at runtime.
// Location: Assets/Scripts/UI/Menus/MenuButtonMotion.cs
//
// ADDS ONLY. Nothing in this file writes a sprite, a colour, a size, a layout
// value or a hierarchy - and nothing here runs in the editor, so no authored
// work can be overwritten by it. It attaches one component at runtime and
// animates localScale, which no layout group reads.
//
// This is deliberately NOT Synty's animator controllers. Those clips animate by
// PATH ("Content", "Content/Label_Button"), so using them means restructuring
// every button to match - moving the art under a new Content node - and setting
// the Selectable to an Animation transition, which throws away its colour
// states. That reshape is exactly the kind of edit that flattens hand styling.
// A pointer-driven scale gets the same feel and touches nothing.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Scales its own transform under the pointer: a little bigger on hover, a
    /// little smaller while held. Runs alongside whatever transition the
    /// Selectable already has, so an authored colour tint keeps working.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuButtonMotion : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public float HoverScale = 1.05f;
        public float PressScale = 0.97f;

        [Tooltip("Seconds to reach the target scale. 0 snaps.")]
        public float Seconds = 0.09f;

        private Selectable _selectable;
        private Vector3 _authored = Vector3.one;
        private float _current = 1f;
        private bool _hover, _press;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            // Multiply the authored scale rather than replacing it, so a button
            // someone deliberately sized in the Inspector keeps that size.
            _authored = transform.localScale;
        }

        private void OnDisable()
        {
            _hover = _press = false;
            _current = 1f;
            transform.localScale = _authored;
        }

        public void OnPointerEnter(PointerEventData _) => _hover = true;
        public void OnPointerExit(PointerEventData _) { _hover = false; _press = false; }
        public void OnPointerDown(PointerEventData _) => _press = true;
        public void OnPointerUp(PointerEventData _) => _press = false;

        private void Update()
        {
            bool live = _selectable == null || _selectable.interactable;
            float target = !live ? 1f : _press ? PressScale : _hover ? HoverScale : 1f;

            if (Mathf.Approximately(_current, target)) return;

            // Exponential smoothing, so the ease is the same at any frame rate.
            // A MoveTowards would need an absolute step, and the whole journey
            // here is about 0.05 - picking that step in scale units rather than
            // in time is how these end up either instant or sluggish.
            // Unscaled: menus animate with nothing simulating behind them.
            float k = Seconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-Time.unscaledDeltaTime / Seconds);
            _current = Mathf.Lerp(_current, target, k);
            if (Mathf.Abs(target - _current) < 0.001f) _current = target;

            transform.localScale = _authored * _current;
        }
    }

    /// <summary>
    /// Attaches <see cref="MenuButtonMotion"/> to the skirmish screen's buttons
    /// when that scene loads.
    ///
    /// Same static scene-hook shape as MenuQuitButton / MenuSettingsButton /
    /// SkirmishMenuButton: no editor pass to run, no scene edit, so re-running
    /// it cannot undo anything. A plain static class, not a MonoBehaviour, so
    /// it is free to share this file.
    ///
    /// Named buttons rather than every Selectable on the screen, on purpose:
    /// the roster rows are Buttons too, and eight of them growing under the
    /// pointer is noise rather than feedback. Add a name here to cover another
    /// button.
    /// </summary>
    internal static class SkirmishMenuMotion
    {
        private static readonly string[] Animated =
        {
            "BackButton",      // CANCEL
            "PrimaryButton",   // START
            "PrevMapButton",
            "NextMapButton",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != TheWaningBorder.Core.SceneNames.Skirmish) return;

            int wired = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
                {
                    if (selectable == null) continue;
                    if (System.Array.IndexOf(Animated, selectable.gameObject.name) < 0) continue;
                    if (selectable.GetComponent<MenuButtonMotion>() != null) continue;
                    selectable.gameObject.AddComponent<MenuButtonMotion>();
                    wired++;
                }
            }

            if (wired > 0)
                Debug.Log($"[SkirmishMenuMotion] Added hover motion to {wired} button(s).");
            else
                Debug.LogWarning("[SkirmishMenuMotion] No matching buttons on the skirmish " +
                                 "screen — the names in Animated may have changed.");
        }
    }
}
