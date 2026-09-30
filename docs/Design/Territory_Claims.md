# Territory Claims & the Curse

> **Doc version: 2026-09-29 — FOURTH MODEL. Canon for how ground is owned, by
> players and by the curse, and for how a match is won.** Supersedes:
>
> - [Regions.md](Regions.md) §2 (claiming by building a Hall, adjacency,
>   builder-in-territory, escalating Hall cost) and §3 (the curse's conquest
>   rule, pure nodes, anchors). Regions.md §1 (map structure, region kinds,
>   where a border is), §4 node quotas and §7 (showing territories) STAND.
> - [Curse_And_Shardroot.md](Curse_And_Shardroot.md): **wells, verbs, rites,
>   the Backlash and well-domination victory are removed.** §2.13's army
>   cadence and growth, the hostile-ground exposure model (§2.5b) and the
>   Shardroot's carry / store / detonate rules (§3.1) STAND where this file
>   does not replace them.
> - [Age_0.md](Age_0.md) Hall and Fortress sections — **the Hall is removed**;
>   the Fortress inherits its roster and research and becomes buildable.
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
0.5 for the square-root curve if deathballs claim too fast in play.

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

**Every player starts with a Fortress**, which claims and locks the home
territory from tick 0 (home meter = 100, holder = that player).
Beside it stands one finished **House** (Hut, 2026-09-30) on a diagonal
10 m out, so the opening population cap is 10 + 3 = **13**.

**The Fortress is buildable** — it is how a player secures ground that has no
resource nodes, or doubles the lock on ground that has. It is **the most
expensive thing in the game**:

| | |
|---|---|
| Cost | **1 200 Supplies + 1 200 Iron + 300 Veilstone** (first pass — must stay above the age-up landmark's 600 / 300 / 200) |
| Limit | one per territory |
| Placement | only in a territory you own (the ordinary build gate, §5) |
| Hosts | everything the Hall hosted: trains Worker and Scout, researches Stone tools / Armed scouts, banks resources |

**The Hall is removed.** Its roster and research move to the Fortress. The
cultured Hall forms of the Age 1 docs (Town Hall / Trader's Hall / Warrior's
Hall) become the cultured Fortress — those docs follow in a later pass.

## 5. The build gate

**Every building may be placed only in a territory you own** (claimed, holder
is you). No exceptions: the Hall's "may be built on ground you do not hold"
rule died with the Hall, and so did adjacency, the builder-inside rule and the
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

- **Built on any resource node** — supply, iron, veilstone or veilsteel.
  The curse builds a node only in a territory it has **claimed**, by the units
  standing there; the node takes `nodeBuildSeconds` and does not lock while
  under construction.
- **Locks** the territory (§3), and **is destructible**. There are no
  indestructible pure nodes any more.
- **Fields the garrison** of its territory — the §2.13 army: whole-spawn every
  `armySpawnSeconds`, growing ×`armyGrowth` per spawn.
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
- **Locked** — it raids: resource buildings first, then any building, then
  walks home after `raidSeconds`. Razing the last locking structure unlocks the
  ground and the party can then claim it.

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
  - **every offensive curse force** (harassment, raid and claim parties) goes
    for the holder's territories and **ignores every other player**;
  - garrisons still defend their own territory against any intruder — the
    curse ignores non-holders, it does not let them walk in.

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
`armySpawnSeconds`, `armyGrowth`, `garrisonCap`, `expansionSeconds`,
`mergePartySize`, `raidSeconds`, `shardrootChance`.

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
