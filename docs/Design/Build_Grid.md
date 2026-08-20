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
| **4 x 4** | 8 x 8 | Hall, Archery Range, Shrine of Ridan, Temple of Ridan, Vault of Almierra, King's Court, Wall Hub, Smelter, Siege Yards, Royal Stable, Runai Outpost / Trade Hub / Siege Workshop / Vault / Veilsteel Foundry, Feraldis Hunting Lodge / Logging Station / Longhouse / Foundry / Pasture, Mine, all four sect buildings |
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
| Veilstone outcropping | 1 cell, impassable, cleared when the node is exhausted |
| Iron deposit | 1 cell, impassable, cleared when exhausted |
| Veilsteel deposit | 1 cell, impassable, cleared when exhausted |
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

- **Wall hubs are buildings.** They snap to the grid like everything else,
  at **4 x 4 cells**.
- **Wall segments between hubs are freeform.** The curtain runs on the exact
  straight line between two hub centres at whatever angle that line has, and
  its instances are spaced to seal that line. Segments are not quantised,
  not snapped, and not required to be axis-aligned.

Forcing segments onto the grid would restrict walls to 45-degree runs and
break the terrain-sealing scan, which follows arbitrary bearings. The hub
graph carries the grid discipline; the curtain follows the ground.
