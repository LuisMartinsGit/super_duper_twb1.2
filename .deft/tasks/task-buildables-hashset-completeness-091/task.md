---
deft:
  id: task-buildables-hashset-completeness-091
  type: bug
  status: ready
  stage: scope
  phase: 0
  total_phases: 0
  priority: high
  source: task-profound-code-review-082
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [ui, building, hashset, drift]
---

# `BuildableBuildings` HashSet completeness — Crucible / VeilsteelFoundry / Fiend Foundry

## Context

Spun out from [task-082 §B.4 / §B.5 / §B.6](../task-profound-code-review-082/task.md#B-codevsdesign-drift).
The `BuildableBuildings` HashSet at
[EntityExtractors.cs:700](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L700)
gates which buildings appear in the build-action panel. Several Age 1
unique buildings have full `BuildingFactory.Create` branches **and** entries
in `BuildCosts.cs` and `TechTree.json` — but are missing from the HashSet,
so the build button never appears for the player.

Affected ids:

- `Alanthor_Crucible` ([BuildingFactory.cs:60](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L60), [BuildCosts.cs:70](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L70))
- `Runai_VeilsteelFoundry` ([BuildingFactory.cs:54](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L54), [BuildCosts.cs:50](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L50))
- `Feraldis_Foundry` (Fiend Foundry) ([BuildingFactory.cs:67](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L67), [BuildCosts.cs:56](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs#L56))

(`KingsCourt` and `Alanthor_RoyalStable` are also missing but covered by
task-068 / task-071 — out of scope here.)

## User Value

Players can construct the Veilsteel-producing buildings their culture
depends on. Right now there is no UI path to build them, so Veilsteel-tier
content is unreachable.

## Requirements

- R1: Add `Alanthor_Crucible`, `Runai_VeilsteelFoundry`, `Feraldis_Foundry`
  to the `BuildableBuildings` HashSet.
- R2: Verify the culture-gating filter (`GetRequiredCulture`) routes each id
  to the correct culture so cross-culture players don't see the wrong
  faction's foundry.
- R3: Verify era gating — these are Age 1 buildings, so the `minEra` check
  should hide them in Age 0.

## Acceptance Criteria

- [ ] All three building ids appear in `BuildableBuildings`.
- [ ] In a sandbox match, after picking the appropriate culture and reaching
      Era 2, the build button for the foundry is visible and clickable.
- [ ] Picking the wrong culture (e.g. Runai) does NOT show the Alanthor
      Crucible in the build panel.

## Technical Notes

- One-line change to a HashSet plus a culture-gating sanity check.
- Inherits file:line anchor from [task-082 §B.4](../task-profound-code-review-082/task.md#B-codevsdesign-drift).
