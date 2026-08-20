# AI Manager Architecture — Budgeted Managers with a Request Bus

> Commissioned 2026-08-04 after three logged human-vs-AI matches exposed the
> structural limit of the single-brain design: every failure was one
> unconditional rule starving another (huts vs Barracks, replacements vs
> age-up, defense vs waves). Reserves and grace windows patched the
> symptoms; this document plans the cure — explicit budgets, separated
> managers, and negotiated requests.
>
> Companion docs: [AI_Assessment_and_Plan.md](AI_Assessment_and_Plan.md)
> (perception/targeting phases — still valid, this builds on it),
> [Research/AoE4_AI_Study.md](Research/AoE4_AI_Study.md).

---

## Part 1 — Survey: how the reference games structure macro AI

### Age of Empires 4 (Relic — and the AoE lineage)

The AoE series is the **data-driven desire** school. AoE2's AI exposed
~250 "strategic numbers" (percent-villagers-on-food, retreat thresholds…)
consumed by one monolithic rule engine; AoE4 modernized the same idea:
difficulty tiers and personalities are **parameter files, not code
branches** (our `AIDifficultyProfile` already copies this). Its economy AI
runs on **gatherer ratio targets** that shift by age and situation, and
production runs on **scored desires** — "I want 2 more ranged units"
competes numerically with "I want the next age" for the same bank.
Key takeaways:
- One shared bank, but **desires carry priority weights** re-evaluated
  continuously — there is no hard budget wall, which is why AoE AIs
  sometimes float or stall exactly like ours did.
- Openings are **scripted build orders that hand off** to the reactive
  desire engine (we already replicated this handoff; our bug history shows
  the handoff boundary is where starvation lives).

### StarCraft 2 (Blizzard's built-in + the bot-community canon)

Blizzard's ladder AI is scripted openings plus reactive priority tables,
but the architecture the user's request actually describes is the
**community bot canon** (UAlbertaBot → CommandCenter lineage, the basis of
most competitive SC2/BW bots):
- **Managers as modules**: `WorkerManager`, `ProductionManager`,
  `BuildingManager`, `CombatCommander` (which owns `Squad`s), each ticked
  by a top `GameCommander`.
- **A single ProductionQueue** into which other modules **push requests**
  with priorities — e.g. CombatCommander pushes "need detection",
  WorkerManager pushes "need depot". This is exactly the "slip requests
  into another manager's queue" pattern requested.
- **Resource reservation**: when the ProductionManager commits to an
  expensive item, it *reserves* minerals/gas so lower-priority requests
  cannot starve it — the direct ancestor of the budget-wallet idea.
- Squads split army control: a **defense squad** holds regions, an
  **attack squad** pushes, units are **requisitioned between squads** by a
  priority arbiter. Defender/attacker managers below mirror this.

### Company of Heroes (Relic)

CoH's skirmish AI separates **strategic purchasing** from **tactical
control** completely:
- A personality-weighted **purchase scorer** decides what to buy from the
  shared manpower/munitions/fuel pools (tables per personality, situation
  multipliers — losing infantry raises infantry weight).
- **Squad-level tactical AI** (cover use, retreat morale) runs
  independently of purchasing; the strategic layer only assigns squads to
  **objectives** (attack ground, defend sector).
- Notable: CoH ties weights to **territory sectors** — losing a fuel
  sector rewrites the purchase table. Our influence/curse map gives us an
  even richer version of this signal for situational weights.

### Supreme Commander (GPG; refined by Sorian / LOUD / M27 community AIs)

The closest existing implementation of the full request architecture:
- Each AI brain owns **BuilderManagers per base**: an
  `EngineerManager`, `FactoryManager`, and `PlatoonFormManager`, each
  holding **priority-sorted builder lists** gated by **condition
  functions** ("mass storage > 40%", "enemy air threat > X") — build
  orders are data with situational gates, per manager.
- **Economy conditions as first-class gates**: nearly every builder task
  checks percentage-of-income conditions, making the economy allocation
  emergent from hundreds of small gated decisions rather than one wallet.
  Community AIs (M27, LOUD) hardened this with explicit **spend
  categories tracked per purpose** — the literal budget split requested.
