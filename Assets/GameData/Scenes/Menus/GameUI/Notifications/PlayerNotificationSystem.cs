// PlayerNotificationSystem.cs
// Floating notification pills at the top-centre of the screen.
//
// Was IMGUI: an OnGUI stack drawing its own pill background texture and border,
// with a cached GUIStyle set "to match the existing Dark Navy + Golden theme".
// It is now an AUTHORED prefab (NotificationStack.prefab, beside this file) —
// the pills are real uGUI objects cloned from a template in it, so they use the
// same sprites and font as every other panel.
//
// The class NAME, NAMESPACE and static API are unchanged on purpose: Notify and
// NotifyError are called from 57 places.
//
// NO PREFAB -> CODE-BUILT (2026-10-11). NotificationStack.prefab was only ever
// produced by an editor menu tool (NotificationStackPrefabBuilder) that was
// never run, so GameUICatalog.notificationStack stayed empty, GameUIManager
// logged "no toasts" and EVERY notice the game posted was dropped - the
// player never saw a single message. With no authored template the stack now
// builds its own (BuildCodeStack): top-centre, Synty pill chrome through
// GameUIKit, and GameUIManager spawns it whenever the catalog has no prefab.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Queues player feedback and shows it as pills cloned from the prefab's
    /// template. Call <see cref="Notify"/> or <see cref="NotifyError"/> anywhere.
    /// </summary>
    public sealed class PlayerNotificationSystem : MonoBehaviour
    {
        #region Tunables

        /// <summary>How long a notification stays up.</summary>
        const float DefaultDuration = 2.5f;

        /// <summary>How long an announcement (a match-level event) stays up.</summary>
        const float AnnounceDuration = 6f;

        /// <summary>Seconds of fade at the end of that life.</summary>
        const float FadeDuration = 0.5f;

        /// <summary>Most pills on screen at once; the oldest is dropped past this.</summary>
        const int MaxVisible = 5;

        // The code-built stack (no authored prefab), in HUD canvas pixels
        // (HudCanvas.StagingReference, 3840 x 2160).
        const float CodeTopOffset = 230f;
        const float CodePillWidth = 1700f;
        const float CodePillHeight = 64f;
        const float CodeFontSize = 34f;
        const float CodeSpacing = 8f;

        static readonly Color ErrorColor = new Color(1f, 0.35f, 0.35f);
        static readonly Color WarnColor = new Color(0.909f, 0.835f, 0.627f);
        static readonly Color AnnounceColor = new Color(1f, 0.95f, 0.80f);

        #endregion

        #region Static API

        /// <summary>
        /// Raised for every notification (message, isError), whether or not a
        /// stack exists to draw it.
        /// </summary>
        public static event System.Action<string, bool> Emitted;

        /// <summary>Show a warning notification.</summary>
        public static void Notify(string message)
        {
            Emitted?.Invoke(message, false);
            if (_instance != null) _instance.Add(message, WarnColor);
        }

        /// <summary>Show an announcement: a match-level event (the Shardroot,
        /// territory), held longer than a notice.</summary>
        public static void Announce(string message)
        {
            Emitted?.Invoke(message, false);
            if (_instance != null) _instance.Add(message, AnnounceColor, AnnounceDuration);
        }

        /// <summary>Show an error notification.</summary>
        public static void NotifyError(string message)
        {
            Emitted?.Invoke(message, true);
            if (_instance != null) _instance.Add(message, ErrorColor);
        }

        #endregion

        sealed class Pill
        {
            public GameObject Root;
            public TMP_Text Label;
            public CanvasGroup Fade;
            public string Message;
            public float Remaining;
        }

        static PlayerNotificationSystem _instance;

        readonly List<Pill> _live = new();
        readonly Stack<Pill> _pool = new();
        RectTransform _stack;
        GameObject _template;

        #region Lifecycle

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;

            _stack = GetComponent<RectTransform>();

            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "PillTemplate") { _template = t.gameObject; break; }

            if (_template == null) BuildCodeStack();
            if (_template == null)
                Debug.LogError("[Notifications] no 'PillTemplate' — nothing can be shown.");
            else
                _template.SetActive(false);
        }

        /// <summary>
        /// The stack with no authored prefab: this RectTransform pinned
        /// top-centre, growing downward, and an inactive pill template framed
        /// with the Synty panel chrome (GameUIKit.PanelChrome).
        /// </summary>
        void BuildCodeStack()
        {
            if (_stack == null) return;
            _stack.anchorMin = _stack.anchorMax = new Vector2(0.5f, 1f);
            _stack.pivot = new Vector2(0.5f, 1f);
            _stack.anchoredPosition = new Vector2(0f, -CodeTopOffset);
            _stack.sizeDelta = new Vector2(CodePillWidth, 0f);

            var layout = gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = CodeSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var pill = GameUIKit.Rect(_stack, "PillTemplate");
            GameUIKit.PanelChrome(pill);
            pill.gameObject.AddComponent<CanvasGroup>();
            var le = pill.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = CodePillHeight;
            var label = GameUIKit.Text(pill, "Label", string.Empty, CodeFontSize, WarnColor,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);
            label.margin = new Vector4(24f, 0f, 24f, 0f);
            label.overflowMode = TextOverflowModes.Ellipsis;
            // A toast must never eat a click.
            GameUIKit.DisableRaycasts(pill.gameObject);
            _template = pill.gameObject;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                p.Remaining -= dt;

                if (p.Remaining <= 0f)
                {
                    Release(p);
                    _live.RemoveAt(i);
                    continue;
                }

                // Fade only over the last stretch of life, so a pill reads as
                // solid until it is genuinely on its way out.
                p.Fade.alpha = p.Remaining >= FadeDuration
                    ? 1f
                    : Mathf.Clamp01(p.Remaining / FadeDuration);
            }
        }

        #endregion

        #region Queue

        void Add(string message, Color colour) => Add(message, colour, DefaultDuration);

        void Add(string message, Color colour, float duration)
        {
            if (_template == null) return;

            // Duplicate suppression: the same message re-arriving refreshes the
            // pill already on screen instead of stacking a second copy.
            foreach (var existing in _live)
                if (existing.Message == message)
                {
                    existing.Remaining = Mathf.Max(existing.Remaining, duration);
                    return;
                }

            while (_live.Count >= MaxVisible)
            {
                Release(_live[0]);
                _live.RemoveAt(0);
            }

            var pill = _pool.Count > 0 ? _pool.Pop() : Clone();
            pill.Message = message;
            pill.Remaining = duration;
            pill.Label.text = message;
            pill.Label.color = colour;
            pill.Fade.alpha = 1f;
            pill.Root.SetActive(true);
            pill.Root.transform.SetAsLastSibling();

            _live.Add(pill);
        }

        Pill Clone()
        {
            var go = Instantiate(_template, _stack);
            go.name = "Pill";

            return new Pill
            {
                Root = go,
                Label = go.GetComponentInChildren<TMP_Text>(true),
                Fade = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>(),
            };
        }

        void Release(Pill p)
        {
            p.Root.SetActive(false);
            _pool.Push(p);
        }

        #endregion
    }
}
