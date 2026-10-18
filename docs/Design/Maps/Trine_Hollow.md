# Trine Hollow (2026-10-18)

Three players, 640 × 640 m, **three-fold rotational symmetry**: a bowl stepping down to a rich basin.

Source: `tools/maps/trine_hollow/layout.py` (the plan; every shape, territory,
ramp and type in it), built into the scene by `Waning Border > Maps > Build +
Bake Trine Hollow` (`Assets/Scripts/Editor/MapBuilderTrineHollow.cs`, over
`MapBuilderFromLayout` with `LayoutMapStandardSpec`). The plan images are
`tools/maps/trine_hollow/preview.png` (annotated: types tinted, areas, homes,
walking distances, checks) and `natural.png`.

## Layout

The wedge round seat 1 (north) is authored and turned 120° and 240°.
Node layouts are not mirrored by the engine on a rotated map, so node spots
are random within each territory; the territory types are the same for every
seat.

| Per seat | What | Type |
|---|---|---|
| Throne | the home on the rim (level 2), two ramps down | Start |
| Aerie, Spire | high pockets off the home (level 3), one ramp each | Normal, Normal + iron |
| Four terraces | a ring below the rim (level 1), cut from the neighbours' by mountain spurs | Normal, Normal + iron, Normal + veilstone, Empty |
| Saddle | a raised Sanctum on the spur between two seats, a ramp to the terraces either side — the back way between neighbours that avoids the basin | Sanctum |
| Four basin territories | the open floor (level 0) | Normal + veilstone, Iron rich |
| The Trine Heart (shared) | a raised mesa in the centre, one ramp per seat | Veilstone rich |

No water. The corners of the square are mountain.

**The curse is pinned** to the Trine Heart (`RegionSeedMarker.CurseStaysHere`,
`pinCurse` in layout.json). A rotated map is not one the engine mirrors, so
`randomizeCurseTerritories` would move the curse to one random territory —
in testing, a basin territory next to a single seat. The Heart is the same
distance from every seat.

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
  rich (the curse — pinned on this map, see above)
  and the Sanctums. Territories are kept small (homes about 9-11k m², the
  rest mostly 2-6k m²; exact areas are on the preview).
- **Sanctums** carry the Sanctum crystal landmark (Art_Direction.md § 6.5)
  and count toward dominion (Territory_Claims.md § 7, § 11.4).

## Verified

See the end of this file once the headless runs are in.
