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

        /// <summary>
        /// Player indices whose tick confirmations gate CanAdvanceTick. NOT
        /// the same as _remotePlayers: a client only SENDS to the host (who
        /// relays), but it must WAIT for every human player's commands —
        /// otherwise relayed commands from a third player can arrive after
        /// their tick was already processed and get silently dropped on this
        /// peer only, diverging the simulations.
        /// </summary>
        private readonly HashSet<int> _expectedPlayers = new HashSet<int>();

        // ═══════════════════════════════════════════════════════════════════════
        // MATCH-START BARRIER
        // ═══════════════════════════════════════════════════════════════════════

        // In loose lockstep the frame-driven simulation starts the moment the
        // scene loads — the host's world ran seconds ahead while the client
        // was still loading (income accrual, veil growth, timers all offset,
        // permanently). Hold SimulationSystemGroup until the first TICK from
        // every expected player arrives, then release; both worlds then start
        // within one network round-trip of each other. Not used under
        // DeterministicLockstep (the fixed-step driver gates itself on ticks).
        private bool _simGateActive;
        private readonly HashSet<int> _seenPlayers = new HashSet<int>();

        /// <summary>Disable the simulation group until every expected player
        /// has been heard from. Call after StartSimulation, multiplayer only.</summary>
        public void HoldSimulationUntilPeersReady()
        {
            if (_expectedPlayers.Count == 0) return;
            if (GameSettings.DeterministicLockstep) return;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var simGroup = world.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
            if (simGroup == null) return;

            simGroup.Enabled = false;
            _simGateActive = true;
            TWBLog.Log("[Lockstep] Simulation held until all players are connected");
        }

        private void MaybeReleaseSimGate()
        {
            if (!_simGateActive) return;
            foreach (int p in _expectedPlayers)
            {
                if (!_seenPlayers.Contains(p)) return;
            }
            ReleaseSimGate();
            TWBLog.Log("[Lockstep] All players connected — simulation released");
        }

        private void ReleaseSimGate()
        {
            if (!_simGateActive) return;
            _simGateActive = false;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var simGroup = world.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
            if (simGroup != null) simGroup.Enabled = true;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK-ID LOOKUP CACHE
        // ═══════════════════════════════════════════════════════════════════════

        // One cached query + a per-tick NetworkId -> Entity map. The old code
        // created a fresh EntityQuery (never disposed — a leak that slowed
        // every structural change world-wide) and did a full O(N) scan PER
        // LOOKUP, 1-3 lookups per command: a 100-command fight tick cost
        // ~200k component reads. Now: one O(N) rebuild per tick, O(1) per
        // lookup. Entities created mid-tick by commands (PlaceBuilding) are
        // registered into the map at creation.
        private EntityQuery _networkedQuery;
        private bool _networkedQueryCreated;
        private readonly Dictionary<int, Entity> _networkIdLookup = new Dictionary<int, Entity>(1024);
        private int _networkIdLookupTick = -1;

        private EntityQuery GetNetworkedQuery(EntityManager em)
        {
            if (!_networkedQueryCreated)
            {
                _networkedQuery = em.CreateEntityQuery(typeof(NetworkedEntity));
                _networkedQueryCreated = true;
            }
            return _networkedQuery;
        }

        private void RebuildNetworkIdLookup(EntityManager em)
        {
            _networkIdLookup.Clear();
            _networkIdLookupTick = _currentTick;
            var query = GetNetworkedQuery(em);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkedEntity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                _networkIdLookup[ids[i].NetworkId] = entities[i];
        }
        
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
        
        // UDP has no retransmit: a lost/fragmented TICK datagram silently
        // dropped that tick's commands on the receiving peer only (later
        // ticks healed the CONFIRMATION, so the tick executed with no
        // commands — divergence). Re-send the last few non-empty tick
        // payloads alongside every broadcast; the 2-tick input delay gives
        // the resends time to land before the tick executes.
        private const int RESEND_HISTORY = 2;
        private readonly List<(int Tick, byte[] Data)> _recentTickPayloads = new List<(int, byte[])>();

        private List<LockstepCommand> _localCommandBuffer = new List<LockstepCommand>();
        private Dictionary<int, Dictionary<int, List<LockstepCommand>>> _remoteCommands = 
            new Dictionary<int, Dictionary<int, List<LockstepCommand>>>();
        private Dictionary<int, int> _confirmedTicks = new Dictionary<int, int>();
        
        // ═══════════════════════════════════════════════════════════════════════
        // SYNC VALIDATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private Dictionary<int, uint> _checksums = new Dictionary<int, uint>();
        private const int SYNC_CHECK_INTERVAL = 30; // Check every 30 ticks

        /// <summary>Set true when a per-tick checksum mismatch is detected.</summary>
        public bool DesyncDetected { get; private set; }
        /// <summary>The tick at which the desync was first observed.</summary>
        public int DesyncTick { get; private set; }
        
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
        /// Current simulation tick number. Used for deterministic seeding.
        /// </summary>
        public int CurrentTick => _currentTick;

        /// <summary>
        /// Queue a command for lockstep synchronization.
        /// </summary>
        public void QueueCommand(LockstepCommand cmd)
        {
            if (!_isSimulationRunning)
            {
                return;
            }

            cmd.PlayerIndex = _localPlayerIndex;
            cmd.Tick = _currentTick + INPUT_DELAY_TICKS;
            cmd.CommandIndex = _localCommandBuffer.Count;

            _localCommandBuffer.Add(cmd);

            // Always log commands during debugging
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
            ReleaseSimGate();

            // Release the cached query; guard against the world being torn
            // down first (disposing a query of a dead world throws).
            if (_networkedQueryCreated)
            {
                _networkedQueryCreated = false;
                try { _networkedQuery.Dispose(); } catch { }
            }
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
                    // Store local commands in _remoteCommands so ProcessTick executes them
                    // alongside any remote commands for deterministic execution order
                    int futureTick = _currentTick + INPUT_DELAY_TICKS;
                    if (_localCommandBuffer.Count > 0)
                    {
                        if (!_remoteCommands.ContainsKey(futureTick))
                            _remoteCommands[futureTick] = new Dictionary<int, List<LockstepCommand>>();
                        _remoteCommands[futureTick][_localPlayerIndex] = new List<LockstepCommand>(_localCommandBuffer);
                    }

                    // Confirm our own tick so we don't block ourselves
                    _confirmedTicks[_localPlayerIndex] = Math.Max(
                        _confirmedTicks.GetValueOrDefault(_localPlayerIndex, -1), futureTick);

                    // Broadcast local commands to remote players
                    BroadcastTick(futureTick, _localCommandBuffer);
                    _localCommandBuffer.Clear();

                    ProcessTick(_currentTick);
                    _currentTick++;
                    _tickAccumulator -= TICK_DURATION;
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

            _expectedPlayers.Clear();
            foreach (var p in _remotePlayers)
                _expectedPlayers.Add(p.PlayerIndex);

            StartNetwork();

        }

        /// <summary>
        /// Initialize as client. <paramref name="otherHumanPlayers"/> lists the
        /// lockstep indices of every OTHER human besides the host and us —
        /// their commands reach us relayed through the host, and we must wait
        /// for them before advancing a tick.
        /// </summary>
        public void InitializeAsClient(int localPort, string hostIP, int hostPort, int playerIndex, Faction faction,
            List<int> otherHumanPlayers = null)
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

            _expectedPlayers.Clear();
            _expectedPlayers.Add(0);
            if (otherHumanPlayers != null)
            {
                foreach (int idx in otherHumanPlayers)
                {
                    if (idx != _localPlayerIndex)
                        _expectedPlayers.Add(idx);
                }
            }

            StartNetwork();

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

            // Initialize confirmed ticks — start at INPUT_DELAY_TICKS so the first
            // CanAdvanceTick() calls succeed. Without this, both host and client deadlock
            // waiting for each other's tick confirmation before either can broadcast.
            _confirmedTicks[_localPlayerIndex] = INPUT_DELAY_TICKS;
            foreach (int playerIndex in _expectedPlayers)
            {
                _confirmedTicks[playerIndex] = INPUT_DELAY_TICKS;
            }
            
        }

        /// <summary>
        /// Stop the simulation
        /// </summary>
        public void StopSimulation()
        {
            _isSimulationRunning = false;
            // Never leave the world frozen behind a gate that can no longer lift.
            ReleaseSimGate();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK SETUP
        // ═══════════════════════════════════════════════════════════════════════

        private void SetupRemotePlayers(List<RemotePlayerInfo> players)
        {
            _remotePlayers.Clear();
            int sequentialIndex = 1;

            foreach (var info in players)
            {
                // Use the slot index the lobby registered (clients identify as
                // their slot index); sequential numbering is only a fallback
                // for legacy callers that never set PlayerIndex.
                int index = info.PlayerIndex > 0 ? info.PlayerIndex : sequentialIndex;
                sequentialIndex = index + 1;

                var remote = new RemotePlayer
                {
                    PlayerIndex = index,
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
            }
            catch (Exception)
            {
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
            if (_expectedPlayers.Count == 0) return true;

            // Check all players (direct peers AND host-relayed ones) have
            // confirmed the current tick
            foreach (int playerIndex in _expectedPlayers)
            {
                if (_confirmedTicks.GetValueOrDefault(playerIndex, -1) < _currentTick)
                    return false;
            }
            return true;
        }

        private void ProcessTick(int tick)
        {
            // Fix #199: partition the NetworkId space per-tick so any entity
            // spawned during this tick on either peer falls into a deterministic
            // slot range. Order divergence inside a tick then manifests as a
            // checksum desync rather than silent ID drift.
            TheWaningBorder.Core.Multiplayer.NetworkIdGenerator.BeginTick(tick);

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
            if (allCommands.Count > 0)
            {
                allCommands.Sort((a, b) =>
                {
                    int cmp = a.PlayerIndex.CompareTo(b.PlayerIndex);
                    return cmp != 0 ? cmp : a.CommandIndex.CompareTo(b.CommandIndex);
                });
            }

            foreach (var cmd in allCommands)
            {
                ExecuteCommand(cmd);
            }

            // TRUE-determinism path: advance the ECS simulation by EXACTLY one
            // fixed-dt step now, so "apply tick T's commands → simulate T" is
            // atomic and identical on every client. No-op unless the determinism
            // flag installed the fixed-step rate manager (otherwise the player
            // loop keeps driving the sim per-frame, as before).
            LockstepFixedStep.Step();

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

            // PlaceBuilding and Train don't require an existing entity lookup —
            // PlaceBuilding creates a new entity, Train uses EntityNetworkId for the building.
            // GodPower + EquipmentUpgrade pack the caster faction into
            // EntityNetworkId, not a real entity id, so they skip the lookup too.
            Entity entity = Entity.Null;
            bool needsEntity = cmd.Type != LockstepCommandType.SetRally
                            && cmd.Type != LockstepCommandType.PlaceBuilding
                            && cmd.Type != LockstepCommandType.GodPower
                            && cmd.Type != LockstepCommandType.EquipmentUpgrade;

            if (needsEntity)
            {
                entity = FindEntityByNetworkId(cmd.EntityNetworkId);
                if (entity == Entity.Null)
                {
                    return;
                }
            }
            else if (cmd.Type != LockstepCommandType.PlaceBuilding)
            {
                // SetRally and others that might use entity optionally
                entity = FindEntityByNetworkId(cmd.EntityNetworkId);
            }

            Entity targetEntity = cmd.TargetEntityId > 0 ? FindEntityByNetworkId(cmd.TargetEntityId) : Entity.Null;

            switch (cmd.Type)
            {
                case LockstepCommandType.Move:
                    MoveCommandHelper.Execute(em, entity, cmd.TargetPosition);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed Move from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.LayeredMove:
                    // RemotePlayer source takes the direct-execution branch in
                    // IssueLayeredMove (no re-queueing).
                    CommandRouter.IssueLayeredMove(em, entity, cmd.TargetPosition,
                        (byte)(cmd.TargetEntityId & 0xFF), CommandSource.RemotePlayer);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed LayeredMove from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Attack:
                    if (targetEntity != Entity.Null)
                    {
                        AttackCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Attack from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Stop:
                    CommandHelper.ClearAllCommands(em, entity);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed Stop from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Gather:
                    if (targetEntity != Entity.Null)
                    {
                        GatherCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Gather from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.GatherVeil:
                    GatherVeilCommandHelper.Execute(em, entity, cmd.TargetPosition);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed GatherVeil from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Build:
                    Entity buildTarget = cmd.TargetEntityId > 0 ? FindEntityByNetworkId(cmd.TargetEntityId) : Entity.Null;
                    BuildCommandHelper.Execute(em, entity, buildTarget, cmd.BuildingId, cmd.TargetPosition);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed Build from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Heal:
                    if (targetEntity != Entity.Null)
                    {
                        HealCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Heal from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.SetRally:
                    if (entity != Entity.Null)
                    {
                        if (!em.HasComponent<RallyPoint>(entity))
                            em.AddComponent<RallyPoint>(entity);
                        em.SetComponentData(entity, new RallyPoint { Position = cmd.TargetPosition, Has = 1 });
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed RallyPoint from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.AttackMove:
                    AttackMoveCommandHelper.Execute(em, entity, cmd.TargetPosition);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed AttackMove from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Repair:
                    if (targetEntity != Entity.Null)
                    {
                        RepairCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Repair from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Convert:
                    if (targetEntity != Entity.Null)
                    {
                        ConvertCommandHelper.Execute(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Convert from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Patrol:
                    PatrolCommandHelper.Execute(em, entity, cmd.TargetPosition);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed Patrol from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.HoldPosition:
                    HoldPositionCommandHelper.Execute(em, entity);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed HoldPosition from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Train:
                    if (entity != Entity.Null && em.HasBuffer<TrainQueueItem>(entity))
                    {
                        string unitId = cmd.BuildingId;
                        // Authoritative level gate — same check IssueTrain
                        // does on the originating peer. Drops silently on
                        // mismatch (replay can't notify; the local peer
                        // already heard about the rejection client-side).
                        if (!CommandRouter.CanTrainAtBuilding(em, entity, unitId, out _, out _))
                            break;
                        var queue = em.GetBuffer<TrainQueueItem>(entity);
                        queue.Add(new TrainQueueItem { UnitId = new Unity.Collections.FixedString64Bytes(unitId) });
                    }
                    break;

                case LockstepCommandType.AgeUp:
                    if (entity != Entity.Null)
                    {
                        CommandRouter.AgeUpCommandDirect(em, entity, (byte)(cmd.TargetEntityId & 0xFF));
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed AgeUp from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.TempleUpgrade:
                    if (entity != Entity.Null)
                    {
                        CommandRouter.TempleUpgradeCommandDirect(em, entity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed TempleUpgrade from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.SectAdopt:
                    if (entity != Entity.Null)
                    {
                        CommandRouter.SectAdoptionCommandDirect(em, entity, cmd.BuildingId,
                            cmd.TargetEntityId, cmd.TargetPosition.x);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed SectAdopt {cmd.BuildingId} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.BuildingUpgrade:
                    if (entity != Entity.Null)
                    {
                        UpgradeBuildingCommandHelper.ApplyDirect(em, entity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed BuildingUpgrade from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Research:
                    if (entity != Entity.Null)
                    {
                        CommandRouter.ResearchCommandDirect(em, entity, cmd.BuildingId);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Research {cmd.BuildingId} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.CancelTrain:
                    if (entity != Entity.Null)
                    {
                        int cancelSlot = cmd.TargetEntityId;
                        CancelTrainCommandHelper.Execute(em, entity, cancelSlot);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed CancelTrain slot={cancelSlot} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.ConvertHut:
                    if (entity != Entity.Null)
                    {
                        var convertTarget = (HutConversionTarget)(byte)(cmd.TargetEntityId & 0xFF);
                        ConvertHutCommandHelper.Execute(em, entity, convertTarget);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed ConvertHut target={convertTarget} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.ConvertSegmentToGate:
                    if (entity != Entity.Null)
                    {
                        // TargetEntityId carries the focus-instance network id
                        // (0 = no focus → fallback to segment midpoint).
                        Entity focus = cmd.TargetEntityId != 0
                            ? FindEntityByNetworkId(cmd.TargetEntityId)
                            : Entity.Null;
                        ConvertSegmentToGateCommandHelper.Execute(em, entity, focus);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed ConvertSegmentToGate from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.PlaceBuilding:
                    {
                        Faction buildFaction = (Faction)cmd.EntityNetworkId;
                        var placed = CommandRouter.PlaceBuildingDirect(em, cmd.BuildingId, cmd.TargetPosition, buildFaction);
                        // Register in the per-tick lookup so a later command in
                        // THIS tick (e.g. Build targeting the new foundation)
                        // resolves it without a rebuild.
                        if (placed != Entity.Null && em.HasComponent<NetworkedEntity>(placed))
                            _networkIdLookup[em.GetComponentData<NetworkedEntity>(placed).NetworkId] = placed;
                    }
                    break;

                case LockstepCommandType.Ability:
                    {
                        // EntityNetworkId is the ability *target* (or 0 for self/none).
                        // The actor was already resolved into `entity` above via the
                        // command's source-entity mapping. Renamed to avoid shadowing
                        // the enclosing `targetEntity` declared earlier in this method
                        // for the AttackCommand path.
                        Entity abilityTarget = cmd.EntityNetworkId != 0
                            ? FindEntityByNetworkId(cmd.EntityNetworkId)
                            : Entity.Null;
                        CommandRouter.IssueAbilityDirect(em, entity, abilityTarget);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Ability from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.Purify:
                    if (entity != Entity.Null && targetEntity != Entity.Null)
                    {
                        CommandRouter.IssuePurifyDirect(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Purify from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.ConvertNode:
                    if (entity != Entity.Null && targetEntity != Entity.Null)
                    {
                        CommandRouter.IssueConvertNodeDirect(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed ConvertNode from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.EquipmentUpgrade:
                    {
                        Faction caster = (Faction)cmd.EntityNetworkId;
                        UnitClass cls = (UnitClass)(byte)(cmd.TargetEntityId & 0xFF);
                        EquipmentTier tier = (EquipmentTier)(byte)((cmd.TargetEntityId >> 8) & 0xFF);
                        CommandRouter.IssueEquipmentUpgradeDirect(em, caster, cls, tier);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed EquipmentUpgrade {caster}/{cls}->{tier} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.GodPower:
                    {
                        Faction caster = (Faction)cmd.EntityNetworkId;
                        CommandRouter.IssueGodPowerDirect(em, caster, cmd.TargetPosition);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed GodPower {caster} from player {cmd.PlayerIndex}");
                    }
                    break;
            }
        }

        private Entity FindEntityByNetworkId(int networkId)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return Entity.Null;

            var em = world.EntityManager;
            if (_networkIdLookupTick != _currentTick)
                RebuildNetworkIdLookup(em);

            if (_networkIdLookup.TryGetValue(networkId, out var entity) && em.Exists(entity))
                return entity;
            return Entity.Null;
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
                    // Resend recent non-empty ticks FIRST so a receiver that
                    // lost an earlier datagram has those commands before this
                    // tick's confirmation lets it advance past them.
                    for (int i = 0; i < _recentTickPayloads.Count; i++)
                    {
                        var payload = _recentTickPayloads[i].Data;
                        _udpClient?.Send(payload, payload.Length, player.EndPoint);
                    }
                    _udpClient?.Send(data, data.Length, player.EndPoint);
                }
                catch (Exception)
            {
                }
            }

            if (commands.Count > 0)
                _recentTickPayloads.Add((tick, data));
            while (_recentTickPayloads.Count > 0 && _recentTickPayloads[0].Tick <= tick - RESEND_HISTORY)
                _recentTickPayloads.RemoveAt(0);
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
            catch (Exception)
            {
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

            // First contact from this player lifts the match-start barrier
            // once everyone has been heard from.
            _seenPlayers.Add(playerIndex);
            MaybeReleaseSimGate();

            _confirmedTicks[playerIndex] = Math.Max(_confirmedTicks.GetValueOrDefault(playerIndex, -1), tick);

            // Store commands only for ticks not yet executed — resends and
            // duplicates for already-processed ticks would just grow the
            // buffer (the tick ran; its outcome cannot be revised).
            if (tick >= _currentTick)
            {
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
            }

            // Host relays to other clients (resends included — a client may
            // still be behind the tick this host already executed).
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
                    DesyncDetected = true;
                    DesyncTick = tick;
                    // Only ACT on a desync in true-deterministic mode. With the
                    // fixed-step OFF (the default), the simulation is frame-rate
                    // driven, so positions/timers legitimately drift between
                    // clients and the checksum mismatches every sync tick — that
                    // is EXPECTED here, not a real desync. Halting on it would
                    // freeze the game (and stop move commands from executing).
                    if (GameSettings.DeterministicLockstep)
                    {
                        UnityEngine.Debug.LogError(
                            $"[Lockstep] DESYNC at tick {tick}: local checksum 0x{localChecksum:X8} " +
                            $"!= remote 0x{remoteChecksum:X8} (from {sender}). Halting simulation.");
                        _isSimulationRunning = false;
                    }
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

            // Checksum based on entity count + health (game-logic state).
            // Positions are NOT included because movement uses frame-rate-dependent
            // deltaTime, causing tiny floating-point drift between clients.
            // Commands are still synchronized via lockstep — drift is cosmetic only.
            var query = GetNetworkedQuery(em);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkedEntity>(Allocator.Temp);

            checksum ^= (uint)(entities.Length * 31);

            for (int i = 0; i < entities.Length; i++)
            {
                checksum ^= (uint)(ids[i].NetworkId * 7919);

                // Include health if present — tracks combat state
                if (em.HasComponent<Health>(entities[i]))
                {
                    var hp = em.GetComponentData<Health>(entities[i]);
                    checksum ^= (uint)(hp.Value * 17 + hp.Max * 53);
                }
            }

            return checksum;
        }
    }
}