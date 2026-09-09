# Saved games and replays — plan (2026-09-07)

What the codebase has today, what each feature needs, and the order to build
them in. Companion to [Multiplayer_LAN_Readiness.md](Multiplayer_LAN_Readiness.md):
both features stand on the same foundation as multiplayer — a deterministic
simulation fed by a stream of tick-stamped commands — and most of that
foundation already exists because multiplayer needed it first.

---

## 0. Inventory — what exists

| Piece | State | Where |
|---|---|---|
| Save system | **None.** No `SaveSystem`, no autosave, no pause-menu entry. `Menu_Item_LoadGame` exists in MainMenu.unity and is hidden in the alpha build | [Alpha_Build.md §Saved games](Design/Alpha_Build.md), `ShipGateMenuTrim.cs` |
| Replays | **None** as a feature. Two pieces of a recorder exist for other reasons (below) | — |
| Command stream | Every player and AI action is a `LockstepCommand` (`Type`, `PlayerIndex`, `Tick`, `CommandIndex`, `EntityNetworkId`, `TargetPosition`, `TargetEntityId`, `SecondaryTargetId`, `BuildingId`) — 42 types, with a **lossless** text `Serialize()` (R-format floats) | `Core/Multiplayer/LockstepTypes.cs` |
| Tick execution | `LockstepManager.ProcessTick`: sort commands into a total order → execute → step the sim **exactly once at a fixed dt** (30 Hz) | `Multiplayer/Lockstep/LockstepManager.cs` |
| Per-tick command log | `LockstepLog.Tick` writes every executed command per tick, in execution order — diffable between peers. Positions are rounded to **millimetres**, so it is a diagnostic, not a replay file | `Multiplayer/Lockstep/LockstepLog.cs` |
| State checksum | `LockstepStateHash.Compute` every 30 ticks — banks, queues, positions, veil grid, research… | `Multiplayer/Lockstep/LockstepStateHash.cs` |
| Determinism | **Proven under lockstep**: the headless harness ran 7 matches across 4 processes bit-identical, 25 min longest (0.0.20). Seeds from `GameSettings.SpawnSeed` per `SimCadence.Epoch`; network ids partitioned per tick; `SimCadence.MatchTimeOr` for sim timing | CHANGELOG 0.0.20, `SimCadence.cs`, `NetworkIdGenerator` |
| **Single-player** | The sim runs **frame-driven with variable dt** — `LockstepFixedRateManager` is installed in multiplayer only ("single-player is unaffected, it keeps running the sim per-frame"). **Single-player is not deterministic today.** | `LockstepFixedRateManager.cs` header |
| AI | Runs on the host only (`ShouldRunAIBrains`); its decisions leave as lockstep commands. The AI is an **input source, not part of the sim** — it keeps managed dictionaries and wall-clock cadences and is not required to be deterministic | `CommandRouter.ShouldQueueForLockstep`, `GameSettings.ShouldRunAIBrains` |
| Curse | In-sim, seeded per epoch (`CurseTerritorySystem._rng`, `BloodCurseSpawnSystem`) — reproduces from the seed, needs no commands | `Border/CurseTerritorySystem.cs` |
| Position recorder | `DeterminismReplaySystem` (nav audit tool): records per-tick unit positions in mm and diffs a second run against them. The comparator half of a replay verifier | `Systems/Navigation/Debug/DeterminismReplaySystem.cs` |
| Headless runner | `HeadlessMp` boots a match from command-line args, runs to a limit, exits with a verdict code | `Bootstrap/HeadlessMp.cs` |

Sim tick cost, from the match Perf logs: **≈2–5 ms per tick** mid-game (the
44 ms spikes are AI thinks). At 30 Hz a 20-minute match is 36 000 ticks —
that number matters for §2.

### 0.1 State that is NOT in the ECS world (matters for snapshot saves)

The world's 439 component types are all blittable (no managed components, no
shared components; three blob references, all nav data rebuilt at boot). Unity's
`SerializeUtility.SerializeWorld` can take that world **except** seven
singletons that hold native containers (`VeilField`, `NavCostField`,
`NavFlowCache`, `NavRequestQueueSingleton`, `PortalOwnerBitsMirror`,
`WallPortalSpecList`, `DeterminismReplayLog`) — the nav ones are rebuilt from the
map, the veil grid must be written by its owner.

Outside the world entirely, and invisible to any generic serializer:

- **Statics / singletons:** `TerritoryOwnership` (owner arrays, curse-held set),
  `PlayerInfluenceMap._values`, `BloodMap._values`, `FactionResearchState`
  (completed techs per faction), `FactionEconomy._bankCache` (rebuildable),
  `NetworkIdGenerator` counters, `SimClock`, `SimCadence.Epoch`,
  `HeroTrainLimit._kingLexorRespawns`, `FogOfWarManager` (revealed grid),
  `VictoryConditionSystem.Instance`.
