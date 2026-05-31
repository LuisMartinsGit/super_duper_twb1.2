---
deft:
  id: task-age1-unit-tier-ladders-079
  type: task
  status: active
  stage: scope
  phase: 0
  total_phases: 3
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [units, age-1, all-cultures, large]
---

# Age 1 unit tier ladders — fill missing L2 / L3 slots

## Context

[docs/Design/Complete.md Parts III / IV / V](../../../docs/Design/Complete.md)
specifies 3-tier unit ladders per culture per class (L1 / L2 / L3 at each
trainer building), but the code only defines L1 — sometimes with the wrong
class hosting it.

Missing per the full-sweep audit:

| Culture | Building | Missing tiers |
|---|---|---|
| Alanthor | Garrison | Swordsman L2 (Alanthor variant), Royal Guard L3 |
| Alanthor | Practice Range | Longbowman (or doc-TBD L3 ranged apex) |
| Runai | Route Guard | L2, L3 infantry (names TBD) |
| Runai | Arrowyard | L2, L3 ranged (names TBD) |
| Runai | Grazing Grounds | Cavalry Archer L2, L3 apex (names TBD) |
| Feraldis | Longhouse | Spearman / Swordsman / Royal Guard line-infantry ladder (inherited) |
| Feraldis | Thrower Camp | L2, L3 ranged (names TBD) |

The doc consistently marks these as "**(new)**" / TBD — names and exact stats
are open design questions to resolve at scope stage.

## User Value

Each culture's military ladder has a sense of progression: starter L1 →
upgraded L2 (research-gated) → apex L3 unit at the building's max level.
Removes the current "one-tier-and-done" feel.

## Requirements

- R1: Each missing tier has a TechTree entry with HP / cost / damage /
  defense / cost per doc tier conventions (final numbers TBD).
- R2: Each missing tier has a C# unit class file (or aliases an existing
  one with stat overrides) and a `UnitFactory` case.
- R3: Each trainer building's `minBuildingLevel` filter exposes the right
  tier at the right level.
- R4: AI managers know to queue the new units as the faction levels its
  buildings.
- R5: HUD train-button order is consistent: L1 enables at L1, L2 at L2, L3
  at L3.

## Acceptance Criteria

- [ ] Selecting an Alanthor Garrison at L1 shows only Spearman; at L2 shows
      Spearman + Swordsman; at L3 shows the full Swordsman + Royal Guard
      lineup.
- [ ] Same pattern for Practice Range / Route Guard / Arrowyard / Grazing
      Grounds / Thrower Camp / Longhouse with their respective ladders.
- [ ] No TechTree entries left with `**(new)**` placeholder text.

## Implementation Phases

### Phase 1: Scope — pick names + stats
**Scope:** Resolve doc TBDs. Produce a single sheet (in this task.md)
listing every new unit's stat block.
**Estimated effort:** Medium

### Phase 2: Data + factories
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
new unit `.cs` files under [Entities/Units/](../../../Assets/Scripts/Entities/Units/),
[UnitFactory.cs](../../../Assets/Scripts/Entities/Units/UnitFactory.cs),
[EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs).
**Estimated effort:** Large

### Phase 3: AI + UI wiring
**Files:** [AIMilitaryManager.cs](../../../Assets/Scripts/AI/Managers/AIMilitaryManager.cs),
[BuildCommandPannel.cs](../../../Assets/Scripts/UI/Panels/BuildCommandPannel.cs),
web HUD `Actions.jsx`.
**Estimated effort:** Medium

## Dependencies

- [task-068 alanthor-royal-stable](../task-alanthor-royal-stable-068/task.md)
  hosts the Alanthor cavalry tiers (Cataphract L1 + L2 / L3 apex).
- [task-069 runai-buildings-split](../task-runai-buildings-split-069/task.md)
  must add Grazing Grounds before the cavalry ladder can live there.

## Out of Scope

- Per-battalion upgrade UX — [task-076](../task-per-battalion-upgrades-076/task.md).
