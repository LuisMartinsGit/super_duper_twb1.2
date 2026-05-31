---
deft:
  id: task-archery-range-tier-units-110
  type: improvement
  status: active
  stage: implementation
  phase: 3
  total_phases: 3
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Archery Range tier units — Crossbowman (lvl 2) + Longbowman (lvl 3)

## Context

User request 2026-05-21: the Archery Range should unlock two new ranged
unit tiers as it upgrades. The building already carries
`BuildingUpgradeable` (per [task-064 audit](../task-codebase-audit-064/task.md))
so `minBuildingLevel` gating is the established mechanism.

**Two new units:**
- **Crossbowman** — high attack but lower rate of fire and range. Unlocks at Archery Range level 2.
- **Longbowman** — low rate of fire, very high range and damage. Unlocks at Archery Range level 3.

Per CLAUDE.md, design folder must land first. Archery Range is currently
documented in [docs/Design/Age_0.md](../../../docs/Design/Age_0.md) (it's
an Age 0 building that carries into Age 1).

## User Value

Players who invest in upgrading the Archery Range get differentiated unit
choices: standard Archers from a fresh Range, Crossbowmen as a slow
heavy-hitter at lvl 2, Longbowmen as a long-range specialist at lvl 3.
Gives the upgrade path a tangible reward beyond stat tweaks.

## Requirements

- **R1** — Update [docs/Design/Age_0.md](../../../docs/Design/Age_0.md)
  Archery Range section to spec the three tiers and the unit ladder
  (Archer baseline, Crossbowman at lvl 2, Longbowman at lvl 3). Mark all
  numeric stats `PLAYTEST PLACEHOLDER`.
- **R2** — Add `Crossbowman` and `Longbowman` entries to
  `Assets/Resources/TechTree.json` following the `Archer` entry as
  template.
- **R3** — Add cost entries to `Assets/Scripts/Data/TechTree/BuildingCosts.cs`
  (or wherever unit costs are stored).
- **R4** — Create `Assets/Scripts/Entities/Units/Crossbowman.cs` and
  `Longbowman.cs` factories following `Archer.cs` template.
- **R5** — Wire Archery Range's `trains` list to include the two new
  units with `minBuildingLevel: 2` and `minBuildingLevel: 3` gates.
- **R6** — Add the two unit ids to the queue glyph map and Actions.jsx
  catalogue so they render correctly in the training queue + builder UI.
- **R7** — No regression to existing Archer training flow.

## Acceptance Criteria

- [ ] `docs/Design/Age_0.md` Archery Range section spec'd with all three
      tiers and the unit ladder.
- [ ] `Crossbowman` and `Longbowman` exist in `TechTree.json` with the
      stat values from the proposal (HP, damage, range, cooldown, speed,
      cost).
- [ ] Selecting a level-1 Archery Range shows only the Archer training
      card.
- [ ] Upgrading to level 2 unlocks the Crossbowman card.
- [ ] Upgrading to level 3 unlocks the Longbowman card.
- [ ] Both new units train, spawn, move, attack, and die without errors.
- [ ] Crossbowman vs Archer in a side-by-side: Crossbowman fires every
      ~3s, Archer every ~1.5s; Crossbowman deals ~18 damage per shot,
      Archer ~8.
