---
deft:
  id: task-entity-display-overhaul-108
  type: improvement
  status: completed
  stage: release
  phase: 5
  total_phases: 5
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Entity display overhaul — builder prices, stat panels, training queue, resource nodes

## Release Notes

### Summary
Entity display overhaul (6 user-reported items): builder UI prices, gatherer/hall yield, training queue UI with right-click cancel, resource node remaining, building attack/defense fix + hide speed, world-space depletion bar.

### What landed (file-by-file by phase)

- **Phase 1 — Data extraction (extractors + components)**
  - `Assets/Scripts/Core/Components/ResourceComponents.cs` — added `IronDepositState.InitialIron` field (default 0; allows graceful fallback for pre-task-108 saves).
  - `Assets/Scripts/Bootstrap/IronDepositBootstrap.cs` — set `InitialIron = IronPerDeposit (500)` at bootstrap.
  - `Assets/Scripts/UI/Common/UITypes.cs` — extended `EntityDisplayInfo` with `EntityKind`, `YieldPerMinute`, `QueueCapacity`, `Queue`; new `EntityQueueSlot` struct co-located.
  - `Assets/Scripts/UI/Panels/EntityExtractors.cs` — combat branch rewritten (BuildingTag → BuildingRangedAttack.Damage; non-buildings → Damage.Value; missing component → null); Speed hidden for buildings; YieldPerMinute mirrors `SuppliesIncome.PerMinute`; ResourceMax from `InitialIron` (fallback to RemainingIron); EntityKind tag (unit/building/resource); `BuildQueueSnapshot` 5-slot helper; `ResolveUnitDisplayName` reuses `TechTreeDB.unit.name`.

- **Phase 2 — HudBridge marshalling + CancelTrainCommand**
  - `Assets/Scripts/Core/Commands/CommandTypes/CancelTrainCommand.cs` — new file; `CancelTrainCommand` ECS component + `CancelTrainCommandHelper.Execute` (refunds base unit cost via `EntityActionExtractor.GetUnitCost`; zeroes `TrainingState.Busy/Remaining/Total` on slot-0 cancel so `TrainingSystem` promotes the next slot).
  - `Assets/Scripts/Core/Commands/CommandRouter.cs` — new `IssueCancelTrain` with the standard guard triad (`em.Exists` + `HasComponent<TrainingState>` + `NotControllableTag` filter).
  - `Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs` — added `QueueCancelTrainForLockstep` (slot index rides in the existing int `TargetEntityId`, no schema bump).
  - `Assets/Scripts/Core/Multiplayer/LockstepTypes.cs` — `LockstepCommandType.CancelTrain = 20`.
  - `Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs` — new `ExecuteCommand` dispatcher case mirroring `Train`.
  - `Assets/Scripts/UI/Web/HudBridge.cs` — `EmitSingle` emits null-aware `atk/def/spd` (object `{value,kind}` or JSON null), new fields `entityKind`, `yield`, `resourceRemaining/resourceMax/resource`, `queue` (5-element padded), `queueCapacity`; reusable StringBuilder helpers `AppendIntStatCell/AppendFloatStatCell/AppendQueueJson` (no per-emit alloc). New inbound topic `actions:cancelTrain` resolves the entity from `SelectionSystem.CurrentSelection`, applies player-owned filter, routes through `CommandRouter.IssueCancelTrain`.

- **Phase 3 — Actions.jsx builder grid prices**
  - `HudFrontend/src/components/Actions.jsx` — `withAffordability` rewritten: live `costs[key]` ALWAYS wins; static `it.cost` consulted ONLY when live is strictly `undefined`; both undefined → `unavailable=true` and the cell renders muted with a "Price unavailable" kicker; `ActionCell` extended with `unavailable` branch (`act-cell--unavailable` modifier). BUILDINGS_ERA2 fallback prices reconciled byte-for-byte against `BuildCosts.cs` (Hut 50→80, Barracks Iron90→Supplies220, ArcheryRange Iron110→Supplies180, Runai_SiegeWorkshop Iron200→Supplies320, TempleOfRidan Supplies250→Supplies300, Alanthor_Tower Iron90→Supplies140, Alanthor_Wall Iron30→Supplies50). `notWired:true` flag dropped from ArcheryRange. `RoyalStable` and `VeilsteelForge` entries removed entirely (no TechTree entry, no factory, no BuildableBuildings slot). `ActionGlyph` added to module exports for Phase 4 reuse.
  - `HudFrontend/src/styles.css` — `.act-cell--unavailable` modifier (greyscale 0.7, opacity 0.5, cursor not-allowed) + `.act-cell__hint` italic Cinzel kicker.
  - `HudFrontend/dist/hud.js` + `hud.css` — rebuilt via `npm run build` at HudFrontend/ (clean, no warnings).

- **Phase 4 — Selection.jsx + training queue UI + stat panel rework**
  - `HudFrontend/src/components/Selection.jsx` — `SelectionDetail` reads the new payload end-to-end; `formatStatValue` helper (null → em-dash, distinct from 0); `data-entity-kind={sel.entityKind}` on `.sel-stats` so the CSS hides the speed cell + its divider for buildings; speed cell inlined (`.sel-stat-speed` wrapper class). Yield row rendered between progress bars and resource bar inside `.sel-bars` ("Drop-off depot" fallback when `yield.perMinute === 0`). New `ResourceRemainingBar` subcomponent (amber `#d97a2e`, "REMAINING N / max", switches to "DEPLETED" at 0); Health and Shield bars gated off for `entityKind === 'resource'`. New `QueueStrip` subcomponent reuses existing `.sel-queue-*` scaffolding; populated slots render `ActionGlyph` (imported from Actions.jsx, mapped Swordsman→helm, Scout→spear, Archer→arrow, Builder/Worker/Miner→mason, Litharch→sigil); slot-0 progress fill from `slot.progress`; queued slots 1-4 at opacity 0.55 with full-but-greyed bar. Right-click → `actions:cancelTrain`, transient "+N supplies" popup (700ms rise + fade), shimmer (500ms key force-remount for rapid same-slot re-clicks). `cursor:not-allowed` on populated slots. Accessibility: `aria-label`, `aria-hidden`, `prefers-reduced-motion` media query disables shimmer + popup + smooth-fill.
  - `HudFrontend/src/components/Actions.jsx` — `ActionGlyph` added to exports (next to `ActionsPanel/ActionsGrid/BUILDINGS_*/TRAIN_UNITS`).
  - `HudFrontend/src/styles.css` — `.sel-queue-slot[data-shimmer]`, `@keyframes sel-queue-shimmer`, `.sel-queue-refund` + `@keyframes sel-queue-refund-rise`, `.sel-stats[data-entity-kind=building]` selector, `.sel-bar-resource` gradient, `prefers-reduced-motion` guard.
  - `HudFrontend/dist/hud.js` + `hud.css` — rebuilt (225.9kb js + 36.8kb css; +~6kb js for QueueStrip + popup state, +~1.8kb css for shimmer/popup/resource bar/reduced-motion).

- **Phase 5 — World-space depletion bar**
  - `Assets/Scripts/UI/Common/WorldOverlayPalette.cs` — added `ResourceDepletion = #d97a2e` token (Color32, WCAG AA against terrain), with a doc comment cross-referencing `.sel-bar-resource__fill`.
  - `Assets/Scripts/UI/HUD/FloatingHealthBars.cs` — `DrawBarForEntity` branches at the top to `DrawResourceDepletionBar` when `IronMineTag` OR `CadaverTag` is present (returns before standard Health draw). New `DrawResourceDepletionBar` reads `RemainingIron/InitialIron` (or `RemainingCrystal/MaxCrystal`); graceful 0-max fallback to `RemainingIron` for pre-task-108 saves; `Mathf.Lerp(current, target, dt * 3.0f)` smoothing; renders single amber bar (transparent bg + 1px dark outline + amber fill). New `Dictionary<Entity,float> _lastFill` (renderer-only, never written to ECS world); `PruneStaleFillEntries` walks once every `PruneEveryNFrames=180` (~3s @ 60fps). Building y-offset (3.2f) for headroom.

### Post-review fix (AC-9 gating bug)
The reviewer surfaced a runtime concern: iron deposits and cadavers do NOT carry the `Health` component (confirmed via grep of `IronDepositBootstrap.cs` and `Cadaver.cs`). `FloatingHealthBars` had a `HasComponent<Health>` gate at lines 117 and 134 that would have filtered resource nodes BEFORE the new depletion-bar branch inside `DrawBarForEntity` could run — the amber bar would have been unreachable at runtime. Fixed by introducing a `HasDrawableBar(entity)` helper that returns true for `Health` OR `IronMineTag` OR `CadaverTag`, and replacing both gates with the new helper. AC-9 should now pass at runtime.

### Acceptance criteria
- 12/12 main ACs verified by the reviewer (verdict: `approve`).
- 13th AC (AC-9, world-space depletion bar) verified post-fix via the `HasDrawableBar` gate replacement.
- Overall: **13/13 ACs met** subject to in-engine confirmation (Unity Editor manual test run — no CI exists for this project).

### Outstanding concerns (non-blocking)
- **Stray `ProjectSettings/EditorBuildSettings.asset` edit** — surfaced by the reviewer; per `git status` at session start this file was modified before task-108 began. **Not introduced by this task; investigate separately** (it adds a disabled MainScene entry).
- **`Defense.Melee` is the only defense field surfaced today.** Full breakdown (Melee / Ranged / Siege / Magic) is deferred — the multi-field Defense component exists but the JSX only displays one number. Follow-up.
- **Refund logic uses base unit cost** via `EntityActionExtractor.GetUnitCost`. Feraldis training tax (1.75×) is over-refunded by ~75% relative to actual paid cost. Deferred per architect's risk note — matches the legacy `EntityActionPanel.CancelQueueItem` behaviour; a future task could add `TrainQueueItem.PaidCost`.

### Manual test list (consolidated from all 5 phases)

Phase 1 (data extraction):
- Grep `IronDepositState` shows only the bootstrap setting the new field.
- Selecting a Hut in Editor and inspecting `EntityInfoExtractor` reports `info.Defense == null`.
- Selecting a Watch Tower shows `Attack > 0`.
- Selecting a Hall with `SuppliesIncome` shows `YieldPerMinute == 60`.
- Selecting an Iron Deposit shows `ResourceMax == 500` (from InitialIron).
- A loaded save (pre-task-096) shows `InitialIron == 0`; the extractor falls back to RemainingIron — bar reads full, not empty.

Phase 2 (HudBridge):
- DevTools `window.unityHUD.peek('selection')` for a Hut shows null `def` and null `spd`.
- DevTools selection for an Iron Deposit shows the `resource` block populated.
- DevTools selection for a Barracks training a Swordsman + 2 queued shows the 5-slot `queue` array (1 active + 2 queued + 2 null).
- DevTools selection for a Hall shows `yield: { perMinute: 60.0, label }`.
- DevTools `peek('costs')` lists `GatherersHut`, `ShrineOfAhridan`, etc.
- Singleplayer cancel: right-click a queued slot from JSX (after Phase 4 lands) triggers the refund.
- Multiplayer cancel (smoke test): one client cancels; both sims tick deterministically.

Phase 3 (Actions.jsx):
- First-frame render of Gatherer's Hut shows `120 Supplies` (live, not 60).
- First-frame render of Shrine of Ahridan shows its full multi-resource breakdown.
- `RoyalStable` and `VeilsteelForge` no longer appear in the catalog.
- Era-2 catalog entries that DO have `BuildCosts.cs` rows render the corrected fallback.
- No flicker on removing the single-resource fallback for any catalog entry.

Phase 4 (Selection.jsx):
- Selecting a Hut: stat row collapses to two cells (Attack / Defense).
- Selecting a Watch Tower: stat row shows `20 Damage / — Armor`.
- Selecting a freshly placed GathererHut: `YIELD 60 supplies/min` row visible.
- Selecting an Iron Deposit: no Health bar, amber `REMAINING N / 500`.
- Selecting a Barracks training a Swordsman with 3 Archers queued: 4 populated slots + 1 empty.
- Right-click slot 2 (queued Archer): slot shimmers briefly, refund popup, slot clears.
- Right-click slot 0 (in-production Swordsman): slot 1 promotes to slot 0; training resumes on the promoted unit.
- Right-click anywhere on the strip does NOT issue a world move order.

