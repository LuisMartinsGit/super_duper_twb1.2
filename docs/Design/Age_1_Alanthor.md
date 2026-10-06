# Age 1 — Alanthor

> Defensive culture. Stone / medieval aesthetic. Strength comes from walls,
> long-range archery, and steady building toughness.
>
> **See also:** [Overview.md](Overview.md) (two-age framing), [Age_0.md](Age_0.md)
> (pre-culture starting buildings), [Territory_Claims.md](Territory_Claims.md)
> (how ground is owned), [Sects.md](Sects.md) / [Religion.md](Religion.md).
>
> **Data lives on the SOs (2026-10-03, [Unification decisions](Unification_Decisions_2026-10-03.md)).**
> This doc keeps the RULES and the REASONS: what each building and unit is
> for, what unlocks what, which level gates which tech or unit. Every number
> — cost, HP, damage, armour, range, line of sight, speed, train / build /
> research / upgrade time, population, footprint, income, multiplier — is on
> the SOs under `Assets/GameData/TechTree/Civs/Alanthor/` (and the Age 0
> entity SOs the cultured buildings extend), and **on a conflict the SO
> wins**. Read the numbers in the generated calculator
> `tools/calculator/TechTree.html` (built from the SOs by
> `tools/gen_calculator.py`). The SO roster is canonical (decision 30): an
> entity that exists only in a doc does not exist.

---

## Culture identity

| Aspect | Alanthor |
|--------|----------|
| Focus | **Defense** (walls, towers, long-range archery, building HP) |
| Style | Stone / Medieval |
| Economy | Huts (the Guild) and Mines on territory slots, the **Trading Outpost** for veilstone (Alanthor never mine veilstone — [Veilstone_Economy.md](Veilstone_Economy.md)), the **Vault** for interest, and the Guild **Surveys** as the deliberate exception that lets huts produce iron, veilstone and veilsteel |
| Territory | Ground is owned by standing on it ([Territory_Claims.md](Territory_Claims.md)); the Fortress and extractors lock it. Alanthor builds only on owned ground, walls included |
| Main upgrade hooks | the **Fortress** (capital, L1-L3) — the tool ladder and the **Mason Guild** building-HP passive; the **Wall Hub** — the wall levels |
| Techs | **faction-wide.** A tech applies to every unit or building it names, standing and future; there is no per-battalion upgrade (decision 31) |

---

## Conventions

- **Building levels** in Age 1 are **L1 / L2 / L3**. Level 0 is the
  pre-culture Age 0 form; **L1 is free and automatic at age-up** for every
  cultured building — Garrison, Archery Range and Guild included (decision 27).
  L2 and L3 are bought; their prices, times and multipliers are on the level
  SOs.
- **The only tech level gate is the tech SO's `minBuildingLevel`** (decision
  21). The gates listed in this doc are those values (they are rules, so the
  doc states them); prices never.
- **A unit's level gate is the unit SO's `minBuildingLevel`** on its trainer.
- **Every trainer lists its units in its SO's `trains[]`** (decision 33); a
  unit no building trains is not trainable.
- Damage formula and counters: [Combat_Pacing.md](Combat_Pacing.md).

---

## Cultured carryover buildings

These buildings exist in Age 0 in their pre-culture form and become the
following on age-up.

### Building levels (2026-10-02 — CANONICAL for where level numbers live)

**Every Alanthor level is its own asset.** A cultured building is the same
entity as its Age 0 form, so the Age 0 form keeps its `BuildingDefSO` and each
level the culture gives it is a `BuildingLevelDefSO` in the Alanthor folder,
under the cultured name:

| Age 0 id (level 0) | Alanthor levels 1-3 | Folder (`Civs/Alanthor/Buildings/`) |
|---|---|---|
| `Hut` — House | House | `House/House_Lvl1..3` |
| `Fortress` — the Shelter, renamed at age-up | Fortress | `Fortress/Fortress_Lvl1..3` |
| `Barracks` | Garrison | `Garrison/Garrison_Lvl1..3` |
| `ArcheryRange` (Age 1 only) | Archery Range | `ArcheryRange/ArcheryRange_Lvl1..3` |
| `GatherersHut` | Guild | `Guild/Guild_Lvl1..3` |
| `Mine` | Mine | `Mine/Mine_Lvl1..3` |
| `VaultOfAlmierra` | Vault of Almiérra | `VaultOfAlmierra/VaultOfAlmierra_Lvl1..3` |
| `Alanthor_RoyalStable` | Royal Stable | `RoyalStable/RoyalStable_Lvl1..3` |
| `Alanthor_Tower` | Watch Tower | `Tower/WatchTower_Lvl1..3` |
| `Alanthor_SiegeYard` | Siege Yard | `SiegeYard/SiegeYard_Lvl1..3` |
| `Alanthor_TradingOutpost` (Age 1 only; also the Veilstone Mine after age-up) | Trading Outpost | `TradingOutpost/TradingOutpost_Lvl1..3` |
| `Alanthor_Wall` — Stone Wall | Stone / Battlemented / Shielded | `Wall/Wall_Lvl1..3` |

Each level carries its **name, upgrade cost and time, HP / train-time /
attack-cooldown multipliers over the base, targets per volley, population,
slot income, interest multiplier, trade-rate multiplier (the Trading
Outpost), an authored attack and sight, and its model**. `BuildingUpgradeConfig` reads them first; its code tables are kept
only for Runai and Feraldis until their levels get the same treatment.

**The HUD names the level.** Level 0 is the bare Age 0 name; every levelled
building reads `<name> - Lvl N`, using the level asset's name — the Hut
becomes **House - Lvl 1 / 2 / 3**. The **stone wall's levels are level SOs
too** (`Wall_Lvl1..3`: the level name and the wall's HP multiplier, decision
34); the palisade has no levels. **The Temple has no levels** (decision 20).

### Fortress — the capital (L1-L3)

**Code id:** `Fortress` (SO `Age0/Buildings/Fortress/Fortress.asset`; levels
`Civs/Alanthor/Buildings/Fortress/Fortress_Lvl1..3.asset`, named
"Fortress - Lvl N").

> **2026-10-03 (decisions 4, 5, 5b-5d):** there is no Hall, no King's Court
> and no Town Hall. Every faction starts Age 0 with the **Shelter**, and at
> age-up the Shelter **automatically becomes the Fortress** for every culture
> — the same entity, renamed. The Fortress keeps **levels L1-L3** (they no
> longer raise a territory limit — there is none since 2026-10-04,
> [Territory_Claims.md §10](Territory_Claims.md)). It earns its own SO supply income and carries its own
> defence block on its SO.

