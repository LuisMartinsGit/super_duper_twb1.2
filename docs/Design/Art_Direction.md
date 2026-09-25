# Art Direction

> **Status (2026-09-18): canonical truth source for the LOOK of the game** —
> mood, light, palette, where the player colour lives, and what every
> decorative element on the ground *means*. Written from a reference
> comp (dark-fantasy dusk, see §1) against the current build. §11 is the
> task breakdown and §12 the open questions. **Implemented so far
> (2026-09-18): Pass 1 tasks 1, 2, 6 and the interim half of 4, plus the
> crystal glow (13 and the well/pocket colour of 12)** — the grade and
> lighting live in `DayNightCycle.asset`, Hollow Table's terrain is remapped
> toward the ground palette, large buildings carry a warm lantern light,
> veilstone glows cyan and the curse glows purple. Verified on screen
> (quick skirmish capture, fog off): open moss 0.24–0.27 luminance, the
> §3.5 floor holds. The first cut of the recipe did NOT — see §3.2.
>
> **Supersedes:**
> - the "magic hour" recipe recorded in the header of
>   `Assets/Scripts/World/DayNightCycle.cs` (warm sun, neutral ambient,
>   fog off) — it is the inverse of this document;
> - the building faction-colour rule 1 in
>   `BuildingFactionColorMarker.Apply` (`*roof*` → solid faction colour)
>   and the atlas-blue roofs it recolours — roofs are **dark slate** and
>   the player colour moves to **lantern/window glow and cloth** (§4);
> - the `Faction.Border` "icy cyan — veilstone aesthetic" comment in
>   `FactionColors.cs` as a description of the CURSE: cyan is
>   **veilstone the resource**, purple is **the curse** (§6.1).
>
> The locked culture palettes in
> [docs/Marketing/03_Art_Hiring_Brief.md](../Marketing/03_Art_Hiring_Brief.md)
> stand unchanged and are repeated in §3.3 so this file is complete on
> its own.

---

## 1. The target in one paragraph

A **stylized low-poly RTS at dusk**. Cool blue-teal ambient, one weak
cool key light, and every point of warmth on screen is *emitted* —
lantern-lit windows, torches, crystals, fissures — and blooms. The ground
is a dark, low-frequency, hand-painted moss with worn dirt paths and
stone paving around buildings; it stays **mid-value**, never black, so
units read on top of it at max zoom. The curse is the brightest thing on
the map: purple crystal growth on a cracked grey-violet crust with a
sickly green pool at each pocket and a crackling magenta boundary line.
Beyond the playable terrain the map falls away into a soft navy-purple
void, not a hard black circle. Same UI.

Reference comps, in order of weight: the dark-fantasy dusk comp this
document was written from; *Northgard* (building read, painterly ground);
*Battle Aces* (readability at zoom — the guardrail, §3.5).

## 2. Reference vs. current — the gap

| Layer | Current build | Target | Share of the look |
|---|---|---|---|
| **Atmosphere & grade** | warm sun `(1.0, 0.957, 0.878)` @ 1.5, bright neutral ambient, fog off, vignette off, saturation +10, bloom threshold 1.1 (nothing blooms) | cool weak sun, navy/teal ambient, blue-violet shadow split-tone, desaturated base, bloom on emissives, light vignette | ~40 % |
| **Buildings** | atlas roofs painted blue → recoloured solid faction colour; no emission maps; no local lights | slate roofs; faction colour on lantern/window glow + cloth; warm emissive windows; one warm point light per large building | ~25 % |
| **Ground** | four photoreal Substance layers (GroundSubstance002, GrassSubstance001/002, RockSubstance003); curse overlay hue-shifts the grass | hand-painted dark moss / dirt path / worn stone / cliff rock; curse = cracked crust with emissive cracks; emissive veil seams and ember cracks | ~20 % |
| **Fog of war, map edge, borders** | unexplored = pure black, re-sharpened edge (the hard circle); flat-tinted territory ribbon | navy-purple drifting smoke, soft edge; HDR crackling border line; cliffs into void | ~10 % |
| **Density** | bare ground; curse pockets are purple spheres; well is a gem cluster | crystal clusters, bone piles, dead trees, mist, glowing fungi — each one a readout (§6.3) | ~5 % per screen, but it is what makes it feel finished |

Every row already has a home in the codebase. This is a retune-and-retexture
pass, not a new system; the one new piece of shader work is §5.2.

## 3. Light & palette

### 3.1 The one-tint rule (why the last attempt failed)

The previous "blue-volcanic" tuning went near-black and was reverted
because it **stacked** a tinted sun × tinted bloom × colour filter ×
white balance × negative exposure. The lesson is not "don't go dark". It
is:

- **Colour temperature comes from exactly two places:** the Trilight
  ambient and the shadows split-tone. Sun colour is *slightly* cool, not
  blue. Bloom tint, colour filter and white balance stay neutral.
