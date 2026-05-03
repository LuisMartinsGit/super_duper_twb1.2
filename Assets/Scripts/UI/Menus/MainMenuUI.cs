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

        // Layout constants
        private const float ButtonWidth = 280f;
        private const float ButtonHeight = 42f;
        private const float ButtonSpacing = 8f;
        private const float TitleHeight = 60f;
        private const float Padding = 20f;
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
            float totalH = TitleHeight + (ButtonHeight + ButtonSpacing) * 6 + Padding * 2;
            float startX = (Screen.width - ButtonWidth) * 0.5f;
            float startY = (Screen.height - totalH) * 0.5f;

            // Title
            GUI.Label(new Rect(startX, startY, ButtonWidth, TitleHeight),
                "THE WANING BORDER", _titleStyle);

            float y = startY + TitleHeight + Padding;

            if (DrawMenuButton(startX, ref y, "Skirmish"))
                _pendingState = MenuState.SkirmishLobby;

            if (DrawMenuButton(startX, ref y, "Multiplayer"))
                _pendingState = MenuState.MultiplayerLobby;

            // Campaign — disabled
            GUI.enabled = false;
            DrawMenuButton(startX, ref y, "Campaign");
            GUI.enabled = true;

            if (DrawMenuButton(startX, ref y, "Scenarios"))
                _pendingState = MenuState.Scenarios;

            if (DrawMenuButton(startX, ref y, "Options"))
                _pendingState = MenuState.Options;

            if (DrawMenuButton(startX, ref y, "Exit"))
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
                if (GUI.Button(btnRect, scenarios[i].label, _buttonStyle))
                    _pendingScenario = scenarios[i].type;
                y += ButtonHeight + ButtonSpacing;
            }

            GUI.EndScrollView();

            // Back button below scroll area
            float backY = scrollRect.yMax + Padding;
            var backRect = new Rect(startX, backY, ButtonWidth, ButtonHeight);
            if (GUI.Button(backRect, "Back", _buttonStyle))
                _pendingState = MenuState.MainMenu;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        private bool DrawMenuButton(float x, ref float y, string label)
        {
            var rect = new Rect(x, y, ButtonWidth, ButtonHeight);
            y += ButtonHeight + ButtonSpacing;
            return GUI.Button(rect, label, _buttonStyle);
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            // Bold 16pt menu buttons with hover textures — specialty (no Styles match).
            // Colors sourced from Styles.HighlightColor; dark-navy bg variants are unique to this menu.
            var btnNormal = Styles.MakeSolid(new Color(0.06f, 0.08f, 0.16f, 0.75f));
            var btnHover = Styles.MakeSolid(new Color(0.12f, 0.14f, 0.24f, 0.85f));
            var btnActive = Styles.MakeSolid(new Color(0.16f, 0.18f, 0.3f, 0.9f));

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(10, 10, 8, 8)
            };
            _buttonStyle.normal.background = btnNormal;
            _buttonStyle.normal.textColor = Styles.HighlightColor;
            _buttonStyle.hover.background = btnHover;
            _buttonStyle.hover.textColor = new Color(1f, 0.85f, 0.4f);
            _buttonStyle.active.background = btnActive;
            _buttonStyle.active.textColor = new Color(1f, 0.9f, 0.5f);

            // Title: large 24pt golden text — derived from Styles.Header (which is 20pt gold).
            _titleStyle = new GUIStyle(Styles.Header)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleCenter
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
