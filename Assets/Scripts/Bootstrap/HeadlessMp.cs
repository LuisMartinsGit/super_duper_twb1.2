// HeadlessMp.cs
// A REAL multiplayer match with no humans and no windows: N processes on one
// machine, one lockstep player each, raw UDP between them, DeterministicLockstep
// on — the desync-hunting instrument (2026-09-03 directive: "make sure the
// headless games mimic a 4-way multiplayer game, simulate all layers").
//
// ─────────────────────────────────────────────────────────────────────────
// HOW IT SIDESTEPS THE LOBBY
//
// The lobby's entire job is to make N processes agree on the match settings
// (TWB_JOIN/TWB_LOBBY/TWB_START in MultiplayerPanel). A test runner already
// knows the settings, so every peer receives them IDENTICALLY on the command
// line and the lobby is skipped outright. The lockstep layer itself — the
// part that can desync — is the REAL one: LockstepBootstrap -> LockstepManager,
// UDP tick exchange, SYNC checksums, fixed-step simulation. Ports are fixed
// per player index, so no discovery is needed either.
//
// WHO PLAYS: every faction is driven by the HOST's AI brains (AI decisions are
// host-authoritative by design — ShouldRunAIBrains — and flow to every peer as
// ordinary lockstep commands). The host's own slots plus the peers' "human"
// slots all get a brain, so a 4-peer match is 4 fighting factions whose every
// order crosses the wire. Clients additionally issue a small periodic move
// order for their own faction ("chaos mover"), so the CLIENT->HOST->relay
// command path carries real traffic too — without it that whole layer would
// go untested.
//
// USAGE (per process; the runner is tools/mp-batch.ps1):
//   TheWaningBorder.exe -batchmode -nographics -twbMp
//     -twbMpPeer 0..N-1     this process's lockstep player index (0 = host)
//     -twbMpPeers N         total peer processes (default 4)
//     -twbMpPort P          host lockstep port (default 17980); peer i binds P+i
//     -twbPlayers TOTAL     total factions (default = peers; extra become AI slots)
//     -twbSeed / -twbMap / -twbLimit   as in HeadlessBatch
//     -twbMpWarm S          WARM-UP (see below): play a local skirmish in this
//                           process until S wall-seconds after launch, tear it
//                           down like a quit-to-menu, THEN boot the MP match
//     -twbMpWarmMap / -twbMpWarmPlayers / -twbMpWarmSeed / -twbMpWarmSpeed
//                           the warm-up skirmish's settings (defaults:
//                           SunderedCrown, 2 + peer%3 factions, seed+1000*(peer+1), 4x).
//                           -twbMpWarmMap none = IDLE until the deadline instead
//                           (a fresh peer that still boots in step with warm ones)
//
// WARM-UP: THE SECOND-MATCH-IN-A-PROCESS CLASS (2026-09-11)
// The 2026-09-10 tester desync (build 0.0.22, tick 150) needed a peer that
// had ALREADY PLAYED a match in the same process: TeardownAfterMatch wipes
// entities but keeps every system object, so a system's private state
// (fractional income carry, RNG stream position, clock anchors, cadence
// phase, the static influence grid) walks from one match into the next --
// and it is different on every machine. A batch of fresh processes can
// never see that class. -twbMpWarm gives each peer a DIFFERENT history first.
// The deadline is wall-clock from launch, not sim time, so peers that warmed
// on different maps still reach the MP world-ready within the 15 s
// StallDropSeconds window of each other -- and a peer meant to stay FRESH
// idles to the same deadline ("-twbMpWarmMap none"), because a fresh host
// that boots at once drops the still-warming clients as lost.
//
// The teardown between the two matches is NOT GameBootstrap.Reset() but the
// scene-load handler's own "entering gameplay from gameplay" branch: that is
// what Restart Match uses, and it wipes only once the NEW scene is live. An
// explicit Reset() while the old map's Terrain still exists lets the
// existence-gated NavGridBootstrapSystem rebuild the grid against the OLD
// terrain in the same frame, and the new map then inherits it (found the
// hard way: a 258x258 SunderedCrown grid under a 1026x1026 Veilmarch match).
//
// EXIT CODES for the runner: 0 = ran to limit or match decided, no desync;
// 42 = DESYNC detected; 43 = peer lost; 44 = wall-clock hang guard.
//
// NO TIME ACCELERATION: GameSpeedControl pins timeScale to 1 in multiplayer,
// and lockstep peers must advance in step anyway — an MP run is wall-clock.
// Budget -twbLimit accordingly (default 900 s).
// ─────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    public class HeadlessMp : MonoBehaviour
    {
        public static bool Active { get; private set; }

        // -twbMpNoChaos: clients issue NO orders — pure host-AI replication.
        // Isolates "does the client->host->relay command path desync" from
        // "does host-AI-only replication desync".
        private static readonly bool s_noChaos =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-twbMpNoChaos") >= 0;

        // -twbMpMonkey: the COMMAND-COVERAGE MONKEY. Each client rotates
        // through every player-facing command type (move / attack / attack-
        // move / stop / hold / train / cancel / place / repair / research /
        // upgrade / formation move + attack-move / patrol / waypoint /
        // layered move / age-up) on a 6 s beat, as the local player of its
        // own faction. Every command type is a distinct serialize -> relay ->
        // execute path, and each is a fresh chance for the CommandSource /
        // signed-zero / un-networked-target desync classes the plain chaos
        // mover (Move only) can never reach. Rejected casts are fine — the
        // routers validate; coverage of the PATH is the point.
        private static readonly bool s_monkey =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-twbMpMonkey") >= 0;

        private int _monkeyBeat;

        private int _peer;
        private int _peers;
        private float _limit = 900f;
        private float _wallStart;
        private bool _done;
        private bool _brainsCreated;
        private float _nextChaosAt;
        private float _decidedAtWall;

        private static readonly Unity.Entities.ComponentType[] QT_Verdict =
            { Unity.Entities.ComponentType.ReadOnly<MatchVerdictState>() };
        private static TheWaningBorder.Core.CachedEntityQuery QC_Verdict;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-twbMp") < 0) return;

            var go = new GameObject("HeadlessMp");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessMp>().Begin(args);
        }

        // ── Warm-up phase (see the header) ──
        private enum Phase { Warm, Mp }
        private Phase _phase = Phase.Mp;
        private float _warmUntilWall;       // wall seconds since launch
        private float _warmSpeed = 4f;
        private string _warmMap;
        private bool _warmIdle;
        private int _warmPlayers, _warmSeed;

        // The MP settings, parsed once and applied when the MP phase boots
        // (immediately, or after the warm-up).
        private int _basePort, _totalFactions, _seed;
        private bool _loose;
        private string _mpMap;

        private void Begin(string[] args)
        {
            Active = true;
            _wallStart = Time.realtimeSinceStartup;

            _peer = ArgInt(args, "-twbMpPeer", 0);
            // peers=1 is the DIAGNOSIS mode: a solo host running the full
            // deterministic fixed-step stack with no network waits — it
            // isolates "lockstep sim path crashes" from "peers disagree".
            _peers = Mathf.Clamp(ArgInt(args, "-twbMpPeers", 4), 1, 8);
            _totalFactions = Mathf.Clamp(ArgInt(args, "-twbPlayers", Mathf.Max(_peers, 2)),
                Mathf.Max(_peers, 2), 8);
            _basePort = ArgInt(args, "-twbMpPort", 17980);
            _limit = ArgInt(args, "-twbLimit", 900);
            _seed = ArgInt(args, "-twbSeed", 12345);
            // -twbMpLoose: DeterministicLockstep OFF (frame-driven sim, only
            // commands synchronised). The second half of the bisection.
            _loose = Array.IndexOf(args, "-twbMpLoose") >= 0;

            string mapArg = ArgStr(args, "-twbMap");
            _mpMap = !string.IsNullOrEmpty(mapArg) ? mapArg : GameSettings.SelectedMapScene;

            int warm = ArgInt(args, "-twbMpWarm", 0);
            if (warm > 0)
            {
                _phase = Phase.Warm;
                _warmUntilWall = warm;
                _warmSpeed = Mathf.Clamp(ArgInt(args, "-twbMpWarmSpeed", 4), 1, 8);
                string wm = ArgStr(args, "-twbMpWarmMap");
                _warmMap = !string.IsNullOrEmpty(wm) ? wm : "SunderedCrown";
                _warmIdle = string.Equals(_warmMap, "none", StringComparison.OrdinalIgnoreCase);
                // Different residue per peer is the whole point: vary the
                // faction count and the seed with the peer index by default.
                _warmPlayers = Mathf.Clamp(ArgInt(args, "-twbMpWarmPlayers", 2 + _peer % 3), 2, 8);
                _warmSeed = ArgInt(args, "-twbMpWarmSeed", _seed + 1000 * (_peer + 1));
                BeginWarm();
                return;
            }

            ConfigureMp();
            LoadMp();
        }

        /// <summary>A local all-AI skirmish, set up the way HeadlessBatch
        /// does it. Nothing here is multiplayer — that is the point.</summary>
        private void BeginWarm()
        {
            if (_warmIdle)
            {
                Debug.Log($"[HeadlessMp] peer {_peer}: FRESH peer idling until " +
                          $"{_warmUntilWall:0}s after launch so the warm peers boot in step");
                return;
            }
            GameSettings.SelectedMapScene = _warmMap;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TutorialActive = false;
            GameSettings.TotalPlayers = _warmPlayers;
            GameSettings.SpawnSeed = _warmSeed;
            GameSettings.DeterministicLockstep = false;
            LobbyConfig.SetupSinglePlayer(_warmPlayers);
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                if (LobbyConfig.Slots[i].Type == SlotType.Human)
                    LobbyConfig.Slots[i].Type = SlotType.AI;
            LobbyConfig.ApplyColorSelections();
            GameSettings.IsObserver = true;
            Time.timeScale = _warmSpeed;

            Debug.Log($"[HeadlessMp] peer {_peer}: WARM-UP skirmish on {_warmMap}, " +
                      $"{_warmPlayers} AI, seed {_warmSeed}, {_warmSpeed}x, until " +
                      $"{_warmUntilWall:0}s after launch — then the MP match on {_mpMap}");
            SceneManager.LoadScene(_warmMap);
        }

        /// <summary>Boot the real match in the SAME process, systems and
        /// statics intact. The warm-up is torn down by GameBootstrap's
        /// scene-load handler when the MP map comes up — the Restart Match
        /// path — never by an explicit Reset() here (see the header).</summary>
        private void EndWarmAndStartMp()
        {
            Debug.Log($"[HeadlessMp] peer {_peer}: warm-up over at " +
                      $"{Time.realtimeSinceStartup - _wallStart:0}s wall — " +
                      "booting the MP match in this process");
            _phase = Phase.Mp;
            Time.timeScale = 1f;

            // Per-match harness state.
            _brainsCreated = false;
            _nextChaosAt = 0f;
            _monkeyBeat = 0;
            _decidedAtWall = 0f;
            _chaosQueryReady = false;
            _wallStart = Time.realtimeSinceStartup;

            ConfigureMp();
            LoadMp();
        }

        private void ConfigureMp()
        {
            int total = _totalFactions;
            GameSettings.SelectedMapScene = _mpMap;

            // ── Identical on every peer (this replaces the lobby). ──
            GameSettings.IsMultiplayer = true;
            GameSettings.IsObserver = false;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TutorialActive = false;
            GameSettings.SpawnSeed = _seed;
            GameSettings.TotalPlayers = total;
            GameSettings.DeterministicLockstep = !_loose;
            LockstepTiming.Reset();

            LobbyConfig.ActiveSlotCount = total;
            LobbyConfig.InitializeSlots();
            for (int i = 0; i < total; i++)
                LobbyConfig.Slots[i].Type = i < _peers ? SlotType.Human : SlotType.AI;
            LobbyConfig.ApplyColorSelections();

            GameSettings.FactionToPlayerMapping.Clear();
            for (int i = 0; i < _peers; i++)
                GameSettings.FactionToPlayerMapping[LobbyConfig.Slots[i].Faction] = (ulong)i;

            // ── Per-peer role. ──
            GameSettings.NetworkRole = _peer == 0 ? NetworkRole.Server : NetworkRole.Client;
            GameSettings.LocalPlayerFaction = LobbyConfig.Slots[_peer].Faction;

            var b = new GameObject("LockstepBootstrap").AddComponent<LockstepBootstrap>();
            if (_peer == 0)
            {
                b.ConfigureAsHost(_basePort, new List<RemotePlayerInfo>());
                for (int i = 1; i < _peers; i++)
                    b.AddRemotePlayer("127.0.0.1", _basePort + i,
                        LobbyConfig.Slots[i].Faction, i);
            }
            else
            {
                var others = new List<int>();
                for (int i = 1; i < _peers; i++) if (i != _peer) others.Add(i);
                b.ConfigureAsClient("127.0.0.1", _basePort, _basePort + _peer,
                    _peer, GameSettings.LocalPlayerFaction, others);
            }

            MatchMetrics.Enabled = true;
            if (GetComponent<MatchMetrics>() == null)
                gameObject.AddComponent<MatchMetrics>();

            Debug.Log($"[HeadlessMp] peer {_peer}/{_peers} ({GameSettings.NetworkRole}), " +
                      $"{total} factions, seed {_seed}, limit {_limit}s, " +
                      $"lockstep port {_basePort + (_peer == 0 ? 0 : _peer)}, " +
                      $"map {GameSettings.SelectedMapScene}");
        }

        private void LoadMp()
        {
            // Straight scene load — LoadingScreen pins timeScale to 0 while
            // visible and IsWorldReady refuses tick 0 behind its overlay flag,
            // so the UI path would deadlock a -nographics process.
            SceneManager.LoadScene(GameSettings.SelectedMapScene);
        }

        private void Update()
        {
            if (_done) return;

            if (_phase == Phase.Warm)
            {
                // GameSpeedControl.Apply() resets timeScale at match start;
                // hold the warm-up speed the way HeadlessBatch does.
                if (!_warmIdle && Time.timeScale != _warmSpeed) Time.timeScale = _warmSpeed;
                if (Time.realtimeSinceStartup - _wallStart >= _warmUntilWall)
                    EndWarmAndStartMp();
                return;
            }

            var lockstep = LockstepManager.Instance;

            // ── Verdicts, most-decisive first. ──
            if (lockstep != null && lockstep.DesyncDetected)
            {
                Finish(42, $"DESYNC at tick {lockstep.DesyncTick} — dumps are in the match log folder");
                return;
            }
            if (lockstep != null && lockstep.PeerLost)
            {
                Finish(43, "peer lost (disconnect timeout)");
                return;
            }
            // A lockstep stall, a boot hang, a peer that never came up: the
            // wall-clock guard keeps the batch from wedging forever.
            if (Time.realtimeSinceStartup - _wallStart > _limit * 2f + 300f)
            {
                Finish(44, "wall-clock guard tripped — lockstep never finished");
                return;
            }

            if (!MatchLifecycle.MapPopulated) return;

            // ── Every faction gets a brain, created IDENTICALLY on every peer
            // so the worlds stay symmetric. Only the host RUNS the AI systems
            // (ShouldRunAIBrains) — decisions leave it as lockstep commands.
            if (!_brainsCreated)
            {
                _brainsCreated = true;
                for (int i = 0; i < _peers; i++)
                {
                    try { TheWaningBorder.AI.AIBootstrap.CreateAIForFaction((Faction)i); }
                    catch (Exception e)
                    { Debug.LogWarning($"[HeadlessMp] brain for faction {i}: {e.Message}"); }
                }
                Debug.Log($"[HeadlessMp] AI brains created for the {_peers} peer factions " +
                          "(driven host-side, commands ride the lockstep stream)");
            }

            // ── Chaos mover (clients only): a small real order through the
            // CLIENT -> HOST -> relay path every few seconds, so that whole
            // layer carries traffic. The AI may immediately re-order the same
            // unit — that is concurrent input, exactly what MP must survive.
            float simNow = lockstep != null
                ? lockstep.CurrentTick * LockstepManager.TICK_DURATION : 0f;
            if (!s_noChaos && _peer != 0 && simNow >= _nextChaosAt && simNow > 30f)
            {
                _nextChaosAt = simNow + (s_monkey ? 6f : 12f);
                try
                {
                    if (s_monkey) IssueMonkeyBeat();
                    else IssueChaosMove();
                }
                catch (Exception e)
                { Debug.LogWarning($"[HeadlessMp] chaos/monkey: {e.Message}"); }
            }

            // ── Normal end conditions. ──
            // The GLOBAL sim verdict, not MatchLifecycle: the lifecycle flag
            // also fires on a LOCAL defeat (that peer's match is over, the
            // battle is not) and its MatchWinner then names the LOSER. Probe
            // 4's "Blue wins at 2744s" was the host's own elimination; the
            // harness must only end on the match-wide decision every peer's
            // EliminationSystem stamps at the same tick.
            bool globallyDecided = false;
            string verdictWinner = "";
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w != null && w.IsCreated)
                {
                    // Cached — a CreateEntityQuery per frame is the
                    // documented registry leak.
                    var q = QC_Verdict.Get(w.EntityManager, QT_Verdict);
                    if (!q.IsEmptyIgnoreFilter)
                    {
                        var v = q.GetSingleton<MatchVerdictState>();
                        globallyDecided = v.Decided != 0;
                        verdictWinner = v.Winner.ToString();
                    }
                }
            }
            if (globallyDecided)
            {
                // LINGER BEFORE EXIT (2026-09-06). The verdict lands on every
                // peer at the same SIM tick, but under late-game stalling the
                // peers reach that tick at different WALL moments — and the
                // first quitter (usually the host) kills the relay the others
                // still need to get there. Probe 4: host decided at sim 2744s
                // and quit; three clients a few ticks short of the verdict
                // tick starved and timed out "peer lost". Keep ticking and
                // relaying for a grace window so everyone crosses the line.
                if (_decidedAtWall <= 0f)
                {
                    _decidedAtWall = Time.realtimeSinceStartup;
                    Debug.Log($"[HeadlessMp] peer {_peer}: verdict reached " +
                        $"({verdictWinner} wins at {simNow:F0}s) — " +
                        "lingering 12s so every peer crosses the verdict tick");
                }
                else if (Time.realtimeSinceStartup - _decidedAtWall > 12f)
                {
                    Finish(0, $"match decided at {simNow:F0}s — {verdictWinner} wins, no desync");
                }
                return;
            }
            if (simNow >= _limit)
                Finish(0, $"limit reached at {simNow:F0}s (tick {lockstep?.CurrentTick ?? 0}), no desync");
        }

        private Unity.Entities.EntityQuery _chaosQuery;
        private bool _chaosQueryReady;

        /// <summary>One plain move order for a unit of OUR faction, issued as
        /// the local player would — it must round-trip the lockstep queue.
        ///
        /// BULLETPROOF-DETERMINISTIC (2026-09-04): the earlier version captured
        /// a unit's LIVE position (frame-arbitrary — Update runs on wall-clock
        /// between ticks) and picked "first in query order", and created a
        /// fresh EntityQuery every call. None of that can desync a REPLICATED
        /// command (the absolute target is what crosses the wire), but to rule
        /// the harness out entirely: the query is cached once, the unit is the
        /// LOWEST-NetworkId unit of our faction (identical on every peer), and
        /// the target is a FIXED absolute point oscillating on CurrentTick —
        /// no live-position read at all. If a desync survives this, it is the
        /// game, not the test.</summary>
        private void IssueChaosMove()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (!_chaosQueryReady)
            {
                _chaosQuery = em.CreateEntityQuery(
                    Unity.Entities.ComponentType.ReadOnly<UnitTag>(),
                    Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
                    Unity.Entities.ComponentType.ReadOnly<TheWaningBorder.Core.Multiplayer.NetworkedEntity>());
                _chaosQueryReady = true;
            }

            using var ents = _chaosQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = _chaosQuery.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var nets = _chaosQuery.ToComponentDataArray<
                TheWaningBorder.Core.Multiplayer.NetworkedEntity>(Unity.Collections.Allocator.Temp);

            // Lowest NetworkId of our faction — peer-identical.
            Unity.Entities.Entity pick = Unity.Entities.Entity.Null;
            int bestId = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != GameSettings.LocalPlayerFaction) continue;
                if (nets[i].NetworkId < bestId) { bestId = nets[i].NetworkId; pick = ents[i]; }
            }
            if (pick == Unity.Entities.Entity.Null) return;

            // Fixed absolute target, oscillating on the SIM tick (not wall
            // clock). Two points near this faction's home corner.
            int tick = LockstepManager.Instance != null ? LockstepManager.Instance.CurrentTick : 0;
            bool a = (tick / 360) % 2 == 0;
            float fx = ((int)GameSettings.LocalPlayerFaction % 2 == 0) ? 60f : -60f;
            float fz = ((int)GameSettings.LocalPlayerFaction / 2 == 0) ? 60f : -60f;
            var target = new Unity.Mathematics.float3(fx + (a ? 8f : -8f), 0f, fz);

            TheWaningBorder.Core.Commands.CommandRouter.IssueMove(
                em, pick, target,
                TheWaningBorder.Core.Commands.CommandSource.LocalPlayer);
        }

        // ── The command-coverage monkey ─────────────────────────────────────
        // All picks are lowest-NetworkId (peer-identical); targets are either
        // fixed points or entities addressed by their networked identity, so
        // whatever this peer decides replicates absolutely. Commands the game
        // rejects (can't afford, wrong building, no queue) cost nothing —
        // the router validates on every peer identically.

        private Unity.Entities.EntityQuery _mkUnits, _mkBuildings;
        private bool _mkReady;

        private void IssueMonkeyBeat()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var my = GameSettings.LocalPlayerFaction;
            var src = TheWaningBorder.Core.Commands.CommandSource.LocalPlayer;
            var C = typeof(TheWaningBorder.Core.Commands.CommandRouter);

            if (!_mkReady)
            {
                _mkUnits = em.CreateEntityQuery(
                    Unity.Entities.ComponentType.ReadOnly<UnitTag>(),
                    Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
                    Unity.Entities.ComponentType.ReadOnly<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(),
                    Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                _mkBuildings = em.CreateEntityQuery(
                    Unity.Entities.ComponentType.ReadOnly<BuildingTag>(),
                    Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
                    Unity.Entities.ComponentType.ReadOnly<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(),
                    Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                _mkReady = true;
            }

            // Snapshot own units (by ascending NetworkId), own buildings, one
            // enemy unit, and hall positions.
            var ownUnits = new System.Collections.Generic.List<(long id, Unity.Entities.Entity e, Unity.Mathematics.float3 p)>();
            Unity.Entities.Entity enemyUnit = Unity.Entities.Entity.Null; long enemyBest = long.MaxValue;
            {
                using var ents = _mkUnits.ToEntityArray(Unity.Collections.Allocator.Temp);
                using var facs = _mkUnits.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
                using var nets = _mkUnits.ToComponentDataArray<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(Unity.Collections.Allocator.Temp);
                using var xfs = _mkUnits.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value == my) ownUnits.Add((nets[i].NetworkId, ents[i], xfs[i].Position));
                    else if (nets[i].NetworkId < enemyBest) { enemyBest = nets[i].NetworkId; enemyUnit = ents[i]; }
                }
            }
            ownUnits.Sort((a, b) => a.id.CompareTo(b.id));
            if (ownUnits.Count == 0) return;

            Unity.Entities.Entity ownHall = Unity.Entities.Entity.Null, ownLowHp = Unity.Entities.Entity.Null;
            Unity.Mathematics.float3 ownHallPos = default, enemyHallPos = default;
            bool haveEnemyHall = false;
            {
                long bestHall = long.MaxValue, bestEnemyHall = long.MaxValue; float lowFrac = 2f;
                using var ents = _mkBuildings.ToEntityArray(Unity.Collections.Allocator.Temp);
                using var facs = _mkBuildings.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
                using var nets = _mkBuildings.ToComponentDataArray<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(Unity.Collections.Allocator.Temp);
                using var xfs = _mkBuildings.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    bool hall = em.HasComponent<HallTag>(ents[i]);
                    if (facs[i].Value == my)
                    {
                        if (hall && nets[i].NetworkId < bestHall)
                        { bestHall = nets[i].NetworkId; ownHall = ents[i]; ownHallPos = xfs[i].Position; }
                        if (em.HasComponent<Health>(ents[i]))
                        {
                            var hp = em.GetComponentData<Health>(ents[i]);
                            float frac = hp.Max > 0 ? hp.Value / (float)hp.Max : 1f;
                            if (frac < lowFrac && frac < 1f) { lowFrac = frac; ownLowHp = ents[i]; }
                        }
                    }
                    else if (hall && facs[i].Value != Faction.Border && nets[i].NetworkId < bestEnemyHall)
                    { bestEnemyHall = nets[i].NetworkId; enemyHallPos = xfs[i].Position; haveEnemyHall = true; }
                }
            }

            var u0 = ownUnits[0].e;
            var u0p = ownUnits[0].p;
            int beat = _monkeyBeat++;
            var mid = new Unity.Mathematics.float3(
                (u0p.x + (haveEnemyHall ? enemyHallPos.x : 0f)) * 0.5f, 0f,
                (u0p.z + (haveEnemyHall ? enemyHallPos.z : 0f)) * 0.5f);

            switch (beat % 19)
            {
                case 0:
                    TheWaningBorder.Core.Commands.CommandRouter.IssueMove(em, u0, mid, src);
                    break;
                case 1:
                    if (haveEnemyHall)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueAttackMove(em, u0, enemyHallPos, src);
                    break;
                case 2:
                    if (enemyUnit != Unity.Entities.Entity.Null)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueAttack(em, u0, enemyUnit, src);
                    break;
                case 3:
                    TheWaningBorder.Core.Commands.CommandRouter.IssueStop(em, u0, src);
                    break;
                case 4:
                    TheWaningBorder.Core.Commands.CommandRouter.IssueHoldPosition(em, u0, src);
                    break;
                case 5:
                    if (ownHall != Unity.Entities.Entity.Null)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueTrain(em, ownHall, "Worker", src);
                    break;
                case 6:
                    if (ownHall != Unity.Entities.Entity.Null)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueCancelTrain(em, ownHall, 0, src);
                    break;
                case 7:
                    if (ownHall != Unity.Entities.Entity.Null)
                    {
                        var at = ownHallPos + new Unity.Mathematics.float3(
                            10f + (beat % 3) * 6f, 0f, -12f - (beat % 5) * 6f);
                        TheWaningBorder.Core.Commands.CommandRouter.IssuePlaceBuilding(em, "Hut", at, my, src);
                    }
                    break;
                case 8:
                    if (ownLowHp != Unity.Entities.Entity.Null)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueRepair(em, u0, ownLowHp, src);
                    break;
                case 9:
                    if (ownHall != Unity.Entities.Entity.Null)
                    {
                        var id = TheWaningBorder.Entities.BuildingIds.Of(ownHall, em);
                        var def = TechCatalog.Building(id);
                        if (def.research != null && def.research.Length > 0)
                            TheWaningBorder.Core.Commands.CommandRouter.IssueResearch(
                                em, ownHall, def.research[beat % def.research.Length], src);
                    }
                    break;
                case 10:
                    if (ownHall != Unity.Entities.Entity.Null)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueBuildingUpgrade(em, ownHall, src);
                    break;
                case 11:
                case 12:
                {
                    var squad = new System.Collections.Generic.List<Unity.Entities.Entity>();
                    for (int i = 0; i < ownUnits.Count && squad.Count < 8; i++) squad.Add(ownUnits[i].e);
                    if (squad.Count >= 2)
                    {
                        if (beat % 19 == 11)
                            TheWaningBorder.Core.Commands.CommandRouter.IssueFormationMove(
                                em, squad, mid, FormationShape.Line, src);
                        else if (haveEnemyHall)
                            TheWaningBorder.Core.Commands.CommandRouter.IssueFormationAttackMove(
                                em, squad, enemyHallPos, FormationShape.Box, src);
                    }
                    break;
                }
                case 13:
                    TheWaningBorder.Core.Commands.CommandRouter.IssuePatrol(em, u0, mid, src);
                    break;
                case 14:
                    TheWaningBorder.Core.Commands.CommandRouter.IssueQueuedWaypoint(
                        em, u0, QueuedCommandType.Move, mid, Unity.Entities.Entity.Null, src);
                    break;
                case 15:
                    TheWaningBorder.Core.Commands.CommandRouter.IssueLayeredMove(em, u0, mid, 0, src);
                    break;
                case 16:
                    // Once the beat count says mid-game, try the age-up — the
                    // biggest single coverage win (culture transform + the
                    // whole Age-1 command surface behind it). Rejected while
                    // unaffordable, which is fine.
                    if (ownHall != Unity.Entities.Entity.Null && beat >= 34)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueAgeUp(
                            em, ownHall, Cultures.Alanthor, src);
                    break;
                case 17:
                    // WALLS (2026-09-05): the replicated wall path had never
                    // run in a lockstep harness match (wall stamp counts were
                    // zero in every run) — the AI wall doctrine's gate never
                    // opens here. Place hubs as the player would; rejected
                    // pre-age-up or when unaffordable, which is fine.
                    if (ownHall != Unity.Entities.Entity.Null)
                    {
                        var wp = ownHallPos + new Unity.Mathematics.float3(
                            -14f - (beat % 3) * 8f, 0f, 10f);
                        TheWaningBorder.Core.Commands.CommandRouter.IssuePlaceWallHub(
                            em, wp, my, autoBuild: true, src);
                    }
                    break;
                case 18:
                {
                    // Extend a curtain from our lowest-id standing hub — the
                    // segment-creation half of the wall pipeline.
                    var hubQ = em.CreateEntityQuery(
                        Unity.Entities.ComponentType.ReadOnly<WallHubTag>(),
                        Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
                        Unity.Entities.ComponentType.ReadOnly<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(),
                        Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                    using (var hents = hubQ.ToEntityArray(Unity.Collections.Allocator.Temp))
                    using (var hfacs = hubQ.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp))
                    using (var hnets = hubQ.ToComponentDataArray<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(Unity.Collections.Allocator.Temp))
                    using (var hxfs = hubQ.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp))
                    {
                        Unity.Entities.Entity hub = Unity.Entities.Entity.Null;
                        Unity.Mathematics.float3 hp = default; long best = long.MaxValue;
                        for (int i = 0; i < hents.Length; i++)
                            if (hfacs[i].Value == my && hnets[i].NetworkId < best)
                            { best = hnets[i].NetworkId; hub = hents[i]; hp = hxfs[i].Position; }
                        if (hub != Unity.Entities.Entity.Null)
                            TheWaningBorder.Core.Commands.CommandRouter.IssueWallExtend(
                                em, hub, Unity.Entities.Entity.Null,
                                hp + new Unity.Mathematics.float3(12f, 0f, 0f), my, src);
                    }
                    hubQ.Dispose();
                    break;
                }
            }
        }

        private void Finish(int exitCode, string why)
        {
            _done = true;
            Debug.Log($"[HeadlessMp] peer {_peer}: {why} (exit {exitCode})");
            // Stop the tick loop and its per-frame world probes BEFORE the
            // quit tears the ECS world down under them — the raw teardown
            // produced query-registry NREs (and one access violation) that
            // polluted the exit codes the runner keys on.
            try { LockstepManager.Instance?.StopSimulation(); } catch { }
            try { MatchMetrics.DumpFinal(); } catch { }
            try
            {
                // Machine-readable verdict beside the logs, one per peer.
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, $"MpVerdict_p{_peer}.txt"),
                    $"exit={exitCode}\npeer={_peer}\n{why}\n");
            }
            catch { }
            Application.Quit(exitCode);
        }

        private static string ArgStr(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static int ArgInt(string[] args, string key, int fallback)
        {
            int i = Array.IndexOf(args, key);
            return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v))
                ? v : fallback;
        }
    }
}
