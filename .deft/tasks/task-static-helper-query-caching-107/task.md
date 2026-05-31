---
deft:
  id: task-static-helper-query-caching-107
  type: improvement
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: parent-task-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Static system helpers — cache EntityQuery or fold back into the SystemBase

Spun out from [task-082 §D row 10](../task-profound-code-review-082/task.md#§d-performance-hot-spots).

## Context

Several systems expose `private static` helpers that call
`em.CreateEntityQuery(...)` afresh on every invocation, because they
have no SystemBase instance to cache against. Two confirmed cases:

1. **`TrainingSystem.FindFactionDropOff`** at
   [TrainingSystem.cs:334-360](../../../Assets/Scripts/Systems/Training/TrainingSystem.cs#L334)
   — creates a `GathererHutTag, FactionTag` query AND a `HallTag,
   FactionTag` query per call. Reached when a freshly trained gatherer
   needs a drop-off site (rally-issued gather command).
2. **`AgeUpSystem.HasFactionTemple`** at
   [AgeUpSystem.cs:145-160](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L145)
   — creates a `TempleTag, FactionTag` query per call. Reached during
   age-up checks (every faction × every age-up tick).

Both helpers compile down to `EntityManager.CreateEntityQuery(...)` ⇒
`ToEntityArray(Allocator.Temp)` ⇒ linear scan ⇒ dispose. The query
handle leak isn't catastrophic (Unity tracks handles per-world) but it
defeats the entity-archetype matching cache and wastes the per-tick
"first call after archetype change" check.

The same anti-pattern likely exists in a handful of other
`private static` helpers across `Systems/` — pattern grep:
`em\.CreateEntityQuery` in a static method body.

## User Value

Eliminates redundant per-call query construction overhead in
infrequently-called but performance-sensitive helpers. Frees up the
archetype-match cache for the queries that DO need to thrash.

## Requirements

- R1: For `TrainingSystem.FindFactionDropOff`: either fold the helper
  into the `TrainingSystem` SystemBase (so it can hold cached
  `_ghDropoffQuery` / `_hallDropoffQuery` fields) OR convert the helper
  to an `EntityQuery` parameter that the caller passes in (caller has
  the cached query).
- R2: For `AgeUpSystem.HasFactionTemple`: same fix shape — cache the
  `TempleTag, FactionTag` query on the AgeUpSystem.
- R3: Audit all `private static` methods under `Assets/Scripts/Systems/`
  that call `em.CreateEntityQuery` and apply the same fix where the
  caller invokes the helper more than 1× per tick (single-shot helpers
  on startup are fine).
- R4: No behaviour change. Drop-off targeting and age-up gating logic
  must produce identical output to current code.

## Acceptance Criteria

- [ ] `TrainingSystem.FindFactionDropOff` no longer creates fresh
      `EntityQuery` handles on each call.
- [ ] `AgeUpSystem.HasFactionTemple` no longer creates a fresh
      `EntityQuery` handle on each call.
- [ ] `grep -r 'private static.*CreateEntityQuery' Assets/Scripts/Systems`
      returns zero per-call constructions in helpers called > 1× per tick.
- [ ] No regression: gatherer rally drop-off still resolves
      `GathererHut > Hall` priority; faction age-up still gates on
      Temple presence.

## Files to touch

- `Assets/Scripts/Systems/Training/TrainingSystem.cs`
- `Assets/Scripts/Systems/Work/AgeUpSystem.cs`
- Possibly other `Systems/*` files surfaced by the audit pass

## Out of Scope

- Other ECS query caching in `OnUpdate` paths — that's **task-089**
  (`ecs-query-caching-hot-systems`) which covers
  `TargetingSystem` / `FogOfWarSystem` / `BuildingCombatSystem` /
  `FeraldisRaiderPatrolSystem` etc.
- HudBridge query consolidation — covered by **task-090**.
- Helper unification into a `EntityHelpers` static class — purely
  cosmetic, not in scope.
