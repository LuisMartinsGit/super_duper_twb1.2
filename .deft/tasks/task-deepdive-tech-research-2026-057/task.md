---
deft:
  id: task-deepdive-tech-research-2026-057
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: critical
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [code-quality, code-review, deep-dive, tech, research, sect]
---

# Tech Tree / Research / Sect / Training / TechEffect / AgeUp deep-dive

## Context

Companion to task-052/053/054/055/056. Same method.

## TL;DR

Research/tech/sect pipeline is **structurally clean** but has **two material wiring holes** that surface scans missed:

- **F-1 CRITICAL**: All 12 sect technologies are silently inert. `FactionSectState.SetTechFlag` has **zero callers** anywhere in the codebase. Sect tech research completes, fires the standard event, but nothing bridges it to the sect flag system. Players see "Tech_DietaryMandate researched" in the UI; the gameplay effect (RegenPerSecond, SpellCooldownReduction, MagicDamage, WallIncomeFromTech) never applies.
- **F-2 CRITICAL**: Sect adoption boosts existing units (via task-001 delta system) but **`SectEffectSystem.ApplySectEffectsToUnit` is defined and never called.** No spawn-time hook exists. A unit trained AFTER adoption starts at base damage forever. Defeats the whole adoption mechanic for any reinforcement.
- **F-3 HIGH**: Six `SectMultipliers` fields are dead — written by sect-tech application paths (themselves dead per F-1) but never read by any consuming system.
- **F-4 HIGH**: Three Runai economy techs (`Runai_LongHaulTariffs`, `Runai_PackBazaar`, `Runai_EscortedCaravans`) are research-able but no system gates behavior on them.

Surface-scan verification: out of 12 economy/tech surface-scan claims, **only 4 are real meaningful bugs** (T-3 perf, T-4 SetTechFlag — confirmed CRITICAL, T-9 cosmetic, T-10 low perf). Most others are cosmetic or design-correct. Same pattern as combat (0/11) and economy (1/10).

## Acceptance Criteria

- [ ] F-1: Wire sect-tech completion to `FactionSectState.SetTechFlag` (e.g. `TechEffectSystem.OnTechCompleted` early-return for `Tech_*` IDs that calls SetTechFlag)
- [ ] F-2: Add `SectEffectSystem.ApplySectEffectsToUnit` calls in TrainingSystem.cs:240, :256 and BatchTrainingSystem.cs:217
- [ ] F-3: Remove dead fields from `SectMultipliers` OR wire them to real consumers
- [ ] F-4: Wire Runai economy techs into `TraderMovementSystem` / Runai trade income, OR remove from JSON
- [ ] T-3 / F-6: Cache the EntityQuery in SpawnPlacementHelper

---

## Verified Findings

