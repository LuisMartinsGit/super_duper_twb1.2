---
deft:
  id: task-nav-stack-flowfields-112
  type: task
  status: completed
  stage: release
  phase: 7
  total_phases: 7
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: new
  mode: human-in-the-loop
  completed_at: "2026-06-01"
---

# Crystal Curse Navigation Stack -- flow-field pathing replacing NavMesh

## Context

Replace the current NavMesh-based movement (`Assets/Scripts/Systems/Movement/MovementSystem.cs`,
`NavMeshManager.cs`, `NavMeshPathRequestSystem.cs`, `NavMeshConfineSystem.cs`,
`NavMeshStaticObstacle.cs`) with a deterministic DOTS pathing stack: grid + hierarchical
portal graph + flow fields + steering, modeled on Relic's *Age of Empires IV* pathing
(Cheng, GDC 2022) over Emerson's flow-field-tiles technique.

The canonical, verbatim user prompt is `spec.md` in this directory. Treat that as the
authoritative scope -- do not paraphrase or restate it in this section. The scope agent
should read `spec.md` first.

**Why the rewrite:**

- Unity 6 deprecated `NavMeshQuery` jobs API with no replacement.
- Baked NavMesh handles toggleable, multi-layer connectivity poorly -- Alanthor's wall
  climb points, ground-level gate portals, and rampart-level gate portals across
  gatehouses cannot be expressed cleanly.
- NavMesh worker-thread float math is nondeterministic across machines, breaking
  lockstep multiplayer.

**Previous attempts (do not resurrect):**

- An earlier flow-field/A*/context-steer stack was deleted by commit `bf909bf`
  ("feat(nav) navmesh PR3: nuke legacy nav stack"). That code is gone.
- A separate .deft attempt (task ID `task-nav-stack-flowfields-112` per prior memory)
  produced M1-M4 commits (`9f0b5e4`, `4f0f5b3`, `3ae909d`, `839da80`, `be653ae`) that
  are now orphaned -- they appear in `git log --all` for `MovementSystem.cs` but no
  branch contains them. This task reuses the slug + ID since neither artifacts nor
  task directory survived.

## User Value

**Players** get the Alanthor wall fantasy that the design folder has been promising
since Age 1 -- units that actually climb stairs, patrol the parapet, and pass through
gatehouses at two levels with owner-gated friend/foe behaviour -- plus the responsive
mass-move feel of a modern RTS at scales (hundreds of units, 512x512 grids today,
1024x1024 tomorrow) the NavMesh stack cannot sustain. **Developers and multiplayer
players** get a deterministic, Burst-friendly sim that survives the Unity 6
`NavMeshQuery` deprecation, restores lockstep correctness (no more worker-thread
float drift across machines), and makes the Crystal Curse map mutation feel
genuinely dynamic without per-frame stalls or full-graph rebakes.

## Requirements

Canonical wording lives in `spec.md`. The numbered items below trace 1:1 to that
file's section 3 (acceptance criteria), section 5 (system inventory S1-S11), and
section 6 (cross-cutting constraints). System tags reference `spec.md` section 5.

### Functional Requirements

- **R1 -- Steering avoids transient obstacles (spec 3.1).** Units must steer
  around each other and around small/dynamic obstacles via S7 (steering /
  local avoidance) using S2 (spatial hash) neighbour queries. Transient
  obstacles MUST NOT be stamped into the S1 cost field or the S3 portal graph.
- **R2 -- Static-obstacle updates are incremental (spec 3.2).** Buildings,
  walls, and Crystal Curse terrain mutation update S1 at runtime; S3 rebuilds
  only the tiles flagged dirty by S1; S5 invalidates only the affected flow
  caches. No full-graph rebake. No per-frame stall.
- **R3 -- Two-layer traversal (spec 3.3).** S1 represents Ground and Rampart
  contexts; S3 connects them only at designated portals (climb points and
  gatehouse links); S10 transitions units between layers along those portals.
