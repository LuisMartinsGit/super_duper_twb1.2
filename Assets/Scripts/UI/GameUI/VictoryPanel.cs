// VictoryPanel.cs
// End-of-match screen. The old post-game UI was removed with the UI redesign
// (2026-07-17) and matches ended with only a 2.5s toast followed by a forced
// scene load 10 seconds later. This panel replaces that flow: a full-screen
// scrim with the outcome and a Return to Main Menu button, shown until the
// player chooses to leave.
//
// Spawned hidden by GameUIManager (code-built, after the pause menu so its
// scrim covers it); VictoryConditionSystem calls TryShow when a match ends
// and falls back to the old toast + timed return if the HUD stack is absent
// (e.g. a headless or observer configuration without GameUIManager).
// Location: Assets/Scripts/UI/GameUI/VictoryPanel.cs

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class VictoryPanel : MonoBehaviour
    {
        private const float PanelWidth = 820f;
        private const float ButtonHeight = 96f;

        public static VictoryPanel Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        private RectTransform _root;
        private TMP_Text _title;
        private TMP_Text _subtitle;

        private void Awake()
        {
            Instance = this;
            IsOpen = false;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            IsOpen = false;
        }

        /// <summary>
        /// Show the end-of-match screen. Returns false when no panel exists
        /// in the scene (caller keeps its non-HUD fallback flow).
        /// </summary>
        public static bool TryShow(string title, string subtitle, bool victory)
        {
            if (Instance == null) return false;
            Instance.Open(title, subtitle, victory);
            return true;
        }

        private void Open(string title, string subtitle, bool victory)
        {
            _title.text = title;
            _title.color = victory ? GameUIKit.Gold : new Color(0.94f, 0.36f, 0.32f);
            _subtitle.text = subtitle ?? string.Empty;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
            _root.gameObject.SetActive(true);
            IsOpen = true;
        }

        // ── Construction ────────────────────────────────────────────────

        private void Build()
        {
            _root = GameUIKit.Rect(transform, "VictoryScreen");
            GameUIKit.Stretch(_root);
            var scrim = GameUIKit.Image(_root, "scrim", new Color(0f, 0f, 0f, 0.78f),
                raycast: true);
            GameUIKit.Stretch(scrim.rectTransform);

            var panel = GameUIKit.Rect(_root, "panel");
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, 0f);
            GameUIKit.PanelChrome(panel);

            var stack = GameUIKit.VStack(panel, 40f, 20f);
            stack.childAlignment = TextAnchor.UpperCenter;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = GameUIKit.Text(panel, "title", Loc.T("VICTORY"), 72f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            _title.fontStyle = FontStyles.Bold;
            _title.characterSpacing = 10f;
            GameUIKit.FixHeight(_title.gameObject, 96f);

            _subtitle = GameUIKit.Text(panel, "subtitle", "", 30f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: true);
            GameUIKit.FixHeight(_subtitle.gameObject, 44f);

            MakeButton(panel, "mainmenu", Loc.T("Return to Main Menu"), ToMainMenu);

            _root.gameObject.SetActive(false);
        }

        private void MakeButton(Transform parent, string name, string label,
            System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, ButtonHeight);

            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var text = GameUIKit.Text(rt, "label", label, 34f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = click;
            relay.OnEnter = () => bg.color = GameUIKit.BarBlue * 0.5f;
            relay.OnExit = () => bg.color = GameUIKit.ButtonBg;
        }

        private void ToMainMenu()
        {
            IsOpen = false;
            // The pause menu may have frozen the clock before the match ended;
            // never carry a zero timescale into the menu scene.
            Time.timeScale = 1f;
            SceneManager.LoadScene(TheWaningBorder.Bootstrap.MainMenuBootstrap.MenuSceneName);
        }
    }
}
