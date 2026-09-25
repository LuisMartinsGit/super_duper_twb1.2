# The Build Grid

**Status:** canonical. Supersedes every earlier per-building footprint number
in [Age_0.md](Age_0.md), [Age_1_Alanthor.md](Age_1_Alanthor.md),
[Age_1_Runai.md](Age_1_Runai.md) and [Age_1_Feraldis.md](Age_1_Feraldis.md).

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

Footprints are authored in **cells**, and the table below is the truth source.

> **Doubled 2026-08-13.** Every footprint is twice what it originally was —
> buildings read far too small against the units and the terrain. The grid
> itself is unchanged at 2 m, so placement keeps its fine granularity. The
> earlier rule that *a Hut is exactly one grid cell* is **superseded**: the Hut
> is still the smallest building, but it now spans 2 x 2 cells.

| Cells | Metres | Buildings |
|---|---|---|
| **1 x 1** | 2 x 2 | every Chapel — the statues docked in the Temple ring |
| **2 x 2** | 4 x 4 | Hut, Gatherer's Hut, Alanthor Watch Tower, Feraldis Tower, War Totem, Runai Trading Post |
| **2 x 2** | 4 x 4 | Wall Hub — a round tower whose radius is 0.7 of a wall section (2.1 m, 30 % smaller since 2026-09-21); the curtain starts at its rim |
| **4 x 4** | 8 x 8 | Hall, Archery Range, Shrine of Ridan, Temple of Ridan, Vault of Almierra, King's Court, Smelter, Siege Yards, Royal Stable, Runai Outpost / Trade Hub / Siege Workshop / Vault / Veilsteel Foundry, Feraldis Hunting Lodge / Logging Station / Longhouse / Foundry / Pasture, Mine, all four sect buildings |
| **5 x 5** | 10 x 10 | Barracks |
| **6 x 6** | 12 x 12 | Fiendstone Keep, Thessara's Bazaar, Border Main Node (the well) |

Unknown ids default to **4 x 4 cells**.

> **Temple halved back (2026-08-17).** The 2026-08-13 doubling had given the
> Temple of Ridan its own 8 x 8-cell class; in play the cathedral dwarfed
> everything around it. It now sits in the Hall class (4 x 4 cells), its
> chapel statues halve with it (1 x 1 cell), and `TempleChapelRing.SlotRadius`
> returns to the pre-doubling 3.95 m the docking was originally tuned for.

Consequences worth stating plainly, because they change how a base packs:

- Non-square footprints (`3x4`, `4x3`) are retired: at 2 m resolution a 3 m and
  a 4 m building are not distinguishable, so the distinction is dropped rather
  than faked.
- **Anything tuned against the old sizes has to move with them.** Concretely:
  `AlanthorWall.HubWidth`, `TempleChapelRing.SlotRadius` (the chapel ring docks
  against the Temple wall — at the old radius the whole ring would now sit
  *inside* the cathedral), the AI's `BuildRingDistanceMin/Max`,
  `MinBuildingSpacing` and `MinResourceNodeClearance`, and the starting-base
  worker/army offsets in `PlayerSpawnSystem`. At the old spacing two 12 m
  buildings overlapped, every AI candidate failed validation, and the starting
  army spawned inside its own Hall's blocked cells.

Building **visuals are scaled to their footprint** so the mesh fills its
cells with no overhang and no gap.

---

## 3. Resource nodes, curse nodes, trees and props

Everything in this class occupies **exactly one cell (2 x 2 m)**, is snapped
to that cell's centre, is scaled to fill it, and is **impassable while it
exists**.

| Thing | Rule |
|---|---|
| Veilstone outcropping | 3 x 3 cells, impassable, cleared when the node is exhausted |
| Iron deposit | 3 x 3 cells, impassable, cleared when exhausted |
| Veilsteel deposit | 1 cell, impassable, cleared when exhausted |
| **Supply spot** | **2 x 2 cells — the Gatherer's Hut's own footprint — and PASSABLE: it is ground you build the hut ON, not a prop beside it. Snaps with even parity (cell boundary) so the hut centres on it exactly. (2026-08-29; was 1 cell)** |
| Blight pocket / Small Node | 1 cell, impassable while alive |
| Border Main Node (well) | **6 x 6 cells** — it is a structure, not a node |
| Trees, rocks, bushes | 1 cell, impassable, at most one per cell |

"Impassable until mined" means the block is tied to the node's lifetime: the
node is removed when depleted, and removal releases the cell on both the nav
cost field and `PassabilityGrid`.

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
- **The map edge is not terrain.** `PassabilityGrid.GetCell` answers
  `TerrainBlocked` for anything off the grid, so a hub near the border would
  seal to the void on every bearing that runs off the map.
- **Nothing seals before the mask is baked.** Gate on
  `PassabilityGrid.IsMaskReady`, never on `Cells.IsCreated`.
