# The Build Grid

**Status:** canonical for the grid and the footprint RULES. Supersedes every
earlier per-building footprint number in [Age_0.md](Age_0.md),
[Age_1_Alanthor.md](Age_1_Alanthor.md), [Age_1_Runai.md](Age_1_Runai.md) and
[Age_1_Feraldis.md](Age_1_Feraldis.md). The footprint VALUES are the SO field
`footprintCells` (2026-10-03).

The map is covered by a single **2 metre square grid**. Everything that
occupies ground — buildings, resource nodes, curse structures, trees and
scatter props — sits on a whole number of grid cells and is snapped to them.

---

## 1. The grid itself

| Property | Value |
|---|---|
| Cell size | **2.0 m** |
| Anchor | world origin. Cell `(i, j)` spans `[2i, 2i+2) x [2j, 2j+2)` |
| Cell centre | `(2i + 1, 2j + 1)` |
| Relationship to the nav grid | the nav cost field and `PassabilityGrid` stay at **1 m**; one build cell is exactly **2x2** nav cells |

The build grid is anchored at the world origin and **not** at either
pathing grid's origin. That keeps it map-independent and identical on every
client — snapping is pure integer arithmetic on world coordinates, with no
terrain sample and no dependence on bootstrap order. Sub-cell offset between
the build grid and a pathing grid's own origin is harmless: footprint stamps
are computed from the world rect, so a 2 m footprint always covers a whole
number of 1 m cells in count, wherever the finer grid starts.

**Height is not snapped.** Only X and Z quantise; Y continues to follow the
terrain.

### Snap rule

A footprint `W x H` **cells** centred at `c` must cover exactly `W x H` whole
cells. That fixes the centre per axis by the parity of the cell count:

- **odd** cell count -> centre lands on a **cell centre** (an odd metre)
- **even** cell count -> centre lands on a **cell boundary** (an even metre)

This is the same odd/even rule the unused `PassabilityGrid.SnapToGridRect`
already implemented, restated in 2 m units.

### Where snapping happens

Snapping is applied at the **entity factory**, not only in the placement UI.
`BuildingFactory.Create` is the single choke point every spawn path routes
through — player placement, AI, scenario seeding, bootstraps and lockstep
replay — so snapping there makes every source grid-aligned by construction
and leaves no path that can author an off-grid building. The placement ghost
snaps too, so the player *sees* the cell the building will take.

---

## 2. Footprints

Footprints are authored in **cells**, on each building's SO: the
`footprintCells` field (decision 34, 2026-10-03 — it replaced the
`BuildingSizeConfig` code table, which no longer holds sizes). **The SO is the
truth source; this doc does not restate cell counts.** Read them in the
generated calculator (`tools/calculator/TechTree.html`). A building SO with no
footprint is a data bug, caught like any other missing stat.

> **Doubled 2026-08-13.** Every footprint is twice what it originally was —
> buildings read far too small against the units and the terrain. The grid
> itself is unchanged at 2 m, so placement keeps its fine granularity. The
> earlier rule that *a Hut is exactly one grid cell* is **superseded**: the Hut
> is still the smallest standing building.

What the footprints must satisfy — the rules, which the SO values follow:

| Rule | Buildings |
|---|---|
| **Smallest** — statues, not buildings | every Chapel — the statues docked in the Temple ring |
| **A resource building's footprint IS its node's** (2026-09-29), so the extractor lands exactly on its node | Gatherer's Hut (on a supply spot), Mine (on an iron deposit), Veilstone Mine (on a veilstone outcrop) |
| **The Alanthor Trading Outpost stands BESIDE its outcrop** (2026-10-04) — on a side slot, never on the node (next subsection) | Trading Outpost (`Alanthor_TradingOutpost`) |
| **The wall hub's footprint is its drum's bounding square**, derived from `AlanthorWall.HubWidth` — the SO must stay in step with it | Wall Hub (`Alanthor_Wall`), Palisade hub (`Palisade`) |
| **One footprint for the capital in both ages** — the Shelter becomes the Fortress in place (2026-10-03, decision 11) | the capital, id `Fortress` |
| **Each building its own** — no building borrows another's size. The **Archery Range** used to fall back on a shared default; it now reads its own `footprintCells` (2026-10-03) | everything else: House, Barracks, Archery Range, Temple, the landmarks, Royal Stable, Siege Yard, Watch Tower, and the Runai / Feraldis / sect buildings |

