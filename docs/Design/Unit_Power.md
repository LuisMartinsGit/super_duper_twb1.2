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
  damage undone; a worker's throughput is what it contributes to the fight it
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

## Reading the roster

There is deliberately **no roster table here.** A table of per-unit Power
values restates SO stats (damage, cooldowns, armour, hp, prices, train times)
and goes stale the moment any of them is retuned — which is exactly the drift
this number exists to avoid. Read a unit's live Power on its training button
or in the selected-unit panel; the stats it is computed from are on the unit
SOs (and in the generated calculator `tools/calculator/TechTree.html`, which
does not compute Power itself). The SO is always right; anything written down
elsewhere is illustrative at best.

What the 2026-08-28 pass taught, as questions rather than verdicts:

- **Healers score high.** Healing counts fully as offence, which is generous —
  healing needs a body to heal, and the metric assumes one is always there.
  Part of a healer's lead is that assumption.
- **A unit far below par is a pricing question.** A unit whose value lies
  elsewhere (a hero, a ritualist, a counter-piece) may legitimately score low;
  a plain line unit that scores low and is hard-countered has no such excuse.
- **Heroes scoring below par is expected.** A hero is bought for what it does
  to a map, not for its stat line.
- **Armour does real work.** Because armour is measured against the reference
  hit, heavy armour moves a unit's effective HP substantially; before the
  armour pass these differences were rounding errors.
- **A support unit with `buildSpeed` unset scores from its attack alone.** A
  worker whose build speed is 0 in its SO is worth checking.
