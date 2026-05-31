# The Waning Border — Player Manual

A real-time strategy game of culture, conquest, and the slow tide of the
Crystal Curse.

> **For game-design content** (resources, buildings, units, techs, factions,
> sects, era progression), see **[docs/Design/](docs/Design/Overview.md)** —
> the canonical truth source. This manual covers **player-facing UX only**:
> controls, hotkeys, UI, victory conditions, AI personalities, multiplayer.
> Earlier drafts of this manual carried duplicate mechanic content that
> conflicted with Design/; those sections were retired on 2026-05-19.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Starting a Match](#2-starting-a-match)
3. [Controls & Interface](#3-controls--interface)
4. [Combat — Damage Math](#4-combat--damage-math)
5. [Victory Conditions](#5-victory-conditions)
6. [AI Opponents](#6-ai-opponents)
7. [Multiplayer](#7-multiplayer)
8. [Appendices](#8-appendices)

---

## 1. Overview

The Waning Border is an RTS in which you take command of one of three
rising cultures on a continent slowly being consumed by the Crystal Curse.
You start in a **neutral, pre-culture age (Age 0)**, gather supplies and
iron, and at the right moment commit your people to a culture — **Runai**
(nomadic traders), **Alanthor** (defensive forgemasters), or **Feraldis**
(fierce warbands). Each culture rewrites your economy, your buildings, and
your military doctrine.

Beyond the rival player factions, every map is haunted by **Crystal Main
Nodes** — alien growths that spawn hostile crystal creatures and slowly
spread cursed ground. Defeating, cleansing, or converting these nodes is
at the heart of every match — and it's the only source of **Glow**, the
late-game super-resource.

**Engine:** Unity 6 with DOTS/ECS for unit simulation; classic
MonoBehaviour UI on top.

**For the design-level mechanics this overview hints at:**
- [docs/Design/Overview.md § Movement axis](docs/Design/Overview.md#north-star-the-movement-axis) — the three-way faction triangle
- [docs/Design/Age_0.md](docs/Design/Age_0.md) — Age 0 starting buildings / units
- [docs/Design/Age_1_*.md](docs/Design/) — per-culture full tech trees
- [docs/Design/Overview.md § Glow economy](docs/Design/Overview.md#the-glow-economy-cross-faction) — how Glow is created and dropped

---

## 2. Starting a Match

### Game Modes

| Mode | Description |
|---|---|
| **Free-For-All** | Standard skirmish, 2+ player factions, no shared teams. |
| **Solo vs. Curse** | A single player against the Crystal Curse AI. |
| **Scenario** | Pre-built maps such as *LargeMelee*, *FourWayCultures*, *WallSiege*. |
| **Sandbox** | Unrestricted building and testing — no victory conditions. |
| **Battalion Test** | Minimal bootstrap for testing formations. |
| **Pathfinding Test** | A diagnostic mode for navigation. |

### Starting Conditions

Every faction begins identically, regardless of mode:

- **Supplies:** 400
- **Iron:** 150
- **Crystal:** 0
- **Veilsteel:** 0
- **Glow:** 0
- **Age:** 0 (pre-culture / neutral aesthetic)
- **Religion Points:** 0
- **Starting structures:** One Hall, placed at your spawn position.
- **Population cap:** 20 (from your starting Hall).

The map will also have one or more **Crystal Main Nodes** pre-placed and a
scatter of **Cadavers** (crystal corpses) to mine.

For the full breakdown of what you can build / train / research in Age 0,
see [docs/Design/Age_0.md](docs/Design/Age_0.md).

---

## 3. Controls & Interface

### Selection

| Action | Control |
|---|---|
| Select one entity | Left Click |
| Drag-select multiple | Left Click + Drag |
| Select all of a type (on screen) | Double-click a unit |
| Select all of a type (map-wide) | Ctrl + Double-click |
| Select an entire battalion | Click any of its members |
| Clear selection | Esc, or left-click empty terrain |

**Smart Military Drag:** A box drag prefers units over buildings, and
military units over economy units when both are inside the box.

### Camera

| Action | Control |
|---|---|
| Pan | W / A / S / D, edge-scroll, or hold Middle Mouse + drag |
| Zoom | Mouse wheel (range ~15–80 units) |
| Rotate left / right | Q / E |
| Tilt up / down | R / F (tilt keys; see note below) |
| Center on group | Double-tap a control group key |
| Click-pan via minimap | Left-click on the minimap |

> **Note on F:** F doubles as a tilt key and as the **Default stance** key.
> Context (whether you have battalions selected) determines which.

### Keyboard Hotkeys

| Key | Command |
|---|---|
| **A** | Enter Attack-Move mode — next right-click is an attack-move order. |
| **P** | Enter Patrol mode — next right-click sets a patrol endpoint. |
| **S** | Stop — clear all commands on selected units. |
| **H** | Hold Position — stay in place but defend in range. |
| **D / F / G** | Stance: Aggressive / Default / Defensive. |
| **B** | Cycle through idle Builders and center the camera. |
| **Z** | Enter / exit Planning Mode (queue commands visually, execute on confirm). |
| **Esc** | Cascading: close menu → exit mode → clear selection → open menu. |
| **1–9** | Recall control group. |
| **1–9 (double-tap)** | Recall **and** center camera. |
| **Ctrl + 1–9** | Save selection as control group. |
| **Shift + 1–9** | Add selection to control group. |

### Right-Click — Context-Sensitive

Right-click does whatever makes sense for the target:

| Target | Result |
|---|---|
| Ground | Move (in formation). |
| Enemy unit / building | Attack. |
| Friendly damaged building | Repair (with builders). |
| Friendly under-construction building | Resume building (with builders). |
| Friendly unit (Litharch selected) | Heal. |
| Resource node / Cadaver (Worker selected) | Gather. |
| Hall or GathererHut (Worker selected) | Return cargo and deposit. |
| Smelter (Worker selected) | Supply the Smelter with iron and crystal. |
| Crystal Main Node, active (Scholar selected) | Begin Purification ritual (Alanthor). |
| Crystal Main Node, active (Acolyte selected) | Begin Conversion ritual (Runai). |
| Ground or unit, with a building selected | Set Rally Point. |

**Shift + Right-Click** — Queue a waypoint instead of replacing the current
order. Hold Shift to chain several; release to execute the chain.

### Control Groups

Save groups with Ctrl+1..9, recall with 1..9, add to a group with
Shift+1..9. Double-tap a number within 0.3 seconds to recall **and** center
the camera.

### UI Panels

| Panel | Location | Contents |
|---|---|---|
| **Resource HUD** | Top / Bottom-right | Supplies, Iron, Crystal, Veilsteel, Glow — live values. |
| **Game Stats** | Bottom-center | Population, unit count, building count, focus count. |
| **Entity Info Panel** | Left | Portrait, HP, stats; or multi-selection grid for big groups. |
| **Entity Action Panel** | Left | Build buttons, train queue, research, stance buttons. |
| **Spell Panel** | Left | Active sect abilities with cooldowns (only after adopting a sect). |
| **Minimap** | Corner | Terrain, fog of war, ally / enemy markers; click to pan. |

### World Overlays

- **Formation Preview** — Green chevrons mark each unit's destination slot and facing.
- **Rally Point Display** — Blue marker plus a path line from the source building.
- **Floating Health Bars** — Above every unit, colored by HP%.
- **Floating Income** — `+10/s` style readouts above Workers and supply buildings.
- **Formation Drag Preview** — Hold right-click instead of clicking to see, in real time, where each unit will end up before you commit.

### Building Placement

When you queue a building it follows the cursor as a ghost:

- **Green** = valid placement; **Red** = blocked.
- **Mouse Wheel** rotates non-wall buildings in 15° steps.
- **Wall hubs** snap to nearby existing hubs (≤2 units).
- **Shift + Click** to place keeps you in placement mode — drop several in a row.
- **Right-click or Esc** to leave placement mode.

---

## 4. Combat — Damage Math

> Full unit stats, costs, and roles live in [docs/Design/](docs/Design/). This
> section covers only the runtime math the player will see in tooltips.

### Targeting

Units scan a spatial grid for enemies inside their line-of-sight radius
(default 20 for battalions) and acquire the closest. **Explicit player move
orders pause auto-acquire** — a unit you sent somewhere will not break off
on its own unless attacked.

### Damage Formula

`finalDamage = baseDamage × dmgTypeVsArmor × (1 − defense / (defense + 100))`

- **Base damage** from the unit's damage component.
- **Damage type vs. armor type** matrix (e.g., slashing vs. plate).
- **Height modifier:** ±4% per unit of elevation difference, capped at ±20%.
- **Diminishing returns** on stacked defense values.
- **Minimum damage 1** — every hit takes at least 1 HP.
- Spell buffs (e.g., **Fortitude**) and debuffs (e.g., burning ground) multiply outgoing or reduce incoming.

### Melee

Engages at ~1.5 unit range. Melee chases targets that flee. If pursuit
times out (~5 seconds outside their leash on Default stance), they return
to their guard point.

### Ranged

Archers have three rings:

- **Minimum range (~10 units):** if an enemy gets inside, the archer **retreats**.
- **Optimal range (10–25):** stops, aims (AimTime), fires.
- **Maximum range (~25):** chases until the target enters the optimal band.

Projectiles travel at 30 units/sec (arrows) or 55 (siege bolts) and apply
damage on hit.

### Special Combat Mechanics

- **Healing** — Litharchs restore HP to nearby allies. (Litharchs have **0 base damage** — they cannot attack unless **Warrior priests** is researched at the Shrine of Ridan, per [Age_0.md](docs/Design/Age_0.md).)
- **Spell Buffs** — Temporary status effects from sect spells (damage, cooldown, invulnerability).
- **Mind Control** — Flips a unit's allegiance for a duration, then returns it.
- **Summons** — Spawned units expire on timer or when the summoner dies.
- **Burning Ground** — Damage-over-time zones; persistent until destroyed.
- **Shield Bars** — Some units carry shield HP that absorbs damage and regenerates out of combat.
- **Iconoclast aura** — Strips `NodeUntargetable` from crystal nodes in a 12u radius, allowing other Feraldis units to damage them. The Iconoclast itself does not attack.

### Death

Dying units play a 2-second death animation; buildings collapse for 2
seconds. The death system cleans up dangling target references so no unit
shoots a corpse.

---

## 5. Victory Conditions

There are **three paths to victory**, plus the threat of being eliminated.

### Path 1 — Last Faction Standing

A faction is **eliminated** when it owns **zero completed buildings**. The
check runs every 2 seconds after a 10-second grace period. If only one
player faction remains, that player wins.

- If you are eliminated → **DEFEAT** screen.
- If you alone remain → **VICTORY** screen.

### Path 2 — Node Victory (Culture-Specific)

Each culture has its own win path against the Crystal Curse:

| Culture | Condition |
|---|---|
| **Alanthor** | All Crystal Main Nodes must be **Cleansed by Alanthor** and held for **5 minutes**. |
| **Runai** | All Crystal Main Nodes must be **Converted by Runai** and held for **5 minutes**. |
| **Feraldis** | **Destroy all** Crystal Main Nodes — instant win, no hold time. |

When the condition is met the **Node Victory** banner fires and the match
ends.

### Path 3 — Surrender

The **End Game** button lets you concede the match (recorded as a defeat).

### Game Modes Without Victory

Sandbox and Battalion Test have no victory conditions — they exist for
testing and free play.

---

## 6. AI Opponents

Computer-controlled factions run on the **AIBrain** with two axes:

### Personality

- **Balanced** — General-purpose.
- **Aggressive** — Early military, harassment.
- **Defensive** — Standing army, fortification.
- **Economic** — Boom first, military later.
- **Rush** — Minimum economy, fast military strike.

### Difficulty

- **Easy / Normal / Hard / Expert.**
- Difficulty controls **think-tick interval** (Expert ticks every 0.2s;
  Easy is much slower) and decision quality.

### AI Strategies

Each AI commits to one strategy at match start and follows a locked-in
Age 0 build order:

| Strategy | Plan |
|---|---|
| **Rush** | Fast Barracks, early harassment, minimal economy. |
| **EcoBoom** | Heavy gathering, crystal farming, late military. |
| **TechRush** | Race to Age 1 with infantry tech. |
| **Aggressive** | Balanced military + Shrine + Age-up. |
| **Defensive** | Standing army, Iron Armor research, Vault. |
| **Turtle** | Heavy economy + healers, stockpile for walls. |

The AI maintains its desired army size by re-queueing replacements for
lost units, allocates Workers between iron and crystal based on the chosen
strategy, and remembers scouted enemy positions in
`EnemyLastKnownPosition`.

---

## 7. Multiplayer

Multiplayer is implemented as **deterministic lockstep**:

- Every client runs the same game logic on the same tick.
- Commands (build, train, attack) are queued through `LockstepManager`,
  broadcast, and executed by all clients in identical order on the same
  tick.
- Random number generation is seeded from the current tick to stay
  synchronized.
- The host acts as tie-breaker; clients send their commands to the host,
  which echoes confirmed ticks.

The lobby (`LobbyUI`, `LobbyManager`) handles player setup, color
selection from the 12-color pool, and game-mode configuration before the
match starts.

---

## 8. Appendices

### A. Quick Reference Card

| Need to… | Do this |
|---|---|
| Mine iron | Right-click an iron deposit with a Worker selected. |
| Drop off resources | Right-click any Hall or Gatherers' Hut with a loaded Worker. |
| Build a wall (Alanthor) | Place Hubs; segments and instances spawn automatically. |
| Upgrade a wall piece | Select the instance and choose Tower or Gate from the action panel. |
| Heal a friendly unit | Right-click it with a Litharch selected. |
| Convert a Crystal Main Node | Channel **Acolyte** (Runai) or **Scholar** (Alanthor) on an active node. |
| Destroy a Crystal Main Node | You need **Iconoclasts** (Feraldis) to bypass node invulnerability. |
| Save a control group | Select your units, press Ctrl+1 through Ctrl+9. |
| Repeat-place buildings | Hold Shift while placing — stay in placement mode. |
| Queue waypoints | Hold Shift and right-click along the path. |

### B. Faction Color Pool

Blue, Red, Green, Yellow, Purple, Orange, Teal, Silver, Pink, Brown,
Black, Maroon. Pool position determines `Faction` enum index (Blue = 0 …
White = 7+).

### C. Hard Caps & Limits

| Stat | Cap |
|---|---|
| Per-resource bank | 100,000 |
| Population | 200 (Runai and Feraldis are auto-set to this cap at age-up — see [docs/Design/Overview.md § Population model](docs/Design/Overview.md#population-model-cross-faction-summary)) |
| Sects per faction | 6 of 12 |
| Control groups | 9 (digits 1–9) |
| Hold time for Alanthor / Runai node victory | 5 minutes |

### D. Spell / Ability Targeting

If a sect ability needs a target, clicking its Spell Panel button enters
**targeting mode** — the cursor changes, the next valid right-click
executes the spell. Esc cancels.

### E. Planning Mode (Z)

Press **Z** to enter Planning Mode. Queue moves, attacks, and other
commands visually without committing them. Press Z again or Enter to
execute the entire plan at once; Esc cancels.

---

*This manual reflects the source code in `Assets/Scripts/` on branch
`test/all-fixes-rolled-up`. Numbers, costs, and timings pulled from
`Core/Settings/`, `Economy/`, and the entity definitions in
`Entities/Units/` and `Entities/Buildings/` may diverge from the design
truth in [docs/Design/](docs/Design/) — when in doubt about gameplay
intent, the Design folder wins.*