> **Temple halved back (2026-08-17).** The 2026-08-13 doubling had given the
> Temple of Ridan its own oversized class; in play the cathedral dwarfed
> everything around it. It went back down a class, its chapel statues halved
> with it, and `TempleChapelRing.SlotRadius` returned to the pre-doubling
> value the docking was originally tuned for.

### The Trading Outpost's side slots (2026-10-04, decision 40)

*Supersedes decision 28's on-outcrop placement.* Every veilstone outcrop has
**four side slots — north, east, south, west** — and an Alanthor Trading
Outpost stands on one of them:

- **Flush and centred.** A slot's centre is the outcrop's centre moved along
  one axis by half the node's footprint plus half the Outpost's
  (`footprintCells` on its SO), so the post's edge meets the node's edge with
  no gap and no overlap, centred on the outcrop's axis. With the node's even
  parity the slot always lands on the Outpost's own grid parity — the
  ordinary snap never moves it.
- **One post per slot**, so at most four per outcrop. **The outcrop's own
  square stays an impassable node** that nothing is built on — the Outpost
  gets no "own node" exemption from the node rule (§3), and two outcrops'
  slots that fall on one spot are one slot.
- **A slot is legal only when the ordinary placement rules pass there** —
  terrain, slope, water, passability, no other node, no building, no wall —
  so a side against a cliff or another node is simply not offered.
- **The ghost snaps to the nearest free, legal side** of any uncursed outcrop
  near the cursor (`TradingOutpostSystem.asset` `nodeReach`), and reads red
  with the node rule when no side is left.
- **At age-up** an Alanthor Veilstone Mine (which stood on the outcrop) moves
  to the free, legal side nearest its capital, ties broken north, east,
  south, west — preferring a side inside the outcrop's own territory; with
  none it stays on the outcrop.

The Outpost's art no longer has to fake standing beside the crystal: it does.

Consequences worth stating plainly, because they change how a base packs:

- Non-square footprints are retired: at 2 m resolution a 3 m and a 4 m
  building are not distinguishable, so the distinction is dropped rather than
  faked.
- **Anything tuned against the sizes has to move with them.** Concretely:
  `AlanthorWall.HubWidth`, `TempleChapelRing.SlotRadius` (the chapel ring docks
  against the Temple wall — at a stale radius the whole ring would sit
  *inside* the cathedral), the AI's `BuildRingDistanceMin/Max`,
  `MinBuildingSpacing` and `MinResourceNodeClearance`, and the starting-base
  worker/army offsets in `PlayerSpawnSystem`. When the footprints doubled, two
  large buildings overlapped at the old spacing, every AI candidate failed
  validation, and the starting army spawned inside its own capital's blocked
  cells.

Building **visuals are scaled to their footprint** so the mesh fills its
cells with no overhang and no gap.

### Buildings may touch (2026-10-04)

**Two footprints may sit flush — edge to edge, sharing a cell boundary — but
never overlap.** The game enforces no gap between buildings: the placement
test is a strict overlap test (touching edges pass), for the player's ghost,
the AI's site search and the command executor alike, and the passability
stamp covers exactly the footprint's cells (no padding ring). This was
already how the executor behaved; it is now the stated rule.

What keeps a base walkable is therefore a matter of LAYOUT, not of a gap
rule: a player who walls their own buildings in has done so on purpose. The
AI may build flush too, and is held to not sealing its own base instead (no
building's last free side, no production exit, no gate, no pocket of open
ground cut off; at most a few buildings in one flush row) — see
[Game_AI.md](Game_AI.md) §6b.

---

### Standing on the ground (2026-09-30)

