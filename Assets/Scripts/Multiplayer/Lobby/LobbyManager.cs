// LobbyManager.cs
// Core lobby state and network management
// Part of: Multiplayer/Lobby/

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using TheWaningBorder.Core.Config;

namespace TheWaningBorder.Multiplayer
{
    /// <summary>
    /// Manages multiplayer lobby state and networking.
    /// 
    /// Architecture:
    /// - Host listens on BROADCAST_PORT (27015) for discovery and join requests
    /// - Client uses two sockets:
    ///   1. Broadcast listener on 27015 (ReuseAddress) for game discovery
    ///   2. Private socket on random port for direct messages
    /// 
    /// Protocol:
    /// - TWB_GAME|GameName|HostName|GamePort         (Host → Broadcast)
    /// - TWB_JOIN|PlayerName|ClientPort              (Client → Host)
    /// - TWB_ACCEPT|SlotIndex                        (Host → Client)
    /// - TWB_LOBBY|SlotCount|Slot0|Slot1|...         (Host → Clients)
    /// - TWB_LEAVE|SlotIndex                         (Client → Host)
    /// - TWB_START|Port|FactionIndex|LockstepPort    (Host → Clients)
    /// </summary>
    public class LobbyManager
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ═══════════════════════════════════════════════════════════════════════
        
        public const int BROADCAST_PORT = 47515;
        public const float BROADCAST_INTERVAL = 1.0f;
        public const float LOBBY_SYNC_INTERVAL = 0.5f;
        public const float DISCOVERY_TIMEOUT = 5.0f;
        
        // Message prefixes
        public const string MSG_GAME = "TWB_GAME|";
        public const string MSG_JOIN = "TWB_JOIN|";
        public const string MSG_LOBBY = "TWB_LOBBY|";
        public const string MSG_LEAVE = "TWB_LEAVE|";
        public const string MSG_START = "TWB_START|";
        public const string MSG_ACCEPT = "TWB_ACCEPT|";
        public const string MSG_DISCOVER = "TWB_DISCOVER|";

        // ═══════════════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private UdpClient _hostSocket;
        private UdpClient _broadcastListener;
        private UdpClient _clientSocket;
        private int _clientPort;
        
        private bool _isHost;
        private bool _isConnected;
        private string _hostIP;
        private int _mySlotIndex = -1;
        
        private Dictionary<string, LobbyClientInfo> _connectedClients = new();
        private List<DiscoveredGame> _discoveredGames = new();
        private LobbySlot[] _slots = new LobbySlot[8];
        
        private float _lastBroadcastTime;
        private float _lastLobbySyncTime;
        
        private string _gameName;
        private string _playerName;
        private int _gamePort;

        // ═══════════════════════════════════════════════════════════════════════
        // EVENTS
        // ═══════════════════════════════════════════════════════════════════════
        
        public event Action<DiscoveredGame> OnGameDiscovered;
        public event Action<string> OnGameLost;
        public event Action<int> OnJoinAccepted;
        public event Action OnLobbyUpdated;
        public event Action<int, int> OnGameStarting; // lockstepPort, factionIndex
        public event Action<string> OnError;

        // ═══════════════════════════════════════════════════════════════════════
        // PROPERTIES
        // ═══════════════════════════════════════════════════════════════════════
        
        public bool IsHost => _isHost;
        public bool IsConnected => _isConnected;
        public int MySlotIndex => _mySlotIndex;
        public string HostIP => _hostIP;
        public LobbySlot[] Slots => _slots;
        public List<DiscoveredGame> DiscoveredGames => _discoveredGames;
        public Dictionary<string, LobbyClientInfo> ConnectedClients => _connectedClients;

        // ═══════════════════════════════════════════════════════════════════════
        // FIREWALL
        // ═══════════════════════════════════════════════════════════════════════

        private static bool _firewallChecked;

