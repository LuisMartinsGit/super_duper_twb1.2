# Age 1 — Runai

> Economy culture. Tents / desert aesthetic. Runai **embodies movement** —
> their economy, army, and territory are all fused into the **trade lane**.
> Build a lane → it earns supplies → it auto-spawns patrolling
> trader-warriors → it claims that corridor of the map. One decision, three
> rewards. Runai has **no walls** and **no Houses**; lanes are their
> defense and caravan-driven mechanics are their pop curve.
>
> **See also:** [Overview.md § Movement axis](Overview.md#north-star-the-movement-axis)
> (the three-way faction triangle), [Age_0.md](Age_0.md) (pre-culture
> starting buildings), [Age_1_Alanthor.md](Age_1_Alanthor.md) for the doc
> template, and the cross-age [Petriarchy doc TBD] for sects.
>
> Doc version: 2026-05-19 — Runai mechanics formalized from the **trade-lane
> design pass** (see [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace)).
> Numbers below come from [TechTree.json](../../Assets/Resources/TechTree.json)
> era 2 / Runai + [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs).
> Items marked **(new — not yet in code)** come from the user's design notes;
> items marked **(spec gap)** are open questions the design has not yet
> answered.

---

## Culture identity

| Aspect | Runai |
|--------|-------|
| Focus | **Economy / movement** — lanes are economy + army + territory fused |
| Style | Tents / Desert / nomadic traders |
| Economy | **Mixed: Iron is still mined, Supplies + Crystal come from trade routes.** Workers mine iron normally. Supplies and Crystal are earned exclusively through **trade lanes** — plant a Trade Post → a lane forms between it and any other Trade Post or the Trader's Hall → caravans travel the lane and earn the trade-only resources (yield scales with route length). On top of that, lanes **auto-spawn trader-warriors** that patrol the lane generating Crystal + Supplies passively ([§ Trader-warriors](#trader-warriors-uncontrollable-lane-patrols)). Veilsteel produced at the Veilsteel Foundry (Iron + Crystal, same rate as Alanthor's Crucible). |
| **No walls** | **Identity-defining absence** ([Overview.md § Movement](Overview.md#north-star-the-movement-axis)) — Runai cannot build any static defensive wall. The lane network *is* their defense; trader-warriors patrolling lanes are what holds territory. Don't ever hand out "just a small palisade." |
| **No Houses** | **Identity-defining absence.** No House building exists for Runai. Instead, **full population is unlocked at age-up** (one big swing, no gradual ramp). Possibly tech-gated trench upgrades expand pop further. *(spec gap — exact pop ceiling at age-up + any tech-gated bumps.)* |
| Crystal-Curse relationship | Runai **grow increasingly neutral to the Crystal Curse over time.** Likely **earned through exposure**: the more trader-traffic walks through cursed land, the more resistant the civ becomes. This makes their lore consequential and gives Runai a positive reason to deliberately route through danger. *(spec gap — exact trigger model: tile-hours of traffic? caravan-passes? tech-gated?)* |
| Vault yield modifier | **−30 %** (worst of the three cultures — Runai is supposed to win by *flowing* supplies, not banking them) |
| Shrine heal modifier | **+30 %** (best of the three) |
| Fiendstone Keep HP/arrows | neutral (0 %) |
| Main upgrade hooks | `ThessarasBazaar` `TariffBoostAura` (numeric value TBD); pack-and-move mobility |

### Age-up power spike: the wagon burst

At age-up, **each Gatherer's Hut the player built in Age 0 transforms into a
mobile caravan-wagon**. These wagons are Runai's **one and only free
trade-post deployment burst** — every Trade Post built after age-up costs
builders + resources at the normal rate. *(Possible future expansion: allow
infantry to build Trade Posts as well, so Runai can fight their way into
contested zones late-game.)*

**Wagons output their full income while in transit.** This makes the age-up
moment Runai's **peak income spike** of the entire match. Every wagon is
briefly maxed out simultaneously; as wagons reach destinations and "settle"
into Trade Posts, income drops to whatever the placement's lane network
justifies. The mechanic naturally creates Runai-specific decisions:

- **Drive far** to extend the transit-spike window (high skill ceiling).
- **Plant quickly** to lock in a known income floor (safe play).
- **Re-route mid-transit** if the map situation changes (adaptive play).

It's also a *recurring* mechanic, not a one-shot trick: any time the
player builds a future wagon (or repositions an existing trade post), the
transit period is a mini-spike. Runai players who micro their network well
get rewarded with sustained higher income through clever re-routing — the
"nomadic master traders" fantasy.

**Wagon-death cost** = (a) one Gatherer's Hut worth of material, plus (b)
the **tempo cost** of having to send a builder out the slow way to
re-establish that trade post manually. The trade post can still be built —
the player just lost the free shortcut. This is harsh enough to make
**escorting wagons matter** but soft enough to keep the game going if a
wagon dies.

> **Playtest heuristic** (from [Overview.md](Overview.md#age-up-transform-dont-replace)):
> in a 1v1 Feraldis-vs-Runai match, Runai should land roughly **70–80 %**
> of age-up wagons at destination with reasonable escort. Lower = Feraldis
> is too punishing on the transition; near 100 % = escorting is decorative.

**Transit-spike model (Q#9 resolved):** wagon output **decays linearly over
4 minutes** from full to zero. A wagon driving for the full 4 minutes
contributes an integral equivalent to one well-placed Gatherer's Hut over
that period. **If the player placed their Age 0 Gatherer's Huts well, the
spike is invisible** — the 4-minute decay matches the natural settlement
rate. If the player drives wagons aggressively far, they extend the
spike's *useful* duration at the cost of placement quality.

**Wagon count (Q#10 resolved):** **no hard cap on age-up wagons** — the
count equals the number of Gatherer's Huts built in Age 0. The
self-balancing pressure is the **Age 0 opportunity cost**: each hut costs
supplies + worker time that the player can't spend on military or research
in Age 0. Over-investing in huts means a smaller standing army at age-up
and fewer resources gathered, so the system caps itself naturally.

---

## Conventions

Same as [Age_1_Alanthor § Conventions](Age_1_Alanthor.md#conventions) — L1
through L3 building ladder, multipliers from [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs),
upgrade durations 30 s / 45 s for L2 / L3.

---

## Cultured carryover buildings

### Trader's Hall — cultured Hall

**Doc id:** Trader's Hall.
**Code mapping:** the Age 0 Hall, renamed at age-up — same entity, same
base HP (parallel to Alanthor's Town Hall / Feraldis's War Hall). `ThessarasBazaar` in [TechTree.json:528](../../Assets/Resources/TechTree.json#L528) is no longer Trader's Hall — it has been **repurposed as a separate Age 1 unique building** ([§ Thessara's Bazaar](#thessaras-bazaar--trade-lane-upgrade-house)) for trade-lane upgrades only.

**Trader's Hall trains only economy units** (Worker, Scout). All Runai
military training is split out to the cultured carryover buildings (Route
Guard / Arrowyard) and the new Grazing Grounds cavalry building.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs Age 0 base 2 400) | 2 640 | 2 760 | 2 880 |
| LoS | 26 | 26 | 26 |
| Auto-fire max targets | 1 | 2 | 4 |
| Provides population | 20 | 20 | 20 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Build / upgrade cost | (at age-up — already standing) | 200 S + 50 I + 15 C | 400 S + 100 I + 40 C + 5 Vs |
| Upgrade duration | — | 30 s | 45 s |

> The TechTree.json override of 2 700 HP / +40 pop / `PackAndMove` /
> `TariffBoostAura` on `ThessarasBazaar` no longer applies to Trader's Hall.
> Those mechanics either move to Thessara's Bazaar (the repurposed building)
> or get retired entirely per the Q-block resolutions below.

#### Trainable units

| Unit | Train time | Cost | Pop |
|------|-----------|------|-----|
| **Worker** | 5 s | 50 Supplies | 1 |
| **Scout** | 4 s | 55 Supplies | 1 |

> Runai military units (Spearman, Skirmisher, Raider, cavalry archer) train
> at Route Guard / Arrowyard / Grazing Grounds — see those sections below.

#### Researchable techs

Runai gets a Tools ladder analogous to Alanthor's (Q#4 / Q#5 review:
"similar tech, different names; some may be universal at Age 0"), plus the
two Crystal-Curse-neutrality techs (Q#16).

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Stone tools** (T1) → **Iron tools** (T2) → **Veilstone tools** (T3) → **Veilsteel tools** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-Worker upgrade tier ([Overview.md § Per-battalion](Overview.md#per-battalion-military-upgrades-cross-faction-rule)) | *(new — same shape as Alanthor; names may end up Runai-flavoured)* |
| **Wheel cart** / **Cranes** equivalents | L1 / L2 | Faction-wide worker buffs — names TBD; possibly identical to Alanthor's if universal | *(new, TBD)* |
| **Veilstride** *(placeholder name — Q#16)* | L2 | −20 % chance of aggroing curse waves when Runai units traverse cursed tiles. Curse defences stay in place (the curse doesn't want to be *converted*, only *cleansed* / *destroyed*). | *(new — Q#16 resolved)* |
| **Lane Caravan tech** | various | Runai-only — see [§ Thessara's Bazaar](#thessaras-bazaar--trade-lane-upgrade-house) | — |

---

### Route Guard — cultured Barracks

**Doc id:** Route Guard.
**Code mapping:** the Age 0 Barracks, renamed at age-up — same entity,
multiplier-path HP. Hosts Runai's infantry roster (per Q#1). Cavalry
trains at the new [Grazing Grounds](#grazing-grounds--cavalry-trainer-new),
not here.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 800) | 880 | 920 | 960 |
| LoS | 18 | 18 | 18 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Upgrade cost | (at age-up — already standing) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

#### Trainable units

Runai gets a **3-tier infantry ladder** parallel to Alanthor's Garrison
(per Q#5 "similar tech ladder as Alanthor"). Code defines Runai Spearman;
the L2 / L3 tiers are new and culture-flavoured (placeholder names below).

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats (from code, where available) |
|----------|---------------|---------------------|-------------------|--------------------------------|
| **Runai Spearman** | early-mid game line infantry | L1 | `Runai_Spearman` | HP 130 / spd 5.6 / dmg 12 melee / def 1/0/0/0 / range 1.5 / cost 110 S + 30 I + 25 C / pop 1 |
| **L2 infantry tier** *(name TBD — "Veil Lancer"? "Tariff-bearer"?)* | mid-late game | L2 | **(new)** | TBD |
| **L3 infantry apex** *(name TBD)* | late game | L3 | **(new)** | TBD |

> **Battalion units** ([Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions)).

#### Researchable techs

Same 4-tier per-battalion weapon ladder as Alanthor's Garrison (per Q#5).

| Tech | Building lvl req. | Effect (unlock) |
|------|-------------------|------------------|
| **Conscription** equivalent | L1 | +20 % training speed at the Route Guard *(faction-wide passive — name TBD)* |
| **Stone weapons** (T1) → **Iron weapons** (T2) → **Veilstone weapons** (T3) → **Glow-infused weapons** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-battalion weapon upgrade tier |

---

### Arrowyard — cultured Archery Range

**Doc id:** Arrowyard.
**Code mapping:** the Age 0 Archery Range, renamed at age-up — same
entity, multiplier-path HP. Hosts Runai's foot-ranged roster. Cavalry
archers train at the new [Grazing Grounds](#grazing-grounds--cavalry-trainer-new).

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 600) | 660 | 690 | 720 |
| LoS | 18 | 18 | 18 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Upgrade cost | (at age-up — already standing) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

#### Trainable units

3-tier foot-ranged ladder parallel to Alanthor's Practice Range.

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats |
|----------|---------------|---------------------|-------------------|-------|
| **Runai Skirmisher** | early-mid game ranged | L1 | `Runai_Skirmisher` | HP 95 / spd 6.0 / dmg 15 ranged / def 0/1/0/0 / range 11 / min 3.5 / cost 95 S + 50 I + 25 C / pop 1 |
| **L2 ranged tier** *(name TBD)* | mid-late game | L2 | **(new)** | TBD |
| **L3 ranged apex** *(name TBD)* | late game | L3 | **(new)** | TBD |

#### Researchable techs

Same as Alanthor's Practice Range — three faction-wide / passive techs
plus the 4-tier arrow ladder.

| Tech | Building lvl req. | Effect (unlock) |
|------|-------------------|------------------|
| **Choreographed volleys** | L1 | Active skill: 2× fire rate for 5 s on Skirmisher battalions, 40 s cd *(faction-wide active)* |
| **Fletching** | L2 | +15 % attack range for all Skirmisher-class units *(faction-wide passive)* |
| **Stone-tipped arrows** (T1) → **Iron-tipped** (T2) → **Veilstone-tipped** (T3) → **Glow-tipped** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-battalion arrow upgrade tier |

---

### Grazing Grounds — cavalry trainer *(new)*

**Doc id:** Grazing Grounds.
**Code mapping:** **(new — no code entry yet)** — added per Q#1 to host
Runai's cavalry roster (light cavalry + cavalry archers). Same level
ladder as the other Age 1 military buildings.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP | TBD (suggest ≈ 900 base) | base × 1.10 | base × 1.20 |
| LoS | TBD (suggest 18) | 18 | 18 |
| Defense (M/R/S/Mg) | TBD | | |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Build cost (L1, at age-up or later) | TBD (suggest 220 S + 80 I) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

#### Trainable units

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats |
|----------|---------------|---------------------|-------------------|-------|
| **Runai Raider** | light cavalry | L1 | `Runai_Raider` | HP 150 / spd 7.2 / dmg 18 melee / def 1/0/0/0 / range 1.5 / cost 220 S + 100 I + 50 C / pop 1 |
| **Cavalry Archer** | mounted ranged | L2 | **(new — no code entry)** | TBD (suggest mid HP / high speed / moderate range) |
| **L3 cavalry apex** *(name TBD)* | late game | L3 | **(new)** | TBD |

#### Researchable techs

Same 4-tier per-battalion barding / cavalry-weapons ladder as the
Alanthor Royal Stable (names TBD).

| Tech | Building lvl req. | Effect (unlock) |
|------|-------------------|------------------|
| **Barding T1 → T2 → T3 → T4** | L1 / L2 / L3 / L3 | **Unlocks** per-battalion cavalry-armor upgrade tier *(name and effect numbers TBD)* |

---

### House — does NOT exist for Runai

Per Q#1 review and [Overview.md § Movement axis](Overview.md#north-star-the-movement-axis):
**Runai has no House at all.** Population is unlocked at age-up (full
pop available immediately, similar to Feraldis's instant-pop model).
Age 0 Houses standing on the map at age-up are removed.

> *(spec gap — exact pop ceiling at age-up. Cross-faction reference:
> Feraldis gets 200 pop instantly per [Age_0.md § Decisions](Age_0.md);
> Runai's value should be the same or similar.)*

---

### Gatherer's Hut (Age 0 carryover) — transforms into a caravan-wagon

Per [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace) and
[§ Age-up power spike](#age-up-power-spike-the-wagon-burst) above. Each
Age 0 Gatherer's Hut transforms 1:1 into a deployable caravan-wagon at
age-up. The huts the player invested in during Age 0 become the wagons of
Age 1 — placement matters in both ages.

The wagon-burst is the **only** free Trade Post deployment the player ever
gets in the match. Every Trade Post after age-up costs builders + supplies
+ iron at the normal rate.

---

## Special / choice buildings (carried from Age 0)

| Building | Runai modifier | Source |
|----------|----------------|--------|
| Vault of Almiérra | **−30 %** yield on interest | [Age_0.md § Vault](Age_0.md) |
| Shrine of Ridan | **+30 %** heal rate | [Age_0.md § Shrine](Age_0.md) |
| Fiendstone Keep | **neutral** (0 %) | [Age_0.md § Fiendstone Keep](Age_0.md) |

L1 → L3 stats, tech tables and trainables are in [Age_0.md](Age_0.md) — only
the per-culture numeric modifier changes.

---

## Runai-unique buildings (new in Age 1)

### Thessara's Bazaar — trade-lane upgrade house

**Code id:** `ThessarasBazaar` (existing code id, **repurposed** — no
longer the cultured Hall, and no longer trains units).
**Doc id:** Thessara's Bazaar.

Per Q#1 review: Thessara's Bazaar is now an Age-1-only unique building
that hosts **trade-lane upgrade research**. It is the home of the
caravan-economy tech tree (LongHaulTariffs and the other route-yield techs).
Does not train any units.

| Stat | Value |
|------|-------|
| HP | 1 400 (suggest — TBD) |
| LoS | 22 |
| Defense (M/R/S/Mg) | 1 / 1 / 0 / 0 |
| Build cost | TBD (suggest 350 S + 80 I + 40 C) |
| Role | Trade-lane upgrade hub. Does not train units. |

#### Researchable techs

| Tech | Effect | Cost | Status |
|------|--------|------|--------|
| `Runai_LongHaulTariffs` | +15 % supplies from trade routes; +25 % bonus if route length > 60 u | 220 S + 20 I | code-exists |
| `Runai_EscortedCaravans` | Trade Hubs spawn 2 uncontrollable escorts per caravan | 160 S + 40 C | code-exists |

> `Runai_PackBazaar` is **retired** per Q#8 review — PackAndMove
> mechanic is removed from the design.

### Runai Outpost — `Runai_Outpost`

| Stat | Value |
|------|-------|
| HP | 900 |
| LoS | 22 |
| Defense (M/R/S/Mg) | 1 / 1 / 0 / 0 |
| Vision aura radius | 18 |
| Build cost | 140 S + 20 I |
| Role | Trade-route anchor / vision pylon. Caravans path between Outposts and the Trader's Hall. |

### Runai Trade Hub — `Runai_TradeHub`

| Stat | Value |
|------|-------|
| HP | 1 200 |
| LoS | 24 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 240 S + 40 I |
| Caravan spawn interval | 22 s |
| Max active caravans per route | 3 |
| Yield model | Base 20 + 0.8 per tile; route ≥ 60 tiles → ×1.25 bonus |
| **TariffBoostAura** *(Q#7 resolved)* | **Per-drop-off timer** — every time a caravan deposits resources at the Trade Hub, the player gets a short bonus-yield window on the next deposit at the same building. **No stacking across multiple buildings.** *(spec gap — exact window duration + bonus %)* |
| Role | Caravan spawner. Requires a valid route to an Outpost or Trader's Hall. **Also auto-spawns trader-warriors that patrol the lane** — see [§ Trader-warriors](#trader-warriors-uncontrollable-lane-patrols). |

> **Runai Vault (`Runai_Vault`) is RETIRED per Q#6.** The Age 0 Vault of
> Almiérra (with its −30 % Runai modifier) is Runai's only bank. The
> separate Runai-specific Vault entry in
> [TechTree.json:638](../../Assets/Resources/TechTree.json#L638) and
> [BuildCosts.cs:49](../../Assets/Scripts/Data/TechTree/BuildCosts.cs#L49)
> needs to be deleted.

### Runai Veilsteel Foundry — `Runai_VeilsteelFoundry`

| Stat | Value |
|------|-------|
| HP | 1 500 |
| LoS | 20 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 450 S + 120 I + 100 C |
| Craft inputs | Iron + Crystal |
| Loss factor | 20 % |
| Role | Produce Veilsteel. |

### Runai Siege Workshop — `Runai_SiegeWorkshop`

| Stat | Value |
|------|-------|
| HP | 1 100 |
| LoS | 20 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 320 S + 140 I + 60 C |
| Trains | Runai_SandBallista |
| Role | Train siege engines. |

---

## Trader-warriors (uncontrollable lane patrols)

Every Trade Hub auto-spawns a small population of **trader-warriors** that
patrol the lanes radiating from it. They are the **defensive backbone of
the Runai map presence** — Runai cannot wall, so the trader-warrior
network covering the lane network *is* the territory claim.

Behavior rules (per user design pass):

- **Uncontrollable by default.** The player cannot order them around;
  they patrol their assigned lane autonomously.
- **Engagement zone around lanes + outposts** (Q#15 resolved): a visible
  area surrounds each trade lane and Trade Post / Outpost. When an enemy
  enters this zone, the player receives an **audible cue + minimap ping**
  and the trader-warriors patrolling that zone become **controllable**
  for the duration of the engagement. They auto-revert to autonomous
  patrolling **5 seconds after the zone is clear of enemies**.
- **Generate Crystal + Supplies passively while patrolling.** This is
  additional income *on top of* caravan trade revenue, so a Runai player
  is rewarded for keeping lanes long and active.
- **Do not consume population.** Outside the standard pop cap entirely.
- **Globally capped, scales with player population** (Q#13 resolved):
  the trader-warrior cap is a **single global number** that grows by **+1
  per soldier trained**. Every Spearman / Skirmisher / Raider / cavalry
  archer the player trains raises the trader-warrior ceiling by 1. This
  prevents Runai from snowballing into a free-army state — a Runai with
  zero army has zero patrols.
- **Network-pooled (not post-pinned)** (Q#14 resolved): trader-warriors
  are **not assigned to specific Trade Posts**. They belong to the lane
  network as a whole. If a Trade Post / Trade Hub dies, the warriors
  redistribute across the remaining nodes.
- **Decent in a fight, not elite.** Comparable to Runai Spearman / light
  infantry. Not designed to win pitched battles — they're the equivalent
  of Alanthor's walls: passive deterrents that buy time and force the
  opponent to commit force.

### How the player keeps agency despite "uncontrollable"

Most RTS players expect total unit control, so this will feel alien at
first. To prevent "watching helplessly" frustration, Runai retains
**agency-adjacent levers**:

- **Lane placement is the strategic layer.** The player designs the
  defense network when they plant Trade Posts and route caravans, not
  when they order units.
- **Reinforce a threatened lane** by dropping a controllable unit (e.g.
  a Spearman or Raider) into the area; the trader-warrior pack will
  fight alongside it.
- **Plant more Trade Posts near a contested zone** to boost the local
  trader-warrior count.

> **(spec gap)** When a Trade Post / Trade Hub dies, what happens to its
> trader-warriors? Options on the table: (a) wander to the nearest
> friendly post, (b) die with the post, (c) become temporarily
> controllable as a "return home" event. Each choice changes the
> punishment severity of losing a node — needs prototype testing.

### Wagon escorts vs trader-warriors

Trader-warriors **patrol existing lanes** — they don't protect wagons in
transit. Wagons need separate **escort** units the player attaches
manually (Spearman, Raider, or whatever controllable infantry the player
has). This split is intentional:

- Trader-warriors hold *steady-state* network territory.
- The player has to *actively engage* in escorting wagons during the
  high-stakes age-up burst (and every subsequent wagon move) — Feraldis
  will absolutely target wagons once they learn the mechanic.

---

## Runai units (full stat blocks from code)

### Runai Spearman — melee anchor

Trains at Trader's Hall (today), possibly Route Guard (per design).

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 130 |
| Speed | 5.6 |
| Training time | *(spec gap — no value in TechTree.json)* |
| Armor type | infantry_heavy |
| Damage | 12 (melee) |
| Defense (M/R/S/Mg) | 1 / 0 / 0 / 0 |
| Attack range | 1.5 |
| LoS | 18 |
| Cost | 110 Supplies + 30 Iron + 25 Crystal |
| Pop | 1 |

### Runai Skirmisher — light ranged

Trains at Trader's Hall (today), possibly Arrowyard (per design).

| Field | Value |
|------|-------|
| Class | `human_ranged` |
| HP | 95 |
| Speed | 6.0 |
| Training time | *(spec gap)* |
| Armor type | ranged |
| Damage | 15 (ranged) |
| Defense | 0 / 1 / 0 / 0 |
| Attack range | 11 |
| Min attack range | 3.5 |
| LoS | 22 |
| Cost | 95 Supplies + 50 Iron + 25 Crystal |
| Pop | 1 |

### Runai Raider — fast cavalry

Trains at Trader's Hall.

| Field | Value |
|------|-------|
| Class | `human_cavalry` |
| HP | 150 |
| Speed | 7.2 (fastest cav in the game) |
| Training time | *(spec gap)* |
| Armor type | cavalry |
| Damage | 18 (melee) |
| Defense | 1 / 0 / 0 / 0 |
| Attack range | 1.5 |
| LoS | 20 |
| Cost | 220 Supplies + 100 Iron + 50 Crystal |
| Pop | 1 |

### Runai Acolyte — religious / magic

Trains at **Temple of Ridan, L3** (per Q#17 — Temple has 3 levels, not 4;
old "L4" referenced a spec-refinement stage that has been retired).

| Field | Value |
|------|-------|
| Class | `human_magic` |
| HP | 90 |
| Speed | 3.0 |
| Training time | 35 s |
| Min building lvl | **3** *(Temple of Ridan L3)* |
| Armor type | ranged |
| Damage | 0 |
| Damage type | magic |
| Defense | 0 / 0 / 0 / 1 |
| LoS | 14 |
| Cost | **~300 Supplies + 150 Iron + 100 Crystal + 30 Veilsteel** *(rebalanced to the cross-faction game-ender religious tier — see [Overview.md § Religious units](Overview.md#religious-units--cross-faction-game-ender-tier))* |
| Pop | 1 |
| Single unit / battalion | **Single** ([Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions)) |
| Role | Channels **Conversion** rituals on Active crystal nodes — Runai's Glow-generator. "The node fights enslavement harder than destruction, so escort is doubly required" ([TechTree.json:812](../../Assets/Resources/TechTree.json#L812)). |

### Runai SandBallista — siege

Trains at Runai Siege Workshop.

| Field | Value |
|------|-------|
| Class | `machinery_siege` |
| HP | 200 |
| Speed | 3.4 |
| Armor type | ranged |
| Damage | 36 (siege) |
| Defense | 0 / 1 / 2 / 0 |
| Attack range | 20 |
| Min attack range | 5.5 |
| LoS | 26 |
| Cost | 260 Supplies + 120 Iron + 80 Crystal |
| Pop | 1 |

### Runai Caravan — civilian trade *(uncontrollable)*

Auto-spawned by Trade Hubs; cannot be issued orders by the player.

| Field | Value |
|------|-------|
| Class | `civilian_trade` |
| HP | 120 |
| Speed | 5.6 |
| Armor type | ranged |
| Damage | 0 |
| Damage type | true *(takes / does flat damage)* |
| Defense | 0 / 1 / 0 / 0 |
| LoS | 18 |
| Cargo capacity | 120 |
| Cargo on death | **Reverts to the killer** *(Q#18 resolved)* — if the caravan dies to a Feraldis attacker, Feraldis gains 50 % of the cargo as supplies. Caravans killed by Alanthor or Runai units (e.g. in skirmishes, friendly-fire, or PvE) yield **nothing** — the cargo is destroyed. This synergizes intentionally with Feraldis's damage-as-income identity. |
| AI | `FollowTradeRoute`, leash 20, avoid enemies in radius 8.0 |
| Flags | civilian, uncontrollable, flees on damage |
| Pop | 0 (housing-provider, not consumer) |

### Runai Escort — caravan guard *(uncontrollable)*

Spawned alongside Caravans when `Runai_EscortedCaravans` is researched.
Despawns when its caravan dies.

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 110 |
| Speed | 6.2 |
| Armor type | infantry_light |
| Damage | 10 (melee) |
| Defense | 0 / 0 / 0 / 0 |
| Attack range | 1.3 |
| LoS | 18 |
| AI | Guard the assigned caravan; leash 12 |
| Flags | uncontrollable, despawnOnCaravanDeath |

---

## Decisions (resolved 2026-05-19)

All 19 open questions answered in this review pass. Each decision is
folded into the doc body above; this block is the **decision record**.
Cross-faction items are flagged.

1. **Building list** — **resolved.** The Runai Age 1 roster is:
   - **Trader's Hall** (cultured Hall) — Worker + Scout only, plus
     Tools + curse-neutrality techs.
   - **Route Guard** (cultured Barracks) — 3-tier infantry ladder.
   - **Arrowyard** (cultured Archery Range) — 3-tier foot-ranged ladder.
   - **Grazing Grounds** *(new)* — light cavalry + cavalry archers.
   - **Thessara's Bazaar** *(repurposed)* — trade-lane upgrade research
     only, **does not train units**.
   - **Runai Outpost** — trade-route anchor / vision pylon.
   - **Runai Trade Hub** — caravan spawner + trader-warrior spawner.
   - **Runai Veilsteel Foundry** — produces Veilsteel (Iron + Crystal,
     same rate as Alanthor).
   - **Runai Siege Workshop** — trains SandBallista.
   - **Temple of Ridan** (Age 0 choice carryover) — Litharch + Acolyte.
   - **No House.** **No Walls.** Identity-defining absences.
2. **`ThessarasBazaar` → `TradersHall` rename** — covered by Q#1. The
   `ThessarasBazaar` code id is **kept** but rebound to the new
   trade-lane-upgrade-house role; the cultured Hall is the Age 0 Hall
   reskinned (id remains `Hall`).
3. **Runai economy** — **resolved.** Runai workers **mine iron normally**.
   Supplies + Crystal come from trade routes only. Veilsteel produced at
   the Veilsteel Foundry from Iron + Crystal (same rate as Alanthor).
4. **Worker tech ladder** — **resolved directionally.** Runai has a Tools
   ladder analogous to Alanthor's (Stone → Iron → Veilstone → Veilsteel
   tools). Some techs may be **universal at Age 0** (TBD which).
5. **Military tech ladder** — **resolved directionally.** Runai inherits
   the same 4-tier per-battalion weapon ladder pattern as Alanthor for
   Route Guard, Arrowyard, and Grazing Grounds. Specific tier names TBD.
6. **Two Vaults** — **resolved: cut `Runai_Vault`.** Vault of Almiérra
   (Age 0 choice building, −30 % Runai modifier) is Runai's only bank.
7. **TariffBoostAura** — **resolved.** Per-drop-off timer (not radius
   aura): when a caravan deposits at a Trade Hub, the next deposit at
   that same building gets a short bonus-yield window. **No stacking.**
8. **PackAndMove** — **resolved: REMOVED.** The mechanic is retired.
   The `Runai_PackBazaar` tech is also retired.
9. **Wagon transit-spike** — **resolved.** Linear decay over **4 minutes**
   from full to zero. Well-placed Age 0 huts make the spike invisible.
10. **Wagon count cap** — **resolved.** No hard cap — count = number of
    Age 0 Gatherer's Huts. Self-balancing via worker count and Age 0
    opportunity cost.
11. **Faction choice timing** — **resolved.** Age 0 is faction-agnostic.
    Players may know their pick (and place huts to synergize), or change
    their mind, or choose at random. Synergy is the player's
    responsibility.
12. **Recoverability floor** — **resolved directionally.** Runai can
    recover from wagon-loss; not a softlock condition.
13. **Trader-warrior cap scope** — **resolved.** **Global cap that scales
    with player population** (+1 trader-warrior slot per soldier trained).
    Prevents snowballing.
14. **Trader-warrior post-death fate** — **resolved.** Warriors belong
    to the lane network, not specific posts. On node death, they
    redistribute across remaining nodes.
15. **"Watching helplessly" UX** — **resolved.** Lane / outpost zones
    trigger audible cue + minimap ping when an enemy enters; warriors
    become controllable while the zone is hot; auto-revert 5 s after
    clear.
16. **Crystal-Curse neutrality** — **resolved.** Tech-based, researched
    at the Trader's Hall. Effect: −20 % chance of aggroing curse waves
    when Runai units traverse cursed tiles. Curse defences themselves
    remain in place (the curse resists *conversion* — Runai's interaction
    type — harder than the other two).
17. **Acolyte training level** — **resolved.** Trains at **Temple of
    Ridan L3** (Temple caps at 3 levels — the old "L4" reference is a
    retired spec stage). **Cross-faction impact:** Alanthor Scholar and
    Feraldis Iconoclast also drop from L4 → L3.
18. **Caravan cargo on death** — **resolved.** Cargo **reverts to the
    killer** if the killer is Feraldis (gives Feraldis 50 % of cargo as
    supplies). For Alanthor / Runai killers, cargo is destroyed.
19. **MP determinism for trader-warrior AI** — **deferred.** Not in
    scope for now.

## Remaining open questions

- **L2 / L3 infantry tier names** at Route Guard.
- **L2 / L3 ranged tier names** at Arrowyard.
- **L2 / L3 cavalry / cavalry-archer tier names** at Grazing Grounds.
- **Grazing Grounds numeric stats** (HP, LoS, defense, build cost).
- **Cavalry Archer stat block** (HP, range, damage, etc.).
- **Thessara's Bazaar numeric stats** (HP, exact build cost).
- **Specific tier names** for the Runai Tools / weapons / arrows /
  barding ladders (Q#4, Q#5 — design directionally resolved, names TBD).
- **Tools that may be Age-0 universal** vs Age-1 culture-specific
  (Q#4 implementation detail).
- **TariffBoostAura window duration + bonus %** (Q#7 mechanic resolved,
  numbers TBD).
- **Runai-specific pop ceiling at age-up** — probably mirrors
  Feraldis's instant-200; confirm.
- **Curse-neutrality tech naming and per-tier numbers** (Q#16 — only
  one example tech shape given).

## Cross-faction follow-ups triggered by this review

- **Religious unit training level: L4 → L3** across all three culture
  docs (Q#17). Affects Scholar in [Age_1_Alanthor.md](Age_1_Alanthor.md)
  and Iconoclast in [Age_1_Feraldis.md](Age_1_Feraldis.md).
- **Caravan death cargo → Feraldis-only benefit** is a new Feraldis
  income stream; flag in [Age_1_Feraldis.md § Pillage / damage-income](Age_1_Feraldis.md#culture-identity) as a passive synergy with the
  damage-as-income mechanic.
- **`Runai_Vault` removed** — cleanup needed in [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L49) and
  [TechTree.json:638](../../Assets/Resources/TechTree.json#L638).
- **`Runai_PackBazaar` retired** — cleanup needed in the same files.
- **`ThessarasBazaar` repurposed** — code rebind from "main hall" to
  "trade-lane upgrade house"; drop the `PackAndMove` and `TariffBoostAura`
  per-building abilities since they're either retired or rebound.
