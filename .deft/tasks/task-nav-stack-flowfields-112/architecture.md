---
deft:
  id: task-nav-stack-flowfields-112
  type: architecture
  stage: architecture
  totalPhases: 7
  generated_at: "2026-05-31"
---

# Architecture -- Crystal Curse Navigation Stack (M1-M7)

This document is the blueprint the implementation agent consumes seven times
(once per milestone / phase). It is written so that an implementer working
only from this file + the current repo state can ship each phase without
re-deriving design decisions. Every requirement (R1-R11) and every
acceptance criterion (AC-1..AC-6, AC-P1..AC-P7, AC-T1/AC-T2) traces to a
phase below. No code in this document -- only contracts and call-site lists.

References that frame the rest of this document:
- `task.md` -- requirements + ACs
- `spec.md` -- 11-system inventory (S1-S11), build order
- `Assets/Scripts/Systems/Movement/` -- the existing NavMesh callers
- `Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs` -- gate state today
- `Assets/Scripts/Systems/Movement/WallDoorAccessSystem.cs` -- door teleport today
- `Assets/Scripts/Core/Components/NavMeshComponents.cs` -- per-unit nav state today

---

## Cross-cutting decisions

These choices apply to every phase. They are locked here so per-phase
sections can reference them without re-justification.

### CCD-1. Folder layout -- new `Systems/Navigation/` subtree

Create a new subfolder `Assets/Scripts/Systems/Navigation/` that owns
**every new file** introduced by this task (S1-S11 systems, components,
blob builders, jobs, helpers). Rationale:

- `Systems/Movement/` is the **caller** of the nav stack
  (`MovementSystem`, `BattalionSyncSystem`, `CommandQueueSystem`, etc.),
  not the producer. Mixing the legacy NavMesh files (which M4 deletes)
  with the new stack inside the same folder would force every
  M1-M3 file rename in M4 just to clean up.
- A new folder gives M4 a clean atomic delete (`Movement/NavMesh*.cs`
  out, `Navigation/*` stays), and gives Phase 7's stress / regression
  tests a clean `using TheWaningBorder.Systems.Navigation;` import to
  target.
- Namespace: `TheWaningBorder.Systems.Navigation` (matches the
  `TheWaningBorder.Systems.<Domain>` convention recorded in
  `.deft/memory/project-facts.md`, where `<Domain>` matches the
  folder name; `Movement` stays plural-free, `Navigation` follows
  suit).

Existing `Systems/Movement/` files continue to compile in their current
namespace. The `using TheWaningBorder.Systems.Navigation;` directive is
added to every caller-side migrator listed in each phase.

### CCD-2. Cell encoding -- byte-per-cell, row-major, layer slab

S1 stores cost as `NativeArray<byte> _costCells` sized
`Width * Height * LayerCount`, row-major within a layer and layer-major
across layers. Indexing helper (inlined, conceptual):

```
index(x, z, layer) = layer * (Width * Height) + z * Width + x
```

Reasoning:
- Byte cost gives 256 distinct cost values; spec section 5 S1 explicitly
  allows `byte/ushort` and 256 is more than enough for slope-bucket +
  terrain-multiplier blends. ushort is rejected (doubles memory for no
  gameplay value at 1024^2; 2 MB vs 1 MB per layer).
- Row-major within a layer keeps `+x` and `+z` neighbour walks
  cache-friendly for the integration sweep (S5) and steering hash (S2).
- Layer-major (one full plane per layer) is preferred over an interleaved
  context dimension because:
  - M5 introduces Rampart as a second layer **late**; the slab layout
    lets M1-M4 allocate `LayerCount=1` and let M5 grow to `LayerCount=2`
    by re-allocating (one-time, deterministic) without touching the
    indexing API.
  - Tile dirty masks stay per-layer (a wall placement dirties only the
    layers it intersects -- ground footprint on layer 0, deck slab on
    layer 1).

Special cell values (sentinels on the byte cost):
- `0` -- nominal walkable
- `1..200` -- weighted walkable (terrain blend)
- `254` -- conditional passable (gate cell -- per-query check)
- `255` -- impassable

A companion `NativeArray<byte> _flagCells` of the same size carries flags
(`OwnerBits = lo nibble`, `IsGate / IsClimbAccess / IsBuildingFootprint
/ IsStaticWall` in the hi nibble). Per-cell owner only stored for cells
where the cost byte is `254` (gates); other cells leave the nibble at 0.

### CCD-3. Direction encoding -- 8-bit / 256-direction table

S5 stores flow direction as `byte` per cell, indexing a static lookup
table of `256` unit vectors (precomputed `float2[256]` at world init,
stored as a `BlobAssetReference<DirectionTable>` so the table is read by
every job through the same Burst-friendly handle). Reasoning:

- Per spec section 5 S5: "store 8-bit / 256 directions" is the
  explicit recommendation, picked here.
- 4-bit / 16-direction was rejected: with 16 directions the angular
  step is 22.5 deg, large enough that flow lines visibly stagger across
  a 1 m cell -- the steering blend in S7 then has to smooth more,
  which costs more than the byte halving saves.
- Sentinel: `255` means "no direction here" (cell is the goal or
  unreachable). Steering treats this as zero-flow and falls back to
  cohesion + separation only.

### CCD-4. Tile size -- 16x16 cells locked

Per spec section 5 S3: "fixed tiles (start 16x16 cells)". This task
locks 16x16 for **all 7 phases**. Reasoning:

- 16x16 = 256 cells per tile -- one A* abstract node per tile, ~32 tiles
  along each axis on a 512x512 map, ~64 along each axis on 1024x1024.
  Total abstract nodes <= 4096 -- comfortably within an A* open-set
  array on the stack-ish `Allocator.TempJob`.
- A 16x16 tile fits a single Burst `IJob` inner loop in L1 cache.
- Phase 4's dirty-tile rebuild touches at most a 3x3 ring of tiles
  around a placed building -- 9 * 256 = 2304 cells re-stamped, sub-ms
  in Burst.
- Phase 7's determinism replay does not require tile-size tuning;
  locking 16x16 removes a tuning variable from the audit.

### CCD-5. BlobAssetReference swap protocol (async-safety)

The portal graph (S3) lives in a single
`BlobAssetReference<PortalGraphBlob>` held by a singleton
`NavGraphSingleton : IComponentData`. M3-M7 jobs that read the graph
take the handle by value at schedule time.

The swap protocol on incremental rebuild (M4+):
1. M4's rebuild job runs as a single `IJob` (not parallel) that
   reads the **old** blob + the dirty-tile list + new portal data,
   and writes a **new** blob.
2. Before publishing the new blob to the singleton, the system calls
   `state.Dependency.Complete()` on the current frame's nav
   dependencies (the previous frame's A* / flow jobs MUST be drained
   first). This is the only point in the pipeline where a sync-point
   is intentional.
3. Old blob is `Dispose()`d **after** the new handle is assigned
   to the singleton (not before).
4. S9's request scheduler tracks per-request "graph generation":
   any in-flight A* request enqueued against the old graph is
   re-issued against the new graph on the next tick (S9 stamps the
   generation index on enqueue; the dispatcher rejects requests
   whose stamp != current).

Determinism note: the swap happens **between** sim ticks (end of
`SimulationSystemGroup`), never mid-tick. All nav jobs schedule under
`SimulationSystemGroup` so the swap is observable only at the next
tick's start.

### CCD-6. Test-asmdef plan

This task creates **three** new `.asmdef` files (the project has none
under `Assets/Scripts/` today -- the runtime code compiles into
`Assembly-CSharp`):

1. `Assets/Tests/PlayMode/TheWaningBorder.Tests.PlayMode.asmdef`
   - `references`: `["Unity.Entities", "Unity.Mathematics",
     "Unity.Collections", "Unity.Burst", "Unity.Transforms",
     "UnityEngine.TestRunner", "UnityEditor.TestRunner"]`
   - `defineConstraints`: `["UNITY_INCLUDE_TESTS"]`
   - `optionalUnityReferences`: `["TestAssemblies"]`
   - **No explicit assembly reference to `Assembly-CSharp`** --
     instead, `autoReferenced: true` so the test asmdef sees the
     default assembly (PlayMode tests live in their own DLL but
     Unity wires the runtime DLL automatically when `autoReferenced`
     is true).
2. `Assets/Tests/EditMode/TheWaningBorder.Tests.EditMode.asmdef`
   - Same shape as PlayMode, except `includePlatforms: ["Editor"]`.
3. (Conditional, only if `autoReferenced` doesn't pick up
   `Assembly-CSharp` cleanly): a wrapper asmdef
   `Assets/Scripts/TheWaningBorder.Runtime.asmdef` to give the
   runtime an explicit name the tests can reference. **Avoid this
   if possible** -- it forces every existing `.cs` file under
   `Assets/Scripts/` to live in the new assembly, and may shake
   loose unrelated cross-file compile issues. Implementer should
   first try `autoReferenced: true` without (3); add (3) only if
   the test compile fails.

### CCD-7. PlayMode harness shape

`Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` exposes one
`[UnityTest]` method:

```
[UnityTest]
public IEnumerator NavStack_AllPhases()
{
    yield return RunScenario(ScenarioType.Phase1Test);
    yield return RunScenario(ScenarioType.Phase2Test);
    yield return RunScenario(ScenarioType.Phase3Test);
    yield return RunScenario(ScenarioType.Phase4Test);
    yield return RunScenario(ScenarioType.Phase5Test);
    yield return RunScenario(ScenarioType.Phase6Test);
    yield return RunScenario(ScenarioType.Phase7Test);
}
```

`RunScenario(ScenarioType)`:
1. Sets `GameSettings.Mode = GameMode.Scenario` +
   `GameSettings.ActiveScenario = type`.
