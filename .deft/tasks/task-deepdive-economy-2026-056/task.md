---
deft:
  id: task-deepdive-economy-2026-056
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
  labels: [code-quality, code-review, deep-dive, economy]
---

# Economy / Trading / Smelter / Supplies / Population deep-dive

## Context

Companion to task-052/053/054/055. Same method.

## TL;DR

Economy is **mostly competent** but contains **3 critical runtime bugs** the surface scan missed entirely while it focused on cosmetic patterns:

- **F1 (CRITICAL)**: `PillageSystem` pays the killer **every frame for ~2 seconds** (death animation duration) per kill. A worker kill → ~1800 supplies + 120 iron instead of 15 + 1. A building kill → 6000 supplies + 600 iron. **120× the intended reward.**
- **F2 (CRITICAL)**: `CaravanDeathSystem` has the same bug. Trader carrying 20 supplies/5 crystal → ~1200 supplies + 300 crystal payout instead of 10/2.
- **F3 (CRITICAL)**: `WallEnclosureIncomeSystem` rebuilds enclosure entities every 5s with `Elapsed=0`, but the tick interval is 10s. Wall enclosure income never accumulates → **0 supplies deposited from wall income, ever**.

Plus 7 lower-severity findings (vault interest double-multiplied, miner load lost on smelter reassign, dead `WallIncomeFromTech` field, ForgeSupply mid-trip leak, etc.).

**Missing-brace verification: 9 of 10 economy claims from task-051 are REFUTED**, only MB-5 (TradingPostSystem trader-approach offset) is a real bug. Same pattern as combat (0/11 confirmed). The task-051 sweep is significantly smaller than reported.

## Acceptance Criteria

- [ ] F1: `PillageSystem` excludes `DeathAnimationState`/`BuildingCollapseState`, OR adds a one-shot `PillagePaidTag`
- [ ] F2: same fix for `CaravanDeathSystem`
- [ ] F3: `WallEnclosureIncomeSystem` rebuild cycle preserves `Elapsed`, or update interval ≥ tick interval
- [ ] F4: vault interest multiplied at exactly one site (drop `ApplyVaultInterestDelta`)
- [ ] F5: forge reassign deposits any carried iron first
- [ ] Drop 9 false-positive MB entries from task-051

---

## Verified Findings

