# Game AI (Computer Opponents) — Design

Canonical design for the skirmish/computer-opponent AI, modeled on Age of
Empires IV's shipped architecture — see
[docs/Research/AoE4_AI_Study.md](../Research/AoE4_AI_Study.md) for sources.
Where this doc and code disagree, this doc wins. The Border faction's PvE
AI (`Systems/Border/`) is a separate system and is NOT covered here.

## 1. Architecture (AoE4 three-tier stack)

- **One brain, data-driven flavor.** A single AI engine; difficulty and
  personality are pure data profiles (`AIProfile`), never separate code
  paths — the AoE4/Relic personality-file model.
- **Tiers**: strategic managers (economy, production, military, scouting,
  tech/age) → **missions** (the encounter/task-force layer: objective,
  members, staging, retreat guidance) → per-unit execution via
  `CommandRouter` (never bypassing the command seam).
- **Staggered thinking**: managers tick on their own intervals, offset per
  faction, so multiple AIs never spike one frame.
- **Perception**: fog-of-war honest. Intel comes only from what the
  faction has actually seen (`IntelSystem` sightings + threat maps with
  decay). **The AI never cheats vision or resources on any tier.**

## 2. Difficulty tiers (all fair — the AoE4 lesson)

Difficulty changes *behavior quality only*. No resource or vision cheats
on any shipped tier; if cheat tiers are ever added they must be clearly
labeled with their multiplier (AoE4's hidden-Hardest-cheat backlash).

| Knob | Easy | Normal | Hard | Expert |
|---|---|---|---|---|
| Think interval (s) | 5.0 | 2.0 | 0.5 | 0.25 |
| Worker target (Age 0 → Age 1) | 8 → 12 | 12 → 18 | 16 → 24 | 20 → 30 |
| First attack earliest (s) | 600 | 420 | 300 | 240 |
| Raiding | off | on | on | on |
| Counter-composition | off | off | on | on |
| Optional build-step skip chance | 25% | 10% | 0% | 0% |
| Forward staging before attacks | off | off | on | on |
| Sustained army cap | 10 | 16 | 24 | 32 |
| Expansion (extra GathererHuts near untapped deposits) | off | on | on | on |

## 3. Personalities (weights, not scripts)

Five personalities (Balanced / Aggressive / Defensive / Economic / Rush),
assigned per faction (lobby-overridable later). Personality scales the
utility weights and thresholds — it does not change code:
attack threshold, military/miner floors, raid cadence, risk tolerance
(target scoring), defense budget. Strategy (the opening build order) and
personality remain separate axes, but personality biases the deterministic
strategy roll (Aggressive → Rush/Balanced openings, Economic → EcoBoom…).

## 4. Economy manager

- **Worker target curve** per age per difficulty (table above); workers
  are trained continuously toward the target, not only replaced.
- **Gatherer allocation**: keep the existing iron/veilstone split solver;
  allocation prefers deposits in threat-safe areas (threat-map query).
- ~~**Expansion**~~ *(removed 2026-07-20)*: mined resources credit the
  stockpile directly — there is no drop-off range, so the AI no longer
  plants GathererHuts near far deposits.
- **Population headroom**: build a Hut when projected headroom < 4 (keep
  the existing anti-stall reflex, raised threshold).

## 5. Production manager (utility spend queue)

After the opening build order completes, all spending goes through one
prioritized utility queue re-evaluated every think tick. Requests and
weights (personality-scaled):

1. **Workers** while below the worker target curve.
2. **Military** while below the desired-army-composition vector (see §6).
3. **Age-up** when: choice building complete, treasury ≥ age-up cost +
   reserve, and base not under threat.
4. **Techs** (from the catalog's researchAt buildings) when treasury
   exceeds a comfort reserve.
5. **Buildings**: Barracks if none; second production building when
   income supports parallel queues; Huts per §4.

Never float: if the top request is unaffordable and the treasury exceeds
a float ceiling, take the next affordable request.

## 6. Military manager

- **Desired composition vector**: base mix per age (spear/archer/sword)
  plus, on Hard+, a **counter term** derived from observed (fog-honest)
  enemy composition: enemy melee-heavy → more archers; enemy ranged-heavy
  → more melee; enemy cavalry → more spears.
- **Missions (encounters)**: Attack / Raid / Defend, with member lists,
  scored targets (`TargetScorer` over `EnemySightingRecord`), per-mission
  retreat (strength comparison via `TacticalQuery`), timeout, and
  regroup-home fallback — all existing behavior, kept.
- **Forward staging (better than AoE4)**: on Hard+, attack missions first
  form up at a staging point ~30 m from the target on the home side,
  regroup to full strength, then commit. Cancels into retreat if the
  staging area becomes contested.
- **Formation movement**: mission moves are issued through the formation
  pipeline (`CommandRouter.IssueFormationAttackMove`) so AI armies march
  in formation exactly like player armies (melee front, ranged back,
  slowest-member speed).
- **Postures** (Develop / Pressure / Defend / Rebuild): kept as-is;
  Defend recalls missions and repairs; Rebuild rebuilds the army before
  re-engaging.

## 7. Scouting

Keep the information-driven `ScoutDirectorSystem` (zone staleness scoring,
threat-aware flee, recon-then-strike). This already matches AoE4's
post-Anniversary scout behavior.

**Expanding scout vision (AoM Oracle model).** Scouts have a small LOS
while moving (**18 m**) that, after **1.5 s** stationary, blooms at
**4.5 m/s** up to **55 m**; moving snaps it back instantly. Applies to
`UnitClass.Scout` only, player and AI alike. AI scouts therefore
**perch-and-bloom**: travel to a vantage zone, hold ~12 s while the circle
blooms (the intel pass records everything it reveals), then hop to the
next zone. A perched scout is deliberately vulnerable — that is the
counterplay.

## 7b. Alanthor age-2 ladder & tower doctrine

Once an Alanthor AI reaches Era 2 it builds, in order (one attempt per
think tick): **Temple of Ridan** (hosts chapel plots — the gate for sect
adoption, sect powers and Litharchs) → **Smelter** (veilsteel) →
**Royal Stable** → **Siege Yard** → **Practice Range**. Sect adoption
follows the Fortitude → Renewal → Antiquity → Reclamation priority once
the Temple stands.

**Towers are dual-purpose** — Alanthor's territory claims (each projects
a 15 m build-space circle) AND its static defense:

- Budget by difficulty: Easy 1 / Normal 2 / Hard 4 / Expert 6, active
  from Era 2 (no 5-minute delay).
- Placement preference: **chokepoints** first — walk the approach line
  from the Hall toward the freshest remembered enemy sighting (fog-honest;
  map center before contact), measure corridor width by perpendicular
  passability probes, flank the narrowest corridor under 26 m on its
  clearer side. Otherwise a **directed ring** within ±60° of the threat
  bearing at 25–40 m.
- **Anti-clump**: own towers never closer than 24 m (1.6× the influence
  radius), so their build-space circles tile ground instead of stacking.

## 8. Tech / age / culture

- Age-up is a utility request (§5). Culture choice remains **Alanthor for
  the demo** (only Alanthor has an endgame brain); the per-strategy
  culture table stays in code behind one switch so enabling Runai/Feraldis
  later is a data change. Non-Alanthor cultures MUST NOT be enabled for
  AI until they have endgame behavior.

## 9. Multiplayer contract

AI brains exist on **every client** and must stay **strictly
deterministic**: seeded RNG only (`GameSettings.SpawnSeed` / lockstep
tick), no wall-clock, no unordered container iteration for decisions, all
sim mutations through the same helpers the lockstep layer uses. Host-only
gating is NOT used for the core brain (two legacy systems still gate;
acceptable). Any future nondeterministic feature must instead be routed
host-only through the lockstep command queue.

## 10. Explicit non-goals

- No machine learning (AoE4 shipped without it).
- No vision or resource cheats on any tier.
- No wall-building AI yet (deferred, same as AoE4 in practice).
- No naval/water AI (no naval gameplay yet).
