# Task Inventory — codebase sweep 2026-07-12

Compiled from a 6-agent parallel review (curse/veil, combat/abilities, AI/nav,
economy/work/buildings, UI, cross-cutting dead-code) plus current-session context.

**Legend** — Severity: `HIGH` / `MED` / `LOW`. Category: `BUG` (defect in
behaviour), `MISSING` (stub / unimplemented / deferred), `DEAD` (unused / legacy
/ disabled-behind-flag / superseded).

> Only the items in **§1 (live-path)** affect the game as it runs *today*.
> Everything under a `CurseFieldsArmies=false` / `UseBakedCrystalRenderer=true`
> flag, or in the Glow / legacy-nav / GPU-influence stacks, is inert until
> revived or deleted.

---

## 1. Live-path bugs — fix first

| Sev | Cat | Location | Issue |
|-----|-----|----------|-------|
| HIGH | BUG | RangedCombatSystem.cs:254 + ProjectileSystem.cs:337 | **`ApplyBonusDamageOnHit` is never called for ranged/projectile hits** → ranged attacks apply NONE of Condemned/Ignite/VoidStrike or the Wrath/Ruin/Antiquity/MarkedForSentence sect passives; Ignite/VoidStrike charges on ranged units never consumed. Effectively melee-only, silently. |
| HIGH | BUG | Presentation/VeilCrystalRenderer.cs:190 | `Graphics.RenderMeshInstanced` is handed a whole chunk's instance array, but the call is capped at **1023 instances**. Full/interior crust chunks (thousands of crystals) throw and render nothing. Latent — only bites once crust chunks fill. **Fix: batch into ≤1023 per call (startInstance/instanceCount loop).** |
| HIGH | BUG | Navigation/FlowFollowSystem.cs:220 | Goal-flow sampler checks `meta.Valid` but never compares `GoalFlowSlot.Generation` to `NavCostField.Generation`, so units follow **stale flow directions** after a wall/gate/crust cost change until re-integration catches up. |
| MED | BUG | Navigation/GoalFlowFieldSystem.cs:149 | `MaxIntegrationsPerTick=4` vs 48 cache slots: a cost-field generation bump invalidates many fields but only 4 re-integrate/tick → ~12 ticks of units on the blocker-ignoring direct-bearing fallback. |
| MED | BUG | Navigation/FlowFollowSystem.cs:250 | Source-3 "direct-to-goal while field pending" bearing ignores cost-grid blockers → a unit with a pending/evicted field walks straight into walls/crust. |
| MED | BUG | Combat/ProjectileSystem.cs:444 | AOE splash (`ApplyAOEDamage`) omits the ranged-vs-building 0.3 chip that direct hits apply at :345 → splash arrows still demolish buildings at full damage. *(follow-on to the recent ranged-vs-building fix.)* |
| MED | BUG | Combat/ProjectileSystem.cs:447 | AOE splash also omits `AbilityDamageHooks.ScaleIncoming` → Liquid Courage's 90% DR is bypassed by any splash hit. |
| MED | BUG | Combat/ShieldBarSystem.cs:80 | Shield absorb is post-hoc via `LastObservedHealth` with no ordering vs the combat/projectile systems → a lethal hit can reach `DeathSystem` before the shield refunds HP; shields can fail to prevent death. |
| MED | BUG | Combat/GodPowerSystem.cs:206 | God-power AOE applies damage with no `Invulnerable` guard and no `ScaleIncoming` — ignores LockdownVault invuln + Liquid Courage DR that every other damage path honours. |
| MED | BUG | Border/VeilFieldSystem.cs:273 (+ TryInitialise, ApplyCrustDebuffs) | `EntityManager.CreateEntityQuery` called every CA substep (several ×/s) and never disposed → unbounded query accumulation on the World for the whole match. Cache the queries. |
| MED | BUG | Border/ShardrootSystem.cs:40/63 | Same: two+ `CreateEntityQuery` per frame, never disposed. |
| MED | BUG | Work/MiningSystem.cs:40 (+ VeilstoneMiningSystem.cs:24) | `[BurstCompile]` struct calls managed `FactionEconomy.TryGetBank` (static Dictionary) → Burst compile fails and silently falls back to managed. Drop `[BurstCompile]` (as VeilMiningSystem already does) or de-manage the bank lookup. |
| MED | BUG | UI/Web/HudWebController.cs:288 | `LoadUrl` wrapped in an empty catch ("will retry never") → one throw leaves the web HUD permanently blank with no recovery. |
| LOW | BUG | AI/SimpleAISystem.cs:1556 | `EnsurePopulationHeadroom` calls `TryBuildBuilding("Hut")` every think-tick with no "Hut already under construction" guard → the AI can stack multiple simultaneous Hut foundations. |
| LOW | BUG | AI/SimpleAISystem.cs:2182 | A single `_rngState` is shared across all AI factions despite per-faction seeding claim → correlated placement angles / step-skip rolls across factions. |
| LOW | BUG | AI/IntelSystem.cs:177 | Classify falls through to `MilitaryUnit` for any non-miner/non-building enemy → lone enemy Scouts stamp the ThreatMap as military pressure, biasing routing/risk. |
| LOW | BUG | Navigation/SteeringSystem.cs:226 | `selfFactionIdx` cast with no 0..7 clamp / 0xFF sentinel (unlike Flow/GoalFlow) → factionless/out-of-range units get inconsistent gate-owner comparisons. |
| LOW | BUG | Combat/ProjectileSystem.cs:216 | `TerrainUtility.GetHeight` called with no `IsReady()` guard (laser path guards) → returns 0 before terrain loads, sinking the bolt. |
| LOW | BUG | Combat/EquipmentTierSystem.cs:108 (+ UnitRankSystem.cs:118) | Tier stat diffs use cumulative int truncation `(int)(value*diff)` on up/down changes → Damage/Defense erode over repeated re-applies. |
| LOW | BUG | Combat/MindControlSystem.cs:51 | On expiry FactionTag reverts but stale `Target`/`AttackCommand` (its own and others' targeting it) aren't cleared → briefly fights former allies. |
| LOW | BUG | Economy/VaultInterestSystem.cs:64 | `StoredAmount` compounds as an unbounded float, never clamped to `ResourceCap` → overflow risk on long games. |
| LOW | BUG | Economy/FeraldisLowHpRewardSystem.cs:105 | Killer culture resolved via Hall `FactionProgress.Culture` instead of the canonical `FactionColors.GetFactionCulture` → misses reward if a faction has no Hall. |
| LOW | BUG | Training/BatchTrainingSystem.cs:64 | Longhouse batch query omits `WithNone<AgeUpState,BuildingUpgrading>` (which `TrainingSystem` has) → keeps training while the building is upgrading/inert. |

---

## 2. Big themes / epics

These are the multi-file refactors most worth turning into deft tasks.

### 2.1 Finish "Curse as a Force" (design §2.5 / §2.6)
The load-bearing gap. See docs/Curse_And_Shardroot.md §2.5-2.6.
- `MISSING` **F1 — absolute-wall nav stamp**: crust never stamped impassable; only `BorderDebuff` applied. VeilFieldSystem.cs:449. No catch-death, no dig-to-path.
- `DEAD-but-live` **F2 — retire combat layer, part 2**: `BorderAISystem` (curse-*faction* brain — builds sub-nodes, **spawns Crystallings/Veilstingers/waves**) is **NOT** gated by `CurseFieldsArmies` and still runs (BorderAISystem.cs:34), even though `BorderArmyAISystem`/`BorderHordeSystem`/`RitualDefenseSystem` were disabled. Gate or delete it.
- `MISSING` **F3 — catch-death + telegraph** (units engulfed die; buildings block/crumble). Not started.
- `MISSING` **F4 — directional "dig toward X"**: AI has no breach-dig — a walled-off objective returns NoDirection and the army just holds. SimpleAISystem.cs:921. (Player corridors already work via mining.)
- `MISSING` Feraldis (blood-totem) + Runai (trader) influence-vs-curse effects (only Alanthor implemented this session).
- `DEAD` The retired ring-spread + DoT model: `BorderSpreadSystem` (gated) transitively kills `CursedGroundDamageSystem`/`CursedGroundRecessionSystem` (both `RequireForUpdate<BorderGroundTag>`, never satisfied). Orphaned components in BorderComponents.cs (BorderSpreadState/BorderNodeLevel/BorderGroundDPS/aura structs).

### 2.2 Purge the Glow economy (dead by design)
Glow is superseded by Shardroot + Veilsteel per canon, but the entire system is still compiled and, in parts, running:
- `DEAD` FactionResources.cs:38 (Glow field/cap/income), Systems/Economy/GlowFlowSystem.cs:30 (**active** SystemBase), Combat/GlowWeaponDropSystem.cs, Combat/GlowReviveSystem, Entities/Buildings/GlowWeapon.cs + GlowPickup.cs, Components/GodPowerComponents.cs (GodPowerState) + Economy/GodPowerSystem.cs (Glow-era, references GlowStored/Temple). Delete or migrate to the sect-glow redesign.

### 2.3 Sect "Phase 2" levers — pervasively unimplemented
The sect system's per-lever effects were stripped ("Phase 2 will reintroduce per-sect") and never re-added, so most sect choices do nothing:
- `MISSING` income (ResourceTickSystem.cs:83), build-speed (BuildingConstructionSystem.cs:152), vault-interest (VaultInterestSystem.cs:61), combat passives + ranged accuracy/vs-border (RangedCombatSystem.cs:187/236, MeleeCombatSystem.cs:204), panic/control on-hit (CombatDamageHelper.cs:348), typed lever params (SectDefinition.cs:120). Also chapel-upgrade host wiring (BuildingComponents.cs:235).

### 2.4 Spell / active-power layer is a stub
- `MISSING` SpellCastSystem.cs:148 (`ApplySpellEffect` always false), SpellDefinition.cs:62 (empty `SpellDatabase`), UI/HUD/ActiveAbilityBar.cs:80/112 (activation no-op, `RefreshAbilities` no-op — its `FactionSectState` source was deleted), PerClassTierAbilitySystem.cs:18 (Veilsteel/Glow tier actives "not in this slice").

### 2.5 UI consolidation to UI Toolkit
Every in-match HUD is built **three times** (IMGUI `UI/Panels`+`UI/HUD`, UITK `UI/Regions`, CEF web `UI/Web`), all mounted in GameBootstrap.cs:241-363; two are dead per match.
- `DEAD` 13 IMGUI HUD components force-disabled in the UITK config (GameplayUIController.cs:111-136); duplicate modals (culture/sect/pause/notifications).
- `MISSING` UITK has **no port** for tech-tree/research (TechTreePanel.cs:154), ability bar, vault, bazaar-wagon-unpack (ActionPanelRegion.cs:506), or post-game stats (GameBootstrap.cs:319) — disabling IMGUI removes those command paths.
- `MISSING` main-menu Campaign + Load Game "Coming soon" (MainMenuUI.cs:225). Objectives "Defeat players 0/3" hardcoded (ObjectivesRegion.cs:13, HudBridge.cs:1762 — no elimination tracker).

### 2.6 Delete the legacy navigation stack
The whole portal → A* → slab pipeline still runs every tick with **no consumer** (movement now sources direction from GoalFlowFieldCache):
- `DEAD` AbstractPathfinderSystem.cs:32, FlowSegmentSystem.cs:43 (+ NavFlowCache), NavRequestSchedulerSystem.cs:244, PortalGraphBuildSystem, IncrementalPortalRebuildSystem, LayerTransitionSystem. Emits `NavPathRequest`/`NavPathResult` nobody reads. Prime deletion candidate (CPU win).

### 2.7 Runai + Feraldis cultures — largely unimplemented
Cultures are hard-blocked "COMING SOON" (CulturePopupRegion.cs:100 / CultureChoicePopup.cs:100); age-up transforms are no-op stubs (AgeUpSystem.cs:180-190); their curse-influence effects unimplemented (§2.1); Conversion ritual only half-wired (RitualDefenseSystem.cs:11 — "only Purification implemented").

---

## 3. Remaining dead code (delete candidates)

| Location | Note |
|----------|------|
| Influence/InfluenceManager.cs + AlanthorInfluence/RunaiiInfluence/FeraldisInfluence/BloodMap/InfluenceBridge | Orphaned GPU influence stack — replaced by `PlayerInfluenceMap`; only the debug overlay toggles it. (AlanthorInfluence.cs:200 also has a latent additive-blit bug if revived.) |
| Border/BorderArmyAISystem.cs, BorderHordeSystem.cs, RitualDefenseSystem.cs | Disabled via `CurseFieldsArmies=false` — full army/horde/ritual-defence subsystem retained but inert. |
| Presentation/VeilSheetRenderer.cs, Systems/Creatures/BorderSpreadSystem.cs | Disabled via `UseBakedCrystalRenderer=true`. |
| Data/TechTree/CombatModifiers.cs:54/80 | The 5×6 damage matrix (`GetModifier`) and `GlobalDamageMultiplier` have **zero callers** (matrix "kept for UI" but nothing reads it). Several system headers still claim it's live (MeleeCombatSystem.cs:15). |
| Entities/Units/Miner.cs | Legacy `Miner.Create` — zero callers (UnitFactory aliases Miner→Builder). |
| Components/NodeStateComponents.cs:82/88 | `NodeInvulnerabilityState` self-documented as unused; archetype slot kept for compat. |
| Components/AI/AIScoutingComponents.cs, AIManagerComponents.cs | `ExplorationZone`/`CombatPower`/`AIScoutingState`/`ScoutAssignment`/`EnemySighting`, `BuildRequest`/`RecruitmentRequest` allocated per brain but no live reader (consumers were the `[DisableAutoCreation]` AI managers). |
| Systems/AI/AIAlanthorEndgameSystem.cs:12 + AIMilitaryManager/AIStrategyEvaluator/AIBuildingManager/AIEconomyManager | `[DisableAutoCreation]` — superseded by SimpleAISystem. |
| Systems/Buildings/WallAutoSegmentSystem.cs:37, Entities/Units/Scout.cs:65 (ScoutTrap) | `[DisableAutoCreation]` dormant features. |
| Components/Buildings/.../CrucibleComponents.cs:24, Entities/Buildings/Smelter.cs:15 | Iron/Veilstone storage fields legacy/always-0 since the conversion economy was removed. |
| BorderConstants.cs:78/118/134/163/190 | Crystalling/Veilstinger/Godsplinter stats, AI costs, sub-node limits, legacy regrow time, Iconoclast block — all only consumed by gated army systems; Iconoclast references the deleted `NodeInvulnerabilitySystem`. |
| Economy/TradingPostSystem.cs:192, ResourceTickSystem.cs:99, Buildings/BuildingUpgradeSystem.cs:62 | Unused `hubIncrements`/`deltaSeconds`; leftover per-tick debug `TWBLog.Log`. |
| UI: Phase0DemoMount.cs, SpellPanel.cs, ReligionHUD.cs (Coming-soon), EntityExtractors.Research.cs:268 (stub) | Dormant / superseded UI. |

---

## 4. Placeholders (design values, not bugs)
Crossbowman/Longbowman/KingLexor/Ledger stats + art (PLAYTEST PLACEHOLDER); KeepWingConfig; Reliquary/hero costs (TechCatalog.cs:227); ability armor scaling (AbilityEffectExecutor.cs:48, AbilityAuraSystem.cs:88). Tune during balance passes.

---

## 5. Verified NOT broken (during the sweep)
- Veilstone economy is intentionally dual-path (Veil-sheet mining on cursed maps, outcroppings otherwise) — neither system is dead.
- Refund paths (self-destruct 80% / cancel-train / repair) route through the shared `BuildCosts.IdFromEntity` and are consistent; training charged once at queue.
- `PillageSystem`/`CaravanDeathSystem` double-pay-on-death was already fixed (gated with `WithNone<DeathAnimationState,BuildingCollapseState>`).
- The off-thread web-HUD load bug is fixed; HUD click-blocking is wired across all three UI stacks (no fall-through).
- The nav terrain-cost bake (`TerrainCostBakeSystem`) is now implemented (earlier stub resolved).
- Fixed this session: worker auto-aggro (economy units excluded from `TargetingSystem`); curse attack waves + ritual-defence gated off; Age 0 veilstone costs zeroed; archer range/LOS + ranged-vs-building damage.

## 6. Fixed in the 2026-07-12 remediation pass
- ✅ VeilCrystalRenderer 1023-instance cap → batched (`§1`).
- ✅ BorderAISystem gated behind `CurseFieldsArmies` (`§2.1 F2`) — curse units no longer spawn.
- ✅ Ranged/projectile hits now call `ApplyBonusDamageOnHit` (Condemned/Ignite/VoidStrike + sect passives) (`§1`).
- ✅ AOE splash now applies the ranged-vs-building chip + `ScaleIncoming` (`§1`).
- ✅ VeilFieldSystem per-substep query leak → cached `_wellQuery` (`§1`).
