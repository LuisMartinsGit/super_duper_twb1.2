---
deft:
  id: task-codebase-audit-064
  type: task
  status: completed
  stage: release
  phase: 0
  total_phases: 0
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Audit unfinished scaffolding and dead code

## Context

Codebase has been through several rewrites — IMGUI → UI Toolkit → CEF web HUD; flat heightmap → procedural; multiple sect / building / culture passes. Each rewrite left behind some plumbing that's still attached but inert. This audit catalogues what's present-but-unfinished so the human can triage into "ship", "delete", or "park".

## Re-verification — 2026-05-19

Snapshot below was authored 2026-05-18. Verified against current `test/all-fixes-rolled-up` HEAD (commit `9e5baf2`):

**Resolved since original audit:**
- `PresentationSpawnSystem.CreateProceduralForest` — **deleted** (no longer in source).
- `GameSettings.SmartMilitaryDrag` — was **not** dead after all; read at [SelectionSystem.cs:462](Assets/Scripts/Input/SelectionSystem.cs#L462). Audit was wrong on this row.
- `Runai_TradingPost` + `ThessarasBazaar` + `Alanthor_Tower` — now in `BuildableBuildings`.

**Still applicable:**
- `SECTS-BINDING-TODO` markers in [HudBridge.cs:21,122](Assets/Scripts/UI/Web/HudBridge.cs#L21) — unchanged.
- `Feraldis_Hunter` orphan in TechTree at line 1121 — unchanged.
- Unplaceable culture buildings: `Runai_Vault` *(now flagged for retirement per Complete.md §4.4)*, `Runai_VeilsteelFoundry`, `Feraldis_Foundry`, `Alanthor_Crucible`, `KingsCourt`, `Alanthor_WallTower`, `Alanthor_WallGate` — none in `BuildableBuildings` at [EntityExtractors.cs:695-707](Assets/Scripts/UI/Panels/EntityExtractors.cs#L695).
- `GameSettings.UseNavMesh` — still only a comment reference.
- `GameSettings.PathfindingCellSize` — still write-once, candidate for `const`.
- `NodeInvulnerabilityState` — still in [NodeStateComponents.cs](Assets/Scripts/Core/Components/NodeStateComponents.cs).
- `PresentationId 551 (WallSegment)` — verify and drop.
- IMGUI suspend list (Section B): unchanged.

**Cross-references to newer tasks (created 2026-05-19):**
- The unplaceable `Runai_Vault` row is superseded by **task-069 runai-buildings-split** (full retirement).
- The `RoyalStable` mockup (Section A) is now scoped in **task-068 alanthor-royal-stable**.
- The `VeilsteelForge` mockup is partially in **task-070 veilsteel-frenzy** (tech rename / wiring) but a *building* is separate — not on roadmap.

## Findings — Audit Report

### A. Stub UI items that fire nothing

**Web HUD `Actions.jsx`** — items showing in UI with no working invoke path:

| Surface | Item | Issue |
|---|---|---|
| `BUILDINGS_START / PLACING / ERA2` | `ArcheryRange` | Marked `notWired: true`; **superseded** — `BuildType.ArcheryRange` enum + placement path now exist, the `notWired` flag is stale and should be removed |
| `BUILDINGS_ERA2` | `RoyalStable` | `notWired: true`. **No TechTree entry, no BuildType, no factory.** Pure mockup. |
| `BUILDINGS_ERA2` | `VeilsteelForge` | `notWired: true`. **No TechTree entry, no BuildType, no factory.** Pure mockup. |
| Selection layout | `vault` | Empty "Resource overflow protected" placeholder card — no buttons |
| `ActionsGrid` 'building' fallback | (removed) | Already cleaned up — used to dump Hall's TRAIN_UNITS + mock RESEARCH for any unknown building |

**Cost mismatches between Actions.jsx hardcoded fallbacks and TechTree.json:**
- `GatherersHut`: JSX `60 supplies` vs TechTree `120` (2× off).
- `Alanthor_Tower` named "Watch Tower" in JSX; actual TechTree id is `Alanthor_WallTower`.
- Other unit costs are fine because the tooltip reads from the live `costs` topic (HudBridge pushes TechTreeDB values); JSX hardcoded `cost: {res, n}` is only a first-frame fallback.

**HudBridge `actions:invoke` fall-through ([HudBridge.cs:295](Assets/Scripts/UI/Web/HudBridge.cs#L295))** — any key not matched by `builder / hall|barracks|archery|shrine / military|multi` lands at `Debug.Log("binding TODO")`. Confirmed unwired surfaces: sect actions (sidebar:action handler logs and exits, [HudBridge.cs:122-123](Assets/Scripts/UI/Web/HudBridge.cs#L122-L123)), research, culture-choose handled separately, anything else.

### B. Suspended IMGUI components — true dead vs static-API-alive

From `GameplayUIController.SuspendedImguiTypeNames` ([GameplayUIController.cs:95-115](Assets/UI/Scripts/GameplayUIController.cs#L95-L115)):

| Type | Suspended | Statics still called by live code? |
|---|---|---|
| `EntityInfoPanel` | yes | `IsPointerOver` → delegates to controller, fine |
| `EntityActionPanel` | yes | `IsPointerOver` delegates, fine |
| `InGameMenuPanel` | yes | `IsOpen`, `Close()` — **alive**, called from HudBridge |
| `ReligionHUD` | yes | **no live callers — dead** |
| `VictoryProgressHUD` | yes | **no live callers — dead** |
| `SpellPanel`, `ActiveAbilityBar`, `CrystalDebugPanel`, `TechTreePanel` | yes | **no live callers — dead** |
| `ResourceHUD` | yes | `NextPanelX` was a layout helper for sibling IMGUI panels — all siblings also disabled → effectively dead |
| `EndGameButton`, `PostGameStatsUI` | yes | `PostGameStatsUI` instantiated by `HudBridge.HandleMenuItem` for surrender flow — alive |
| `MinimapRenderer` | NOT suspended | rendered as RawImage inside web-HUD diamond frame, alive |

### C. Standing TODOs tied to gameplay

Only 5 across the whole codebase — focused, not sprawling:
- `BuildingComponents.cs:242` — `// TODO(task-063 phase 2)` chapel upgrade hosts (sect system).
- `HudBridge.cs:21, 122-123, 295` — three **SECTS-BINDING-TODO** markers: sect adoption / level-up / cast all unwired in web HUD.

No combat, training, victory, multiplayer/lockstep, or AI-brain TODOs found.

### D. ECS components & systems

**Truly dead components:**
- `NodeInvulnerabilityState` ([NodeStateComponents.cs](Assets/Scripts/Core/Components/NodeStateComponents.cs)) — file comment says "NodeInvulnerabilitySystem has been deleted; this struct is unused at runtime but its archetype slot stays". Archetype-slot trick is fine but the struct can drop once a migration is done.

**Removed-but-mentioned:**
- `HarassTimer`, `UnitSpawnTimer` already pruned from `CrystalAIState` per code comments.

**Systems:** 101 systems scanned, all properly `[UpdateInGroup]`-tagged and auto-registering. None abandoned, no `[DisableAutoCreation]`, no `#if false` blocks.

**No duplicate component names** across `Assets/Scripts/Core/Components/`.

### E. PresentationId orphans

`PresentationSpawnSystem._prefabPaths` registers paths for IDs that no entity factory ever assigns:

| ID(s) | Path | Why orphaned |
|---|---|---|
| 551 | `WallSegment` | Comment in source: "legacy, no longer spawned" |
| 400, 401 | `Forest`, `Rock` | Forest visual is now Unity terrain trees (no PresentationId); Rock still spawned by `ObstacleBootstrap` so 401 is actually live |
| 101, 102, 200-207, 300-322, 390-399 (gaps), 550-554, 560 | various | Procedural-only entities (visuals drawn by their own system, not the prefab-path table). Expected. |

Genuine orphans to drop: **551** plus a sweep of any other legacy entries that pre-date the procedural visual pipeline.

### F. Bootstrap & GameSettings dead flags

- **MusicManager** auto-creates via `[RuntimeInitializeOnLoadMethod]` but **no other code reads `MusicManager.*`** — singleton sits in memory unreferenced.
- `GameSettings.PathfindingCellSize` — read only inside `PassabilityGrid.Start` (1 hit). Never written; could be `const`.
- `GameSettings.MapArchetype` — written by `SkirmishLobbyUI`, read by `ProceduralTerrain.Awake`, fine actually.
- `GameSettings.SmartMilitaryDrag` — declared, **never read** anywhere in `Assets/Scripts/`. Dead flag.
- `GameSettings.UseNavMesh` — referenced only in a `// Future PRs flip ...` comment in `NavMeshManager.cs`. Dead flag.

### G. Legacy fallback paths still alive in source

- `ProceduralTerrain.GenerateTerrainLayers / GenerateHeightmap / PaintSplatmaps` — fallback path for when `ProceduralMapGen.Generate` fails, kept on purpose. Still alive.
- `PresentationSpawnSystem.Obstacles.CreateLegacyIronDepositMesh` — fallback when the iron-deposit prefab is missing from the build. Defensive, low cost, fine to keep.
- `PresentationSpawnSystem.CreateProceduralForest` — **dead**. Forests no longer carry `PresentationId`, so the dispatcher never routes to this method. ~120 lines of sphere/cylinder forest mesh code that can drop.
- `GenerateTerrainLayers` (the `southood`-era version) and `BakeShadedCurseDiffuse` curse-layer baker — still alive but only used in fallback branches.

### H. Content-data reachability gaps

**Buildings in TechTree but missing from `BuildableBuildings` HashSet → player can never place them:**
- `Runai_Vault`
- `Runai_VeilsteelFoundry`
- `Feraldis_Foundry`
- `Alanthor_Crucible`
- `KingsCourt` (Alanthor HQ alternative)
- `Alanthor_WallTower`, `Alanthor_WallGate` (wall upgrade variants)

**Untrained units:**
- `Feraldis_Hunter` — defined in TechTree.json but no building's `trains` array lists it. Orphan.

**Culture-building upgrade gap:** Only Hall / Barracks / ArcheryRange / Hut carry `BuildingUpgradeable`. `BuildingUpgradeConfig.TryGetCost` has cases only for those four. None of the per-culture buildings (Runai_*/Feraldis_*/Alanthor_*) can be upgraded even if a button were exposed.

**Actions.jsx catalogue staleness:** `BUILDINGS_ERA2` mockup is missing the Feraldis_HuntingLodge / LoggingStation / Longhouse / Tower / SiegeYard set. The C# `GetBuildingActions` reads TechTreeDB dynamically and would render them correctly — but the static JSX catalogue is out of date.

---

### Triage priorities (suggested)

**Drop now (zero-risk):**
1. `RoyalStable` and `VeilsteelForge` rows in `Actions.jsx BUILDINGS_ERA2` — no TechTree backing, no factory.
2. `notWired: true` flag on `ArcheryRange` rows (it's wired now).
3. `PresentationSpawnSystem.CreateProceduralForest` (~120 lines).
4. PresentationId 551 (`WallSegment`) entry.
5. `GameSettings.SmartMilitaryDrag` and `UseNavMesh` flags.
6. `NodeInvulnerabilityState` archetype slot (after migration sweep).

**Wire or decide:**
1. **SECTS-BINDING-TODO** in HudBridge (sidebar sect actions). Visible UI, doesn't fire — biggest gameplay-facing gap.
2. **Untrained `Feraldis_Hunter`** — add `trainAt` or remove from TechTree.
3. **Unplaceable culture buildings** (Runai_Vault / VeilsteelFoundry / Feraldis_Foundry / Alanthor_Crucible / KingsCourt) — six buildings authored but unreachable. Decide ship vs cut.
4. **Culture-building upgrades** — if upgrades are intended, extend `TryGetCost` + add `BuildingUpgradeable` in each factory.

**Fix soon:**
1. `GatherersHut` cost in Actions.jsx (60 → 120 to match TechTree).
2. `Alanthor_Tower` vs `Alanthor_WallTower` name mismatch in JSX.
3. Update `BUILDINGS_ERA2` JSX catalogue to include the Feraldis tier-2 set.

**Park (intentionally dormant):**
- All `SuspendedImguiTypeNames` panels with no live callers — leave attached for fallback, accept as cold storage. Could collapse the suspend list someday but no urgency.
- `ProceduralTerrain` legacy continental noise path — defensive fallback, low cost.
- `CreateLegacyIronDepositMesh` — defensive fallback if prefab vanishes from build.

## User Value

Triageable inventory of dead code surfaces so cleanup tasks can be scoped without each one re-discovering the same baseline.

## Requirements

1. Findings catalogued in this task body (above).
2. No code changes performed under this task — deletions land as follow-up tasks per row of "Drop now".

## Acceptance Criteria

- Audit covers UI, ECS, bootstrap, presentation, settings flags, and content reachability.
- Each finding has a one-line context anchor (file + reason).
- Triage column proposes ship / wire / drop / park for each finding.

## Implementation Phases

Not applicable — this is a survey task. Cleanup work is intended to spin out as child tasks.
