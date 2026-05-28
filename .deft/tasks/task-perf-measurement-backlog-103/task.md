---
deft:
  id: task-perf-measurement-backlog-103
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

# Performance measurement backlog — roll-up of §D `needs-measurement` rows

Spun out from [task-082 §D](../task-profound-code-review-082/task.md#§d-performance-hot-spots).
Per the parent task's scope-stage decision, every `needs-measurement` row in
§D rolls up into **one** backlog stub rather than one stub per row, so the
human can plan a single profiling pass that hits every suspected hot spot
in one Unity session.

## Context

§D of [task-profound-code-review-082](../task-profound-code-review-082/task.md)
catalogued 18 performance findings against HEAD `2ad11c1`. Seven rows are
tagged `needs-measurement` because the static-analysis signal is plausible
but the absolute frame cost is unknown. Rather than build profiling
infrastructure inside 082, those rows accumulate here. Once measurements
land, rows that show real cost graduate to a concrete fix task; rows below
noise threshold flip to `park`.

## Rows to measure

| §D row | Surface | File:line | Hypothesis |
|---|---|---|---|
| 1 | `TargetingSystem` per-tick query + 4 snapshots | [TargetingSystem.cs:68-82](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68) | 4 × N Temp arrays + spatial-hash rebuild at 60 Hz with N ≈ 300 entities. Expected ~0.3-1.0 ms per tick. Already in **task-089** scope as a fix; measurement validates priority. |
| 3 | `PresentationSpawnSystem.SyncTransforms` per-entity managed `GetComponent` lookups | [PresentationSpawnSystem.cs:1056-1115](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1056) | 3 `GetComponent` calls × 400 visuals × 60 Hz = ~72 k managed dict lookups/sec. Expected ~0.5-2.0 ms per frame. Spun out as **task-104**; measurement decides whether the cache layer is worth the complexity. |
| 6 | `FogOfWarSystem.OnUpdate` per-tick query + 4 snapshots | [FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45) | Same shape as row 1 but at a separate tick boundary. In **task-089** scope; measurement informs ordering. |
| 7 | `HudBridge.Update` push cadence — 9 topics at 30 Hz | [HudBridge.cs:429-451](../../../Assets/Scripts/UI/Web/HudBridge.cs#L429) | StringBuilder churn estimated at ~54 KB/sec on idle. Expected GC pressure rather than tick cost. Spun out as **task-106**. |
| 11 | `FeraldisRaiderPatrolSystem` per-tick enemy query | [FeraldisRaiderPatrolSystem.cs:37](../../../Assets/Scripts/Systems/AI/FeraldisRaiderPatrolSystem.cs#L37) | Same shape as row 1; cost scales with raider count. In **task-089** scope. |
| 13 | `SectActivePowerSystem` per-cast queries | [SectActivePowerSystem.cs:286](../../../Assets/Scripts/Systems/Sect/SectActivePowerSystem.cs#L286) | Cast frequency is low (multi-second cooldowns) so expected ~0 ms steady-state. Re-tagged `needs-measurement` so future cooldown-reduction patches catch the cost. |
| 18 | `TerrainUtility.GetHeight` called per visual per frame | [PresentationSpawnSystem.cs:1070](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1070) | 24 k height samples/sec just for visual placement, plus the same call hit by `MovementSystem` and `UnitSeparationSystem`. Expected ~0.2-0.5 ms per frame. Spun out as part of **task-104** scope if hot. |

## Measurement approach

- **Unity Profiler GPU/CPU timeline:** record a 60-second session at typical
  game state (~300 combatants, ~400 visuals, 2-4 buildings auto-firing).
  Capture `Update`, `OnUpdate`, and managed GC alloc per frame.
- **Deep Profile** the systems above for representative frames; isolate
  the per-frame cost of each row.
- **Allocation tracking:** use `Profiler.GetTempAllocatorSize`-equivalent
  or the new ProfilerRecorder API for `Allocator.Temp` churn.
- **HudBridge specific:** wrap each `Push*` call in a custom profiler
  marker so the 30 Hz cadence is visible in the timeline.

## Acceptance Criteria

- [ ] Each row above carries a measured value (frame ms or alloc bytes/sec).
- [ ] Rows below noise threshold (< 0.05 ms per frame, < 1 KB/sec alloc)
      get marked `park` back in §D of [task-082](../task-profound-code-review-082/task.md).
- [ ] Rows above noise threshold graduate to their respective fix tasks
      (089 / 104 / 105 / 106) with measured priority.
- [ ] One short report summarises top 3 actual hot spots by frame ms,
      regardless of static-analysis severity.

## Out of Scope

- **No code changes.** This is a measurement pass only.
- **No new tests / no CI integration.** Local Unity profiling session.
- **No comparison against pre-082 baseline.** Just measure HEAD `2ad11c1`.
