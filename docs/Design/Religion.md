# Religion

> **Doc version: 2026-09-29. Canon for religion points, the Temple, chapels and
> sect heroes.** Supersedes:
>
> - [Overview.md](Overview.md) — the old per-age RP awards, the old adoption
>   price, double chapels and "sects upgrade with the Temple".
> - [Sects.md](Sects.md) §1 Building / Unit slots and §3 (power level from
>   adoption timing). Sects.md's kits, radii, cooldowns and damage ladder
>   (§2, §2b, §3b-§6) STAND.
> - [Age_0.md](Age_0.md) — **the Shrine of Ridan is gone** (purged 2026-10-03):
>   the Temple of Ridan holds the Litharch and the heal ladder.
> - [Heroes.md](Heroes.md) "sect unique units are not heroes" — **they are now.**
>
> **Where the numbers are (2026-10-03).** This doc states the rules. Every
> value — points per kill, the ladder's parameters, the Tithe's price and
> step, every RP price — is a config value or an SO cost, named in §5. On a
> conflict the asset wins.

---

## 1. Religion points come from the curse

**Killing curse units earns points (pts).** Points convert to **Religion
Points (RP)**, the only currency of the religion layer.

Each curse unit pays its own pts (`ptsCrystalling`, `ptsVeilstinger`,
`ptsGodsplinter` in the religion config, §5): the bigger the curse unit, the
more it pays.

**The last hit decides who is paid**, whatever dealt it — a soldier, a tower, a
sect power, a hero. An ally's last hit pays the ally.

**The first points are cheap.** The pts needed for your next RP rise with the
RP you have already earned from kills, then level off:

`pts for RP #n = min(ptsBase + ptsStep × (n − 1), ptsCap)`

`ptsBase`, `ptsStep` and `ptsCap` are config values (§5). The shape is the
rule: a cheap first RP, a linear climb, then a flat cap.

Points accrue from the first tick, **before any Temple exists**: the first RP
is what pays for the Temple.

### 1.1 Buying RP

Players who start far from the curse are not locked out: the Temple sells RP
through its research **Tithe**, repeatable, 1 RP per purchase.

| | |
|---|---|
| First purchase | a supplies + iron + veilstone price (`titheSupplies`, `titheIron`, `titheVeilstone`) → 1 RP |
| Each later purchase | the previous price × `titheStep` (per faction, rounded) |

Buying is always worse than fighting after the first couple of purchases — on
purpose.

## 2. The Temple of Ridan

| | |
|---|---|
| Age | **0** |
| Cost | an RP price (`templeRp`, §5) plus the resource cost on the Temple SO |
| Limit | **one per faction** |
| Levels | **none** — the Temple is never upgraded; chapel levels are bought with RP (§3.1). Any rule gated on a Temple level is void. |
| Trains | **Litharch** |
| Research | Tithe (§1.1), the heal ladder (Heightened → Pious → Fervored Masses) and Warrior Priests. The only level gate a tech has is its SO's `minBuildingLevel`; **Fervored Masses is priced in veilstone only** (no veilsteel in Age 0 costs). Prices on the tech SOs. |
| Chapel sites | **six** — the six non-door faces of the seven-sided cathedral (Overview.md) |

**Destroying a curse node pays a full Religion Point** (2026-09-29) to the
faction that lands the last hit — straight to the balance, not through the
points ladder, and not boosted by the Temple.

**A standing Temple also boosts the curse harvest** (2026-09-29): while a
faction's Temple is finished and alive, every curse kill pays it a percentage
bonus in points (`templeKillBonusPct`).

**The Temple produces religion slowly** (2026-09-29): while it stands it pays
its faction one point every `templeSecondsPerPoint` seconds — the same points
curse kills pay, converted at the same escalating rate. At the ladder's cap it
is a floor for a player far from the curse, never a substitute for fighting
it.

The Temple is the religion layer's root: no Temple, no chapels, no sect
powers, no sect heroes. If it falls, chapels stand but their passives go dark
(Sects.md §1, "live only while the Temple stands").

## 3. Chapels

**A chapel is how a sect is adopted.** It docks to a free Temple face; the sect
building slot of Sects.md §1 is this chapel — there is no separate sect
building any more.

| | |
|---|---|
| Adoption (build the chapel) | an RP price that is **cheaper if the sect's cluster matches your culture** (affinity) than otherwise. **Before age-up there is no culture, so every chapel is at the non-affinity price.** Values: `SectConfig.AdoptCostSameCulture` / `AdoptCostCrossCulture`; the chapel's resource cost is on its SO. |
| Limit | six chapels, one per sect (double chapels are retired) |
| On completion | the sect's **Passive**, its **Research** (bought at the chapel with resources) and its **first active power** |

### 3.1 Powers and levels

Each of these is bought with RP (prices in §5):

| Purchase | Config value |
|---|---|
| Unlock the second active (the other counterpart) | `unlockSecondRp` |
| Unlock the wildcard | `unlockWildcardRp` |
| Chapel level II | `chapelLevel2Rp` |
| Chapel level III | `chapelLevel3Rp` |

**A chapel's level is the level of every power it has unlocked** (I / II /
III). A power unlocked later arrives at the chapel's current level. This
replaces Sects.md §3: the level is bought, not a reward for early adoption.

**A fully developed sect is expensive on purpose** (adoption + both unlocks +
both levels + its hero): six full sects cost far more RP than a match pays, so
a real match develops two or three deeply, or spreads thin. That is the choice
this layer asks for.

## 4. Sect heroes

**Every sect's unit is now a hero.** It replaces the capped sect unit of
Sects.md §1.

| | |
|---|---|
| Recruit | at its chapel, for an RP price (`heroRp`) + the unit's resource price (its SO) |
| Limit | **one per sect** |
| Levels | 1-10 from kills, exactly as [Heroes.md](Heroes.md) |
| Death | Recruited again at its chapel for its resources only — revival never costs RP. **As implemented (2026-09-29):** it returns at level 1; the Heroes.md §4 Rally-the-Oath / Full-Honours choice is still King Lexor's alone (`HeroRevival` is keyed one hero per faction) |

## 5. Knobs

All but two live in the religion config,
`Assets/Scripts/Economy/FactionReligionPoints.asset`
(`FactionReligionPointsConfig`):

`ptsCrystalling`, `ptsVeilstinger`, `ptsGodsplinter`, `templeSecondsPerPoint`,
`templeKillBonusPct`, `ptsBase`, `ptsStep`, `ptsCap`, `titheSupplies`,
`titheIron`, `titheVeilstone`, `titheStep`, `templeRp`, `unlockSecondRp`,
`unlockWildcardRp`, `chapelLevel2Rp`, `chapelLevel3Rp`, `heroRp`.

The two that are not: the chapel adoption RP prices are code constants,
`SectConfig.AdoptCostSameCulture` / `AdoptCostCrossCulture`
(`Assets/GameData/TechTree/Sects/SectConfig.cs`). Resource prices (Temple,
chapels, sect heroes, Temple research) are on their SOs.

## 6. Watch in playtest

- **The curse is a farm by design.** Kills pay; the curse's garrisons regrow and
  grow. The user's call (2026-09-29): it is only a farm if you can win the
  fights, and there are no rites or Backlash left to exploit. If farming
  dominates, cut `ptsCap` returns or rotate the per-unit pts, not the model.
- **Distance to the curse decides early religion.** Random and intended; Tithe
  is the safety valve.
