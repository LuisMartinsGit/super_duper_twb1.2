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

The May 2026 system-wide deep-dive (formerly tasks 050–061) was rolled into three PRs:
- **PR #245** — phase 1–4 from the original audit: brace sweep, sect tech bridge, ApplySectEffectsToUnit wiring, lockstep Ability case, Pillage/Caravan loot frame-multiplication.
- **PR #246** — gameplay-critical (G-1..G-3, G-6) + combat correctness (C-1..C-4). Two findings refuted on re-read (G-5 wall enclosure income, C-5 BattalionLeader zombie) and documented as verified-fine.
- **(this task)** tracks the remaining work: Phase 3 multiplayer determinism (M-1..M-10, plus the originally-tagged G-4 walls), Phase 4 quality/perf (Q-1..Q-55), and the architectural smells.

Methodology and the verification table for refuted surface-scan claims are in PR #245's commit message and closed PR description.

PR #245 + PR #246 together shipped:
- All 12 confirmed missing-brace bugs.
- Sect tech bridge + ApplySectEffectsToUnit wiring + DeathAnimationState/BuildingCollapseState exclusion on loot.
- `LockstepManager` `Ability` case + `IssueAbilityDirect` public.
- `.editorconfig` `csharp_prefer_braces = true:warning` so missing braces can't recur silently.
- `chore(gitignore)`: tracks Audio C# source.
- G-1 Rush crystal floor, G-2 orphan UnderConstruction sites, G-3 Litharch/Scout/Builder DesiredDestination, G-6 vault-interest double-multiply.
- C-1 SpellBuff.ArmorBonus + DamageMultiplier read sites, C-2 Reflect at impact, C-3 SpellBuff merge, C-4 AOE/DoT honor Invulnerable.

---

## Phase 1 + 2 — DONE (PR #246)

G-1..G-3 + G-6 (gameplay-critical) and C-1..C-4 (combat correctness) all landed in PR #246. G-5 (wall enclosure income) and C-5 (BattalionLeader zombie) were refuted on re-read — see PR #246 commit message for the verification notes. G-4 (walls bypass lockstep) is multiplayer determinism work and lives with the M-* items below.

---

## Phase 3 — Multiplayer determinism (all flagged DEFERRED per user; pick up after rest of systems)

### M-0 — Walls bypass lockstep entirely (was G-4, multiplayer-critical)
**Source:** task-061 B-1
**File:** `Assets/Scripts/Entities/Buildings/AlanthorWall.cs` + `BuildingFactory.cs` + `UI/Panels/BuildCommandPannel.cs:588`
Walls are placed via a direct path that doesn't go through `LockstepManager`. `BuildCommandPannel.SpawnWallHub` calls `AlanthorWall.CreateHub` directly instead of `CommandRouter.PlaceBuildingDirect`, segments are likewise not lockstep-driven. `Alanthor_Wall` is also missing from `BuildingFactory.Create(EntityCommandBuffer, ...)` switch (latent — no caller hits the ECB path today).
**Fix:** Route hub placement through `CommandRouter.PlaceBuildingDirect("Alanthor_Wall", ...)`; introduce a lockstep `BuildWallSegment` command for segment+instances spawn so all peers create identical entities; add `NetworkedEntity` to wall pieces (BuildingFactory will already do this once the EM path is used); patch the ECB switch.

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

### Q-2 — DONE in PR #248
~~`MiningSystem` now declares `[UpdateAfter(typeof(GatheringSystem))]` so the player-command path runs first; ProcessIdleState only fires after GatheringSystem has consumed the command.~~

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

### Q-9 — DONE in PR #248
~~Synchronous `FlowFieldGenerator.Generate` removed — async ScheduleAsync/CompleteAsync is the only caller.~~

### Q-10 — `SpawnPlacementHelper.IsPositionClear` per-call EntityQuery alloc
**Source:** task-057 F-6 (T-3 confirmed)
Up to 17 calls per spawn; entire-world `ToComponentDataArray` each time.
**Fix:** Cache the query in a static or per-system handle.

### Q-11 — `Cadaver.CreateOrMerge` immediate destroy/recreate churns NetworkIds
**Source:** task-055 F-8
**Fix:** Update existing cadaver entity in place.

### Q-12 — DONE in PR #248
~~`CleanupLastAttacker` only removes when the attacker entity no longer exists; combat systems still overwrite the value when a new hit lands.~~

### Q-13 — DONE in PR #248
~~`HealOverTime` accumulates fractional HP and only commits whole HP when the accumulator crosses 1 (no more 1-HP/frame floor overheal). Final flush at duration end.~~

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

### Q-22 — DONE in PR #248
~~`CadaverDecaySystem` ticks `CadaverState.DecayTimer`; CrystalMiningSystem resets the timer per gather. Default lifetime 240s for unmined cadavers.~~

### Q-23 — DONE in PR #248
~~Construction tick now applies HP as a delta from the previous tick (`UnderConstruction.LastProgressHp`) so combat damage taken between ticks survives.~~

### Q-24 — DONE in PR #248
~~`HarassTimer` and `UnitSpawnTimer` removed from `CrystalAIState`; the unused `MainNodeHarassTimer` constant also dropped.~~

