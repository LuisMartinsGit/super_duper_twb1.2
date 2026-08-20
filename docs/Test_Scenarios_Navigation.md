# Navigation Test Scenarios — Tester Guide

Seven scenarios cover the seven milestones of the The Border Navigation
Stack (task-112). Each one isolates the systems added in its milestone
so a single visible failure points to a single subsystem. Launch from the
Main Menu → Scenarios list.

| # | Menu label                                   | Milestone | Systems exercised        |
|---|----------------------------------------------|-----------|--------------------------|
| 1 | Phase 1 Nav Test (1 unit flat grid)          | M1        | S1 cost field, S6 flow follow, LOS pass |
| 2 | Phase 2 Nav Test (300 swords flow + steering) | M2       | + S2 spatial hash, S7 steering, arrival decay |
| 3 | Phase 3 Nav Test (1 unit 512x512 SW→NE)      | M3        | + S3 portal graph, S4 abstract A*, S5 segmented flow |
| 4 | Phase 4 Nav Test (50 swords + wall place/destroy) | M4   | + S1 dirty-tile, S3 incremental rebuild, S5 cache invalidation |
| 5 | Phase 5 Nav Test (wall ring + 10 Blue + 10 Red) | M5     | + Rampart layer, climb/gate portals, S10 layer transition |
| 6 | Phase 6 Nav Test (formations + mixed footprints) | M6    | + S8 formations, S5 extended flow, S9 request budget |
| 7 | Phase 7 Nav Test (determinism replay 100 units) | M7     | + DeterminismReplaySystem, BurstAttributeAudit |

---

## Phase 1 — One unit on open ground

**What it tests.** The baseline: cost field is built, flow direction is
computed, the unit reads the flow and walks. Plus the M3+M5 LOS-to-goal
pass that makes open-ground movement a straight line.

**Setup.**
- 1 Blue Swordsman spawns at world `(4, _, 4)`.
- Goal is `(60, _, 60)` — a clean diagonal.
- No obstacles.

**Tester actions.** None required. Optionally: select the unit and
right-click somewhere else to re-issue a move command — it should
respond immediately and walk straight to the new point.

**PASS.**
- Unit walks a visible **straight diagonal line** from spawn to goal.
- Reaches within ~1 m of `(60, _, 60)` in under ~10 s.
- Stops cleanly at the destination.
- On a manual re-click, accepts the new order without delay.

