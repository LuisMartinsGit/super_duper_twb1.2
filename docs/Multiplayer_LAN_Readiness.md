# LAN Readiness — Two Humans, One Match (2026-08-15)

> **STATUS: the P0 and P1 lists below are implemented** (2026-08-15, same day).
> Everything marked **DONE** is in the code and compiles; none of it has been
> through a two-machine test yet, so §7 is the next step and not optional. The
> findings are kept in full rather than deleted — they are the reasoning behind
> each change, and the test ladder in §7 is written against them.

What has to change before two people on the same LAN can play each other and
see the same game. Companion to [Multiplayer_Audit.md](Multiplayer_Audit.md)
(2026-07-15), which catalogued unreplicated mutations; this one is about
**determinism, lag and failure handling**, and re-checks the audit's open items
against the code as it stands a month later.

---

## Verdict

Two players can already **connect, sit in a lobby, and load into a match**. That
part works. What they cannot do is play *the same match*.

The blocker is not lag and it is not packet loss. It is that **only commands are
synchronised, never outcomes**, and the simulation that turns commands into
outcomes runs on each machine's own frame clock. Two peers given a byte-identical
command stream still resolve different fights, finish buildings at different
moments, and spawn different units. Within a minute or two the two worlds are
different games that happen to share a lobby.

