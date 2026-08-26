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
| [docs/Design/Alpha_Build.md](docs/Design/Alpha_Build.md) | **The alpha test build** — which menu entries the shipped player hides (Campaign / Multiplayer / Scenarios / Load Game) and what every match records into the `logs` folder beside the exe |
| [docs/Design/Lobby_Setup.md](docs/Design/Lobby_Setup.md) | **Lobby colour + start-position picking** — 12-swatch colour picker (taken colours locked out), click-a-row-then-click-the-map start assignment, and the `StartIndex` contract binding `MapInfo.PlayerStarts` to `MapMarkerRegistry` (re-bake required) |
| [docs/Design/Teams.md](docs/Design/Teams.md) | **Teams** — lobby team assignment (or no team), shared line of sight, allies cannot damage each other, allied heals/buffs apply but never stack, last-team-standing victory. `Alliances.AreHostile` is the only valid hostility test |
| [docs/Design/Build_Grid.md](docs/Design/Build_Grid.md) | **The 2 m build grid** — cell size and snap rule, per-building footprints in cells (Hut = 1 cell), one-cell impassable resource/curse/tree nodes, footprint-shaped outlines, wall hubs snap / segments freeform. Supersedes every earlier footprint number |
| [docs/Design/Age_0.md](docs/Design/Age_0.md) | Pre-culture Age 0 — every building / unit / tech / cost |
| [docs/Design/Age_1_Alanthor.md](docs/Design/Age_1_Alanthor.md) | Alanthor (defense focus) Age 1 tree |
| [docs/Design/Age_1_Runai.md](docs/Design/Age_1_Runai.md) | Runai (economy / movement focus) Age 1 tree |
| [docs/Design/Age_1_Feraldis.md](docs/Design/Age_1_Feraldis.md) | Feraldis (military focus) Age 1 tree |
| [docs/Design/Fire.md](docs/Design/Fire.md) | **Fire** — the four ground states (Fuel/Burning/Ash/Bare), slow orthogonal spread, burn DOT, the blood chain-ignition rule (one blood tile lit = every blood tile lit), ash reverting to its original terrain, and the two effect languages |
| [docs/Design/Sects.md](docs/Design/Sects.md) | **The 12 sects** — 3 active powers (levels I-III), 1 passive, 1 unit, 1 research each; the four fixed casting radii; the adoption-timing level rule; **no chapel auras**. Supersedes the sect sections of task-063 and the shipped `SectLeverEffects` numbers. Visualization: [docs/SectReference.html](docs/SectReference.html) |

Player-facing UX (controls, hotkeys, AI personalities, multiplayer) lives
in [GAME_MANUAL.md](GAME_MANUAL.md). Code-level runtime reference (what
the code currently does, often pre-design-pass) lives in
[docs/Technical_Reference.md](docs/Technical_Reference.md).

## Architecture

### ECS (Data-Oriented)
- **Per-entity code is co-located with its data** in `Assets/GameData/TechTree/{Units,Buildings}/<Culture>/<Entity>/`: the factory, the entity's components file, any single-entity systems and visuals, next to its SO/prefab. Kept in the runtime assembly via `Assets/GameData/TechTree/TheWaningBorder.Runtime.asmref`. Sect units follow the same pattern under `Units/Sects/<Unit>/` and **sect buildings under `Buildings/Sects/<Building>/`** (a branch beside the cultures — Chapel and Reliquary today, the other eleven sect-unique buildings when they land); the curse's two structures live at `Buildings/Border/LargeNode/` (BorderMainNode, the corner well / verb objective) and `Buildings/Border/SmallNode/` (the blight-pocket / corrupted-crop anchor, formerly "Sporeling").
- **Set-level code sits one level above**: culture-wide tier components at `.../<Culture>/` (e.g. `Age0UnitComponents.cs`), all-unit/all-building code at `Units/` / `Buildings/` root (e.g. `UnitComponents.cs`, `BuildingUpgradeSystem.cs`, wall-set systems at `Buildings/Alanthor/`).
- **Cross-domain components** (CoreComponents, CombatComponents, etc.) stay in `Scripts/Components/`; **cross-domain systems** (Combat, Navigation, Work, Training, AI, Border) stay in `Scripts/Systems/` by domain.
- **`Scripts/<Domain>/` vs `Scripts/Systems/<Domain>/`** — four domains (AI, Abilities, World, Economy) appear in both places, and the rule is:
  - `Scripts/<Domain>/` holds the domain's **state, policy and helpers** used from anywhere — `AIBrain`, `TargetScorer`, `AbilityCatalog`, `FactionResources`, `TerrainUtility`, `PassabilityGrid`.
  - `Scripts/Systems/<Domain>/` holds its **ECS systems**, plus helpers used *only* by them (`AIEndgameCommon`, `AIPivotalReserve`).

  Audited 2026-08-26: the rule holds in every file — no ECS system sits in a domain folder, and no domain-wide helper sits under `Systems/`. It looked like the same domain scattered across two folders, which is why it is written down now; splitting a domain's state from its systems is deliberate, not drift.
