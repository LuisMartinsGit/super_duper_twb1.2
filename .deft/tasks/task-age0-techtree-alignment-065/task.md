---
deft:
  id: task-age0-techtree-alignment-065
  type: task
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Align Age 0 implementation with the new Age 0 tech-tree design doc

## Context

The canonical Age 0 design lives at [docs/Design/Age_0.md](../../../docs/Design/Age_0.md)
(authored 2026-05-19). It supersedes the values currently in
[TechTree.json](../../../Assets/Resources/TechTree.json),
[BuildingUpgradeConfig.cs](../../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs),
[BuildCosts.cs](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs),
[VaultInterestSystem.cs](../../../Assets/Scripts/Systems/Economy/VaultInterestSystem.cs),
and the various Unit/Building factory files. Several Age 0 features in the doc
have no code support yet (most archery / shrine / vault / keep techs, the
unified Worker, the Shrine aura, culture modifiers on the choice buildings).

This task captures the alignment work as a discrete backlog so it can be
phased and reviewed.

## User Value

Players get the Age 0 they read about: balanced costs, a unified Worker, the
named techs (Stone tools / Wheel cart / Conscription / Stone weapons /
Choreographed volleys / Fletching / banking tiers / masses tiers / siege
emplacements), and the special buildings behaving per culture.

## Requirements

- R1: All Age 0 unit stats (HP, speed, train time, cost, attack range, pop)
  match the doc. Currently mismatched: Spearman (Swordsman renamed + attack
  range 1.0 → 1.5), Worker (Builder + Miner merge), Litharch (no functional
  delta — keep stats), Scout (no stat delta, but UI re-parent to Hall).
- R2: Out of scope for Age 0 — the existing `BuildingUpgradeConfig` L0→L3
  entries for Hall / Barracks / Archery Range / Hut are **post-age-up
  cultured-form data**. Age 0 only exposes the pre-culture lvl 0 form;
  the cultured rename + L1/L2/L3 upgrade ladder is Age 1+ work (separate
  doc / task). No code change required here; verify the upgrade button is
  *hidden* on these buildings while the faction is still in Age 0.
- R3: Vault / Shrine / Fiendstone Keep gain upgrade entries L2/L3 in
  `BuildingUpgradeConfig.TryGetCost` (none today).
- R4: Vault interest rate becomes **25 % per minute** at L1, 50 / 75 / 100 %
  at the three banking tiers. Culture modifier ±30 % applied.
- R5: Shrine of Ridan gains an aura heal (10 u radius, 1 s ticks, 1 % Max HP
  at L1 → 3 / 6 / 15 % via Heightened / Pious / Fervored masses tech). Culture
  modifier ±30 %.
- R6: Fiendstone Keep HP / arrow-count culture modifier (Feraldis +50 % /
  Alanthor −50 %) and the four emplacement / wall techs.
- R7: Archery Range gains three techs (Choreographed volleys active skill,
  Stone-tipped arrows tier-1 unit upgrade, Fletching +15 % range).
- R8: Barracks tech rename + behavior swap: `BasicDrills` → `Conscription`
  (+20 % train speed at Barracks), `WoodenArmor` → `Stone weapons` (unlock
  unit upgrade 1).
- R9: Hall tech rename: `ImprovedTools` → `Stone tools` (effect unchanged),
  `StorageCarts` → `Wheel cart` (+5 carry, currently +10 — reduce).
- R10: Scout training button moves from Barracks to Hall.

## Acceptance Criteria

- [ ] The Upgrade button on Hall / Barracks / Archery Range / House is
      **hidden / disabled** while the player is still in Age 0 (lvl 0 form);
      becomes available only after age-up resolves the cultured rename.
- [ ] Feraldis age-up removes (or never spawns) the House entity entirely —
      pop comes from Longhouse + batch training.
- [ ] Gatherer's Hut at age-up **transforms in place** per culture (no
      auto-despawn / refund): Alanthor → wall-segment anchor that
      auto-fortifies a small radius; Runai → mobile caravan-wagon
      deployable to plant a Trade Post (full income while in transit);
      Feraldis → persists as a building AND spawns auto-patrolling raider
      units. Crystal-Curse creatures / nodes count as damage targets for
      Feraldis income (cold-start floor). See [docs/Design/Overview.md](../../../docs/Design/Overview.md) and per-faction Age 1 docs for the full mechanic.
      *(This is its own substantial implementation chunk — likely a
      sibling task; see Out of Scope.)*
- [ ] Spearman exists (or Swordsman is renamed) with the doc's stats; all UI
      buttons, AI build orders, and EntityExtractors mappings updated.
