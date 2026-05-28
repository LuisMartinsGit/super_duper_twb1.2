---
deft:
  id: task-combat-system-namespace-cohesion-086
  type: bug
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: spin-out
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [ecs, refactor, namespaces]
---

# Namespace cohesion — fix three Combat / Core / Work systems

## Context

Spun out from [task-082 §A rows on namespace boundaries](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene).

Three module-boundary violations identified during the architecture
audit:

1. Three systems under `Assets/Scripts/Systems/Combat/` have **no
   namespace declaration** (live in global namespace):
   - [BurningGroundSystem.cs:20](../../../Assets/Scripts/Systems/Combat/BurningGroundSystem.cs#L20)
   - [MindControlSystem.cs:18](../../../Assets/Scripts/Systems/Combat/MindControlSystem.cs#L18)
   - [SummonDespawnSystem.cs:15](../../../Assets/Scripts/Systems/Combat/SummonDespawnSystem.cs#L15)

   Every other Combat system uses `namespace TheWaningBorder.Systems.Combat`.

2. [VictoryConditionSystem.cs:10](../../../Assets/Scripts/Systems/Core/VictoryConditionSystem.cs#L10)
   lives under `Assets/Scripts/Systems/Core/` but declares
   `namespace TheWaningBorder.UI.HUD` — a simulation-tier system
   labelled as UI/HUD.

3. [TempleCascadeDestroySystem.cs:9](../../../Assets/Scripts/Systems/Work/TempleCascadeDestroySystem.cs#L9)
   declares `namespace TheWaningBorder.Systems.Building` (singular)
   while every sibling under `Systems/Buildings/` uses `Buildings`
   (plural). One-letter typo.

## User Value

`using TheWaningBorder.Systems.Combat;` pulls in everything in the
folder. Grep recipes (`namespace TheWaningBorder.Systems.Combat`) find
every combat system. Module-boundary checks become straightforward.

## Requirements

- R1: Add `namespace TheWaningBorder.Systems.Combat { … }` around
  `BurningGroundSystem`, `MindControlSystem`, `SummonDespawnSystem`.
  Fix any `[UpdateAfter(typeof(...))]` references that now need the
  fully-qualified type.
- R2: Decide `VictoryConditionSystem`'s correct namespace —
  `TheWaningBorder.Systems.Core` (matches its directory) OR move the
  file to `Assets/Scripts/UI/HUD/` (matches its namespace). Pick one
  and update callsites.
- R3: Fix `TempleCascadeDestroySystem`'s typo:
  `Systems.Building` → `Systems.Buildings`. Verify no caller depends
  on the misspelled namespace.

## File:line anchor (inherited from §A rows)

- [BurningGroundSystem.cs:20](../../../Assets/Scripts/Systems/Combat/BurningGroundSystem.cs#L20)
- [VictoryConditionSystem.cs:10](../../../Assets/Scripts/Systems/Core/VictoryConditionSystem.cs#L10)
- [TempleCascadeDestroySystem.cs:9](../../../Assets/Scripts/Systems/Work/TempleCascadeDestroySystem.cs#L9)

## Out of Scope

- Renaming any system class.
- Splitting / merging system files.
- Other namespace drift (this stub covers only the three rows from
  task-082 §A).
