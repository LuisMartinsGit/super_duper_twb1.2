---
deft:
  id: task-deepdive-crystal-construction-2026-058
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
  labels: [code-quality, code-review, deep-dive, crystal, construction]
---

# Crystal-Faction / Curse / Construction deep-dive

## Context

Companion to task-052..057. Same method.

## TL;DR

Crystal/curse/construction is **architecturally sound** but contains **3 real correctness bugs** the surface scan missed:

- **F-1 CRITICAL**: `VeilstingerCombatSystem.cs:140-272` lost an `else if` keyword. Bare `{ }` block at line 242 ("Too far - CHASE") runs after EVERY successful in-range shot, immediately overwriting `DesiredDestination` to walk into target → exit range → RETREAT branch fires → oscillate. Combat AI for Veilstingers is broken. HoldPosition is also clobbered.
- **F-2 CRITICAL**: `CrystalExtinctionSystem.cs:107-111` missing braces — multiplayer-deterministic seed (`LockstepServiceLocator.Instance.CurrentTick * 4217 + ...`) is **immediately overwritten** by `World.Time.ElapsedTime` derivation. Host and client compute different respawn positions on extinction → state desync. This is the genuine MB-21 from task-051 (the only real one in this batch).
- **F-3**: Failed respawn (no valid position found) schedules a full 3-minute wait instead of retrying. With deterministic seed advancing only by elapsed time, retries may keep failing on the same map.

Plus **F-5 (HIGH)**: `BuildingConstructionSystem.cs:178-185` recomputes HP from construction-progress ratio every builder tick, **erasing any combat damage taken during construction**. Half-built structures with at least one builder are effectively unkillable.

Surface-scan verification: 4/9 X/MB claims confirmed — but only MB-21 is the cosmetic-class bug; the others (X-1, X-2, X-5) are real but mis-described by surface scan. False positives: MB-22, MB-23, MB-24, X-3 (CrystalTag vs CrystalUnitTag — by design, two distinct scopes).

## Acceptance Criteria

- [ ] F-1: Add `else if (dist > vs.MaxRange)` to VeilstingerCombatSystem.cs:242
- [ ] F-2: Add braces to CrystalExtinctionSystem.cs:108-110 so the lockstep seed actually wins
- [ ] F-3: Either preserve `IsExtinct = 1` until respawn succeeds, or schedule a short retry on failure
- [ ] F-5: Construction HP scaling subtracts existing damage instead of overwriting from ratio
- [ ] Drop MB-22, MB-23, MB-24, X-3 from task-051's claim list

---

## Verified Findings

