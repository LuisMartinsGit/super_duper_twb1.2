# Territory & Nature

> **Nature regions are the map's territory readout.** Forests, thickets and
> groves are impassable terrain that **changes appearance to show who owns the
> ground under them** — wild at the start, then blossoming under Alanthor,
> stilled and dry under Runai, burnt to ash under Feraldis, crystallised under
> the curse.
>
> This exists because the in-world territory signal is deliberately thin.
> [Overview.md § The influence map](Overview.md#the-influence-map-decided-2026-07-06)
> settled that the in-world overlay is **border lines only** — no interior
> fill — and that decision stands. Ground tint sits under units, health bars
> and fog and washes out at camera distance. Nature regions give the interior
> a signal that reads at a glance **because it is vertical and silhouetted**,
> without painting swathes of ground.
>
> The second reason is the important one: **nature regions are impassable, so
> they are walls.** Coding them to ownership means the map's *shape* tells you
> whose land you are walking into. The signal is welded to a tactical fact
> instead of decorating one.
>
> **See also:** [Overview.md § The influence map](Overview.md#the-influence-map-decided-2026-07-06)
> (channels, the 0.5 border, decay, fog), [Curse_And_Shardroot.md](Curse_And_Shardroot.md)
> (the per-culture verbs this borrows), [Fire.md](Fire.md) (the four ground
> states — the Feraldis exception below hands off to it),
> [Build_Grid.md](Build_Grid.md) (one-cell impassable nodes).
>
> Doc version: 2026-08-27 — **first design pass, from user direction.**
> Everything here is marked **(new — not yet in code)** unless it says
> otherwise. Decisions marked **(call)** are recommendations made in the
> absence of direction and are the ones to overturn first.

---

## 1. Why not just paint the ground

Already answered, but worth stating so it is not re-litigated:

| | Ground paint | Nature regions |
|---|---|---|
| Reads at RTS camera distance | Poorly — washed out under units and fog | Strongly — vertical, silhouetted |
| Costs map area | Yes, the whole interior | No, only where the author placed nature |
| Says anything tactical | No | **Yes — it is impassable; it shapes the fight** |
| Canon position | In-world fill was rejected; border lines only | Fills the gap that decision left |

Passable ground **still** shifts with ownership, and keeps doing so — that is
already shipped behaviour in `InfluenceTerrainPainter`. This doc does not
change it beyond one instruction: **keep it subtle.** It is the underlay; the
nature regions are the headline. Borders stay exactly as canon has them —
smooth splines along the 0.5 contour.

---

## 2. The five states

One per influence channel owner, plus the unowned default. The verb column is
not decoration — it is the same verb the culture applies to a well in
[Curse_And_Shardroot.md](Curse_And_Shardroot.md), applied to living ground.

| State | Owner | Verb | Look |
|---|---|---|---|
| **Wild** | none (neutral) | — | Ordinary temperate woodland. Mixed canopy, undergrowth, no signal. The map's resting state. |
| **Blighted** | Curse | — | Veil-crystal growth up the trunks, canopy greyed and translucent, purple crust creeping from the roots. Reads as the same material as the crust. |
| **Cultivated** | Alanthor | *purify* | Blossom and fruit, cleared undergrowth, ordered spacing — woodland kept rather than woodland conquered. Stone markers at the edge. |
| **Stilled** | Runai | *pacify* | Not dead — **arrested.** Leaves dried and motionless, pale bark, sand drifted into the roots, no undergrowth. Desert reclaiming it slowly. Matches the canon Runai tents/desert aesthetic. |
| **Ashen** | Feraldis | *destroy* | Charred trunks, no canopy, ash floor, embers. **Not snow** — Feraldis is fire and blood and means to taint, not to freeze. See §6. |

**Age 0 shows only Wild.** Age 0 buildings and units grant no influence
(canon), so the map cannot change state before cultures emerge. This is free
pacing: the world visibly wakes up at age-up, which is exactly when territory
starts to matter.

---

## 3. Passability — the rule and the one exception

**(call) A nature region is impassable in every state.** Appearance carries the
signal; passability stays constant. This is the conservative choice on purpose:
making large areas flip between passable and blocked mid-match would rewrite
chokepoints under the players' feet, and every path, formation and flow-field
consumer would have to tolerate it.

**The one deliberate exception: Feraldis burns it down.**

A region that reaches the Ashen state **burns**, and burnt ground is **flat and
passable**. Feraldis is the only culture that can open a chokepoint that was
closed at map load.

This earns its complexity:

- It is the culture's canon verb (*destroy*) applied to terrain, consistent
  with what they do to wells.
- It hands off cleanly to [Fire.md](Fire.md) — the region burns through
  Fuel → Burning → Ash, and Ash is already specified as passable ground that
  reverts to its original terrain. Nature regions become a fuel source; the
  fire model already exists.
- It gives the military culture a **map-shaping** power that is not just more
  damage, and it is legible to the enemy: you can *see* the hole being opened.
- It is one-way and slow, so it cannot flicker.

Consequences to accept: a Feraldis-opened corridor stays open (Ash reverts to
terrain, not to forest), and Feraldis influence never decays (canon), so the
ground behind it stays theirs. That is a real strategic asset and should be
costed as one.

**Regrowth:** none within a match. **(call)** — reopening a burnt region would
re-close a chokepoint and reintroduce exactly the flicker this section avoids.

---

## 4. Flipping states without flickering

Influence is a live, decaying field; naive per-frame sampling would make
contested forests strobe.

- **Region-level, not per-tree.** The whole region has one state, decided from
  the **dominant channel at the region centre**. A forest with a two-tone
  canopy reads as noise.
- **Hysteresis band.** Claim requires the channel ≥ **0.6**; the region reverts
  toward Wild only below **0.4**. The canon 0.5 border sits inside the dead
  zone, so a border resting on a forest does not oscillate.
- **Dwell time — 20 s (call).** The threshold must hold continuously. A raid
  passing through does not re-landscape the map.
- **Transition is a visible animation, not a swap.** Seconds, not frames,
  staggered per tree so the region turns like weather crossing it rather than
  a light switch. Reuse the front-tracked glide model
  `InfluenceTerrainPainter` already uses for ground.
- **Contested regions hold their last state.** No blended or "half-owned"
  look. If nobody is above 0.6, the region keeps what it had; it returns to
  Wild only after the owner drops below 0.4 for the dwell time.
- **Ashen never reverts** (§3).

---

## 5. Fog of war

**The influence map is not public information** (canon, 2026-07-06 rev.4), and
nature state is a direct readout of it. It must obey the same rule or it is an
intel leak that bypasses fog:

- **Unexplored:** the region renders **Wild**, whatever it actually is.
- **Explored but not currently visible:** renders its **last seen** state,
  dimmed with the terrain — the same treatment influence itself gets.
- **Visible:** renders live.

This is a hard requirement, not polish. A player must not be able to read the
enemy's territory off the treeline through fog.

---

## 6. Canon reconciliation

Two of the original per-culture looks needed checking against the Design folder.

**Runai — desert: confirmed.** [Age_1_Runai.md](Age_1_Runai.md) states "Tents /
desert aesthetic" outright. The only refinement here is *pacified*, not
*killed*: Runai's verb is **pacify**, so their forest is **stilled and drying**,
not destroyed. Sand takes it slowly; that is the culture.

**Feraldis — snow: rejected.** [Age_1_Feraldis.md](Age_1_Feraldis.md) is
explicit: "**Fire and blood.** Where Alanthor means to purify and guard against
the curse, Feraldis wants to **taint it further**." Withered snow reads as a
frost faction and belongs to nobody in this game. **Ash and char** is the
on-canon look, and it is what makes the burn-down exception in §3 coherent
rather than bolted on.

**Alanthor — fruit and flowers: kept, with a caveat.** Their verb is *purify*
and their identity is defense and stewardship, so a kept, blossoming woodland
fits. The caveat is economic — see §7.

---

## 7. Does ownership do anything, or only show something?

**(call) Appearance is the signal. It grants no new resource and no new
mechanic** — with the single §3 exception.

The temptation is to make Alanthor's fruiting forest harvestable. Resist
inventing a resource for it: canon already has a rule that does the job.
**Economy inside the border doubles** (Overview.md), currently wired to
Gatherer's Hut area income. An Alanthor player who has claimed a forest is by
definition inside their own border there, so a hut on a Cultivated forest edge
already earns double. The fruit is the *visual explanation* of a bonus the
design already grants — not a second bonus.

If a distinct mechanic is wanted later, that is a separate pass and should be
costed against the counter table in [Combat_Pacing.md](Combat_Pacing.md).

---

## 8. Authoring: mint state once, analogues by table

**The map is authored exactly once, in its mint natural state.** No culture
variants are ever hand-painted. Every asset the author uses declares its
per-owner **analogues**, and the runtime substitutes.

```
  dirt   ->  tiles (Alanthor)  |  sandstone (Runai)  |  ash (Feraldis)  |  crust (Curse)
  grass  ->  lawn              |  dry scrub          |  cinder          |  greyed
  oak    ->  blossoming oak    |  dried oak          |  charred oak     |  crystalled oak
```

Author dirt paths, grass clearings and oak stands the way any map is made. The
territory look is a **lookup**, not a second pass of authoring.

### Analogues are shader overlays, not terrain layers

**Correction (2026-08-27): an earlier draft of this section budgeted analogues
as extra `TerrainLayer`s and concluded six ground types would need 30 layers
and was "not viable". That analysis was wrong for this project** — it described
Unity's default splatmap pipeline, which the game stopped using. The real
constraint is not layer count.

The project already renders dynamic ground the way large RTS titles do, and the
shader says so outright: `TWBTerrainOverlays.hlsl` is *"SC2-creep-style dynamic
ground overlays"*. A small world-space coverage mask is sampled **per pixel**
and blended over the splat result:

| Mask | Channels | Source |
|---|---|---|
| `_TWB_CultureMask` (128² RGBA32) | R = Alanthor, G = Feraldis, B = Runai, A = Curse | `PlayerInfluenceMap` |
| `_TWB_BloodMask` (128² RGBA32) | R = Blood | `BloodMap` |

Built each frame by `InfluenceMaskTexture`, whose own header states the cost:
**"~16k texels of float math and two 64 KB texture uploads (skipped entirely
when nothing is changing)"** — and, decisively, *"It replaces ALL runtime
SetAlphamaps painting — the terrain's splat data is never touched at runtime
again."*

So the two budgets are separate, and only one of them is the author's problem:

| | Lives as | Budgeted by | Author's concern |
|---|---|---|---|
| **Natural ground** (mint state) | `TerrainLayer` splat layers | Unity's ~4-layers-per-pass shading cost | **Yes** — this is normal map authoring |
| **Owner analogues** | Shader properties + the coverage mask | Texture samples in one pass; no extra terrain layers | **No** — adding a culture costs no layers |

**Adding a culture look costs zero terrain layers.** `_AlanthorAlbedo`,
`_BloodAlbedo`, `_CurseAlbedo` and the `_TerraceAlbedo` masonry set are already
wired this way. `_FeraldisTint` and `_RunaiTint` are explicitly marked
*"placeholder"* / *"not in demo"* — those two cultures are missing **art**, not
missing layer slots.

### Substitution in the mask model

The mint-state-once principle (§8 opening) is unchanged and gets *easier* here.
The analogue lookup moves into the shader:

- The terrain's own splat control map already says which natural layer dominates
  at a texel — that is exactly the substitution's input, readable per-pixel with
  no CPU work at all.
- The culture mask says who owns that texel.
- The overlay picks the analogue for (dominant natural layer x owner) and blends
  by mask coverage.

`dirt -> tiles, grass -> lawn` becomes a shader-side branch on the dominant
splat channel rather than a second set of terrain layers. The authored pattern
survives for the same reason as before — the pattern *is* the splat control map
— and it now survives without touching splat data at runtime.

Boundaries already erode through value noise (the SC2 trick, per the shader
header), so fronts are organic and continuous without any CPU painting or tick
cadence.

**Practical ceiling:** the per-culture-per-natural-layer analogue count is
bounded by texture samples and shader variants in one pass, not by terrain
layers. That is a much higher ceiling and a different kind of cost — it wants
profiling on a target GPU, not arithmetic on a layer count.

### `InfluenceTerrainPainter` is superseded

`InfluenceMaskTexture` states it replaced all runtime `SetAlphamaps` painting.
Any design that reasons about `_baseWeights`, row-amortized alphamap uploads or
per-frame splat writes — **including an earlier draft of this doc** — is
describing the legacy path. New work belongs in the shader + mask system.

Whether the painter should be deleted outright is a separate cleanup question,
not settled here.

### Trees: prototype substitution, not re-authoring

Substitution may let vegetation **stay in Unity Terrain** rather than moving to
spawned prefabs as an earlier draft of this doc assumed.

`TreeInstance.prototypeIndex` is per-instance, and `treeInstances` is settable
at runtime. Register every state's prototype on the terrain (species x 5), then
a region's state change rewrites `prototypeIndex` for the instances inside it.
The author paints trees with Unity's normal tree brush, in mint state, once.

**(risk — prototype before committing)** Writing `treeInstances` triggers a full
tree and collider refresh. State changes are rare by design (20 s dwell, §4), so
this should be affordable, but it needs measuring on a real map before it is
load-bearing. If the refresh proves too costly, the fallback is the
spawned-prefab path, which is strictly more code but has no refresh cost.

### Region markers

Region extent is still authored, because passability (§3) needs an area:

| Field | Meaning |
|---|---|
| `Radius` | Region extent — drives the impassable disc |
| `AnalogueSet` | Which table this region uses (optional; falls back to the map default) |

Place a `NatureRegionMarker`, set the radius, bake — same `MapMarker` +
`MapInfoBaker` contract as `PlayerStartMarker`, and the same failure mode if a
re-bake is skipped after moving one. Non-circular regions are **out of scope for
the first pass (call)**; overlapping circles cover most shapes, and a disc is
what `PassabilityGrid.BlockObstacle(center, radius)` already takes.

---

## 9. What this lands on (existing code)

Recorded so the implementation pass does not rebuild any of it:

| Need | Already exists |
|---|---|
| Ownership at a point | `PlayerInfluenceMap.ChannelStrengthWorld(channel, x, z)` — 8 faction channels + curse |
| Per-pixel owner coverage | `InfluenceMaskTexture` — 128² RGBA culture mask (R/G/B/A = Alanthor/Feraldis/Runai/Curse) + blood mask, ~16k texels + two 64 KB uploads per frame, skipped when static |
| Per-pixel ground substitution | `TWBTerrainOverlays.hlsl` in `TWB/Terrain/Lit` — already blends Alanthor slate, blood and curse over the splat result, with noise-eroded fronts |
| Which natural layer a texel is | the terrain's own splat control map, readable in-shader — the substitution's input, needing no CPU pass |
| Area impassability | `PassabilityGrid.BlockObstacle(center, radius)` / `UnblockObstacle` |
| Obstacle as its own cell class | `PassabilityGrid.ObstacleBlocked = 3` — flips without disturbing cliffs (`TerrainBlocked`) or footprints (`BuildingBlocked`) |
| Forest as a `(center, radius)` area | `ObstacleBootstrap.ForestPositions`, already this shape and already read by the minimap |
| Region authoring base | `MapMarker` abstract MonoBehaviour + `MapInfoBaker` |
| Burning, ash, reversion | [Fire.md](Fire.md)'s four ground states |

### Two existing gaps this closes

1. **`ObstacleBootstrap.ForestPositions` is empty on hand-authored maps.**
   Procedural scatter was removed and never replaced, so there is no forest
   area data on any real map today.
2. **Nothing calls `BlockObstacle` for trees** — only the three resource-node
   types do. Meanwhile the shipped localization tells players *"Forests block
   sight and movement. Scouts pierce sight through trees but still pay the move
   cost."* On hand-authored maps that string is currently **false**. §3 is what
   makes it true.

---

## 10. Open questions

1. **Tree refresh cost** — measure a `treeInstances` rewrite on a real map
   before the Unity-Terrain path is load-bearing (§8).
2. **Analogue shader cost** — the ceiling is texture samples and shader variants
   in one pass, not terrain layers (§8). Wants profiling on a target GPU.
3. **Do nature regions block line of sight?** The localization says forests
   block sight with a Scout exception. Reaffirm or drop it — it is not in the
   Design folder anywhere, only in a translation string.
4. **Does the curse's crust spread *into* a nature region**, or does Blighted
   replace it? Interacts with the crust's own impassability stamp.
5. **Feraldis and Runai ground art does not exist.** `_FeraldisTint` and
   `_RunaiTint` are flat placeholder colours marked *"not in demo"*, while
   Alanthor has a full slate + masonry-terrace set. This is the nearest
   shippable work and it needs no new system.

6. **Code and canon disagree about fog, and canon wins.** §5 of this doc follows
   [Overview.md](Overview.md): the influence map *"is NOT public information"*.
   But `InfluenceMaskTexture` states the opposite in its header — *"masks are not
   fog-gated … territory is public information by design"* — and ships that way.
   Per CLAUDE.md the Design folder wins, so either the mask gets fog-gated or
   Overview.md gets amended. **This must be resolved before nature regions ship**,
   or the treeline leaks territory through fog.
