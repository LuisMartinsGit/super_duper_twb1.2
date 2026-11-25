# Making Alanthor buildings by hand

> **Status (2026-11-11): working guide.** How to build Alanthor buildings
> like the approved test Fortress ("PT3") by hand, in Blender and Unity,
> starting from the Synty POLYGON pieces already in the project. No scripts
> are needed for any step. The look rules (unit scale, verdigris roofs,
> player colour on a wall band and cloth) are summarised in
> [Art_Direction.md](Art_Direction.md) §13; this file is the how-to.
>
> Every measurement here is an ART measurement in metres. Game numbers
> (footprint cells, HP, costs) are on the SOs and are not repeated here.

## Kit prefixes, and no UK pieces (2026-11-24)

Every Synty kit prefab, and every Synty piece inside a building prefab, carries
the prefix of the kit it comes from:

| Prefix | Kit | Folder |
|---|---|---|
| `K_` | Knights (Fantasy Kingdom) | `Assets/Synty/PolygonFantasyKingdom` |
| `R_` | Ancient Empire | `Assets/Synty/PolygonAncientEmpire` |
| `J_` | Samurai Empire | `Assets/Synty/PolygonSamuraiEmpire` |

Only the prefabs are prefixed; their FBX, materials and textures keep the Synty
name. Unit prefabs keep their part names: code finds them by name. A piece with
a rise number keeps the number first (`1_K_Keep`).

**The unit kit (`UK_*`, `Art/UnitKit/`) is removed** from the project and from
every building prefab. Anything below that tells you to use a `UK_*` piece no
longer applies. The player-colour band material and the cut wall meshes stay, in
`Art/StripeBand/` (`Al_Band_Playercolor.mat`).

## How to use this guide

1. Read sections 1 to 3 once. They are the rules.
2. Pick the size recipe for your building (section 3).
3. Make any missing pieces in Blender (sections 5 to 7).
4. Compose the prefab in Unity (section 8) and bind it (section 9).
5. Tick the checklist in section 11 before you call it done.

Section 10 is a full worked example (the Fortress). Section 12 lists what
PT3 still gets wrong, so you can do better.

---

## 1. Why (the short version)

Our buildings looked chunky for one reason. Real-size Synty castle pieces
(5 m walls, 0.55 m thick roof slabs) were composed into a building, and then
the whole prefab was shrunk to about 0.40 to fit its footprint. A Spearman
is 2.0 m tall and is not shrunk. So doors came out 1 m tall and every edge
stayed as thick as a soldier's arm.

Four tests on the Garrison and the Fortress, checked against the AoE4
building sheets, gave the rules below. The fourth test, PT3, was judged
"almost perfect".

## 2. The ten rules

In short: **build at 1 : 1, make less of it, make it thinner.**

1. **Ground storey at unit scale, and very little of it.** Door leaf 2.0 to
   2.4 m. Small windows at head height. Low walls: the eaves sit a little
   above the door. If it does not fit, delete a bay or a storey. Never
   shrink it.
2. **Fit is 1.0.** Compose on a plot the size of the real footprint, so the
   game draws the prefab at scale 1. Never rely on the footprint fit to
   shrink a big composition.
3. **The roof does the work.** Half or more of the visible height is roof.
   Several roof planes at different heights. Steep, with an overhang.
4. **Thin edges.** Eave edge 6 cm (8 cm at most). Plaster walls 12 to 15 cm
   at openings. Posts 14 to 18 cm. Railings and bars 2 to 5 cm.
5. **Nothing dead straight.** Ridges sag a little. Slopes are concave.
   Eave corners turn up. Stone bases lean in (batter).
6. **Small tiles, painted detail.** Tiles about 22 x 24 cm, so a roof reads
   as one fine surface. Grime, weathering and tile-to-tile colour are
   PAINTED. Do not model chunky detail.
7. **Small things stick out.** Brackets, lanterns, ridge ornaments, banner
   poles. They break the outline.
8. **Bigger building = more volumes, not bigger ones.** Storey height never
   grows. A large building is a hollow compound, open toward the camera
   (court, U or ring), with the court floor raised so the camera sees in.
9. **Part of the plot is yard.** Unit-scale props say what the building
   does. Synty props go in at their native size.
10. **No paved slab.** A soft ground patch only. A foundation skirt at
    least 1 m below ground (the battered stone base does this job) so the
    building sits on slopes.

Two more that are about colour, not shape:

- **Player colour = a painted wall band at chest height + cloth.** Roofs
  never take the player colour.
- **Roofs are verdigris**: tiles the colour of corroded copper. Matte,
  never glowing. Veilstone is the glowing cyan; the roof must not be
  confused with it.

### The Alanthor style in one paragraph

A medieval blend of **Roman** and **Japanese**. Roman: white marble
columns, porticos, colonnades, podiums, pediments, arches. Japanese: tiered
keeps, curved tiled roofs, plaster-and-timber walls, roofed gates, corner
turrets (yagura), stone lanterns. Alanthor is the defensive "turtle"
culture, so every building must look STRONG: a battered mass at the base,
few and small openings, layered defences. And at the same time every edge
stays thin. Strength comes from the base and the layout, never from fat
pieces.

## 3. Size recipes

General rule: the storey is always the same height. Size changes how many
volumes you have and how much yard.

The plot side is the building's footprint: `footprintCells` on its
`BuildingDefSO`, times the 2 m cell ([Build_Grid.md](Build_Grid.md)). Read
it from the SO before you start. The examples below are what the SOs say
today.

| Class | Plot | Examples today |
|---|---|---|
| Small | 4 m | House, Mine, Farm, Watch Tower |
| Medium | 8 m | Archery Range, Royal Stable, Siege Yard, Temple |
| Garrison | 18 m | Garrison (2026-11-12: the hand-made L1 spans about 15 m between its corner markers; the plot leaves about 1.4 m free on each side) |
| Large | 10 m | Fortress |
| Special | varies | Vault of Almierra |

(The Market sits between Small and Medium at 6 m. Use the Medium recipe
with one volume less.)

### Small (4 m)

- One room. One door. Two or three small windows.
- Roof is at least half the height.
- A strip of yard, 1.0 to 1.5 m deep, on the front or one side, with two or
  three props.
- No court or ring: there is no room for one.

### Medium (8 m)

- One low hall OR one open shed, on 40 to 60 % of the plot.
- Several roof planes: the hall roof, a lean-to, a porch.
- A walled or fenced yard on the rest, with props that show the function.
- One accent: a slim turret, a chimney or a banner pole.

### Large (10 m)

- A walled compound: 3 to 5 volumes around a court.
- A gate with a GRAND frame and a SMALL door (the frame is grand, the door
  leaf stays 2.2 m).
- One taller element: the tiered keep.
- Court at least 35 % of the plot, open to the sky.

### Special (Vault, wonder-like)

A special building may use **mixed scale**. This is the trick the Ottoman
wonders in AoE4 use.

- The OUTER storey is at unit scale: real doors, real walls.
- The CORE is a miniature: stepped, drawn at about half scale, with many
  small repeated elements (tiny windows, tiny roofs, tiny merlons).
- The eye reads the outer ring for scale and takes the core for something
  huge and far away.

Use it only on a building that is meant to be a wonder. On an ordinary
building it reads as a dollhouse (we tried: PT2).

### Height caps

Bulk = the top of the main mass. Tip = the highest ornament.

| Class | Bulk at most | Tip at most |
|---|---|---|
| Small | 5 m | 6.5 m |
| Medium | 7.5 m | 9 m |
| Large | 11.5 m | 13.5 m |

The Large row is what PT3 was checked against. The Small and Medium rows
are proposed here and not tested yet. One rule is firm: nothing may stand
taller than the Fortress of the same level.

### Levels L1 to L3

General rule: **one frame, one scale, for the whole ladder.**

- Same origin, same floor height, same outer ring or yard at every level.
- A level ADDS things: a tier, a turret, a colonnade, ornaments, props.
- A level never widens the building and never raises the storey height.
- Pieces that exist at L1 stay exactly where they are at L2 and L3.

What PT3 does, as a model:

| | L1 | L2 | L3 |
|---|---|---|---|
| Keep tiers | 2 | 3 | 4 |
| Bulk height | 8.2 m | 9.8 m | 11.3 m |
| Roof planes | 28 | 33 | 45 |
| Court open | 42 % | 42 % | 36 % |

Constant at every level: compound wall about 4 m above ground, door leaf
2.2 m, skirt 1.3 m below ground, footprint 10 x 10 m.

---

## 4. Setup and references

### 4.1 What to open and take apart

| What | Where |
|---|---|
| Approved Fortress | `Assets/GameData/TechTree/Civs/Alanthor/Buildings/Fortress/ProportionTest/Fortress_PT3_Lvl1..3.prefab` |
| Unit kit (thin pieces) | `Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/UnitKit/` |
| Your Oriental kit | `Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Oriental/` |
| Failed tests (do not copy) | same `ProportionTest/` folder: `Fortress_PT_Lvl*` (scaled to 0.80), `Fortress_PT2_Lvl*` (mixed scale) |

Open `Fortress_PT3_Lvl3.prefab` in Prefab Mode first. Its root groups are
the build order of section 10: `Footprint`, `Podium`, `CompoundWall`,
`WallCoping`, `Gatehouse`, `GateCap`, `Yagura..`, `Keep`, `KeepCaps`,
`Colonnade`, `ColonnadeCap`, `Cloth`, `Yard`.

The unit kit holds one prefab per piece (`UK_*.prefab`), three materials
(`UK_Atlas`, `Al_Band_Playercolor`, `UK_Glow`), the painted texture
`UK_Atlas.png`, and `uk_manifest.json`, which lists every piece with its
bounds. You may use the kit pieces directly in a hand-made building. This
guide also shows how to make your own.

The visual target is your set of AoE4 building sheets (the Sperasoft
ones). They are not in the repository; keep them open on a second screen.

### 4.2 Synty pieces worth starting from

Everything usable is in **POLYGON Fantasy Kingdom**:

- Prefabs (for Unity): `Assets/Synty/PolygonFantasyKingdom/Prefabs/`
  (`Castle/`, `Buildings/`, `Props/`, `Environments/`).
- FBX (for Blender): `Assets/Synty/PolygonFantasyKingdom/Models/` (one flat
  folder).
- Textures: `Assets/Synty/PolygonFantasyKingdom/Textures/`
  (`Roof_Tile_01..04`, `Wall_Stucco_01`, `Wall_Brick_01`, the atlas under
  `Alts/`).

`Assets/Synty/PolygonGeneric/` is also installed; it has nothing this guide
needs.

**Pieces to re-shape in Blender** (too thick to use as they are):

