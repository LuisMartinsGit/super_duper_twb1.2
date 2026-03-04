# The Waning Border - Refactored Project

## What's Included

### Split Components (from Components.cs)
- `Core/Components/Core/IdentityComponents.cs` - Faction, Health, MoveSpeed, etc.
- `Core/Components/Unit/UnitComponents.cs` - UnitTag, BuilderTag, capabilities
- `Core/Components/Building/BuildingComponents.cs` - BuildingTag, TrainingState
- `Core/Components/Combat/CombatComponents.cs` - Target, Damage, ArcherState
- `Core/Components/Movement/MovementComponents.cs` - DesiredDestination
- `Core/Components/Economy/EconomyComponents.cs` - FactionResources
- `Core/Components/AI/AIComponents.cs` - AIBrain, missions

### Unified Factories
- `Entities/Units/UnitFactory.cs` - Single entry for ALL unit creation
- `Entities/Buildings/BuildingFactory.cs` - Single entry for ALL building creation

### Command Routing
- `Core/Commands/CommandRouter.cs` - Unified commands for player/AI/network
- `Core/Commands/CommandComponents.cs` - All command types

### Core Systems
- `Core/Settings/GameSettings.cs` - Global settings
- `Core/Settings/FactionColors.cs` - Faction colors
- `Core/Bootstrap/GameBootstrap.cs` - Game initialization
- `Data/TechTree/TechTreeDB.cs` - Data definitions
- `Economy/FactionEconomy.cs` - Resource management

### Assembly Definitions (9 total)
For faster incremental compilation.

## Migration Steps

1. Copy `Scripts/` folder to your `Assets/Scripts/`
2. Add using statements to existing files:
```csharp
using TheWaningBorder.Core;
using TheWaningBorder.Core.Components;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
```

3. Replace entity creation:
```csharp
// Before
var unit = em.CreateEntity(...);
em.SetComponentData(unit, new Health { ... });

// After
var unit = UnitFactory.Create(em, "Swordsman", position, faction);
```

4. Replace command manipulation:
```csharp
// Before
em.SetComponentData(entity, new DesiredDestination { Position = dest, Has = 1 });

// After
CommandRouter.IssueMove(entity, destination);
```

5. Delete old files once everything compiles
