# Regions

> **Doc version: 2026-08-27 — THIRD PASS. This supersedes the whole of the
> second pass.** The map model is now Nature ring + Territories, and a territory
> is claimed by BUILDING something in it rather than by dominating it on the
> influence map. Sections marked **(INCOMPLETE)** are waiting on the rest of the
> user's direction and must not be implemented from guesswork.
>
> Marked **(new — not yet in code)** throughout. §7 is the exception:
> it describes shipped rendering that survives the redesign.

---

## 1. Map structure

A map is **512 x 512 m** and has exactly two kinds of ground.
The authored reference map is **6 players, ~25 territories** (§5).

### The Nature ring

A band of **Nature** around the whole map edge. It exists to give the map a
smooth, natural-looking border instead of a hard rectangular cut.

| Property | Value |
|---|---|
| Walkable | **No** |
| Claimable | **No** |
| Takes culture look | **No** |
| Takes curse look | **No** |
| Fog | **Explored from the start, never *revealed*** — permanently in the remembered/dimmed state, never in live vision |

The fog rule is the unusual one and is deliberate: the ring is scenery. You can
see it is there, so the map reads as a whole and its edge does not look like a
cliff someone cut with a ruler, but no vision is ever granted there because
nothing can ever happen there.

### Territories

Everything inside the ring is divided into **Territories** — the claimable
regions, each with a border.

Inside and between territories there are two kinds of impassable terrain, and
they are NOT the same thing:

| | Impassable | Claimable | Takes culture look | Produces |
|---|---|---|---|---|
| **Mountains / cliffs** | Yes | **No** | **No** | nothing |
| **Forests** | Yes | **Yes** (with their territory) | **Yes** | **Supplies** |

Mountains and cliffs are pure structure — scenery that divides territories and
belongs to nobody, ever. Forests are the opposite: impassable, but they are part
of their territory. Claim the territory and its forests take on your culture's
decorations and start producing supplies.

---

## 2. Claiming a territory

**Claiming is an ACTION, not a threshold.** This reverses the second pass, which
had a territory flip to whoever dominated it on the influence map.

**You claim a territory by building a HALL in it. One rule, every culture**
(2026-08-28). The per-culture claim structures — an Alanthor fortification, a
Runai trade post, a Feraldis totem — are retired: they were three names for one
mechanic, they arrived only at age-up, and they made "can I build here" a
question with a different answer per culture.

A Hall is the ONLY building that may be raised on ground you do not hold.
Everything else, towers included, goes inside territory that is already yours.

**A Hall is expensive on purpose** — 600 supplies and 200 iron, against 350/100
before. Taking ground is the largest single purchase in the game, because it is
the only purchase that grows the economy.

**One Hall per territory.** A second claims nothing, so there is no reason to
build it. This replaces the old flat six-per-faction cap: the limit on how wide
you spread is how much ground you can hold, not a number.

**Claiming starts in Age 0.** The Hall is an Age 0 building, so expansion is
open from the first minute — this supersedes the earlier "no claiming before
age-up", which existed only because every claim structure was Age 1.

### Losing and taking

- **A claim decays back to Natural when its structure dies.** Not instantly
  transferred, not held forever: the territory returns to unowned and is open to
  anyone. Killing the structure does not hand the ground to the killer, it
  un-paints it.
- **You cannot claim over a live claim.** The existing structure must be
  destroyed first. So taking ground is always two acts — break, then build — and
  there is a window in between where the territory belongs to nobody and either
  side can take it.

Together these make territory a *maintained* thing rather than a scoreboard: a
front line is a row of structures somebody has to keep alive.

---

## 3. The curse takes territory by force

The curse does not claim with influence and does not build. It **attacks**.

- **A Node sits at the CENTRE of some territories.** Node placement is authored,
  not derived — the map decides which ground is cursed at the start.
- **A Node sends waves at neighbouring territories.**
- **If only curse units are left alive in an attacked territory, the curse
  claims it** — and the wave moves on to the next.

This is the mechanic that makes the map an active opponent rather than a
backdrop. It also means the curse's spread is *legible*: you can see the wave
coming, you know which territory is next, and holding a border territory is a
military commitment rather than a bookkeeping one.