- [ ] Worker unit replaces separate Builder + Miner; AI and player
      behaviors (auto-find on depletion within LOS, auto-chain to nearby
      builds, explicit gather command for player workers) preserved.
- [ ] Vault interest = 25 %/min at L1, banking tiers configurable; culture
      yield ±30 % applied.
- [ ] Shrine of Ridan emits a heal aura (rate per tier), culture ±30 %
      applied, +1 RP on build (+1 more if player picks Runai at age-up).
- [ ] Fiendstone Keep HP and arrow auto-fire scaled per culture; four techs
      implemented (Ballista / Trebuchet / Additional Towers / Reinforced walls).
- [ ] All renamed techs show the new name in `TechTreePanel` and use the new
      effect numbers.
- [ ] BuildingUpgradeConfig entries exist for Vault, Shrine, Keep at L2/L3.
- [ ] Doc's "Open design questions" 1–5 resolved (numbers picked, code
      matches).

## Implementation Phases

### Phase 1: Hall economy techs — rename + Wheel cart carry value
**Scope:** Rename `ImprovedTools` → `Stone tools`, `StorageCarts` → `Wheel
cart`; reduce Wheel cart carry bonus from +10 → +5.
**Files:**
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json) (name + effect)
- [Assets/Scripts/Data/TechTree/TechTreeDB.cs](../../../Assets/Scripts/Data/TechTree/TechTreeDB.cs) (parse names if hard-coded)
- [Assets/Scripts/UI/Panels/TechTreePanel.cs](../../../Assets/Scripts/UI/Panels/TechTreePanel.cs) (display label)
- [Assets/Scripts/Core/Components/UnitComponents.cs](../../../Assets/Scripts/Core/Components/UnitComponents.cs) (StorageCarts effect comment)
**Verification:**
- [ ] Researching Wheel cart raises carry capacity by 5 (not 10).
- [ ] Stone tools effect unchanged (×1.15 gather speed).
**Estimated effort:** Small