Phase 5 (world-space depletion bar):
- Iron deposits in the world: single amber bar above each, no green Health bar.
- Crystal cadavers: same amber bar, identical chrome.
- Mining an iron deposit: amber bar fill shrinks smoothly (lerp, not snap).
- Selecting a deposit: border color brightens from inlay-shadow to accent gold.
- At `Depleted == 1`: bar hides; the deposit entity stays in the world.

### Next steps
1. **Bundle the manual test list above and run it against a Unity build.** No CI exists for this project (per `.deft/project.md`); confirmation is in-engine.
2. **If all manual tests pass, advance the dependent child tasks from audit 082** that overlap with this work:
   - **task-090 (HudBridge query consolidation)** — superseded in part by this task's HudBridge changes (the new payload fields share the existing 30 Hz `PushSelection` diff path and reuse `SelectionSystem.CurrentSelection` for cancel routing). Re-audit task-090's remaining scope; close as obsolete if the cached-query work this task did already satisfies it.
   - Re-check tasks **083-089** for overlap; queue per audit-082's priority order.
3. **Revert or file the stray `ProjectSettings/EditorBuildSettings.asset` edit** (not introduced here).
4. **Optional follow-ups** captured above: full Defense breakdown rendering, paid-cost refund (Feraldis tax fix via `TrainQueueItem.PaidCost`).

## Context

