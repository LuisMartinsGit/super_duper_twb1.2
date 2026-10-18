# Moatwatch (2026-10-18)

Four players, 512 × 512 m. Each player starts in a corner keep behind a moat; two fords lead out to one big open middle.

Source: `tools/maps/moatwatch/layout.py` (the plan; every shape, territory,
ramp and type in it), built into the scene by `Waning Border > Maps > Build +
Bake Moatwatch` (`Assets/Scripts/Editor/MapBuilderMoatwatch.cs`, over
`MapBuilderFromLayout` with `LayoutMapStandardSpec`). The plan images are
`tools/maps/moatwatch/preview.png` (annotated: types tinted, areas, homes,
walking distances, checks) and `natural.png`.

## Layout

**Mirrored across both axes**: the south-west quadrant is authored and the
other three are its mirror images, so `TerritoryResources.MirrorPartners`
lays the same nodes for every seat. Nothing straddles an axis (the centre is
a rock tor).

| Per quadrant | What | Type |
|---|---|---|
| Keep | the home, inside the moat | Start |
| Two garths | small territories inside the moat, against the map edges | Normal, Normal + iron |
| Two fords | waded water across the moat, facing the middle — territories so the keep borders the expanse (a territory ringed by unclaimed water borders nothing) | Empty |
| Barrow | a raised mound (one level up), two ramps | Sanctum |
| The expanse | 11 small territories outside the moat: poor by the moats and along the edges, rich toward the centre | Normal / Normal + iron near the moat; Normal + veilstone and Iron rich in the middle ring; Veilstone rich round the tor |

The moat is deep water (unclaimable, impassable) except at the fords, which
sit just under the surface (`WaterPlane.wadeDepth`) — armies wade them,
nothing is built in them. Holding the keep is easy: two 20 m crossings.
Everything worth having is outside.

## Rules the map uses

- **Drawn with the shared kit** (`tools/maps/layoutkit.py`, 2026-10-18):
  `layout.py` only places shapes, territories, ramps and types; the kit
  folds the canonical part onto every seat, builds the heights (levels a
  cliff apart, ramps, rolling ground, mountains, water), and checks every
  territory is reachable on foot, borders another territory (or it could
  never be claimed), sits in the claim graph of every home, and has room
  for its nodes under the node rules (start clearing, home band, border
  band, inset, spacing — replayed from `TerritoryResources`). `layout.py`
  writes `natural.png` and `preview.png`; `--export` also writes the
  heights, masks and `layout.json` the builder reads.
- **Cliffs** are height steps steeper than the walk limit; **ramps** are
  16 m slopes. Mountains are covered by no territory outline (unclaimable)
  and carry NoWalk paint.
- **Forest** is decoration (Regions.md § Region kinds): clumps at mountain
  and cliff feet and by water, never on a ramp's approach, a ford, a home,
  a pocket or a Sanctum; the kit thins it until nothing is cut off.
- **Territory types are authored**, the same for every seat
  (Territory_Claims.md § 11). The poor, defensible ground is Normal / Normal
  + iron / Empty; the rich ground is Normal + veilstone, Iron rich, Veilstone
  rich (the curse — re-dealt each match by `randomizeCurseTerritories` as one territory and its three mirror images, so every seat faces the same curse)
  and the Sanctums. Territories are kept small (homes about 9-11k m², the
  rest mostly 2-6k m²; exact areas are on the preview).
- **Sanctums** carry the Sanctum crystal landmark (Art_Direction.md § 6.5)
  and count toward dominion (Territory_Claims.md § 7, § 11.4).

## Verified

See the end of this file once the headless runs are in.
