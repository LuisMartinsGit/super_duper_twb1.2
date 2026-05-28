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

        // Styling — specialty cached locals (jade panel chrome,
        // gold title, dim eyebrow + foot, gold tick-decorated rows).
        private GUIStyle _buttonStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _eyebrowStyle;
        private GUIStyle _medallionStyle;
        private GUIStyle _footStyle;
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

        // Jade-theme constants (mirror HudFrontend/src/components/themes.js
        // .jade). Used by the menu/lobby/loading IMGUI styles so the
        // pre-game UI matches the in-game pause-menu look.
        public static readonly Color JadeBase   = new Color(0.043f, 0.122f, 0.102f, 1f);  // #0b1f1a
        public static readonly Color JadeMid    = new Color(0.078f, 0.196f, 0.157f, 1f);  // #143228
        public static readonly Color JadeGem    = new Color(0.114f, 0.416f, 0.333f, 1f);  // #1d6a55
        public static readonly Color JadeGemHi  = new Color(0.247f, 0.749f, 0.604f, 1f);  // #3fbf9a
        public static readonly Color JadeText   = new Color(0.902f, 0.937f, 0.918f, 1f);  // #e6efea
        public static readonly Color JadeAccent = new Color(0.910f, 0.722f, 0.290f, 1f);  // #e8b84a

        private void DrawBackground()
        {
            if (_bgTexture == null) return;

            float screenW = Screen.width;
            float screenH = Screen.height;

            // Static background — fit to screen, no pan or zoom
            GUI.DrawTexture(new Rect(0, 0, screenW, screenH), _bgTexture, ScaleMode.ScaleAndCrop);

            // Jade-tinted dim overlay — mirrors the pause-menu modal
            // backdrop which uses hexAlpha(theme.gem, 0.38) over a
            // radial gem-tinted gradient. Keeps the southood image
            // readable underneath while pushing the entire pre-game UI
            // into the in-game jade colour space.
            GUI.color = new Color(JadeGem.r, JadeGem.g, JadeGem.b, 0.32f);
            GUI.DrawTexture(new Rect(0, 0, screenW, screenH), Texture2D.whiteTexture);
            // Vignette darkening at the edges — softens the band where
            // the southood image meets the wash.
            GUI.color = new Color(0f, 0.04f, 0.02f, 0.28f);
            GUI.DrawTexture(new Rect(0, 0, screenW, screenH), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MAIN MENU (borderless, centered buttons)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawMainMenu()
        {
            // Mirrors the in-game pause-menu modal
            // (HudFrontend/src/components/Menu.jsx → .hud-menu-modal):
            // dark jade panel chrome, gold border + corner accents,
            // green-gem medallion at the top of the chrome, eyebrow
            // line + gold "ARMISTICE"-style title, the menu rows, then
            // a foot caption. The southood + gem wash from
            // DrawBackground stays underneath so the panel reads as
            // floating above the same scene the lobby uses.
            const int rowCount = 7;
            float listH = (ButtonHeight + ButtonSpacing) * rowCount;
            float panelW = ButtonWidth + 60;
            float panelH = 30 /*eyebrow gap*/ + TitleHeight + 14 + listH + Padding * 3 + 28 /*foot*/;
            float panelX = (Screen.width - panelW) * 0.5f;
            float panelY = (Screen.height - panelH) * 0.5f;

            DrawJadePanel(new Rect(panelX, panelY, panelW, panelH));

            float topPad = Padding;
            float y = panelY + topPad;

            // Top medallion — a single gold diamond stands in for the
            // pause-menu's filigree medallion + green gem. Keeps the
            // visual cue without needing texture assets.
            GUI.Label(new Rect(panelX, y - 8, panelW, 20),
                "<color=#3fbf9a>◆</color>", _medallionStyle);
            y += 14;

            // Eyebrow line (smaller, dim) — matches the
            // .hud-menu-eyebrow "THE FIELD IS HELD" line above
            // "ARMISTICE".
            GUI.Label(new Rect(panelX, y, panelW, 18),
                "ENTER THE BORDER", _eyebrowStyle);
            y += 22;

            // Title — gold, centered, no letter-spacing (IMGUI doesn't
            // do CSS letter-spacing cleanly; the in-game version uses
            // Cinzel which already has built-in spacing). Plain
            // uppercase keeps the panel from forcing a wrap.
            GUI.Label(new Rect(panelX, y, panelW, TitleHeight),
                "THE WANING BORDER", _titleStyle);
            y += TitleHeight - 6;

            // Horizontal rule — gradient line, same colour as the
            // pause menu's `linear-gradient(transparent, inlay, transparent)`.
            DrawRule(new Rect(panelX + 24, y, panelW - 48, 1));
            y += 8;

            // Menu rows — centred horizontally inside the panel.
            float startX = panelX + (panelW - ButtonWidth) * 0.5f;

            if (DrawMenuButton(startX, ref y, "Skirmish", "Single-player vs AI"))
                _pendingState = MenuState.SkirmishLobby;
            if (DrawMenuButton(startX, ref y, "Multiplayer", "Lockstep network match"))
                _pendingState = MenuState.MultiplayerLobby;
            DrawMenuButton(startX, ref y, "Campaign", "Coming soon", enabled: false);
            DrawMenuButton(startX, ref y, "Load Game", "Coming soon", enabled: false);
            if (DrawMenuButton(startX, ref y, "Scenarios", "Stress-test & showcase rooms"))
                _pendingState = MenuState.Scenarios;
            if (DrawMenuButton(startX, ref y, "Options", "Sound, video, controls"))
                _pendingState = MenuState.Options;
            if (DrawMenuButton(startX, ref y, "Exit", "Leave the keep"))
                ExitGame();

            // Foot line — mirrors the pause menu's "Esc to dismiss ·
            // Autosave at every dawn" caption.
            GUI.Label(new Rect(panelX, panelY + panelH - 24, panelW, 18),
                "Hold fast · The border still wanes", _footStyle);
        }

        // Solid dark-jade panel with a thin gold border and four small
        // gold corner accents. Cheaper to draw than real filigree but
        // reads as the same family of UI chrome.
        private void DrawJadePanel(Rect r)
        {
            // Body — slightly more opaque than the in-game modal so the
            // text inside stays readable on top of the southood wash.
            var bodyTex = GetOrMakeSolid(ref _panelBodyTex,
                new Color(JadeBase.r, JadeBase.g, JadeBase.b, 0.92f));
            GUI.color = Color.white;
            GUI.DrawTexture(r, bodyTex);

            // Highlight at the top edge — a single brighter strip so
            // the panel has a hint of light coming from above.
            var topTex = GetOrMakeSolid(ref _panelTopTex,
                new Color(JadeMid.r, JadeMid.g, JadeMid.b, 0.55f));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 32f), topTex);

            // Border — thin gold frame around the whole panel.
            var borderTex = GetOrMakeSolid(ref _panelBorderTex, JadeAccent);
            GUI.DrawTexture(new Rect(r.x,            r.y,             r.width, 1f), borderTex);
            GUI.DrawTexture(new Rect(r.x,            r.yMax - 1,      r.width, 1f), borderTex);
            GUI.DrawTexture(new Rect(r.x,            r.y,             1f, r.height), borderTex);
            GUI.DrawTexture(new Rect(r.xMax - 1,     r.y,             1f, r.height), borderTex);

            // Corner accents — short bracket strokes inside each corner
            // (top-left, top-right, bottom-left, bottom-right) so the
            // panel has the same "four marked corners" rhythm as the
            // pause menu's filigree corners.
            const float armLen = 14f;
            const float inset  = 6f;
            // Top-left
            GUI.DrawTexture(new Rect(r.x + inset,         r.y + inset,         armLen, 1f), borderTex);
            GUI.DrawTexture(new Rect(r.x + inset,         r.y + inset,         1f, armLen), borderTex);
            // Top-right
            GUI.DrawTexture(new Rect(r.xMax - inset - armLen, r.y + inset,     armLen, 1f), borderTex);
            GUI.DrawTexture(new Rect(r.xMax - inset - 1,  r.y + inset,         1f, armLen), borderTex);
            // Bottom-left
            GUI.DrawTexture(new Rect(r.x + inset,         r.yMax - inset - 1,  armLen, 1f), borderTex);
            GUI.DrawTexture(new Rect(r.x + inset,         r.yMax - inset - armLen, 1f, armLen), borderTex);
            // Bottom-right
            GUI.DrawTexture(new Rect(r.xMax - inset - armLen, r.yMax - inset - 1, armLen, 1f), borderTex);
            GUI.DrawTexture(new Rect(r.xMax - inset - 1,  r.yMax - inset - armLen, 1f, armLen), borderTex);
            GUI.color = Color.white;
        }

        private void DrawRule(Rect r)
        {
            var tex = GetOrMakeSolid(ref _ruleTex,
                new Color(JadeAccent.r, JadeAccent.g, JadeAccent.b, 0.45f));
            GUI.DrawTexture(r, tex);
        }

        // Cached single-pixel solid textures so we don't reallocate per
        // frame. Slot is passed by ref so the field gets populated on
        // first call and reused for every subsequent OnGUI.
        private static Texture2D GetOrMakeSolid(ref Texture2D slot, Color c)
        {
            if (slot != null) return slot;
            slot = Styles.MakeSolid(c);
            return slot;
        }

        private static Texture2D _panelBodyTex;
        private static Texture2D _panelTopTex;
        private static Texture2D _panelBorderTex;
        private static Texture2D _ruleTex;

        // ═══════════════════════════════════════════════════════════════════════
        // SCENARIOS (borderless, centered)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawScenarios()
        {
            // Same panel chrome the main menu uses. The scroll view + a
            // single back-row footer live inside.
            float maxVisibleButtons = 8;
            float scrollAreaH = (ButtonHeight + ButtonSpacing) * maxVisibleButtons;
            float panelW = ButtonWidth + 60;
            float panelH = 30 + TitleHeight + 14 + scrollAreaH + ButtonHeight + Padding * 3 + 28;
            float panelX = (Screen.width - panelW) * 0.5f;
            float panelY = (Screen.height - panelH) * 0.5f;

            DrawJadePanel(new Rect(panelX, panelY, panelW, panelH));

            float y = panelY + Padding;
            GUI.Label(new Rect(panelX, y - 8, panelW, 20),
                "<color=#3fbf9a>◆</color>", _medallionStyle);
            y += 14;
            GUI.Label(new Rect(panelX, y, panelW, 18),
                "TRAINING GROUNDS", _eyebrowStyle);
            y += 22;
            GUI.Label(new Rect(panelX, y, panelW, TitleHeight),
                "SCENARIOS", _titleStyle);
            y += TitleHeight - 6;
            DrawRule(new Rect(panelX + 24, y, panelW - 48, 1));
            y += 8;

            float startX = panelX + (panelW - ButtonWidth) * 0.5f;

            // Scrollable area for scenario buttons
            var scrollRect = new Rect(startX, y, ButtonWidth, scrollAreaH);
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
                ("Crystal Curse Combat Test", ScenarioType.CurseCombatTest),
                ("Patrol Defense (6 Veilstingers vs Wave)", ScenarioType.PatrolDefense),
                ("Alanthor vs Crystal Horde (6 batt. vs 50)", ScenarioType.AlanthorVsCrystal),
            };

            float contentH = (ButtonHeight + ButtonSpacing) * scenarios.Length;
            var viewRect = new Rect(0, 0, ButtonWidth - 16, contentH);

            _scenarioScrollPos = GUI.BeginScrollView(scrollRect, _scenarioScrollPos, viewRect);

            float ry = 0;
            for (int i = 0; i < scenarios.Length; i++)
            {
                var btnRect = new Rect(0, ry, ButtonWidth - 16, ButtonHeight);
                // Long scenario labels — pre-build the rich-text row
                // (gold tick + label + chevron, no hint subtitle) since
                // the sentence form would overflow if we letter-spaced
                // it.
                string display = $"<color=#e8b84a>◆</color>   <b>{scenarios[i].label}</b>   <color=#e8b84a>▸</color>";
                if (GUI.Button(btnRect, display, _buttonStyle))
                    _pendingScenario = scenarios[i].type;
                ry += ButtonHeight + ButtonSpacing;
            }

            GUI.EndScrollView();

            // Back button below scroll area — uses the same pause-menu
            // row treatment as the main menu list.
            float backY = scrollRect.yMax + 8f;
            if (DrawMenuButton(startX, ref backY, "Back", "Return to main menu"))
                _pendingState = MenuState.MainMenu;

            // Foot caption
            GUI.Label(new Rect(panelX, panelY + panelH - 24, panelW, 18),
                "Stress the engine · find the edges", _footStyle);
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
            //   ◆  LABEL  ▸           (decorations + label both gold)
            //   <italic dim hint>
            // Mirrors the in-game pause-menu rows
            // (HudFrontend Menu.jsx .hud-menu-item-tick / -chevron):
            // gold diamond + chevron, gold uppercase label, italic
            // dim hint. Letter-spacing is dropped — IMGUI can't
            // emulate CSS letter-spacing without collapsing word
            // breaks (LOAD GAME → LOADGAME) so we just rely on
            // ToUpperInvariant + the natural Cinzel-ish weight of
            // the bold font instead.
            string upperLabel = label.ToUpperInvariant();
            string display;
            if (string.IsNullOrEmpty(hint))
            {
                display = $"<color=#e8b84a>◆</color>   <b>{upperLabel}</b>   <color=#e8b84a>▸</color>";
            }
            else
            {
                display =
                    $"<color=#e8b84a>◆</color>   <b>{upperLabel}</b>   <color=#e8b84a>▸</color>\n" +
                    $"<size=12><i><color=#cfd6d3aa>{hint}</color></i></size>";
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

            // Jade pause-menu button look. Idle row is text-only over
            // the southood + jade wash; hover paints a subtle jade-
            // green band; label colour shifts silver → bright jade on
            // hover, matching .hud-menu-item:hover .hud-menu-item-label.
            var btnHover  = Styles.MakeSolid(new Color(JadeGem.r, JadeGem.g, JadeGem.b, 0.22f));
            var btnActive = Styles.MakeSolid(new Color(JadeGemHi.r, JadeGemHi.g, JadeGemHi.b, 0.22f));

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
            _buttonStyle.normal.background = null;        // transparent — keep the southood + jade wash visible
            _buttonStyle.normal.textColor   = JadeText;   // silver/off-white, matches .hud-menu-item-label
            _buttonStyle.hover.background   = btnHover;
            _buttonStyle.hover.textColor    = JadeGemHi;  // bright jade on hover, matches in-game accent glow
            _buttonStyle.active.background  = btnActive;
            _buttonStyle.active.textColor   = JadeGemHi;
            _buttonStyle.focused.background = null;
            _buttonStyle.focused.textColor  = _buttonStyle.normal.textColor;

            // Title: gold "ARMISTICE"-style heading. The in-game
            // pause-menu's title is `theme.accent` (gold) with a faint
            // text-shadow glow — IMGUI can't do text-shadow, but gold
            // text on the dark-jade panel still reads as the same
            // family.
            _titleStyle = new GUIStyle(Styles.Header)
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                fontStyle = FontStyle.Bold,
            };
            _titleStyle.normal.textColor = JadeAccent;

            // Eyebrow — small dim caps line above the title, matches
            // .hud-menu-eyebrow.
            _eyebrowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
            _eyebrowStyle.normal.textColor = new Color(0.86f, 0.90f, 0.86f, 0.65f);

            // Medallion stub — a single coloured glyph centered above
            // the eyebrow. Replaces the pause menu's FiligreeMedallion
            // graphic without needing texture work.
            _medallionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
            _medallionStyle.normal.textColor = JadeGemHi;

            // Foot — matches .hud-menu-foot (Cormorant italic dim).
            _footStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
            _footStyle.normal.textColor = new Color(0.78f, 0.82f, 0.78f, 0.55f);

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
