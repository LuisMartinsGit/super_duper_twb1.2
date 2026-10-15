# Age 1 — Feraldis

> **Industrial dark-magic Vikings who mean to rid the world of the curse.**
> Feraldis live on the border, between the curse and the sea. They do not
> fear it and they do not worship it: they burn it out, mine what it leaves,
> and forge it into weapons. Their economy is bodies and furnaces, their army
> is fast and lethal up close, their siege is the best in the game — and a
> Feraldis player who stops fighting starves.
>
> **Doc version: 2026-10-12 — full redesign (developer direction, decided in
> chat 2026-10-12).** Supersedes every earlier version of this document: the
> Raider Camp, the Plunderer, the House raider waves, "every warrior
> plunders", the Raiding Surveys, the old influence model (War Totems as the
> influence engine, marching influence, influence that never decays), the
> workerless Feraldis Mine and the Corruptor are **retired**. The 2026-08-05
> blood rules (frenzy on blood, bleeding is blood, fire eats blood) STAND.
> The previous text is in git history (`git show v0.0.41:docs/Design/Age_1_Feraldis.md`).
>
> **Amended 2026-10-13 (developer direction):** religion points come from
> **drawing blood** — every enemy kill and every sacrifice, Altar or not
> (§5, Religion.md §7); the cultured Barracks is the **Longhouse** (§7.2);
> the palisade becomes the Feraldis **Wooden Wall** at age-up (§7.7); and a
> slave can build no other culture's building (§3.1).
>
> **Data lives on the SOs** ([Unification decisions](Unification_Decisions_2026-10-03.md)):
> this doc keeps rules and reasons. Every cost, HP, damage, range, time,
> share and multiplier is on the Feraldis SOs under
> `Assets/GameData/TechTree/Civs/Feraldis/` or on the named config asset;
> on a conflict the SO wins. Values the developer set in conversation are
> recorded here only as the seed each config field was authored with.
>
> **Edition:** Feraldis ships in editions **B and C only**, never in the demo
> ([Alpha_Build.md §4](Alpha_Build.md)).
>
> **See also:** [Age_1_Alanthor.md](Age_1_Alanthor.md) (the balance yardstick),
> [Veilstone_Economy.md](Veilstone_Economy.md) §3.2 and §5,
> [Religion.md](Religion.md) §7, [Territory_Claims.md](Territory_Claims.md)
> §5, [Combat_Pacing.md](Combat_Pacing.md), [Teams.md](Teams.md) §2,
> [Heroes.md](Heroes.md) §6.

---

## 1. Identity

| Aspect | Feraldis |
|---|---|
| Relationship to the curse | **Wants it gone.** Kills of curse units and razed curse nodes pay them in veilstone (§3.6); their hero razes curse nodes outright (§8) |
| Look | Industrial dark-magic Vikings: timber halls and longhouses, iron furnaces and smokestacks, rune-lit machinery that runs on blood and veilstone |
| Economy signature | **Slaves** staff the extractors (§3.1); one **Great Forge** turns veilstone and iron into veilsteel (§4); late-game supplies come **only from raiding** (§3.5) |
| Combat signature | **Fast, deadly in close quarters**, short ranges, light armour; **frenzy on blood** (§6.1); the **best siege in the game** (§7.5) |
| Religion signature | **Drawing blood** — kills and sacrifices pay religion points, more at the Blood Altar (§5) |
| Population | army population is the game cap from age-up (unchanged); **slaves are capped separately** by the Slave Pens (§3.2) |

**Design yardstick — Alanthor.** Alanthor's economy is buildings that keep
paying after the nodes run dry (Vault, Guild surveys, the Trading Outpost
chain); it wins by outlasting. Feraldis peaks earlier, carries its value in
bodies that can be killed and in one Forge that can be razed, and keeps
pace after minute 30 **only while it raids**. At equal investment the Great
Forge loop is tuned to match the Outpost loop's net; Feraldis reaches its
ceiling sooner and loses more when the Forge falls.

---

## 2. Age-up

