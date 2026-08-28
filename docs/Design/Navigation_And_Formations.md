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

1. A **formation group** is created with a **virtual leader** that paths to
   the clicked destination, starting from the army's current pose (see 7).
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
6. **Members track their spot's VELOCITY, not its position.** A member is
   commanded its spot's own velocity — the leader's step plus the tick's
   rotation applied to the arm out to that spot — plus a proportional pull
   toward it, capped at **+40%** of the group speed.
   - Aiming a member AT its spot is pure pursuit, and pure pursuit only
     converges while the target moves the way the pursuer does. That holds
     for the body of a formation on a straight line and fails everywhere
     else: a flank unit in a wheel has a spot moving almost entirely
     sideways, and chasing it **orbits** — measured at 2.1 complete circles
     per 45° corner for the inner wing.
   - The velocity term is what makes a wheel work at all: the outer flank is
     told to ride forward and the inner flank to give ground, which is the
     difference between ranks wheeling and ranks scattering.
   - This subsumes what used to be four separate speed tiers, a lateral
     correction gain and a don't-steer-backwards rule. Behind, the pull
     points forward and adds speed; ahead, it points back and removes it;
     abeam, it angles the heading. Each was an approximation of this law
     with its own failure mode.
7. Formation **facing = direction of travel**, reached by TURNING, not by
   snapping. No manual facing control.
   - A new order **starts the group in the pose the army is already in**,
     recovered by fitting the remembered slot lattice to the units' live
     positions. Snapping facing to the new bearing teleports every spot, so
     a corner became a scramble rather than a turn. A body of units that is
     not actually in the formation (RMS fit > 2 m) still snaps — for a first
     order, facing the destination is correct.
   - The leader then **wheels**: it travels along its OWN facing and rotates
     toward the flow direction at a bounded rate. Travelling along the flow
     while the facing lags is a *crab* — the lattice points one way, the
     group slides another, and the spots drag sideways through the units
     standing in them.
   - **Turn rate = (catch-up headroom) ÷ (formation radius)**, capped at 1.2
     rad/s, with a 0.05 rad/s anti-deadlock floor only. The outermost slot is
     dragged `ω × radius` sideways and a member has only 40% of its speed
     spare, so this is a hard physical budget: **the floor must never be big
     enough to override it.** At 0.25 it was — a siege train makes the army
     15 m deep *and* drops the group speed to the catapult's, so the floor
     demanded three times the sideways speed the rearmost engine had.
   - Deep, slow armies therefore wheel slowly (a 45° corner: ~2.5 s for
     infantry, ~10 s with siege). That is not a bug to tune away. The leader
     keeps its forward speed through the turn, so the formation sweeps a wide
     arc rather than stalling — shorten the army if a tighter turn is wanted.
   - Forward speed eases by **cos(heading error)**, floored at **50%**, so a
     corner costs tempo without stopping the army dead.
   - Past **~100°** the formation does not wheel, it **re-forms** on the new
     bearing. An about-face at flank-limited rate is ten seconds of pivoting
     on the spot, which is the kiting failure this system exists to prevent.
8. **Slots are remembered across orders** (`FormationSlotMemory`, by index,
   guarded by a layout key). Without this, each order re-derives the
   assignment from positions measured along the NEW travel axis, so every
   turn reshuffles who stands where and the army trades places instead of
   turning. The memory outlives group dissolution — which happens on every
   arrival — and is cleared only when a unit genuinely leaves formation
   (plain move, attack-move).
9. **Arrival**: the leader stops at the destination, spots freeze, units
   settle into spots and hold; the group dissolves once members are settled.
10. **Combat dissolves formation**: a unit that engages (attack command or
    auto-acquired target) leaves the group and fights individually.
    Re-issuing a move order re-forms.
11. **Villagers / worker units never form up** — they path independently
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

**Type layering (front rank → back rank):**

| Rank | Who | Notes |
|---|---|---|
| 0 | **Heroes** | `UniqueUnitTag`. Tested before cavalry, so a mounted hero leads rather than joining the screen |
| 1 | **Cavalry & scouts** | Half screen the front, a quarter cover **each flank** — see below |
| 2 | Melee infantry | |
| 3 | Ranged | |
| 4 | **Support / magic** (healers) | Between the line and the siege, so they can reach the line without standing in it |
| 5 | **Siege** | Rearmost, at **2× spacing** — a catapult is not a spearman with more health |
| 6 | Economy / miners | |

Siege used to sit *ahead* of support, which put engines in front of the
people everything else exists to protect.

**Cavalry wings.** Half the cavalry rides in front, a quarter covers each
flank as a column abeam the body. The split rounds **toward the front**,
because a wing of one is not a wing: two knights both ride ahead, and the
wings only appear at four. Wings need a body to screen — all-cavalry keeps
its block. The flank gap equals the front-to-back block gap, so the
formation reads at one spacing in both axes.

**Blocks are separated by `TypeGapPitches` (2) row pitches**, scaled up to
the roomier of the two ranks where they differ — so the gap in front of a
siege train is a siege-sized gap.

The layout is computed in exactly one place,
`FormationMoveCommandHelper.BuildLayout`, which takes a rank census and
returns slot offsets. Anything that needs to know the shape — including
scenarios that spawn an army already in formation — calls it rather than
re-deriving it, because a spawn that disagrees with the layout by more than
the pose-fit tolerance makes the first order snap instead of continue.

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