### (INCOMPLETE) — needs direction

- **Wave cadence and strength.** How often, how many, and does it escalate with
  match time or with the number of territories the curse holds?
- **"Only curse units survive"** — does a surviving BUILDING (a fortification,
  a hut) prevent the claim, or is it strictly units on the ground?
- **Can a player take a cursed territory back**, and does that require killing
  the Node or only clearing the units and building a claim structure?
- **Does a claimed territory keep spawning waves**, or does the Node's own
  territory have to be cursed for it to be active?

---

## 4. Economy (Alanthor) — the territory turn

Territory, not workers, is the engine.

### What each resource is for (2026-08-28)

Measured across every cost in the tree, the economy was inverted: **Supplies
were 62.6% of everything the game sells, Iron 24.2%, Veilstone 11.0% and
Veilsteel 2.2% — and Veilsteel appeared in 0% of buildings.** Supplies were
also the one resource that arrived *unconditionally*, on every territory,
whether it held anything or not.

So the resources that could drive conflict (iron, veilstone, veilsteel — they
only exist where a node is) were the ones nothing asked for, and the resource
everything asked for was the one you got just for owning any ground. Territory
COUNT mattered; territory CONTENTS did not.

| Resource | Role | Where it comes from |
|---|---|---|
| **Supplies** | The bread and butter — basic infantry, workers, opening buildings | Every territory, unconditionally (lowered to **52/min**) |
| **Iron** | Weapons and armour: military units, military buildings, weapon techs | Iron deposits only |
| **Veilstone** | The heavy line — **cavalry, siege and the religious tier cannot be built without it** — plus Age-1 buildings and techs | Veilstone outcroppings only |
| **Veilsteel** | The apex: elite units and the top rung of each tech ladder | Veilsteel deposits, ~1 territory in 3 |

**Requirements, not contributions.** A resource that is 5% of a price is a
rounding error; one that *gates* a unit is a reason to take ground. Veilstone
and veilsteel are floors on the heavy and elite lines — 59% of units now need
veilstone and **38% need veilsteel**, up from 55% and 9%.

**Line infantry is deliberately exempt.** The Spearman, Swordsman, Sentinel and
Nobleman are tagged `Heavy` but they are the backbone of every army; gating
them on veilstone locks a faction out of the game rather than giving it
something to fight for. The same reasoning keeps veilsteel out of *base*
building costs — it reaches buildings through the upgrade ladders instead.

**Income was rebalanced with it**: the unconditional supply base dropped
72 → 52/min and node yield rose 75 → 95/min. Ore is now **65% of a faction's
income** (was 51%), and a territory with a node is worth **2.83x** a bare one
(was 2.04x). That difference is the whole reason to contest a particular
region.

Everything below is authored **per minute**, because that is the unit the player
is shown (see *Reading a territory* at the end of this section).

- **A territory produces a supply tick** on its own.
- **A territory containing a resource node produces a trickle of that
  resource — 95/min per node**, whether or not anything is built on it. Holding
  the ground is what pays; the node is the reason the ground is worth holding.
- **Every resource has its OWN extraction building, and it stands ON the node.**
  One per node — the node count is what limits how many a territory supports,
  which is the whole reason nodes replaced area-based caps.

  | Resource | Building | Node |
  |---|---|---|
  | Supplies | Gatherer's Hut | Supply site |
  | Iron | Mine | Iron deposit |
  | Veilstone | **Veilstone Mine** | Veilstone outcropping |
  | Veilsteel | **Smelter** (Alanthor) | Veilsteel deposit |

  Before this, one generic Mine counted toward *any* node within 12 m, so a
  single building extracted all three ores and there was no decision about
  what to invest in. Veilsteel had no building at all.

- **An extraction building adds 25/min per level.** All four are upgradeable,
  and the ladders for the two mines are priced in **veilstone and veilsteel** —
  the currencies a territory-holding faction accumulates and previously could
  not spend.