        /// <summary>
        /// Try to add Windows Firewall rules for the game's UDP ports.
        /// Runs once per session. Silently continues if it fails (user may not have admin rights).
        /// </summary>
        private static void EnsureFirewallRules()
        {
            if (_firewallChecked) return;
            _firewallChecked = true;

            try
            {
                // Add inbound UDP rule for broadcast port
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"The Waning Border Multiplayer\" dir=in action=allow protocol=UDP localport={BROADCAST_PORT} enable=yes",
                    UseShellExecute = true,
                    Verb = "runas",        // Request elevation
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC prompt — that's fine
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Create a UDP broadcast socket bound to a specific port.
        /// Uses UdpClient(port) which binds during construction, then enables broadcast.
        /// If that fails with AccessDenied, falls back to building a raw Socket
        /// with ExclusiveAddressUse=false before binding.
        /// </summary>
        private static UdpClient CreateBroadcastSocket(int port)
        {
            // Attempt 1: Simple UdpClient(port) — works in most cases
            try
            {
                var udp = new UdpClient(port);
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 1;
                return udp;
            }
            catch (SocketException)
            {
                // Fall through to raw socket approach
            }

            // Attempt 2: Raw socket with ExclusiveAddressUse=false for port sharing
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            socket.ReceiveTimeout = 1;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            // UdpClient(AddressFamily) does NOT auto-bind — safe to swap socket
            var fallback = new UdpClient(AddressFamily.InterNetwork);
            fallback.Client.Close();
            fallback.Client = socket;
            return fallback;
        }

        /// <summary>
        /// Properly close and dispose a UdpClient, releasing the port immediately.
        /// </summary>
        private static void DisposeSocket(ref UdpClient socket)
        {
            if (socket == null) return;
            try { socket.Client?.Shutdown(SocketShutdown.Both); } catch { }
            try { socket.Client?.Close(); } catch { }
            try { socket.Close(); } catch { }
            try { socket.Dispose(); } catch { }
            socket = null;
        }

        /// <summary>
        /// Format a SocketException into a user-friendly message.
        /// </summary>
        private string FormatSocketError(SocketException se)
        {
            return se.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse =>
                    $"Port {BROADCAST_PORT} in use. Close other game instances or restart Unity.",
                SocketError.AccessDenied =>
                    $"Port {BROADCAST_PORT} access denied. Run Unity as Administrator, or check Windows Firewall.",
                _ => $"{se.Message} (error {se.SocketErrorCode})"
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════════

        public LobbyManager()
        {
            for (int i = 0; i < 8; i++)
            {
                _slots[i] = new LobbySlot();
            }
        }

        /// <summary>
        /// Start hosting a game.
        /// </summary>
        public bool StartHost(string gameName, string playerName, int gamePort)
        {
            _gameName = gameName;
            _playerName = playerName;
            _gamePort = gamePort;
            _isHost = true;
            _mySlotIndex = 0;

            try
            {
                _hostSocket = CreateBroadcastSocket(BROADCAST_PORT);

                // Setup slot 0 as host
                _slots[0].Type = LobbySlotType.Human;
                _slots[0].PlayerName = playerName;
                _slots[0].ClientKey = "";

                return true;
            }
            catch (SocketException se)
            {
                OnError?.Invoke($"Network error: {FormatSocketError(se)}");
                return false;
            }
            catch (Exception e)
            {
                OnError?.Invoke($"Failed to start host: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start as client looking for games.
        /// </summary>
        public bool StartClient(string playerName)
        {
            _playerName = playerName;
            _isHost = false;
            _discoveredGames.Clear();

            try
            {
                // Broadcast listener for game discovery
                _broadcastListener = CreateBroadcastSocket(BROADCAST_PORT);

                // Private socket for direct messages
                _clientSocket = new UdpClient(0);
                _clientSocket.Client.ReceiveTimeout = 1;
                _clientPort = ((IPEndPoint)_clientSocket.Client.LocalEndPoint).Port;

                return true;
            }
            catch (SocketException se)
            {
                OnError?.Invoke($"Network error: {FormatSocketError(se)}");
                return false;
            }
            catch (Exception e)
            {
                OnError?.Invoke($"Failed to start client: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shutdown and cleanup all sockets.
        /// </summary>
        public void Shutdown()
        {
            DisposeSocket(ref _hostSocket);
            DisposeSocket(ref _broadcastListener);
            DisposeSocket(ref _clientSocket);
            
            _isHost = false;
            _isConnected = false;
            _mySlotIndex = -1;
            _connectedClients.Clear();
            _discoveredGames.Clear();
            
        }

        // ═══════════════════════════════════════════════════════════════════════
        // UPDATE (Call from MonoBehaviour.Update)
        // ═══════════════════════════════════════════════════════════════════════

        public void Update()
        {
            ReceiveMessages();
            
            if (_isHost)
            {
                UpdateHost();
            }
            else if (_isConnected)
            {
                UpdateClient();
            }
            
            CleanupStaleGames();
        }

        private void UpdateHost()
        {
            float time = Time.time;
            
            // Broadcast game availability
            if (time - _lastBroadcastTime >= BROADCAST_INTERVAL)
            {
                _lastBroadcastTime = time;
                BroadcastGameAvailability();
            }
            
            // Sync lobby state to clients
            if (time - _lastLobbySyncTime >= LOBBY_SYNC_INTERVAL)
            {
                _lastLobbySyncTime = time;
                BroadcastLobbyState();
            }
        }

        private void UpdateClient()
        {
            // Could add heartbeat here if needed
        }

        private void CleanupStaleGames()
        {
            var now = DateTime.Now;
            for (int i = _discoveredGames.Count - 1; i >= 0; i--)
            {
                if ((now - _discoveredGames[i].LastSeen).TotalSeconds > DISCOVERY_TIMEOUT)
                {
                    var game = _discoveredGames[i];
                    _discoveredGames.RemoveAt(i);
                    OnGameLost?.Invoke(game.IPAddress);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HOST OPERATIONS
        // ═══════════════════════════════════════════════════════════════════════

        private void BroadcastGameAvailability()
        {
            if (_hostSocket == null) return;

            try
            {
                string message = $"{MSG_GAME}{_gameName}|{_playerName}|{_gamePort}";
                byte[] data = Encoding.UTF8.GetBytes(message);
                _hostSocket.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, BROADCAST_PORT));
            }
            catch (Exception)
            {
            }
        }

        private void BroadcastLobbyState()
        {
            if (_hostSocket == null) return;

            int activeSlots = LobbyConfig.ActiveSlotCount;
            var sb = new StringBuilder();
            sb.Append(MSG_LOBBY);
            sb.Append(activeSlots);

            for (int i = 0; i < activeSlots; i++)
            {
                var slot = _slots[i];
                sb.Append($"|{(int)slot.Type},{slot.PlayerName},{(int)slot.AIDifficulty}");
            }

            string message = sb.ToString();
            byte[] data = Encoding.UTF8.GetBytes(message);

            foreach (var client in _connectedClients.Values)
            {
                try
                {
                    _hostSocket.Send(data, data.Length, client.IP, client.Port);
                }
                catch { }
            }
        }

        /// <summary>
        /// Start the game (host only).
        /// </summary>
        public void StartGame(int lockstepPort)
        {
            if (!_isHost) return;

            // Send start message to all clients
            foreach (var client in _connectedClients.Values)
            {
                try
                {
                    // Include faction index for the client
                    int factionIndex = (int)LobbyConfig.Slots[client.SlotIndex].Faction;
                    string message = $"{MSG_START}{lockstepPort}|{factionIndex}|{lockstepPort}";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    _hostSocket.Send(data, data.Length, client.IP, client.Port);
                    
                }
                catch (Exception)
            {
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CLIENT OPERATIONS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send join request to a discovered game.
        /// </summary>
        public void JoinGame(DiscoveredGame game)
        {
            if (_clientSocket == null) return;

            _hostIP = game.IPAddress;

            try
            {
                string message = $"{MSG_JOIN}{_playerName}|{_clientPort}";
                byte[] data = Encoding.UTF8.GetBytes(message);
                _clientSocket.Send(data, data.Length, game.IPAddress, BROADCAST_PORT);
                
            }
            catch (Exception e)
            {
                OnError?.Invoke($"Failed to join: {e.Message}");
            }
        }

        /// <summary>
        /// Leave the current lobby.
        /// </summary>
        public void LeaveLobby()
        {
            if (_clientSocket == null || string.IsNullOrEmpty(_hostIP)) return;

            try
            {
                string message = $"{MSG_LEAVE}{_mySlotIndex}";
                byte[] data = Encoding.UTF8.GetBytes(message);
                _clientSocket.Send(data, data.Length, _hostIP, BROADCAST_PORT);
            }
            catch { }

            _isConnected = false;
            _mySlotIndex = -1;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MESSAGE RECEIVING
        // ═══════════════════════════════════════════════════════════════════════

        private void ReceiveMessages()
        {
            if (_isHost)
            {
                ReceiveOnSocket(_hostSocket);
            }
            else
            {
                ReceiveOnSocket(_broadcastListener);
                ReceiveOnSocket(_clientSocket);
            }
        }

        private void ReceiveOnSocket(UdpClient socket)
        {
            if (socket == null) return;

            try
            {
                while (socket.Available > 0)
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = socket.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data);
                    ProcessMessage(message, remoteEP);
                }
            }
            catch (SocketException) { }
            catch (Exception)
            {
            }
        }

        private void ProcessMessage(string message, IPEndPoint sender)
        {
            string senderIP = sender.Address.ToString();
            int senderPort = sender.Port;

            if (message.StartsWith(MSG_GAME) && !_isHost)
            {
                ProcessGameBroadcast(message, senderIP);
            }
            else if (message.StartsWith(MSG_JOIN) && _isHost)
            {
                ProcessJoinRequest(message, senderIP, senderPort);
            }
            else if (message.StartsWith(MSG_ACCEPT) && !_isHost)
            {
                ProcessJoinAccepted(message, senderIP);
            }
            else if (message.StartsWith(MSG_LOBBY) && !_isHost)
            {
                ProcessLobbyUpdate(message);
            }
            else if (message.StartsWith(MSG_LEAVE) && _isHost)
            {
                ProcessLeaveRequest(message, senderIP, senderPort);
            }
            else if (message.StartsWith(MSG_START) && !_isHost)
            {
                ProcessGameStart(message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MESSAGE PROCESSING
        // ═══════════════════════════════════════════════════════════════════════

        private void ProcessGameBroadcast(string message, string senderIP)
        {
            // Format: TWB_GAME|GameName|HostName|GamePort
            string[] parts = message.Split('|');
            if (parts.Length < 4) return;

            string gameName = parts[1];
            string hostName = parts[2];
            if (!ushort.TryParse(parts[3], out ushort port)) return;

            var existing = _discoveredGames.Find(g => g.IPAddress == senderIP);
            if (existing != null)
            {
                existing.GameName = gameName;
                existing.HostName = hostName;
                existing.Port = port;
                existing.LastSeen = DateTime.Now;
            }
            else
            {
                var game = new DiscoveredGame
                {
                    GameName = gameName,
                    HostName = hostName,
                    IPAddress = senderIP,
                    Port = port,
                    LastSeen = DateTime.Now
                };
                _discoveredGames.Add(game);
                OnGameDiscovered?.Invoke(game);
            }
        }

        private void ProcessJoinRequest(string message, string senderIP, int senderPort)
        {
            // Format: TWB_JOIN|PlayerName|ClientPort
            string[] parts = message.Split('|');
            if (parts.Length < 3) return;

            string playerName = parts[1];
            if (!int.TryParse(parts[2], out int clientPrivatePort)) return;

            string clientKey = $"{senderIP}:{clientPrivatePort}";

            // Check if already connected
            if (_connectedClients.TryGetValue(clientKey, out var existingClient))
            {
                existingClient.LastSeen = DateTime.Now;
                
                // Re-send accept
                string acceptMsg = $"{MSG_ACCEPT}{existingClient.SlotIndex}";
                byte[] acceptData = Encoding.UTF8.GetBytes(acceptMsg);
                _hostSocket.Send(acceptData, acceptData.Length, senderIP, clientPrivatePort);
                return;
            }

            // Find available slot
            int assignedSlot = -1;
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (_slots[i].Type == LobbySlotType.Empty || _slots[i].Type == LobbySlotType.AI)
                {
                    if (string.IsNullOrEmpty(_slots[i].ClientKey) || _slots[i].Type == LobbySlotType.AI)
                    {
                        assignedSlot = i;
                        break;
                    }
                }
            }

            if (assignedSlot >= 0)
            {
                _slots[assignedSlot].Type = LobbySlotType.Human;
                _slots[assignedSlot].PlayerName = playerName;
                _slots[assignedSlot].ClientKey = clientKey;

                _connectedClients[clientKey] = new LobbyClientInfo
                {
                    PlayerName = playerName,
                    SlotIndex = assignedSlot,
                    IP = senderIP,
                    Port = clientPrivatePort,
                    Faction = LobbyConfig.Slots[assignedSlot].Faction,
                    LastSeen = DateTime.Now
                };

                // Send accept
                string acceptMsg = $"{MSG_ACCEPT}{assignedSlot}";
                byte[] data = Encoding.UTF8.GetBytes(acceptMsg);
                _hostSocket.Send(data, data.Length, senderIP, clientPrivatePort);

                OnLobbyUpdated?.Invoke();
            }
        }

        private void ProcessJoinAccepted(string message, string senderIP)
        {
            string[] parts = message.Split('|');
            if (parts.Length < 2) return;

            if (int.TryParse(parts[1], out int slotIndex))
            {
                _mySlotIndex = slotIndex;
                _hostIP = senderIP;
                _isConnected = true;
                
                GameSettings.IsMultiplayer = true;
                GameSettings.NetworkRole = NetworkRole.Client;
                
                OnJoinAccepted?.Invoke(slotIndex);
            }
        }

        private void ProcessLobbyUpdate(string message)
        {
            string[] parts = message.Split('|');
            if (parts.Length < 2) return;

            if (!int.TryParse(parts[1], out int slotCount)) return;

            LobbyConfig.ActiveSlotCount = slotCount;

            for (int i = 0; i < slotCount && i + 2 < parts.Length; i++)
            {
                string[] slotParts = parts[i + 2].Split(',');
                if (slotParts.Length >= 3)
                {
                    if (int.TryParse(slotParts[0], out int type))
                        _slots[i].Type = (LobbySlotType)type;
                    
                    _slots[i].PlayerName = slotParts[1];
                    
                    if (int.TryParse(slotParts[2], out int diff))
                        _slots[i].AIDifficulty = (LobbyAIDifficulty)diff;
                }
            }
            
            OnLobbyUpdated?.Invoke();
        }

        private void ProcessLeaveRequest(string message, string senderIP, int senderPort)
        {
            string[] parts = message.Split('|');
            if (parts.Length < 2) return;

            if (!int.TryParse(parts[1], out int slotIndex)) return;

            // Find and remove client
            string keyToRemove = null;
            foreach (var kvp in _connectedClients)
            {
                if (kvp.Value.SlotIndex == slotIndex)
                {
                    keyToRemove = kvp.Key;
                    break;
                }
            }

            if (keyToRemove != null)
            {
                _connectedClients.Remove(keyToRemove);
                _slots[slotIndex].Type = LobbySlotType.AI;
                _slots[slotIndex].PlayerName = "";
                _slots[slotIndex].ClientKey = "";
                
                OnLobbyUpdated?.Invoke();
            }
        }

        private void ProcessGameStart(string message)
        {
            // Format: TWB_START|Port|FactionIndex|LockstepPort
            string[] parts = message.Split('|');
            
            int lockstepPort = BROADCAST_PORT + 1;
            int factionIndex = _mySlotIndex;
            
            if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
                lockstepPort = port;
            
            if (parts.Length >= 3 && int.TryParse(parts[2], out int fi))
                factionIndex = fi;

            GameSettings.LocalPlayerFaction = (Faction)factionIndex;
            
            OnGameStarting?.Invoke(lockstepPort, factionIndex);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SUPPORTING TYPES (unique to Multiplayer networking)
    // LobbyConfig, PlayerSlot, LobbySlot, SlotType, LobbySlotType,
    // and LobbyAIDifficulty are defined in TheWaningBorder.Core.Config.LobbyTypes
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Information about a connected client.
    /// </summary>
    public class LobbyClientInfo
    {
        public string PlayerName;
        public int SlotIndex;
        public string IP;
        public int Port;
        public Faction Faction;
        public DateTime LastSeen;
    }

    /// <summary>
    /// A discovered game on the LAN.
    /// </summary>
    public class DiscoveredGame
    {
        public string GameName;
        public string HostName;
        public string IPAddress;
        public ushort Port;
        public DateTime LastSeen;
    }
}