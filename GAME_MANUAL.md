# The Waning Border — Player Manual

A real-time strategy game of culture, conquest, and the slow tide of the
The Border.

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
rising cultures on a continent slowly being consumed by the The Border.
You start in a **neutral, pre-culture age (Age 0)**, gather supplies and
iron, and at the right moment commit your people to a culture — **Runai**
(nomadic traders), **Alanthor** (defensive forgemasters), or **Feraldis**
(fierce warbands). Each culture rewrites your economy, your buildings, and
your military doctrine.

Beyond the rival player factions, every map is haunted by **Veilstone Main
Nodes** — alien growths that spawn hostile veilstone creatures and slowly
spread border ground. Defeating, cleansing, or converting these nodes is
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
| **Solo vs. Border** | A single player against the The Border AI. |
| **Scenario** | Pre-built maps such as *LargeMelee*, *FourWayCultures*, *WallSiege*. |
| **Sandbox** | Unrestricted building and testing — no victory conditions. |
| **Battalion Test** | Minimal bootstrap for testing formations. |
| **Pathfinding Test** | A diagnostic mode for navigation. |

### Starting Conditions

Every faction begins identically, regardless of mode:

- **Supplies:** 400
- **Iron:** 150
- **Veilstone:** 0
- **Veilsteel:** 0
- **Glow:** 0
- **Age:** 0 (pre-culture / neutral aesthetic)
- **Religion Points:** 0
- **Starting structures:** One Shelter (your capital), placed at your spawn position.
  At age-up it automatically becomes the **Fortress** for every culture — the
  same building renamed, with Fortress levels L1-L3. There is no Hall, King's
  Court or Town Hall.
- **Starting army:** **5 Spearmen, 1 Scout and 3 Workers** — no Archers;
  Age 0 is the melee age and the bow arrives with the Age 1 Archery Range.
  One finished **House** stands beside the Shelter.
- **Population cap:** the Shelter's housing plus the starting House's (both
  set on their SOs — see the calculator `tools/calculator/TechTree.html`).

The map will also have one or more **Veilstone Main Nodes** pre-placed and a
scatter of **Cadavers** (veilstone corpses) to mine.

For the full breakdown of what you can build / train / research in Age 0,
see [docs/Design/Age_0.md](docs/Design/Age_0.md).

---

## 3. Controls & Interface

