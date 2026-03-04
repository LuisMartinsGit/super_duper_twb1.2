// LockstepBootstrap.cs
// Scene-aware bootstrap for lockstep initialization
// Part of: Multiplayer/Lockstep/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Multiplayer;  

namespace TheWaningBorder.Multiplayer
{
    /// <summary>
    /// Bootstrap component that initializes lockstep multiplayer when the Game scene loads.
    /// 
    /// Created by MultiplayerLobbyUI before loading the game scene.
    /// Persists across scene loads (DontDestroyOnLoad) to carry configuration.
    /// 
    /// Initialization Flow:
    /// 1. Lobby UI creates LockstepBootstrap and sets configuration
    /// 2. Scene transitions to "Game"
    /// 3. OnSceneLoaded triggers and initializes LockstepManager
    /// 4. Lockstep simulation begins
    /// </summary>
    public class LockstepBootstrap : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════════
        
        public static LockstepBootstrap Instance { get; private set; }

        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION (Set by Lobby before scene load)
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>Is this instance the game host?</summary>
        public bool IsHost { get; set; }
        
        /// <summary>Local player's index (0 = host)</summary>
        public int LocalPlayerIndex { get; set; }
        
        /// <summary>Local player's faction</summary>
        public Faction LocalFaction { get; set; }
        
        /// <summary>Local UDP port for lockstep communication</summary>
        public int LocalPort { get; set; }
        
        /// <summary>Host's IP address (for clients)</summary>
        public string HostIP { get; set; }
        
        /// <summary>Host's lockstep port</summary>
        public int HostPort { get; set; }
        
        /// <summary>Information about other players (for host)</summary>
        public List<RemotePlayerInfo> RemotePlayers { get; set; } = new List<RemotePlayerInfo>();

        // ═══════════════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private bool _initialized = false;

        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SCENE HANDLING
        // ═══════════════════════════════════════════════════════════════════════

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only initialize in the Game scene
            if (scene.name != "Game") return;
            
            // Don't re-initialize
            if (_initialized) return;
            
            // Only for multiplayer games
            if (!GameSettings.IsMultiplayer) return;

            StartCoroutine(InitializeLockstep());
        }

        private IEnumerator InitializeLockstep()
        {
            // Wait for scene to fully load and entities to be created
            yield return null;
            yield return null;

            Debug.Log($"[LockstepBootstrap] Initializing - IsHost: {IsHost}, LocalPlayer: {LocalPlayerIndex}, Faction: {LocalFaction}");

            // Create LockstepManager if needed
            var lockstep = LockstepManager.Instance;
            if (lockstep == null)
            {
                var go = new GameObject("LockstepManager");
                lockstep = go.AddComponent<LockstepManager>();
            }

            // Initialize based on role
            if (IsHost)
            {
                lockstep.InitializeAsHost(HostPort, RemotePlayers);
            }
            else
            {
                lockstep.InitializeAsClient(LocalPort, HostIP, HostPort, LocalPlayerIndex, LocalFaction);
            }

            // Wait for network ID assignment
            yield return null;

            // Start simulation
            lockstep.StartSimulation();

            _initialized = true;
            Debug.Log("[LockstepBootstrap] Lockstep simulation started!");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Configure as host with the given port and player list.
        /// </summary>
        public void ConfigureAsHost(int port, List<RemotePlayerInfo> remotePlayers)
        {
            IsHost = true;
            LocalPlayerIndex = 0;
            LocalFaction = Faction.Blue;
            HostPort = port;
            RemotePlayers = remotePlayers ?? new List<RemotePlayerInfo>();
            
            Debug.Log($"[LockstepBootstrap] Configured as HOST on port {port} with {RemotePlayers.Count} remote players");
        }

        /// <summary>
        /// Configure as client connecting to the specified host.
        /// </summary>
        public void ConfigureAsClient(string hostIP, int hostPort, int localPort, int playerIndex, Faction faction)
        {
            IsHost = false;
            HostIP = hostIP;
            HostPort = hostPort;
            LocalPort = localPort;
            LocalPlayerIndex = playerIndex;
            LocalFaction = faction;
            
            Debug.Log($"[LockstepBootstrap] Configured as CLIENT - Host: {hostIP}:{hostPort}, LocalPort: {localPort}, Player: {playerIndex}");
        }

        /// <summary>
        /// Add a remote player (host only).
        /// </summary>
        public void AddRemotePlayer(string ip, int port, Faction faction)
        {
            RemotePlayers.Add(new RemotePlayerInfo
            {
                IP = ip,
                Port = port,
                Faction = faction
            });
        }

        /// <summary>
        /// Reset configuration for a new game.
        /// </summary>
        public void Reset()
        {
            _initialized = false;
            IsHost = false;
            LocalPlayerIndex = 0;
            LocalFaction = Faction.Blue;
            LocalPort = 0;
            HostIP = null;
            HostPort = 0;
            RemotePlayers.Clear();
        }
    }
}