| Piece (`SM_Bld_Castle_...`) | Native size | Use it for |
|---|---|---|
| `Wall_01` | 5.0 long, 5.0 high, 0.50 thick | plaster wall, stone base |
| `Wall_Cut_02` | 5.0 long, 2.5 high, 0.50 thick | low wall |
| `Wall_Cut_04` | 1.25 long, 5.0 high, 0.50 thick | narrow bay |
| `Wall_Arrowslit_03` | 5.0 x 5.0, 0.50 thick | loop slit reference |
| `Wall_Door_01` | 5.0 x 5.0, 0.63 thick | door opening reference |
| `Roof_S_01` | 5.0 long, 2.5 run, 5.0 rise | roof slope (after flattening) |
| `Roof_M_01` | 5.0 long, 3.75 run, 5.0 rise | roof slope |
| `Roof_M_Curved_01` | 5.0 long, 3.75 run, 3.75 rise | curved roof slope |
| `Roof_Cap_Square_01` | 2.45 square, 1.07 high | turret cap |
| `Pillar_Stone_Round_01` | 1.65 across, 5.0 high | marble column |
| `Pillar_Wood_02` | 0.83 square, 5.05 high | timber post |
| `Floor_Stone_01` | 5.0 x 5.0, 0.28 thick | court floor |

Compare those with the targets: wall 0.14 thick (Synty 0.50), roof edge
0.055 (Synty slab about 0.55), column 0.26 across (Synty 1.65), post 0.16
(Synty 0.83). That gap is the whole "chunky" problem.

**Props to use as they are, at native size** (they are already at unit
scale; all are `SM_Prop_...`):

| Prop | Native size (w x d x h) | Says |
|---|---|---|
| `Camp_Weapon_Rack_01` | 1.8 x 1.1 x 2.4 | barracks |
| `Weapon_Rack_02` | 2.0 x 0.7 x 1.6 | barracks |
| `Dummy_Armour_01` | 0.7 x 0.6 x 1.8 | training |
| `Dummy_Archery_01` | 1.0 x 0.4 x 1.9 | archery |
| `Target_01` | 0.7 x 1.0 x 1.0 | archery |
| `Knight_Armor_01` | 0.8 x 0.9 x 2.3 | armoury |
| `Barrel_01` | 0.7 x 0.7 x 0.9 | stores |
| `Barrel_Arrows_01` | 0.7 x 0.7 x 1.2 | archery |
| `Crate_Wood_01` | 1.1 cube | stores |
| `Sack_Stack_01` | 1.0 x 1.0 x 1.0 | stores, farm |
| `Hay_Bale_01` | 1.4 x 1.1 x 1.1 | stable |
| `Hay_Cart_01` | 1.7 x 2.6 x 1.2 | stable, farm |
| `Horse_Hitching_Post_01` | 0.3 x 2.9 x 1.7 | stable |
| `Anvil_01` | 0.3 x 0.8 x 0.5 | siege yard, mine |
| `Camp_Brazier_01` | 0.6 x 0.6 x 0.6 | guard post |
| `Fence_01` | 2.5 long, 1.1 above ground | yard edge |
| `Well_01` | 1.8 x 1.8, 2.2 above ground | house, court |
| `Statue_Knight_01` | 1.2 x 0.9 x 2.7 | court |

**Cloth** (player colour, see 8.6):

| Piece | Native size | Note |
|---|---|---|
| `SM_Prop_Flag_01` | 2.7 wide, 5.2 long | PT3 hangs it at scale 0.22 to 0.26 |
| `SM_Prop_Battle_Banner_01` | 0.75 x 2.0 | native size works |
| `SM_Prop_Banner_01` | 0.7 x 2.2 | hangs from its top |
| `SM_Prop_Flag_Horizontal_01` | 4.0 x 2.1 | scale to about 0.3 |

Traps with Synty pieces (all seen in this project):

- `SM_Prop_Fence_01` posts go 0.52 m below the pivot and `Street_Lamp_01`
  0.25 m. Check they do not poke out under a raised court.
- Synty's blue awnings and tents read as the Blue player. Do not use them.
- A castle wall at yaw 0 runs toward -X from its pivot and faces +Z. A
  `Roof_M_Curved_01` is the opposite (runs +X, slopes toward -Z). Place by
  looking, not by assuming the pivot.
- `Castle_Wall_Archway` across a passage shows as a thin slab. Build arches
  as in 5.9 instead.

### 4.3 Reference sizes

- A Spearman is 2.0 m tall. Keep his prefab next to your work, always. It
  is the `prefab` field of
  `Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Spearman/Spearman.asset`.
- Module: 2.5 m bays, 1.25 m halves, 1.0 m keep panels.
- Wall panel height: 2.3 m (compound walls), 2.0 m (keep storeys; each tier
  is scaled a little lower than the one below).

---

## 5. Making pieces in Blender

General rule for every piece: **work in metres, at 1 : 1, with the piece's
origin where you will want to snap it in Unity** (bottom centre unless noted).
Take the Synty mesh for its proportions and UV habits, then cut away most of
its thickness.

### 5.0 Blender shortcuts used in this guide

These are the keys of Blender's DEFAULT keymap (4.x / 5.x, left-click
select). A shortcut acts on the editor under the mouse: keep the pointer
over the 3D view for mesh keys, over the UV editor for UV keys, over the
Modifiers tab for modifier keys.

General:

| Action | Shortcut |
|---|---|
| Object Mode / Edit Mode | Tab |
| Mode pie (Texture Paint and the others) | Ctrl+Tab |
| Vertex / edge / face select (Edit Mode) | 1 / 2 / 3 |
| Sidebar (Item, Dimensions) | N |
| Search any menu entry by name | F3 |
| Change the settings of the step you just did | F9 |
| Add menu (mesh, lattice, empty, curve) | Shift+A |
| Rename the active object | F2 |
| Save the .blend | Ctrl+S |
| Undo / redo | Ctrl+Z / Ctrl+Shift+Z |
| Quick Favourites menu | Q |

Views:

| Action | Shortcut |
|---|---|
| Front / back | Numpad 1 / Ctrl+Numpad 1 |
| Right / left | Numpad 3 / Ctrl+Numpad 3 |
| Top / bottom | Numpad 7 / Ctrl+Numpad 7 |
| Perspective / orthographic | Numpad 5 |
| Frame the selection | Numpad . |
| Work on the selection alone (local view) | Numpad / |
| View pie (no numpad needed) | ` (the key under Esc) |
| Wireframe / solid pie | Z |
| Hide / show all | H / Alt+H |

Selecting:

| Action | Shortcut |
|---|---|
| Select all / none | A / Alt+A |
| Invert | Ctrl+I |
| Edge loop / add another loop | Alt+click / Shift+Alt+click |
| Edge ring | Ctrl+Alt+click |
| Everything connected to what is under the mouse | L |
| Everything connected to the selection | Ctrl+L |
| Grow / shrink the selection | Ctrl+Numpad + / Ctrl+Numpad - |
| Box select | B (or drag) |

Moving and typing values:

| Action | Shortcut |
|---|---|
| Move / rotate / scale | G / R / S |
| Lock to an axis | X, Y or Z after G / R / S |
| Lock to a plane (all but one axis) | Shift+X, Shift+Y or Shift+Z |
| Type an exact value | digits after the axis, then Enter (for example G, Z, 0.035) |
| Slide an edge or loop along the surface | G, G |
| Snap on / off | Shift+Tab (or hold Ctrl while moving) |
| Choose what to snap to | Ctrl+Shift+Tab |
| Proportional editing on / off | O |
| Proportional falloff pie | Shift+O |
| Proportional radius, while moving | mouse wheel (or Page Up / Page Down) |
| Place the 3D cursor | Shift+right-click |
| Snap pie (Cursor to Selected, Selection to Cursor) | Shift+S |
| Apply transforms menu (Object Mode) | Ctrl+A |

Mesh editing (Edit Mode):

| Action | Shortcut |
|---|---|
| Loop cut | Ctrl+R (type or scroll the number of cuts, Enter, then right-click to leave it centred) |
| Extrude | E |
| Extrude menu (Faces Along Normals) | Alt+E |
| Knife | K (A = angle snapping, Enter = confirm) |
| Split menu (Faces by Edges) | Alt+M |
| Merge menu (At Center) | M |
| Separate menu (Selection) | P |
| Duplicate | Shift+D |
| Delete menu (Vertices / Edges / Faces) | X |
| Fill a face | F |
| Recalculate normals outside | Shift+N |
| Join objects (Object Mode) | Ctrl+J |

Modifiers (mouse over the Modifiers tab):

| Action | Shortcut |
|---|---|
| Add Modifier menu | Shift+A |
| Apply the modifier under the mouse | Ctrl+A |
| Duplicate / delete the modifier under the mouse | Shift+D / X |

UV editor and painting:

| Action | Shortcut |
|---|---|
| UV menu (Unwrap, Project From View), in the 3D view | U |
| Select a whole island under the mouse | L |
| Move / scale an island | G / S (then X for U only, Y for V only) |
| Brush size / strength (Texture Paint) | F / Shift+F |
| Save the painted image (mouse over the image) | Alt+S |

These actions have NO default shortcut. Use the menu path given in the
step, or press F3 and type the name:

- File > Import > FBX and File > Export > FBX
- Select > Select Linked > Linked Flat Faces
- Object > Set Origin (also in the right-click menu in Object Mode)
- Object > Convert (also in the right-click menu in Object Mode)
- Shade Smooth / Shade Auto Smooth (right-click menu in Object Mode)
- Mesh > Transform > Randomize
- Mesh > Sort Elements
- Remove Unused Slots, Assign (material slot list)
- Overlays > Face Orientation and Edge Length

If you use one of them often, right-click its menu entry and choose
**Add to Quick Favorites** (it is then under Q), or **Assign Shortcut**.

### 5.1 Scene setup (once per .blend)

1. Scene Properties > Units: Unit System **Metric**, Unit Scale **1.0**,
   Length **Meters**.
2. Viewport Overlays > Guides: grid Scale **0.25** (a 2.5 m bay is 10 squares).
3. Turn on Snap (the magnet, Shift+Tab) and snap to the grid / Increment
   (Ctrl+Shift+Tab opens the list).
4. Edit Mode (Tab) > Viewport Overlays > Measurement: tick **Edge Length**.
   You will read thicknesses from it all day.
5. Sidebar (N) > Item shows Dimensions; you can type sizes there.

### 5.2 Importing a Synty FBX

1. File > Import > FBX (.fbx) (no shortcut; F3 and type "fbx"). Pick the
   file from `Assets/Synty/PolygonFantasyKingdom/Models/`. Leave the importer
   defaults.
2. The object arrives with Scale 0.01 and Rotation X 90 degrees. It LOOKS the
   right size (a `Wall_01` is 5 m), but the mesh data is not. Select it and
   run Object > Apply > **All Transforms** (Ctrl+A).
3. Check Dimensions in the sidebar against the table in 4.2.
4. Material slots come in with junk names (`lambert1`, `Wall21`, `Roof`).
   Rename them now to the slot names of 6.1 (double-click the slot name;
   F2 renames the object).
5. The UV map is called `map1`. If you will Join (Ctrl+J) Synty meshes with
   your own, name your UV map `map1` too. Join matches UV maps by NAME, and
   a mismatch leaves one part sampling a single pixel.

Never save over a file in `Assets/Synty/`. Save your .blend in the building's
own `Art/` folder (see 7.3).

Axes. With the export settings of 7.1:

| In Blender | Becomes in Unity |
|---|---|
| +Z (up) | +Y (up) |
| -Y (what Front view, Numpad 1, shows you) | +Z |
| +Y | -Z |
| +X | -X |

The unit kit's rule is "a piece's outside face looks toward Unity -Z at yaw
0". In Blender that is the **+Y** side, the side you see in Back view
(Ctrl+Numpad 1). Follow the same rule and your pieces mix with the kit
without thinking. Export one test piece first and check it in Unity before
you make twenty.

### 5.3 A thin-eaved roof slope from a Synty roof slab

Start: `SM_Bld_Castle_Roof_S_01` (5.0 long, 2.5 run, 5.0 rise, a solid slab
about 0.55 m thick).

Target: a skin with a 5.5 cm edge, lower pitch, small tiles, a concave slope.

| Target | Value |
|---|---|
| Edge thickness at the eave | 5.5 cm (8 cm is the limit) |
| Pitch of a main roof | rise 1.15 over run 1.7 (about 34 degrees) |
| Pitch of a keep top roof | rise 1.1 to 1.5 over run 1.35 to 1.95 |
| Overhang past the wall line | 0.4 to 0.85 m |
| Tile | 22 cm wide, 24 cm course |
| Course step | 3.5 cm proud of the course below |

Which edge is the eave: on a slope piece the **eave** is the LOW long
edge, the one that hangs out past the wall; the **ridge** is the HIGH long
edge. Synty models the eave as separate pieces (`SM_Bld_Castle_Roof_*_Edge_*`
and the `_Edge_Corner_` pieces) that you snap under the slab. Do not import
those. Here the eave is not a piece: it is the slope itself carried past the
wall line by the overhang, and its thin edge comes from Solidify in step 9.
The origin you set in step 4 marks the wall line, so everything on the low
side of the origin is eave.

Steps:

1. Import and apply transforms (5.2).
2. Keep only the top skin. Edit Mode (Tab), face select (3). Click one tile
   face on top, then Select > Select Linked > **Linked Flat Faces** (no
   shortcut). Then Select > Invert (Ctrl+I), and Delete > Faces (X > Faces).
   The underside, the ends and the timber are gone.
3. Set the pitch. In Object Mode (Tab) type Dimensions in the sidebar (N):
   X = your bay (2.5 or 5.0), Y = the run including overhang (for example
   1.7), Z = the rise (1.15). Apply the scale (Ctrl+A > Scale).
4. Put the origin on the wall line at the level of the eave: 3D cursor
   there (Shift+right-click; or select a vertex and Shift+S > Cursor to
   Selected), then in Object Mode Object > Set Origin > Origin to 3D Cursor
   (right-click > Set Origin).
5. Cut the courses. Ctrl+R across the slope; type the number of cuts =
   slope length / 0.24, minus 1. For a 2.05 m slope that is 8 cuts. Press
   Enter, then right-click to leave the cuts evenly spaced.
6. Cut along the eave too (Ctrl+R again): one loop about every 0.55 m (4
   cuts on a 2.5 m piece). The curve steps need these.
7. Step the courses (this is what makes the fine shadow lines). Edge select
   (2). Select the course lines (Alt+click the first across-slope loop,
   Shift+Alt+click each of the others), then
   **Mesh > Split > Faces by Edges** (or press Alt+M and pick Faces by
   Edges). The Edge menu has no split entry in Blender 4.x / 5.x. Nothing
   moves on screen, but each course is now a loose strip: check by hovering
   one and pressing L, which should select that strip only. For each strip,
   select its LOWER edge and move it up 3.5 cm (G, Z, 0.035). Then select
   all lower edges and extrude down 3.5 cm (E, Z, -0.035) to close the
   little riser under each lip.
   - Faster: build ONE strip (bay wide, 0.24 deep, lower edge raised 0.035,
     with its riser), lying flat. Add an **Array** modifier (Shift+A with
     the mouse over the Modifiers tab), Fit Type Fixed Count, Relative
     Offset off, **Constant Offset** Y = 0.24, Count = number of courses.
     Apply it (Ctrl+A with the mouse over the modifier), then rotate the
     whole thing to the pitch (R, X, the angle).
8. Make it concave. Add > **Lattice** (Shift+A > Lattice), scale it (S) in
   Object Mode to enclose the slope, set Resolution U 5 (along the eave),
   V 3 (up the slope), W 1. Give the roof a Lattice modifier pointing at it.
   In the lattice's Edit Mode (select the lattice, Tab), move points with
   G, Z and a typed distance:
   - pull the middle row DOWN by about a tenth of the rise (12 to 13 cm for a
     1.15 m rise). The slope is now flatter at the eave, steeper at the ridge;
   - pull the two eave-corner points UP 0.18 to 0.26 m (the upturned
     corners; only on end pieces and hips);
   - pull the middle top points DOWN 3 to 5 cm (ridge sag).
9. Give it its edge. Add a **Solidify** modifier: Thickness 0.055, Offset -1
   (grows downward), Even Thickness on, Fill Rim on. Under Materials set the
   Rim offset so the rim lands on your edge/ridge slot.
   If you stepped the courses in step 7, use only 0.02 here: step plus
   solidify must total about 5.5 cm at the eave.
10. Apply the modifiers (Lattice first, then Solidify; Ctrl+A with the
    mouse over each one), or leave them live in the .blend and tick Apply
    Modifiers on export (7.1).

If the lattice does not move the roof: the roof needs the loop cuts of
steps 5 and 6 (a lattice only moves vertices that exist), the modifier must
be on the mesh object itself (not on a parent), its Vertex Group field must
be empty, and you must move the lattice points in the lattice's Edit Mode.

Simple Deform (Bend) also works for a plain curved slope: Add Modifier >
Simple Deform, Bend, Axis X (along the eave), Angle 12 to 18 degrees. Use the
lattice when you need the upturned corners.

Proportional editing (O, falloff Smooth from the Shift+O pie; while moving,
scroll to set the radius to about one third of the eave) is the quick way to
lift ONE corner by hand.

```
   section through a slope (not to scale)

                      ridge roll 12 x 15 cm
                     _/
                 __--     <- steeper at the top
             __--
         __--             <- courses 24 cm, each lip 3.5 cm proud
   ___---                 <- flatter at the eave
  |___  edge 5.5 cm
      \_ soffit (dark timber colour)
  |<-->|  overhang 0.4 - 0.85 m      | wall line
```

### 5.4 Small tiles

General rule: tiles are a TEXTURE on stepped courses. Do not model single
tiles.

- One course = one strip of the tile texture, mapped once across its 24 cm.
- Along the eave the texture repeats every tile (22 cm).
- Shift every second course half a tile sideways (11 cm). In the UV editor
  select the islands of alternate courses (L with the mouse over each
  one; Shift+L takes one back out) and move them in U by half a tile (G, X, the distance).
- Use three different tile strips and deal them out to the courses in an
  irregular order (the kit uses a 0, 1, 2 pattern that shifts with the row).
  This is the cheap tile-to-tile variation: no two neighbouring courses
  show the same tiles.
- Using Synty's `Roof_Tile_01..04` textures: they are drawn for big tiles.
  Scale the UV islands up (S in the UV editor) until Edge Length divided by the tiles you count
  across an edge is 0.22 m. Their teal is also not verdigris; see 6.3.

### 5.5 Hips, corners, ridges

- **Hip corner**: two slopes meeting on a diagonal. Make one slope 1.7 m
  long, cut it along the 45 degree line (best with Mesh > Bisect: select all, drag
  the line, then in the F9 panel type Plane Normal 0.7071, 0.7071, 0 and
  the corner as Plane Point; or Knife, K, then A for angle snapping, type
  45 for the step, Enter to confirm), and mirror it across that line (Mirror
  modifier with an Empty, Shift+A > Empty, rotated 45 degrees with R, Z, 45
  as Mirror Object). Lift the outer eave corner 0.2 m with proportional
  editing (O, then G, Z, 0.2) BEFORE mirroring.
- **Hip roll**: a bar 9 cm wide, 7 cm high, lying on the hip line. Select
  the hip edge loop (Alt+click), Shift+D, right-click to drop the copy in
  place, P > Selection, then in Object Mode Object > Convert > Curve
  (right-click > Convert To > Curve). In the curve's Geometry panel set
  Bevel Depth 0.04, Resolution 1. Convert back to mesh (right-click >
  Convert To > Mesh). Finish the low end with a small turned-up tip (7 cm
  out, 5 cm up).
- **Ridge roll**: the same, 12 cm wide and 15 cm high, sagging 3 to 5 cm in
  the middle.
- **Ridge-end fin**: a thin bar (6 x 7 cm) curling up and out about 36 cm,
  tapering to a third of its width. Gilt on the capital's top roof only.
- **Coping** (the little roof on a wall top): a gable 0.62 m wide, 0.17 m
  rise, no upturn, no fins.
- **Skirt roof** (the roof between two tiers of a keep): four slopes from an
  outer eave rectangle up to the wall of the tier above. The eave stands
  0.75 to 0.85 m outside that wall; rise 0.45 to 0.55 m; corners lifted 0.2
  to 0.24 m. Add a 10 x 6 cm flashing bar where tiles meet the wall.

### 5.6 A battered stone base

Start: `SM_Bld_Castle_Wall_01` (or a cube; the Synty wall is just a box).

| Target | Value |
|---|---|
| Height above ground | 1.2 m (compound podium), 1.0 m (keep base), 0.5 m (turret base) |
| Depth below ground | 1.3 m, vertical (the skirt) |
| Batter | 0.40 m lean over 1.2 m of height |
| Top ledge | 0.35 m deep |
| Run lengths | 2.5 m and 1.25 m, plus a corner piece |

Steps:

1. Make a block (Shift+A > Mesh > Cube) 2.5 long, 0.75 deep, 2.5 tall (type
   the Dimensions in the sidebar, N, then Ctrl+A > Scale). Put its bottom at
   Z = -1.3 and its top at Z = +1.2.
2. Ctrl+R one horizontal loop; set it to Z = 0 (ground) by typing Z in the
   sidebar (N > Item > Median). The part below stays vertical.
3. Select the top OUTER edge only and move it back 0.40 (G, Y, 0.40). The
   face above ground now leans in.
4. Now cut the courses, AFTER the lean (so they follow it): loops at Z =
   0.27, 0.58, 0.86. Uneven on purpose. One more below ground at -0.55.
5. Ctrl+R vertical loops about every 0.5 m.
6. Roughen. Select the interior vertices of the outer face only (not the two
   ends, not the top and bottom rows, or runs will not join).
   Mesh > Transform > **Randomize** (no shortcut; F3 and type "randomize"),
   Amount 0.02 (set it in the panel that F9 opens). Then slide a few
   vertical loops sideways (Alt+click the loop, then G, G) by up to 5 cm so
   the joints do not line up.
7. Delete the back and bottom faces (X > Faces; never seen).
8. Origin: at ground level (Z = 0), under the top outer edge, centred along
   the run. In Unity the top edge then sits exactly on the line you place
   the piece on, and the foot spreads 0.40 m outward.

The corner piece is the same profile turned through 90 degrees; its origin
is the top outer corner. A free-standing base (the keep's) is a block whose
four faces all lean in 0.2 m over 1.0 m.

Dry-stone, not brick: PT3's base reads as regular brick. Vary the course
heights more (0.2 to 0.45 m), vary stone lengths 0.3 to 0.8 m in the
texture, and let a few big stones span two courses.

```
   wall section (not to scale)            heights above GROUND

        _/\_   coping 0.62 wide            4.06
        |  |
        |  |   plaster 0.14 thick
        |##|   BAND 1.12-1.36 above the    2.32 - 2.56
        |  |   wall foot (chest height
        |  |   for a man on the court)
   _____|__|_  court floor                 1.20
   \        |
    \ stone |  batter 0.40 in 1.2 m
  ___\      |  ground                      0.00
      |     |
      |     |  skirt, vertical            -1.30
      |_____|