- **Platoons**: formed by the PlatoonFormManager from factory output
  pools, then handed to platoon behaviors (raid, defend, assault).
  Factories *produce into a pool*; platoon formers *requisition from it* —
  precisely the "attacker/defender requisition from the general military
  manager" pattern.

### Synthesis — what we adopt

| Pattern | Source | Adopted as |
|---|---|---|
| Data-driven weights per situation | AoE4 / CoH | `BudgetPolicy` weight tables |
| Hard budget wallets + reservation | SC2 bots / M27 | `IncomeAllocator` + per-manager wallets |
| Managers with own queues, cross-pushed requests | SC2 bot canon | `RequestBus` |
| Per-manager build orders with condition gates | SupCom builder lists | Manager `TaskList`s |
| Production pool + requisition for squads | SupCom platoons | MilitaryManager pool + Defender/Attacker requisition |
| Strategic/tactical separation | CoH | Managers never micro; missions/squads keep tactical control |

---

## Part 2 — Target architecture

### 2.1 The income split

`IncomeAllocator` (runs each think tick, host-side, before all managers):

- Tracks **actual income** per resource (delta of bank + spend ledger per
  window, already samplable the way StatsBoardHUD does).
- Splits each resource's income into three **wallets** by the current
  weight vector: **Advancement / Military / EconomyExpansion**
  (weights normalized; e.g. default 25/35/40 in Age 0).
- Wallets are *virtual*: one real faction bank remains (lockstep
  untouched); a wallet is a running allowance `wallet += income * weight −
  spends`. A manager may only issue a purchase if **its wallet covers it**
  (clamped low at 0, capped so windfalls don't distort: cap ≈ 2 minutes of
  income).
