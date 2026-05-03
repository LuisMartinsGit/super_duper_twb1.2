---
deft:
  id: task-deepdive-combat-2026-055
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
  labels: [code-quality, code-review, deep-dive, combat]
---

# Combat / Damage / Projectiles / Death / Spells / Healing deep-dive

## Context

Companion to task-052/053/054. Same method: deft-review agent with explicit deep-read instructions; ~30 files read in full; traced 6 end-to-end paths (melee, ranged, damage-type matrix, spell/buff lifecycle, healing, death-drop / cadaver).

## TL;DR

Combat is **structurally cleaner** than AI/Movement/Multiplayer were at audit time — earlier task fixes (Invulnerable checks, Health guards, singleton ECB, CombatDamageHelper extraction, immediate-write Health for parallel-attacker stacking) all hold up. The damage-type × armor-type matrix is consistent across MeleeCombat / RangedCombat / ProjectileSystem / BuildingCombat / GodsplinterCombat (all use `CombatModifiers.CalculateFinalDamage`).

But there are **10 substantive behavioral bugs the surface scan missed entirely** while it focused on cosmetic missing-brace patterns:

- **F1 (CRITICAL)**: `SpellBuff.ArmorBonus` and `SpellBuff.DamageMultiplier` are write-only. Three sect abilities (Safeguard, StoneheartBastion, MirrorShield) advertise armor/damage in their tooltips but no damage system reads those fields. Silent no-ops.
- **F2 (HIGH)**: RangedCombatSystem applies DamageReflect / Ignite / VoidStrike at fire time, not at impact. Archers take reflect damage from arrows that haven't landed (or never will). Ignite/VoidStrike charges burn even on missed shots.
- **F3 (HIGH)**: `UnitAbilitySystem.ApplyAoeBuff` (Safeguard, WarCry) overwrites pre-existing `SpellBuff` via `ecb.AddComponent`, wiping MirrorShield's DamageReflect when Safeguard arrives in the same tick. SectBuildingAuraSystem got this right; ability system didn't.
- **F4 (HIGH)**: Multiple AOE/DoT damage paths (`ProjectileSystem.ApplyAOEDamage`, `BurningGroundSystem`, `CursedGroundDamageSystem`, `UnitAbilitySystem.ApplyAoeDamage`, `SectBuildingAuraSystem.ApplyBindingPillarDamage`, `GodsplinterCombatSystem` siege) bypass the `Invulnerable` guard that single-target attacks honor. Fix #211 was applied piecewise — LockdownVault's Invulnerable spell silently fails against half the damage sources in the game.

**The 11 surface-scan missing-brace claims in CombatDamageHelper / ProjectileSystem / TargetingSystem are ALL FALSE POSITIVES** — every single one is `if X; else Y;` where `else` correctly binds to the most recent unmatched `if`. Cosmetically ugly, behaviorally correct.

The **one** real missing-brace bug in this domain that surface-scan missed entirely is `SpellState.cs:75-77` (F7).

## Requirements

- R1: Triage every verified finding (F1..F10).
- R2: Fix Critical/High before next playtest (F1, F2, F3, F4).
- R3: Address Medium findings (F5, F6, F8, F9).
- R4: Drop the 11 false positives from task-051's MB list.

## Acceptance Criteria

- [ ] F1: SpellBuff.ArmorBonus is consumed by MeleeCombatSystem / RangedCombatSystem / ProjectileSystem.ApplyDamage's defenseValue calculation
- [ ] F2: Ranged Reflect / Ignite / VoidStrike applied at projectile impact, not fire time
- [ ] F3: ApplyAoeBuff and ApplyAoeSlow merge in-place with existing SpellBuff/SpellDebuff
- [ ] F4: AOE/DoT call sites either route through `CombatDamageHelper.TryApplyDamage(em, target, amount)` (new helper) or each gets `if (em.HasComponent<Invulnerable>(target)) continue;`
- [ ] F5, F6, F7, F8, F9, F10 either resolved or filed as low-priority follow-ups
- [ ] Drop 11 false-positive entries from task-051's MB list

