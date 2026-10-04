# Territory Claims & the Curse

> **Doc version: 2026-09-29 — FOURTH MODEL. Canon for how ground is owned, by
> players and by the curse, and for how a match is won.** Supersedes:
>
> - [Regions.md](Regions.md) §2 (claiming by building a Hall, adjacency,
>   worker-in-territory, escalating Hall cost) and §3 (the curse's conquest
>   rule, pure nodes, anchors). Regions.md §1 (map structure, region kinds,
>   where a border is), §4 node quotas and §7 (showing territories) STAND.
> - [Curse_And_Shardroot.md](Curse_And_Shardroot.md): **wells, verbs, rites,
>   the Backlash and well-domination victory are removed.** §2.13's army
>   cadence and growth, the hostile-ground exposure model (§2.5b) and the
>   Shardroot's carry / store / detonate rules (§3.1) STAND where this file
>   does not replace them.
> - [Age_0.md](Age_0.md) Hall and Fortress sections — **the Hall is removed**;
>   the capital is the Shelter (Age 0), which becomes the Fortress at age-up;
>   the Fortress inherits the Hall's roster and research and is buildable.
>
> Everything here is **(new — not yet in code)**.

---

## 1. The model in one paragraph

Ground belongs to whoever **stands on it**. Every territory carries one
ownership meter, 0-100. Military units fill it, an enemy standing alone drains
it, and an empty, unbuilt territory decays back to nobody. Two hostile sides
standing in the same territory **freeze** it — you take ground by clearing it,
not by out-numbering on paper. Buildings **hold** ground against decay;
buildings on resource nodes, and Fortresses, **lock** it so it cannot be
drained at all. The curse plays by the same meter, counting double, and it
builds its own nodes on resource nodes, which lock ground exactly as a player's
extractor does.

## 2. The meter

Each territory stores **a holder** (a faction, the curse, or nobody) and
**a value** 0-100.

| State | Condition |
|---|---|
| **Unclaimed** | value 0, or value > 0 but the holder has never reached 100 (a claim in progress) |
| **Claimed** | the holder reached 100 and the value has not since returned to 0 |
| **Cursed** | claimed, and the holder is the curse |

**Only a claimed territory is owned.** A claim in progress grants nothing:
no building, no income.

### 2.1 Who counts

A unit counts toward its faction's **claim weight** in the territory it
stands in when it is:

- a **military** unit — soldiers, cavalry, siege, sect heroes and King Lexor
  count; **Workers, Scouts and caravans do not**;
- alive, finished training, and not a temporary summon (Honour thy Pledge,
  Raise Anew and the like do not claim — same rule as Heroes.md §1.1 XP).

Its weight is its **population cost**. A faction's weight in a territory is

`weight = (Σ popCost) ^ claimExponent`

with `claimExponent` = **1.0** to start (linear in population). Set it to
0.5 for the square-root curve if deathballs claim too fast in play. The meter
parameters (`claimExponent`, `claimRate`, `decayRate`) are config values in
`Assets/Scripts/World/Regions/TerritoryOwnership.asset`; the numbers quoted in
this section are the rule's starting values, and the asset wins.

**The curse** counts **×2** (§6.1), and only while it is standing, not
passing (§6.2). Curse units carry no population cost, so they weigh by tier:
crystalling 1, veilstinger 2, godsplinter 3 (before the ×2).

### 2.2 Rates (per second, evaluated at 1 Hz on the lockstep clock)

| Situation in the territory | Effect on the value |
|---|---|
| **Two or more mutually hostile sides** have weight > 0 (the curse is hostile to everyone) | **Frozen** — nothing moves, including decay |
| Only the **holder** (or nobody holding yet, and one side present) has weight | rises by `weight × claimRate` (claimRate = 1), capped at 100 |
| Only a **challenger** has weight | falls by `weight × claimRate`; at 0 the challenger becomes the holder and the value starts rising |
| **Nobody** has weight, and the holder has **no finished building** there | falls by `decayRate` (**3**) |
| Nobody has weight, and the holder has a finished building there | **Held** — no change |
| The territory is **locked** (§3) | the value is pinned at 100; challengers cannot drain it |

So an unclaimed territory takes **100 points** to claim, and taking one
somebody else has claimed takes **200** — drain, then fill. A standing claim is
worth something.