- **R4 -- Conditional gate portals (spec 3.4).** Gates are S3 portals whose
  passability depends on owner + open state; the ground portal (inside-outside)
  and the rampart portal (across the gatehouse roof) are independently
  toggleable; S4 consults gate state at query time; S10 enforces eligibility
  at traversal time as a backstop against stale paths.
- **R5 -- Per-unit-type traversal profile (spec 3.5).** S4 accepts a
  TraversalProfile (footprint size, allowed layers, can-climb, owner,
  terrain-cost multipliers) and uses it for portal admissibility, cost
  computation, and flow-field selection (S5 extended-flow).
- **R6 -- Architecture matches the chosen design.** Global pathing
  (S1->S3->S4->S5) is decoupled from local steering (S6->S7) per spec
  section 4; transient obstacles live ONLY in the steering layer; the portal
  graph is the only structure that expresses wall/gate connectivity.

### Non-Functional Requirements

- **R7 -- Deterministic / lockstep-safe (spec 3.6, 6).** No
  `UnityEngine.Random`, no wall-clock, no machine-dependent float ops in
  sim-affecting code. Integer costs and integer integration where possible.
  Deterministic iteration order in S1 stamping, S2 hash bucket walk, S3
  flood-fill, S4 A* tie-break, S5 integration sweep, S7 force accumulation,
  S8 slot assignment. All nav work runs inside the fixed-step deterministic
  sim group; debug viz (S11) is editor-only and outside the sim group.
- **R8 -- Burst + DOTS idiomatic.** Every system implements `ISystem`; every
  job is `IJobEntity` / `IJobParallelFor` / `IJob` Burst-compiled; no
  managed allocations in hot paths; static graph data uses
  `BlobAssetReference`; mutable per-tick data uses `NativeArray` /
  `NativeParallel*` with explicit `Allocator` choices (Persistent for the
  graph, TempJob for per-tick scratch); ownership and disposal are documented
  per allocation.
- **R9 -- No deprecated APIs.** Do not call `NavMeshQuery` or experimental
  NavMesh jobs. The Unity AI Navigation package may remain in `manifest.json`
  but nav-stack code MUST NOT depend on it.
- **R10 -- Scale targets.** Design for ~1500 simultaneously-moving units on
  a 1024x1024 grid; M3 must show the 512x512 corner-to-corner scenario
  without a full-map integration; S9 budgets per-tick request work so a
  mass-move never stalls the sim.
- **R11 -- Test coverage.** A PlayMode harness runs all 7 phase scenarios
  end-to-end and reports structured pass/fail per phase; an EditMode suite
  covers every system's invariants on hand-authored grids (see the
  `Test Script` section below).

### Out of Scope

- **OOS1 -- Resurrecting deleted nav code.** The pre-`bf909bf` flow-field /
  A* / context-steer stack and the orphan M1-M4 commits (`9f0b5e4`,
  `4f0f5b3`, `3ae909d`, `839da80`, `be653ae`) are reference patterns only.
  Do not copy their files wholesale; this task rebuilds the stack against
  the current `MovementSystem.cs` callers.
- **OOS2 -- NavMesh removal before M4.** `MovementSystem`, `NavMeshManager`,
  `NavMeshPathRequestSystem`, `NavMeshConfineSystem`, `NavMeshStaticObstacle`
  remain in place until M4's caller migration completes; M5 keeps
  `WallDoorAccessSystem` / `WallGatePassabilitySystem` / any extant
  `WallAccessSystem` until their portal-graph replacements ship.
- **OOS3 -- Unity AI Navigation package removal.** Removing the
  `com.unity.ai.navigation` package from `manifest.json` is a separate
  cleanup; this task only removes the C# call sites.
- **OOS4 -- Networking transport changes.** Lockstep determinism inside the
  sim is required, but the lockstep transport / dispatch
  (`LockstepManager`, `NetworkIdGenerator`) is unchanged.
- **OOS5 -- Spec section 8 "don't jump ahead" rule.** Per the 2026-05-31
  user override recorded in `Implementation Phases`, all 7 phases execute
  end-to-end in one autopilot pass; no Editor verification gate between
  phases.

