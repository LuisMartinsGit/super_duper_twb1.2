# How Age of Empires IV Implements Unit Movement, Formations, and Pathfinding

**A technical study for re-implementation in The Waning Border (Unity DOTS/ECS).**

Primary source: GDC 2022 talk *"Pathing in 'Age of Empires IV': Flow Fields and
Steering Behaviors"* by Frank Cheng, Lead Navigation Engineer, World's Edge
([GDC Vault page](https://www.gdcvault.com/play/1027659/Pathing-in-Age-of-Empires),
[full speaker slides PDF](https://media.gdcvault.com/GDC+2022/Speaker+Slides/Pathing+In+Age_Cheng_Frank+2022-03-29+00.16.38.pdf)).
All slide-level claims below are from that deck unless noted. Where AoE4
specifics are not public, the section says so and gives the standard technique
that matches observed behavior, marked **[Inference]**.

The design decisions The Waning Border adopts from this study live in
[docs/Design/Navigation_And_Formations.md](../Design/Navigation_And_Formations.md)
(the design truth source). This file is research/reference only.

## 0. Design constraints the system was built for

From Cheng's slides (slide 2):

- **Max 8 players × 200 units each = 1,600 units** simulated at once.
- **1024 × 1024 pathing grid** covering the (largest) map, with randomly generated terrain, mountains, trees, rivers.
- **Dynamic environment**: construction, destruction, deforestation continuously change passability.
- **Formation movement**: "Units often move in cohesive formations" was an explicit requirement, not an afterthought.

Adam Isgreen (franchise creative director) confirmed in press interviews that
AoE4 deliberately moved away from the pure waypoint-A\* lineage of earlier Age
games to a flow-field solution
([Windows Central](https://www.windowscentral.com/age-empires-4-need-know)).

A sibling GDC 2022 talk, *"The Maw: Safely Multithreading the Deterministic
Gameplay of 'Age of Empires IV'"* (Joel Pritchett, Microsoft), covers the
constraint that matters most for a DOTS port: the whole simulation, pathfinding
included, is **deterministic lockstep and multithreaded** — every pathfinding
result must be bit-identical across machines.

## 1. Pathfinding architecture

### 1.1 Why not plain A\*

Slide 3 gives the rationale: treating units as A\* obstacles requires
recomputing every frame (too expensive); using obstacle-avoidance steering on
top of an A\* waypoint list gives *no guarantee of a clear path back to the
waypoint list* and may invalidate the shortest path. Flow fields fix this
because every cell in the flowed region carries a direction — a steering
deviation never strands the unit.

### 1.2 Three-field flow-field core

AoE4's flow fields follow Elijah Emerson's Supreme Commander 2 design
(Game AI Pro ch. 23, cited directly on Cheng's slides:
[Crowd Pathfinding and Steering Using Flow Field Tiles](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)):

1. **Cost field** — per-cell traversal cost, can be non-uniform (terrain types).
2. **Integration field** — distance-transform result from the goal.
3. **Flow field** — per-cell flow direction toward the goal.

Two AoE4-specific upgrades to the integration step:

- **Fast Marching Method (FMM) instead of 8-neighbor Dijkstra.** Plain
  8-neighbor integration yields only 16 usable gradient directions, causing
  unnecessary zig-zag turns. FMM produces a smooth continuous gradient. The
  resulting direction is quantized into **an 8-bit value (256 possible
  directions) per cell** (slide 6).
- **Line-of-Sight (LOS) integration**, "faster and more accurate than FMM"
  (slides 7–11): BFS outward from the goal through the visible area; detect
  impasse (obstacle) corners and draw "shadow lines" from the goal through each
  corner to the tile edge; every cell with LOS to the goal just stores "go
  straight at the goal"; FMM integration is run only inside the shaded
  (non-LOS) regions, seeded at the shadow lines. This is the flow-field analog
  of theta\*-style any-angle paths.

### 1.3 Hierarchical layer: the portal graph (HPA\*)

Integrating 1024×1024 cells per request is too expensive and mostly wasted
(slide 12), so a hierarchical abstraction sits on top, borrowed from
[HPA\* (Botea et al.)](https://webdocs.cs.ualberta.ca/~mmueller/ps/hpastar.pdf)
(slides 13–15):

- Divide the grid into **tiles** (sector size not stated in slides; Emerson's
  SupCom2 chapter uses 10×10 cells per tile — **[Inference]** AoE4 uses
  something in the 10–32 cell range).
- Detect **portals** on each shared tile edge (contiguous walkable spans).
- Determine **intra-tile portal connectivity and cost via flood fill**.
- Result: an abstract portal graph over the whole map.

A path request then (slides 16–18):

1. Runs **A\* on the portal graph** (cheap, long-range).
2. Generates **flow fields only for the tiles traversed by that A\* path**,
   carrying the flow direction through the connecting portals tile-to-tile.
3. Units **follow the flow** to the goal.

So: **long-range = portal-graph A\*; short-range = per-tile flow fields;
last meter = steering.**

### 1.4 Segmented, cached, on-demand flow generation

Generating the "perfect flow" (integrating the whole corridor back from the
destination) is rejected (slide 19): costly for 20–30-tile paths, wasted when
orders change mid-route, and not cache-friendly. Single-tile-at-a-time flow
(integrate each tile only from its entry portal) is cheap and cacheable but
inaccurate (slide 20).

The shipped solution is **overlapping segmented flow** (slides 21–22):

- Generate flow in **short segments a few tiles long, starting further
  upstream** than the unit's current tile, with **each new segment overlapping
  its parent tile** so the single-tile quality problem doesn't recur.
- Segments are **generated as the unit moves** onto new tiles.
- Segments are **cached individually and reused as building blocks for
  different paths** — this is the group-sharing mechanism: all units traveling
  the same general direction through the same tiles share the cached flow
  tiles (slide 35).

**Path repair when blocked:** the slides list the cost explicitly — "extra
recompute cost when terrain changes" (slide 35). **[Inference, standard
technique matching this]**: when a building/wall/tree changes, the affected
cells' cost field is updated, the owning tile's portals and connectivity are
rebuilt, cached flow tiles touching it are invalidated, and active
portal-graph paths crossing the dirty tile are re-planned. *Unit-level*
blockage (crowds) is deliberately NOT handled by re-pathing — it's handled by
steering, which is safe precisely because the flow field gives a valid
direction from wherever steering pushed the unit.

### 1.5 Mixed unit sizes and traversal classes: extended flow

Units occupy different footprints on the grid — the slides' example uses a
**large unit occupying 3 cells vs a small unit occupying 1 cell**
(slides 23–30). Two naive options fail: pathing everyone with the
most-restricted clearance leaves small units without flow in narrow gaps;
giving each class its own flow makes groups split along different routes.
AoE4's **extended flow**: generate the flow for the **most restricted type in
the group**, then **extend** it into cells passable only by smaller units, so
small units can use shortcuts near the shared route without the group
splitting. (Clearance/annotated-grid variant — **[Inference]** on the exact
clearance encoding.)

### 1.6 Land–water merged graph

For transports (slide 31): grid cells of valid **landing areas are marked
up**, and the **land portal graph and water portal graph are stitched together
at landing-area portals**, so a **single A\* call spans land→water**. Directly
reusable for any multi-domain movement — including ground vs wall-top/bridge-deck
layers (see §6).

### 1.7 Measured performance (slide 34, Cheng's own numbers)

| Units moving | Flow field time | Steering time |
|---|---|---|
| 1 cavalry | 0.247 ms | 0.008 ms |
| 10 cavalry | 0.376 ms (1.5×) | 0.044 ms (5.5×) |
| 200 cavalry | 1.11 ms (4.5×) | 0.561 ms (70×) |

Flow-field cost is strongly sub-linear in unit count (shared tiles); steering
is near-linear per unit. Budget accordingly: steering is the per-unit hot
loop; flow generation amortizes.

## 2. Group movement

### 2.1 Virtual leader + formation spots (slide 32)

When N units get one move order:

1. **A virtual formation leader requests and follows a flow path.** One
   pathfinding request per group, not per unit.
2. **Formation spots are generated around the leader**; each unit steers to
   its assigned spot ("formation steering").
3. **If a unit has no line-of-sight to its formation spot** (spot across an
   obstacle, unit fell behind), it **falls back to following the flow field
   directly** until it regains sight of its spot.
4. **Expanding flow path**: extra leaf tiles are expanded off the flow path so
   the flow **fully covers all the units' starting positions** — stragglers
   standing off the corridor still get valid flow.

This is how groups share path computation: one leader path + shared flow tiles
+ per-unit steering offsets to slots.

### 2.2 Form-up behavior

- Units **gather into formation while starting to move** — largely on the move
  via the catch-up mechanic, not standing still until assembled
  ([Maguro's AoE4 analysis](https://www.maguro.one/2021/12/aoe4.html)).
- **Cohesion gate**: units only travel as a formation "when they are within a
  few tiles of each other"
  ([AoE Fandom wiki, Unit formation](https://ageofempires.fandom.com/wiki/Unit_formation)).
  Units farther away path independently to the destination. Exact radius
  unpublished (**approximate: a few tiles**).
- **Catch-up speed boost**: units behind their slot get a temporary speed
  boost. Concrete number from patch notes: **villager formation catch-up speed
  was reduced from 100% to 40% "to match all other units"** in the Season Four
  update (Feb 2023)
  ([patch notes coverage](https://videogames.si.com/news/age-of-empires-4-season-4-update-patch-notes))
  — i.e. the standard catch-up bonus is **+40% speed** for all units.

### 2.3 Group speed rule (current)

While traveling in formation, **all units move at the speed of the slowest
unit in the group**
([AoE Fandom wiki](https://ageofempires.fandom.com/wiki/Unit_formation)).
Two mitigations: the cohesion gate (a lone knight far from your trebuchet is
not slowed) and the +40% catch-up boost. No patch ever changed the
slowest-unit rule itself — the patch-visible changes were to the catch-up
multiplier and villager behavior.

### 2.4 Destination expansion into slots

The clicked point becomes the leader's goal; **per-unit target slots are the
formation spots laid out around the leader's final pose** (slide 32).
**[Inference, standard]**: slots are assigned by type-layer first, then
nearest-slot-to-current-relative-position within a layer to minimize crossing;
the same spot layout is used continuously during travel (spots move with the
leader), so arrival is just "spots stop moving."

## 3. Formations

All from the
[AoE Fandom wiki AoE4 section](https://ageofempires.fandom.com/wiki/Unit_formation)
unless noted; community-documented observed behavior as of update 13.0.4178.

### 3.1 Formation set

- **Default formation** (no button): rectangle **approximately twice as wide
  as it is deep** in unit counts. Applied to move, attack-move, and attack
  commands. Villagers/worker units never form up.
- **Line formation**: much wider, only **1–2 units deep** — spreads ranged
  damage and counters pass-through/AoE damage.
- **Wedge formation**: pyramid, 1 unit at the head, each row +2 units.
- **Staggered formation**: same width/depth in unit counts as default but
  **significantly increased spacing** and offset alternate rows (no unit
  directly behind another) — anti-AoE (mangonels).
- **Column formation**: **ships only**, 2 wide.
- Selecting a formation **rearranges units immediately, even when standing
  still**.

### 3.2 Type layering (front → back)

Scouts and heavy cavalry → light melee cavalry → spearmen → heavy melee
infantry → light melee infantry → ranged units → siege engines → religious
units. Implement per-unit-type integer "formation rank"; sort into rows by
rank — the melee-front/ranged-back result falls out of the rank table.

### 3.3 Facing, arrival, combat

- **Facing**: the formation faces its direction of travel; the layout is built
  perpendicular to the leader's approach direction. No manual facing control.
  **[Inference]**: facing = normalized (destination − last waypoint); on a
  zero-length move, keep prior facing.
- **Arrival**: units settle into their spots and hold; idle units
  **auto-aggro** nearby enemies
  ([Season Five update 7.0.5861](https://www.ageofempires.com/news/age-of-empires-iv-season-five-update-7-0-5861/):
  units on walls that idle-aggro no longer leave the wall to chase).
- **In combat, formations dissolve**: the formation is a *travel* arrangement;
  once units engage, they pick targets and move individually. Re-issuing a
  move order re-forms the formation.

### 3.4 Spacing numbers

- Land spacing values are not published. **Approximate, observed**: default
  spacing ≈ 1 unit-width gap (~1 tile between unit centers); staggered ≈ 2×.
- One hard naval number: **warship formation spacing reduced from 4.5 to 3
  tiles** in the Season One update
  ([release notes](https://www.ageofempires.com/news/age-of-empires-iv-season-one-update-release-notes/)).

## 4. Local avoidance and collision

- Slide 33 names four steering behaviors shipped: **avoid moving units**,
  **avoid immobile units**, **avoid small static obstacles** (props too small
  to bake into the grid), **group cohesion**. Classic Reynolds-style weighted
  steering, not RVO/ORCA. **[Inference on the exact blend]**: seek(formation
  spot or flow direction) + separation + the four above, priority-weighted.
- **Units are never written into the pathfinding grid** (slide 3); all
  unit-vs-unit interaction is steering + collision radii.
- **Unit sizes**: footprints from **1 cell (small) to 3 cells (large)** on the
  pathing grid (slides 23–30). Per-unit collision radii in meters are not
  published.
- **Soft collision / clumping**: allied overlap is permitted and resolved
  softly by separation forces; enemy units are not pushed — enemy lines
  physically block
  ([Steam discussion](https://steamcommunity.com/app/1466860/discussions/0/3199240042209182260/),
  [AoEZone](https://aoezone.net/threads/attack-move-ban-or-bang.180713/page-6)).

## 5. Command semantics

- **Right-click = context command**: move on ground, attack on enemy,
  gather/build/repair for villagers, garrison on own building, board transport.
- **Shift-queue**: shift+right-click queues waypoints/commands; also applies
  to **rally points** (a production building's rally can be a shift-queued
  chain) ([AoE Fandom, Gather Point](https://ageofempires.fandom.com/wiki/Gather_Point)).
  Rally points are typed: generic (gold flag) vs economic (blue flag).
- **Attack-move** (default key A): move along path, engage enemies
  encountered; the group travels in formation while attack-moving; ranged
  units stop to fire, melee peel off to chase.
- **Patrol**: added in the Season One update (April 2022) — move along a path
  and attack any enemies seen; ping-pongs.
- **Stop** halts and clears the queue.
- **Stances**: only **Stand Ground** (hold position toggle); no
  aggressive/defensive stance set. Default behavior: **idle aggro**
  auto-engages nearby enemies, with special-case suppression (units on walls
  don't chase off the wall).
- **Commands × formations**: move/attack-move/attack all use the current
  formation; formation choice persists per selection; changing formation
  re-slots immediately.

## 6. Gates and walls: the dual-level navigation case

Observed shipped behavior (from
[Stone Wall (AoE4) wiki](https://ageofempires.fandom.com/wiki/Stone_Wall_(Age_of_Empires_IV))
and [Stone Wall Gate wiki](https://ageofempires.fandom.com/wiki/Stone_Wall_Gate)):

- **Gate passage**: a gate lets allied units path through the wall at ground
  level automatically; enemies never; a **locked** gate blocks everyone
  including the owner. Gate traversability is a **per-player, per-toggle
  dynamic property of the nav graph**, not geometry.
- **Walkable wall tops**: infantry stand and fight on top of stone walls
  (+1 range, −25% ranged damage taken, −50% siege damage as of 12.0.1974).
  **Access points**: allied units climb via a Stone Wall Gate or Tower, or via
  a destroyed segment; enemy infantry only via Siege Towers or breaches, and
  cannot use gates/towers to get down. Since update 9.1.176 (Nov 2023) allies
  climb from either side regardless of gate orientation.
- The gate is therefore exactly the "overpass": the **same XZ footprint is
  simultaneously ground-walkable (through the arch, allied-only) and
  wall-top-walkable (over the arch)** — the BFME2 gate/wall behavior.

**How the dual level is represented — not publicly documented.**
**[Inference — standard technique consistent with the rest of the
architecture]:**

- The wall top is a **second navigation layer**: a separate set of walkable
  cells that exists in parallel with the ground grid at the same XZ.
- **Gates, towers, siege towers, and breach points are portal nodes linking
  the two layers** — the same mechanism AoE4 uses for land↔water (merged
  graph, slide 31). Wall access points are the "landing areas" of the wall
  layer.
- **Traversal filters on portals**: gate-arch ground portal = {owner+allies,
  only while unlocked}; gate/tower up-stairs portal = {owner+allies};
  siege-tower portal = {besieger, up only}; enemy down-transitions = none.
  Locking a gate = disabling a portal = a cheap dirty-portal update, not a
  grid rebake.

## 7. Known numbers (consolidated)

| Quantity | Value | Source |
|---|---|---|
| Simulation scale target | 1,600 units (8 × 200) | GDC slides p.2 |
| Pathing grid | 1024 × 1024 cells | GDC slides p.2 |
| Flow direction storage | 8 bits/cell (256 directions) | GDC slides p.6 |
| Unit footprints | 1 cell (small) … 3 cells (large) | GDC slides p.23–30 |
| Flow field cost, 1/10/200 units | 0.247 / 0.376 / 1.11 ms | GDC slides p.34 |
| Steering cost, 1/10/200 units | 0.008 / 0.044 / 0.561 ms | GDC slides p.34 |
| Formation catch-up speed bonus | +40% | Season 4 notes |
| Warship formation spacing | 3 tiles (was 4.5) | Season One notes |
| Default land formation shape | ~2:1 width:depth (unit counts) | Fandom wiki |
| On-wall combat modifiers | +1 range, −25% ranged, −50% siege dmg | Stone Wall wiki |

Approximate / unpublished: portal tile size (**~10–32 cells**), formation
cohesion range (**"a few tiles"**), land spacing (**~1 tile between centers,
~2× staggered**), collision radii, flow-segment length.

## 8. Patch history relevant to movement

- **Season One 5.0.7274 (Apr 2022)**: Patrol added; ship responsiveness;
  warship spacing 4.5→3.
- **Season Four 6.0.878 (Feb 2023)**: villager catch-up 100%→40%.
- **Season Five 7.0.5861 (Jun 2023)**: AI pathfinding around walls; on-wall
  idle-aggro no longer chases off the wall.
- **9.1.176 (Nov 2023) — wall rework**: climb from either side; walls connect
  to static resources (resources become nav blockers); chained destruction of
  adjacent segments; on-wall damage rebalance.
- **10.0.576 (Mar 2024)**: wall-blueprint→gate conversion. **13.0.4178**: ship
  formations.
- AoE4 never shipped a pathfinding *algorithm* overhaul post-launch — the
  flow-field system shipped at launch; patches tuned behavior only.

## 9. Re-implementation blueprint (condensed)

1. **Grid**: cost field (byte/cell) + a layer index for wall-tops/decks;
   clearance value per cell for size classes.
2. **Portal graph**: ~16² sectors; portals rebuilt per dirty sector
   (structural changes only, never units); A\* over portals per group request.
3. **Flow tiles**: LRU cache keyed by (sector, entry portal, goal, traversal
   class, layer); LOS integration first, FMM/Dijkstra for shadowed cells;
   1 byte direction/cell.
4. **Group entity**: virtual leader follows the flow; formation spots = child
   offsets by type-rank table; per-unit steering = seek(spot) with fallback
   seek(flow) when no LOS to spot; +40% catch-up; group speed = min speed of
   members within cohesion range.
5. **Steering/collision**: separation + avoid-moving + avoid-immobile +
   avoid-small-static + cohesion, weighted; soft overlap resolution; enemies
   non-pushable; never write units into the grid.
6. **Gates/overpasses**: deck/wall-top = layer-1 cells; gate arch = layer-0
   conditional cells with faction+lock mask; ramps/stairs/breaches =
   inter-layer portals with per-faction, per-direction masks; lock toggle =
   portal enable/disable, no rebake.
7. **Determinism**: fixed-point or strictly-ordered math; all pathfinding
   inside the lockstep sim tick.

## Sources

- [GDC Vault — Pathing in 'Age of Empires IV' (Frank Cheng, GDC 2022)](https://www.gdcvault.com/play/1027659/Pathing-in-Age-of-Empires) · [slides PDF](https://media.gdcvault.com/GDC+2022/Speaker+Slides/Pathing+In+Age_Cheng_Frank+2022-03-29+00.16.38.pdf)
- [Emerson — Crowd Pathfinding and Steering Using Flow Field Tiles (Game AI Pro ch.23)](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)
- [Botea et al. — Near Optimal Hierarchical Path-Finding (HPA\*)](https://webdocs.cs.ualberta.ca/~mmueller/ps/hpastar.pdf)
- [AoE Fandom — Unit formation](https://ageofempires.fandom.com/wiki/Unit_formation) · [Stone Wall (AoE4)](https://ageofempires.fandom.com/wiki/Stone_Wall_(Age_of_Empires_IV)) · [Stone Wall Gate](https://ageofempires.fandom.com/wiki/Stone_Wall_Gate) · [Gather Point](https://ageofempires.fandom.com/wiki/Gather_Point)
- [AoE4 Season One notes](https://www.ageofempires.com/news/age-of-empires-iv-season-one-update-release-notes/) · [Season Five 7.0.5861](https://www.ageofempires.com/news/age-of-empires-iv-season-five-update-7-0-5861/) · [Season 4 notes](https://videogames.si.com/news/age-of-empires-4-season-4-update-patch-notes) · [aoe4world patch index](https://aoe4world.com/explorer/patches)
- [Maguro — AoE4 movement analysis](https://www.maguro.one/2021/12/aoe4.html) · [Windows Central — AoE4 guide](https://www.windowscentral.com/age-empires-4-need-know)
