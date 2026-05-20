---
deft:
  id: task-veilsteel-frenzy-070
  type: task
  status: active
  stage: implementation
  phase: 1
  total_phases: 3
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feraldis, economy, veilsteel, carry]
---

# Veilsteel Frenzy — Feraldis-only kill-carry attack bonus

## Context

[docs/Design/Complete.md §1.7 (lines 216-237)](../../../docs/Design/Complete.md)
specifies Feraldis-only resource carry: military units pick up Veilsteel
shavings from kills (up to 5 slots), each shaving granting +2 % attack bonus
that stacks to +10 %. Researched at War Hall as "Veilsteel Frenzy".

Code state:
- [TechTree.json:1225-1231](../../../Assets/Resources/TechTree.json#L1225)
  defines `Feraldis_IronFury` with text "Units can carry up to 5 Iron"
  (wrong resource — should be Veilsteel — and wrong tech name).
- No `VeilsteelCarry` component anywhere.
- No system applies the per-shaving attack bonus.
- No `researchAt: WarHall` host pointer.
- Iron-carry mechanic (retired per doc) is also not implemented, so retiring
  it is a no-op.

## User Value

Feraldis gets its identity-defining late-game scaling — military units that
get measurably stronger as they kill more, with a clear floor and ceiling.
Also closes the cross-link with §1.9 (caravan kills feed Feraldis) by
sharing the on-kill drop infrastructure.

## Requirements

- R1: Rename `Feraldis_IronFury` → `Feraldis_VeilsteelFrenzy` (with id
  migration alias for save compatibility) in TechTree.json. Description
  updated to "Units carry Veilsteel shavings from kills (max 5, +2 % attack
  each)".
- R2: New `VeilsteelCarry` ECS component: `byte Shavings` (0-5).
- R3: New `VeilsteelKillDropSystem`: on a Feraldis military unit kill, the
  killer gains +1 Shaving (clamped at 5).
- R4: `EquipmentTierSystem` or a new sibling reads `VeilsteelCarry.Shavings`
  and applies +2 % attack per shaving to the unit's `AttackDamage` (or a
  damage multiplier, whichever matches the existing tier-bonus pattern).
- R5: Tech `Feraldis_VeilsteelFrenzy` is gated on `researchAt: WarHall`.
  Until researched, the carry component exists but the attack-bonus system
  is inert.
- R6: Cross-link: when a Runai caravan dies to a Feraldis killer (see
  [task-075](../task-caravan-loot-feraldis-only-075/task.md)), grant +1
  Shaving to the killer in addition to the loot.

## Acceptance Criteria

- [ ] Research panel at War Hall shows "Veilsteel Frenzy" (not "Iron Fury").
- [ ] A Feraldis Berserker with 0 shavings deals base damage; after killing
      5 enemies and post-research, deals +10 % damage.
- [ ] Non-Feraldis units never accumulate shavings even after the tech is
      researched on their own faction (faction-gate enforced).
- [ ] Killing a Runai caravan as Feraldis grants +1 shaving (and the
      caravan loot from task-075).

## Implementation Phases

### Phase 1: Tech rename + component
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
new `Core/Components/VeilsteelCarryComponent.cs`,
[TechTreeDB.cs](../../../Assets/Scripts/Data/TechTree/TechTreeDB.cs) (id alias).
**Estimated effort:** Small

### Phase 2: Drop + apply systems
**Files:** new `Systems/Combat/VeilsteelKillDropSystem.cs`,
new or extend `Systems/Combat/VeilsteelAttackBonusSystem.cs`,
[ResearchSystem.cs](../../../Assets/Scripts/Systems/Research/ResearchSystem.cs).
**Estimated effort:** Medium

### Phase 3: War Hall research gate + UI surface
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json) (researchAt),
HudBridge research dispatch, web HUD `Actions.jsx`.
**Estimated effort:** Small

## Dependencies

- [task-075 caravan-loot-feraldis-only](../task-caravan-loot-feraldis-only-075/task.md)
  shares the on-kill reward path.
- [task-071 cultured-rename-layer](../task-cultured-rename-layer-071/task.md)
  defines the War Hall identity that hosts the research.

## Out of Scope

- HUD visual for the carry stack (icon row on unit selection) — flag for art
  pass.
