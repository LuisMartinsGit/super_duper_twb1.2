---
deft:
  id: task-deepdive-world-fog-2026-059
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
  labels: [code-quality, code-review, deep-dive, world, fog, minimap]
---

# World / Terrain / Fog / Minimap / Influence / DayNightCycle deep-dive

## Context

Companion to task-052..058. Same method.

## TL;DR

World/visibility is **mostly clean** — the recent MinimapRenderer EnsureECSQueries fix held, all HUD components self-heal stale world references, FoW is correctly wired. But **two critical bugs** the surface scan caught half-rightly:

- **F-1 CRITICAL (live in production)**: `MinimapClickProxy` in `MinimapRenderer.cs:759-761` has the missing-brace pattern. Right-click on minimap fires move command AND snaps camera. Same shape as task-051 MB-30.
- **F-2 CONFIRMED MB-30**: `MusicManager.cs:117-122` — menu music always plays in Game scene because both `CrossfadeTo` calls run sequentially instead of one-or-the-other.

Plus DayNightCycle leaks Texture2D + Mesh on every scene reload (~1 MB/reload).

Surface-scan verification: 5/9 W-* / MB claims confirmed (~55% TPR). Notably W-7 (PassabilityGrid race), W-8 (FloatingHealthBars predicate), W-11 (FogVisibilitySync NRE), and MB-29 (FoW NRE) are all REFUTED — surface scan misread the surrounding context.

## Acceptance Criteria

- [ ] F-1: Add braces to `MinimapClickProxy` so right-click doesn't snap camera
- [ ] F-2: Add `else` to MusicManager scene check so menu music doesn't play in-game
- [ ] F-4: DayNightCycle.OnDestroy releases the cloud Mesh + Texture2D
- [ ] W-3: Push placement-preview transform via static from BuilderCommandPanel instead of GameObject.Find
- [ ] Drop W-7, W-8, W-11, MB-29 from task-051's claim list

---

## Verified Findings