```

### 5.7 A thin plaster wall

Start: `SM_Bld_Castle_Wall_01`.

1. Import, apply transforms. Type Dimensions: X 2.5 (or 1.25), Y **0.14**,
   Z **2.3**. Apply scale.
2. Origin at the bottom centre of the wall line.
3. Ctrl+R horizontal loops at Z = 0.13 (top of the foot rail), 1.12 and 1.36
   (the band), 2.20 (bottom of the head rail).
4. **Timber rails.** Select the foot strip faces (front, back) and
   Alt+E > Extrude Faces Along Normals, 0.012. Same for the head strip. Map
   them to the timber colour.
5. **The band.** Select the band faces on the OUTSIDE only. Extrude along
   normals (Alt+E again) by -0.012 (a 1.2 cm recess). Assign these faces to
   their own material slot, `Band` (see 6.4): pick the slot in the Material
   tab and press Assign (no shortcut). Give the little ledge above it a dark
   colour.
6. Delete the bottom face (X > Faces).

Keep-storey walls are the same without the band: 1.0 long, 2.0 high, 0.12
thick.

Openings (cut in a copy of the plain wall):

| Opening | Size | Where |
|---|---|---|
| Loop slit | 10 cm wide, 55 cm tall | 1.5 to 2.05 m above the wall foot |
| Window | 48 cm wide, 50 cm tall | 1.5 to 2.0 m above the wall foot |
| Single door | leaf 1.0 wide, 2.2 tall | frame posts 12 cm, head 14 cm |
| Gate | two leaves 0.91 wide, 2.2 tall | frame posts 18 cm, to 2.5; lintel to 2.58 |

How to cut one by hand:

1. Ctrl+R two vertical and two horizontal loops; type their positions (in
   the sidebar, N, or slide one with G, G).
2. Select the face they enclose on the outside (face select, 3). Extrude it
   inward 7 cm (E, then type -0.07) for a slit or window; delete it for a
   door (X > Faces).
3. Colour the recess back dark, its sides dark timber.
4. Window extras: a sill and a head 4 to 5 cm thick sticking out 1.5 cm, and
   two upright bars 2.4 cm wide.
5. A door leaf is a separate thin box (6 cm) so you can name it. Name the
   leaf object with "Door" in it (the Proportion Probe looks for that word).

A Boolean modifier (Difference, a cube as cutter; no default shortcut)
also works. Clean the
result: it leaves n-gons, which is fine for FBX, but check the UVs.

### 5.8 Timber

- Post: 16 x 18 cm, 2.3 m (or 14 x 16 cm, 2.0 m). Start from
  `SM_Bld_Castle_Pillar_Wood_02` and type the Dimensions, or use a cube.
- Beam: 12 x 14 cm, 2.5 m.
- Bracket: an arm 36 cm long, 5 x 8 cm, a 22 cm drop against the wall and a
  diagonal strut between them.
- Bargeboard on a gable end: 4 cm thick, 15 cm deep, following the slope
  curve 16 cm under the tiles.

### 5.9 Marble: column, entablature, pediment, arch

**Column.** Start from `SM_Bld_Castle_Pillar_Stone_Round_01` for the look,
but it is quicker to lathe a new one:

1. Add a Plane (Shift+A > Mesh > Plane), merge it to one vertex (Edit Mode,
   Tab; select all, A; M > At Center), put that vertex at X 0.20, Z 0, and
   extrude it (E) point by point to draw the half profile in Front view
   (Numpad 1), on the +X side of the Z axis. Type each point's X and Z in
   the sidebar (N).
2. Profile for a 2.6 m column, radius 0.13 (radius, height):

| Part | Radius | Height |
|---|---|---|
| Base, bottom | 0.20 | 0.00 |
| Base, top | 0.20 | 0.05 |
| Base, groove | 0.17 | 0.09 |
| Base, ring | 0.18 | 0.13 |
| Shaft start | 0.14 | 0.17 |
| Shaft, one third up | 0.131 | 0.95 |
| Shaft, top | 0.119 | 2.38 |
| Neck | 0.117 | 2.40 |
| Ring | 0.133 | 2.43 |
| Groove | 0.12 | 2.46 |
| Capital flare | 0.169 | 2.53 |

3. Add a **Screw** modifier: Axis Z, Angle 360, Steps 12 (16 if you like),
   Merge on. Shade smooth (Object Mode, right-click > Shade Smooth).
4. Add the abacus: a square slab 0.39 x 0.39, 7 cm thick, on top (to 2.60).
5. Origin at the bottom centre.

The shaft is the point: almost straight for the first third, then tapering
(entasis). A straight cylinder reads as a pipe.

Heights in use: **2.2 m** (colonnades), **2.6 m** (porticos), **3.6 m**
(radius 0.16; for a grand front). Change only the shaft length.

**Entablature**: a beam 2.5 long, 0.30 high, 0.28 deep, with the top 5 cm
stepping out 6 cm on the front (the cornice). Extrude an L-shaped profile.

**Pediment**: a triangle 3.2 wide, 0.72 high, 0.28 deep. A raking cornice
7 cm thick on the two slopes, sticking out 6 cm. A small gilt device in the
middle.

**Arch**: opening radius 0.8 m; jambs 1.7 m tall, 22 cm wide, 20 cm deep,
each with a small impost block at the top; the ring 22 cm thick in 12
segments. Draw the half-circle with Add > Mesh > Circle (Shift+A; set 24
vertices in the panel that F9 opens), delete the lower half (X > Vertices),
extrude (E) and Solidify. Overall about 1.8 wide, 2.6 high.

**Pilaster** (a flat column against a wall): 20 cm wide, 5 cm deep, 2.6
tall, with a base block and a cap block.

### 5.10 Lanterns and ornaments

| Piece | Key sizes |
|---|---|
| Stone lantern | 1.52 tall. Base radius 0.20; shaft radius 0.08 up to 0.78; platform radius 0.17 to 0.90; light box 0.24 cube, 0.90 to 1.14 (slot `Glow`); cap radius 0.27, finial to 1.52. 8 sides |
| Hanging lantern | box 16 x 16 x 22 cm (slot `Glow`), cap radius 0.13, hanger rod 16 cm |
| Ridge ornament | fin 38 cm tall, 7 x 8 cm at the root, gilt |
| Banner pole | 5 x 5 cm, 3.4 m tall, crossbar 68 cm wide at 3.2 m, small finial |
| Eagle standard | pole 6 cm, 2.9 m; crossbar; wings to 3.2 m; gilt |

Lathe the round ones with the Screw modifier (8 steps), as for the column.

### 5.11 Naming and origin rules

- Name by what it is and its size in decimetres: `Kawara_Slope_25`,
  `Plaster_Wall_25_Slit`, `Marble_Column_26`, `Base_Run_125`.
- **Never put "roof" in a name.** Use "Kawara" (roof tile) or "Cap". See 8.5.
- Do not put "stripe" in a name either (8.5).
- Do not start your own files with `UK_`: the kit script owns that prefix.
- Origins: walls, posts, columns, lanterns at the bottom centre. Roofs at
  the wall line, at eave level. Bases at ground level under the top outer
  edge. Hanging things at the hook.
- One object per FBX, one FBX per piece. Then a prefab per piece in Unity.

---

## 6. UVs, textures and materials

### 6.1 Three slots are enough

| Slot | What uses it | Unity material |
|---|---|---|
| `Atlas` | everything painted: tiles, plaster, stone, marble, timber, door | one textured material |
| `Band` | the recessed chest-high band only | a material with **playercolor** in its name |
| `Glow` | lantern panes | an emissive material |

The unit kit's three materials do exactly this and you may reuse them:
`UK_Atlas.mat`, `Al_Band_Playercolor.mat`, `UK_Glow.mat` in
`Civs/Alanthor/Buildings/Art/UnitKit/`.

### 6.2 Synty's atlas way and the painted way

**Synty's way** (flat palette): every face's UVs are shrunk onto one small
colour patch of the atlas (`PolygonFantasyKingdom_Texture_01_A`). One colour
per face. It is fast, and it is why Synty reads as flat and blocky when you
see it close: no grime, no variation, no texture scale to tell your eye how
big the thing is.

**The painted way** (what PT3 uses): the UVs are laid out at a real scale
on a painted strip, so the surface carries small detail.

General rule: **big surfaces are painted; small parts may stay flat.** Flat
colours are fine for iron, gilt, bars, the dark inside of a slit.

### 6.3 One atlas, in strips

Keep everything on ONE texture. The kit's `UK_Atlas.png` is 1024 x 1024,
built of horizontal strips that repeat along U only. Lay your UVs on the
same strips and your pieces match the kit with no new material.

| Strip | Pixel rows (from the top) | One full width = |
|---|---|---|
| Tile A, B, C | 0-80, 80-160, 160-240 | 3.52 m (16 tiles) |
| Ridge / edge copper | 240-272 | 2.0 m |
| Plaster | 272-496 | 4.0 m |
| Stone | 496-720 | 4.0 m |
| Marble | 720-816 | 2.0 m |
| Timber | 816-864 | 2.0 m |
| Door | 864-976 | 1.76 m (8 planks) |
| Swatches (8 flat colours) | 976-1024 | n/a |

The swatches, left to right: gold, iron, earth, dark, paper, flag, soffit,
moss.

How to UV a face onto a strip:

1. In Blender, open `UK_Atlas.png` in the UV editor.
2. Select the faces, U > Project From View (or Unwrap), then scale and move
   the island (S and G, mouse over the UV editor) so its height spans the
   strip top to bottom.
3. Scale in U (S, X) so the island's width matches the real size: a 2.5 m plaster
   wall covers 2.5 / 4.0 = 0.625 of the texture width.
4. A UV island may run off the side: the texture repeats along U.
5. Keep 2 pixels clear of the strip's top and bottom edge, or the next strip
   bleeds in at a distance.

Which way up:

- Plaster and stone: the strip's BOTTOM is the foot of the wall (that is
  where the grime and moss are painted). Always map the full strip height to
  the full wall height.
- Tiles: the strip's bottom is the lower lip of the course.

The texture must import with Wrap Mode U = Repeat, V = Clamp, mip maps on,
Trilinear, Aniso 4 (the kit's atlas is set that way).

### 6.4 The palette

Take colours from here. They are the kit's painted values today
(`tools/blender/alanthor/build_alanthor_unit_kit.py`, read 2026-11-11). Tune
the tile colours in the game, under the real light, against a veilstone
outcrop.

| Surface | Base | Variation |
|---|---|---|
| Roof tile (verdigris patina) | `#4D8F7D` | each tile 80-115 % brightness |
| Tile, chalky (about 1 in 5) | `#99C7AD` mixed in | pale rain streaks down the slope |
| Tile, wet / joints | `#12302E` | between tiles, under each lip |
| Tile, worn to bronze (about 1 in 8) | `#6E4A26` | also on the lower lip |
| Ridges, hips, eave edge | `#2F584D` | patches of bronze `#6E4A26` |
| Plaster | `#EDE7DB` | soft blotches, streaks under the eaves |
| Plaster grime (foot) | `#6B5C47` | rises over the bottom 40 % |
| Stone | `#807A73` | each stone 78-120 % brightness |
| Stone dirt / moss (foot) | `#4D4530` / `#384D24` | bottom 45 % / bottom 15 % |
| Marble | `#F2EFE6` | veins `#9E9480`, thin and few |
| Timber | `#332217` | long grain, 70-130 % |
| Door planks | `#54361F` | iron straps `#1A1A1C`, studs `#575452` |
| Gilt | `#DBA12E` | flat |
| Soffit / recess | `#291C14` | flat |
| Lantern paper | `#FADB99` | flat, emissive |
| Ground patch | `#665742` | flat |

