// ReplayControlsPanel.cs
// The playback bar shown while a replay is watched: clock, progress, pause,
// speed, skip ahead, restart, and whether the replay still matches the match
// it recorded. docs/Design/Replays_And_Saves.md
//
// Speed is Time.timeScale. LockstepManager paces a watched replay on
// Time.deltaTime, so the one value drives both the simulation's tick rate and
// every animation — the world simply plays faster or slower. This panel owns
// timeScale for the whole replay, except while the pause menu holds it at 0.
//
// Seeking forward is a fast-forward (ReplaySession.SeekTick); seeking
// BACKWARDS restarts the map and fast-forwards from the top, because the
// simulation only runs one way.

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Replay;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class ReplayControlsPanel : MonoBehaviour
    {
        private const float PanelWidth = 1500f;
        private const float RowHeight = 64f;

        static ReplayControlsPanelConfig Cfg => ReplayControlsPanelConfig.I;

        private RectTransform _root;
        private TMP_Text _clock;
        private TMP_Text _status;
        private TMP_Text _playLabel;
        private RectTransform _barFill;
        private RectTransform _barArea;
        private readonly System.Collections.Generic.List<(float speed, Image bg, TMP_Text text)> _speedButtons
            = new System.Collections.Generic.List<(float, Image, TMP_Text)>();

        private float _speedBeforePause = 1f;

        private void Awake() => Build();

        private void OnDestroy()
        {
            // Leaving the replay must not strand the menu at a replay speed.
            if (ReplaySession.Watching) Time.timeScale = 1f;
        }

        // ── Construction ───────────────────────────────────────────────────

        private void Build()
        {
            _root = GameUIKit.Rect(transform, "ReplayBar");
            _root.anchorMin = new Vector2(0.5f, 1f);
            _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -150f);
            _root.sizeDelta = new Vector2(PanelWidth, 0f);
            GameUIKit.PanelChrome(_root);
            var stack = GameUIKit.VStack(_root, 18f, 10f);
            stack.childAlignment = TextAnchor.UpperCenter;
            var fit = _root.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Row 1: title + clock + verification status.
            var head = Row(_root, "head", 52f);
            var title = GameUIKit.Text(head, "title", Loc.T("REPLAY"), 34f, GameUIKit.Gold,
                TextAlignmentOptions.Left, wrap: false);
            title.fontStyle = FontStyles.Bold;
            Flex(title.gameObject, 1f);
            _clock = GameUIKit.Text(head, "clock", "0:00 / 0:00", 32f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            Flex(_clock.gameObject, 1f);
            _status = GameUIKit.Text(head, "status", "", 26f, GameUIKit.TextDim,
                TextAlignmentOptions.Right, wrap: false);
            Flex(_status.gameObject, 1.4f);

            // Row 2: the progress bar — click to seek.
            _barArea = GameUIKit.Rect(_root, "bar");
            GameUIKit.FixHeight(_barArea.gameObject, 26f);
            var barBg = GameUIKit.Image(_barArea, "bg", GameUIKit.BarBg, raycast: true);
            GameUIKit.Stretch(barBg.rectTransform);
            var fill = GameUIKit.Image(_barArea, "fill", GameUIKit.BarGold);
            _barFill = fill.rectTransform;
            _barFill.anchorMin = new Vector2(0f, 0f);
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            var barRelay = UITooltip.Relay(barBg.gameObject);
            barRelay.OnLeftClick = SeekToPointer;
            UITooltip.Bind(barBg.gameObject, Loc.T("Click to jump to that moment. " +
                "Jumping back replays the match from the start."));

            // Row 3: transport.
            var row = Row(_root, "transport", RowHeight);
            Button(row, "restart", "↺", Loc.T("Play the replay again from the start."), 0.6f,
                () => SeekTo(0));
            _playLabel = Button(row, "play", "II", Loc.T("Pause or resume (Space)."), 0.6f,
                TogglePause).text;
            if (Cfg != null && Cfg.speeds != null)
            {
                foreach (float sp in Cfg.speeds)
                {
                    float speed = sp;
                    var b = Button(row, "speed" + speed, FormatSpeed(speed),
                        string.Format(Loc.T("Play at {0} speed."), FormatSpeed(speed)), 0.7f,
                        () => SetSpeed(speed));
                    _speedButtons.Add((speed, b.bg, b.text));
                }
            }
            float skip = Cfg != null ? Cfg.skipSeconds : 60f;
            Button(row, "skip", "+" + Mathf.RoundToInt(skip / 60f) + " " + Loc.T("min"),
                string.Format(Loc.T("Skip ahead {0} seconds."), Mathf.RoundToInt(skip)), 0.9f,
                () => SeekTo(ReplaySession.CurrentTick + Mathf.RoundToInt(skip * LockstepTiming.TicksPerSecond)));
        }

        private static RectTransform Row(Transform parent, string name, float height)
        {
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, height);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            return rt;
        }

        private static void Flex(GameObject go, float weight)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = weight;
        }

        private (Image bg, TMP_Text text) Button(Transform parent, string name, string label,
            string tooltip, float weight, System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            Flex(rt.gameObject, weight);
            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            var text = GameUIKit.Text(rt, "label", label, 28f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);
            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = click;
            UITooltip.Bind(bg.gameObject, tooltip);
            return (bg, text);
        }

        private static string FormatSpeed(float s)
            => (s < 1f ? s.ToString("0.##") : Mathf.RoundToInt(s).ToString()) + "×";

        // ── Actions ────────────────────────────────────────────────────────

        private void TogglePause()
        {
            if (ReplaySession.Speed > 0f)
            {
                _speedBeforePause = ReplaySession.Speed;
                ReplaySession.Speed = 0f;
            }
            else ReplaySession.Speed = _speedBeforePause > 0f ? _speedBeforePause : 1f;
        }

        private static void SetSpeed(float s) => ReplaySession.Speed = s;

        private void SeekToPointer()
        {
            var cam = _barArea.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera uiCam = cam != null && cam.renderMode != RenderMode.ScreenSpaceOverlay ? cam.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_barArea,
                    UnityEngine.Input.mousePosition, uiCam, out var local)) return;
            var r = _barArea.rect;
            float t = Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width));
            SeekTo(Mathf.RoundToInt(t * ReplaySession.StopTick));
        }

        private static void SeekTo(int tick)
        {
            if (!ReplaySession.Watching) return;
            tick = Mathf.Clamp(tick, 0, Mathf.Max(0, ReplaySession.StopTick - 1));
            if (tick > ReplaySession.CurrentTick)
            {
                ReplaySession.SeekTick = tick;
                return;
            }
            // Backwards: the simulation only runs forward, so start the match
            // again and fast-forward to the target.
            ReplaySession.SeekAfterRestart = tick > 0 ? tick : -1;
            TheWaningBorder.UI.Menus.LoadingScreen.Show(SceneManager.GetActiveScene().name);
        }

        // ── Per frame ──────────────────────────────────────────────────────

        private void Update()
        {
            bool show = ReplaySession.Watching;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            // The replay owns the clock — except while the pause menu holds it.
            if (!PauseMenuPanel.IsOpen && !TheWaningBorder.UI.Menus.LoadingScreen.IsVisible)
            {
                float want = ReplaySession.Ended ? 0f : Mathf.Max(0f, ReplaySession.Speed);
                if (!Mathf.Approximately(Time.timeScale, want)) Time.timeScale = want;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Space) && !PauseMenuPanel.IsOpen
                && (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null))
                TogglePause();

            float tps = LockstepTiming.TicksPerSecond;
            _clock.text = Clock(ReplaySession.CurrentTick / tps) + " / " + Clock(ReplaySession.StopTick / tps);
            _barFill.anchorMax = new Vector2(ReplaySession.Progress, 1f);
            _playLabel.text = ReplaySession.Speed > 0f ? "II" : "▶";

            foreach (var (speed, bg, text) in _speedButtons)
            {
                bool on = Mathf.Approximately(speed, ReplaySession.Speed);
                text.color = on ? GameUIKit.Gold : GameUIKit.TextMain;
            }

            if (ReplaySession.SeekTick >= 0)
            {
                _status.text = Loc.T("Skipping ahead…");
                _status.color = GameUIKit.TextDim;
            }
            else if (ReplaySession.Diverged)
            {
                _status.text = string.Format(Loc.T("Out of sync from {0}"),
                    Clock(ReplaySession.DivergedTick / tps));
                _status.color = new Color(0.95f, 0.42f, 0.36f);
            }
            else if (ReplaySession.Ended)
            {
                _status.text = Loc.T("End of replay");
                _status.color = GameUIKit.Gold;
            }
            else
            {
                _status.text = ReplaySession.HashesVerified > 0
                    ? string.Format(Loc.T("In sync ({0} checks)"), ReplaySession.HashesVerified)
                    : "";
                _status.color = GameUIKit.TextDim;
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }
}
