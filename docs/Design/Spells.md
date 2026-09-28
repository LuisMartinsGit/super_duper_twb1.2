# Spells: the unified model

Scope: every ability a player or AI **casts**, meaning the ability cards (unit and
hero actives and passives) and the sect active powers. This page covers the
fields a spell has, how its damage lands, how a cast starts and gets
interrupted, and what the UI shows for its cooldown. What each spell *is*
still lives in its own doc: [Sects.md](Sects.md) for the sect powers,
[Heroes.md](Heroes.md) for hero abilities, and
[Curse_And_Shardroot.md](Curse_And_Shardroot.md) §3.1 for Shardbound Fury.

This page defines the mechanics shared by every spell, and (since the
2026-09-27 balance pass) **the balance ladder every spell is measured
against** (§8) and **the value of every spell** (§9). A spell's number that is
not on the ladder needs a written reason in §9; a doc that lists a spell's
numbers (Sects.md, Heroes.md, Age_1_Alanthor.md) must agree with §9.

---

## 1. Two pipelines, one set of rules

| Pipeline | Data | Runtime |
|---|---|---|
| **Ability cards**: King's Call, Liquid Courage (plus its aftermath), Automate Facility, Use Celestar, Scout Sight, War Horn, Full Gallop, Deploy Field Hospital, Honour thy Pledge, Choreographed Volleys, Shardbound Fury | one `AbilityDefSO` per ability, filed under its owner (`.../Abilities/<Ability>/`), and listed **in index order** in `Assets/Resources/AbilityCatalog.asset` | `AbilityLifecycleSystem` (cast, cooldown, aftermath), `AbilityEffectExecutor` (effects), `AbilityAuraSystem` (passives) |
| **Sect active powers**: three per sect, levels I to III | `SectActivePowerSpec` rows in the `SectLeverEffects` tables | `SectActivePowerHelper.Fire` leads to a `PendingSectStrike` telegraph, then `DispatchEffect` |

**The SOs are what runs.** `AbilityCatalog` builds its card table from
`Resources/AbilityCatalog.asset`. The code seed in `AbilityCatalog.cs` is only
the fallback for a missing catalog, and it must stay equal to the SOs. A card's
**index** is its identity in `UnitAbilities` and on the lockstep wire, so the
catalog list is append-only. An empty entry falls back to the seed card at that
index instead of shifting every later card down.

Legacy pipelines that are still in the tree but can no longer be cast:
`UnitAbilitySystem` (the sect units' `UnitAbility`, since `IssueAbility` has no
callers) and `GodPowerSystem` (since `IssueGodPower` has no callers). Their
damage also goes through §3. They are not part of this model.

## 2. Fields

### Ability card (`AbilityCard` / `AbilityDefSO`)

| Field | Meaning |
|---|---|
| `Activation` | Active / Passive / OnDeath |
| `Targeting` | SelfCast / SingleTarget / Area / Aura / Global |
| `Affects` | who the effects reach (Self, allied culture, allied cavalry, allied ranged, enemies, economy buildings, ...) |
| `CastTime` | seconds of **channel** before the effects land (0 = instant). See §4 |
| `Duration` | seconds the effect lasts (-1 = permanent passive) |
| `Cooldown` | seconds before a recast, **charged when the cast completes**. 0 = *auto* (§5) |
| `Radius` / `Range` | area size / cast range. On an aimed card, Range 0 means unlimited |
| `AimedAtPoint` | **new.** The player picks a ground point with the targeting ring. False for an Area card that forms around the caster wherever he stands (War Horn, Full Gallop, Honour thy Pledge, Choreographed Volleys, Shardbound Fury). True only for Use Celestar |
| `Damage` | **new.** Damage per victim (0 = deals none). Shardbound Fury: 60, the landing slam |
| `DamageType` | **new.** The armor column the damage is measured against. **Magic** unless the design names another type |
| `Effects[]` | structured effects (`AbilityEffectKind` + value) |
| `Aftermath[]` | cards cast automatically when this one ends |
| `UnlocksAtLevel` | hero level gate ([Heroes.md](Heroes.md) §2) |

