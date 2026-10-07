# Age 0 — Tech Tree

> Design document for **Age 0** (the starting age): its buildings, units,
> techs and the RULES and REASONS behind them.
>
> **Data lives on the SOs (2026-10-03, [Unification decisions](Unification_Decisions_2026-10-03.md)).**
> Every number — cost, HP, damage, armour, range, line of sight, speed,
> train / build / research time, population, footprint, income, interest,
> multiplier — is authored on the entity's ScriptableObject under
> `Assets/GameData/TechTree/Age0/` (and the culture folders for levels), and
> **on a conflict the SO wins**. This doc does not restate them. To read the
> numbers, open the generated calculator `tools/calculator/TechTree.html`
> (built from the SOs by `tools/gen_calculator.py`). `Resources/TechTree.json`
> is gone; there is no JSON fallback.
>
> **See also:** [Overview.md](Overview.md) for the game-wide framing (two-age
> structure, culture focuses, Petriarchy / sect system).
>
> Resources used in Age 0: **Supplies**, **Iron**, **Veilstone**. Veilsteel and
> Glow do **not** appear in Age 0 costs (**veilsteel is never an Age 0 cost** —
> the rule that turned Fervored Masses into a veilstone-only price, decision 37).

---

## The curse in Age 0 (2026-08-03 — exposure model, see [Curse_And_Shardroot.md §2.5b](Curse_And_Shardroot.md))

Age 0 projects **no influence**, so it gets its own curse layer:

- **Capital hearth** — every Shelter / Fortress radiates a fixed, small **veil-suppression
  circle** (the curse cannot grow inside it and existing haze decays).
  Veil-only: no territory claim, no combat aura.
- **Mining corruption** — mining out a veilstone node has a **15 %
  chance** of transforming it into a **resistant curse node** (Sporeling)
  that immediately hazes the whole patch, invalidating it. Kill it (a
  real military investment — it is deliberately tough), starve it under
  suppression, or abandon the patch. Killing it collapses the growth and
  reclaims the patch — **it pays no veilstone** (2026-09-11: killing a blight
  source never spawns nodes). Nodes on **suppressed
  ground** (hearth ring or any player influence) **never corrupt** —
  secured mining is guaranteed safe. The starting army has **no
  Catapult**.
- **Veilstone sourcing** — map patches are the mining base (authored
  markers, or equivalent self-provisioned starter patches on markerless
  skirmish maps — progression hard-gates on veilstone, so a map must
  never start with zero sources); beyond them the Veil precipitates
  nodes (corruption residue, recede residue, frontier eruptions).
- **Ward** *(planned, not yet implemented)* — a cheap Age 0 building with
  a suppression-only circle, for deliberately extending the secured ring
  before culture influence exists.

---

## Conventions