- **Brightness comes from emissives, not from exposure.** `postExposure`
  stays 0. If the image is too dark, raise the ambient or the ground
  albedo, never the exposure; if it is too bright, lower the sun.
- **Saturation is taken from the base, given back by the emissives.** The
  global grade desaturates a little; the warm windows and the purple
  crystals are HDR and untouched by it.

### 3.2 Starting recipe (tune in Play mode, then commit to the asset)

These are the `DayNightCycle` fields. Per the component-config rule they
move out of code into `DayNightCycle.asset` (§11, Pass 1, task 1).

| Field | Now | Target start | Note |
|---|---|---|---|
| `sunPitch` / `sunHeading` | 50 / −30 | **42 / −35** | lower = longer shadows, more form on buildings |
| `sunColor` | `#FFF4E0` | **`#C8D4E6`** | cool, NOT saturated blue. `#8FA6C8` @ 0.75 was the first draft: on screen it was simply dark — night, not dusk — and desaturating the base on top of it left nothing to pop |
| `sunIntensity` | 1.5 | **1.45** | the mood is in the COLOUR of the light, not its absence. Same strength as before, cool instead of warm |
| `ambientSkyColor` | `#B3BFCC` | **`#4A5F88`** | navy |
| `ambientEquatorColor` | `#808080` | **`#335161`** | teal-navy; this is the colour of "night" |
| `ambientGroundColor` | `#4D4738` | **`#1A1C2A`** | |
| `fogColor` / `fogDensity` | — / 0 | **`#141A2E` / 0.0015** | ExpSq at 0.0015 is 2 % at 120 m, 12 % at 300 m — a horizon fade only. 0.005 was the value that ate the map |
| `saturation` | +10 | **+8** | NOT negative. The first cut (−15) flattened the base so far that a dark faction roof read as brown; colour has to stay on the base too |
| `contrast` | +15 | **+12** | |
| `smhShadowsTint` | `(0.92, 0.96, 1.05)` | **`(0.90, 0.94, 1.12)`** | blue-violet shadows |
| `smhHighlightsTint` | `(1.05, 1.00, 0.92)` | **`(1.08, 1.04, 0.96)`** | keep lantern light warm |
| `bloomIntensity` / `bloomThreshold` | 0.6 / 1.1 | **1.4 / 0.8** | scatter 0.7 unchanged. Only HDR (> 1) surfaces cross 0.8 |
| `vignetteIntensity` / smoothness | 0 / 0.4 | **0.12 / 0.5** | was disabled for GPU cost; measure again before keeping it |
| `postExposure` | 0 | **0** | never used to set mood (§3.1) |
| `filmGrainIntensity` | 0 | **0** | stays off; per-pixel cost for nothing the reference has |
| Tonemapping | ACES | ACES | unchanged |
| `voidColor` (camera clear) | black | **`#05060C`** | the void beyond the map (§7.2). Set by `DayNightCycle`, which already owns the camera's post-process flag |

Shadows: soft, strength 0.8, distance 120 m, 2 cascades — unchanged.

### 3.3 Palettes

**Ground (neutral, every map):**

| Use | Hex | Note |
|---|---|---|
| Moss (base) | `#3F5A3A` | the resting value of the map; luminance ≈ 0.30 before grade |
| Moss dark (edges / under canopy) | `#2E4430` | |
| Dirt path | `#5A4A3A` | worn trails between buildings and to resources |
| Worn stone (paving near buildings) | `#6E6C68` | |
| Cliff rock | `#4A4F58` | cool grey; the map-edge material |

> **These hexes are RENDERED colours, not albedos.** Under the §3.2 light
> (irradiance ≈ 0.5 on flat ground) a texture must sit about one step
> brighter than its hex to render AT the hex — moss albedo ≈ `#557850` renders
> as `#3F5A3A`. The Hollow Table remap multipliers (Pass 1, task 2) were
> derived that way from each source texture's measured mean.

**Curse:** crust base `#4B3A63`, crack/crystal pole `#8E5CF0` (purple pole,
`_CurseTint`), pool/decay pole `#7FCB3A` (green pole, `_CurseTint2`).
**Veilstone (resource):** `#5FD8E8` cyan. The two never share a hue.

**Fire (from [Fire.md](Fire.md)):** ember crack `#FF6A2A`, ash `#2A2A2C`.

**Cultures** — unchanged from the art brief:

| Culture | Stone / material | Cloth / banners | Roof | Magic |
|---|---|---|---|---|
| Alanthor | warm limestone `#737068`, foundations `#555550` | sage `#8CA680` | **dark slate** | arcane-machine, runic, cool white-blue glow |
| Runai | sandstone / cyan; copper domes | deep indigo, warm gold | sandstone / copper | light-blue, flowing |
| Feraldis | dark grey `#3A3A3A`, weathered wood `#5C3A21`, obsidian veining | crimson `#9E2A2B` | dark, angular | blood-red, eldritch |