#### Trainable units

| Unit | Notes |
|------|-------|
| **Worker** | every culture — the only builder |
| **Scout** | every culture |
| **Ledger** | Alanthor — court automaton, one per player ([§ Ledger](#ledger--court-automaton)); level gate on the unit SO (L2) |
| **King Lexor** | Alanthor — the culture's hero ([Heroes.md](Heroes.md)); level gate on the unit SO (L3) |

#### Researchable techs

Every capital tech is `researchAt: Fortress`. The Age 0 pair is available to
everyone; the Alanthor ones are Alanthor-gated and filed in
`Civs/Alanthor/Buildings/Fortress/Research/`.

| Tech | Culture | Gate | Effect (rule) |
|------|---------|------|---------------|
| **Stone Tools** | Age 0, all | — | workers build faster (tool tier 1) |
| **Armed Scouts** | Age 0, all | — | arms Scouts, standing and newly trained |
| **Iron Tools** | Alanthor | Fortress L2 | build speed, tier 2 |
| **Veilstone Tools** | Alanthor | Fortress L3 | build speed, tier 3 |
| **Veilsteel Tools** | Alanthor | Fortress L3 | build speed, tier 4 |
| **Mason Guild** | Alanthor | Fortress L2 | faction-wide building HP |
| **Scouting Celestarii** | Alanthor | — | Use Celestar on Scouts + full Scout vision |

**The tool ladder is build speed** — Alanthor builds its walls and towers
faster as it climbs it; it does not touch gathering (nobody gathers).

The age-up itself is the landmark — there is no "Advance from Age 0"
research.

---

### Garrison — cultured Barracks

**Code id:** `Barracks` — the Alanthor Garrison is the same entity as the
Age 0 Barracks, renamed and reskinned. Its research files under
`Civs/Alanthor/Buildings/Garrison/Research/` (still `researchAt: Barracks`).

#### Trainable units

| Unit | Gate | Role |
|------|------|------|
| **Spearman** | — | the Age 0 line unit; the anti-cavalry leg of the triangle |
| **Swordsman** (`Alanthor_Swordsman`) | L1 | mid-game line infantry; bonus vs Siege — infantry is how a siege line dies |
| **Nobleman** (`Alanthor_Nobleman`) | L2 | the all-rounder — **no bonus vs anything**, so it never wins an exchange on type advantage and never loses one either. Prices the flexibility |
| **Sentinel** (`Alanthor_Sentinel`) | L3 | the damage sponge — the heaviest foot armour, bonus vs Heavy |

Cavalry is not trained here — it is the Royal Stable's.

#### Researchable techs

All faction-wide. Gates are the tech SOs' `minBuildingLevel` on the Garrison.

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Conscription** | — | faster training at any Barracks | |
| **Stone Weapons** | — | melee weapon tier 1 | |
| **Iron Weapons** | L1 | melee weapon tier 2 | Stone Weapons |
| **Veilstone Weapons** | L2 | melee weapon tier 3 | Iron Weapons |
| **Shard-infused Weapons** | L3 | melee weapon tier 4 (needs veilsteel) | Veilstone Weapons |
| **Iron Plate** | L1 | melee armour tier 1 | |
| **Veilstone Plate** | L2 | melee armour tier 2 | Iron Plate |
| **Shard Plate** | L3 | melee armour tier 3 | Veilstone Plate |
| **Seasoned Infantry** | L1 | veterancy tier 1 for the line infantry | |
| **Veteran Infantry** | L2 | veterancy tier 2 | Seasoned Infantry |
| **Elite Infantry** | L3 | veterancy tier 3 | Veteran Infantry |
| **Charge** | L2 | after a spell out of combat, a soldier closes with a **speed burst** and his **first strike** in that window hits harder; miss the window and it resets. The burst, the bonus and the timings are on the tech SO | |
| **Shield Wall** | L3 | a stationary-defence passive for the line infantry | Charge |

---

### Archery Range — Age 1, no cultured rename

The Archery Range is `minEra: 2` and never appears in Age 0, so for Alanthor
there is nothing to rename: it is the Archery Range (SO
`Civs/Alanthor/Buildings/ArcheryRange/ArcheryRange.asset`). Its roster is
**Archer / Crossbowman / Longbowman**; the Archer's canonical id is
`Alanthor_Archer` (the bare `Archer` id survives only as a factory alias for
old scenarios and saves). It reads its **own** footprint (decision 11 note in
[Build_Grid.md](Build_Grid.md)).

#### Trainable units — the bow ladder

| Unit | Gate | Role |
|------|------|------|
| **Archer** (`Alanthor_Archer`) | L1 | the generalist bow; bonus vs Infantry closes the triangle |
| **Crossbowman** (`Alanthor_Crossbowman`) | L2 | the heavy bolt; bonus vs Cavalry — the cataphract answer |
| **Longbowman** (`Alanthor_Longbowman`) | L3 | the top of the ladder in damage and reach; no bonus |

The ladder's rules (range rises with tier, range never exceeds sight, no
minimum range) are in [Combat_Pacing.md § The ranged ladder](Combat_Pacing.md).

#### Researchable techs

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Choreographed Volleys** | — | a unit active carried by every ranged unit: nearby allied ranged units fire faster for a few seconds ([Spells.md](Spells.md) §9) | |
| **Fletching** | — | more attack range for every ranged unit | |
| **Stone-Tipped Arrows** | — | arrow tier 1 | |
| **Iron-Tipped Arrows** | L1 | arrow tier 2 | Stone-Tipped |
| **Veilstone-Tipped Arrows** | L2 | arrow tier 3 | Iron-Tipped |
| **Shard-Tipped Arrows** | L3 | arrow tier 4 (needs veilsteel) | Veilstone-Tipped |
| **Iron / Veilstone / Shard Brigandine** | L1 / L2 / L3 | ranged armour tiers 1-3, each requiring the previous | |
| **Seasoned / Veteran / Elite Archers** | L1 / L2 / L3 | veterancy tiers, each requiring the previous | |
| **Arrow Volley** | — | permanent faster fire for every ranged unit | |
| **Arrow Shower** | L2 | faster still | Arrow Volley |
| **Deploy Stakes** | L3 | the first cavalry charge against an archer is blunted and partly reflected; the stakes re-plant | |

The arrow tiers are visible in flight ([Combat_Pacing.md § Arrow tips](Combat_Pacing.md)).

---

### House — cultured Hut

**Code id:** `Hut`. Levels in `House_Lvl1..3.asset`: housing, HP multiplier,
upgrade price (supplies only) and model per level. Level 1 is free at age-up.
No trainable units and no tech.

---

### Guild — cultured Gatherer's Hut (canonical 2026-07-08)

The Alanthor Gatherer's Hut **becomes the Guild** at age-up (id still
`GatherersHut`; levels `Guild/Guild_Lvl1..3`). It keeps paying its territory
slot income — the Alanthor level SOs carry their own ladder — and gains a
**level ladder (L1 free, L2-L3 bought)** plus two faction-wide research
tracks hosted on it (`researchAt: GatherersHut`, filed in
`Civs/Alanthor/Buildings/Guild/Research/`, Alanthor-gated; decision 16). The
old per-hut Wall Hub / Watch Tower conversion at age-up is **retired** — walls
and Watch Towers are built directly.

