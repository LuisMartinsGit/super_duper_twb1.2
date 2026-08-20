# Multiplayer Audit — Sync and Lag Sources (2026-07-15)

Full sweep of the multiplayer stack: lockstep core, command routing, UI/AI
mutation paths, simulation determinism, and runtime performance. Findings
only — nothing in this document has been fixed unless marked FIXED.

Scenario used for cost estimates: 2 players, 10 ticks/sec, 500-1000
networked entities, bursts of 50-100 commands per tick during large fights.

Already fixed earlier the same day (excluded from the open list):
lockstep player index = slot index; client waits for all humans; join
robustness (retry, dedup, AI-seat fallback, TWB_LEAVE); dedicated host join
socket; IssueLayeredMove replication; research replication via
EntityActionPanel + SimpleAISystem; formation rampart units.

FIXED in the follow-up pass (2026-07-15, same day): F1 (building level-ups
now replicate via LockstepCommandType.BuildingUpgrade=26 —
UpgradeBuildingCommandHelper split into Execute [validate+spend, issuer]
and ApplyDirect [mutation, every peer], all four callers route through it);
F4 (AI placement now uses the IssuePlaceBuilding out-param overload; queued
case dispatches builders at the position with a null target, human-MP
pattern); F5 (AI training routes through IssueTrain with CommandSource.AI,
level-gated BEFORE the spend); F13 (ActionPanelRegion research append);
F20 (both AI DispatchBuildersTo now pass CommandSource.AI). Perf item D1
fixed: LockstepManager caches one NetworkedEntity query (disposed in
OnDestroy) and resolves ids through a per-tick Dictionary rebuilt once per
tick; PlaceBuilding registers its entity into the map for same-tick
lookups; checksum reuses the cached query; the dead debug StringBuilder
block was removed.

FIXED in the third pass (2026-07-15, same day): F2/F3 (age-up + culture:
LockstepCommandType.AgeUp=27, CommandRouter.IssueAgeUp — CultureChoicePopup
both sites and SimpleAISystem.TryAgeUp route through it; duration
recomputed per peer, culture byte in TargetEntityId); F6 (temple upgrade:
TempleUpgrade=28, IssueTempleUpgrade — EntityActionPanel + HudBridge;
level/duration recomputed per peer); F7 (sect adoption: SectAdopt=29,
IssueSectAdoption — ReligionHUD, SectChoicePopup, SectPickerModal, and the
endgame AI all route the chapel-slot stamp; targeted slot index preserved,
build time in TargetPosition.x, replay-safe on already-building sects).
A2 match-start barrier: LockstepManager.HoldSimulationUntilPeersReady
disables SimulationSystemGroup after StartSimulation (loose mode only) and
releases it when the first TICK from every expected player has arrived;
released defensively in StopSimulation/OnDestroy. A4 partial mitigation:
each BroadcastTick re-sends the last 2 non-empty tick payloads ahead of
the current one, so a single lost/fragmented datagram self-heals within
the input-delay window; receivers ignore command payloads for
already-executed ticks (confirmations still count). Sustained multi-loss
still degrades — a full ack/resend layer remains future work.

---

## A. Architecture-level sync sources

**A1. Loose lockstep is the default.** Only commands are synchronized. The
ECS simulation itself is frame-driven per peer: auto-acquired combat
targets, mining progress, veil/Border CA growth, construction and training
timers all advance on local frame time. Two peers given identical commands
still drift; checksums (entity count + health) mismatch by design and only
halt under `GameSettings.DeterministicLockstep`. Everything else in this
document either amplifies or rides on top of this.

**A2. No match-start barrier.** The host loads the game scene immediately
after sending TWB_START; the client loads after receiving it. Nothing gates
frame-driven systems until both peers are connected, so the host's world
(income accrual, veil growth, match clock) starts seconds earlier than the
client's. The time origins never re-align. Mitigation: hold the sim (or at
least mutating system groups) until the first tick round-trip completes.

**A3. NetworkId tick partitioning vs frame-driven spawns.**
`NetworkIdGenerator` (Core/Multiplayer/LockstepTypes.cs) allocates IDs from
a per-tick partition (`BeginTick`), but mid-game spawns (training
completion, deaths freeing IDs aside) happen on frame boundaries. A unit
that finishes training one tick earlier on one peer lands in a different
partition, gets a different NetworkId, and every subsequent command
targeting it resolves to Entity.Null on the other peer — silently dropped.
This degrades any long match even with perfect command replication.

