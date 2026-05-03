---
deft:
  id: task-audit-may-2026-systemwide-051
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
  labels: [code-quality, code-review, audit, systemwide]
---

# May 2026 system-wide code-health audit (combat / economy / multiplayer / research / crystal / world+UI)

## ⚠️ POST-DEEP-DIVE VERIFICATION STATUS (2026-05-03)

The 9 deep-dive audits in task-052..061 verified each finding here against the live code. **Many MB-* claims are false positives** — the surface scan saw `if X; else Y;` indentation patterns and called them missing-brace bugs, but `else` correctly binds to the most recent unmatched `if` per C# parsing rules. The actual confirmed missing-brace count is **~12, not 30**.

**The MB sweep should fix only these confirmed bugs:**

| ID | File:Line | Status | Source |
|---|---|---|---|
| MB-1 | BattalionSyncSystem.cs:435-437 | ✅ CONFIRMED | task-053 F-1 |
| MB-2 | BattalionSyncSystem.cs:514-516 | ✅ CONFIRMED | task-053 F-2 |
| MB-3 | UnitSeparationSystem.cs:262-264 | ✅ CONFIRMED | task-053 F-3 |
| MB-4 | ResourceTickSystem.cs:62-64 | ❌ REFUTED — TryAdd no-ops on existing key | task-056 |
| MB-5 | TradingPostSystem.cs:549-550 | ✅ CONFIRMED | task-056 |
| MB-6 | ForgeSupplySystem.cs:273-276 | ❌ REFUTED — cosmetic, else binds correctly | task-056 |
| MB-7 | ForgeSupplySystem.cs:281-282 | ❌ REFUTED — single-statement if, no else needed | task-056 |
| MB-8 | BuildingConstructionSystem.cs:156-159 | ❌ REFUTED — cosmetic | task-056 |
| MB-9 | BuildingConstructionSystem.cs:477-480 | ❌ REFUTED — cosmetic | task-056 |
| MB-10..13 | CombatDamageHelper.cs (4 sites) | ❌ ALL REFUTED — cosmetic | task-055 |
| MB-14..17 | ProjectileSystem.cs (4 sites) | ❌ ALL REFUTED — cosmetic | task-055 |
| MB-18..20 | TargetingSystem.cs (3 sites) | ❌ ALL REFUTED — cosmetic | task-055 |
| MB-21 | CrystalExtinctionSystem.cs:109-110 | ✅ CONFIRMED CRITICAL — multiplayer determinism break | task-058 F-2 |
| MB-22 | CrystalAISystem.cs:349-352 | ❌ REFUTED — cosmetic | task-058 |
| MB-23 | EnforcementNodeSystem.cs:88-91 | ❌ REFUTED — cosmetic | task-058 |
| MB-24 | SuppressionNodeSystem.cs:91-94 | ❌ REFUTED — cosmetic | task-058 |
| MB-25 | CommandRouter.cs:437-440 | ⚠️ COSMETIC — `if X; else Y;` binds correctly; no behavioral bug |
| MB-26 | CommandRouter.cs:515-518 | ⚠️ COSMETIC — same pattern |
| MB-27 | TrainingSystem.cs:262-264 | ❌ REFUTED — cosmetic | task-057 |
| MB-28 | BatchTrainingSystem.cs:223-225 | ❌ REFUTED — cosmetic | task-057 |
| MB-29 | FogOfWarManager.cs:240-242 | ❌ REFUTED — `_tex` is never null at the call | task-059 |
| MB-30 | MusicManager.cs:117-122 | ✅ CONFIRMED CRITICAL — menu music plays in-game | task-059 F-2 |

**Plus NEW missing-brace bugs the surface scan missed (deep-dives caught):**

