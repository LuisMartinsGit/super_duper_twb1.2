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
| Sustained army cap | 200 | 200 | 200 | 200 |
| Expansion (extra GathererHuts near untapped deposits) | off | on | on | on |

**The army cap is NOT a difficulty knob (2026-09-12).** Every tier sustains
up to 200 — the population ceiling — and difficulty is expressed entirely in
the quality and speed of decisions: how often the brain thinks, how stale its
intel is allowed to be, how long before it first attacks, how often waves go
out, whether it counter-composes, raids or stages forward, and how often it
skips an optional build step.

Capping the army instead was doing the same job by simply giving the weaker
tier fewer soldiers, which is the least interesting way to lose. It also made
Easy read as passive rather than as clumsy: a capped AI stops producing and
then stands still, which looks like a broken opponent rather than a beatable
one. A slow, badly-aimed full-sized army is a better teacher and a better
fight than a small well-aimed one.

The shipped assets had drifted far from the old table anyway (55 / 100 / 125 /
150 against a documented 10 / 16 / 24 / 32), so nothing was reading it.

## 3. Personalities (weights, not scripts)

Five personalities (Balanced / Aggressive / Defensive / Economic / Rush),
assigned per faction (lobby-overridable later). Personality scales the
utility weights and thresholds — it does not change code:
attack threshold, military/miner floors, raid cadence, risk tolerance
(target scoring), defense budget. Strategy (the opening build order) and
personality remain separate axes, but personality biases the deterministic
strategy roll (Aggressive → Rush/Balanced openings, Economic → EcoBoom…).

### 3a. The standing army (military floor)

Each personality carries a **military floor**: the standing army the AI keeps
before it considers anything else military. It is multiplied by the PLAN's
army scale, not by difficulty -- difficulty sets the army CAP, the wave base
and how fast the brain thinks, never the floor -- so every tier keeps the same
standing army and differs in how well it uses it.

| Personality | Floor |
|-------------|-------|
| Economic    | 12 |
| TechBoom    | 14 |
| Balanced    | 16 |
| Aggressive  | 20 |
| Rush        | 20 |
| Defensive   | 24 |
| Turtle      | 28 |

**Doubled on 2026-09-12** (operator directive) from 6/7/8/10/10/12/14. The old
floors were set when the army cap was small; with the cap at 200 they left
every faction fielding single figures deep into a match, and the whole muster
chain downstream of them -- wave bar, mission size, reinforcement -- can only
ever divide up an army that was never raised.

**Age 0 still clamps the floor to 8**, and that clamp is NOT doubled. It exists
because Age 0 has exactly one combat unit, so supplies past a garrison buy a
longer identical spear age instead of the age-up that ends it: measured on
Veilmarch, factions held 87-unit spear armies while 42 of 48 never aged up in
30 minutes. Doubling the floor therefore changes Age 1 onward, which is where
an army means something.

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

### 6a. What counts as available, and how big a wave has to be

Two rules that sound like implementation detail and are not — between them
they decided whether any wave ever left home at all.

**Only fighting is busy.** A unit is unavailable to a wave in exactly three
cases: it is already on the mission roster, it is swinging at something
(`AttackCommand`), or the human player gave it an order of their own. Merely
*walking* is available. This has to be written down because the natural
reading — "don't grab a unit that is under a move order" — starves the army:
a mission that times out sends its survivors home with a formation
attack-move, so the very act of releasing an army re-marks every one of its
members as busy for the length of the walk back, and ordinary repositioning
does the same. Observed on Veilmarch, 2026-09-12: Yellow held 137 units and
could not assemble the eight it needed to attack.

**The wave bar is a target, not a doorstep.** A wave launches at
`max(WaveBaseUnits x personality scale, half the faction's desired army)`.
The base value alone is 4-6, which a couple of fresh recruits satisfy, so
the AI spent its army two at a time into defended bases and reset its own
attack timer each time it did. Scaling the bar to the army the faction is
*trying* to keep means a big army waits until it is an army; the base value
survives as the floor so an early rush is still legal, and the existing
overdue-wave release still fires a small wave rather than never attacking.

**And the bar can never exceed what population allows.** It is clamped to a
third of the faction's population ceiling. `DesiredMilitary` is the sustain
army cap times the plan's army scale and reaches 320, while the hard ceiling is
200 -- most of which is workers, support and units already committed. Halving
an impossible number leaves an impossible one: measured on Veilmarch
2026-09-12, Green stood at 200/200 population with an army of 157 and logged
"need 160 idle" indefinitely, so the strongest faction in the match was the one
that stopped attacking. A target the population cap forbids is not a target.

**Past minute 25, an army only leaves home at FULL POPULATION** (operator
directive, 2026-09-12). Before that mark the rules above stand and waves go out
at the scaled bar. After it, a faction must be at its population ceiling --
`pop >= popMax` -- before any wave launches, and the overdue release does not
override it. The AI already drives its own ceiling upward: it builds housing
whenever it is within `populationHeadroomFloor` of the cap and stops at
`FactionPopulation.AbsoluteMax` (200), so "full" settles at a 200-population
faction committing everything it has.

