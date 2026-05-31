---
deft:
  id: task-ecs-query-caching-hot-systems-089
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: spin-out
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [ecs, performance, refactor]
---

# ECS — cache hot-system EntityQueries in OnCreate (Targeting, FogOfWar)

## Context

Spun out from [task-082 §A rows "TargetingSystem.OnUpdate"](../task-profound-code-review-082/task.md#§A-Architecture--ECS-hygiene)
and the matching FogOfWar row.

Two hot-path systems rebuild their EntityQueries inside `OnUpdate`:

1. [TargetingSystem.cs:68](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68)
   — `SystemAPI.QueryBuilder().WithAll<LocalTransform, FactionTag, Health>().WithNone<BattalionLeader, NodeUntargetable>()`
   built per tick. Snapshots 4 parallel arrays (entities, transforms,
   factions, health) + builds a spatial hash. Targeting cadence is
   every tick; the query archetype is invariant.

2. [FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45)
   — `em.CreateEntityQuery(LineOfSight, LocalTransform, FactionTag, Exclude<CrystalTag>)`
   rebuilt per tick + 4 parallel temp arrays without `using`. Also
   replicates at lines 133 (`FogOfWarSystem.OnUpdate`) and 180
   (`FogVisibilitySyncSystem.OnUpdate`).

Sibling pattern: [BuildingCombatSystem.cs:34](../../../Assets/Scripts/Systems/Combat/BuildingCombatSystem.cs#L34)
does the same query rebuild.

## User Value

Lower per-frame allocation (each rebuild is ~handful of allocs from
the query archetype hash + array snapshots), more predictable jitter
in combat-heavy scenes.

§D performance pass will measure the win before / after.

## Requirements

- R1: Move `EntityQuery` field into the system, resolve via
  `state.GetEntityQuery(...)` in `OnCreate` (for `ISystem`) or
  `GetEntityQuery(...)` in `OnCreate` (for `SystemBase`).
- R2: Reuse the query across ticks in `OnUpdate`.
- R3: Wrap every `ToEntityArray` / `ToComponentDataArray` in `using`
  so accidental early-returns can't leak.
- R4: Apply to `TargetingSystem`, `BuildingCombatSystem`, both
  `FogOfWar` systems, and any peer the §D pass flags.

## File:line anchor (inherited from §A row)

- [TargetingSystem.cs:68](../../../Assets/Scripts/Systems/Combat/TargetingSystem.cs#L68)
- [FogOfWarSystem.cs:45](../../../Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L45)
- [BuildingCombatSystem.cs:34](../../../Assets/Scripts/Systems/Combat/BuildingCombatSystem.cs#L34)

## Out of Scope

- Migrating these systems to `IJobEntity` (separate refactor, scoped
  in §D).
- Spatial-hash redesign (`TargetingSystem`'s cell map is fine; only
  the query rebuild is the concern here).