**Allies never contest each other** (Teams.md). An ally's units neither drain
nor freeze the holder's meter, and add nothing to it: each faction claims for
itself. Two allies standing in the same unclaimed territory: the first to put
weight on it becomes the holder; the other's weight is ignored until the
holder's weight there is zero.

### 2.3 What changes at the edges

- **Reaching 100** — the territory becomes claimed by the holder. Ownership
  event: borders, minimap and ground rebuild (Regions.md §3b — event-driven,
  never per frame).
- **Falling to 0 from claimed** — the territory becomes unclaimed. **Every
  building the former owner has in it collapses**: walls, houses, towers,
  foundations, everything. A *foundation* is a building placed but not yet
  finished; it neither holds nor locks (§3) and it collapses with the rest.

## 3. Holding and locking

| Structure (finished) | Stops decay (**holds**) | Stops draining (**locks**) |
|---|---|---|
| Any building | yes | no |
| A building **on a resource node** (extractor, Gatherer's Hut on a supply node, …) | yes | **yes** |
| A **Fortress** | yes | **yes** |
| A curse node (§6) | yes (for the curse) | **yes** |
| Anything under construction | no | no |

**A territory stays locked while ANY locking structure stands.** To take locked
ground you raze every extractor and Fortress in it, then drain it. Walls and
towers slow that down; they do not stop it.

## 4. The Fortress

**Every player starts with a Shelter** — the Age 0 form of the capital —
which claims and locks the home territory from tick 0 (home meter = 100, holder = that player).
Beside it stands one finished **House** (Hut, 2026-09-30) on a diagonal
just outside the capital, so the opening population cap is **the Shelter's
housing plus one House's** — both read from their SOs (`Fortress.asset` and
`Hut.asset` `populationProvided`; decision 8, 2026-10-03, is what retired the
old "13"). The opening army — 5 Spearmen, 1 Scout and 3 Workers (decision 19;
no Archers, Age 0 is the melee age) — fits under that cap with room for the
first trainings, so the first extra House is an early but not an immediate
need.