**A4. No UDP reliability layer.** TICK datagrams have no sequence/ack/
retransmit. A lost datagram usually recovers confirmation via later ticks,
which means the lost tick's commands are treated as "none" on the receiving
peer — silent command loss and divergence. A 50-100 command tick serializes
to several KB, over Ethernet MTU, so it fragments; one lost fragment drops
the whole datagram. Bursts (big fights) are exactly when loss is likeliest.
If confirmations stall entirely, command execution freezes with no recovery
path while the frame-driven sim keeps running (A1) — a divergence amplifier.

**A5. Per-peer resource banks.** Train/Research/PlaceBuilding spend on the
issuing peer only (established model). Remote peers never debit that
faction, so banks diverge from the first purchase. Mostly latent today
(clients don't run AI, affordability is rechecked locally), but any future
system reading another faction's bank will misbehave.

---

## B. Unreplicated state mutations (same bug class as the fixed right-click move)

The CommandRouter itself is clean — every Issue*/Set*/Cancel* method gates on
`ShouldQueueForLockstep`. All bypasses below are UI/AI code writing ECS
directly instead of calling the router, or calling post-lockstep executors.
`LockstepCommandType` tops out at `Research = 25`; none of the mutations
below has a command type, so none has any replication path.

### Critical — structural divergence

| # | Mutation | Where | Effect in MP |
|---|----------|-------|--------------|
| F1 | Building level-up (`BuildingUpgradeState`) | UpgradeBuildingCommand.cs:54-75 via HudBridge.cs:774, ActionPanelRegion.cs:438, EntityInfoPanel.cs:942, AIBuildingUpgradeSystem.cs:182 | Upgrade exists on one peer; remote peer then silently drops replicated Train commands gated on building level (CommandRouter.cs:975) — divergent buildings AND armies |
| F2 | Player age-up + culture (`AgeUpState`, `SetFactionCulture`) | CultureChoicePopup.cs:115-124, 367-377 | Era advance and entire culture tree invisible to remote peers |
| F3 | AI age-up | SimpleAISystem.cs:773-776 | Host-only: AI faction ages up on host, frozen in Age 1 on clients |
| F4 | AI building placement calls `PlaceBuildingDirect` (post-lockstep executor), not `IssuePlaceBuilding` | SimpleAISystem.cs:483, AIAlanthorEndgameSystem.cs:664, 724 | Every AI building exists on the host only |
| F5 | AI training appends `TrainQueueItem` directly | SimpleAISystem.cs:376, AIAlanthorEndgameSystem.cs:903 | Host spawns AI units clients never see |
| F6 | Temple upgrade (`TempleUpgradeState`) | EntityActionPanel.cs:909, HudBridge.cs:901 | Temple level, RP grants, sect lever levels diverge |
| F7 | Sect adoption (`TryStartAdoption` + `TempleChapelSlot`) | ReligionHUD.cs:406, SectChoicePopup.cs:296, SectPickerModal.cs:212, AIAlanthorEndgameSystem.cs:296 | Religion state, combat bonuses, power availability diverge |

### High — combat/structure desync

| # | Mutation | Where |
|---|----------|-------|
| F8 | Sect active-power casts (`SectActivePowerHelper.Fire` — AoE damage, spawned strikes) | ReligionHUD.cs:439, HudBridge.cs:968 |
| F9 | Wall hub/segment placement (no IsMultiplayer branch, unlike normal placement in the same file) | BuildCommandPannel.cs:745-858 |
| F10 | Fiendstone Keep wing (`KeepWingConstruction`) | EntityActionPanel.cs:1220 |
| F11 | Wall upgrade to Tower/Gate (`WallUpgradeState`) | EntityActionPanel.cs:2145, 2167 |
| F12 | Reliquary build + abilities (direct factory + `ReliquaryHelper.Fire`) | EntityActionPanel.cs:1682, 1701-1709 |

### Medium — economy/unit-state desync

| # | Mutation | Where |
|---|----------|-------|
| F13 | Research append missed in one file (sibling of the fixed paths) | ActionPanelRegion.cs:761 |
| F14 | Unit promotion (`UnitRankCommandHelper.Execute`) | EntityActionPanel.cs:2248 |
| F15 | Vault deposit/withdraw (bank + `VaultStorage`) | EntityActionPanel.cs:1988-2006 |
| F16 | Bazaar pack/unpack command tags | EntityActionPanel.cs:396, 2070 |
| F17 | ~~Miner drop-off right-click~~ (obsolete — the drop-off mechanic was removed 2026-07-20; mined resources credit the bank directly) | — |
| F18 | Shift-queued waypoints (`QueuedCommand` buffer) | RTSInputManager.cs:956-979 |
| F19 | AI worker-flee retasking | AIAlanthorEndgameSystem.cs:992-1011 |

### Low — semantic

- F20: AI builder dispatch omits `CommandSource.AI` (defaults LocalPlayer) —
  SimpleAISystem.cs:584, AIAlanthorEndgameSystem.cs:1083. Queues by luck, but
  attributes AI orders to the player stream and pairs with F4 targets that
  do not exist on clients.
- SetRallyPoint lockstep payload drops `TargetEntity`
  (CommandRouter.LockstepQueue.cs:243-260): rally-onto-deposit auto-gather
  works in SP, silently lost for everyone in MP.
- Stale comments claim EquipmentUpgrade/GodPower "log and drop" in MP —
  both actually replicate; comments only.

---

## C. Simulation nondeterminism (matters as DeterministicLockstep matures)

RNG hygiene is largely good: world-gen, deposits, veil growth, Border AI,
and SimpleAISystem all seed from `GameSettings.SpawnSeed` and/or the
lockstep tick. Open items:

- HIGH (scenario-only): ScenarioWaveSpawner.cs:34-53 spawns combat units on
  frame timers with unseeded `UnityEngine.Random` (positions + walk
  targets). Not reachable in normal MP matches today.
- MEDIUM: RitualDefenseSystem.cs:92 seeds from `entity.Index` + a
  system-local counter — one extra/missing spawn permanently forks the
  stream. NodeStateDeathInterceptSystem.cs:181 and
  AIAlanthorEndgameSystem.cs:1107 seed from hashed float world positions;
  positions are explicitly not bit-identical across peers, so these forks
  are expected, affecting death-wave composition and AI building rings.
- LOW: VictoryConditionSystem uses wall-clock `Time.time` and dictionary
  iteration for elimination checks (outcome timing/banner order only).
- `UnityEngine.Random.InitState` is never called anywhere — any future
  gameplay use of the global stream is automatically divergent.

---

## D. Lag sources, ranked

1. **`FindEntityByNetworkId` — CRITICAL** (LockstepManager.cs:686-707).
   Creates a new `EntityQuery` (never disposed — a genuine leak that
   progressively slows ALL structural changes world-wide) plus a
   `ToEntityArray(Temp)` sync-point and O(N) scan, per lookup, 1-3 lookups
   per command. Big-fight bursts: up to ~200k component reads and ~2k leaked
   queries per second. Fix shape: one cached query + a NetworkId→Entity
   NativeHashMap maintained on spawn/despawn.
2. **No reliability + MTU fragmentation on command bursts — HIGH** (see A4).
   Lag manifests as command freezes and unrecoverable stalls, not framerate.
3. **String protocol GC — MODERATE.** Per-tick StringBuilder/Format("R"
   floats)/Split/List allocations scaling with command volume
   (LockstepTypes.cs:97-135, LockstepManager.cs:399, 713, 797, 824). TICK
   sent every tick even with zero commands.
4. **Checksum full scan every 30 ticks — LOW-MODERATE**
   (LockstepManager.cs:899-932). Leaks one more query per call and is wasted
   work in the default mode where mismatches are ignored.
5. **Dead debug StringBuilder** built and discarded every 3 s
   (LockstepManager.cs:186-198); relay `string.Join` allocates even with no
   second remote. Trivial.
6. Lobby-only churn (MultiplayerPanel rebuild every 0.5 s) — zero in-game
   cost.

`NetworkIdGenerator` is O(1) and clean. No other per-frame NetworkedEntity
scans exist outside LockstepManager.

---

## Suggested fix order

1. F1 + F4 + F5 (building upgrades, AI placement, AI training) — these are
   the "AI/opponent looks frozen or ghostly" bugs, the biggest visible sync
   breaks in a normal 1v1 vs or with AI.
2. FindEntityByNetworkId cache (D1) — one contained change, removes the
   query leak and the O(commands x entities) scan.
3. F2/F3 age-up + F6/F7 temple/sects — new lockstep command types
   (BuildingUpgrade, AgeUp, TempleUpgrade, SectAdopt) following the
   LayeredMove/Research pattern.
4. Match-start barrier (A2) and TICK reliability (A4 — per-player last-N
   command history resend or ack-based).
5. The medium F-list, then nondeterminism seeds (C) as DeterministicLockstep
   becomes the target.
