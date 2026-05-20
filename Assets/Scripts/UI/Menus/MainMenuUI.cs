// File: Assets/Scripts/UI/Menus/MainMenuUI.cs
// Central manager for the main menu system
// Features: panning background image, borderless transparent menu, golden-themed buttons

using TheWaningBorder.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Central manager for the main menu system.
    /// Handles navigation between: Main Menu, Skirmish Lobby, Multiplayer Lobby.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public enum MenuState
        {
            MainMenu,
            SkirmishLobby,
            MultiplayerLobby,
            Options,
            Scenarios
        }

        private MenuState _currentState = MenuState.MainMenu;
        private MenuState? _pendingState = null;
        private ScenarioType? _pendingScenario = null;

        // Sub-components
        private SkirmishLobbyUI _skirmishLobby;
        private MultiplayerLobbyUI _multiplayerLobby;
        private OptionsMenuUI _optionsMenu;

        // Styling — specialty cached locals (bold 16pt buttons + 24pt title; no Styles match)
        private GUIStyle _buttonStyle;
        private GUIStyle _titleStyle;
        private bool _stylesInitialized = false;

        // Background
        private Texture2D _bgTexture;

        // Scenario scroll
        private Vector2 _scenarioScrollPos;

        // Layout constants. Sized to host two lines per button (label +
        // italic hint) the way the in-game pause menu does
        // (HudFrontend/src/components/Menu.jsx → .hud-menu-item).
        private const float ButtonWidth = 360f;
        private const float ButtonHeight = 56f;
        private const float ButtonSpacing = 2f;
        private const float TitleHeight = 72f;
        private const float Padding = 24f;
        private const string GameSceneName = "Game";

        void Awake()
        {
            MenuBootstrap.EnsureMenuCamera();

            // Apply persisted settings on launch
            OptionsMenuUI.LoadAndApplySettings();

            _skirmishLobby = gameObject.AddComponent<SkirmishLobbyUI>();
            _skirmishLobby.enabled = false;

            _multiplayerLobby = gameObject.AddComponent<MultiplayerLobbyUI>();
            _multiplayerLobby.enabled = false;

            _optionsMenu = gameObject.AddComponent<OptionsMenuUI>();
            _optionsMenu.enabled = false;

            _skirmishLobby.OnBackPressed += () => SetState(MenuState.MainMenu);
            _multiplayerLobby.OnBackPressed += () => SetState(MenuState.MainMenu);
            _optionsMenu.OnBackPressed += () => SetState(MenuState.MainMenu);

            _bgTexture = Resources.Load<Texture2D>("UI/southood");
        }

        void Update()
        {
            // (pan animation removed)

            if (_pendingScenario.HasValue)
            {
                var scenario = _pendingScenario.Value;
                _pendingScenario = null;
                LaunchScenario(scenario);
                return;
            }
            if (_pendingState.HasValue)
            {
                var next = _pendingState.Value;
                _pendingState = null;
                SetState(next);
            }
        }

        void OnGUI()
        {
            Styles.Initialize();
            InitStyles();
            DrawBackground();

            if (_currentState == MenuState.MainMenu)
            {
                DrawMainMenu();
            }
            else if (_currentState == MenuState.Scenarios)
            {
                DrawScenarios();
            }
        }

        private void DrawBackground()
        {
            if (_bgTexture == null) return;

            float screenW = Screen.width;
            float screenH = Screen.height;

            // Static background — fit to screen, no pan or zoom
            GUI.DrawTexture(new Rect(0, 0, screenW, screenH), _bgTexture, ScaleMode.ScaleAndCrop);

            // Subtle dark overlay for text readability — navy-tinted 0.35 alpha,
            // intentionally NOT Styles.DrawDimOverlay (0.7 black would over-darken). See AD-2.
            GUI.color = new Color(0f, 0f, 0.02f, 0.35f);
            GUI.DrawTexture(new Rect(0, 0, screenW, screenH), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MAIN MENU (borderless, centered buttons)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawMainMenu()
        {
            // 7 rows: Skirmish, Multiplayer, Campaign, Load, Scenarios,
            // Options, Exit. Two of them are stubs (Campaign / Load).
            const int rowCount = 7;
            float totalH = TitleHeight + (ButtonHeight + ButtonSpacing) * rowCount + Padding * 2;
            float startX = (Screen.width - ButtonWidth) * 0.5f;
            float startY = (Screen.height - totalH) * 0.5f;

            // Title — uses the same spaced-uppercase treatment the
            // pause menu's "ARMISTICE / PAUSED" header has.
            GUI.Label(new Rect(startX, startY, ButtonWidth, TitleHeight),
                SpaceOut("THE WANING BORDER"), _titleStyle);

            float y = startY + TitleHeight + Padding;

            if (DrawMenuButton(startX, ref y, "Skirmish", "Single-player vs AI"))
                _pendingState = MenuState.SkirmishLobby;

            if (DrawMenuButton(startX, ref y, "Multiplayer", "Lockstep network match"))
                _pendingState = MenuState.MultiplayerLobby;

            // Campaign — not yet implemented.
            DrawMenuButton(startX, ref y, "Campaign", "Coming soon", enabled: false);

            // Load Game — depends on task-save-load-system-081. Visible
            // here so the player sees the slot exists; greyed out until
            // the save system ships.
            DrawMenuButton(startX, ref y, "Load Game", "Coming soon", enabled: false);

            if (DrawMenuButton(startX, ref y, "Scenarios", "Stress-test & showcase rooms"))
                _pendingState = MenuState.Scenarios;

            if (DrawMenuButton(startX, ref y, "Options", "Sound, video, controls"))
                _pendingState = MenuState.Options;

            if (DrawMenuButton(startX, ref y, "Exit", "Leave the keep"))
                ExitGame();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SCENARIOS (borderless, centered)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawScenarios()
        {
            float maxVisibleButtons = 8;
            float scrollAreaH = (ButtonHeight + ButtonSpacing) * maxVisibleButtons;
            float totalH = TitleHeight + Padding + scrollAreaH + Padding + ButtonHeight + Padding;
            float startX = (Screen.width - ButtonWidth) * 0.5f;
            float startY = (Screen.height - totalH) * 0.5f;

            // Title
            GUI.Label(new Rect(startX, startY, ButtonWidth, TitleHeight),
                "SCENARIOS", _titleStyle);

            // Scrollable area for scenario buttons
            var scrollRect = new Rect(startX, startY + TitleHeight + Padding, ButtonWidth, scrollAreaH);
            var scenarios = new (string label, ScenarioType type)[]
            {
                ("Large Melee Battle (6v6)", ScenarioType.LargeMelee),
                ("Large Ranged Battle (6v6)", ScenarioType.LargeRanged),
                ("Large Mixed Battle (6v6)", ScenarioType.LargeMixed),
                ("Healer Test", ScenarioType.HealerTest),
                ("Four-Way Cultures (4 armies)", ScenarioType.FourWayCultures),
                ("Full Army (Archers + Swords + Siege)", ScenarioType.FullArmy),
                ("Wall Siege (Walls vs Siege)", ScenarioType.WallSiege),
                ("Sect Showcase (12 Sect Abilities)", ScenarioType.SectShowcase),
                ("Building Showcase (every culture)", ScenarioType.BuildingShowcase),
            };

            float contentH = (ButtonHeight + ButtonSpacing) * scenarios.Length;
            var viewRect = new Rect(0, 0, ButtonWidth - 16, contentH);

            _scenarioScrollPos = GUI.BeginScrollView(scrollRect, _scenarioScrollPos, viewRect);

            float y = 0;
            for (int i = 0; i < scenarios.Length; i++)
            {
                var btnRect = new Rect(0, y, ButtonWidth - 16, ButtonHeight);
                // Long scenario labels — pre-build the rich-text row
                // (tick + label + chevron, no hint subtitle) since the
                // sentence form would overflow if we letter-spaced it.
                string display = $"<color=#d3a942>◆</color>   <b>{scenarios[i].label}</b>   <color=#d3a942>▸</color>";
                if (GUI.Button(btnRect, display, _buttonStyle))
                    _pendingScenario = scenarios[i].type;
                y += ButtonHeight + ButtonSpacing;
            }

            GUI.EndScrollView();

            // Back button below scroll area — uses the same pause-menu
            // row treatment as the main menu list.
            float backY = scrollRect.yMax + Padding;
            float backY_ref = backY;
            if (DrawMenuButton(startX, ref backY_ref, "Back", "Return to main menu"))
                _pendingState = MenuState.MainMenu;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        // Draw one menu row in the in-game pause-menu style: transparent
        // background, gold uppercase label with letter-spacing, italic
        // dimmed hint underneath, diamond tick on the left, chevron on
        // the right. `enabled=false` paints the whole row at low opacity
        // and disables clicks (matches the disabled Save/Load look in
        // HudFrontend/src/components/Menu.jsx).
        private bool DrawMenuButton(float x, ref float y, string label, string hint = null, bool enabled = true)
        {
            var rect = new Rect(x, y, ButtonWidth, ButtonHeight);
            y += ButtonHeight + ButtonSpacing;

            // Rich-text payload — two lines:
            //   <gold bold label>     ▸
            //   <dim italic hint>
            // The trailing chevron and leading diamond tick are inlined
            // as rich-text characters so a single GUI.Button hosts the
            // entire row (no separate widgets to align).
            string spacedLabel = SpaceOut(label.ToUpperInvariant());
            string display;
            if (string.IsNullOrEmpty(hint))
            {
                display = $"<color=#d3a942>◆</color>   {spacedLabel}   <color=#d3a942>▸</color>";
            }
            else
            {
                display =
                    $"<color=#d3a942>◆</color>   {spacedLabel}   <color=#d3a942>▸</color>\n" +
                    $"<size=12><i><color=#ffffffaa>{hint}</color></i></size>";
            }

            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && enabled;
            bool clicked = GUI.Button(rect, display, _buttonStyle);
            GUI.enabled = prevEnabled;
            return clicked && enabled;
        }

        // Fake CSS letter-spacing in IMGUI by inserting hair-thin spaces
        // between characters. Matches the Cinzel-uppercase look the
        // pause menu uses (.hud-menu-item-label letter-spacing: 0.16em).
        private static string SpaceOut(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length == 1) return s;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if (i + 1 < s.Length && s[i] != ' ' && s[i + 1] != ' ')
                    sb.Append(' ');
            }
            return sb.ToString();
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            // Match the in-game pause-menu button look (HudFrontend
            // Menu.jsx → .hud-menu-item). The row has no solid
            // background; hover paints a subtle white tint band. Gold
            // accent matches the in-game theme accent.
            var btnHover  = Styles.MakeSolid(new Color(1f, 1f, 1f, 0.06f));
            var btnActive = Styles.MakeSolid(new Color(1f, 1f, 1f, 0.10f));

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                richText = true,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(22, 22, 6, 8),
                wordWrap = false,
            };
            _buttonStyle.normal.background = null; // transparent — keep the menu background visible
            _buttonStyle.normal.textColor = Styles.HighlightColor;
            _buttonStyle.hover.background = btnHover;
            _buttonStyle.hover.textColor = new Color(1f, 0.92f, 0.55f);
            _buttonStyle.active.background = btnActive;
            _buttonStyle.active.textColor = new Color(1f, 0.95f, 0.65f);
            _buttonStyle.focused.background = null;
            _buttonStyle.focused.textColor = _buttonStyle.normal.textColor;

            // Title: larger spaced golden heading, mirrors the modal
            // "PAUSED" / "ARMISTICE" treatment in the pause menu.
            _titleStyle = new GUIStyle(Styles.Header)
            {
                fontSize = 32,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };

            _stylesInitialized = true;
        }

        private void LaunchScenario(ScenarioType scenario)
        {
            GameSettings.Mode = GameMode.Scenario;
            GameSettings.ActiveScenario = scenario;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.TotalPlayers = 2;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = false;
            LoadingScreen.Show(GameSceneName);
        }

        private void SetState(MenuState newState)
        {
            _currentState = newState;

            _skirmishLobby.enabled = (newState == MenuState.SkirmishLobby);
            _multiplayerLobby.enabled = (newState == MenuState.MultiplayerLobby);
            _optionsMenu.enabled = (newState == MenuState.Options);

            if (newState == MenuState.SkirmishLobby)
            {
                GameSettings.IsMultiplayer = false;
                GameSettings.NetworkRole = NetworkRole.None;
                LobbyConfig.SetupSinglePlayer(GameSettings.TotalPlayers);
            }
            else if (newState == MenuState.MultiplayerLobby)
            {
                GameSettings.IsMultiplayer = true;
                LobbyConfig.SetupMultiplayer(GameSettings.TotalPlayers);
            }
        }

        private void LaunchPathfindingTest()
        {
            GameSettings.Mode = GameMode.PathfindingTest;
            GameSettings.TotalPlayers = 2;
            GameSettings.MapHalfSize = 60;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.FogOfWarEnabled = false;
            LobbyConfig.SetupSinglePlayer(2);
            SceneManager.LoadScene("Game");
        }

        private void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>
    /// Helper to ensure menu camera exists.
    /// </summary>
    public static class MenuBootstrap
    {
        public static void EnsureMenuCamera()
        {
            if (Camera.main == null)
            {
                var camGo = new GameObject("MenuCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.backgroundColor = new Color(0.02f, 0.02f, 0.06f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                camGo.AddComponent<AudioListener>();
            }
        }
    }
}