- **Shared factories are DISPATCH ONLY**: `UnitFactory.cs` in `Entities/Units/` and `BuildingFactory.cs` in `Entities/Buildings/` hold the id→recipe table and the cross-entity queries; the per-entity creation code lives in that entity's GameData folder as its own static class (`Hall.Create`, `KingsCourt.Create`, …, both an `EntityManager` and an `EntityCommandBuffer` overload). Adding a building = write its class in its folder, add one row to the recipe table.
- **Abilities are co-located too**: `Assets/GameData/TechTree/Abilities/{Unit,Status}/<Ability>/` carries one `AbilityDefSO` per ability plus its icon/VFX-prefab slots (generate via `Waning Border > Tech Tree > Generate Ability SOs`; the `AbilityCatalog` code seed is the runtime fallback). Per-sect mechanic systems live at `Abilities/Sect/<Sect>/`; the generic ability engine stays in `Scripts/Abilities/` + `Scripts/Systems/Abilities/`, sect god powers stay JSON-backed.
- **Presentation** lives at the TechTree top layer: `Assets/GameData/TechTree/Presentation/{Spawn,Buildings,Units,Border,Vfx,Procedural}/` holds the shared pipeline (PresentationSpawnSystem core, EntityViewManager, all-building/all-unit visual systems). Entity-specific visuals live in that entity's folder — including `PresentationSpawnSystem.<Entity>.cs` partials for procedural builders (Smelter, Vault of Almierra, Border LargeNode, the Alanthor wall set).
- **Resource nodes follow the same convention**: `Assets/GameData/TechTree/ResourceNodes/{VeilstoneOutcropping,VeilsteelDeposit,IronDeposit}/` carry each node's factory, bootstrap, map marker and visual code (the veilstone gem-cluster prefab cache lives in VeilstoneOutcropping and is shared by the well and veilsteel visuals). The branch is named `ResourceNodes`, **not** `Resources`, on purpose: a folder called `Resources` anywhere in `Assets/` is a Unity magic folder, so every asset under it would be force-included in builds and `Resources.Load`-able. Do not rename it back.
- **The sect layer lives with the sects**: `Assets/GameData/TechTree/Abilities/Sect/` holds `SectConfig`, `SectAdoption`, `SectQuery`, `SectLeverEffects`, `SectInfo`, `SectDefinition`, `SectAdoptionState`, `WarSectCostHelper` and the two generic lever systems, directly above the 12 per-sect mechanic folders. Nothing sect-related remains in `Scripts/`.
- **The curse's code is all under `Buildings/Border/`**: set-level components/settings/construction/death-drop at the root, well code in `LargeNode/` (factory, bootstrap, marker, node-state, verb/victory/income systems, extinction), pocket code in `SmallNode/` (factory, bootstrap, marker, pocket system). The map-wide veil *field* simulation stays in `Scripts/Systems/Border/` — it is a grid, not a structure.
- All player commands route through `Core/Commands/CommandRouter.cs`

### Managed (MonoBehaviour)
- UI uses IMGUI panels in `UI/Panels/` and `UI/HUD/`
- Input handling in `Input/RTSInputManager.cs` and `Input/SelectionSystem.cs`
- Camera in `Input/CameraController.cs`

### Assemblies
The game compiles into **two** assemblies today, and the split is being widened
one layer at a time (see the restructure plan):

| Assembly | Root | Files | Contains |
|----------|------|-------|----------|
| `TheWaningBorder.Runtime` | `Assets/Scripts/` (+ `GameData/TechTree` via asmref) | 670 | Core, Components, Systems, Entities, Data, Multiplayer, World and all content. **References nothing of ours.** |
| `TheWaningBorder.Presentation` | `Assets/Scripts/Presentation/` | 73 | `UI/` and `Input/`. They are mutually dependent (14 files one way, 3 the other), so they are one assembly. → Runtime |
| `TheWaningBorder.Bootstrap` | `Assets/Scripts/Bootstrap/` | 15 | wiring; the only layer allowed to know about everything. → Runtime, Presentation |
| `TheWaningBorder.Editor` | `Assets/Scripts/Editor/` | 6 | `PlayerBuild`, `AlphaBuildPostProcess`, `MapSceneSync`, `MapInfoBaker`, `MapLobbyImageBaker`, `MapAssetFolders`. → Runtime |

**The dependency arrows only point one way, and the compiler now enforces it.**
Simulation code cannot reference the UI: it posts to `Core/SimSignals` (notices,
minimap pings, match end) and `UI/HUD/SimSignalPump` drains that each frame.
Screen facts the sim genuinely needs — is the loading overlay up, is a building
being placed, what is selected — are PUBLISHED down into `Core/PresentationState`
by their owner, never read up out of the UI.

Two traps this split exposed, worth knowing before adding code:
- `internal` is per-ASSEMBLY. `AgeUpSystem`'s transform helpers were internal and
  became invisible to `StartAgePromoter` the moment Bootstrap moved out.
- A namespace can lie about which assembly a type is in. `VictoryConditionSystem`
  sat in `Scripts/Systems/Core` (Runtime) declaring `namespace TheWaningBorder.UI.HUD`,
  so a content file needed `using TheWaningBorder.UI.HUD` to reach a Runtime type.
  Keep the namespace matching the folder.

`TheWaningBorder.Editor` is `includePlatforms: ["Editor"]`, so editor code can
no longer reach a player build and **needs no `#if UNITY_EDITOR` guard**. That
guard used to be the only thing keeping `UnityEditor` out of the shipped
assembly, because the `Editor/` folder convention does NOT apply inside an
asmdef — it still doesn't, anywhere else in the tree, so a new editor-only file
placed outside `Assets/Scripts/Editor/` still needs the guard.

**The release pipeline lives in this assembly**: `tools/release.ps1` drives
`-executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build`, which calls
`MapSceneSync.ScenesForPlayerBuild` for the ship gate. If it fails to compile,
builds stop.

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
