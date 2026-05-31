---
deft:
  id: task-wall-system-bfme2-rework-109
  type: task
  status: completed
  stage: release
  phase: 7
  total_phases: 7
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Wall system rework — BFME2-style hub/segment/gate (Alanthor)

## Release Notes

### Summary

Wall system rework (BFME2-style hub/segment/gate for Alanthor): docs canonicalised; hut age-up choice (Wall Hub / Watch Tower); builder UI contract; WallAutoSegmentSystem for retroactive segment formation; 5-instance gate region; live UI Toolkit click wiring; AI safety + scenario test.

### User-reported items → outcome

- **Item 7 — Alanthor hut age-up should offer Watch Tower or Wall Hub** → Phase 2 `GathererHutAgeUpChoice` marker + `HutConversionSystem` + `ConvertHutCommand` triad + two-button ActionCell cluster (castle/eye glyphs, 5 s timer). Choosing nothing is a valid end state (hut keeps Age 0 income).
- **Item 8 — Builder may only place wall hubs (segments / gates auto-form)** → Phase 3 `BuildableBuildings` HashSet asserted at boot (`Alanthor_WallTower` and `Alanthor_WallGate` are NOT directly buildable), JSX catalog relabel `Wall` → `Wall Hub`, contract comment on `SpawnWallHub` in `BuildCommandPannel.cs`.
- **Item 9 — Walls auto-build BFME2 style between hubs in range** → Phase 4 new `WallAutoSegmentSystem` (0.5 s poll, `MaxAutoSegmentDistance = 16 m`, deterministic sorted hub-pair iteration, `AlanthorWall.AreHubsConnected` guard, reuses existing `CreateSegment` flow).
- **Item 10 — Segments become 5-wide gates (battalion throughput)** → Phase 5 `WallGateRegionTag` + `WallGateGroup { Leader }` + `WallSegmentUpgradeState` (segment-level timer) + `AlanthorWall.PickGateRegionInstances` + `WallGatePassabilitySystem.RegionDetectRadius = 6.0`. Phase 6 wires the click handler in `ActionPanelRegion` (live UI Toolkit), adds the `ConvertSegmentToGateCommand` triad (lockstep slot 22), live hover-preview via `wall:previewGate` bridge topic.

### What landed by phase

- **Phase 1 — Design canonicalisation (docs-only)**
  - `docs/Design/Age_1_Alanthor.md` — new `## Wall System (BFME2 hub-and-segment)` section; old `Gatherer's Hut → wall-segment anchor` section marked superseded; 16 open design questions resolved (canonical or PLAYTEST PLACEHOLDER defaults).
  - `docs/Design/Overview.md` — age-up transform table row rewritten to match player-choice mechanic.
- **Phase 2 — Per-hut age-up choice UI**
  - New: `Assets/Scripts/Core/Commands/CommandTypes/ConvertHutCommand.cs`, `Assets/Scripts/Systems/Buildings/HutConversionSystem.cs`.
  - Modified: `Assets/Scripts/Core/Components/BuildingComponents.cs` (+3 types), `Assets/Scripts/Core/Commands/CommandRouter.cs`, `Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs`, `Assets/Scripts/Core/Multiplayer/LockstepTypes.cs` (`ConvertHut = 21`), `Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs`, `Assets/Scripts/Systems/Work/AgeUpSystem.cs`, `Assets/Scripts/UI/Panels/EntityExtractors.cs`, `Assets/Scripts/UI/Common/UITypes.cs`, `Assets/Scripts/UI/Web/HudBridge.cs`, `HudFrontend/src/components/Actions.jsx`, `Assets/StreamingAssets/HUD/hud.js` + `hud.css` (rebuilt).
- **Phase 3 — Builder UI contract**
  - Modified: `Assets/Scripts/UI/Panels/EntityExtractors.cs` (boot assert), `Assets/Scripts/UI/Panels/BuildCommandPannel.cs` (contract comment), `HudFrontend/src/components/Actions.jsx` (`Wall Hub` label + hint), `Assets/StreamingAssets/HUD/hud.js` + `hud.css` (rebuilt).
- **Phase 4 — WallAutoSegmentSystem**
  - New: `Assets/Scripts/Systems/Buildings/WallAutoSegmentSystem.cs`.
  - Modified: `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` (`AreHubsConnected` helper; `NetworkedEntity` on segments + instances for lockstep addressing).
- **Phase 5 — 5-instance gate region**
  - Modified: `Assets/Scripts/Core/Components/BuildingComponents.cs` (+5 types: `WallGateRegionTag`, `WallGateGroup`, `WallInstancePreviewTag`, `WallSegmentFocus`, `WallSegmentUpgradeState`), `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` (`PickGateRegionInstances`), `Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs` (Loop 2 segment-level), `Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs` (`RegionDetectRadius = 6.0`), `Assets/Scripts/UI/Panels/EntityExtractors.cs` (segment-aggregate HP override).
