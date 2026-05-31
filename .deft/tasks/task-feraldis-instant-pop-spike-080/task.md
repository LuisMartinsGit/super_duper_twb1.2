---
deft:
  id: task-feraldis-instant-pop-spike-080
  type: task
  status: completed
  stage: release
  phase: 1
  total_phases: 1
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feraldis, population, age-up]
---

# Feraldis instant 200 population at age-up

## Context

[docs/Design/Complete.md §5.1 (lines 1599-1613) + §1.3 (lines 126-135)](../../../docs/Design/Complete.md)
specifies Feraldis age-up bumps population cap to 200 instantly (same shape
as Runai's wagon-burst pop spike) — Houses don't contribute pop for
Feraldis (they're raider-spawn buildings).

Runai already has this via `RunaiPopOverride` applied at
[AgeUpSystem.cs:104-115](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L104):

```csharp
em.AddComponent<RunaiPopOverride>(hall);
em.SetComponentData(hall, new RunaiPopOverride { Max = 200 });
```

Feraldis has no equivalent. Hut.cs:53 still tags Houses with
`PopulationProvider`, so a Feraldis player who builds Houses gets pop from
them — also contradicting §5.1 ("Houses do not contribute pop").

## User Value

Feraldis identity is preserved: aggression is sustained by the 200-pop
instant ceiling, not by carefully building Houses; Houses become a pure
raider-spawn structure ([task-066](../task-ageup-transform-hut-066/task.md)
Phase 3) instead of an awkward dual-purpose pop+spawner.

## Requirements

- R1: New `FeraldisPopOverride` ECS component analogous to
  `RunaiPopOverride`.
- R2: At Feraldis age-up,
  [AgeUpSystem.cs](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs)
  stamps `FeraldisPopOverride.Max = 200` on the Hall, mirroring Runai's
  path.
- R3: When a Hut is built by a Feraldis-cultured faction, the
  `PopulationProvider` component is **not** added (or is set to amount 0).
  Faction culture check in `BuildingFactory.CreateHut` (or the relevant
  factory branch).
- R4: `PopulationSyncSystem` already supports `RunaiPopOverride`; extend
  to handle `FeraldisPopOverride` identically.

## Acceptance Criteria

- [x] Aging up as Feraldis sets the population cap to 200 within one tick.
- [x] Houses built by a Feraldis faction do not raise the population cap.
- [x] Aging up as Alanthor still uses the standard per-House population
      ladder (no regression).

## Implementation Phases

### Phase 1: Component + age-up hook + house-pop gate
**Files:** new `Core/Components/FeraldisPopOverride.cs` (or extend the
existing override),
[AgeUpSystem.cs](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs),
[PopulationSyncSystem.cs](../../../Assets/Scripts/Economy/PopulationSyncSystem.cs),
[Hut.cs](../../../Assets/Scripts/Entities/Buildings/Hut.cs) or
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs).
**Verification:** Skirmish: age up each culture; observe pop cap in HUD.
**Estimated effort:** Small

## Dependencies

- [task-066 ageup-transform-hut](../task-ageup-transform-hut-066/task.md)
  Phase 3 (House → Raider auto-spawn — separate concern; this task just
  removes the pop contribution).

## Out of Scope

- Feraldis House → Raider auto-spawn behavior (task-066).
- L2 / L3 unit tiers (task-079).
