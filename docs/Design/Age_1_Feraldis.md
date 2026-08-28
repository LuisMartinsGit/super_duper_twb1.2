# Age 1 — Feraldis

> Military culture. **Fire and blood.** Where Alanthor means to purify and
> guard against the curse, Feraldis wants to **taint it further**. Strength
> comes from **spilling blood and fighting on top of it**: Feraldis units
> frenzy on bloodsoaked ground, Feraldis territory is claimed by **War
> Totems planted on blood pools**, and the roster is built to create blood
> (the Suicidal) and to exploit it (everyone else). Raiding pressure,
> persistent gather buildings, and the **pillage** damage-as-income drip
> round out the identity.
>
> **See also:** [Overview.md](Overview.md) (two-age framing), [Age_0.md](Age_0.md)
> (pre-culture starting buildings), [Age_1_Alanthor.md](Age_1_Alanthor.md) for
> the doc template, [Curse_And_Shardroot.md](Curse_And_Shardroot.md) §2.6
> (per-culture influence engagement — blood totems are canon there), and the
> cross-age [Petriarchy doc TBD] for sects.
>
> Doc version: 2026-08-05 — **second design pass (user direction): blood /
> fire theme, frenzy-on-blood, War Totem influence, War Hall +
> the four-unit combat roster.** Supersedes the 2026-05-19 first-pass
> extract where the two conflict; first-pass content that is not
> contradicted (huts, Houses, choice buildings, Iconoclast, siege) still
> stands. Items marked **(new — not yet in code)** come from the user's
> design notes; items marked **(spec gap)** are open questions.

---

## Culture identity