- **Weight policy** (`BudgetPolicy.Evaluate`) — situational table, the
  CoH/AoE4 lesson. Inputs we already compute: posture, army size vs
  desired, hut count/income slope, current build-order gate, curse threat
  at base, match phase. Examples:
  - Current gate is AgeUp/Choice → Advancement 60/M 25/E 15 (replaces the
    ad-hoc `savingForGate`).
  - Posture Defend/Rebuild → Military 60/E 25/A 15.
  - Supplies income < spend rate → Economy +20 (the "lack of supplies
    means huts" rule, now expressed as budget, not bypass).
  - Early game default → Economy-heavy (mirrors every surveyed game).

### 2.2 The three macro managers

Each manager: own **TaskList** (its "build order" — data, condition-gated
like SupCom builders), own wallet, one `Tick(em, ctx)` entry called from
the brain loop in fixed order (Economy → Advancement → Military so
foundational requests land first). They SHARE: the worker pool, the
request bus, and the perception context (posture, threat, intel).

- **EconomyManager** — owns: Gatherer's Hut pipeline, worker floor +
  growth targets, housing (pop headroom), GH research line
  (the Guild Surveys), expansion placement (covered-ground
  rule), miner allocation (absorbs `AssignIdleMiners`), reclaim triggers
  for corrupted patches (it owns the patches). Fulfills: resource &
  housing & builder requests.
- **AdvancementManager** — owns: choice building, age-up, non-GH
  research ladders, Temple/sect adoption, King's Court uniques,
  Crucible/Smelter veilsteel engine, building level-ups (with the
  army-first rule now expressed as budget priority, not an iron constant).
- **MilitaryManager** — owns: production buildings (Barracks/Range/
  Stable/SiegeYard counts), unit production into the **army pool**,
  composition targets (absorbs PickCompositionUnit + counter-comp),
  equipment tier upgrades. Does NOT command armies — it produces and
  maintains the pool.

### 2.3 The request bus

```
struct AIRequest {
  RequestKind Kind;      // Resources, Housing, Builder, Troops, Production
  Faction Owner; ManagerId From, To;
  int Amount; FixedString64 What;   // e.g. unit id, resource type
  byte Priority;         // Critical / High / Normal
  float Expiry;          // stale requests die — no zombie queues
}
```

- Plain managed list per brain (AI is host-only — no lockstep concerns;
  decisions exit only through CommandRouter as today).
- A request is **slipped into the target manager's TaskList** at a
  position set by Priority (Critical preempts the current task, High goes
  next, Normal appends). The receiving manager fulfills it *from its own
  wallet* — a Military "need housing" request costs the EconomyManager's
  budget: that is the negotiation.
- Canonical flows: Military→Economy (resources, housing),
  Advancement→Economy (resources), Economy→others (**builders**: a
  requested builder is released from mining and tagged reserved — the
  workers-are-shared rule with explicit ownership transfer),
  Defender/Attacker→Military (troops), Defender↔Attacker (troop transfer).
- **Anti-deadlock rules** (the lesson of this whole week): every request
  has an expiry; wallet transfers are one-shot grants, not standing
  drains; a manager that cannot fulfill logs `REQUEST-DENIED` with the
  gate — the AILogger discipline extended to negotiations.

### 2.4 Army management: Defender + Attacker over the pool

Mirrors SupCom platoons / SC2 squads:

- **MilitaryManager** produces into the **army pool** (unassigned units).
- **DefenderManager** — owns home security: garrison size scaled by
  threat (ThreatMap + curse growths near base), absorbs
  `DefendBase`-engagement and Sporeling strikes; requisitions troops from
  the pool at **Critical** when the base is hit (and may requisition FROM
  the AttackerManager — recall — when the pool is empty; that is today's
  recall behavior, now explicit).
- **AttackerManager** — owns waves and raids: absorbs TickAttackWaves,
  missions/staging, raid parties, retreat handling. Requisitions
  wave-sized batches at Normal priority; releases survivors back to the
  pool on mission end.
- Priority arbiter: Critical defense requisitions strip the attacker's
  *staging* units but never a mission already striking (no thrash — the
  M6 retreat logic remains the only way a striking force comes home).

### 2.5 What deliberately stays

Waves' cadence/escalation values, posture evaluation, target scoring,
scout director, all difficulty profiles, and every CommandRouter contract.
The managers re-home existing logic; they do not re-derive it.

---

## Part 3 — Migration plan (extract, never rewrite)

Ordered so every phase ships playable and log-verifiable. Each phase ends
with a human-vs-AI match and a log read — the loop that found every bug
this week.

- **M-A (scaffold)**: `AIManagers/` folder — `IncomeAllocator`,
  `BudgetPolicy`, `RequestBus`, `ManagerBase`, wallet ledger + AILogger
  category `BUDGET` (per-minute wallet snapshot lines). SimpleAISystem
  ticks the allocator but managers are empty shells: pure observation
  match to validate income tracking.
- **M-B (EconomyManager)**: move TickEconomy contents (worker floor, hut
  pipeline, GH research), AssignIdleMiners, EnsurePopulationHeadroom,
  reclaim triggers. All spends gated by the Economy wallet. The
  `savingForGate` hack dies here (BudgetPolicy covers it).
- **M-C (MilitaryManager + pool)**: move production-building growth,
  sustained production, ReplaceLostUnits, composition. Introduce the army
  pool component (`ArmyAssignment { ManagerId Owner }` on units).
- **M-D (Defender/Attacker)**: split DefendBase + TickAttackWaves +
  missions into the two managers with requisition; delete the posture
  special-cases they replace.
- **M-E (AdvancementManager)**: move age-up/choice/research/uniques/
  upgrade ladder + endgame-system building duties (AIAlanthorEndgame
  keeps culture-specific verbs/sects, requests buildings via the bus).
- **M-F (request completeness + tuning)**: housing/builder/troop request
  flows exercised end-to-end; weight-table tuning from match logs.
- Per-manager **build orders as data** (M-B onward): strategy files
  become three short lists (economy opener / military opener / tech
  opener) instead of one interleaved script — Rush = heavy military list,
  EcoBoom = heavy economy list; the allocator's weights per strategy
  replace most step ordering.

Estimated shape: ~5 new files, ~4 heavily-thinned existing systems;
SimpleAISystem shrinks to perception + posture + the manager tick loop.

## Part 4 — Risks

- **Wallet starvation replacing bank starvation** — mitigated by weight
  floors (no wallet below 10%) and request escalation to Critical.
- **Handoff regressions while extracting** — mitigated by phase-per-match
  log verification and keeping old code paths behind a
  `UseManagerAI` constant until M-F.
- **Determinism** — unchanged: AI is host-authoritative and already
  replicates through CommandRouter only.
