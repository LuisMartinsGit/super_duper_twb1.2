# Age 0 — Tech Tree

> Authoritative design document for **Age 0** (the starting age). Where this
> document and the code disagree, **this document supersedes the code** — the
> code is to be updated to match. Missing information falls back to the code
> values noted in *Code reference* lines.
>
> **See also:** [Overview.md](Overview.md) for the game-wide framing (two-age
> structure, culture focuses, Petriarchy / sect system).
>
> Resources used in Age 0: **Supplies**, **Iron**, **Crystal**. Veilsteel and
> Glow do **not** appear in Age 0 costs (Veilsteel only as the apex Hall L3
> upgrade sink).
>
> Doc version: 2026-05-19 — derived from [TechTree.json](../../Assets/Resources/TechTree.json) Era 1 + [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs) + [BuildingCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs).

---

## Conventions

- In Age 0 the player has **not yet picked a culture**, so the standard
  buildings (Hall, Barracks, Archery Range, House) exist only in their
  **lvl 0 / pre-culture form**. They have no in-Age-0 upgrade ladder — their
  "lvl 1+" forms are the **cultured rename** that lands at age-up, and those
  per-culture stats live in the Age 1 doc (TBD). See [§ Age-up transitions](#age-up-transitions) at the end of this document for the rename mapping.
- **Choice / unique buildings** (`Vault of Almiérra`, `Shrine of Ridan`,
  `Fiendstone Keep`) are different: they are built **complete at lvl 1** in
  Age 0 and *can* be upgraded to lvl 2 / lvl 3 within Age 0 — these levels
  gate the tier-tech research.
- Upgrade costs and durations are taken from [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs:71). The existing
  L0→L3 entries for Hall / Barracks / Archery Range / Hut are post-age-up
  cultured-form data and are out of scope for Age 0. Where the doc adds a
  missing entry, it is marked **(new)** and code must be extended.
- **Population**: `popCost` = consumed by units, `provides.population` = housing.
- **Training time** is in seconds at the trainer's lvl 0 (pre-culture form).
  Trainer levels reduce train time per [TrainTimeMultiplier](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs:34) only in Age 1+.
- **Damage formula** (unchanged from TechTree.json): `finalDamage = baseDamage * dmgTypeVsArmor * (1 - defense / (defense + 100))`.

---

## Buildings

### Hall — lvl 0 (pre-culture)

The starting building. Provides economic units and core economy research.
Acts as a drop-off point for collected resources. At age-up the Hall is
**renamed and reskinned** to its cultured form (`Town Hall` / `Trader's Hall`
/ `War Hall` — see [§ Age-up transitions](#age-up-transitions)); the cultured
form then starts at lvl 1 and has its own upgrade ladder. In Age 0 only the
pre-culture lvl 0 form exists.

| Stat | Value |
|------|-------|
| HP | 2 400 |
| Line of Sight | 24 |
| Auto-fire max targets | 1 |
| Provides population | 20 |
| Build cost | starting (free) |

#### Trainable units

| Unit | Train time | Cost | Pop | Notes |
|------|-----------|------|-----|-------|
| **Worker** | 5 s | 50 Supplies | 1 | Unified Builder + Miner (see Units section). |
| **Scout** | 4 s | 55 Supplies | 1 | Moved from Barracks to Hall per overview. |

#### Researchable techs

| Tech | Cost | Time | Effect | Code id |
|------|------|------|--------|---------|
| **Stone tools** | 80 S + 40 I | 30 s | +15 % gathering speed (gatherSpeedMult 1.15) | `ImprovedTools` (rename pending) |
| **Wheel cart** | 90 S | 25 s | +5 resource carry capacity | `StorageCarts` (currently +10 in code → reduce to +5) |
| **Advance to Era II** | 1 000 S + 200 I + 150 C | — | Triggers age-up, opens culture choice. Requires 1 of `Shrine of Ridan` / `Vault of Almiérra` / `Fiendstone Keep` built. | `Research_Era2` |

---

### Barracks — lvl 0 (pre-culture)

Trains and upgrades melee units. At age-up the Barracks is **renamed and
reskinned** to its cultured form (`Garrison` / `Route Guard` / `Longhouse` —
see [§ Age-up transitions](#age-up-transitions)); the cultured form then
starts at lvl 1 and has its own upgrade ladder. In Age 0 only the pre-culture
lvl 0 form exists.

| Stat | Value |
|------|-------|
| HP | 800 |
| Line of Sight | 18 |
| Train-time multiplier | ×1.00 |
| Build cost | 220 Supplies + 40 Iron |

#### Trainable units

| Unit | Train time | Cost | Pop |
|------|-----------|------|-----|
| **Spearman** | 7 s | 80 S + 30 I | 1 |

#### Researchable techs

| Tech | Cost | Time | Effect | Code id |
|------|------|------|--------|---------|
| **Conscription** | 100 S + 40 I | 35 s | +20 % training speed at the Barracks | **(new — replaces `BasicDrills`'s +10 % melee atkspd; code needs rewire)** |
| **Stone weapons** | 80 S | 25 s | Unlocks unit upgrade 1 (Spearman tier-1 stat bump — TBD damage/defense line) | **(new — replaces `WoodenArmor`)** |

---

### Archery Range — lvl 0 (pre-culture)

Trains and upgrades ranged units. At age-up the Archery Range is **renamed
and reskinned** to its cultured form (`Longbow Grounds` / `Arrowyard` /
`Thrower Camp` — see [§ Age-up transitions](#age-up-transitions)); the
cultured form then starts at lvl 1 and has its own upgrade ladder.

Although the cultured upgrade ladder is an Age 1 affair, the Archery Range
**also carries the `BuildingUpgradeable` component in Age 0** (see [task-064 audit](../../.deft/tasks/task-codebase-audit-064/task.md))
so its **three building levels (lvl 1 / lvl 2 / lvl 3)** drive a **ranged
unit ladder**. Each upgrade unlocks one new unit tier — the unit roster grows
as the building grows. The unit ladder is logically a single block and is
documented here in Age 0 even though levels 2 / 3 are typically reached after
the culture pick.

> See: `BuildingUpgradeable` system and `BuildingUpgradeConfig.TryGetCost`
> for upgrade costs / durations.

| Stat | Value |
|------|-------|
| HP | 600 |
| Line of Sight | 18 |
| Train-time multiplier | ×1.00 |
| Build cost | 180 Supplies + 50 Iron |

#### Unit ladder (by building level)

| Building level | Unlocks | Role |
|----------------|---------|------|
| **Level 1** (base) | **Archer** | Generalist ranged unit (baseline). |
| **Level 2** | **Crossbowman** | Slow heavy-hitter — high damage per shot, low fire rate. |
| **Level 3** | **Longbowman** | Long-range sniper — outranges all other archers, slowest cooldown. |

Implementation gating uses `minBuildingLevel: 2` / `minBuildingLevel: 3`
on the training options.

#### Trainable units

Each unit is documented below with its full stat block. All numeric values
are **PLAYTEST PLACEHOLDER** until validated through playtest.

##### Archer — level 1 (baseline)

The all-purpose ranged unit. Available the moment the Archery Range is built.
Balanced fire rate, decent range, low cost. The yardstick the other two
tiers are tuned against.

| Field | Value |
|------|-------|
| Unlocked at | Archery Range **lvl 1** (base) |
| HP | **60** *(PLAYTEST PLACEHOLDER)* |
| Damage | **8** *(PLAYTEST PLACEHOLDER)* |
| Min attack range | **10** *(PLAYTEST PLACEHOLDER)* |
| Max attack range | **25** *(PLAYTEST PLACEHOLDER)* |
| Cooldown | **1.5 s** *(PLAYTEST PLACEHOLDER)* |
| Speed | **4** *(PLAYTEST PLACEHOLDER)* |
| Line of Sight | **25** *(PLAYTEST PLACEHOLDER)* |
| Population | **1** *(PLAYTEST PLACEHOLDER)* |
| Cost | (existing — see Age 0 unit stat block below) |
| Train time | (existing — see Age 0 unit stat block below) |

> Note: the legacy Archer stat block further down this doc (see
> [§ Archer](#archer)) lists the previous values (HP 90, Dmg 17, CD 2.0 s,
> Speed 5.2, LoS 30). The values above are the **new PLAYTEST PLACEHOLDER**
> proposal that ties Archer into the three-tier ladder — when these are
> committed in code (task-110 Phase 2), the older block becomes stale and
> should be reconciled.

##### Crossbowman — level 2

A slow, heavy crossbow shot. The Crossbowman trades fire rate and mobility
for a single thumping bolt that punches through tough targets. Designed to
shine against high-HP / heavy-armor units where the Archer's fast-but-light
shots get blunted by defense rolls.

| Field | Value |
|------|-------|
| Unlocked at | Archery Range **lvl 2** (`minBuildingLevel: 2`) |
| HP | **70** *(PLAYTEST PLACEHOLDER)* |
| Damage | **18** *(PLAYTEST PLACEHOLDER)* |
| Min attack range | **6** *(PLAYTEST PLACEHOLDER)* |
| Max attack range | **18** *(PLAYTEST PLACEHOLDER)* |
| Cooldown | **3.0 s** *(PLAYTEST PLACEHOLDER)* |
| Speed | **3.5** *(PLAYTEST PLACEHOLDER)* |
| Line of Sight | **22** *(PLAYTEST PLACEHOLDER)* |
| Population | **1** *(PLAYTEST PLACEHOLDER)* |
| Cost | **40 Supplies + 35 Iron** *(PLAYTEST PLACEHOLDER)* |
| Train time | **18 s** *(PLAYTEST PLACEHOLDER)* |

##### Longbowman — level 3

The long-range specialist. Slower than the Archer to ready a shot, but
significantly outranges any other archer in the game and hits hard. A
support-line unit — best protected by Spearmen and used to soften targets
before the melee engagement closes.

| Field | Value |
|------|-------|
| Unlocked at | Archery Range **lvl 3** (`minBuildingLevel: 3`) |
| HP | **55** *(PLAYTEST PLACEHOLDER)* |
| Damage | **25** *(PLAYTEST PLACEHOLDER)* |
| Min attack range | **12** *(PLAYTEST PLACEHOLDER)* |
| Max attack range | **40** *(PLAYTEST PLACEHOLDER)* |
| Cooldown | **3.5 s** *(PLAYTEST PLACEHOLDER)* |
| Speed | **4** *(PLAYTEST PLACEHOLDER)* |
| Line of Sight | **35** *(PLAYTEST PLACEHOLDER)* |
| Population | **1** *(PLAYTEST PLACEHOLDER)* |
| Cost | **50 Supplies + 40 Iron** *(PLAYTEST PLACEHOLDER)* |
| Train time | **25 s** *(PLAYTEST PLACEHOLDER)* |

#### Researchable techs

| Tech | Cost | Time | Effect |
|------|------|------|--------|
| **Choreographed volleys** | 120 S + 30 I | 35 s | Active skill: doubles fire-rate of all Archers for 5 s. 40 s cooldown. **(new)** |
| **Stone-tipped arrows** | 80 S + 20 I | 25 s | Unlocks unit upgrade 1 (Archer tier-1 damage bump — TBD line). **(new)** |
| **Fletching** | 80 S + 30 I | 30 s | +15 % range for Archers (attackRange 25 → 28.75). **(new)** |

---

### Gatherer's Hut — starts lvl 0, Age 0 only

Early supply generation, exclusive to Age 0. Must be upgraded to a Hunting
Lodge / Logging Station after age-up — if left un-upgraded it is refunded and
removed (auto-despawn 2 min after Era 2 except Feraldis, per TechTree.json).

| Stat | Value |
|------|-------|
| HP | 800 (code default) / 400 (json) — **doc: 800** |
| Line of Sight | 16 |
| Aura | +60 Supplies / minute, radius 12 |
| Build cost | 120 Supplies + 10 Iron |
| Provides population | 0 |

No level-up path. No trainable units. No tech.

---

### House (a.k.a. Hut) — lvl 0 (pre-culture)

Provides population in Age 0. At age-up the per-culture behavior splits
three ways (see [§ Age-up transitions](#age-up-transitions) for details):

- **Alanthor** — renamed and reskinned to House (Alanthor); standard pop
  ladder applies.
- **Runai** — **no House exists post-age-up**. Runai pop is set to the
  game cap (200) instantly at age-up; standing Age 0 Houses are removed.
- **Feraldis** — Houses remain but **pop becomes 0** at age-up (Feraldis
  also gets instant 200 pop). Houses convert into pure **raider-spawn
  buildings**.

| Stat | Value |
|------|-------|
| HP | 600 |
| Line of Sight | 14 |
| Provides population | 10 |
| Build cost | 80 Supplies |

> Note: The overview document uses the name **House**. Internal code id is
> `Hut` (preserved by [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs:27) and [HutTag](../../Assets/Scripts/Core/Components/BuildingComponents.cs)). Display name is "House"; internal id stays `Hut`.

No trainable units. No tech (population is its product).

---

## Special buildings (starts lvl 1)

These three are mutually-exclusive **choice buildings** in Age 0 — the player
picks one to unlock the age-up research. All three start at lvl 1 (no lvl 0
form).

### Vault of Almiérra — starts lvl 1

Acts as a resource bank. Resources can be deposited for an extended duration
and generate interest.

| Stat | L1 (base) | L2 | L3 |
|------|-----------|----|----|
| HP | 1 200 | 1 380 | 1 440 |
| Line of Sight | 14 | 14 | 14 |
| Interest rate (compounded, per minute) | **25 %** | 50 % / 75 % / 100 % (gated by *banking* tech tier) | — |
| Culture modifier | **Alanthor +30 % yield**, **Runai −30 % yield**, Feraldis neutral | | |
| Build / upgrade cost | 300 S + 100 C (build) | 200 S + 50 I + 15 C | 400 S + 100 I + 40 C **(new entry needed in [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs:71))** |
| Upgrade duration | — | 30 s | 45 s |

No trainable units.

#### Researchable techs

The three banking-grade techs are **mutually exclusive tiers** — the player
can only have one banking grade active at a time. Researching a higher
grade replaces the active interest rate.

| Tech | Building lvl req. | Cost | Time | Effect |
|------|------------------|------|------|--------|
| **Coffers** | L1 | 150 S + 40 I | 30 s | Bumps interest tier to 50 % / min. *(Safe storage — "the Vault keeps coin"  )* |
| **Merchant Charters** | L1 | 200 S + 80 I | 35 s | Bumps interest tier to 75 % / min. *(Active credit — "the Vault lends to traders"  )* |
| **Sovereign Bonds** | L1 | 250 S + 120 I | 40 s | Bumps interest tier to 100 % / min. *(High-stakes investment — "the Vault speculates"  )* |
| **Iron Subsidies** | L1 | 180 S + 80 I | 35 s | Unlocks **Iron** banking (Iron can be deposited like Supplies). |
| **Veilstone monetization** | L2 | 220 S + 100 I + 40 C | 40 s | Unlocks **Veilstone** (Crystal) banking. |
| **Veilsteel Bonds** | L3 | 300 S + 120 I + 60 C | 50 s | Unlocks **Veilsteel** banking. |

---

### Shrine of Ridan — starts lvl 1

Early religious / Litharch training building. Slowly heals all friendly units
within a 10-unit radius (1 s ticks). On build, awards **+1 Religion Point**;
+1 additional RP if the player chooses **Runai** at age-up.

| Stat | L1 (base) | L2 | L3 |
|------|-----------|----|----|
| HP | 800 | 920 | 960 |
| Line of Sight | 16 | 16 | 16 |
| Heal rate (% Max HP / s, in radius 10) | 1 % | 3 % (Heightened) → 6 % (Pious) | 15 % (Fervored) |
| Culture modifier | **Runai +30 %** heal rate, **Feraldis −30 %**, Alanthor neutral | | |
| Build / upgrade cost | 300 S + 100 C (build) | 200 S + 50 I + 15 C | 400 S + 100 I + 40 C **(new entries needed)** |
| Upgrade duration | — | 30 s | 45 s |

#### Trainable units

| Unit | Train time | Cost | Pop |
|------|-----------|------|-----|
| **Litharch** | 7 s | 100 S + 25 I + 10 C | 1 |

#### Researchable techs

| Tech | Building lvl req. | Cost | Time | Effect |
|------|------------------|------|------|--------|
| **Heightened masses** | L1 | 150 S + 40 C | 30 s | Heal rate 1 % → 3 % / s. |
| **Warrior priests** | L1 | 180 S + 50 I + 20 C | 35 s | Litharchs gain a melee attack (default melee damage TBD — suggest 6 dmg/1.5 s). |
| **Pious masses** | L2 | 220 S + 80 C | 40 s | Heal rate 3 % → 6 % / s. Requires Heightened masses. |
| **Fervored masses** | L3 | 320 S + 120 C | 50 s | Heal rate 6 % → 15 % / s. Requires Pious masses. |

---

### Fiendstone Keep — starts lvl 1

Fortified position. Generates a modest amount of Supplies (half what the Hall
produces). Trains all non-religious, non-siege military units (melee, cavalry,
ranged — no sect units, no siege, no Litharchs). Faster training time than
other buildings. Large HP pool, fires arrow volleys at enemies.

| Stat | L1 (base) | L2 | L3 |
|------|-----------|----|----|
| HP | 2 000 | 2 300 | 2 400 |
| HP with **Feraldis** | 3 000 | 3 450 | 3 600 |
| HP with **Alanthor** | 1 000 | 1 150 | 1 200 |
| Line of Sight | 18 | 18 | 18 |
| Auto-fire max targets | **4** *(Q#3 bumped from 3)* | 4 (+2 with Additional Towers) | 4 (+2) |
| Auto-fire damage / cooldown | **20 dmg / 2.0 s (range 30 — Q#3 bumped from 25)** | with **Ballista emplacement**: +18 siege dmg shot | with **Trebuchet emplacement**: +36 siege dmg AoE shot |
| Provides population | 20 | 20 | 20 |
| Train-time multiplier | ×1.00 (already 25 % faster aura per code) | ×0.870 | ×0.800 |
| Build / upgrade cost | 300 S + 100 C (build) | 200 S + 50 I + 15 C | 400 S + 100 I + 40 C **(new entries needed)** |
| Upgrade duration | — | 30 s | 45 s |

#### Trainable units

Inherits the rosters of Barracks + Archery Range (no level prerequisites — the
Keep itself is the gating building). For Age 0 this means:

| Unit | Train time | Cost | Pop |
|------|-----------|------|-----|
| **Spearman** | 7 s | 80 S + 30 I | 1 |
| **Archer** | 15 s | 50 S + 25 I | 1 |

Once an age-up culture is chosen, also trains that culture's basic cavalry
unit (Runai Raider / Feraldis Warboar Rider / Alanthor Cataphract) — gated by
that culture's Era 2 unlock, not Age 0.

#### Researchable techs

| Tech | Building lvl req. | Cost | Time | Effect |
|------|------------------|------|------|--------|
| **Ballista emplacement** | L1 | 200 S + 80 I | 35 s | Keep auto-fire gains a per-cooldown ballista shot (siege dmg, single-target). |
| **Trebuchet emplacement** | L2 | 300 S + 140 I + 50 C | 45 s | Keep auto-fire gains a trebuchet shot (siege dmg, AoE). |
| **Additional Towers** | L2 | 240 S + 80 I | 35 s | Max auto-fire targets +2. |
| **Reinforced walls** | L1 | 180 S + 60 I | 30 s | Keep HP +20 % (applied after culture modifier). |

---

## Units (Age 0)

Combat math: `finalDamage = baseDamage × dmgTypeVsArmor × (1 − defense / (defense + 100))`. Armor / damage type matrix is in [TechTree.json](../../Assets/Resources/TechTree.json#L28).

### Worker — unified Builder + Miner

> Combines the two Age 0 economy units. AI workers continue to auto-find
> deposits / building sites; player workers require an explicit command except
> for auto-chain on depletion within Line of Sight (preserved from current
> Miner / Builder behavior).

| Field | Value |
|------|-------|
| Class | `human_support` |
| HP | 70 |
| Speed | 6.0 |
| Training time | 5 s |
| Armor type | infantry_light |
| Damage | 2 (melee, self-defense only) |
| Defense (M/R/S/Mg) | 0 / 0 / 0 / 0 |
| Attack range | 1.0 |
| Line of Sight | 14 |
| **Build speed** | 1.0 |
| **Gathering speed** | 1.0 |
| **Carry capacity** | 1 (+5 with Wheel cart → 6) |
| Cost | 50 Supplies |
| Pop cost | 1 |

### Scout

| Field | Value |
|------|-------|
| Class | `human_scout` |
| HP | 60 |
| Speed | 6.0 |
| Training time | 4 s |
| Armor type | infantry_light |
| Damage | 2 (melee) |
| Defense | 0 / 0 / 0 / 0 |
| Attack range | 1.0 |
| Line of Sight | **40** (extreme vision is the role) |
| Cost | 55 Supplies |
| Pop cost | 1 |

### Spearman (replaces Swordsman)

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 120 |
| Speed | 5.5 |
| Training time | 7 s |
| Armor type | infantry_heavy |
| Damage | 10 (melee) |
| Attack speed | 1.5 s cooldown |
| Defense (M/R/S/Mg) | 1 / 0 / 0 / 0 |
| Attack range | **1.5** (slightly longer than sword — spear reach) |
| Line of Sight | 16 |
| Cost | 80 Supplies + 30 Iron |
| Pop cost | 1 |
| Notes | Bonus vs cavalry: applied via existing `melee` vs `cavalry` modifier (0.9). No extra-anti-cav bonus in Age 0 — added later via tech. |

### Archer

| Field | Value |
|------|-------|
| Class | `human_ranged` |
| HP | 90 |
| Speed | 5.2 |
| Training time | 15 s |
| Armor type | ranged |
| Damage | 17 (ranged) |
| Attack speed | 2.0 s cooldown |
| Defense | 0 / 1 / 0 / 0 |
| Attack range | 25 (28.75 with Fletching) |
| Min attack range | 10 |
| Line of Sight | 30 |
| Cost | 50 Supplies + 25 Iron |
| Pop cost | 1 |
| Active skill (post **Choreographed volleys**) | Halve cooldown to 1.0 s for 5 s, then 40 s cooldown. Faction-wide skill triggered at the Archery Range UI. |

### Litharch

| Field | Value |
|------|-------|
| Class | `human_support` |
| HP | 120 |
| Speed | 5.5 |
| Training time | 7 s |
| Armor type | ranged |
| Damage | **0 (Litharchs cannot attack — they are pure healers)**. The **Warrior priests** tech is what grants them an attack ability. *(spec gap — exact damage / cooldown when Warrior priests is researched. Q#2 resolved this rule.)* |
| Heal | 6 HP / s on target (single-target right-click heal). Shrine's *aura* heal is separate. |
| Defense (M/R/S/Mg) | 0 / 0 / 0 / 2 |
| Attack range | 10 (heal range) |
| Line of Sight | 20 |
| Cost | 100 Supplies + 25 Iron + 10 Crystal |
| Pop cost | 1 |
| Trains at | Shrine of Ridan |

---

## Age-up transitions

At age-up the player chooses a culture (Runai / Feraldis / Alanthor) and the
pre-culture buildings standing on the map are renamed, reskinned, and become
the lvl 1 form of their cultured variant. Detailed per-level stats for those
cultured forms belong to the Age 1 docs — included here only as the rename
map so the Age 0 build order can be planned forward.

| Age 0 building | Alanthor (lvl 1) | Runai (lvl 1) | Feraldis (lvl 1) |
|----------------|------------------|---------------|------------------|
| Hall | Town Hall | Trader's Hall | War Hall |
| Barracks | Garrison | Route Guard | Longhouse |
| Archery Range | Longbow Grounds | Arrowyard | Thrower Camp |
| House | House (Alanthor) — standard pop ladder | *(no House — Runai gets instant 200 pop at age-up; standing Age 0 Houses are removed)* | **House (Feraldis)** — exists as a **raider-spawn building only** (0 pop, since Feraldis also gets instant 200 pop at age-up). Every build / upgrade spawns autonomous Raider units that attack the closest enemy. |
| **Gatherer's Hut** | **transforms into a wall-segment anchor** that auto-fortifies a small radius around itself | **transforms into a mobile caravan-wagon** the player drives outward to plant their first trade post (wagons output full income *while in transit* — this **is** Runai's age-up power spike) | persists — can be upgraded to **Hunting Lodge** (wildlife synergy) or **Logging Station** (forest synergy); also see Feraldis hut-to-raider note below |

> **Transform, don't replace.** This is the cross-faction rule for the
> Gatherer's Hut at age-up — see [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace).
> The huts the player invested in during Age 0 *become* the seed of each
> faction's mechanic; they do **not** despawn. (Earlier drafts of this doc
> said "despawns 2 min after age-up with full refund" — that model is
> retired.) Each transformation is the **only** free-territory burst the
> faction ever gets; every subsequent trade-post / wall / raider after
> age-up costs builders + resources at the normal rate.

**Feraldis special case:** in addition to *also* gaining Hunting Lodge /
Logging Station upgrade paths from the persistent Gatherer's Hut, the user
design note specifies a parallel transformation — at age-up, a subset of
the Feraldis player's gatherers transform into **raider/skirmisher units**
that auto-patrol outward seeking targets. This solves Feraldis's
"cold-start" problem (damage-income only works if there's something to
damage) by handing the player roaming raiders the moment they choose the
culture. Floor mechanic: **Crystal Curse creatures and nodes count as
damage targets**, so an isolated Feraldis player can always farm
damage-income from the curse layer without contacting another player.

The three **choice buildings** keep their names across cultures (no rename at
age-up): **Vault of Almiérra**, **Shrine of Ridan**, **Fiendstone Keep**.
Their existing lvl 1 / 2 / 3 stats and tech tables apply unchanged after
age-up — culture only adjusts their numeric modifiers (Vault yield ±30 %,
Shrine heal ±30 %, Keep HP & arrows ±50 %).

---

## Decisions (resolved 2026-05-19)

The original Age 0 open-questions pass was reviewed and answered. Each
decision is folded into the doc body above; this block is the **decision
record**. Cross-faction items are flagged.

1. **Stone weapons / Stone-tipped arrows "unit upgrade 1" stat line** —
   **resolved.** These techs **unlock** a per-battalion / per-unit
   upgrade ladder (Stone → Iron → Veilstone → Glow-infused) per the
   cross-faction rule in [Overview.md § Per-battalion upgrades](Overview.md#per-battalion-military-upgrades-cross-faction-rule).
   Per-tier stat numbers TBD; see Age 1 culture docs for the full
   ladder.
2. **Warrior priests Litharch damage** — **resolved.** Litharch has
   **0 damage by default** (pure healer). The **Warrior priests** tech
   is what grants attack ability. Exact damage / cooldown post-Warrior-
   priests TBD.
3. **Fiendstone Keep base ranged stats** — **resolved.** Bump to:
   **range 30** (from 25), **max targets 4** (from 3). Damage and
   cooldown stay (20 dmg / 2.0 s). Emplacement techs still add separate
   shots per cooldown on top.
4. **Vault interest model** — **resolved.** Confirmed **compound
   interest** with the formula `next = current × (1 + rate / 100)` per
   minute. Worked example from review: at 60 % rate, depositing 100
   yields 160 after the first minute (and 256 after two minutes —
   compound, not flat).
5. **Banking tier names** — **resolved.** Three thematic names picked
   for the three rate-tiers: **Coffers** (50 %), **Merchant Charters**
   (75 %), **Sovereign Bonds** (100 %). The three resource-unlock techs
   (Iron Subsidies / Veilstone monetization / Veilsteel Bonds) keep
   their existing names.
6. **Feraldis housing in early game** — **resolved.** Feraldis pop is
   set to **200 (the game cap) instantly at age-up** — no building is
   required for pop. Houses still exist for Feraldis (per
   [Age_1_Feraldis.md § House](Age_1_Feraldis.md#house-feraldis--raider-spawn-building-not-a-pop-source)) but only as a **raider-spawn mechanic**, not a pop source.
   **Cross-faction impact:** Runai also gets instant 200 pop at age-up
   (Runai has no House at all). See [Overview.md § Population model](Overview.md#population-model-cross-faction-summary).
7. **Gatherer's Hut on age-up** — **resolved.** Huts do not despawn;
   they **transform** per culture (wall-anchor / wagon / Hunting Lodge
   or Logging Station). See [§ Age-up transitions](#age-up-transitions).

## Remaining open questions

- **Per-tier stat numbers** for the unit-upgrade ladders (Stone /
  Iron / Veilstone / Glow weapons + Stone-tipped / Iron-tipped /
  Veilstone-tipped / Glow-tipped arrows + Tools 4-tier). Numbers TBD
  per playtest; the *unlock* mechanic is set.
- **Warrior priests Litharch damage** (Q#2) — exact damage and cooldown
  TBD once the rule is "0 by default, attack granted by Warrior
  priests."
- **Fiendstone Keep range bump validation** (Q#3) — 30 range with 4 max
  targets is a meaningful buff vs the prior 25 range / 3 targets. Worth
  a playtest pass to confirm it doesn't make the Keep dominant in
  Age 0 vs raw Vault / Shrine picks.