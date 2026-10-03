# Heroes

**Canon for hero levels, hero ability unlocks, and hero death.** Created
2026-09-08. Supersedes the flat respawn tax in `HeroTrainLimit`
(a compounding training-time penalty per death), which is replaced by the
revival choice in §4.

> **Where the numbers are (2026-10-03).** This doc states the rules. The XP
> curve, the assist radius and share, the Rally level loss and the Full
> Honours scale are constants in `HeroProgressionConfig`
> (`Assets/Scripts/Economy/HeroProgressionConfig.cs`); Honour thy Pledge's
> unlock level, ring, rank and duration ladder are in its folder
> (`Civs/Alanthor/Units/KingLexor/Abilities/HonourThyPledge/`, the
> `PledgeArmy` class and the ability SO); ability cooldowns are on the ability
> SOs and the spell ladder ([Spells.md](Spells.md) §8-9); a hero's own stats
> and price are on its unit SO.

A **hero** is a unit carrying `UniqueUnitTag` — one per faction, gated at the
training command. King Lexor is the only one built today; he **trains at the
Fortress** (the capital, from the level its SO's `minBuildingLevel` names). The
three Shardbound Heroes of [Curse_And_Shardroot.md](Curse_And_Shardroot.md) §3
join this system when they exist. Sect unique units (`SectUniqueUnitTag`) are
**not** heroes and do not level.
**Superseded 2026-09-29 ([Religion.md](Religion.md) §4): every sect's unit is
now a hero** — one per sect, recruited at its chapel for an RP price
(Religion.md §5), levelling by this document.

---

## 1. Levels

**Heroes have levels 1 to 10.** A hero enters play at level 1 and can never
lose a level while alive.

Levels are **earned by fighting, never bought.** Everything else in this game
progresses by paying for it — unit ranks, building levels, technologies — and a
hero is deliberately the exception. A hero you leave standing in your base is a
hero that stays at level 1 no matter how rich you are, which is what makes
"where is my hero right now" a real question and makes the level-gated ability
in §3 a reward for use rather than for income.

### 1.1 Experience

| | |
|---|---|
| A kill is worth | the victim's **total resource cost**, in XP |
| The killer | takes the full amount |
| Allied heroes nearby (`AssistRadius`) | take **a share** (`AssistShare`), so a hero present at the battle still grows |
| Temporary summoned units | are worth **nothing**, either as killer or as victim |

That last row matters: §3 lets a hero conjure an army, and without the rule the
ability would be an XP farm feeding itself — summon, let them die, level up,
summon a bigger army.

### 1.2 The curve

Cumulative XP to reach each level is a table in `HeroProgressionConfig`
(first-pass balance). Its shape is the rule: **the step between levels widens
every level**, so early levels come quickly enough to feel earned in the
first engagement and level 10 is a whole-match project.

### 1.3 What a level does

A level does two things today: it **unlocks abilities** (§2) and it **sets the
price of dying** (§4).

**Levels grant no stat increase yet.** This is deliberate and is the open
balance item on this doc: a hero that gains raw numbers per level compounds
with the ability unlocks and with `UnitRank`, and none of that has been played
yet. Add stat scaling only after the unlock ladder has been tested on its own.

---

## 2. Abilities unlock at a level

An ability may declare the hero level it becomes available at. Below that
level the ability is **not castable and not shown** — it is not merely greyed
out, because a button the player cannot explain is worse than no button.

Abilities with no declared level unlock at 1, which is every ability that
exists today, so nothing already shipped changes behaviour.

**Heroes may carry more than one active ability.** The 2026-08-02 rule that a
unit carries at most one Active and one Passive was always written as a
non-hero rule ("the four slots exist for future heroes"), and this is where
that exception arrives: King Lexor holds King's Call (passive), Liquid Courage
(active) and Honour thy Pledge (active). The cast path takes the slot it is
asked for instead of assuming the first ready active.

Their numbers sit on the spell ladder ([Spells.md](Spells.md) §8-9) and the
ability SOs: Liquid Courage is a hero active; Honour thy Pledge and Shardbound
Fury are hero ultimates.

---

## 3. Honour thy Pledge — King Lexor

> *The oath runs both ways. Lexor calls it in.*

| | |
|---|---|
| **Type** | Active, area centred on the caster |
| **Unlocks at** | hero level **4** |
| **Effect** | Spawns a temporary army of sworn Alanthor soldiers on a ring around the king |
| **Cooldown** | the hero-ultimate band ([Spells.md](Spells.md) §8.2); value on the ability SO |

Size, quality and duration all scale with Lexor's level. The rules:

- **soldiers = hero level**;
- **one in three is an Archer** (rounded down, so the line of Swordsmen always
  outnumbers the bows);
- **rank rises every two levels** from rank 1 at the unlock level to rank 4 at
  level 10;
- **duration grows with each level above the unlock** (base and step in
  `PledgeArmy`).

The "upgrades" half of the ability is the rank: pledged soldiers arrive
already veteran, which is what makes a level-10 call qualitatively different
from a level-4 one rather than merely larger.

**The pledged are temporary and they are not your army.**

- They **cost no population.** The ability is a burst of tempo, not a way past
  the population ceiling.
- They **expire** when their duration runs out, and expiry is a clean removal:
  it awards no experience to anyone, drops no veteran pickup, and triggers no
  death effects. Killing them in the field is a normal death.
- They **give no XP** to whoever kills them (§1.1), so an opponent cannot farm
  Lexor's own summons.

---

## 4. Death and revival

A dead hero is not gone. The faction may call him back at the building that
trained him (the Fortress, for King Lexor), and the player chooses **how much
of the man returns**:

| | Returns at | Cost | Training time |
|---|---|---|---|
| **Rally the Oath** | `max(1, level − 3)` (`RallyLevelLoss`) | the hero's normal cost | normal |
| **Full Honours** | the level he died at | scaled up with that level | scaled up with that level |

Full Honours multiplies both cost and training time by a factor that grows
linearly with the level he died at (`FullHonoursPerLevel` per level above 1),
so a high-level hero costs well over his original price to bring back whole.

This is the decision the rule exists to create: losing a level-9 hero is a
genuine loss either way, and the player has to price their own tempo. Take him
back cheap and fast three levels down and lose Honour thy Pledge's top end, or
pay far more than his original price and wait for the man himself.

Two consequences worth stating:

- **Rally the Oath can drop an ability.** Dying at level 6 and rallying returns
  him at 3, below Honour thy Pledge's unlock. That is the cost, and it is the
  reason the choice is interesting rather than obvious.
- **Reviving does not restore banked XP.** A hero revived at level L starts at
  that level's XP floor, so the progress past it is gone. Otherwise Rally the
  Oath would refund itself within a fight.

The old flat respawn tax (a training-time penalty per death, compounding
forever) is **retired**. It punished death without ever offering a decision,
and it never scaled with what was actually lost.

---

## 5. Open items

- **Stat scaling per level** (§1.3) — deliberately absent; decide after the
  unlock ladder has been played.
- **The other two Shardbound Heroes** need level-gated kits of their own.
- **XP curve and Honour thy Pledge's ladder are first-pass balance**, tuned on
  paper, not yet from a match.
- **The hero numbers are code constants**, not SO/config assets
  (`HeroProgressionConfig`, `PledgeArmy`) — they should move to config assets
  under the component-values rule.
- **Multiplayer**: `HeroTrainLimit`'s respawn counter is a static dictionary
  and is already flagged for the netcode pass; the revival level must live in
  per-faction sim state for the same reason.
