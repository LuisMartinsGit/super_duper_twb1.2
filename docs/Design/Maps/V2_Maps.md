# The v2 maps (2026-11-14)

Units were halved (radius and speed, on the unit SOs). The 2 m build grid and
every building footprint were **not**, on purpose. On the first maps that left
every march twice as long and the players too far apart. Each map therefore
gets a **v2**: the same concept on half the ground, players closer. The
originals stay in the project beside them; Yiel Lymwérra needs no v2.

A v2 is a **redesign, not the old map scaled**. A building does not shrink, so
a half-size map cannot keep its old territory count: every v2 territory is
drawn big enough for what it must hold, and the layout tool proves it (below).

| v2 map | Players | Size | Original |
|---|---|---|---|
| [Moatwatch v2](#moatwatch-v2) | 4 | 256 × 256 m | [Moatwatch.md](Moatwatch.md), 512 m |
| [Highfold v2](#highfold-v2) | 8 | 384 × 384 m | [Highfold.md](Highfold.md), 768 m |
| [Broken Oath v2](#broken-oath-v2) | 5 (3v2) | 384 × 224 m | [Broken_Oath.md](Broken_Oath.md), 768 × 448 m |
| [Trine Hollow v2](#trine-hollow-v2) | 3 | 320 × 320 m | [Trine_Hollow.md](Trine_Hollow.md), 640 m |
| [Fiendstone Pass v2](#fiendstone-pass-v2) | 6 | 512 × 512 m | [Fiendstone_Pass.md](Fiendstone_Pass.md), 1024 m |

Source: `tools/maps/<map>_v2/layout.py` over the shared kit
`tools/maps/layoutkit.py` (Fiendstone Pass v2 too: it left its own generator).
`layout.py --export` writes the data; `Waning Border > Maps > v2 > Build + Bake
<map>` (`Assets/Scripts/Editor/MapBuilderV2.cs`) builds the scene
`<Original>_v2` in `Assets/GameData/Scenes/Maps/<Original folder>_v2/`, which
wears its original's biome (`TerrainBiomeDress`). The lobby lists them as
"<Map> v2" (`MapRegistry.ShippingMapScenes`).

## The look: a biome of its own, trees and rocks

Each v2 map wears its OWN biome from the pack Yiel Lymwérra uses (the Free
Stylized Textures kit, through `TerrainBiomeDress`), not its original's:

| v2 map | Biome | Ground | Props |
|---|---|---|---|
| Moatwatch v2 | wetland fen | olive grass, peat, mud | thin and round trees, bushes |
| Highfold v2 | alpine pine highlands | tundra heath and turf, snow on the heights and a light drift | pines, grey rock |
| Broken Oath v2 | red-rock canyon | red sand, cracked clay, sandstone, dry scrub | mostly boulders and rock, a few trees |
| Trine Hollow v2 | mossy boulder woods | deep green moss, stones | big trees, mossy boulders |
| Fiendstone Pass v2 | autumn broadleaf valley | gold grass, red-orange leaf litter | round and large broadleaves, flowers |

None of them uses a decoration the art direction reserves
([Art_Direction.md §6.3](../Art_Direction.md)): no dead or burnt trees
(Blighted ground), no bones, no ember cracks, no purple or cyan crystal.

**Where the trees and rocks stand.** A tree or rock takes a build cell and is
impassable, so on half-size ground it is placed where it costs no room the
rule below needs:

- **Hedgerows**: broken copses in the outer 2 m of a territory, along its
  borders (`Plan.hedgerows`), off ramps, fords and home camps, and never in a
  home, a Sanctum or a four-node territory (veilstone, curse) — those have no
  room to spare. Moatwatch v2's hedges are thinner still: its arms are all the
  open ground the map has.
- **Mountains**: a deeper rock-and-tree fringe (12 m in, `fringe_depth`), 45 %
  of it planted.
- The builder plants; the biome then re-dresses every prop **in place** (same
  spot, same width) from its own tree and rock palettes, so the NoWalk under
  each prop is untouched. The room rule is checked **with the trees in place**.

## The building-room rule

Every claimable territory on a v2 map must hold, **beside its own nodes**:

- **the home**: the Fortress at the start, two Farms **with their whole crop
  fields**, four trainers, a Temple, a Market and eight Houses;
- **every other territory**: one Farm with its whole field and two Houses.

The layout kit replays it (`layoutkit.py`, the building-room probe): it lays
the territory's nodes the way `TerritoryResources` does (random spots, the
border margins, node spacing, the home's offset band), then packs the
footprints on buildable ground — walkable, no steeper than the build rule's
slope, inside the territory, a grid cell of gap round each, a Farm's field
whole inside the territory. A territory passes only if it fits against **six
independent node draws**; the plan image draws the fitted fields and
buildings. Footprints and the field radius mirror the building SOs and
`Farm.asset`; the kit says which.

What the rule taught, so a new layout starts right:

- A territory that holds three or four nodes needs compact ground — a blob,
  not a strip. The nodes land in its fat interior, exactly where a Farm field
  would go, so a long thin territory fails however large it is.
- Forest eats Farm room. On a v2 map the claimable territories carry none.
- A territory with no nodes (a Sanctum, an Empty one) needs only room for one
  field.

## Rules the v2 maps changed

- **Node spacing round the home** (`TerritoryResources.asset`
  `homeNodeMinOffset`) and **the start clearing** (`StartClearing.asset`
  `radiusCells`) were cut, so a home on half the ground keeps a camp clear of
  nodes and still has room for its seven. They apply to every map.
- **A territory with no nodes may sit across a mirror axis.**
  `TerritoryResources.MirrorPartners` lays one quadrant's nodes and mirrors
  them to every seat only when nothing straddles an axis; a Sanctum or Empty
  territory lays nothing, so it no longer counts. Moatwatch v2's central
  Sanctum relies on it.
- **A mirrored map finds a territory's twin from a point inside it**, not its
  bounding box's centre (`MirrorPartners`): Moatwatch v2's arms curve round
  the moat, their box centres fell in the water, and the map silently lost its
  mirrored node layout.
- **Open: no Empty territory on four of the five.** [Territory_Claims.md](../Territory_Claims.md)
  §11 asks every map for at least one of each special type (Empty, Veilstone
  rich, Iron rich, Sanctum); Moatwatch, Highfold, Broken Oath and Trine Hollow
  v2 have no Empty, because half the ground had no territory to spare
  (`TerritoryResources` logs a warning; nothing else changes). Fiendstone Pass
  v2 keeps its Empty pass.
- **The outcrop's Trading Outpost room is no longer checked** by the kit; the
  runtime stopped keeping it when the posts were retired (decision 55).

## Moatwatch v2

Four players, 256 × 256 m, mirrored on both axes. Homes are 178 m apart on
foot to a neighbour and 251 m across (the original: 407 and 595 m).

The concept is unchanged: each corner keep behind a moat, two waded fords
toward the middle split between the keep and the ground across, rich ground
toward the centre. What changed, and why:

| | Original | v2 |
|---|---|---|
| Keep | home + two garths inside a round moat | the home alone, inside an **elliptical** moat along the diagonal |
| Outside the moat | eleven small territories a quadrant | **two arms** along the map edges, each meeting the neighbour's arm at the axis: **iron-rich** toward one neighbour, **cursed** toward the other |
| Sanctum | a raised barrow in each quadrant | **one raised barrow in the centre**, where the tor was, a ramp up from each quadrant: the hill all four keeps fight for |
| Curse | four cursed fields, re-dealt per match | the four cursed arms, **pinned** (they never move) |
| Forest | clumps in the expanse | none on claimable ground |

- A round moat reaches as far along the map edges as along the diagonal, so on
  half the ground it left the arms a thin strip with no room for a Farm field;
  the ellipse holds the same home area and stops well short of the edges.
- Garths beside the home, quadrant barrows and a cursed heart quarter all
  failed the room rule; merging the ground into two arms and one shared
  Sanctum is what passes.
- Each seat faces iron on one side and a curse to cleanse on the other; its
  two neighbours mirror that, so the contested arm between two keeps is the
  same kind for both.

13 territories: 4 homes, 4 iron-rich arms, 4 cursed arms, 1 Sanctum. The plan
is `tools/maps/moatwatch_v2/preview.png`.

## Trine Hollow v2

Three players, 320 × 320 m, three-fold rotation (nodes are laid at random per
territory, as on the original; the curse is pinned). Homes are about 233 m
apart on foot (the original: about 440 m).

The concept is unchanged: a bowl stepping down to a rich basin with a cursed
mesa in the centre, rim homes, Sanctum saddles on the spurs as the back way
between neighbours. A 120° wedge of the half-size bowl is about 25,000 m², and
the original's bands, halved, were 30-40 m deep — too thin for a Farm field
beside random nodes — so every band became ONE deeper territory per seat:

| Per seat | Original | v2 |
|---|---|---|
| Home | the rim | reaches from the rim down to the basin's edge on its own axis (a home that holds the full base needs ~7,600 m² of compact ground) |
| High pockets | two (Aerie, Spire) | one, the Aerie, on the home's west flank |
| Terraces | a ring cut by the spurs, four territories | one, the Shelf (veilstone), on the home's east flank |
| Basin | four territories | one iron-rich Floor |
| Saddle | a Sanctum on the spur joining two terraces | the same Sanctum, now joining one seat's Shelf to the neighbour's Aerie — the pocket's back door, through contested ground |
| Mesa | 30 m, cursed | 36 m, cursed, pinned (four nodes and a Farm field need it) |

Cut for room: the second pocket, the Empty terrace, the other basin
territories and the two basin tors. 16 territories (the original: 37). The
layout is tight: it passes every draw the kit makes, and a wider stress test
failed about one territory-draw in seventy, so a further cut (the Aerie into
the home) is the lever if the room ever proves short in play.

## Fiendstone Pass v2

Six players, 512 × 512 m, the north bank drawn and the south bank its exact
180° turn — now on the shared kit (`group_rotation(2)`), no longer
Fiendstone's own generator. Every home-to-home walk is about 0.4 of the
original's (nearest pairs ~315-330 m, the original's ~750-790 m; the longest
~670 m, the original's ~1,660 m).

**Seat order changed:** seats alternate banks — 1 / 2, 3 / 4 and 5 / 6 face
each other across the river (the original numbered the north bank 1-3). A
two-player game on seats 1 and 2 is a fair cross-river match.

The concept is unchanged: cliff shelves stepping down to a river crossed by
three waded fords, each split between the banks' low ground; ramps are the
chokes, archers above. What changed, and why:

- **15 territories a bank, not 21.** The two rim pockets (iron-rich) are cut —
  the plateaus need their width for a Farm field and four nodes, and every
  start pocket is iron-rich already. **The pass is one territory** (a centre
  and two saddle lanes joining the plateaus, Empty as before): a lane narrow
  enough to read as a pass cannot hold a Farm field alone.
- **Start pockets are full-height beside each start** (the corners' on the map
  edge), big enough for three iron nodes and a Farm field; the originals were
  slivers.
- **The rows touch**: start on shelf, shelf on plateau or pass, plateau on low
  ground, cliffs between, no mountain bands. The low ground's top dips in the
  middle to give the Sanctum crypt its own ledge under the pass.
- **No mountain spurs between the low grounds**: the cursed vein stands in the
  bank between the west and centre low ground instead, so those two meet only
  along the river.
- **Territory types are the original's** for every surviving territory; the
  curse is **pinned** (a rotation map would re-deal it unfairly). Forest stays
  only on the west and east low ground and the plateaus.
- River 24 m wide on a gentle curve that maps onto itself under the turn;
  fords at x = −170, 0, +170, ~18 m wide; 10 m ramps.

34 territories (the original: 42).

## Broken Oath v2

Five players (3v2), 384 × 224 m, mirrored north/south (nodes at random per
territory, as on the original; the curse is pinned). The teams' own homes are
about half as far apart as before (Hold to Hold 144 m, Bastion to Bastion
124 m); the two teams are closer still (Hold to the near Bastion 210 m, the
original's 647 m), because the middle went from eleven territories to four.
**Every home is the same walk from the Oathstone** (~110 m), as the concept
requires.

The constraint is size: five homes of ~7,400 m² and their pockets leave room
for only about ten more territories on half the ground, so:

| | Original | v2 |
|---|---|---|
| Shape | blobs and Voronoi splits | three bands (west / middle / plateau) of near-rectangular territories with rough edges — irregular cells left too little compact ground for a Farm field |
| The trio (west) | homes, two cliff pockets per half, the six-territory Lowmarch | homes and ONE cliff pocket per half (the Scar, iron, one ramp); the Lowmarch is gone |
| The pair (east) | Bastions on the Highmarch, seven plateau territories per half, a tarn each | Bastions reaching the escarpment edge, ONE shared Highmarch (veilstone) on the axis between them, a tarn each |
| Escarpment | four ramps | three: one into each Bastion, one into the Highmarch |
| Middle | eleven territories and three tors | per half: the curse (pinned) beside the Oathstone and an iron-rich territory toward the edge; the Oathstone (raised Sanctum, four ramps) unchanged |
| Rim and forest | 14 m mountain rim, forest | 4 m rim, no forest — both ate Farm room |

15 territories (the original: 45). It passes every draw the kit makes, with
little to spare: there is no room left to add a territory.

## Highfold v2

Eight players, 384 × 384 m, mirrored on both axes (nodes mirrored to every
seat). Seats 1-4 are the mountain seats, 5-8 the field seats, as before. A
mountain seat is about as far from its own field seat as on the original
(225 m, was 230 m — the one way out is still the long way); everything else is
about half to two-thirds as far (field seats 146-258 m apart, the original's
276-429 m; mountain seats 311-519 m, the original's 697-882 m).

A quadrant is 192 × 192 m. Every version that kept the original's round
massif, the Meadow on the diagonal and Voronoi fields left the fields as 40 m
strips along the axes, which no Farm field survives, so the quadrant is laid
out in BLOCKS:

| | Original | v2 |
|---|---|---|
| Mountain seat | the Crag in a round corner massif with two high pockets (Eyrie, Cairn) and a Ledge, one ramp down | the Crag in the corner, the Ledge (iron) above it up the map edge, ONE ramp from the Ledge to the West Field; a mountain shoulder seals it from the fields; the pockets are cut |
| Field seat | the Meadow on the diagonal in open fields | the Meadow beside the axis, an iron-rich edge hollow below it (one ramp), the curse above it |
| Fields | eighteen territories a quadrant | two: the West Field (veilstone) under the Ledge and the East Field (the curse, pinned) round the Barrow |
| Edge hollows | two a quadrant (iron, veilstone) | one, iron-rich, on the north and south edges; the veilstone moved to the West Field |
| Between neighbours | open fields, tors | a mountain spur along the x axis between the two Meadows; they meet round its north end, through the curse |
| Centre | a pond, Sanctum barrows | a pond, a raised Sanctum barrow in each quadrant (two ramps from the curse) |
| Forest | yes | none on claimable ground |

28 territories (the original: about 100). The territories read as rectangles
softened only by the edge noise: rounder ones cost Farm room the homes do not
have. Rounding them is the first thing to revisit if the look matters more
than the room.
