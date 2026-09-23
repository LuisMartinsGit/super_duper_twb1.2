# Age 1 — Alanthor

> Defensive culture. Stone / medieval aesthetic. Strength comes from walls,
> long-range archery, and steady building HP / repair scaling. Territorial
> identity: fortifications project **Alanthor influence** on the shared
> influence map ([Overview.md § The influence map](Overview.md)). The old
> closed-wall-compartment supply income was removed 2026-07-06.
>
> **See also:** [Overview.md](Overview.md) (two-age framing), [Age_0.md](Age_0.md)
> (pre-culture starting buildings), and the cross-age [Petriarchy doc TBD] for sects.
>
> Doc version: 2026-05-19 — **first-pass extract from code**. Numbers below
> come from [TechTree.json](../../Assets/Resources/TechTree.json) era 2 / Alanthor + [BuildingUpgradeConfig.cs](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs)
> + [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs). Items marked **(new — not yet in
> code)** come from the design draft and need to land in code. Items marked
> **(code only — confirm)** are in code but not in the design draft.

---

## Culture identity

| Aspect | Alanthor |
|--------|----------|
| Focus | **Defense** (walls, towers, long-range archery, building HP) |
| Style | Stone / Medieval |
| Economy | Gathering, doubled inside the border: **ground inside Alanthor influence (≥0.5) produces 2× resources**. The old walled-compartment supply income was removed (2026-07-06). |
| Territory | Every Alanthor building except Gatherer's Huts grants influence — the **Hall is the potent anchor** (r32 @ 16/s, claims a buildable bubble within seconds; a fresh Age 1 start owns nothing else), fortifications are strong (hubs r18 / instances r9 / towers r22 @ 9/s), everything else weak (r16 @ 4.5/s). Influence **decays back to neutral** (5 %/s proportional) once its source dies. **Alanthor cannot build outside its own influence border** — except **Gatherer's Huts** and **Watch Towers**, which may be placed anywhere (huts harvest beyond the border; towers are forward claims that project influence into new ground). Claimed ground also repaints the terrain itself via the `AlanthorInfluence` terrain layer (see [Overview.md § The influence map](Overview.md)). |
| Vault yield modifier | **+30 %** (best of the three cultures) |
| Shrine heal modifier | neutral (0 %) |
| Fiendstone Keep HP/arrows | **−50 %** (worst of the three) |
| Main upgrade hooks | `KingsCourt` global aura: **+10 % building HP**, **+15 % repair rate** |

---

## Conventions

- **Building levels** in Age 1 are **L1 / L2 / L3** (lvl 0 was the pre-culture
  Age 0 form, which no longer exists once the building reskins at age-up).
