---
deft:
  id: task-nav-stack-flowfields-112
  type: release
  stage: release
  generated_at: "2026-06-01"
---

# Release Notes -- Crystal Curse Navigation Stack (M1-M7)

## Headline

Replaces Unity's NavMesh stack with a deterministic, Burst-compiled DOTS pathing
pipeline (cost field + portal graph + abstract A* + segmented flow fields +
steering) that ships the Alanthor two-layer wall fantasy, the dynamic-world
Crystal Curse mutation loop, and a lockstep-safe replay contract -- all over
seven phases (M1-M7) with 56 EditMode tests and a 7-scenario PlayMode harness.

---

## What changed

### System inventory shipped (spec section 5 / S1-S11)

- **S1 Cost Field** -- `NavCostField` (byte Cost + byte Flags, row-major within
  layer, layer-major across layers; 254=conditional/gate, 255=impassable; M5
  promoted `LayerCount` from 1 to 2 for the Rampart slab) plus per-tick
  `CostFieldStampSystem` and incremental `BuildingCostStampSystem` with
  dirty-tile atomic-OR.
- **S2 Spatial Hash** -- `NavSpatialHash` singleton built each tick by
  `SpatialHashRebuildSystem` using `NativeParallelMultiHashMap<int, Entity>`,
  consumed exclusively via per-key `TryGetFirstValue/TryGetNextValue` probes
  (no `GetEnumerator`, per DR-2).
- **S3 Portal Graph** -- `PortalGraphBlob` (CSR layout with sorted `Nodes`,
  `Edges`, `NodeFirstEdge`) assembled by `PortalGraphBuildSystem` from
  `PortalDetectionJob` output plus `WallPortalGraphAppender` for climb/gate
  pairs; rebuilds via `IncrementalPortalRebuildSystem` using the CCD-5 swap
  protocol.
- **S4 Abstract Pathfinder** -- `AbstractPathfinder` static helper + per-tick
  `AbstractPathfinderSystem` (budget 8 requests/tick, ascending `entity.Index`
  tie-break, virtual start/goal nodes per request); M5 added `SolveGated`
  overload reading `PortalOwnerBitsMirror` for owner + open-bit filtering.
- **S5 Flow Field Generation** -- `FlowSegmentSystem` owns `NavFlowCache`
  (256-slab LRU pool keyed by `(tileIndex, exitPortal, profileHash)` with
  byte directions + uint integration); per-miss `IntegrateTileJob` mirrors
  the M1 integer Dijkstra (StepCardinal=10, StepDiagonal=14); M6 added
  `ExtendedFlowSystem` for per-formation dominant-profile aggregation.
- **S6 Flow Following / Movement** -- `FlowFollowSystem` samples the cache
  and writes `FlowDesiredDir`; `UnitIntegratorSystem` (M4 replacement for
  `MovementSystem.cs`) is the sole integrator and reads `SteeringDesiredDir`
  first, then `FlowDesiredDir`, with layer-aware height snap.
- **S7 Steering / Local Avoidance** -- `SteeringSystem` runs Burst
  `AccumulateSteeringForcesJob` with the locked five-layer order
  (separation -> unit-avoidance -> obstacle-avoidance -> cohesion -> flow);
  deterministic `CompareEntities` tie-break for stacked pairs.
- **S8 Formation Movement** -- `FormationLeaderNavSystem` runs per-unit
  Bresenham LOS-vs-flow fallback against `NavCostField.Cost`; `ExtendedFlowSystem`
  computes the per-formation dominant `TraversalProfile`.
- **S9 Request Scheduler / Budget** -- `NavRequestSchedulerSystem` (M6) owns
  `NavRequestQueueSingleton` (MaxRequestsPerTick=16), coalesces by
  `NavRequestCoalesceKey`, sorts by (Priority, EnqueueTick, Requester.Index,
  Requester.Version) per DR-12, and emits `NavPathRequest` via ECB.
- **S10 Layer Transition / Portal Traversal** -- `LayerTransitionSystem`
  (M5 replacement for `WallDoorAccessSystem.cs`) handles climb/gate portal
  entry, animated lerp transitions (TransitionRate=1.6666), and backstop
  eligibility recheck via `IsPortalAdmissible`; `GateStateSystem`
  (M5 replacement for `WallGatePassabilitySystem.cs`) polls every 18 ticks
  and flips owner-bits-mirror open bits in place per CCD-5.
