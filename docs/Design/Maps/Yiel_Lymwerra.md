# Yiel Lymwérra (2026-10-17; buildable shallows and lower mountains 2026-10-19)

Two players, **384 × 192 m** — a rectangular map. A coast: mountains in the north, a grass plateau,
a cliff down to the beaches and the sea in the south. Cut into the plateau is
a round **bay of shallows** with the cursed island **Yiel** in its middle.
The two starts sit on the plateau either side of the bay.

Source: `tools/maps/yiel_lymwerra/layout.py` (the plan and every number in
it), built into the scene by `Waning Border > Maps > Build + Bake Yiel
Lymwerra` (`Assets/Scripts/Editor/MapBuilderYielLymwerra.cs`, over the shared
`MapBuilderFromLayout`). The plan images are
`tools/maps/yiel_lymwerra/preview.png` (annotated) and `natural.png`.

**Size history (2026-10-17).** First drawn at 1024 × 1024 m (a size meant for
the largest, mostly impassable maps), then redrawn at 256 × 128 m. That was
too small for the territory rules: the start clearing (no node within 26 m
of a start, `StartClearing.asset`) is 52 m across, wider than a home could be,
so the homes had **no legal ground** for their seven nodes and 18 of 41 nodes
failed. It was enlarged to 384 × 192 m, which keeps every rule as it is.
The plan is drawn in **plan units** on a 256 × 128 field and the world is the
plan × `SCALE` (1.5, horizontal only; heights are metres), so resizing the
map again is one number in `layout.py`.

## Layout

The **east half is the west half mirrored**, exactly — both seats get the
same ground.

| Territory (west / east) | Where | Type |
|---|---|---|
| Gullhold / Ternhold | the starts, on the plateau beside the bay | Start |
| Gull Bluff / Tern Bluff | clifftop between the start and the bay's south horn | Normal + iron |
| Westerly Downs / Easterly Downs | plateau north-west / north-east of the bay, holds a bay ramp | Normal + veilstone |
| Gull Strand / Tern Strand | the beach under the coastal cliff — **reached only through the bay** | Iron rich |
| West / East Lymwérra | the shallows, split through the island — **buildable** (2026-10-19) | Empty |
| Bayhead (shared) | plateau north of the bay, between both bay ramps | Normal |
| Stormcairn (shared) | a pocket up in the mountains, one ramp down to Bayhead | Sanctum |
| Yiel (shared) | the island | Veilstone rich — **the curse always starts here** |

13 territories (the 1024 m draft's Ridge territories were folded into the
homes when the map shrank). Every territory has room for its nodes: all 41
are laid, and the island takes all four of its outcrops.

## Rules the map uses

- **Cliffs** ring the bay and run along the coast; they are height steps
  steeper than the walk limit and need no paint. Two 15 m **ramps** lead from
  the plateau into the bay (north-west and north-east); the Sanctum's ramp
  climbs from Bayhead into the mountains. The beaches have no ramp: they meet
  the shallows at the bay's mouth, so everything below the cliffs is reached
  through the bay.
- **The shallows are waded** (the `WaterPlane.wadeDepth` rule Fiendstone Pass
  introduced): the bay floor sits just under the surface, so armies walk it.
  The sea shelves off past the bay mouth and is too deep to walk.
- **The shallows are territories.** Adjacency is outlines touching
  (RegionMap), so an island ringed by unclaimed water would border nothing,
  and a territory bordering nothing can never be claimed (claims need a
  bordering connected territory), nor reached by anything that walks the
  territory graph. Two Empty shallows territories give the island its
  neighbours, and armies standing in the bay contest them. Folding them into
  the land around would make no sense here: the bay is a wide place, not a
  crossing.
