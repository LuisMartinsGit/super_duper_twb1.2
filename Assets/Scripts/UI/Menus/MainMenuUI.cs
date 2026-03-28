// File: Assets/Scripts/UI/Menus/MainMenuUI.cs
// Central manager for the main menu system
// Features: panning background image, borderless transparent menu, golden-themed buttons

using TheWaningBorder.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        // Styling
        private GUIStyle _buttonStyle;
        private GUIStyle _disabledButtonStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _transparentWindow;
        private bool _stylesInitialized = false;

        // Background panning
        private Texture2D _bgTexture;
        private float _panOffset;
        private const float PanSpeed = 8f; // pixels per second

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

            _skirmishLobby = gameObject.AddComponent<SkirmishLobbyUI>();
            _skirmishLobby.enabled = false;

            _multiplayerLobby = gameObject.AddComponent<MultiplayerLobbyUI>();
            _multiplayerLobby.enabled = false;

            _optionsMenu = gameObject.AddComponent<OptionsMenuUI>();
            _optionsMenu.enabled = false;

            _skirmishLobby.OnBackPressed += () => SetState(MenuState.MainMenu);
            _multiplayerLobby.OnBackPressed += () => SetState(MenuState.MainMenu);
            _optionsMenu.OnBackPressed += () => SetState(MenuState.MainMenu);

            GenerateBackground();
        }

        void Update()
        {
            // Pan background
            _panOffset += PanSpeed * Time.deltaTime;

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

        // ═══════════════════════════════════════════════════════════════════════
        // BACKGROUND
        // ═══════════════════════════════════════════════════════════════════════

        private void GenerateBackground()
        {
            // Generate a procedural dark fantasy landscape texture
            int w = 512, h = 512;
            _bgTexture = new Texture2D(w, h, TextureFormat.RGB24, false);
            _bgTexture.wrapMode = TextureWrapMode.Repeat;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)w;
                    float ny = y / (float)h;

                    // Layered noise for a dark mountainous landscape feel
                    float n1 = Mathf.PerlinNoise(nx * 3f, ny * 3f);
                    float n2 = Mathf.PerlinNoise(nx * 7f + 100f, ny * 7f + 100f) * 0.5f;
                    float n3 = Mathf.PerlinNoise(nx * 15f + 200f, ny * 15f + 200f) * 0.25f;
                    float val = (n1 + n2 + n3) / 1.75f;

                    // Gradient: darker at top, slightly lighter at bottom horizon
                    float gradient = Mathf.Lerp(0.6f, 1.0f, ny);
                    val *= gradient;

                    // Dark navy/purple palette
                    float r = val * 0.08f + 0.02f;
                    float g = val * 0.06f + 0.03f;
                    float b = val * 0.15f + 0.06f;

                    // Add subtle golden highlights on peaks
                    if (val > 0.55f)
                    {
                        float highlight = (val - 0.55f) * 2f;
                        r += highlight * 0.12f;
                        g += highlight * 0.08f;
                        b += highlight * 0.02f;
                    }

                    _bgTexture.SetPixel(x, y, new Color(r, g, b));
                }
            }
            _bgTexture.Apply();
        }

        private void DrawBackground()
        {
            if (_bgTexture == null) return;

            // Tile the texture across the screen with slow horizontal pan
            float texAspect = 1f; // square texture
            float screenW = Screen.width;
            float screenH = Screen.height;

            // Scale texture to cover screen height, tile horizontally
            float drawH = screenH;
            float drawW = drawH * texAspect;
            float offset = _panOffset % drawW;

            // Draw enough tiles to cover screen + pan offset
            for (float x = -offset; x < screenW + drawW; x += drawW)
            {
                GUI.DrawTexture(new Rect(x, 0, drawW, drawH), _bgTexture, ScaleMode.StretchToFill);
            }

            // Dark overlay vignette
            var overlayTex = Texture2D.whiteTexture;
            GUI.color = new Color(0f, 0f, 0.05f, 0.3f);
            GUI.DrawTexture(new Rect(0, 0, screenW, screenH), overlayTex);
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
            float totalH = TitleHeight + (ButtonHeight + ButtonSpacing) * 7 + Padding * 2;
            float startX = (Screen.width - ButtonWidth) * 0.5f;
            float startY = (Screen.height - totalH) * 0.5f;

            GUI.Label(new Rect(startX, startY, ButtonWidth, TitleHeight),
                "SCENARIOS", _titleStyle);

            float y = startY + TitleHeight + Padding;

            if (DrawMenuButton(startX, ref y, "Large Melee Battle (6v6)"))
                _pendingScenario = ScenarioType.LargeMelee;

            if (DrawMenuButton(startX, ref y, "Large Ranged Battle (6v6)"))
                _pendingScenario = ScenarioType.LargeRanged;

            if (DrawMenuButton(startX, ref y, "Large Mixed Battle (6v6)"))
                _pendingScenario = ScenarioType.LargeMixed;

            if (DrawMenuButton(startX, ref y, "Healer Test"))
                _pendingScenario = ScenarioType.HealerTest;

            if (DrawMenuButton(startX, ref y, "Four-Way Cultures (4 armies)"))
                _pendingScenario = ScenarioType.FourWayCultures;

            y += ButtonSpacing; // Extra gap before Back

            if (DrawMenuButton(startX, ref y, "Back"))
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

            // Button: dark semi-transparent background with golden text
            var btnNormal = MakeTex(2, 2, new Color(0.06f, 0.08f, 0.16f, 0.75f));
            var btnHover = MakeTex(2, 2, new Color(0.12f, 0.14f, 0.24f, 0.85f));
            var btnActive = MakeTex(2, 2, new Color(0.16f, 0.18f, 0.3f, 0.9f));

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
            _buttonStyle.normal.textColor = new Color(0.83f, 0.66f, 0.26f);
            _buttonStyle.hover.background = btnHover;
            _buttonStyle.hover.textColor = new Color(1f, 0.85f, 0.4f);
            _buttonStyle.active.background = btnActive;
            _buttonStyle.active.textColor = new Color(1f, 0.9f, 0.5f);

            // Title: large golden text
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = new Color(0.83f, 0.66f, 0.26f);

            _stylesInitialized = true;
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
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
