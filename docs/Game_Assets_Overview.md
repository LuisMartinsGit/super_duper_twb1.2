# The Waning Border - Complete Game Assets Overview

## Table of Contents
- [Resources](#resources)
- [Factions](#factions)
- [Cultures](#cultures)
- [Era 1 Units](#era-1-units---universal)
- [Runai Units](#runai-culture-units)
- [Alanthor Units](#alanthor-culture-units)
- [Feraldis Units](#feraldis-culture-units)
- [Crystal Curse Units](#crystal-curse-units)
- [Sect Units](#sect-units)
- [Special Units](#special-units)
- [Era 1 Buildings](#era-1-buildings---universal)
- [Runai Buildings](#runai-culture-buildings)
- [Alanthor Buildings](#alanthor-culture-buildings)
- [Feraldis Buildings](#feraldis-culture-buildings)
- [Crystal Curse Buildings](#crystal-curse-buildings)
- [Religious Buildings](#religious-buildings)
- [Religion & Sects](#religion--sects)

---

## Resources

The game has **5 resource types**, each with a hard cap of 100,000 per faction.

| Resource | Weight | Primary Sources | Role |
|----------|--------|-----------------|------|
| **Supplies** | 1x | Hall (50/15s), Gatherer Hut (area-based), trade | Basic currency for construction and training |
| **Iron** | 2x | Iron deposits (mined), passive building income | Industrial resource for military and advanced structures |
| **Crystal** | 3x | Creature cadavers (mined), Crystal Shrine income | Magical resource for temples, magic units, and advanced tech |
| **Veilsteel** | 5x | Smelter (5 Iron + 3 Crystal = 1 Veilsteel / 5s) | Rare resource for elite units and advanced technologies |
| **Glow** | 4x | Ley Line Nexus, special buildings | Energy resource for special abilities and ultimate powers |

### Iron Deposits
- **Per deposit:** 500 iron
- **Spawned:** 12-20 per map (mountainous terrain, slight slopes)
- **Gather rate:** 1 iron every 2 seconds, max carry 10
- **Deposits persist** when depleted (remain on map)

### Crystal Cadavers
- **Per cadaver:** 300 crystal (default)
- **Source:** Spawned when Crystal Curse creatures die
- **Gather rate:** 1 crystal every 1.5 seconds, max carry 10
- **Cadavers are destroyed** when fully depleted

### Smelter Conversion
- **Input:** 5 Iron + 3 Crystal
- **Output:** 1 Veilsteel (every 5 seconds)
- **Smelter local storage:** 100 Iron, 50 Crystal max

---

## Factions

The game supports **8 faction slots**, color-coded for multiplayer.

| Slot | Name | RGB Color | Notes |
|------|------|-----------|-------|
| 0 | Blue | (0.20, 0.55, 1.00) | Player faction |
| 1 | Red | (1.00, 0.20, 0.25) | Player faction |
| 2 | Green | (0.20, 0.90, 0.35) | Player faction |
| 3 | Yellow | (1.00, 0.85, 0.20) | Player faction |
| 4 | Purple | (0.80, 0.40, 1.00) | Player faction |
| 5 | Orange | (1.00, 0.55, 0.15) | Player faction |
| 6 | Teal | (0.20, 1.00, 0.95) | Player faction |
| 7 | White | (1.00, 1.00, 1.00) | **Crystal Curse AI** (always) |

A 12-color pool adds Pink, Brown, Black, and Maroon for multiplayer lobby flexibility.

---

## Cultures

All players start in **Era 1** with an identical roster. Upon advancing to **Era 2** (cost: 800 Supplies + 200 Iron + 150 Crystal), each player chooses one of three cultures that permanently defines their playstyle, unit roster, buildings, and economy.

### Runai - "The Veil Scholars"

**Philosophy:** Preserve the Curse, learn from it, pacify it.
**Playstyle:** Trade / Guerrilla / Tech
**Aesthetic:** Arabic-influenced tents, sandstone, flowing robes, cyan-blue magic
**Colors:** Cyan (0.25, 0.75, 0.80) / Sandstone (0.76, 0.65, 0.45)

Nomadic traders and explorers who believe crystalline corruption contains ancient knowledge. Their economy revolves around trade routes between outposts and mobile headquarters. Caravans generate supplies automatically, with longer routes yielding higher income. Their mobile HQ (Thessara's Bazaar) can pack up and relocate, offering unmatched strategic flexibility.

### Alanthor - "The Iron Covenant"

**Philosophy:** Exploit the Curse for profit.
**Playstyle:** Economy / Defense
**Aesthetic:** Medieval European stone, thick walls, forges, arcane machinery
**Colors:** Sage Green (0.55, 0.65, 0.50) / Warm Grey (0.45, 0.45, 0.42)

Industrial forgemasters who view Crystal as another resource to extract and monetize. Their economy is built on walled compartments: enclosed areas generate passive Supplies income proportional to their size, but income pauses if walls fall. They field the heaviest armor and strongest fortifications, with units that cost more but dominate in prolonged engagements.

### Feraldis - "The Ashborn"

**Philosophy:** Destroy the Curse utterly through blood and fire.
**Playstyle:** Aggression / Magic Denial
**Aesthetic:** Celtic/Viking, dark wood, totems, blood-red war paint, fire magic
**Colors:** Crimson Red (0.70, 0.18, 0.15) / Dark Grey (0.28, 0.26, 0.24)

Fierce warband culture that believes the Curse is an abomination that must be burned from the earth. Their Gatherer Huts persist into Era 2 (unique among cultures), upgrading into Hunting Lodges and Logging Stations. They have a Pillage mechanic (+15 Supplies + 1 Iron per non-military kill) and batch-training at the Longhouse. Their Fiendstone Keep HQ is a 2000 HP fortress with ranged attack and +25% training speed aura.

### Crystal Curse (Faction White)

The Crystal Curse is an AI-controlled antagonist faction present in every game. It operates from a central Crystal Main Node, spreading cursed ground across the map. Unlike player factions, Crystal units have **no population cost** and use a crystal-based economy. The Curse fields swarm units (Crystallings), glass-cannon ranged attackers (Veilstingers), and massive siege hybrids (Godsplinters). Crystal nodes provide aura buffs/debuffs and area healing, making entrenched Curse positions extremely dangerous.

---

## Era 1 Units - Universal

All factions share these units before choosing a culture.

| Unit | HP | Speed | Damage | Range | LoS | Cost | Pop | Role |
|------|----|-------|--------|-------|-----|------|-----|------|
| **Builder** | 60 | 4.0 | 2 | 1.0 | 14 | 50 Supplies | 1 | Construction, building repair |
| **Miner** | 70 | 6.0 | 2 | 1.0 | 14 | 50 Supplies | 1 | Iron/Crystal gathering |
| **Scout** | 60 | 6.0 | 2 | 1.0 | 40 | 55 Supplies | 1 | Reconnaissance (extended LoS) |
| **Swordsman** | 120 | 5.5 | 10 | 1.0 | 16 | 140 Supplies | 1 | Heavy melee frontline |
| **Archer** | 90 | 5.2 | 17 | 10-25 | 30 | 75 Supplies | 1 | Ranged (retreats when enemies close in) |
| **Litharch** | 120 | 5.5 | 0 (6 heal/s) | 10.0 | 20 | 100S + 25I + 10C | 1 | Support healer |

---

## Runai Culture Units

| Unit | HP | Speed | Damage | Range | LoS | Pop | Role |
|------|----|-------|--------|-------|-----|-----|------|
| **Spearman** | 130 | 5.6 | 12 | 1.0 | 10 | 1 | Anti-cavalry melee (+50% vs Cavalry) |
| **Skirmisher** | 95 | 6.0 | 15 | 5-11 | 15 | 1 | Fast ranged hit-and-run |
| **Raider** | 120 | 7.2 | 10 | 3-14 | 15 | 1 | Mounted archer, fires while moving |
| **Catapult** | 160 | 3.0 | 24 | 10-18 | 22 | 2 | AOE splash damage (radius 3.0) |
| **Sand Ballista** | 200 | 3.4 | 36 | 8-20 | 22 | 2 | Long-range siege |

---

## Alanthor Culture Units

| Unit | HP | Speed | Damage | Range | LoS | Pop | Role |
|------|----|-------|--------|-------|-----|-----|------|
| **Sentinel** | 160 | 3.2 | 14 | 1.0 | 10 | 2 | Heavy tank (Melee Def +8, Ranged +4) |
| **Crossbowman** | 100 | 3.5 | 16 | 6-22 | 25 | 2 | Armored ranged (heavy infantry armor) |
| **Cataphract** | 180 | 6.5 | 18 | 1.0 | 10 | 2 | Heavy armored cavalry |
| **Ballista** | 220 | 2.8 | 50 | 10-24 | 26 | 2 | Longest range, highest single-target damage |

---

## Feraldis Culture Units

| Unit | HP | Speed | Damage | Range | LoS | Pop | Role |
|------|----|-------|--------|-------|-----|-----|------|
| **Berserker** | 80 | 5.8 | 14 | 1.0 | 10 | 1 | High DPS melee (cannot be healed) |
| **Hunter** | 90 | 5.7 | 16 | 0-8 | 12 | 1 | Close-range axe thrower (no retreat) |
| **Warboar Rider** | 200 | 5.8 | 20 | 1.0 | 10 | 1 | Fast mounted warrior |
| **Siege Ram** | 300 | 3.0 | 34 | 1.0 | 10 | 2 | Melee battering ram (Siege Def +6) |

---

## Crystal Curse Units

| Unit | Damage Type | Armor | Pop | Role |
|------|-------------|-------|-----|------|
| **Crystalling** | Siege | Infantry Light | 0 | Fast melee swarm unit |
| **Veilstinger** | Magic | Ranged | 0 | Dual-laser glass cannon (hits 2 targets) |
| **Godsplinter** | Siege | Infantry Heavy | 0 | Massive hybrid (melee + multi-target laser) |

Crystal units have no population cost and extremely high defense values (Godsplinter: Melee +10, Ranged +8, Siege +5, Magic +5).

---

## Sect Units

Each of the 12 religious sects unlocks a unique unit trained at chapels.

| Sect | Unit | HP | Speed | Damage | Range | Pop | Notes |
|------|------|----|-------|--------|-------|-----|-------|
| **Renewal** | ScarGuard | 170 | 3.2 | 16 | Melee | 1 | Tough melee infantry |
| **Antiquity** | Golem Autark | 320 | 2.0 | 22 | 0-10 | 1 | Magic damage, highest HP sect unit |
| **Living Stone** | StoneWarden | 200 | 2.8 | 10 | Melee | 1 | HP tank, damage soak |
| **Veiled Memory** | Archivist Adept | 110 | 3.5 | 14 | 0-14 | 1 | Fragile long-range caster |
| **Still Flame** | FlameWarden | 150 | 3.8 | 15 | Melee | 1 | Fast balanced melee |
| **Quiet Vault** | VaultKeeper | 140 | 3.5 | 12 | Melee | 1 | Defensive trade route guard |
| **Mirror Rite** | Glassmark Arcanist | 100 | 3.5 | 18 | 0-15 | 1 | Highest magic damage, lowest HP |
| **Shard Judgment** | Judicator | 160 | 3.4 | 16 | Melee | 1 | Balanced heavy fighter |
| **Ember Ash** | Ashblade | 155 | 5.0 | 14 | Melee | 1 | Very fast light raider |
| **Hollow Brand** | Brandbreaker | 150 | 4.0 | 12 (Siege) | Melee | 1 | Anti-structure specialist |
| **Flamewrought Chains** | Chaincaster | 105 | 3.5 | 10 | 0-14 | 1 | Control-oriented caster |
| **Unmaker's Grasp** | Nullblade | 150 | 4.2 | 14 | Melee | 1 | Anti-Crystal specialist |

---

## Special Units

| Unit | HP | Speed | Damage | Pop | Notes |
|------|----|-------|--------|-----|-------|
| **Caravan** | 120 | 5.6 | 0 | 0 | Uncontrollable, auto-trades between posts, drops cargo on death |
| **Trade Patrol** | 80 | 5.0 | 8 | 0 | 5 per trade lane, auto-patrols and engages enemies |

---

## Era 1 Buildings - Universal

| Building | HP | Cost | Pop | LoS | Role |
|----------|----|------|-----|-----|------|
| **Hall** | 2400 | Starting building | +20 | 24 | Main HQ. Trains Builder/Miner/Scout. Ranged attack (12 dmg, range 20). Generates 50 Supplies/15s |
| **Hut** | 350 | 50 Supplies | +5 | 12 | Housing |
| **Gatherer Hut** | 400 | 120 Supplies | - | 16 | Area-based Supplies income (60/min within 12-unit radius). Auto-despawns in Era 2 (except Feraldis) |
| **Barracks** | 800 | 150S + 70I | - | 18 | Trains Swordsman, Archer. Researches BasicDrills, WoodenArmor |

---

## Religious Buildings (Era 1, choose one)

| Building | HP | Cost | Role |
|----------|----|------|------|
| **Shrine of Ridan** | 800 | 300S + 100C | Temple with leveling (1-4). Trains Litharch. Grants 1 Sect Point. Enables era advancement |
| **Vault of Almierra** | 1200 | 300S + 100C | Banking with 3% interest/min on deposits. One per player |
| **Fiendstone Keep** | 2000 | 300S + 100C | Feraldis fortress HQ. +25% training speed aura. Ranged attack |

---

## Runai Culture Buildings

| Building | HP | Cost | LoS | Role |
|----------|----|------|-----|------|
| **Thessara's Bazaar** | 2700 | 600S + 200I + 100C | 26 | Mobile HQ. 2 training queues. Trains Spearman/Skirmisher/Raider. +40 pop |
| **Outpost** | 900 | 140S + 20I | 22 | Trade route anchor, extended vision |
| **Trade Hub** | 1200 | 240S + 40I | 24 | Spawns caravans (1 every 22s, max 3). +25% yield for long routes |
| **Runai Vault** | 1100 | 1500S + 250I + 200C | 20 | Banking with 3% interest/min and tariff synergy |
| **Veilsteel Foundry** | 1500 | 450S + 120I + 100C | 20 | Crafts veilsteel (20% loss factor) |
| **Siege Workshop** | 1100 | 320S + 140I + 60C | 20 | Trains Sand Ballista |

---

## Alanthor Culture Buildings

| Building | HP | Cost | LoS | Role |
|----------|----|------|-----|------|
| **King's Court** | 2100 | 360S + 80I | 26 | HQ. +10% building HP aura, +15% repair rate. +10 pop |
| **Wall Hub** | 600 | 40S + 20I | 8 | Connection point for wall segments |
| **Wall Segment** | 400 | 40S + 20I | 5 | Connects hubs; enclosed areas generate Supplies |
| **Watch Tower** | 950 | 140S + 70I | 28 | Defensive tower. 4 garrison slots with arrow fire |
| **Garrison** | 1500 | 220S + 90I | 22 | Trains Sentinel/Crossbowman. 6 garrison slots. +8 pop |
| **Royal Stable** | 1300 | 260S + 120I + 40C | 20 | Trains Cataphract |
| **Siege Yard** | 1300 | 260S + 140I + 60C | 20 | Trains Ballista |
| **Smelter** | 1000 | 220S + 100I | 14 | Converts 5 Iron + 3 Crystal into 1 Veilsteel every 5s |
| **Crucible** | 1200 | 200S + 60I + 40C | 18 | Advanced veilsteel forging (20% loss factor) |

---

## Feraldis Culture Buildings

| Building | HP | Cost | LoS | Role |
|----------|----|------|-----|------|
| **Hunting Lodge** | 1000 | 160S + 20I | 18 | Upgraded Gatherer Hut (bonus near wildlife) |
| **Logging Station** | 1000 | 160S + 20I | 18 | Upgraded Gatherer Hut (bonus near trees) |
| **Fiend Foundry** | 1300 | 200S + 80I + 30C | 18 | Veilsteel forging and weapons |
| **Totem Tower** | 900 | 120S + 60I | 26 | Defensive tower. 4 garrison slots. +25% attack on bloody ground |
| **Longhouse** | 1400 | 260S + 100I | 20 | Batch-trains Berserker/Warboar Rider (5 or 10 at a time, -5% cost, -10% time). +10 pop |
| **Siege Yard** | 1200 | 260S + 120I + 40C | 20 | Trains Siege Ram |

---

## Crystal Curse Buildings

| Building | HP | Radius | Role |
|----------|----|--------|------|
| **Crystal Main Node** | 5000 | 1.5 | Central hive. Spreads cursed ground. Ranged attack (30 dmg, range 20, 3 targets). Def: +15M/+15R/+10S/+10Mag |
| **Crystal Resource Node** | 1500 | 1.0 | Sub-node that spreads cursed ground and generates crystal income |
| **Crystal Enforcement Node** | 1200 | 0.8 | Aura: +3 Def, +2 Att, +15% Speed to nearby crystal entities (12-unit radius) |
| **Crystal Suppression Node** | 1200 | 0.8 | Aura: -2 Def, -2 Att, -20% Speed to nearby enemies (12-unit radius) |
| **Crystal Restoration Node** | 1200 | 0.8 | Aura: Heals nearby crystal entities at 5 HP/s (12-unit radius) |
| **Crystal Turret Node** | 1500 | 1.0 | Ranged turret: 25 magic damage, range 18, 3 max targets, 2.0s cooldown |

Cursed ground deals damage over time to non-crystal units standing on it.

---

## Religious Buildings

| Building | Role |
|----------|------|
| **Small Chapel** | Sect-specific training and research (early) |
| **Large Chapel** | Sect-specific training and research (advanced) |
| **Sect Unique Building** | Sect-specific specialized building with unique effects |

---

## Religion & Sects

### Era Progression

| Era | Name | Requirement |
|-----|------|-------------|
| Era 1 | Dawn of Ashes | Starting era, all players identical |
| Era 2 | Age of Divergence | 800 Supplies + 200 Iron + 150 Crystal (choose culture) |
| Era 3-5 | Temple Levels 2-4 | Advancing through Temple upgrades |

### Religion Points (RP)

| Source | RP Gained |
|--------|-----------|
| Culture adoption (Era 2) | 2 RP |
| Temple Level 2 (Era 3) | 3 RP |
| Temple Level 3 (Era 4) | 3 RP |
| Temple Level 4 (Era 5) | 3 RP |
| Shrine bonus (if built Era 1) | +1 RP |
| **Maximum** | **8-9 RP** |

**Sect adoption cost:** 1 RP (culture affinity) or 3 RP (foreign sect).

### The 12 Sects

#### Alanthor-Affinity Sects
| Sect | Unique Unit | Bonuses |
|------|-------------|---------|
| **Renewal** | ScarGuard | Healing, wall integrity, self-repair |
| **Antiquity** | Golem Autark | Research speed, arcane golems |
| **Living Stone** | StoneWarden | Wall income, building speed, fortification |
| **Veiled Memory** | Archivist Adept | Vision, spell cooldown, fog manipulation |

**Synergy pairs:** Renewal + Living Stone = "The Fortress" | Antiquity + Veiled Memory = "The Archive"

#### Runai-Affinity Sects
| Sect | Unique Unit | Bonuses |
|------|-------------|---------|
| **Still Flame** | FlameWarden | Trade income, route protection |
| **Quiet Vault** | VaultKeeper | Banking interest, economic defense |
| **Mirror Rite** | Glassmark Arcanist | Magic accuracy, spell reflection |
| **Shard Judgment** | Judicator | Law enforcement, trade area denial |

**Synergy pairs:** Still Flame + Quiet Vault = "The Merchant" | Mirror Rite + Shard Judgment = "The Inquisitor"

#### Feraldis-Affinity Sects
| Sect | Unique Unit | Bonuses |
|------|-------------|---------|
| **Ember Ash** | Ashblade | +12% melee damage, +10% training speed |
| **Hollow Brand** | Brandbreaker | 5% panic on hit, morale disruption |
| **Flamewrought Chains** | Chaincaster | 3% control chance, Curse binding |
| **Unmaker's Grasp** | Nullblade | +20% vs Crystal entities, Crystal yield bonus |

**Synergy pairs:** Ember Ash + Hollow Brand = "The Warband" | Flamewrought Chains + Unmaker's Grasp = "The Purifier"

### Temple Power Scaling

All sect bonuses scale with temple level:

| Temple Level | Multiplier |
|--------------|------------|
| Level 1 | 1.0x |
| Level 2 | 1.5x |
| Level 3 | 2.0x |
| Level 4 | 2.5x |