**Walls (Palisade in Age 0, Stone Wall for Alanthor):** pick the wall in the worker palette, then **press and drag** on the ground to draw it — it follows the cursor as a curve and **retracing over the path erases it** back to that point. Release to build the whole line; a plain click still places a single hub. Right-click / Esc cancels. What the code does today:
- Hubs go at the two **ends** of the stroke; a run longer than **11 modules (33 m)** gets extra hubs at **equal intervals** along it (`WallDrawTool.asset`). At most **24 hubs** per stroke.
- The curtain is laid in **3 m modules** and you pay per hub **and** per module.
- A bend tighter than a **12 m radius**, or a path crossing itself, turns red and is refused.
- The start and end **snap** onto a friendly hub of the same kind within **about 4.2 m** (twice the hub's radius) or onto a friendly wall section within **3 m** (one module), which becomes a hub — that is how you join or branch walls. Palisades and stone walls never join each other.
- Hubs do **not** snap to the build grid; the wall runs exactly where you drew it, on ground you own along its whole length.

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
| Pan | Hold **Middle Mouse** + drag, or push the cursor to a **screen edge** |
| Zoom | **Mouse wheel** (range ~15–80 units) |
| Rotate | **Ctrl + Mouse wheel** |
| Centre here | **Left-click the minimap** |
| Centre on group | Double-tap a control group key |

> **Tilt is not a control.** The camera pitches automatically with zoom: close
> in it sits low and near-horizontal, zoomed out it rises toward top-down. There
> is no key for it.
>
> **The minimap turns with you.** Its "up" is always the direction you are
> facing, so a blip's position on the minimap matches where it is on screen
> without any mental rotation.
>
> **There is no keyboard pan.** Arrow-key panning was removed (2026-08-28) so
> **A** stays unambiguously attack-move and **D** unambiguously the Defensive stance.
> Pan with the middle mouse button, the screen edge, or the minimap.
>
> Edge panning does **not** fire while the cursor is over the interface — the
> minimap sits in the bottom-left corner, which is inside the edge band, so
> reaching for it would otherwise scroll the view away from what you were about
> to click.

### Keyboard Hotkeys

| Key | Command |
|---|---|
| **A** | Enter Attack-Move mode — next right-click is an attack-move order. |
| **P** | Enter Patrol mode — next right-click sets a patrol endpoint. |
| **S** | Stop — clear all commands on selected units. |
| **H** | Stance: **Hold Position** — stop, and from now on stay completely passive: never moves on its own and does not even return fire. Your orders still work. |
| **D** | Stance: **Defensive** (the default for your units) — returns fire on whatever attacks it, if it can reach it from where it stands; never pursues. |
| **G** | Stance: **Aggressive** — engages anything it can see and pursues it up to 30 m from its post, then walks back. |
| **X** | Cycle formation shape: Box → Line → Wedge → Staggered. Re-slots the current selection immediately (AoE4-style). |
| **B** | Cycle through idle Workers and center the camera. |
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
| Ground | Move (in formation: units hold their shape en route — melee front, ranged back — at the slowest member's speed, slows included, with a +40% catch-up boost for stragglers; workers and far-away outliers path independently). The rear ranks keep formation right up to their own slots after the front has arrived. On an attack-move a unit that steps out of rank to fight falls back in once its fight is over. Formations hold their shape in multiplayer too. |
| Overpass bridge deck | Send the selection OVER the bridge — any unit type; they climb a ramp, cross the deck, and descend the far side. Units not ordered onto the deck simply walk UNDER the span. |
| Enemy unit / building | Attack. |
| Friendly damaged building | Repair (with workers). |
| Friendly under-construction building | Resume building (with workers). |
| Friendly unit (Litharch selected) | Heal. |
| Veilstone Main Node, active (Acolyte selected) | Begin Conversion ritual (Runai). |
| Ground or resource, with only buildings selected | Set Rally Point. |
| Enemy, with a shooting building selected (Shelter/Fortress, tower, Keep, wall tower) | Direct its fire: the enemy takes one of the building's target slots while in range; the rest keep auto-firing. The order holds if the target walks out of range, and ends when it dies or you press **Stop**. On an emplacement, its engine attacks. |
| Enemy wall piece, no siege selected | Nothing — "Only siege can damage walls". Only siege units (and siege-firing buildings) can damage walls. |

**Shift + Right-Click** — Add an order to the END of each selected unit's
queue instead of replacing what it is doing (up to **15** queued orders per
unit). The first order starts at once if the unit is idle; the rest run in
turn as each one finishes. What you click decides the order: an enemy is a
queued **attack**, your construction site a queued **build**, a damaged
building a queued **repair**, a wounded ally a queued **heal** (for units that
can), anything else a queued **move** — or, with **A** / **P** armed, a
queued attack-move / patrol. Queued moves, attack-moves and patrols are
marched **in formation**; attacks and work orders are per unit. A queued
attack is dropped if its target dies or leaves your (and your allies')
sight. While walking queued moves, units **hold fire** — waypoints outweigh
the Aggressive stance; queue an attack-move if the route should fight. The
queued route is drawn with the usual movement lines. Any ordinary order
clears the queue.

**Idle soldiers form up.** Four or more idle soldiers standing together (and
nowhere near a fight) tidy themselves into ranks where they stand, in the
formation shape they last marched in. A formation that has just arrived is
left alone, and one formation is never split into two. Units on Hold
Position are never moved.

### Control Groups

Save groups with Ctrl+1..9, recall with 1..9, add to a group with
Shift+1..9. Double-tap a number within 0.3 seconds to recall **and** center
the camera.

### UI Panels

| Panel | Location | Contents |
|---|---|---|
| **Resource HUD** | Top / Bottom-right | Supplies, Iron, Veilstone, Veilsteel, Glow — live values. |
| **Game Stats** | Bottom-center | Population, unit count, building count, focus count. |
| **Entity Info Panel** | Left | Portrait, HP, stats; or multi-selection grid for big groups. |
| **Entity Action Panel** | Left | Build buttons, train queue, research. With units selected: the four formation shapes (top row) and the three stances (second row). The stance the whole selection shares is lit gold; in a mixed selection every stance present is lit dimmer and its tooltip says how many units hold it. |
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

- **Green** = valid placement; **Red** = blocked. Clicking a red ghost tells you
  **why** — not your territory, held by another player or the curse, this
  territory already has a capital, not adjacent, worker too far, no worker
  selected, unsuitable ground, something already built there, or the building's
  own rule (a free node, blood, a forest).
- **Claiming territory with a capital** (the Shelter in Age 0, the Fortress
  after age-up): the capital is buildable, locks the territory it stands in,
  and is the one building you may place outside your own ground, and only
  - in an unclaimed territory that **borders one you already hold** — no
    hopping across the map; and
  - while **one of your selected workers stands within 30 m** of the site.
    Walk a worker out first, then place. The worker is named in the order, so
    in multiplayer the claim is refused if that worker has died or wandered off
    by the time the order runs.
  One capital per territory. See [docs/Design/Regions.md §2](docs/Design/Regions.md).
- **Mouse Wheel** rotates non-wall buildings in 15° steps.
- **Wall hubs** snap to a friendly hub of the same kind within about 4.2 m, or onto a friendly wall section within 3 m (see Walls above); they are not grid-snapped.
- **Shift + Click** to place keeps you in placement mode — drop several in a row.
- **Right-click or Esc** to leave placement mode.

---

## 4. Combat — Damage Math

> Full unit stats, costs, and roles live in [docs/Design/](docs/Design/). This
> section covers only the runtime math the player will see in tooltips.

### Targeting

Idle units pick their own targets by **stance** (see
[docs/Design/Stances.md](docs/Design/Stances.md)):

| Stance | Picks a fight with | Returns fire | Pursues (from its post) |
|---|---|---|---|
| **Aggressive** (AI default) | anything inside its line of sight | yes | up to 30 m, then walks back |
| **Defensive** (your default) | only an enemy attacking it, inside its reach | yes | never |
| **Hold Position** | nothing | no | never |

- **Your orders always win.** An attack order is carried out on every
  stance — a unit on Hold walks over and fights the target you gave it.
  Stances only decide what a unit does *on its own*.
- **Emplaced engines** (Ballista / Trebuchet emplacements) are fixed
  mounts: they always shoot whatever comes into range and never move.
- **Support units** (Litharch, Lorekeeper, Inquisitor) never go looking
  for a fight; once armed they only fight back, and never chase into
  melee. A Litharch heals from a few metres back, prefers patients who are
  not in melee, and steps away when an enemy gets close.

- A unit's **post** is where it was last sent: a finished move, its rally
  point, or where you put it on Hold.
- A stance is a mode, not an order: it survives every order — including
  **Stop** — until you pick another.
- **Explicit player move orders pause auto-acquire** — a unit you sent
  somewhere will not break off on its own.
- **Your attack orders are never cut short** by the leash, the stance or
  stuck recovery; the unit keeps after the target you gave it.
- **Attack-move and patrol** fight their way along with the Aggressive rules,
  whatever the stance, and resume the march after each fight.

### Damage Formula

`finalDamage = max(1, baseDamage − armor) + bonusVsTags`

- **Base damage** from the unit's damage component; **armor** is the target's
  defense for that kind of attack (melee / ranged / siege / magic), subtracted
  flat.
- **Bonus vs tags** — hard counters (e.g. Spearman vs Cavalry) add flat damage
  after armor; see [docs/Design/Combat_Pacing.md](docs/Design/Combat_Pacing.md).
- **Height modifier:** ±4% per unit of elevation difference, capped at ±20%.
- **Minimum damage 1** — every hit takes at least 1 HP.
- Every unit's numbers are on its SO; read them in `tools/calculator/TechTree.html`.
- Spell buffs (e.g., **Fortitude**) and debuffs (e.g., burning ground) multiply outgoing or reduce incoming.

### Melee

Engages at ~1.5 unit range. Melee chases targets that flee — always for a
target you ordered; for a target it picked itself only on Aggressive (and
attack-move / patrol), as far as the 30 m leash allows, then it returns to
its post and will not re-engage for a moment. A Defensive unit lets a
fleeing attacker go.

**Flanking.** A melee blow that lands on a unit's side or back deals extra
damage (the multiplier is `flankDamageMultiplier` on
`MeleeCombatSystem.asset`). It works for everyone, the AI and the
curse included. Units turn to face what they fight and where they walk, so the
way to land it is to hit a unit that is already locked in front of someone
else, or one that is running away. Buildings have no sides and cannot be
flanked; arrows and spells never flank. See
[docs/Design/Combat_Pacing.md](docs/Design/Combat_Pacing.md) § Flanking.

### Ranged

Bow units have **no minimum range** (only siege engines keep a dead zone):

- **Inside range:** stops, aims (AimTime), fires.
- **Outside range:** chases until the target is in range — only for an ordered target, or on Aggressive / attack-move; a Defensive or Hold archer lets it go.
- Range never exceeds the unit's line of sight, and rises Archer → Crossbowman → Longbowman.

Projectiles travel at 30 units/sec (arrows) or 55 (siege bolts) and apply
damage on hit.

### Special Combat Mechanics

- **Healing** — Litharchs restore HP to allies in heal range. (Litharchs have **no attack** unless **Warrior Priests** is researched at the Temple of Ridan, per [Age_0.md](docs/Design/Age_0.md).)
- **Watch Tower garrison** (Alanthor) — up to 4 foot units inside a Watch Tower; each adds one target to its volley.
- **Spell Buffs** — Temporary status effects from sect spells (damage, cooldown, invulnerability).
- **Mind Control** — Flips a unit's allegiance for a duration, then returns it.
- **Summons** — Spawned units expire on timer or when the summoner dies.
- **Burning Ground** — Damage-over-time zones; persistent until destroyed.
- **Shield Bars** — Some units carry shield HP that absorbs damage and regenerates out of combat.
- **Iconoclast aura** — Strips `NodeUntargetable` from veilstone nodes in a 12u radius, allowing other Feraldis units to damage them. The Iconoclast itself does not attack.

### Death

Dying units play a 2-second death animation; buildings collapse for 2
seconds. The death system cleans up dangling target references so no unit
shoots a corpse.

---

## 5. Victory Conditions

There are **three paths to victory**, plus the threat of being eliminated.

### Path 1 — Last Faction Standing

A faction is **eliminated** when it has **no Fortress, no military building
and no Worker**, or when it has held **no territory** for a short grace period
(docs/Design/Territory_Claims.md § 7). Everything an eliminated faction still
owns is destroyed. If only one player faction (or team) remains, it wins.

- If you are eliminated → **DEFEAT** screen.
- If you alone remain → **VICTORY** screen.

### Path 2 — Node Victory (Culture-Specific)

Each culture has its own win path against the The Border:

| Culture | Condition |
|---|---|
| **Alanthor** | All Veilstone Main Nodes must be **Cleansed by Alanthor** and held for **5 minutes**. |
| **Runai** | All Veilstone Main Nodes must be **Converted by Runai** and held for **5 minutes**. |
| **Feraldis** | **Destroy all** Veilstone Main Nodes — instant win, no hold time. |

When the condition is met the **Node Victory** banner fires and the match
ends.

### Path 3 — Surrender

The **End Game** button lets you concede the match (recorded as a defeat).

### After the Match — the Muster Rolls

The victory / defeat screen has a **Muster Rolls** button that opens the
post-game report: a **Standings** table (Score with its Economy / Strategy /
Military parts, K/D, kills, deaths, territories, Fortresses, techs) with the
score over time, **Charts** of every faction's banks, army, population,
buildings and territories over the match (click a faction in the legend to
hide or show it), and a **Map** that replays the whole match from above —
territories by owner, buildings, units and deaths — with Play / Pause, five
speeds, a timeline scrubber, mouse-wheel zoom and drag to pan. **Close**
returns to the victory screen. Every match is recorded for it automatically
(docs/Design/Muster_Rolls_PostGame.md).

### Saving, Loading and Replays

Single-player skirmishes can be saved and every one is recorded
(docs/Design/Replays_And_Saves.md):

- **Save Game** — pause menu (**Esc**). Saves go to the `Saves` folder next to
  the game.
- **Load Game** — main menu. The **Saved Games** tab resumes a save: every
  unit, building and order comes back exactly where it was and you are back in
  control. The AI picks up from the board as it stands.
- **Replays** — the same screen's **Replays** tab, or **Watch Replay** on the
  end-of-match screen. A replay shows the whole match with full vision; click a
  unit or building to see through that player's eyes. The bar at the top has
  pause (**Space**), speeds, skip ahead, and a timeline you can click to jump.
  It also says whether the replay is still *in sync* with the match that was
  played.
- Saves and replays only open on the game version that made them.
- Scenarios, the sandbox and the tutorial are not recorded and cannot be saved.

### Game Modes Without Victory

Sandbox and Battalion Test have no victory conditions — they exist for
testing and free play.

---

## 6. AI Opponents

Computer-controlled factions run on the **AIBrain** with two axes:

### Personality

The lobby's strategy dropdown (RANDOM / ECONOMIST / BALANCED / TECHNOLOGIST
/ AGGRESSOR / TURTLE / DEFENDER) picks one per AI slot; it is kept for the
whole match. RANDOM gives each colour its fixed personality (Red and Orange
rush, Yellow techs, Green booms, Blue turtles, White is Aggressive) and
rolls one for any other colour. A personality is a row of tuning numbers
(`Assets/Resources/AISettings.asset`) — which strategic plans it leans
toward, how big a standing army it keeps, how much risk it accepts, whether
it raids, how many huts it founds, when it pushes the age-up, how much it
fortifies — never a script.

- **BALANCED** — The reference. Mild lean to massing and teching, raids.
- **AGGRESSOR** (Rush) — The hardest lean in the table: constant pressure
  with cheap units, the least caution, fewest huts, most barracks, ages up
  last, almost no towers or walls.
- **DEFENDER** — Fortifies first, techs second; cautious, no raids, a big
  standing army, many towers and walls.
- **ECONOMIST** — Booms first, techs second; the smallest army, the most
  huts, ages up early, no raids.
- **TECHNOLOGIST** — Techs first; the earliest age-up push, spends its
  veilstone on role units rather than basics, no raids.
- **TURTLE** — Fortifies with a little massing; the most cautious, the
  biggest standing army and the most towers and walls.
- **Aggressive** (RANDOM only — White, or the roll) — Rushes and masses,
  accepts risk, raids, more barracks and fewer huts, ages up late (after
  its first wave), few towers or walls.

### Difficulty

**Easy / Normal / Hard / Expert.** Modeled on Age of Empires IV: difficulty
is pure behavior quality — **no AI tier ever cheats resources or vision**.
Each tier is a data profile:

| Knob | Easy | Normal | Hard | Expert |
|---|---|---|---|---|
| Think tick (s) | 5.0 | 2.0 | 0.5 | 0.25 |
| Worker target (Age 0 → 1) | 8 → 12 | 12 → 18 | 16 → 24 | 20 → 30 |
| Earliest attack | 10:00 | 7:00 | 5:00 | 4:00 |
| Economy raids | — | ✓ | ✓ | ✓ |
| Counter-composition | — | — | ✓ | ✓ |
| Forward staging before attacks | — | — | ✓ | ✓ |
| Sustained army cap | 10 | 16 | 24 | 32 |

### The Curse, the Wells & the Shardroot

The map's **wells** (Border nodes) are the game's only veilstone source —
each is ringed by a guarded crystal field. Every culture has one **verb**:
**Feraldis destroys** (the well shatters into a lootable shard field),
**Runai pacifies** (a converted well trickles veilstone to its owner),
**Alanthor purifies** (a cleansed well generates veilstone). Every hold
lasts **10 minutes** — but applying your verb to another well **refreshes
all your holds** (stay active or the curse returns). Claimed wells can be
attacked to break the hold; destroyed wells are untouchable until they
respawn. **Hold every well on the map at once and you WIN** — reaching
all-but-one triggers a map-wide warning, so expect company.

One well secretly holds the **SHARDROOT**. The first player to claim that
well unearths it: a persistent artifact any unit can carry (visible to
everyone on the minimap). Deliver it to your **Fortress** to awaken the
**Shardbound Hero**, or to your **Temple** to enshrine it (all god/sect
powers surge — but the Temple detonates catastrophically if it falls, and
the Shardroot drops in the crater). The choice is locked until the vessel
dies. And beware: while you hold it, **the Border hunts you**.

### Scout Vision

A Scout always sees its full line of sight (**40 m**), moving or standing
still. (The old perch-and-bloom vision, which shrank while moving and grew
while standing, was removed on 2026-09-29.)

### How the AI lays out its base

Buildings may sit flush against each other, for you and for the AI. The AI
prefers a one-cell (2 m) lane and builds flush when its ground is full, but
it never seals its own base: every building keeps a free side, every
production building keeps its unit exit clear, gates and the way out stay
reachable, no open ground is walled off, and no more than a few buildings
stand in one flush row. Mines, veilstone mines and gatherer's huts stand on
their resource node wherever the map put it. An Alanthor AI that has planned
a perimeter wall builds everything inside it.

The AI develops every territory it holds in a fixed order: resource
buildings on its nodes first, then watch towers facing hostile or unclaimed
neighbours, then production buildings (so its armies are trained near the
front), then a Fortress. A spot for that Fortress is kept free from the
moment the territory is taken. See docs/Design/Game_AI.md §5g and §6b.

The AI claims territory under the same rules you do: only territories that
border ground it holds, and only once a worker has walked to the capital site —
you will see a lone worker head out to a neighbouring territory a little
before its capital foundation appears there.

### Observer Mode (AI vs AI)

Toggle **OBSERVER** in the Skirmish match options to spectate an AI-only
match: every slot — including yours — becomes an AI warband with its own
strategy and difficulty dropdowns (at least 2 AI required). As an observer
you have **full map vision**, free camera, and can select any unit or
building to inspect it (the resource bar follows whatever faction you have
selected), but you cannot issue commands. The match runs until one AI
faction remains and ends with a "&lt;faction&gt; WINS" banner.

### Strategic plans (what an AI is doing right now)

Beyond its opening build order, every AI holds a **plan** — a named
intention that decides how it spends, how big an army it wants, and how
much it needs before it will attack. The plan is announced in
`logs/<session>/AI_<colour>.log` as a `PLAN:` line with the reason, so you
can watch a match and know what each faction is up to.

| Plan | What you should see |
|---|---|
| **BOOMING** | Claiming territory twice as often, army kept deliberately thin. A bet that nobody punishes it. |
| **MASSING** | Most income into troops, army target well past its normal cap, and it *waits* — it will not attack until the force is big. |
| **RUSHING** | Same military spending, opposite trade: attacks with half the normal count, constantly. |
| **TECHING** | Advancement takes the lion's share while it races the age-up and the elite tier it unlocks. |
| **FORTIFYING** | Keeps a real standing army but will not go looking for a fight. |

A plan is **committed** for 90–140 seconds, so it lasts long enough to see
and to accomplish something. The one thing that breaks commitment early is
an enemy in the base — no personality gets a vote on that.

**Plans are chosen by reading the board, not just its own state.** An AI
that sees a rival holding more ground than it can defend will switch to
RUSHING to punish the greed; one facing an army twice its size will
FORTIFY; one that is ahead on army will MASS and commit. Personality biases
the choice, so on an even board four AIs will pick four different plans —
and on a decisive one (a deathball on the map, a base under attack) they
will correctly all reach the same conclusion.

The AI reads territory counts (public — they are on the map) and army
sizes (a human would have to scout for this; it is a deliberate difficulty
assist, like the AI knowing where the resource nodes are).

### How the AI plays its personality

There is no scripted opening: from the first second the AI runs an
AoE4-style maintenance brain steered by its personality's numbers and its
current plan. It keeps workers at its worker rule (three plus one per
territory it conquers), grows huts toward its personality's cap, keeps its
standing army at its personality's floor, trains a mixed army toward a
composition that counter-picks what it has scouted of yours, and launches
**missions** — armies that march in formation, stage near the target before
committing, raid your economy with fast parties (if its personality raids),
retreat when locally outmatched, and regroup at home. All of it is
fog-of-war honest: the AI only acts on what its own units have scouted.

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
| Get iron | Build a Mine on an iron node in ground you hold; it pays straight into your stockpile. Workers only build and repair. |
| Build a wall | Pick the Palisade (Age 0) or Stone Wall (Alanthor) and **drag** the line; hubs and sections are laid along it. |
| Convert a wall section | Select the section: **Gate** (needs 4 clear sections; the gatehouse takes 3) or **Hub**; on a stone wall also **Tower** (3 clear sections), **Mount Ballista** (wall level 2+) or **Mount Trebuchet** (wall level 3) — emplacements are built by workers. |
| Raise the stone wall's level | Select any Wall Hub and research **Battlements**, then **Shielded Ramparts** — every wall you own is re-clad at once. |
| Garrison a Watch Tower (Alanthor) | Right-click it with **foot units** selected: up to **4** go inside, and each one adds a target to the tower's volley. |
| Heal a friendly unit | Right-click it with a Litharch selected. |
| Convert a Veilstone Main Node | Channel **Acolyte** (Runai) on an active node. |
| Destroy a Veilstone Main Node | You need **Iconoclasts** (Feraldis) to bypass node invulnerability. |
| Save a control group | Select your units, press Ctrl+1 through Ctrl+9. |
| Repeat-place buildings | Hold Shift while placing — stay in placement mode. |
| Queue orders | Hold Shift and right-click along the path — targets, sites and ground (up to 15). |

### B. Faction Color Pool

Blue, Red, Green, Yellow, Purple, Orange, Teal, Silver, Pink, Brown,
Black, Maroon. Pool position determines `Faction` enum index (Blue = 0 …
White = 7+).

### C. Hard Caps & Limits

| Stat | Cap |
|---|---|
| Per-resource bank | 100,000 |
| Population | `FactionPopulation.AbsoluteMax` (300) (Runai and Feraldis are auto-set to the cap at age-up — see [docs/Design/Overview.md](docs/Design/Overview.md)) |
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
