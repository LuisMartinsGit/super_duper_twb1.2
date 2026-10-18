# Fiendstone Pass (2026-10-16)

Six players, 1024 × 1024 m. Cliff shelves stepping down to a river. The map
is about **chokes you can shoot down into**: every way between levels is a
narrow ramp under a cliff edge, so archers on the shelf above hit attackers
before they reach the top.

Source: `tools/maps/fiendstone_pass/layout.py` (the plan and every number in
it), built into the scene by `Waning Border > Maps > Build Fiendstone Pass`
(`Assets/Scripts/Editor/MapBuilderFiendstonePass.cs`). The plan image is
`tools/maps/fiendstone_pass/preview.png`.

## Layout

The **south bank is the north bank turned 180°**, exactly — every start has
the same ground as the one opposite it.

From the river up, per bank:

| Level | What | Territories per bank |
|---|---|---|
| River | unclaimable water, crossed at **three fords** (west, centre, east) | — |
| 0 — low ground | between the river, the mountains and the first cliff; mountain spurs reaching toward the water separate them, the bank stays open along the river | 3 |
| 1 — rich pockets | small, closed by cliffs, **one ramp in** | 4 (neutral) |
| 2 — plateaus and the pass | two open plateaus (west, east), each with two ramps down to the low ground; a **mountain pass** (three territories) joins them behind the low ground | 2 + 3 |
| 3 — start shelves | one under each start; the corner players' shelves drop to their plateau, the middle player's to the pass centre | 3 |
| 3 — start pockets | a private pocket beside each start, one ramp off the start | 3 |
| 4 — starts | enclosed by cliffs and mountains, one ramp down to the shelf | 3 |

42 territories in all. The middle players (2 and 5) sit above the pass, so
both plateaus are reachable from them and they from both — riskier and richer
than the corners.

## Rules the map uses

- **Cliffs** are height steps steeper than the walk limit
  (`PassabilityGrid.MaxWalkableSlope`); they need no paint. **Ramps** are
  24 m wide slopes. Archer range (Archer `attackRange`) decides the kill zone
  under each ramp; the plan marks it.
- **Mountains** and **the river** are covered by no territory outline, which
  makes them unclaimable (a fully authored map's uncovered ground is no
  region). Mountains also carry NoWalk paint; nothing walks deeper than the
  water's wade depth.
- **Fords are waded, under water (2026-10-17).** They are bars in the
  riverbed that stop just *below* the surface, so they read as shallow water
  that armies walk through, not as dry land. This needs the one new rule this
  map brings: **`WaterPlane.wadeDepth`**, set per map on the scene's water
  (`layout.json` `wadeDepth`). Ground less than that far below the surface
  stays walkable (`PassabilityGrid`, and the move-click snap in
  `WorldClickInput`); deeper is blocked as before. Building in water stays
  forbidden at any depth. Every other map has a wade depth of 0, which is
  the old rule. The water level, the wade depth and the ford height are in
  `layout.py`. The sloped banks also pass through the wade band, which leaves
  a strip about a metre wide along each bank where units can step into the
  water. Only the fords cross; the riverbed between them is too deep.
  They are in the river, so unclaimable.
- **The river's look** is the Houidisoft "One Click Add Water" ocean Shader
  Graph (`Assets/Tools/Houidisoft technology/`), on the WaterPlane's mesh as
  `River Water.mat` in the map folder. The material is data: the builder
  copies it from the tool's `water.mat` only if it is missing, and after that
  it is tuned by hand (deep blue-teal, not cyan, because cyan means veilstone
  in [Art_Direction.md](../Art_Direction.md); waves off; small caustics).
- **Forest** is decoration, not territory ([Regions.md](../Regions.md) §
  Region kinds, 2026-10-16): patches of trees inside or across territories,
  at the feet of mountains and cliffs and along the banks — never on a ramp's
  approach, a ford, a pocket, a start or the pass.

## Ground shape and look (2026-10-17)

All authored in `layout.py`, mirrored between banks like everything else:

- **The river's edge is ragged**: its half-width wobbles by several metres,
  and each bank's slope varies in width (about 4.5 to 11.5 m) and is roughened.
- **The fords are organic**: their centre line swings and their width breathes
  across the river, their top is bumpy but always inside the wading band, and
  their sides slope into the riverbed instead of dropping off.
- **Rolling ground**: gentle rises on the flats, up to 5 m on the low ground
  and 4 m on the plateaus (less on shelves, pockets and starts; the per-level
  amounts are written to `layout.json` as `rollHeights`). The rises never dip
  (no land sinks under the water) and fade out near cliffs, ramps and the shore,
  so a cliff keeps its full step and a ramp its slope.
- **Ground texture variation**: the map's second ground layer is moss
  (`PaintSpec.MeadowSubstance`), painted from `meadow.png` into the hollows,
  along forest edges and cliff feet, and in loose patches; `bare.png` adds
  worn bare-earth patches to the dirt layer. Both stay within the terrain
  shader's four main layers.

## Resources (territory types, [Territory_Claims.md](../Territory_Claims.md) §11)

Every type is **authored, and identical on both banks and on both sides of a
bank** — Auto would deal them per match and hand one seat better ground.

| Territory | Type |
|---|---|
| Starts | Start |
| Start shelves | Normal + iron |
| Start pockets | Iron rich |
| Low ground: west and east | Normal + iron |
| Low ground: centre (the ford) | Normal + veilstone |
| Plateaus | Normal + veilstone |
| Pass saddles (west, east) | Normal |
| Pass centre | Empty — too small for a node set; it is the crossing |
| Pocket under the pass centre | Sanctum |
| Rim pockets (west, east) | Iron rich |
| Pocket in the bank under the west pass | Veilstone rich — see below |

**Veilstone rich** (cursed from the start) is authored once per bank, but
only as a count: every match swaps it with a random territory that is not
next to a start (`randomizeCurseTerritories`), and that territory takes the
pocket's place. The map must have at least one (Territory_Claims.md §9).

## Verified (2026-10-17)

- **Six-AI skirmish, headless, seed 20261017:** 0 exceptions; the replay
  re-simulates with every state hash matching (`-twbReplay`, exit 0).
- **Lockstep multiplayer, headless:** 2 peers + 4 AI slots, 600 s: both peers
  agree on all 18,000 tick checksums, no desync.
- **Resume from a save** (`-twbResume`) loads and plays on. A replay recorded
  across a mid-match save does NOT verify, but that is true on every map
  (Hollow Table fails the same way), so it is a save-system issue and not this
  map's.
- **Look, in the Editor:** cliffs and ramps read clearly from the player camera;
  the river is deep blue water and each ford is a shallow under it, its bed
  visible through the caustics. The fords' edges are straight-cut.
