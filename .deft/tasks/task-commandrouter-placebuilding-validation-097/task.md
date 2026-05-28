---
deft:
  id: task-commandrouter-placebuilding-validation-097
  type: bug
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [commands, validation, building]
---

# CommandRouter.IssuePlaceBuilding — validate position + buildingId

## Context

Spun out from [task-082 §C.4](../task-profound-code-review-082/task.md#L1035).
Two related gaps in the place-building command path:

1. **Position bypass.** `CommandRouter.IssuePlaceBuilding` at
   [CommandRouter.cs:800](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L800)
   does NOT call `BuildCommandHelper.IsValidBuildPosition`. The UI path
   (`BuildCommandPannel`) validates beforehand; the AI path
   (`SimpleAISystem.TryBuildBuilding`) calls `IsValidBuildPosition`
   internally via `TryFindBuildPosition`. Any non-UI direct caller
   (network replay, scripted spawn, modding API, future tooling) can
   place a building on water / cliff / occupied tile.

2. **Bogus buildingId fallthrough.** `BuildingFactory.Create` at
   [BuildingFactory.cs:73](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L73)
   has `_ => CreateDefault(em, buildingId, position, faction)` as the
   switch fallback. An unknown id silently spawns a generic 500-HP
   building with `PresentationId = 100` — no validation against TechTreeDB
   on the command-router path.

Severity: `wrong-result` (silent bad placement / wrong building spawn).
Static-only — needs a unit test that posts a bad position / unknown id.

## User Value

Defense in depth. Non-UI callers can't accidentally place buildings on
illegal tiles or spawn placeholder buildings under a garbage id.

## Requirements

- R1: `CommandRouter.IssuePlaceBuilding` validates position via
  `BuildCommandHelper.IsValidBuildPosition` before queueing/executing,
  regardless of source.
- R2: `BuildingFactory.Create` rejects unknown ids (return `Entity.Null`
  + log) instead of falling through to `CreateDefault`. The factory's
  fallback path should be reserved for the explicit `CreateDefault`
  call from tests, not from the production command path.
- R3: A unit test that posts a bad position OR unknown buildingId
  through `IssuePlaceBuilding` returns failure and does not spawn an
  entity.

## Acceptance Criteria

- [ ] `IsValidBuildPosition` is called inside `IssuePlaceBuilding`.
- [ ] `BuildingFactory.Create` returns `Entity.Null` for unknown ids,
      with a `Debug.LogWarning` naming the id.
- [ ] Bad-position / unknown-id tests pass (or, if no test harness yet,
      a manual repro logs the rejection and does not spawn).

## Technical Notes

- This is two related fixes — keep them in one PR since both touch the
  same command path.
- AI path already validates internally; this task is about defending the
  public API surface for non-UI callers.
