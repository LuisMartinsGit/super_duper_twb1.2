---
deft:
  id: task-sect-unit-lever-multiplier-semantics-102
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
  labels: [sect, multiplier, balance]
---

# SectUnitLeverSystem.ApplyDelta — multiplier semantics ambiguity

## Context

Spun out from [task-082 §C.6](../task-profound-code-review-082/task.md#L1074).
At [SectUnitLeverSystem.cs:140](../../../Assets/Scripts/Systems/Sect/SectUnitLeverSystem.cs#L140):

```
float diff = 1f + (spec.HpMultiplier - 1f) * scalar;
```

where `scalar = LevelScalar(level) / LevelScalar(appliedLevel)`.

If `spec.HpMultiplier = 1.5` and the unit is being moved from level 1
(LevelScalar = 1) to level 2 (LevelScalar = 1.5):
- `scalar = 1.5`
- `diff = 1 + 0.5 × 1.5 = 1.75`

Current HP is then multiplied by 1.75×, which over-applies the buff
relative to either of the plausible design intents:
- "Level 2 = +50% on top of level 1 base" → `diff` should be
  `LevelScalar(level) / LevelScalar(appliedLevel) = 1.5`.
- "Level 2 = HpMultiplier × 1.5 of base" → needs a base-relative
  formula, not a delta over current HP.

Comments are not clear on which intent is correct.
`static-only — needs repro` with a sect-lever HP test.

Severity: `wrong-result`. Triage: `spin-out`.

## User Value

Sect HP buffs apply the multiplier the designer intended. No silent
over-buff on level-up paths.

## Requirements

- R1: Confirm the intended semantics with the design (which formula
  is right — the simple `scalar` form or a base-relative form).
- R2: Update `ApplyDelta` to match.
- R3: Add a unit test or in-engine sanity script that walks a unit
  from level 1 → 4 with a known `HpMultiplier` and verifies max HP
  at each step.

## Acceptance Criteria

- [ ] Formula matches confirmed design intent.
- [ ] Walking a unit level 1 → 4 with `HpMultiplier = 1.5` yields the
      documented HP at each step (e.g. base × 1.0 / 1.5 / 2.0 / 2.5
      OR base × 1.0 / 1.5 / 2.25 / 3.375 depending on intent).
- [ ] No unintended HP changes on the level-DOWN path (sect lever
      can reduce level).

## Edge Cases

- Sequential level-ups (1 → 2 → 3 in two ticks) should produce the
  same end state as a single 1 → 3 jump.
- Re-application during the same level (no-op) must not change HP.

## Technical Notes

- Memory references the anti-pattern: "Multiplying current component
  values by the FULL new multiplier on re-application (instead of
  applying a delta)" — this fits that family.
- Cross-ref [task-religious-unit-tier-074](../task-religious-unit-tier-074/task.md)
  for religious-unit tier interactions; the formula change should not
  break that tier ladder.
