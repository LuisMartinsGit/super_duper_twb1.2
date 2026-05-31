---
deft:
  id: task-runai-curse-neutrality-078
  type: task
  status: active
  stage: implementation
  phase: 1
  total_phases: 2
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [runai, crystal-curse, tech]
---

# Runai Crystal-Curse neutrality — tech-gated −20 % wave aggro

## Context

[docs/Design/Complete.md §1.10 (lines 294-306)](../../../docs/Design/Complete.md)
gives Runai a Veilstride / curse-neutrality tech: when researched at the
Trader's Hall, the Crystal-Curse PvE waves aggro Runai units less often
(−20 % aggro chance per tier). Doc framing: "Runai walks the cursed lands
without provoking them."

Code state:
- The associated tech exists in TechTree.json (the `Runai_LongHaulTariffs` /
  Veilstride family is parsed by TechTreeDB).
- No aggro-reduction system exists anywhere. The CrystalCurse aggro pickers
  treat all factions equally.

## User Value

Runai gets the third leg of its identity (no walls + no houses + neutral
crystal). The curse landscape becomes a Runai-friendly travel space,
incentivizing the across-map caravan / trader-warrior style.

## Requirements

- R1: New `Runai_CrystalNeutrality` tech (rename from the current placeholder
  if needed), gated to `researchAt: TradersHall` post-age-up.
- R2: A `FactionCurseAggroModifier` component (per-faction, 0.0-1.0)
  reduces the probability that a Crystal-Curse wave picks a Runai unit as
  its target.
- R3: The existing curse-wave target picker reads this modifier and applies
  it before the random roll.
- R4: Tiered: L1 −20 %, L2 −40 %, L3 −60 % (final numbers TBD per doc).

## Acceptance Criteria

- [ ] A Runai faction without the tech draws curse aggro at the baseline
      rate.
- [ ] Researching the tech at Trader's Hall reduces curse-wave aggro on
      Runai units measurably in skirmish.
- [ ] Other cultures' aggro rates remain unchanged.

## Implementation Phases

### Phase 1: Tech + component
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
new `Core/Components/FactionCurseAggroModifier.cs`,
[ResearchSystem.cs](../../../Assets/Scripts/Systems/Research/ResearchSystem.cs).
**Estimated effort:** Small

### Phase 2: Aggro picker hook
**Files:** the existing crystal-curse wave / aggression system (likely under
[Systems/Spawning/](../../../Assets/Scripts/Systems/Spawning/) or under
`Systems/Combat/` — locate at scope stage).
**Estimated effort:** Medium

## Dependencies

- [task-069 runai-buildings-split](../task-runai-buildings-split-069/task.md)
  Phase 1 (Trader's Hall identity).
- [task-071 cultured-rename-layer](../task-cultured-rename-layer-071/task.md)
  (research-at hostname resolution).

## Out of Scope

- Per-tier numeric tuning — pick during scope stage.