Verdigris, safe range: hue 155 to 172 degrees, saturation 35 to 50 %, value
45 to 60 %. Veilstone is `#5FD8E8` (hue about 187, bright, and it glows).
Keep the roof greener, duller and darker than that, and never emissive.
Stay out of hue 190 to 260 altogether: that band is the old player-colour
marker (8.5).

### 6.5 Painting the textures by hand

You can paint in any 2D editor (Krita, Photoshop, GIMP) or in Blender's
Texture Paint. Either way, work on a copy first.

General rules:

- Every strip must tile left to right. Paint with the editor's wrap-around
  mode on (Krita: W; Photoshop: Filter > Other > Offset to check the seam).
- Nothing is one flat colour. Every big surface gets three layers: a base
  with per-element variation, a top-to-bottom gradient, and streaks.
- Paint the shading in. A tile is lighter along its crown and darker at
  its edges and under the lip above. A stone course is lighter along its top
  edge.

Recipes:

- **Verdigris tiles.** 16 tiles across a strip. Fill each with the patina,
  each a slightly different brightness. Make about a fifth chalky, about a
  tenth dark and wet, about a tenth worn through to bronze. Add a soft light
  band down each tile left of centre (the round tile's highlight). Darken
  the top 10 % (shadow of the course above) and draw a 2-pixel dark teal
  joint between tiles. Add thin pale streaks running down. Finish the lower
  lip with a little bronze. Paint three different strips.
- **Plaster.** Warm off-white. Large soft blotches (7 % darker), fine grain
  (3 %). Thin vertical rain streaks fading out over the top half. Then the
  grime: brown rising from the foot over the bottom 40 %, strongest at the
  very bottom, with an uneven upper edge. PT3's grime is too faint; go about
  twice as strong as the kit.
- **Dry stone.** Courses of uneven height. Stones of uneven length, each
  its own grey-brown. Dark joints 2 pixels wide, a lighter line on each
  stone's top edge. Dirt over the bottom half, moss patches along the bottom
  15 %.
- **Marble.** Warm white with a few thin grey-gold veins on a diagonal.
  Less is more. A soft shadow at the foot.
- **Timber.** Very dark brown with long horizontal grain streaks.
- **Studded door.** Eight vertical planks, each a different brown, with a
  dark gap line. Two iron straps across (at about 1/4 and 3/4 height) with a
  round stud on every plank.

In Blender Texture Paint (Ctrl+Tab > Texture Paint in the 3D view): split
a UV editor beside the 3D view, pick the image, set the brush to the
palette colour (F sets the brush size, Shift+F its strength), and use
Stroke > Stabilize for straight streaks. Save the image (Image > Save, or
Alt+S with the mouse over the image) every time; Blender does not save
painted images with the .blend.

### 6.6 Tile variation tricks

- **UV offset** (the one that works here): map different courses to
  different tile strips, and shift alternate courses half a tile. Also slide
  each roof PIECE's UVs by a random whole number of tiles, so two pieces side
  by side do not show the same tiles.
- **Vertex colours** do nothing in this project: the building shader
  (`TheWaningBorder/BuildingLit`) has no vertex-colour input. Do not spend
  time on them.

### 6.7 Unity materials

- Building materials use the shader `TheWaningBorder/BuildingLit`. A
  material left on URP Lit is switched at run time with a one-time warning;
  `Waning Border > Buildings > Convert Materials To BuildingLit` converts
  them on disk.
- Smoothness low (about 0.1), Metallic 0. Copper tiles included: verdigris
  is matte.
- Do not write a custom shader for a building. (If you ever must: URP takes
  no shadow or depth pass from a `Fallback`, so the object drops out of
  shadows, ambient occlusion and the ink outline.)

---

## 7. Exporting from Blender

### 7.1 The settings

Select ONE piece. File > Export > FBX (.fbx). It has no shortcut;
right-click the menu entry > Add to Quick Favorites puts it under Q.

| Setting | Value |
|---|---|
| Limit to | **Selected Objects** |
| Object Types | **Mesh** only |
| Scale | 1.0 |
| Apply Scalings | **FBX Units Scale** |
| Forward / Up | **-Z Forward**, **Y Up** |
| Apply Unit | on |
| Use Space Transform | on |
| Apply Transform | **on** (the one marked experimental) |
| Apply Modifiers | on (if you left modifiers live) |
| Smoothing | Normals Only |
| Bake Animation | off |
| Add Leaf Bones | off (Armature section) |
| Path Mode | Strip; Embed Textures off |

Save these as an operator preset (the + beside Operator Presets) and name it
"TWB building piece".

These are the settings the two kit scripts use
(`build_alanthor_unit_kit.py`, `build_oriental_kit.py`). A piece exported
this way arrives in Unity at scale 1, rotation 0, in metres.

### 7.2 Before every export

- [ ] Object > Apply > All Transforms (Ctrl+A; scale 1, rotation 0).
- [ ] Origin where the rule in 5.11 says.
- [ ] **Sort faces by material.** Edit Mode (Tab), select all (A),
      Mesh > Sort Elements > **Material** (no shortcut; F3 and type
      "sort"). Unity numbers submeshes in the
      order polygons first USE a material, not in slot order. Skip this and
      the plaster lands on the band and the band on the plaster.
- [ ] Remove unused material slots (the down-arrow menu beside the slot
      list > Remove Unused Slots).
- [ ] Normals face out (Overlays > Face Orientation: all blue). Fix with
      Mesh > Normals > Recalculate Outside (select all, A, then Shift+N).
- [ ] Shading: flat for stone and plaster; smooth for columns and rolls
      (Object > Shade Auto Smooth, about 38 degrees; also in the right-click
      menu in Object Mode).
- [ ] No "roof" or "stripe" in the object name.

### 7.3 Where files go, and what git keeps

General rule: `*.fbx`, `*.fbx.meta`, `*.blend` and `*.blend.meta` are
IGNORED by git in this project, with a few exceptions.

| Folder | FBX / .blend tracked? |
|---|---|
| directly inside a folder named `Art/` under `Assets/GameData/TechTree/` | yes (through Git LFS) |
| `.../Art/Oriental/` | FBX yes, .blend no |
| `.../Art/UnitKit/` | **no** |
| any other sub-folder of `Art/` | no |
| a building's own folder (beside its SOs) | no |

So: put a building's own pieces and its .blend **directly** in
`Civs/Alanthor/Buildings/<Building>/Art/`, and shared pieces directly in
`Civs/Alanthor/Buildings/Art/`. If you want a sub-folder for a hand-made
kit, add an exception to `.gitignore` first (copy the two `Art/Oriental`
lines).

Prefabs, materials and PNG textures are tracked everywhere.

### 7.4 Traps

- **An FBX carries geometry, UVs and material NAMES. Nothing else.** Do not
  try to make it carry textures. Make a prefab per piece instead:
  1. drag the FBX into a scene;
  2. right-click it > Prefab > Unpack Completely;
  3. assign your `.mat` files on its Mesh Renderer, in slot order;
  4. drag it into the Project beside the FBX as `<Piece>.prefab`.

  The materials now live on the prefab and the FBX is replaceable. (If you
  would rather keep the FBX itself as the model:
  `Waning Border > Art > Bind FBX Materials From Materials Folder` binds
  each slot to the same-named `.mat` in a `Materials/` folder beside the
  selected FBX.)
- **Re-exporting can reset the `.fbx.meta`** (the importer settings). With
  the per-piece prefab above nothing is lost, because the prefab holds the
  materials. After a re-export just check the mesh still has the same
  number of submeshes in the same order.
- **In an ignored folder there is no undo.** If the `.fbx.meta` is not
  tracked (see 7.3) and something rewrites it, git cannot give it back.
- **Never name an FBX `alanthor_<Name>_lv<N>.fbx` or `<Id>_Age_0.fbx` in a
  building's `Art/` folder** unless you mean it to BE that level's model:
  `Waning Border > Art > Bind Building Art (SO only)` binds any such file to
  the level SO and replaces your hand-made prefab there (9.2).
- Blender writes a `.blend1` backup beside each save. It is ignored by git;
  leave it or delete it.
- Do not add an `AssetPostprocessor` to "fix" importer settings for your
  folder. One scoped too widely reimports every model under it, units
  included; that broke every character once.
- Import settings for a static piece: Animation Type None, Import Cameras /
  Lights / Animation off, Material Creation Mode None. Set them once per
  FBX in the Inspector.

---

## 8. Composing the building in Unity

### 8.1 The frame

General rule: **the prefab root is the centre of the plot, at ground level.
X and Z run from minus half the plot to plus half. Y = 0 is the ground.**

1. Create an empty GameObject at the origin. Name it for the building and
   level. Position 0, rotation 0, **scale 1, 1, 1**.
2. Save it as a prefab beside the level SOs:
   `Civs/Alanthor/Buildings/<Building>/<Name> <N>.prefab` (the hand-made
   ones are named like `Fortress 1.prefab`, `Garrison_3 1.prefab`).
3. Make L1 first. Duplicate the prefab ASSET for L2 and L3 and add to it.
   That is what keeps all three in one frame.

### 8.2 The Footprint group (this sets the placement)

**2026-11-12: the game no longer scales any building.** Every prefab is drawn
at 1 : 1, the size it was authored, whatever its footprint. The `Footprint`
group now only PLACES the model: its bounds centre goes on the middle of the
cells and its lowest point on the ground. A prefab bigger than its plot
overhangs it; make it the right size, or change `footprintCells` on the SO.
Where the text below says "scales", read "would have scaled".

The game used to fit every building to its footprint. It measures the bounds of
the child named `Footprint` that carries the `FootprintBounds` component,
and scales the whole prefab so that group fills the plot. If there is no
such group it measures the WHOLE model, overhangs and all.

1. Add an empty child named `Footprint`. Add the component **Footprint
   Bounds** to it (class `FootprintBounds`).
2. Put ONE thing in it: a ground patch exactly as big as the plot. For a
   10 m plot use `UK_Ground_Patch_10.prefab` (10 x 10 m, 2 cm thick, earth
   colour). For other plots scale a copy: 0.8 for 8 m, 0.4 for 4 m. (Scaling
   a flat earth-coloured slab is the one place scaling is fine.)
3. Its top is at Y = 0.02, its bottom at Y = 0.

What this gives you:

- Plot 10 m, patch 10 m: the fit is **1.0**. The game draws your prefab at
  exactly the size you built it.
- The LOWEST point of the `Footprint` group is what stands on the terrain.
  So Y = 0 is ground.
- Anything outside the group may hang lower. **Keep the stone skirt outside
  the `Footprint` group**, and it stays underground where it belongs. Put it
  inside and the whole building is lifted by 1.3 m.
- Nothing may reach outside the patch sideways (not even an eave). It would
  hang over the neighbouring cells.

The ground patch is a soft patch of earth, never a paved slab, and nothing
of it may show beyond the wall base.

With levels: the game takes the scale and the placement from ONE prefab of
the ladder and uses it for all three (the highest level that has a
`FootprintBounds`; `PresentationSpawnSystem.ResolveFitReference`). So every
level needs the same `Footprint` group in the same place. That is also why
a common piece must sit at the same local position in every level.

### 8.2a Marker empties: corners, unit spawn, exit (2026-11-12)

A prefab may carry plain empty GameObjects, as direct children of the root
or of one group under it (`0 [snap] - Logic` in `Garrison_2`),
found by name (`Shared/Buildings/BuildingAnchors.cs`). They replace the
ground patch for a building made with no floor.

- **`N_corner`, `S_corner`, `E_corner`, `W_corner`** (any child whose name
  ends in `_corner`): the ground contact points. The game lifts the building
  until every corner is on or above the terrain, so on an incline it stands
  on the highest ground under its corners. A prefab with corners takes **no
  footprint fit and no pivot offset**: it is drawn at the size and place you
  built it, centred on the root, whatever the other levels of its ladder do.
  Keep a skirt below Y = 0 for the downhill side.
- **`Unit_spawn` and `Exit_point`** (both, or neither): a trained unit
  appears on `Unit_spawn` and walks in a straight line to `Exit_point`, then
  goes to the rally point. With no rally point (or one inside the building)
  it stops a few metres past the exit. `Unit_spawn` may be inside the
  footprint; `Exit_point` must be OUTSIDE it (footprint plus one metre), on
  ground a unit can walk. Units released together leave one after another.

- **`Footprint_center`** (2026-11-25): the middle of the plot, on the prefab
  root. It counts as one more ground contact point, so a hump inside the plot
  cannot come through the floor; edge midpoints named `NW_corner`,
  `SW_corner`, `SE_corner`, `NE_corner` count the same way. Put every marker
  at the Y of the ground line (for a podium that is the foot of its steps,
  not its floor). An authored root position is ignored by the game.

The markers are read from the prefab of the level the building shows, so put
them in every level. A prefab without them behaves as before.

### 8.3 Which way is the front

The game turns every building view half a turn about Y
(`PresentationSpawnSystem.BuildingVisualOffsetY180`).

**In the prefab, the front (the door, the gate) must face +Z.** In a match
it then faces the default camera.

How PT3 does it: every root group is rotated 180 degrees about Y; inside the
groups the pieces sit with the front toward -Z. You can do the same (compose
toward -Z inside a group, turn the group) or simply build toward +Z. Either
way, the test is the same: place the building in a match (11.3) and look. Our
first test prefabs faced away from the camera; do this check on the first
day, not the last.

### 8.4 Pieces stay editable

- Drag PREFABS in (the `UK_*` pieces, your own piece prefabs, Synty prefabs
  from `Assets/Synty/PolygonFantasyKingdom/Prefabs/`). They stay nested
  prefab instances; you can move each one later, and a fix to a piece
  reaches every building.
- Do not unpack them. Do not merge meshes.
- Group them under empties by part: `Podium`, `CompoundWall`, `Keep`,
  `KeepCaps`, `Yard`, `Cloth`. Give roofs their own groups (`...Cap`,
  `...Caps`), so you can hide them and look inside.
- Grid snapping: Edit > Grid and Snap Settings. Set Grid Size to 0.25 and
  Move increment to 0.25 (bays are 2.5, halves 1.25). Hold Ctrl while
  dragging to snap. Rotate increment 90 (or 45 for hips).
- Scaling a piece ALONG its run to close a gap is fine (PT3 stretches wall
  runs by up to about 10 %). Never scale a piece's thickness, and never
  scale a whole group to make things fit.

### 8.5 How the game colours a building (read this once)

`BuildingFactionColorMarker.Apply` runs on every building view. For each
material on each renderer, the first rule that matches wins:

| Order | Matches | What happens |
|---|---|---|
| 0 | material has a `_DetailMask` texture named `*PlayerColorMask*` | player colour where the mask is white |
| 1 | MATERIAL name contains `playercolor` | flat player colour; the texture is replaced by white |
| 2 | MATERIAL name contains `_Player` or `Castle_Roof` | player colour multiplied over the texture |
| 3 | GAMEOBJECT name contains `roof` | flat dark slate; the texture is replaced by white |
| 4 | GAMEOBJECT name contains `stripe` | player colour tint |
| 5 | none of these | pixels with hue 190-260 degrees and saturation over 20 % turn into the player colour |

All name tests ignore case.

One more rule on top: **if any material in the model matches rule 0 or 1,
rule 5 is switched off for the whole model.** The player colour then goes
only where you named it.

What that means for you:

- **Always include the band** (a `*playercolor*` material) somewhere in the
  building. It is your player colour AND it switches off the hue guess, so a
  bluish pixel in a Synty atlas never turns red.
- The band is drawn as a flat colour. Do not paint detail into it; keep it a
  narrow strip (24 cm).
- **Never leave "roof" in the name of an object that has a renderer.** A
  Synty roof prefab is called `SM_Bld_Castle_Roof_...`: its roof object
  matches rule 3 and its timber is painted slate. If you nest one, RENAME
  the instance in your prefab (for example `Kawara_S_01`).
- **A Synty roof's tile material is `Castle_Roof_01`**: rule 2, so it takes
  the player colour. On a building made by this guide that is wrong. Swap
  that slot for your verdigris material on every nested Synty roof piece.
  Your verdigris material must not contain `Castle_Roof`, `_Player` or
  `playercolor` in its name.
- Careful with innocent names: a material called `Stone_PlayerSide` matches
  rule 2.
- Do not run `Waning Border > Art > Roofs > Castle_Roof_01 ->
  Castle_Roof_Player` to "fix" a new building. That menu exists to make
  roofs player-coloured, which is the old rule.

### 8.6 Player colour: the band and the cloth

**The band.** Material `Al_Band_Playercolor.mat`
(`Civs/Alanthor/Buildings/Art/StripeBand/`). It sits on the `Band` slot of the
plaster wall pieces. In the editor it shows marker blue; in a match it is
the owner's colour.

- Height: 1.12 to 1.36 m above the foot of the wall, on the OUTSIDE only.
- Put it on the outer walls a player sees from the game camera: the front
  and the two sides at least.
- Keep-storey walls have no band.

**The cloth.** Material `Flag_Symbol_Player.mat`
(`Civs/Alanthor/Buildings/Art/`). It is a white-and-grey copy of Synty's lion
flag that the game multiplies by the player colour (rule 2).

1. Drag in `SM_Prop_Flag_01` (or a Battle Banner).
2. On its renderer, replace the `Flag_Symbol_01` material slot with
   `Flag_Symbol_Player`.
3. Scale: 0.22 to 0.26 for a hanging banner about 1.2 m long.
4. Hang at least four: either side of the gate, on the main building's
   front, on poles in the yard.

Roofs: verdigris, and nothing else. No roof material may match rules 1 or 2.

### 8.7 Lanterns and light

- Lantern panes use `UK_Glow.mat`: warm paper colour with emission at the
  "window" rung of the emissive ladder (`EmissiveLadder.Window`, a warm
  orange). If you make your own glow material, copy that one; do not pick an
  emission strength by eye.
- The glow is warm for every player. It is not a player-colour part.
- A prefab may bring its own point lights (a `[snap] - Lights` group). On a
  prefab with an authored sequence they stay dark until the building is
  finished (`BuildingConstructionSequence`). Garrison and Archery Range do.
- Moving banners: a Samurai `J_SM_Prop_Banner_02..10` with every slot set to
  `Art/Banner_Samurai_Player.mat` sways with the wind and takes the player
  colour. Painted blue stucco stripes (patterns 01 and 02) take it through
  `Art/StripeBand/Stucco_White_Pattern_0N_Striped.mat`, with no mesh cut.
- You do not need a Light component. For plots 8 m and up the game adds one
  warm point light by itself, unless the building's id is listed in
  `Assets/GameSystems/Rendering/Buildings/BuildingLanternLight.asset`
  (`excludedBuildingIds`; today Barracks, ArcheryRange, Alanthor_Tower and
  Fortress are listed) or the prefab carries a Light of its own.

### 8.8 Things you do not need to add

- **Colliders.** The game fits one selection box to the view by itself.
- **Scripts.** A building prefab is only transforms, renderers and the
  `FootprintBounds` marker.
- **Construction animation.** The site rises piece by piece without any
  setup: bottom pieces first, top pieces last. If you want to choose the
  order, name ROOT groups with a leading number: `1_Podium`, `2_Walls`,
  `3_Keep`, `4_Caps`. Groups that share a number rise together. Only a
  number at the START of a name counts (the `_01` at the end of a Synty
  name is ignored). If you number any group, number them all: unnumbered
  ones rise last.
- **A building is scaled only as a WHOLE, at its prefab root (2026-11-12).**
  The game applies no factor of its own: a prefab is drawn at its root
  scale. Where a composition is larger than its tile, the ROOT is scaled so
  it fits — `Garrison_2` (corner markers on the 8 m tile) and the three
  Archery Range levels (one factor for all three) are scaled this way by
  `Waning Border > Art > Scale > Fit Garrison_2 + Archery Range to the 8 m
  tile`. The scales of the PIECES inside a building are the author's
  composition and are never normalised by a tool: their ratios to each
  other are kept. Units are not scaled at all: unit prefabs have root scale
  1 and unit entities are created at scale 1.
- **Authored construction sequence (2026-11-12).** To choose HOW each group
  appears as well as when, name the root groups
  `<number> [<effect>] - <name>`, for example
  `3 [rise sequential] - Pillars_secondary` (see `Garrison_2.prefab`). The
  groups appear in number order, one event after another with a short pause
  between events, laid over the site's construction progress (a site nobody
  is working on stands still mid-event). The effects:

  | Effect | What happens |
  |--------|--------------|
  | `snap` | there at once, no delay, no animation - for logic groups (markers, the Footprint). Takes no time |
  | `rise` | the whole group rises slowly out of the ground, shaking, under a dust cloud |
  | `rise sequential` | the group's direct children rise one at a time, each shaking under its own dust cloud |
  | `rise clean` | the whole group rises; no shake, no dust |
  | `disolve` | the group is revealed by the level-up wave; no dust, no shake |
  | `disolve sequential` | the same, one direct child at a time |
  | `fade in` | the group goes from 0 to 100 % opacity |

  `dissolve` is read too, `-` and `_` count as spaces, case is ignored; an
  unknown effect snaps and logs a warning. The dust is the ordinary site dust
  prefab (`BuildingEffectSystem.asset`); a sequenced site gets no whole-site
  dust loop, only the dust its `rise` groups raise. Children of the root
  without such a name are simply there from the start. Code:
  `GameSystems/Rendering/Buildings/BuildingConstructionSequence.cs`.
