# The Power number

One statistic per unit, for comparing units against each other and balancing
them. Implemented in `Assets/Scripts/Data/TechTree/UnitPower.cs`; shown on the
training button and in the selected-unit panel.

## What it is

**Combat output per resource invested.** Cost has to be inside it or the number
cannot answer the question balancing actually asks. A Trebuchet out-fights a
Spearman — that tells you nothing. A Trebuchet that out-fights a Spearman by
*less than it costs* is the finding.

**~100 is par** for the Age 0 + Alanthor roster, so 50 reads as "half the value
for the money" at a glance and the eye does the work instead of arithmetic.

**It is purely derived.** Every input is a stat the unit already carries in its
SO, so there is nothing to author and nothing that can drift: retune a cost or a
cooldown and the Power number moves with it the same frame. It is deliberately
*not* a field on `UnitDefSO` — an authored power rating is a second opinion
about a unit that immediately starts disagreeing with the first.

## How it is built

```
cycle       = max(attackCooldown + aimTime, 0.5)
DPS         = damage / cycle                       (0 if the unit has no attack)
offence     = DPS x (1 + aoeRadius/4) + healsPerSecond + buildSpeed x 0.5
effectiveHP = hp x 12 / max(1, 12 - avgArmor)      (12 = median attack)
reach       = 1 + max(attackRange, siegeRange) / 40
combat      = sqrt(offence x effectiveHP) x reach
investment  = supplies + iron x2 + veilstone x4 + veilsteel x8 + trainingTime x2
POWER       = 819 x combat / investment
```

Why each piece is shaped that way:

- **Aim time is part of the cycle, not an alternative to cooldown.** An archer
  that winds up for 0.5 s then waits 1.5 s fires every 2 s. Treating those as
  competing floors flattered every ranged unit in the roster.
- **Armor is subtracted, so durability is measured against a reference hit.**
  N armor multiplies how long you live by `12 / (12 - N)`, not by some
  percentage of your health bar — 12 being the roster's median attack, so the
  score reads as "how long it survives the average thing shooting at it".
  Averaged across the four damage types, because the metric does not know what
  the unit will be shot by. Until 2026-08-28 this read the `Defense` component's
  stale comment and computed `(d + 100) / 100`, valuing 5 armor at +5% when it
  is really +71%: every durability figure before that date was wrong by that
  much. Canonical armor values: [Combat_Pacing.md § Armor](Combat_Pacing.md).
- **Geometric mean, not a product or a sum.** A glass cannon and a damage sponge
  should each score like the mid-range unit they respectively beat and lose to.
  A product lets one dimension run away with the number; a sum lets a unit with
  no offence at all still look like a fighter because it has health.
- **Range is survivability you do not pay for in HP.** A longbow that never gets
  hit is worth more than its health bar says.
- **Support output is on the damage scale.** A point of healing is a point of
  damage undone; a builder's throughput is what it contributes to the fight it
  is not in.
- **Resource weights double per tier.** A territory pays supplies for free and
  iron / veilstone only where the map put a node (Regions.md §4); veilsteel is
  rarer still.
- **Training time is investment.** A unit also costs the *building* that made
  it, for as long as it was in there. Leaving that out makes a slow, cheap unit
  look free.
- **819 is a readability constant.** It moves every unit together and so can
  never change a comparison.

## What it is NOT

It does not predict who wins a fight. It knows nothing about counters
(`bonusVsTags`), formations, terrain, micro or numbers, and a unit whose whole
job is one of those will score badly while being essential. **Treat an outlier
as a question, not a verdict.**

Units with no combat or support output at all report **n/a** rather than 0. A
Scout is not a weak fighter; it is not a fighter. Reporting 0 would be a lie
dressed as a number.

## The roster (2026-08-28, after the armor pass)

| Unit | Class | DPS | Armor m/r/s/mg | Eff. HP | Combat | Investment | **Power** |
|---|---|--:|:--:|--:|--:|--:|--:|
| Scout | human_scout | 0.0 | 0/1/0/0 | 61 | — | 107 | n/a |
| Ledger | support | 0.0 | 0/0/0/2 | 146 | — | 370 | n/a |
| Litharch | human_support | 6.7 | 0/0/0/3 | 128 | 50 | 198 | **208** |
| Swordsman | human_melee | 10.0 | 4/2/0/1 | 170 | 42 | 243 | **142** |
| Spearman | human_melee | 6.7 | 1/1/0/0 | 125 | 30 | 184 | **133** |
| Longbowman | human_ranged | 6.2 | 0/1/0/0 | 56 | 28 | 174 | **132** |
| Sentinel | human_melee | 6.7 | 7/5/0/2 | 296 | 46 | 298 | **127** |
| Nobleman | human_melee | 13.8 | 5/3/0/2 | 221 | 57 | 396 | **118** |
| Archer | human_ranged | 4.0 | 0/1/0/0 | 61 | 20 | 140 | **114** |
| Catapult | machinery_siege | 5.6 | 0/6/0/0 | 229 | 71 | 576 | **101** |
| Outrider | human_cavalry | 8.6 | 2/2/0/0 | 104 | 31 | 254 | **100** |
| Ballista | machinery_siege | 8.0 | 0/6/0/0 | 251 | 70 | 576 | **99** |
| Crossbowman | human_ranged | 5.4 | 1/2/0/0 | 75 | 26 | 234 | **91** |
| BatteringRam | machinery_siege | 12.0 | 2/8/0/0 | 429 | 74 | 692 | **87** |
| KingLexor | melee | 32.1 | 6/5/0/3 | 918 | 176 | 1680 | **86** |
| Trebuchet | machinery_siege | 8.6 | 0/5/0/0 | 223 | 135 | 1340 | **82** |
| Worker | human_support | 4.0 | 0/0/0/0 | 70 | 17 | 204 | **69** |
| Cataphract | human_cavalry | 11.2 | 5/4/0/1 | 202 | 50 | 880 | **46** |
| Scholar | human_magic | 20.0 | 0/1/0/4 | 100 | 47 | 1376 | **28** |

Median 100, range 28-208.

### What this table says

Read these as questions, not as a to-do list.

- **The Litharch at 208 is the biggest outlier.** 6 heal/s counts fully as
  offence, which is generous — healing needs a body to heal, and the metric
  assumes one is always there. Some of the gap is that assumption; the rest is
  that a 100/25 healer is genuinely cheap for what it does.
- **The Scholar at 28 and the Cataphract at 46** are the two units paying far
  above the roster rate. The Scholar is a well ritualist whose value is
  unlocking a victory condition, so a low combat score is not automatically
  wrong. The Cataphract has no such excuse — 320/120/60 for a unit the Spearman
  and Crossbowman both hard-counter.
- **King Lexor at 86 is expected.** A hero is bought for what it does to a map,
  not for its stat line.
- **Armor now does real work.** The Sentinel's 7/5/0/2 is worth +37% effective
  HP against the median attack and moved it from 114 to 127; the Battering Ram's
  8 ranged armor means an Archer does 1 to it. Before the pass every one of
  these numbers was a rounding error.
- **The Worker's 69 still comes entirely from its 2 damage**, because
  `buildSpeed` is 0 in its SO. A builder whose build speed is unset is worth
  checking — it is the one hole this pass did not close.
