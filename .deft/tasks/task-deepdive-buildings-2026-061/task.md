---
deft:
  id: task-deepdive-buildings-2026-061
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [code-quality, code-review, deep-dive, buildings, factories]
---

# Building Factories / Placement / Cultural Variants deep-dive (FINAL of series)

## Context

**Final task in the system-wide deep-dive series (task-052..061).** Companion to all priors. Covers `BuildingFactory.cs` (2,572 lines), all `Entities/Buildings/*.cs`, `BuildingSizeConfig`, all wall systems, all building construction systems, building placement UI, and cultural variant coverage.

## TL;DR

Factory itself is mostly mechanically correct (every `Create*` method returns a properly-formed entity with the components combat/passability/training systems require, and recent task-050 fixes for `BuildingSize` on TempleOfRidan/TradingPost are intact). Damage clusters in three places:

- **B-1 CRITICAL**: Walls bypass lockstep entirely. `SpawnWallHub` calls `AlanthorWall.CreateHub/CreateSegment/CreateInstance` directly — no `CommandRouter.IssuePlaceBuilding`, no NetworkedEntity. **Multiplayer-breaking** for walls. Same root cause as task-054 F-6.
- **B-5 HIGH**: `BuildCommandPanel.BuildType` enum missing 8+ building IDs (KingsCourt, Crucible, both Foundries, Runai Vault/TradingPost, all Chapels, all Sect uniques). **Player has no UI path to build half the cultural roster** — buttons that exist silently default to building Huts.
- **B-2 dormant**: Chapels with PIDs 400 and 401 collide with `ForestPresentationId=400` and `RockPresentationId=401`. The Chapel `_Sect_FlamewroughtChains` and `_Sect_UnmakersGrasp` would render as tree clusters/boulders if anyone ever spawned them. Disarmed only because no current path constructs them.
- **B-6 MEDIUM**: ShrineOfAhridan footprint mismatch — placement preview validates 3×3, but `CreateShrineOfAhridan` writes 4×4 BuildingSize. Caused by a typo in `BuildingSizeConfig` (`"ShrineOfRidan"` instead of `"ShrineOfAhridan"`). Player can place a Shrine in a 3×3 gap that becomes 4×4 obstruction.

Plus 15 lower-severity findings (ECB factory missing Alanthor_Wall, GetBuildTime/GetPresentationId switch holes, chapel-slot dead code, query leaks, etc.).

## Acceptance Criteria

- [ ] B-1: Walls route through CommandRouter.IssuePlaceBuilding in multiplayer
- [ ] B-3: Add Alanthor_Wall case to BuildingFactory.Create(ECB)
- [ ] B-2: Move Chapel PIDs 400/401 to a non-conflicting range, OR add explicit branches in PresentationSpawnSystem
- [ ] B-5: BuildType enum + TriggerBuildingPlacement + BuildId all updated for KingsCourt, Crucible, both Foundries, Runai Vault, Runai_TradingPost, all Chapels, all Sect uniques
- [ ] B-6: Fix `BuildingSizeConfig` "ShrineOfRidan" → "ShrineOfAhridan" typo + use correct lookup in `CreateShrineOfAhridan`
- [ ] B-4: Add `else` to EntityActionPanel.cs:211-213
- [ ] B-7: Decide on chapel-slot pipeline (implement or delete)
- [ ] B-8: Reconcile CommandRouter.GetBuildTime vs AIBuildingManager.GetBuildTime; add missing entries

---

## Verified Findings

