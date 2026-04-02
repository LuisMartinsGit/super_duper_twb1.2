# The Waning Border - Technical Reference

> Unity 6 (6000.0.37f1) | DOTS/ECS (Entities 1.3.14) | C# | Hybrid MonoBehaviour UI

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Resources & Economy](#2-resources--economy)
3. [Factions & Cultures](#3-factions--cultures)
4. [Units](#4-units)
5. [Buildings](#5-buildings)
6. [Combat Systems](#6-combat-systems)
7. [Movement & Pathfinding](#7-movement--pathfinding)
8. [AI Systems](#8-ai-systems)
9. [Crystal Curse Faction](#9-crystal-curse-faction)
10. [Visibility & Fog of War](#10-visibility--fog-of-war)
11. [Training & Population](#11-training--population)
12. [Construction](#12-construction)
13. [Religion & Sects](#13-religion--sects)
14. [Era Progression](#14-era-progression)
15. [Input & Commands](#15-input--commands)
16. [Multiplayer & Lockstep](#16-multiplayer--lockstep)
17. [UI Systems](#17-ui-systems)
18. [Presentation & Visuals](#18-presentation--visuals)
19. [Tech Tree Diagrams](#19-tech-tree-diagrams)

---

## 1. Architecture Overview

### ECS Layer (Data-Oriented)
- **Components**: Global namespace in `Core/Components/` (CoreComponents, UnitComponents, BuildingComponents, ResourceComponents, CrystalComponents, FlowFieldComponents)
- **Systems**: `Systems/` organized by domain (Movement, Combat, Work, Training, Visibility, Presentation, Economy)
- **Entity Factories**: `Entities/Units/` and `Entities/Buildings/` create configured entities via ECB

### Managed Layer (MonoBehaviour)
- **UI**: IMGUI panels in `UI/Panels/` and `UI/HUD/`
- **Input**: `Input/RTSInputManager.cs`, `Input/SelectionSystem.cs`, `Input/CameraController.cs`
- **Presentation**: `Presentation/PresentationSpawnSystem.cs`, procedural generators
- **Managers**: FogOfWarManager, FlowFieldManager, EntityViewManager

### Command Flow
```
Player Input / AI Decision
        |
        v
  CommandRouter.cs  (single entry point)
        |
        +-- Singleplayer --> Execute immediately
        |
        +-- Multiplayer  --> LockstepManager queue
                                |
                                v
                          Deterministic tick execution
```

### System Execution Order
```
Input Systems
    -> CommandRouter
        -> Movement Systems (MovementSystem, FlowFieldManager, UnitSeparationSystem)
            -> TargetingSystem
                -> Combat Systems (Melee, Ranged, Building, Projectile)
                    -> DeathSystem
                        -> Economy Systems (Mining, Gathering, Income)
                            -> Training / Research Systems
                                -> Building Construction
```

### Key Files
| Domain | Files |
|--------|-------|
| Commands | `Core/Commands/CommandRouter.cs`, `Core/Commands/CommandTypes/*.cs` |
| Economy | `Economy/FactionEconomy.cs`, `Economy/FactionResources.cs`, `Economy/ResourceTickSystem.cs` |
| Mining | `Systems/Work/MiningSystem.cs`, `Systems/Work/CrystalMiningSystem.cs` |
| Construction | `Systems/Work/BuildingConstructionSystem.cs` |
| Combat | `Systems/Combat/TargetingSystem.cs`, `MeleeCombatSystem.cs`, `RangedCombatSystem.cs` |
| AI | `AI/Core/AIBrain.cs`, `AI/Managers/AIEconomyManager.cs`, `AI/Managers/AIBuildingManager.cs` |
| Movement | `Systems/Movement/MovementSystem.cs`, `FlowFieldManager.cs`, `FlowFieldGenerator.cs` |
| Pathfinding | `Systems/Movement/AStarPathfinder.cs`, `PassabilityGrid.cs` |
| Input | `Input/RTSInputManager.cs`, `Input/SelectionSystem.cs`, `Input/CameraController.cs` |
| UI | `UI/Panels/EntityInfoPanel.cs`, `UI/Panels/EntityActionPanel.cs`, `UI/Panels/BuildCommandPannel.cs` |
| Training | `Systems/Training/TrainingSystem.cs` |
| Crystal | `Systems/Crystal/CrystalSpreadSystem.cs`, `CrystalAISystem.cs` |
| Multiplayer | `Multiplayer/LockstepManager.cs`, `LockstepTypes.cs`, `LockstepBootstrap.cs` |
| Tech Tree | `Data/TechTree/TechTreeDB.cs`, `Resources/TechTree.json` |

---

## 2. Resources & Economy

### Resource Types

| Resource | Weight | Cap | Primary Sources |
|----------|--------|-----|-----------------|
| **Supplies** | 1x | 100,000 | Hall (50/15s), Gatherer Hut (area-based ~60/min), trade caravans, wall compartments (Alanthor) |
| **Iron** | 2x | 100,000 | Iron deposits (mined by miners), passive building income |
| **Crystal** | 3x | 100,000 | Creature cadavers (mined), Crystal Shrine income |
| **Veilsteel** | 5x | 100,000 | Smelter conversion (5 Iron + 3 Crystal = 1 Veilsteel / 5s) |
| **Glow** | 4x | 100,000 | Ley Line Nexus, special buildings |

### Iron Mining

| Parameter | Value |
|-----------|-------|
| Iron per deposit | 500 |
| Deposits per map | 12-20 |
| Gather interval | 2.0s per unit |
| Iron per gather | 1 |
| Max carry | 10 (+ CarryCapacityBonus from tech) |
| Gather range | 5.0 units |
| Dropoff range | 6.0 units |
| Auto-find radius (AI) | 50 units |

**Miner State Machine**: Idle -> MovingToDeposit -> Gathering -> ReturningToBase -> (loop)

Deposits persist when depleted. Miners auto-find new deposits within LineOfSight on depletion. Player miners require explicit GatherCommand; AI miners auto-find.

### Crystal Mining (Cadavers)

| Parameter | Value |
|-----------|-------|
| Crystal per cadaver | 300 (default) |
| Gather interval | 1.5s per unit |
| Max carry | 10 |
| Cadaver radius | 0.8 units |

Cadavers spawn when Crystal Curse creatures die. **Cadavers are destroyed when depleted** (unlike iron deposits).

### Smelter Conversion

| Parameter | Value |
|-----------|-------|
| Input | 5 Iron + 3 Crystal |
| Output | 1 Veilsteel |
| Conversion time | 5 seconds |
| Local storage | 100 Iron, 50 Crystal max |
| Loss factor | 20% (Foundry/Crucible buildings) |

Miners with ForgeSupplyOrder fetch resources from Hall/GathererHut and deliver to the Smelter's ForgeStorage.

### Passive Income Buildings

| Building | Income | Interval |
|----------|--------|----------|
| Hall | 50 Supplies | 15 seconds |
| Gatherer Hut | ~60 Supplies/min | Area-based (12-unit radius, no stacking) |
| Custom buildings | IronIncome, CrystalIncome, VeilsteelIncome, GlowIncome | Per-minute -> ticks per-second |

### Vault Banking (Vault of Almierra / Runai Vault)

- Interest rate: 3% per minute on deposits
- Lock timer: 3 minutes after deposit/withdraw
- Continuous compounding: `amount += amount * rate * dt / 60`

### Trade Economy (Runai)

- Trade Hubs spawn caravans every 22s (max 3 per route)
- Base yield: 20 Supplies per caravan delivery
- Distance bonus: +25% if route > 60 units
- Caravan armor aura: +1 to nearby caravans
- 5 TradePatrol escorts per trade lane (uncontrollable)

### Wall Compartment Income (Alanthor)

- Enclosed wall areas generate Supplies proportional to area
- Income pauses if any wall segment in the compartment falls
- Stone Ledgers tech: +8 Supplies per 10 sq. units per minute

---

## 3. Factions & Cultures

### Faction Slots (8)

| Slot | Name | Color RGB | Role |
|------|------|-----------|------|
| 0 | Blue | (0.20, 0.55, 1.00) | Player |
| 1 | Red | (1.00, 0.20, 0.25) | Player |
| 2 | Green | (0.20, 0.90, 0.35) | Player |
| 3 | Yellow | (1.00, 0.85, 0.20) | Player |
| 4 | Purple | (0.80, 0.40, 1.00) | Player |
| 5 | Orange | (1.00, 0.55, 0.15) | Player |
| 6 | Teal | (0.20, 1.00, 0.95) | Player |
| 7 | White | (1.00, 1.00, 1.00) | Crystal Curse AI (always) |

Additional lobby colors: Pink, Brown, Black, Maroon (12-color pool total).

### Cultures (chosen at Era 2)

**Age-up cost**: 800 Supplies + 200 Iron + 150 Crystal (at Hall, requires one religious building).

#### Runai - "The Veil Scholars"
- **Philosophy**: Preserve the Curse, learn from it, pacify it
- **Playstyle**: Trade / Guerrilla / Tech
- **Aesthetic**: Arabic-influenced tents, sandstone, cyan-blue magic
- **Economy**: Trade routes between outposts; caravans auto-generate Supplies; distance bonus
- **Unique mechanic**: Mobile HQ (Thessara's Bazaar) with pack/unpack, 2 training queues
- **Colors**: Cyan (0.25, 0.75, 0.80) / Sandstone (0.76, 0.65, 0.45)

#### Alanthor - "The Iron Covenant"
- **Philosophy**: Exploit the Curse for profit
- **Playstyle**: Economy / Defense
- **Aesthetic**: Medieval European stone, thick walls, forges
- **Economy**: Walled compartments generate income by enclosed area; income pauses if walls fall
- **Unique mechanic**: Wall hub/segment system; King's Court with +10% building HP and +15% repair rate auras
- **Colors**: Sage Green (0.55, 0.65, 0.50) / Warm Grey (0.45, 0.45, 0.42)

#### Feraldis - "The Ashborn"
- **Philosophy**: Destroy the Curse through blood and fire
- **Playstyle**: Aggression / Magic Denial
- **Aesthetic**: Celtic/Viking, dark wood, totems, blood-red war paint
- **Economy**: Gatherer Huts persist into Era 2 (unique); Pillage mechanic (+15 Supplies + 1 Iron per non-military kill)
- **Unique mechanic**: Fiendstone Keep (2000 HP, +25% training speed aura, ranged attack); Longhouse batch training (5 or 10 units, -5% cost, -10% time)
- **Colors**: Crimson Red (0.70, 0.18, 0.15) / Dark Grey (0.28, 0.26, 0.24)

---

## 4. Units

### Unit Classification

| Class ID | Name | Examples |
|----------|------|----------|
| 0 | Melee | Swordsman, Spearman, Sentinel, Berserker |
| 1 | Ranged | Archer, Crossbowman, Skirmisher |
| 2 | Siege | Catapult, Ballista, Siege Ram |
| 3 | Support | Litharch, Builder |
| 4 | Magic | Golem Autark, Archivist Adept, Glassmark Arcanist |
| 5 | Economy | Miner, Builder, Caravan |
| 6 | Miner | Resource gatherers |
| 7 | Scout | Fast reconnaissance |

### Era 1 Units (Universal)

| Unit | HP | Speed | Damage | Range | LoS | Cost | Pop | Cooldown | Armor | Special |
|------|----|-------|--------|-------|-----|------|-----|----------|-------|---------|
| Builder | 60 | 4.0 | 2 | 1.0 | 14 | 50S | 1 | - | Infantry Light | CanBuild |
| Miner | 70 | 6.0 | 2 | 1.0 | 14 | 50S | 1 | - | Infantry Light | Gathers Iron/Crystal |
| Scout | 60 | 6.0 | 2 | 1.0 | 40 | 55S | 1 | - | Infantry Light | Extended LoS |
| Swordsman | 120 | 5.5 | 10 | 1.0 | 16 | 140S | 1 | 1.5s | Infantry Heavy | Melee Def +1 |
| Archer | 90 | 5.2 | 17 | 10-25 | 30 | 75S | 1 | 2.0s | Ranged | Retreats at min range, Ranged Def +1 |
| Litharch | 120 | 5.5 | 0 (6 heal/s) | 10.0 | 20 | 100S+25I+10C | 1 | 1.5s | Ranged | Healer, Magic Def +2 |

### Runai Culture Units

| Unit | HP | Speed | Damage | Range | LoS | Cost | Pop | Cooldown | Armor | Special |
|------|----|-------|--------|-------|-----|------|-----|----------|-------|---------|
| Spearman | 130 | 5.6 | 12 | 1.0 | 10 | 110S+30I+25C | 1 | 1.0s | Infantry Heavy | +50% vs Cavalry, Melee Def +2, Ranged Def +1 |
| Skirmisher | 95 | 6.0 | 15 | 5-11 | 15 | 95S+50I+25C | 1 | 1.3s | Ranged | Hit-and-run, Ranged Def +1 |
| Raider | 120 | 7.2 | 10 | 3-14 | 15 | 220S+100I+50C | 1 | 1.4s | Cavalry | Mounted archer, fires while moving |
| Catapult | 160 | 3.0 | 24 | 10-18 | 22 | - | 2 | 4.5s | Structure | AOE splash (radius 3.0), Siege Def +2 |
| Sand Ballista | 200 | 3.4 | 36 | 8-20 | 22 | 260S+120I+80C | 2 | 3.5s | Structure | Long-range siege, Siege Def +2 |

### Alanthor Culture Units

| Unit | HP | Speed | Damage | Range | LoS | Cost | Pop | Cooldown | Armor | Special |
|------|----|-------|--------|-------|-----|------|-----|----------|-------|---------|
| Sentinel | 160 | 3.2 | 14 | 1.0 | 10 | - | 2 | 1.4s | Infantry Heavy | Melee +8, Ranged +4, Siege +2, Magic +1 |
| Crossbowman | 100 | 3.5 | 16 | 6-22 | 25 | - | 2 | 2.0s | Infantry Heavy | Melee +2, Ranged +2 |
| Cataphract | 180 | 6.5 | 18 | 1.0 | 10 | - | 2 | 1.1s | Cavalry | Melee +3, Ranged +2 |
| Ballista | 220 | 2.8 | 50 | 10-24 | 26 | - | 2 | 4.0s | Structure | Longest range, highest single-target damage, Siege +3 |

### Feraldis Culture Units

| Unit | HP | Speed | Damage | Range | LoS | Cost | Pop | Cooldown | Armor | Special |
|------|----|-------|--------|-------|-----|------|-----|----------|-------|---------|
| Berserker | 80 | 5.8 | 14 | 1.0 | 10 | 110S+20I+20C | 1 | 1.0s | Infantry Light | Cannot be healed (UnhealableTag), Melee +2 |
| Hunter | 90 | 5.7 | 16 | 0-8 | 12 | 90S+10I+20C | 1 | 1.2s | Infantry Light | MinRange=0 (never retreats) |
| Warboar Rider | 200 | 5.8 | 20 | 1.0 | 10 | 210S+80I+40C | 1 | 1.0s | Cavalry | Melee +1 |
| Siege Ram | 300 | 3.0 | 34 | 1.0 | 10 | 280S+140I+70C | 2 | 3.0s | Infantry Heavy | Melee +4, Ranged +2, Siege +6 |

### Crystal Curse Units (Faction White, no population cost)

| Unit | Damage Type | Armor | Def (M/R/S/Mag) | Special |
|------|-------------|-------|------------------|---------|
| Crystalling | Siege | Infantry Light | +2/+1/0/+1 | Fast melee swarm |
| Veilstinger | Magic | Ranged | +3/+2/+1/+2 | Dual-laser (hits 2 targets simultaneously) |
| Godsplinter | Siege | Infantry Heavy | +10/+8/+5/+5 | Massive hybrid: melee + multi-target laser barrage |

### Sect Units (12 total, one per sect)

| Sect | Unit | HP | Speed | Dmg | Range | Cooldown | Armor | Key Trait |
|------|------|----|-------|-----|-------|----------|-------|-----------|
| Renewal | ScarGuard | 170 | 3.2 | 16 | Melee | 1.2s | Infantry Heavy | RapidMend self-heal ability |
| Antiquity | Golem Autark | 320 | 2.0 | 22 | 0-10 | 2.0s | Infantry Heavy | Magic damage, highest HP sect unit |
| Living Stone | StoneWarden | 200 | 2.8 | 10 | Melee | 1.4s | Infantry Heavy | HP tank (Melee +4, Ranged +3) |
| Veiled Memory | Archivist Adept | 110 | 3.5 | 14 | 0-14 | 1.6s | Ranged | Dispel ability, Magic +2 |
| Still Flame | FlameWarden | 150 | 3.8 | 15 | Melee | 1.1s | Infantry Heavy | Sanction (root 2s) |
| Quiet Vault | VaultKeeper | 140 | 3.5 | 12 | Melee | 1.3s | Infantry Heavy | Safeguard (damage reduction aura) |
| Mirror Rite | Glassmark Arcanist | 100 | 3.5 | 18 | 0-15 | 1.5s | Ranged | Highest magic damage, Magic +1 |
| Shard Judgment | Judicator | 160 | 3.4 | 16 | Melee | 1.2s | Infantry Heavy | Melee +3, Ranged +1 |
| Ember Ash | Ashblade | 155 | 5.0 | 14 | Melee | 1.0s | Infantry Light | Very fast raider |
| Hollow Brand | Brandbreaker | 150 | 4.0 | 12 (Siege) | Melee | 1.5s | Infantry Heavy | Anti-structure (SiegeTag) |
| Flamewrought Chains | Chaincaster | 105 | 3.5 | 10 | 0-14 | 1.8s | Ranged | ChainBind (short root), Magic +1 |
| Unmaker's Grasp | Nullblade | 150 | 4.2 | 14 | Melee | 1.1s | Infantry Light | Anti-Crystal (+6 vs magic), Magic +1 |

### Special Units

| Unit | HP | Speed | Damage | Pop | Behavior |
|------|----|-------|--------|-----|----------|
| Caravan | 120 | 5.6 | 0 | 0 | Uncontrollable, auto-trades between posts, drops cargo on death |
| Trade Patrol | 80 | 5.0 | 8 | 0 | 5 per trade lane, auto-patrols and engages enemies |

---

## 5. Buildings

### Era 1 Buildings (Universal)

| Building | HP | Cost | Pop | LoS | Trains | Researches | Special |
|----------|----|------|-----|-----|--------|------------|---------|
| Hall | 2400 | Starting | +20 | 24 | Builder, Miner, Scout | Research_Era2, ImprovedTools, StorageCarts | Ranged attack (12 dmg, range 20, 2.5s cd). 50 Supplies/15s |
| Hut | 350 | 50S | +5 | 12 | - | - | Housing only |
| Gatherer Hut | 400 | 120S | - | 16 | - | - | Area-based 60 Supplies/min (12-unit radius). Auto-despawns Era 2 (except Feraldis) |
| Barracks | 800 | 150S+70I | - | 18 | Swordsman, Archer | BasicDrills, WoodenArmor | Single training queue |

### Religious Buildings (Era 1, choose one)

| Building | HP | Cost | Special |
|----------|----|------|---------|
| Shrine of Ridan | 800 | 300S+100C | Temple leveling (1-4). Trains Litharch. Grants 1 Sect Point. Enables era advancement |
| Vault of Almierra | 1200 | 300S+100C | Banking (3% interest/min). Deposit/withdraw with 3-min lock |
| Fiendstone Keep | 2000 | 300S+100C | 1.25x training speed aura. Ranged attack. Berserker conversion |

### Runai Culture Buildings

| Building | HP | Cost | LoS | Trains | Special |
|----------|----|------|-----|--------|---------|
| Thessara's Bazaar | 2700 | 600S+200I+100C | 26 | Spearman, Skirmisher, Raider | Mobile HQ (pack/unpack). 2 training queues. +40 pop |
| Outpost | 900 | 140S+20I | 22 | - | Trade route anchor, 18-unit vision radius |
| Trade Hub | 1200 | 240S+40I | 24 | - | Spawns caravans (1/22s, max 3). +25% yield for long routes |
| Runai Vault | 1100 | 1500S+250I+200C | 20 | - | Banking (3% interest/min) with tariff synergy |
| Veilsteel Foundry | 1500 | 450S+120I+100C | 20 | - | Veilsteel crafting (20% loss factor) |
| Siege Workshop | 1100 | 320S+140I+60C | 20 | Sand Ballista | Siege unit training |

### Alanthor Culture Buildings

| Building | HP | Cost | LoS | Trains | Special |
|----------|----|------|-----|--------|---------|
| King's Court | 2100 | 360S+80I | 26 | - | +10% building HP aura, +15% repair rate. +10 pop. Researches Stone Ledgers, Mason's Guild |
| Wall Hub | 600 | 40S+20I | 8 | - | Connection point for wall segments |
| Wall Segment | 400 | 40S+20I | 5 | - | Connects hubs; enclosed areas generate Supplies |
| Watch Tower | 950 | 140S+70I | 28 | - | 4 garrison slots, arrow fire |
| Garrison | 1500 | 220S+90I | 22 | Sentinel, Crossbowman | 6 garrison slots. +8 pop |
| Royal Stable | 1300 | 260S+120I+40C | 20 | Cataphract | Heavy cavalry training |
| Siege Yard | 1300 | 260S+140I+60C | 20 | Ballista | Siege engine training |
| Smelter | 1000 | 220S+100I | 14 | - | 5 Iron + 3 Crystal = 1 Veilsteel / 5s. Local storage: 100I, 50C |
| Crucible | 1200 | 200S+60I+40C | 18 | - | Advanced veilsteel (20% loss factor) |

### Feraldis Culture Buildings

| Building | HP | Cost | LoS | Trains | Special |
|----------|----|------|-----|--------|---------|
| Hunting Lodge | 1000 | 160S+20I | 18 | - | Upgraded Gatherer Hut (bonus near wildlife) |
| Logging Station | 1000 | 160S+20I | 18 | - | Upgraded Gatherer Hut (bonus near trees) |
| Fiend Foundry | 1300 | 200S+80I+30C | 18 | - | Veilsteel forging and weapons |
| Totem Tower | 900 | 120S+60I | 26 | - | 4 garrison slots. +25% attack on bloody ground |
| Longhouse | 1400 | 260S+100I | 20 | Berserker, Warboar Rider | Batch training (5/10 units, -5% cost, -10% time). +10 pop |
| Siege Yard | 1200 | 260S+120I+40C | 20 | Siege Ram | Siege training |

### Crystal Curse Buildings

| Building | HP | Radius | Defense (M/R/S/Mag) | Special |
|----------|----|---------|--------------------|---------|
| Crystal Main Node | 5000 | 1.5 | +15/+15/+10/+10 | Central hive. Spreads cursed ground. Self-defense turret (30 dmg, range 20, 3 targets) |
| Crystal Resource Node | 1500 | 1.0 | - | Spreads cursed ground. Generates crystal income |
| Crystal Enforcement Node | 1200 | 0.8 | - | Buff aura (12u): +3 Def, +2 Att, +15% Speed to crystal entities |
| Crystal Suppression Node | 1200 | 0.8 | - | Debuff aura (12u): -2 Def, -2 Att, -20% Speed to enemies |
| Crystal Restoration Node | 1200 | 0.8 | - | Heal aura (12u): 5 HP/s to crystal entities |
| Crystal Turret Node | 1500 | 1.0 | - | Ranged turret: 25 magic dmg, range 18, 3 max targets, 2.0s cd |

---

## 6. Combat Systems

### Damage Types & Armor Types

**Damage Types**: Melee (0), Ranged (1), Siege (2), Magic (3), True (4)

**Armor Types**: Infantry Light (0), Infantry Heavy (1), Ranged (2), Cavalry (3), Structure (4), Structure_Human (5)

### Damage Modifier Matrix

|  | Infantry Light | Infantry Heavy | Ranged | Cavalry | Structure | Structure Human |
|--|---------------|---------------|--------|---------|-----------|----------------|
| **Melee** | 1.0 | 1.0 | 1.1 | 0.9 | 0.2 | 0.2 |
| **Ranged** | 1.1 | 0.9 | 1.0 | 0.8 | 0.15 | 0.15 |
| **Siege** | 0.6 | 0.8 | 0.8 | 0.7 | 3.0 | 2.4 |
| **Magic** | 1.1 | 0.9 | 1.1 | 1.0 | 0.5 | 0.45 |
| **True** | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |

### Defense Formula

```
effective_damage = base_damage * modifier[dmgType][armorType] * (1 - defense / (defense + 100))
```

Defense uses diminishing returns. The relevant defense stat is matched by damage type (e.g., Melee defense vs Melee damage).

### Height Advantage

| Parameter | Value |
|-----------|-------|
| HeightDamageScale | 0.04 (4% per unit of height) |
| MaxHeightBonus | +0.20 (+20% cap) |
| MaxHeightPenalty | -0.20 (-20% cap) |

### Crystal Buff/Debuff Modifiers

Applied by Crystal Enforcement and Suppression Nodes:
```
crystalMod = (1 + attacker_buff.AttBonus) * (1 + target_debuff.AttPenalty)
finalDamage = max(1, baseDamage * armorMod * defenseMod * heightMod * crystalMod)
```

### Melee Combat System

| Parameter | Value |
|-----------|-------|
| MeleeRange | 1.5 (+ target radius for buildings) |
| Default cooldown | 1.5s |

Flow: Validate target -> Check range -> In range: stop, apply damage on cooldown. Out of range: chase via DesiredDestination.

### Ranged Combat System (Archers)

| Parameter | Value |
|-----------|-------|
| Default MinRange | 10 |
| Default MaxRange | 25 |
| ArrowSpeed | 30 |
| AimDuration | Dynamic (longer at range) |

**States**: Too close (< MinRange) -> retreat. Optimal (MinRange-MaxRange) -> aim and fire. Too far (> MaxRange) -> chase. Battalion members do NOT retreat independently.

### Projectile System

**Arrows**: Quadratic Bezier curve, FlightDuration 0.8s, ArcHeight 3 units, HitRadius 0.8. Guaranteed hit if target alive.

**Lasers**: Straight line at constant velocity. Terrain collision check. Used by Crystal buildings and Veilstingers.

**AOE**: Splash damage within radius at impact point (Catapults, Godsplinters).

### Building Combat System

| Parameter | Value |
|-----------|-------|
| ArrowSpeed | 25 |
| LaserSpeed | 55 |
| SpawnYOffset | Radius + 0.5 (taller buildings shoot higher) |

Buildings auto-target nearest enemies within range. Sorts by distance, fires at up to MaxTargets. Crystal buildings fire lasers (Magic damage); others fire arrows (Ranged damage).

### Targeting System

| Parameter | Value |
|-----------|-------|
| MaxGuardDistance | 20 |
| GuardReturnThreshold | 2 |
| BattalionDefaultLeash | 25 |
| DefaultMeleeRange | 1.5 |

1. Creates GuardPoint (current position) and AttackCooldown (1.5s)
2. Processes user AttackCommands -> sets Target
3. Auto-acquire: idle units scan LoS for nearest enemy
4. Return to Guard: no enemies in LoS -> walk back to GuardPoint
5. Cleanup: removes stale AttackCommand components

### Death System

1. Tick death animations (DeathAnimationState.Timer)
2. Collect dead entities (Health <= 0, no BattalionLeader, no death animation)
3. Remove from battalions (dead members removed from leader's buffer)
4. Squadron cleanup (if battalion empty, kill leader)
5. Cleanup references (remove Target/AttackCommand from entities targeting the dead)
6. Destroy entities via ECB

---

## 7. Movement & Pathfinding

### Movement System

| Parameter | Value |
|-----------|-------|
| StopDistance | 0.5 |
| DefaultMoveSpeed | 3.5 |
| TurnSpeed | 8 rad/s (~460 deg/s) |
| MaxWalkableSlope | 0.55 |
| SlopeCheckStep | 1.5 |

**Pipeline**:
1. Command conversion: MoveCommand/AttackMoveCommand -> DesiredDestination + UserMoveOrder
2. Pathfinding: Flow Field (default) or A* (fallback)
3. Movement: step = min(speed * dt, distance). Passability + slope checks.
4. Stuck detection (3-tier): 1-5 frames retry, 6-30 frames perpendicular, 30+ cancel.
5. Terrain height snapping (cached per cell). Smooth rotation via slerp.

### Flow Field System

| Parameter | Value |
|-----------|-------|
| MaxCacheSize | 8 concurrent flow fields |
| MaxFieldsPerFrame | 2 (async throttle) |
| MaxSnapSearchCells | 25 |
| Direction lerp rate | 12.0 rad/s |

**Architecture**: LRU cache keyed by snapped destination cell. Async BFS generation on worker thread. NativeArray pooling for zero-allocation steady state. Grid version tracking detects passability changes.

**FlowFieldGenerator**: BFS from destination cell. Cost grid (1=passable, 255=blocked). Per-cell float2 direction. 8-directional with diagonal corner-cutting rules.

**FlowFieldLookup**: Burst-compatible. Flat NativeArray indexed by `slot_i * total_cells`. NativeHashMap for O(1) destination->slot lookup.

### A* Pathfinding (Fallback)

| Parameter | Value |
|-----------|-------|
| CardinalCost | 10 |
| DiagonalCost | 14 |
| MaxSnapSearch | 25 (spiral to passable) |

Per-unit waypoint following from AStarPathStore. Path simplification removes collinear intermediate points. Corner-cutting prevention on diagonals.

### Passability Grid

- Cell-based occupancy from terrain + buildings + obstacles
- PassabilityBuildingSync updates on placement/destruction
- Grid-aligned rectangular buildings with decal indicators

### Unit Separation System

| Parameter | Value |
|-----------|-------|
| PushForce | 8 |
| MinSeparation | 0.1 |
| UpdateInterval | 0.1s (10/sec) |
| CellSize | 3 (spatial hash) |

Spatial hashing via NativeParallelMultiHashMap. Reduces neighbor scan from O(n^2) to O(neighbors). Reduces push force for moving units to avoid jitter.

### Battalion System

**BattalionLeader**: Columns, Rows (formation grid), Spacing, LeashDistance.

**Sticky slot assignment**: Members keep Column/Row between frames. Reassign only on rotation > 30 deg, NeedsReassignment flag, or initial setup. Greedy nearest-slot matching.

**BattalionSyncSystem**: Computes slot world positions from leader pos + rotation. Handles obstacle avoidance (blocked members path toward leader).

**BattalionLeashSystem**: Teleports members exceeding LeashDistance to slot.

---

## 8. AI Systems

### AI Brain

```
AIBrain {
    Faction Owner;
    float UpdateInterval;
    AIPersonality: Balanced | Aggressive | Defensive | Economic | Rush;
    AIDifficulty: Easy | Normal | Hard | Expert;
}
```

### AI Managers

| Manager | Responsibilities |
|---------|-----------------|
| **AIEconomyManager** | Tracks gatherer huts, miners, resource levels. Builds huts, assigns miners, manages economy |
| **AIBuildingManager** | Places buildings based on needs. Prioritizes Halls, Barracks, defenses. Respects passability |
| **AIMilitaryManager** | Spawns units by economic capacity. Organizes attack waves |
| **AITacticalManager** | Identifies threats. Plans attack routes. Attack vs defend decisions |
| **AIMissionManager** | High-level mission control. Multi-unit coordination |

### Shared Knowledge

```
AISharedKnowledge {
    EnemyLastKnownPosition, EnemyLastSeenTime, EnemyEstimatedStrength,
    KnownEnemyBases, OwnMilitaryStrength, OwnEconomicStrength,
    EnemyBasesSpotted, EnemyArmiesSpotted
}
```

### AI Tuning (Configurable Constants)

Extracted to `AITuning` class for per-difficulty adjustment. Controls build priorities, economy thresholds, aggression timing, and smelter/vault interaction logic.

---

## 9. Crystal Curse Faction

### Spread System

| Parameter | Value |
|-----------|-------|
| MainNodeTickInterval | Configurable (CrystalConstants) |
| Level 1 spread rate | 3.0 units/tick |
| Level 2 spread rate | 2.0 units/tick |
| Level 3 spread rate | 1.0 units/tick |
| BaseRingStep | 2 units |
| TileSpacing | 3.5 units arc distance |
| TileRadius | 2 units per tile |
| MaxTilesPerNode | 200 |

Ring expansion model: cursed ground spreads in expanding rings from main node. Level calculated from CurrentRingRadius (1: 0-5, 2: 5-10, 3: 10+).

### Cursed Ground Damage

| Parameter | Value |
|-----------|-------|
| DamageTickInterval | 1 second |
| BaseDPS | 2 per second |
| Effect radius | 2 units per tile |

Crystal-tagged entities are immune. Damage = max(1, DPS * interval).

### Crystal Income

| Parameter | Value |
|-----------|-------|
| TickInterval | 1 second |
| IncomePerAreaUnit | 0.03 crystal |
| TileRadius | 2 (area = pi * 4) |

`income = ceil(tileCount * pi * 4 * 0.03)` credited to Faction.White.

### Crystal AI

| Parameter | Value |
|-----------|-------|
| DecisionInterval | 5 seconds |
| MaxCurseNodes | 16 |
| BaseExpansionInterval | 30s |
| ExpansionSlowdownRate | 20 |
| MaxExpansionInterval | 300s (5 min) |

**Behaviors**: Node building (sub-nodes in cursed areas), unit spawning (from crystal bank), harassment (attack waves at player bases), expansion (new main nodes).

### Sub-Node Types

| Type | Role |
|------|------|
| Resource | Crystal generation |
| Turret | Ranged defense |
| Restoration | Heal crystal entities (5 HP/s) |
| Enforcement | Buff aura (+3 Def, +2 Att, +15% Speed) |
| Suppression | Debuff aura (-2 Def, -2 Att, -20% Speed) |

---

## 10. Visibility & Fog of War

### States

| State | Visual | Information |
|-------|--------|-------------|
| Hidden | Dark fog | Never seen, no information |
| Revealed | Light fog | Previously seen, building ghosts visible |
| Visible | Clear | Currently in LoS, real-time |

### Implementation

- FogOfWarManager (MonoBehaviour singleton)
- Per-frame: clear visibility -> stamp circles for all units with LineOfSight -> mark revealed permanently -> generate texture
- Static queries: `IsVisibleToFaction()`, `IsRevealedToFaction()`
- Crystal entities excluded from revealing fog

---

## 11. Training & Population

### Training System

1. UI adds TrainQueueItem to building's buffer
2. System starts first queued unit if Busy = 0
3. Timer: Remaining -= dt each frame
4. Complete: check population capacity, spawn unit via ECB, reset
5. Cost paid at queue time (not spawn time)
6. MAX_TRAIN_QUEUE: 5

### Population

- PopulationHelper.GetUnitPopulationCost(unitId)
- Battalions: cost * 15 (5 columns * 3 rows)
- Spawn blocked if insufficient capacity (waits for pop to free)
- Buildings provide pop: Hall +20, Hut +5, Garrison +8, Longhouse +10, Bazaar +40

### Batch Training (Feraldis Longhouse)

| Parameter | Value |
|-----------|-------|
| BatchSize | 5 or 10 units |
| TimeMultiplier | 0.9x (10% faster) |
| CostDiscount | 5% |

All units spawn simultaneously when timer expires. Pop check: total_pop_cost * batch_size.

---

## 12. Construction

### Building Construction System

| Parameter | Value |
|-----------|-------|
| BuildRange | 4 units |
| BuildRatePerBuilder | 1 progress/second |

Multiple builders can work simultaneously. On completion: remove UnderConstruction tag, set Health to max, apply DeferredDefense as Defense component.

### Building Placement Validation

- Circle-vs-circle overlap (buildings, obstacles)
- Terrain slope and passability
- Water depth check
- Faction grid-aware snapping
- Wall hub snap distance: 2.0 units (chain placement)
- Shift+click: stay in placement mode for repeated placement

---

## 13. Religion & Sects

### Sect Point Income

| Source | Points |
|--------|--------|
| Culture adoption (Era 2) | 2 RP |
| Temple Level 2 (Era 3) | 3 RP |
| Temple Level 3 (Era 4) | 3 RP |
| Temple Level 4 (Era 5) | 3 RP |
| Shrine bonus (if built Era 1) | +1 RP |
| **Maximum** | **8-9 RP** |

### Sect Adoption Cost

- Culture affinity: 1 RP
- Foreign sect: 2-3 RP

### The 12 Sects

#### Alanthor-Affinity

| Sect | Passive | Tech | Spell (Cooldown) |
|------|---------|------|------------------|
| **Renewal** | +20% income if all walls full HP | DietaryMandate: out-of-combat regen | RepairLevies (40s): rapid building repair |
| **Antiquity** | +20% research speed | ClockworkArchives: -15% research, -5% cooldown | CrystalSurvey (50s): reveal nodes + 30 Crystal |
| **Living Stone** | +20% wall income, +10% build speed | TerracePlanning: +20% compartment Supplies | BulwarkRise (60s): +3 armor to buildings |
| **Veiled Memory** | +15% fog vision, -10% spell cooldown | HiddenRecords: -25% Crystal retaliation | Shroud (55s): 12u mist (blocks vision, -20% speed) |

**Synergy pairs**: Renewal + Living Stone = "The Fortress" | Antiquity + Veiled Memory = "The Archive"

#### Runai-Affinity

| Sect | Passive | Tech | Spell (Cooldown) |
|------|---------|------|------------------|
| **Still Flame** | +15% trade income, +25% tariff | SanctifiedRoutes: +5 armor to route caravans | Embargo (60s): disable enemy trade 20s, siphon 30S |
| **Quiet Vault** | +30% bank interest | HiddenLedgers: retain 50% on depot destruction | LockdownVault (70s): unraidable 15s, slow enemies |
| **Mirror Rite** | +10% ranged accuracy, -5% spell cd | RefinedSilverInlays: +10% magic, -10% cd | ReflectiveWard (60s): reflect 25% spell + 10% physical |
| **Shard Judgment** | +10% law enforcement, -20% enemy build near trade | IronDecrees: -20% enemy build speed near nodes | EdictOfSeizure (65s): drain 50S from enemy over 10s |

**Synergy pairs**: Still Flame + Quiet Vault = "The Merchant" | Mirror Rite + Shard Judgment = "The Inquisitor"

#### Feraldis-Affinity

| Sect | Passive | Tech | Spell (Cooldown) |
|------|---------|------|------------------|
| **Ember Ash** | +12% melee damage, +10% train speed | WarTithe: +5S per enemy civilian kill | BattleFervor (55s): +25% attack & speed in 10u for 10s |
| **Hollow Brand** | 5% panic on hit | DesecrateStandards: -20% enemy morale auras | ProfaneRally (60s): pull enemies 2u + 30% slow 5s |
| **Flamewrought Chains** | 3% control stack per hit | VeilsteelLinks: +1% DR per iron ingot | BindTheCore (90s): pacify Crystal sub-node 15s |
| **Unmaker's Grasp** | +20% vs Crystal entities | ErasureRites: +20% Crystal drop yield | Unravel (80s): heavy true damage to Crystal unit |

**Synergy pairs**: Ember Ash + Hollow Brand = "The Warband" | Flamewrought Chains + Unmaker's Grasp = "The Purifier"

### Temple Power Scaling

| Temple Level | Multiplier |
|-------------|------------|
| 1 | 1.0x |
| 2 | 1.5x |
| 3 | 2.0x |
| 4 | 2.5x |

### Temple Cascade

Destroying a temple destroys all attached chapels (TempleCascadeDestroySystem).

---

## 14. Era Progression

| Era | Name | Requirement |
|-----|------|-------------|
| 1 | Dawn of Ashes | Starting era, universal roster |
| 2 | Age of Divergence | 800S + 200I + 150C + one religious building. Choose culture |
| 3 | - | Temple Level 2 |
| 4 | - | Temple Level 3 |
| 5 | - | Temple Level 4 |

### Age-Up Process (Era 1 -> 2)

1. Hall starts age-up timer (AgeUpState.Remaining)
2. Timer ticks down each frame
3. On completion:
   - Set FactionProgress.Culture on Hall
   - Scale Hall 1.3x
   - Set FactionEra = 2 on faction bank
   - Grant RP if Temple exists (2 RP for temple level 1)
   - **Alanthor special**: Start 2-minute self-destruct on all GathererHuts (80% refund)
   - Rebuild visual with culture tone
   - Remove AgeUpState

---

## 15. Input & Commands

### Command Types (10 + extras)

| Command | Trigger | Behavior |
|---------|---------|----------|
| Move | Right-click ground | Sets DesiredDestination + UserMoveOrder. Clears attack/gather/build |
| Attack | Right-click enemy | Sets Target. Clears UserMoveOrder. Sets GuardPoint |
| AttackMove | Hotkey + right-click | Move with auto-acquire (no UserMoveOrder -> TargetingSystem can engage) |
| Patrol | Ctrl + right-click | Waypoint cycling between start and destination. Auto-acquires targets |
| Build | Builder + click building | Moves builder to site, starts construction |
| Gather | Right-click resource | Sets GatherCommand with deposit entity |
| Heal | Right-click friendly | Validates friendly + needs healing. Moves healer toward target |
| HoldPosition | Hotkey | Clears all commands. Adds HoldPositionTag. GuardPoint = current pos |
| Repair | Right-click damaged building | Moves builder to building, starts repair |
| Convert | Right-click Keep with miner | Miner -> Berserker conversion at Fiendstone Keep |

### Selection System

| Action | Behavior |
|--------|----------|
| Click | Select single entity (battalion members resolve to leader) |
| Double-click | All visible units of same UnitClass |
| Ctrl+double-click | All map-wide units of same type |
| Drag box | Multi-select (min 4px). Priority: units > buildings, military > economy |
| Ctrl+1-9 | Save control group |
| Shift+1-9 | Add to control group |
| 1-9 | Recall control group |
| Double-tap 1-9 | Center camera on group |

### Camera

| Parameter | Value |
|-----------|-------|
| Keyboard speed | 25 u/s (WASD) |
| Edge scroll speed | 30 u/s (border: 15px) |
| Zoom range | 15-80 |
| Rotation speed | 100 deg/s (Q/E) |
| Tilt range | 30-75 degrees (R/F) |
| Move damping | 0.15 |
| Height damping | 0.1 |

---

## 16. Multiplayer & Lockstep

### Architecture

Deterministic tick-based simulation over UDP.

| Parameter | Value |
|-----------|-------|
| TICKS_PER_SECOND | 10 (100ms per tick) |
| INPUT_DELAY_TICKS | 2 (200ms buffering) |
| MAX_TICK_BUFFER | 60 ticks ahead |
| SYNC_CHECK_INTERVAL | 30 ticks |

### Network Protocol

```
TICK|playerIndex|tickNumber|commandCount|cmd1|cmd2|...
SYNC|tickNumber|checksum
PING|timestamp / PONG|timestamp
```

### Command Serialization

```
"Type,EntityId,PosX,PosY,PosZ,TargetId,SecondaryId,BuildingId"
```

14 command types: Move, Attack, Stop, Build, Train, Gather, SetRally, Heal, AttackMove, Repair, Convert, Patrol, HoldPosition, PlaceBuilding.

### Execution Flow

1. Player issues command -> queued at currentTick + INPUT_DELAY_TICKS
2. Send tick to all players
3. Advance only when ALL players confirm
4. Execute all commands for that tick deterministically
5. Periodic checksum validation (every 30 ticks)

### NetworkedEntity

Each entity has NetworkId (unique int, assigned at spawn) + SpawnTick. Thread-safe ID generator with sync capability.

---

## 17. UI Systems

### Panel Layout

| Panel | Position | Size | Content |
|-------|----------|------|---------|
| ResourceHUD | Bottom-left | 200x256 | Population, RP, Supplies, Iron, Crystal, Veilsteel, Glow |
| EntityInfoPanel | Left of ResourceHUD | 320xDynamic | Single: portrait + stats. Multi: army breakdown grid |
| EntityActionPanel | Right side | 370xDynamic | Actions: build, train, research, vault, temple, stance |
| BuildCommandPannel | Overlay | - | Building placement preview with validation |
| MinimapUI | Bottom-right | 256x256 | Fog-of-war map, faction blips, camera frustum, click-to-move |

### ResourceHUD

- Updates every 0.25s (cached per faction)
- Shows income rates for all 7 resource types
- Alanthor-specific: wall income breakdown

### EntityActionPanel Action Types

| ActionType | Trigger | Content |
|------------|---------|---------|
| BuildingPlacement | Builder selected | Building placement buttons by era/culture |
| UnitTraining | Barracks/etc selected | Training queue (max 5), unit buttons |
| UnitTrainingAndResearch | Hall/Barracks selected | Training + tech tree buttons |
| VaultManagement | Vault selected | Deposit/withdraw interface |
| TempleUpgrade | Temple selected | Level upgrade, sect selection |
| BattalionStance | Battalion selected | Formation/stance buttons |

### Minimap

| Parameter | Value |
|-----------|-------|
| Resolution | 128 samples |
| Refresh | 0.1s |
| World bounds | (-125, -125) to (125, 125) |
| Unit blip radius | 2px |
| Building blip radius | 3px |

---

## 18. Presentation & Visuals

### Procedural Unit Generation

| Category | Scale | IDs |
|----------|-------|-----|
| Era 1 | 3.5x | Builder (200), Swordsman (201), Archer (202), Miner (203), Scout (206), Litharch (207), Berserker (210) |
| Runai | 3.5x | Spearman (330), Skirmisher (331), Raider (332), Catapult (333) |
| Alanthor | 3.5x | Sentinel (334), Crossbowman (335), Cataphract (336), Ballista (337) |
| Feraldis | 3.5x | Hunter (338), Warboar Rider (339), Siege Ram (340) |
| Sect | 3.5x | 370-381 (ScarGuard through Nullblade) |
| Mounted | 2.9x | Raiders, Cataphracts, Warboar Riders |
| Siege | 3.1x | Catapults, Ballistas, Rams |

### Procedural Building Generation

| Category | IDs |
|----------|-----|
| Era 1 Core | Hall (100), GatherersHut (101), Hut (102), Barracks (510) |
| Religious | Temple (520), Vault (530), Keep (540) |
| Runai | Outpost (350), TradeHub (351), Bazaar (352), SiegeWorkshop (353) |
| Alanthor | Tower (354), Garrison (355), Stable (356), SiegeYard (357), Wall Hub (550), Segment (551), Smelter (560) |
| Feraldis | HuntingLodge (358), LoggingStation (359), Longhouse (360), TotemTower (361), SiegeYard (362) |
| Chapels | 390-401 |

### Culture Color Accents

| Culture | Primary Accent |
|---------|---------------|
| Runai | Gold (0.76, 0.65, 0.45) |
| Alanthor | Grey (0.45, 0.45, 0.42) |
| Feraldis | Red (0.70, 0.18, 0.15) |

### EntityViewManager

Registry pattern: `Dict<Entity, GameObject>`. Bridges ECS entities to GameObjects for visual representation. PresentationSpawnSystem queries entities with PresentationID, instantiates/generates visuals, registers with manager, applies faction color.

### Construction Animation

Buildings emerge from ground (rising animation) during construction phase.

---

## 19. Tech Tree Diagrams

### Era 1 - Shared Tech Tree

```
                          +-----------+
                          |   HALL    |
                          | (Start)  |
                          +-----+-----+
                                |
              +-----------------+-----------------+
              |                 |                 |
    +---------v-------+  +-----v------+  +-------v--------+
    | Improved Tools  |  | Storage    |  | Research_Era2  |
    | 80S + 40I       |  | Carts      |  | 1000S+200I+150C|
    | +15% gather     |  | 90S        |  | -> Choose      |
    | speed           |  | +10 carry  |  |    Culture     |
    +-----------------+  +------------+  +--------+-------+
                                                  |
                                         Requires one of:
                                    Shrine / Vault / Keep
```

```
                        +------------+
                        |  BARRACKS  |
                        +------+-----+
                               |
                  +------------+------------+
                  |                         |
        +---------v--------+     +----------v--------+
        | Basic Drills     |     | Wooden Armor      |
        | 100S + 40I       |     | 80S               |
        | +10% melee       |     | +1 melee defense  |
        | attack speed     |     |                   |
        +------------------+     +-------------------+
```

### Era 2 - Runai Tech Tree

```
                     +---------------------+
                     | THESSARA'S BAZAAR   |
                     | (Mobile HQ)         |
                     +----------+----------+
                                |
          +---------------------+---------------------+
          |                     |                     |
+---------v----------+ +-------v---------+ +---------v----------+
| Long-Haul Tariffs  | | Pack Bazaar     | | Escorted Caravans  |
| 220S + 20I         | | 180S+10I+10C    | | 160S + 40C         |
| +15% trade income  | | -40% pack time  | | 2 escorts/caravan  |
| +25% if route >60u | | +200 HP packed  | | +2 pop per caravan |
+--------------------+ +-----------------+ +--------------------+


              RUNAI BUILDING UNLOCK TREE

    +----------+     +-----------+     +--------------+
    | Outpost  |---->| Trade Hub |---->| Runai Vault  |
    | 140S+20I |     | 240S+40I  |     | 1500S+250I   |
    | Route    |     | Spawns    |     | +200C        |
    | anchor   |     | caravans  |     | 3% interest  |
    +----------+     +-----------+     +--------------+

    +------------------+     +-------------------+
    | Veilsteel        |     | Siege Workshop    |
    | Foundry          |     | 320S+140I+60C     |
    | 450S+120I+100C   |     | Trains:           |
    | Veilsteel craft  |     | Sand Ballista     |
    +------------------+     +-------------------+


              RUNAI UNIT ROSTER

    Bazaar ----+----> Spearman (melee, anti-cav)
               |----> Skirmisher (ranged, hit-and-run)
               +----> Raider (cavalry, mounted archer)

    Siege Workshop --> Sand Ballista (siege, long-range)

    Trade Hub ------> Caravan (auto-trade, uncontrollable)
                      Trade Patrol (auto-escort x5)
```

### Era 2 - Alanthor Tech Tree

```
                     +---------------------+
                     |    KING'S COURT     |
                     | +10% building HP    |
                     | +15% repair rate    |
                     +----------+----------+
                                |
                   +------------+------------+
                   |                         |
         +---------v----------+    +---------v----------+
         | Stone Ledgers      |    | Mason's Guild      |
         | 220S + 40I         |    | 180S + 40I         |
         | +8S per 10 sq.u    |    | +15% building HP   |
         | per min (walled)   |    | +20% repair rate   |
         +--------------------+    +--------------------+


              ALANTHOR BUILDING UNLOCK TREE

    +----------+     +------------+     +---------------+
    | Wall Hub |<--->| Wall       |     | Watch Tower   |
    | 40S+20I  |     | Segment    |     | 140S+70I      |
    | Connect  |     | 40S+20I    |     | 4 garrison    |
    | point    |     | Enclose    |     | Arrow fire    |
    +----------+     +------------+     +---------------+

    +----------+     +---------------+     +--------------+
    | Garrison |     | Royal Stable  |     | Siege Yard   |
    | 220S+90I |     | 260S+120I+40C |     | 260S+140I    |
    | +8 pop   |     | Trains:       |     | +60C         |
    | Sentinel |     | Cataphract    |     | Trains:      |
    | Crossbow |     |               |     | Ballista     |
    +----------+     +---------------+     +--------------+

    +----------+     +-----------+
    | Smelter  |     | Crucible  |
    | 220S+100I|     | 200S+60I  |
    | 5I+3C    |     | +40C      |
    | = 1 Veil |     | Advanced  |
    | /5s      |     | forging   |
    +----------+     +-----------+


              ALANTHOR UNIT ROSTER

    Garrison ------+----> Sentinel (heavy melee tank, Def +8)
                   +----> Crossbowman (armored ranged)

    Royal Stable ------> Cataphract (heavy cavalry)

    Siege Yard --------> Ballista (longest range, 50 dmg)
```

### Era 2 - Feraldis Tech Tree

```
                     +---------------------+
                     |  FIENDSTONE KEEP    |
                     | +25% train speed    |
                     | Ranged attack       |
                     | Berserker convert   |
                     +----------+----------+
                                |
                   +------------+------------+
                   |                         |
         +---------v----------+    +---------v----------+
         | Pillage            |    | Iron Fury          |
         | 160S               |    | 120S + 40I         |
         | +15S +1I per       |    | Units carry 5 Iron |
         | non-military kill  |    | +2% attack/ingot   |
         +--------------------+    +--------------------+


              FERALDIS BUILDING UNLOCK TREE

    +---------------+     +------------------+
    | Hunting Lodge |     | Logging Station  |
    | 160S+20I      |     | 160S+20I         |
    | Upgraded hut  |     | Upgraded hut     |
    | (wildlife)    |     | (trees)          |
    +---------------+     +------------------+

    +---------------+     +--------------+     +---------------+
    | Fiend Foundry |     | Totem Tower  |     | Longhouse     |
    | 200S+80I+30C  |     | 120S+60I     |     | 260S+100I     |
    | Veilsteel     |     | 4 garrison   |     | Batch train   |
    | forging       |     | +25% on      |     | 5/10 units    |
    |               |     | bloody ground|     | -5%cost -10%t |
    +---------------+     +--------------+     | +10 pop       |
                                               +---------------+

    +------------------+
    | Siege Yard       |
    | 260S+120I+40C    |
    | Trains:          |
    | Siege Ram        |
    +------------------+


              FERALDIS UNIT ROSTER

    Longhouse -----+----> Berserker (high DPS, unhealable)
                   +----> Warboar Rider (fast cavalry)

    Siege Yard --------> Siege Ram (300 HP battering ram)

    Keep + Miner ------> Berserker (conversion, no cost)

    Keep + Hunters ----> Hunter (close-range axe thrower)
```

### Sect Tech Tree (All Cultures)

```
              TEMPLE OF RIDAN (Levels 1-4)
              +---------------------------+
              |  Level 1: +2 RP (Era 2)  |
              |  Level 2: +3 RP (Era 3)  |
              |  Level 3: +3 RP (Era 4)  |
              |  Level 4: +3 RP (Era 5)  |
              +-------------+-------------+
                            |
              Sect adoption (1 RP affinity / 2-3 RP foreign)
                            |
         +------------------+------------------+
         |                  |                  |
    ALANTHOR           RUNAI              FERALDIS
    AFFINITY           AFFINITY           AFFINITY
    (1 RP each)        (1 RP each)        (1 RP each)
         |                  |                  |
    +----+----+        +----+----+        +----+----+
    |         |        |         |        |         |
 Renewal  Antiquity  Still    Quiet    Ember    Hollow
    |         |      Flame    Vault     Ash     Brand
 Living   Veiled     |         |        |         |
 Stone    Memory   Mirror   Shard   Flamewrought  Unmaker's
                    Rite   Judgment   Chains       Grasp


              SYNERGY PAIRS (Stronger-than-sum bonuses)

    Alanthor:  Renewal + Living Stone  = "The Fortress"
               Antiquity + Veiled Memory = "The Archive"

    Runai:     Still Flame + Quiet Vault = "The Merchant"
               Mirror Rite + Shard Judgment = "The Inquisitor"

    Feraldis:  Ember Ash + Hollow Brand = "The Warband"
               Flamewrought Chains + Unmaker's Grasp = "The Purifier"
```

### Complete Era Progression Diagram

```
    ERA 1                    ERA 2                     ERA 3-5
    Dawn of Ashes            Age of Divergence         Temple Advancement
    +-----------+            +------------------+      +------------------+
    | Universal |  800S+200I | Choose Culture:  | Temple| Unlock advanced |
    | roster    |  +150C     |                  | Level | units, techs,   |
    | 6 units   +----------->+  +-- RUNAI       | 2-4   | and buildings   |
    | 4 buildings|           |  |   Trade/Mobile +------>| per culture     |
    | 5 techs   |           |  +-- ALANTHOR     |      |                 |
    |           |           |  |   Walls/Defense |      | Sect bonuses    |
    | Shrine OR |           |  +-- FERALDIS     |      | scale 1.5x-2.5x|
    | Vault OR  |           |      Raid/Aggro   |      |                 |
    | Keep      |           +------------------+      +------------------+
    +-----------+
                    +2 RP          +3 RP    +3 RP    +3 RP
                    (temple L1)    (L2)     (L3)     (L4)

    TOTAL RP BUDGET: 8-9 RP
    Sect costs: 1 RP (affinity) or 2-3 RP (foreign)
```

### Damage Type Effectiveness Diagram

```
                        TARGETS
                 Light  Heavy  Ranged  Cav   Struct  S.Human
    MELEE    [   1.0    1.0    1.1    0.9    0.2     0.2   ]
    RANGED   [   1.1    0.9    1.0    0.8    0.15    0.15  ]
D   SIEGE    [   0.6    0.8    0.8    0.7    3.0     2.4   ]
A   MAGIC    [   1.1    0.9    1.1    1.0    0.5     0.45  ]
M   TRUE     [   1.0    1.0    1.0    1.0    1.0     1.0   ]
G

    KEY MATCHUPS:
    Siege vs Structure:  3.0x  (siege weapons destroy buildings)
    Ranged vs Structure: 0.15x (archers barely scratch walls)
    Melee vs Cavalry:    0.9x  (slight cavalry resistance)
    Spearman vs Cavalry: 0.9x * 1.5 = 1.35x (anti-cav bonus)
    Magic vs Heavy:      0.9x  (armor resists magic somewhat)
    True vs anything:    1.0x  (ignores all armor)
```

### Resource Flow Diagram

```
    IRON DEPOSITS                    CRYSTAL CADAVERS
    (500 iron each)                  (300 crystal each)
    12-20 per map                    Spawn on creature death
         |                                |
         | Miners (1/2s)                  | Miners (1/1.5s)
         v                                v
    +----------+    +----------+    +----------+
    |   IRON   |    | SUPPLIES |    | CRYSTAL  |
    +----+-----+    +-----+----+    +-----+----+
         |               |               |
         +-------+-------+-------+-------+
                 |               |
           +-----v-----+  +-----v-----+
           |  SMELTER   |  | Buildings |
           | 5I+3C=1V   |  | & Units   |
           | every 5s   |  | (costs)   |
           +-----+------+  +-----------+
                 |
           +-----v------+
           | VEILSTEEL   |
           | (elite      |
           |  units)     |
           +-------------+

    PASSIVE INCOME:
    Hall ----------> 50 Supplies / 15s
    Gatherer Hut --> ~60 Supplies / min (area-based)
    Trade Hub ----> Caravans -> Supplies (distance-scaled)
    Wall Enclosure> Supplies (area-scaled, Alanthor only)
    Vault --------> 3% interest/min on deposits
```
