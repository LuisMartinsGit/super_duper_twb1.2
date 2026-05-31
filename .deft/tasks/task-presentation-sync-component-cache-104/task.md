---
deft:
  id: task-presentation-sync-component-cache-104
  type: improvement
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: parent-task-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# PresentationSpawnSystem — cache per-visual components, drop per-frame List alloc

Spun out from [task-082 §D row 3 + row 4](../task-profound-code-review-082/task.md#§d-performance-hot-spots).

## Context

`PresentationSpawnSystem.SyncTransforms` runs every frame at 60 Hz and for
**every spawned visual** (~400 entities in a mid-game RTS session) calls
three managed `GetComponent` lookups —
`GetComponent<BuildingVisualSinkDepth>`,
`GetComponent<BuildingRiseData>`, and
`GetComponent<ProceduralScaleTag>` (file anchor:
[PresentationSpawnSystem.cs:1080-1111](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1080)).
That's ~72 000 managed dictionary lookups per second on the main thread
just to place visuals on the terrain.

`PresentationSpawnSystem.CleanupDestroyedEntities` (same `Update()` tick)
allocates a fresh `List<Entity> toRemove` every frame at
[PresentationSpawnSystem.cs:212](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L212)
even when the cleanup pass finds nothing — managed allocation × 60 Hz.

Both findings live in the same file and the same `Update()` tick, so they
fix together.

## User Value

Frees main-thread CPU budget that's currently spent in repeated
GetComponent dictionary lookups, and stops the per-frame managed
allocation that contributes to GC pauses.

## Requirements

- R1: Replace the three per-entity `GetComponent` calls in
  `SyncTransforms` with cached component references. Suggested approach:
  add an `EntityVisualMetadata` struct (or sidecar `Dictionary<Entity, …>`)
  populated when a visual is spawned in `SpawnMissingVisuals`, and read
  the cached refs in `SyncTransforms` instead of `GetComponent`.
- R2: Replace the per-frame `new List<Entity>()` in
  `CleanupDestroyedEntities` with a persistent field cleared each tick,
  or a `NativeList<Entity>(Allocator.Temp)`.
- R3: No behaviour change. Visuals that don't have one of the three
  components must continue to fall through to the default branches
  (sink depth = radius × 2, no rise data → manual y-offset, no scale tag
  → 1.0 base scale).
- R4: No regression on construction rise / level-up flourish animation.

## Acceptance Criteria

- [ ] `SyncTransforms` does zero `GameObject.GetComponent` calls per frame
      for the visual positioning hot path (excluding renderer queries that
      live outside this loop).
- [ ] `CleanupDestroyedEntities` allocates no managed memory per frame
      when no entities are dying.
- [ ] Building rise animation still plays bottom-to-top during
      construction; rise data spawn → ApplyRise → NotifyConstructionComplete
      flow unchanged.
- [ ] ProceduralScaleTag base-scale multiplier still applies to procedural
      units.
- [ ] No change to `PresentationSpawnSystem.SpawnMissingVisuals` /
      `SpawnVisual` public shape.

## Files to touch

- `Assets/Scripts/Presentation/PresentationSpawnSystem.cs` (caller)
- `Assets/Scripts/Presentation/EntityViewManager.cs` (may extend to hold
  the cached refs alongside the GameObject view)
- New: small `EntityVisualMetadata` sidecar struct (single-file addition)

## Out of Scope

- TerrainUtility.GetHeight optimisation — that's §D row 18, rolled into
  the measurement backlog [task-103](../task-perf-measurement-backlog-103/task.md).
- Renderer / MaterialPropertyBlock pooling — that's `FogVisibilitySyncSystem`
  in [task-105](../task-fog-visibility-sync-perf-105/task.md).
