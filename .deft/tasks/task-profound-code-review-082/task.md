---
deft:
  id: task-profound-code-review-082
  type: task
  status: completed
  stage: release
  phase: 4
  total_phases: 4
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Profound codebase review — architecture, design drift, correctness, performance

## Audit Summary

**Status:** completed 2026-05-20. All nine acceptance criteria pass; review verdict `approve`.

**Section row counts (HEAD `2ad11c1`):**

- §A Architecture & ECS hygiene — **19 rows**.
- §B Code-vs-Design drift — **76 rows** across six subsections (B.1 Overview, B.2 Tech_Tree, B.3 Age_0, B.4 Alanthor, B.5 Runai, B.6 Feraldis). The phase-2 completion note in `state.json` recorded 53; the audit went deeper than tracked.
- §C Correctness & bug surface — **24 finding rows** + a 247-component save/load enumeration across 23 component files (every declaration tagged `MISSING` pending task-081).
- §D Performance hot spots — **18 rows** (7 `needs-measurement`, 11 `n/a — static analysis`, 0 measured).

**Child tasks spun out (25 total, range 083–107):**

- §A (8): [083](../task-hudbridge-sect-rail-wiring-083/task.md) HudBridge sect rail · [084](../task-hudbridge-military-action-targeting-084/task.md) HudBridge military targeting · [085](../task-ai-managers-removal-085/task.md) AI managers removal · [086](../task-combat-system-namespace-cohesion-086/task.md) namespace cohesion · [087](../task-commandrouter-lockstep-drift-087/task.md) CommandRouter lockstep drift · [088](../task-ai-endgame-runai-feraldis-088/task.md) AI endgame Runai/Feraldis · [089](../task-ecs-query-caching-hot-systems-089/task.md) ECS query caching · [090](../task-hudbridge-query-consolidation-090/task.md) HudBridge query consolidation.
- §B (5 new + 23 cross-refs): [091](../task-buildables-hashset-completeness-091/task.md) buildables hashset · [092](../task-alanthor-crucible-cost-fix-092/task.md) Crucible cost fix · [093](../task-feraldis-beastpen-doc-or-drop-093/task.md) BeastPen doc-or-drop · [094](../task-glow-once-per-node-gate-094/task.md) Glow once-per-node gate · [095](../task-feraldis-hut-terrain-yields-095/task.md) Feraldis hut terrain yields. Remaining §B rows cross-ref existing alignment tasks **065–080**.
- §C (7): [096](../task-save-load-coverage-gap-096/task.md) save/load coverage gate · [097](../task-commandrouter-placebuilding-validation-097/task.md) PlaceBuilding validation · [098](../task-clearallcommands-bazaar-coverage-098/task.md) ClearAllCommands bazaar coverage · [099](../task-lockstep-command-index-serialization-099/task.md) lockstep CommandIndex · [100](../task-lockstep-tick-payload-mtu-100/task.md) lockstep MTU · [101](../task-ai-launchattack-target-refresh-101/task.md) AI launchattack target refresh · [102](../task-sect-unit-lever-multiplier-semantics-102/task.md) sect-lever HP semantics.
- §D (5): [103](../task-perf-measurement-backlog-103/task.md) perf measurement backlog · [104](../task-presentation-sync-component-cache-104/task.md) PresentationSpawn component cache · [105](../task-fog-visibility-sync-perf-105/task.md) FogVisibilitySync perf · [106](../task-hudbridge-push-cadence-tiers-106/task.md) HudBridge push cadence · [107](../task-static-helper-query-caching-107/task.md) static helper query caching.

**Top findings per section:**

- **§A** — (1) AI managers (`AIManagerComponents`, `AIScoutingComponents`) are dead scaffolding — `AIBrain` orchestrates everything directly (→ 085). (2) `CommandRouter.LockstepQueue` partial drifts from main router for unit ability / sect-lever paths (→ 087). (3) Hot systems (BuildingCombat, AIEconomy) rebuild queries every tick instead of caching in `OnCreate` (→ 089).
- **§B** — (1) Per-battalion upgrade mechanic completely absent and all 4-tier weapon / arrow / tools ladders missing from `TechTree.json` (covered by 065/076/079). (2) Hut age-up transformations (wagon-burst / wall-anchor / raider-spawn) unimplemented (covered by 066/067). (3) Vault interest model is `3 %` flat in code vs the design's `25/50/75/100 %` banking-tier ladder (covered by 065).
- **§C** — (1) `LockstepCommand.Serialize` drops the `CommandIndex` field — silent multiplayer **desync** on multi-command ticks (→ 099, critical). (2) `CommandRouter.IssuePlaceBuilding` bypasses position + building-id validation — non-UI callers can spawn on water / cliff or instantiate placeholder building IDs (→ 097, critical). (3) `AIBrain.TryLaunchAttack` doesn't refresh the target after death — attack waves wander toward dead anchors (→ 101).
- **§D** — (1) `PresentationSpawnSystem.SyncTransforms` issues **~72 000 `GetComponent` calls/sec** for visual placement (→ 104). (2) `FogVisibilitySyncSystem` does per-frame `FindFirstObjectByType` + 3 uncached queries + per-entity `MaterialPropertyBlock` alloc (~24 000 MPB/sec) (→ 105). (3) `BuildingCombatSystem` per-tick query + per-building O(N) target scan with per-building `NativeList` alloc (cross-ref 089).

**Cross-referenced existing alignment tasks:** Age 0 (065), Hut age-up (066), Feraldis raiders (067), Royal Stable (068), Runai buildings split (069), Veilsteel/frenzy (070), cultured rename layer (071), trader warriors (072), rejected stat overrides (073 — completed), religious-unit tier (074), caravan loot Feraldis-only (075 — completed), per-battalion upgrades (076), Runai curse neutrality (078), Age 1 unit tier ladders (079), Feraldis instant pop spike (080 — completed).

**Biggest single follow-up:** [task-096 save/load coverage gate](../task-save-load-coverage-gap-096/task.md). All 247 component declarations across 23 files in `Core/Components/` are tagged `MISSING` because the snapshot writer / reader from task-081 has not landed yet. Until 081 ships, every `IComponentData` and `IBufferElementData` is presumed un-serialized; once it lands, 096 re-anchors §C.3 against the actual writer/reader and graduates each row to `serialized` / `derived` / `MISSING` / `excluded-intentionally`.

## Context

The `test/all-fixes-rolled-up` branch (HEAD `2ad11c1`) has absorbed a long
sequence of feature landings since the last audit:
[task-codebase-audit-064](../task-codebase-audit-064/task.md) was the most
recent project-wide pass but was deliberately scoped to **dead code and
unfinished scaffolding only** — it did not look at architecture coherence,
design-document drift, correctness/bug surface, or performance.

Since 064 closed, the following has landed: a save/load pipeline
([task-save-load-system-081](../task-save-load-system-081/task.md)), six
to nine rolled-up feature/fix patches (jade theme, scout/AI worker/camera
work, end-game, curse forests, Litharch, affordability, menu mirror, etc.),
plus a swath of sect/religion/Petriarchy changes. The Design folder
([docs/Design/](../../../docs/Design/Overview.md)) was rewritten as the
canonical truth source between 064 and now, and only Age 0 has an active
alignment task — Age 1 (Alanthor / Runai / Feraldis) is unverified against
code.

A fresh, broader audit is needed to produce a triageable inventory across
**four axes** before any of the open follow-up tasks (065, 066, 067, 069,
070, 071, 072, 073, 074, 075, 076, 077, 078, 079, 080) re-discover the
same baseline drift, hazards, or hot spots.

This is an **audit-only** task. No production code is edited under it.
Concrete fixes are spun out as **child tasks** per finding, mirroring how
064 produced 069/068/070 etc.

## User Value

A single triageable inventory across four axes —
**architecture, design drift, correctness, performance** — that
downstream cleanup, realignment, and optimization tasks can scope against
without each one re-discovering the same baseline. Every finding carries a
file:line anchor and a triage column (`drop` / `wire` / `fix` / `park` /
`spin-out`) so the human can resolve the backlog in one pass instead of
across a dozen sub-audits.

## Requirements

- R1: Architecture & ECS hygiene catalogued — namespace cohesion (global
  ECS vs `TheWaningBorder.*` managed), module boundary integrity
  (Core / Systems / Entities / UI / Input / AI / Economy), ECS pattern
  adherence (SystemBase vs ISystem choice,
  `[UpdateInGroup]` / `[UpdateAfter]` ordering, IJobEntity vs
  `Entities.ForEach`, `SystemAPI.QueryBuilder` query reuse), hybrid
  MonoBehaviour ↔ ECS seam quality, `CommandRouter` flow correctness,
  AI brain orchestration coherence.
- R2: Code-vs-Design drift catalogued. Every doc in
  [docs/Design/](../../../docs/Design/Overview.md) (Overview,
  Tech_Tree, Age_0, Age_1_Alanthor, Age_1_Runai, Age_1_Feraldis) walked
  against actual code. Each divergence (units missing, costs wrong,
  tech-tree edges off, age-up transformations, Glow rules, per-battalion
  upgrades, religious-unit tier, Petriarchy framing) recorded with a
  cross-reference to the existing alignment task
  [task-age0-techtree-alignment-065](../task-age0-techtree-alignment-065/task.md)
  so coverage is not duplicated.
- R3: Correctness & bug surface catalogued — latent null/ref hazards
  (`EntityManager.GetComponentData` without `HasComponent`), ECS
  structural-change ordering (`EntityCommandBuffer` not playback-ordered),
  command-router edge cases (right-click on dead entity, miners on
  depleted node, build placement on bad tile), save/load round-trip
  integrity now that [task-save-load-system-081](../task-save-load-system-081/task.md)
  has landed (verify component coverage), AI brain stall conditions
  (worker assignment loops, dead-target queues), multiplayer/lockstep
  determinism gaps if any.
- R4: Performance hot spots catalogued — per-frame allocations in
  `Update()` (LINQ, `ToArray()` on queries, string concat), SystemBase
  vs IJobEntity choice for hot systems (Combat, Targeting, Visibility,
  Movement, Mining), query thrash (same query rebuilt each frame),
  Presentation/Visibility chunk-iteration cost, UI bridge chatter (HudBridge
  push frequency, JSON marshalling), pathfinding cost and reachability
  cache lifetime.
- R5: **No production code edits** under this task. Concrete fixes spin
  out as child tasks — one per finding category (or one per finding for
  high-severity rows). Implementation stage of 082 only writes findings
  into `task.md` and creates child task stubs.

## Acceptance Criteria

- [x] `task.md` body contains four findings sections — `§A Architecture
      & ECS hygiene`, `§B Code-vs-Design drift`, `§C Correctness & bug
      surface`, `§D Performance hot spots` — each populated with at least
      one row.
- [x] Every finding row carries a file-and-line anchor in the form
      `Assets/Scripts/.../File.cs#LNNN` (or `docs/Design/<doc>.md` /
      `Assets/Resources/TechTree.json` for design-side anchors).
- [x] Every finding row carries a triage column with one of:
      `drop` / `wire` / `fix` / `park` / `spin-out`.
- [x] §B explicitly cross-references task-065 for every Age 0 row it
      records, so the human can see at a glance which rows are already
      covered vs new.
- [x] §C save/load coverage is verified component-by-component against
      the components registered in
      [Assets/Scripts/Core/Components/](../../../Assets/Scripts/Core/Components/)
      and the snapshot writer / reader landed under task-081. Missing
      components are listed by name + file.
- [x] §D performance rows include either a measured cost (frame ms, alloc
      count) or a clear "needs measurement" tag — no hand-waving rows.
- [x] At the end of each section, a triage summary lists which rows
      become **`Fix soon`** or **`Wire or decide`** child tasks vs
      **`Park`** / **`Drop`**.
- [x] Child task stubs (`.deft/tasks/task-<slug>-<id>/`) are created for
      every `Fix soon` and `Wire or decide` row, with their `task.md`
      `Context` pointing back to the parent finding row in 082.
- [x] No `git diff` outside of `.deft/tasks/task-profound-code-review-082/`
      and the newly created child task directories. Specifically: no
      changes under `Assets/Scripts/`, `docs/Design/`, `HudFrontend/`,
      `ProjectSettings/`.

## Implementation Phases

### Phase 1: Architecture & ECS hygiene audit
**Scope:** Walk the codebase looking at structural cohesion — namespaces,
module boundaries, ECS pattern adherence, hybrid seam, command flow, AI
brain orchestration. Produce §A in `task.md` with file:line anchors and
triage column. Spin out child tasks for `Fix soon` and `Wire or decide`
rows.
**Files (audited, not edited):**
- `Assets/Scripts/Core/Components/` — namespace check on every component
- `Assets/Scripts/Core/Commands/CommandRouter.cs` and `CommandRouter.LockstepQueue.cs`
- `Assets/Scripts/Systems/` — every system file (101+ per project profile)
  with focus on `[UpdateInGroup]` / `[UpdateAfter]` annotations,
  SystemBase vs ISystem choice, `SystemAPI.QueryBuilder` use, IJobEntity
  vs `Entities.ForEach`
- `Assets/Scripts/AI/Core/AIBrain.cs`, `AI/Managers/*`, `AI/Behaviors/*`
- `Assets/Scripts/UI/Web/HudBridge.cs`, `UI/Web/HudWebController.cs` —
  hybrid seam
- `Assets/Scripts/Input/RTSInputManager.cs`, `Input/SelectionSystem.cs`
- `Assets/Scripts/Presentation/PresentationSpawnSystem*.cs`
**Verification:**
- [ ] §A in `task.md` populated with rows for namespace, module boundary,
      ECS pattern, hybrid seam, command flow, AI orchestration
- [ ] Every row has file:line + triage column
- [ ] Child tasks spun out for `Fix soon` and `Wire or decide` rows
**Estimated effort:** Large

### Phase 2: Code-vs-Design drift audit
**Scope:** Walk every doc in [docs/Design/](../../../docs/Design/Overview.md)
against the actual code that implements (or fails to implement) it.
Catalogue every divergence in §B with cross-references to task-065 so we
don't double-up its Age 0 backlog. Spin out child tasks for new
divergences not already covered by 065 / 066 / 067 / 069 / 070 / 071 /
072 / 073 / 074 / 075 / 076 / 077 / 078 / 079 / 080.
**Files (audited, not edited):**
- `docs/Design/Overview.md` (two-age structure, movement axis, Glow,
  religious-unit tier, per-battalion upgrades, Petriarchy framing,
  caravan-death rule, population model)
- `docs/Design/Tech_Tree.md` (Mermaid edges — buildings, units, techs)
- `docs/Design/Age_0.md`, `Age_1_Alanthor.md`, `Age_1_Runai.md`,
  `Age_1_Feraldis.md`
- `Assets/Resources/TechTree.json` (canonical content data)
- `Assets/Scripts/Data/TechTree/TechTreeDB.cs`, `BuildingCosts.cs`
- `Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs`,
  `CultureConfig.cs`, `FactionColors.cs`
- `Assets/Scripts/Entities/Buildings/BuildingFactory.cs`,
  `Entities/Units/UnitFactory.cs` and per-unit / per-building helpers
- `Assets/Scripts/Systems/Economy/*`, `Systems/Work/AgeUpSystem.cs`,
  `Systems/Research/*`, `Systems/Combat/EquipmentTierSystem.cs`
- `Assets/Scripts/Economy/SectEffectSystem.cs` and `Systems/Sect/*`
**Verification:**
- [ ] §B in `task.md` populated with one subsection per design doc
- [ ] Every divergence row has file:line anchor on the code side AND a
      doc anchor (e.g. `docs/Design/Age_1_Runai.md` §heading)
- [ ] Every Age 0 row carries a `(covered by task-065)` /
      `(NEW — not in task-065)` tag
- [ ] Child tasks spun out for divergences not covered by an existing
      alignment task
**Estimated effort:** Large

### Phase 3: Correctness & bug surface audit
**Scope:** Walk the simulation code looking for latent hazards —
null/ref without `HasComponent` guards, ECS structural-change ordering
problems with EntityCommandBuffer playback, command-router edge cases,
save/load component-coverage gaps, AI brain stall conditions,
lockstep/multiplayer determinism gaps. Each finding gets a one-line
reproduction (or "static analysis only — needs play test") and a triage
column.
**Files (audited, not edited):**
- `Assets/Scripts/Core/Commands/CommandRouter.cs` (right-click on dead
  entity, miners on depleted node, build on bad tile)
- `Assets/Scripts/Systems/Work/MiningSystem.cs`,
  `CrystalMiningSystem.cs`, `BuildingConstructionSystem.cs`
- `Assets/Scripts/Systems/Combat/TargetingSystem.cs`,
  `MeleeCombatSystem.cs`, `RangedCombatSystem.cs`,
  `ProjectileSystem.cs`, `DeathSystem.cs`
- `Assets/Scripts/Systems/Movement/MovementSystem.cs`,
  `BattalionSyncSystem.cs`, `BattalionLeashSystem.cs`,
  `BattalionFormationHelpers.cs`, `BattalionCombatHelpers.cs`
- `Assets/Scripts/AI/Core/AIBrain.cs`, `AI/Managers/AIEconomyManager.cs`,
  `AIMilitaryManager.cs`, `AIBuildingManager.cs`,
  `AIMissionManager.cs`, `AITacticalManager.cs`,
  `AI/SimpleAISystem.cs`
- `Assets/Scripts/Core/Components/` (component archetype review for
  save/load coverage)
- save/load entry points landed under task-081 (snapshot writer /
  reader files — to be located during phase)
- `Assets/Scripts/Core/Multiplayer/`, lockstep command queue partial
  (`CommandRouter.LockstepQueue.cs`)
**Verification:**
- [ ] §C in `task.md` populated with at minimum: null/ref hazards,
      structural-change ordering, command-router edges, save/load
      component coverage, AI stall conditions, lockstep determinism
- [ ] Save/load coverage row enumerates every `IComponentData` /
      `IBufferElementData` / `ISharedComponentData` in
      `Assets/Scripts/Core/Components/` and marks each as
      `serialized` / `derived` / `MISSING` / `excluded-intentionally`
- [ ] Every row has file:line and triage column
- [ ] Child tasks spun out for `Fix soon` and `Wire or decide` rows
**Estimated effort:** Large

### Phase 4: Performance hot spots audit
**Scope:** Walk update paths for per-frame allocations, query thrash,
SystemBase-when-IJobEntity-would-fit, presentation/visibility
chunk-iteration cost, HudBridge push frequency / JSON marshalling, and
pathfinding cost / reachability cache lifetime. Either attach a measured
cost (frame ms, alloc count) or tag the row `needs-measurement` —
hand-waving rows are not acceptable per AC.
**Files (audited, not edited):**
- `Assets/Scripts/Systems/Combat/TargetingSystem.cs`,
  `MeleeCombatSystem.cs`, `RangedCombatSystem.cs`,
  `ProjectileSystem.cs`
- `Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs`
- `Assets/Scripts/Systems/Movement/MovementSystem.cs`,
  `FlowField*.cs`, `AStar*.cs`, `NavMeshPathRequestSystem.cs`,
  `NavMeshManager.cs`, `UnitSeparationSystem.cs`,
  `BattalionSyncSystem.cs`
- `Assets/Scripts/Systems/Work/MiningSystem.cs`,
  `CrystalMiningSystem.cs`
- `Assets/Scripts/Presentation/PresentationSpawnSystem*.cs`,
  `EntityViewManager.cs`, `BuildingPrefabSwapSystem.cs`,
  `BuildingEffectSystem.cs`
- `Assets/Scripts/UI/Web/HudBridge.cs` (push frequency, marshalling)
- `Assets/Scripts/World/Terrain/PassabilityGrid.cs`,
  `World/Terrain/ProceduralTerrain.cs`
- `Assets/Scripts/UI/Panels/EntityActionPanel.cs`,
  `EntityInfoPanel.cs`, `BuildCommandPannel.cs` (IMGUI per-frame
  alloc patterns)
