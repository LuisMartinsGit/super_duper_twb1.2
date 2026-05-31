---
deft:
  id: task-caravan-loot-feraldis-only-075
  type: bug
  status: completed
  stage: release
  phase: 1
  total_phases: 1
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feraldis, caravan, economy, gate]
---

# Caravan-kill loot: gate to Feraldis killers only

## Context

[docs/Design/Complete.md §1.9 (lines 272-293)](../../../docs/Design/Complete.md):

> When a Runai Caravan dies to a Feraldis killer, the killer's faction
> receives 50 % of the cargo as Glow + Supplies. Cargo killed by Alanthor,
> Runai (friendly fire), or the Crystal-Curse is destroyed.

Code at [CaravanDeathSystem.cs:47-66](../../../Assets/Scripts/Systems/Combat/CaravanDeathSystem.cs#L47):

```csharp
Faction killerFaction = lastDamager.ValueRO.Value;
FactionEconomy.Add(em, killerFaction, Cost.Of(supplies: lootSupplies, crystal: lootCrystal));
```

Every killer faction is awarded the loot regardless of culture. The correct
pattern already exists at
[PillageSystem.cs](../../../Assets/Scripts/Systems/Combat/PillageSystem.cs)
which gates on `Cultures.Feraldis`.

## User Value

Feraldis's identity-defining "caravan kills feed our economy" loop becomes
actually exclusive to Feraldis; Alanthor and Runai stop accidentally
benefiting from incidental caravan interceptions.

## Requirements

- R1: Gate the caravan-death cargo award on
  `FactionColors.GetFactionCulture(killerFaction) == Cultures.Feraldis`.
- R2: When the killer is non-Feraldis (including Crystal-Curse PvE), the
  cargo is destroyed; no resource transfer to any party.
- R3: Hook into [task-070](../task-veilsteel-frenzy-070/task.md) so that a
  Feraldis caravan-kill also grants +1 Veilsteel shaving to the killing
  unit.

## Acceptance Criteria

- [x] Killing a Runai caravan as Feraldis credits the documented 50 % cargo
      split as Supplies + Crystal (matching prior behavior).
- [x] Killing a Runai caravan as Alanthor or Runai produces 0 resource gain.
- [x] Crystal-curse creature kills (no faction owner) destroy the cargo.

## Implementation Phases

### Phase 1: Gate the award
**Files:** [CaravanDeathSystem.cs](../../../Assets/Scripts/Systems/Combat/CaravanDeathSystem.cs).
**Verification:** Run skirmish, kill caravans as each culture, observe
EntityActionPanel resource readout pre / post.
**Estimated effort:** Small

## Dependencies

- [task-070 veilsteel-frenzy](../task-veilsteel-frenzy-070/task.md) shares
  the on-kill drop infrastructure (the Veilsteel shaving award is added
  alongside the loot in the same gated block).

## Out of Scope

- Pillage-system parallels — those are already correctly gated and don't
  need this task.
