---
deft:
  id: task-clearallcommands-bazaar-coverage-098
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
  labels: [commands, runai, bazaar]
---

# CommandHelper.ClearAllCommands misses Runai bazaar wagon commands

## Context

Spun out from [task-082 §C.4](../task-profound-code-review-082/task.md#L1035).
`CommandHelper.ClearAllCommands` at
[CommandRouter.cs:930](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L930)
clears 21 different command components when a new command is issued on an
entity. But it does NOT clear `BazaarPackCommand` / `BazaarUnpackCommand`
declared at
[RunaiTradeComponents.cs:112](../../../Assets/Scripts/Core/Components/RunaiTradeComponents.cs#L112).

Issuing any other command on a Bazaar wagon mid-pack leaves the pack
command attached — the entity would re-pack on the next BazaarPackSystem
tick. `static-only — needs repro`.

Severity: `wrong-result`. Triage: `spin-out`.

Note: [task-066 (hut transform)](../task-ageup-transform-hut-066/task.md)
may rewire this whole flow; this fix should land **after** 066 if 066
is already in flight, to avoid merge conflicts.

## User Value

Bazaar wagons respond correctly to player override commands mid-pack
(e.g. "stop packing, move to safety").

## Requirements

- R1: Add `BazaarPackCommand` and `BazaarUnpackCommand` to the
  `ClearAllCommands` clear list at line 930+.
- R2: Verify no other Runai trade commands are missing from the clear
  list (`TradeMoveCommand`, `LaneJoinCommand`, etc. — enumerate all
  trade-component command types).

## Acceptance Criteria

- [ ] Both bazaar commands cleared by `ClearAllCommands`.
- [ ] Manual repro: pack a Bazaar wagon → mid-pack issue a Move command
      → wagon stops packing and moves; does not resume packing on
      arrival.

## Dependencies

- Coordinate with [task-066](../task-ageup-transform-hut-066/task.md)
  to avoid double-touch on the bazaar flow.

## Technical Notes

- One-line change in the clear list, plus a sweep for any other Runai
  trade command types missing from coverage.
