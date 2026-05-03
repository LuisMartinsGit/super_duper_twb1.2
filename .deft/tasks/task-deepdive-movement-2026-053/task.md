---
deft:
  id: task-deepdive-movement-2026-053
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
  labels: [code-quality, code-review, deep-dive, movement, pathfinding]
---

# Movement / Pathfinding / Battalion / Passability deep-dive

## Context

Companion to [task-052](../task-deepdive-ai-2026-052/task.md), the AI-subsystem deep-dive. Same method: deft-review agent with explicit "no Grep snippets, full Read" instruction; full-file reads of every movement-related file; traced data flows for MoveCommand/Battalion/PassabilityGrid lifecycle/Flow-field invalidation/Cross-system structural-change interactions; recent fixes validated against current code.

Method:
1. Read 20+ files in full (every Movement system, FlowField machinery, all unit factories, both passability systems, all Move/Attack/Patrol/Gather command helpers, SelectionSystem, RTSInputManager, AI behaviors that touch movement)
2. Traced 6 paths end-to-end
3. Validated every recent fix against the live code
4. Looked for architectural concerns the surface scans missed

Outcome: **10 verified findings** (3 are duplicates of MB-1/2/3 from task-051 with traces; 4 are new bugs the surface scan missed; 3 are perf/architectural). Plus 4 architectural smells, 12 confirmations of recent fixes, explicit out-of-scope list.

## User Value

