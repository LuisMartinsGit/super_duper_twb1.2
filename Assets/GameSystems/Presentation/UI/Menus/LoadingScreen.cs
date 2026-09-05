using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Full-screen loading overlay shown during scene transitions.
    /// Persists across scene loads via DontDestroyOnLoad. Uses the same
    /// "southood" background image as the main menu, with a full-width
    /// jade progress bar pinned to the bottom and a cycling tip band
    /// directly above it.
    ///
    /// WHY THIS IS uGUI AND NOT IMGUI (2026-08-21)
    /// It used to draw in OnGUI, with a DefaultExecutionOrder and a
    /// GUI.depth of -1000 to force itself above the other IMGUI panels. That
    /// worked when the menus were IMGUI. They are not any more: the menus and
    /// the in-game HUD are authored uGUI, and a Screen Space Overlay canvas is
    /// composited AFTER all IMGUI, unconditionally — no depth or execution
    /// order can put OnGUI on top of it.
    ///
    /// So the overlay was being created, was holding timeScale at 0, was
    /// cycling its tips — and was invisible the whole time, behind the menu
    /// canvas the player was still looking at. "There is no loading screen
    /// when starting a skirmish" was exactly right, and the reason it looked
    /// like the game froze on the menu for a few seconds.
    ///
    /// It is now a canvas of its own at a sorting order nothing else uses, so
    /// it is above the menu, above the HUD, and above anything either of them
    /// grows later.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        private static LoadingScreen _instance;

        /// <summary>Above every other canvas in the game. The authored UI
        /// lives in the low hundreds and the runtime prompts at 5000.</summary>
        private const int SortOrder = 32000;

        // Visual config — jade palette matches the in-game pause menu
        // (HudFrontend Menu.jsx + .hud-menu-modal gem accents). Bar
        // lives at the screen bottom; the dim overlay over the
        // southood background uses the same gem-tinted wash that the
        // main menu does.
        // Original names kept (GoldFill / GoldEdge) to avoid touching
        // call sites; renaming would be a wider edit with no win.
        private static readonly Color GoldFill    = new(0.247f, 0.749f, 0.604f, 1f); // #3fbf9a — bright jade
        private static readonly Color GoldEdge    = new(0.6f,   0.95f,  0.80f,  1f); // brighter jade glint
        private static readonly Color BarTrack    = new(0.04f,  0.10f,  0.08f,  0.85f); // dark jade groove
        private static readonly Color JadeWash    = new(0.114f, 0.416f, 0.333f, 0.32f);
        private const float BarHeight = 14f;          // px, full-width at the bottom
        private const float TipBandHeight = 110f;     // px, sits directly above the bar
        private const float TipCycleSeconds = 6f;
        private const float TipFadeSeconds  = 0.55f;

        /// <summary>Reference height for the scaler. The IMGUI original sized
        /// everything in raw screen pixels against a 1080p-ish window, so
        /// matching height at 1080 reproduces that look exactly — and, unlike
        /// the original, keeps it legible on a 4K display instead of shrinking
        /// the tip text to twelve physical pixels.</summary>
        private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

        private float _alpha = 1f;
        private float _fadeSpeed = 1.5f;
        private bool _fadingOut;
        #pragma warning disable 414
        private bool _sceneLoaded;
        #pragma warning restore 414
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

        // Built widgets.
        private CanvasGroup _group;
        private RawImage _background;
        private TMP_Text _tipLabel;
        private TMP_Text _statusLabel;
        private RectTransform _barFill;
        private RectTransform _barEdge;
        private string _shownTip;
        private string _shownStatus;

        /// <summary>
        /// Show the loading screen and begin async scene load.
        /// </summary>
        public static void Show(string sceneName)
        {
            if (_instance != null) return;

            var go = new GameObject("LoadingScreen");
            _instance = go.AddComponent<LoadingScreen>();
            DontDestroyOnLoad(go);

            // Publish downward: lockstep's world-ready gate reads this rather
            // than reaching up into the UI for IsVisible. See
            // Core/PresentationState.
            TheWaningBorder.Core.PresentationState.LoadingOverlayVisible = true;

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

        /// <summary>
        /// True while ANY part of the loading screen is still on screen,
        /// including the fade-out. The simulation is held until this goes
        /// false: single-player through <c>Time.timeScale</c> below, and
        /// multiplayer through LockstepManager.IsWorldReady, which refuses to
        /// start tick 0 while this is true (timeScale cannot hold a lockstep
        /// match — the fixed-step driver pushes its own delta and ignores it).
        /// </summary>
        public static bool IsVisible => _instance != null;

        void Awake()
        {
            // Same background texture as MainMenuUI for visual continuity.
            _bgTexture = Resources.Load<Texture2D>("UI/southood");
            LoadTips();
            if (_tips.Count > 0)
                _tipIndex = Random.Range(0, _tips.Count);

            Build();
        }

        // ── Construction ────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 1f;   // match HEIGHT, like the menu canvas

            // A raycaster IS needed, even though nothing here is clickable:
            // CanvasGroup.blocksRaycasts only blocks what a raycaster on this
            // canvas would have hit, so without one every click sails through
            // to the menu still sitting underneath during the scene load.
            gameObject.AddComponent<GraphicRaycaster>();

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = true;   // eat clicks aimed at the menu behind

            var root = (RectTransform)transform;

            // ── Background ──────────────────────────────────────────────
            // Same southood image MainMenuUI uses, cropped to cover (the
            // IMGUI original used ScaleMode.ScaleAndCrop). Falls back to a
            // flat navy fill if the texture failed to load.
            var bg = Stretch("Background", root);
            if (_bgTexture != null)
            {
                _background = bg.gameObject.AddComponent<RawImage>();
                _background.texture = _bgTexture;
            }
            else
            {
                var flat = bg.gameObject.AddComponent<Image>();
                flat.color = new Color(0.02f, 0.02f, 0.06f, 1f);
            }

            // Jade-tinted dim overlay — matches the main menu's same
            // gem-tinted wash so the southood reads as the same scene
            // across menu → lobby → loading → in-game pause.
            Stretch("JadeWash", root).gameObject.AddComponent<Image>().color = JadeWash;
            // Slight black wash for legibility.
            Stretch("Darken", root).gameObject.AddComponent<Image>().color =
                new Color(0f, 0.04f, 0.02f, 0.22f);

            // ── Tip band (above bar) ────────────────────────────────────
            var tip = Child("Tip", root);
            tip.anchorMin = new Vector2(0f, 0f);
            tip.anchorMax = new Vector2(1f, 0f);
            tip.pivot = new Vector2(0.5f, 0f);
            tip.offsetMin = new Vector2(60f, BarHeight);
            tip.offsetMax = new Vector2(-60f, BarHeight + TipBandHeight);
            _tipLabel = Label(tip, 18f, FontStyles.Italic,
                              new Color(0.55f, 0.90f, 0.78f, 1f));

            // Status text underneath the tip — small, muted. Sits just
            // above the bar so the bar's % isn't visually crowded.
            var status = Child("Status", root);
            status.anchorMin = new Vector2(0f, 0f);
            status.anchorMax = new Vector2(1f, 0f);
            status.pivot = new Vector2(0.5f, 0f);
            status.offsetMin = new Vector2(0f, BarHeight + 4f);
            status.offsetMax = new Vector2(0f, BarHeight + 22f);
            _statusLabel = Label(status, 12f, FontStyles.Normal,
                                 new Color(0.78f, 0.85f, 0.82f, 0.85f));

            // ── Progress bar (full-width, jade, bottom) ─────────────────
            var track = Child("BarTrack", root);
            track.anchorMin = Vector2.zero;
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.offsetMin = Vector2.zero;
            track.offsetMax = new Vector2(0f, BarHeight);
            track.gameObject.AddComponent<Image>().color = BarTrack;

            // Inner inset for the fill so the bar reads as a recessed
            // channel rather than a flat strip painted over the screen.
            const float pad = 2f;

            _barFill = Child("BarFill", root);
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 0f);
            _barFill.pivot = new Vector2(0f, 0f);
            _barFill.anchoredPosition = new Vector2(pad, pad);
            _barFill.sizeDelta = new Vector2(0f, BarHeight - pad * 2f);
            _barFill.gameObject.AddComponent<Image>().color = GoldFill;

            // Bright edge line at the top of the fill — fakes specular.
            _barEdge = Child("BarEdge", root);
            _barEdge.anchorMin = Vector2.zero;
            _barEdge.anchorMax = new Vector2(0f, 0f);
            _barEdge.pivot = new Vector2(0f, 0f);
            _barEdge.anchoredPosition = new Vector2(pad, BarHeight - pad - 1.5f);
            _barEdge.sizeDelta = new Vector2(0f, 1.5f);
            _barEdge.gameObject.AddComponent<Image>().color = GoldEdge;
        }

        private static RectTransform Child(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static RectTransform Stretch(string name, RectTransform parent)
        {
            var rt = Child(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TMP_Text Label(RectTransform parent, float size,
                                      FontStyles style, Color color)
        {
            var rt = Stretch("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
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
            // The coroutine just waits for a few frames so Unity has had every
            // opportunity to composite the newly-built canvas and present the
            // overlay on screen. No SceneManager work, no settings, no
            // AddComponent — pure idle.
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
            if (op == null)
            {
                // Scene missing from Build Settings (ship-gate exclusion or a
                // stale name). Without this guard the next line NREd and
                // stranded the player on a frozen overlay — Update re-stamps
                // timeScale = 0 every frame, so nothing could ever move again
                // (2026-08-17, Scenario_BuildingShowcase).
                Debug.LogError($"[LoadingScreen] Scene '{sceneName}' could not be loaded " +
                               "(not in Build Settings) — returning to the main menu.");
                _statusText = "Scene could not be loaded";
                float wait = 0f;
                while (wait < 2.5f) { wait += Time.unscaledDeltaTime; yield return null; }
                SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Menu);
                _instance = null;
                Destroy(gameObject);   // OnDestroy restores timeScale
                yield break;
            }
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
            // HOLD THE SIM PAUSED while any of the overlay is on screen —
            // the match must not tick (AI, curse, income, camera-blind
            // opening seconds) before the player can actually see it.
            // Re-stamped every frame so nothing else can un-pause early.
            //
            // This covers single-player. It does NOT cover lockstep: the
            // fixed-step rate manager pushes its own TimeData and never reads
            // timeScale, so a multiplayer match is held by
            // LockstepManager.IsWorldReady refusing to start tick 0 while
            // IsVisible is true.
            Time.timeScale = 0f;

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
                // UNSCALED time — the fade must progress while the sim is
                // paused (scaled dt is 0 here) or the pause never lifts.
                _alpha -= Time.unscaledDeltaTime * _fadeSpeed;
                if (_alpha <= 0f)
                {
                    _instance = null;
                    TheWaningBorder.Core.PresentationState.LoadingOverlayVisible = false;
                    Destroy(gameObject);
                    return;
                }
            }

            Redraw();
        }

        /// <summary>Push state into the widgets. Cheap: the texts are only
        /// re-assigned when the string actually changes, because setting
        /// TMP_Text.text rebuilds its mesh whether or not it differs.</summary>
        private void Redraw()
        {
            if (_group != null) _group.alpha = Mathf.Clamp01(_alpha);

            // Cover-fit the background, the equivalent of the old
            // ScaleMode.ScaleAndCrop: fill the screen, crop the overflow,
            // never letterbox and never stretch.
            if (_background != null && _bgTexture != null && _bgTexture.height > 0)
            {
                float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                float texAspect = (float)_bgTexture.width / _bgTexture.height;
                if (screenAspect > texAspect)
                {
                    float h = texAspect / screenAspect;
                    _background.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
                }
                else
                {
                    float w = screenAspect / texAspect;
                    _background.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
                }
            }

            if (_tipLabel != null)
            {
                // Tips are stored in English (the JSON is the key source);
                // translated at render. Tip translations live in the tips
                // domain table.
                string tip = _tips.Count > 0 ? Loc.T(_tips[_tipIndex]) : string.Empty;
                if (tip != _shownTip) { _shownTip = tip; _tipLabel.text = tip; }
                var c = _tipLabel.color;
                _tipLabel.color = new Color(c.r, c.g, c.b, _tipFadeT);
            }

            if (_statusLabel != null)
            {
                // _statusText stays English in state; translate at render.
                string status = Loc.T(_statusText);
                if (status != _shownStatus) { _shownStatus = status; _statusLabel.text = status; }
            }

            // Bar fill width, in the scaler's reference space rather than
            // screen pixels — the canvas does the scaling.
            var root = (RectTransform)transform;
            const float pad = 2f;
            float full = Mathf.Max(0f, root.rect.width - pad * 2f);
            float w2 = full * Mathf.Clamp01(_progress);
            if (_barFill != null) _barFill.sizeDelta = new Vector2(w2, BarHeight - pad * 2f);
            if (_barEdge != null) _barEdge.sizeDelta = new Vector2(w2, 1.5f);
        }

        void OnDestroy()
        {
            // The pause lifts the moment the overlay is completely gone —
            // and on ANY destruction path, so a scene teardown can never
            // strand the game frozen.
            if (_instance == this) _instance = null;
            TheWaningBorder.Core.PresentationState.LoadingOverlayVisible = false;
            GameSpeedControl.Apply();
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
