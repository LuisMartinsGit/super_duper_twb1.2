---
deft:
  id: task-save-load-coverage-gap-096
  type: improvement
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [save-load, coverage, ecs]
  blocked_by: [task-save-load-system-081]
---

# Save/load component coverage gap

## Context

Spun out from [task-082 §C.3](../task-profound-code-review-082/task.md#L966).
Task-081 (the save/load pipeline) is still in `stage: scope` at HEAD `2ad11c1`
— no snapshot writer or reader has landed in `Assets/Scripts/` yet. The
audit enumerated 23 component files / ~247 component declarations in
[Assets/Scripts/Core/Components/](../../../Assets/Scripts/Core/Components/)
and marked all of them **`MISSING`** because there is no writer to verify
against.

This task is a **coverage gate** for 081: when 081's writer/reader lands,
every component in the §C.3 enumeration must be tagged
`serialized` / `derived` / `excluded-intentionally`. None may remain `MISSING`.

## User Value

Save files round-trip the full simulation state. No silent data loss
when a player reloads a mid-game save.

## Requirements

- R1: After 081's writer/reader lands, re-walk the §C.3 enumeration in
  task-082 and tag every component with its serialization status.
- R2: Any component still tagged `MISSING` is either added to the writer
  or explicitly marked `excluded-intentionally` with a one-line reason
  inline next to its row.
- R3: AI components under `Assets/Scripts/AI/Components/` are deferred
  to **after task-085** (AI managers removal) — only the surviving
  AI component set needs coverage.

## Acceptance Criteria

- [ ] No `MISSING` rows remain in §C.3 after 081 + 085 land.
- [ ] Every `excluded-intentionally` row has a justification in §C.3.
- [ ] A round-trip test (save → reload → diff) over a 5-minute sandbox
      session shows zero entity / component drift.

## Dependencies

- Blocked by [task-save-load-system-081](../task-save-load-system-081/task.md).
- Should run after [task-ai-managers-removal-085](../task-ai-managers-removal-085/task.md)
  to avoid serializing components that 085 will delete.

## Technical Notes

- The §C.3 enumeration in task-082 is the canonical input list. Do not
  re-enumerate; re-use that table.
