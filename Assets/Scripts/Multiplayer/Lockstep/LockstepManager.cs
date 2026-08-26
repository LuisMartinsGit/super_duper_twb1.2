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
using Unity.Mathematics;
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
        
        // Tick rate and input delay live in Core so the match-settings sync can
        // put them on the wire; these forward to it. They are no longer consts —
        // the tick rate is a per-match value the host chooses, and the input
        // delay adapts to measured latency. docs/Multiplayer_LAN_Readiness.md
        public static int TICKS_PER_SECOND => LockstepTiming.TicksPerSecond;
        public static float TICK_DURATION => LockstepTiming.TickDuration;
        public static int INPUT_DELAY_TICKS => LockstepTiming.InputDelayTicks;

        /// <summary>Ticks of command history kept for replay/late arrival.</summary>
        public const int MAX_TICK_BUFFER = 180;
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

        /// <summary>
        /// How many ticks of sent payloads are kept for retransmission. Must
        /// comfortably exceed the input delay: a peer blocked on tick T needs
        /// us to still be holding T, and T is up to InputDelayTicks behind the
        /// tick we last sent. 45 ticks is 1.5 s at 30 Hz.
        /// </summary>
        private const int RetainTicks = 45;

        /// <summary>Seconds between stall retransmissions. Fast enough to
        /// repair a lost datagram long before the disconnect timeout, slow
        /// enough that it is not a flood.</summary>
        private const float StallResendInterval = 0.15f;

        private float _nextStallResendAt;
        private readonly List<(int Tick, byte[] Data)> _recentTickPayloads = new List<(int, byte[])>();

        private List<LockstepCommand> _localCommandBuffer = new List<LockstepCommand>();
        private Dictionary<int, Dictionary<int, List<LockstepCommand>>> _remoteCommands = 
            new Dictionary<int, Dictionary<int, List<LockstepCommand>>>();
        private Dictionary<int, int> _confirmedTicks = new Dictionary<int, int>();
        
        // ═══════════════════════════════════════════════════════════════════════
        // SYNC VALIDATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private Dictionary<int, uint> _checksums = new Dictionary<int, uint>();

        /// <summary>
        /// Sync every ~1 second of simulated time. Expressed in ticks, so it
        /// follows the tick rate instead of drifting when the rate changes.
        /// </summary>
        private static int SYNC_CHECK_INTERVAL => LockstepTiming.TicksPerSecond;

        /// <summary>How many past sync points stay comparable.</summary>
        private const int ChecksumHistoryTicks = 8;

        /// <summary>Networked entity count at the last checksum, for the log.</summary>
        private int _lastChecksumEntityCount;

        /// <summary>Set true when a per-tick checksum mismatch is detected.</summary>
        public bool DesyncDetected { get; private set; }
        /// <summary>The tick at which the desync was first observed.</summary>
        public int DesyncTick { get; private set; }

        // ═══════════════════════════════════════════════════════════════════════
        // LIVENESS, LATENCY AND STALL REPORTING
        // ═══════════════════════════════════════════════════════════════════════
        //
        // A peer that quit used to freeze the other one forever: CanAdvanceTick
        // never returns true again, and in the old frame-driven mode the world
        // kept animating while every order was silently ignored — a game that
        // looks alive but is deaf, with nothing on screen to explain it.
        //
        // These fields are what the network HUD reads. They are deliberately
        // plain properties rather than events: the HUD polls once a frame and
        // nothing else in the simulation may depend on them (they are wall-clock
        // derived and therefore NOT deterministic state).

        /// <summary>Wall-clock seconds since we last heard anything from a peer.</summary>
        private readonly Dictionary<int, float> _lastHeardFrom = new Dictionary<int, float>();
        /// <summary>Measured round-trip time per peer, milliseconds.</summary>
        private readonly Dictionary<int, float> _latencyMs = new Dictionary<int, float>();
        private float _nextPingAt;
        private const float PingInterval = 1f;

        /// <summary>Seconds of silence before a peer is reported as stalled.</summary>
        public const float StallWarnSeconds = 1.0f;
        /// <summary>Seconds of silence before a peer is treated as gone.</summary>
        public const float StallDropSeconds = 15f;

        /// <summary>
        /// Seconds a match may be stuck on one peer before it is ended.
        ///
        /// Two DIFFERENT conditions both count against this, because either
        /// one means the match can never continue:
        ///   * silence  — nothing at all from that peer, the plain disconnect;
        ///   * deadlock — packets still arriving (pings, pongs) but their tick
        ///                never advances, so lockstep waits forever.
        ///
        /// The second is the one that actually happens. In the 2026-08-26
        /// match both peers sat in the deadlock for 39 s and 47 s, a warning
        /// per second, until each player alt-tabbed out and quit by hand:
        /// PeerLost never fired, because the pings kept the silence timer
        /// fresh the whole time.
        /// </summary>
        public const float DisconnectEndSeconds = 10f;

        /// <summary>Set once the match has been ended by a disconnect, so the
        /// teardown runs exactly once however many peers time out.</summary>
        private bool _endedOnDisconnect;

        /// <summary>
        /// The player index this peer is currently blocked on, or -1 when the
        /// simulation is advancing normally. Read by the network HUD.
        /// </summary>
        public int BlockedOnPlayer { get; private set; } = -1;

        /// <summary>How long we have been blocked on <see cref="BlockedOnPlayer"/>.</summary>
        public float BlockedSeconds { get; private set; }

        /// <summary>
        /// Seconds left before the match is ended for a disconnect, or 0 when
        /// no countdown is running. Non-zero only once the stall has been
        /// going long enough to be a disconnect rather than a hitch, and only
        /// against a peer that was genuinely ticking with us — the same
        /// conditions EndMatchOnDisconnect uses, so the banner never promises
        /// an end that will not come.
        /// </summary>
        public float DisconnectCountdown
        {
            get
            {
                if (_endedOnDisconnect || !_worldReady || BlockedOnPlayer < 0) return 0f;
                if (_confirmedTicks.GetValueOrDefault(BlockedOnPlayer, -1) < 0) return 0f;
                // Silent for the first couple of seconds: a brief hitch is
                // normal and a countdown that appears on every stutter would
                // be noise.
                if (BlockedSeconds < DisconnectWarnAfterSeconds) return 0f;
                return Mathf.Max(0f, DisconnectEndSeconds - BlockedSeconds);
            }
        }

        /// <summary>Stall length after which the countdown becomes visible.</summary>
        private const float DisconnectWarnAfterSeconds = 3f;

        /// <summary>True once a peer has been silent past <see cref="StallDropSeconds"/>.</summary>
        public bool PeerLost { get; private set; }

        /// <summary>Round-trip time to the slowest peer in milliseconds, 0 if unknown.</summary>
        public float WorstLatencyMs
        {
            get
            {
                float worst = 0f;
                foreach (var kv in _latencyMs) if (kv.Value > worst) worst = kv.Value;
                return worst;
            }
        }
        
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

            // Tear the fixed-step driver down with the match. It was never
            // uninstalled, so its statics (and the presentation layer's
            // interpolation switch, which reads them) survived into whatever
            // came next — a single-player game started after a multiplayer one
            // would have inherited both.
            LockstepFixedStep.Uninstall();
            LockstepTiming.Reset();
            LockstepLog.Close();
            LockstepTrace.Close();

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
            MaintainLiveness();

            // Tick 0 must not run until the world is fully built. The terrain
            // mask is produced by a coroutine and the nav cost field is baked by
            // a one-shot system in InitializationSystemGroup — neither of which
            // the tick loop governs, and neither of which lands at the same
            // moment on two machines. Starting before they do gives the faster
            // peer several ticks of a world with no water, no cliffs and no
            // bridges that the slower peer never had.
            //
            // Each peer gates itself; lockstep does the rest. A peer that is not
            // ready does not broadcast, so the other one runs out of its primed
            // confirmations and waits — which is exactly the behaviour wanted.
            if (!_worldReady && !IsWorldReady()) return;

            _tickAccumulator += Time.deltaTime;

            // Bound the catch-up burst. Without a cap, a peer returning from a
            // long stall (alt-tab, a hitch, a stalled peer that came back) runs
            // every missed tick in ONE frame — and under the fixed-step driver
            // each of those is a full simulation update, so the recovery frame
            // takes longer than the stall did and the game appears to hang.
            float maxCatchUp = MaxCatchUpTicks * TICK_DURATION;
            if (_tickAccumulator > maxCatchUp) _tickAccumulator = maxCatchUp;

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

        /// <summary>Most ticks a single frame may run while catching up.</summary>
        private const int MaxCatchUpTicks = 8;

        /// <summary>Latches once the world has finished building — see Update().</summary>
        private bool _worldReady;
        private float _worldWaitStarted;

        /// <summary>
        /// True when everything the simulation reads from the map exists: the
        /// terrain passability mask, the nav cost field baked from it, and the
        /// full match-start entity population.
        /// </summary>
        private bool IsWorldReady()
        {
            var pg = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (pg == null || !pg.IsMaskReady) return NotYet();

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return NotYet();

            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(typeof(NavCostField));
            if (q.CalculateEntityCount() == 0) return NotYet();
            if (q.GetSingleton<NavCostField>().TerrainBaked == 0) return NotYet();

            // The frame-paced spawn coroutine must have placed EVERY starting
            // entity (factions, deposits, wells, pockets) before tick 0. A tick
            // that elapses mid-population sees a world the other peer does not
            // have yet, and any spawn after BeginTick(0) gets a tick-partition
            // NetworkId stamped with this machine's LOCAL tick — both fork the
            // first checksum (the instant tick-30 desync, 2026-08-16).
            if (!TheWaningBorder.Core.MatchLifecycle.MapPopulated) return NotYet();

            // The loading screen must be COMPLETELY gone — fade included —
            // before the match clock starts. timeScale holds single-player,
            // but the fixed-step driver pushes its own delta and never reads
            // timeScale, so without this a lockstep match ticks away behind
            // the overlay: the AI acts, income accrues and the curse spreads
            // in seconds the player never sees, and the game time they are
            // shown does not match the game they played.
            if (TheWaningBorder.Core.PresentationState.LoadingOverlayVisible) return NotYet();

            // LAST-LINE ASSERTION before the clock starts: in deterministic
            // mode the fixed-step driver MUST be holding the sim group. If it
            // is not — a failed Install, a stray Uninstall, any lifecycle bug
            // — every tick from here would advance a frame-driven world and
            // the match forks at the first checksum while looking healthy.
            // Repair it here and say so, rather than desyncing silently.
            // IsAttached, NOT Active: desync #5 proved the flag can stand
            // while the manager has been detached from the group.
            if (GameSettings.DeterministicLockstep
                && !TheWaningBorder.Multiplayer.LockstepFixedStep.IsAttached)
            {
                UnityEngine.Debug.LogError(
                    "[Lockstep] Fixed-step driver was NOT attached at world-ready — " +
                    "re-installing now. Whatever detached it is a bug; this match " +
                    "is safe, but check the log above for the cause.");
                TheWaningBorder.Multiplayer.LockstepFixedStep.Install(
                    EntityWorld.DefaultGameObjectInjectionWorld, TICK_DURATION);
            }

            _worldReady = true;

            // The whole world build counted as "blocked on peer" (nobody has
            // confirmed a tick yet), so BlockedSeconds is now however long the
            // slowest of terrain, bake and spawn took. Zero it, or the
            // disconnect timer would fire on the first frame of the match.
            BlockedOnPlayer = -1;
            BlockedSeconds = 0f;

            if (_worldWaitStarted > 0f)
            {
                UnityEngine.Debug.Log($"[Lockstep] World ready after " +
                    $"{Time.realtimeSinceStartup - _worldWaitStarted:0.00}s — starting tick 0.");
            }
            // Deliberately WITHOUT the wait duration: how long each peer took to
            // build its world is per-machine, and putting it in the diffable log
            // would make two healthy peers' files differ on line one.
            LockstepLog.Event(0, "world ready — tick 0 begins");
            return true;

            bool NotYet()
            {
                if (_worldWaitStarted <= 0f) _worldWaitStarted = Time.realtimeSinceStartup;
                // 120s, not the old 30s: the gate now also waits for the full
                // spawn coroutine, and MapMagic terrain generation alone can
                // blow 30s on a slow machine. A force-start before population
                // finishes is a GUARANTEED desync, so the bail-out exists only
                // for a world that will truly never finish.
                else if (Time.realtimeSinceStartup - _worldWaitStarted > 120f)
                {
                    // Never hang the match on a world that will not finish.
                    UnityEngine.Debug.LogError(
                        "[Lockstep] The world did not finish building within 120s — starting anyway. " +
                        "Terrain blocking, pathing and the entity population may be wrong for the whole match.");
                    _worldReady = true;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Wall-clock housekeeping: measure latency, notice peers that have gone
        /// quiet, and keep <see cref="BlockedOnPlayer"/> current for the HUD.
        ///
        /// NOTHING here may touch simulation state — it all reads Time.time and
        /// is therefore per-peer and nondeterministic by construction.
        /// </summary>
        private void MaintainLiveness()
        {
            float now = Time.realtimeSinceStartup;
            LockstepLog.Pump(now);

            // ── Latency probe ──────────────────────────────────────────
            if (now >= _nextPingAt)
            {
                _nextPingAt = now + PingInterval;
                SendPing(now);
            }

            // ── Who, if anyone, are we waiting on? ─────────────────────
            int blocking = -1;
            foreach (int playerIndex in _expectedPlayers)
            {
                if (_confirmedTicks.GetValueOrDefault(playerIndex, -1) < _currentTick)
                {
                    blocking = playerIndex;
                    break;
                }
            }

            if (blocking != BlockedOnPlayer)
            {
                // Console, not the lockstep log: which peer is momentarily
                // ahead is a per-machine fact, and two healthy peers block on
                // each other constantly.
                if (blocking >= 0)
                    UnityEngine.Debug.Log($"[Lockstep] tick {_currentTick}: waiting on player {blocking}.");
                else if (BlockedOnPlayer >= 0 && BlockedSeconds > StallWarnSeconds)
                    UnityEngine.Debug.Log($"[Lockstep] tick {_currentTick}: player {BlockedOnPlayer} " +
                                          $"caught up after {BlockedSeconds:0.0}s.");

                BlockedOnPlayer = blocking;
                BlockedSeconds = 0f;
            }
            else if (blocking >= 0)
            {
                float was = BlockedSeconds;
                BlockedSeconds += Time.unscaledDeltaTime;

                // THE RECOVERY PATH. A blocked peer used to transmit nothing
                // at all: the advance loop breaks before BroadcastTick, so the
                // only retransmission there is rides along with a tick we are
                // no longer able to send. Both peers stall, both go silent,
                // and the lost datagram can never be repaired — which is
                // exactly how the 2026-08-26 match ended, two players waiting
                // 39 s and 47 s on each other before quitting by hand.
                //
                // So while we are blocked, keep pushing our recent ticks at
                // them. Whatever they are missing is in that window, and the
                // peer blocking us is very likely blocked on US for the same
                // reason — one repaired datagram unblocks both.
                if (now >= _nextStallResendAt)
                {
                    _nextStallResendAt = now + StallResendInterval;
                    ResendRecentTicks();
                }

                // One line each time a stall crosses a whole second, so a long
                // hang leaves a trail rather than a single ambiguous entry.
                if ((int)BlockedSeconds != (int)was && BlockedSeconds >= StallWarnSeconds)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[Lockstep] tick {_currentTick}: still waiting on player {blocking} " +
                        $"({BlockedSeconds:0}s). Their last confirmed tick is " +
                        $"{_confirmedTicks.GetValueOrDefault(blocking, -1)}.");
                }

                // Stuck on this peer for the full count — end it. This is the
                // deadlock arm: their packets may still be arriving, so the
                // silence check below will never fire.
                //
                // Both extra conditions matter, and dropping either turns this
                // into a match-killer:
                //   _worldReady        — before tick 0 every peer is "blocking"
                //                        by definition (confirmed tick -1 < 0),
                //                        and a world can legitimately take 120 s
                //                        to build;
                //   confirmed >= 0     — a peer who has never confirmed a tick
                //                        is still LOADING, not gone. Their
                //                        terrain may simply be slower than ours.
                // Together they mean: this peer was ticking with us, and has
                // now stopped. That is a disconnect.
                if (_worldReady
                    && _confirmedTicks.GetValueOrDefault(blocking, -1) >= 0
                    && BlockedSeconds >= DisconnectEndSeconds)
                {
                    EndMatchOnDisconnect(blocking,
                        $"their simulation stopped advancing {BlockedSeconds:0}s ago " +
                        $"(last confirmed tick {_confirmedTicks.GetValueOrDefault(blocking, -1)}, " +
                        $"we are on {_currentTick})");
                    return;
                }
            }

            // ── Has anyone gone for good? ──────────────────────────────
            if (!PeerLost)
            {
                foreach (int playerIndex in _expectedPlayers)
                {
                    float last = _lastHeardFrom.GetValueOrDefault(playerIndex, now);

                    // Silence arm of the same rule: nothing at all from them
                    // for the count, so the match is over whether or not we
                    // happen to be blocked on their tick right now. Same two
                    // guards as the deadlock arm above — see the note there.
                    if (_worldReady
                        && _confirmedTicks.GetValueOrDefault(playerIndex, -1) >= 0
                        && now - last > DisconnectEndSeconds)
                    {
                        PeerLost = true;
                        EndMatchOnDisconnect(playerIndex,
                            $"nothing received from them for {now - last:0}s");
                        return;
                    }

                    if (now - last > StallDropSeconds)
                    {
                        PeerLost = true;
                        UnityEngine.Debug.LogError(
                            $"[Lockstep] Player {playerIndex} has been silent for " +
                            $"{StallDropSeconds:0}s — treating the match as ended for them.");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// End the match because a peer is gone, and say so plainly.
        ///
        /// Before this existed, a lost peer left the survivor in a permanent
        /// stall: the sim frozen mid-tick, a warning a second in the console,
        /// and no way out but Alt-F4 or the pause menu. The 2026-08-26 logs
        /// are two players doing exactly that, 39 s and 47 s apart.
        ///
        /// Deliberately NOT a victory. With AI factions still alive, "the
        /// human opposite me dropped" is not the same as winning the match,
        /// and awarding a win here would put a false result in the stats and
        /// in Summary.txt. The match simply ends and says why.
        /// </summary>
        private void EndMatchOnDisconnect(int playerIndex, string detail)
        {
            if (_endedOnDisconnect) return;
            _endedOnDisconnect = true;

            string who = NameOfPlayer(playerIndex);

            UnityEngine.Debug.LogError(
                $"[Lockstep] Ending the match: {who} disconnected — {detail}. " +
                $"Waited {DisconnectEndSeconds:0}s.");

            // Into Summary.txt, so a log sent in afterwards says how it ended
            // instead of reading as another unexplained "quit".
            TheWaningBorder.Core.Diagnostics.MatchLogSession.RecordOutcome(
                $"ENDED — {who} disconnected ({detail})");

            // Stop ticking first: the sim must not lurch forward if a late
            // packet arrives while the end-of-match panel is coming up.
            StopSimulation();

            // The world is frozen behind the fixed-step driver, which only
            // steps on a requested tick — and no more are coming. Hand it back
            // so the menu and the panel animate normally.
            TheWaningBorder.Multiplayer.LockstepFixedStep.Uninstall();
            Time.timeScale = 1f;

            string title = TheWaningBorder.Core.Localization.Loc.T("MATCH ENDED");
            string subtitle = string.Format(
                TheWaningBorder.Core.Localization.Loc.T("{0} disconnected"), who);

            // Through the same seam the victory flow uses, so there is one
            // end-of-match screen with one Return to Main Menu button -- and so
            // the multiplayer layer does not need to know that either the panel
            // or its toast fallback exists.
            TheWaningBorder.Core.SimSignals.MatchEnded(title, subtitle, localWon: false);
        }

        /// <summary>Lobby name for a player index, falling back to the index
        /// when the slot table is not available (a client that never saw the
        /// full lobby, or a slot that was reassigned).</summary>
        private static string NameOfPlayer(int playerIndex)
        {
            try
            {
                var slots = TheWaningBorder.Core.Config.LobbyConfig.Slots;
                if (slots != null && playerIndex >= 0 && playerIndex < slots.Length)
                {
                    string n = slots[playerIndex].PlayerName;
                    if (!string.IsNullOrWhiteSpace(n)) return n;
                }
            }
            catch { /* naming is a nicety; never let it stop the teardown */ }
            return string.Format(
                TheWaningBorder.Core.Localization.Loc.T("Player {0}"), playerIndex);
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

            // The manager OBJECT is reused across matches (InitializeLockstepNow
            // reuses Instance), and this latch was the one piece of per-match
            // state not reset here: a stale true skips the whole world gate on
            // match 2 of a session, so ticks start mid-bootstrap on a world
            // still populating — the exact desync the gate exists to prevent.
            _worldReady = false;
            _worldWaitStarted = 0f;
            _localCommandBuffer.Clear();
            _remoteCommands.Clear();
            _confirmedTicks.Clear();
            _checksums.Clear();
            _pendingRemoteChecksums.Clear();

            _lastHeardFrom.Clear();
            _latencyMs.Clear();
            _seenPlayers.Clear();
            _recentTickPayloads.Clear();
            DesyncDetected = false;
            DesyncTick = 0;
            PeerLost = false;
            _endedOnDisconnect = false;
            BlockedOnPlayer = -1;
            BlockedSeconds = 0f;
            _nextPingAt = 0f;

            // Everyone starts "just heard from" so the stall detector does not
            // fire during the scene load the other peer is still finishing.
            float now = Time.realtimeSinceStartup;
            foreach (int p in _expectedPlayers) _lastHeardFrom[p] = now;

            // The diffable per-tick record. Opened here so it covers the whole
            // match including the pre-tick-0 wait.
            LockstepLog.Begin(_localPlayerIndex, _isHost);
            LockstepTrace.Begin();

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

            LockstepLog.Event(_currentTick, "simulation stopped");
            LockstepLog.Close();
            LockstepTrace.Close();
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

            // Execution order is (PlayerIndex, CommandIndex) and it must be a
            // TOTAL order, because List.Sort is not stable: any pair the
            // comparison calls equal may come out either way round, and the
            // input order itself differs between peers (each dictionary is
            // built in the order its own datagrams arrived). CommandIndex is
            // unique per player per tick, so the pair is total and the sort's
            // instability cannot express itself.
            if (allCommands.Count > 1)
            {
                allCommands.Sort((a, b) =>
                {
                    int cmp = a.PlayerIndex.CompareTo(b.PlayerIndex);
                    if (cmp != 0) return cmp;
                    cmp = a.CommandIndex.CompareTo(b.CommandIndex);
                    if (cmp != 0) return cmp;
                    // Last resort so the comparison is never "equal" for two
                    // distinct commands: type is stable across peers.
                    return ((int)a.Type).CompareTo((int)b.Type);
                });
            }

            // Log the tick's input BEFORE executing it, in execution order.
            // This is the line two peers' logs are diffed on; writing it after
            // execution would lose it if a command threw.
            LockstepLog.Tick(tick, allCommands);

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

            // Deterministic mode hashes EVERY tick into the log while still
            // broadcasting only on the interval. Costs no network traffic, and
            // it is the difference between "the fork is somewhere in the last
            // 30 ticks, here are nine suspect entities" and "the fork is this
            // tick" when the two logs are diffed.
            //
            // Desync 2026-08-18 was localised only to ticks 3421-3450 for
            // exactly this reason: positions were bit-identical at 3420 and
            // wrong at 3450, with no way to narrow it further.
            bool syncTick = tick % SYNC_CHECK_INTERVAL == 0;
            if (syncTick || GameSettings.DeterministicLockstep)
            {
                var hash = ComputeSimStateHash();

                // The rolling trace, so the ticks BEFORE the fork survive. A
                // desync is always detected later than it happens (the sync
                // interval guarantees it), and until this existed the only
                // per-entity evidence was a snapshot taken at detection time —
                // twenty ticks of consequence after the cause.
                LockstepTrace.Record(tick, hash, LockstepTrace.CaptureBuffer);
                LockstepLog.Checksum(tick, hash);

                if (syncTick)
                {
                    _checksums[tick] = hash.Total;
                    BroadcastSync(tick, hash.Total);

                    // A peer ahead of us may have sent this tick's SYNC before
                    // we computed ours — compare against the stash now, so the
                    // mismatch is detected on BOTH sides and both write dumps.
                    if (_pendingRemoteChecksums.TryGetValue(tick, out uint earlyRemote))
                    {
                        _pendingRemoteChecksums.Remove(tick);
                        if (earlyRemote != hash.Total)
                            OnChecksumMismatch(tick, hash.Total, earlyRemote, "stashed early SYNC");
                    }

                    // The checksum history was never pruned — one entry every
                    // sync tick, kept for the life of the match. Only recent
                    // ones can still be compared against an arriving SYNC.
                    _checksums.Remove(tick - SYNC_CHECK_INTERVAL * ChecksumHistoryTicks);
                    _pendingRemoteChecksums.Remove(tick - SYNC_CHECK_INTERVAL * ChecksumHistoryTicks);
                }
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
                            && cmd.Type != LockstepCommandType.EquipmentUpgrade
                            // SectPower packs the caster FACTION into
                            // EntityNetworkId, not an entity id — same as
                            // GodPower above.
                            && cmd.Type != LockstepCommandType.SectPower
                            // PlaceWallHub packs the FACTION too (there is no
                            // entity yet — the executor creates it).
                            && cmd.Type != LockstepCommandType.PlaceWallHub
                            // SectGlowAlloc packs the FACTION as well.
                            && cmd.Type != LockstepCommandType.SectGlowAlloc;

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
                    if (entity != Entity.Null)
                    {
                        // Through the SAME executor the single-player path
                        // uses: it validates (level gate, hero caps, queue
                        // cap) and SPENDS the unit cost on this peer. The
                        // spend must run here, not at the issue site — the
                        // faction banks feed the desync checksum, so an
                        // issuer-only debit desynced on the first purchase
                        // (docs/Multiplayer_LAN_Readiness.md).
                        CommandRouter.TrainCommandDirect(em, entity, cmd.BuildingId);
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

                case LockstepCommandType.PlaceWallHub:
                    {
                        // EntityNetworkId carries the FACTION (no entity exists
                        // until the executor creates the hub); TargetEntityId
                        // is the autoBuild flag.
                        var hub = CommandRouter.PlaceWallHubDirect(
                            em, cmd.TargetPosition, (Faction)cmd.EntityNetworkId,
                            autoBuild: cmd.TargetEntityId != 0);
                        if (hub != Entity.Null && em.HasComponent<NetworkedEntity>(hub))
                            _networkIdLookup[em.GetComponentData<NetworkedEntity>(hub).NetworkId] = hub;
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed PlaceWallHub from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.WallExtend:
                    if (entity != Entity.Null)
                    {
                        // TargetEntityId: snap-target hub network id (0 = place
                        // a new hub at TargetPosition). SecondaryTargetId:
                        // faction.
                        Entity snap = cmd.TargetEntityId != 0
                            ? FindEntityByNetworkId(cmd.TargetEntityId)
                            : Entity.Null;
                        CommandRouter.WallExtendDirect(em, entity, snap,
                            cmd.TargetPosition, (Faction)cmd.SecondaryTargetId);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed WallExtend from player {cmd.PlayerIndex}");
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

                case LockstepCommandType.VaultTransfer:
                    if (entity != Entity.Null)
                    {
                        // TargetEntityId: resource type in the low byte,
                        // deposit flag at bit 8. SecondaryTargetId: amount.
                        CommandRouter.VaultTransferDirect(em, entity,
                            cmd.TargetEntityId & 0xFF, cmd.SecondaryTargetId,
                            (cmd.TargetEntityId & 0x100) != 0);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed VaultTransfer from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.BazaarPack:
                    if (entity != Entity.Null)
                    {
                        CommandRouter.BazaarPackDirect(em, entity, cmd.TargetEntityId != 0);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed BazaarPack from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.SectGlowAlloc:
                    CommandRouter.SectGlowAllocDirect(em, (Faction)cmd.EntityNetworkId,
                        cmd.BuildingId, cmd.TargetEntityId != 0);
                    if (LogCommands) TWBLog.Log($"[Lockstep] Executed SectGlowAlloc from player {cmd.PlayerIndex}");
                    break;

                case LockstepCommandType.Corrupt:
                    if (entity != Entity.Null && targetEntity != Entity.Null)
                    {
                        CommandRouter.IssueCorruptDirect(em, entity, targetEntity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed Corrupt from player {cmd.PlayerIndex}");
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

                // ── 2026-08-15: the six that used to bypass lockstep ──────
                case LockstepCommandType.SectPower:
                    {
                        Faction caster = (Faction)cmd.EntityNetworkId;
                        CommandRouter.SectPowerDirect(em, caster, cmd.BuildingId,
                            cmd.TargetEntityId, cmd.TargetPosition);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed SectPower {cmd.BuildingId} t{cmd.TargetEntityId} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.ReliquaryAbility:
                    if (entity != Entity.Null)
                    {
                        CommandRouter.ReliquaryAbilityDirect(em, entity,
                            cmd.TargetEntityId, cmd.TargetPosition);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed ReliquaryAbility {cmd.TargetEntityId} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.WallUpgrade:
                    if (entity != Entity.Null)
                    {
                        // Charged variant: validates + spends on this peer
                        // before stamping the timer (the plain Direct is
                        // stamp-only; paying at the click site forked the
                        // banks — docs/Multiplayer_LAN_Readiness.md).
                        CommandRouter.WallUpgradeChargedDirect(em, entity,
                            cmd.TargetEntityId, cmd.TargetPosition.x);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed WallUpgrade {cmd.TargetEntityId} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.KeepWing:
                    if (entity != Entity.Null)
                    {
                        // Charged variant — same reasoning as WallUpgrade.
                        CommandRouter.KeepWingChargedDirect(em, entity,
                            (byte)(cmd.TargetEntityId & 0xFF), cmd.TargetPosition.x);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed KeepWing {cmd.TargetEntityId} from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.UnitPromote:
                    if (entity != Entity.Null)
                    {
                        UnitRankCommandHelper.Execute(em, entity);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed UnitPromote from player {cmd.PlayerIndex}");
                    }
                    break;

                case LockstepCommandType.QueueWaypoint:
                    if (entity != Entity.Null)
                    {
                        Entity wpTarget = cmd.SecondaryTargetId > 0
                            ? FindEntityByNetworkId(cmd.SecondaryTargetId)
                            : Entity.Null;
                        CommandRouter.QueuedWaypointDirect(em, entity,
                            (QueuedCommandType)cmd.TargetEntityId, cmd.TargetPosition, wpTarget);
                        if (LogCommands) TWBLog.Log($"[Lockstep] Executed QueueWaypoint from player {cmd.PlayerIndex}");
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

        /// <summary>
        /// Largest payload we will put in one datagram.
        ///
        /// Ethernet's MTU is 1500 bytes; past roughly 1400 (after IP+UDP
        /// headers) the datagram is fragmented at the IP layer, and losing ONE
        /// fragment discards the whole thing. A 50-100 command tick serialises
        /// to several KB, so exactly the ticks that matter most — the ones
        /// during a big fight — were the ones most likely to vanish whole.
        /// Splitting them into MTU-sized datagrams means a loss costs one
        /// chunk's commands, and the resend window can recover it.
        /// </summary>
        private const int MaxDatagramBytes = 1200;

        private void BroadcastTick(int tick, List<LockstepCommand> commands)
        {
            // Build one datagram per chunk. Every chunk is a self-describing
            // TICK message carrying its own command count, so a receiver that
            // gets two of three chunks applies what it got and the resend
            // covers the rest — rather than the all-or-nothing of one big
            // fragmented datagram.
            var payloads = new List<byte[]>(1);
            var sb = new StringBuilder(256);
            int chunkStart = 0;

            while (true)
            {
                int count = 0;
                sb.Clear();
                // Header is rewritten once the chunk's size is known, so build
                // the body first and prepend afterwards.
                var body = new StringBuilder(256);
                for (int i = chunkStart; i < commands.Count; i++)
                {
                    string piece = "|" + commands[i].Serialize();
                    if (count > 0 && body.Length + piece.Length > MaxDatagramBytes) break;
                    body.Append(piece);
                    count++;
                }

                sb.Append("TICK|").Append(_localPlayerIndex).Append('|')
                  .Append(tick).Append('|').Append(count).Append(body);

                payloads.Add(Encoding.UTF8.GetBytes(sb.ToString()));
                chunkStart += count;

                // A tick with NO commands still sends one datagram: the
                // confirmation is what lets the other peer advance.
                if (chunkStart >= commands.Count) break;
            }

            foreach (var player in _remotePlayers)
            {
                try
                {
                    // Resend the most recent ticks FIRST so a receiver that
                    // lost an earlier datagram has those commands before this
                    // tick's confirmation lets it advance past them.
                    //
                    // Only the newest RESEND_HISTORY ride along here: the
                    // retained window is much longer, but shipping all of it
                    // every tick would be pure waste on a healthy link. The
                    // rest is what ResendRecentTicks sends when a peer is
                    // actually stalled.
                    for (int i = 0; i < _recentTickPayloads.Count; i++)
                    {
                        if (_recentTickPayloads[i].Tick <= tick - RESEND_HISTORY) continue;
                        var payload = _recentTickPayloads[i].Data;
                        _udpClient?.Send(payload, payload.Length, player.EndPoint);
                    }
                    for (int i = 0; i < payloads.Count; i++)
                        _udpClient?.Send(payloads[i], payloads[i].Length, player.EndPoint);
                }
                catch (Exception)
                {
                }
            }

            // EVERY tick is retained, including the empty ones. The old
            // `if (commands.Count > 0)` meant a tick that carried no commands
            // was sent exactly once, over UDP, and could never be resent — and
            // an empty tick is still a CONFIRMATION, the thing the other peer
            // needs to advance.
            //
            // That single line is the 2026-08-26 hang. The last command in
            // that match was at tick 3815; every tick after it was empty. The
            // client's bare "TICK|1|3883|0" went missing, nothing could resend
            // it, and both peers waited on each other until the players quit.
            // An empty payload is about twenty bytes; keeping a second of them
            // costs nothing worth counting.
            for (int i = 0; i < payloads.Count; i++)
                _recentTickPayloads.Add((tick, payloads[i]));

            while (_recentTickPayloads.Count > 0 && _recentTickPayloads[0].Tick <= tick - RetainTicks)
                _recentTickPayloads.RemoveAt(0);
        }

        /// <summary>
        /// Push every retained tick payload at every peer, out of band.
        ///
        /// Called only while stalled, so on a healthy link this never runs.
        /// Deliberately unconditional about WHAT it sends: we cannot tell
        /// which datagram was lost, the whole retained window is a kilobyte or
        /// so of empty confirmations, and a duplicate TICK is harmless — the
        /// receiver keys them by tick and player, so applying one twice is a
        /// no-op.
        /// </summary>
        private void ResendRecentTicks()
        {
            if (_recentTickPayloads.Count == 0) return;

            foreach (var player in _remotePlayers)
            {
                try
                {
                    for (int i = 0; i < _recentTickPayloads.Count; i++)
                    {
                        var payload = _recentTickPayloads[i].Data;
                        _udpClient?.Send(payload, payload.Length, player.EndPoint);
                    }
                }
                catch (Exception)
                {
                    // Same as BroadcastTick: a send failure here is not worth
                    // taking the match down for. The next stall resend tries
                    // again, and the disconnect timeout is the backstop.
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
                    // The pinger advertises its input delay as a 4th field;
                    // adopt it if it is higher than ours (see AdoptPeerInputDelay).
                    if (parts.Length > 3 && int.TryParse(parts[3], out int peerDelay))
                        AdoptPeerInputDelay(peerDelay);
                    break;
                case "PONG":
                    ProcessPongMessage(parts);
                    break;
            }
        }

        /// <summary>
        /// Fold a round-trip sample into the peer's latency estimate and let the
        /// input delay follow it.
        ///
        /// The delay used to be a hard-coded 2 ticks no matter the link — 200 ms
        /// of self-inflicted lag on a LAN whose real round trip is a fraction of
        /// a millisecond. PING was handled but never SENT by anything, so
        /// nothing had ever measured the link at all.
        /// </summary>
        private void ProcessPongMessage(string[] parts)
        {
            if (parts.Length < 3) return;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float sentAt)) return;
            if (!int.TryParse(parts[2], out int fromPlayer)) return;

            float rtt = (Time.realtimeSinceStartup - sentAt) * 1000f;
            if (rtt < 0f) return;

            // Exponential smoothing: a single delayed datagram must not yank the
            // whole input delay up a tick.
            float prev = _latencyMs.GetValueOrDefault(fromPlayer, rtt);
            _latencyMs[fromPlayer] = prev * 0.7f + rtt * 0.3f;
            _lastHeardFrom[fromPlayer] = Time.realtimeSinceStartup;

            // Follow the SLOWEST peer — the delay has to cover everyone.
            int wanted = LockstepTiming.RecommendInputDelay(WorstLatencyMs);
            if (wanted != LockstepTiming.InputDelayTicks)
            {
                // Only ever raise it mid-match. Lowering it would stamp a command
                // for a tick that has already been confirmed and executed, and it
                // would be dropped on arrival.
                if (wanted > LockstepTiming.InputDelayTicks)
                {
                    LockstepTiming.InputDelayTicks = wanted;
                    UnityEngine.Debug.Log(
                        $"[Lockstep] input delay raised to {LockstepTiming.InputDelayTicks} ticks " +
                        $"({LockstepTiming.InputDelayMs:0} ms) — worst round trip {WorstLatencyMs:0} ms.");
                }
            }
        }

        /// <summary>
        /// Take a peer's advertised input delay and raise ours to match.
        ///
        /// Desync #6's dumps read "input delay 5" on the host and "3" on the
        /// client: ProcessPongMessage adapts each peer to ITS OWN measured
        /// round trip, so the two ends of one link settle on different
        /// delays. Execution ticks are issuer-stamped, so the mismatch never
        /// forked the sim — but it paces the two pipelines asymmetrically and
        /// makes the per-peer log headers disagree, which cost real diagnosis
        /// time. Peers now advertise their delay in every PING and everyone
        /// adopts the highest. Raise-only, for the same reason
        /// ProcessPongMessage only raises: lowering would stamp commands into
        /// ticks that have already been confirmed and executed.
        /// </summary>
        private void AdoptPeerInputDelay(int peerDelayTicks)
        {
            if (peerDelayTicks <= LockstepTiming.InputDelayTicks) return;
            if (peerDelayTicks > 30) return;   // corrupt-datagram guard: >1s of delay is never right
            int previous = LockstepTiming.InputDelayTicks;
            LockstepTiming.InputDelayTicks = peerDelayTicks;
            UnityEngine.Debug.Log(
                $"[Lockstep] input delay raised to {peerDelayTicks} ticks "
                + $"({LockstepTiming.InputDelayMs:0} ms) to match a peer.");

            // Into the DIFFABLE log, with the tick it happened on. Input delay
            // decides which tick a command executes on, so if the two peers
            // adopt a new delay at different ticks a command issued in that
            // window can land on different ticks -- a replication fork with a
            // perfectly healthy network. In two of the three 2026-08-21
            // matches the host raised its delay AFTER tick 0 had begun and
            // nothing recorded it here.
            LockstepLog.Event(_currentTick,
                $"input delay {previous} -> {peerDelayTicks} ticks (adopted from a peer)");
        }

        private void SendPing(float now)
        {
            string message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "PING|{0:R}|{1}|{2}", now, _localPlayerIndex, LockstepTiming.InputDelayTicks);
            byte[] data = Encoding.UTF8.GetBytes(message);
            foreach (var player in _remotePlayers)
            {
                try { _udpClient?.Send(data, data.Length, player.EndPoint); }
                catch { }
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
            _lastHeardFrom[playerIndex] = Time.realtimeSinceStartup;
            MaybeReleaseSimGate();

            _confirmedTicks[playerIndex] = Math.Max(_confirmedTicks.GetValueOrDefault(playerIndex, -1), tick);

            // Store commands only for ticks not yet executed — resends and
            // duplicates for already-processed ticks would just grow the
            // buffer (the tick ran; its outcome cannot be revised).
            if (tick >= _currentTick)
            {
                if (!_remoteCommands.TryGetValue(tick, out var byPlayer))
                {
                    byPlayer = new Dictionary<int, List<LockstepCommand>>();
                    _remoteCommands[tick] = byPlayer;
                }
                if (!byPlayer.TryGetValue(playerIndex, out var commands))
                {
                    commands = new List<LockstepCommand>();
                    byPlayer[playerIndex] = commands;
                }

                int cmdStartIndex = 4;
                for (int i = 0; i < cmdCount && cmdStartIndex < parts.Length; i++)
                {
                    var cmd = LockstepCommand.Deserialize(parts[cmdStartIndex]);
                    cmdStartIndex++;
                    if (cmd == null) continue;

                    cmd.PlayerIndex = playerIndex;
                    cmd.Tick = tick;

                    // MERGE, don't replace. A tick's commands can arrive across
                    // several datagrams (chunked to stay under the MTU) and the
                    // same datagram can arrive twice (the resend window), so
                    // the arriving set is deduplicated by CommandIndex rather
                    // than overwriting what is already held.
                    bool duplicate = false;
                    for (int k = 0; k < commands.Count; k++)
                    {
                        if (commands[k].CommandIndex == cmd.CommandIndex)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate) commands.Add(cmd);
                }
            }

            // Host relays to other clients (resends included — a client may
            // still be behind the tick this host already executed).
            if (_isHost)
            {
                string originalMessage = string.Join("|", parts);
                RelayTickMessage(originalMessage, playerIndex);
            }
        }

        /// <summary>
        /// Remote checksums that arrived BEFORE this peer computed its own for
        /// that tick. Without this stash the early arrival was silently
        /// dropped, the mismatch went undetected on the slower peer, and only
        /// ONE side ever wrote a Desync dump — leaving nothing to diff the
        /// forked entity against (2026-08-16, tick-0 desync investigation).
        /// </summary>
        private readonly Dictionary<int, uint> _pendingRemoteChecksums = new Dictionary<int, uint>();

        private void ProcessSyncMessage(string[] parts, IPEndPoint sender)
        {
            if (parts.Length < 3) return;

            if (!int.TryParse(parts[1], out int tick)) return;
            if (!uint.TryParse(parts[2], out uint remoteChecksum)) return;

            if (!_checksums.TryGetValue(tick, out uint localChecksum))
            {
                // Ours is not computed yet — keep theirs and compare in
                // ProcessTick the moment ours lands.
                _pendingRemoteChecksums[tick] = remoteChecksum;
                return;
            }
            if (localChecksum == remoteChecksum) return;

            OnChecksumMismatch(tick, localChecksum, remoteChecksum, sender.ToString());
        }

        /// <summary>
        /// The one mismatch handler, reached from BOTH directions: a SYNC
        /// arriving after our checksum (ProcessSyncMessage) and our checksum
        /// landing after a stashed early SYNC (ProcessTick). Either way both
        /// peers must end up here, each writing its own Desync dump.
        /// </summary>
        private void OnChecksumMismatch(int tick, uint localChecksum, uint remoteChecksum, string senderDesc)
        {
            // Only the FIRST mismatch is worth anything: after a fork the two
            // worlds diverge further every tick, so tick 900's mismatch tells you
            // nothing that tick 870's did not.
            if (DesyncDetected) return;

            DesyncDetected = true;
            DesyncTick = tick;

            if (!GameSettings.DeterministicLockstep)
            {
                // Frame-driven mode drifts by design; a mismatch here is expected
                // and halting on it would freeze a game that was never promised
                // to be in sync. Say so once, quietly, and carry on.
                UnityEngine.Debug.LogWarning(
                    $"[Lockstep] Checksum mismatch at tick {tick} with DeterministicLockstep OFF — " +
                    "expected, the simulation is frame-driven in this mode.");
                return;
            }

            UnityEngine.Debug.LogError(
                $"[Lockstep] DESYNC at tick {tick}: local checksum 0x{localChecksum:X8} " +
                $"!= remote 0x{remoteChecksum:X8} (from {senderDesc}).");

            // Both peers record it, so whichever log you open says the same
            // tick — and the checksum lines above it show how far back they
            // still agreed.
            LockstepLog.Event(tick,
                $"DESYNC local=0x{localChecksum:X8} remote=0x{remoteChecksum:X8} " +
                "— diff this file against the other peer's from the top");

            // A desync is a bug that has already happened; freezing tells the
            // player nothing and destroys the evidence. Write the state that
            // produced it to the match log folder FIRST — the first forked
            // entity is the whole answer, and without this the report is "it
            // desynced at tick 900" and nothing more.
            DumpDesyncState(tick, localChecksum, remoteChecksum);

            _isSimulationRunning = false;
        }

        /// <summary>
        /// Write every networked entity's contribution to the checksum, plus the
        /// faction banks, into the match log folder. Both peers write their own;
        /// diffing the two files names the entity that forked.
        /// </summary>
        private void DumpDesyncState(int tick, uint localChecksum, uint remoteChecksum)
        {
            try
            {
                var world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                var em = world.EntityManager;

                var sb = new StringBuilder(256 * 1024);
                sb.AppendLine($"=== DESYNC tick {tick} ===");
                sb.AppendLine($"player       : {_localPlayerIndex} ({(_isHost ? "host" : "client")})");
                sb.AppendLine($"local  cksum : 0x{localChecksum:X8}");
                sb.AppendLine($"remote cksum : 0x{remoteChecksum:X8}");
                sb.AppendLine($"build        : {MatchSettingsSync.BuildLabel} fp={MatchSettingsSync.Fingerprint}");
                sb.AppendLine($"tick rate    : {TICKS_PER_SECOND} Hz, input delay {INPUT_DELAY_TICKS} ticks");
                sb.AppendLine($"sync every   : {SYNC_CHECK_INTERVAL} ticks (so the FORK is up to that many ticks earlier)");
                sb.AppendLine();

                // The two things a dump could never answer before: what is
                // this machine, and who does this peer think plays whom. Both
                // legitimately differ between peers, so neither can live in the
                // diffable body -- but "their CPU has a different core count"
                // and "we each spawned an AI for the other's faction" are both
                // whole-investigation answers, and neither was recoverable
                // from the 2026-08-21 logs.
                sb.AppendLine("---- this machine ----");
                sb.Append(LockstepEnvironment.Describe());
                sb.AppendLine();
                sb.AppendLine("---- faction control (as THIS peer sees it) ----");
                sb.Append(LockstepEnvironment.DescribeFactionControl());
                sb.AppendLine();

                // Full per-entity state in the SAME row format as the rolling
                // trace, sorted by network id so two peers' dumps line up in a
                // plain text diff even when their chunk layouts differ.
                var snapshots = new List<EntitySnapshot>(1024);
                var hash = LockstepStateHash.Compute(
                    em, GetNetworkedQuery(em), detailed: true, snapshots: snapshots);

                sb.AppendLine("---- state now (tick " + tick + ") ----");
                sb.AppendLine($"entities     : {hash.Entities}");
                sb.AppendLine($"pos=0x{hash.Pos:X8} rot=0x{hash.Rot:X8} hp=0x{hash.Health:X8} " +
                              $"nav=0x{hash.Nav:X8} cbt=0x{hash.Combat:X8} wrk=0x{hash.Work:X8}");
                sb.AppendLine($"bank=0x{hash.Bank:X8} tech=0x{hash.Tech:X8} rng=0x{hash.Rng:X8} " +
                              $"veil=0x{hash.Veil:X8} cost=0x{hash.Cost:X8}");
                for (int f = 0; f < 8; f++)
                {
                    uint fh = hash.FactionAt(f);
                    if (fh == 2166136261u) continue;
                    sb.AppendLine($"faction {(Faction)f,-7} = 0x{fh:X8}");
                }
                sb.AppendLine();

                for (int i = 0; i < snapshots.Count; i++)
                {
                    var snap = snapshots[i];
                    LockstepTrace.AppendEntityLine(sb, tick, ref snap);
                }

                sb.AppendLine();
                for (int f = 0; f < 8; f++)
                {
                    if (!TheWaningBorder.Economy.FactionEconomy.TryGetResources(
                            em, (Faction)f, out var bank)) continue;
                    sb.AppendLine($"bank {(Faction)f,-7} supplies={bank.Supplies} iron={bank.Iron} " +
                                  $"veilstone={bank.Veilstone} veilsteel={bank.Veilsteel}");
                }

                string fileName = $"Desync_tick{tick}_p{_localPlayerIndex}.log";
                System.IO.File.WriteAllText(
                    TheWaningBorder.Core.Diagnostics.MatchLogSession.File(fileName), sb.ToString());

                // The rolling trace: every tick in the window, not just this
                // one. This is the file that contains the fork itself, because
                // the fork is always earlier than the detection.
                string traceName = $"Desync_tick{tick}_p{_localPlayerIndex}_trace.log";
                int traceTicks = LockstepTrace.Flush(
                    TheWaningBorder.Core.Diagnostics.MatchLogSession.File(traceName),
                    tick, _localPlayerIndex, _isHost);

                UnityEngine.Debug.LogError(
                    $"[Lockstep] Desync state written to the match log folder ({fileName}" +
                    (traceTicks > 0 ? $", plus {traceTicks} ticks of per-entity history in {traceName}" : "") +
                    "). Diff each against the other player's copy — the first differing line is the fork.");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Lockstep] Could not write the desync dump: {e.Message}");
            }
        }

        /// <summary>
        /// Echo the pinger's own timestamp back, plus OUR player index so they
        /// can attribute the round trip to the right peer.
        /// </summary>
        private void SendPong(IPEndPoint target, string timestamp)
        {
            string message = $"PONG|{timestamp}|{_localPlayerIndex}";
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

        /// <summary>
        /// Hash of everything the two peers must agree on, plus the
        /// per-subsystem and per-faction breakdown that makes a fork
        /// attributable.
        ///
        /// The old version XORed per-entity terms of NetworkId and health.
        /// XOR is order-independent, which was the right instinct — entity
        /// iteration order is a chunk-layout detail, not simulation state — but
        /// it is also self-cancelling: two entities swapping health values
        /// produced an IDENTICAL checksum, and so did a hundred other
        /// permutations. It also excluded positions (deliberately, because the
        /// frame-driven simulation drifted by design) and with them the whole
        /// movement system, plus resources, research and construction.
        ///
        /// Now: each entity is hashed into a well-mixed per-entity value with
        /// FNV-1a, and those values are SUMMED. Summation keeps the
        /// order-independence and loses the self-cancelling — a swap changes the
        /// per-entity hashes, so it changes the total. Positions are included
        /// because under the fixed-step simulation they are deterministic; if
        /// they ever drift again, that is the bug, and this is what reports it.
        ///
        /// The arithmetic itself lives in <see cref="LockstepStateHash"/>, in
        /// ONE place: the total goes over the wire, so a second copy that could
        /// drift from this one would take the whole match down with it.
        /// </summary>
        private SimStateHash ComputeSimStateHash()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return default;

            var em = world.EntityManager;
            var hash = LockstepStateHash.Compute(
                em, GetNetworkedQuery(em),
                detailed: GameSettings.DeterministicLockstep,
                snapshots: LockstepTrace.CaptureBuffer);

            _lastChecksumEntityCount = hash.Entities;
            return hash;
        }

    }
}