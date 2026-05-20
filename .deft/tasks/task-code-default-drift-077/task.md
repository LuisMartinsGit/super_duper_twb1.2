---
deft:
  id: task-code-default-drift-077
  type: improvement
  status: completed
  stage: release
  phase: 1
  total_phases: 1
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [stats, drift, cleanup]
---

# Stat drift sweep — C# defaults vs TechTree.json vs Complete.md

## Context

The full-sweep audit on 2026-05-19 found a consistent pattern: C# class
defaults (`Builder.DefaultHP`, `Sentinel.DefaultSpeed`, etc.) lag behind the
authoritative numbers in `TechTree.json`. TechTreeDB overrides at runtime,
so these are **latent bugs**: a TechTreeDB load failure would surface the
wrong values, and the code defaults are misleading to readers.

A second pattern: TechTree.json itself drifts from the doc on a handful of
basic stats.

## User Value

Code defaults match shipped values; whatever the loader resolves matches the
doc. No misleading numbers in source — code reads as authoritative for the
"if DB fails" path.

## Requirements

### C# default → match TechTree.json

| File | Symbol | Currently | Should be |
|---|---|---|---|
| [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs) | Hall `DefaultLoS` | 35 | 24 |
| [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs) | Barracks `DefaultHP` / `DefaultLoS` | 600 / 14 | 800 / 18 |
| [Swordsman.cs](../../../Assets/Scripts/Entities/Units/Swordsman.cs) | `DefaultSpeed` | 3.5 | 5.5 |
| [Sentinel.cs](../../../Assets/Scripts/Entities/Units/Sentinel.cs) | `DefaultSpeed` | 3.2 | 5.0 |
| [WarboarRider.cs](../../../Assets/Scripts/Entities/Units/WarboarRider.cs) | `DefaultHP` / `DefaultDamage` / `DefaultSpeed` | 200 / 20 / 5.8 | 160 / 16 / 7.0 |

### TechTree.json → match doc

| Entity | Field | Currently | Doc says |
|---|---|---|---|
| `Hut` | HP / pop / LoS | 350 / 5 / 12 | 600 / 10 / 14 |
| `GatherersHut` | HP | 400 | 800 |
| `Archer` | minAttackRange | 10 | 1 |
| `KingsCourt` | HP | 2100 | 2640 |
| `Alanthor_Wall` | Supplies | 40 | 50 |
| `Alanthor_SiegeYard` | Iron | 140 | 100 |
| `Alanthor_Smelter` | Supplies / Iron | 180 / 60 | 220 / 100 |
| `Hunter` | DamageType (in Hunter.cs:84) | Melee | Ranged (doc says ranged, throwing axes are ranged) |
| `Berserker` | ArmorType (in Berserker.cs:65) | InfantryLight | human_melee |

### Income drift

- `GathererHutIncomeSystem`: emits 90 S/min (15 / 10-s tick). Doc says 60 S/min.
  Choose: change tick to 10 / 10-s, or change cadence to 15 s. Use whichever
  matches the doc's intent best.

### Three-way cost drift (Age 0)

This row is owned by [task-065](../task-age0-techtree-alignment-065/task.md)
but listed here for completeness:

| Building | TechTree.json | BuildCosts.cs | Doc |
|---|---|---|---|
| Barracks | 150 S + 70 I | 220 S + 40 I | 220 S + 40 I |
| Archery Range | 150 S + 60 I | (n/a) | 180 S + 50 I |
| Gatherer's Hut | 120 S | (n/a) | 120 S + 10 I |

Resolve by picking the doc value and aligning both sources to it.

## Acceptance Criteria

- [x] Every row in the tables above either matches the doc or is explicitly
      excluded with a comment + decision link.
- [x] A "TechTreeDB failure" smoke test (load with the DB stubbed empty)
      shows units/buildings with stats that match what the doc says, not
      old defaults.
- [x] `GathererHutIncomeSystem` emits the doc's S/min when timed in skirmish.

## Implementation Phases

### Phase 1: Reconciliation sweep
**Scope:** Single PR; small but wide. No new mechanics — just numbers.
**Files:** See tables above.
**Estimated effort:** Medium

## Dependencies

- [task-073 rejected-stat-overrides](../task-rejected-stat-overrides-073/task.md)
  covers Practice Range HP+pop and Longhouse HP (different concern — *explicitly
  rejected* overrides rather than passive drift).

## Out of Scope

- FiendstoneKeep being declared inside Era 2 cultures rather than Era 1 main
  buildings — that's a structural JSON refactor; owned by [task-065](../task-age0-techtree-alignment-065/task.md)
  Phase 9.