**Verification:**
- [ ] §D in `task.md` populated with rows for per-frame alloc,
      SystemBase-vs-IJobEntity, query thrash, presentation /
      visibility cost, HudBridge chatter, pathfinding cache
- [ ] Every row has a measured cost OR `needs-measurement` tag (no
      hand-waving)
- [ ] Every row has file:line and triage column
- [ ] Child tasks spun out for `Fix soon` and `Wire or decide` rows
**Estimated effort:** Medium

## Edge Cases

- **Phase overlap with task-065**: §B Age 0 rows will heavily overlap
  with the open backlog in
  [task-age0-techtree-alignment-065](../task-age0-techtree-alignment-065/task.md).
  Resolution: tag each row `(covered by task-065)` and only spin out a
  new child task if the row is NOT in 065's existing R1–R10 list.
- **Phase overlap with other Age 1 tasks**: §B Age 1 rows must check
  against 067 (Feraldis raiders), 069 (Runai buildings), 070 (Veilsteel),
  071 (cultured rename), 072 (trader warriors), 074 (religious tier),
  075 (caravan loot), 076 (per-battalion upgrades), 078 (Runai curse
  neutrality), 079 (Age 1 tier ladders), 080 (Feraldis pop spike) and
  066 (age-up Hut transform). Same `(covered by task-NNN)` tag rule.
- **Save/load component coverage**: if task-081 reached release before
  082's implementation phase starts, the snapshot writer is canonical
  and §C row enumerates against it. If 081 is still mid-implementation,
  §C records the gap as-of the snapshot landed on
  `test/all-fixes-rolled-up` HEAD and flags rows likely to shift.
- **Measurement-bound performance rows**: rows tagged
  `needs-measurement` may not graduate to child tasks immediately — they
  go into a "measurement backlog" child task instead of one task per row.
- **Pre-existing TODO markers**: §A / §C may re-surface markers already
  catalogued in task-064 (SECTS-BINDING-TODO at HudBridge:21, 122, 295).
  Reference 064's row rather than duplicating.
- **Dead findings**: if a finding turns out on inspection to be a false
  alarm (e.g. a `GetComponentData` is actually guarded one frame up the
  stack), record as `park` with the one-line justification — do NOT
  silently drop.

## Dependencies

- [task-codebase-audit-064](../task-codebase-audit-064/task.md) — prior
  audit, dead-code-only scope. §A / §C cross-reference its findings.
- [task-age0-techtree-alignment-065](../task-age0-techtree-alignment-065/task.md)
  — Age 0 alignment backlog. §B Age 0 rows tag against it.
- [task-save-load-system-081](../task-save-load-system-081/task.md) —
  the snapshot pipeline 082's §C verifies component coverage against.
- The other open Age 1 / sect / drift tasks (066, 067, 069, 070, 071,
  072, 073, 074, 075, 076, 077, 078, 079, 080) — §B Age 1 rows tag
  against them.

## Technical Notes

- **Output format**: each finding section uses the same table shape as
  task-064: `| Surface | Item | Issue | File:line | Triage |`. This
  keeps the inventory grep-able and consistent across audits.
- **Triage vocabulary** (one of):
  - `drop` — confirmed dead, safe to remove now
  - `wire` — UI surface exists but invoke path missing
  - `fix` — bug or drift, needs concrete code change
  - `park` — intentionally dormant or defensive fallback
  - `spin-out` — already has or will get its own child task
- **Severity** is implicit in the suggested triage section at the end of
  each finding section (Drop now / Wire or decide / Fix soon / Park),
  mirroring 064's structure.
- **Static analysis vs runtime**: §C rows that cannot be confirmed
  without play-test get the `static-only — needs repro` flag in the
  Issue column.
- **Memory recall**: the consolidated memory in
  [.deft/memory/decisions.md](../../memory/decisions.md) records prior
  ECS patterns (RefRW direct-write, delta multipliers, NetworkIdGenerator
  partitioning, F2 vs R serialization). §A / §C should grep for these
  anti-patterns rather than re-discovering them.
- **Source line stability**: anchors use HEAD `2ad11c1` line numbers.
  If the branch advances mid-audit, re-anchor at audit completion.

## Report Schema

This section defines the **output schema** the implementation stage must
follow when populating §A–§D in this file. It is a contract, not
guidance. The implementer fills tables; they do not redesign them.

### Table shape

All four sections share the **task-064 baseline** —
`| Surface | Item | Issue | File:line | Triage |` — and each extends it
with the columns below. `Surface` is the module / file group; `Item` is
the specific symbol / row / mechanic; `Issue` is one sentence; the last
two columns are mandatory.

**§A Architecture & ECS hygiene** — baseline only.

| Surface | Item | Issue | File:line | Triage |

**§B Code-vs-Design drift** — extends with `Doc-ref` (which Design doc
& line the divergence is measured against) and `Cross-ref task` (existing
alignment task ID, or `NEW` when not yet covered).

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |

**§C Correctness & bug surface** — extends with `Severity` taking one of
`crash` (will throw / corrupt), `wrong-result` (silently incorrect),
`degraded` (works but stalls / leaks), `edge-only` (rare repro path).

| Surface | Item | Issue | File:line | Severity | Triage |

**§D Performance hot spots** — extends with `Measured?` taking either a
concrete cost (`measured: 3.2 ms`, `measured: 412 allocs/frame`) or the
literal tag `needs-measurement`. Hand-wave rows fail AC.

| Surface | Item | Issue | File:line | Measured? | Triage |

### Triage column vocabulary

Exactly five values — same set across all four sections, same as the
scope-stage decision in `state.json`:

- `drop` — confirmed dead / stale, safe to remove now with zero risk.
- `wire` — surface (UI, command path, doc reference) exists but the
  invoke / implementation path is missing; needs hooking up.
- `fix` — concrete bug or drift; needs a real code change.
- `park` — intentionally dormant, defensive fallback, or false alarm
  on inspection (record the one-line justification — do **not** silently
  drop per Edge Cases).
- `spin-out` — already has or will get its own child task; finding
  recorded here for cross-reference only.

### Anchor format

File anchors are **VS Code clickable links** from this task's directory.
That means relative paths back up three levels to repo root.

- **C# / JSON source:** `[File.cs:NNN](../../../Assets/Scripts/.../File.cs#LNNN)`
  for single-line, or `:NNN-MMM` with `#LNNN` for ranges.
- **Design docs:** `[Overview.md §heading](../../../docs/Design/Overview.md#L120)`
  — anchor the closest line, name the heading in the link text.
- **TechTree.json (dynamic content):** `[TechTree.json entries[Runai_Vault]](../../../Assets/Resources/TechTree.json)`
  — **no line number** (the file is regenerated); use a JSON key path
  in the link text (`entries[Foo]`, `units[Bar].cost`) so the row is
  still grep-able.
- **Cross-references to other tasks:** `[task-065](../task-age0-techtree-alignment-065/task.md)`.

### Per-section triage summary

Each of §A, §B, §C, §D must end with a `### Triage summary` sub-section
listing finding rows under exactly four buckets, mirroring task-064:

```
### Triage summary
**Drop now (zero-risk):**
1. <row reference> — <one line>

**Wire or decide:**
1. <row reference> — <one line>

**Fix soon:**
1. <row reference> — <one line>

**Park (intentionally dormant):**
1. <row reference> — <one line>
```

Row reference is `§X.row N` or the `Item` cell value verbatim, whichever
disambiguates faster.

### Child task stub format

For every row landing in `Fix soon` or `Wire or decide`, the
implementation stage creates a stub at
`.deft/tasks/task-<slug>-<NNN>/` with a minimal `task.md` that:

1. **Links back to the parent finding row** in this file:
   `Spun out from [task-082 §X row N](../task-profound-code-review-082/task.md#§X-<anchor>).`
2. **Inherits the file:line anchor** from the parent row verbatim — same
   VS Code clickable link, same line range.
3. **Carries a `type:` front-matter field** matching the finding:
   - `type: bug` — §C rows with `Severity: crash` / `wrong-result`, and
     any §A / §B / §D row that describes a defect.
   - `type: improvement` — §A / §D rows that describe a non-defect
     refactor, optimization, or pattern alignment.
   - `type: task` — §B drift rows and general realignment work.
4. **Starts in `scope` stage** (`stage: scope` in front-matter,
   `status: ready` in `state.json`). Implementation work does **not**
   auto-start — the human reviews each stub before advancing it to
   `active` and running it through the agent pipeline.

### Example rows

One plausible-but-fictional row per section so the implementer has a
concrete template. **(EXAMPLE — REMOVE BEFORE IMPLEMENTATION)** on each.

**§A example:**

