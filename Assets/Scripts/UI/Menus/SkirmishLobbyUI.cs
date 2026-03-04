// File: Assets/Scripts/UI/Menus/SkirmishLobbyUI.cs
// Single-player skirmish lobby with player slots, color picker, and map configuration

using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using TheWaningBorder.Core.Config;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Single-player skirmish lobby.
    /// Allows configuration of player slots (1 human, rest AI/empty),
    /// per-slot color selection from a 12-color pool, and map options.
    /// </summary>
    public class SkirmishLobbyUI : MonoBehaviour
    {
        public event Action OnBackPressed;

        private const string GameSceneName = "Game";

        // Window layout
        private Rect _windowRect = new Rect(40, 40, 560, 620);
        private Vector2 _slotsScrollPos;

        // Map settings
        private SpawnLayout _layout = GameSettings.SpawnLayout;
        private TwoSidesPreset _twoSides = GameSettings.TwoSides;
        private int _spawnSeed = GameSettings.SpawnSeed;
        private bool _fogOfWar = GameSettings.FogOfWarEnabled;
        private int _mapHalfSize = GameSettings.MapHalfSize;

        // Error display
        private string _error;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _slotStyle;
        private GUIStyle _factionLabelStyle;
        private GUIStyle _colorBtnStyle;
        private Texture2D _colorSwatchTex;
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

            _stylesInit = true;
        }

        private void DrawWindow(int windowId)
        {
            // Player count
            GUILayout.Label("<b>Number of Players</b>", _headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(" - ", GUILayout.Width(40)))
                SetPlayerCount(LobbyConfig.ActiveSlotCount - 1);
            GUILayout.Label(LobbyConfig.ActiveSlotCount.ToString(), GUILayout.Width(40));
            if (GUILayout.Button(" + ", GUILayout.Width(40)))
                SetPlayerCount(LobbyConfig.ActiveSlotCount + 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Player slots
            GUILayout.Label("<b>Player Slots</b>", _headerStyle);

            _slotsScrollPos = GUILayout.BeginScrollView(_slotsScrollPos, GUILayout.Height(220));

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                DrawPlayerSlot(i);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);

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

            // Fog of war
            GUILayout.Label("<b>Fog of War</b>", _headerStyle);
            _fogOfWar = GUILayout.Toggle(_fogOfWar, _fogOfWar ? " Enabled" : " Disabled");

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

            GUILayout.FlexibleSpace();

            // Action buttons
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

            GUILayout.Space(10);

            GUI.DragWindow(new Rect(0, 0, 10000, 25));
        }

        private void DrawPlayerSlot(int index)
        {
            var slot = LobbyConfig.Slots[index];

            GUILayout.BeginHorizontal(_slotStyle);

            // Color swatch (filled rectangle)
            Color oldColor = GUI.color;
            Color slotColor = slot.GetFactionColor();
            GUI.color = slotColor;
            GUILayout.Label("■", GUILayout.Width(18));
            GUI.color = oldColor;

            // Color picker button - click to cycle through available colors
            string colorName = slot.GetColorName();
            if (GUILayout.Button(colorName, _colorBtnStyle, GUILayout.Width(70)))
            {
                CycleSlotColor(index);
            }

            // Player label
            if (index == 0)
            {
                GUILayout.Label("Player (You)", GUILayout.Width(100));
            }
            else
            {
                // Slot type selector: Empty or AI
                string[] options = { "Empty", "AI" };
                int currentOption = slot.Type == SlotType.AI ? 1 : 0;

                int newOption = GUILayout.SelectionGrid(currentOption, options, 2, GUILayout.Width(100));
                slot.Type = newOption == 1 ? SlotType.AI : SlotType.Empty;
            }

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

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

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
                    Debug.Log($"[SkirmishLobby] → Changed to color {next} ({slot.GetColorName()})");
                    return;
                }
            }

            // All colors in use (shouldn't happen with 12 colors and 8 slots) - just cycle
            slot.ColorIndex = (current + 1) % FactionColors.ColorCount;
            Debug.Log($"[SkirmishLobby] → Fallback to color {slot.ColorIndex} ({slot.GetColorName()})");
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

        private void SetPlayerCount(int count)
        {
            count = Mathf.Clamp(count, 2, 8);

            // Preserve existing color assignments
            int[] savedColors = new int[8];
            for (int i = 0; i < 8; i++)
                savedColors[i] = LobbyConfig.Slots[i].ColorIndex;

            LobbyConfig.SetupSinglePlayer(count);

            // Restore color assignments
            for (int i = 0; i < 8; i++)
                LobbyConfig.Slots[i].ColorIndex = savedColors[i];

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

        private void StartGame()
        {
            // Apply settings
            GameSettings.SpawnLayout = _layout;
            GameSettings.TwoSides = _twoSides;
            GameSettings.SpawnSeed = _spawnSeed;
            GameSettings.FogOfWarEnabled = _fogOfWar;
            GameSettings.MapHalfSize = _mapHalfSize;

            // Apply color selections to FactionColors runtime system
            LobbyConfig.ApplyColorSelections();

            // Count active players
            int humanCount = 0;
            int aiCount = 0;

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot.Type == SlotType.Human) humanCount++;
                else if (slot.Type == SlotType.AI) aiCount++;
            }

            if (humanCount == 0)
            {
                _error = "Need at least 1 human player!";
                return;
            }

            GameSettings.TotalPlayers = humanCount + aiCount;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.LocalPlayerFaction = Faction.Blue;

            _error = null;

            Debug.Log($"[SkirmishLobby] Starting game with {GameSettings.TotalPlayers} players " +
                      $"(colors: {GetColorSummary()})");
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