---

## Verified Findings

### F1 — `SpellBuff.ArmorBonus` and `SpellBuff.DamageMultiplier` are write-only (CRITICAL)
**Files:** [SectBuildingAuraSystem.cs:151-181, 192-235](Assets/Scripts/Systems/Combat/SectBuildingAuraSystem.cs#L151), [UnitAbilitySystem.cs:124-137, 297-338](Assets/Scripts/Systems/Combat/UnitAbilitySystem.cs#L124)
**Severity:** Critical — silently breaks 3 distinct sect abilities

`SpellBuff` has 4 effect fields. Greps across the entire codebase show:

- `ArmorBonus` — written by `StoneheartBastion` aura (line 153, 166), `MirrorShield` ability (line 131), `Safeguard` ability (line 326). **Not read by any damage system.** `MeleeCombatSystem.cs:144-153` adds `Fortified.ArmorBonus` to defenseValue but never adds `SpellBuff.ArmorBonus`. `RangedCombatSystem.cs:285-289` same. `ProjectileSystem.ApplyDamage:295-319` same.
- `DamageMultiplier` — written by `MirrorShield`, `Safeguard` (both as 0f, harmless). Not read anywhere.
- `SpeedMultiplier` — written by all 3 above (as 0f). Read once: `MovementSystem.cs:298-299`. Currently no-op because all writers set 0f.
- `DamageReflect` — both written and read. Live.

MirrorShield, StoneheartBastion, and Safeguard are advertised as damage-reduction abilities — players see no effect.

**Fix:** In `MeleeCombatSystem.cs:144-153`, `RangedCombatSystem.cs:285-289`, `ProjectileSystem.ApplyDamage`, after the `Fortified` check add:
```csharp
if (em.HasComponent<SpellBuff>(tgt.Value))
    defenseValue += (int)em.GetComponentData<SpellBuff>(tgt.Value).ArmorBonus;
```

### F2 — RangedCombatSystem applies Reflect / Ignite / VoidStrike at fire time, not at impact (HIGH)
**File:** [RangedCombatSystem.cs:292-295](Assets/Scripts/Systems/Combat/RangedCombatSystem.cs#L292-L295)
**Severity:** High — asymmetric handling vs. melee; wrong damage scale

Fire-time block:
```csharp
finalDamage = CombatDamageHelper.ApplyBonusDamageOnHit(em, ecb, entity, tgt.Value, finalDamage);
CombatDamageHelper.ApplyDamageReflect(em, entity, tgt.Value, finalDamage);
finalDamage = math.max(1, finalDamage);
```

Inside the `if (archer.AimTimer >= effectiveAimRequired && archer.CooldownTimer <= 0)` block. Arrow created at line 318. Damage applied at impact by `ProjectileSystem.ApplyDamage` — but Reflect already fired and Ignite/VoidStrike charges already consumed.

Concrete consequences:
- Archer fires at MirrorShield target → reflect damage applies immediately. Arrow misses (target dies first, terrain block) → shooter took reflect for a phantom hit.
- Ashblade with `IgniteBuff(AttacksRemaining=3)` fires 3 arrows in quick succession → all 3 charges consumed when fired, not when they hit. If 2 miss, Ignite is wasted.

In melee this isn't an issue because melee damage is applied immediately in the same call.

**Fix:** Store `finalDamage` and on-hit-pipeline state in the projectile and apply Reflect/charge consumption inside `ProjectileSystem.ApplyDamage` after the impact actually lands.

### F3 — Safeguard / Aura SpellBuff additions wipe pre-existing SpellBuff via `ecb.AddComponent` (HIGH)
**File:** [UnitAbilitySystem.cs:312-333](Assets/Scripts/Systems/Combat/UnitAbilitySystem.cs#L312-L333)
**Severity:** High — silently cancels other sect abilities

`Safeguard` and `WarCry` (and `ApplyAoeDamage`'s sibling pattern) iterate friendlies/enemies and unconditionally call `ecb.AddComponent(entity, new SpellBuff{...})` / `new SpellDebuff{...}`. **No `if (em.HasComponent<SpellBuff>(target))` check**. ECB `AddComponent` on an entity that already has the component **overwrites the existing data**.

Trace:
1. Mage casts MirrorShield on self → `ecb.AddComponent(entity, new SpellBuff{ DamageReflect=0.30, TimeRemaining=6 })`.
2. Friendly VaultKeeper casts Safeguard nearby (same tick) → `ApplyAoeBuff` runs, sees mage in range, calls `ecb.AddComponent(mage, new SpellBuff{ ArmorBonus=3, DamageReflect=0, TimeRemaining=5 })`.
3. ECB plays back. AddComponent on already-present SpellBuff replaces it. Mage now has DamageReflect=0 — MirrorShield is wiped.

`SectBuildingAuraSystem` (lines 160-181, 215-234) gets this right with `HasComponent` check + merge in-place. Ability system doesn't.

**Fix:** Mirror the SectBuildingAuraSystem pattern in `ApplyAoeBuff` and `ApplyAoeSlow` — check existence, merge fields, refresh timer.

### F4 — Multiple AOE/DoT damage paths bypass the `Invulnerable` check (HIGH)
**Files:**
- [ProjectileSystem.cs:347-403](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L347-L403) (`ApplyAOEDamage`)
- [BurningGroundSystem.cs:99-105](Assets/Scripts/Systems/Combat/BurningGroundSystem.cs#L99-L105)
- [CursedGroundDamageSystem.cs:70-77](Assets/Scripts/Systems/Creatures/CursedGroundDamageSystem.cs#L70-L77)
- [UnitAbilitySystem.cs:280-286](Assets/Scripts/Systems/Combat/UnitAbilitySystem.cs#L280-L286) (ArcanePulse)
- [SectBuildingAuraSystem.cs:319-324](Assets/Scripts/Systems/Combat/SectBuildingAuraSystem.cs#L319-L324) (BindingPillar)
- [GodsplinterCombatSystem.cs:143-148](Assets/Scripts/Systems/Crystal/GodsplinterCombatSystem.cs#L143-L148) (siege mode)

**Severity:** High — Fix #211 was piecewise; Invulnerable spell silently fails against AOE/DoT

Fix #211 was applied to `MeleeCombatSystem.cs:101`, `RangedCombatSystem.cs:107`, `ProjectileSystem.ApplyDamage:293` — but every AOE/DoT site does damage subtract directly without checking `Invulnerable`.

So `Invulnerable` (the LockdownVault spell, per SpellComponents.cs:48-54) protects against arrows and melee, but a unit standing on burning ground, in cursed terrain, in an ArcanePulse blast, in a BindingPillar's radius, or being hit by a Godsplinter siege blast still takes full damage.

**Fix:** Either add `if (em.HasComponent<Invulnerable>(target)) continue;` at each call site, or factor into a `CombatDamageHelper.TryApplyDamage(em, target, amount)` helper.

### F5 — Ranged DamageReflect uses pre-armor-pre-defense damage (MEDIUM)
**Files:** [RangedCombatSystem.cs:294](Assets/Scripts/Systems/Combat/RangedCombatSystem.cs#L294), [ProjectileSystem.cs:316-322](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L316-L322)
**Severity:** Medium — damage scale issue, melee/ranged asymmetry

`finalDamage` passed to `ApplyDamageReflect` is the archer's outgoing damage *before* armor / defense reduction. Actual `impactDamage` applied to target goes through the armor matrix and defense reduction.

Example: Ranged shot dealing 50 base damage at InfantryHeavy target (Ranged vs Heavy = 0.9x) with 50 defense:
- `finalDamage` at fire time = 50
- `impactDamage` actually dealt = ~30
- Reflect computed off 50, not 30 — over-reflects by ~67%

In melee `finalDamage` IS the post-armor value because melee runs the matrix in-place before calling `ApplyDamageReflect`. Ranged does not.

**Fix:** Combined with F2, move reflect to projectile impact in `ProjectileSystem.ApplyDamage` after `impactDamage` is computed.

### F6 — `UnitAbilitySystem.HealOverTime` overheals due to int-truncation + 1HP/frame floor (MEDIUM)
**File:** [UnitAbilitySystem.cs:200-219](Assets/Scripts/Systems/Combat/UnitAbilitySystem.cs#L200-L219)
**Severity:** Medium — sect ability balance off by 3-4×

```csharp
float healRate = hot.ValueRO.TotalHealing / hot.ValueRO.Duration;  // 50/3 = 16.67 hp/s
int healAmount = (int)(healRate * dt);                              // at 60fps: 0.28 → cast to int = 0
if (healAmount < 1 && hot.ValueRO.Elapsed > prevElapsed) healAmount = 1;  // floor to 1
```

For RapidMend (TotalHealing=50, Duration=3) at 60fps: per-frame heal rounds to 0, floor kicks in to 1 HP/frame, 180 frames × 1 HP = **180 HP healed** (clamped to Max). Even at 30fps: 90 HP. The `TotalHealing = 50f` field is effectively ignored.

**Fix:** Accumulate fractional healing in a float field on `HealOverTime`, only apply integer ticks when the accumulator crosses 1.0. Or compute total-applied vs `TotalHealing * (Elapsed/Duration)` and apply the delta.

### F7 — `SpellState.cs:75-77` — Real missing-brace bug surface scan missed (LOW)
**File:** [SpellState.cs:73-78](Assets/Scripts/Economy/SpellState.cs#L73-L78)
**Severity:** Low — dead-store on every expiring cooldown; behavior happens to be correct

```csharp
foreach (var spellKvp in cooldowns)
{
    float remaining = spellKvp.Value - dt;
    if (remaining <= 0f)
        keysToRemove.Add(spellKvp.Key);
        cooldowns[spellKvp.Key] = remaining;   // ← runs unconditionally
}
```

Intent was "either add to remove list OR update value". As written, does both for expiring entries. Net behavior is correct (keys end up removed at end of frame anyway), but it's the SAME missing-brace pattern as the 25+ instances in task-051. Add to the sweep.

**Fix:** Add braces, swap dead-store into an `else`.

### F8 — `Cadaver.CreateOrMerge` does immediate destroy/recreate, churns NetworkIds (MEDIUM)
**Files:** [Cadaver.cs:51-80](Assets/Scripts/Entities/Cadaver.cs#L51-L80), [CrystalDeathDropSystem.cs:57-60](Assets/Scripts/Systems/Crystal/CrystalDeathDropSystem.cs#L57-L60)
**Severity:** Medium — perf + MP determinism

For each death-drop within 1m of an existing cadaver, `CreateOrMerge`:
1. Calls `em.CreateEntityQuery` (allocates fresh query handle per call — task-008 anti-pattern).
2. Calls `em.DestroyEntity(existing)` (immediate structural change).
3. Calls `em.CreateEntity` which calls `NetworkIdGenerator.GetNextId()`.

Consequences:
- Every merge invalidates previous cadaver's NetworkId and allocates a new one → in MP, peers may merge in different orders
- Per-drop EntityQuery allocation = 8 deaths in a fight = 8 fresh queries
- Any UI / command pointing at the old cadaver entity (a miner mid-route to it) has its target invalidated

**Fix:** Cache the cadaver `EntityQuery`. For merging, instead of destroy+create, `em.SetComponentData(existing, new CadaverState{ RemainingCrystal = sum, ... })` and `em.SetComponentData(existing, LocalTransform.FromPositionRotationScale(pos, rot, newScale))`. Preserves NetworkId.

### F9 — `TargetingSystem.CleanupLastAttacker` removes/re-adds component every frame (MEDIUM perf)
**File:** [TargetingSystem.cs:752-762](Assets/Scripts/Systems/Combat/TargetingSystem.cs#L752-L762)
**Severity:** Medium — performance only; behavior correct

200 units in active combat = 200 RemoveComponent + 200 AddComponent ECB ops per frame just to maintain a 1-frame lifetime tag. Each component change triggers an archetype change, one of the most expensive operations in DOTS.

**Fix:** Use a `LastAttackerFrame { uint Frame; Entity Value; }` component that stays on the entity and carries the simulation tick. Read sites check `if (currentFrame - lastFrame > 1) /* ignore */`. Avoids 400 archetype changes per frame in heavy combat.

### F10 — `BattalionLeader` can become zombie at HP=0 from AOE (LOW)
**Files:** [DeathSystem.cs:81-115](Assets/Scripts/Systems/Combat/DeathSystem.cs#L81-L115), [ProjectileSystem.cs:44-48](Assets/Scripts/Systems/Combat/ProjectileSystem.cs#L44-L48)
**Severity:** Low — edge case; only triggers on AOE shots near a battalion centroid

`ProjectileSystem._aoeTargetQuery` is built with only `LocalTransform + FactionTag + Health` — no `WithNone<BattalionLeader>`. An AOE arrow that lands within radius of an invisible leader's position (BattalionSyncSystem keeps it at the formation centroid) WILL damage the leader. With 1 HP base, any AOE one-shots it. Phase 1 of DeathSystem excludes leaders from death detection; Phase 1.5 only kills leader if buffer is empty. Leader becomes a zombie at HP=0.

**Fix:** Add `.WithNone<BattalionLeader>()` to `_aoeTargetQuery` (line 44-48) and `BuildingCombatSystem`'s `targetQuery` (line 34-36).

---

## Architectural Smells

### S1 — Crystal aura systems still use manual `new EntityCommandBuffer(Allocator.Temp)`
`EnforcementNodeSystem.cs:45`, `SuppressionNodeSystem.cs:45`, `RestorationNodeSystem.cs`. Combat-systems half of task-027 was applied (all `Systems/Combat/*.cs` use the singleton ECB), but the crystal-systems half wasn't.

### S2 — DamageReflect / Ignite / VoidStrike pipeline coupling
`CombatDamageHelper` is a clean extraction for melee, but bakes in the assumption that attacker, target, and damage are all known at damage-application time. For ranged, damage is deferred to projectile impact. Either the helper needs split (`ApplyAtFireTime` / `ApplyAtImpact`) or projectile entity needs to carry shooter buff snapshot.

### S3 — `SpellBuff` fields overlap multiple disjoint effect channels with no priority/merge protocol
Five sources write `SpellBuff`: StoneheartBastion (ArmorBonus), GlassSanctum (DamageReflect), MirrorShield ability (DamageReflect), Safeguard ability (ArmorBonus), and four spell systems read different subsets. No single owner of the SpellBuff lifecycle; merge logic duplicated and inconsistent. Either split into per-effect components (`ArmorBuff`, `ReflectBuff`, `SpeedBuff`) so writers don't collide, or centralize an `AddOrMergeSpellBuff(target, source)` helper.

### S4 — Damage application pattern split: immediate `em.SetComponentData` for melee/siege, deferred ECB for tags
Health writes are immediate (per `MeleeCombatSystem.cs:199-200` comment) so multiple attackers in the same frame stack damage correctly. But on-hit metadata (`LastDamagedByFaction`, `LastAttackerEntity`) is half-immediate / half-ECB. Correct but fragile.

### S5 — Per-frame `ProjectileVisualSystem` builds projectile entity array twice and clones materials
`SpawnMissing` and `SyncTransforms` both call `_projectileQuery.ToEntityArray + ToComponentDataArray` — same query, ~80% overlap. Plus `Instantiate(template)` clones the GameObject including the renderer's material reference — with 100 in-flight arrows that's 100 materials/frame allocated and freed. Use an object pool keyed on `isLaser`.

---

## Verification of the 11 Surface-Scan Missing-Brace Claims

| Claim | File:Line | Verdict | Evidence |
|---|---|---|---|
| MB-10 | CombatDamageHelper.cs:111-113 | **REFUTED** | `if (HasComponent) em.SetComponentData; else ecb.AddComponent;` — `else` binds correctly |
| MB-11 | CombatDamageHelper.cs:119-121 | **REFUTED** | Same pattern |
| MB-12 | CombatDamageHelper.cs:140-142 | **REFUTED** | `if (!HasComponent) ecb.AddComponent; else ecb.SetComponent;` |
| MB-13 | CombatDamageHelper.cs:154-156 | **REFUTED** | Identical pattern |
| MB-14 | ProjectileSystem.cs:329-331 | **REFUTED** | Same pattern |
| MB-15 | ProjectileSystem.cs:337-339 | **REFUTED** | Same pattern, inside `if (shooter != Null)` block |
| MB-16 | ProjectileSystem.cs:391-393 | **REFUTED** | Same pattern in `ApplyAOEDamage` |
| MB-17 | ProjectileSystem.cs:398-400 | **REFUTED** | Same |
| MB-18 | TargetingSystem.cs:497-499 | **REFUTED** | `if (HasComponent<AttackCommand>) em.SetComponentData; else ecb.AddComponent;` |
| MB-19 | TargetingSystem.cs:508-510 | **REFUTED** | BattalionAttackTarget — same |
| MB-20 | TargetingSystem.cs:518-520 | **REFUTED** | DesiredDestination — same |

**Score: 0 / 11 confirmed.** All 11 are `if X-then-immediate / else-then-deferred` blocks where `else` correctly binds to the most recent unmatched `if`. Cosmetic ugliness, not bugs.

This is a substantial finding: it means the surface scan's "missing-brace cluster" is significantly smaller than reported. The actual count after verification is closer to 19-20 confirmed across the codebase, not 30+. Drop these 11 from the sweep.

---

## What I Verified Is Fine

- **Damage matrix** (TechTreeDB.cs:520-598 `CombatModifiers`) — initialized once, lookup consistent across all combat systems. Numbers match the design doc.
- **Defense per-damage-type** — `CombatModifiers.GetDefenseValue` correctly maps Melee/Ranged/Siege/Magic/True; True damage zeros defense.
- **Singleton ECB conversion (task-027)** — all 8 files in `Systems/Combat/` use it. Crystal aura systems still don't (S1).
- **`HasComponent<Health>` guards (task-014, Fix #212)** — present in MeleeCombatSystem.cs:76, RangedCombatSystem.cs:84, ProjectileSystem.cs:290.
- **`Invulnerable` check on single-target attacks (task-013, Fix #211)** — applied in MeleeCombatSystem, RangedCombatSystem, ProjectileSystem.ApplyDamage. NOT applied to AOE/DoT (F4).
- **CombatDamageHelper extraction (task-028)** — Condemned/Ignite/VoidStrike/DamageReflect/LastDamager all routed through the shared helper.
- **DeathSystem ordering** — `[UpdateAfter(typeof(ProjectileSystem))]` so all damage resolved before death detection. Phase pipeline is sound.
- **CrystalDeathDropSystem** — `[UpdateBefore(typeof(DeathSystem))]` and uses `WithNone<DeathAnimationState, BuildingCollapseState>` so cadavers fire exactly once per death.
- **Litharch healing (task-030)** — uses `em.SetComponentData(targetHealth)` for immediate write so heal isn't overwritten by combat-system damage in the same frame.
- **MindControlSystem, SummonDespawnSystem, SpellBuffSystem, Invulnerable timer ticking** — all clean.

## Things I Deliberately Didn't Dig Into

- **`BattalionSyncSystem.AssignPerMemberTargets` / `BattalionCombatHelpers`** — covered by task-053.
- **`GodsplinterCombatSystem` / `VeilstingerCombatSystem` full read** — only sampled siege-mode damage block.
- **`AbilityCommand.cs` / `CommandRouter.IssueAbilityDirect`** — task-051 MB-25 + task-054 F-1 already covered.
- **Tech-tree influences on damage** — covered by task-051 T-section; will be in tech deep-dive.
- **Visual / animation systems for combat** — out of subsystem.
- **Audio cues for hits / deaths** — out of subsystem.
