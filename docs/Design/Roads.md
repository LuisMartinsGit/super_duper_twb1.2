# Roads & Plazas

> **Status (2026-09-18): canonical, and IMPLEMENTED the same day** — the
> procedural road network: what a site is, how sites connect, where roads
> go, and what they look like under each owner. Code in
> `Assets/Scripts/World/Roads/` (+ `RoadNetwork.asset`), shader side in
> `TWBTerrainOverlays.hlsl`. **Verified on screen:** the Age 0 earthen
> network on Hollow Table — plaza around the Hall, routed roads to the
> supply spots and outcrops, organic edges. **Not yet seen on screen:**
> the culture-paving path and its road-aligned bricks (needs an Age 1
> match), the construction gate (a site placed mid-match) and a territory
> merge; all are single comparisons in the shader / one union-find step
> and are covered by the rules below, but look at them before calling the
> feature done. The curved roads, carriage tracks and lobed, fading plazas
> ARE verified on screen (same day) and the developer signed the look off. Decided with the developer on 2026-09-18: **paving is
> network-only** (stone appears on plazas and roads, never as a blanket
> over a territory), and **networks are per territory**, merging only
> where one faction has built on both sides of a border.
>
> **Supersedes:**
> - the blanket culture ground in
>   [Territory_And_Nature.md](Territory_And_Nature.md) §8 ("dirt → tiles /
>   sandstone / ash" over the whole owned area). The ground analogue is now
>   the road network; forests and borders carry the rest of the territory
>   readout. Cliff terracing inside Alanthor territory is unchanged — it is
>   a cliff treatment, not ground paving.
> - the hand-painted "paths worn between hall, huts and the nearest
>   resources" in [Art_Direction.md](Art_Direction.md) §5.1. Paths are
>   procedural and follow play; the map author paints no roads.

---

## 1. In one paragraph

Every map carries a trail network from the first frame: within each
territory, the outcrops, supply spots and curse nodes are joined by
earthen trails routed around cliffs and forests. Every building that goes
up clears a **plaza** around itself and joins the network, so a base grows
a road web as it is built, and the web changes shape as buildings come and
go. What the ground *is* follows who owns it: under the curse and in Age 0
the network is trampled earth; once a culture holds the ground its plazas
become that culture's paving and its roads become well-defined stone. No
map author paints a road; no player places one.

## 2. Sites

A **site** is anything worth walking to:

| Site | Source |
|---|---|
| Veilstone outcropping, iron deposit, veilsteel deposit | resource-node entities (`VeilstoneOutcroppingTag`, iron, `VeilsteelDepositTag`) |
| Supply spot | `SupplyNodeTag` (the Gatherer's Hut footprint — a site whether or not the hut exists yet) |
| Curse well, curse pocket | `BorderMainNodeTag`, `SmallNodeTag` |
| Building | `BuildingTag` + `BuildingSize`, any faction, **including under construction** |

**Not sites:** wall segments, gates and wall towers (they are a barrier,
not a destination), chapels docked to the temple (the temple is the site),
and anything owned by no one and tagged as none of the above.

A site belongs to the **territory it stands in** (`RegionMap.RegionAt`).

## 3. Networks

### 3.1 Per territory, merged by building

- Each territory's sites form one network.
- Two territories' networks **merge** when the same **player** faction has
  at least one building in each. Merging is transitive (three territories
  with Blue buildings are one Blue network). Curse nodes never merge
  territories: a pocket in Blue's land is a site in Blue's network, not a
  bridge to the well's.
- A territory with no buildings keeps its own trail network of nodes.

So at map start there is one small network per territory; roads cross a
border only once someone has built on the far side.

### 3.2 Which sites connect

Within a network, edges are the **relative-neighbourhood graph**: two
sites are joined unless a third site sits closer to both of them. This
is the standard for organic road webs — always connected, no crossings, no
star-bursts from a central hub, and a new site re-routes its neighbours'
edges instead of just adding spokes. Recomputed for the whole network on
every change (networks are tens of sites; this is microseconds).

### 3.3 Where roads go

An edge is **routed, not drawn straight**: A* on the 1 m nav grid using
`NavGridQuery.IsCellPassable` plus a slope penalty from the terrain
height. Roads bend around cliffs, forests and water. If no passable route
exists (an island site) the edge is dropped.

The cell path is then made a **road, not a ruler line** (2026-09-18):
Douglas-Peucker keeps only the cells that bend it (1.2 m tolerance), knots
are resampled no more than one wavelength apart (14 m), every interior
knot is pushed sideways by smooth noise (± 2 m, fading to zero at both
ends), and a Unity `Spline` (`com.unity.splines`, `AutoSmooth` tangents)
runs through the knots, sampled every 0.5 m. Every sample is checked for
passability; if the meander would enter a cliff the un-pushed knots are
splined instead, and if even that fails the rounded cell path is used. So
a road always curves, and never through a wall.

## 4. Plazas

A building clears a disc of radius **half its longer footprint side + 3 m**
(Hut ≈ 5 m, Hall ≈ 7 m, Barracks ≈ 8 m). Plazas of adjacent buildings
merge naturally. Resource nodes and curse nodes get a smaller disc
(**2.5 m**) so trails visibly arrive somewhere.

**Never a circle.** The rim radius varies with bearing by ± 30 % of
low-frequency noise seeded per site (two or three lobes, no two alike),
and coverage is full only out to 45 % of the radius, fading to the rim —
so the shader's noise erosion bites into a gradient and the earth blends
into the moss instead of stopping at a line. Roads fade over their outer
40 % the same way.

Widths: road **3 m**; trail (a network with no buildings at all) **2 m**.

## 5. The look

Decided **per pixel in the shader**, never at raster time — so a territory
flipping owner restyles its roads with no rebuild, and every rule below is
one comparison:

| Ground owner (culture mask) | Network state | Plaza | Road |
|---|---|---|---|
| none (unclaimed / Age 0 player) | any | earthen | earthen |
| Curse | any | earthen, crust creeps over it | earthen |
| Age 1 culture | endpoint(s) under construction | earthen (trampled approach) | earthen |
| Age 1 culture | finished | **culture paving** | **culture paving, road-width, well defined** |

- **Earthen** = the map's own Dirt terrain layer, sampled at road tiling,
  **lifted** at the core (`earthenDarken` 1.15 — a worn dry path reads
  lighter than the moss around it; the first cut darkened it and the roads
  read as black).
- **A road is not a plaza's dirt: it carries carriage tracks.** The raster
  stores each texel's exact signed lateral distance across the road (mask
  A channel), and the shader draws two wheel ruts from it (centred at half
  the road width, darkened 45 %, wobbled by world noise so they are not
  ruler-straight) with a strip of the original ground surviving between
  them — a cart track with moss down the middle. Plazas carry no lateral,
  so no ruts.
- **Bricks follow the road.** The raster also stores the road's tangent
  (`_TWB_RoadDir`), and Age 1 paving on a road samples the brick texture
  in the road's own frame (along, across) instead of world XZ; plazas and
  cliff terraces stay world-aligned, and the two are blended where a road
  enters a plaza so nothing snaps.
- **Culture paving**: Alanthor = the existing slate-brick set
  (`_AlanthorAlbedo`); Runai = sandstone, Feraldis = dark flagstone — both
  ship with their art passes and use the Alanthor set under their tint
  until then.
- **Finished** = the building is complete; for a road, both endpoints are
  complete (nodes count as complete). Stored in the mask at raster time
  (§7) because it is a property of the network, not of the ground.
- The curse overlay stays on top of everything, as today.

The blanket culture ground is gone: outside plazas and roads, owned
territory shows its natural layers. Cliff terracing (Alanthor) stays
territory-wide.

## 6. When it changes — and how it fades

The network is presentation — it reads simulation state and never writes
it, so it is not lockstep-relevant. It re-reads its sites twice a second
(cached entity queries, no per-tick `CreateEntityQuery`) and reconciles
them with its own state: every plaza and every road is a **state with a
strength** (0..1) that eases toward 1 while its site / edge exists and
back to 0 after it is gone. Coverage is scaled by that strength, and
because the shader erodes coverage through noise, a fading shape is not a
translucent ghost — it is eaten from the edges inward.

| Event | What happens on the ground |
|---|---|
| Building placed | its plaza and its new roads **wear in over 2.5 s** (`fadeInSeconds`), from the centreline outward |
| Building destroyed, node exhausted, pocket cleansed | its plaza and every road that no longer belongs to the graph are **reclaimed by the grass over 75 s** (`fadeOutSeconds`, 60–90 s by design), from the rims inward |
| A new site re-routes its neighbours | the replaced road reclaims at the slow rate, the new ones wear in at the fast rate — the old path lingers a minute under the new web, as a worn path would |
| Building completes (Age 1) | the finished flag flips and stone appears; not eased yet (open question 5) |
| Match start | the map's trails pre-exist: the first sync snaps every state to full strength |

A dead state at strength 0 is forgotten. Routes are cached per endpoint
pair, so a change re-routes only the edges it touched; the mask is
re-rasterised only while something is animating — every 1/12 s while a
wear-in is running (it has to read as continuous under the erosion),
every 0.5 s when only reclaims are — and not at all when the network is
still. The direction mask is rewritten only when a route appears or dies.

## 7. Data

- **`_TWB_RoadMask`**, 512² RGBA32, world-aligned with the culture mask
  (`_TWB_MaskST`): **R** coverage (plaza + road, soft-edged), **G**
  finished, **B** plaza-vs-road (1 = plaza), **A** signed lateral distance
  across the nearest road (0.5 = centreline). 512 because the 128² culture
  mask is ~1.5 m per texel on a 192 m map and a 3 m road needs ~0.4 m.
- **`_TWB_RoadDir`**, same size: **RG** the nearest road's unit tangent
  (0.5, 0.5 = none). Lateral and tangent belong to whichever road is
  NEAREST at that texel (a per-texel distance buffer settles junctions).
- **`RoadNetwork.asset`** beside `RoadNetwork.cs`: plaza margin, node disc
  radius, road and trail width, slope penalty, curve tolerance / meander
  amplitude and wavelength / sample step, plaza edge noise and fade, road
  edge fade, rut offset / width / depth, centre strip, earthen tone, poll
  interval, fade-in / fade-out seconds, fast / slow raster intervals. No
  numbers in code.
- Code: `Assets/Scripts/World/Roads/` — `RoadNetwork` (mount, sites,
  rebuild, mask), `RoadGraph` (RNG + A* + smoothing), `RoadRaster`
  (curves and discs → mask). Shader side in `TWBTerrainOverlays.hlsl`.

## 8. What this touches

- [Territory_And_Nature.md](Territory_And_Nature.md) §8 — ground analogue
  is network-only (note added there).
- [Art_Direction.md](Art_Direction.md) §5.1 — paths are procedural;
  §6.3 gains a row: "roads and plazas = someone built here / walks here".
- The minimap does not draw roads (open question 3).

## 9. Open questions

1. **Runai** "no walls" identity — do Runai roads read as *sand-swept
   paths* rather than paving, to keep the nomad feel? Default until decided:
   sandstone paving.
2. Should a **destroyed** building leave a ruined plaza (earthen scar) for
   a while, or vanish with the building? Default: vanishes on the next
   rebuild.
3. Minimap roads — probably yes at some zoom, not in scope here.
4. Should AI base-building **prefer** plazas / road-adjacent cells? Not in
   scope; the network is presentation only.
5. Should the earthen → stone flip on completion (Age 1) be eased like the
   wear-in, and should a reclaim under Age 1 paving go stone → earthen →
   grass? Today the finished flag is a hard switch.
6. **The fades are implemented but not yet seen on screen** (2026-09-18):
   no capture caught a building going up or down. The cost to measure is
   the 12 Hz re-raster during a 2.5 s wear-in (~3–5 ms of CPU raster plus a
   1 MB upload each); if it hitches, lower `fastRasterSeconds` to 8 Hz.
