# Highfold (2026-10-18)

Eight players, 768 × 768 m. Four players start in the mountains, four in the open fields.

Source: `tools/maps/highfold/layout.py` (the plan; every shape, territory,
ramp and type in it), built into the scene by `Waning Border > Maps > Build +
Bake Highfold` (`Assets/Scripts/Editor/MapBuilderHighfold.cs`, over
`MapBuilderFromLayout` with `LayoutMapStandardSpec`). The plan images are
`tools/maps/highfold/preview.png` (annotated: types tinted, areas, homes,
walking distances, checks) and `natural.png`.

## Layout

**Mirrored across both axes** (the south-west quadrant is authored, two
seats per quadrant), so node layouts match per kind of seat. The centre is a
pond, so nothing straddles an axis.

| Per quadrant | What | Type |
|---|---|---|
| Crag (seats 1-4) | a valley in the corner massif, two levels up | Start |
| Eyrie, Cairn | high pockets off the crag, one ramp each | Normal, Normal + iron |
| Ledge | below the crag; its **one ramp is the only way out** to the fields | Normal |
| Meadow (seats 5-8) | in the open fields: no cliffs, no chokes | Start |
| Two edge hollows | between the massifs, one ramp each | Iron rich, Normal + veilstone |
| Barrow | round the pond, two ramps | Sanctum |
| The fields | 18 small territories, richest toward the centre | Veilstone rich and Iron rich nearest the pond, Normal + veilstone around, Normal / Normal + iron / Empty toward the massifs |

The mountain seats are the safest ground on the map and the poorest; the
field seats start beside the rich centre and have to hold it in the open.

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