### 3.4 The emissive ladder

Emission is `EmissionMap × EmissionColor`, EmissionColor in HDR. Anything
below ~1.0 does not bloom at threshold 0.9; that is deliberate, so the
ladder is the only thing that glows:

| Surface | Colour | HDR multiplier | Blooms |
|---|---|---|---|
| Building windows | `#FFB05A` | × 2.5 | yes, soft |
| Faction lanterns / sconces | faction *glow* colour (§4.2) | × 3.0 | yes |
| Veilstone outcropping crystals | `#5FD8E8` | × 3.0 | yes |
| Curse crystals (well, pockets) | `#B14BFF` | × 4.0 | yes, strongest static glow |
| Curse pool | `#7FCB3A` | × 2.0 | faint |
| Veil seams (ground fissures near the curse) | `#3AD6FF` → `#8E5CF0` by proximity | × 3.0 | yes |
| Ember cracks (burning ground) | `#FF6A2A` | × 3.0 | yes, flickers |
| Territory border line, owned | owner glow colour | × 4.0 | yes |
| Territory border line, unclaimed | `#3A3F4A` | × 0 | no |
| Curse hold-radius ring | `#FF3AD6` | × 5.0 | the brightest line on the map |

Ability VFX keep their own authored intensities; they sit above this ladder.

The ladder's code home is `Assets/GameSystems/Rendering/Vfx/EmissiveLadder.cs`
(2026-09-18): every runtime visual that makes something glow reads its
colour there, so the rungs cannot drift apart across files.

### 3.5 Readability guardrails (checked before any pass is "done")

1. **Ground luminance.** A screenshot of open moss at default zoom, after
   grade, reads at ≥ 0.22 luminance (sRGB ~ `#3C3C3C`). Below that the
   map is night, not dusk, and units stop reading.
2. **Max zoom-out.** At the camera's far limit every unit is a lighter
   silhouette than the ground under it. If not, lift the ambient, not the
   exposure.
3. **All 12 swatches.** Every lobby colour is distinguishable on a lantern
   at default zoom (§4.2 solves Black / Brown / Maroon).
4. **Fog does not swallow the map.** At max zoom-out the far edge of the
   playable area is still visible through fog.

## 4. Where the player colour lives

### 4.1 The decision

Roofs are **dark slate** (`#3B3E46` Alanthor; the culture roof material
otherwise). The player colour is carried by:

1. **Lantern / sconce / window glow** — emissive, HDR × 3, blooms. This is
   the primary read at night and at zoom.
2. **Cloth** — banners, awnings, tents (Runai). Diffuse, atlas-swapped as
   today.
3. **Selection ring, health bar, minimap** — unchanged.

Units are untouched: authored materials, ownership via ring / bar /
minimap, as the current rule already says.

### 4.2 The glow colour

A lantern painted Black, Brown or Maroon is a dead lantern. The emissive
uses the swatch's **hue** at a floor of saturation and value:

```
glow = HSV(hue(swatch), max(sat(swatch), 0.55), max(val(swatch), 0.85))
Silver (hue-less) → #DCE6FF   Black → #9AA6C8 (cold white-blue)
```

`FactionColors.GetGlow(faction)` is the single place this lives; every
emissive consumer (lanterns, border line, minimap glow if ever) reads it.
Cloth keeps the plain swatch.

### 4.3 What changes in the rule

`BuildingFactionColorMarker.Apply` today: (1) `*roof*` node → solid
faction colour; (2) `*stripe*` → tint; (3) atlas pixels in hue 190–260°
with sat > 0.20 → faction hue, value preserved. Rule 3 is what recolours
the blue-painted roofs in the shared atlases (`Assets/GameData/Art/Atlases/`).

- Rule 1 becomes `*roof*` → **slate**. (No roof-named nodes were found in
  the current variant prefabs; the rule stays as a guard for authored
  ones.)
- Rule 3 is unchanged in logic and **extended to `_EmissionMap`**: the same
  hue-band swap runs on the emission texture, and `_EmissionColor` is set
  to white × the ladder multiplier. One rule, two textures.
- The atlas is **repainted** so the rule finds the right pixels: roof
  regions leave the 190–260° band (slate), lantern / sconce / cloth
  regions enter it (marker blue), and a new `Texture_01_Emission.png`
  carries warm windows (`#FFB05A`) and marker-blue lanterns.
- **Interim, until the atlas is repainted:** rule 3 multiplies the swapped
  roof pixels by 0.45 and desaturates 40 %, so roofs read as dark faction
  colour instead of the current saturated slab. Removed when the repaint
  lands (task 5); it exists so Pass 1 is shippable without 2D work.
  Shipped 2026-09-18 as `InterimSwapValue` / `InterimSwapSaturation`.