| Surface | Item | Issue | File:line | Triage |
|---|---|---|---|---|
| Systems/Combat | `MeleeCombatSystem` | **(EXAMPLE — REMOVE BEFORE IMPLEMENTATION)** Inherits `SystemBase` but body is a single `IJobEntity.ScheduleParallel` with no managed dependencies — ISystem fits and removes one main-thread sync | [MeleeCombatSystem.cs:38](../../../Assets/Scripts/Systems/Combat/MeleeCombatSystem.cs#L38) | spin-out |

**§B example:**

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Data/TechTree | `Runai_Vault.cost` | **(EXAMPLE — REMOVE BEFORE IMPLEMENTATION)** Code costs `200 supplies / 80 stone`; design specifies `150 supplies / 60 stone / 1 Glow`. Glow cost not modelled in code at all. | [TechTree.json entries[Runai_Vault]](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Vault](../../../docs/Design/Age_1_Runai.md#L142) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |

**§C example:**

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| Systems/Work | `BuildingConstructionSystem` | **(EXAMPLE — REMOVE BEFORE IMPLEMENTATION)** EntityCommandBuffer playback ordered before `AddComponent<BuildingFinishedTag>` on same entity in `PresentationSpawnSystem` — first frame the prefab swap can race the tag. `static-only — needs repro` | [BuildingConstructionSystem.cs:214](../../../Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs#L214) | wrong-result | fix |

**§D example:**

| Surface | Item | Issue | File:line | Measured? | Triage |
|---|---|---|---|---|---|
| Systems/Combat | `TargetingSystem.FindNearestEnemy` | **(EXAMPLE — REMOVE BEFORE IMPLEMENTATION)** `query.ToEntityArray(Allocator.TempJob).ToArray()` per attacker per frame — N×M LINQ allocation in main-thread path | [TargetingSystem.cs:88](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L88) | needs-measurement | fix |

## Technical Approach

This is an **audit**, so the technical approach is "how to walk the
codebase systematically without missing anything or doing the same
search twice". No code architecture decisions are needed — only a
disciplined sweep order, a fixed grep recipe per phase, and clear
boundaries on what to skim vs read end-to-end.

### Phase 1: Architecture & ECS hygiene — technical approach

- **Tools:** Glob to enumerate `Assets/Scripts/Systems/**/*.cs` and
  `Assets/Scripts/Core/Components/**/*.cs`; Grep for the patterns below;
  Read end-to-end only on seam files (`CommandRouter.cs`,
  `CommandRouter.LockstepQueue.cs`, `HudBridge.cs`, `AIBrain.cs`,
  `PresentationSpawnSystem*.cs`).
- **Specific greps to run:**
  - `(class|struct)\s+\w+\s*:\s*(SystemBase|ISystem)` — enumerate every
    system, tag SystemBase-vs-ISystem on each row.
  - `\[UpdateInGroup\(` and `\[UpdateAfter\(` / `\[UpdateBefore\(` —
    verify every system has an ordering anchor; orphan systems = §A row.
  - `Entities\.ForEach` vs `IJobEntity` — count both, flag any
    `Entities.ForEach` in a chunk-iteration hot path.
  - `SystemAPI\.QueryBuilder` — confirm caching in `OnCreate`, not
    rebuilt per `OnUpdate`.
  - `namespace TheWaningBorder` and (separately) `^namespace ` over
    `Assets/Scripts/Core/Components/**` — components must be global
    namespace per `.deft/memory/project-facts.md`. Any
    `TheWaningBorder.*` namespace under `Core/Components/` is a §A row.
  - `using TheWaningBorder\.` inside files under `Core/Components/` —
    managed/data leak smell.
  - `EntityManager\.GetComponent(Data)?` — group hits per-file, then
    Read those files and check for `HasComponent` within ~5 lines above.
- **Walk order:** components (data) → commands (`CommandRouter*`) →
  systems by domain (Work, Combat, Movement, AI) → seam (`HudBridge`,
  `Presentation*`) → orchestration (`AIBrain` + managers). Data first,
  orchestration last.
- **Anti-patterns to flag from memory** (`.deft/memory/decisions.md`,
  `mistakes.md`): RefRW copy-without-writeback, mid-frame structural
  changes, `F2` float-format in lockstep commands, sequential counters
  without tick partitioning, full-multiplier-on-recompute (delta
  pattern), main-thread-only helpers with misleading locks.

### Phase 2: Code-vs-Design drift — technical approach

- **Tools:** Read each Design doc in full (six files). Cross-reference
  against `Assets/Resources/TechTree.json` (Read in chunks — large
  file), per-building / per-unit factories under
  `Assets/Scripts/Entities/`, `BuildableBuildings` HashSet in
  `EntityExtractors.cs`, `BuildingUpgradeConfig.TryGetCost` cases,
  `CultureConfig.cs`, `TechTreeDB.cs`.
- **Walk order:** `Overview.md` first (two-age structure, Glow,
  religious tier, Petriarchy, caravan-death, per-battalion upgrades,
  population) → `Tech_Tree.md` (walk one Mermaid edge at a time, mark
  each present/absent in TechTree.json) → `Age_0.md` (cross-ref
  task-065 backlog R1–R10 — most rows will already be there) →
  `Age_1_Alanthor.md` → `Age_1_Runai.md` → `Age_1_Feraldis.md`.
- **Per-row check:** for each unit / building / tech in the design doc,
  ask (1) does it have a TechTree.json entry? (2) is there a factory
  file under `Entities/Buildings/` or `Entities/Units/`? (3) is the
  building in `BuildableBuildings`? (4) is the unit listed in some
  building's `trains` array? (5) do costs / stats match exactly? (6)
  are documented mechanics (transformations, sect framing,
  per-battalion upgrade pattern, religious-tier framing) implemented?
- **De-dupe rule:** if a row is already covered by an open child task
  (065 / 066 / 067 / 069 / 070 / 071 / 072 / 073 / 074 / 075 / 076 /
  077 / 078 / 079 / 080), record the row in §B with the `Cross-ref
  task` column populated and **do not spin out a new child task**. Only
  rows tagged `NEW` graduate to a fresh stub.
- **Reverse-drift:** if code has something the design doc doesn't, log
  it with `doc-missing — needs spec` in the Issue column and `park` or
  `spin-out` triage — do not write the missing doc here.

### Phase 3: Correctness & bug surface — technical approach

- **Tools:** Grep for the hazard patterns below + Read end-to-end on
  `CommandRouter.cs`, `AIBrain.cs`, every file under `AI/Managers/`,
  the save/load entry points landed under task-081 (locate via Glob on
  `Assets/Scripts/**/Save*.cs` / `*Snapshot*.cs` / `*Persist*.cs`).
- **Hazard patterns:**
  - **Null / ref:** Grep `EntityManager\.(GetComponentData|GetSharedComponent|GetBuffer)`
    with file grouping; for each hit Read ±5 lines and verify a
    `HasComponent` / `Exists` guard precedes it.
  - **ECS structural-change ordering:** Grep `EntityCommandBuffer`
    usage and look for `EntityManager.DestroyEntity` /
    `AddComponent` / `RemoveComponent` inside `Entities.ForEach`
    bodies; flag any ECB without a matching
    `[UpdateInGroup(typeof(EndSimulationEntityCommandBufferSystem))]`
    or similar playback companion.
  - **Save/load round-trip:** Glob
    `Assets/Scripts/Core/Components/*.cs`, Grep
    `IComponentData|IBufferElementData|ISharedComponentData` over the
    glob, dump the resulting list, then read the task-081 snapshot
    writer / reader and mark each component
    `serialized` / `derived` / `MISSING` / `excluded-intentionally`.
    This row is the AC anchor — must be exhaustive.
  - **Command router:** Read `CommandRouter.cs` end to end. For every
    command type, trace right-click-on-dead-entity,
    target-out-of-LOS, target-depleted-node, build-on-bad-tile paths.
    Each missing guard = §C row.
  - **AI brain:** Read `AIBrain.cs` + every file in `AI/Managers/`.
    Look for unbounded queues / lists (`new List<Entity>()` that grows
    forever), missing dead-target cleanup, worker-assignment loops
    with no break condition, missions stuck in pending state.
  - **Lockstep determinism:** Grep `\.ToString\("F[0-9]"` and
    `\.ToString\(\)` over `Core/Commands/`, `Multiplayer/`,
    `Core/Multiplayer/`; flag any non-`R` float serialization (per
    `mistakes.md`). Grep `new Random\(` / `UnityEngine\.Random` and
    confirm only the deterministic RNG path is used in
    simulation-affecting code. Grep `foreach.*Dictionary` for
    iteration-order assumptions.
- **Static-only flag:** any row whose hazard cannot be confirmed
  without play-test gets the `static-only — needs repro` flag per
  Technical Notes.

### Phase 4: Performance hot spots — technical approach

- **Tools:** Grep for allocation patterns + Read for hot systems.
  Frame-cost measurements are out of scope per AC (`needs-measurement`
  tag is the documented escape).
- **Allocation patterns to find:**
  - `\.ToArray\(\)` / `\.ToList\(\)` / `\.ToDictionary\(\)` —
    especially inside any method named `OnUpdate` / `Update`.
  - `\.Where\(` / `\.Select\(` / `\.OrderBy\(` — LINQ inside
    `Update()` / `OnUpdate()` is an allocation per call.
  - `new List<` / `new Dictionary<` / `new HashSet<` —
    scoped to files under `Systems/Combat/`, `Systems/Targeting/`,
    `Systems/Visibility/`, `Systems/Movement/`, `Systems/Work/`,
    `Presentation/`. Flag any inside an update loop.
  - `\$"` (interpolated strings) and `string\.Format` inside update
    loops, especially `HudBridge.cs` push helpers (every JSON push
    allocates a string).
  - `GetComponentDataFromEntity` / `GetComponentLookup` rebuilt each
    frame — must be cached on the SystemBase / ISystem in `OnCreate`,
    refreshed in `OnUpdate` via `Update(ref state)`.
- **System triage:** enumerate every SystemBase under
  `Assets/Scripts/Systems/Combat/`, `Targeting/`, `Visibility/`,
  `Movement/`, `Work/`. For each, answer: should this be IJobEntity?
  Does it iterate many entities? Does it allocate per call? Is the
  query cached? Each "no" = a §D row.
- **HudBridge chatter:** Grep `_lastSend`, `_sendInterval`,
  `Push(`, `JsonUtility\.ToJson` inside `HudBridge.cs`; flag any push
  that fires more than ~10 Hz with no rate-limiting and any
  `JsonUtility.ToJson` on a large payload per tick.
- **Pathfinding:** Read `Movement/FlowField*.cs`, `AStar*.cs`,
  `NavMeshPathRequestSystem.cs`, `PassabilityGrid.cs`. Look for
  reachability cache invalidation on every move, A* without
  ring-buffered open-set, full-grid scans inside `OnUpdate`.
- **`Measured?` column rule:** if a cost is not measured in this pass,
  the row gets `needs-measurement`. **All `needs-measurement` rows
  roll up into one** `task-perf-measurement-needed-NNN` child task,
  **not** one task per row (per scope-stage decision).

### Execution rules across all four phases

- Use the **Explore agent** for breadth queries that would touch 5+
  files at once (e.g. "find every SystemBase that lacks
  `[UpdateInGroup]`"). Keeps the main-context lean.
- Use **Grep** directly for targeted patterns (single regex, single
  glob).
- Use **Read** only for files you need end-to-end (seam files, the
  routers, `AIBrain`). Do NOT Read every system — Grep + glob is
  cheaper.
- Use the **deft skill `/log-event`** after each phase completes to
  append a `phase_completed` event to `state.json`.
- Use the **deft skill `/create-task`** to spin out child task stubs
  for every `Fix soon` and `Wire or decide` row. Each child task
  starts in `stage: scope` (not active) per Report Schema.
- **Do NOT modify** any file under `Assets/Scripts/`, `docs/Design/`,
  `HudFrontend/`, or `ProjectSettings/`. The only writable surface is
  this `task.md` plus newly created `.deft/tasks/task-<slug>-<NNN>/`
  stubs.

### Phase boundaries

- **Phase 1 → Phase 2:** Phase 2 needs the component enumeration Phase
  1 produces while walking `Core/Components/`. Persist intermediate
  findings into §A before starting Phase 2 so the component list is
  reusable.
- **Phase 2 → Phase 3:** Phase 3's save/load coverage row reuses the
  component list from Phase 1, and Phase 2's design-doc walk surfaces
  some correctness rows (e.g. an Age 1 mechanic that's silently
  no-op).
- **Phase 4 runs last** because measurement is the most expensive and
  least value-dense work, and because Phases 1/2/3 already surface
  the obvious hot paths to look at.

## Re-verification — <future-date>

Placeholder. When this audit is re-run in a few months, copy the
task-064 re-verification block shape here: snapshot the original
findings, mark `Resolved since original audit` / `Still applicable`,
and add `Cross-references to newer tasks` for child tasks created
between runs. Schema above stays intact; only the date and rows change.

## §A Architecture & ECS hygiene

Audit performed against HEAD `2ad11c1` on branch `test/all-fixes-rolled-up`.
File anchors are relative to the repo root.

| Surface | Item | Issue | File:line | Triage |
|---|---|---|---|---|
| AI/Managers | `AIEconomyManager`, `AIMilitaryManager`, `AIBuildingManager`, `AIMissionManager`, `AITacticalManager`, `AIStrategyEvaluator`, `AIScoutingBehavior`, `AIDefenseBehavior` | Eight entire AI driver systems are `[DisableAutoCreation]` with "Replaced by SimpleAISystem" comments. They form a parallel dead-orchestration tree (~3.5k LoC) — components (`AIEconomyState`, `AIMilitaryState`, `AIBuildingState`, `AIMissionState`, `AITacticalState`, `AIScoutingState`, `ResourceRequest` buffer, `BuildRequest` / `RecruitmentRequest` buffers in `AIManagerComponents`) are still attached to every brain via `AIBootstrap.CreateAIBrain` but no system reads them. Dead archetype bloat per AI faction. | [AIEconomyManager.cs:17](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L17) | fix |
| AI/Bootstrap | `AIBootstrap.CreateAIBrain` | Still calls `em.AddComponentData` for every dead manager state (`AIEconomyState`, `AIBuildingState`, `AIMilitaryState`, `AIMissionState`, `AITacticalState`, `AISharedKnowledge`, `AIStrategyState`) on every AI brain entity — components attached but never read. Should be reduced to `AIBrain` + `SimpleAIState` + `FactionTag` (+ scouting state if/when ported). | [AIBootstrap.cs:227](../../../Assets/Scripts/Core/Bootstrap/AIBootstrap.cs#L227) | spin-out |
| AI/Endgame | `AIAlanthorEndgameSystem` | Only Alanthor has a culture-specific endgame driver (towers, smelter, sect adoption, veilsteel production). Runai and Feraldis have nothing analogous — they advance through SimpleAISystem build-order then go idle in Age 2. Architecture gap: either generalize the endgame driver or spin per-culture peers. | [AIAlanthorEndgameSystem.cs:76](../../../Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs#L76) | spin-out |
| Systems/Combat (namespace) | `BurningGroundSystem`, `MindControlSystem`, `SummonDespawnSystem` | Three systems under `Assets/Scripts/Systems/Combat/` declare no namespace and live in the global namespace, breaking the `TheWaningBorder.Systems.Combat` cohesion every other Combat system follows. Confuses `[UpdateAfter(typeof(...))]` ordering grep recipes and namespace-import patterns. | [BurningGroundSystem.cs:20](../../../Assets/Scripts/Systems/Combat/BurningGroundSystem.cs#L20) | fix |
| Systems/Core (namespace) | `VictoryConditionSystem` | File lives under `Assets/Scripts/Systems/Core/` but declares `namespace TheWaningBorder.UI.HUD` — module-boundary violation. A simulation-tier system labelled as UI/HUD. Other UI/HUD callers reach it via that namespace, so renaming has callsite ripple. | [VictoryConditionSystem.cs:10](../../../Assets/Scripts/Systems/Core/VictoryConditionSystem.cs#L10) | fix |
| Systems/Work (namespace) | `TempleCascadeDestroySystem` | Declares `namespace TheWaningBorder.Systems.Building` (singular) while every other peer under `Systems/Buildings/` uses `Buildings` (plural). One-letter typo that breaks namespace-based queries (`using TheWaningBorder.Systems.Buildings;` doesn't pull it in). | [TempleCascadeDestroySystem.cs:9](../../../Assets/Scripts/Systems/Work/TempleCascadeDestroySystem.cs#L9) | fix |
| Systems/Combat | `TargetingSystem.OnUpdate` | Builds `SystemAPI.QueryBuilder().WithAll<LocalTransform,FactionTag,Health>().WithNone<BattalionLeader,NodeUntargetable>()` every tick (~10 Hz) and snapshots four parallel arrays + spatial-hash. Query is uncached — should resolve in `OnCreate` via `state.GetEntityQuery` and reuse across ticks. Same pattern at [BuildingCombatSystem.cs:34](../../../Assets/Scripts/Systems/Combat/BuildingCombatSystem.cs#L34). Cross-ref: §D performance row will measure cost. | [TargetingSystem.cs:68](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68) | spin-out |
| Systems/Visibility | `FogOfWarSystem.OnUpdate` | `em.CreateEntityQuery(...)` rebuilt every tick (line 45-49) + four un-`using`-bracketed `ToEntityArray`/`ToComponentDataArray` temp allocations (51-54) freed implicitly at end-of-method. Early-return on `mgr == null` is fine (returns above), but any future early-return inside the loop would leak. Query should be cached in `OnCreate`; same applies to the duplicate at [FogOfWarSystem.cs:133](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L133) and the `FogVisibilitySyncSystem` at L180. | [FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45) | spin-out |
| Systems/Crystal | `SectActivePowerSystem.DispatchEffect` helpers | Each `ApplyCircleDamage` / `ApplyCircleHeal` / `ApplyCircleBuff` builds `em.CreateEntityQuery(UnitTag, LocalTransform, FactionTag, Health)` on every fire (lines 286, 309, 333). Cast frequency is low (sect active powers, seconds-of-cooldown each) so this is `park` — flagged so the perf pass tags it `needs-measurement` rather than re-discovering it. | [SectActivePowerSystem.cs:286](../../../Assets/Scripts/Systems/Sect/SectActivePowerSystem.cs#L286) | park |
| UI/Web | `HudBridge.OnHudMessage` | `sidebar:action` case is wired only as a `Debug.Log` — sect adoption / level-up / cast clicks from the React rail fall through with no handler. This is `wire`, not `fix`: the UI surface exists, the routing path is missing. Tracked in code as `SECTS-BINDING-TODO` (also at lines 21 + 295 per task-064 inventory). | [HudBridge.cs:122](../../../Assets/Scripts/UI/Web/HudBridge.cs#L122) | wire |
| UI/Web | `HudBridge.HandleActionInvoke` | Military/multi keys `patrol` / `attack` / `formation` / `retreat` / `special` / `stance` all return a "must be issued via right-click" notification — surface exists but no targeting flow. Either build the deferred-click pipeline or hide the buttons from the React rail. | [HudBridge.cs:288-300](../../../Assets/Scripts/UI/Web/HudBridge.cs#L288) | wire |
| UI/Web | `HudBridge` query cache | `_qHall` field is created lazily in `PushBuilderState` (line 702-706) AND duplicated in `PushCultureChoice` (line 756) AND `PushSects` builds its own one-shot `em.CreateEntityQuery` (line 514-516) on every push. Three different lookup paths for the same per-faction Hall. Consolidate to one cached query released in `OnDestroy`. | [HudBridge.cs:514](../../../Assets/Scripts/UI/Web/HudBridge.cs#L514) | spin-out |
| Core/Commands | `CommandRouter.IssueEquipmentUpgrade` / `IssueGodPower` | Both have `Multiplayer lockstep wiring for this command is a follow-up — the LockstepCommand schema needs a payload variant. For now, singleplayer + AI execute directly; multiplayer logs and drops` (see header). Equipment-upgrade silently drops on `ShouldQueueForLockstep` (line 397-401 returns true with `QueueEquipmentUpgradeForLockstep`); GodPower comment says drops but code path returns true. Behaviour vs comment drift. | [CommandRouter.cs:393](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L393) | fix |
| Core/Commands | `CommandRouter.SetRallyPoint` lockstep path | Comment at line 344-347 admits "Lockstep queue currently doesn't replicate targetEntity — single-player sets it directly; multiplayer falls back to a position-only rally." This is a determinism gap — the rally point on a resource node behaves differently in multiplayer than single-player. | [CommandRouter.cs:344](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L344) | spin-out |
| AI/SimpleAI | `SimpleAISystem.OnUpdate` snapshot | `SystemAPI.QueryBuilder().WithAll<AIBrain, SimpleAIState>().Build()` rebuilt every tick (line 73). Brain count is tiny (≤8) so cost is low, but the same query is requested both by SimpleAISystem and by AIBuildingUpgradeSystem (line 57). Cache + reuse across the AI passes. | [SimpleAISystem.cs:73](../../../Assets/Scripts/AI/SimpleAISystem.cs#L73) | park |
| Systems (Burst markers) | `NavMeshPathRequestSystem` is `SystemBase` not `ISystem` | Documented at top of file as deliberate: `NavMesh.CalculatePath is a managed API on the main thread`. Park as intentional. | [NavMeshPathRequestSystem.cs:30](../../../Assets/Scripts/Systems/Movement/NavMeshPathRequestSystem.cs#L30) | park |
| Systems (Burst markers) | `FeraldisRaiderPatrolSystem` is `SystemBase` not `ISystem` | The only system under `Assets/Scripts/Systems/AI/` — class-based with no `[BurstCompile]`. Body issues `CommandRouter` calls (managed) so SystemBase is correct, but the system's domain placement (`Systems/AI/`) makes more sense under `AI/Managers/` or `AI/Behaviors/` alongside the other faction-AI drivers. Module boundary smell. | [FeraldisRaiderPatrolSystem.cs:14](../../../Assets/Scripts/Systems/AI/FeraldisRaiderPatrolSystem.cs#L14) | park |
| Core/Components | (no findings) | Walked every file under `Assets/Scripts/Core/Components/` — all components are declared in the global namespace per project convention. No `TheWaningBorder.*` leak. No `using TheWaningBorder.*` import in any component file. Architecture clean here. | [CoreComponents.cs](../../../Assets/Scripts/Core/Components/CoreComponents.cs) | park |
| ECS pattern | `Entities.ForEach` usage | Only 1 file across `Assets/Scripts/` uses the legacy `Entities.ForEach` source-gen path: `NavMeshPathRequestSystem.cs` (and its comment says manual iteration is required because the source generator rejects `LocalTransform` as a lambda parameter type — DC0005). All other systems use either `SystemAPI.Query<...>` foreach or manual `ToEntityArray` snapshots. Migration to `SystemAPI.Query` is effectively complete. | [NavMeshPathRequestSystem.cs:14](../../../Assets/Scripts/Systems/Movement/NavMeshPathRequestSystem.cs#L14) | park |

### Triage summary — §A

**Drop now (zero-risk):**

_(none — every dead surface in §A is a multi-file teardown, not a single safe delete.)_

**Wire or decide:**

1. `HudBridge.OnHudMessage` `sidebar:action` — sect rail clicks have no route handler ([HudBridge.cs:122](../../../Assets/Scripts/UI/Web/HudBridge.cs#L122)). Spun out as **task-083** (`hudbridge-sect-rail-wiring`).
2. `HudBridge.HandleActionInvoke` military buttons — surface exists, deferred-target pipeline missing ([HudBridge.cs:288](../../../Assets/Scripts/UI/Web/HudBridge.cs#L288)). Spun out as **task-084** (`hudbridge-military-action-targeting`).

**Fix soon:**

1. Dead AI Managers + Behaviors (8 files, `[DisableAutoCreation]`) — remove or revive ([AIEconomyManager.cs:17](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L17)). Spun out as **task-085** (`ai-managers-removal`).
2. `AIBootstrap` attaches dead state components per brain ([AIBootstrap.cs:227](../../../Assets/Scripts/Core/Bootstrap/AIBootstrap.cs#L227)). Subsumed under task-085.
3. Three Combat systems have no namespace declaration ([BurningGroundSystem.cs:20](../../../Assets/Scripts/Systems/Combat/BurningGroundSystem.cs#L20)). Spun out as **task-086** (`combat-system-namespace-cohesion`).
4. `VictoryConditionSystem` declares `TheWaningBorder.UI.HUD` namespace under `Systems/Core/` ([VictoryConditionSystem.cs:10](../../../Assets/Scripts/Systems/Core/VictoryConditionSystem.cs#L10)). Subsumed under task-086.
5. `TempleCascadeDestroySystem` declares `Systems.Building` (singular) vs sibling `Systems.Buildings` ([TempleCascadeDestroySystem.cs:9](../../../Assets/Scripts/Systems/Work/TempleCascadeDestroySystem.cs#L9)). Subsumed under task-086.
6. `CommandRouter.IssueEquipmentUpgrade` lockstep behaviour vs header comment drift ([CommandRouter.cs:393](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L393)). Spun out as **task-087** (`commandrouter-lockstep-drift`).
7. `AIAlanthorEndgameSystem` only — Runai/Feraldis have no endgame ([AIAlanthorEndgameSystem.cs:76](../../../Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs#L76)). Spun out as **task-088** (`ai-endgame-runai-feraldis`).
8. `TargetingSystem.OnUpdate` rebuilds query per tick ([TargetingSystem.cs:68](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68)). Spun out as **task-089** (`ecs-query-caching-hot-systems`). §D will attach measurement.
9. `FogOfWarSystem` rebuilds query per tick ([FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45)). Subsumed under task-089.
10. `HudBridge` has three different Hall lookups + per-push `em.CreateEntityQuery` ([HudBridge.cs:514](../../../Assets/Scripts/UI/Web/HudBridge.cs#L514)). Spun out as **task-090** (`hudbridge-query-consolidation`).
11. `CommandRouter.SetRallyPoint` target-entity not replicated through lockstep ([CommandRouter.cs:344](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L344)). Subsumed under task-087.

**Park (intentionally dormant / defensive / false alarm on inspection):**

1. `SectActivePowerSystem` per-cast query rebuilds — low frequency, `needs-measurement` flag in §D will catch any real cost.
2. `SimpleAISystem` per-tick brain query — tiny N, low impact.
3. `NavMeshPathRequestSystem` is `SystemBase` not `ISystem` — documented as deliberate (managed `NavMesh.CalculatePath` API).
4. `FeraldisRaiderPatrolSystem` location under `Systems/AI/` — module-boundary smell but no behavioural impact; defer to a future AI re-org pass.
5. `Core/Components/` namespace check — all 23 files clean.
6. `Entities.ForEach` legacy usage — only 1 file, documented as required.

## §B Code-vs-Design drift

Audit performed against HEAD `2ad11c1` on branch `test/all-fixes-rolled-up`.
Walk order per the Technical Approach: `Overview.md` → `Tech_Tree.md` →
`Age_0.md` → `Age_1_Alanthor.md` → `Age_1_Runai.md` → `Age_1_Feraldis.md`.

Cross-ref legend:
- A populated task ID (`task-NNN`) means the divergence is already covered
  by that open alignment task — **no new child task spun out**.
- `NEW` means the divergence is not yet covered — a fresh child task stub is
  created (or planned).

### B.1 — `Overview.md` walk

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Data/TechTree | `ironCarry` cross-faction loop | Doc retires `ironCarry` entirely — no faction gets the +2 %/ingot attack bonus from carrying Iron. Code still has the full `ironCarry: { slots: 5, perIngotAttackBonusPct: 2 }` block live in TechTree.json (and `veilsteel.carrySlots/perIngotAttackBonusPct: 3` is still cross-faction shape, doc says Feraldis-only with +2 % per shaving — not +3 %). Both numbers and ownership drift. | [TechTree.json crystalInteractions.ironCarry](../../../Assets/Resources/TechTree.json) | [Overview.md §Resource carry — Veilsteel Frenzy](../../../docs/Design/Overview.md#L174) | [task-070](../task-veilsteel-frenzy-070/task.md) | spin-out |
| Data/TechTree | `Feraldis_VeilsteelFrenzy` cost + +%/shaving | Tech exists in JSON but effect description still reads "+3% (shaving)" wording is absent; cost is `120 S + 40 I` while doc says it should be a Feraldis War Hall research with `+2 % per shaving stacking to +10 %`. Implementation may be on track via task-070 but the JSON entry hasn't been rewritten yet. | [TechTree.json entries[Feraldis_VeilsteelFrenzy]](../../../Assets/Resources/TechTree.json) | [Overview.md §Resource carry](../../../docs/Design/Overview.md#L185) | [task-070](../task-veilsteel-frenzy-070/task.md) | spin-out |
| Entities/Buildings | Age-up Gatherer's Hut wagon-burst (Runai) | Design specifies each Runai Gatherer's Hut transforms in place into a **mobile caravan-wagon** at age-up, outputting full income while in transit with 4-min linear decay. Code's `AgeUpSystem` at L97-102 comments `Phases 2-3 add the wall-anchor / wagon-burst / raider-spawn behaviors. For now the huts simply persist across age-up — no auto-destruction.` Behavior absent. | [AgeUpSystem.cs:97](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L97) | [Overview.md §Age-up](../../../docs/Design/Overview.md#L90) | [task-066](../task-ageup-transform-hut-066/task.md) | spin-out |
| Entities/Buildings | Age-up Gatherer's Hut wall-anchor (Alanthor) | Same code path — Alanthor's hut→wall-anchor with auto-fortify radius is unimplemented. Hut just persists. | [AgeUpSystem.cs:101](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L101) | [Overview.md §Age-up](../../../docs/Design/Overview.md#L98) | [task-066](../task-ageup-transform-hut-066/task.md) | spin-out |
| Entities/Buildings | Age-up Gatherer's Hut Raider-spawn (Feraldis) | Same code path — Feraldis's hut-as-raider-source on age-up is unimplemented. Hut just persists; persistent gather chain works (Hunting Lodge / Logging Station upgrades exist in BuildingFactory) but the auto-raider portion does not. | [AgeUpSystem.cs:101](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L101) | [Overview.md §Age-up Feraldis](../../../docs/Design/Overview.md#L99) | [task-066](../task-ageup-transform-hut-066/task.md), [task-067](../task-feraldis-raider-rebuild-067/task.md) | spin-out |
| Combat | Per-battalion upgrade pattern (cross-faction) | Doc specifies researching a weapon-tier tech UNLOCKS a per-battalion Upgrade button which costs resources **per battalion**. Existing army stays at old tier; only newly-trained battalions arrive at the new base. Code's `EquipmentTierSystem`/research path applies tier bumps faction-wide on research completion (existing units auto-upgrade). | [Assets/Scripts/Systems/Combat/EquipmentTierSystem.cs](../../../Assets/Scripts/Systems/Combat/EquipmentTierSystem.cs) | [Overview.md §Per-battalion upgrades](../../../docs/Design/Overview.md#L142) | [task-076](../task-per-battalion-upgrades-076/task.md) | spin-out |
| Combat | Religious-unit cross-faction game-ender tier (cost) | Doc resolves all three religious units (Scholar / Acolyte / Iconoclast) to **~300 S + 150 I + 100 C + 30 Vs**. Code: `Alanthor_Scholar` cost matches (300 S + 150 I + 100 C + 30 Vs) in TechTree.json; `Runai_Acolyte` is `140 S + 50 C` — diverges hard (missing Iron, Crystal, Veilsteel scales). `Feraldis_Iconoclast` matches at 300/150/100/30. | [TechTree.json entries[Runai_Acolyte].cost](../../../Assets/Resources/TechTree.json) | [Overview.md §Religious units](../../../docs/Design/Overview.md#L200) | [task-074](../task-religious-unit-tier-074/task.md) | spin-out |
| Combat | Religious-unit min building level | Code has all three religious units at `minBuildingLevel: 3` (Temple L3). Matches design; cross-ref only. | [TechTree.json entries[Runai_Acolyte].minBuildingLevel](../../../Assets/Resources/TechTree.json) | [Overview.md §Religious units](../../../docs/Design/Overview.md#L219) | [task-074](../task-religious-unit-tier-074/task.md) | park |
| Systems/Combat | Caravan kill → Feraldis cargo award | Doc: caravan death only awards 50 % cargo as supplies if killer is **Feraldis**; Alanthor / Runai / curse kills destroy cargo. `task-075` marked completed; cross-ref only. | [Assets/Scripts/Systems/Combat/CaravanCargoSystem.cs](../../../Assets/Scripts/Systems/Combat) | [Overview.md §Caravan kills](../../../docs/Design/Overview.md#L234) | [task-075](../task-caravan-loot-feraldis-only-075/task.md) | park |
| Combat | Crystal-Curse neutrality (Runai-only) | Doc: tech-gated `−20 %` chance of aggroing curse waves on cursed-tile traverse. Researched at Trader's Hall. Code: `Runai_CrystalNeutrality` tech exists in TechTree.json (line 924) with `researchedAt: TradersHall` but `TradersHall` is not a building id that exists in the BuildingFactory (cultured Hall stays `Hall` — see task-071). The wave-aggro reduction effect itself is not wired. | [TechTree.json entries[Runai_CrystalNeutrality]](../../../Assets/Resources/TechTree.json) | [Overview.md §Crystal-Curse neutrality](../../../docs/Design/Overview.md#L258) | [task-078](../task-runai-curse-neutrality-078/task.md) | spin-out |
| Economy | Glow economy — once-per-node first state change | Doc: first cleanse / convert / destroy on a curse node drops one Glow pickup; back-and-forth cannot fabricate infinite Glow. `GlowFlowSystem.cs` implements the pickup chain (despawn, transfer, deposit, intercept on death) but the **"first state change only"** gate on per-node drops is implicit — needs a flag on the node entity to prevent repeated Glow drops if a node is re-converted. Not catalogued as a child task yet. | [Assets/Scripts/Systems/Economy/GlowFlowSystem.cs:1](../../../Assets/Scripts/Systems/Economy/GlowFlowSystem.cs#L1) | [Overview.md §Glow economy](../../../docs/Design/Overview.md#L271) | NEW | spin-out |
| Data/TechTree | Vault interest model — tiered 25/50/75/100 % via banking techs | Doc: Vault L1 base is **25 %/min** with banking-tier techs (Coffers 50 / Merchant Charters 75 / Sovereign Bonds 100) mutually exclusive. Code: `VaultStorage.InterestRate` defaults to **3 %/min** (TechTree.json `interestRatePctPerMin: 3`), no banking-tier techs exist. Massive drift on the entire Vault economy. | [TechTree.json entries[VaultOfAlmierra].systems.interestRatePctPerMin](../../../Assets/Resources/TechTree.json) | [Overview.md §Petriarchy reference](../../../docs/Design/Overview.md#L17) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |

### B.2 — `Tech_Tree.md` (Mermaid) walk

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Entities/Buildings | `Alanthor_RoyalStable` factory missing | Building exists in TechTree.json (id `Alanthor_RoyalStable`, line 1472-1491, trains `Alanthor_Cataphract`). Doc shows it as the Cataphract trainer with own 3-tier ladder. No `BuildingFactory.Create("Alanthor_RoyalStable", …)` branch — falls through to `CreateDefault`. Not in `BuildableBuildings` HashSet so the build button never shows. | [BuildingFactory.cs:32](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L32) | [Tech_Tree.md §3 Alanthor RS_A](../../../docs/Design/Tech_Tree.md#L203) | [task-068](../task-alanthor-royal-stable-068/task.md) | spin-out |
| Entities/Buildings | `Alanthor_Cataphract` train-source still Garrison | Cataphract still in `Barracks.trains` (TechTree.json:196, Garrison) — design moved it out to Royal Stable. Cataphract's own `trainAt` list (TechTree.json:1565) already points at `Alanthor_RoyalStable`, so the JSON has the new training source but Barracks's `trains` array hasn't been pruned. | [TechTree.json entries[Barracks].trains[]](../../../Assets/Resources/TechTree.json) | [Tech_Tree.md §3 Garrison roster](../../../docs/Design/Tech_Tree.md#L188) | [task-068](../task-alanthor-royal-stable-068/task.md) | spin-out |
| Entities/Buildings | Runai `Grazing Grounds` not in code | New cavalry trainer for Runai (Raider + Cavalry Archer + L3 cavalry apex). No TechTree.json entry, no factory, not in BuildableBuildings. Runai Raider currently trains at `ThessarasBazaar` (TechTree.json:779). | [TechTree.json no Runai_GrazingGrounds entry](../../../Assets/Resources/TechTree.json) | [Tech_Tree.md §4 Runai GG_R](../../../docs/Design/Tech_Tree.md#L330) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Entities/Buildings | `Runai_Vault` retired but still in code | Q#6 retires `Runai_Vault` (Vault of Almiérra is Runai's only bank with −30 % modifier). Code keeps the building in BuildingFactory branch (line 53), `BuildCosts.cs:49` (`Runai_Vault: 1500 S + 250 I + 200 C`), and TechTree.json entry at line 638. | [BuildCosts.cs:49](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L49) | [Age_1_Runai.md §Trade Hub > Runai Vault retired](../../../docs/Design/Age_1_Runai.md#L361) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Data/TechTree | `Runai_PackBazaar` retired but still in code | Q#8 retires `PackAndMove` and the `Runai_PackBazaar` tech entirely. TechTree.json still has the tech entry at line 904-913 (`researchedAt: ThessarasBazaar`, cost `180 S + 10 I + 10 C`). `BazaarPackSystem.cs` still implements the mechanic. | [TechTree.json entries[Runai_PackBazaar]](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Thessara's Bazaar](../../../docs/Design/Age_1_Runai.md#L334) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Data/TechTree | `ThessarasBazaar` repurpose (no longer cultured Hall, no unit training) | Doc Q#1: Thessara's Bazaar is now a trade-lane-upgrade-only building — **does not train units**. Code has `ThessarasBazaar` with `trains: [Runai_Spearman, Runai_Skirmisher, Runai_Raider]` and `abilities: [PackAndMove, TariffBoostAura]` (both retired or rebound). Plus `provides.population: 40` (cultured Hall artifact). | [TechTree.json entries[Runai].main.id=ThessarasBazaar](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Thessara's Bazaar](../../../docs/Design/Age_1_Runai.md#L307) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Entities | Three culture-named Hall variants missing (`TownHall` / `TradersHall` / `WarHall`) | Design pins the cultured Hall renames per culture. Code keeps a single `Hall` entity reskinned at age-up (no rename, no new tag) — `task-071` is the rename layer. None of the names appear in BuildingFactory or BuildableBuildings. Several places in code reference `TradersHall` (e.g. `Runai_CrystalNeutrality.researchedAt = "TradersHall"`) but `TradersHall` is not a buildable id, so the research-source lookup will fail. | [BuildingFactory.cs:36](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L36) | [Tech_Tree.md §1 Age-up renames](../../../docs/Design/Tech_Tree.md#L41) | [task-071](../task-cultured-rename-layer-071/task.md) | spin-out |
| Combat | Feraldis Houses spawn Raider on build/upgrade | Doc: every Feraldis House build/upgrade spawns autonomous Raiders. `FeraldisRaider.cs` exists with `UnitFactory.Create("Feraldis_Raider", …)` branch (line 57). House-on-build-spawn trigger is not implemented (`task-067` is mid-implementation). | [Assets/Scripts/Entities/Units/FeraldisRaider.cs](../../../Assets/Scripts/Entities/Units/FeraldisRaider.cs) | [Tech_Tree.md §5 Feraldis H_F](../../../docs/Design/Tech_Tree.md#L438) | [task-067](../task-feraldis-raider-rebuild-067/task.md) | spin-out |
| Entities/Units | Runai Trader-Warrior unit type | New uncontrollable patrol unit per `Age_1_Runai.md` (autonomous, lane-pooled, +1 cap per soldier trained). Not in TechTree.json, not in UnitFactory, no factory file. Implementation pending under `task-072`. | [Assets/Scripts/Entities/Units/](../../../Assets/Scripts/Entities/Units) | [Age_1_Runai.md §Trader-warriors](../../../docs/Design/Age_1_Runai.md#L393) | [task-072](../task-trader-warriors-072/task.md) | spin-out |
| Tech ladders | Stone → Iron → Veilstone → Glow weapons (all three cultures) | Mermaid charts spell out 4-tier weapon ladders at every melee/ranged/cavalry trainer (Garrison/Route Guard/Longhouse, Practice Range/Arrowyard/Thrower Camp, Royal Stable/Grazing Grounds). Code has none of these techs in TechTree.json. Only the legacy `WoodenArmor` (Barracks) and `Alanthor_MasonGuild` exist. All marked **⚠** in Tech_Tree.md — "new — does not yet exist in code". | [TechTree.json no Stone/Iron/Veilstone/Glow weapon entries](../../../Assets/Resources/TechTree.json) | [Tech_Tree.md §3-5 weapon ladders](../../../docs/Design/Tech_Tree.md#L196) | [task-076](../task-per-battalion-upgrades-076/task.md), [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Tech ladders | Tools tier ladder (Stone/Iron/Veilstone/Veilsteel) | Same shape as weapons but for Workers, at the cultured Hall. Code has `ImprovedTools` and `StorageCarts` at the Hall — that's it. Stone/Iron/Veilstone/Veilsteel Tools, Wheel cart (split from StorageCarts), Cranes, Mason Guild are all missing. | [TechTree.json no Iron/Veilstone/Veilsteel Tools entries](../../../Assets/Resources/TechTree.json) | [Tech_Tree.md §3 Alanthor TH_A Tools](../../../docs/Design/Tech_Tree.md#L172) | [task-065](../task-age0-techtree-alignment-065/task.md), [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Tech ladders | Stone-tipped → Iron-tipped → Veilstone-tipped → Glow-tipped arrows | Same shape for Archery Range / Practice Range / Arrowyard / Thrower Camp. Code has none of these. `Fletching` and `Choreographed volleys` are also missing. | [TechTree.json no arrow-tier entries](../../../Assets/Resources/TechTree.json) | [Tech_Tree.md §3-5 arrow ladders](../../../docs/Design/Tech_Tree.md#L222) | [task-065](../task-age0-techtree-alignment-065/task.md), [task-076](../task-per-battalion-upgrades-076/task.md) | spin-out |

### B.3 — `Age_0.md` walk

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Data/TechTree | Hall Worker train cost vs Miner cost | Doc unifies Builder + Miner into a single **Worker** (50 S, HP 70, speed 6.0). Code keeps two separate units in TechTree.json: `Builder` (HP 60, speed 4.0, 50 S) at line 286 and `Miner` (HP 70, speed 6.0, 50 S) at line 312. Two factory files (`Builder.cs`, `Miner.cs`) parallel them. The "Worker" terminology is used in the Hall's `trains: [Builder, Scout]` (line 121) but the unit is named `Worker` in JSON. | [TechTree.json entries[Builder, Miner]](../../../Assets/Resources/TechTree.json) | [Age_0.md §Worker](../../../docs/Design/Age_0.md#L304) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | `ImprovedTools` should rename to `Stone tools` | Code id `ImprovedTools` (+15 % gather, 80 S + 40 I, 30 s) — doc id `Stone tools` (same effect, same cost). Rename only. | [TechTree.json entries[ImprovedTools]](../../../Assets/Resources/TechTree.json) | [Age_0.md §Hall techs](../../../docs/Design/Age_0.md#L71) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | `StorageCarts` carry value off by 5 | Code `StorageCarts` grants +10 carry; doc `Wheel cart` says +5. Plus doc splits Wheel cart (move-speed) and Cranes (carry) into two separate techs — code conflates them. | [TechTree.json entries[StorageCarts].effects.carryCapacityBonus](../../../Assets/Resources/TechTree.json) | [Age_0.md §Hall techs](../../../docs/Design/Age_0.md#L72) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Spearman vs `Swordsman` rename + Spearman stats | Doc replaces Swordsman with Spearman (HP 120, dmg 10, range **1.5**, def 1/0/0/0). Code's `Swordsman` JSON entry has the right stats (HP 120, dmg 10, range **1.0**, def 1/0/0/0) — but range and name differ. UnitFactory has both a `Swordsman.cs` and a `Spearman.cs` factory. | [TechTree.json entries[Swordsman]](../../../Assets/Resources/TechTree.json) | [Age_0.md §Spearman](../../../docs/Design/Age_0.md#L343) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | `BasicDrills` and `WoodenArmor` should retire | Doc: Conscription (+20 % training speed at Barracks) replaces `BasicDrills` (+10 % melee atkspd); Stone weapons replaces `WoodenArmor`. Both code techs still live. | [TechTree.json entries[BasicDrills, WoodenArmor]](../../../Assets/Resources/TechTree.json) | [Age_0.md §Barracks techs](../../../docs/Design/Age_0.md#L102) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Archery Range tech roster | Doc: `Choreographed volleys`, `Stone-tipped arrows`, `Fletching`. Code: `ArcheryRange.research: []` (empty, line 222). Three techs missing entirely. | [TechTree.json entries[ArcheryRange].research[]](../../../Assets/Resources/TechTree.json) | [Age_0.md §Archery Range techs](../../../docs/Design/Age_0.md#L131) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Gatherer's Hut HP | Doc: 800; code TechTree.json: 800. Already correct. Cross-ref only. | [TechTree.json entries[GatherersHut].hp](../../../Assets/Resources/TechTree.json) | [Age_0.md §Gatherer's Hut](../../../docs/Design/Age_0.md#L147) | [task-065](../task-age0-techtree-alignment-065/task.md) | park |
| Data/TechTree | Gatherer's Hut despawn note stale | TechTree.json (line 179) says `Auto-despawns 2 min after advancing to Era 2 (except Feraldis)` — design retired this model (Q#7 in Age_0.md decisions: "Huts do not despawn; they transform per culture"). | [TechTree.json entries[GatherersHut].notes](../../../Assets/Resources/TechTree.json) | [Age_0.md §Age-up transitions](../../../docs/Design/Age_0.md#L416) | [task-066](../task-ageup-transform-hut-066/task.md) | spin-out |
| Data/TechTree | House (Hut) display-name vs code id | Doc: display name is "House", internal id stays `Hut`. Code: TechTree.json `Hut.name = "Hut"` — display name still says "Hut". | [TechTree.json entries[Hut].name](../../../Assets/Resources/TechTree.json) | [Age_0.md §House](../../../docs/Design/Age_0.md#L156) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Hut HP value | Doc: 600. Code TechTree.json: 600. Matches. Cross-ref only. | [TechTree.json entries[Hut].hp](../../../Assets/Resources/TechTree.json) | [Age_0.md §House](../../../docs/Design/Age_0.md#L172) | [task-065](../task-age0-techtree-alignment-065/task.md) | park |
| Data/TechTree | Vault of Almiérra interest model | Doc L1 base is `25 %/min` compound, scales up through banking-grade techs (Coffers/Merchant Charters/Sovereign Bonds). Code: `interestRatePctPerMin: 3` flat (TechTree.json:276), no banking-tier techs (`Coffers`, `Merchant Charters`, `Sovereign Bonds`, `Iron Subsidies`, `Veilstone monetization`, `Veilsteel Bonds` — none exist in code). | [TechTree.json entries[VaultOfAlmierra].systems.interestRatePctPerMin](../../../Assets/Resources/TechTree.json) | [Age_0.md §Vault of Almiérra](../../../docs/Design/Age_0.md#L189) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Temple/Shrine cap at L3 (not L4) | Doc Q#17: Temple of Ridan caps at **L3**. TechTree.json `TempleOfRidan` has `levels: 1` (line 236) and `maxEra: 5`; `evolution.toEra5.requiresTempleLevel: 4` (line 99) — design says no L4 exists. | [TechTree.json entries[TempleOfRidan].levels](../../../Assets/Resources/TechTree.json) | [Age_0.md §Shrine of Ridan](../../../docs/Design/Age_0.md#L222) | [task-074](../task-religious-unit-tier-074/task.md) | spin-out |
| Data/TechTree | Shrine "Sect Point on build" RP value | Doc: +1 RP on build, +1 if Runai picked at age-up. Code: `passive.sectPointsOnBuild: 1` only (TechTree.json:253); the Runai-pick bonus is not modelled. | [TechTree.json entries[TempleOfRidan].passive](../../../Assets/Resources/TechTree.json) | [Age_0.md §Shrine of Ridan](../../../docs/Design/Age_0.md#L224) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Shrine heal-tier techs (Heightened/Pious/Fervored masses + Warrior priests) | Doc lists 4 Shrine techs gated by L1/L2/L3. Code: TechTree.json `TempleOfRidan` has no tech array. Heal rate tier mechanics absent. | [TechTree.json entries[TempleOfRidan]](../../../Assets/Resources/TechTree.json) | [Age_0.md §Shrine techs](../../../docs/Design/Age_0.md#L245) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Combat | Litharch damage default | Doc: Litharch has **0 damage by default** (pure healer); attack only after `Warrior priests`. Code: `Litharch` JSON has `damage: 0` (line 417), matches design. The `Warrior priests` tech that grants attack is absent. | [TechTree.json entries[Litharch].damage](../../../Assets/Resources/TechTree.json) | [Age_0.md §Litharch](../../../docs/Design/Age_0.md#L389) | [task-065](../task-age0-techtree-alignment-065/task.md) | park |
| Data/TechTree | Fiendstone Keep range / max-targets bump | Doc Q#3: range bumped 25 → **30**, max targets 3 → **4**. TechTree.json `FiendstoneKeep.main` block (line 938) has no `attackRange` or `maxTargets` — neither value is explicit; lives in code defaults via TowerArrowSystem / KeepArrowFireSystem. Drift surface: design pinned new numbers, code defaults need verification. | [TechTree.json entries[FiendstoneKeep] no attackRange/maxTargets](../../../Assets/Resources/TechTree.json) | [Age_0.md §Fiendstone Keep](../../../docs/Design/Age_0.md#L268) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | Keep emplacement techs (Ballista/Trebuchet) and Reinforced walls + Additional Towers | Doc lists four Keep techs. None exist in code (no `Ballista_Emplacement`, `Trebuchet_Emplacement`, `Additional_Towers`, `Reinforced_Walls` entries). | [TechTree.json no Keep tech entries](../../../Assets/Resources/TechTree.json) | [Age_0.md §Keep techs](../../../docs/Design/Age_0.md#L289) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Core/Settings | Choice-building L2/L3 upgrade cost rows | Doc body of Age_0.md explicitly flags `**(new entries needed in BuildingUpgradeConfig.cs)**` for Vault, Shrine, Keep — none of those buildings appear in `BuildingUpgradeConfig.TryGetCost` (only Hall/Barracks/ArcheryRange/Hut, lines 76-108). | [BuildingUpgradeConfig.cs:71](../../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs#L71) | [Age_0.md §Vault, Shrine, Keep cost rows](../../../docs/Design/Age_0.md#L200) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |

### B.4 — `Age_1_Alanthor.md` walk

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Entities | `KingsCourt` → `TownHall` rename | Doc Q#1 confirms: queue rename of tag, prefab, presentation id, and BuildCosts entries from `KingsCourt` to `TownHall`. Code: `KingsCourt` still the id in `BuildingFactory` (line 59), `BuildCosts.cs:62`, `TechTree.json:1274`. No `TownHall` id anywhere. | [BuildingFactory.cs:59](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L59) | [Age_1_Alanthor.md §Town Hall](../../../docs/Design/Age_1_Alanthor.md#L56) | [task-071](../task-cultured-rename-layer-071/task.md) | spin-out |
| Data/TechTree | KingsCourt cost mismatch | Doc says cost is `360 S + 80 I (at age-up — already standing)`. Code `BuildCosts.cs:62`: `KingsCourt` = **500 S + 150 I + 50 C**. TechTree.json:1298 matches doc at 360/80. So code TechTree.json and Design agree; BuildCosts is wrong (or it's the player-built standalone cost — but no such use case exists since KingsCourt is the cultured Hall, never standalone-built). | [BuildCosts.cs:62](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L62) | [Age_1_Alanthor.md §Town Hall cost](../../../docs/Design/Age_1_Alanthor.md#L69) | [task-071](../task-cultured-rename-layer-071/task.md) | spin-out |
| Data/TechTree | Alanthor weapon ladder (Stone/Iron/Veilstone/Glow weapons) | None of these techs exist in TechTree.json. Cross-ref task-076 covers the per-battalion mechanic; the actual TechTree entries also need to land. | [TechTree.json no Alanthor weapon-tier entries](../../../Assets/Resources/TechTree.json) | [Age_1_Alanthor.md §Garrison techs](../../../docs/Design/Age_1_Alanthor.md#L153) | [task-076](../task-per-battalion-upgrades-076/task.md), [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Entities/Units | Alanthor Swordsman / Royal Guard L2/L3 line-infantry | Doc Q#2: Garrison trains 3-tier ladder Spearman → Swordsman → Royal Guard plus Sentinel. Code has only `Spearman` and `Alanthor_Sentinel` — Swordsman (cultured) and Royal Guard don't exist. Note: a `Swordsman` JSON entry exists at line 337 but that's the **pre-rename Age 0 Spearman** — the doc Swordsman is the L2 cultured promotion. | [TechTree.json no Alanthor_Swordsman or Alanthor_RoyalGuard entries](../../../Assets/Resources/TechTree.json) | [Age_1_Alanthor.md §Garrison roster](../../../docs/Design/Age_1_Alanthor.md#L132) | [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Entities/Units | Alanthor L3 ranged apex (Longbowman?) | Doc Q#7: Practice Range has 3-tier ladder ending in a TBD-named apex. Code: Archer + Crossbowman only. | [TechTree.json no Alanthor L3 ranged apex](../../../Assets/Resources/TechTree.json) | [Age_1_Alanthor.md §Practice Range](../../../docs/Design/Age_1_Alanthor.md#L189) | [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Data/TechTree | Practice Range HP / pop override rejected | Doc Q#3: reject the 1 500 HP / +8 pop override; use standard multiplier path (660/690/720, 0 pop). Verified completed under `task-073`. Cross-ref only. | [TechTree.json entries[Alanthor_PracticeRange]](../../../Assets/Resources/TechTree.json) | [Age_1_Alanthor.md §Practice Range](../../../docs/Design/Age_1_Alanthor.md#L179) | [task-073](../task-rejected-stat-overrides-073/task.md) | park |
| Data/TechTree | Alanthor Wall cost & stats | Doc: 50 S + 20 I, HP 900, def 2/2/0/0. Code TechTree.json: 50 S + 20 I, HP 900 — matches. Cross-ref only. | [TechTree.json entries[Alanthor_Wall]](../../../Assets/Resources/TechTree.json) | [Age_1_Alanthor.md §Alanthor Wall](../../../docs/Design/Age_1_Alanthor.md#L313) | — | park |
| Data/TechTree | `Alanthor_Crucible` chicken-and-egg cost | Doc Q#9 flags Crucible's `30 Veilsteel` build cost as a code bug (need Veilsteel to build the building that produces Veilsteel). Code `BuildCosts.cs:70`: `300 S + 80 Crystal + 30 Veilsteel`. TechTree.json:1465 is sane (200 S + 60 I + 40 C). The doc says BuildCosts.cs is authoritative — but for THIS row it's wrong. | [BuildCosts.cs:70](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L70) | [Age_1_Alanthor.md §Crucible chicken-and-egg](../../../docs/Design/Age_1_Alanthor.md#L385) | NEW | spin-out |
| Combat | `Alanthor_MasonGuild` effect numbers | Doc Q#6: rebalance to `+20 % HP` (canonical "Mason Guild" name). Code TechTree.json:1643: `+15 % building HP, +20 % repair rate` — partial drift (effect numbers right for repair, off for HP). Old `Masonry` name was dropped. | [TechTree.json entries[Alanthor_MasonGuild]](../../../Assets/Resources/TechTree.json) | [Age_1_Alanthor.md §existing tech list](../../../docs/Design/Age_1_Alanthor.md#L502) | [task-065](../task-age0-techtree-alignment-065/task.md) | spin-out |
| Data/TechTree | `Alanthor_Smelter` and `Alanthor_Crucible` not in BuildableBuildings | Code BuildableBuildings HashSet lists `Alanthor_Smelter` (line 704 of EntityExtractors.cs) but **not** `Alanthor_Crucible`. Players can't place the Crucible. | [Assets/Scripts/UI/Panels/EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700) | [Age_1_Alanthor.md §Crucible](../../../docs/Design/Age_1_Alanthor.md#L374) | NEW | fix |
| Data/TechTree | `KingsCourt` and `Alanthor_RoyalStable` not in BuildableBuildings | Same HashSet. `KingsCourt` is the cultured Hall — should never be player-built (it's a transform target), so omission is **correct**. `Alanthor_RoyalStable` is a player-built building per doc — omission is wrong. | [Assets/Scripts/UI/Panels/EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700) | [Age_1_Alanthor.md §Royal Stable](../../../docs/Design/Age_1_Alanthor.md#L285) | [task-068](../task-alanthor-royal-stable-068/task.md) | spin-out |

### B.5 — `Age_1_Runai.md` walk

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Entities/Buildings | Runai Spearman / Skirmisher / Raider train source | Doc: Spearman & Skirmisher train at Route Guard (cultured Barracks); Raider trains at Grazing Grounds (new). Code TechTree.json: all three have `trainAt: [ThessarasBazaar]` (lines 728, 754, 779). | [TechTree.json entries[Runai_Spearman/Skirmisher/Raider].trainAt](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Route Guard / Grazing Grounds](../../../docs/Design/Age_1_Runai.md#L154) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Combat | Runai Acolyte cost (game-ender bracket) | Code `Runai_Acolyte.cost: { Supplies: 140, Crystal: 50 }` (TechTree.json:809-811). Doc: `~300 S + 150 I + 100 C + 30 Vs`. Half-cost outlier among the three religious units. | [TechTree.json entries[Runai_Acolyte].cost](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Runai Acolyte](../../../docs/Design/Age_1_Runai.md#L538) | [task-074](../task-religious-unit-tier-074/task.md) | spin-out |
| Combat | Runai `RunaiPopOverride` instant 200 pop | Doc: full population unlocked at age-up (200 cap). Code: `AgeUpSystem.cs:107-111` adds `RunaiPopOverride` component on age-up. Matches. Cross-ref only. | [AgeUpSystem.cs:107](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L107) | [Age_1_Runai.md §No House](../../../docs/Design/Age_1_Runai.md#L266) | — | park |
| Data/TechTree | Trade Hub `TariffBoostAura` mechanic | Doc Q#7: per-drop-off timer (next deposit gets a short bonus-yield window; no stacking across multiple buildings). Code: `TechTree.json:629` only has `aura.nearbyCaravanArmorAdd: 1` on the Trade Hub. The drop-off-timer bonus mechanic is not in code; the `TariffBoostAura` ability tag lives on Thessara's Bazaar (line 541) tied to PackAndMove. | [TechTree.json entries[Runai_TradeHub].aura](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Trade Hub](../../../docs/Design/Age_1_Runai.md#L358) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Entities/Buildings | `Runai_TradingPost` code-only entity | Building id exists in BuildableBuildings (line 706 of EntityExtractors), BuildingFactory (line 50), BuildCosts (line 46). Not mentioned in any Design doc — Trade Posts in the doc are conceptual ("planted by wagon-burst"), the data model uses Outpost + Trade Hub. Reverse-drift: code-only entity with no Design mention. | [BuildCosts.cs:46](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L46) | doc-missing — needs spec | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |
| Systems | Runai trade-lane infrastructure | Doc: caravans path between Outposts/Trade Hubs, yield scales with route length. Code: `RunaiTradeComponents.cs` exists, `caravanSpawner` config block in TechTree.json:603, escort system in JSON. Matches design at the data level. Trader-warrior auto-spawn from lanes is missing (task-072). Cross-ref only. | [TechTree.json entries[Runai_TradeHub].systems.caravanSpawner](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Trade Hub](../../../docs/Design/Age_1_Runai.md#L347) | [task-072](../task-trader-warriors-072/task.md) | park |
| Data/TechTree | Runai_VeilsteelFoundry exists, matches | Doc & TechTree.json both: 1500 HP, 450 S + 120 I + 100 C, Iron + Crystal inputs, 20 % loss. Matches. | [TechTree.json entries[Runai_VeilsteelFoundry]](../../../Assets/Resources/TechTree.json) | [Age_1_Runai.md §Runai Veilsteel Foundry](../../../docs/Design/Age_1_Runai.md#L368) | — | park |
| Entities/Buildings | `Runai_VeilsteelFoundry` not in BuildableBuildings | Building exists in BuildingFactory (line 54), BuildCosts (line 50), TechTree.json — but missing from `BuildableBuildings` HashSet. Player can't construct it. | [Assets/Scripts/UI/Panels/EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700) | [Age_1_Runai.md §Runai Veilsteel Foundry](../../../docs/Design/Age_1_Runai.md#L368) | NEW | fix |
| Systems/Economy | Runai workers mining iron | Doc Q#3: Runai workers mine iron normally; Supplies + Crystal come from trade routes only. Code: Workers can mine all resources for any faction. Implementation: need to gate Runai supplies/crystal so they zero out from worker output. | [Assets/Scripts/Systems/Work/MiningSystem.cs](../../../Assets/Scripts/Systems/Work/MiningSystem.cs) | [Age_1_Runai.md §Economy](../../../docs/Design/Age_1_Runai.md#L31) | [task-069](../task-runai-buildings-split-069/task.md) | spin-out |

### B.6 — `Age_1_Feraldis.md` walk

| Surface | Item | Issue | File:line | Doc-ref | Cross-ref task | Triage |
|---|---|---|---|---|---|---|
| Data/TechTree | `WarHall` cultured Hall rename | Doc: War Hall is the cultured Feraldis Hall (same entity, renamed). TechTree.json:937 still pins `main.id: FiendstoneKeep` for the Feraldis culture's main building — Q#1 explicitly flagged this as a stale code-era artefact. | [TechTree.json entries[Feraldis].main.id](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §War Hall](../../../docs/Design/Age_1_Feraldis.md#L47) | [task-071](../task-cultured-rename-layer-071/task.md) | spin-out |
| Data/TechTree | `Feraldis_VeilsteelFrenzy.researchAt` value | Code TechTree.json:1262: `researchAt: "WarHall"`. But no `WarHall` building exists in BuildingFactory or BuildableBuildings. Research-source lookup will fail silently. | [TechTree.json entries[Feraldis_VeilsteelFrenzy].researchAt](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §War Hall techs](../../../docs/Design/Age_1_Feraldis.md#L72) | [task-070](../task-veilsteel-frenzy-070/task.md), [task-071](../task-cultured-rename-layer-071/task.md) | spin-out |
| Combat | Longhouse HP override rejected | Doc Q#11: reject the 1 400 HP override; use multiplier path 880 / 920 / 960. `task-073` completed; cross-ref only. | [TechTree.json entries[Feraldis_Longhouse]](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §Longhouse HP](../../../docs/Design/Age_1_Feraldis.md#L109) | [task-073](../task-rejected-stat-overrides-073/task.md) | park |
| Entities/Units | Feraldis Swordsman / Royal-Guard L2/L3 line-infantry | Doc Q#3 "same logic as Alanthor": Longhouse trains Spearman → Swordsman → Royal Guard apex (+ Berserker parallel + Warboar Rider cavalry). Code has only Berserker and Warboar Rider; no Feraldis Swordsman or Royal Guard. | [TechTree.json no Feraldis_Swordsman or Feraldis_RoyalGuard](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §Longhouse roster](../../../docs/Design/Age_1_Feraldis.md#L120) | [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Entities/Units | Feraldis L2/L3 ranged tier | Doc: Thrower Camp trains Hunter (L1) → L2 (TBD "Tracker"?) → L3 apex. Code: Hunter only. | [TechTree.json no Feraldis ranged L2/L3](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §Thrower Camp](../../../docs/Design/Age_1_Feraldis.md#L167) | [task-079](../task-age1-unit-tier-ladders-079/task.md) | spin-out |
| Data/TechTree | `Feraldis_Hunter` missing `trainAt` | Doc & code intent: Hunter trains at Thrower Camp (cultured Archery Range). Code TechTree.json:1154-1175: `Feraldis_Hunter` has no `trainAt` array — comment in `Age_1_Feraldis.md§Feraldis Hunter` explicitly notes "no `trainAt` in JSON today — add". | [TechTree.json entries[Feraldis_Hunter] missing trainAt](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §Feraldis Hunter](../../../docs/Design/Age_1_Feraldis.md#L169) | [task-071](../task-cultured-rename-layer-071/task.md) | spin-out |
| Combat | Feraldis House Raider auto-spawn | Doc: every Feraldis House build/upgrade spawns 1 / 2 / 3 Raiders (L1/L2/L3). Code: Houses use `Hut` tag — no Feraldis-specific spawn hook on construction or upgrade. `Feraldis_Raider` unit factory exists but the building-driven spawn is unimplemented. | [Assets/Scripts/Entities/Buildings/Hut.cs](../../../Assets/Scripts/Entities/Buildings/Hut.cs) | [Age_1_Feraldis.md §House](../../../docs/Design/Age_1_Feraldis.md#L189) | [task-067](../task-feraldis-raider-rebuild-067/task.md) | spin-out |
| Combat | Feraldis Houses provide 0 pop after age-up | Doc: Feraldis Houses produce **0 pop** post-age-up (pop is at 200 cap). `task-080` marked completed (`FeraldisPopOverride` added in AgeUpSystem.cs:120). Cross-ref only. | [AgeUpSystem.cs:120](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L120) | [Age_1_Feraldis.md §House](../../../docs/Design/Age_1_Feraldis.md#L208) | [task-080](../task-feraldis-instant-pop-spike-080/task.md) | park |
| Entities/Buildings | `Feraldis_BeastPen` reverse-drift | Building id exists in `BuildCosts.cs:53` (`Feraldis_BeastPen: 150 S + 30 I`) but **not** in `BuildingFactory.Create` and **not** in any Design doc. Code-only orphan. | [BuildCosts.cs:53](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L53) | doc-missing — needs spec | NEW | spin-out |
| Entities/Buildings | `Feraldis_Foundry` not in BuildableBuildings | Building exists in BuildingFactory (line 67), BuildCosts (line 56), TechTree.json (line 1002) — but missing from `BuildableBuildings` HashSet. Player can't construct Fiend Foundry. | [Assets/Scripts/UI/Panels/EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700) | [Age_1_Feraldis.md §Fiend Foundry](../../../docs/Design/Age_1_Feraldis.md#L314) | NEW | fix |
| Entities/Buildings | Hut-upgrade tech locking Hunting Lodge / Logging Station | Doc Q#4: each Feraldis Gatherer's Hut upgrades into exactly one of (Hunting Lodge / Logging Station), behind a Hut-upgrade tech (TBD name). Code: TechTree.json has both buildings (`Feraldis_HuntingLodge`, `Feraldis_LoggingStation`) and `GatherersHut.upgradesTo` in code — but the tech gate doesn't exist. | [Assets/Scripts/Entities/Buildings/GatherersHut.cs](../../../Assets/Scripts/Entities/Buildings/GatherersHut.cs) | [Age_1_Feraldis.md §Gatherer's Hut carryover](../../../docs/Design/Age_1_Feraldis.md#L261) | [task-066](../task-ageup-transform-hut-066/task.md) | spin-out |
| Data/TechTree | Hunting Lodge / Logging Station terrain bonus | Doc: +30 % yield when placed near preferred terrain (mountains / trees). Code TechTree.json:966-999 has the buildings but no terrain-yield bonus mechanic. | [TechTree.json entries[Feraldis_HuntingLodge,Feraldis_LoggingStation]](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §Hunting Lodge terrain bonus](../../../docs/Design/Age_1_Feraldis.md#L276) | NEW | spin-out |
| Combat | Bloody-ground mechanic | Doc: Totem Tower's `+1.25× attack, +2.0 range` on bloody ground (tiles where kills happened). TechTree.json:1033 has the aura block. Implementation: needs a "bloody ground" tile-state tracker (decay rate, radius, stacking). Doc Q#5 explicitly defers this. | [TechTree.json entries[Feraldis_Tower].aura.onBloodyGround](../../../Assets/Resources/TechTree.json) | [Age_1_Feraldis.md §Totem Tower](../../../docs/Design/Age_1_Feraldis.md#L333) | NEW | park |
| Entities/Units | Crystalling / Veilstinger / Godsplinter reverse-drift | Three unit factories exist in code (`UnitFactory.cs:41-43`, plus `Crystalling.cs`, `Veilstinger.cs`, `Godsplinter.cs`, plus presentation IDs 320-322) and are wired through `GodsplinterCombatSystem`. Zero mentions in any Design doc — Crystal-Curse creature stat blocks are referenced in `Crystal_Curse_Sweep_And_Checklist_v2.md` but not folded into the canonical Design folder. | [Assets/Scripts/Entities/Units/Crystalling.cs](../../../Assets/Scripts/Entities/Units/Crystalling.cs) | doc-missing — needs spec | NEW | park |

### Triage summary — §B

**Drop now (zero-risk):**

_(none — every §B row is a design-implementation gap, not a dead surface to delete.)_

**Wire or decide:**

1. `Alanthor_Crucible` not in BuildableBuildings ([EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700)) — building exists in factory and JSON, just unbuildable. Spun out as **task-091** (`buildables-hashset-completeness`).
2. `Runai_VeilsteelFoundry` not in BuildableBuildings ([EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700)). Subsumed under task-091.
3. `Feraldis_Foundry` (Fiend Foundry) not in BuildableBuildings ([EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700)). Subsumed under task-091.

**Fix soon:**

1. `Alanthor_Crucible.cost` chicken-and-egg Veilsteel requirement ([BuildCosts.cs:70](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L70)). Spun out as **task-092** (`alanthor-crucible-cost-fix`).
2. `Runai_TradingPost` reverse-drift — code-only building with no Design entry. Covered by task-069 (Runai split). Cross-ref only.
3. `Feraldis_BeastPen` reverse-drift — code-only building with no Design entry ([BuildCosts.cs:53](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L53)). Spun out as **task-093** (`feraldis-beastpen-doc-or-drop`).
4. Crystalling / Veilstinger / Godsplinter reverse-drift — Crystal-Curse units referenced in code but absent from the Design folder. **Park** for now — covered indirectly by `docs/Crystal_Curse_Sweep_And_Checklist_v2.md`; promoting to canonical Design is a separate doc effort. Recorded for cross-reference.
5. Glow once-per-node "first state change" gate not implemented ([GlowFlowSystem.cs:1](../../../Assets/Scripts/Systems/Economy/GlowFlowSystem.cs#L1)). Spun out as **task-094** (`glow-once-per-node-gate`).
6. Hunting Lodge / Logging Station terrain-yield bonus not implemented. Spun out as **task-095** (`feraldis-hut-terrain-yields`).

**Subsumed under existing alignment tasks (cross-ref, NO new child task):**

1. `ironCarry` retirement + `veilsteel.carrySlots` rewrite — [task-070](../task-veilsteel-frenzy-070/task.md).
2. `Feraldis_VeilsteelFrenzy` cost / effect — [task-070](../task-veilsteel-frenzy-070/task.md).
3. Hut age-up transformations (wagon-burst / wall-anchor / raider-spawn) — [task-066](../task-ageup-transform-hut-066/task.md), [task-067](../task-feraldis-raider-rebuild-067/task.md).
4. Per-battalion upgrade mechanic — [task-076](../task-per-battalion-upgrades-076/task.md).
5. Religious-unit cross-faction cost (Acolyte at 140 S + 50 C vs game-ender bracket) — [task-074](../task-religious-unit-tier-074/task.md).
6. Crystal-Curse neutrality tech — [task-078](../task-runai-curse-neutrality-078/task.md).
7. Vault interest model (25/50/75/100 % banking-tier techs) — [task-065](../task-age0-techtree-alignment-065/task.md).
8. `Alanthor_RoyalStable` factory + BuildableBuildings + Cataphract reparent — [task-068](../task-alanthor-royal-stable-068/task.md).
9. Runai `Grazing Grounds` building — [task-069](../task-runai-buildings-split-069/task.md).
10. `Runai_Vault` + `Runai_PackBazaar` retirements — [task-069](../task-runai-buildings-split-069/task.md).
11. `ThessarasBazaar` repurpose (no longer trains units) — [task-069](../task-runai-buildings-split-069/task.md).
12. Cultured Hall renames (`TownHall` / `TradersHall` / `WarHall`) — [task-071](../task-cultured-rename-layer-071/task.md).
13. Trader-Warrior unit — [task-072](../task-trader-warriors-072/task.md).
14. All Age 0 ladder techs (Conscription, Stone weapons, Stone-tipped arrows, Fletching, Choreographed volleys, banking-tier techs, Shrine heal-tier techs, Keep emplacement techs) — [task-065](../task-age0-techtree-alignment-065/task.md).
15. All Age 1 weapon / arrow / tools ladders — [task-076](../task-per-battalion-upgrades-076/task.md), [task-079](../task-age1-unit-tier-ladders-079/task.md).
16. Age 1 L2/L3 unit slots (Swordsman, Royal Guard, L3 ranged apex, Cavalry Archer, L3 cavalry apex, L2 ranged tier across cultures) — [task-079](../task-age1-unit-tier-ladders-079/task.md).
17. Building HP override rejections (Practice Range 1 500 HP, Longhouse 1 400 HP) — [task-073](../task-rejected-stat-overrides-073/task.md) (completed).
18. Caravan-loot → Feraldis-only gating — [task-075](../task-caravan-loot-feraldis-only-075/task.md) (completed).
19. `Feraldis` pop override — [task-080](../task-feraldis-instant-pop-spike-080/task.md) (completed).
20. KingsCourt cost mismatch (BuildCosts vs TechTree.json vs design) — [task-071](../task-cultured-rename-layer-071/task.md).
21. Temple of Ridan caps at L3 (no L4) — [task-074](../task-religious-unit-tier-074/task.md).
22. Gatherer's Hut despawn note retired — [task-066](../task-ageup-transform-hut-066/task.md).
23. Worker = Builder + Miner unification — [task-065](../task-age0-techtree-alignment-065/task.md).

**Park (intentionally dormant / out-of-scope / on track):**

1. Crystalling / Veilstinger / Godsplinter — recorded for cross-reference; Crystal-Curse layer is documented separately, no Design-canonical doc required by 082.
2. Bloody-ground mechanic — doc Q#5 explicitly defers (decay rate, radius, stacking TBD).
3. Litharch 0 damage default — code and design agree.
4. Religious-unit `minBuildingLevel: 3` — code and design agree.
5. Caravan-loot Feraldis-only — task-075 completed.
6. Feraldis pop override — task-080 completed.
7. Runai pop override — `RunaiPopOverride` in `AgeUpSystem.cs` works.
8. Runai trade-lane infrastructure base — caravan spawner config exists; only the trader-warrior portion is missing (task-072).
9. Practice Range HP override rejection — task-073 completed.
10. Longhouse HP override rejection — task-073 completed.

## §C Correctness & bug surface

Audit performed against HEAD `2ad11c1` on branch `test/all-fixes-rolled-up`.
Walk order per the Technical Approach: null/ref hazards →
EntityCommandBuffer structural-change ordering → save/load
component coverage (the exhaustive subsection — AC anchor) →
command-router edges → AI brain stalls → lockstep / determinism.

Severity legend (`Severity` column):
- `crash` — will throw / corrupt state on the documented path.
- `wrong-result` — silently incorrect output (desync, bad value, dropped event).
- `degraded` — runs but stalls / leaks / accumulates rounding.
- `edge-only` — rare repro path; usually safe in normal play.

Static-only rows are tagged `static-only — needs repro` in the
`Issue` column per Technical Notes; they fail-soft to "needs play
test" rather than being dropped silently.

### C.1 Null / ref hazards (HasComponent guard check)

Walked every `EntityManager.GetComponentData` / `GetSharedComponent` /
`GetBuffer` call site outside `Core/Commands/` (which is already
guard-discipline-clean — every public Issue* method runs
`em.Exists` + `em.HasComponent` before deref). The simulation
systems and AI behaviors are clean — every read is preceded by
either an explicit `HasComponent` guard or a structural query
filter that guarantees the component exists.

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| Systems/Combat | `DeathSystem` — battalion / blood-pool reads | All eight `state.EntityManager.GetComponentData<T>(dead)` call sites at L100/L166/L173 are preceded by a matching `HasComponent` check within ±5 lines (`BattalionMemberData` L98, `LocalTransform` L164, `Health` L172). Audit clean — recorded as cross-reference. | [DeathSystem.cs:100](../../../Assets/Scripts/Systems/Combat/DeathSystem.cs#L100) | edge-only | park |
| Systems/Training | `TrainingSystem.OnUpdate` — Feraldis training multiplier | `state.EntityManager.GetComponentData<FactionTag>(entity).Value` at L89 has no `HasComponent` guard. Defended by the surrounding query (`SystemAPI.Query<RefRW<TrainingState>, …>` requires the trainer building, and BuildingFactory attaches `FactionTag` to every building). Implicit but not explicit. `static-only — needs repro` for any code path that adds `TrainingState` without `FactionTag`. | [TrainingSystem.cs:89](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs#L89) | edge-only | park |
| Systems/Crystal | `NodeStateDeathInterceptSystem.OnUpdate` — victory-state lookup | `EntityManager.GetComponentData<NodeVictoryState>(victoryEntity)` at L89 is guarded by `hasVictoryState = !_victoryQuery.IsEmpty` at L82-85 — only enters the dereference path when the query produced at least one entity. Defensive pattern; clean. | [NodeStateDeathInterceptSystem.cs:89](../../../Assets/Scripts/Systems/Crystal/NodeStateDeathInterceptSystem.cs#L89) | edge-only | park |
| Systems/Crystal | `NodeVictorySystem.OnUpdate` — singleton victory read | `EntityManager.GetComponentData<NodeVictoryState>(victoryEntity)` at L59 reads `victoryEntities[0]` straight from `_victoryQuery.ToEntityArray(...)` with no `if (victoryEntities.Length == 0) return;` guard. `RequireForUpdate(_victoryQuery)` at L52 prevents `OnUpdate` from firing when empty — but if a future change removes the `RequireForUpdate` (or the singleton is destroyed mid-frame), this would throw `IndexOutOfRangeException`. Defensive but fragile. | [NodeVictorySystem.cs:57](../../../Assets/Scripts/Systems/Crystal/NodeVictorySystem.cs#L57) | edge-only | park |

### C.2 ECS structural-change ordering (EntityCommandBuffer + iteration)

Walked every `new EntityCommandBuffer(Allocator.Temp)` call site
(40+ files). The dominant pattern is **manual playback at the end
of `OnUpdate` via `ecb.Playback(em); ecb.Dispose();`**, NOT the
wired `BeginInitializationEntityCommandBufferSystem` /
`EndSimulationEntityCommandBufferSystem` model. The manual pattern
is fine for systems whose structural changes don't need to be
visible to a later system in the same frame, but it makes ordering
ambiguous when one system's changes are read by another later in
the frame — those reads see the changes only if `Playback` already
fired.

A spot-check confirmed `DestroyEntity` / `RemoveComponent` is
always called against a snapshot list (NativeList collected before
the structural change) rather than inside a live SystemAPI.Query
iteration. No `Entities.ForEach` body issues structural changes
directly (only `NavMeshPathRequestSystem` uses the legacy path and
its body is read-only).

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| Systems/Work | `BuildingConstructionSystem.OnUpdate` direct `em.RemoveComponent` calls | L91 / L98-100 call `em.RemoveComponent<BuildOrder>(builder)` immediately after iterating a pre-collected `builders` `NativeList<Entity>`. The original `SystemAPI.Query` foreach finished at L78 — structural changes are issued post-iteration, safe. Recorded for cross-reference: any new in-loop structural change must keep this pattern. | [BuildingConstructionSystem.cs:91](../../../Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs#L91) | edge-only | park |
| Systems/Crystal | `NodeStateDeathInterceptSystem` — `CrystalNodeStateHelper.SetState` inside `for` loop | The helper calls `EntityManager.AddComponent` / `RemoveComponent` / `SetComponentData` for each dying node inside the `for (int i = 0; i < dyingNodes.Length; i++)` loop at L92-110. The loop iterates a pre-collected NativeList (not a live query), so structural changes are safe per call. Cross-reference. | [NodeStateDeathInterceptSystem.cs:104](../../../Assets/Scripts/Systems/Crystal/NodeStateDeathInterceptSystem.cs#L104) | edge-only | park |
| Systems/Sect | `SectFortitudeHpSystem` — RefRW direct write | L67 `health.ValueRW.Max = (int)(hp.Max * mult);` after `var hp = health.ValueRO;` at L66. The `.ValueRW` setter writes directly to the chunk — no copy-then-writeback needed (this is the memory-recall RefRW pattern done correctly). Cross-reference. | [SectFortitudeHpSystem.cs:67](../../../Assets/Scripts/Systems/Sect/SectFortitudeHpSystem.cs#L67) | edge-only | park |
| Systems/Combat | `EquipmentTierSystem.ApplyTierDiff` integer truncation | L108: `d.Value = (int)(d.Value * diff);` truncates to int per tier-move. Equipment tier moves are bounded (Base→Iron→Crystal→Veilsteel→Glow, max 4 hops) and the unit's `applied != target` short-circuits at L83 — so accumulated rounding error is bounded to ~3-4 points per stat. `degraded` not `wrong-result` because the magnitude is below the design's per-tier delta. | [EquipmentTierSystem.cs:108](../../../Assets/Scripts/Systems/Combat/EquipmentTierSystem.cs#L108) | degraded | park |

### C.3 Save/load component coverage

**This is the AC anchor row.** Task-081 (save/load pipeline) is
still in `stage: scope` as of HEAD `2ad11c1` — no snapshot writer
or reader has landed yet (`Glob` for `Save*.cs` / `*Snapshot*.cs` /
`*Persist*.cs` returns zero files under `Assets/Scripts/`). Per
the Edge Cases note in this task's body ("if 081 is still
mid-implementation, §C records the gap as-of the snapshot landed
on `test/all-fixes-rolled-up` HEAD"), every component below is
status **`MISSING`** with note `task-081 not yet implemented`.
When 081 lands, this whole subsection re-anchors against the
writer/reader code.

Components are grouped by source file; counts are
`IComponentData` + `IBufferElementData` (no `ISharedComponentData`
in this codebase). Total: 23 component files, 247 component
declarations. **All MISSING until task-081 ships.**

Note: `AI/Components/*` (AIScoutingComponents, AIManagerComponents)
and `AI/Core/AIBrain.cs` (AIBrain, SimpleAIState, AISharedKnowledge,
AIStrategyState, ResourceRequest) live OUTSIDE `Core/Components/`
but are part of the simulation. §A row 1 (task-085 ai-managers-removal)
will delete the dead `AIManagerComponents` and `AIScoutingComponents`
before save/load 081 lands, so their save-coverage status is
deliberately deferred — they belong to whichever set survives 085.

#### Per-file enumeration (status as-of HEAD `2ad11c1`)

| Component file | Component count | Status | Notes |
|---|---|---|---|
| [AbilityComponents.cs](../../../Assets/Scripts/Core/Components/AbilityComponents.cs) | 11 | MISSING | UnitAbility, AbilityActivated, Condemned, Fortified, IgniteBuff, VoidStrikeBuff, HealOverTime, SummonedUnit, BurningGround, MindControlled, StealthTag — task-081 not yet implemented |
| [BattalionComponents.cs](../../../Assets/Scripts/Core/Components/BattalionComponents.cs) | 9 | MISSING | BattalionTag, BattalionLeader, **BattalionMember** (IBufferElementData), BattalionMemberData, BattalionStanceData, DefaultStancePursuit, BattalionAlignmentState, LastAttackerEntity, BattalionAttackTarget — task-081 not yet implemented |
| [BuildingComponents.cs](../../../Assets/Scripts/Core/Components/BuildingComponents.cs) | 73 | MISSING | The single largest file. Includes BuildingTag, HallTag, BarracksTag, ArcheryRangeTag, HutTag, GathererHutTag, WallTag (+ WallHub/Segment/Instance/Tower/Gate families with `WallHubLink` / `WallEnclosureVertex` / `WallInstanceRef` IBufferElementData), VaultTag, FiendstoneKeepTag, TempleOfRidanTag, TempleLevel, TempleUpgradeState, ChapelTag, SectUniqueBuildingTag, TempleOwner, DeathAnimationState, BuildingCollapseState, Buildable, UnderConstruction, BuildingDamageState, BuildOrder, RepairOrder, DeferredDefense, Defense, TrainingState, BuildingRangedAttack, VaultStorage, BuildingSize, ObstacleTag, AgeUpState, SelfDestructTimer, ForgeStorage, **TrainQueueItem** (IBufferElementData), **TempleChapelSlot** (IBufferElementData), `FarmBuildOrder`, `ShrineRPGranted`, `WallConnection`, `WallEnclosureIncomeTag`, `WallInstanceParent`, `WallGateState`, `WallUpgradeState`, `OutpostTag`, `TradeHubTag`, `BazaarTag`, `SiegeWorkshopTag`, `SmelterTag`, `CrucibleTag`, `WatchTowerTag`, `PracticeRangeTag`, `SiegeYardTag`, `HuntingLodgeTag`, `LoggingStationTag`, `WarbrandFoundryTag`, `LonghouseTag`, `BatchTrainingTag`, `TotemTowerTag`, `FerSiegeYardTag`, `ChapelSmallTag`, `ChapelLargeTag`, `SectUniqueUnitTag`, `ChoiceBuildingTag` — task-081 not yet implemented |
| [BuildingUpgradeComponents.cs](../../../Assets/Scripts/Core/Components/BuildingUpgradeComponents.cs) | 3 | MISSING | BuildingUpgradeable, BuildingUpgradeState, BuildingUpgrading — task-081 not yet implemented |
| [CombatComponents.cs](../../../Assets/Scripts/Core/Components/CombatComponents.cs) | 5 | MISSING | Damage, AttackCooldown, Target, DamageTypeData, ArmorTypeData — task-081 not yet implemented |
| [CommandQueueComponents.cs](../../../Assets/Scripts/Core/Components/CommandQueueComponents.cs) | 3 | MISSING | **QueuedCommand** (IBufferElementData), CommandQueueActive, CommandQueueFrozen — task-081 not yet implemented |
| [CoreComponents.cs](../../../Assets/Scripts/Core/Components/CoreComponents.cs) | 20 | MISSING | FactionTag, FactionProgress, PresentationId, Health, MoveSpeed, Radius, LineOfSight, DesiredDestination, UserMoveOrder, AttackMoveTag, FormationSpeedOverride, GuardPoint, SmoothedDirection, StuckState, MovementCache, RallyPoint, HoldPositionTag, PatrolTag, PatrolAgent, **PatrolWaypoint** (IBufferElementData) — task-081 not yet implemented |
| [CrystalComponents.cs](../../../Assets/Scripts/Core/Components/CrystalComponents.cs) | 29 | MISSING | CrystalTag, CursedGroundTag, CrystalMainNodeTag, CrystalSubNodeTag, CrystalNode, CrystalSpreadState, CrystalNodeLevel, CrystalAIState, CrystalUnitTag, CrystalResourceValue, CursedGroundDPS, OwnerNode, CadaverTag, CadaverState, VeilstingerState, GodsplinterState, EnforcementAura, SuppressionAura, RestorationAura, CursedGroundReceding, LaserProjectileTag, CrystalBuff, CrystalDebuff, CrystalCadaverLifetime, CrystalExtinctionState, CrystalWaveState, CrystalWaveOrder, CrystalTrainingState, CrystalAutoBuild — task-081 not yet implemented |
| [EconomyComponents.cs](../../../Assets/Scripts/Core/Components/EconomyComponents.cs) | 0 | excluded-intentionally | File is a stub redirect to `Economy/FactionResources.cs` and `Economy/FactionPopulation.cs`. The actual Economy components live in `TheWaningBorder.Economy` namespace OUTSIDE `Core/Components/` — save/load coverage for those must be enumerated separately when 081 lands. |
| [EquipmentComponents.cs](../../../Assets/Scripts/Core/Components/EquipmentComponents.cs) | 10 | MISSING | FactionEquipmentTier, UnitEquipmentApplied, UnitTierOverride, ShieldBar, GlowReviveCooldown, SiegeShieldAura, AuraShieldBoost, HeroPhaseShield, GlowWeaponTag, GlowWeaponState — task-081 not yet implemented |
| [FactionCurseAggroModifier.cs](../../../Assets/Scripts/Core/Components/FactionCurseAggroModifier.cs) | 1 | MISSING | FactionCurseAggroModifier — task-081 not yet implemented |
| [GodPowerComponents.cs](../../../Assets/Scripts/Core/Components/GodPowerComponents.cs) | 2 | MISSING | GodPowerState, PendingGodPowerCast — task-081 not yet implemented |
| [NavMeshComponents.cs](../../../Assets/Scripts/Core/Components/NavMeshComponents.cs) | 2 | MISSING | NavMeshPathfollowState, **NavMeshWaypoint** (IBufferElementData) — task-081 not yet implemented. **Caveat:** pathfollow state is transient — likely a `derived` candidate (recompute on load) rather than `serialized`, but the call is task-081's. |
| [NodeStateComponents.cs](../../../Assets/Scripts/Core/Components/NodeStateComponents.cs) | 5 | MISSING | CrystalNodeState, NodeDormant, NodeUntargetable, NodeInvulnerabilityState, NodeVictoryState — task-081 not yet implemented |
| [ProjectileComponents.cs](../../../Assets/Scripts/Core/Components/ProjectileComponents.cs) | 4 | MISSING | Projectile, AOEProjectile, AOEShooterData, PiercingProjectile — task-081 not yet implemented. **Caveat:** in-flight projectiles are transient — likely `excluded-intentionally` (load picks up post-projectile combat state) but the call is task-081's. |
| [ResearchComponents.cs](../../../Assets/Scripts/Core/Components/ResearchComponents.cs) | 2 | MISSING | ResearchState, **ResearchQueueItem** (IBufferElementData) — task-081 not yet implemented |
| [ResourceComponents.cs](../../../Assets/Scripts/Core/Components/ResourceComponents.cs) | 2 | MISSING | IronMineTag, IronDepositState — task-081 not yet implemented |
| [RitualComponents.cs](../../../Assets/Scripts/Core/Components/RitualComponents.cs) | 11 | MISSING | ScholarTag, AcolyteTag, IconoclastTag, RitualState, ActiveRitualOnNode, PurifyCommand, ConvertNodeCommand, GlowPickupTag, GlowPickupState, GlowCarrier, GlowStored — task-081 not yet implemented |
| [RunaiTradeComponents.cs](../../../Assets/Scripts/Core/Components/RunaiTradeComponents.cs) | 11 | MISSING | TradeNodeTag, TradeHubSpawner, TradeNodePatrolSpawner, RunaiTraderState, RunaiPopOverride, FeraldisPopOverride, BazaarWagonTag, BazaarWagonState, BazaarPackCommand, BazaarUnpackCommand, PatrolAlertState — task-081 not yet implemented |
| [SpellComponents.cs](../../../Assets/Scripts/Core/Components/SpellComponents.cs) | 14 | MISSING | SpellBuff, SpellDebuff, Invulnerable, FortitudeHpApplied, WitnessVisionApplied, MarkedForSentence, VenerationFervor, AntiquityKills, SilenceVigilState, SilenceVigilArmor, BloodPool, InBloodPool, **SectUnitLeverApplied** (IBufferElementData), **SectActivePowerCooldown** (IBufferElementData) — task-081 not yet implemented |
| [TradeComponents.cs](../../../Assets/Scripts/Core/Components/TradeComponents.cs) | 7 | MISSING | TradingPostTag, CaravanTag, TradeUpgrades, CaravanFollowerTag, NotControllableTag, TradePatrolData, LastDamagedByFaction — task-081 not yet implemented |
| [UnitComponents.cs](../../../Assets/Scripts/Core/Components/UnitComponents.cs) | 21 | MISSING | UnitTag, UnitRank, UnitRankApplied, GlowAbilityState, UpgradePile, CanBuild, ArcherTag, BerserkerTag, CavalryTag, SiegeTag, SpearmanTag, ArcherState, ArrowProjectile, MinerTag, MinerState, ForgeSupplyOrder, UnhealableTag, CanHeal, LitharchTag, LitharchState, ArmyTag — task-081 not yet implemented |
| [VeilsteelCarryComponent.cs](../../../Assets/Scripts/Core/Components/VeilsteelCarryComponent.cs) | 1 | MISSING | VeilsteelCarry — task-081 not yet implemented |

#### Save/load coverage summary

| Status | Component count | Notes |
|---|---|---|
| `serialized` | 0 | no writer/reader exists |
| `derived` | 0 | will be the call for transient state (NavMesh, Projectile, MovementCache, SmoothedDirection, StuckState, GatherTimer-style transient state inside MinerState/TrainingState/etc) when task-081 ships |
| `MISSING` | 247 | every component in `Core/Components/` — the entire simulation |
| `excluded-intentionally` | 1 file (EconomyComponents.cs stub) | redirected to `TheWaningBorder.Economy` namespace; counted separately |

**Roll-up triage row** for save/load:

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| Save/Load | Component coverage gap | Task-081 (snapshot writer / reader) has not landed on HEAD `2ad11c1` — every IComponentData / IBufferElementData in `Core/Components/` is currently `MISSING` (247 across 22 files; EconomyComponents.cs is a stub). When 081 implementation phase starts, the writer's component list MUST be reviewed against this enumeration so the same set lands at once. Without that review, partial coverage (saving only "obvious" components like Health / FactionTag / LocalTransform) will produce hard-to-diagnose save-corruption bugs. | [task-save-load-system-081](../task-save-load-system-081/task.md) | wrong-result | spin-out |

### C.4 Command-router edge cases

CommandRouter (994 lines + 372-line lockstep partial) was read
end-to-end. Every public `Issue*` method runs the
`Entity.Null` / `em.Exists(unit)` / `em.HasComponent<T>` triad
before deref, plus the `NotControllableTag` filter for
LocalPlayer-source commands. The defensive layer is in good shape.
Remaining edges are about **bypassing the public API** or **state
the API doesn't check** (depleted resource, bad terrain).

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| Core/Commands | `CommandRouter.IssuePlaceBuilding` bypasses `IsValidBuildPosition` | The public API at L800 calls `PlaceBuildingDirect` (or queues for lockstep) WITHOUT calling `BuildCommandHelper.IsValidBuildPosition`. Player UI validates beforehand (BuildCommandPannel), but the AI path (`SimpleAISystem.TryBuildBuilding`) DOES call `IsValidBuildPosition` inside `TryFindBuildPosition`. Any future direct-`PlaceBuilding` caller (network replay, scripted spawn, modding API) could place on water / cliff / occupied tile. `static-only — needs repro` via a unit test that posts a bad position. | [CommandRouter.cs:800](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L800) | wrong-result | fix |
| Core/Commands | `CommandRouter.IssuePlaceBuilding` bogus `buildingId` falls through to `CreateDefault` | `BuildingFactory.Create` at line 73 has `_ => CreateDefault(em, buildingId, position, faction)` as the switch fallback — an unknown id spawns a generic 500-HP building with `PresentationId = 100`. No validation against TechTreeDB on the command-router path. UI restricts the id set, but the same risk vector as the row above applies for non-UI callers. | [BuildingFactory.cs:73](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L73) | wrong-result | spin-out |
| Core/Commands | `GatherCommandHelper.Execute` doesn't check deposit `Depleted` flag | L37 checks `em.Exists(resourceNode)` but not `IronDepositState.Depleted == 1` / `CadaverState.Depleted == 1`. A right-click on a depleted-but-not-yet-destroyed node attaches `GatherCommand`, miner transitions to MovingToDeposit, MiningSystem catches the depleted state at L188 and bounces the miner. UX-correct but the miner takes one wasted think-tick. | [GatherCommand.cs:37](../../../Assets/Scripts/Core/Commands/CommandTypes/GatherCommand.cs#L37) | edge-only | park |
| Core/Commands | `CommandRouter.SetRallyPoint` direct-callers vs lockstep payload | Already in §A row 14 — comment at L344-347 explicitly admits the lockstep path drops `targetEntity`. In multiplayer, a rally point on a resource node behaves differently than in single-player (host treats it as a gather rally; remote sees a position-only rally and miners don't auto-gather). Cross-ref task-087. | [CommandRouter.cs:344](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L344) | wrong-result | spin-out |
| Core/Commands | `CommandHelper.ClearAllCommands` misses `BazaarPackCommand` / `BazaarUnpackCommand` | L930-993 clears 21 different command components but the two Runai bazaar wagon commands (`BazaarPackCommand` / `BazaarUnpackCommand` declared at [RunaiTradeComponents.cs:112](../../../Assets/Scripts/Core/Components/RunaiTradeComponents.cs#L112)) are not in the clear list. Issuing any other command on a Bazaar wagon mid-pack would leave the pack command attached and the entity would re-pack on the next BazaarPackSystem tick. `static-only — needs repro` — task-066 (hut transform) may rewire this whole flow. | [CommandRouter.cs:930](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L930) | wrong-result | spin-out |
| Core/Commands | `IssueGodPower` / `IssueEquipmentUpgrade` lockstep header comment vs behaviour | §A row 13 flagged the header comment "multiplayer logs and drops" as drift — re-verified during Phase 3. The current implementation actually wires both commands through `QueueEquipmentUpgradeForLockstep` / `QueueGodPowerForLockstep` + `LockstepManager.ExecuteCommand` (CommandRouter.LockstepQueue.cs L343-369, LockstepManager.cs L565-580). Behaviour is correct; the header comment is stale and should be removed. Cross-ref task-087. | [CommandRouter.cs:391](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L391) | edge-only | spin-out |

### C.5 AI brain stall conditions

`SimpleAISystem` (1064 LoC) was read end-to-end. It's the only
live AI driver — `AI/Managers/*` and `AI/Behaviors/*` are
`[DisableAutoCreation]` per §A row 1. Stall analysis is
**SimpleAISystem-only**. The replacement loop is well-bounded
(uses `aiState.DesiredMilitary` counters; never rewinds
`StepIndex`); no unbounded list / queue growth observed. The risk
surface is in **dead-target cleanup** (one suspect row below) and
**dead-code AI/Behaviors that still ship in the build** (recorded
for cross-reference, NOT a new bug since §A task-085 will delete
them).

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| AI/SimpleAI | `TryLaunchAttack.ChooseAttackTarget` doesn't refresh on target death | L734 picks the closest enemy economy target once per build-order `LaunchAttack` step. Once the step issues `AttackMoveCommandHelper.Execute` to every idle military unit at L739-740, the step latches as complete (returns true) and `StepIndex` advances. If the chosen target dies during the march, units will keep attack-moving toward `targetPos` (the snapshot position) — the build order won't re-issue a fresh target. `degraded` because units are still on attack-move and will engage anything on the way. | [SimpleAISystem.cs:734](../../../Assets/Scripts/AI/SimpleAISystem.cs#L734) | degraded | spin-out |
| AI/SimpleAI | `ReplaceLostUnits` deficit calc — no upper bound | L574-605 issues one `TryTrainUnit` per category per tick when alive + queued < Desired. If the build order's `DesiredMilitary` counter is large (e.g. 10) and the AI keeps losing units faster than they train, the train queue stays at the `MaxProductionQueue = 5` cap forever. Not strictly a stall — just steady-state pressure. Recorded for cross-reference; design-intended behaviour. | [SimpleAISystem.cs:574](../../../Assets/Scripts/AI/SimpleAISystem.cs#L574) | edge-only | park |
| AI/Behaviors (dead) | `AIScoutingBehavior.UpdateSharedKnowledge` indentation bug | L572-574: `if (sightings[i].IsBase == 1) enemyBasesSpotted++; enemyArmiesSpotted++;` — no braces, so `enemyArmiesSpotted++` runs for EVERY sighting (including bases), inflating the count. Hard `wrong-result` bug in dead code. The `[DisableAutoCreation]` saves the runtime; the file ships in the build as-is. Subsumed under task-085 (AI managers removal) — file will be deleted before save/load 081 lands. | [AIScoutingBehavior.cs:572](../../../Assets/Scripts/AI/Behaviors/AIScoutingBehavior.cs#L572) | wrong-result | spin-out |
| AI/SimpleAI | `FindFactionBuilding<TTag>` picks first match without `UnderConstruction` exclusion | L799-814: the helper returns `entities[i]` for the first faction match — it does NOT filter out `UnderConstruction`. Callers like `TryAgeUp` at L518-519 then check `UnderConstruction` only on the Hall but not on the choice building. `FactionHasChoiceBuilding` at L824 uses `GetCompletedFactionChoiceBuilding` which DOES exclude — but other call sites (`AssignIdleMiners.dropoff = FindFactionBuilding<HallTag>` at L898) don't, so the AI might dispatch miners to a Hall still mid-construction. Mining systems likely re-find a deposit but the destination is the Hall position. `static-only — needs repro` with a half-built Hall + idle miner scenario. | [SimpleAISystem.cs:799](../../../Assets/Scripts/AI/SimpleAISystem.cs#L799) | degraded | spin-out |

### C.6 Lockstep determinism gaps

`LockstepTypes.cs` + `LockstepManager.cs` + `CommandRouter.LockstepQueue.cs`
audit. The `R`-format float serialization + InvariantCulture
parse + `NetworkIdGenerator` tick-partitioning + entity-source
filter (`needsEntity` switch at L407) are all clean. The
remaining risks are about **the serializer's payload coverage**
and **the dispatcher's sort key correctness**.

| Surface | Item | Issue | File:line | Severity | Triage |
|---|---|---|---|---|---|
| Core/Multiplayer | `LockstepCommand.Serialize` drops `CommandIndex` from wire payload | `Serialize()` at [LockstepTypes.cs:91](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L91) writes `Type,EntityId,X:R,Y:R,Z:R,TargetId,SecondaryId,BuildingId` — `CommandIndex` is NOT in the format string. On the receiving side, `ProcessTickMessage` at [LockstepManager.cs:730](../../../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L730) sets `cmd.PlayerIndex = playerIndex; cmd.Tick = tick;` and **leaves CommandIndex at default 0**. `ProcessTick` at L371-375 then sorts `allCommands` by `(PlayerIndex, CommandIndex)`. Locally-queued commands have a proper monotonic CommandIndex (set at QueueCommand L128); remote commands all have CommandIndex=0. `List<T>.Sort` is unstable, so the order of multiple same-player remote commands is not guaranteed equal across peers — desync. | [LockstepTypes.cs:95](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L95) | wrong-result | fix |
| Core/Multiplayer | `glowByFaction` accumulates Glow across multiple temples per faction | `GodPowerSystem.OnUpdate` at [GodPowerSystem.cs:68](../../../Assets/Scripts/Systems/Economy/GodPowerSystem.cs#L68) sums `GlowStored.Amount` across every TempleOfRidan owned by a faction (`prior + templeStored[i].Amount`). If a faction owns two temples (sect chapel paths can create a second), stored Glow doubles up and the cooldown discount compounds (`0.8^total_stored` — exponential in temple count). Design specifies one Temple per faction; if the design holds, this is `edge-only`. If two temples become legal (Petriarchy framing?), `wrong-result`. | [GodPowerSystem.cs:68](../../../Assets/Scripts/Systems/Economy/GodPowerSystem.cs#L68) | edge-only | park |
| Core/Multiplayer | `LockstepCommand.Serialize` truncates `BuildingId` containing comma | `Serialize()` at [LockstepTypes.cs:95](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L95) writes `{7}` directly without escaping; `Deserialize` splits on `,`. None of the current building ids contain a comma (verified — all match `^[A-Za-z_]+$`), but a future id collision (e.g. `Feraldis_Foo,Bar`) would silently drop everything after the comma. Static-only; current Sect chapel ids and culture-prefixed ids are safe. Cross-reference to lockstep hardening backlog. | [LockstepTypes.cs:95](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L95) | edge-only | park |
| Core/Multiplayer | `BroadcastTick` uses `|` as separator and concatenates entire tick payload into one UDP datagram | `LockstepManager.BroadcastTick` at L612-636 packs **every command for a tick** into a single UDP packet (`sb.Append("|"); sb.Append(cmd.Serialize());`). A high command burst (e.g. 50 units queued same tick = ~50 commands × 80 bytes ≈ 4 KB) approaches the MTU. UDP fragmentation will work but lost fragments lose the whole tick. Players issuing rapid clicks (build queue rampage) on slow networks would silently desync. `static-only — needs repro` via UDP MTU tests. | [LockstepManager.cs:614](../../../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L614) | wrong-result | spin-out |
| Systems/Sect | `SectUnitLeverSystem.ApplyDelta` accumulated multiplier semantics | L140: `float diff = 1f + (spec.HpMultiplier - 1f) * scalar;` where `scalar = LevelScalar(level) / LevelScalar(appliedLevel)`. If `spec.HpMultiplier = 1.5` and the unit is being moved from level 1 (LevelScalar=1) to level 2 (LevelScalar=1.5), `scalar = 1.5` and `diff = 1 + 0.5*1.5 = 1.75` — multiplying current HP by 1.75x. Intent is unclear from comments — if the design wants "level 2 = +50% on top of level 1" the formula should be `diff = LevelScalar(level)/LevelScalar(appliedLevel) when applying a level-aware spec multiplier`. `static-only — needs repro` with a sect-lever HP test. | [SectUnitLeverSystem.cs:140](../../../Assets/Scripts/Systems/Sect/SectUnitLeverSystem.cs#L140) | wrong-result | spin-out |

### Triage summary — §C

**Drop now (zero-risk):**

_(none — every §C row is either a real defect, a guard pattern worth recording, or an edge that needs a repro.)_

**Wire or decide:**

_(none — §C did not surface any UI / route-handler gaps; those landed in §A.)_

**Fix soon:**

1. Save/load component coverage gap — every IComponentData / IBufferElementData in `Core/Components/` is `MISSING` because task-081 hasn't landed yet. Roll-up child task **task-096** (`save-load-coverage-gap`). The list in §C.3 is the canonical input set for 081's writer review.
2. `CommandRouter.IssuePlaceBuilding` bypasses `IsValidBuildPosition` for non-UI callers ([CommandRouter.cs:800](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L800)) and bogus `buildingId` falls through to `BuildingFactory.CreateDefault` ([BuildingFactory.cs:73](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L73)). Spun out as **task-097** (`commandrouter-placebuilding-validation`).
3. `CommandHelper.ClearAllCommands` misses Runai bazaar wagon commands ([CommandRouter.cs:930](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L930)). Spun out as **task-098** (`clearallcommands-bazaar-coverage`).
4. `LockstepCommand.Serialize` drops `CommandIndex` from the wire payload → unstable sort within same-player remote command batches ([LockstepTypes.cs:95](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L95)). Spun out as **task-099** (`lockstep-command-index-serialization`).
5. `LockstepManager.BroadcastTick` packs all commands into one UDP datagram; high-burst ticks approach MTU ([LockstepManager.cs:614](../../../Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L614)). Spun out as **task-100** (`lockstep-tick-payload-mtu`).
6. `SimpleAISystem.TryLaunchAttack` doesn't refresh target on death ([SimpleAISystem.cs:734](../../../Assets/Scripts/AI/SimpleAISystem.cs#L734)). Spun out as **task-101** (`ai-launchattack-target-refresh`).
7. `SimpleAISystem.FindFactionBuilding<TTag>` doesn't exclude `UnderConstruction` for non-Hall callers ([SimpleAISystem.cs:799](../../../Assets/Scripts/AI/SimpleAISystem.cs#L799)). Subsumed under task-101 (same scope: SimpleAI tactical correctness).
8. `SectUnitLeverSystem.ApplyDelta` multiplier semantics ambiguous — possible HP over-application on level-up ([SectUnitLeverSystem.cs:140](../../../Assets/Scripts/Systems/Sect/SectUnitLeverSystem.cs#L140)). Spun out as **task-102** (`sect-unit-lever-multiplier-semantics`).
9. `AIScoutingBehavior.UpdateSharedKnowledge` indentation bug ([AIScoutingBehavior.cs:572](../../../Assets/Scripts/AI/Behaviors/AIScoutingBehavior.cs#L572)). **Subsumed under task-085** (AI managers removal — file will be deleted, not patched).
10. `CommandRouter.SetRallyPoint` lockstep payload drops `targetEntity` ([CommandRouter.cs:344](../../../Assets/Scripts/Core/Commands/CommandRouter.cs#L344)). **Subsumed under task-087** (commandrouter-lockstep-drift — cross-ref to §A row 14).
11. `CommandRouter` `IssueGodPower` / `IssueEquipmentUpgrade` header comments are stale (behaviour is actually wired through lockstep). **Subsumed under task-087** (comment cleanup is part of the lockstep drift fix).

**Park (intentionally dormant / defensive / false alarm on inspection):**

1. DeathSystem battalion / blood-pool reads ([DeathSystem.cs:100](../../../Assets/Scripts/Systems/Combat/DeathSystem.cs#L100)) — all guarded; recorded as audit-clean cross-reference.
2. TrainingSystem L89 `GetComponentData<FactionTag>` — implicitly safe (every trainer building carries FactionTag); recorded for future-proofing.
3. NodeStateDeathInterceptSystem L89 — guarded by `_victoryQuery.IsEmpty` check.
4. NodeVictorySystem L57 — guarded by `RequireForUpdate(_victoryQuery)`; fragile to future changes but currently safe.
5. BuildingConstructionSystem in-loop structural changes — pre-collected snapshot list, safe.
6. NodeStateDeathInterceptSystem in-loop structural changes — same pattern, safe.
7. SectFortitudeHpSystem RefRW direct write — done correctly; no copy-and-writeback.
8. EquipmentTierSystem integer truncation — bounded to ~3-4 stat points across the whole tier ladder; below design granularity.
9. GatherCommandHelper doesn't check `Depleted` — MiningSystem catches and bounces; one wasted think-tick cost only.
10. GodPowerSystem multi-temple Glow sum — design says one Temple per faction; edge-only.
11. LockstepCommand BuildingId comma-escape — current building id set is comma-safe.
12. ReplaceLostUnits high-deficit pressure — design-intended steady-state behaviour.

## §D Performance hot spots

Audit performed against HEAD `2ad11c1` on branch `test/all-fixes-rolled-up`.
No frame-cost measurements were captured in this pass (per AC: `needs-measurement`
is the documented escape). Static-analysis-obvious rows (LINQ-equivalent
allocations in tick paths, O(N×M) loops with managed-list growth, query
rebuilds in HudBridge / SystemBase OnUpdate) are marked `n/a — static analysis`
and graduate directly to a `fix` child task. Rows that need real measurement
before triage roll up into a single `task-perf-measurement-backlog-103` so
the backlog isn't one stub per row.

Cross-refs:
- §A row 7 (`TargetingSystem` per-tick query) → covered by **task-089**; §D
  row 1 adds measurement context.
- §A row 8 (`FogOfWarSystem` per-tick query) → covered by **task-089** (same
  refactor scope); §D row 5 adds the per-entity `MaterialPropertyBlock`
  alloc that §A didn't surface.
- §A row 9 (`SectActivePowerSystem` per-cast query) → already `park` in
  §A; §D row 13 tags `needs-measurement` for the backlog.
- §A row 12 (`HudBridge` query consolidation) → covered by **task-090**;
  §D rows 7/8 extend with push-frequency + duplicate-query specifics.

| # | Surface | Item | Issue | File:line | Measured? | Triage |
|---|---|---|---|---|---|---|
| 1 | Systems/Combat | `TargetingSystem.OnUpdate` per-tick query + 4 snapshots | Builds `SystemAPI.QueryBuilder().WithAll<LocalTransform,FactionTag,Health>().WithNone<BattalionLeader,NodeUntargetable>()` every tick, then `ToEntityArray` + 3× `ToComponentDataArray<...>` to feed the spatial hash. Four `Allocator.Temp` arrays per tick (sized N = every entity with Health/FactionTag). Spatial hash itself uses `NativeParallelMultiHashMap` sized `N*2` — also `Temp`. At 60 Hz with ~300 combatants, that's 4×300 = 1.2 k `Temp` allocations + ~1.8 k hash entries per second baseline. The query should resolve in `OnCreate` (cached `EntityQuery` field) and the snapshot arrays should remain `Allocator.Temp` (correct as-is — only the query handle wastes work). | [TargetingSystem.cs:68-82](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68) | needs-measurement | spin-out |
| 2 | Systems/Combat | `BuildingCombatSystem.OnUpdate` per-tick query + per-building O(N) scan + per-building `NativeList` alloc | Builds `SystemAPI.QueryBuilder().WithAll<LocalTransform,FactionTag,Health>()` (broader than `TargetingSystem` — no battalion-leader / node exclusions) every tick (L34-36), then for **every building** with `BuildingRangedAttack` allocates a fresh `NativeList<TargetCandidate>(maxTargets, Allocator.Temp)` (L63) and runs a linear scan over **all** entities with Health (L65-99). With 8 keeps × 1 hall × 4 towers × 300 entities = ~3 900 distance checks per tick. Distance sort is bubble-sort over `maxTargets` (typically 3-4) so the sort cost is fine — but the outer scan is unbounded. Should: cache query in `OnCreate`, build a spatial hash like `TargetingSystem` does. | [BuildingCombatSystem.cs:34-99](../../../Assets/Scripts/Systems/Combat/BuildingCombatSystem.cs#L34) | n/a — static analysis | spin-out |
| 3 | Presentation | `PresentationSpawnSystem.SyncTransforms` ToEntityArray + per-entity managed `GetComponent` lookups every frame | `_presentationQuery.ToEntityArray(Allocator.Temp)` + `ToComponentDataArray<LocalTransform>` (L1060-1061), then for **every visual** the loop calls `GetComponent<BuildingVisualSinkDepth>`, `GetComponent<BuildingRiseData>`, `GetComponent<ProceduralScaleTag>` (L1080, L1087, L1111) — three managed dictionary lookups per entity per frame. At 60 Hz with ~400 spawned visuals, that's ~72 k GetComponent calls/sec. `_spawnedEntities` HashSet already records every spawned entity — cache the rise/sink/scale components on a `EntityVisualMetadata` struct keyed by Entity instead of GetComponent in the hot loop. Two `Allocator.Temp` arrays per frame are smaller next to this — but the query is cached, so they're acceptable. | [PresentationSpawnSystem.cs:1056-1115](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1056) | needs-measurement | spin-out |
| 4 | Presentation | `PresentationSpawnSystem.CleanupDestroyedEntities` allocates `List<Entity>` every frame | `var toRemove = new System.Collections.Generic.List<Entity>();` at [PresentationSpawnSystem.cs:212](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L212) inside `Update()` — managed list churn at 60 Hz. Even if usually empty (no entities died this frame), the allocation still fires. Should be a persistent `List<Entity>` cleared each frame, or a `NativeList<Entity>(Allocator.Temp)`. | [PresentationSpawnSystem.cs:212](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L212) | n/a — static analysis | spin-out |
| 5 | Systems/Visibility | `FogVisibilitySyncSystem.OnUpdate` per-frame `FindFirstObjectByType` + 3 uncached queries + per-entity `MaterialPropertyBlock` allocation | Three problems compound: (a) `Object.FindFirstObjectByType<EntityViewManager>()` every frame (L122) — Unity's reflection-based scene walk, ~0.1-0.5 ms per call; (b) `em.CreateEntityQuery(...)` rebuilt 3-4× per frame across the no-fog and with-fog branches (L130, L146, L176); (c) `new MaterialPropertyBlock()` allocated **per entity per frame** inside the foreach (L206, L244, L256) — that's a managed allocation × ~400 visuals × 60 Hz = 24 k MaterialPropertyBlock allocations/sec. `EntityViewManager.Instance` static accessor exists and would replace the `FindFirstObjectByType`; queries should cache in `OnCreate` (this is a `SystemBase`, so `GetEntityQuery` is the right pattern); MPB can be reused across iterations. | [FogOfWarSystem.cs:122-275](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L122) | n/a — static analysis | spin-out |
| 6 | Systems/Visibility | `FogOfWarSystem.OnUpdate` per-tick query + 4 snapshots | `em.CreateEntityQuery(LineOfSight, LocalTransform, FactionTag, Exclude<CrystalTag>)` rebuilt every tick (L45-49) followed by `ToEntityArray` + 3× `ToComponentDataArray<...>` (L51-54) — same shape as TargetingSystem row 1 but at a separate tick boundary. Already flagged in §A row 8; §D extends with the per-entity work: only `mgr.Stamp(faction, position, radius)` per entity in the loop (cheap — writes into a `byte[]` grid). The query churn is the cost, not the loop. | [FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45) | needs-measurement | spin-out |
| 7 | UI/Web | `HudBridge.Update` push cadence runs all 9 push helpers at 30 Hz unconditionally | `Update()` (L429-451) accumulates `_accumCheap` against `pushHz = 30` (L45) and once per ~33 ms fires `PushMenu / PushResources / PushObjectives / PushSelection / PushCosts / PushCultureChoice / PushBuilderState / PushSectsVisibility / PushSects`. Most of these are guarded by `PushIfChanged` (string compare against `_lastJson`), so the C#→JS send is cheap if nothing changed — BUT building the JSON to compare against still allocates a string via `StringBuilder.ToString` every push. With 9 topics × 30 Hz × ~200-byte payloads = ~54 KB/sec of throwaway strings on idle. `PushIfChanged` should hash-check, not string-compare; or topics should declare their own update frequency (e.g. `resources` at 4 Hz, `selection` at 30 Hz, `sects` at 1 Hz). | [HudBridge.cs:429-451](../../../Assets/Scripts/UI/Web/HudBridge.cs#L429) | needs-measurement | spin-out |
| 8 | UI/Web | `HudBridge.PushSectsVisibility` + `PushSects` both rebuild the same Temple query at 30 Hz | `PushSectsVisibility` at L466-468 calls `em.CreateEntityQuery(TempleOfRidanTag, FactionTag)` then `ToEntityArray(Allocator.Temp)` (L469); `PushSects` at L514-516 immediately rebuilds the **same query** + the same `ToEntityArray` (L517). Two query creations + two Temp arrays per push, 30 Hz → 60 query rebuilds/sec just for sect rail visibility. Already in §A row 12 / task-090; §D adds the duplicate-call cost: even if the query were cached, the two-pass approach can be collapsed into one query → one loop → both flags. | [HudBridge.cs:466](../../../Assets/Scripts/UI/Web/HudBridge.cs#L466) | n/a — static analysis | spin-out |
| 9 | UI/Web | `HudBridge.JsonEscape` allocates a fresh `StringBuilder` per call | At [HudBridge.cs:1494](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1494) `var sb = new System.Text.StringBuilder(s.Length + 8);` — called for every tooltip / display name / sect description that crosses the C#→JS boundary. Inside `PushSelection` (L1244, L1413) the function runs per selection field per push. The existing `_sb` field on the class is the cached StringBuilder; `JsonEscape` should reuse it (or accept it as an `out` param) instead of allocating per call. Same pattern at [HudBridge.cs:670](../../../Assets/Scripts/UI/Web/HudBridge.cs#L670) (`var sb = new StringBuilder(s.Length + 8);` in some other helper). | [HudBridge.cs:1494](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1494) | n/a — static analysis | spin-out |
| 10 | Systems/Training | `TrainingSystem.FindFactionDropOff` rebuilds 2 queries per rally call | `FindFactionDropOff` at [TrainingSystem.cs:334](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs#L334) creates a fresh `em.CreateEntityQuery(GathererHutTag, FactionTag)` AND `em.CreateEntityQuery(HallTag, FactionTag)` on every call. Called whenever a freshly trained gatherer needs a drop-off site (rally-issued gather command). Frequency: low (≤ 1× per unit trained), but the static helper is reachable from any system, so cost compounds with batch training. Cache both queries on a `TrainingSystem` instance and pass them in, or move the helper into the SystemBase. Same pattern at [AgeUpSystem.HasFactionTemple:145](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L145). | [TrainingSystem.cs:334](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs#L334) | n/a — static analysis | spin-out |
| 11 | Systems/AI | `FeraldisRaiderPatrolSystem.OnUpdate` per-tick enemy query + 4 snapshots | At [FeraldisRaiderPatrolSystem.cs:37-45](../../../Assets/Scripts/Systems/AI/FeraldisRaiderPatrolSystem.cs#L37) creates `em.CreateEntityQuery(UnitTag, FactionTag, LocalTransform, Health)` per tick then `ToEntityArray` + 3× `ToComponentDataArray<...>`. Same pattern as TargetingSystem. The raider patrol cadence is throttled internally (raiders pick targets on patrol-state change), but the query rebuild fires every OnUpdate. Cache in `OnCreate` via `GetEntityQuery`. Cross-ref task-089 — this is the same fix shape. | [FeraldisRaiderPatrolSystem.cs:37](../../../Assets/Scripts/Systems/AI/FeraldisRaiderPatrolSystem.cs#L37) | needs-measurement | spin-out |
| 12 | Systems/Movement | `NavMeshManager.SyncBuildings` allocates managed `Dictionary<Entity, BuildingRecord>` every 0.5 s | At [NavMeshManager.cs:203](../../../Assets/Scripts/Systems/Movement/NavMeshManager.cs#L203) `var current = new Dictionary<Entity, BuildingRecord>(entities.Length + obstacleEntities.Length);` — a fresh managed dict per 0.5-s rebuild cycle. Plus 2 ECS queries built per call (L187, L196). Low frequency (2 Hz) and only touches buildings + obstacles, so absolute cost is small; recorded for completeness. The dict could be a persistent field cleared each sync. | [NavMeshManager.cs:203](../../../Assets/Scripts/Systems/Movement/NavMeshManager.cs#L203) | n/a — static analysis | park |
| 13 | Systems/Sect | `SectActivePowerSystem` per-cast queries (Damage/Heal/Buff dispatchers) | `ApplyCircleDamage` / `ApplyCircleHeal` / `ApplyCircleBuff` at lines 286 / 309 / 333 build `em.CreateEntityQuery(UnitTag, LocalTransform, FactionTag, Health)` on every fire. Already noted in §A row 9 as `park` because cast frequency is low (active powers have multi-second cooldowns). Re-tagged here with `needs-measurement` so if a future sect spec lowers the cooldown or a script fires multiple casts per second, the measurement backlog catches it. Also: `TryGetFactionTemple` at L163-167 does the same per-cast query rebuild. | [SectActivePowerSystem.cs:286](../../../Assets/Scripts/Systems/Sect/SectActivePowerSystem.cs#L286) | needs-measurement | park |
| 14 | Systems/Combat | `ProjectileSystem` cached AOE query + once-per-frame snapshots | Recorded as **exemplar** — `_aoeTargetQuery` is cached in `OnCreate` (L44-48) and snapshots are taken **once at the top of `OnUpdate`** (L67-70) and shared across every projectile (per the in-source Fix #213 comment). This is the pattern §D rows 1/2/6/11 should converge on. No change needed; row exists so the task-089 refactor has a target shape. | [ProjectileSystem.cs:44](../../../Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L44) | n/a — static analysis | park |
| 15 | Systems/Movement | `UnitSeparationSystem` cached queries + throttled to 10 Hz | Recorded as **exemplar** — 4 queries cached in `OnCreate` (L51-66), `OnUpdate` throttled to `UpdateInterval = 0.1 f` (L32, L76-79). The spatial-hash + push-force pattern is correct. No change needed. | [UnitSeparationSystem.cs:51](../../../Assets/Scripts/Systems/Movement/UnitSeparationSystem.cs#L51) | n/a — static analysis | park |
| 16 | Systems/Movement | `BattalionSyncSystem` persistent `NativeArray` caches grow-only | Recorded as **exemplar** — 9 persistent `NativeArray<...>` fields allocated in `OnCreate` (L71-79), grown on demand via `EnsureMemberCapacity` instead of per-frame `Temp` alloc. Disposes in `OnDestroy`. This is the right pattern for any system that re-walks the same archetype every tick. | [BattalionSyncSystem.cs:71](../../../Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs#L71) | n/a — static analysis | park |
| 17 | Systems/Work | `MiningSystem` cached queries + `Allocator.Temp` ECB | Recorded as **exemplar** — `_hallDropoffQuery` / `_hutDropoffQuery` / `_ironDepositQuery` all cached in `OnCreate` (L54-72) via `state.GetEntityQuery`. Per-tick `ecb = new EntityCommandBuffer(Allocator.Temp)` (L81) is the correct pattern for in-loop structural changes. No change needed. | [MiningSystem.cs:54](../../../Assets/Scripts/Systems/Work/MiningSystem.cs#L54) | n/a — static analysis | park |
| 18 | World/Terrain | `TerrainUtility.GetHeight` called per visual per frame in `PresentationSpawnSystem.SyncTransforms` | `pos.y = TerrainUtility.GetHeight(pos.x, pos.z);` at [PresentationSpawnSystem.cs:1070](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1070) is called for **every** visual every frame. `TerrainUtility.GetHeight` is the managed sample helper (heightmap interpolation). At 60 Hz × 400 visuals = 24 k height samples/sec just for visual placement. For buildings (static positions) the height should cache; for units it's the cost of fidelity (terrain following). Plus the same `TerrainUtility.GetHeight` is hit by `MovementSystem` and `UnitSeparationSystem` for slope checks. Should profile to decide whether the heightmap sampler needs a fast-path / SOA layout. | [PresentationSpawnSystem.cs:1070](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1070) | needs-measurement | spin-out |

### Triage summary — §D

**Drop now (zero-risk):**

_(none — §D rows are either real allocation costs that warrant a refactor or exemplar patterns recorded for reference.)_

**Wire or decide:**

_(none — §D is structural / allocation-shape; no UI surfaces to wire.)_

**Fix soon:**

1. `TargetingSystem` per-tick query + 4 snapshots ([TargetingSystem.cs:68](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68)). **Cross-ref task-089** (`ecs-query-caching-hot-systems`) — adds row to its scope; no new stub.
2. `BuildingCombatSystem` per-tick query + per-building O(N) target scan ([BuildingCombatSystem.cs:34](../../../Assets/Scripts/Systems/Combat/BuildingCombatSystem.cs#L34)). **Cross-ref task-089** — adds row to its scope; no new stub.
3. `PresentationSpawnSystem.SyncTransforms` per-entity `GetComponent` hot loop ([PresentationSpawnSystem.cs:1056](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1056)). Spun out as **task-104** (`presentation-sync-component-cache`).
4. `PresentationSpawnSystem.CleanupDestroyedEntities` per-frame `List<Entity>` alloc ([PresentationSpawnSystem.cs:212](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L212)). Subsumed under task-104 (same file, same scope: PresentationSpawnSystem hot-path cleanup).
5. `FogVisibilitySyncSystem` per-frame `FindFirstObjectByType` + uncached queries + per-entity `MaterialPropertyBlock` alloc ([FogOfWarSystem.cs:122](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L122)). Spun out as **task-105** (`fog-visibility-sync-perf`).
6. `FogOfWarSystem.OnUpdate` per-tick query + 4 snapshots ([FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45)). **Cross-ref task-089** — adds row to its scope.
7. `HudBridge.Update` push cadence — 9 topics at 30 Hz with StringBuilder churn ([HudBridge.cs:429](../../../Assets/Scripts/UI/Web/HudBridge.cs#L429)). Spun out as **task-106** (`hudbridge-push-cadence-tiers`).
8. `HudBridge.PushSectsVisibility` + `PushSects` duplicate Temple query at 30 Hz ([HudBridge.cs:466](../../../Assets/Scripts/UI/Web/HudBridge.cs#L466)). **Cross-ref task-090** (`hudbridge-query-consolidation`) — query caching already in scope; subsumed.
9. `HudBridge.JsonEscape` per-call `StringBuilder` alloc ([HudBridge.cs:1494](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1494)). Subsumed under task-106 (same push-path scope).
10. `TrainingSystem.FindFactionDropOff` + `AgeUpSystem.HasFactionTemple` rebuild queries per call ([TrainingSystem.cs:334](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs#L334)). Spun out as **task-107** (`static-helper-query-caching`).
11. `FeraldisRaiderPatrolSystem` per-tick enemy query ([FeraldisRaiderPatrolSystem.cs:37](../../../Assets/Scripts/Systems/AI/FeraldisRaiderPatrolSystem.cs#L37)). **Cross-ref task-089** — adds row to its scope.
12. `TerrainUtility.GetHeight` called per visual per frame ([PresentationSpawnSystem.cs:1070](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L1070)). Roll into measurement backlog **task-103** — refactor only if measurement shows it's hot.

**Park (intentionally dormant / defensive / exemplar):**

1. `NavMeshManager.SyncBuildings` managed Dict alloc every 0.5 s — low frequency, acceptable.
2. `SectActivePowerSystem` per-cast queries — multi-second cooldowns, `needs-measurement` if cooldowns drop.
3. `ProjectileSystem` cached AOE query — **exemplar**, no change.
4. `UnitSeparationSystem` cached queries + 10-Hz throttle — **exemplar**, no change.
5. `BattalionSyncSystem` persistent `NativeArray` caches — **exemplar**, no change.
6. `MiningSystem` cached queries + Temp ECB — **exemplar**, no change.

Measurement backlog **task-103** (`perf-measurement-backlog`) rolls up every `needs-measurement` row (rows 1, 3, 6, 7, 11, 13, 18) so we don't open one stub per row. Once measurements land, rows that show real cost graduate to fixes; rows below noise threshold flip to `park`.

## Out of Scope

- **No production code edits.** All concrete fixes spin out as child
  tasks. The implementation stage of 082 only writes findings into
  `task.md` and creates child task stubs.
- **No edits to `docs/Design/*`.** Design-doc alignment is the job of
  the existing per-doc tasks (065 for Age 0, 067/069/070/071/072/074/
  075/076/078/079/080 for Age 1 slices). 082 surfaces drift; it does
  not resolve it.
- **No rebalancing decisions.** §B records the divergence between code
  and doc. It does not pick which side is right — that's the human's
  call on each spun-out child task.
- **No new doc authoring.** If a feature exists in code with no doc
  coverage (reverse-drift), 082 records it as a §B row tagged
  `doc-missing — needs spec`. It does not write the missing doc.
- **No measurement infrastructure work.** §D rows tagged
  `needs-measurement` get rolled into a single measurement backlog
  child task rather than 082 building profiling tooling itself.
- **No multiplayer protocol changes.** §C lockstep / determinism rows
  surface gaps; protocol fixes are separate (and likely tagged
  critical-priority on spin-out).
- **No `.deft/memory/*` updates from 082 directly.** Consolidation runs
  on the normal cadence after child tasks resolve.
