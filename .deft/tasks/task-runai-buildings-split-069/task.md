---
deft:
  id: task-runai-buildings-split-069
  type: task
  status: active
  stage: scope
  phase: 0
  total_phases: 4
  priority: critical
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [runai, building, large, bazaar, grazing-grounds]
---

# Runai buildings: Bazaar/Hall split, Grazing Grounds, retire Vault + PackBazaar

## Context

The Runai building roster in code conflates three separate concerns from
[docs/Design/Complete.md Part IV](../../../docs/Design/Complete.md):

1. **Trader's Hall vs Thessara's Bazaar** are conflated.
   [TechTree.json:527-556](../../../Assets/Resources/TechTree.json#L527)
   defines `ThessarasBazaar` as the main Hall (role: "Mobile trade HQ / Trains
   light military", trains Spearman / Skirmisher / Raider, has `PackAndMove`
   ability). Doc §4.4 (line 1341) says Thessara's Bazaar is a **separate
   trade-research-only building**; the main Hall renames to **Trader's Hall**
   and only trains Worker + Scout.
2. **Grazing Grounds** (new cavalry trainer per doc §4.3 lines 1297-1324) is
   missing entirely — no BuildType, factory, TechTree entry, or Tag.
   `Runai_Raider` references it as `trainAt: "Grazing Grounds"` but the
   building doesn't exist.
3. **Runai_Vault** ([TechTree.json:638-660](../../../Assets/Resources/TechTree.json#L638),
   [BuildingFactory.cs:1130-1162](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L1130))
   is **retired** per doc §4.4 (line 1384) — Vault of Almiérra is Runai's
   only bank.
4. **Runai_PackBazaar tech** + the **`PackAndMove`** ability on
   ThessarasBazaar are retired per doc §4.4 (line 1362, decision #8).
   [BazaarPackSystem.cs](../../../Assets/Scripts/Systems/Work/BazaarPackSystem.cs)
   still fully implements pack/unpack.

## User Value

Runai's economy tree becomes coherent: an identity Hall that's not also a
unit factory, a trade-tech hub that's not also a Hall, a cavalry trainer that
actually exists, and a clean banking story (single Vault, no PackAndMove
hangover).

## Requirements

- R1: Add a new `TradersHall` building (cultured rename of Hall — see
  [task-071](../task-cultured-rename-layer-071/task.md) for the rename
  machinery). Trains only `Worker` and `Scout`. Provides 10 pop.
- R2: Refactor `ThessarasBazaar` into a **separate, secondary** building.
  Trains nothing. Hosts trade-lane research only: Tariffs, Escorted Caravans.
  Per doc §4.4: HP TBD, build cost ≈ 350 S + 80 I + 40 C.
- R3: Add new `Runai_GrazingGrounds` building. Trains `Runai_Raider` (L1),
  with L2 + L3 cavalry tier slots
  ([task-079](../task-age1-unit-tier-ladders-079/task.md) fills these).
- R4: Delete `Runai_Vault` end-to-end: TechTree entry, BuildingFactory cases
  (EM + ECB), associated tag, AI build-order references.
- R5: Delete `Runai_PackBazaar` tech (TechTree.json) + remove `PackAndMove`
  from ThessarasBazaar abilities + delete `BazaarPackSystem.cs` +
  `BazaarWagon.cs` (if not consumed by [task-066 Phase 2](../task-ageup-transform-hut-066/task.md)
  for the Runai wagon-burst).

## Acceptance Criteria

- [ ] A Runai faction post-age-up has a "Trader's Hall" entity (renamed
      from Hall) that trains only Worker + Scout buttons.
- [ ] Thessara's Bazaar is a buildable secondary structure with research
      tiles only (no train buttons).
- [ ] Grazing Grounds is buildable; clicking spawns the L1 Raider trainer
      with Runai_Raider in its queue.
- [ ] `Runai_Vault` does not appear in any UI surface or AI build order;
      attempting to spawn one fails with "unknown building id".
- [ ] `Runai_PackBazaar` tech does not appear in the research panel for
      ThessarasBazaar; the `Pack` ability button is gone.

## Implementation Phases

### Phase 1: Split ThessarasBazaar from Trader's Hall
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json) (move
training out of `ThessarasBazaar`; add `TradersHall` as the cultured-Hall
entry); [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs);
[BuildingComponents.cs](../../../Assets/Scripts/Core/Components/BuildingComponents.cs)
(new `TradersHallTag`).
**Estimated effort:** Medium

### Phase 2: Add Grazing Grounds
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs),
[BuildingComponents.cs](../../../Assets/Scripts/Core/Components/BuildingComponents.cs),
[EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs)
(BuildableBuildings).
**Estimated effort:** Medium

### Phase 3: Retire Runai_Vault
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
[BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs)
(remove `CreateRunaiVault` + `CreateRunaiVaultECB`), AI managers, web HUD
Actions.jsx.
**Estimated effort:** Small

### Phase 4: Retire PackBazaar tech + PackAndMove
**Files:** [TechTree.json](../../../Assets/Resources/TechTree.json),
delete [BazaarPackSystem.cs](../../../Assets/Scripts/Systems/Work/BazaarPackSystem.cs)
(unless consumed by task-066), delete [BazaarWagon.cs](../../../Assets/Scripts/Entities/Units/BazaarWagon.cs)
(same caveat).
**Estimated effort:** Small

## Dependencies

- [task-071 cultured-rename-layer](../task-cultured-rename-layer-071/task.md)
  provides the Hall→Trader's Hall rename plumbing.
- [task-066 ageup-transform-hut](../task-ageup-transform-hut-066/task.md)
  Phase 2 may reuse parts of BazaarWagon.cs for the Runai wagon-burst — co-
  ordinate before deletion in Phase 4.
- [task-079 age1-unit-tier-ladders](../task-age1-unit-tier-ladders-079/task.md)
  fills the L2 / L3 cavalry tiers at Grazing Grounds.

## Out of Scope

- Trader-warrior auto-patrols — [task-072](../task-trader-warriors-072/task.md).
