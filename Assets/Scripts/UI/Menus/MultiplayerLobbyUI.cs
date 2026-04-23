// File: Assets/Scripts/UI/Menus/MultiplayerLobbyUI.cs
// Multiplayer lobby UI with LAN discovery and game hosting

using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Multiplayer lobby UI with full LAN networking.
    /// 
    /// Protocol:
    /// - TWB_GAME|GameName|HostName|GamePort    (Host → Broadcast)
    /// - TWB_JOIN|PlayerName|ClientPort         (Client → Host)
    /// - TWB_ACCEPT|SlotIndex                   (Host → Client)
    /// - TWB_LOBBY|SlotCount|Slot0|Slot1|...    (Host → Client)
    /// - TWB_LEAVE|SlotIndex                    (Client → Host)
    /// - TWB_START|Port                         (Host → Client)
    /// </summary>
    public class MultiplayerLobbyUI : MonoBehaviour
    {
        public event Action OnBackPressed;

        private const string GameSceneName = "Game";
        private const int BROADCAST_PORT = 47515;
        private const float BROADCAST_INTERVAL = 1.0f;
        private const float LOBBY_SYNC_INTERVAL = 0.5f;
        private const float DISCOVERY_TIMEOUT = 5.0f;

        // Message prefixes
        private const string MSG_GAME = "TWB_GAME|";
        private const string MSG_JOIN = "TWB_JOIN|";
        private const string MSG_LOBBY = "TWB_LOBBY|";
        private const string MSG_LEAVE = "TWB_LEAVE|";
        private const string MSG_START = "TWB_START|";
        private const string MSG_ACCEPT = "TWB_ACCEPT|";
        private const string MSG_COLOR = "TWB_COLOR|";

        // State machine
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

        // Window layout
        private Rect _windowRect = new Rect(40, 40, 560, 620);
        private Vector2 _slotsScrollPos;
        private Vector2 _gamesScrollPos;

        // Host settings
        private string _gameName = "My Game";
        private string _playerName = System.Environment.MachineName;
        private ushort _port = 7979;

        // Map settings
        private SpawnLayout _layout = GameSettings.SpawnLayout;
        private TwoSidesPreset _twoSides = GameSettings.TwoSides;
        private int _spawnSeed = GameSettings.SpawnSeed;
        private bool _fogOfWar = GameSettings.FogOfWarEnabled;
        private int _mapHalfSize = GameSettings.MapHalfSize;
        private bool _crystalCurse = GameSettings.CrystalCurseEnabled;

        // Network state
        private UdpClient _hostSocket;
        private UdpClient _clientBroadcastSocket;
        private UdpClient _clientPrivateSocket;
        private ushort _clientPrivatePort;
        private bool _isHost;
        private int _mySlotIndex = -1;
        private IPEndPoint _hostEndpoint;

        // Game discovery
        private class DiscoveredGame
        {
            public string GameName;
            public string HostName;
            public ushort Port;
            public IPEndPoint Endpoint;
            public float LastSeen;
        }
        private List<DiscoveredGame> _discoveredGames = new();

        // Network slots
        private class NetworkSlot
        {
            public SlotType Type = SlotType.Empty;
            public string PlayerName = "";
            public IPEndPoint Endpoint;
            public ushort ClientPort;
            public LobbyAIDifficulty AIDifficulty = LobbyAIDifficulty.Normal;
        }
        private NetworkSlot[] _networkSlots = new NetworkSlot[8];

        // Timers
        private float _broadcastTimer;
        private float _lobbySyncTimer;
        private string _error;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _slotStyle;
        private GUIStyle _factionLabelStyle;
        private GUIStyle _gameListStyle;
        private bool _stylesInit;

        void OnEnable()
        {
            _state = LobbyState.MainChoice;
            for (int i = 0; i < 8; i++)
                _networkSlots[i] = new NetworkSlot();
            // Randomize seed each time the lobby opens
            _spawnSeed = UnityEngine.Random.Range(1, 99999);
        }

        void OnDisable()
        {
            Cleanup();
        }

        void Update()
        {
            if (_isHost)
            {
                HostUpdate();
            }
            else
            {
                ClientUpdate();
            }

            // Timeout old games
            _discoveredGames.RemoveAll(g => Time.time - g.LastSeen > DISCOVERY_TIMEOUT);
        }

        void OnGUI()
        {
            InitStyles();
            _windowRect = GUI.Window(10003, _windowRect, DrawWindow, "Multiplayer Lobby");
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

            _gameListStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4)
            };

            _stylesInit = true;
        }

        private void DrawWindow(int windowId)
        {
            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = Color.red;
                GUILayout.Label(_error);
                GUI.color = Color.white;
            }

            switch (_state)
            {
                case LobbyState.MainChoice: DrawMainChoice(); break;
                case LobbyState.HostSetup: DrawHostSetup(); break;
                case LobbyState.HostLobby: DrawHostLobby(); break;
                case LobbyState.BrowseGames: DrawBrowseGames(); break;
                case LobbyState.ClientLobby: DrawClientLobby(); break;
                case LobbyState.Connecting: DrawConnecting(); break;
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 25));
        }

        private void DrawMainChoice()
        {
            GUILayout.Label("<b>Multiplayer</b>", _headerStyle);
            GUILayout.Space(20);

            if (GUILayout.Button("Host Game", GUILayout.Height(50)))
                _state = LobbyState.HostSetup;

            GUILayout.Space(10);

            if (GUILayout.Button("Join Game", GUILayout.Height(50)))
            {
                StartClient();
                _state = LobbyState.BrowseGames;
            }

            GUILayout.Space(20);

            if (GUILayout.Button("Back", GUILayout.Height(40)))
                OnBackPressed?.Invoke();
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

            GUILayout.BeginHorizontal();
            GUILayout.Label("Port:", GUILayout.Width(100));
            var portStr = GUILayout.TextField(_port.ToString(), GUILayout.Width(80));
            if (ushort.TryParse(portStr, out ushort newPort))
                _port = newPort;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Players:", GUILayout.Width(100));
            if (GUILayout.Button("-", GUILayout.Width(30)) && LobbyConfig.ActiveSlotCount > 2)
                LobbyConfig.ActiveSlotCount--;
            GUILayout.Label(LobbyConfig.ActiveSlotCount.ToString(), GUILayout.Width(30));
            if (GUILayout.Button("+", GUILayout.Width(30)) && LobbyConfig.ActiveSlotCount < 8)
                LobbyConfig.ActiveSlotCount++;
            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Height(36), GUILayout.Width(100)))
                _state = LobbyState.MainChoice;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Create Lobby", GUILayout.Height(36), GUILayout.Width(120)))
            {
                StartHost();
                _state = LobbyState.HostLobby;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHostLobby()
        {
            GUILayout.Label($"<b>Hosting: {_gameName}</b>", _headerStyle);
            GUILayout.Space(10);

            // Player slots
            _slotsScrollPos = GUILayout.BeginScrollView(_slotsScrollPos, GUILayout.Height(200));
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                DrawNetworkSlot(i, isHost: true);
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            DrawMapOptions();

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", GUILayout.Height(36), GUILayout.Width(100)))
            {
                Cleanup();
                _state = LobbyState.MainChoice;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Start Game", GUILayout.Height(36), GUILayout.Width(120)))
                StartMultiplayerGame();
            GUILayout.EndHorizontal();
        }

        private void DrawBrowseGames()
        {
            GUILayout.Label("<b>Available Games</b>", _headerStyle);
            GUILayout.Space(10);

            _gamesScrollPos = GUILayout.BeginScrollView(_gamesScrollPos, GUILayout.Height(300));

            if (_discoveredGames.Count == 0)
            {
                GUILayout.Label("Searching for games...");
            }
            else
            {
                foreach (var game in _discoveredGames)
                {
                    GUILayout.BeginHorizontal(_gameListStyle);
                    GUILayout.Label($"{game.GameName} ({game.HostName})", GUILayout.Width(300));
                    if (GUILayout.Button("Join", GUILayout.Width(80)))
                    {
                        JoinGame(game);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);

            if (GUILayout.Button("Back", GUILayout.Height(36), GUILayout.Width(100)))
            {
                Cleanup();
                _state = LobbyState.MainChoice;
            }
        }

        private void DrawClientLobby()
        {
            GUILayout.Label("<b>Lobby</b>", _headerStyle);
            GUILayout.Space(10);

            _slotsScrollPos = GUILayout.BeginScrollView(_slotsScrollPos, GUILayout.Height(200));
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                DrawNetworkSlot(i, isHost: false);
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            DrawMapOptionsReadOnly();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Leave", GUILayout.Height(36), GUILayout.Width(100)))
            {
                Cleanup();
                StartClient();
                _state = LobbyState.BrowseGames;
            }
        }

        private void DrawConnecting()
        {
            GUILayout.Label("Connecting...", _headerStyle);
            GUILayout.Label(_error ?? "Please wait...");

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Height(36), GUILayout.Width(100)))
            {
                Cleanup();
                StartClient();
                _state = LobbyState.BrowseGames;
            }
        }

        private void DrawNetworkSlot(int index, bool isHost)
        {
            var slot = _networkSlots[index];

            GUILayout.BeginHorizontal(_slotStyle);

            // Color swatch — clickable for host (any slot) or client (own slot only)
            Color oldColor = GUI.color;
            GUI.color = LobbyConfig.Slots[index].GetFactionColor();
            bool canChangeColor = isHost || index == _mySlotIndex;
            if (canChangeColor)
            {
                if (GUILayout.Button("■", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    if (isHost)
                        CycleSlotColor(index);
                        SendColorChange(index);
                }
            }
            else
            {
                GUILayout.Label("■", GUILayout.Width(24));
            }
            GUI.color = oldColor;

            // Faction name
            GUILayout.Label(LobbyConfig.Slots[index].Faction.ToString(), _factionLabelStyle, GUILayout.Width(60));

            // Slot content
            if (slot.Type == SlotType.Human)
            {
                string label = string.IsNullOrEmpty(slot.PlayerName) ? "Player" : slot.PlayerName;
                if (index == 0) label += " (Host)";
                if (index == _mySlotIndex && !_isHost) label += " (You)";
                GUI.color = Color.cyan;
                GUILayout.Label(label);
                GUI.color = Color.white;
            }
            else if (slot.Type == SlotType.AI)
            {
                if (isHost)
                {
                    if (GUILayout.Button("AI", GUILayout.Width(50)))
                        slot.Type = SlotType.Empty;
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
                    if (GUILayout.Button("Open", GUILayout.Width(50)))
                        slot.Type = SlotType.AI;
                    GUILayout.Label("(Waiting for player)");
                }
                else
                {
                    GUILayout.Label("Open");
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void CycleSlotColor(int slotIndex)
        {
            var slot = LobbyConfig.Slots[slotIndex];
            int current = slot.ColorIndex;
            for (int i = 1; i < FactionColors.ColorCount; i++)
            {
                int next = (current + i) % FactionColors.ColorCount;
                if (!IsColorInUse(next, slotIndex))
                {
                    slot.ColorIndex = next;
                    return;
                }
            }
        }

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

        private void SendColorChange(int slotIndex)
        {
            // Client cycles color locally and sends to host
            CycleSlotColor(slotIndex);
            int newColor = LobbyConfig.Slots[slotIndex].ColorIndex;
            string msg = $"{MSG_COLOR}{slotIndex}|{newColor}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            if (_clientPrivateSocket != null && _hostEndpoint != null)
                _clientPrivateSocket.Send(data, data.Length, _hostEndpoint);
        }

        private void DrawMapOptions()
        {
            GUILayout.Label("<b>Map & Game Settings</b>", _headerStyle);

            // Layout
            GUILayout.BeginHorizontal();
            GUILayout.Label("Layout:", GUILayout.Width(60));
            if (GUILayout.Toggle(_layout == SpawnLayout.Circle, "Circle", "Button", GUILayout.Width(70)))
                _layout = SpawnLayout.Circle;
            if (GUILayout.Toggle(_layout == SpawnLayout.TwoSides, "2-Sides", "Button", GUILayout.Width(70)))
                _layout = SpawnLayout.TwoSides;
            GUILayout.EndHorizontal();

            // 2-Sides preset (only if TwoSides selected)
            if (_layout == SpawnLayout.TwoSides)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Sides:", GUILayout.Width(60));
                string[] presets = { "L-R", "U-D", "LU", "LD", "RU", "RD" };
                int twoIdx = (int)_twoSides;
                if (GUILayout.Button(presets[twoIdx], GUILayout.Width(50)))
                    _twoSides = (TwoSidesPreset)((twoIdx + 1) % presets.Length);
                GUILayout.EndHorizontal();
            }

            // Map size — button toggle: Small (50) / Medium (75) / Large (125)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map Size:", GUILayout.Width(60));
            if (GUILayout.Toggle(_mapHalfSize <= 50, "Small", "Button", GUILayout.Width(60)))
                _mapHalfSize = 50;
            if (GUILayout.Toggle(_mapHalfSize > 50 && _mapHalfSize <= 100, "Medium", "Button", GUILayout.Width(70)))
                _mapHalfSize = 75;
            if (GUILayout.Toggle(_mapHalfSize > 100, "Large", "Button", GUILayout.Width(60)))
                _mapHalfSize = 125;
            GUILayout.EndHorizontal();

            // Toggles
            _fogOfWar = GUILayout.Toggle(_fogOfWar, " Fog of War");
            _crystalCurse = GUILayout.Toggle(_crystalCurse, " Crystal Curse");
        }

        private void DrawMapOptionsReadOnly()
        {
            GUILayout.Label("<b>Map & Game Settings</b>", _headerStyle);
            string layoutStr = _layout == SpawnLayout.TwoSides ? "2-Sides" : "Circle";
            string sizeLabel = _mapHalfSize <= 50 ? "Small" : _mapHalfSize <= 100 ? "Medium" : "Large";
            GUILayout.Label($"  Layout: {layoutStr}");
            GUILayout.Label($"  Map Size: {sizeLabel} ({_mapHalfSize * 2})");
            GUILayout.Label($"  Fog of War: {(_fogOfWar ? "On" : "Off")}");
            GUILayout.Label($"  Crystal Curse: {(_crystalCurse ? "On" : "Off")}");
        }

        // ==================== NETWORKING ====================

        private void StartHost()
        {
            _isHost = true;
            _mySlotIndex = 0;

            // Setup host slot
            _networkSlots[0].Type = SlotType.Human;
            _networkSlots[0].PlayerName = _playerName;

            // Setup other slots as AI
            for (int i = 1; i < 8; i++)
            {
                _networkSlots[i].Type = i < LobbyConfig.ActiveSlotCount ? SlotType.AI : SlotType.Empty;
            }

            try
            {
                _hostSocket = CreateBroadcastSocket(BROADCAST_PORT);
            }
            catch (SocketException se)
            {
                string hint = se.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"Port {BROADCAST_PORT} already in use. Close other game instances or restart Unity."
                    : $"Socket error ({se.SocketErrorCode}): {se.Message}";
                _error = $"Network error: {hint}";
            }
            catch (Exception e)
            {
                _error = $"Failed to start host: {e.Message}";
            }
        }

        private void StartClient()
        {
            _isHost = false;
            _mySlotIndex = -1;

            try
            {
                // Broadcast listener (shared port — must use raw socket to avoid double-bind)
                _clientBroadcastSocket = CreateBroadcastSocket(BROADCAST_PORT);

                // Private socket (random port for direct host communication)
                _clientPrivateSocket = new UdpClient(0);
                _clientPrivatePort = (ushort)((IPEndPoint)_clientPrivateSocket.Client.LocalEndPoint).Port;

            }
            catch (SocketException se)
            {
                string hint = se.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"Port {BROADCAST_PORT} already in use. Close other game instances or restart Unity."
                    : $"Socket error ({se.SocketErrorCode}): {se.Message}";
                _error = $"Network error: {hint}";
            }
            catch (Exception e)
            {
                _error = $"Failed to start client: {e.Message}";
            }
        }

        private void HostUpdate()
        {
            if (_hostSocket == null) return;

            // Broadcast game info
            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= BROADCAST_INTERVAL)
            {
                _broadcastTimer = 0f;
                BroadcastGameInfo();
            }

            // Receive messages
            while (_hostSocket.Available > 0)
            {
                try
                {
                    IPEndPoint remote = null;
                    byte[] data = _hostSocket.Receive(ref remote);
                    string msg = Encoding.UTF8.GetString(data);
                    HandleHostMessage(msg, remote);
                }
                catch { }
            }

            // Sync lobby state
            _lobbySyncTimer += Time.deltaTime;
            if (_lobbySyncTimer >= LOBBY_SYNC_INTERVAL)
            {
                _lobbySyncTimer = 0f;
                SyncLobbyState();
            }
        }

        private void ClientUpdate()
        {
            // Listen for broadcasts
            if (_clientBroadcastSocket != null)
            {
                while (_clientBroadcastSocket.Available > 0)
                {
                    try
                    {
                        IPEndPoint remote = null;
                        byte[] data = _clientBroadcastSocket.Receive(ref remote);
                        string msg = Encoding.UTF8.GetString(data);
                        HandleClientBroadcast(msg, remote);
                    }
                    catch { }
                }
            }

            // Listen for direct messages
            if (_clientPrivateSocket != null)
            {
                while (_clientPrivateSocket.Available > 0)
                {
                    try
                    {
                        IPEndPoint remote = null;
                        byte[] data = _clientPrivateSocket.Receive(ref remote);
                        string msg = Encoding.UTF8.GetString(data);
                        HandleClientMessage(msg, remote);
                    }
                    catch { }
                }
            }
        }

        private void BroadcastGameInfo()
        {
            string msg = $"{MSG_GAME}{_gameName}|{_playerName}|{_port}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            _hostSocket.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, BROADCAST_PORT));
        }

        private void HandleHostMessage(string msg, IPEndPoint remote)
        {
            if (msg.StartsWith(MSG_JOIN))
            {
                var parts = msg.Substring(MSG_JOIN.Length).Split('|');
                if (parts.Length >= 2)
                {
                    string playerName = parts[0];
                    ushort clientPort = ushort.Parse(parts[1]);

                    // Find empty slot
                    int slot = FindEmptySlot();
                    if (slot >= 0)
                    {
                        _networkSlots[slot].Type = SlotType.Human;
                        _networkSlots[slot].PlayerName = playerName;
                        _networkSlots[slot].Endpoint = remote;
                        _networkSlots[slot].ClientPort = clientPort;

                        // Send accept
                        string accept = $"{MSG_ACCEPT}{slot}";
                        byte[] data = Encoding.UTF8.GetBytes(accept);
                        _hostSocket.Send(data, data.Length, new IPEndPoint(remote.Address, clientPort));

                    }
                }
            }
            else if (msg.StartsWith(MSG_COLOR))
            {
                var parts = msg.Substring(MSG_COLOR.Length).Split('|');
                if (parts.Length >= 2)
                {
                    int slotIndex = int.Parse(parts[0]);
                    int colorIndex = int.Parse(parts[1]);
                    if (slotIndex > 0 && slotIndex < LobbyConfig.ActiveSlotCount && !IsColorInUse(colorIndex, slotIndex))
                    {
                        LobbyConfig.Slots[slotIndex].ColorIndex = colorIndex;
                    }
                }
            }
        }

        private void HandleClientBroadcast(string msg, IPEndPoint remote)
        {
            if (msg.StartsWith(MSG_GAME))
            {
                var parts = msg.Substring(MSG_GAME.Length).Split('|');
                if (parts.Length >= 3)
                {
                    var game = _discoveredGames.FirstOrDefault(g => g.Endpoint.Address.Equals(remote.Address));
                    if (game == null)
                    {
                        game = new DiscoveredGame { Endpoint = remote };
                        _discoveredGames.Add(game);
                    }
                    game.GameName = parts[0];
                    game.HostName = parts[1];
                    game.Port = ushort.Parse(parts[2]);
                    game.LastSeen = Time.time;
                }
            }
        }

        private void HandleClientMessage(string msg, IPEndPoint remote)
        {
            if (msg.StartsWith(MSG_ACCEPT))
            {
                _mySlotIndex = int.Parse(msg.Substring(MSG_ACCEPT.Length));
                _hostEndpoint = remote;
                _state = LobbyState.ClientLobby;
            }
            else if (msg.StartsWith(MSG_LOBBY))
            {
                ParseLobbyState(msg);
            }
            else if (msg.StartsWith(MSG_START))
            {
                var startParts = msg.Substring(MSG_START.Length).Split('|');
                ushort gamePort = ushort.Parse(startParts[0]);
                int seed = startParts.Length > 1 ? int.Parse(startParts[1]) : 12345;
                int lockstepPort = startParts.Length > 2 ? int.Parse(startParts[2]) : gamePort + 1;
                bool crystalCurse = startParts.Length > 3 ? int.Parse(startParts[3]) != 0 : true;
                GameSettings.CrystalCurseEnabled = crystalCurse;
                StartAsClient(remote.Address.ToString(), gamePort, seed, lockstepPort);
            }
        }

        private void JoinGame(DiscoveredGame game)
        {
            _state = LobbyState.Connecting;
            string msg = $"{MSG_JOIN}{_playerName}|{_clientPrivatePort}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            _clientBroadcastSocket.Send(data, data.Length, game.Endpoint);
        }

        private void SyncLobbyState()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(MSG_LOBBY);
            sb.Append(LobbyConfig.ActiveSlotCount);
            // Map settings: layout|twoSides|mapSize|fogOfWar|crystalCurse
            sb.Append($"|{(int)_layout}|{(int)_twoSides}|{_mapHalfSize}|{(_fogOfWar ? 1 : 0)}|{(_crystalCurse ? 1 : 0)}");

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                sb.Append($"|{(int)slot.Type},{slot.PlayerName},{(int)slot.AIDifficulty},{LobbyConfig.Slots[i].ColorIndex}");
            }

            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                if (slot.Type == SlotType.Human && slot.Endpoint != null)
                    _hostSocket.Send(data, data.Length, new IPEndPoint(slot.Endpoint.Address, slot.ClientPort));
            }
        }

        private void ParseLobbyState(string msg)
        {
            var parts = msg.Substring(MSG_LOBBY.Length).Split('|');
            if (parts.Length < 6) return; // slotCount + 5 map settings minimum

            int slotCount = int.Parse(parts[0]);
            LobbyConfig.ActiveSlotCount = slotCount;

            // Parse map settings
            if (parts.Length > 1) _layout = (SpawnLayout)int.Parse(parts[1]);
            if (parts.Length > 2) _twoSides = (TwoSidesPreset)int.Parse(parts[2]);
            if (parts.Length > 3) _mapHalfSize = int.Parse(parts[3]);
            if (parts.Length > 4) _fogOfWar = int.Parse(parts[4]) != 0;
            if (parts.Length > 5) _crystalCurse = int.Parse(parts[5]) != 0;

            // Parse slot data (starts at index 6)
            for (int i = 0; i < slotCount && i + 6 < parts.Length; i++)
            {
                var slotParts = parts[i + 6].Split(',');
                if (slotParts.Length >= 3)
                {
                    _networkSlots[i].Type = (SlotType)int.Parse(slotParts[0]);
                    _networkSlots[i].PlayerName = slotParts[1];
                    _networkSlots[i].AIDifficulty = (LobbyAIDifficulty)int.Parse(slotParts[2]);
                    if (slotParts.Length >= 4)
                        LobbyConfig.Slots[i].ColorIndex = int.Parse(slotParts[3]);
                }
            }
        }

        private int FindEmptySlot()
        {
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (_networkSlots[i].Type == SlotType.Empty)
                    return i;
            }
            return -1;
        }

        private void StartMultiplayerGame()
        {
            // Apply settings
            GameSettings.SpawnLayout = _layout;
            GameSettings.TwoSides = _twoSides;
            GameSettings.MapHalfSize = _mapHalfSize;
            GameSettings.FogOfWarEnabled = _fogOfWar;
            GameSettings.CrystalCurseEnabled = _crystalCurse;
            GameSettings.IsMultiplayer = true;
            GameSettings.NetworkRole = NetworkRole.Server;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TotalPlayers = LobbyConfig.ActiveSlotCount;

            // Sync lobby network slot types into LobbyConfig so AI bootstrap
            // knows which factions are human-controlled
            GameSettings.FactionToPlayerMapping.Clear();
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                LobbyConfig.Slots[i].Type = _networkSlots[i].Type;
                if (_networkSlots[i].Type == SlotType.Human)
                {
                    GameSettings.FactionToPlayerMapping[LobbyConfig.Slots[i].Faction] = (ulong)i;
                }
            }

            // Set shared random seed so all clients generate the same world
            GameSettings.SpawnSeed = _spawnSeed;

            // Create LockstepBootstrap to carry config across scene load
            int lockstepPort = _port + 1;
            var bootstrap = CreateLockstepBootstrap();
            bootstrap.ConfigureAsHost(lockstepPort, new List<RemotePlayerInfo>());
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                if (slot.Type == SlotType.Human && slot.Endpoint != null)
                {
                    bootstrap.AddRemotePlayer(
                        slot.Endpoint.Address.ToString(),
                        lockstepPort + i,
                        LobbyConfig.Slots[i].Faction);
                }
            }

            // Apply color selections before scene load
            LobbyConfig.ApplyColorSelections();

            // Notify clients — include seed, lockstep port, and crystal curse flag
            string msg = $"{MSG_START}{_port}|{_spawnSeed}|{lockstepPort}|{(_crystalCurse ? 1 : 0)}";
            byte[] data = Encoding.UTF8.GetBytes(msg);

            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                if (slot.Type == SlotType.Human && slot.Endpoint != null)
                {
                    _hostSocket.Send(data, data.Length, new IPEndPoint(slot.Endpoint.Address, slot.ClientPort));
                }
            }

            Cleanup();
            SceneManager.LoadScene(GameSceneName);
        }

        private void StartAsClient(string hostIp, ushort port, int seed, int lockstepPort)
        {
            GameSettings.IsMultiplayer = true;
            GameSettings.NetworkRole = NetworkRole.Client;
            GameSettings.LocalPlayerFaction = LobbyConfig.Slots[_mySlotIndex].Faction;
            GameSettings.TotalPlayers = LobbyConfig.ActiveSlotCount;
            GameSettings.SpawnSeed = seed;
            GameSettings.SpawnLayout = _layout;
            GameSettings.TwoSides = _twoSides;
            GameSettings.MapHalfSize = _mapHalfSize;
            GameSettings.FogOfWarEnabled = _fogOfWar;

            // Sync lobby network slot types into LobbyConfig so AI bootstrap
            // knows which factions are human-controlled
            GameSettings.FactionToPlayerMapping.Clear();
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                LobbyConfig.Slots[i].Type = _networkSlots[i].Type;
                if (_networkSlots[i].Type == SlotType.Human)
                {
                    GameSettings.FactionToPlayerMapping[LobbyConfig.Slots[i].Faction] = (ulong)i;
                }
            }

            // Create LockstepBootstrap to carry config across scene load
            int clientPort = lockstepPort + _mySlotIndex;
            var bootstrap = CreateLockstepBootstrap();
            bootstrap.ConfigureAsClient(hostIp, lockstepPort, clientPort, _mySlotIndex, GameSettings.LocalPlayerFaction);

            LobbyConfig.ApplyColorSelections();

            Cleanup();
            SceneManager.LoadScene(GameSceneName);
        }

        private void Cleanup()
        {
            DisposeSocket(ref _hostSocket);
            DisposeSocket(ref _clientBroadcastSocket);
            DisposeSocket(ref _clientPrivateSocket);

            _discoveredGames.Clear();
            _isHost = false;
            _mySlotIndex = -1;
        }

        /// <summary>
        /// Create a LockstepBootstrap that persists across scene loads.
        /// Carries multiplayer configuration from lobby to game scene.
        /// </summary>
        private static TheWaningBorder.Multiplayer.LockstepBootstrap CreateLockstepBootstrap()
        {
            // Destroy existing instance if any (from previous game)
            if (TheWaningBorder.Multiplayer.LockstepBootstrap.Instance != null)
            {
                Destroy(TheWaningBorder.Multiplayer.LockstepBootstrap.Instance.gameObject);
            }

            var go = new GameObject("LockstepBootstrap");
            return go.AddComponent<TheWaningBorder.Multiplayer.LockstepBootstrap>();
        }

        /// <summary>
        /// Properly close and dispose a UdpClient, releasing the port immediately.
        /// </summary>
        private static void DisposeSocket(ref UdpClient socket)
        {
            if (socket == null) return;
            try { socket.Client?.Close(); } catch { }
            try { socket.Close(); } catch { }
            try { socket.Dispose(); } catch { }
            socket = null;
        }

        /// <summary>
        /// Create a UDP broadcast socket bound to a specific port.
        /// Builds a raw Socket with ReuseAddress BEFORE binding, then wraps
        /// it in UdpClient(AddressFamily) which does NOT auto-bind.
        /// </summary>
        private static UdpClient CreateBroadcastSocket(int port)
        {
            // Create raw socket — NOT bound yet
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            socket.ReceiveTimeout = 1;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            // UdpClient(AddressFamily) creates an internal socket but does NOT bind it.
            // We close that unused socket and swap in our pre-bound one.
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Close();
            udp.Client = socket;
            return udp;
        }
    }
}