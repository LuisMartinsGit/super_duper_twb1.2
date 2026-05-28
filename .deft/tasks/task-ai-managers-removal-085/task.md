---
deft:
  id: task-ai-managers-removal-085
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: spin-out
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [ai, cleanup, ecs, refactor]
---

# AI — remove or revive dead manager / behavior orchestration tree

## Context

Spun out from [task-082 §A row "AI/Managers dead orchestration"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene)
and the associated `AIBootstrap` row.

Eight AI driver systems are `[DisableAutoCreation]` with the comment
`Replaced by SimpleAISystem`:

- [AIEconomyManager.cs:17](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L17)
- [AIMilitaryManager.cs:16](../../../Assets/Scripts/AI/Managers/AIMilitaryManager.cs#L16)
- [AIBuildingManager.cs:16](../../../Assets/Scripts/AI/Managers/AIBuildingManager.cs#L16)
- [AIMissionManager.cs:11](../../../Assets/Scripts/AI/Managers/AIMissionManager.cs#L11)
- [AITacticalManager.cs:13](../../../Assets/Scripts/AI/Managers/AITacticalManager.cs#L13)
- [AIStrategyEvaluator.cs:14](../../../Assets/Scripts/AI/Core/AIStrategyEvaluator.cs#L14)
- [AIScoutingBehavior.cs:12](../../../Assets/Scripts/AI/Behaviors/AIScoutingBehavior.cs#L12)
- [AIDefenseBehavior.cs:16](../../../Assets/Scripts/AI/Behaviors/AIDefenseBehavior.cs#L16)

Their per-faction state components are still attached on every AI
brain by [AIBootstrap.cs:227](../../../Assets/Scripts/Core/Bootstrap/AIBootstrap.cs#L227)
even though no system reads them — pure archetype bloat.

`SimpleAISystem` covers Age-1 build-order play; `AIAlanthorEndgameSystem`
covers Alanthor's Age-2 endgame. The dead managers neither fit nor
contribute. Decision: delete (and the components in
`AIManagerComponents.cs` / `AIScoutingComponents.cs`) OR revive
selected pieces (scouting behavior is the most likely candidate per the
header comments in `SimpleAISystem`).

## User Value

~3.5k LoC of dead code removed; AI brain archetype shrinks from ~10
components to ~3; cleaner namespace for future AI work.

## Requirements

- R1: Decide per-system: keep (and re-enable) or delete.
- R2: For deletions: drop the file, drop the matching state component
  in `AIManagerComponents.cs` / `AIScoutingComponents.cs`, drop the
  matching `em.AddComponentData` call in `AIBootstrap.cs`.
- R3: For revivals: remove the `[DisableAutoCreation]`, verify the
  build still passes, ensure the revived system doesn't conflict with
  `SimpleAISystem`'s build-order steps.
- R4: Preserve scouting if it's the only thing the player notices is
  missing — fog-of-war reveal flow needs the patrol behavior.

## File:line anchor (inherited from §A row)

- [AIEconomyManager.cs:17](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L17)
- [AIBootstrap.cs:227](../../../Assets/Scripts/Core/Bootstrap/AIBootstrap.cs#L227)

## Out of Scope

- New AI behaviour. This is removal / re-enable only.
- Touching `SimpleAISystem` or `AIAlanthorEndgameSystem`.