**At age-up the Shelter automatically becomes the Fortress** (2026-10-03),
for every culture: the same building renamed, internal id `Fortress`, with
levels L1-L3 (Alanthor's are `Civs/Alanthor/Buildings/Fortress/Fortress_Lvl1..3`).
Everything below that says "Fortress" applies to the Shelter too.

**The Fortress is buildable** — it is how a player secures ground that has no
resource nodes, or doubles the lock on ground that has. It is **the most
expensive thing in the game**:

| | |
|---|---|
| Cost | on the `Fortress` SO. **Rule:** it must stay the most expensive building and above the age-up landmark's price |
| Limit | one per territory |
| Placement | only in a territory you own (the ordinary build gate, §5) |
| Hosts | trains Worker and Scout (Alanthor adds the Ledger and King Lexor), researches Stone Tools / Armed Scouts (Alanthor adds the tool ladder, Mason Guild and Scouting Celestarii — all `researchAt: Fortress`), banks resources |

**There is no Hall, no King's Court and no Town Hall** (deleted 2026-10-03).
Their roster and research belong to the capital — the Shelter in Age 0, the
Fortress after — and every culture's cultured capital is the Fortress.

## 5. The build gate

**Every building may be placed only in a territory you own** (claimed, holder
is you). No exceptions: the Hall's "may be built on ground you do not hold"
rule died with the Hall, and so did adjacency, the worker-inside rule and the
escalating Hall price. Expansion is paid for in army time and in extractors,
not in a claim building.

**Walls included, every metre of them (2026-09-30).** A wall hub must stand on
ground you own, and so must the whole curtain between two hubs: a wall between
two of your hubs may not cut across a neighbour's corner. A drawn wall that
leaves your ground anywhere is refused whole, before anything is paid. Checked
where the order executes, for players and AI alike.

The placement ghost and the command executor both check ownership at the tick
the order runs (the same two-place enforcement Regions.md §2 established), and
a refusal names the rule.

## 6. The curse as a claimant

The curse is not a player: no economy, no tech, no brain. It claims ground,
builds nodes, and fields armies — nothing else (Regions.md §3's scope rule
stands).

### 6.1 Double weight

A curse unit's claim weight is `2 × popCost`.

### 6.2 Standing, not passing

A curse unit counts only when it **means to be there**: it is idle, or its
current order's destination lies inside this territory. A raid party marching
*through* a territory to reach another claims nothing on the way.
*(Players are not under this rule. If player armies passing through turn out
to flip ground too cheaply, the same rule applies to them.)*

### 6.3 Curse nodes (a.k.a. pylons)

> **2026-10-01:** veilsteel deposits are removed from the map, and what a
> veilstone outcrop under a curse node means (Cursed state, replenished
> reserve, pacified when the node dies) is canon in
> [Veilstone_Economy.md](Veilstone_Economy.md) §2.

- **Built on any resource node** — supply, iron, veilstone or veilsteel.
  The curse builds a node only in a territory it has **claimed**, by the units
  standing there; the node takes `nodeBuildSeconds` and does not lock while
  under construction.
- **Locks** the territory (§3), and **is destructible**. There are no
  indestructible pure nodes any more.
- **Fields its own garrison** — the §2.13 army: whole-spawn every
  `armySpawnSeconds`, growing ×`armyGrowth` per spawn. **Every node spawns
  (2026-10-03)**, not one per territory: a territory with four curse nodes
  fields four garrisons of `garrisonCap` each, and each garrison guards the
  node it rose from (§6.7).
- **Cursed ground is a RADIUS, not a territory.** The existing hostile-ground
  effect (speed debuff, damage-over-time, cursed texture — Curse_And_Shardroot.md
  §2.5b) applies within `nodeAuraRadius` of each curse node, not across the
  whole curse territory.

### 6.4 Where the curse starts

At match start the curse raises `initialNodes` nodes (default: one per player)
on **random resource nodes**, chosen from the match seed so every lockstep
peer agrees. Two exclusions and one fairness rule:

- never in a player's **start territory**;
- **fairness**: the draw is rejected and redrawn (deterministically) until
  every player's graph distance (`RegionMap.AreAdjacent` hops) to their
  nearest curse node differs by at most one.

### 6.5 How the curse spreads

**Fill before spreading (2026-09-29).** The curse takes EVERY resource node in
ground it holds before it reaches for new ground: while a held territory has a
free node (no curse node, no building on it), the dispatch goes there instead —
a merge party from that territory, raising a curse node on it. Only the
Shardroot hunt outranks this.

Every `expansionSeconds` the curse sends a party of `mergePartySize` from a
held territory to an adjacent one it does not hold (the §2.13 harassment
party). There the party **stands** and claims by the meter:

- **Unclaimed, or claimed but not locked** — it drains and claims, then builds
  a node on a resource node in it (if the territory has one).
- **Locked** — ~~it raids~~ **not a target (2026-10-03).** The curse only
  advances on ground it means to take, and locked ground cannot be taken, so
  a locked territory is never picked. A claim party whose target is locked
  under it while it stands there gives up and walks home to its garrison.
  There are no raids.

**If the curse holds no node at all**, it re-seeds one after `reseedSeconds`
(default 180 s) under the §6.4 rules. The curse can be driven back, never out:
it is the only source of religion points (Religion.md).

### 6.6 The Shardroot

There are no wells, so there is no host well and no Maw backstop.

- **Source**: the Shardroot rides out with the curse — on every curse spawn,
  a `shardrootChance` roll puts it on one unit (Curse_And_Shardroot.md §2.13
  rule 5). **Backstop**: the first spawn after `shardrootGuaranteeSeconds`
  (720 s) carries it if it is not out yet. Exactly one per match.
- **Carry / store / detonate**: unchanged from Curse_And_Shardroot.md §3.1,
  with **Fortress** in place of Hall as the store that awakens the Shardbound
  Hero (for Alanthor, King Lexor).
- **The curse hunts the holder, and only the holder.** While any player holds
  it (carrier, hero or enshrining Temple):
  - the curse's garrison size and spawn rate rise by `shardrootCurseBonus`
    (**+50 %**: size ×1.5, interval ÷1.5);
  - **every offensive curse force** (harassment, raid and claim parties, and
    the §6.8 attack waves) goes
    for the holder's territories and **ignores every other player**;
  - garrisons still defend their own territory against any intruder — the
    curse ignores non-holders, it does not let them walk in.

### 6.7 The curse defends; it advances only to take (2026-10-03)

The curse is a **defensive** force that spreads, not an army that hunts.

- **Garrisons guard their node.** A garrison unit engages only hostiles
  within `guardRadius` of the node it guards, and never chases past
  `guardLeashRadius` from it: past that it drops the fight and walks back.
  It no longer sweeps its whole territory for intruders. A unit whose node
  dies adopts the nearest live node in its territory.
- **Only claim parties and attack waves leave home** (waves since
  2026-10-04, §6.8). A claim party goes only for an adjacent, unlocked
  territory it can take (§6.5), or to fill a free node in ground it holds;
  an attack wave marches on a player (§6.8). Both are DRAFTED out of the
  garrisons (never below `garrisonMinPerNode` a node) — the garrison is the
  curse's only spawner. A unit still on garrison duty never leaves: the
  rules above are unchanged by waves.
- **The Shardroot hunt is unchanged** (§6.6): while a player holds it,
  offensive parties go for the holder.
- Why: the curse is the only source of religion points (Religion.md), and a
  curse that roams and raids punishes everyone at random. A curse that sits
  on its nodes is a target players choose to attack, and the reward is theirs.

### 6.8 Curse waves and the cap (2026-10-04)

Developer directives: *"Curse should send waves towards the players"*,
*"Curse must be capped at 250 units"*, and the spawn model: *"all spawn as
garrison, then waves are 30% of that."* The curse still defends (§6.7); on
top of that it now **attacks on a clock**, and its whole army has a hard
ceiling. Every number below is a field on `BorderSettings.asset` — this
section names the field, the asset holds the value.

**One spawner: the garrison.** In the living curse **every** curse unit
rises as **garrison** of a node (§6.3, `armySpawnSeconds`). Nothing else
spawns. Claim and fill parties (§6.5), the Shardroot hunt (§6.6) and the
attack waves below are **drafted out of the garrison pool**:

- A draft takes garrison units that are not in a fight; a defender already
  fighting stays in it.
- **No node is stripped bare:** a draft never takes a node's garrison below
  `garrisonMinPerNode`.
- A party (claim, fill, hunt) wants `mergePartySize` units (× the §6.6
  bonus while someone holds the Shardroot). It takes them from the sending
  territory's garrisons first, then from the guard posts nearest it. If
  there are not enough spare units, the party goes **smaller**; if there are
  none at all, it is **skipped** (logged either way).
- Drafted units leave the garrison count, so their node fields them again
  at its next army spawn (§6.3) — that regrowth is what feeds the next
  draft.

**Attack waves.**

- **When.** The first wave forms at `firstWaveSeconds` of match time, then
  one every `waveIntervalSeconds`. The clock runs on whether or not a wave
  could actually be sent.
- **How big.** At each slot the curse drafts **`waveDraftFraction` of all
  its garrison units** into the wave, nearest the target first (guard posts
  by distance to the objective, entity order breaking ties — the same on
  every peer), never below `garrisonMinPerNode` a node. If that comes to
  fewer than `waveMinSize`, the slot is **skipped**, not banked.
- **Whom.** Among the living players (every player with a standing
  building), the target is the one whose nearest building is closest to any
  curse node. **Fair rotation:** if that player was also the last one a wave
  went for, and any other player's distance is within
  `waveTargetDistanceSlack` × the nearest one's, the wave goes to one of
  those others instead (a seeded draw among them, on every peer), so no single
  player eats every wave. With only one player in reach, they get it again.
  **While a player holds the Shardroot, every wave goes for the holder** —
  §6.6's "every offensive curse force" now includes waves.
- **What it attacks.** The target player's **nearest territory** — the one
  holding their building nearest the curse — and in it, their nearest
  production building (anything that trains units) for preference, else
  their nearest building there. The wave marches in formation, attack-moving,
  and fights. Whenever it stands idle it re-targets: the nearest hostile unit
  in that territory, else the nearest of the player's buildings there, else
  the player's nearest building anywhere (whose territory becomes the new
  one). A wave hunting the Shardroot holder re-aims at the holder instead.
