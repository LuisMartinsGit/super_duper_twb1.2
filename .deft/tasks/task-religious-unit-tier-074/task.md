---
deft:
  id: task-religious-unit-tier-074
  type: bug
  status: active
  stage: implementation
  phase: 1
  total_phases: 2
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [iconoclast, scholar, acolyte, religious-unit, game-ender]
---

# Religious-unit tier fix — Iconoclast L3/pop/cost, Scholar cost, singleton cap

## Context

[docs/Design/Complete.md §1.8 (lines 238-271)](../../../docs/Design/Complete.md)
defines the cross-faction game-ender tier: Scholar (Alanthor) / Acolyte
(Runai) / Iconoclast (Feraldis). Each is a high-cost single unit, capped at
**1 per faction**, trains from a fully-leveled Temple (L3).

Code state:

- **Iconoclast** ([TechTree.json:1191-1214](../../../Assets/Resources/TechTree.json#L1191),
  [Iconoclast.cs:59](../../../Assets/Scripts/Entities/Units/Iconoclast.cs#L59)):
  - `minBuildingLevel: 4` — **unreachable**, Temple caps at L3.
  - `Population.Amount = 4` — doc says 1.
  - Cost is missing the 30 Veilsteel component the doc specifies.
- **Scholar** ([TechTree.json:1548-1572](../../../Assets/Resources/TechTree.json#L1548)):
  - Cost `120 S + 30 C` — doc tier prescribes ≈ `300 S + 150 I + 100 C + 30 Vs`.
- **Singleton cap**: no faction-side "max 1 per faction" enforcement
  anywhere — players can train multiple of any religious unit.

## User Value

Game-enders cost what game-enders should cost; the +1-per-faction cap turns
each religious unit into a marquee decision instead of a spammable ability.
Iconoclast becomes actually trainable.

## Requirements

- R1: `Iconoclast` `minBuildingLevel` → 3 in TechTree.json.
- R2: `Iconoclast` `Population.Amount` → 1 in Iconoclast.cs.
- R3: `Iconoclast` cost gains 30 Veilsteel in TechTree.json.
- R4: `Scholar` cost in TechTree.json raised to ~`300 S + 150 I + 100 C + 30 Vs`
  to match game-ender tier (final numbers per doc §3.5 Q-resolution; pick
  values during design stage).
- R5: A new `FactionUnitLimit` enforcement system caps religious-unit
  training: at most one trained-or-alive Scholar per Alanthor faction, one
  Acolyte per Runai, one Iconoclast per Feraldis. Queue attempts that would
  exceed the cap fail with a UI toast.

## Acceptance Criteria

- [ ] At a Temple L3, the Iconoclast train button is enabled and produces a
      living unit with `Population.Amount = 1` and 30 Veilsteel deducted.
- [ ] Scholar cost in the train tooltip matches the new tier numbers.
- [ ] Attempting to queue a second Scholar / Acolyte / Iconoclast for the
      same faction while the first is alive or in-queue fails with a clear
      "1 per faction" message; the second slot doesn't queue.
- [ ] Killing the unit re-enables the train slot.

## Implementation Phases

### Phase 1: Stat / cost fixes
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
[Iconoclast.cs](../../../Assets/Scripts/Entities/Units/Iconoclast.cs).
**Estimated effort:** Small

### Phase 2: Singleton cap enforcement
**Files:** new `Systems/Training/FactionUnitLimitSystem.cs`,
[TrainingSystem.cs](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs)
(consult limits before queueing),
[HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs) (toast on reject).
**Estimated effort:** Medium

## Edge Cases

- Religious unit is the result of a saved-game load: do not panic if a faction
  already has 2+ — log a warning and tolerate (the cap applies to new training
  only).
- Iconoclast cost gain (30 Veilsteel): make sure the AI economy manager has
  a path to source Veilsteel before queueing.

## Out of Scope

- Resolving open design questions on exact Scholar cost numbers — pick during
  scope stage of this task; doc §3.5 question #2 covers it.