- **Painted stripes take the player colour (2026-11-12).** The Ancient
  Empire stucco `Stucco_White_Pattern_01` has its blue stripes painted into
  the texture, and a material is per submesh, so the stripes cannot wear
  `Al_Band_Playercolor` until they are geometry of their own. After using
  that stucco on a building, run `Waning Border > Art > Walls > Stucco
  stripes -> Al_Band_Playercolor`: it cuts every stucco slot of every wall
  piece along the stripes' edges, puts the stripe faces in a new last
  submesh bound to `Al_Band_Playercolor` (the same material as the band on
  the `UK_Plaster_Wall_*_Slit` pieces), and saves the cut meshes in
  `Art/StripeBand/StripeCut/`. The stripes become straight-edged bands. Safe to
  run again. Code: `Scripts/Editor/PrefabAuthoring/StuccoStripeBandPass.cs`.

### 8.9 Fire points (shooting buildings only)

Buildings that shoot loose their arrows from "fire points" stored on the SO.
After the art changes, run `Waning Border > Art > Bake Building Fire Points`.

It looks, in this order, for:

1. Synty `Hoarding_Corner` pieces;
2. otherwise any part whose path contains `Tower`;
3. otherwise the top of the building.

Thin spikes are ignored. So if you want arrows to come from your corner
turrets, put "Tower" in the turret GROUP names (`Tower_SW`, not `Yagura_SW`).
PT3 uses `Yagura..` and so would shoot from the top of the keep.