- **Going home.** After `waveDurationSeconds`, or once it is reduced below
  `waveRetreatFraction` of the size it set out with, or when the target
  player has nothing left standing, the wave turns back: its units **become
  garrison** of the curse node nearest them at once, and the §6.7 leash walks
  them home (a fight past `guardLeashRadius` is dropped). They count toward
  that node's garrison from then on.
- **Warning.** Every wave pings the minimap at the curse node nearest the
  target and at its objective, logs `[CurseTerritory] WAVE …`, and the
  targeted player is told a curse wave is coming.
- Waves stand on player ground and therefore claim it (§6.1, §6.2) — that is
  intended: a wave left alone in a territory drains it.

**The cap.** Live curse units — every curse creature, whatever raised it —
never exceed `maxCurseUnits`. There is **one gate**: every spawn path asks
how much room is left under the cap (live units counted once per ask) and
raises at most that many. In the living curse the only spawner is the
garrison, so **the cap bounds the garrisons**, and waves and parties — being
drafted from them — are bounded with them. When the room is short at an army
spawn, **nodes under attack** (a hostile within `guardRadius`) are topped up
before any other node. The curse's event spawns — blood pools on cursed
ground, a failed rite's backlash, a corrupted well's defenders, a worker
turned by the veil, the Feraldis Violent Extraction final wave — take
whatever room is left at the moment they fire, and raise nothing at the cap
(an infected worker still dies; no creature rises).

