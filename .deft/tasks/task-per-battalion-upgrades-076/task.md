---
deft:
  id: task-per-battalion-upgrades-076
  type: task
  status: active
  stage: scope
  phase: 0
  total_phases: 3
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [combat, battalion, upgrade, large]
---

# Per-battalion military upgrades (replace faction-wide tier application)

## Context

[docs/Design/Complete.md §1.6 (lines 186-215)](../../../docs/Design/Complete.md)
specifies per-battalion upgrades:

> Each trained battalion has its own Upgrade button + cost. Mid-stream
> battalions stay at the old tier; only newly-purchased battalions and the
> ones the player explicitly upgrades change.

Code at [EquipmentTierSystem.cs:64-91](../../../Assets/Scripts/Systems/Combat/EquipmentTierSystem.cs#L64)
applies tier diffs **faction-wide**: researching Iron-tier instantly upgrades
every existing Spearman in the faction. The doc rejects this model.

## User Value

Military upgrades become tactical decisions instead of automatic
windfalls: invest in upgrading the elite battalion at the front, leave the
green garrison battalion cheap. Battalion identity sticks.

## Requirements

- R1: Tier state moves from `FactionEquipmentTier[class]` to a per-battalion
  `BattalionEquipmentTier` component on the `BattalionLeader` entity.
- R2: At train time, the new battalion inherits the *current researched
  tier* of its faction for its class (so research still unlocks the ceiling).
- R3: An "Upgrade" command targets a specific battalion and bumps its
  `BattalionEquipmentTier` by 1 step at the per-battalion resource cost.
- R4: `EquipmentTierSystem` reads the per-battalion field instead of the
  per-faction field; mid-stream battalions stay at whatever tier they were
  trained at.
- R5: UI: a battalion-selected action panel surfaces an "Upgrade" button +
  cost matching the doc's table (TBD per design pass; structural for now).

## Acceptance Criteria

- [ ] Researching Iron-tier and then training a Spearman battalion produces
      a battalion whose stats reflect the Iron tier.
- [ ] Pre-existing Spearman battalions do **not** automatically upgrade.
- [ ] Clicking Upgrade on a selected battalion deducts the per-battalion
      cost and bumps that battalion's stats one tier.
- [ ] Two battalions of the same class at different tiers have visibly
      different stats (HP / damage) when inspected.

## Implementation Phases

### Phase 1: Per-battalion tier component
**Files:** new `Core/Components/BattalionEquipmentTier.cs`,
[BattalionComponents.cs](../../../Assets/Scripts/Core/Components/BattalionComponents.cs),
[BattalionFactory.cs](../../../Assets/Scripts/Entities/Units/BattalionFactory.cs).
**Estimated effort:** Medium

### Phase 2: System rewire
**Files:** [EquipmentTierSystem.cs](../../../Assets/Scripts/Systems/Combat/EquipmentTierSystem.cs)
(read per-battalion, fall back to per-faction during migration).
**Estimated effort:** Medium

### Phase 3: Upgrade command + UI
**Files:** new `Core/Commands/CommandTypes/UpgradeBattalionCommand.cs`,
[CommandRouter.cs](../../../Assets/Scripts/Core/Commands/CommandRouter.cs),
[EntityActionPanel.cs](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs),
web HUD `Actions.jsx`.
**Estimated effort:** Large

## Edge Cases

- Battalion losses replaced by `ReplaceLostUnits` system: the replacement
  units must inherit the battalion's tier, not the faction's.
- Save migration: existing saves should default each battalion's tier to its
  faction's current researched tier at load time.

## Out of Scope

- Per-class battalion size (infantry 5 / cavalry 3 / siege 1) — flagged in
  the audit but a separate concern.
