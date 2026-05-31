---
deft:
  id: task-trader-warriors-072
  type: task
  status: active
  stage: scope
  phase: 0
  total_phases: 4
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [runai, trader-warrior, ai, large]
---

# Trader-warrior lane patrols (Runai)

## Context

[docs/Design/Complete.md §4.5 (lines 1401-1455)](../../../docs/Design/Complete.md)
specifies Runai trader-warriors:

- Trade Hubs auto-spawn autonomous patrol units that walk lanes.
- **Uncontrollable** by default; become controllable when an enemy enters the
  engagement zone.
- Generate **Supplies + Crystal** passively while patrolling.
- Do **not** consume the player's population cap (separate pool).
- **Globally capped**: +1 trader-warrior per soldier trained.
- **Network-pooled**: if a Trade Hub dies, surviving trader-warriors
  redistribute across remaining hubs.

Code state:
- [PatrolThreatDetectionSystem.cs](../../../Assets/Scripts/Systems/Combat/PatrolThreatDetectionSystem.cs)
  exists and toggles the `NotControllableTag` on caravans — but only on
  caravans. No trader-warrior unit class is defined.
- Runai Trade Hub
  ([BuildingFactory.cs:709-735](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L709))
  spawns nothing on completion / tick.
- No passive income system, no global cap, no network pool.

## User Value

Runai gets the visible "trade network defends itself" identity that backs
the no-walls / no-houses culture model: enemies stepping on a lane see
patrols converge; the player feels the world is alive instead of empty.

## Requirements

- R1: New `TraderWarriorTag` unit + factory entry, stat block per doc TBD
  (suggested: HP 100, melee, speed 5.0). Spawn-only — never trainable from
  a queue.
- R2: Runai Trade Hub auto-spawns a trader-warrior every N seconds while
  under cap; the cap rises by +1 per soldier the player trains (any unit
  with `MilitaryTag`).
- R3: `NotControllableTag` applied to trader-warriors; engagement-zone
  detection (model after `PatrolThreatDetectionSystem`) toggles it off
  while an enemy is within radius, and back on after a grace period.
- R4: Passive income: each trader-warrior emits +X Supplies + +Y Crystal per
  minute to its faction. Independent of population pop cap; uses a separate
  `TraderWarriorPopulation` component.
- R5: Network pool: on Trade Hub destruction, surviving trader-warriors
  rebind to the nearest surviving hub; if none, despawn.
- R6: Player-feedback UX: engagement-zone visualization (subtle minimap
  ping + audio cue when threat detected) so the player notices the
  controllability handoff (doc §4.5 line 1444 — avoid "watching helplessly"
  frustration).

## Acceptance Criteria

- [ ] A Runai Trade Hub spawned in skirmish auto-spawns trader-warriors at
      the documented rate.
- [ ] Player cannot drag-select a trader-warrior under normal conditions.
- [ ] Walking an enemy unit within engagement range toggles a single trader-
      warrior to controllable; removing the threat for the grace period
      reverts.
- [ ] Supplies + Crystal income accumulates passively per active trader-
      warrior; capped per the +1-per-soldier rule.
- [ ] Destroying a Trade Hub causes its trader-warriors to redistribute or
      despawn (no orphans).

## Implementation Phases

### Phase 1: Trader-warrior unit + spawner
**Files:** new `Entities/Units/TraderWarrior.cs`,
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs)
(Runai Trade Hub spawn hook), [TechTree.json](../../../Assets/Resources/TechTree.json).
**Estimated effort:** Medium

### Phase 2: Controllability handoff
**Files:** extend [PatrolThreatDetectionSystem.cs](../../../Assets/Scripts/Systems/Combat/PatrolThreatDetectionSystem.cs)
to cover trader-warriors, [SelectionSystem.cs](../../../Assets/Scripts/Input/SelectionSystem.cs).
**Estimated effort:** Medium

### Phase 3: Passive income + global cap + network pool
**Files:** new `Systems/Economy/TraderWarriorIncomeSystem.cs`,
new `Systems/Economy/TraderWarriorCapSystem.cs`,
new `Systems/Work/TraderWarriorNetworkSystem.cs`.
**Estimated effort:** Large

### Phase 4: UX hooks (minimap ping + audio)
**Files:** [MinimapRenderer.cs](../../../Assets/Scripts/UI/MinimapRenderer.cs),
[HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs).
**Estimated effort:** Small

## Dependencies

- [task-069 runai-buildings-split](../task-runai-buildings-split-069/task.md)
  Phase 2 must define Grazing Grounds; trade-hub topology stable.

## Out of Scope

- MP determinism for trader-warrior AI is part of
  [task-062](../task-deepdive-backlog-2026-062/task.md) Phase 3
  (multiplayer deferred backlog).