**Survey track** — the huts also produce a resource. This is the deliberate
exception to the veilstone rule ([Veilstone_Economy.md §5.1](Veilstone_Economy.md),
decision 17): a developed hut ring is Alanthor's late-game mine.

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Iron Surveying I** | — | every Guild also produces iron | |
| **Iron Survey II** | Guild L2 | more iron | Iron Surveying I |
| **Iron Survey III** | Guild L3 | more iron | Iron Survey II |
| **Veilstone Survey I** | Guild L2 | every Guild also produces veilstone | Iron Surveying I |
| **Veilstone Survey II** | Guild L3 | more veilstone | Veilstone Survey I |
| **Veilsteel Survey** | Guild L3 | every Guild also produces veilsteel | Veilstone Survey II |

The survey drips **scale with the hut's Guild level** (2026-08-11, "fully
developed huts are the late-game mine"): map iron deposits are finite and run
dry around mid-game, and a maxed hut ring with the top survey is designed to
carry the iron economy from there. Rates and the level scaling are on the
tech and level SOs.

**Reinforcement track** — the hut defends itself.

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Iron Reinforcements** | — | the hut auto-repairs once out of combat | |
| **Veilstone Walls** | Guild L2 | below half HP the hut casts a one-shot **Slow** burst on enemies in its gather radius (long cooldown); faster repair | Iron Reinforcements |
| **Veilsteel Pylons** | Guild L3 | the burst becomes a **Stop**; faster repair still | Veilstone Walls |

Implemented in `GathererHutIncomeSystem` (surveys), `GathererHutReinforcementSystem`
(repair + slow/stop) and the level SOs.

---

### Mine

Every culture's iron Mine (Age 0 building, `Age0/Buildings/Mine/`). Alanthor
levels are `Mine/Mine_Lvl1..3` with their own slot-income ladder. The Mine
hosts **Deep Shafts** and **Rich Seams** ([Veilstone_Economy.md §6](Veilstone_Economy.md)).
Note: until `UpgradeBuildingCommandHelper.ResolveBuildingId` gains a Mine row,
Mines cannot be levelled and `Mine_Lvl1..3` are data waiting for that switch.

---

### Vault of Almiérra — the Alanthor landmark

Built in Age 0 as the landmark that ages the faction up
([Age_0.md § Vault](Age_0.md)). Interest is **simple, on the principal up to
the Vault SO's cap** (decision 41, 2026-10-05) — never compounding. Its
Alanthor levels (`VaultOfAlmierra/VaultOfAlmierra_Lvl1..3`) carry the
interest multiplier: **interest applies from L1 and grows with level**
(decision 12); the banking-grade techs raise the rate (the grade rates are on
the Vault SO). Its research gates: Veilstone Monetization at **Vault L2**,
Veilsteel Bonds at **Vault L3**.

---

## Alanthor-unique buildings (new in Age 1)

These exist only after the player picks Alanthor at age-up.

### Royal Stable — `Alanthor_RoyalStable`

Alanthor's cavalry trainer (cavalry left the Garrison for it). Levels
`RoyalStable/RoyalStable_Lvl1..3`.

#### Trainable units

| Unit | Gate | Role |
|------|------|------|
| **Outrider** (`Alanthor_Outrider`) | L1 | cheap, fast **light** cavalry — the raid / screen slot, and the **cavalry scout** |
| **Cataphract** (`Alanthor_Cataphract`) | **L3** (decision 22) | the **heavy** barded cavalry that runs down bow lines |

**The Outrider is the cavalry scout (2026-09-12).** Its line of sight is wide
enough that riding IS its scouting method. It has no vision bloom and never
perches: the Scout trades standing still for a bigger circle, the Outrider
trades a smaller circle for covering ground fast — sweep rate is speed times
width, so a riding Outrider out-sweeps a moving Scout while a perched Scout
sees more of one spot and nothing else. This is also what the AI drafts when
its scouts are dead ([Game_AI.md](Game_AI.md) §7).

#### Researchable techs

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Stone-Barded Lances** | — | cavalry weapon tier 1 | |
| **Iron-Barded Lances** | L1 | tier 2 | Stone-Barded |
| **Veilstone Lances** | L2 | tier 3 | Iron-Barded |
| **Shard-infused Lances** | L3 | tier 4 (needs veilsteel) | Veilstone Lances |
| **Iron / Veilstone / Shard Barding** | L1 / L2 / L3 | cavalry armour tiers 1-3, each requiring the previous | |
| **Seasoned / Veteran / Elite Cavalry** | L1 / L2 / L3 | veterancy tiers, each requiring the previous | |
| **Charge** (`CavalryCharge`) | L1 | the cavalry version of the Garrison's Charge: speed burst + harder first strike, values on the tech SO | |
| **War Horn** | L2 | unlocks the War Horn active for cavalry (`RoyalStable/Abilities/WarHorn/`) | |
| **Full Gallop** | L3 | unlocks the Full Gallop active (a speed burst during which the riders cannot attack) | War Horn |

### Siege Yard — `Alanthor_SiegeYard`

Levels `SiegeYard/SiegeYard_Lvl1..3`. **All four engines coexist** (decision
32; settled 2026-08-27 — the 2026-08-02 "Catapult replaced Ballista" swap is
reversed):