### B-1 — CRITICAL: Walls bypass lockstep, no NetworkedEntity, no validity check
**Files:** [BuildCommandPannel.cs:563-647](Assets/Scripts/UI/Panels/BuildCommandPannel.cs#L563-L647) (`SpawnWallHub`), [AlanthorWall.cs:47-91, 99-152, 207-240](Assets/Scripts/Entities/Buildings/AlanthorWall.cs#L47-L91)
**Severity:** Critical — multiplayer-breaking

`SpawnSelectedBuilding` (line 429) properly branches on `IsMultiplayer` and queues via `CommandRouter.IssuePlaceBuilding`. `SpawnWallHub` has **no such branch** — calls `AlanthorWall.CreateHub(_em, pos, fac)` directly, then `CreateSegment`, adds `UnderConstruction` directly via `_em.AddComponentData`. In MP:
- Each client only sees its own walls.
- Wall HP/destruction desyncs immediately.
- `BuildingFactory.Create` adds `NetworkedEntity` (line 98) — `AlanthorWall.CreateHub/Segment/Instance` add NONE.

Additionally, `SpawnWallHub` performs **no `IsValidBuildPosition` check** — `_placementValid` only computed for non-wall builds (lines 145-156). You can place walls on water, slopes >15°, on top of trees/iron deposits, or on top of other buildings.

### B-2 — Chapels with PIDs 400 and 401 render as Forest/Rock obstacles (DORMANT)
**Files:** [BuildingFactory.cs:233-234](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L233-L234), [ObstacleBootstrap.cs:23-24](Assets/Scripts/Bootstrap/ObstacleBootstrap.cs#L23-L24), [PresentationSpawnSystem.cs:268-279](Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L268-L279)
**Severity:** Dormant but armed

`Chapel_Sect_FlamewroughtChains => 400` and `Chapel_Sect_UnmakersGrasp => 401` collide with `ForestPresentationId = 400` and `RockPresentationId = 401`. `SpawnVisual` checks for forest/rock PIDs first → never reaches `CreateChapel`. Currently dormant (no production path constructs these chapels — see B-7), but a permanently-armed bug.

### B-3 — `BuildingFactory.Create(EntityCommandBuffer, ...)` switch missing `"Alanthor_Wall"` case
**File:** [BuildingFactory.cs:110-172](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L110-L172)
**Severity:** Medium — silent corruption if used

EM variant has `"Alanthor_Wall" => AlanthorWall.CreateHub(em, position, faction)` at line 44. ECB variant **omits the entry** → falls through to `CreateDefault(ecb, ...)` → builds a generic 3×3 entity with PID=100 (Hall). Lacks `WallTag/WallHubTag/WallHubLink` buffer — wall systems treat such an entity as a stray Hall. AI build orders never queue this so latent.

### B-4 — `EntityActionPanel.DrawBuildingPlacementPanel` missing `else`
**File:** [EntityActionPanel.cs:211-213](Assets/Scripts/UI/Panels/EntityActionPanel.cs#L211-L213)
**Severity:** Low — visual noise

Both labels render stacked while placing. Same indent-without-braces pattern.

### B-5 — `BuildCommandPanel.BuildType` enum missing 8+ building IDs
**File:** [BuildCommandPannel.cs:54-63](Assets/Scripts/UI/Panels/BuildCommandPannel.cs#L54-L63) (enum), [:212-240](Assets/Scripts/UI/Panels/BuildCommandPannel.cs#L212-L240) (switch)
**Severity:** High — half the cultural roster is unbuildable via UI

Missing: `KingsCourt`, `Alanthor_Crucible`, `Runai_TradingPost`, `Runai_Vault`, `Runai_VeilsteelFoundry`, `Feraldis_Foundry`, all `Chapel_Sect_*`, all `Sect_*` IDs. Switch returns `BuildType.Hut` on default. UI buttons posting these IDs through `TriggerBuildingPlacement` silently get a Hut.

`EntityActionPanel.DrawBuildingPlacementPanel` (line 222) blindly forwards `button.Id` from action grid → any unlock surfacing these IDs is broken at the UI layer.

### B-6 — ShrineOfAhridan footprint mismatch (placement vs build)
**Files:** [BuildingSizeConfig.cs:29](Assets/Scripts/Core/Settings/BuildingSizeConfig.cs#L29) (typo: `"ShrineOfRidan"` not `"ShrineOfAhridan"`), [BuildingFactory.cs:483](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L483) (lookups `TempleOfRidan` size = 4×4)
**Severity:** Medium — placement-time/build-time desync

`BuildCommandHelper.IsValidBuildPosition` invoked from `BuildCommandPannel.cs:152-154` with `BuildCommandHelper.GetBuildingSize("ShrineOfAhridan")` → falls through `BuildingSizeConfig`'s default `int2(3,3)`. Preview validates 3×3 footprint. But `CreateShrineOfAhridan` writes `BuildingSize { Width=4, Height=4 }` (using `TempleOfRidan` lookup key). `PassabilityBuildingSync` then blocks 4×4. Players place Shrine in 3×3 gap that becomes 4×4 obstruction → adjacent buildings/units may overlap new footprint.

`BuildCommand.GetBuildingRadius` (line 274) repeats same typo: `"ShrineOfRidan" => 1.8f`.

### B-7 — Chapel-slot system is half-implemented dead code
**Files:** [BuildingFactory.cs:1983, 555-566, 2009-2029](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L1983), [TempleChapelBuildSystem.cs:85-101](Assets/Scripts/Systems/Work/TempleChapelBuildSystem.cs#L85-L101)
**Severity:** Medium — design unclear

`ChapelSlotCount = 7` (line 1983) but `for i < 8` (lines 555-566) initialise the buffer. `CreateChapelAtSlot` (lines 2009-2029) is unreferenced. `TempleChapelBuildSystem` says "chapels are NOT standalone entities anymore" but grants RP and recalculates passives without instantiating a chapel entity. Whole pipeline (`TempleChapelSlot.State`, `BuildProgress`, `BuildTime`) appears dead.

### B-8 — `CommandRouter.GetBuildTime` switch holes; player vs AI inconsistency
**File:** [CommandRouter.cs:530-547](Assets/Scripts/Core/Commands/CommandRouter.cs#L530-L547)
**Severity:** Medium

Missing entries default to 30f: Hall, ShrineOfAhridan, KingsCourt, Crucible, Runai_Vault, Runai_VeilsteelFoundry, Feraldis_Foundry, Runai_TradingPost, all Chapel/Sect_*. `AIBuildingManager.GetBuildTime` (line 298-326) has a richer map but disagrees on TempleOfRidan (40f vs 50f). **Same building takes different time depending on whether player or AI placed it.**

### B-9 — `BuildingFactory.GetPresentationId` missing `Runai_TradingPost`
**File:** [BuildingFactory.cs:187-242](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L187-L242)
**Severity:** Low — latent

Returns 100 (Hall) for `Runai_TradingPost`. AI counting logic via PID would mistake Halls for trading posts. Currently dormant.

### B-10 — `WallGatePassabilitySystem` leaks an EntityQuery per tick
**File:** [WallGatePassabilitySystem.cs:42-46](Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs#L42-L46)
**Severity:** Medium — slow leak

Created via `EntityManager.CreateEntityQuery` every 0.3s, never disposed. Same pattern in `BuildingFactory.GetFactionChoiceBuilding` (line 311) and `GetFactionBuildingCount<T>` (line 334) but those run only on player action.

### B-11 — Sect unique buildings have no special components
**File:** [BuildingFactory.cs:2034-2305](Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L2034-L2305)
**Severity:** Medium — design incomplete

All twelve `CreateSect*` methods give buildings only `SectUniqueBuildingTag`. None have Defense, BuildingRangedAttack, ResearchState/TrainingState, or other components implied by names (FlameBeacon, ArchiveTower described as towers/spires; Tribunal "judges" with no state machine). Plain HP+visual placeholders.

### B-12 — `BuildCommandHelper.IsValidBuildPosition(float radius)` overload + `GetBuildingRadius` dead
**File:** [BuildCommand.cs:70-133, 262-298](Assets/Scripts/Core/Commands/CommandTypes/BuildCommand.cs#L70-L133)
**Severity:** Low — dead code

Radius-based overload has no callers. Only `int2 buildingSize` overload (line 148) is used.

### B-13 — System queries not disposed in BuildingConstructionSystem / Wall systems
**File:** [BuildingConstructionSystem.cs:395-399](Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs#L395-L399)
**Severity:** Low — reclaimed at world teardown

Pattern repeats in Wall*System files. World teardown reclaims so not catastrophic but inconsistent with cached-query pattern (task-008).

### B-14 — Indentation traps in three places (valid C# but reads as bug)
**Files:** [BuildCommand.cs:310-313](Assets/Scripts/Core/Commands/CommandTypes/BuildCommand.cs#L310-L313), [BuildingConstructionSystem.cs:156-159, 477-480](Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs#L156-L159), [CommandRouter.cs:515-518](Assets/Scripts/Core/Commands/CommandRouter.cs#L515-L518)
**Severity:** Low — readability landmine

Each block looks like the second branch is unconditionally executed. They compile correctly because C# pairs `else` with the lone `if`, but indentation is so misleading any future formatter run would reintroduce a bug. Add braces.

### B-15 — `AutoDecorateExisting` skips PID 355 in auto-decorate path
**File:** [ProceduralBuildingGenerator.cs:83](Assets/Scripts/Presentation/ProceduralBuildingGenerator.cs#L83)
**Severity:** Low — visual

PID 355 (Runai_TradingPost / Alanthor_Garrison) created by `Create355` at PresentationSpawnSystem.cs:377 doesn't run auto-decorator. Foundation/stripes/culture lighting missing.

### B-16 — Hub `Radius` undersized vs visual
**Files:** [AlanthorWall.cs:80-82](Assets/Scripts/Entities/Buildings/AlanthorWall.cs#L80-L82), [PresentationSpawnSystem.Walls.cs:91-146](Assets/Scripts/Presentation/PresentationSpawnSystem.Walls.cs#L91-L146)
**Severity:** Low — units clip into hub bases

Hub blocking footprint = 1m². Visual silhouette ≈ 2.5m² (1.7m wide, >4m tall). Units clip into base of every wall hub.

### B-17 — `BuildingSizeConfig` Sect wildcard after explicit cases is dead
**File:** [BuildingSizeConfig.cs:79](Assets/Scripts/Core/Settings/BuildingSizeConfig.cs#L79)
**Severity:** Low — cleanup

Wildcard `_ when buildingId.StartsWith("Sect_") => new int2(2, 2)` unreachable since explicit Sect_* arms cover everything.

### B-18 — `TempleChapelBuildSystem` namespace inconsistency
**File:** [TempleChapelBuildSystem.cs:10](Assets/Scripts/Systems/Work/TempleChapelBuildSystem.cs#L10)
**Severity:** Low — convention

Namespace `TheWaningBorder.Systems.Building` (singular) while other files use `Systems.Work` or matching plural. File path doesn't match.

### B-19 — `GrantTempleConstructionRP` granted on Shrine, not Temple
**File:** [BuildingConstructionSystem.cs:278-283, 322-335](Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs#L278-L283)
**Severity:** Low — naming

Method named `GrantTempleConstructionRP` and grants `TempleLevelConfig.ShrineBonus`. Caller (line 279) is `if (em.HasComponent<ShrineTag>(building))`. Functionally correct (Shrine deserves bonus, value matches), but naming says Temple. No parallel "grant when Temple of Ridan completes". Misleading.

---

## Cultural Building Coverage Matrix

| Building ID | EM Factory | ECB Factory | GetPresentationId | BuildType enum | UI placement | AI build order | Showcase |
|---|---|---|---|---|---|---|---|
| Hall | yes | yes | 100 | n/a | n/a | n/a | yes |
| Hut | yes | yes | 102 | yes | yes | (Era1) | yes |
| GatherersHut | yes | yes | 101 | yes | yes | (Era1) | yes |
| Barracks | yes | yes | 510 | yes | yes | (Era1) | yes |
| ShrineOfAhridan | yes | yes | 520 | yes | yes (B-6 footprint) | yes | yes |
| TempleOfRidan | yes | yes | 521 | yes | yes | yes | yes |
| VaultOfAlmierra | yes | yes | 530 | yes | yes | yes | yes |
| FiendstoneKeep | yes | yes | 540 | yes | yes | yes | yes |
| Alanthor_Wall | yes | **MISSING (B-3)** | 550 | yes | yes (B-1 mp/valid) | no | yes |
| Alanthor_Smelter | yes | yes | 560 | yes | yes | no | yes |
| Alanthor_Tower | yes | yes | 354 | yes | yes | yes | yes |
| Alanthor_Garrison | yes | yes | 355 | yes | yes | yes | yes |
| Alanthor_Stable | yes | yes | 356 | yes | yes | yes | yes |
| Alanthor_SiegeYard | yes | yes | 357 | yes | yes | yes | yes |
| KingsCourt | yes | yes | 363 | **MISSING (B-5)** | broken | yes | yes |
| Alanthor_Crucible | yes | yes | 364 | **MISSING (B-5)** | broken | no | yes |
| Runai_Outpost | yes | yes | 350 | yes | yes | yes | yes |
| Runai_TradeHub | yes | yes | 351 | yes | yes | no | yes |
| Runai_TradingPost | yes | yes | **MISSING → 100 (B-9)** | **MISSING (B-5)** | broken | no | no |
| ThessarasBazaar | yes | yes | 352 | yes | yes | yes | yes |
| Runai_SiegeWorkshop | yes | yes | 353 | yes | yes | yes | yes |
| Runai_Vault | yes | yes | 365 | **MISSING (B-5)** | broken | no | yes |
| Runai_VeilsteelFoundry | yes | yes | 366 | **MISSING (B-5)** | broken | no | yes |
| Feraldis_HuntingLodge | yes | yes | 358 | yes | yes | yes | yes |
| Feraldis_LoggingStation | yes | yes | 359 | yes | yes | no | yes |
| Feraldis_Longhouse | yes | yes | 360 | yes | yes | yes | yes |
| Feraldis_Tower | yes | yes | 361 | yes | yes | yes | yes |
| Feraldis_SiegeYard | yes | yes | 362 | yes | yes | yes | yes |
| Feraldis_Foundry | yes | yes | 367 | **MISSING (B-5)** | broken | yes | yes |
| Chapel_Sect_* (10 of 12) | yes | yes | 390-399 | **MISSING (B-5)** | broken | no | no |
| Chapel_Sect_FlamewroughtChains | yes | yes | **400 = Forest (B-2)** | **MISSING (B-5)** | broken | no | no |
| Chapel_Sect_UnmakersGrasp | yes | yes | **401 = Rock (B-2)** | **MISSING (B-5)** | broken | no | no |
| Sect_Sanctuary..PurgeAltar (12) | yes | yes | 410-421 | **MISSING (B-5)** | broken | no | no |

**Summary: 8 player-facing buildings have no working UI path** because `BuildType` enum is incomplete.

---

## What I Verified Is Fine

- BuildingFactory EM/ECB pairs match for non-wall buildings (same components added on both paths) — verified by spot-checking 30+ factory methods.
- Every building going through `BuildingFactory.Create(em|ecb, ...)` gets `NetworkedEntity`. Walls bypass — see B-1.
- Brace balance verified: BuildingFactory.cs (881/881), ProceduralBuildingGenerator.cs (245/245), AlanthorWall.cs (39/39), BuildCommandPannel.cs (76/76).
- `WallSegmentCleanupSystem.DestroySegmentWithInstances` correctly snapshots `WallInstanceRef` buffer to NativeArray before structural changes.
- `WallGatePassabilitySystem` correctly uses braced if/else for open/close (the missing-brace bug it replaced has not regressed).
- `BuildCommandHelper.IsValidBuildPosition(int2)` correctly checks AABB-vs-AABB for sized buildings, falls back to circular for legacy. Slope check uses 4 corner samples + center. Water check via WaterPlane.Instance. PassabilityGrid.IsFootprintPassable as final check.
- `BuildingConstructionSystem.CompleteConstruction` properly removes Buildable, restores Scale = 1f, applies sect HP multiplier, grants Shrine RP bonus.
- `WallUpgradeSystem` correctly removes WallUpgradeState and forces presentation respawn.
- `PassabilityBuildingSync` correctly excludes WallGateTag and UnderConstruction.

---

## Things I Deliberately Didn't Dig Into

- **PresentationSpawnSystem.cs (3500-line god class)** — already noted task-007.
- **Visual / animation / audio for buildings** — out of subsystem.
- **TechTreeDB integration with BuildingDef** — task-057.