- [ ] Longbowman attack range ~40 vs Archer ~25 (visible "shoots from
      further away" behavior).
- [ ] Training queue shows correct icons / glyphs for both new units.
- [ ] No new compile errors.

## Implementation Phases

### Phase 1: Design folder update (docs-only)
**Scope:** Update [docs/Design/Age_0.md](../../../docs/Design/Age_0.md)
Archery Range section to spec the three-tier ladder and the unit unlocks.
Stats marked `PLAYTEST PLACEHOLDER`.
**Files:**
- `docs/Design/Age_0.md`
**Verification:**
- [ ] Archery Range section has three tier subsections with HP, damage,
      range, cooldown for each unit (Archer / Crossbowman / Longbowman).
- [ ] All numeric values marked `PLAYTEST PLACEHOLDER`.
- [ ] No non-`.md` files modified in this phase.

### Phase 2: TechTree + factories + training options
**Scope:** Add `Crossbowman` and `Longbowman` entries to TechTree.json
following the Archer template. Create factory files
`Crossbowman.cs` and `Longbowman.cs` mirroring `Archer.cs`. Wire the
Archery Range training options to include both with `minBuildingLevel`
gates.
**Files:**
- `Assets/Resources/TechTree.json` (add 2 unit entries)
- `Assets/Scripts/Entities/Units/Crossbowman.cs` (new)
- `Assets/Scripts/Entities/Units/Longbowman.cs` (new)
- `Assets/Scripts/Core/Components/UnitComponents.cs` (add `CrossbowmanTag` + `LongbowmanTag`)
- `Assets/Scripts/Data/TechTree/UnitCosts.cs` or `BuildingCosts.cs` — wherever unit costs live
- `Assets/Scripts/Entities/Buildings/ArcheryRange.cs` (or wherever its `trains` array lives) — add the two unit ids with `minBuildingLevel`
**Verification:**
- [ ] Project compiles cleanly.
- [ ] `Crossbowman` and `Longbowman` registered in TechTree (via TechTreeDB lookup).
- [ ] ArcheryRange.trains array contains both units with correct `minBuildingLevel`.

### Phase 3: JSX glyph wiring + builder catalogue
**Scope:** Add the two new unit ids to the queue glyph map in
`Selection.jsx` (built in task-108) so the training queue shows correct
icons. Add to Actions.jsx catalogue if there's a static unit catalogue.
**Files:**
- `HudFrontend/src/components/Selection.jsx` — extend `QUEUE_GLYPH_BY_UNIT_ID`
- `HudFrontend/src/components/Actions.jsx` — if a static unit catalogue exists, extend it
- `Assets/StreamingAssets/HUD/hud.js` + `hud.css` (auto-regenerated by `npm run build`)
**Verification:**
- [ ] `npm run build` clean.
- [ ] Queue glyph for both units displays (reuse `arrow` glyph or pick new ones).

## Technical Notes

**Stat values (PLAYTEST PLACEHOLDER, per user-confirmed proposal 2026-05-21):**

| Stat | Archer (baseline) | Crossbowman | Longbowman |
|---|---|---|---|
| HP | 60 | 70 | 55 |
| Damage | 8 | 18 | 25 |
| Min Range | 10 | 6 | 12 |
| Max Range | 25 | 18 | 40 |
| Cooldown | 1.5s | 3.0s | 3.5s |
| Speed | 4 | 3.5 | 4 |
| LoS | 25 | 22 | 35 |
| Population | 1 | 1 | 1 |
| Cost | (existing) | 40 supplies + 35 iron | 50 supplies + 40 iron |
| Train time | (existing) | 18s | 25s |

**Combat type tags** — both use `DamageType.Ranged` / `ArmorType.Ranged`
matching Archer.cs:90-91.

**Presentation IDs** — pick the next available IDs after the existing
unit range. Reuse Archer's prefab visual for v1 (per pattern from
task-109 walls reusing existing prefabs). Visual differentiation deferred
to art pass.

**Training queue glyph** — both can reuse the `arrow` glyph from
`ActionGlyph` map in Actions.jsx (added in task-108 Phase 4). Visual
differentiation a future polish item.

## Out of Scope

- Unique visual prefabs (reuse Archer's; art pass later).
- Special abilities (no Crossbowman heavy-shot stun, no Longbowman
  volley — just raw stat profiles).
- AI tactical use (SimpleAISystem trains via BuildOrderStep; no AI
  preference logic for picking Crossbowman over Archer in v1).
- Counter-balance changes to other units.
- Per-battalion upgrades (task-076 is the canonical home for that).