### Q-25 — DONE in PR #247
~~`AICrystalHuntBehavior` deleted (was `[DisableAutoCreation]` / superseded by `SimpleAISystem`).~~

### Q-26 — DONE in PR #247
~~`WallGatePassabilitySystem` caches query via `state.GetEntityQuery` in `OnCreate`.~~

### Q-27 — DONE in PR #248
~~`DayNightCycle` caches `_cloudMesh`, `_cloudTexture`, `_cloudMaterial` fields and Destroys them in OnDestroy.~~

### Q-28 — DONE in PR #247
~~`Camera.main` cached in `Awake` into `_mainCamera` (re-resolves on null).~~

### Q-29 — DONE in PR #247
~~`GameObject.Find("PlacementPreview")` cached in `_cachedPreview`, cleared on placement-end.~~

### Q-30 — Minimap and `FogVisibilitySync` use different visibility predicates
**Source:** task-059 F-8
Minimap shows enemies that fog-of-war is hiding (or vice versa).
**Fix:** Single predicate consumed by both.

### Q-31 — Per-frame `GUIStyle` allocations in `EntityActionPanel` / `EntityInfoPanel`
**Source:** task-060 F-7, F-8
**Fix:** Cache styles statically; use `GUI.skin.GetStyle("name")` once.

### Q-32 — DONE in PR #248
~~`EntityActionPanel` ArgumentException catch now logs the message in debug builds (with the offending action type) instead of bare-swallow.~~

### Q-33 — DONE in PR #247
~~`OptionsMenuUI` slider applies `AudioListener.volume` on each value change; persistence still happens at Apply.~~

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

### Q-39 — DONE in PR #247
~~`BuildingFactory.GetPresentationId` returns 355 for `Runai_TradingPost` (shares mesh with Alanthor_Garrison).~~

### Q-40 — Sect unique buildings have no special components
**Source:** task-061 B-11
Sect-specific buildings (Sanctum, Forge, etc.) created as plain `BuildingTag` entities without their gameplay components.
**Fix:** Add the matching component (e.g. `SanctumTag`, `ForgeTag`) at creation.

### Q-41 — DONE in PR #248
~~`IsValidBuildPosition(float radius)` overload and `GetBuildingRadius` removed — every caller goes through the int2-size AABB overload.~~

### Q-42 — DONE in PR #248
~~`BuildingConstructionSystem` and `BuildCommandSystem` now use `state.GetEntityQuery` so SystemState owns the lifetime (auto-dispose). PR #247 already covered `WallGatePassabilitySystem` (Q-26).~~

### Q-43 — Indentation traps in three places (valid C# but reads as bug)
**Source:** task-061 B-14
**Fix:** Add explicit braces to make intent unambiguous.

### Q-44 — DONE in PR #248
~~`Create355` now applies `AutoDecorateExisting` with the matching culture tint (Alanthor for Garrison, Runai for TradingPost) so PID 355 buildings get the same plinth + culture lighting as their PID-350-353/365-366 siblings.~~

### Q-45 — VERIFIED FINE (audit refuted)
The audit said hub `Radius` was undersized vs the visual mesh. Re-checking: the AlanthorWall hub plinth uses Unity-primitive scale 1.55 (so half-extent ≈0.775), and the existing collision Radius is 0.8 — they match. The audit confused mesh-scale with mesh-extent. No change.

### Q-46 — DONE in PR #247
~~Unreachable `Sect_*` wildcard branch removed from `BuildingSizeConfig`.~~

### Q-47 — DONE in PR #248
~~Moved from `TheWaningBorder.Systems.Building` to `TheWaningBorder.Systems.Buildings` (matches neighbouring systems).~~

### Q-48 — DONE in PR #248
~~`GrantTempleConstructionRP` now fires on `TempleTag` completion (was on `ShrineTag`, despite the helper name).~~

### Q-49 — Chapels with PIDs 400 and 401 render as Forest/Rock obstacles (DORMANT)
**Source:** task-061 B-2
These chapels currently never spawn from any code path, but the PID→mesh table is wrong.
**Fix:** Correct the mesh mapping or remove the dormant entries.

### Q-50 — `SkirmishLobbyUI` "Free For All" button is duplicate of "Circle"
**Source:** task-060 F-6
**Fix:** Distinguish the two layouts or drop one.

### Q-51 — DONE in PR #248
~~Fixed in `MinimapClickProxy.OnPointerClick` (`MinimapRenderer.cs`) — right-click → `HandleRightClick`, else `HandleLeftClick`. The earlier "fix" comment claimed PR #245 had landed it but a subsequent merge re-introduced the missing braces; properly closed now with explicit if/else.~~

### Q-52 — `Right-click camera-snap` interrupts user's drag
**Source:** task-059 F-10
**Fix:** Suppress camera-snap while a drag is in progress.

### Q-53 — DONE in PR #248 (audit was wrong about cause)
~~`MinimapUI.HandleClick` was NOT dead code — it's the live click handler called via `MinimapClickProxy.OnPointerClick`. The NRE was a missing-brace bug: when `cameraRig` was the active controller, `_cameraController.MoveToPositionSmooth(...)` ran unconditionally on a null `_cameraController`. Patched with explicit if/else.~~

### Q-54 — DONE in PR #248
~~Empty `else { }` blocks removed from `InfluenceManager.Awake`.~~

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