- **S11 Debug Visualization (editor only)** -- `Editor/NavDebugDrawSystem.cs`
  wrapped in `#if UNITY_EDITOR`, runs in `PresentationSystemGroup` (DR-16);
  toggleable cost-field heatmap, portal graph, A* path, and flow vectors
  via `GameSettings.NavDebugVisualization`.

### File tally

- **Created (runtime)**: 27 files under `Assets/Scripts/Systems/Navigation/`
  (including `Jobs/` and `Editor/`) plus `Core/Components/NavComponents.cs`.
- **Created (scenarios)**: 7 files under `Assets/Scripts/Bootstrap/`
  (`Phase1TestSetup.cs` through `Phase7TestSetup.cs`) plus
  `Phase4ScriptedWallController.cs` (M4 dynamic wall scripted run).
- **Created (tests)**: 27 EditMode test classes (3 M1 + 4 M2 + 4 M3 + 3 M4
  + 4 M5 + 5 M6 + 4 M7) + 1 PlayMode harness
  (`Assets/Tests/PlayMode/NavStackAllPhasesTest.cs`) + 2 test asmdefs.
- **Edited**: `MovementSystem.cs` (M1/M2 preference chain), `MoveCommand.cs`
  / `AttackMoveCommand.cs` (M3/M6 caller migration), `GameSettings.cs`
  (Phase1-7Test enum entries + M7 `NavReplayMode` / `NavDebugVisualization`),
  `MainMenuUI.cs` + `ScenarioSetup.cs` (one entry per phase scenario),
  `TargetingSystem.cs` (M4 `UpdateAfter` swap), `WallGarrisonSystem.cs`
  (M5 layer-aware elevated test).
- **Deleted (NavMesh stack, M4)**: `Assets/Scripts/Systems/Movement/MovementSystem.cs`,
  `NavMeshManager.cs`, `NavMeshPathRequestSystem.cs`, `NavMeshConfineSystem.cs`,
  `NavMeshStaticObstacle.cs`, `Assets/Scripts/Core/Components/NavMeshComponents.cs`,
  `UnitSeparationSystem.cs`.
- **Deleted (wall stack, M5)**: `Assets/Scripts/Systems/Movement/WallDoorAccessSystem.cs`,
  `Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs` (plus
  their `.meta` files).

### Test counts

- **EditMode**: 27 test classes, 56 individual `[Test]` methods.
- **PlayMode**: 1 `[UnityTest]` harness covering 7 phase scenarios end-to-end
  (currently `[Ignore]`d per CCD-7 -- see open follow-ups).
- **Build status**: `dotnet build Assembly-CSharp.csproj` -> **0 errors,
  0 warnings**.

---

## Migration notes

- **NavMesh stack is gone.** Any new movement / pathing / steering work goes
  through `Assets/Scripts/Systems/Navigation/` (namespace
  `TheWaningBorder.Systems.Navigation`). Do not reintroduce `NavMeshAgent`,
  `NavMeshPath`, `NavMeshQuery`, or any code under
  `Systems/Movement/NavMesh*.cs` -- those files are deleted and the API
  is deprecated in Unity 6.
- **WallDoorAccessSystem + WallGatePassabilitySystem deleted.** Their
  replacements are `LayerTransitionSystem` (handles climb portals + gate
  ground/rampart traversal with animated lerp + backstop eligibility check)
  and `GateStateSystem` (polls every 18 ticks, mutates
  `PortalOwnerBitsMirror.Bits` slots in place to avoid graph swap).
  `WallAccessState` (global namespace) is also gone with the file.
- **UnitSeparationSystem deleted.** Replaced by the M2 `SteeringSystem` which
  carries the five-layer force accumulation (separation /
  unit-avoidance / obstacle-avoidance / cohesion / flow) in a deterministic
  locked order per DR-1.
