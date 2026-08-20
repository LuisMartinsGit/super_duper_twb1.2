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
| **0 — Skirmish** | Age 0 | **A melee age**: spearmen and workers. Ranged is an Age-1 unlock (2026-08-11 — the Age-0 archer rush was uncounterable and ended matches by minute 15), so the age is about spear lines, map reading, and the economy race to age-up. |
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

### The ranged ladder

Truth source for the three bow lines. Rebalanced 2026-08-13 — the old numbers
had the **Archer out-ranging the Crossbowman** (25 vs 18) despite sitting below
it on the ladder, and every line shot further than it could see.

| Unit | Damage | Range | Min range | Line of sight |
|---|---|---|---|---|
| Archer | 8 | 10 | 2 | 10 |
| Crossbowman | 18 | 12 | 3 | 12 |
| Longbowman | 25 | 20 | 8 | 20 |

Two rules hold across the ladder, and new ranged units must respect both:

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
