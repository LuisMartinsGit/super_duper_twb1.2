---
deft:
  id: task-alanthor-crucible-cost-fix-092
  type: bug
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: high
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [alanthor, crucible, cost, veilsteel, chicken-and-egg]
---

# Alanthor Crucible cost — remove chicken-and-egg Veilsteel requirement

## Context

Spun out from [task-082 §B.4 row "Alanthor_Crucible chicken-and-egg cost"](../task-profound-code-review-082/task.md#B-codevsdesign-drift).
[Age_1_Alanthor.md §Crucible](../../../docs/Design/Age_1_Alanthor.md#L385)
flags the current build cost as a code bug:

> ⚠ The 30 Veilsteel build cost in [BuildCosts.cs](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs) creates a
> **chicken-and-egg problem** — you need a Crucible to forge Veilsteel,
> but you need Veilsteel to build a Crucible. Likely a code bug.

Code:
- [BuildCosts.cs:70](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L70):
  `{ "Alanthor_Crucible", Cost.Of(supplies: 300, crystal: 80, veilsteel: 30) }`
- [TechTree.json:1465-1469](../../../Assets/Resources/TechTree.json):
  `"cost": { "Supplies": 200, "Iron": 60, "Crystal": 40 }`

TechTree.json's number is sane (200 S + 60 I + 40 C). `BuildCosts.cs` is
the authoritative runtime source per Q#9 in `Age_1_Alanthor.md`, but for
this specific row BuildCosts is wrong. Either harmonize on the
TechTree.json value (200/60/40) or pick the user-design value, but the
Veilsteel must be removed.

## User Value

Alanthor players can build their first Crucible and start the Veilsteel
production chain. Currently they can't, blocking all Veilsteel-tier
content for the culture.

## Requirements

- R1: Update `BuildCosts.cs` for `Alanthor_Crucible` to drop the Veilsteel
  requirement.
- R2: Reconcile against `TechTree.json` so both sources agree on the final
  cost.
- R3: Verify `SyncFromTechTree()` doesn't re-introduce the bad value at
  runtime (the sync direction is TechTree.json → BuildCosts; the JSON
  number must be the canonical 200/60/40).

## Acceptance Criteria

- [ ] `BuildCosts.Get("Alanthor_Crucible").Veilsteel` returns 0.
- [ ] In a sandbox match, an Alanthor player with no Veilsteel income can
      build their first Crucible.
- [ ] No regression for the Smelter (separate building, separate cost).

## Technical Notes

- One-line change to `BuildCosts.cs`.
- Inherits file:line anchor from [task-082 §B.4](../task-profound-code-review-082/task.md#B-codevsdesign-drift).
