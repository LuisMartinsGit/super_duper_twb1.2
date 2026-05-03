---
deft:
  id: task-deepdive-backlog-2026-062
  type: improvement
  status: active
  stage: implementation
  phase: 1
  total_phases: 4
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [code-quality, deep-dive, backlog]
---

# Deep-Dive Remaining Backlog (post-PR #245)

## Context

PR #245 (`fix(audit): deep-dive phase 1-4`) landed the highest-impact items from the May 2026 system-wide deep-dive (formerly tasks 050–061). This task tracks every verified finding that did **not** make it into that PR. Methodology and verification table are in PR #245's commit message and the closed PR description.

What shipped in PR #245:
- All 12 confirmed missing-brace bugs (BattalionSync ×2, UnitSeparation, TradingPost, CrystalExtinction, Veilstinger, MinimapRenderer, SelectionSystem, InGameMenuPanel, SpellPanel, EntityActionPanel, MultiplayerLobbyUI, SpellState).
- Sect tech bridge: `TechEffectSystem.OnTechCompleted` → `FactionSectState.SetTechFlag` + `RecalculateAllPassives`.
- `SectEffectSystem.ApplySectEffectsToUnit` wired into `TrainingSystem`, `BatchTrainingSystem`, and the battalion-leader path.
- `LockstepManager.ExecuteCommand` now handles `Ability`; `CommandRouter.IssueAbilityDirect` made public.
- Pillage / Caravan loot frame-multiplication (~120× per kill) closed via `WithNone<DeathAnimationState, BuildingCollapseState>` on the loot loops.
- `.editorconfig` with `csharp_prefer_braces = true:warning` so the brace pattern can't recur silently.
- `chore(gitignore)`: tracks Audio C# source; brings `MusicManager.cs` into the tree (carries the menu-music brace fix).

---

## Phase 1 — Critical gameplay bugs

### G-1 — `Rush` strategy cannot opt out of the 50/50 crystal floor (regression)
**Source:** task-052 F-1
**File:** `Assets/Scripts/AI/Core/AIStrategyEvaluator.cs` — earlier "they are still undervaluing crystal" fix added a hard 50/50 supplies/crystal floor that overrides `Rush.SetCrystalTarget(1)`. Rush is now indistinguishable from Balanced for crystal-priority purposes.
**Fix:** Apply the floor only when the strategy hasn't explicitly set its own crystal target (sentinel value or per-strategy flag).

### G-2 — `TryBuildBuilding` advances step on zero builders dispatched (orphan sites)
**Source:** task-052 F-2
**File:** `Assets/Scripts/AI/Managers/AIBuildingManager.cs` (TryBuildBuilding step-advance path)
The build-order step is marked done even when no builder was actually dispatched (dispatch returned 0 because all builders busy/dead). Resulting half-built orphan blocks the build queue and the AI sits idle.
**Fix:** Only advance the step when ≥1 builder was dispatched. Re-queue otherwise with a backoff counter so we don't spin.

### G-3 — `Litharch` factory never bakes `DesiredDestination` (AI Litharchs are paperweights)
**Source:** task-052 F-7
**File:** `Assets/Scripts/Entities/Units/Litharch.cs` (factory)
`AIMilitaryManager` issues movement via `ecb.SetComponent<DesiredDestination>`, but the component was never added at creation. Set silently fails on the ECB path or NREs depending on Entities version. AI-trained Litharchs sit at the spawn point.
**Fix:** Mirror `Miner.cs:54-58` — `em.AddComponentData(unit, new DesiredDestination { Has = 0 })` at unit creation. Same fix needed for **Scout** (task-053 F-6 — currently dead-code-with-trap because `AIScoutingBehavior` is `[DisableAutoCreation]`).

### G-4 — Walls bypass lockstep entirely (multiplayer-critical)
**Source:** task-061 B-1
**File:** `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` + `BuildingFactory.cs`
Walls are placed via a direct path that doesn't go through `LockstepManager` and `Alanthor_Wall` lacks `NetworkedEntity`. Wall placement desyncs in multiplayer; placement validity isn't checked the same way as other buildings.
**Fix:** Route through `CommandRouter.IssuePlaceBuildingDirect`/lockstep `Build` command; add `NetworkedEntity` to wall creation; verify `BuildingFactory.Create(EntityCommandBuffer, ...)` switch covers `"Alanthor_Wall"` (task-061 B-3 — also currently missing).