Culture variants (Lv1–Lv3 branches of the `*Variants.fbx` models)
reference all seven atlas materials; §12 Q1 is which one Age 0 actually
samples.

### 4.4 Local light

One warm point light per **large** building (Hall, Barracks, Archery
Range, Temple, culture halls): colour `#FFB05A`, range 14 m, intensity
4.5, 4 m above the pivot, **shadows off**, dark while under construction.
None on Huts, Gatherer's Huts or wall pieces — the rule is the shorter
footprint side ≥ 8 m (`BuildingLanternLight.asset`). The renderer is
Forward+ so the count is not a per-object limit, but the budget is a
design choice: light pools mark the *important* buildings. Placed in
WORLD units, not local: building roots carry an arbitrary authored scale.

## 5. Ground

### 5.1 Terrain layers

Each map's four Substance layers are replaced by four hand-painted,
low-frequency layers from the §3.3 ground palette — **Moss, MossDark,
DirtPath, Stone** — plus **CliffRock** on slopes. Tiling ~8 m; detail
comes from the splat *pattern* and from the **procedural road network**
([Roads.md](Roads.md)) — the map author paints no paths and no paving;
plazas and roads are generated from what is built and where. `Rock` and `NoWalk` currently point at the same Substance rock
and can share the new CliffRock.

Layers live in the map folder (`Assets/GameData/Scenes/Maps/<Map>/`) as
today; the root-level `Assets/*.terrainlayer` duplicates are removed once
no map references them. Hollow Table is the first map; the others follow
the same set.

### 5.2 Overlay channels (`TWBTerrainOverlays.hlsl`)

The overlay model — a 128² world-space mask sampled per pixel, edges eroded
through value noise — is kept exactly. What changes is what each channel
*draws*:

| Channel | Now | Target |
|---|---|---|
| `_TWB_CultureMask.A` (curse) | grass albedo hue-shifted to purple + sparkle | **crust**: `_CurseAlbedo` cracked grey-violet, `_CurseNormal` for the cracks, **new `_CurseEmission`** mask (cracks only) × `_CurseTint` HDR. Green pole (`_CurseTint2`) drives the pool ring at pocket centres. Sparkle kept at the rim only |
| `_TWB_CultureMask.R` (Alanthor) | white slate bricks | unchanged texture, tint down to `#737068` limestone |
| `_TWB_CultureMask.G/.B` (Feraldis / Runai) | placeholder tints | Feraldis: ash + ember cracks via the Fire states; Runai: sand `#B59A6B` drift. Both are Pass 2+ and gated on those cultures' art |
| `_TWB_BloodMask.R` (blood) | spatters + puddles | unchanged |
| `_TWB_BloodMask.G` (region boundary) | darkening line, baked once | unchanged (the in-world border LINE is §8) |
| **`_TWB_BloodMask.B` — new: veil seams** | — | baked once at load like the boundary channel: a distance field from every well and veilstone outcropping, eroded by the same noise into branching fissures. Emissive `#3AD6FF` near outcroppings shading to `#8E5CF0` near wells, × 3. Static; costs one channel of a texture already sampled |

Ember cracks are **not** a new channel: they are the Burning ground state
from [Fire.md](Fire.md) and render through whatever that state's pass
becomes. Ash floor is that document's Ash state.

### 5.3 Wet & sparkle

Smoothness stays low everywhere (hand-painted look) except blood
(`_BloodSmoothness`, unchanged) and the curse crust cracks, which get
smoothness 0.6 so the emissive reads as glassy.

## 6. Curse & veilstone objects

### 6.1 Colour language

**Purple is the curse. Cyan is veilstone.** They are never mixed on one
object. The well is a *purple* crystal cluster growing over a *cyan* seam;
an outcropping is *cyan* only; a pocket is *purple* buds around a *green*
pool. `Faction.Border` in `FactionColors` keeps its cyan for the minimap /
UI (where it denotes veilstone territory), and the in-world curse reads
purple from the terrain and crystals — "purple ground IS the scoreboard"
([Curse_And_Shardroot.md](Curse_And_Shardroot.md)).

### 6.2 Objects