### Phase 2: Worker unit (merge Builder + Miner)
**Scope:** Introduce `Worker` with HP 70 / speed 6 / buildSpeed 1 /
gatherSpeed 1 / carry 1. Map old `Builder` + `Miner` ids to the new entity
where needed; preserve behaviors documented in [CLAUDE.md "Key Design Decisions"](../../../CLAUDE.md).
**Files:**
- [Assets/Scripts/Entities/Units/UnitFactory.cs](../../../Assets/Scripts/Entities/Units/UnitFactory.cs)
- [Assets/Scripts/Entities/Units/Worker.cs](../../../Assets/Scripts/Entities/Units/Worker.cs) (new)
- [Assets/Scripts/Entities/Units/Builder.cs](../../../Assets/Scripts/Entities/Units/Builder.cs) / [Miner.cs](../../../Assets/Scripts/Entities/Units/Miner.cs) (remove or alias)
- [Assets/Scripts/Systems/Work/MiningSystem.cs](../../../Assets/Scripts/Systems/Work/MiningSystem.cs), [BuildingConstructionSystem.cs](../../../Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs)
- [Assets/Scripts/AI/*](../../../Assets/Scripts/AI) (AI build/economy managers)
- [Assets/Scripts/UI/Panels/EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs)
- [Assets/Scripts/UI/Web/HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs)
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json)
**Verification:**
- [ ] Single Hall train button reads "Worker" and produces a unit that can
      both gather and build.
- [ ] Existing mining / construction systems work unchanged with the merged
      unit.
**Estimated effort:** Large

### Phase 3: Move Scout button from Barracks to Hall
**Scope:** Remove Scout from Barracks' `trains` list and add it to Hall's.
Update UI mapping.
**Files:**
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json)
- [Assets/Scripts/Entities/Buildings/Hall.cs](../../../Assets/Scripts/Entities/Buildings/Hall.cs), [Barracks.cs](../../../Assets/Scripts/Entities/Buildings/Barracks.cs)
- HUD command panel sources for the trainer buttons
**Verification:**
- [ ] Selecting a Hall shows a "Scout" train button; selecting Barracks does not.
**Estimated effort:** Small

### Phase 4: Spearman replaces Swordsman
**Scope:** Rename `Swordsman` → `Spearman`. Stats per doc (attack range 1.0
→ 1.5; everything else equal). Keep one-to-one mapping for save / replay
compatibility if needed.
**Files:**
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json)
- [Assets/Scripts/Entities/Units/Swordsman.cs](../../../Assets/Scripts/Entities/Units/Swordsman.cs) → `Spearman.cs`
- [Assets/Scripts/Entities/Units/UnitFactory.cs](../../../Assets/Scripts/Entities/Units/UnitFactory.cs)
- [Assets/Scripts/UI/Panels/EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs) (presentation id 201, UnitClass mapping)
- AI behaviors that reference `Swordsman` by id
**Verification:**
- [ ] Train button reads "Spearman"; trained unit has 1.5 attack range and
      otherwise matches Swordsman.
**Estimated effort:** Small

### Phase 5: Barracks tech rewrite (Conscription + Stone weapons)
**Scope:** Replace `BasicDrills` with `Conscription` (+20 % train speed at the
Barracks) and `WoodenArmor` with `Stone weapons` (unlocks Spearman tier-1
stat bump — pick concrete numbers, doc question #1).
**Files:**
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json)
- [Assets/Scripts/Data/TechTree/TechTreeDB.cs](../../../Assets/Scripts/Data/TechTree/TechTreeDB.cs) (parser whitelist)
- [Assets/Scripts/Systems/Research/ResearchSystem.cs](../../../Assets/Scripts/Systems/Research/ResearchSystem.cs) (effect application)
- [Assets/Scripts/AI/AIBuildOrder.cs](../../../Assets/Scripts/AI/AIBuildOrder.cs) (replace `BasicDrills` / `WoodenArmor` references)
- [Assets/Scripts/UI/Panels/TechTreePanel.cs](../../../Assets/Scripts/UI/Panels/TechTreePanel.cs)
**Verification:**
- [ ] Conscription reduces Barracks train time by ≈ 1/1.2.
- [ ] Stone weapons applies the tier-1 stat bump to existing + future Spearmen.
**Estimated effort:** Medium

### Phase 6: Archery Range techs (Choreographed volleys / Stone-tipped arrows / Fletching)
**Scope:** Add the three techs end-to-end (data + research + effects).
Choreographed volleys is an *active* skill, not a passive tech — needs UI
button on Archery Range, active-skill cooldown component on the building, and
a temporary Archer fire-rate buff in radius/faction-wide for 5 s.
**Files:**
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json)
- [Assets/Scripts/Data/TechTree/TechTreeDB.cs](../../../Assets/Scripts/Data/TechTree/TechTreeDB.cs) (parse list)
- [Assets/Scripts/Systems/Research/ResearchSystem.cs](../../../Assets/Scripts/Systems/Research/ResearchSystem.cs)
- New active-skill components/systems (model after Litharch heal-cooldown pattern)
- [Assets/Scripts/UI/Panels/TechTreePanel.cs](../../../Assets/Scripts/UI/Panels/TechTreePanel.cs)
**Verification:**
- [ ] Each tech researchable from the Archery Range with the documented cost.
- [ ] Fletching grows Archer `attackRange` from 25 → 28.75.
- [ ] Volleys doubles Archer fire-rate for 5 s, locks for 40 s after.
**Estimated effort:** Medium

### Phase 7: Vault — interest rate, banking tiers, resource unlocks
**Scope:** Bump base interest 3 % → 25 %/min. Add banking tier techs that
escalate to 50 / 75 / 100 %. Implement Iron / Veilstone / Veilsteel banking
unlocks (Vault accepts those resources after the relevant tech). Apply
culture yield modifier ±30 % at age-up resolution.
**Files:**
- [Assets/Scripts/Systems/Economy/VaultInterestSystem.cs](../../../Assets/Scripts/Systems/Economy/VaultInterestSystem.cs)
- [Assets/Scripts/Core/Components/BuildingComponents.cs](../../../Assets/Scripts/Core/Components/BuildingComponents.cs) (`VaultStorage.InterestRate`)
- [Assets/Scripts/Entities/Buildings/BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs) (set rate at create)
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json) + parser
- [Assets/Scripts/UI/Panels/EntityActionPanel.cs](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs) (deposit-resource UI)
**Verification:**
- [ ] A 1-min deposit of 100 Supplies at base L1 returns ~125 Supplies.
- [ ] Researching the L1 banking tier moves the rate to 50 %/min next tick.
- [ ] Alanthor Vault yields +30 %; Runai Vault yields −30 % vs base.
**Estimated effort:** Large

### Phase 8: Shrine of Ridan — aura heal + culture mod + masses tiers + Warrior priests
**Scope:** Add aura heal component on the Shrine itself (1 % Max HP / s in
radius 10). Tech upgrades raise the rate to 3 / 6 / 15 %. Warrior priests
tech grants Litharchs a melee attack (numbers TBD per doc question #2). +1 RP
already granted on build; add the conditional +1 RP when Runai is picked at
age-up.
**Files:**
- [Assets/Scripts/Entities/Buildings/BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs) (CreateShrineOfAhridan: aura component)
- [Assets/Scripts/Systems/Combat/](../../../Assets/Scripts/Systems/Combat/) — new ShrineAuraHealSystem (model after [LitharchHealingSystem.cs](../../../Assets/Scripts/Systems/Combat/LitharchHealingSystem.cs))
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json) + parser for masses + Warrior priests
- Age-up flow that detects culture pick (Runai) and grants +1 RP
**Verification:**
- [ ] Friendly units inside 10 u of a built Shrine heal 1 % Max HP / s.
- [ ] Each masses tech bumps the rate per doc.
- [ ] Warrior priests grants Litharchs a melee attack with the agreed stat.
- [ ] Picking Runai at age-up grants an extra +1 RP via the existing
      `ShrineRPGranted` path or a sibling component.
**Estimated effort:** Large

### Phase 9: Fiendstone Keep — culture HP/arrows + four emplacement techs
**Scope:** Apply ±50 % HP & arrow-count culture modifier at age-up (Feraldis +,
Alanthor −). Implement Ballista emplacement (per-cooldown siege single-shot),
Trebuchet emplacement (per-cooldown siege AoE), Additional Towers (+2 max
targets), Reinforced walls (+20 % HP).
**Files:**
- [Assets/Scripts/Entities/Buildings/BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs) (CreateFiendstoneKeep)
- [Assets/Scripts/Systems/Combat/](../../../Assets/Scripts/Systems/Combat/) (auto-fire system extension for ballista/trebuchet shots)
- [Assets/Resources/TechTree.json](../../../Assets/Resources/TechTree.json) + parser
- [Assets/Scripts/Systems/Research/ResearchSystem.cs](../../../Assets/Scripts/Systems/Research/ResearchSystem.cs)
**Verification:**
- [ ] Feraldis Keep starts at 3 000 HP / Alanthor 1 000 HP / neutral 2 000.
- [ ] Each emplacement adds the documented shot type per cooldown.
- [ ] Additional Towers raises `MaxTargets` to 5.
**Estimated effort:** Large

### Phase 10: BuildingUpgradeConfig entries for choice buildings
**Scope:** Add `VaultOfAlmierra` / `TempleOfRidan` / `ShrineOfAhridan` /
`FiendstoneKeep` cost rows to `BuildingUpgradeConfig.TryGetCost` for L2/L3 so
the upgrade ladder works on choice buildings (they start at L1 in the doc).
**Files:**
- [Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs](../../../Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs)
- [Assets/Scripts/Core/Commands/CommandTypes/UpgradeBuildingCommand.cs](../../../Assets/Scripts/Core/Commands/CommandTypes/UpgradeBuildingCommand.cs) (level-cap check if any)
**Verification:**
- [ ] Selecting a Vault / Shrine / Keep at L1 shows an Upgrade button with
      the per-doc cost; the upgrade applies HP multiplier as expected.
**Estimated effort:** Small

## Edge Cases

- Renaming `Swordsman` → `Spearman` may break in-flight games / saves keyed
  on the old id. Provide a one-time migration in `TechTreeDB` (alias old id
  to new) and in `UnitFactory.CreateUnit`.
- `Builder` / `Miner` → `Worker` merge similarly: keep alias creators that
  forward to `Worker.Create` so legacy save data and AI build orders continue
  to resolve.
- Vault interest jumps from 3 % → 25 %/min are massive — flag for play-test
  rebalance before merging Phase 7.
- Shrine aura should ignore units inside friendly transports / garrisons (no
  heal through walls).
- Active skill (Choreographed volleys) must integrate with the existing
  faction-wide-buff plumbing if any; if none, this introduces a new buff bus
  scoped to a single building's faction.

## Dependencies

- Doc resolution of open questions #1 (Stone weapons / Stone-tipped arrows
  numbers) and #2 (Warrior priests damage) before Phase 5 / 8.
- Age-up culture-pick hook (already exists for prefab swap) — Phases 7–9 hang
  off of it for the ±30 / ±50 % modifiers.

## Technical Notes

- Existing `BuildingUpgradeConfig.HpMultiplier` cascade `{1.00, 1.10, 1.15,
  1.20}` is absolute over base (not cumulative) per its comment — the doc
  tables use it as written.
- `VaultStorage.InterestRate` is set at build time in
  [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs);
  changing the base rate is a single-value edit, but banking tiers will need
  a tech-listener that overwrites it on completion.
- `TechTreeDB.ParseTechnology` whitelist must grow as techs are added — see
  [TechTreeDB.cs:240-243](../../../Assets/Scripts/Data/TechTree/TechTreeDB.cs#L240).
- Naming reconciliation: code has `ShrineTag` (alias `ShrineOfAhridan`) and
  `TempleOfRidanTag` (alias `TempleOfRidan`). The doc's "Shrine of Ridan" is
  the **TempleOfRidan** entity in the JSON. Standardize during Phase 8.

## Out of Scope

- Era 2+ (post-age-up) cultural buildings and units — Age 1 alignment work
  spun out as separate tasks 066–080 on 2026-05-19 (see "Sibling tasks" below).
- Glow mechanics, sect chapels, ritual systems — owned by
  [task-sect-system-redesign-063](../task-sect-system-redesign-063/task.md).
- HUD visual polish for the new techs (icons, tooltips) — list of art deltas
  can spin off as a child task after Phase 10.
- **Age-up "transform, don't replace" mechanic** — owned by
  [task-ageup-transform-hut-066](../task-ageup-transform-hut-066/task.md).

## Sibling tasks created 2026-05-19 (full-sweep audit fanout)

After auditing `docs/Design/Complete.md` end-to-end on 2026-05-19, the
following sibling tasks cover the Age 1 + cross-cutting gaps that fall
outside this task's Age 0 scope:

- **task-066** Age-up transform-don't-replace (Alanthor wall anchor / Runai wagon burst / Feraldis raider spawn)
- **task-067** Feraldis Raider rebuild (split from Runai_Raider cavalry)
- **task-068** Alanthor Royal Stable + Cataphract reparent
- **task-069** Runai buildings split (Grazing Grounds, Bazaar/Hall split, retire Runai_Vault + PackBazaar)
- **task-070** Veilsteel Frenzy carry system
- **task-071** Cultured rename layer (Hall→Town Hall/Trader's Hall/War Hall etc.)
- **task-072** Trader-warrior lane patrols
- **task-073** Revert rejected stat overrides (Practice Range 1500 HP, Longhouse 1400 HP)
- **task-074** Religious-unit tier fix (Iconoclast L3 + pop 1 + 30 Vs, Scholar cost, singleton cap)
- **task-075** Caravan-loot Feraldis-only
- **task-076** Per-battalion upgrades (replace faction-wide tier application)
- **task-077** Code-default vs TechTree.json drift sweep
- **task-078** Runai Crystal-Curse neutrality wiring
- **task-079** Age 1 unit tier ladders (L2/L3 across all three cultures)
- **task-080** Feraldis instant-200 population at age-up

## New findings beyond the original 10 phases (full-sweep audit 2026-05-19)

These are Age 0–scoped divergences the 10 phases above don't yet cover:

- **Code default drift** (also in task-077 cross-cutting): in
  [BuildingFactory.cs](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs),
  `DefaultLoS = 35` (Hall doc 24), `DefaultHP = 600`, `DefaultLoS = 14`
  (Barracks doc 800 / 18). [Swordsman.cs](../../../Assets/Scripts/Entities/Units/Swordsman.cs)
  `DefaultSpeed = 3.5` (doc 5.5).
  TechTreeDB overrides at runtime, so these are latent bugs only if the DB
  fails to load — but they're misleading to read.
- **TechTree.json drift** that *will* affect runtime:
  - `GatherersHut` HP `400` ([TechTree.json](../../../Assets/Resources/TechTree.json))
    vs doc 800 (code default also 800).
  - `Hut` HP `350` / pop `5` / LoS `12` vs doc 600 / 10 / 14.
  - `Archer` `minAttackRange: 10` vs doc 1 — affects pathing (units back
    to 10 before firing).
- **GatherersHut income**: doc says 60 S/min; code emits 90 S/min
  (15 per 10 s tick) in [GathererHutIncomeSystem.cs](../../../Assets/Scripts/Economy/GathererHutIncomeSystem.cs).
- **Building cost three-way drift** between
  [BuildCosts.cs](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs),
  TechTree.json, and the doc: Barracks (JSON 150 S + 70 I, BuildCosts.cs
  220 S + 40 I, doc 220 S + 40 I); Archery Range (JSON 150 S + 60 I, doc
  180 S + 50 I); Gatherer's Hut (JSON 120 S, missing 10 I per doc).
  TechTree.json wins at runtime — fold this into Phase 1/5/6 file
  modifications or split into a small "cost reconciliation" sub-phase.
- **FiendstoneKeep is defined inside Era 2 culture sections in
  TechTree.json**, not alongside Vault and Shrine in the Era 1 main
  buildings list. Structural inconsistency — fold into Phase 9 file
  modifications.
