# Veilstone Economy

> **2026-11-09 revision ([decision 42](Unification_Decisions_2026-10-03.md)) — read this
> first; it overrides everything below that disagrees.**
>
> - **Veilsteel no longer exists.** Veilstone is the only crystal. Wherever this doc
>   says veilsteel is made, forged, bought, sold, paid or spent, read veilstone: every
>   price converted at 2 veilstone per veilsteel, and every source pays veilstone. The
>   Veilsteel Survey is cut. The lore of §1 stays background (lore-only, no mechanic).
> - **Alanthor keep and build Veilstone Mines.** The age-up conversion to Trading
>   Outposts is gone (§3.1's first row and §4's `ConvertMinesForCulture` are void).
> - **The Trading Outpost (§3.1) is rewritten — see §3.1 below.**
> - **Stop extracting** (§3.5): every extractor can stop drawing on its node.

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
| ~~**Depleted**~~ **Collapsed** (2026-10-18) | Mined out — **gone** | Its reserve reached zero: the outcrop is removed from the map with every building working it (Territory_Claims.md § 11.3) |

**Revised 2026-10-18:** an outcrop has a **finite** reserve and no lifetime;
when it is fully extracted it **collapses** — removed, together with the
Veilstone Mine or Sanctuary on it and the Trading Outposts on its side slots.
There is no Depleted outcrop any more, so nothing below that refills, trades
at or builds on a Depleted outcrop applies (struck through where it appears).

- **An empty node pays a small trickle** of its resource to whoever holds its
  ground (the empty-slot rate in `TerritoryIncomeSystem`); built on, it pays
  its extractor's rate instead (§5). A Cursed or Depleted outcrop
  pays nothing.
- ~~**The curse replenishes.**~~ *(void since 2026-10-18: a spent outcrop
  collapses, so there is nothing to refill.)* When the curse raises a node on a depleted
  outcrop, the outcrop's reserve refills to full. Pacify it later and it is
  a full Inactive node again. **The refill is capped by the node lifetime**
  (Territory_Claims.md § 11.3, 2026-10-08): it restores only what the
  lifetime still allows, so after minute 30 taking an outcrop restores
  nothing — a spent outcrop stays Depleted, and its Trading Outposts keep
  trading beside it.
- **Destroying a curse node pacifies the outcrop** for everyone: it returns to
  Inactive. Who destroyed it decides what else happens (§3).

State is derived, not authored: an outcrop with a live curse node on it is
Cursed; otherwise it is Inactive (an outcrop whose reserve is spent no longer
exists, 2026-10-18).

## 3. Per-culture interaction

| | Inactive node | Cursed node | ~~Depleted node~~ (collapsed since 2026-10-18 — the column is void) |
|---|---|---|---|
| **Feraldis** | **Mine** on it (slave-staffed): fast veilstone, drains the node | Destroy the curse node → **veilstone + veilsteel bounty** | nothing |
| **Runai** | **Sanctuary** on it: religion points, never drains it | **Sanctuary** on it with Consecrated Ground (even in curse-held ground): the node sleeps, more religion points | nothing |
| **Age 0 (no culture)** | **Veilstone Mine** on it | Destroy the curse node → pacified | nothing |
| **Alanthor** | **Veilstone Mine** on it; after Trade Charters, up to four **Trading Outposts** beside it (any node, §3.1) | Destroy the curse node → pacified (back to Inactive) | Up to four **Trading Outposts** beside it |

### 3.1 Alanthor — the Trading Outpost (rewritten 2026-11-09, decision 42)

The Outpost is Alanthor's late-game answer to nodes that run dry. It does not trade:
it **trickles the resource of the node it stands beside, without ever drawing on that
node**.

