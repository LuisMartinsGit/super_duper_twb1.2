---
deft:
  id: task-deepdive-ai-2026-052
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
  labels: [code-quality, code-review, deep-dive, ai]
---

# AI subsystem deep-dive (replaces parallel-scan portion of task-050/051)

## Context

The earlier audits (task-050, task-051) used parallel surface-scan agents and produced too many style-level findings with ~15% false-positive rate. The user explicitly called the output "superficial" and asked for a real deep dive using the deft framework. This task is the **AI-subsystem do-over**: full file reads, traced data flows, verified findings only, architectural concerns separated from style nits, recent fixes validated against the current code.

Method:
1. **deft-review agent** dispatched with explicit "no Grep snippets, full Read" instruction
2. Read 18 files in full: `SimpleAISystem.cs`, `AIBuildOrder.cs`, `AISimpleDifficulty.cs`, `AIBrain.cs`, `AIBootstrap.cs`, all mining systems, `Miner.cs`, `Litharch.cs`, `CrystalPatchBootstrap.cs`, `IronDepositBootstrap.cs`, `GatherCommand.cs`, `AttackMoveCommand.cs`, `CommandRouter.cs`, training systems, `AgeUpSystem`
3. Traced 6 end-to-end paths: Train, Build, Mining, Replace-Lost, AgeUp, LaunchAttack
4. Each finding includes a data-flow trace as proof; each was re-verified after drafting

Outcome: **10 verified findings, 5 architectural smells, 9 confirmations of recent fixes still being correct, explicit out-of-scope list.** Significantly higher signal-per-line than the surface scan.

## User Value

A reliable, traced backlog of AI bugs. Most consequential is **F-1**, which is a regression I introduced in this very session — the 50/50 crystal-miner floor silently overrides the `Rush` strategy's intentional `SetCrystalTarget(1)`, neutralizing one of six strategies.

## Requirements

- R1: Triage every verified finding (F-1 through F-10).
- R2: Fix all High-severity findings (F-1, F-7) before next playtest.
- R3: Address Medium findings (F-2, F-3, F-4) within this milestone.
- R4: Decide go/no-go on each architectural smell.
- R5: Reproduce the recent-fixes-confirmation list before any future change touches those files.

## Acceptance Criteria

- [ ] F-1: Rush can opt out of the 50/50 crystal floor (or Rush gets a different mechanism than `SetCrystalTarget` to express "iron-heavy")
- [ ] F-2: Build steps that fail to dispatch any builder no longer leave HP=1 orphan UnderConstruction sites
- [ ] F-3: Eliminated AI brains stop spinning their think loop
- [ ] F-7: Litharch (and other unit factories) have `DesiredDestination` baked in like `Miner.cs` does
- [ ] F-4, F-8 resolved or explicitly punted with rationale
- [ ] F-5, F-6, F-9, F-10 either resolved or filed as low-priority follow-ups

---

## Verified Findings

