# Navigation Stack -- Technical Reference

Implementation reference for the task-112 navigation rewrite (M1-M7).
Captures the shipped runtime, the test suite, and the determinism
audit protocol so a future contributor can answer
"what does the nav stack actually do?" without re-reading every commit
in the M1-M7 series.

## Runtime layout

All runtime nav code lives in
`Assets/Scripts/Systems/Navigation/` in namespace
`TheWaningBorder.Systems.Navigation`. ECS components live alongside
the existing project convention at
`Assets/Scripts/Core/Components/NavComponents.cs`.

| Layer | System | Owns |
|-------|--------|------|
| Grid bootstrap | `NavGridBootstrapSystem` | `NavGridSingleton`, `NavCostField`, `DirectionTableSingleton` |
| Cost field stamping | `CostFieldStampSystem` | per-tick re-clear of layer 0/1, wall stamps |
| Dynamic stamping | `BuildingCostStampSystem` | building footprint diff, dirty-tile mask |
| Portal graph (M3) | `PortalGraphBuildSystem` | `PortalGraphSingleton` (BlobAssetReference) |
| Incremental rebuild (M4) | `IncrementalPortalRebuildSystem` | drains dirty tiles, rebuilds graph blob |
| Pathfinder (M3) | `AbstractPathfinderSystem` + `AbstractPathfinder` static helper | A* on the portal graph |
| Flow cache (M3) | `FlowSegmentSystem` | `NavFlowCache` (256 slabs) |
| Flow follow (M3) | `FlowFollowSystem` | per-unit `FlowDesiredDir` |
| Spatial hash (M2) | `SpatialHashRebuildSystem` | `NavSpatialHash` |
| Steering (M2) | `SteeringSystem` | per-unit `SteeringDesiredDir` |
| Wall portals (M5) | `WallPortalDetectionSystem` + `WallPortalGraphAppender` + `WallGateRegistrationSystem` | climb / gate-ground / gate-rampart portals |
| Gate state (M5) | `GateStateSystem` | flips open-bit on `PortalOwnerBitsMirror` |
| Layer transition (M5) | `LayerTransitionSystem` | climb / gate traversal animation |
| Unit integrator | `UnitIntegratorSystem` | per-unit position integration (replaced legacy `MovementSystem`) |
| Profile attach (M5) | `UnitNavProfileAttachSystem` | lazy-attach `NavLayerIndex` / `NavTraversalProfile` / `FormationFlowFollower` |
| Traversal profiles | `TraversalProfileBootstrapSystem` | `TraversalProfileSingleton` (3 profiles) |
| Request scheduler (M6) | `NavRequestSchedulerSystem` | `NavRequestQueueSingleton` -- coalesces requests, releases up to 16/tick |
| Extended flow (M6) | `ExtendedFlowSystem` | aggregates per-formation dominant profile |
| Formation flow follower (M6) | `FormationLeaderNavSystem` | per-unit LOS-vs-flow blend |
| Determinism replay (M7) | `DeterminismReplaySystem` | per-tick `DeterminismReplayLog` (record / replay) |
| Debug viz (M7, S11) | `Editor/NavDebugDrawSystem` (`#if UNITY_EDITOR`) | gizmo overlays for cost / portals / flow / A* paths |

The legacy NavMesh stack (`MovementSystem`, `NavMeshManager`, etc.)
was deleted in M4 (see deletion list in
`.deft/tasks/task-nav-stack-flowfields-112/architecture.md` section 4.6).
The legacy `WallDoorAccessSystem` + `WallGatePassabilitySystem` were
deleted in M5.

## Determinism contract

The audit covers four risk classes (see Determinism Risk Register
DR-1..DR-20 in the architecture for the full list):

- **Ordering**: every job that walks an unordered container snapshots
  it into a `NativeArray<T>`, sorts by a stable integer key
  (typically `entity.Index`), and walks the sorted view.
- **Float math**: pre-converted to integer wherever the result feeds
  determinism-affecting state. The replay log uses
  `int3` MILLIMETRES (`UnitPositionSnapshot.PositionMillimeters`),
  not floats. Force accumulation in steering is fixed-point Q16.
- **Burst**: every IJob / IJobEntity / IJobParallelFor / IJobChunk in
  the `Navigation` namespace carries `[BurstCompile]`. Enforced by
  `BurstAttributeAuditTest` (M7).
- **Editor-only viz**: `NavDebugDrawSystem` is `#if UNITY_EDITOR`
  guarded AND lives in `PresentationSystemGroup` (outside the sim).
  The viz reads nav singletons read-only; it never writes them.

### Replay protocol (M7)

`DeterminismReplaySystem` runs `OrderLast` in `SimulationSystemGroup`.
Three modes via `GameSettings.NavReplayMode`:

- `Off` -- no work, no allocation. Default for production.
- `Record` -- snapshots every `UnitTag + LocalTransform`'s
  position into the `DeterminismReplayLog.Log` as
  `(Tick, EntityIndex, PositionMillimeters)` tuples, sorted by
  `entity.Index` ascending.