- **AI memory (managed, per faction):** `SimpleAISystem` missions / plans /
  claim timers / wave heartbeat, `ScoutDirectorSystem` zones + plans, `AIBudget`
  brains + queues, `AIPivotalReserve` pending + hold-since, `ThreatMaps` grids,
  `AIEndgameCommon` temple-block ticks, `IdleFormUpSystem` formed sets.
- **System-private fields:** 24 files with `SimCadence.Periodic` timers, 3 with
  `Unity.Mathematics.Random` streams, `CurseTerritorySystem` (`_anchors`,
  `_nextWaveAt`, `_held`, `_waves`, `_rng`), `VeilFieldSystem` scratch arrays.
- **Presentation:** `PresentationViewSpawned` marks (must be stripped so views
  respawn), camera pose, selection.

Every one of these is a place a snapshot save can silently go wrong. A
command-stream save (§2, design A) has none of them, which is why it comes first.

---

## 1. Replays

### 1.1 The premise, corrected

> "should only depend on seed and human player commands"

Seed + commands is exactly right, with one correction: **the AI's commands
have to be in the stream too.** The AI is deliberately outside the simulation
(host-only, managed state, wall-clock cadence) — that is what let multiplayer
work without making the AI deterministic. Replaying a human's commands alone
would re-run the AI live, and it would make different decisions. The stream
already carries `PlayerIndex`, so an AI slot's commands are recorded like
anyone's; nothing extra is needed. The curse needs nothing: it is in-sim and
seeded.

"When" is the **tick**, never wall time. "Where" is `TargetPosition` in the
lossless R-format the network already uses — not `Lockstep.log`'s millimetres.

### 1.2 How it works

**File** `logs/<match>/Replay.twbr` (text, gz-able; a 30-minute match is a few
thousand commands, well under 1 MB):

```
header   build fingerprint + version, map scene + MapInfo bake hash,
         GameSettings match fields (SpawnSeed, StartAge, StartCulture,
         BorderEnabled, FogOfWar, MaxStartingResources, Mode, TotalPlayers,
         SpawnLayout, TwoSides), LobbyConfig.Slots (type, faction, AI
         difficulty + strategy, colour, team, start index, name),
         TicksPerSecond, InputDelayTicks
tick N   commands in execution order, LockstepCommand.Serialize() + PlayerIndex
hash N   the SimStateHash every 30 ticks (already computed in MP)
footer   final tick, outcome, final hash
```

