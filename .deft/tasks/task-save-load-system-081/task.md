---
deft:
  id: task-save-load-system-081
  type: feature
  status: active
  stage: scope
  phase: 0
  total_phases: 5
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: feature/save-load
  mode: human-in-the-loop
  labels: [save-load, ecs, serialization, ui]
---

# Save / Load — full deterministic state

## Context

The in-game menu (web HUD + IMGUI) already surfaces Save / Load
buttons, but they're stubs — the actual snapshot pipeline doesn't
exist. Until this lands, the items are flagged `disabled: true`
in [Menu.jsx](../../../HudFrontend/src/components/Menu.jsx) and
the C# handler in [HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs)
Notifies "coming soon".

User chose **full deterministic state** at scope time:
- Snapshot the whole ECS world (every entity, every component) so
  a reloaded save resumes mid-tick byte-identical to the original.
- Capable of feeding a multiplayer replay system later.
- Save format must be versioned so saves survive schema changes.

This is multi-day work. Estimated 5 phases.

## User Value

- Resume long matches across sessions.
- "Save before this battle" tactical safety net.
- Foundation for replay (campaign-mode debugging + sharing).

## Requirements

- R1: A `SaveSnapshot` data type capturing
  - All `Entity`s and their full component sets (excluding
    Presentation-only / GameObject-side state).
  - The `LockstepManager` tick counter + commands-in-flight ring.
  - The current MapArchetype + procedural seed (so terrain
    regenerates identically; we don't have to serialize heightmaps).
  - The current `MatchPhase`, `Faction*` singletons, `SectAdoptionState`
    buffers, `TempleChapelSlot` buffers, `Era` markers, all `TechTreeDB`-derived
    research-state component values.
- R2: Save and Load entry points usable from
  [InGameMenuPanel](../../../Assets/Scripts/UI/HUD/InGameMenuPanel.cs)
  and [HudBridge `menu:item` handler](../../../Assets/Scripts/UI/Web/HudBridge.cs).
- R3: Slot-based save UI rendered in the web HUD (Menu.jsx) with
  per-slot metadata (timestamp, faction, age, elapsed match
  time). At least 8 slots + autosave slot.
- R4: Save format versioning. A `saveVersion: int` header + a
  table mapping schema versions to migration steps so older saves
  load (or fail with a clear "save too old" message).
- R5: Multiplayer guard — save/load only allowed in single-player
  match. Multiplayer saves require lockstep snapshot agreement
  across all peers (out of scope for v1).

## Acceptance Criteria

- [ ] Saving in a mid-match skirmish then loading the same save
      reproduces resource totals, building positions/levels,
      unit positions/HP, research state, sect state byte-identical.
- [ ] Load works after a full game-process restart (the save file
      survives outside the running session).
- [ ] Saving while a unit is in mid-flight train queue reproduces
      the queue contents post-load (including remaining time).
- [ ] Schema-version mismatch surfaces a clear modal, doesn't
      silently corrupt state.
- [ ] Save slot picker UI lists slots with metadata, sorted by
      most-recent.

## Implementation Phases

### Phase 1: Snapshot schema + ECS dump
**Files:** new `Assets/Scripts/Persistence/SaveSnapshot.cs`,
new `Assets/Scripts/Persistence/SaveWriter.cs`,
new `Assets/Scripts/Persistence/SaveReader.cs`.
**Estimated effort:** Large — covers every component type the
game uses; each one needs a reflective serializer or a hand-written
DTO. Plan to use Unity's built-in `EntityManager` serialization
where possible (it dumps entire chunks) and only hand-write the
managed singletons.

### Phase 2: World reconstruction on load
**Files:** new `Assets/Scripts/Persistence/SaveLoader.cs`,
[GameBootstrap.cs](../../../Assets/Scripts/Bootstrap/GameBootstrap.cs)
hook for the "loading from save" branch.
**Estimated effort:** Medium-Large. Trickiest piece is the
managed-side state (`FactionResources`, `LockstepManager`,
`InfluenceManager`, etc.) — the loader has to restore them in the
right order before any system ticks.

### Phase 3: Web HUD save UI
**Files:** [HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs)
new `saves:list` / `saves:save` / `saves:load` / `saves:delete`
topics, new `HudFrontend/src/components/SaveLoad.jsx`,
[Menu.jsx](../../../HudFrontend/src/components/Menu.jsx) opens
the SaveLoad overlay instead of the disabled stub.
**Estimated effort:** Medium.

### Phase 4: Schema versioning + migration
**Files:** `Assets/Scripts/Persistence/SaveMigrations.cs`,
test fixtures for old-format saves.
**Estimated effort:** Small — but worth doing before the format
ships so users don't lose their first round of saves to the
next change.

### Phase 5: Acceptance test pass
**Files:** new `Tests/PlayMode/SaveLoadRoundtripTests.cs`,
manual playtest checklist.
**Estimated effort:** Small.

## Dependencies

- None blocking. Recommended to land after the camera /
  HUD / training fixes settle so the snapshot doesn't churn from
  schema changes underneath it.

## Out of Scope

- Multiplayer co-op saves (R5 explicitly defers).
- Cloud sync.
- Campaign-narrative saves (separate from match saves).
- Replay playback (separate task — uses the same snapshot
  infrastructure but adds tick-by-tick deltas).
