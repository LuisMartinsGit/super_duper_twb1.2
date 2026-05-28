---
deft:
  id: task-ai-launchattack-target-refresh-101
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
  labels: [ai, targeting, simpleai]
---

# SimpleAI — refresh attack target + exclude UnderConstruction from FindFactionBuilding

## Context

Spun out from [task-082 §C.5](../task-profound-code-review-082/task.md#L1054).
Two related correctness gaps in `SimpleAISystem`:

### Issue 1 — `TryLaunchAttack.ChooseAttackTarget` doesn't refresh on target death
[SimpleAISystem.cs:734](../../../Assets/Scripts/AI/SimpleAISystem.cs#L734)

`TryLaunchAttack` picks the closest enemy economy target once per
build-order `LaunchAttack` step. Once the step issues
`AttackMoveCommandHelper.Execute` to every idle military unit at
L739-740, the step latches as complete (returns true) and `StepIndex`
advances. If the chosen target dies during the march, units keep
attack-moving toward the snapshot `targetPos` — the build order does
not re-issue a fresh target. Units still engage things on the way
(attack-move semantics), so degraded but not broken.

Severity: `degraded`.

### Issue 2 — `FindFactionBuilding<TTag>` includes UnderConstruction for non-Hall callers
[SimpleAISystem.cs:799](../../../Assets/Scripts/AI/SimpleAISystem.cs#L799)

The helper returns `entities[i]` for the first faction match — it does
NOT filter out `UnderConstruction`. Callers like `TryAgeUp` at L518-519
check `UnderConstruction` only on the Hall. But other call sites
(e.g. `AssignIdleMiners.dropoff = FindFactionBuilding<HallTag>` at
L898) don't, so the AI might dispatch miners to a Hall still
mid-construction. Mining systems re-find a deposit on bounce, but the
destination is the half-built Hall's position.

`FactionHasChoiceBuilding` at L824 uses `GetCompletedFactionChoiceBuilding`
which DOES exclude — proves the pattern is known.

`static-only — needs repro` with half-built Hall + idle miner scenario.

Severity: `degraded`.

## User Value

AI plays slightly better:
- Attack waves redirect when the original target dies.
- Idle miners don't stall walking to half-built halls.

## Requirements

- R1: `TryLaunchAttack` re-evaluates target if `targetEntity` is dead
  or `Existence.Exists == 0` before issuing the attack-move on each
  re-check (or split into a multi-step build order with target refresh
  between steps).
- R2: `FindFactionBuilding<TTag>` accepts a `requireCompleted` parameter
  (default `true`) and filters out `UnderConstruction` matches.
- R3: Audit all call sites of `FindFactionBuilding<>` and decide which
  ones want completed-only vs include-construction.

## Acceptance Criteria

- [ ] Repro test: AI launches attack on enemy economy → target dies
      mid-march → AI re-targets to next-closest economy entity within
      one AI tick.
- [ ] Repro test: half-built Hall + idle miner — miner is NOT dispatched
      to the half-built Hall; either waits or routes to the completed
      Hall if one exists.

## Technical Notes

- Both issues live in the same SimpleAISystem refactor unit — keep them
  together to amortize the test scaffolding.
- AIBootstrap allocates dead-AI manager state (per §A row 1 / task-085);
  this task should land BEFORE 085 deletes those files so the SimpleAI
  refactor is the source of truth.