### F-1 — Sect tech bridge missing — 12 sect techs silently inert (CRITICAL)
**File:** [FactionSectState.cs:220](Assets/Scripts/Economy/FactionSectState.cs#L220) (SetTechFlag definition with zero callers)
**Severity:** Critical — entire sect-tech feature surface non-functional

`SetTechFlag` has **zero callers** in the codebase (`Grep SetTechFlag` returns only the definition). The chapel research path queues a `ResearchQueueItem` like any other tech; ResearchSystem completes it and fires `OnTechCompleted(faction, "Tech_DietaryMandate")`; only TechEffectSystem listens, and it only handles `gatherSpeedMult/carryCapacityBonus/meleeAttackSpeedMult/meleeDefenseAdd`. Result: every `flags.X` branch in `FactionSectState.ApplyTechFlagsToMultipliers` (lines 587-606) is unreachable. Four derived multipliers are computed only on dead branches.

Player observation: the UI advertises "Tech_DietaryMandate researched"; the gameplay effect never fires.

**Fix:** Add a `TechEffectSystem.OnTechCompleted` early-return branch that detects `techId.StartsWith("Tech_")`, looks up the matching sect ID via `SectConfig.GetSectIdForTechId` (need to add inverse lookup), and calls `FactionSectState.Instance.SetTechFlag(faction, sectId)` followed by `SectEffectSystem.Instance?.RecalculateAllPassives(faction)`.

### F-2 — Sect effects never applied to trained units (CRITICAL)
**File:** [SectEffectSystem.cs:420](Assets/Scripts/Systems/Crystal/SectEffectSystem.cs#L420) (ApplySectEffectsToUnit defined, no callers)
**Severity:** Critical — defeats the adoption mechanic for any reinforcement

`SectEffectSystem.ApplySectEffectsToUnit` is the documented spawn-time hook for sect bonuses. **No file calls it.** TrainingSystem.SpawnUnit (line 256) calls only `TechEffectSystem.ApplyCompletedTechEffects`; BatchTrainingSystem (line 217) and BattalionFactory (line 240, leader) likewise.

Concrete consequence: if a faction adopts EmberAsh (+10% MeleeDamage), every Swordsman alive at adoption time gets the boost via the delta pass, but every Swordsman trained afterward starts at base damage forever. Same for RangedDamage (HollowBrand interactions), AttackSpeed, FogVisionBonus, RangedAccuracy. **The compounding fix from task-001 explicitly relied on this spawn-time hook running to keep new units in sync; it never wired in.**

**Fix:** Add `SectEffectSystem.ApplySectEffectsToUnit(em, unit, faction)` calls at TrainingSystem.cs:240, :256 and BatchTrainingSystem.cs:217.

### F-3 — Dead multiplier fields (HIGH)
**File:** [FactionSectState.cs](Assets/Scripts/Economy/FactionSectState.cs) `SectMultipliers` struct
**Severity:** High — dead code surface; misleading for future feature work

`SectMultipliers` carries fields no system queries: `MagicDamage`, `WallIncomeFromTech`, `RegenPerSecond`, `HasRenewal`, `RenewalIncomeBonus`. `WallEnclosureIncomeSystem.cs:208` reads `mults.WallIncome` but never adds `WallIncomeFromTech` — so the +20% TerracePlanning bonus is doubly inert (dead path + missing add).

**Fix:** Either delete the dead fields or wire them to real consumers.

### F-4 — Runai economy techs dead (HIGH)
**Files:** [TechTreeDB.cs:234-236](Assets/Scripts/Data/TechTree/TechTreeDB.cs#L234-L236) (parsed); [TechTreePanel.cs](Assets/Scripts/UI/Panels/TechTreePanel.cs), [EntityExtractors.cs](Assets/Scripts/UI/Panels/EntityExtractors.cs) (UI-exposed); zero gating consumers
**Severity:** High — three techs are flavor-only

`Runai_LongHaulTariffs`, `Runai_PackBazaar`, `Runai_EscortedCaravans` are parsed, exposed in chapel/UI, researched, stored in `FactionResearchState`. Zero systems gate behavior on `HasResearched(faction, "Runai_*")`.

**Fix:** Wire HasResearched checks into `TraderMovementSystem` / Runai trade income, or remove from JSON.

### F-5 — AI cultural unit mismatch (MEDIUM, latent)
**Files:** [SimpleAISystem.cs:221-240](Assets/Scripts/AI/SimpleAISystem.cs#L221-L240), [AIMilitaryManager.cs:519-544](Assets/Scripts/AI/Managers/AIMilitaryManager.cs#L519-L544)
**Severity:** Medium — latent footgun for build-order authors

`SimpleAISystem.FindTrainerForUnit` only handles generic IDs (Miner/Builder/Scout/Swordsman/Archer/Litharch). After age-up, `AIMilitaryManager.GetCultureUnitIdForClass` requests cultural variants like `Runai_Spearman`. The two AI subsystems run in parallel — `AIMilitaryManager` queues directly into a discovered Barracks via its own RecruitmentRequest path (line 468), so cultural unit training works through that path. But if a build-order Train step ever requests a cultural unit by full ID, `SimpleAISystem.TryTrainUnit` will silently return `Entity.Null` and the build-order step stalls. No build order does this today, so it's latent.

### F-6 — Per-position EntityQuery alloc (MEDIUM perf, T-3 confirmed)
**File:** [SpawnPlacementHelper.cs:86-89](Assets/Scripts/Systems/Training/SpawnPlacementHelper.cs#L86-L89)
**Severity:** Medium — perf at scale

`IsPositionClear` creates a fresh `EntityQuery` and three full-world `ToComponentDataArray<LocalTransform/Radius>` allocations on every check. Called from `FindEmptyPosition` up to 17 times per spawn. With 1000+ entities late game and a Longhouse batch (5 units), ~85 queries and ~5000 distance checks per batch.

**Fix:** Cache the query in OnCreate or per-frame.

### F-7 — TempleChapelBuildSystem dual representation (LOW-MEDIUM, design)
**File:** [TempleChapelBuildSystem.cs:85](Assets/Scripts/Systems/Buildings/TempleChapelBuildSystem.cs#L85)
**Severity:** Low-medium — undocumented dual state

Comment says "chapels are NOT standalone entities anymore" but `BuildingFactory.cs:1077-1082` and `1112-1117` show two paths that DO create ChapelTag entities for chapels. Are chapels entities or buffer slots? Both. The temple has `TempleChapelSlot` buffer for the build-progress timer, AND independent ChapelTag entities serve as click targets. Dual representation is undocumented and prone to going out of sync.

### F-8 — SectEffectSystem delta only covers 7 fields (verified safe)
**File:** [SectEffectSystem.cs:146-171](Assets/Scripts/Systems/Crystal/SectEffectSystem.cs#L146-L171) `ApplyMultiplierDelta`
**Severity:** N/A — verified safe

Handles MeleeDamage, RangedDamage, VaultInterest, BuildingHP, AttackSpeed, FogVisionBonus, RangedAccuracy. The other multipliers are live-queried each tick (AllIncome, WallIncome, BuildSpeed, etc.) so delta tracking isn't needed. Verified — task-001 fix is correctly scoped. Delta math at lines 150-170 is correct.

### F-9 — Two FactionEra writers (LOW, design)
**Files:** [AgeUpSystem.cs:89](Assets/Scripts/Systems/Buildings/AgeUpSystem.cs#L89), [TempleUpgradeSystem.cs:76](Assets/Scripts/Systems/Buildings/TempleUpgradeSystem.cs#L76)
**Severity:** Low — only matters if Era 3+ ever ships

AgeUpSystem always writes `FactionEra { Value = 2 }`. TempleUpgradeSystem writes `FactionEra { Value = TempleLevelConfig.GetEraForLevel(nextLevel) }`. Two paths can race or contradict. Not a bug today (only Era 1→2 implemented).

### F-10 — Wasted resources on duplicate research queue (LOW)
**File:** [ResearchSystem.cs:56-60](Assets/Scripts/Systems/Research/ResearchSystem.cs#L56-L60)
**Severity:** Low — minor exploit prevention

When `HasResearched` is true at start time, the queued tech is silently dropped. Cost was paid at queue time. No refund. Same in TrainingSystem (already-trained-or-mismatched-unit).

---

## Architectural Smells

- **Two parallel AI architectures** active (SimpleAISystem + AI*Manager). Both create train-queue items into the same Barracks; double-queueing risk. Separate audit (task-052), but interacts with cultural-unit handling.
- **Sect/Tech effects use four separate application surfaces** with asymmetric coverage (see F-1, F-2). Root of two critical bugs.
- **Dead-code surface area is large.** `LogSampleUnits`, the empty `if (db.TryGetTechnology...) {}` in ResearchSystem, unused `count` increments in TechEffectSystem (lines 125, 161, 192) all exist as cleanup-after-debugging artifacts.
- **Cost vs CostBlock split** — verbose but intentional. Acceptable.

---

## Verification of Surface-Scan Claims

| ID | Claim | Verdict | Notes |
|---|---|---|---|
| **T-1** | ResearchSystem.cs:104-106 empty completion block | **CONFIRMED cosmetic** | Empty `if` body after Debug.Log stripped. `techDef` unused. No behavior bug. |
| **T-2** | SpawnPlacementHelper.cs:65-67 non-deterministic Random | **REFUTED** | `CreateFromIndex(uint)` is deterministic. Multiplayer-safe. |
| **T-3** | SpawnPlacementHelper.cs:86 per-position EntityQuery alloc | **CONFIRMED real perf** | See F-6. |
| **T-4** | missing SetTechFlag hook | **CONFIRMED CRITICAL** | See F-1. |
| **T-5** | AgeUpSystem.cs:88-89 silent fail | **REFUTED** | Normal guarded block; rest of age-up doesn't depend on bank. Defensive, not a fail. |
| **T-6** | FactionSectState.cs:593 additive ResearchSpeed | **CONFIRMED but DESIGN-CORRECT** | Additive on 1.0 base = 1.15× speed. Pattern consistent with all other multiplicative fields. |
| **T-7** | Cost vs CostBlock duplicate | **CONFIRMED but INTENTIONAL** | Type segregation rationale (struct value-semantics vs class JSON-DTO) is sound. |
| **T-8** | TrainingSystem/ResearchSystem `Remaining <= 0f` rare double-fire | **REFUTED** | `Busy` byte flag set to 0 immediately on completion. `set.Add` returns false on duplicate. No double-fire. |
| **T-9** | TechTreeDB.cs:276-285 LogSampleUnits no-op | **CONFIRMED cosmetic** | Pure dead code from stripped Debug.Log. |
| **T-10** | TechEffectSystem.cs:213-264 per-unit DB lookup performance | **CONFIRMED low** | O(1) Dictionary lookup; ~5 calls per spawn. Real but minor. |
| **MB-27** | TrainingSystem.cs:262-264 missing braces | **REFUTED** | `if X; else Y;` — `else` correctly binds. Cosmetic, not a bug. |
| **MB-28** | BatchTrainingSystem.cs:223-225 missing braces | **REFUTED** | Same pattern. |

**Score: 4 of 12 are real meaningful bugs** (T-3 perf, T-4 critical, T-9 cosmetic dead code, T-10 low perf). Drop the other 8 from task-051's MB sweep.

---

## What I Verified Is Fine

- **Delta-tracking math in SectEffectSystem** (lines 146-375) — task-001 fix is correct for all 7 per-entity fields it covers.
- **Research/training cost-paid-at-queue-time** is consistent across UI, AI, and lockstep paths.
- **Determinism**: no `UnityEngine.Random` or `System.Random` in research/training/sect/tech systems.
- **Population gating** in TrainingSystem and BatchTrainingSystem properly tracks per-frame extras.
- **AgeUp build-order step** checks affordability AND choice building before spending; CultureChoicePopup does the same for human player.
- **Era gating at build UI** correctly disables Era 2+ buildings until faction era >= minEra.
- **TempleChapelBuildSystem** defers chapel-completion side effects to after iteration; calls `RecalculateAllPassives`.
- **Tech-effect timing** for the 4 effects TechEffectSystem actually handles: existing units boosted at completion, in-flight queue items boost at spawn, future units boost at spawn. All three timing categories covered consistently.
- **Determinism of AgeUp completion**: timer-based, no Random, all writes idempotent.
- **Cultural variants exist** in UnitFactory for all advertised IDs. No spawn requests fall to default fallback.

---

## Things I Deliberately Didn't Dig Into

- **TechTree.json content correctness** — content audit out of scope.
- **Lockstep determinism of train/research mutations** — covered by task-054.
- **BuildingConstructionSystem interaction with mults.BuildSpeed** — for buildings audit.
- **Combat damage interactions with sect mults** — task-055 territory; F-2 explains why newly trained units don't carry the mult.
- **TempleChapelSlot ↔ ChapelTag entity duality** — F-7 noted but not deeply traced.
- **AIBuildingManager research-queueing path** — separate audit.
