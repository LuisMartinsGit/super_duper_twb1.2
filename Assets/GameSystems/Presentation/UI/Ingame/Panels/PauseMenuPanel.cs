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
//   Esc  →  menu open?          step back (save / confirm page → menu → closed)
//        →  placing a building / aiming a ground ability?   their own
//           handlers cancel; do nothing here
//        →  planning mode?      cancel it
//        →  culture menu open?  close it
//        →  attack-move / patrol armed, or units selected?  cancel / deselect
//        →  otherwise           open the menu
//
// THREE PAGES on one scrim (2026-10-16), only one shown at a time: the menu
// itself, a confirm page (Restart / Quit to Main Menu / Quit to Desktop) and
// the save page, where the player names the save. The confirm used to be a
// row ADDED under the menu with every button still showing above it, which
// read as two menus at once. Every Cancel is red (GameUIKit.StyleDanger).
// The top-left "Menu" button opens it too — it was authored in the GameUI
// staging scene but never built in a match.
//
// Pausing is Time.timeScale = 0. Every HUD binder already ticks on
// unscaledDeltaTime, so panels keep repainting while the simulation is
// frozen. In MULTIPLAYER the clock is NOT stopped — one player cannot freeze
// a lockstep match — the menu simply opens over a running game.

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class PauseMenuPanel : MonoBehaviour
    {
        private const float PanelWidth = 760f;
        private const float ButtonHeight = 92f;
        private const string MainMenuScene = "MainMenu";
        private const int SaveNameLimit = 40;
        private static readonly Vector2 MenuButtonSize = new Vector2(190f, 72f);
        private static readonly Vector2 MenuButtonMargin = new Vector2(24f, 24f);

        public static bool IsOpen { get; private set; }

        private RectTransform _root;
        private GameObject _menuPage, _confirmPage, _savePage;
        private TMP_Text _confirmLabel;
        private System.Action _confirmAction;
        private TMP_InputField _saveName;
        private TMP_Text _saveError;
        private GameObject _menuButton;

        /// <summary>Restored on close — never assume it was 1.</summary>
        private float _resumeTimeScale = 1f;

        /// <summary>"Save Game" — shown only when this match can be saved.</summary>
        private GameObject _saveButton;
        private TMP_Text _saveLabel;

        private void Awake()
        {
            IsOpen = false;
            BuildMenuButton();
            Build();
        }

        private void OnDestroy()
        {
            // A destroyed menu must not leave the game frozen (scene change
            // while paused, domain reload in the editor).
            // Unfreeze to the player's GAME SPEED, not to 1 — otherwise a
            // scene change or domain reload while paused silently resets it.
            if (IsOpen && Time.timeScale == 0f) GameSpeedControl.Apply();
            IsOpen = false;
        }

        // ── Construction ───────────────────────────────────────────────────

        /// <summary>The always-visible top-left "Menu" button.</summary>
        private void BuildMenuButton()
        {
            var rt = GameUIKit.Rect(transform, "MenuButton");
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(MenuButtonMargin.x, -MenuButtonMargin.y);
            rt.sizeDelta = MenuButtonSize;

            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            var text = GameUIKit.Text(rt, "label", Loc.T("Menu"), 30f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);
            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = Open;
            relay.OnEnter = () => { bg.color = GameUIKit.BarBlue * 0.5f; text.color = GameUIKit.Gold; };
            relay.OnExit = () => { bg.color = GameUIKit.ButtonBg; text.color = GameUIKit.TextMain; };
            UITooltip.Bind(bg.gameObject, Loc.T("Menu (Esc): pause, save, restart or quit."));
            _menuButton = rt.gameObject;
        }

        private void Build()
        {
            // Full-screen dimmer; also swallows clicks aimed at the map.
            _root = GameUIKit.Rect(transform, "PauseMenu");
            GameUIKit.Stretch(_root);
            var scrim = GameUIKit.Image(_root, "scrim", new Color(0f, 0f, 0f, 0.72f),
                raycast: true);
            GameUIKit.Stretch(scrim.rectTransform);

            BuildMenuPage();
            BuildConfirmPage();
            BuildSavePage();

            _root.gameObject.SetActive(false);
        }

        /// <summary>A centred, chrome-framed page that sizes to its content.</summary>
        private RectTransform Page(string name, string title)
        {
            var panel = GameUIKit.Rect(_root, name);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, 0f);
            GameUIKit.PanelChrome(panel);

            var stack = GameUIKit.VStack(panel, 36f, 16f);
            stack.childAlignment = TextAnchor.UpperCenter;
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = GameUIKit.Text(panel, "title", Loc.T(title), 52f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 8f;
            GameUIKit.FixHeight(t.gameObject, 70f);
            return panel;
        }

        private void BuildMenuPage()
        {
            var panel = Page("menu", "PAUSED");

            MakeButton(panel, "resume", "Resume", "Close this menu and carry on (Esc).",
                Close);
            _saveButton = MakeButton(panel, "save", "Save Game",
                "Name this save and write it. Load it again from Load Game on the main menu.",
                ShowSavePage);
            _saveLabel = _saveButton.GetComponentInChildren<TMP_Text>(true);
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

            _menuPage = panel.gameObject;
        }

        /// <summary>The confirm page for the three destructive entries — a
        /// misclick on "Quit to Desktop" should not end the session.</summary>
        private void BuildConfirmPage()
        {
            var panel = Page("confirm", "ARE YOU SURE?");

            _confirmLabel = GameUIKit.Text(panel, "question", "", 32f, GameUIKit.TextMain,
                TextAlignmentOptions.Center);
            GameUIKit.FixHeight(_confirmLabel.gameObject, 90f);

            var row = ButtonRow(panel);
            MakeSmall(row, "yes", "Confirm", GameUIKit.Gold, danger: false, () =>
            {
                var action = _confirmAction;
                _confirmAction = null;
                action?.Invoke();
            });
            MakeSmall(row, "no", "Cancel", GameUIKit.TextMain, danger: true, ShowMenuPage);

            _confirmPage = panel.gameObject;
        }

        /// <summary>The save page: the player names the save; the minimap is
        /// baked into it as the Load Game preview.</summary>
        private void BuildSavePage()
        {
            var panel = Page("savepage", "SAVE GAME");

            var hint = GameUIKit.Text(panel, "hint", Loc.T("Name this save:"), 28f, GameUIKit.TextDim,
                TextAlignmentOptions.Left, wrap: false);
            GameUIKit.FixHeight(hint.gameObject, 40f);

            _saveName = NameField(panel);

            _saveError = GameUIKit.Text(panel, "error", "", 24f, new Color(0.95f, 0.45f, 0.38f),
                TextAlignmentOptions.Center);
            GameUIKit.FixHeight(_saveError.gameObject, 34f);

            var row = ButtonRow(panel);
            MakeSmall(row, "save", "Save", GameUIKit.Gold, danger: false, SaveGame);
            MakeSmall(row, "cancel", "Cancel", GameUIKit.TextMain, danger: true, ShowMenuPage);

            _savePage = panel.gameObject;
        }

        private TMP_InputField NameField(RectTransform parent)
        {
            var rt = GameUIKit.Rect(parent, "name");
            GameUIKit.FixHeight(rt.gameObject, 84f);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.BarBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);

            var area = GameUIKit.Rect(rt, "Text Area");
            GameUIKit.Stretch(area);
            area.offsetMin = new Vector2(20f, 6f);
            area.offsetMax = new Vector2(-20f, -6f);
            area.gameObject.AddComponent<RectMask2D>();

            var text = GameUIKit.Text(area, "Text", "", 34f, GameUIKit.TextMain,
                TextAlignmentOptions.MidlineLeft, wrap: false);
            GameUIKit.Stretch(text.rectTransform);

            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.characterLimit = SaveNameLimit;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.targetGraphic = bg;
            input.onSubmit.AddListener(_ => SaveGame());
            input.onValueChanged.AddListener(_ => _saveError.text = "");
            return input;
        }

        private static RectTransform ButtonRow(RectTransform parent)
        {
            var row = GameUIKit.Rect(parent, "buttons");
            GameUIKit.FixHeight(row.gameObject, 80f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            return row;
        }

        private GameObject MakeButton(Transform parent, string name, string label, string tooltip,
            System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, ButtonHeight);

            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
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
            return rt.gameObject;
        }

        private static void MakeSmall(Transform parent, string name, string label, Color color,
            bool danger, System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            var text = GameUIKit.Text(rt, "label", Loc.T(label), 30f, color,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);
            UITooltip.Relay(bg.gameObject).OnLeftClick = click;
            if (danger) GameUIKit.StyleDanger(bg);
        }

        // ── Pages ──────────────────────────────────────────────────────────

        private void ShowPage(GameObject page)
        {
            _menuPage.SetActive(page == _menuPage);
            _confirmPage.SetActive(page == _confirmPage);
            _savePage.SetActive(page == _savePage);
        }

        private void ShowMenuPage()
        {
            _confirmAction = null;
            DeselectInput();
            ShowPage(_menuPage);
        }

        private void Confirm(string question, System.Action action)
        {
            _confirmAction = action;
            _confirmLabel.text = Loc.T(question);
            ShowPage(_confirmPage);
        }

        private void ShowSavePage()
        {
            _saveName.text = DefaultSaveName();
            _saveError.text = "";
            ShowPage(_savePage);
            _saveName.Select();
            _saveName.ActivateInputField();
        }

        private void DeselectInput()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _saveName != null && es.currentSelectedGameObject == _saveName.gameObject)
                es.SetSelectedGameObject(null);
        }

        // ── Escape cascade ─────────────────────────────────────────────────

        private void Update()
        {
            // The Menu button is part of the HUD: gone while the menu (or the
            // end-of-match screens) cover the screen.
            bool showButton = !IsOpen && !MusterRollsPanel.IsOpen;
            if (_menuButton.activeSelf != showButton) _menuButton.SetActive(showButton);

            if (!UnityEngine.Input.GetKeyDown(KeyCode.Escape)) return;

            if (IsOpen)
            {
                if (!_menuPage.activeSelf) ShowMenuPage();
                else Close();
                return;
            }

            // The post-game Muster Rolls screen sits over the victory panel;
            // Esc steps back out of it rather than opening this menu on top.
            if (MusterRollsPanel.IsOpen) { MusterRollsPanel.CloseIfOpen(); return; }

            // Modes that own Esc themselves — WorkerCommandPanel cancels
            // placement, GroundTargeting cancels the aim ring. Both run their
            // own key check this frame, so this must not also fire.
            if (WorkerCommandPanel.IsPlacingBuilding) return;
            if (GroundTargeting.IsActive || EntityTargeting.IsActive) return;

            if (PlanningModeOverlay.IsActive) { PlanningModeOverlay.Cancel(); return; }
            if (TheWaningBorder.Input.RTSInputManager.CancelModesOrSelection()) return;

            Open();
        }

        // ── Open / close ───────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            ShowMenuPage();
            // Raise the HOST too — the menu is spawned before most panels, so
            // without this the scrim would sit under half the HUD.
            transform.SetAsLastSibling();
            _root.transform.SetAsLastSibling();
            _root.gameObject.SetActive(true);

            // A save is a snapshot of the world plus the replay so far, so it
            // exists only where a replay does: a single-player lockstep match
            // that is being played (not watched, not decided).
            // docs/Design/Replays_And_Saves.md
            bool canSave = TheWaningBorder.Core.Save.SaveGameWriter.CanSave;
            _saveButton.SetActive(canSave);
            if (_saveLabel != null) _saveLabel.text = Loc.T("Save Game");

            // Lockstep peers cannot be frozen by one player's menu.
            if (!GameSettings.IsMultiplayer)
            {
                // Resume to the player's GAME SPEED, not to whatever happened
                // to be in timeScale. Capturing the live value is fine while
                // running, but a pause opened during another pause (or before
                // the first Apply) would latch 0 or 1 and quietly undo the
                // preference on close.
                _resumeTimeScale = Time.timeScale > 0f
                    ? Time.timeScale : GameSpeedControl.Current;
                Time.timeScale = 0f;
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            DeselectInput();
            _root.gameObject.SetActive(false);
            if (!GameSettings.IsMultiplayer)
                Time.timeScale = _resumeTimeScale > 0f
                    ? _resumeTimeScale : GameSpeedControl.Current;
        }

        // ── Actions ────────────────────────────────────────────────────────

        /// <summary>"Map — 12:34", what the name field starts with.</summary>
        private static string DefaultSaveName()
        {
            var ls = TheWaningBorder.Core.Multiplayer.LockstepServiceLocator.Instance;
            int tick = ls != null ? ls.CurrentTick : 0;
            float seconds = tick * TheWaningBorder.Core.Multiplayer.LockstepTiming.TickDuration;
            var entry = TheWaningBorder.Core.Maps.MapRegistry.GetEntry(GameSettings.SelectedMapScene);
            string map = entry.SceneName == GameSettings.SelectedMapScene && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName : GameSettings.SelectedMapScene;
            int min = (int)(seconds / 60f), sec = (int)seconds % 60;
            return $"{map} {min}-{sec:00}";
        }

        private void SaveGame()
        {
            if (!_savePage.activeSelf) return;   // onSubmit also fires on deselect
            var ls = TheWaningBorder.Core.Multiplayer.LockstepServiceLocator.Instance;
            if (ls == null || !ls.IsSimulationRunning) return;

            string label = _saveName.text.Trim();
            if (label.Length == 0) { _saveError.text = Loc.T("Give the save a name."); return; }

            byte[] preview = null;
            try { preview = MinimapPanelBinder.Active != null ? MinimapPanelBinder.Active.BakePreviewPng() : null; }
            catch (System.Exception e) { Debug.LogWarning("[PauseMenu] Minimap preview failed: " + e.Message); }

            if (TheWaningBorder.Core.Save.SaveGameWriter.Save(label, preview, out _, out string error))
            {
                if (_saveLabel != null) _saveLabel.text = Loc.T("Game Saved");
                TheWaningBorder.Core.SimSignals.Notify(Loc.T("Game saved") + ": " + label);
                ShowMenuPage();
            }
            else
            {
                _saveError.text = error;
                TheWaningBorder.Core.SimSignals.NotifyError(error);
            }
        }

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
            GameSpeedControl.Apply();
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