---

## 9. Binding the prefab to the game

### 9.1 The field

The game takes a building's look only from its SOs.

| Building has | Set this field |
|---|---|
| Levels (most Alanthor buildings) | `prefab` on each `BuildingLevelDefSO`: `Civs/Alanthor/Buildings/<Building>/<Name>_Lvl1..3.asset` |
| No levels in Alanthor | the Alanthor entry of `culturePrefabs` on the `BuildingDefSO` |

Drag your prefab into the field in the Inspector. That is all. (The Watch
Tower's level SOs are `Tower/WatchTower_Lvl1..3.asset`; the Garrison's are
`Garrison/Garrison_Lvl1..3.asset`.)

### 9.2 Switch the whole ladder together

Because one prefab of the ladder sets the scale for all three (8.2), never
bind one new level and leave two old ones.

- [ ] L1, L2 and L3 prefabs exist, all with the same `Footprint` group.
- [ ] All three `_Lvl` SOs point at them.
- [ ] Bake fire points if the building shoots (8.9).
- [ ] `Waning Border > Art > Render Building Icons` to refresh the build
      menu icons.
- [ ] Look at it in a match (11.3).

You do NOT need `Waning Border > Art > Bind Building Art (SO only)` for a
hand-made prefab. What that menu does: it finds every FBX in an `Art/`
folder named `alanthor_<Name>_lv<N>` or `<Id>_Age_0`, sets it as that SO's
`prefab`, fills the wall art slots, and writes an audit to
`Temp/building-art-binder.txt`. It leaves a hand-made prefab alone as long
as no FBX with one of those names sits in the building's `Art/` folder.

Do not run `Waning Border > Art > Author Age 0 + Alanthor Art (Synty)`
after binding hand-made prefabs: it rebuilds the scripted Alanthor set and
re-binds SOs (noted from earlier sessions; not re-checked for this guide).

---

## 10. Worked example: the Fortress (PT3)

Plot 10 x 10 m. All positions are from the plot centre. "Front" is the
gate side (+Z in the prefab, see 8.3). Heights are above GROUND unless they
say "above the court".

```
   plan, L3 (1 square = 1 m; front at the bottom)

   +----------------------------------------+
   |t t                                  t t|   t = low back turret (L3)
   |t t      .------------------.        t t|
   |         |                  |           |
   | c       |       KEEP       |           |   c = colonnade (L2+)
   | c       |    4 tiers (L3)  |      props|
   | c       |                  |           |
   | c       '------[door]------'           |
   | c            o  steps  o               |   o = brazier
   | c                                      |
   |              COURT (raised 1.2 m)      |
   |        P            S           P      |   P = banner pole, S = standard
   |T T T    ____[  GATE  ]____      T T T  |   T = front turret (yagura)
   |T T T   |    =  steps  =   |     T T T  |
   +--------'------------------'------------+
            columns of the portico stand
            on the step cheeks
```

### 10.1 Build order, L1

1. **Frame.** Root at the origin. `Footprint` group with the 10 m ground
   patch (8.2).

2. **Podium** (the battered stone ring; group `Podium`, outside
   `Footprint`).
   - The foot of the stone is on the 5.0 m line (the plot edge). The top
     outer edge is at 4.60 m from the centre.
   - Top 1.2 m above ground. Stone continues 1.3 m below ground.
   - Four corner pieces, then 2.5 m runs along the back and both sides.
   - On the front leave a notch in the middle for the steps: 3.04 m wide in
     all (steps 2.4 m plus two 0.3 m cheeks).

3. **Steps.** Six marble steps in the notch: 2.4 m wide, each 0.2 m high
   and 0.28 m deep. Stone cheeks 0.3 m wide each side, with a small marble
   plinth on top for the portico columns.

4. **Court floor.** Stone paving at 1.2 m, inside the podium top, from the
   top of the steps to the back.

5. **Compound wall** (groups `CompoundWall` and `WallCoping`).
   - Centre line 4.43 m from the plot centre, standing on the podium top.
   - Plaster panels 2.3 m tall, 0.14 thick: top at 3.5 m above ground.
   - Alternate a slit panel and a plain panel every 2.5 m.
   - The band faces out.
   - A coping roof on top, 0.62 m wide: the wall ends about 4.06 m above
     ground (2.86 m above the court).
   - A timber post at each corner that has no turret.

6. **Gate recess.** The gate does not sit in the wall line. It is set back
   1.26 m, at 3.17 m from the centre.
   - Two short return walls run back from the wall line to the gate line,
     at the edges of the notch, with a post at each outer corner.
   - Fill between the returns and the gate frame with narrow plain wall.

7. **Gate** (group `Gatehouse`).
   - Timber frame: posts to 2.5 m, lintel to 2.58 m, opening 1.84 m wide.
   - Two studded leaves, each 0.91 wide and **2.2 m tall**.
   - A plain head wall above the lintel, the width of the notch.
   - Portico: two marble columns 2.6 m tall, 2.68 m apart, on the step
     cheeks, 4.78 m from the centre. A marble entablature across them.
   - Roof (group `GateCap`): a hipped tile roof, eave 3.9 x 2.5 m, rise
     0.95 m, ridge 2.0 m long, sitting on the entablature.

8. **Front turrets** (groups for each; PT3 calls them `YaguraSW` / `SE`).
   - Plan 2.2 x 2.2 m, in the two front corners, outer faces on the wall
     line.
   - Lower storey: the same banded plaster as the wall, 2.3 m, one slit
     each side, a post at each corner.
   - A skirt roof: eave 2.8 m square, rise 0.42 m.
   - Upper storey: 1.72 m square, 1.9 m tall, small windows.
   - A hip cap: eave 2.6 m square, rise 0.95 m, with a finial.

9. **Keep** (groups `Keep` and `KeepCaps`), centred 1.85 m behind the plot
   centre.
   - Stone base: 5.2 x 4.4 m at the foot, 1.0 m high, each face leaning in
     0.2 m. It stands on the court.
   - Tier 1: 4.6 x 3.8 m, 2.1 m tall. Slits only, no windows (it is the
     ground storey of a fortress).
   - Skirt roof A: eave 5.5 x 4.7 m, rise 0.55 m.
   - Tier 2: 3.8 x 3.0 m, 1.9 m tall. Windows and plain panels by turns.
   - Top roof: hipped, eave 4.7 x 3.9 m, rise 1.5 m, ridge 2.0 m.
   - A timber post at every corner of every tier.
   - Door: single leaf, 1.0 x 2.2 m, in the front of tier 1, with three
     marble steps up the base.

10. **Cloth** (group `Cloth`). Two banners on the keep front, either side of
    the door, tops 3.0 m above the court. Two on the compound wall, either
    side of the gate.

11. **Court props** (group `Yard`), native size: two braziers in front of
    the keep; on one side a spear rack, a training dummy and two barrels.

Result: bulk 8.2 m, 28 roof planes, court 42 % open.

### 10.2 The keep tiers and roofs, all levels

| Tier | Plan | Height |
|---|---|---|
| 1 | 4.6 x 3.8 | 2.1 |
| 2 | 3.8 x 3.0 | 1.9 |
| 3 | 3.0 x 2.4 | 1.75 |
| 4 | 2.2 x 1.8 | 1.6 |

| Roof | Eave | Rise |
|---|---|---|
| Skirt over tier 1 | 5.5 x 4.7 | 0.55 |
| Skirt over tier 2 | 4.6 x 3.8 | 0.50 |
| Skirt over tier 3 | 3.8 x 3.2 | 0.46 |
| Top roof on tier 2 (L1) | 4.7 x 3.9 | 1.5 |
| Top roof on tier 3 (L2) | 3.9 x 3.3 | 1.3 |
| Top roof on tier 4 (L3) | 3.1 x 2.7 | 1.1 |

Each tier steps in 0.3 to 0.4 m a side, and each is a little lower than the
one below. Each skirt's eave stands 0.4 to 0.45 m outside the tier BELOW it.

### 10.3 What L2 adds

- A third keep tier and its skirt; the top roof moves up.
- Two more portico columns behind the first pair, with side entablatures (a
  proper porch).
- Two hanging lanterns under the gate roof. Two stone lanterns by the keep
  steps.
- The **colonnade** along one side wall: three marble columns 2.2 m tall,
  1.2 m apart, 1.1 m in from the wall; an architrave on top; a lean-to tile
  roof from the wall down to it (1.45 m deep, 0.55 m rise).
- Two banner poles (3.4 m) in the front of the court with banners.
- More props: a second dummy, a crate, a sword rack.

Bulk 9.8 m, 33 roof planes.

### 10.4 What L3 adds

- A fourth keep tier; gilt fins on the top ridge.
- Two low turrets in the BACK corners (1.6 m square, the cap straight on the
  wall-high storey).
- A marble pediment over the gate portico.
- The colonnade grows to five columns.
- An eagle standard in the middle front of the court.
- More props: an armour stand, an arrow barrel, sacks.

Bulk 11.3 m, 45 roof planes, court 36 % open. The compound wall, the gate,
the front turrets and the podium have not moved since L1.

### 10.5 A Medium recipe: the Garrison (not built yet)

This is a recipe from the rules, not a tested build. Plot 8 x 8 m.