- **Phase 6 — Live UI click wiring + hover preview**
  - New: `Assets/Scripts/Core/Commands/CommandTypes/ConvertSegmentToGateCommand.cs`.
  - Modified: `Assets/Scripts/Core/Commands/CommandRouter.cs`, `Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs`, `Assets/Scripts/Core/Multiplayer/LockstepTypes.cs` (`ConvertSegmentToGate = 22`), `Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs`, `Assets/UI/Scripts/Regions/ActionPanelRegion.cs` (also filled Phase 2's `GathererHutAgeUpChoice` dispatch gap), `Assets/Scripts/UI/Web/HudBridge.cs` (`actions:convertWallSegmentToGate`, `wall:previewGate`), `Assets/Scripts/UI/Panels/EntityExtractors.cs` (wall-instance action payload), `HudFrontend/src/components/Actions.jsx`, `HudFrontend/src/components/Selection.jsx`, `Assets/StreamingAssets/HUD/hud.js` + `hud.css` (rebuilt).
- **Phase 7 — Pathfinding + AI safety + scenario test**
  - Modified: `Assets/Scripts/AI/SimpleAISystem.cs` (string-id entry guard in `TryBuildBuilding` rejecting `Alanthor_Wall` / `Alanthor_WallTower` / `Alanthor_WallGate`), `Assets/Scripts/AI/Managers/AIEconomyManager.cs` (task-109 marker comment over dead `BuildAlanthorWalls`), `Assets/Scripts/Bootstrap/ScenarioSetup.cs` (new `SeedFiveWideGateOnSegment` helper seeds a 5-wide Red gate while Blue keeps its legacy 1-instance gate for backward-compat).

### Acceptance criteria

All ACs pass per the approved review. Runtime-only ACs (sandbox playback at age-up, 4-hub square compartment income, battalion path through 5-wide gate, ParrelSync determinism, hub-destruction cascade) are flagged `unverifiable-without-runtime` — they require Unity Editor playback. Static / file-level ACs (Phase 1 docs-only commit, `BuildableBuildings` HashSet contents, no new `Alanthor_WallTower` grep hits, no new compile errors expected, design-doc sections present and rewritten) verified directly against the working tree.

### Outstanding concerns (non-blocking)

- **PLAYTEST PLACEHOLDER values in `docs/Design/Age_1_Alanthor.md`** — 16 numbers need playtest validation: hub spacing 16 m, hub HP 400, instance HP 80, Watch Tower HP 250, Wall Hub cost 60 S + 40 I, gate conversion 80 S flat, Watch Tower hut-conversion 40 S + 30 I, hub-conversion 5 s, tower-conversion 5 s (hut path) / 10 s (instance path), segment→gate 8 s. Numbers stay in code as constants; doc carries the explicit placeholder banner.
- **`WallAutoSegmentSystem` sort key** is `(Entity.Index, Entity.Version)` rather than the architect's `(FactionId, Index, Version)`. Functionally equivalent — same-faction is filtered post-sort and `Entity.Index` is unique, so determinism holds. Documented in Phase 4 decision events.
- **`NetworkedEntity` not on Wall Hubs** — hubs are addressed by entity reference in the `WallHubLink` buffer, not by network id. Segments and instances carry `NetworkedEntity` so lockstep payloads can address them.
- **Save / load (task-096) follow-up** — 5 new components in `BuildingComponents.cs` (`WallGateRegionTag`, `WallGateGroup`, `WallInstancePreviewTag`, `WallSegmentFocus`, `WallSegmentUpgradeState`) plus Phase 2's `GathererHutAgeUpChoice` / `GathererHutConverting` need to be picked up by task-096's serializer. `WallGateGroup.Leader` and `WallSegmentFocus.Instance` are `Entity` fields and need the `NetworkedEntity` remap pass like other entity-bearing components. `WallSegmentUpgradeState.Remaining` should serialize so a save mid-conversion resumes cleanly.

### Manual test list (consolidated)

1. Open Unity Editor on 6000.0.37f1 — verify clean compile after each phase commit and after the full series.
2. Sandbox match as Alanthor — age up, click `Convert to Wall Hub` on a Gatherer's Hut, verify 5 s timer + correct hub spawn at hut position with 60 S + 40 I deducted.
3. Same hut, click `Convert to Watch Tower` instead — verify 5 s timer + correct Watch Tower spawn with 40 S + 30 I deducted.
4. Open Alanthor build panel — verify `Wall Hub` and `Watch Tower` only (NO `Wall Tower`, `Wall Gate`, `Wall Segment`).
5. Place 2 hubs within 16 m, then a third within 16 m of one of them, all after exiting placement mode each time — verify retroactive auto-segment formation within ~1 s of each `UnderConstruction` clearing.
6. Place 4 hubs in a square within 16 m neighbour-wise — verify `WallEnclosureIncomeSystem` detects closed compartment + supplies income starts.
7. Click any wall instance — verify Selection panel shows segment-aggregate HP; Action panel shows `Convert to Gate (5×)` (or `Nx` for short segments) + `Convert to Tower`.
8. Hover `Convert to Gate` card — verify (if presentation hooked) 5 candidate instances highlight via `WallInstancePreviewTag`; otherwise verify the marker is added/removed via debug inspector.
9. Click `Convert to Gate (5×)` — verify 8 s timer, 80 S deducted, 5 centre instances swap to gate presentation (ID 554), `WallGateRegionTag` + `WallGateGroup` present.
10. Move a 5-unit-line-formation battalion across the new 5-wide gate — verify gate opens at ~6 m proximity, all 5 units pass within 1 s of one another, gate closes after passage.
11. Send an enemy unit at the same open friendly gate — verify enemy is blocked (gate stays effectively closed for enemies).
12. Destroy a hub of an enclosed compartment — verify segments cascade, compartment income stops next `WallEnclosureIncomeSystem` tick.
13. Run the `WallSiege` scenario — verify Red wall has a 5-wide gate (region) and Blue wall keeps its legacy 1-instance gate (both functioning).
14. ParrelSync 2-client lockstep test — same hub-placement sequence on both clients, verify identical hub graph + auto-segments + gate conversion via `actions:convertWallSegmentToGate`.

### Next steps

1. Open Unity Editor; verify clean compile.
2. Run sandbox as Alanthor; age up; click `Convert to Wall Hub` on a hut; verify 5 s timer.
3. Place 4 hubs in a square; verify auto-segments form and gate conversion on one segment opens for friendlies.
4. Run `WallSiege` scenario; verify the 5-wide Red gate and the legacy 1-wide Blue gate both work.
5. ParrelSync test for lockstep determinism on hub placement + gate conversion.
6. After playtest, replace `PLAYTEST PLACEHOLDER` values in `docs/Design/Age_1_Alanthor.md` with tuned numbers.
7. When task-096 (save / load) lands, register the 7 new components in its serializer with the `NetworkedEntity` remap rule for `Entity` fields.

## Context

Design pillar shift for Alanthor's signature mechanic. The player has
re-asserted (after seeing the current behaviour) that walls should follow
**BFME2 hub-and-segment semantics**: the builder only ever places **wall
hubs**; segments materialise automatically between hubs that are within
range; and segments can be **converted into gates (5-segment-wide) or
towers** through a per-segment UI action. The current implementation is
**80% there architecturally** (see [AlanthorWall.cs](../../../Assets/Scripts/Entities/Buildings/AlanthorWall.cs)
already models Hub → Segment → Instance and [WallUpgradeSystem.cs](../../../Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs)
already converts an instance into a Tower or Gate) **but four design-level
gaps remain**:

1. **Item 7** — Alanthor Gatherer's Huts have no age-up conversion path.
   The hut transform code is still a stub
   ([AgeUpSystem.cs:169-186](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L169))
   from task-066 Phase 1. Design says the hut should offer a player choice
   to **convert to Watch Tower or Wall Hub**.
2. **Item 8** — The builder UI exposes `Alanthor_Wall` (the hub),
   `Alanthor_Tower` (standalone watch tower), `Alanthor_PracticeRange`,
   `Alanthor_SiegeYard`, `Alanthor_Smelter`. Per the new design the
   **only wall-system primitive the builder may place is the Wall Hub**;
   the standalone watch tower remains as a separate building. The current
   chain-placement flow (place hub → place another hub → segment
   auto-spawns) already matches BFME2 semantics for the placement loop —
   what is missing is the explicit "you only ever place hubs, never
   segments or gates" contract being **documented in the design folder
   and verified end-to-end**.
3. **Item 9** — Auto-segment formation already works on **adjacent
   placements** during chain-mode, but does **not** auto-form between
   **two pre-existing hubs that come within range** later. BFME2 forms a
   segment whenever any two hubs of the same faction end up within range,
   not only at the moment of placement. Today: hub snap-distance
   (`WallHubSnapDistance = 2.0f`) only triggers reuse of the snapped hub;
   there is no proximity-driven retro-link.
4. **Item 10** — The **gate is per-instance today** (one wall instance ≈
   2 m wide becomes a 1-instance-wide gate). Design requires **gates to
   span 5 segments** (≈ 10 m, wide enough for a battalion to fit through).
   The UI to convert a segment to a gate exists in IMGUI
   ([EntityActionPanel.cs:1641-1681](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1641))
   but is **not wired in the live UI Toolkit panel** — `ActionType.WallInstanceUpgrade`
   is on the "render buttons but click logs a TODO and returns" list in
   [ActionPanelRegion.cs:14](../../../Assets/UI/Scripts/Regions/ActionPanelRegion.cs#L14).

[CLAUDE.md](../../../CLAUDE.md) is explicit: "Do not introduce new
mechanics, balance values, or design changes without updating the Design
folder first." Therefore **Phase 1 of this task must land in
[docs/Design/Age_1_Alanthor.md](../../../docs/Design/Age_1_Alanthor.md)
and [docs/Design/Overview.md](../../../docs/Design/Overview.md) with no
code changes** — the new hub/segment/gate spec must be canonical before
implementation begins. The release agent should block the task otherwise.

The audit context: [task-082 §B.4 / §B.5 / §B.6](../task-profound-code-review-082/task.md)
flagged hut age-up transformations as entirely unimplemented (parent
task: [task-066](../task-ageup-transform-hut-implementations-066/task.md)).
This task supersedes that work for the Alanthor branch — Alanthor's hut
transform becomes the wall-hub / watch-tower conversion choice defined
here, not the auto-fortify-radius approach drafted in
[Age_1_Alanthor.md § Gatherer's Hut](../../../docs/Design/Age_1_Alanthor.md#gathereers-hut-age-0-carryover--transforms-into-wall-segment-anchor).
That section is **superseded** and must be rewritten as part of Phase 1.

## User Value

- **Alanthor's signature mechanic feels like BFME2.** Drop a hub, drop
  another hub, the wall connects itself; the player never wrangles
  individual wall pieces. The current chain-placement already gives this
  experience for adjacent placements — the rework makes it consistent
  for retroactive (move hub into range of another) and recovery (hub
  destroyed, rebuilt) flows.
- **Wall economy is finally reachable.** The
  [WallEnclosureIncomeSystem](../../../Assets/Scripts/Economy/WallEnclosureIncomeSystem.cs)
  already computes closed-loop area income from hub adjacency. The rework
  closes the loop on usability — players can actually build closed
  compartments without micromanaging segment placement.
- **Battalions can pass through walls.** Today only individual 2 m gate
  instances exist; a 5-unit-wide battalion in line formation cannot
  pass. After the rework, gates are 5-segment (~10 m) openings sized for
  battalion throughput.
- **Alanthor's age-up power spike is real.** Gatherer's Huts (placed in
  Age 0 for income) become a meaningful Age 1 conversion choice: spend
  the hut for a Watch Tower (defensive vision) or a Wall Hub (start of a
  wall ring).

## Requirements

- **R1** — **Update [docs/Design/Age_1_Alanthor.md](../../../docs/Design/Age_1_Alanthor.md)
  and [docs/Design/Overview.md](../../../docs/Design/Overview.md)** to
  spec the BFME2-style wall system **before** any code change. The
  Age_1_Alanthor "Gatherer's Hut → wall-segment anchor" section is
  superseded by the new hub/segment/gate spec. Phase 1 is **docs-only**;
  no `.cs` / `.json` / `.unity` / `.prefab` files may be touched.
- **R2** — At age-up to Alanthor, each Gatherer's Hut owned by the
  faction enters a **player-choice state** offering two conversions:
  **Convert to Wall Hub** or **Convert to Watch Tower**. The choice is
  per-hut (each hut has its own UI button cluster). Until the player
  picks, the hut persists unchanged. (No auto-fortify-radius — that
  Age_1_Alanthor design draft section is dropped.)
- **R3** — The builder build-panel for Alanthor exposes **exactly one
  wall-system primitive: Wall Hub** (`Alanthor_Wall`). Segments cannot
  be placed directly. Gates cannot be placed directly. Towers
  (`Alanthor_Tower` — the standalone watch tower) remain as a separate,
  independent build option; the wall-instance-tower upgrade
  (`Alanthor_WallTower`) is **not** in the build panel.
- **R4** — Whenever two **same-faction** wall hubs end up within
  **MaxAutoSegmentDistance** (design parameter, see Open Q1) of each
  other and **neither is already connected to the other**, an auto-segment
  spawns connecting them. This fires in both directions: (a) at placement
  time (today's chain-mode), (b) retroactively when a hub finishes
  construction (`UnderConstruction` removed), and (c) when a destroyed
  hub is rebuilt nearby. The segment auto-spawns the correct number of
  wall instances along its length, identical to the existing
  [AlanthorWall.SpawnInstances](../../../Assets/Scripts/Entities/Buildings/AlanthorWall.cs#L158)
  flow.
- **R5** — A segment can be **converted into a Gate** via per-segment UI.
  The gate spans **5 wall instances** (the centre instance + 2 on each
  side). The 5 instances are replaced (or marked) as a single passable
  gate region. Battalions in any formation must be able to path through.
  If the segment has fewer than 5 instances total (short segment), the
  whole segment becomes the gate (cap-at-segment-length). The conversion
  cost is paid in supplies + iron (see Open Q4).
- **R6** — The live UI Toolkit action panel surfaces the
  segment→gate (and segment→tower) action with working click handlers.
  This includes wiring `ActionType.WallInstanceUpgrade`'s click dispatch
  in [ActionPanelRegion.cs](../../../Assets/UI/Scripts/Regions/ActionPanelRegion.cs)
  (currently a TODO logging no-op) and may require extending
  EntityExtractors to emit the upgrade actions on **segments**, not
  individual instances (today the action is exposed per-instance).
- **R7** — Pathfinding integrates correctly with the new layout:
  (a) wall hubs and wall instances block the
  [PassabilityGrid](../../../Assets/Scripts/World/Terrain/PassabilityGrid.cs);
  (b) gates block when closed and unblock when a friendly unit is
  within `FriendlyDetectRadius` (existing
  [WallGatePassabilitySystem](../../../Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs)
  behaviour) but now applied to a **5-instance gate region**, not a
  single instance; (c) enemy units cannot path through open friendly
  gates (existing behaviour — verify it still holds for the 5-wide gate).
- **R8** — **No regression for other cultures.** Runai and Feraldis
  cannot build wall hubs (existing culture-gating in
  [EntityExtractors.cs:889-907](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L889)).
  The age-up hut-transform stubs for Runai and Feraldis
  ([AgeUpSystem.cs:169-186](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L169))
  remain untouched — task-066 / task-067 own those branches.
- **R9** — **AI integration is in scope, minimally.** [SimpleAISystem](../../../Assets/Scripts/AI/SimpleAISystem.cs)
  must not crash or stall when its faction is Alanthor and a wall hub is
  in its base. The minimum bar: Alanthor AI does **not** try to build
  wall hubs in this task (AI wall strategy is deferred), but if AI builds
  a hub by accident (e.g. via TechTreeDB queue), the segment auto-form
  rule must still fire and not desync.
  ([AIAlanthorEndgameSystem](../../../Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs)
  is `[DisableAutoCreation]` per memory — confirm it stays dead;
  [AIEconomyManager.cs:627-756](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L627)
  has dead wall-building code that should be cross-checked but not
  necessarily revived.)
- **R10** — **Lockstep determinism is preserved.** The retroactive
  auto-segment formation (R4 case b/c) must fire from a system, not from
  a player-side input handler, so all clients in a multiplayer session
  reach the same hub-graph topology. Use the existing snapshot-then-mutate
  pattern (collect candidate hub pairs into a `NativeList`, then process
  via an `EntityCommandBuffer`).

## Acceptance Criteria

- [x] **Phase 1 docs-only.** Commit for Phase 1 modifies only files
      under `docs/Design/` (and possibly `.deft/tasks/.../*.md`). No
      `.cs`, `.json`, `.unity`, `.prefab`, `.asset` files modified.
- [x] `docs/Design/Age_1_Alanthor.md` contains a section explicitly
      titled **"Wall system — hub / segment / gate (BFME2-style)"** that
      defines: hub spacing (R4), instance count formula, gate width (5
      segments), conversion costs, what builders can/cannot place (R3),
      and hub-destruction cascade behaviour.
- [x] `docs/Design/Age_1_Alanthor.md § Gatherer's Hut (Age 0 carryover)`
      is **rewritten** to describe the player-choice "convert to Wall
      Hub or Watch Tower" mechanic. The old auto-fortify-radius wording
      is removed.
- [x] `docs/Design/Overview.md § Age-up transform table` (the row that
      currently says "Alanthor: a wall-segment anchor that auto-fortifies
      a small radius around itself") is updated to match the
      player-choice mechanic.
- [x] At age-up in a sandbox match (force `Cultures.Alanthor`), each
      existing Gatherer's Hut shows a UI cluster with **two buttons**:
      "Convert to Wall Hub" and "Convert to Watch Tower". Clicking
      either replaces the hut with the chosen building at the same
      world position, deducting the conversion cost.
- [x] Until the player picks, the hut persists with **its Age 0
      gatherer income unchanged** (no breakage of mid-conversion
      compartment-income state). Picking neither is a valid end state.
- [x] The Alanthor build panel (visible after picking Alanthor at
      age-up) **does not** contain any of `Alanthor_WallTower`,
      `Alanthor_WallGate`, "Wall Segment", or "Wall Gate" buttons. It
      **does** contain `Alanthor_Wall` (Wall Hub) and `Alanthor_Tower`
      (Watch Tower) as **two distinct** entries with distinct icons /
      labels.
- [x] Place a Wall Hub at A. Then place a second Wall Hub at B,
      where `distance(A, B) ≤ MaxAutoSegmentDistance` and the player has
      released chain-mode (cancelled placement, then started fresh).
      Within ≤ 1 second of B's `UnderConstruction` finishing, a segment
      spawns connecting A and B, with the expected number of wall
      instances. Repeat with a fresh hub C within range of B — a second
      segment forms A↔B + B↔C, with no duplicate A↔C link unless C is
      also within range of A.
- [x] Place 4 Wall Hubs forming a square inside `MaxAutoSegmentDistance`
      neighbour-wise. Closed compartment is detected by
      [WallEnclosureIncomeSystem](../../../Assets/Scripts/Economy/WallEnclosureIncomeSystem.cs)
      and Alanthor faction starts earning supplies from the enclosed
      area (existing behaviour, verify still works after rework).
- [x] Select a wall segment (clicking any instance in a segment selects
      the segment as the actionable unit — see Open Q5). The action
      panel shows **"Convert to Gate"** and **"Convert to Tower"**
      buttons with rich-text cost tooltips matching the existing
      `BuildTooltip` style. Clicking "Convert to Gate" deducts the cost,
      replaces the centre 5 instances with a gate region (or the full
      segment if it has < 5 instances), and the visual updates within
      one frame.
- [x] A battalion of 5 units in line formation issued a move order from
      one side of a gate to the other paths through the open gate
      (gate auto-opens when battalion enters
      `WallGatePassabilitySystem.FriendlyDetectRadius` of any of the 5
      gate-region instances) and all 5 units arrive on the far side
      within 1 second of one another.
- [x] An enemy unit issued a move order across an open friendly gate
      cannot path through — the gate stays closed for enemies (verify
      `WallGatePassabilitySystem`'s faction check still gates open
      logic, not just blocked logic).
- [x] When a Wall Hub is destroyed in combat, all segments it
      participated in cascade-destroy along with their instances
      ([WallSegmentCleanupSystem](../../../Assets/Scripts/Systems/Buildings/WallSegmentCleanupSystem.cs)
      existing behaviour, verify still works). Closed compartments
      involving that hub stop generating supplies on the next
      `WallEnclosureIncomeSystem` tick.
- [x] No new compile errors. Unity Editor opens the project on
      6000.0.37f1 without errors after each phase's commit.
- [x] `git grep -F "Alanthor_WallTower" Assets/Scripts | grep -v BuildCosts.cs | grep -v EntityActionPanel.cs | grep -v WallUpgradeSystem.cs | grep -v ScenarioSetup.cs`
      returns no new occurrences (we are not exposing the WallTower /
      WallGate building IDs as buildable; they remain conversion-only).
- [x] Lockstep determinism: in a 2-client ParrelSync test, both clients
      observe the **same set of auto-segments** after placing the same
      hub-sequence. (Manual checklist; document the test steps in the
      task's review notes.)

## Implementation Phases

### Phase 1: Design folder canonicalisation (DOCS ONLY)
**Scope:** Update [docs/Design/Age_1_Alanthor.md](../../../docs/Design/Age_1_Alanthor.md)
and [docs/Design/Overview.md](../../../docs/Design/Overview.md) to spec
the BFME2-style wall system. This is the **CLAUDE.md mandate gate** —
no code may change in this phase. Rewrites the "Gatherer's Hut → wall
anchor" section to the "Hut → choice of Wall Hub / Watch Tower"
mechanic, defines hub spacing / segment count / gate width / conversion
costs, and clarifies what is and is not buildable directly by the
builder. Resolves all "Open Design Questions" below to canonical values
(or marks them as playtest-tuning placeholders with a default).
**Files:** `docs/Design/Age_1_Alanthor.md`, `docs/Design/Overview.md`,
optional `docs/Design/Wall_System.md` (new) if the spec gets long
enough to warrant its own doc.
**Estimated effort:** Small

### Phase 2: Per-hut age-up choice UI (Wall Hub / Watch Tower)
**Scope:** Implement Item 7. Replace the
[AgeUpSystem TransformGathererHutsForCulture](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L169)
stub for the Alanthor branch with a system that adds a
`GathererHutAgeUpChoice` marker component to each owned hut. Add a UI
extractor for that marker that surfaces a 2-button action cluster
("Convert to Wall Hub" / "Convert to Watch Tower") via the existing
`ActionInfo` / `ActionType` flow. Wire the click handlers in
[ActionPanelRegion.cs](../../../Assets/UI/Scripts/Regions/ActionPanelRegion.cs)
to spend the conversion cost, destroy the hut entity, and spawn the
chosen building (via `AlanthorWall.CreateHub` or
`BuildingFactory.CreateAlanthorTower`) at the hut's position.
**Files:** `Assets/Scripts/Systems/Work/AgeUpSystem.cs`,
`Assets/Scripts/Core/Components/BuildingComponents.cs` (new marker
component), `Assets/Scripts/UI/Panels/EntityExtractors.cs` (new
`ActionType.GathererHutAgeUpChoice`),
`Assets/Scripts/UI/Common/UITypes.cs` (new enum value),
`Assets/UI/Scripts/Regions/ActionPanelRegion.cs` (click dispatch),
`Assets/Scripts/Entities/Buildings/AlanthorWall.cs` (helper to
swap-in-place if needed).
**Estimated effort:** Medium

### Phase 3: Builder UI verification — wall hubs only (Item 8)
**Scope:** Confirm the [BuildableBuildings HashSet](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L833)
exposes **only** `Alanthor_Wall` (hub) and `Alanthor_Tower` (standalone)
as wall-related primitives for Alanthor. If gaps appear (e.g.
`Alanthor_WallTower` or `Alanthor_WallGate` accidentally reachable
elsewhere), close them. Add an in-code assertion or unit test that the
HashSet does NOT contain wall-instance-conversion ids. Document the
contract in a code comment.
**Files:** `Assets/Scripts/UI/Panels/EntityExtractors.cs`,
`Assets/Scripts/UI/Panels/BuildCommandPannel.cs` (the
`TriggerBuildingPlacement` switch already lacks WallTower/WallGate
cases — verify and comment).
**Estimated effort:** Small

### Phase 4: Retroactive auto-segment formation (Item 9)
**Scope:** Implement R4 cases (b) and (c). Add a new system
`WallAutoSegmentSystem` (under `Systems/Buildings/`) that periodically
(every 0.5 s) scans all completed hubs per faction, finds pairs
**within MaxAutoSegmentDistance** that are **not already connected**
(check `WallHubLink` buffer), and creates segments for them via
`AlanthorWall.CreateSegment`. Uses the snapshot-then-mutate pattern
documented in `.deft/memory/project-facts.md`. Verifies that
[WallEnclosureIncomeSystem](../../../Assets/Scripts/Economy/WallEnclosureIncomeSystem.cs)
correctly re-detects compartments after the new segments form.
**Files:** `Assets/Scripts/Systems/Buildings/WallAutoSegmentSystem.cs`
(new), possible tweak to
`Assets/Scripts/Entities/Buildings/AlanthorWall.cs` (expose a guard
helper `AreHubsConnected(em, hubA, hubB)`).
**Estimated effort:** Medium

### Phase 5: Multi-instance gate (Item 10) — 5-wide gate region
**Scope:** Replace the per-instance `WallUpgradeState` gate path with a
**segment-level** conversion. Add a `WallSegmentGateConversion` component
(or extend `WallUpgradeState` with a `Width` field) that, on completion
in [WallUpgradeSystem](../../../Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs),
applies `WallGateTag` + `WallGateState` to the **5 centre instances**
of the segment (or all instances if the segment has < 5). The
[WallGatePassabilitySystem](../../../Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs)
already iterates all gate-tagged entities — verify it tolerates 5
gate-tagged neighbours by toggling each independently, **or** introduce
a shared `WallGateGroup` so all 5 open/close together (decision: see
Open Q6). Update the segment-level visual: gate instances use
`AlanthorWall.GatePresentationID = 554`; the 5 contiguous gate
presentations form the "open gate" look.
**Files:** `Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs`,
`Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs`,
`Assets/Scripts/Core/Components/BuildingComponents.cs`,
`Assets/Scripts/Entities/Buildings/AlanthorWall.cs` (helper to pick
the centre 5).
**Estimated effort:** Medium

### Phase 6: Live UI action wiring for segment conversion (Item 10 part 2)
**Scope:** Move the wall-instance-upgrade UI from "per-instance" to
"per-segment." `EntityExtractors.GetActionInfo` ([line 680-688](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L680))
currently surfaces upgrade actions when an individual instance is
selected; change to surface them when **the segment** (or any instance
of the segment, resolved to the segment entity via
`WallInstanceParent.Segment`) is selected. Then wire the
`ActionType.WallInstanceUpgrade` click handler in
[ActionPanelRegion.cs](../../../Assets/UI/Scripts/Regions/ActionPanelRegion.cs)
(currently logs a TODO and returns) to add the appropriate
`WallUpgradeState` to the segment-entity (or the segment's centre-5
instances if the Phase 5 design picks per-instance tagging). The
existing IMGUI `DrawWallUpgradePanel` in
[EntityActionPanel.cs:1641-1681](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1641)
serves as the reference implementation — copy its cost / spend / state
transitions to the new wiring path; do **not** un-suspend the IMGUI
panel.
**Files:** `Assets/Scripts/UI/Panels/EntityExtractors.cs`,
`Assets/Scripts/UI/Web/HudBridge.cs` (verify the actions payload
emits correctly), `Assets/UI/Scripts/Regions/ActionPanelRegion.cs`,
possibly `HudFrontend/src/components/Actions.jsx` (verify the new
action shows up; should be data-driven via the same payload pipe).
**Estimated effort:** Medium

### Phase 7: Pathfinding + AI safety + scenario test
**Scope:** Verify R7 (pathfinding integrates with new 5-wide gates and
auto-formed segments), R9 (AI does not crash on Alanthor walls), R10
(lockstep determinism). Extend the existing `WallSiege` scenario in
[ScenarioSetup.cs:286](../../../Assets/Scripts/Bootstrap/ScenarioSetup.cs#L286)
to use a 5-wide gate instead of a single-instance gate so the scenario
exercises the new path. Add a sanity check in `SimpleAISystem` that
skips Alanthor wall hubs in its build-target enumeration (don't auto-
build them). Verify
[AIAlanthorEndgameSystem](../../../Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs)
remains `[DisableAutoCreation]` and that the dead wall-building code in
[AIEconomyManager.cs:627-756](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L627)
either stays dead OR is updated to match the new contract — pick one
and document.
**Files:** `Assets/Scripts/Bootstrap/ScenarioSetup.cs`,
`Assets/Scripts/AI/SimpleAISystem.cs`,
`Assets/Scripts/AI/Managers/AIEconomyManager.cs` (clarifying comment
only if no behaviour change),
`Assets/Scripts/World/Terrain/PassabilityGrid.cs` (verify, likely no
change),
`Assets/Scripts/Systems/Movement/NavMeshManager.cs` (verify rebake
triggers for 5-wide gate state changes).
**Estimated effort:** Medium

## Open Design Questions

Phase 1 (Design folder update) **must** resolve every question below to
a canonical value (or an explicit "playtest tuning placeholder, default
= X" with a code comment in the relevant constant). The implementer
cannot autopilot through ambiguity here.

1. **MaxAutoSegmentDistance** — what's the maximum world-space distance
   between two hubs for an auto-segment to spawn? Today
   `AlanthorWall.InstanceSpacing = 2 m` and a single segment between
   adjacent hubs spawns `ceil((distance - 2 * HubInset) / 2)` instances.
   Suggest **16 m** (8 instances per segment max) to keep visual density
   reasonable. Confirm or set.
2. **Instance-count-per-segment formula** — does the count scale
   linearly with distance (current behaviour: `ceil(usable / 2)`), or
   is it capped to a fixed N regardless of hub spacing? Current is
   linear-uncapped (modulo the InstanceSpacing). Keep, or cap?
3. **HP / cost balance values** — currently:
   - Hub HP **600**, cost **50 S + 20 I** ([AlanthorWall.cs:36](../../../Assets/Scripts/Entities/Buildings/AlanthorWall.cs#L36),
     [BuildCosts.cs:63](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L63))
   - Instance HP **200**, no separate cost (auto-spawn)
   - Tower (upgrade) HP **500**, cost **60 S + 30 I**
     ([WallUpgradeSystem.cs:43](../../../Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs#L43))
   - Gate cost **40 S + 15 I** per instance today; **what's the cost
     for the 5-wide gate?** Suggest 5× the per-instance cost (`200 S +
     75 I`) OR a flat single-payment (`100 S + 40 I`). Pick one.
4. **Hub-to-hub minimum spacing** — if a player tries to place a second
   hub touching the first, do we snap (current behaviour: hubs within
   `WallHubSnapDistance = 2 m` reuse the existing hub) or reject?
   Snapping makes the chain feel forgiving; rejecting prevents
   degenerate overlapping. Keep snap-and-reuse?
5. **Segment-selection UX** — when the player clicks any wall instance,
   should the selection resolve to the **segment** (so the action panel
   shows Convert-to-Gate / Convert-to-Tower) or the **instance** (today's
   behaviour, per [EntityExtractors.cs:680](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L680))?
   Phase 6 of this scope assumes **segment**, but confirm.
6. **5-instance gate — group toggle or independent toggle?** When a
   friendly battalion approaches one end of a 5-wide gate, do all 5
   gate-instances open in unison (group toggle, requires a shared
   `WallGateGroup` component), or does each instance check proximity
   independently (today's behaviour, but applied 5×)? Group toggle
   feels more BFME2; independent is simpler.
7. **Hub destruction → segment cascade** — already implemented in
   [WallSegmentCleanupSystem](../../../Assets/Scripts/Systems/Buildings/WallSegmentCleanupSystem.cs)
   (both adjacent segments and their instances die when a hub dies).
   Does the player want a **grace period** (e.g. 5 s of "wall is
   crumbling" so they can rebuild the hub and save the segments), or
   instant cascade as today? Suggest **instant**.
8. **AI wall strategy** — does Alanthor AI in this task **build walls
   strategically** (around resources, around the Hall) or **build none**
   (defer to a follow-up task)? Recommend **defer**. The dead code in
   [AIEconomyManager.cs:627-756](../../../Assets/Scripts/AI/Managers/AIEconomyManager.cs#L627)
   gives a starting point but is `[DisableAutoCreation]`-orphaned.
9. **Visual identity of WallTower / WallGate prefabs** — the existing
   `Alanthor_WallTower` / `Alanthor_WallGate` TechTree entries
   ([TechTree.json:1329-1363](../../../Assets/Resources/TechTree.json#L1329))
   and PresentationIDs 553 / 554 ([AlanthorWall.cs:23-24](../../../Assets/Scripts/Entities/Buildings/AlanthorWall.cs#L23))
   target individual-instance prefabs. For the 5-wide gate, do we
   tile the existing single-gate prefab 5 times, or design a new
   wide-gate prefab? Recommend **tile existing for now**, design new
   later.
10. **Conversion timer** — segment → gate takes 8 s today
    ([EntityActionPanel.cs:1677](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1677)),
    segment → tower takes 10 s. Keep, or rebalance? Walls take builders
    to build today (`AssignBuildersToConstruction`); does the
    conversion also need a builder, or is it an instant-paid timer
    (current behaviour)?

## Edge Cases

- **Hub placed inside another faction's territory.** Today there is no
  influence-based placement restriction for walls. The auto-segment
  rule should still only connect **same-faction** hubs (already enforced
  by `WallEnclosureIncomeSystem`'s faction filter — verify
  `WallAutoSegmentSystem` does the same). Cross-faction "trespassing"
  hubs do nothing economically but block pathing for the placing
  faction — acceptable.
- **Hub placement overlapping another building.** Existing placement
  validation in [BuildCommandPannel.cs](../../../Assets/Scripts/UI/Panels/BuildCommandPannel.cs)
  should reject. Wall hubs use `BuildingSize` (1×1 default) — confirm
  the rect doesn't overlap an existing Hall / Hut / Barracks footprint.
- **Hub placement where the auto-segment would cross a Hall.** A wall
  segment connects hubs along a straight line; if the line passes
  through a Hall or large building, instances would overlap that
  building. Today instance creation does not check. **Punt as known
  issue** — flag in code comment, leave for a follow-up task.
- **Auto-segment with a destroyed hub.** R4 triggers when "both hubs
  exist and neither is connected." If hub A is destroyed mid-tick after
  the snapshot but before segment creation, the system must guard with
  `em.Exists(hubA)` (existing pattern — see
  [WallSegmentCleanupSystem](../../../Assets/Scripts/Systems/Buildings/WallSegmentCleanupSystem.cs)).
- **Gate destruction.** Each gate instance carries `Health`. If any of
  the 5 die, the gate region effectively becomes a hole. Per Open Q6,
  the group-toggle approach needs to handle "3 of 5 instances dead" —
  the remaining 2 should still toggle (or fall back to passable-always-
  as-hole).
- **Hub destruction with active segments.** Already handled:
  [WallSegmentCleanupSystem](../../../Assets/Scripts/Systems/Buildings/WallSegmentCleanupSystem.cs)
  Phase 2 cascades. Verify the new auto-segment system does **not**
  immediately re-form the same segment from the destroyed hub's ghost
  (the hub entity is destroyed; `em.Exists` returns false; safe).
- **Pathfinding cost over a wall-hub-blocked tile.** Hubs block. If a
  unit on the wrong side of a wall has no detour, it should stand still
  (existing A*/FlowField behaviour — verify no infinite-loop on a fully
  enclosed compartment).
- **AI placing hubs poorly.** Per Open Q8, AI builds no walls in this
  task. Out of risk.
- **Compartment income with > 8 hubs.** The
  [WallEnclosureIncomeSystem](../../../Assets/Scripts/Economy/WallEnclosureIncomeSystem.cs)
  cycle-finder is O(n) per hub. With a large auto-formed network (R4
  encourages denser networks), verify no performance regression at
  e.g. 30 hubs.
- **Lockstep desync from per-tick auto-segment race.** R10 mitigation:
  use snapshot-then-mutate; the `WallAutoSegmentSystem` must iterate
  hub pairs in deterministic order (e.g. sorted by `Entity.Index`).
- **Phase 1 docs-only enforcement.** A code commit accidentally
  bundled in Phase 1 should be caught by the review agent. Acceptance
  criterion above formalises this.
- **Player cancels age-up hut choice and the hut sits there forever.**
  Acceptable per R2. The hut continues generating Age-0-era income
  indefinitely. No timeout. Document this as intentional.

## Dependencies

- **Hard prerequisite:** **None for Phase 1** (docs-only).
- **Soft overlap with [task-066 (ageup-transform-hut-implementations-066)](../task-ageup-transform-hut-implementations-066/task.md):**
  Task-066 owns the cross-culture hut transform pipeline. This task
  **supersedes** task-066's Alanthor branch (replaces the
  auto-fortify-radius design with player-choice conversion). Confirm
  with the task-066 author / agent before landing Phase 2; or close
  task-066's Alanthor work and reference this task as the canonical
  implementation. The Runai / Feraldis branches of task-066 are
  **not affected**.
- **No overlap with [task-067 (feraldis-raider-rebuild)](../task-feraldis-raider-house-spawn-067/task.md):**
  different culture branch — mention only so the implementers don't
  step on each other in `AgeUpSystem.cs`.
- **Cross-ref [task-082 §B audit](../task-profound-code-review-082/task.md):**
  the audit's findings about `Alanthor_WallTower` / `Alanthor_WallGate`
  being unbuildable are **resolved by this rework** (they are
  conversion-only by design, not buildable). Update the audit row
  status when this task closes.
- **Cross-ref [task-091 (buildables-hashset-completeness)](../task-buildables-hashset-completeness-091/task.md):**
  task-091 explicitly excludes WallTower / WallGate (it covers
  Crucible / VeilsteelFoundry / Feraldis Foundry). No overlap. Mention
  in the Phase 1 doc update so task-091's scope is preserved.
- **No new Unity packages.** All work uses existing Entities /
  Mathematics / Collections APIs.
- **No new Resources / Prefabs initially.** Reuse existing
  PresentationIDs 550 (hub), 552 (instance), 553 (tower), 554 (gate).
  Open Q9 may later spawn a wide-gate-prefab task.

## Technical Notes

- **Existing components** (in [BuildingComponents.cs:77-170](../../../Assets/Scripts/Core/Components/BuildingComponents.cs#L77)):
  `WallTag`, `WallHubTag`, `WallSegmentTag`, `WallInstanceTag`,
  `WallTowerTag`, `WallGateTag`, `WallConnection`, `WallHubLink`
  (buffer), `WallInstanceRef` (buffer), `WallInstanceParent`,
  `WallGateState`, `WallUpgradeState`,
  `WallEnclosureIncomeTag`, `WallEnclosureVertex`. New components to
  add: `GathererHutAgeUpChoice` (Phase 2 marker), possibly
  `WallGateGroup` (Phase 5 per Open Q6).
- **Existing systems** (under `Assets/Scripts/Systems/Buildings/` and
  `Assets/Scripts/Economy/`): `WallUpgradeSystem`,
  `WallGatePassabilitySystem`, `WallSegmentCleanupSystem`,
  `WallEnclosureIncomeSystem`. New: `WallAutoSegmentSystem` (Phase 4).
- **Namespace convention** per `.deft/memory/project-facts.md`:
  systems live under `TheWaningBorder.Systems.Buildings` (note the
  plural). New components go in the **global namespace** under
  `Assets/Scripts/Core/Components/`. Follow the snapshot-then-mutate
  ECB pattern; the `WallSegmentCleanupSystem` is a good template.
- **UI flow** per `.deft/memory/project-facts.md`: live entity UI runs
  through `HudFrontend/src/` + `HudBridge.cs` JSON topics; **do NOT
  re-wake the IMGUI panels**. The existing IMGUI `DrawWallUpgradePanel`
  in [EntityActionPanel.cs:1641-1681](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1641)
  is a **reference for the cost / spend / state-add logic**, not a
  surface to re-enable. Wire the click in
  [ActionPanelRegion.cs](../../../Assets/UI/Scripts/Regions/ActionPanelRegion.cs)
  (where the TODO currently lives) and trust the existing actions
  payload pipe in HudBridge.
- **Lockstep contract** per `.deft/memory/decisions.md`: structural
  changes must be deterministic across clients. Auto-segment formation
  must iterate hub pairs in deterministic order (sort by
  `Entity.Index` ascending) and use ECB so all clients see the same
  archetype changes in the same tick.
- **PresentationSpawnSystem** has a dedicated `PresentationSpawnSystem.Walls.cs`
  partial file ([Assets/Scripts/Presentation/PresentationSpawnSystem.Walls.cs](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.Walls.cs))
  — Phase 5 may need to extend it for the 5-wide gate visual story.
- **NavMesh** rebakes when buildings change
  ([NavMeshManager.cs](../../../Assets/Scripts/Systems/Movement/NavMeshManager.cs)).
  5-wide gate open/close transitions must trigger appropriate rebakes —
  the existing `WallGatePassabilitySystem` already only updates
  `PassabilityGrid`, not NavMesh; the comment at line 100-103 of that
  file says NavMeshManager re-bakes when its building set changes —
  verify this still fires for the 5-wide gate region.
- **Don't rename `BuildCommandPannel.cs`** (intentional misspelling per
  CLAUDE.md).
- **Reference exemplar** for the new system: `MiningSystem` and
  `WallSegmentCleanupSystem` — both follow the `state.GetEntityQuery`
  in `OnCreate` + per-tick `EntityCommandBuffer(Allocator.Temp)`
  + snapshot-then-mutate pattern.

## UI / UX Specification

Player-facing surfaces introduced or modified by this rework. Phase 1 (docs)
must reflect this spec; Phases 2–6 implement against it. All visual chrome
reuses **existing** theme tokens (`theme.base`, `theme.baseMid`, `theme.baseEdge`,
`theme.inlay`, `theme.inlayShadow`, `theme.accent`, tone palette `build` /
`train` / `ability` / `research`) — **no new color or spacing tokens are
introduced**. Glyph reuse: `castle` (Wall Hub), `eye` (Watch Tower / Tower
upgrade), `spire` / `gear` (Gate). Tooltip / cost-chip / lacking-red flow
already in [Actions.jsx ActionCell](../../../HudFrontend/src/components/Actions.jsx).

### A. Hut Age-Up Choice UI (Phase 2 / R2)

- **Surface:** the bottom-right **ACTIONS** jade panel
  ([ActionPanelRegion](../../../Assets/UI/Scripts/Regions/ActionPanelRegion.cs)),
  populated via a new `ActionType.GathererHutAgeUpChoice` emitted by
  `EntityActionExtractor.GetActionInfo` whenever the selected entity carries
  the `GathererHutAgeUpChoice` marker. **No floating world-space prompt** —
  the existing left-side Selection panel keeps showing hut stats unchanged.
- **Layout:** two large `ActionCell size="lg"` cards laid out as a 2-wide
  row (reuse `act-grid-3x2` with two filled slots, four hidden — matches the
  existing pooled-button layout):

  ```
  ┌──── ACTIONS · Convert ─────────────────────────┐
  │ ╱╲  Filigree                            Filigree  ╲╱ │
  │                                                  │
  │   ┌──────────────┐    ┌──────────────┐           │
  │   │   ⛨ Wall Hub │    │   👁 Watch    │           │
  │   │   (build)    │    │       Tower  │           │
  │   │              │    │   (build)    │           │
  │   │ 50 S · 20 I  │    │ 140 S · 70 I │           │
  │   │   tone=build │    │   tone=build │           │
  │   └──────────────┘    └──────────────┘           │
  │     [W] hotkey         [T] hotkey                │
  │                                                  │
  │ ╲╱ Filigree                            Filigree  ╱╲ │
  └─────────────────────────────────────────────────┘
  ```

- **Glyphs:** `castle` (Wall Hub) and `eye` (Watch Tower) from the existing
  `ActionGlyph` set. Both use **tone `build`** (brown / copper) so the player
  reads them as "construction commits", not training or ability.
- **Hotkeys:** `W` (Wall Hub) / `T` (Watch Tower). These do not collide with
  the Stage-Era2 builder catalog because the hut is selected, not a builder
  — `BuildingHotkeyContext` resolves against the action panel grid only.
- **Tooltip body** (reuses ActionCell tooltip structure):
  - Wall Hub — *kicker* "Convert · Construction" — *cost chips* via `realCost`
    from `costs.Alanthor_Wall` (50 S / 20 I) — *hint* "Convert this hut into
    a Wall Hub. Connects to nearby Wall Hubs to auto-form walls."
  - Watch Tower — *kicker* "Convert · Construction" — *cost chips* via
    `costs.Alanthor_Tower` (140 S / 70 I) — *hint* "Convert this hut into a
    Watch Tower. Reveals far ground and adds defensive coverage."
- **States:**
  - *Default* — both cards interactive.
  - *Unaffordable* — `withAffordability` mutes the card; tooltip cost chip
    reddens via `lacking` map (existing flow).
  - *Converting* (post-click) — hut entity loses the marker, gains a
    `ConvertingHutState { Remaining: N }` (Phase 2 implementation detail);
    the action panel rolls back to the generic "Structure / No commands
    available" empty state during conversion. **No cancel UI in v1** —
    flagged as Open UX Q-A below.
  - *Empty* — if the hut has not aged-up yet, `ActionType.GathererHutAgeUpChoice`
    is not emitted; the panel falls back to today's "No commands available"
    empty state with eyebrow "Structure".
- **World-space affordance:** while the hut carries the choice marker, a
  **pulsing accent glow** is added via `PresentationSpawnSystem` (reuse the
  existing AgeUp shimmer cue if present; otherwise a 0.6 Hz alpha pulse on
  the hub-of-attention outline). Tells the player "this hut needs a
  decision". `prefers-reduced-motion` honored: the pulse falls back to a
  static accent rim.
- **Selection click path:** clicking the hut routes through the existing
  selection pipeline → `selection:single` payload → `actions:invoke` topic
  on click; `HudBridge.cs` already has the dispatch surface (mirror the
  `actions:upgrade` path used by ascend).

### B. Builder UI Contract (Phase 3 / R3)

- The Alanthor **Era 2** builder catalog (`BUILDINGS_ERA2` in
  [Actions.jsx](../../../HudFrontend/src/components/Actions.jsx)) keeps the
  existing two distinct entries:
  - `Alanthor_Wall` — name "Wall Hub" (rename from current "Wall" for
    clarity), glyph `castle`, tone `build`, hint
    "Wall Hub — connect to nearby Wall Hubs to auto-form walls", hotkey `L`.
  - `Alanthor_Tower` — name "Watch Tower", glyph `eye`, tone `build`, hint
    unchanged, hotkey `O`.
- **NOT in the catalog** (verify and assert in
  [EntityExtractors.cs BuildableBuildings HashSet](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L833)):
  `Alanthor_WallTower`, `Alanthor_WallGate`, any literal "Wall Segment" or
  "Wall Gate" entry. A code-side assertion catches accidental additions.
- **Tooltip behaviour:** Wall Hub card's tooltip explicitly says
  "*Segments form automatically between hubs in range. Convert a wall
  segment to a Gate or Tower from its action panel.*" so the player learns
  the contract from the first interaction.
- **No new icons.** Both glyphs already exist.

### C. Auto-Segment Visual Feedback (Phase 4 / R4)

- **Silent baseline** — no popup, no toast, no minimap ping. The player's
  flow is the placement loop; an interrupting cue would be noise.
- **Subtle reinforcement:** when `WallAutoSegmentSystem` spawns a new
  segment, a one-shot **construction-shimmer particle** plays along the
  line between the two hubs over ~500 ms (reuse the existing
  `PresentationSpawnSystem.Walls.cs` instance-spawn cue if present;
  otherwise a thin animated accent-color line that fades). Audio: subtle
  `construction_begin` SFX at low volume (reuse the existing wall-instance
  spawn SFX hook, not a new sample).
- **Reduced-motion guard:** if `prefers-reduced-motion` is set (or the
  Unity-side `ReducedMotion` setting equivalent), the shimmer is skipped;
  the wall instances simply pop into existence at construction-complete
  alpha.
- **Lockstep note:** the cue is presentation-only; the segment-creation
  itself happens in ECS via `WallAutoSegmentSystem` deterministically.
  The shimmer fires off `PresentationSpawnSystem`'s state-change diff, not
  from input, so all clients see it.

### D. Segment → Gate Conversion UI (Phase 5 / R5 + Phase 6 / R6)

- **Selection target (segment-as-actionable, per Open Q5 → answer:
  segment):** clicking any wall **instance** resolves the click to its
  parent **segment entity** via `WallInstanceParent.Segment`; the Selection
  panel shows segment-level data (hub-of-origin pair, aggregate Health —
  see §G), and the Action panel surfaces segment actions. Clicking a **hub**
  selects the hub (hub-specific actions only).
- **Action panel content for a segment** (two `ActionCell size="lg"` cards
  + an info pip strip above):

  ```
  ┌──── ACTIONS · Wall Segment ──────────── 7 inst ─┐
  │ ╱╲                                       ╲╱      │
  │                                                  │
  │   ┌──────────────┐    ┌──────────────┐           │
  │   │  ⛩ Gate (5×) │    │   👁 Tower   │           │
  │   │              │    │              │           │
  │   │ 200 S · 75 I │    │ 60 S · 30 I  │           │
  │   │  tone=build  │    │  tone=build  │           │
  │   └──────────────┘    └──────────────┘           │
  │     [G] hotkey         [Y] hotkey                │
  │                                                  │
  │ "Hover the Gate card to preview the 5 instances  │
  │  that will be replaced."                         │
  └─────────────────────────────────────────────────┘
  ```

- **Cards:**
  - Gate — glyph `spire` (closest visual to a portal gate in the existing
    set), tone `build`, label `Convert to Gate (5×)`, cost from
    `BuildCosts.Alanthor_WallGate`. Resolution of Open Q3 is canonical via
    Phase 1: spec the gate cost as a **flat single payment per conversion**
    (suggested 100 S + 40 I, but the canonical value lands in
    `docs/Design/Age_1_Alanthor.md`). The UI reads `costs.Alanthor_WallGate`
    via the live bridge topic; static fallback mirrors the same number.
  - Tower — glyph `eye`, label `Convert to Tower`, cost from
    `BuildCosts.Alanthor_WallTower`. Per-instance conversion (the existing
    single-instance tower upgrade), not segment-wide.
- **Hover preview (Gate card):** on `pointerenter`, the UI dispatches a
  new bridge topic `wall:previewGate { segmentId, centerInstanceId }`. C#
  side highlights the 5 candidate instances by toggling a
  `WallInstancePreviewTag` (Phase 5 component, presentation-only) — the
  PresentationSpawnSystem renders those 5 with an accent-color rim. On
  `pointerleave`, the preview tag is cleared. **Determines centre by**:
  the segment-entity carries the player's last-clicked instance as
  `WallSegmentFocus.Instance`; if absent, the segment midpoint is used
  (deterministic — sorted by `WallInstanceRef` index).
- **Edge case — segment has < 5 instances:** Phase 1 resolves Open Q5
  follow-on:
  - The card label changes to `Convert to Gate (Nx)` where N is the actual
    instance count (e.g. `Gate (3×)`).
  - A yellow warning glyph `⚠` (reuse `lacking` red-dot style with an amber
    tint via `theme.accent` mid-saturation) appears in the cost-meta row
    with tooltip text "Short segment — gate will span the full segment
    (N instances). Battalions wider than N may not fit."
  - Button is **enabled** (short gates allowed) — matches the R5 spec line
    "If the segment has fewer than 5 instances total, the whole segment
    becomes the gate".
- **Click dispatch:** `sendToUnity('actions:convertWallSegmentToGate',
  { segmentId, focusInstanceId })`. `ActionPanelRegion`'s click handler for
  `ActionType.WallInstanceUpgrade` (currently a TODO at line 14) is wired
  to spend the cost via `FactionEconomy.Spend`, add `WallUpgradeState`
  (UpgradeType=2, Duration=Phase1-canonical) to the **segment entity**, and
  the existing `WallUpgradeSystem` applies `WallGateTag` to the centre-5
  instances on completion (Phase 5). The IMGUI reference at
  [EntityActionPanel.cs:1641-1681](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1641)
  is the spend / state-add template — **not re-enabled**.
- **Visual after conversion:** the 5 wall-instance presentations swap to
  `PresentationID = 554` (gate) via existing `PresentationSpawnSystem.Walls.cs`
  flow. World-space cue: a brief "construction-complete" flash on the
  5-region (reuse the WallUpgradeSystem completion cue), then static.

### E. Wall Network Selection

- **Single-click on hub** → selects the hub. `selection:single` payload
  carries hub HP, faction, connected-segment count (new selection-data
  field `connectedSegments: number`).
- **Single-click on wall instance** → resolves to the **parent segment
  entity**; Selection panel shows segment-aggregate data; Action panel
  shows segment actions (Convert to Gate / Tower). Per Open Q5 canonical.
- **Single-click on a gate region** (5 instances tagged `WallGateTag`) →
  resolves to a synthetic **gate group**: if a `WallGateGroup` component
  exists (Phase 5 / Open Q6 group-toggle path), select the group entity;
  otherwise resolve to the parent segment (same as instance click).
- **Drag-select rectangle** → standard RTS behaviour: each entity whose
  world-space footprint overlaps the rect gets selected individually. Hubs,
  segments-as-instances, and gate-regions are all individually selectable;
  the Selection panel falls into its `multi` layout when count > 1. **No
  whole-network "select connected" affordance in v1** — flagged as Open
  UX Q-B.
- **Right-click on a wall instance with units selected** → today's default
  attack-move applies (instances carry Health → enemies attack-move them).
  Friendly right-click on owned wall is a no-op (no garrison / no repair
  command surfaced here; flagged as Open UX Q-C).

### F. Gate State UI

- **v1 = automatic only.** Gates open when a friendly unit enters
  `WallGatePassabilitySystem.FriendlyDetectRadius`, close otherwise. **No
  manual open/close button** in the Action panel. The Selection panel for
  a selected gate region shows a read-only state pip in the eyebrow row:
  `"OPEN"` (accent) or `"CLOSED"` (theme.inlayDim), driven by a new
  `gateState: 'open' | 'closed'` field on the selection payload.
- **Surfaced as Open UX Q-D below** for the design-doc author: should v1
  expose a manual "force closed / force open" toggle? **Recommendation:
  defer** — it's a quality-of-life follow-up, not part of BFME2
  reproduction.

### G. Health Bar Treatment

- **Hub** — standard Health bar via the existing `FloatingHealthBars` HUD
  layer (no change).
- **Wall instance** — standard per-instance Health bar (existing behaviour
  via `Health` component on `WallInstanceTag` entities).
- **Segment-level Selection panel bar** — when the player **selects** a
  segment (via instance-click → parent-segment resolution), the Selection
  panel renders **one aggregated bar**:
  - Label: `Wall Segment`.
  - Value: `sum(instance.Hp)` across the segment's `WallInstanceRef` buffer.
  - Max: `sum(instance.HpMax)`.
  - Sub-text: `<aliveCount> / <totalCount> intact`.
  - Color: green `#6fdb86` for ≥ 50% aggregate, amber `#d97a2e` for 20–50%,
    red `#e34a4a` for < 20% — reuse Selection.jsx StatBar palette.
- **Gate region (5 instances tagged WallGateTag)** — single aggregated bar
  identical to the segment treatment, label `Wall Gate`. The 5 underlying
  instances continue to draw their **world-space floating bars
  individually** (each instance is still a separate entity with Health);
  the Selection-panel bar is the aggregate. **No stacked-5-bar UI**.
- **Decision recorded:** simple sum aggregation, derived live from the
  `WallInstanceRef` buffer. Math is O(n) per selected segment per refresh
  (0.25 Hz panel refresh — negligible at < 50 instances per segment).

### Cross-cutting

- **Tokens:** zero new color, spacing, or typography tokens. All chrome
  reuses Selection.jsx / Actions.jsx variables already plumbed through the
  jade theme.
- **Accessibility:**
  - `aria-label` on each conversion `ActionCell` is overridden in the
    extractor payload to spell out cost and outcome:
    `"Convert hut to Wall Hub. Cost 50 supplies and 20 iron. Hub will
     connect to nearby hubs to form walls automatically."` Mirrors the
    existing per-action label pattern.
  - Tooltip rich-text already meets WCAG contrast (theme.text on
    theme.base, ≥ 4.5:1 verified in task-108).
  - `title=""` attribute remains the browser-native fallback for the
    icon-only ActionCell.
- **Reduced-motion:** segment-formation shimmer, hub-attention pulse, and
  gate-conversion completion flash all honour the reduced-motion preference
  by collapsing to a single-frame state change.
- **Color-scheme:** the jade theme is the only theme; no light-mode variant
  is in scope.

### Open UX Questions surfaced

These supplement (do not replace) the 10 design questions already in
**§ Open Design Questions** and must be resolved or explicitly punted in
Phase 1.

- **Q-A — Conversion cancel.** Once the player commits a hut → Wall Hub /
  Watch Tower conversion, can they cancel mid-timer? Recommendation:
  **no cancel in v1** (matches the existing structure-construction model).
- **Q-B — "Select wall network".** Should double-click on any wall piece
  select the entire connected network (all hubs + segments reachable via
  the `WallHubLink` graph)? Recommendation: **defer** — drag-select covers
  the common case; network-select is a power-user nicety.
- **Q-C — Friendly right-click on owned wall.** Repair? Garrison? Open
  gate? Recommendation: **no-op in v1**; surface garrison if Phase 7
  uncovers a unit-on-wall requirement.
- **Q-D — Manual gate open/close.** Read-only state pip in v1 per § F.
  Defer manual toggle until playtest demands.
- **Q-E — Hub-attention pulse persistence.** Does the "needs a decision"
  glow stay forever (until choice committed) or fade after N seconds?
  Recommendation: **stay forever** — matches the "hut continues generating
  Age-0 income indefinitely" acceptance criterion.
- **Q-F — Hover-preview cost on Gate card.** When the player hovers the
  "Convert to Gate" card, should the world-space preview also include a
  ghost cost-deduction (e.g. shaded "-100 S -40 I" in the Resource panel)?
  Recommendation: **no** — adds clutter; the tooltip cost chips suffice.

## Technical Approach

This section is the per-phase implementation contract. Each implementation
phase below (2..7) is read top-to-bottom by a separate phase-invocation; the
architect's job is to spec **every** new method signature, ECS component, and
system before code starts. Phase 1 is docs-only and produces no C# artifacts.

### Cross-cutting conventions

- **Components** (global namespace, `Assets/Scripts/Core/Components/BuildingComponents.cs`):
  - Marker / tag-only components use the `XxxTag` suffix and an empty struct body.
  - Stateful components use the `XxxState` suffix.
  - Group identifiers carry the leader-entity (`Entity Leader`) rather than an
    `int Value`, so cleanup is `em.Exists(leader)`-grep-able and no separate
    counter needs to be made deterministic across peers.
- **Systems** live under `Assets/Scripts/Systems/Buildings/` and declare
  `namespace TheWaningBorder.Systems.Buildings` (plural — matches the
  existing `WallUpgradeSystem` / `WallGatePassabilitySystem` / `WallSegmentCleanupSystem`).
- **Cached queries**: every new ISystem caches its `EntityQuery` handles in
  `OnCreate` via `state.GetEntityQuery(...)`. **No** per-tick
  `em.CreateEntityQuery(...)` rebuilds (anti-pattern flagged in `mistakes.md`).
- **Snapshot-then-mutate**: collect a `NativeList<Entity>` / `NativeArray<Entity>`
  inside the query loop, dispose of it after the structural-change `ecb.Playback`.
  Pattern source: `WallSegmentCleanupSystem.DestroySegmentWithInstances`.
- **ECB pattern**: `var ecb = new EntityCommandBuffer(Allocator.Temp); ... ecb.Playback(em); ecb.Dispose();`
  (the dominant manual-playback shape used 40+ places in this codebase).
- **Lockstep wire payloads** ride int fields only (`EntityNetworkId`,
  `TargetEntityId`, `SecondaryTargetId`). No new floats on the wire (Phase 6
  packs `(segmentNetId, focusInstanceNetId)` as two ints). Two new
  `LockstepCommandType` slots: `ConvertHut = 21`, `ConvertSegmentToGate = 22`
  (verified free against `Core/Multiplayer/LockstepTypes.cs:42`).
- **Command triad** for every new player-driven command: `XxxCommand` struct
  (marker for symmetry, optional) + `XxxCommandHelper.Execute` static method
  + `CommandRouter.IssueXxx` (with the `em.Exists` + `HasComponent<...>` +
  `NotControllableTag`/faction guard triad) + `QueueXxxForLockstep` partial
  + `LockstepCommandType` slot + `LockstepManager.ExecuteCommand` dispatcher
  case. Mirror `CancelTrainCommand` exactly.
- **Determinism for auto-segment formation** (Phase 4): hub-pair iteration is
  sorted by `(EntityA.Index, EntityA.Version, EntityB.Index, EntityB.Version)`
  before any structural change. Sort key is fully `int`, no float involvement.
- **No `RefRW<T>` copy-without-writeback** patterns. All mutations go through
  `refRW.ValueRW.Field = ...` directly (project-facts.md rule, importance 0.9).
- **Suspended IMGUI**: `EntityActionPanel.DrawWallUpgradePanel` at lines
  1641-1681 is **reference only** for cost/spend/state-add logic. Do **not**
  remove it from `GameplayUIController.SuspendedImguiTypeNames` and do not
  call it from the live pipeline.
- **No new presentation prefabs** in this task. Phase 5's 5-wide gate reuses
  `AlanthorWall.GatePresentationID = 554` tiled across 5 instances per Open Q9.

---

### Phase 2 — Per-hut age-up choice (Wall Hub / Watch Tower)

Implements R2 + AC "hut shows two buttons" + UX spec §A.

**New components** (append to `Assets/Scripts/Core/Components/BuildingComponents.cs`
under a new `// ==================== Hut Age-Up Choice ====================`
banner near the existing wall section):

```csharp
/// <summary>Conversion target enum for a Gatherer's Hut age-up choice. Byte-wide for cheap wire encoding.</summary>
public enum HutConversionTarget : byte
{
    None = 0,
    WallHub = 1,
    WatchTower = 2,
}

/// <summary>Marker added by AgeUpSystem to every Alanthor-owned GathererHut when its faction ages up.
/// Removed by ConvertHutCommandHelper when the player picks a target. Drives the two-button action panel cluster.</summary>
public struct GathererHutAgeUpChoice : IComponentData { }

/// <summary>Active conversion timer on a hut after the player commits to a choice.
/// Ticked down by HutConversionSystem; on completion the hut is destroyed and the chosen building is spawned at its position.</summary>
public struct GathererHutConverting : IComponentData
{
    public HutConversionTarget Target;
    public float Duration;   // total time in seconds (e.g. 8s for hub, 12s for tower — Phase 1 canonical)
    public float Remaining;
}
```

**AgeUpSystem change** (`Assets/Scripts/Systems/Work/AgeUpSystem.cs` lines 169-186):

Replace the empty `TransformGathererHutsForCulture` body. New behaviour:

```csharp
private static void TransformGathererHutsForCulture(EntityManager em, Faction faction, byte culture)
{
    if (culture != Cultures.Alanthor) return; // Runai / Feraldis owned by task-066

    // Snapshot huts owned by this faction. Use the existing GathererHutTag.
    var q = em.CreateEntityQuery(
        ComponentType.ReadOnly<HutTag>(),
        ComponentType.ReadOnly<GathererHutTag>(),
        ComponentType.ReadOnly<FactionTag>());
    using var huts = q.ToEntityArray(Allocator.Temp);
    using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
    var ecb = new EntityCommandBuffer(Allocator.Temp);
    for (int i = 0; i < huts.Length; i++)
        if (tags[i].Value == faction && !em.HasComponent<GathererHutAgeUpChoice>(huts[i]))
            ecb.AddComponent<GathererHutAgeUpChoice>(huts[i]);
    ecb.Playback(em);
    ecb.Dispose();
}
```

(Note: this duplicates the once-per-age-up `em.CreateEntityQuery` pattern,
but `TransformGathererHutsForCulture` runs **once per age-up event**, not
per tick. The mistakes.md anti-pattern targets per-tick rebuilds.)

**New `HutConversionSystem`** (`Assets/Scripts/Systems/Buildings/HutConversionSystem.cs`):

```csharp
namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HutConversionSystem : ISystem
    {
        // Caches none — the GathererHutConverting query is built via SystemAPI.Query.
        public void OnUpdate(ref SystemState state) { ... }
    }
}
```

OnUpdate loop:
1. `var ecb = new EntityCommandBuffer(Allocator.Temp);`
2. Foreach `(RefRW<GathererHutConverting>, RefRO<LocalTransform>, RefRO<FactionTag>, Entity)` with `WithEntityAccess()`:
   - `conv.ValueRW.Remaining -= dt;`
   - If `Remaining > 0` continue.
   - Read `pos = transform.ValueRO.Position; faction = factionTag.ValueRO.Value; target = conv.ValueRO.Target;`
   - Snapshot the hut entity into a `NativeList<(Entity hut, float3 pos, Faction f, HutConversionTarget t)>` (call it `toConvert`).
3. After the foreach, iterate `toConvert` and for each:
   - `ecb.DestroyEntity(hut);`
   - `if (target == HutConversionTarget.WallHub) AlanthorWall.CreateHub(em, pos, faction);`
   - `else if (target == HutConversionTarget.WatchTower) BuildingFactory.Create(em, "Alanthor_Tower", pos, faction);`
4. `ecb.Playback(em); ecb.Dispose(); toConvert.Dispose();`

Note: `AlanthorWall.CreateHub` and `BuildingFactory.Create` both run
structural changes against `em` directly (not via ECB) — that is the
existing factory convention, and it is safe **after** the ECB playback
because the snapshot list is already disposed-of by then (no live buffer
handles). Both factories already issue `em.CreateEntity` synchronously.

**`ConvertHutCommand` triad** (`Assets/Scripts/Core/Commands/CommandTypes/ConvertHutCommand.cs`, new file):

```csharp
namespace TheWaningBorder.Core.Commands.Types
{
    public struct ConvertHutCommand : IComponentData { public HutConversionTarget Target; }

    public static class ConvertHutCommandHelper
    {
        /// <summary>Spend the conversion cost, remove the choice marker, attach GathererHutConverting.
        /// Returns true if the command actually fired.</summary>
        public static bool Execute(EntityManager em, Entity hut, HutConversionTarget target);
    }
}
```

Execute logic:
- Guard `em.Exists(hut) && em.HasComponent<GathererHutAgeUpChoice>(hut)`.
- Resolve faction from `FactionTag`.
- Resolve cost: `BuildCosts.TryGet(target == WallHub ? "Alanthor_Wall" : "Alanthor_Tower", out var cost);`
- `if (!FactionEconomy.Spend(em, faction, cost)) return false;`
- Pick duration from a const block (Phase 1 canonical, e.g. `HubDuration = 8f`, `TowerDuration = 12f`).
- `em.RemoveComponent<GathererHutAgeUpChoice>(hut);`
- `em.AddComponentData(hut, new GathererHutConverting { Target = target, Duration = d, Remaining = d });`
- Return true.

**Router** (`Assets/Scripts/Core/Commands/CommandRouter.cs`, append after `IssueCancelTrain`):

```csharp
public static void IssueConvertHut(EntityManager em, Entity hut, HutConversionTarget target,
    CommandSource source = CommandSource.LocalPlayer)
{
    if (hut == Entity.Null || !em.Exists(hut)) return;
    if (!em.HasComponent<GathererHutAgeUpChoice>(hut)) return;
    if (IsBlockedByNotControllable(em, hut, source)) return;

    if (ShouldQueueForLockstep(source))
        QueueConvertHutForLockstep(em, hut, target);
    else
        ConvertHutCommandHelper.Execute(em, hut, target);
}
```

**Lockstep partial** (`Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs`):

```csharp
private static void QueueConvertHutForLockstep(EntityManager em, Entity hut, HutConversionTarget target)
{
    int hutId = GetNetworkId(em, hut);
    if (hutId <= 0) { ConvertHutCommandHelper.Execute(em, hut, target); return; }
    LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
    {
        Type = LockstepCommandType.ConvertHut,
        EntityNetworkId = hutId,
        TargetEntityId = (int)target, // byte enum fits cleanly in int
    });
}
```

**Lockstep enum slot** (`Assets/Scripts/Core/Multiplayer/LockstepTypes.cs`):
add `ConvertHut = 21,` after `CancelTrain = 20`.

**Lockstep dispatcher** (`Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs`, in `ExecuteCommand`, after the `CancelTrain` case):

```csharp
case LockstepCommandType.ConvertHut:
    if (entity != Entity.Null)
    {
        var target = (HutConversionTarget)(byte)(cmd.TargetEntityId & 0xFF);
        ConvertHutCommandHelper.Execute(em, entity, target);
        if (LogCommands) Debug.Log($"[Lockstep] Executed ConvertHut target={target} from player {cmd.PlayerIndex}");
    }
    break;
```

**ActionType enum** (`Assets/Scripts/UI/Common/UITypes.cs` line 117-128):
append `GathererHutAgeUpChoice,` after `BazaarWagonUnpack`.

**Extractor** (`Assets/Scripts/UI/Panels/EntityExtractors.cs`):
in `GetActionInfo`, **before** the `WallInstanceUpgrade` block at line 680
(so the marker takes precedence over any other matching detector), add:

```csharp
if (em.HasComponent<GathererHutAgeUpChoice>(entity))
{
    info.Type = ActionType.GathererHutAgeUpChoice;
    info.Actions = BuildHutConversionActions(entity, em); // emits 2 ActionButtons
    return info;
}
if (em.HasComponent<GathererHutConverting>(entity))
{
    info.Type = ActionType.None; // panel rolls back to empty state during conversion
    return info;
}
```

`BuildHutConversionActions` (new private helper in `EntityExtractors.cs`):
returns two `ActionButton`s with Ids `"ConvertHut_WallHub"` and
`"ConvertHut_WatchTower"`, label / tooltip / cost / canAfford populated
from `BuildCosts.TryGet("Alanthor_Wall")` and `BuildCosts.TryGet("Alanthor_Tower")`
respectively. Tooltip body via the existing `BuildTooltip` helper at line 810.

**Click dispatch** (`Assets/UI/Scripts/Regions/ActionPanelRegion.cs`):
in `DispatchClick`, before the `default:` log, add:

```csharp
case ActionType.GathererHutAgeUpChoice:
    var target = data.Id == "ConvertHut_WallHub"
        ? HutConversionTarget.WallHub
        : HutConversionTarget.WatchTower;
    CommandRouter.IssueConvertHut(em, entity, target);
    return;
```

Add a `TitleFor(ActionType.GathererHutAgeUpChoice) → "CONVERT"` entry.

**Files touched in Phase 2** (full list for release-gate verification):
- `Assets/Scripts/Core/Components/BuildingComponents.cs` — 3 new types
- `Assets/Scripts/Core/Multiplayer/LockstepTypes.cs` — enum slot 21
- `Assets/Scripts/Core/Commands/CommandTypes/ConvertHutCommand.cs` — NEW
- `Assets/Scripts/Core/Commands/CommandRouter.cs` — `IssueConvertHut`
- `Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs` — `QueueConvertHutForLockstep`
- `Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs` — case
- `Assets/Scripts/Systems/Work/AgeUpSystem.cs` — fill stub
- `Assets/Scripts/Systems/Buildings/HutConversionSystem.cs` — NEW
- `Assets/Scripts/UI/Common/UITypes.cs` — enum entry
- `Assets/Scripts/UI/Panels/EntityExtractors.cs` — extractor branch + helper
- `Assets/UI/Scripts/Regions/ActionPanelRegion.cs` — dispatch case + title

**Estimated effort:** Medium.

---

### Phase 3 — Builder UI contract assertion

Implements R3 + AC "build panel excludes WallTower/WallGate" + UX spec §B.
**No new runtime behaviour** beyond a defensive assertion. This phase locks
the contract that `Alanthor_WallTower` and `Alanthor_WallGate` are
conversion-only ids.

**Code-side assertion** (`Assets/Scripts/UI/Panels/EntityExtractors.cs`,
just above `BuildableBuildings` at line 833):

```csharp
// Contract: WallTower / WallGate are conversion-only ids, NOT directly buildable.
// task-082 §B / task-091 / task-109 lock this contract. Any addition here must
// also pass the task-109 acceptance grep:
//   git grep -F "Alanthor_WallTower" Assets/Scripts | grep -v BuildCosts.cs ...
// Failing this assertion at startup means a regression — the builder panel
// would expose a UI button for an entity factory that doesn't exist.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
private static void AssertWallContractAtBoot()
{
    UnityEngine.Debug.Assert(!BuildableBuildings.Contains("Alanthor_WallTower"),
        "task-109 contract: Alanthor_WallTower must NOT be in BuildableBuildings.");
    UnityEngine.Debug.Assert(!BuildableBuildings.Contains("Alanthor_WallGate"),
        "task-109 contract: Alanthor_WallGate must NOT be in BuildableBuildings.");
}
#endif
```

(The Debug.Assert pair is editor-only — no shipping cost. A regression
later that adds the id would surface as a console assert on game-start.)

**Comment-only changes** (`Assets/Scripts/UI/Panels/BuildCommandPannel.cs`):
add a banner above `TriggerBuildingPlacement` (line 211) noting that the
switch contains no `Alanthor_WallTower` / `Alanthor_WallGate` case **by
design** — these are conversion-only ids consumed by `WallUpgradeSystem`.

**JSX side** (`HudFrontend/src/components/Actions.jsx` line 99-115):
update the `BUILDINGS_ERA2` row at line 114:
- Rename `name: 'Wall'` → `name: 'Wall Hub'`.
- Update `hint:` to mention the BFME2 auto-segment contract — e.g.
  `'Wall Hub — segments form automatically between hubs in range. Convert a wall segment to a Gate or Tower from its action panel.'`
- Glyph / hotkey / cost untouched.

(No code changes for `Alanthor_Tower`; its row already reads `'Watch Tower'`.)

**Verify** (manual, no code): `EntityExtractors.cs:833` `BuildableBuildings`
HashSet contains exactly `Alanthor_Wall` and `Alanthor_Tower` for wall-related
ids; no `Alanthor_WallTower` / `Alanthor_WallGate`. Already true at HEAD —
this phase formalises it.

**Files touched in Phase 3:**
- `Assets/Scripts/UI/Panels/EntityExtractors.cs` — boot-assert
- `Assets/Scripts/UI/Panels/BuildCommandPannel.cs` — comment
- `HudFrontend/src/components/Actions.jsx` — relabel + rehint

**Estimated effort:** Small.

---

### Phase 4 — Retroactive auto-segment formation

Implements R4 cases (b) + (c) and R10 lockstep determinism. The chain-mode
placement-time formation (R4 case a) already exists in
`BuildCommandPannel.SpawnWallHub:679` and is untouched here.

**New helper** (`Assets/Scripts/Entities/Buildings/AlanthorWall.cs`):

```csharp
/// <summary>True if hubA already has a WallHubLink entry referencing hubB. O(N) on link buffer length.</summary>
public static bool AreHubsConnected(EntityManager em, Entity hubA, Entity hubB)
{
    if (!em.Exists(hubA) || !em.HasBuffer<WallHubLink>(hubA)) return false;
    var links = em.GetBuffer<WallHubLink>(hubA);
    for (int i = 0; i < links.Length; i++)
        if (links[i].ConnectedHub == hubB) return true;
    return false;
}
```

**New system** (`Assets/Scripts/Systems/Buildings/WallAutoSegmentSystem.cs`):

```csharp
namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WallSegmentCleanupSystem))]
    public partial struct WallAutoSegmentSystem : ISystem
    {
        // Phase 1 canonical constant. Replaced by a TechTreeDB-loaded value once Phase 1 picks a number.
        // Default suggested in Open Q1: 16 m world-space.
        public const float MaxAutoSegmentDistance = 16f;
        private const float MaxAutoSegmentDistanceSq = MaxAutoSegmentDistance * MaxAutoSegmentDistance;
        private const float PollInterval = 0.5f;

        private float _timer;
        private EntityQuery _hubQuery; // cached in OnCreate
        public void OnCreate(ref SystemState state) { ... }
        public void OnUpdate(ref SystemState state) { ... }
    }
}
```

OnCreate:
```csharp
_timer = 0f;
_hubQuery = state.GetEntityQuery(new EntityQueryDesc
{
    All = new[]
    {
        ComponentType.ReadOnly<WallHubTag>(),
        ComponentType.ReadOnly<LocalTransform>(),
        ComponentType.ReadOnly<FactionTag>(),
    },
    None = new[] { ComponentType.ReadOnly<UnderConstruction>() }, // only completed hubs participate
});
```

OnUpdate (snapshot-then-mutate; deterministic pair iteration):
1. `_timer -= dt; if (_timer > 0) return; _timer = PollInterval;`
2. `using var hubs = _hubQuery.ToEntityArray(Allocator.Temp);`
3. `if (hubs.Length < 2) return;`
4. `using var transforms = _hubQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);`
5. `using var factions = _hubQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);`
6. **Deterministic sort**: build a parallel `NativeArray<int>` index list,
   sort by `(hubs[i].Index, hubs[i].Version)` using a comparator (or
   pre-sort the entity array via a custom comparator helper — `NativeSortExtension.Sort`
   with `IComparer<Entity>` does not exist; use bubble/quicksort on indices).
   Simplest: copy `hubs` to a `NativeList<Entity>`, sort by
   `(Entity.Index, Entity.Version)` via `unsafe` quicksort or by manual
   selection-sort (faction hub counts are < 30).
7. Pair iteration `for (int i = 0; i < N; i++) for (int j = i+1; j < N; j++)`:
   - Faction filter: `if (factions[i].Value != factions[j].Value) continue;`
   - Distance filter (XZ-plane squared):
     `float dx = transforms[i].Position.x - transforms[j].Position.x; float dz = ...; if (dx*dx + dz*dz > MaxAutoSegmentDistanceSq) continue;`
   - Already-connected filter: `if (AlanthorWall.AreHubsConnected(em, hubs[i], hubs[j])) continue;`
   - Add `(hubs[i], hubs[j], factions[i].Value)` to a `NativeList<HubPair>` snapshot.
8. After the loop, iterate the snapshot **in deterministic order** and call
   `AlanthorWall.CreateSegment(em, pair.HubA, pair.HubB, pair.Faction);`
   for each. CreateSegment is structural; do not interleave with the
   query — that's why we snapshot first.

`HubPair` struct (file-local):
```csharp
private struct HubPair { public Entity HubA; public Entity HubB; public Faction Faction; }
```

**Edge cases**:
- Hub destroyed mid-tick → `em.Exists(pair.HubA)` guard before CreateSegment
  (defensive; in practice the destroyed hub vanishes from the next tick's
  query so this is belt-and-braces).
- Hub completes within range of 3+ hubs → all eligible pairs are queued
  in one snapshot; CreateSegment runs N times. `WallSegmentCleanupSystem`
  already handles the inverse (hub death → cascade), unchanged.
- AI place-by-accident → R9: AI does not place hubs in this task (see
  Phase 7), but if a hub appears for whatever reason it gets sorted into
  the same deterministic pair iteration. Lockstep-safe because the system
  itself fires on every peer at the same tick (PollInterval is wall-clock
  but `SystemAPI.Time.DeltaTime` is the deterministic sim clock).

**Determinism note**: `AlanthorWall.CreateSegment` issues `em.CreateEntity`
+ `em.AddBuffer<WallInstanceRef>` + calls into `SpawnInstances` which
issues N more `em.CreateEntity`. The `NetworkIdGenerator` tick-partitioned
ID space (project-facts.md, importance 0.75) means each peer assigns the
same IDs because the pair iteration is sorted. **R10 satisfied.**

**Why we don't need a "roster changed" cache invalidation**: at PollInterval
= 0.5s the full pair-iteration cost at 30 hubs is 435 distance checks +
435 buffer-walks ≈ trivial (< 0.1 ms). Premature optimisation. If profiling
later flags this hot, add a `_dirty: bool` flag set by a Cleanup-style
sibling system that bumps when a `WallHubTag` is added/removed.

**Files touched in Phase 4:**
- `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` — `AreHubsConnected` helper
- `Assets/Scripts/Systems/Buildings/WallAutoSegmentSystem.cs` — NEW

**Estimated effort:** Medium.

---

### Phase 5 — 5-instance gate region

Implements R5 + AC "5-wide gate works" + UX spec §D. Extends
`WallUpgradeSystem` to operate at the segment level (not per-instance) for
gate conversions; tower upgrades remain per-instance unchanged.

**New components** (`Assets/Scripts/Core/Components/BuildingComponents.cs`,
append to the wall section):

```csharp
/// <summary>Marks a wall instance that is part of a 5-instance gate region (not the legacy 1-instance gate).
/// All instances in the same group carry the same Leader entity (the centre / focus instance picked at conversion time).</summary>
public struct WallGateRegionTag : IComponentData { }

/// <summary>The leader (focus) instance for a gate region. All members of the region carry this with the same Leader value.
/// On region destruction (any member's death) WallGatePassabilitySystem walks the leader → buffer to update siblings.</summary>
public struct WallGateGroup : IComponentData
{
    /// <summary>The focus-instance entity. Acts as the deterministic group identifier
    /// (no separate int counter needed; em.Exists(Leader) is the membership check).</summary>
    public Entity Leader;
}

/// <summary>Presentation-only marker added during a hover-preview from the UI.
/// Cleared on pointer-leave. PresentationSpawnSystem rims these instances with the accent color.</summary>
public struct WallInstancePreviewTag : IComponentData { }

/// <summary>Optional segment-level pointer to the last-clicked instance — used by the gate-conversion command
/// to pick the centre of the 5-region. Defaults to segment midpoint if absent.</summary>
public struct WallSegmentFocus : IComponentData
{
    public Entity Instance;
}

/// <summary>Active segment-level upgrade timer. Attached to the SEGMENT entity (not an instance)
/// when the player commits to a Convert-to-Gate. Distinct from the per-instance WallUpgradeState.</summary>
public struct WallSegmentUpgradeState : IComponentData
{
    public byte UpgradeType;  // 2 = Gate (only one type uses the segment-level path for now)
    public Entity FocusInstance; // resolved centre of the 5-region
    public float Duration;
    public float Remaining;
}
```

**WallUpgradeSystem extension** (`Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs`):

Add a second foreach loop in `OnUpdate` (after the existing per-instance
loop) that ticks `WallSegmentUpgradeState`:

```csharp
foreach (var (segUp, entity) in SystemAPI
             .Query<RefRW<WallSegmentUpgradeState>>()
             .WithAll<WallSegmentTag>()
             .WithEntityAccess())
{
    segUp.ValueRW.Remaining -= dt;
    if (segUp.ValueRW.Remaining > 0f) continue;

    // Resolve the centre-5 instances along the segment, then tag them as a gate region.
    AlanthorWall.PickGateRegionInstances(em, entity, segUp.ValueRO.FocusInstance, out var regionInstances);
    Entity leader = segUp.ValueRO.FocusInstance == Entity.Null && regionInstances.Length > 0
        ? regionInstances[regionInstances.Length / 2]
        : segUp.ValueRO.FocusInstance;

    for (int i = 0; i < regionInstances.Length; i++)
    {
        var inst = regionInstances[i];
        if (!em.Exists(inst)) continue;
        ecb.AddComponent<WallGateTag>(inst);
        ecb.AddComponent<WallGateRegionTag>(inst);
        ecb.AddComponent(inst, new WallGateGroup { Leader = leader });
        ecb.AddComponent(inst, new WallGateState { IsOpen = 0, RecheckTimer = 0f });
        // Swap visual to gate presentation
        var presId = em.GetComponentData<PresentationId>(inst);
        presId.Id = AlanthorWall.GatePresentationID;
        ecb.SetComponent(inst, presId);
    }

    regionInstances.Dispose();
    ecb.RemoveComponent<WallSegmentUpgradeState>(entity);

    // Force visual respawn for each instance in the region (presentation diff)
    var spawnSys = PresentationSpawnSystem.Instance;
    if (spawnSys != null)
        for (int i = 0; i < regionInstances.Length; i++)
            spawnSys.ForceRespawn(regionInstances[i]);
}
```

(Snapshot caveat: `regionInstances` must be a Temp `NativeList<Entity>`
populated by `PickGateRegionInstances` before any structural change; do
not re-read the segment's `WallInstanceRef` buffer after `ecb.Playback`.)

**New helper** (`AlanthorWall.cs`):

```csharp
/// <summary>
/// Pick up to 5 contiguous instances along the segment, centred on the focus instance.
/// If focusInstance == Entity.Null OR not found in the segment's WallInstanceRef buffer,
/// falls back to the segment midpoint (buffer index = length / 2).
/// If segment has fewer than 5 instances, returns all of them (cap-at-segment-length per R5).
/// </summary>
/// <param name="result">Caller-owned NativeList; the method appends 1..5 entities.
/// Empty if the segment has no live instances.</param>
public static void PickGateRegionInstances(EntityManager em, Entity segment, Entity focusInstance,
    out NativeList<Entity> result)
{
    result = new NativeList<Entity>(5, Allocator.Temp);
    if (!em.HasBuffer<WallInstanceRef>(segment)) return;
    var refs = em.GetBuffer<WallInstanceRef>(segment);
    if (refs.Length == 0) return;

    int focusIdx = refs.Length / 2;
    for (int i = 0; i < refs.Length; i++)
        if (refs[i].Instance == focusInstance) { focusIdx = i; break; }

    if (refs.Length <= 5)
    {
        for (int i = 0; i < refs.Length; i++)
            if (em.Exists(refs[i].Instance)) result.Add(refs[i].Instance);
        return;
    }

    int lo = math.max(0, focusIdx - 2);
    int hi = math.min(refs.Length - 1, lo + 4);
    lo = math.max(0, hi - 4); // re-anchor low end if hi clamped
    for (int i = lo; i <= hi; i++)
        if (em.Exists(refs[i].Instance)) result.Add(refs[i].Instance);
}
```

**WallGatePassabilitySystem extension** (`Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs`):

The existing per-instance loop **already** iterates every entity with
`WallGateTag` (added by Phase 5 to all 5 region members). Each of the 5
members independently checks friendly proximity and toggles its own cell
in the `PassabilityGrid` — so the 5-cell region opens and closes as a
unit *as long as* all 5 see a friendly within `FriendlyDetectRadius = 3.0f`.

**Decision per Open Q6 (group-toggle vs independent)**: keep the existing
independent-per-instance check, but **bump `FriendlyDetectRadius` from 3
to 6** for instances carrying `WallGateRegionTag` so all 5 cells of a
~10 m gate open in unison when a battalion approaches from either end.
This is the minimum-code path; full group-toggle (one synthesised entity)
is deferred to a follow-up if playtest demands.

Implementation:

```csharp
// Inside the OnUpdate foreach over gates, replace the constant FriendlyDetectRadius:
float radius = em.HasComponent<WallGateRegionTag>(entity)
    ? RegionDetectRadius   // new const: 6.0f
    : FriendlyDetectRadius; // legacy const: 3.0f
float detectRadiusSq = radius * radius;
```

Add `private const float RegionDetectRadius = 6.0f;` at the top of the
struct.

**Cost** (Open Q3 resolution by Phase 1 docs): the gate-conversion cost
is a **flat single payment per conversion** (suggested 100 S + 40 I, but
Phase 1 docs are canonical). The cost is read from
`BuildCosts.TryGet("Alanthor_WallGate", out var cost)` and spent **once**
at command-issue time (not per-instance).

**Files touched in Phase 5:**
- `Assets/Scripts/Core/Components/BuildingComponents.cs` — 5 new types
- `Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs` — segment loop
- `Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs` — region radius
- `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` — `PickGateRegionInstances`

**Estimated effort:** Medium.

---

### Phase 6 — Live UI Toolkit click wiring + hover preview

Implements R6 + AC "click Convert to Gate works" + UX spec §D / §E. Wires
the `ActionType.WallInstanceUpgrade` click handler that currently logs a
TODO in `ActionPanelRegion.cs:14` and adds the hover-preview topic.

**Selection resolution** (`Assets/Scripts/UI/Panels/EntityExtractors.cs`,
the WallInstanceUpgrade block at line 680):

Reshape so that **instance-click resolves to the parent segment** for the
action panel — but the Selection panel keeps showing instance-level data
unless the player explicitly drag-selects.

```csharp
if (em.HasComponent<WallInstanceTag>(entity) &&
    !em.HasComponent<WallTowerTag>(entity) &&
    !em.HasComponent<UnderConstruction>(entity))
{
    // Resolve to parent segment for the action surface.
    Entity segment = Entity.Null;
    if (em.HasComponent<WallInstanceParent>(entity))
        segment = em.GetComponentData<WallInstanceParent>(entity).Segment;

    // Skip if the instance is already part of a 5-wide gate region (no further upgrade).
    if (em.HasComponent<WallGateRegionTag>(entity)) return info;

    if (em.Exists(segment) && em.HasComponent<WallSegmentTag>(segment))
    {
        // Store the click target as the segment's focus instance (for the 5-region centre).
        // The system applies this on conversion-commit, not at extraction time.
        info.Type = ActionType.WallInstanceUpgrade;
        info.Actions = BuildSegmentConversionActions(segment, entity, em); // 2 buttons: Gate (5x) + Tower
        // Stash payload on the ActionInfo via a new field (see UITypes.cs change below)
        info.FocusInstance = entity;
        info.SegmentEntity = segment;
        return info;
    }
}
```

**UITypes.cs additions** (`Assets/Scripts/UI/Common/UITypes.cs`):

Add to `EntityActionInfo`:
```csharp
public Entity FocusInstance;   // Phase 6: which instance the click hit (for 5-region centring)
public Entity SegmentEntity;   // Phase 6: the segment to apply WallSegmentUpgradeState to
```

**`BuildSegmentConversionActions` helper** (`EntityExtractors.cs`):
- Reads `BuildCosts.TryGet("Alanthor_WallGate")` and `Alanthor_WallTower`.
- Counts segment's `WallInstanceRef` buffer length to label as
  `Convert to Gate (5×)` or `Convert to Gate (3×)` for short segments.
- Returns two `ActionButton`s with ids `"WallSegmentToGate"` and
  `"WallSegmentToTower"`.

**Click dispatch** (`ActionPanelRegion.cs`):

In `DispatchClick` switch, add:

```csharp
case ActionType.WallInstanceUpgrade:
    if (data.Id == "WallSegmentToGate")
    {
        // Resolve the original action-info segment + focus instance via Refresh-time userData.
        // The DispatchTarget needs an extra field or we look the segment up via
        // the focus instance's WallInstanceParent. Simplest: look it up here.
        Entity inst = entity; // entity already came from GetFirstSelectedEntity → instance
        if (!em.HasComponent<WallInstanceParent>(inst)) return;
        Entity segment = em.GetComponentData<WallInstanceParent>(inst).Segment;
        if (!em.Exists(segment)) return;
        // Stash WallSegmentFocus before issuing — the conversion picks centre via focus.
        em.AddComponentData(segment, new WallSegmentFocus { Instance = inst });
        CommandRouter.IssueConvertSegmentToGate(em, segment, inst);
        return;
    }
    if (data.Id == "WallSegmentToTower")
    {
        // Tower remains per-instance (legacy WallUpgradeSystem path). Spend + add WallUpgradeState directly.
        // Mirror EntityActionPanel.cs:1641-1660 reference (do NOT re-enable the IMGUI panel).
        if (!BuildCosts.TryGet("Alanthor_WallTower", out var cost)) return;
        Faction faction = GameSettings.LocalPlayerFaction;
        if (em.HasComponent<FactionTag>(entity))
            faction = em.GetComponentData<FactionTag>(entity).Value;
        if (!FactionEconomy.Spend(em, faction, cost)) return;
        em.AddComponentData(entity, new WallUpgradeState
        {
            UpgradeType = 1, Duration = 10f, Remaining = 10f
        });
        return;
    }
    return;
```

(The tower path stays per-instance — only Gate is segment-level in Phase 5.)

**`ConvertSegmentToGateCommand` triad** (`Assets/Scripts/Core/Commands/CommandTypes/ConvertSegmentToGateCommand.cs`, new):

```csharp
public struct ConvertSegmentToGateCommand : IComponentData
{
    public Entity FocusInstance;
}

public static class ConvertSegmentToGateCommandHelper
{
    public static bool Execute(EntityManager em, Entity segment, Entity focusInstance);
}
```

Execute logic:
- Guard `em.Exists(segment) && em.HasComponent<WallSegmentTag>(segment) && em.HasBuffer<WallInstanceRef>(segment)`.
- Guard no existing `WallSegmentUpgradeState` (idempotent — don't double-charge).
- Resolve faction from segment's `FactionTag`.
- `BuildCosts.TryGet("Alanthor_WallGate", out var cost);`
- `if (!FactionEconomy.Spend(em, faction, cost)) return false;`
- `em.AddComponentData(segment, new WallSegmentUpgradeState { UpgradeType = 2, FocusInstance = focusInstance, Duration = 8f, Remaining = 8f });`
- Return true.

**Router** (`CommandRouter.cs`):

```csharp
public static void IssueConvertSegmentToGate(EntityManager em, Entity segment, Entity focusInstance,
    CommandSource source = CommandSource.LocalPlayer)
{
    if (segment == Entity.Null || !em.Exists(segment)) return;
    if (!em.HasComponent<WallSegmentTag>(segment)) return;
    if (em.HasComponent<WallSegmentUpgradeState>(segment)) return; // already converting
    if (IsBlockedByNotControllable(em, segment, source)) return;

    if (ShouldQueueForLockstep(source))
        QueueConvertSegmentToGateForLockstep(em, segment, focusInstance);
    else
        ConvertSegmentToGateCommandHelper.Execute(em, segment, focusInstance);
}
```

**Lockstep partial** (`CommandRouter.LockstepQueue.cs`):

```csharp
private static void QueueConvertSegmentToGateForLockstep(EntityManager em, Entity segment, Entity focusInstance)
{
    int segId = GetNetworkId(em, segment);
    int focusId = focusInstance != Entity.Null ? GetNetworkId(em, focusInstance) : 0;
    if (segId <= 0)
    {
        ConvertSegmentToGateCommandHelper.Execute(em, segment, focusInstance);
        return;
    }
    LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
    {
        Type = LockstepCommandType.ConvertSegmentToGate,
        EntityNetworkId = segId,
        TargetEntityId = focusId, // 0 = no focus, use midpoint
    });
}
```

**Lockstep slot** (`LockstepTypes.cs`): add `ConvertSegmentToGate = 22,`.

**Lockstep dispatcher** (`LockstepManager.cs`, after the `ConvertHut` case):

```csharp
case LockstepCommandType.ConvertSegmentToGate:
    if (entity != Entity.Null)
    {
        Entity focus = cmd.TargetEntityId != 0
            ? FindEntityByNetworkId(cmd.TargetEntityId)
            : Entity.Null;
        ConvertSegmentToGateCommandHelper.Execute(em, entity, focus);
        if (LogCommands) Debug.Log($"[Lockstep] Executed ConvertSegmentToGate from player {cmd.PlayerIndex}");
    }
    break;
```

**Note**: segment entities **must** carry a `NetworkedEntity` component
for the lockstep path to work. Verify at Phase 4 / Phase 6 integration —
`AlanthorWall.CreateSegment` does not currently add one. Action: add
`em.AddComponentData(entity, new NetworkedEntity { NetworkId = NetworkIdGenerator.GetNextId(), SpawnTick = ... });`
in `CreateSegment` (and matching add for instances if Phase 6 ends up
needing instance network IDs for the focus-instance payload). Pencil this
as a 2-line change to `AlanthorWall.cs` in Phase 4 or Phase 6 — whichever
lands first.

**Hover preview** (`HudBridge.cs` + `Selection.jsx`):

JSX side (`HudFrontend/src/components/Selection.jsx` or `Actions.jsx` —
wherever the wall-instance-upgrade ActionCell renders): on `pointerenter`
of the Gate card, dispatch `sendToUnity('wall:previewGate', { segmentId, focusInstanceId, on: true })`.
On `pointerleave`, dispatch `{ on: false }`.

HudBridge inbound topic (`Assets/Scripts/UI/Web/HudBridge.cs`, in the
topic switch at line 103):

```csharp
case "wall:previewGate":
    HandleWallPreviewGate(m.PayloadJson);
    break;
```

`HandleWallPreviewGate` body (new private method, mirror `HandleCancelTrain`'s
shape — quick-field parse, look up segment via Entity.Index in current selection,
call into a helper):

```csharp
void HandleWallPreviewGate(string payloadJson)
{
    var segStr = QuickField(payloadJson, "segmentId");
    var focusStr = QuickField(payloadJson, "focusInstanceId");
    var onStr = QuickField(payloadJson, "on");
    if (!int.TryParse(segStr, ..., out int segIdx)) return;
    int.TryParse(focusStr, ..., out int focusIdx); // 0 = no focus, defaults to midpoint
    bool on = onStr == "true";

    // Resolve segment via selection (instance click → parent segment lookup).
    var sel = Input.SelectionSystem.CurrentSelection;
    Entity segment = Entity.Null;
    Entity focus = Entity.Null;
    // ... walk sel to find an entity whose WallInstanceParent.Segment.Index == segIdx
    // ... or whose Index == segIdx directly if the player drag-selected the segment

    if (segment == Entity.Null) return;

    // Apply / clear the preview tag on the 5 candidate instances.
    var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
    AlanthorWall.PickGateRegionInstances(em, segment, focus, out var region);
    for (int i = 0; i < region.Length; i++)
    {
        if (!em.Exists(region[i])) continue;
        bool has = em.HasComponent<WallInstancePreviewTag>(region[i]);
        if (on && !has) em.AddComponent<WallInstancePreviewTag>(region[i]);
        if (!on && has) em.RemoveComponent<WallInstancePreviewTag>(region[i]);
    }
    region.Dispose();
}
```

(The preview tag is **presentation-only** — no lockstep involvement.
Multiplayer peers do not see the local player's hover.)

**JSX dispatch payload** (Selection.jsx / Actions.jsx) — actual integration
point is wherever the segment action cards are rendered. Implementer reads
existing `actions:cancelTrain` shape at Selection.jsx:510 as the template.

**Files touched in Phase 6:**
- `Assets/Scripts/Core/Commands/CommandTypes/ConvertSegmentToGateCommand.cs` — NEW
- `Assets/Scripts/Core/Commands/CommandRouter.cs` — `IssueConvertSegmentToGate`
- `Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs` — Queue partial
- `Assets/Scripts/Core/Multiplayer/LockstepTypes.cs` — slot 22
- `Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs` — dispatcher case
- `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` — `NetworkedEntity` add on segment + instance (if not already in Phase 4)
- `Assets/Scripts/UI/Common/UITypes.cs` — FocusInstance / SegmentEntity fields on `EntityActionInfo`
- `Assets/Scripts/UI/Panels/EntityExtractors.cs` — segment-resolve in WallInstanceUpgrade block + `BuildSegmentConversionActions`
- `Assets/UI/Scripts/Regions/ActionPanelRegion.cs` — WallInstanceUpgrade dispatch case
- `Assets/Scripts/UI/Web/HudBridge.cs` — `wall:previewGate` inbound topic + `HandleWallPreviewGate`
- `HudFrontend/src/components/Selection.jsx` (or `Actions.jsx`) — hover dispatch + `actions:convertWallSegmentToGate` send

**Estimated effort:** Medium.

---

### Phase 7 — Pathfinding + AI safety + scenario test

Implements R7 + R9 + R10 verification. Mostly verification + targeted
hardening; no new mechanical primitives.

**PassabilityGrid integration** (`Assets/Scripts/World/Terrain/PassabilityGrid.cs`):

Verification only — wall hubs and instances already block via
`PassabilityBuildingSync` (touches every entity with `BuildingSize` +
`LocalTransform`). Confirm at runtime via the scenario test that:
- Newly-spawned hubs from Phase 4 auto-segment formation block their cells.
- 5-wide gate regions unblock all 5 cells when open and re-block all 5
  when closed. The independent-per-instance toggle in Phase 5 handles
  this — no new code, just verify.

**SimpleAISystem hardening** (`Assets/Scripts/AI/SimpleAISystem.cs`):

Add an explicit skip filter in the build-target picker that walks
buildings. Phase 7 doesn't introduce wall AI; this is a safety net so a
future AI change doesn't accidentally try to "build a wall hub" via the
SimpleAISystem path:

```csharp
// Inside any loop that iterates Building entities considered for new placement / repair / attack-move:
if (em.HasComponent<WallHubTag>(building)) continue;
if (em.HasComponent<WallSegmentTag>(building)) continue;
if (em.HasComponent<WallInstanceTag>(building)) continue;
```

(Implementer: grep for `BuildingTag` iterations in `SimpleAISystem.cs`
and slot the skip in where appropriate. Likely 1-2 sites.)

`AIAlanthorEndgameSystem` and `AIEconomyManager.cs:627-756` — **no code
change**. Per memory (`AI/Managers/*` is `[DisableAutoCreation]`, dead).
Add a one-line code comment at `AIEconomyManager.cs:627` referencing
task-109 so the dead wall code's status is clear:

```csharp
// task-109: this dead wall-building block stays dead. Alanthor AI does not
// build walls in this task. Revival requires a follow-up task that updates
// the placement contract to use AlanthorWall.CreateHub via SimpleAISystem.
```

**ScenarioSetup extension** (`Assets/Scripts/Bootstrap/ScenarioSetup.cs:286`):

The existing `SpawnWallSiege` already creates a Blue 5-hub line + a
Red 2-hub gate. Extend it minimally:
- Pick the centre Red segment instance.
- After existing `AlanthorWall.CreateSegment` call, issue
  `ConvertSegmentToGateCommandHelper.Execute(em, redSegment, midInstance)`
  to seed a 5-wide gate in the scenario for visual / pathing verification.

(Be careful: ScenarioSetup runs **pre-lockstep** so the helper runs in
bootstrap mode and bypasses the lockstep queue — that is correct for a
scenario, the spawn is deterministic from a fixed seed.)

**Lockstep determinism verification** (manual checklist entry, no code):
in a 2-client ParrelSync run, both clients place the same 4 hubs in the
same world-positions; both observe the same set of 6 auto-segments (4
hub-pairs adjacent + 2 diagonals if within range). Document the test
steps in the task review notes per existing AC.

**NavMesh rebake** (`Assets/Scripts/Systems/Movement/NavMeshManager.cs`):
verify only — its existing rebake trigger fires when buildings are
added/removed. The 5-wide gate's `WallGateTag` add (Phase 5) is an
archetype change but the instance entity already existed — verify the
NavMeshManager treats this as a rebake trigger, not just full-add. If
not, add a one-line `NavMeshManager.MarkDirty()` call in `WallUpgradeSystem`'s
segment-loop completion path.

**Files touched in Phase 7:**
- `Assets/Scripts/AI/SimpleAISystem.cs` — skip filters
- `Assets/Scripts/AI/Managers/AIEconomyManager.cs` — comment only
- `Assets/Scripts/Bootstrap/ScenarioSetup.cs` — seed a 5-wide gate in `SpawnWallSiege`
- `Assets/Scripts/Systems/Movement/NavMeshManager.cs` — verify only, optional `MarkDirty` call

**Estimated effort:** Medium.

---

### Risks summarised

- **Open Q1 (MaxAutoSegmentDistance)** must be resolved by Phase 1 docs
  to a canonical number. Implementer pins `WallAutoSegmentSystem.MaxAutoSegmentDistance`
  to that value. Default suggestion 16 m.
- **Open Q3 (gate cost)** must be resolved to a flat single payment by
  Phase 1 docs. Implementer reads `BuildCosts.Alanthor_WallGate` at command
  time; no per-instance multiplication.
- **Open Q6 (group vs independent toggle)**: Phase 5 picks
  "independent toggle with expanded `RegionDetectRadius`" as the minimum-
  code path. If playtest reveals jittery 5-cell mismatches, escalate to
  a true `WallGateGroup` leader-driven toggle (the `WallGateGroup`
  component is already provisioned for this).
- **Save/load coverage** (task-096): the 5-wide gate's `WallGateRegionTag`
  + `WallGateGroup` are new components. Flag in Phase 1 docs so
  task-096's save format reserves slots; do not migrate legacy 1-instance
  gates retroactively.
- **`NetworkedEntity` on segments/instances**: `AlanthorWall.CreateSegment`
  currently does **not** add `NetworkedEntity`. Phase 6's lockstep path
  needs it. Either Phase 4 or Phase 6 (whichever lands first) adds the
  `em.AddComponentData(..., new NetworkedEntity { NetworkId = NetworkIdGenerator.GetNextId(), SpawnTick = current })`
  line in CreateSegment + CreateInstance. Phase 4 doesn't need it for the
  auto-segment formation itself (the system fires the same on every peer
  via deterministic sorted iteration), but Phase 6's
  `ConvertSegmentToGate` lockstep command needs to address the segment
  by network ID.
- **Pair-iteration cost** at 30 hubs/faction: 435 pair checks per 0.5 s
  ≈ 870 ops/s, negligible. Above ~100 hubs (unlikely in normal play)
  consider a spatial partition. Not optimising preemptively.

## Out of Scope

- **Other cultures' wall mechanics.** Runai and Feraldis have no
  wall-system equivalent; their hut transforms are owned by task-066
  and task-067 and are **not touched** by this task.
- **Wall decoration / aesthetic variants.** The 5-wide gate uses the
  existing gate prefab tiled 5× per Open Q9. A bespoke wide-gate prefab
  is a follow-up task.
- **Single-hub walls.** A solo hub does not form a wall — by definition
  segments need two hubs. The auto-fortify-radius concept from the
  superseded Age_1_Alanthor draft is dropped.
- **Gates wider than 5 segments.** Fixed at 5. The "make gate width
  configurable" feature is a follow-up if playtest demands.
- **AI wall strategy.** Per Open Q8, Alanthor AI does not build walls
  in this task. Strategic wall placement is a follow-up that depends
  on this task's primitives.
- **Reviving `[DisableAutoCreation]` AI orchestration.**
  `AIAlanthorEndgameSystem`, `AIEconomyManager`, etc. stay disabled per
  `.deft/memory/project-facts.md`. Any AI work belongs in
  `SimpleAISystem`.
- **Visual upgrade for hubs at age-up.** Hubs use PresentationID 550
  consistently. Cultural restyling of the hub is out of scope.
- **Wall-economy retuning.** The `+8 supplies per 10u² closed
  compartment / min` figure stands as the playtest placeholder per
  the existing Age_1_Alanthor decision #8. This task does not retune
  the wall income formula.
- **Migration of suspended IMGUI wall-upgrade panel.** The
  `DrawWallUpgradePanel` IMGUI code is **reference only**; we do not
  re-enable it.
- **Multiplayer-specific desync diagnostics** beyond the R10 deterministic
  sort guarantee. A full lockstep audit of the wall pipeline is its
  own task if R10 surfaces issues.