2. Bootstraps its own ECS `World` via
   `DefaultWorldInitialization.Initialize($"NavTest_{type}")` so the
   test does not depend on Build Settings scenes or scene prefabs.
3. Calls the relevant `PhaseNTestSetup.Build(world)` to spawn
   that phase's entities + manager singletons + flat test terrain.
4. Yields fixed-step ticks (`world.GetExistingSystemManaged<
   SimulationSystemGroup>().Update()` in a loop) until the
   phase's success predicate fires OR a per-phase timeout
   (Phase 1: 200 ticks; Phase 3: 800 ticks; others tuned per
   `## Phase N` section below).
5. Asserts the phase's success criterion via NUnit `Assert`.
6. Tears down the world before returning.

The test does NOT call `UnityEditor.SceneManagement.*` or load
scenes; it is a pure ECS-world test. This is the AC-T1 contract.

---

## Phase 1 (M1) -- One unit moves

**Scope (from spec / task):** S1 single-layer cost field + whole-map
integration field + S6 flow following. One Swordsman click-to-move on a
flat 64x64 grid. No hierarchy, no steering. Targets R6 (architecture
shape) and lays the data foundation for every later phase.

**Estimated effort:** Medium.

### 1.1 Components / Blobs

All components added to global namespace under
`Assets/Scripts/Core/Components/NavComponents.cs` (a new file; matches
the existing `NavMeshComponents.cs` convention).

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `NavGridSingleton` | `IComponentData` (singleton) | `int Width; int Height; float CellSize; float3 Origin; int LayerCount;` | Singleton entity created once at world init by `NavGridBootstrapSystem`. |
| `NavCostField` | `IComponentData` (singleton) | `NativeArray<byte> Cost; NativeArray<byte> Flags; int Width; int Height; int LayerCount; int Generation;` | `Allocator.Persistent`, owned by `NavGridBootstrapSystem`, disposed in its `OnDestroy`. |
| `NavFlowFieldM1` | `IComponentData` (singleton) | `NativeArray<byte> Dir; NativeArray<ushort> Integration; int Width; int Height; int Generation; int2 Goal; byte Valid;` | `Allocator.Persistent`, owned by `WholeMapFlowFieldSystem`, disposed in its `OnDestroy`. **Phase 1-only**; replaced by per-tile cache in M3. |
| `NavFollowState` | `IComponentData` (per unit) | `int2 LastSampledCell; byte CurrentGoalValid; int2 GoalCell;` | Per-entity component, added lazily by `FlowFollowSystem` when a unit first acquires `DesiredDestination`. |
| `DirectionTable` (blob) | `BlobAssetReference<DirectionTableBlob>` | `BlobArray<float2> Dirs` (256 entries) | One-time build at `NavGridBootstrapSystem.OnCreate`, lives until world tear-down. |

