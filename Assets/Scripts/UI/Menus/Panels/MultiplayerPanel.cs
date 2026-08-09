// MultiplayerPanel.cs
// uGUI controller for the Multiplayer panel (scene GameObjects under
// UI_Canvas, scaffolded once by MenuPanelsBuilder and then hand-editable).
// Five panes toggled by the state machine: choice (host/join), host setup,
// browse (LAN discovery + direct IP), lobby (skirmish-style: map preview +
// options left, player slots right), connecting.
//
// Networking is byte-for-byte the protocol every previous lobby spoke:
//   TWB_GAME|GameName|HostName|GamePort    (Host -> Broadcast)
//   TWB_JOIN|PlayerName|ClientPort         (Client -> Host)
//   TWB_ACCEPT|SlotIndex                   (Host -> Client)
//   TWB_LOBBY|SlotCount|Slot0|Slot1|...    (Host -> Client)
//   TWB_COLOR|SlotIndex|ColorIndex         (Client -> Host)
//   TWB_LEAVE|SlotIndex                    (Client -> Host)
//   TWB_START|Port                         (Host -> Client)
// Legacy spawn fields (layout / sides / map size) are still sent and parsed
// for protocol compatibility but have no UI — every map is hand-authored.
// Sockets pump in Update(); OnDisable releases the ports.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Core.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class MultiplayerPanel : MonoBehaviour
    {
        private const int BROADCAST_PORT = 47515;
        private const float BROADCAST_INTERVAL = 1.0f;
        private const float LOBBY_SYNC_INTERVAL = 0.5f;
        private const float DISCOVERY_TIMEOUT = 5.0f;
        private const float UI_REFRESH_INTERVAL = 0.5f;

        private const string MSG_GAME = "TWB_GAME|";
        private const string MSG_JOIN = "TWB_JOIN|";
        private const string MSG_LOBBY = "TWB_LOBBY|";
        private const string MSG_START = "TWB_START|";
        private const string MSG_ACCEPT = "TWB_ACCEPT|";
        private const string MSG_COLOR = "TWB_COLOR|";
        private const string MSG_LEAVE = "TWB_LEAVE|";

        // TWB_JOIN is a single UDP datagram; retry it while Connecting and
        // give up with an error instead of hanging on "Please wait..." forever.
        private const float JOIN_RETRY_INTERVAL = 1.0f;
        private const float JOIN_TIMEOUT = 6.0f;

        private static readonly string[] DifficultyNames = { "EASY", "NORMAL", "HARD", "EXPERT" };
        private static readonly Color PillOn  = new Color(0.690f, 0.525f, 0.173f);
        private static readonly Color PillOff = new Color(0.086f, 0.118f, 0.141f);
        private static readonly Color TextDim = new Color(0.784f, 0.824f, 0.808f, 0.60f);

        private enum LobbyState
        {
            MainChoice,
            HostSetup,
            HostLobby,
            BrowseGames,
            ClientLobby,
            Connecting
        }

        private class DiscoveredGame
        {
            public string GameName;
            public string HostName;
            public ushort Port;
            public IPEndPoint Endpoint;
            public float LastSeen;
        }

        private class NetworkSlot
        {
            public SlotType Type = SlotType.Empty;
            public string PlayerName = "";
            public IPEndPoint Endpoint;
            public ushort ClientPort;
            public LobbyAIDifficulty AIDifficulty = LobbyAIDifficulty.Normal;
        }

        // ── Inspector wiring (assigned by MenuPanelsBuilder) ────────────

        [Header("Panes")]
        public GameObject PaneChoice;
        public GameObject PaneHostSetup;
        public GameObject PaneBrowse;
        public GameObject PaneLobby;
        public GameObject PaneConnecting;

        [Header("Choice")]
        public Button HostButton;
        public Button JoinButton;

        [Header("Host setup")]
        public TMP_InputField GameNameField;
        public TMP_InputField PlayerNameField;
        public TMP_InputField PortField;
        public Button PlayersMinus;
        public Button PlayersPlus;
        public TMP_Text PlayersValue;
        public Button CreateButton;

        [Header("Browse")]
        public RectTransform GamesContent;
        public GameObject GameRowTemplate; // inactive: TMP label + "JoinButton"
        public TMP_InputField IpField;
        public TMP_InputField DirectPortField;
        public Button DirectJoinButton;

        [Header("Lobby")]
        public TMP_Text LobbyTitle;
        public MapPreviewWidget MapPreview;
        public Button PrevMapButton;
        public Button NextMapButton;
        public Button FogToggle;
        public TMP_Text FogState;
        public Button BorderToggle;
        public TMP_Text BorderState;
        public RectTransform SlotsContent;
        public GameObject SlotRowTemplate;

        [Header("Connecting")]
        public TMP_Text ConnectingLabel;

        [Header("Footer")]
        public Button BackButton;
        public TMP_Text BackLabel;
        public Button StartButton;
        public TMP_Text ErrorText;

        // ── Lobby state ─────────────────────────────────────────────────

        private LobbyState _state = LobbyState.MainChoice;
        private string _gameName = "My Game";
        private string _playerName = Environment.MachineName;
        private ushort _port = 7979;

        private int _selectedMapIndex;
        private SpawnLayout _layout;             // protocol compat, no UI
        private TwoSidesPreset _twoSides;        // protocol compat, no UI
        private int _spawnSeed;
        private bool _fogOfWar;
        private int _mapHalfSize;                // protocol compat, no UI
        private bool _borderEnabled;

        // ── Network state ───────────────────────────────────────────────

        private UdpClient _hostSocket;

        // Host-only unicast socket, bound EXCLUSIVELY to the game port. All
        // lobby control traffic (JOIN/ACCEPT/COLOR/LEAVE/LOBBY/START) rides
        // this socket; _hostSocket stays discovery-broadcast only. Reason:
        // every instance binds 47515 with ReuseAddress, and Windows hands a
        // UNICAST datagram sent to a shared port to only ONE of the bound
        // sockets — with two instances on one PC the client's join usually
        // landed back on the client itself and the host never saw it.
        private UdpClient _joinSocket;

        private UdpClient _clientBroadcastSocket;
        private UdpClient _clientPrivateSocket;
        private ushort _clientPrivatePort;
        private bool _isHost;
        private int _mySlotIndex = -1;
        private IPEndPoint _hostEndpoint;
        private readonly List<DiscoveredGame> _discoveredGames = new List<DiscoveredGame>();
        private readonly NetworkSlot[] _networkSlots = new NetworkSlot[8];
        private float _lastBroadcast;
        private float _lastLobbySync;
        private float _lastUiRefresh;
        private string _error;
        private string _directIp = "127.0.0.1";
        private string _directPort = "7979";
        private DiscoveredGame _pendingJoin;
        private float _joinFirstSent;
        private float _joinLastSent;

        private LobbyState _renderedState = (LobbyState)(-1);
        private int _renderedMapIndex = -1;
        private bool _wired;

        // ── Lifecycle ───────────────────────────────────────────────────

        private void OnEnable()
        {
            GameSettings.IsMultiplayer = true;
            LobbyConfig.SetupMultiplayer(GameSettings.TotalPlayers);

            for (int i = 0; i < 8; i++)
                _networkSlots[i] = new NetworkSlot();
            _spawnSeed = UnityEngine.Random.Range(1, 99999);

            _selectedMapIndex = Mathf.Max(0, MapRegistry.IndexOf(GameSettings.SelectedMapScene));
            _layout = GameSettings.SpawnLayout;
            _twoSides = GameSettings.TwoSides;
            _fogOfWar = GameSettings.FogOfWarEnabled;
            _mapHalfSize = GameSettings.MapHalfSize;
            _borderEnabled = GameSettings.BorderEnabled;
            _error = null;
            _renderedState = (LobbyState)(-1);
            _renderedMapIndex = -1;

            Wire();
            SetState(LobbyState.MainChoice);
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void Update()
        {
            if (_isHost) HostUpdate();
            else ClientUpdate();

            float now = Time.realtimeSinceStartup;
            _discoveredGames.RemoveAll(g => now - g.LastSeen > DISCOVERY_TIMEOUT);

            if (_state == LobbyState.Connecting && _pendingJoin != null)
            {
                if (now - _joinFirstSent > JOIN_TIMEOUT)
                {
                    _pendingJoin = null;
                    _error = "No response from host.";
                    SetState(LobbyState.BrowseGames);
                }
                else if (now - _joinLastSent >= JOIN_RETRY_INTERVAL)
                {
                    _joinLastSent = now;
                    SendJoinRequest(_pendingJoin);
                }
            }

            // Network handlers can move the state machine (TWB_ACCEPT ->
            // ClientLobby); rebuild right away, otherwise throttle.
            if (_state != _renderedState || now - _lastUiRefresh >= UI_REFRESH_INTERVAL)
            {
                _lastUiRefresh = now;
                RefreshUi();
            }
        }

        // ── Wiring ──────────────────────────────────────────────────────

        private void Wire()
        {
            if (_wired) return;
            _wired = true;

            if (HostButton != null) HostButton.onClick.AddListener(() => SetState(LobbyState.HostSetup));
            if (JoinButton != null) JoinButton.onClick.AddListener(() =>
            {
                StartClient();
                SetState(LobbyState.BrowseGames);
            });

            if (GameNameField != null)
            {
                GameNameField.text = _gameName;
                GameNameField.onValueChanged.AddListener(v => _gameName = v);
            }
            if (PlayerNameField != null)
            {
                PlayerNameField.text = _playerName;
                PlayerNameField.onValueChanged.AddListener(v => _playerName = v);
            }
            if (PortField != null)
            {
                PortField.text = _port.ToString();
                PortField.onValueChanged.AddListener(v =>
                {
                    if (ushort.TryParse(v, out ushort p)) _port = p;
                });
            }
            if (PlayersMinus != null) PlayersMinus.onClick.AddListener(() => BumpPlayers(-1));
            if (PlayersPlus != null) PlayersPlus.onClick.AddListener(() => BumpPlayers(+1));
            if (CreateButton != null) CreateButton.onClick.AddListener(() =>
            {
                StartHost();
                SetState(LobbyState.HostLobby);
            });

            if (IpField != null)
            {
                IpField.text = _directIp;
                IpField.onValueChanged.AddListener(v => _directIp = v);
            }
            if (DirectPortField != null)
            {
                DirectPortField.text = _directPort;
                DirectPortField.onValueChanged.AddListener(v => _directPort = v);
            }
            if (DirectJoinButton != null) DirectJoinButton.onClick.AddListener(JoinDirect);

            if (PrevMapButton != null) PrevMapButton.onClick.AddListener(() => CycleMap(-1));
            if (NextMapButton != null) NextMapButton.onClick.AddListener(() => CycleMap(+1));
            if (FogToggle != null) FogToggle.onClick.AddListener(() =>
            {
                if (!_isHost) return;
                _fogOfWar = !_fogOfWar;
                SyncPills();
            });
            if (BorderToggle != null) BorderToggle.onClick.AddListener(() =>
            {
                if (!_isHost) return;
                _borderEnabled = !_borderEnabled;
                SyncPills();
            });

            if (BackButton != null) BackButton.onClick.AddListener(BackAction);
            if (StartButton != null) StartButton.onClick.AddListener(StartMultiplayerGame);
        }

        private void BumpPlayers(int delta)
        {
            int next = LobbyConfig.ActiveSlotCount + delta;
            if (next >= 2 && next <= 8) LobbyConfig.ActiveSlotCount = next;
            if (PlayersValue != null) PlayersValue.text = LobbyConfig.ActiveSlotCount.ToString();
        }

        private void BackAction()
        {
            switch (_state)
            {
                case LobbyState.MainChoice:
                    Cleanup();
                    gameObject.SetActive(false); // back to the main menu
                    break;
                case LobbyState.HostSetup:
                    SetState(LobbyState.MainChoice);
                    break;
                case LobbyState.HostLobby:
                case LobbyState.BrowseGames:
                    Cleanup();
                    SetState(LobbyState.MainChoice);
                    break;
                case LobbyState.ClientLobby:
                case LobbyState.Connecting:
                    SendLeave();
                    _pendingJoin = null;
                    Cleanup();
                    StartClient();
                    SetState(LobbyState.BrowseGames);
                    break;
            }
        }

        // ── UI refresh / pane switching ─────────────────────────────────

        private void SetState(LobbyState state)
        {
            _state = state;
            RefreshUi();
        }

        private void RefreshUi()
        {
            if (ErrorText != null) ErrorText.text = _error ?? string.Empty;

            if (_state != _renderedState)
            {
                RebuildContent();
                return;
            }

            switch (_state)
            {
                case LobbyState.HostLobby:
                    RebuildSlots(isHost: true);
                    break;
                case LobbyState.ClientLobby:
                    RebuildSlots(isHost: false);
                    if (_selectedMapIndex != _renderedMapIndex) RefreshMap();
                    SyncPills();
                    break;
                case LobbyState.BrowseGames:
                    RebuildGames();
                    break;
                case LobbyState.Connecting:
                    if (ConnectingLabel != null)
                        ConnectingLabel.text = _error ?? "Please wait...";
                    break;
            }
        }

        private void RebuildContent()
        {
            _renderedState = _state;

            SetPane(PaneChoice, _state == LobbyState.MainChoice);
            SetPane(PaneHostSetup, _state == LobbyState.HostSetup);
            SetPane(PaneBrowse, _state == LobbyState.BrowseGames);
            SetPane(PaneLobby, _state == LobbyState.HostLobby || _state == LobbyState.ClientLobby);
            SetPane(PaneConnecting, _state == LobbyState.Connecting);

            switch (_state)
            {
                case LobbyState.MainChoice:
                    SetFooter("< MAIN MENU", startVisible: false);
                    break;
                case LobbyState.HostSetup:
                    SetFooter("< BACK", startVisible: false);
                    if (PlayersValue != null) PlayersValue.text = LobbyConfig.ActiveSlotCount.ToString();
                    break;
                case LobbyState.HostLobby:
                    SetFooter("CANCEL LOBBY", startVisible: true);
                    if (LobbyTitle != null) LobbyTitle.text = $"HOSTING: {_gameName.ToUpperInvariant()}";
                    SetMapButtonsVisible(true);
                    RefreshMap();
                    SyncPills();
                    RebuildSlots(isHost: true);
                    break;
                case LobbyState.BrowseGames:
                    SetFooter("< BACK", startVisible: false);
                    RebuildGames();
                    break;
                case LobbyState.ClientLobby:
                    SetFooter("LEAVE LOBBY", startVisible: false);
                    if (LobbyTitle != null) LobbyTitle.text = "LOBBY";
                    SetMapButtonsVisible(false); // client sees the host's pick read-only
                    RefreshMap();
                    SyncPills();
                    RebuildSlots(isHost: false);
                    break;
                case LobbyState.Connecting:
                    SetFooter("CANCEL", startVisible: false);
                    if (ConnectingLabel != null)
                        ConnectingLabel.text = _error ?? "Please wait...";
                    break;
            }
        }

        private static void SetPane(GameObject pane, bool visible)
        {
            if (pane != null && pane.activeSelf != visible) pane.SetActive(visible);
        }

        private void SetFooter(string backText, bool startVisible)
        {
            if (BackLabel != null) BackLabel.text = backText;
            else if (BackButton != null)
            {
                var t = BackButton.GetComponentInChildren<TMP_Text>(true);
                if (t != null) t.text = backText;
            }
            if (StartButton != null) StartButton.gameObject.SetActive(startVisible);
        }

        private void SetMapButtonsVisible(bool visible)
        {
            if (PrevMapButton != null) PrevMapButton.gameObject.SetActive(visible);
            if (NextMapButton != null) NextMapButton.gameObject.SetActive(visible);
        }

        private void CycleMap(int delta)
        {
            if (!_isHost) return;
            int count = MapRegistry.Maps.Count;
            if (count == 0) return;
            _selectedMapIndex = ((_selectedMapIndex + delta) % count + count) % count;
            RefreshMap();
        }

        private void RefreshMap()
        {
            _renderedMapIndex = _selectedMapIndex;
            if (MapPreview != null) MapPreview.Show(_selectedMapIndex);
        }

        private void SyncPills()
        {
            SyncPill(FogToggle, FogState, _fogOfWar);
            SyncPill(BorderToggle, BorderState, _borderEnabled);
        }

        private static void SyncPill(Button toggle, TMP_Text state, bool on)
        {
            if (state != null) state.text = on ? "ON" : "OFF";
            if (toggle != null && toggle.targetGraphic is Image img)
                img.color = on ? PillOn : PillOff;
        }

        // ── Dynamic lists: player slots + discovered games ──────────────

        private void RebuildSlots(bool isHost)
        {
            if (SlotsContent == null || SlotRowTemplate == null) return;

            for (int i = SlotsContent.childCount - 1; i >= 0; i--)
            {
                var child = SlotsContent.GetChild(i).gameObject;
                if (child != SlotRowTemplate) Destroy(child);
            }

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                BuildSlotRow(i, isHost);
        }

        private void BuildSlotRow(int index, bool isHost)
        {
            var slot = _networkSlots[index];
            int idx = index;

            var row = Instantiate(SlotRowTemplate, SlotsContent);
            row.SetActive(true);
            var t = row.transform;

            // Banner-colour strip — host cycles any slot; a client only its own
            // (cycles locally + sends TWB_COLOR to the host).
            var strip = t.Find("ColorStrip");
            if (strip != null)
            {
                Image stripImg = null;
                strip.TryGetComponent(out stripImg);
                if (stripImg != null) stripImg.color = LobbyConfig.Slots[index].GetFactionColor();
                if (strip.TryGetComponent(out Button stripBtn))
                {
                    bool canChange = isHost || index == _mySlotIndex;
                    stripBtn.interactable = canChange;
                    if (canChange)
                        stripBtn.onClick.AddListener(() =>
                        {
                            if (_isHost) CycleSlotColor(idx);
                            else SendColorChange(idx);
                            if (stripImg != null)
                                stripImg.color = LobbyConfig.Slots[idx].GetFactionColor();
                        });
                }
            }

            var chip = t.Find("TeamChip")?.GetComponentInChildren<TMP_Text>(true);
            if (chip != null) chip.text = $"TEAM {index + 1}";

            var badge = t.Find("HostBadge");
            var name = t.Find("NameLabel")?.GetComponent<TMP_Text>();
            var aiBtn = t.Find("AiButton")?.GetComponent<Button>();
            var diffBtn = t.Find("DifficultyButton")?.GetComponent<Button>();

            if (badge != null) badge.gameObject.SetActive(index == 0 && slot.Type == SlotType.Human);
            if (aiBtn != null) aiBtn.gameObject.SetActive(false);
            if (diffBtn != null) diffBtn.gameObject.SetActive(false);

            if (slot.Type == SlotType.Human)
            {
                if (name != null)
                {
                    string label = string.IsNullOrEmpty(slot.PlayerName) ? "Player" : slot.PlayerName;
                    if (index == _mySlotIndex && !_isHost) label += "  (YOU)";
                    name.text = label.ToUpperInvariant();
                    name.color = Color.white;
                    name.gameObject.SetActive(true);
                }
            }
            else if (slot.Type == SlotType.AI)
            {
                if (isHost)
                {
                    if (name != null) name.gameObject.SetActive(false);
                    if (aiBtn != null)
                    {
                        aiBtn.gameObject.SetActive(true);
                        SetButtonLabel(aiBtn, "AI");
                        aiBtn.onClick.AddListener(() =>
                        {
                            slot.Type = SlotType.Empty;
                            RebuildSlots(true);
                        });
                    }
                    if (diffBtn != null)
                    {
                        diffBtn.gameObject.SetActive(true);
                        SetButtonLabel(diffBtn, DifficultyNames[(int)slot.AIDifficulty]);
                        diffBtn.onClick.AddListener(() =>
                        {
                            slot.AIDifficulty = (LobbyAIDifficulty)(((int)slot.AIDifficulty + 1) % 4);
                            RebuildSlots(true);
                        });
                    }
                }
                else if (name != null)
                {
                    name.text = $"AI · {DifficultyNames[(int)slot.AIDifficulty]}";
                    name.color = TextDim;
                    name.gameObject.SetActive(true);
                }
            }
            else // Empty
            {
                if (isHost)
                {
                    if (name != null)
                    {
                        name.text = "waiting for player…";
                        name.color = TextDim;
                        name.gameObject.SetActive(true);
                    }
                    if (aiBtn != null)
                    {
                        aiBtn.gameObject.SetActive(true);
                        SetButtonLabel(aiBtn, "OPEN");
                        aiBtn.onClick.AddListener(() =>
                        {
                            slot.Type = SlotType.AI;
                            RebuildSlots(true);
                        });
                    }
                }
                else if (name != null)
                {
                    name.text = "OPEN";
                    name.color = TextDim;
                    name.gameObject.SetActive(true);
                }
            }
        }

        private static void SetButtonLabel(Button btn, string text)
        {
            var t = btn.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = text;
        }

        private void RebuildGames()
        {
            if (GamesContent == null || GameRowTemplate == null) return;

            for (int i = GamesContent.childCount - 1; i >= 0; i--)
            {
                var child = GamesContent.GetChild(i).gameObject;
                if (child != GameRowTemplate) Destroy(child);
            }

            if (_discoveredGames.Count == 0)
            {
                var row = Instantiate(GameRowTemplate, GamesContent);
                row.SetActive(true);
                var lbl = row.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null) { lbl.text = "Searching for games…"; lbl.color = TextDim; }
                var joinT = row.transform.Find("JoinButton");
                if (joinT != null) joinT.gameObject.SetActive(false);
                return;
            }

            foreach (var game in _discoveredGames)
            {
                var g = game;
                var row = Instantiate(GameRowTemplate, GamesContent);
                row.SetActive(true);
                var lbl = row.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null) lbl.text = $"{g.GameName.ToUpperInvariant()}  ·  {g.HostName}";
                var join = row.transform.Find("JoinButton")?.GetComponent<Button>();
                if (join != null) join.onClick.AddListener(() => JoinGame(g));
            }
        }

        private void JoinDirect()
        {
            // Be forgiving: empty and "localhost" mean this machine (the
            // common two-instances-on-one-PC test), and anything that is not
            // a dotted address gets a DNS lookup before we reject it.
            string raw = (_directIp ?? "").Trim();
            if (raw.Length == 0 || raw.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                raw = "127.0.0.1";

            if (!IPAddress.TryParse(raw, out var addr))
            {
                try
                {
                    addr = Dns.GetHostAddresses(raw)
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                }
                catch { addr = null; }

                if (addr == null)
                {
                    _error = $"Invalid IP address: {_directIp}";
                    RefreshUi();
                    return;
                }
            }
            if (!ushort.TryParse(_directPort.Trim(), out ushort port) || port == 0)
            {
                _error = $"Invalid port: {_directPort}";
                RefreshUi();
                return;
            }
            var game = new DiscoveredGame
            {
                GameName = "Direct",
                HostName = _directIp,
                Port = port,
                Endpoint = new IPEndPoint(addr, port),
                LastSeen = Time.realtimeSinceStartup
            };
            JoinGame(game);
            RefreshUi();
        }

        // ── NETWORKING — identical protocol to every previous lobby ─────

        private void StartHost()
        {
            _isHost = true;
            _mySlotIndex = 0;

            _networkSlots[0].Type = SlotType.Human;
            _networkSlots[0].PlayerName = _playerName;

            for (int i = 1; i < 8; i++)
            {
                _networkSlots[i].Type = i < LobbyConfig.ActiveSlotCount ? SlotType.AI : SlotType.Empty;
            }

            try
            {
                _hostSocket = CreateBroadcastSocket(BROADCAST_PORT);

                // Exclusive bind — if another instance already hosts on this
                // port the user must pick a different one, which is exactly
                // the signal we want instead of silent shared-port misrouting.
                _joinSocket = new UdpClient(_port);
            }
            catch (SocketException se)
            {
                string hint = se.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"Port {BROADCAST_PORT} or {_port} already in use. Close other game instances or pick another port."
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
                _clientBroadcastSocket = CreateBroadcastSocket(BROADCAST_PORT);
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

            float now = Time.realtimeSinceStartup;

            if (now - _lastBroadcast >= BROADCAST_INTERVAL)
            {
                _lastBroadcast = now;
                BroadcastGameInfo();
            }

            // Receive messages (guard the socket in the condition — a handler
            // can dispose+null it mid-loop). The broadcast socket is still
            // drained as a fallback for stray control messages, but the join
            // socket is the real lobby-control inbox.
            while (_hostSocket != null && _hostSocket.Available > 0)
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

            while (_joinSocket != null && _joinSocket.Available > 0)
            {
                try
                {
                    IPEndPoint remote = null;
                    byte[] data = _joinSocket.Receive(ref remote);
                    string msg = Encoding.UTF8.GetString(data);
                    HandleHostMessage(msg, remote);
                }
                catch { }
            }

            if (now - _lastLobbySync >= LOBBY_SYNC_INTERVAL)
            {
                _lastLobbySync = now;
                SyncLobbyState();
            }
        }

        private void ClientUpdate()
        {
            // NOTE: a handler (e.g. TWB_START -> StartAsClient -> Cleanup) can
            // dispose+null these sockets mid-loop, so the null-check must be
            // IN the while condition.
            if (_clientBroadcastSocket != null)
            {
                while (_clientBroadcastSocket != null && _clientBroadcastSocket.Available > 0)
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

            if (_clientPrivateSocket != null)
            {
                while (_clientPrivateSocket != null && _clientPrivateSocket.Available > 0)
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

        /// <summary>Host-to-client unicast. Prefers the exclusively-bound
        /// join socket so replies never originate from the shared broadcast
        /// port (clients answer to the source endpoint they saw).</summary>
        private void HostSend(byte[] data, IPEndPoint target)
        {
            var socket = _joinSocket ?? _hostSocket;
            if (socket == null) return;
            try { socket.Send(data, data.Length, target); } catch { }
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

                    // A join can arrive more than once (lost ACCEPT, client
                    // retry) — the same endpoint keeps its ONE slot instead of
                    // consuming a fresh one as a ghost human each time.
                    for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
                    {
                        var existing = _networkSlots[i];
                        if (existing.Type == SlotType.Human && existing.Endpoint != null
                            && existing.Endpoint.Address.Equals(remote.Address)
                            && existing.ClientPort == clientPort)
                        {
                            string reAccept = $"{MSG_ACCEPT}{i}";
                            byte[] reData = Encoding.UTF8.GetBytes(reAccept);
                            HostSend(reData, new IPEndPoint(remote.Address, clientPort));
                            return;
                        }
                    }

                    int slot = FindEmptySlot();
                    if (slot >= 0)
                    {
                        _networkSlots[slot].Type = SlotType.Human;
                        _networkSlots[slot].PlayerName = playerName;
                        _networkSlots[slot].Endpoint = remote;
                        _networkSlots[slot].ClientPort = clientPort;

                        string accept = $"{MSG_ACCEPT}{slot}";
                        byte[] data = Encoding.UTF8.GetBytes(accept);
                        HostSend(data, new IPEndPoint(remote.Address, clientPort));
                    }
                }
            }
            else if (msg.StartsWith(MSG_LEAVE))
            {
                var parts = msg.Substring(MSG_LEAVE.Length).Split('|');
                if (parts.Length >= 1 && int.TryParse(parts[0], out int leaveSlot)
                    && leaveSlot > 0 && leaveSlot < LobbyConfig.ActiveSlotCount)
                {
                    var s = _networkSlots[leaveSlot];
                    // Only the endpoint that owns the slot may free it.
                    if (s.Type == SlotType.Human && s.Endpoint != null
                        && s.Endpoint.Address.Equals(remote.Address)
                        && s.ClientPort == remote.Port)
                    {
                        s.Type = SlotType.AI;
                        s.PlayerName = "";
                        s.Endpoint = null;
                        s.ClientPort = 0;
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
                    game.LastSeen = Time.realtimeSinceStartup;
                }
            }
        }

        private void HandleClientMessage(string msg, IPEndPoint remote)
        {
            if (msg.StartsWith(MSG_ACCEPT))
            {
                _mySlotIndex = int.Parse(msg.Substring(MSG_ACCEPT.Length));
                _hostEndpoint = remote;
                _pendingJoin = null;
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
                bool borderEnabled = startParts.Length > 3 ? int.Parse(startParts[3]) != 0 : true;
                GameSettings.BorderEnabled = borderEnabled;
                GameSettings.SelectedMapScene = startParts.Length > 4 && !string.IsNullOrEmpty(startParts[4])
                    ? startParts[4]
                    : MapRegistry.Default.SceneName;
                StartAsClient(remote.Address.ToString(), gamePort, seed, lockstepPort);
            }
        }

        private void JoinGame(DiscoveredGame game)
        {
            _state = LobbyState.Connecting;
            _pendingJoin = game;
            _joinFirstSent = _joinLastSent = Time.realtimeSinceStartup;
            SendJoinRequest(game);
        }

        private void SendJoinRequest(DiscoveredGame game)
        {
            if (_clientPrivateSocket == null) return;
            // Target the host's GAME port (advertised in TWB_GAME / typed in
            // direct join), where the host listens exclusively — never the
            // shared broadcast port, which Windows may deliver to another
            // instance on the same machine. Send from the private socket so
            // the reply path is unambiguous too.
            var target = new IPEndPoint(game.Endpoint.Address, game.Port);
            string msg = $"{MSG_JOIN}{_playerName}|{_clientPrivatePort}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            try { _clientPrivateSocket.Send(data, data.Length, target); } catch { }
        }

        private void SyncLobbyState()
        {
            var sb = new StringBuilder();
            sb.Append(MSG_LOBBY);
            sb.Append(LobbyConfig.ActiveSlotCount);
            // Map settings: layout|twoSides|mapSize|fogOfWar|borderEnabled|mapScene
            var maps = MapRegistry.Maps;
            string mapScene = maps[_selectedMapIndex >= 0 && _selectedMapIndex < maps.Count
                ? _selectedMapIndex : 0].SceneName;
            sb.Append($"|{(int)_layout}|{(int)_twoSides}|{_mapHalfSize}|{(_fogOfWar ? 1 : 0)}|{(_borderEnabled ? 1 : 0)}|{mapScene}");

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
                    HostSend(data, new IPEndPoint(slot.Endpoint.Address, slot.ClientPort));
            }
        }

        private void ParseLobbyState(string msg)
        {
            var parts = msg.Substring(MSG_LOBBY.Length).Split('|');
            if (parts.Length < 7) return; // slotCount + 6 map settings minimum

            int slotCount = int.Parse(parts[0]);
            LobbyConfig.ActiveSlotCount = slotCount;

            if (parts.Length > 1) _layout = (SpawnLayout)int.Parse(parts[1]);
            if (parts.Length > 2) _twoSides = (TwoSidesPreset)int.Parse(parts[2]);
            if (parts.Length > 3) _mapHalfSize = int.Parse(parts[3]);
            if (parts.Length > 4) _fogOfWar = int.Parse(parts[4]) != 0;
            if (parts.Length > 5) _borderEnabled = int.Parse(parts[5]) != 0;
            if (parts.Length > 6)
                _selectedMapIndex = MapRegistry.IndexOf(parts[6]);

            for (int i = 0; i < slotCount && i + 7 < parts.Length; i++)
            {
                var slotParts = parts[i + 7].Split(',');
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
            // Open seats first...
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (_networkSlots[i].Type == SlotType.Empty)
                    return i;
            }
            // ...then AI seats. StartHost fills every active slot with AI, so
            // without this fallback a LAN joiner is never accepted until the
            // host manually opens a slot — they just hang on "Connecting".
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (_networkSlots[i].Type == SlotType.AI)
                    return i;
            }
            return -1;
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

        /// <summary>Tell the host we are leaving so our slot reverts to AI
        /// instead of lingering as a ghost human (which would block the seat
        /// and spawn an uncontrolled faction at start). Best-effort UDP —
        /// no-ops when the sockets are already down.</summary>
        private void SendLeave()
        {
            if (_clientPrivateSocket == null || _hostEndpoint == null || _mySlotIndex <= 0) return;
            string msg = $"{MSG_LEAVE}{_mySlotIndex}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            try { _clientPrivateSocket.Send(data, data.Length, _hostEndpoint); } catch { }
        }

        private void SendColorChange(int slotIndex)
        {
            CycleSlotColor(slotIndex);
            int newColor = LobbyConfig.Slots[slotIndex].ColorIndex;
            string msg = $"{MSG_COLOR}{slotIndex}|{newColor}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            if (_clientPrivateSocket != null && _hostEndpoint != null)
                _clientPrivateSocket.Send(data, data.Length, _hostEndpoint);
        }

        // ── START-MATCH FLOW — same GameSettings/LobbyConfig writes and
        // LockstepBootstrap wiring as every previous lobby. ───────────────

        private void StartMultiplayerGame()
        {
            GameSettings.SpawnLayout = _layout;
            GameSettings.TwoSides = _twoSides;
            GameSettings.MapHalfSize = _mapHalfSize;
            GameSettings.FogOfWarEnabled = _fogOfWar;
            GameSettings.BorderEnabled = _borderEnabled;
            GameSettings.IsMultiplayer = true;
            GameSettings.NetworkRole = NetworkRole.Server;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TotalPlayers = LobbyConfig.ActiveSlotCount;

            GameSettings.FactionToPlayerMapping.Clear();
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                LobbyConfig.Slots[i].Type = _networkSlots[i].Type;
                if (_networkSlots[i].Type == SlotType.Human)
                {
                    GameSettings.FactionToPlayerMapping[LobbyConfig.Slots[i].Faction] = (ulong)i;
                }
            }

            GameSettings.SpawnSeed = _spawnSeed;

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
                        LobbyConfig.Slots[i].Faction,
                        i);
                }
            }

            LobbyConfig.ApplyColorSelections();

            var mapList = MapRegistry.Maps;
            string sceneName = mapList[_selectedMapIndex >= 0 && _selectedMapIndex < mapList.Count
                ? _selectedMapIndex : 0].SceneName;
            GameSettings.SelectedMapScene = sceneName;

            string msg = $"{MSG_START}{_port}|{_spawnSeed}|{lockstepPort}|{(_borderEnabled ? 1 : 0)}|{sceneName}";
            byte[] data = Encoding.UTF8.GetBytes(msg);

            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                if (slot.Type == SlotType.Human && slot.Endpoint != null)
                {
                    HostSend(data, new IPEndPoint(slot.Endpoint.Address, slot.ClientPort));
                }
            }

            Cleanup();
            SceneManager.LoadScene(sceneName);
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

            GameSettings.FactionToPlayerMapping.Clear();
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                LobbyConfig.Slots[i].Type = _networkSlots[i].Type;
                if (_networkSlots[i].Type == SlotType.Human)
                {
                    GameSettings.FactionToPlayerMapping[LobbyConfig.Slots[i].Faction] = (ulong)i;
                }
            }

            int clientPort = lockstepPort + _mySlotIndex;
            var bootstrap = CreateLockstepBootstrap();

            // Every other human's commands arrive relayed through the host —
            // the lockstep layer must wait for their ticks too.
            var otherHumans = new List<int>();
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (i != _mySlotIndex && _networkSlots[i].Type == SlotType.Human)
                    otherHumans.Add(i);
            }

            bootstrap.ConfigureAsClient(hostIp, lockstepPort, clientPort, _mySlotIndex,
                GameSettings.LocalPlayerFaction, otherHumans);

            LobbyConfig.ApplyColorSelections();

            Cleanup();
            string clientScene = !string.IsNullOrEmpty(GameSettings.SelectedMapScene)
                ? GameSettings.SelectedMapScene
                : MapRegistry.Default.SceneName;
            SceneManager.LoadScene(clientScene);
        }

        private void Cleanup()
        {
            DisposeSocket(ref _hostSocket);
            DisposeSocket(ref _joinSocket);
            DisposeSocket(ref _clientBroadcastSocket);
            DisposeSocket(ref _clientPrivateSocket);

            _discoveredGames.Clear();
            _isHost = false;
            _mySlotIndex = -1;
        }

        /// <summary>Create a LockstepBootstrap that persists across scene loads.</summary>
        private static TheWaningBorder.Multiplayer.LockstepBootstrap CreateLockstepBootstrap()
        {
            if (TheWaningBorder.Multiplayer.LockstepBootstrap.Instance != null)
            {
                Destroy(TheWaningBorder.Multiplayer.LockstepBootstrap.Instance.gameObject);
            }

            var go = new GameObject("LockstepBootstrap");
            return go.AddComponent<TheWaningBorder.Multiplayer.LockstepBootstrap>();
        }

        /// <summary>Properly close and dispose a UdpClient, releasing the port immediately.</summary>
        private static void DisposeSocket(ref UdpClient socket)
        {
            if (socket == null) return;
            try { socket.Client?.Close(); } catch { }
            try { socket.Close(); } catch { }
            try { socket.Dispose(); } catch { }
            socket = null;
        }

        /// <summary>
        /// Create a UDP broadcast socket bound to a specific port. Builds a raw
        /// Socket with ReuseAddress BEFORE binding, then wraps it in
        /// UdpClient(AddressFamily) which does NOT auto-bind.
        /// </summary>
        private static UdpClient CreateBroadcastSocket(int port)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            socket.ReceiveTimeout = 1;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Close();
            udp.Client = socket;
            return udp;
        }
    }
}