| Object | Now | Target | Assets on hand |
|---|---|---|---|
| Well (LargeNode) | authored gem-cluster prefab, procedural spire fallback | purple crystal cluster (3–5 spires, HDR × 4) on a crust disc, mist at the base, ring of bone / dead growth | `Assets/MISC/SineVFX/TranslucentCrystals`, `Assets/Shaders/Crystal.shader`, Hovl "Magic effects pack" for mist |
| Pocket (SmallNode) | purple spheres | 2–3 crystal buds + a green pool decal (`GroundDecals`, `#7FCB3A` × 2) + a thin mist puff | same |
| Veilstone outcropping | cyan gem cluster | unchanged shape, cyan HDR × 3, cyan veil seam radiating from it (§5.2) | existing prefab cache in `ResourceNodes/VeilstoneOutcropping/` |
| Hold-radius ring (the "river") | — | see §8.2 | |
| Bone piles | — | scattered inside crust at pockets and wells | Synty FK `SM_Deadbody_Pile_01/02`, `SM_Dead_Guard_*` |

### 6.3 Every decoration is a readout

The reference is a painting and its decorations are free. In the game
each one must carry information, or it is noise at zoom:

| Element in the reference | What it means here | Source of truth |
|---|---|---|
| Purple cracked crust | Blighted territory | `_TWB_CultureMask.A`, Regions.md |
| Purple crystals | a well or a pocket, and its held state | Curse_And_Shardroot.md |
| Green pool | a pocket's centre | Curse_And_Shardroot.md |
| Cyan crystals + cyan seams | veilstone, minable | ResourceNodes |
| Orange ember cracks | burning / burnt ground | Fire.md |
| Magenta boundary river | the well's hold radius while a verb is in progress | Curse_And_Shardroot.md |
| Aurora veil along a border | this ground is the curse's — its edge, in place of a line | §6.4 |
| Bones | inside curse crust only | §6.2 |
| Dead twisted trees | Blighted nature region | Territory_And_Nature.md §2 |
| Glowing fungi | Wild nature region floor at night | Territory_And_Nature.md |
| Blue lanterns on buildings | the owning player | §4 |
| Warm windows | any complete building (dark = under construction / abandoned) | §4.3 |
| Earthen trails and plazas | sites joined within one territory; someone builds or walks here | Roads.md |
| Stone roads and paved plazas | an Age 1 culture holds this ground and the buildings are finished | Roads.md §5 |

### 6.4 The curse veil (2026-09-18)

**The edge of cursed ground is not a line — it is a standing veil.** Every
territory the curse holds gets, along the same traced boundary curve the
decal renderer uses, an **aurora rising from the ground**: a translucent
double arch (feet on both sides of the boundary, peak over it, so it is
never edge-on to the RTS camera) whose brightness drifts along the line in
slow curtains, with vertical rays streaming upward and bursts that leave
the feet and climb, in emissive dark cyan and purple; wisps of the same two
colours drift up off it. Player and unclaimed borders keep their decal
lines. **Cursed territories draw no purple line at all** — the veil is the
border.

This is the one place cyan and purple meet on one object: the veil is
where veilstone and the curse touch. §6.1 stands everywhere else.

Built from `TerritoryBorderCurves`' loops when the curse takes a
territory, torn down when it loses one (`CurseBarrierVfx`, config in
`CurseBarrierVfx.asset`; shaders `TWB/CurseBarrier` and `TWB/CurseWisp`,
materials in `Resources/CurseBarrier/`). Textures are the purchased packs'
(UNI noise, Hovl smoke); their materials and shaders could not be reused —
Hovl's are built-in-pipeline, UNI's are VFX Graph beams.

Tuned live with the developer on 2026-09-18: first cut too thick and too
bright; now arch half-width 1.1 m, height 3.2 m, wisp intensity 0.9, body
0.06, and "getting better" — expect more passes.

## 7. Fog of war & the void

### 7.1 Fog of war

`FogOfWarShader.shader` keeps its two-channel visible/revealed model. The
look changes are parameters plus one extra sample:

- `_Tint` black → **`#0B0E1C`** navy-purple.
- `_HiddenA` **stays 1.0.** Unexplored ground is fully opaque by design (the
  shape of the map must not be readable before scouting). The void colour
  is what makes it read as mist instead of a hole.
- `_Softness` 1 → **2**, `_ExploredA` 0.65 → **0.55**.
- **Drifting noise**: multiply the alpha by a scrolling low-frequency
  cloud texture (0.7–1.0 range) so the boundary breathes. One extra
  texture sample; the projector cloud texture `DayNightCycle` already
  generates at runtime is the obvious source.

### 7.2 The void beyond the map

Camera clear colour `#05060C`; no skybox. Each map's terrain falls off at
its edge into CliffRock at slope, then to the clear colour — authored per
map, not a system. Fog (§3.2) does the rest.

## 8. Territory borders

### 8.1 Owned / unclaimed lines

`TerritoryBorderCurves` stays the renderer (vector curve → AA outline
texture → `GroundDecals` projector). Changes: the owned tint becomes the
owner's **glow colour × 4 (HDR)** so the line blooms; a scrolling crackle
texture (thin bright filaments along the curve, 0.5 m period) is
multiplied in for owned lines only. Unclaimed lines stay the thin dark
grey, non-emissive.

