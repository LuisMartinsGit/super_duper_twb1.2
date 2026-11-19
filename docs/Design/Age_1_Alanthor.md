# Age 1 — Alanthor

> **2026-11-07:** heroes changed. [Heroes.md](Heroes.md) is canon for hero stats, levels, auto-cast (no manual hero abilities below level 10; reach = the hero's line of sight), level-10 abilities, revival at the same level, and who carries the Shardroot. Text here that disagrees is out of date.

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
| Economy | Huts (the Guild) and Mines on territory slots, Veilstone Mines (nodes never run out — decision 55), the **Farm** and the **Market**, the **Vault** for interest, and the Guild **Surveys** as the deliberate exception that lets huts produce iron and veilstone |
| Territory | Ground is owned by standing on it ([Territory_Claims.md](Territory_Claims.md)); the Fortress and extractors lock it. Alanthor builds only on owned ground, walls included |
| Main upgrade hooks | the **Fortress** (capital, L1-L3) — the tool ladder and the **Mason Guild** building-HP passive; the **Wall Hub** — the wall levels |
| Techs | **faction-wide.** A tech applies to every unit or building it names, standing and future; there is no per-battalion upgrade (decision 31) |

---

## Conventions

- **Building levels** in Age 1 are **L1 / L2 / L3**. Level 0 is the
  pre-culture Age 0 form; **L1 is free and automatic at age-up** for every
  cultured building — Garrison, Archery Range and Guild included (decision 27).
  L2 and L3 are bought; their prices, times and multipliers are on the level
  SOs. **A level is faction-wide per building type** (2026-10-19,
  [Overview.md § Building levels](Overview.md#building-levels-faction-wide)):
  levelling one Garrison levels every Garrison, and new ones are built at it.
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
| `Alanthor_Tower` | Battle Tower | `Tower/WatchTower_Lvl1..3` |
| `Alanthor_SiegeYard` | Siege Yard | `SiegeYard/SiegeYard_Lvl1..3` |
| `Alanthor_Farm` (Age 1 only) | Farm | `Farm/Farm_Lvl1..3` (decision 57) |
| `Alanthor_Wall` — Stone Wall | Stone / Battlemented / Shielded | `Wall/Wall_Lvl1..3` |

Each level carries its **name, upgrade cost and time, HP / train-time /
attack-cooldown multipliers over the base, targets per volley, population,
slot income, trade-rate multiplier (the Trading
Outpost), supplies per Worker (the Farm), an authored attack and sight, and its model**. `BuildingUpgradeConfig` reads them first; its code tables are kept
only for Runai and Feraldis until their levels get the same treatment.

**A level belongs to the faction, per building type** (2026-10-19,
[Overview.md § Building levels](Overview.md#building-levels-faction-wide)):
one level-up raises every building of the type the faction owns, new
buildings of the type are finished at that level, and only one level-up of a
type runs at a time. The upgrade button and queue slot say it applies to
every building of the type.

**A level carries its unit tiers** (decision 53, 2026-11-11). The weapon /
armour / experience tier techs of the Garrison, Archery Range, Royal Stable
and Siege Yard, and the Fortress's tool tiers, are **not researched**: each
level SO lists the techs it grants (`BuildingLevelDefSO.grantsTechs`), and the
moment the faction holds that level of the type, those techs become
researched for it — the same completion a finished research makes, so every
effect applies exactly as if it had been bought, prerequisites ignored.

- **L1** grants arrive once the faction has completed its age-up **and** owns
  a finished building of the type (its first Siege Yard, say). **L2 / L3**
  grants arrive with the level-up itself (a level-up needs a building).
- A granted tech is **never offered as research** and the AI never asks for
  it: the building's research list skips every tech some level grants.
- Granted techs stay researched — losing every building of the type takes
  nothing back.
- The level-up prices absorb part of the folded techs' prices (on the level
  SOs); L1 stays free. The upgrade tooltip lists what the next level grants.
- Code: `FactionBuildingLevels.GrantLevelTechs` (run by
  `BuildingCultureAutoLevelSystem`), `TechCatalog.IsGrantedByLevel`.

| Building | L1 grants | L2 grants | L3 grants |
|---|---|---|---|
| Garrison | Seasoned Infantry | Iron Weapons, Iron Plate, Veilstone Weapons, Veilstone Plate, Veteran Infantry | Shard-infused Weapons, Shard Plate, Elite Infantry |
| Archery Range | Stone-Tipped Arrows, Seasoned Archers | Iron-Tipped Arrows, Iron Brigandine, Veilstone-Tipped Arrows, Veilstone Brigandine, Veteran Archers | Shard-Tipped Arrows, Shard Brigandine, Elite Archers |
| Royal Stable | Stone-Barded Lances, Seasoned Cavalry | Iron-Barded Lances, Iron Barding, Veilstone Lances, Veilstone Barding, Veteran Cavalry | Shard-infused Lances, Shard Barding, Elite Cavalry |
| Siege Yard | Stone Shot, Seasoned Crews | Iron Shot, Iron Plating, Veilstone Shot, Veilstone Plating, Veteran Crews | Shard-infused Shot, Shard Plating, Elite Crews |
| Fortress | — | Iron Tools | Veilstone Tools, Shard Tools |

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
| **Field Intelligence** | all | Fortress L3 | marks hostiles in the territories your soldiers stand in ([Territory_Claims.md §7.4](Territory_Claims.md)) |
| **Mason Guild** | Alanthor | Fortress L2 | faction-wide building HP |
| **Scouting Celestarii** | Alanthor | — | the **Celestar passive** on every Scout ([§ Scouting Celestarii](#scouting-celestarii--the-celestar-passive-2026-10-19)) |

**The tool ladder is build speed** — Alanthor builds its walls and towers
faster as it climbs it; it does not touch gathering (nobody gathers). Since
decision 53 only Stone Tools is researched: **Iron Tools** come with Fortress
L2, **Veilstone Tools** and **Shard Tools** (id `VeilsteelTools`) with
Fortress L3 ([§ Building levels](#building-levels-2026-10-02--canonical-for-where-level-numbers-live)).

#### Scouting Celestarii — the Celestar passive (2026-10-19)

**Alanthor only** (culture-gated on the tech SO, filed in
`Civs/Alanthor/Buildings/Fortress/Research/ScoutingCelestarii.asset`). It used
to unlock *Use Celestar*, an aimed active reveal; from 2026-10-19 it is a
**passive** and there is no button.

- Once researched, **every Scout** of the faction — standing and newly
  trained — carries the passive.
- **Each Scout runs it independently**, and **only while idle**: no move,
  attack, attack-move, patrol or queued order, no target, and not moving.
- An idle Scout whose passive is ready **reveals a random patch anywhere on
  the map** (uniform over the playable rectangle, drawn from the match seed,
  so every lockstep peer reveals the same patch). The patch is a circle that
  stays revealed for the ability's duration, for the faction and its team
  (shared line of sight, [Teams.md](Teams.md)).
- **Then the cooldown runs**: it starts when the reveal ends, and it keeps
  running whether or not the Scout is idle — a busy Scout simply waits until
  it is idle again before the next reveal.
- Radius, reveal duration and cooldown are the `UseCelestar` ability SO's
  `radius` (and its RevealFog effect value, kept equal) / `duration` /
  `cooldown`
  (`Age0/Buildings/Fortress/Units/Scout/Abilities/UseCelestar/UseCelestar.asset`).
- **The AI** (Alanthor) researches it early on its economy ladder and, once
  it owns it and has more than one Scout, parks one Scout beside its Fortress
  as a watcher so the passive keeps firing; the rest keep exploring.

The age-up itself is the landmark — there is no "Advance from Age 0"
research.

---

### Garrison — cultured Barracks

> **2026-11-17, decision 71 ([Unification_Decisions](Unification_Decisions_2026-10-03.md)):
> the Garrison, Archery Range, Royal Stable and Siege Yard rosters, unlock levels, upgrade
> ladders and skills were redone AoE-style — flat upgrades, three tiers, no Shard tier, units
> unlocked by building level. Where this section disagrees, decision 71 wins.**


**Code id:** `Barracks` — the Alanthor Garrison is the same entity as the
Age 0 Barracks, renamed and reskinned. Its research files under
`Civs/Alanthor/Buildings/Garrison/Research/` (still `researchAt: Barracks`).

#### Trainable units

| Unit | Gate | Role |
|------|------|------|
| **Spearman** | — | the Age 0 line unit; the anti-cavalry leg of the triangle |
| **Swordsman** (`Alanthor_Swordsman`) | L1 | the heavy line infantry; bonus vs Siege — infantry is how a siege line dies. It grows into the heavy-infantry role through the Garrison's levels (decision 52) |

**One job per unit (decision 52, 2026-11-11).** The Nobleman and the Sentinel
left the roster; their SOs and factories are parked (old saves and replays
still spawn them), but no building trains them.

Cavalry is not trained here — it is the Royal Stable's.

#### Researchable techs

All faction-wide. Gates are the tech SOs' `minBuildingLevel` on the Garrison.
The melee weapon tiers above Stone, the plate tiers and the infantry veterancy
tiers are **granted by the Garrison's levels**, not researched
([§ Building levels](#building-levels-2026-10-02--canonical-for-where-level-numbers-live)).

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Conscription** | — | faster training at every military building | |
| **Stone Weapons** | — | melee weapon tier 1 (Age 0) | |
| **Charge** | L2 | after a spell out of combat, a soldier closes with a **speed burst** and his **first strike** in that window hits harder; miss the window and it resets. The burst, the bonus and the timings are on the tech SO | |
| **Shield Wall** | L3 | a stationary-defence passive for the line infantry | Charge |

---

### Archery Range — Age 1, no cultured rename

> **2026-11-17, decision 71 ([Unification_Decisions](Unification_Decisions_2026-10-03.md)):
> the Garrison, Archery Range, Royal Stable and Siege Yard rosters, unlock levels, upgrade
> ladders and skills were redone AoE-style — flat upgrades, three tiers, no Shard tier, units
> unlocked by building level. Where this section disagrees, decision 71 wins.**


The Archery Range is `minEra: 2` and never appears in Age 0, so for Alanthor
there is nothing to rename: it is the Archery Range (SO
`Civs/Alanthor/Buildings/ArcheryRange/ArcheryRange.asset`). Its roster is
**Archer / Crossbowman** (the Longbowman left it in decision 52); the Archer's canonical id is
`Alanthor_Archer` (the bare `Archer` id survives only as a factory alias for
old scenarios and saves). It reads its **own** footprint (decision 11 note in
[Build_Grid.md](Build_Grid.md)).

#### Trainable units — the bow ladder

| Unit | Gate | Role |
|------|------|------|
| **Archer** (`Alanthor_Archer`) | L1 | the generalist bow; bonus vs Infantry closes the triangle |
| **Crossbowman** (`Alanthor_Crossbowman`) | L2 | the heavy bolt; bonus vs Cavalry — the cataphract answer, and the bow that beats armour |

The ladder's rules (range rises with tier, range never exceeds sight, no
minimum range) are in [Combat_Pacing.md § The ranged ladder](Combat_Pacing.md).

#### Researchable techs

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Choreographed Volleys** | — | a unit active carried by every ranged unit: nearby allied ranged units fire faster for a few seconds ([Spells.md](Spells.md) §9) | |
| **Fletching** | — | more attack range for every ranged unit | |
| **Arrow Volley** | — | permanent faster fire for every ranged unit | |
| **Deploy Stakes** | L3 | the first cavalry charge against an archer is blunted and partly reflected; the stakes re-plant | |

The arrow tiers (Stone at L1, Iron and Veilstone at L2, Shard at L3), the
brigandine tiers and the archer veterancy tiers are **granted by the Range's
levels** ([§ Building levels](#building-levels-2026-10-02--canonical-for-where-level-numbers-live));
Arrow Shower is cut (decision 53). The arrow tiers are visible in flight ([Combat_Pacing.md § Arrow tips](Combat_Pacing.md)).

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
| **Supply Survey I** | Guild L1 | every Guild also produces supplies, depleted Guilds included (2026-10-20, [Veilstone_Economy.md §5.2](Veilstone_Economy.md)) | |
| **Supply Survey II** | Guild L2 | more supplies | Supply Survey I |
| **Supply Survey III** | Guild L3 | more supplies | Supply Survey II |

The survey drips **scale with the hut's Guild level** (2026-08-11, "fully
developed huts are the late-game mine"): map iron deposits were finite then
(nodes never run out since decision 55), and a maxed hut ring with the top
survey was designed to carry the iron economy from there. **Since 2026-10-08 this is literal:**
every completed survey tier makes every Guild pay a flat amount of its
resource, scaled by that Guild's level, **drawn from no node** — it keeps
paying after the nodes run dry at minute 30 (Territory_Claims.md § 11.3) —
and a survey no longer multiplies the Mines. The per-tier rates and the
level scaling are on `TerritoryIncomeSystem.asset`.

**Reinforcement track** — the hut defends itself.

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Iron Reinforcements** | — | the hut auto-repairs once out of combat | |
| **Veilstone Walls** | Guild L2 | below half HP the hut casts a one-shot **Slow** burst on enemies in its gather radius (long cooldown); faster repair | Iron Reinforcements |
| **Shard Pylons** (id `VeilsteelPylons`) | Guild L3 | the burst becomes a **Stop**; faster repair still | Veilstone Walls |

Implemented in `GathererHutIncomeSystem` (surveys), `GathererHutReinforcementSystem`
(repair + slow/stop) and the level SOs.

---

### Mine

Every culture's iron Mine (Age 0 building, `Age0/Buildings/Mine/`). Alanthor
levels are `Mine/Mine_Lvl1..3` with their own slot-income ladder. The Mine
hosts **Deep Shafts** and **Rich Seams** ([Veilstone_Economy.md §6](Veilstone_Economy.md)).
**Mines level L1-L3 (2026-10-19)**, for every culture (each has its own
`Civs/<Culture>/Buildings/Mine/Mine_Lvl1..3`): L1 free at age-up, L2 and L3
bought, **faction-wide** like every building level
([Overview.md § Building levels](Overview.md#building-levels-faction-wide)) —
one level-up raises every Mine the faction owns, and its slot income with it.
The AI levels Mines with its other economy buildings.

---

### Veilstone is for elite and siege (2026-11-10, decision 48)

Alanthor's **line units cost no veilstone** — the Swordsman, Crossbowman and
Outrider are paid in supplies and iron. **Elite units carry a third** of the veilstone they
used to (the Cataphract), and **siege engines and heroes keep theirs**. The
economy pays about five supplies per veilstone at every level of play, so a line army is
always affordable; veilstone decides how much elite and siege a faction can field.

**The Veilstone Mine levels up as a real investment**: its Alanthor level SOs
(`VeilstoneMine_Lvl1..3`) carry a steep `slotIncomePerMinute` ladder, so holding outcrops
and levelling their mines is how a player chooses a veilstone economy. Prices and rates on
the SOs.

### Vault of Almiérra — the Alanthor landmark

Built in Age 0 as the landmark that ages the faction up
([Age_0.md § Vault](Age_0.md)). **Four bars of income** (decision 46, 2026-11-10):
paid-in resources fill four bars, and a full bar raises the income of the resource it
boosts; a withdrawal returns everything and empties every bar. Its Alanthor levels
(`VaultOfAlmierra/VaultOfAlmierra_Lvl1..3`) raise HP only; the Coffers and Accounts
research opens the bars and the boosts. **One Vault per faction** (2026-10-19,
[Age_0.md § Vault](Age_0.md)).

---

## Alanthor-unique buildings (new in Age 1)

These exist only after the player picks Alanthor at age-up.

### Royal Stable — `Alanthor_RoyalStable`

> **2026-11-17, decision 71 ([Unification_Decisions](Unification_Decisions_2026-10-03.md)):
> the Garrison, Archery Range, Royal Stable and Siege Yard rosters, unlock levels, upgrade
> ladders and skills were redone AoE-style — flat upgrades, three tiers, no Shard tier, units
> unlocked by building level. Where this section disagrees, decision 71 wins.**


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
| **Charge** (`CavalryCharge`) | L1 | the cavalry version of the Garrison's Charge: speed burst + harder first strike, values on the tech SO | |
| **War Horn** | L2 | unlocks the War Horn active for cavalry (`RoyalStable/Abilities/WarHorn/`) | |
| **Full Gallop** | L3 | unlocks **Galloping** (below) for every Alanthor cavalry unit, and the Full Gallop active (a speed burst during which the riders cannot attack) | War Horn |

The lance tiers, the barding tiers and the cavalry veterancy tiers are
**granted by the Stable's levels** ([§ Building levels](#building-levels-2026-10-02--canonical-for-where-level-numbers-live)).

#### Galloping (2026-11-07)

Unlocked by **Full Gallop**. Alanthor cavalry only (the Outrider and the
Cataphract); no other culture's riders gallop, and heroes do not.

1. **When.** A rider that keeps moving for a few seconds starts to gallop.
   "Moving" means covering ground at a good share of its own speed; brief
   slowdowns through a turn do not count as stopping. It stops galloping the
   moment it stops (after a short grace), and the clock starts over. How long it
   takes, the speed share and the grace are on the Full Gallop SO.
2. **Tougher.** A galloping rider takes less damage from everything, weapons,
   spells and fire alike.
3. **Harder.** It deals more damage with everything it does, its trample
   included.
4. **Trample.** It rides **through** enemy units instead of steering around
   them, and every enemy unit it crosses takes trample damage (a share of the
   rider's own attack) **once per gallop**. Allies are never hurt and are
   steered around as usual. Buildings and walls still block it.
5. **It does not stop to attack.** While galloping, a rider takes no target on
   its own: it keeps riding where it was sent and strikes any enemy unit in
   reach as it passes, on its normal attack timer, without turning or slowing.
   An explicit attack order still sends it at that target; the blow it lands on
   arrival is a galloping blow, and from then on it fights normally.

Every percentage and timing is on `Research/FullGallop.asset` (`effectsList`);
none is stated here. Code: `Civs/Alanthor/AlanthorGallop*.cs`.

### Siege Yard — `Alanthor_SiegeYard`

> **2026-11-17, decision 71 ([Unification_Decisions](Unification_Decisions_2026-10-03.md)):
> the Garrison, Archery Range, Royal Stable and Siege Yard rosters, unlock levels, upgrade
> ladders and skills were redone AoE-style — flat upgrades, three tiers, no Shard tier, units
> unlocked by building level. Where this section disagrees, decision 71 wins.**


Levels `SiegeYard/SiegeYard_Lvl1..3`. **Two engines, one job each**
(decision 52, 2026-11-11 — the Ballista and the Catapult left the roster;
their SOs and factories are parked for old saves and replays, and the wall's
emplaced Ballista and Trebuchet are wall features, untouched):

| Unit | Gate | Role |
|------|------|------|
| **Battering Ram** (`Alanthor_BatteringRam`) | L2 | buildings-only attacker, an armoured shell |
| **Trebuchet** (`Alanthor_Trebuchet`) | L3 | long-range area siege, the wall-line killer |

**Artillery doctrine (2026-08-02):** an engine may outrange its own sight; it
auto-engages only inside its line of sight, and firing at the far band needs
a spotter (scout or ally vision) plus an explicit attack order.

#### Researchable techs

| Tech | Gate | Effect (rule) | Requires |
|------|------|---------------|----------|
| **Iron-Shod Ram** | L2 | the Ram survives the approach | |
| **Ranging Shot** | L2 | an active: after standing still, the next shot hits much harder | |
| **Counterweight Tuning** | L3 | the Trebuchet reaches further | |
| **Siege Screens** | L3 | a stationary ranged-defence passive for engines | Ranging Shot |
| **Field Repair** | L3 | every siege engine repairs itself over time, all the time (rate on `FieldRepair.asset`, `RepairHpPerSecond`; `SiegeYard/FieldRepairSystem.cs`) — decision 75 | Iron-Shod Ram |

The plating tiers add melee AND ranged armour to the Ballista, Catapult and
Trebuchet (and the emplaced engines), but **ranged armour only to the Battering
Ram** — the Ram stays open to swords (decision 75, Combat_Pacing.md § Armour).

The shot tiers, the plating tiers and the crew veterancy tiers are **granted
by the Yard's levels** ([§ Building levels](#building-levels-2026-10-02--canonical-for-where-level-numbers-live));
Reinforced Bolts is cut with the Ballista (decision 53).

### Battle Tower — `Alanthor_Tower`

*(Shown as the Watch Tower until 2026-11-18; renamed when the Age 0 Watch Tower — `Lookout`, Age_0.md — took the name.)*

A stand-alone defensive tower, built directly from the worker palette (no
hut conversion). Levels `Tower/WatchTower_Lvl1..3`: each level raises its
fire rate, range and sight, and the top level adds targets and a ballista
bolt (a `BuildingSiegeShot`, so it may hit a wall piece). Every number is on
`Tower.asset` and the level SOs.

**Garrison (decision 24).** The Battle Tower has **4 garrison slots**
(`garrisonSlots`). Only **foot units** may enter — infantry and archers; not
cavalry, siege, workers or heroes. **Each occupant adds one target to the
tower's volley** (`garrisonArrowsPerOccupant`): a full tower shoots at more
enemies at once. Occupants are safe inside while the tower stands and step
out beside it when ordered out (the tower's **Empty** button).

**Garrisoning rules (2026-10-20 — supersedes "if the tower falls they die
with it").** These hold for every building with garrison slots, not only the
Watch Tower:

- **The order walks; the entry waits.** Right-clicking the tower gives the
  order at once, but a unit goes in only when it **arrives** — within the
  tower's footprint plus `entryRange` (`Age0/Buildings/Wall/WallGarrison.asset`).
  From further away it walks to the near side of the tower first. Any other
  order on the way cancels the walk; so does the tower dying, changing hands
  or filling up (the unit then stops where it is). When more men arrive than
  there are slots, the first ones in fill it and the rest stand beside it.
- **A garrisoned unit is gone from the map.** It leaves the selection on
  entry and shows nothing — no body, selection ring, ownership disc, health
  bar or decal — until it comes out.
- **A tower that falls puts its men out alive.** Whatever ends the building
  — killed, demolished with Delete, collapsed, converted, rebuilt — its
  occupants are **ejected** on free ground around where it stood
  (`ejectGap`, `ejectSpacing`, `ejectRings` on the same asset). They are never
  destroyed with it.
- **The garrison is always readable.** A manned tower shows an **`n/max`**
  badge over it at all times (gold when full) — yours and your allies'
  always, an enemy's only while you can see the tower. Your own tower shows
  it when selected even when empty.

### Farm — `Alanthor_Farm` (decisions 55 and 57, 2026-11-11)

> **2026-11-19, [decision 83](Unification_Decisions_2026-10-03.md): THE FARM GROWS ITS OWN
> HANDS.** A finished Farm that is not full produces one Worker every
> `garrisonWorkerSeconds` (`Farm.asset`), free of charge, who **starts inside it**. It needs
> population room like any unit; with none, the Farm waits and produces as soon as there is
> room. A full Farm produces nothing, and when a slot frees up the next Worker takes the whole
> interval. **The Workers who build a Farm go inside it when it is finished**, unless they
> have something else queued (a shift-queued build or command); they no longer move on to the
> next unfinished site by themselves. Code: `FarmSystem.GrowFarmhands`, `FarmWorkerClock`,
> `Farm.StepInBuilders` (called by `BuildingConstructionSystem`).

Supplies from **Workers**, paid in proportion to the **free ground around the
Farm**. Every number — price, footprint, HP, the crop ring's width, the cap and
the slots — is on `Civs/Alanthor/Buildings/Farm/Farm.asset`; the supplies per
Worker and the level prices are on its level SOs `Farm_Lvl1..3` in the same
folder.

- **Placement:** built by Workers from the build palette, on the player's own
  ground like every building, and **never on or over a resource node**.
- **Cap:** at most `maxPerFaction` Farms per player (four), counting finished
  Farms, sites and plans. At the cap the palette shows the Farm greyed and a
  placement is refused.
- **Workers inside:** a Farm holds up to `garrisonSlots` Workers (five) and
  takes **only Workers** — no soldiers, no heroes. A Worker whose walk ends
  within `farmIdleEntryRange` of the Farm (`WallGarrison.asset`) goes in even
  when the edge point it aimed for was unreachable (2026-11-11). It uses the garrison rules
  above (the order walks, the entry waits; the Workers leave the map; the
  `n/max` badge; the **Empty** button), so right-clicking a finished Farm with
  Workers selected sends them in. A damaged Farm is repaired first: the
  right-click repairs it, and only an undamaged Farm takes Workers.
  Workers inside still count against population.
- **The crop ring (decision 57):** every Farm has a field around it — a
  circle reaching the Farm SO's `cropRingMeters` out from the Farm's
  footprint edge, less the Farm's own footprint. Its **free share** is the
  part of that ring not covered by an obstacle:
  - any **building's footprint** — any faction's, finished or under
    construction (a plan is not a building and does not count; a wall counts
    at the cells it really crosses);
  - **impassable ground** — cliffs, water, slopes too steep to walk, the map
    edge, trees, rocks and forest;
  - a **resource node** (supply, iron or veilstone; a curse node stands on
    one);
  - **another Farm's land** — where two rings overlap, the land is **split
    straight down the middle** (the perpendicular bisector of the two Farms'
    centres): each point belongs to the **nearer** Farm and is free crop land
    for it and not part of the other's field, whoever owns them (an exact tie
    goes to the older Farm, the lower entity index). Nothing is lost by both
    (2026-11-11, replaces "the overlap counts against both").

  Roads and units are not obstacles. The share is counted on a fixed 1 m
  lattice of points in the ring (free points over all points, a whole
  per-mille), recomputed every couple of seconds by the simulation and kept
  on the Farm — never per frame.
- **Pays:** every minute, **Workers inside × supplies per Worker (the Farm's
  level) × the free share**, rounded down. An empty or unfinished Farm pays
  nothing. It is its own income line (`IncomeSource.Farm`), shown as
  **Farms** among the incomes (the Economy panel that listed it was removed 2026-11-17), and the Vault's
  full supply bars raise it like any income.
- **Levels (decision 57):** L1-L3, like every Alanthor level — one asset per
  level (`Farm_Lvl1..3`), L1 free at age-up, the level **faction-wide** for
  the type (one level-up raises every Farm; a new Farm is finished at it), queued
  through the building's production queue. A level raises only the supplies
  per Worker; the ring and the slots never change.
- **Placement preview:** while placing a Farm, the cursor reads **Yield NN%**
  (and what it would pay with every slot filled) and the ring is drawn on the
  ground. While placing **any** building whose footprint — or, for a Farm,
  whose ring — would cut into one of your Farms' rings, the cursor lists each
  such Farm as **Farm 84% → 71%** and draws its ring. Both use the very
  function the simulation pays with.
- **Readout:** a Farm's badge reads `workers/slots  NN%` (your own and your
  allies' Farms; your own whenever selected), and its supplies bar in the
  selection panel is what it pays now.
- **The AI** scores several legal sites by their free share and builds on the
  best (refusing any below a floor in `SimpleAISystem.asset`), keeps the ring
  off its base layout's free slots, keeps its other buildings (the drawn slots
  included) off its own Farm rings while it has room elsewhere, staffs them with
  Workers it trains for the empty slots, and levels Farms when a level repays
  its price ([Game_AI.md § 6ac](Game_AI.md)).
- **A destroyed Farm** puts its Workers out alive beside where it stood.
- **No research.**
- Code: `Farm.cs`, `FarmComponents.cs` (`FarmPay`, `FarmField`),
  `FarmSystem.cs`, `FarmRing.cs` (the ring's geometry) and `FarmRing.Field.cs`
  (the one free / blocked test the simulation, the preview and the crop
  visuals share) in that folder; the preview is
  `UI/World/FarmYieldPreview.cs`. The slots are the wall garrison's
  (`WallGarrison.MayEnter` is the one rule that differs: a Farm takes Workers,
  every other garrison foot units). The model is placeholder art (the Guild's
  level-1 prefab, on all three levels).

#### The field's look (2026-11-11)

Presentation only — it never changes what a Farm pays.

- **Crop only on free ground:** tall cereal (stalks about a metre high, with
  ears) stands on the ring's free ground and nowhere else — the same blocked
  test the pay uses (`FarmRing.Field`). Where two rings overlap each Farm's
  crop runs right up to the split down the middle: no bare ground between
  Farms.
- **Roads cut through:** no stalk stands on a road (the road mask), and the
  stalks shorten toward its rim, so a road through a field stays visible.
  Roads still do not touch the yield.
- **Grows over time:** a Farm's field starts as tilled soil with sprouts the
  moment it is finished and grows to a ripe, muted gold over a few minutes,
  faster with Workers inside (times on the config asset). A Farm first seen
  already finished shows ripe; a hostile Farm out of vision keeps the field it
  had when last seen.
- **Boundary by level:** level 1 has none; level 2 a rough wooden fence, level
  3 a low stone wall, along the ring's outer edge and beside every road
  through the field. **Between two Farms the fields either merge or are split
  straight down the middle:** Farms of the same owner or of allies merge
  completely — no boundary on the shared line, the rims run on continuously;
  against a **hostile** Farm the boundary runs straight along the split (each
  Farm's on its own side of it). A boundary
  never crosses a road (an opening where one crosses), leaves one opening on
  the rim facing the Farm's door, and simply has a gap where a building or
  impassable ground cuts its line. The layout is one pure function,
  `FarmBoundaryLayout.cs` in the Farm's folder (segments with an opening
  flag). The level-2 fence is drawn only and blocks nothing; the level-3 wall
  is a simulation object (next section).
- Code: `GameSystems/Rendering/FarmCropField/` (`FarmCropField.cs`,
  `.Fences.cs`, the `FarmCrop` / `FarmSoil` shaders and every tunable on
  `FarmCropField.asset`).

#### The level-3 wall (2026-11-11)

"Level 3 adds a low stone wall. The stone wall is an obstacle and can be
destroyed. It rebuilds automatically after some time out of combat."
(developer). Every number — HP, armour, the rebuild and out-of-combat times,
the piece length, the opening widths and the art — is on
`Civs/Alanthor/Buildings/Farm/FarmStoneWallSystem.asset`.

- **Where it runs:** the same layout as the look (`FarmBoundaryLayout`), laid
  out by the **simulation** the moment a finished Farm's faction holds Farm
  level 3 — along the ring's rim and, against a hostile Farm, along the split
  down the middle (a friendly neighbour's field merges, with no wall
  between). It does **not** follow the roads through the field (no road-edge
  pieces): the drawn road is a presentation trace the simulation does not
  know, so the simulation's wall keeps to the rim. Laid out again only when
  something that shapes it changes — a neighbouring Farm, a building in or
  near the ring, a road of the simulation road graph near it — never per
  frame. A piece whose line is unchanged keeps its HP through a re-layout.
- **Openings, open to everyone:** a gap wherever a **road of the simulation
  road graph** (Roads.md §0) crosses the line — each graph edge taken as the
  straight line between its two nodes, with `roadOpeningHalfWidth` either
  side — and one gap on the rim facing the Farm's door (`doorOpeningMeters`).
  A building or impassable ground on the line leaves a plain gap, as for the
  fence. Enemies walk through the gaps too.
- **Pieces are real:** each piece is its own entity, owned by the Farm's
  owner, with its own HP and armour; it **blocks movement** (stamped into the
  nav cost field as a footprint), so units route through the openings. It is
  **not a building**: it never counts for the building cap, the score,
  territory, victory, the AI's building lists or the road network, it cannot
  be selected, and it is **not an obstacle for the crop ring's yield** — not
  for its own Farm, not for a neighbour.
- **Anyone can damage it**, with any damage type — it is a low field wall, not
  a rampart, and the Wall Rule does not cover it
  ([Combat_Pacing.md](Combat_Pacing.md) § The Wall Rule). **Nobody attacks it
  on their own**: no unit, tower or siege engine auto-acquires a piece, and
  splash passes over it; units attack it only on an explicit attack order
  (right-click), siege included. A damaged piece shows a health bar.
- **Rebuilds itself:** a destroyed piece comes back at full HP after
  `rebuildSeconds`, once the Farm is **out of combat** — nothing has damaged
  the Farm or any of its pieces for `outOfCombatSeconds`. It never comes back
  on top of a unit (it waits until the spot is clear) or a building.
- **Goes with the Farm:** a destroyed Farm takes its pieces with it, and so
  does losing level 3; an unfinished Farm has none.
- **Last seen:** another player's pieces follow the ground rule for
  structures that are not buildings (shown where the ground has been
  explored), and are hidden whenever their Farm itself is not shown.
- Code: `FarmStoneWall.cs` (the deterministic layout and the piece factory),
  `FarmStoneWallSystem.cs` (layout, out-of-combat clock, rebuild),
  `FarmComponents.cs` (`FarmWallPiece`, `FarmWallState`, `FarmWallSlot`) and
  `PresentationSpawnSystem.FarmStoneWall.cs` (the piece's view) in the Farm's
  folder; the nav stamp is a pass of `CostFieldStampSystem`; a dead piece is
  removed by `DeathSystem` with no death record.

### Market — `Alanthor_Market` (decision 67, 2026-11-16; first built by decision 55)

**Veilstone is the money.** The Market buys and sells **supplies** and **iron** for
veilstone, at prices **every player shares**. Every number — cost, footprint, HP, the lot
sizes, the starting prices, the price step, the seller's share, the floor and ceiling, the
drift — is on `Civs/Alanthor/Buildings/Market/Market.asset`.

- **One per player** (`maxPerFaction`), built by Workers on owned ground. A faction trades
  only through its own finished Market.
- **Trades are instant and by the lot.** A lot is a fixed amount of a resource
  (`marketSuppliesLot`, `marketIronLot`). Buying a lot pays its price in veilstone and
  credits the resource; selling a lot takes the resource and pays veilstone. Nothing runs
  by itself: there are no rates and no surplus mode (decisions 55-59 are withdrawn on
  this point).
- **Every resource can be bought with veilstone and sold for veilstone.** Veilstone itself
  is not traded — it is what trades are paid in.
- **The prices move.** Each lot bought raises that resource's price and each lot sold
  lowers it, by `marketPriceStepPermille` of the price, lot by lot — the twentieth lot of
  one click is dearer (or pays less) than the first.
- **The prices are shared.** There is one price per resource for the whole match
  (`MarketPrices`). A player who dumps supplies makes supplies cheap for everybody else;
  a player who buys up iron makes iron dear for everybody else, and worth selling.
- **A sale pays less than a purchase costs** (`marketSellPercent` of the price), so
  buying and selling back always loses veilstone: the Market cannot be milked alone, only
  by reading what the others do.
- **Bounds and drift.** A price stays between `marketPriceFloorPercent` and
  `marketPriceCeilingPercent` of its starting price (`marketSuppliesLotPrice`,
  `marketIronLotPrice`), and every `marketDriftSeconds` it closes `marketDriftPermille`
  of the gap back to the starting price — a dump marks the prices for minutes, not for
  the match.
- **The floor never stops a sale** (decision 81, developer: "The market being at the floor
  price should not stop the players from selling. They just have to sell more of it.").
  At the floor each further lot pays the floor price; a seller wanting more veilstone sells
  more lots. No player, human or AI, is refused a sale for the price.
- **No debt:** a trade stops at the first lot the bank cannot pay for (veilstone on a
  purchase, the resource on a sale).
- **The panel:** one column per resource — **+** (buy a lot) on top, the resource's icon
  with the current price of a lot under it (green while cheaper than its starting price,
  red while dearer), **−** (sell a lot) below. A click trades one lot, Shift five, Ctrl
  twenty; the tooltips quote what the click costs or pays. The level-up takes the
  bottom-right cell of the 4 x 4 grid (Overview.md § The actions panel).
- **The AI watches the prices** ([Game_AI.md § 2j](Game_AI.md)): it sells a glut and
  sells into a price somebody drove up, buys what it is short of, and buys what
  somebody dumped cheap.
- **Intent:** resources become convertible. A faction rich in one resource and starved
  of another (the 15 Nov match: Expert AIs with tens of thousands of iron and no
  supplies) can rebalance, at a cost, and what it does is visible to and usable by every
  other faction.
- The trade is a lockstep command (`MarketTrade`); the shared prices live on one entity,
  so they ride saves and replays. Trades are booked as `IncomeSource.Market` /
  `SpendCategory.Trade` and are never raised by the Vault or by an AI's cheat.
- Code: `Market.cs` (the trade and price rules), `MarketComponents.cs`,
  `MarketSystem.cs` (the drift) in that folder, `CommandRouter.Market.cs`, the panel in
  `ActionsPanelPrefabBinder.Market.cs`, the AI in `SimpleAISystem.Farms.cs`.
  The model is the retired Trading Outpost's art (`Market/Art/`).

### ~~Trading Outpost — `Alanthor_TradingOutpost` (L1-L3, decision 42, 2026-11-09)~~

**RETIRED 2026-11-11 ([decision 55](Unification_Decisions_2026-10-03.md))**, with Trade
Charters, Trade Ledgers and Merchant Houses: nodes never run out, so there is nothing for a
post to hold. Its art is the Market's model. The text below is history.

Alanthor's late-game income: unlocked by **Trade Charters** (Fortress research,
Fortress L3). Alanthor mine veilstone like everyone else until then, and keep their
Veilstone Mines at age-up. The full rule is
[Veilstone_Economy.md §3.1](Veilstone_Economy.md).

- **Placement:** on one of the four side slots (N / E / S / W) of **any** resource
  node — supply, iron or veilstone — up to four per node, never beside a Cursed one.
  Every post locks its territory like an extractor.
- **Pays:** a trickle of its node's resource (rates on `TradingOutpostSystem.asset`,
  times the node's purity and the post's level), **never drawn from the node**, only
  while the node lives. A spent node collapses with every post beside it.
- **Replaces the empty-node trickle** of its node, so an unworked node with a post no
  longer wears down.
- **Per-node cost ramp:** the 2nd, 3rd and 4th post beside one node cost more to build
  (`outcropRampMultipliers`).
- **Levels:** L1 free, L2 and L3 bought (`TradingOutpost_Lvl1..3`); a level raises the
  trickle (`tradeRateMultiplier`) and HP.
- **No trades, no research of its own.** Trade Agreements, Swift Caravans and Veilsteel
  Forging / Export are deleted. Buy and sell at the Temple.
- Pairs with **Stop extracting** ([Veilstone_Economy.md §3.5](Veilstone_Economy.md)):
  stop the node's extractor before the node runs dry, and the posts keep paying.

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
| **1** | **Stone Wall** | the Alanthor culture pick — free | **gate, wall tower** | dressed stone, a low parapet on both edges; hubs are round bastions flush with the walk |
| **2** | **Battlemented Wall** | `Battlements`, at the Wall Hub | + **Ballista emplacement** | arrow-slit faces, full battlements on both edges |
| **3** | **Shielded Wall** | `ShieldedRamparts`, at the Wall Hub, after `Battlements` | + **Trebuchet emplacement** | rough-coursed faces, battlements both edges, a **roofed hoarding gallery along both faces**; hubs stay flush with the walk (they fire — below) |

Each level's HP multiplier is on its level SO (`Wall_Lvl1..3`). Level 3 also:

- **Hardens the men on it.** A foot unit standing on its own (or an ally's)
  Shielded wall gains melee and ranged armour (`WallTiers.DeckArmorBonus`). It
  is gained on the deck and lost the moment the unit steps off it.
- **Puts a tower on every hub.** A Shielded hub fires like a wall tower. Hubs
  raised after the research are towers from the start; standing hubs gain it
  with the promotion. It is still drawn **at deck height** (2026-10-20, next
  bullet list) — the hub fires, it does not grow.

**The wall is walkable.** The walk is a second nav layer (the rampart layer).
Right-click a friendly or allied stone wall with **foot units** selected and
they walk to the nearest of their side's **hubs, wall towers or gates**, climb,
and walk the deck to the clicked spot. Order them to the ground and they come
down the same way. A **breach** — a dead module — turns the modules either
side of it into ramps **anyone** can climb, enemies included. Palisades have no
deck. Cavalry, siege, workers and heroes stay on the ground. **Wall garrison
slots are retired:** units stand on the deck, visibly, fighting from it.

**On the battlements (AoE4 walls, 2026-10-20):**

- **The wall-walk is ONE corridor.** A hub's top is flush with the walk at
  every level — a round drum whose floor is the walk, its deck a disc of the
  drum's radius (`AlanthorWall.HubDeckRadius`), so the curtain, the hubs, the
  gatehouse roof and the wall towers are one continuous walk at
  `AlanthorWall.DeckHeight` and men walk straight through a hub. A hub draws
  no crown ring (it would cross the walk wherever a curtain meets it). A
  **wall tower** keeps the walk too: the module is an ordinary run and its
  two turrets hang off the faces just outside the walkway, so it is taller
  without breaking the corridor.
- **A wall-top order spreads the squad along the walk.** Each man gets his
  own deck cell, chosen along the corridor joined to the clicked cell (so the
  file follows the walk through hubs and round bends), centred on the click:
  the middle of the walk first, a staggered file a spacing apart, then the
  walk's edges when the squad is bigger than the middle. Never two on one
  cell.

- **Any piece takes the order.** A right-click on a hub, a wall tower, a gate
  or the curtain itself — its top or its face — sends the foot units to the
  deck cell nearest the click. They walk to the access point on **their own
  side** of the wall, climb, and walk the wall-walk to the spot. The access
  is the nearest one **whose wall-walk joins the spot** (2026-10-20) — never a
  nearer piece of a different, unconnected wall — and coming down, the
  nearest one on the walk they stand on.
- **They are visible and controllable up there.** A unit on the deck is drawn
  standing on the walk at deck height, is selectable (click or box), and takes
  orders: a move along the wall stays on the deck; Stop holds there; a move
  to the ground brings it down the way it went up (landing beside the access
  point, on the side it is heading for).
- **They fight from the wall.** A unit on the deck engages **only what is
  inside its reach**, whatever its stance, and never chases off the deck —
  the deck is its guard point. **Troops on top of walls have 15 % damage and
  armour buff** (the developer's rule — values on `WallGarrison.asset`): their
  hits land harder and they carry more of their own armour while they stand
  on the walk. Bowmen keep the height advantage's longer range; the wall bonus
  replaces its damage scaling rather than stacking on it
  ([Combat_Pacing.md § Fighting from a wall-walk](Combat_Pacing.md)).
  Melee on the deck can only reach men on the deck (an enemy that climbed in
  through a breach); ground melee cannot reach them.
- Simplified for now: an **ordered** attack on a target out of reach from the
  deck walks the unit along the deck towards it as far as the walk goes — it
  does not climb down to chase.

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
freely, so the art is tiled along the curve at runtime (`WallArtMesh` /
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
entity** (`AlanthorWall.GateRegionSpan`): the **middle module of the
window** becomes the gatehouse, and **the module on each side is
destroyed** — the gatehouse's own masonry occupies that ground. The curtain
mesh leaves the whole span open so nothing is drawn through it. The window
is centred on the module the player converted when it can be; next to a
hub, tower or emplacement it is shifted along the free run, and the gate
goes with it (2026-10-28 — it used to stay on the clicked module while the
window shifted, so one destroyed flank was left as a one-module hole beside
the gatehouse, the gap seen at the AI's gates).

| Property | Rule |
|----------|-------|
| Entity count | **one** — with `WallGateTag` and its own Health |
| HP | three curtain modules' worth at the faction's wall level |
| Cost / timer | on `WallGate.asset` — the same for the palisade's gate (decision 36) |
| Short segment | a segment with fewer than 3 live modules converts the modules it has; the gatehouse is built to the span it actually got |
| Doors | **two leaves that swing**, driven by the gate's open state — always drawn, never teleported |
| Opening | the centre third; the flanking thirds are solid masonry |

**Opening and closing it.** By default it opens when a friendly comes near and
closes behind them, and it never opens for a hostile. **Only men on the ground
count** (2026-10-20): a unit on the wall-walk over the gate, one climbing up or
down at an access point, and one on its way to climb never swing the doors —
none of them goes through the passage. The player can override:

| Mode | Behaviour | UI |
|------|-----------|-----|
| **Auto** (default) | opens for nearby friendlies, closes when they leave | the gate's action panel shows **Close Gate** |
| **Sealed** | stays shut, *including to friendlies* | the panel shows **Open Gate**; the doors close on the spot |

The toggle is one order (`SetGateLock`, lockstep type 44). Sealing does not
make the gate tougher — it is a pathing decision, not a defensive one.

### Wall towers

A **wall tower** is a single module converted to a ranged tower on the
wall — a run of curtain with a turret hanging off each face, the walk passing
between them (2026-10-20) (`Alanthor_WallTower`, SO `Wall/Tower/WallTower.asset`; unrelated to the
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

> **Re-authored 2026-10-20 in the House's architecture**
> (docs/Design/Art_Direction.md § 13): one FBX + .blend per part per level,
> `alanthor_StoneWall_<Part>_L1..3` in `Civs/Alanthor/Buildings/Wall/Art/`,
> built by `tools/blender/alanthor/build_walls.py` (curtain, hub, gatehouse,
> wall tower, ballista and trebuchet mounts) and bound per level in
> `Age0/Buildings/Wall/WallModuleArt.asset` (slot 0 = the palisade: its timber
> curtain / hub / gate plus `Palisade_<Part>_L0` tower and mounts). Every
> level is symmetrical — both faces carry the same plaster-over-brick
> facade and parapet; L2 adds battlement hoods along both parapets, L3 posts
> and lanterns. The Synty castle-kit set (`Stone/`, Author Castle Prefabs)
> is retired.

**Where things live.** The shared hub/segment CODE, the Gate (a palisade takes
gates too), the timber art and `WallModuleArt` stay in `Age0/Buildings/Wall/`
— one machine, two kinds of content; the Stone Wall's content (`WallHub`,
`WallSegment`, `Tower/`, `Art/`, the level SOs and the `Battlements` /
`ShieldedRamparts` research) is in `Civs/Alanthor/Buildings/Wall/`. Every wall
PIECE has its own SO and `presentationId`, but its ART comes only from its
WallModuleArt slot for the level; an empty slot draws nothing and logs
`[Wall] no art for …` (there is no procedural wall any more, 2026-10-20).

**Textures do not travel in the FBX, and must not.** The FBX carries
**geometry, UVs and material NAMES**, and each name is remapped to a `.mat`
asset in the piece's folder (`Wooden_Stakes`, `Player_cloth` — base colour
stays white, the player's colour is applied on top — and `GroundMaterial`).
Re-exporting the mesh cannot disturb them, because the link is by name.
**Binding:** run **`Waning Border > Art > Bind Building Art (SO only)`** after
adding or replacing an FBX; it re-applies the importer settings (Read/Write on)
and the House material remap a Blender re-export resets, and fills the
WallModuleArt slots from the file names. Artist steps: [docs/Art_Pipeline_FBX.md](../Art_Pipeline_FBX.md).

**How it is drawn.** Still **one mesh per segment**: the authored module baked
along the curve, one copy per sim cell (`WallArtMesh`).
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

- **Swordsman and the Royal Stable are settled** (decision 30) — no longer
  "TBD".
- **One job per unit** (decision 52): the army is two units a kind —
  Spearman / Swordsman, Archer / Crossbowman, Outrider / Cataphract,
  Battering Ram / Trebuchet. The Sentinel, Nobleman, Longbowman, Ballista and
  Catapult train nowhere (their SOs and factories are parked).
- **Cut, never to be documented again** (decision 30): Royal Guard, L2 / L3
  cavalry tiers, the Academy tech, the Wheel cart, Stone Ledgers, the
  Crucible. The Holy Scholar is cut (decision 33); the Smelter is removed
  (decision 1).
- **Weight tags** (decision 35): Outrider Light; Cataphract Heavy; Archer
  and Crossbowman Light; Trebuchet, Battering Ram and the emplaced engines
  Heavy ([Combat_Pacing.md](Combat_Pacing.md)).

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
2. **Garrison roster** — Spearman, Swordsman (L1) since decision 52 (the
   Nobleman and Sentinel are gone); cavalry moved to the **Royal Stable** (Outrider L1, Cataphract L3).
   The Royal Guard is cut (decision 30).
3. **Archery Range** — standard cultured-building path, no population; levels
   on the level SOs.
4. **Tech ladders** — originally "unlock a per-battalion upgrade". **Superseded
   2026-10-03 (decision 31):** every weapon / armour / arrow / tool / veterancy
   tier is a faction-wide tech — **granted by the building's level** since
   decision 53, never researched.
5. **Glow tiers** — superseded: the fourth tier of every ladder is the
   Shard tier (veilstone-priced since 2026-11-09).
6. **Mason Guild** — canonical name; faction-wide building HP (value on the
   tech SO).
7. **Archery Range ladder** — Stone → Iron → Veilstone → Shard-tipped arrows
   (granted by the levels, decision 53), plus the researched Fletching, Arrow
   Volley, Choreographed Volleys and Deploy Stakes.
8. **Wall economy yield** — gone: compartment income was removed 2026-07-06 and
   Stone Ledgers is cut.
9. **Build-cost discrepancies** — **resolved 2026-10-03:** the SO is the only
   source; `BuildCosts.cs` and `TechTree.json` no longer hold prices.

## The Garrison, restructured (2026-11-13)

Developer's rules; they supersede item 4 above and the earlier Garrison tech
lists for the Garrison only. Every value is on the unit and tech SOs.

- **Unit level = building level.** The Garrison's units have four levels: Age 0
  (the Spearman only), then Garrison 1, 2 and 3. Each level changes the unit's
  look and raises its defensive stats a little: HP, armour of every type, move
  speed and line of sight. The step is granted by the Garrison's level
  (`grantsTechs`: Seasoned / Veteran / Elite Infantry, which are no longer
  shown as researches). The Swordsman's own SO stats are his Garrison 1 stats.
- **The plate tiers are gone** (Iron / Veilstone / Shard Plate): armour is part
  of the unit level.
- **Four weapon technologies, one per building level**, each requiring the one
  before: Stone Weapons (Barracks, Age 0), Iron (Garrison 1), Veilstone
  (Garrison 2), Shard-infused (Garrison 3). They are RESEARCHED, not granted,
  share one cell of the panel, and apply to all infantry. Each has its own
  weapon look on the units.
- **Two passives for the Spearman**, researched: Charge (Garrison 2) and Shield
  Wall (Garrison 3). They no longer apply to the Swordsman.
- **Three passives for the Swordsman**, researched: **Parry** (turns one melee
  attack aside completely, then rests), **Counter** (part of every melee blow
  that lands on him is dealt back) and **Reckless Attack** (every few swings he
  also hits all enemies round him, and is briefly vulnerable after). Code:
  `ParryState` / `CounterState` / `RecklessState` in `AlanthorCombatPassives.cs`,
  applied at the damage site in `CombatDamageHelper`.
- **The Age 0 Spearman is the Militia.** Same unit SO (`Spearman`): its display
  name is Militia until the faction has a culture and Spearman from then on.
- **The Swordsman does not exist in Age 0.**
- **Panel** (the 4 x 4 grid every building uses, Overview.md § The actions
  panel, 2026-11-18): row 1 the building (Conscription and its research
  ladders, one cell each); row 2 the Spearman followed by his skills; row 3
  the Swordsman followed by his; row 4 free, its last cell the level-up
  (`BuildingActionLayouts`, key `Barracks`; which skills are listed there is
  the layout's, the SOs gate them). Unit cells show a rendered picture
  of the unit at the building's level, the weapon cell a picture of the weapon
  (`ActionIcons`, rendered by `UnitGearIcons`). A building's menu uses the
  builder's grid.

## Remaining open questions

- ~~**Trading Outpost art**~~ — moot: the Trading Outpost is retired (decision 55).
