---
deft:
  id: task-audit-may-2026-050
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
  labels: [code-quality, code-review, audit]
---

# May 2026 code-health audit (post AI-rework / observer / patch-mining)

## Context

Three parallel read-only audits on **2026-05-02** covered the subsystems that have changed since the April-08 sweep that produced tasks #001–#046:

- **AI subsystem** — SimpleAISystem build-order rewrite, `AIEconomyManager`/`AIMilitaryManager` made `[DisableAutoCreation]`, mining systems rewritten for 50/50 iron-vs-crystal, `LaunchAttack`/`SetCrystalTarget`/`ReplaceLostUnits` step kinds.
- **Movement / pathfinding** — flow-field cache fix, BlendRadius tightened to 2 m, `GatherCommand` now pre-warms the flow field, `Miner.cs` factory adds `DesiredDestination`.
- **Presentation / spawn / observer / UI** — patch-based iron / crystal spawning, `FlatTestMap` added, observer-mode `ResourceHUD` faction-switching, `MinimapRenderer` query re-init after world disposal, `SelectionSystem` hierarchy walk, `Runai_TradingPost` / `TempleOfRidanNew` `BuildingSize` fix.

This task captures the new findings. Some are independently verified by re-reading the file; others are agent-flagged and need confirmation in scope. Already-fixed items from the April sweep were filtered out before reporting.

## User Value

Triaged backlog of bugs, dead code, and inconsistencies that surfaced after the recent feature sprints. Highest-impact items (the three confirmed missing-brace bugs in formation/separation movement) directly affect AI playtesting reliability.

## Requirements

- R1: Triage every finding below into `keep / fix / not-an-issue`.
- R2: Spin off individual `task-*` entries (or fix in-place) for items marked `keep` with severity ≥ medium.
- R3: Verify findings marked `[unverified]` against the live code before deciding.
- R4: Do not regress the recent fixes the agents skipped (BlendRadius=2m, MovementSystem cache fix, observer-mode ResourceHUD, etc.).

## Acceptance Criteria

- [ ] Each finding either has a follow-up task or is annotated `won't fix` with a reason
- [ ] All three confirmed `Critical` missing-brace bugs are fixed
- [ ] `AIEconomyManager` / `AIMilitaryManager` cargo-cult component initialization in `AIBootstrap` is either removed or justified in a comment
- [ ] No new compile warnings introduced

---

## Findings

### Critical (verified by re-reading the file)

