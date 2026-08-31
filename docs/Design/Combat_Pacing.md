# Combat Pacing — the Meta Ladder & Counter System

Canonical source for **match pacing**: which unit compositions define each
phase of a match, which units counter which, and the wall-siege rule.
Numbers here are the truth source for the unit SOs' `bonusVsTags` data —
change them here first, then re-author the SOs.

The game has only two ages ([Overview.md](Overview.md) — Age 0 and the
cultured Age 1, progression via building levels L1-L3). The match still
moves through **five meta beats**; the later beats are keyed to building
levels and the endgame loop, not to further ages.

---

## The five meta beats

| Beat | Keyed to | Defining meta |
|------|----------|---------------|
| **0 — Skirmish** | Age 0 | **A melee age**: spearmen and workers. Ranged is an Age-1 unlock (2026-08-11 — the Age-0 archer rush was uncounterable and ended matches by minute 15), so the age is about spear lines, map reading, and the economy race to age-up. **The race is SHORT by design: median age-up lands at 3-6 minutes depending on difficulty (2026-08-29) — Age 0 is a prologue, not a third of the match.** |
| **1 — Lines** | Age-up, buildings L1-L2 | The bow arrives: archers and crossbowmen enter alongside swordsmen (the Archery Range unlocks at era 2) and beat the Age-0 comps. Walls appear: **walls keep you mostly safe — only siege units can attack wall pieces** (see The Wall Rule below). |
| **2 — Maneuver** | Buildings L2-L3 | Early cavalry wins every open encounter **except against spearmen**. Longbowmen (Range L3) rule the field, urging the advent of the cataphract. Early siege cracks hard targets and brings area damage (Trebuchet). |
| **3 — Game-enders** | L3 + veterancy / equipment tiers | Longbowmen and cataphracts answer almost everything. The triangle closes: **cavalry counters longbowmen; crossbowmen counter cataphracts; spearmen still counter cavalry**. |
| **4 — The Shardroot** | Endgame loop | The match revolves around escorting religious units to wells and holding the Shardroot — see [Curse_And_Shardroot.md](Curse_And_Shardroot.md). Armies exist to screen ritualists and break enemy holds. |

Pacing is therefore tuned through **building-upgrade timing and cost** — each
beat transition is a level gate, not a stat patch.

---

## Combat model (context)

Damage is AoE4-style (`CombatModifiers.CalculateFinalDamage`):

```
final = max(1, baseDamage - flatArmor) + bonusVsTags
```

The old damage-type x armor-type multiplier matrix is **retired** (UI
counter-hints only). All hard counters live in per-unit `bonusVsTags`
entries — flat bonus damage vs a target tag, added **after** armor and
ignoring it. Tags: Infantry, Cavalry, Ranged, Siege, Heavy, Light,
Building, Worker, Religious, Ship.

---

## Armor (canonical values)

Armor is **subtracted**, so a point of it is worth a fixed number of hit points
*per hit* — and therefore worth wildly different amounts depending on what is
hitting. 4 armor halves an 8-damage arrow and is a rounding error against a
60-damage trebuchet. That inversion is the whole design: it is what lets a
heavy unit genuinely counter light attacks without being good against
everything at once, and it is why these are authored against the attacks that
will actually land on them rather than picked as percentages.

The `Defense` component's doc comment claimed a diminishing-returns percentage
(`d / (d + 100)`) until 2026-08-28. That formula has not been in the game for a
long time; anything that reasoned about durability from it was wrong by an
order of magnitude, `UnitPower` included.

### Units

| Role | Melee | Ranged | Siege | Magic | Why |
|---|--:|--:|--:|--:|---|
| Worker / Ledger | 0 | 0 | 0 | 0-2 | Not meant to survive contact |
| Scout | 0 | 1 | 0 | 0 | Survives by not being there |
| Litharch / Scholar | 0 | 0-1 | 0 | 3-4 | Robes: the magic column is their only protection |
| Spearman | 1 | 1 | 0 | 0 | Cheap line infantry; its counter is the +15 vs Cavalry, not its armor |
| Swordsman | 4 | 2 | 0 | 1 | Mail. Takes 6 from a spear where a Spearman takes 9 |
| Nobleman | 5 | 3 | 0 | 2 | |
| Sentinel | 7 | 5 | 0 | 2 | The wall. An 8-damage Archer does 3 |
| Archer / Longbowman | 0 | 1 | 0 | 0 | Glass. 0 melee armor is what makes cavalry the answer |
| Crossbowman | 1 | 2 | 0 | 0 | |
| Outrider | 2 | 2 | 0 | 0 | |
| Cataphract | 5 | 4 | 0 | 1 | Barded — but the Spearman's +15 and Crossbowman's +12 land AFTER armor and ignore it, so the counters still hit in full |
| Ballista / Catapult | 0 | 6 | 0 | 0 | Arrows bounce; swords do not. You cannot shoot a siege line down, you send something at it |
| Trebuchet | 0 | 5 | 0 | 0 | |
| Battering Ram | 2 | 8 | 0 | 0 | Armoured shell. An Archer does 1 to it |
| King Lexor | 6 | 5 | 0 | 3 | |