### 8.2 The curse hold ring

While a verb (destroy / pacify / purify) runs on a well, its hold radius
draws a `#FF3AD6` × 5 ring with the same crackle, plus sparks
(`ProceduralBorderParticleGenerator` already owns curse particles). It is
the brightest line on the map because it is the clock the whole match
runs on. Removed when the hold ends; the territory line takes over.

## 9. Density & props

Follows the author-once substitution model in
[Territory_And_Nature.md](Territory_And_Nature.md) §8: the map is built
mint (Wild) and each nature region's owner swaps prototypes.

- **Wild:** dark, sparse temperate trees; a few `#7FCB3A`-glowing fungi
  (× 1.5, faint) at the floor.
- **Blighted:** dead twisted trunks, crystal buds at the roots, bone piles.
- **Cultivated / Stilled / Ashen:** per that document; art gated on the
  culture passes.

Rocks, stumps and fallen logs are Synty FK. **Dead trees are the one asset
gap** — FK has none (§12 Q3).

## 10. Performance guardrails

- Bloom, vignette and the extra fog sample are full-screen; the 2026-08-31
  GPU pass is the baseline to measure against (vignette and grain were
  turned off there for cost, and grain stays off).
- Point lights: Forward+, shadows off, large buildings only (§4.4).
- No new terrain layers beyond the five; the overlays are shader channels
  on masks already sampled.
- Emissive crystals are static meshes with an HDR material — no per-frame
  material writes. Flicker (embers) is a shader time term, not a script.

## Stylised look (2026-09-21) — the post pass

> **This supersedes the grade numbers earlier in this document**, and moves
> the target from filmic dusk toward the readable, saturated look of DOTA 2
> and League of Legends. The PALETTE is unchanged — purple is still curse,
> cyan still veilstone, the sky is still dusk. What changed is how it is lit
> and graded: high-key instead of moody, colour-true instead of filmic.

**Post-processing is about a fifth of this look, and it is worth saying so.**
DOTA 2's is a character shader — a diffuse warp ramp, a Fresnel rim, specular
exponent masks. League paints its lighting into the albedo. Neither uses
outline post-effects. The grade below gets the frame out of the way; the rest
is shading and texture authoring (§ What the grade cannot do).

### What changed

| | Was | Now | Why |
|---|---|---|---|
| Tonemapping | ACES, hard-coded | **Neutral**, from `DayNightCycle.asset` | ACES is a filmic curve for photographic realism and desaturates bright colour toward white. Saturation pushed below it was being eaten in the highlights |
| Colour LUT | none | `TWB_Stylised.png` at 0.85 | one bake carries the shadow lift, the hue shifts and the saturation; swapping the texture A/Bs a whole look |
| Shadow strength | 0.80 | **0.55** | a cartoon look keeps shadows light and coloured, never holes |
| Ambient sky / equator / ground | 0.29/0.37/0.53, 0.20/0.32/0.38, 0.10/0.11/0.17 | 0.42/0.50/0.63, 0.34/0.42/0.46, **0.24/0.25/0.29** | the ground bounce was near black. High ambient is what stops unlit faces going dead |
| Fog | navy 0.078/0.102/0.18 @ 0.0015 | lighter 0.16/0.20/0.30 @ **0.0006** | the playfield reads clear; the mist survives as depth, not as a veil |
| Bloom | threshold 0.80, scatter 0.70 | **0.55 / 0.82** | catches mid-bright emissives and spreads soft, the way stylised VFX glow |
| Saturation / contrast | +8 / +12 | **+22 / +8** | saturation can go further once ACES is not eating it; contrast comes down because the LUT supplies the S-curve |
| MSAA (PC) | off | **4x** | flat high-contrast shading shows crawling edges badly, and crawl is deeply anti-cartoon |
| Material reflectance | uncapped | **smoothness <= 0.30, metallic <= 0.30** | specular and metal response are what the eye reads as photographic |

Unchanged and deliberately so: SSAO stays on (contact shadows ground units in
a top-down view), film grain stays at 0, depth of field and motion blur stay
off. Grain and defocus are realism cues.

### The LUT

`Assets/GameData/Art/LUTs/TWB_Stylised.png` — a 1024 x 32 unrolled strip, 32
slices of 32 x 32. It lifts black to a cool ~0.06, tints shadows cool and
highlights warm, and raises saturation most in the mid-tones where readable
colour lives, easing off at both ends so brights do not clip to neon.

`TWB_Neutral.png` sits beside it: the identity strip, to be opened in an image
editor, graded with adjustment layers and saved as a new look. That is the
intended way to iterate — not by editing the generator.

A lookup texture is DATA: **no mipmaps, no compression, clamp wrap, bilinear**.
Mips average unrelated colour slices together and compression becomes banding.
The import settings are in its `.meta` and must not be "fixed".

