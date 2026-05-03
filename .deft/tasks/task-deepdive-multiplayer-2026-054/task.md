---
deft:
  id: task-deepdive-multiplayer-2026-054
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: critical
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [code-quality, code-review, deep-dive, multiplayer, lockstep, determinism, architectural]
---

# Multiplayer / Lockstep / Commands / Input deep-dive

## Context

Companion to task-052 (AI) and task-053 (Movement). Same method: deft-review agent with explicit "no Grep snippets, full Read" instruction; full reads of every multiplayer/lockstep/command-routing/input file (~30 files, 247K agent tokens).

This was the **highest-stakes domain to audit** — the surface scan in task-051 flagged 3 unverified critical claims, and those needed actual verification before being acted on.

## User Value

The multiplayer subsystem is **fundamentally broken at the architectural level**. Not "has bugs" — actually does not function as a deterministic lockstep simulation. The transport, lobby, discovery, and command-queue layers all *appear* coherent, but the simulation that would consume synced commands is non-deterministic from the start.

## TL;DR — what's actually wrong

1. **Lockstep transport is decoupled from the simulation it's supposed to lock.** Every system in `SimulationSystemGroup` (Movement, Combat, Mining, Training, Construction, Spell, Cooldown) reads `SystemAPI.Time.DeltaTime` (= local frame time) and runs every Unity frame. Two peers at 30fps and 60fps integrate damage/training/HP/cooldowns at completely different rates between lockstep ticks. The lockstep tick CADENCE is correct (only commits when all peers confirm) but everything BETWEEN ticks diverges.
2. **Lockstep `ExecuteCommand` switch is missing the `Ability` case** — abilities are queued and silently dropped on every peer, including the issuer.
3. **Wire format drops `CommandIndex`** — ProcessTick sorts remote commands by `(PlayerIndex, CommandIndex)` but every deserialized command has CommandIndex=0, so they tiebreak via non-stable List.Sort. Same three commands can execute in different orders on different peers.
4. **Multiple UI fast-paths spend resources locally without lockstep**: train, research, build, miner-dropoff, forge-supply. Local peer pays, remote peers don't deduct.
5. **`SimpleAISystem` bypasses lockstep entirely** AND mis-tags AI commands as `LocalPlayer` (so they get queued on every peer's AI = doubled commands). It's also not gated on `IsHost()` — every peer runs its own AI loop.
6. **Entities spawn from frame-rate-driven systems** (`CrystalDeathDropSystem`, `TrainingSystem.SpawnUnit`) → NetworkIds allocated outside ProcessTick → diverge across peers → subsequent commands targeting those entities silently dropped.
7. **Checksum-mismatch handler is an empty `{ }`** — desyncs detected but never reported. Combined with #1 and #6, the game is *guaranteed* to silently desync within seconds.
8. **No UDP retransmit** — single packet loss stalls every peer until the next non-lost tick.
9. **Two parallel lobby implementations** — `Multiplayer/Lobby/LobbyManager.cs` + `LobbyUI.cs` are dead code; `UI/Menus/MultiplayerLobbyUI.cs` is the live one.
10. **`BattalionStance` change bypasses lockstep** — only local peer sees the stance change.

To make multiplayer actually work would require: (a) moving simulation onto a fixed-step budget driven by lockstep ticks, not Unity wall-clock, (b) gating every entity-creating path through `ProcessTick`, (c) routing every cost-paying or state-mutating UI/AI action through CommandRouter, (d) implementing reliability on top of UDP, (e) handling checksum desyncs. This is many weeks of work — multiplayer is currently aspirational.

## Requirements

- R1: Acknowledge that multiplayer is non-functional today and either deprioritize it OR commit to the architectural rework.
- R2: Triage individual findings F-1 through F-12.
- R3: For findings that are easy single-line fixes (F-1 missing Ability case, F-7 empty checksum handler, F-9 cosmetic indentation, F-12 SpawnTick=0), batch into a small "lockstep correctness pass 1" task.
- R4: For architectural findings (F-2 dt-driven simulation, F-5 SimpleAISystem bypasses lockstep, F-6 NetworkId mid-frame allocation), file a single "multiplayer architecture rework" tracking issue.
- R5: Delete dead code (F-10) immediately to avoid confusion.

## Acceptance Criteria

- [ ] User decision: pursue multiplayer or table it
- [ ] If pursued: F-1, F-3, F-4, F-7, F-9, F-10, F-11, F-12 fixed in a focused PR
- [ ] If pursued: tracking issue opened for F-2, F-5, F-6 (architectural rework)
- [ ] If tabled: explicit `[DisableAutoCreation]` on `LockstepBootstrap` so the dead transport doesn't run

---

## Verified Findings

### F-1 — `LockstepManager.ExecuteCommand` is missing the `Ability` case (silent drop on every peer)
**File:** [LockstepManager.cs:424-523](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L424-L523)
**Severity:** Critical (confirms task-051 unverified claim)

`LockstepCommandType.Ability = 15` (LockstepTypes.cs:37). `CommandRouter.QueueAbilityForLockstep` (LockstepQueue.cs:282-300) constructs and queues `Type = LockstepCommandType.Ability`. The switch in `ExecuteCommand` enumerates Move, Attack, Stop, Gather, Build, Heal, SetRally, AttackMove, Repair, Convert, Patrol, HoldPosition, Train, PlaceBuilding — but **no `case LockstepCommandType.Ability:`**. C# switch with no default arm does nothing. So in MP, every IssueAbility goes through `ShouldQueueForLockstep == true → QueueAbility...`, and on every peer (including the issuer, which never direct-executes when in MP) the lockstep replay drops the command. Litharch heal, Cadaver crystal cast, every UnitAbility — all dead in MP.

**Fix:** Add `case LockstepCommandType.Ability: IssueAbilityDirect(em, entity, targetEntity); break;` to the switch.

### F-2 — Lockstep tick accumulator uses non-deterministic frame time (architectural)
**File:** [LockstepManager.cs:168](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L168)
**Severity:** Critical, architectural (confirms task-051 unverified claim with broader scope)

`_tickAccumulator += Time.deltaTime;` advances ticks at local wall-clock rate. The tick CADENCE is fine (lockstep itself only commits when all peers confirm), but every system in `SimulationSystemGroup` (MovementSystem.cs:55, MeleeCombatSystem.cs:44, MiningSystem, TrainingSystem, BuildingConstructionSystem, etc.) also reads `SystemAPI.Time.DeltaTime` for its delta and runs every Unity frame. Between two lockstep ticks (~100ms), peer A might run 6 simulation frames while peer B runs 12, with completely different delta integrations.

**Fix:** Drive simulation off `LockstepManager.CurrentTick` (e.g. sim runs in fixed steps of `TICK_DURATION` per lockstep tick), OR run the entire `SimulationSystemGroup` only from inside `ProcessTick`. Many-week architectural change.

### F-3 — `LockstepCommand.Serialize` drops `CommandIndex`, breaking deterministic sort
**Files:** [LockstepTypes.cs:87-94](Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L87-L94) and [LockstepManager.cs:371-376](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L371-L376)
**Severity:** High (per-tick determinism)

Wire format: `Type,EntityId,PosX,PosY,PosZ,TargetId,SecondaryId,BuildingId`. NO field for `CommandIndex`. On `ProcessTickMessage`, only `PlayerIndex` and `Tick` are restored after deserialize; `CommandIndex` stays at default `0`. Then `ProcessTick` sorts by `(PlayerIndex, CommandIndex)` — every deserialized command from one player has CommandIndex=0, so they tiebreak via `List<T>.Sort` which is **not stable in .NET** (uses introsort).

**Fix:** Add CommandIndex to wire format and parse it back on receive.

### F-4 — UI fast-paths spend resources locally without lockstep
**Files:**
- [EntityActionPanel.cs:294-301](Assets/Scripts/UI/Panels/EntityActionPanel.cs#L294-L301) (train)
- [EntityActionPanel.cs:1030-1044](Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1030-L1044) (research)
- [BuildCommandPannel.cs:423-432](Assets/Scripts/UI/Panels/BuildCommandPannel.cs#L423-L432) (build placement)
- [RTSInputManager.cs:643-687](Assets/Scripts/Input/RTSInputManager.cs#L643-L687) (`IssueMinerDropOff`)
- [RTSInputManager.cs:694-735](Assets/Scripts/Input/RTSInputManager.cs#L694-L735) (`IssueForgeSupply`)

**Severity:** High

Each path calls `FactionEconomy.Spend` or mutates ECS state directly on the local EntityManager BEFORE (or instead of) routing through CommandRouter. `TrainCommandDirect`/`PlaceBuildingDirect` (the lockstep-replay helpers) don't spend or repeat the state mutation. Net: in MP, the local player pays for things other peers never deduct, and miner/forge orders never replicate.

**Fix:** Route every cost-paying or state-mutating UI action through CommandRouter; the helper executed on each peer must be the one that spends + applies state, not the UI layer.

### F-5 — `SimpleAISystem` bypasses lockstep AND mis-tags commands as `LocalPlayer`
**File:** [SimpleAISystem.cs:274, 320](Assets/Scripts/AI/SimpleAISystem.cs#L274)
**Severity:** Critical

- Line 274: `CommandRouter.PlaceBuildingDirect(em, buildingId, pos, faction)` — called by AI directly. `PlaceBuildingDirect` is the post-lockstep helper that other peers call from `case LockstepCommandType.PlaceBuilding`; calling it directly skips lockstep entirely. AI buildings appear on host only.
- Line 320: `CommandRouter.IssueBuild(em, idle[i].Entity, site, buildingId, sitePos)` — uses default `source = CommandSource.LocalPlayer`. SimpleAISystem itself isn't gated on `IsHost()` — every peer runs its own AI. So both peers' AI generate "LocalPlayer" build commands, both queue, both execute on every peer = doubled commands.
- Line 270: `FactionEconomy.Spend` runs locally before queueing.

Same pattern in [AIBuildingManager.cs:273](Assets/Scripts/AI/Managers/AIBuildingManager.cs#L273) — `BuildingFactory.Create(ecb, ...)` directly. That one IS host-gated, so on the host it creates a local building with no lockstep replication; clients never see it.

**Fix:** Gate `SimpleAISystem` on `IsHost()`; route AI building placement through `CommandRouter.IssuePlaceBuilding(..., CommandSource.AI)`; route AI builder dispatch through `AICommandAdapter.IssueBuild` (which already passes `CommandSource.AI`).

### F-6 — `CrystalDeathDropSystem` and `TrainingSystem.SpawnUnit` allocate NetworkIds at frame rate
**Files:** [CrystalDeathDropSystem.cs:57-60](Assets/Scripts/Systems/Crystal/CrystalDeathDropSystem.cs#L57-L60) and [TrainingSystem.cs:159-165, 193](Assets/Scripts/Systems/Training/TrainingSystem.cs#L159-L165)
**Severity:** High

Both run in `SimulationSystemGroup` (frame-rate driven, F-2). On each frame they call `Cadaver.CreateOrMerge` / `UnitFactory.Create` which call `NetworkIdGenerator.GetNextId()`. Because deaths and training completions are dt-driven (HP from MeleeCombatSystem dt; TrainingState.Remaining from TrainingSystem dt), the SAME spawn happens on different lockstep ticks on different peers. A spawn that happens on tick 5 on peer A and tick 6 on peer B gets a different NetworkId base, then `FindEntityByNetworkId` (LockstepManager.cs:526-547) silently returns Entity.Null on the peer that doesn't have the matching ID, and any subsequent command targeting that entity is dropped (LockstepManager.cs:411-414).

**Fix:** Spawn entities only inside `ProcessTick` (in response to lockstep commands), not from mid-frame simulation systems.

### F-7 — Checksum desync detected but silently ignored
**File:** [LockstepManager.cs:701-706](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L701-L706)
**Severity:** High

```csharp
if (_checksums.TryGetValue(tick, out uint localChecksum))
{
    if (localChecksum != remoteChecksum)
    {
    }
}
```

Empty body. No log, no game-over, no resync. Combined with F-2/F-6 the game is *guaranteed* to desync within seconds and *guaranteed* to never tell anyone.

**Fix:** Log + raise an event so the host can pause and surface a desync banner; ideally request a state snapshot from host.

### F-8 — UDP only, no retransmit
**File:** [LockstepManager.cs:185-216, 567-577](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L185-L216)
**Severity:** Medium

Single packet loss stalls every peer until the next non-lost tick. UDP loss on a LAN is rare but inevitable on Wi-Fi.

**Fix:** Either layer reliability (sequence numbers + ack + retransmit) or use TCP for the tick stream.

### F-9 — `BroadcastTick` exception handler indentation is malformed but compiles fine (REFUTES task-051 claim)
**File:** [LockstepManager.cs:567-577](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L567-L577)
**Severity:** Cosmetic (not the compile error task-051 claimed)

Line 574's `{` is at column 0 — this is **valid C#**. The block compiles, the exception is silently swallowed. Previous surface scan (task-051 M-2) called this a compile error; it is not.

**Fix:** Reformat for readability; consider logging the swallowed exception.

### F-10 — Two parallel multiplayer lobby implementations; one is dead code
**Files:**
- `Assets/Scripts/Multiplayer/Lobby/LobbyManager.cs` (738 lines, dead)
- `Assets/Scripts/Multiplayer/Lobby/LobbyUI.cs` (652 lines, dead)
- `Assets/Scripts/UI/Menus/MultiplayerLobbyUI.cs` (998 lines, live — referenced by `MainMenuUI.cs:34, 66`)

**Severity:** Medium (maintenance footgun)

`AddComponent<LobbyUI>` returns zero hits across the codebase. Both implementations independently configure the `LockstepBootstrap` if they were to run.

**Fix:** Delete `Assets/Scripts/Multiplayer/Lobby/` entirely.

### F-11 — `BattalionStance` change bypasses lockstep
**File:** [CommandRouter.cs:393-401](Assets/Scripts/Core/Commands/CommandRouter.cs#L393-L401)
**Severity:** Medium

`IssueStanceChange` does not consult `ShouldQueueForLockstep`. Writes `BattalionStanceData` directly. In MP, only the local peer sees the stance change.

**Fix:** Add `LockstepCommandType.SetStance` and route through it.

### F-12 — `NetworkedEntity.SpawnTick` hardcoded to 0 everywhere
**Files:** Every spawn path — [BuildingFactory.cs:101, 178](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L101); [UnitFactory.cs:81, 140](Assets/Scripts/Entities/Units/UnitFactory.cs#L81); [Cadaver.cs:112, 139](Assets/Scripts/Entities/Cadaver.cs#L112); [IronDepositBootstrap.cs:214](Assets/Scripts/Bootstrap/IronDepositBootstrap.cs#L214); 5 Crystal*Node.cs files.
**Severity:** Low

The field was added (LockstepTypes.cs:171) for sync validation but is never populated correctly, so it's useless dead data.

**Fix:** Pass `LockstepServiceLocator.Instance?.CurrentTick ?? 0` at spawn.

---

## Architectural Smells

### S-1 — Lockstep transport is decoupled from the simulation it's supposed to lock
The clean separation `CommandRouter → LockstepManager → ProcessTick → Helper` is well-designed at the syntactic level. But ~50 simulation systems read `Time.deltaTime` and run in Unity's per-frame Update loop. The lockstep manager is essentially a glorified UDP chat room broadcasting commands that the simulation locally interprets at its own pace. To actually be lockstep-deterministic, the project would need: (1) a fixed-step simulation budget per tick, (2) every `dt` consumer switched to consume `TICK_DURATION` while inside `ProcessTick`, (3) all entity creation deferred into the ProcessTick window. None of (1)/(2)/(3) exists.

### S-2 — Two enums named `LobbySlotType` and `SlotType` with identical members
[LobbyTypes.cs:39-45](Assets/Scripts/Core/Config/LobbyTypes.cs#L39-L45) defines `SlotType { Empty, Human, AI, Observer }` and `:51-57` defines `LobbySlotType` with same members. Comment says "Alias for SlotType". Two parallel enums; the dead-code lobby uses one while live `MultiplayerLobbyUI` uses the other.

### S-3 — `PlaceBuildingDirect` is exposed as public to support replay, also called by AI to bypass lockstep
`PlaceBuildingDirect` (CommandRouter.cs:509) is `public` because LockstepManager calls it from `case LockstepCommandType.PlaceBuilding`. That same publicness invites direct calls from `SimpleAISystem.cs:274` and `BuildCommandPannel.cs:452` that bypass lockstep entirely. Either make it `internal` to a Lockstep-only assembly or add a `[CalledByLockstepOnly]` attribute + Roslyn analyzer.

### S-4 — `_remotePlayers.Count == 0` doubles as "single player" detection
`LockstepManager.CanAdvanceTick` returns `true` if `_remotePlayers.Count == 0`. For a host with no clients connected, this is also true. Reasonable but undocumented; an Observer or re-connecting client needs careful handling.

### S-5 — Per-faction RNG in `SimpleAISystem` is per-system instance, not per-faction-per-tick (also noted in task-052 F-9)
The state is shared across all factions iterated in OnUpdate. Iteration order depends on chunk layout, which diverges across peers in MP. Per-faction RNG keyed on `(seed, factionIndex, currentTick)` would be deterministic.

---

## Verification of the 3 Unverified Critical Claims from task-051

1. **Missing `Ability` case in `LockstepManager.ExecuteCommand`** — **CONFIRMED** (F-1).
2. **`LockstepManager.cs:168 Time.deltaTime` tick accumulation** — **CONFIRMED** (F-2). But the impact is BROADER than claimed: the tick cadence itself is fine (lockstep gates on peer confirmation); the real damage is that simulation systems consume `dt` independently of lockstep ticks.
3. **`SpawnPlacementHelper.cs:65-67` non-deterministic Random** — **REFUTED**. The earlier surface scan misread `Unity.Mathematics.Random.CreateFromIndex((uint)attempt)` as `UnityEngine.Random.Range`. `CreateFromIndex` is a deterministic PRNG — both peers compute the same offset for the same `attempt`. The real non-determinism in this file is upstream (entity creation order, see F-6).

---

## What I Verified Is Fine

- **`NetworkIdGenerator`** itself (LockstepTypes.cs:204-288) — bootstrap-mode pre-tick allocation is sequential and deterministic; tick-mode partition correctly catches divergence as a non-overlapping range. Implementation is solid; surrounding callers (F-6) are the issue.
- **`CommandRouter.ShouldQueueForLockstep`** (line 553-570) — correct routing logic for all four `CommandSource` values.
- **`CommandSource` enum** (ICommand.cs:18-31) — single canonical definition; the duplicate noted in `// Fix #235` was indeed removed and not reinstated.
- **`LockstepCommand.Serialize` float precision** — uses round-trip "R" format and `InvariantCulture` (correct fix for the previous F2 truncation desync).
- **`LockstepBootstrap` lifecycle** — `DontDestroyOnLoad` + `_initialized` guard prevents double init; `LockstepServiceLocator.Instance` is guaranteed available when AI brains start issuing commands.
- **`PlayerSpawnSystem` ID determinism** — calls `NetworkIdGenerator.Reset()` at the top, iterates `0..playerCount` deterministically. NetworkIds 1..(N*4) are deterministic across peers as long as both run the same `LobbyConfig.Slots[]`.
- **`AICommandAdapter`** — every method correctly tags commands as `CommandSource.AI` and gates with `ShouldAIIssueCommands() = IsHost()`. The Manager classes use this adapter. Bug is that `SimpleAISystem` and `AIBuildingManager` bypass it (F-5).
- **Lobby Discovery (`MultiplayerLobbyUI`)** — UDP broadcast pattern is sound; ReuseAddress fallback handles port collisions; broadcast/discovery cleanup timeout exists.

---

## Things I Deliberately Didn't Dig Into

- **Wall, gate, chapel, sect-building combat & passability** — orthogonal; their `dt` usage is part of S-1 umbrella.
- **Trade caravan / TradingPost determinism** — same `dt` problem, no novel finding beyond S-1.
- **PresentationSpawnSystem and visual sync** — presentation is local-only by design.
- **Camera, edge-scroll, hover, selection** — local-only input concerns.
- **Fog of war and minimap** — local-only display.
- **Detailed UDP packet loss math** — F-8 covers core point.
- **`ResearchSystem` internals** — F-4 already establishes the route is broken at the entry point.
- **Performance profile of `FindEntityByNetworkId`** — O(N) linear scan per command (LockstepManager.cs:526-547); not a correctness bug.
