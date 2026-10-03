# Veilstone Economy

> **Doc version: 2026-10-01. Canon for where veilstone and veilsteel come
> from.** Supersedes:
>
> - [Territory_Claims.md](Territory_Claims.md) / [Regions.md](Regions.md) §4:
>   **a veilstone node no longer pays its territory's owner just for being
>   held** (the 190/min node trickle is gone for veilstone), and **veilsteel
>   deposits are removed from the map**.
> - [Age_1_Alanthor.md](Age_1_Alanthor.md) § Smelter: the Smelter **no longer
>   generates veilsteel** and no longer stands on a node. It keeps its armour
>   research and is placed like any other building.
> - [Age_0.md](Age_0.md) § Mine: the iron Mine no longer leaks veilstone from
>   nearby nodes. Every faction may still build Mines and Veilstone Mines in
>   Age 0 (before culture); at age-up an Alanthor faction's Veilstone Mines
>   become Trading Outposts (§3.1). Iron Mines are every culture's.
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

- **An empty node pays 10/min** of its resource to whoever holds its ground;
  built on, it pays its extractor's rate (§5). A Cursed or Depleted outcrop
  pays nothing.
- **The curse replenishes.** When the curse raises a node on a depleted
  outcrop, the outcrop's reserve refills to full. Pacify it later and it is
  a full Inactive node again.
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
| **Alanthor** | **Trading Outpost** beside it | Destroy the curse node → pacified (back to Inactive) | **Trading Outpost** beside it |

### 3.1 Alanthor — the Trading Outpost

The Outpost simulates trade without caravans or a menu: it is a building on
the map, in contested ground, that anyone can burn.