| File:Line | Bug | Source |
|---|---|---|
| SelectionSystem.cs:463-465 | box-select military filter is a no-op | task-053 F-4, task-060 F-1 |
| InGameMenuPanel.cs:67-72 | Toggle() can never close menu | task-060 F-2 |
| SpellPanel.cs:173-175 | active spell can't be cancelled | task-060 F-3 |
| MinimapRenderer.cs:759-761 | right-click also snaps camera | task-059 F-1 |
| EntityActionPanel.cs:211-213 | both placement labels render stacked | task-060 F-4, task-061 B-4 |
| MultiplayerLobbyUI.cs:396-399 | host color cycles twice per click | task-060 F-5 |
| SpellState.cs:73-78 | dead-store on every expiring cooldown | task-055 F-7 |
| VeilstingerCombatSystem.cs:140, 242 | missing `else if` causes oscillation | task-058 F-1 |

**Net: 12 confirmed missing-brace bugs total.** MB-25 and MB-26 are cosmetic-but-fragile (add braces for readability, no behavior change needed).

The W-* claims in this task are mostly correct (W-1, W-2, W-3, W-6, W-9, W-10 all CONFIRMED). W-7 (PassabilityGrid race), W-8 (FloatingHealthBars predicate), W-11 (FogOfWarSystem NRE) are REFUTED — surface scan misread surrounding context.

The other category-prefixed claims (E-*, M-*, T-*, X-*, F-*) — see the per-task companion deep-dive (052-061) for verification status of each.

## Context

Companion to [task-audit-may-2026-050](../task-audit-may-2026-050/task.md), which covered AI / movement / spawn-presentation. This task extends the audit to **every remaining domain** in `Assets/Scripts/`:

- **Combat** — `Systems/Combat/`, projectiles, damage tables, healing, spells/buffs/debuffs
- **Economy** — `Economy/`, `Systems/Economy/` trading, smelter, supplies/iron/crystal banks, population, alanthor walls
- **Multiplayer / Commands / Input** — `Multiplayer/`, lockstep, NetworkIds, every `CommandTypes/*.cs`, `RTSInputManager`
- **Tech / Research / Sect / Training** — `Data/TechTree/`, `Systems/Research/`, `FactionResearchState`, `FactionSectState`, `SectEffectSystem`, `TechEffectSystem`, training queues
- **Crystal / Curse / Construction** — `Systems/Crystal/`, crystal node families, cadaver factory, construction system
- **World / Terrain / Fog / Influence / UI Panels** — `World/`, `Influence/`, `Systems/Visibility/`, `UI/Panels/`, `UI/HUD/` (sans already-fixed), `UI/Menus/`, `Audio/`

Six parallel read-only Explore agents on **2026-05-02**, ~293 C# files surveyed, agent claims spot-checked against the live code. Already-fixed items from `.deft/tasks/001..049` and `task-050` were filtered out before reporting.

## User Value

A triaged, system-wide bug + dead-code backlog. The dominant theme — **endemic missing-brace / missing-else bugs scattered across 25+ sites in 11 files** — is now the highest-leverage fix because it explains symptom clusters across mining, combat, formations, training, audio, and lockstep. A single grep-and-format pass closes most of them.

## Requirements

- R1: Triage every finding into `keep / fix / not-an-issue`.
- R2: Fix all confirmed Critical/High items or spin off individual `task-*` entries for them.
- R3: Document any "won't fix" decisions with a one-line reason.
- R4: Verify findings marked `[unverified]` against the live code before deciding.
- R5: Do not regress recent fixes (BlendRadius=2m, MovementSystem flow-field cache, observer ResourceHUD, Miner DesiredDestination, etc.).

## Acceptance Criteria

- [ ] All 25+ confirmed missing-brace bugs fixed (single sweep PR is fine)
- [ ] `ResearchSystem.cs:104-106` empty-completion-block branch either filled in or removed
- [ ] `LockstepManager.ExecuteCommand` Ability case added (or proven unnecessary)
- [ ] All flow-field-pre-warm-missing commands either pre-warm or are documented as "no benefit" (BuildCommand, RepairCommand, HealCommand, ConvertCommand, PatrolCommand)
- [ ] No new compile warnings introduced