**FAIL.**
- Visible **zig-zag** or stair-step path (means the 256-bin flow gradient
  isn't kicking in — check `FlowSegmentSystem.IntegrateTileJob`).
- Unit drifts or stops short (means LOS pass isn't firing — check
  `SampleFlowFromCacheJob.HasLineOfSight`).
- Re-click is ignored for several seconds (means
  `MoveCommandHelper.Execute` isn't clearing stale `NavPathResult`).

---

## Phase 2 — 300-unit crowd avoidance

**What it tests.** Spatial hash + locked-order steering forces. Hundreds
of units sharing a goal must reach it without piling on top of each
other.

**Setup.**
- 300 Blue Swordsmen in a tightly-packed 10×30 block centred at
  `(-30, _, 0)`, spaced 1.5 m apart.
- All commanded to the same goal: `(30, _, 0)`.

**Tester actions.** None required. The move command is auto-issued at
spawn. Optionally: pause, select an arbitrary subset, right-click a
fresh point and watch the subset peel off cleanly.

**PASS.**
- Crowd marches east as a coherent stream — no orbital / vortex
  motion, no random sideways drift.
- Units reach the goal area in waves over ~15 s.
- At the goal point, units **form a ring / cluster** without overlap —
  the arrival-decay term causes flow to fade and separation to
  dominate inside ~3.5 m of the destination.
- A re-commanded subset peels off without disturbing the rest.

**FAIL.**
- Units **orbit** each other or spiral around the goal (steering
  weights regressed — flow should dominate at `FlowWeight = 3.0`).
- Units **stack on top** of each other at the goal (arrival decay
  isn't firing — check `DestLookup` in `AccumulateSteeringForcesJob`).
- Crowd ignores movement orders mid-transit (stale `NavPathResult` —
  see Phase 1 fail-mode).

---

## Phase 3 — Hierarchical pathing across the 512² grid

**What it tests.** Portal-graph build + abstract A* + segmented flow
cache. Long path that has to traverse many tiles is solved without
allocating a whole-map flow field.

**Setup.**
- 1 Blue Swordsman spawns at world `(-248, _, -248)` (~cell `(8, 8)`).
- Goal is `(+248, _, +248)` (~cell `(504, 504)`).
- 512×512 grid, 16×16 tiles → ~32 tiles diagonally. Unit must cross
  ~31 portals.

**Tester actions.** None required.

**PASS.**
- Unit walks **a straight diagonal across the entire map** — the LOS
  pass over open ground beats portal-by-portal traversal.
- Arrives within ~1 m of the NE corner.
- No per-frame stalls or stutters (per-tile flow is cached + the budget
  spreads work across ticks).

**FAIL.**
- Visible **zig-zag through tile-boundary midpoints** (LOS pass not
  firing for this distance — check that
  `SampleFlowFromCacheJob.HasLineOfSight` returns true for the
  start/goal pair).
- Unit appears to "hop" across portal points instead of walking
  smoothly between them.
- Long pause before the unit starts moving (A* request not being
  serviced — check `AbstractPathfinderSystem.MaxRequestsPerTick`).
- Editor freeze when issuing the move (whole-map integration leaked
  back in — should be per-tile only).

---

## Phase 4 — Dynamic world: place / destroy walls mid-run

**What it tests.** `BuildingCostStampSystem` marks tiles dirty when
walls are placed or destroyed. `IncrementalPortalRebuildSystem`
rebuilds only those tiles' portals + edges. `FlowSegmentSystem`
invalidates only the slabs touching dirty tiles.

**Setup.**
- 50 Blue Swordsmen spawn in a formation south of origin.
- All issued a move command to a point north of origin.
- After ~5 sim seconds a wall block spawns between them and the goal.
- After ~10 sim seconds the wall is removed.

**Tester actions.** None required for the scripted sequence. Optionally
the build button can be used to drop additional walls in arbitrary
spots; the units should re-route around them mid-transit.

**PASS.**
- Units march north, hit the wall placement, **smoothly re-route**
  around it (no per-frame stutter).
- When the wall is destroyed, units that were detouring straighten
  out and resume the direct path.
- No console errors. No "stale" units stuck mid-detour after the wall
  is removed.

**FAIL.**
- Units **walk into the wall** and pile up against it (dirty tile not
  flagged — check `BuildingCostStampSystem.OnUpdate`).
- All units **freeze** for 1+ s when the wall is placed (the rebuild is
  doing a full-map pass instead of incremental — check the dirty-set
  drain in `IncrementalPortalRebuildSystem`).
- Path stays detoured even after the wall is gone (cache invalidation
  is missing — check `FlowSegmentSystem.InvalidateTile`).

---

## Phase 5 — Walls, gates, climb portals

**What it tests.** Two-layer cost field (Ground + Rampart). Climb
portals at stairs. Gate portals at gatehouses, with separate
owner-gated bits for the ground (inside↔outside) and rampart
(gatehouse roof) layers. `LayerTransitionSystem` (S10) lerps units
between layers as they pass through a portal.

**Setup.**
- A square Alanthor wall ring at origin: 4 hub corners, 4 segments,
  one stair on the west segment, one gatehouse on the south segment.
- 10 Blue Swordsmen spawn south of the ring, commanded north.
- 10 Red Swordsmen spawn south-east of the ring, also commanded north.
- The gate owner is Blue.

**Tester actions.** None required for the scripted sequence. Optional:
once units are positioned, manually pause and inspect the
`NavLayerIndex` component on individual entities to verify climbers
got `Layer = 1`.

**PASS.**
- Blue units approach the south gate. Within ~6 m the gate
  proximity-opens and Blue units **pass through** to the inside, then
  some climb the stair and **patrol the rampart**.
- Red units approach the same gate. The gate **rejects** them
  (Red ≠ owner) — they path around the wall to find another way in.
- Blue units on the rampart sit at Y ≈ 4 m (`DeckY`). Ground units sit
  at terrain height.
- Selecting a unit on the rampart shows `NavLayerIndex.Layer == 1`.
  Ground units show `Layer == 0`.

**FAIL.**
- Blue units **walk past** the gate and around the wall (gate isn't
  registering — check `WallGateRegistrationSystem`).
- Red units **pass through** the gate (owner gating broken — check
  `AbstractPathfinder.IsPortalAdmissibleForA` consuming
  `PortalOwnerBitsMirror`).
- Units on the rampart **clip** through the wall back to ground
  layer mid-traverse (`LayerTransitionSystem` cancels partway).
- Units **teleport** instantly between layers instead of lerping
  along the climb portal.

---

## Phase 6 — Formations, mixed footprints, request budget

**What it tests.** `FormationLeaderNavSystem` (S8) drives the leader
through the flow; followers occupy formation slots when they have
LOS, fall back to flow when blocked. `ExtendedFlowSystem` (S5
extended) generates flow with the most-restrictive profile in the
formation so large-footprint units don't get stranded.
`NavRequestSchedulerSystem` (S9) coalesces duplicate requests and
budgets release across ticks.

**Setup.**
- 4 battalions, 15 units each (60 units total): 2× Swordsman
  (small footprint), 2× Cataphract (large footprint).
- Spawned on the west side of the map.
- Two rows of 5 obstacle blocks at `z = ±14` form a chokepoint at
  `z ≈ 0`.
- All 4 battalions commanded to the east side of the map.

**Tester actions.** None required for the scripted sequence. Optional:
mid-transit, drop more obstacle blocks via the build button and watch
the formation re-route as a unit.

**PASS.**
- All 4 battalions **converge on the gap** and squeeze through in
  formation. Cataphracts (the large-footprint constraint) don't get
  stuck on the gap edges — the extended flow accounts for their
  footprint.
- Units inside a battalion **stay roughly in slot** while in open
  ground; fall back to the leader's flow when an obstacle interrupts
  the slot line-of-sight.
- No per-frame stall when the mass-move is issued — the scheduler's
  16-request-per-tick budget paces the A* work.
- All 4 leaders share **one A* solve** (same goal + profile).

**FAIL.**
- Cataphracts **wedge** at the gap edges while Swordsmen pass through
  (extended flow isn't using the dominant profile — check
  `FormationProfileAggregate`).
- Battalions **lose cohesion** and Swordsmen race ahead of
  Cataphracts (slot-following branch isn't taking over from flow when
  LOS is clear).
- Sim **stalls** for several frames when the mass-move command fires
  (scheduler not paginating — check `MaxRequestsPerTick`).

---

## Phase 7 — Determinism replay

**What it tests.** `DeterminismReplaySystem` records per-tick unit
position snapshots (in integer millimetres) during a "Record" pass,
then compares the same scripted scenario byte-for-byte during a
"Replay" pass. The whole nav stack must produce identical positions
twice in a row.

**Setup.**
- 100 Blue Swordsmen in a 10×10 grid centred near `(-10, _, -10)`.
- A `Phase7ScriptedCommands` queue issues 6 scripted move commands
  over the first ~25 sim ticks.

**Tester actions.**
1. Run the scenario. Let it tick for at least 30 sim seconds. The
   `DeterminismReplayLog` accumulates one snapshot per unit per tick.
2. Flip `GameSettings.NavReplayMode` from `Record` → `Replay` (via
   debug menu, the inspector on the singleton, or a one-line editor
   script).
3. Restart the scenario — same script runs again, but now the system
   asserts each snapshot against the recorded log.

**PASS.**
- Inspecting the `DeterminismReplayLog` after Record:
  `HasData != 0`, `CurrentTick >= 30`, `Log.Length == 30 × 100 = 3000`.
- After Replay: `DivergenceCount == 0` and no `Debug.LogError` lines
  about position mismatch.
- All 100 units follow the same paths visually in both runs.
- All 7 `Window > General > Test Runner > NavStack > M7` tests pass
  (`BurstAttributeAudit`, `DeterminismIntegrationSweep`,
  `DeterminismAStarRepeat`, `DeterminismSpatialHashRepeat`).

**FAIL.**
- `DivergenceCount > 0` after Replay — locate the first divergence in
  the Console; the divergence tick + entity index point at the system
  introducing non-determinism (most often a stray `Mathf.*` /
  `Time.deltaTime` / `UnityEngine.Random` slipped into a `[BurstCompile]`
  job).
- Replay log is empty after Record — `DeterminismReplaySystem` isn't
  in `SimulationSystemGroup OrderLast`, or `NavReplayMode != Record`.
- `BurstAttributeAuditTest` red — a new job in
  `TheWaningBorder.Systems.Navigation` is missing `[BurstCompile]`.

---

## Cross-cutting "definitely broken" symptoms

These should NEVER appear in any scenario; if they do, the stack
itself is in a bad state and other tests are unreliable:

- `IndexOutOfRangeException: ReadWriteBuffers are restricted...` — a
  parallel job lost its `[NativeDisableParallelForRestriction]`
  attribute or got a new field without one.
- `InvalidOperationException: ... was not declared in the EntityQuery` —
  a system's `OnUpdate` calls `ToComponentDataArray<X>` on a query
  whose `WithAll<>` doesn't list `X`.
- `Failed to resolve assembly: 'TheWaningBorder.Runtime'` — the
  runtime asmdef didn't compile (usually a missing package reference
  in `TheWaningBorder.Runtime.asmdef`).
- "There are no audio listeners in the scene" / "No cameras
  rendering" — the loaded scene name isn't registered in
  `MapRegistry.Maps`, so `GameBootstrap.OnSceneLoadedHandler` early-
  exited before running any bootstrap.
- TLS Allocator leak warnings every tick — an `OnUpdate` is throwing
  before its `Allocator.Temp` arrays dispose; the actual exception
  (whatever caused the early-return) is the real bug.
