# Game Design Document — *The Waning Border*

## 1. Game Summary
**Genre:** RTS / Base-Building  
**Core Loop:** Gather → Build → Fight → Expand  
**Player Fantasy:** Lead evolving civilizations across eras.  
**USP:**
- Hybrid ECS RTS with 1k+ units
- Intrinsically air starting conditions (tehre are no factions at game start)
- Faction asymmetry (Runai, Alanthor, Feraldis)
- Survival RTS element (The curse of Ahridan)

---

## 2. Gameplay Overview
**Core Loop:**  
1. Collect resources  
2. Construct buildings  
3. Train and command units  
4. Destroy enemy units

**Era progression loop**
1. Players start with no culture or religion (remnants of a lost empire)
2. Players chose their main culture.
3. Players start a religion 
4. Players upgrade their religion (and their culture) by converting to Petriarchy Sects.
5. Players chose between embracing, exterminating or pacifying the curse (changing the game's economy)

**Survival RTS loop**
1. players are forced to harvest the crystal curse, either for resources or for territory.
2. Interactions with the curse cause it to fight back, expand and evolve.
3. in Late game players harvest the curse for their benefit or exterminate it to eliminate the advantage of other players. 

---

## 3. Mechanics
### Economy
- Resource types: Supplies, Iron, Veilstone, Veilsteel, Glow

#### Supplies 
    Era 1 (factionless)
    - Players build gatherer's huts which generate a supply trickle proportional to unobtructed circular area around them (does not stack). Main building (Hall) also generates a trickle. 

    Era 2 and forward

    - Runai Culture - Players lose the ability to build Gatherer's huts, they can build trade outposts in stead. Trade outposts generate caravans which yield supplies when they reach their destination. Caravans can be upgraded to have defenses, and the end goal is for them to be a moving fortress on the late game.

    - Feraldis culture - Players retain their gatherer's huts, and can upgrade them to hunting lodges or logging camps, which generate supplies based on nearby trees and fauna. They also generate resources by attacking enemy walls, caravans and gatherer's huts. 

    - Alanthor culture - Players lose the ability to build Gatherer's huts, they can in stead build walls. Each walled area (district) can be assigned to generate supplies proportional to area, aid in technology or produce cheap expendable military units.

#### Iron ingots
    - Players can use miners to gather from iron outcroppings, generating iron ingots.

#### Veilstone Crystal
    - The curse is a large crystalline landscape that starts on one random location of the map and spreads outwards. Players can assign miners to work on the veilstone crystal curse, reducing its area and generating Veilstone crystal resource, however, this will trigger the curse to spawn creatures to attack the players and evolve. These creatures also drop crystal (which does not have to be mined) when they die.

#### Veilsteel
    - Players can build veilsteel foundries to convert iron and veilstone into veilsteel weapons. these weapons can be carried by units to make them stronger, but are dropped on the battlefield if they die.

#### Glow
    - Glow can only be attained by defeating colossal Curse units or by fullt upgrading the temple (trickle). Glow can be equiped by units, confering them mytical powers. 

### Combat
- Standard Rock-paper-scisors logic
- Damage model: base, melee, ranged, siege, all can be magic or not (WIP)
- Armor types: base, melee, ranged, siege, all can be magic or not (WIP)
- unit abilities (AOE attacks or effects)

---

## 4. World & Narrative
### Factions
**Runai:** Merchant-scholars. Focus on trade, mobile military units and technology, best veilsteel production and ranged units.  
**Alanthor:** Architect-warriors. Heavy defense and durable units.  
**Feraldis:** Warborn tribes. Aggressive melee, minimal tech, berserkers.

---

## 5. Aesthetics
**Visual Style:** Stylized low-poly painterly textures with strong faction hues.  
**Color Palette:**  
- Runai – gold/azure  arabic-like buildings, mostly canvas tents, long flowing robes as clothing. light-blue magic.
- Alanthor – marble/steel  medieval europe-style buildings, thick stone walls and construnctions. Arcane machine based magic.
- Feraldis – crimson/charcoal  celtic/viking vibes, with dark bloody magic.

---

## 6. Technology
- Unity 2023 LTS  
- Entities 1.2 (ECS)  



Assets/Scripts/
├── Core/                           # Foundation systems
│   ├── Bootstrap/
│   │   ├── GameBootstrap.cs
│   │   ├── EconomyBootstrap.cs
│   │   └── AIBootstrap.cs
│   ├── Commands/
│   │   ├── CommandRouter.cs
│   │   ├── ICommand.cs
│   │   └── CommandTypes/
│   │       ├── MoveCommand.cs
│   │       ├── AttackCommand.cs
│   │       ├── BuildCommand.cs
│   │       ├── GatherCommand.cs
│   │       └── HealCommand.cs
│   ├── Components/                 # SPLIT Components.cs here
│   │   ├── Core/
│   │   │   ├── FactionTag.cs
│   │   │   ├── Health.cs
│   │   │   ├── MoveSpeed.cs
│   │   │   └── ...
│   │   ├── Unit/
│   │   │   ├── UnitTag.cs
│   │   │   ├── MinerTag.cs
│   │   │   ├── ArcherTag.cs
│   │   │   └── ...
│   │   ├── Building/
│   │   │   ├── BuildingTag.cs
│   │   │   ├── TrainingState.cs
│   │   │   └── ...
│   │   ├── Combat/
│   │   │   ├── Target.cs
│   │   │   ├── Damage.cs
│   │   │   ├── AttackCooldown.cs
│   │   │   └── ...
│   │   └── AI/
│   │       ├── AIBrain.cs
│   │       ├── AIEconomyState.cs
│   │       └── ...
│   └── Settings/
│       ├── GameSettings.cs
│       └── FactionColors.cs
│
├── Data/                           # Static data & configuration
│   ├── TechTree/
│   │   ├── TechTreeDB.cs
│   │   └── Definitions/
│   │       ├── UnitDef.cs
│   │       ├── BuildingDef.cs
│   │       └── TechnologyDef.cs
│   └── BuildCosts.cs
│
├── Economy/                        # Resource management
│   ├── FactionEconomy.cs
│   ├── FactionResources.cs
│   ├── FactionPopulation.cs
│   └── ResourceTickSystem.cs
│
├── Entities/                       # Entity factories (rename from Faction/)
│   ├── Units/
│   │   ├── UnitFactory.cs          # Unified factory
│   │   ├── Builder.cs
│   │   ├── Miner.cs
│   │   ├── Swordsman.cs
│   │   └── Archer.cs
│   └── Buildings/
│       ├── BuildingFactory.cs      # Unified factory
│       ├── Hall.cs
│       ├── Barracks.cs
│       └── ...
│
├── Systems/                        # ECS Systems (rename from ECS/)
│   ├── Movement/
│   │   ├── MovementSystem.cs
│   │   └── UnitSeparationSystem.cs
│   ├── Combat/
│   │   ├── TargetingSystem.cs
│   │   ├── MeleeCombatSystem.cs
│   │   ├── RangedCombatSystem.cs
│   │   └── ProjectileSystem.cs
│   ├── Work/
│   │   ├── GatheringSystem.cs
│   │   ├── BuildingConstructionSystem.cs
│   │   └── MiningSystem.cs
│   ├── Training/
│   │   └── TrainingSystem.cs
│   └── Visibility/
│       └── FogOfWarSystem.cs
│
├── AI/                             # AI systems (keep, but organize)
│   ├── Core/
│   │   ├── AIBrain.cs              # Main AI controller
│   │   └── AICommandAdapter.cs
│   ├── Managers/
│   │   ├── AIEconomyManager.cs
│   │   ├── AIBuildingManager.cs
│   │   ├── AITacticalManager.cs
│   │   └── AIMissionManager.cs
│   └── Behaviors/
│       ├── AIScoutingBehavior.cs
│       └── AIDefenseBehavior.cs
│
├── Input/                          # Player input (rename from Inputs/)
│   ├── RTSInputManager.cs          # Core input handling only
│   ├── SelectionSystem.cs          # Split from RTSInput
│   └── CameraController.cs
│
├── Multiplayer/                    # Networking (keep structure)
│   ├── Lockstep/
│   │   ├── LockstepManager.cs
│   │   └── LockstepBootstrap.cs
│   └── Lobby/
│       ├── LobbyManager.cs
│       └── LobbyUI.cs
│
├── UI/                             # All UI (consolidate from Presentation/)
│   ├── HUD/
│   │   ├── ResourceHUD.cs
│   │   ├── MinimapUI.cs
│   │   └── SelectionRings.cs
│   ├── Panels/
│   │   ├── EntityInfoPanel.cs
│   │   ├── EntityActionPanel.cs
│   │   └── BuilderCommandPanel.cs
│   ├── Menus/
│   │   ├── MainMenuUI.cs
│   │   ├── SkirmishLobbyUI.cs
│   │   └── MultiplayerLobbyUI.cs
│   └── Common/
│       ├── UIHelpers.cs
│       └── Styles.cs
│
└── World/                          # World/Map generation
    ├── Terrain/
    │   └── ProceduralTerrain.cs
    ├── FogOfWar/
    │   ├── FogOfWarManager.cs
    │   ├── FogOfWarSystem.cs
    │   └── FogOfWarShader.shader
    └── Minimap/
        └── MinimapRenderer.cs