Every prefab building's model is placed so its **lowest point sits on the
terrain**, whatever pivot it was exported with — the same measurement that
centres it on its footprint (`ComputeFootprintFit`) records the pivot → lowest
point drop, and the view and the placement ghost both apply it. A model
authored with its base at the pivot does not move. A model meant to sink part
of itself below ground (a foundation skirt for slopes) cannot do that any more
— it is lifted too.

## 3. Resource nodes, curse nodes, trees and props

Everything in this class occupies **exactly one cell (2 x 2 m)**, is snapped
to that cell's centre, is scaled to fill it, and is **impassable while it
exists**.

| Thing | Rule |
|---|---|
| **Every resource node (2026-09-29)** | **2 x 2 cells (4 x 4 m), even parity (snapped to a cell boundary), the same footprint as the resource building that stands on it — so the extractor lands exactly on its node.** `BuildGrid.ResourceNode*` holds the rule; the nav cost field stamps the full 4 m (`NodeFootprint`), PassabilityGrid blocks exactly that square |
| Veilstone outcropping | 2 x 2 cells, impassable, cleared when the node is exhausted |
| Iron deposit | 2 x 2 cells, impassable, cleared when exhausted |
| Veilsteel deposit | 2 x 2 cells, impassable, cleared when exhausted |
| **Supply spot** | **2 x 2 cells — the Gatherer's Hut's own footprint — and PASSABLE: it is ground you build the hut ON, not a prop beside it. Snaps with even parity (cell boundary) so the hut centres on it exactly. (2026-08-29; was 1 cell)** |
| Blight pocket / Small Node | 1 cell, impassable while alive |
| Border Main Node (well) | **6 x 6 cells** — it is a structure, not a node |
| Trees, rocks, bushes | 1 cell, impassable, at most one per cell |

"Impassable until mined" means the block is tied to the node's lifetime: the
node is removed when depleted, and removal releases the cell on both the nav
cost field and `PassabilityGrid`.

### Where a node may stand, and what may stand on one (2026-09-29)

- **A node site is legal only when its whole 4 x 4 m square is buildable
  ground** — inside the map, no water, no slope over 15°, every passability
  cell open (forests, cliffs, other obstacles), no building on it — **and it
  keeps one clear cell (2 m) from every other node's square.** Nodes never
  overlap.
- Every node factory resolves its position through `ResourceNodeSite.TryResolve`,
  so authored markers, fallback scatters, coverage passes and the curse's
  veilstone precipitation all obey it. An illegal spot moves to the **nearest
  legal grid site** (fixed nearest-first search, identical on every lockstep
  peer, up to 12 cells / 24 m); with none in range the node is **not spawned**
  and a warning is logged. A legal authored spot never moves.
- Start Fortresses and the nature-region blocking run before any node spawns,
  so nodes also keep off them.
