# Religion

> **Doc version: 2026-09-29. Canon for religion points, the Temple, chapels and
> sect heroes.** Supersedes:
>
> - [Overview.md](Overview.md) — the RP awards (Shrine +1, Age II +3, …), the
>   2 / 3 adoption price, double chapels and "sects upgrade with the Temple".
> - [Sects.md](Sects.md) §1 Building / Unit slots and §3 (power level from
>   adoption timing). Sects.md's kits, radii, cooldowns and damage ladder
>   (§2, §2b, §3b-§6) STAND.
> - [Age_0.md](Age_0.md) — **the Shrine of Ridan is cut.**
> - [Heroes.md](Heroes.md) "sect unique units are not heroes" — **they are now.**
>
> Everything here is **(new — not yet in code)**.

---

## 1. Religion points come from the curse

**Killing curse units earns points (pts).** Points convert to **Religion
Points (RP)**, the only currency of the religion layer.

| Curse unit killed | pts |
|---|---|
| Crystalling | 3 |
| Veilstinger | 8 |
| Godsplinter | 20 |

**The last hit decides who is paid**, whatever dealt it — a soldier, a tower, a
sect power, a hero. An ally's last hit pays the ally.

**The first points are cheap.** The pts needed for your next RP rise with the
RP you have already earned from kills, then level off:

`pts for RP #n = min(ptsBase + ptsStep × (n − 1), ptsCap)`

with `ptsBase` 40, `ptsStep` 15, `ptsCap` 100 → 40, 55, 70, 85, 100, 100, …
The first RP is 14 crystallings; from the fifth on, it is 100 pts.

Points accrue from the first tick, **before any Temple exists**: the first RP
is what pays for the Temple.

### 1.1 Buying RP

Players who start far from the curse are not locked out: the Temple sells RP
through its research **Tithe**, repeatable.

| | |
|---|---|
| First purchase | **300 Supplies + 150 Iron + 100 Veilstone** → 1 RP |
| Each later purchase | ×1.5 the previous price (per faction, rounded) |

Buying is always worse than fighting after the first couple of purchases — on
purpose.

## 2. The Temple of Ridan

| | |
|---|---|
| Age | **0** |
| Cost | **1 RP** + 200 Supplies + 100 Iron |
| Limit | one per faction |
| Trains | **Litharch** (moved from the cut Shrine) |
| Research | Tithe (§1.1); the Shrine's heal ladder and Warrior priests move here unchanged |
| Chapel sites | **six** — the six non-door faces of the seven-sided cathedral (Overview.md) |

**Destroying a curse node pays a full Religion Point** (2026-09-29) to the
faction that lands the last hit — straight to the balance, not through the
points ladder, and not boosted by the Temple.

**A standing Temple also boosts the curse harvest** (2026-09-29): while a
faction's Temple is finished and alive, every curse kill pays it **+50 %**
points (`templeKillBonusPct`) — crystalling 4, veilstinger 12, godsplinter 30.

**The Temple produces religion slowly** (2026-09-29): while it stands it pays
its faction **1 point every 5 seconds** (`templeSecondsPerPoint`) — the same
points curse kills pay, converted at the same escalating rate. At the 100-point
cap that is one RP every ~8 minutes: a floor for a player far from the curse,
never a substitute for fighting it.

The Temple is the religion layer's root: no Temple, no chapels, no sect
powers, no sect heroes. If it falls, chapels stand but their passives go dark
(Sects.md §1, "live only while the Temple stands").

## 3. Chapels

**A chapel is how a sect is adopted.** It docks to a free Temple face; the sect
building slot of Sects.md §1 is this chapel — there is no separate sect
building any more.

| | |
|---|---|
| Adoption (build the chapel) | **2 RP** if the sect's cluster matches your culture (affinity), **3 RP** otherwise. **Before age-up there is no culture, so every chapel is 3 RP.** |
| Limit | six chapels, one per sect (double chapels are retired) |
| On completion | the sect's **Passive**, its **Research** (bought at the chapel with resources) and its **first active power** |

### 3.1 Powers and levels

| Purchase | Price |
|---|---|
| Unlock the second active (the other counterpart) | **1 RP** |
| Unlock the wildcard | **2 RP** |
| Chapel level II | **2 RP** |
| Chapel level III | **3 RP** |

**A chapel's level is the level of every power it has unlocked** (I / II /
III). A power unlocked later arrives at the chapel's current level. This
replaces Sects.md §3: the level is bought, not a reward for early adoption.

**A fully developed sect costs 11 RP with affinity** (2 + 1 + 2 + 2 + 3 + the
1 RP hero), 12 without. Six full sects is ~70 RP — a real match develops two
or three deeply, or spreads thin. That is the choice this layer asks for.

## 4. Sect heroes

**Every sect's unit is now a hero.** It replaces the sect unit of Sects.md §1
(which was capped at 5).

| | |
|---|---|
| Recruit | at its chapel, **1 RP** + the unit's resource price |
| Limit | **one per sect** |
| Levels | 1-10 from kills, exactly as [Heroes.md](Heroes.md) |
| Death | Recruited again at its chapel for its resources only — revival never costs RP. **As implemented (2026-09-29):** it returns at level 1; the Heroes.md §4 Rally-the-Oath / Full-Honours choice is still King Lexor's alone (`HeroRevival` is keyed one hero per faction) |

## 5. Knobs

`ptsPerKill` (per curse unit id), `ptsBase` 40, `ptsStep` 15, `ptsCap` 100,
`titheCost`, `titheStep` 1.5, `templeRpCost` 1, `chapelRpAffinity` 2,
`chapelRpOther` 3, `unlockSecondRp` 1, `unlockWildcardRp` 2, `chapelLevel2Rp` 2,
`chapelLevel3Rp` 3, `heroRp` 1.

## 6. Watch in playtest

- **The curse is a farm by design.** Kills pay; the curse's garrisons regrow and
  grow. The user's call (2026-09-29): it is only a farm if you can win the
  fights, and there are no rites or Backlash left to exploit. If farming
  dominates, cut `ptsCap` returns or rotate the per-unit pts, not the model.
- **Distance to the curse decides early religion.** Random and intended; Tithe
  is the safety valve.
