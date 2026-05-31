---
deft:
  id: task-feraldis-hut-terrain-yields-095
  type: task
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feraldis, gatherers-hut, terrain, hunting-lodge, logging-station]
---

# Feraldis Hut upgrade terrain yield — +30 % near mountains / trees

## Context

Spun out from [task-082 §B.6 row "Hunting Lodge / Logging Station terrain bonus"](../task-profound-code-review-082/task.md#B-codevsdesign-drift).

[Age_1_Feraldis.md §Gatherer's Hut carryover](../../../docs/Design/Age_1_Feraldis.md#L261)
specifies:

> Each lodge type has a terrain preference: placement near the preferred
> terrain gives **+30 % yield** vs the base hut.
>
> | Hunting Lodge | +30 % yield when placed near mountains (mountain game — boar, goat, big game) |
> | Logging Station | +30 % yield when placed near trees (forests) |

Code: both buildings exist in `TechTree.json` (lines 966-999) and have
factory branches (`BuildingFactory.cs:62-63`) — but the terrain-yield
bonus mechanic is not implemented. They emit the same supply trickle as
the base Gatherer's Hut regardless of placement.

## User Value

Feraldis's persistent-gather economy has the placement-skill ceiling the
design intends. Lodge-near-mountain and Station-near-forest decisions
mean something. Without this, the two lodge variants are mechanically
identical and the Feraldis economy has no spatial decision layer past
"plant a hut".

## Requirements

- R1: Define "near mountains" and "near trees" — radius in tiles + the
  terrain-type check (depends on existing `PassabilityGrid` /
  `ProceduralTerrain` data).
- R2: Apply a +30 % multiplier to the lodge's supply aura when the
  preferred terrain is within radius.
- R3: Surface the bonus in the build-tooltip and the building-info panel
  so the player can see whether the placement triggered the bonus.

## Acceptance Criteria

- [ ] A Hunting Lodge placed within radius of a mountain tile produces
      +30 % more supplies than the base hut.
- [ ] A Logging Station placed within radius of a tree tile produces
      +30 % more supplies than the base hut.
- [ ] Lodges placed away from preferred terrain produce the base rate
      (no penalty — just no bonus).
- [ ] The bonus is visible in the UI (tooltip + info panel).

## Technical Notes

- Read order: `PassabilityGrid.cs`, `ProceduralTerrain.cs` for the terrain
  classification; `GatherersHut` aura code for the yield application
  point.
- "Radius" and "near" definitions are spec gaps per the doc; pick sane
  defaults and surface for playtest tuning.
- Doc Q#4 also specifies the hut-upgrade tech that locks both lodge
  choices behind a single tech — that's `task-066` scope; this task is
  yield-only.
