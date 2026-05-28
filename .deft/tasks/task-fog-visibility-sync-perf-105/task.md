---
deft:
  id: task-fog-visibility-sync-perf-105
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

# FogVisibilitySyncSystem — drop FindFirstObjectByType, cache queries, reuse MaterialPropertyBlock

Spun out from [task-082 §D row 5](../task-profound-code-review-082/task.md#§d-performance-hot-spots).

## Context

`FogVisibilitySyncSystem.OnUpdate` runs every frame and has three
overlapping perf issues that compound:

1. **`Object.FindFirstObjectByType<EntityViewManager>()` every frame** at
   [FogOfWarSystem.cs:122](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L122)
   — Unity's reflection-based scene walk. `EntityViewManager.Instance`
   exists and replaces this with a static field lookup.
2. **Three `em.CreateEntityQuery(...)` calls rebuilt per frame** at
   L130 (no-fog branch), L146 (player LOS), and L176 (presentation entities)
   — same anti-pattern as §A row 8 / task-089 but in the `FogVisibilitySyncSystem`
   that runs alongside `FogOfWarSystem`.
3. **`new MaterialPropertyBlock()` allocated per entity per frame** inside
   the foreach at L206, L244, L256 — managed allocation × ~400 visuals ×
   60 Hz = 24 k MaterialPropertyBlock allocations/sec.

## User Value

Reduces per-frame GC pressure on the main thread; the
FindFirstObjectByType call alone is typically 0.1-0.5 ms per frame in
Unity 6 (depends on scene root count).

## Requirements

- R1: Replace `Object.FindFirstObjectByType<EntityViewManager>()` with
  `EntityViewManager.Instance` (or cache on `OnCreate` of the SystemBase).
- R2: Cache the three EntityQuerys in `OnCreate` via `GetEntityQuery(...)`.
- R3: Hoist `MaterialPropertyBlock` creation out of the loop — reuse a
  single instance per `OnUpdate` (the property block is just a scratch
  buffer; `renderer.SetPropertyBlock(mpb)` copies the data, so reuse is
  safe).
- R4: No behaviour change on visibility / ghost-fog rendering.

## Acceptance Criteria

- [ ] `FogVisibilitySyncSystem` does zero `FindFirstObjectByType` calls
      per frame.
- [ ] Three EntityQuerys cached as fields, built in `OnCreate`.
- [ ] At most one `MaterialPropertyBlock` instance allocated per
      `OnUpdate`, reused across all visual updates that frame.
- [ ] Player-owned visuals still always render; enemy units still hide
      outside LOS; enemy buildings still render as ghosts when
      `IsRevealed` && !`IsVisible`.
- [ ] No regression in fog-of-war disable path (`mgr == null` branch
      activates all visuals).

## Files to touch

- `Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs` (FogVisibilitySyncSystem
  class lives in the same file)

## Out of Scope

- `FogOfWarSystem` itself — that's covered by **task-089**
  (`ecs-query-caching-hot-systems`).
- Custom ghost-shader properties (the L257-261 comment-out is design
  decision territory, not perf).
- FogOfWarManager grid resolution / `Stamp` performance — that's
  separate from the GameObject sync hot loop.