### F1 — CRITICAL: Pillage paid every frame for ~2 seconds per kill
**File:** [PillageSystem.cs:43-95](Assets/Scripts/Systems/Economy/PillageSystem.cs#L43-L95)
**Severity:** Critical — economy completely warped on Feraldis maps

Query (line 48-50) is `Health, LastDamagedByFaction, FactionTag` with no exclusion of `DeathAnimationState` or `BuildingCollapseState`. Body does `if (health.ValueRO.Value > 0) continue;` to keep only the dead, then unconditionally calls `FactionEconomy.Add(...)`.

[DeathSystem.cs:148-186](Assets/Scripts/Systems/Combat/DeathSystem.cs#L148-L186) keeps dead units alive for `DeathAnimationDuration = 2.0f` and dead buildings for `BuildingCollapseDuration = 2.0f`. Health stays at 0, `LastDamagedByFaction` and `FactionTag` are not removed. **At 60 fps a single Feraldis kill on a worker pays ~120 × (15 supplies + 1 iron) = 1800 supplies + 120 iron per kill, not the 15 + 1 the constants advertise.** Killed building → 6000 supplies + 600 iron.

**Fix:** Add `.WithNone<DeathAnimationState, BuildingCollapseState>()` to the query, or stamp a one-shot `PillagePaidTag` after first payout.

### F2 — CRITICAL: Caravan loot paid every frame for ~2 seconds per trader killed
**File:** [CaravanDeathSystem.cs:32-56](Assets/Scripts/Systems/Economy/CaravanDeathSystem.cs#L32-L56)
**Severity:** Critical — same shape as F1

Query is `Health, RunaiTraderState, LastDamagedByFaction` with no exclusion. `[UpdateBefore(typeof(DeathSystem))]` only handles the first frame. A trader carrying `AccumulatedSupplies=20, AccumulatedCrystal=5` will pay roughly 20×0.5 supplies + 5×0.5 crystal per frame for 2 seconds = **~1200 supplies + ~300 crystal instead of 10/2**.

### F3 — CRITICAL: Wall enclosure income never deposits
**File:** [WallEnclosureIncomeSystem.cs:24-41, 211-223](Assets/Scripts/Economy/WallEnclosureIncomeSystem.cs#L24-L41)
**Severity:** Critical — entire Alanthor wall income mechanic is silently broken

`UpdateInterval = 5f` (line 24), `TickInterval = 10f` (line 26). Each 5-second update destroys all `WallEnclosureIncomeTag` entities (lines 47-52) then creates new ones with `SuppliesIncome { ..., Interval = 10f, Elapsed = 0f }` (lines 218-223). Because the entity is destroyed and recreated before its `Elapsed` accumulator can ever reach 10s, `ResourceTickSystem`'s `if (Elapsed >= Interval)` branch (`ResourceTickSystem.cs:53`) never fires.

**Net wall income = 0 supplies.** The HUD displays the wall income (per ResourceHUD), but the deposit never happens.

**Fix:** Make `UpdateInterval ≥ TickInterval` and don't destroy entities that still represent a valid enclosure; OR preserve `Elapsed` across rebuilds; OR refactor to update existing entities in place.

### F4 — Vault interest sect multiplier applied twice
**Files:** [SectEffectSystem.cs:235-258](Assets/Scripts/Economy/SectEffectSystem.cs#L235-L258) (`ApplyVaultInterestDelta`) and [VaultInterestSystem.cs:38-47](Assets/Scripts/Systems/Economy/VaultInterestSystem.cs#L38-L47)
**Severity:** High — Quiet Vault sect bonus is 1.69× instead of 1.3×

`ApplyVaultInterestDelta` does `vault.InterestRate *= delta` and writes back via `SetComponentData`. `VaultInterestSystem` then reads `vault.InterestRate` and multiplies again: `rate *= FactionSectState.Instance.GetMultipliers(vFaction).VaultInterest`. With QuietVault adopted (`VaultInterest = 1.3`), effective rate = `base × 1.3 × 1.3 = 1.69 × base`. At temple level 2 (`scaling = 1.5`), `1 + 0.30·1.5 = 1.45`, doubled = **2.10× base**. Newly-placed vaults built after adoption start at `0.03f` factory default → only get the runtime mult → yield differs from older vaults.

**Fix:** Drop `ApplyVaultInterestDelta`; rely solely on runtime multiplication.

### F5 — Reassigning a loaded miner to a Smelter loses its carried resources
**Files:** [RTSInputManager.cs:694-735](Assets/Scripts/Input/RTSInputManager.cs#L694-L735), [ForgeSupplySystem.cs:128-138](Assets/Scripts/Systems/Work/ForgeSupplySystem.cs#L128-L138)
**Severity:** Medium — small recurring iron leak

`IssueForgeSupply` resets `MinerState.State = Idle`, `AssignedDeposit = Null`, `DropoffTarget = Null`, but does **not** reset `CurrentLoad`. `ForgeSupplySystem.ProcessPickupPhase` line 135 then unconditionally writes `miner.CurrentLoad = PickupAmount`, overwriting whatever the miner was carrying. A miner returning from a deposit with 9 iron, redirected to a forge, drops those 9 iron on the floor.

**Fix:** Before assigning `ForgeSupplyOrder`, deposit any existing `CurrentLoad` to bank/dropoff and zero `CurrentLoad`.

### F6 — `BatchTrainingSystem` / `TrainingSystem` can stall if queue externally cleared (LOW)
**Files:** [BatchTrainingSystem.cs:90-131](Assets/Scripts/Systems/Training/BatchTrainingSystem.cs#L90-L131), [TrainingSystem.cs:96-155](Assets/Scripts/Systems/Training/TrainingSystem.cs#L96-L155)
**Severity:** Low — fragility; no current code path triggers it

If queue is somehow emptied while `Busy = 1`, `Remaining` keeps going negative without being reset. Cancel-training UI would trigger this.

### F7 — `ForgeSupplySystem` charges bank then loses load if forge dies mid-trip
**File:** [ForgeSupplySystem.cs:128-204](Assets/Scripts/Systems/Work/ForgeSupplySystem.cs#L128-L204)
**Severity:** Medium

`ProcessPickupPhase` spends `PickupAmount=10` from the bank **before** the miner walks to the forge. If the forge is destroyed between pickup and delivery (lines 150-156), `ForgeSupplyOrder` is removed and miner goes idle — the **carried 10 iron is never refunded**.

**Fix:** If the forge dies in `ProcessDeliveryPhase`, set `State = ReturningToBase` and route to nearest Hall/Hut.

### F8 — `WallIncomeFromTech` field set but never read
**File:** [SectEffectSystem.cs:599](Assets/Scripts/Economy/SectEffectSystem.cs#L599)
**Severity:** Low — dead code; TerracePlanning's +20% has no effect

`WallIncomeFromTech` field is assigned but greps confirm it's never queried.

### F9 — `PopulationSyncSystem` overflow on hut destruction (verified safe)
**File:** [PopulationSyncSystem.cs:24-77](Assets/Scripts/Systems/Training/PopulationSyncSystem.cs#L24-L77)
Listed for completeness — verified safe. Recompute every frame; training is correctly blocked when Current > Max; no units killed.

### F10 — `IssueForgeSupply` clears `UserMoveOrder` directly (fragile order)
**File:** [RTSInputManager.cs:704-705](Assets/Scripts/Input/RTSInputManager.cs#L704-L705)
**Severity:** Low

Removes `UserMoveOrder` directly via `EntityManager.RemoveComponent`. RTSInputManager runs before ECS so this is safe in current code; flagging only because the pattern is fragile.

---

## Architectural Smells

- **Two-layer multiplier application**: `SectEffectSystem.ApplyXxxDelta` mutates entity components AND `FactionSectState` exposes a query API. Some systems read live multipliers; others rely on the component already being modified. The contract is implicit and was the source of F-4. Single source of truth (read-time multiplication only) would be safer.
- **`WallEnclosureIncomeSystem` does a full destroy-and-rebuild every 5s.** Beyond F-3, this thrashes the entity manager.
- **Pillage / loot systems lack a one-shot guard.** Both rely on `Health <= 0` as the only "did I already pay?" signal. Adding `PaidPillageTag` after first payout would make F-1/F-2 unrepresentable.
- **`FactionEconomy._bankCache` is a static `Dictionary` keyed by `Faction`** — per-process, not per-world. If two worlds run in parallel they'd fight over the cache.
- **`GathererHutIncomeSystem` recomputes farm areas every 2s using O(huts²) nested loops** — for 20 huts × 30×30 cells × 20 enclosures = ~1.1M iterations every 2s. Acceptable now; problem at scale.
- **`Hut.cs` lacks a `CreateUnderConstruction` overload** while `BuildingFactory` provides one elsewhere — `BuildingConstructionSystem.CompleteConstruction` safety net only re-attaches `SuppliesIncome` to GathererHuts; if any other income building's CreateUnderConstruction path forgets the income component, it will silently never produce income.

---

## Verification of Surface-Scan Claims from task-051

| Claim | Verdict | Evidence |
|---|---|---|
| **MB-4** ResourceTickSystem.cs:62-64 — supplies double-applied | **REFUTED** (cosmetic). `if (TryGetValue) ... = existing + amount; suppliesPerFaction.TryAdd(key, amount);`. The unbraced `if` only governs the first statement, but `TryAdd`'s "fail-on-existence" semantics make the second call a no-op when the key already exists. Net behavior correct. |
| **MB-5** TradingPostSystem.cs:549-550 | **CONFIRMED** (real bug). `if (len > 0.01f) position = buildingPos + (dir / len) * 3f; position = buildingPos + new float3(3f, 0f, 0f);` — line 550 runs unconditionally, **always overwriting** the directional offset with a fixed +X offset. Traders/patrols always approach from the +X side. Visual/clustering bug. |
| **MB-6** ForgeSupplySystem.cs:273-276 | **REFUTED** (cosmetic). `SetDestination`'s `if/else` is correctly bound. |
| **MB-7** ForgeSupplySystem.cs:281-282 | **REFUTED**. `StopMoving` is a single-statement `if` with no `else`; nothing missing. |
| **MB-8** BuildingConstructionSystem.cs:156-159 | **REFUTED** (cosmetic). `if (!HasComponent<BuildOrder>) AddComponentData; else SetComponentData;` — `else` correctly binds. |
| **MB-9** BuildingConstructionSystem.cs:477-480 | **REFUTED** (cosmetic). Same pattern. |
| **E-1** FactionEconomy.cs:32-36 — ClearCache incompleteness | **REFUTED**. ClearCache clears `_bankCache` and resets `_bankQueryInitialized`; the next `TryGetBank` rebuilds `_bankQuery` against the live `EntityManager`. The stale field is overwritten before next use. |
| **E-2** ResourceTickSystem.cs:95-128 — race condition | **REFUTED**. Both phases execute sequentially in `OnUpdate` on the main thread. No parallel writes. |
| **E-3** GathererHutIncomeSystem.cs:62-63 — redundant pattern | **CONFIRMED but intentional**. Block comment at line 51-53 documents "Two-step add+set for reliability outside Burst." Cosmetic / micro-perf only. |
| **E-4** ResourceTickSystem.cs:85-86 — no lower-bound clamp | **CONFIRMED but harmless**. The accumulator is built only from positive `PerTick` × positive `ticks` × positive `AllIncome`. Code-style issue, not a bug. |

**Score: 1 / 10 confirmed as real bug** (MB-5). Drop the other 9 from task-051's MB sweep.

---

## What I Verified Is Fine

- **Miner → bank deposit** (MiningSystem.cs:333-394, CrystalMiningSystem.cs:304-368): once on arrival, Clamp() called, deposit-then-loop logic correct.
- **`ForgeConversionSystem`** 5 iron + 3 crystal → 1 veilsteel: timer logic sound, no double-conversion.
- **`FactionEconomy.Spend` / `Add`**: affordability check before deduction; `Add` calls `Clamp()`.
- **`PopulationSyncSystem`**: clean per-frame recompute; respects RunaiPopOverride and AbsoluteMax = 200.
- **`TrainingSystem` / `BatchTrainingSystem`** population gating via `spawnedPopThisFrame`.
- **`BazaarPackSystem`** proportional HP transfer with placement validation on unpack.
- **`GathererHutIncomeSystem`** first-come-first-served farm priority via `FarmBuildOrder`.
- **`SectEffectSystem._appliedMults` delta tracking** (Fix #198): correctly prevents exponential compounding across multiple sect adoptions and temple level-ups, *except* for the F-4 vault interest case (which is a different shape).
- **`TraderMovementSystem`** distance-based accumulator with fractional remainder preserved.
- **`EconomyBootstrap`** initializes per-faction state cleanly.

---

## Things I Deliberately Didn't Dig Into

- **`SpellCastSystem.cs`** — outside subsystem.
- **`PassabilityBuildingSync`** — covered in task-053.
- **`AlanthorWall` chapel/tower upgrade paths** — only the income side touches Economy.
- **`TempleCascadeDestroySystem` / `TempleChapelBuildSystem`** — RP / sect adoption flow, not direct resource accounting.
- **AI strategic income/spending in `AIEconomyManager`** — covered in task-052.
- **Multiplayer determinism for resource ticks** — `ResourceTickSystem` is not gated by `IsHost` (per task-054), but `FactionSectState.Instance` is a managed singleton; whether it stays in sync across host/client is a multiplayer concern (see task-054).