### F-1 — CRITICAL: Right-click on minimap also snaps camera (bad UX, production bug)
**File:** [MinimapRenderer.cs:759-761](Assets/Scripts/World/Minimap/MinimapRenderer.cs#L759-L761)
**Severity:** Critical UX

`MinimapClickProxy` has missing braces — `if (eventData.button == Right)` runs unconditionally, so `HandleLeftClick(eventData)` fires on EVERY click regardless of button. Result: every right-click move order also yanks the camera to the click point with `instant: true` (line 656), jerking the user away from where they were looking.

This component is added in `GameBootstrap.CreateManagersObject` (line 183), so it's live in production.

**Fix:** Add braces.

### F-2 — CONFIRMED MB-30: Menu music plays over game music
**File:** [MusicManager.cs:117-122](Assets/Scripts/Audio/MusicManager.cs#L117-L122)
**Severity:** Critical UX

Identical missing-brace bug:
```csharp
if (string.Equals(sceneName, "Game"))
    CrossfadeTo(_gameClip);
    CrossfadeTo(_menuClip);   // unconditionally fires
```

In Game scene the second call immediately replaces the first. Net effect: menu music plays in-game, no game music ever. **By accident, the Menu scene works** (only the second line fires).

**Fix:** Add `else` between the two `CrossfadeTo` calls.

### F-3 — NRE in dead-code `MinimapUI.HandleClick` (LOW)
**File:** [MinimapUI.cs:407-409](Assets/Scripts/UI/HUD/MinimapUI.cs#L407-L409)
**Severity:** Low — dead code (no bootstrap adds MinimapUI)

Same missing-brace pattern. `_cameraController.MoveToPositionSmooth` called unconditionally; if `cameraRig != null` then `_cameraController` is null. Currently dead code, but a landmine if anyone switches to it.

### F-4 — Texture2D + Mesh leaks in `DayNightCycle.CreateCloudProjector` (HIGH)
**File:** [DayNightCycle.cs:243, 260](Assets/Scripts/World/DayNightCycle.cs#L243)
**Severity:** High — leaks ~1 MB per scene reload

`new Mesh()` (line 243) and `new Texture2D(512, 512, …)` (line 260) assigned to renderer/material but never explicitly destroyed. `OnDestroy` only destroys the projector GameObject (line 306), which doesn't cascade to the Mesh/Texture2D assets.

**Fix:** In OnDestroy, explicitly `Destroy(_cloudMesh); Destroy(_cloudTex);`.

### F-5 — W-2 CONFIRMED: `Camera.main` per-frame in DayNightCycle
**File:** [DayNightCycle.cs:228-231](Assets/Scripts/World/DayNightCycle.cs#L228-L231)
**Severity:** Medium — per-frame perf

`Camera.main` dereferenced twice per Update tick. Same anti-pattern fixed elsewhere via `_cachedCamera`.

### F-6 — W-3 CONFIRMED: `GameObject.Find("PlacementPreview")` per LateUpdate during placement
**File:** [GathererHutAreaDisplay.cs:106](Assets/Scripts/UI/HUD/GathererHutAreaDisplay.cs#L106)
**Severity:** Low — bounded to "while placing"

`BuilderCommandPanel` already knows the preview transform — pushing via static would erase the cost.

### F-7 — W-10 CONFIRMED: Empty `else { }` blocks in InfluenceManager.Awake (LOW)
**File:** [InfluenceManager.cs:117-119, 121-123](Assets/Scripts/Influence/InfluenceManager.cs#L117-L119)
**Severity:** Low — cleanup-after-debugging artifact

Two empty else blocks where `Debug.LogWarning` was likely stripped without removing the branches.

### F-8 — Minimap and FogVisibilitySync use different visibility predicates (MEDIUM)
**Files:** [MinimapRenderer.cs:470](Assets/Scripts/World/Minimap/MinimapRenderer.cs#L470) vs [FogOfWarSystem.cs:222-236](Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L222-L236)
**Severity:** Medium — visual inconsistency

`FogVisibilitySyncSystem` falls back to a direct distance check against player units' LOS radii when an enemy unit isn't fog-visible (covers grid resolution edge cases). `MinimapRenderer` does NOT apply the same fallback. Net: an enemy unit can be visible as a 3D model but absent from the minimap blip layer.

The "canonical predicate" mentioned in W-8 should be implemented and shared, but the right place to fix is the minimap, NOT FloatingHealthBars (which W-8 incorrectly fingered).

### F-9 — `MinimapUI.OnPointerClick` discards button info (LOW)
**File:** [MinimapUI.cs:415](Assets/Scripts/UI/HUD/MinimapUI.cs#L415)
**Severity:** Low — dead code

Combined with F-3, the dead `MinimapUI` is doubly broken.

### F-10 — Right-click camera-snap interrupts user's drag (CRITICAL UX)
Same site as F-1, but a second behavioral concern: because `HandleRightClick` runs *before* `HandleLeftClick` (which the missing-brace bug always invokes), a right-click on the minimap can simultaneously issue move orders and re-center the camera in the same frame. Worse, the camera snap is `instant: true`.

---

## Architectural Smells

- **Two minimap implementations** (`MinimapRenderer` live, `MinimapUI` dead). Same set of bugs duplicated in both. Either delete `MinimapUI.cs` or designate it canonical and retire `MinimapRenderer`.
- **Per-faction influence "scratch RTs"** duplicate the same-size float texture three times. Could share a single scratch RT serially since rebake runs in `LateUpdate` sequentially.
- **`InfluenceBridge` reads ECS state every 2s** and rebuilds full polygon/lane lists — `O(n²)` pair loop over all trade nodes. AI managers should signal changes; 2s polling fallback can stay as a safety net.
- **`ProceduralTerrain.cs` is 1,984 lines** doing terrain gen, splatmap, water, trees, tints, prefabs — five domains in one MonoBehaviour.
- **`PassabilityGrid`** is a pure managed singleton populated synchronously in `Start` for an N×N walk-of-terrain. On a 256-half map at cellSize 4, that's 16,384 cells × 5 SampleHeight = 81,920 synchronous calls in one frame.

---

## Verification of Surface-Scan Claims

| Claim | Verdict | Evidence |
|------|---------|----------|
| **MB-29** FogOfWarManager:240-242 — `_tex.Reinitialize` runs even when `_tex == null` | **REFUTED** | Line above unconditionally creates `new Texture2D` when `_tex == null`. `_tex` is never null at the Reinitialize call. Misleading indentation, no bug. |
| **MB-30** MusicManager:117-122 | **CONFIRMED** | F-2. |
| **W-2** DayNightCycle Camera.main per-frame | **CONFIRMED + extra leaks** | F-5 + F-4. |
| **W-3** GathererHutAreaDisplay GameObject.Find | **CONFIRMED** | F-6 (bounded scope, low impact). |
| **W-4** MinimapUI:217 per-frame `FindFirstObjectByType<FogOfWarManager>` | **CONFIRMED but irrelevant** | Dead code. Live `MinimapRenderer` caches `_fow` once in Awake. |
| **W-5** MinimapUI:373-390 RTSCameraRig found per click | **CONFIRMED but irrelevant** | Dead code; only runs on click anyway. |
| **W-7** PassabilityGrid race vs ProceduralTerrain | **REFUTED** | ProceduralTerrain.Awake (-100) generates heightmap synchronously inside Awake. PassabilityGrid uses Start. Unity guarantees Awakes complete before Starts. No race. |
| **W-8** FloatingHealthBars:61, 97 different visibility predicates | **REFUTED** | Lines 61 and 97 are not visibility predicates — they check Health and LocalTransform. Component intentionally relies on FogVisibilitySyncSystem having already deactivated invisible GameObjects. The real inconsistency is in the minimap (F-8). |
| **W-10** InfluenceManager empty `else { }` blocks | **CONFIRMED** | F-7. |
| **W-11** FogOfWarSystem:126 no null check on EntityViewManager | **REFUTED** | Lines 126-127 do exactly `if (entityViewManager == null) return;`. Misread or guard added since the audit. |

**Score: 5/9 confirmed (55% TPR)** — slightly higher than other audits, but 4 are refuted.

---

## What I Verified Is Fine

- **`InfluenceManager.OnDestroy`** properly releases both RTs (`InfluenceMap`, `BloodMap`) — task-043 fix held.
- **All three faction influence components** properly release `_scratchRT` in OnDestroy.
- **`MinimapRenderer`** correctly handles ECS world recreation: `EnsureECSQueries()` reruns at top of Update, uses `ReferenceEquals` to detect a swapped world.
- **`MovementLineDisplay`, `RallyPointDisplay`, `GathererHutAreaDisplay`, `FormationPreview`, `FormationDragPreview`** all self-heal stale ECS world references. No DontDestroyOnLoad NRE risk.
- **`FogOfWarManager.ForceRebuildGrid`** correctly preserves explored state when only dimensions change (Fix #242).
- **`FogOfWarManager.SetupFogOfWar`** correctly calls `ApplyBounds` *after* Awake, so default-25-unit grid bug is fixed.
- **`TerrainUtility.GetActiveTerrain`** has a three-tier fallback. Robust.
- **`InfluenceBridge` totem tracking** — `Dictionary<int, int>` keyed by `Entity.Index` correctly handles recycled indices.
- **`FogOfWarSystem.OnUpdate`** correctly excludes `CrystalTag` so neutral crystal entities don't reveal fog.
- **All HUD components have OnDestroy cleanup** — pools and runtime-created Materials are destroyed (DayNightCycle is the exception, F-4).

---

## Things I Deliberately Didn't Dig Into

- **`WaterPlane.cs`** — out of explicit scope.
- **The actual shaders** — out of scope; bugs there would manifest as visual glitches the user would see immediately.
- **`OptionsMenuUI`** (W-9) — UI subsystem.
- **`EntityActionPanel`** (W-1, W-6) — UI subsystem.
- **The full MinimapUI dead-code path** — left at "dead code with bugs".
- **AI consumption of the InfluenceMap** — AI subsystem audit.
- **`PathfindingTestSetup` and `LockstepBootstrap`** DontDestroyOnLoad — not in visibility subsystem.
