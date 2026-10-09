# Veilstone Economy

> **Doc version: 2026-10-08. Canon for where veilstone and veilsteel come
> from.** Numbers (prices, trade rates, income ladders) live on the SOs and in
> `TradingOutpostSystem.asset`; on a conflict they win (2026-10-03,
> [Unification decisions](Unification_Decisions_2026-10-03.md)). Supersedes:
>
> - [Territory_Claims.md](Territory_Claims.md) / [Regions.md](Regions.md) §4:
>   **a veilstone node no longer pays its territory's owner just for being
>   held** (the old node trickle is gone for veilstone), and **veilsteel
>   deposits are removed from the map**.
> - [Age_0.md](Age_0.md) § Mine: the iron Mine no longer leaks veilstone from
>   nearby nodes. Every faction may still build Mines and Veilstone Mines in
>   Age 0 (before culture); at age-up an Alanthor faction's Veilstone Mines
>   become Trading Outposts and step off the outcrop onto one of its sides
>   (§3.1). Iron Mines are every culture's.
> - [Religion.md](Religion.md) is unchanged for Alanthor. The Feraldis and
>   Runai religion rules below are the target for those cultures' passes.

---

## 1. The lore it encodes

Veilstone is the curse's crystal. The curse is a **neutral** defence
mechanism of this world's god, out of control and corrupted. It sides with
no one.

| Culture | Relationship to the curse |
|---|---|
| **Feraldis** | Live on the border itself, between it and the sea. Mine it aggressively and suffer for it. Sell what they mine; depend on veilsteel weapons; want the curse destroyed. |
| **Runai** | The curse is sacred. They do not mine it, but they secretly use it: only they know how to make **veilsteel**, and it is their main export. Where they live the curse sleeps. |
| **Alanthor** | Live away from it and profit from trading in it: buy veilstone, sell it, buy veilsteel. They want the curse's power for magical weapons. |

**Veilsteel is made, never mined.** There are no veilsteel nodes.

## 2. Veilstone nodes have three states

Every veilstone outcrop on the map is in exactly one state:

| State | Meaning | How it gets there |
|---|---|---|
| **Inactive** | Ordinary, uncursed crystal | Start state; a curse node on it is destroyed |
| **Cursed** | A curse node stands on it | The curse builds a node on it (Territory_Claims.md §6.3) |
| **Depleted** | Mined out | A mine drew its reserve to zero |

- **An empty node pays a small trickle** of its resource to whoever holds its
  ground (the empty-slot rate in `TerritoryIncomeSystem`); built on, it pays
  its extractor's rate instead (§5). A Cursed or Depleted outcrop
  pays nothing.
- **The curse replenishes.** When the curse raises a node on a depleted
  outcrop, the outcrop's reserve refills to full. Pacify it later and it is
  a full Inactive node again. **The refill is capped by the node lifetime**
  (Territory_Claims.md § 11.3, 2026-10-08): it restores only what the
  lifetime still allows, so after minute 30 taking an outcrop restores
  nothing — a spent outcrop stays Depleted, and its Trading Outposts keep
  trading beside it.
- **Destroying a curse node pacifies the outcrop** for everyone: it returns to
  Inactive. Who destroyed it decides what else happens (§3).

State is derived, not authored: an outcrop with a live curse node on it is
Cursed; otherwise an outcrop whose reserve is spent is Depleted; otherwise it
is Inactive.

## 3. Per-culture interaction

| | Inactive node | Cursed node | Depleted node |
|---|---|---|---|
| **Feraldis** | **Mine** on it: fast veilstone, drains the node | Destroy the curse node → **veilsteel bounty** | nothing |
| **Runai** | **Mine** on it: veilstone | **Sanctuary** on it: keeps the curse's units away, produces veilsteel, must be fed by a caravan from a Runai mine; produces religion | nothing |
| **Age 0 (no culture)** | **Veilstone Mine** on it | Destroy the curse node → pacified | nothing |
| **Alanthor** | Up to four **Trading Outposts** beside it | Destroy the curse node → pacified (back to Inactive) | Up to four **Trading Outposts** beside it |

### 3.1 Alanthor — the Trading Outpost

The Outpost simulates trade without caravans or a menu: it is a building on
the map, in contested ground, that anyone can burn.