Determinism:
- Cost values are bytes -- no float math.
- The integration field is `ushort` (0..65535 = "tile-cost units to
  goal"), not float, per spec R7.
- Direction table is build once and read-only forever; no per-tick
  float ops touch it.

### 1.2 Systems

All new systems live under `Assets/Scripts/Systems/Navigation/` in
namespace `TheWaningBorder.Systems.Navigation`. Update groups + ordering:

| System | Type | Group / Ordering | Jobs Scheduled | ECB target |
|--------|------|--------------------|----------------|--------------|
| `NavGridBootstrapSystem` | `ISystem` (Burst) | `InitializationSystemGroup` | `BuildDirectionTableJob` (single `IJob`, runs once); `StampTerrainCostJob` (`IJobParallelFor` over rows) | none (singleton allocs in `OnCreate`) |
| `CostFieldStampSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(NavGridBootstrapSystem))]` | `StampBuildingFootprintJob` (`IJobEntity`, parallel) | `EndSimulationEntityCommandBufferSystem` (none in M1 -- buildings stamp via per-frame snapshot of `BuildingTag+LocalTransform+BuildingSize`; structural changes deferred). |
| `WholeMapFlowFieldSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(CostFieldStampSystem))]` | `IntegrationDijkstraJob` (`IJob` -- sequential frontier walk; parallelism is over multiple goal cells, but M1 has at most 1 goal so single-threaded), `FlowDirectionJob` (`IJobParallelFor` over cells) | none |
| `FlowFollowSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(WholeMapFlowFieldSystem))]`, `[UpdateBefore(typeof(MovementSystem))]` | `SampleFlowAndWriteDesiredDirJob` (`IJobEntity`, parallel) | none -- writes a new component `FlowDesiredDir` (added below) by ECB once. |

Additional per-unit component (added in this phase):

| Name | Kind | Fields | Notes |
|------|------|--------|-------|
| `FlowDesiredDir` | `IComponentData` | `float3 Value; byte HasValue;` | Written by `FlowFollowSystem`; read by `MovementSystem` (caller migration, see 1.5). |

Phase 1 still uses **`MovementSystem` from `Systems/Movement/`** as the
integrator -- but a small read-side migration is applied (1.5). The
NavMesh stack stays bootstrapped and live; M1 is additive.

### 1.3 Determinism notes

- Integration sweep uses a deterministic min-heap built from a
  `NativeArray<int>` keyed by `(integrationCost << 20) | cellIndex` --
  tie-break is by `cellIndex`, which is byte-row-major-stable.
- `IJobParallelFor` over cells uses `[NativeDisableParallelForRestriction]`
  only on the read-only cost array. Writes are to disjoint
  partitions (per-cell direction byte) -- no cross-thread races.
- No `SystemAPI.Time.DeltaTime` reads inside sim-affecting jobs. The
  flow build is event-driven (goal-changed), not time-stepped.
- Direction table built deterministically from
  `dir[i] = (cos(i * 2pi/256), sin(i * 2pi/256))` -- the sin/cos calls
  are at world init only, never per-tick, and produce identical bits
  on every machine (Burst math intrinsics are deterministic at the
  same Burst version, which the project ships).

### 1.4 File map

**New under `Assets/Scripts/`:**
- `Core/Components/NavComponents.cs` -- `NavGridSingleton`,
  `NavCostField`, `NavFlowFieldM1`, `NavFollowState`,
  `FlowDesiredDir`, `DirectionTableBlob` (struct only).
- `Systems/Navigation/NavGridBootstrapSystem.cs`
- `Systems/Navigation/CostFieldStampSystem.cs`
- `Systems/Navigation/WholeMapFlowFieldSystem.cs`
- `Systems/Navigation/FlowFollowSystem.cs`
- `Systems/Navigation/Jobs/BuildDirectionTableJob.cs`
- `Systems/Navigation/Jobs/StampTerrainCostJob.cs`
- `Systems/Navigation/Jobs/StampBuildingFootprintJob.cs`
- `Systems/Navigation/Jobs/IntegrationDijkstraJob.cs`
- `Systems/Navigation/Jobs/FlowDirectionJob.cs`
- `Systems/Navigation/Jobs/SampleFlowAndWriteDesiredDirJob.cs`
- `Bootstrap/Phase1TestSetup.cs` -- spawns flat 64x64 grid + one
  Blue Swordsman + sets up the singleton manager bag.

**Edited under `Assets/Scripts/`:**
- `Core/Settings/GameSettings.cs` -- add `Phase1Test = 12` to
  `ScenarioType` enum (after `AlanthorVsCrystal = 11`; the values
  matter for the lobby dropdown serialization, so explicit indices).
- `UI/Menus/MainMenuUI.cs` -- one new entry in the `scenarios[]`
  array at line ~344: `("Phase 1 Nav Test (1 unit flat grid)",
  ScenarioType.Phase1Test)`.
- `Bootstrap/ScenarioSetup.cs` -- one new case in the
  `SpawnScenarioEntities` switch dispatching to
  `Phase1TestSetup.SpawnScenarioEntities(em)`.
- `Systems/Movement/MovementSystem.cs` -- single-line read change:
  when a unit has `FlowDesiredDir.HasValue == 1`, use that as the
  primary direction source **before** the NavMesh corridor fallback.
  This is additive; existing NavMesh code stays as a fallback for
  units without the flow component (everyone else in the game).
  Concretely: insert a `HasComponent<FlowDesiredDir>` check at the
  top of the "PATHFINDING DIRECTION" section (around line 359),
  and if present and `HasValue`, set `dir` and short-circuit the
  corridor block. Do not remove the corridor block in M1.

**Tests under `Assets/Tests/`:**
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- create with
  the harness scaffolding, but only Phase 1 active (`yield return
  RunScenario(ScenarioType.Phase1Test)`; other yields commented out
  with `// TODO Phase N`).
- `Assets/Tests/PlayMode/TheWaningBorder.Tests.PlayMode.asmdef` --
  see CCD-6.
- `Assets/Tests/EditMode/NavStack/CostFieldStampTests.cs` --
  hand-authored 8x8 grid; assert that
  stamping a 2x2 building at (3,3) marks `Flags[ix(3,3,0)]` ..
  `Flags[ix(4,4,0)]` as `IsBuildingFootprint` and sets cost to 255.
- `Assets/Tests/EditMode/NavStack/FlowFieldCorrectnessTests.cs` --
  4x4 grid with a single obstacle at (2,1); assert flow from
  (0,0) toward goal (3,3) routes around the obstacle (i.e.
  the direction byte at (2,0) points "+x" toward (3,0) not "+z").
- `Assets/Tests/EditMode/TheWaningBorder.Tests.EditMode.asmdef`.

### 1.5 Caller migration list

Phase 1 keeps M1 surgical -- only one caller side is touched.

| Caller (current) | Replacement | Why |
|---|---|---|
| `MovementSystem` PATHFINDING DIRECTION block (line ~359) | If `HasComponent<FlowDesiredDir>` && `HasValue == 1`, use `FlowDesiredDir.Value` as `dir` and skip NavMesh corridor block. Else fall through to existing NavMesh behaviour. | Lets the M1 test scenario drive `dir` from the new flow without disturbing the rest of the game. |

No other callers are migrated in M1. `MoveCommandHelper.Execute` still
calls `NavMeshManager.SnapToNavMesh` -- the Phase 1 test spawns on a
flat grid where the snap is a no-op.

### 1.6 Deletion list

**None.** Per task OOS2, the NavMesh stack stays in place.

### 1.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase1Test = 12`
- **Setup script:** `Bootstrap/Phase1TestSetup.cs`
  (`static void SpawnScenarioEntities(EntityManager)`)
- **Success criterion (one sentence):** within 200 sim ticks of the
  click-to-move command, the spawned Swordsman's `LocalTransform.Position`
  is within 1.0 world units of the goal cell centre AND
  `DesiredDestination.Has == 0`.

PlayMode harness for Phase 1: spawn entity at world (4, 0, 4), issue
move to (60, 0, 60), tick until predicate true, assert position
within 1.0 of (60, _, 60).

---

## Phase 2 (M2) -- Crowds

**Scope:** Add S2 (spatial hash) + S7 (steering / local avoidance).
300 Swordsmen sharing a destination; no unit stacking. Reuses the M1
whole-map flow.

**Estimated effort:** Medium.

### 2.1 Components / Blobs

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `NavSpatialHash` | `IComponentData` (singleton) | `NativeParallelMultiHashMap<int2, NavHashEntry> Map; int Generation;` | `Allocator.Persistent`, owned by `NavSpatialHashSystem`. Cleared (`.Clear()`) every tick, capacity grown via `Capacity` setter when unit count exceeds it. |
| `NavHashEntry` | plain struct, not an `IComponentData` | `Entity Entity; float3 Position; float Radius; int OrderKey;` | Lives in the multimap above. `OrderKey = entity.Index` (deterministic insertion key for tie-break in steering force accumulation). |
| `SteeringDesiredDir` | `IComponentData` (per unit) | `float3 Value; byte HasValue;` | Written by `SteeringSystem`. Supersedes `FlowDesiredDir` as the direction source for `MovementSystem`. |

The M1 `FlowDesiredDir` becomes an **intermediate** -- M2 promotes it
to "raw flow direction before avoidance"; `SteeringSystem` consumes
`FlowDesiredDir` + neighbours and produces `SteeringDesiredDir`. The
`MovementSystem` migrator now reads `SteeringDesiredDir` first, then
falls back to `FlowDesiredDir`, then NavMesh corridor.

### 2.2 Systems

| System | Type | Group / Ordering | Jobs |
|--------|------|--------------------|-------|
| `NavSpatialHashSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(FlowFollowSystem))]` | `ClearHashJob` (single `IJob`), `PopulateHashJob` (`IJobEntity`, single-threaded -- multimap insert is not parallel-safe without sharding, single-thread keeps determinism trivially) |
| `SteeringSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(NavSpatialHashSystem))]`, `[UpdateBefore(typeof(MovementSystem))]` | `AccumulateSteeringForcesJob` (`IJobEntity`, parallel; reads hashmap via `[ReadOnly]`, writes own entity's `SteeringDesiredDir`) |

The legacy `UnitSeparationSystem` stays alive in M2 -- M2 only adds
**pre-movement** desired-direction blending. The post-movement push
in `UnitSeparationSystem` continues to handle the "two units exactly
overlapping" corner case. M2 then **reduces** the
`UnitSeparationSystem` push multiplier from `1.0` to `0.4` because
steering now does most of the work; see 2.5.

### 2.3 Determinism notes

- Hash bucket walk is over a `NativeParallelMultiHashMap` -- iterator
  order **is not deterministic by default**. The `AccumulateSteering
  ForcesJob` therefore:
  1. Walks the 3x3 neighbour ring, copies every hit into a local
     `NativeList<NavHashEntry>` (Allocator.Temp).
  2. Sorts by `OrderKey` (entity.Index).
  3. Accumulates separation / cohesion / avoidance in sorted order.
- Force accumulation uses **integer-scaled fixed-point** for the
  intermediate sum (`int3 forceAcc; forceAcc += dir * weightFixed
  Q16`), and only converts to float at the end. Avoids float
  associativity drift across threads.
- All force constants (`SeparationWeight`, `CohesionWeight`, etc.)
  are `const float` in the job, identical bits on every machine.

### 2.4 File map

**New:**
- `Core/Components/NavComponents.cs` -- extend with
  `NavSpatialHash`, `NavHashEntry`, `SteeringDesiredDir`.
- `Systems/Navigation/NavSpatialHashSystem.cs`
- `Systems/Navigation/SteeringSystem.cs`
- `Systems/Navigation/Jobs/ClearHashJob.cs`
- `Systems/Navigation/Jobs/PopulateHashJob.cs`
- `Systems/Navigation/Jobs/AccumulateSteeringForcesJob.cs`
- `Bootstrap/Phase2TestSetup.cs` -- spawns 10x30 block of 300
  Swordsmen.

**Edited:**
- `Core/Settings/GameSettings.cs` -- `Phase2Test = 13`.
- `UI/Menus/MainMenuUI.cs` -- one more entry.
- `Bootstrap/ScenarioSetup.cs` -- one more dispatch case.
- `Systems/Movement/MovementSystem.cs` -- extend the pathfinding
  direction block: read `SteeringDesiredDir` first, then
  `FlowDesiredDir`, then NavMesh corridor.
- `Systems/Movement/UnitSeparationSystem.cs` -- change
  `pushMultiplier = 1.0f` for `idle-non-battalion` case to `0.4f`
  (steering now resolves most pushes). Single literal edit at the
  PR3 three-state multiplier block.

**Tests:**
- `Assets/Tests/EditMode/NavStack/SpatialHashDeterminismTests.cs` --
  insert 100 entities at fixed positions, walk the 3x3 ring around
  cell (5,5), assert the iterator-then-sort sequence is byte-stable
  across two test runs.
- `Assets/Tests/EditMode/NavStack/SteeringForceOrderTests.cs` --
  hand-author 3 units in a tight cluster, assert the separation
  forces sum identically regardless of `entity.Index` shuffling
  (because the job sorts before accumulation).
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- enable the
  Phase 2 yield.

### 2.5 Caller migration list

| Caller (current) | Replacement | Why |
|---|---|---|
| `MovementSystem` PATHFINDING DIRECTION block | Now reads `SteeringDesiredDir` first, falls back to `FlowDesiredDir`, then NavMesh corridor. | Steering layer wins over flow raw. |
| `UnitSeparationSystem` push multiplier 1.0 -> 0.4 (idle non-battalion case) | Hard literal edit, no API change. | Steering handles the pre-movement avoidance; legacy push is now corrective only. |

### 2.6 Deletion list

**None.**

### 2.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase2Test = 13`
- **Setup script:** `Bootstrap/Phase2TestSetup.cs` -- spawns
  300 Blue Swordsmen in a 10x30 grid centred on (-30, _, 0), each
  at 1.5 m spacing.
- **Success criterion (one sentence):** within 800 sim ticks of a
  click-to-move command to (30, _, 0), at least 295 of 300 units
  have reached within 3.0 world units of their assigned arrival
  position AND for every pair of arrived units the centre-to-centre
  distance is >= `(r_i + r_j) - 0.1` (no collider overlap modulo a
  10 cm epsilon).

---

## Phase 3 (M3) -- Scale

**Scope:** S3 (portal graph + HPA*), S4 (abstract A* on the portal
graph), S5 (segmented + cached flow). 512x512 grid, single Swordsman
SW->NE corner; the whole-map flow from M1 is replaced by per-tile
segmented flow. Validates R10 scale target.

**Estimated effort:** Large.

### 3.1 Components / Blobs

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `NavGraphSingleton` | `IComponentData` (singleton) | `BlobAssetReference<PortalGraphBlob> Graph; int Generation;` | Owned by `PortalGraphSystem`. Old blob `Dispose()`d after the new handle is published per CCD-5. |
| `PortalGraphBlob` | `BlobAsset` | `BlobArray<PortalNode> Nodes; BlobArray<PortalEdge> Edges; BlobArray<int> NodeFirstEdge; BlobArray<TileMeta> Tiles; int TileSize; int TilesX; int TilesZ; int LayerCount;` (CSR layout: `NodeFirstEdge[node]` indexes start in `Edges`; `Edges[i+1].Source - Edges[i].Source == 0` defines run end -- standard CSR.) | Built once at M3 init, rebuilt incrementally from M4. Blob allocated via `BlobBuilder` against `Allocator.Persistent`. |
| `PortalNode` (in blob) | struct | `int2 Cell; int TileIndex; byte Layer; byte PortalKind; ushort OwnerBits;` | 8 bytes. `PortalKind` enum: 0=plain, 1=climb (M5), 2=gateGround (M5), 3=gateRampart (M5). |
| `PortalEdge` (in blob) | struct | `int Source; int Target; ushort Cost; ushort ProfileMask;` | 12 bytes. `ProfileMask` -- which traversal profiles this edge admits (8 bits today; large-footprint vs small-footprint, etc.). |
| `TileMeta` (in blob) | struct | `ushort FirstNode; ushort NodeCount; byte DirtyVersion;` | CSR-style index from tile to its node range. |
| `NavPathRequest` | `IComponentData` (per unit) | `int2 StartCell; int2 GoalCell; byte LayerStart; byte LayerGoal; byte ProfileIndex; byte Status; int RequestId; int Generation;` | Added when a unit gets a `DesiredDestination` whose path is longer than 1 tile. Removed on completion. |
| `NavPathResult` | `IComponentData` (per unit) + `DynamicBuffer<NavPathTile>` | header: `int Length; byte Valid; int Generation;` | Result of S4 A*. Buffer of `NavPathTile { int TileIndex; int PortalNodeFrom; int PortalNodeTo; }`. |
| `NavFlowTile` (cache key + value) | not an entity component -- lives in a `NativeHashMap<NavFlowTileKey, NavFlowTileEntry>` on `NavFlowCache` singleton | key: `(int TileIndex, int TargetPortalNode, byte ProfileIndex, int Generation)`; value: `int CellOffset; ushort IntegrationMax; byte LayerMask;` with packed integration + direction stored in two shared `NativeArray<byte>` pools | `Allocator.Persistent`, owned by `NavFlowCacheSystem`. LRU evicted -- cache cap 256 entries, dropped oldest by request tick. |
| `NavFlowCache` | `IComponentData` (singleton) | `NativeHashMap<NavFlowTileKey, NavFlowTileEntry> Entries; NativeArray<byte> DirPool; NativeArray<ushort> IntegrationPool; int PoolSize; int FreeListHead;` | Owned by `NavFlowCacheSystem`. Pools laid out as a free-list of fixed-size tile slabs (16x16x1byte = 256 bytes per slab dir + 16x16x2byte integration = 512 bytes; 256 slabs * 768 bytes ~= 196 KB resident). |

Pool sizing: a 1024^2 map = 64^2 = 4096 tiles per layer; caching
256 active tiles is ~6% coverage and matches the active-front size
for a single mass-move.

### 3.2 Systems

| System | Type | Group / Ordering | Jobs |
|--------|------|--------------------|-------|
| `PortalGraphSystem` | `ISystem` (Burst) | `InitializationSystemGroup`, after `NavGridBootstrapSystem` | M3 build: `DetectPortalsJob` (`IJobParallelFor` over tile boundaries -- one row per tile-row, no cross-tile writes), `IntraTileFloodFillJob` (`IJobParallelFor` over tiles -- per-tile flood that produces intra-portal edges), `AssemblePortalGraphBlobJob` (single `IJob` that runs `BlobBuilder` -- single-thread is mandatory for blob construction). |
| `NavRequestSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(CostFieldStampSystem))]`, `[UpdateBefore(typeof(AbstractPathfinderSystem))]` | Examines units with `DesiredDestination.Has == 1`; lazy-adds `NavPathRequest` (one job: `EmitNavRequestsJob`, `IJobEntity` parallel) and stamps the current generation. |
| `AbstractPathfinderSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, after `NavRequestSystem` | `RunAStarJob` (`IJobParallelFor` over a fixed budget of requests this tick -- spec S9 budget. In M3 the budget is simply `MaxRequestsPerTick = 8`; S9 hardens it in M6.) Each job invocation runs one A* on a thread-local open/closed set sized `MaxAbstractNodes = 4096`. |
| `NavFlowCacheSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, after `AbstractPathfinderSystem` | `BuildMissingTileFlowJob` (`IJobParallelFor` -- one job invocation per missing tile in the path's active window). |
| `FlowFollowSystem` (M1) | unchanged signature | unchanged group | Per-cell sample now consults `NavFlowCache` first (current tile + next tile in path), falls back to legacy whole-map flow only if no `NavPathResult` is attached. |

The M1 `WholeMapFlowFieldSystem` is **kept** in M3 but only updates
when no `NavPathRequest` is in flight (legacy fallback for units
without a path request -- M3 doesn't migrate every caller yet). M4
deletes the whole-map flow.

### 3.3 Determinism notes

- A* open set: a `NativeArray<int>` binary heap keyed by
  `(fScore << 32) | nodeIndex`. Tie-break by `nodeIndex`, which is
  stable across runs because the portal graph blob is built in a
  deterministic order (tile index ascending, intra-tile flood-fill
  visits cells in row-major).
- Cost type: edges carry `ushort` cost (0..65535). The cost
  computation in `DetectPortalsJob` uses integer Manhattan distance
  through the tile, scaled by the underlying byte cost.
- Flood-fill order: BFS from each portal cell, neighbour order
  `[+x, -x, +z, -z]` -- locked.
- The CSR `NodeFirstEdge` array is sorted by node index. Edges
  within a node's run are sorted by `Target` node index.

### 3.4 File map

**New:**
- `Core/Components/NavComponents.cs` -- extend with
  `NavGraphSingleton`, `NavPathRequest`, `NavPathResult`,
  `NavPathTile`, `NavFlowCache`, `NavFlowTileKey`,
  `NavFlowTileEntry`, `PortalNode`, `PortalEdge`, `TileMeta`,
  `PortalGraphBlob`.
- `Systems/Navigation/PortalGraphSystem.cs`
- `Systems/Navigation/NavRequestSystem.cs`
- `Systems/Navigation/AbstractPathfinderSystem.cs`
- `Systems/Navigation/NavFlowCacheSystem.cs`
- `Systems/Navigation/Jobs/DetectPortalsJob.cs`
- `Systems/Navigation/Jobs/IntraTileFloodFillJob.cs`
- `Systems/Navigation/Jobs/AssemblePortalGraphBlobJob.cs`
- `Systems/Navigation/Jobs/EmitNavRequestsJob.cs`
- `Systems/Navigation/Jobs/RunAStarJob.cs`
- `Systems/Navigation/Jobs/BuildMissingTileFlowJob.cs`
- `Bootstrap/Phase3TestSetup.cs` -- spawns 512x512 grid + 1
  Swordsman at SW corner + click to NE corner.

**Edited:**
- `Core/Settings/GameSettings.cs` -- `Phase3Test = 14`.
- `UI/Menus/MainMenuUI.cs` -- one more entry.
- `Bootstrap/ScenarioSetup.cs` -- one more dispatch.
- `Systems/Navigation/FlowFollowSystem.cs` (M1 file) -- extend to
  consult `NavFlowCache` first when the unit has a `NavPathResult`.
- `Core/Commands/CommandTypes/MoveCommand.cs` -- remove the
  `NavMeshManager.SnapToNavMesh` call (and the early `nmm` lookup);
  replace with a new helper `NavGridQuery.SnapToWalkable(em,
  destination, MoveTargetSnapRadius)` that snaps to the nearest
  passable cell on the cost field. The snap-radius constant
  (`MoveTargetSnapRadius = 30f`) moves to the new helper.
- `Core/Commands/CommandTypes/AttackMoveCommand.cs` -- same edit.
- `Systems/Navigation/NavGridQuery.cs` (NEW static helper) --
  hosts `SnapToWalkable`, `WorldToCell`, `CellToWorld`,
  `IsPassable` against the cost field singleton.

**Tests:**
- `Assets/Tests/EditMode/NavStack/PortalDetectionTests.cs` --
  hand-author a 32x32 grid (2x2 tiles) with a vertical wall down
  the middle leaving a 2-cell gap at the centre; assert the
  detected portals are exactly the 2 cells of the gap, on the
  shared tile boundary.
- `Assets/Tests/EditMode/NavStack/AbstractAStarTests.cs` --
  hand-author a `PortalGraphBlob` with 9 nodes in a 3x3 grid
  (Manhattan-connected) and a goal at the opposite corner; assert
  the returned `NavPathResult` is the lex-min shortest path.
- `Assets/Tests/EditMode/NavStack/FlowCacheReuseTests.cs` --
  request two paths sharing the same final 5 tiles; assert
  the second request reuses the same cache slabs (assert by
  `Generation` + `FreeListHead` stability).
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- enable
  the Phase 3 yield + assert no whole-map integration field
  was allocated for Phase 3's request (assert `NavFlowFieldM1.
  Valid == 0` at test end).

### 3.5 Caller migration list

| Caller (current) | Replacement | Why |
|---|---|---|
| `MoveCommandHelper.Execute` (line ~51) `NavMeshManager.SnapToNavMesh` call | `NavGridQuery.SnapToWalkable(em, destination, MoveTargetSnapRadius)` | Drops NavMesh dependency from the move-command write side. |
| `AttackMoveCommandHelper.Execute` (line ~43) `NavMeshManager.SnapToNavMesh` call | `NavGridQuery.SnapToWalkable(em, destination, MoveTargetSnapRadius)` | Same. |
| (Optional, defer to M4 if migration is risky in M3) `BattalionSyncSystem` line ~276 `nmm.SnapToNavMesh` call inside the alignment shift | leave for M4 | Reduces M3 surface area. |

### 3.6 Deletion list

**None.** All NavMesh files remain. The `NavMeshManager.SnapToNavMesh`
method stays callable (`BattalionSyncSystem` still uses it).

### 3.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase3Test = 14`
- **Setup script:** `Bootstrap/Phase3TestSetup.cs` -- 512x512 nav
  grid, spawn one Blue Swordsman at world cell (8, 8), click-to-move
  to (504, 504).
- **Success criterion (one sentence):** within 4000 sim ticks of the
  command, the unit's `LocalTransform.Position` is within 1.0 world
  units of the goal cell AND a runtime log assertion confirms that
  no `NavFlowFieldM1` allocation occurred for this scenario (the
  cache `NavFlowCache.Entries.Count()` rises monotonically as the
  unit advances, peaking below 32 active tile-slabs).

---

## Phase 4 (M4) -- Dynamic world + NavMesh stack deletion

**Scope:** S1 dirty-tracking drives S3 incremental rebuild and S5
cache invalidation. Player places / destroys a wall mid-run; pathing
adapts without stalls. **Migrates every remaining NavMesh caller**
and deletes the NavMesh stack at end of phase.

**Estimated effort:** Large.

### 4.1 Components / Blobs

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `NavDirtyTiles` | `IComponentData` (singleton) | `NativeList<int> DirtyTileIndices; NativeBitArray DirtyMask; int Generation;` | `Allocator.Persistent`, owned by `CostFieldStampSystem`. `DirtyMask` sized `TilesX * TilesZ * LayerCount`. |
| `NavGenerationCounter` | `IComponentData` (singleton) | `int CurrentGeneration; int CommittedGeneration;` | Bumped whenever a graph swap completes per CCD-5. |

No blob layout changes -- the rebuild produces a fresh
`PortalGraphBlob` from the old + dirty delta and atomically swaps.

### 4.2 Systems

| System | Type | Group / Ordering | Jobs |
|--------|------|--------------------|-------|
| `CostFieldStampSystem` (extend M1 version) | unchanged class | unchanged group | `StampBuildingFootprintJob` now also writes a "tile this cell belongs to" entry into `NavDirtyTiles.DirtyMask` whenever the byte at the stamped cell changes value. Mask writes use `Interlocked.Or` (Burst-safe) so parallelism is preserved. |
| `IncrementalPortalRebuildSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(CostFieldStampSystem))]`, `[UpdateBefore(typeof(NavRequestSystem))]` | `CollectDirtyTilesJob` (single `IJob` -- walks the mask, emits a deterministic ascending `NativeList<int>`); `RebuildDirtyTilesPortalsJob` (`IJobParallelFor` over the dirty tile list -- each invocation re-runs `DetectPortalsJob` + `IntraTileFloodFillJob` for its tile + the immediate 4-neighbour boundary); `SwapGraphBlobJob` (single `IJob` -- runs the CCD-5 swap protocol: drain dep, build new blob, publish, dispose old). |
| `NavFlowCacheSystem` (extend) | unchanged class | unchanged group | After `IncrementalPortalRebuildSystem` runs, evict cache entries whose `TileIndex` intersects the dirty mask. Single `IJob`. |

### 4.3 Determinism notes

- Dirty mask reads use `NativeBitArray` which has byte-stable
  iteration order via `CollectDirtyTilesJob` (the collector walks
  bit-indices ascending, emitting tile indices in stable order).
- The graph swap is a hard `state.Dependency.Complete()` point --
  intentionally serializing here so the new graph is observable to
  every subsequent system in the tick. This is the ONLY sync point
  in the nav pipeline.
- The blob build uses `Allocator.Persistent` directly (not `TempJob`)
  because the new blob outlives the job; the old blob's `Dispose()`
  is called from the main thread of the publishing system, not from
  a job, to keep the disposal site grep-able.

### 4.4 File map

**New:**
- `Core/Components/NavComponents.cs` -- extend with `NavDirtyTiles`,
  `NavGenerationCounter`.
- `Systems/Navigation/IncrementalPortalRebuildSystem.cs`
- `Systems/Navigation/Jobs/CollectDirtyTilesJob.cs`
- `Systems/Navigation/Jobs/RebuildDirtyTilesPortalsJob.cs`
- `Systems/Navigation/Jobs/SwapGraphBlobJob.cs`
- `Bootstrap/Phase4TestSetup.cs` -- spawns 50 Swordsmen on a
  128x128 grid + a scripted "place wall at tick 60, destroy at
  tick 240" controller (registered as a `ISystem` that runs only
  while `Phase4Test` is the active scenario).

**Edited:**
- `Core/Settings/GameSettings.cs` -- `Phase4Test = 15`.
- `UI/Menus/MainMenuUI.cs` -- one more entry.
- `Bootstrap/ScenarioSetup.cs` -- one more dispatch.
- `Systems/Movement/MovementSystem.cs` -- **delete** the NavMesh
  corridor block (the `NavMeshPathfollowState` / `NavMeshWaypoint`
  lookup, the `followingNavCorridor` flag, the `NavMesh.SamplePosition`
  off-mesh gate, the `NavMesh.SamplePosition` height snap). Replace
  the height snap with `TerrainUtility.GetHeight(nextPos.x, nextPos.z)`
  + a per-unit `LayerIndex` (M5 introduces) override.
- `Systems/Movement/BattalionSyncSystem.cs` -- delete the
  `NavMeshManager.Instance.SnapToNavMesh` block (line ~276).
- `Bootstrap/GameBootstrap.cs` -- delete the
  `NavMeshManager` GameObject creation (line 452-457).
- `Bootstrap/PathfindingTestSetup.cs` -- no edit (already a no-op
  for NavMesh).
- `Core/Commands/CommandTypes/MoveCommand.cs` -- remove the
  `using TheWaningBorder.Systems.Movement;` import (it was only
  pulling in `NavMeshManager`); the snap helper is in
  `TheWaningBorder.Systems.Navigation` after M3.

**Tests:**
- `Assets/Tests/EditMode/NavStack/DirtyTileRebuildTests.cs` --
  build a 64x64 cost field with a known graph; stamp a wall in
  cell (32, 32); assert exactly 4 tile indices are dirty (the
  cell sits at a tile corner so 4 tiles touch it) and the
  resulting graph differs from the old at exactly those 4 tiles'
  portal entries.
- `Assets/Tests/EditMode/NavStack/CacheInvalidationTests.cs` --
  prime the cache with 8 tile-slabs covering a path; mark
  one of them dirty; assert exactly 1 cache entry is evicted
  AND `FreeListHead` returns to its pre-prime value for that
  slab.
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- enable
  the Phase 4 yield + assert that the tick spanning the wall
  placement has a measured update time under
  `MaxNavTickMillis = 4.0` (S9 budget proxy for M4).

### 4.5 Caller migration list (the full sweep)

| Caller | Old | New |
|---|---|---|
| `MovementSystem` PATHFINDING DIRECTION block | NavMesh corridor walk + off-mesh gate | Deleted; only `SteeringDesiredDir` / `FlowDesiredDir` flow. |
| `MovementSystem` height snap (`NavMesh.SamplePosition` for ramp-rider) | NavMesh sample | `TerrainUtility.GetHeight` + optional per-unit `LayerIndex` (introduced in M5; M4 uses ground-only). |
| `MovementSystem` `NavStepTolerance` off-mesh check | NavMesh sample | Deleted; cost-field check via `NavGridQuery.IsPassable` instead. |
| `BattalionSyncSystem` alignment shift (line ~276) | `nmm.SnapToNavMesh` | `NavGridQuery.SnapToWalkable` |
| `MoveCommandHelper.Execute` | already migrated in M3 | -- |
| `AttackMoveCommandHelper.Execute` | already migrated in M3 | -- |
| `GameBootstrap.OnSceneLoadedHandler` -> NavMeshManager spawn | spawn NavMeshManager GameObject | deleted |
| `WallDoorAccessSystem` | depends on NavMesh height snap to teleport between islands | **stays alive in M4** -- M5 replaces it. M4 only ensures it compiles after NavMeshManager is deleted (it does -- the system doesn't import `NavMeshManager`, only `NavMesh` static API for the ground-vs-deck height test). The `UnityEngine.AI.NavMesh.SamplePosition` calls inside `WallDoorAccessSystem` are **kept for M4** -- they degrade gracefully when no nav data exists (no-op) and the system already gates on `(pos.y - terrainY) > Elevated` which is a non-NavMesh test. |
| `WallGatePassabilitySystem` | uses `PassabilityGrid.BlockBuildingRect` | unchanged -- this is `PassabilityGrid`, not NavMesh. |

### 4.6 Deletion list (M4)

At end of M4, **delete** these files:

1. `Assets/Scripts/Systems/Movement/MovementSystem.cs` -- replaced by
   `Systems/Navigation/UnitIntegratorSystem.cs` (new; the new
   integrator is a thinner version of the old MovementSystem with
   the NavMesh code excised and `SteeringDesiredDir` / cost-field
   stuck handling). The `MovementSystem.cs` filename is gone --
   downstream `[UpdateAfter(typeof(MovementSystem))]` annotations
   migrate to `[UpdateAfter(typeof(UnitIntegratorSystem))]` in
   their respective files (`BattalionSyncSystem`,
   `WallGarrisonSystem`, `UnitSeparationSystem`,
   `WallDoorAccessSystem`).
2. `Assets/Scripts/Systems/Movement/NavMeshManager.cs`
3. `Assets/Scripts/Systems/Movement/NavMeshPathRequestSystem.cs`
4. `Assets/Scripts/Systems/Movement/NavMeshConfineSystem.cs`
5. `Assets/Scripts/Systems/Movement/NavMeshStaticObstacle.cs`
6. `Assets/Scripts/Core/Components/NavMeshComponents.cs` (the
   `NavMeshPathfollowState` + `NavMeshWaypoint` types are deleted
   with their only callers).

Also delete the `.meta` siblings of every deleted `.cs`.

The Unity AI Navigation package stays in `manifest.json` (per OOS3 --
package removal is a separate cleanup task). The
`Presentation/ProceduralCliffGenerator.cs` reference to
`NavMeshStaticObstacle` is replaced with a `NavObstacleTag` Mono
component if needed (alternative: drop the reference -- cliffs are
already obstacles via `ObstacleTag` for the cost-field stamp). M4
chooses the latter to minimize migration surface.

### 4.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase4Test = 15`
- **Setup script:** `Bootstrap/Phase4TestSetup.cs` -- 128x128 grid,
  spawn 50 Blue Swordsmen at (-50, _, 0) commanded to (50, _, 0).
  A `Phase4ScriptedWallController : ISystem` (registered only when
  `ActiveScenario == Phase4Test`) places a 20-cell wall across
  the path at tick 60 and destroys it at tick 240.
- **Success criterion (one sentence):** within 1500 sim ticks, all
  50 units have reached the goal AND the tick that processed the
  wall placement reports a wall-clock duration below 4.0 ms (S9
  budget proxy) AND the cache eviction count for that tick is
  > 0 and < 32 (incremental, not full flush) AND every
  `NavMesh*.cs` file listed in 4.6 is absent from the working tree.

---

## Phase 5 (M5) -- Walls + gates (Rampart layer + S10)

**Scope:** Add Rampart layer to S1 (`LayerCount = 2`), add climb +
gate portals to S3, add S10 (layer transition / portal traversal).
Friendlies traverse, enemies are rejected at gate portals.
**Deletes** `WallDoorAccessSystem` and `WallGatePassabilitySystem`
once their portal-graph replacements ship.

**Estimated effort:** Large.

### 5.1 Components / Blobs

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `TraversalProfileBlob` | `BlobAsset` | `BlobArray<TraversalProfile> Profiles` | One-time build at world init. Profiles include: 0=GroundUnitSmall, 1=GroundUnitLarge, 2=GroundUnitClimber, 3=Siege (future). |
| `TraversalProfile` (in blob) | struct | `float Radius; byte LayerMask; byte CanClimb; byte CanGateRampart; ushort TerrainMultiplierQ8;` | 8 bytes. `LayerMask` bit 0 = ground, bit 1 = rampart. |
| `NavLayerIndex` | `IComponentData` (per unit) | `byte CurrentLayer; byte TraversalProfileIndex;` | Added in M5. Read by `UnitIntegratorSystem` (was MovementSystem) for height snap (ground = terrain, rampart = `DeckY`). |
| `PortalGraphBlob` (M3) | extended | new portal kinds 1=climb, 2=gateGround, 3=gateRampart populated by `WallPortalDetectionSystem`. The `OwnerBits` on a gate node now encodes the gate's owner faction (Faction enum bits 0-2) + open/closed state (bit 7). | -- |
| `GateRuntimeState` | `IComponentData` (per gate entity) | `byte IsOpen; Faction Owner; int PortalNodeGround; int PortalNodeRampart;` | Lazy-added to each `WallGateTag` entity by `WallGateRegistrationSystem` at gate spawn. Two portal node indices because ground + rampart toggle independently per R4. |
| `LayerTraversalState` | `IComponentData` (per unit, transient) | `byte Phase; Entity AccessStructure; int FromPortalNode; int ToPortalNode; float3 FinalDest; float TraversalProgress;` | Added when a unit reaches a portal cell of its current `NavPathResult`; removed once `TraversalProgress >= 1.0`. Replaces the legacy `WallAccessState` from `WallDoorAccessSystem.cs`. |

### 5.2 Systems

| System | Type | Group / Ordering | Jobs |
|--------|------|--------------------|-------|
| `WallPortalDetectionSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(IncrementalPortalRebuildSystem))]`, `[UpdateBefore(typeof(NavRequestSystem))]` | `EmitWallPortalsJob` (`IJobEntity` over `WallHubTag`/`WallTowerTag`/`WallGateTag`) -- writes a `WallPortalSpec` element into a `NativeList<WallPortalSpec>` shared with `IncrementalPortalRebuildSystem` for the *next* rebuild. `WallPortalSpec { int2 Cell; byte Kind; Entity SourceWall; Faction Owner; }`. The rebuilder then incorporates these as portal nodes with `PortalKind = climb / gateGround / gateRampart`. |
| `WallGateRegistrationSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, after `WallPortalDetectionSystem` | `RegisterGatePortalNodesJob` (`IJobEntity`) -- when a gate is first spawned, looks up the gate's two portal-node indices (ground + rampart) in the graph blob and writes them into the gate's new `GateRuntimeState`. |
| `GateStateSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, after `WallGateRegistrationSystem`, **replaces** `WallGatePassabilitySystem` | `UpdateGateOpenStateJob` (`IJobEntity`) -- per-gate friend-proximity poll (same 6m radius for `WallGateRegionTag`, 3m otherwise) and flips the `IsOpen` bit on `GateRuntimeState` + updates the `OwnerBits` open bit on both linked portal nodes in the **graph singleton's portal-node mutable mirror array** (a `NativeArray<ushort> _portalOwnerBitsMutable` parallel to the blob's read-only `Nodes[i].OwnerBits` -- the A* job reads from this mirror, not the blob, so flips are sync-free). |
| `LayerTransitionSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, `[UpdateAfter(typeof(UnitIntegratorSystem))]`, **replaces** `WallDoorAccessSystem` | `DetectPortalArrivalJob` (`IJobEntity`) -- when a unit's `NavPathResult` says the next portal is climb/gate AND the unit is within `ArriveDoor` (= 2.5m) of the portal cell, add `LayerTraversalState`. `AnimatePortalTraversalJob` (`IJobEntity`) -- advances `TraversalProgress` over `LayerTraversalDuration = 0.6s`, interpolates `LocalTransform.Position` between the two endpoints (animated, not teleport per AC-3), flips `NavLayerIndex.CurrentLayer` at `TraversalProgress >= 0.5`. `RejectIneligibleTraversalJob` (`IJobEntity`) -- runs first; if `PortalKind == gateGround` or `gateRampart` AND `(OwnerBits.Owner != unit.Faction || OwnerBits.IsOpen == 0)`, remove `LayerTraversalState` and re-issue a `NavPathRequest` (re-route around). This is the R4 backstop. |
| `AbstractPathfinderSystem` (M3) | extend | unchanged group | A* now: (a) reads `TraversalProfileBlob` indexed by `NavPathRequest.ProfileIndex`; (b) skips edges whose `ProfileMask & (1<<ProfileIndex) == 0`; (c) for gate portal nodes, reads `_portalOwnerBitsMutable` -- if closed for the unit's owner or owner != requesting faction, treat the edge cost as `ushort.MaxValue` (effectively impassable). |

### 5.3 Determinism notes

- The mutable `_portalOwnerBitsMutable` is updated **only** by
  `GateStateSystem`, which runs single-threaded on the main thread
  via an `IJob` so flip order is fixed (gate entities iterated by
  `entity.Index` ascending).
- `LayerTraversalState.TraversalProgress` is integrated by
  `dt = 1f / FixedTickHz`, a constant (`60 Hz` per
  `SimulationSystemGroup` fixed-step config). No `Time.deltaTime`
  in the traversal job.
- The `WallPortalSpec` list is sorted by `entity.Index` before being
  consumed by `IncrementalPortalRebuildSystem` -- ensures
  portal-node indices are deterministic across machines.

### 5.4 File map

**New:**
- `Core/Components/NavComponents.cs` -- extend with
  `NavLayerIndex`, `GateRuntimeState`, `LayerTraversalState`,
  `WallPortalSpec`, `TraversalProfileBlob`, `TraversalProfile`.
- `Systems/Navigation/WallPortalDetectionSystem.cs`
- `Systems/Navigation/WallGateRegistrationSystem.cs`
- `Systems/Navigation/GateStateSystem.cs`
- `Systems/Navigation/LayerTransitionSystem.cs`
- `Systems/Navigation/Jobs/EmitWallPortalsJob.cs`
- `Systems/Navigation/Jobs/RegisterGatePortalNodesJob.cs`
- `Systems/Navigation/Jobs/UpdateGateOpenStateJob.cs`
- `Systems/Navigation/Jobs/DetectPortalArrivalJob.cs`
- `Systems/Navigation/Jobs/AnimatePortalTraversalJob.cs`
- `Systems/Navigation/Jobs/RejectIneligibleTraversalJob.cs`
- `Bootstrap/Phase5TestSetup.cs` -- spawns Alanthor wall ring
  (one stair + one gatehouse) + 10 Blue + 10 Red Swordsmen.

**Edited:**
- `Core/Settings/GameSettings.cs` -- `Phase5Test = 16`.
- `UI/Menus/MainMenuUI.cs` -- one more entry.
- `Bootstrap/ScenarioSetup.cs` -- one more dispatch.
- `Systems/Navigation/PortalGraphSystem.cs` -- extend
  `AssemblePortalGraphBlobJob` to include the `WallPortalSpec` list
  in the new blob.
- `Systems/Navigation/AbstractPathfinderSystem.cs` -- profile-aware
  edge filter + owner/open check.
- `Systems/Navigation/NavGridBootstrapSystem.cs` -- on M5 init,
  re-allocate `NavCostField` with `LayerCount = 2`, expand the
  cost array to `Width * Height * 2`.
- `Systems/Navigation/UnitIntegratorSystem.cs` -- height snap now
  reads `NavLayerIndex.CurrentLayer`: layer 0 = `TerrainUtility.
  GetHeight`, layer 1 = `DeckY = 4.0f` (constant moved here from
  `WallDoorAccessSystem`).
- `Entities/Units/*.cs` -- unit factories add `NavLayerIndex
  { CurrentLayer = 0, TraversalProfileIndex = 0 }` on creation
  (one-line edit in each factory's `Create` -- mechanical edit
  via the `IEntityCreator` pattern documented in
  `.deft/memory/decisions.md`).

**Tests:**
- `Assets/Tests/EditMode/NavStack/GatePortalOwnerGatingTests.cs` --
  build a tiny graph with one gate portal; assert A* with
  matching-owner profile crosses, with mismatched-owner profile
  re-routes.
- `Assets/Tests/EditMode/NavStack/ClimbPortalTransitionTests.cs` --
  unit with `CanClimb = 1` reaches climb portal; assert
  `LayerTraversalState` is added, animation advances over
  `LayerTraversalDuration` ticks, `CurrentLayer` flips at midpoint.
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- enable
  the Phase 5 yield + assert 10 friendlies reach the wall ring
  AND 10 enemies are rejected at the gate AND no
  `WallDoorAccessSystem.cs` or `WallGatePassabilitySystem.cs`
  files exist after this phase.

### 5.5 Caller migration list

| Caller | Old | New |
|---|---|---|
| `Systems/Buildings/WallGatePassabilitySystem.cs` (entire system) | poll friendly proximity + flip `PassabilityGrid` cells | Replaced by `GateStateSystem` which flips `GateRuntimeState.IsOpen` + portal-node `OwnerBits`. The `PassabilityGrid.BlockBuildingRect` / `UnblockBuildingRect` calls go away (PassabilityGrid stays alive for non-pathing queries -- enclosure, spawn placement -- but no longer gates traversal). |
| `Systems/Movement/WallDoorAccessSystem.cs` (entire system, plus the `WallAccessState` ECS struct in the file's global namespace) | door-teleport bridge between ground and deck navmesh islands | Replaced by `LayerTransitionSystem` + `LayerTraversalState`. The `WallAccessState` global-namespace component is deleted with the file; no external code references it (grep confirms only `WallDoorAccessSystem.cs` reads/writes it). |
| `Systems/Buildings/WallGarrisonSystem.cs` | reads unit's `(pos.y - terrainY) > Elevated` to detect "on a deck" | reads `NavLayerIndex.CurrentLayer == 1` instead -- one-line edit. |
| `WallAutoSegmentSystem` (gate creation path) | adds `WallGateState { IsOpen = 0 }` | also adds `GateRuntimeState { IsOpen = 0, Owner = <gate.Faction>, PortalNodeGround = -1, PortalNodeRampart = -1 }` (the `-1`s are populated by `WallGateRegistrationSystem` on the next tick). |

### 5.6 Deletion list (M5)

At end of M5, **delete** these files (only after the replacements
above are passing):

1. `Assets/Scripts/Systems/Movement/WallDoorAccessSystem.cs` +
   `.meta`.
2. `Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs`
   + `.meta`.

The `WallGateState` component (in `Core/Components/BuildingComponents.cs`)
**stays** -- it carries gate metadata other systems consume (gate
visual state, upgrade UI). `GateRuntimeState` is a separate ECS
component that owns the nav-graph link. `WallGateState.IsOpen` and
`GateRuntimeState.IsOpen` are kept in sync by `GateStateSystem`
(one writes both each tick).

### 5.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase5Test = 17`
- **Setup script:** `Bootstrap/Phase5TestSetup.cs` -- builds an
  Alanthor wall ring centred at origin: 4 hubs forming a square,
  one stair access (a `WallHubTag` with a `ClimbAccess` marker), one
  5-instance gatehouse on the south edge. Spawns 10 Blue and 10 Red
  Swordsmen south of the wall. Issues all 20 to move to a point
  inside the ring.
- **Success criterion (one sentence):** within 2000 sim ticks, all
  10 Blue units have reached the inside point (via climb portal or
  gate -- mix is acceptable), no Red unit has entered the ring, all
  Red units have either reached a different position outside the
  ring or been auto-routed around it AND post-transition sampling
  on any Blue unit on the rampart returns
  `NavLayerIndex.CurrentLayer == 1`.

---

## Phase 6 (M6) -- Polish (S8 formations, mixed footprints, S9 budget)

**Scope:** S8 (formation movement -- already partly in
`BattalionSyncSystem`, M6 cleans it up against the new flow API),
extended flow + mixed sizes (S5 extension), S9 (request scheduler /
budget).

**Estimated effort:** Medium.

### 6.1 Components / Blobs

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `NavRequestQueue` | `IComponentData` (singleton) | `NativeRingBuffer<NavRequestSlot> Pending; int BudgetPerTick; int Generation; int NextRequestId;` | `Allocator.Persistent`, owned by `NavRequestSchedulerSystem`. Replaces the M3 `MaxRequestsPerTick = 8` literal. |
| `NavRequestSlot` | struct | `Entity Owner; int2 StartCell; int2 GoalCell; byte LayerStart; byte LayerGoal; byte ProfileIndex; int RequestId; int Generation; ushort Priority;` | Lives in the ring buffer. Sorted by `Priority desc, RequestId asc` for stable dispatch. |
| `FormationLeaderState` | `IComponentData` (per battalion leader, extended) | existing `BattalionLeader` fields + `int CurrentNavRequestId; byte FormationProfile;` | One-line extension. `FormationProfile = max(member.TraversalProfileIndex)` -- the "most restricted" profile per spec S5 extended-flow. |

### 6.2 Systems

| System | Type | Group / Ordering | Jobs |
|--------|------|--------------------|-------|
| `NavRequestSchedulerSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, replaces the M3 direct `EmitNavRequestsJob` -> `RunAStarJob` chain at the entry | `EnqueueRequestsJob` (`IJobEntity` parallel -- writes to per-thread `NativeStream`); `MergeAndSortQueueJob` (single `IJob` -- merges streams, dedupes by `(GoalCell, ProfileIndex)`, sorts by `Priority desc, RequestId asc`); `DispatchBudgetJob` (single `IJob` -- pops up to `BudgetPerTick` from the head, emits `NavPathRequest` components via ECB targeting `EndSimulationEntityCommandBufferSystem.Singleton`). |
| `ExtendedFlowSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, after `NavFlowCacheSystem` | `ExtendCacheForSmallerProfilesJob` (`IJobParallelFor` over cached tile slabs) -- for a tile slab built against profile P, write derivative slabs for every profile P' with strictly larger admissibility (smaller-radius units inherit P's flow but with cells re-flagged passable where the small-radius footprint fits but P didn't). |
| `FormationLeaderNavSystem` | `ISystem` (Burst) | `SimulationSystemGroup`, after `NavRequestSchedulerSystem`, before `BattalionSyncSystem` | `ComputeFormationProfileJob` (`IJobEntity` over leaders) -- walks members, sets `FormationLeaderState.FormationProfile = max(member.TraversalProfileIndex)`; only the leader emits a `NavPathRequest` (members read the leader's flow cache during M2's steering blend). |

`BattalionSyncSystem` is updated to consume the leader's
`NavPathResult` instead of NavMesh corridors; this is a straightforward
data-source swap, the formation slot logic is unchanged.

### 6.3 Determinism notes

- Ring buffer pop order is `priority desc, requestId asc` -- both
  integers, stable.
- Per-thread stream merge uses `NativeStream` -> single-thread
  consolidation, no parallel sort across threads (sort happens
  after merge on a single thread).
- Extended-flow derivation visits profiles in `ProfileIndex`
  ascending order so the derivative slabs are produced
  deterministically.

### 6.4 File map

**New:**
- `Core/Components/NavComponents.cs` -- extend with
  `NavRequestQueue`, `NavRequestSlot`, `FormationLeaderState`
  extension.
- `Systems/Navigation/NavRequestSchedulerSystem.cs`
- `Systems/Navigation/ExtendedFlowSystem.cs`
- `Systems/Navigation/FormationLeaderNavSystem.cs`
- `Systems/Navigation/Jobs/EnqueueRequestsJob.cs`
- `Systems/Navigation/Jobs/MergeAndSortQueueJob.cs`
- `Systems/Navigation/Jobs/DispatchBudgetJob.cs`
- `Systems/Navigation/Jobs/ExtendCacheForSmallerProfilesJob.cs`
- `Systems/Navigation/Jobs/ComputeFormationProfileJob.cs`
- `Bootstrap/Phase6TestSetup.cs` -- 40 small + 20 large units
  formation move.

**Edited:**
- `Core/Settings/GameSettings.cs` -- `Phase6Test = 18`.
- `UI/Menus/MainMenuUI.cs` -- one more entry.
- `Bootstrap/ScenarioSetup.cs` -- one more dispatch.
- `Systems/Navigation/NavRequestSystem.cs` (M3) -- now emits into
  the scheduler queue instead of directly creating
  `NavPathRequest` components.
- `Systems/Movement/BattalionSyncSystem.cs` -- replace any
  remaining direct path consumption with reads of the leader's
  `NavPathResult` buffer.

**Tests:**
- `Assets/Tests/EditMode/NavStack/FormationSlotDeterminismTests.cs`
  -- 60 mixed units, assert formation slot assignment is
  byte-identical across two runs.
- `Assets/Tests/EditMode/NavStack/RequestBudgetCoalesceTests.cs` --
  enqueue 50 requests to the same goal in one tick; assert exactly
  1 dispatched (coalesced) and 49 dropped from the queue.
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- enable
  the Phase 6 yield.

### 6.5 Caller migration list

| Caller | Old | New |
|---|---|---|
| `NavRequestSystem` directly adds `NavPathRequest` | direct add | enqueue via `NavRequestSchedulerSystem` |
| `BattalionSyncSystem` leader pathing | NavMesh corridor (now gone) | reads `NavPathResult` buffer on the leader, computes per-member desired position from the flow cache (`NavGridQuery.SampleFlowAtCell`) |

### 6.6 Deletion list

**None** (M6 polishes; deletions happened in M4 and M5).

### 6.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase6Test = 19`
- **Setup script:** `Bootstrap/Phase6TestSetup.cs` -- 40 small
  (Swordsman, radius 0.5) + 20 large (`Siege` placeholder, radius
  1.5) in 4 battalions of 15 mixed each, ordered across the map.
- **Success criterion (one sentence):** within 2500 sim ticks, all
  60 units reached the goal AND no large-footprint unit traversed
  a portal whose admission profile lacked the large profile bit AND
  no tick during the mass-issue exceeded the S9 per-tick budget
  (`MaxNavTickMillis = 4.0`).

---

## Phase 7 (M7) -- Hardening (determinism + S11 viz)

**Scope:** Determinism audit, S11 debug visualization (editor-only),
stress + regression tests.

**Estimated effort:** Medium.

### 7.1 Components / Blobs

| Name | Kind | Fields | Allocator / Owner |
|------|------|--------|--------------------|
| `NavReplaySnapshot` | `IComponentData` (singleton, debug only) | `NativeArray<float3> Positions; NativeArray<int> Generations; int Tick;` | `Allocator.Persistent`, owned by `DeterminismReplaySystem`. Allocated only when `Phase7Test` scenario is active or `ENABLE_NAV_REPLAY` define is set. |
| (S11) `NavDebugDrawConfig` | static MonoBehaviour singleton (not ECS) | toggles for heatmap, portals, A* path, flow vectors per layer | Editor only. |

No production blob changes in M7.

### 7.2 Systems

| System | Type | Group / Ordering | Jobs |
|--------|------|--------------------|-------|
| `DeterminismReplaySystem` | `ISystem` (Burst) | `LateSimulationSystemGroup`, end of tick | `SnapshotPositionsJob` (`IJobEntity` parallel) -- emits all `UnitTag+LocalTransform` positions into a sorted-by-`entity.Index` array; appends to a replay log if recording. On replay mode: asserts position matches the recorded byte-for-byte. |
| `NavBurstAttributeAuditSystem` | not an ECS system -- an EditMode test only | -- | runs once: reflects over `TheWaningBorder.Systems.Navigation.*` assembly and asserts every `IJobEntity` / `IJobParallelFor` / `IJob` struct carries `[BurstCompile]`. |
| `NavDebugDrawSystem` | MonoBehaviour (`#if UNITY_EDITOR`) | `OnDrawGizmos` | Reads `NavCostField` / `NavGraphSingleton` / `NavFlowCache` and renders heatmap / portals / flow arrows. **Outside the sim group** so it doesn't affect determinism. |

### 7.3 Determinism notes

- The replay log is a `NativeList<float3>` written at end of
  sim tick; **not** read by any sim system. Snapshot-vs-replay
  comparison runs in the test driver after the scripted ticks.
- Snapshot positions are sorted by `entity.Index` so the
  byte-identical check is comparable across machines (entity
  indices are stable in lockstep within a tick partition per the
  task-002 decision in `.deft/memory/decisions.md`).
- Burst attribute audit uses reflection -- runs in EditMode only,
  is itself nondeterministic across machine state but emits a
  pass/fail boolean that is deterministic given the same compiled
  assembly.

### 7.4 File map

**New:**
- `Core/Components/NavComponents.cs` -- extend with
  `NavReplaySnapshot`.
- `Systems/Navigation/DeterminismReplaySystem.cs`
- `Systems/Navigation/Jobs/SnapshotPositionsJob.cs`
- `Editor/Navigation/NavDebugDrawSystem.cs` (`#if UNITY_EDITOR`)
- `Editor/Navigation/NavDebugDrawConfig.cs` (MonoBehaviour
  singleton, Inspector tickboxes for heatmap / portals / A* / flow)
- `Bootstrap/Phase7TestSetup.cs` -- spawns 100 units, registers
  scripted command sequence (30 ticks), records snapshot, replays.

**Edited:**
- `Core/Settings/GameSettings.cs` -- `Phase7Test = 20`.
- `UI/Menus/MainMenuUI.cs` -- one more entry.
- `Bootstrap/ScenarioSetup.cs` -- one more dispatch.

**Tests:**
- `Assets/Tests/EditMode/NavStack/BurstAttributePresenceTests.cs`
  -- reflection-based scan over the
  `TheWaningBorder.Systems.Navigation` namespace; asserts every
  job type carries `[BurstCompile]`.
- `Assets/Tests/EditMode/NavStack/DeterminismReplayUnitTests.cs`
  -- run the integration sweep, A*, and steering on a fixed grid
  twice; assert byte-identical output arrays.
- `Assets/Tests/EditMode/NavStack/StressGridStampTests.cs` --
  stamp 1000 random (deterministic seed) buildings into a
  1024x1024 grid; assert dirty-tile count is bounded and rebuild
  remains under 8 ms.
- `Assets/Tests/PlayMode/NavStackAllPhasesTest.cs` -- enable
  the Phase 7 yield; the harness runs the scenario, records 30
  ticks, then re-runs and asserts byte-identical snapshot.

### 7.5 Caller migration list

**None** -- M7 adds no caller-visible API. Internal only.

### 7.6 Deletion list

If the Unity AI Navigation package removal is tackled in M7
(optional, separate task per OOS3), delete from
`Packages/manifest.json` the `"com.unity.ai.navigation"` line.
**Default M7 behaviour: do NOT touch the manifest.** Leave package
removal for a follow-up cleanup task.

### 7.7 Phase scenario

- **Enum entry:** `ScenarioType.Phase7Test = 20`
- **Setup script:** `Bootstrap/Phase7TestSetup.cs` -- 100 Blue
  Swordsmen on a 128x128 grid; scripted move-orders at ticks
  0/5/10/15/20/25 to varying destinations. Records all positions
  at tick 30 into `NavReplaySnapshot`.
- **Success criterion (one sentence):** the recorded snapshot at
  tick 30 is byte-identical when the scenario is run twice in the
  same Editor session AND the Burst-attribute audit reports zero
  unbursted nav jobs.

---

## Determinism Risk Register

One row per known float / ordering / nondeterminism risk across all 7
phases, with the mitigation that the architecture mandates.

| # | Risk | Phase introduced | Mitigation |
|---|------|------------------|------------|
| DR-1 | Float associativity in steering force accumulation | M2 | Sort neighbour list by `entity.Index`, accumulate in fixed-point `int3` Q16, convert to float only at write-out. |
| DR-2 | `NativeParallelMultiHashMap` iteration order | M2 | Always copy iterator output to a `NativeList<NavHashEntry>` and sort by `OrderKey = entity.Index` before any consumer reads it. |
| DR-3 | A* tie-break ambiguity | M3 | Key the heap as `(fScore << 32) | nodeIndex`; tie-break by `nodeIndex` which is stable across runs (portal-graph blob is built in tile-index ascending order). |
| DR-4 | Portal-graph blob node order | M3 | `AssemblePortalGraphBlobJob` runs single-thread; tiles processed in index ascending; intra-tile cells in row-major; portals on a boundary sorted by `(cellAlongBoundary, sourceTileIndex)`. |
| DR-5 | CSR edge order within a node's run | M3 | Edges within a node sorted by `Target` node index. |
| DR-6 | Dirty-mask write race in `StampBuildingFootprintJob` | M4 | `NativeBitArray` writes via `Interlocked.Or` (Burst-supported). |
| DR-7 | Graph blob swap interleaving with in-flight A* jobs | M4 | CCD-5 protocol -- hard `state.Dependency.Complete()` before swap; S9 rejects requests whose `Generation` is stale. |
| DR-8 | NavMesh height snap returning machine-dependent y | M4 (cured) | `MovementSystem` height snap deleted; `TerrainUtility.GetHeight` (deterministic integer-indexed lookup) is the only height source. |
| DR-9 | Gate state bit flip racing the A* read | M5 | Single-thread `GateStateSystem` updates `_portalOwnerBitsMutable` before any A* job for this tick is scheduled; ordering guaranteed by `[UpdateBefore(typeof(AbstractPathfinderSystem))]`. |
| DR-10 | Portal-node index drift when gates spawn / die | M5 | `WallPortalSpec` list sorted by `entity.Index` before consumption; portal-node indices stable across rebuilds for unchanged tiles (only dirty tiles re-index). |
| DR-11 | Layer-transition `TraversalProgress` integrated with `Time.deltaTime` | M5 (cured) | Always uses the fixed-step constant `1f / FixedTickHz`, never `SystemAPI.Time.DeltaTime`. |
| DR-12 | Request-queue priority ties | M6 | Sort by `Priority desc, RequestId asc`; `RequestId` is a monotonically-incrementing integer assigned in `entity.Index` ascending order during enqueue. |
| DR-13 | Extended-flow derivative-slab order | M6 | Profiles visited in `ProfileIndex` ascending; derivative slabs written in profile-major, cell-row-major order. |
| DR-14 | Per-thread `NativeStream` consolidation order | M6 | Stream consumed in fixed thread order (`stream.AsReader()` iterated by `foreachIndex` ascending) on a single thread before any sort. |
| DR-15 | Burst version drift across machines | global | The repo pins Burst via `Packages/packages-lock.json`; any nav job is `[BurstCompile]` so the same compiler ships everywhere. Burst-version mismatch becomes a build-time error, not a silent desync. |
| DR-16 | Editor-only debug viz writing to sim state | M7 | `NavDebugDrawSystem` is a MonoBehaviour in `#if UNITY_EDITOR`, runs in `OnDrawGizmos` (outside `SimulationSystemGroup`); it only READs cost / graph / flow data. |
| DR-17 | Replay log allocator outlasting the world | M7 | `NavReplaySnapshot` lives on a singleton entity; disposed in `DeterminismReplaySystem.OnDestroy`. |
| DR-18 | `Time.ElapsedTime` reads in M2 / M3 hot paths | M2/M3 | The pre-existing `UnitSeparationSystem` reads `SystemAPI.Time.ElapsedTime` for throttling; it is deterministic at fixed step but per CLAUDE.md memory the policy is "no wall-clock in sim-affecting code". Leave the throttle in (the system is deterministic at fixed step) but document: **no NEW nav system reads `Time.ElapsedTime` or `Time.DeltaTime` for sim-affecting computation**; only `SystemAPI.Time.DeltaTime` at the fixed step is allowed, and only for purely-cosmetic per-frame integration (e.g. `LayerTraversalState.TraversalProgress` -- which uses the constant `1f / FixedTickHz` instead). |
| DR-19 | `WallGatePassabilitySystem` polling float `Time.DeltaTime` | M5 (cured) | Replaced by `GateStateSystem` which polls on a tick-count modulo (every 18 ticks ~= 0.3s at 60 Hz). |
| DR-20 | `WallDoorAccessSystem` non-deterministic teleport landing y | M4/M5 (cured) | Replaced by `LayerTransitionSystem`; landing y is `TerrainUtility.GetHeight` for layer 0, `DeckY` constant for layer 1. |
