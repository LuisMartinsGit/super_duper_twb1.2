---
deft:
  id: task-ageup-transform-hut-066
  type: task
  status: active
  stage: implementation
  phase: 3
  total_phases: 3
  priority: critical
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [age-up, gatherers-hut, alanthor, runai, feraldis, large]
---

# Age-up: transform Gatherer's Hut in place per culture (no self-destruct)

## Context

[docs/Design/Complete.md §1.4](../../../docs/Design/Complete.md) ("Age-up:
transform, don't replace") and the per-culture Age 1 sections all hinge on a
single mechanic: when a faction ages up, every existing Gatherer's Hut on the
map transforms **in place** into a culture-specific successor:

- **Alanthor** → wall-segment anchor that auto-fortifies a small radius of
  walls around itself (free seed ring of compartments).
- **Runai** → deployable caravan-wagon that grants full income while in
  transit, with linear 4-min decay to a settled rate (the "wagon-burst" power
  spike).
- **Feraldis** → persists as a normal building, **plus** auto-spawns
  uncontrollable Raider units on every build and upgrade tick (raider
  spawn-house).

Current code in
[AgeUpSystem.cs:97-115](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L97)
does the opposite: Alanthor and Runai huts get a 2-minute **self-destruct
timer** (`StartGathererHutSelfDestruct` / `StartHutSelfDestruct`); Feraldis
huts just sit there. No wall-anchor entity, no wagon spawn, no decay timer, no
Raider auto-spawn. Three culture-defining power spikes are broken.

## User Value

Age-up becomes the dramatic identity moment the doc promises. Alanthor wakes
up to a pre-built defensive perimeter; Runai gets a wagon-fleet that lets it
rip income from anywhere on the map for four minutes; Feraldis gains a hand
of disposable harasser-spawners that pressure the neighbouring economy.

## Requirements

- R1: At Alanthor age-up, every faction-owned Gatherer's Hut is converted
  in place into a wall-segment anchor entity that auto-builds a small ring of
  walls around its tile. HP is preserved; the ladder resets to L1.
- R2: At Runai age-up, every faction-owned Gatherer's Hut is converted in
  place into a controllable Caravan unit (the "wagon-burst") that emits full
  pre-conversion income while moving and decays linearly to 0 over 4 minutes.
  When the wagon is unpacked at a Trade Post site, the income becomes
  settled.
- R3: At Feraldis age-up, the Gatherer's Hut persists with full HP and
  income, and gains a tag that causes every build / upgrade tick on a
  Feraldis House to spawn `Feraldis_Raider` units (uncontrollable, see
  [task-067](../task-feraldis-raider-rebuild-067/task.md)).
- R4: The self-destruct paths in [AgeUpSystem.cs:97-115](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs#L97)
  are removed; no hut is destroyed by age-up.

## Acceptance Criteria

- [ ] Alanthor faction with N Gatherer's Huts at age-up has N wall-anchor
      entities + the auto-fortified ring is visible on the map within 2 s of
      the culture pick resolving.
- [ ] Runai faction with N huts at age-up has N controllable Caravan units;
      moving one gives the full Supplies + Crystal income from the hut; the
      income tapers to 0 over 4 min; unpacking at a Trade Post site stops the
      decay and produces a stationary Trade Post.
- [ ] Feraldis faction's Gatherer's Huts are still on the map after age-up
      and still emitting income; building or upgrading a Feraldis House
      spawns N Raiders (N = 1 / 2 / 3 for L1 / L2 / L3).
- [ ] `StartGathererHutSelfDestruct` and `StartHutSelfDestruct` are removed
      from the codebase.

## Implementation Phases

### Phase 1: Remove self-destruct + add culture switch
**Scope:** Delete the self-destruct paths; introduce a culture-keyed
transform dispatch in `AgeUpSystem` that calls into three new strategy
classes (no-op stubs at first).
**Files:** [AgeUpSystem.cs](../../../Assets/Scripts/Systems/Work/AgeUpSystem.cs).
**Verification:** Aging up any culture leaves the huts standing for at least
30 s (no auto-destroy).
**Estimated effort:** Small

### Phase 2: Alanthor wall-anchor + Runai wagon-burst
**Scope:** Implement the Alanthor wall-anchor conversion (spawn a hub +
N segments around the hut tile; consume the hut entity) and the Runai
caravan-wagon conversion (spawn a Caravan with income-decay component;
consume the hut). New components: `WallAnchorTag`, `WagonIncomeDecay`.
**Files:** new `Systems/AgeUp/AlanthorWallAnchorSystem.cs`,
`Systems/AgeUp/RunaiWagonBurstSystem.cs`,
[Caravan.cs](../../../Assets/Scripts/Entities/Units/Caravan.cs) (income hook),
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs).
**Estimated effort:** Large

### Phase 3: Feraldis persistence + House → Raider auto-spawn
**Scope:** Mark Feraldis Gatherer's Huts as persistent; gate the persistence
on `FactionProgress.Culture == Cultures.Feraldis`. Add a `RaiderSpawner`
component on Feraldis Houses driven by build / upgrade events. Depends on
[task-067](../task-feraldis-raider-rebuild-067/task.md) for the
Raider-as-Feraldis unit.
**Files:** [GatherersHut.cs](../../../Assets/Scripts/Entities/Buildings/GatherersHut.cs),
new `Systems/AgeUp/FeraldisRaiderSpawnSystem.cs`,
[Hut.cs](../../../Assets/Scripts/Entities/Buildings/Hut.cs).
**Estimated effort:** Medium

## Dependencies

- [task-067 feraldis-raider-rebuild](../task-feraldis-raider-rebuild-067/task.md)
  must land before Phase 3 (Raider must be a Feraldis unit type, not the
  Runai cavalry it currently aliases to).
- [task-069 runai-buildings-split](../task-runai-buildings-split-069/task.md)
  defines the Trade Post unpack destination for Phase 2's Runai wagon.

## Edge Cases

- Hut at low HP at age-up: preserve HP on the wall-anchor / wagon (no free
  heal).
- Hut under attack at age-up: cancel transform or carry the damage state to
  the successor entity.
- More huts than wall ring tiles support: cap ring radius per anchor.
- Wagon destroyed mid-decay: no salvage; income simply ends.

## Out of Scope

- Trader-warrior lane patrols spawned by Runai Trade Hubs — owned by
  [task-072](../task-trader-warriors-072/task.md).
- Detailed combat balance of Raiders — owned by
  [task-067](../task-feraldis-raider-rebuild-067/task.md).
