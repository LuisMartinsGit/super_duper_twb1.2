# Age 1 — Feraldis

> Military culture. Wood / Norse aesthetic. Strength comes from **raiding
> pressure, persistent gather buildings** (the Age 0 Gatherer's Hut does
> not despawn — it can be upgraded to a Hunting Lodge or Logging Station),
> a +25 % training-speed aura, and a unique **pillage** mechanic that turns
> killing enemy civilians into a supply / iron drip.
>
> **See also:** [Overview.md](Overview.md) (two-age framing), [Age_0.md](Age_0.md)
> (pre-culture starting buildings), [Age_1_Alanthor.md](Age_1_Alanthor.md) for
> the doc template, and the cross-age [Petriarchy doc TBD] for sects.
>
> Doc version: 2026-05-19 — **first-pass extract from code; the user noted
> Feraldis is one of the two most incomplete factions.** Numbers below come
> from [TechTree.json](../../Assets/Resources/TechTree.json) era 2 / Feraldis +
> [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildCosts.cs). Items marked
> **(new — not yet in code)** come from the user's design notes; items marked
> **(spec gap)** are open questions the design has not yet answered.

---

## Culture identity

| Aspect | Feraldis |
|--------|----------|
| Focus | **Military** (raiding pressure, fast training, pillage economy) |
| Style | Wood / Norse |
| Economy | **Damage-as-income** is the core mechanic — inflicting damage on other players' units / buildings generates supplies. Floor mechanic (so an isolated Feraldis player isn't softlocked): **Crystal Curse creatures and nodes count as damage targets**, so Feraldis can always farm income off the curse layer. Gatherer's Huts also **persist across age-up** as a secondary, settled supply stream — they can be upgraded to **Hunting Lodge** (wildlife synergy) or **Logging Station** (forest synergy). On top of that, the `Feraldis_Pillage` tech gives **+15 Supplies and +1 Iron per non-military kill** to the attacker's owner. |
| Vault yield modifier | **neutral** (0 %) |
| Shrine heal modifier | **−30 %** (worst of the three) |
| Fiendstone Keep HP/arrows | **+50 %** (best of the three) |
| Population model | **Feraldis pop is set to the game-cap (200) instantly at age-up** — no building is required to scale pop. Houses still exist, but **they are not the pop-cap source for Feraldis** (per Age_0 Q#6); they are now a pure **aggression tool**: every House build / upgrade spawns autonomous Raider units that immediately attack the closest enemy target. Building Houses becomes a strategic offensive decision rather than a housing necessity. Longhouse's +10 pop is irrelevant to Feraldis (already at cap). |
| Main upgrade hooks | `FiendstoneKeep` train-speed aura (+25 % at all friendly trainers); pillage drip; bloody-ground tower buff |

---

## Conventions

Same as [Age_1_Alanthor § Conventions](Age_1_Alanthor.md#conventions) — L1
through L3 building ladder, multipliers from [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs),
upgrade durations 30 s / 45 s for L2 / L3.

---

## Cultured carryover buildings

### War Hall — cultured Hall

**Doc id:** War Hall.
**Code mapping:** **The Age 0 Hall, renamed at age-up.** Same entity, same
base HP — only the display name, visual reskin, and culture-specific tech
list change (parallel to Alanthor's Town Hall and Runai's Trader's Hall).
The `main: FiendstoneKeep` line in [TechTree.json](../../Assets/Resources/TechTree.json#L928) is a stale code-era artefact and needs to be replaced with `WarHall` (or just removed — the `Hall` id covers it).

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs Age 0 base 2 400) | 2 640 | 2 760 | 2 880 |
| LoS | 24 | 24 | 24 |
| Auto-fire max targets | 1 | 2 | 4 |
| Provides population | 20 | 20 | 20 |
| Train-time multiplier | ×0.870 *(stacks with FiendstoneKeep aura +25 % at trainers)* | ×0.800 | ×0.714 |
| Build / upgrade cost | (at age-up — already standing) | 200 S + 50 I + 15 C | 400 S + 100 I + 40 C + 5 Vs |
| Upgrade duration | — | 30 s | 45 s |

#### Trainable units

| Unit | Train time | Cost | Pop |
|------|-----------|------|-----|
| **Worker** | 5 s | 50 Supplies | 1 |
| **Scout** | 4 s | 55 Supplies | 1 |

#### Researchable techs

Inherits the standard 4-tier Tools ladder + faction-wide worker buffs from
the Age 0 / Alanthor pattern (per design Q#3's "same logic" answer applied
across cultures), **plus** the two Feraldis-specific passives that today
live unattached in code (per Q#9):

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Stone tools** (T1) → **Iron tools** (T2) → **Veilstone tools** (T3) → **Veilsteel tools** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-Worker upgrade tier ([Overview.md § Per-battalion](Overview.md#per-battalion-military-upgrades-cross-faction-rule)) | *(new — same as Alanthor)* |
| **Wheel cart** | L1 | +20 % worker move speed *(faction-wide passive)* | *(new)* |
| **Cranes** | L2 | +10 carry capacity for workers *(faction-wide passive)* | *(new)* |
| **`Feraldis_Pillage`** | L1 (suggested) | Killing **non-military units** grants the attacker's owner +15 Supplies and +1 Iron per kill. "Non-military" = **Workers, Scouts, Traders (incl. Runai caravans), Raiders (auto-spawned of any kind), Litharchs** (per Q#6). Numbers TBD per playtest. | Code-existing tech, **host = War Hall** (per Q#9). |
| **Veilsteel Frenzy** *(renamed from `Feraldis_IronFury` per Q#7)* | L2 (suggested) | Feraldis units gain the ability to **carry up to 5 Veilsteel shavings**; each shaving grants +2 % attack (stacks to +10 %). **Veilsteel-only**, **Feraldis-only.** Replaces the cross-faction Iron-carry mechanic for Feraldis. *Lore: Feraldis warriors consume Veilsteel shavings as a psychoactive battle stimulant, like Norse berserkers and mushrooms.* | Code-existing tech renamed; effect rewired. |

---

### Longhouse — cultured Barracks

**Code id:** `Feraldis_Longhouse` ([TechTree.json:1040](../../Assets/Resources/TechTree.json#L1040)).
**Doc id:** Longhouse.

> Unlike Alanthor/Runai, the Feraldis cultured Barracks **is** code-defined,
> and has its own unique mechanic: **batch training**. Train units in groups
> of 5 or 10 with a 5 % cost discount and 10 % time discount.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 800) | 880 | 920 | 960 |
| LoS | 20 | 20 | 20 |
| Defense (M/R/S/Mg) | 2 / 1 / 0 / 0 | | |
| Provides population | **+10** *(Longhouse doubles as housing — stacks with the standard House pop ladder)* | +10 | +10 |
| Train-time multiplier | ×0.870 *(stacks with Fiendstone Keep +25 % train aura)* | ×0.800 | ×0.714 |
| Batch training | sizes [5, 10], −5 % cost, −10 % time | (same) | (same) |
| Upgrade cost | (at age-up — already standing) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

> TechTree.json's 1 400 HP override is **rejected per Q#11 review** —
> Longhouse follows the standard multiplier path (880 / 920 / 960). Code
> values need to drop to match.

#### Trainable units

Inherits the **Spearman → Swordsman → Royal Guard line-infantry ladder**
that the Garrison defines for Alanthor (Q#3's "same logic" rule applied
across cultures), **plus** Feraldis-specific Berserker and Warboar Rider.
Berserker fits as a parallel late-game heavy-melee unit (analogous to
Alanthor's Sentinel); Warboar Rider is Feraldis's only cavalry trainable
(there is no Royal-Stable analogue for Feraldis — cavalry stays at the
Longhouse).

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats (from code, where available) |
|----------|---------------|---------------------|-------------------|--------------------------------|
| **Spearman** | early-mid game line infantry | L1 | `Spearman` (renamed `Swordsman` per [Age_0.md](Age_0.md)) | HP 120 / 7 s train / 80 S + 30 I / pop 1 |
| **Swordsman** | mid-late game line infantry | L2 | **(new)** | TBD |
| **Royal Guard** *(or culture-specific apex name TBD)* | late-game line infantry | L3 | **(new)** | TBD |
| **Berserker** | late game **damage-dealer** (parallel to Sentinel role for Alanthor) | L2 | `Feraldis_Berserker` | HP 150 / spd 5.8 / dmg 14 melee / def 2/0/0/0 / range 1.6 / cost 110 S + 20 I + 20 C / pop 1 |
| **Warboar Rider** | cavalry (Feraldis has no Royal Stable — cavalry trains here) | L2 | `Feraldis_WarboarRider` | HP 160 / spd 7.0 / dmg 16 melee / def 1/0/0/0 / range 1.5 / cost 210 S + 80 I + 40 C / pop 1 |

> **Batch training amplifies all of the above** — Feraldis can queue
> Spearman / Berserker / Warboar Rider in batches of 5 or 10 with the cost
> + time discount. This is Feraldis's signature production advantage and
> makes the +25 % Fiendstone Keep train-speed aura even more potent.

#### Researchable techs

Same 4-tier weapon ladder as Alanthor's Garrison, plus the Feraldis-flavour
twist on Conscription. Veilsteel Frenzy lives at the **War Hall**, not here
(per Q#9).

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Conscription** | L1 | +20 % training speed at the Longhouse *(faction-wide passive)* — stacks multiplicatively with the Fiendstone Keep aura | *(new — same as Alanthor; Feraldis benefits most from this due to batch training)* |
| **Stone weapons** (T1) → **Iron weapons** (T2) → **Veilstone weapons** (T3) → **Glow-infused weapons** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-battalion weapon upgrade tier ([Overview.md § Per-battalion](Overview.md#per-battalion-military-upgrades-cross-faction-rule)) | *(new — same pattern as Alanthor)* |

---

### Thrower Camp — cultured Archery Range

**Doc id:** Thrower Camp.
**Code mapping:** The Age 0 Archery Range, renamed at age-up — same entity,
multiplier-path HP, same trainer role (per Q#3). Feraldis_Hunter trains
here.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 600) | 660 | 690 | 720 |
| LoS | 18 | 18 | 18 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Upgrade cost | (at age-up — already standing) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

#### Trainable units

3-tier ranged ladder parallel to Alanthor's Practice Range (Hunter at L1,
TBD names for L2 and L3 tiers).

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats |
|----------|---------------|---------------------|-------------------|-------|
| **Feraldis Hunter** | early-mid game ranged | L1 | `Feraldis_Hunter` (no `trainAt` in JSON today — add) | HP 100 / spd 5.7 / dmg 11 ranged / def 0/1/0/0 / range 12 / min 4 / cost 90 S + 10 I + 20 C / pop 1 |
| **L2 ranged tier** *(name TBD — "Tracker"? "Stalker"?)* | mid-late game ranged | L2 | **(new)** | TBD |
| **L3 ranged apex** *(name TBD)* | late-game ranged | L3 | **(new)** | TBD |

> **Battalion units.** Stats above are battalion totals pending size
> finalization per [Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions).

#### Researchable techs

Same 4-tier per-battalion arrow ladder as Alanthor's Practice Range
(Q#3 confirmed parity), plus the two faction-wide passives.

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Choreographed volleys** | L1 | Active skill: 2× fire rate for 5 s on Hunter battalions, 40 s cd *(faction-wide active)* | *(new — same as Alanthor)* |
| **Fletching** | L2 | +15 % attack range for all Hunter-class units *(faction-wide passive)* | *(new)* |
| **Stone-tipped arrows** (T1) → **Iron-tipped** (T2) → **Veilstone-tipped** (T3) → **Glow-tipped** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-battalion arrow upgrade tier ([Overview.md § Per-battalion](Overview.md#per-battalion-military-upgrades-cross-faction-rule)) | *(new — same pattern as Alanthor)* |

---

### House (Feraldis) — raider-spawn building (not a pop source)

Per the Q#2 / Age_0 Q#6 reviews, **Feraldis Houses exist as a raider-
spawn mechanic only**. They are **not** Feraldis's pop source — Feraldis
gets the game-cap pop (200) instantly at age-up. Houses are therefore a
**strategic offensive investment**, not a build-order necessity.

> **Every time a Feraldis House is built or upgraded (L0 → L1 → L2 → L3),
> it spawns a small batch of autonomous Raider units** that immediately
> path to and attack the closest enemy unit or structure. Raiders are
> uncontrollable, do not consume population, and persist until killed.

This turns the housing slot into pure pressure — every wave of new houses
sends a wave of free aggressors that may generate Pillage income.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 600) | 660 | 690 | 720 |
| LoS | 14 | 14 | 14 |
| Provides population | **0** *(Feraldis pop is already at 200 cap from age-up — Houses do not contribute)* | 0 | 0 |
| Build / upgrade cost | (at age-up — already standing, if the player built any Houses in Age 0) | 120 S + 25 I + 5 C | 240 S + 50 I + 15 C |
| **Raiders spawned per build / upgrade** | 1 Raider | 2 Raiders | 3 Raiders *(suggested ramp — TBD)* |

> **Age 0 House → Feraldis House transition:** Age 0 Houses do provide
> pop, but at age-up that pop "evaporates" (folded into the instant 200
> cap). Standing Houses immediately switch to the raider-spawn mode and
> spawn their L1 raider wave on the age-up transition itself.

#### Raider (auto-spawned, uncontrollable)

> **(new — no code entry yet.)** A light infantry / skirmisher class
> unit. Auto-targets closest enemy (priority order: enemy military units >
> civilians > buildings — TBD). Does **not** count toward Feraldis's
> population cap. Subset of the same Raider concept that auto-spawns from
> the persistent Gatherer's Hut at age-up.

| Field | Value (suggested — TBD) |
|------|------|
| Class | `human_melee` or `human_ranged` (TBD — likely melee given "Raider" connotations) |
| HP | TBD (suggest ≈ 80, lighter than a battalion Spearman) |
| Speed | TBD (suggest 6.0, faster than line infantry) |
| Single unit / battalion | **Single, uncontrolled** ([Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions)) |
| Cost | none (free spawn) |
| Pop | 0 |

> *(spec gap — concrete Raider stat block, plus how this same Raider unit
> reconciles with the Gatherer's-Hut-at-age-up Raider auto-spawn — same
> unit definition, or different?)*

---

### Gatherer's Hut (Age 0 carryover) — **persists for Feraldis** + spawns raiders

Per [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace),
Feraldis is the one culture whose Gatherer's Huts **persist as buildings**
across age-up (the user design note also specifies a parallel
transformation: a subset of the gatherers themselves leave the hut and
become **raider/skirmisher units that auto-patrol outward seeking
targets**). This is Feraldis's age-up power spike — roaming raiders
immediately start generating damage-income without forcing the player to
manually organize an offensive in the first 30 seconds.

The persisting building can then be upgraded into one of two cultured
forms. Per [TechTree.json:950](../../Assets/Resources/TechTree.json#L950).

> **(spec gap)** Concrete numbers for the raider transformation: how many
> raider units per hut (1? 2-3?), which unit they spawn as (a new
> `Feraldis_Raider` light unit? recycled `Feraldis_Hunter`?), auto-patrol
> radius, what they target by priority order (other players' units >
> Crystal Curse creatures > civilian wildlife?). Top open question for
> Feraldis along with War Hall and Thrower Camp existence.

**Upgrade rule (Q#4 resolved):** the player picks **exactly one** of
Hunting Lodge or Logging Station per Gatherer's Hut. Both upgrades are
**locked behind a tech** (TBD which — likely a single Hut-upgrade tech that
unlocks both choices simultaneously). Each lodge type has a terrain
preference: placement near the preferred terrain gives **+30 % yield** vs
the base hut.

#### Hunting Lodge — `Feraldis_HuntingLodge`

| Stat | Value |
|------|-------|
| HP | 1 000 |
| LoS | 18 |
| Defense | 1 / 1 / 0 / 0 |
| Upgrade cost (from Gatherer's Hut) | 160 S + 20 I |
| Terrain bonus | **+30 % yield when placed near mountains** (mountain game — boar, goat, big game) |
| Role | Upgraded hut for hunting; better in rocky / mountainous terrain. |

#### Logging Station — `Feraldis_LoggingStation`

| Stat | Value |
|------|-------|
| HP | 1 000 |
| LoS | 18 |
| Defense | 1 / 1 / 0 / 0 |
| Upgrade cost | 160 S + 20 I |
| Terrain bonus | **+30 % yield when placed near trees** (forests) |
| Role | Upgraded hut for logging; better in wooded terrain. |

> *(spec gap)* The "tech that unlocks Hut upgrades" — exact tech id, cost,
> and where it's researched (War Hall? Longhouse?). And the radius / tile
> count that counts as "near" the preferred terrain.

---

## Special / choice buildings (carried from Age 0)

| Building | Feraldis modifier | Source |
|----------|-------------------|--------|
| Vault of Almiérra | **neutral** (0 %) | [Age_0.md § Vault](Age_0.md) |
| Shrine of Ridan | **−30 %** heal rate | [Age_0.md § Shrine](Age_0.md) |
| Fiendstone Keep | **+50 %** HP and arrow count | [Age_0.md § Fiendstone Keep](Age_0.md) |

> Sanity check: if a Feraldis player picks the Fiendstone Keep as their Age 0
> choice building, the keep ends up at **3 000 HP** with extra arrow shots —
> the natural Feraldis fortress identity. If they picked Vault or Shrine
> instead, the +50 % modifier is wasted. Worth flagging in the UI ("Feraldis
> synergy") at the choice step.

---

## Feraldis-unique buildings (new in Age 1)

### Fiend Foundry — `Feraldis_Foundry`

| Stat | Value |
|------|-------|
| HP | 1 300 |
| LoS | 18 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 200 S + 80 I + 30 C |
| Role | Veilsteel forging & weapons. *(spec gap — does this overlap with Crucible / Veilsteel Foundry of the other cultures? Inputs/loss rate?)* |

### Totem Tower — `Feraldis_Tower`

| Stat | Value |
|------|-------|
| HP | 900 |
| LoS | **26** (only beaten by Alanthor Watch Tower's 28) |
| Defense | 2 / 3 / 0 / 0 |
| Build cost | 120 S + 60 I |
| Garrison slots / arrow-fire | 4 / yes |
| **Bloody-ground aura** | On bloody ground: attack ×1.25, range +2.0 ([TechTree.json:1024](../../Assets/Resources/TechTree.json#L1024)) |
| Role | Detects bloody ground (i.e. tiles where kills happened) and empowers itself. *(spec gap — bloody-ground decay rate, radius, and stacking with multiple towers.)* |

### Siege Yard — `Feraldis_SiegeYard`

| Stat | Value |
|------|-------|
| HP | 1 200 |
| LoS | 20 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 260 S + 120 I + 40 C |
| Trains | Feraldis_SiegeRam |
| Role | Train siege engines. |

---

## Feraldis units (full stat blocks from code)

### Feraldis Berserker — heavy melee

Trains at Longhouse.

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 150 |
| Speed | 5.8 |
| Training time | *(spec gap — no value in TechTree.json)* |
| Armor type | infantry_heavy |
| Damage | 14 (melee) — highest base melee dmg in Age 1 except Cataphract |
| Defense (M/R/S/Mg) | 2 / 0 / 0 / 0 |
| Attack range | 1.6 |
| LoS | 18 |
| Cost | 110 Supplies + 20 Iron + 20 Crystal |
| Pop | 1 |

### Feraldis Hunter — ranged

Trains at Thrower Camp *(if it exists — currently no trainer in code).*

| Field | Value |
|------|-------|
| Class | `human_ranged` |
| HP | 100 |
| Speed | 5.7 |
| Training time | *(spec gap)* |
| Armor type | ranged |
| Damage | 11 (ranged) |
| Defense | 0 / 1 / 0 / 0 |
| Attack range | 12 |
| Min attack range | 4 |
| LoS | 22 |
| Cost | 90 Supplies + 10 Iron + 20 Crystal |
| Pop | 1 |

### Feraldis Warboar Rider — heavy cavalry

Trains at Longhouse.

| Field | Value |
|------|-------|
| Class | `human_cavalry` |
| HP | 160 |
| Speed | 7.0 |
| Training time | *(spec gap)* |
| Armor type | cavalry |
| Damage | 16 (melee) |
| Defense | 1 / 0 / 0 / 0 |
| Attack range | 1.5 |
| LoS | 20 |
| Cost | 210 Supplies + 80 Iron + 40 Crystal |
| Pop | 1 |

### Feraldis Siege Ram — siege

Trains at Feraldis Siege Yard.

| Field | Value |
|------|-------|
| Class | `machinery_siege` |
| HP | 300 (highest-HP siege engine across cultures) |
| Speed | 3.0 |
| Armor type | ranged |
| Damage | 34 (siege) |
| Defense | 0 / 1 / 2 / 0 |
| Attack range | 1.0 *(melee range — must touch the wall)* |
| Min attack range | 0 |
| LoS | 20 |
| Cost | 280 Supplies + 140 Iron + 70 Crystal |
| Pop | 1 |

### Feraldis Iconoclast — enabler / no-attack religious unit

Trains at **Temple of Ridan, L3** (per the Runai-review Q#17 fix — the
Temple caps at 3 levels, not 4; the old "L4" reference is a retired spec
stage).

**Per Q#12 review — religious-unit tier rebalance.** All three culture
religious units (Scholar / Acolyte / Iconoclast) target a **single
"game-ender" cost bracket**: ~**300 Supplies + 150 Iron + 100 Crystal + 30
Veilsteel** (NO Glow — Glow would be a chicken-and-egg since the
Iconoclast is the one that *unblocks* Glow generation for Feraldis). Stats
already match the design intent (tanky, slow, irreplaceable).

| Field | Value |
|------|-------|
| Class | `human_magic` |
| HP | 280 *(very tanky for a religious unit)* |
| Speed | 3.2 |
| Training time | 60 s *(longest in the game)* |
| Min building lvl | **3** *(Temple of Ridan L3)* |
| Armor type | infantry_heavy |
| Damage | 0 (cannot attack) |
| Damage type | melee |
| Defense (M/R/S/Mg) | 4 / 3 / 1 / 2 |
| LoS | 16 |
| Cost | **300 Supplies + 150 Iron + 100 Crystal + 30 Veilsteel** *(per Q#12 — rebalanced; adds 30 Veilsteel)* |
| Pop | 1 |
| Single unit / battalion | **Single** ([Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions)) |
| Role | **Enabler** — strips `NodeUntargetable` from crystal nodes within `IconoclastAuraRadius` so other units can damage them. Cannot attack itself ([TechTree.json:1213](../../Assets/Resources/TechTree.json#L1213)). |

> Note: the per-culture religious units are intentionally asymmetric.
> Alanthor's Scholar **purifies** nodes, Runai's Acolyte **converts** them,
> Feraldis's Iconoclast **unprotects** them so the army can kill them. All
> three are now in the **same cost bracket** ([Overview.md § Religious-unit tier](Overview.md#religious-units--cross-faction-game-ender-tier)) — losing one is meant to be a major setback. The Crystal-Curse spec ([docs/Crystal_Curse_Sweep_And_Checklist_v2.md](../Crystal_Curse_Sweep_And_Checklist_v2.md)) already covers some of this.

---

## Feraldis-specific tech — index

Both Feraldis-specific techs are hosted at the **War Hall** per Q#9 and
documented in detail in [§ War Hall — researchable techs](#researchable-techs).
Summary:

| Tech (code id) | Doc name | Effect | Researched at |
|----------------|----------|--------|---------------|
| `Feraldis_Pillage` | **Pillage** | +15 Supplies + 1 Iron per non-military kill (Workers / Scouts / Traders / Raiders / Litharchs) | War Hall |
| `Feraldis_IronFury` → renamed **`VeilsteelFrenzy`** | **Veilsteel Frenzy** | Units can carry up to 5 Veilsteel shavings; each grants +2 % attack (stacks). **Feraldis-only; replaces the cross-faction Iron-carry bonus for Feraldis units.** | War Hall |

---

## Decisions (resolved 2026-05-19)

The original open-questions pass was reviewed and answered. Each decision
is folded into the doc body above; this block is the **decision record**
so future readers can trace why a number or rule is the way it is.
Several have **cross-faction implications** flagged at the end.

1. **War Hall existence** — **resolved.** The cultured Feraldis Hall is
   the Age 0 Hall renamed to "War Hall" (same entity, same base HP,
   culture-specific tech list). `main: FiendstoneKeep` in
   [TechTree.json](../../Assets/Resources/TechTree.json) is a stale
   artefact — drop or replace.
2. **Population / Houses** — **resolved (reversal of earlier "no Houses"
   rule).** Feraldis **does** have Houses, identical to Alanthor's base
   shape. The Feraldis twist: every build / upgrade spawns N autonomous
   uncontrollable Raider units that immediately attack the closest enemy
   target. Pop ladder: standard 15 / 20 / 25 + Longhouse +10. **Cross-
   faction impact:** updates [Age_0.md § Age-up transitions](Age_0.md#age-up-transitions),
   [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace).
3. **Thrower Camp existence** — **resolved.** Thrower Camp is the Age 0
   Archery Range renamed at age-up — same entity, multiplier-path HP,
   inherits the 4-tier arrow ladder.
4. **Gatherer's Hut → Hunting Lodge / Logging Station rules** —
   **resolved.** Player picks **one** per hut, locked behind a Hut-
   upgrade tech. **Hunting Lodge** = +30 % yield near mountains;
   **Logging Station** = +30 % yield near trees.
5. **Bloody-ground mechanic** — **deferred TBD.** Depends on technical
   aspects (decay rate, radius, stacking, interaction with Pillage).
6. **Pillage scope** — **resolved.** "Non-military units" = Workers,
   Scouts, Traders (incl. Runai caravans + trader-warriors), Raiders
   (any auto-spawned), Litharchs. Balance numbers (currently +15 Supplies
   / +1 Iron per kill) still need playtest.
7. **`Feraldis_IronFury` → Veilsteel rewrite** — **resolved.** Tech
   renamed to **Veilsteel Frenzy**; effect changed to **Veilsteel-only**
   and **Feraldis-only** (5 Veilsteel-shaving carry slots, +2 % attack
   per shaving). Lore: psychoactive consumption like Norse berserkers.
   **Cross-faction impact:** the cross-faction Iron-carry mechanic from
   [TechTree.json:1620](../../Assets/Resources/TechTree.json#L1620) is
   **removed** for everyone; Feraldis gets Veilsteel-carry exclusively.
8. **Fiend Foundry vs other Veilsteel producers** — **resolved
   directionally:** Feraldis Fiend Foundry should need **fewer inputs**
   than Alanthor Crucible / Runai Veilsteel Foundry. Exact loss-factor
   numbers TBD.
9. **Pillage / Veilsteel Frenzy research location** — **resolved.** Both
   research at the **War Hall** (faction-specific passives).
10. **Fiendstone Keep + Feraldis +50 % + Reinforced walls +20 %** —
    **resolved: modifiers stack.** Final HP = base × 1.50 × 1.20 = base
    × 1.80 (= 3 600 from a 2 000 base Keep).
11. **Longhouse HP** — **resolved.** Pick the lowest path: 880 / 920 /
    960 via the multiplier. TechTree.json's 1 400 override drops.
12. **Religious-unit tier rebalance** — **resolved.** All three culture
    religious units (Scholar / Acolyte / Iconoclast) target the same
    **game-ender** cost bracket: ~300 Supplies + 150 Iron + 100 Crystal
    + 30 Veilsteel. **No Glow** in the cost — Iconoclast is the Glow-
    unblocker for Feraldis, so requiring Glow would be a chicken-and-egg.
    **Cross-faction impact:** updates Scholar and Acolyte costs in
    [Age_1_Alanthor.md](Age_1_Alanthor.md) and [Age_1_Runai.md](Age_1_Runai.md).

## Remaining open questions

- **Raider auto-spawn stats** — concrete stat block for the auto-spawn
  Raider (from Houses + from persistent Gatherer's Huts at age-up).
  Suggested: ≈ 80 HP, speed 6.0, single-unit melee. Need final values.
- **Raiders-per-House ramp** — currently suggested 1 / 2 / 3 per
  L1 / L2 / L3 build/upgrade. Balance TBD.
- **L2 / L3 ranged tier names** at the Thrower Camp (placeholder
  "Tracker" / TBD).
- **Royal Guard apex name** — does Feraldis use the same "Royal Guard"
  name as Alanthor for the L3 line-infantry tier, or does it get a
  culture-flavoured name (e.g. "Huscarl" / "Jarl-guard" / "Thane")?
- **Hut-upgrade tech** — name, cost, and host building for the tech
  that unlocks Hunting Lodge / Logging Station.
- **Bloody-ground mechanic numbers** — Q#5 above; deferred.
- **Pillage balance numbers** — Q#6 above; deferred.
- **Fiend Foundry loss factor / inputs** — Q#8 above; deferred.

## Cross-faction follow-ups triggered by this review

These need application to **Alanthor**, **Runai**, **Overview**, and
**Age 0** docs:

1. **Veilsteel Frenzy is Feraldis-only.** The cross-faction Iron-carry
   mechanic from [TechTree.json:1620](../../Assets/Resources/TechTree.json#L1620) is removed for Alanthor and Runai; Feraldis gets Veilsteel-carry exclusively.
   → Add a resource-carry rule to [Overview.md](Overview.md).
2. **Religious units are a single game-ender cost bracket.** Bump
   Alanthor Scholar (currently 120 S + 30 C) and Runai Acolyte
   (currently 140 S + 50 C) to ~300 S + 150 I + 100 C + 30 Vs.
   → Update [Age_1_Alanthor.md](Age_1_Alanthor.md) and [Age_1_Runai.md](Age_1_Runai.md).
3. **Feraldis has Houses after all.** Reverse the "no Houses" rule that
   appeared in [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace), [Age_0.md § Age-up transitions](Age_0.md#age-up-transitions), and elsewhere — replace with "Feraldis Houses spawn auto-Raiders on build/upgrade".
4. **Building HP overrides pattern.** Both Alanthor Practice Range
   (rejected 1 500 override → 660) and Feraldis Longhouse (rejected
   1 400 override → 880) used TechTree.json overrides that the design
   explicitly drops. Codify the rule: **cultured buildings always use
   the multiplier path off the uncultured Age 0 base** — no per-culture
   HP overrides allowed.
