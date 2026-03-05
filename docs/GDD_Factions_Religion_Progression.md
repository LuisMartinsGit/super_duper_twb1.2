# The Waning Border - Factions, Religion & Progression
## Game Design Document v1.0

---

# 1. Overview

The Waning Border is a real-time strategy game set in the aftermath of a cataclysm that unleashed "the Curse" - a crystalline corruption spreading across the land. Humanity fragments into three cultures, each defined by its philosophical response to the Curse:

- **Runai** - Preserve the Curse, learn from it, pacify it
- **Alanthor** - Exploit the Curse for industrial profit
- **Feraldis** - Destroy the Curse through blood and fire

Players begin as a single, unified human civilization (Era 1) and branch into one of three cultures upon advancing to Era 2. Progression continues through a religion system of 12 Sects, unlocked via a Temple building and Religion Points.

---

# 2. Era Progression

## 2.1 Era Structure

| Era | Name | Theme |
|-----|------|-------|
| 1 | **Dawn of Ashes** | Unified humanity. Survival basics. No culture identity. |
| 2 | **Age of Divergence** | Choose a culture. Basic culture identity established. |
| 3 | **Age of Doctrine** | Advanced culture. Temple Level 2 unlocks deeper sect investment. |
| 4 | **Age of Dominion** | Peak culture power. Temple Level 3. Late-game economies. |
| 5 | **Age of Reckoning** | Endgame. Temple Level 4. Mythical abilities. Glow resources. |

## 2.2 Age-Up Requirements

| Transition | Cost | Building Prerequisite |
|------------|------|-----------------------|
| Era 1 -> 2 | 800 Supplies, 200 Iron, 150 Crystal | One of: Temple of Ridan, Warrior's Hall, or Vault of Almierra |
| Era 2 -> 3 | Temple Level 2 research cost | Temple Level 2 |
| Era 3 -> 4 | Temple Level 3 research cost | Temple Level 3 |
| Era 4 -> 5 | Temple Level 4 research cost | Temple Level 4 |

## 2.3 Era 1 - Unified Humanity

All players share the same roster:

**Buildings:** Hall, Hut, Gatherer's Hut, Barracks, Temple of Ridan, Warrior's Hall, Vault of Almierra

**Units:** Builder, Miner, Scout, Swordsman, Archer, Litharch (from Temple)

**Technologies:** Advance to Era II, Improved Tools, Storage Carts, Basic Drills, Wooden Armor

