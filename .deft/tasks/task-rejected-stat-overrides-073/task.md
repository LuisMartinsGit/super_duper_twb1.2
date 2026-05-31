---
deft:
  id: task-rejected-stat-overrides-073
  type: bug
  status: completed
  stage: release
  phase: 1
  total_phases: 1
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [stats, techtree, balance]
---

# Revert rejected stat overrides — Practice Range, Longhouse

## Context

[docs/Design/Complete.md](../../../docs/Design/Complete.md) explicitly
**rejects** two stat overrides that are still live in TechTree.json:

- **Practice Range (Alanthor cultured Archery Range)**:
  [TechTree.json:1351-1378](../../../Assets/Resources/TechTree.json#L1351)
  has HP 1500 and provides 8 population. Doc §3.2 lines 913-914 (Q#3
  rejection) mandates the standard multiplier path: HP base 600 × {1.10,
  1.15, 1.20} = 660 / 690 / 720; population 0.
- **Longhouse (Feraldis cultured Barracks)**:
  [TechTree.json:1040](../../../Assets/Resources/TechTree.json#L1040)
  hardcodes HP 1400. Doc §5.7 decision #11 rejects this; mandates base 800
  × {1.10, 1.15, 1.20} = 880 / 920 / 960.

These overrides make the buildings 2.3× and 1.6× stronger than intended,
breaking balance.

## User Value

Alanthor and Feraldis play with the intended building toughness; no
unintended pop windfall for Alanthor; the cultured-rename ladder is
internally consistent across all three cultures.

## Requirements

- R1: `Alanthor_PracticeRange` HP override removed; HP comes from
  `BuildingUpgradeConfig.HpMultiplier` against base 600.
- R2: `Alanthor_PracticeRange` `provides.population: 8` removed (population 0).
- R3: `Feraldis_Longhouse` HP override removed; HP comes from
  `BuildingUpgradeConfig.HpMultiplier` against base 800.

## Acceptance Criteria

- [x] A freshly built Alanthor_PracticeRange at L1 has HP 660 (or whatever
      the multiplier path produces against base 600), not 1500.
- [x] Alanthor_PracticeRange contributes 0 to the population cap.
- [x] A freshly built Feraldis_Longhouse at L1 has HP 880, not 1400.

## Implementation Phases

### Phase 1: Strip overrides
**Scope:** Edit two JSON entries; verify no other code reads the overridden
fields directly.
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs)
(if `CreateFeraldisLonghouse` reads HP directly, route through `BuildingUpgradeConfig`).
**Verification:** In-game, select each building post-build and confirm HP +
pop match the multiplier path.
**Estimated effort:** Small

## Dependencies

None — purely data fixes. Can land independently.

## Out of Scope

- Other stat divergences with the doc — captured in the MEDIUM table of the
  full-sweep audit; tracked in [task-077](../task-code-default-drift-077/task.md).