At age-up into Feraldis (by building the **Fiendstone Keep**, the Feraldis
landmark — [Age_0.md](Age_0.md)):

| Age 0 entity | Becomes | Notes |
|---|---|---|
| Shelter (`Fortress`) | **Fortress** | the capital, L1 free, L2–L3 bought, like every culture's ([Age_1_Alanthor.md § Fortress](Age_1_Alanthor.md)); Feraldis level SOs `Civs/Feraldis/Buildings/Fortress/Fortress_Lvl1..3` |
| Worker | **Slave** | every standing Worker is converted in place (same entity, HP fraction kept); Workers can no longer be trained — the Fortress trains Slaves |
| Barracks | **Longhouse** | cultured rename (same entity) — was "War Hall" until 2026-10-13 (§7.2) |
| Hut | **Slave Pens** | cultured rename; houses slaves, not soldiers (§3.2) |
| Gatherer's Hut | **Work Camp** | cultured rename; an ordinary supply extractor staffed by slaves (§3.1). **No Raider Camp, no Plunderers** |
| Mine / Veilstone Mine | Mine / Veilstone Mine | staffed by slaves (§3.1) |
| Temple of Ridan | **Blood Altar** | cultured rename; chapels, sects and the Litharch unchanged; adds sacrifice (§5) |
| Palisade | **Wooden Wall** | raised automatically to its one cultured level — sturdier, no further levels; tower and gate fittings (§7.7) |

**Nothing spawns at age-up.** The House raider waves are retired.

---

## 3. The economy

### 3.1 Slaves staff the extractors

**Nobody gathers** stays true ([Regions.md](Regions.md) §4): there is no
carrying and no drop-off. A slave is ordered onto a Work Camp, Mine or
Veilstone Mine and **works on site** — it stands at the node, visible and
killable, and the building credits the bank directly through the territory
tick exactly as today.

- Each extractor has a **crew** of a few slots (`crewSlots` on the building SO).
- **An unstaffed extractor pays a reduced share** of its slot income; each
  slave adds to it, and a **full crew out-earns** an Alanthor Guild / Mine
  of the same level — the premium for slaves that cost supplies, walk there
  and die. The curve is `crewIncomeAtZero` / `crewIncomeAtFull`
  (`FeraldisEconomy.asset`).
- **On a Veilstone Mine every slave also drains the outcrop faster** — the
  standing rule "Feraldis mine fast and finite" ([Veilstone_Economy.md §3.2](Veilstone_Economy.md)).
- Slaves still **build and repair**, exactly as Workers did — **only the
  Age 0 buildings and the Feraldis ones** (2026-10-13). No Alanthor or Runai
  building (Archery Range, Royal Stable, Siege Yard, Trading Outpost, stone
  wall, …) is offered to a slave or accepted from any order; the one rule is
  `CultureGate.CanFactionBuild`, read by the build palette and enforced by
  `CommandRouter` for the AI and every lockstep order. The reverse holds for
  Alanthor and Runai.
- Killing a staffing slave is how an enemy raids a Feraldis economy; a slave
  on a node is a target, the building is not the only one.

### 3.2 Slave Pens

The cultured Hut. Army population is already the game cap for Feraldis, so
the Pens do not house soldiers: they set the **slave cap** — the most slaves
a faction may own is the sum of its Pens' capacities (by level, on the level
SOs) plus the Fortress's own. Training a slave past the cap is refused like
any population block. Slaves count against the slave cap, **not** the army
population.

### 3.3 The Overseer

A unique unit (one per faction) trained at the Fortress from **L2** — the
counterpart of Alanthor's Ledger. Slaves near the Overseer work faster (crew
output and Forge stoking, §4). Radius and bonus on its SO.

### 3.4 The raze bounty

**Destroying an enemy building pays the destroyer a quarter of what it cost**
— its build price **plus every level its owner bought** — in the same
resource mix (`razeBountyShare`, seeded at 25 %). Wall pieces count.
Planned buildings (no ground broken) and cancelled sites pay nothing; a
building its owner demolished pays nothing. It is never profitable on its
own (the owner lost all of it), but it swings fights and means siege is
never dead money.

