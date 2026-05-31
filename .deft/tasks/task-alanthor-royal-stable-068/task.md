---
deft:
  id: task-alanthor-royal-stable-068
  type: task
  status: active
  stage: implementation
  phase: 1
  total_phases: 2
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [alanthor, building, cavalry, cataphract]
---

# Alanthor Royal Stable — new building, Cataphract reparent

## Context

[docs/Design/Complete.md §3.3 (lines 974-995)](../../../docs/Design/Complete.md)
introduces **Royal Stable** as a new Alanthor cavalry trainer:

> Cataphract has been moved out of Garrison into a new Royal Stable building.

Currently:
- No `RoyalStable` Tag, factory case, or TechTree entry exists anywhere.
- `Cataphract` still trains from `Barracks` per
  [TechTree.json:1512-1514](../../../Assets/Resources/TechTree.json#L1512)
  (`trainAt: ["Barracks"]`).
- The web HUD `Actions.jsx` shows a `RoyalStable` mockup tile marked
  `notWired: true` (called out as a pure mockup in
  [task-codebase-audit-064](../task-codebase-audit-064/task.md) §A).

## User Value

Alanthor's heavy cavalry gets its dedicated host building — a clear training
identity for the culture's signature shock unit and headroom for L2/L3
cavalry tiers later ([task-079](../task-age1-unit-tier-ladders-079/task.md)).

## Requirements

- R1: New TechTree.json entry `Alanthor_RoyalStable` with HP, build cost,
  LoS, trains list = `["Alanthor_Cataphract"]`, age-gating Era 2 Alanthor.
- R2: New `RoyalStableTag` ECS component.
- R3: New factory case in [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs)
  (EM + ECB paths).
- R4: `Alanthor_Cataphract` `trainAt` changes from `["Barracks"]` to
  `["Alanthor_RoyalStable"]` in TechTree.json.
- R5: Web HUD Actions.jsx mockup tile flipped from `notWired: true` to live.
- R6: AI Alanthor military manager
  ([AIMilitaryManager.cs](../../../Assets/Scripts/AI/Managers/AIMilitaryManager.cs))
  knows to build a Royal Stable before queueing Cataphract.
- R7: Stat values per doc (placeholder until §3.5 doc questions resolve):
  HP ≈ 1,000, build cost ≈ 220 S + 80 I.

## Acceptance Criteria

- [ ] Selecting an Alanthor Hall (post-age-up) shows a "Royal Stable"
      placement option in the build menu.
- [ ] Cataphracts can only be queued from a Royal Stable; the Garrison no
      longer surfaces the Cataphract button.
- [ ] AI Alanthor faction queues Royal Stable before Cataphract in scripted
      skirmish.
- [ ] Building Royal Stable consumes its build cost; HP scales via the
      standard `BuildingUpgradeConfig` multiplier path.

## Implementation Phases

### Phase 1: Data + factory
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
[BuildingComponents.cs](../../../Assets/Scripts/Core/Components/BuildingComponents.cs),
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs),
[BuildingCosts.cs](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs),
[EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs)
(add to `BuildableBuildings`).
**Estimated effort:** Medium

### Phase 2: Cataphract reparent + AI + UI
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json) (trainAt
update), [AIMilitaryManager.cs](../../../Assets/Scripts/AI/Managers/AIMilitaryManager.cs),
[AIBuildingManager.cs](../../../Assets/Scripts/AI/Managers/AIBuildingManager.cs),
web HUD `Actions.jsx`.
**Estimated effort:** Small

## Out of Scope

- L2/L3 cavalry tier units — owned by [task-079](../task-age1-unit-tier-ladders-079/task.md).
- Royal Stable art prefab — flag for art pass.
