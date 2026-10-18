# Religion

> **2026-10-15:** adoption is instant and final, the chapel is decoration, each
> sect has a separate **sect building** (built by Workers, needs a standing
> Temple), B and ★ unlock independently, a sect has a trainable **unit
> (limit 20)** and a unique **hero**, and complete sets of sects earn **set
> passives** (§2, §3, §4). The religion menu that drives all of it:
> [Religion_Menu.md](Religion_Menu.md).

> **2026-10-09:** every religion point any player earns also counts down
> the global "religion points until the Shardroot descends" counter
> ([Curse_And_Shardroot.md §3.1c](Curse_And_Shardroot.md)).

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
is what pays for the Temple. (Feraldis, from age-up: every kill and every
sacrifice pays points too — §7.)

**The lobby can start you with RP** (2026-10-16). The starting-resources
option has four levels — LOW / NORMAL / HIGH / MAX — and every level but
LOW puts some religion points in the bank at the first tick, so a NORMAL or
richer start can raise the Temple without first hunting the curse. The amount
per level is data on `Scripts/Bootstrap/EconomyBootstrap.asset`, beside the
starting bank.

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

The Temple is the religion layer's root (2026-10-15). **While a faction has
no standing Temple** — none built yet, or destroyed:

- no sect can be adopted;
- **no sect building can be built or rebuilt** (§3.2);
- **no sect power can be cast**.

Nothing already bought is lost: adoptions, power unlocks and chapel levels
survive the Temple's fall and work again the moment it is rebuilt. Set
passives (§3.3) and sect-building effects are untouched by it.

## 3. Chapels

> **Rewritten 2026-10-15** (developer decisions, final): adoption is instant,
> the chapel is decoration, and the sect building is a separate building.
> Supersedes the 2026-09-29 "the chapel docks to a Temple face and IS the
> sect building".

**Adopting a sect is instant.** The player pays the adoption RP and the sect
fills one of six slots at once; a chapel appears on the Temple as
**decoration only** — it has no HP, no build time and no role in play.

| | |
|---|---|
| Adoption | an RP price that is **cheaper if the sect's cluster matches your culture** (affinity) than otherwise. **Before age-up there is no culture, so every sect is at the non-affinity price.** Values: `SectConfig.AdoptCostSameCulture` / `AdoptCostCrossCulture`. No resource cost. |
| Requires | a standing Temple (§2) and a free slot |
| Limit | **six sects**, one slot each |
| On adoption | the sect's **first active power** (A) at chapel level I, and the right to build its **sect building** (§3.2) |
| Final | **every choice is final.** A sect cannot be abandoned; nothing is ever refunded; nothing bought is lost to any destruction |

### 3.1 Powers and levels

Each of these is bought with RP (prices in §5), from the religion menu:

| Purchase | Config value |
|---|---|
| Unlock power B | `unlockSecondRp` |
| Unlock the wildcard ★ | `unlockWildcardRp` |
| Chapel level II | `chapelLevel2Rp` |
| Chapel level III (after II) | `chapelLevel3Rp` |

**B and ★ are independent purchases** (2026-10-15): either can be bought as
soon as the sect is adopted, in either order. The old sequential tiers
(power 2 before power 3) are gone.

**A chapel's level is the level of every power it has unlocked** (I / II /
III). A power unlocked later arrives at the chapel's current level. This
replaces Sects.md §3: the level is bought, not a reward for early adoption.

### 3.2 Sect buildings

Every sect has **one sect building** (Sects.md names it), separate from the
Temple:

- **Built by Workers anywhere on legal build ground**, from the Worker's build
  palette (Religion category), once its sect is adopted. Resource cost and
  build time are on its SO. **Limit one per sect.**
- **Requires a standing Temple to be built or rebuilt** (§2). A standing sect
  building keeps working while the Temple is down.
- It **trains the sect unit** (§4), **sells the sect research** and
  **recruits the sect hero** (§4).
- While it stands it gives the sect's **standing effect** (the sect's
  passive, Sects.md).
- If destroyed, the player must rebuild it. The sect, its powers and levels
  are unaffected; only what the building itself provides pauses.

### 3.3 Set passives

A faction that holds a **complete set** of sects earns that set's passive,
for as long as it holds them (adoption is permanent, so in practice for the
rest of the match). With six slots there are exactly two kinds of set, and
the cap makes them exclusive:

| Set | Requires | Passive |
|---|---|---|
| Alanthor | all four Alanthor-cluster sects | **Sovereign Hour** |
| Feraldis | all four Feraldis-cluster sects | **The Breaking Hour** |
| Runai | all four Runai-cluster sects | **Foreknown** |
| Concord | two sects from each cluster | **Concord** |

A set counts the sects, not the adopter's culture. What each passive does is
in Sects.md §6b. Destruction never switches a set passive off.

**A fully developed sect is expensive on purpose** (adoption + both unlocks +
both levels + its hero): six full sects cost far more RP than a match pays, so
a real match develops two or three deeply, or spreads thin. That is the choice
this layer asks for.

## 4. Sect units and sect heroes

> **Rewritten 2026-10-15:** a sect has BOTH a trainable unit and a unique
> hero. Supersedes 2026-09-29's "every sect's unit is a hero".

| | Sect unit | Sect hero |
|---|---|---|
| Trained / recruited at | the sect building | the sect building |
| Price | the unit's resource price (its SO) | an RP price (`heroRp`) + the hero's resource price (its SO) |
| Limit | **20 per faction**, live and queued | **one** (unique) |
| Levels | — | 1-10 from kills, exactly as [Heroes.md](Heroes.md); abilities unlock at levels 1, 3 and 6 |
| Death | train another | recruited again at the sect building for its resources only — revival never costs RP. It returns at level 1 (the Rally-the-Oath / Full-Honours choice is King Lexor's alone) |

Sect units are offensive by design. Their kits, and the heroes', are in
Sects.md.

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
sect buildings, sect units, sect heroes, Temple research) are on their SOs.
Adoption has no resource price.

## 6. Watch in playtest

- **The curse is a farm by design.** Kills pay; the curse's garrisons regrow and
  grow. The user's call (2026-09-29): it is only a farm if you can win the
  fights, and there are no rites or Backlash left to exploit. If farming
  dominates, cut `ptsCap` returns or rotate the per-unit pts, not the model.
- **Distance to the curse decides early religion.** Random and intended; Tithe
  is the safety valve.

## 7. Feraldis — drawing blood (2026-10-13, supersedes 2026-10-12)

From age-up a Feraldis faction's religion runs on **drawing blood**
([Age_1_Feraldis.md §5](Age_1_Feraldis.md)). Every source below banks
points, converted to RP through the **same escalating ladder as §1**
(`ptsBase`, `ptsStep`, `ptsCap`), and **none of them needs an Altar or a
Temple** — a Feraldis faction with 0 RP and no Altar can kill and sacrifice
its way to the Altar's `templeRp`.

> **Why (developer, 2026-10-13):** the 2026-10-12 rule paid Feraldis curse
> kills in veilstone only and made Altar sacrifice the one point source, and
> the Altar costs RP — a Feraldis player with no RP at age-up was soft-locked
> out of the whole religion layer. Blood in any form now pays.

- **Every enemy unit a Feraldis faction kills pays points** — the last hit
  is paid, as in §1, whatever dealt it. Points scale with the victim's whole
  cost (`killPtsPerCost`, at least `killPtsMin`, `FeraldisEconomy.asset`).
  Only hostile units count (`Alliances.AreHostile`): an ally or a teammate
  pays nothing, nor does a unit its own side sacrificed or melted.
- **Curse kills pay both**: the §1 per-unit points (`ptsCrystalling` …, with
  the standing-Temple bonus — the Altar is the Temple) **and** the Feraldis
  veilstone (Age_1_Feraldis.md §3.6). Destroying a curse node still pays its
  full Religion Point (§2) and the Feraldis node bounty.
- **Every sacrifice pays points** scaled by the unit's cost
  (`sacrificePtsPerCost`) — the Altar sacrifice **and** the slave's field
  sacrifice, which can be made anywhere and still leaves its blood pool.
- **A sacrifice made at the Blood Altar counts `altarSacrificeMultiplier`
  times**: an Altar sacrifice always, a field sacrifice when the
  slave stands within `altarSacrificeRadius` of one of the faction's finished
  Altars. Heroes cannot be sacrificed.
- The Temple's slow point trickle (§2) and the Tithe (§1.1) stand.
- Every point a Feraldis faction earns counts down the Shardroot counter like
  anyone's.