### 3.5 Raiding is the late-game supply

After the nodes run dry ([Territory_Claims.md § 11.3](Territory_Claims.md)),
**Feraldis supplies come only from raiding**. A Feraldis player who avoids
fights is meant to lose. The sources:

| Source | Rule |
|---|---|
| **Raze bounty** | §3.4 — buildings are priced mostly in supplies |
| **Pillage** | killing an enemy unit pays a share of its whole cost, **as supplies** (`pillageShare`) — Alanthor soldiers cost iron and veilstone, so a share of the supply cost alone would pay nothing; slaves and workers count |
| **Siphon** | a Feraldis unit attacking an enemy **extractor, Trading Outpost or Fortress** drains supplies from that faction's bank to its own (`siphonPerSecond`, never below zero) |
| **The curse** | pays **no supplies** (§3.6) — otherwise farming the curse would be a way to avoid other players |

To make it bite, **Feraldis territory claims pay no supplies** — only their
iron and veilstone ([Veilstone_Economy.md §5](Veilstone_Economy.md)). The
Fortress keeps its own supply income as the floor that stops a beaten
Feraldis being soft-locked; it is enough to keep a few slaves alive, never an
army.

**Recovering from a collapse: Melt Down** (§4.3). A beaten Feraldis melts
soldiers at the Forge to rebuild, at the price of its next attack.

### 3.6 The curse pays veilstone

From age-up a Feraldis faction is paid by the curse in veilstone **as well
as** religion points (2026-10-13 — until then veilstone replaced the points,
which soft-locked a Feraldis player with no RP):

- **a curse unit killed** (last hit, as Religion.md §1) pays veilstone
  scaled by the unit (`curseKillVeilstone*`) **and** its Religion.md §1
  points (§5);
- **a curse node destroyed** pays a lump of **veilstone and veilsteel**
  (`curseNodeVeilstone`, `curseNodeVeilsteel`) — on top of the Religion
  Point every node pays its destroyer (Religion.md §2).

Before age-up the Religion.md §1 rules apply to Feraldis as to everyone.

---

## 4. The Great Forge

`Feraldis_GreatForge` — **one per faction**, levels L1–L3. The industrial
heart of the culture and the building an enemy most wants to raze.

### 4.1 Bays

The Forge holds up to **four bays**, each bought separately at a rising
price (the per-bay ramp, like the Trading Outpost's per-outcrop ramp —
`bayCostRamp`). Each bay runs **one recipe** at a time, has its own level
(L1–L3, rate multiplier on the level SO) and can be set to **Hold**:

| Recipe | In | Out | Alanthor counterpart |
|---|---|---|---|
| **Soulforge** | slaves fed into the furnace | veilstone | Buy Veilstone |
| **Veilsteel** | veilstone + iron | veilsteel | Forge Veilsteel |
| **Arms Export** | veilsteel | **iron only** | Sell Veilsteel |

- The three make a **closed loop that runs forever**: supplies buy slaves,
  slaves become veilstone, veilstone and iron become veilsteel, veilsteel
  exports for iron. Supplies are the throttle — no raiding, no slaves, no
  loop (§3.5).
- **Soulforge consumes slaves**: a bay on Soulforge takes the slaves ordered
  into the Forge one at a time. The line of slaves walking to the Forge is
  the loop's weak point.
- **Slaves crewing a bay speed it** (the same crew rule as §3.1).
- **The Forge is the only Feraldis source of veilsteel** besides curse nodes
  (§3.6). There is no veilsteel mining and no Feraldis Outpost.
- Curse veilstone (§3.6) feeds the Veilsteel bay directly.

### 4.2 Forge research

| Tech | Gate | Effect |
|---|---|---|
| **Bellows I / II / III** | — / L2 / L3 | every bay's inputs cost progressively less (the Trade Agreements counterpart) |
| **Draught Furnaces** | after Bellows I | every bay runs faster (the Swift Caravans counterpart) |
| **War Smithing I / II** | L2 / L3 | unlock the veilstone and veilsteel **weapon tiers** at every Feraldis trainer — the Forge level, not the trainer's, gates the top tiers |