| Aspect | Feraldis |
|--------|----------|
| Focus | **Military** (blood-fueled combat, raiding pressure, fast training) |
| **Economy signature** | **Raider Camps.** Feraldis does not gather — it *takes*. Its Gatherer's Huts become Raider Camps that spit out fragile, uncontrollable Plunderers which run at the enemy and **steal resources straight out of their bank**. See [§ Raider Camp](#raider-camp--the-feraldis-gatherers-hut). |
| Theme | **Fire and blood** — Feraldis does not fear the curse; it wants to taint it further |
| Style | Wood / Norse |
| **Combat signature** | **Frenzy on blood** — every Feraldis unit fighting on top of blood (the persistent [BloodMap](../../Assets/Scripts/Influence/BloodMap.cs) ground layer) gains the Frenzy buff: **+25 % attack damage, +20 % attack speed** (numbers are playtest knobs). See [§ Blood, Frenzy & War Totems](#blood-frenzy--war-totems). |
| **Influence signature** | **War Totems planted on blood pools** are Feraldis's territory engine — generic Feraldis buildings project **no** civic influence (the Hall keeps its universal anchor ring). See [§ War Totem](#war-totem--feraldis_wartotem). |
| Economy | **Damage-as-income** is the core mechanic — inflicting damage on other players' units / buildings generates supplies. Floor mechanic (so an isolated Feraldis player isn't softlocked): **The Border creatures and nodes count as damage targets**, so Feraldis can always farm income off the border layer. Gatherer's Huts also **persist across age-up** as a secondary, settled supply stream — they can be upgraded to **Hunting Lodge** (wildlife synergy) or **Logging Station** (forest synergy). On top of that, the `Feraldis_Pillage` tech gives **+15 Supplies and +1 Iron per non-military kill** to the attacker's owner. |
| Vault yield modifier | **neutral** (0 %) |
| Shrine heal modifier | **−30 %** (worst of the three) |
| Fiendstone Keep HP/arrows | **+50 %** (best of the three) |
| Population model | **Feraldis pop is set to the game-cap (200) instantly at age-up** — no building is required to scale pop. Houses still exist, but **they are not the pop-cap source for Feraldis** (per Age_0 Q#6); they are now a pure **aggression tool**: every House build / upgrade spawns autonomous Raider units that immediately attack the closest enemy target. Building Houses becomes a strategic offensive decision rather than a housing necessity. Longhouse's +10 pop is irrelevant to Feraldis (already at cap). |
| Main upgrade hooks | `FiendstoneKeep` train-speed aura (+25 % at all friendly trainers); pillage drip; bloody-ground tower buff |

---

## Blood, Frenzy & War Totems

The Feraldis loop (canonized 2026-08-05; aligns with
[Curse_And_Shardroot.md](Curse_And_Shardroot.md) §2.6 "blood totems"):

1. **Blood is real ground state.** Every unit death splats the persistent
   `BloodMap` grid (already shipped — X7: blood inside any player influence
   fades slowly; outside influence it is eternal). Feraldis reads this map
   as a resource.
2. **Frenzy on blood.** Any Feraldis unit standing on blood
   (`BloodMap.SampleWorld ≥ 0.15`) gains **Frenzy**: +25 % attack damage,
   +20 % attack speed. A ~1 s linger prevents flicker at pool edges. No
   stacking; applies to every Feraldis unit class. *(All numbers are
   playtest knobs.)*
3. **War Totems claim blood.** Feraldis influence is **not** produced by
   ordinary buildings. It is produced by **War Totems**, which can only be
   **placed on a blood pool** (placement is rejected off-blood). A standing
   totem **drinks** nearby blood, converting it into permanent stored
   **Fervor**; its influence rate and radius scale with Fervor. Drinking
   resolves the X7 tension (blood decays inside influence — a totem that
   merely projected influence over its pool would starve the pool): the
   totem banks the pool's value into Fervor before decay eats it. Fervor
   never decays; the totem is a normal killable building — when it dies its
   influence decays off the map like any lost source.
4. **Bleeding IS blood.** *(rule, 2026-08-05 rev.2)* Every Bleeding effect in
   the game does two things: damage over time **and** a steady drip of blood
   onto the ground under the victim. A bleeding unit is a walking blood
   brush — it paints the ground its own army frenzies on and its totems
   drink, whether it dies or not. This is why so much of the Feraldis
   roster inflicts bleed.
5. **Fire eats blood.** *(rule, 2026-08-05 rev.2)* Feraldis fire ignites
   bloodsoaked ground: the blood is **consumed** and the patch burns,
   damaging everything standing in it. The two halves of "fire and blood"
   are a real resource conversion — burning a patch spends the frenzy /
   totem fuel to deal immediate area damage instead.
6. **The roster feeds the loop.** The Suicidal explodes into a large blood
   pool; the Bloodletter's area bleed drops many enemies on the same spot;
   the War Chariot draws blood lines across the field; the Berserker's death
   frenzy keeps it killing on top of the blood it spills. Fight where the
   blood is, or make new blood.

> **LOCKED (2026-08-05 rev.2, user):** the blood mechanics above and the
> War Hall unit roster are settled. Later passes extend Feraldis
> outward from them — they are not to be re-opened.

### The Warpath — burning a lane through the curse *(2026-08-06)*

The two cultures relate to the crust in **opposite** ways, and that
asymmetry is the design:

| | Relationship to the crust |
|---|---|
| **Alanthor** | Turtles. The crust is a **free outer wall** — it guards their flanks and funnels attackers into their towers. Curse growth partly *helps* them. |
| **Feraldis** | Attacks. That same crust is a **moat around every target** they want to reach, and their whole kit — raiders, chariots, a 3.2-speed Corruptor that must physically walk to a well — dies crossing it. |

Feraldis cannot out-turtle the curse and should not try. Instead **its army
burns through it**: crust dies under a Feraldis advance
(`WarpathBurnPerSecond`, r6), so an attack **carves its own corridor**.

Three properties keep this a decision rather than a free pass:

- The lane exists **only where the army is**.
- It **closes behind them** — the veil regrows once they move on.
- It clears ground; it does not **hold** ground. Feraldis still cannot
  garrison territory against the curse the way Alanthor can.

**War Totems burn far harder** (`TotemBurnPerSecond`, r16) — that is what
lets a planted totem hold its patch instead of being swallowed.

**The burning PAYS.** Every cell of crust the warpath actually destroys
yields **veilstone** (`VeilstonePerCellCleared`). This finally delivers
[Curse_And_Shardroot.md](Curse_And_Shardroot.md) §2.6's standing promise —
*"Feraldis is the ONLY culture that earns veilstone FROM the curse"* — paid
on **destruction** rather than on mining, which suits a culture that
attacks rather than digs.

It is self-limiting by construction: a cell can only be cleared once until
the veil regrows over it. An army parked on clean ground earns nothing, so
the income tracks how much curse the faction is genuinely deleting. **The
more the curse spreads, the more there is for Feraldis to burn** — the one
culture that gets *richer* as the map gets worse.

> **Who counts as a soldier.** Both the warpath and marching influence apply
> to **every military unit of a Feraldis faction**, not only units built by a
> Feraldis factory — a Feraldis player's Spearmen and Archers come from the
> shared Age 0 roster and carry no Feraldis tag. Raiders count; a worker
> still on build duty does not (a conscripted one does).

> **Why it exists.** Across six playtests Feraldis finished at 0–3.5 %
> influence while the curse took 60–82 % of the map. Its territory
> mechanics all assumed ground it had no way to reach, and the
> 2026-08-06 match planted **34 War Totems** that were all swallowed —
> 26 of them on the same cell.

---

### Marching influence — the army IS the border *(2026-08-06)*

War Totems are Feraldis's **anchors**; this is the **connective tissue**.

Every Feraldis military unit, every Plunderer, and every conscripted worker
leaks a small amount of influence into the ground it stands on
(`MarchInfluencePerSecond`, r7). Alanthor claims by building outward from
home and Runai by trade lanes — **Feraldis claims by walking on you.** Its
border grows toward wherever its army is, which is by definition toward the
enemy.

Per-source it is far weaker than a totem: an army passing through smudges a
corridor, it does not plant a border. **Holding ground is what makes it
stick.** A worker still on build duty leaks nothing — free home ground is
Alanthor's identity, not Feraldis's.

> **Why it exists.** Across five playtests Feraldis sat at 0.0-2.8 %
> influence for entire matches with the curse at 60 %, because its ordinary
> buildings project nothing and totems need blood that only exists at the
> front. It had no way to *reach* the ground it was supposed to claim.

**Interactions already in the engine:**

- **Blood + curse = spawner** (X7, `BloodCurseSpawnSystem`): blood on
  crusted ground births curse creatures and drains the pool. Feraldis
  totems now *compete with the curse* for blood — thematic (Feraldis
  taints; the curse harvests) and self-balancing.
- **Sect blood pools** (`SectBloodPoolSystem`, entity-based, sect-gated)
  remain a separate short-lived layer; their `InBloodPool` combat bonus
  can stack with Frenzy. Flagged as a playtest watch item.
- **Curse-kill exception** (X8): exposure kills leave no blood — the curse
  cannot feed Feraldis, and Feraldis cannot farm the curse's own kills.
- Feraldis influence still suppresses/decays crust like every culture; the
  §2.6 *corrupt-and-mine* variant stays **deferred**.

---

### Feraldis influence never decays *(2026-08-18)*

Every other channel bleeds back to neutral when its source dies. **Feraldis
influence does not decay at all.** Once a cell is Feraldis ground it stays
Feraldis ground for the rest of the match — army wiped, totems burned,
buildings razed, it does not matter.

It has exactly one way out: **being replaced.**

- A Feraldis cell erodes **only while another channel — any other player,
  or the curse — sits at or above its own strength on that cell.** While
  that is true it decays at the normal rate (5 %/s proportional plus the
  linear term), so contested ground flips on the same ~15 s / ~45 s clock
  everyone else lives on.
- The moment the challenger's own influence falls back below it (its source
  died, the curse got cleansed), the Feraldis value **freezes again**
  wherever it happens to be. There is no slow bleed in between.
- Channel values saturate at a shared ceiling, so an enemy who merely
  **matches** a saturated Feraldis cell already begins taking it — a
  fully-claimed Feraldis map is never permanently locked.

**Why.** The whole culture is "claim it by walking on it and bleeding on
it". A decaying claim meant a Feraldis player's territory evaporated behind
the army the moment it moved on, which is precisely the failure the
marching-influence pass above was added to fix — Feraldis kept re-buying the
same ground. Permanent-until-contested makes the Feraldis map a **ratchet**:
slow to gain, never given back for free, and taken off them only by an enemy
who out-pushes them there or by the curse rolling over it.

**Consequences to watch:** Feraldis's economy doubling and the Plunderer's
"outside its own influence" gate both read this map, so a long match leaves
Feraldis with permanently doubled ground it no longer garrisons. Blood
inside influence also self-cleans, so old Feraldis ground stops feeding
blood-curse spawns.

---

## Conventions

