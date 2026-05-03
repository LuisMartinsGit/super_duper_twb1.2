// InGameMenuPanel.cs
// In-game menu panel with Resume, Keybinds, Surrender, Quit to Menu
// Location: Assets/Scripts/UI/HUD/InGameMenuPanel.cs

using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Modal in-game menu panel toggled by ESC or the Menu button in the resource bar.
    /// Pauses the game in singleplayer mode. Contains Resume, Keybinds, Surrender,
    /// and Quit to Menu buttons.
    /// </summary>
    public class InGameMenuPanel : MonoBehaviour
    {
        // ================================================================
        // PUBLIC STATE
        // ================================================================

        /// <summary>True when the menu panel is visible (blocks game input).</summary>
        public static bool IsOpen { get; private set; }

        // ================================================================
        // PRIVATE STATE
        // ================================================================

        private enum SubView { Main, Keybinds }
        private SubView _currentView = SubView.Main;
        private bool _showSurrenderConfirm;

        // Local cached styles. The screen-dim overlay is drawn via Styles.DrawDimOverlay()
        // (AD-1 in task-048 state.json — accepts a +0.05 alpha shift 0.65 -> 0.7), so
        // _overlayBg / _texOverlay are deleted. _panelBg, _titleStyle, _buttonStyle,
        // _keybindLabelStyle, _keybindKeyStyle, _keybindHeaderStyle are kept cached because
        // their specific layouts (no border on panel, custom padded button hover textures,
        // keybind two-column layout) don't map cleanly to Styles.cs members.
        private GUIStyle _panelBg;
        private GUIStyle _titleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _keybindLabelStyle;
        private GUIStyle _keybindKeyStyle;
        private GUIStyle _keybindHeaderStyle;
        private Texture2D _texPanel;
        private Texture2D _texButton;
        private Texture2D _texButtonHover;
        private bool _stylesBuilt;

        // Layout constants
        private const float PanelWidth = 340f;
        private const float PanelMinHeight = 320f;
        private const float ButtonWidth = 260f;
        private const float ButtonHeight = 38f;
        private const float ButtonSpacing = 10f;
        private const float TitleHeight = 40f;

        // Keybinds scroll
        private Vector2 _keybindsScroll;

        // ================================================================
        // PUBLIC API
        // ================================================================

        /// <summary>Toggle menu open/closed.</summary>
        public static void Toggle()
        {
            // Earlier missing braces meant Open() ran unconditionally — when
            // called with IsOpen=true, Close() set IsOpen=false then Open()
            // immediately re-opened. Toggle was effectively one-way. The
            // ResourceHUD "Menu" button (the only path that called Toggle
            // when the menu was open) couldn't actually close the menu.
            // (task-060 F-2)
            if (IsOpen)
                Close();
                Open();
        }

        /// <summary>Open the menu panel.</summary>
        public static void Open()
        {
            if (IsOpen) return;
            if (PostGameStatsUI.IsVisible) return; // Don't open over post-game stats

            IsOpen = true;

            // Pause in singleplayer
            if (!GameSettings.IsMultiplayer)
            {
                Time.timeScale = 0f;
            }
        }

        /// <summary>Close the menu panel and resume.</summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;

            // Resume time (only if post-game stats aren't showing)
            if (!PostGameStatsUI.IsVisible)
            {
                Time.timeScale = 1f;
            }

            // Reset sub-view state
            var instance = Object.FindFirstObjectByType<InGameMenuPanel>();
            if (instance != null)
            {
                instance._currentView = SubView.Main;
                instance._showSurrenderConfirm = false;
            }
        }

        // ================================================================
        // LIFECYCLE
        // ================================================================

        void Awake()
        {
            IsOpen = false;
        }

        void OnDestroy()
        {
            // Ensure time is restored if we're destroyed while open
            if (IsOpen)
            {
                IsOpen = false;
                Time.timeScale = 1f;
            }
        }

        // ================================================================
        // RENDERING
        // ================================================================

        void OnGUI()
        {
            if (!IsOpen) return;
            Styles.Initialize();
            if (!_stylesBuilt) BuildLocalStyles();

            // Semi-transparent dark overlay covering the whole screen.
            // Per AD-1 (task-048 state.json): migrated from inline (0,0,0,0.65) to
            // Styles.DrawDimOverlay() which uses DimOverlayColor (0,0,0,0.7) — the +0.05
            // alpha shift on a black overlay is imperceptible in playtest.
            GUI.color = Color.white;
            Styles.DrawDimOverlay(new Rect(0, 0, Screen.width, Screen.height));

            // Draw the appropriate view
            switch (_currentView)
            {
                case SubView.Main:
                    DrawMainMenu();
                    break;
                case SubView.Keybinds:
                    DrawKeybindsView();
                    break;
            }
        }

        // ================================================================
        // MAIN MENU VIEW
        // ================================================================

        private void DrawMainMenu()
        {
            float panelHeight = PanelMinHeight;
            if (_showSurrenderConfirm) panelHeight += 60f;

            float px = (Screen.width - PanelWidth) * 0.5f;
            float py = (Screen.height - panelHeight) * 0.5f;
            var panelRect = new Rect(px, py, PanelWidth, panelHeight);

            // Panel background
            GUI.Box(panelRect, "", _panelBg);

            // Title
            var titleRect = new Rect(px, py + 15f, PanelWidth, TitleHeight);
            GUI.Label(titleRect, "MENU", _titleStyle);

            // Pause indicator for singleplayer
            if (!GameSettings.IsMultiplayer)
            {
                var pauseStyle = new GUIStyle(_titleStyle)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Italic,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
                };
                GUI.Label(new Rect(px, py + 48f, PanelWidth, 20f), "Game Paused", pauseStyle);
            }

            float buttonY = py + 75f;
            float buttonX = px + (PanelWidth - ButtonWidth) * 0.5f;

            // Resume button
            if (GUI.Button(new Rect(buttonX, buttonY, ButtonWidth, ButtonHeight), "Resume", _buttonStyle))
            {
                Close();
            }
            buttonY += ButtonHeight + ButtonSpacing;

            // Keybinds button
            if (GUI.Button(new Rect(buttonX, buttonY, ButtonWidth, ButtonHeight), "Keybinds", _buttonStyle))
            {
                _currentView = SubView.Keybinds;
            }
            buttonY += ButtonHeight + ButtonSpacing;

            // Surrender button
            if (!_showSurrenderConfirm)
            {
                if (GUI.Button(new Rect(buttonX, buttonY, ButtonWidth, ButtonHeight), "Surrender", _buttonStyle))
                {
                    _showSurrenderConfirm = true;
                }
                buttonY += ButtonHeight + ButtonSpacing;
            }
            else
            {
                // Confirmation row
                var confirmLabelStyle = new GUIStyle(_titleStyle)
                {
                    fontSize = 13,
                    normal = { textColor = new Color(1f, 0.5f, 0.5f) }
                };
                GUI.Label(new Rect(buttonX, buttonY, ButtonWidth, 22f), "Are you sure?", confirmLabelStyle);
                buttonY += 24f;

                float halfBtn = (ButtonWidth - 10f) * 0.5f;
                if (GUI.Button(new Rect(buttonX, buttonY, halfBtn, ButtonHeight), "Yes, Surrender", _buttonStyle))
                {
                    _showSurrenderConfirm = false;
                    DoSurrender();
                    return;
                }
                if (GUI.Button(new Rect(buttonX + halfBtn + 10f, buttonY, halfBtn, ButtonHeight), "Cancel", _buttonStyle))
                {
                    _showSurrenderConfirm = false;
                }
                buttonY += ButtonHeight + ButtonSpacing;
            }

            // Quit to Menu button
            if (GUI.Button(new Rect(buttonX, buttonY, ButtonWidth, ButtonHeight), "Quit to Menu", _buttonStyle))
            {
                DoQuitToMenu();
            }
        }

        // ================================================================
        // KEYBINDS VIEW
        // ================================================================

        private void DrawKeybindsView()
        {
            float panelHeight = 480f;
            float px = (Screen.width - PanelWidth) * 0.5f;
            float py = (Screen.height - panelHeight) * 0.5f;
            var panelRect = new Rect(px, py, PanelWidth, panelHeight);

            // Panel background
            GUI.Box(panelRect, "", _panelBg);

            // Title
            var titleRect = new Rect(px, py + 15f, PanelWidth, TitleHeight);
            GUI.Label(titleRect, "KEYBINDS", _titleStyle);

            // Back button (top-left of panel)
            var backStyle = new GUIStyle(_buttonStyle) { fontSize = 12 };
            if (GUI.Button(new Rect(px + 10f, py + 18f, 60f, 28f), "< Back", backStyle))
            {
                _currentView = SubView.Main;
            }

            // Keybinds list (scrollable)
            float listY = py + 65f;
            float listHeight = panelHeight - 80f;
            var listRect = new Rect(px + 15f, listY, PanelWidth - 30f, listHeight);

            // Content height calculation
            float contentHeight = 520f; // enough for all keybinds
            var viewRect = new Rect(0, 0, PanelWidth - 50f, contentHeight);

            _keybindsScroll = GUI.BeginScrollView(listRect, _keybindsScroll, viewRect);

            float y = 5f;
            float col1 = 0f;        // Key column
            float col2 = 140f;      // Description column
            float rowH = 22f;

            // Selection
            DrawKeybindHeader(ref y, "Selection");
            DrawKeybindRow(ref y, col1, col2, rowH, "Left-click", "Select unit");
            DrawKeybindRow(ref y, col1, col2, rowH, "Double-click", "Select all of type on screen");
            DrawKeybindRow(ref y, col1, col2, rowH, "Ctrl+Dbl-click", "Select all of type (map)");
            DrawKeybindRow(ref y, col1, col2, rowH, "Left-drag", "Box select");
            DrawKeybindRow(ref y, col1, col2, rowH, "Shift+click", "Add to selection");

            y += 8f;

            // Commands
            DrawKeybindHeader(ref y, "Commands");
            DrawKeybindRow(ref y, col1, col2, rowH, "Right-click", "Move / Attack / Gather");
            DrawKeybindRow(ref y, col1, col2, rowH, "A + Right-click", "Attack-move");
            DrawKeybindRow(ref y, col1, col2, rowH, "P + Right-click", "Patrol");
            DrawKeybindRow(ref y, col1, col2, rowH, "S", "Stop");
            DrawKeybindRow(ref y, col1, col2, rowH, "H", "Hold position");

            y += 8f;

            // Control Groups
            DrawKeybindHeader(ref y, "Control Groups");
            DrawKeybindRow(ref y, col1, col2, rowH, "Ctrl+1-9", "Save control group");
            DrawKeybindRow(ref y, col1, col2, rowH, "1-9", "Recall group (2x: center cam)");
            DrawKeybindRow(ref y, col1, col2, rowH, "Shift+1-9", "Add to group");

            y += 8f;

            // General
            DrawKeybindHeader(ref y, "General");
            DrawKeybindRow(ref y, col1, col2, rowH, "ESC", "Open / close menu");

            GUI.EndScrollView();
        }

        private void DrawKeybindHeader(ref float y, string header)
        {
            GUI.Label(new Rect(0, y, 300f, 22f), header, _keybindHeaderStyle);
            y += 26f;
        }

        private void DrawKeybindRow(ref float y, float col1, float col2, float rowH,
                                      string key, string description)
        {
            GUI.Label(new Rect(col1, y, 135f, rowH), key, _keybindKeyStyle);
            GUI.Label(new Rect(col2, y, 160f, rowH), description, _keybindLabelStyle);
            y += rowH;
        }

        // ================================================================
        // ACTIONS
        // ================================================================

        private void DoSurrender()
        {
            // Restore time before transitioning
            Time.timeScale = 1f;
            IsOpen = false;

            // Route through VictoryConditionSystem for proper elimination tracking
            if (VictoryConditionSystem.Instance != null)
            {
                VictoryConditionSystem.Instance.Surrender();
                return;
            }

            // Fallback: show post-game stats directly
            if (GameStatsTracker.Instance != null)
                GameStatsTracker.Instance.EndGame();

            var statsUI = PostGameStatsUI.Instance;
            if (statsUI == null)
            {
                var go = new GameObject("PostGameStatsUI");
                statsUI = go.AddComponent<PostGameStatsUI>();
            }
            statsUI.Show();
        }

        private void DoQuitToMenu()
        {
            // Restore time before transitioning
            Time.timeScale = 1f;
            IsOpen = false;

            // Reset bootstrap state so a new game can start fresh
            GameBootstrap.Reset();

            // Destroy all DontDestroyOnLoad managers
            var managers = Object.FindFirstObjectByType<RuntimeManagers>();
            if (managers != null)
                Destroy(managers.gameObject);

            // Clean up ECS world
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                world.Dispose();
            }

            SceneManager.LoadScene("MainMenu");
        }

        // ================================================================
        // STYLES
        // ================================================================

        // Build local cached styles. Overlay drawn via Styles.DrawDimOverlay (AD-1) —
        // _overlayBg / _texOverlay / MakeTex deleted. All textures use Styles.MakeSolid.
        private void BuildLocalStyles()
        {
            // Dark navy panel background (matches resource bar theme). No clean Styles match
            // because Styles.PanelBox includes a baked-in golden border which would conflict
            // with the modal's borderless design.
            _texPanel = Styles.MakeSolid(new Color(Styles.PanelBgColor.r, Styles.PanelBgColor.g,
                                                    Styles.PanelBgColor.b, 0.96f));
            _panelBg = new GUIStyle
            {
                normal = { background = _texPanel },
                border = new RectOffset(4, 4, 4, 4)
            };

            // Button textures (idle / hover variants).
            _texButton = Styles.MakeSolid(new Color(0.10f, 0.12f, 0.28f, 0.9f));
            _texButtonHover = Styles.MakeSolid(new Color(0.15f, 0.18f, 0.38f, 0.95f));

            // Title style (golden, centered).
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor }
            };

            // Button style (golden text on dark background).
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor, background = _texButton },
                hover = { textColor = new Color(1f, 0.85f, 0.4f), background = _texButtonHover },
                active = { textColor = Color.white, background = _texButtonHover },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(8, 8, 6, 6)
            };

            // Keybind styles.
            _keybindKeyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor }
            };

            _keybindLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            _keybindHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.6f, 0.8f, 1f) }
            };

            _stylesBuilt = true;
        }
    }
}
