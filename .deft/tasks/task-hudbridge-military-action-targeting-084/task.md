---
deft:
  id: task-hudbridge-military-action-targeting-084
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
  labels: [ui, web-hud, hudbridge, input]
---

# HudBridge — military action buttons need deferred-target pipeline

## Context

Spun out from [task-082 §A row "UI/Web HudBridge.HandleActionInvoke"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene).

The React HUD's military action cells emit `actions:invoke` with keys
`patrol`, `attack`, `formation`, `retreat`, `special`, `stance`. The
C# handler at [HudBridge.cs:288](../../../Assets/Scripts/UI/Web/HudBridge.cs#L288)
notifies the player they must right-click in the world instead. Players
read the button as broken.

Decision needed: either (a) build a deferred-targeting pipeline where
clicking the HUD button arms the next world-space click, or (b) hide
the buttons until a viable wire-up exists.

## User Value

Web HUD military buttons either work or aren't shown — no dead surface.

## Requirements

- R1: Decide between deferred-targeting vs hide-buttons (human choice
  in scope stage).
- R2: If deferred-targeting: arm-on-click flag in `RTSInputManager` /
  `SelectionSystem` so the next ground / unit click routes through the
  armed command instead of the default move/attack-move.
- R3: If hide-buttons: pass a `selectionKind`-aware capability map down
  to the React rail so it only renders viable buttons.

## File:line anchor (inherited from §A row)

- [HudBridge.cs:288](../../../Assets/Scripts/UI/Web/HudBridge.cs#L288)

## Out of Scope

- Touching the right-click world-input pipeline.
- Re-designing the React HUD action cells.
