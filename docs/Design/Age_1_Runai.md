# Age 1 — Runai

> **Nomad traders who hold the curse sacred.** The Runai do not dig the
> curse's crystal and they do not fight it: they build Sanctuaries over it,
> pray, and in the end command it. Their wealth is never where it is made —
> supplies and iron reach the bank only when a **caravan** carries them to a
> market along the roads, and the roads themselves are taxed for veilstone at
> the Runai Waystations. Their army fires on the move, climbs walls and flies
> its siege over them.
>
> **Doc version: 2026-10-14 — second design pass (developer direction, chat
> 2026-10-14).** Supersedes the 2026-10-13 version where they disagree:
> Sanctuaries no longer produce veilstone or veilsteel (religion points only,
> traded for veilsteel); Waystations are watch towers that tax veilstone;
> trader-warriors are replaced by per-caravan **escort squads**; the trade
> research is seven upgrade paths; caravans travel the **road graph**
> ([Roads.md §0](Roads.md)); four trainers in Alanthor's shape; flying siege;
> wall scaling; **The Call**. Earlier text is in git history.
>
> **Data lives on the SOs** ([Unification decisions](Unification_Decisions_2026-10-03.md)):
> this doc keeps rules and reasons. Every cost, HP, damage, range, time,
> rate and multiplier is on the Runai SOs under
> `Assets/GameData/TechTree/Civs/Runai/` (and `Age0/Buildings/ThessarasCrossing/`)
> or on the named config asset (`RunaiTradeSystem.asset`,
> `RunaiSanctuarySystem.asset`, `RunaiCombat.asset`); on a conflict the SO
> wins. **Every Runai number is a placeholder** until the first balance pass.
>
> **Edition:** Runai ships in edition **C only** ([Alpha_Build.md §4](Alpha_Build.md)).
>
> **See also:** [Overview.md § Movement axis](Overview.md), [Age_0.md](Age_0.md),
> [Age_1_Alanthor.md](Age_1_Alanthor.md), [Age_1_Feraldis.md](Age_1_Feraldis.md),
> [Veilstone_Economy.md](Veilstone_Economy.md) §3.3, [Territory_Claims.md](Territory_Claims.md),
> [Roads.md](Roads.md), [Combat_Pacing.md](Combat_Pacing.md).

---

## 1. Identity

| Aspect | Runai |
|---|---|
| Relationship to movement | **Embodies it** — supplies and iron pay only when hauled; the roads pay veilstone |
| Relationship to the curse | **Sacred.** Sanctuaries on veilstone outcrops make religion points; the curse never targets a territory that holds one; the Sanctuary ladder ends in command of the curse (§5) |
| Economy signature | **Haul and tax** (§3) |
| Combat signature | Fires on the move, scales walls (§6.5), flies its siege (§6.4) |
| Identity-defining absences | **No walls** (palisades stop at age-up, as for Alanthor) and **no Houses** (full population at age-up; the Hut becomes the Waystation, §4.3) |

### 1.1 Look (art direction)

**Monumental sandstone** — Arabian / Egyptian temples: domes, open
colonnades, colourful **silk awnings flowing in the wind**; at the higher
levels part of the architecture is **suspended from air balloons**.
**Palette: sandstone, silver, teal (drapery), purple (decoration).**

**Buildings (2026-10-14)** are composed from the Synty Fantasy Kingdom kit by
`Scripts/Editor/PrefabAuthoring/RunaiKitAuthor.cs` (`Waning Border > Art >
Author Runai Kit Art`; check with `Render Runai Level Comparisons` →
`Temp/runai-kit-previews/`, and in play with `Waning Border > Debug > Launch
Runai Probe`). A dome is the kit's round cap stretched on a stone drum, a
balloon a silk sphere on rope tethers with a lantern basket. Every building
grows inside the same paved yard, and **from L3 each carries a
balloon-suspended part** (a hanging pavilion, a lookout, a Sky Barge on its
envelope, lantern balloons):

