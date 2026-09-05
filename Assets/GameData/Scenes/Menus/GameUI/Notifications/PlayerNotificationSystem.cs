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

        /// <summary>Seconds of fade at the end of that life.</summary>
        const float FadeDuration = 0.5f;

        /// <summary>Most pills on screen at once; the oldest is dropped past this.</summary>
        const int MaxVisible = 5;

        static readonly Color ErrorColor = new Color(1f, 0.35f, 0.35f);
        static readonly Color WarnColor = new Color(0.909f, 0.835f, 0.627f);

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

            if (_template == null)
                Debug.LogError("[Notifications] prefab has no 'PillTemplate' — nothing can be shown.");
            else
                _template.SetActive(false);
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

        void Add(string message, Color colour)
        {
            if (_template == null) return;

            // Duplicate suppression: the same message re-arriving refreshes the
            // pill already on screen instead of stacking a second copy.
            foreach (var existing in _live)
                if (existing.Message == message)
                {
                    existing.Remaining = DefaultDuration;
                    return;
                }

            while (_live.Count >= MaxVisible)
            {
                Release(_live[0]);
                _live.RemoveAt(0);
            }

            var pill = _pool.Count > 0 ? _pool.Pop() : Clone();
            pill.Message = message;
            pill.Remaining = DefaultDuration;
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