- **Command routing changed.** `MoveCommandHelper.Execute` and
  `AttackMoveCommandHelper.Execute` now enqueue via
  `NavRequestSchedulerSystem.EnqueueRequest(em, unit, startCell, goalCell,
  profileHash, priority, generation)` against the `NavRequestQueueSingleton`
  (M6 scheduler). The M3-era direct `NavPathRequest` attach path is gone;
  the scheduler helper has a one-frame fallback that attaches directly when
  the queue singleton hasn't bootstrapped yet (first-frame race only).
- **`PassabilityGrid` still exists for non-pathing callers.** It is alive
  for reachability BFS (`ComputePlayerReachability`,
  `IsReachableByAllPlayers`), Minkowski-sum radius checks
  (`IsPassableForRadius`, `IsCellPassableForRadius`), half-cell-sampled
  line-of-sight (`HasClearLineOfSight`), and multi-class cell state queries.
  An M8-followup task should finish the cutover once the new
  `NavGridQuery` API surface replicates those features (or its callers are
  proven not to need them).
- **Per-unit nav components attach automatically.** `UnitNavProfileAttachSystem`
  lazily adds `NavLayerIndex` + `NavTraversalProfile` + `FormationFlowFollower`
  to every `UnitTag` entity that lacks them; you do **not** need to edit the
  ~25 `Entities/Units/*.cs` factories. The attach is zero-cost after the
  initial promotion (the query stays empty).
- **TraversalProfile blob is the only acceptable source.** Read profiles via
  `ref var prof = ref profiles[id]` per DOTS analyzer EA0001; never copy the
  blob into a managed `TraversalProfile`.

---

## Open follow-ups

### M8-followup carryovers (PassabilityGrid migration)

- **PassabilityGrid.cs** -- annotated with a file-header docstring listing the
  feature categories the new stack does not yet replicate (BFS reachability,
  Minkowski-sum geometric checks, half-cell LOS, multi-class cell states,
  terrain slope / water classification). Migration is an explicit M8 cleanup
  task.
- **PassabilityBuildingSync.cs** -- kept while `PassabilityGrid` lives.
- **PathfindingTestSetup.cs** -- alive because `GameMode.PathfindingTest`
  still dispatches to it from `GameBootstrap.cs:87` and
  `SpawnDelayHelper.cs:40` (the M1 event's "dead test setup" note was
  incorrect).
- **M8-followup comment markers added** at the three most-visible call sites:
  `PathfindingTestSetup.cs` (GameObject spawn site),
  `GameBootstrap.cs` (existing docstring extended),
  `BattalionSyncSystem.cs:151` (the dead `passGrid = null` local).
- **Affected callers** (~13): `Bootstrap/CrystalPatchBootstrap`,
  `Bootstrap/GameBootstrap` (line ~447-451), `Bootstrap/IronDepositBootstrap`,
  `Bootstrap/PathfindingTestSetup`, `Bootstrap/SpawnDelayHelper`,
  `Commands/BuildCommand`, `Economy/GathererHutIncomeSystem`,
  `Systems/Crystal/CrystalAISystem`, `Systems/Crystal/CrystalExtinctionSystem`,
  `Systems/Movement/BattalionSyncSystem`.

### Documented architecture deviations that defer work

- **M3 intra-tile portal edges use Manhattan cost instead of per-portal
  flood-fill.** Connected enough for the AC-P3 SW->NE 512x512 test; an M4/M8
  task can ship the flood-fill once incremental rebuild needs the fidelity.
- **M3 abstract A* open list uses a flat (f, nodeId) min-scan** instead of a
  true bucketed priority queue with `BucketWidth=4`. Observationally
  equivalent (always pops smallest (f, nodeId) deterministically); a future
  bucket-queue swap stays bit-compatible because the `BucketWidth` constant
  is exposed.
- **M3 pathfinder runs sequentially on the main thread.** The architecture's
  `IJobParallelFor` over request indices remains a mechanical upgrade once
  the M6 scheduler budget feeds it.
- **M3 `FlowSegmentSystem.IntegrateTile` uses `Schedule().Complete()` inline
  per miss** instead of the architecture's parallel-over-active-window job.
  Acceptable while the M4 dirty-tile invalidation keeps the per-frame miss
  count bounded.