### G-5 — Wall enclosure income never deposits
**Source:** task-056 F-3
**File:** `Assets/Scripts/Systems/Economy/WallEnclosureIncomeSystem.cs`
The system computes income from enclosed area but the deposit path is missing — value is computed and discarded. Walls advertised as economy multiplier do nothing.
**Fix:** Add `FactionEconomy.Add(...)` call at the computed accrual interval. Verify against the multiplier from `mults.WallIncome` and (per task-057 F-3) eventually `mults.WallIncomeFromTech` once the dead field is wired or removed.

### G-6 — Vault interest sect multiplier applied twice
**Source:** task-056 F-4
**File:** `Assets/Scripts/Systems/Economy/VaultInterestSystem.cs` + `SectEffectSystem`
Vault interest reads `mults.VaultInterest` directly AND `SectEffectSystem.ApplyMultiplierDelta` also adjusts the stored interest rate when a vault-bonus sect is adopted, so adoption applies the multiplier on top of an already-multiplied base.
**Fix:** Pick one source of truth — recommend live-query each tick and remove the delta-tracking for `VaultInterest` from `ApplyMultiplierDelta`.

---

## Phase 2 — Combat correctness

### C-1 — `SpellBuff.ArmorBonus` and `SpellBuff.DamageMultiplier` are write-only (3 sect abilities silent no-ops)
**Source:** task-055 F-1
**Files:** `Assets/Scripts/Core/Components/SpellBuff` definition + every read site
The fields are written by Aura/Safeguard/Empower paths but no combat system reads them. Sect abilities advertised as armor/damage buffs do nothing in fights.
**Fix:** Make `MeleeCombatSystem` and `RangedCombatSystem` honor `SpellBuff.ArmorBonus` (added to defender's `Defense.Melee/.Ranged`) and `SpellBuff.DamageMultiplier` (multiplied into final damage).

### C-2 — Ranged on-hit (Reflect / Ignite / VoidStrike) applied at fire time, not at impact
**Source:** task-055 F-2
**File:** `Assets/Scripts/Systems/Combat/RangedCombatSystem.cs`
The on-hit pipeline runs when the projectile is fired, so dodged/missed shots still apply effects, and shots fired at one target that re-target mid-flight apply effects to the original.
**Fix:** Move on-hit handling to `ProjectileSystem` impact resolution, behind the same guard that confirms a hit landed.

### C-3 — Safeguard / Aura `SpellBuff` add wipes pre-existing buff
**Source:** task-055 F-3
**Files:** `Assets/Scripts/Systems/Spells/AuraSystem.cs`, `SafeguardSystem.cs`
Both use `ecb.AddComponent<SpellBuff>(unit, new SpellBuff { ... })` which overwrites if already present. Stacking Safeguard onto a unit already inside a friendly Aura discards the Aura's buff.
**Fix:** Read existing SpellBuff (if any), merge fields, then `SetComponent`/`AddComponent` (use HasComponent to choose).

### C-4 — Multiple AOE/DoT damage paths bypass `Invulnerable`
**Source:** task-055 F-4
**Files:** crystal-aura damage, ignite tick, void DOT, possibly LitharchHealing damage path
Direct `Health.Value -=` writes happen without checking `HasComponent<Invulnerable>`. Invulnerability mechanic is partially honored.
**Fix:** Funnel all health-mutating writes through `CombatDamageHelper.ApplyDamage` (or add the Invulnerable guard at each call site).

### C-5 — `BattalionLeader` can become zombie at HP=0 from AOE
**Source:** task-055 F-10
**File:** AOE damage paths + `BattalionSyncSystem`
AOE writes Health below 0 without going through the death pipeline that detaches members. Members keep following a leader entity that's about to be destroyed; on the same frame the destruction ECB plays back, members briefly target a non-existent leader.
**Fix:** AOE damage path should clamp at 0 and rely on `DeathSystem` to drive the destruction; alternately mark the leader as dead before destroying.

---

## Phase 3 — Multiplayer determinism (all flagged DEFERRED per user; pick up after rest of systems)

### M-1 — Lockstep tick accumulator uses non-deterministic frame time (architectural)
**Source:** task-054 F-2
Each peer accumulates wall-clock time, not turn count. Drift accumulates over long matches.
**Fix:** Drive ticks from a fixed-step counter, not `Time.deltaTime`.

### M-2 — `LockstepCommand.Serialize` drops `CommandIndex`
**Source:** task-054 F-3
Tie-break for same-tick commands is broken; sort order may differ across peers.
**Fix:** Include CommandIndex in the wire format.

### M-3 — UI fast-paths spend resources locally without lockstep
**Source:** task-054 F-4
Some UI buttons deduct cost optimistically before the lockstep command lands.
**Fix:** Reserve only at queue time on every peer (already correct in some paths; audit all).

### M-4 — `SimpleAISystem` bypasses lockstep AND mis-tags commands as `LocalPlayer`
**Source:** task-054 F-5
AI commands are issued directly. The mis-tag also makes it look like the local player issued them.
**Fix:** Route through lockstep with the AI's CommandSource tag.

### M-5 — `CrystalDeathDropSystem` and `TrainingSystem.SpawnUnit` allocate `NetworkId`s at frame rate
**Source:** task-054 F-6
NetworkId issuance must be deterministic across peers; per-frame allocation in non-lockstep systems can desync.
**Fix:** Allocate inside lockstep-driven systems only.

### M-6 — Checksum desync detected but silently ignored
**Source:** task-054 F-7
Logs the desync but doesn't halt or recover.
**Fix:** Soft-pause + reconnect dialog at minimum; ideally state resync.

### M-7 — UDP only, no retransmit
**Source:** task-054 F-8
Lost packets cause silent missing commands.
**Fix:** Add ack + retransmit on top of UDP, or accept the limitation explicitly.

### M-8 — Two parallel multiplayer lobby implementations; one is dead code
**Source:** task-054 F-10
**Files:** `MultiplayerLobbyUI.cs` + `SkirmishLobbyUI.cs`
**Fix:** Delete the dead path.

### M-9 — `BattalionStance` change bypasses lockstep
**Source:** task-054 F-11
Stance-toggle writes the component directly.
**Fix:** Wrap as a lockstep `Stance` command.

### M-10 — `NetworkedEntity.SpawnTick` hardcoded to 0 everywhere
**Source:** task-054 F-12
Field exists but never set; reproducibility tooling is half-built.
**Fix:** Stamp with current lockstep tick at entity creation.

---

## Phase 4 — Quality / cleanup / perf

### Q-1 — `ReplaceLostUnits` CPU sink for eliminated factions
**Source:** task-052 F-3
Loops trying to find a trainer that no longer exists.
**Fix:** Early-out when faction has no trainer of the required class.

### Q-2 — `GatherCommand` double-processed by MiningSystem AND GatheringSystem same frame
**Source:** task-052 F-4
Two systems both interpret the command. Right now they happen to produce the same result, but it's a latent bug.
**Fix:** One owner — recommend `GatheringSystem`.

### Q-3 — `_rngState` shared across all factions (subtle determinism quirk)
**Source:** task-052 F-9
RNG state in some AI subsystems is shared, so AI A's roll affects AI B's next roll.
**Fix:** Per-faction RNG state.

### Q-4 — `TryFindBuildPosition` allocations
**Source:** task-052 F-10
Allocates ~6 NativeArrays + a managed `bool[]` per call.
**Fix:** Pool the buffers or cache the query.

### Q-5 — `CommandQueueSystem` structural changes during iteration
**Source:** task-053 F-5
**File:** `Assets/Scripts/Systems/Commands/CommandQueueSystem.cs`
Iterator-invalidation pattern (mining/gathering systems already had this fixed). Shift-queue feature breaks once a queued command activates.
**Fix:** Collect into a `NativeList`, mutate after iteration.

### Q-6 — `MovementSystem` `BlendRadius=2f` lets units clip walls within 2m of goal
**Source:** task-053 F-7
**Fix:** Reduce `BlendRadius` near walls or skip blending when path crosses a static obstacle.

### Q-7 — `PassabilityGrid` cell types (Terrain/Building/Obstacle) never used
**Source:** task-053 F-8
Field is set but no system queries it.
**Fix:** Either delete the enum or wire it (e.g. for cost-aware pathing).

### Q-8 — `FlowFieldGenerator` 64KB+ Persistent allocation per BFS, never pooled
**Source:** task-053 F-9
**Fix:** Pool the work buffers across generator runs.

### Q-9 — `FlowFieldGenerator.Generate` (sync path) is dead code
**Source:** task-053 F-10
**Fix:** Delete sync path; jobified path is the only caller.

### Q-10 — `SpawnPlacementHelper.IsPositionClear` per-call EntityQuery alloc
**Source:** task-057 F-6 (T-3 confirmed)
Up to 17 calls per spawn; entire-world `ToComponentDataArray` each time.
**Fix:** Cache the query in a static or per-system handle.

### Q-11 — `Cadaver.CreateOrMerge` immediate destroy/recreate churns NetworkIds
**Source:** task-055 F-8
**Fix:** Update existing cadaver entity in place.

### Q-12 — `TargetingSystem.CleanupLastAttacker` removes/re-adds component every frame
**Source:** task-055 F-9
**Fix:** Conditional add — only remove when the attacker is genuinely stale.

### Q-13 — `UnitAbilitySystem.HealOverTime` overheals (int truncation + 1HP/frame floor)
**Source:** task-055 F-6
**Fix:** Track fractional health debt; only commit when it crosses 1 HP.

### Q-14 — `Reflect` damage uses pre-armor-pre-defense damage
**Source:** task-055 F-5
**Fix:** Reflect post-mitigation damage to match expected behavior.

### Q-15 — Dead `SectMultipliers` fields
**Source:** task-057 F-3
`MagicDamage`, `WallIncomeFromTech`, `RegenPerSecond`, `HasRenewal`, `RenewalIncomeBonus` — written by sect-tech application paths but never read.
**Fix:** Either wire to consumers or delete the fields.

### Q-16 — Three Runai economy techs are flavor-only
**Source:** task-057 F-4
`Runai_LongHaulTariffs`, `Runai_PackBazaar`, `Runai_EscortedCaravans` — researched but no system gates on them.
**Fix:** Wire `HasResearched(faction, "Runai_*")` into TraderMovementSystem / Runai trade income, OR drop from JSON.

### Q-17 — AI cultural unit mismatch (latent)
**Source:** task-057 F-5
`SimpleAISystem.FindTrainerForUnit` only handles generic IDs; cultural variants (`Runai_Spearman`, etc.) silently return `Entity.Null` if requested directly. No build order does this today.
**Fix:** Extend FindTrainerForUnit to resolve cultural IDs via UnitFactory's culture map.

### Q-18 — `TempleChapelBuildSystem` dual representation undocumented
**Source:** task-057 F-7
Chapels exist as both buffer slots AND independent ChapelTag entities. State can drift.
**Fix:** Document the dual representation OR collapse to one model (chapel-as-entity-only is simpler).

### Q-19 — Two `FactionEra` writers
**Source:** task-057 F-9
`AgeUpSystem` and `TempleUpgradeSystem` both write `FactionEra`. Not a bug today (only Era 1→2 implemented) but a latent footgun.
**Fix:** One writer when Era 3+ ships.

### Q-20 — Wasted resources on duplicate research/training queue
**Source:** task-057 F-10
Cost paid at queue time; if already-researched/already-trained, item is silently dropped without refund.
**Fix:** Refund on drop.

### Q-21 — Failed extinction respawn waits 3 minutes
**Source:** task-058 F-3
If respawn fails (no valid spawn), it waits a full crystal-extinction cycle.
**Fix:** Short retry (5–10s) on respawn failure.

### Q-22 — Unmined cadavers persist forever
**Source:** task-058 F-4
**File:** `CadaverDecaySystem` (or similar) — no decay timer for cadavers that no miner has touched.
**Fix:** Decay-timer countdown for cadavers older than N minutes.

### Q-23 — Construction tick erases combat damage to half-built structures
**Source:** task-058 F-5
**File:** `BuildingConstructionSystem.cs`
`Health.Value = Mathf.Lerp(0, MaxHealth, Progress)` overwrites combat damage.
**Fix:** Track construction-progress and combat-damage separately; render Health = constructionPct * MaxHealth - combatDamage.

### Q-24 — Dead `CrystalAIState.HarassTimer` / `UnitSpawnTimer`
**Source:** task-058 F-6
**Fix:** Delete unused fields.

### Q-25 — `AICrystalHuntBehavior` is dead code
**Source:** task-058 F-8
**Fix:** Delete the class (functionality moved into `AIMilitaryManager`).

### Q-26 — `WallGatePassabilitySystem` re-creates EntityQuery every poll
**Source:** task-058 F-9 / task-061 B-10
**Fix:** Cache query in `OnCreate`.

### Q-27 — Texture2D + Mesh leaks in `DayNightCycle.CreateCloudProjector`
**Source:** task-059 F-4
Both objects allocated each call to `CreateCloudProjector`; never released on Disable/Destroy.
**Fix:** Cache and release in OnDestroy.

### Q-28 — `Camera.main` per-frame in DayNightCycle
**Source:** task-059 F-5 (W-2 confirmed)
**Fix:** Cache reference in `Awake`.

### Q-29 — `GameObject.Find("PlacementPreview")` per LateUpdate during placement
**Source:** task-059 F-6
**Fix:** Cache the GameObject reference when placement starts.

### Q-30 — Minimap and `FogVisibilitySync` use different visibility predicates
**Source:** task-059 F-8
Minimap shows enemies that fog-of-war is hiding (or vice versa).
**Fix:** Single predicate consumed by both.

### Q-31 — Per-frame `GUIStyle` allocations in `EntityActionPanel` / `EntityInfoPanel`
**Source:** task-060 F-7, F-8
**Fix:** Cache styles statically; use `GUI.skin.GetStyle("name")` once.

### Q-32 — `EntityActionPanel` swallows `ArgumentException` silently
**Source:** task-060 F-9
**Fix:** Log the exception or handle the specific case that throws.

### Q-33 — `OptionsMenuUI` master volume doesn't apply in-session
**Source:** task-060 F-10
Slider writes to PlayerPrefs but doesn't update `AudioListener.volume` until next launch.
**Fix:** Set `AudioListener.volume` immediately on slider change.

### Q-34 — `InGameMenuPanel.DoQuitToMenu` cleanup is incomplete
**Source:** task-060 F-11
Doesn't tear down lockstep / network state when quitting to menu.
**Fix:** Call `LockstepManager.Shutdown()` (or equivalent) before scene load.

### Q-35 — `BuildCommandPanel.BuildType` enum missing 8+ building IDs
**Source:** task-061 B-5
Some chapels and culture-specific buildings have no enum entry, so the placement panel can't surface them.
**Fix:** Either remove the enum (use string IDs) or extend it.

### Q-36 — `ShrineOfAhridan` footprint mismatch
**Source:** task-061 B-6
Placement-panel footprint and built footprint differ; placement validation passes but the building overlaps neighbors.
**Fix:** Align `BuildingSizeConfig` value with the placement preview.

### Q-37 — Chapel-slot system is half-implemented dead code
**Source:** task-061 B-7
`TempleChapelSlot` buffer is read but never gates anything actively gameplay-affecting. Overlaps with Q-18.
**Fix:** Either wire it (slot-cap enforcement, build-progress UI) or delete it.

### Q-38 — `CommandRouter.GetBuildTime` switch holes; player vs AI inconsistency
**Source:** task-061 B-8
Some building IDs missing from `GetBuildTime`. Player path falls through to default (10s); AI path uses TechTreeDB. Same building has different build times depending on who builds it.
**Fix:** Single source of truth — read from TechTreeDB on both paths.

### Q-39 — `BuildingFactory.GetPresentationId` missing `Runai_TradingPost`
**Source:** task-061 B-9
Falls back to default mesh.
**Fix:** Add the case.

### Q-40 — Sect unique buildings have no special components
**Source:** task-061 B-11
Sect-specific buildings (Sanctum, Forge, etc.) created as plain `BuildingTag` entities without their gameplay components.
**Fix:** Add the matching component (e.g. `SanctumTag`, `ForgeTag`) at creation.

### Q-41 — `BuildCommandHelper.IsValidBuildPosition(float radius)` overload + `GetBuildingRadius` are dead
**Source:** task-061 B-12
**Fix:** Delete both.

### Q-42 — System queries not disposed in `BuildingConstructionSystem` / wall systems
**Source:** task-061 B-13
**Fix:** Use `state.GetEntityQuery` (cached, owned by SystemState) or call `Dispose` in OnDestroy.

### Q-43 — Indentation traps in three places (valid C# but reads as bug)
**Source:** task-061 B-14
**Fix:** Add explicit braces to make intent unambiguous.

### Q-44 — `AutoDecorateExisting` skips PID 355 in auto-decorate path
**Source:** task-061 B-15
**Fix:** Either include PID 355 or document why it's excluded.

### Q-45 — Hub `Radius` undersized vs visual
**Source:** task-061 B-16
Selection / interaction radius is smaller than the hub mesh.
**Fix:** Bump `Radius` to match visible footprint.

### Q-46 — `BuildingSizeConfig` Sect wildcard after explicit cases is dead
**Source:** task-061 B-17
The wildcard `Sect_*` branch is unreachable because explicit cases cover all sect buildings.
**Fix:** Delete the wildcard or move it before explicit cases.

### Q-47 — `TempleChapelBuildSystem` namespace inconsistency
**Source:** task-061 B-18
Lives in a different namespace from neighboring systems.
**Fix:** Move to `TheWaningBorder.Systems.Buildings`.

### Q-48 — `GrantTempleConstructionRP` granted on Shrine, not Temple
**Source:** task-061 B-19
**Fix:** Move grant call to temple-completion handler.

### Q-49 — Chapels with PIDs 400 and 401 render as Forest/Rock obstacles (DORMANT)
**Source:** task-061 B-2
These chapels currently never spawn from any code path, but the PID→mesh table is wrong.
**Fix:** Correct the mesh mapping or remove the dormant entries.

### Q-50 — `SkirmishLobbyUI` "Free For All" button is duplicate of "Circle"
**Source:** task-060 F-6
**Fix:** Distinguish the two layouts or drop one.

### Q-51 — `MinimapUI.OnPointerClick` discards button info
**Source:** task-059 F-9
Right-click and left-click handled identically.
**Fix:** Pass the `PointerEventData.button` through.

### Q-52 — `Right-click camera-snap` interrupts user's drag
**Source:** task-059 F-10
**Fix:** Suppress camera-snap while a drag is in progress.

### Q-53 — `MinimapUI.HandleClick` NRE in dead-code path
**Source:** task-059 F-3
**Fix:** Delete the dead method.

### Q-54 — `InfluenceManager.Awake` empty `else { }`
**Source:** task-059 F-7 (W-10 confirmed)
**Fix:** Cosmetic — drop the empty else.

### Q-55 — Per-frame allocations across many UI panels
**Source:** task-060 F-12
**Fix:** Sweep with the same fix as Q-31 (cache GUIStyles).

---

## Architectural smells (for future planning, not phase-1 work)

- **Two parallel AI architectures**: `SimpleAISystem` (build-order driven) + `AI*Manager` MonoBehaviours. Both can queue training into the same Barracks. Risk of double-queueing; coverage gaps differ between the two paths. Long-term: pick one.
- **Damage application split**: melee/siege uses immediate `em.SetComponentData<Health>`; AOE/DoT/spell paths use deferred ECB. `Invulnerable` enforcement is split too (Q-... C-4).
- **Two `FactionEra` writers** (Q-19), only safe today because Era 3+ unimplemented.
- **Sect/Tech effects use four separate application surfaces** with asymmetric coverage. F-1/F-2 in PR #245 fixed two of those; the dead `SectMultipliers` fields (Q-15) and Runai economy techs (Q-16) are the same root pattern.

---

## Phase plan

| Phase | Scope |
|---|---|
| 1 | G-1..G-6 (gameplay-critical / multiplayer-critical) |
| 2 | C-1..C-5 (combat correctness — visible to players) |
| 3 | M-1..M-10 (multiplayer determinism — picked up after rest of systems per user) |
| 4 | Q-1..Q-55 (perf, cleanup, dead code) — opportunistic, not a single PR |