- **NODES DEPLETE.** A fresh node holds ~4,000 units; yield scales with what is
  left, down to a **25% floor** so a spent node still trickles rather than
  turning its territory into dead ground nobody contests.

  At the base 95/min trickle that is roughly 42 minutes of undisturbed
  extraction — about **53% yield by minute 25**, and sooner where a building is
  drawing on it. Extraction draws faster than the bare trickle, so **upgrading
  is a decision to spend the seam sooner**: it raises income now and shortens
  the node's life.

  This is what stops the opening land grab from being the entire economy. A
  node that never runs down fixes income the moment the map is divided and
  removes any reason to take more ground or invest in what you hold; it also
  let banks run away (one logged AI reached 15,242 unspent veilstone).

- **Veilsteel is scarce: about one territory in three carries a deposit**, and
  no territory carries two. Authored markers are honoured first and only the
  shortfall is seeded, so a hand-built map keeps its composition; the seeding
  is deterministic (regions walked in index order, strided) so lockstep peers
  agree. Scarcity is what makes veilsteel ground worth taking — before this it
  was a single node on the whole map, which is an objective, not an economy.
- **A Gatherer's Hut adds 50/min of supplies** — and a hut may only be built
  **on a supply node**.

### Nodes, not patches (2026-08-28)

Iron and veilstone used to be *patches*: a scatter of thirty small deposits
around a marker. They are now **one large node per site**. A patch was a
mining-era shape — many small things so many workers had somewhere to stand —
and nothing stands on them any more. One node is also something a player can
point at, contest, and build a mine on top of.

**Supply nodes are new, and they exist to be a placement rule.** They pay
nothing themselves. What they do is decide WHERE a Gatherer's Hut can go, and
that single rule replaces the two crutches the hut needed before it:

- the **per-territory hut cap** — the count is now however many supply nodes the
  territory has, which is map data rather than a magic number, and it can differ
  between a rich territory and a poor one;
- the **gather-area rule** — there is nothing to overlap, so no coverage
  percentage, no first-come-first-served split, and no reason to inspect the
  ground before placing.

### Reading a territory

**Every Hall states what its territory yields, per minute, by resource.** A
player choosing where to expand is choosing between numbers, so the numbers have
to be visible without arithmetic: the Hall that claims the ground is where they
are shown. Nothing in the economy is allowed to be a figure the player can only
infer by watching their bank.

### One unit: the Worker

**The Miner and the Builder are gone. There is a single Worker unit, and it only
builds.** Workers are much more expensive than the units they replace.

This is the largest change in the document. Gathering as an activity no longer
exists: income comes from ground you hold and the buildings you put on it, and
the only thing a worker does is put them there. A player's economy is therefore
a map position, not a worker count.

### Implemented (2026-08-27)

| Piece | Where |
|---|---|
| Territory ownership (claim / decay / cannot-claim-over) | `World/Regions/TerritoryOwnership.cs` |
| Territory tick — supplies, hut boost, forests, iron/veilstone trickle | `Systems/World/TerritoryIncomeSystem.cs` |
| Curse escalation, re-triggered on territory tenure | `TechTree/Border/SmallNode/TerritoryCorruptionSystem.cs` |
| Worker gathering | **deleted** — 4 systems, 2 commands, the AI allocator, the input paths |

Rates are placeholders, all `const` at the top of `TerritoryIncomeSystem`:
6 supplies per territory per 5 s, +4 per hut (max 3), +5 per forest, 2 iron and
1 veilstone for a deposit-bearing territory.

**The hut limit is a HARD placement cap, not an income ceiling** (2026-08-28).
`TerritoryOwnership.MaxGathererHutsPerTerritory` is the single number, read by
both the placement gate and the income pass, so the count that is refused and
the count that is paid for cannot drift apart. A hut also earns nothing on its
own any more: the BFME2-style area model — a gather circle, a coverage
percentage, first-come-first-served splitting of overlaps — is deleted, along
with the placement circle and the yield readout that displayed it. Where a hut
sits inside its territory no longer matters, only which territory it is in. Worker: 50 -> **140** supplies,
25 -> **32 s** train time.

**The Hall claims its own territory.** Every culture claim structure is Age 1,
so ownership derived purely from them would pay NOTHING for the whole of Age 0 —
no territory bonus, no economy at all, in the exact age the Gatherer's Hut
belongs to. §2's "you begin holding the region your start sits in" is what fixes
it, and the Hall is what marks that ground.