- `Replay` -- snapshots the same way, compares each tuple against
  the previously recorded entry at the same `ReplayCursor` offset,
  bumps `DivergenceCount` on mismatch, and logs the first divergence
  via `Debug.LogError` for editor halt.

The Phase7Test scenario flips into `Record` mode on setup; the
PlayMode harness (or a manual test driver) re-runs the scenario in
`Replay` mode and asserts `DivergenceCount == 0` at the end of the
30-tick scripted window.

## Test Suite

### EditMode tests

All EditMode tests live in `Assets/Tests/EditMode/NavStack/<Mn>/`
where `Mn` matches the milestone they were introduced in.

| Phase | Test class | Validates |
|-------|------------|-----------|
| M1 | `CostFieldStampingTests` | building footprint stamping, NavCostField.Index layout |
| M1 | `WholeMapFlowTests` | integration sweep, direction code, around-wall routing |
| M1 | `FlowFollowDeterminismTests` | per-tick flow sampler is byte-identical across two runs |
| M2 | `SpatialHashDeterminismTests` | bucket iteration order stable across runs (DR-2) |
| M2 | `SpatialHashBucketTests` | WorldToCell + PackKey contract |
| M2 | `SteeringForceOrderTests` | locked accumulation order (DR-1) |
| M2 | `SteeringNoStackingTests` | stacked-pair separation produces opposite-direction forces |
| M3 | `PortalDetectionTests` | tile-boundary span detection, ordering |
| M3 | `AbstractPathfinderTests` | A* path correctness, ascending-id tie-break (DR-3) |
| M3 | `FlowSegmentCacheTests` | cache hit/miss, LRU eviction |
| M3 | `BucketQueueDeterminismTests` | A* open-list determinism |
| M4 | `DirtyTrackingTests` | dirty tile bit-mask correctness |
| M4 | `IncrementalRebuildTests` | dirty-only graph rebuild |
| M4 | `CacheInvalidationTests` | flow cache eviction on dirty intersection |
| M5 | `LayerCostFieldTests` | wall / gate / climb stamps across layers |
| M5 | `GateOwnerGatingTests` | owner-mismatch path rejection (AC-4 / R4) |
| M5 | `GateToggleTests` | Pack/Unpack round-trip + in-place open flip |
| M5 | `LayerTransitionTests` | Progress integration + position lerp + layer flip |
| M6 | `RequestSchedulerCoalesceTests` | (goal, profile) coalescing |
| M6 | `RequestSchedulerBudgetTests` | 16-per-tick budget over multiple ticks |
| M6 | `RequestSchedulerTieBreakTests` | priority + tick + entity-index sort order |
| M6 | `ExtendedFlowProfileTests` | dominant-profile aggregation |
| M6 | `FormationLOSFallbackTests` | Bresenham LOS over the cost field |
| M7 | `BurstAttributeAuditTest` | every IJob* in the Navigation namespace carries [BurstCompile] |
| M7 | `DeterminismIntegrationSweepTest` | 100 repeat runs of IntegrationDijkstraJob produce identical output |
| M7 | `DeterminismAStarRepeatTest` | 100 repeat runs of AbstractPathfinder.Solve produce identical portal sequence |
| M7 | `DeterminismSpatialHashRepeatTest` | 100 repeat runs of NavSpatialHash insertion produce identical iteration sequence |

Total: 27 EditMode test classes.

### PlayMode harness (AC-T1)

`Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- single
`[UnityTest]` that bootstraps an ECS world per phase, dispatches the
scenario's setup, ticks the SimulationSystemGroup for a per-phase
budget, and asserts the phase's success criterion. Shipped `[Ignore]`d
because the project's Build Settings is currently incomplete (the
PlayMode test runner requires at least one scene to launch).
Re-enable by removing the `[Ignore]` attribute on `RunAllPhases` once
the editor's Build Settings has the test scene wired.

### Manual scenario menu

For interactive verification, the main menu's Scenario list exposes
each phase scenario (`Phase 1 Nav Test (1 unit flat grid)` ..
`Phase 7 Nav Test (determinism replay 100 units)`). Selecting one
boots into the relevant `PhaseNTestSetup` so a developer can watch
the behaviour in the Scene view.

## Future cleanup

The `PassabilityGrid` MonoBehaviour (`Assets/Scripts/World/Terrain/`)
remains alive after M4-M7 for two reasons: (1) it carries
non-pathing features (terrain reachability BFS, geometric
Minkowski-sum, line-of-sight) the new nav stack does not yet
replicate; (2) ~13 callers use those features. Migrating those
callers and deleting the file is a separate M8-style cleanup task --
see the `// M8-followup:` markers in
`BattalionSyncSystem`, `WallGatePassabilitySystem` (post-M5 deletion
left only docstring references), and the bootstrap files that spawn
the legacy grid GameObject. The runtime is correct today; the
cleanup is a code-debt item, not a correctness gap.