The intent is that the late game is decided by real pushes rather than by a
stream of half-armies feeding a defended base one wave at a time. The cost is
that a faction which cannot fill its cap stops attacking altogether, so the
block is logged by name and count every time it fires -- a silent version of
this rule would be indistinguishable from the passivity bug it replaces.

**A mission that times out poisons its ground.** When an attack mission
expires without its objective falling, the 40 m cell it was aimed at goes on
that faction's blocked list for sixteen minutes and the target scorer skips
every sighting inside it. The alternative is what the AI did before: the
scorer is deterministic, so the same sighting wins again the moment the army
is free, and the wave commutes to unreachable ground for the rest of the
match. This is the third instance of one bug shape in this system -- an
action fails, nothing records the failure, and the next decision repeats it.
The claim planner's `_siteBlocked` is the same rule for build sites.

### 6b. Building spacing yields to being able to fight

The AI keeps roughly 20 m between its buildings so a base stays walkable.
That is a preference, and it now yields: when a placement scan finds no legal
site in its normal passes, it runs one more with the spacing rule dropped.
Nothing else relaxes. Footprint overlap, resource-node clearance, curse
crust, territory ownership, the hall cap and the router's own validator all
still refuse the candidate, so the relaxed pass can produce a cramped base
but never an illegal one.

Why this is not optional. Hollow Table 1v1, 2026-09-12: at minute 16 Red held
24 buildings, 13,166 iron and 11,315 veilstone, and fielded ONE unit. It had
no Barracks and no Archery Range, because it had filled its only territory
with huts and mines, and all 216 candidate sites were refused -- 201 of them
on spacing. Blue, on the other side of the same match, had eight units.
Neither faction ever attacked. This is upstream of every wave rule in 6a: an
army that was never trainable cannot be mustered, however good the muster is.

## 6c. The production snowball

**A full queue is a build order.** When every trainer of a kind has a full
production queue, that line is the bottleneck and the AI must raise another
building of that kind. There is no fixed ceiling on how many: the target for a
line is `max(baseline, standing + 1)` and it ratchets upward for as long as the
queues stay saturated. A faction that can afford to keep twelve Barracks busy
should have twelve Barracks.

This is what makes the match snowball the way a good player's does. More
trainers means more soldiers before the next wave timer, which means bigger
waves, which means more of the map, which pays for more trainers. By the late
game a strong faction is sending wave after wave from scores of production
buildings with full queues, and its training rate exceeds its death rate.

**The only limit is population.** Not resources, and not the AI's willingness
to give orders. Three things previously stood in for a population limit and
each is now removed:

- **A static per-line cap.** The target was `productionBuildingTarget / 4`,
  about five to seven, and nothing could exceed it however saturated the
  queues were.
- **The build crew.** Only `crew` sites may be open at once, and the crew was
  a flat three to five, so a faction with five sites in flight could not start
  a sixth however rich it was. The crew now grows with the work waiting:
  `minerFloor + open sites`, capped at 12.
- **The savings hold.** A production line whose queues are all full is an
  essential purchase and spends past the claim reservation, exactly as housing
  and the first of each line already do.

Measured before this rule, Hollow Table 2026-09-12: Blue held ONE Barracks
with a five-slot queue against a deficit of twenty Spearmen, 20,505 iron and
2,896 supplies in its military budget. Nine times the log recorded five sites
open against a crew of five. Eighteen buildings stood and exactly one of them
trained soldiers. Its army was four units at minute 29.

**Strategy still chooses the ORDER.** Which line saturates first, and which
research is bought before which, remains the military strategy's decision and
may change over the match. The snowball rule only says that a saturated line
gets another building; it never says which line to open first.

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

**The Outrider stands in when the scouts are dead (2026-09-12).**
Scouting must never stop because the scouts died. When a faction has no
living `UnitClass.Scout`, the director drafts light cavalry instead — the
**Outrider** on Alanthor, and on any culture the fastest `human_cavalry` it
owns. Cavalry is the right stand-in for the obvious reason: it is the only
thing on the field that can cross the map at scouting speed, and a horseman
sent to look at something is a recognisable piece of RTS vocabulary rather
than an odd-looking rule.

At most two at a time, never a worker, and never a unit already committed
to a wave. A real Scout takes the job back the moment one exists again, and
the drafted Outrider returns to the army.

Stand-ins do NOT get the Oracle vision bloom: that stays a Scout-class
privilege, so a drafted trooper reveals ground the slow way. It is a worse
scout, which is the point — losing your scouts should hurt without
blinding you.

Why this is not optional. Observed on Veilmarch, 2026-09-12: Yellow's last
scout died around minute 26 and the scout director went silent for the
remaining ninety minutes. Its wave target sat on ground nobody had ever
revealed, the `NO BLIND DISPATCH` gate converted every attack into a recon
request, and nothing was left alive to answer one. An army of 137 units
with 22 live enemy sightings stood still for an hour and a half because
one tile was dark. Scouting is a dependency of attacking, so it needs a
fallback, and the attack gate needs the escape hatch described in §8.

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
