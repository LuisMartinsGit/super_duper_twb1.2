// File: Assets/Scripts/UI/Menus/SkirmishLobbyUI.cs
// Single-player skirmish lobby with tabbed layout, observer option, and sandbox mode

using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using TheWaningBorder.Core.Config;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Lobby tab identifiers.
    /// </summary>
    public enum LobbyTab
    {
        PlayerSetup,
        MapSetup,
        GameSetup
    }

    /// <summary>
    /// Single-player skirmish lobby.
    /// Split into three tabs: Player Setup, Map Setup, Game Setup.
    /// Supports Observer role and Sandbox game mode.
    /// </summary>
    public class SkirmishLobbyUI : MonoBehaviour
    {
        public event Action OnBackPressed;

        private const string GameSceneName = "Game";

        // Window layout
        private Rect _windowRect = new Rect(40, 40, 580, 720);
        private Vector2 _slotsScrollPos;

        // Active tab
        private LobbyTab _activeTab = LobbyTab.PlayerSetup;

        // Map settings
        private SpawnLayout _layout = GameSettings.SpawnLayout;
        private TwoSidesPreset _twoSides = GameSettings.TwoSides;
        private int _spawnSeed = GameSettings.SpawnSeed;
        private bool _fogOfWar = GameSettings.FogOfWarEnabled;
        private int _mapHalfSize = GameSettings.MapHalfSize;

        // Game settings
        private bool _maxResources = GameSettings.MaxStartingResources;
        private bool _crystalCurse = GameSettings.CrystalCurseEnabled;
        private bool _sandbox = false;
        private bool _isObserver = false;

        // Sandbox stores pre-sandbox settings for restoration
        private int _preSandboxPlayerCount = 2;
        private bool _preSandboxFog = false;
        private bool _preSandboxMaxRes = false;

        // Error display
        private string _error;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _slotStyle;
        private GUIStyle _factionLabelStyle;
        private GUIStyle _colorBtnStyle;
        private GUIStyle _tabActiveStyle;
        private GUIStyle _tabInactiveStyle;
        private Texture2D _colorSwatchTex;
        private Texture2D _tabActiveTex;
        private Texture2D _tabInactiveTex;
        private bool _stylesInit = false;

        void OnEnable()
        {
            // Sync settings when entering lobby
            _layout = GameSettings.SpawnLayout;
            _twoSides = GameSettings.TwoSides;
            // Randomize seed each time the lobby opens so every game is different
            _spawnSeed = UnityEngine.Random.Range(1, 99999);
            _fogOfWar = GameSettings.FogOfWarEnabled;
            _mapHalfSize = Mathf.Clamp(GameSettings.MapHalfSize, 64, 512);

            // Reset modes
            _sandbox = false;
            _isObserver = false;
            _activeTab = LobbyTab.PlayerSetup;
            GameSettings.Mode = GameMode.FreeForAll;

            FactionColors.ResetToDefaults();
            LobbyConfig.SetupSinglePlayer(LobbyConfig.ActiveSlotCount);
        }

        void OnGUI()
        {
            InitStyles();
            _windowRect = GUI.Window(10002, _windowRect, DrawWindow, "Skirmish Setup");

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = new Color(1, 0.5f, 0.5f, 1);
                GUI.Box(new Rect(_windowRect.x, _windowRect.yMax + 8, _windowRect.width, 50), _error);
                GUI.color = Color.white;
            }
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                richText = true
            };

            _slotStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4)
            };

            _factionLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _colorBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };

            _colorSwatchTex = new Texture2D(1, 1);
            _colorSwatchTex.SetPixel(0, 0, Color.white);
            _colorSwatchTex.Apply();

            // Tab styles
            _tabActiveTex = MakeSolidTexture(new Color(0.83f, 0.66f, 0.26f, 0.35f));
            _tabInactiveTex = MakeSolidTexture(new Color(0.15f, 0.15f, 0.25f, 0.5f));
            _tabActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                normal = { background = _tabActiveTex, textColor = new Color(0.83f, 0.66f, 0.26f) },
                hover = { background = _tabActiveTex, textColor = new Color(0.93f, 0.76f, 0.36f) },
                active = { background = _tabActiveTex, textColor = new Color(0.93f, 0.76f, 0.36f) },
                padding = new RectOffset(12, 12, 6, 6)
            };

            _tabInactiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                normal = { background = _tabInactiveTex, textColor = new Color(0.7f, 0.7f, 0.7f) },
                hover = { background = _tabActiveTex, textColor = new Color(0.9f, 0.9f, 0.9f) },
                active = { background = _tabActiveTex, textColor = new Color(0.9f, 0.9f, 0.9f) },
                padding = new RectOffset(12, 12, 6, 6)
            };

            _stylesInit = true;
        }

        void OnDestroy()
        {
            if (_colorSwatchTex != null) Destroy(_colorSwatchTex);
            if (_tabActiveTex != null) Destroy(_tabActiveTex);
            if (_tabInactiveTex != null) Destroy(_tabInactiveTex);
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════
        // MAIN WINDOW
        // ═══════════════════════════════════════════════════════════════

        private void DrawWindow(int windowId)
        {
            // Tab bar
            DrawTabBar();

            GUILayout.Space(6);

            // Tab content
            switch (_activeTab)
            {
                case LobbyTab.PlayerSetup:
                    DrawPlayerSetupTab();
                    break;
                case LobbyTab.MapSetup:
                    DrawMapSetupTab();
                    break;
                case LobbyTab.GameSetup:
                    DrawGameSetupTab();
                    break;
            }

            GUILayout.FlexibleSpace();

            // Action buttons (always visible)
            DrawActionButtons();

            GUILayout.Space(10);

            GUI.DragWindow(new Rect(0, 0, 10000, 25));
        }

        private void DrawTabBar()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Player Setup",
                _activeTab == LobbyTab.PlayerSetup ? _tabActiveStyle : _tabInactiveStyle,
                GUILayout.Height(30)))
            {
                _activeTab = LobbyTab.PlayerSetup;
            }

            if (GUILayout.Button("Map Setup",
                _activeTab == LobbyTab.MapSetup ? _tabActiveStyle : _tabInactiveStyle,
                GUILayout.Height(30)))
            {
                _activeTab = LobbyTab.MapSetup;
            }

            if (GUILayout.Button("Game Setup",
                _activeTab == LobbyTab.GameSetup ? _tabActiveStyle : _tabInactiveStyle,
                GUILayout.Height(30)))
            {
                _activeTab = LobbyTab.GameSetup;
            }

            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYER SETUP TAB
        // ═══════════════════════════════════════════════════════════════

        private void DrawPlayerSetupTab()
        {
            // Number of players (disabled in sandbox)
            GUILayout.Label("<b>Number of Players</b>", _headerStyle);
            GUI.enabled = !_sandbox;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(" - ", GUILayout.Width(40)))
                SetPlayerCount(LobbyConfig.ActiveSlotCount - 1);
            GUILayout.Label(LobbyConfig.ActiveSlotCount.ToString(), GUILayout.Width(40));
            if (GUILayout.Button(" + ", GUILayout.Width(40)))
                SetPlayerCount(LobbyConfig.ActiveSlotCount + 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.Space(10);

            // Player slots
            GUILayout.Label("<b>Player Slots</b>", _headerStyle);

            if (_sandbox)
            {
                GUILayout.Label("  Sandbox mode: 1 human player, no opponents.", _headerStyle);
            }

            _slotsScrollPos = GUILayout.BeginScrollView(_slotsScrollPos, GUILayout.Height(320));

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                DrawPlayerSlot(i);
            }

            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        // MAP SETUP TAB
        // ═══════════════════════════════════════════════════════════════

        private void DrawMapSetupTab()
        {
            // Spawn layout
            GUILayout.Label("<b>Spawn Layout</b>", _headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_layout == SpawnLayout.Circle, " Circle", "Button"))
                _layout = SpawnLayout.Circle;
            if (GUILayout.Toggle(_layout == SpawnLayout.TwoSides, " Two Sides", "Button"))
                _layout = SpawnLayout.TwoSides;
            if (GUILayout.Toggle(_layout == SpawnLayout.Circle, " Free For All", "Button"))
                _layout = SpawnLayout.Circle;
            GUILayout.EndHorizontal();

            // Two sides preset
            if (_layout == SpawnLayout.TwoSides)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Preset:", GUILayout.Width(60));
                if (GUILayout.Toggle(_twoSides == TwoSidesPreset.LeftRight, " E/W", "Button"))
                    _twoSides = TwoSidesPreset.LeftRight;
                if (GUILayout.Toggle(_twoSides == TwoSidesPreset.UpDown, " N/S", "Button"))
                    _twoSides = TwoSidesPreset.UpDown;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            // Spawn seed
            GUILayout.Label("<b>Spawn Seed</b>", _headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Random", GUILayout.Width(80)))
                _spawnSeed = UnityEngine.Random.Range(1, 99999);
            var seedStr = GUILayout.TextField(_spawnSeed.ToString(), GUILayout.Width(100));
            if (int.TryParse(seedStr, out int newSeed))
                _spawnSeed = Mathf.Max(0, newSeed);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Map size
            GUILayout.Label("<b>Map Size</b> (total side = 2 x HalfSize)", _headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(" - ", GUILayout.Width(40)))
                _mapHalfSize = Mathf.Max(64, _mapHalfSize - 16);
            GUILayout.Label($"Half Size: {_mapHalfSize}  (Total = {_mapHalfSize * 2})", GUILayout.Width(240));
            if (GUILayout.Button(" + ", GUILayout.Width(40)))
                _mapHalfSize = Mathf.Min(512, _mapHalfSize + 16);
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════
        // GAME SETUP TAB
        // ═══════════════════════════════════════════════════════════════

        private void DrawGameSetupTab()
        {
            // Sandbox mode toggle
            GUILayout.Label("<b>Game Mode</b>", _headerStyle);
            bool newSandbox = GUILayout.Toggle(_sandbox, _sandbox
                ? "  Sandbox Mode (single player, no victory conditions)"
                : "  Standard Skirmish");

            if (newSandbox != _sandbox)
            {
                ApplySandboxToggle(newSandbox);
            }

            // Battalion test toggle
            bool isBatTest = GameSettings.Mode == GameMode.BattalionTest;
            bool newBatTest = GUILayout.Toggle(isBatTest, "  Battalion Test (spawns 2 test battalions)");
            if (newBatTest != isBatTest)
            {
                if (newBatTest)
                {
                    _sandbox = false;
                    GameSettings.Mode = GameMode.BattalionTest;
                }
                else
                {
                    GameSettings.Mode = GameMode.FreeForAll;
                }
            }

            GUILayout.Space(10);

            // Fog of war
            GUILayout.Label("<b>Fog of War</b>", _headerStyle);
            GUI.enabled = !_sandbox; // Sandbox forces fog off
            _fogOfWar = GUILayout.Toggle(_fogOfWar, _fogOfWar ? " Enabled" : " Disabled");
            if (_sandbox) _fogOfWar = false;
            GUI.enabled = true;

            GUILayout.Space(10);

            // Crystal Curse
            GUILayout.Label("<b>Crystal Curse</b>", _headerStyle);
            _crystalCurse = GUILayout.Toggle(_crystalCurse, _crystalCurse
                ? " Enabled (NPC curse faction spawns)"
                : " Disabled (no crystal threat)");

            GUILayout.Space(10);

            // Starting resources
            GUILayout.Label("<b>Starting Resources</b>", _headerStyle);
            GUI.enabled = !_sandbox; // Sandbox forces max resources
            _maxResources = GUILayout.Toggle(_maxResources, _maxResources
                ? " Max Resources (100k each)"
                : " Normal (400 Supplies, 150 Iron)");
            if (_sandbox) _maxResources = true;
            GUI.enabled = true;

            GUILayout.Space(10);

            // Victory conditions info
            GUILayout.Label("<b>Victory Conditions</b>", _headerStyle);
            if (_sandbox)
            {
                GUILayout.Label("  No victory conditions in Sandbox mode.");
                GUILayout.Label("  Build, explore, and experiment freely.");
            }
            else
            {
                GUILayout.Label("  Elimination: Destroy all enemy buildings to win.");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SANDBOX TOGGLE
        // ═══════════════════════════════════════════════════════════════

        private void ApplySandboxToggle(bool enable)
        {
            if (enable)
            {
                // Save current settings before sandbox override
                _preSandboxPlayerCount = LobbyConfig.ActiveSlotCount;
                _preSandboxFog = _fogOfWar;
                _preSandboxMaxRes = _maxResources;

                _sandbox = true;
                _isObserver = false; // Observer and Sandbox are mutually exclusive
                GameSettings.Mode = GameMode.Sandbox;

                // Force sandbox constraints
                _fogOfWar = false;
                _maxResources = true;
                SetPlayerCount(1);
            }
            else
            {
                _sandbox = false;
                GameSettings.Mode = GameMode.FreeForAll;

                // Restore previous settings
                _fogOfWar = _preSandboxFog;
                _maxResources = _preSandboxMaxRes;
                SetPlayerCount(Mathf.Max(2, _preSandboxPlayerCount));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ACTION BUTTONS (always visible)
        // ═══════════════════════════════════════════════════════════════

        private void DrawActionButtons()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Back", GUILayout.Height(36), GUILayout.Width(100)))
            {
                OnBackPressed?.Invoke();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Start Game", GUILayout.Height(36), GUILayout.Width(150)))
            {
                StartGame();
            }

            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYER SLOT RENDERING
        // ═══════════════════════════════════════════════════════════════

        private void DrawPlayerSlot(int index)
        {
            var slot = LobbyConfig.Slots[index];

            GUILayout.BeginHorizontal(_slotStyle);

            if (index == 0)
            {
                // Human player slot - can be Player or Observer
                DrawHumanSlot(slot);
            }
            else
            {
                // AI/Empty slot
                DrawAISlot(index, slot);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawHumanSlot(PlayerSlot slot)
        {
            if (_isObserver)
            {
                // Observer mode: no color, just label
                GUILayout.Label("  ", GUILayout.Width(18));
                GUILayout.Label("Observer", _factionLabelStyle, GUILayout.Width(80));
            }
            else
            {
                // Normal player: color swatch + color picker
                Color oldColor = GUI.color;
                Color slotColor = slot.GetFactionColor();
                GUI.color = slotColor;
                GUILayout.Label("\u25a0", GUILayout.Width(18));
                GUI.color = oldColor;

                string colorName = slot.GetColorName();
                if (GUILayout.Button(colorName, _colorBtnStyle, GUILayout.Width(70)))
                {
                    CycleSlotColor(0);
                }
            }

            // Player/Observer toggle (disabled in sandbox)
            if (!_sandbox)
            {
                string[] modeOptions = { "Player", "Observer" };
                int currentMode = _isObserver ? 1 : 0;
                int newMode = GUILayout.SelectionGrid(currentMode, modeOptions, 2, GUILayout.Width(140));

                if (newMode != currentMode)
                {
                    _isObserver = (newMode == 1);
                    slot.Type = _isObserver ? SlotType.Observer : SlotType.Human;
                }
            }
            else
            {
                GUILayout.Label("Player (You)", GUILayout.Width(140));
            }

            GUILayout.Label("", GUILayout.Width(65)); // spacing to match AI slot
        }

        private void DrawAISlot(int index, PlayerSlot slot)
        {
            // Color swatch
            Color oldColor = GUI.color;
            Color slotColor = slot.GetFactionColor();
            GUI.color = slotColor;
            GUILayout.Label("\u25a0", GUILayout.Width(18));
            GUI.color = oldColor;

            // Color picker button
            string colorName = slot.GetColorName();
            if (GUILayout.Button(colorName, _colorBtnStyle, GUILayout.Width(70)))
            {
                CycleSlotColor(index);
            }

            // Slot type selector: Empty or AI
            string[] options = { "Empty", "AI" };
            int currentOption = slot.Type == SlotType.AI ? 1 : 0;

            int newOption = GUILayout.SelectionGrid(currentOption, options, 2, GUILayout.Width(100));
            slot.Type = newOption == 1 ? SlotType.AI : SlotType.Empty;

            // AI difficulty (only for AI slots)
            if (slot.Type == SlotType.AI)
            {
                string[] difficulties = { "Easy", "Normal", "Hard", "Expert" };
                int diffIndex = (int)slot.AIDifficulty;

                if (GUILayout.Button(difficulties[diffIndex], GUILayout.Width(65)))
                {
                    slot.AIDifficulty = (LobbyAIDifficulty)((diffIndex + 1) % difficulties.Length);
                }
            }
            else
            {
                GUILayout.Label("", GUILayout.Width(65));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // COLOR CYCLING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cycle the color of a slot to the next available (unused) color in the pool.
        /// </summary>
        private void CycleSlotColor(int slotIndex)
        {
            var slot = LobbyConfig.Slots[slotIndex];
            int current = slot.ColorIndex;

            Debug.Log($"[SkirmishLobby] CycleSlotColor({slotIndex}) current={current} ({slot.GetColorName()})");

            // Find the next color not already used by another active slot
            for (int attempt = 0; attempt < FactionColors.ColorCount; attempt++)
            {
                int next = (current + 1 + attempt) % FactionColors.ColorCount;
                if (!IsColorInUse(next, slotIndex))
                {
                    slot.ColorIndex = next;
                    Debug.Log($"[SkirmishLobby] -> Changed to color {next} ({slot.GetColorName()})");
                    return;
                }
            }

            // All colors in use (shouldn't happen with 12 colors and 8 slots) - just cycle
            slot.ColorIndex = (current + 1) % FactionColors.ColorCount;
            Debug.Log($"[SkirmishLobby] -> Fallback to color {slot.ColorIndex} ({slot.GetColorName()})");
        }

        /// <summary>
        /// Check if a color pool index is already used by another active slot.
        /// </summary>
        private bool IsColorInUse(int colorIndex, int excludeSlot)
        {
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (i == excludeSlot) continue;
                if (LobbyConfig.Slots[i].ColorIndex == colorIndex)
                    return true;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYER COUNT
        // ═══════════════════════════════════════════════════════════════

        private void SetPlayerCount(int count)
        {
            int minPlayers = _sandbox ? 1 : 2;
            count = Mathf.Clamp(count, minPlayers, 8);

            // Preserve existing color assignments
            int[] savedColors = new int[8];
            for (int i = 0; i < 8; i++)
                savedColors[i] = LobbyConfig.Slots[i].ColorIndex;

            LobbyConfig.SetupSinglePlayer(count);

            // Restore color assignments
            for (int i = 0; i < 8; i++)
                LobbyConfig.Slots[i].ColorIndex = savedColors[i];

            // Restore observer state on slot 0
            if (_isObserver)
                LobbyConfig.Slots[0].Type = SlotType.Observer;

            // Ensure no color conflicts among active slots
            ResolveColorConflicts();
        }

        /// <summary>
        /// Ensure no two active slots share the same color.
        /// If a conflict is found, the higher-index slot gets reassigned.
        /// </summary>
        private void ResolveColorConflicts()
        {
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                while (IsColorInUse(LobbyConfig.Slots[i].ColorIndex, i))
                {
                    LobbyConfig.Slots[i].ColorIndex =
                        (LobbyConfig.Slots[i].ColorIndex + 1) % FactionColors.ColorCount;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // START GAME
        // ═══════════════════════════════════════════════════════════════

        private void StartGame()
        {
            // Apply settings
            GameSettings.SpawnLayout = _layout;
            GameSettings.TwoSides = _twoSides;
            GameSettings.SpawnSeed = _spawnSeed;
            GameSettings.FogOfWarEnabled = _fogOfWar;
            GameSettings.MapHalfSize = _mapHalfSize;
            GameSettings.MaxStartingResources = _maxResources;
            GameSettings.CrystalCurseEnabled = _crystalCurse;
            GameSettings.IsObserver = _isObserver;

            if (_sandbox)
                GameSettings.Mode = GameMode.Sandbox;
            else
                GameSettings.Mode = GameMode.FreeForAll;

            // Apply color selections to FactionColors runtime system
            LobbyConfig.ApplyColorSelections();

            // Count active players
            int humanCount = 0;
            int aiCount = 0;
            int observerCount = 0;

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot.Type == SlotType.Human) humanCount++;
                else if (slot.Type == SlotType.AI) aiCount++;
                else if (slot.Type == SlotType.Observer) observerCount++;
            }

            // Validation
            if (!_sandbox && humanCount == 0 && observerCount == 0)
            {
                _error = "Need at least 1 human player or observer!";
                return;
            }

            if (_isObserver && aiCount == 0)
            {
                _error = "Observer mode needs at least 1 AI player!";
                return;
            }

            if (_sandbox && humanCount == 0)
            {
                _error = "Need at least 1 human player!";
                return;
            }

            // TotalPlayers includes observer slot when observer is active, so spawn
            // positions and faction indices stay consistent. The observer slot is
            // simply skipped during actual entity spawning.
            GameSettings.TotalPlayers = humanCount + aiCount + observerCount;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.LocalPlayerFaction = Faction.Blue;

            _error = null;

            Debug.Log($"[SkirmishLobby] Starting game with {GameSettings.TotalPlayers} players " +
                      $"(Observer={_isObserver}, Sandbox={_sandbox}, colors: {GetColorSummary()})");
            LoadingScreen.Show(GameSceneName);
        }

        /// <summary>
        /// Build a debug summary of slot color assignments.
        /// </summary>
        private string GetColorSummary()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot.Type == SlotType.Empty) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"P{i + 1}={slot.GetColorName()}");
            }
            return sb.ToString();
        }
    }
}