## 7. Winning

**Elimination is the only victory.** Last player — or last team (Teams.md) —
standing wins. The curse cannot win; if the last players fall together, the
match is a draw.

A faction is eliminated when it has **no Fortress, no military building and no
Worker** — the existing lifeline in `EliminationSystem`, with Fortress in place
of Hall.

## 8. Knobs

All in `TerritoryOwnership.asset` / `BorderSettings.asset`:
`claimRate` 1, `claimExponent` 1.0, `decayRate` 3, `curseClaimMultiplier` 2,
`nodeBuildSeconds`, `nodeAuraRadius`, `initialNodes`, `reseedSeconds` 180,
`shardrootGuaranteeSeconds` 720, `shardrootCurseBonus` 0.5, plus the existing
`armySpawnSeconds` (120 since 2026-10-03, was 180), `armyGrowth`,
`garrisonCap` (now **per node**), `expansionSeconds`, `mergePartySize`,
`shardrootChance`, and (2026-10-03) `guardRadius` 30, `guardLeashRadius` 45,
and (2026-10-04, §6.8) `firstWaveSeconds`, `waveIntervalSeconds`,
`waveDraftFraction`, `waveMinSize`, `garrisonMinPerNode`, `waveDurationSeconds`,
`waveRetreatFraction`, `waveTargetDistanceSlack`, `maxCurseUnits`.
`raidSeconds` is retired with the raids.

## 9. Known risks (to watch in playtest, not to fix in advance)

- **Curse snowball.** Any-node building + double weight + compounding garrisons
  is positive feedback with no ceiling. First knobs if it runs away:
  `expansionSeconds`, `armyGrowth`, a cap on concurrent curse nodes.
- **Wide empires are cheap again.** The escalating Hall price is gone; the only
  cost of a territory is holding it. The user's call (2026-09-29): see what
  happens.
- **Collapse is brutal.** Losing ownership deletes every building in the
  territory. That is intended; it is also what makes a lone unguarded extractor
  the most important building on the map.

## 10. Fortresses anchor the empire (2026-10-01, cap removed 2026-10-04)

> **2026-10-04 (developer: "Remove the limit. It should be granted by how much
> you can defend, not a hard cap."):** the territory limit (Fortress levels +2
> once aged up) is **deleted**. Fortress levels and extra Fortresses no longer
> buy room. Superseded text is not kept.

**There is no cap on how many territories you hold. You hold what you can
defend.** Ground is kept by the meter (§2-§3) and nothing else: standing
military fills it, a hostile side standing in it freezes it, empty unbuilt
ground decays, and losing ownership collapses everything you built there.
Spread an army too thin and its ground decays or is drained out from under it;
a building holds a territory against decay, and an extractor or a Fortress
locks it.

**Claims open at age-up.** In Age 0 you hold your start territory only (the
age gate of [Regions.md](Regions.md) — not a cap): an army standing on other
ground claims nothing (frozen, with a notice). From age-up you may take any
number of territories, subject to the connection rule below.

