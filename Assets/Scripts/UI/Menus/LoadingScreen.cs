// File: Assets/Scripts/UI/Menus/LoadingScreen.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Full-screen loading overlay shown during scene transitions.
    /// Persists across scene loads via DontDestroyOnLoad. Uses the same
    /// "southood" background image as the main menu, with a full-width
    /// golden progress bar pinned to the bottom and a cycling tip band
    /// directly above it.
    /// </summary>
    // Force OnGUI dispatch order LATE so the loading screen paints on TOP
    // of the lobby's IMGUI. Without this the lobby's full-screen menu can
    // draw after LoadingScreen.OnGUI and cover it for the first frame.
    [DefaultExecutionOrder(32760)]
    public class LoadingScreen : MonoBehaviour
    {
        private static LoadingScreen _instance;

        // Visual config — golden palette matches MainMenuUI's highlight tone
        // (Styles.HighlightColor). Bar lives at the screen bottom.
        private static readonly Color GoldFill    = new(0.92f, 0.74f, 0.30f, 1f);
        private static readonly Color GoldEdge    = new(1.00f, 0.86f, 0.45f, 1f);
        private static readonly Color BarTrack    = new(0.08f, 0.10f, 0.18f, 0.85f);
        private const float BarHeight = 14f;          // px, full-width at the bottom
        private const float TipBandHeight = 110f;     // px, sits directly above the bar
        private const float TipCycleSeconds = 6f;
        private const float TipFadeSeconds  = 0.55f;

        private float _alpha = 1f;
        private float _fadeSpeed = 1.5f;
        private bool _fadingOut;
        #pragma warning disable 414
        private bool _sceneLoaded;
        private string _statusText = "Loading...";
        private float _progress;

        // Background image — shared with MainMenuUI.
        private Texture2D _bgTexture;

        // Tip rotation state. Tips are loaded once from
        // Resources/UI/LoadingTips.json at startup. _tipIndex advances every
        // TipCycleSeconds. _tipFadeT is the cross-fade alpha (1 = current tip
        // fully visible, 0 = mid-swap).
        private List<string> _tips = new();
        private int _tipIndex;
        private float _tipTimer;
        private float _tipFadeT = 1f;

        // Cached IMGUI styles.
        private GUIStyle _statusStyle;
        private GUIStyle _tipStyle;
        private bool _stylesInit;

        /// <summary>
        /// Show the loading screen and begin async scene load.
        /// </summary>
        public static void Show(string sceneName)
        {
            if (_instance != null) return;

            var go = new GameObject("LoadingScreen");
            _instance = go.AddComponent<LoadingScreen>();
            DontDestroyOnLoad(go);

            _instance.StartCoroutine(_instance.LoadSceneRoutine(sceneName));
        }

        /// <summary>
        /// Called by game systems when initialization is complete.
        /// Triggers the fade-out.
        /// </summary>
        public static void NotifyReady()
        {
            if (_instance != null)
            {
                _instance._statusText = "Ready";
                _instance._fadingOut = true;
            }
        }

        /// <summary>
        /// Update the displayed status text. Bootstrap steps call this so the
        /// player sees "Loading world…", "Warming up prefabs…" etc. instead
        /// of the screen sitting at one label for the entire bootstrap.
        /// </summary>
        public static void SetStatus(string text)
        {
            if (_instance != null) _instance._statusText = text;
        }

        /// <summary>
        /// Optional progress fraction (0..1) for the status bar.
        /// </summary>
        public static void SetProgress(float t)
        {
            if (_instance != null) _instance._progress = Mathf.Clamp01(t);
        }

        /// <summary>
        /// True if a loading screen is currently active.
        /// </summary>
        public static bool IsActive => _instance != null && !_instance._fadingOut;

        void Awake()
        {
            // Same background texture as MainMenuUI for visual continuity.
            _bgTexture = Resources.Load<Texture2D>("UI/southood");
            LoadTips();
            if (_tips.Count > 0)
                _tipIndex = Random.Range(0, _tips.Count);
        }

        // Load the tip rotation from Resources/UI/LoadingTips.json. The file
        // is shipped as a TextAsset; we parse the lightweight DTO via
        // JsonUtility and copy out the string[]. Failures fall back to an
        // empty tip list — the loading screen still functions, just with
        // an empty band above the progress bar.
        void LoadTips()
        {
            try
            {
                var asset = Resources.Load<TextAsset>("UI/LoadingTips");
                if (asset == null || string.IsNullOrEmpty(asset.text)) return;
                var dto = JsonUtility.FromJson<LoadingTipsDto>(asset.text);
                if (dto?.tips != null)
                {
                    for (int i = 0; i < dto.tips.Length; i++)
                        if (!string.IsNullOrWhiteSpace(dto.tips[i]))
                            _tips.Add(dto.tips[i]);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[LoadingScreen] Could not load tips: {e.Message}");
            }
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            // STEP 1: Set the overlay to "Starting" 0 % and DO NOTHING ELSE.
            // The coroutine just waits for ~5 frames so Unity has had every
            // opportunity to dispatch this newly-added MonoBehaviour's OnGUI
            // and present the overlay on screen. No SceneManager work, no
            // settings, no AddComponent — pure idle.
            _statusText = "Starting";
            _progress = 0f;
            for (int i = 0; i < 5; i++) yield return null;

            // STEP 2: Once we KNOW the overlay is on screen, start the
            // async scene load. Hold activation so we get visible progress
            // during the disk read, then let activation fire and the staged
            // GameBootstrap coroutine takes over driving the bar (table:
            // 36 → 100 % across managers / world / terrain / spawn).
            _statusText = "Loading world…";
            _progress = 0.05f;
            yield return null;

            var op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = true;

            // Scene disk-read covers 5 → 35 % of the bar. After that the
            // GameBootstrap coroutine takes over driving 36 → 100 %, so we
            // must NOT slam to 100 % here — that would pop the bar to full
            // then snap back to 36 % the moment BootstrapCoroutine started.
            while (!op.isDone)
            {
                _progress = 0.05f + Mathf.Clamp01(op.progress / 0.9f) * 0.30f;
                _statusText = "Loading world…";
                yield return null;
            }

            _sceneLoaded = true;
            // Hand off — leave _statusText / _progress alone here. The
            // GameBootstrap.BootstrapCoroutine sets its own "Initialising
            // world…" / 36 % as its first frame, with no visible regression.
        }

        void Update()
        {
            // Tip cycling — only when at least 2 tips exist (so a single
            // tip doesn't fade in and out every TipCycleSeconds).
            if (_tips.Count >= 2)
            {
                _tipTimer += Time.unscaledDeltaTime;
                if (_tipTimer >= TipCycleSeconds)
                {
                    _tipTimer = 0f;
                    _tipFadeT = 0f;
                    _tipIndex = (_tipIndex + 1) % _tips.Count;
                }
                if (_tipFadeT < 1f)
                    _tipFadeT = Mathf.Clamp01(_tipFadeT + Time.unscaledDeltaTime / TipFadeSeconds);
            }

            if (_fadingOut)
            {
                _alpha -= Time.deltaTime * _fadeSpeed;
                if (_alpha <= 0f)
                {
                    _instance = null;
                    Destroy(gameObject);
                }
            }
        }

        void OnGUI()
        {
            if (_alpha <= 0f) return;

            // Force IMGUI to draw this OnGUI call ON TOP of every other.
            // Lower GUI.depth = drawn later = above other OnGUI panels.
            GUI.depth = -1000;

            Styles.Initialize();
            InitStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            // ── Background ──────────────────────────────────────────────
            // Same southood image MainMenuUI uses. Falls back to a flat
            // navy fill if the texture failed to load (Resources missing).
            if (_bgTexture != null)
            {
                GUI.color = new Color(1f, 1f, 1f, _alpha);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), _bgTexture, ScaleMode.ScaleAndCrop);
            }
            else
            {
                GUI.color = new Color(0.02f, 0.02f, 0.06f, _alpha);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
            }

            // Subtle navy overlay so the tip text and bar contrast against
            // the painted background — matches MainMenuUI's 0.35-alpha dim.
            GUI.color = new Color(0f, 0f, 0.02f, 0.35f * _alpha);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, _alpha);

            // ── Tip band (above bar) ────────────────────────────────────
            // Centred horizontally, anchored just above the progress bar.
            if (_tips.Count > 0)
            {
                float tipY = sh - BarHeight - TipBandHeight;
                var tipRect = new Rect(60f, tipY, sw - 120f, TipBandHeight);
                var c = _tipStyle.normal.textColor;
                _tipStyle.normal.textColor = new Color(c.r, c.g, c.b, _tipFadeT * _alpha);
                GUI.Label(tipRect, _tips[_tipIndex], _tipStyle);
                _tipStyle.normal.textColor = c;

                // Status text underneath the tip — small, muted. Sits just
                // above the bar so the bar's % isn't visually crowded.
                var statusRect = new Rect(0, sh - BarHeight - 22f, sw, 18f);
                GUI.Label(statusRect, _statusText, _statusStyle);
            }

            // ── Progress bar (full-width, golden, bottom) ───────────────
            DrawProgressBar(sw, sh);

            GUI.color = Color.white;
        }

        void DrawProgressBar(float sw, float sh)
        {
            float y = sh - BarHeight;

            // Track (dark navy band).
            GUI.color = new Color(BarTrack.r, BarTrack.g, BarTrack.b, BarTrack.a * _alpha);
            GUI.DrawTexture(new Rect(0, y, sw, BarHeight), Texture2D.whiteTexture);

            // Inner inset for the gold fill so the bar reads as a recessed
            // channel rather than a flat strip painted over the screen.
            const float pad = 2f;
            float fillX = pad;
            float fillY = y + pad;
            float fillW = Mathf.Max(0f, (sw - pad * 2f) * Mathf.Clamp01(_progress));
            float fillH = BarHeight - pad * 2f;

            // Gold fill — two-tone (bottom darker, top brighter) so it
            // catches the eye even without specular highlights.
            GUI.color = new Color(GoldFill.r, GoldFill.g, GoldFill.b, _alpha);
            GUI.DrawTexture(new Rect(fillX, fillY, fillW, fillH), Texture2D.whiteTexture);

            // Bright edge line at the top of the fill — fakes specular.
            if (fillW > 0f)
            {
                GUI.color = new Color(GoldEdge.r, GoldEdge.g, GoldEdge.b, _alpha);
                GUI.DrawTexture(new Rect(fillX, fillY, fillW, 1.5f), Texture2D.whiteTexture);
            }
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            // Status — small, dim grey, centred.
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.78f, 0.78f, 0.80f, 0.85f) }
            };

            // Tip — larger, gold accent, centred, wraps on screen.
            _tipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = new Color(0.95f, 0.90f, 0.70f, 1f) }
            };

            _stylesInit = true;
        }

        // JSON DTO matching Resources/UI/LoadingTips.json. JsonUtility
        // needs concrete fields, not anonymous types — keep this minimal.
        [System.Serializable]
        private class LoadingTipsDto
        {
            public string[] tips;
        }
    }
}