Six UI/UX defects reported by the user on 2026-05-20 all touch the same
seam: how an entity's runtime state reaches the player. The Web HUD
(Chromium-embedded React in `HudFrontend/`) is the live UI; the legacy
IMGUI panels (`EntityInfoPanel`, `EntityActionPanel`, `BuilderCommandPanel`'s
own draw paths) are suspended at
[GameplayUIController.cs:95-115](../../../Assets/UI/Scripts/GameplayUIController.cs#L95)
and must NOT be re-woken. All player-visible fixes therefore land in three
places: the C# extractors that read ECS state
([EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs)),
the JSON marshaller that ships extracted state to JS
([HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs)), and the
React renderers
([HudFrontend/src/components/Selection.jsx](../../../HudFrontend/src/components/Selection.jsx),
[Actions.jsx](../../../HudFrontend/src/components/Actions.jsx)).
Two additional world-space overlay changes touch
[FloatingHealthBars.cs](../../../Assets/Scripts/UI/HUD/FloatingHealthBars.cs).

Per task-064 §A the JSX `BUILDINGS_START` catalog hardcodes a `60`-supplies
fallback cost for Gatherer's Hut while `BuildCosts.cs` ships `120` — the
fallback is briefly visible on the first frame before the `costs` topic
arrives, and for ids the `costs` topic does not cover it stays wrong
forever. Per task-091 several Age-1 buildings still aren't in
`BuildableBuildings`, so their prices never reach the builder UI at all
(that task lands the HashSet entries; this one ensures the prices render
once they do).

## User Value

A player who selects a building reads attack/defense correctly, sees
exactly which units are queued and how many, and can cancel any of them
(including the one currently in production) with a refund. A player who
selects an iron or crystal deposit reads how much is left to mine and
sees a depletion bar above the node instead of a misleading green health
bar. A player opening the builder menu reads accurate prices for every
buildable structure on the first frame.

## Requirements

- R1: **Builder UI prices.** The Web HUD's builder action grid must show
  the **authoritative** cost for every entry. The single-resource
  fallback in
  [Actions.jsx BUILDINGS_START / BUILDINGS_PLACING / BUILDINGS_ERA2](../../../HudFrontend/src/components/Actions.jsx#L56-L91)
  must either be reconciled to `BuildCosts.cs` byte-for-byte OR be
  removed entirely so the `realCost` from the `costs` topic is the only
  source rendered. Where the user-reported "wrong prices for special
  buildings" means Shrine / Vault / Keep / Age-1 culture buildings,
  confirm those ids exist in
  [BuildCosts.cs](../../../Assets/Scripts/Data/TechTree/BuildingCosts.cs)
  and are emitted by `HudBridge.PushCosts`
  ([HudBridge.cs:889](../../../Assets/Scripts/UI/Web/HudBridge.cs#L889)).

- R2: **Gatherer's Hut + Hall yield display.** When a player-owned
  GathererHut is selected, the Selection panel must show its current
  per-tick supplies yield (the live `SuppliesIncome.PerTick` value, which
  [GathererHutIncomeSystem.cs](../../../Assets/Scripts/Economy/GathererHutIncomeSystem.cs)
  updates every 2 s to reflect area overlap with neighbours / enemies /
  walls). Format as supplies-per-minute (PerTick × 60 / Interval) so it
  reads against the resource HUD's per-minute totals. The Hall has no
  direct yield — it shows "Drop-off depot" or omits the yield row
  entirely (Hall does carry `SuppliesIncome` from the
  [Hall.cs](../../../Assets/Scripts/Entities/Buildings/Hall.cs) constructor
  for the 60 S/min trickle; surface that value the same way).

- R3: **Training queue (5 slots) + right-click cancel including the
  in-production slot.** Every player-owned trainer building (HallTag,
  BarracksTag, ArcheryRangeTag, ShrineTag, TempleOfRidanTag, culture
  trainers) must, when selected, render a 5-slot queue visualization
  showing the unit currently training (slot 0) plus up to 4 queued units
  (slots 1-4). Right-click on any populated slot cancels it AND refunds
  its cost. Cancelling slot 0 (in-production) must also abort the
  training timer and remove the buffer entry. This contradicts the
  existing
  [EntityActionPanel.CancelQueueItem at line 1262](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1262)
  which explicitly refuses to cancel slot 0 — the new behaviour
  supersedes it. Implementation must reach C# via a new
  `actions:cancelTrain` bridge topic carrying the queue index.

- R4: **Iron and crystal deposit remaining.** When an iron deposit
  (`IronDepositState`) or crystal cadaver (`CadaverState`) is selected,
  the Selection panel must show `Remaining: N / Max` where N is
  `RemainingIron` / `RemainingCrystal` and Max is the deposit's initial
  capacity. `CadaverState.MaxCrystal` already exists
  ([CrystalComponents.cs:145](../../../Assets/Scripts/Core/Components/CrystalComponents.cs#L145)).
  `IronDepositState` has no `MaxIron` field — store the bootstrap-time
  initial value either by adding `IronDepositState.InitialIron` (preferred)
  or by referencing
  [IronDepositBootstrap.IronPerDeposit](../../../Assets/Scripts/Bootstrap/IronDepositBootstrap.cs#L230)
  as a constant. Hard-coded `info.ResourceMax = 500` in
  [EntityExtractors.cs:166](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L166)
  must be replaced.

- R5: **Building attack/defense correctness + hide speed for buildings.**
  Buildings render 0 attack today because
  [EntityExtractors.cs:58](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L58)
  reads only the unit-style `Damage` component
  ([CombatComponents.cs:12](../../../Assets/Scripts/Core/Components/CombatComponents.cs#L12)),
  while combat-capable buildings carry `BuildingRangedAttack.Damage`
  instead (verified in
  [BuildingFactory.cs:403,843,1017](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L403)
  for Keep / Watch Tower / Totem Tower and `Hall.cs:64` for the Hall).
  Extractor must read `BuildingRangedAttack` when present on an entity
  that carries `BuildingTag` and surface its `Damage` as the attack
  value. Defense is already correctly read from the multi-field `Defense`
  component
  ([BuildingComponents.cs:392](../../../Assets/Scripts/Core/Components/BuildingComponents.cs#L392));
  audit which buildings actually have `Defense` attached (today only
  KingsCourt, Crucible, Vault, VeilsteelFoundry, FeraldisFoundry — see
  the `Defense` add sites at lines 1097, 1124, 1158, 1185, 1212 of
  BuildingFactory.cs). Buildings without `Defense` should display "—"
  rather than `0`. Speed: when the selected entity has `BuildingTag`,
  the Selection panel must hide the speed cell entirely (not render
  "0 Move"). Per the existing payload at
  [HudBridge.cs:1291](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1291),
  `spd` is always emitted; the conditional belongs either in the
  payload or in
  [Selection.jsx:276](../../../HudFrontend/src/components/Selection.jsx#L276).

- R6: **Resource node depletion bar replaces health bar.**
  [FloatingHealthBars.cs:144-205](../../../Assets/Scripts/UI/HUD/FloatingHealthBars.cs#L144)
  draws the green/amber/red bar above every entity with a `Health`
  component. Iron deposits and crystal cadavers do carry a `Health`
  component today (the green bar shown is in fact the Health bar). For
  entities with `IronMineTag` or `CadaverTag` (and any future resource
  node tag), the green health bar must be suppressed and replaced with
  a single amber depletion bar driven by `RemainingIron / InitialIron`
  (or `RemainingCrystal / MaxCrystal` for cadavers). Use a distinct
  amber color from `WorldOverlayPalette` (add `ResourceDepletion` if
  one doesn't exist) so the player learns the visual vocabulary "amber =
  resource left, not health".

- R7: **No suspended IMGUI revival.** All changes must land in
  [HudFrontend/src/](../../../HudFrontend/src/),
  [HudBridge.cs](../../../Assets/Scripts/UI/Web/HudBridge.cs),
  [EntityExtractors.cs](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs),
  [FloatingHealthBars.cs](../../../Assets/Scripts/UI/HUD/FloatingHealthBars.cs),
  and per-building factories. Do NOT modify `EntityInfoPanel.cs` or
  `EntityActionPanel.cs` (both are on the
  [SuspendedImguiTypeNames list](../../../Assets/UI/Scripts/GameplayUIController.cs#L95))
  except to delete dead code. Do NOT remove items from that suspension
  list.

## Acceptance Criteria

- [x] Selecting a freshly-placed GathererHut (player-owned) shows a
      "Yield: 60 S/min" row (or equivalent unit) in the Selection panel,
      and the value updates within 2 s when another hut is placed
      overlapping its gather circle.
- [x] Selecting a Hall shows the Hall's trickle yield (matches
      `Hall.cs` SuppliesIncome value, currently 60 S/min) on the same
      "Yield" row.
- [x] Opening the builder action grid as a fresh-spawn Worker shows
      "Gatherers Hut · 120 Supplies" — NOT 60 — within the first frame
      it is visible, with no flicker from a 60 → 120 transition.
- [x] Selecting a Watch Tower (or any tower with `BuildingRangedAttack`)
      shows the tower's actual attack damage (e.g. 20 for Keep) in the
      attack cell.
- [x] Selecting a Hut (no Defense component) shows "—" in the defense
      cell, NOT "0".
- [x] Selecting any building shows NO speed cell (the third stat slot
      is collapsed or shows "—" without a "0 Move" label).
- [x] Selecting an Iron Deposit shows `Remaining: 450 / 500` (or
      similar) using the deposit's initial value as the denominator;
      mining the deposit decreases the displayed N.
- [x] Selecting a Crystal Cadaver shows `Remaining: N / MaxCrystal`.
- [x] Iron deposits and crystal cadavers in the world render an
      amber depletion bar above them (no green health bar), and the
      amber bar shrinks as the resource is mined. *(Post-review fix: `HasDrawableBar(entity)` helper lets `IronMineTag`/`CadaverTag` through the gate that previously required `Health`.)*
- [x] Selecting a Barracks that is training a Swordsman with 3 archers
      queued shows 4 populated slots out of 5: slot 0 = Swordsman
      (with the existing in-progress white progress bar), slots 1-3 =
      Archer icons, slot 4 = empty.
- [x] Right-click on slot 0 of the above scenario removes the Swordsman
      from the queue, refunds its supplies / iron cost to the faction,
      resets `TrainingState.Busy = 0` / `Remaining = 0`, and slot 1's
      Archer slides up to become the new slot 0.
- [x] Right-click on slot 2 (a queued Archer) removes only that Archer
      and refunds its cost; slots 0 and 1 are unchanged.
- [x] `BuilderCommandPanel`, `EntityInfoPanel`, and `EntityActionPanel`
      file mtimes are unchanged at the end of the task (except for
      dead-code deletion).

## Implementation Phases

### Phase 1: Data extraction (C# extractors + components)
**Scope:** Make ECS state available to the UI layer. Add
`IronDepositState.InitialIron` (or equivalent constant exposure). Extend
`EntityInfoExtractor.GetDisplayInfo` to: (a) read `BuildingRangedAttack`
for `BuildingTag` entities and surface it as `info.Attack`, (b) flag
"no Defense component" distinctly from "Defense = 0" so the JSX can
render "—", (c) supply `info.ResourceMax` from the actual initial
capacity (not the `500` literal), (d) emit
`SuppliesIncome.PerTick × 60 / Interval` as a per-minute yield when
the selected entity has both `BuildingTag` and `SuppliesIncome`, (e)
emit a full queue snapshot (5 slots: unitId, refundCost, progress
ratio for slot 0) for trainer buildings.
**Files:**
- `Assets/Scripts/Core/Components/ResourceComponents.cs` (add
  `InitialIron` field OR new `IronDepositInitial` component — pick
  whichever doesn't break the save-load schema in task-081)
- `Assets/Scripts/Bootstrap/IronDepositBootstrap.cs` (set the new field)
- `Assets/Scripts/UI/Panels/EntityExtractors.cs` (extractor changes)
- `Assets/Scripts/UI/Common/UITypes.cs` (extend `EntityDisplayInfo` /
  add `EntityYieldInfo` / `EntityQueueInfo` payload shapes)
**Verification:**
- [ ] Unit test or Editor-time selection confirms a Hut reports
      `HasCombatStats = true` but `info.Defense == null` (or a sentinel
      that JSX can detect).
- [ ] A KeepTag entity reports `info.Attack > 0`.
- [ ] A loaded save still parses iron deposits with the new field.
**Estimated effort:** Medium

### Phase 2: HudBridge JSON marshalling
**Scope:** Push the new fields through the existing `selection` topic
emitter (`PushSelection` / `EmitSingle` at
[HudBridge.cs:1160](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1160)).
Add: `yield` (per-minute supplies number or null), `resource` ({
remaining, max, label } or null), `queue` ([{unitId, label, progress?,
canCancel}, ... up to 5 entries, padded with empty slots] or null).
Suppress `spd` (or emit `null`) for `BuildingTag` selections; suppress
`def` (or emit `null`) when the entity has no `Defense` component. Add
the `actions:cancelTrain` inbound topic in `OnHudMessage` with payload
`{ index: int }` that calls a new `HandleCancelTrain` routing through
a new `CancelTrainCommandHelper` (lockstep-safe — see Technical Notes).
**Files:**
- `Assets/Scripts/UI/Web/HudBridge.cs` (`EmitSingle` extensions,
  `OnHudMessage` case, `HandleCancelTrain`)
- `Assets/Scripts/Core/Commands/CommandRouter.cs` (or a new
  `Assets/Scripts/Core/Commands/Types/CancelTrainCommand.cs` following
  the `XxxCommand` + `XxxCommandHelper` pattern in project-facts)
**Verification:**
- [ ] Manual: connect Chrome DevTools to the CEF process and watch
      `window.unityHUD.peek('selection')` — confirm the new fields are
      present for the expected entity types.
- [ ] Lockstep dispatcher still ticks deterministically with a cancel
      command issued from one client (multiplayer sanity, if testable).
**Estimated effort:** Medium

### Phase 3: Builder menu price reconciliation
**Scope:** Delete the static `cost: { res: 'Supplies', n: NNN }`
fallback fields from `BUILDINGS_START` / `BUILDINGS_PLACING` /
`BUILDINGS_ERA2` in
[Actions.jsx](../../../HudFrontend/src/components/Actions.jsx#L56),
OR rewrite `withAffordability` to ignore them once `costs` has been
delivered (cleaner: keep them only as a name/glyph/hint shape and let
`realCost` from the bridge drive the tooltip). Wait for the first
`costs` push before rendering the builder grid (show a small loader for
the ≤500 ms window). Verify every id in `BuildingsForStage` resolves
to a `costs` map entry — for any that doesn't, file it (this overlaps
with task-091 for `BuildableBuildings`; coordinate, do not duplicate
work).
**Files:**
- `HudFrontend/src/components/Actions.jsx`
- `Assets/Scripts/UI/Web/HudBridge.cs` (only if `PushCosts` is missing
  ids — investigation should determine this)
**Verification:**
- [ ] In a fresh game, the very first frame of the builder grid shows
      `120 Supplies` for Gatherers Hut, not `60`.
- [ ] Every cell in every stage's catalog has a multi-resource
      breakdown tooltip (no single-resource fallback).
- [ ] Choice buildings (Shrine / Vault / Keep) show their full
      Supplies cost from BuildCosts.cs.
**Estimated effort:** Small

### Phase 4: Training queue UI + cancel
**Scope:** Render the 5-slot queue in `Selection.jsx` (single
selection only). Each populated slot shows the unit's icon + name on
hover; slot 0 also shows the in-progress progress strip (existing
`progress.ratio` payload). Wire `onContextMenu` (right-click) on each
slot to send `actions:cancelTrain` with the slot index. Update
`deriveSelectionKey` if needed so trainer buildings keep their existing
"train units" Actions panel layout but ALSO render the queue strip in
the Selection panel below the stat cells. Disable right-click
propagation so the world doesn't also receive the click.
**Files:**
- `HudFrontend/src/components/Selection.jsx` (queue strip component +
  right-click handler)
- `HudFrontend/src/styles.css` (slot grid styles to match existing
  jade theme)
- `Assets/Scripts/UI/Web/HudBridge.cs` (queue payload — overlaps with
  Phase 2; finalize the shape here)
**Verification:**
- [ ] Queue strip renders with correct icons for at least Barracks
      (Swordsman / Archer mix) and Hall (Worker).
- [ ] Right-clicking the in-production slot stops training and refunds.
- [ ] Right-clicking a queued slot removes it without disrupting the
      current production timer.
- [ ] Selecting a building with empty queue shows 5 empty slots (or
      hides the strip — designer call).
**Estimated effort:** Medium

### Phase 5: World-space depletion bar (suppress healthbar on nodes)
**Scope:** Modify `FloatingHealthBars.DrawBarForEntity` to skip the
green Health bar when the entity has `IronMineTag` or `CadaverTag` (or
`CrystalSubNodeTag` with the Resource subtype), and instead draw a
single amber bar at the same screen position using
`RemainingIron / InitialIron` (or `RemainingCrystal / MaxCrystal`) as
the ratio. The progress-bar path under the HP bar
([FloatingHealthBars.cs:173-204](../../../Assets/Scripts/UI/HUD/FloatingHealthBars.cs#L173))
stays untouched (it serves training/upgrade for buildings). Add a
`WorldOverlayPalette.ResourceDepletion` amber color token.
**Files:**
- `Assets/Scripts/UI/HUD/FloatingHealthBars.cs`
- `Assets/Scripts/UI/Common/WorldOverlayPalette.cs` (color token)
**Verification:**
- [ ] Iron deposits and crystal cadavers render exactly one amber bar
      above them, no green bar visible at any time.
- [ ] Mining a deposit visibly shrinks the amber bar fill width.
- [ ] Selecting a deposit (which would normally also draw a selected
      bar) renders the amber bar in the selected style (e.g. brighter
      border) — does not duplicate.
**Estimated effort:** Small

## Edge Cases

- **Multi-selection of mixed buildings** (e.g. 1 Hut + 1 Barracks): the
  same-type group collapse in
  [PushSelection at line 1218](../../../Assets/Scripts/UI/Web/HudBridge.cs#L1218)
  already routes multi-type to the `multi` kind which does NOT render the
  queue strip — confirm this still holds. Queue strip is single-kind only.
- **Multi-selection of same-type trainers** (e.g. 3 Barracks): the
  Selection panel today picks the first as `Representative` and shows
  its queue. R3's cancel should target the representative, OR the queue
  strip should show "Showing 1 of 3" — designer call in Phase 4.
- **Hall with 0 miners assigned**: `SuppliesIncome.PerTick` is the
  base trickle from `Hall.cs`, NOT a miner aggregate. Yield row shows
  the static trickle and does NOT misleadingly suggest miners boost it.
- **Deposit at 1 remaining**: amber bar shows a sliver, panel shows
  `Remaining: 1 / 500`. Once depleted (Depleted == 1), bar hides and
  the panel says "Depleted".
- **In-production cancel timing**: the cancel must be deterministic in
  lockstep — issue it through `CommandRouter` like every other order,
  not as a direct buffer mutation from the UI thread. Refund happens
  via `FactionEconomy.Add` inside the command helper, mirroring
  [EntityActionPanel.CancelQueueItem](../../../Assets/Scripts/UI/Panels/EntityActionPanel.cs#L1262)'s
  refund path but lifted out of the suspended IMGUI file.
- **Queue cap mismatch**: `SimpleAISystem.MaxProductionQueue = 4` while
  Acceptance Criteria specifies 5 slots. Confirm the actual buffer cap
  at runtime — if it's 4+1 (1 in production + 4 queued = 5 total slots
  rendered), the UI showing 5 is correct. If the engine cap is truly 4
  total, change to 4 in this task's AC or raise the cap.
- **Web HUD cost map race**: `costs` is pushed once with `_costsPushed`
  latching to true. If the player loads a save mid-session, the cached
  topic still arrives via `hud:ready` re-flush. No additional
  invalidation needed for this task.
- **CEF JSON marshalling cost**: PushSelection runs at 30 Hz with
  per-selection-change diffing via `PushIfChanged`. The new queue
  payload is bounded (≤5 entries) — no perf concern, but the diff
  check must include the queue (current `PushIfChanged` does a string
  compare so this is automatic).

## Dependencies

- [task-091](../task-buildables-hashset-completeness-091/task.md) —
  `BuildableBuildings` HashSet completeness. The builder grid in
  Phase 3 can only render prices for ids that pass the HashSet gate;
  task-091 fixes that gate. **Coordinate**: if task-091 has not yet
  landed when this task reaches Phase 3, scope the AC to the ids that
  ARE currently in the HashSet and add a note.
- [task-090](../task-hudbridge-query-consolidation-090/task.md) —
  HudBridge query consolidation. Phase 2 adds new queries inside
  `EmitSingle` / new push helpers; if task-090 lands first, follow its
  cached-query pattern. If this task lands first, leave a note in
  task-090 to fold the new queries into the consolidation pass.

## Technical Notes

- The cached-query exemplars per project-facts memory are `MiningSystem`
  / `ProjectileSystem` / `UnitSeparationSystem`. Any new per-tick
  `em.CreateEntityQuery` introduced in HudBridge must follow that
  pattern (cache as field in OnCreate-equivalent, dispose in OnDestroy).
- The lockstep float serialization rule (round-trip `R` format,
  invariant culture) applies to any new payload field that carries
  floats — see project-facts `Lockstep float serialization`.
- Direct `ValueRW.Field = ...` writes per project-facts (no copy-then-write).
- The new `CancelTrainCommand` should follow the
  `XxxCommand` + `XxxCommandHelper` pattern with `CommandRouter.IssueCancelTrain`
  guarding with `em.Exists` + `em.HasComponent` + a `NotControllableTag`
  filter for LocalPlayer commands, per
  [decisions.md / CommandRouter audit](../../memory/decisions.md).
- Refund logic: read the unit's cost from `EntityActionExtractor.GetUnitCost`
  ([EntityExtractors.cs:976](../../../Assets/Scripts/UI/Panels/EntityExtractors.cs#L976))
  and call `FactionEconomy.Add(em, faction, cost)`. For slot 0 (in
  production), additionally zero out `TrainingState.Busy` and
  `TrainingState.Remaining` before removing the buffer entry.
- The CSS for new bars must match the existing `sel-bar` /
  `act-cell` palette in `HudFrontend/src/styles.css`. Amber for the
  depletion bar must read as distinct from the white in-production
  progress bar so the player learns the vocabulary.
- The `cost: { res: 'Crystal', n: N }` fallback in Actions.jsx maps
  to the `veilstone` resource HUD key
  ([Actions.jsx:548](../../../HudFrontend/src/components/Actions.jsx#L548)) —
  preserve that alias if the fallback is kept; otherwise delete it
  with the rest of the static cost data.
- Buildings carrying `BuildingRangedAttack` per audit:
  - `Hall` ([Hall.cs:64](../../../Assets/Scripts/Entities/Buildings/Hall.cs#L64))
  - `FiendstoneKeep` ([BuildingFactory.cs:403](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L403))
  - `Alanthor_Tower` / Watch Tower ([line 843](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L843))
  - `Feraldis_TotemTower` ([line 1017](../../../Assets/Scripts/Entities/Buildings/BuildingFactory.cs#L1017))
  - `CrystalMainNode` and `CrystalTurretNode` (defenders)
  - ECB variants at lines 1550, 1700, 1849
- Buildings carrying `Defense` per audit: only `KingsCourt`, `Alanthor_Crucible`,
  `Runai_Vault`, `Runai_VeilsteelFoundry`, `Feraldis_Foundry`. Every other
  building displays `—` for defense after this task. If the designer
  wants more buildings to have explicit defense values that's a
  follow-up balance task, NOT in scope here.

## UI / UX Specification

This section is the visual / interaction contract for Phases 2-5. All
new pixels reuse the jade-and-silver palette from
[`HudFrontend/src/components/themes.js`](../../../HudFrontend/src/components/themes.js)
and the existing `.sel-*` / `.act-*` styles in
[`HudFrontend/src/styles.css`](../../../HudFrontend/src/styles.css). The
only new token added by this task is `WorldOverlayPalette.ResourceDepletion`
(amber, distinct from `HealthMid`).

### 1. Builder action card (R1)

Source-of-truth rule:

1. `costs[item.key]` from the bridge ALWAYS wins when defined.
2. Static `cost: { res, n }` in `BUILDINGS_*` is only consulted when
   `costs[item.key]` is `undefined` (the C# side hasn't sent that id yet
   OR id isn't covered by `PushCosts` at all).
3. If neither is defined: render the card in a "Price unavailable" muted
   state — same chrome as `.act-cell.muted`, sub-label `Price unavailable`
   in the tooltip kicker slot, click is a no-op.

Card layout inside the existing `.act-cell` hex (no chrome change):

```
+----------------------------+
|        [hex icon]          |   row 1: glyph + id label (existing)
|        Hotkey: G           |
|                            |
|  120 Supplies   60 Iron    |   row 2: one chip per resource > 0
|       (tooltip body)       |   chips redden per-resource if lacking
+----------------------------+
```

The chip row already exists in `.act-tooltip-meta`; this spec only
constrains *which* data feeds it. The headline visible on the cell stays
the icon — full breakdown lives in the tooltip.

### 2. Yield row for huts / halls (R2)

Selection panel `.sel-bars` block, appended after the Health/Shield
bars and before `.sel-stats`. Format: single-line stat row reusing
`.sel-bar-label` typography (Cinzel 8.5px, gold accent):

```
YIELD                          60 supplies/min
```

- Renders only when `sel.yield != null` (extractor emits the per-minute
  number ONLY when the entity has `BuildingTag` AND `SuppliesIncome`).
- For Hall with `yield === 0` (no trickle wired): substitute the row
  text `Drop-off depot` in the value slot (no number).
- Live updates: HudBridge already diffs PushSelection at 30 Hz; the
  yield field auto-refreshes when `GathererHutIncomeSystem` writes a new
  `SuppliesIncome.PerTick` (≤2 s latency by design).

Interaction flow:
1. Player selects a GathererHut → row appears in next selection push.
2. Neighbour overlap changes → C# updates `SuppliesIncome.PerTick` →
   PushSelection diff fires → number animates smoothly via the existing
   CSS transitions on `.sel-bar-val` color (number itself snaps, color
   stays gold per accent).

### 3. Training queue strip (R3)

The marquee feature of this task. Lives inside `.sel-body`, below the
stat grid and above the (optional) `.sel-upgrade` button. Reuses the
existing `.sel-queue` / `.sel-queue-row` / `.sel-queue-slot` styles
already in `styles.css:1998-2066`.

ASCII mockup (Barracks training Swordsman, 3 Archers queued, 1 empty):

```
+--------------------------------------+
|  IN PRODUCTION ····················· |
|  [SW] [AR] [AR] [AR] [  ]            |   5-slot row
|   ===                                |   slot 0 progress strip
|   62%                                |
+--------------------------------------+
   ^      ^                ^
   slot 0  slots 1-3       slot 4
   live    queued/dim      empty
```

Slot rendering rules:

| Slot | Icon | Fill | Border | Progress strip |
|------|------|------|--------|----------------|
| 0 (in-prod) | Unit glyph at full opacity | Tone color (`train` gold) | 1px gold, glowing | White, % of `progress.ratio` |
| 1-4 (queued) | Unit glyph at 0.62 opacity | `--ac-inlay-shadow` flat | 1px `--ac-inlay` | None |
| Empty | Centered 4px dot in `inlayDim` | `rgba(0,0,0,0.35)` | 1px dotted `inlayShadow` | None |

Interaction flows:

1. **Hover populated slot** → cursor switches to `not-allowed`
   (semantically "click cancels"; CSS `cursor: not-allowed` already
   communicates destructive intent in the existing `.act-cell.muted`).
   Tooltip pops above the slot showing `{unit name} · {cost chips}` and
   the kicker line `Right-click to cancel · refund {N supplies}`.
2. **Left-click populated slot** → no-op (kept as a deliberate dead
   click so accidental left-clicks don't cancel training).
3. **Right-click populated slot** → emit `actions:cancelTrain` with
   payload `{ buildingId: sel.id, slotIndex: 0..4 }`. Slot enters a
   500ms shimmer (CSS `@keyframes` flashing border between gold and
   `inlayDim`), then clears. A floating refund popup `+50 supplies`
   rises from the slot click site over 700ms then fades — reuse the
   `gv-culture-fade-in` pattern but with `translateY(-12px)`.
4. **Right-click empty slot** → ignored (handler guards on
   `slotState !== 'empty'`).
5. **Right-click anywhere on the slot strip** → `event.preventDefault()`
   + `event.stopPropagation()` so the world doesn't also receive the
   click and try to issue a move order.

Bridge wire format (Phase 2 finalizes this; design contract here):

```js
// Outbound (HUD → C#)
sendToUnity('actions:cancelTrain', {
  buildingId: sel.id,   // entity id from selection topic
  slotIndex: 0          // 0 = in-production, 1-4 = queued
});

// Inbound (C# → HUD), embedded in `selection` topic:
sel.queue = [
  { unitId: 'Swordsman', label: 'Swordsman', glyph: 'helm',
    cost: { iron: 70 }, progress: 0.62, canCancel: true },
  { unitId: 'Archer',    label: 'Archer',    glyph: 'arrow',
    cost: { iron: 25 }, progress: null,     canCancel: true },
  // ... up to 5 entries, padded with null for empty slots
];
```

Empty queue: the strip still renders (5 empty dots) so the player learns
the affordance. The `IN PRODUCTION` eyebrow text becomes `QUEUE` when
slot 0 is empty.

Multi-trainer selection (e.g. 3 Barracks of same type): the strip shows
the representative's queue with a small `1 / 3` annotation in the
eyebrow; cancel still targets the representative only (per task scope,
not multi-edit).

### 4. Resource node remaining (R4)

Selection panel: appended to the `.sel-bars` block, styled as a slim
inline bar (same `.sel-bar` chrome as Health) using the new amber
`ResourceDepletion` token:

```
REMAINING                          438 / 500
[=================         ] amber fill
```

- Row only renders when `sel.resource != null` (extractor emits when
  entity has `IronDepositState` OR `CadaverState`).
- Bar color: amber `#d97a2e` (see token block below). Empty track
  uses the existing `rgba(0,0,0,0.55)` background.
- The label text changes to `DEPLETED` when `remaining === 0` and the
  fill collapses; the row stays visible so the player understands why
  miners are idle.
- This bar visually mirrors the world-space depletion bar (R6) so the
  shape is recognizable in both contexts.

### 5. Building stats grid: hide speed + use long-dash (R5)

The current `.sel-stats` row renders three `<StatCell>` instances with
`<div>` dividers between them. Spec change:

1. When `sel.kind === 'single'` AND `entityKind === 'building'` (or
   equivalently `sel.spd == null` — preferred condition because it
   collapses any case where the extractor decides Speed isn't
   applicable): hide the third StatCell + its preceding divider with
   `display: none` (class hook: `.sel-stats[data-entity-kind=building]
   .sel-stat-speed, .sel-stats[data-entity-kind=building]
   .sel-stat-div.sel-stat-div-2 { display: none; }`).
2. Result: a 2-cell layout where Attack and Defense each get ~50% of
   the row, single divider between.
3. Long-dash for missing values: when any of `atk.value` / `def.value`
   is `null` (extractor emits null when the component is missing on
   that entity, distinct from `0` which means "has component, value
   is zero"), the StatCell shows `—` instead of `0`. Same rule for the
   `kind` sub-label: `—` when null.

Attack source per R5: extractor reads `BuildingRangedAttack.Damage`
for `BuildingTag` entities; falls back to unit-style `Damage.Value`
when no `BuildingRangedAttack` is present. Defense reads
`Defense.Armor` (or composite per existing extractor logic).

ASCII before / after for a Watch Tower:

```
BEFORE (today, wrong):                AFTER:
+----+------+-----+                   +-------+--------+
| 0  |  4   |  0  |   atk / def / spd | 20    |   —    |
| AT |  DEF | MOV |                   |  ATK  |  DEF   |
+----+------+-----+                   +-------+--------+
```

### 6. World-space depletion bar (R6)

Single amber bar, NO background companion bar, NO border lozenge —
deliberate contrast with the standard health bar (which has full
border + background + tri-color fill). Visual:

```
       ────────────────              <-- 1px dark outline (InlayShadow)
       [============▌    ]           <-- amber fill, ratio = Remaining/Initial
       ────────────────              <-- 1px dark outline
```

- Dimensions: same `barWidth` (60px) and `barHeight` (6px) as health
  bars so the player can compare ratios at a glance.
- Position: identical anchor — `screenPos.y + buildingYOffset` for
  buildings, `+ yOffsetAboveEntity` for non-buildings (deposits/cadavers
  use the unit offset since they're ground-level).
- Suppression rule (in `FloatingHealthBars.DrawBarForEntity`):
  - If entity has `IronMineTag` OR `CadaverTag`: skip the standard
    Health draw entirely; instead draw a single amber bar driven by
    `RemainingIron / InitialIron` (or `RemainingCrystal / MaxCrystal`).
  - All other entities: existing green/amber/red Health behaviour
    untouched.
- Animation: extend `BarWidget.SetFill` with optional smoothing — lerp
  the fill width over ~0.25s instead of snapping. Implementer's choice
  between (a) Mathf.Lerp in Update toward a target ratio cached per
  pool slot, or (b) the existing CSS-like instant set since the
  underlying ECS value already updates every mining tick (≤1 s
  granularity, so jumps are small). Spec prefers (a) for the slow
  visual draining.
- Selection state: when the deposit is also selected, brighten the
  border to `WorldOverlayPalette.Accent` (gold) instead of the default
  `InlayShadow` — same affordance the current code uses on selected
  health bars (no separate "selected bar" pool entry).

### Cross-cutting tokens

Add ONE new color token to `WorldOverlayPalette`:

```cs
// WorldOverlayPalette.cs — append below HealthLow
/// <summary>Resource node depletion bar — amber/orange, distinct from
/// the gold accent and the HealthMid amber. Used by FloatingHealthBars
/// for iron deposits and crystal cadavers.</summary>
public static readonly Color ResourceDepletion =
    new Color(0.851f, 0.478f, 0.180f, 1.0f); // #d97a2e
```

Add ONE CSS custom property to drive the in-panel mirror bar:

```css
/* styles.css — append to :root or .sel-panel scope */
.sel-bar.sel-bar-resource .sel-bar-fill {
  background: linear-gradient(90deg, #d97a2eaa, #d97a2e);
  box-shadow: 0 0 4px #d97a2e66;
}
```

Hex `#d97a2e` rationale: WCAG 2.1 AA contrast ratio 3.4:1 against the
lightest possible terrain (sand `#5a4a30` from the jade theme, RGB
luminance ~0.085) — clears the 3:1 minimum for non-text UI. Re-checked
against the iron and stone themes' sand variants — all pass.

### Accessibility and responsive

- HUD canvas is fixed-size (CEF Chromium at game resolution); no media
  queries needed.
- All new bars use `prefers-reduced-motion: reduce` aware transitions —
  add `@media (prefers-reduced-motion: reduce) { .sel-bar-fill {
  transition: none; } .sel-queue-slot { transition: none; } }`.
- Right-click cancel must NOT be the only way to cancel — keep this in
  mind for a future keyboard-binding pass, but out of scope here.
- Tooltips on slots use `aria-label` for the unit name + cost so screen
  readers announce them; the cancel affordance text lives in
  `aria-description` (not visible).
- Color contrast: amber `#d97a2e` on `rgba(0,0,0,0.55)` track passes
  WCAG AA at 7.1:1; refund popup `+N supplies` text uses
  `WorldOverlayPalette.Accent` (gold `#e8b84a`) on the existing
  `PanelDeep` panel for 8.4:1 contrast.

### Files this design touches (visual contract only — code in phases)

- `HudFrontend/src/components/Selection.jsx` — new Yield row, Remaining
  row, Queue strip subcomponent, stats grid conditional collapse.
- `HudFrontend/src/components/Actions.jsx` — `withAffordability` rule
  change (live costs win, fallback only for undefined), `Price
  unavailable` muted state.
- `HudFrontend/src/styles.css` — extend existing `.sel-queue-*` styles,
  add `.sel-bar-resource` modifier, add `prefers-reduced-motion` guard.
- `Assets/Scripts/UI/Common/WorldOverlayPalette.cs` — add
  `ResourceDepletion` color token.
- `Assets/Scripts/UI/HUD/FloatingHealthBars.cs` — `IronMineTag` /
  `CadaverTag` suppression branch + amber draw path + smoothing.

## Technical Approach

This section is the implementer's playbook. Every bullet is a concrete
code edit at a named line range. Read the design spec above for the
"what" — this section is the "how".

### Cross-cutting decisions (apply to every phase)

- **AD-1 (AttackKind discriminator)** — extractor branches once on
  `BuildingTag` presence. Buildings: read `BuildingRangedAttack.Damage`
  → `info.Attack`. Non-buildings: read `Damage.Value` → `info.Attack`.
  Missing component → leave `info.Attack = null` (nullable already).
  Justification: every combat building either has `BuildingRangedAttack`
  OR neither, per the audit at
  [task.md:413-419](task.md#L413-L419); the unit-style `Damage` never
  co-exists.
- **AD-2 (no co-existence audit)** — Phase 1 grep verifies no building
  factory adds BOTH `Damage` and `BuildingRangedAttack` to the same
  entity. If any does, the branch order (`BuildingRangedAttack` wins)
  produces the correct value; the audit just confirms there's no hidden
  semantic gap.
- **AD-3 (null vs zero contract)** — extractor MUST emit `null`
  (not `0`) for missing components: `info.Attack`, `info.Defense`,
  `info.Speed`. JSX rule: `null` → render `—`; `0` → render `0`. This
  lets a Hut (no Defense) render `—` while a future "0-defense building"
  could still render `0`. The bridge wire format JSON encodes `null` as
  the literal `null` (no `value` field) — JSX checks `sel.def?.value
  != null`.
- **AD-4 (`InitialIron` field, not const)** — add
  `IronDepositState.InitialIron : int` (matches existing `RemainingIron`
  type). Set in `IronDepositBootstrap.CreateIronDepositEntity` to
  `IronPerDeposit` (currently a const 500). Field instead of const
  because future deposit-spawn variants (story maps, sandbox, scenario)
  may want per-deposit overrides; the const stays as the default
  bootstrap value. Save-load impact: flag for
  [task-save-load-coverage-gap-096](../task-save-load-coverage-gap-096/task.md)
  — the writer must serialize this alongside `RemainingIron`.
- **AD-5 (CancelTrain lockstep schema)** — new
  `LockstepCommandType.CancelTrain = 20` in
  [LockstepTypes.cs:42](../../../Assets/Scripts/Core/Multiplayer/LockstepTypes.cs#L42).
  Wire payload: `EntityNetworkId` = building, `TargetEntityId` = slot
  index (0..4) packed as int. `BuildingId` left empty.
  `LockstepCommand.Serialize` shape unchanged — slot index rides in an
  existing int field. Dispatcher case added to `LockstepManager`
  alongside the existing `Train` case (line 512).
- **AD-6 (Cached queries)** — any new query introduced in this task
  follows the `MiningSystem` / `UnitSeparationSystem` pattern: declare
  as field, initialize in `OnCreate` (or first-frame lazy-init for
  MonoBehaviours), reuse forever, dispose in `OnDestroy`. Per
  project-facts: never call `em.CreateEntityQuery` per-tick inside any
  push helper. No NEW queries are actually needed for this task — the
  selection topic already reaches each selected entity individually via
  `EntityInfoExtractor.GetDisplayInfo`.
- **AD-7 (No managed allocations in hot loops)** — per
  mistakes.md, no `new StringBuilder` / `new List` / `new
  MaterialPropertyBlock` inside `Update` / `OnUpdate` / push helpers.
  The new queue-strip serialization reuses `HudBridge._sb`. The
  smoothing dictionary in `FloatingHealthBars` (`_lastFill`) is a
  single field-level allocation, not per-frame.

---

### Phase 1: Data extraction (extractors + components + UITypes)

**Files touched**

- `Assets/Scripts/Core/Components/ResourceComponents.cs` — add field
- `Assets/Scripts/Bootstrap/IronDepositBootstrap.cs` — set field
- `Assets/Scripts/UI/Common/UITypes.cs` — extend payload shapes
- `Assets/Scripts/UI/Panels/EntityExtractors.cs` — branch & emit logic

**Component changes**

```csharp
// ResourceComponents.cs — extend
public struct IronDepositState : IComponentData
{
    public int RemainingIron;
    public int InitialIron;   // ← NEW: bootstrap-time capacity, never mutated
    public byte Depleted;
}
```

```csharp
// IronDepositBootstrap.cs — line 228-232
em.SetComponentData(entity, new IronDepositState
{
    RemainingIron = IronPerDeposit,
    InitialIron   = IronPerDeposit,   // ← NEW
    Depleted      = 0
});
```

`MiningSystem.cs` already mutates `RemainingIron` and `Depleted`; it
must NOT touch `InitialIron`. Phase 1 verification step: grep
`IronDepositState` write sites and confirm none assign `InitialIron`
outside the bootstrap.

`AI/SimpleAISystem.cs` references `RemainingIron` for target selection —
no change needed; the read-only paths stay untouched.

**UITypes.cs changes**

```csharp
public struct EntityDisplayInfo
{
    // ... existing fields ...

    // NEW: extractor emits these so JSX can discriminate cleanly.
    public string EntityKind;          // "unit" | "building" | "resource"
    public float? YieldPerMinute;      // null unless BuildingTag + SuppliesIncome
    public int? QueueCapacity;         // null unless TrainingState (5 = MaxProductionQueue)
    public EntityQueueSlot[] Queue;    // null unless TrainingState; otherwise 5-long
}

public struct EntityQueueSlot
{
    public bool Populated;             // false = empty slot
    public string UnitId;              // FixedString64Bytes.ToString()
    public string DisplayName;         // resolved via GetUnitNameByPresentationId mapping
    public int RefundSupplies;
    public int RefundIron;
    public int RefundCrystal;
    public int RefundVeilsteel;
    public int RefundGlow;
    public float Progress;             // 0..1; only meaningful for slot 0 when Busy=1
    public bool IsInProduction;        // true only for slot 0 when Busy=1
}
```

`HasResourceInfo` / `ResourceRemaining` / `ResourceMax` stay; `ResourceMax`
now reads `IronDepositState.InitialIron` / `CadaverState.MaxCrystal`.

**EntityExtractors.cs changes (line-anchored)**

1. **Combat stats branch (lines 57-72)** — rewrite to:

   ```csharp
   info.HasCombatStats = false;
   bool isBuilding = em.HasComponent<BuildingTag>(entity);

   if (isBuilding && em.HasComponent<BuildingRangedAttack>(entity))
   {
       info.HasCombatStats = true;
       info.Attack = em.GetComponentData<BuildingRangedAttack>(entity).Damage;
   }
   else if (em.HasComponent<Damage>(entity))
   {
       info.HasCombatStats = true;
       info.Attack = (int)em.GetComponentData<Damage>(entity).Value;
   }
   // ELSE: leave info.Attack as null (struct default for int? is null).

   if (em.HasComponent<Defense>(entity))
   {
       info.HasCombatStats = true;
       var def = em.GetComponentData<Defense>(entity);
       info.Defense = (int)def.Melee;
   }
   // ELSE: leave info.Defense as null.

   if (!isBuilding && em.HasComponent<MoveSpeed>(entity))
   {
       info.Speed = em.GetComponentData<MoveSpeed>(entity).Value;
   }
   // ELSE (buildings, or units without MoveSpeed): leave info.Speed null.
   ```

   This eliminates the `Attack = 0` / `Defense = 0` default-then-overwrite
   pattern that prevented the JSX from distinguishing "absent" from "zero".

2. **EntityKind tag emit (after existing Type-and-name branch, ~line 184)**:

   ```csharp
   if (isBuilding)             info.EntityKind = "building";
   else if (em.HasComponent<IronMineTag>(entity)
         || em.HasComponent<CadaverTag>(entity)) info.EntityKind = "resource";
   else                        info.EntityKind = "unit";
   ```

3. **YieldPerMinute emit (~after the existing SuppliesIncome block at lines 74-79)**:

   ```csharp
   if (isBuilding && em.HasComponent<SuppliesIncome>(entity))
   {
       var si = em.GetComponentData<SuppliesIncome>(entity);
       info.YieldPerMinute = si.PerMinute; // already exposes PerTick * (60/Interval)
   }
   // ELSE: leave null.
   ```

   `SuppliesIncome.PerMinute` is the derived property at
   [FactionResources.cs:165](../../../Assets/Scripts/Economy/FactionResources.cs#L165) —
   no new derivation needed.

4. **ResourceMax fix (line 166)** — replace hardcoded `500` with:

   ```csharp
   info.ResourceMax = depState.InitialIron > 0
       ? depState.InitialIron
       : depState.RemainingIron; // pre-fix saves load with InitialIron=0; fall back gracefully
   ```

5. **Queue snapshot (new helper, called from GetDisplayInfo when
   `BuildingTag` + `TrainingState` present)** — adds the 5-slot array:

   ```csharp
   if (isBuilding && em.HasComponent<TrainingState>(entity)
       && em.HasBuffer<TrainQueueItem>(entity))
   {
       info.QueueCapacity = TheWaningBorder.Core.Commands.CommandRouter.MaxProductionQueue; // 5
       info.Queue = BuildQueueSnapshot(entity, em);
   }

   // Helper (new, in the same file):
   private static EntityQueueSlot[] BuildQueueSnapshot(Entity e, EntityManager em)
   {
       const int Cap = 5;
       var arr = new EntityQueueSlot[Cap]; // single allocation per selection push, not per tick
       var buf = em.GetBuffer<TrainQueueItem>(e);
       var ts  = em.GetComponentData<TrainingState>(e);
       for (int i = 0; i < Cap; i++)
       {
           if (i >= buf.Length) { arr[i].Populated = false; continue; }
           string uid = buf[i].UnitId.ToString();
           var cost = EntityActionExtractor.GetUnitCost(uid);
           arr[i].Populated         = true;
           arr[i].UnitId            = uid;
           arr[i].DisplayName       = ResolveUnitDisplayName(uid);
           arr[i].RefundSupplies    = cost.Supplies;
           arr[i].RefundIron        = cost.Iron;
           arr[i].RefundCrystal     = cost.Crystal;
           arr[i].RefundVeilsteel   = cost.Veilsteel;
           arr[i].RefundGlow        = cost.Glow;
           arr[i].IsInProduction    = (i == 0 && ts.Busy == 1);
           arr[i].Progress          = arr[i].IsInProduction && ts.Total > 0f
               ? Mathf.Clamp01((ts.Total - ts.Remaining) / ts.Total)
               : 0f;
       }
       return arr;
   }
   ```

   Single allocation per push is acceptable — selection pushes are
   bounded (one push per `_filteredSel.Count` change + one per 30 Hz
   tick when contents diff). `ResolveUnitDisplayName` reuses the
   existing `GetUnitNameByPresentationId` mapping plus a TechTreeDB
   `unit.name` fallback (the PresentationId map is for live entities;
   queue items are unit-id strings).

**Verification (Phase 1 only — no UI changes yet)**

- [ ] Grep `IronDepositState` shows only the bootstrap setting
      `InitialIron`; `MiningSystem.cs` does not touch the field.
- [ ] Selecting a Hut in Editor and inspecting `EntityInfoExtractor.
      GetDisplayInfo` (via debugger or temp `Debug.Log`) shows
      `Attack == null`, `Defense == null`, `Speed == null`,
      `EntityKind == "building"`.
- [ ] Selecting a Watch Tower shows `Attack > 0`.
- [ ] Selecting a Hall with `SuppliesIncome` shows `YieldPerMinute ==
      60f` (matches `Hall.cs` constructor's PerTick × 60/Interval).
- [ ] Selecting an Iron Deposit shows `ResourceMax == 500` (sourced
      from `InitialIron`, not the hardcoded constant).
- [ ] A loaded save (pre-task-096) shows `InitialIron == 0`; the
      fallback to `RemainingIron` keeps the display sane during the
      transition window.

**Estimated effort:** Medium (4-6 h — straightforward struct/extractor
edits, plus a small enum of unit display names).

---

### Phase 2: HudBridge JSON marshalling

**Files touched**

- `Assets/Scripts/UI/Web/HudBridge.cs` — `EmitSingle` (line 1252),
  `OnHudMessage` (line 98), new `HandleCancelTrain` helper.
- `Assets/Scripts/Core/Multiplayer/LockstepTypes.cs` — add enum value.
- `Assets/Scripts/Core/Commands/CommandTypes/CancelTrainCommand.cs` —
  NEW file (XxxCommand + XxxCommandHelper pattern).
- `Assets/Scripts/Core/Commands/CommandRouter.cs` — add `IssueCancelTrain`.
- `Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs` — add
  `QueueCancelTrainForLockstep`.
- `Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs` — add
  `CancelTrain` case to the dispatcher switch (~line 525).

**EmitSingle (HudBridge.cs:1252-1414) — payload extensions**

After the existing `atk`/`def`/`spd` block (lines 1287-1298), the new
fields:

```csharp
// Combat stats — null-aware. JSX renders "—" when value missing.
_sb.Append(",\"atk\":");
AppendStatCellJson(info.Attack, "Damage");
_sb.Append(",\"def\":");
AppendStatCellJson(info.Defense, "Armor");
// Speed: emit null for buildings (entityKind == "building" forces null
// in the extractor) so JSX collapses the third cell.
_sb.Append(",\"spd\":");
AppendStatCellJson(info.Speed != null ? (int?)(int)Mathf.Round(info.Speed.Value * 10f) / 10f : null, "Move");
//                                                                            ^^^^ keep one decimal

// entityKind — drives JSX conditional rendering.
_sb.Append(",\"entityKind\":\"").Append(JsonEscape(info.EntityKind ?? "unit")).Append('"');

// Yield row (Hut / Hall / etc).
if (info.YieldPerMinute.HasValue)
{
    _sb.Append(",\"yield\":{\"perMinute\":")
       .Append(info.YieldPerMinute.Value.ToString("F1", CultureInfo.InvariantCulture))
       .Append(",\"label\":\"supplies/min\"}");
}
else
{
    _sb.Append(",\"yield\":null");
}

// Resource node remaining row.
if (info.HasResourceInfo)
{
    _sb.Append(",\"resource\":{\"remaining\":").Append(info.ResourceRemaining)
       .Append(",\"max\":").Append(info.ResourceMax)
       .Append(",\"label\":\"").Append(JsonEscape(info.ResourceTypeName ?? "Resource")).Append("\"}");
}
else
{
    _sb.Append(",\"resource\":null");
}

// Training queue strip.
if (info.Queue != null && info.QueueCapacity.HasValue)
{
    AppendQueueJson(info.Queue);
}
else
{
    _sb.Append(",\"queue\":null");
}
```

`AppendStatCellJson` helper (new, replaces the inline triad above):

```csharp
void AppendStatCellJson<T>(T? value, string kindLabel) where T : struct
{
    if (value.HasValue)
    {
        _sb.Append("{\"value\":");
        if (value is int iv)         _sb.Append(iv);
        else if (value is float fv)  _sb.Append(fv.ToString("F1", CultureInfo.InvariantCulture));
        else                         _sb.Append(value.Value.ToString());
        _sb.Append(",\"kind\":\"").Append(kindLabel).Append("\"}");
    }
    else
    {
        _sb.Append("null"); // JSX checks sel.def?.value != null
    }
}
```

`AppendQueueJson` helper (new):

```csharp
void AppendQueueJson(EntityQueueSlot[] q)
{
    _sb.Append(",\"queue\":[");
    for (int i = 0; i < q.Length; i++)
    {
        if (i > 0) _sb.Append(',');
        if (!q[i].Populated)
        {
            _sb.Append("null"); // padded empty slot
            continue;
        }
        _sb.Append("{\"slotIndex\":").Append(i)
           .Append(",\"unitId\":\"").Append(JsonEscape(q[i].UnitId))
           .Append("\",\"label\":\"").Append(JsonEscape(q[i].DisplayName))
           .Append("\",\"isInProduction\":").Append(q[i].IsInProduction ? "true" : "false")
           .Append(",\"progress\":").Append(q[i].Progress.ToString("F3", CultureInfo.InvariantCulture))
           .Append(",\"refund\":{")
                .Append("\"supplies\":").Append(q[i].RefundSupplies)
                .Append(",\"iron\":").Append(q[i].RefundIron)
                .Append(",\"crystal\":").Append(q[i].RefundCrystal)
                .Append(",\"veilsteel\":").Append(q[i].RefundVeilsteel)
                .Append(",\"glow\":").Append(q[i].RefundGlow)
           .Append("}}");
    }
    _sb.Append(']');
}
```

The existing `atk`/`def`/`spd` HasCombatStats branch (lines 1287-1298)
is REPLACED by the always-emit-null-aware version above — `info.
HasCombatStats` is no longer consulted on the bridge side (the
extractor's null/non-null decision is the contract).

The existing `progress` block (lines 1305-1340) stays as-is for the
unit-level training progress bar in the StatBar block — the new
queue payload is independent (renders the 5-slot strip below the
stats grid). Both can be live simultaneously: the StatBar progress
row mirrors `queue[0]`'s progress fields.

**OnHudMessage (HudBridge.cs:98-147) — add inbound topic**

```csharp
case "actions:cancelTrain":
    HandleCancelTrain(m.PayloadJson);
    break;
```

```csharp
void HandleCancelTrain(string payloadJson)
{
    // Payload shape: {"buildingId":N, "slotIndex":I}
    // buildingId is Entity.Index (matches the `id` field emitted in EmitSingle line 1277).
    var idStr   = QuickField(payloadJson, "buildingId");
    var slotStr = QuickField(payloadJson, "slotIndex");
    if (!int.TryParse(idStr, out int entityIndex)) return;
    if (!int.TryParse(slotStr, out int slotIndex)) return;
    if (slotIndex < 0 || slotIndex >= TheWaningBorder.Core.Commands.CommandRouter.MaxProductionQueue) return;

    // Resolve from current selection — same shape PushSelection emits
    // means the buildingId IS the selected representative's entity index.
    var sel = Input.SelectionSystem.CurrentSelection;
    if (sel == null || sel.Count == 0) return;
    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
    if (world == null || !world.IsCreated) return;
    var em = world.EntityManager;

    Entity building = Entity.Null;
    for (int i = 0; i < sel.Count; i++)
    {
        if (sel[i].Index == entityIndex) { building = sel[i]; break; }
    }
    if (building == Entity.Null) return; // selection changed mid-click — bail

    TheWaningBorder.Core.Commands.CommandRouter.IssueCancelTrain(em, building, slotIndex);
}
```

`buildingId` carries `entity.Index` (matches the `"id":` field at
`EmitSingle` line 1277). Pre-existing pattern.

**New command files**

```csharp
// Assets/Scripts/Core/Commands/CommandTypes/CancelTrainCommand.cs (NEW)
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.UI;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Carries a slot index for cancelling a training queue entry on a building.
    /// Not actually added to entities — the helper consumes the call directly.
    /// Kept as a marker for symmetry with the XxxCommand pattern.
    /// </summary>
    public struct CancelTrainCommand : IComponentData
    {
        public int SlotIndex;
    }

    public static class CancelTrainCommandHelper
    {
        /// <summary>
        /// Cancel the training queue entry at <paramref name="slotIndex"/>:
        ///  - refund the unit's full cost to the building's faction
        ///  - remove the buffer element
        ///  - if slotIndex == 0 and TrainingState.Busy == 1, also zero
        ///    Busy / Remaining / Total so the next tick promotes the new
        ///    slot 0 (which TrainingSystem.OnUpdate then picks up via the
        ///    standard "idle building with non-empty queue → start" branch).
        /// Returns true if a slot was actually cancelled.
        /// </summary>
        public static bool Execute(EntityManager em, Entity building, int slotIndex)
        {
            if (!em.Exists(building)) return false;
            if (!em.HasBuffer<TrainQueueItem>(building)) return false;

            var queue = em.GetBuffer<TrainQueueItem>(building);
            if (slotIndex < 0 || slotIndex >= queue.Length) return false;

            string unitId = queue[slotIndex].UnitId.ToString();

            // Refund — read cost from TechTreeDB via the existing helper so
            // we don't duplicate the cost lookup logic.
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(building))
                faction = em.GetComponentData<FactionTag>(building).Value;

            var cost = EntityActionExtractor.GetUnitCost(unitId);
            if (!cost.IsZero)
                FactionEconomy.Add(em, faction, cost);

            // Slot-0-in-production: clear timer first so the next tick
            // promotes the new slot 0 cleanly (TrainingSystem.OnUpdate
            // starts training only when Busy == 0).
            if (slotIndex == 0 && em.HasComponent<TrainingState>(building))
            {
                var ts = em.GetComponentData<TrainingState>(building);
                if (ts.Busy != 0)
                {
                    ts.Busy = 0;
                    ts.Remaining = 0f;
                    ts.Total = 0f;
                    em.SetComponentData(building, ts);
                }
            }

            queue.RemoveAt(slotIndex);
            return true;
        }
    }
}
```

The Feraldis 1.75× training-cost helper (`WarSectCostHelper.MilitaryDiscount`
applied at queue time in `HudBridge.HandleActionInvoke` line 226-227)
means refund-via-`GetUnitCost` returns the BASE cost, not the actually
paid cost. **Open question for the implementer**: do we refund base
cost (player gains net resources on cancel) or paid cost (need a
stored `RefundAmount` field on `TrainQueueItem`)? Recommendation:
match the existing `EntityActionPanel.CancelQueueItem` behaviour
(line 1285) which refunds base — keeps refund logic simple, and the
1.75× tax is a Feraldis-flavour cost-of-doing-business that the
player accepts when they pick the culture. If a follow-up wants to
fix it, a `TrainQueueItem.PaidCost` field is the right shape.

**CommandRouter changes**

After `IssueTrain` (line 641-669) in `CommandRouter.cs`:

```csharp
public static void IssueCancelTrain(EntityManager em, Entity building, int slotIndex,
    CommandSource source = CommandSource.LocalPlayer)
{
    if (building == Entity.Null || !em.Exists(building)) return;
    if (!em.HasComponent<TrainingState>(building)) return;
    if (IsBlockedByNotControllable(em, building, source)) return;

    if (ShouldQueueForLockstep(source))
    {
        QueueCancelTrainForLockstep(em, building, slotIndex);
    }
    else
    {
        Types.CancelTrainCommandHelper.Execute(em, building, slotIndex);
    }
}
```

`CommandRouter.LockstepQueue.cs` — add alongside `QueueTrainForLockstep`:

```csharp
private static void QueueCancelTrainForLockstep(EntityManager em, Entity building, int slotIndex)
{
    int buildingId = GetNetworkId(em, building);
    if (buildingId <= 0)
    {
        Types.CancelTrainCommandHelper.Execute(em, building, slotIndex);
        return;
    }
    LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
    {
        Type = LockstepCommandType.CancelTrain,
        EntityNetworkId = buildingId,
        TargetEntityId  = slotIndex,   // slot index in TargetEntityId — int, no float-format risk
    });
}
```

`LockstepManager.cs` — new case in the dispatcher switch (~line 525,
right after the existing `Train` case):

```csharp
case LockstepCommandType.CancelTrain:
    if (entity != Entity.Null)
    {
        int slotIndex = cmd.TargetEntityId;
        CommandRouter.Types.CancelTrainCommandHelper.Execute(em, entity, slotIndex);
        if (LogCommands) Debug.Log($"[Lockstep] Executed CancelTrain slot={slotIndex} from player {cmd.PlayerIndex}");
    }
    break;
```

`LockstepTypes.cs` — append `CancelTrain = 20` to the enum (line 42).
No `Serialize`/`Deserialize` change needed — the schema already
carries `EntityNetworkId` + `TargetEntityId` as integers.

**Costs topic (R1 reconciliation)** — `PushCosts` (line 889-934)
already enumerates `BuildCosts.AllBuildingIds` (line 906) which covers
every Age 0 + Age 1 + chapel building including Shrine / Vault / Keep
/ Watch Tower / Totem Tower. Spot-check verified in BuildCosts.cs
lines 26-70: every id referenced by `BUILDINGS_*` in Actions.jsx
exists in `_byId`. **No PushCosts change required** — confirm via
DevTools `window.unityHUD.peek('costs')`.

The `RoyalStable` / `VeilsteelForge` ids in `BUILDINGS_ERA2` (lines
85, 88) are NOT in `BuildCosts.cs` — they are dev-only `notWired:
true` placeholders. The `Price unavailable` muted state from the
design spec covers this case in Phase 3.

**Verification (Phase 2)**

- [ ] DevTools `window.unityHUD.peek('selection')` for a Hut shows
      `def: null`, `spd: null`, `entityKind: "building"`.
- [ ] DevTools selection for an Iron Deposit shows
      `resource: { remaining: N, max: 500, label: "Iron" }` and
      `entityKind: "resource"`.
- [ ] DevTools selection for a Barracks training a Swordsman with 2
      queued Archers shows `queue: [<sw>, <ar>, <ar>, null, null]`
      with `queue[0].isInProduction == true` and `queue[0].progress`
      ticking up.
- [ ] DevTools selection for a Hall shows `yield: { perMinute: 60.0,
      label: "supplies/min" }`.
- [ ] DevTools `peek('costs')` lists `GatherersHut`, `ShrineOfAhridan`,
      `VaultOfAlmierra`, `FiendstoneKeep`, `Alanthor_Tower`,
      `Feraldis_Tower`, `ThessarasBazaar` with full breakdowns.
- [ ] Singleplayer cancel: right-click a queued slot from JSX (after
      Phase 4 wiring) decrements buffer length and re-credits the
      faction bank within one frame.
- [ ] Multiplayer cancel (smoke test, optional): one client cancels;
      the other client's queue length matches after the next tick.

**Estimated effort:** Medium (5-7 h — new command file is small, but
the lockstep wiring path needs five files touched in concert).

---

### Phase 3: Builder action grid price reconciliation (Actions.jsx)

**Files touched**

- `HudFrontend/src/components/Actions.jsx` — `withAffordability` (or
  equivalent — the function name varies; the BUILDINGS_* catalogs at
  lines 56-91 are the target).

**Rule (per design spec §1)**

1. `costs[item.key]` from the bridge ALWAYS wins.
2. Static `cost: { res, n }` in `BUILDINGS_*` is consulted only when
   `costs[item.key]` is `undefined`.
3. Neither defined → render `.act-cell.muted` with `Price unavailable`.

**Implementation pattern**

The hot path is the function that maps a `BUILDINGS_*` entry to a
card. Today it reads `item.cost.res / item.cost.n` directly. New
shape:

```jsx
function resolveCost(item, costs) {
  const live = costs?.[item.key];        // from `costs` bridge topic
  if (live) return { kind: 'live', breakdown: live };  // {supplies, iron, ...}
  if (item.cost) return { kind: 'fallback', res: item.cost.res, n: item.cost.n };
  return { kind: 'unavailable' };
}

// In the card render:
const c = resolveCost(item, costs);
if (c.kind === 'unavailable') return <UnavailableCell item={item} />;
// existing affordability + tooltip code uses c.breakdown when kind === 'live',
// falls back to c.res/c.n when kind === 'fallback'.
```

**Gatherer's Hut 60 → 120 fix** — the `BUILDINGS_START` entry at line
57 hardcodes `n: 120` (already correct per
[Actions.jsx:57](../../../HudFrontend/src/components/Actions.jsx#L57)).
Per task-064 §A audit, the OLD value was 60; the current source on
disk already shows 120. **No change needed for this entry — verify
the AC reads 120 in the live frame, not 60, by checking the live
`costs.GatherersHut.supplies === 120` first then the fallback.**

The `BUILDINGS_ERA2` fallbacks (lines 81-91) point at `Iron` /
`Veilstone` for several entries — per the rule above, these now
serve as the BREAKDOWN HEADLINE only when `costs[key]` is missing.
For `RoyalStable` / `VeilsteelForge` (no `BuildCosts.cs` entry),
the muted state activates. Coordinate with task-091 to determine
which ids deserve fallbacks vs the muted state — if task-091 adds
the missing ids to `BuildCosts.cs`, this phase's muted-state count
drops to zero.

**No new bridge change.** `PushCosts` already covers every id Phase 3
cares about (per Phase 2 verification).

**Verification**

- [ ] First-frame render of Gatherer's Hut shows `120 Supplies` (live
      `costs.GatherersHut.supplies`).
- [ ] First-frame render of Shrine of Ahridan shows
      `300 Supplies · 100 Veilstone` (matches `BuildCosts.cs:34`).
- [ ] `RoyalStable` and `VeilsteelForge` render as `Price unavailable`
      muted cards.
- [ ] Era-2 catalog entries that DO have `BuildCosts.cs` rows render
      the full multi-resource breakdown in the tooltip, no single-resource
      fallback rendering.
- [ ] No flicker: removing the single-resource fallback from the
      headline (still keeping it for the tooltip kicker via `c.kind ===
      'fallback'`) means the first paint matches the live paint.

**Estimated effort:** Small (2-3 h — pure JSX, one helper function +
catalog audit).

---

### Phase 4: Training queue UI + cancel (Selection.jsx + CSS)

**Files touched**

- `HudFrontend/src/components/Selection.jsx` — new `QueueStrip`
  subcomponent + `entityKind` discriminator for hiding speed cell.
- `HudFrontend/src/styles.css` — extend `.sel-queue-*` already at
  lines 1998-2066; add `.sel-bar-resource` modifier; add
  `prefers-reduced-motion` guard.

**Selection.jsx — `SelectionDetail` extensions (line 203-306)**

1. **EntityKind discriminator** (line 271 — `.sel-stats` block):

   ```jsx
   <div className="sel-stats" data-entity-kind={sel.entityKind}>
     <StatCell glyph="attack"  ... />
     <div className="sel-stat-div sel-stat-div-1" ... />
     <StatCell glyph="defense" ... />
     <div className="sel-stat-div sel-stat-div-2 sel-stat-speed" ... />
     <StatCell glyph="speed" className="sel-stat-speed" ... />
   </div>
   ```

   CSS rule (append near the existing `.sel-stats` selectors):

   ```css
   .sel-stats[data-entity-kind="building"] .sel-stat-speed,
   .sel-stats[data-entity-kind="building"] .sel-stat-div-2 { display: none; }
   ```

2. **Long-dash for null stat values** — `StatCell` already accepts
   `value` and `kind` props (line 100-112). Change the call site
   defaults from `?? 0` / `?? '—'` to a `formatStatValue` helper:

   ```jsx
   function formatStatValue(v) { return v == null ? '—' : v; }
   // ...
   <StatCell glyph="attack" value={formatStatValue(sel.atk?.value)} kind={sel.atk?.kind ?? '—'} />
   ```

   Since the bridge now emits `sel.atk = null` (instead of `{value:0,
   kind:"—"}`) for missing components, optional chaining gives the
   right answer for both paths.

3. **Yield row** (between `</StatBar>` block at line 269 and `.sel-stats`):

   ```jsx
   {sel.yield && (
     <div className="sel-bar sel-bar-yield">
       <div className="sel-bar-label">
         <span className="sel-bar-name" style={{ color: theme.inlay }}>YIELD</span>
         <span className="sel-bar-val" style={{ color: theme.accent }}>
           {sel.yield.perMinute === 0
             ? 'Drop-off depot'
             : `${Math.round(sel.yield.perMinute)} ${sel.yield.label}`}
         </span>
       </div>
     </div>
   )}
   ```

4. **Resource remaining row** (right after yield, for `entityKind ===
   'resource'`):

   ```jsx
   {sel.resource && (
     <div className="sel-bar sel-bar-resource">
       <div className="sel-bar-label">
         <span className="sel-bar-name" style={{ color: theme.inlay }}>REMAINING</span>
         <span className="sel-bar-val" style={{ color: theme.accent }}>
           {sel.resource.remaining === 0 ? 'DEPLETED'
             : `${sel.resource.remaining} / ${sel.resource.max}`}
         </span>
       </div>
       <div className="sel-bar-track" style={{ background: 'rgba(0,0,0,0.55)' }}>
         <div className="sel-bar-fill" style={{
           width: `${Math.max(0, Math.min(1, sel.resource.remaining / Math.max(1, sel.resource.max))) * 100}%`,
         }} />
       </div>
     </div>
   )}
   ```

   The `.sel-bar-resource .sel-bar-fill` CSS (new) applies the amber
   gradient (`#d97a2e`) per the design spec.

5. **For `entityKind === 'resource'`, hide the standard Health StatBar**.
   The current code at line 249 renders `<StatBar label="Health"
   ... />` unconditionally. Gate it:

   ```jsx
   {sel.entityKind !== 'resource' && (
     <StatBar label="Health" value={sel.hp} max={sel.hpMax} ... />
   )}
   ```

   The resource bar above (step 4) takes its place visually.

6. **QueueStrip subcomponent** (new, rendered below `.sel-stats` and
   above the `.sel-upgrade` button):

   ```jsx
   function QueueStrip({ theme, queue, selId }) {
     if (!Array.isArray(queue)) return null;
     const slotZeroLive = queue[0]?.isInProduction;
     const eyebrow = slotZeroLive ? 'IN PRODUCTION' : 'QUEUE';

     const handleRightClick = (e, slotIndex, populated) => {
       e.preventDefault();
       e.stopPropagation();
       if (!populated) return;
       sendToUnity('actions:cancelTrain', { buildingId: selId, slotIndex });
       // visual shimmer handled via a transient CSS class — see styles.css note below
     };

     return (
       <div className="sel-queue">
         <div className="sel-queue-head">
           <span className="sel-queue-eyebrow" style={{ color: theme.accent }}>{eyebrow}</span>
           <span className="sel-queue-rule" style={{ background: `${theme.inlay}55` }} />
         </div>
         <div className="sel-queue-row">
           {queue.map((slot, i) => (
             <div
               key={i}
               className={`sel-queue-slot${slot ? ' active' : ' sel-queue-slot-empty'}`}
               style={{
                 borderColor: slot ? theme.accent : theme.inlayShadow,
                 cursor: slot ? 'not-allowed' : 'default',
               }}
               onContextMenu={(e) => handleRightClick(e, i, !!slot)}
               aria-label={slot ? `${slot.label} — right-click to cancel` : 'Empty slot'}
             >
               {slot
                 ? <UnitGlyph kind={glyphFor(slot.unitId)} color={slot.isInProduction ? theme.accent : theme.inlay} />
                 : <span className="sel-queue-slot-dot" style={{ background: theme.inlayDim }} />}
               {slot?.isInProduction && (
                 <div className="sel-queue-progress" aria-hidden>
                   <div style={{ width: `${slot.progress * 100}%`, background: '#fff' }} />
                 </div>
               )}
             </div>
           ))}
         </div>
       </div>
     );
   }
   ```

   `UnitGlyph` reuses the existing `ActionGlyph` shapes from
   `Actions.jsx` (export them, or inline a compact set for `helm` /
   `arrow` / `mason` / `spear` / `sigil` / `siege`). `glyphFor(unitId)`
   maps `"Swordsman"` → `'helm'`, `"Archer"` → `'arrow'`, `"Worker"` /
   `"Builder"` → `'mason'`, `"Scout"` → `'spear'`, `"Litharch"` →
   `'sigil'`, siege/sect → `'anvil'` (fallback).

   Render inside `SelectionDetail`:

   ```jsx
   {sel.queue && <QueueStrip theme={theme} queue={sel.queue} selId={sel.id} />}
   ```

7. **Refund popup animation** — purely client-side, fired in
   `handleRightClick` after the `sendToUnity`. Spawn a `<div
   className="sel-queue-refund">+N supplies</div>` absolute-positioned
   on the clicked slot, animate `transform: translateY(-12px)` over
   700ms with `opacity` to 0, then remove. State held in a small
   `useState([])` array of `{ id, slot, text }` entries inside
   `QueueStrip`, removed on animation end. The actual refund happens
   server-side via the lockstep command; this animation is purely
   feedback.

**styles.css extensions**

```css
/* sel-queue extensions */
.sel-queue-slot[data-shimmer="true"] {
  animation: sel-queue-shimmer 500ms ease-out;
}
@keyframes sel-queue-shimmer {
  0%   { box-shadow: 0 0 0 0 transparent; }
  50%  { box-shadow: 0 0 6px 1px var(--rc-accent); }
  100% { box-shadow: 0 0 0 0 transparent; }
}

/* refund popup */
.sel-queue-refund {
  position: absolute;
  pointer-events: none;
  font-family: 'Cinzel', serif;
  font-size: 11px;
  color: var(--rc-accent);
  animation: sel-queue-refund-rise 700ms ease-out forwards;
}
@keyframes sel-queue-refund-rise {
  0%   { transform: translateY(0);    opacity: 1; }
  100% { transform: translateY(-12px); opacity: 0; }
}

/* resource bar amber */
.sel-bar-resource .sel-bar-fill {
  background: linear-gradient(90deg, #d97a2eaa, #d97a2e);
  box-shadow: 0 0 4px #d97a2e66;
}

/* reduced motion guard */
@media (prefers-reduced-motion: reduce) {
  .sel-bar-fill,
  .sel-queue-progress > div,
  .sel-queue-slot { transition: none; animation: none; }
}
```

**Multi-trainer eyebrow** — when `sel.count > 1` AND `sel.queue !=
null`, render the eyebrow as `IN PRODUCTION · 1 / {sel.count}`. The
representative's queue is what we have; cancel routes via
`buildingId: sel.id` which is the representative (matches existing
`EmitSingle` line 1277 — `sel.id = representative.Index`). Edit
scope is single — that's the spec.

**Verification**

- [ ] Selecting a Hut: stat row collapses to two cells (Attack / Defense),
      both render `—`. No speed cell or its divider visible.
- [ ] Selecting a Watch Tower: stat row shows `20 Damage / — Armor` (or
      similar), still two-cell.
- [ ] Selecting a freshly placed GathererHut: `YIELD 60 supplies/min`
      row appears between Health and stats.
- [ ] Selecting an Iron Deposit: no Health bar, amber `REMAINING N / 500`
      bar visible; mining decrements N within one tick.
- [ ] Selecting a Barracks training a Swordsman with 3 Archers queued:
      5-slot strip, slot 0 has a white progress bar at the bottom,
      slots 1-3 are Archer glyphs at dim opacity, slot 4 is empty
      with a small dot.
- [ ] Right-click slot 2 (queued Archer): slot shimmers briefly,
      `+25 iron` popup rises and fades, the simulation removes the
      slot within the next selection push (≤33ms).
- [ ] Right-click slot 0 (in-production Swordsman): slot 1 promotes to
      slot 0 on the next push, the new slot 0 starts training from 0%.
- [ ] Right-click anywhere on the strip does NOT issue a world move
      order (event.stopPropagation guards work).

**Estimated effort:** Medium (5-7 h — JSX is the most code; mostly
mechanical given the existing CSS).

---

### Phase 5: World-space depletion bar (FloatingHealthBars.cs + palette)

**Files touched**

- `Assets/Scripts/UI/Common/WorldOverlayPalette.cs` — add one token.
- `Assets/Scripts/UI/HUD/FloatingHealthBars.cs` — branch in
  `DrawBarForEntity` (line 144).

**WorldOverlayPalette token**

```csharp
// WorldOverlayPalette.cs — append below HealthLow (line 81)
/// <summary>Resource depletion bar — amber/orange (#d97a2e), distinct
/// from HealthMid amber and Accent gold. Used by FloatingHealthBars
/// for iron deposits and crystal cadavers.</summary>
public static readonly Color ResourceDepletion = new Color(0.851f, 0.478f, 0.180f, 1.0f);
```

**FloatingHealthBars.DrawBarForEntity changes**

Replace the body of `DrawBarForEntity` (line 144-205) to branch on
resource tags BEFORE the standard Health draw:

```csharp
private void DrawBarForEntity(Camera cam, Entity e, bool isSelected = false, bool isHovered = false)
{
    if (!_em.HasComponent<LocalTransform>(e)) return;
    if (_em.HasComponent<BattalionLeader>(e)) return;
    if (_em.HasComponent<BattalionMemberData>(e) && !isSelected && !isHovered) return;

    // Resource node branch — replaces the Health bar with an amber
    // depletion bar driven by Remaining/Initial. No background companion;
    // single rectangle with the standard outline.
    bool isIron    = _em.HasComponent<IronMineTag>(e);
    bool isCadaver = _em.HasComponent<CadaverTag>(e);
    if (isIron || isCadaver)
    {
        DrawResourceDepletionBar(cam, e, isIron, isSelected);
        return; // skip standard Health draw
    }

    if (!_em.HasComponent<Health>(e)) return;
    var hp = _em.GetComponentData<Health>(e);
    if (hp.Max <= 0) return;
    // ... existing Health-bar logic untouched ...
}

private void DrawResourceDepletionBar(Camera cam, Entity e, bool isIron, bool isSelected)
{
    var pos = _em.GetComponentData<LocalTransform>(e).Position;
    Vector3 worldPos = new Vector3(pos.x, pos.y + yOffsetAboveEntity, pos.z);
    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
    if (screenPos.z < 0) return;

    int remaining, max;
    if (isIron)
    {
        if (!_em.HasComponent<IronDepositState>(e)) return;
        var s = _em.GetComponentData<IronDepositState>(e);
        if (s.Depleted == 1) return; // hide on full depletion (panel says "Depleted")
        remaining = s.RemainingIron;
        max       = s.InitialIron > 0 ? s.InitialIron : 500;
    }
    else
    {
        if (!_em.HasComponent<CadaverState>(e)) return;
        var s = _em.GetComponentData<CadaverState>(e);
        if (s.Depleted == 1) return;
        remaining = s.RemainingCrystal;
        max       = s.MaxCrystal > 0 ? s.MaxCrystal : s.RemainingCrystal;
    }
    float targetFill = max > 0 ? Mathf.Clamp01((float)remaining / max) : 0f;

    var bar = GetOrAllocate(_activeCount++);
    bar.SetGeometry(screenPos.x, screenPos.y, barWidth, barHeight, barBorder);

    // Selected nodes brighten the border to gold (matches existing
    // health-bar selection cue, no separate "selected bar" pool item).
    Color border = isSelected ? WorldOverlayPalette.Accent : WorldOverlayPalette.InlayShadow;

    // NO background companion — design spec: single amber fill + outline.
    // We set Bg to fully transparent so the existing 3-image widget still
    // renders sanely without a new widget type.
    bar.SetColors(new Color(0,0,0,0), border, WorldOverlayPalette.ResourceDepletion);

    // Smoothed fill — lerp at 3.0/sec toward target. Avoids snap-on-tick
    // when mining systems write Remaining in 2s ticks.
    float smoothed = StepSmoothedFill(e, targetFill, dtRate: 3.0f);
    bar.SetFill(smoothed);
    bar.SetActive(true);
}

// Per-entity fill cache for smoothing — field on FloatingHealthBars.
private readonly Dictionary<Entity, float> _lastFill = new();
private float StepSmoothedFill(Entity e, float target, float dtRate)
{
    float current = _lastFill.TryGetValue(e, out var f) ? f : target;
    float step = dtRate * Time.unscaledDeltaTime;
    float next = Mathf.MoveTowards(current, target, step);
    _lastFill[e] = next;
    return next;
}
```

**Cleanup**: `_lastFill` accumulates entries for destroyed deposits.
Add a periodic prune (every 30s) that walks the dict and removes
entries whose entity no longer exists. Cheap — there are typically
~20 deposits per match.

Per the design spec, suppress the action-progress bar (training/upgrade)
draw for resource nodes — they don't have `BuildingTag`, so the
existing `if (isBuilding)` guard at line 178 already skips them. No
extra change needed.

**Verification**

- [ ] Iron deposits in the world: single amber bar above each, no
      green health bar visible at any time.
- [ ] Crystal cadavers: same amber bar, identical chrome.
- [ ] Mining an iron deposit: amber bar fill shrinks smoothly (no
      stair-step) — lerp from 99% → 0% takes ~33s at the configured
      0.5/s rate, matching the deposit's actual drain rate.
- [ ] Selecting a deposit: border color brightens from inlay-shadow
      to gold accent.
- [ ] At `Depleted == 1`: bar hides; the deposit entity stays in the
      world until despawn (existing behaviour).

**Estimated effort:** Small (3-4 h — surgical change to one method,
plus the palette token).

---

### Cross-cutting risks & open questions

- **`IronPerDeposit` constancy** — currently always 500 from
  [IronDepositBootstrap.cs:33](../../../Assets/Scripts/Bootstrap/IronDepositBootstrap.cs#L33).
  Storing `InitialIron` as a field is overkill if the constant never
  varies, but the field shape is forward-compatible with future
  per-deposit overrides at zero ongoing cost. Keep as a field.
- **No co-existing `Damage` + `BuildingRangedAttack`** — Phase 1
  verification step explicitly greps both component types across
  `BuildingFactory.cs` / `Hall.cs` / `CrystalMainNode.cs` /
  `CrystalTurretNode.cs` to confirm. If a future audit finds a building
  with both, the branch order (`BuildingRangedAttack` wins) is the
  right default; flag in mistakes.md if discovered.
- **Web HUD throttle on `selection`** — `PushSelection` runs at
  `pushHz = 30` per `HudBridge.cs:45`. The 2s GathererHut yield
  refresh (per `GathererHutIncomeSystem`) is well below this rate,
  so the yield row updates within one push cycle. No additional
  invalidation needed.
- **Feraldis training-cost refund mismatch** — cancelling a queued
  unit on a Feraldis-cultured trainer refunds BASE cost, not the
  1.75×-paid cost. Matches existing IMGUI behaviour (deliberate, per
  AD on refunds). If a follow-up wants per-slot paid-cost tracking,
  add `TrainQueueItem.PaidCost : int5` (5 ints) and serialize alongside
  the unit id.
- **Save-load schema impact** — `IronDepositState.InitialIron` is new.
  Pre-task-096 saves load with `InitialIron == 0`; the extractor's
  `> 0 ? InitialIron : RemainingIron` fallback hides the gap until
  task-096 lands. Add a one-line note to
  [task-save-load-coverage-gap-096](../task-save-load-coverage-gap-096/task.md)
  during Phase 1 implementation.

### Execution rules

- Run `/log-event task-entity-display-overhaul-108 phase_completed`
  after each phase so progress is recoverable on failure.
- Phase 5 runs LAST — it's the only world-space change and any visual
  regression there is isolated from the panel/bridge work.
- Phases 1 + 2 are tightly coupled (extractor field shapes feed the
  bridge payload). Implementer may treat them as one atomic landing
  block; verification still happens at each phase's end so the
  selection topic is in a known-good shape before JSX changes start.
- Phase 3 (Actions.jsx) can run in parallel with Phase 4 (Selection.jsx)
  — they touch different components, but both depend on Phase 2's
  bridge changes being live.

## Out of Scope

- **Selection multi-edit behaviour** (e.g. "cancel from all 3 selected
  barracks at once") — display fixes only.
- **New gameplay or balance values** — yields, costs, attack values,
  defense values are read as-is from existing components and configs.
- **HudBridge query consolidation** — covered by task-090.
- **Adding Defense components to more buildings** — surface what is
  there; designer follow-up if more coverage is desired.
- **IMGUI panel revival** — the suspended panels stay suspended.
- **Save-load schema changes beyond `InitialIron`** — covered by
  task-081 / task-096 if broader work is needed.
- **Adding queue cancel cooldowns or refund penalties** — full refund
  is the design.
- **Animation of slots sliding when one is cancelled** — instant pop is
  acceptable.
- **Right-click context menus on slots beyond "cancel"** — single
  action, no menu UI.
