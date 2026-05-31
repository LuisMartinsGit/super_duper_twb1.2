---
deft:
  id: task-feraldis-raider-rebuild-067
  type: bug
  status: active
  stage: implementation
  phase: 2
  total_phases: 2
  priority: critical
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feraldis, raider, unit, uncontrollable]
---

# Feraldis Raider rebuild — split from Runai_Raider cavalry

## Context

[docs/Design/Complete.md §5.3 (lines 1726-1766)](../../../docs/Design/Complete.md)
specifies Feraldis Raiders as **uncontrollable infantry skirmishers** auto-
spawned by Feraldis Houses on every build / upgrade tick (HP 80, speed 6.0,
skirmisher armor class).

The code's `Raider` unit is something else entirely.
[Raider.cs:14-50](../../../Assets/Scripts/Entities/Units/Raider.cs#L14) loads
the `Runai_Raider` TechTree entry — Runai's fast cavalry (HP 150, speed 7.2,
trains at Grazing Grounds). There is no `Feraldis_Raider` unit, no
uncontrollable flag on the entity, and no spawn-from-House wiring.

This is a fundamental misalignment: the doc's "Feraldis Raider" and code's
`Raider` describe two different units that happen to share a name.

## User Value

Feraldis gets its signature aggression tool: cheap disposable harassers that
spawn automatically from the population economy. The Runai fast-cavalry
Raider remains intact under its proper name.

## Requirements

- R1: A new `Feraldis_Raider` TechTree entry exists with the doc's stats
  (HP 80, speed 6.0, skirmisher melee, no train button — auto-spawn only).
- R2: A separate `Runai_Raider` continues to exist with current stats
  (HP 150, speed 7.2, fast cavalry, trained at Grazing Grounds).
- R3: The C# `Raider.cs` factory is split — `Feraldis_Raider` and
  `Runai_Raider` resolve to different stat blocks. Existing call sites in
  AI managers, EntityExtractors, save migrations are updated.
- R4: Feraldis Raiders carry a `NotControllableTag` (existing pattern, see
  [Caravan.cs:79](../../../Assets/Scripts/Entities/Units/Caravan.cs#L79));
  [SelectionSystem.cs](../../../Assets/Scripts/Input/SelectionSystem.cs)
  excludes them from any player drag-select.
- R5: A simple aggressive-patrol AI behavior drives Feraldis Raiders to seek
  the nearest enemy unit / building.

## Acceptance Criteria

- [x] `Feraldis_Raider` and `Runai_Raider` are distinct in TechTree.json
      with the doc's stat blocks each.
- [x] Selecting a Feraldis Raider as the local player is not possible (no
      drag-select, no command via CommandRouter NotControllableTag gate).
- [x] Feraldis Raiders, when spawned by a House (task-066 Phase 3), move
      towards the nearest enemy entity within their LoS.
- [x] No regression on Runai's existing `Runai_Raider` trainer path.

## Implementation Phases

### Phase 1: Split TechTree + factory
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
[Raider.cs](../../../Assets/Scripts/Entities/Units/Raider.cs),
[UnitFactory.cs](../../../Assets/Scripts/Entities/Units/UnitFactory.cs),
[EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs).
**Estimated effort:** Medium

### Phase 2: Uncontrollable + patrol AI
**Files:** [SelectionSystem.cs](../../../Assets/Scripts/Input/SelectionSystem.cs),
new `Systems/AI/FeraldisRaiderPatrolSystem.cs` (model after
[PatrolThreatDetectionSystem.cs](../../../Assets/Scripts/Systems/Combat/PatrolThreatDetectionSystem.cs)).
**Estimated effort:** Medium

## Dependencies

- [task-066](../task-ageup-transform-hut-066/task.md) Phase 3 consumes this
  task's output (House → Raider auto-spawn).

## Out of Scope

- Feraldis Raider art / visual prefab — flag for art pass after gameplay
  locked.
