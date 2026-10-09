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
a merge party from that territory, raising a curse node on it. ~~Only the
Shardroot hunt outranks this.~~ The hunt no longer competes for this slot
(2026-10-07, §6.6).

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
- **Player ground during the opening grace (2026-10-05)** — before
  `claimGraceSeconds` of match time, a territory a player holds (claimed,
  locked or not) is **not a target** either: the opening claims only
  unclaimed ground, so a player who starts beside the curse is not
  contested before the first attack wave. Each dispatch that the grace
  narrows logs `[CurseTerritory] GRACE: party held (before first wave)`.
  The Shardroot hunt (§6.6) ignores the grace, and a fill party never needs
  it (it works the curse's own ground). Default: the same second as
  `firstWaveSeconds`.

**Conquest focus (2026-10-07).** Developer directive: *"Curse should focus on
conquering and holding territories."*

- **Several claims at once.** Up to `maxConcurrentClaims` claim and fill
  parties may be out together (it used to be one: a single takeover was the
  curse's whole attention). Each expansion check still sends at most one
  more, and a territory or node a party is already on is never picked twice.
- **Reconquest first.** Among the territories it can take, the curse prefers
  ground it **held and lost**, then **unlocked player ground**, then natural
  ground; within the preferred group the pick is the one seeded draw the
  check always makes (the same on every peer, whichever group wins).
- **Locked ground is taken by conquest waves** (§6.8), not by claim parties:
  a wave breaks the lock, then stays and claims.
- **Holding.** No draft — party, wave, hunt or guard — takes a node's
  garrison below `garrisonMinPerNode` (§6.8), so every held node keeps its
  defenders while the curse advances.

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
- **The curse hunts the holder (revised 2026-10-07).** Developer directive:
  *"Curse defends the Shardroot and focuses its attention on the player who
  owns it."* While any player holds it (carrier, hero or enshrining Temple):
  - the curse's garrison size and spawn rate rise by `shardrootCurseBonus`
    (size × (1 + bonus), interval ÷ (1 + bonus));
  - **hunt parties on their own clock.** Every `shardrootHuntSeconds` (the
    first at once when a player takes it) a hunt party of
    `shardrootHuntPartySize` (× the bonus) is drafted from the garrisons
    nearest the holder and presses the holder for as long as they hold it,
    then walks home. At most `shardrootMaxHunts` are out at once. The hunt
    **no longer takes the expansion slot**: claims and fills (§6.5) go on
    while the curse hunts;
  - **every attack wave goes for the holder** (§6.8), and waves come faster:
    the interval is multiplied by `shardrootHolderWaveIntervalMult`;
  - **garrisons go for the holder first**: a garrison whose node has the
    holder within `shardrootHolderAggroRadius` attacks the holder ahead of
    any other intruder (its `guardLeashRadius` still holds). Garrisons still
    defend their own node against anyone else — the curse ignores
    non-holders, it does not let them walk in.
- **The curse guards the Shardroot on the ground (2026-10-07).** Curse units
  still cannot pick the artifact up. While it lies on the ground (a dead
  carrier's drop, a ruined Temple's), the curse drafts a **guard** of
  `shardrootGuardPartySize` from the garrisons nearest it, topped up every
  check. The guard stands Defensive on the artifact: it engages hostiles
  within `shardrootGuardRadius` of it and drops a fight past
  `shardrootGuardLeashRadius` — it denies the pickup by fighting near it.
  A hunt party that has just put the artifact back on the ground joins the
  guard. When a player takes it, the guard **becomes a hunt party** on the
  holder; when it leaves the ground any other way, the guard rejoins the
  garrison.
- **A curse bearer stays home.** The curse unit that carries the Shardroot
  out of a spawn is never drafted into a party, guard or wave: it stays with
  its garrison, so the artifact sits behind the curse's defences.

### 6.7 The curse defends; it advances only to take (2026-10-03)

The curse is a **defensive** force that spreads, not an army that hunts.

- **Garrisons guard their node.** A garrison unit engages only hostiles
  within `guardRadius` of the node it guards, and never chases past
  `guardLeashRadius` from it: past that it drops the fight and walks back.
  It no longer sweeps its whole territory for intruders. A unit whose node
  dies adopts the nearest live node in its territory.
  **A garrison unit stands Defensive (2026-10-05)** — it only returns fire
  on its own; every other engagement is the guard order above (an
  attack-move on an intruder inside `guardRadius`). Why: on the default
  Aggressive stance an idle defender auto-acquired anything in its own line
  of sight, so a defender standing at the edge of its guard radius shot
  player buildings up to `guardRadius` + its sight away and walked out to
  `guardLeashRadius` to finish them. That was the curse "raiding" the
  extractors of whoever started beside it (the 2026-10-05 Sundered Crown
  log: one Gatherer's Hut 35 m from a node was destroyed ten times before
  the first wave ever formed). The guard order looks for intruding UNITS
  in the garrison's own territory, and a player building cannot stand on
  curse ground, so a building is engaged only when it shoots at a defender
  (a tower) or stands in the path of a guard order's attack-move. Drafted
  units (claim, fill and hunt parties, attack waves) stand
  Aggressive while drafted and go back to Defensive when they rejoin a
  garrison. This is the curse's exception to Stances.md §4.
- **Only claim parties and attack waves leave home** (waves since
  2026-10-04, §6.8). A claim party goes only for an adjacent, unlocked
  territory it can take (§6.5), or to fill a free node in ground it holds;
  an attack wave marches on a player (§6.8). Both are DRAFTED out of the
  garrisons (never below `garrisonMinPerNode` a node) — the garrison is the
  curse's only spawner. A unit still on garrison duty never leaves: the
  rules above are unchanged by waves.
- **The Shardroot hunt and guard** (§6.6, revised 2026-10-07) are the two
  other forces that leave home: hunt parties go for the holder, and a guard
  stands over the artifact while it lies on the ground.
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
- A party wants `mergePartySize` units for a claim or fill,
  `shardrootHuntPartySize` for a hunt and `shardrootGuardPartySize` for the
  Shardroot guard (claims, fills and hunts × the §6.6 bonus while someone
  holds the Shardroot). The unit bearing the Shardroot is never drafted. It takes them from the sending
  territory's garrisons first, then from the guard posts nearest it. If
  there are not enough spare units, the party goes **smaller**; if there are
  none at all, it is **skipped** (logged either way).
- Drafted units leave the garrison count, so their node fields them again
  at its next army spawn (§6.3) — that regrowth is what feeds the next
  draft.

**Attack waves.**

- **When.** The first wave forms at `firstWaveSeconds` of match time, then
  one every `waveIntervalSeconds` (× `shardrootHolderWaveIntervalMult` while
  a player holds the Shardroot, §6.6). The clock runs on whether or not a
  wave could actually be sent.
- **Whom — the strong carry the curse (2026-10-05).** Supersedes the
  nearest-player rule and its `waveTargetDistanceSlack` rotation, which the
  2026-10-05 Sundered Crown log showed feeding the weakest player: the
  Easy AI, whose base sat nearest the curse, took four of six waves (24,
  43, 63 and 75 units) while the Hard AI that had just eaten two
  neighbours took none. Now:
  - Every living player (a standing building) has a **strength**: its
    army power share (the sum, over its combat units, of
    √(damage per second × current HP) — the same geometric mean
    `UnitPower` uses, on live stats) blended with its territory share by
    `waveShareTerritoryWeight` (0 = army only, 1 = territories only).
  - Its **wave share** is that strength, floored at `waveShareFloor` so
    nobody is ever forgotten, renormalised to sum to 1.
  - Shares accrue as **credit**; each wave goes to the eligible player
    with the most credit (faction order breaking ties, the same on every
    peer) and costs that player one wave. Over a match each player's
    count of waves converges on their share: the strongest get the most,
    the weakest the fewest — a deterministic schedule, no draw.
  - **Cooldown.** A player is not eligible while a wave is out against
    them, nor for `wavePlayerCooldownSeconds` after it turned home. When
    every living player is cooling down, the slot is **skipped**.
  - **While a player holds the Shardroot, every wave goes for the holder**
    — §6.6's "every offensive curse force" includes waves. The holder hunt
    ignores shares and cooldown and drafts the full `waveDraftFraction`.
- **How big — sized to the target (2026-10-05).** The ceiling is still
  **`waveDraftFraction` of all the curse's garrison units**; under it the
  wave is drafted, nearest the target first (guard posts by distance to the
  objective, entity order breaking ties — the same on every peer, never
  below `garrisonMinPerNode` a node), **only until its own power reaches
  `waveSizeVsPower` × the target's army power × the target's difficulty
  multiplier** (below), and never smaller than `waveMinSize`. A player with
  no army is met by `waveMinSize` units, not by a third of the curse. If
  the ceiling itself comes to fewer than `waveMinSize`, the slot is
  **skipped**, not banked.
- **Difficulty.** `waveSizeByDifficulty` is a four-entry table — Easy,
  Normal, Hard, Expert — multiplying the wave-size budget against a player
  by that player's AI difficulty (the lobby slot's). **Human players count
  as Normal.** Default: Easy lighter, Normal and Hard as authored, Expert
  heavier; the values are on the asset. It changes how big a wave is, not
  how often a player is chosen (strength already does that).
- **Log.** Each slot logs the shares (`WAVE shares — <faction> <share>
  (army <power>, <territories> terr)…`) and each launch
  `WAVE n -> <faction> (share s, size k vs power p)`.
- **What it attacks.** The target player's **nearest territory** — the one
  holding their building nearest the curse — and in it, their nearest
  production building (anything that trains units) for preference, else
  their nearest building there. The wave marches in formation, attack-moving,
  and fights. Whenever it stands idle it re-targets: the nearest hostile unit
  in that territory, else the nearest of the player's buildings there, else
  the player's nearest building anywhere (whose territory becomes the new
  one). A wave hunting the Shardroot holder re-aims at the holder instead.