| Building | Form | Path |
|---|---|---|
| Fortress | domed palace, pointed iwan, minarets (2 → 4) | `Runai/Lv1..3` in the Fortress prefab |
| Route Guard | pylon-gated drill court, colonnaded hall, teal-domed towers | `Runai/Lv1..3` in the Barracks prefab |
| Trading Post | silk market stall, domed kiosk | `Runai/Lv1..3` in the Gatherer's Hut prefab |
| Arrowyard | colonnade gallery over a target range, loggia, archers' tower | own variant prefab |
| Grazing Grounds | arcaded stable, paddock, silk pavilion | own variant prefab |
| Wagonworks | silk canopy over the slip, balloon envelope, crane | own variant prefab |
| Sanctuary | teal-domed shrine, colonnade ring, lantern balloons | own variant prefab |
| Thessara's Crossing | great silver dome, four iwans, four minarets, balloons | one form |
| Waystation | road tower with lantern dome and balcony | per-level prefabs on the level SOs |
| Mine | pithead under silk, headframe, balloon hoist | `Runai/Lv1..3` in the Mine prefab |

Runai **units** still borrow existing models.

---

## 2. Age-up — Thessara's Crossing

**Thessara's Crossing is the Runai landmark** ([Age_0.md § Age-up by landmark](Age_0.md)):
building it in Age 0 is the age-up into Runai. It is filed in
`Age0/Buildings/ThessarasCrossing/` beside the Vault and the Keep; its
research is post-age-up Runai content and files in
`Civs/Runai/Buildings/ThessarasCrossing/Research/`. One per faction. It is
the **grand market** (§3.3) and the home of the trade research (§7.2). The AI
sites it like the Fiendstone Keep — at the base's chokepoint — and ignores
the wall-ring reservation (the Runai never wall it).

| Age 0 entity | Becomes | Notes |
|---|---|---|
| Shelter (`Fortress`) | **Fortress** | a market (§3.3); levels `Civs/Runai/Buildings/Fortress/Fortress_Lvl1..3` |
| Worker | Worker | builds and repairs; can cast **The Call** (§8.1) |
| Barracks | **Route Guard** | the infantry trainer |
| Hut | **Waystation** | standing Houses **collapse** at age-up; a Hut built after it is a watch tower that taxes caravans (§4.3); provides no population |
| Gatherer's Hut | **Trading Post** | supply depot (§3.1) |
| Mine | Mine | iron depot (§3.1) |
| Veilstone Mine | **Sanctuary** | converted in place (§5) |
| Temple of Ridan | Temple of Ridan | unchanged |
| Palisade | stays timber; no new palisade | as for Alanthor |

**Population:** the full game cap from age-up (`RunaiPopOverride`). **Every
standing House collapses at age-up** (2026-10-14) — the Runai need no housing,
and a Hut built from then on is a Waystation.

---

## 3. The economy — haul and tax

### 3.1 Depots and caravans

- **Supply nodes** (Trading Post) and **iron nodes** (Mine) are **depots**.
  Their output — and the territory's claim income — is **stock**, not bank
  money: it reaches the bank only when a caravan sells it at a market.
- A territory's yield goes to its depots of the matching resource; a resource
  with no matching depot is split over the territory's other depots; a
  territory with **no depot pays nothing**. Depots hold a few loads at most
  (`stockCapLoads`).
- **The start territory hauls nothing** (2026-10-14): its huts and mines are
  not depots and spawn **no wagons**, and its income reaches the bank directly,
  as in Age 0. Every other territory follows the rules above.
- **Every depot keeps caravans** (`Runai_Caravan`) — one, more with the
  Crossing research (§7.2), from the moment the faction ages up. A dead caravan is replaced
  after `caravanRespawnSeconds`, free.
- A caravan **loads**, **drives the road graph** to the market that pays the
  most per second of round trip ([Roads.md §0](Roads.md)), **sells** — cargo ×
  (1 + the **distance premium**, by road length, capped) × the market's
  multiplier — and returns. It has no orders, does not claim ground and costs
  no population.
- A caravan has **arrived** when it is near the building — within
  `arriveSlack` of the footprint edge — never inside it.