| | |
|---|---|
| **Alanthor do not mine veilstone** | No Veilstone Mine once Alanthor (iron Mines stay — they are every culture's). **At age-up every Veilstone Mine the faction owns becomes the FIRST Trading Outpost of its outcrop** — same building (same entity, owner, health fraction and territory lock), but it **moves off the outcrop onto one of its four sides**: the free, buildable side nearest the faction's capital, ties broken north, east, south, west — a side in the outcrop's own territory first, one across a border only when no other is free. The other three sides can then be built. The mine's level is lost; the Outpost starts at its own Level 1. With no free side it stays on the outcrop (a fallback, logged). |
| Placement (2026-10-04, decision 40 — supersedes the on-outcrop placement of decision 28) | **Beside** an Inactive or Depleted veilstone outcrop, on one of its **four side slots — north, east, south, west** — the post's footprint flush against the outcrop's square and centred on its axis ([Build_Grid.md § The Trading Outpost's side slots](Build_Grid.md)). **At most one post per side, so at most four per outcrop.** The outcrop itself stays an impassable node nobody builds on. The placement ghost snaps to the nearest free side of an outcrop near the cursor and skips a side the ground refuses; it is red when every side is taken or blocked. Never beside a Cursed outcrop. Each post remembers the outcrop it trades at. Alanthor's Mine button and the Outpost's own button both raise it. |
| **Four posts, one outcrop** | This is how Alanthor's veilstone grows **without more veilstone slots on the map**: an outcrop can carry up to four times the trade it carried as a single Outpost — at a steeper price for every post beyond the first. |
| Cost, build time | on the `Alanthor_TradingOutpost` SO — the price of the **first** post beside an outcrop |
| **Per-outcrop cost ramp** | Every further post beside the **same** outcrop costs more — its build price AND every level-up it later buys are the base price times the ramp entry for its place (the 1st, 2nd, 3rd or 4th post there). The **first post beside a new outcrop is base price again**, which rewards taking more veilstone ground over stacking one outcrop. A post's place is counted from the posts already standing beside that outcrop (any owner, finished or not) plus the builder's own plans there, and is fixed when its ground is broken. The multipliers are `outcropRampMultipliers` in `TradingOutpostSystem.asset`. The build button names the ramp; the price charged is the one for the side the post snaps to; refunds hand back the price paid. |
| **Levels 1-3** | Like every Alanthor building, the Outpost has three levels, `TradingOutpost_Lvl1..3` (level SOs in `Civs/Alanthor/Buildings/TradingOutpost/`): L1 granted free (at age-up, or when a new post is finished), L2 and L3 bought — their prices follow the same per-outcrop ramp. **A level raises that post's trade rate** — what it spends and what it earns per minute alike, by the level SO's `tradeRateMultiplier` — and its HP. The HUD reads `Trading Outpost - Lvl N`. |
| Locks territory | Yes, like an extractor (Territory_Claims.md §3) |
| **Buy Veilstone** (default) | supplies + iron in, veilstone out |
| **Forge Veilsteel** (research: Veilsteel Forging) | veilstone in, veilsteel out |
| **Sell Veilsteel** (research: Veilsteel Export, after Forging) | veilsteel in, iron out (**no supplies since 2026-10-08** — late-game supplies come only from the Fortress, the Vault and territory claims, §5) |
| **Hold** (always available; 2026-10-05) | the post trades nothing — nothing spent, nothing earned — until set to a trade again. A bank that already holds more veilstone than it can use stops paying supplies and iron for more |
| **Trade Agreements I / II / III** (research, chained) | every trade's INPUTS cost progressively less |
| **Swift Caravans** (research, after Trade Agreements I; 2026-10-03) | every trade runs faster — Buy, Forge and Sell alike, inputs and outputs scaled together, on every Outpost the faction owns. The percentage is on the tech SO (`effectsList`, `TradeSpeed` on `building:Alanthor_TradingOutpost`) |
| Research | Hosted by the Outpost itself (`TradingOutpost/Research/`) |
| Can't afford a cycle | That cycle is skipped; nothing is spent |
| Its outcrop turns Cursed | **Every** post beside it idles until the outcrop is pacified |
| Each post trades on its own | Four posts round one outcrop are four trades — each runs its own recipe toggle, its own level and its own fractional carry. None of them is a slot extractor, so the outcrop's empty-slot trickle (§2) does not change with how many posts stand beside it |

The trade choice is the balancing act: every Outpost forging or selling is one
not buying veilstone — per post, so with four posts on an outcrop one can
forge while three buy. The trade runs on a short fixed cycle (`cycleSeconds`,
1 s since 2026-10-03, decision 28) with a per-Outpost fractional carry, so the
per-minute numbers are paid exactly. The Outpost does **not** drain the
outcrop; trade is not mining. Depleted outcrops left behind by Feraldis are as
good as fresh ones for an Outpost.

Every trade rate, the cycle and the Trade Agreements discounts live in
`TradingOutpostSystem.asset` beside the class (rates authored per minute); the
research prices, and the Swift Caravans speed-up, are on the tech SOs in
`TradingOutpost/Research/` (the asset only lists which techs are speed techs,
`speedTechs`). **The Buy Veilstone output was raised on 2026-10-03** (developer
directive, chosen over cutting unit prices: veilstone was the Alanthor army's
bottleneck — Game_AI.md §5a); its inputs were left unchanged, so the trade got
cheaper per veilstone as well as faster.

### 3.2 Feraldis

- **Veilstone Mine** on an Inactive outcrop: veilstone per mine level, ×1.5
  for Feraldis, and every unit paid is drawn from the outcrop's reserve. A
  spent outcrop is Depleted and pays nothing (no yield floor for veilstone).
- **Veilsteel**: destroying a curse node pays its last-hitter a veilsteel
  bounty if that faction is Feraldis (`feraldisNodeVeilsteel`).
- **Can raid Sanctuaries and Trading Outposts** *(target behaviour: not yet
  implemented).*
- **Religion points from war**: any kill pays points, curse kills pay more
  *(not yet implemented; today every culture uses Religion.md §1)*.

### 3.3 Runai

- **Veilstone Mine** on an Inactive outcrop: veilstone per mine level.
- **Sanctuary** on a Cursed outcrop *(not yet implemented)*: curse units are
  not spawned at or sent to it; produces veilsteel while a caravan from a
  Runai Veilstone Mine keeps it fed; Runai religion points come from
  Sanctuaries.

### 3.4 The curse

- Produces units that attack **everyone equally**.
- Takes a Depleted outcrop and replenishes it (§2).
- *Open item:* the curse still hunts the Shardroot holder
  (Territory_Claims.md §6.6). That is a reaction to holding a piece of it,
  not a preference for a faction, and is kept until decided otherwise.

## 5. Territory income (2026-10-01; data moved onto the SOs 2026-10-03)

**Superseded in timing (2026-10-09):** developer: "I need deaths to start occurring from minute 15 or even earlier", with ~45-minute 8-player matches. The late game now comes earlier (the minute is `allInAfterSeconds` on `SimpleAISystem.asset` and `nodeLifetimeMinutes` on `TerritoryIncomeSystem.asset`), and stalled factions are hunted before it ([Game_AI.md § 6o](Game_AI.md)). Read "minute 30" below as "the late game".

**The late game comes at 30 minutes (2026-10-08).** Developer: "Late game
comes at 30 minutes; from there onward players are expected to start
falling. Adjust all nodes to be spent by minute 30." Every node — supply,
iron, veilstone — is spent by then (Territory_Claims.md § 11.3), so the
economy has two halves:

| Resource | Early and mid game (the nodes) | Late game (after the nodes) |
|---|---|---|
| **Supplies** | Guilds / Gatherer's Huts on supply nodes, depleting them | **only** the Fortress's own income, the Vault and territory claims |
| **Iron** | Mines on iron nodes, depleting them | **only** trade, the Vault and territory claims |
| **Veilstone** | Trading Outposts (Alanthor) / Veilstone Mines on outcrops | **only** trade, the Vault and territory claims |
| **Veilsteel** | trade and the Vault | trade and the Vault |

On top of these, **the Guild surveys** (§5.1) pay every resource but
supplies from upgraded Guilds, from no node — the late-game income a
developed Alanthor hut ring earns.

Who pays, and where the number lives — the doc does not restate the rates
(decisions 9, 10 and 34):

| Source | Pays | The number lives on |
|---|---|---|
| **The capital** (Shelter / Fortress) | its **own SO income** — supplies on a fixed interval. There is **no separate territory supply for the capital** any more: `TerritoryIncomeSystem`'s extra flat capital line was dropped (decision 10) | `Fortress.asset` (`suppliesPerTick` / `suppliesInterval`) |
| **The Fortress level** (2026-10-08) | multiplies **that Fortress's own supplies income** and nothing else — it no longer scales the territory's slots (that was the mid-game iron spike). The share above L1 is paid by the territory tick while the ground is held | `TerritoryIncomeSystem.asset` (`fortressLevelIncomeMultipliers`) |
| **Territory claim** (2026-10-08) | every held territory pays its holder a flat amount of supplies, iron and veilstone, nodes or not, **once the holder has aged up** (claims open at age-up, so the Age 0 opening is unchanged). It never runs dry: after minute 30 it is what ground is worth | `TerritoryIncomeSystem.asset` (`claim*PerMinute`) |
| **Empty slot** (supply / iron / uncursed veilstone node in held ground) | a small trickle of its resource | `TerritoryIncomeSystem` |
| **Gatherer's Hut** on a supply slot | its **slot-income ladder**, one rate per level — and nothing else: the construction safety-net income is gone (decision 9) | `GatherersHut.asset` `slotIncomePerMinute` |
| **Mine** on an iron slot | its slot-income ladder, raised by the Mine research (§6) — not by the surveys or the Fortress level | `Mine.asset` `slotIncomePerMinute` |
| **Veilstone Mine** on a veilstone slot | its slot-income ladder (Feraldis scaled up, drains the outcrop) | `VeilstoneMine.asset` |
| **Alanthor** (after age-up): Guild (the Gatherer's Hut) and Mine | the Alanthor level SOs' own slot income | `Civs/Alanthor/Buildings/Guild/Guild_Lvl1..3`, `Mine/Mine_Lvl1..3` |
| **Alanthor Trading Outpost** (up to four beside one outcrop) | not a slot: each post runs its own trade every cycle, scaled by the faction's research and the post's level (§3.1) | `TradingOutpostSystem.asset` (rates), `Civs/Alanthor/Buildings/TradingOutpost/TradingOutpost_Lvl1..3` (`tradeRateMultiplier`) |

A built slot REPLACES its empty trickle, it does not add to it. Every slot,
empty or built, draws on its node and falls silent when the node is spent
(Territory_Claims.md § 11.3). **Neither the Fortress level nor the survey
research scales a slot any more (2026-10-08, developer: "Iron income still
soars in the middle of the match — reduce the upgrade power").** The iron
Mine's old patch income (`MineIncomeSystem`) is retired. **Every source pays
every second** — the territory tick and every Trading Outpost (fractional
carry keeps the per-minute numbers exact). **A resource the faction's trades
consume faster than it produces turns red in the resource bar**
(`TerritoryIncomeSystem.FactionNetForDisplay`).

Rule kept from 2026-10-01: **Mines pay more than huts** — an iron or
veilstone slot worked by a Mine out-earns a supply slot worked by a hut of the
same level, because the Mine is the scarcer, contested extractor.

### 5.1 The one exception: Guild Surveys (decision 17, 2026-10-03)

The rule of this document is that veilstone comes from outcrops (mined,
bought or pacified) and veilsteel is made. **The Alanthor Guild Surveys are a
deliberate exception:** researched at the Guild (the cultured Gatherer's Hut),
**Veilstone Survey I-II** make every Guild produce a veilstone trickle and
**Veilsteel Survey** a veilsteel trickle, on top of its supplies (the Iron
Surveying line does the same for iron). It is a research reward for a
developed hut ring, not a node — no outcrop, no curse interaction. Their gates
are the Guild levels on the tech SOs (Veilstone Survey I at L2, II at L3;
Veilsteel Survey at L3).

**How it pays (2026-10-08).** Each completed survey tier makes **every
Guild** (the built Alanthor Gatherer's Hut, any level) pay a flat amount of
that survey's resource a minute — Iron Surveying I-III iron, Veilstone
Survey I-II veilstone, Veilsteel Survey veilsteel — scaled by the Guild's
level. It is **drawn from no node** and so keeps paying after the Guild's
supply node is spent: it is the late-game Guild income. It is paid by the
territory tick only while the Guild's territory is held, and booked in the
match metrics as `guildSurvey`. A survey does **not** multiply the Mines or
Veilstone Mines (until 2026-10-08 it did, which is what made iron soar
mid-match while the surveys paid the Guilds nothing). The per-tier rates and
the level scaling are on `TerritoryIncomeSystem.asset`.

**Forests pay nothing and the Sawyer is gone (2026-10-01).** The armour
ladders live at the buildings that train what they protect — Plate (melee) at
the Barracks, Brigandine (ranged) at the Archery Range, Barding (cavalry) at
the Royal Stable, Plating (siege) at the Siege Yard — each gated by its host's
level. Their prices are on the tech SOs, and they are **not** uniform across
the ladders (decision 23: the upper tiers were raised).

## 6. The iron pass (2026-10-02)

> Two 30-minute 8-AI batches on Veilmarch ended with every faction sitting on a
> huge unspent supply bank and almost no iron: iron was the wall every economy
> hit. This pass moves iron off the things that are not military and pays more
> of it.

| Change | Rule (values on the SOs / configs) |
|---|---|
| **Start territory** | one more iron node than before (Territory_Claims.md §11 Start type) |
| **Iron yield** | every iron line pays a flat bonus — empty slots and Mines alike (`ironYieldMultiplier`, `TerritoryIncomeSystem.asset`; set back to no bonus on 2026-10-08) |
| **Mine** and **Veilstone Mine** cost | supplies and a little veilstone, **no iron** (decision 15) |
| **Palisade** hub | supplies only, no iron |
| **House (Hut)** | supplies only, its level-ups included |

**The Mine's own research.** Every Mine is a research host, and two techs
make the iron slots it works pay more. They replace each other, they do not
stack, and an empty slot is not mined so it does not benefit:

| Tech | At | Effect | Requires |
|---|---|---|---|
| **Deep Shafts** | Mine | raises Mine-worked iron slots | — |
| **Rich Seams** | Mine | raises them further (replaces Deep Shafts) | Deep Shafts |

Prices, times and percentages are on the tech SOs
(`Age0/Buildings/Mine/Research/`); the multipliers themselves, and the flat
iron yield bonus, are on `TerritoryIncomeSystem.asset` (toned down
2026-10-08, "reduce the upgrade power"). Since 2026-10-08 they are the ONLY
multipliers on a Mine's iron — the Fortress level and the Iron Surveying
ladder no longer scale it. The AI researches Deep Shafts right after Iron Surveying I
and Rich Seams after Iron Surveying II (Feraldis: after Iron Plunder /
Raiding II); its extractor walk reads the Mine's cost from the SO, so the
cheaper Mine needs no AI change of its own.

## 4. What changed in code (2026-10-01)

Implemented:

- `VeilstoneNodeState` on every outcrop, kept by `VeilstoneNodeStateSystem`
  (Inactive / Cursed / Depleted; refills the reserve when the curse takes it).
- `TerritoryIncomeSystem`: no veilstone or veilsteel node trickle; a Veilstone
  Mine pays per level from an Inactive outcrop and drains it to zero.
- Alanthor cannot build `VeilstoneMine` (`TerritoryOwnership.MayBuildMine`,
  enforced in `CheckPlaceBuilding`); `TradingOutpost.ConvertMinesForCulture` turns
  their Veilstone Mines into Outposts at age-up and in `StartAgePromoter`. Veilsteel deposits are no longer
  spawned; the iron Mine pays no veilstone.
- `Alanthor_TradingOutpost` with its trade/forge toggle (`SetOutpostMode`
  lockstep order), placement rule in `CommandRouter.CheckPlaceBuilding`, and
  AI siting and mode choice.
- Feraldis veilsteel bounty on curse-node kills.
- **2026-10-04 (decision 40):** the Outpost's four side slots
  (`TradingOutpost.SideSlot` / `TrySnapToSide`, reached through
  `TerritoryOwnership.TrySnapToNode`; `TerritoryOwnership.NodeStoodOnBy` keeps
  it off every node), the age-up move beside the outcrop
  (`ConvertMinesForCulture`), the per-outcrop ramp
  (`BuildCosts.For(..., position)` for the build price,
  `UpgradeBuildingCommandHelper` for level-ups, `TradingOutpostSite` records
  each post's outcrop, side and ramp place), the L1-L3 ladder
  (`TradingOutpost_Lvl1..3`, `TradingOutpostSystem.PerMinuteFor`), and the AI
  (`SimpleAISystem.Extractors.cs` sites one post per outcrop cheapest ramp
  first; `SimpleAISystem.Surplus.cs` levels posts while veilstone-held).

- **Income overlay** (`UI/World/IncomeOverlay`): hover ground you hold to see
  what that territory pays per minute, hover one of your buildings to see what
  it adds or spends per minute (green income, red spending). Synty
  Frame_Box_Medium_05 panels floating over the world like the claim bars, fed by
  `TerritoryIncomeSystem.ComputeYieldForDisplay` / `BuildingYieldForDisplay` —
  the same arithmetic the income tick pays.

Not yet implemented: Runai Sanctuary and caravan feeding, Feraldis raiding,
per-culture religion income. Veilsteel deposit **markers** still sit in five
map scenes (inert: nothing spawns from them); strip them on the next re-bake.
