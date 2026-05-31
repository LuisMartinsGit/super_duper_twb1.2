---
deft:
  id: task-hudbridge-query-consolidation-090
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: spin-out
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [ui, web-hud, hudbridge, performance]
---

# HudBridge — consolidate per-faction Hall / Temple queries

## Context

Spun out from [task-082 §A row "HudBridge query cache"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene).

`HudBridge` push helpers each open their own one-shot
`em.CreateEntityQuery(...)` for the same per-faction queries
(local-player Hall, local-player Temple of Ridan):

- [HudBridge.cs:466](../../../Assets/Scripts/UI/Web/HudBridge.cs#L466) — `PushSectsVisibility` Temple query
- [HudBridge.cs:514](../../../Assets/Scripts/UI/Web/HudBridge.cs#L514) — `PushSects` Temple query (built fresh)
- [HudBridge.cs:703](../../../Assets/Scripts/UI/Web/HudBridge.cs#L703) — `PushBuilderState` Hall query (lazy field `_qHall`)
- [HudBridge.cs:756](../../../Assets/Scripts/UI/Web/HudBridge.cs#L756) — `PushCultureChoice` Hall query (duplicate of above)
- [HudBridge.cs:1445](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1445) — another Hall query

Push cadence is 30 Hz (`pushHz = 30f`, [HudBridge.cs:42](../../../Assets/Scripts/UI/Web/HudBridge.cs#L42)).
Multiple identical queries fire every push.

## User Value

Lower per-frame overhead in the UI bridge; one source of truth for
"the local player's Hall / Temple of Ridan" instead of five.

## Requirements

- R1: Cache local-faction Hall and Temple-of-Ridan queries as fields
  in `OnCreate`-equivalent (`Awake` or `EnsureQueriesBuilt`).
- R2: Dispose them in `OnDestroy` alongside the existing `_qNodes`
  cleanup.
- R3: Replace every duplicate `em.CreateEntityQuery` lookup with a
  helper (`TryGetLocalHall(out Entity)`, `TryGetLocalTemple(out Entity)`).
- R4: Keep `pushHz = 30` — this is a bookkeeping fix, not a cadence
  change.

## File:line anchor (inherited from §A row)

- [HudBridge.cs:514](../../../Assets/Scripts/UI/Web/HudBridge.cs#L514)
- [HudBridge.cs:703](../../../Assets/Scripts/UI/Web/HudBridge.cs#L703)

## Out of Scope

- Other HudBridge push functions that aren't Hall/Temple-bound.
- React-side payload changes.