**Playback:** boot the header's map and settings, `IsObserver = true`, local
input barred from issuing commands, a `ReplayLockstepService` (implements the
existing 4-member `ILockstepService`) hands each tick's recorded commands to
the existing `ProcessTick`. Speed = ticks per frame (pause, 1–8×, "jump to
tick" = ticks with presentation muted). The recorded hashes make every replay
**self-verifying**: playback compares its own hash at each mark and reports the
first divergent tick — the same first-differing-line idea `Lockstep.log` was
built on.

### 1.3 What is missing

| # | Work | Notes | Size |
|---|---|---|---|
| **R1** | **Solo lockstep.** Run single-player through `LockstepManager` with no sockets: `InitializeSolo()` (expected players = {0}, `StartNetwork` skipped, ticks self-confirm), `LockstepFixedStep.Install` in SP, `ShouldQueueForLockstep` → true whenever a lockstep service runs (drop the `!IsMultiplayer` short-circuit), input delay 1 tick (33 ms, imperceptible) | The keystone. Makes SP deterministic — which also hands every SP match the multiplayer determinism guarantees, and makes SP bugs reproducible from a file. The harness already proved the sim under this driver; the per-frame path is the odd one out | 1–2 d |
| **R2** | Recorder at `ProcessTick` (after the sort, before execution) + header at `StartSimulation` + hashes + footer | Reuse `Serialize()`; write-through so a crash keeps everything up to it | 0.5 d |
| **R3** | Player: `ReplayLockstepService`, a driver that replaces `LockstepManager.Update` pacing, `CommandRouter` refuses `LocalPlayer` commands while replaying, presentation-muted fast-forward | `ProcessTick` already takes a command list; it needs an injectable source | 1–2 d |
| **R4** | Command-coverage audit: every sim mutation must be a command. 20 Presentation files still write ECS directly — most are presentation (health bars, tooltips, camera); `TopChoiceBar`, `SpellsPanelBinder`, `BuildCommandPannel` (wall hubs), `SandboxPanel`, `TutorialDirector` need checking. The 2026-07-15 audit's F1–F20 were mostly closed by `Replication2026` | Mechanical once R1–R3 exist: record, replay, diff. A miss shows up as a hash divergence at a known tick | 1–2 d |
| **R5** | Header validation: refuse another build fingerprint or map bake with a clear message | Replays are build-bound by nature | 0.5 d |
| **R6** | UI: Replays screen (list, play), playback HUD (speed, clock, faction view), "record replays" on by default | New menu scene in the Skirmish look, like Scenarios/Settings | 1–2 d |
| **R7** | Verifier: `-twbReplay <file>` headless mode on `HeadlessMp` — play, compare the final hash, exit code. Release checklist: record three matches, replay all three | Turns "still deterministic?" into a CI question | 0.5 d |

Order: R1 → R2 → R3 → R7 (so R4 can be done by diffing) → R4 → R5 → R6.

---

## 2. Saved games

### 2.1 Two designs, and the order to build them

**A — Replay as save.** A save is the replay file plus a marker tick. Load =
boot the map, fast-forward the recorded commands to the marker with
presentation muted, then swap the replay service for the solo service and hand
control back. Tiny files, always consistent, **zero per-system serializers**.
Its one cost is load time: at 2–5 ms a tick, a 20-minute match takes **1–3
minutes** to load, a 40-minute one 3–6. That is fine for "continue the match I
quit last night" and wrong for a 45-minute campaign save.

**B — World snapshot.** `SerializeUtility.SerializeWorld` for the ECS world,
plus a `SaveRegistry` where every owner of state from §0.1 registers a
(write, read) pair, plus rebuild of derived state after load (passability
stamps, region map, nav cost field, territory from influence, fog, view
respawn). Instant load, a few MB. Its cost is the surface: every forgotten
timer, RNG or AI dictionary is a bug that only shows minutes later.

**Build A first, then B, and use A as B's oracle.** A reuses R1–R3 almost
entirely. B's correctness test is then mechanical: save → load →
`LockstepStateHash` equal; and save → load → run N ticks versus replay → run N
ticks — equal hashes or B is missing something.

### 2.2 What is missing

| # | Work | Notes | Size |
|---|---|---|---|
| S0 | R1–R3 | prerequisite | — |
| **S1** | Save = flush the replay + write the marker; "Continue" on the main menu = load, fast-forward, hand over | Autosave is free: it is a marker | 0.5 d |
| **S2** | Fast-forward mode: `ProcessTick` in a tight loop per frame, `PresentationSystemGroup` off, `PresentationSpawnSystem` deferred to the end, loading screen with tick progress | The presentation partition exists (Rendering vs Simulation groups) | 1 d |
| **S3** | Snapshot writer/reader (B): `SerializeUtility` + `SaveRegistry` + owner serializers for everything in §0.1, versioned header, atomic write, slots under `persistentDataPath/Saves/` | The bulk of B; ~20 owners | 3–4 d |
| **S4** | Load branch in `GameBootstrap` after "terrain generation complete": skip the spawn phases, deserialize, rebuild derived state, strip `PresentationViewSpawned` so views respawn | Bootstrap phase list is already traced in the log | 1–2 d |
| **S5** | Integrity tests using A as the oracle (above) | Headless, on `HeadlessMp` | 0.5 d |
| **S6** | UI: pause-menu "Save Game" with slots, unhide `Menu_Item_LoadGame`, slot list with map / time / age | Pause menu is code-built (`PauseMenuPanel.cs`), four buttons today | 1 d |
| S7 | Multiplayer saves | Out of scope: every peer needs the same snapshot and a resync. Each peer already records its own replay | — |

---

## 3. Risks and decisions

- **Build-bound formats.** Any sim change invalidates replays and design-A
  saves (the sim would take a different path). Design-B saves survive sim
  changes but not component-layout changes. Rule: a save or replay is valid
  for the version that wrote it; the header says which, and the loader refuses
  the rest. Same rule multiplayer already enforces on its version handshake.
- **Determinism debts still on the books** (from the 07-15 audit; the 09-05
  harness passed, so most are closed): `VictoryConditionSystem` uses
  `Time.time` (banner timing only); scenario spawners use `UnityEngine.Random`
  (dev scenes); two systems seed from float positions. R7 finds any survivor
  at a tick number.
- **Solo lockstep changes the feel of SP** — to what multiplayer already
  feels like: a 30 Hz fixed-step sim with one tick of input delay. It also
  removes the "works in SP, desyncs in MP" class of bug, because there is no
  longer a separate SP path.
- **Where it lives:** `Scripts/Core/Replay/` (format, recorder — Runtime),
  `Scripts/Multiplayer/Lockstep/ReplayLockstepService.cs` + solo init,
  `Scripts/Core/Save/` (registry, snapshot), `Scenes/Menus/Replays/` and the
  pause menu for UI. Design folder: a short `docs/Design/Replays_And_Saves.md`
  before R6/S6 land, per CLAUDE.md.

Total: replays ≈ 6–9 days to shippable; design-A saves ≈ 2 more; design-B
saves ≈ 6–8 more on top.