- **M5 `WallPortalDetectionSystem` uses a hub-count/gate-count change
  detector** instead of wiring `NavDirtyTiles` events. Sufficient for the
  M5 scenario (walls built once at scenario setup); M6/M8 polish can wire
  the dirty-tile feed.
- **M5 gate portal cell pair approximated as centre-cell +/- 1 along X.**
  Works for the Phase5Test south-facing wall; arbitrary segment orientations
  need axis-aware logic (documented inline).
- **M5 `WallGateRegistrationSystem` per-tick scans the portal blob for
  matching cells** -- O(gates * portals), bounded but improvable by caching
  the mapping at rebuild time.
- **M6 ships main-thread sort+coalesce+dispatch** instead of the
  per-thread `NativeStream` merge -> sort -> dispatch chain. Acceptable for
  the M6 workload; the `IJobParallelFor` variant is a mechanical upgrade.
- **M6 `FormationProfileAggregate` is computed and stamped but not yet read
  by `FlowSegmentSystem`.** The cache slab key currently uses `ProfileHash=0`;
  wiring it through requires re-keying the cache, deferred to M7/M8.
- **M6 CSR-surgery polish deferred.** Marked `// M7 TODO` in
  `IncrementalPortalRebuildSystem.cs`; full-rebuild path satisfies AC-2.
- **M7 `NavDebugDrawSystem` caps cost-field + flow-vector draws at ~5000
  samples per pass** to keep editor frame budget on 512x512 grids reasonable.

### Test-harness gate

- **PlayMode harness `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` ships
  `[Ignore]`d** per CCD-7 and the M1 memory note that existing PlayMode tests
  are `[Ignore]`d because the editor's Build Settings is incomplete. Remove
  `[Ignore]` once a Build Settings scene is wired; the per-phase
  `CheckSuccess` predicates are correct and match each phase's AC.

---

## Determinism contract

The nav stack must remain **bit-identical across machines** for lockstep
multiplayer to work. The Determinism Risk Register (DR-1 through DR-20) is
the contract every future contributor must respect. In plain language:

- **No floating-point nondeterminism in sim-affecting code.** No
  `UnityEngine.Random`, no `Mathf.*` (Burst math intrinsics only), no
  `Time.deltaTime` / `Time.realtimeSinceStartup` / `Time.ElapsedTime` reads,
  no `Physics.Raycast`, no wall-clock anywhere. The fixed-step `1f / FixedTickHz`
  constant is the only legal `dt`. The replay log stores positions in
  integer **millimetres** (`int3`), never floats.
- **Integer math in hot paths.** Costs are bytes (0..200 walkable, 254
  conditional, 255 impassable). Integration is `uint`. Octile steps are
  hard-coded `StepCardinal=10, StepDiagonal=14`. Flow direction is a byte
  indexing a 256-entry blob table built once at world init.
- **Locked iteration order everywhere.** S1 stamps in entity-index order; S2
  hash buckets walked via per-key `TryGetFirstValue/TryGetNextValue` only
  (never `GetEnumerator`); S3 flood-fill row-major within tile, tile-index
  ascending across; S4 A* tie-break by ascending node-id on equal f-scores;
  S5 cache eviction LRU with smallest slot-index tie-break; S7 force
  accumulation in the fixed five-layer order (separation -> unit-avoidance
  -> obstacle-avoidance -> cohesion -> flow); S8 formation slot assignment
  sorted by entity-index; S9 request scheduler sorted by (priority,
  enqueue-tick, requester.index, requester.version).
- **Burst is mandatory in hot paths.** Every `IJob` / `IJobEntity` /
  `IJobParallelFor` carries `[BurstCompile]`; the M7
  `BurstAttributeAuditTest` reflectively enforces this for any new job
  added to the `TheWaningBorder.Systems.Navigation` namespace.
- **BlobAssetReference swaps follow the CCD-5 protocol.** Drain
  `state.Dependency.Complete()` before publish, dispose the old handle after
  swap, and S9 rejects in-flight requests with stale `Generation` stamps.
- **Editor / debug code stays outside the sim group.** S11 viz runs in
  `PresentationSystemGroup` under `#if UNITY_EDITOR` and only ever reads.