- **The shallows are BUILDABLE (2026-10-19).** A territory you can claim
  you must be able to build in, or it can never be held (an unbuilt
  territory's meter only stands while an army does). The rule, for every
  map: **waded water — ground no deeper under the `WaterPlane` than its
  `wadeDepth`, i.e. above its walk line — is buildable only inside a
  SHALLOWS territory, one that is itself waded water**
  (`RegionSeedMarker.BuildableShallows`, written by the builder from
  `buildableShallows` in layout.json, read through
  `RegionMap.BuildableShallows`). Everywhere else water stays unbuildable at
  any depth, so a ford inside a land territory (Moatwatch, Fiendstone Pass)
  is crossed, never built on. One test enforces it,
  `BuildCommandHelper.TerrainPointOk`, which the player's placement, the
  plan / site validation, the AI's site searches and the node placement all
  share; the 15° slope limit and the passability test still apply on top.
- **The curse is pinned to Yiel.** Veilstone rich territories normally swap
  to a random spot each match (`randomizeCurseTerritories`,
  Territory_Claims.md); a territory whose `RegionSeedMarker.CurseStaysHere`
  is set never moves, and the island carries it (`pinCurse` in layout.json).
- **Small territories keep proportionate margins.** A node normally keeps
  10 m clear of any other territory and 8 m from impassable ground
  (`TerritoryResources` `BorderBand` / `BorderInset`); a territory too small
  for that (the island, first 49 × 32 m) has the margins capped at 35 % of its
  shorter half-width (Territory_Claims.md § 11). Without it the island had no
  legal ground and the curse rose elsewhere.
- **The island's top is flat enough to build on** (2026-10-18): radii 19 × 16
  plan units (57 × 48 m), a 4 m shore ramp and gentle 14 m-wavelength relief,
  so each of its four outcrops keeps room for four Trading Outposts once the
  curse is cleared (Build_Grid.md § The Trading Outpost's side slots). The
  earlier 4 m-wavelength relief was steeper than the 15° build limit almost
  everywhere.
- **Mountains** and **the sea** are covered by no territory outline, so they
  are unclaimable; mountains also carry NoWalk paint, which (not their
  height) is what makes them impassable.
- **Mountains are a third as high (2026-10-19)**: the original mountain
  profile — base, dome and ridge noise — kept whole, with its relief over
  the ground around it (the plateau, the pocket) scaled by 1/3 (`ground + (old - ground) / 3`,
  noise included), so every peak and trough keeps its place and
  proportion: the same ruggedness, squashed vertically. Never less than a
  few metres over the ground at their foot; their NoWalk paint and the
  missing territory outline, not their height, keep them impassable and
  unclaimable. The numbers are `MTN_BASE` / `MTN_DOME` (the original
  profile), `MTN_SCALE` and `MTN_MIN` in `layout.py`, which prints old and
  new relief.
- **Forest** is decoration (Regions.md § Region kinds): clumps at the
  mountains' feet and along the clifftops, never near a start, a ramp or
  Bayhead.

## Ground look

The second ground layer is **sand** on this map (`GroundSubstance003`, in
the slot other maps use for meadow or moss): beaches, the bay and sea floor,
the island's shore and the scree at the cliff feet (`sand.png`). Dirt is the
ramps plus worn patches (`dirt.png`, `bare.png`). Grass blades grow only on
the plateau, the pocket and the island's crown (`grass.png`, via
`FloraSpec.CanGrass`). The water is the Houidisoft ocean material as
`Sea Water.mat` in the map folder, started from Fiendstone Pass's hand-tuned
`River Water.mat` (deep blue-teal, waves and foam off, fine caustics); the
ocean material's own values band into stripes across a sea this size.

## Verified (2026-10-17)

- Every territory reachable on foot and bordering another (layout.py checks
  both); every seed on its own level.
- In the Editor, two Hard AIs: all 41 nodes laid, the curse rises on Yiel with
  4 nodes and its first army spawns there; no exceptions.
- The rectangle (2026-10-17): MapGenKit builds a non-square terrain
  (`TerrainSpec.SizeZ`), and the lobby image, thumbnail outlines, Muster Rolls
  map and road mask were made rectangle-safe; the rest of the runtime (nav,
  passability, fog, camera, curse field, minimap) already measured each axis.