The choice of which Era 1 special building to construct (Temple / Warrior's Hall / Vault) does not lock you into a culture - you choose your culture when you research "Advance to Era II" at the Hall.

---

# 3. The Three Cultures

## 3.1 Runai - The Veil Scholars

> *"The Curse is not our enemy - it is our teacher. To sever it is to sever ourselves from understanding."*

**Philosophy:** Preserve the Curse, learn from it, pacify it. The Runai believe the crystalline corruption contains ancient knowledge and that understanding it is the path to coexistence. They study Curse formations, trade in Crystal freely, and develop technologies to calm hostile Curse entities rather than destroy them.

**Playstyle:** Trade / Guerrilla / Tech

**Aesthetic:** Arabic-influenced tents, sandstone, flowing robes, cyan-blue magic

**Color Palette:** Cyan primary (0.25, 0.75, 0.80) + Sandstone secondary (0.76, 0.65, 0.45)

### 3.1.1 Basic Age (Era 2)

**Culture HQ:** Thessara's Bazaar
- Mobile HQ (can pack and move)
- Trains: Spearman, Skirmisher, Raider
- 2 training queues, provides 40 population
- Tariff Boost Aura for nearby trade routes

**Economy:** Trade routes replace Gatherer's Huts (auto-despawn after 2 minutes)
- Build Outposts to anchor trade routes
- Trade Hubs auto-spawn caravans on valid routes
- Income scales with route distance and tariff auras
- Caravans can be raided by enemies (risk/reward)

**Buildings:**
- Runai Outpost (trade node / vision)
- Runai Trade Hub (caravan spawner, housing per caravan)
- Runai Vault (banking, 3% interest per minute)
- Runai Veilsteel Foundry (crafts Veilsteel from Iron + Crystal)
- Runai Siege Workshop (trains Sand Ballista)

**Units:**
- Runai Spearman (melee infantry, 130 HP)
- Runai Skirmisher (ranged, fast, 95 HP)
- Runai Raider (cavalry, 150 HP, fast harassment)
- Runai Caravan (civilian trade unit, 120 HP, uncontrollable)
- Runai Escort (auto-spawned caravan guard)
- Runai Sand Ballista (siege, 200 HP)

**Technologies:**
- Long-Haul Tariffs (+15% Supplies from routes; +25% if route > 60 units)
- Pack Bazaar (faster pack/unpack, +200 HP while packed)
- Escorted Caravans (2 escorts per caravan, +2 pop per active caravan)

### 3.1.2 Advanced Age (Era 3+)

**New Buildings:**
- Runai Observatory (vision upgrade, reveals Curse spread patterns, reduces Crystal mining retaliation chance by 15%)
- Runai Caravan Fortress (upgrade: Trade Hub becomes a mobile armed caravan base at Era 4)

**New Units:**
- Veilwalker (magic ranged unit, 110 HP, 14 range, can briefly pacify Curse creatures for 8 seconds, trained at Observatory)
- Dune Warden (heavy cavalry, 200 HP, bonus damage to siege units, Era 4)

**New Technologies:**
- Crystal Attunement (Miners near Runai buildings gather Crystal 20% faster with 25% less retaliation chance)
- Veil Linguistics (Pacified Curse creatures drop 50% more loot)
- Mobile Fortress Protocol (Era 4: Trade Hubs can relocate; packed form gains ranged attack)

---

## 3.2 Alanthor - The Iron Covenant

> *"The Curse is ore. The Curse is fuel. What bleeds from the earth, we forge into empire."*

**Philosophy:** Exploit the Curse for profit. The Alanthor see Crystal as just another resource to be extracted, refined, and sold. They build fortified districts, process Crystal industrially, and view the Curse's expansion as a business opportunity. They neither fear it nor respect it - they monetize it.

**Playstyle:** Economy / Defense

**Aesthetic:** Medieval European stone, thick walls, forges, arcane machinery

**Color Palette:** Sage Green primary (0.55, 0.65, 0.50) + Warm Grey secondary (0.45, 0.45, 0.42)

### 3.2.1 Basic Age (Era 2)

**Culture HQ:** King's Court
- Generates Supplies, global building techs
- +10% building HP aura, +15% repair rate aura
- Provides 10 population

**Economy:** Walled compartments (districts) replace Gatherer's Huts
- Build walls to enclose areas; enclosed area = income
- Larger compartments = more Supplies
- If a wall segment falls, that compartment's income pauses until repaired
- Can assign districts to boost tech speed or produce expendable militia

**Buildings:**
- Alanthor Wall (compartment boundaries, 900 HP)
- Alanthor Watch Tower (defensive, garrison 4, long range)
- Alanthor Garrison (trains Sentinel + Crossbowman, 8 pop, garrison 6)
- Royal Stable (trains Cataphract heavy cavalry)
- Siege Yard (trains Ballista)
- Smelter (iron processing)
- Crucible (Veilsteel forging)

**Units:**
- Alanthor Sentinel (heavy melee, 160 HP, high defense)
- Alanthor Crossbowman (ranged, 100 HP, slow but armored)
- Alanthor Cataphract (heavy cavalry, 180 HP)
- Alanthor Ballista (siege, 220 HP, longest range)

**Technologies:**
- Stone Ledgers (compartments yield +8 Supplies per 10 sq. units area per minute)
- Mason's Guild (+15% building HP, +20% repair rate)

### 3.2.2 Advanced Age (Era 3+)

**New Buildings:**
- Crystal Refinery (processes raw Crystal into refined Crystal at 2:1 ratio; refined Crystal sells for double on trade, Era 3)
- Ironclad Foundry (Veilsteel weapons grant units +1 melee defense passively, Era 3)
- Mint (generates passive Supplies income proportional to total Crystal stockpile, Era 4)

**New Units:**
- Ironclad Knight (upgraded Cataphract, 240 HP, Veilsteel armor gives +2 all defense, Era 3)
- Siege Tower (mobile garrison, carries 8 units to walls, Era 4)

**New Technologies:**
- Industrial Extraction (Crystal mines within compartments yield +30% more Crystal)
- Profit Margins (Crystal sold via trade is worth 40% more Supplies)
- Fortified Districts (Compartments with 4+ wall segments gain +2 armor on all segments)

---

## 3.3 Feraldis - The Ashborn

> *"Every crystal shard is a wound in the world. We will cauterize them all."*

**Philosophy:** Destroy the Curse utterly. The Feraldis believe the Curse is an abomination that must be burned from the earth. They attack Crystal nodes aggressively, use bloody war magic to deny Curse spread, and view anyone who tolerates the Curse as complicit in humanity's destruction.

**Playstyle:** Aggression / Magic Denial

**Aesthetic:** Celtic/Viking, dark wood, totems, blood-red war paint, fire magic

**Color Palette:** Crimson Red primary (0.70, 0.18, 0.15) + Dark Grey secondary (0.28, 0.26, 0.24)

### 3.3.1 Basic Age (Era 2)

**Culture HQ:** Fiendstone Keep
- Fast training aura (+25% train speed)
- 2000 HP fortress with ranged attack (Range 25, Damage 20, 3 targets)
- Provides 20 population

**Economy:** Gatherer's Huts persist (unique to Feraldis), upgraded to specialized camps
- Hunting Lodges (bonus near wildlife)
- Logging Stations (bonus near trees)
- Gain Supplies by attacking non-military enemy units (pillage mechanic)

**Buildings:**
- Hunting Lodge (upgraded Gatherer's Hut, bonus near wildlife)
- Logging Station (upgraded Gatherer's Hut, bonus near trees)
- Fiend Foundry (Veilsteel forging and weapons)
- Totem Tower (defensive tower, empowered on bloody ground)
- Longhouse (batch-trains Berserkers/Warboar Riders at discount)
- Siege Yard (trains Siege Ram)

**Units:**
- Feraldis Berserker (melee, 150 HP, fast, aggressive)
- Feraldis Hunter (ranged, 100 HP, mobile)
- Feraldis Warboar Rider (cavalry, 160 HP)
- Feraldis Siege Ram (siege, 300 HP, melee range)

**Technologies:**
- Pillage (killing non-military units grants +15 Supplies and +1 Iron)
- Iron Fury (units carry up to 5 Iron, each grants +2% attack)

### 3.3.2 Advanced Age (Era 3+)

**New Buildings:**
- Pyre Altar (area denial: slowly burns away nearby Curse ground, prevents Curse spread in radius, Era 3)
- Blood Forge (units killed near this building grant attacker Iron + Crystal, Era 3)
- War Pyre (massive AOE fire that damages all Curse entities in range, Era 4)

**New Units:**
- Ashwalker (anti-magic melee, 180 HP, +40% damage to Curse entities, immune to Curse debuffs, Era 3)
- Firestorm Trebuchet (long-range siege, fires burning projectiles that leave denial zones, Era 4)

**New Technologies:**
- Scorched Earth (Curse ground within 8 units of a Pyre Altar is cleansed over 30 seconds)
- War Trophies (killing Curse creatures grants a stacking faction-wide attack buff, +1% per kill, max 20%)
- Annihilation Doctrine (Era 4: Crystal nodes destroyed by Feraldis units drop 50% more resources and cannot respawn for 3 minutes)

---

# 4. Religion System - The Twelve Sects

## 4.1 Core Mechanic

The religion system allows deep customization through 12 Sects. Each sect unlocks exactly **5 things**:

1. **One unique unit**
2. **One technology**
3. **One unique building** (chapel module attached to Temple)
4. **One passive magic ability** (always-on effect)
5. **One active magic ability** (cooldown-based spell)

## 4.2 Religion Points

Religion Points (RP) are the currency for adopting sects.

### 4.2.1 Point Income

| Event | Points Gained |
|-------|---------------|
| Adopt your culture (transition to Era 2) and build/upgrade Temple | **2 RP** |
| Level up Temple to Level 2 (Era 3) | **3 RP** |
| Level up Temple to Level 3 (Era 4) | **3 RP** |
| Build a Shrine building (if chosen in Era 1) | **+1 bonus RP** (one-time) |

**Maximum possible RP:** 8 (without Shrine) or 9 (with Shrine)

### 4.2.2 Point Cost

| Condition | Cost to Adopt Sect |
|-----------|--------------------|
| Sect has **affinity** with your culture | **1 RP** |
| Sect has **no affinity** with your culture | **3 RP** |

This creates meaningful choices:
- With affinity sects (1 RP each), you can adopt up to 8-9 sects across the game
- Cross-culture sects (3 RP each) are expensive - you sacrifice breadth for a specific counter-strategy
- Mixing 1-cost and 3-cost sects forces prioritization

### 4.2.3 The Shrine Bonus

If you chose to build the **Temple of Ridan** (Shrine) as your Era 1 special building, you receive **+1 bonus RP** as soon as you construct it. This makes the Temple path the "religion-focused" opening, sacrificing the military benefits of Warrior's Hall or the economic benefits of Vault of Almierra for an extra sect adoption.

## 4.3 Temple Leveling and Sect Power Scaling

**Critical design tension:** *"Do I adopt a sect now to get it stronger, or do I hide my strategy until the last moment?"*

Each time the Temple levels up, **all previously adopted sects receive a power boost:**

| Temple Level | Sect Power Multiplier | Passive Effect | Active Spell |
|--------------|----------------------|----------------|--------------|
| Level 1 (Era 2) | 1.0x (base) | Base values | Base cooldown |
| Level 2 (Era 3) | 1.5x | +50% passive values | -15% cooldown |
| Level 3 (Era 4) | 2.0x | +100% passive values | -30% cooldown, +25% effect |
| Level 4 (Era 5) | 2.5x | +150% passive values | -40% cooldown, +50% effect |

**Example - Sect of Ember Ash:**
- At Level 1: +12% melee damage, +10% train speed
- At Level 2: +18% melee damage, +15% train speed
- At Level 3: +24% melee damage, +20% train speed
- At Level 4: +30% melee damage, +25% train speed

**The Dilemma:** A sect adopted at Temple Level 1 and held through Level 4 has been at 2.5x power for the entire late game. But your opponent saw it coming at Era 2. A sect adopted at Level 3 starts at 2.0x power immediately but gives your opponent no warning. The tradeoff is: early investment = stronger cumulative value vs. late investment = strategic surprise.

## 4.4 Culture Affinities

Each culture has affinity with **4 of the 12 sects**. The remaining 8 sects are available but cost 3x the points.

| Sect | Affinity | Theme |
|------|----------|-------|
| **Renewal** | Alanthor | Healing, wall integrity, self-repair |
| **Antiquity** | Alanthor | Research speed, arcane golems, Crystal surveying |
| **Living Stone** | Alanthor | Wall income, building speed, fortification |
| **Veiled Memory** | Alanthor | Vision, spell cooldown, fog manipulation |
| **Still Flame** | Runai | Trade income, tariff bonuses, route protection |
| **Quiet Vault** | Runai | Banking interest, deposit protection, economic defense |
| **Mirror Rite** | Runai | Magic accuracy, spell reflection, arcane precision |
| **Shard Judgment** | Runai | Law enforcement, trade area denial, resource theft |
| **Ember Ash** | Feraldis | Melee damage, training speed, aggressive buffs |
| **Hollow Brand** | Feraldis | Panic, siege bonus, morale disruption |
| **Flamewrought Chains** | Feraldis | Crowd control, Veilsteel synergy, Curse binding |
| **Unmaker's Grasp** | Feraldis | Anti-Curse damage, Crystal yield bonus, Curse destruction |

## 4.5 Sect Synergy Pairs

Sects are designed with **paired synergies** - two sects that complement each other mechanically. Adopting both sects in a pair creates a stronger-than-sum effect.

| Pair | Sects | Synergy Description |
|------|-------|---------------------|
| **The Fortress** | Renewal + Living Stone | Self-repair + fortification. Walls become nearly indestructible. Renewal heals what Living Stone armors. |
| **The Archive** | Antiquity + Veiled Memory | Research speed + spell cooldown. Golem Autarks benefit from Veiled Memory's vision. Crystal Survey + Shroud = map control. |
| **The Merchant** | Still Flame + Quiet Vault | Trade income + banking interest. Compound economic snowball. Protected routes feed protected vaults. |
| **The Inquisitor** | Mirror Rite + Shard Judgment | Magic precision + law enforcement. Glassmark Arcanists benefit from Judicator frontline. Double debuff on enemy buildings near trade. |
| **The Warband** | Ember Ash + Hollow Brand | Raw damage + panic/morale. Ashblades hit harder while Brandbreakers terrorize. Battle Fervor + Profane Rally = devastating push combo. |
| **The Purifier** | Flamewrought Chains + Unmaker's Grasp | Curse control + Curse destruction. Bind crystal nodes, then annihilate them. Nullblades clear what Chaincasters lock down. |

### Cross-Culture Synergy Examples

These pairs can be broken across culture lines at 3 RP per foreign sect:

- **Alanthor + Ember Ash:** Fortified districts with aggressive garrison units. Wall income funds constant Ashblade production.
- **Runai + Unmaker's Grasp:** Trade in Crystal you aggressively harvest. Veil Linguistics + Erasure Rites = maximum Crystal value.
- **Feraldis + Quiet Vault:** Banking pillage income. Hide stolen resources in protected vaults.

---

# 5. Complete Sect Reference

## 5.1 Alanthor-Affinity Sects

### Sect of Renewal
**Passive:** Immaculate Economy - +20% income if all walls have full health
**Unit:** Scar Guard (heavy melee, 170 HP, self-heal ability, passive out-of-combat heal aura)
**Tech:** Dietary Mandate (all units gain tiny out-of-combat regeneration)
**Building:** Chapel of Renewal (small chapel module on Temple)
**Active Spell:** Repair Levies (CD 40s) - Rapidly repair all buildings in a small area

### Sect of Antiquity
**Passive:** +20% building tech research speed
**Unit:** Golem Autark (magic construct, 320 HP, slow, high defense, 10 range magic attack)
**Tech:** Clockwork Archives (-15% research time, -5% spell cooldowns)
**Building:** Chapel of Antiquity (small chapel module on Temple)
**Active Spell:** Crystal Survey (CD 50s) - Reveal Crystal nodes in large area; +30 Crystal if first reveal

### Sect of Living Stone
**Passive:** +20% wall income, +10% building construction speed
**Unit:** Stone Warden (heavy melee tank, 200 HP, very high defense, slow)
**Tech:** Terrace Planning (+20% Supplies from compartment size)
**Building:** Chapel of Living Stone (small chapel module on Temple)
**Active Spell:** Bulwark Rise (CD 60s) - +3 armor to all buildings in target compartment, 20s duration

### Sect of Veiled Memory
**Passive:** +15% fog vision range, -10% spell cooldowns
**Unit:** Archivist Adept (magic support, 110 HP, 14 range, can Dispel ally/enemy buffs)
**Tech:** Hidden Records (first Crystal Node mined: -25% retaliation chance)
**Building:** Chapel of Veiled Memory (small chapel module on Temple)
**Active Spell:** Shroud (CD 55s) - Create 12-unit mist: blocks enemy vision, slows by 20%

---

## 5.2 Runai-Affinity Sects

### Sect of Still Flame
**Passive:** +15% trade income, +25% tariff bonus
**Unit:** Flame Warden (melee, 150 HP, can root enemy caravans/raiders for 2s)
**Tech:** Sanctified Routes (trade routes grant +5 armor to nearby caravans)
**Building:** Chapel of Still Flame (small chapel module on Temple)
**Active Spell:** Embargo (CD 60s) - Disable enemy trade on a route for 20s; siphon 30 Supplies

### Sect of Quiet Vault
**Passive:** +30% banking interest rate
**Unit:** Vault Keeper (defensive melee, 140 HP, damage reduction aura ability)
**Tech:** Hidden Ledgers (on depot destroyed, retain 50% of Crystal/Iron)
**Building:** Chapel of Quiet Vault (small chapel module on Temple)
**Active Spell:** Lockdown Vault (CD 70s) - Target storage becomes unraidable for 15s; slows nearby enemies

### Sect of Mirror Rite
**Passive:** +10% ranged accuracy, -5% spell cooldowns
**Unit:** Glassmark Arcanist (magic ranged, 100 HP, 15 range)
**Tech:** Refined Silver Inlays (+10% magic attack, -10% spell cooldown)
**Building:** Chapel of Mirror Rite (small chapel module on Temple)
**Active Spell:** Reflective Ward (CD 60s) - Reflect 25% spell damage and 10% physical as true damage for 10s

### Sect of Shard Judgment
**Passive:** +10% law enforcement bonus, enemy buildings near trade build -20% slower
**Unit:** Judicator (heavy melee, 160 HP, high melee defense)
**Tech:** Iron Decrees (enemy buildings near your trade nodes build -20% slower)
**Building:** Chapel of Shard Judgment (small chapel module on Temple)
**Active Spell:** Edict of Seizure (CD 65s) - Drain 50 Supplies from enemy storehouse over 10s

---

## 5.3 Feraldis-Affinity Sects

### Sect of Ember Ash
**Passive:** +12% melee damage, +10% training speed
**Unit:** Ashblade (aggressive melee, 155 HP, fast)
**Tech:** War Tithe (each enemy civilian killed refunds 5 Supplies)
**Building:** Chapel of Ember Ash (small chapel module on Temple)
**Active Spell:** Battle Fervor (CD 55s) - +25% attack and speed to army in 10-unit radius for 10s

### Sect of Hollow Brand
**Passive:** 5% chance to cause panic on hit
**Unit:** Brandbreaker (anti-structure melee, 150 HP, +4 bonus siege damage)
**Tech:** Desecrate Standards (enemy morale auras -20% effectiveness near your units)
**Building:** Chapel of Hollow Brand (small chapel module on Temple)
**Active Spell:** Profane Rally (CD 60s) - Pull nearby enemies 2 units closer and slow them 30% for 5s

### Sect of Flamewrought Chains
**Passive:** 3% chance to briefly control enemy unit on hit
**Unit:** Chaincaster (magic support, 105 HP, 14 range, ChainBind root ability)
**Tech:** Veilsteel Links (Veilsteel carry bonus also grants +1% damage reduction per ingot)
**Building:** Chapel of Flamewrought Chains (small chapel module on Temple)
**Active Spell:** Bind the Core (CD 90s) - Temporarily pacify a Crystal sub-node for 15s

### Sect of Unmaker's Grasp
**Passive:** +20% bonus damage against Crystal entities
**Unit:** Nullblade (anti-magic melee, 150 HP, +6 bonus magic damage, +2 magic defense)
**Tech:** Erasure Rites (Crystal drops yield +20% more resources)
**Building:** Chapel of Unmaker's Grasp (small chapel module on Temple)
**Active Spell:** Unravel (CD 80s) - Deal heavy true damage to a Crystal unit; minor damage to nearby sub-nodes

---

# 6. Progression Flowchart

```
ERA 1 - DAWN OF ASHES
  |
  |-- Build: Hall, Barracks, Gatherer's Hut, Hut
  |-- Optional: Temple of Ridan (+1 bonus RP later)
  |-- Optional: Warrior's Hall (military boost)
  |-- Optional: Vault of Almierra (banking)
  |
  v
CHOOSE CULTURE + Research "Advance to Era II"
  |
  +-- RUNAI (Trade/Guerrilla/Tech)
  |     |-- Build Temple -> gain 2 RP
  |     |-- (If Shrine built in Era 1: +1 bonus RP)
  |     |-- Adopt affinity sects (1 RP each): Still Flame, Quiet Vault, Mirror Rite, Shard Judgment
  |     |-- Or cross-culture sects (3 RP each)
  |     |
  |     v
  |   ERA 3 - Temple Level 2 -> +3 RP, all adopted sects scale to 1.5x
  |     |
  |     v
  |   ERA 4 - Temple Level 3 -> +3 RP, all adopted sects scale to 2.0x
  |
  +-- ALANTHOR (Economy/Defense)
  |     |-- Build Temple -> gain 2 RP
  |     |-- Adopt affinity sects (1 RP each): Renewal, Antiquity, Living Stone, Veiled Memory
  |     |-- ...same progression
  |
  +-- FERALDIS (Aggression/Magic Denial)
        |-- Build Temple -> gain 2 RP
        |-- Adopt affinity sects (1 RP each): Ember Ash, Hollow Brand, Flamewrought Chains, Unmaker's Grasp
        |-- ...same progression
```

---

# 7. Strategic Decision Matrix

## 7.1 Culture vs. Curse Philosophy

| | Runai | Alanthor | Feraldis |
|---|---|---|---|
| **Curse Stance** | Preserve & pacify | Exploit for profit | Destroy utterly |
| **Crystal Mining** | Careful, reduced retaliation | Industrial, high-volume | Aggressive, bonus yields |
| **Curse Spread** | Tolerated, studied | Contained by walls | Actively burned away |
| **Anti-Curse Units** | Veilwalker (pacify) | None (economic response) | Ashwalker, Nullblade |
| **Curse Interaction** | Crystal Attunement, Veil Linguistics | Industrial Extraction, Profit Margins | Scorched Earth, War Trophies |

## 7.2 Sect Investment Timing

| Strategy | When to Adopt | Risk | Reward |
|----------|---------------|------|--------|
| Rush 2 sects at Era 2 | Immediately (2 RP) | Opponent knows your build | Sects scale through 3 temple levels |
| Save for Era 3 burst | Temple Level 2 (5 RP total) | No sect bonuses in Era 2 | Can adopt 5 affinity sects at once |
| Cross-culture splash | Any era | 3 RP per sect, limits total sects | Access to powerful counters |
| All-in synergy pair | Era 2 + Era 3 | Predictable if opponent scouts | Devastating combo at full power |

## 7.3 Point Budget Scenarios

**Scenario A: Pure Affinity (with Shrine)**
- Era 1: Build Shrine (+1 bonus RP when built)
- Era 2: +2 RP -> Adopt 3 affinity sects (3 RP)
- Era 3: +3 RP -> Adopt 3 more affinity sects (3 RP)
- Era 4: +3 RP -> Adopt remaining 1 affinity sect (1 RP), save 2 RP
- **Result:** All 4 affinity sects + 2 RP saved for future or 1 cross-culture splash

**Scenario B: Cross-Culture Counter (with Shrine)**
- Era 2: +2 RP + 1 shrine = 3 RP -> Adopt 1 cross-culture sect (3 RP)
- Era 3: +3 RP -> Adopt 3 affinity sects (3 RP)
- Era 4: +3 RP -> Adopt 3 more affinity sects (3 RP)
- **Result:** 1 cross-culture counter + 3 affinity sects total (but strong foreign counter from Era 2)

**Scenario C: Greedy Econ (no Shrine)**
- Era 1: Build Vault of Almierra (banking income, no shrine bonus)
- Era 2: +2 RP -> Adopt 2 affinity sects (2 RP)
- Era 3: +3 RP -> Adopt 3 affinity sects (3 RP)
- Era 4: +3 RP -> Adopt 2 affinity sects (2 RP), save 1 RP
- **Result:** 4 affinity sects total, strong economy but one fewer RP than Shrine path

---

# 8. Resources

| Resource | Source | Primary Use |
|----------|--------|-------------|
| **Supplies** | Gatherer's Huts / Trade / Walls / Pillage | Everything (universal currency) |
| **Iron** | Mined from outcroppings | Weapons, armor, buildings |
| **Crystal** | Mined from Curse nodes (risky) | Advanced tech, Veilsteel input, sect costs |
| **Veilsteel** | Crafted (Iron + Crystal) at Foundries | Elite units, powerful upgrades |
| **Glow** | Defeating colossal Curse entities / Temple grants | Mythical unit empowerment (+50-100% HP and attack per stack) |

---

# 9. Combat Profile

## 9.1 Damage Types
Melee, Ranged, Siege, Magic, True

## 9.2 Armor Types
Infantry Light, Infantry Heavy, Ranged, Cavalry, Structure, Structure (Human)

## 9.3 Key Interactions

| | Light Inf | Heavy Inf | Ranged | Cavalry | Structure |
|---|---|---|---|---|---|
| **Melee** | 1.0x | 1.0x | 1.1x | 0.9x | 0.2x |
| **Ranged** | 1.1x | 0.9x | 1.0x | 0.8x | 0.15x |
| **Siege** | 0.6x | 0.8x | 0.8x | 0.7x | 3.0x |
| **Magic** | 1.1x | 0.9x | 1.1x | 1.0x | 0.5x |
| **True** | 1.0x | 1.0x | 1.0x | 1.0x | 1.0x |

**Defense Formula:** `finalDamage = baseDamage * modifier * (1 - defense / (defense + 100))`

---

# 10. Design Principles

1. **No dominant strategy.** Each culture should have matchups where it excels and where it struggles.
2. **Curse as catalyst.** The Curse is not just flavor - it is the central strategic axis. How you interact with Crystal defines your economy, your military timing, and your religion path.
3. **Readable scouting.** An observant opponent should be able to deduce your sect choices from your building layout and unit composition, creating counterplay opportunities.
4. **Scaling tension.** The Temple leveling mechanic creates a genuine dilemma between early commitment (more power over time) and late flexibility (strategic surprise).
5. **Cross-culture tax.** The 3x cost for foreign sects ensures culture identity remains strong while allowing creative counter-builds.

---

*Document version: 1.0*
*Game version: The Waning Border 1.2*
*Last updated: 2026-03-05*