- **Conquest waves (2026-10-07, `conquestWaves`).** When the target holds a
  **locked** territory that borders curse ground, the wave marches on that
  territory instead — the one whose locking structure stands nearest a curse
  node — and its objectives are **the structures that lock it** (extractors,
  Trading Outposts, Fortresses — §3). Idle, it goes for the next locking
  structure there before any unit. The moment the territory is **unlocked**,
  the wave does not go home: it **becomes a claim party** on it (§6.5 — it
  stands until the meter turns, then raises a curse node on a free resource
  node). That claim is not counted against `maxConcurrentClaims`: the ground
  was won in battle. Logged `[CurseTerritory] CONQUEST — …`.
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

**The cap.** ~~Live curse units never exceed `maxCurseUnits`.~~
**The cap follows the ground (2026-10-07).** Developer directive: *"Curse
unit cap should be proportional to territories held (max of 1000 units)."*
Live curse units — every curse creature, whatever raised it — never exceed
`curseUnitsBase` + `curseUnitsPerTerritory` × the territories the curse
holds, and never `maxCurseUnits` (the ceiling, the directive's maximum).
A curse that conquers fields more; a curse driven back fields less — its
surplus is not culled, it is simply not replaced. The held count is the
ownership meter's, so every peer reads the same cap. There is **one gate**: every spawn path asks
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

**One exception (2026-10-09): ascension** — a faction whose enshrining
Temple survives the Shardroot's 10-minute countdown wins
([Curse_And_Shardroot.md §3.1c](Curse_And_Shardroot.md)).

A faction is eliminated by **any** of three rules (`EliminationSystem`, all
checked on the lockstep clock, so every peer drops the same faction on the
same tick):

1. **No lifeline** — no Fortress, no military building and no Worker (the
   original rule, with Fortress in place of Hall).
2. **No territory** (2026-10-08, developer: "Players die when they have no
   territories.") — the faction has held **no territory at all** for a
   continuous grace period (`noTerritoryGraceSeconds` on
   `EliminationSystem.asset`, beside `EliminationSystem.cs`). Holding one
   territory again at any moment inside the grace cancels it; the clock starts
   over the next time the last territory is lost. The rule arms only once the
   faction has held ground at least once, so a start that has not been claimed
   yet is never a death sentence. Ground is the economy (§1) and the only
   place a faction may build (§5); a faction with none can never recover it
   (claims need ground bordering Fortress-linked territory, §10), so it is
   already out of the game — the rule ends the match for it instead of
   letting a landless remnant linger for an hour.

3. **No buildings** (2026-10-09). Developer approved: "OK". The v8 batch ended
   two of three matches at the 90-minute cap because of **zombie factions** —
   Blue (match 2) held one territory from minute 60 to 90 with **0 buildings
   and 3 units**; Red (match 3) held two territories with 0 buildings and 3
   units. Standing units keep a territory's ownership meter, so rule 2 never
   fired, and no AI sends an army after three stragglers. As in StarCraft, a
   faction with **no buildings left** (finished or under construction; plans
   do not count, Planned_Buildings.md) is eliminated after the same
   `noTerritoryGraceSeconds` grace; placing any building inside the grace
   cancels it.

**What happens to an eliminated faction's leftovers** (all rules alike): every
unit and building it still owns is destroyed on the tick of elimination
(Health 0 — DeathSystem does the destruction, as for any death). Nothing of an
eliminated faction stays on the map, so a remnant army can never stand on a
territory and claim or hold it, and a surviving building can never lock one.
This is the convention the lifeline rule already had; the territory rule keeps
it.

**Superseded in timing (2026-10-09):** developer: "I need deaths to start occurring from minute 15 or even earlier", with ~45-minute 8-player matches. The late game now comes earlier (the minute is `allInAfterSeconds` on `SimpleAISystem.asset` and `nodeLifetimeMinutes` on `TerritoryIncomeSystem.asset`), and stalled factions are hunted before it ([Game_AI.md § 6o](Game_AI.md)). Read "minute 30" below as "the late game".

**Late game.** Late game starts around minute 30, and from there players are
expected to start falling (developer, 2026-10-08). The AI's side of that is the
all-in doctrine ([Game_AI.md § 6n](Game_AI.md)).

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
`waveRetreatFraction`, `maxCurseUnits`, and (2026-10-05, §6.5, §6.8)
`claimGraceSeconds`, `waveShareTerritoryWeight`, `waveShareFloor`,
`wavePlayerCooldownSeconds`, `waveSizeVsPower`, `waveSizeByDifficulty`, and
(2026-10-07, §6.5, §6.6, §6.8) `curseUnitsBase`, `curseUnitsPerTerritory`
(`maxCurseUnits` is now the ceiling), `maxConcurrentClaims`, `conquestWaves`,
`shardrootHuntSeconds`, `shardrootHuntPartySize`, `shardrootMaxHunts`,
`shardrootGuardPartySize`, `shardrootGuardRadius`, `shardrootGuardLeashRadius`,
`shardrootHolderAggroRadius`, `shardrootHolderWaveIntervalMult`.
`waveTargetDistanceSlack` is retired with the nearest-player rule.
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
a random legal spot, 9 m from the territory's other nodes and outside every
**start clearing** (below) — from a per-territory seeded stream, so every peer
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

**The curse starts somewhere new each match (2026-10-07).** The authored
curse territories (Veilstone rich) swap types with territories drawn from
the match seed (`randomizeCurseTerritories`, TerritoryResources.asset). On a
mirrored map the draw is one territory and its three mirror images, so every
seat faces the same curse; it is never a home or a territory bordering one.
The log names them: `the curse starts in … this match`.

**Home nodes line the edge (2026-10-07).** In a home territory every node
stands at least `homeNodeMinOffset` (TerritoryResources.asset) from the start
on one axis or the other — a square band along the territory's edge — so
the AI's main camp and its wall ring (Game_AI.md § 6g) are never built round
a node. Applies to every map; a home too small for the band comes up short
and says so in the log.

**The start clearing (2026-10-06).** Every player start opens on clear,
walkable ground. Within `StartClearing.asset` → `radiusCells` build cells of
each start position (where the Fortress was placed), in whatever territory
that ground lies:

- **no resource node stands** — supply, iron, veilstone or veilsteel, and so
  no curse node either (the curse rises only on resource nodes). It is a
  node-site legality rule (`ResourceNodeSite`), so generated nodes, mirrored
  copies, marker-path nodes and the curse's later precipitation all obey it;
- **no impassable paint survives** — the `NoWalk` terrain layer is removed
  from the circle and its weight handed to the dominant walkable layer
  around the start, so the ground looks walkable as well as being walkable,
  and the passability grid and nav cost field are re-derived for it;
- **no trees, and no forest stand blocks a cell.**

The heightmap is not touched: ground blocked by the incline budget or the
water line (a cliff, a lake) stays blocked — that is a map-authoring matter.
The clearing is applied once at match load, from the map's terrain data and
the sorted start positions, so every lockstep peer clears the same cells. A
home too small to fit its nodes outside the clearing comes up short and the
generator logs it, as for any thin territory. Implemented in `StartClearing`
(`Scripts/World/Terrain/`), called from `SpawnDelayHelper` before the nature
regions, reachability and node generation.

**An authored Start territory stays a Start (2026-10-05)** whether or not a
player is seated there. It used to be re-dealt as an ordinary type when no
start stood in it, so a two-player match on a four-seat map changed the empty
corners, the quadrants of a mirrored map stopped matching, and the node layout
fell back to random (one home's outcrop landed where nothing could reach it).
The map author's seats keep their layout for any player count.

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

### 11.2 Node purity (2026-10-07)

Developer: "Add 3 levels of purity to resource nodes. 1 - Pure produces 150%
of resources, 2 - Normal produces 100%, 3 - Poor produces 50%. Starting node
resources are all pure. The rest is a mixture: 2 pure nodes for every 3
normal and for every 10 poor."

- Every generated node — supply, iron and veilstone alike — has a **purity**:
  Pure, Normal or Poor. It scales everything the node's slot pays, empty or
  built, at every extractor level and through every research multiplier; a
  node pays (and drains its reserve) at its purity's rate. (The Fortress
  level no longer scales slots at all since 2026-10-08, § 11.3.)
- **Start territories are all Pure**, seated or not, so the opening is the
  same as before purity existed.
- **Every other node is dealt from a shuffled bag** of 2 Pure : 3 Normal :
  10 Poor, refilled when empty, in territory order from the match seed, so
  every peer deals the same grades and the mix is exact over each 15 nodes
  rather than likely. On a mirrored map a node's mirror images take ITS
  purity, so every seat still opens on the same ground.
- A node made any other way (scenario fixtures, curse refills of an emptied
  outcrop keep the node they refill) is Normal unless it was dealt a grade.
- The purity is shown with the node's name (Pure / Poor; Normal is unmarked).
- The three multipliers and the bag's mix are on `TerritoryResources.asset`.

### 11.3 Nodes run dry (2026-10-07)

Developer: "Why does the economy stop mattering? Add decay to the nodes."
In the 8-player batch every faction reached the population cap by about
minute 40 and the resource cap soon after: income kept rising on research,
Fortress levels and breadth while there was nothing left to buy, so losing
an army cost nothing. Iron nodes only thinned to a permanent floor, and
supply nodes never thinned at all.

- **Every supply and iron node has a reserve**, and every unit a slot pays
  (empty or built, at its purity) is drawn from it. Veilstone keeps its own
  rule (fast and finite, Veilstone_Economy.md).
- **Yield falls with the reserve and stops when it is gone** -- a worked node
  runs dry. The late game is then paid for by taking fresh ground, not by
  sitting on old ground.
- **Purity sets the reserve as well as the yield** (developer: "Pure supply
  must have higher yield and last longer than poor ones"). A Pure node holds
  much more than a Normal one -- more than its higher yield draws -- and a Poor
  node holds less, so Pure pays more AND lasts longer, Poor pays less and
  runs out first.
- **Better extraction drains faster** (developer: "higher tech buildings
  means you drain the nodes faster"). Everything that raises what a slot
  pulls out of the ground is taken from the reserve: the extractor's level
  and the Mine research.
- The reserve sizes, the per-purity reserve multipliers and the yield floor
  are on `TerritoryIncomeSystem.asset` (a floor above zero brings back a
  permanent trickle).

**Every node is spent by minute 30 (2026-10-08).** Developer: "Late game
comes at 30 minutes; from there onward players are expected to start
falling. Adjust all nodes to be spent by minute 30."

- **A node has a lifetime as well as a reserve.** Besides what extraction
  draws, a node's reserve is capped by a line that falls from its full
  reserve at the start of the match to nothing at `nodeLifetimeMinutes`
  (`TerritoryIncomeSystem.asset`) of **simulated match time** — the
  lockstep clock, so every peer caps the same nodes on the same tick. Its
  yield follows what is left as before, so every node's pay — supply, iron
  and veilstone, empty or built — falls to nothing by then. Heavy
  extraction empties a node sooner; a Poor node, with the smallest reserve,
  first; an untouched node simply fades out on the line.
- **An empty slot on a spent node pays nothing** either; with the yield
  floor at zero there is no permanent trickle.
- **Veilstone keeps its own shape**: an outcrop pays its full rate while
  anything is left, so under the line it pays at the rate the line falls,
  and it is Depleted when the line reaches zero. **The curse's refill of an
  outcrop is capped by the same line** (Veilstone_Economy.md §2), so after
  minute 30 the curse taking an outcrop cannot resurrect its income. A
  Depleted outcrop still hosts its Trading Outposts — late veilstone is
  trade.
- **What pays after minute 30** is what draws on no node: the Fortress's
  own income (raised by its level), the Vault, trade, **territory claims**
  (a flat rate per held territory once its holder has aged up) and the
  Alanthor **Guild surveys** (Veilstone_Economy.md §5).
- **The Fortress level no longer scales slots.** It used to multiply
  everything its territory paid — Mines included — which made iron soar in
  the middle of the match (developer: "reduce the upgrade power"). It now
  multiplies only that Fortress's own supplies income, and the surveys no
  longer multiply the Mines (they pay the Guilds instead).