| | |
|---|---|
| **Alanthor do not mine veilstone** | No Veilstone Mine once Alanthor (iron Mines stay — they are every culture's). **At age-up every Veilstone Mine the faction owns becomes a Trading Outpost** — same building, same spot, same health fraction; the mine's level is lost. |
| Placement | **On top of** an Inactive or Depleted veilstone outcrop, snapped onto it like a mine (2026-10-01; it stood beside the outcrop before). **One Outpost per outcrop.** Never on a Cursed outcrop. Alanthor's Mine button raises it on veilstone. |
| Cost | 160 Supplies + 80 Iron, 30 s build |
| Locks territory | Yes, like an extractor (Territory_Claims.md §3) |
| **Buy Veilstone** (default) | **−50 Supplies −50 Iron → +65 Veilstone** per minute |
| **Forge Veilsteel** (research: Veilsteel Forging) | **−50 Veilstone → +10 Veilsteel** per minute |
| **Sell Veilsteel** (research: Veilsteel Export, after Forging) | **−50 Veilsteel → +300 Iron +450 Supplies** per minute |
| **Trade Agreements I / II / III** (research, chained) | Every trade's INPUTS cost **20 / 45 / 75 %** less |
| Research | Hosted by the Outpost itself (`TradingOutpost/Research/`) |
| Can't afford a cycle | That cycle is skipped; nothing is spent |
| Its outcrop turns Cursed | The Outpost idles until the outcrop is pacified |

The trade choice is the balancing act: every Outpost forging or selling is one
not buying veilstone. The trade runs on a 12 s cycle with a per-Outpost
fractional carry, so the per-minute numbers are paid exactly. The Outpost does **not** drain the outcrop; trade is not
mining. Depleted outcrops left behind by Feraldis are as good as fresh ones
for an Outpost.

Numbers live in `TradingOutpostSystem.asset` beside the class, authored per
minute.

### 3.2 Feraldis

- **Veilstone Mine** on an Inactive outcrop: veilstone per mine level, ×1.5
  for Feraldis, and every unit paid is drawn from the outcrop's reserve. A
  spent outcrop is Depleted and pays nothing (no yield floor for veilstone).
- **Veilsteel**: destroying a curse node pays its last-hitter **40
  Veilsteel** if that faction is Feraldis (`feraldisNodeVeilsteel`).
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

## 5. Territory income, per minute (2026-10-01)

| Source | Pays |
|---|---|
| **Fortress** | 50 Supplies |
| **Empty slot** (supply / iron / uncursed veilstone node in held ground) | 10 of its resource |
| **Gatherer's Hut** on a supply slot | 50 / 100 / 200 Supplies (L1 / L2 / L3) |
| **Mine** on an iron slot | 100 / 200 / 400 Iron |
| **Veilstone Mine** on a veilstone slot | 100 / 200 / 400 Veilstone (×1.5 Feraldis, drains the outcrop) |
| **Alanthor** (after age-up): Gatherer's Hut | **70 / 100 / 200** Supplies |
| **Alanthor** (after age-up): Mine | **140 / 200 / 400** Iron |

A built slot REPLACES its empty 10, it does not add to it. The Hall/Fortress
level multiplier (×1 / ×2 / ×4) and the survey research still scale the
territory. The iron Mine's old patch income (`MineIncomeSystem`) is retired.
**Every source pays every second** — the territory tick and every Trading
Outpost (fractional carry keeps the per-minute numbers exact). **A resource the
faction's trades consume faster than it produces turns red in the resource
bar** (`TerritoryIncomeSystem.FactionNetForDisplay`).

A fresh start with 5 supply, 2 iron and 2 veilstone slots pays
**100 Supplies + 20 Iron + 20 Veilstone** per minute (Fortress 50 + slots).
Mines pay double the hut ladder (2026-10-01).

**Forests pay nothing and the Sawyer is gone (2026-10-01).** **The Smelter is
gone (2026-10-01)**: its armour ladders moved to the buildings that train what
they protect — Plate (melee) to the Barracks, Brigandine (ranged) to the
Archery Range, Barding (cavalry) to the Royal Stable, Plating (siege) to the
Siege Yard — same costs and the same L1/L2/L3 host-level gates.

## 6. The iron pass (2026-10-02)

> Two 30-minute 8-AI batches on Veilmarch ended with every faction on 8-12k
> unspent supplies and six of eight under 60 iron: iron was the wall every
> economy hit. This pass moves iron off the things that are not military and
> pays more of it.

| Change | Value |
|---|---|
| **Start territory** | **3 iron** nodes (was 2) — 3 supply, 3 iron, 1 veilstone |
| **Iron yield** | every iron line pays **+20 %** — empty slots and Mines alike (`TerritoryIncomeSystem.IronYieldMultiplier`) |
| **Mine** and **Veilstone Mine** cost | **100 S + 10 V**, no iron (was 90 S + 140 I / 90 S + 160 I) |
| **Palisade** hub | **50 S**, no iron (was 50 S + 20 I); its modules stay 6 S |
| **House (Hut)** | supplies only, its level-ups included: L2 270 S, L3 533 S (the iron is gone) |

**The Mine's own research.** Every Mine is a research host, and two techs
make the iron slots it works pay more. They replace each other, they do not
stack, and an empty slot is not mined so it does not benefit:

| Tech | At | Cost | Time | Effect | Requires |
|---|---|---|---|---|---|
| **Deep Shafts** | Mine | 150 S + 20 V | 40 s | Mine-worked iron slots **+50 %** | — |
| **Rich Seams** | Mine | 300 S + 60 V | 60 s | Mine-worked iron slots **+100 %** | Deep Shafts |

These multiply with everything else on the iron line (the +20 %, the Hall's
x1/x2/x4, the Iron Surveying ladder). The AI researches Deep Shafts right after
Iron Surveying I and Rich Seams after Iron Surveying II (Feraldis: after Iron
Plunder / Raiding II); its extractor walk reads the Mine's cost from the SO, so
the cheaper Mine needs no AI change of its own.

## 4. What changed in code (2026-10-01)

Implemented:

- `VeilstoneNodeState` on every outcrop, kept by `VeilstoneNodeStateSystem`
  (Inactive / Cursed / Depleted; refills the reserve when the curse takes it).
- `TerritoryIncomeSystem`: no veilstone or veilsteel node trickle; a Veilstone
  Mine pays per level from an Inactive outcrop and drains it to zero.
- Alanthor cannot build `VeilstoneMine` (`TerritoryOwnership.MayBuildMine`,
  enforced in `CheckPlaceBuilding`); `TradingOutpost.ConvertMinesForCulture` turns
  their Veilstone Mines into Outposts at age-up and in `StartAgePromoter`. Veilsteel deposits are no longer
  spawned; the iron Mine pays no veilstone; the Smelter generates nothing and
  is placed freely.
- `Alanthor_TradingOutpost` with its trade/forge toggle (`SetOutpostMode`
  lockstep order), placement rule in `CommandRouter.CheckPlaceBuilding`, and
  AI siting and mode choice.
- Feraldis veilsteel bounty on curse-node kills.

- **Income overlay** (`UI/World/IncomeOverlay`): hover ground you hold to see
  what that territory pays per minute, hover one of your buildings to see what
  it adds or spends per minute (green income, red spending). Synty
  Frame_Box_Medium_05 panels floating over the world like the claim bars, fed by
  `TerritoryIncomeSystem.ComputeYieldForDisplay` / `BuildingYieldForDisplay` —
  the same arithmetic the income tick pays.

Not yet implemented: Runai Sanctuary and caravan feeding, Feraldis raiding,
per-culture religion income. Veilsteel deposit **markers** still sit in five
map scenes (inert: nothing spawns from them); strip them on the next re-bake.
