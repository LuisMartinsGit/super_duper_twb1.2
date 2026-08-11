# The Waning Border 1.2 - Claude Code Instructions

## Project Overview
Unity 6 (6000.0.37f1) RTS game using DOTS/ECS (Entities 1.3.14) with hybrid MonoBehaviour UI.
C# source code lives in `Assets/Scripts/` organized by domain modules.

## Game-design truth source

**[docs/Design/](docs/Design/Overview.md) is the canonical truth source** for
every game-design decision — units, buildings, costs, techs, factions,
ages, Glow rules, religious-unit tier, per-battalion upgrade pattern,
Runai trade lanes, Feraldis raider houses, Alanthor walls, sect framing.

When the **code** and the **Design folder** disagree, the Design folder
wins; the code is being progressively aligned through tasks in
[.deft/tasks/](. deft/tasks/) (notably
[task-age0-techtree-alignment-065](.deft/tasks/task-age0-techtree-alignment-065/task.md)).
Do **not** introduce new mechanics, balance values, or design changes
without updating the Design folder first.

| Doc | Scope |
|-----|-------|
| [docs/Design/Overview.md](docs/Design/Overview.md) | Cross-faction framing — two-age structure, movement axis, age-up transformations, per-battalion upgrades, religious-unit tier, population model, caravan-death rule, Petriarchy. (Glow economy: **superseded** — see Curse_And_Shardroot.md) |
| [docs/Design/Curse_And_Shardroot.md](docs/Design/Curse_And_Shardroot.md) | **The curse & Shardroot loop** (replaces the old Border design AND the Glow economy): N wells, per-culture verbs (destroy/pacify/purify) with 10-min holds + tempo refresh, **well-domination victory** (all N wells yours at once), veilstone-only-from-curse, the Shardroot power artifact (One-Ring model, three Shardbound Heroes, first verb on the host well claims it) |
| [docs/Design/Tech_Tree.md](docs/Design/Tech_Tree.md) | At-a-glance Mermaid charts of every building, unit, and tech across Age 0 and the three cultures |
| [docs/Design/Combat_Pacing.md](docs/Design/Combat_Pacing.md) | Match pacing — the five meta beats, the unit counter table (bonusVsTags truth source), the siege-only wall rule |
| [docs/Design/Age_0.md](docs/Design/Age_0.md) | Pre-culture Age 0 — every building / unit / tech / cost |
| [docs/Design/Age_1_Alanthor.md](docs/Design/Age_1_Alanthor.md) | Alanthor (defense focus) Age 1 tree |
| [docs/Design/Age_1_Runai.md](docs/Design/Age_1_Runai.md) | Runai (economy / movement focus) Age 1 tree |
| [docs/Design/Age_1_Feraldis.md](docs/Design/Age_1_Feraldis.md) | Feraldis (military focus) Age 1 tree |

Player-facing UX (controls, hotkeys, AI personalities, multiplayer) lives
in [GAME_MANUAL.md](GAME_MANUAL.md). Code-level runtime reference (what
the code currently does, often pre-design-pass) lives in
[docs/Technical_Reference.md](docs/Technical_Reference.md).

## Architecture

### ECS (Data-Oriented)
- **Per-entity code is co-located with its data** in `Assets/GameData/TechTree/{Units,Buildings}/<Culture>/<Entity>/`: the factory, the entity's components file, any single-entity systems and visuals, next to its SO/prefab. Kept in the runtime assembly via `Assets/GameData/TechTree/TheWaningBorder.Runtime.asmref`.
- **Set-level code sits one level above**: culture-wide tier components at `.../<Culture>/` (e.g. `Age0UnitComponents.cs`), all-unit/all-building code at `Units/` / `Buildings/` root (e.g. `UnitComponents.cs`, `BuildingUpgradeSystem.cs`, wall-set systems at `Buildings/Alanthor/`).
- **Cross-domain components** (CoreComponents, CombatComponents, etc.) stay in `Scripts/Components/`; **cross-domain systems** (Combat, Navigation, Work, Training, AI, Border) stay in `Scripts/Systems/` by domain.
- **Shared factories**: `UnitFactory.cs` + sect-unit factories in `Entities/Units/`; `BuildingFactory.cs` + Border-node factories in `Entities/Buildings/`.
- **Presentation** (`Scripts/Presentation/`) is organized into `Spawn/`, `Buildings/`, `Units/`, `Border/`, `Vfx/`, `Procedural/`; entity-specific visuals live in that entity's GameData folder.
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
- Mined resources are credited straight to the faction bank on each gather
  tick — there are NO carrying workers and NO dropoff buildings
- Miners: local player miners require explicit GatherCommand; AI miners auto-find
- Miners auto-find new deposits only on depletion and only within LineOfSight range
- Builders auto-chain to nearby unfinished structures within LOS
- Shift+click stays in building placement mode for repeated placement

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
| Mining | `Systems/Work/MiningSystem.cs`, `Systems/Work/VeilstoneMiningSystem.cs` |
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

## Agent Pipeline

This project uses a 4-agent development pipeline. Agent definitions are in `.github/agents/`.

### Quick Reference
| Command | What it does |
|---------|-------------|
| "process task: \<description\>" | Runs the full pipeline (intake → spec → code → review) |
| "create issue for: \<description\>" | Task Intake agent only |
| "write spec for issue #N" | Spec Writer agent only |
| "implement issue #N" | Coder agent only |
| "review PR #N" | Reviewer agent only |

### Pipeline: process task
1. **Task Intake** (`.github/agents/task-intake.md`) - Creates a labeled GitHub issue
2. **Spec Writer** (`.github/agents/spec-writer.md`) - Posts implementation spec as issue comment
3. **Coder** (`.github/agents/coder.md`) - Branches, implements, commits, creates PR
4. **Reviewer** (`.github/agents/reviewer.md`) - Reviews PR, approves or requests fixes
5. Coder ↔ Reviewer cycle (max 3 rounds) until approved

### Setup
The pipeline requires a GitHub PAT in `.env`:
```
GH_TOKEN=ghp_your_token_here
```

### GitHub API Pattern
Since `gh` CLI is not installed, use `curl` for all GitHub operations:
```bash
# Read token
GH_TOKEN=$(grep GH_TOKEN .env | cut -d= -f2)

# API calls
curl -s -H "Authorization: token $GH_TOKEN" \
  "https://api.github.com/repos/LuisMartinsGit/super_duper_twb1.2/..."
```

### Pipeline Checkpoints
The pipeline pauses for user confirmation after:
- Issue creation
- Spec posting
- PR creation
- Review completion
