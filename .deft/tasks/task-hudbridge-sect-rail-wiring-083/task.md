---
deft:
  id: task-hudbridge-sect-rail-wiring-083
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
  labels: [ui, web-hud, sects, hudbridge]
---

# HudBridge — wire the React sect rail to adopt / level-up / cast handlers

## Context

Spun out from [task-082 §A row "UI/Web HudBridge.OnHudMessage sidebar:action"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene).

The `sidebar:action` topic in [HudBridge.cs:122](../../../Assets/Scripts/UI/Web/HudBridge.cs#L122)
currently logs a debug line and falls through — the React sect rail
sends adoption / level-up / cast clicks, but no C# routing exists to
turn those into `SectAdoption` calls, `SectLever*` lever buys, or
`SectActivePowerSystem.Fire` invocations.

Same TODO marker (`SECTS-BINDING-TODO`) is referenced at lines 21 and
295 of `HudBridge.cs` and was originally surfaced by [task-064](../task-codebase-audit-064/task.md).

## User Value

Sect rail buttons in the React HUD do what the player expects instead
of silently logging.

## Requirements

- R1: `sidebar:action` payload parsing — extract `sect`, `variant`
  (active / passive / building / unit / adopt), and optional `target`
  payload fields.
- R2: Route to the right handler:
  - `adopt` → `SectAdoption.AdoptSect` / chapel placement flow
  - `passive` / `building` / `unit` / `active` (lever buy) →
    `SectLever*System` upgrade entry points
  - `cast` → `SectActivePowerSystem.Fire` (needs target pick for
    targeted powers, instant cast for self-buffs)
- R3: Mirror the same authoritative checks the IMGUI ReligionHUD
  uses — RP cost, cooldown, glow allocation, prerequisite chapel.

## File:line anchor (inherited from §A row)

- [HudBridge.cs:122](../../../Assets/Scripts/UI/Web/HudBridge.cs#L122)
- Cross-refs: [HudBridge.cs:21](../../../Assets/Scripts/UI/Web/HudBridge.cs#L21), [HudBridge.cs:295](../../../Assets/Scripts/UI/Web/HudBridge.cs#L295)

## Out of Scope

- Designing the React-side payload shape (it already exists; this task
  consumes it).
- New sect mechanics — those are owned by [task-063](../task-sect-system-redesign-063/task.md).
