---
deft:
  id: task-cultured-rename-layer-071
  type: task
  status: active
  stage: implementation
  phase: 1
  total_phases: 3
  priority: critical
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [age-up, building, rename, all-cultures]
---

# Cultured rename layer — Hall / Barracks / Archery Range / House per culture

## Context

[docs/Design/Complete.md](../../../docs/Design/Complete.md) Parts III, IV, V
rely on cultured renames at age-up:

| Age 0 building | Alanthor | Runai | Feraldis |
|---|---|---|---|
| Hall | Town Hall | Trader's Hall | War Hall |
| Barracks | Garrison | Route Guard | Longhouse |
| Archery Range | Practice Range | Arrowyard | Thrower Camp |
| House (Hut) | House | *(removed — pop 200 instant)* | House (raider-spawn) |

Current code has **no rename layer**. Only `Alanthor_PracticeRange` and
`Feraldis_Longhouse` exist as distinct TechTree IDs; everything else is the
generic `Hall` / `Barracks` / `ArcheryRange`. `BuildingUpgradeConfig` costs
are culture-agnostic. `Feraldis_Longhouse` even has a hardcoded HP 1400
override that the doc rejects ([task-073](../task-rejected-stat-overrides-073/task.md)).

Per doc §3.2 line 818-820: "Cultured renames keep the same base — only the
multiplier ladder applies." So the rename is **display + train-restriction +
culture-keyed cost lookup**, not a different stat block.

## User Value

Players see the culturally appropriate building name + identity at age-up
("Trader's Hall" for Runai economy, "War Hall" for Feraldis aggression,
etc.) and the per-culture training rosters become enforceable.

## Requirements

- R1: A `BuildingDisplayName` ECS component (or extension to
  `BuildingTag`) that yields a culture-aware display name for Hall /
  Barracks / Archery Range / House.
- R2: Age-up writes the display-name override; the existing prefab-swap
  system ([BuildingPrefabSwapSystem.cs](../../../Assets/Scripts/Presentation/BuildingPrefabSwapSystem.cs))
  is already culture-aware — extend it to also stamp display name and
  train-list restriction.
- R3: Train-list filtering: a Runai Hall (cultured "Trader's Hall") only
  exposes Worker + Scout. A Feraldis War Hall exposes its culture's military
  ladder. Generic Hall in Age 0 keeps the current full list.
- R4: `BuildingUpgradeConfig.TryGetCost` keys on `(buildingId, culture, lvl)`
  so per-culture cost differences (if any per doc) resolve correctly.
- R5: Display-name surface goes to web HUD via HudBridge.

## Acceptance Criteria

- [ ] Selecting a post-age-up Runai Hall shows "Trader's Hall" in the
      EntityInfoPanel header + web HUD selection card.
- [ ] Same Hall surfaces only Worker + Scout train buttons.
- [ ] Same applies for Alanthor (Town Hall, full Garrison stack) and
      Feraldis (War Hall).
- [ ] BuildingUpgradeConfig.TryGetCost returns the right cost for a Runai
      "Trader's Hall" L1→L2 (per doc table at §4.3).

## Implementation Phases

### Phase 1: Display-name layer
**Files:** new `Core/Components/CulturedBuildingDisplayName.cs`,
[BuildingPrefabSwapSystem.cs](../../../Assets/Scripts/Presentation/BuildingPrefabSwapSystem.cs),
[EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs),
[HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs).
**Estimated effort:** Medium

### Phase 2: Train-list culture filter
**Files:** [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs),
[TrainingSystem.cs](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs),
[BuildCommandPannel.cs](../../../Assets/Scripts/UI/Panels/BuildCommandPannel.cs),
[EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs).
**Estimated effort:** Medium

### Phase 3: Culture-keyed upgrade costs
**Files:** [BuildingUpgradeConfig.cs](../../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs),
[UpgradeBuildingCommand.cs](../../../Assets/Scripts/Core/Commands/CommandTypes/UpgradeBuildingCommand.cs).
**Estimated effort:** Medium

## Dependencies

Consumed by tasks 068, 069, 070, 079, 080.

## Out of Scope

- The actual L2/L3 unit tier slots — owned by [task-079](../task-age1-unit-tier-ladders-079/task.md).
