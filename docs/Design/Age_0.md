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

**A player has one, ever** (decision 69, 2026-11-17 — it was buildable, one per
territory): when it falls it leaves rubble, it can be built again only there, for
a share of its price, and
a player whose keystones — Fortress, Temple, landmark and sect buildings, each of
which leaves rubble and rebuilds the same way — are all down for the rebuild grace is out
([Territory_Claims.md](Territory_Claims.md) § 7), and it **locks** the territory it stands in. Cost, HP, defence,
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
| **Scouting Celestarii** | Alanthor | `ScoutingCelestarii` — the Celestar passive: idle Scouts reveal random patches of the map (2026-10-19, [Age_1_Alanthor.md](Age_1_Alanthor.md#scouting-celestarii--the-celestar-passive-2026-10-19)) |

There is **no age-up research** — the age-up is the landmark (see
[§ Age-up by landmark](#age-up-by-landmark-2026-09-29)).

> **Removed (2026-07-20):** the *Wheel cart* carry-capacity tech. Workers
> never carry resources and there are no drop-off buildings.

---

### Barracks — level 0 (pre-culture)

Trains melee units. At age-up the Barracks is **renamed and reskinned** to its
cultured form (`Garrison` / `Route Guard` / `Longhouse` — see
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
| **Conscription** | units train **faster at every military building** (faction-wide: any building but the capital, for melee, ranged and siege units; 2026-11-04) | `Conscription` |
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
research (Deep Shafts, Rich Seams). Alanthor keep their Veilstone Mines at
age-up (decision 42; the Trading Outpost they once became is retired, [decision 55](Unification_Decisions_2026-10-03.md)). **The iron Mine levels L1-L3 after age-up** (2026-10-19), every
culture, faction-wide ([Age_1_Alanthor.md § Mine](Age_1_Alanthor.md),
[Overview.md § Building levels](Overview.md#building-levels-faction-wide));
the Veilstone Mine has no levels.

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
- **Runai** — standing Age 0 Houses **collapse** at age-up; a Hut built after
  it is the **Waystation** (no population; a watch tower that shoots, speeds
  caravans and taxes them veilstone — placed anywhere, at most 20 —
  [Age_1_Runai.md §4.3](Age_1_Runai.md), 2026-10-14). Runai population is set
  to the game cap instantly at age-up.
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

Every culture can draw a palisade from the first minute. **At age-up, Runai
lose it** — the build button is gone, and nothing new can be added
to a palisade they already own (no Build Wall from a palisade hub, no Convert to
Hub on a palisade section). What they built **stays timber**: it keeps its HP,
it can still be turned into a gate, and it can be deleted. **Feraldis keep
building palisades for the whole game** — they never leave timber. **Alanthor
keep building it too (2026-11-18)**: the developer's Age 1 Alanthor builder
layout puts the Palisade first in the Defense row, beside the Stone Wall
([Overview.md § The actions panel](Overview.md)); it stays the Age 0 timber
fence, with no levels.

**Feraldis: the Wooden Wall (2026-10-13).** At a Feraldis age-up every
palisade the faction owns is raised **automatically** to the Feraldis
**Wooden Wall** — its ONE cultured level, sturdier than the Age 0 fence. No
research, no button, no cost; every palisade raised afterwards stands at it
from the start. **It has no further levels and no upgrades.** Its name and HP
multiplier are on the level SO
`Civs/Feraldis/Buildings/Palisade/Palisade_Lvl1.asset` (the hub takes it
through the ordinary age-up level-1 bump; curtain modules, gates and towers
through `FeraldisPalisadeSystem`). The Wooden Wall's fittings are the **wall
tower and the gate** — the one palisade that carries a tower; never a mounted
engine ([Age_1_Feraldis.md § Walls](Age_1_Feraldis.md)).

| Property | Rule |
|------|-------|
| Id | `Palisade` (hub, `Age0/Buildings/Palisade/Palisade.asset`). Its curtain sections, gates and seals are the shared wall pieces, carrying `PalisadeTag` |
| Who | **every culture in Age 0; Feraldis and Alanthor (2026-11-18) after age-up — not Runai** |
| Cost | a price **per hub plus a price per curtain module** (`PalisadeSegment.asset`), charged for the whole length when the fence is laid — supplies only. The gate conversion costs the same as the stone wall's gate (decision 36) |
| HP | hub and module HP on their SOs; never scaled for Alanthor / Runai (their palisade has no levels). **Feraldis**: scaled once by the Wooden Wall level SO at age-up (above) |
| Footprint | the hub's footprint is its SO's, **not grid-snapped** (the wall hubs are the buildings exempt from the build grid, [Build_Grid.md § 5](Build_Grid.md)). The curtain is freeform and **thin** |
| Walkable | **no.** A fence has no wall-walk; only the stone wall has a deck. A right-click on a palisade with soldiers is an ordinary move to its foot (2026-10-20: the battlements of [Age_1_Alanthor.md § The stone wall](Age_1_Alanthor.md) are stone-only) |
| Look | authored timber art — `Wall_segment.fbx` / `Wall_hub.fbx` in `Age0/Buildings/Wall/`, the palisade slot (0) of `WallModuleArt.asset` |
| Conversions | **Gate and Hub only.** No tower and no mounted engine. No placeholder cards. **Feraldis Wooden Wall: Gate, wall Tower and Hub** — still no mounted engine |
| Joins | **palisade to palisade only.** A palisade never snaps to, auto-connects with, or is branched from a stone wall, and the reverse — they are different buildings |
| Placement | drawn, exactly as [Age_1_Alanthor.md § Drawing walls](Age_1_Alanthor.md) describes |

---

### Watch Tower (id `Lookout`) — Age 0, every culture, every age (2026-11-18)

Developer: "a tower that provides LOS but no attack, does not evolve to a
tower on age up. cheap." A small timber tower that only **sees**: it gives a
wide line of sight over its ground and does nothing else.

| Property | Rule |
|------|-------|
| Id | `Lookout` (`Age0/Buildings/Lookout/Lookout.asset`), displayed **Watch Tower**. The Alanthor tower that shoots (id `Alanthor_Tower`) is displayed **Battle Tower** since the same day |
| Who | every culture, in Age 0 and after; built by Workers from the Defense row of the build palette ([Overview.md § The actions panel](Overview.md)) |
| Does | line of sight only — **no attack, no garrison, no levels, no research**. Hit points low, line of sight wide, footprint small, price cheap and supplies-only: the numbers are on the SO |
| Where | **any ground** — inside or outside your territory, the curse's and rivals' included (developer, 2026-11-18); the only building exempt from the owned-ground rule (`TerritoryOwnership.TerritoryRefusal`) |
| Age-up | **nothing**: it is not converted, renamed or levelled at age-up and stays a Watch Tower all match |
| Look | the Age 0 timber tower form already authored for the Battle Tower (`Civs/Alanthor/Buildings/Tower/Art/Alanthor_Tower_Age_0.prefab`), referenced from the SO |
| AI | the AI does not build it |

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
- **The Temple's research LEVEL is the highest level among the faction's
  adopted sects** (decision 77, 2026-11-18, developer: "Litharch tiers unlock
  with the max level of sects adopted") — 0 until a sect is adopted. It is
  what `minBuildingLevel` on a Temple tech is checked against
  (`ResearchHostLevel`), by the panel, the AI and the research command alike.
  The Masses ladder and Warrior Priests keep `minBuildingLevel` 0.
- **What the Temple shows (2026-11-18, developer: "most actions should be
  invisible").** **Age 0: the Litharch and nothing else. From Age 1: the
  Litharch and Warrior Priests, and nothing else.** Everything else the Temple
  used to offer is HIDDEN, not greyed — the Masses ladder (so its heal aura
  never switches on), the Litharch upgrade ladder below, the Petriarchy's
  Support and Army purchases ([Monuments_And_Petriarchy.md](Monuments_And_Petriarchy.md)
  §5-§6) and the adopted sects' units. Their SOs and code stay; they are
  simply not offered. The lists are on `TempleOffer.asset`
  (`TempleOfRidan/TempleOffer.cs`), and the one rule is read by the panel, the
  AI research sweep, the research command (refused for everyone) and
  `ReligionPurchases.CanBuy` (the Petriarchy purchases), so the AI cannot buy
  what the player cannot reach. *Where the hidden features should live is an
  open question.*

---

## Age-up by landmark (2026-09-29)

**The age-up IS the construction of a landmark.** There is no age-up research
and no culture-choice dialog: the landmark you build decides the culture.

| Landmark | Culture | Demo |
|---|---|---|
| **Vault of Almiérra** | Alanthor | available |
| **Fiendstone Keep** | Feraldis | shown, disabled ("Unavailable in the demo") |
| **Thessara's Crossing** ([Age_1_Runai.md §2](Age_1_Runai.md)) | Runai | edition C only; shown, disabled in A and B |

**The demo is 100 % Alanthor** — every player, AI included.

- **Where they are placed.** All three show, in this order, on the fourth row of
  the Worker's build palette in Age 0 (2026-11-17), one the edition does not
  ship greyed with the reason; they leave the palette once one is placed. The
  palette is the only place: the top-of-screen landmark buttons and age-up pill
  were removed the same day.

- **Cost** is the landmark SO's, paid on placement. Every start territory
  carries veilstone (Regions.md node quotas), so the price is reachable on
  every map.
- **Progress = construction.** Workers build the landmark like any other
  building (2026-11-17, developer: "Completely remove the auto build from the
  vault" — it used to build itself over its SO build time, and a free,
  self-building rebuilt Vault kept a beaten faction alive); more Workers, a
  faster age-up. On completion the faction ages up to that culture.
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

> **2026-11-19, [Unification decision 85](Unification_Decisions_2026-10-03.md): THE BARS PAY A
> TRICKLE.** Developer: "Instead of being a 5% multiplier, it acts as a trickle income of
> whatever resource is stored, and it can't store supplies. No resource type locked behind
> research." This block replaces the bonus model below wherever they disagree.
>
> - **A bar stores iron or veilstone; only the last bar also takes supplies** (developer: "The
>   last bar can be filled with supplies"). A bar takes whichever is first paid into it and keeps
>   that resource until a withdrawal empties it. Bars are not tied to a
>   resource in advance (`vaultBarResource` is deleted).
> - **A full bar pays its own amount of that resource every minute** (the level SOs'
>   `vaultBarIncomePerMinute`, one entry per bar, raised by each Vault level). A bar that is not
>   full pays nothing; sub-bars only draw its progress. Nothing is multiplied any more
>   (`vaultBarBonus` is deleted); the trickle is its own income line (`IncomeSource.VaultBar`).
> - **What a bar holds** is the level SOs' `vaultBarCapacity` (the same at every level).
> - **Bars still open in order** by the Coffer research; the research opens bars, never a
>   resource. A withdrawal still returns everything as paid in and empties every bar.
> - **The Coffers are priced to repay themselves** (developer: "Don't make it prohibitively
>   expensive"; prices on the tech SOs), and the AI researches them on its economy ladder.
> - The panel: an iron, a veilstone and a supplies column (deposit a step / all that fits; the badge is what
>   that resource's full bars pay a minute), Withdraw All, the Coffers, the level-up; the bars
>   square shows each bar's resource (or "iron or veilstone" while empty), stored / capacity and
>   what it pays a minute when full.

**Four bars, one resource each (2026-11-15, [Unification decision 60](Unification_Decisions_2026-10-03.md);
developer. It replaces "any resource fills any bar, the player picks the boost" of decision 46.)**

- **Each bar belongs to one resource** (the Vault SO's `vaultBarResource`): **bars 1 and 2 take
  supplies, bar 3 iron, bar 4 veilstone.** A bar is filled only with its own resource and raises
  only that resource's income. There is nothing to choose.
- **Bars open in order**, by research: bar 1 with the Vault, then Second / Third / Fourth Coffer.
- **The Vault's level sets the stakes** (its level SOs' `vaultBarCapacity` and `vaultBarBonus`):
  each of its two level-ups **raises what a full bar gives** and **raises what every bar holds**,
  so a bar that was full is part-filled again after a level-up until it is topped up.

*The four bullets below are decision 46's and still hold, except that a bar's resource is fixed.*

- **Every bar pays as it fills** (2026-11-10, developer): it is cut into `vaultSubBars`
  sub-bars, and each filled sub-bar at once adds its share of the Vault SO's `vaultBarBonus` to
  the **income** of the resource the bar boosts — a full bar gives the whole bonus. Bars stack:
  four full bars on one resource give four times the bonus.
- **Income** means resource income: territory, extractors, the capital and
  every building trickle. Never refunds, grants, loot, curse bounties, a withdrawal or
  religion points.
- **Withdraw** takes everything back, at any time, exactly as it was paid in (by resource).
  **Every bar empties: the bonus and the progress toward it are lost.** There is no partial
  withdrawal and no lock timer.
- **A lost Vault loses what was in it**, and its bonus ends at once.

**The panel (2026-11-15, decision 60): two squares side by side.** The first is the actions
panel on a 4 x 4 grid: one column per resource with a **+** (deposit a step of that resource)
over the resource's icon (deposit all that fits; its badge is the income bonus that resource has
now), **Withdraw All** in the fourth column, then the Coffer research (one cell: it unlocks the
next bar) and the level-up in the bottom-right cell (as on every building, Overview.md § The
actions panel). The second square, in the same frame, holds the four bars: each row
names its resource, shows the bar cut into its sub-bars, stored / capacity and what it pays now;
a locked bar is dim and names the Coffer that opens it.

**One Vault per faction (2026-10-19).** A faction may own only one Vault of
Almiérra at a time — standing, under construction or planned all count
(`maxPerFaction` on the Vault SO, enforced in the build menu, the placement
executor and the AI like every per-faction cap). A lost Vault may be rebuilt.

**Levels** (`Civs/Alanthor/Buildings/VaultOfAlmierra/VaultOfAlmierra_Lvl1..3`) raise its HP,
the bonus of a full bar and the capacity of every bar (decision 60); the bars are opened by
research, not levels. Iron Accounts and Veilstone Accounts are removed. The bar capacity and bonus are on
`Age0/Buildings/VaultOfAlmierra/VaultOfAlmierra.asset`; every price is on the tech SOs.

> Why bars: the interest Vault paid a small, safe return on idle money and asked nothing.
> A bar asks a real commitment (a large sum parked for good) and pays a lasting edge in the
> resource the player chose, which the player gives up the moment the money is needed.

#### Researchable techs

**Expensive by design** (prices on the tech SOs). Bar 1 and supplies are open as soon as the
Vault stands.

| Tech | Prerequisite | Effect (rule) |
|------|------|---------------|
| **Second Coffer** | none | opens bar 2 |
| **Third Coffer** | Second Coffer | opens bar 3 |
| **Fourth Coffer** | Third Coffer | opens bar 4 |

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
line of sight. SO: `Age0/Buildings/Fortress/Units/Worker/Worker.asset`, which
also holds its work rates (2026-11-17, developer: "Make a worker SO"): how fast
it builds (`buildSpeed`, seconds of build time per second; several Workers add
up, with diminishing returns — Unification decision 72) and repairs
(`repairSpeed`, HP per second, paid as the HP goes in; it stops when the bank
cannot pay). They used to be constants in the construction and repair systems.

**Where a Worker stands to build or repair** (2026-11-18, developer: "builders
should move out of the construction footprint before starting the
construction. Also, they should only be able to build when very close to the
square footprint of the building"). One rule for construction and repair,
numbers on `Scripts/Systems/Work/BuildingConstructionSystem.asset`:

- A Worker works **only from outside the footprint, right at its edge**: its
  centre no further from the edge than its own radius plus `reachBeyondRadius`,
  and no nearer than its radius minus `bodyOverlapTolerance`. A Worker inside
  the footprint, or overlapping it, first **walks out**; one further away
  walks in. It never builds from inside the site or from across the yard.
- Each Worker on a site walks to **its own spot**: one slot on a ring
  `standGap` beyond its body all round the footprint, slots `slotSpacing`
  apart — the nearest slot no other Worker on that site holds, on walkable
  ground and not inside another building. The spot is kept for the job, so
  the Worker heads for a fixed point, and a crew spreads round the building
  instead of crowding one side. Only a crew bigger than the ring shares slots.
- A Worker within `stallRadius` of its spot that gets no closer for
  `stallSeconds` gives that slot up for the next one; when every slot has been
  tried the job is dropped and the Worker stops where it is — it never circles
  a building it cannot reach.
- Walking to a Build order, the Worker heads for the near edge of the
  footprint, never its centre; within `handoverRange` of the edge the plan
  breaks ground and the order becomes the build itself, after which the rules
  above place the Worker.
- In reach, the Worker stops and faces the building while it works.

### Scout

Extreme line of sight is the role. **Unarmed until Armed Scouts** (decision
13): a Scout has no damage and never auto-engages until that research
completes, after which every Scout — standing or newly trained — has the
damage the tech's effect sets. **Scout Sight:** a Scout always sees its full
line of sight, moving or standing (the perch-and-bloom ramp was removed
2026-09-29). The Alanthor **Scouting Celestarii** research no longer touches
Scout vision; since 2026-10-19 it gives every Scout the **Celestar passive** —
an idle Scout periodically reveals a random patch of the map
([Age_1_Alanthor.md](Age_1_Alanthor.md#scouting-celestarii--the-celestar-passive-2026-10-19)).

### Spearman

The Age 0 line infantry (it replaced the Swordsman in Age 0; the Swordsman is
Alanthor's Garrison unit). Spear reach — slightly longer than a sword. Its
**bonus vs Cavalry** is the infantry leg of the counter triangle
([Combat_Pacing.md](Combat_Pacing.md)).

**Look (2026-11-13).** In the first age the Spearman is a levy: a peasant with
a spear gripped at its middle, no armour and no shield. It is a reskin of the
aged-up look and plays exactly the same animations. Once its faction has aged
up it is drawn as an armoured legionary with a helmet, a tall shield and a
pilum — the same unit and the same stats, a different kit on the same prefab
(`UnitAgeKit`). The cloth and the shield face are in the player's colour
(`UnitPlayerColor`).

### Litharch

Trains at the Temple of Ridan. **A pure healer by default — no attack** until
**Warrior Priests** grants one. Even armed, a Litharch never goes looking for a
fight: it only returns fire on an attacker in reach and never chases
([Stances.md § 1b](Stances.md#1b-support-units)). Single-target right-click
heal (`healRange` on `Litharch.asset`); the Temple's aura heal is separate.

**No abilities in Age 0 (2026-11-18, developer: "no abilities on age 0, only
its default heal power").** While its faction is in Age 0 the Litharch cannot
cast Cleanse or Cleanse Pulse and shows no ability bar; from the age-up they
work as before. The gate is data — `requiresAgeUp` on the two ability SOs —
read in one place (`AbilityQuery.IsUnlocked`), so the bar, the cast command
and the AI's cleanse errands obey the same rule. (The tutorial's Mission 1
does not use Cleanse: its node falls to the army.)

**Litharch upgrades (decision 77, 2026-11-18)** — *hidden from the Temple
since 2026-11-18 (Temple of Ridan, above); kept as data.* Built in the same shape every other
trainer gives its units, in three tiers that open with the Temple's research
level (the highest adopted sect level, above). Researched at the Temple; every
number is on the tech SO (`TempleOfRidan/Research/`):

| Tier | Heal | Armour (melee + ranged) | Health | Skills |
|------|------|-------------------------|--------|--------|
| I    | Prayer Beads | Linen Vestments | | (Warrior Priests, open from the start) |
| II   | Litany | Wool Vestments | Veteran Litharchs | Swift Rites (the Litharch's abilities recharge faster), Long Reach (heals from further) |
| III  | Psalter | Blessed Vestments | Elite Litharchs | Twin Blessing (each heal also mends the most-wounded other ally in reach, `TwinBlessing.cs`) |

Each heal / armour / health line is a ladder (each tier needs the one below)
and takes one cell of the Temple's panel. New research stats behind them:
`HealRate`, `HealRange` (the unit's `CanHeal`) and `AbilityCooldown`
(`AbilityCooldownScale`, read when an ability's cooldown is charged).

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
| Barracks | Garrison | Route Guard | **Longhouse** (2026-10-13; was War Hall) |
| House | House (Alanthor) — level ladder | **Waystation** — no population (Runai get the full cap at age-up); a shooting tower that speeds caravans and taxes them veilstone | **House (Feraldis)** — a **raider-spawn building only** (no population). Every build / upgrade spawns autonomous Raider units that attack the closest enemy. |
| Gatherer's Hut | **Guild** (levels + the Guild research lines) | **Trading Post** — a supply depot; its output is hauled by caravan ([Age_1_Runai.md §3](Age_1_Runai.md)) | persists — can be upgraded to **Hunting Lodge** or **Logging Station** |
| Mine | Mine (Alanthor levels) | Mine | Mine |
| Veilstone Mine | Veilstone Mine (Alanthor keep it since decision 42; the Trading Outpost is retired, decision 55) | **Sanctuary**, in place (the Runai never mine it either) | Veilstone Mine |
| Palisade | stays timber; **keeps building it** (2026-11-18) | stays timber; no new palisade | **Wooden Wall** — raised automatically to its one cultured level; keeps building it |

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
4. **Vault model** — originally compound, then simple capped interest
   (decision 41). **Superseded 2026-11-10 (decision 46): four bars that raise
   income when full.** See § Vault of Almiérra above.
5. **Vault research names** — Second / Third / Fourth Coffer (the bars),
   Iron / Veilstone Accounts (the boosts). The banking grades are gone.
6. **Feraldis housing in early game** — Feraldis and Runai get the full
   population cap instantly at age-up; Feraldis Houses are raider spawners
   only ([Age_1_Feraldis.md](Age_1_Feraldis.md)). See
   [Overview.md § Population model](Overview.md).
7. **Gatherer's Hut on age-up** — huts do not despawn; they **transform** per
   culture (Guild / wagon / Hunting Lodge or Logging Station). See
   [§ Age-up transitions](#age-up-transitions).

## Remaining open questions

- **Ward** (planned) — design and price.
- ~~Thessara's Crossing design~~ — done 2026-10-13, [Age_1_Runai.md §2](Age_1_Runai.md).