## Acceptance Criteria

Criteria trace to `spec.md` section 3 (AC-1..AC-6), the 7 phase scenarios
(AC-P1..AC-P7), and the test-suite delivery (AC-T1, AC-T2).

### Spec section 3 -- core architectural acceptance

- [x] **AC-1 (R1):** Given a Phase2Test crowd of 300 Swordsmen all commanded
      to the same point, when the steering tick runs, then no two units
      occupy overlapping collider footprints at rest AND no transient unit
      position is ever written into S1 / S3 storage (grep assertion in the
      EditMode suite).
- [x] **AC-2 (R2):** Given Phase4Test running with 50 active pathers, when
      the player places or destroys a wall mid-run, then S1 marks only the
      touched tiles dirty, S3 incrementally rebuilds only those tiles'
      portals, S5 evicts only the cache entries that intersect the dirty
      set, and the sim tick that processes the change does not exceed the
      S9 per-tick budget (asserted via tick-time sample).
- [x] **AC-3 (R3):** Given Phase5Test's Alanthor wall ring with one stair
      and one gatehouse, when a friendly unit is commanded from the ground
      to a rampart waypoint, then S4 returns a path that traverses a climb
      portal and S10 transitions the unit between Ground and Rampart
      contexts (animated, not teleport) and the post-transition cost-field
      sample comes from the Rampart context.
- [x] **AC-4 (R4):** Given Phase5Test's gatehouse with the gate closed,
      when an enemy unit pathing through the ground gate portal reaches it,
      then S10 rejects the traversal AND S4 (when re-queried after gate
      state change) treats the rampart gate portal independently from the
      ground gate portal (each toggleable on its own).
- [x] **AC-5 (R5):** Given Phase6Test's 60 mixed-footprint units (40 small
      + 20 large), when S4 plans a group move, then large-footprint units
      do not select portals narrower than their footprint AND S5 generates
      flow against the most-restricted profile then extends coverage so
      small-footprint units in the group are not stranded.
- [x] **AC-6 (R7, R8):** Given Phase7Test's determinism replay (100 units,
      30 scripted sim ticks), when the scenario runs twice in the same
      Editor session and once on a second machine, then the final entity
      position snapshots are byte-identical across all three runs AND every
      nav job is Burst-compiled (asserted via `[BurstCompile]` attribute
      scan in the EditMode suite).

### Per-phase scenario acceptance (one runnable scenario per phase)

- [x] **AC-P1 (Phase 1 / M1):** `ScenarioType.Phase1Test` enum entry exists,
      a menu button launches it, `Assets/Scripts/Bootstrap/Phase1Test*.cs`
      sets up a flat 64x64 ground grid and one Swordsman, click-to-move
      drives the unit to the goal cell via whole-map flow + S6 follow.
- [x] **AC-P2 (Phase 2 / M2):** `ScenarioType.Phase2Test` spawns 300
      Swordsmen in a 10x30 block; a single click-to-move target resolves
      with all units reaching the goal and no unit stacking violations
      (S2 + S7 active).
- [x] **AC-P3 (Phase 3 / M3):** `ScenarioType.Phase3Test` spawns 1
      Swordsman on a 512x512 grid; SW->NE corner move completes via S3
      portal graph + S4 abstract A* + S5 segmented flow with NO whole-map
      integration field allocated (asserted by S5 allocation log).
- [x] **AC-P4 (Phase 4 / M4):** `ScenarioType.Phase4Test` spawns 50
      Swordsmen pathing; player wall place/destroy mid-run triggers S1
      dirty-tracking -> S3 incremental rebuild + S5 cache invalidation;
      legacy NavMesh stack files (`MovementSystem.cs`, `NavMeshManager.cs`,
      `NavMeshPathRequestSystem.cs`, `NavMeshConfineSystem.cs`,
      `NavMeshStaticObstacle.cs`) are deleted from the working tree.
