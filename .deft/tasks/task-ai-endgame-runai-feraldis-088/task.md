---
deft:
  id: task-ai-endgame-runai-feraldis-088
  type: task
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: spin-out
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [ai, gameplay, culture]
---

# AI — culture-specific endgame for Runai and Feraldis

## Context

Spun out from [task-082 §A row "AI/Endgame AIAlanthorEndgameSystem"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene).

Only Alanthor has a culture-specific endgame driver
([AIAlanthorEndgameSystem.cs:76](../../../Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs#L76))
which covers towers, smelter / veilsteel production, sect adoption
(Fortitude / Renewal cluster), active-power firing, armoured-unit
production, and worker flee.

`SimpleAISystem` runs the Age-1 build-order then goes idle in Age 2.
A Runai or Feraldis AI in Age 2 produces nothing — no trade lanes /
caravans (Runai), no raider houses / siege (Feraldis), no sect
adoption.

## User Value

Multiplayer / skirmish vs an AI feels meaningful in Age 2 regardless
of which culture the AI chose. Today, AI play is effectively a
one-culture loop.

## Requirements

- R1: Decide pattern: per-culture system (mirrors Alanthor) OR
  generalise the Alanthor driver into a culture-table that picks per
  brain.
- R2: Runai endgame: TradeHub construction, caravan dispatch,
  Bazaar / Vault upgrades, sect adoption favouring trade / curse
  neutrality (cross-ref [task-078](../task-runai-curse-neutrality-078/task.md)).
- R3: Feraldis endgame: Longhouse / Hunting Lodge / Logging Station,
  raider patrols (cross-ref [task-067](../task-feraldis-raider-rebuild-067/task.md)),
  sect adoption favouring War / Ash, pop-spike (cross-ref
  [task-080](../task-feraldis-instant-pop-spike-080/task.md)).
- R4: Each new system mirrors Alanthor's slow-tick (5s) and runs
  `[UpdateAfter(typeof(SimpleAISystem))]`.

## File:line anchor (inherited from §A row)

- [AIAlanthorEndgameSystem.cs:76](../../../Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs#L76)

## Out of Scope

- Designing Runai / Feraldis Age-1 build orders (those exist in
  `AIBuildOrder.cs`).
- New unit / building mechanics — this task drives existing content.
