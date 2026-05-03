---
deft:
  id: task-deepdive-ui-panels-2026-060
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [code-quality, code-review, deep-dive, ui]
---

# UI Panels / Menus / HUD / Observer / Selection deep-dive

## Context

Companion to task-052..059. Same method.

## TL;DR

UI is large but well-organized around `Styles.cs`. Observer-mode gating is consistent across command-issuing panels. **Five real missing-brace bugs found** — three with material runtime impact:

- **F-1 HIGH (also task-053 F-4)**: `SelectionSystem.FilterBoxSelection` military filter is broken — `military.Add(e)` runs unconditionally so box-selecting mixed economic+military returns ALL units instead of just military.
- **F-2 MEDIUM**: `InGameMenuPanel.Toggle()` can never close the menu — `Open()` always runs after `Close()`. Mitigated for ESC by direct close-call elsewhere, but the ResourceHUD "Menu" button can't toggle off.
- **F-3 MEDIUM**: `SpellPanel` can't cancel an active spell — `Cancel` then immediately `BeginTargeting` again.

Plus 9 lower-severity findings (UI per-frame allocations confirmed at ~9 sites in EntityActionPanel + 7 other panels; volume slider doesn't apply mid-session; quit-to-menu cleanup is shallow; "Free For All" lobby button is a duplicate of Circle).

## Acceptance Criteria

- [ ] F-1: Add braces to SelectionSystem.cs:463-465 (also closes task-053 F-4)
- [ ] F-2: Add braces to InGameMenuPanel.cs:67-72
- [ ] F-3: Add braces to SpellPanel.cs:173-175
- [ ] F-4..F-6: Add braces to the 3 cosmetic missing-brace sites
- [ ] F-8: Cache GUIStyle allocations in EntityActionPanel and other panels (single sweep PR matches W-1 task-051)
- [ ] F-10: OptionsMenuUI volume slider applies live

---

## Verified Findings

### F-1 — `SelectionSystem.FilterBoxSelection` military filter is broken (HIGH)
**File:** [SelectionSystem.cs:463-465](Assets/Scripts/Input/SelectionSystem.cs#L463-L465)
**Severity:** High — duplicate of task-053 F-4, persistent UX bug

```cs
if (cls == UnitClass.Economy || cls == UnitClass.Miner)
    economic.Add(e);
    military.Add(e);   // ← runs unconditionally
```

Every entity gets pushed into `military`, so the post-filter that should drop economic/miner units when soldiers are present (line 468-472) never filters them out. Box-dragging across a base with miners + a few archers selects miners along with archers.

### F-2 — `InGameMenuPanel.Toggle()` can never close the menu (MEDIUM)
**File:** [InGameMenuPanel.cs:67-72](Assets/Scripts/UI/HUD/InGameMenuPanel.cs#L67-L72)
**Severity:** Medium — UX bug, mitigated

```cs
public static void Toggle()
{
    if (IsOpen)
        Close();
        Open();   // ← always runs
}
```

When called with `IsOpen=true`, Close() sets `IsOpen=false`, then Open() re-opens. Mitigated for ESC because RTSInputManager.cs:94-99 short-circuits to Close() when menu is open, but the ResourceHUD "Menu" button (ResourceHUD.cs:373) goes through Toggle — so clicking it while open does nothing visible. User must use in-panel Resume button.

### F-3 — `SpellPanel` cannot cancel active spell (MEDIUM)
**File:** [SpellPanel.cs:173-175](Assets/Scripts/UI/HUD/SpellPanel.cs#L173-L175)
**Severity:** Medium — UX

```cs
if (isActive)
    castSystem.CancelTargeting();
    castSystem.BeginTargeting(humanFaction, spell);  // ← always runs
```

Clicking an active spell's icon to cancel calls Cancel then immediately re-enters targeting. User's mental model ("click again to cancel") doesn't work.

### F-4 — `EntityActionPanel.DrawBuildingPlacementPanel` shows both labels (LOW)
**File:** [EntityActionPanel.cs:211-213](Assets/Scripts/UI/Panels/EntityActionPanel.cs#L211-L213)
**Severity:** Low — visual noise

While placing, both "Left-click to place..." and "Build Structure" labels render stacked.

### F-5 — `MultiplayerLobbyUI.DrawNetworkSlot` host color cycles twice per click (LOW)
**File:** [MultiplayerLobbyUI.cs:396-399](Assets/Scripts/UI/Menus/MultiplayerLobbyUI.cs#L396-L399)
**Severity:** Low

```cs
if (isHost)
    CycleSlotColor(index);
    SendColorChange(index);   // ← always runs; calls CycleSlotColor internally
```

Host effectively cycles its color twice per click.

### F-6 — `SkirmishLobbyUI` "Free For All" button is duplicate of "Circle" (LOW)
**File:** [SkirmishLobbyUI.cs:256-261](Assets/Scripts/UI/Menus/SkirmishLobbyUI.cs#L256-L261)
**Severity:** Low — UX

Both buttons read same flag, write same value. Visibly active when Circle is chosen. Either missing `SpawnLayout.FFA` enum value or one button needs deleting.

### F-7 — `EntityInfoPanel` per-frame GUIStyle allocations (MEDIUM)
**File:** [EntityInfoPanel.cs](Assets/Scripts/UI/Panels/EntityInfoPanel.cs) lines 315, 464, 553, 684
**Severity:** Medium — perf

For a multi-select grid of 30+ units, line 464 allocates 30 GUIStyles per frame.

### F-8 — `EntityActionPanel` per-frame GUIStyle allocations (HIGH, W-1 task-051)
**File:** [EntityActionPanel.cs](Assets/Scripts/UI/Panels/EntityActionPanel.cs) lines 902, 1088, 1224, 1247, 1267, 1297, 1364, 1549, 1557
**Severity:** High — perf

9 active sites verified. Worst is line 1247 — fires 5x per hovered build button per frame.

### F-9 — `EntityActionPanel` swallows ArgumentException silently (W-6 confirmed) (MEDIUM)
**File:** [EntityActionPanel.cs:90-133](Assets/Scripts/UI/Panels/EntityActionPanel.cs#L90-L133)
**Severity:** Medium

`try { ... } catch (System.ArgumentException) { /* silent */ }`. Hides real bugs in dispatch switch. Either log+rethrow once per type or remove.

### F-10 — `OptionsMenuUI` master volume doesn't apply in-session (W-9 confirmed) (LOW)
**File:** [OptionsMenuUI.cs:257-262, 384-411](Assets/Scripts/UI/Menus/OptionsMenuUI.cs#L257-L262)
**Severity:** Low — UX

Volume slider writes to local `_masterVolume` per-frame, but `AudioListener.volume` only updates in `ApplySettings()` on Apply button click. Resolution/fullscreen pattern is correct (disruptive); volume should be live.

### F-11 — `InGameMenuPanel.DoQuitToMenu` cleanup is incomplete (MEDIUM)
**Files:** [InGameMenuPanel.cs:369-391](Assets/Scripts/UI/HUD/InGameMenuPanel.cs#L369-L391), [PostGameStatsUI.cs:531-553](Assets/Scripts/UI/Menus/PostGameStatsUI.cs#L531-L553)
**Severity:** Medium

Both quit paths Destroy `RuntimeManagers` and Dispose ECS world, but several DontDestroyOnLoad objects survive: `TechTreeDB`, `LockstepBootstrap`, `LoadingScreen`, plus `ProjectileVisualSystem` and `PresentationSpawnSystem` prefab roots. The presentation prefab cache root will reference disposed ECS entities. After return-to-menu, GameBootstrap recreates `RuntimeManagers` with fresh `PresentationSpawnSystem` — old one duplicated, not replaced. `GameBootstrap.Reset()` doesn't dispose the orphaned visual roots.

### F-12 — Per-frame allocations across many other panels (MEDIUM)
- PostGameStatsUI.cs:171, 197, 370 — `new GUIStyle(Styles.Button)` per faction button + per graph button
- ResourceHUD.cs:354 — `new GUIStyle(Styles.SmallLabel)` per row when icon missing
- PlayerNotificationSystem.cs:215 — `new GUIStyle(_pillTextStyle)` per active notification per frame
- TechTreePanel.cs:205, 247, 255, 294, 308 — multiple per-row allocations
- PlanningModeOverlay.cs:125, 136, 176, 186 — markerStyle + typeStyle per planned waypoint
- GathererHutAreaDisplay.cs:395 — `new Vector3[CircleSegments]` (64 elements) per `SetCircle` call
- FloatingHealthBars.cs:57 — `var drawn = new HashSet<Entity>()` per OnGUI invocation
- FloatingIncomeDisplay.cs:35 — `_prevElapsed` Dictionary grows unbounded

---

## Architectural Smells

- **Inconsistent click-to-cancel pattern across IMGUI panels.** EntityInfoPanel uses deferred-pending-index, EntityActionPanel uses try/catch swallow, SpellPanel does direct in-loop dispatch (and has F-3). Three different solutions to the same IMGUI fragility.
- **Style mutation in static SectAdoptionPanel** (`_cultureHeaderStyle.normal.textColor` save/restore) — works but inconsistent with the per-allocation pattern elsewhere.
- **`UnifiedUIManager` is a thin static + Awake bag.** No mechanism to clean up panels — combined with F-11, in-game menu's "Quit to Menu" leaves shadows.
- **Selection cleanup runs every Update**, not on entity-destroyed events. Fine with few units; with 200-unit selection, walks the list every frame.
- **No DPI / aspect-ratio awareness.** All panel sizes hard-coded in absolute pixels. On 4K display panels become tiny; on ultrawide leave huge dead space. IMGUI limitation, not easy to fix.

---

## Verification of Surface-Scan Claims

| Claim | Verdict | Evidence |
|---|---|---|
| **W-1** 10+ per-frame GUIStyle allocs in EntityActionPanel | **CONFIRMED** | 9 active sites verified (F-8). Line numbers match within ±5 from W-1 (drift from edits). |
| **W-6** Silent try/catch in EntityActionPanel.OnGUI | **CONFIRMED** | F-9. |
| **W-9** OptionsMenuUI slider doesn't apply in-session | **CONFIRMED for master volume** | F-10. Resolution/fullscreen/quality apply only on Apply (intentional). |
| task-052 F-7 EntityInfoPanel observer guard | **N/A** | task-052 F-7 is about Litharch DesiredDestination, not EntityInfoPanel. EntityInfoPanel has no observer guard but doesn't need one — it's read-only and observer-mode display works correctly. |
| task-054 F-4 build cost local-only in MP | **CONFIRMED still present** | BuildCommandPannel.cs:421-449 spends locally before queueing. Out of scope for UI pass. |

---

## What I Verified Is Fine

- **Observer-mode gating on command panels** — EntityActionPanel:69, BuilderCommandPanel:207, FormationDragPreview:111 all check `IsObserver`. RTSInputManager:108-110 also gates command-issuing input.
- **Selection-during-destruction NRE risk** — SelectionSystem.CleanSelectionInternal runs every Update; UnifiedUIManager.GetFirstSelectedEntity / GetAllSelectedEntities re-check `Exists(e)`; EntityInfoPanel multi-grid loop also `continue`s on non-existent. No NRE path observed.
- **Modal popup input blocking** — CultureChoicePopup correctly blocks via RTSInputManager.ShouldBlockInput, SelectionSystem.ShouldBlockSelection, UnifiedUIManager.IsPointerOverAnyPanel.
- **Settings persistence at boot** — OptionsMenuUI.LoadAndApplySettings called from MainMenuUI.Awake before any UI shown.
- **Skirmish lobby color uniqueness** — CycleSlotColor + IsColorInUse + ResolveColorConflicts maintain uniqueness.
- **Tab-style caching** — both lobby UIs use `Styles.TabActive/Inactive` cache. No per-tab allocation.
- **Tooltip rendering pattern** — `ResourceIcons.DrawTooltip()` called at end of each panel's OnGUI; `_tooltipStyle` cached lazily.
- **Faction-accent style cache** — `Styles.GetFactionAccentStyle` 8-entry array indexed by Faction, allocates once per faction across session.
- **Resolution dropdown deduplication** — BuildResolutionList correctly de-dupes by width×height and sorts descending.

---

## Things I Deliberately Didn't Dig Into

- **MinimapUI.cs / MinimapRenderer.cs** — task-059.
- **EntityExtractors.cs (1371 lines)** — pure read-side data extraction. Spot-checked GetActionInfo and GetUnitCost; no NRE.
- **MultiplayerLobbyUI networking internals (lines 540+)** — task-054.
- **LoadingScreen, ActiveAbilityBar, UnitIndicatorSystem, RallyPointDisplay, FormationPreview, GameStatsTracker, CrystalDebugPanel** — single-purpose or task-059 covered self-heal pattern.
- **Debug/EntityCounter, Debug/TerrainPassabilityGizmo** — debug-only, no game-state mutation.