### Material reflectance

The ceiling is enforced in two places, both of which are the single place for
their half:

- `ProceduralMaterialHelper.MaxSmoothness` / `MaxMetallic` clamp every
  procedural visual, so the ~200 call sites keep saying what they mean
  ("iron is shinier than timber") and simply top out lower.
- Authored `.mat` assets under `GameData/`, `GameSystems/`, `Shaders/` and
  `Resources/` were clamped in place — 51 of 96.

**Water is exempt.** It is a specular plane rather than a painted surface, and
flattening it makes a river read as poured concrete. Third-party packs
(Synty, Tools, MISC) are untouched.

### What the grade cannot do

The remaining four fifths, in order of impact:

1. **Rim / Fresnel light on units.** The single biggest "DOTA feel" change and
   the one that matters most at RTS zoom, where silhouette separation is
   readability. Needs a shader, not a volume.
2. **A diffuse ramp** — lighting quantised through an artist-painted gradient
   instead of smooth PBR falloff. Also a shader.
3. **Albedo authoring.** Mid-value, saturated, low-frequency, with lighting
   hinted into the texture and hue-shifted shadows. Noise and grain read as
   realism; big painted value shapes read as stylised.
4. **Palette discipline** — strong hue separation between terrain, factions
   and VFX, so nothing has to rely on brightness alone to read.

### Experiment: pencil ink outlines (2026-09-25) — NOT adopted

A Borderlands-style ink pass is being **prototyped for an A/B**, against the
statement above that the DOTA / League target uses no outline post-effect.
Until this section says otherwise the DOTA direction stands: the asset is
checked in with the pass ON so it can be looked at, and it must be switched
**off** before any release build unless it has been adopted.

- **What it is:** one full-screen pass after opaques and before transparents.
  It finds edges in the depth + normals buffers and draws them as dark
  pencil strokes: widths wobble and pressure breaks up, the lines "boil" at a
  low frame rate, and they thin and fade with camera distance so the RTS zoom
  does not turn to scribble. Grass (no depth-normals pass), water, VFX and fog
  of war (Overlay queue, drawn over the lines) are left un-inked by
  construction.
- **Cost:** requesting normals makes URP run a depth-normals prepass that the
  PC renderer does not run today (SSAO is off on it). Measure before adopting.
- **Where:** `Assets/GameSystems/Rendering/PencilOutline/`. Every value is in
  `PencilOutline.asset`, and `enabled` there is the A/B switch; it can be
  edited live in Play mode.
- **To adopt:** capture Hollow Table at default and max zoom (§3.5) with the
  pass on and off, then either rewrite the opening paragraph of this
  "Stylised look" section and delete this subsection, or delete the folder.

## 11. Implementation plan

Each pass ends with the §3.5 screenshots taken on Hollow Table at default
and max zoom and attached to its task.

### Pass 1 — atmosphere, buildings, fog (code + data; one 2D task)