- **Nothing is built on a node except the extractor made for it** (Gatherer's
  Hut on supply, Mine on iron, Veilstone Mine on veilstone). The Alanthor
  Trading Outpost is NOT built on its outcrop — it stands on a side slot (§2,
  The Trading Outpost's side slots), and the node rule applies to it in full. The whole node square is tested — the supply spot
  included, which is passable and no obstacle — by the placement ghost, the
  AI's site search and the command executor. Refusal: *"Cannot build on a
  resource node — only its own extractor may stand there"*.

Trees are baked as Unity terrain tree instances rather than entities, so they
get the same treatment through the map generator: scattered positions quantise
to cell centres, one instance per cell, and each occupied cell is painted into
the terrain `NoWalk` layer that the passability bake already reads.

---

## 4. Selection and placement outlines

A building's outline is drawn from its **cell footprint**, not from its
legacy `Radius` circle, so what the player sees is exactly the ground the
building takes. The placement ghost shows the same rect, snapped, in
valid/invalid colour.

---

## 5. Walls

Walls are the one deliberate exception, and only half of one.

- **The wall hub is the ONE building exempt from the grid (2026-09-24).**
  It keeps a **2 x 2 cell** footprint for placement legality, passability and
  selection — a round tower of radius `AlanthorWall.HubRadius` = 0.7 of a wall
  section (2.1 m), so the footprint is the tower's bounding square and the
  curtain meets the drum with no gap — but it does **not snap**. It stands
  exactly where it was placed.
- **Wall segments between hubs are freeform.** The curtain runs on the exact
  line between two hub centres at whatever angle that line has, and its
  instances are spaced to seal that line. Segments are not quantised, not
  snapped, and not required to be axis-aligned.

Forcing segments onto the grid would restrict walls to 45-degree runs and
break the terrain-sealing scan, which follows arbitrary bearings.

**Why the hub had to follow the curtain out.** A wall is DRAWN, and the run
cap inserts hubs along the stroke automatically. While the hub snapped and
the curve did not, every inserted hub landed up to ~1.4 m off the line the
player drew, and the wall visibly kinked at each one. Snapping the CURVE to
match only moved the kink into the curve itself. The two have to agree, the
curtain cannot be quantised, and so the hub is not either: a drawn wall now
runs exactly where it was drawn, with the drum centred on it.

Nothing else is exempt. The hub can afford to be because it is the only
building that is placed as part of a continuous line rather than on its own
patch of ground, and because its footprint is still declared — `BuildingSize`
2 x 2, stamped on the passability grid at whatever offset it lands, exactly as
a moving obstacle would be.

### Walls on the grid (2026-10-02)

> **How a freeform wall and a square grid share ground** — the Age of Empires
> IV answer. The wall is DRAWN smooth, but on the grid it OCCUPIES the
> stair-stepped cells it actually crosses, and buildings and walls are both
> tested against those cells. Neither can be laid through the other.

- **One occupancy truth: the nav grid (1 m cells).** Every wall piece is
  stamped onto it turned to its own heading — a stone curtain blocks a 5 m
  band (its 4 m plus a margin), a palisade 3 m, a hub a 5 m square — the
  moment it is placed, under construction or not. Buildings and obstacles
  stamp their footprints there too.
- **A building may not cover a wall cell.** Its footprint is tested against
  those cells (`BuildCommandHelper.OverlapsWall`) in the placement check, the
  executor's last-line re-check, a plan breaking ground, and the AI's site
  search. A wall piece's own box was axis-aligned while the wall was not, so a
  diagonal or curved wall left notches that building corners slotted into.
- **A wall is clear along its WHOLE length** (`CommandRouter.WallLineClear`).
  Every metre of the curtain's cross-section must be free of buildings,
  obstacles, other walls and impassable ground, and every two metres free of
  resource nodes and the owner's own plans. Within one hub radius + 2.5 m of a
  standing hub or wall cell the new wall JOINS, it may touch that wall. The
  draw tool marks the blocked stretch red; the executor refuses the whole wall,
  before any spend, on every peer. It used to check the new hub spots only.

### The terrain seal (2026-09-24)

A new hub throws a short stub of curtain at the nearest **impassable
terrain** within 9 m, so nothing squeezes between the tower and the rock it
was put against (`AlanthorWall.SealToTerrain`). That is the whole feature,
and it must fire **only against a real obstacle**:

- The blocked ground has to be a **face, not a speck** — the bearing's first
  blocked cell must be backed by more blocked cells behind it and beside it.
  A single slope-blocked cell from a bump in the terrain is not shelter, and
  sealing to one puts a stub of wall at an angle the player never asked for.
  That, not the feature, was the "wall hubs sprout segments in random
  directions" bug.
- **The map edge is not terrain — but it IS a seal target (2026-10-02).**
  `PassabilityGrid.GetCell` answers `TerrainBlocked` for anything off the
  grid, so the terrain scan must never read off-grid samples (that sealed hubs
  to the void on every bearing). The edge is handled on its own: units walk to
  the last cell, so a hub within the seal range (9 m) of an edge throws a
  curtain straight to it, square to the edge, measured from the grid's bounds —
  one seal per edge in range, two in a corner.
- **A seal obeys the whole-length rule.** Its curtain must be clear (no
  building, node, wall or impassable ground) from past the hub's joint to just
  short of the rock or edge it laps into, or it is not laid. A hub may carry
  several seals, never two on the same bearing.
- **Nothing seals before the mask is baked.** Gate on
  `PassabilityGrid.IsMaskReady`, never on `Cells.IsCreated`.