The flow-field core is sound — recent fixes (don't-cache-null, BlendRadius=2m, GatherCommand pre-warm, Miner DesiredDestination, SelectionSystem hierarchy walk) all hold up. The damage is in surrounding glue: a missing-brace cluster in BattalionSyncSystem and UnitSeparationSystem (already known), plus three NEW high-severity bugs the surface scan missed:

- **F-4 (NEW)**: `SelectionSystem` military-prioritization box-select filter is silently a no-op — selecting 5 builders + 5 swordsmen returns all 10 instead of just the 5 swordsmen.
- **F-5 (NEW)**: `CommandQueueSystem` performs structural changes during `SystemAPI.Query` foreach — same iterator-invalidation anti-pattern just fixed in mining systems. Shift-queue feature is broken once a queued command activates.
- **F-6 (NEW)**: `AIScoutingBehavior.cs:285-289` calls `ecb.SetComponent<DesiredDestination>` on Scout entities that never had the component baked in. Guaranteed crash when AI trains a Scout — except `AIScoutingBehavior` is `[DisableAutoCreation]` (per task-052 audit), so this is currently dead-code-with-a-trap rather than a live crash. **Scout.cs should bake DesiredDestination at creation either way** (same as Miner.cs:54-58).

## Requirements

- R1: Triage every verified finding (F-1..F-10).
- R2: Fix High-severity findings before next playtest (F-1, F-2, F-5, F-6).
- R3: Address Medium findings (F-3, F-4, F-7).
- R4: Decide on each architectural smell.

## Acceptance Criteria

- [ ] F-1, F-2, F-3 fixed via the missing-brace sweep (overlap with task-051 MB-1/2/3 — single PR closes all three)
- [ ] F-4: box-select military prioritization actually works — selecting mixed economic+military returns military only
- [ ] F-5: CommandQueueSystem uses ECB pattern for all structural changes
- [ ] F-6: Scout.cs (and other unit factories per task-052 F-7) bake DesiredDestination at creation
- [ ] F-7: BlendRadius vs StopDistance gap addressed (either further tighten BlendRadius or add passability check inside the blend window)
- [ ] F-8, F-9, F-10 either fixed or filed as low-priority follow-ups

---

## Verified Findings

### F-1 — `BattalionSyncSystem.cs:435-438` — Combat-member passability fallback is dead code
**Severity:** High — battalions in combat near terrain or buildings get stuck
**Already filed as MB-1 in task-051; this is the data-flow trace.**

```csharp
if (passGrid.IsPassable(altPos))
    newPos = altPos;
    newPos = memberXf.Position;   // <-- always runs (no braces)
```

`newPos = memberXf.Position` is unconditional. The "try alternate direction" fallback (lines 432-436) computes a flow-field path, but the result is immediately discarded. Battalion combat members blocked by passability **never move toward their target this frame** even if a viable detour exists.

**Fix:** Wrap lines 435-437 in `{ }`; line 437 should be `else newPos = memberXf.Position;`.

### F-2 — `BattalionSyncSystem.cs:514-517` — Formation-member passability fallback is dead code
**Severity:** High — visible as members "lagging behind" or "deserting" near walls
**Already filed as MB-2 in task-051; this is the data-flow trace.**

Identical pattern in §5b. A blocked formation member tries the leader's flow field, then result is discarded. Combined with `BattalionLeashSystem` (which only teleports the *leader* to cluster center, not stuck members), members blocked by an obstacle become permanently stationary while the rest of the battalion moves.

### F-3 — `UnitSeparationSystem.cs:262-265` — Building-push always pops to corner
**Severity:** Medium — cosmetic but visible jank
**Already filed as MB-3 in task-051; this is the data-flow trace.**

Only the X assignment is guarded; Z is unconditional. A unit overlapping a rectangular building's AABB always gets pushed along **both** axes simultaneously — snapped to the nearest corner instead of the nearest edge. The `if (pushX < pushZ)` test was clearly meant to choose the smaller-overlap axis.

### F-4 — **NEW** — `SelectionSystem.cs:463-465` — Box-select military filter is silently a no-op
**Severity:** Medium — quality-of-life bug in box select; same missing-brace pattern, missed by previous scans

```csharp
if (cls == UnitClass.Economy || cls == UnitClass.Miner)
    economic.Add(e);
    military.Add(e);   // <-- runs for every unit
```

`military.Add(e)` is unconditional. Every unit in the box ends up in `military`, so the `if (military.Count > 0 && economic.Count > 0)` branch (line 468) replaces `_selection` with `military` — which already equals `_selection`. **The "prioritize military over economic" filter does nothing.** Box-selecting 5 builders + 5 swordsmen returns all 10, not the 5 swordsmen the comment promises.

**Fix:** Add braces; `military.Add(e)` belongs in an `else`.

### F-5 — **NEW** — `CommandQueueSystem.cs:36, 43, 54-60` — Structural changes during `SystemAPI.Query` iteration
**Severity:** High — entire shift-queue feature is broken once a queued command activates

The foreach iterates entities with `CommandQueueActive`, then calls `em.RemoveComponent<CommandQueueActive>(entity)` directly (lines 36, 43) AND `MoveCommandHelper.Execute(em, entity, ...)` (line 54), which internally calls `em.AddComponent<MoveCommand>`, `em.AddComponent<UserMoveOrder>`, etc. These are direct `EntityManager` structural changes inside an active query iterator — exactly the bug just fixed in `MiningSystem`/`CrystalMiningSystem`. Unity ECS will throw `InvalidOperationException: This query has been invalidated...` on the first queue pop. **Shift-queued waypoints (the entire feature) never fire after the first one without exception.**

**Fix:** Convert to ECB pattern (allocate `EntityCommandBuffer`, defer all structural changes, playback after foreach), matching the recently-fixed mining systems.

### F-6 — **NEW** — `AIScoutingBehavior.cs:285-289` — `ecb.SetComponent<DesiredDestination>` on Scout that lacks the component
**Severity:** High in principle, but currently dead-code-with-a-trap (`[DisableAutoCreation]` per task-052)

`Scout.cs` (lines 28-62) does NOT bake `DesiredDestination` at creation. `AIScoutingBehavior.AssignScoutPatrols` runs whenever `AssignedZoneIndex < 0` (line 244-247) and unconditionally issues `ecb.SetComponent(scoutUnit, new DesiredDestination { ... })` at line 285. ECB.SetComponent on a missing component throws `ArgumentException: Component DesiredDestination not found on entity {n}` at playback. Every fresh AI scout assignment **would** crash unless something else added DesiredDestination to the Scout.

`AIScoutingBehavior` is `[DisableAutoCreation]` per task-052, so this is a trap waiting for someone to re-enable the manager — but **Scout.cs should bake DesiredDestination at creation regardless** (mirror Miner.cs:54-58 fix), since this is the same root cause as task-052 F-7 (Litharchs are paperweights for the same reason).

**Fix:** Bake `DesiredDestination { Has = 0 }` into Scout.cs (and every other unit factory the audit flagged in task-052 F-7).

### F-7 — `MovementSystem.cs:447` interaction with `BlendRadius=2f` — units clip through walls within 2m of goal
**Severity:** Medium — degraded but not eliminated by recent BlendRadius reduction

Within `BlendRadius=2m` of the goal, `FlowFieldLookup.GetDirection` returns the direct-line direction. `StopDistance=0.5f` is much smaller, so the unit is on direct-line steering for the 0.5m..2m approach. If a wall stands between unit and goal in that band (e.g., goal is an iron deposit 1.5m away from the unit, wall in between), the unit walks straight at the wall. The `passGrid.IsPassable(nextCell)` check at line 488 then blocks motion, escalating to tier-3 stuck recovery (line 540-554) which spiral-teleports the unit out. The earlier 6m blend was worse (the recent reduction is correct), but **the bug isn't gone — it's reduced from a 6m clipping window to 2m**. For miners targeting deposits placed adjacent to building footprints, the failure is reproducible.

**Fix:** Either reduce blend radius further (1m or `cellSize`), OR add a per-cell passability check inside `GetDirection`'s blend branch and fall back to flow-field direction if the direct-line cell is blocked.

### F-8 — `PassabilityGrid.cs:39-42, 245-251` — Cell type information (Terrain/Building/Obstacle) is never used
**Severity:** Low (architectural smell, not a bug)

The grid distinguishes four cell types (`Passable=0`, `TerrainBlocked=1`, `BuildingBlocked=2`, `ObstacleBlocked=3`) and `Block*`/`Unblock*` carefully avoid mutating the wrong type to handle priority. But `IsPassable` (the only query used by MovementSystem, BattalionSyncSystem, FlowFieldGenerator) only checks `== Passable`. `GetCell` exists but has zero callers (`grep "\.GetCell\("` returns nothing). The whole 4-state distinction is functionally dead — only the 2-state Passable/Not matters at runtime.

**Fix:** Either ship the type info to consumers (e.g., AI heuristics that prefer paths through obstacle terrain over building terrain) or collapse the enum to a single bit.

### F-9 — `FlowFieldGenerator.cs:175-176` — 64KB+ Persistent allocation per scheduled BFS, never pooled
**Severity:** Medium-low (perf, not correctness)

Each `ScheduleAsync` call allocates `passabilityCopy = new NativeArray<byte>(grid.Width * grid.Height, Allocator.Persistent)` plus a fresh `NativeQueue<int>` for the BFS frontier. With a typical ~256×256 grid that's ~65KB per copy + queue overhead. At `MaxFieldsPerFrame=2` and frequent invalidations (every building placement bumps GridVersion), peak allocation is **~130KB/frame, ~7.7MB/sec at 60fps**, none of which goes through `FlowFieldArrayPool` (which only pools the integration/direction arrays at the same size). On fresh build placement the grid invalidates and units re-request en-masse — every queued request counts against the per-frame budget but the eventually-scheduled BFS still allocates fresh.

**Fix:** Add `_passabilityCopyPool` and `_bfsQueuePool` of `NativeArray<byte>`/`NativeQueue<int>` rented at `_totalCells` size.

### F-10 — `FlowFieldGenerator.Generate` (sync path) is dead code
**Severity:** Low (code hygiene)

The sync `Generate()` API on FlowFieldGenerator (lines 55-103) has zero callers. Only `ScheduleAsync`/`CompleteAsync` is used by FlowFieldManager. The sync path adds 50 lines of code that's been unused since the async pipeline was wired up.

**Fix:** Delete the sync method or actually call it from `MoveCommandHelper`'s pre-warm path for first-frame responsiveness.

---

## Architectural Smells

- **Dual movement representation for battalion members.** Members are stripped of `DesiredDestination`/`SmoothedDirection`/`StuckState` at creation (`BattalionFactory.cs:134-141`), so `MovementSystem` skips them entirely (its query uses `WithNone<BattalionMemberData>`). All member positioning happens via direct `em.SetComponentData<LocalTransform>` calls in `BattalionSyncSystem.cs:447, 522`. Members never participate in MovementSystem's slope-checking, terrain-snap caching, smoothing, or stuck-recovery logic. The duplication of movement code paths between `MovementSystem` (single units) and `BattalionSyncSystem` (members) is structural debt — most movement features need to be implemented twice and drift out of sync.

- **`FlowFieldManager.Update` (managed `MonoBehaviour`) reads `PassabilityGrid.Instance.Cells` from a static-state singleton inside a job-scheduling path.** No fence between `WallGatePassabilitySystem` (which mutates `_cells` via `BlockBuildingRect` etc.) and `FlowFieldManager.Update` (which reads `grid.Cells` to schedule jobs). Both run in the SimulationSystemGroup, but their `[DefaultExecutionOrder]` is set on the GameObjects (`-50` for grid, `-40` for FFM) — that ordering only applies to `MonoBehaviour.Update`, not to `ISystem` updates. The `GridVersion` staleness check handles the consequence, but the snapshot/check race is implicit.

- **Per-unit `MovementCache` is added lazily in `MovementSystem.OnUpdate` via ECB at line 256-265.** Deferred so it doesn't crash, but the first frame after `MoveCommand` runs without any cache; cache fields are only effective from frame 2 onward. Combined with the per-frame `RequestFlowField` cost on a fresh destination, every new movement order pays an extra frame of full-cache-miss work. The Miner-style "bake into the factory" pattern (Miner.cs:54-58) would fix this for all unit types AND avoid the lazy-add ECB plumbing.

- **`PassabilityBuildingSync` polls every 0.5s, `WallGatePassabilitySystem` polls every 0.3s.** Up to 500ms of stale data after a building is placed before passability notices and bumps GridVersion. During that window, units happily path through what is visually a wall. The polling interval is set for managed-singleton-access cost reasons, but a "dirty flag" pushed by `BuildingFactory`/`BuildingConstructionSystem` on placement would close the window without needing a timer at all.

---

## What I Verified Is Fine

- **`MovementSystem.cs:421-445`** — Don't-cache-null fix: confirmed correct.
- **`FlowFieldComponents.cs:223-231`** — `BlendRadius = 2f`: confirmed reduced from 6m. (See F-7 for residual.)
- **`FlowFieldMovementHelper.cs:34`** — `BlendRadius = 2f`: confirmed mirrored on managed path.
- **`GatherCommand.cs:240-241`** — Pre-warm: confirmed `RequestFlowField(nodePos)` called in `SetupGather`.
- **`Miner.cs:54-58`** — `DesiredDestination { Has = 0 }` baked: confirmed.
- **`SelectionSystem.cs:590-614`** — Full hierarchy walk for `EntityReference`: confirmed.
- **`RTSInputManager.cs:1354-1374`** — `RaycastPickEntity` does the same hierarchy walk: confirmed consistent.
- **`MiningSystem.cs:74` / `CrystalMiningSystem.cs:71`** — ECB allocated and used for structural changes during iteration: confirmed.
- **`DeathSystem.cs:83`** — `WithNone<BattalionLeader>` excludes invisible leaders from death detection: confirmed.
- **`BattalionFactory.cs:74`** — Leader gets `DesiredDestination { Has = 0 }` at creation: confirmed.
- **`PassabilityBuildingSync.cs:198-203`** — Calls `FlowFieldManager.InvalidateAll()` when buildings change: confirmed.
- **`WallGatePassabilitySystem.cs:84-90`** — Older missing-braces concern: not present in current code; proper if/else governs Unblock vs Block.

---

## Things I Deliberately Didn't Dig Into

- **A* pathfinding** (`AStarPathStore`/`AStarPathfinder`) — `UseFlowFields = true` by default, A* is fallback. Separate audit.
- **`FormationDragPreview`** drag-preview gesture itself.
- **`PatrolSystem`/`CommandQueueSystem`** waypoint cycling.
- **`ProceduralTerrain.Instance`** dependency timing — bootstrap-order issue, not movement-subsystem proper.
- **Per-unit `MovementCache` archetype fragmentation** from lazy-add.
- **`UnitSeparationSystem` slope-recompute cost** (4× `TerrainUtility.GetHeight` per moved unit at 10Hz).
