// PauseMenuPanel.cs
// The in-game pause menu — the one the old UI removal left a hole where.
// RTSInputManager still carried the note "the final uGUI owns the pause menu"
// while no such menu existed, so Esc did nothing but drop the selection and
// there was no way out of a match short of Alt+F4.
//
// It is also the single owner of the Escape key. Running the cascade from here
// rather than from RTSInputManager matters: that manager stops handling
// hotkeys whenever the pointer is over a uGUI element, which is most of the
// screen with the HUD up and all of it with this menu open.
//
//   Esc  →  menu open?          close it
//        →  placing a building / aiming a ground ability?   their own
//           handlers cancel; do nothing here
//        →  planning mode?      cancel it
//        →  culture menu open?  close it
//        →  attack-move / patrol armed, or units selected?  cancel / deselect
//        →  otherwise           open the menu
//
// Pausing is Time.timeScale = 0. Every HUD binder already ticks on
// unscaledDeltaTime, so panels keep repainting while the simulation is
// frozen. In MULTIPLAYER the clock is NOT stopped — one player cannot freeze
// a lockstep match — the menu simply opens over a running game.
// Location: Assets/Scripts/UI/GameUI/PauseMenuPanel.cs

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class PauseMenuPanel : MonoBehaviour
    {
        private const float PanelWidth = 760f;
        private const float ButtonHeight = 92f;
        private const string MainMenuScene = "MainMenu";

        public static bool IsOpen { get; private set; }

        private RectTransform _root;
        private GameObject _confirmRow;
        private TMP_Text _confirmLabel;
        private System.Action _confirmAction;

        /// <summary>Restored on close — never assume it was 1.</summary>
        private float _resumeTimeScale = 1f;

        private void Awake()
        {
            IsOpen = false;
            Build();
        }

        private void OnDestroy()
        {
            // A destroyed menu must not leave the game frozen (scene change
            // while paused, domain reload in the editor).
            if (IsOpen && Time.timeScale == 0f) Time.timeScale = 1f;
            IsOpen = false;
        }

        // ── Construction ───────────────────────────────────────────────────

        private void Build()
        {
            // Full-screen dimmer; also swallows clicks aimed at the map.
            _root = GameUIKit.Rect(transform, "PauseMenu");
            GameUIKit.Stretch(_root);
            var scrim = GameUIKit.Image(_root, "scrim", new Color(0f, 0f, 0f, 0.72f),
                raycast: true);
            GameUIKit.Stretch(scrim.rectTransform);

            var panel = GameUIKit.Rect(_root, "panel");
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, 0f);
            GameUIKit.PanelChrome(panel);

            var stack = GameUIKit.VStack(panel, 36f, 16f);
            stack.childAlignment = TextAnchor.UpperCenter;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = GameUIKit.Text(panel, "title", Loc.T("PAUSED"), 52f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            GameUIKit.FixHeight(title.gameObject, 70f);

            MakeButton(panel, "resume", "Resume", "Close this menu and carry on (Esc).",
                Close);
            MakeButton(panel, "restart", "Restart Match",
                "Reload this map from the beginning. All progress in the current " +
                "match is lost.",
                () => Confirm("Restart the match? Current progress is lost.", Restart));
            MakeButton(panel, "mainmenu", "Quit to Main Menu",
                "Abandon the match and return to the main menu.",
                () => Confirm("Quit to the main menu? Current progress is lost.", ToMainMenu));
            MakeButton(panel, "quit", "Quit to Desktop",
                "Close the game.",
                () => Confirm("Quit to desktop?", QuitGame));

            BuildConfirmRow(panel);

            _root.gameObject.SetActive(false);
        }

        private void MakeButton(Transform parent, string name, string label, string tooltip,
            System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, ButtonHeight);

            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var text = GameUIKit.Text(rt, "label", Loc.T(label), 32f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = click;
            // Hover STYLING stays on the relay — it only recolours, so it
            // cannot reflow anything. The tooltip itself is polled.
            relay.OnEnter = () =>
            {
                bg.color = GameUIKit.BarBlue * 0.5f;
                text.color = GameUIKit.Gold;
            };
            relay.OnExit = () =>
            {
                bg.color = GameUIKit.ButtonBg;
                text.color = GameUIKit.TextMain;
            };
            UITooltip.Bind(bg.gameObject, Loc.T(tooltip));
        }

        /// <summary>Yes/no strip for the three destructive entries — a
        /// misclick on "Quit to Desktop" should not end the session.</summary>
        private void BuildConfirmRow(Transform parent)
        {
            var rt = GameUIKit.Rect(parent, "confirm");
            GameUIKit.FixHeight(rt.gameObject, 150f);
            var v = GameUIKit.VStack(rt, 0f, 10f);
            v.childAlignment = TextAnchor.UpperCenter;

            _confirmLabel = GameUIKit.Text(rt, "question", "", 28f, GameUIKit.TextMain,
                TextAlignmentOptions.Center);
            GameUIKit.FixHeight(_confirmLabel.gameObject, 60f);

            var row = GameUIKit.Rect(rt, "buttons");
            GameUIKit.FixHeight(row.gameObject, 72f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;

            MakeSmall(row, "yes", "Confirm", GameUIKit.Gold, () =>
            {
                var action = _confirmAction;
                _confirmAction = null;
                action?.Invoke();
            });
            MakeSmall(row, "no", "Cancel", GameUIKit.TextMain, () =>
            {
                _confirmAction = null;
                _confirmRow.SetActive(false);
            });

            _confirmRow = rt.gameObject;
            _confirmRow.SetActive(false);
        }

        private static void MakeSmall(Transform parent, string name, string label, Color color,
            System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var text = GameUIKit.Text(rt, "label", Loc.T(label), 28f, color,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);
            UITooltip.Relay(bg.gameObject).OnLeftClick = click;
        }

        private void Confirm(string question, System.Action action)
        {
            _confirmAction = action;
            _confirmLabel.text = Loc.T(question);
            _confirmRow.SetActive(true);
        }

        // ── Escape cascade ─────────────────────────────────────────────────

        private void Update()
        {
            if (!UnityEngine.Input.GetKeyDown(KeyCode.Escape)) return;

            if (IsOpen)
            {
                if (_confirmRow.activeSelf) { _confirmAction = null; _confirmRow.SetActive(false); }
                else Close();
                return;
            }

            // Modes that own Esc themselves — BuilderCommandPanel cancels
            // placement, GroundTargeting cancels the aim ring. Both run their
            // own key check this frame, so this must not also fire.
            if (BuilderCommandPanel.IsPlacingBuilding) return;
            if (GroundTargeting.IsActive) return;

            if (PlanningModeOverlay.IsActive) { PlanningModeOverlay.Cancel(); return; }
            if (TopChoiceBar.CloseCultureMenu()) return;
            if (TheWaningBorder.Input.RTSInputManager.CancelModesOrSelection()) return;

            Open();
        }

        // ── Open / close ───────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _confirmAction = null;
            _confirmRow.SetActive(false);
            // Raise the HOST too — the menu is spawned before most panels, so
            // without this the scrim would sit under half the HUD.
            transform.SetAsLastSibling();
            _root.transform.SetAsLastSibling();
            _root.gameObject.SetActive(true);

            // Lockstep peers cannot be frozen by one player's menu.
            if (!GameSettings.IsMultiplayer)
            {
                _resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _root.gameObject.SetActive(false);
            if (!GameSettings.IsMultiplayer) Time.timeScale = _resumeTimeScale;
        }

        // ── Actions ────────────────────────────────────────────────────────

        private void Restart()
        {
            Close();
            var scene = SceneManager.GetActiveScene().name;
            TheWaningBorder.UI.Menus.LoadingScreen.Show(scene);
        }

        private void ToMainMenu()
        {
            Close();
            // The lobby/loading path is menu-only; going back is a plain load.
            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuScene);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
