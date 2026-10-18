# Broken Oath (2026-10-18)

Five players, 768 × 448 m (rectangular, `TerrainSpec.SizeZ`), built for **3 v 2**.

Source: `tools/maps/broken_oath/layout.py` (the plan; every shape, territory,
ramp and type in it), built into the scene by `Waning Border > Maps > Build +
Bake Broken Oath` (`Assets/Scripts/Editor/MapBuilderBrokenOath.cs`, over
`MapBuilderFromLayout` with `LayoutMapStandardSpec`). The plan images are
`tools/maps/broken_oath/preview.png` (annotated: types tinted, areas, homes,
walking distances, checks) and `natural.png`.

## Layout

**Mirrored north/south only**; the two teams are not mirrors of each other.
The balance is numbers against ground (a design call, 2026-10-18):

| Side | Seats | What | Types |
|---|---|---|---|
| The three (west) | 1-3 | open low ground, homes close together; two one-ramp cliff pockets each half | Start; Crook (Normal), Scar (Normal + iron); the Lowmarch mostly Normal / Normal + iron, one Normal + veilstone |
| The two (east) | 4-5 | the **Highmarch**, a plateau above an escarpment with **four ramps up**; a private Tarn pocket each; more safe ground per player | Start; Tarn (Normal); the Highmarch Normal / Normal + iron / Empty, one Normal + veilstone at the back |
| The middle | — | low, open and rich, with the **Oathstone** on the axis (a raised Sanctum, four ramps) | Veilstone rich, Iron rich, Normal + veilstone |

Walking distance from every home to the middle is the same for both teams.
Node layouts are not mirrored by the engine on this map (it mirrors only maps
symmetric on both axes), so node spots are random within each territory;
the types are as above for every seat of a team. The lobby's team
assignment is the players' (Teams.md): the map intends seats 1-3 against
4-5.

**The curse is pinned** to the two Veilstone rich territories in the middle
(`RegionSeedMarker.CurseStaysHere`, `pinCurse` in layout.json). This map is
mirrored on one axis only, so `randomizeCurseTerritories` would deal the
curse to two RANDOM territories — in testing it landed on the Oathstone and on
one north-side territory, favouring the south seats. Pinned, both curse
territories sit on the axis between the teams, mirrored north/south.

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