### 4.3 Melt Down

A soldier ordered into the Forge is melted for a **share of its training
cost** (`meltRefundShare`), paid back in the resources it was bought with.

- **The refund scales with current HP** — a wounded soldier is worth less,
  so a beaten army cannot be cashed in at full value to deny the enemy its
  pillage.
- It is **always less than the soldier cost** and **below the pillage an
  enemy earns** for the same unit (`meltRefundShare < pillageShare` in
  effect), so melting a doomed army is never better than fighting with it.
- **Heroes cannot be melted.** Slaves are not melted — they go to the
  Soulforge.
- The soldier must **walk to the Forge**.

---

## 5. Religion — drawing blood (2026-10-13)

The Temple of Ridan becomes the **Blood Altar** at age-up (same entity,
same six chapel faces; sects, chapels and the Litharch are unchanged —
sects are the same for everyone). [Religion.md](Religion.md) §7 is the
religion-points rule for Feraldis: **drawing blood pays points**, with or
without an Altar, all of it through the **same escalating ladder** as curse
kills (Religion.md §1), so mass killing and mass sacrifice both run out of
value quickly.

- **Kills.** Every enemy unit a Feraldis faction kills (last hit, any
  hostile unit — the curse's included) pays points scaled by its cost
  (`killPtsPerCost`, floor `killPtsMin`). A curse kill pays its Religion.md
  §1 points **and** its veilstone (§3.6). Allies and teammates pay nothing
  (`Alliances.AreHostile`).
- **Sacrifice at the Altar.** A unit ordered into the Altar is killed there
  and pays points scaled by what it cost (`sacrificePtsPerCost`; veterans and
  expensive units pay more, slaves a little), **times
  `altarSacrificeMultiplier`**. Heroes cannot be sacrificed.
- **Field sacrifice.** A slave can be sacrificed **anywhere** (an ability on
  the slave). It dies, leaves a blood pool — frenzy ground and a totem
  site — and pays the same points an Altar sacrifice of it would before the
  multiplier; within `altarSacrificeRadius` of one of the faction's finished
  Altars it counts as made at the Altar and is multiplied too. It needs no
  Altar: a Feraldis faction that ages up with no RP and no Altar bleeds its
  way to the Altar's `templeRp`.
- **The Altar stands on a permanent blood pool** — the ground under it is
  saturated and never fades; a home totem can be planted beside it.
- The Temple's slow point trickle and the Tithe stand.
- Every religion point a Feraldis faction earns counts down the Shardroot
  counter like anyone's.
- The AI field-sacrifices a spare slave (at its usual rate limit) when it has
  no finished Altar, and uses the Altar when it has one.

The five values (`killPtsPerCost`, `killPtsMin`, `sacrificePtsPerCost`,
`altarSacrificeMultiplier`, `altarSacrificeRadius`) are on
`FeraldisEconomy.asset`.

---

## 6. Blood

### 6.1 The standing rules (2026-08-05, unchanged)

1. Blood is real ground state (`BloodMap`).
2. **Frenzy on blood**: every Feraldis unit standing on blood fights harder
   and faster (values on the Frenzy config).
3. Bleeding drips blood — every bleed paints the ground its own army
   frenzies on.
4. Fire eats blood — Feraldis fire ignites a pool, consuming it to burn
   everything standing in it.

**Only the Berserker fights better at low HP** (§7.2). There is no
army-wide low-HP rule.

### 6.2 Totems

`Feraldis_WarTotem` — placed **only on blood**, and **may be placed on blood
in a territory no other faction owns** (the one Feraldis exception to the
build gate, [Territory_Claims.md §5](Territory_Claims.md)) — so a Suicidal's
pool or a War Chariot's trail opens new ground. A finished totem **holds**
its territory like any building.

A totem **drinks** the blood in its radius into two counters:

- **Lifetime blood** — everything it has ever drunk. It never goes down, and
  it is what **evolves** the totem to its next tier.
- **Stored blood** — what it holds now. It is **spent on rites**.

Spending never slows evolution.

| Tier | Name | Passive aura (radius) | Rites (timed, cost stored blood) |
|---|---|---|---|
| I | **Blood Pylon** | frenzy lingers longer after a unit leaves blood | **Bloodlust** (attack speed) · **Hunger** (lifesteal) |
| II | **Gore Engine** | — the tier-I aura, stronger | tier-I rites stronger · **Red Mist** (units in radius cannot drop below 1 HP for a few seconds) |
| III | **Skull Furnace** | kills in radius heal the killer | **Blood Moon** (the whole radius counts as blood for a while) |

A totem whose pool runs dry and whose store is empty collapses after a
grace period (the 2026-08-07 rule stands — it stops totem spam). Lifetime
thresholds, rite costs, durations and strengths are on `FeraldisTotem.asset`.

---

## 7. The army

**Doctrine.** Fast, lightly armoured, lethal up close. Most Feraldis infantry
is **Light**, so Alanthor's anti-Heavy Sentinel is wasted on it while
Alanthor's Archers (bonus vs Infantry) still punish it in the open; Feraldis'
answer to range is speed. Every melee unit has a way to stick to its target
(slow, pull, bleed). Siege beats Alanthor's siege on damage and mobility;
Alanthor's siege and wall emplacements outrange it, so Feraldis must rush
the wall line through the fire.

**Every combat unit carries a class tag and a weight tag** ([Combat_Pacing.md](Combat_Pacing.md)).

### 7.1 The Fortress

| Unit | Gate | Tags | Role |
|---|---|---|---|
| **Slave** (`Feraldis_Slave`) | — | Worker, Light | builds, repairs, staffs, feeds the Forge, field sacrifice |
| Scout | — | Light | as everyone |
| **Overseer** (`Feraldis_Overseer`) | L2 | Light | §3.3 — one per faction |
| **Harkon Fiend** (`Feraldis_HarkonFiend`) | L3 | Infantry, Heavy | the hero, §8 |

### 7.2 Longhouse — cultured Barracks

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| **Spearman** (`Feraldis_Spearman`) | L1 | Infantry, **Light** | Cavalry | cheap, fast; the anti-cavalry leg |
| **Bloodletter** | L2 | Infantry, Light | — | fast whirling area attack that bleeds everything it hits |
| **Suicidal** | L2 | Infantry, Heavy | — | soaks arrows, explodes into a large blood pool (not siege damage) |
| **Berserker** | L3 | Infantry, Heavy | Heavy | trained here again (no longer conversion-only); the elite melee |

**The Barracks IS the Longhouse (2026-10-13).** The Age 0 Barracks is
renamed the **Longhouse** at a Feraldis age-up — the SAME entity, its roster
the Barracks def's `trains[]` (culture-gated by id prefix) and its research
the techs whose `researchAt` is the Barracks (filed under
`Civs/Feraldis/Buildings/WarHall/Research/`). Its level names are on the
level SOs `Civs/Feraldis/Buildings/WarHall/WarHall_Lvl1..3` (the folder keeps
its old name for now; the art pipeline addresses it by path). The separately
placeable `Feraldis_Longhouse` building is **retired** — off the build palette
and refused by `CommandRouter` — so there are never two Longhouses with one
role.

**The Berserker's two researches** (Longhouse), both required before they work:

| Tech | Gate | Effect |
|---|---|---|
| **Berserk** | L1 | at **a quarter HP** the Berserker goes berserk: much more attack and some more speed (seeded +50 % / +20 %) |
| **Altered Self-Perception** | L3, after Berserk | at **1 HP** the Berserker becomes **invulnerable for a short time** (seeded 10 s), then **dies unless healed** above the threshold by then |

They replace the Death Frenzy. Healing in that window comes from Feraldis'
own sources (totem Hunger, Skull Furnace, the hero) — the Altar's heal penalty
still stands.

### 7.3 Hall of Axes — the ranged building (Age 1, `Feraldis_HallOfAxes`)

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| **Archer** (`Feraldis_Archer`) | L1 | Ranged, Light | Infantry | **more range and less damage than Alanthor's Archer** — the one patient unit; covers the approach |
| **Axe Thrower** (`Feraldis_Hunter`) | L2 | Ranged, Light | — | point-blank damage plus bleed |
| **Firethrower** | L3 | Ranged, Light | — | ignites blood (§6.1) |

### 7.4 Rig Yard — the cavalry building (`Feraldis_Pasture`, displayed "Rig Yard")

| Unit | Gate | Tags | Bonus vs | Role |
|---|---|---|---|---|
| **Raider** (`Feraldis_Raider`) | L1 | Cavalry, Light | Ranged | fast harasser that leaves buildings burning — the raze-bounty and siphon tool |
| **War Chariot** | L2 | Cavalry, Heavy | Ranged | leaves a blood trail; **bladed wheels damage infantry it drives through**; Feraldis' heavy cavalry a level before Alanthor's Cataphract |

### 7.5 Engine Works — siege (`Feraldis_SiegeYard`, displayed "Engine Works")

Only siege damages walls ([Combat_Pacing.md § The Wall Rule](Combat_Pacing.md));
Feraldis has four engines for it.

| Engine | Gate | Bonus vs | Rule |
|---|---|---|---|
| **Ram Rig** (`Feraldis_RamRig`) | L1 | Building | a moderately fast, **fragile explosive on wheels**. It drives to its ordered structure and **explodes on contact with that structure, or wherever it dies**. The explosion is siege damage and **hits everything** in its radius — enemies, allies and its own escort. Units cannot set it off by touching it, only by killing it |
| **Harpoon Rig** (`Feraldis_HarpoonRig`) | L2 | — | fires a harpoon at a **unit** and leaves a **barbed-wire line from the rig to the target** for a short time (seeded 3 s). Anything crossing it — **friend or foe** — takes damage and heavy bleed (which paints blood). Fire it across the enemy's escape path, not between the armies |
| **Corpse Engine** (`Feraldis_CorpseEngine`) | L2 | Building, Infantry | lobbed splash; **every impact leaves a blood pool**, so a bombarded wall line becomes frenzy ground and totem sites |
| **War Rig** (`Feraldis_WarRig`) | L3 | Building | the flagship: a furnace-driven land-ship that **carries a squad of infantry** (unload at the breach) and a turret cannon that **fires on the move**. Shorter range than a Trebuchet, more damage. Costs veilsteel |

The Ram Rig's and the Harpoon Rig's damage is the deliberate exception to
"allies cannot damage each other" ([Teams.md](Teams.md) §2).

### 7.6 Research

Every Feraldis tech is **faction-wide** (decision 31). Each trainer carries
the four-tier weapon ladder of its class (the top two tiers gated by War
Smithing, §4.2), the veterancy ladder, and a **Blood ladder in place of
Alanthor's armour ladder** (Frenzy stronger and longer-lasting per tier) —
siege keeps an armour ladder, because it has to survive the rush.

| Building | Specials (gate) |
|---|---|
| **Longhouse** | Conscription (—) · **Warpath Sprint** (L1: out-of-combat speed burst) · **Hamstring** (L2: melee hits slow the target) · **No Retreat** (L3: frenzied units cannot be slowed or stunned) · Berserk / Altered Self-Perception (§7.2) |
| **Hall of Axes** | Choreographed Volleys (—) · **Barbed Heads** (L1: longer bleed) · **Quick Hands** (L2: faster throwing) · **Pitch Pots** (L3: fire burns longer and spreads to adjacent blood) |
| **Rig Yard** | **Spiked Wheels** (L1: stronger wheel damage) · **Blood-Burners** (L2: active speed burst during which the riders **can** attack) · **Ride-By** (L3: Raiders attack without stopping) |
| **Engine Works** | **Fire on the Move** (L1: Corpse Engine and Harpoon Rig) · **Barbed Coils** (L2: the Harpoon Rig's wire lasts longer) · **Blood Boilers** (L2: every engine moves faster on blood) · **Overcharge** (L3: War Rig cannon active — a damage burst that costs the rig HP) |
| **Fortress** | **Iron / Veilstone / Veilsteel Chains** (slave work speed — the Feraldis tool ladder) · **Pen Expansion** (L2: more slaves allowed) · **Bloodsight** (—: Scouts see bleeding enemies through fog) |
| **Great Forge** | §4.2 |

### 7.7 Walls — the Wooden Wall (2026-10-13)

Feraldis never leave timber ([Age_0.md § Palisade](Age_0.md)), and they do
not climb a wall ladder either:

- **At age-up every Feraldis palisade is raised automatically** to the
  **Wooden Wall**, the palisade's one Feraldis level — sturdier than the
  Age 0 fence. No research, no upgrade button, no cost; palisade raised
  later stands at it from the start. Name and HP multiplier are on
  `Civs/Feraldis/Buildings/Palisade/Palisade_Lvl1.asset`.
- **There are no further levels and no upgrades** — no Battlements, no
  Shielded Ramparts (those are the Alanthor stone wall's).
- **Fittings: the wall tower and the gate only.** The Wooden Wall is the one
  palisade that carries a tower; it never mounts a Ballista or a Trebuchet.
  It can still be branched (Convert to Hub), which is drawing, not a fitting.
- **Feraldis cannot build the Alanthor stone wall** (§3.1).

### 7.8 Retired

The **Corruptor** (`Feraldis_Iconoclast`) has no job since wells were
removed and is off every roster. The **Warboar Rider**, the **Plunderer**,
the **Raider Camp**, the **House raider**, the **Fiend Foundry**, the
**Veilsteel Forge** spec and the **Totem Tower** are retired (ids may stay in
code until cleaned up; nothing routes to them). The stand-alone
**`Feraldis_Longhouse`** building is retired too (2026-10-13): the cultured
Barracks is the Longhouse (§7.2).

---

## 8. Harkon Fiend — the hero

`Feraldis_HarkonFiend`, trained at the Fortress from L3, one per faction,
levels 1–10 from kills ([Heroes.md](Heroes.md)).

| Level | Ability | Effect |
|---|---|---|
| 1 | **Warcry** | nearby allies frenzy as if standing on blood, for a few seconds |
| 3 | **Leap** | jumps into a melee and slows on landing |
| 5 | **Raze** | **instantly destroys a target building** — wall pieces and **curse nodes** included ("the tip of the spear"). Against a **capital-class** building (Fortress, Vault of Almiérra, Fiendstone Keep, Great Forge, Temple / Altar) it deals a share of its HP instead (seeded 30 %). Very long cooldown (seeded 5 min). Pays the raze bounty |
| 10 | **Blood Oath** | every Feraldis unit near him cannot die for a short window |

**Raze is the one non-siege attack that may damage a wall** — the explicit
exception to the Wall Rule ([Combat_Pacing.md](Combat_Pacing.md)). One Raze
opens one segment: Harkon opens the breach, the rigs widen it.

---

## 9. How it balances against Alanthor

| Alanthor strength | Feraldis answer | Alanthor's counter back |
|---|---|---|
| Longbow lines | speed (Sprint, Blood-Burners) + Raider / Chariot bonus vs Ranged | Crossbowmen and Spearmen against the cavalry |
| Stone walls | four fast engines, the Ram Rig, Raze | Trebuchet and wall emplacements outrange every Feraldis engine; Swordsman bonus vs Siege at the breach |
| Armour ladders | frenzy, bleed, Hamstring, the Berserker | fight off the blood; kill at range |
| Patience (Vault, surveys, Outposts) | raiding income, the Forge loop | raid the slaves, raze the Forge, refuse fights |

**Watch in playtest:** snowballing. A Feraldis that loses its late fights
cannot refill supplies; the lever is the Fortress floor and Melt Down, not
the raiding rates.

---

## 10. Open items

- Whether Alanthor's Trade Agreements III discount is intended — the Forge's
  Bellows ladder is tuned against it.
- Art: every new unit and building uses placeholder or reused visuals until
  art exists.
