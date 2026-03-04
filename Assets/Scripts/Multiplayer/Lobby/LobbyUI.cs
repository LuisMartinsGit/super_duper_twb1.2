// LobbyUI.cs
// IMGUI-based multiplayer lobby interface
// Part of: Multiplayer/Lobby/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Multiplayer
{
    /// <summary>
    /// Multiplayer lobby UI using Unity's IMGUI system.
    /// 
    /// States:
    /// - MainChoice: Host/Join selection
    /// - HostSetup: Configure game settings before hosting
    /// - HostLobby: Active lobby as host
    /// - BrowseGames: Discovering and listing LAN games
    /// - ClientLobby: Connected to host's lobby
    /// - Connecting: Waiting for connection
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // EVENTS
        // ═══════════════════════════════════════════════════════════════════════
        
        public event Action OnBackPressed;

        // ═══════════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ═══════════════════════════════════════════════════════════════════════
        
        private const string GameSceneName = "Game";

        // ═══════════════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private enum LobbyState
        {
            MainChoice,
            HostSetup,
            HostLobby,
            BrowseGames,
            ClientLobby,
            Connecting
        }
        private LobbyState _state = LobbyState.MainChoice;

        // ═══════════════════════════════════════════════════════════════════════
        // UI LAYOUT
        // ═══════════════════════════════════════════════════════════════════════
        
        private Rect _windowRect = new Rect(40, 40, 560, 620);
        private Vector2 _slotsScrollPos;
        private Vector2 _gamesScrollPos;

        // ═══════════════════════════════════════════════════════════════════════
        // SETTINGS
        // ═══════════════════════════════════════════════════════════════════════
        
        private string _gameName = "My Game";
        private string _playerName = System.Environment.MachineName;
        private ushort _port = 7979;

        // Map settings
        private SpawnLayout _layout = GameSettings.SpawnLayout;
        private TwoSidesPreset _twoSides = GameSettings.TwoSides;
        private int _spawnSeed = GameSettings.SpawnSeed;
        private bool _fogOfWar = GameSettings.FogOfWarEnabled;
        private int _mapHalfSize = GameSettings.MapHalfSize;

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORKING
        // ═══════════════════════════════════════════════════════════════════════
        
        private LobbyManager _lobby;
        private string _error;
        private string _status;

        // ═══════════════════════════════════════════════════════════════════════
        // GUI STYLES
        // ═══════════════════════════════════════════════════════════════════════
        
        private GUIStyle _headerStyle;
        private GUIStyle _slotStyle;
        private GUIStyle _factionLabelStyle;
        private GUIStyle _gameListStyle;
        private bool _stylesInit = false;

        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════

        void Awake()
        {
            Application.runInBackground = true;
            _lobby = new LobbyManager();
            
            // Subscribe to events
            _lobby.OnGameDiscovered += OnGameDiscovered;
            _lobby.OnGameLost += OnGameLost;
            _lobby.OnJoinAccepted += OnJoinAccepted;
            _lobby.OnLobbyUpdated += OnLobbyUpdated;
            _lobby.OnGameStarting += OnGameStarting;
            _lobby.OnError += OnLobbyError;
        }

        void OnEnable()
        {
            _state = LobbyState.MainChoice;
            _error = null;
            _status = null;
        }

        void OnDisable()
        {
            _lobby?.Shutdown();
        }

        void OnDestroy()
        {
            _lobby?.Shutdown();
        }

        void Update()
        {
            _lobby?.Update();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ═══════════════════════════════════════════════════════════════════════

        private void OnGameDiscovered(DiscoveredGame game)
        {
            Debug.Log($"[LobbyUI] Discovered: {game.GameName}");
        }

        private void OnGameLost(string ip)
        {
            Debug.Log($"[LobbyUI] Lost game at {ip}");
        }

        private void OnJoinAccepted(int slotIndex)
        {
            _state = LobbyState.ClientLobby;
            _status = $"Connected! You are in slot {slotIndex + 1}";
        }

        private void OnLobbyUpdated()
        {
            // Refresh UI
        }

        private void OnGameStarting(int lockstepPort, int factionIndex)
        {
            // Setup lockstep bootstrap for client
            SetupLockstepBootstrapAsClient(lockstepPort, factionIndex);
            LoadGameScene();
        }

        private void OnLobbyError(string error)
        {
            _error = error;
            Debug.LogError($"[LobbyUI] {error}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // GUI
        // ═══════════════════════════════════════════════════════════════════════

        void OnGUI()
        {
            InitStyles();
            _windowRect = GUI.Window(1001, _windowRect, DrawWindow, "Multiplayer Lobby");
        }

        private void InitStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                richText = true
            };

            _slotStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };

            _factionLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _gameListStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = Color.red;
                GUILayout.Label(_error);
                GUI.color = Color.white;
            }

            switch (_state)
            {
                case LobbyState.MainChoice:
                    DrawMainChoice();
                    break;
                case LobbyState.HostSetup:
                    DrawHostSetup();
                    break;
                case LobbyState.HostLobby:
                    DrawHostLobby();
                    break;
                case LobbyState.BrowseGames:
                    DrawBrowseGames();
                    break;
                case LobbyState.ClientLobby:
                    DrawClientLobby();
                    break;
                case LobbyState.Connecting:
                    DrawConnecting();
                    break;
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // STATE VIEWS
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawMainChoice()
        {
            GUILayout.Label("<b>LAN Multiplayer</b>", _headerStyle);
            GUILayout.Space(20);

            if (GUILayout.Button("Host Game", GUILayout.Height(50)))
            {
                _state = LobbyState.HostSetup;
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Join Game", GUILayout.Height(50)))
            {
                _lobby.StartClient(_playerName);
                _state = LobbyState.BrowseGames;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Back", GUILayout.Height(36)))
            {
                OnBackPressed?.Invoke();
            }
        }

        private void DrawHostSetup()
        {
            GUILayout.Label("<b>Host Game Setup</b>", _headerStyle);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Game Name:", GUILayout.Width(100));
            _gameName = GUILayout.TextField(_gameName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Your Name:", GUILayout.Width(100));
            _playerName = GUILayout.TextField(_playerName);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.Label("<b>Players</b>", _headerStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Player Count: {LobbyConfig.ActiveSlotCount}", GUILayout.Width(120));
            if (GUILayout.Button(" - ", GUILayout.Width(40)))
                SetPlayerCount(LobbyConfig.ActiveSlotCount - 1);
            if (GUILayout.Button(" + ", GUILayout.Width(40)))
                SetPlayerCount(LobbyConfig.ActiveSlotCount + 1);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            DrawMapOptions();

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Height(36), GUILayout.Width(100)))
                _state = LobbyState.MainChoice;
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Create Lobby", GUILayout.Height(36), GUILayout.Width(150)))
                CreateHostLobby();
            GUILayout.EndHorizontal();
        }

        private void DrawHostLobby()
        {
            GUILayout.Label($"<b>Hosting: {_gameName}</b>", _headerStyle);

            GUI.color = Color.green;
            GUILayout.Label($"● Broadcasting on port {LobbyManager.BROADCAST_PORT}");
            GUILayout.Label($"● {_lobby.ConnectedClients.Count} client(s) connected");
            GUI.color = Color.white;

            GUILayout.Space(10);
            GUILayout.Label("<b>Player Slots</b>", _headerStyle);

            _slotsScrollPos = GUILayout.BeginScrollView(_slotsScrollPos, GUILayout.Height(200));
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                DrawSlot(i, isHost: true);
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            GUILayout.Label($"<b>Map:</b> {_layout} | FoW: {(_fogOfWar ? "On" : "Off")} | Size: {_mapHalfSize * 2}");

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", GUILayout.Height(36), GUILayout.Width(100)))
            {
                _lobby.Shutdown();
                _state = LobbyState.MainChoice;
            }
            
            GUILayout.FlexibleSpace();

            int humanCount = _lobby.Slots.Take(LobbyConfig.ActiveSlotCount).Count(s => s.Type == LobbySlotType.Human);
            GUI.enabled = humanCount >= 1;
            if (GUILayout.Button("Start Game", GUILayout.Height(36), GUILayout.Width(150)))
                StartMultiplayerGame();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawBrowseGames()
        {
            GUILayout.Label("<b>Available Games</b>", _headerStyle);

            GUI.color = Color.green;
            GUILayout.Label($"● Listening on port {LobbyManager.BROADCAST_PORT}");
            GUI.color = Color.white;

            GUILayout.Space(10);

            var games = _lobby.DiscoveredGames;
            if (games.Count == 0)
            {
                GUILayout.Label("Searching for games...");
            }
            else
            {
                _gamesScrollPos = GUILayout.BeginScrollView(_gamesScrollPos, GUILayout.Height(300));
                foreach (var game in games)
                {
                    GUILayout.BeginHorizontal(_gameListStyle);
                    GUILayout.Label($"{game.GameName} ({game.HostName})", GUILayout.Width(250));
                    GUILayout.Label(game.IPAddress, GUILayout.Width(120));
                    if (GUILayout.Button("Join", GUILayout.Width(80)))
                    {
                        _lobby.JoinGame(game);
                        _state = LobbyState.Connecting;
                        _status = $"Joining {game.GameName}...";
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Back", GUILayout.Height(36), GUILayout.Width(100)))
            {
                _lobby.Shutdown();
                _state = LobbyState.MainChoice;
            }
        }

        private void DrawClientLobby()
        {
            GUILayout.Label("<b>Connected to Game</b>", _headerStyle);

            GUI.color = Color.green;
            GUILayout.Label($"● Connected to {_lobby.HostIP}");
            GUILayout.Label($"● You are Player {_lobby.MySlotIndex + 1}");
            GUI.color = Color.white;

            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);

            GUILayout.Space(10);
            GUILayout.Label("<b>Player Slots</b>", _headerStyle);

            _slotsScrollPos = GUILayout.BeginScrollView(_slotsScrollPos, GUILayout.Height(200));
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                DrawSlot(i, isHost: false);
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Leave", GUILayout.Height(36), GUILayout.Width(100)))
            {
                _lobby.LeaveLobby();
                _lobby.Shutdown();
                _lobby.StartClient(_playerName);
                _state = LobbyState.BrowseGames;
            }
        }

        private void DrawConnecting()
        {
            GUILayout.Label("<b>Connecting...</b>", _headerStyle);
            GUILayout.Space(20);
            GUILayout.Label(_status ?? "Please wait...");

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Height(36), GUILayout.Width(100)))
            {
                _lobby.Shutdown();
                _lobby.StartClient(_playerName);
                _state = LobbyState.BrowseGames;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SLOT DRAWING
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawSlot(int index, bool isHost)
        {
            var slot = _lobby.Slots[index];
            var faction = LobbyConfig.Slots[index].Faction;

            GUILayout.BeginHorizontal(_slotStyle);

            // Faction color indicator
            Color oldColor = GUI.color;
            GUI.color = LobbyConfig.Slots[index].GetFactionColor();
            GUILayout.Label("■", GUILayout.Width(20));
            GUI.color = oldColor;

            GUILayout.Label(faction.ToString(), _factionLabelStyle, GUILayout.Width(60));

            if (slot.Type == LobbySlotType.Human)
            {
                string label = string.IsNullOrEmpty(slot.PlayerName) ? "Player" : slot.PlayerName;
                if (index == 0) label += " (Host)";
                if (index == _lobby.MySlotIndex && !_lobby.IsHost) label += " (You)";
                
                GUI.color = Color.cyan;
                GUILayout.Label(label);
                GUI.color = Color.white;
            }
            else if (slot.Type == LobbySlotType.AI)
            {
                if (isHost)
                {
                    if (GUILayout.Button("AI", GUILayout.Width(50)))
                        slot.Type = LobbySlotType.Empty;
                    
                    string[] difficulties = { "Easy", "Normal", "Hard", "Expert" };
                    if (GUILayout.Button(difficulties[(int)slot.AIDifficulty], GUILayout.Width(70)))
                        slot.AIDifficulty = (LobbyAIDifficulty)(((int)slot.AIDifficulty + 1) % 4);
                }
                else
                {
                    GUILayout.Label($"AI ({slot.AIDifficulty})");
                }
            }
            else // Empty
            {
                if (isHost)
                {
                    if (GUILayout.Button("Empty", GUILayout.Width(50)))
                        slot.Type = LobbySlotType.AI;
                    GUILayout.Label("(Open)");
                }
                else
                {
                    GUILayout.Label("Empty");
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MAP OPTIONS
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawMapOptions()
        {
            GUILayout.Label("<b>Map Options</b>", _headerStyle);

            // Layout
            GUILayout.BeginHorizontal();
            GUILayout.Label("Layout:", GUILayout.Width(80));
            string[] layouts = Enum.GetNames(typeof(SpawnLayout));
            int layoutIdx = (int)_layout;
            if (GUILayout.Button(layouts[layoutIdx], GUILayout.Width(100)))
                _layout = (SpawnLayout)((layoutIdx + 1) % layouts.Length);
            GUILayout.EndHorizontal();

            // Fog of War
            GUILayout.BeginHorizontal();
            GUILayout.Label("Fog of War:", GUILayout.Width(80));
            if (GUILayout.Button(_fogOfWar ? "ON" : "OFF", GUILayout.Width(60)))
                _fogOfWar = !_fogOfWar;
            GUILayout.EndHorizontal();

            // Map Size
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map Size:", GUILayout.Width(80));
            if (GUILayout.Button("-", GUILayout.Width(30)))
                _mapHalfSize = Mathf.Max(25, _mapHalfSize - 25);
            GUILayout.Label($"{_mapHalfSize * 2}", GUILayout.Width(50));
            if (GUILayout.Button("+", GUILayout.Width(30)))
                _mapHalfSize = Mathf.Min(200, _mapHalfSize + 25);
            GUILayout.EndHorizontal();

            // Seed
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed:", GUILayout.Width(80));
            string seedStr = GUILayout.TextField(_spawnSeed.ToString(), GUILayout.Width(80));
            if (int.TryParse(seedStr, out int seed))
                _spawnSeed = seed;
            if (GUILayout.Button("Random", GUILayout.Width(70)))
                _spawnSeed = UnityEngine.Random.Range(1, 99999);
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ACTIONS
        // ═══════════════════════════════════════════════════════════════════════

        private void SetPlayerCount(int count)
        {
            LobbyConfig.ActiveSlotCount = Mathf.Clamp(count, 2, 8);
        }

        private void CreateHostLobby()
        {
            LobbyConfig.SetupMultiplayer(LobbyConfig.ActiveSlotCount);
            
            if (_lobby.StartHost(_gameName, _playerName, _port))
            {
                GameSettings.IsMultiplayer = true;
                GameSettings.NetworkRole = NetworkRole.Server;
                GameSettings.LocalPlayerFaction = Faction.Blue;
                
                _state = LobbyState.HostLobby;
            }
        }

        private void StartMultiplayerGame()
        {
            // Apply map settings
            GameSettings.SpawnLayout = _layout;
            GameSettings.TwoSides = _twoSides;
            GameSettings.SpawnSeed = _spawnSeed;
            GameSettings.FogOfWarEnabled = _fogOfWar;
            GameSettings.MapHalfSize = _mapHalfSize;

            // Set total player count (critical — without this, defaults to 2)
            GameSettings.TotalPlayers = LobbyConfig.ActiveSlotCount;

            // Notify clients
            int lockstepPort = _port + 1;
            _lobby.StartGame(lockstepPort);

            // Setup lockstep for host
            SetupLockstepBootstrapAsHost(lockstepPort);

            LoadGameScene();
        }

        private void SetupLockstepBootstrapAsHost(int lockstepPort)
        {
            var bootstrap = GetOrCreateBootstrap();
            
            var remotePlayers = new List<RemotePlayerInfo>();
            foreach (var client in _lobby.ConnectedClients.Values)
            {
                remotePlayers.Add(new RemotePlayerInfo
                {
                    IP = client.IP,
                    Port = client.Port,
                    Faction = client.Faction,
                    PlayerName = client.PlayerName
                });
            }
            
            bootstrap.ConfigureAsHost(lockstepPort, remotePlayers);
        }

        private void SetupLockstepBootstrapAsClient(int lockstepPort, int factionIndex)
        {
            var bootstrap = GetOrCreateBootstrap();
            
            bootstrap.ConfigureAsClient(
                _lobby.HostIP,
                lockstepPort,
                lockstepPort + _lobby.MySlotIndex,
                _lobby.MySlotIndex,
                (Faction)factionIndex
            );
        }

        private LockstepBootstrap GetOrCreateBootstrap()
        {
            var bootstrap = LockstepBootstrap.Instance;
            if (bootstrap == null)
            {
                var go = new GameObject("LockstepBootstrap");
                bootstrap = go.AddComponent<LockstepBootstrap>();
            }
            return bootstrap;
        }

        private void LoadGameScene()
        {
            _lobby.Shutdown();
            SceneManager.LoadScene(GameSceneName);
        }
    }
}