- HP / train-time / attack-cooldown multipliers come from
  [BuildingUpgradeConfig.cs:27-41](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs#L27)
  — absolute over base, not cumulative (so L2 HP = base × 1.15, not L1 × 1.15).
- Upgrade durations (from [BuildingUpgradeConfig.cs:57](../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs#L57)): L1 → L2 = 30 s, L2 → L3 = 45 s. (L0 → L1 happens at age-up automatically — no manual upgrade.)
- "Base HP" is the **uncultured Age 0 HP** carried forward (Hall = 2 400,
  Barracks = 800, Archery Range = 600, Hut = 600). Cultured renames keep the
  same base — only the multiplier ladder applies.
- **Pop / cost columns** are L1 build cost (one-time) → L2 / L3 upgrade costs.
- Damage formula is unchanged from Age 0.

---

## Cultured carryover buildings

These four buildings exist in Age 0 in their pre-culture form and become the
following on age-up. Stats below cover their **Alanthor L1 → L3** form only;
the pre-culture lvl 0 stats live in [Age_0.md](Age_0.md).

### Town Hall — cultured Hall

**Code id:** `KingsCourt` ([BuildingFactory CreateKingsCourt](../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs)).
**Doc id:** Town Hall.

> Open question: the in-code main building is `KingsCourt` ([TechTree.json:1238](../../Assets/Resources/TechTree.json#L1238)) — design wants it renamed to `AlanthorTownHall` / `TownHall` to match the draft. The visual/aesthetic identity (king's-court motifs) can stay; only the id needs renaming.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs Age 0 base 2 400) | 2 640 | 2 760 | 2 880 |
| Line of Sight | 26 | 26 | 26 |
| Auto-fire max targets | 1 | 2 | 4 |
| Provides population | 10 *(code value — design draft is silent)* | 10 | 10 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Build / upgrade cost | 360 S + 80 I (at age-up — already standing) | 200 S + 50 I + 15 C | 400 S + 100 I + 40 C + 5 Vs |
| Upgrade duration | — | 30 s | 45 s |

**Global aura** (from any built Town Hall): +10 % HP and +15 % repair rate
to all friendly buildings within faction. ([TechTree.json:1250-1253](../../Assets/Resources/TechTree.json#L1250))

#### Trainable units (carryover)

> **Restored in data 2026-09-12.** `KingsCourt.asset` shipped with an EMPTY
> `trains` list, so the carryover this table describes did not exist in the
> game. Age-up renames the Hall into the King's Court, and the Hall is the
> only Worker and Scout trainer — so the moment any Alanthor faction aged up
> it permanently lost the ability to make workers or scouts. Log-proven,
> Hollow Table 2026-09-12: Red aged up at minute 7, fell to ONE worker after
> losing 23 units, and could then never clear its open build sites, never
> raise a Barracks, and never field an army; it ended holding 8,168 iron with
> two units alive. It also explains factions going permanently blind mid-match
> once their scouts died, which is what the Outrider stand-in rule in
> Game_AI.md 7 was written to paper over.

| Unit | Train time | Cost | Pop |
|------|-----------|------|-----|
| **Worker** | 5 s | 50 Supplies | 1 |
| **Scout** | 4 s | 55 Supplies | 1 |

#### Researchable techs

The 4-tier **Tools** ladder uses the **per-battalion / per-unit upgrade**
model defined in [Overview.md § Per-battalion upgrades](Overview.md#per-battalion-military-upgrades-cross-faction-rule):
the research **unlocks** the next tier of upgrade for **each individual
Worker** (Workers are single units, not battalions, but the same
unlock-then-pay-per-unit pattern applies). Workers must be paid for and
upgraded individually after the research lands.

The **Wheel cart / Cranes / Mason Guild** track is a separate faction-wide
passive set (no per-unit upgrade — they apply automatically when researched).

| Tech (tier) | Building lvl req. | Effect (unlock) | Status |
|-------------|-------------------|------------------|--------|
| **Stone tools** (T1) | L1 | **Unlocks** tier-1 Worker upgrade (gather-speed bump, per-Worker cost when applied) | Currently `ImprovedTools` in code — needs rewire to per-unit unlock model *(new)* |
| **Iron tools** (T2) | L2 | **Unlocks** tier-2 Worker upgrade | *(new)* |
| **Veilstone tools** (T3) | L3 | **Unlocks** tier-3 Worker upgrade | *(new)* |
| **Veilsteel tools** (T4) | L3 + post-research of T3 | **Unlocks** tier-4 Worker upgrade (consumes Veilsteel per Worker when applied) | *(new — verify L4 fits in the L3 cap)* |
| **Wheel cart** | L1 | +20 % worker move speed *(faction-wide passive)* | *(new — the old carry-capacity effect is gone: mined resources credit the stockpile directly, 2026-07-20)* |
| ~~**Cranes**~~ | — | *(removed 2026-07-20 — carry capacity no longer exists; workers never carry resources)* | — |
| **Mason Guild** | L2 | +20 % HP to all friendly buildings *(faction-wide passive)* | **Replaces and renames** the old `Alanthor_MasonGuild` code tech (was "+15 % HP / +20 % repair"). The "Masonry" name from the draft is dropped — final canonical name is **Mason Guild**. |
| **Advance from Age 0** | — | already triggered to enter Age 1 | (paid in Age 0) | Hooked, see `Research_Era2` |

#### Existing code techs that need re-homing or removal

| Code id | Current effect | Action |
|---------|----------------|--------|
| `Alanthor_StoneLedgers` ([TechTree.json:1576](../../Assets/Resources/TechTree.json#L1576)) | +8 Supplies per 10u² closed compartment / min | **Keep** — folds into the wall-economy mechanic; researchedAt `KingsCourt`. *(Final yield number is a placeholder pending playtest, per Q#8 below.)* |
| `Alanthor_MasonGuild` ([TechTree.json:1586](../../Assets/Resources/TechTree.json#L1586)) | +15 % building HP, +20 % repair | **Keep as canonical "Mason Guild"** — rebalance the effect numbers to match the +20 % HP figure in the Mason Guild row above (currently +15 % HP). |

---

### Garrison — cultured Barracks

**Code id:** `Barracks` (no Alanthor-specific tag in [BuildingFactory](../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs); the Alanthor Barracks is a visual reskin of the same entity, per [Alanthor_Visual_Systems_Spec.md](../Alanthor_Visual_Systems_Spec.md)).
**Doc id:** Garrison.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 800) | 880 | 920 | 960 |
| Line of Sight | 18 | 18 | 18 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Upgrade cost | 80 S + 20 I | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |
| Upgrade duration | 20 s (at age-up the L0 → L1 swap is automatic; manual L2/L3 only) | 30 s | 45 s |

#### Trainable units

The Garrison trains a **3-tier infantry ladder** (Spearman → Swordsman →
Royal Guard) plus the **Sentinel** as a parallel late-game damage-sponge /
siege-melee unit. **Cataphract has been moved out of Garrison into a new
Royal Stable building** (see [§ Royal Stable](#royal-stable--cataphract-host-new) below).

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats (from code, where available) |
|----------|--------------|--------------------|-------------------|-------------------|
| **Spearman** | early-mid game line infantry | L1 | `Spearman` (renamed from `Swordsman` per [Age_0.md](Age_0.md)) | HP 120 / 7 s train / 80 S + 30 I / pop 1 |
| **Swordsman** | mid-late game line infantry | L2 | **(new — no code entry; design draft only)** | TBD (place between Spearman and Royal Guard on the damage / HP curve) |
| **Royal Guard** | late-game line infantry (Spearman apex) | L3 | **(new — no code entry; design draft only)** | TBD |
| **Nobleman** | all-rounder — no bonus vs anything, expensive | L2 | `Alanthor_Nobleman` *(shipped)* | Confirmed 2026-08-27 as a deliberate fourth Garrison trainable. It is **not** the Royal Guard: the Nobleman's identity is having no counter-matchup at all, so it never wins an exchange on type advantage and never loses one either. Prices the flexibility. |
| **Sentinel** | late game **damage-sponge / siege-melee** *(parallel to the line-infantry tier, not on it)* | L2 | `Alanthor_Sentinel` ([TechTree.json:1442](../../Assets/Resources/TechTree.json#L1442)) | HP 160 / spd 5.0 / 18 s train / dmg 12 melee / def 3/2/0/1 / range 1.7 / cost 90 S + 20 Vs / pop 1 |

> **Battalion unit** per [Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions) —
> each of the four Garrison trainables is a battalion. Battalion sizes
> still TBD. Stats above are battalion-total values pending verification.

#### Researchable techs

Researching a weapon-tier tech **unlocks the next tier of per-battalion
upgrade** for units trained at the Garrison — it does **not** auto-upgrade
existing battalions. See [Overview.md § Per-battalion military upgrades](Overview.md#per-battalion-military-upgrades-cross-faction-rule)
for the full pattern; the per-battalion upgrade cost is separate from the
research cost listed below.

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|-----------------|--------|
| **Conscription** | L1 | +20 % training speed at the Garrison (faction-wide passive — not per-battalion) | *(new — replaces `BasicDrills`, see Age 0 doc)* |
| **Academy** | L2 | TBD — design draft only | *(new)* |
| **Stone weapons** (T1) | L1 | **Unlocks** tier-1 weapon upgrade for Garrison battalions (Spearman / Swordsman / Royal Guard / Sentinel) | *(new — replaces `WoodenArmor`)* |
| **Iron weapons** (T2) | L2 | **Unlocks** tier-2 weapon upgrade | *(new)* |
| **Veilstone weapons** (T3) | L3 | **Unlocks** tier-3 weapon upgrade | *(new)* |
| **Glow-infused weapons** (T4) | L3 + Glow available | **Unlocks** tier-4 weapon upgrade (consumes Glow per battalion when applied) | *(new — Glow availability is now defined: see [Overview.md § The Glow economy](Overview.md#the-glow-economy-cross-faction))* |

---

### Archery Range — era 2, no cultured rename

> **Renamed back, 2026-08-27.** This section was "Practice Range — cultured
> Archery Range". The Archery Range is `minEra: 2` and never appears in Age 0,
> so for Alanthor there is nothing to rename: it is the Archery Range in both
> ages. Every "Practice Range" below should be read as "Archery Range".
> Its roster is settled as **Archer / Crossbowman / Longbowman**, and the
> Archer's canonical id is now `Alanthor_Archer` (the bare `Archer` id survives
> only as a factory alias for old scenarios and saves).

**Code id:** `Alanthor_PracticeRange` ([TechTree.json:1351](../../Assets/Resources/TechTree.json#L1351)).
**Doc id:** Archery Range *(the user's draft still calls it "Archery Range" — match design name; code id `Alanthor_PracticeRange` is fine internally).*

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 600) | 660 | 690 | 720 |
| Line of Sight | 22 | 22 | 22 |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Provides population | 0 | 0 | 0 |
| Garrison slots / arrow-fire | 6 / yes ([TechTree.json:1370](../../Assets/Resources/TechTree.json#L1370)) | 6 | 6 |
| Upgrade cost | (at age-up — already standing as Archery Range) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

> The TechTree.json over-tuned numbers (1 500 HP, +8 pop) are **rejected
> per design Q#3 review** — Practice Range follows the standard cultured-
> building HP path (660 / 690 / 720) and provides 0 population. Code values
> need to drop to match.

#### Trainable units

The Practice Range trains a **3-tier ranged ladder** in parallel to
Garrison's infantry ladder (per the per-battalion upgrade pattern). Code
today defines only Archer + Crossbowman; the L3 apex is new.

| Doc name | Role / window | Building lvl unlock | Mapped to code id | Stats |
|----------|---------------|--------------------|-------------------|-------|
| **Archer** | early-mid game ranged | L1 | `Archer` | HP 90 / 15 s train / 50 S + 25 I / range 25 / pop 1 |
| **Crossbowman** | mid-late game ranged | L2 | `Alanthor_Crossbowman` ([TechTree.json:1468](../../Assets/Resources/TechTree.json#L1468)) | HP 100 / spd 5.0 / 22 s train / dmg 13 ranged / def 0/2/0/0 / range 13 / min 4 / cost 70 S + 15 Vs / pop 1 |
| **L3 apex ranged** *(name TBD — "Longbowman"?)* | late-game ranged | L3 | **(new — no code entry)** | TBD |

> **Battalion units.** Stats above are battalion totals pending size
> finalization per [Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions).

#### Researchable techs

Three faction-wide / building-passive techs **plus** a 4-tier arrow ladder
that uses the same per-battalion upgrade model as Garrison's weapon ladder
(per design Q#7).

| Tech | Building lvl req. | Effect (unlock) | Status |
|------|-------------------|------------------|--------|
| **Choreographed volleys** | L1 | Active skill on the Practice Range: 2× fire rate for 5 s on all Archers in faction, 40 s cd *(faction-wide active)* | *(new)* |
| **Fletching** | L2 | +15 % attack range for all Archer-class units *(faction-wide passive)* | *(new)* |
| **Stone-tipped arrows** (T1) | L1 | **Unlocks** tier-1 arrow upgrade for Practice Range battalions (per-battalion cost when applied) | *(new — replaces the old "single tech" model)* |
| **Iron-tipped arrows** (T2) | L2 | **Unlocks** tier-2 arrow upgrade | *(new)* |
| **Veilstone-tipped arrows** (T3) | L3 | **Unlocks** tier-3 arrow upgrade | *(new)* |
| **Glow-tipped arrows** (T4) | L3 + Glow available | **Unlocks** tier-4 arrow upgrade (consumes Glow per battalion when applied) | *(new — same Glow path as [Overview.md § The Glow economy](Overview.md#the-glow-economy-cross-faction))* |

---

### House — cultured Hut

**Code id:** `Hut` (visual reskin only, per [Alanthor_Visual_Systems_Spec.md:150](../Alanthor_Visual_Systems_Spec.md#L150) — `BDP_Alanthor_Hut`).
**Doc id:** House.

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP (vs base 600) | 660 | 690 | 720 |
| Line of Sight | 14 | 14 | 14 |
| Provides population | 15 (+ HutBonusPop[1]) | 20 (+10) | 25 (+15) |
| Upgrade cost | 60 S + 10 I | 120 S + 25 I + 5 C | 240 S + 50 I + 15 C |
| Upgrade duration | 20 s | 30 s | 45 s |

No trainable units or tech.

---

### Gatherer's Hut (Age 0 carryover) → the **Guild** (canonical 2026-07-08)

> **Superseded by the tech-tree calculator canon ([tools/calculator/techtree.json](../../tools/calculator/techtree.json), 2026-07-08).** The Alanthor
> Gatherer's Hut **no longer converts** to a Wall Hub or Watch Tower at age-up.
> Instead it **becomes the Guild** — it keeps generating Supplies and gains a
> per-building **level ladder (L1–L3)** plus two faction-wide research tracks:
> a **resource Survey** track and a **defensive reinforcement** track. Walls and
> Watch Towers remain **directly buildable** primitives (see [§ Wall System](#wall-system-bfme2-hub-and-segment)); they are simply
> no longer sourced from a hut conversion. The per-hut Wall Hub / Watch Tower
> conversion prompt described below is **retired**.
>
> **Guild — level ladder** (per building, via the standard `BuildingUpgradeable`
> path; auto-bumps to L1 at age-up, L2/L3 manual):
>
> | Level | Cost | Effect |
> |-------|------|--------|
> | **L1** | 120 S + 25 I + 5 Vs | +5 Supplies/tick, +10% HP, +gather radius |
> | **L2** | 240 S + 50 I + 15 V + 5 Vs | +10 Supplies/tick (cumulative), +15% HP |
> | **L3** | 360 S + 75 I + 40 V + 20 Vs | +20 Supplies/tick (cumulative), +20% HP |
>
> **Guild — Survey track** (faction-wide research; the hut also generates the
> resource, doubled inside the owner's influence border). The old single
> `DeepGathering` tech is **removed outright** (2026-08-04) — the Surveys are
> the only hut drips:
>
> | Tech | Yield |
> |------|-------|
> | Iron Surveying I / II / III | +12 / +24 / +42 Iron / min |
> | Veilstone Survey I / II | +6 / +18 Veilstone / min |
> | Veilsteel Survey | +6 Veilsteel / min — **max-level (L4) huts only** |
>
> **Level scaling (2026-08-11 — "fully developed huts are the late-game
> mine"):** the Iron / Veilstone survey drips scale with the hut's Guild
> level — **L2 ×1.5, L3 ×2** (before the influence doubling). Map iron
> deposits are finite and run dry around mid-game (67-min playtest:
> map-wide iron extinction at minute 30 froze every faction); a maxed hut
> ring with Iron Surveying III is DESIGNED to carry the whole iron economy
> from there — e.g. an L3 hut inside influence yields 42 × 2 × 2 =
> 168 Iron / min.
>
> **Guild — Reinforcement track** (faction-wide research; behaviour-by-id):
>
> | Tech | Effect |
> |------|--------|
> | Iron reinforcements | Hut auto-repairs 5 HP/s once out of combat ≥ 10 s |
> | Veilstone walls | Below 50% HP, **casts** a **Slow** burst (−50% speed for 7.5 s) on every enemy in the hut's **gather radius** (~19.5) — one-shot, **90 s cooldown**; repair → 10 HP/s |
> | Veilsteel Pylons | The low-HP cast becomes a **Stop** burst (−100% speed for 10 s, same gather-radius area, 90 s cooldown); repair → 20 HP/s |
>
> Implemented in: `GathererHutIncomeSystem` (surveys + level supplies),
> `GathererHutReinforcementSystem` (auto-repair + slow/stop), `BuildingUpgradeConfig`
> (level ladder), and `Assets/Resources/TechTree.json` (tech defs).

> **Historical — retired [task-wall-system-bfme2-rework-109](../../.deft/tasks/task-wall-system-bfme2-rework-109/task.md) (2026-05-21).** The text below described the earlier per-hut Wall Hub / Watch Tower conversion prompt, itself a replacement for an even older "wall-segment anchor" model. Both are **retired** by the Guild canon above; the section is kept for provenance only.

At age-up for **Alanthor**, each Gatherer's Hut owned by the player gains
a `GathererHutAgeUpChoice` marker (no automatic transformation, no
auto-fortify radius). Selecting the hut surfaces a 2-button **Convert**
action cluster in the ACTIONS panel:

| Choice | Glyph | Result | Cost (supplies / iron) | Conversion time |
|--------|-------|--------|------------------------|------------------|
| **Convert to Wall Hub** | `castle` | Hut entity is destroyed; a fresh **Wall Hub** (`Alanthor_Wall`) is spawned at the hut's footprint. The hub immediately auto-forms segments to any other completed friendly hubs within `MaxAutoSegmentDistance` (see [§ Wall System](#wall-system-bfme2-hub-and-segment)). | **60 S + 40 I** | **5 s** |
| **Convert to Watch Tower** | `eye` | Hut entity is destroyed; a fresh **Watch Tower** (`Alanthor_Tower`) is spawned at the hut's footprint. | **40 S + 30 I** *(discount vs the 140 S + 70 I fresh-build cost — the hut is being re-used)* | **5 s** |

The conversion is **paid up-front, timed, no builder required** (matches
the existing "instant-paid timer" pattern used elsewhere in the codebase).
The hut continues generating its Age-0 gathering income **for the duration
of the timer**; once the timer elapses, the hut entity is replaced by the
chosen building.

**The choice is per-hut, not faction-wide.** A typical Alanthor player ends
up with a mix — three or four huts on the wall perimeter become Wall Hubs
to anchor the compartment, one or two on inner / high-ground spots become
Watch Towers for arrow coverage and far-LoS scouting.

**If the player never picks** — the hut keeps the marker and continues
generating Age-0 income indefinitely. There is **no timeout, no auto-
default**. This is intentional: Alanthor's age-up does not force a
re-planning beat; the player commits each hut on their own schedule.

**Huts never despawn and stay buildable** (directive 2026-07-04) —
Alanthor Gatherer's Huts do not self-destruct at age-up, and **new
Gatherer's Huts remain buildable throughout the whole game** (the old
"Alanthor cannot build Gatherer's Huts after culture selection" rule is
retired). Conversion to Wall Hub / Watch Tower stays a purely optional
per-hut choice.

**Cancel** — once the timer starts, the conversion cannot be cancelled
in v1 (matches the existing structure-construction model). The
`GathererHutAgeUpChoice` marker disappears on click; the hut is in
"converting" state until the timer elapses.

**AI behaviour** — Alanthor AI in v1 does **not** convert its own huts to
Wall Hubs (the AI wall-building path is deferred). AI huts retain their
Age-0 gathering behaviour. See [§ Wall System / AI Behaviour](#ai-behaviour) for the rationale.

---

## Special / choice buildings (carried from Age 0)

Vault / Shrine / Fiendstone Keep are built at lvl 1 in Age 0 and persist
across age-up unchanged in structure. Only the **culture modifier** applies
when Alanthor is picked:

| Building | Alanthor modifier | Source |
|----------|-------------------|--------|
| Vault of Almiérra | **+30 %** yield on interest | [Age_0.md § Vault](Age_0.md) |
| Shrine of Ridan | **0 %** (neutral) heal rate | [Age_0.md § Shrine](Age_0.md) |
| Fiendstone Keep | **−50 %** HP and arrow count | [Age_0.md § Fiendstone Keep](Age_0.md) |

Their L1 → L3 stats, tech tables, and trainables are in
[Age_0.md](Age_0.md) — no Alanthor-specific overrides beyond the modifier.

---

## Alanthor-unique buildings (new in Age 1)

These exist only after the player picks Alanthor at age-up. All present in
[TechTree.json](../../Assets/Resources/TechTree.json) era 2 / Alanthor and
[BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs).

> **Cost source:** all build costs in this section are taken from
> [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs)
> (runtime authoritative). Where TechTree.json gave different numbers,
> the JSON entry needs to be updated to match — flagged in the
> implementation backlog.

### Royal Stable — Cataphract host *(new)*

> **(new — no code entry)** — added per design Q#2 review to move
> Cataphract out of the Garrison roster. The Royal Stable is Alanthor's
> dedicated cavalry trainer; same level ladder as the other military
> buildings (L1 → L3, ×0.870 / ×0.800 / ×0.714 train-time multipliers).

| Stat | L1 | L2 | L3 |
|------|----|----|----|
| HP | TBD (suggest ≈1 000 base) | base × 1.10 | base × 1.20 |
| LoS | TBD (suggest 18) | 18 | 18 |
| Defense (M/R/S/Mg) | TBD | | |
| Train-time multiplier | ×0.870 | ×0.800 | ×0.714 |
| Build cost (L1, at age-up or later) | TBD (suggest 220 S + 80 I, mid-tier military) | 160 S + 40 I + 10 C | 320 S + 80 I + 30 C |

#### Trainable units

| Doc name | Building lvl unlock | Mapped to code id | Stats (from code) |
|----------|---------------------|-------------------|-------------------|
| **Outrider** *(new 2026-08-04)* | L1 | `Alanthor_Outrider` | Cheap, fast LIGHT cavalry — the raid/screen slot under the Cataphract. HP 95 / spd 8.2 / **LOS 34** / 22 s train / dmg 12 melee / def 1/1/0/0 / cost 130 S + 40 I / pop 1 |

**The Outrider is the cavalry scout (2026-09-12).** Its line of sight went
22 -> **34 m** so that riding IS its scouting method. It has no vision bloom
and never perches: the Scout's Oracle model trades standing still for a 55 m
circle, and the Outrider trades a smaller circle for covering ground at 8.2
m/s. Sweep rate is speed times width, so 8.2 x 68 m beats a moving scout's
6 x 36 m by nearly three to one while a perched scout sees more of one spot
and nothing else.

This is also what the AI drafts when its scouts are dead (Game_AI.md 7).
The wider sight is a real combat buff as well as a scouting one - light
cavalry now spots trouble before it rides into it, which suits the raid and
screen slot it already fills.
| **Cataphract** | L1 | `Alanthor_Cataphract` | **Rebalanced 2026-08-04 (substantially pricier, slightly weaker):** HP 160 / spd 6.6 / 40 s train / dmg 18 melee / def 2/1/0/0 / range 1.6 / **cost 320 S + 120 I + 60 C** / pop 2 |
| *(L2 / L3 cavalry tiers)* | TBD | **(new)** | TBD |

#### Researchable techs

The same 4-tier per-battalion upgrade pattern as Garrison / Practice Range
applies — exact tech names TBD (suggest **Barding** / **Iron barding** /
**Veilstone barding** / **Glow-bonded barding**).

### Wall primitives (`Alanthor_Wall`, `Alanthor_WallTower`, `Alanthor_WallGate`, `Alanthor_Tower`)

> **Superseded by [task-wall-system-bfme2-rework-109](../../.deft/tasks/task-wall-system-bfme2-rework-109/task.md) (2026-05-21).** The four flat "build a Wall / Wall Tower / Wall Gate / Watch Tower" entries previously listed here are now organised under the canonical BFME2 hub-and-segment model. The **builder catalog** for Alanthor exposes only **two** of these directly: `Alanthor_Wall` (the **Wall Hub**) and `Alanthor_Tower` (the **Watch Tower**). `Alanthor_WallTower` and `Alanthor_WallGate` are **conversion-only**, never directly placeable — they are obtained by converting an existing wall instance / segment from its action panel. See the full spec in [§ Wall System (BFME2 hub-and-segment)](#wall-system-bfme2-hub-and-segment).

### Watch Tower — `Alanthor_Tower` (canonical stat block)

| Stat | Value |
|------|-------|
| HP | **250** |
| LoS | **28** (longest in the Alanthor roster) |
| Defense (M/R/S/Mg) | 2 / 3 / 0 / 0 |
| Garrison slots / arrow-fire | 4 / yes |
| Build cost (fresh build via builder catalog) | 140 S + 70 I |
| Build cost (via hut conversion — see Gatherer's Hut) | **40 S + 30 I** |
| Conversion timer (from hut) | **5 s** |
| Role | Stand-alone defensive tower (not anchored to a wall). The "field of view / arrow coverage" leg of the hut age-up choice. |

> The earlier 950 HP value for Watch Tower is **rejected** — it overlapped the Wall Hub's defensive role and made towers tankier than walls. Watch Towers are now **soft long-range eyes** (28 LoS, 250 HP) — fragile but far-seeing.

### Siege Yard — `Alanthor_SiegeYard`

| Stat | Value |
|------|-------|
| HP | 1 300 |
| LoS | 20 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 260 S + 100 I + 60 C |
| Trains | Alanthor_Ballista, Alanthor_BatteringRam, **Alanthor_Catapult**, Alanthor_Trebuchet *(all four — settled 2026-08-27; the 2026-08-02 "Catapult replaced Ballista" swap is reversed, they now coexist as opposite halves of the tier: the Ballista is the flat single-target +30 vs Building bolt, the Catapult is the lobbed AOE r3 anti-INFANTRY stone)* |

### Smelter (Forge) — `Alanthor_Smelter`

> Reworked by directive 2026-07-04: the Forge no longer converts iron +
> veilstone (the miner supply chain was removed). It **passively generates
> Veilsteel** — 1 Veilsteel / 10 s, no inputs — is **much more expensive**,
> and is **build-limited to 1 per faction**. It is the slow, infinite
> complement to mining the Veilsteel Mine map node.

| Stat | Value |
|------|-------|
| HP | 1 200 |
| LoS | 18 |
| Defense | 1 / 1 / 0 / 0 |
| Build cost | 800 S + 400 I + 100 Veilstone |
| Build limit | **1 per faction** |
| Role | Passive Veilsteel generation (1 / 10 s). |

### Crucible — `Alanthor_Crucible`

| Stat | Value |
|------|-------|
| HP | 1 200 |
| LoS | 18 |
| Defense | 1 / 1 / 0 / 0 |
| Loss factor on craft | 20 % |
| Build cost | 300 S + 80 Veilstone + 30 Veilsteel ⚠ |
| Role | **Veilsteel GENERATOR (2026-08-04):** each completed Crucible passively produces **1 Veilsteel per 10 s**. **Hard cap: 5 per player** (enforced at placement, players and AI alike). Building Crucibles is THE veilsteel engine — the AI builds toward the cap in its Age-2 ladder. |

> ⚠ The 30 Veilsteel build cost in [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs) creates a
> **chicken-and-egg problem** — you need a Crucible to forge Veilsteel,
> but you need Veilsteel to build a Crucible. Likely a code bug.
> TechTree.json's 200 S + 60 I + 40 C cost makes more sense for the first
> Crucible. Flag for fixing before this rule lands.

---

## Wall System (BFME2 hub-and-segment)

> Canonicalised by [task-wall-system-bfme2-rework-109](../../.deft/tasks/task-wall-system-bfme2-rework-109/task.md) (2026-05-21). This is the **single canonical truth source** for the Alanthor wall mechanic. The previous "wall-segment anchor that auto-fortifies a small radius" and the per-instance "build a Wall / build a Wall Gate / build a Wall Tower" listings are retired (see the [Gatherer's Hut](#gatherers-hut-age-0-carryover--per-hut-age-up-choice-wall-hub-or-watch-tower) and [Wall primitives](#wall-primitives-alanthor_wall-alanthor_walltower-alanthor_wallgate-alanthor_tower) sections above for the superseded callouts).

### Overview

Alanthor walls follow the **Battle for Middle-earth II model**: the player
places **Wall Hubs** (the only directly-buildable wall primitive), and the
game **auto-forms segments** between any two friendly completed hubs whose
world-space distance is below `MaxAutoSegmentDistance`. A segment is
composed of N **wall instances** at fixed 2 m spacing (one tile each).
**Gates** are obtained by **converting a segment** in-place: the conversion
swaps 5 contiguous instances of the segment to gate cells (or all of them
if the segment is shorter than 5). **Watch Towers** are a separate
primitive — they can be built directly via the builder catalog, **or**
obtained per-hut at age-up via the Gatherer's Hut conversion choice.

This is the player-facing trade triangle:

| Primitive | Built how | Role |
|-----------|-----------|------|
| **Wall Hub** | Directly via builder (catalog id `Alanthor_Wall`) OR via Gatherer's Hut conversion at age-up. Auto-forms segments to nearby completed hubs. | Compartment corner / wall anchor / boundary node. |
| **Wall Segment** | **Auto-spawned** between two hubs within `MaxAutoSegmentDistance`. **Never directly placed.** | Composite entity owning the chain of instances; selectable for Convert-to-Gate / Convert-to-Tower. |
| **Wall Instance** | **Auto-spawned** as part of a segment, one per 2 m. **Never directly placed.** | The individual 1×1-tile wall piece. Carries its own HP and presentation. |
| **Wall Gate** | **Conversion-only** from a segment's action panel (5 contiguous instances become gate cells). **Never directly placed.** | Auto-passable for owning faction; auto-closed otherwise. |
| **Wall Tower** | **Conversion-only** from a single wall instance's action panel. **Never directly placed.** | Ranged anti-infantry on a wall — taller LoS, garrison-fire. |
| **Watch Tower** | Directly via builder (catalog id `Alanthor_Tower`) OR via Gatherer's Hut conversion at age-up. | Stand-alone, off-wall variant of the wall-tower idea. |

> **Builder catalog contract.** Alanthor's Era-2 builder palette exposes
> **only** `Alanthor_Wall` (rendered as "Wall Hub" with glyph `castle`)
> and `Alanthor_Tower` (rendered as "Watch Tower" with glyph `eye`) as
> wall-related primitives. `Alanthor_WallTower` and `Alanthor_WallGate`
> are deliberately **omitted** from `BuildableBuildings` — a boot-time
> assertion verifies this in code.

### Drawing walls (2026-09-18)

> **Canonical for HOW the player places walls.** The hub-and-segment
> topology above is unchanged — this is the input on top of it. Supersedes
> the one-click-per-hub placement (`SpawnFirstWallHub`) and the per-hub
> "Build Wall" click (`SpawnExtendedWallHub`) as the player's way of laying
> a wall; both survive as the degenerate case of a drawn path with one hub.

A wall is **drawn, not clicked**. With the Wall Hub selected in the builder
palette:

| Step | What happens |
|---|---|
| **Press** on the ground | the path starts there — or, within the hub snap radius of a friendly hub, *at that hub* (so a wall can be extended from the per-hub Build Wall action the same way) — or, within one wall section of a friendly **wall cell**, *at that cell*, which **becomes a hub** when the order lands (see § Branching walls) |
| **Drag** | the path follows the cursor as a curve. Hubs go up at the stroke's **two ends** — and, once the stroke passes the run cap, at **evenly spaced points between**, so everything between two hubs is ONE continuous swept wall |
| **Turn too sharply** | the curve cannot bend tighter than a **12 m radius**: it follows the cursor at maximum curvature and catches up when the cursor is ahead of it again. A wall of 3 m modules and 6 m hubs has no business kinking |
| **Retrace** over the path | **backtracking** — moving the cursor back onto an earlier part of the path erases everything drawn after that point |
| **Release** | every hub and the segments between them are placed as **one order**; a press-and-release with no drag places a single hub as before. The **end snaps** the same way the start does: onto a friendly hub (closing a loop or joining an older wall) or onto a friendly wall cell, which becomes a hub — a T-junction into a standing wall. The preview line runs on to whatever it snapped to |
| Right-click / Esc | cancels the drawing |

Hubs snap to the 2 m build grid as every building does. Each hub ghost is
green or red on the usual placement rules (footprint free, own territory);
a path with any red hub is refused on release, as is one the bank cannot
pay for (hub cost × hubs — segments stay free). The path is capped at
**24 hubs**.

**One mesh, invisible cells.** Between two hubs the presentation sweeps a
single `WallCurveMesh` along the drawn curve — plinth, body, coping and a
crenellated crown, terrain-following, re-clad per wall level. The sim still
works in **modules**: invisible cells every 3 m along the same arc carry the
HP, the passability and the gate / tower / hub conversions, and a dead
cell's span is simply left open by the mesh, so a breach shows as a breach.
Presentation ids: `555` the swept segment, `556` a cell's pick collider.

**The run cap: 11 modules.** A single wall run is capped at **11 modules
(33 m)**. A longer stroke is cut into the fewest runs that all fit under the
cap, with the hubs at **equal arc intervals** — so a stroke needing one extra
hub gets it exactly at the midpoint, two get them at the thirds, and a drawn
wall is symmetrical rather than "full runs plus a stub at the end". The cap
lives in `WallDrawTool.asset` as `maxModulesPerSegment`.

**Why not a hub every few modules.** Intermediate hubs at one module's
spacing left a single 3 m module per chord: the wall read as a row of towers
with trim between them, which is the opposite of a curtain wall. Hubs are
structure, not decoration — they appear when a run would otherwise be too
long to be one piece.

**Lockstep.** The whole path rides one `PlaceWallPath` command (positions
in the command's string field, 2 dp), executed hub → segment → hub on
every peer through the same `PlaceWallHubDirect` / `WallExtendDirect`
executors a click uses, so a drawn wall cannot desync where a clicked one
would not. The AI wall planner is unchanged — it still places hubs.
(`PlaceWallPath` packs the FACTION in `EntityNetworkId`, not an entity:
until 2026-09-21 it was missing from `LockstepManager`'s skip list, so the
entity lookup failed and every drawn wall was dropped on remote peers.)

Numbers live in `WallDrawTool.asset` beside `WallDrawTool.cs`
(`Assets/GameSystems/Presentation/UI/World/`). Test scenario: **Wall
Drawing** (Scenarios menu) — an Alanthor Age 1 Hall, three builders, a
full bank, open ground.

### Walkable Ramparts, Doors & Garrison (2026-05-29 rework)

> **This subsection is canonical and supersedes the *geometry* and
> *walkability* of the thin-wall model below.** Walls are no longer a
> 1-tile-thick impassable line: they are **battalion-wide walkable
> ramparts** that owned units can march along, climb via hub/tower/gate
> doors, and garrison. The **hub-and-segment topology, HP, cascade, gate
> open/close, compartment-economy, and conversion rules are unchanged** —
> only the dimensions and the new "on-wall" movement layer are added here.
> All numbers are **playtest placeholders**.

#### Dimensions

| Property | Old (thin wall) | **New (rampart)** | Notes |
|----------|-----------------|-------------------|-------|
| Deck **walkable width** (across the wall) | ~0.8 m | **8 m** | Battalion corridor is 7 m (`BattalionAgentRadius` 3.5 m); 8 m leaves clearance for the outer garrison rank inside the parapets. |
| Total footprint width (incl. parapets) | ~1.1 m | **~9 m** | 8 m deck + ~0.5 m parapet each side. Widens the passability/obstacle footprint → re-check placement spacing. |
| Module length (along the wall) | 2 m | **4 m** | Fewer, longer modules ("segments longer and wider"). `AlanthorWall.InstanceSpacing` 2 m → 4 m. |
| Deck surface height (where units stand) | n/a | **4 m** | **Symmetrical** crenellated parapets to ~5.4 m on **both** edges — no inner/outer face. |
| Hub footprint | ~2.4 m drum | **~9 m** square w/ doors | Must be ≥ deck width so the deck lands flush on the hub. |
| Access | n/a | **doors** (ground + deck) | On hubs / towers / gates only; see Doors below. Plain instances have no access. |

#### Walkability & the navmesh

- The **deck top is baked as a walkable navmesh surface at y≈4**, instead of
  the current "every wall piece is an obstacle box" treatment in
  `NavMeshManager`. The ground navmesh is unchanged.
- The deck is its **own navmesh island**: vertical faces are cliffs (rejected
  by the slope budget), so enemies can't walk up the side and there is **no
  foot route from the ground to the deck except through a door**.

#### Getting on / off the wall — doors (2026-05-30)

> **Supersedes the earlier navmesh-ramp idea.** A walkable ramp could not be
> made to reliably connect the ground and deck navmesh islands — units
> bee-lined into the wall base instead of climbing — so access is now via
> **doors** with a deterministic teleport bridging the two islands.

- **Hubs, wall towers, and wall gates each have two doors:** a **ground door**
  on the inner face and a **deck door** on the deck above it. Plain wall
  instances have no door (no access point).
- **Ascend on order.** A move order whose destination is on a deck routes the
  unit (ground navmesh) to the nearest structure's **ground door**; on arrival
  it **emerges from that structure's deck door** and continues along the deck.
- **Descend on order.** A move order to the ground routes an on-wall unit to
  the nearest **deck door**; it emerges at the **ground door** and proceeds.
- The ground↔deck transition is a **deterministic teleport** through the
  structure (lockstep-safe: structures + units visited in entity-id order).
  `WallDoorAccessSystem` owns this.
- On the deck, `MovementSystem` **follows the navmesh path's Y** instead of
  snapping to terrain, so units stand at deck height (deterministic sampling).
- Hostiles never gain wall access in v1 (gates closed; no enemy door routing).

> **Optional, not yet built: buildable access ramps.** A separate ramp
> *building* placeable only when snapped to a wall, giving an easy on-foot
> route to the deck (BFME2 fortress-wall style) as an alternative to doors.

#### Garrison ranks

- On reaching the deck, a battalion **forms ranks along the *outer* parapet
  edge**, facing outward, so it reads as manning the wall.
- Rank spacing ≈ 1.2 m; the battalion fills the outer rank first along the
  deck, then steps inward by rank as needed to fit. Placement is computed
  **deterministically** from the segment's local axis + the unit count.
- Leaving (a ground move order) dissolves the garrison formation and routes
  units down.

#### Implementation pointers

| Concern | File |
|---------|------|
| Instance/hub/gate footprint + module length | [`AlanthorWall.cs`](../../Assets/Scripts/Entities/Buildings/AlanthorWall.cs) (`InstanceSpacing`, `BuildingSize`, hub footprint) |
| Deck + symmetrical parapets + door geometry | [`PresentationSpawnSystem.Walls.cs`](../../Assets/GameData/TechTree/Age0/Buildings/Wall/PresentationSpawnSystem.Walls.cs) |
| Deck baked **walkable** as its own island (no ramp sources) | [`NavMeshManager.cs`](../../Assets/Scripts/Systems/Movement/NavMeshManager.cs) |
| Door routing + ground↔deck teleport | [`WallDoorAccessSystem.cs`](../../Assets/Scripts/Systems/Movement/WallDoorAccessSystem.cs) |
| On-deck path-Y following | [`MovementSystem.cs`](../../Assets/Scripts/Systems/Movement/MovementSystem.cs) |
| Garrison-rank placement | [`WallGarrisonSystem.cs`](../../Assets/Scripts/Systems/Buildings/WallGarrisonSystem.cs) |

> **Verification is editor-only.** Geometry (deck width, door placement,
> parapet height), the deck navmesh island, the door teleport (units reach
> the deck via a door and not the open face), and on-wall steering all require Unity
> play-testing — they cannot be validated from source review.

### Wall levels, the gate structure and emplacements (2026-09-21)

> **Canonical for wall TIERS, the gate as a structure, wall garrison and the
> two emplacement buildings.** Supersedes, in the sections below: the Wall
> Hub's `3 x 3 cells (6 x 6 m)` footprint, the "Gate (5-instance composite)"
> section in full (a gate is now ONE entity, not a tagged run of cells), and
> the "no manual open/close in v1" line in it. Everything else about the
> hub-and-segment topology and § Drawing walls stands.

#### The three wall levels

A wall is not one building that gets tougher — it is three visually
distinct walls, and which one a faction raises is decided by its research,
not by the placement. Every hub, curtain and gate the faction owns is
**re-clad the moment the tech lands**; there is no per-wall upgrade click
and nothing to re-place.

| Lv | Player-facing name | Reached by | Look | Towers? | Curtain HP | Hub HP |
|----|--------------------|-----------|------|---------|-----------:|-------:|
| **1** | **Wooden Wall** | **every culture, from Age 0** — it is simply the wall you can build | Split logs driven into a timber sill, sharpened tops, a lashed walk-rail. Timber hub with a conical shingle roof. | **no** | 200 | 600 |
| **2** | **Stone Wall** | **the Alanthor age-up itself.** Choosing Alanthor re-clads every wall you own, free and at once. There is no tech to buy | Rough-coursed limestone, a coping ledge and the merlon crown. | yes | 320 (×1.6) | 960 |
| **3** | **Reinforced Wall** | **Alanthor only** — `ShieldedRamparts` at the Hall | Level 2 plus an **iron band** at the coping and **large metallic shields hung along the outer face**, one per module, under the merlons. Each curtain module carries **two garrison slots**. | yes | 460 (×2.3) | 1 380 |

**The name follows the level, the id does not.** Everything — the build
button, the selection panel, the tooltips — calls it a **Wooden Wall**, a
**Stone Wall** or a **Reinforced Wall**. The internal id stays
`Alanthor_Wall` (renaming it ripples through the recipe table, footprints,
costs, build times, the name resolver and the AI), but nothing a player sees
ever says "Alanthor Wall" for a fence a Runai player put up in Age 0.

**A palisade carries no tower.** Timber will not hold one, so the
Convert-to-Tower card is absent at level 1 and the panel says why. Gates and
emplacements are fine on timber: a barred gate is a gate, and an engine sits
on a platform, not on masonry.

**The palisade is an Age 0 building.** Walls stop being an Alanthor-only
toy: any faction can fence its start region in from the first minute, at
the Age 0 wall cost. What Alanthor keeps is everything above level 1 — the
stone, the shields and the garrison slots.

**Level 2 is not bought.** Committing to Alanthor at age-up IS the stone
upgrade: `AgeUpSystem` promotes the faction's whole wall the moment the
culture lands. The only wall tech is the last step, on the **Hall**
(`Age0/Buildings/Hall/Research/`), Alanthor-gated, and it has a slot in the
Hall's authored 3 × 5 action grid (`BuildingActionLayouts["Hall"]`) — a tech
with no slot there never appears, whatever its `researchAt` says:

| Tech | Cost | Time | Effect |
|------|------|------|--------|
| `ShieldedRamparts` | 400 S + 250 I + 60 V | 60 s | level 3: HP ×2.3 over a palisade, shields, **2 garrison slots per curtain module** |

A wall raised **after** the promotion starts at that level; HP is read from
the tier at creation (its own SO's `hp` × the tier
multiplier), never patched afterwards. A wall raised **before** it is
promoted in place (HP scaled proportionally, so a breached wall stays
breached, `WallTier` bumped, visual respawned), so a faction's wall is never
a patchwork of levels.

#### The wall's art (2026-09-21)

The wall follows the same shape every other building does: **one folder, with
the FBX, the SO and the prefab in it** —
`Assets/GameData/TechTree/Age0/Buildings/Wall/`. It is an **Age 0** folder,
not an Alanthor one, because the wall's first level is every culture's
(docs/Design/Age_0.md § Wooden Wall); what stays Alanthor's is the level, not
the building. `Gate/` and `Tower/` sit inside it — they are conversions of a
wall module, not buildings of their own.

**Every wall PIECE has its own SO**, because every piece has its own stats
and its own art. They were one `BuildingDefSO` with the curtain's numbers
bolted onto the hub's as `segmentHp` / `segmentLineOfSight`, which left the
curtain with nowhere to put a defence block, a radius, a line of sight or a
prefab of its own. Split 2026-09-21:

| SO | Id | Pid | Is | Level-1 HP |
|----|----|----:|----|-----------:|
| `Hub/WallHub.asset` | `Alanthor_Wall` | 550 | the hub — the only directly placeable wall piece | 600 |
| `Segment/WallSegment.asset` | `Alanthor_WallSegment` | 552 | one 3 m curtain module | 200 |
| `Tower/WallTower.asset` | `Alanthor_WallTower` | 553 | a module converted to a ranged tower | 900 |
| `Gate/WallGate.asset` | `Alanthor_WallGate` | 554 | the gatehouse, three modules wide | 500 |

Each carries its own `prefab` + `presentationId`, so each piece's art binds
independently and a piece with no art keeps its procedural visual. The level
multipliers in § The three wall levels apply to whichever def the piece reads,
so promoting a wall rescales each piece off its own number. `Wall.asset`'s
`segmentHp` and `segmentLineOfSight` are **retired** — zeroed, and read by
nothing.

Art files sit beside the SO that owns them: the model, its prefab and its
baked textures in the piece's folder.

**Textures do not travel in the FBX, and must not.** Blender writes a texture
link into an FBX only from one exact node setup, and re-exporting the mesh
wipes Unity's importer settings every time — a pipeline that breaks whenever
the artist touches the model. Instead the FBX carries **geometry, UVs and
material NAMES**, and each name is remapped to a `.mat` asset in this folder:

| FBX material | Unity material | Carries |
|---|---|---|
| `Wooden_Stakes` | `Wooden_Stakes.mat` | the wall's timber — baked albedo in `_BaseMap`, normal in `_BumpMap` |
| `Player_cloth` | `Player_cloth.mat` | the ownership cloth. Base colour stays **white**: the player's colour is applied on top, and a tinted base would colour it twice |
| `GroundMaterial` | `GroundMaterial.mat` | the foundation the wall stands on |

Baked maps are dropped into those three materials and stay there. Re-exporting
the mesh cannot disturb them, because the link is by name.

**The folder binds the art.** Each piece owns a folder — `Hub/`, `Segment/`,
`Gate/`, `Tower/` — holding its SO, its model, its prefab and its textures.
The binder reads the folder to know which piece a model is, falling back to
the file name (`*_hub`, `*_segment`, `*_gate`, `*_tower`) for a model sitting
loose. Dropping gate art into `Gate/` and re-running is the whole of adding it.

**Step-by-step for the artist** — baking the procedural materials, the exact
FBX export settings and what to do on the Unity side:
[docs/Art_Pipeline_FBX.md](../Art_Pipeline_FBX.md).

**Binding.** Run **`Waning Border > Walls > Bind Wall Art`** once after adding
or replacing the FBX. It also re-applies the importer settings and the
material remap, both of which a Blender re-export resets. It sets the importer flags the runtime needs (Read/Write
on — the combine reads vertices — and no cameras, lights or rig), builds
`Wall.prefab` with the Blender export's default camera, lamp and ground plane
dropped, and writes it into `Wall.asset`. A prefab's internal object ids only
exist after Unity has imported the model, which is why this one step needs the
Editor rather than being a file on disk.

**How it is drawn.** Still **one mesh per segment** — that has not changed and
must not. What changed is where the geometry comes from:

| | Source | Path |
|---|--------|------|
| prefab bound | the authored module, baked along the curve, one copy per sim cell | `WallArtMesh` |
| nothing bound | the procedural cross-section, swept along the curve | `WallCurveMesh` |

`WallModuleArt` prepares the module once: it measures what the FBX actually
contains, turns its long horizontal axis to run along the wall, puts its base
at ground level and **fits its length to the 3 m sim pitch**. So the art does
not have to be authored to the game's numbers — it is fitted to them, and a
re-export at a different scale keeps working. Copies butt end to end because
the pitch is stretched to divide the run exactly.

**Ownership.** EVERY wall piece takes the owner's colour on its ownership
parts — procedural prims named `Stripe_*` and authored models alike — through
`WallModuleArt.ApplyOwnerColor`, called on each piece as it spawns. Walls
deliberately do NOT go through `BuildingFactionColorMarker`: that path uses
`renderer.materials`, which clones a material per renderer, and a finished
perimeter is hundreds of pieces — the exact batching collapse the wall's
procedural builders were cleaned up to stop causing. The tint rides a
MaterialPropertyBlock instead.

The module's blue posts are the owner's colour, not blue: the
binder renames them `Stripe_Ownership_*`, which is the project-wide rule
`BuildingFactionColorMarker` already follows, and the baked wall finds the
same parts (by that name, or by a saturated-blue material) and tints their
sub-mesh per renderer. One shared material, every faction's walls, and the
single-draw-call batching the one-mesh wall exists for is untouched. To move
the marker to a different part of the model, either name it `Stripe_*` or
give it the saturated blue — no code changes either way.

Hubs, gates and towers draw their authored prefab when one is bound and fall
back to their procedural builder when it is not, so the set can be replaced
one piece at a time. Only the curtain module has art today. The emplacements
are still procedural throughout.

#### What a module may become, and where

A wall is a wall, not a rack for fittings. Every conversion — tower, gate,
ballista, trebuchet — needs **a clear run of untouched modules around the one
you clicked**, and the rule is checked twice: the action panel hides the card
and says why, and the executor re-checks it on every peer before it spends.

| Fitting | Needs a clear run of | Why |
|---------|---------------------:|-----|
| **Tower** | **3** modules (one clear either side) | plus masonry: level 2+ only |
| **Ballista emplacement** | **3** modules | any wall level |
| **Trebuchet emplacement** | **3** modules | any wall level |
| **Gate** | **4** modules | the gatehouse eats three of them; the fourth keeps it off the next fitting |

"Clear" means alive, finished, and not already a gate, a tower, an
emplacement or mid-conversion. **A dead module is not clear either** — a
breach is not building space, so you cannot hang a tower on the edge of a
hole. `AlanthorWall.FreeRunAround` walks the segment both ways from the
clicked module and returns the run's length; that single number is the whole
rule.

#### Garrison slots (level 3 only)

A reinforced curtain module holds **two** infantry or archers. Right-click
a reinforced wall with foot units selected and the nearest module with a
free slot takes them; they disappear into the wall and appear as manned
shield positions on its crown.

| Rule | Value |
|------|-------|
| Who may garrison | any **foot** unit — infantry or archers. Not cavalry, not siege, not workers, not heroes |
| Slots | **2 per curtain module**, level 3 only. Hubs and gates take none |
| What it gives | the module gains a ranged attack: **range 18 m**, damage = the sum of its occupants' own damage, one target per occupant, 2.0 s cooldown. LoS rises to 14 |
| Ungarrison | the module's action panel has an **Empty** card; the occupants step back out beside it |
| Module dies | its occupants die with it. A wall that falls takes the men on it |

#### The gate is one structure, three modules wide

A gate is **no longer a run of tagged cells**. Converting a wall replaces
**three contiguous modules with a single gate entity**: the module the
player converted becomes the gatehouse, and **the module on each side is
destroyed** — the gatehouse's own masonry occupies that ground. The curtain
mesh leaves the whole 9 m span open so nothing is drawn through it.

| Property | Value |
|----------|-------|
| Width | **9 m** = 3 curtain modules (`AlanthorWall.GateRegionSpan`) |
| Entity count | **one** — with `WallGateTag`, a 1 × 9 m footprint and its own Health |
| HP | 3 × the curtain module's HP at the faction's wall level (600 at level 1, 960 at 2, 1 380 at 3) |
| Cost / timer | **80 S** flat / **8 s**, unchanged |
| Short segment | a segment with fewer than 3 live modules converts the modules it has; the gatehouse is built to the span it actually got |
| Doors | **two leaves that swing**, driven by the gate's open state — 1.2 s to swing, always drawn, never teleported |
| Opening | the centre third (≈3 m). The flanking thirds are solid masonry |

#### Opening and closing it

The gate's default is what it always was: it opens when a friendly comes
within 6 m and closes behind them, and it never opens for a hostile. What
is new is that the player can override it.

| Mode | Behaviour | UI |
|------|-----------|-----|
| **Auto** (default) | opens for friendlies within 6 m, closes when they leave | the gate's action panel shows **Close Gate** |
| **Sealed** | stays shut, *including to friendlies* — the garrison locks its own people out | the panel shows **Open Gate**; the doors close on the spot |

The toggle is one order (`SetGateLock`, lockstep type 44) so both peers
flip the same gate on the same tick. Sealing does not make the gate tougher
— it is a pathing decision, not a defensive one: it stops your own army
from routing through a gate you would rather keep shut.

#### Ballista and Trebuchet emplacements

Two engines, in both cases **the emplacement and the engine are separate
entities**: the emplacement is the platform (the thing the player builds,
the thing that is repaired, the thing that holds the ground) and the engine
is a unit standing on it that **never moves**.

They are placed **two ways**, and the pair works identically in both:

- **On a wall** — the primary route. Select a curtain module and its action
  panel offers **Mount Ballista** / **Mount Trebuchet**, subject to the clear
  run above. The module stays wall (its HP, its footprint, its place in the
  segment); it gains corbels out to a planked fighting deck, a mantlet and
  the engine's pintle ring, and 50 % more HP to carry the weight. A module
  with an engine on it takes no garrison — the crew needs the whole crown.
- **On the ground** — a free-standing platform placed like any building, for
  a battery that is not part of a wall line.

`EmplacementCrewSystem` does not care which: it sees a finished platform with
no engine and raises one.

| | **Ballista Emplacement** | **Trebuchet Emplacement** |
|---|---|---|
| Id | `Alanthor_BallistaEmplacement` | `Alanthor_TrebuchetEmplacement` |
| Cost | 140 S + 80 I | 260 S + 140 I + 40 V |
| Build time | 35 s | 55 s |
| Footprint | 4 × 4 m | 6 × 6 m |
| Platform HP | 500 | 700 |
| Engine | `Alanthor_EmplacedBallista` — 260 HP, 46 dmg, 3.5 s, range 8–26 | `Alanthor_EmplacedTrebuchet` — 320 HP, 120 dmg, 8 s, range 14–48, 4 m splash |
| Against | single targets, **+30 vs Building** | massed infantry and siege lines, **+45 vs Building** |
| Movement | **none.** No move speed, no destination, no move order will take it | same |
| Crew | if the engine is destroyed the platform rebuilds it in **45 s**, free. Killing the emplacement kills the engine with it | same, 60 s |
| Where | on a curtain module (**Mount** on its action panel) or free-standing in own territory |
| Mounting cost / timer | the same cost as the free-standing platform, 12 s, no builder |

The engines are deliberately *stronger per shot and slower* than the mobile
Siege Yard versions and cost no population: an emplacement is ground you
have decided to hold, not an army you can move.

### Wall Hub

| Stat | Value |
|------|-------|
| HP | **400** |
| LoS | 10 |
| Defense (M/R/S/Mg) | 2 / 2 / 0 / 0 |
| Build cost (direct, via builder) | **60 S + 40 I** |
| Build cost (via hut conversion) | **60 S + 40 I** (same as direct) |
| Conversion timer (from hut) | **5 s** |
| Reach | **The curtain runs hub CENTRE to hub CENTRE** (`AlanthorWall.HubInset` = 0, 2026-09-21). A wall drawn from A to B runs from A to B, and the hub stands on top of that end. It used to start at the tower's rim, which made every hub a plug in a hole: right only while the hub's art was exactly as wide as the inset, and a hub's death exposed a hub-wide gap that read as "the wall beside it died too". |
| Footprint | **2 x 2 cells (4 x 4 m)** — [Build_Grid.md](Build_Grid.md) § 2. Shrunk 30 % on 2026-09-21: at 6 m the tower read as the wall's main event and the curtain as trim between towers. The hub is a **round tower** (domed in stone, shingle-roofed in timber), radius `AlanthorWall.HubRadius` = **0.7 wall sections (2.1 m)**; the curtain starts at the drum's rim (`HubInset` = the radius — the spacing is derived from the hub, never tuned on its own), and the selection ring is drawn at that radius. |
| Construction | Requires builders, standard `AssignBuildersToConstruction` flow (direct build only — conversion path is instant-paid + timer). |
| Hub-to-hub snap radius (`WallHubSnapDistance`) | **2 m** — placing a hub within 2 m of an existing hub reuses the existing hub instead of creating a degenerate overlapping pair. |
| Auto-segment range (`MaxAutoSegmentDistance`) | **16 m** (8 tiles) — see [§ Wall Segment](#wall-segment). |
| Role | The **only directly-placeable wall primitive**. Wall Hubs are the focal defensive structure: tankier than a tower, anchor for auto-formed segments, and the only way to seed a closed compartment. |

> **Why 400 HP?** Hubs are the anchor of any wall siege — killing one opens a 6 m breach where the tower stood (see § Walls fall in sections). 400 HP positions the hub as roughly 2× a Watch Tower in toughness, while still well below a Hall (~2 400 HP). Concrete value finalises after playtest.

### Wall Segment

A wall segment is an **ECS entity in its own right** that owns a chain of
wall-instance children. It carries no HP (its HP is the live sum of its
instances' HP — see [§ Health bar treatment](#health-bar-treatment-segments-and-gates)), but it is the
**selection target for the Convert-to-Gate action**.

| Property | Value |
|----------|-------|
| Spawn rule | Auto-formed by `WallAutoSegmentSystem` (polled at **0.5 s**) when two friendly **completed** hubs are within `MaxAutoSegmentDistance` (= 16 m) and **not already connected** (via the `WallHubLink` buffer guard). |
| Owning faction | Inherited from the two endpoint hubs (must match — auto-segments **never cross factions**). |
| Endpoint hubs | Stored in the segment's components. A hub's death does **not** take the segment with it — the segment stands on its own cells until the last of them dies (see § Walls fall in sections). |
| Selection behaviour | Clicking any **instance** of the segment resolves the selection to the **segment entity** (per [task-109 § E. Wall Network Selection](../../.deft/tasks/task-wall-system-bfme2-rework-109/task.md)). Clicking a **hub** selects the hub. |
| Visual | Composite — rendered as the sum of its individual instance presentations. The segment entity itself has no presentation prefab. |
| Determinism | Auto-formation iterates hub pairs in deterministic sort order (sort key: `(Entity.Index, Entity.Version)` of each endpoint) to keep lockstep clients in sync. |

### Wall Instance

A wall instance is the **2 m × 1 tile piece** that makes up a segment.
Each instance is its own ECS entity with its own Health and presentation
(presentation IDs 551 = wall, 553 = tower, 554 = gate).

| Stat | Value |
|------|-------|
| HP | **80** |
| LoS | 0 (inherits from the parent hub for selection-panel rollup; instances themselves do not see) |
| Defense (M/R/S/Mg) | 2 / 2 / 0 / 0 (inherited from the hub line) |
| Cost | **None — auto-spawned with the segment.** The player pays no per-instance cost; the gameplay cost is the hub placement itself. |
| Spacing | **2 m** (one tile) — fixed by `AlanthorWall.InstanceSpacing`. |
| Instance count per segment | `ceil((distance - 2 × HubInset) / 2)` — **linear with hub distance**, capped only by `MaxAutoSegmentDistance` (16 m → max 8 instances per segment). |
| Selection behaviour | Click resolves to **parent segment**. Drag-select selects the instance individually. |
| Conversion options | An instance can be **converted to a Wall Tower** individually (per-instance conversion), or **converted to a Hub** (per-instance, costs a hub, 10 s) — the cell becomes a hub and its segment **splits** there, so walls can be drawn off it (see § Branching walls). A run of 3 contiguous instances can be **converted to a Gate region** via the parent segment's action panel. |

### Gate (5-instance composite)

> **SUPERSEDED 2026-09-21** by § Wall levels, the gate structure and
> emplacements above: a gate is ONE entity three modules wide, the two
> modules beside the converted one are destroyed, the doors swing, and
> the player can seal it. The table below is the pre-2026-09-21 model
> and is kept only to explain `WallGateGroup` on old saves.

A gate is **not its own primitive** — it is a state applied to **5
contiguous wall instances** of a segment. Conversion swaps each
underlying instance's presentation from `551` (wall) to `554` (gate) and
flags it with `WallGateTag`. A shared `WallGateGroup` component links the
5 instances so they open and close in unison.

| Property | Value |
|----------|-------|
| Width | **5 instances = 10 m** — wide enough for a battalion-formation traversal. |
| HP (total) | **400** = 5 × 80 (computed from instances; gate is **not** a separate Health-bearing entity). |
| Cost (single conversion) | **80 S** *flat* — single payment, not per-instance. (Resolves Open Q3: prefer flat over 5× per-instance for cost-clarity. PLAYTEST PLACEHOLDER: revisit if the gate ends up too cheap relative to a fresh wall ring.) |
| Conversion timer | **8 s** (segment-level `WallSegmentUpgradeState`; matches the legacy per-instance gate timer from `EntityActionPanel.cs:1677`, kept canonical until playtest demands a change). |
| Conversion builder | **None required** — conversion is instant-paid + timer, same as the hut → hub / tower flow. |
| Short-segment behaviour | If the segment has **< 5 instances**, the gate becomes a **full-segment gate** (e.g. 3 instances → 3-cell gate). UI marks the card with an amber warning glyph: "Short segment — gate will span the full segment (N instances). Battalions wider than N may not fit." |
| Owner-faction passability | **Always-open** for the owning faction (gate auto-opens when a friendly unit enters `WallGatePassabilitySystem.RegionDetectRadius = 6.0 m`, auto-closes when no friendlies are inside). **No manual open/close in v1.** |
| Hostile passability | Hostiles cannot pass — the gate cells block pathing just like a wall instance when closed; the gate stays **closed for hostiles** regardless of approach distance. |
| Region detection | All 5 gate cells share the same open/closed state via the `WallGateGroup` leader pattern — approaching from either end opens the whole region. Legacy 1-instance gates (no `WallGateGroup`) continue with the original 3.0 m radius for backward compatibility. |
| Visual | 5 tiled `Alanthor_WallGate` (presentation 554) cells for now — a custom 5-cell wide-gate prefab can replace this later without spec churn (PLAYTEST PLACEHOLDER on visual identity). |

> **Gate state UX in v1.** Gates are **always-passable for the owner**
> when a friendly approaches; **always-closed otherwise.** There is **no
> manual toggle button** in the action panel — the selection panel shows
> a read-only `OPEN` / `CLOSED` pip in the eyebrow row. Manual toggle is
> deferred (see [§ Open Items / Playtest Placeholders](#open-items--playtest-placeholders)).

### Wall Tower

A wall tower is **not its own primitive** — it is a state applied to a
**single wall instance** that converts it into a ranged-attack tower
sitting on top of the wall. (Unrelated to `Alanthor_Tower`, the stand-alone
Watch Tower — see [§ Watch Tower](#watch-tower) below.)

| Stat | Value |
|------|-------|
| HP | **500** (the converted instance jumps from 80 to 500 on completion) |
| LoS | 16 |
| Defense (M/R/S/Mg) | 2 / 3 / 0 / 0 |
| Conversion cost | **60 S + 30 I** (single instance) |
| Conversion timer | **10 s** (per-instance `WallUpgradeState`; matches legacy value, kept canonical) |
| Conversion builder | **None required.** |
| Visual | Presentation 553 (`Alanthor_WallTower`). |
| Source | Per-instance action panel — click a wall instance, the action panel surfaces a "Convert to Tower" card. |

### Watch Tower

Same entity as the **stand-alone Watch Tower** primitive — defined in full
in the [Watch Tower section](#watch-tower--alanthor_tower-canonical-stat-block) above. Key facts in the wall-system context:

- Built **directly via the builder catalog** (`Alanthor_Tower`, cost 140 S + 70 I) — the standard route.
- Built **via Gatherer's Hut conversion** at age-up (cost 40 S + 30 I, 5 s timer) — the discounted, hut-replacement route.
- **Independent of walls.** A Watch Tower placed adjacent to a Wall Hub does **not** auto-merge with the wall — it stays a free-standing structure with its own footprint.

### Gatherer's Hut Age-Up Choice (summary cross-link)

Each owned Gatherer's Hut surfaces a 2-button **Convert** prompt at
age-up (see [§ Gatherer's Hut](#gatherers-hut-age-0-carryover--per-hut-age-up-choice-wall-hub-or-watch-tower) above for the full spec):

| Choice | Cost | Timer | Result |
|--------|------|-------|--------|
| **Convert to Wall Hub** | 60 S + 40 I | 5 s | Hut → Wall Hub at the same footprint |
| **Convert to Watch Tower** | 40 S + 30 I | 5 s | Hut → Watch Tower at the same footprint |

If the player ignores the prompt, the hut continues generating Age-0
income indefinitely. No timeout, no auto-default.

### Walls fall in sections

> **Supersedes the hub-death cascade (2026-09-19).** Until now a Wall Hub's
> death cascade-destroyed every segment attached to it, and each cascaded
> segment destroyed all of its cells. With a drawn wall being ONE segment
> from its first hub to its last (`WallDrawTool.asset` `hubSpacing: 0`), a
> single hub kill erased the entire stroke — a 100 m wall vanished because
> one 400 HP bastion fell. That is retired.

Every piece of a wall dies **on its own HP and only itself**:

| Piece dies | What collapses |
|------------|----------------|
| **Wall cell** (a curtain module / curve cell) | That cell. The swept mesh opens a breach at its span; its neighbours stand. |
| **Wall Hub** | That hub. A 6 m breach opens where the tower stood. Every segment attached to it keeps standing on its own cells, ending at the hub's empty footprint. |
| **Wall Segment** | Only when its **last cell** dies — it is a graph edge with no HP of its own, and `WallSegmentCleanupSystem` retires it (and its hub links) once nothing is left standing on it. |

Rationale: a wall should have to be **breached**, and the breach should be
exactly as wide as what was knocked down. Sieges pick a section and grind it;
they do not one-shot a bastion to erase a perimeter. The hub is still the
worthwhile target — it is the widest breach for one kill and it stops
anchoring further wall — but it is no longer a hidden kill switch.

A segment whose hub has died keeps its `WallConnection` pointing at the dead
entity (every reader tolerates that — `em.Exists` guards). **Rebuilding the
hub repairs the line:** a same-faction hub built with the dead hub's centre
inside its footprint adopts every orphaned segment that ended there
(`AlanthorWall.AdoptOrphanedSegments`, run from the hub factory), so the
standing cells become its wall again and "Build Wall" / the AI's plan refill
does not lay a second segment of cells on top of them.

Compartment income no longer exists (removed 2026-07-06 with
`WallEnclosureIncomeSystem`) — the territorial consequence of losing wall
pieces is that the destroyed fortifications stop granting Alanthor influence,
and the area slowly decays back to neutral on the influence map.

### Branching walls (2026-09-19)

A wall can be joined from any point of any other wall, not only at its
hubs — that is what makes **T- and X-shaped** layouts possible.

**Any standing wall cell can become a hub.** Two ways in:

| How | What happens |
|-----|--------------|
| **Convert to Hub** on a selected wall cell | Costs a hub, 10 s timer (`WallUpgradeState` type 3). On completion the cell is replaced by a hub. |
| **Drawing a wall onto a wall** | With the Wall Hub tool, pressing within one wall section of a friendly cell starts the stroke *on that cell*; releasing within one section of a friendly cell ends it there. Each such cell is converted as part of the order (`WallPathKind.CellHub`, costed as a hub, instant — the wall it joins is already built) and the new wall attaches to the hub it became. Starting on a cell and dragging off it is an X or T; ending on a cell is a T into a standing wall. |

**The segment splits at the new hub** (`AlanthorWall.ConvertInstanceToHub`).
The cells before it stay on the original segment, which now ends at the new
hub; the cells after it move to a fresh segment from the new hub to the far
hub; the cell under the hub (and any other sitting mostly inside the drum)
is removed. Nothing is rebuilt — every other cell keeps its position, HP and
construction state — so the graph changes but the wall on the ground does
not. A drawn (curved) segment splits its curve at the cell, each half passing
through the hub, and the swept mesh follows: cells keep their place on the
curve by their stored position, not by an assumed even spacing.

Not convertible: a gate cell, a tower cell, a cell still under construction,
or a cell whose segment is mid-gate-conversion. The hub goes up finished
(there is no builder involved) with full hub HP; the wall it interrupts was
already standing.

**Snapping.** Wall placement snaps to hubs (6 m) and to wall cells (one
section, 3 m), at both ends of the stroke. The end point of the order is
the hub's or cell's own position, so what the preview line shows is what
every peer resolves.

### Auto-segment formation feedback

Auto-segment spawn is **silent by default** — no popup, no toast, no
minimap ping. A subtle **500 ms construction shimmer** plays along the
line between the two hubs (reuses the existing wall-instance spawn cue).
Audio: low-volume `construction_begin` SFX (reuses the existing
wall-instance spawn SFX hook). Both effects honour
`prefers-reduced-motion`: under reduced-motion, the instances pop in
without animation.

### Health bar treatment (segments and gates)

| Selection | Bar shown |
|-----------|-----------|
| **Hub** | Standard `FloatingHealthBars` Health bar (existing). |
| **Wall instance (individual)** | Standard per-instance world-space Health bar (existing). |
| **Wall segment (clicked via instance)** | Selection panel renders **one aggregated bar** — `sum(instance.Hp) / sum(instance.HpMax)` with sub-text `<aliveCount> / <totalCount> intact`. Bar palette: green ≥ 50 %, amber 20–50 %, red < 20 %. The per-instance world-space bars still render. |
| **Gate region (5 instances tagged WallGateTag)** | Same aggregated-bar treatment as a segment, label `Wall Gate`. No stacked-5-bar UI. |

### AI Behaviour

**Alanthor AI does not build walls in v1.** This is explicit:

- `SimpleAISystem` skips Wall Hubs / Wall Segments / Wall Instances in its build-target / repair-target / attack-target enumerators.
- The dead wall-building code in `AIEconomyManager.cs:627-756` (already `[DisableAutoCreation]`-orphaned) **stays dead** with a `task-109` comment marker so a future task can resurrect it.
- `AIAlanthorEndgameSystem` remains `[DisableAutoCreation]`.
- AI-owned Gatherer's Huts at age-up **do not** convert to Wall Hubs or Watch Towers — the AI keeps Age-0 gathering huts indefinitely.

AI wall-building (strategic placement around Halls / resources / chokes) is a **separate follow-up task** that depends on the primitives canonicalised here.

### Open Items / Playtest Placeholders

Concrete numeric values pinned in this section come from the
task-109 architecture pass (2026-05-21). The following are **PLAYTEST
PLACEHOLDERs** — the spec is committed to a default value, but designers
should revisit after first playtest:

- **Wall Hub HP = 400.** Default reasoned as "2× a Watch Tower (250), well below a Hall (2 400)". If hubs feel too fragile / too tanky in playtest, rebalance.
- **Gate conversion cost = 80 S flat.** Default reasoned as "single payment matches the player's mental model of one decisive structural change". If gates end up too cheap relative to building a fresh wall ring, switch to per-instance pricing (5 × 16 S = 80 S, or raise to 5 × 20 S = 100 S).
- **Wall instance HP = 80** (gate total HP therefore = 400). Default reasoned as "individual instance dies in ~3 catapult hits; the chain forms the resilience". If sieges are too quick, raise to 100 (gate total 500).
- **MaxAutoSegmentDistance = 16 m (8 instances).** Default reasoned as "long enough to wall a typical compartment in 2–3 hubs, short enough to force multiple hubs around a Hall". If players spam single-hub compartments, lower to 12 m.
- **Conversion timer (hut → hub / tower) = 5 s.** Default reasoned as "visible-but-not-painful". Tune if playtest reveals the wait feels off.
- **Conversion timer (segment → gate) = 8 s, (instance → tower) = 10 s.** Default kept from legacy code values. Tune as needed.
- **Watch Tower visual identity for the 5-cell gate.** Currently the gate tiles the existing single-gate prefab 5 times. A bespoke 5-cell wide-gate prefab is **out of scope for task-109**; lands in a follow-up.
- **Manual gate open/close toggle.** Deferred to v2 of the wall system; v1 is automatic-only. Selection panel shows a read-only `OPEN` / `CLOSED` pip.
- **Network-select affordance** ("double-click any wall piece → select the entire connected network"). Deferred; drag-select handles the common case.
- **Friendly right-click on owned wall** (repair / garrison / open-gate). **No-op in v1.** Re-evaluate if Phase 7 surfaces a unit-on-wall requirement.
- **Hover-preview cost overlay** on the Gate card. Tooltip cost chips suffice; **no** additional Resource-panel ghost-deduction in v1.
- **Hub-attention pulse persistence** (on huts carrying the `GathererHutAgeUpChoice` marker). **Stays forever** until the player commits or the hut dies — matches the "hut keeps generating Age-0 income indefinitely" rule.
- **Cancel mid-conversion** (hut → hub / tower, segment → gate, instance → tower). **No cancel in v1** — matches the existing structure-construction model.

### Cross-references

- **UI / UX spec (full):** [task-109 § UI / UX Specification](../../.deft/tasks/task-wall-system-bfme2-rework-109/task.md).
- **Code touchpoints:** `AlanthorWall.cs` (hub / segment / instance factories), `WallUpgradeSystem.cs` (conversion to gate / tower), `WallGatePassabilitySystem.cs` (friendly-detect open / close), `WallSegmentCleanupSystem.cs` (dead-segment cleanup — walls fall in sections, no hub cascade), `InfluenceMapSystem.cs` (fortification influence), `WallAutoSegmentSystem.cs` (Phase 4, new — retroactive auto-formation).
- **Wall-economy compartment yield** is unchanged from prior spec: `+8 Supplies per 10 u² closed compartment / min` via the `Alanthor_StoneLedgers` tech (PLAYTEST PLACEHOLDER, see [§ Cultured carryover buildings — KingsCourt techs](#existing-code-techs-that-need-re-homing-or-removal)).

---

## Alanthor units (full stat blocks from code)

### Alanthor Sentinel — heavy infantry

Trained at Garrison (Barracks).

| Field | Value |
|------|-------|
| Class | `human_melee` |
| HP | 160 |
| Speed | 5.0 |
| Training time | 18 s |
| Min building lvl | 2 |
| Armor type | infantry_heavy |
| Damage | 12 (melee) |
| Defense (M/R/S/Mg) | 3 / 2 / 0 / 1 |
| Attack range | 1.7 |
| LoS | 18 |
| Cost | 90 Supplies + 20 Veilsteel |
| Pop | 1 |

> Open: is this the Swordsman from the draft? Stats fit (defensive heavy
> melee with armor). Likely yes.

### Alanthor Crossbowman — heavy ranged

Trained at Practice Range, L2.

| Field | Value |
|------|-------|
| Class | `human_ranged` |
| HP | 100 |
| Speed | 5.0 |
| Training time | 22 s |
| Min building lvl | 2 |
| Armor type | ranged |
| Damage | 13 (ranged) |
| Defense | 0 / 2 / 0 / 0 |
| Attack range | 13 |
| Min attack range | 4 |
| LoS | 22 |
| Cost | 70 Supplies + 15 Veilsteel |
| Pop | 1 |

### Alanthor Cataphract — heavy cavalry

Trained at Garrison (Barracks), L2.

| Field | Value |
|------|-------|
| Class | `human_cavalry` |
| HP | **160** *(2026-08-04 rebalance — was 180)* |
| Speed | 6.6 |
| Training time | 40 s |
| Min building lvl | 1 (Royal Stable) |
| Armor type | cavalry |
| Damage | **18** (melee) *(was 20)* |
| Defense | 2 / 1 / 0 / 0 |
| Attack range | 1.6 |
| LoS | 20 |
| Cost | **320 Supplies + 120 Iron + 60 Veilstone** *(was 220/80/40 — the Outrider fills the cheap slot)* |
| Pop | 2 |

### Alanthor Catapult — siege

> **Replaced the Alanthor Ballista (2026-08-02).** Same battlefield slot,
> but a lobbed AOE stone-thrower instead of a piercing bolt-thrower.

Trained at Siege Yard.

| Field | Value |
|------|-------|
| Class | `machinery_siege` |
| HP | 220 |
| Speed | 3.2 |
| Armor type | ranged |
| Damage | 40 (siege), **AOE radius 3** on impact — shots do NOT pierce (that was the ballista bolt's trait) |
| Reload | **4 s** |
| Trajectory | High lob (longbow-family parabola) |
| Defense | 0 / 1 / 2 / 0 |
| Attack range | **30** |
| Min attack range | **10** |
| LoS | **20** — the catapult OUTRANGES its own vision: it auto-engages only inside 20, and firing at 20-30 needs a spotter (scout/ally vision) plus an explicit attack order. Artillery doctrine (2026-08-02). |
| Cost | 180 Supplies + 80 Iron + 40 Veilstone |
| Pop | 1 |
| Visual | Synty `SM_Wep_Catapult_01` with procedural arm release/re-wind; shots render as the `FX_Catapult_Single_01` effect. The shooter's launch angle is solved ballistically (speed + gravity from the authored FX, launch height and terrain height difference included) so the stone impacts the ground at the target's location. |

### Alanthor Ledger — court automaton (visual identity, 2026-08-02)

Mechanics unchanged (autonomous economy automaton, King's Court / Hall L2).
Visual identity: a **legless floating automaton** — it hovers on a
**forcefield disc** projected beneath it (tinted the owning player's color,
with a low synthesized hum). The open-frame torso is **full of cogwheels**
(spinning constantly), it has **four articulated arms**, and at its center
floats a **shining crystal in the player's color**, pulsing with its
machinery.

Rules (confirmed 2026-08-02):

- **Automate Facility**: +30 % yield for **30 s** on one economy building.
- **Per-building cycle: 90 s** — the 30 s boost is followed by a 60 s
  *Under Automation* lockout, so the same building can be re-automated
  exactly 90 s after the previous application. (Implemented as the
  Aftermath chain: boost Duration 30 + lockout Duration 60.)
- **One Ledger per player** — enforced at the training-command gate, same
  mechanism as King Lexor (live unit or queued order both count).
- Feedback VFX: automated buildings carry a golden rising-spark aura for
  the boost's 30 s; a larger golden burst plays at the building the moment
  the ability lands.

### Alanthor Scholar — religious / magic

Trained at **Temple of Ridan, L3** (per the Runai-review Q#17 fix — the
Temple caps at 3 levels, not 4; the old "L4" reference is a retired spec
stage).

| Field | Value |
|------|-------|
| Class | `human_magic` |
| HP | 90 |
| Speed | 3.0 |
| Training time | 30 s |
| Min building lvl | **3** *(Temple of Ridan L3)* |
| Armor type | ranged |
| Damage | 0 |
| Damage type | magic |
| Defense | 0 / 0 / 0 / 1 |
| LoS | 14 |
| Cost | **~300 Supplies + 150 Iron + 100 Veilstone + 30 Veilsteel** *(rebalanced to the cross-faction game-ender religious tier — see [Overview.md § Religious units](Overview.md#religious-units--cross-faction-game-ender-tier))* |
| Pop | 1 |
| Single unit / battalion | **Single** ([Overview.md § Unit granularity](Overview.md#unit-granularity--single-units-vs-battalions)) |
| Role | Channels **Purification** rituals on Active veilstone nodes — Alanthor's Glow-generator. Vulnerable to direct attack, needs escort. *(Note: L4 building level conflicts with the L1-L3 cap stated in this culture summary — the Temple/Shrine's "level 4" is the spec-refinement #5 stage, not a fourth upgrade tier in the building-upgrade system.)* |

---

## Alanthor-specific tech (code-existing)

| Tech | Researched at | Effect | Cost |
|------|---------------|--------|------|
| `Alanthor_StoneLedgers` | KingsCourt / Town Hall | +8 Supplies per 10u² closed compartment / min | 220 S + 40 I |
| `Alanthor_MasonGuild` | KingsCourt / Town Hall | +15 % building HP, +20 % repair rate | 180 S + 40 I |

> Both predate the draft's `Stone tools / Cranes / Masonry` ladder.
> `MasonGuild` overlaps the proposed `Masonry` tech — reconcile before
> implementing.

---

## Decisions (resolved 2026-05-19)

The original "Open design questions" pass was reviewed and answered. Each
decision is folded into the doc body above; this block is the **decision
record** so future readers can trace why a number or rule is the way it is.

1. **`KingsCourt` → `TownHall` rename** — **confirmed.** Queue code rename
   of tag, prefab, presentation id, and BuildCosts entries.
2. **Garrison roster** — **resolved.** Garrison trains a **3-tier line-
   infantry ladder** (Spearman → Swordsman → Royal Guard, L1 / L2 / L3) plus
   the **Sentinel** in parallel as a late-game damage-sponge / siege-melee
   unit. **Cataphract is moved out of Garrison** into a new **Royal Stable**
   building (see [§ Royal Stable](#royal-stable--cataphract-host-new)).
3. **Practice Range HP / pop** — **resolved.** Drop the over-tuned
   1 500 HP / 8 pop. Use the standard cultured-building multiplier path:
   660 / 690 / 720 HP, 0 pop.
4. **4-tier tech ladder semantics** — **resolved.** Researching a weapon
   / arrow / tool tier **unlocks** a per-battalion (or per-Worker)
   upgrade button; the upgrade is then **paid for per battalion** when
   applied. Same rule applies in all cultures. See
   [Overview.md § Per-battalion military upgrades](Overview.md#per-battalion-military-upgrades-cross-faction-rule).
5. **Glow source / drop rules** — **resolved.** Glow is now defined as a
   cross-faction resource produced only by Border node state
   changes (cleanse / convert / destroy, once per node). Drop conditions
   defined for unit / building death and the "drop Glow" UI button. See
   [Overview.md § The Glow economy](Overview.md#the-glow-economy-cross-faction).
6. **`Masonry` vs `Alanthor_MasonGuild`** — **resolved.** Canonical name
   is **Mason Guild**. The draft's `Masonry` is dropped; `Alanthor_MasonGuild`
   is renamed to `MasonGuild` and rebalanced to the +20 % HP figure.
7. **Practice Range ranged-weapon ladder** — **resolved.** Same 4-tier
   per-battalion upgrade pattern as Garrison: Stone-tipped → Iron-tipped
   → Veilstone-tipped → Glow-tipped arrows. Plus the original two
   passives (Choreographed volleys, Fletching).
8. **Wall economy yield** — **resolved to placeholder.** The `+8 supplies
   per 10u² closed compartment / min` figure from `Alanthor_StoneLedgers`
   stands as a placeholder; final value requires playtesting.
9. **Build-cost discrepancies** — **resolved.** [BuildCosts.cs](../../Assets/Scripts/Data/TechTree/BuildingCosts.cs)
   is the authoritative source (runtime-loaded). All cost rows above use
   BuildCosts values; TechTree.json needs to be updated to match.
   Exception: the Crucible's 30-Veilsteel build cost (chicken-and-egg)
   is flagged for fixing.

## Remaining open questions

- **Battalion sizes** for each Garrison / Practice Range / Royal Stable
  trainable (Spearman / Swordsman / Royal Guard / Sentinel / Archer /
  Crossbowman / L3 ranged apex / Cataphract / L2-L3 cavalry tiers). Stats
  currently in this doc are battalion totals but the headcount per
  battalion is TBD.
- **Royal Stable** numeric stats — HP base, build cost, defense, LoS —
  all marked TBD in the section above.
- **L2 / L3 Cataphract tiers** — does the Royal Stable train a single
  Cataphract unit at L1 only, or does it parallel the 3-tier ladder of
  Garrison / Practice Range with three cavalry tiers? If three tiers,
  what are the L2 and L3 names?
- **L3 ranged apex name** — placeholder "Longbowman" suggested. Confirm.
- **Crucible build cost** — fix the BuildCosts.cs chicken-and-egg
  Veilsteel requirement (likely should be 200 S + 60 I + 40 C, matching
  the TechTree.json value).
- **`Academy` tech effect** — currently TBD; design draft only listed the
  name.