| Unit | Gate | Role |
|------|------|------|
| **Ballista** (`Alanthor_Ballista`) | L1 | the flat single-target bolt, bonus vs Building — the hard-target cracker |
| **Catapult** (`Alanthor_Catapult`) | L1 | the lobbed splash stone, bonus vs Building and Infantry — the anti-mass answer |
| **Battering Ram** (`Alanthor_BatteringRam`) | L2 | buildings-only attacker, an armoured shell |
| **Trebuchet** (`Alanthor_Trebuchet`) | L3 | long-range area siege, the wall-line killer |

**Artillery doctrine (2026-08-02):** an engine may outrange its own sight; it
auto-engages only inside its line of sight, and firing at the far band needs
a spotter (scout or ally vision) plus an explicit attack order. The Catapult's
launch angle is solved ballistically so the stone lands where the target is
(Synty `SM_Wep_Catapult_01`, `FX_Catapult_Single_01`).

#### Researchable techs

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Stone / Iron / Veilstone / Shard-infused Shot** | — / L1 / L2 / L3 | munition tiers 1-4 (tier 4 needs veilsteel), each requiring the previous | |
| **Iron / Veilstone / Shard Plating** | L1 / L2 / L3 | siege armour tiers 1-3, each requiring the previous | |
| **Seasoned / Veteran / Elite Crews** | L1 / L2 / L3 | veterancy tiers, each requiring the previous | |
| **Reinforced Bolts** | L1 | the Ballista hits harder | |
| **Iron-Shod Ram** | L2 | the Ram survives the approach | |
| **Ranging Shot** | L2 | an active: after standing still, the next shot hits much harder | |
| **Counterweight Tuning** | L3 | the Trebuchet reaches further | |
| **Siege Screens** | L3 | a stationary ranged-defence passive for engines | Ranging Shot |

### Watch Tower — `Alanthor_Tower`

A stand-alone defensive tower, built directly from the worker palette (no
hut conversion). Levels `Tower/WatchTower_Lvl1..3`: each level raises its
fire rate, range and sight, and the top level adds targets and a ballista
bolt (a `BuildingSiegeShot`, so it may hit a wall piece). Every number is on
`Tower.asset` and the level SOs.

**Garrison (decision 24).** The Watch Tower has **4 garrison slots**
(`garrisonSlots`). Only **foot units** may enter — infantry and archers; not
cavalry, siege, workers or heroes. **Each occupant adds one target to the
tower's volley** (`garrisonArrowsPerOccupant`): a full tower shoots at more
enemies at once. Occupants are safe inside while the tower stands and step
out beside it when ordered out; if the tower falls they die with it.

### Trading Outpost — `Alanthor_TradingOutpost` (L1-L3, decision 40)

Alanthor's veilstone source: **Alanthor never mine veilstone**, and at age-up
every Veilstone Mine the faction owns becomes the first Trading Outpost of its
outcrop, moved off the outcrop onto a side
([Veilstone_Economy.md §3.1](Veilstone_Economy.md)).