| # | File:Line | Finding | Category |
|---|-----------|---------|----------|
| 1 | [BattalionSyncSystem.cs:435-437](Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs#L435-L437) | Same missing-brace pattern as the four we already fixed: `if (passGrid.IsPassable(altPos)) newPos = altPos; newPos = memberXf.Position;` — the second assignment runs unconditionally so the alternate-direction probe **always** snaps back to the original position. Battalion members in combat never use alternate paths. | bug |
| 2 | [BattalionSyncSystem.cs:514-516](Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs#L514-L516) | Identical pattern in the formation-slot-movement path. Members trying to detour around a building always teleport back to their start cell instead of taking the flow-field detour. | bug |
| 3 | [UnitSeparationSystem.cs:262-264](Assets/Scripts/Systems/Movement/UnitSeparationSystem.cs#L262-L264) | Building push: `if (pushX < pushZ) correctedPos.x = ...; correctedPos.z = ...;` — the Z assignment runs unconditionally, so units always get pushed in *both* axes (Z always wins) instead of along the shorter side of the AABB. Causes weird scraping along building edges. | bug |

### High

| # | File:Line | Finding | Category |
|---|-----------|---------|----------|
| 4 | [AIBootstrap.cs:226-310](Assets/Scripts/Core/Bootstrap/AIBootstrap.cs#L226) | Cargo-cult component init: every AI brain is created with `AIEconomyState`, `AIBuildingState`, `AIMilitaryState`, `AISharedKnowledge`, `AIScoutingState`, `AICrystalHuntState`, `AIStrategyState` even though their owning managers are all `[DisableAutoCreation]` and `SimpleAISystem` reads none of them. Bloats every AI archetype. | dead-code |
| 5 | [SimpleAISystem.cs:227-240](Assets/Scripts/AI/SimpleAISystem.cs#L227) | `FindTrainerForUnit` hardcodes Miner→Hall, Swordsman/Archer→Barracks, Litharch→Temple. Any culture-specific unit (Spearman, Sentinel, Hunter, etc.) added to a build order will fall into `default => Entity.Null` and silently never train. | missing-impl |
| 6 | [BuildingFactory.cs](Assets/Scripts/Entities/Buildings/BuildingFactory.cs) parallel paths | Some entity creators (e.g. `Cadaver.Create` called by `CrystalPatchBootstrap`) bypass `BuildingFactory.Create` and never get a `NetworkedEntity` ID — fine for cadavers (we already do networked IDs on those), but it's a pattern to watch as more direct creators land. `[unverified]` — agent-flagged, needs spot check on each creator. | inconsistency |
| 7 | [EntityInfoPanel.cs](Assets/Scripts/UI/Panels/EntityInfoPanel.cs) | Has no `IsObserver` guard like `EntityActionPanel.cs:69` does. Observer can click an enemy unit and see its full info panel — fine for an observer (that's actually the new ResourceHUD use case), but the *training queue / build queue* sub-panels of buildings might not render correctly when the displayed entity is owned by a different faction. `[unverified]` — needs UI play-through to confirm. | missing-impl |

### Medium

| # | File:Line | Finding | Category |
|---|-----------|---------|----------|
| 8 | [AISimpleDifficulty.cs:30-37](Assets/Scripts/AI/AISimpleDifficulty.cs#L30) | `GetIncomeMultiplier` returns 0.7×/1.0×/1.3× per difficulty but is **never called** — Hard/Expert AI gets no actual economic boost despite the API. Either wire it into `FactionEconomy.Tick` per-faction, or delete the dead hook. | dead-code / missing-impl |
| 9 | [SimpleAISystem.cs:159-178](Assets/Scripts/AI/SimpleAISystem.cs#L159) | `RegisterTrainedUnit` only counts Military and Miner classes — Builder/Scout/Support are silently ignored. If a build order ever queues a Builder via `Train("Builder")`, the AI won't replace it on death even though it'd be a real economy hit. Acceptable today (build orders don't use it) but a footgun. | inconsistency |
| 10 | [PassabilityBuildingSync.cs:34](Assets/Scripts/Systems/Work/PassabilityBuildingSync.cs#L34) vs [ObstacleBootstrap.cs:264](Assets/Scripts/Bootstrap/ObstacleBootstrap.cs#L264) | Buildings get `FootprintPaddingCells = 0` (block exact footprint) but trees get `max(TreeObstacleRadius, grid.CellSize)` — at the default 1m cell size that's just `0.75 → 1.0`, but if `PathfindingCellSize` ever rises (e.g. 2m for perf), tree-blocked footprints inflate disproportionately to building footprints. | inconsistency |
| 11 | [WallGatePassabilitySystem.cs:19-36](Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs#L19) | 0.3 s poll interval — if a unit happens to be inside the gate footprint when it closes (rare but possible if combat pushes it through), the unit is trapped. `UnitSeparationSystem` only handles building Radius circles, not gate `BuildingSize` rectangles. Add an "evict-units-inside-footprint-on-close" step. | missing-impl |
| 12 | [MovementSystem.cs:586-594](Assets/Scripts/Systems/Movement/MovementSystem.cs#L586) | Tier-2 stuck (counter > 5): tries one perpendicular per frame, alternating left/right. If both perpendiculars are blocked the unit makes zero progress for 24 frames before tier-3 escalates. Not catastrophic but tightens corridor pathing. Could short-circuit to tier-3 sooner if both perps blocked. | bug (minor) |
| 13 | [ResourceHUD.cs:25](Assets/Scripts/UI/HUD/ResourceHUD.cs#L25) | `[SerializeField] private Faction humanFaction = GameSettings.LocalPlayerFaction;` is now dead — every read replaced by `GameSettings.LocalPlayerFaction` or `GetDisplayedFaction()` after the observer-mode rework. Drop the field. | dead-code |
| 14 | [BuildingSizeConfig.cs:29](Assets/Scripts/Core/Settings/BuildingSizeConfig.cs#L29) | Entry says `"ShrineOfRidan"` but `BuildingFactory` and the build orders use `"ShrineOfAhridan"`. The Shrine never matches the config — falls into the default 3×3 (right by accident). Rename the entry. | inconsistency |
| 15 | [ProceduralBuildingGenerator.cs:51](Assets/Scripts/Presentation/ProceduralBuildingGenerator.cs#L51) | Comment claims PresentationId 355 is "shared between Runai_TradingPost and Alanthor_Garrison" but the BuildingFactory dispatch shows distinct paths. Stale comment. | outdated-pattern |
| 16 | [SpawnDelayHelper.cs](Assets/Scripts/Bootstrap/SpawnDelayHelper.cs) FlatTestMap gating | `IronDepositBootstrap` and `CrystalPatchBootstrap` run regardless of `FlatTestMap`, so the flat sandbox always gets ~30 deposits + ~18 cadavers. That's intended (the user wants resources on the test map) but the README/feature comment in `GameSettings.cs:114` should clarify that `FlatTestMap` strips clutter, **not** resources. | missing-impl (docs) |
| 17 | [GameBootstrap.cs:259-261](Assets/Scripts/Bootstrap/GameBootstrap.cs#L259) | Empty `else if (GameSettings.IsObserver) {}` block — leftover from a removed log call. Either re-add the log or delete the branch. | dead-code |
| 18 | [RTSInputManager.cs:109-116](Assets/Scripts/Input/RTSInputManager.cs#L109) | Observer guard suppresses `HandleHotkeys()` and `HandleRightClick()` but lets Ctrl-1..9 control-group save/load through. Either block them or document them as deliberate. `[unverified]` — agent flagged, confirm against current `ControlGroupSystem`. | inconsistency |

### Low

| # | File:Line | Finding | Category |
|---|-----------|---------|----------|
| 19 | [SimpleAISystem.cs:813-816](Assets/Scripts/AI/SimpleAISystem.cs#L813) | `MaxCrystalMiners = 16` cap means a faction with 100 miners stays at 16% crystal, not 50/50. Acceptable upper bound for Age-1 but worth raising or removing once economy scales further. | inconsistency |
| 20 | [SimpleAISystem.cs:104-108](Assets/Scripts/AI/SimpleAISystem.cs#L104) | When `StepIndex >= buildOrder.Length` the AI silently sits idle. No "post-build-order" behaviour (defend, expand, attack-loop) — important once Age-2 build orders exist. | missing-impl |
| 21 | [CrystalMiningSystem.cs:82](Assets/Scripts/Systems/Work/CrystalMiningSystem.cs#L82) / [MiningSystem.cs:85](Assets/Scripts/Systems/Work/MiningSystem.cs#L85) | Both filter on `GatheringResource == 0/1` exclusively. A future third resource type would silently skip both systems — defensive `else` log would help. | missing-impl |
| 22 | [MovementSystem.cs:35,52](Assets/Scripts/Systems/Movement/MovementSystem.cs#L52) | Struct is declared as if Burst-ready (`partial struct ISystem`, `OnCreate` is `[BurstCompile]`) but `OnUpdate` has no attribute and uses managed `FlowFieldManager.Instance`, `em.HasComponent`, etc. The "future Burst" comment is misleading. Either drop the attempt or split the hot loop. | outdated-pattern |
| 23 | [FlowFieldMovementHelper.cs:96-103](Assets/Scripts/Systems/Movement/FlowFieldMovementHelper.cs#L96) | Private static `SampleManaged` is never called. Drop. | dead-code |
| 24 | [AIBuildOrder.cs:251-260](Assets/Scripts/AI/AIBuildOrder.cs#L251) | `For()` keeps `Aggressive → Balanced` and `TechRush → TechBoom` as legacy aliases. If the enum stays this way for another release we should drop the duplicates from the lobby UI. | outdated-pattern |
| 25 | [AIBuildOrder.cs:262-267](Assets/Scripts/AI/AIBuildOrder.cs#L262) | `CulturePicks[randomSeed % 3]` uses an unbounded `randomSeed` — works because of modulo wraparound, but a more obvious `(int)(randomSeed % 3u)` reads better. | nitpick |

### False positives (agent flagged, not actually bugs)

| # | Claim | Reality |
|---|-------|---------|
| FP-1 | `FlowFieldMovementHelper.GetDirection` "missing null guard, NRE risk" | Line 57 already does `if (field == null) return directDir;` — the guard exists. |
| FP-2 | "GameBootstrap empty else-if = malformed conditional" | Already noted as #17 above (low-severity dead-code, not malformed). Dedup. |

## Notes for the next agent in the pipeline

- The three Critical bugs (#1, #2, #3) are mechanically identical to fixes already shipped this session — same missing-`else`/missing-braces pattern. A single small task can knock all three out.
- Findings #4 (cargo-cult AIBootstrap init) is the highest-leverage cleanup — touches every AI brain spawn and removes ~50 lines of dead initialization.
- Skip findings marked `[unverified]` until someone has confirmed them against the current code — the agent batched a few and was wrong about FP-1.

## Out of Scope

- Performance optimization beyond removing dead component init
- Any rewrite of the legacy `AIEconomyManager`/`AIMilitaryManager` files (they're already `[DisableAutoCreation]` — leave them as reference until the SimpleAISystem matures)
- UI Toolkit migration (covered separately by the UI harmonize tasks)