**Connection.** You may only take ground that **borders** territory linked to
one of your Fortresses through ground you hold. Held ground that **loses** that
link wears down: every one of your buildings there loses its full health over
**240 s**, so the lot is gone in about four minutes unless the link is restored.
The curse is bound by neither rule.

Implemented in `TerritoryClaimSystem` (`ComputeReach`, `MayTake`,
`WearDisconnected`; the refusal notice is `NoticeRefusedClaim`).

## 11. Territory types (2026-10-01)

Every territory has up to five nodes, set by its TYPE — the scene's node
markers no longer decide anything; nodes are generated from the type.

| Type | Nodes |
|---|---|
| **Start** (holds a player start) | 3 supply, **3 iron** (2 until 2026-10-02 — Veilstone_Economy.md §6), 1 veilstone |
| Normal | 3 supply |
| Normal + iron | 2 supply, 1 iron |
| Normal + veilstone | 2 supply, **1-2 veilstone** (a seeded draw per territory; 1 until 2026-10-03) |
| **Empty** | none — position and build space only |
| **Veilstone rich** | 4 veilstone, **starts cursed** (a curse node on every outcrop) |
| **Iron rich** | 3 iron |
| **Sanctum** | none — its holder earns **1 Religion Point a minute** |

**Placement is random inside the territory** (2026-10-01): each node lands on
a random legal spot, 9 m from the territory's other nodes and, in a home,
20 m clear of the start — from a per-territory seeded stream, so every peer
lays the same map. **Never in the restriction zone (2026-10-02):** a node's
whole 4 m footprint keeps **10 m** clear of any OTHER territory — covering
the band inside every border where the border wall runs and no AI building
stands (5 m, Age_1_Alanthor.md § The AI's wall) — and its centre stays 8 m from impassable
ground (lake, mountain, map edge). A territory too thin to fit a node under
those rules comes up short, and the generator logs it.

**Every map has at least one of each special type** (Empty, Veilstone rich,
Iron rich, Sanctum); the rest are the three Normal kinds. The type is authored
on `RegionSeedMarker.Resources`; `Auto` (the default) is resolved from the
match seed, filling the missing special types first. A map with too few
non-start territories logs a warning.

**The Normal kinds are dealt 1 : 2 : 4 (2026-10-03)** — of every seven filler
territories, one is Normal, two are Normal + iron and four are Normal +
veilstone (~57 % carry veilstone, was an even third each). Veilstone is what an
aged-up army runs out of, and Alanthor gets it only from Trading Outposts
beside outcrops in held ground (Veilstone_Economy.md §3.1): a 0.0.33 batch on
Veilmarch dealt 23 outcrops to 8 players and every faction held 1-3 of them.
With the new deal and the 1-2 draw, Veilmarch's 33 fillers carry ~19
veilstone territories and ~28 of their outcrops (was 11), ~40 on the map in
all. The kinds are interleaved by a smooth weighted round-robin over the
seed-shuffled territories, so small maps get their share too. The weights and
the outcrop range are `TerritoryResources.asset` (beside `TerritoryResources.cs`).

Implemented in `TerritoryResources` (resolve + generation), the
`SpawnDelayHelper` resource step, `CurseNodeSeeding.CurseVeilstoneRich` and
`SanctumSystem`. The territory hover overlay shows a Sanctum's +1 RP/min.

### 11.1 Map review (2026-10-01)

| Map | Territories | Homes | Free for types | Verdict |
|---|---|---|---|---|
| Hollow Table | 5 | 2 | 3 | **Redraw** — needs at least 4 non-start territories for the four special types (Empty is missing) and has no room for any Normal filler |
| Sundered Crown | 17 | 4 | 13 | OK |
| Sundered Reach | 13 | 3 | 10 | OK |
| Twin Spans | 25 | 6 | 19 | OK |
| Veilmarch | 45 | 8 | 37 | OK |

With node generation, no map needs its node markers moved: the old authored
layouts (homes with 6-8 nodes, territories with no ore, 4-veilstone centres)
are simply ignored. The supply/iron/veilstone/veilsteel/well/blight-pocket
markers in the scenes are now inert and can be stripped on the next re-bake.
None of the five maps authors a region `Kind` other than Normal.
