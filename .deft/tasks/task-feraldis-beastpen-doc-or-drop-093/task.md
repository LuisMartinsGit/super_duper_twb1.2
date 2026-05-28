---
deft:
  id: task-feraldis-beastpen-doc-or-drop-093
  type: task
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feraldis, building, drift, reverse-drift]
---

# `Feraldis_BeastPen` — doc-or-drop decision

## Context

Spun out from [task-082 §B.6 row "Feraldis_BeastPen reverse-drift"](../task-profound-code-review-082/task.md#B-codevsdesign-drift).

[BuildCosts.cs:53](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L53)
registers a cost for `Feraldis_BeastPen` (`150 S + 30 I`):

```csharp
{ "Feraldis_BeastPen",       Cost.Of(supplies: 150, iron: 30) },
```

But:

- No factory branch exists in `BuildingFactory.Create` for the id.
- No `TechTree.json` entry exists for it.
- No mention in [Age_1_Feraldis.md](../../../docs/Design/Age_1_Feraldis.md),
  [Tech_Tree.md](../../../docs/Design/Tech_Tree.md), or
  [Overview.md](../../../docs/Design/Overview.md).
- No tag component (`BeastPenTag`) found in the code.

This is a code-only orphan — a half-stubbed building that never landed.

## User Value

Two outcomes both clean up technical debt:

1. **If BeastPen is a planned mechanic** (e.g. cavalry / Warboar trainer
   for Feraldis, paralleling Alanthor's Royal Stable + Runai's Grazing
   Grounds), document it in `docs/Design/Age_1_Feraldis.md` and complete
   the implementation (factory branch, TechTree.json entry, tag
   component, presentation id).
2. **If BeastPen is dead scaffolding**, drop the BuildCosts entry.

Either way, the inventory is cleaner.

## Requirements

- R1: Decide whether `Feraldis_BeastPen` is a planned design building or
  dead code. Document the decision in this task's state.json.
- R2: If keep — write the Design-doc entry (the Feraldis cavalry
  trainer is a real spec gap, see Q#3 in Age_1_Feraldis.md noting that
  Warboar Rider trains at Longhouse because Feraldis has no Royal-Stable-
  analogue. A BeastPen could fill that role, paralleling cross-faction
  cavalry-house symmetry.)
- R3: If drop — remove the `BuildCosts` entry.

## Acceptance Criteria

- [ ] Either the Design folder mentions `Feraldis_BeastPen` with stats /
      role / cost, or the `BuildCosts` entry is gone.
- [ ] No reverse-drift remains for this id.

## Technical Notes

- Doc-then-implement decision lives upstream of any code change.
- Cross-ref: task-068 (Alanthor Royal Stable) for the cavalry-house
  symmetry pattern.
