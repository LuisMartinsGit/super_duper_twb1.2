# AI Assessment and Full-Scale AI Plan

Date: 2026-06-11. Branch: refactor/scripts-layout-and-stat-sos.
Scope: player-faction AI, The Border AI, and the perception/targeting
infrastructure both build on. This is a technical plan; any item marked
**[design-gate]** introduces player-visible mechanics and must be specified in
docs/Design/ before implementation (per CLAUDE.md).

---

## Part 1 — Assessment

### 1.1 Player AI (SimpleAISystem + satellites)

Active stack (the old 4,800-line manager stack was deleted in b43ca5f; no
duplicate AI remains):

| System | Cadence | Role |
|---|---|---|
| SimpleAISystem | 0.5–5 s by difficulty | Age-1 build orders, training, placement, mining, age-up, attacks, scout patrol |
| AIBuildingUpgradeSystem | 6 s | Era-2+ building level upgrades |
| AIAlanthorEndgameSystem | 5 s | Alanthor-only: sects, powers, smelter, towers, armored units, worker flee |
| FeraldisRaiderPatrolSystem | 1.5 s | Uncontrollable raider aggression |

What works well today:
- Six build-order strategies (Rush/EcoBoom/TechBoom/Aggressive/Turtle/Defensive),
  deterministically assigned per faction from SpawnSeed.
- Loss replacement (desired-vs-alive+queued, one replacement per category per
  tick) and an indefinite maintenance loop after the build order ends.