Same as [Age_1_Alanthor § Conventions](Age_1_Alanthor.md#conventions) — L1
through L3 building ladder, multipliers from [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs),
upgrade durations 30 s / 45 s for L2 / L3.

---

## Cultured carryover buildings

### Warrior's Hall — cultured Hall

**Doc id:** Warrior's Hall *(settled 2026-08-27. The first pass called this
the "War Hall", but 2026-08-05 rev.4 gave that name to the cultured BARRACKS,
leaving the cultured Hall needing its own. "Warrior's Hall" is that name —
note it is a DIFFERENT building from the "War Hall" (cultured Barracks) named
below in this same doc; the two are easy to confuse in a search.)*
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
| ~~**Cranes**~~ | — | *(removed 2026-07-20 — carry capacity no longer exists; mined resources credit the stockpile directly)* | — |
| **`Feraldis_Pillage`** | L1 (suggested) | Killing **non-military units** grants the attacker's owner +15 Supplies and +1 Iron per kill. "Non-military" = **Workers, Scouts, Traders (incl. Runai caravans), Raiders (auto-spawned of any kind), Litharchs** (per Q#6). Numbers TBD per playtest. | Code-existing tech, **host = War Hall** (per Q#9). |
| **Veilsteel Frenzy** *(renamed from `Feraldis_IronFury` per Q#7)* | L2 (suggested) | Feraldis units gain the ability to **carry up to 5 Veilsteel shavings**; each shaving grants +2 % attack (stacks to +10 %). **Veilsteel-only**, **Feraldis-only.** Replaces the cross-faction Iron-carry mechanic for Feraldis. *Lore: Feraldis warriors consume Veilsteel shavings as a psychoactive battle stimulant, like Norse berserkers and mushrooms.* | Code-existing tech renamed; effect rewired. |

---

### War Hall — cultured Barracks

**Doc id:** War Hall *(replaces "Longhouse" as the cultured
Barracks, 2026-08-05 pass — the `Feraldis_Longhouse` standalone building
id is retired from this role; its batch-training flavor survives via the
existing code rule that Feraldis training takes 1.75× time but spawns
2 units per completion)*.
**Code mapping:** the Age 0 Barracks entity, renamed at age-up for
Feraldis factions (same runtime entity + level ladder, parallel to
Alanthor's Garrison). The Feraldis roster below lives in the Barracks
def's trains list, culture-gated by the `Feraldis_` id prefix.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 800) | 880 | 920 | 960 |
| LoS | 18 | 18 | 18 |
| Train-time multiplier | ×0.870 *(stacks with Fiendstone Keep +25 % train aura; Feraldis pays 1.75× time for 2 units per batch)* | ×0.800 | ×0.714 |
| Upgrade cost | 80 S + 20 I | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |
| Upgrade duration | (at age-up L0 → L1 automatic) | 30 s | 45 s |

> **Berserker removed from the War Hall, 2026-08-27.** The War Hall does not
> train Berserkers. The unit is **conversion-only** — the existing miner→Berserker
> path at the Fiendstone Keep (`BerserkerConversionSystem`) — plus the Longhouse,
> which is now its only training host. The War Hall roster is therefore
> **Spearman / Bloodletter / Suicidal**.

#### Trainable units — the four-unit combat roster (2026-08-05 canon)

| Tier | Doc name | Role | Lvl unlock | Code id | Stats |
|------|----------|------|-----------|---------|-------|
| Basic | **Spearman (Feraldis)** | line infantry — Alanthor's Spearman chassis with **less HP, more attack** | L1 | `Feraldis_Spearman` | HP **100** / dmg **13** melee / spd 5.5 / cd 1.5 s / rng 1.5 / def 1/0/0/0 / LoS 16 / 7 s train / 80 S + 30 I / pop 1 |
| Mid | **Bloodletter** | low HP, low attack, **high mobility**; **attacks everything in an area around it** and **inflicts Bleeding on all it hits** | L2 | `Feraldis_Bloodletter` | HP **90** / dmg **6** melee AoE (r 2.5 whirl) / spd **6.8** / cd 1.2 s / def 0/1/0/0 / LoS 16 / 9 s train / 90 S + 20 I / pop 1. **Bleeding:** 2 HP/s for 5 s, refreshed per hit, no stacking |
| Elite | **Berserker** | high-attack melee; **Death Frenzy** — when HP would drop below 1 it locks at 1 HP, gains **+50 % attack and +50 % speed for 5 s, cannot die during it, then dies** | L3 | `Feraldis_Berserker` *(existing unit, re-specced)* | HP 150 / dmg **16** melee / spd 5.8 / rng 1.6 / def 2/0/0/0 / 110 S + 20 I + 20 Vs / pop 1 |
| Special | **Suicidal** *(working name — flavor name TBD)* | **runs at the enemy, soaks ranged fire, then explodes leaving a large blood pool.** No normal attack; explodes on death from ANY cause (arrival detonation or being shot down) — enemy fire converts into Feraldis blood | L2 | `Feraldis_Suicidal` | HP **220** / dmg 0 / spd 6.5 / def **0/4/0/0** (arrow-soak) / LoS 14 / 12 s train / 90 S + 30 I / pop 1. **Detonation:** 45 melee dmg in r 6 (enemies only), blood pool ≈ r 6 heavy saturation |

> All four frenzy on blood like every Feraldis unit. The Suicidal is the
> loop-starter: it manufactures the blood the rest of the roster fights on
> and the War Totems claim.
>
> **Parked from the first pass (not deleted — awaiting user decision):**
> Swordsman / Royal Guard line-infantry ladder, **Warboar Rider** (was the
> Longhouse-trained cavalry), and the batch-training [5,10] discount UI.
> The existing miner→Berserker conversion at the Fiendstone Keep
> (`BerserkerConversionSystem`) is unchanged and now feeds the same
> re-specced Berserker.

#### Researchable techs

Same 4-tier weapon ladder as Alanthor's Garrison, plus the Feraldis-flavour
twist on Conscription. Veilsteel Frenzy lives at the **War Hall**, not here
(per Q#9).

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Conscription** | L1 | +20 % training speed at the War Hall *(faction-wide passive)* — stacks multiplicatively with the Fiendstone Keep aura | *(new — same as Alanthor)* |
| **Stone weapons** (T1) → **Iron weapons** (T2) → **Veilstone weapons** (T3) → **Glow-infused weapons** (T4) | L1 / L2 / L3 / L3 | **Unlocks** per-battalion weapon upgrade tier ([Overview.md § Per-battalion](Overview.md#per-battalion-military-upgrades-cross-faction-rule)) | *(new — same pattern as Alanthor)* |

---

### Hall of Axes — Feraldis's ranged building

**Doc id:** Hall of Axes *(named 2026-08-27; replaces "Thrower Camp").* Its
throwers hurl axes and fire rather than loosing arrows, which is why it is a
hall and not a range. Code id `Feraldis_HallOfAxes`, presentation 369,
8x8 footprint, 180 S + 50 I, HP 600 / LoS 18. Trains `Feraldis_Archer`,
`Feraldis_Hunter`, `Feraldis_Firethrower`.

> **Not a cultured Archery Range (2026-08-27).** The Archery Range is
> **Alanthor-only, era 2**, so there was never a shared entity for Feraldis to
> rename — the old "Age 0 Archery Range renamed at age-up" premise was broken
> regardless, since the Archery Range is `minEra: 2` and never stands in Age 0.
> The Hall of Axes is a building in its own right and is **authored and
> registered**: SO, factory, tag, footprint and cost all exist.

**Code mapping:** The Age 0 Archery Range, renamed at age-up — same entity,
multiplier-path HP, same trainer role (per Q#3). Trains the Feraldis ranged
roster below.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 600) | 660 | 690 | 720 |
| LoS | 18 | 18 | 18 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Upgrade cost | (at age-up — already standing) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

#### Trainable units — the ranged roster (2026-08-05 rev.2 canon)

A **range-for-violence ladder**: each tier throws less far and hurts more.
Feraldis ranged units are not skirmishers who kite — they are close-range
brawlers who make the ground bleed and burn.

| Tier | Doc name | Role | Lvl unlock | Code id | Stats |
|------|----------|------|-----------|---------|-------|
| L1 | **Archer** | Alanthor's Archer with **less range and less defense** — same bow, worse discipline | L1 | `Feraldis_Archer` | HP 90 / spd 5.2 / dmg 17 ranged / **range 13** (vs 16) / min 1 / **def 0/1/0/0** (vs 0/2/0/0) / LoS 20 / 20 s train / 50 S + 25 I / pop 1 |
| L2 | **Axe Thrower** | **Shorter range still, lots of damage, inflicts Bleeding.** Never retreats (min range 0) — fights at point blank | L2 | `Feraldis_Hunter` *(existing entity, re-badged — the code already called it an axe thrower)* | HP 90 / spd 5.7 / **dmg 20** / **range 8** / min 0 / def 0/0/0/0 / LoS 12 / 14 s train / 90 S + 20 I / pop 1. **Bleed:** 2 HP/s for 5 s |
| L3 | **Firethrower** | Hurls burning balls of fire. **On bloodsoaked ground the blood CATCHES FIRE** — the pool is consumed and the whole patch burns for 5 s, damaging everything in it | L3 | `Feraldis_Firethrower` | HP 100 / spd 5.0 / dmg 14 ranged / **range 10** / min 2 / def 0/1/0/0 / LoS 14 / 18 s train / 120 S + 30 I + 20 Vs / pop 1. **Ignition:** r 6 patch, 12 HP/s for 5 s |

> **The Firethrower is the culture's release valve.** Everything else in the
> Feraldis kit *accumulates* blood; the Firethrower spends it. Burning a
> patch destroys frenzy ground and totem fuel to deal immediate area
> damage — a real decision, not a free bonus. Fire on clean ground is just
> a mediocre ranged attack.
>
> *(spec gap — whether ignition should also deny the ground to enemy
> Feraldis in a mirror match, and whether burning ground should block totem
> placement while it burns.)*
>
> **VFX:** the Firethrower's shot reuses the Synty catapult fire effect
> (`Prefabs/Effects/FX_CatapultShot`) **scaled way down** — a hurled
> fireball, not a boulder.

#### Researchable techs

Same 4-tier per-battalion arrow ladder as Alanthor's Practice Range
(Q#3 confirmed parity), plus the two faction-wide passives.

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Choreographed volleys** | L1 | Active skill: 2× fire rate for 5 s, 40 s cd *(faction-wide active)* | *(new — same as Alanthor)* |
| **Fletching** | L2 | +15 % attack range for all Hall of Axes units *(faction-wide passive)* — worth more to Feraldis than anyone, since their whole ranged tree is range-starved | *(new)* |
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

### Raider Camp — the Feraldis Gatherer's Hut

**Code id:** `Feraldis_RaiderCamp` (the Age 0 `GatherersHut` entity, tagged
at age-up — same building, new behaviour).
**Doc id:** Raider Camp.

> **This is the Feraldis economy.** Feraldis does not gather; it takes.
> Every Gatherer's Hut a Feraldis player owns becomes a Raider Camp at
> age-up, and camps continuously vomit **Plunderers** at the enemy. What
> those Plunderers steal IS the faction's income — there is no passive
> harvest to fall back on.
>
> **Supersedes** the first-pass Hunting Lodge / Logging Station upgrade
> pair (2026-08-05 rev.3). Those two buildings are **retired** — their ids
> and factories remain in the codebase but nothing routes to them.

| Stat | Value |
|------|-------|
| HP / LoS / footprint | unchanged from the Gatherer's Hut it was |
| Spawn interval | **1 Plunderer every 22 s** *(raised from 5 s — rev.4 playtest: waves arrived far too fast once several camps stood)* |
| Live cap | **5 Plunderers per camp** — a camp stops producing at cap and resumes as they die |
| Passive supply income | **none.** A Raider Camp does not gather at all, and it does **not** contest gather area either — it neither harvests nor denies a neighbouring hut's yield |

#### Plunderer — the camp's raider *(uncontrollable)*

**Code id:** `Feraldis_Plunderer`. Free, no population cost, cannot be
selected or ordered. It runs at the nearest enemy unit or building and
attacks it.

| Field | Value |
|------|-------|
| HP | **1** (2026-08-05 PM) — anything at all kills it: one arrow, one swing, one tick of curse exposure. In exchange it gets a **2-second berserk** before it drops (same machinery as the Berserker's Death Frenzy, a fraction of the length). Raiders must now be escorted to survive contact instead of tanking through it |
| Speed | 6.5 |
| Damage | **3** melee (it is not a threat; it is a tax collector) |
| Defense | 0 / 0 / 0 / 0 |
| LoS | 16 |
| Cost / Pop | free / **0** |

**Stealing.** While a Plunderer is *raiding* it drains resources from its
victim's bank straight into its owner's. Raiding means:

- it has an enemy target it is engaging, **and**
- it is standing **outside its owner's own influence**, **and**
- it is standing **outside the curse's influence**.

A Plunderer loitering at home earns nothing; one standing in cursed ground
earns nothing. **The income only exists out in the world**, which is the
whole point — Feraldis has to be in someone's face to be paid.

Base take: **2 Supplies/s** per raiding Plunderer, deducted from the victim
faction (never below zero). A camp at full cap of 5 therefore pulls
~10 Supplies/s while its raiders are actually on target.

> **Retuned 2026-08-05 PM.** At 5 Supplies/s the raid economy won
> matches on its own: the lone Feraldis finished on 10,378 supplies
> while every rival sat between 0 and 504, one of them drained to
> nothing with no army left. Raiding is meant to be strong pressure,
> not the whole win condition — so the rate, the spawn cadence and the
> raiders' survivability were all cut in the same pass.

> **Floor rule.** If the victim is not a player faction (the curse, or
> neutral), the Plunderer still earns its take — it is generated rather
> than stolen. Without this an isolated or turtled-out Feraldis player
> would be hard-softlocked with no economy at all, which the original
> design already called out as a hazard.

#### Raiding Surveys — the Feraldis economy tech ladder

Researched **at the Raider Camp**, mirroring the Alanthor Guild's Survey
ladder in shape: each tier raises the take, and the branch techs unlock
*new resources* the Plunderers can carry off.

| Tech | Requires | Effect |
|------|----------|--------|
| **Raiding I** | — | Supplies take ×1.6 |
| **Raiding II** | Raiding I | Supplies take ×2.4 |
| **Raiding III** | Raiding II | Supplies take ×3.4 |
| **Iron Plunder** | Raiding I | Plunderers also steal **Iron** |
| **Veilstone Plunder** | Iron Plunder | Plunderers also steal **Veilstone** |
| **Veilsteel Plunder** | Veilstone Plunder | Plunderers also steal **Veilsteel** (slow trickle) |

> Secondary resources are stolen at a fraction of the supply rate, and
> like supplies they only flow while the Plunderer is genuinely raiding.
>
> *(spec gap — exact costs/research times are first-pass numbers; the
> supply-take rates in particular need a playtest against Alanthor's
> doubled-gathering and Runai's trade income.)*

---

## Special / choice buildings (carried from Age 0)

| Building | Feraldis modifier | Source |
|----------|-------------------|--------|
| Vault of Almiérra | **neutral** (0 %) | [Age_0.md § Vault](Age_0.md) |
| Shrine of Ridan | **−30 %** heal rate | [Age_0.md § Shrine](Age_0.md) |
| Fiendstone Keep | **+50 %** HP and arrow count | [Age_0.md § Fiendstone Keep](Age_0.md) |

> **The Fiendstone Keep is a shared building and gets NO Feraldis-specific
> changes** (user, 2026-08-05 rev.2). It is identical for every culture,
> including its Worker → Berserker conversion, which **any** culture may
> use. A Berserker fielded by a non-Feraldis player is a normal Berserker:
> it keeps its Death Frenzy (a property of the unit) but does **not** gain
> blood frenzy, which is gated on the owner's culture. Feraldis's only
> relationship to the Keep is the +50 % modifier above.

> Sanity check: if a Feraldis player picks the Fiendstone Keep as their Age 0
> choice building, the keep ends up at **3 000 HP** with extra arrow shots —
> the natural Feraldis fortress identity. If they picked Vault or Shrine
> instead, the +50 % modifier is wasted. Worth flagging in the UI ("Feraldis
> synergy") at the choice step.

---

## Feraldis rebalance — raiders, plunder, totems (2026-08-07)

Playtest finding: a Feraldis AI **held the map on raiders alone**, and once the
army-floor bug was fixed (House Raiders were satisfying the recruitment floor,
so it trained zero combat units in 46 minutes) it had raiders *and* an army.
Raiders cannot be nerfed on stats — they already have 1 HP and negligible
attack, and their whole identity is the death-frenzy spree. The remaining knob
is **how fast they arrive**.

### Raider throughput — the one knob

`CampSpawnInterval` 22 s → **60 s**. A Raider Camp still sustains
`CampPlundererCap` bodies, it just takes far longer to refill them, so raiders
become a persistent nuisance rather than a free standing army. Intended as the
*base* rate: a later Raider Camp technology should buy the interval back down,
making raider tempo something Feraldis invests in rather than something it
receives.

### Plunder is the whole army's job now

To compensate, **every Feraldis warrior plunders**, not just the Plunderer.
The three raiding conditions are unchanged — engaging an enemy, outside your
own influence, outside the curse's — so Feraldis is still paid only for being
in someone's face. A line soldier earns at `PlunderWarriorFraction` of a
dedicated Plunderer's rate: raiding stops being a separate unit type you build
and becomes what the Feraldis army *does*.

### War Totems — a totem must pay for itself

Totems already plant only on blood and drink it into permanent Fervor. They now
also **project an aura**: healing and a combat buff to friendly units in
`TotemAuraRadius`.

The aura is **paid for in blood**. Sustaining it drains the pool the totem sits
on, on top of the Fervor drink. A totem whose pool runs dry has
`TotemDryLifetime` (60 s) to find more before it **collapses on its own**.

This makes a totem a decision rather than free furniture: plant it where a real
battle happened, get a fortified position that heals your army — and watch it
consume the very thing keeping it alive. Spamming totems on thin blood now
costs more than it returns.

## Feraldis-unique buildings (new in Age 1)

### Mine — `Mine` *(2026-08-13 — Feraldis-only, Age 1)*

Workerless ore extraction. Placed **next to an iron or veilstone patch**
(placement is rejected elsewhere). It works **every** iron and veilstone node
inside its radius with **no workers at all**, and — the point of the building
— **without depleting them**.

| Stat | Value |
|------|-------|
| HP | 700 |
| Footprint | 3 x 3 |
| LoS | 12 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 180 S + 40 I |
| Build time | 25 s |
| Working radius | 18 |
| Yield | 0.25 Iron/s and 0.15 Veilstone/s **per node in range**, capped at 8 worked nodes |
| Depletion | **none** — nodes are never consumed |

> **The trade is the design.** Hand-mining is fast and finite; a Mine is
> slow and permanent. A patch worked by hand pays out sooner and then is
> gone forever; the same patch with a Mine pays less per second but pays for
> the rest of the match. The node cap stops one Mine dropped in the middle
> of a huge field from scaling without limit.
>
> **Feraldis-only, and Age 1.** Feraldis Workers cannot gather at all, so the
> Mine is that culture's ONLY route to ore — which is what makes it a culture
> identity building rather than a shared option. Gated behind the age-up for
> the same reason: a workerless, never-depleting income source in the
> pre-culture opening undercuts the hand-mining economy the whole Age 0
> economy is built on.
>
> **Note the id has no culture prefix.** It is `Mine`, not `Feraldis_Mine`, so
> the prefix-driven culture gate in `EntityInfoExtractor.GetRequiredCulture`
> does not catch it — it carries an explicit entry there instead, next to
> `ThessarasBazaar`. Renaming the id would ripple through the factory recipe
> table, `BuildingSizeConfig`, `BuildCosts`, `CommandRouter` build times,
> `BuildCommandPannel`'s BuildType map and the Feraldis AI.
>
> The era half of the gate is data — `minEra: 1` on the `Mine.asset`
> `BuildingDefSO`. The Mine has no entry in `Resources/TechTree.json`, so that
> asset is the only source and there is no fallback to keep in step.
>
> *(spec gap — whether Mines should be capturable, and whether an enemy Mine
> on your patch should block your own workers from hand-mining it.)*

### Veilsteel Forge — `Feraldis_VeilsteelForge` *(2026-08-07, SPECIFIED — NOT YET IMPLEMENTED)*

Feraldis's answer to the Alanthor Smelter: a building that produces veilsteel
over time so the culture can reach its T4 equipment tiers without trading for
it. Mirrors `Alanthor_Smelter` in role and cost band.

**Status: design only.** Buildings are defined by ScriptableObject assets that
the tech catalog loads (`TechCatalog._buildingSOsById`), so a new building needs
its `BuildingDefSO` authored alongside the tag, factory, presentation ID and
`BuildCosts.IdFromEntity` mapping. That is a data-authoring pass, not a code
edit, and is deliberately left unbuilt rather than half-wired.



### Pasture — `Feraldis_Pasture` *(2026-08-05 rev.2 — the cavalry house)*

Feraldis's cavalry building. Replaces the old "cavalry trains at the
Longhouse" arrangement, and there is still no Royal-Stable analogue.

| Stat | Value |
|------|-------|
| HP | 900 |
| Footprint | 4 × 3 |
| LoS | 16 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 200 S + 60 I |
| Build time | 30 s |

#### Trainable units

| Doc name | Role | Lvl unlock | Code id | Stats |
|----------|------|-----------|---------|-------|
| **Raider** | **Light cavalry** — fast harasser that **inflicts damage-over-time on enemy BUILDINGS** it hits. Raiders don't siege a structure down, they leave it burning while they ride on | L1 | `Feraldis_Raider` | HP 110 / spd 7.5 / dmg 10 melee / def 1/0/0/0 / range 1.5 / LoS 16 / 10 s train / 100 S + 30 I / pop 1. **Building DOT:** 4 HP/s for 8 s, refreshed per hit |
| **War Chariot** | Heavy cavalry. **Leaves a trail of blood behind it as it moves** — a mobile blood brush that paints frenzy ground and totem sites wherever it rides | L2 | `Feraldis_WarChariot` | HP 180 / spd 7.0 / dmg 16 melee / def 1/0/0/0 / range 1.5 / LoS 20 / 20 s train / 210 S + 80 I + 40 Vs / pop 2 |

> **The War Chariot is the answer to "what if there's no blood yet".** Every
> other route to blood requires something to die first. A Chariot can drive
> out to a contested spot and *manufacture* totem ground on the way — which
> is why it costs 2 pop and sits at L2.
>
> **Warboar Rider is RETIRED** (2026-08-05 rev.2) — the War Chariot replaces
> it as Feraldis's heavy cavalry. Its `Feraldis_WarboarRider` id and factory
> stay in the codebase for now but are off every roster.

---

### War Totem — `Feraldis_WarTotem` *(2026-08-05 canon — the influence engine)*

| Stat | Value |
|------|-------|
| HP | 500 |
| Footprint | 2 × 2 |
| LoS | 16 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 60 S + 20 I |
| Build time | 15 s |
| Attack | none |
| **Placement rule** | **Only on blood** — placement is rejected unless `BloodMap ≥ 0.15` at the totem's center |
| **Fervor** | Every 2 s the totem **drinks** 5 % of the blood in r 10, banking **Fervor** scaled by how saturated that ground is (cap 100; a totem on rich blood maxes out in ~80 s, by which time it has largely drunk the pool dry). Fervor never decays |
| **Influence** | Deposit rate 6 → 15 and radius 12 → 24, scaling linearly with Fervor. This is Feraldis's **only** influence source besides the universal Hall anchor — generic Feraldis buildings project no civic influence |
| Death | Normal killable building; on death its influence decays off the map like any lost source |
| Role | Territory engine. Implements [Curse_And_Shardroot.md](Curse_And_Shardroot.md) §2.6 "blood totems: planted on a blood pool ≥ min size, feedable (more nearby blood → stronger), non-decaying (Fervor is permanent), killable" |

> *(spec gap — first pass had these "planted by a military unit"; the
> 2026-08-05 direction says "placed", so v1 uses the normal build flow with
> the blood-gated placement check. Military-unit planting can return later
> as flavor.)*

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

### Feraldis Spearman — line infantry *(2026-08-05)*

Trains at War Hall L1. The Alanthor/Age 0 Spearman chassis traded
toward aggression: **−20 HP, +3 damage**, everything else identical.

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | **100** (vs 120 base) |
| Speed | 5.5 |
| Training time | 7 s |
| Armor type | infantry_heavy |
| Damage | **13** (melee; vs 10 base) |
| Attack speed | 1.5 s cooldown |
| Defense (M/R/S/Mg) | 1 / 0 / 0 / 0 |
| Attack range | 1.5 |
| LoS | 16 |
| Cost | 80 Supplies + 30 Iron |
| Pop | 1 |

### Feraldis Bloodletter — whirling skirmisher *(2026-08-05)*

Trains at War Hall L2. Low HP, low per-hit damage, high mobility.
Its attack is a **whirl that strikes every enemy within r 2.5** and
**inflicts Bleeding** (2 HP/s for 5 s, refreshed per hit, no stacking) on
each. Excellent blood-farmer: mass bleed-out deaths pool on one spot.

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 90 |
| Speed | **6.8** |
| Training time | 9 s |
| Armor type | light |
| Damage | 6 (melee, AoE whirl r 2.5). **Bleeding applies to units only** — buildings don't bleed |
| Attack speed | 1.2 s cooldown |
| Defense (M/R/S/Mg) | 0 / 1 / 0 / 0 |
| Attack range | 1.6 |
| LoS | 16 |
| Cost | 90 Supplies + 20 Iron |
| Pop | 1 |

### Feraldis Berserker — heavy melee, Death Frenzy *(re-specced 2026-08-05)*

Trains at War Hall L3; also produced by the Fiendstone Keep
miner-conversion (`BerserkerConversionSystem`, unchanged).

**Death Frenzy:** when damage would drop the Berserker below 1 HP, it
locks at 1 HP, becomes unkillable, and gains **+50 % attack and +50 %
move speed for 5 seconds — then it dies.** Once per life; the corpse
still splats blood normally.

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 150 |
| Speed | 5.8 |
| Training time | *(spec gap — no value in TechTree.json)* |
| Armor type | infantry_heavy |
| Damage | **16** (melee; +2 over first pass — "high attack" per 2026-08-05 direction) |
| Defense (M/R/S/Mg) | 2 / 0 / 0 / 0 |
| Attack range | 1.6 |
| LoS | 18 |
| Cost | 110 Supplies + 20 Iron + 20 Veilstone |
| Pop | 1 |

### Feraldis Suicidal — walking bomb *(2026-08-05; flavor name TBD)*

Trains at War Hall L2. **No attack.** Runs at the enemy soaking
ranged fire (heavy ranged defense), and **explodes on death from any
cause** — arrival detonation (auto-triggers within 2.5 of an enemy) or
being shot down en route. The explosion damages enemies only and leaves a
**large blood pool** (~r 6, heavy saturation) — converting enemy fire into
the ground Feraldis fights best on.

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 220 |
| Speed | 6.5 |
| Training time | 12 s |
| Armor type | infantry_heavy |
| Damage | 0 (never attacks — the detonation trigger at 2.5 fires before it could ever reach melee range 1.5); detonation 45 dmg, r 6, **enemies only, never friendly fire** (massing Suicidals is meant to be viable). Enemy **buildings** are valid blast targets and also trip the proximity trigger — the Suicidal doubles as a cheap demolition charge |
| Defense (M/R/S/Mg) | 0 / **4** / 0 / 0 |
| LoS | 14 |
| Cost | 90 Supplies + 30 Iron |
| Pop | 1 |

### Feraldis Hunter — ranged

Trains at the **Hall of Axes** *(authored and registered 2026-08-27).*

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
| Cost | 90 Supplies + 10 Iron + 20 Veilstone |
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
| Cost | 210 Supplies + 80 Iron + 40 Veilstone |
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
| Cost | 280 Supplies + 140 Iron + 70 Veilstone |
| Pop | 1 |

### Feraldis Iconoclast — enabler / no-attack religious unit

Trains at **Temple of Ridan, L3** (per the Runai-review Q#17 fix — the
Temple caps at 3 levels, not 4; the old "L4" reference is a retired spec
stage).

**Per Q#12 review — religious-unit tier rebalance.** All three culture
religious units (Scholar / Acolyte / Iconoclast) target a **single
"game-ender" cost bracket**: ~**300 Supplies + 150 Iron + 100 Veilstone + 30
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
| Cost | **300 Supplies + 150 Iron + 100 Veilstone + 30 Veilsteel** *(per Q#12 — rebalanced; adds 30 Veilsteel)* |
| Pop | 1 |
| Single unit / battalion | **Single** ([Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions)) |
| Role | **Enabler** — strips `NodeUntargetable` from veilstone nodes within `IconoclastAuraRadius` so other units can damage them. Cannot attack itself ([TechTree.json:1213](../../Assets/Resources/TechTree.json#L1213)). |

> Note: the per-culture religious units are intentionally asymmetric.
> Alanthor's Scholar **purifies** nodes, Runai's Acolyte **converts** them,
> Feraldis's Iconoclast **unprotects** them so the army can kill them. All
> three are now in the **same cost bracket** ([Overview.md § Religious-unit tier](Overview.md#religious-units--cross-faction-game-ender-tier)) — losing one is meant to be a major setback. The Border spec ([docs/Crystal_Curse_Sweep_And_Checklist_v2.md](../Crystal_Curse_Sweep_And_Checklist_v2.md)) already covers some of this.

---

## The Feraldis endgame — the Corruptor *(2026-08-05 rev.5)*

Alanthor purifies wells and holds them. Runai pacifies and holds them.
**Feraldis holds nothing — it breaks them.**

### Corruptor — `Feraldis_Iconoclast` *(re-badged; display name "Corruptor")*

Feraldis's Temple-trained religious unit, the mirror of the Alanthor
Scholar. Trains at **Temple of Ridan L3**.

> **Why the old id:** the Iconoclast already existed as Feraldis's Temple
> unit, but the "aura that strips well protection" it was specced for was
> **never implemented** — nothing in the codebase read `IconoclastTag`, and
> it had no train button either. Rather than ship a second Feraldis
> religious unit, the Corruptor IS that unit, finally given a mechanic.
> The `IconoclastTag` is kept for reference stability; `CorruptorTag`
> carries the behaviour.

| Field | Value |
|------|-------|
| HP / Speed | 280 / 3.2 |
| Defense (M/R/S/Mg) | 4 / 3 / 1 / 2 |
| Cost | 300 S + 150 I + 100 Vs + 30 Vst |
| Pop | **4** *(the factory said 1 and the pop table said 4 — reconciled to 4; this is a game-ender unit and is meant to be scarce)* |
| Train time | 68 s, Temple **L3** |

### The verb: CORRUPT → BREAK

1. **Channel** — the Corruptor walks to a living well and channels for
   **40 s**. Interruptible: kill the Corruptor, or drag it out of range.
   One corruption per well; a rival ritual on the same well blocks it.
2. **Crack** — the well becomes **vulnerable for 60 s**. For that window it
   can be damaged *and* auto-acquired, so an army attack-moving onto it
   will engage. Outside the window wells are never auto-targeted at all.
3. **Defend** — while the well is open the curse fights for it: waves of
   **3 defenders** on a ramping cadence (6 s → 2.5 s as the window runs
   down), capped at **30** per corruption.
   **NO GODSPLINTERS.** A Godsplinter is magic-siege-tank class even after
   its nerf; putting them in a wave that has to be survived *while* killing
   a 4000 HP well made the objective impossible.
4. **Break it** — 4000 HP inside 60 s, or the well seals and you start over.
5. **Respawn** — a destroyed well returns after **exactly 10 minutes**
   (540 s rubble + 60 s rebuild).

**Kill credit follows the Corruptor's faction**, not the last hit. A
cracked well is open to everyone standing there, so without this rule a
rival could rush in and smash it purely to deny the Feraldis win.

### Victory

**Destroy every well at once and Feraldis wins instantly** — no hold timer,
unlike Alanthor's and Runai's 5 s domination grace. The tempo rule already
refreshes the rubble timer on your other kills each time you break a new
well, which is what makes chaining N wells inside the respawn window
possible at all.

> *(spec gap — the 60 s window and 4000 well HP have never been played
> against each other. If breaking a well proves impossible, the window is
> the knob to widen, not the well's HP.)*

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

1. **War Hall existence** — **superseded by rev.4:** "War Hall" is now
   the cultured **Barracks**. The cultured HALL is the Age 0 Hall renamed
   (same entity, same base HP, culture-specific tech list) but still needs
   a name of its own. `main: FiendstoneKeep` in
   [TechTree.json](../../Assets/Resources/TechTree.json) is a stale
   artefact — drop or replace.
2. **Population / Houses** — **resolved (reversal of earlier "no Houses"
   rule).** Feraldis **does** have Houses, identical to Alanthor's base
   shape. The Feraldis twist: every build / upgrade spawns N autonomous
   uncontrollable Raider units that immediately attack the closest enemy
   target. Pop ladder: standard 15 / 20 / 25 + Longhouse +10. **Cross-
   faction impact:** updates [Age_0.md § Age-up transitions](Age_0.md#age-up-transitions),
   [Overview.md § Age-up](Overview.md#age-up-transform-dont-replace).
3. **Thrower Camp existence** — **superseded 2026-08-27: it is the Hall of Axes, a stand-alone building, not a cultured Age 0 range.** ~~Thrower Camp is the Age 0~~
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
    **game-ender** cost bracket: ~300 Supplies + 150 Iron + 100 Veilstone
    + 30 Veilsteel. **No Glow** in the cost — Iconoclast is the Glow-
    unblocker for Feraldis, so requiring Glow would be a chicken-and-egg.
    **Cross-faction impact:** updates Scholar and Acolyte costs in
    [Age_1_Alanthor.md](Age_1_Alanthor.md) and [Age_1_Runai.md](Age_1_Runai.md).

## Decisions (2026-08-05 second pass — user direction)

13. **Theme locked: fire and blood.** Feraldis wants to taint the curse
    further, not guard against it.
14. **Frenzy on blood** — all Feraldis units fighting on top of blood
    (BloodMap) gain +25 % attack damage / +20 % attack speed with a ~1 s
    linger. Replaces the influence-keyed "+attack / last stand" aura from
    Curse_And_Shardroot §2.6 (last-stand is now the Berserker's unit
    mechanic; the buff key is **blood**, not influence). Resolves the old
    Q#5 "bloody-ground mechanic — deferred".
15. **Influence = War Totems on blood pools.** New `Feraldis_WarTotem`
    building, placement blood-gated, drinks blood into permanent Fervor
    that scales its influence. Generic Feraldis buildings project no civic
    influence; the Hall keeps its universal anchor. §2.6's "planted by a
    military unit" is relaxed to normal build-flow placement for v1.
16. **Cultured Barracks renamed War Hall** (was Longhouse / Hall of Warriors) with a
    fixed four-unit roster: Feraldis Spearman (L1) → Bloodletter +
    Suicidal (L2) → Berserker (L3). Swordsman / Royal Guard ladder,
    Warboar Rider, and the batch [5,10] discount are **parked**, not
    deleted. The existing 1.75×-time / 2-units-per-completion Feraldis
    training rule stays as the batch flavor.
17. **Feraldis unlocks at Age 1** exactly like Alanthor
    (`CultureConfig.IsComingSoon` drops Feraldis; Runai stays locked).

## Decisions (2026-08-05 rev.4 — user direction)

18. **War Hall = the cultured BARRACKS** (not the cultured Hall, and not
    "Hall of Warriors"). The cultured Hall's name is now an open question.
19. *(superseded 2026-08-27 — Practice Range is retired and the Archery Range is Alanthor-only; Thrower Camp is a separate building.)* **Practice Range = the cultured Archery Range for Alanthor**; **Thrower
    Camp** is the Feraldis one. Both are display renames of the same entity.
20. **Feraldis builds Archery Ranges** like everyone else — it is a
    universal Age 0 building and was never culture-gated. The Feraldis
    ranged roster trains there.
21. **Hunting Lodge and Logging Station are CUT.** With huts converted to
    Raider Camps there was nothing for a gathering upgrade to upgrade.
22. **Raider Camps claim no gather area at all** — they neither harvest nor
    deny area to neighbouring huts (they were doing the latter for free).
23. **Camp spawn interval 5 s → 12 s** (playtest: waves came far too fast).
24. **Feraldis Workers BUILD ONLY** — no gathering whatsoever — but are real
    light infantry (110 HP / 9 dmg) rather than helpless civilians. They do
    not auto-acquire targets; they fight when ordered.
25. **Feraldis Scouts lose the scout-sight ramp** (LoS drops to an ordinary
    18) and gain an **eagle** that circles them carrying its own LoS 30.
    Same vision budget, a sweeping arc instead of a static bubble.
26. **New building: the Mine** — Feraldis-only and Age 1 as of 2026-08-13
    (it was briefly specced as a universal Age 0 building). See
    [§ Mine](#mine--mine-2026-08-13--feraldis-only-age-1) above.

## Remaining open questions

- **Cultured Hall name** — "War Hall" moved to the Barracks; what is the
  Feraldis Hall called?
- **Suicidal flavor name** — "Suicidal" is a working name (Immolator?
  Blood-sworn? Pyrebearer?).
- **Warboar Rider / Swordsman / Royal Guard** — parked; return, rework, or
  cut?
- **Frenzy + sect InBloodPool stacking** — allowed for now; playtest.
- **Raider auto-spawn stats** — concrete stat block for the auto-spawn
  Raider (from Houses + from persistent Gatherer's Huts at age-up).
  Suggested: ≈ 80 HP, speed 6.0, single-unit melee. Need final values.
- **Raiders-per-House ramp** — currently suggested 1 / 2 / 3 per
  L1 / L2 / L3 build/upgrade. Balance TBD.
- **L2 / L3 ranged tier names** at the Hall of Axes (placeholder
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
