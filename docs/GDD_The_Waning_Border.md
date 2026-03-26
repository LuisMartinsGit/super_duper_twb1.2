# The Waning Border — Game Design Document

**Version:** 1.2
**Engine:** Unity 6 (6000.0.37f1) · DOTS/ECS (Entities 1.3.14)
**Genre:** Real-Time Strategy
**Players:** 1–8 (Multiplayer via Lockstep)
**Platform:** PC (Windows)

---

## Table of Contents

1. [Game Overview](#1-game-overview)
2. [Core Pillars](#2-core-pillars)
3. [World & Map Generation](#3-world--map-generation)
4. [Factions & Player Setup](#4-factions--player-setup)
5. [Resources & Economy](#5-resources--economy)
6. [Era Progression](#6-era-progression)
7. [Buildings](#7-buildings)
8. [Units](#8-units)
9. [Combat System](#9-combat-system)
10. [Religion & Sects](#10-religion--sects)
11. [The Crystal Curse Faction](#11-the-crystal-curse-faction)
12. [Cultures](#12-cultures)
13. [AI System](#13-ai-system)
14. [Controls & Camera](#14-controls--camera)
15. [User Interface](#15-user-interface)
16. [Fog of War & Visibility](#16-fog-of-war--visibility)
17. [Multiplayer](#17-multiplayer)
18. [Appendix: Stat Tables](#appendix-stat-tables)

---

## 1. Game Overview

**The Waning Border** is a real-time strategy game set in a world consumed by a supernatural phenomenon known as **The Curse** — an ever-expanding crystalline corruption that devours the land. Players command human factions struggling to survive, build economies, train armies, and confront both rival players and the autonomous Crystal Curse that threatens to consume everything.

The game blends classic RTS base-building and combat with a unique three-way tension: players must fight each other for dominance while managing their relationship with the Curse. Three divergent cultures offer fundamentally different philosophies toward the Curse — preserve it, exploit it, or destroy it — creating asymmetric strategies and emergent alliances.

**Win Condition:** Eliminate all enemy Halls (main base buildings) or be the last faction standing.

---

## 2. Core Pillars

### 2.1 The Curse as Living Opponent
The Crystal Curse is not a passive environmental hazard — it is a fully autonomous AI faction that builds, spawns combat units, defends territory, and launches coordinated attacks against player bases. It escalates over time, creating an ever-present threat that shapes every strategic decision.

### 2.2 Cultural Divergence
All players begin as a unified human civilization. At Era 2, each player chooses one of three cultures — Runai, Alanthor, or Feraldis — unlocking unique buildings, units, technologies, and a distinct economic philosophy. This choice is irreversible and defines the player's identity for the remainder of the game.

### 2.3 Religion as Strategic Layer
The twelve Sects provide deep customization through Religion Points earned at temple milestones. Each sect unlocks a unit, technology, building, passive ability, and active spell. Sect choices create synergies, counter-strategies, and a compelling dilemma: adopt early for power or delay for surprise.

### 2.4 Economic Asymmetry
Each culture has a fundamentally different economy: Runai profits through trade routes and banking; Alanthor generates income from walled compartments and taxation; Feraldis pillages enemies and batch-trains armies. The Crystal Curse funds itself through cursed ground area.

---

## 3. World & Map Generation

### 3.1 Terrain Generation
Maps are procedurally generated using layered Perlin noise with domain warping for organic coastlines.

| Parameter | Value |
|-----------|-------|
| Map Size | 512 × 512 world units |
| Max Height | 100 world units |
| Heightmap Resolution | 1025 px |
| Water Height | 20 world units |
| Seed | Configurable (default: 12345) |

**Terrain Layers:**
- **Continental Shape:** 5-octave FBM noise (scale 0.008, persistence 0.5). Values below 0.35 become ocean; 0.35–0.42 become beach transition.
- **Domain Warping:** 60-unit distortion at 0.005 scale creates organic, non-circular coastlines.
- **Hills:** Rolling terrain (scale 0.025, amplitude 15 units) provides tactical elevation.
- **Mountains:** Ridge formations (scale 0.012, amplitude 35 units) create natural choke points. Only placed where base land value exceeds 0.55.
- **Edge Falloff:** 30% of map half is ocean border, preventing edge-camping.

### 3.2 Texture Layers

| Layer | Application |
|-------|-------------|
| Sand | Beach transitions near water |
| Grass | Primary land surface |
| Dirt | Slopes and mixed terrain |
| Rock | Mountain faces and cliffs |
| Snow | High-elevation peaks |
| Curse | Crystal-corrupted ground (painted dynamically) |

Curse ground uses multi-octave Perlin noise for organic, irregular edges. Adjacent cursed patches share the same world-space noise field, so they merge seamlessly into continuous corrupted regions rather than appearing as discrete circles.

### 3.3 Spawn Zones
- **8 symmetric spawn positions** arranged around the map center
- **Flatten Radius:** 40 units of flat terrain per spawn for base placement
- **Blend Radius:** 20-unit smooth transition from flat to natural terrain
- **Spawn Distance:** 50% of map half-width from center
- **Target Height:** 30 world units (above water level)

### 3.4 Resources
- **Iron Deposits:** Scattered across the map as minable nodes with finite quantities
- **Crystal Cadavers:** Dropped by destroyed Crystal entities (50% of build cost)
- **Cursed Ground:** Passively damages non-Crystal units standing on it

---

## 4. Factions & Player Setup

### 4.1 Faction Slots
The game supports up to 8 player factions, each identified by a color:

| Slot | Faction | Color (RGB) |
|------|---------|-------------|
| 0 | Blue | (0.20, 0.55, 1.00) |
| 1 | Red | (1.00, 0.20, 0.25) |
| 2 | Green | (0.20, 0.90, 0.35) |
| 3 | Yellow | (1.00, 0.85, 0.20) |
| 4 | Purple | (0.80, 0.40, 1.00) |
| 5 | Orange | (1.00, 0.55, 0.15) |
| 6 | Teal | (0.20, 1.00, 0.95) |
| 7 | White | (1.00, 1.00, 1.00) — Crystal Curse |

Slot 7 (White) is always occupied by the Crystal Curse AI faction.

### 4.2 Starting Conditions
Each human player begins with:
- 1 Hall (main base, 20 population capacity)
- 3 Builders
- 2 Miners
- Starting resources (Supplies, Iron, Crystal)

Player color does **not** change when a culture is selected — culture is tracked separately.

---

## 5. Resources & Economy

### 5.1 Resource Types

| Resource | Icon | Primary Sources | Usage |
|----------|------|-----------------|-------|
| **Supplies** | — | Halls (passive), Gatherer's Huts (passive), trade | Buildings, units, technologies |
| **Iron** | — | Iron deposits (mined) | Military units, advanced buildings |
| **Crystal** | — | Crystal cadavers (mined), passive node income | Temples, magical units, Veilsteel |
| **Veilsteel** | — | Crafted: 5 Iron + 3 Crystal → 1 Veilsteel at Smelter | Elite units, advanced technologies |
| **Glow** | — | Defeating colossal Curse entities, Temple grants | Mythical abilities (endgame) |

### 5.2 Passive Income
- **Hall:** 50 Supplies per 15 seconds
- **Gatherer's Hut:** 15 Supplies per 10 seconds (radius-based area income)
- **Alanthor Compartments:** Income proportional to walled-off area
- **Vault Banking:** 3% interest per minute on stored resources

### 5.3 Mining Mechanics

| Parameter | Value |
|-----------|-------|
| Gather Interval | 2 seconds per unit |
| Max Carry | 10 iron per trip |
| Gather Range | 5 units from deposit |
| Dropoff Range | 6 units from Hall/Gatherer's Hut |
| Search Radius | 50 units (AI miners only) |

**Mining Behavior:**
- **Local player miners** require an explicit Gather command (right-click on deposit) to begin mining. They do not auto-find deposits.
- **AI miners** automatically find the nearest deposit within their search radius.
- **All miners** auto-find new deposits only upon depletion and only within their Line of Sight range.
- Miners follow a state machine: Idle → Moving to Deposit → Gathering → Returning to Base → Depositing → repeat.
- A `UserMoveOrder` interrupts mining (miner keeps its current load but goes idle).

### 5.4 Smelting (Veilsteel Production)
- Smelters store up to 100 Iron and 50 Crystal
- Conversion: 5 Iron + 3 Crystal → 1 Veilsteel every 5 seconds
- Miners can be assigned to fetch resources to the Smelter

---

## 6. Era Progression

The game progresses through five eras. Each era unlocks new buildings, units, and technologies.

| Era | Name | Theme | Requirements |
|-----|------|-------|-------------|
| 1 | Dawn of Ashes | Unified humanity, survival | — (starting era) |
| 2 | Age of Divergence | Choose culture | Research "Advance to Era II" at Hall (400 Supplies + 60 Crystal) |
| 3 | Age of Doctrine | Advanced culture | Temple Level 2 |
| 4 | Age of Dominion | Peak culture power | Temple Level 3 |
| 5 | Age of Reckoning | Endgame | Temple Level 4 |

**Age-Up Cost (Era 1 → 2):** 800 Supplies, 200 Iron, 150 Crystal

---

## 7. Buildings

### 7.1 Era 1 — Universal Buildings

| Building | HP | Cost | Pop | Role |
|----------|----|------|-----|------|
| **Hall** | 2,400 | Starting | +20 | Main base. Trains Builders. Generates Supplies. Research hub. Ranged defense (12 dmg, 20 range). |
| **Hut** | 600 | 50 Supplies | +10 | Housing. Miner dropoff point. |
| **Gatherer's Hut** | 800 | 120 Supplies | — | Passive Supplies income. Miner dropoff point. Build time: 25s. |
| **Barracks** | 600 | 150 Supplies + 70 Iron | — | Trains Swordsmen and Archers. Research hub. |
| **Temple of Ridan** | 800 | 300 Supplies + 100 Crystal | — | Trains Litharches (healers). Grants Sect Points at each level. |
| **Warrior's Hall** | 1,400 | 150 Supplies + 160 Iron + 50 Crystal | — | Aggression techs. +10% training speed aura. |
| **Vault of Almierra** | 1,200 | 300 Supplies + 100 Crystal | — | Banking: 3% interest per minute. |

### 7.2 Era 1 — Technologies

| Technology | Effect | Cost |
|------------|--------|------|
| Advance to Era II | Unlock culture selection | 400 Supplies + 60 Crystal |
| Improved Tools | +15% gather speed | 80 Supplies + 40 Iron |
| Storage Carts | +10 carry capacity | 90 Supplies |
| Basic Drills | +10% melee attack speed | 100 Supplies + 40 Iron |
| Wooden Armor | +1 melee defense | 80 Supplies |

### 7.3 Culture-Specific Buildings (Era 2+)

See [Section 12: Cultures](#12-cultures) for complete culture building lists.

---

## 8. Units

### 8.1 Era 1 — Universal Units

| Unit | HP | Speed | Damage | Range | Cost | Pop | Role |
|------|----|----|--------|-------|------|-----|------|
| **Builder** | 60 | 4.0 | 2 | Melee | 50 Supplies | 1 | Construction. Auto-chains to nearby unfinished buildings within LOS. |
| **Miner** | 50 | 3.5 | 2 | Melee | 50 Supplies | 1 | Resource gathering from iron deposits and crystal cadavers. |
| **Scout** | 40 | 6.0 | 3 | Melee | 55 Supplies | 1 | Reconnaissance. LOS 20 (highest of any Era 1 unit). |
| **Swordsman** | 120 | 3.5 | 12 | Melee | 140 Supplies | 1 | Frontline infantry. +1 melee defense. 1.2s attack cooldown. |
| **Archer** | 60 | 4.0 | 8 | 10–25 | 75 Supplies | 1 | Ranged infantry. 1.5s cooldown. Retreats when enemies close. |
| **Litharch** | 60 | 3.5 | 5 | 10 | 100 Supplies + 25 Iron + 10 Crystal | 1 | Healer. 8 HP/s heal rate, 4u heal range. |

### 8.2 Culture-Specific Units (Era 2+)

See [Section 12: Cultures](#12-cultures) for complete culture unit lists.

### 8.3 Crystal Curse Units

| Unit | HP | Speed | Damage | Range | Crystal Cost | Role |
|------|----|-------|--------|-------|-------------|------|
| **Crystalling** | 60 | 5.5 | 8 | Melee | 35 | Fast swarm melee. 0.8s cooldown. |
| **Veilstinger** | 40 | 4.0 | 18 | 8–24 | 80 | Dual-laser glass cannon. Fires 2 targets simultaneously. |
| **Godsplinter** | 1,200 | 1.8 | 40 | 4 (siege) / 22 (laser) | 350 | Massive siege creature. 4-target laser barrage + 2x building damage melee. |

---

## 9. Combat System

### 9.1 Damage Types & Armor

**Damage Types:** Melee, Ranged, Siege, Magic, True

**Armor Types:** Infantry Light, Infantry Heavy, Ranged, Cavalry, Structure, Structure (Human)

**Damage Modifier Matrix:**

|  | Light Inf | Heavy Inf | Ranged | Cavalry | Structure | Struct (Human) |
|---|-----------|-----------|--------|---------|-----------|----------------|
| **Melee** | 1.0× | 1.0× | 1.1× | 0.9× | 0.2× | 0.2× |
| **Ranged** | 1.1× | 0.9× | 1.0× | 0.8× | 0.15× | 0.15× |
| **Siege** | 0.6× | 0.8× | 0.8× | 0.7× | 3.0× | 2.4× |
| **Magic** | 1.1× | 0.9× | 1.1× | 1.0× | 0.5× | 0.45× |
| **True** | 1.0× | 1.0× | 1.0× | 1.0× | 1.0× | 1.0× |

**Defense Formula:**
```
finalDamage = baseDamage × modifier × (1 - defense / (defense + 100))
```

### 9.2 Melee Combat
- **Range:** 1.5 units
- **Default Cooldown:** 1.5 seconds
- **Height Bonus:** ±4% per unit of height difference, capped at ±20%
- **Behavior:** Units chase targets unless on Hold Position. Minimum guaranteed damage: 1.

### 9.3 Ranged Combat (Archers)
- **Default Range:** 10–25 units (minimum–maximum)
- **Dynamic Aim Time:** 0.3s (close) to 1.2s (far)
- **Arrow Speed:** 30 units/second
- **Arrow Trajectory:** Quadratic Bezier arc (0.8s flight, 3u arc height)
- **Retreat:** Archers retreat when enemies enter minimum range.

### 9.4 Projectile System

**Arrow Projectiles:**
- Quadratic Bezier curve trajectory with guaranteed hits
- Flight duration: 0.8 seconds (fixed)
- Arc height: 3 units, scaled by horizontal distance (max at 15 units)
- Hit radius: 0.8 units

**Laser Projectiles:**
- Straight-line flight at constant velocity
- Terrain collision: lasers are blocked by hills and cliffs (checked against terrain heightmap with 0.5u margin)
- Speed: 55–60 units/second (varies by source)
- Despawn after 1.5× expected flight time

### 9.5 Building Combat
Buildings with ranged attacks auto-target enemies within range. Crystal buildings fire laser projectiles (straight-line, terrain-blocked); human buildings fire arrow projectiles (arced trajectory).

| Building | Range | Damage | Cooldown | Max Targets |
|----------|-------|--------|----------|-------------|
| Hall | 20 | 12 | 2.5s | 1 |
| Fiendstone Keep | 25 | 20 | — | 3 |
| Crystal Turret Node | 25 | 15 | 1.5s | 2 |

### 9.6 Targeting Rules
- Units auto-acquire enemies within their Line of Sight when idle
- **Guard Point:** Units maintain a guard point (max distance 20u) and return when disengaged
- **Builders and Miners** never auto-target (passive workers)
- **UserMoveOrder** blocks auto-targeting (unit obeys player command)
- **Attack-Move:** Units advance toward destination while scanning for enemies
- **Patrol:** Units follow waypoints, engaging enemies along the route
- **Hold Position:** Units attack in range but never chase or move

### 9.7 Crystal Combat Mechanics

**Veilstinger (Dual-Target Ranged):**
- Fires simultaneous lasers from left and right gun positions
- Primary target from TargetingSystem; secondary target is nearest other enemy
- Retreats if enemies enter minimum range (8u)
- Gun positions computed relative to facing direction

**Godsplinter (Hybrid Siege/Ranged):**
- **Siege Mode** (≤4u range): Direct melee damage with 2× multiplier vs buildings
- **Laser Barrage** (≤22u range): Multi-target laser attack hitting up to 4 enemies simultaneously
- Priority: siege if in range and ready, else laser barrage, else chase

---

## 10. Religion & Sects

### 10.1 Overview
The religion system provides deep strategic customization through twelve Sects, each tied thematically to one of the three cultures. Players earn Religion Points (RP) at temple milestones and spend them to adopt sects, unlocking unique units, technologies, buildings, passive buffs, and active spells.

### 10.2 Religion Point Income

| Milestone | RP Earned | Cumulative |
|-----------|----------|-----------|
| Build Temple (Era 2) | 2 RP | 2 RP |
| Build Shrine (Era 1) bonus | +1 RP | 3 RP |
| Temple Level 2 (Era 3) | 3 RP | 5–6 RP |
| Temple Level 3 (Era 4) | 3 RP | 8–9 RP |

### 10.3 Sect Adoption Costs

| Affinity | Cost | Notes |
|----------|------|-------|
| **Affinity sect** (4 per culture) | 1 RP | Thematically aligned to your culture |
| **Non-affinity sect** | 3 RP | From another culture's aligned sects |

### 10.4 Temple Scaling

Temple levels amplify all previously adopted sects:

| Temple Level | Power Multiplier | Passive Buff | Active Spell |
|-------------|-----------------|-------------|-------------|
| 1 (Era 2) | 1.0× | Base values | Base cooldown |
| 2 (Era 3) | 1.5× | +50% values | −15% cooldown |
| 3 (Era 4) | 2.0× | +100% values | −30% cooldown, +25% effect |
| 4 (Era 5) | 2.5× | +150% values | −40% cooldown, +50% effect |

**Strategic Dilemma:** Adopting sects early gives more time with scaling bonuses, but reveals your strategy. Waiting preserves surprise but reduces total scaling benefit.

### 10.5 The Twelve Sects

#### Alanthor-Affinity Sects

**Renewal** — Defensive restoration and self-repair.
- Passive: +20% income if all walls at full health
- Unit: Scar Guard (170 HP, heavy melee, self-heal + out-of-combat heal aura)
- Tech: Dietary Mandate (all units gain out-of-combat regen)
- Building: Chapel of Renewal
- Active: Repair Levies (CD 40s) — Rapidly repair buildings in area

**Antiquity** — Knowledge and research acceleration.
- Passive: +20% building tech research speed
- Unit: Golem Autark (320 HP, magic construct, slow, 10u range magic attack)
- Tech: Clockwork Archives (−15% research time, −5% cooldowns)
- Building: Chapel of Antiquity
- Active: Crystal Survey (CD 50s) — Reveal Crystal nodes, +30 Crystal on first reveal

**Living Stone** — Fortification and construction mastery.
- Passive: +20% wall income, +10% construction speed
- Unit: Stone Warden (200 HP, heavy melee tank, very high defense)
- Tech: Terrace Planning (+20% Supplies from compartment size)
- Building: Chapel of Living Stone
- Active: Bulwark Rise (CD 60s) — +3 armor to buildings in compartment for 20s

**Veiled Memory** — Vision control and arcane subtlety.
- Passive: +15% fog vision range, −10% spell cooldowns
- Unit: Archivist Adept (110 HP, magic support, 14u range, Dispel ability)
- Tech: Hidden Records (−25% retaliation on first Crystal mine)
- Building: Chapel of Veiled Memory
- Active: Shroud (CD 55s) — 12u mist zone, blocks vision, slows 20%

#### Runai-Affinity Sects

**Still Flame** — Trade enhancement and route protection.
- Passive: +15% trade income, +25% tariff bonus
- Unit: Flame Warden (150 HP, melee, roots enemy caravans/raiders 2s)
- Tech: Sanctified Routes (routes grant +5 armor to nearby caravans)
- Building: Chapel of Still Flame
- Active: Embargo (CD 60s) — Disable enemy trade 20s, siphon 30 Supplies

**Quiet Vault** — Banking and resource preservation.
- Passive: +30% banking interest rate
- Unit: Vault Keeper (140 HP, defensive melee, damage reduction aura)
- Tech: Hidden Ledgers (retain 50% Crystal/Iron on depot destroyed)
- Building: Chapel of Quiet Vault
- Active: Lockdown Vault (CD 70s) — Storage unraidable 15s, slows nearby enemies

**Mirror Rite** — Magical precision and spell reflection.
- Passive: +10% ranged accuracy, −5% spell cooldowns
- Unit: Glassmark Arcanist (100 HP, magic ranged, 15u range)
- Tech: Refined Silver Inlays (+10% magic attack, −10% cooldown)
- Building: Chapel of Mirror Rite
- Active: Reflective Ward (CD 60s) — Reflect 25% spell damage + 10% physical for 10s

**Shard Judgment** — Economic warfare and legal enforcement.
- Passive: +10% law enforcement, enemy buildings near trade build 20% slower
- Unit: Judicator (160 HP, heavy melee, high melee defense)
- Tech: Iron Decrees (enemy buildings near trade routes build 20% slower)
- Building: Chapel of Shard Judgment
- Active: Edict of Seizure (CD 65s) — Drain 50 Supplies from enemy over 10s

#### Feraldis-Affinity Sects

**Ember Ash** — Raw aggression and military acceleration.
- Passive: +12% melee damage, +10% training speed
- Unit: Ashblade (155 HP, aggressive melee, fast)
- Tech: War Tithe (enemy civilian kills refund 5 Supplies)
- Building: Chapel of Ember Ash
- Active: Battle Fervor (CD 55s) — +25% attack/speed in 10u radius for 10s

**Hollow Brand** — Fear and morale disruption.
- Passive: 5% chance to cause panic on hit
- Unit: Brandbreaker (150 HP, anti-structure melee, +4 siege damage)
- Tech: Desecrate Standards (enemy morale auras −20% effectiveness)
- Building: Chapel of Hollow Brand
- Active: Profane Rally (CD 60s) — Pull nearby enemies 2u, slow 30% for 5s

**Flamewrought Chains** — Curse manipulation and control.
- Passive: 3% chance to briefly control enemy unit on hit
- Unit: Chaincaster (105 HP, magic support, 14u range, ChainBind root)
- Tech: Veilsteel Links (Veilsteel grants +1% damage reduction per ingot)
- Building: Chapel of Flamewrought Chains
- Active: Bind the Core (CD 90s) — Pacify Crystal sub-node for 15s

**Unmaker's Grasp** — Anti-Curse specialization.
- Passive: +20% bonus damage vs Crystal entities
- Unit: Nullblade (150 HP, anti-magic melee, +6 magic damage, +2 magic defense)
- Tech: Erasure Rites (Crystal drops yield +20% more resources)
- Building: Chapel of Unmaker's Grasp
- Active: Unravel (CD 80s) — Heavy true damage to Crystal unit, minor splash to sub-nodes

### 10.6 Sect Synergy Pairs

Adopting both sects in a synergy pair grants a bonus effect greater than the sum of their parts.

| Pair Name | Sects | Combined Theme |
|-----------|-------|----------------|
| **The Fortress** | Renewal + Living Stone | Self-repair + fortification → near-indestructible walls |
| **The Archive** | Antiquity + Veiled Memory | Research + cooldown reduction → tech supremacy + map control |
| **The Merchant** | Still Flame + Quiet Vault | Trade income + banking → compound economic growth |
| **The Inquisitor** | Mirror Rite + Shard Judgment | Magic precision + law enforcement → economic warfare |
| **The Warband** | Ember Ash + Hollow Brand | Raw damage + panic → devastating assault force |
| **The Purifier** | Flamewrought Chains + Unmaker's Grasp | Curse control + destruction → Crystal counter-specialist |

---

## 11. The Crystal Curse Faction

### 11.1 Overview
The Crystal Curse (Faction.White) is a fully autonomous AI-controlled faction that spreads crystalline corruption across the map. It operates without population limits and funds itself entirely through cursed ground area. It is hostile to all human players.

### 11.2 Crystal Economy
- **Income Source:** Cursed ground area. Formula: `0.1 × cursedArea` crystal per second
- **Resource Nodes** add a flat income bonus
- **No population limits** — the Crystal faction spawns units directly from its crystal bank
- **Starting Resources:** 100 Crystal per main node

### 11.3 Crystal Buildings

| Building | HP | Crystal Cost | Role |
|----------|----|-------------|------|
| **Main Node** | 1,500 | 2,000 (expansion) | Central hive. Spreads curse (15u radius). Controls AI brain. |
| **Resource Node** | 200 | 50 | Spreads curse (8u radius). Bonus income. |
| **Turret Node** | 500 | 100 | Auto-fires lasers (25 range, 15 dmg, 1.5s CD, 2 targets). |
| **Restoration Node** | 400 | 120 | Heals crystal entities (5 HP/s, 15u radius). |
| **Enforcement Node** | 600 | 200 | Buffs crystal allies (+15% def, +15% att, +10% speed, 20u radius). |
| **Suppression Node** | 600 | 200 | Debuffs enemies (−15% def, −15% att, −10% speed, 20u radius). |

### 11.4 Cursed Ground
- **Spread Mechanic:** Main nodes spread cursed ground in concentric rings every 30 seconds. Resource sub-nodes spread at smaller radius (8u).
- **Visual:** Multi-octave Perlin noise creates organic, irregular edges. Adjacent patches merge seamlessly.
- **Damage:** Non-crystal units standing on cursed ground take damage over time.

### 11.5 Crystal AI Brain
The Crystal AI operates as a standalone decision system (not using the player AI architecture). It runs on a 5-second decision interval per main node.

**AI Phases:**

| Phase | Condition | Behavior |
|-------|-----------|----------|
| Early | Spread < 10 or bank < 100 | Crystallings only, Resource Nodes priority |
| Mid | Spread < 20 or bank < 500 | Mix in Veilstingers, add Turrets and Restoration |
| Late | Otherwise | Godsplinters, all sub-node types, expansion |

**Decision Loop:**
1. **Build Phase** (25–35s cooldown): Place sub-nodes within cursed area. Priority: Resource → Turret → Restoration → Enforcement/Suppression.
2. **Spawn Phase** (10–15s cooldown): Spawn 1–3 units at main node. Distribution: 65% Crystalling, 25% Veilstinger, 10% Godsplinter (late game).
3. **Harassment Phase** (120–180s cooldown): Send 30–60% of nearby units toward nearest player Hall.
4. **Expansion Phase** (bank ≥ 2,000): Place new Main Node 40–80u away, creating an independent AI cell.
5. **Defense:** Turret Nodes and idle units auto-engage via TargetingSystem.

### 11.6 Death Drops
When any Crystal entity (unit or building) is destroyed, it drops a loot pile (cadaver) containing 50% of its build cost in crystal. These cadavers are mineable by player miners using the same mechanics as iron mining.

---

## 12. Cultures

### 12.1 Runai — The Veil Scholars
**Philosophy:** Preserve the Curse, learn from it, pacify it.
**Playstyle:** Trade, guerrilla warfare, technological superiority.
**Colors:** Cyan (0.25, 0.75, 0.80) + Sandstone (0.76, 0.65, 0.45)

**Main HQ:** Thessara's Bazaar (2,700 HP, dual training queues, 40 pop, can pack/move)

**Buildings:**

| Building | HP | Cost | Role |
|----------|----|------|------|
| Runai Outpost | 900 | 140 Supplies + 20 Iron | Trade node, vision |
| Runai Trade Hub | 1,200 | 240 Supplies + 40 Iron | Spawns caravans (1 per 22s, max 3 per route) |
| Runai Vault | 1,100 | 1,500 Supplies + 250 Iron + 200 Crystal | Banking (3% interest) |
| Runai Veilsteel Foundry | 1,500 | 450 Supplies + 120 Iron + 100 Crystal | Crafts Veilsteel |
| Runai Siege Workshop | 1,100 | 320 Supplies + 140 Iron + 60 Crystal | Trains Sand Ballista |

**Units:**

| Unit | HP | Speed | Damage | Cost | Special |
|------|----|----|--------|------|---------|
| Runai Spearman | 130 | 5.6 | 12 melee | 110S + 30I + 25C | Anti-cavalry |
| Runai Skirmisher | 95 | 6.0 | 15 ranged (11u) | 95S + 50I + 25C | Hit-and-run |
| Runai Raider | 150 | 7.2 | 18 melee | 220S + 100I + 50C | Cavalry, fast |
| Runai Sand Ballista | 200 | 3.4 | 36 siege (20u) | 260S + 120I + 80C | Siege unit |
| Runai Caravan | 120 | 5.6 | — | Auto-spawned | Uncontrollable, flees combat |
| Runai Escort | 110 | 6.2 | 10 melee | Auto-spawned | Guards caravans |

**Technologies:**
- Long-Haul Tariffs: +15% Supplies from routes, +25% if route > 60u
- Pack Bazaar: 40% faster pack/unpack, +200 HP while packed
- Escorted Caravans: 2 escorts per caravan, +2 pop per active caravan

### 12.2 Alanthor — The Iron Covenant
**Philosophy:** Exploit the Curse for profit.
**Playstyle:** Economy, defense, walled compartments.
**Colors:** Sage Green (0.55, 0.65, 0.50) + Warm Grey (0.45, 0.45, 0.42)

**Main HQ:** King's Court (2,100 HP, generates Supplies, +10% building HP aura, +15% repair rate aura, 10 pop)

**Buildings:**

| Building | HP | Cost | Role |
|----------|----|------|------|
| Alanthor Wall Hub | 600 | 40 Supplies + 20 Iron | Tower connection point |
| Alanthor Wall Segment | 400 | — (auto-created) | Links hubs |
| Alanthor Watch Tower | 950 | 140 Supplies + 70 Iron | Defensive, garrison 4 |
| Alanthor Garrison | 1,500 | 220 Supplies + 90 Iron | Trains Sentinel + Crossbowman, garrison 6, 8 pop |
| Royal Stable | 1,300 | 260 Supplies + 120 Iron + 40 Crystal | Trains Cataphract |
| Alanthor Siege Yard | — | — | Trains Ballista |

**Units:**

| Unit | HP | Speed | Damage | Cost | Special |
|------|----|----|--------|------|---------|
| Alanthor Sentinel | 160 | — | Melee | 180S + — | Heavy defense, high armor |
| Alanthor Crossbowman | 100 | — | Ranged | — | Slow but armored |
| Alanthor Cataphract | 180 | — | Melee (cavalry) | — | High damage cavalry |
| Alanthor Ballista | 220 | — | Siege | — | Longest range siege |

**Technologies:**
- Stone Ledgers: Compartments generate +8 Supplies per 10 sq. units area per minute
- Mason's Guild: +15% building HP, +20% repair rate

**Compartment Economy:** Walls placed in enclosed shapes generate passive income proportional to the enclosed area. This is Alanthor's primary economic engine.

### 12.3 Feraldis — The Ashborn
**Philosophy:** Destroy the Curse utterly.
**Playstyle:** Aggression, batch training, magic denial.
**Colors:** Crimson (0.70, 0.18, 0.15) + Dark Grey (0.28, 0.26, 0.24)

**Main HQ:** Fiendstone Keep (2,000 HP, +25% training speed aura, 20 pop, ranged attack: 25 range, 20 dmg, 3 targets)

**Buildings:**

| Building | HP | Cost | Role |
|----------|----|------|------|
| Hunting Lodge | 1,000 | 160 Supplies + 20 Iron | Upgraded Hut, bonus near wildlife |
| Logging Station | 1,000 | 160 Supplies + 20 Iron | Upgraded Hut, bonus near trees |
| Fiend Foundry | 1,300 | 200 Supplies + 80 Iron + 30 Crystal | Veilsteel forging |
| Totem Tower | 900 | 120 Supplies + 60 Iron | Defensive tower, +25% attack on bloody ground |
| Longhouse | 1,400 | 260 Supplies + 100 Iron | Batch-trains 5–10 units, 5% cost + 10% time discount |
| Siege Yard | 1,200 | 260 Supplies + 120 Iron + 40 Crystal | Trains Siege Ram |

**Units:**

| Unit | HP | Speed | Damage | Cost | Special |
|------|----|----|--------|------|---------|
| Feraldis Berserker | 150 | 5.8 | 14 melee | 110S + 20I + 20C | +2 melee defense. Unhealable. |
| Feraldis Hunter | 100 | 5.7 | 11 ranged (12u) | 90S + 10I + 20C | Hit-and-run |
| Feraldis Warboar Rider | 160 | 7.0 | 16 melee | 210S + 80I + 40C | Cavalry |
| Feraldis Siege Ram | 300 | 3.0 | 34 siege | 280S + 140I + 70C | Anti-structure |

**Technologies:**
- Pillage: Killing non-military units grants +15 Supplies + 1 Iron per kill
- Iron Fury: Units carry 5 Iron, each grants +2% attack

**Berserker Conversion:** Miners sent to the Fiendstone Keep are converted into Berserkers — high-damage melee units that cannot be healed. This is a one-way, permanent conversion.

---

## 13. AI System

### 13.1 Player AI (AIBrain)
Human-faction AI players use a modular architecture with specialized managers:

- **AIBrain:** Central coordinator that prioritizes between economy, military, and expansion
- **AIEconomyManager:** Manages resource gathering, building placement, and tech research
- **AITacticalManager:** Controls army composition, attack timing, and target selection

AI players follow the same rules as human players — they must research technologies, build structures, train units, and manage resources. They cannot cheat with hidden information or free resources.

### 13.2 Crystal AI
See [Section 11.5: Crystal AI Brain](#115-crystal-ai-brain).

---

## 14. Controls & Camera

### 14.1 Camera Controls

| Control | Action |
|---------|--------|
| **WASD** | Move camera |
| **Q / E** | Rotate camera left / right |
| **R / F** | Tilt camera up / down |
| **Scroll Wheel** | Zoom in / out |
| **Middle Mouse Drag** | Pan camera |
| **Screen Edges** | Edge scrolling (15px border, 30 u/s) |
| **Minimap Click** | Jump to location |

**Camera Parameters:**

| Parameter | Value |
|-----------|-------|
| Keyboard Speed | 25 u/s |
| Edge Scroll Speed | 30 u/s |
| Zoom Range | 15–80 world units |
| Tilt Range | 30°–75° |
| Rotation Speed | 100°/s |
| Terrain Following | Enabled (2u offset above terrain) |

### 14.2 Unit Commands

| Input | Action |
|-------|--------|
| **Left Click** | Select unit / building |
| **Shift + Left Click** | Add to selection |
| **Drag Box** | Area select |
| **Right Click (ground)** | Move / formation move |
| **Right Click (enemy)** | Attack |
| **Right Click (ally building, under construction)** | Repair |
| **Right Click (resource deposit)** | Gather |
| **Right Click (Hall/Gatherer's Hut, miners selected)** | Force return and deposit |
| **A + Click** | Attack-move |
| **P + Click** | Patrol |
| **S** | Stop |
| **H** | Hold position |
| **Ctrl + 1–9** | Save control group |
| **1–9** | Recall control group (double-tap: center camera) |
| **Shift + 1–9** | Add to control group |
| **Shift + Click (building placement)** | Repeat placement mode |
| **ESC** | Clear selection / exit modes |

### 14.3 Formation Movement
When multiple units are selected, they move in a grid formation:
- Columns = √(unit count)
- Spacing: 2.0 units between slots
- Arranged relative to camera orientation
- **Speed Synchronization:** All units move at the slowest unit's effective speed to arrive simultaneously

---

## 15. User Interface

### 15.1 HUD Layout
The game uses an IMGUI-based interface with the following panels:
- **Resource Bar** (top): Displays Supplies, Iron, Crystal, Veilsteel, Glow, Population
- **Minimap** (bottom-left): Tactical overview with faction colors and fog of war
- **Selection Panel** (bottom-center): Selected entity info, HP bars, stats
- **Command Panel** (bottom-right): Context-sensitive commands based on selection
- **Build Panel** (bottom-right, builders selected): Building placement options

### 15.2 Build Panel
When builders are selected, the build panel offers:
- Hut, Gatherer's Hut, Barracks, Shrine, Vault, Keep, Wall, Smelter
- Real-time placement preview with validity feedback
- Cost checking (grayed out if unaffordable)
- Wall hub snapping (2u snap distance)

### 15.3 Rally Points
- Set by right-clicking ground with only buildings selected
- Newly trained units automatically move to the rally point

---

## 16. Fog of War & Visibility

### 16.1 Visibility States

| State | Visual | Description |
|-------|--------|-------------|
| **Hidden** | Dark fog | Area never seen. No information. |
| **Revealed** | Light fog | Previously seen. Buildings show as "ghosts" (last known state). |
| **Visible** | Clear | Currently within any unit's Line of Sight. Full information. |

### 16.2 Visibility Rules
- **Player units:** Always visible to the owning player
- **Enemy units:** Only visible when inside current Line of Sight
- **Enemy buildings:** Full visibility in LoS, ghost in Revealed, hidden otherwise
- **Visibility stamped each frame** from all unit positions as circles

### 16.3 Line of Sight Ranges

| Unit Type | LoS Range |
|-----------|-----------|
| Scout | 20 units |
| Archer | 25 units |
| Builder | 12 units |
| Hall | 35 units |
| Default | 10 units |

---

## 17. Multiplayer

### 17.1 Architecture
The game uses a **lockstep** multiplayer model:
- All players execute the same simulation deterministically
- Commands are broadcast to all players and executed on the same simulation tick
- Local player and system commands execute immediately in singleplayer
- Remote player commands are queued for lockstep synchronization

### 17.2 Command Routing
All game commands flow through a single `CommandRouter` entry point with tagged sources:
- **LocalPlayer** — Input and UI commands
- **RemotePlayer** — Network-received commands
- **AI** — AI manager commands
- **System** — Automatic behaviors (auto-targeting, etc.)

---

## Appendix: Stat Tables

### A.1 Key Gameplay Constants

| System | Parameter | Value |
|--------|-----------|-------|
| **Melee** | Range | 1.5 units |
| | Attack Cooldown | 1.5 seconds |
| | Height Damage Mod | ±20% cap |
| **Ranged** | Min Range | 10 units |
| | Max Range | 25 units |
| | Arrow Speed | 30 u/s |
| | Aim Time | 0.3–1.2s |
| **Building Combat** | Arrow Speed | 25 u/s |
| | Laser Speed | 55 u/s |
| **Projectiles** | Arrow Flight | 0.8s (Bezier arc) |
| | Arc Height | 3 units |
| | Hit Radius | 0.8 units |
| | Laser Terrain Margin | 0.5 units |
| **Movement** | Default Speed | 3.5 u/s |
| | Stop Distance | 0.5 units |
| | Turn Speed | 8 rad/s |
| | Max Slope | 0.55 |
| **Mining** | Gather Interval | 2 seconds |
| | Max Carry | 10 units |
| | Gather Range | 5 units |
| | Dropoff Range | 6 units |
| **Construction** | Build Range | 4.0 units |
| | Build Rate | 1.0 progress/s |
| **Targeting** | Guard Distance | 20 units |
| | Guard Return | 2 units |
| **Crystal AI** | Decision Interval | 5 seconds |
| | Build Cooldown | 25–35 seconds |
| | Spawn Cooldown | 10–15 seconds |
| | Harass Interval | 120–180 seconds |
| | Expansion Cost | 2,000 crystal |

### A.2 Era 1 Unit Comparison

| Unit | HP | Speed | Damage | Type | LOS | Cost |
|------|----|----|--------|------|-----|------|
| Builder | 60 | 4.0 | 2 | Melee | 12 | 50S |
| Miner | 50 | 3.5 | 2 | Melee | 10 | 50S |
| Scout | 40 | 6.0 | 3 | Melee | 20 | 55S |
| Swordsman | 120 | 3.5 | 12 | Melee | 10 | 140S |
| Archer | 60 | 4.0 | 8 | Ranged | 25 | 75S |
| Litharch | 60 | 3.5 | 5 | Magic | 10 | 100S+25I+10C |

### A.3 Crystal Unit Comparison

| Unit | HP | Speed | Damage | Type | Crystal Cost |
|------|----|-------|--------|------|-------------|
| Crystalling | 60 | 5.5 | 8 | Melee | 35 |
| Veilstinger | 40 | 4.0 | 18×2 | Laser | 80 |
| Godsplinter | 1,200 | 1.8 | 40 | Siege/Laser | 350 |

---

*Document generated from source code analysis of The Waning Border 1.2 codebase.*
*Last updated: March 2026*