- In Age 0 the player has **not yet picked a culture**, so the standard
  buildings (Shelter, Barracks, House, Gatherer's Hut, Mine) exist only in
  their **level 0 / pre-culture form**. They have no Age 0 upgrade ladder —
  their levels 1-3 are the **cultured form** that lands at age-up, authored as
  level SOs in the culture's folder (for Alanthor, see
  [Age_1_Alanthor.md § Building levels](Age_1_Alanthor.md)). See
  [§ Age-up transitions](#age-up-transitions) for the rename mapping.
- **Level 1 is free at age-up** for every cultured building (decision 27):
  the culture pick promotes level 0 to level 1 at no price; levels 2 and 3
  are bought.
- **Tech level gates:** a tech's `minBuildingLevel` on its SO is the **only**
  level gate (decision 21). Age 0 buildings are level 0, so every tech an Age 0
  player can see has no level gate.
- **Population**: a unit's population cost and a building's housing are SO
  fields (`popCost`, `populationProvided`).
- **Damage formula** (AoE4-style, [Combat_Pacing.md](Combat_Pacing.md)):
  `final = max(1, baseDamage - armor) + bonusVsTags`. Armour per damage
  column and the `bonusVsTags` counters are on the unit SOs.

---

## Starting position (decision 19, 2026-10-03)

Every player starts with:

- one **Shelter** (the capital) and one finished **House** beside it
  ([Territory_Claims.md §4](Territory_Claims.md));
- **5 Spearmen, 1 Scout and 3 Workers.** **No Archers** — Age 0 is the melee
  age (the bow arrives with the Age 1 Archery Range). Spawned by
  `PlayerSpawnSystem`.

The opening population cap is the Shelter's housing plus the House's, both
from their SOs.

---

## Buildings

### Shelter — the capital (Fortress from age-up) (2026-10-03)

> **2026-10-03:** the capital is one building with two names — the Shelter
> in Age 0, the Fortress from age-up (decisions 4-5d).

**Every player starts with a Shelter.** It is the capital: it trains Workers
and Scouts, researches the economy bench, earns its own supply income (its
SO's income — there is no separate territory line for it, decision 10) and
claims its home territory. At age-up it **automatically becomes the
Fortress** — the same building, renamed, for every culture. The Fortress has
**levels L1-L3** (for Alanthor the level SOs read "Fortress - Lvl N"). There
is no territory limit for the levels to raise (Territory_Claims.md §10,
2026-10-04).
Internally the building id is `Fortress` in both ages (`HallTag` survives as
an internal component only).

**It is buildable, one per territory** ([Territory_Claims.md](Territory_Claims.md)
§4), and it **locks** the territory it stands in. Cost, HP, defence,
population, footprint and income live on the `Fortress` SO
(`Age0/Buildings/Fortress/Fortress.asset`).

#### Trainable units

Listed in the Fortress SO's `trains[]`:

| Unit | Who | Notes |
|------|-----|-------|
| **Worker** | everyone | The one worker unit — it builds (see Units). |
| **Scout** | everyone | |
| **Ledger** | Alanthor, after age-up | Level gate on the unit SO. |
| **King Lexor** | Alanthor, after age-up | Hero; level gate on the unit SO. |

#### Researchable techs

Every tech below has `researchAt: Fortress`; the Alanthor-only ones are
culture-gated and appear only after an Alanthor age-up. Prices, times,
effects and gates are on the tech SOs.

| Tech | Who | Effect (rule) |
|------|-----|---------------|
| **Stone Tools** (`StoneTools`) | everyone, Age 0 | workers **build faster** |
| **Armed Scouts** (`ArmedScouts`) | everyone, Age 0 | **arms Scouts**: sets their damage, for Scouts already on the field **and every Scout trained afterwards** (decision 13). Until then Scouts are unarmed and vision-only |
| **Iron / Veilstone / Veilsteel Tools** | Alanthor | further **build speed** tiers ([Age_1_Alanthor.md](Age_1_Alanthor.md)) |
| **Mason Guild** | Alanthor | faction-wide building HP |
| **Scouting Celestarii** | Alanthor | `ScoutingCelestarii` — Use Celestar + full Scout vision |

There is **no age-up research** — the age-up is the landmark (see
[§ Age-up by landmark](#age-up-by-landmark-2026-09-29)).

> **Removed (2026-07-20):** the *Wheel cart* carry-capacity tech. Workers
> never carry resources and there are no drop-off buildings.

---

### Barracks — level 0 (pre-culture)

Trains melee units. At age-up the Barracks is **renamed and reskinned** to its
cultured form (`Garrison` / `Route Guard` / `War Hall` — see
[§ Age-up transitions](#age-up-transitions)); the cultured form starts at
level 1 (free) and has its own upgrade ladder. In Age 0 only the pre-culture
level 0 form exists. Its stats are on `Age0/Buildings/Barracks/Barracks.asset`.

#### Trainable units

| Unit | Notes |
|------|-------|
| **Spearman** | The Age 0 line unit and the anti-cavalry leg of the triangle |

#### Researchable techs

| Tech | Effect (rule) | Code id |
|------|---------------|---------|
| **Conscription** | units train **faster at any Barracks** (faction-wide; decision 14) | `Conscription` |
| **Stone Weapons** | melee weapon tier 1 — a faction-wide melee damage bump (per-battalion upgrades are dropped, decision 31) | `StoneWeapons` |

---

### Mine and Veilstone Mine — Age 0, every culture (2026-10-03)

**The iron Mine and the Veilstone Mine are Age 0 buildings for everyone**
(decision 15; supersedes the 2026-08-13 move of the Mine to Feraldis Age 1).
Each stands on its own node — the Mine on an iron deposit, the Veilstone Mine
on a veilstone outcrop ([Build_Grid.md § 3](Build_Grid.md)). The SOs live in
`Age0/Buildings/Mine/` and `Age0/Buildings/VeilstoneMine/`. They are priced in
supplies and veilstone, never iron. Each pays its slot-income ladder
([Veilstone_Economy.md §5](Veilstone_Economy.md)). The Mine hosts its own
research (Deep Shafts, Rich Seams). At age-up **Alanthor's Veilstone Mines
become Trading Outposts** (Alanthor never mine veilstone; their iron Mines
stay).

---

### Archery Range — not an Age 0 building

> **MOVED TO AGE 1 (2026-08-11):** the Archery Range requires **era 2**
> (`minEra: 2` — enforced for the player in the build UI and for the AI in
> `SimpleAISystem.TryBuildBuilding`). Playtest-proven: the Age-0 archer
> rush was uncounterable — massed archers ended a match by minute 15 with
> nothing in the age able to answer them. **Age 0 is a melee age**:
> spearmen hold the line until the culture pick brings the bow.

The building, its Archer / Crossbowman / Longbowman ladder (unlocked at
levels 1 / 2 / 3) and its research are documented in
[Age_1_Alanthor.md § Archery Range](Age_1_Alanthor.md); its SO lives in
`Civs/Alanthor/Buildings/ArcheryRange/`.

---

### Gatherer's Hut — level 0

Early supply generation. The hut stands **on a supply spot** (its footprint is
the spot's, [Build_Grid.md](Build_Grid.md)) and pays **only its territory
slot-income ladder** (decision 9: no construction safety-net income). Stats
and the ladder are on `Age0/Buildings/GatherersHut/GatherersHut.asset`. No
trainable units.

#### Researchable techs

> **The Gatherer's Hut hosts NO research in Age 0 (2026-10-03, decision 16).**
> Its research lines are culture-gated and filed under the culture that gets
> them: the Alanthor Guild line (Iron Surveying, Veilstone / Veilsteel
> Surveys, Iron Reinforcements, Veilstone Walls, Veilsteel Pylons) under
> `Civs/Alanthor/Buildings/Guild/Research/` (see
> [Age_1_Alanthor.md](Age_1_Alanthor.md)), and the Feraldis Raiding / Plunder
> line under `Civs/Feraldis/Buildings/RaiderCamp/Research/`. Deep Gathering
> was removed 2026-08-04; Retaliatory Measures is cut (decision 18).

> **What reduces a hut's yield.** The hut's % indicator and its output are the
> same number: the fraction of its gather circle that is *productive ground*.
> A cell yields nothing when it is
> - **NoWalk / terrain-blocked** ground (also building- or obstacle-blocked),
> - **cursed** ground (veil saturation at or past the crust threshold),
> - **owned by a hostile player** (their influence channel dominates it at
>   >= 0.5 — allied ground still counts, so a shared border does not starve
>   both partners),
> - already claimed by an **older friendly hut** or **any enemy hut** circle, or
> - inside a **wall enclosure** polygon.

---

### House (id `Hut`) — level 0 (pre-culture)

Provides population in Age 0. The internal id is `Hut`
(`Age0/Buildings/Hut/Hut.asset`, `HutTag`); the display name is "House".
**No research** — population is its product. No trainable units.

- **Housing** is on the SO (`populationProvided`; decision 8), and the cultured
  levels' housing on the culture's level SOs. The faction-wide population
  ceiling is `FactionPopulation.AbsoluteMax`.
- **LIMIT (2026-10-02):** a faction may own at most `maxPerFaction` Houses at
  once — finished, rising or still a plan — enforced for every player at the
  command layer by `BuildingFactory.AtFactionCap`. The limit is sized so the
  population ceiling stays reachable with fully levelled Houses.
- **The AI keeps them in one quarter (2026-10-02):** its first House goes in
  the normal base ring; every later one is placed outward from the centre of
  the Houses it already has, with no centre spacing and no lane — Houses may
  stand wall to wall, so its housing is one residential block rather than huts
  dotted through the base.

At age-up the per-culture behaviour splits three ways (see
[§ Age-up transitions](#age-up-transitions)):

- **Alanthor** — renamed and reskinned to House (Alanthor) with its level
  ladder.
- **Runai** — **no House exists post-age-up**. Runai population is set to
  the game cap instantly at age-up; standing Age 0 Houses are removed.
- **Feraldis** — Houses remain but provide **no population** after age-up
  (Feraldis also gets the full cap instantly). Houses convert into pure
  **raider-spawn buildings**.

---

### Palisade — the Age 0 wall (2026-10-02 — supersedes "Wooden Wall", 2026-09-21)

> **The palisade is its OWN building, not level 0 of the stone wall.** Until
> 2026-10-02 the timber fence was `Alanthor_Wall` at level 0 and the Alanthor
> age-up re-clad it in stone. That is retired: a palisade is a palisade for its
> whole life, and the Stone Wall is a different building Alanthor gains at
> age-up ([Age_1_Alanthor.md § The stone wall](Age_1_Alanthor.md)).

Every culture can draw a palisade from the first minute. **At age-up, Alanthor
and Runai lose it** — the build button is gone, and nothing new can be added
to a palisade they already own (no Build Wall from a palisade hub, no Convert to
Hub on a palisade section). What they built **stays timber**: it keeps its HP,
it can still be turned into a gate, and it can be deleted. **Feraldis keep
building palisades for the whole game** — they never leave timber.

| Property | Rule |
|------|-------|
| Id | `Palisade` (hub, `Age0/Buildings/Palisade/Palisade.asset`). Its curtain sections, gates and seals are the shared wall pieces, carrying `PalisadeTag` |
| Who | **every culture in Age 0; Feraldis only after age-up** |
| Cost | a price **per hub plus a price per curtain module** (`PalisadeSegment.asset`), charged for the whole length when the fence is laid — supplies only. The gate conversion costs the same as the stone wall's gate (decision 36) |
| HP | hub and module HP on their SOs, never scaled — a palisade has no levels |
| Footprint | the hub's footprint is its SO's, **not grid-snapped** (the wall hubs are the buildings exempt from the build grid, [Build_Grid.md § 5](Build_Grid.md)). The curtain is freeform and **thin** |
| Walkable | **no.** A fence has no wall-walk; only the stone wall has a deck |
| Look | authored timber art — `Wall_segment.fbx` / `Wall_hub.fbx` in `Age0/Buildings/Wall/`, the palisade slot (0) of `WallModuleArt.asset` |
| Conversions | **Gate and Hub only.** No tower and no mounted engine. No placeholder cards |
| Joins | **palisade to palisade only.** A palisade never snaps to, auto-connects with, or is branched from a stone wall, and the reverse — they are different buildings |
| Placement | drawn, exactly as [Age_1_Alanthor.md § Drawing walls](Age_1_Alanthor.md) describes |

---

### Temple of Ridan — Age 0, one per faction

**The Litharch trains at the Temple of Ridan**, an Age 0 building costing
Religion Points ([Religion.md](Religion.md) §2; the price is on the SO). The
Temple also hosts the heal ladder — **Heightened → Pious → Fervored Masses**
(each requires the previous) and **Warrior Priests** — and the Temple's own
heal aura (`TempleHealSystem`). The Shrine of Ridan is gone (2026-10-02).

- **The Temple has no levels** (decision 20) and is limited to **one per
  faction** (`maxPerFaction`). Any older text that gated Pious or Fervored
  Masses on a Temple level is **void**: the techs' only gates are their
  prerequisites (and `minBuildingLevel`, which is unset).
- **Fervored Masses is priced in veilstone only** (decision 37) — no
  veilsteel, per the Age 0 cost rule.
- **Warrior Priests** gives the Litharch an attack; the Litharch has none
  without it.

---

## Age-up by landmark (2026-09-29)

**The age-up IS the construction of a landmark.** There is no age-up research
and no culture-choice dialog: the landmark you build decides the culture.

| Landmark | Culture | Demo |
|---|---|---|
| **Vault of Almiérra** | Alanthor | available |
| **Fiendstone Keep** | Feraldis | shown, disabled ("Unavailable in the demo") |
| **Thessara's Crossing** *(new building, design TBD)* | Runai | shown, disabled ("Unavailable in the demo") |

**The demo is 100 % Alanthor** — every player, AI included.

- **Cost** is the landmark SO's, paid on placement. Every start territory
  carries veilstone (Regions.md node quotas), so the price is reachable on
  every map.
- **Progress = construction.** The landmark self-builds over its SO build
  time; each worker on the site adds build rate (intended: workers buy a
  faster age-up). On completion the faction ages up to that culture.
- **One landmark per faction.** Placing one disables the other buttons for the
  rest of the match.
- **Destroyed before completion:** progress resets and **everything spent is
  lost**. The buttons return and the player may pay again.
- **Destroyed after completion:** the culture stays; the building is gone.
- Placement follows the ordinary build gate — owned territory only
  ([Territory_Claims.md](Territory_Claims.md) §5).

### The AI and the age-up (2026-10-02)

**Age 0 is for the age-up, not the war.** Before it has aged up an AI does not
attack and SAVES for its landmark:

- **Aggressive and Rush** send out exactly **one** attack wave in Age 0, then
  stop attacking and save. Their Age 0 army is what that wave needs. If that
  wave cannot launch, they start saving a fixed delay past their usual age-up
  push anyway — a rusher is never stuck in Age 0.
- **Every other personality** (Balanced, Defensive, Economic, TechBoom, Turtle)
  launches **no** wave in Age 0 and saves from the start; it keeps only a
  small defensive garrison.
- **Saving is strict.** While an AI saves for the landmark, its savings hold
  does not lapse (the usual duty cycle does not apply): only workers,
  houses, supply huts and the garrison floor may spend until the landmark is
  paid for.

(The wave size, garrison floor and delays are AI config values in
`Assets/GameSystems/AI/` — see [Game_AI.md](Game_AI.md).)

---

## The landmarks

> **Superseded 2026-09-29** in their placement and price by § Age-up by
> landmark above. Kept for the buildings' rules and research.

### Vault of Almiérra — the Alanthor landmark

A resource bank: deposited resources earn **simple interest** per minute on
the stored **principal**, and only on the part of it **up to a cap**
([Unification decision 41](Unification_Decisions_2026-10-03.md), 2026-10-05).
The principal is what the Vault holds as of the last deposit; the interest it
pays is added to the payout, never to the principal, so the yield grows in a
straight line and never compounds. Anything stored above the cap earns
nothing (it is still safe and still paid out on withdrawal). A withdrawal
takes everything and zeroes the principal.

**Interest applies from level 1 and grows with the Vault's level**
(decision 12), and **the banking-grade techs raise the rate**: the base rate,
the principal cap and the three grade rates are on
`Age0/Buildings/VaultOfAlmierra/VaultOfAlmierra.asset`
(`interestPerMinute`, `interestPrincipalCap`, `coffersRate`,
`merchantChartersRate`, `sovereignBondsRate`), and each level's multiplier on
`Civs/Alanthor/Buildings/VaultOfAlmierra/VaultOfAlmierra_Lvl1..3`. HP, upgrade
prices and times are on the same SOs. No trainable units.

**A hard time cap (2026-10-07):** a deposit earns interest for at most the
Vault SO's `interestMaxSeconds` (10 minutes); after that it earns nothing
until it is withdrawn and deposited again. Each deposit restarts the clock.

> Why simple and capped: under compounding the Vault was a money printer —
> one test saw 2,645 iron become 7.5 million eleven minutes later. The point
> of the Vault is a modest, safe return on idle resources, not an economy.

#### Researchable techs

The three banking-grade techs are **mutually exclusive tiers** — only one
banking grade is active at a time; researching a higher grade replaces the
active rate with that grade's rate from the Vault SO (the level multiplier
still applies on top). The resource-unlock techs widen what may be deposited.

| Tech | Gate (`minBuildingLevel`) | Effect (rule) |
|------|------|---------------|
| **Coffers** | none | banking grade 1 *(safe storage — "the Vault keeps coin")* |
| **Merchant Charters** | none | banking grade 2 *(active credit — "the Vault lends to traders")* |
| **Sovereign Bonds** | none | banking grade 3 *(high-stakes — "the Vault speculates")* |
| **Iron Subsidies** | none | Iron can be deposited |
| **Veilstone Monetization** | **Vault L2** | Veilstone can be deposited |
| **Veilsteel Bonds** | **Vault L3** | Veilsteel can be deposited |

---

### Fiendstone Keep — the Feraldis landmark (out of scope)

> **Left for the Feraldis pass** (decision 38): it will move out of `Age0/`
> then. Until that pass, read its stats on
> `Age0/Buildings/FiendstoneKeep/FiendstoneKeep.asset`. **The level gates this
> doc used to give its techs are void** — the tech SOs' `minBuildingLevel` is
> the only gate (decision 21), and only the Trebuchet emplacement has a rule
> on it: it requires the Ballista emplacement.

Fortified position: trains non-religious, non-siege military units, shoots
arrow volleys at enemies, and levels up by building **wings** (below). Its
techs: **Ballista emplacement** (adds a single-target siege bolt to each
volley), **Trebuchet emplacement** (adds a splash siege shot; requires the
Ballista emplacement), **Additional Towers** (more targets per volley),
**Reinforced Walls** (more HP).

#### Levels via WINGS (directive 2026-07-04)

The Keep levels up by BUILDING WINGS. The player chooses up to THREE wings
out of six (each wing type at most once):

| Wing | Effect |
|------|--------|
| **War wing** | Allows training of Barracks / Archery Range / Stable units at the Keep. |
| **Civic wing** | Keep generates Supplies and trains Workers. |
| **Engineers wing** | Gains ballista emplacements (extra bolts per volley) and more HP. |
| **Economic wing** | Behaves as a Gatherer's Hut with a larger area; economic buffs. *(v1: flat Supplies income)* |
| **Librarians' wing** | Additional researches available at the Keep (the capital's economy techs); speeds up research globally. |
| **Temple wing** | Allows training of every unlocked sect unit *(pending sect Unit lever, task-063 phase 2 — v1 trains Litharchs)*; yields Religion Points when built. |

Wing values are in `KeepWingConfig`.

---

## Units (Age 0)

Every stat is on the unit SO; the rules below are what the SOs must respect.

### Worker

The one Age 0 economy unit, and **the only builder** (decision: the Miner and
the Builder are gone; nobody gathers — income is territory, slots and
extractors). AI workers find building sites on their own; player workers need
an order, except that they auto-chain to nearby unfinished structures within
line of sight. SO: `Age0/Buildings/Fortress/Units/Worker/Worker.asset`.

### Scout

Extreme line of sight is the role. **Unarmed until Armed Scouts** (decision
13): a Scout has no damage and never auto-engages until that research
completes, after which every Scout — standing or newly trained — has the
damage the tech's effect sets. **Scout Sight:** a reduced share of LoS while
moving, ramping to the full radius while standing still and unharmed; before
**Scouting Celestarii** the settled maximum and the ramp speed are reduced,
and the research restores them (2026-08-02). Ramp values on the Scout Sight
ability SO.

### Spearman

The Age 0 line infantry (it replaced the Swordsman in Age 0; the Swordsman is
Alanthor's Garrison unit). Spear reach — slightly longer than a sword. Its
**bonus vs Cavalry** is the infantry leg of the counter triangle
([Combat_Pacing.md](Combat_Pacing.md)).

### Litharch

Trains at the Temple of Ridan. **A pure healer by default — no attack** until
**Warrior Priests** grants one. Even armed, a Litharch never goes looking for a
fight: it only returns fire on an attacker in reach and never chases
([Stances.md § 1b](Stances.md#1b-support-units)). Single-target right-click
heal (`healRange` on `Litharch.asset`); the Temple's aura heal is separate.

**Healer positioning:** walks to a stand-off point inside heal range on the
patient-to-Litharch line (never onto the patient); prefers wounded allies
**not in melee contact**; **steps away** from any armed enemy that comes
close; auto-searches wounded allies nearby. On Hold it heals only what is
already in range. A move order always wins. Tunables:
`LitharchHealingSystem.asset`.

---

## Age-up transitions

At age-up the pre-culture buildings standing on the map are renamed,
reskinned, and become **level 1 (free)** of their cultured variant. The
per-level data of those cultured forms is on the culture's level SOs; this is
only the rename map so the Age 0 build order can be planned forward.

| Age 0 building | Alanthor (lvl 1) | Runai (lvl 1) | Feraldis (lvl 1) |
|----------------|------------------|---------------|------------------|
| Shelter | Fortress | Fortress | Fortress |
| Barracks | Garrison | Route Guard | War Hall |
| House | House (Alanthor) — level ladder | *(no House — Runai get the full population cap at age-up; standing Age 0 Houses are removed)* | **House (Feraldis)** — a **raider-spawn building only** (no population). Every build / upgrade spawns autonomous Raider units that attack the closest enemy. |
| Gatherer's Hut | **Guild** (levels + the Guild research lines) | **transforms into a mobile caravan-wagon** the player drives outward to plant their first trade post | persists — can be upgraded to **Hunting Lodge** or **Logging Station** |
| Mine | Mine (Alanthor levels) | Mine | Mine |
| Veilstone Mine | **Trading Outpost** (Alanthor never mine veilstone) | Veilstone Mine | Veilstone Mine |
| Palisade | stays timber; no new palisade | stays timber; no new palisade | keeps building palisades |

> **The capital (2026-10-03).** The Shelter becomes the **Fortress** for every
> culture, automatically at age-up; there are no per-culture capital names.
>
> **The Archery Range is not a carryover.** It is gated to **era 2** and never
> stands in Age 0, so there is nothing to rename (2026-08-27). Runai's
> Arrowyard and Feraldis's Thrower Camp are their own buildings.

> **Transform, don't replace.** This is the cross-faction rule for the
> Gatherer's Hut at age-up — see [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace).
> The huts the player invested in during Age 0 *become* the seed of each
> faction's mechanic; they do **not** despawn.

**Feraldis special case:** in addition to the Hunting Lodge / Logging Station
upgrade paths, a subset of the Feraldis player's gatherers transform into
**raider/skirmisher units** at age-up that auto-patrol outward seeking
targets. This solves Feraldis's "cold-start" problem (damage-income only
works if there's something to damage). Floor mechanic: **The Border creatures
and nodes count as damage targets**, so an isolated Feraldis player can always
farm damage-income from the border layer without contacting another player.

The **landmarks** keep their names across cultures (no rename at age-up):
**Vault of Almiérra**, **Fiendstone Keep**. The Temple of Ridan likewise keeps
its name.

---

## Decisions (resolved 2026-05-19, revised 2026-10-03)

1. **Stone Weapons / Stone-Tipped Arrows** — originally "unlock a
   per-battalion upgrade ladder". **Superseded 2026-10-03 (decision 31):**
   per-battalion upgrades are dropped; every weapon / arrow / armour tier is a
   faction-wide tech whose effect is on its SO.
2. **Warrior Priests** — the Litharch has no attack by default; Warrior
   Priests grants one (values on the tech SO).
3. **Fiendstone Keep** — out of scope until the Feraldis pass (decision 38).
4. **Vault interest model** — originally compound. **Superseded 2026-10-05
   (decision 41):** **simple interest on the principal up to the SO cap**,
   applying from level 1 and growing with level (decision 12); the banking
   grades replace the rate. See § Vault of Almiérra above.
5. **Banking tier names** — **Coffers**, **Merchant Charters**, **Sovereign
   Bonds** (the three grades); Iron Subsidies / Veilstone Monetization /
   Veilsteel Bonds are the resource unlocks.
6. **Feraldis housing in early game** — Feraldis and Runai get the full
   population cap instantly at age-up; Feraldis Houses are raider spawners
   only ([Age_1_Feraldis.md](Age_1_Feraldis.md)). See
   [Overview.md § Population model](Overview.md).
7. **Gatherer's Hut on age-up** — huts do not despawn; they **transform** per
   culture (Guild / wagon / Hunting Lodge or Logging Station). See
   [§ Age-up transitions](#age-up-transitions).

## Remaining open questions

- **Ward** (planned) — design and price.
- **Thessara's Crossing** (the Runai landmark) — design TBD.