1. `Footprint`: the ground patch at 0.8.
2. A stone base only under the hall: 0.5 m above ground, 1.3 m below, batter
   0.15 m. The yard stays on the ground.
3. **Hall** on the back-left: 5.0 x 3.4 m (about 27 % of the plot). Plaster
   walls 2.3 m, banded on the front and the open side. One door (2.2 m
   leaf) in the front, toward the yard. Two slits per long side. No windows
   at ground level.
4. **Hall roof**: a gable, ridge along the 5 m, eaves 4.2 m apart (0.4 m
   overhang), rise 1.4 m. Ridge sag 4 cm, end fins. Then add a second,
   smaller gable crossing it over the door (a porch roof). That makes two
   roof volumes and lifts the roof's share of the height without raising
   the walls.
5. **Lean-to weapons shed** on the hall's yard side: 3.0 x 1.4 m, open
   front, two timber posts, roof rise 0.55 m. Racks and armour stands under
   it.
6. **Yard** on the front and right, about 55 % of the plot: a low plaster
   wall 1.2 m high with a coping on the two outer sides (banded), open
   toward the camera. Training dummies, a target, a spear rack, barrels.
7. **Accent**: a slim watch turret at the hall's back corner, 1.7 m square,
   one storey above the hall eave, hip cap. Or a banner pole.
8. Cloth: one banner on the hall gable, one on the pole.
9. Levels: L2 adds the porch columns (marble, 2.2 m) and a second lean-to;
   L3 adds the turret's upper storey and cap, gilt fins, a marble door
   frame. The hall never gets wider.

Target: bulk at most 7.5 m at L3, body (hall + shed) 40 to 60 % of the
plot, at least 5 roof planes at L1.

### 10.6 A Small recipe: the House (not built yet)

Also a recipe, not a tested build. Plot 4 x 4 m.

1. `Footprint`: the ground patch at 0.4.
2. A stone footing 0.3 m above ground, 1.0 m below, under the room only.
3. **One room**: 3.0 x 2.2 m at the back of the plot. Plaster walls 2.1 m,
   timber posts at the corners. One door (2.0 m leaf) in the front. One
   small window beside it, one on a side.
4. **Roof**: a hip or gable with 0.4 m overhang (eave 3.8 x 3.0 m), rise
   1.6 m or more. Check the roof's share from the game camera. If it is
   under half, make the roof steeper or add a small porch roof over the
   door. Do not lower the door.
5. **Yard strip** 1.2 m deep along the front: a barrel, a sack stack, a
   stone lantern or a short fence piece.
6. Player colour: the band on the front wall and one small banner by the
   door.
7. Levels: L2 adds the porch (two thin posts, a lean-to) and a lantern; L3
   adds a second roof plane (a raised ridge vent or a dormer), a marble door
   frame, gilt ridge ends. Same room, same plot.

Target: bulk at most 5 m, at least 2 roof planes at L1.

---

## 11. Checking your work

### 11.1 The measurable checklist

Measure in Unity with the prefab at scale 1 (fit 1.0), or in Blender for
single pieces.

| Check | Target | How to measure |
|---|---|---|
| Door leaf height | 2.0 - 2.4 m | select the leaf; Inspector bounds, or stand a Spearman beside it: the door is a head taller than him |
| Wall height, ground storey | eaves a little above the door: 2.3 - 2.8 m above the floor it stands on | Y of the wall top minus Y of its floor |
| Compound wall top (Large) | 3.5 - 4.5 m above ground | Y of the coping top |
| Eave edge thickness | 5.5 cm; never over 8 | Blender Edge Length on the eave edge |
| Tile size | at most 25 x 25 cm | course depth in Blender; count tiles along 1 m in the UV editor |
| Wall thickness at openings | 12 - 15 cm | Blender Edge Length |
| Roof share of height | half or more | from the game camera: roof height seen / total height seen |
| Body share of plot (Medium) | 40 - 60 % | sum of the volumes' plan areas / plot area |
| Court open (Large) | 35 % or more (20 % is the hard floor) | court area not under a building / plot area |
| Skirt depth | 1.0 m or more below Y = 0 | lowest Y of the stone base |
| Ground patch | nothing of it shows past the wall base | top view |
| Overhang of the footprint | none (2 cm tolerance) | top view, orthographic: nothing outside the patch |
| Bulk and tip height | section 3 caps | highest Y of the main mass; highest Y of anything |
| Roof planes | L1 6+, L2 8+, L3 14+ (Large) | count separate slopes, copings not counted |
| Fit | 1.00 | Proportion Probe `detailScale` column (11.2) |
| One frame | common pieces at identical positions in L1-L3 | open two levels, compare Transform values |
| Band and banners | take the player colour | Render Prefab Shots in two colours (11.2) |
| Roofs | do NOT change with the player colour | same two shots |
| Front | faces the camera in a match | 11.3 |

A quick way to measure in Unity: put an empty Cube at scale 1 beside the
part (it is 1 m), or read `Bounds` from a renderer's debug Inspector.

### 11.2 The tools that exist

**Proportion Probe.** `Waning Border > Debug > Proportion Probe (CSV)`
(`Assets/Scripts/Editor/ProportionProbe.cs`). Writes `Temp/proportions.csv`.
One row per Age 0 / Alanthor building and level AS BOUND ON THE SOs, fitted
to its footprint the way the game does, plus the unit models. Columns:

`kind, id, level, culture, footW, footD, detailScale, width, depth, height,
height_over_width, door_h, prefab`

- `detailScale` is the scale the game draws the prefab at. You want 1.00.
- `width` / `depth` over `footW` / `footD` means something overhangs.
- `door_h` is the tallest renderer with "door" in its name. Name your leaf
  accordingly.
- It only sees prefabs that are bound (section 9). Unity empties `Temp/`
  when a batch run quits; in the open Editor the file stays.

**Render Prefab Shots.** `Waning Border > Art > Render Prefab Shots`
(`PrefabShotProbe.cs`). Renders any prefab without starting a match.

1. Create `Temp/prefab-shots.txt`. One job per line:
   `asset path|output png|yaw|pitch|width|height|r,g,b`
2. Yaw 0 looks along +Z; pitch is degrees above the horizon. The last field
   is a player colour (0-1 values); with it the game's colour rule runs.
3. Run the menu. A log is written beside the job file (`.log`).

Do every building twice, once red (`0.8,0.14,0.12`) and once blue
(`0.16,0.36,0.85`). The band and banners must change; nothing else may.

**Alanthor Art Probe.** `Waning Border > Debug > Alanthor Art Probe: Launch
...` (in Play mode) starts a match with three allied Alanthor players showing
L1, L2 and L3 side by side; `... Arrange ...` raises the levels; the
`Camera to ... Capital` items jump there.

### 11.3 Judge it where the player sees it

Close-ups lie. A piece that looks delicate at 3 m can vanish at game
distance, and a piece that looks fine alone can be a block beside a soldier.

1. Bind the ladder (section 9).
2. Start a match (the Art Probe above, or a skirmish as Alanthor).
3. Look at the building from the normal game camera, zoomed in, zoomed out.
4. Walk soldiers past the door and into the yard.
5. Place it on a slope: no gap may show under the base.
6. Rotate it (mouse wheel while placing) and check the back is not bare.
7. Compare with a neighbour of another size class: does the storey height
   match?

### 11.4 Eye test: does it read blocky?

Tick each; any "yes" is a fix.

- [ ] Is any edge thicker than a soldier's wrist where it should be a tile,
      a board or a blade? (eaves, rails, bars, banner poles)
- [ ] Is any long line dead straight? (ridge, eave, wall top, base)
- [ ] Is any big surface one flat colour?
- [ ] Can you count the tiles on a roof from the game camera? (too big)
- [ ] Is there a paved slab under the building?
- [ ] Is the outline a plain box or a plain triangle, with nothing small
      sticking out of it?
- [ ] Is one volume much fatter than a soldier-scale room would be?
- [ ] Do the walls take more of the height than the roofs?

### 11.5 Eye test: does it read strong?

Tick each; any "no" is a fix.

- [ ] Does stone meet the ground, leaning in?
- [ ] Are the ground-storey openings few and small (slits, not windows)?
- [ ] Is there more than one line of defence to cross (base, wall, gate
      recess, keep)?
- [ ] Is the gate frame grand while the door stays small?
- [ ] Is the main mass wider at the bottom than at the top?
- [ ] Is there a place where soldiers would stand guard?
- [ ] Does it still look thin-edged? (Strong must not have become fat.)

### 11.6 What went wrong before

| Mistake | Result |
|---|---|
| Shrinking a real-size composition to fit | dollhouse; doors 1 m tall |
| Scaling the whole thing up to 0.80 instead | thick pieces got thicker; needed 15 m of bulk |
| Heavy merlons on the compound wall | they hid the court |
| One fat keep | a block; no roof planes |
| Player-colour roofs | every roof a flat coloured slab; red times teal went purple |
| A paved base slab | the building sat on a tray; gaps on slopes |
| Miniature core on an ordinary building | read as a toy, not as far away |
| First prefabs built facing -Z without the turn | the building showed its back to the camera |

---

## 12. Where PT3 is still weak (do these better)

1. **The keep is too slender for a fortress.** Make tier 1 wider and lower,
   or raise the stone base to 1.5 m. The mass must be at the bottom.
2. **The Roman half is thin.** The colonnade hides behind the side wall.
   Bring marble to where the camera sees it: the gate portico deeper, a
   colonnade across the keep front, pilasters on the compound wall.
3. **The court is cramped at L3.** The back turrets and the longer
   colonnade take it to 36 %. Add height at L3, not floor area.
4. **The stone reads as regular brick.** Paint dry-stone: uneven courses,
   uneven stones, a few large ones (5.6, 6.5).
5. **The grime is faint.** Double it at the foot of the plaster and stone.
6. **Eave ends are plain.** Add round eave-end tiles along the lowest
   course and thin rafter tails under the eave (3 x 5 cm, every 30 cm).
7. **The short wall runs beside the gate are squeezed.** Pieces were scaled
   to fit. Make a proper narrow panel (0.6 to 0.9 m) instead.
8. **The keep steps are improvised** (scaled entablature blocks). Model
   three real marble steps, 1.4 m wide.
9. **Lantern panes are near white.** The glow washes out the paper colour.
   Darken the pane's base colour toward amber, or paint a lattice over it,
   so it reads as a lantern and not as a white square.

---

## 13. One-page summary

- Build at 1 : 1 on a patch the size of the plot. Fit 1.0.
- Door 2.2 m. Walls low. Roofs half the height, many planes.
- Edges: eave 5.5 cm, wall 14 cm, column 26 cm, post 16 cm.
- Tiles 22 x 24 cm, painted, verdigris, matte.
- Stone base leaning in, 1.3 m into the ground, OUTSIDE the `Footprint`
  group.
- Player colour: `Al_Band_Playercolor` band + `Flag_Symbol_Player` cloth.
  Never the roof.
- No "roof" in object names. No `Castle_Roof` / `_Player` / `playercolor`
  in a material name unless you mean it.
- Front faces +Z in the prefab.
- Same frame for L1, L2, L3. Levels add, never widen.
- Sort faces by material before every FBX export.
- Judge it in a match, beside soldiers.