- Fog-of-war-honest target selection: mobile targets need current visibility,
  static targets need revealed-only (mirrors the player's ghost-building rule).
- Scouts exist and patrol because the AI genuinely needs vision to attack.
- Fully lockstep-safe: seeded shared RNG, all commands via CommandRouter,
  host-only AI in multiplayer.

Hard gaps (verbatim from the audit):
- Strategy locked at spawn; `AIPersonality` enum exists but is read by nothing.
- Scouting is random wandering; sightings never feed back into decisions.
- Attack target choice is a fixed priority ladder (miners > gatherer huts >
  border nodes > halls), nearest-first — no value scoring, no risk term.
- No threat assessment, no defense response, no retreat, no focus fire, no
  counter-composition, no walls (task-109), no post-Age-1 research breadth.
- Only Alanthor has an endgame system; Runai/Feraldis idle after age-up.
- `GetIncomeMultiplier` difficulty knob exists but is wired to nothing.

### 1.2 The Border AI

Active stack: BorderAISystem (5 s; node builds, expansion, phases),
BorderArmyAISystem (1 s income/training, 5 s decisions; per-node banks, defend +
attack tiers), BorderHordeSystem (0.5 s; cohesive marches), CrystalIncomeSystem,
NodeStateReversionSystem (the "map wants to be Active" reversion timers).

What works well: per-node economies, 9-tier army compositions, phase-gated
sub-node unlocks (0/5/15 min), logarithmic expansion pacing, cohesive horde
movement with repathing, correct Glow rules (drops only on ritual state
changes), deterministic seeded RNG keyed to lockstep tick.

Hard gaps:
- Terrain creep is visual only — spreadRadius is fixed at spawn; nothing
  functionally converts ground or grows territory. **[design-gate]**
- Escalation unlocks building types but never scales income, production speed,
  or unit caps — late-game pressure plateaus.
- The wave-dispatch system is dead code (UpdateAttackWaves commented out).
- Attack target selection is a seeded-random faction pick, then nearest Hall —
  the border cannot punish weakness or react to ritual threats.

### 1.3 Infrastructure available to a full-scale AI

Ready today:
- Per-faction visibility: FogOfWarManager keeps separate visible (per-frame) and
  revealed (persistent) grids per faction, with static queries
  `FogOfWarSystem.IsVisibleToFaction / IsRevealedToFaction`.
- TargetingSystem: 20 u spatial hash rebuilt per frame, melee two-pass nearest
  with an 8-attacker-per-target cap; scouts are class-gated out of combat.
- Nav stack: NavCostField (1 u passability + `NavGridQuery` helpers),
  NavSpatialHash (2 u unit hash, currently steering-internal), portal graph.
- Full deterministic command vocabulary via CommandRouter (23 commands: move,
  attack-move, patrol, hold, build, gather, train, research, rally, repair,
  god powers, rituals, equipment tiers, wall-gate conversion, ...).
- Cheap economy introspection: FactionResources, FactionPopulation,
  FactionEconomy.CanAfford/Spend.
- Dormant but well-shaped AI components surviving the manager-stack deletion:
  `ExplorationZone`, `ScoutAssignment`, `EnemySighting`, `CombatPower`,
  `AISharedKnowledge` (single aggregate last-known-position).

Missing (the load-bearing absences this plan fills):
1. No per-enemy last-known-position store (intel memory).
2. No threat map (the Influence system is a GPU visual, not tactical data).
3. No target value function anywhere (strategic or tactical).
4. No reusable spatial query API for AI ("enemy strength within R of P").
5. No scout-sighting feedback loop into decisions.

---

## Part 2 — Plan: full-scale AI with scout mechanics and target value assessment

Design principles: build on what exists (revive the dormant scouting
components rather than inventing parallel ones); every layer deterministic
(seeded RNG, fixed iteration order, commands via CommandRouter, host-only);
every constant in a ScriptableObject following the BorderSettingsSO pattern;
each phase shippable and independently testable.

### Phase 0 — Shared perception foundations (everything else depends on this)

**0.1 FactionIntel (last-known-position memory).**
Per AI faction, a dynamic buffer keyed by enemy entity:

    EnemySightingRecord { Entity Enemy; float3 Position; uint LastSeenTick;
                          half EstStrength; byte Category; byte Stale; }

Written by a new `IntelSystem` on the AI think cadence (not per frame): for
each entity visible to faction F (reuse the existing fog stamp pass — piggyback
during FogOfWarSystem's stamp loop to collect "F sees entity E" pairs instead
of re-scanning), upsert a record. Mark `Stale` when the entity dies (hook the
existing death/cleanup path). Replaces and generalizes
`AISharedKnowledge.EnemyLastKnownPosition`.

**0.2 ThreatMap (per-faction coarse grid).**
16 u cells (map of 400 u -> 25x25 = 625 cells; trivially small). Two layers:
- Dynamic: combat systems stamp damage events (position, amount) into the
  attacker's victim-faction grid; exponential decay applied on the AI tick.
  Use integer fixed-point accumulation for determinism.
- Static: known enemy towers/military buildings from FactionIntel project a
  constant threat disc.
Exposed as `ThreatMap.SampleWorld(faction, float3) -> int` and
`MaxCellInRadius(...)`.

**0.3 TacticalQuery API.**
Thin readonly facade over a per-AI-tick rebuild (reuse TargetingSystem's hash
build code, lifted into a shared utility): `EnemiesInRadius(F, pos, r)`,
`StrengthInRadius(F, pos, r)` (sums CombatPower), `NearestOwn(F, pos, filter)`.
Strength = the revived `CombatPower` component, computed once per unit from its
def (damage x HP heuristic) at spawn.

Deliverable: systems + an AIDebugOverlay (threat heatmap + intel pins) behind a
debug flag. No behavior change yet. Est. 3-5 sessions.

### Phase 1 — Target value assessment

**1.1 Strategic target scoring (replaces the fixed ladder).**
`TargetScorer.Score(candidate, faction)` over FactionIntel candidates:

    score = TypeValue(category)            // data-driven per personality
          + Opportunity                    // low defense: -StrengthInRadius(target)
          + EconomicDamage                 // miners/eco buildings while contested
          - Risk                           // ThreatMap sample on approach corridor
          - TravelCost                     // path distance (portal-graph estimate)
          - IntelAge penalty               // stale sightings score low

SimpleAISystem.ChooseAttackTarget consumes the top-scored candidate; requires
intel freshness below a threshold or triggers Phase-2 re-scouting first.
All weights in `AISettingsSO` (new, mirrors BorderSettingsSO).

**1.2 Tactical value weighting in TargetingSystem.**
Keep nearest-first as the base (perf + determinism), add a bounded type-priority
tie-break within 1.25x of nearest distance: healer > siege > ranged > melee >
worker for combat units; keep the 8-attacker cap. One small scoring function,
no architectural change.

**1.3 Border target value.** Replace random faction pick in BorderArmyAISystem
with the same scorer (border-specific weights: ritual casters and weakest-defended
bases high, fortified positions low). Punishes weakness; reacts to purification
threats.

Est. 3-4 sessions. Testable: AI-vs-AI headless runs, assert attack targets
correlate with undefended/valuable candidates.

### Phase 2 — Scout mechanics (information-driven scouting)

**2.1 Zone-based exploration.** Revive `ExplorationZone` + `ScoutAssignment`:
zone grid (32 u) with priority = staleness x strategic interest (unexplored >
suspected expansion sites > enemy base perimeter > own perimeter). A
`ScoutDirectorSystem` (AI cadence) assigns each idle scout the best zone by
priority/distance; arrival re-stamps zone freshness.

**2.2 Sighting feedback loop.** Scouts have no special code need — IntelSystem
(0.1) already records what they reveal. The director adds: on first sighting of
an enemy base, mark its perimeter zones as periodic re-scout targets; before
any TryLaunchAttack, if best target's intel age > threshold, dispatch a scout
to it and defer the attack (the "scout then strike" loop).

**2.3 Threat-aware routing + survival.** Scout destination selection samples
ThreatMap and prefers low-threat approach cells; on taking damage, scout flees
toward own Hall (generalize the worker-flee logic already in
AIAlanthorEndgameSystem). Maintain N scouts by phase (1 early, 2 after age-up)
through the existing replacement mechanism.

Est. 3-4 sessions. Testable: time-to-enemy-base-discovered metric; intel
freshness at attack time; scout survival rate.

### Phase 3 — Adaptive strategic layer

**3.1 Wire AIPersonality.** Personality becomes a weight vector (economy,
aggression, defense, tech, risk tolerance) in AISettingsSO, modulating: build
order selection, attack thresholds, TargetScorer weights, scout count.

**3.2 Posture evaluation.** A `StrategyEvaluatorSystem` (10-15 s cadence) reads
FactionIntel + ThreatMap + economy and switches posture among
Develop / Pressure / AllIn / Defend / Rebuild. Posture gates the maintenance
loop (army floor, attack threshold) rather than rewriting build orders —
build orders stay as openers, postures govern mid/late game.

**3.3 Defense response.** ThreatMap spike inside own base radius =>
Defend posture: recall fielded army to a defense rally, queue defenders,
emergency-repair (IssueRepair exists and is unused by AI).

**3.4 Counter-composition.** Tally enemy composition categories from
FactionIntel sightings; bias training toward counters via the existing
bonus-vs-tags data in unit defs (no new combat mechanics needed).

**3.5 Culture endgame parity.** Runai and Feraldis endgame systems patterned on
AIAlanthorEndgameSystem (Runai: trade lanes, vault, conversion rituals;
Feraldis: raider houses, blood pools, keep conversion). **[design-gate]** for
any behavior not already specified in the Age-1 design docs.

Est. 5-7 sessions.

### Phase 4 — Border director upgrade

**4.1 Economy-scaling escalation.** Phase index multiplies per-node income and
training speed (new BorderSettingsSO fields), so late-game pressure grows
instead of plateauing.
**4.2 Director waves.** Replace the dead UpdateAttackWaves with a director that
sizes and aims waves using the Phase-1 scorer (attack the weakest player;
threaten ritual casters; synchronized multi-node waves at high phases).
**4.3 Functional creep.** Spread radius slowly grows with node age; border
ground gives the border income it already counts but also slows enemy units /
blocks construction. **[design-gate — must be specified in docs/Design first.]**

Est. 3-4 sessions (4.3 excluded until designed).

### Phase 5 — Combat micro (stretch)

Retreat when local fight is lost (StrengthInRadius comparison at the
engagement centroid), shared focus-fire hints (army-level priority target
written to a component the tactical tie-break reads), ranged kiting. Strictly
optional; biggest risk-to-payoff ratio, schedule last.

---

## Cross-cutting rules

- Determinism: all new state updated on fixed AI cadences from lockstep tick;
  integer/fixed-point accumulators for threat; seeded RNG only; every order
  through CommandRouter with CommandSource.AI; host-only gating preserved.
- Performance budget: IntelSystem and ThreatMap on AI tick (>= 0.5 s), grids
  coarse (625 cells), TacticalQuery hash shared not duplicated. Target < 2 ms
  added per AI tick at 8 factions.
- Tuning: one `AISettingsSO` (+ per-personality blocks) and additions to
  `BorderSettingsSO`; no new hardcoded constants.
- Testing: extend the PhaseNTestSetup harness pattern with AI-vs-AI scenarios;
  metrics logged per match (time-to-scout, intel age at attack, target-choice
  quality, defense response latency).
- Cleanup rolled in: delete `AITuning.cs` legacy constants, wire or delete
  `GetIncomeMultiplier`, retire `AISharedKnowledge` aggregate position once
  FactionIntel lands.

## Suggested milestone order

| Milestone | Contents | Outcome |
|---|---|---|
| M1 | Phase 0 | AI can remember and measure the world (debug overlay proves it) |
| M2 | Phase 1 | Attacks aim at scored targets; border punishes weakness |
| M3 | Phase 2 | Scouts gather the intel the scorer consumes; scout-then-strike |
| M4 | Phase 3 | Personalities differ visibly; AI defends itself; postures shift |
| M5 | Phase 4 | Border pressure scales to the end of the match |
| M6 | Phase 5 | Micro polish if playtests demand it |

M1-M3 form the minimum coherent slice of "full-scale AI with scout mechanics
and target value assessment" and are designed to land in that order.