**The curse escalation had to be rescued before the deletion.** The old
"drain the last bud of a patch, wake a blight pocket" trigger lived inside
`VeilstoneMiningSystem` and was the ONLY in-match producer of
`PendingCorruption`. Deleting worker gathering would have taken the entire
mid-game curse loop with it — pocket, purple telegraph ping and announcement —
and produced no compile error at all. It now triggers on TENURE instead of
depletion: holding a veilstone territory that is not your home ground wakes its
pocket after 120 s. That keeps the original intent intact — "a curse players
CHOOSE to create", home immune, the risk out on the contested ground you had to
leave home for.

**Lockstep ordinals 6 (Gather) and 23 (GatherVeil) are RETIRED, not reused** —
same convention as the retired `ScenarioType` indices. A future command taking
either would be silently mistaken for a gather order by a peer on an old build.

### (INCOMPLETE) — needs direction

- **Runai and Feraldis economies.** Deliberately deferred — the pass is Alanthor
  only.
- ~~The Sawyer and the mine do not exist.~~ **Both exist now (2026-08-28).**

  **Mine** — `Mine`, 220 supplies, 8x8. It already existed as a Feraldis
  building doing exactly what §4 describes ("workerless ore extraction; works
  every iron and veilstone node in range with no workers at all"), so it was
  made **universal** rather than duplicated per culture: §4's trickle-plus-mine
  rule is for everybody, not a Feraldis perk. Two fixes went with that — its
  `minEra` was **1 (Age 0)** despite CLAUDE.md calling it an Age 1 building, now
  **2**; and the Feraldis culture gate in `EntityExtractors.GetRequiredCulture`
  is removed. It keeps its unprefixed id for the reasons CLAUDE.md gives.

  It does **not** double-count with the territory trickle: the Mine touches no
  node state and pays 0.25 iron/s and 0.15 veilstone/s *per worked node* within
  18 m, on top of the territory's flat 2 iron / 1 veilstone per tick. Trickle is
  the passive baseline, the Mine is the investment — which is the rule as
  written.

  **Sawyer** — `Alanthor_Sawyer`, 150 supplies + 40 iron, 4x4, Age 1
  (`minEra: 2`). Earns nothing itself: it **doubles the forest supply of the
  territory it stands in**, capped at one per territory (a second would stack a
  pure multiplier with no counterplay, and the interesting decision is *which*
  forested territory to invest in). Placement is gated to within 14 m of a
  forest's edge, mirroring the Mine's patch gate — an ungated Sawyer would be
  placeable anywhere and earn nothing, which reads as broken rather than as a
  rule.

- **Every home territory is guaranteed iron AND veilstone.** Under §2 an Age 0
  player is confined to their start, and under §4 income is the territory tick,
  so a home without iron is not a weak start — it is a player who can never
  build anything costing iron, for the whole first age, with no way to go and
  get some. The generator now **verifies** this rather than trusting polar
  arithmetic: territory boundaries are domain-warped by up to +/-42 m per axis,
  which is wider than the margin between a home deposit and its neighbour, so a
  deposit placed by angle alone can silently land next door. `PlaceInHome` walks
  each deposit toward the home seed until the partition itself agrees, and
  `ValidateHomeResources` fails the build loudly if any home lacks either.
- **`MinerTag` and `MinerState` survive on the Worker and must.** `MinerTag` is
  the game's de-facto "this unit is a villager" discriminator — 20+ non-economy
  sites test it (combat auto-acquire exclusion, veil infection, auto-flee,
  wall-garrison ban, formation exclusion, Feraldis Berserker conversion, AI
  worker counting, unit naming). `MinerState` is read by the animator, the info
  panel and the lockstep hash. They are vestigial names, not vestigial code.
- **Resource nodes never deplete now.** Depletion bars will render permanently
  full, and the `gatherSpeedMult` tech effect plus
  `SectResearchEffects.VeilstoneYieldMultiplier` are dead levers that need
  re-pointing at the territory tick or removing from the design.

---

## 5. Map structure numbers

**512 x 512 m, 6 players, ~25 territories.**

Region count scales with **players**, not with area — it is the number of
territories *per player* that decides whether the claim game has enough moves in
it, and under §2 the starting territory is also the box an Age 0 player is
confined to.

    territories ~= players * 4 + 1

| Map | Size | Players | Territories |
|---|---|---|---|
| Hollow Table | 192 m | 2 | 9 |
| Sundered Crown | 256 m | 4 | 17 |
| **the 512 map** | **512 m** | **6** | **25** (authored reference) |

This replaces the second pass's constant-region-SIZE rule (~3,120 m²), which
was calibrated on Sundered Crown alone and gave 65 territories on a 512 m map —
past the readable limit and past the point where any single flip matters.

The Nature ring eats into this: a ring ~30 m thick leaves ~452 x 452 m of
playable ground, so 25 territories average ~8,200 m² (~90 m across) rather than
the whole 512² divided 25 ways.

---

## 6. What this supersedes

| Superseded | By |
|---|---|
| Regions.md 2nd pass — territory flips on influence dominance (0.6/0.4 + dwell) | §2: claim by structure |
| Regions.md 2nd pass — constant region SIZE (~3,120 m²) | §5: count scales with players |
| Regions.md 2nd pass — curse as an influence channel | §3: Nodes send waves |
| [Overview.md](Overview.md) — "Alanthor players cannot build outside their own influence" | superseded, now by territory rather than influence |
| [Overview.md](Overview.md) — mined resources credited per gather tick by workers | §4: workers no longer gather |
| [CLAUDE.md](../../CLAUDE.md) "Do Not Change" — direct-credit mining, miner auto-find | §4: the Miner no longer exists |
| Map size conventions (352 / 704 m) | §5: 512 x 512 m |

The bottom three are the heavy ones. Worker gathering is wired through
`MiningSystem`, `VeilstoneMiningSystem`, the AI economy manager and the
Gatherer's Hut area income; the Miner is a unit with factories, SOs, AI
behaviour and UI. Removing it is a deletion pass across the codebase, not a
tuning change — and CLAUDE.md's "Key Design Decisions (Do Not Change)" list
needs rewriting to match, since it currently asserts the opposite.

---

## 7. Showing territories (SHIPPED — survives the redesign)

This part is implemented and is not affected by the model change above: it draws
whatever partition it is given.

| View | How |
|---|---|
| **In-world terrain** | A quiet darkening along the border, drawn last in `TWBTerrainOverlays.hlsl` so it survives cursed, bloodied and cultured ground |
| **Minimap** | A readable dark lattice over the territory tint, under the blips; rasterised once and cached |
| **Lobby thumbnail** | Parchment lattice burned into the baked PNG |

Those three draw the PARTITION — every region, in neutral grey. **Who holds
what** is a second, separate line drawn over them in the owner's banner colour:
a terrain-draped ribbon in-world (`InfluenceOverlayRenderer`) and a one-pixel
seam on the minimap (`MinimapPanelBinder.DrawTerritoryOutlines`). Both trace the
outline of the UNION of a faction's regions, so internal divisions between two
of your own territories stay grey and only the edge of your holding is coloured.
Unlike the partition, ownership is intel: both are fog-gated to ground the
viewing player has explored.

Implementation facts worth keeping:

- The partition lives in `RegionMap` and **every view goes through it**. A
  second copy of the nearest-seed maths anywhere would draw a different border
  from the other two.
- Boundaries are **domain-warped** (~150 m wavelength, ~14 m amplitude) so they
  wander instead of ruling straight. The warp is applied to the QUERY, not the
  seeds, which is what keeps the partition watertight. The amplitude was 42 m
  against a 110 m wavelength until 2026-08-28, where the displacement changed
  faster than the bends were wide and the result read as a zig-zag rather than
  a wander.
- Unclaimable ground (below 4 m / above 24 m — mountains, cliffs, water, the
  rim) returns `None` and draws no border, so lines hug the foot of a mountain
  and the shore of a lake rather than crossing them.
- The terrain line is baked into the spare **G channel of `_TWB_BloodMask`**
  (only R was used), so it costs no extra texture, sampler or per-frame work.
- Line width is always in **metres**, never pixels, so a border is the same
  thickness on the ground on any map size.
- Territory shape is **not fog-gated** — it is map structure, the same partition
  the lobby shows before the match starts.