- [x] **AC-P5 (Phase 5 / M5):** `ScenarioType.Phase5Test` spawns an
      Alanthor wall ring (one stair + one gatehouse) with 10 friendly +
      10 enemy units; friendlies path through climb + gate portals on
      both layers, enemies are rejected at gate portals;
      `WallDoorAccessSystem` and `WallGatePassabilitySystem` are deleted
      after their portal-graph replacements ship.
- [x] **AC-P6 (Phase 6 / M6):** `ScenarioType.Phase6Test` spawns 60 mixed
      units (40 small + 20 large) in a formation move across the map;
      formation slots are deterministically assigned (S8), extended flow
      handles mixed sizes (S5), S9 budgets queue without stalling.
- [x] **AC-P7 (Phase 7 / M7):** `ScenarioType.Phase7Test` runs the
      determinism replay (100 units, 30 ticks, snapshot, replay,
      byte-identical assertion); S11 debug viz draws cost-field heatmap,
      portal graph, A* path, and flow vectors per layer in the Editor
      Scene view (editor-only, does not affect sim).

### Test-suite delivery

- [x] **AC-T1 (PlayMode harness):**
      `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` exists, bootstraps
      its own World (no dependency on missing prefabs / Build Settings
      scenes), runs all 7 phase scenarios in sequence in a single
      PlayMode test, asserts the success criterion of each
      (units-reached-goal / no-overlap / determinism-byte-identical), and
      emits a structured pass/fail report per phase. Discoverable via
      `Window > General > Test Runner`.
- [x] **AC-T2 (EditMode suite):** `Assets/Tests/EditMode/NavStack/`
      directory contains tests covering: cost-field stamping (S1),
      spatial hash bucket determinism (S2), portal detection (S3),
      abstract A* on a hand-authored graph (S4), flow correctness (S5),
      steering force-accumulation order (S7), dirty-tile incremental
      rebuild (S1->S3), gate-portal owner gating (S10), formation slot
      assignment determinism (S8), and request budget coalescing (S9).
      Discoverable via `Window > General > Test Runner`.

## Implementation Phases

The user prompt mandates a fixed 7-milestone build order (`spec.md` section 7) and
a working-agreement rule "Don't jump ahead to later milestones; finish and verify
the current one first" (`spec.md` section 8). Each milestone maps to one phase:

| Phase | Milestone | Scope |
|-------|-----------|-------|
| 1 | M1 | One unit moves on whole-map flow (S1 single-layer + whole-map flow + S6) |
| 2 | M2 | Crowds -- ~hundreds of units avoid each other (add S2 + S7) |
| 3 | M3 | Scale -- hierarchical portal graph + segmented/cached flow (add S3 + S4 + S5) |
| 4 | M4 | Dynamic world -- S1 dirty-tracking drives S3 incremental rebuild; NavMesh stack deletion |
| 5 | M5 | Walls + gates -- Rampart layer in S1, climb + gate portals in S3, add S10 |
| 6 | M6 | Polish -- formations (S8), mixed footprints / extended flow, request budget (S9) |
| 7 | M7 | Hardening -- determinism audit, S11 debug viz, stress + regression tests |

Phase boundaries are gates: M4 deletes the NavMesh stack only after all callers
migrated; M5 deletes `WallAccessSystem`/`WallDoorAccessSystem`/`WallGatePassabilitySystem`
only after their replacements ship.

**User override (2026-05-31):** the canonical spec section 8 says "Don't jump
ahead to later milestones; finish and verify the current one first." The user
has explicitly waived this for this run -- execute all 7 phases end-to-end in
one pass without pausing for Editor verification between phases.

## Per-Phase Scenarios (cross-cutting requirement)

Each phase must ship **one runnable scenario** registered as a `ScenarioType`
enum entry + a menu button + a setup script under `Assets/Scripts/Bootstrap/`,
exercising the core of what that phase added:

| Phase | Scenario | Validates |
|-------|----------|-----------|
| 1 | `Phase1Test` -- 1 Swordsman click-to-move on flat 64x64 ground | S1 cost field + whole-map flow + S6 follow |
| 2 | `Phase2Test` -- 300 Swordsmen spawned in a 10x30 block, all click to same point | S2 spatial hash + S7 steering (no stacking) |
| 3 | `Phase3Test` -- 1 Swordsman moves SW corner to NE corner on 512x512 grid | S3 portal graph + S4 abstract A* + S5 segmented flow |
| 4 | `Phase4Test` -- 50 Swordsmen pathing; player places/destroys a wall mid-run | S1 dirty tracking -> S3 incremental rebuild + cache invalidation |
| 5 | `Phase5Test` -- Alanthor wall ring with one stair + one gatehouse; 10 friendly + 10 enemy units pathing through | S10 layer transition + climb portals + ground/rampart gate portals + owner gating |
| 6 | `Phase6Test` -- 60 mixed units (40 small footprint + 20 large) in formation move across the map | S8 formations + S5 extended flow + S9 budgeting |
| 7 | `Phase7Test` -- determinism replay: spawn 100 units, run scripted commands for 30 sim ticks, snapshot positions, replay, assert byte-identical | S11 viz + determinism audit |

Scenario menu entries replace the existing `Phase1-4Test` slots in the previous
nav-stack attempt (referenced in commits `9f0b5e4..be653ae` -- the patterns
survive in git even though the task dir doesn't).

## Test Script (cross-cutting requirement)

Deliver two complementary harnesses:

1. **PlayMode harness** at `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs`
   that runs all 7 scenarios in sequence in a single PlayMode test, asserts
   the success criterion of each (units reached goal / no overlap / determinism
   matches), and reports a structured pass/fail per phase.
2. **EditMode suite** at `Assets/Tests/EditMode/NavStack/` covering every
   system's invariants on hand-authored grids: cost-field stamping, spatial
   hash bucket determinism, portal detection, abstract A* on a known graph,
   flow correctness, steering force-accumulation order, dirty-tile incremental
   rebuild, gate-portal owner gating, formation slot assignment determinism,
   request budget coalescing.

Both suites must run via `Window > General > Test Runner` with zero
dependencies on missing prefabs / Build Settings scenes (the PlayMode test
must bootstrap its own World).

## Release Notes

Crystal Curse Navigation Stack (M1-M7) shipped on 2026-06-01. Replaces
Unity's NavMesh stack with a deterministic, Burst-compiled DOTS pathing
pipeline (cost field + portal graph + abstract A* + segmented flow fields
+ steering) delivering the Alanthor two-layer wall fantasy, dynamic Crystal
Curse mutation, and lockstep-safe replay. All 6 spec ACs + 7 phase ACs +
2 test-suite ACs verified by the 2026-06-01 review (`"verdict": "approve"`,
0 blocking findings). Build green (0 errors / 0 warnings). 56 EditMode
tests across 27 classes + 1 PlayMode harness (shipped `[Ignore]`d per
CCD-7, gate documented). NavMesh stack files
(`MovementSystem.cs` / `NavMeshManager.cs` / `NavMeshPathRequestSystem.cs`
/ `NavMeshConfineSystem.cs` / `NavMeshStaticObstacle.cs` /
`NavMeshComponents.cs` / `UnitSeparationSystem.cs`) deleted in M4;
`WallDoorAccessSystem.cs` + `WallGatePassabilitySystem.cs` deleted in M5
after `LayerTransitionSystem` + `GateStateSystem` shipped.

Open follow-up: M8 task to migrate the remaining ~13 `PassabilityGrid`
callers (reachability BFS, Minkowski-sum radius checks, half-cell LOS,
multi-class cell states) once `NavGridQuery` replicates those features.
PlayMode harness `[Ignore]` lifts when Build Settings is wired with a
scene.

See `release.md` in this directory for the full release report, system
inventory (S1-S11), file tally, migration notes, open follow-ups, the
DR-1..DR-20 determinism contract, how-to-run instructions, and the
full acceptance-criteria table.