### F-1 — `Rush` cannot opt out of the 50/50 crystal floor (gameplay regression I introduced)
**File:** [SimpleAISystem.cs:905-906](Assets/Scripts/AI/SimpleAISystem.cs#L905-L906)
**Severity:** High — silently neutralizes one of six strategies

**Trace:** `AssignIdleMiners` is called every think tick with `aiState.CrystalMinerTarget` (line 96). After snapshotting miners, lines 905-906 do `if (anyCadaver) targetCrystal = math.max(targetCrystal, totalMiners / 2);`. `CrystalNodeBootstrap.SpawnStartingCrystalPatches` seeds 5 cadavers per player at game start, so `anyCadaver` is true from frame 1. The `Rush` build order ([AIBuildOrder.cs:178-212](Assets/Scripts/AI/AIBuildOrder.cs#L178)) sets `SetCrystalTarget(1)` very late and front-loads iron — but the floor immediately overrides to `totalMiners/2`. With 6 miners during early Rush waves and `CrystalMinerTarget=0`, **3 of 6 miners go to crystal — exactly what the strategy was written to avoid**. The XML doc on `SetCrystalTarget` (`AIBrain.cs:73-79`) calls it a "FLOOR" but in practice the per-tick floor recomputation makes it impossible to specify a CEILING.

**Fix options:** (a) Negative `IntArg` means "ceiling, no floor"; (b) gate the 50/50 floor behind a separate `SetMinCrystalRatio` step; (c) change rule to `min(targetCrystal, totalMiners/2)` only when `targetCrystal > 0`.

### F-2 — `TryBuildBuilding` advances the step even when zero builders dispatched (orphan sites)
**File:** [SimpleAISystem.cs:281-282](Assets/Scripts/AI/SimpleAISystem.cs#L281-L282)
**Severity:** Medium — rare but breaks AI's recovery

**Trace:** After `CommandRouter.PlaceBuildingDirect` creates the building with HP=1 + UnderConstruction (line 274), `DispatchBuildersTo` is called with `maxBuilders: 2`. `DispatchBuildersTo` (lines 290-323) silently dispatches `0..maxBuilders` depending on idle-builder count. `TryBuildBuilding` returns `true` unconditionally (line 282), so the build-order step advances. If both builders are busy or the AI lost its only Builder, the new site sits at HP=1 with `UnderConstruction` and zero progress until some other builder finishes a current task and auto-chains via `BuildingConstructionSystem.FindNearbyUnfinishedBuilding`. That auto-chain only runs at LOS range from the FINISHING builder, so an orphan outside any active builder's LOS is permanent. The AI also won't queue another Builder because Builder isn't in `RegisterTrainedUnit`'s replacement list ([SimpleAISystem.cs:174-178](Assets/Scripts/AI/SimpleAISystem.cs#L174)).

**Fix:** Return `false` from `TryBuildBuilding` when `dispatched == 0` AND refund the spent cost (currently `FactionEconomy.Spend` is called on line 270 regardless).

### F-3 — `ReplaceLostUnits` wedges itself when the only trainer is gone (CPU sink for eliminated factions)
**File:** [SimpleAISystem.cs:531-559](Assets/Scripts/AI/SimpleAISystem.cs#L531-L559)
**Severity:** Medium — visible as wasted CPU late game

**Trace:** `DesiredMilitary` and `DesiredMiners` are monotonically increased (intentionally — see doc on `SimpleAIState.DesiredMilitary`). When the AI's only Hall is destroyed, `CountAliveMiners` returns 0, `CountQueuedByPredicate` returns 0, deficit equals `DesiredMiners`. `TryTrainUnit("Miner")` → `FindTrainerForUnit` returns `Entity.Null` → returns false silently. Same for military when the only Barracks dies. The brain entity isn't destroyed by `VictoryConditionSystem` (confirmed: only counts buildings, never touches `AIBrain`), so `SimpleAISystem.OnUpdate` keeps iterating eliminated factions forever.

**Fix:** In `OnUpdate` (line 79) bail when the faction has no Hall AND no Barracks AND no Temple, OR hook `VictoryConditionSystem.RecordElimination` to set `brain.IsActive = 0`.

### F-4 — `GatherCommand` is processed by `MiningSystem.ProcessIdleState` AND `GatheringSystem` in the same frame
**Files:** [MiningSystem.cs:128-159](Assets/Scripts/Systems/Work/MiningSystem.cs#L128) and [GatheringSystem.cs:41-130](Assets/Scripts/Systems/Work/GatheringSystem.cs#L41)
**Severity:** Medium — not yet broken, footgun for next edit

**Trace:** Both are `[UpdateInGroup(typeof(SimulationSystemGroup))]` with no `[UpdateBefore]/[UpdateAfter]` between them. `MiningSystem.ProcessIdleState` reads `GatherCommand`, sets `MinerState`, removes via per-update Temp ECB playing back at end of `MiningSystem.OnUpdate`. `GatheringSystem` queries `<RefRO<LocalTransform>, RefRO<GatherCommand>>` and writes `MinerState` + `DesiredDestination` via the EndSimulation ECB — only removing `GatherCommand` once `dist <= GatherRange`. Final state converges (both write the same fields with the same values) but work is duplicated and order is undefined. The "MiningSystem takes over" comment in `GatheringSystem` (line 19) describes a separation the code does not enforce.

**Fix:** Pick one. Either give `GatheringSystem` `[UpdateBefore(typeof(MiningSystem))]` and have `MiningSystem.ProcessIdleState` skip if already consumed, or delete `GatheringSystem` entirely and route player right-click through `MiningSystem.ProcessIdleState` like the AI does. Given the player-AI parity goal, the second is cleaner.

### F-5 — `SetCrystalTarget` clamp comment is stale
**File:** [SimpleAISystem.cs:188-189](Assets/Scripts/AI/SimpleAISystem.cs#L188-L189)
**Severity:** Low — documentation drift

**Trace:** Comment reads "Clamp at the system cap (4)" but actual clamp is `math.clamp(count, 0, MaxCrystalMiners)` and `MaxCrystalMiners = 16` (line 813). A typo'd `SetCrystalTarget(50)` is silently capped to 16, not 4 as promised.

**Fix:** Either restore the 4-cap (matches every shipped build order) or update the comment to "16".

### F-6 — `CountQueuedByPredicate` walks every faction's queues even though scoped to one faction
**File:** [SimpleAISystem.cs:605-630](Assets/Scripts/AI/SimpleAISystem.cs#L605-L630)
**Severity:** Low — wasted CPU + GC pressure on Hard

**Trace:** Called inside `ReplaceLostUnits` once per think tick per faction. Every call queries ALL `<FactionTag, TrainQueueItem>` entities via a fresh `CreateEntityQuery`, then filters inline. With 7 AI players + 0.5s think interval on Hard: `7 * (~12 buildings) * 2 (military+miner) = ~170 queue walks/sec`, each allocating Temp arrays.

**Fix:** Cache the query in `OnCreate`. Better: per-faction aggregator updated on Train+Death events; `ReplaceLostUnits` becomes O(1).

### F-7 — `Litharch` factory does not bake in `DesiredDestination` — AI Litharchs are paperweights
**File:** [Litharch.cs:125-192](Assets/Scripts/Entities/Units/Litharch.cs#L125-L192)
**Severity:** Medium — Turtle strategy ships 2 non-functional Litharchs

**Trace:** `Miner.cs:54-58` deliberately bakes in `DesiredDestination { Has = 0 }` "because MovementSystem's query requires DesiredDestination". `Litharch.Create(EntityManager, …)` does not. `MovementSystem.OnUpdate` line 236-240 queries `<RefRW<LocalTransform>, RefRW<DesiredDestination>>().WithAll<UnitTag>()`. Litharch has UnitTag. Without the component, Litharch is excluded from the move loop entirely. `TrainingSystem.SpawnUnit` only adds `DesiredDestination` when the building has a rally point set (lines 259-264); the AI never sets rally points. Net effect: a freshly trained AI Litharch sits at the Temple until `LitharchHealingSystem` lazy-adds DesiredDestination via ECB when chasing a heal target — but heal targets must come within search radius first, and there are no friendly units at the Temple.

Same applies to AI-trained Builder/Swordsman/Archer/Scout when no rally is set; they only get a destination when a command path (Build/Move/AttackMove) adds one.

**Fix:** Bake `DesiredDestination { Has = 0 }` into every unit factory the way `Miner.cs` does, or have `TrainingSystem.SpawnUnit` add a `Has=0` `DesiredDestination` when there's no rally point.

### F-8 — `TryAgeUp` returns `true` even when brain entity lookup fails (silent unrecoverable failure)
**File:** [SimpleAISystem.cs:493-512](Assets/Scripts/AI/SimpleAISystem.cs#L493-L512)
**Severity:** Low — rare, but failure mode is silent and unrecoverable

**Trace:** After spending resources (line 490), code does `var brainEntity = FindBrainEntity(em, faction); byte culture = Cultures.None; if (brainEntity != Entity.Null) { ... culture = AIBuildOrder.CultureFor(...); }`. If `FindBrainEntity` returns `Entity.Null`, `culture` stays `Cultures.None`. Hall is given `AgeUpState { Culture = Cultures.None }`, `FactionColors.SetFactionCulture(faction, Cultures.None)` is called, AI continues, `aiState.AgeUpIssued = 1`, build order advances. Age-up fires but faction is transitioned to a "no culture" branch — likely a soft-broken end state where the cultural unit/building roster never appears. Cost is spent so only recovery is restart.

**Fix:** If `FindBrainEntity` fails after resources have been spent, refund the cost. Better: pass the brain entity (already known in `OnUpdate`) into `TryAgeUp` instead of re-querying it.

### F-9 — `_rngState` is shared across all factions (subtle determinism quirk)
**File:** [SimpleAISystem.cs:60, 988-999](Assets/Scripts/AI/SimpleAISystem.cs#L60)
**Severity:** Low — gameplay can feel synchronized when it shouldn't

**Trace:** `_rngState` is an instance field (one instance per world). Every `NextRandFloat01` call (build placement angles line 377, optional-step skips line 115, AgeUp culture choice line 498) advances the same state across all faction iterations. Iteration order is determined by ECS chunk layout (deterministic across hosts in multiplayer — not a desync risk). But two factions with the same strategy roll the SAME sequence of optional-skip dice on consecutive ticks, biasing both to either skip-skip or take-take depending on parity.

**Fix:** Seed RNG per-faction in `SimpleAIState` (e.g. `uint RngState`) and use that field instead of the shared instance variable.

### F-10 — `TryFindBuildPosition` allocates ~6 NativeArrays + a managed bool[] every call, scaled by total building count
**File:** [SimpleAISystem.cs:350-402](Assets/Scripts/AI/SimpleAISystem.cs#L350-L402)
**Severity:** Low — performance, surfaces only on Hard with 8 players

**Trace:** Each `TryBuildBuilding` invocation calls `TryFindBuildPosition`, which snapshots `<BuildingTag, LocalTransform>`, allocates a managed `bool[]`, then loops up to ~144 candidate positions, each calling `BuildCommandHelper.IsValidBuildPosition` which itself allocates two more `NativeArray`s plus terrain probes for 5 corners. With 7 AI factions × 2 ticks/sec × 144 candidates × ~5 NativeArray allocs = **~10K NativeArray allocations/sec** in the worst case. Burst-disabled because `SimpleAISystem` is managed.

**Fix:** `IsValidBuildPosition` should accept a pre-snapshot of buildings/obstacles so the AI can call it 144× without re-querying.

---

## Architectural Smells

- **`SimpleAISystem` is a 1000-line managed `SystemBase` doing five jobs** (build order execution, miner tasking, replacement training, attack launching, RNG). A more honest factoring: one system per concern (`AIMinerTaskingSystem`, `AIReplacementSystem`, `AIBuildOrderExecutorSystem`, `AIAttackLauncherSystem`) all reading the same `SimpleAIState`. Gives proper update ordering, isolates per-system cost, lets you Burst-compile parts that don't touch managed types.

- **The `[DisableAutoCreation]` Manager/Behavior layer is dead code in the same namespace as the live system** (`AIEconomyManager`, `AIBuildingManager`, `AIMilitaryManager`, `AIScoutingBehavior`, `AICrystalHuntBehavior`, `AIDefenseBehavior`, `AITacticalManager`, `AIMissionManager`, `AIStrategyEvaluator`). They share state types (`AIEconomyState`, etc.) which `AIBootstrap` STILL initializes. Risk: a future PR re-enables one and finds state consumers diverged from `SimpleAIState`. Recommendation: delete the dead managers and unused state structs in one cleanup pass.

- **`BuildOrderStep.IntArg` is overloaded for two semantically unrelated things** (SetCrystalTarget count, LaunchAttack min units). A third numeric step would force a switch on Kind. Worth promoting to a struct with named fields now while the surface is small.

- **The build-order pointer never rewinds.** `aiState.StepIndex++` is the only mutation. If a step's effect is undone (building destroyed mid-construction), there's no way to re-attempt. Replacement training partially compensates for units, not for buildings/research.

- **Repeated snapshot-everything-then-filter** pattern in `AssignIdleMiners`, `CountAliveMilitary`, `CountAliveMiners`, `CountQueuedByPredicate`, `FindClosestEnemyOf<T>`, `FindFactionBuilding<T>`. None individually hot but together make `OnUpdate` linear in `entities × factions × ticks/sec`. Caching faction-scoped queries in `OnCreate` would help.

---

## Recent Fixes Confirmed Still Correct

- **`MovementSystem` flow-field cache fix** (lines 421-445): cache untouched when field is null, so `needsRefresh` stays true. ✓
- **Miner factory `DesiredDestination`** (`Miner.cs:54-58`): baked in at `Has = 0`. ✓
- **GatherCommand pre-warms flow field** (`GatherCommand.cs:240-241`): unconditional pre-warm when `UseFlowFields`. ✓
- **50/50 crystal floor**: mechanically correct, but see F-1 — overrides build-order intent for Rush.
- **`MaxCrystalMiners = 16`** (line 813): bumped from 4. But callsite comment (line 188) is stale — see F-5.
- **Litharch EM-overload returning a non-deferred entity** (`Litharch.cs:117-192`): returns a real Entity. ✓
- **AgeUp gating on choice building** (lines 765-777): accepts both `ChoiceBuildingTag` and `TempleOfRidan` with under-construction check. ✓
- **Replacement-training avoids double-counting** (lines 540-557): `TryTrainUnit` called directly. ✓
- **Battalion-leader-only attack issuance** (lines 644-692): `BattalionMemberData` filtered out. ✓
- **`PassabilityBuildingSync.FootprintPaddingCells = 0`** (line 34): correct per the comment. ✓

---

## Bonus finding (out of subsystem, caught in passing)

- [VictoryConditionSystem.cs:178-179](Assets/Scripts/Systems/Core/VictoryConditionSystem.cs#L178-L179) has the same missing-`else` pattern we found 30 of in task-051 — `result = "DEFEAT"` is dead. Add to the missing-brace sweep.

## Out of Scope

- `[DisableAutoCreation]` Manager/Behavior code — covered architecturally above; no individual-bug audit because they don't run.
- `AILogger` per-faction file rotation behavior.
- Multiplayer `CommandSource.AI` vs `CommandSource.LocalPlayer` propagation — gitStatus shows pre-multiplayer state.
- `AIStrategyEvaluator` strategy-transition balance — `[DisableAutoCreation]`.
- `BuildingFactory` cultural variants — read enough for AgeUp prereq path; full audit is its own task.
- `CrystalAISystem` (the curse) — different domain.
