# Multiplayer Desync Sweep — 2026-08-16

Full-codebase determinism audit (six parallel sweeps: wall-clock, randomness,
out-of-group mutation, iteration order, replication gaps, AI) plus the three
live desyncs root-caused the same day. This file is the ledger: what forks a
lockstep match, what was fixed, what remains. Companion to
[Multiplayer_LAN_Readiness.md](Multiplayer_LAN_Readiness.md).

Ground truth the audit is judged against: in deterministic MP,
`SimulationSystemGroup` steps exactly once per lockstep tick with a fixed
pushed `TimeData` (`SystemAPI.Time` is safe); everything else — MonoBehaviours,
coroutines, other system groups — still runs per rendered frame and therefore
per-peer. Faction banks and every networked entity's id/health/faction/position
are in the desync checksum.

## Live desyncs root-caused today (all fixed)

| # | Fork | Root cause | Fix |
|---|---|---|---|
| 1 | tick 30, entity counts 748 vs 744 | frame-paced spawn coroutine still populating after tick 0; late spawns took tick-partition NetworkIds stamped with each peer's local tick | `SpawnDelayHelper.MapPopulated` gates tick 0 (`LockstepManager.IsWorldReady`) |
| 2 | tick 0, equal counts, different sums | `MapMarkerRegistry` sorted only player starts; deposit markers enumerated in per-process `FindObjectsByType` order → same sequential NetworkIds paired with different deposits | every marker list sorted (`CompareMarkers`: name, x, z, y) |
| 3 | tick 90, one outcropping id 1830000 vs 1790000 | `VeilFieldSystem.TryInitialise` gated on wall-clock-flavored `TerrainUtility.IsReady`, so the system went live at different ticks per peer, shifting the whole pulse schedule; precipitation then spawned the same node at tick 83 vs 79 | gate skipped under lockstep (world gate already guarantees it); init tick logged as an `evt` tripwire in Lockstep.log |
| 5 | tick 30, veil evt `tick=1034/1027 stepped=True` | **THE BIG ONE — upstream of #3 and #4's veil symptoms.** `GameBootstrap.InitializeWorld`'s NetCode-defense sweep unconditionally set `SimulationSystemGroup.RateManager = null` — three seconds AFTER `InitializeLockstepNow` had installed the fixed-step driver. Every deterministic match ever played ran frame-driven from that line on, while `LockstepFixedStep.Active` (a static, untouched by the detach) kept reporting deterministic | the sweep now spares `LockstepFixedRateManager` (only a FOREIGN manager is a hijack); new `IsAttached` property checks the real group attachment; the world-ready assertion and the veil tripwire use it; `Step()` re-attaches and screams if detached mid-match |
| 4 | tick 30, three spawns with tick-partition ids 2/25 vs 4/11, identical positions | cross-match lockstep-lifecycle leaks (both editors in one long play session): the reused `LockstepManager` never reset `_worldReady` (stale true skips the world gate on match 2+), `LockstepFixedStep.Uninstall` never popped a pending pushed `TimeData` (the veil-init tripwire read the PREVIOUS match's final sim time — "tick 766/636" — proving non-stepped execution pre-tick-0), and `Install` failed silently on a missing world/group | `StartSimulation` resets the latch; `Uninstall` pops the pending push; `Install` logs loudly; a self-healing assertion at world-ready re-installs the fixed-step driver if anything undid it; the veil tripwire now logs `stepped=` |

## Fixed in this sweep

- **VictoryConditionSystem** — poll cadence was `Time.deltaTime` (render-paced)
  while `SelfDestructFactionAssets` zeroes a whole faction's Health; now
  SimClock-paced. Also: the elimination loop `return`ed early on the LOCAL
  player's defeat, skipping remaining factions' self-destructs — and "local"
  differs per peer; loop now completes all sim mutations first, and
  `newlyEliminated` is sorted.
- **PresentationSpawnSystem** no longer stamps `BuildingUpgradeState` /
  `ApplyLevel` at view-spawn time (render-paced writes to checksummed
  Health.Max); new tick-driven `BuildingLevelOneSeedSystem` stamps completed
  cultured buildings inside the sim.
- **Shift-queued waypoints** — `CommandQueueActive` now added inside
  `QueuedWaypointDirect` (travels with the payload to every peer); the shift
  freeze is single-player-only (a frozen queue on one peer and a draining one
  on the other is a position fork).
- **PlanningModeOverlay** — orders 2..N now route through
  `IssueQueuedWaypoint` instead of raw buffer appends (they never replicated).
- **AbilityAuraSystem** Ledger automation relabeled `CommandSource.System`
  (ran on every peer but queued host-side as AI = tick-offset divergence).
- **VeilBreakInputSystem** (Alt+click dev break) disabled in MP.
- **Match-epoch reset** (`SpawnDelayHelper.MatchEpoch`): system objects survive
  across matches in one session; `VeilFieldSystem` and `BlightPocketSystem`
  now reset accumulators and reseed RNG per match. Any stateful sim system
  added later must follow the pattern.
- **Seed hardening** — `NodeStateDeathInterceptSystem` final-wave seed and
  `AIEndgameCommon` ring-scan seed quantize positions to millimetres before
  hashing (raw float-bit seeds flip on 1 ULP of drift).
- **Total sort comparers** — `PortalIntraTileEdges` (tile, then CellIndex),
  `PortalGraphBuildSystem` / `IncrementalPortalRebuildSystem` edge sorts
  (From, To, then Cost). `Array.Sort` is unstable; ties resolved arbitrarily
  differ per peer.
- **FogVisibilitySyncSystem** throttle moved off `unscaledTime` onto sim time
  (wrote no sim state, but keeps the tick wall-clock-free on principle).
- **SpawnDelayHelper** terrain wait: 5 s proceed-anyway timeout → 120 s with a
  loud error (spawning onto an unfinished heightmap forks everything).
- **Spend symmetry (in progress at time of writing)** — the architectural fix:
  every resource spend moves from the issue site (UI/AI, issuer-only) into the
  lockstep-executed Direct handler (every peer), following the pattern
  ConvertHut/UnitPromote/EquipmentUpgrade already used. Banks are checksummed;
  issuer-only spends desync on the first purchase. Cancel-train wired through
  `IssueCancelTrain` (was calling the helper directly; refund was already
  symmetric, which made cancels GRANT resources on remote peers).

## Fixed in the follow-up waves (same day)

- **AI order routing DONE** — 22 sites across 8 files now go through
  `CommandRouter.Issue*(..., CommandSource.AI)`: all attack waves/formation
  moves (`SimpleAISystem.Military/Posture`), all miner tasking
  (`SimpleAISystem.Mining`), scout steering (`ScoutDirectorSystem`), endgame
  escort/flee/conscript moves. Plus `ShouldDropCommand`: AI-source commands
  on a non-host peer hard-drop instead of executing locally.
- **Wall placement replicated** — new opcodes `PlaceWallHub = 36` (builder
  5 s, or autoBuild 30 s flavour in `TargetEntityId`) and `WallExtend = 37`
  (segment to a snap hub, or new auto-build hub + segment). Player path
  (`BuildCommandPannel`) and AI wall doctrine (`AIAlanthorEndgameSystem.Walls`
  hub fill + gap closing) both routed; spends live in the executors. In MP
  the AI's immediate proximity-link loop defers to `TryCloseWallGaps`
  (the placed hub does not exist until the command executes).
- **ProtocolVersion 2 → 3** — the spend-model migration and the new opcodes
  are wire-breaking; mixed builds refuse at the lobby door.
- **Uncharged wall/keep fallbacks** (`networkId <= 0` in MP) now hard-drop
  with a LogError instead of stamping a local-only upgrade.

- **Final opcode wave DONE**: `Corrupt = 38` (mirrors Purify),
  `SectGlowAlloc = 39` (SP keeps the direct call for the instant error
  message), `BazaarPack = 40`, `VaultTransfer = 41` (faction from the
  vault's own FactionTag; short bank rejects identically everywhere).
  Enum now ends at 41; all four dispatch in `LockstepManager.ExecuteCommand`.

## Remaining (tracked, in priority order)

1. **Small unrouted leftovers (MEDIUM)**: building placement rotation
   (mouse-wheel yaw applied only on the SP branch — cosmetic unless a
   footprint/nav stamp ever reads rotation), ability ground-aim point
   (`AbilityAimPoint` stamped before the lockstep branch and not carried in
   the Ability payload — ground-aimed casts land differently on remote
   peers), `FleeWorkers` MinerState reset / `BuildOrder` removal (host-only
   clears of work state that exists on every peer; consider `IssueStop`).
2. **Culture statics timing** — `FactionColors.SetFactionCulture` fires at
   CLICK time on the choosing peer but at command-execution time elsewhere:
   a ~2-tick window where sim reads of culture differ. Move the local set
   into the AgeUp executor.
3. **Landmines (scenario/dev-only today, guaranteed forks if reached in MP)**:
   `ScenarioWaveSpawner` (wall-clock cadence + global `UnityEngine.Random`),
   `HutEvolutionDriver` (render-clock research completion + damage),
   `TutorialDirector` (no MP guard; grants resources), `TechEffectSystem`
   (correct today but event-subscription-fragile), trade-lane LCG streams
   (shared across factions, unseeded from match), `RangedCombatSystem` arrow
   spread seed (fine under fixed-step, frame-driven in loose mode),
   `IssuePlanOrders` doc-comment claims a lockstep role it no longer has.
4. **Silent fallbacks worth hardening**: every `Queue*ForLockstep` falls back
   to direct execution when the target has no NetworkId (host-local, no
   warning); a peer that never receives MatchSettingsSync keeps the default
   seed silently.

## Rules distilled (for new code)

- Sim state changes only inside `SimulationSystemGroup`, timed by
  `SystemAPI.Time`, never `UnityEngine.Time`.
- Every player/AI-triggered mutation routes through `CommandRouter` with a
  `LockstepCommandType`; spends live in the executor, never the issue site.
- Anything that feeds spawn order or NetworkId assignment must iterate a
  deterministically ordered source — `FindObjectsByType` order never is.
- Stateful sim systems reset per match via `SpawnDelayHelper.MatchEpoch`.
- Seed RNG from `SpawnSeed`/tick/stable ids; never from raw float bits,
  wall clocks, or `UnityEngine.Random`.

## Desync #6 (2026-08-16, build 0.0.6) — first cross-machine match: Burst CPU dispatch

The first LAN match between two DIFFERENT machines (all prior testing was two
editors / two instances on one PC) desynced at tick 420. The log pair was
textbook: every `cmds` line byte-identical, sums identical through 390, fork
at 420 with the last command at 325 — pure simulation non-determinism. The
dumps agreed on 768 of 775 entities and every bank; the divergences were
three host-commanded units (0.2-0.6 m apart) and three in-flight construction
progresses (2-3 hp) — sub-millimetre float drift that hid below the
checksum's millimetre quantisation until it crossed decision thresholds
(build-range / arrival tests).

Root cause: no `BurstAotSettings_StandaloneWindows64.json` existed, so builds
used Burst's default `SSE2|AVX2` multi-target. The player picks a codepath
PER CPU at runtime, and AVX2 codegen (fused multiply-add) rounds differently
from SSE2 — so two CPUs of different generations simulate different low bits
from tick 1. Same-machine tests can never reach this class of bug.

Fixes (0.0.7, protocol 4):

- Burst AOT pinned to a single x64 target (SSE4) for Windows builds —
  `ProjectSettings/BurstAotSettings_StandaloneWindows64.json`, CpuTargetsX64
  bit 16. Every machine now executes identical instructions.
- Deterministic-mode checksum additionally mixes the raw position bits, so a
  fork is caught at the first sync check after it exists instead of at
  threshold-crossing — the fork tick now names the guilty window.
- Input delay is advertised as a 4th PING field and adopted max-wise
  (LockstepManager.AdoptPeerInputDelay). The dumps' headers read 5 vs 3:
  harmless for execution (ticks are issuer-stamped) but asymmetric pacing
  and misleading forensics.
- Desync dump rows go through `FormattableString.Invariant` (a PT-locale
  dump diffed against a dot-locale dump flags every line), and
  `NetworkedEntity.SpawnTick` is stamped from the new
  `NetworkIdGenerator.CurrentTick` (the column printed 0 forever; the spawn
  tick had to be reverse-engineered from the id's slot range).

Rules added:

- Editor pairs prove LOGIC determinism only; hardware determinism needs two
  different CPUs running pinned-target BUILDS. Never widen the Burst target
  set without a cross-CPU desync test.
- A quantised checksum field hides drift below its grid until a decision
  threshold amplifies it — deterministic mode must hash exact bits.