| # | Task | Files | Done when |
|---|---|---|---|
| 1 | **DONE 2026-09-18.** `DayNightCycle` values → `DayNightCycleConfig` + `DayNightCycle.asset` beside the class (the `CameraController` pattern); no field initialisers; retuned to §3.2; header rewritten; catalog row added (run `Waning Border > Component Config > Rebuild Catalog` once to confirm the naming check passes) | `Assets/Scripts/World/DayNightCycle.cs`, `DayNightCycleConfig.cs`, `DayNightCycle.asset` | grade matches §3.2; guardrails 1, 2, 4 pass **(not yet checked on screen)** |
| 2 | **DONE 2026-09-18 — a STOPGAP until Pass 2 task 8.** Hollow Table's five Substance layers keep their textures but get a per-channel `m_DiffuseRemapMax` (the terrain shader honours it) derived from each texture's measured mean so it lands on the §3.3 albedo: Grass → `#557850`, Meadow → `#446446`, Dirt → `#6E5C4A`, Rock / NoWalk → `#606670`. Photoreal frequency stays; the colour lands | `Assets/GameData/Scenes/Maps/Hollow Table/*.terrainlayer` | open moss reads ≥ 0.22 luminance after grade **(not yet checked on screen)** |
| 3 | `FactionColors.GetGlow` (§4.2) | `Assets/Scripts/Core/Settings/FactionColors.cs` | 12 swatches → 12 distinct glow colours |
| 4 | Roof rule. **Interim half DONE 2026-09-18:** rule 1 → slate (`RoofSlate`), and the atlas swap darkens every pixel it recolours (`InterimSwapValue` 0.65, saturation kept — a 0.6 saturation cut made Blue's trim read as brown). **Finding from the capture:** the Hall's roof BODY is already authored dark-neutral in the atlas; only the trim/rim is marker-blue. So the atlas repaint (task 5) is smaller than planned — lanterns/cloth into the band and the emission mask, no roof work. **Still to do:** extend rule 3 to `_EmissionMap` + `_EmissionColor` × ladder, then delete the interim knobs | `Assets/GameSystems/Rendering/Buildings/BuildingFactionColorMarker.cs` | no saturated roof slab on any building; upgrade / damage / variant paths re-apply correctly (they all funnel through `Apply`) |
| 5 | Atlas repaint + emission mask (the one 2D task): roof → slate, lanterns / cloth → marker blue, `Texture_01_Emission.png`; assign on the atlas material(s) Age 0 samples | `Assets/GameData/Art/Atlases/` | windows warm, lanterns faction glow, roofs slate; interim multiplier removed |
| 6 | **DONE 2026-09-18.** Warm point light on large buildings (§4.4) — attached at spawn on both visual paths rather than authored per prefab, so every current and future large building gets one from a single rule | `Assets/GameSystems/Rendering/Buildings/BuildingLanternLight.cs` + `Config.cs` + `.asset`; `PresentationSpawnSystem.cs` (two `Attach` calls) | pool visible on the Hall's front wall and steps in the capture; no shadow cost |
| 7 | Fog of war tint / softness / drift (§7.1); camera clear colour (§7.2) | `Assets/Scripts/World/FogOfWar/FogOfWarShader.shader`, `FogOfWarManager.cs`, `CameraController.cs` | no hard black circle; unexplored still opaque |

### Pass 2 — ground

| # | Task | Files |
|---|---|---|
| 8 | Five hand-painted terrain layers for Hollow Table; repaint splats (paths, paving) | `Assets/GameData/Scenes/Maps/Hollow Table/*.terrainlayer`, terrain data |
| 9 | Curse crust: `_CurseAlbedo` / `_CurseNormal` textures, new `_CurseEmission`, pool ring from the green pole | `Assets/Shaders/Terrain/TWBTerrainOverlays.hlsl`, `Assets/Scripts/Influence/InfluenceMaskTexture.cs` (property binding) |
| 10 | Veil seams channel (`_TWB_BloodMask.B`), baked once at load from wells + outcroppings | same shader; bake beside the region-boundary bake in `RegionMap` / `InfluenceMaskTexture` |
| 11 | Alanthor overlay tint to limestone | shader material values |

### Pass 3 — density

| # | Task | Files |
|---|---|---|
| 12 | Well and pocket visuals per §6.2 (crystal clusters, green pool decal, mist). **Colour half DONE 2026-09-18:** both are the cyan gem prefab hue-shifted a quarter turn to purple with the Curse rung of `EmissiveLadder` (they were cyan — a §6.1 violation). Shapes, pool and mist still to do | `Assets/GameData/TechTree/Border/LargeNode/PresentationSpawnSystem.LargeNode.cs`, `Border/SmallNode/`, `Assets/GameSystems/Rendering/Border/` |
| 13 | **DONE 2026-09-18.** Veilstone outcropping HDR cyan (`EmissiveLadder.Veilstone`) | `Assets/GameData/TechTree/ResourceNodes/VeilstoneOutcropping/` |
| 14 | Wild / Blighted prototype sets + fungi + bones via nature regions | `Assets/Scripts/World/MapMarkers/NatureRegion*.cs`, map scenes |

### Pass 4 — lines and edges

| # | Task | Files |
|---|---|---|
| 15 | HDR owned border line + crackle scroll | `Assets/GameSystems/Rendering/Border/TerritoryBorderCurves.cs`, `Vfx/GroundDecals.cs` |
| 16 | Curse hold ring (§8.2) | `Rendering/Border/`, `TechTree/Border/LargeNode/` verb systems for the hold state |
| 17 | Map-edge falloff on every shipped map | map scenes; `MapSceneSync` ship gate unchanged |

## 12. Open questions

1. **Which atlas does Age 0 sample?** The `*Variants.fbx` models reference
   all seven materials in `Assets/GameData/Art/Atlases/`; the repaint (task
   5) should start with the one the Lv0 meshes actually use and the culture
   variants follow with their culture passes.
2. **Menus.** The main-menu scene is a separate authored Synty skin with its
   own camera and never runs `DayNightCycle`. Does it adopt the grade
   (likely yes, for continuity into the match), and if so as its own
   volume?
3. **Dead-tree asset source** for Blighted regions (Synty FK has none).
4. **Runai at dusk.** Sandstone and copper under a navy ambient — needs a
   comp before Runai's overlay and buildings are tuned, so its warmth does
   not read as another faction's orange.
5. **One preset or per-map?** `DayNightCycle` is a single static preset by
   design (no cycle). This document assumes one global look. A per-map
   override (e.g. a colder northern map) would be a second config asset and
   a `MapInfo` field — not planned, noted so it is not done ad hoc.