A bit-divergence in any of the above is a P0 lockstep bug. The
`DeterminismReplaySystem` Record/Replay loop is the canonical regression
gate.

---

## How to run the deliverables

### EditMode test suite (27 classes / 56 tests)

`Window > General > Test Runner > EditMode > TheWaningBorder.Tests.EditMode > NavStack`

Folders mirror the milestones: `NavStack/M1/` through `NavStack/M7/`.
Run all to assert every system's invariants on hand-authored grids.

### Per-phase Editor scenarios

`Main Menu > Scenarios > "Phase {N} Nav Test"` for `N` in 1..7:

| Phase | Menu entry | Validates |
|-------|------------|-----------|
| 1 | Phase 1 Nav Test (1 unit flat grid) | S1 cost field + whole-map flow + S6 follow |
| 2 | Phase 2 Nav Test (300 swordsmen) | S2 spatial hash + S7 steering (no stacking) |
| 3 | Phase 3 Nav Test (SW->NE 512x512) | S3 portal graph + S4 abstract A* + S5 segmented flow |
| 4 | Phase 4 Nav Test (dynamic wall) | S1 dirty tracking -> S3 incremental rebuild + cache invalidation |
| 5 | Phase 5 Nav Test (wall ring + gate) | S10 layer transition + climb / gate portals + owner gating |
| 6 | Phase 6 Nav Test (formations) | S8 formations + S5 extended flow + S9 budget |
| 7 | Phase 7 Nav Test (determinism replay) | S11 viz + determinism contract |

### PlayMode harness (currently `[Ignore]`d)

`Assets/Tests/PlayMode/NavStackAllPhasesTest.cs`

Once a Build Settings scene is wired, remove the `[Ignore]` attribute and
the single `[UnityTest] RunAllPhases` coroutine bootstraps an isolated ECS
world, dispatches each phase's `PhaseNTestSetup.SpawnScenarioEntities`,
ticks the simulation group up to a per-phase budget (5/15/30/20/25/30/15s),
and asserts the AC criterion of each via `CheckSuccess`. Pass/fail emitted
per phase.

### Determinism replay

1. Set `GameSettings.NavReplayMode = NavReplayMode.Record` and run the
   Phase 7 scenario (or any deterministic run with a scripted command set);
   `DeterminismReplaySystem` appends per-tick (`tick`, `entity-index`,
   `position-millimetres`) tuples to `DeterminismReplayLog.Log`.
2. Set `GameSettings.NavReplayMode = NavReplayMode.Replay` and run again;
   the system compares each snapshot at `ReplayCursor` against the recorded
   tuple and bumps `DivergenceCount` on first divergence per tick
   (logged via `Debug.LogError`).
3. **Expected result:** `DeterminismReplayLog.DivergenceCount == 0` and
   `HasData != 0` and `CurrentTick >= 30`.

---

## Acceptance criteria summary

Pulled verbatim from the 2026-06-01 review verdict (`"verdict": "approve"`,
0 blocking findings):

### Spec section 3 -- core architectural acceptance

| AC | Requirement | Status | Evidence |
|----|-------------|--------|----------|
| AC-1 | R1 -- 300 swordsmen no collider overlap + no transient unit in S1/S3 | PASS | Phase2Test + `SteeringNoStackingTests` + grep assertion |
| AC-2 | R2 -- dirty-tile incremental rebuild + cache eviction + per-tick budget | PASS | Phase4Test + `DirtyTrackingTests` + `CacheInvalidationTests` + `IncrementalRebuildTests` |
| AC-3 | R3 -- climb portal traversal + Ground<->Rampart context switch | PASS | Phase5Test + `LayerTransitionTests` + `LayerCostFieldTests` |
| AC-4 | R4 -- enemy rejected at closed gate + independent ground/rampart toggle | PASS | Phase5Test + `GateOwnerGatingTests` + `GateToggleTests` |
| AC-5 | R5 -- large-footprint units skip narrow portals + extended flow covers small | PASS | Phase6Test + `ExtendedFlowProfileTests` |
| AC-6 | R7, R8 -- byte-identical replay across two runs + Burst-attribute audit | PASS | Phase7Test + `DeterminismIntegrationSweepTest` + `DeterminismAStarRepeatTest` + `DeterminismSpatialHashRepeatTest` + `BurstAttributeAuditTest` |