- **A caravan killed by an enemy gives its cargo to the faction that killed it**
  (2026-10-14, `CaravanLootSystem`); Feraldis pillage pays on top, as on any unit.
- **Every resource gain on the map shows as a floating "+N [icon]"** where it
  happens — a Waystation's tax as the wagon passes, a market sale, a wagon's
  looted cargo, Feraldis plunder (`ResourceGainPopups`, fed by `SimSignals.Gain`).

### 3.2 Waystations tax veilstone

- **Each time a caravan passes a Waystation of its own faction** (within
  `waystationRadius`) it pays a little **veilstone** to the bank — the road
  tax. Runai veilstone comes only from this tax: a territory's veilstone and
  veilsteel income is never stocked or hauled.
- **Spacing is enforced on the wagon:** a caravan that has paid a tax cannot
  pay another for `taxCooldownSeconds`, so Waystations built close together
  earn nothing extra — they must be far apart to tax.
- **At most 20 Waystations** per faction — the Hut SO's `maxPerFaction`.

### 3.3 Markets

| Market | Rule |
|---|---|
| **Fortress** (any of the faction's) | pays the base sale |
| **Thessara's Crossing** | pays × `crossingMarketMultiplier` |
| **Waystation** | after **Market Rights**; pays × `waystationMarketMultiplier` (below 1) |

### 3.4 Escort squads

Every caravan travels with its **escort squad** — free, no population, no
claim weight, no orders: they ride with their caravan, fight what threatens
it, and wait at the depot when it is dead. The squad grows with the **Escort**
research (§7.2):

| Escort level | Squad per caravan |
|---|---|
| 1 | 1 melee |
| 2 | 1 melee + 2 ranged |
| 3 | 3 melee + 3 ranged + 2 cavalry |

The escort units are `Runai_EscortGuard` (melee), `Runai_EscortArcher`
(ranged) and `Runai_EscortRider` (cavalry). They replace the 2026-10-13
trader-warriors.

### 3.5 Why

Alanthor holds ground and Feraldis raids it; the Runai **haul across it and
tax the roads**. Their supply and iron are only as good as their roads are
safe, their veilstone only as good as their Waystation spacing. Feraldis
hunts the caravans; the AI of every culture targets them (§9).

---

## 4. Buildings

| Building | Role |
|---|---|
| **Route Guard** (cultured Barracks) | infantry: 3 melee tiers (§6.1) |
| **Arrowyard** (`Runai_Arrowyard`) | ranged: 3 tiers, stronger than Alanthor's (§6.2) |
| **Grazing Grounds** (`Runai_GrazingGrounds`) | cavalry: 2 melee + 1 ranged cavalry (§6.3) |
| **Wagonworks** (`Runai_SiegeWorkshop`) | siege, flying (§6.4) |
| **Trading Post** (cultured Gatherer's Hut) | supply depot |
| Mine | iron depot |
| **Waystation** (cultured Hut) | §4.3 |
| **Sanctuary** (`Runai_Sanctuary`) | §5 |
| **Thessara's Crossing** | landmark, grand market, trade research |

All trainers level L1-L3 (level SOs in their folders). The **Waystation
levels** too (`Waystation_Lvl1..3`). As for every culture, a level is
**faction-wide per building type** — one level-up raises every building of
the type, and new ones are built at it
([Overview.md § Building levels](Overview.md#building-levels-faction-wide)).

### 4.3 Waystation — the Runai watch tower

- The cultured Hut. **Free placement** — anywhere on owned ground, no route
  rule. **No garrison.** **At most 20.**
- It **shoots arrows** like a watch tower; its levels (L1-L3) raise the
  attack (the authored `attack` on its level SOs).
- **Caravans within `waystationRadius` move faster** (`waystationSpeedPct`);
  Road March (§7.3) extends that to every Runai unit.
- It **taxes passing caravans for veilstone** (§3.2).
- After Market Rights it is a minor market (§3.3).
- No population.

### 4.7 Retired

`Runai_Outpost`, `Runai_TradeHub`, the old chain `Runai_TradingPost`,
`ThessarasBazaar`, `Runai_Vault`, `Runai_VeilsteelFoundry`, the
trader-warriors (`Runai_TraderWarrior`) and the Runai Catapult.

---

## 5. The Sanctuary and the curse

### 5.1 The building

- **Built atop a veilstone outcrop.** By default on an **Inactive** outcrop
  only; **Consecrated Ground** (Sanctuary research) lets it be built on a
  **Cursed** one — in ground the curse holds too (the Runai exception to the
  build gate, [Territory_Claims.md §5](Territory_Claims.md)) — where the curse
  node **sleeps** (raises no garrison).
- It **never drains the outcrop**, and it **produces only religion points**
  (`rpPerMinute`, more on a cursed outcrop).
- **Trade RP for veilsteel** at the Sanctuary: an action that spends
  `rpPerTrade` religion points for `veilsteelPerTrade` veilsteel. It is the
  Runai's only veilsteel.
- **The curse never targets a territory that holds a Runai Sanctuary** — no
  curse wave, raid or conquest is sent at it.
- Levels L1-L3 (`Sanctuary_Lvl1..3`); the ladder below is gated on them.

### 5.2 The curse ladder (Sanctuary research)

| Tech | Gate | Effect |
|---|---|---|
| **Veiled Pilgrims** | L1 | curse units never auto-target Runai caravans, escorts or workers |
| **Consecrated Ground** | L1 | Sanctuaries may be built on Cursed outcrops (§5.1) |
| **Hushed Veil** | L2, after Veiled Pilgrims | curse units notice Runai units only up close; no curse wave is sent against the faction |
| **Communion** | L2, after Hushed Veil | Runai units regenerate on curse-held ground |
| **Veilbound** | L3, after Communion | curse units near a Sanctuary join the faction, one at a time |
| **Voice of the Veil** | L3, after Veilbound | **peace with the curse** (`Alliances`) and Veilbound reaches far — the Runai control the curse |

The Shardroot rule stands above it: a Runai holder of the Shardroot loses the
peace while holding it.

---

## 6. The army

Same tiers as Alanthor: **three melee, three ranged (stronger), two cavalry
plus one ranged cavalry, and siege.** Every combat unit carries a class tag
and a weight tag ([Combat_Pacing.md](Combat_Pacing.md)).

### 6.1 Route Guard — infantry

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| Spearman | — | Infantry, Light | Cavalry | the Age 0 line unit |
| **Dune Guard** (`Runai_DuneGuard`) | L1 | Infantry, Light | Siege | line infantry |
| **Road Warden** (`Runai_RoadWarden`) | L2 | Infantry, Heavy | Cavalry | the escort; Escort Duty |
| **Sandstrider** (`Runai_Sandstrider`) | L3 | Infantry, Light | — | elite, fastest foot |

### 6.2 Arrowyard — ranged

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| **Skirmisher** (`Runai_Skirmisher`) | L1 | Ranged, Light | Infantry | javelins |
| **Slinger** (`Runai_Slinger`) | L2 | Ranged, Light | Cavalry | long reach, fast |
| **Sun Archer** (`Runai_SunArcher`) | L3 | Ranged, Light | — | top of the ladder; out-hits the Longbowman |

### 6.3 Grazing Grounds — cavalry

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| **Raider** (`Runai_Raider`) | L1 | Cavalry, Light | Ranged | fastest cavalry |
| **Horse Archer** (`Runai_HorseArcher`) | L2 | Cavalry, Ranged, Light | Infantry | ranged cavalry; fires on the move after Parthian Shot; **can hit flying machines** |
| **Camel Lancer** (`Runai_CamelLancer`) | L3 | Cavalry, Heavy | Cavalry | heavy cavalry |

### 6.4 Wagonworks — flying siege

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| **Sky Barge** (`Runai_SkyBarge`) | L1 | Siege, Flying | Building | a balloon bomber; **replaces the Catapult** |
| War Wagon (`Runai_WarWagon`) | L2 | Siege, Heavy | — | carries a squad, fires on the move |
| Siege Caravan (`Runai_SiegeCaravan`) | L3 | Siege, Heavy | Building | long-range building killer |

**Flying units** (`FlyingTag`) **ignore pathfinding** — they move in a straight
line over walls, cliffs, water and forests — and **can be targeted only by
ranged units** (damage type Ranged: archers, skirmishers, the Horse Archer…)
**and ballistas** (the Alanthor Ballista, the emplaced ballista, a tower's
ballista bolt). Melee units, splash siege and building arrows cannot touch
them.

### 6.5 Wall scaling

**Scaling Hooks** (Route Guard research): Runai **infantry** may climb onto the
**rampart** of an enemy stone wall anywhere along it — not only through a
hub, tower or gate — and fight on the deck or drop down inside. Cavalry,
siege and workers cannot.

### 6.6 Autonomous

`Runai_Caravan` and the escort units (§3.4).

---

## 7. Research

Every Runai tech is **faction-wide** (decision 31). Each trainer carries a
weapon ladder, a veterancy ladder and — in place of an armour ladder — a
**Stride ladder**; siege keeps an armour ladder.

### 7.1 Fortress

Iron / Veilstone / Veilsteel Tools · **Pack Animals** (caravans carry more) ·
**Dune Riders** (faster, farther-seeing Scouts).

### 7.2 Thessara's Crossing — the seven trade paths

| Path | Techs | Effect |
|---|---|---|
| **Speed** | Swift Wheels I-II | caravans move faster |
| **Retaliating Fire** | Wagon Archers | a caravan shoots back at what attacks it |
| **Escort** | Escort I / II / III | the escort squad (§3.4) |
| **Caravan Durability** | Strong Axles I-II | caravans are tougher |
| **Escort Durability** | Hardened Escorts I-II | escorts are tougher and hit harder |
| **Caravans per Building** | Second … Fifth Caravan | +1 caravan per depot each, to five |
| **Hunker Down** | Hunker Down | automatic: a caravan that falls to **half health stops** and gains **+80 % armour for 15 s** |

Plus **Long-Haul Tariffs** (distance premium) and **Market Rights** (§3.3).

### 7.3 Route Guard

Weapons, Stride, veterancy; **Road March** · **Escort Duty** · **Desert Cloaks**
· **Scaling Hooks** (§6.5).

### 7.4 Arrowyard

Arrow tiers, Stride, veterancy; **Fletching** (range).

### 7.5 Grazing Grounds

Lances, Stride, veterancy; **Remounts** · **Parthian Shot** · **Feigned Retreat**.

### 7.6 Wagonworks

Munitions, plating, crews; **Wagon Laager**.

### 7.7 Sanctuary

§5.2.

---

## 8. Heroes and abilities

### 8.1 The Call

Castable by **Litharchs, Workers and heroes** of a Runai faction. Every
**escort** of the faction's caravans in the caster's territory **and the
territories bordering it** is **teleported to the caster** and becomes a
**controllable army for one minute** (`callSeconds`). The caster may dismiss
it early. When it ends, the escorts **do not teleport back** — they walk to
their wagons. **Their caravans travel unescorted for the whole Call and the
walk back** — that is the price of an extra army. Long cooldown (at least
3 minutes, on the ability SO).

### 8.2 The Wayfinder

`Runai_Wayfinder`, Fortress L3, one per faction, levels 1-10 from kills:
Wayfinding (1), Mirage (3), Caravan of Swords (5), Exodus (10). Also casts
The Call.

### 8.3 The Caravan Master

`Runai_CaravanMaster`, Fortress L2, one per faction: caravans near him are
faster and carry more; **Scatter**.

---

## 9. AI

- **Every AI targets caravans**: caravan wagons are preferred targets of AI
  armies and raids.
- The Runai AI: chooses Thessara's Crossing, builds the four trainers, levels
  them, trains every tier, builds Sanctuaries and Waystations, researches
  the trade paths and the curse ladder, casts The Call when its caravans are
  attacked.

---

## 10. Open items

- The art pass (§1.1).
- Every number (all placeholders).