| | |
|---|---|
| **Unlock** | Research **Trade Charters** at the Fortress, gated to Fortress L3 (the tech SO's `minBuildingLevel`). Before it, the Outpost cannot be built. |
| **Alanthor mine veilstone** | Alanthor build Veilstone Mines like every other culture, and keep them at age-up. The early and mid game is extraction; the Outpost is what comes after. |
| **Placement** | On one of the four side slots (N, E, S, W) of **any resource node**: supply, iron or veilstone ([Build_Grid.md § The Trading Outpost's side slots](Build_Grid.md)). At most one post per side, four per node. Never beside a Cursed outcrop. A side the extractor or the ground blocks is skipped. The ghost snaps to the nearest free side of the nearest node. |
| **What it pays** | That node's resource, at the trickle rate for that resource on `TradingOutpostSystem.asset` (`trickleSuppliesPerMinute` / `trickleIronPerMinute` / `trickleVeilstonePerMinute`), times the node's purity and the post's level (`tradeRateMultiplier` on `TradingOutpost_Lvl1..3`). It spends nothing. |
| **Only while the node lives** | It pays only while its node exists, is not Cursed, and lies in ground its owner holds. **The trickle is never drawn from the node**, but whatever else works the node still is: when the node is spent it collapses (Territory_Claims.md § 11.3) and takes every post beside it. |
| **It replaces the empty-node trickle** | A node with a finished post of its holder beside it pays no empty-node trickle, so an unworked node stops wearing down once it has a post. |
| **The decision** | Harvest the node to its last units with the extractor, or **stop extracting** (§3.5) in time and keep the posts paying forever. The node's reserve bar shows what is left. |
| Cost, build time | On the `Alanthor_TradingOutpost` SO: the price of the **first** post beside a node. |
| **Per-node cost ramp** | Every further post beside the **same** node costs more (`outcropRampMultipliers` on `TradingOutpostSystem.asset`); the first post beside a new node is base price again. Counted as before (posts standing there plus the builder's own plans), fixed when ground is broken. |
| **Levels 1-3** | `TradingOutpost_Lvl1..3`, faction-wide like every building level. A level raises the trickle (`tradeRateMultiplier`) and HP. |
| Locks territory | Yes, like an extractor (Territory_Claims.md §3). |
| **No trade** | No trade menu, no routes, no Hold, no Buy / Forge / Sell. Trade Agreements, Swift Caravans and Veilsteel Forging / Export are deleted. Resources are bought and sold at the Temple (Tithe, and its resource trade — Monuments_And_Petriarchy.md). |
| Its node turns Cursed | Every post beside it idles until the node is pacified (or the territory is retaken). |

### 3.5 Stop extracting (2026-11-09)

Every extractor — the Gatherer's Hut line (Gatherer's Hut, Guild, Work Camp, Trading Post),
the Mine and the Veilstone Mine, every culture — carries a **Stop extracting** toggle (the
selection panel, and the `SetExtractorStopped` lockstep order). A stopped extractor:

- draws nothing from its node and pays no slot income;
- still stands, holds and **locks** its territory, and still pays every survey on it (the
  Guild surveys, the Supply Surveys), which are drawn from no node;
- turns its node back into an empty node: it pays the empty-node trickle (and drains by it)
  unless a finished Trading Outpost of its holder stands beside it.

Toggle it again to resume. Delete still works, and is the other way to stop.

### 3.2 Feraldis

**Superseded in detail by [Age_1_Feraldis.md](Age_1_Feraldis.md) §3-4 (2026-10-12).** In short:

- **Veilstone Mine** on an Inactive outcrop, **staffed by slaves**: veilstone
  per mine level and crew, Feraldis scaled up, and every unit paid is drawn
  from the outcrop's reserve — each slave drains it faster. A spent outcrop
  collapses with the Mine on it (2026-10-18).
- **The curse pays veilstone**: a curse unit Feraldis kills pays veilstone
  (not religion points); a destroyed curse node pays **veilstone and
  veilsteel** (`curseNodeVeilstone`, `curseNodeVeilsteel` in
  `FeraldisEconomy.asset`; the old `feraldisNodeVeilsteel` is folded into it).
- ~~**Veilsteel is forged** at the Great Forge~~ (2026-11-09, decision 42):
  the Great Forge's former Veilsteel bay turns **iron into veilstone**; its
  other bays turn slaves into veilstone and export veilstone for iron.
- **Raiding**, not trade, is the late-game supply: the raze bounty, pillage
  on kills and the siphon while attacking an enemy extractor, Outpost or
  Fortress.

### 3.3 Runai

(2026-10-13 — rules in [Age_1_Runai.md §5](Age_1_Runai.md).)

- **The Runai never mine veilstone.** Their Age 0 Veilstone Mines become
  **Sanctuaries** in place at age-up, and they can build no new Veilstone Mine.
- **Sanctuary** atop an Inactive outcrop, or — with **Consecrated Ground** —
  a **Cursed** one (allowed even in ground the curse holds; the curse node
  under it sleeps and raises no garrison). It never drains the outcrop and
  **produces only religion points** (more on a cursed outcrop).
- **Veilstone is bought at the Sanctuary with religion points** (the trade
  action, `RunaiSanctuarySystem.asset`; it sold veilsteel until 2026-11-09); **veilstone** comes only from the
  **Waystation road tax** paid by passing caravans (Age_1_Runai.md §3.2).
  A territory's veilstone / veilsteel income is not hauled.
- The Sanctuary hosts the curse ladder (Veiled Pilgrims → Voice of the Veil).

### 3.4 The curse

- Produces units that attack **everyone equally**.
- ~~Takes a Depleted outcrop and replenishes it (§2).~~ *(void 2026-10-18:
  spent outcrops collapse.)*
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

On top of these, **the Guild surveys** (§5.1) pay every resource but
supplies from upgraded Guilds, from no node — the late-game income a
developed Alanthor hut ring earns.

Who pays, and where the number lives — the doc does not restate the rates
(decisions 9, 10 and 34):

| Source | Pays | The number lives on |
|---|---|---|
| **The capital** (Shelter / Fortress) | its **own SO income** — supplies on a fixed interval. **Only the capital pays (2026-11-04): the faction's oldest standing Fortress; any other Fortress holds and locks ground and trains, but earns nothing, and so does its level** (`CapitalIncome`). There is **no separate territory supply for the capital** any more: `TerritoryIncomeSystem`'s extra flat capital line was dropped (decision 10) | `Fortress.asset` (`suppliesPerTick` / `suppliesInterval`) |
| **The Fortress level** (2026-10-08) | multiplies **that Fortress's own supplies income** and nothing else — it no longer scales the territory's slots (that was the mid-game iron spike). The share above L1 is paid by the territory tick while the ground is held | `TerritoryIncomeSystem.asset` (`fortressLevelIncomeMultipliers`) |
| **Territory claim** (2026-10-08) | every held territory pays its holder a flat amount of supplies, iron and veilstone, nodes or not, **once the holder has aged up** — **Feraldis claims pay no supplies** (2026-10-12, Age_1_Feraldis.md §3.5) (claims open at age-up, so the Age 0 opening is unchanged). It never runs dry: after minute 30 it is what ground is worth | `TerritoryIncomeSystem.asset` (`claim*PerMinute`) |
| **Empty slot** (supply / iron / uncursed veilstone node in held ground) | a small trickle of its resource | `TerritoryIncomeSystem` |
| **Gatherer's Hut** on a supply slot | its **slot-income ladder**, one rate per level — and nothing else: the construction safety-net income is gone (decision 9) | `GatherersHut.asset` `slotIncomePerMinute` |
| **Mine** on an iron slot | its slot-income ladder, raised by the Mine research (§6) and, after age-up, by its **level** (L1-L3, faction-wide like every building level, every culture — 2026-10-19) — not by the surveys or the Fortress level | `Mine.asset` `slotIncomePerMinute` (Age 0); after age-up `Civs/<Culture>/Buildings/Mine/Mine_Lvl1..3` |
| **Veilstone Mine** on a veilstone slot | its slot-income ladder (Feraldis scaled up, drains the outcrop) | `VeilstoneMine.asset` |
| **After age-up** (every culture): the cultured Gatherer's Hut (Guild / Work Camp / Trading Post) and the Mine | the culture's level SOs' own slot income | `Civs/<Culture>/Buildings/<Guild\|WorkCamp\|TradingPost>/…_Lvl1..3`, `Mine/Mine_Lvl1..3` |
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
supply node is spent: it is the late-game Guild income. **A Guild is never
destroyed with its node (2026-10-19)**: when the supply node collapses the
Guild stays standing, Depleted, and its surveys keep paying
(Territory_Claims.md § 11.3). It is paid by the
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

### 5.2 ~~Supply Surveys — the supply floor (2026-10-20)~~ — REMOVED 2026-11-09 (developer)

The Supply Survey I-III techs (Alanthor Guild, Runai Trading Post) are cut. Node reserves were cut at the same time (`supplyNodeReserve` / `ironNodeReserve` / `veilstoneNodeReserve` on `TerritoryIncomeSystem.asset`) so nodes deplete within the match; the late-game income that outlives them is the Trading Outpost (§3.1). The text below is history.


Developer: "Supplies need to have a stable income value, otherwise AI players
stall hard and get defeated by the curse. Fix: Supply Surveys technology
(produces increasing amounts of supplies)." Supplies came almost entirely from
supply nodes, every unit paid drawn from a finite reserve, so a hut ring went
dry and its owner stalled. Two changes:

- **Node reserves raised** (supply, iron and veilstone; the values are
  `supplyNodeReserve` / `ironNodeReserve` / `veilstoneNodeReserve` on
  `TerritoryIncomeSystem.asset`, still scaled by purity, § 11.2 of
  Territory_Claims.md).
- **Supply Survey I-III** — three tiers researched at the hut (the cultured
  Gatherer's Hut) by **Alanthor (the Guild) and Runai (the Trading Post)**,
  gated to hut L1 / L2 / L3, each requiring the previous. Each completed tier
  makes **every hut** of the faction in a held territory pay a flat supply
  trickle a minute, scaled by the hut's level — the same shape as the Guild
  surveys (§ 5.1). It is **drawn from no node**, so a **depleted** hut keeps
  paying it: the floor that outlives the nodes. Runai supply from outside the
  home territory goes to the depot as all its supply does (Age_1_Runai.md).
  **Feraldis has no Supply Survey**: its late-game supplies are raiding
  (Age_1_Feraldis.md § 3.5). The per-tier rate is `supplySurveyPerTier` on
  `TerritoryIncomeSystem.asset`, scaled by `guildSurveyLevelScale`; costs and
  times are on the tech SOs (`Civs/Alanthor/Buildings/Guild/Research/SupplySurvey1-3`,
  `Civs/Runai/Buildings/TradingPost/Research/Runai_SupplySurvey1-3`).

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
