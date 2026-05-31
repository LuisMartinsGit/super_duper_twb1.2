---
deft:
  id: task-hudbridge-push-cadence-tiers-106
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

# HudBridge — per-topic push cadence + JsonEscape allocation cleanup

Spun out from [task-082 §D row 7 + row 9](../task-profound-code-review-082/task.md#§d-performance-hot-spots).

## Context

`HudBridge.Update` (every frame) once per ~33 ms (driven by
`pushHz = 30`) runs **all 9 push helpers**:
[HudBridge.cs:429-451](../../../Assets/Scripts/UI/Web/HudBridge.cs#L429):

```
PushMenu(); PushResources(); PushObjectives(); PushSelection();
PushCosts(); PushCultureChoice(); PushBuilderState();
PushSectsVisibility(); PushSects();
```

Each helper builds a JSON payload (via `_sb` StringBuilder) and calls
`PushIfChanged(topic, payload)`. `PushIfChanged` string-compares the new
payload against the last value cached in `_lastJson` — but the payload
**string is built unconditionally** before the comparison can happen.
Idle game state with no state changes still burns ~54 KB/sec on
throwaway strings.

Additionally, `JsonEscape` at [HudBridge.cs:1494](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1494)
and the helper at L670 each allocate a fresh `StringBuilder` per call.
With selection containing 5-10 escaped fields per push, that's another
~150-300 StringBuilder allocations per second on idle.

Topics have very different update characteristics:
- **`resources` / `selection`** — change often, want low latency (~10-30 Hz).
- **`menu` / `cultureChoice` / `builderState` / `sectsVisible` / `sects`** —
  change rarely (per-click / per-build event), 1-2 Hz is plenty.
- **`objectives` / `costs`** — change on tech research / age-up, 1 Hz is plenty.

## User Value

Cuts steady-state HudBridge GC pressure by ~80 %; opens room for
higher-frequency `selection` / `resources` updates without making
everything 30 Hz.

## Requirements

- R1: Replace the single `pushHz = 30` cadence with **per-topic cadences**.
  Suggested grouping:
  - High-frequency (10-30 Hz): `selection`, `resources`.
  - Medium-frequency (2-5 Hz): `costs`, `objectives`.
  - Low-frequency (~1 Hz): `menu`, `cultureChoice`, `builderState`,
    `sectsVisible`, `sects`. Push-on-event (e.g. on sect adoption) is
    preferable but a 1 Hz fallback covers the polling case.
- R2: Reuse `_sb` (the existing class-level StringBuilder) for
  `JsonEscape`. Either pass it in or hold a second `_escSb` field for
  the escape buffer.
- R3: Eliminate the second `var sb = new StringBuilder(...)` at L670
  (same fix shape — share `_sb` or `_escSb`).
- R4: No change to any `PushIfChanged` topic payload schema. React side
  must not see new fields, dropped fields, or reordered fields.

## Acceptance Criteria

- [ ] `HudBridge.Update` no longer calls all 9 push helpers at the same
      cadence — at least 3 tiers (high / medium / low).
- [ ] `JsonEscape` allocates zero `StringBuilder` instances per call
      (reuses a class-level buffer).
- [ ] HudBridge GC alloc per second drops measurably under
      `[task-103](../task-perf-measurement-backlog-103/task.md)` measurement
      (target: < 10 KB/sec on idle, down from estimated 54 KB/sec).
- [ ] React rail still updates immediately on culture choice / sect
      adoption / menu toggle (event-pushed topics).
- [ ] No flicker / staleness in the resources / selection bars at 30 Hz.

## Files to touch

- `Assets/Scripts/UI/Web/HudBridge.cs`

## Out of Scope

- `_qHall` / temple-query consolidation — covered by **task-090**
  (`hudbridge-query-consolidation`). §D row 8 (duplicate Temple query in
  `PushSectsVisibility` + `PushSects`) is subsumed under task-090 since
  it's the same scope.
- React-side reactivity changes — the JS receiver already diffs on each
  topic; no contract change needed.
- WebView interop overhead — out of scope here; that's
  `HudWebController.Push` territory.
