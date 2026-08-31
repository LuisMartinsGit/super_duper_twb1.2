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

## 3. The curse is a PLAYER (2026-08-31 — THIRD MODEL, supersedes the wave-claim model below)

**The curse expands exactly the way a player does: it takes whole territories,
instantly, by the same ownership rules.** No influence, no gradient, no
creep-front — a territory is curse-held or it is not.

**The curse is NOT a full player.** It has no economy, no tech, no build
orders, no AI brain. It does exactly two things: it takes territory (the
expansion rule below) and it spawns armies that attack players. Do not give
it any other player system.

**The FORTRESS (2026-08-31, see Age_0.md):** every player's STARTING
building is now a Fortress — the capital, larger and far tougher than a
Hall. It claims its home territory under the same rule as the Hall
(mechanically it carries the Hall's claim), while the buildable Hall
remains the one and only expansion claim structure of §2.

**Hall cost rebalanced 600s/200i → 450s/450i (2026-08-31, batch 8):**
supplies were both the army's fuel and the claim's price, so the two
engines fought over one resource while iron sat idle past 1,500. Splitting
the price across both lets a faction save a claim pot and rebuild its army
at the same time — expansion pace and army size stop trading off
one-for-one.

- **The curse starts from PURE NODES.** A pure node is authored map data,
  INDESTRUCTIBLE, and is the curse's version of a Hall: the territory holding
  it is curse-owned from the first tick. Pure nodes are the verb-victory
  objectives (purify / pacify / destroy per culture) and the Shardroot host —
  they are interacted with, never razed. **Veilmarch carries exactly ONE pure
  node, in the centre territory.**
- **Expansion rule: the curse conquers a RANDOM ADJACENT territory when that
  territory (a) contains no Hall and (b) contains at least one veilstone
  node.** Adjacent means region-graph adjacent (Voronoi neighbours). The
  conquest is instant, like a player's claim. Ground without veilstone does
  not interest it; ground a player holds (a live Hall) is safe from
  conquest — the curse fights players with waves, not paperwork.
- **A conquered territory gets a destroyable curse anchor** (a well). Killing
  the anchor reverts the territory to Natural instantly — the same
  claim-decays-on-structure-death rule players live by. Only pure nodes are
  beyond destruction.
- **Curse-held veilstone territories PRODUCE WAVES that attack players** —
  the more ground it holds, the more fronts it opens. Waves target the
  nearest player holdings; killing a wave does not free ground, killing the
  anchor does.

This keeps the map an active opponent with completely legible rules: you can
read on the territory map exactly what the curse holds, what it can take next
(hall-less veilstone ground adjacent to it), and what to kill to push it back.

### Veilstone placement rules (2026-08-31)

- **Every starter territory MUST have a veilstone outcropping.** Supersedes
  Veilmarch's centre-ring exclusivity and the older "homes have no veilstone"
  authoring.
- **50% of ALL territories carry veilstone.** Veilstone is both the army
  economy and the curse's food: the same ground that makes a territory worth
  taking makes it conquerable by the curse if left hall-less. Authored
  markers are honoured first; the runtime coverage pass fills the shortfall
  deterministically (same contract as veilsteel's).

## 3b. Territory visuals (2026-08-31 — INFLUENCE MAPS ARE REMOVED)

**There are no influence maps.** Territories have fixed shapes; ownership is
the only variable. Border colours and ground textures follow OWNERSHIP, and
they change when ownership changes — an event, not a per-frame computation.
**NO per-frame territory/influence compute anywhere**: the border ribbon, the
minimap tint, and the culture ground mask are all rebuilt once per ownership
change and then static. (Measured motivation, 2026-08-31 session: the
per-frame influence border tracer alone logged 325 frame spikes in ten
minutes.) The per-frame influence simulation (PlayerInfluenceMap growth,
coverage sampling, hut "covered ground" preference) is retired with it.

---

## 4. Economy (Alanthor) — the territory turn

Territory, not workers, is the engine.

### Resource domains (2026-08-28, rev. 2)

**Each resource owns a domain, not a tier.** Iron spans two of them, so the
domains are connected rather than separate games.

| Domain | Pays with | Covers |
|---|---|---|
| **Infrastructure** | Supplies + Iron | Buildings, building upgrades, early tech, the entry units |
| **The army** | Veilstone + Iron | Every soldier above the entry line, mid tech |
| **The end of the tree** | Veilsteel + Veilstone | Late tech only |

**Why.** Measured over a 29-minute four-AI match: supplies were consumed to
zero by every faction (banks of 2, 72, 140, 347) while veilstone climbed to
1,129-10,076 and veilsteel to 357-2,097. Soldiers were priced in supplies, so
armies died around minute 8 and never rebuilt, and 7,000 units of ore per
faction bought nothing. Three of four AIs finished with no army; the winner had
two soldiers and won by outliving the others rather than by fighting.

The domains match demand to supply: the resource that is always spent buys the
thing you always build, and the resource that piles up buys the thing you
continuously lose.

**Tiers come from the prerequisite graph, not from names.** Depth 0 is early
(49 techs), depth 1 mid (21), depth 2+ late (20). The weapon ladder falls out
of it exactly: Stone -> Iron -> Veilstone -> ShardInfused.

**Entry units stay on supplies.** Spearman, Archer, Scout, Worker and Litharch
keep their supply price. Veilstone in the first two minutes ranges 55-165
across factions - a 3x spread on map draw alone - so gating every soldier on it
from minute zero would decide the opening by luck and make the early game
quieter, which is the opposite of the intent. From the second tier up, the army
runs on veilstone.

**Exchange rates come from observed abundance**: veilstone is plentiful so it
costs MORE per unit of value (1.5x supplies); veilsteel is scarce so it costs
LESS (a quarter). Resulting demand share: Supplies 36%, Veilstone 41%, Iron
20%, Veilsteel 3% - against 61/12/25/3 before.

**Army prices are sized to the 200-population ceiling** (2026-08-29). The cap
should be something every player reaches inside twenty minutes, which is
roughly 35 workers and 160 soldiers. Against measured income over 1,200 s
(~12,000 supplies, ~9,600 iron, ~8,400 veilstone), and after paying for workers
and ~18 Huts of housing, that leaves about 55 veilstone and 25 iron per
soldier — so military costs were cut to hit it (soldiers: veilstone x0.35,
iron x0.55; entry units x0.55).

Three things had to move together, because each alone is a hard ceiling:
the PRICE (above), the AI's `SustainArmyCap` (10/20/24/32 across the ladder was
an order of magnitude below 200 pop, so the AI stopped wanting soldiers long
before it ran out of money), and `PopulationHeadroomFloor` (at 2, housing
trailed production and stalled every trainer while a hut went up; at 16 it
leads).

**Watch**: supplies are now a pure infrastructure currency. Buildings are
bought once where soldiers are lost continuously, so if supplies start piling
up unspent, the answer is more continuous building demand (depletion already
pushes that way), not moving soldiers back onto them.

Everything below is authored **per minute**, because that is the unit the player
is shown (see *Reading a territory* at the end of this section).

- **A territory produces a supply tick that scales with its supply nodes**
  (2026-08-29): 20/min for the bare ground plus **26/min per supply node**. A
  standard 2-node territory therefore pays the same 72/min the old flat base
  did; a 4-node home pays 124. The base yield now CORRELATES with what stands
  in the territory instead of being one number for every region — an empty
  territory is still never pointless to hold, but a stocked one is visibly
  richer before anything is built on it.
- **A territory containing a resource node produces a trickle of that
  resource — iron and veilstone 190/min per node, veilsteel 95/min**
  (2026-08-30: iron/veilstone doubled from the flat 95 — armies were being
  trained but not replaced fast enough to fight with, and the ore economy
  was the bottleneck), whether or not anything is built on it. Holding the
  ground is what pays; the node is the reason the ground is worth holding.
  Doubled yield drains the node's reserve twice as fast — a fresh 4,000-unit
  seam now runs to its 25% floor in roughly 21 undisturbed minutes, which
  sharpens the expand-or-decline pressure rather than blunting it.
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

- **Ore extractors are priced in IRON first** (2026-08-30): Mine 90 supplies +
  140 iron, Veilstone Mine 90 + 160 iron, Smelter 240 + 320 iron. They were
  supply-priced, and the measured result — the moment on-node placement
  actually worked — was a straight duel with the Hall for the one currency
  everything needs: six of eight batch matches ended with ZERO expansions,
  every 600-supply claim starving behind a queue of 198-supply mines. Iron is
  the currency a territory-holding faction banks and barely spends, so paying
  iron to dig ore is the resource-domain rule (Infrastructure = Supplies +
  Iron) applied *inside* the domain. The Gatherer's Hut stays supply-priced.

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

**Supply nodes are a placement rule first** — they decide WHERE a Gatherer's
Hut can go (and since 2026-08-29 they also scale the territory's base supply
tick, above). The placement rule replaces the two crutches the hut needed
before it:

- the **per-territory hut cap** — the count is now however many supply nodes the
  territory has, which is map data rather than a magic number, and it can differ
  between a rich territory and a poor one;
- the **gather-area rule** — there is nothing to overlap, so no coverage
  percentage, no first-come-first-served split, and no reason to inspect the
  ground before placing.

### Node quotas (2026-08-29)

Every territory is guaranteed a working economy, and a home is guaranteed a
bigger one:

- **Every territory carries 2 supply nodes. A home territory — one holding a
  player start — carries 4.** The count is the hut cap AND the base-tick
  multiplier, so this is the "small territory vs large start territory"
  difference made concrete: a home supports twice the huts and twice the
  base supply trickle of the ground around it.
- **Every territory carries at least one ore node** (iron, veilstone or
  veilsteel) **and never more than four.** No region is dead ground: whatever
  you take, something in it trickles. Veilsteel keeps its own scarcity rule
  (about one territory in three, never two in one).
- **Enforced twice.** The map generators author these counts and their
  validators fail the build when a region misses quota; at match load the
  node bootstraps then top up any shortfall the same way the veilsteel
  coverage pass always has — authored markers honoured first, only the
  shortfall seeded, walked deterministically so lockstep peers agree. The
  generic min-ore top-up is IRON; **veilstone has its own coverage pass**
  (§3, 2026-08-31: every home + 50% of all territories), which superseded
  Veilmarch's old "veilstone exists only in the centre ring" rule.
  Veilsteel exclusivity still survives the guarantee.
- **The AI values ground by the same numbers**: its claim scorer counts every
  node kind (supply included) when picking which region to take next, and its
  extractor pass then builds the matching building on each node it holds.

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

Rates are all `const` at the top of `TerritoryIncomeSystem`, authored per
minute: 20 supplies base + 26 per supply node, +50 per hut, +60 per forest
(Sawyer x2), 95 per ore node + 25 per extractor level, survey ladders x1.5 a
tier. Node quotas are enforced by the generator validators and by the runtime
top-up passes (`SupplyNodeBootstrap`, `ResourceNodeCoverage`,
`VeilsteelDepositBootstrap`).

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