`ArmorPct` is now a genuine **percentage of the unit's own armor for the
incoming damage type** (`SpellBuff.ArmorPct`, resolved where the damage lands).
King's Call's +15 % gives a 6-armor unit +1. It used to be added as a flat +15
armor, which made King's Call's aura close to immunity against light attacks.
`ArmorFlat` stays flat.

### Sect power (`SectActivePowerSpec`)

`Kind`, `Radius`/`Reach`, `Magnitude`, `Duration`, `Cooldown`, and `Secondary`
are unchanged. New:

- `DamageType`: **Magic** by default. Set to **True** on Justice's Sentence
  (Sects.md: "true damage"), and on Unmake and Spite, whose design is exact
  arithmetic ("loses 50 % of its current HP", "five units that have dealt 200
  take 40 each") that armor would break. See §7.
- `Damage` (read-only): the flat damage number a damaging kind carries in
  `Magnitude`. That covers Smite/Sentence and Nowhere to Hide. For Writ of
  Attainder it is the amount *per kill*. For every other kind it is 0
  (Spite's `Magnitude` is its per-head **cap**, not a damage figure).

The damage type rides the `PendingSectStrike` (`DamageKind`) for the same
reason `Secondary` does: a strike resolves with the values it was fired with.

## 3. Damage routing: one door

**Every point of spell damage goes through `SpellDamage.Apply`**
(`Scripts/Systems/Combat/SpellDamage.cs`). No spell writes `Health` itself.
In order, it applies:

1. **Friendly fire.** Allies take nothing unless the caller opts in.
   `Alliances.AreHostile` is the only hostility test ([Teams.md](Teams.md)).
   The Smite's building pass used to spare only the caster's own buildings, so
   it hit team allies.
2. **Invulnerable.** Immovable III: no damage.
3. **The Wall Rule.** Only Siege damage hurts a wall piece
   ([Combat_Pacing.md](Combat_Pacing.md)).
4. **Armor.** The same formula as a weapon hit:
   `max(1, damage - armor)`. The armor value is the victim's `Defense` column
   for the spell's `DamageType`, plus Fortified, plus `SpellBuff` armor (flat
   and %). **True** damage skips armor entirely.
5. **Incoming-damage scaling.** `AbilityDamageHooks.ScaleIncoming`
   (Liquid Courage's 90 % reduction).
6. **Commit.** `DamageOverTime.Commit`: the Life Cling and Second Wind
   (`SectDeathWard`) floors, and hostile kill credit.

Death is never handled at the damage site. `Health` is written, and
`DeathSystem` destroys the entity on its own pass (the unit death contract).

Sites routed through the door: Smite / Sentence (units and buildings), Unmake
(target and level III splash), Spite, Writ of Attainder, Nowhere to Hide
(units and buildings), Shardbound Fury (the landing slam and the building
damage), and the legacy ArcanePulse. Damage over time (burning ground, bleed,
curse) already had its own door, `DamageOverTime`, which obeys the same rules
([Fire.md](Fire.md)). The Shardbound **death detonation** deliberately stays
raw: its design is "killed on landing, friend or foe".

## 4. Cast and interrupt

Ability cards (`CastTime > 0`):

- A cast starts a **channel** (`AbilityCastState`). **The cooldown is not
  charged yet.**
- The channel is **interrupted**, losing the effect but charging **no**
  cooldown, by any of these:
  - a **new order**: a move order to a different point, or an attack order on a
    different target, compared with a snapshot taken when the channel began.
    An order that simply *ends* (arrival, target dead) does not count.
  - a **stun**: being hurled into the air (`Launched`, Shardbound Fury). This
    is the one hard stun in the game.
  - **death**: `Health` at 0, or the death animation running.
- Silence (Blood Rain) and Blinding Glare III **block new casts** but do
  **not** break a channel that is already in flight. That is unchanged.
- While a unit is channelling it cannot start another cast.
- When the channel completes, the effects land and **then** the slot's cooldown
  is charged.
- Instant casts (`CastTime 0`) charge the cooldown and apply in the same tick.

A channel's progress (`AbilityCastState.Progress`, 0 to 1, read with
`AbilityQuery.TryGetCast`) drives the **cast bar**: the channelling ability's
button in the spells bar fills from left to right, and its label reads
"casting N s".

Sect powers keep a fixed **wind-up**, shown as a telegraph ring, in three
tiers (§8.6):

| Power | Wind-up |
|---|---|
| deals damage (Writ of Attainder, Sentence, Unmake, Spite, Pyre, Nowhere to Hide) | **3.0 s** |
| hostile, no damage (Heavy Bureaucracy, Sew Disorder, Spy Network, Blinding Glare) | **1.5 s** |
| friendly (heals, buffs, wards, economy, structures) | **1.0 s** |

The damage tier is the "3 s telegraph" Sects.md names for Sentence and Unmake,
applied to every damage power: the power with the biggest per-victim number
gets the longest tell. The cooldown is charged when the power is fired. The
wind-up is a dodge window, not a channel, so nothing interrupts it.

## 5. Cooldown display: what you read is what is charged

- **Cards.** `AbilityCard.EffectiveCooldown` is the authored `Cooldown`. For
  *auto* (0), it is `Duration + 1 s` from the moment the effect lands, which
  gives the same recast timing as the old "cast + duration + 1 from the start".
  Every active shows this number, including the auto cards (Liquid Courage,
  Automate Facility), which used to show no cooldown at all.
- **Sect powers.** `SectActivePowerHelper.EffectiveCooldown` is the one
  formula, used by both `Fire` and the Religion panel: the **authored**
  cooldown, then `x 0.7` with the Shardroot enshrined for that sect
  ([Curse_And_Shardroot.md](Curse_And_Shardroot.md): "all sect power
  cooldowns reduced by 30 %"), then `x 0.9` or `x 0.8` from the Shrine of
  Ridan level ([Age_0.md](Age_0.md): -10 % / -20 %).
  **There is no hidden global scale any more.** Until 2026-09-27 every spec
  was multiplied by `CooldownScale = 0.5`, so the tables said 240 where the
  game charged 120. That halving is folded into the authored numbers: what
  the table says is what a player without Shrine or Shardroot waits.
  - The Religion panel's power tooltip now describes the power **at the
    faction's current power level** (I / II / III, from adoption timing, which
    is exactly what `Fire` casts). Its cooldown is the fully effective figure.
    It used to show the raw level-I figure, twice the real wait.
  - The roster and sect texts, which have no faction to ask about the
    Shardroot or the Shrine, show the authored cooldown.

## 6. Tooltips and aiming

- An ability is **aimed** (it puts up the targeting ring) exactly when
  `Targeting == Area && AimedAtPoint`. This fixes Use Celestar, which could not
  be aimed because the old gate demanded `Range > 0`.
- Card tooltips now describe `FireRatePct`, `SummonPledgeArmy` and
  `ShardboundFury`, and add a damage line ("deals 60 magic damage") and a
  channel line.
- The armour line reads "+15 % armour" and is now true.


## 7. Open questions

What the 2026-09-27 balance pass settled, and what it left open.

Settled (see §8 and §9 for the numbers):

- **Liquid Courage uptime** was 91 % (10 s on an 11 s auto cooldown). It now
  has an explicit 45 s cooldown: 22 % uptime, under the 25 % cap for strong
  defensive effects.
- **Damage spread** (15 to 200 per victim, no ladder) is replaced by the
  per-reach damage bands of §8.3.
- **Justice Sentence** is now the ladder's single-target figure, 120 true
  damage at level I, which is also what Sects.md always said. Sects.md's 120
  (small) and 180 (medium) at II / III are replaced by the ladder's 90 and 80.
- **Sect wind-up** has three tiers, with 3 s for every damage power (§4).
- **Ash Pyre** burns for 15 / 30 / 30 s, as Sects.md says.
- **Wrath Spite** uses the canon radii 8 / 15 / 25 m.
- **Reliquary radii** snap to 8 / 8 / 15 m.
- **Writ of Attainder** is capped at 4 kills per victim (120 damage).

Still open:

1. **Missing legacy sect powers.** These are named in Sects.md but have no
   implementation, because their slot falls back to a legacy stand-in:
   Silence *Hush* and *Entomb*; Justice *Writ of Blood*; Veneration *Crystal
   Communion* and *Ascend*; Ash *Cinderfall* and *Ashen Veil*; Ruin *Profane
   Strike* and *Sunder*; Wrath *Final Hour* and *Wrathfire*. The legacy
   stand-ins escalate the sect's one implemented power across its three
   slots (the slot acts as the level), and they are on the ladder like
   everything else.
2. **Damage types chosen here.** Unmake and Spite are True, because their
   arithmetic is exact. Every other spell is Magic, because no doc names a
   type. Should Unmake instead be Magic, so that building magic armor (2 to 6)
   applies?
3. **Shardroot model.** Curse_And_Shardroot.md enshrines the Shardroot in the
   Temple and cuts *all* sect powers by 30 %. The code allocates it to ONE
   sect's chapel slot and cuts only that sect. The multiplier now matches the
   doc (x 0.7); the per-sect allocation does not.
4. **Legacy pipelines not on the ladder.** `GodPowerSystem` (radius 14 m,
   120 damage) and `UnitAbilitySystem`'s Arcane Pulse (15 damage, 20 s) cannot
   be cast any more, so they were left as they are.
5. **Shardbound Fury's geometry** (throw heights, flight time) and the
   Shardbound death detonation (lethal, 2500 to buildings) are still code
   constants in `ShardboundFury.cs`. The detonation is a death event, not a
   spell, and is off the ladder by design.

---

## 8. The balance ladder

Every spell is measured against the game's own units, so its number means
something next to a sword.

### 8.1 Reference frame

| Measure | Value | Source |
|---|---|---|
| **Line unit HP** | **130** (Spearman 100-130, Archer 60-90, Swordsman 145, Cataphract 160, Nobleman 175) | unit SOs |
| Line unit DPS | 8-11 (Spearman 13 / 1.5 s, Swordsman 14 / 1.4 s, Cataphract 18 / 1.6 s) | unit SOs |
| Line unit magic armor | 0-2 (Litharch / Scholar 3-4, Lexor 3) | [Combat_Pacing.md](Combat_Pacing.md) |
| Hero | King Lexor 650 HP, 45 / 1.4 s | KingLexor SO |
| Standard building | 600-1500 HP (Hut 650, Barracks 800, Hall 1200, Temple 1500) | building SOs |
| Match pacing | Age 0 ends at 3-6 min; a big fight lasts about 30-60 s | [Combat_Pacing.md](Combat_Pacing.md) |

So **10 damage is about one line-unit hit**, and **130 is one line unit**.

### 8.2 Cooldown bands

Real seconds: the number authored is the number charged (§5). A power's
cooldown is **flat across its levels I / II / III**: the level buys reach,
magnitude or duration, never tempo. Tempo is what the Shrine of Ridan and the
Shardroot sell, and a level that also shortened the cooldown sold it twice.

| Band | Cooldown | Who |
|---|--:|---|
| Hero active | **45 s** | Liquid Courage |
| Unit tactical active | **60 s** | War Horn, Full Gallop, Choreographed Volleys, Use Celestar, Ranging Shot |
| Sect tactical (buff, debuff, heal, ward, utility) | **60 s** | most sect powers |
| Sect economy | **60 s** | Harvest the Veil, Call to Arms, Cleanse |
| Sect damage | **75 s** | Writ of Attainder, Sentence, Unmake, Spite, Pyre |
| Building active | **90 s** | the Reliquary's Scry, Lockout and Vision |
| Hero ultimate | **120 s** | Honour thy Pledge, Shardbound Fury |
| Sect wildcard | **120 s** | Sew Disorder, Immovable III, Raise Anew I |
| Map-wide | **150 s** | Blood Rain, Nowhere to Hide |
| Deployable structure | **300 s** | Deploy Field Hospital |
| Economy card, auto | duration + 1 s | Automate Facility (its Under Automation lockout is the brake) |

**The one-minute rhythm.** A big fight lasts 30-60 s, so a tactical power
comes back once per fight, a damage power a little later, and a wildcard or
map-wide power once every two or three fights.

**Exceptions**, where the cooldown rises with the level because the level buys
a qualitatively bigger effect, not more of the same:

- **Raise Anew** 120 / 150 / 180 s. Each level raises a different, permanent
  and much larger building (Tower, then Fortification, then a Fortress).
- **Immovable** 60 / 60 / 120 s. Levels I-II are an armor buff (tactical);
  III is invulnerability, a wildcard-class effect.

### 8.3 Damage bands

Damage **per victim**, at level I, by reach. The wider the net, the less each
fish pays.

| Reach | Per victim | Share of a line unit |
|---|--:|---|
| **Single** (1.5 m pick) | **120** | about a whole line unit |
| **Small** (8 m) | **60** | about half |
| **Medium** (15 m) | **40** | about a third |
| **Large** (25 m) | **30** | about a quarter |
| **Map-wide** | **20** | a sixth |

**Level rule.** Damage = band(reach at that level) x **1.0 / 1.5 / 2.0** for
level I / II / III (`SectLeverEffects.LevelScalar`). A power whose reach grows
one step per level therefore holds its per-victim damage (Small 60, Medium
40 x 1.5 = 60, Large 30 x 2 = 60), and a power whose reach holds hits harder.
**A level buys reach or damage, never both for free.**

Derived rules:

- **Heals** mirror damage as a fraction of max HP: Single 90 %, Small 45 %,
  Medium 30 %, Large 25 % (the damage band over 130 HP, rounded to 5 %), times
  the same level multiplier.
- **Damage zones** (burning ground) deal the band over **10 s**: a unit that
  stands in the fire for ten seconds has taken one hit of the band.
- **Conditional damage** (it lands only on units that meet a condition, like
  Writ of Attainder's kill record or Spite's damage-dealt pool) may reach
  **2x the band**, because a crowd that does not meet the condition pays
  nothing.
- **A hero ultimate** deals the damage of a level-III sect power of the same
  reach: Shardbound Fury's 25 m slam is Large x 2 = **60**. Its flat building
  figure, **300**, is half a standard building: a hero ultimate cracks a
  district, it does not level it.
- **Fractions of current HP** (Unmake) scale 40 / 60 / 80 %, the level
  multiplier on a 40 % base. They can never kill on their own.

### 8.4 Uptime caps

Uptime = duration / cooldown, for a buff or debuff one caster can keep
refreshing.

| Effect | Cap |
|---|--:|
| **Strong defense**: at least 50 % damage reduction (Liquid Courage, +8 armor against 13-18 damage hits), invulnerable, cannot die, +100 % building HP | **25 %** |
| Every other combat buff or debuff | **35 %** |
| Exempt: territory and economy (Cleanse, Harvest, Call to Arms, Automate Facility), curse immunity (Veil-Touched), vision (Spy Network, reveals), summons | none |

### 8.5 Radii

Every area uses one of the four canon radii of [Sects.md](Sects.md) §2 -
Single 1.5, Small 8, Medium 15, Large 25 m - including the ability cards. A
value between two snaps to the nearest; a tie snaps to the smaller. Honour
thy Pledge's 6 m is a spawn ring (where the soldiers appear), not an area of
effect, and is exempt.

### 8.6 Wind-ups

| Sect power | Wind-up |
|---|--:|
| Deals damage | 3.0 s |
| Hostile, no damage | 1.5 s |
| Friendly | 1.0 s |

---

## 9. Every spell

Before = what shipped before 2026-09-27, **in real seconds** (sect cooldowns
already halved by the retired 0.5x scale). After = the ladder. I / II / III
separated by slashes. "-" = none.

### 9.1 Ability cards and building actives

| Spell | Band | Cooldown before -> after | Damage | Radius before -> after | Duration | Cast |
|---|---|---|---|---|---|---|
| King's Call (Lexor, passive) | aura | - | - | 15 | permanent | - |
| Liquid Courage (Lexor) | hero active | auto 11 -> **45** | - | - | 10 (22 % uptime) | - |
| Veilshift Withdrawal / Life Cling | aftermath | - | - | - | 5 | - |
| Honour thy Pledge (Lexor, level 4+) | hero ultimate | 90 -> **120** | - | 6 spawn ring | 30-60 by level | - |
| Shardbound Fury (Lexor + Shardroot) | hero ultimate | 120 | slam 90 -> **60** magic; buildings 600 -> **300** | 22 -> **25** | - | - |
| Use Celestar (Scout) | unit tactical | 30 -> **60** | - | 10 -> **8** | 15 | 5 s |
| Scout Sight (passive) | - | - | - | - | permanent | - |
| War Horn (cavalry) | unit tactical | 60 | - | 20 -> **15** | 20 window | - |
| Full Gallop (cavalry) | unit tactical | 75 -> **60** | - | 20 -> **15** | 8 | - |
| Choreographed Volleys (ranged) | unit tactical | 120 -> **60** | - | 20 -> **15** | 5 | - |
| Automate Facility (Ledger) | economy, auto | 31 | - | single | 30 (+60 lockout) | 6 s |
| Deploy Field Hospital (Litharch) | deployable | 300 | heals 3 HP/s | 12 -> **15** | 120 | 3 s |
| Ranging Shot (Siege Yard) | unit tactical | 45 -> **60** | next shot +100 % | faction | 10 window | - |
| Reliquary: Scry | building active | 90 | - | 10 -> **8** | 10 | - |
| Reliquary: Lockout | building active | 120 -> **90** | - | 8 | 6 | - |
| Reliquary: Vision | building active | 75 -> **90** | - | 18 -> **15** | 15 | - |

Reliquary building level III still cuts its three cooldowns by 30 %.

### 9.2 Sect powers (canon)

| Sect / power | Band | Cooldown before -> after | Effect before -> after | Reach |
|---|---|---|---|---|
| Antiquity: **Writ of Attainder** | damage | 55/50/45 -> **75** | 40/60/80 per kill, uncapped -> **30 per kill, max 120** (4 kills); III floor 60 -> **30** | S / M / L |
| Antiquity: Heavy Bureaucracy | tactical | 75/67/60 -> **60** | 30 s -> **20 s** shutdown | Single / S / L |
| Antiquity: Sew Disorder | wildcard | 150/135/120 -> **120** | 8 s / 20 s / until killed | S / M / L |
| Renewal: **Hands of Plenty** | tactical | 45/42/40 -> **60** | heal 30/50/80 % -> **45/45/60 %** (III + 10 s regen) | S / M / M |
| Renewal: Raise Anew | wildcard (exception) | 78/98/117 -> **120/150/180** | Tower / Fortification / Fortress, permanent | Single |
| Renewal: Second Wind | tactical | 75/70/65 -> **60** | cannot die 6 / 12 / 12 s; III heals 25 % | S / S / M |
| Fortitude: Stoneveil | tactical | 60/55/50 -> **60** | veil 8 / 15 / 15 s; III +25 % damage 10 s | S / S / M |
| Fortitude: **Bulwark** | tactical | 60/55/50 -> **60** | +100 % building HP 30 s -> **15 s** | Single / S / M |
| Fortitude: Immovable | tactical / wildcard (exception) | 60/65/120 -> **60/60/120** | +5 armor 10 s / +8 armor 15 s / invulnerable 20 s | S / M / L |
| Reclamation: Harvest the Veil | economy | 60 | node over-yield, unchanged | Single |
| Reclamation: Cleanse | economy | 60 | influence 20 / 40 / 40 s, unchanged | S / M / L |
| Reclamation: Veil-Touched | tactical | 50 -> **60** | no curse damage 15 / 30 / 30 s | S / M / L |
| Witness: Spy Network | tactical | 60/52/45 -> **60** | spies 45 s / 90 s / until dead | Single |
| Witness: Blinding Glare | tactical | 60/55/50 -> **60** | blind 8 / 12 / 12 s; III locks abilities | S / M / L |
| Witness: **Nowhere to Hide** | map-wide | 150/135/120 -> **150** | 60/110/200 -> **20/30/40** per revealed enemy (II+ buildings) | map-wide |
| War: Blood Rain | map-wide | 120/110/100 -> **150** | map-wide haste +5/10/15 % and spell lockout, 10 / 20 / 30 s | pool S / M / L |
| War: Call to Arms | economy | 75/67/60 -> **60** | half-cost training 15 / 30 / 30 s (III double speed) | Single / S / M |
| War: Bloodfury | tactical | 60/55/50 -> **60** | +25 % damage 8 / 12 / 12 s; III +5 armor | S / M / L |

Uptime check at the new numbers: Second Wind 10 / 20 / 20 %, Bulwark 25 %,
Immovable 17 / 25 / 17 %, Stoneveil 13 / 25 / 25 %, Blinding Glare 13 / 20 /
20 %, Bloodfury 13 / 20 / 20 %, Heavy Bureaucracy 33 %, Blood Rain 7 / 13 /
20 %. Bulwark's 30 s was 50-60 % uptime on a doubled building, the reason it
drops to 15 s.

### 9.3 Sect powers (legacy stand-ins)

A legacy sect has one implemented power; slot 1 / 2 / 3 is that power at
level I / II / III (slots unlock one Temple level at a time). Justice is the
exception: its slots are Eye of the Law I, Sentence I and Sentence III (named
Final Sentence).

| Sect / power | Band | Cooldown before -> after | Effect before -> after | Reach before -> after |
|---|---|---|---|---|
| Silence: Whisper-Wind | tactical | 45/37/30 -> **60** | +20/30/40 % speed 8/10/12 s -> **+20 % speed 8 / 12 / 12 s** | 8/9/10 -> **S / M / L** |
| Justice: Eye of the Law (slot 1) | tactical | 30 -> **60** | reveal 10 s | 14 -> **M** |
| Justice: Sentence (slot 2) | damage | 60 -> **75** | 60 -> **120** true | 6 -> **Single** |
| Justice: Final Sentence (slot 3) | damage | 120 -> **75** | 150 -> **80** true | 10 -> **M** |
| Veneration: Litany | tactical | 60/50/40 -> **60** | +20/35/50 % damage 10/12/14 s -> **+20 % 10 s / +20 % 15 s / +50 % 15 s** | 8/9/10 -> **S / M / L** |
| Ash: Pyre | damage | 60/50/40 -> **75** | 8/11/14 DPS for 6/9/12 s -> **6 DPS for 15 / 30 / 30 s** | 6/7/8 -> **S / M / L** |
| Ruin: Unmake | damage | 75/62/50 -> **75** | 50/75/90 % -> **40/60/80 %** of current HP; III splash 25 % | search 8/9/10 -> **S**; splash 6 -> **S** |
| Wrath: Spite | damage | 60/50/40 -> **75** | pooled damage split, uncapped -> **capped at 120 per head** | 6/9/13 -> **S / M / L** |

The Pyre's 6 DPS is the zone rule (Small 60 over 10 s; Medium 40 x 1.5 and
Large 30 x 2 over 10 s give the same). Spite's cap is the conditional cap
(2x the band): the canon arithmetic is unchanged below it, so the worked
example (five units, 200 dealt, 40 each) still holds. What the cap stops is a
single veteran with thousands of damage dealt paying it all at once.

### 9.4 Where the numbers live

- Ability cards: the `AbilityDefSO` assets, mirrored by the code seed in
  `AbilityCatalog.cs` (they must stay equal).
- Sect powers: the `SectLeverEffects` tables, which build their figures from
  `SpellLadder` (`Sects/SpellLadder.cs`), the one place the bands of §8 live
  in code.
- Ranging Shot: `AlanthorActiveHelper.asset`, beside `AlanthorActiveHelper.cs`.
- The Reliquary's three actives: `ReliquaryHelper.asset`, beside
  `ReliquaryHelper.cs`.
- Shardbound Fury: its card (cooldown, radius, slam damage) and
  `ShardboundFury.cs` (the building figure and the throw geometry).