### F-1 — Veilstinger fall-through clobbers in-range firing (CRITICAL)
**File:** [VeilstingerCombatSystem.cs:140-272](Assets/Scripts/Systems/Crystal/VeilstingerCombatSystem.cs#L140-L272)
**Severity:** Critical — Veilstinger combat AI broken

Lines 112 (`if (dist < minRange)`) and 140 (`else if (dist <= maxRange)`) form a proper chain, but the third "Too far - CHASE" block at line 242 is a bare `{ }` block with no `else if (dist > maxRange)` guard. After the in-range branch fires (resets `vs.AimTimer = 0; vs.IsFiring = 0;`, sets stop-DesiredDestination, dispatches lasers), execution falls into the bare block which **immediately overwrites `DesiredDestination` with `Position = targetPos, Has = 1`** — telling the unit to walk into its target. Net effect: a Veilstinger that just successfully fired walks into melee range, exits it (RETREAT branch), oscillates.

The HoldPositionTag check at line 244 also clears `tgt.Value = Entity.Null` after every shot if HoldPosition is set, breaking hold-position behavior entirely.

Compare with the correctly-chained `else if (dist > gs.LaserRange)` in `GodsplinterCombatSystem.cs:252` — confirms this is a regression, not intentional.

**Fix:** Change line 242 from `{` to `else if (dist > vs.MaxRange) {`.

### F-2 — Crystal extinction respawn loses multiplayer determinism (CRITICAL)
**File:** [CrystalExtinctionSystem.cs:107-111](Assets/Scripts/Systems/Crystal/CrystalExtinctionSystem.cs#L107-L111)
**Severity:** Critical — desyncs at extinction trigger

```csharp
if (GameSettings.IsMultiplayer && LockstepServiceLocator.IsActive)
    seed = (uint)(LockstepServiceLocator.Instance.CurrentTick * 4217 + GameSettings.SpawnSeed + 99);
    seed = (uint)(World.Time.ElapsedTime * 1000 + GameSettings.SpawnSeed + 99);
```

The braces are missing; the second `seed = ...` always runs, so the multiplayer branch's lockstep-tick-derived seed is immediately overwritten. Host and client diverge → respawn position diverges → state desync at the moment Crystal extinction triggers.

This is the **genuine MB-21** — *not* a cosmetic case (other MB-22/23/24 are cosmetic).

**Fix:** Add braces:
```csharp
if (GameSettings.IsMultiplayer && LockstepServiceLocator.IsActive)
    seed = ... lockstep version ...;
else
    seed = ... elapsed time version ...;
```

### F-3 — Failed extinction respawn costs 3 minutes (HIGH)
**File:** [CrystalExtinctionSystem.cs:92-99](Assets/Scripts/Systems/Crystal/CrystalExtinctionSystem.cs#L92-L99)
**Severity:** High — design issue

When `RespawnTimer <= 0f`, code calls `TryRespawn(em)` then unconditionally sets `ext.IsExtinct = 0; ext.RespawnTimer = 0f;`. If `TryRespawn` failed all 30 attempts (no valid position), no node was created. Comment line 165 claims it'll retry next frame because RespawnTimer is still 0, but `IsExtinct` is now 0 → next OnUpdate sees `nodeCount == 0 && IsExtinct == 0` and re-enters the "just went extinct" branch → sets `RespawnTimer = RespawnDelay` (180s).

Result: a single failed respawn attempt costs another full 3 minutes. With deterministic seed advancing only by elapsed time, repeat attempts may keep failing.

**Fix:** Only clear `IsExtinct` after a successful respawn. On failure, leave `IsExtinct = 1` and reschedule with a shorter retry interval (e.g. 5s).

### F-4 — Unmined cadavers persist forever (HIGH)
**Files:** [Cadaver.cs](Assets/Scripts/Entities/Cadaver.cs), [CrystalComponents.cs:258](Assets/Scripts/Core/Components/CrystalComponents.cs#L258), [CrystalDeathDropSystem.cs](Assets/Scripts/Systems/Crystal/CrystalDeathDropSystem.cs)
**Severity:** High — entity bloat over a long match

`CadaverState` has no decay timer. `CrystalCadaverLifetime { TimeRemaining }` is **declared but never added to any entity, never decremented, and no system queries for it** — pure dead code.

`CrystalDeathDropSystem` calls `Cadaver.CreateOrMerge` with `MaxCrystalNodes = 128` — once the cap is reached, **new creature deaths silently produce no cadaver** (no log, no compensation). Unmined cadavers (e.g., from a wave dying far from any miner) sit at full crystal value indefinitely. With waves of up to 100 units firing every 25-120 seconds, the field saturates at 128 cadavers and further crystal income from kills is lost.

**Fix:** Either implement the `CrystalCadaverLifetime` countdown that's already declared, or eject the oldest cadaver when the cap is hit, or add a log event when drops are silently lost.

### F-5 — Construction tick erases combat damage to half-built structures (HIGH)
**File:** [BuildingConstructionSystem.cs:178-185](Assets/Scripts/Systems/Buildings/BuildingConstructionSystem.cs#L178-L185)
**Severity:** High — half-built buildings effectively unkillable

While `UnderConstruction.Progress < Total`, every builder tick recomputes `hp.Value = math.max(1, (int)math.round(hp.Max * ratio))` from construction-progress ratio. If structure took damage during this tick, the damage is silently overwritten. Tick rate of construction (~16ms with one builder, faster with many) is faster than any combat damage application.

Probably *not* the intended design — combat damage to under-construction buildings should reduce HP visibly.

**Fix:** Track damage delta separately and apply `hp.Value = math.max(1, ratio_target_hp - damageTaken)`.

### F-6 — `CrystalAIState.HarassTimer` and `UnitSpawnTimer` are dead state (LOW, X-2 confirmed)
**Files:** [CrystalComponents.cs:84-86](Assets/Scripts/Core/Components/CrystalComponents.cs#L84-L86), [CrystalMainNode.cs:57-59, 116-118](Assets/Scripts/Entities/Buildings/CrystalMainNode.cs#L57-L59)
**Severity:** Low — schema cleanup

Both fields written at node creation, never read anywhere. Wave system migrated to `CrystalWaveState` singleton; per-node training migrated to `CrystalTrainingState`. Pure cruft.

### F-7 — `CrystalAISystem` log comment is stale (LOW)
**File:** [CrystalAISystem.cs:160-161](Assets/Scripts/Systems/Crystal/CrystalAISystem.cs#L160-L161)
**Severity:** Low — design smell

Comment says "spread system is disabled" but `CrystalSpreadSystem` is alive and runs. Two separate "level" computations exist (time-based via `CrystalAIState.Phase`, radius-based via `CrystalNodeLevel.FromRadius`). Values can disagree.

### F-8 — `AICrystalHuntBehavior` is dead code (LOW)
**File:** [AICrystalHuntBehavior.cs:17](Assets/Scripts/AI/Behaviors/AICrystalHuntBehavior.cs#L17)
**Severity:** Low

Marked `[DisableAutoCreation]` with comment "Replaced by SimpleAISystem." 200+ lines of unreachable logic.

### F-9 — `WallGatePassabilitySystem` re-creates EntityQuery every poll (LOW)
**File:** [WallGatePassabilitySystem.cs:42-46](Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs#L42-L46)
**Severity:** Low — minor perf, slow leak

Created at line 42 every 0.3s, never disposed. Should be cached in `OnCreate`. Slow leak in EntityManager's query cache.

---

## Architectural Smells

- **Two separate "level" abstractions** for crystal main nodes — `CrystalAIState.Phase` (time-driven) vs `CrystalNodeLevel.Value` (radius-driven). Both clobber each other within the same SimulationSystemGroup.
- **Cadaver merge invalidates other miners' targets** without notification. Handler exists; momentary "go idle, re-search" stutter on every merge. Pre-existing.
- **Sub-nodes lack Defense component** (only Main Node has one) — full damage with no reduction. Probably intentional ("brittle"), worth a comment in each factory.
- **`CrystalDeathDropSystem` silently drops kill-rewards above 128-cadaver cap.** Either eject oldest or log it.
- **`CrystalAISystem.RescueStrandedUnits` teleports without vision check** — units pop in/out of player vision.

---

## Verification of Surface-Scan Claims

| Claim | Verdict | Notes |
|---|---|---|
| **X-1** HP scaling | **REAL but different** — actual bug is that scaling **erases combat damage**, not the math (F-5) |
| **X-2** Dead AIState fields | **CONFIRMED** (F-6) |
| **X-3** CrystalTag vs CrystalUnitTag | **REFUTED** — two tags by design with different scopes (CursedGround uses broad CrystalTag for immunity; EnforcementNode uses CrystalUnitTag for unit-only buff) |
| **X-4** Builder strands | **REFUTED** — auto-chain to next nearby site works; `else` is technically dead but harmless |
| **X-5** Cadaver decay | **REAL but nuanced** — depleted cadavers ARE destroyed; UNmined cadavers never decay (F-4) |
| **MB-21** | **CONFIRMED CRITICAL** (F-2) |
| **MB-22** | **REFUTED** (cosmetic) |
| **MB-23** | **REFUTED** (cosmetic) |
| **MB-24** | **REFUTED** (cosmetic) |

**Score: 4/9 are real meaningful bugs** (X-1 mis-described → F-5; X-2; X-5 nuanced → F-4; MB-21 → F-2). 44% TPR — slightly higher than other audits due to MB-21 being a genuine missing-brace bug with critical impact.

---

## What I Verified Is Fine

- **Cadaver merge logic** — destroys + recreates is intentional, miners handle the orphaning gracefully.
- **`CrystalDeathDropSystem` fires once per death** — `WithNone<DeathAnimationState, BuildingCollapseState>` correctly dedupes.
- **`CrystalAISystem` multiplayer determinism** (lines 146-151) — correctly seeds from `LockstepServiceLocator.Instance.CurrentTick` (compare to F-2 which does NOT do this correctly).
- **`CrystalSpreadSystem` ring-step bound** — `newRadius = math.min(prevRadius + ringStep, crystalNode.SpreadRadius)`. Bounded.
- **`CursedGroundRecessionSystem` orphan detection** — owner-existence + CrystalNode-component checks.
- **Construction completion path** — applies DeferredDefense, sect HP multiplier, GathererHut SuppliesIncome safety net, ChapelSmall/Shrine RP bonus.
- **Builder dies / site destroyed mid-construction** — both handled (lines 85-98).
- **Multiple builders on same site** — sequential `Get→add→Set` per builder stacks correctly.
- **`WallGatePassabilitySystem`** — previously-fixed missing-brace bug stayed fixed.
- **Suppression aura faction filter** — line 58 properly skips Faction.White.
- **Veilstinger/Godsplinter NOT double-fired by RangedCombatSystem** — neither has ArcherTag.
- **`GodsplinterCombatSystem`** has the correct `else if (dist > gs.LaserRange)` chain — confirms F-1 is a regression.

---

## Things I Deliberately Didn't Dig Into

- **`PassabilityGrid.IsPassable` semantics** — task-053.
- **`AILogger.Log` perf** — out of scope.
- **`LockstepServiceLocator` correctness** — task-054.
- **`FactionEconomy.Spend/Add`** — task-056.
- **`CombatModifiers.CalculateFinalDamage`** — task-055.
- **`ProceduralTerrain.PaintCursedGround`** — managed terrain, out of scope.
- **`InfluenceBridge.OnUnitDied`** — Feraldis blood map, separate subsystem.
- **`TechTreeDB.Instance.TryGetUnit`** — task-057.
- **Presentation layer `PresentationId` mappings** — render-side.
- **Sect aura math** (`SectBuildingAuraSystem.cs:316`) — sect-side.
- **Pathfinding interaction with cursed-ground tiles** — cursed entities don't block passability; integration moot.
