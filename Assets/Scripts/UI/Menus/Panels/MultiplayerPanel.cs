// MultiplayerPanel.cs
// uGUI controller for the Multiplayer panel (scene GameObjects under
// UI_Canvas, scaffolded once by MenuPanelsBuilder and then hand-editable).
// Five panes toggled by the state machine: choice (host/join), host setup,
// browse (LAN discovery + direct IP), lobby (skirmish-style: map preview +
// options left, player slots right), connecting.
//
// Networking is byte-for-byte the protocol every previous lobby spoke:
//   TWB_GAME|GameName|HostName|GamePort|MapScene|SlotsUsed|SlotsTotal
//                                                  (Host -> Broadcast)
//   TWB_FIND|                                       (Client -> Broadcast; host answers TWB_GAME unicast)
//   TWB_JOIN|PlayerName|ClientPort|BuildFingerprint (Client -> Host)
//   TWB_ACCEPT|SlotIndex                            (Host -> Client)
//   TWB_REJECT|Reason                               (Host -> Client)
//   TWB_LOBBY|SlotCount|Layout|TwoSides|MapHalfSize|Fog|Border|MapScene|
//             MaxRes|StartAge|GameName|Slot0|Slot1|...   (Host -> Client)
//             ...where each SlotN is
//             Type,PlayerName,Difficulty,Colour,Team,Start,AIStrategy
//   TWB_COLOR|SlotIndex|ColorIndex                  (Client -> Host)
//   TWB_LEAVE|SlotIndex                             (Client -> Host)
//   TWB_START|Port|Seed|LockstepPort|Border|Scene|MatchSettingsBlob
//                                                   (Host -> Client)
//
// The trailing blob on TWB_START is MatchSettingsSync — every GameSettings
// value that shapes the simulated world. It is the ONLY thing that decides the
// client's start age, culture, starting resources and pathfinding cell size;
// before it existed those were read from whatever the client's own process had
// left in its statics, and the two peers built different worlds from tick 0.
// Legacy spawn fields (layout / sides / map size) are still sent and parsed
// for protocol compatibility but have no UI — every map is hand-authored.
// Sockets pump in Update(); OnDisable releases the ports.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
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
        /// <summary>Client -> Broadcast: "anyone hosting?" — the host answers
        /// with TWB_GAME unicast to the asker. Discovery must work in BOTH
        /// directions because either side's firewall can eat the other's
        /// broadcast; a unicast reply to a datagram the client just sent is the
        /// one inbound path consumer firewalls reliably allow.</summary>
        private const string MSG_FIND = "TWB_FIND|";
        private const string MSG_JOIN = "TWB_JOIN|";
        private const string MSG_LOBBY = "TWB_LOBBY|";
        private const string MSG_START = "TWB_START|";
        private const string MSG_ACCEPT = "TWB_ACCEPT|";
        private const string MSG_COLOR = "TWB_COLOR|";
        private const string MSG_LEAVE = "TWB_LEAVE|";
        /// <summary>Host -> Client: this join cannot work, here is why.</summary>
        private const string MSG_REJECT = "TWB_REJECT|";

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
            public string MapScene = "";
            public int SlotsUsed;
            public int SlotsTotal;

            /// <summary>A game with a seat free. A full lobby is listed —
            /// seeing it is useful — but it cannot be joined, which is what
            /// the browser gates CONNECT on.</summary>
            public bool HasRoom => SlotsTotal <= 0 || SlotsUsed < SlotsTotal;
            /// <summary>Endpoint came from a unicast TWB_FIND reply. That
            /// source address is the one the host's OWN routing table picked
            /// to reach us, so it wins over broadcast source addresses.</summary>
            public bool ViaProbeReply;
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
        // No player-count spinner. The lobby roster is the size control now -
        // an eight-rung ladder whose top-most free rung adds a player, exactly
        // like the skirmish roster. Asking for a count up front, before the
        // host has even seen the map, was a second source of truth for
        // something the roster already shows. See RebuildSlots.
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
        // Same two match options the skirmish lobby offers, from the same
        // tables (LobbyOptions) so an age label cannot promise one culture
        // here and another there. Host-only, like the pills.
        public TMP_Dropdown ResourcesDropdown;
        public TMP_Dropdown AgeDropdown;
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
        // Who you are is a profile setting now, not something re-typed in
        // every lobby: OnEnable reads it from PlayerProfile, and Settings owns
        // it. NOT initialised from PlayerProfile here — a field initialiser
        // runs in the MonoBehaviour CONSTRUCTOR, on Unity's serialization
        // thread, where most of the engine API is off-limits. It throws there,
        // and because the throw aborts the constructor every field declared
        // below this line stays null, which surfaces as a pile of unrelated
        // NullReferenceExceptions in OnEnable / Update / Cleanup.
        private string _playerName = "";
        /// <summary>
        /// The standard game port. Not a setting: there is no UI for it, and
        /// <see cref="BindGamePort"/> moves off it by itself when it is busy.
        /// Direct connect still shows a port, because a joiner reaching a host
        /// that broadcast cannot find has no other way to name it.
        /// </summary>
        private const ushort DefaultGamePort = 7979;

        /// <summary>How many ports BindGamePort will try before giving up.</summary>
        private const int GamePortAttempts = 8;

        private ushort _port = DefaultGamePort;

        private int _selectedMapIndex;
        private SpawnLayout _layout;             // protocol compat, no UI
        private TwoSidesPreset _twoSides;        // protocol compat, no UI
        private int _spawnSeed;
        private bool _fogOfWar;
        private int _mapHalfSize;                // protocol compat, no UI
        private bool _borderEnabled;
        private bool _maxResources;
        private SkirmishStartAge _startAge;

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

        /// <summary>
        /// Row the player has picked in the browse list, or null. Held as the
        /// DiscoveredGame itself, not the row GameObject: the list rebuilds on
        /// a timer while browsing, so any reference to a row is stale within
        /// the second. The advert handler updates an existing entry in place
        /// and only allocates for a genuinely new host, so this reference
        /// survives every refresh until the game actually goes away.
        /// </summary>
        private DiscoveredGame _selectedGame;
        private Color _gameRowBaseColor = new Color(0.08f, 0.11f, 0.13f, 1f);
        private bool _gameRowBaseCaptured;
        private readonly NetworkSlot[] _networkSlots = new NetworkSlot[8];
        private float _lastBroadcast;
        private float _lastFindProbe;
        private float _browseEnteredAt;
        private bool _loggedAdvertTargets;
        private float _lastLobbySync;
        private float _lastUiRefresh;
        private string _error;
        private string _directIp = "127.0.0.1";
        private string _directPort = DefaultGamePort.ToString();
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
            GameSettings.TutorialActive = false;   // sticky static; see TutorialMenuItem
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
            _maxResources = GameSettings.MaxStartingResources;
            _startAge = GameSettings.StartAge;
            _playerName = PlayerProfile.PlayerName;
            _error = null;
            _renderedState = (LobbyState)(-1);
            _renderedMapIndex = -1;

            Wire();

            // Two buttons, HOST and JOIN. Discovery does NOT start here: it
            // binds the broadcast port, and a player heading for HOST would
            // have to have it torn down again a moment later. JOIN starts it.
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

            // HOST opens the lobby immediately. Everything the create-lobby
            // window used to ask for is editable in the lobby itself, so there
            // is nothing left to fill in first.
            //
            // Cleanup() before StartHost() stays even though the choice pane
            // opens with no sockets: BackAction can land here from a browse or
            // a client lobby that did have them, and hosting wants the
            // broadcast port plus an EXCLUSIVE bind on the game port. Two
            // sockets on one port in one process is silent misrouting, with
            // Windows free to deliver a join to whichever it likes.
            if (HostButton != null) HostButton.onClick.AddListener(() =>
            {
                Cleanup();
                StartHost();
                SetState(LobbyState.HostLobby);
            });
            // JOIN opens the list and starts looking. Joining one of the
            // games on it moves to Connecting and then the lobby; nothing else
            // here needs a socket open.
            if (JoinButton != null) JoinButton.onClick.AddListener(() =>
            {
                if (_clientBroadcastSocket == null) StartClient();
                SetState(LobbyState.BrowseGames);
            });

            // The game name is named IN THE LOBBY now, not in the create
            // window — the title reads "MULTIPLAYER - <name>" and this is the
            // field that fills it, so the thing you are naming is on screen
            // while you name it.
            if (GameNameField != null)
            {
                GameNameField.text = _gameName;
                GameNameField.onValueChanged.AddListener(v =>
                {
                    if (!_isHost) return;
                    _gameName = v;
                    RefreshLobbyTitle();
                    SyncLobbyState();   // clients' titles follow, and so does TWB_GAME
                });
            }
            if (PlayerNameField != null)
            {
                PlayerNameField.text = _playerName;
                PlayerNameField.onValueChanged.AddListener(v => _playerName = v);
            }

            // Match options. Host-only: the guard is here as well as on
            // `interactable` because a client's dropdown is still scriptable,
            // and a value it set locally would be overwritten by the next
            // TWB_LOBBY anyway — silently, which reads as the control being
            // broken rather than not being yours.
            if (ResourcesDropdown != null)
            {
                SetOptions(ResourcesDropdown, LobbyOptions.ResourceNames);
                ResourcesDropdown.onValueChanged.AddListener(v =>
                {
                    if (!_isHost) return;
                    _maxResources = v == 1;
                    SyncLobbyState();
                });
            }
            if (AgeDropdown != null)
            {
                SetOptions(AgeDropdown, LobbyOptions.AgeNames);
                AgeDropdown.onValueChanged.AddListener(v =>
                {
                    if (!_isHost) return;
                    _startAge = (SkirmishStartAge)v;
                    SyncLobbyState();
                });
            }
            if (PortField != null)
            {
                PortField.text = _port.ToString();
                PortField.onValueChanged.AddListener(v =>
                {
                    if (ushort.TryParse(v, out ushort p)) _port = p;
                });
            }
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
            // One dressed button, two jobs: CONNECT while browsing, START
            // MATCH in the lobby. Dispatching on state beats a second button
            // that would have to be hidden everywhere else.
            if (StartButton != null) StartButton.onClick.AddListener(() =>
            {
                if (_state == LobbyState.BrowseGames)
                {
                    if (_selectedGame != null && _selectedGame.HasRoom) JoinGame(_selectedGame);
                }
                else StartMultiplayerGame();
            });
        }

        // ── Lobby size ──────────────────────────────────────────────────
        // The host grows and shrinks the lobby from the roster ladder, not
        // from a spinner. TWB_LOBBY already carries SlotCount as its first
        // field, so every change reaches connected clients on the next
        // SyncLobbyState with no protocol work.

        /// <summary>
        /// Slots this map can seat. A four-start map cannot host eight, and
        /// CycleMap can move to a smaller map at any time — hence
        /// <see cref="ClampRoster"/>.
        ///
        /// The empty-registry guard is not theoretical: MapPreviewWidget
        /// indexes Maps through Mathf.Clamp(i, 0, Count - 1), which is -1 when
        /// the list is empty, and StartHost reaches this before anything has
        /// checked that a map exists.
        /// </summary>
        private int MaxPlayers() =>
            MapRegistry.Maps.Count == 0 ? 8 : MapPreviewWidget.MaxPlayers(_selectedMapIndex);

        private void ClampRoster()
        {
            if (LobbyConfig.ActiveSlotCount > MaxPlayers())
            {
                SetSlotCount(MaxPlayers());
                if (_selectedSlot >= LobbyConfig.ActiveSlotCount) _selectedSlot = -1;
            }
        }

        /// <summary>
        /// Set the lobby size.
        ///
        /// Deliberately NOT LobbyConfig.SetupMultiplayer, which SkirmishPanel's
        /// namesake has to call and then undo: that helper rewrites every
        /// slot's Type and AIDifficulty, and here those live on _networkSlots,
        /// which is the authoritative copy the host broadcasts. Writing the
        /// count on its own leaves colours, teams and start positions exactly
        /// as the host set them, so there is nothing to save and restore.
        /// </summary>
        private void SetSlotCount(int count)
        {
            LobbyConfig.ActiveSlotCount = Mathf.Clamp(count, 2, MaxPlayers());
        }

        /// <summary>Open one more slot. It starts EMPTY rather than AI: a
        /// multiplayer lobby exists for humans to walk into, and the host can
        /// still turn the rung into an AI with its OPEN button.</summary>
        private void AddPlayer()
        {
            if (!_isHost) return;

            int idx = LobbyConfig.ActiveSlotCount;
            if (idx >= MaxPlayers()) return;

            SetSlotCount(idx + 1);
            if (idx < LobbyConfig.ActiveSlotCount)
            {
                _networkSlots[idx].Type = SlotType.Empty;
                _networkSlots[idx].PlayerName = "";
                _networkSlots[idx].Endpoint = null;
                _networkSlots[idx].AIDifficulty = LobbyAIDifficulty.Normal;
            }

            RebuildSlots(true);
            SyncLobbyState();
        }

        /// <summary>
        /// Close the BOTTOM slot.
        ///
        /// Only the bottom one, where the skirmish roster puts an X on every
        /// row. SkirmishPanel.RemoveSlot shifts the slots above it down, which
        /// is safe when nobody is connected and is not safe here: a lobby slot
        /// index IS the lockstep player index, so shifting would renumber
        /// every client mid-lobby. Shrinking from the bottom renumbers nobody.
        /// A slot with a human in it is not removable at all — see
        /// CanRemoveSlot.
        /// </summary>
        private void RemoveLastSlot()
        {
            if (!_isHost || !CanRemoveSlot()) return;

            int last = LobbyConfig.ActiveSlotCount - 1;
            _networkSlots[last].Type = SlotType.Empty;
            _networkSlots[last].PlayerName = "";
            _networkSlots[last].Endpoint = null;

            SetSlotCount(LobbyConfig.ActiveSlotCount - 1);
            if (_selectedSlot >= LobbyConfig.ActiveSlotCount) _selectedSlot = -1;

            RebuildSlots(true);
            SyncLobbyState();
        }

        private bool CanRemoveSlot()
        {
            int last = LobbyConfig.ActiveSlotCount - 1;
            if (last <= 0) return false;                    // never the host's own slot
            if (LobbyConfig.ActiveSlotCount <= 2) return false; // a match needs two
            return _networkSlots[last].Type != SlotType.Human;  // somebody is standing there
        }

        private void BackAction()
        {
            switch (_state)
            {
                case LobbyState.MainChoice:
                    Cleanup();
                    LeaveScreen();
                    break;
                case LobbyState.HostSetup:
                case LobbyState.BrowseGames:
                case LobbyState.HostLobby:
                    // Every one of these is one step in from the choice pane,
                    // so back means back to HOST / JOIN — and the sockets go
                    // with them, because the next choice may be the other one.
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

        /// <summary>
        /// Leave the multiplayer screen entirely.
        ///
        /// This panel lives in two places: as a pane inside MainMenu.unity,
        /// where switching it off reveals the menu behind it, and as the whole
        /// of MultiplayerMenu.unity, where switching it off would leave the
        /// player staring at an empty canvas. Load the menu back in that case,
        /// the same way SkirmishPanel's CANCEL does.
        /// </summary>
        private void LeaveScreen()
        {
            if (SceneManager.GetActiveScene().name == MainMenuBootstrap.MenuSceneName)
            {
                gameObject.SetActive(false);
                return;
            }
            SceneManager.LoadScene(MainMenuBootstrap.MenuSceneName);
        }

        // ── UI refresh / pane switching ─────────────────────────────────

        private void SetState(LobbyState state)
        {
            if (state == LobbyState.BrowseGames && _state != LobbyState.BrowseGames)
                _browseEnteredAt = Time.realtimeSinceStartup;
            if (state != LobbyState.BrowseGames) _selectedGame = null;
            _state = state;
            RefreshUi();
        }

        private void RefreshUi()
        {
            // _error stays English in state (some values are composed from
            // templates already, which pass through Loc.T unchanged).
            if (ErrorText != null) ErrorText.text = _error == null ? string.Empty : Loc.T(_error);

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
                        ConnectingLabel.text = Loc.T(_error ?? "Please wait...");
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
                    break;
                case LobbyState.HostLobby:
                    SetFooter("CANCEL LOBBY", startVisible: true, startText: "START MATCH");
                    RefreshLobbyTitle();
                    SetMapButtonsVisible(true);
                    RefreshMap();
                    SyncPills();
                    RebuildSlots(isHost: true);
                    break;
                case LobbyState.BrowseGames:
                    // CONNECT lives where START MATCH lives in the lobby, and
                    // RebuildGames re-gates it every refresh.
                    SetFooter("< BACK", startVisible: false, startText: "CONNECT");
                    RefreshLobbyTitle();
                    RebuildGames();
                    break;
                case LobbyState.ClientLobby:
                    SetFooter("LEAVE LOBBY", startVisible: false);
                    RefreshLobbyTitle();
                    SetMapButtonsVisible(false); // client sees the host's pick read-only
                    RefreshMap();
                    SyncPills();
                    RebuildSlots(isHost: false);
                    break;
                case LobbyState.Connecting:
                    SetFooter("CANCEL", startVisible: false);
                    if (ConnectingLabel != null)
                        ConnectingLabel.text = Loc.T(_error ?? "Please wait...");
                    break;
            }
        }

        private static void SetPane(GameObject pane, bool visible)
        {
            if (pane != null && pane.activeSelf != visible) pane.SetActive(visible);
        }

        private void SetFooter(string backText, bool startVisible,
                               string startText = null, bool startEnabled = true)
        {
            backText = Loc.T(backText);   // render chokepoint for footer labels
            if (BackLabel != null) BackLabel.text = backText;
            else if (BackButton != null)
            {
                var t = BackButton.GetComponentInChildren<TMP_Text>(true);
                if (t != null) t.text = backText;
            }

            if (StartButton == null) return;
            StartButton.gameObject.SetActive(startVisible);
            StartButton.interactable = startEnabled;
            if (startText != null)
            {
                var t = StartButton.GetComponentInChildren<TMP_Text>(true);
                if (t != null) t.text = Loc.T(startText);
            }
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

            // A smaller map seats fewer players, and the roster is the only
            // place the lobby size lives now — so trim it here rather than
            // letting the host walk into a start-position shortage.
            ClampRoster();

            RefreshMap();
            RebuildSlots(true);
            SyncLobbyState();
        }

        private void RefreshMap()
        {
            _renderedMapIndex = _selectedMapIndex;
            if (MapPreview == null) return;

            // Start positions belong to the map — changing map invalidates
            // every assignment. docs/Design/Lobby_Setup.md
            if (_selectedMapIndex != _startsMapIndex)
            {
                for (int i = 0; i < LobbyConfig.Slots.Length; i++)
                    if (LobbyConfig.Slots[i] != null)
                        LobbyConfig.Slots[i].StartIndex = PlayerSlot.AutoStart;
                _startsMapIndex = _selectedMapIndex;
            }

            MapPreview.StartState = GetStartState;
            // Only the host places players: start assignment is part of the
            // lobby state the host broadcasts, so a client editing it locally
            // would just be overwritten by the next TWB_LOBBY.
            MapPreview.OnStartClicked = _isHost ? OnStartPositionClicked : (System.Action<int>)null;
            MapPreview.Show(_selectedMapIndex);
        }

        // ── Start positions (host-assigned) ─────────────────────────────

        private int _selectedSlot = -1;
        private int _startsMapIndex = -1;
        private readonly List<GameObject> _slotRowRoots = new List<GameObject>();
        private Color _slotRowBaseColor = new Color(0.08f, 0.11f, 0.13f, 1f);

        private void ApplySlotRowSelection()
        {
            for (int i = 0; i < _slotRowRoots.Count; i++)
            {
                if (_slotRowRoots[i] == null) continue;
                var img = _slotRowRoots[i].GetComponent<Image>();
                if (img == null) continue;

                // Ladder rungs past the lobby sit back at 45% so the roster
                // reads as "this many players, room for more" at a glance.
                // Same treatment as the skirmish roster.
                bool occupied = i < _slotRowKinds.Count &&
                                _slotRowKinds[i] == SlotRowKind.Occupied;
                if (i == _selectedSlot && occupied)
                    img.color = new Color(0.16f, 0.24f, 0.30f, 1f);
                else if (occupied)
                    img.color = _slotRowBaseColor;
                else
                    img.color = new Color(_slotRowBaseColor.r, _slotRowBaseColor.g,
                                          _slotRowBaseColor.b, _slotRowBaseColor.a * 0.45f);
            }
        }

        private static int SlotHolding(int startIndex)
        {
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var s = LobbyConfig.Slots[i];
                if (s != null && s.StartIndex == startIndex) return i;
            }
            return -1;
        }

        private void GetStartState(int startIndex, out Color tint, out string holderLabel)
        {
            int holder = SlotHolding(startIndex);
            if (holder < 0)
            {
                tint = MapPreviewWidget.StartsColor;
                holderLabel = string.Empty;
                return;
            }
            tint = LobbyConfig.Slots[holder].GetFactionColor();
            holderLabel = (holder + 1).ToString();
        }

        private void OnStartPositionClicked(int startIndex)
        {
            if (!_isHost) return;

            int holder = SlotHolding(startIndex);

            if (_selectedSlot < 0 || _selectedSlot >= LobbyConfig.ActiveSlotCount)
            {
                if (holder >= 0)
                {
                    LobbyConfig.Slots[holder].StartIndex = PlayerSlot.AutoStart;
                    MapPreview?.RefreshStartMarkers();
                    SyncLobbyState();
                }
                return;
            }

            var sel = LobbyConfig.Slots[_selectedSlot];
            if (sel.StartIndex == startIndex)
                sel.StartIndex = PlayerSlot.AutoStart;
            else
            {
                if (holder >= 0) LobbyConfig.Slots[holder].StartIndex = PlayerSlot.AutoStart;
                sel.StartIndex = startIndex;
            }

            MapPreview?.RefreshStartMarkers();
            SyncLobbyState();
        }

        private void SyncPills()
        {
            SyncPill(FogToggle, FogState, _fogOfWar);
            SyncPill(BorderToggle, BorderState, _borderEnabled);

            // SetValueWithoutNotify on purpose: this runs when the HOST's
            // lobby state arrives on a client, and firing onValueChanged here
            // would have the client write the value straight back over the
            // host's. Same reason the skirmish lobby restores with it.
            if (ResourcesDropdown != null)
                ResourcesDropdown.SetValueWithoutNotify(_maxResources ? 1 : 0);
            if (AgeDropdown != null)
                AgeDropdown.SetValueWithoutNotify((int)_startAge);

            // Only the host owns the match settings; a client reads them.
            if (ResourcesDropdown != null) ResourcesDropdown.interactable = _isHost;
            if (AgeDropdown != null) AgeDropdown.interactable = _isHost;

            // Same for the name. SetTextWithoutNotify, or echoing the host's
            // name into a client's field would fire the listener and the
            // client would try to rename the lobby back at the host.
            if (GameNameField != null)
            {
                GameNameField.interactable = _isHost;
                if (GameNameField.text != _gameName) GameNameField.SetTextWithoutNotify(_gameName);
            }
            RefreshLobbyTitle();
        }

        /// <summary>
        /// Screen title: "MULTIPLAYER - <game name>", or just "MULTIPLAYER"
        /// until the lobby has a name. Both peers render the same string —
        /// the name travels on TWB_LOBBY — so a client is not left staring at
        /// a generic "LOBBY" while the host looks at a named one.
        /// </summary>
        private void RefreshLobbyTitle()
        {
            if (LobbyTitle == null) return;
            string named = string.IsNullOrWhiteSpace(_gameName)
                ? string.Empty : _gameName.Trim().ToUpperInvariant();
            LobbyTitle.text = named.Length == 0
                ? Loc.T("MULTIPLAYER")
                : string.Format(Loc.T("MULTIPLAYER - {0}"), named);
        }

        private static void SyncPill(Button toggle, TMP_Text state, bool on)
        {
            if (state != null) state.text = Loc.T(on ? "ON" : "OFF");
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

            _slotRowRoots.Clear();
            _slotRowKinds.Clear();

            // Does this template size its own columns?
            //
            // MainMenu's template does not: it force-expands its children, so
            // every widget grows to share the row and its authored width is
            // ignored — which is the whole reason LobbyRowLayout exists.
            // MultiplayerMenu.unity's template is derived from the skirmish
            // roster, which went through that pass: force-expand is off and
            // every column carries its own LayoutElement. Pinning columns over
            // the top of THAT only fights the authored design and silently
            // restyles the row (a 172-wide team chip becomes 112, a 330-wide
            // button becomes 180). Detect it and leave the row alone.
            _rowSelfSizing = SlotRowTemplate.TryGetComponent(out HorizontalLayoutGroup rowGroup)
                             && !rowGroup.childForceExpandWidth;

            int active = LobbyConfig.ActiveSlotCount;
            int cap = MaxPlayers();

            // The HOST sees the full eight-rung ladder, so the lobby's size is
            // something you read off the roster and change in place. A CLIENT
            // sees only the slots that exist: it has no add affordance, so
            // empty rungs below the lobby would just be four rows of nothing
            // it can act on.
            int rows = isHost ? TotalSlotRows : active;
            for (int i = 0; i < rows; i++)
            {
                SlotRowKind kind =
                    i < active ? SlotRowKind.Occupied
                    : i == active && active < cap ? SlotRowKind.AddHere
                    : SlotRowKind.Empty;
                BuildSlotRow(i, isHost, kind);
            }

            if (_selectedSlot >= LobbyConfig.ActiveSlotCount) _selectedSlot = -1;
            ApplySlotRowSelection();
            MapPreview?.RefreshStartMarkers();
        }

        /// <summary>What a lobby roster row is showing. Mirrors
        /// SkirmishPanel.RowKind — the two rosters are the same ladder.</summary>
        private enum SlotRowKind
        {
            /// <summary>A slot that exists: human, AI, or open and waiting.</summary>
            Occupied,
            /// <summary>Top-most rung past the lobby — carries "ADD PLAYER".</summary>
            AddHere,
            /// <summary>Past the add rung, or past what the map can seat.</summary>
            Empty
        }

        /// <summary>Always eight rungs, matching the engine's faction range
        /// (Blue..White) and LobbyConfig.Slots.</summary>
        private const int TotalSlotRows = 8;

        private readonly List<SlotRowKind> _slotRowKinds = new List<SlotRowKind>();

        /// <summary>
        /// Draw a row that holds no slot: either the add rung or dead space
        /// past it. Same visual language as the skirmish roster's empty rows —
        /// a barely-there colour strip, every per-slot widget switched off, and
        /// the name column carrying the whole message.
        /// </summary>
        private void BuildLadderRung(Transform t, bool addHere)
        {
            // A coloured swatch on a slot with nobody in it reads as occupied.
            var strip = t.Find("ColorStrip");
            if (strip != null)
            {
                if (strip.TryGetComponent(out Image stripImg))
                {
                    stripImg.color = new Color(1f, 1f, 1f, 0.07f);
                    stripImg.raycastTarget = false;
                }
                if (strip.TryGetComponent(out Button stripBtn)) stripBtn.interactable = false;
            }

            foreach (var node in new[] { "TeamChip", "HostBadge", "AiButton",
                                         "DifficultyButton", "StrategyDropdown",
                                         "RemoveButton" })
                t.Find(node)?.gameObject.SetActive(false);

            var name = t.Find("NameLabel")?.GetComponent<TMP_Text>();
            if (name == null) return;

            name.gameObject.SetActive(true);
            if (addHere)
            {
                name.color = Color.white;
                name.text = Loc.T("+ ADD PLAYER");
            }
            else
            {
                name.color = new Color(1f, 1f, 1f, 0.22f);
                // Past the add rung but still seatable reads "EMPTY"; past what
                // the map can seat is not a slot at all, so it gets a dash.
                name.text = _slotRowRoots.Count <= MaxPlayers() ? Loc.T("EMPTY") : "—";
            }
        }

        private void BuildSlotRow(int index, bool isHost, SlotRowKind kind)
        {
            var slot = _networkSlots[index];
            int idx = index;
            bool occupied = kind == SlotRowKind.Occupied;

            var row = Instantiate(SlotRowTemplate, SlotsContent);
            row.SetActive(true);
            var t = row.transform;
            if (!_rowSelfSizing) LobbyRowLayout.PrepareRow(row);

            // Host selects a slot here, then clicks a start position on the map
            // preview to place that player. docs/Design/Lobby_Setup.md
            _slotRowRoots.Add(row);
            _slotRowKinds.Add(kind);
            if (isHost && row.TryGetComponent(out Image rowImg))
            {
                if (_slotRowRoots.Count == 1) _slotRowBaseColor = rowImg.color;
                rowImg.raycastTarget = true;
                var rowBtn = row.GetComponent<Button>() ?? row.AddComponent<Button>();
                rowBtn.transition = Selectable.Transition.None;
                rowBtn.targetGraphic = rowImg;

                if (occupied)
                    rowBtn.onClick.AddListener(() =>
                    {
                        _selectedSlot = _selectedSlot == idx ? -1 : idx;
                        ApplySlotRowSelection();
                    });
                else if (kind == SlotRowKind.AddHere)
                    rowBtn.onClick.AddListener(AddPlayer);
                else
                    rowBtn.interactable = false;
            }

            // A rung past the end of the lobby carries no slot data — no
            // colour, no team, no name, nothing to configure. Everything below
            // reads _networkSlots[index] and LobbyConfig.Slots[index] for a
            // slot that does not exist yet, so the ladder rungs are drawn here
            // and the row is finished.
            if (!occupied)
            {
                BuildLadderRung(t, kind == SlotRowKind.AddHere);
                return;
            }

            // Banner-colour strip — host picks any slot's colour; a client only its own
            // (cycles locally + sends TWB_COLOR to the host).
            var strip = t.Find("ColorStrip");
            if (strip != null)
            {
                Image stripImg = null;
                strip.TryGetComponent(out stripImg);
                if (stripImg != null)
                {
                    stripImg.color = LobbyConfig.Slots[index].GetFactionColor();
                    // Builder-made swatches have raycastTarget off; the Button
                    // needs a raycastable graphic.
                    stripImg.raycastTarget = true;
                }
                if (strip.TryGetComponent(out Button stripBtn))
                {
                    bool canChange = isHost || index == _mySlotIndex;
                    stripBtn.interactable = canChange;
                    if (canChange)
                        stripBtn.onClick.AddListener(() =>
                        {
                            // Same 12-swatch picker as the skirmish lobby.
                            // docs/Design/Lobby_Setup.md
                            ColorPickerPopup.Open(
                                this,
                                strip as RectTransform,
                                LobbyConfig.Slots[idx].ColorIndex,
                                c => IsColorInUse(c, idx),
                                chosen =>
                                {
                                    if (_isHost) LobbyConfig.Slots[idx].ColorIndex = chosen;
                                    else SendColorSet(idx, chosen);
                                    if (stripImg != null)
                                        stripImg.color = LobbyConfig.Slots[idx].GetFactionColor();
                                });
                        });
                }
            }

            // Team column: a number, or "-" for no team. This used to print
            // "TEAM {row+1}", which merely echoed the row index and has been
            // actively misleading since teams became real.
            // docs/Design/Teams.md
            var chipRoot = t.Find("TeamChip");
            var chip = chipRoot?.GetComponentInChildren<TMP_Text>(true);
            if (chip != null)
                chip.text = LobbyConfig.Slots[index].TeamIndex == Alliances.NoTeam
                    ? "-" : LobbyConfig.Slots[index].TeamIndex.ToString();
            if (chipRoot != null && isHost)
            {
                // Only the host edits teams — team assignment rides the lobby
                // state the host broadcasts.
                var chipBtn = chipRoot.GetComponent<Button>()
                              ?? chipRoot.gameObject.AddComponent<Button>();
                chipBtn.onClick.AddListener(() =>
                {
                    var s = LobbyConfig.Slots[idx];
                    s.TeamIndex = (byte)((s.TeamIndex + 1) % (Alliances.MaxTeams + 1));
                    if (chip != null)
                        chip.text = s.TeamIndex == Alliances.NoTeam ? "-" : s.TeamIndex.ToString();
                    SyncLobbyState();
                });
            }

            var badge = t.Find("HostBadge");
            var name = t.Find("NameLabel")?.GetComponent<TMP_Text>();
            var aiBtn = t.Find("AiButton")?.GetComponent<Button>();
            var diffBtn = t.Find("DifficultyButton")?.GetComponent<Button>();
            // Present in MultiplayerMenu.unity, absent from MainMenu's older
            // template — null-safe so both keep working.
            var stratDd = t.Find("StrategyDropdown")?.GetComponent<TMP_Dropdown>();

            if (badge != null) badge.gameObject.SetActive(index == 0 && slot.Type == SlotType.Human);
            if (aiBtn != null) aiBtn.gameObject.SetActive(false);
            if (diffBtn != null) diffBtn.gameObject.SetActive(false);
            if (stratDd != null) stratDd.gameObject.SetActive(false);

            if (slot.Type == SlotType.Human)
            {
                if (name != null)
                {
                    string label = string.IsNullOrEmpty(slot.PlayerName) ? Loc.T("Player") : slot.PlayerName;
                    if (index == _mySlotIndex && !_isHost) label += Loc.T("  (YOU)");
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
                            SyncLobbyState();
                        });
                    }
                    // AI personality — the same seven the skirmish lobby
                    // offers, off the same table. It lives on LobbyConfig.Slots
                    // rather than _networkSlots because that is the copy the
                    // AI bootstrap reads when the match starts.
                    if (stratDd != null)
                    {
                        stratDd.gameObject.SetActive(true);
                        SetOptions(stratDd, LobbyOptions.StrategyNames);
                        stratDd.SetValueWithoutNotify((int)LobbyConfig.Slots[idx].AIStrategy);
                        stratDd.onValueChanged.AddListener(v =>
                        {
                            LobbyConfig.Slots[idx].AIStrategy = (LobbyAIStrategy)v;
                            SyncLobbyState();
                        });
                    }
                }
                else if (name != null)
                {
                    name.text = string.Format(Loc.T("AI · {0}"),
                        Loc.T(DifficultyNames[(int)slot.AIDifficulty]));
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
                        name.text = Loc.T("waiting for player…");
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
                    name.text = Loc.T("OPEN");
                    name.color = TextDim;
                    name.gameObject.SetActive(true);
                }
            }

            // Shrink handle. Only the bottom rung carries it, and only when
            // that rung is the host's to close — see RemoveLastSlot for why
            // this is not an X on every row like the skirmish roster.
            // MultiplayerPanelLayout adds the button to the scene template; the
            // lookup stays null-safe so the roster still works without it.
            var remove = t.Find("RemoveButton");
            if (remove != null)
            {
                bool isBottomRung = isHost && index == LobbyConfig.ActiveSlotCount - 1
                                    && CanRemoveSlot();
                remove.gameObject.SetActive(isBottomRung);
                if (isBottomRung && remove.TryGetComponent(out Button removeBtn))
                {
                    removeBtn.onClick.RemoveAllListeners();
                    removeBtn.onClick.AddListener(RemoveLastSlot);
                }
            }

            // ── Columns ────────────────────────────────────────────────
            // Only for a template that does NOT size itself. MainMenu's row
            // force-expands its children, so authored widths are ignored and
            // the columns stagger; pinning order and width is the fix. A
            // self-sizing template already has all of this and is left alone —
            // see _rowSelfSizing in RebuildSlots. HOST stays in the order
            // either way: it is a real distinction in a networked lobby.
            if (_rowSelfSizing) return;

            float scale = LobbyRowLayout.RowScale(row);
            int order = 0;
            LobbyRowLayout.Column(strip, LobbyRowLayout.ColColor, scale, ref order);
            LobbyRowLayout.Column(name != null ? name.transform : null,
                                  LobbyRowLayout.ColName, scale, ref order, flexible: 1f);
            LobbyRowLayout.Column(t.Find("TeamChip"), LobbyRowLayout.ColTeam, scale, ref order);
            LobbyRowLayout.Column(stratDd != null ? stratDd.transform : null,
                                  LobbyRowLayout.ColStrat, scale, ref order);
            LobbyRowLayout.Column(diffBtn != null ? diffBtn.transform : null,
                                  LobbyRowLayout.ColDiff, scale, ref order);
            LobbyRowLayout.Column(aiBtn != null ? aiBtn.transform : null,
                                  LobbyRowLayout.ColButton, scale, ref order);
            LobbyRowLayout.Column(badge, LobbyRowLayout.ColBadge, scale, ref order);
            LobbyRowLayout.Column(remove, LobbyRowLayout.ColRemove, scale, ref order);
        }

        /// <summary>Set per rebuild from the row template — see RebuildSlots.</summary>
        private bool _rowSelfSizing;

        /// <summary>Fill a dropdown, translating at the render chokepoint —
        /// callers pass the English source so the authored option list and the
        /// localisation table stay in step.</summary>
        private static void SetOptions(TMP_Dropdown dd, string[] names)
        {
            if (dd == null) return;
            dd.ClearOptions();
            var opts = new List<string>(names.Length);
            foreach (var n in names) opts.Add(Loc.T(n));
            dd.AddOptions(opts);
        }

        private static void SetButtonLabel(Button btn, string text)
        {
            // Render chokepoint: callers pass the English source ("AI",
            // "OPEN", DifficultyNames entries) and it is translated here.
            var t = btn.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = Loc.T(text);
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
                // After ~10 quiet seconds the search has failed, not lagged —
                // say so, and point at the two causes that account for nearly
                // every LAN discovery failure plus the fallback that bypasses
                // discovery entirely.
                bool quiet = Time.realtimeSinceStartup - _browseEnteredAt > DISCOVERY_TIMEOUT * 2f;
                SetRowColumns(row,
                    quiet
                        ? Loc.T("No host heard yet — check both PCs share a network and the firewall allows the game, or join by IP below.")
                        : Loc.T("Searching for games…"),
                    "", "", dim: true);
                var joinT = row.transform.Find("JoinButton");
                if (joinT != null) joinT.gameObject.SetActive(false);
                GateConnect();
                return;
            }

            foreach (var game in _discoveredGames)
            {
                var g = game;
                var row = Instantiate(GameRowTemplate, GamesContent);
                row.SetActive(true);
                SetRowColumns(row, g.HostName.ToUpperInvariant(), MapNameOf(g), SlotsOf(g),
                              dim: !g.HasRoom);

                // The per-row JOIN is retired: picking a game and acting on it
                // are two different decisions, and one CONNECT in the footer
                // says which host you are about to reach instead of hiding the
                // action in whichever row the pointer happened to be over.
                var joinBtn = row.transform.Find("JoinButton");
                if (joinBtn != null) joinBtn.gameObject.SetActive(false);

                // Whole row selects. It already carries a background Image, so
                // there is a raycast target to click and something to tint.
                if (row.TryGetComponent(out Image rowImg))
                {
                    if (!_gameRowBaseCaptured)
                    {
                        _gameRowBaseColor = rowImg.color;
                        _gameRowBaseCaptured = true;
                    }
                    rowImg.raycastTarget = true;
                    rowImg.color = ReferenceEquals(g, _selectedGame)
                        ? new Color(0.16f, 0.24f, 0.30f, 1f)
                        : _gameRowBaseColor;

                    var rowBtn = row.GetComponent<Button>() ?? row.AddComponent<Button>();
                    rowBtn.transition = Selectable.Transition.None;
                    rowBtn.targetGraphic = rowImg;
                    rowBtn.onClick.AddListener(() =>
                    {
                        _selectedGame = ReferenceEquals(g, _selectedGame) ? null : g;
                        RebuildGames();
                    });
                }
            }

            GateConnect();
        }

        /// <summary>Map as the player knows it, resolved from the scene name
        /// the host advertised. An unknown scene (a map this build does not
        /// have) shows the raw name rather than an empty column — that is a
        /// useful thing to see before trying to join it.</summary>
        private static string MapNameOf(DiscoveredGame g)
        {
            if (string.IsNullOrEmpty(g.MapScene)) return "";
            int i = MapRegistry.IndexOf(g.MapScene);
            return i >= 0 ? MapPreviewWidget.DisplayName(i).ToUpperInvariant()
                          : g.MapScene.ToUpperInvariant();
        }

        /// <summary>Occupancy as "used/total" — 4/4 reads as full at a glance,
        /// which is the whole point of showing it.</summary>
        private static string SlotsOf(DiscoveredGame g) =>
            g.SlotsTotal <= 0 ? "" : $"{g.SlotsUsed}/{g.SlotsTotal}";

        /// <summary>
        /// Fill a browse row's three columns. The template ships with one
        /// label; MapLabel and SlotsLabel are added beside it in the scene, so
        /// this stays null-safe and a row without them still shows the host.
        /// </summary>
        private static void SetRowColumns(GameObject row, string host, string map,
                                          string slots, bool dim)
        {
            var t = row.transform;
            Set(t.Find("GameLabel"), host);
            Set(t.Find("MapLabel"), map);
            Set(t.Find("SlotsLabel"), slots);

            void Set(Transform node, string text)
            {
                var lbl = node != null ? node.GetComponent<TMP_Text>() : null;
                if (lbl == null) return;
                lbl.text = text;
                lbl.color = dim ? TextDim : Color.white;
            }
        }

        /// <summary>
        /// CONNECT is live only with a real game picked. Re-run on every list
        /// refresh, not just on selection: a host that stops advertising is
        /// dropped by the DISCOVERY_TIMEOUT sweep, and connecting to a lobby
        /// that has already gone would just sit on "no response from host".
        /// </summary>
        private void GateConnect()
        {
            if (_selectedGame != null && !_discoveredGames.Contains(_selectedGame))
                _selectedGame = null;

            if (_state != LobbyState.BrowseGames || StartButton == null) return;

            // HIDDEN, not greyed. There is nothing to explain here — with no
            // game picked the button has no meaning at all, and a permanently
            // dead control in the corner just invites clicking.
            bool suitable = _selectedGame != null && _selectedGame.HasRoom;
            StartButton.gameObject.SetActive(suitable);
            StartButton.interactable = suitable;
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
                    _error = string.Format(Loc.T("Invalid IP address: {0}"), _directIp);
                    RefreshUi();
                    return;
                }
            }
            if (!ushort.TryParse(_directPort.Trim(), out ushort port) || port == 0)
            {
                _error = string.Format(Loc.T("Invalid port: {0}"), _directPort);
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

            // Every slot but the host's opens EMPTY, i.e. joinable. It used to
            // open as AI, which was defensible when the host had just typed a
            // player count into the setup window and clearly meant "fill it" —
            // but that spinner is gone, and a LAN lobby whose only free seat is
            // occupied by a bot nobody asked for is a lobby nobody can join
            // until the host clicks the seat empty again. The host turns a rung
            // into an AI with its OPEN button, same as any other slot.
            for (int i = 1; i < 8; i++) _networkSlots[i].Type = SlotType.Empty;

            // A map change can have shrunk what this lobby can seat since the
            // count was last set.
            ClampRoster();

            try
            {
                _hostSocket = CreateBroadcastSocket(BROADCAST_PORT);
                BindGamePort();
            }
            catch (SocketException se)
            {
                string hint = se.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? string.Format(Loc.T("Port {0} already in use. Close other game instances or restart Unity."), BROADCAST_PORT)
                    : string.Format(Loc.T("Socket error ({0}): {1}"), se.SocketErrorCode, se.Message);
                _error = string.Format(Loc.T("Network error: {0}"), hint);
            }
            catch (Exception e)
            {
                _error = string.Format(Loc.T("Failed to start host: {0}"), e.Message);
            }
        }

        /// <summary>
        /// Take the lobby's game port, walking upward if the standard one is
        /// busy. The player is never asked: a discovering client reads the
        /// port out of the TWB_GAME advert, so the only party that ever needed
        /// to know it was the host itself.
        ///
        /// The bind is EXCLUSIVE on purpose — two sockets sharing this port in
        /// one process is silent misrouting, with Windows free to deliver a
        /// join to whichever it likes. So the busy case is handled by moving,
        /// not by sharing.
        ///
        /// STEP OF TWO, not one. StartMultiplayerGame hands lockstep
        /// <c>_port + 1</c>, so a second host on the same machine stepping to
        /// 7980 would collide its GAME port against the first host's LOCKSTEP
        /// port — which fails late, mid-launch, instead of here.
        /// </summary>
        private void BindGamePort()
        {
            for (int i = 0; i < GamePortAttempts; i++)
            {
                ushort candidate = (ushort)(DefaultGamePort + i * 2);
                try
                {
                    _joinSocket = new UdpClient(candidate);
                    _port = candidate;
                    if (i > 0)
                        Debug.Log($"[MultiplayerPanel] Game port {DefaultGamePort} was busy — "
                                  + $"hosting on {candidate} instead. Clients discover this from "
                                  + "the advert, so nothing needs telling.");
                    return;
                }
                catch (SocketException se)
                    when (se.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    // Next candidate.
                }
            }

            _error = string.Format(
                Loc.T("No free game port in {0}-{1}. Close other game instances."),
                DefaultGamePort, DefaultGamePort + (GamePortAttempts - 1) * 2);
        }

        private void StartClient()
        {
            _isHost = false;
            _mySlotIndex = -1;

            try
            {
                _clientBroadcastSocket = CreateBroadcastSocket(BROADCAST_PORT);
                _clientPrivateSocket = new UdpClient(0);
                _clientPrivateSocket.EnableBroadcast = true; // TWB_FIND probes
                _clientPrivatePort = (ushort)((IPEndPoint)_clientPrivateSocket.Client.LocalEndPoint).Port;
                Debug.Log($"[MultiplayerPanel] Browsing for LAN games — listening on UDP "
                          + $"{BROADCAST_PORT}, probing from port {_clientPrivatePort}.");
            }
            catch (SocketException se)
            {
                string hint = se.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? string.Format(Loc.T("Port {0} already in use. Close other game instances or restart Unity."), BROADCAST_PORT)
                    : string.Format(Loc.T("Socket error ({0}): {1}"), se.SocketErrorCode, se.Message);
                _error = string.Format(Loc.T("Network error: {0}"), hint);
            }
            catch (Exception e)
            {
                _error = string.Format(Loc.T("Failed to start client: {0}"), e.Message);
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
            // Active probe while browsing: a firewall that silently eats the
            // host's broadcast usually still lets through a REPLY to a datagram
            // this machine sent first. The probe leaves from the private socket,
            // so the host's unicast answer lands on a port only this instance
            // owns — immune to the shared-port delivery quirk documented on
            // _joinSocket.
            if (_state == LobbyState.BrowseGames && _clientPrivateSocket != null)
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastFindProbe >= BROADCAST_INTERVAL)
                {
                    _lastFindProbe = now;
                    byte[] probe = Encoding.UTF8.GetBytes(MSG_FIND);
                    foreach (var target in BroadcastTargets())
                    {
                        try { _clientPrivateSocket.Send(probe, probe.Length, target); } catch { }
                    }
                }
            }

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
            string msg = MSG_GAME + AdvertBody();
            byte[] data = Encoding.UTF8.GetBytes(msg);
            var targets = BroadcastTargets();
            foreach (var target in targets)
            {
                // Per-target try: a send can fail on an interface with no
                // route (VPN mid-connect) and the rest must still go out.
                try { _hostSocket.Send(data, data.Length, target); } catch { }
            }

            if (!_loggedAdvertTargets)
            {
                _loggedAdvertTargets = true;
                Debug.Log($"[MultiplayerPanel] Hosting '{_gameName}' — advertising on "
                          + string.Join(", ", targets.Select(t => t.Address.ToString()))
                          + $" (UDP {BROADCAST_PORT}), lobby port {_port}.");
            }
        }

        // ── Broadcast targets ───────────────────────────────────────────
        //
        // Discovery datagrams go to EVERY viable broadcast address, not just
        // 255.255.255.255. Windows routes the limited-broadcast address out
        // exactly ONE interface (whichever the route table prefers), and on a
        // machine with a VPN / Hyper-V / WSL / VirtualBox adapter that is
        // routinely the wrong one — the advert leaves on a virtual network no
        // other player is on and the LAN never hears the lobby. The
        // subnet-directed broadcast of each up interface (e.g. 192.168.1.255)
        // reaches that interface's actual network, so the advert and the
        // TWB_FIND probe are sent to all of them.

        private static readonly List<IPEndPoint> _broadcastTargets = new List<IPEndPoint>();
        private static float _broadcastTargetsBuiltAt = -999f;

        private static List<IPEndPoint> BroadcastTargets()
        {
            // Interfaces change rarely (cable, Wi-Fi, VPN up/down) — rebuild
            // every few seconds, not per send.
            float now = Time.realtimeSinceStartup;
            if (_broadcastTargets.Count > 0 && now - _broadcastTargetsBuiltAt < 5f)
                return _broadcastTargets;

            _broadcastTargetsBuiltAt = now;
            _broadcastTargets.Clear();
            _broadcastTargets.Add(new IPEndPoint(IPAddress.Broadcast, BROADCAST_PORT));
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var mask = ua.IPv4Mask;
                        if (mask == null || mask.Equals(IPAddress.Any)) continue;

                        byte[] a = ua.Address.GetAddressBytes();
                        byte[] m = mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                        var directed = new IPAddress(b);

                        bool known = false;
                        for (int i = 0; i < _broadcastTargets.Count; i++)
                            if (_broadcastTargets[i].Address.Equals(directed)) { known = true; break; }
                        if (!known)
                            _broadcastTargets.Add(new IPEndPoint(directed, BROADCAST_PORT));
                    }
                }
            }
            catch
            {
                // NIC enumeration can throw on exotic adapters; the limited
                // broadcast entry added above still stands.
            }
            return _broadcastTargets;
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
            if (msg.StartsWith(MSG_FIND))
            {
                // A browsing client asks "anyone hosting?" — answer unicast to
                // the asker's private socket. This is what makes discovery work
                // when our periodic broadcast never reaches the client (their
                // firewall, or the broadcast egressing a virtual adapter).
                string reply = MSG_GAME + AdvertBody();
                HostSend(Encoding.UTF8.GetBytes(reply), remote);
                return;
            }

            if (msg.StartsWith(MSG_JOIN))
            {
                var parts = msg.Substring(MSG_JOIN.Length).Split('|');
                if (parts.Length >= 2)
                {
                    string playerName = parts[0];
                    ushort clientPort = ushort.Parse(parts[1]);

                    // Build check BEFORE a seat is handed out. Two peers running
                    // different builds used to connect happily and then diverge
                    // on the first cost lookup, which reads as a mysterious
                    // mid-match desync rather than the version mismatch it is.
                    // docs/Multiplayer_LAN_Readiness.md
                    string clientFingerprint = parts.Length > 2 ? parts[2] : null;
                    if (clientFingerprint != MatchSettingsSync.Fingerprint)
                    {
                        string why = string.IsNullOrEmpty(clientFingerprint)
                            ? "they are running an older build that does not send a version"
                            : $"their build is {clientFingerprint}, this one is {MatchSettingsSync.Fingerprint}";
                        string reject = $"{MSG_REJECT}Build mismatch — {why}. " +
                                        $"Both players need {MatchSettingsSync.BuildLabel}.";
                        HostSend(Encoding.UTF8.GetBytes(reject),
                            new IPEndPoint(remote.Address, clientPort));
                        return;
                    }

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
                RegisterDiscoveredGame(msg, remote, viaProbeReply: false);
        }

        /// <summary>Record a TWB_GAME advert, whether it arrived as the host's
        /// broadcast (on the shared discovery socket) or as the unicast answer
        /// to our TWB_FIND probe (on the private socket).
        ///
        /// Keyed by lobby IDENTITY (host name + game port + game name), never
        /// by source address: the advert goes out once per interface, so a
        /// host with a second adapter (VPN, Hyper-V, Wi-Fi + Ethernet) is
        /// heard from several source IPs — address-keying listed ONE hosted
        /// game as two or more. Same machine hosting twice is impossible on
        /// one port (exclusive _joinSocket bind), so the key is unique.</summary>
        private void RegisterDiscoveredGame(string msg, IPEndPoint remote, bool viaProbeReply)
        {
            var parts = msg.Substring(MSG_GAME.Length).Split('|');
            if (parts.Length < 3) return;
            string gameName = parts[0];
            string hostName = parts[1];
            if (!ushort.TryParse(parts[2], out ushort port)) return;

            // Appended fields — an advert without them still lists, it just
            // shows no map and no occupancy.
            string mapScene = parts.Length > 3 ? parts[3] : "";
            int used = parts.Length > 4 && int.TryParse(parts[4], out int u) ? u : 0;
            int total = parts.Length > 5 && int.TryParse(parts[5], out int t) ? t : 0;

            var game = _discoveredGames.FirstOrDefault(g =>
                g.Port == port && g.HostName == hostName && g.GameName == gameName);
            if (game == null)
            {
                game = new DiscoveredGame
                {
                    GameName = gameName,
                    HostName = hostName,
                    Port = port,
                    Endpoint = remote,
                    ViaProbeReply = viaProbeReply
                };
                _discoveredGames.Add(game);
                Debug.Log($"[MultiplayerPanel] Discovered '{gameName}' hosted by {hostName} "
                          + $"at {remote.Address}:{port}.");
            }
            else if (viaProbeReply && !game.ViaProbeReply)
            {
                // Upgrade to the routed address once — see ViaProbeReply.
                game.Endpoint = remote;
                game.ViaProbeReply = true;
            }
            // Refreshed every advert: a lobby fills and empties while the
            // browser is looking at it.
            game.MapScene = mapScene;
            game.SlotsUsed = used;
            game.SlotsTotal = total;
            game.LastSeen = Time.realtimeSinceStartup;
        }

        private void HandleClientMessage(string msg, IPEndPoint remote)
        {
            if (msg.StartsWith(MSG_GAME))
            {
                // Unicast answer to our TWB_FIND probe — same payload as the
                // periodic broadcast, same registration.
                RegisterDiscoveredGame(msg, remote, viaProbeReply: true);
            }
            else if (msg.StartsWith(MSG_ACCEPT))
            {
                _mySlotIndex = int.Parse(msg.Substring(MSG_ACCEPT.Length));
                _hostEndpoint = remote;
                _pendingJoin = null;
                _state = LobbyState.ClientLobby;
            }
            else if (msg.StartsWith(MSG_REJECT))
            {
                // The host has told us why this can never work — stop retrying
                // and show the reason instead of timing out with "could not
                // connect", which sends people hunting for a firewall problem.
                _error = msg.Substring(MSG_REJECT.Length);
                _pendingJoin = null;
                _state = LobbyState.BrowseGames;
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

                // Adopt the host's world wholesale. Refusing here is far better
                // than loading into a match that is already two different games:
                // StartAge, StartCulture, MaxStartingResources and
                // PathfindingCellSize used to be read from this process's own
                // leftovers and never crossed the wire at all.
                string blob = startParts.Length > 5 ? startParts[5] : null;
                if (!MatchSettingsSync.Apply(blob, out string settingsError))
                {
                    _error = settingsError;
                    _state = LobbyState.BrowseGames;
                    return;
                }

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
            // The fingerprint rides the join so the host can refuse a mismatched
            // build at the door instead of mid-match. docs/Multiplayer_LAN_Readiness.md
            string msg = $"{MSG_JOIN}{_playerName}|{_clientPrivatePort}|{MatchSettingsSync.Fingerprint}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            try { _clientPrivateSocket.Send(data, data.Length, target); } catch { }
        }

        /// <summary>
        /// Index of the first slot tuple in a TWB_LOBBY message: one slot
        /// count, then the map/settings block. Named rather than written as a
        /// literal in two places, which is how the offset drifts.
        /// </summary>
        private const int LobbySlotOffset = 10;

        /// <summary>
        /// Make a free-text value safe to put in a delimited field. Game names
        /// and player names are typed by people, and a single '|' or ',' in one
        /// used to shift every field after it — the receiving lobby would read
        /// a colour index as a team, or drop the slot list entirely.
        /// </summary>
        private static string Field(string value) =>
            string.IsNullOrEmpty(value) ? "" : value.Replace('|', ' ').Replace(',', ' ');

        /// <summary>
        /// The advert body, identical on the broadcast and on the unicast
        /// TWB_FIND reply — built once so the two cannot drift.
        ///
        /// Map and slot counts were appended 2026-08-19 so the browser can
        /// show what a game IS before you commit to it. Occupancy counts every
        /// slot that is not Empty: an AI holds a seat exactly as a human does,
        /// and a joiner who reads "4/4" and finds three bots would rightly
        /// call that a lie.
        /// </summary>
        private string AdvertBody()
        {
            var maps = MapRegistry.Maps;
            string mapScene = maps.Count == 0 ? "" : maps[
                _selectedMapIndex >= 0 && _selectedMapIndex < maps.Count
                    ? _selectedMapIndex : 0].SceneName;

            int total = LobbyConfig.ActiveSlotCount;
            int used = 0;
            for (int i = 0; i < total; i++)
                if (_networkSlots[i].Type != SlotType.Empty) used++;

            return $"{Field(_gameName)}|{Field(_playerName)}|{_port}|" +
                   $"{Field(mapScene)}|{used}|{total}";
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
            // Appended 2026-08-19 with the match options and the editable game
            // name. Slot tuples therefore start at LobbySlotOffset, not 7 —
            // both ends move together, and a build with a different field list
            // is refused at the door by MatchSettingsSync.Fingerprint on JOIN.
            sb.Append($"|{(_maxResources ? 1 : 0)}|{(int)_startAge}|{Field(_gameName)}");

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                // TeamIndex is appended last so an older client that splits on
                // ',' and reads the first four fields still parses the rest.
                // Without it clients would disagree about who is allied, and
                // lockstep would desync on the first friendly-fire test.
                // docs/Design/Teams.md
                sb.Append($"|{(int)slot.Type},{Field(slot.PlayerName)},{(int)slot.AIDifficulty},{LobbyConfig.Slots[i].ColorIndex},{LobbyConfig.Slots[i].TeamIndex},{LobbyConfig.Slots[i].StartIndex},{(int)LobbyConfig.Slots[i].AIStrategy}");
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
            if (parts.Length > 7) _maxResources = int.Parse(parts[7]) != 0;
            if (parts.Length > 8 && int.TryParse(parts[8], out int age))
                _startAge = (SkirmishStartAge)age;
            if (parts.Length > 9) _gameName = parts[9];

            for (int i = 0; i < slotCount && i + LobbySlotOffset < parts.Length; i++)
            {
                var slotParts = parts[i + LobbySlotOffset].Split(',');
                if (slotParts.Length >= 3)
                {
                    _networkSlots[i].Type = (SlotType)int.Parse(slotParts[0]);
                    _networkSlots[i].PlayerName = slotParts[1];
                    _networkSlots[i].AIDifficulty = (LobbyAIDifficulty)int.Parse(slotParts[2]);
                    if (slotParts.Length >= 4)
                        LobbyConfig.Slots[i].ColorIndex = int.Parse(slotParts[3]);
                    if (slotParts.Length >= 5 && byte.TryParse(slotParts[4], out byte team))
                        LobbyConfig.Slots[i].TeamIndex = team;
                    if (slotParts.Length >= 6 && int.TryParse(slotParts[5], out int start))
                        LobbyConfig.Slots[i].StartIndex = start;
                    if (slotParts.Length >= 7 && int.TryParse(slotParts[6], out int strat))
                        LobbyConfig.Slots[i].AIStrategy = (LobbyAIStrategy)strat;
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

        // CycleSlotColor removed: the swatch opens ColorPickerPopup and the
        // chosen index travels as TWB_COLOR. docs/Design/Lobby_Setup.md

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

        /// <summary>
        /// Ask the host to set a slot to a SPECIFIC colour.
        ///
        /// Replaces the old cycle-then-send: the client used to advance its own
        /// colour and report where it landed, which meant sender and receiver
        /// could disagree whenever another player's change arrived in between.
        /// The picker names the colour it wants. docs/Design/Lobby_Setup.md
        /// </summary>
        private void SendColorSet(int slotIndex, int colorIndex)
        {
            // Optimistic local echo so the swatch responds immediately; the
            // host's next TWB_LOBBY broadcast is authoritative and will correct
            // this if the colour was taken in the meantime.
            LobbyConfig.Slots[slotIndex].ColorIndex = colorIndex;

            string msg = $"{MSG_COLOR}{slotIndex}|{colorIndex}";
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
            // Both ride to the client inside MatchSettingsSync.Capture() on
            // TWB_START, so setting them here is the whole job — StartCulture
            // is DERIVED from the age, never stored, for the reason spelled
            // out on LobbyOptions.CultureForAge.
            GameSettings.MaxStartingResources = _maxResources;
            GameSettings.StartAge = _startAge;
            GameSettings.StartCulture = LobbyOptions.CultureForAge(_startAge);
            GameSettings.IsMultiplayer = true;
            GameSettings.NetworkRole = NetworkRole.Server;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TotalPlayers = LobbyConfig.ActiveSlotCount;

            // Multiplayer means the fixed-step simulation, always. Set it
            // EXPLICITLY on both peers rather than relying on the default, so a
            // stale value from an earlier session can never decide whether this
            // match is deterministic. docs/Multiplayer_LAN_Readiness.md
            GameSettings.DeterministicLockstep = true;
            LockstepTiming.Reset();

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
            var chosenMap = mapList[_selectedMapIndex >= 0 && _selectedMapIndex < mapList.Count
                ? _selectedMapIndex : 0];
            string sceneName = chosenMap.SceneName;

            // A map whose terrain is generated at RUNTIME cannot be trusted to
            // come out bit-identical on two machines, and if the heightmaps
            // differ by one bit then every height sample, slope test and
            // passability cell differs and nothing else about determinism
            // matters. Every shipping map bakes its TerrainData as an asset;
            // this refuses the case rather than letting it desync silently.
            // docs/Multiplayer_LAN_Readiness.md
            if (chosenMap.IsProcedural)
            {
                _error = string.Format(
                    Loc.T("{0} generates its terrain at runtime, which cannot be guaranteed identical on both machines. Pick a map with baked terrain."),
                    chosenMap.DisplayName);
                return;
            }

            GameSettings.SelectedMapScene = sceneName;

            // Everything that shapes the simulated world travels as ONE blob so
            // adding a setting later is a change in MatchSettingsSync and
            // nowhere else. The five legacy positional fields stay for the
            // ports the client needs before it can parse anything.
            // docs/Multiplayer_LAN_Readiness.md
            string msg = $"{MSG_START}{_port}|{_spawnSeed}|{lockstepPort}|" +
                         $"{(_borderEnabled ? 1 : 0)}|{sceneName}|{MatchSettingsSync.Capture()}";
            byte[] data = Encoding.UTF8.GetBytes(msg);

            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = _networkSlots[i];
                if (slot.Type == SlotType.Human && slot.Endpoint != null)
                {
                    HostSend(data, new IPEndPoint(slot.Endpoint.Address, slot.ClientPort));
                }
            }

            // Launch-path forensics — see the SkirmishPanel counterpart.
            Debug.Log($"[MultiplayerPanel] Starting match as HOST — map {sceneName}, " +
                      $"seed {_spawnSeed}, players {LobbyConfig.ActiveSlotCount}, " +
                      $"lockstep port {lockstepPort}.");

            Cleanup();
            SceneManager.LoadScene(sceneName);
        }

        private void StartAsClient(string hostIp, ushort port, int seed, int lockstepPort)
        {
            // Launch-path forensics — see the SkirmishPanel counterpart.
            Debug.Log($"[MultiplayerPanel] Starting match as CLIENT — host {hostIp}:{port}, " +
                      $"seed {seed}, my slot {_mySlotIndex}.");

            GameSettings.IsMultiplayer = true;
            GameSettings.NetworkRole = NetworkRole.Client;
            GameSettings.LocalPlayerFaction = LobbyConfig.Slots[_mySlotIndex].Faction;
            GameSettings.TotalPlayers = LobbyConfig.ActiveSlotCount;

            // Seed and world shape already came from MatchSettingsSync.Apply —
            // do NOT overwrite them from this peer's own lobby widgets, which is
            // exactly how the two worlds used to disagree. The seed is passed
            // through only as a belt-and-braces check on the legacy field.
            if (GameSettings.SpawnSeed != seed) GameSettings.SpawnSeed = seed;

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
            _loggedAdvertTargets = false;
            _lastFindProbe = 0f;
        }

        /// <summary>Create a LockstepBootstrap that persists across scene loads.</summary>
        private static TheWaningBorder.Multiplayer.LockstepBootstrap CreateLockstepBootstrap()
        {
            if (TheWaningBorder.Multiplayer.LockstepBootstrap.Instance != null)
            {
                // DestroyImmediate, not Destroy: the deferred variant left
                // Instance pointing at the dying object until end of frame,
                // which made the replacement bootstrap self-destruct in its
                // Awake (see LockstepBootstrap.Awake) — the bug behind every
                // "second multiplayer match of the session runs solo" report.
                DestroyImmediate(TheWaningBorder.Multiplayer.LockstepBootstrap.Instance.gameObject);
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