---

## The Big One: Endemic Missing-Brace / Missing-Else Pattern

We already fixed **four** instances earlier this session (PresentationSpawnSystem, MiningSystem, CrystalMiningSystem, SkirmishLobbyUI). The agents found **23 more confirmed** across the codebase, all the same shape:

```csharp
if (cond)
    DoA();
    DoB();   // ← always runs, was meant to be inside if or after else
```

or

```csharp
if (cond)
    DoA();
else
    DoB();
    DoC();   // ← always runs, was meant to be inside else
```

**This is the single highest-impact theme of the audit.** Half of the entries are HIGH severity because they cause silent logic failures in hot systems. Recommended approach: one sweep PR that touches every file below, plus a Roslyn / EditorConfig rule (`csharp_prefer_braces = true:warning`) so it can't regress.

| # | File:Line | Description | Severity |
|---|-----------|-------------|----------|
| MB-1 | [BattalionSyncSystem.cs:435-437](Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs#L435-L437) | combat alternate-direction probe always snaps back | critical |
| MB-2 | [BattalionSyncSystem.cs:514-516](Assets/Scripts/Systems/Movement/BattalionSyncSystem.cs#L514-L516) | formation-slot detour always snaps back | critical |
| MB-3 | [UnitSeparationSystem.cs:262-264](Assets/Scripts/Systems/Movement/UnitSeparationSystem.cs#L262-L264) | building push always uses Z axis (scrapes edges) | critical |
| MB-4 | [ResourceTickSystem.cs:62-64](Assets/Scripts/Economy/ResourceTickSystem.cs#L62-L64) | supplies tick: TryAdd runs after update; harmless today (TryAdd no-ops on existing key) but the intent is broken — fix to add `else` for clarity | high |
| MB-5 | [TradingPostSystem.cs:549-550](Assets/Scripts/Systems/Economy/TradingPostSystem.cs#L549-L550) | distance-based position calc immediately overwritten by fallback | high |
| MB-6 | [ForgeSupplySystem.cs:273-276](Assets/Scripts/Systems/Work/ForgeSupplySystem.cs#L273-L276) | AddComponentData runs after SetComponentData unconditionally | high |
| MB-7 | [ForgeSupplySystem.cs:281-282](Assets/Scripts/Systems/Work/ForgeSupplySystem.cs#L281-L282) | StopMoving SetComponentData missing HasComponent guard | medium |
| MB-8 | [BuildingConstructionSystem.cs:156-159](Assets/Scripts/Systems/Buildings/BuildingConstructionSystem.cs#L156-L159) | builder next-site assignment skipped | high |
| MB-9 | [BuildingConstructionSystem.cs:477-480](Assets/Scripts/Systems/Buildings/BuildingConstructionSystem.cs#L477-L480) | same pattern in BuildCommandSystem | high |
| MB-10 | [CombatDamageHelper.cs:111-113](Assets/Scripts/Systems/Combat/CombatDamageHelper.cs#L111-L113) | LastDamagedByFaction tracking write skipped | high |
| MB-11 | [CombatDamageHelper.cs:119-121](Assets/Scripts/Systems/Combat/CombatDamageHelper.cs#L119-L121) | LastAttackerEntity tracking write skipped | high |
| MB-12 | [CombatDamageHelper.cs:140-142](Assets/Scripts/Systems/Combat/CombatDamageHelper.cs#L140-L142) | spell debuff add/set | high |
| MB-13 | [CombatDamageHelper.cs:154-156](Assets/Scripts/Systems/Combat/CombatDamageHelper.cs#L154-L156) | spell debuff add/set (second site) | high |
| MB-14 | [ProjectileSystem.cs:329-331](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L329-L331) | projectile-impact damage tracking | high |
| MB-15 | [ProjectileSystem.cs:337-339](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L337-L339) | same pattern, second site | high |
| MB-16 | [ProjectileSystem.cs:391-393](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L391-L393) | same pattern, third site | high |
| MB-17 | [ProjectileSystem.cs:398-400](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L398-L400) | same pattern, fourth site | high |
| MB-18 | [TargetingSystem.cs:497-499](Assets/Scripts/Systems/Combat/TargetingSystem.cs#L497-L499) | battalion auto-targeting propagation | high |
| MB-19 | [TargetingSystem.cs:508-510](Assets/Scripts/Systems/Combat/TargetingSystem.cs#L508-L510) | same pattern, second site | high |
| MB-20 | [TargetingSystem.cs:518-520](Assets/Scripts/Systems/Combat/TargetingSystem.cs#L518-L520) | same pattern, third site | high |
| MB-21 | [CrystalExtinctionSystem.cs:109-110](Assets/Scripts/Systems/Crystal/CrystalExtinctionSystem.cs#L109-L110) | seed assignment bypass | high |
| MB-22 | [CrystalAISystem.cs:349-352](Assets/Scripts/Systems/Crystal/CrystalAISystem.cs#L349-L352) | OwnerNode handling | medium |
| MB-23 | [EnforcementNodeSystem.cs:88-91](Assets/Scripts/Systems/Crystal/EnforcementNodeSystem.cs#L88-L91) | buff assignment can fail silently | high |
| MB-24 | [SuppressionNodeSystem.cs:91-94](Assets/Scripts/Systems/Crystal/SuppressionNodeSystem.cs#L91-L94) | debuff assignment can fail silently | high |
| MB-25 | [CommandRouter.cs:437-440](Assets/Scripts/Core/Commands/CommandRouter.cs#L437-L440) | IssueAbilityDirect SetComponentData runs unconditionally | high |
| MB-26 | [CommandRouter.cs:515-518](Assets/Scripts/Core/Commands/CommandRouter.cs#L515-L518) | PlaceBuildingDirect UnderConstruction set unconditionally | high |
| MB-27 | [TrainingSystem.cs:262-264](Assets/Scripts/Systems/Training/TrainingSystem.cs#L262-L264) | DesiredDestination add/set | high |
| MB-28 | [BatchTrainingSystem.cs:223-225](Assets/Scripts/Systems/Training/BatchTrainingSystem.cs#L223-L225) | same pattern | high |
| MB-29 | [FogOfWarManager.cs:240-242](Assets/Scripts/World/FogOfWar/FogOfWarManager.cs#L240-L242) | `_tex.Reinitialize` runs even when `_tex == null` (NRE risk) | critical |
| MB-30 | [MusicManager.cs:117-122](Assets/Scripts/Audio/MusicManager.cs#L117-L122) | menu music always plays after game music — wrong scene check effect | critical |

---

## Other Findings by Domain

### Combat / Damage / Projectiles / Spells / Healing

| # | File:Line | Finding | Severity | Category |
|---|-----------|---------|----------|----------|
| C-1 | [SectBuildingAuraSystem.cs:163-174, 218-228](Assets/Scripts/Systems/Crystal/SectBuildingAuraSystem.cs#L163) | overwrites only ArmorBonus or DamageReflect on existing SpellBuff, doesn't clear other fields. Inconsistent stacking when buffs overlap. | medium | inconsistency |
| C-2 | [RangedCombatSystem.cs:293-295](Assets/Scripts/Systems/Combat/RangedCombatSystem.cs#L293-L295) | DamageReflect uses pre-clamp damage value while target health uses post-clamp; asymmetric reflect. | medium | bug |
| C-3 | [ProjectileSystem.cs:203-206](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L203-L206) | piercing projectile uses stale snapshot — relies on per-frame snapshot freshness. Document or guard. | low | missing-impl |
| C-4 | [LitharchHealingSystem.cs:81](Assets/Scripts/Systems/Work/LitharchHealingSystem.cs#L81) | uses `goto ProcessHealing` — outdated control flow, no other system in the codebase uses goto. | low | outdated-pattern |

### Economy / Trading / Population

| # | File:Line | Finding | Severity | Category |
|---|-----------|---------|----------|----------|
| E-1 | [FactionEconomy.cs:32-36](Assets/Scripts/Economy/FactionEconomy.cs#L32-L36) | `ClearCache()` clears `_bankCache` and `_bankQueryInitialized` but doesn't reset `_bankQuery = default`. After menu-bounce, the query handle could reference a disposed world. | high | missing-impl |
| E-2 | [ResourceTickSystem.cs:95-128](Assets/Scripts/Economy/ResourceTickSystem.cs#L95-L128) | supplies and other resources both write to `FactionResources` in same frame via different read-modify-write paths. Last-writer-wins on contended faction. `[unverified]` — claim is plausible but second pass would re-read; needs trace. | high (if real) | race-condition |
| E-3 | [GathererHutIncomeSystem.cs:62-63](Assets/Scripts/Economy/GathererHutIncomeSystem.cs#L62-L63) | redundant `AddComponent<FarmBuildOrder>` immediately followed by `SetComponentData`; AddComponent already initialises the struct. Cosmetic. | low | dead-code |
| E-4 | [ResourceTickSystem.cs:85-86](Assets/Scripts/Economy/ResourceTickSystem.cs#L85-L86) | per-faction supplies clamped to upper bound but no `Clamp()` call → no lower-bound check. If income ever goes negative, bypasses `FactionResources.Clamp` floor. | medium | inconsistency |
| E-5 | BuildingConstructionSystem direct EM-during-iteration | uses `em.AddComponentData` mid `SystemAPI.Query` foreach (lines 156-159, 477-480). Same anti-pattern as task-017. | medium | outdated-pattern |

### Multiplayer / Commands / Input

| # | File:Line | Finding | Severity | Category |
|---|-----------|---------|----------|----------|
| M-1 | [LockstepManager.cs ExecuteCommand switch](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs) | claimed missing case for `LockstepCommandType.Ability` despite being queued at [CommandRouter.LockstepQueue.cs:295](Assets/Scripts/Core/Commands/CommandRouter.LockstepQueue.cs#L295) — abilities serialized but never executed on remote peers. **`[unverified]`** — agent claim, must verify line-by-line before fixing. If true, **CRITICAL**. | critical (if real) | missing-impl |
| M-2 | [LockstepManager.cs:573-575](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L573-L575) | `catch (Exception)` block has weird indentation but compiles fine — not a syntax error. Style only. (False positive on the agent's "compile error" claim.) | low | nitpick |
| M-3 | [LockstepManager.cs:168-171](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L168-L171) | uses `Time.deltaTime` for tick accumulation; framerate-dependent. Two peers at 30fps and 60fps will desync. **`[unverified]`** — should be using a fixed tick rate. | critical (if real) | bug |
| M-4 | PatrolCommand / HealCommand / ConvertCommand / BuildCommand / RepairCommand | none of them pre-warm the flow field at command-issue time. We fixed Move/AttackMove/Gather; the rest still cold-start the BFS, costing a frame or two of direct-line movement (the same bug we just fixed for miners — units can clip a building corner on first frame). | high | inconsistency |
| M-5 | [LockstepManager.cs:387-392](Assets/Scripts/Multiplayer/Lockstep/LockstepManager.cs#L387-L392) | `ComputeGameStateChecksum` only includes `NetworkedEntity` and `Health`; doesn't include command components. Desyncs in MoveCommand/AttackCommand state would go undetected for tens of seconds. | medium | missing-impl |
| M-6 | [NetworkIdGenerator.cs:223-238](Assets/Scripts/Multiplayer/NetworkIdGenerator.cs#L223-L238) | no overflow guard if `_bootstrapNextId` exceeds `BOOTSTRAP_RESERVE` (1M); silent wrap into the tick-allocated range. | medium | missing-impl |
| M-7 | [CommandRouter.cs:605-658](Assets/Scripts/Core/Commands/CommandRouter.cs#L605-L658) | `ClearAllCommands` clears most commands but misses Convert/Patrol-related state. | medium | inconsistency |
| M-8 | [LockstepBootstrap.cs:127](Assets/Scripts/Multiplayer/Lockstep/LockstepBootstrap.cs#L127) | `HostPort` not validated as non-zero before `InitializeAsHost`; lockstep silently fails if 0. | medium | missing-impl |
| M-9 | [RTSInputManager.cs:109-110](Assets/Scripts/Input/RTSInputManager.cs#L109-L110) | observer mode blocks all hotkeys including control-group save/load (Ctrl+1..9). Either intentional (then document) or too broad. | medium | inconsistency |

### Tech / Research / Sect / Training

| # | File:Line | Finding | Severity | Category |
|---|-----------|---------|----------|----------|
| T-1 | [ResearchSystem.cs:104-106](Assets/Scripts/Systems/Research/ResearchSystem.cs#L104-L106) | **VERIFIED** `if (db.TryGetTechnology(techId, out var techDef)) { }` — empty body. `techDef` fetched, never used. Tech effects rely on `researchState.CompleteResearch` firing an event picked up by `TechEffectSystem` (line 97) — verify the event path actually fires, or this is dead code. | medium | dead-code |
| T-2 | [SpawnPlacementHelper.cs:65-67](Assets/Scripts/Systems/Training/SpawnPlacementHelper.cs#L65-L67) | uses `Unity.Mathematics.Random.CreateFromIndex((uint)attempt)` with **per-attempt** seed. Each fallback retry uses a different deterministic seed, but the seed isn't tied to the lockstep tick — peers attempting different fallback positions will desync. | high | bug (lockstep) |
| T-3 | [SpawnPlacementHelper.cs:86](Assets/Scripts/Systems/Training/SpawnPlacementHelper.cs#L86) | `em.CreateEntityQuery(...)` is called per position-test (up to 16 per spawn). Should cache. | medium | performance |
| T-4 | sect-tech `SetTechFlag` hook | claimed by agent: `FactionSectState.SetTechFlag` defined but no system calls it on sect-tech research completion. **`[unverified]`** — must check `TechEffectSystem.OnTechCompleted` and any sect-specific completion handler. If true, sect tech flags never set. | critical (if real) | missing-impl |
| T-5 | [AgeUpSystem.cs:88-89](Assets/Scripts/Systems/Buildings/AgeUpSystem.cs#L88-L89) | `FactionEra = 2` only set if `FactionEconomy.TryGetBank` succeeds. If bank lookup fails, age-up completes but era stays at 1, breaking subsequent era-gated techs. Add a fallback or assert. | high | missing-impl |
| T-6 | [FactionSectState.cs:593](Assets/Scripts/Economy/FactionSectState.cs#L593) | `m.ResearchSpeed += 0.15f * scaling` — additive on a multiplier semantically meant to be multiplicative. Multiple +0.15 stacks linearly instead of compounding. | medium | bug |
| T-7 | `Cost` (Core/Types) vs `CostBlock` (TechTreeDB) | two parallel cost representations with identical fields. | medium | inconsistency |
| T-8 | [TrainingSystem.cs:100](Assets/Scripts/Systems/Training/TrainingSystem.cs#L100) / [ResearchSystem.cs:89](Assets/Scripts/Systems/Research/ResearchSystem.cs#L89) | both use `Remaining <= 0f` — completion can fire twice across a frame jump. Use `< 0f` or zero-then-mark-done. | low | bug (rare) |
| T-9 | [TechTreeDB.cs:276-285](Assets/Scripts/Data/TechTree/TechTreeDB.cs#L276-L285) | `LogSampleUnits` is a no-op (empty `{}` after `TryGetValue`). Debug stub left behind. | low | dead-code |
| T-10 | [TechEffectSystem.cs:213-264](Assets/Scripts/Systems/Research/TechEffectSystem.cs#L213-L264) | unit-spawn loop calls `TechTreeDB.GetTechnology` per unit per completed tech. Cache the per-faction completed-tech effect block once, apply to spawn. | low | performance |

### Crystal / Curse / Construction

| # | File:Line | Finding | Severity | Category |
|---|-----------|---------|----------|----------|
| X-1 | [BuildingConstructionSystem.cs:176-186](Assets/Scripts/Systems/Buildings/BuildingConstructionSystem.cs#L176-L186) | building HP scaled to construction progress, but if it takes damage mid-build, `hp.Max` isn't recalculated to match. Visual HP and construction progress drift. | medium | missing-impl |
| X-2 | [CrystalAIState.HarassTimer / UnitSpawnTimer](Assets/Scripts/Core/Components/CrystalComponents.cs) | initialised in `CrystalMainNode.Create` but never read in any system. Only `BuildTimer` and `ExpansionTimer` are used. Dead fields. | low | dead-code |
| X-3 | CrystalTag vs CrystalUnitTag | `RestorationNodeSystem` heals `CrystalTag`, but `EnforcementNodeSystem` only buffs `CrystalUnitTag` — buildings get healed but not buffed. Pick one consistent predicate. | medium | inconsistency |
| X-4 | [BuildingConstructionSystem.cs:153-172](Assets/Scripts/Systems/Buildings/BuildingConstructionSystem.cs#L153-L172) | builder with no nearby unfinished site sets `GuardPoint = current position`. Builder strands at the edge of a finished construction zone forever. Should walk back to Hall. | low | missing-impl |
| X-5 | Cadaver decay / persistence | cadavers never expire — if crystal is never mined, they stay forever. Acceptable today but worth a TTL once the world fills with them in long games. | low | missing-impl |

### World / Terrain / Fog / Influence / UI

| # | File:Line | Finding | Severity | Category |
|---|-----------|---------|----------|----------|
| W-1 | [EntityActionPanel.cs](Assets/Scripts/UI/Panels/EntityActionPanel.cs) lines 902, 1088, 1224, 1247, 1267, 1297, 1364, 1464, 1549, 1557 | **10+ per-frame `new GUIStyle(...)` allocations** in OnGUI tooltip / queue / progress-bar paths. Cache them in `BuildLocalStyles` like ResourceHUD does. Same pattern as the already-fixed task-023 cleanup but in a different file. | high | performance |
| W-2 | [DayNightCycle.cs:228-232](Assets/Scripts/World/DayNightCycle.cs#L228-L232) | per-frame `Camera.main` lookup in `UpdateCloudShadows`. Cache in Awake (sibling to task-024). | high | performance |
| W-3 | [GathererHutAreaDisplay.cs:106](Assets/Scripts/UI/HUD/GathererHutAreaDisplay.cs#L106) | per-frame `GameObject.Find("PlacementPreview")` while placing. Should be a static setter from BuilderCommandPanel. | high | performance |
| W-4 | [MinimapUI.cs:217](Assets/Scripts/UI/HUD/MinimapUI.cs#L217) | per-frame `FindFirstObjectByType<FogOfWarManager>` fallback when FoW disabled. Cache once. | medium | performance |
| W-5 | [MinimapUI.cs:373-390](Assets/Scripts/UI/HUD/MinimapUI.cs#L373-L390) | RTSCameraRig / CameraController found on demand each frame. Cache in Awake. | medium | performance |
| W-6 | [EntityActionPanel.cs:90-92](Assets/Scripts/UI/Panels/EntityActionPanel.cs#L90-L92) | `try { ... } catch { }` swallows everything in OnGUI without logging. Hides UI bugs. Either log + rethrow or remove. | medium | dead-code / hidden-bug |
| W-7 | [PassabilityGrid.cs](Assets/Scripts/World/Terrain/PassabilityGrid.cs) | no validation that `ProceduralTerrain` finished generating before grid sampling begins. If sampled early, all heights = 0 → the whole map registers passable. Race condition at boot. | medium | missing-impl |
| W-8 | [FloatingHealthBars.cs:61, 97](Assets/Scripts/UI/HUD/FloatingHealthBars.cs#L61) | uses two different visibility predicates — `HasComponent<Health>` inline (line 61) vs a separate check (line 97). Should call the same canonical "is visible" helper used by `FogVisibilitySyncSystem`. | medium | inconsistency |
| W-9 | [OptionsMenuUI.cs:89-124](Assets/Scripts/UI/Menus/OptionsMenuUI.cs#L89-L124) | `LoadAndApplySettings` runs at boot but slider changes don't apply within-session — only persisted to PlayerPrefs and applied at next boot. | low | missing-impl |
| W-10 | [InfluenceManager.cs:117-119](Assets/Scripts/Influence/InfluenceManager.cs#L117-L119) | empty `else { /* comment */ }` blocks — dead handlers. | low | dead-code |
| W-11 | [FogOfWarSystem.cs:126](Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs#L126) | no null-check on `EntityViewManager` in `FogVisibilitySyncSystem.OnUpdate` — if EVM is unloaded between frames, line 140 derefs null. | low | bug (NRE risk) |

---

## False Positives (agent flagged, verified not-an-issue)

| # | Claim | Reality |
|---|-------|---------|
| FP-1 | `LockstepManager.cs:573-575` claimed compile-error catch block | Verified — valid C#, just oddly indented. Style only. |
| FP-2 | `ResearchSystem.cs:104-106` claimed CRITICAL "research completion broken" | Verified — empty `if` body is dead code, but `researchState.CompleteResearch` (line 97) still fires the event path that drives `TechEffectSystem`. Effects probably still apply; severity downgraded to medium dead-code. |
| FP-3 | `ResourceTickSystem.cs:62-64` claimed CRITICAL double-application of supplies | Verified — `TryAdd` is a no-op when the key exists, so this doesn't actually double-count. Bug intent is real (missing else), accumulation works by accident. Severity high (clarity), not critical. |
| FP-4 | `FlowFieldMovementHelper.GetDirection` "missing null-field guard" (round-1) | Line 57 already has it. |

---

## Recommendations

1. **Single sweep PR** for the 30 missing-brace bugs — they're mechanically identical, mostly small (3-4 lines each), and high cumulative impact.
2. **Add EditorConfig + Roslyn rule** `csharp_prefer_braces = true:warning` to the repo so the pattern can't recur. This is the single most preventive change.
3. **Verify the three `[unverified]` claims** before fixing: missing Ability case in lockstep, `Time.deltaTime` for lockstep tick accumulation, missing SetTechFlag hook. All three are critical-if-true but the agent's track record this audit is ~85% — about 15% false-positive rate, so don't take them on faith.
4. **Stand up a flow-field pre-warm helper** that all command issuers call uniformly, so PatrolCommand/HealCommand/ConvertCommand/BuildCommand/RepairCommand stop drifting from MoveCommand/AttackMoveCommand/GatherCommand.
5. **Tighten observer mode**: `ResourceHUD` and `EntityActionPanel` already gate on `IsObserver`. Audit `EntityInfoPanel`, `BuildCommandPanel`, control-group hotkeys, formation drag for the same gate.

## Out of Scope

- Refactor of the 3500-line `PresentationSpawnSystem.cs` (already covered by task-007)
- BattalionSyncSystem 795-line split (task-020)
- General performance optimization beyond the cited GUIStyle / FindFirstObjectByType allocations
- Multiplayer feature work — only auditing existing lockstep correctness