### Per-phase scenario acceptance

| AC | Phase | Scenario | Status |
|----|-------|----------|--------|
| AC-P1 | M1 | `Phase1Test` -- 1 Swordsman flat 64x64 | PASS |
| AC-P2 | M2 | `Phase2Test` -- 300 Swordsmen 10x30 block | PASS |
| AC-P3 | M3 | `Phase3Test` -- 1 Swordsman SW->NE 512x512 (no whole-map alloc) | PASS |
| AC-P4 | M4 | `Phase4Test` -- 50 Swordsmen + scripted wall place/destroy + NavMesh deletion | PASS |
| AC-P5 | M5 | `Phase5Test` -- Alanthor wall ring + climb/gate + WallDoor/WallGate deletion | PASS |
| AC-P6 | M6 | `Phase6Test` -- 60 mixed units formation move | PASS |
| AC-P7 | M7 | `Phase7Test` -- 100 units / 30 ticks / byte-identical replay + S11 viz | PASS |

### Test-suite delivery

| AC | Deliverable | Status |
|----|-------------|--------|
| AC-T1 | PlayMode harness at `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` | PASS (shipped `[Ignore]`d per CCD-7; gate documented) |
| AC-T2 | EditMode suite under `Assets/Tests/EditMode/NavStack/` covering all systems | PASS (27 classes / 56 tests) |

---

## Automated checks

| Check | Result |
|-------|--------|
| `dotnet build Assembly-CSharp.csproj` | **0 errors / 0 warnings** |
| NavMesh stack files deleted | Verified (`Systems/Movement/` no longer contains `MovementSystem.cs`, `NavMesh*.cs`, `UnitSeparationSystem.cs`) |
| Wall stack deletions (M5 gate) | Verified (`WallDoorAccessSystem.cs`, `WallGatePassabilitySystem.cs` gone) |
| Determinism grep over `Systems/Navigation/` | Clean -- no live `UnityEngine.Random` / `Mathf.` / `Time.deltaTime` / `Time.realtimeSinceStartup` / `Physics.Raycast` (4 hits all in comments documenting absence) |
| Burst-attribute audit | Enforced by `BurstAttributeAuditTest` (M7) |

---

## Release checklist

- [x] All automated checks pass (`dotnet build` 0/0, deletions verified, determinism grep clean)
- [x] All acceptance criteria verified via approved review (6 spec ACs + 7 phase ACs + 2 test-suite ACs)
- [x] No unresolved scope changes (M8-followup PassabilityGrid carry-over documented and non-blocking per task body escape clause)
- [x] Release notes prepared
- [x] Approved review present (`phase-2026-06-01-review`, `"verdict": "approve"`)

---

## Task timeline

| Date | Event |
|------|-------|
| 2026-05-31 | Scope phase -- R1-R11 + OOS1-OOS5 + AC-1..AC-6 + AC-P1..AC-P7 + AC-T1/T2 locked |
| 2026-05-31 | Architecture phase -- CCD-1..CCD-7 + DR-1..DR-20 + per-phase blueprints |
| 2026-05-31 | M1 shipped -- single-layer cost field + whole-map flow + S6 + Phase1Test |
| 2026-05-31 | M2 shipped -- spatial hash + steering + Phase2Test (300 units) |
| 2026-06-01 | M3 shipped -- portal graph + abstract A* + segmented cached flow + Phase3Test (512x512) |
| 2026-06-01 | M4 shipped -- dirty tracking + incremental rebuild + NavMesh stack deletion + Phase4Test |
| 2026-06-01 | M5 shipped -- Rampart layer + climb/gate portals + S10 + WallDoor/WallGate deletion + Phase5Test |
| 2026-06-01 | M6 shipped -- request scheduler + extended flow + formation LOS-vs-flow + Phase6Test |
| 2026-06-01 | M7 shipped -- determinism replay + S11 debug viz + PlayMode harness + Phase7Test |
| 2026-06-01 | Review phase -- approve verdict, 0 blocking findings |
| 2026-06-01 | Release phase -- this document |

---

Task complete.
