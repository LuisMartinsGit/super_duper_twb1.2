---
deft:
  id: task-glow-once-per-node-gate-094
  type: bug
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: high
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [glow, crystal-curse, economy, once-per-node]
---

# Glow once-per-node "first state change" gate

## Context

Spun out from [task-082 §B.1 row "Glow economy — once-per-node first state change"](../task-profound-code-review-082/task.md#B-codevsdesign-drift).

[Overview.md §The Glow economy](../../../docs/Design/Overview.md#L271)
specifies:

> **Glow** is a high-tier resource. It is **only created** by interacting with
> **Crystal-Curse nodes**, and each node only ever yields Glow **once** — the
> first time its state changes. Back-and-forth contesting cannot fabricate
> infinite Glow.

The current Glow pickup pipeline in
[GlowFlowSystem.cs:1](../../../Assets/Scripts/Systems/Economy/GlowFlowSystem.cs#L1)
implements:

1. GlowPickup despawn after timeout
2. Unit walks over a GlowPickup → GlowCarrier transfers the amount
3. GlowCarrier walks near an owned Reliquary → deposit into reliquary
4. Reliquary flushes stored amount into faction bank
5. Carrier dies → respawn a GlowPickup at the death position (interception)

But there is no per-node flag enforcing "first state change only". If a
curse node is cleansed → re-cursed → cleansed again (e.g. across multiple
sieges), each cleanse would re-drop Glow under the current code path.
This breaks the "Glow supply is finite per map" rule that caps the
late-game power ceiling.

## User Value

The Glow economy stays bounded. A faction that ignores the curse layer
has no path to T4 unit upgrades, and a faction that camps a single node
can't farm infinite Glow by toggling state.

## Requirements

- R1: Add a `NodeGlowYielded` component (or boolean field on the node's
  `NodeState` component) that flips true on the first cleanse / convert /
  destroy event.
- R2: Gate the Glow drop in the cleanse / convert / destroy paths
  (`PurificationRitualSystem`, `ConversionRitualSystem`,
  `NodeStateDeathInterceptSystem`) on this flag being false.
- R3: Snapshot the flag in `task-save-load-system-081`'s component
  coverage so save/load preserves the once-per-node invariant.

## Acceptance Criteria

- [ ] Cleansing the same curse node twice produces exactly one Glow
      pickup.
- [ ] Conversion → re-curse → re-conversion produces exactly one Glow
      pickup.
- [ ] Destruction → re-spawn (if applicable) → re-destruction produces
      exactly one Glow pickup.
- [ ] Save/load round-trip preserves the `NodeGlowYielded` state.

## Technical Notes

- Cross-system change touching three ritual / death-intercept systems.
- Save/load coverage will surface as a §C row in `task-082` Phase 3
  (component-by-component audit).
