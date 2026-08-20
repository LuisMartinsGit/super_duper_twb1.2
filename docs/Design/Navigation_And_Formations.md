# Navigation, Formations & Overpass Structures — Design

Canonical design for unit commands, group movement, formations, and
multi-level (over/under) navigation. Modeled 1:1 on Age of Empires IV's
shipped system — see the research study in
[docs/Research/AoE4_Navigation_Study.md](../Research/AoE4_Navigation_Study.md)
for sources. Where this doc and code disagree, this doc wins.

## 1. Movement model (AoE4 parity)

- **Pathing = flow fields + steering**, never per-unit waypoint A\*.
  Long-range direction comes from a goal flow field (label-correcting
  integration over the nav cost grid, LOS shortcut on open ground);
  the last meters and unit-vs-unit interaction are steering only.
- **Units are never written into the pathing grid.** Crowds are resolved by
  steering (separation, avoid-moving, avoid-immobile, obstacle look-ahead),
  not by re-pathing.
- **Best path**: flow-field integration is cost-optimal over the grid by
  construction (octile metric, terrain weights, wall-clearance penalty).
  LOS-to-goal short-circuit gives straight any-angle movement on open ground.
- Determinism (lockstep multiplayer) is a hard constraint: integer math in
  fields, locked force order in steering, fixed iteration orders.

## 2. Group movement (AoE4 virtual-leader model)

When N ≥ 2 movable units receive one move / attack-move order:

1. A **formation group** is created with a **virtual leader** that paths from
   the group centroid to the clicked destination.
2. **Formation spots** are laid out around the leader (see §3) and assigned
   to units by type-rank first, then nearest-slot to minimize crossing.
3. Each unit steers to its moving spot. **If a unit has no line of sight to
   its spot**, it falls back to following the flow field toward the
   destination until the spot is visible again.
4. **Cohesion gate**: only units within the cohesion range
   (**12 m ≈ "a few tiles"**) of the group centroid at order time travel in
   formation; outliers path independently to the destination.
5. **Group speed = slowest member's speed** (members inside the cohesion
   range only).
6. **Catch-up**: a unit behind its spot moves at up to **+40%** of the group
   speed (never above its own base speed × 1.4) until it regains its spot.
7. Formation **facing = direction of travel** (destination − start). No
   manual facing control.
8. **Arrival**: the leader stops at the destination, spots freeze, units
   settle into spots and hold; the group dissolves once members are settled.
9. **Combat dissolves formation**: a unit that engages (attack command or
   auto-acquired target) leaves the group and fights individually.
   Re-issuing a move order re-forms.
10. **Villagers / worker units never form up** — they path independently
    (matches AoE4).

## 3. Formation set (AoE4 parity)

Formation choice persists per selection; changing it re-slots immediately,
even standing still. All formations are built perpendicular to travel
direction. Base spacing: **2.0 m between unit centers** (existing value).

| Formation | Shape |
|---|---|
| **Default (Box)** | Rectangle ~2:1 width:depth in unit counts |
| **Line** | Wide, 1–2 ranks deep |
| **Wedge** | Pyramid: 1 at head, each rank +2 |
| **Staggered** | Same counts as Box, **2× spacing**, alternate ranks offset half a step (no unit directly behind another) |

**Type layering (front rank → back rank):** scouts & heavy cavalry → light
cavalry → spear/heavy melee infantry → light melee infantry → ranged →
siege → religious/support. Implemented as an integer *formation rank* per
unit class.

## 4. Command semantics

- **Right-click = context command** (move / attack / gather / build / repair /
  garrison), **Shift+right-click = queue**, **A = attack-move**,
  **Patrol**, **Stop**, **Hold Position** (the only stance, = AoE4
  Stand Ground). Idle units auto-aggro; units on ramparts never chase off
  the wall. (All existing behavior, kept.)
- Move / attack-move orders on multi-selections use the current formation.
- Rally points support target entities and shift-queued chains.

## 5. Gates, walls & overpass bridges (dual-level navigation)

The world has **two nav layers at the same XZ**: layer 0 (ground) and
layer 1 (rampart / deck). BFME2-gate rule: **the same footprint can be
walkable both on top (over) and underneath/through (under)**.

- **Walls**: ground layer blocked, rampart layer walkable (existing).
- **Gates**: ground layer *conditionally* passable — owner + allies only,
  blocked for everyone while **locked**; rampart layer walkable over the
  arch. Lock/unlock is a dynamic nav property, not a geometry change.
  (Existing, kept.)
- **Overpass bridges** (new structure class): a deck spanning ground that
  remains walkable underneath.
  - **Under**: ground-layer cells beneath the deck keep their normal terrain
    cost — any unit walks under the bridge freely, no faction filter.
  - **Over**: deck cells are layer-1 walkable for **all factions** (a bridge
    is a road, not a fortification).
  - **Ramps** at both ends are layer-transition cells (same mechanism as
    wall climb-access); units path onto/off the deck automatically when the
    deck route is cheaper — one order, no special input.
  - Deck height per bridge instance (default 4 m, matching rampart DeckY).
- Access filters (AoE4 parity, existing walls): allies climb walls via
  towers/gates from either side; enemies only via breaches (destroyed
  segments); enemies never descend via gates/towers.

## 6. Explicit non-goals

- No RVO/ORCA velocity-obstacle avoidance (AoE4 doesn't use it).
- No manual formation facing (AoE4 has none).
- No aggressive/defensive stance matrix — Hold Position only.
- No per-unit waypoint A\* for ground movement.