**Siege armor is 0 on every unit.** Siege damage is the universal answer, and a
siege-armor column that did anything would make its own counter unreliable.

### Buildings

The shape of every building row is one statement: **arrows do almost nothing,
infantry chips slowly, siege goes through.**

| Class | Melee | Ranged | Siege | Magic |
|---|--:|--:|--:|--:|
| Light (Gatherer's Hut, Sawyer) | 3 | 9 | 0 | 1 |
| Standard (Hut, Archery Range, Royal Stable, choice buildings) | 4 | 10 | 0 | 2-3 |
| Military / industrial (Barracks, Siege Yard, Smelter) | 5 | 11 | 0 | 2 |
| Core (Hall, Temple) | 6 | 12 | 0 | 4-6 |
| Fortification (Tower, Wall, Wall Tower, Gate) | 6-8 | 13-14 | 0 | 3-4 |
| King's Court | 8 | 14 | 0 | 5 |

Ranged armor is set **at or above a bow's entire attack** — Archer 8,
Crossbowman 18, Longbowman 25 — so an Archer does the minimum 1 to a wall and a
bow line simply *cannot* take a base. That is what makes the Siege Yard a
necessary building rather than an optional one. Siege armor stays 0 everywhere,
so a Ballista's 40 + its `+30 vs Building` lands in full.

Infantry keeps a slow path in on purpose: a Swordsman does 14 - 5 = 9 to a
Barracks, roughly 90 swings. Possible, never efficient — the AoE relationship.

---

## Counter table (canonical `bonusVsTags` values)

| Unit | Bonus | Delivers the beat |
|------|-------|-------------------|
| Spearman (Age 0) | **+15 vs Cavalry** | The one thing early cavalry loses to (beats 2-3) |
| Alanthor_Crossbowman | **+12 vs Cavalry** | Bolts pierce barding — the cataphract answer (beat 3) |
| Alanthor_Cataphract | **+10 vs Ranged** | Runs down longbow/crossbow lines (beats 2-3) |
| Alanthor_Outrider | **+6 vs Ranged** | Light harasser version of the same job |
| Alanthor_Ballista | +30 vs Building | Hard-target cracker (beat 2) |
| Alanthor_Trebuchet | +80 vs Building | Area siege, wall-line killer (beats 2-3) |
| Alanthor_BatteringRam | +80 vs Building | Buildings-only attacker (`BuildingsOnlyAttacker`) |
| Alanthor_Archer | **+6 vs Infantry** | Closes the triangle (below) - massed bows clear a foot line |
| Alanthor_Swordsman | **+10 vs Siege** | Infantry is how a siege line dies; siege carries 0 melee armor to match |
| Alanthor_Sentinel | **+10 vs Heavy** | Gives the tank something it can actually kill: elite armour |
| Alanthor_Catapult | **+30 vs Building, +20 vs Infantry** | Splash - the anti-mass answer as well as a wall-breaker |

### The triangle

The counter set is a closed rock-paper-scissors, and a new unit should be placed
against it rather than given a bonus in isolation:

- **Infantry beats Cavalry** - Spearman +15 vs Cavalry.
- **Cavalry beats Ranged** - Cataphract +10, Outrider +6 vs Ranged.
- **Ranged beats Infantry** - Archer +6 vs Infantry. **This leg was missing
  until 2026-08-28**: the first two legs were authored and the third was not, so
  infantry had no natural predator and massing Spearmen answered everything
  except the cavalry charge the Spearman already countered.

Siege sits outside the triangle: it beats Buildings, and Infantry beats it.

**Longbowmen still carry no bonus.** Their dominance is raw stats (25 dmg /
20 range against the crossbow's 18 / 12). Giving them the anti-infantry leg as
well would leave them strong against two classes of three, which is exactly what
makes the cavalry counter load-bearing.

### Tags are what make any of this fire

A bonus matches the target's `tags`, so **a unit with no tags cannot be
countered by anything** - every bonus in the table above silently reads 0
against it. As of 2026-08-28 that was true of every sect unit, the Ledger and
the Bazaar Wagon; and the Feraldis Raider was tagged `Infantry` while being
cavalry, so it walked through the anti-cavalry counter untouched.

Tag vocabulary, case-insensitive (`UnitTagParse.Tag`): Infantry, Cavalry,
Ranged, Siege, Heavy, Light, Building, Worker, Religious, Ship. An unrecognised
tag parses to 0 and is silently ignored, so a typo reads exactly like no tag.

**Every combat unit needs a class tag (Infantry / Cavalry / Ranged / Siege) and
a weight tag (Heavy / Light).**

### The ranged ladder

Truth source for the three bow lines. Rebalanced 2026-08-13 — the old numbers
had the **Archer out-ranging the Crossbowman** (25 vs 18) despite sitting below
it on the ladder, and every line shot further than it could see.

| Unit | Damage | Range | Min range | Line of sight |
|---|---|---|---|---|
| Archer | 8 | 10 | 0 | 10 |
| Crossbowman | 18 | 12 | 0 | 12 |
| Longbowman | 25 | 20 | 0 | 20 |

**No ranged unit has a minimum range** (2026-08-28). Only SIEGE keeps a dead
zone — an engine that cannot depress its arc is modelling something real; an
archer backing away from a swordsman is not. In play it cost about a third of
every engagement: the bow lines walked backwards to satisfy a dead zone instead
of shooting, and read as though they were refusing to fight.

Note for anyone re-authoring this: **`minAttackRange: 0` used to mean 10 m.**
`RangedCombatSystem` applied a `DefaultMinRange` of 10 whenever the unit's own
value was zero, so the data could not express "no dead zone" at all. Zero is
taken verbatim now, in both the combat system and the steering halt band.

Three rules hold across the ladder, and new ranged units must respect all:

- **Range never exceeds line of sight.** A unit that outranges its own vision
  can only use the difference through someone else's eyes, which reads as
  shooting at nothing. Range and sight are set equal here.
- **Range rises with the ladder.** Damage and reach both increase
  Archer → Crossbowman → Longbowman, so the ordering is unambiguous and a
  higher-tier bow is never a sidegrade.

Longbowmen deliberately carry **no** bonus tag — their dominance is raw
stats (25 dmg / 20 range vs the crossbow's 18 / 12), which is exactly what
makes the cavalry counter necessary.

Runai / Feraldis counter data follows the same pattern when those trees
are unlocked; the triangle roles (anti-cavalry spear, armor-piercing
crossbow, line-running cavalry) are cross-culture.

---

## The Wall Rule

**Only siege can attack walls.** Any entity carrying `WallTag` — hubs,
curtain instances, wall towers, wall gates — can only be damaged by
attackers whose damage type is **Siege**.

- Non-siege units never auto-acquire wall pieces, and refuse a force-order
  against one (target dropped, same contract as the Battering Ram's
  buildings-only rule).
- Ordinary buildings (halls, barracks, huts...) are NOT covered — any unit
  may still raze them. The rule protects the fortification line only.
- The Border is not exempt and needs no exemption: its wall answer is the
  **Godsplinter** (siege class). Curse pressure against a walled base
  otherwise comes from hostile ground, not from creature chip damage.

Consequence for the meta: beat-1 walls genuinely shelter a base until the
opponent fields beat-2 siege — which is the intended pacing lever.

---

## AI conformance

The AI rides this ladder automatically (`SimpleAISystem` /
`AIAlanthorEndgameSystem`):

- Composition picker trains the best trainable unit per class —
  Longbowman > Crossbowman > Archer, Swordsman > Spearman — and holds
  spearmen while enemy cavalry dominates sightings.
- Cavalry from the Royal Stable (Cataphract > Outrider), siege from the
  Siege Yard (Trebuchet > Ballista).
- King's Court uniques (Ledger, King Lexor) train once, outside the
  budget window.
- The wall doctrine ([Game_AI.md](Game_AI.md)) seals terrain chokepoints
  or encloses the base, with gates and wall towers.