- **Placement (2026-10-04):** **beside** an Inactive or Depleted veilstone
  outcrop, on one of its **four side slots (N / E / S / W)**, flush against
  it — **up to four posts per outcrop**, one per side, never beside a Cursed
  one. The outcrop stays an impassable node
  ([Build_Grid.md § The Trading Outpost's side slots](Build_Grid.md)). Every
  post locks its territory like an extractor.
- **Per-outcrop cost ramp:** the 2nd, 3rd and 4th post beside the same
  outcrop cost progressively more, to build AND to level; the first post
  beside a new outcrop is base price again. Multipliers:
  `TradingOutpostSystem.asset` `outcropRampMultipliers`.
- **Levels:** L1 free, L2 and L3 bought (`TradingOutpost_Lvl1..3`, prices
  ramped per outcrop). A level raises the post's trade rate — spend and earn
  alike (`tradeRateMultiplier`) — and its HP.
- **Trades** (toggled on the building, one at a time): Buy Veilstone (the
  default), Forge Veilsteel (after Veilsteel Forging), Sell Veilsteel (after
  Veilsteel Export). It never drains the outcrop.
- **Cycle:** the trade pays on a short fixed cycle (1 s since 2026-10-03) —
  the cycle and every rate are in `TradingOutpostSystem.asset`.
- **Research** (hosted by the Outpost, `TradingOutpost/Research/`): Trade
  Agreements I-III (chained, cheaper inputs), **Swift Caravans** (after Trade
  Agreements I — every trade of every Outpost runs faster, inputs and outputs
  together; the percentage is on its SO), Veilsteel Forging, Veilsteel
  Export.

---

## The stone wall (2026-10-02 — CANONICAL)

### The stone wall

**The Stone Wall is Alanthor's building, and only Alanthor's.** It is
`Alanthor_Wall`, it appears in the worker panel **at age-up**, and its first
level is Stone. The timber fence is a different building — the
[Palisade](Age_0.md#palisade--the-age-0-wall-2026-10-02--supersedes-wooden-wall-2026-09-21)
— which Alanthor stops being able to build at age-up. A palisade is **never
re-clad in stone**: what an Alanthor player fenced in Age 0 stays timber, and a
stone wall can neither snap to it nor branch from it.

The wall is one hub-and-segment machine: **hubs** are the only placed piece;
the **curtain** between two hubs is a chain of invisible **modules** that carry
the HP, the passability and the conversions; a **gate**, a **wall tower** or
an **emplacement** is a conversion of a module. The SOs:
`Civs/Alanthor/Buildings/Wall/WallHub.asset` (hub), `WallSegment.asset` (one
curtain module), `Tower/WallTower.asset`, `Age0/Buildings/Wall/Gate/WallGate.asset`
(shared with the palisade), and the level SOs `Wall_Lvl1..3`.

**A wall is paid per module (2026-10-02).** Hubs have their price, and every
curtain module has its own (`WallSegment.asset` `cost`), charged in the
executor for exactly the modules it lays — the whole drawn wall must be
affordable before any of it is built. The draw tool shows the full price.

**Every level is the same wall.** One cross-section for all three: as deep as
the hub's footprint (`AlanthorWall.StoneWallDepth`), with a wall-walk at
`AlanthorWall.DeckHeight` between two parapets. A level changes the wall's
toughness, its dressing and what may be fitted to it — never its size.

**A wall has no inside.** Both faces are identical at every level. A wall
drawn left-to-right and one drawn right-to-left are the same wall.

| Lv | Name | Reached by | May fit | Look (both faces) |
|----|------|-----------|---------|------|
| **1** | **Stone Wall** | the Alanthor culture pick — free | **gate, wall tower** | dressed stone, a low parapet on both edges; hubs are square bastions flush with the walk |
| **2** | **Battlemented Wall** | `Battlements`, at the Wall Hub | + **Ballista emplacement** | arrow-slit faces, full battlements on both edges |
| **3** | **Shielded Wall** | `ShieldedRamparts`, at the Wall Hub, after `Battlements` | + **Trebuchet emplacement** | rough-coursed faces, battlements both edges, a **roofed hoarding gallery along both faces**; every hub becomes a round tower under a slate cone |

Each level's HP multiplier is on its level SO (`Wall_Lvl1..3`). Level 3 also:

- **Hardens the men on it.** A foot unit standing on its own (or an ally's)
  Shielded wall gains melee and ranged armour (`WallTiers.DeckArmorBonus`). It
  is gained on the deck and lost the moment the unit steps off it.
- **Puts a tower on every hub.** A Shielded hub fires like a wall tower. Hubs
  raised after the research are towers from the start; standing hubs gain it
  with the promotion.

**The wall is walkable.** The walk is a second nav layer (the rampart layer).
Right-click a friendly or allied stone wall with **foot units** selected and
they walk to the nearest of their side's **hubs, wall towers or gates**, climb,
and walk the deck to the clicked spot. Order them to the ground and they come
down the same way. A **breach** — a dead module — turns the modules either
side of it into ramps **anyone** can climb, enemies included. Palisades have no
deck. Cavalry, siege, workers and heroes stay on the ground. **Wall garrison
slots are retired:** units stand on the deck, visibly, fighting from it.

### Promotion, research and the wall lock

**Level 1 is not bought.** Committing to Alanthor at age-up IS the stone wall:
`AgeUpSystem` gives the faction the stone wall at level 1. **Levels 2 and 3
are researched at the Wall Hub** (`Battlements`, then `ShieldedRamparts`;
`researchAt: Alanthor_Wall`, prices on the tech SOs) — a building's own ladder
belongs on that building; the Fortress keeps `MasonGuild` and has no wall
tech.

**One hub's button upgrades EVERY wall you own.** A wall is never a patchwork
of levels, so the purchase is faction-wide: `PromoteFactionWalls` re-clads
every hub, curtain, gate and tower at once. Which hub you clicked does not
matter, and the button disappears from all of them the moment it is queued
anywhere. A hub therefore carries a `ProductionQueueItem` buffer **and a
`ProductionState`** like any other research host.

**The walls are LOCKED while a level researches (2026-09-27).** From the
moment `Battlements` or `ShieldedRamparts` is queued anywhere until it lands
or is cancelled:

- **every wall-changing action is greyed**, with the tooltip *"Walls are being
  upgraded"*: Build Wall on a hub, and Convert to Gate / Tower / Hub, Mount
  Ballista / Trebuchet on a module. A drawn wall that **attaches to a standing
  hub or cell** is refused whole; a free-standing new wall may still be drawn
  (it rises at the current level and is re-clad with the rest when the
  research lands). Gate Open / Close and Replace Equipment stay live — they
  change no wall.
- **the level button becomes its progress**: an *"Upgrading: <tech> (N%)"*
  cell stands where it was, and the panel's progress bar follows the research
  on whichever hub is running it.
- **a Cancel Upgrade cell** (on any of the faction's wall pieces, and the
  running slot in the hub's queue strip) stops it and refunds the full price.
- The lock is enforced by the **executors**, not just the panel
  (`CommandRouter.WallsLockedForUpgrade`, read from `WallTiers.LevelResearchActive`),
  so a stale panel or an order already in flight is refused identically on
  every lockstep peer. The AI's wall doctrine waits the lock out.
- **A tech is one-shot per faction.** The research executor refuses a tech
  already researched or already queued in ANY of the faction's queues before
  charging.

A wall raised **after** the promotion starts at that level; HP is read from
the level at creation (the piece's own SO `hp` × the level SO's multiplier),
never patched afterwards. A wall raised **before** it is promoted in place (HP
scaled proportionally, so a breached wall stays breached), so a faction's wall
is never a patchwork of levels.

### The AI's wall (2026-10-02)

**Which ground (2026-10-03):** the AI builds a Fortress in every territory it
can, but walls only its starting (home) territory — one ring, piece-capped,
rebuild-capped and paid from the Economy wallet, yielding to the army. The
full rule is [Game_AI.md § Walls](Game_AI.md).

The Alanthor AI walls its **territory border**, and the wall stands close to
it: its centre line a few metres inside the border (`AIWallPlanner.asset`
`borderInset`), measured to FOREIGN ground — another faction's or neutral
territory. A lake or mountain inside the territory is not a border and does not
pull the wall in; a stretch of border that runs along impassable ground is left
to the map. The plan is traced from the territory's real outline, and a hub
goes in wherever that outline bends, so the straight wall between two hubs
follows the border instead of cutting across it.

**The band is kept clear from the first minute.** Every AI building — except
claims and extractors, which the map sites — keeps a clearance between its
EDGE and its territory border (`buildingBorderClearance`; halved on 2026-10-02
when the larger value left no legal Vault spot in small start territories).
The wall is planned after age-up, long after most of the base stands; without
the band the base was built exactly where the wall later had to go. **Only in
the home territory** (2026-10-04): only the home is ever walled, so a
province keeps no band (Game_AI.md §6b).

**A refused link is repaired, then abandoned — never retried forever.**
Before linking two hubs the AI checks the run with the executor's own rules
(own ground, clear along its whole length, affordable). If the straight run is
blocked it lays a CURVED run bulging round the blocker, left and right, wider
each try; if nothing fits, the link is marked refused and left open, and the AI
moves on to the next link, its gates and its towers. An order that was sent but
never produced its hub or link within a few think ticks counts as refused too
(under lockstep the AI never hears the executor's answer). A new hub links only
to its plan neighbours, never to every hub in reach.

When the territory changes the plan is redrawn. The hub cap counts only hubs
on the CURRENT plan, so an older inner ring left standing never stops the wall
at the real border from being built.

### Drawing walls (2026-09-18)

> **Canonical for HOW the player places walls** (stone wall and palisade
> alike). Supersedes one-click-per-hub placement; a press-and-release with no
> drag still places a single hub.

A wall is **drawn, not clicked**. With the Wall Hub (or Palisade) selected in
the worker palette:

| Step | What happens |
|---|---|
| **Press** on the ground | the path starts there — or, within the **hub snap radius** of a friendly hub of the same kind, *at that hub* — or, within **one wall module** of a friendly **wall cell**, *at that cell*, which **becomes a hub** when the order lands (see § Branching walls) |
| **Drag** | the path **follows the drag** — the cursor's own track, sampled and lightly smoothed; it never grows faster than the cursor moves and never steers on its own. Hubs go up at the stroke's **two ends** — and, once the stroke passes the run cap, at **evenly spaced points between**, so everything between two hubs is ONE continuous swept wall |
| **Turn too sharply** | a path that bends tighter than the minimum bend radius anywhere (measured over a short window, so hand jitter does not count) turns **red** and is refused on release |
| **Run back over itself** | a path that crosses, doubles back onto or spirals within one module of an earlier part of itself is **red** and refused |
| **Retrace** over the path | **backtracking** — moving the cursor back along the path erases everything drawn after that point |
| **Release** | every hub and the segments between them are placed as **one order**. The **end snaps** the same way the start does — judged at the PATH's end: onto a friendly hub (joining an older wall) or onto a friendly wall cell, which becomes a hub — a T-junction into a standing wall. The last stretch is **bent smoothly onto** whatever it snapped to |
| **Close a loop** | bring the end back to the stroke's own start once the stroke is at least three snap radii long. A closed loop always gets **at least three runs**, because a one-run loop would join a hub to itself and a two-run one would join the same pair of hubs twice |
| Right-click / Esc | cancels the drawing |

**The snap distances, as the code has them:** a hub snaps to a friendly hub
of the same kind within **twice the hub's radius** (`AlanthorWall.HubRadius`
× 2 — about 4.2 m; `BuildCommandPannel.HubSnapRadius` and
`CommandRouter.WallPathHubSnap` must stay equal) and to a friendly wall cell
within **one module** (`AlanthorWall.InstanceSpacing`, 3 m). Hubs do **not**
snap to the build grid ([Build_Grid.md § 5](Build_Grid.md)). Each hub ghost is
green or red on the usual placement rules (footprint free, own territory,
whole curtain on owned ground); a path with any red hub, or one the bank
cannot pay for, is refused on release.

The draw tool's tunables — minimum bend radius, run cap
(`maxModulesPerSegment`), hub cap (`maxHubs`), sampling — are in
`WallDrawTool.asset` beside `WallDrawTool.cs`
(`Assets/GameSystems/Presentation/UI/World/`).

**One mesh, invisible cells.** Between two hubs the presentation sweeps a
single mesh along the drawn curve, terrain-following, re-clad per wall level.
The sim works in **modules** (`AlanthorWall.InstanceSpacing` apart along the
same arc): invisible cells carrying the HP, the passability and the
conversions, and a dead cell's span is simply left open by the mesh, so a
breach shows as a breach. Presentation ids: `555` the swept segment, `556` a
cell's pick collider.

**The segment itself carries no HP (2026-09-25).** It is the graph edge
between two hubs and the owner of the swept mesh, nothing more: no
`Health`, so no attack, splash or auto-acquire can find it. It lives exactly
as long as one of its cells does. A hub link to a segment that no longer
exists is pruned, and an invisible cell whose segment is gone is destroyed. A
cell killed mid-construction, or before a wall-level promotion lands, stays
dead.

**The run cap.** A single wall run is capped at `maxModulesPerSegment`
modules. A longer stroke is cut into the fewest runs that all fit under the
cap, with the hubs at **equal arc intervals** — so a stroke needing one extra
hub gets it exactly at the midpoint, two at the thirds, and a drawn wall is
symmetrical rather than "full runs plus a stub at the end".

**An inserted hub never bends the wall (2026-09-24).** **The two runs meet at
the point the player drew**, dead straight through, and the hub drum stands
there. **The accepted failure mode is that the hub CLIPS the curtain** — a
straight wall with a tower sunk slightly into it reads as a wall; a wall that
kinks at every third tower reads as broken geometry. This is also exactly why
a wall piece **cannot be a prefab variant**: the pieces have to interpenetrate
freely, so the art is swept and tiled at runtime (`WallCurveMesh` /
`WallModuleArt`), never assembled from fitted variants.

**Why not a hub every few modules.** Intermediate hubs at one module's
spacing left a single module per chord: the wall read as a row of towers with
trim between them, the opposite of a curtain wall. Hubs are structure, not
decoration — they appear when a run would otherwise be too long to be one
piece.

**Lockstep.** The whole path rides one `PlaceWallPath` command (positions in
the command's string field, 2 dp), executed hub → segment → hub on every peer
through the same `PlaceWallHubDirect` / `WallExtendDirect` executors a click
uses, so a drawn wall cannot desync where a clicked one would not.
(`PlaceWallPath` packs the FACTION in `EntityNetworkId`, not an entity; it is
in `LockstepManager`'s skip list.)

Test scenario: **Wall Drawing** (Scenarios menu) — an Alanthor Age 1
Fortress, workers, a full bank, open ground.

### What a module may become, and where

A wall is a wall, not a rack for fittings. Every conversion — tower, gate,
ballista, trebuchet — needs **a clear run of untouched modules around the one
you clicked**, and the rule is checked twice: the action panel hides the card,
and the executor re-checks it on every peer before it spends.

| Fitting | Needs a clear run of | Wall level |
|---------|---------------------:|-----|
| **Wall tower** | **3** modules (one clear either side) | any stone level (1+); never a palisade |
| **Ballista emplacement** | **3** modules | **2+** (Battlemented) — `minWallLevel` on its SO |
| **Trebuchet emplacement** | **3** modules | **3** (Shielded) — `minWallLevel` on its SO |
| **Gate** | **4** modules | any; a palisade too. The gatehouse eats three, the fourth keeps it off the next fitting |

"Clear" means alive, finished, and not already a gate, a tower, an
emplacement or mid-conversion. **A dead module is not clear either** — a
breach is not building space. `AlanthorWall.FreeRunAround` walks the segment
both ways from the clicked module and returns the run's length; that single
number is the whole rule.

### The gate is one structure, three modules wide

Converting a wall replaces **three contiguous modules with a single gate
entity** (`AlanthorWall.GateRegionSpan`): the module the player converted
becomes the gatehouse, and **the module on each side is destroyed** — the
gatehouse's own masonry occupies that ground. The curtain mesh leaves the
whole span open so nothing is drawn through it.

| Property | Rule |
|----------|-------|
| Entity count | **one** — with `WallGateTag` and its own Health |
| HP | three curtain modules' worth at the faction's wall level |
| Cost / timer | on `WallGate.asset` — the same for the palisade's gate (decision 36) |
| Short segment | a segment with fewer than 3 live modules converts the modules it has; the gatehouse is built to the span it actually got |
| Doors | **two leaves that swing**, driven by the gate's open state — always drawn, never teleported |
| Opening | the centre third; the flanking thirds are solid masonry |

**Opening and closing it.** By default it opens when a friendly comes near and
closes behind them, and it never opens for a hostile. The player can override:

| Mode | Behaviour | UI |
|------|-----------|-----|
| **Auto** (default) | opens for nearby friendlies, closes when they leave | the gate's action panel shows **Close Gate** |
| **Sealed** | stays shut, *including to friendlies* | the panel shows **Open Gate**; the doors close on the spot |

The toggle is one order (`SetGateLock`, lockstep type 44). Sealing does not
make the gate tougher — it is a pathing decision, not a defensive one.

### Wall towers

A **wall tower** is a single module converted to a ranged tower astride the
wall (`Alanthor_WallTower`, SO `Wall/Tower/WallTower.asset`; unrelated to the
stand-alone Watch Tower). Stone levels only, the clear run of 3, a conversion
price and timer on its SO, no worker.

### Ballista and Trebuchet emplacements (decision 26, 2026-10-03)

In both cases **the emplacement and the engine are separate entities**: the
emplacement is the platform (the thing that is built, repaired and holds the
ground) and the engine is a unit standing on it that **never moves**.

- **Wall-mount only.** Select a **masonry** curtain module and its action
  panel offers **Mount Ballista** (wall **level 2+**) / **Mount Trebuchet**
  (wall **level 3**), subject to the clear run of 3. A palisade cannot carry
  an engine, and there is no free-standing platform.
- **Worker-built.** Mounting places the emplacement as a construction site on
  the module that **workers build**, over the emplacement SO's `buildTime` —
  there is no worker-less self-mount timer any more. The module stays wall
  (its HP, its footprint, its place in the segment); it gains corbels out to a
  planked fighting deck, a mantlet and the engine's pintle ring, and extra HP
  to carry the weight. The **first engine is raised when the mount
  completes**.
- **The engine stands on the deck** (`AlanthorWall.EmplacementDeckHeight` —
  the same number the deck visual reads, so the two cannot drift apart),
  which gives it the normal high-ground bonus.
- **A destroyed engine is not rebuilt for free.** The platform stays,
  **empty**, and its action panel offers **Replace Equipment** for the
  ENGINE's own SO price and `trainingTime` (`EmplacedBallista.asset` /
  `EmplacedTrebuchet.asset`), no worker. One lockstep command
  (`ReplaceEquipment`, type 47).
- **Movement: none.** No move speed, no destination; the engine never chases,
  never backs off out of its minimum range, never returns to a guard post. A
  target outside its band is dropped.
- **Platform destroyed:** the module dies as wall; its engine dies with it.
- The engines carry **0 siege armour** and the **Heavy** tag
  ([Combat_Pacing.md](Combat_Pacing.md), decision 35), cost no population, and
  are deliberately *stronger per shot and slower* than the mobile Siege Yard
  versions: an emplacement is ground you have decided to hold, not an army you
  can move.

| | **Wall Ballista** | **Wall Trebuchet** |
|---|---|---|
| Platform SO | `Alanthor_BallistaEmplacement` | `Alanthor_TrebuchetEmplacement` |
| Engine SO | `Alanthor_EmplacedBallista` | `Alanthor_EmplacedTrebuchet` |
| Wall level | 2+ | 3 |
| Against | single targets, bonus vs Building | massed infantry and siege lines (splash), bonus vs Building |

### Walls fall in sections

> **Supersedes the hub-death cascade (2026-09-19).** A hub's death used to
> cascade-destroy every segment attached to it; with a drawn wall being ONE
> segment from its first hub to its last, a single hub kill erased a whole
> stroke. That is retired.

Every piece of a wall dies **on its own HP and only itself**:

| Piece dies | What collapses |
|------------|----------------|
| **Wall cell** (a curtain module) | That cell. The swept mesh opens a breach at its span; its neighbours stand. |
| **Wall Hub** | That hub. A breach opens where the tower stood. Every segment attached to it keeps standing on its own cells. |
| **Wall Segment** | Only when its **last cell** dies — it is a graph edge with no HP of its own, and `WallSegmentCleanupSystem` retires it once nothing is left standing on it. |

Rationale: a wall should have to be **breached**, and the breach should be
exactly as wide as what was knocked down. The hub is still the worthwhile
target — the widest breach for one kill — but it is no longer a hidden kill
switch. **Rebuilding the hub repairs the line:** a same-faction hub built with
the dead hub's centre inside its footprint adopts every orphaned segment that
ended there (`AlanthorWall.AdoptOrphanedSegments`).

### Branching walls (2026-09-19)

A wall can be joined from any point of any other wall, not only at its hubs —
that is what makes **T- and X-shaped** layouts possible. **Any standing wall
cell can become a hub**:

| How | What happens |
|-----|--------------|
| **Convert to Hub** on a selected wall cell | Costs a hub, with a short timer (`WallUpgradeState` type 3). On completion the cell is replaced by a hub. |
| **Drawing a wall onto a wall** | Pressing within one module of a friendly cell starts the stroke *on that cell*; releasing within one module of a friendly cell ends it there. Each such cell is converted as part of the order (`WallPathKind.CellHub`, costed as a hub, instant) and the new wall attaches to the hub it became. |

**The segment splits at the new hub** (`AlanthorWall.ConvertInstanceToHub`):
the cells before it stay on the original segment, the cells after it move to
a fresh segment from the new hub to the far hub, and the cell under the hub is
removed. Nothing is rebuilt — every other cell keeps its position, HP and
construction state. Not convertible: a gate cell, a tower cell, a cell still
under construction, or a cell whose segment is mid-gate-conversion.

### Health bars (segments and gates)

| Selection | Bar shown |
|-----------|-----------|
| **Hub** | Standard `FloatingHealthBars` Health bar. |
| **Wall cell (individual)** | Standard per-cell world-space Health bar. |
| **Wall segment** | One aggregated bar — `sum(cell.Hp) / sum(cell.HpMax)` with `<alive> / <total> intact`. |
| **Gate** | Its own Health bar, label `Wall Gate`. |

### The wall's art

> **Stone levels authored 2026-10-01, re-authored 2026-10-02.** `Waning Border
> > Art > Author Castle Prefabs` (`Scripts/Editor/CastleArtAuthor.cs`) builds
> the stone set from the Synty POLYGON Fantasy Kingdom castle kit into
> `Civs/Alanthor/Buildings/Wall/Stone/` and binds it per level in
> `Age0/Buildings/Wall/WallModuleArt.asset` (curtain, hub, gate, wall tower,
> ballista and trebuchet bastions; slot 0 = the timber palisade). Every level is
> symmetrical — both faces carry the same masonry and parapet, and level 3's
> roofed hoarding gallery runs along BOTH faces. Hubs are square bastions
> flush with the walk at levels 1-2 and round towers under a slate cone at
> level 3. The same tool authors the **Watch Tower** (`Tower/WatchTower.prefab`)
> and the **Fortress** (`Fortress/Fortress.prefab`). `Preview Castle Prefabs`
> renders all of it to `Temp/castle_preview/`.

**Where things live.** The shared hub/segment CODE, the Gate (a palisade takes
gates too), the timber art and `WallModuleArt` stay in `Age0/Buildings/Wall/`
— one machine, two kinds of content; the Stone Wall's content (`WallHub`,
`WallSegment`, `Tower/`, `Stone/`, the level SOs and the `Battlements` /
`ShieldedRamparts` research) is in `Civs/Alanthor/Buildings/Wall/`. Every wall
PIECE has its own SO and its own `prefab` + `presentationId`, so each piece's
art binds independently and a piece with no art keeps its procedural visual.

**Textures do not travel in the FBX, and must not.** The FBX carries
**geometry, UVs and material NAMES**, and each name is remapped to a `.mat`
asset in the piece's folder (`Wooden_Stakes`, `Player_cloth` — base colour
stays white, the player's colour is applied on top — and `GroundMaterial`).
Re-exporting the mesh cannot disturb them, because the link is by name.
**Binding:** run **`Waning Border > Walls > Bind Wall Art`** after adding or
replacing an FBX; it re-applies the importer settings and the material remap a
Blender re-export resets. Artist steps: [docs/Art_Pipeline_FBX.md](../Art_Pipeline_FBX.md).

**How it is drawn.** Still **one mesh per segment**: the authored module baked
along the curve, one copy per sim cell (`WallArtMesh`), or the procedural
cross-section swept along it when no prefab is bound (`WallCurveMesh`).
`WallModuleArt` measures the module once and **fits its length to the sim
module pitch**, so the art does not have to be authored to the game's numbers.

**Ownership.** Every wall piece takes the owner's colour on its ownership
parts through `WallModuleArt.ApplyOwnerColor` and a MaterialPropertyBlock —
not `BuildingFactionColorMarker`, whose per-renderer material clones would
collapse the batching a finished perimeter of hundreds of pieces depends on.
Name a part `Stripe_*` (or give it the saturated marker blue) to make it the
ownership part — no code changes either way.

---

## Alanthor units

Every stat is on the unit SO; the roles, trainers and gates are above. Notes
that are rules rather than numbers:

- **Swordsman, Longbowman and the Royal Stable are settled** (decision 30) —
  no longer "TBD".
- **Cut, never to be documented again** (decision 30): Royal Guard, L2 / L3
  cavalry tiers, the Academy tech, the Wheel cart, Stone Ledgers, the
  Crucible. The Holy Scholar is cut (decision 33); the Smelter is removed
  (decision 1).
- **Weight tags** (decision 35): Outrider Light; Cataphract Heavy; Archer,
  Crossbowman and Longbowman Light; Ballista, Catapult, Trebuchet, Battering
  Ram and the emplaced engines Heavy ([Combat_Pacing.md](Combat_Pacing.md)).

### Ledger — court automaton

Trained at the **Fortress** (decision 33); its level gate is the unit SO's
`minBuildingLevel`. **One Ledger per player** — enforced at the training-command
gate, same mechanism as King Lexor (live unit or queued order both count).

- **Automate Facility**: a timed yield boost on one economy building, followed
  by an *Under Automation* lockout on that building (the Aftermath chain), so
  the same building can be re-automated only after the full cycle. Durations
  and the boost are on the `AutomateFacility` / `UnderAutomation` ability SOs
  (`Civs/Alanthor/Units/Ledger/Abilities/`).
- Feedback VFX: automated buildings carry a golden rising-spark aura for the
  boost; a larger golden burst plays at the building when the ability lands.

Visual identity (2026-08-02): a **legless floating automaton** hovering on a
**forcefield disc** tinted the owning player's colour, with a low synthesized
hum. The open-frame torso is **full of cogwheels** (spinning constantly), it
has **four articulated arms**, and at its centre floats a **shining crystal in
the player's colour**, pulsing with its machinery.

### King Lexor

The Alanthor hero, trained at the **Fortress** (decision 33), one at a time.
Levels, abilities and the death / revival choice: [Heroes.md](Heroes.md).

---

## Decisions record

1. ~~`KingsCourt` → `TownHall` rename~~ — **superseded 2026-10-03.** The Age 0
   Shelter becomes the **Fortress** at age-up.
2. **Garrison roster** — Spearman, Swordsman (L1), Nobleman (L2), Sentinel
   (L3); cavalry moved to the **Royal Stable** (Outrider L1, Cataphract L3).
   The Royal Guard is cut (decision 30).
3. **Archery Range** — standard cultured-building path, no population; levels
   on the level SOs.
4. **Tech ladders** — originally "unlock a per-battalion upgrade". **Superseded
   2026-10-03 (decision 31):** every weapon / armour / arrow / tool / veterancy
   tier is a faction-wide tech.
5. **Glow tiers** — superseded: the fourth tier of every ladder is the
   veilsteel-priced Shard tier.
6. **Mason Guild** — canonical name; faction-wide building HP (value on the
   tech SO).
7. **Archery Range ladder** — Stone → Iron → Veilstone → Shard-tipped arrows,
   plus Choreographed Volleys and Fletching.
8. **Wall economy yield** — gone: compartment income was removed 2026-07-06 and
   Stone Ledgers is cut.
9. **Build-cost discrepancies** — **resolved 2026-10-03:** the SO is the only
   source; `BuildCosts.cs` and `TechTree.json` no longer hold prices.

## Remaining open questions

- **Trading Outpost art** — its own Alanthor model (it still borrows the Runai
  Trading Post visual), now that it really stands beside the outcrop
  (decision 40); level models for L2 / L3.
- **Mine levels** — wire the Mine into `UpgradeBuildingCommandHelper` so
  `Mine_Lvl1..3` take effect.
