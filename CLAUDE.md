# The Waning Border 1.2 - Claude Code Instructions

## Project Overview
Unity 2023 LTS RTS game using DOTS/ECS (Entities 1.3.14) with hybrid MonoBehaviour UI.
C# source code lives in `Assets/Scripts/` organized by domain modules.

## Architecture

### ECS (Data-Oriented)
- **Components** are in global namespace, defined in `Core/Components/` (CoreComponents.cs, UnitComponents.cs, BuildingComponents.cs, etc.)
- **Systems** live in `Systems/` organized by domain (Movement, Combat, Work, Training, Visibility)
- **Entity factories** are in `Entities/Units/` and `Entities/Buildings/`
- All player commands route through `Core/Commands/CommandRouter.cs`

### Managed (MonoBehaviour)
- UI uses IMGUI panels in `UI/Panels/` and `UI/HUD/`
- Input handling in `Input/RTSInputManager.cs` and `Input/SelectionSystem.cs`
- Camera in `Input/CameraController.cs`

### Namespaces
- `TheWaningBorder.AI` - AI brain, managers, behaviors
- `TheWaningBorder.Economy` - FactionResources, FactionEconomy, SuppliesIncome
- Global namespace - ECS components (CoreComponents, UnitComponents, etc.)

## Naming Conventions
- ECS marker components: `XxxTag` (e.g., `HallTag`, `MinerTag`)
- ECS stateful components: `XxxState` (e.g., `MiningState`)
- Commands: `XxxCommand` (ECS component) + `XxxCommandHelper` (static helper)
- Building tags: `HallTag`, `BarracksTag`, `GathererHutTag`, `HutTag`
- Factions: enum `Faction` (Blue=0 .. White=7)
- Cultures: `Cultures.None / Runai / Alanthor / Feraldis`

## Key Design Decisions (Do Not Change)
- Player color does NOT change on culture selection
- Miners: local player miners require explicit GatherCommand; AI miners auto-find
- Miners auto-find new deposits only on depletion and only within LineOfSight range
- Builders auto-chain to nearby unfinished structures within LOS
- Shift+click stays in building placement mode for repeated placement
- Right-click on Hall/GathererHut with miners selected -> force return/deposit

## Development Workflow

### Branch Strategy
- `main` - stable, reviewed code only
- `develop` - integration branch for features
- `feature/<name>` - new features (branch from develop)
- `fix/<name>` - bug fixes (branch from develop)
- `refactor/<name>` - code restructuring (branch from develop)

### Commit Message Format
```
<type>(<scope>): <short description>

<optional body>
```
Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`
Scopes: `ai`, `combat`, `economy`, `ui`, `input`, `movement`, `building`, `mining`, `multiplayer`, `world`, `core`

### Before Committing
1. Ensure no compile errors (check for missing references, namespace issues)
2. Verify ECS component changes don't break SystemBase queries
3. Check that new components are registered in appropriate bootstrap files
4. Test that UI panels still render correctly after changes

## File Map (Key Files)
| Domain | Key Files |
|--------|-----------|
| Commands | `Core/Commands/CommandRouter.cs` |
| Economy | `Economy/FactionEconomy.cs`, `Economy/FactionResources.cs` |
| Mining | `Systems/Work/MiningSystem.cs`, `Systems/Work/CrystalMiningSystem.cs` |
| Construction | `Systems/Work/BuildingConstructionSystem.cs` |
| Combat | `Systems/Combat/TargetingSystem.cs`, `Systems/Combat/MeleeCombatSystem.cs` |
| AI | `AI/Core/AIBrain.cs`, `AI/Managers/AIEconomyManager.cs` |
| Input | `Input/RTSInputManager.cs`, `Input/SelectionSystem.cs` |
| UI | `UI/Panels/EntityInfoPanel.cs`, `UI/Panels/BuildCommandPannel.cs` |
| Training | `Systems/Training/TrainingSystem.cs` |
| Factions | `Core/Settings/FactionColors.cs`, `Core/Settings/CultureConfig.cs` |

## What NOT to Modify
- Do not rename `BuildCommandPannel.cs` (known misspelling, kept for reference stability)
- Do not change the global namespace of ECS components without updating all systems
- Do not modify `CommandRouter.cs` routing logic without reviewing all command types
- Do not add Unity packages without consulting the developer