There is a designed fix already half-built in the repo — `DeterministicLockstep`
— and it is **unreachable in any shipped build**: the flag is declared `false`
and nothing anywhere assigns it ([GameSettings.cs:356](../Assets/Scripts/Core/Settings/GameSettings.cs#L356)).
Turning it on is one line, but the code behind it has never run a match, and
switching it on today would expose the four problems in **§3** immediately.

Realistic shape of the work: **the P0 list below is the difference between
"broken" and "playable 1v1 on LAN"**. Everything after that is polish, headroom
and diagnostics.

---

## 1. The stack as built

| Layer | Where | State |
|---|---|---|
| Discovery | UDP broadcast 47515, both directions (2026-08-16) | Host adverts on every up interface's subnet-directed broadcast (not just 255.255.255.255, which Windows routes out ONE interface — VPN/Hyper-V/WSL adapters routinely steal it); browsing clients also send `TWB_FIND` probes that the host answers unicast, so discovery survives a client-side firewall eating the broadcast. Direct IP remains the fallback |
| Lobby / control | Host's game port (default 7979), exclusive `_joinSocket` | Works; retry, dedup, AI-seat fallback, `TWB_LEAVE` |
| Lockstep transport | UDP, host `gamePort+1`, client `gamePort+1+slotIndex` | Works, no reliability layer |
| Tick model | 10 ticks/s, 2 ticks input delay (200 ms), 60-tick buffer | Works |
| Command replication | `CommandRouter` → `LockstepCommandType` 1..29 | Good coverage, real gaps (§4) |
| Simulation | ECS, **frame-driven** by default | Not deterministic (§3) |
| Fixed-step sim | `LockstepFixedRateManager` | Written, wired, **never enabled** |
| Desync detection | Checksum every 30 ticks (3 s) | Present; weak, and ignored unless the flag is on |
| Lag/latency | `PING`/`PONG` handled | **`PING` is never sent by anyone** — latency is never measured |
| Failure handling | — | **None** |

The tick loop itself ([LockstepManager.cs:284-324](../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L284-L324))
is sound: commands are buffered locally, stamped for `currentTick + 2`, sorted by
`(PlayerIndex, CommandIndex)` before execution, and every peer waits on every
other human's confirmation before advancing. The bones are right.

---

## 2. The decision that governs everything else

Two ways to make two machines agree:

**A. Deterministic lockstep** — every peer runs the same simulation from the same
inputs and trusts it to land in the same place. Bandwidth is a few hundred bytes
a second regardless of army size. A single nondeterminism anywhere forks the
match. This is what the repo is built for.

**B. Authoritative host** — the host simulates, clients render what they are
told. Immune to nondeterminism, but needs state replication, interpolation, and
a client-prediction story for responsiveness. It is a different game
architecture, not a patch.

**Take A.** For a DOTS RTS with 500-1000 entities and a lockstep skeleton already
in place, B would mean rebuilding the presentation layer around replicated state
and inventing a bandwidth budget the current design doesn't need. The remaining
work on A is a finite list of concrete bugs. The rest of this document assumes A.

---

## 3. Class A — the simulation is not deterministic

### A1. The fixed-step path cannot be switched on

`DeterministicLockstep` is read in six places and written in none. There is no
lobby toggle, no launch flag, no config entry. Until it can be turned on, every
match is loose lockstep and **§3 is unfixable by definition**.

### A2. Turning it on today gives you a 10 fps-looking game

This is the one nobody has written down. Under the fixed-step rate manager the
`SimulationSystemGroup` runs **once per lockstep tick — ten times a second**.
Views are hard-snapped to ECS transforms with no interpolation
(`PresentationSpawnSystem` sets `go.transform.position = pos` directly; there is
no `Lerp` anywhere in the sync path). So every unit on screen would move in
100 ms jumps.

Two ways out, and one of them is needed before the flag is worth flipping:

- **Raise the tick rate** to 20-30 Hz. Cheapest change, costs bandwidth and CPU
  linearly, and 30 Hz snapping is close to invisible for RTS-scale movement.
- **Interpolate the views** — keep the sim at 10 Hz and have the presentation
  layer lerp each view between the last two sim transforms. More work, better
  result, and it decouples visual smoothness from tick rate permanently.

Recommended: do both eventually; ship the tick-rate bump first (it is a
constant), add interpolation when the sim is otherwise stable.

### A3. Both peers run the AI, and the client runs it twice

`SimpleAISystem` — which per the AI notes owns essentially all AI behaviour —
has **no host gate in any of its nine partials**. Only three AI systems gate
(`AIAlanthorEndgameSystem`, `AIFeraldisEndgameSystem`, `AIBuildingUpgradeSystem`,
each `if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;`).

On a client, then:

1. The client's own `SimpleAISystem` decides to train a unit and calls
   `CommandRouter.IssueTrain(..., CommandSource.AI)`.
2. `ShouldQueueForLockstep(AI)` returns `lockstep.IsHost` — **false on a client**
   ([CommandRouter.cs:1463](../Assets/Scripts/Core/Commands/CommandRouter.cs#L1463)) — so
   it falls through to the direct-execution branch and applies **locally, now**.
3. The host's AI made its own decision at its own frame timing, queued it, and it
   arrives a tick later — and the client applies it **again**.

So AI orders are double-applied on clients, and the two AI brains have already
forked anyway: `SimpleAISystem._rngState` is seeded from `SpawnSeed` and then
advanced once per call, so a single extra call on one peer permanently offsets
the stream ([SimpleAISystem.cs:51](../Assets/Scripts/Systems/AI/SimpleAISystem.cs#L51)).

**Fix now:** host-gate every AI system, exactly as the three endgame systems
already do. Small, mechanical, high value.

**Fix properly, later:** once the sim is deterministic, the AI should run
identically on every peer and replicate *nothing* — that is how lockstep RTS
normally handle AI, and it removes AI commands from the wire entirely. It needs
the AI itself to be clean first (see A4).

### A4. Wall-clock time inside the simulation

Under the fixed-step driver, `SystemAPI.Time.DeltaTime` becomes the fixed
timestep — 106 files read it and all of them become deterministic for free.
These do not:

| Where | What |
|---|---|
| [VictoryConditionSystem.cs:69,104,106,178,437](../Assets/Scripts/Systems/Core/VictoryConditionSystem.cs#L69) | `Time.time` / `Time.deltaTime` for grace period, cadence and match clock |
| [AIPivotalReserve.cs:88](../Assets/Scripts/Systems/AI/AIPivotalReserve.cs#L88) | `UnityEngine.Time.time` gating AI savings decisions |
| [FogOfWarSystem.cs:119-120](../Assets/Scripts/Systems/World/FogOfWarSystem.cs#L119-L120) | `Time.unscaledTime` throttle — fog feeds AI intel, so this is simulation, not presentation |

Everything else that reads `Time.time` / `Time.deltaTime` is in
`Presentation/` or a `*Visual.cs` and is correctly cosmetic.

`UnityEngine.Random` appears only in building visuals and particle effects —
clean. RNG hygiene in the simulation is genuinely good: world gen, veil growth,
Border AI, blood curse, veilstone mining and `SimpleAISystem` all seed from
`GameSettings.SpawnSeed`.

### A5. One-shot init systems mutate simulation state outside the tick

`NavGridBootstrapSystem`, `TerrainCostBakeSystem` and
`TraversalProfileBootstrapSystem` sit in `InitializationSystemGroup`, which the
fixed-step rate manager does **not** govern — it only replaces the
`SimulationSystemGroup` rate manager. They write `NavCostField`, which is
simulation state that pathing reads.

They are one-shot, so the hazard is *when* they land: the terrain bake waits on a
coroutine-built `PassabilityGrid`, so on a slower-loading peer it can complete
several ticks later than on the other. Until it does, that peer's units path over
water. (The bake now retries rather than latching a bad result — see the
2026-08-15 changelog — which helps correctness but does not pin the *tick* it
lands on.)

**Fix:** gate tick 0 on "world fully initialised" — extend the existing
`HoldSimulationUntilPeersReady` barrier to also require the nav bake, rather than
only the first `TICK` from each peer.

### A6. Parallel jobs writing a shared array

Only eight parallel jobs exist, and seven are per-entity writes (safe).
The exception is `CostFieldStampSystem`, which `ScheduleParallel`s six stamp
passes into the **shared** `field.Cost` array
([CostFieldStampSystem.cs:179-276](../Assets/Scripts/Systems/Navigation/CostFieldStampSystem.cs#L179)).
Where two footprints touch the same cell the winning write depends on thread
scheduling. Most stamps write the same "impassable" constant so it is usually
benign, but "usually" is not a determinism argument.

**Fix:** make the stamps a min/max reduce (order-independent by construction), or
run them single-threaded when the determinism flag is on. The nav stack already
has this shape of switch — `GoalFlowFieldSystem` takes a synchronous path under
`DeterministicLockstep` for exactly this reason.

### A7. Runtime-generated terrain

TwinSpans and the other generated maps ship a **baked** `TerrainData` asset —
byte-identical on both peers, so `Terrain.SampleHeight` is deterministic. MapMagic
maps generate their `TerrainData` at runtime on a threaded, async pass. If two
peers' heightmaps differ by one bit, every height sample, slope test and
passability cell differs and nothing else matters.

**Fix:** restrict multiplayer to maps with a baked `TerrainData`, and have the
lobby refuse the rest until proven.

### A8. Build identity is never checked

Nothing verifies the two peers are running the same binary, the same Burst
setting, or the same tech-tree data. A host on 0.0.3 and a client on 0.0.2 will
connect happily and diverge on the first cost lookup.

**Fix:** a build hash + data hash in `TWB_JOIN` / `TWB_ACCEPT`; refuse mismatches
with a clear message. Cheap, and it turns a whole class of confusing desyncs into
a lobby error.

---

## 4. Class B — state that never crosses the wire

### B1. Match settings the client never receives

`TWB_START` carries `gamePort | seed | lockstepPort | borderEnabled | mapScene`
([MultiplayerPanel.cs:1147-1159](../Assets/Scripts/UI/Menus/Panels/MultiplayerPanel.cs#L1147-L1159)),
and `TWB_LOBBY` adds layout, map size, fog, and per-slot type/name/difficulty/
colour/team/start.

Never sent, and read from process-local statics that may hold whatever the last
single-player menu left there:

| Setting | Consequence if it differs |
|---|---|
| `StartAge` | One player starts in Age 1, the other in Age 0 |
| `StartCulture` | Different tech trees from tick 0 |
| `MaxStartingResources` | Different banks from tick 0 |
| `PathfindingCellSize` | **Different nav grid resolutions** — total divergence |
| `SpawnEdgeBuffer*`, `SpawnMinSeparation` | Different start placement on non-authored maps |
| `AIStrategy` (per slot) | Only matters once AI is host-gated; harmless after A3 |

This is the cheapest high-severity fix in the document: extend the `TWB_START`
payload and set them all in `StartAsClient`.

### B2. Mutations with no replication path — the audit's open F-list

The audit's fixes held, but the **UI redesign moved the offenders into the new
uGUI binders**, so the line references are stale while the bugs are live. Every
one of these writes ECS directly instead of going through `CommandRouter`, and
none has a `LockstepCommandType` (the enum still tops out at `SectAdopt = 29`):

| Item | Now lives in |
|---|---|
| Sect active powers (AoE damage, spawned strikes) | `ReligionPanelBinder.cs:663,688` |
| Wall hub / segment placement | `BuildCommandPannel.cs:862,910` (`SpawnFirstWallHub` / `SpawnExtendedWallHub`) |
| Wall upgrade to Tower/Gate | `ActionsPanelBinder.cs:562` |
| Reliquary abilities | `ActionsPanelBinder.cs:607,621`, `ActionsPanelPrefabBinder.cs:770,800` |
| Fiendstone Keep wing | `ActionsPanelPrefabBinder.cs:825` |
| Vault deposit / withdraw | `ActionsPanelBinder.cs:758-815` |
| Unit promotion, Bazaar pack/unpack | `EntityActionPanel` equivalents |
| Shift-queued waypoints | `RTSInputManager.cs:948-950` |

Each is the same shape of fix as the ones already done (`AgeUp`, `TempleUpgrade`,
`SectAdopt`): add a command type, split the helper into *validate + spend on the
issuer* and *apply on every peer*, route all call sites through the router.

### B3. Resource banks are per-peer by design — FIXED (partially)

`FactionResources` is an ECS component on a bank entity (`FactionEconomy` is the
static *helper*, not the storage). Those bank entities carry no
`NetworkedEntity`, so the old checksum's entity scan never saw them.
Train/Research/Place still spend on the issuing peer only — that is the
established model and it is fine as long as every purchase replicates — but the
checksum now hashes all eight banks, so an economy that has quietly diverged is
caught within a second instead of surfacing much later as "how can they afford
that".

### B4. `SetRally` drops its target entity

The lockstep payload omits `TargetEntity`, so rally-onto-a-resource silently
degrades to a plain position for everyone in multiplayer. Still open.

---

## 5. Class C — lag, loss and failure

On a LAN, raw latency is not the problem (sub-millisecond RTT, ~0% loss). The
problems are what happens in the rare bad case, and how little the game tells you.

### C1. Input delay is a hard-coded constant

`INPUT_DELAY_TICKS = 2` at 10 Hz = **200 ms between click and effect**, on a link
with 0.2 ms of latency. `PING` is never sent by anything, so `RemotePlayer.Latency`
is dead code and no adaptive scheme is possible today.

**Fix:** send `PING` on a timer, populate `Latency`, and pick the delay from
measured RTT (1 tick on LAN). Combined with a 20-30 Hz tick rate this takes
command latency from 200 ms to ~35-50 ms, which is the difference between "feels
laggy" and "feels local".

### C2. No reliability layer

`TICK` datagrams have no sequence, ack or retransmit. The mitigation in place —
re-sending the last two non-empty payloads before each broadcast — self-heals a
*single* lost datagram inside the input-delay window, and nothing beyond that. A
50-100 command tick serialises to several KB of UTF-8, which fragments over a
1500-byte MTU; one lost fragment discards the whole datagram, and command bursts
during big fights are exactly when that happens.

**Fix, in order of value:** bound datagram size by chunking large ticks; move to a
binary protocol (roughly 4x smaller, and it removes the per-tick
`StringBuilder`/`Format("R")`/`Split` garbage); then a real ack + retransmit
window if any of it still shows up on LAN.

### C3. A peer that leaves freezes the other forever

There is no timeout, no heartbeat during the match, no disconnect detection, and
no abort path. If one player alt-F4s, `CanAdvanceTick` never returns true again on
the other machine, and — because the sim is frame-driven in loose mode — the world
keeps animating while every order is silently ignored. The player sees a game that
is alive but deaf, with no explanation.

Under `DeterministicLockstep` it is more honest (everything stops) but equally
unexplained.

**Fix:** per-peer last-heard-from timestamp → "Waiting for <player>…" overlay after
~1 s → offer drop/abort after ~10 s.

### C4. Nothing in-game reports network state

No ping display, no tick-lag indicator, no waiting-for-player overlay, no desync
banner. `DesyncDetected` and `DesyncTick` are public properties on
`LockstepManager` and **no UI reads them**. On desync in deterministic mode the
game sets `_isSimulationRunning = false` and writes one `Debug.LogError` — the
player just watches everything stop.

### C5. The checksum is too weak to trust

`ComputeGameStateChecksum` XORs per-entity terms of `NetworkId` and health
([LockstepManager.cs:1045-1077](../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L1045-L1077)).
XOR is order-independent, which is the right instinct, but it also means two
entities swapping health values cancel out completely. It excludes positions (by
design, because loose lockstep drifts), and also resources, research, ages,
cooldowns and construction progress.

**Fix, once the sim is fixed-step:** hash a stream sorted by `NetworkId`, with a
real mixing function (FNV-1a or xxHash), covering position, health, faction,
resources and research. Then a mismatch means something, and it means it within
3 s rather than never.

### C6. Diagnostics when it does go wrong

The alpha already writes per-match logs beside the exe. On a checksum mismatch,
dump both peers' per-entity state to that folder — that is the difference between
"desync at tick 900" and knowing which entity forked. A deterministic replay from
the recorded command stream would be better still, and is nearly free once the
sim is deterministic: the command log *is* the replay.

---

## 6. Work plan — status

### P0 — required for a playable 1v1

1. **DONE — `DeterministicLockstep` defaults ON** and every multiplayer entry
   point sets it explicitly, so a stale value from an earlier session cannot
   decide whether a match is deterministic.
2. **DONE — tick rate is 30 Hz and views interpolate.** `LockstepTiming` owns
   the rate; `PresentationSpawnSystem` plays each view along a segment between
   the last two simulated transforms, with a teleport guard, and does nothing at
   all when the simulation free-runs per frame.
3. **DONE — every AI system is host-gated** through
   `GameSettings.ShouldRunAIBrains()`, including `SimpleAISystem`, `IntelSystem`
   and `ScoutDirectorSystem`, which had no gate at all.
4. **DONE — `MatchSettingsSync`** carries every world-shaping setting on
   `TWB_START` as one self-describing blob, and the client adopts it instead of
   its own leftovers. Adding a setting is now a one-line change in one file.
5. **DONE — tick 0 waits for the world.** The tick loop does not start until the
   passability mask is built and the nav cost field is baked, with a 30 s
   backstop that logs loudly rather than hanging.
6. **DONE — build handshake.** `TWB_JOIN` carries a build fingerprint; a
   mismatch is refused at the door with `TWB_REJECT` and a readable reason.

### P1 — determinism hygiene

7. **DONE — `SimClock`.** Victory timing and AI reserve budgeting read simulated
   seconds instead of `Time.time`. `FogOfWarSystem`'s throttle stays on
   wall-clock deliberately: it drives view visibility, and the AI that consumed
   its intel is now host-only.
8. **DONE — cost-field stamps run single-threaded** under the fixed step, so two
   overlapping footprints cannot land in thread-scheduling order.
9. **DONE (6 of 9) — the unreplicated mutations.** Sect active powers, reliquary
   abilities, wall upgrades, keep wings, unit promotion and shift-queued
   waypoints now route through `CommandRouter` with command types 30-35.
   **Still open:** vault deposit/withdraw, bazaar pack/unpack, and wall
   hub/segment placement — the first two are economy-local, the third needs
   `BuilderCommandPanel`'s hub spawn split into validate/apply first.
10. **DONE — runtime-terrain maps are refused for multiplayer.** All three
    shipping maps bake their `TerrainData`, so this is a guard rather than a
    filter today.
11. **DONE — the checksum is worth acting on.** Per-entity FNV-1a hashes summed
    (order-independent without being self-cancelling), covering position,
    health, faction and all eight faction banks.

### P2 — feel and robustness

12. **DONE — latency is measured and the input delay follows it.** `PING` is now
    actually sent; the delay only ever rises mid-match, because lowering it
    would stamp a command for a tick that has already executed.
13. **DONE — disconnect detection.** Per-peer last-heard-from, a "waiting for"
    state after 1 s and a lost-peer state after 15 s.
14. **DONE — `NetworkStatusOverlay`.** Ping and input delay in the corner; a
    banner for waiting / connection lost / desync.
15. **DONE — datagram chunking.** Tick payloads are split to stay under the MTU,
    so a lost fragment costs one chunk rather than the whole tick. The protocol
    is still UTF-8 text; a binary encoding is still worth doing for the GC
    churn, but the correctness problem is closed.
16. **DONE — desync dumps.** Both peers write a `NetworkId`-sorted state file
    into the match log folder; diffing them names the entity that forked.

### Found while implementing

17. **DONE — `CommandIndex` was never serialised.** Every received command
    arrived with index 0, so the "sort by (PlayerIndex, CommandIndex)" that
    orders a tick had nothing to order one player's commands by and fell back on
    datagram arrival order — and `List.Sort` is not stable. Two commands issued
    in the same tick could execute in opposite orders on the two peers. It is
    now the first field on the wire and the sort is a total order.
18. **DONE — the fixed-step driver was never uninstalled.** Its statics survived
    a match, so a single-player game started after a multiplayer one inherited
    them.
19. **DONE — catch-up is bounded** at 8 ticks per frame. Without it a peer
    returning from a stall ran every missed tick in one frame, and under the
    fixed step each of those is a full simulation update.
20. **DONE — `_checksums` is pruned.** It grew by one entry per sync for the life
    of the match.

### Still open

- The three mutations in item 9.
- Binary wire protocol (item 15) — correctness is fixed, the GC churn is not.
- Deterministic replay from the command stream. Nearly free now that the
  simulation is deterministic: the command log *is* the replay.
- Running the AI identically on every peer and replicating nothing (the "fix
  properly" half of A3).

---

## 7. Testing it with two instances

### Getting two copies running

Any of these works; the logging handles all three:

| How | Notes |
|---|---|
| **Unity 6 Multiplayer Play Mode** virtual players | Same project folder — both processes resolve the *same* `logs/`, which is why log files are instance-discriminated (below) |
| **ParrelSync** clone | Separate project folder, so a separate `logs/` each |
| **Two copies of the built exe** | Same folder = shared `logs/`, different folders = one each |

The one thing to get right regardless: **the host must pick a map with baked
terrain** (all three shipping maps qualify) and both instances must be the same
build — the lobby now refuses a mismatch at join with a message saying so.

### Where the logs land

Each match writes a folder under `logs/`, and in multiplayer the folder name
says which peer wrote it:

```
logs/
  2026-08-15_14-02-11_TwinSpans_host/
    Console.log        per-peer: stalls, latency, socket trouble, warnings
    Lockstep.log       THE ONE THAT MATTERS — see below
    Summary.txt
    Desync_tick900_p0.log   (only if it desynced)
  2026-08-15_14-02-13_TwinSpans_client1/
    Console-2.log      note the suffix — two instances, one folder, no clobbering
    Lockstep.log
    ...
```

Instances sharing a `logs/` folder claim a slot by holding a lock file
(`.instance0.lock`, `.instance1.lock`); the first keeps the plain filenames and
the rest get a `-2`, `-3` suffix. Before this, two virtual players both opened
`Console.log`, the second failed, and that instance logged nothing all session —
silently, and it was usually the instance you needed.

### Reading `Lockstep.log`

It is written so that **two peers in sync produce identical files**. Everything
in it comes from state the peers are supposed to agree on and nothing else — no
wall-clock times, no frame counts, no latency, no addresses, because all of
those differ legitimately between two healthy machines and any one of them in
the file would drown the signal.

```
# Lockstep log — player 0 (host)
# build      0.0.3 (protocol 2)  fingerprint 4C2A9F13
# map        TwinSpans  seed 60486
# rules      age=Age0 culture=1 maxres=False fog=True curse=True
# sim        30 Hz  cell=1  deterministic=True
#
evt  tick=000000 world ready — tick 0 begins
tick=000000 cmds=0
tick=000030 cmds=1 | p0#0 LayeredMove e=1000123 @(12.500,0.000,-40.250)
sum  tick=000030 = 0xA41B7C05  entities=214
tick=000060 cmds=2 | p0#0 Train e=1000045 id=Worker | p1#0 PlaceBuilding e=1 id=Hut @(60.000,12.000,110.000)
sum  tick=000060 = 0x99E2D330  entities=216
```

Then diff the two:

```
fc   logs\..._host\Lockstep.log  logs\..._client1\Lockstep.log     (Windows)
diff logs/..._host/Lockstep.log  logs/..._client1/Lockstep.log     (git bash)
```

**The first differing line is the fork.** Everything after it is consequence.
And the *kind* of line that differs first tells you which half of the stack to
look at:

| First difference | Meaning |
|---|---|
| a `tick=` line — one peer has a command the other lacks | **Replication bug.** Something mutated state without going through `CommandRouter`, or a datagram was lost beyond the resend window |
| a `tick=` line — same commands, different order | Ordering bug. `CommandIndex` should make this impossible; if you see it, that is the bug |
| a `sum=` line, with identical commands above it | **Determinism bug.** Both peers ran the same input and got different state — the simulation itself forked |
| the header | The two peers disagreed about the match before it started. Compare `fingerprint`, `seed`, `rules`, `cell` |

Empty ticks are logged every 30th only, to keep a 30-minute match to a readable
size while still keeping the two files aligned on a coarse grid — if the tick
COUNTS ever diverge, that shows up as misaligned `sum=` lines.

**First live catch (2026-08-16, first two-editor test):** instant desync at the
first checksum — `sum tick=000030` host `entities=748`, client `entities=744`,
zero commands, identical headers. The instrument worked exactly as designed and
the ID histogram named the culprit: the frame-paced spawn coroutine
(`SpawnDelayHelper.WaitForTerrainAndSpawn` — factions, then iron, then
veilstone, then wells, one `yield` apart) was still populating the world while
ticks ran, so each peer's deposits/wells got NetworkIds stamped with whatever
LOCAL tick they landed on (partitions 21/24/32 on the slower peer). Fixed by
gating tick 0 on the new `SpawnDelayHelper.MapPopulated` latch in
`LockstepManager.IsWorldReady()` (bail-out raised 30s → 120s to cover MapMagic
generation): the whole population now happens in the ID generator's
bootstrap-sequential mode before the clock starts, and the tick-0 checksum sees
the complete world on both peers.

**Second catch (same day):** with the gate in, the very next match desynced at
tick 0 with **identical entity counts** (748 = 748) but different sums — same
entities, different id↔position pairing. Cause: `MapMarkerRegistry.Refresh`
sorted only the player-start list; the iron/veilstone/veilsteel/border/blight
lists stayed in `FindObjectsByType(SortMode.None)` order, which differs per
process, so each peer consumed markers — and therefore sequential bootstrap
NetworkIds — in a different order. Fixed by sorting every marker list with a
canonical (name, x, z, y) comparison. Also fixed the evidence gap: a SYNC that
arrives before the local checksum for that tick is now stashed and compared
when the local one lands, so BOTH peers detect a mismatch and write
`Desync_tickN_pP.log` — one dump alone cannot show which entity forked.

### The desync dump

If the checksums disagree, both peers write
`Desync_tick<N>_p<player>.log` into their match folder: every networked entity
sorted by `NetworkId`, with faction, health and position, plus all eight faction
banks. Diff the two and the differing line names the entity that forked.

`Lockstep.log` tells you *when*; the desync dump tells you *what*.

---

## 8. How to know it works


A desync test is worth more than any amount of reading:

1. Two instances, same machine, LAN loopback, `DeterministicLockstep` on.
2. Fixed seed, fixed map, both players idle, no AI. Run 10 minutes. Checksums must
   match at every 3-second sync. **If idle desyncs, nothing else is worth testing.**
3. Add one AI. Run again — this catches A3 immediately.
4. Both players issue orders, no combat. Then a scripted fight (the biggest
   nondeterminism amplifier: targeting order, death order, ID allocation).
5. Only then, a real match.

Log the checksum every sync tick on both peers to their match log folders and diff
the files. The first mismatched tick is the whole answer, and step 2 through 5
narrows *which subsystem* forked before you have to go entity-by-entity.

---

## Appendix — quick reference

| Constant | Value | File |
|---|---|---|
| Tick rate | 10 /s | `LockstepManager.TICKS_PER_SECOND` |
| Input delay | 2 ticks (200 ms) | `LockstepManager.INPUT_DELAY_TICKS` |
| Tick buffer | 60 ticks | `LockstepManager.MAX_TICK_BUFFER` |
| Sync interval | 30 ticks (3 s) | `LockstepManager.SYNC_CHECK_INTERVAL` |
| Resend window | 2 payloads | `LockstepManager.RESEND_HISTORY` |
| Discovery port | UDP 47515 broadcast (all interfaces + `TWB_FIND` probe/reply) | `MultiplayerPanel.BROADCAST_PORT` |
| Lobby control | host game port (7979) | `MultiplayerPanel._port` |
| Lockstep port | host `+1`, client `+1+slotIndex` | `MultiplayerPanel.StartAsClient` |
| Bootstrap ID range | 1 .. 999,999 | `NetworkIdGenerator.BOOTSTRAP_RESERVE` |
| IDs per tick | 10,000 | `NetworkIdGenerator.SLOTS_PER_TICK` |
