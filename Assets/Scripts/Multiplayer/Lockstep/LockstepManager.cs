// LockstepManager.cs
// Lockstep multiplayer manager for deterministic simulation
// Location: Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Commands;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Multiplayer
{
    /// <summary>
    /// Lockstep multiplayer manager.
    /// Implements ILockstepService to allow Core assemblies to queue commands
    /// without circular dependency.
    /// 
    /// How it works:
    /// 1. Game runs in discrete "ticks" (e.g., 10 ticks per second)
    /// 2. Player commands are collected locally but NOT executed immediately
    /// 3. Commands are sent to all players with a target tick number
    /// 4. Simulation only advances when ALL players have confirmed their commands for that tick
    /// 5. All players execute the same commands on the same tick = deterministic
    /// 
    /// Network Protocol:
    /// - TICK|playerIndex|tickNumber|commandCount|cmd1|cmd2|...  (Player sends commands)
    /// - SYNC|tickNumber|checksum                                 (Periodic sync check)
    /// - PING|timestamp                                           (Latency measurement)
    /// - PONG|timestamp                                           (Latency response)
    /// </summary>
    public class LockstepManager : MonoBehaviour, ILockstepService
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════════
        
        public static LockstepManager Instance { get; private set; }

        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        
        public const int TICKS_PER_SECOND = 10;
        public const float TICK_DURATION = 1f / TICKS_PER_SECOND;
        public const int INPUT_DELAY_TICKS = 2;
        public const int MAX_TICK_BUFFER = 60;
        private Faction _localFaction;
        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private UdpClient _udpClient;
        private int _localPort;
        private bool _isHost;
        private List<RemotePlayer> _remotePlayers = new List<RemotePlayer>();
        
        // ═══════════════════════════════════════════════════════════════════════
        // SIMULATION STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private int _currentTick;
        private float _tickAccumulator;
        private bool _isSimulationRunning;
        private int _localPlayerIndex;
        
        // ═══════════════════════════════════════════════════════════════════════
        // COMMAND BUFFERS
        // ═══════════════════════════════════════════════════════════════════════
        
        private List<LockstepCommand> _localCommandBuffer = new List<LockstepCommand>();
        private Dictionary<int, Dictionary<int, List<LockstepCommand>>> _remoteCommands = 
            new Dictionary<int, Dictionary<int, List<LockstepCommand>>>();
        private Dictionary<int, int> _confirmedTicks = new Dictionary<int, int>();
        
        // ═══════════════════════════════════════════════════════════════════════
        // SYNC VALIDATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private Dictionary<int, uint> _checksums = new Dictionary<int, uint>();
        private const int SYNC_CHECK_INTERVAL = 30; // Check every 30 ticks
        
        // ═══════════════════════════════════════════════════════════════════════
        // DEBUG
        // ═══════════════════════════════════════════════════════════════════════
        
        public bool LogTicks = false;
        public bool LogCommands = false;

        // ═══════════════════════════════════════════════════════════════════════
        // ILockstepService IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Whether the lockstep simulation is currently running.
        /// </summary>
        public bool IsSimulationRunning => _isSimulationRunning;

        /// <summary>
        /// Whether this instance is the host (server).
        /// </summary>
        public bool IsHost => _isHost;

        /// <summary>
        /// Queue a command for lockstep synchronization.
        /// </summary>
        public void QueueCommand(LockstepCommand cmd)
        {
            if (!_isSimulationRunning) return;
            
            cmd.PlayerIndex = _localPlayerIndex;
            cmd.Tick = _currentTick + INPUT_DELAY_TICKS;
            cmd.CommandIndex = _localCommandBuffer.Count;
            
            _localCommandBuffer.Add(cmd);
            
            if (LogCommands)
                Debug.Log($"[Lockstep] Queued command type {cmd.Type} for tick {cmd.Tick}");
        }

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
            
            // Register with service locator
            LockstepServiceLocator.Register(this);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                LockstepServiceLocator.Unregister(this);
            }
            StopNetwork();
        }

        void Update()
        {
            if (!_isSimulationRunning) return;

            ReceiveNetworkMessages();
            
            _tickAccumulator += Time.deltaTime;
            
            while (_tickAccumulator >= TICK_DURATION)
            {
                if (CanAdvanceTick())
                {
                    ProcessTick(_currentTick);
                    _currentTick++;
                    _tickAccumulator -= TICK_DURATION;
                    
                    // Send our commands for future tick
                    BroadcastTick(_currentTick + INPUT_DELAY_TICKS, _localCommandBuffer);
                    _localCommandBuffer.Clear();
                }
                else
                {
                    // Waiting for other players
                    break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initialize as host
        /// </summary>
        public void InitializeAsHost(int port, List<RemotePlayerInfo> players)
        {
            _isHost = true;
            _localPlayerIndex = 0;
            _localPort = port;
            
            SetupRemotePlayers(players);
            StartNetwork();
            
            Debug.Log($"[Lockstep] Initialized as HOST on port {port} with {players.Count} remote players");
        }

        /// <summary>
        /// Initialize as client
        /// </summary>
        public void InitializeAsClient(int localPort, string hostIP, int hostPort, int playerIndex, Faction faction)
        {
            _isHost = false;
            _localPlayerIndex = playerIndex;
            _localPort = localPort;
            _localFaction = faction; 
            
            var hostPlayer = new RemotePlayer
            {
                PlayerIndex = 0,
                EndPoint = new IPEndPoint(IPAddress.Parse(hostIP), hostPort)
            };
            _remotePlayers.Add(hostPlayer);
            
            StartNetwork();
            
            Debug.Log($"[Lockstep] Initialized as CLIENT (player {playerIndex}) connecting to {hostIP}:{hostPort}");
        }

        /// <summary>
        /// Start the lockstep simulation
        /// </summary>
        public void StartSimulation()
        {
            _currentTick = 0;
            _tickAccumulator = 0;
            _isSimulationRunning = true;
            _localCommandBuffer.Clear();
            _remoteCommands.Clear();
            _confirmedTicks.Clear();
            _checksums.Clear();
            
            // Initialize confirmed ticks for all players
            _confirmedTicks[_localPlayerIndex] = -1;
            foreach (var player in _remotePlayers)
            {
                _confirmedTicks[player.PlayerIndex] = -1;
            }
            
            Debug.Log("[Lockstep] Simulation started");
        }

        /// <summary>
        /// Stop the simulation
        /// </summary>
        public void StopSimulation()
        {
            _isSimulationRunning = false;
            Debug.Log($"[Lockstep] Simulation stopped at tick {_currentTick}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK SETUP
        // ═══════════════════════════════════════════════════════════════════════

        private void SetupRemotePlayers(List<RemotePlayerInfo> players)
        {
            _remotePlayers.Clear();
            int playerIndex = 1;
            
            foreach (var info in players)
            {
                var remote = new RemotePlayer
                {
                    PlayerIndex = playerIndex++,
                    Faction = info.Faction,
                    EndPoint = new IPEndPoint(IPAddress.Parse(info.IP), info.Port),
                    LastConfirmedTick = -1
                };
                _remotePlayers.Add(remote);
            }
        }

        private void StartNetwork()
        {
            try
            {
                _udpClient = new UdpClient(_localPort);
                _udpClient.Client.Blocking = false;
                Debug.Log($"[Lockstep] Network started on port {_localPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lockstep] Failed to start network: {e.Message}");
            }
        }

        private void StopNetwork()
        {
            _udpClient?.Close();
            _udpClient = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TICK PROCESSING
        // ═══════════════════════════════════════════════════════════════════════

        private bool CanAdvanceTick()
        {
            // In single player, always advance
            if (_remotePlayers.Count == 0) return true;
            
            // Check all players have confirmed the current tick
            foreach (var player in _remotePlayers)
            {
                if (_confirmedTicks.GetValueOrDefault(player.PlayerIndex, -1) < _currentTick)
                    return false;
            }
            return true;
        }

        private void ProcessTick(int tick)
        {
            if (LogTicks)
                Debug.Log($"[Lockstep] Processing tick {tick}");

            // Gather all commands for this tick
            var allCommands = new List<LockstepCommand>();
            
            // Remote commands
            if (_remoteCommands.TryGetValue(tick, out var tickCommands))
            {
                foreach (var playerCommands in tickCommands.Values)
                {
                    allCommands.AddRange(playerCommands);
                }
            }

            // Sort for determinism (by player index, then command index)
            allCommands.Sort((a, b) =>
            {
                int cmp = a.PlayerIndex.CompareTo(b.PlayerIndex);
                return cmp != 0 ? cmp : a.CommandIndex.CompareTo(b.CommandIndex);
            });

            if (LogTicks && allCommands.Count > 0)
                Debug.Log($"[Lockstep] Tick {tick} executing {allCommands.Count} commands");

            foreach (var cmd in allCommands)
            {
                ExecuteCommand(cmd);
            }
            
            // Cleanup old tick data
            _remoteCommands.Remove(tick - MAX_TICK_BUFFER);
            
            // Periodic sync check
            if (tick % SYNC_CHECK_INTERVAL == 0)
            {
                uint checksum = ComputeGameStateChecksum();
                _checksums[tick] = checksum;
                BroadcastSync(tick, checksum);
            }
        }

        private void ExecuteCommand(LockstepCommand cmd)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            Entity entity = FindEntityByNetworkId(cmd.EntityNetworkId);
            if (entity == Entity.Null && cmd.Type != LockstepCommandType.SetRally)
            {
                if (LogCommands)
                    Debug.LogWarning($"[Lockstep] Entity not found for network ID {cmd.EntityNetworkId}");
                return;
            }

            Entity targetEntity = cmd.TargetEntityId > 0 ? FindEntityByNetworkId(cmd.TargetEntityId) : Entity.Null;

            switch (cmd.Type)
            {
                case LockstepCommandType.Move:
                    MoveCommandHelper.Execute(em, entity, cmd.TargetPosition);
                    if (LogCommands) Debug.Log($"[Lockstep] Executed Move from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Attack:
                    if (targetEntity != Entity.Null)
                    {
                        AttackCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) Debug.Log($"[Lockstep] Executed Attack from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Stop:
                    CommandHelper.ClearAllCommands(em, entity);
                    if (LogCommands) Debug.Log($"[Lockstep] Executed Stop from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Gather:
                    Entity depositEntity = cmd.SecondaryTargetId > 0 ? FindEntityByNetworkId(cmd.SecondaryTargetId) : Entity.Null;
                    if (targetEntity != Entity.Null)
                    {
                        GatherCommandHelper.Execute(em, entity, targetEntity, depositEntity);
                        if (LogCommands) Debug.Log($"[Lockstep] Executed Gather from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Build:
                    Entity buildTarget = cmd.TargetEntityId > 0 ? FindEntityByNetworkId(cmd.TargetEntityId) : Entity.Null;
                    BuildCommandHelper.Execute(em, entity, buildTarget, cmd.BuildingId, cmd.TargetPosition);
                    if (LogCommands) Debug.Log($"[Lockstep] Executed Build from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Heal:
                    if (targetEntity != Entity.Null)
                    {
                        HealCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) Debug.Log($"[Lockstep] Executed Heal from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.SetRally:
                    if (entity != Entity.Null)
                    {
                        if (!em.HasComponent<RallyPoint>(entity))
                            em.AddComponent<RallyPoint>(entity);
                        em.SetComponentData(entity, new RallyPoint { Position = cmd.TargetPosition, Has = 1 });
                        if (LogCommands) Debug.Log($"[Lockstep] Executed RallyPoint from player {cmd.PlayerIndex}");
                    }
                    break;
            }
        }

        private Entity FindEntityByNetworkId(int networkId)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return Entity.Null;

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(typeof(NetworkedEntity));
            var entities = query.ToEntityArray(Allocator.Temp);

            Entity result = Entity.Null;
            for (int i = 0; i < entities.Length; i++)
            {
                if (em.GetComponentData<NetworkedEntity>(entities[i]).NetworkId == networkId)
                {
                    result = entities[i];
                    break;
                }
            }
            
            entities.Dispose();
            return result;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK - SEND
        // ═══════════════════════════════════════════════════════════════════════

        private void BroadcastTick(int tick, List<LockstepCommand> commands)
        {
            var sb = new StringBuilder();
            sb.Append($"TICK|{_localPlayerIndex}|{tick}|{commands.Count}");

            foreach (var cmd in commands)
            {
                sb.Append("|");
                sb.Append(cmd.Serialize());
            }

            string message = sb.ToString();
            byte[] data = Encoding.UTF8.GetBytes(message);

            foreach (var player in _remotePlayers)
            {
                try
                {
                    _udpClient?.Send(data, data.Length, player.EndPoint);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lockstep] Failed to send to player {player.PlayerIndex}: {e.Message}");
                }
            }
        }

        private void BroadcastSync(int tick, uint checksum)
        {
            string message = $"SYNC|{tick}|{checksum}";
            byte[] data = Encoding.UTF8.GetBytes(message);

            foreach (var player in _remotePlayers)
            {
                try
                {
                    _udpClient?.Send(data, data.Length, player.EndPoint);
                }
                catch { }
            }
        }

        private void RelayTickMessage(string message, int originalSender)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            
            foreach (var player in _remotePlayers)
            {
                if (player.PlayerIndex != originalSender)
                {
                    try
                    {
                        _udpClient?.Send(data, data.Length, player.EndPoint);
                    }
                    catch { }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK - RECEIVE
        // ═══════════════════════════════════════════════════════════════════════

        private void ReceiveNetworkMessages()
        {
            if (_udpClient == null) return;

            try
            {
                while (_udpClient.Available > 0)
                {
                    IPEndPoint sender = null;
                    byte[] data = _udpClient.Receive(ref sender);
                    string message = Encoding.UTF8.GetString(data);
                    ProcessNetworkMessage(message, sender);
                }
            }
            catch (SocketException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lockstep] Network receive error: {e.Message}");
            }
        }

        private void ProcessNetworkMessage(string message, IPEndPoint sender)
        {
            string[] parts = message.Split('|');
            if (parts.Length < 1) return;

            switch (parts[0])
            {
                case "TICK":
                    ProcessTickMessage(parts, sender);
                    break;
                case "SYNC":
                    ProcessSyncMessage(parts, sender);
                    break;
                case "PING":
                    SendPong(sender, parts.Length > 1 ? parts[1] : "0");
                    break;
                case "PONG":
                    break;
            }
        }

        private void ProcessTickMessage(string[] parts, IPEndPoint sender)
        {
            if (parts.Length < 4) return;

            if (!int.TryParse(parts[1], out int playerIndex)) return;
            if (!int.TryParse(parts[2], out int tick)) return;
            if (!int.TryParse(parts[3], out int cmdCount)) return;

            var commands = new List<LockstepCommand>();
            int cmdStartIndex = 4;
            for (int i = 0; i < cmdCount && cmdStartIndex < parts.Length; i++)
            {
                var cmd = LockstepCommand.Deserialize(parts[cmdStartIndex]);
                if (cmd != null)
                {
                    cmd.PlayerIndex = playerIndex;
                    cmd.Tick = tick;
                    commands.Add(cmd);
                }
                cmdStartIndex++;
            }

            if (!_remoteCommands.ContainsKey(tick))
                _remoteCommands[tick] = new Dictionary<int, List<LockstepCommand>>();

            _remoteCommands[tick][playerIndex] = commands;
            _confirmedTicks[playerIndex] = Math.Max(_confirmedTicks.GetValueOrDefault(playerIndex, -1), tick);

            if (LogCommands)
                Debug.Log($"[Lockstep] Received tick {tick} from player {playerIndex} with {cmdCount} commands");

            // Host relays to other clients
            if (_isHost)
            {
                string originalMessage = string.Join("|", parts);
                RelayTickMessage(originalMessage, playerIndex);
            }
        }

        private void ProcessSyncMessage(string[] parts, IPEndPoint sender)
        {
            if (parts.Length < 3) return;

            if (!int.TryParse(parts[1], out int tick)) return;
            if (!uint.TryParse(parts[2], out uint remoteChecksum)) return;

            if (_checksums.TryGetValue(tick, out uint localChecksum))
            {
                if (localChecksum != remoteChecksum)
                {
                    Debug.LogError($"[Lockstep] DESYNC DETECTED at tick {tick}! Local: {localChecksum}, Remote: {remoteChecksum}");
                }
            }
        }

        private void SendPong(IPEndPoint target, string timestamp)
        {
            string message = $"PONG|{timestamp}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            try
            {
                _udpClient?.Send(data, data.Length, target);
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SYNC VALIDATION
        // ═══════════════════════════════════════════════════════════════════════

        private uint ComputeGameStateChecksum()
        {
            uint checksum = 0;
            
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return checksum;

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(typeof(NetworkedEntity), typeof(Unity.Transforms.LocalTransform));
            var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var netEntity = em.GetComponentData<NetworkedEntity>(entities[i]);
                var transform = em.GetComponentData<Unity.Transforms.LocalTransform>(entities[i]);
                
                checksum ^= (uint)netEntity.NetworkId;
                checksum ^= (uint)(transform.Position.x * 100);
                checksum ^= (uint)(transform.Position.z * 100);
            }
            
            entities.Dispose();
            return checksum;
        }
    }
}