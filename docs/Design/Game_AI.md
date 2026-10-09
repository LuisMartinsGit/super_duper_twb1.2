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
| Counter-composition strength (`counterResponse`, §5d) | 0.4 | 0.75 | 1.0 | 1.15 |
| Basics share scale (`basicsShareScale`, §5d) | 1.35 | 1.0 | 0.85 | 0.75 |
| Claim squads out at once (`expansionDrive` x `claimMaxParallelSquads`, §5b) | 0.5x | 1x | 1.25x | 1.5x |
| Capital level is a savings goal (`reserveForCapitalLevel`, §5h) | off | off | on | on |
| Economy levels first (`economyUpgradesPerThink`, §5h) | none | none | some | most |
| Production queue depth (`productionQueueDepth`, §5h) | uncapped | uncapped | shallow | current unit only |
| Fortress savings goal (`fortressReserve*`, §5c) | always | always | always | always |
| Units before economy (`unitsBeforeEconomy`, §5h) | off | off | on | on |
| The army never pays for a Fortress (`armyBeforeSaves`, §5f) | off | off | on | on |
| Capital savings hold capped (`capitalReserveMaxHoldSeconds`, `capitalReserveRestSeconds`, §5h) | n/a | n/a | capped | capped |
| Home production per line (`homeProductionPerLine`, §5g) | below Normal | the home floor | the home floor | the home floor |
| Production per province (`provinceProductionPerTerritory`, §5g) | one | one | one | one |
| Extra production: busy share and how long (`productionSaturationThreshold`, `productionSaturationSeconds`, §5g) | stricter, longer | baseline | looser, shorter | loosest, shortest |
| Standing army a wave never drafts (`standingArmyFloorFraction`, §6a) | none | small | small | small |
| Wave minimum as a share of the standing army (`waveMinArmyFraction`, §6a) | none | small | small | small |
| Age-up savings protected from the Age 0 wave (`protectAgeUpSavings`, §5h) | off | off | off | off |
| Curse hunt waits for the capital (`curseHuntMinCapitalLevel`, §5h) | no | no | aged up | aged up |
| Vault share / withdraw on need (`vaultDepositShare`, `vaultWithdrawOnNeed`, §5h) | small / timer only | half / yes | most / yes | most / yes |
| Optional build-step skip chance | 25% | 10% | 0% | 0% |
| Forward staging before attacks | off | off | on | on |
| Sustained army cap (`sustainArmyCap`, a hard ceiling) | 60 | 120 | 180 | 200 |
| Expansion (extra GathererHuts near untapped deposits) | off | on | on | on |
| Counter-aware target choice / focus-fire weight (`tactics`, §6e) | off / off | 0.5 / 0.6 | 0.85 / 0.85 | 1 / 1 |
| Ranged hold a line behind the melee (§6e) | off | on | on | on |
| Kiting (§6e) | off | off | on | on |
| Flanking share of the fast melee (§6e) | 0 | 0 | 20% | 30% |
| Fall back to regroup at enemy/own power (re-engage at) (§6e) | never | 1.8 (0.8) | 1.5 (0.9) | 1.35 (0.95) |
| Casts unit abilities; AoE needs N enemies (§6e) | no; 2 | yes; 3 | yes; 4 | yes; 4 |

| Claim / develop / extractor cadence (`territoryCadenceScale`, §5b, §5g) | slowest | slow | baseline | fastest |
| Fortress after a territory qualifies (`fortressDelaySeconds`, §5c) | late | later than Hard | soon | at once |
| Retakes rival ground (`reconquestMargin`, §5b) | never | at a wide margin | yes | at a narrow margin |
| Late strength-gated wave needs (`strengthWaveRatioScale`, §6a) | the biggest edge | a bigger edge | baseline | a smaller edge |
| How much of its personality it plays (`personalityWeight`, §3) | all of it | most | some | a little |
| Armies per wave (`concurrentArmies`, §6f) | one | one | two | three |
| Waves aim at the enemy's income (`incomeTargeting`, §6f) | no | no | yes | yes |

(The values are the four profile assets'; the table says which way each knob leans.)

**Pace is the ladder (2026-10-05, Mirror Marches v3-v4).** With the
holding-back knobs flattened, Easy stopped winning but kept placing second:
on this map the whole economy is territory, and taking, developing and
fortifying territory ran at one cadence for every tier, so Easy claimed as
fast as Expert and built eight Fortresses to its one. The tiers now differ
in exactly those paces — how often a claim round goes out, how often a
territory's next building is placed and an extractor walk runs, how long a
qualified territory waits for its Fortress, whether and at what margin the
tier retakes ground, and how big an edge the late wave waits for. Easy is
slow and never retakes; Expert is fastest and fortifies at once. Nothing
here is a cheat: every tier plays the same rules at a different speed.

**The holding-back ladder was inverted (2026-10-05, Mirror Marches
M1-M6).** On a map with every seat on identical ground, Expert finished first
in none of five matches and Easy won two; Expert's first wave went out at
13-18 minutes against a 180 s earliest, it built one Fortress to Easy's eight,
and 60% of its trainers stood idle. The ladder had been stacking restraint on
the harder tier: a standing floor of 40% of the army, a wave minimum of a
quarter of the rest, the Age 0 strike switched off, twice the trainers with a
one-deep queue, and the Fortress "waited for the bank" that a 0.25 s think
never let fill. Those knobs now lean the other way or are flat: the floor
and wave minimum are small on every tier above Easy, no tier protects its
age-up savings from the opening strike, trainers start at the home floor and
one per province and grow only by saturation (§ 5g), the Fortress is always
saved for (§ 5c), and Expert's ultimates are no longer spent at a 1.15
disadvantage. What still separates the tiers is reaction speed, claim
parallelism, economy levels, tactics (§ 6e) and the in-fight skills — not
how much of its army a tier keeps at home.

**More trainers, not a smaller army (2026-10-05).** Developer: "Having less
time between attacks in higher difficulties should not come at the cost of
army count. Harder AIs should have more parallel military training facilities
to make up for this." Measured in Headless33-35 (same maps and seeds): Normal
stood with a larger army than Expert at every checkpoint, Expert had no more
production buildings, and it spent markedly less on units, because the
economy drive took the money and the short wave interval kept drafting
everything idle into small waves. Expert's tactics still won its fights. So a
harder tier now (a) builds more production in parallel (the home floor per
line, production per province, and an earlier saturation gate for extras,
§ 5g), (b) keeps a standing army at home that waves and reinforcements never
draft, so faster waves draw only the surplus, and makes each wave a real share
of the army (§ 6a), and (c) spends on the economy drive only once the money
can no longer become units (§ 5h). Normal's values are what shipped before;
Easy sits below Normal. The numbers are in the four profile assets.

**Decision rate is not difficulty (2026-10-04).** The think interval stays
(it is how often the brain re-plans its economy and dispatch), but it was never
why an AI lost fights. What separates the tiers now is what an army does IN a
fight — the `tactics` block on each profile asset (§6e): whether it picks
targets by counter relationship, kites, flanks, falls back to regroup, keeps its
shooters behind its melee, and how patient its abilities are. Micro runs on its
own fixed cadence for every tier (`AITactics.asset`), so a slower-thinking tier
is not a slower-reacting army; Easy simply has the skills switched off.

**The army cap IS a difficulty knob (2026-10-05, operator directive;
supersedes the 2026-09-12 rule below).** Expert plays its saves — the
Fortress pot, the capital level, trade — and that is what makes it Expert;
Easy saves for nothing and simply trains. With every tier allowed the same
200 soldiers, the tier that never saved out-produced the one that did: Easy
beat Expert in both seats of a v9 1v1 (34.5k to 14.8k and 28.7k to 16.1k)
and finished ahead of it in half of fourteen matches. Decision quality
cannot win a match against three times the army, so the ladder caps what
each tier may keep: Easy 60, Normal 120, Hard 180, Expert 200 (the
population ceiling, so unrestricted). `sustainArmyCap` is a HARD ceiling on
the desired army — the plan's `ArmyScale` shapes the target under it (a
Mass plan at Easy still stops at 60), the per-territory keep-up (§3a) is
clamped to it, the escalator never passes it, and every think ends by
clamping the target back under it — the wave strength gate, the curse hunt
and the per-unit escalator all used to raise it past the cap (Easy read 83
against a cap of 60 in the first v16 pair). Only Easy's cap is a real
restriction in play; the middle steps are there so the order holds.

*Historical, superseded:* **The army cap is NOT a difficulty knob
(2026-09-12).** Every tier sustained up to 200 — the population ceiling —
and difficulty was expressed entirely in the quality and speed of
decisions: how often the brain thinks, how stale its intel is allowed to
be, how long before it first attacks, how often waves go out, whether it
counter-composes, raids or stages forward, and how often it skips an
optional build step. Capping the army was judged "the least interesting way
to lose"; the measurement above showed it is also the only way the tiers
finish in order.

The shipped assets had drifted far from the old table anyway (55 / 100 / 125 /
150 against a documented 10 / 16 / 24 / 32), so nothing was reading it.

## 3. Personalities (weights, not scripts)

**Dampened by tier (2026-10-05, approved).** A personality pulled every tier
by the same amount, and in mixed matches the pull outweighed the tier: an
Expert that drew Defensive or Economic played a slow-army plan and finished
third behind a Hard Rush. Each tier now plays its personality at the
profile's `personalityWeight` — every numeric value of the block, the plan
affinities and the army mix are blended from Balanced toward the personality
by that weight (a flag takes the personality's value from one half). Easy
plays its flavour in full; Expert takes a fraction, so flavour stays visible
and a higher tier never loses to a lower one for having drawn it.

Seven personalities: **Balanced / Aggressive / Defensive / Economic / Rush /
TechBoom / Turtle**. The lobby's strategy dropdown picks one per AI slot
(`AIBootstrap.LobbyToPersonality`; Aggressive has no dropdown entry), RANDOM
falls back to the colour table (Red / Orange Rush, Yellow TechBoom, Green
Economic, Blue Turtle, White Aggressive) and then to a seeded roll
(`AIBootstrap.ResolvePersonality`), and the AI keeps it for the whole match. **A personality is one row of numbers,
nothing else** -- `AISettingsSO.PersonalityBlock`, authored on
`Assets/Resources/AISettings.asset`, which is the single source of every
value in this section. There is no coded default table and no per-personality
script: the scripted Age 0 build orders (`AIBuildOrder` step lists) were
deleted on 2026-10-05, because their step pointer never advanced and no step
of any order was ever issued -- every opening the AI ever played was the
maintenance loop below, steered by these numbers. Every field is a how-much,
never a which: no unit id may ever appear on the row (unit choice is layer 3,
§ 5d).

What the row decides, and where each field is read:

| Field | What it steers | Read by |
|-------|----------------|---------|
| `boomAffinity` / `massAffinity` / `rushAffinity` / `techAffinity` / `fortressAffinity` | **Plan affinity** -- a score bonus on each strategic plan when a plan is chosen (§ 5, `AIPlans.Affinity`). The board is read the same way for everybody; this is the only thing that keeps four AIs on one board from reaching one answer, and it is sized so an ambiguous board splits them while a decisive one (deathball, base under attack) still collapses them onto the right plan | `SimpleAISystem.Plan` |
| `militaryFloor` | **Standing army** (§ 3a) -- multiplied by the plan's `ArmyScale`, never by difficulty | `Economy`, `Goals` |
| `attackThreshold` | Idle units before the brain flips to Pressure posture | `Posture` |
| `riskMultiplier` | **Risk** -- multiplies the risk term of target scoring and the wave's strength gate; above 1 is cautious, below 1 takes the fight | `Targeting`, `Military` |
| `raidingEnabled` | **Raiding** -- whether a wave launch also peels a fast raid party at the enemy economy, and whether the independent raids of `raidIntervalSeconds` may go | `Military` |
| `gathererHutTarget` | **Hut cap** -- the early-game Gatherer's Hut ceiling; doubles over the match for gathering cultures, Feraldis (Raider Camps) stays hard-capped at the smaller of it and `feraldisRaiderCampCap` | `Economy` |
| `productionBuildingTarget` | Production buildings per line (`/2` in Age 0, `/4` after age-up, min 2) | `Goals` |
| `ageUpPushSeconds` | **Age-up push** -- game time after which the AI stops founding huts, treats the age-up as its advancement gate and banks for it (Aggressive / Rush first push their one Age 0 wave) | `SimpleAISystem`, `Goals`, `Economy` |
| `basicsAppetite` | **Army mix** -- multiplier on the cheap basics' share of the army plan (§ 5d); the role mix itself is `RoleBudget.For` in `AIComposition.cs`, still code | `Composition` |
| `towerCoverageScale` | **Towers** -- multiplier on how much ground the personality wants covered by watch towers (1 = Balanced): the per-province tower cap and coverage, and the Alanthor endgame's tower budget per tier | `TowerCoverage`, `AIAlanthorEndgameSystem.Towers` |
| `wallPriorityScale` | **Walls** -- below 1 the wall doctrine waits until the army stands at that share of its target (a drawn home ring is exempt, § 6h); 1 and above it holds only while the army is short | `AIAlanthorEndgameSystem.Walls` |
| `wallsEnabled` | **No walls at all** when off (Rush): no plan, no ring corridor kept free -- *structural* | `AIAlanthorEndgameSystem`, `AIWallCorridor` |
| `homeRingScale` | **Ring width** -- multiplier on the drawn main-camp ring's hub offsets (§ 3b) -- *structural* | `AIBaseTemplate.HubPositions` |
| `frontierWallTerritories` | **Frontier walls** -- how many held territories bordering hostile ground are walled besides the home (§ 3b) -- *structural* | `AIWallPlanner.CollectWallTerritories` |
| `wallGuardShare` | **Wall guard** -- share of the idle standing army posted at the home ring's gates (§ 3b) | `Posture` (`TickWallGuard`) |
| `raidIntervalSeconds` / `raidPartyScale` | **Independent raids** -- seconds between raid parties sent at the enemy's extractors on their own timer (0 = raids only split off a wave), and the multiplier on the party size (§ 3b); the interval is *structural* | `Military` (`TickRaids`) |
| `strengthGateFromSeconds` / `noOverdueRelease` | **Only fights it can win** -- the time from which every wave must pass the strength test (the earlier of it and `strengthWaveAfterSeconds`), and whether an overdue wave is ever released past the assessment or the Defend veto (§ 3b) -- *structural* | `Military` |
| `fortressAppetite` | **Fortress spread** -- multiplier on the Fortress ceiling, pace and rival-border weight (§ 3b, § 5c) | `Expansion` (`EnsureFortressExpansion`) |

A *structural* field is read from the personality's row as authored at every
tier (`For(p, weight)` copies it unblended): whether a Rush walls, or how wide
a Turtle's ring is, is the flavour itself, not a strength that dampening
should soften. Every other field blends toward Balanced as above.

Workers are NOT on the row: the worker target is the one rule in § 4
(`economyWorkerFloor` + one per conquered territory, `SimpleAISystem.asset`),
the same for every personality. Culture is only leaned, not chosen:
`AIBuildOrder.CultureLeanFor` gives the prior (Rush and Balanced toward
Feraldis, Defensive and Turtle toward Alanthor) that `AICultureChoice` bends
with scouted intel.

How the seven read, from the values on the asset (relative, not restated --
open the asset for the numbers):

- **Balanced** -- the reference row: every scale 1, a mild lean to Mass and
  Tech, raids, mid floor and hut cap.
- **Aggressive** -- strongest Rush and Mass affinity after Rush itself, low
  risk aversion, raids, a higher floor and more production buildings, fewer
  huts, pushes the age-up late (after its wave), light on towers and walls.
- **Defensive** -- Fortress first, Tech second; cautious targeting, no raids,
  the biggest floor and attack threshold in the table; **attacks only when it
  is sure to win**: every wave passes the strength test from the first minute
  and is never released as overdue; **spreads towers and Fortresses** -- the
  most tower coverage, and the largest Fortress appetite (more of them,
  sooner, toward its rivals). Walls after Turtle.
- **Economic** -- Boom first, Tech second; the smallest floor and the highest
  hut cap, pushes the age-up early, no raids, slightly under par on
  fortification.
- **Rush** -- the strongest single affinity in the table (Rush), the lowest
  risk aversion, the fewest huts and the most production buildings, pushes
  the age-up last, builds almost no towers and **no walls at all**; **harasses
  the enemy economy**: after its age-up it sends larger raid parties at
  extractors on their own timer, between waves as well as with them.
- **TechBoom** -- Tech first with a little Boom; the earliest age-up push,
  the lowest basics appetite (its veilstone goes into role units), no raids,
  par fortification.
- **Turtle** -- Fortress with a little Mass; the highest risk aversion, a big
  floor, no raids, the most walls; **a wider home ring** that fences more
  building ground, **walls along its frontier** territories against
  invaders, and **defends its walls**: part of its idle army stands at the
  ring's gates.

### 3a. The standing army (military floor)

Each personality carries a **military floor**: the standing army the AI keeps
before it considers anything else military. It is multiplied by the PLAN's
army scale, not by difficulty -- difficulty sets the army CAP, the wave base
and how fast the brain thinks, never the floor -- so every tier keeps the same
standing army and differs in how well it uses it. The floors run Economic <
TechBoom < Balanced < Aggressive = Rush < Turtle < Defensive (Defensive
overtook Turtle on 2026-10-07, § 3b: it builds a large army before it
attacks); the values are `militaryFloor` on `AISettings.asset`.

**Doubled on 2026-09-12** (operator directive). The old floors were set when
the army cap was small; with the cap at 200 they left every faction fielding
single figures deep into a match, and the whole muster chain downstream of
them -- wave bar, mission size, reinforcement -- can only ever divide up an
army that was never raised.

**Age 0 still clamps the floor to 8**, and that clamp is NOT doubled. It exists
because Age 0 has exactly one combat unit, so supplies past a garrison buy a
longer identical spear age instead of the age-up that ends it: measured on
Veilmarch, factions held 87-unit spear armies while 42 of 48 never aged up in
30 minutes. Doubling the floor therefore changes Age 1 onward, which is where
an army means something.

**The army keeps up with the ground (2026-10-05).** The desired army used
to grow by one per unit bought, and the escalator stopped under any
savings hold, so a tier that saves (the Fortress pot, the capital level)
froze its target near the opening floor and spent every surplus on the
economy — Expert lost to Easy in both seats of a 1v1 with a third of its
unit spend. After the age-up the target is at least `armyPerTerritory` per
territory held (capped at the plan's army cap), whatever the saves; below
`armyEssentialFraction` of that target a combat unit passes the ordinary
holds (§ 5f).

### 3b. Personality doctrines: walls, raids, caution, and workers as targets (2026-10-07)

**Developer directives:** "Turtle: a wider wall around the main base, so more
buildings fit inside; builds walls along its territories to stop invaders;
defends its walls with the army. Rush: ditches walls completely; focuses on
harassing enemy economic buildings. Defensive: builds a large army and only
attacks when it is sure it can win; spreads towers and Fortresses. Armies
treat enemy workers as high-value targets."

Every number below is a field on the personality row (`AISettings.asset`,
table in § 3) or on `SimpleAISystem.asset` / `AITactics.asset` /
`AIBaseTemplate.asset`; none is restated here.

1. **The wider ring (Turtle, `homeRingScale`).** The drawn main-camp ring
   (§ 6g) is drawn with every hub's offset from the Fortress multiplied by
   the scale. A hub that would leave the home territory, or stand within the
   wall inset of its border, is pulled back toward its drawn spot (never
   inside it). A ring drawn wider gets an extra hub on every link longer than
   `ringMaxLinkMeters`, so the wall doctrine's link radius still spans every
   link. The building slots keep the drawn town's shape; the extra ground
   inside the ring is the ordinary site search's (§ 6b), which keeps base
   buildings inside the ring.
2. **Frontier walls (Turtle, `frontierWallTerritories`).** Besides the home
   ring, up to that many held territories are walled: claimed,
   Fortress-connected and bordering a hostile faction's or the curse's
   ground — those with the faction's own Fortress first, then those next to
   home. Each is traced like the old border wall, but only along stretches
   that face ground the faction does not hold; the border with its own
   territories stays open, so the wall never fences in its own army. The
   picks are kept while held, so the plan is redrawn only when one is lost or
   a slot opens. This is the one exception to "secondary bases are not
   walled" (§ 6g rule 3), and only for a personality that asks for it. The
   doctrine's hub cap counts per walled territory.
3. **The wall guard (Turtle, `wallGuardShare`).** Every
   `wallGuardIntervalSeconds` that share of the idle standing army (not on a
   mission, a claim or an economy response) is posted
   `wallGuardInsetMeters` inside the home ring's built gates, round-robin by
   entity index; a unit within `wallGuardArriveMeters` of its post is left
   alone. Posted units attack-move, so they meet an attacker at the wall, and
   stay draftable by the next wave. Not in Defend posture (the defence owns
   the army then).
4. **No walls (Rush, `wallsEnabled` off).** No wall plan, no hubs, no gates,
   and no ring corridor kept free in the base layout.
5. **Raids on their own timer (Rush, `raidIntervalSeconds`,
   `raidPartyScale`).** After its age-up, every interval a raiding
   personality sends a party (`raidPartySize` x the scale; also the size of
   the raid split at wave launch) of its fastest idle units above the
   standing floor at the best-ranked income target (§ 6f: extractors first,
   then houses; never military buildings; the war's victim only while a war
   is on, § 6i) that the party can take (AIEngagement assessment), with at
   most `raidMaxConcurrent` raids out. A raid whose objective falls moves on
   to the next extractor within `raidChainRadius` before it heads for safe
   ground. Not in Defend posture.
6. **Only fights it can win (Defensive, `strengthGateFromSeconds`,
   `noOverdueRelease`).** The wave strength test (§ 6a) applies from the
   personality's time instead of only past `strengthWaveAfterSeconds`; before
   that mark the head-count bar still applies as well, so it attacks with a
   big army that can win. Its waves are never overdue: no release past the
   assessment, none past the Defend veto. A held wave raises the desired army
   toward what the defence demands, up to the tier's cap.
7. **Fortresses spread (Defensive, `fortressAppetite`).** The Fortress
   ceiling is multiplied by it, the check interval and the tier's
   `fortressDelaySeconds` divided by it, and the bonus for ground bordering a
   rival or the curse multiplied by it (§ 5c). Towers spread through the
   higher `towerCoverageScale` (§ 3 table).
8. **Workers are high-value targets (every personality).** In a fight the
   tactics layer's army scoring and `AIEngagement.PickPriorityTarget` add
   `workerTargetBonus` (`AITactics.asset`) to an enemy worker — flat, at
   every tier, not scaled by the tier's focus-fire skill. In income targeting
   (§ 6f) an extractor scores `incomeWorkerBonus` more per enemy worker of its
   owner seen within `incomeWorkerRadius` in the last
   `incomeWorkerMaxAgeSeconds` (at most `incomeWorkerMaxCounted`). The
   shared `TargetingSystem` is unchanged: this is the AI's choice, not a
   rule of the simulation.

Logs: `RAID: n fast unit(s) at <faction>'s <building> …`, `RAID: objective
down — on to …`, `WALL GUARD: n unit(s) to the ring's k gate(s) …`, the wall
plan line's `territory N: border trace (frontier): …`.

## 4. Economy manager

- **The worker rule** (2026-10-03, operator directive): **3 workers for
  the home territory + 1 for every territory conquered**, the same for every
  culture, age and difficulty (`economyWorkerFloor` 3 +
  `workersPerConqueredTerritory` 1 in `SimpleAISystem.asset`). It is the
  only worker target: the worker floor, the goal list's build crew and the
  maintenance loop all read `WorkerFloorFor`, and it is assigned on every
  pass rather than ratcheted, so losing a territory lowers it. It replaces
  the per-age, per-difficulty worker curve.
- **Gatherer allocation**: keep the existing iron/veilstone split solver;
  allocation prefers deposits in threat-safe areas (threat-map query).
- ~~**Expansion**~~ *(removed 2026-07-20)*: mined resources credit the
  stockpile directly — there is no drop-off range, so the AI no longer
  plants GathererHuts near far deposits.
- **The opening is Gatherer's Huts** (2026-10-03). Until the faction owns
  `hutPipelineFreeCount` huts (4, never above the difficulty's hut cap), it
  raises them straight from the bank, as many as it can afford in one
  think, each on the free supply node nearest its Hall. While any are still
  missing in Age 0, a savings reserve (`OpeningHuts`) holds research, army
  growth, ore extractors, scouts, other non-essential buildings, workers
  past the worker rule, and every house that is not nearly blocking
  (headroom `openingHutHousingHeadroom` 2 instead of 8). It saves for ALL
  the missing opening huts, and for the first `openingHutGraceSeconds` (45)
  it is armed before any node has been seen. The reserve is not strict, so
  it follows the usual hold/release duty cycle, and it
  releases as soon as there is no free supply node. An extractor requested
  without an explicit site is always placed on a free node of its kind,
  never in the ring around the Hall.
- **The first Religion Point is hunted** (2026-10-03). The Temple costs 1 RP,
  and before a Temple exists only the curse pays RP: kills pay points, and a
  destroyed curse node pays a whole RP (Religion.md). While a faction has no
  Temple and no RP, from `religionHuntEarliestSeconds` (60) it picks the
  nearest curse node it has seen, at any distance. It compares its free army
  with what stands within `religionHuntAssessRadius` (40 m) of the node,
  using the attack waves' assessment (`AIEngagement.AssessAssault`). If it
  wins, it attacks with all of its free army, and units freed in the next
  `religionHuntReinforceSeconds` (90) join that attack. The hunt's power is
  its **roster** — every unit sent, wherever it stands. It used to count only
  units inside the node's radius, so a hunt still on the road read "losing
  (0+0)" five seconds after launch. A hunt whose roster plus newcomers falls
  below `religionHuntCallOffMargin` of the curse estimate is called off, and
  its hunters are **walked home**. Before this, a called-off hunt left them
  attack-moving into the node to die. The node is always judged by the
  **curse estimate** (§ 5i rule 2), never the bare reading. If not, it raises
  its army target and trains toward what the node needs. With no curse
  node seen yet it waits for the scouts. Logged as `RELIGION`.
- **The reclaim squad never feeds a losing fight, and the age-up comes first**
  (2026-10-04). The squad that answers curse nodes near the base (curse at
  the doorstep, veilstone poverty, or short of Religion Points) goes only when
  it, plus our units already fighting there, wins against what stands within
  `reclaimAssessRadius` of the node (`AIEngagement`'s commit ratio); otherwise
  it waits (`RECLAIM: held back …`). While an Age 0 faction is saving for its
  age-up landmark it answers only curse at its doorstep. Why: the 60-minute
  batch's age-up stragglers (14, 16, 17, 23 minutes, one never) each lost
  65-112 units to the curse before minute 15 — every freshly trained Spearman
  walked alone onto a guarded node — and the supplies that should have bought
  the landmark went into replacements (banks of 1,000+ iron and veilstone,
  under 200 supplies).
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

### 5a. Veilstone is the Alanthor army's bottleneck (2026-10-03)

Alanthor never mine veilstone; they buy it at Trading Outposts, up to four
beside each uncursed outcrop in held ground, one per side
(docs/Design/Veilstone_Economy.md §3.1), and
every soldier past the Spearman and the Archer costs it. When a military
purchase is refused, the brain records WHICH resource the bank lacked
(`AIBudget.NoteMilitaryShort`, held `militaryShortHoldSeconds`) and acts on
it instead of re-asking:

- ~~the composition picker steps each line down its ladder to the basics~~
  **superseded for an aged-up Alanthor army by § 5d** (the step-down filled
  300 population with Spearmen and Archers): the army now SAVES for its role
  unit once the basics are at their cap. The pass-over memory
  (`unaffordableUnitCooldownSeconds`) still serves Age 0 and the other cultures;
- while the army is short of veilstone every Trading Outpost BUYS (no forging;
  selling veilsteel only while supplies or iron cannot fund a Buy cycle);
- a free outcrop in held ground the bank cannot yet pay an Outpost for becomes
  a savings goal (AIPivotalReserve), so walls and houses hold until it stands;
- the claim picker scores each uncursed outcrop `claimVeilstoneNodeBonus` extra
  (and goes after curse-held outcrops — § Veilstone-driven conquest below).

**The glut rule — the Outposts stop buying past what the army needs**
(2026-10-05). Buying veilstone spends supplies and iron. An Expert Blue
held 4,900 veilstone and 14,000 iron at 27 minutes, with 409 supplies and
an army of 29. Over the match its four Outposts put more supplies into
trade than its whole army cost. So the Outposts BUY only while the bank
holds less veilstone than the army plan needs. The need is the plan's
veilstone spend a minute at full production (each role's share, its SO price
and training time, times the military trainers) for
`outpostVeilstoneNeedMinutes`, never below `outpostVeilstoneNeedFloor`.
Above the need:
- they **forge** the surplus into veilsteel. One of them **sells** veilsteel
  for supplies and iron once Veilsteel Export is researched. Without the
  sale, forging runs only up to `outpostVeilsteelTarget`;
- any post with nothing to do is set to **Hold** (Veilstone_Economy.md §3.1);
- the Outpost researches Veilsteel Forging, then Veilsteel Export. Forge then
  Sell is the way back from veilstone to supplies.

Buying resumes when veilstone falls below `outpostBuyResumeFraction` of the
need. It also resumes at once when the army is short of veilstone (the rule
above). The composition reacts too. While veilstone (the glut) or iron
(`glutIronAbove`) piles up and the army is short of supplies, every role's
share is tilted by `glutCompositionTilt` toward units paid mostly in iron and
veilstone. The basics cap and the role locks still apply. Logged as `TRADE:
outposts buy off (veilstone v > need n)`, `TRADE: outposts buy off: s sell /
f forge / h hold (...)`, `TRADE: outposts buy on (...)`, and `| glut tilt` on
the composition line.

Numbers: `SimpleAISystem.asset`, `AIBudget.asset`.

**Supplies outrank a veilstone purchase (2026-10-05).** Buying veilstone
trades supplies for it. While the army's refused purchases are short of
SUPPLIES and not of veilstone, the Outposts hold or forge rather than buy —
and the four-minute need is capped by `outpostVeilstoneNeedMax`, because with
twenty-nine Outposts it read sixteen thousand and Expert bought at its whole
supply income for twenty minutes with twelve thousand banked.

### 5b. Defend-based expansion and veilstone-driven conquest (2026-10-03, rewritten 2026-10-04)

**There is no territory cap** (Territory_Claims.md §10, 2026-10-04: "granted
by how much you can defend, not a hard cap"). Territory is the economy — every
held slot pays and every extractor on it pays its ladder — so the AI RACES for
ground and stops only when it can no longer defend what it holds. The 60-minute
batch before this rule left maps 30-40 minutes from full: one claim squad at a
time per faction, about eight launched a minute across 27 factions, and the
then-limit refusing 504 claims.

- **Claims open at age-up.** In Age 0 the AI claims nothing. Once its age-up
  landmark stands (the age-up is a build timer away) it PRE-STAGES claim
  squads on its best targets, so their meters start filling the moment the
  era turns; a staged squad's clock starts at age-up, and one waiting longer
  than `claimPrestageMaxSeconds` (the landmark stalled) is released.
- **Every free claimable territory, in parallel.** Each claim round
  (`claimAttemptInterval`) ranks every candidate and staffs as many as it may:
  up to `claimMaxParallelSquads` squads out at once, times the difficulty's
  `expansionDrive` and the plan's claim appetite. Best first: nearest, then
  richest — every known node scores `claimNodeBonus`, a supply node
  `claimSupplyNodeBonus` more, an uncursed outcrop `claimOutcropBonus` more
  (and more again while veilstone is the wall, below). Unseen neighbours
  follow as exploring candidates (one at a time).
- **Sized to the known threat.** A squad takes the idle soldiers NEAREST its
  target, at least `claimSquadMinSize`, adding more until its power
  (AIEngagement scale) reaches the hostile power the faction KNOWS at the
  target (mobile + static within `claimThreatRadius`) times
  `claimThreatMargin`, at most `claimSquadMaxSize`. Unseen ground counts as
  empty. If what the army can spare is too weak, that target is held back for
  `claimHeldBackSeconds` and the round moves to the next. Claim squads hold at
  most `claimArmyShare` of the army; the rest stays free for defence and waves.
- **Claims outrank waves; home outranks claims.** The claim round runs before
  the attack waves draft, so claims take the idle army first, and while a
  round found no idle soldiers for open ground (within
  `claimWaveYieldWindowSeconds`) the waves yield — for at most
  `claimWaveYieldMaxSeconds` in a row, then one wave goes anyway — and the
  yield is then SPENT for a wave interval (2026-10-05): the attempt that
  followed the bound used to fail on the idle count (the claim squads hold
  the soldiers) and the claims re-armed another full yield twenty seconds
  later, so Expert, the tier that claims most, held its first wave until
  minute 15 while Hard attacked at 11. While the
  posture is Defend (a threat at home) no new claim leaves; squads already out
  stay.
- **Stop when stretched, consolidate.** Before each round the AI compares its
  whole army power with the territories it holds or is claiming: below
  `expandPowerPerTerritoryFloor` (divided by the plan's claim appetite) per
  territory it is STRETCHED and claims nothing; a round may only add as many
  territories as that floor still covers. The test applies from
  `expandStretchMinTerritories` held. A claimed territory LOST (decayed,
  drained or collapsed) makes it consolidate for
  `expandLossConsolidateSeconds`. While consolidating the Fortress picker
  SAVES for a Fortress (§ 5c) — a Fortress locks the ground the army cannot
  garrison — but a Fortress never gates claiming.
- **A squad holds until the ground is locked.** It stands on the territory
  until the meter claims it, then waits up to `claimHoldForLockSeconds` for an
  extractor or a Fortress to lock it (unlocked, unbuilt ground decays the
  moment it leaves); it is released when locked, wiped out, timed out
  (`claimSquadTimeoutSeconds` unclaimed) or locked by another side, and
  unclaimed failures blacklist the ground for a while.
- **Adjacency is the claim system's own.** A candidate must border held
  ground LINKED to one of the faction's Fortresses — the exact test
  TerritoryClaimSystem.MayTake applies, read from its per-tick link table.
  Cut-off ground is no base to grow from, and there is no plain-adjacency
  fallback (it sent squads to ground the meter refused). A new Fortress
  re-links cut-off ground, and its neighbours become candidates on the next
  tick.

**Log:** `CLAIM: parallel N squad(s) -> A (5, 1 outcrop(s)), B (3) | k out, c
candidate(s)`, `CLAIM: pre-staged N squad(s) for the age-up -> …`, `CLAIM: X
held back: the n soldiers the army can spare have power p vs known threat t`,
`CLAIM: no claim: <why>` (throttled by `claimLogInterval`), `EXPAND:
stretched (power/territory x < y, …) — consolidating`, `EXPAND: territory
lost: X`, `EXPAND: lost X Ns ago — consolidating for Ms`. Numbers:
`SimpleAISystem.asset`, the difficulty profiles (`expansionDrive`).

**Veilstone decides the target.** While an Alanthor army is short of
veilstone (`AIBudget.IsMilitaryShort`), each known outcrop in a candidate is
worth `claimVeilstoneNodeBonus` more on top of its node and outcrop bonus, which outweighs
the distance spread between neighbouring territories and any number of supply
or iron nodes: the next territory taken is the one with the most veilstone.

**Curse-held veilstone is a target.** Curse nodes lock their territory, so
the claim picker used to skip it outright, and nothing else in the brain went
after it — the first-RP hunt stops once a Temple stands, and the reclaim
squad only answers curse within 110 m of the capital. Now, while the army is
short of veilstone, a curse-held, locked territory with known outcrops (the
cursed Veilstone-rich centre, any ground the curse spread onto) is a
candidate: every outcrop counts (its node's fall pacifies it), less
`claimCurseTargetPenalty` for the garrison. Picking one launches a **curse
assault** instead of a claim squad (one at a time): every free soldier up to
`claimCurseSquadMax`, and only if AIEngagement judges that army the winner
within `claimCurseAssessRadius` of the first node (too weak: logged, the
ground skipped for a while, the runner-up tried). The assault walks from node
to node; when the last one in the territory falls it becomes an ordinary
claim on the same ground, clock restarted (`claimCurseTimeoutSeconds` bounds
the assault itself). Members are claim-squad members, so waves never draft
them mid-fight.

**The idle army clears the curse (2026-10-05).** Curse-held ground was a
candidate only while an Alanthor army was short of veilstone, so a faction
whose every neighbour was curse-held logged "no claimable territory next to
Fortress-linked ground" and stood its whole army at home (Yellow, 193 units,
SunderedCrown). Now, after the age-up, when the army has **no wave out, no
economy response and no Defend**, and holds at least `curseClearMinUnits`
idle soldiers **above its standing floor**, the surplus marches on the
**weakest** curse-held territory bordering its Fortress-linked ground —
weakest by the curse's power (AIEngagement, mobile army and static
defences) around the known node nearest home, nearest breaking ties. It goes
only when it beats that power by `curseClearPowerMargin` (never at parity);
otherwise it holds and says so. The sortie is a curse assault (above): one at
a time, node to node, an ordinary claim once the last node falls, the ground
skipped for a while if it is wiped or times out. Every `curseClearInterval`.
Log: `CURSE CLEAR: n units -> territory <name> (curse node at (x,z), power a
vs b)` / `CURSE CLEAR: held — ...`.

**Reconquest (2026-10-05).** A rival's locked territory used to be off the
claim table for good, so every side on a filled map logged "no claimable
territory next to Fortress-linked ground" from minute 20 on, and ground lost
at minute 11 stayed lost. A rival's territory is now a claim candidate when
it borders the faction's Fortress-linked ground and is NOT the rival's walled
home (the territory holding its capital — a wave's business, § 6a), scored
below free land by `claimHostileTargetPenalty`. The squad is drafted like a
curse assault (the free army up to `claimCurseSquadMax`, one assault out at a
time) and goes only when `AIEngagement` says it beats what stands at the
nearest known rival building there by the tier's `reconquestMargin` (0 on
Easy: it never retakes ground). On the
ground it walks building to building — the rival's extractors, houses and
Fortress are what lock the territory (Territory_Claims.md §6) — and when the
last one falls it becomes an ordinary claim on the freed ground, its clock
restarted. Allies are never candidates (`Alliances.AreHostile`).

### 5c. Fortress expansion (2026-10-03)

**The AI builds Fortresses in conquered ground.** After age-up, every
`fortressCheckInterval` it looks at its held, claimed, non-home territories
with no Fortress in them (one per territory, §4 — anyone's counts) and scores
each: `fortressOutcropWeight` per known outcrop inside, plus
`fortressFrontierOutcropWeight` per known outcrop in neighbours it does not
hold, plus `fortressBorderBonus` if it borders a hostile player or the curse
(or its meter is contested), plus `fortressDisconnectedBonus` if it is cut off
from every Fortress (a Fortress there re-links it and stops the wear-down),
less `fortressDistanceWeight` per metre from the home capital.

- **One in flight**: nothing new while one of its Fortresses is a plan or
  under construction; at most `fortressMaxPerFaction` in all.
- **Never starve the army**: it places one only when the bank covers the
  Fortress price (`BuildCosts`, the SO) PLUS `fortressReserve*`. Short of that,
  while expansion is consolidating (§ 5b) it SAVES (AIPivotalReserve
  `FortressExpansion`, non-strict, so the hold breathes); otherwise it simply
  waits. It never gates claiming.
- **The savings hold is resource-aware** (2026-10-03, every AIPivotalReserve
  goal, not just this one): a purchase is held only if it spends a resource
  the bank is short on against the summed reserves (+ pad), so a save short
  only on veilstone keeps training, building and researching with supplies and
  iron, and the composition picker steps past units the hold would refuse.
  The duty cycle, strict reserves and exemptions apply unchanged on top.
  Measured before it, 60-minute batch: the Fortress save never filled
  (veilstone ~214 against 400) while supplies sat near the cap, and the median
  army fell from 106 to 47.
- **Sited inside the chosen territory** (the site search is locked to it),
  anchored on the territory's seed; a refused site skips that territory for
  `fortressSiteRetrySeconds`. Every decision is logged under `EXPANSION`
  (`no Fortress: …`, `Fortress ordered in …`, `… refused: <reason>`).
- **No walls follow** (2026-10-03): the AI builds a Fortress in every
  territory it can, but § Walls walls only its starting territory — a
  Fortress territory is never ringed in stone.
- **The home capital stays the anchor**: a Fortress carries HallTag like the
  capital, so "the Hall" the brain lays its base around is now the faction's
  LOWEST-NetworkId capital (the starting one), not whichever capital the
  chunk order lists first.

Each extra Fortress locks its territory and is a new link for §10
connection (there is no territory limit for it to raise, 2026-10-04). Numbers: `SimpleAISystem.asset`.

**Always saved for (2026-10-05).** The Fortress used to be a savings goal
only while the faction was consolidating after a loss; otherwise the check
"waited for the bank" to hold the cost plus a reserve at once. A tier that
thinks every quarter second never has a bank — every surplus goes to
something cheaper first — so Expert and Hard logged "waiting for a Fortress"
twenty times a match and built exactly one, while Easy built eight and
Fortress levels were the winners' largest income line. Now the moment a held
territory qualifies (its resource buildings up, or cut off from every
Fortress), the Fortress is the faction's non-strict `AIPivotalReserve` goal:
the cost is what the bank must reach, and the `fortressReserve*` values on
`SimpleAISystem.asset` are the buffer the goal keeps on top, not a second
price. Essentials still carve through a non-strict goal (§ 5f). **And a
budget reservation (2026-10-05, v4):** the pivotal hold breathes and pauses
under distress, and every release let a fast tier spend the pot on levels
and towers, so while the goal stands the Fortress cost is also
`AIBudget.Reserve`d — held off the top of every wallet spend and of the
building levels, above the working float — and cleared when it is ordered.

### 5d. Army composition by role, the dynamic basics share and veilstone priority (2026-10-03)

The 0.0.33 60-minute batch ended with armies of Spearmen and Archers (Blue,
Sundered Crown: 147 + 132, then 2 Cataphracts, 1 Outrider, 1 Ballista), no
Swordsman / Nobleman / Sentinel / Crossbowman / Longbowman, no ram, catapult or
trebuchet, and King Lexor in 9 factions of 27. Every one of the missing units
is priced in veilstone and the basics are not; the picker passed veilstone
units over whenever any savings hold was short of veilstone (the Fortress save
nearly always was), stepped down to the basics, and those filled population.
A first fix planned the army as five "lines" each training its best unlocked
tier. **That is superseded (developer directive, same day): there is no "best
tier".** Swordsman, Nobleman and Sentinel are not tiers of one line, and
neither are Archer, Crossbowman and Longbowman — every unit has a ROLE and a
COUNTER (§ Counter table in [Combat_Pacing.md](Combat_Pacing.md)), and the
army answers what the enemy has been seen fielding.
Implementation: `SimpleAISystem.Composition.cs`; every number is in an asset.

**Roles.** An aged-up Alanthor army is planned over a role table
(`compositionRoles` in `SimpleAISystem.asset`), one row per unit:

| Unit | Role (why it is in the army) | More of it when the enemy shows | Fallback while locked |
|------|------------------------------|---------------------------------|-----------------------|
| Spearman (basic) | Anti-cavalry: the bonus vs Cavalry. Cheap, veilstone-free, **kept all game** — a baseline share without intel, scaled by the enemy's cavalry | cavalry | — |
| Archer (basic) | Anti-infantry: massed cheap bows clear a foot line | infantry; **less** vs armour and cavalry | Spearman |
| Swordsman | Mail line infantry, the bonus vs Siege: the body of the line and how a siege train dies | siege | Spearman |
| Nobleman | Elite shock infantry, no bonus: the most melee damage and the fastest foot — the gap-closer onto bow lines | ranged | Swordsman |
| Sentinel | The heaviest armour, the bonus vs Heavy: heavy infantry, barded cavalry, what arrows cannot hurt | armoured, heavy, heavy cavalry | Swordsman |
| Crossbowman | Ranged anti-cavalry (bonus vs Cavalry, ignores barding): the Cataphract answer | cavalry, heavy cavalry | Spearman |
| Longbowman | Long-range bow DPS, no bonus: outranges every bow, shreds slow foot and masses | infantry, massed; **less** vs cavalry | Archer |
| Outrider | Light raider cavalry (bonus vs Ranged), the fastest unit | ranged, siege; **less** vs cavalry | Nobleman |
| Cataphract | Heavy shock cavalry (bonus vs Ranged): breaks bow lines and siege | ranged, siege | Outrider |
| Battering Ram | **Early** anti-building; its share moves to the Trebuchet once the Siege Yard can train one | — | Ballista |
| Trebuchet | **Late** anti-building / fortification, the longest reach | — | Battering Ram |
| Ballista | **Anti-army**, single target: heroes, heavy cavalry, enemy engines | high-value (heroes + heavy cavalry + siege) | Sentinel |
| Catapult | **Anti-army**, splash (bonus vs Infantry): clumped foot and formations | massed foot | Longbowman |

**How a share is made.**

1. **Intel.** `share = baseShare + counterResponse x sum(perEnemyX x X)`,
   where X are fractions of the scouted enemy army by estimated strength,
   over the brain's fresh sightings (`EnemySightingRecord`; freshness is the
   difficulty's `intelFreshnessSeconds`, measured against the newest
   sighting): cavalry, heavy cavalry, infantry, ranged, Heavy, armoured
   (ranged armour >= `armouredRangedDefense`), siege, high-value, massed
   (foot standing `massedMinUnits`+ to a `massedCellSize` cell). A negative
   coefficient marks the wrong answer. Fewer than `enemyReadMinSightings`
   sightings = no read: the baseline mix.
2. **Personality.** Each class (infantry / ranged / cavalry / siege) is scaled
   by the personality's `RoleBudget` relative to Balanced's; the basics by
   its `basicsAppetite` (AISettingsSO personality block).
3. **Difficulty.** `counterResponse` (how hard intel bends the mix) and
   `basicsShareScale`, on each difficulty profile asset.
4. **Economy.** The veilstone roles are scaled by the faction's Trading
   Outpost income (Buy rate x Outposts, Swift Caravans included) against
   `veilstoneIncomeForFullLadder`, down to `ladderScaleWhenStarved` at none (a
   bank of `veilstoneBankForFullLadder` counts as full); the basics take up the
   slack.
5. **Locks.** A role its trainer cannot train yet hands its share down its
   fallback chain; the Ram hands its share UP to the Trebuchet once that can
   train (early rams, late trebuchets).
6. The basics' total is clamped into [`basicsMinShare`, `basicsMaxShare`].

The next unit is the role furthest below its share (`(count + 1) / share`,
table order breaks ties). Counts include units still in production queues.

| Difficulty | `counterResponse` | `basicsShareScale` |
|---|---|---|
| Easy | 0.4 | 1.35 |
| Normal | 0.75 | 1.0 |
| Hard | 1.0 | 0.85 |
| Expert | 1.15 | 0.75 |

`basicsAppetite` by personality: Balanced 1.0, Aggressive 1.15, Defensive 1.05,
Economic 0.9, Rush 1.3, TechBoom 0.75, Turtle 0.95.

With no intel and full veilstone income the table puts the basics at about
22% of a Balanced Normal army; a starved economy pushes them toward 40%; a
cavalry-heavy enemy raises the Spearmen (and the Crossbowmen) with it.

**The basics cap is a HARD ceiling (2026-10-04, developer ruling: "The
initial share on basic units is fixed, even if there is population for them.
The plan must always be to grab more veilstone and, do upgrades").** Once any
role unit can train, Spearmen + Archers may number the LARGER of
`basicsFloor` and the plan's basics share x army — the floor only lets a
small army start, it is never added on top of the share. The share itself
stays dynamic (intel, personality, difficulty, economy, above); what it
computes is the ceiling, whatever population is free. Under the cap a basic
fills in while the chosen role unit is unaffordable (or IS the role most
behind — the spear wall); at the cap the AI WAITS and SAVES for the role unit
(its train attempt fails on the bank, which records the veilstone shortage
and keeps every Trading Outpost buying). Nothing lets a basic past the cap:
the old one-basic-every-`basicsOverflowSeconds` valve is gone, the "floor
unit has no trainer — fall back to Spearman" rescue does not fire while the
plan is active and the basics are capped, and the 2026-10-04 "basics fill"
(veilstone-free units past the cap while supplies and iron piled up) is
removed — a 60-minute batch with it ended 84% Spearman/Archer and starved the
role units. Idle supplies and iron go to veilstone and upgrades instead
(§ 5e). The cap is enforced in the training pre-flight, so every path (floor,
growth burst, goal list, build order, emergency defence) obeys it. Age 0, and
an aged-up army with no role unit trainable yet, are unchanged: the basics
are all that exists.

**Siege by phase and by target.** The siege program (`siegeTrainFloor`) trains
the siege role most below its share: rams early and trebuchets late against
buildings, and the anti-army engines by what was scouted — heroes, heavy
cavalry and enemy engines call for Ballistas, massed foot for Catapults. **In
the field** the two anti-army engines pick their own kind of target from what
is already in reach (unit SO `preferTargets`: the Ballista prefers heroes,
Cavalry+Heavy and Siege, the Catapult the densest knot of enemies) —
[Combat_Pacing.md § Target preference](Combat_Pacing.md). Ordered targets are
never overridden.

**Veilstone priority (the army's earmark).** The wallets were not the problem
— Military already borrows from both other wallets. What was missing was
priority. While a veilstone role is below its share of the army or King Lexor
is owed, and there is population to spawn into, `militaryVeilstoneShare` of
every think window's veilstone income is earmarked for the army
(`AIBudget.SetMilitaryVeilstoneClaim`, capped at `militaryVeilstoneCreditCap`
and at the bank). Military purchases debit it; every non-military veilstone
spender — any building, the expansion Fortress included, and research (the
economy ladder and the sweep) — must leave it in the bank; the savings hold
judges a combat unit only on the veilstone the earmark does not cover. The
Alanthor endgame system no longer runs its own unit picker. Sect units are
unchanged.

**The veilstone income itself** was raised: a Trading Outpost's Buy Veilstone
now delivers 100 a minute (was 65; inputs unchanged — developer directive,
chosen over cutting unit prices), and the Outpost's new **Swift Caravans**
research speeds every trade up (Veilstone_Economy.md §3.1). The AI researches
Trade Agreements I then Swift Caravans on its economy ladder (the research-host
case for the Outpost was missing, so no Outpost tech had ever been bought).

**King Lexor.** The level gate was never wrong: in every faction whose home
capital reached L3 the "needs Lv3" refusals stopped within a minute. What kept
him away was (1) capitals stuck below L3 — AIBuildingUpgradeSystem gave the
capital one turn in ten, spent the Fortress turn on the LOWEST Fortress (any
new expansion one) and shut it out under the supply reserve, so capitals sat
at L1 for 50 minutes while expansion Fortresses levelled in three — and (2)
once unlocked, the Fortress save's veilstone hold, the bank and a full
population. Now the home capital is levelled ahead of the rotation and past
the reserves up to `capitalPriorityLevel` (only Gatherer's Huts level while
it saves for it); a capital unique is never held by a savings hold; while he
is owed every other combat unit leaves his veilstone in the bank and his
population free; and he trains at any of the faction's capitals that can take
him (home first). A dead king is owed again and re-trained.

**Log.** Once per `compositionLogInterval`: `MILITARY: composition: army N,
basics b/cap (share s%: difficulty xd, personality xp, economy xe at v
veilstone/min) | enemy (n sighted, response xr): cav 40% … | Spearman 12/20
30% (enemy cav 40%) | Swordsman 4/9 14% (base, covering Sentinel) | … |
veilstone v, army earmark e | King Lexor owed | saving for X` — each role's
count / target, its share and the reason for it.

Numbers: `SimpleAISystem.asset` (`compositionRoles`, `basicsFloor` 16,
`basicsMinShare` 0.12, `basicsMaxShare` 0.6,
`veilstoneIncomeForFullLadder` 300, `veilstoneBankForFullLadder` 600,
`ladderScaleWhenStarved` 0.35, `armouredRangedDefense` 3, `massedCellSize` 10,
`massedMinUnits` 6, `enemyReadMinSightings` 4, `compositionLogInterval` 60),
the four difficulty profiles, the `AISettingsSO` personality blocks,
`AIBudget.asset` (`militaryVeilstoneShare` 0.75, `militaryVeilstoneCreditCap`
400), `AIBuildingUpgradeSystem.asset` (`capitalPriorityLevel` 3),
`TradingOutpostSystem.asset` (`buyVeilstone` 100).

### 5e. Surplus goes to veilstone and upgrades (2026-10-04)

The basics cap (§ 5d) means an Alanthor bank can fill with supplies and iron
while the army waits on veilstone. That surplus is never turned into more
basics; it is spent on what raises veilstone income and on upgrades.

**The triggers.** The bank is *overflowing* when it holds at least
`surplusSupplies` supplies AND `surplusIron` iron (`AIBudget.asset`). It is
*veilstone-held* when the army's last refused purchases were short of
veilstone (`AIBudget.IsMilitaryShort`). The composition also records that
shortage itself every think while it is saving for a role unit the bank
cannot cover in veilstone, so the signal does not depend on a train attempt
having been made. *Veilstone surplus* is both together, for an aged-up
Alanthor faction.

**Veilstone surplus: grab more veilstone**, in this order:

1. **A Trading Outpost beside every outcrop with a free side in held
   ground.** The extractor walk offers ONE side per outcrop (its free,
   buildable side nearest the Hall), cheapest per-outcrop ramp first — so a
   fresh outcrop at base price always comes before a 3rd or 4th post on one
   it already trades at (2026-10-04, Veilstone_Economy.md §3.1) — and places
   as many as it can in one pass, not one per pass, for as long as a worker is idle to take the next site
   (the open-site cap and the bank are re-checked per placement; the worker
   rule is unchanged, so a busy crew still paces it). Every
   Outpost BUYS while the army is short of veilstone (§ 5a); trading is paid
   from the bank each cycle, so the surplus becomes veilstone with no further
   switch.
2. **The Trading Outpost's speed research, then its discount research.**
   These are the ids listed in `TradingOutpostSystem.asset` (`speedTechs`,
   then `discountTechs`), not a list in the AI. They are bought as soon as an
   Outpost can host them. They pass the savings hold and the army's veilstone
   earmark, because they are the veilstone income the earmark waits on.
   **Then one Outpost level-up per think** (2026-10-04): a level raises that
   post's trade rate. The cheapest next level first (the ramp makes a first
   post's level cheaper than a fourth's), never dipping into the army's
   veilstone earmark; L1 is the free level and is never bought.
3. **Expansion toward veilstone.** Each known outcrop in a claim candidate
   scores `surplusClaimOutcropScale` times its usual veilstone bonus. Claims
   are tried every `claimAttemptInterval` x `surplusClaimIntervalScale`, and
   the "army before the next claim" bar does not apply: the army is waiting
   on veilstone anyway, and a claim costs no money. The Fortress picker
   scores outcrops (inside and on the frontier) `surplusFortressOutcropScale`
   times as much. When the chosen territory or its frontier holds an outcrop,
   that Fortress may also use the army's veilstone earmark, because more
   ground means more Outposts. The one-Fortress-per-territory and
   one-in-flight rules still apply.

**Overflowing bank: upgrades.**

- **Building levels.** AIBuildingUpgradeSystem queues up to
  `surplusUpgradesPerThink` level-ups per think instead of one. The capital
  still comes first (§ 5d King Lexor), and the rotation then walks the other
  lines. While the bank is veilstone-held, a level-up whose next level costs
  veilstone must leave the army's earmark in the bank.
- **No housing nobody lives in (2026-10-04).** A Hut level buys population
  and nothing else, so Huts are levelled only while free housing (cap minus
  population) is at most `hutUpgradeHeadroomMax`. Without this, surplus went
  into House levels until the population cap reached its ceiling, which
  is what showed up as an "income jump" around minute 30: the spending stopped,
  the income did not change.
- **Research.** The endgame sweep runs every `surplusResearchSweepInterval`
  instead of `researchSweepInterval`. In era 2 it no longer waits behind the
  authored ladder. It walks research hosts in this order: the Trading
  Outpost, then the buildings that train a unit the composition plan has a
  share for (their military techs improve the army being built), then
  everything else. The usual gates still apply: owned host, its level against
  the tech's `minBuildingLevel`, prerequisites, culture. While veilstone-held,
  only techs that cost no veilstone are bought from the surplus (Trading
  Outpost techs excepted, above).
- **Budget splits do not block it.** These surplus spends pay from the bank
  directly, like the capital's level-ups. The wallets are a partition of the
  bank and lend to each other (§ AIBudget), so with an overflowing bank the
  only thing they could block is a live lump-sum reservation. Surplus spends
  respect the resource-aware savings hold except where stated above.

**Log** (one line per action; the repeating "held" line is rate-limited by
`surplusLogInterval`): `SURPLUS: outpost placed at (x,z) (n free outcrop(s))`,
`SURPLUS: research <tech> at <building>`, `SURPLUS: upgrade <building> to
L<n>`, `SURPLUS: veilstone claim -> <territory> (<n> outcrop(s))`,
`SURPLUS: veilstone Fortress -> <territory>`, and once per interval
`SURPLUS: veilstone-held (supplies s, iron i, veilstone v, outposts o, free
outcrops f)`. Numbers: `AIBudget.asset` (`surplusSupplies`, `surplusIron`),
`SimpleAISystem.asset` (`surplusClaimOutcropScale`,
`surplusClaimIntervalScale`, `surplusFortressOutcropScale`,
`surplusResearchSweepInterval`, `surplusLogInterval`),
`AIBuildingUpgradeSystem.asset` (`surplusUpgradesPerThink`).

### 5f. Army first: surplus spenders yield to an army below target (2026-10-04)

Surplus is for what the army cannot use. Building levels that do not raise
unit output, and the endgame research sweep, therefore **yield to an army
that is below its desired size and could spend the money**.

**The reading.** Every think the army floor (`ReplaceLostUnits`) records
alive + queued combat units, the desired size, the cost of the unit it is
training next, and whether the army can take money at all. It cannot when
every trainer's queue is full, population is capped, or no trainer stands.
A reading older than `armyFirstStatusMaxAge` is ignored.

**The rule** (`AIBudget.ArmyFirstYield`):

- The army is at target (alive + queued at least `armyFirstTargetFraction`
  x desired), or it cannot take money: nothing yields.
- The army can buy its next unit, meaning it has not been refused for any
  resource lately (`IsMilitaryShort`): the spend yields.
- The army is waiting on a resource (veilstone, usually): a spend that costs
  that resource yields. Any other spend must leave `armyFirstReserveUnits` of
  the army's next unit (capped at the deficit) banked in each resource the
  spend costs. The units are not affordable, so the rest of the bank may go,
  but not the part that buys the army the moment the veilstone lands.

**What yields.** In AIBuildingUpgradeSystem, every level except the four
production lines (Barracks, Archery Range, Royal Stable, Siege Yard). A level
cuts their train time, so it raises unit output and never yields. **Production buildings never yield to the army, new or levelled**
(2026-10-04, operator: "the buildings are what allows the army to grow
faster"). `ArmyFirstYield` is applied nowhere to them: not to a new
Barracks, Archery Range, Royal Stable or Siege Yard (no placement path calls
it), and not to their levels. The other army-first gates exempt them too: a
production level ignores the upgrade pass's iron floor (`upgradeIronReserve`)
and the army's veilstone earmark, and still runs below the reserve floor; a
new production building is not held by the veilstone earmark either. What
bounds production spending instead is § 5g: one per province, more only
while the existing production is saturated. The savings hold
(`AIPivotalReserve`) and the wallets still apply, as for every building. The
capital's priority levels (§ 5d King Lexor) are exempt. So is the Gatherer's
Hut when it is levelled as the supply engine (below the reserves, or while
the capital's price forms). Every other level also respects the
resource-aware savings hold. In the endgame research sweep, every tech except
the Trading Outpost's own (veilstone income, § 5e).

**Why.** Headless28 (six 60-minute matches), spending per faction-minute:
units 542, buildings 537, building levels 489, trade 258, research 245. Over
the batch, levels cost 560k supplies and 153k iron, against the army's 175k
supplies and 190k iron. Iron fell to about 1,900. Of 1,655 level-ups, 481 were
production lines. The rest were Gatherer's Huts (461), Watch Towers (269),
Huts (101), Fortresses (77) and the Vault (38).

**Units before economy is the stricter form, per tier** (2026-10-05, § 5h).
On a tier with `unitsBeforeEconomy`, the economy drive (economy levels, the
capital's levels past its essential one, Vault deposits, every
non-production level, watch towers) waits for the army at its full target,
not `armyFirstTargetFraction` of it, whenever an idle trainer could still
start a unit. It is the same idea with one more condition (the trainers) and
no reserve arithmetic. Production buildings and production levels are exempt
from it exactly as from army first (developer ruling, unchanged).

**Log:** `ARMYFIRST: upgrade <building> yields — <reason>` and `ARMYFIRST:
research <tech> yields — <reason>`, at most once per `armyFirstLogInterval`
per faction, with the number of yields since the last line. Numbers:
`AIBudget.asset` (`armyFirstTargetFraction`, `armyFirstReserveUnits`,
`armyFirstStatusMaxAge`, `armyFirstLogInterval`).

**The army floor outranks every ordinary save (2026-10-05).** The Fortress
pot and the capital-level pot used to hold combat units like any other
spend: Expert in a 1v1 bought 1,622 of units against 20,000 of buildings,
levels and trade between minutes 5 and 17, had no idle soldiers to claim
with, read itself as stretched and stopped expanding. While the army is
below `armyEssentialFraction` of its target (the Rebuild line), a combat
unit passes the non-strict pivotal holds and the budget's lump-sum
reservation; the age-up's strict hold still binds. Above that line the
saves apply as before — on Easy and Normal.

**On Hard and Expert the army NEVER pays for a Fortress (`armyBeforeSaves`,
2026-10-05, operator directive: "Expert should not save for fortresses at
the expense of the army; it should have an economy so powerful that it can
spare building fortresses").** Measured across v9-v12, Expert's saving was
the thing Easy beat: Easy saves for nothing and trains to its cap, Expert
held units for the Fortress and capital pots and arrived second with a
third of the army. On such a tier a combat unit passes every non-strict
savings goal and the budget's lump-sum reservation at any army size, and the
income levels (Gatherer's Hut, Trading Outpost — §5h) pass the Fortress
reservation as well, because they raise the income the pot is filled from.
The Fortress is then paid from what the economy makes BEYOND what
production can spend: army spend is bounded by trainer throughput (one unit
per trainer per tick, `productionQueueDepth`), so a strong economy
overflows into the pot by itself, and a weak one builds its army first and
its Fortress late — which is the right order. The age-up stays strict on
every tier (Age 0 is the race); the Fortress and capital pots still hold
discretionary spending (towers, walls, Vault, non-income buildings).

### 5g. Every territory is developed, in order (2026-10-04)

Operator direction: "AI should build production buildings on all provinces"
and "AI building priorities when building a territory: 1 - resource
buildings 2 - watch towers near periphery 3 - production buildings 4 -
fortress (make sure a spot is always left available for this)".

Revised the same day, after the 60-minute batch: one production building per
province, chosen by what the army needs; more only when the existing
production is saturated; and the Fortress straight after the resource
buildings, ahead of towers and production.

**The order.** Every `territoryDevelopInterval` the AI walks each territory
it holds (claimed, not water or mountain) and runs that territory's build
order. It places at most one building per walk:

1. **Resource buildings.** An extractor on every free node in the
   territory: Gatherer's Huts, Mines, Veilstone Mines, and Trading Outposts
   on outcrop sides (the same culture rules and node gates as the extractor
   walk, § 4).
2. **The Fortress, as soon as the resource buildings are up.** The home
   territory has its capital already. Everywhere else, the Fortress
   expansion (§ 5c) picks only territories whose step 1 is done, unless the
   territory is cut off from every Fortress (`fortressAfterResources`); a
   cut-off territory needs the Fortress to re-link it. It is placed on the
   reserved spot (below). While the Fortress is due, steps 3 to 5 in that
   territory wait for it. It does not hold them when it cannot happen:
   before the age-up, at the faction's Fortress ceiling, or while a site
   there was recently refused.
3. **Watch Towers.** Only cultures with a Watch Tower take this step.
   - **The home** keeps its walls and its periphery towers: up to
     `towersPerTerritory` when it borders unowned, curse-held or hostile
     ground, each facing one such neighbour in turn, its search anchored
     `towerPeripheryFraction` of the way from the seed toward that
     neighbour's seed and locked inside the territory.
   - **Every other province is secured by coverage** (operator, 2026-10-04:
     "Players should scatter towers through the rest of the provinces so
     they can secure them"). Its important ground is a set of weighted
     points: every resource node in it (built on or free), its Fortress or
     reserved Fortress spot, each production building (sites and plans
     too), and each border sample facing an unowned, curse-held or hostile
     neighbour (the crossings). The weights are config
     (`towerWeightResource / Fortress / Production / Border`). A point is
     covered while it lies within the reach of any own Watch Tower,
     finished, site or plan, in any territory. The reach is read from the
     tower's SO: its attack range, else its line of sight. It is never a
     code number.
   - **Siting is greedy and deterministic.** Each province has a fixed grid
     of sample cells (`towerSampleStep`), flooded from its seed and built
     once per map. The next site is the sample that brings the most
     still-uncovered weight into reach. It must stand at least
     `towerMinSpacingRangeFraction` of the reach from every own tower. Ties
     go to the earlier sample. The tower is placed through the ordinary site
     search, locked to the province and held within `towerSiteSearchRadius`
     of the site, so the wall corridor, the seal check, the reserved
     Fortress spot, curse and terrain rules all still apply. A site with no
     legal footprint is set aside for `territoryBlockedStepSeconds`, and the
     next best is tried (`towerSiteTriesPerWalk` per walk).
   - **It stops** when `towerCoverageTarget` of the province's weight is
     covered, at the province cap, or when no site adds `towerMinGainWeight`.
     Then it is re-checked after `territoryBlockedStepSeconds`, since a new
     production building or a new hostile neighbour adds points.
   - Step 3 places only the first `towersProvinceFirst`. These are not
     army-gated, and a money refusal holds step 4 as any step does. The rest
     are step 5. Cut-off provinces get no new towers.
4. **The province's production buildings** (`provinceProductionPerTerritory`,
   per difficulty tier: one on Normal, more on Expert) in each province,
   breadth-first (every line to one before any line to two): **the line the
   army plan needs most** among those the territory has fewest of. Per
   line, the plan's share is the composition's raw wish
   for the units that building's SO `trains[]`, over all four lines; its
   shortfall is that share minus the line's share of the faction's production
   buildings (sites and plans counted). The largest shortfall wins. The
   Barracks when nothing else qualifies (before an Alanthor age-up, or when
   the plan names no other line). The search is anchored at the territory's
   core (its capital or Fortress, else its seed), so units are trained near
   the front.

   **The home keeps a floor instead** (operator, 2026-10-04: "Home province
   is larger, it should have at least 2 of each building"). The home
   territory keeps `homeProductionPerLine` (per difficulty tier: Normal's is
   the operator's two, harder tiers keep more, Easy fewer) of **each**
   production line the
   faction can build: the Barracks always, and the Archery Range, Royal
   Stable and Siege Yard once the culture and age make them available (the
   same rule as above: an aged-up Alanthor faction). Finished buildings,
   sites and plans in the home all count. It fills breadth-first: every line
   to one before any line to two, so a missing line always comes before a
   duplicate. Within a round the line the army plan needs most goes first,
   then any line the plan does not name. Before
   `homeProductionFloorAfterSeconds` it asks for one of each only, so the
   hut-first opening and the age-up save run first. The floor passes the
   saturation gate, as a province's own building does. Everything else
   still applies: placement, the wall corridor and seal checks, the Military
   wallet and the savings hold. The army-first rule never applies to it. A
   line with no legal spot waits `territoryBlockedStepSeconds` while the
   other lines go on. This floor replaces the economy's redundant-Barracks
   floor (§ 6c); there is one rule, not two.
5. **More coverage towers in each province**, up to
   `towersPerProvinceMax`, sited by the same coverage rule. This step comes
   **after** the province's production building, and is **army first**: it
   waits while the alive army is below `towerExtraArmyFraction` of its
   target. Towers are not production buildings, and they must never starve
   the army that defends the province. They pay from the Military wallet,
   as the first towers do.

   Logs: `TOWERS: <province> n/max placed at (x,z) — covers k/N points`
   (k = the points in that tower's reach, N = the province's points, with
   the coverage before and after), and `TOWERS: <province> coverage p% —
   done` when the province stops.

**Blocked steps.** A step blocked by money (bank, wallet, savings hold)
holds the steps below it in that territory. That holding is the priority.
A step blocked by placement (no legal spot) does not hold them: it is logged
and the next step runs. When the crew or the open-site cap refuses, the
whole walk stops. A territory waits `territoryStepCooldown` after a placement
before it may place again, because a lockstep placement lands two ticks
later.

**The reserved Fortress spot.** From the moment a non-home territory is held
and has no Fortress, one legal Fortress footprint in it is reserved. The
spot is found by a deterministic ring search from the territory's seed, out
to `fortressSpotSearchRadius`. The footprint must lie wholly inside the
territory, pass the territory, curse, overlap and terrain rules, and not
seal the base. The search first keeps the resource-node clearance and then
drops it. Every AI placer (the base placer, the endgame ring scans, the
tower scan) refuses a footprint on the spot plus `fortressSpotMarginCells`,
just as the wall corridor is kept clear. The spot is re-checked every walk.
If a rival, the curse or a node takes it, a new spot is reserved. When the
territory is lost or a Fortress stands there, the reservation is dropped.
The Fortress is placed on its spot first, and on the usual search (locked to
the territory, starting at its seed) only if the spot no longer passes.

**More production only when the existing production is saturated**
(2026-10-04 revision). Every production building past a province's own one,
and past the home floor, whatever asks for it (the goal list, the
economy's per-line growth, the siege program), passes one gate
(`ProductionGate`). It is placed only when all of these hold:

- **Saturated.** At least `productionSaturationThreshold` of the faction's
  finished production buildings have work in their queue, and that has held
  without a break for `productionSaturationSeconds`. Sampled every think.
  Both are per difficulty tier: a harder tier counts as saturated at a lower
  busy share and sooner, so it adds trainers in parallel earlier.
- **The army is below its target** (alive combat units under the floor's
  desired size).
- **No production building is still rising**, so one extra lands before the
  next is judged.
- **It is the line the army plan needs most** (the step 4 rule, over the
  whole faction). A request for another line is refused. **A line whose
  last extra found no legal spot does not count** (2026-10-05): it sits out
  `territoryBlockedStepSeconds` and the next-needed line is asked instead,
  so an unplaceable Siege Yard can no longer veto every other trainer.

After an extra is placed the saturation window starts again. Four things
pass without the gate: a province's own step 4, the home floor, the
lost-trainer rebuild
(§ 6c, unchanged), and the first Barracks of a faction with no production at
all. The Alanthor endgame's age-2 ladder no longer places the Royal Stable
and Siege Yard; they come from this rule like every other line. The siege
program saves for its yard only when the gate is not what refused it.

Why: Headless30 against Headless29 (60-minute batches), after production on
every province went in: the typical late army fell by half, building spend
per faction-minute rose by half, production buildings standing at the end
rose by a third, and Fortresses ordered fell by more than half. The
buildings were bought with the army's money and then stood idle, and the
Fortress waited behind them.

**Production in every province.** A production building requested without
an anchor is no longer searched only around the home capital. This covers
the goal list, the economy's per-line growth and the lost-trainer rebuild.
The request tries up to `productionSiteTerritoriesPerCall` held territories:
the one with the fewest production buildings first, then frontier ground,
then the home, then by index. Each is searched inside its own ground from
its core. Cut-off territories, and provinces still on steps 1-3, are
skipped. The rally logic is unchanged: a remote Barracks' units are idle
military like any other, and the wave draft and wave reinforcement collect
them wherever they stand. Why: Headless29 logged `placement rejected — on
wall corridor (…; search failed)` 1,678 times, every one a production
building (or a tower). Each was a whole home-ring search that failed. Of the
247-672 candidates in each, `past-ring` refused 100-425, `gap` 52-240,
`spacing` 26-130 and the border band/ring 7-233. The home ring was full
while the provinces stood empty, and banks climbed to about 10,000 supplies
and 9,000 iron that production could not absorb.

**Logs:** `TERRITORY <name>: step <n> <what>` (on each placement and each
step change), `TERRITORY production: N Barracks, … across N territories —
<name> B1 R1 S0 Y0 T2 [step] spot (x,z) | …` every
`territoryReportInterval`, `FORTRESS SPOT reserved in <territory> at
(x,z)`, `FORTRESS SPOT in <territory> at (x,z) lost — re-reserving`,
`FORTRESS SPOT: no legal NxN spot in <territory> …`, `BUILDING: <id> sited
in <territory> (N production building(s) there before)`, `EXPANSION: no
Fortress: N territory(ies) without one still on step 1 (resource
buildings)`, `PRODUCTION: province <territory> gets <line> (shortfall N pts:
plan N% vs N% of N trainer(s))`, `PRODUCTION: home floor <line> n/N
(<why this line>)`, `PRODUCTION: extra <line> — saturated (N%
busy over Ns, army a/target)` and `PRODUCTION: extra held — <why> [<line>
asked]` (not saturated, army at target, one still rising, or the plan needs
another line; at most once per `productionLogInterval`), and once per match
per faction `PRODUCTION: capacity home x/line, province y, extras at z% busy
for Ns (<tier>)`. Numbers: the difficulty profiles
(`AISimpleDifficulty/Profiles/`: `homeProductionPerLine`,
`provinceProductionPerTerritory`, `productionSaturationThreshold`,
`productionSaturationSeconds`) and `SimpleAISystem.asset` (the rest).

**Why the capacity is per tier (2026-10-05).** A harder tier attacks more
often. It must pay for that with more trainers working in parallel, never with
a smaller standing army (§ 2). Production placement and production levels
stay exempt from army first and from every other army-protecting gate.

### 5h. The economic drive, the Vault and the economy's ceiling (2026-10-04)

**What was measured** (Headless33 all-Normal, Headless34 Red-Expert and
all-Expert, from `MapTrace.txt` + the AI logs). Building on held ground is
NOT the bottleneck: a claimed territory gets its first extractor site within
about a minute and every slot covered within one to four, sites finish in
seconds, and "no idle worker" was the rarest reason an extractor waited
(about one block in ten; the bank and free nodes were the rest), so the
worker rule (3 + 1 per conquered territory) is not what binds. What separates a rich faction
from a poor one is **levels**, above all the **capital's**: a territory's
slots are multiplied by its Fortress's level (x1 / x2 / x4,
`TerritoryIncomeSystem.HallMultiplier`), so the home capital at L2 and L3 is
by far the highest-return purchase in the game and pays itself back in about
a minute. Factions whose capital reached L3 by ~20 minutes earned three to
six times the income of those stuck at L1, and a capital stuck at L1 meant
the extractor levels never came either. The faction that stayed at L1 was
always the one that spent every supply on units as it arrived (the Rush
personality, Expert included): the capital's price never formed. Second:
extractor levels came 13-30 minutes after the extractor, only as surplus.
Third, a rusher saved for the age-up only after its Age 0 wave, ageing up
four to six minutes after everyone else, and the first-Religion-Point hunt
fought curse nodes at even power and lost.

**The rules** (every tier runs them; the difficulty profile says how hard):

- **The capital's price is a savings goal** (`reserveForCapitalLevel`). When
  the home capital is below its priority level and the bank cannot pay, its
  price is registered with the savings hold (`AIPivotalReserve`, key
  `CapitalLevel`), so discretionary army spending of the short resource
  pauses until it forms. The army floors stay exempt (§ 3a: the essential
  units below the claim gate always train), and the hold is lifted while
  the posture is Defend. A tier without the flag behaves as before.
- **Economy levels first** (`economyUpgradesPerThink`). Before the upgrade
  rotation, up to that many economy levels a think: the Gatherer's Hut
  (highest first, as § 5e) and the Trading Outpost (cheapest first). They do
  not yield to the army (§ 5f) — a level is income, which is what the army
  is waiting on — but they respect the savings hold and leave the army's
  veilstone earmark in the bank. 0 = only the rotation and the surplus pass,
  as before.
- **Production queues stay shallow** (`productionQueueDepth`). Units are
  paid for when queued, so a deep queue locks money that could start a unit
  in another, idle building. A tier with a depth queues into a building only
  while its whole queue (units, research, levels) is shorter than the depth,
  always into the least busy one; 1 means only the unit in training, and the
  next one is started on the think after it finishes. The army plan then
  re-decides what to train every time. 0 = no AI-side cap (the building's
  16-slot cap only).
- **Units before economy** (`unitsBeforeEconomy`, 2026-10-05). On a tier
  with the flag the economy drive spends only when the money can no longer
  become units. The drive is: the economy-level pass, the capital's levels
  past its essential one, every other non-production level in the upgrade
  rotation, Vault deposits, and the discretionary non-production buildings
  (the home's periphery towers, the provinces' coverage towers, the
  endgame's extra towers). It spends when ANY of these holds, and otherwise
  the money goes to units:
  - the army (alive + queued) is at or above its target, or cannot take money
    (every trainer full, population capped, no trainer), or there is no fresh
    reading;
  - every finished production building is training at the tier's queue depth
    (one item on a tier with no cap), so no more units can start;
  - the bank is overflowing (§ 5e), so the units are not absorbing it;
  - the army is waiting on a resource this spend does not cost (veilstone,
    usually).
  Never deferred: production buildings and their levels, housing,
  extractors, the Fortress, the landmark, the Temple, and the Gatherer's Hut
  levelled as the supply engine while the capital's essential price forms.
- **The capital's essential level, and a cap on its savings hold.** Capital
  levels up to `capitalEssentialLevel` (`AIBuildingUpgradeSystem.asset`; L2,
  which doubles the home territory's income) stay essential: bought and saved
  for ahead of everything. Levels above it, up to the priority level, are
  economy drive under the rule above. The savings hold itself is capped: once
  it has held the bank unbroken for `capitalReserveMaxHoldSeconds` it is
  released for `capitalReserveRestSeconds` (the upgrade rotation runs
  meanwhile), so the capital cannot starve production for long. The capital
  is still bought whenever the bank can pay.
- **The age-up savings are protected** (`protectAgeUpSavings`). A tier with
  the flag does not make the Age 0 wave even as Aggressive / Rush: it saves
  for the landmark from the start with the 4-unit garrison every other
  personality keeps (§ 3a), and attacks after the age-up.
- **The curse hunt waits for the economy, and never fights at parity.** The
  first-Religion-Point hunt (§ 6) launches only when the army's power beats
  the node's by `religionHuntPowerMargin` (every tier), and a tier with
  `curseHuntMinCapitalLevel` defers it, without growing the army for it,
  until its capital stands at that level (1 = aged up).
- **The basics share is not a way to shrink the army.** Expert's lower
  `basicsShareScale` leans on role units only while veilstone flows. When
  the plan is veilstone-starved (its economy scale below 1), the basics
  scale is raised toward 1 by the same amount, so a veilstone-bound army
  is not also capped on the basics it can afford.

**The Vault of Almiérra is used by every AI** (2026-10-04, developer: "All
AIs should be able to use the market"). The Vault pass runs
(every `econPassInterval`, per Vault, while unlocked):

- **Deposit** when the Vault is empty and the posture is not Defend: the
  resource with the largest idle surplus — the bank above
  `vaultKeep<Resource>` and above every pending savings goal — that the
  design lets it deposit (supplies always; iron after Iron Subsidies,
  veilstone after Veilstone Monetization, veilsteel after Veilsteel Bonds,
  [Age_0.md § Vault](Age_0.md)), never veilstone the army is waiting on.
  It deposits `vaultDepositShare` of that surplus, at least
  `vaultMinDeposit`.
- **Withdraw** everything when the hold time `vaultHoldSeconds` has run;
  earlier, on a tier with `vaultWithdrawOnNeed`, when a purchase is waiting
  on the stored resource (the army is short of it, or a savings goal is);
  and always when the Vault is damaged below `vaultDamagedFraction` of its
  health or the posture turns Defend (stored resources die with the Vault).

Difficulty scales how much is deposited and how cleverly it is withdrawn,
never whether the Vault is used.

**The ceiling and the easier tiers.** With the rules above, an Expert's
income is bounded by what it holds and how fast levels can be bought —
which the developer's batches show is reached by the best Normal factions
too. The levers that keep the easier tiers below it, all difficulty data
and none of them a resource cheat, are listed in the 2026-10-04 economy
report (capital-level priority, economy upgrades per think, queue depth,
Vault share, age-up protection, think interval). None is implemented as a
cap yet.

**Logs:** `ECON: node <id> built in Ns after claim (<territory>)`,
`ECON: idle crew n of m`, `ECON: upgrade <building> -> Ln`, `ECON: capital
L<n> saving (<price>)`, `ECON: capital L<n> saving released after Ns (units
and production first for Ns)`, `ECON: deferred (army a/t, trainers idle k) —
<what> (n deferral(s) since the last line)` (at most once per
`armyFirstLogInterval` per faction), `PRODUCTION: parallel n buildings training, queue
depth d`, `VAULT: deposit <n> <resource>` / `VAULT: withdraw <n> <resource>
(<why>)`, `RELIGION: hunt deferred (economy first)`. Numbers: the
difficulty profiles (`AISimpleDifficulty/Profiles/`) and
`SimpleAISystem.asset` (`religionHuntPowerMargin`, `econPassInterval`, `vault*`,
`econLogInterval`).

### 5i. Defending and rebuilding the economy; the savings pause (2026-10-05)

An in-editor SunderedCrown match: Blue (Expert) started beside a curse node
whose garrison razed its supply sites all match — 13 Gatherer's Huts, 8
Huts, 6 Mines — and its supply income fell from ~19/s to ~4/s while the
capital's L2 saving, the Fortress pot and the Outpost pot kept holding every
trickle (the army floor read "pivotal hold (saving) short supplies"). Its
army never grew; a rival's wave razed its Fortress at 25:47. Four rules:

**1. An attack on the economy gets a response.** An extractor (Gatherer's
Hut, Mine, Veilstone Mine, Trading Outpost), a house or a worker **in held
ground** whose last attacker is alive, hostile — **the curse included** —
and still beside it (`economyDefenceProbeRadius`) is an incident, one per
20 m. After the tier's `economyDefenceDelaySeconds` the standing army
answers: the attacker's power is read with AIEngagement
(`economyDefenceAssessRadius`, mobile army plus static defences), and free
soldiers — not in a wave, a claim, another response or a player's order,
not already fighting, within `economyDefenceDraftRadius` — are drafted
**nearest first until our power there (units already in the band and
responders on the road included) beats the attacker's by the tier's
`economyDefenceMargin`**, never at parity. When everything free is not
enough, **nothing is sent** (a feeder squad is the failure this replaces)
and the hold is logged. A response is topped up at most every
`economyDefenceTopUpSeconds`, and released — its soldiers walk home — when
no hostile power is left there or after `economyDefenceTimeoutSeconds`.
Responders count as claim-squad members, so no wave, reclaim or claim drafts
them, and the posture's recall leaves them on their fight. Harder tiers
answer sooner and stronger (delay and margin are profile fields).
The posture's own Defend response to a building under attack now names the
curse as a threat too: it used to skip Border units, so a curse raid entered
Defend, disbanded every mission and dispatched no one.

**2. Curse nodes are attacked in force or not at all.** The reclaim squad
(curse at the doorstep, veilstone poverty, the RP hunt) needs **the squad
alone** — our units already fighting at the node do not count — to beat the
node by `religionHuntPowerMargin`, and at least `reclaimMinSquadSize`
soldiers: when its first `reclaimSquadSize` fall short it takes more free
ones, nearest first, up to `claimCurseSquadMax`. After a squad marched on a
node no other marches on it for `reclaimRetrySeconds`, so a live attempt is
not fed piecemeal and a failed one is not repeated at once. The first-RP
hunt reinforces only a fight won by the same margin, and only with groups of
at least `reclaimMinSquadSize`. (Blue logged "1 units vs curse node" eleven
times in one minute; Red sent 59 sorties.)

**The curse is judged by what it will field, not by what stands there**
(2026-10-05). Red (Easy) lost 124 units at two curse nodes although the curse
sent it no wave. Its hunts launched at "power 160 vs 78", "216 vs 87" and
"252 vs 99". The same node had read 249-288 a minute earlier, and the
garrison was back at full strength once the fight started. Every path that
attacks a curse node uses one estimate: the first-RP hunt, the reclaim squad,
the curse-clearing sortie and the claim's curse assault. The estimate is the
**maximum** of three readings:
- **visible** — curse power in the band now, with the Crystalling pack's
  damage bonus for the count standing there (the strength scale cannot see
  it);
- **last seen** — the highest reading this faction took while the node was
  in sight. It decays with half-life `curseIntelLastSeenHalfLifeSeconds`, and
  a lower reading never replaces it;
- **baseline** — the garrison the curse's own rules give a node by now
  (`garrisonCap` x `armyGrowth`^n, n = spawns so far, capped by
  the curse's territory-scaled cap (`CurseUnitCap.Max`) shared among the live nodes, at the match minute's army
  tier, pack included), x `curseIntelBaselineFraction`.

A node no one has had in sight for `curseIntelLookSeconds` is multiplied by
`curseIntelStaleFactor`, and every estimate by `curseIntelGroundFactor` for
the cursed ground's slow and burn. Logged as `INTEL: curse node (x,z) power
est e (visible v [...], last-seen m, baseline b)`.

**3. A lost extractor is rebuilt first — but not into the same reach.** A
drop in an extractor count makes that kind **owed** a rebuild for
`extractorRebuildWindowSeconds` (or until the count is back): a **strict**
savings goal holds its price, above the capital's level, the Fortress and the
Outposts, and the extractor walk runs every `extractorRebuildAttemptInterval`
with the owed kind first. The debt is dropped when no free node of the kind
is left in held ground, when the culture cannot build it, or after
`extractorRebuildMaxRefusals` walks in which every free node refused it.
**Sites that keep dying wait:** a site lost once is not rebuilt while a live
curse node stands within `extractorCurseKeepoutRadius` of it (the reclaim
squad and the curse-clearing sortie are what free it — Blue's hut 35 m from a
node died ten times), and a site lost `extractorSiteMaxLosses` times to
anyone rests `extractorSiteBlockSeconds`. A faction with **no worker at all**
retrains one bank-direct, past the wallets.

**4. Distress pauses the savings.** Every think, after the budget measures
income, the economy is in **distress** while:
- supply income is **collapsed** — below `economyCollapseIncomeFraction` of
  the **expected** income, the best supply income the faction has measured,
  decaying with `economyExpectedHalfLifeSeconds` and allowed to climb only
  `economyExpectedRisePerSecond` (a windfall is not the norm); no collapse is
  read while the expected income is under `economyCollapseMinExpected` (the
  opening). It recovers past `economyRecoveredIncomeFraction` (hysteresis);
- or the economy is **under attack** — Defend posture, a response out, or an
  incident in the last `economyAttackLingerSeconds`;
- or an extractor rebuild is **owed**.

While it is, every **ordinary** savings goal pauses
(`AIPivotalReserve.SetSuspended`: the goals still exist but hold nothing and
count toward no shortfall) — the capital's level saving
(AIBuildingUpgradeSystem does not arm it at all), the Fortress and Outpost
pots, the hero and siege pots — so the trickle reaches the rebuild and the
army floor again. **Strict** goals keep holding: the age-up landmark, a lost
sole trainer, the extractor rebuild. The Vault deposits nothing and releases
its supplies. Units-before-economy is unchanged: it never holds production
or the army, and with the pots paused the floor trains again on a trickle.

**Logs:** `DEFEND: response n vs attacker at (x,z) (power a vs b)`, `DEFEND:
held at (x,z): ...`, `DEFEND: response at (x,z) released (...)`, `ECON: lost
n <id> — rebuilding first (c/t)`, `ECON: rebuild of <id> dropped ...`, `ECON:
savings paused (supply income x/s of expected y/s[; economy under attack][;
rebuilding <id>])`, `ECON: savings resumed (...)`, `ECON: no workers left —
retraining one at once`, `RECLAIM: n units vs curse node at (x,z) (power a vs
b)`, `EXTRACT: blocked: ... site(s) skipped: lost there before`. Numbers: the
difficulty profiles (`economyDefenceDelaySeconds`, `economyDefenceMargin`)
and `SimpleAISystem.asset`.

**The pause is confirmed, not sampled (2026-10-05).** The collapse reading
has to hold for `economyCollapseConfirmSeconds` before the pause flips on or
off: on Mirror Marches the income estimate read spiky against the ledger's
tick and the pause flipped nineteen times in five minutes, each flip a window
for the economy levels to spend the Fortress savings.

**No idle worker is not a rollback (2026-10-05).** When the AI's placement
found no idle worker to send, it refunded and destroyed the building — and
a fast tier placed it again next think: dozens of pay-and-refund cycles a
minute, a bank that read one building short whenever Expert checked whether
it could afford a soldier, and a quarter of Easy's unit spend in the opening.
A worker-raised building is a plan (Planned_Buildings.md), so it now stands
and the nearest busy worker takes it as its next job; idle workers adopt
unattended plans on their own.

**Ground being taken is an incident (2026-10-05).** A hostile standing on
held, unlocked ground drains its meter (Territory_Claims.md §2) without
touching a building, and the economy defence only ever saw attacks on
extractors, houses and workers — Expert lost the strip north of its home to
Easy at minute 11 and never answered. Every held territory whose last-tick
challenger is a hostile player now raises an incident at that side's unit
nearest the territory's seed, answered on the tier's delay and margin like a
raid.

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
  **Every group order, not only waves (2026-10-03):** any decision that
  sends two or more soldiers to one destination — raid parties, the
  first-Religion-Point curse hunt and its reinforcements, the reclaim squad,
  claim squads and curse assaults, the well assault — goes through
  `AICommon.IssueGroupOrder`, so it marches as one formation. A single unit
  (a scout, a worker, a hero, a lone reinforcement) keeps a per-unit order,
  and so does a unit already standing at the destination (within
  `waveArrivedRadius`), so a re-draft never re-slots arrivals. Paths that
  re-send a group periodically re-order only the members NOT already
  marching under the current order (newcomers, idle, or the target moved) —
  re-issuing a formation order re-plans the whole formation. Before this,
  traces measured those per-unit groups as a scattered stream (nearest
  neighbour p90 44 m) while formation members held ~2 m spacing.
- **Postures** (Develop / Pressure / Defend / Rebuild): kept as-is;
  Defend recalls missions and repairs; Rebuild rebuilds the army before
  re-engaging.

### 6a. What counts as available, and how big a wave has to be

Two rules that sound like implementation detail and are not — between them
they decided whether any wave ever left home at all.

**A wave's follow-up objective is a player's building** (2026-10-05). After
an objective falls, the army presses on only to a hostile PLAYER's building
or sighting. A curse node or structure is never a wave objective. Red's raid
on a stray eco building "pressed on" to two curse nodes 30 m away six times,
and fed 6-7 units into garrisons it never assessed. Curse nodes fall only to
the margin-checked curse paths (§ 4, § 5b, § 5i). With no player objective
left, the wave walks home (`WAVE: no player objective — returning`).

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

**The standing army is never drafted** (2026-10-05, per difficulty tier).
A tier with `standingArmyFloorFraction` keeps that share of its desired army
at home, capped at a third of the population ceiling like the bar. A wave,
and every reinforcement column after it, drafts only the standing army above
that floor; the units nearest home are the ones kept. The standing army is
every draftable combat unit not already on a mission roster (fighting at home
and walking included). The bar's "half the desired army" term becomes half
of the desired army above the floor, so a faster wave cadence spends the
surplus and never the army count. A tier with `waveMinArmyFraction` also
makes every wave at least that share of the standing army, so frequent waves
are real pushes and not feeder trickles. Normal and Easy keep neither (they
draft every idle unit, as before). Logged at each launch as `WAVE: standing
floor n kept (s standing at home, k idle held back; wave w, min m)`, and from
the reinforcement pass as `WAVE: standing floor n kept (k held back from
reinforcing wave w, s standing)`.

**Late waves launch on STRENGTH, not on a head count** (2026-10-04; replaced
the 2026-09-12 "full population past minute 25" rule). Before
`strengthWaveAfterSeconds` the rules above stand and waves go out at the scaled
bar. After it, a wave needs two things, and the overdue release overrides
neither:

- **A floor of bodies:** at least `strengthWaveMinArmy` idle soldiers, times
  the plan's `WaveBarScale` (clamped to a third of the population ceiling like
  the early bar), so a tiny army never trickles out at an undefended target.
- **A winning fight:** the idle army's power (AIEngagement scale) must reach
  `strengthWaveRatio` times the **best known defence** at the objective,
  scaled by the personality's `riskMultiplier` (the same appetite for risk the
  target scorer reads). The best known defence is the larger of the live read
  (hostile army plus static defences in the assess radius) and what the scouts
  reported there (the strongest garrison recorded around a sighted building,
  or the fresh mobile sightings, plus the static defences).

While held, the faction raises its desired army toward the size the defence
demands, so production grows into the fight instead of stalling. An army at
its population ceiling cannot grow further, so it launches with everything it
has -- the one piece of the old rule kept, as a release valve. Every hold is
logged with the numbers (strength, known defence, ratio). Values: `SimpleAISystem.asset`.

The full-population rule it replaced held late waves 221 times in one 60-minute
batch while banks piled up 34k supplies and 27k iron: a faction that could not
fill its cap simply stopped attacking. The intent is unchanged -- the late game
is decided by real pushes, not half-armies fed into a defended base -- but the
bar is now the fight, not the population counter.

**A mission that times out poisons its ground.** When an attack mission
expires without its objective falling, the 40 m cell it was aimed at goes on
that faction's blocked list for sixteen minutes and the target scorer skips
every sighting inside it. The alternative is what the AI did before: the
scorer is deterministic, so the same sighting wins again the moment the army
is free, and the wave commutes to unreachable ground for the rest of the
match. This is the third instance of one bug shape in this system -- an
action fails, nothing records the failure, and the next decision repeats it.
The claim planner's `_siteBlocked` is the same rule for build sites.

**No stalling for intel (2026-10-05).** A wave whose objective is on ground
nobody has seen still requests recon, but recon no longer gates it for
long: an objective at a **player start position** (within
`startHallKnownRadius` of a start marker — public knowledge) is marched on
at once, and any other unseen objective holds at most
`waveIntelHoldMaxSeconds`, then the army advances itself and fights what it
meets on the way, the curse included (the tactical layer engages it like any
other enemy). The scored-target recon gate is skipped for such a blind
advance. Yellow, the last strong side on SunderedCrown, held 193 units at
home logging "holding — no intel on (75,75)" every think because its scouts
could not reach the last enemy's start through the curse. Log: `WAVE:
advancing without intel on (x,z) after Ns (a start position: public
knowledge | recon timed out)`.

**The holdings outside the walls first; the march is on the clock; the
wall in the way (2026-10-05).** Three findings from Mirror Marches M1-M6:

- 45 of 60 wave objectives were "no sighting — marching on the nearest
  hostile start Hall", and every capital on a developed board stands behind
  150-240 wall pieces that the Wall Rule (Combat_Pacing.md) keeps infantry
  off. The wave stood under the towers until its clock ran out (87 units,
  480 s, 3 kills). The doctrine now sends a wave at the victim's nearest
  KNOWN eco or military building on ground that does not hold its capital
  (`IsWalledGround`) before it considers the Hall; the raze chain presses on
  from there. The Hall is the objective only when nothing else of the
  victim's is known. Sightings whose building is already razed, and ground a
  timed-out mission blacklisted, are skipped.
- `missionTimeoutSeconds` ran from launch, and on a 1024 m map the muster and
  march alone took 500-1500 s, so missions timed out on arrival. A mission's
  deadline is now the flat timeout plus the march at
  `missionMarchSpeedForTimeout`, re-armed at every chain.
- The wave bar read half of DesiredMilitary — 200 from the age-up on — and
  was clamped to a third of the population cap, so the tier that houses
  fastest had the highest bar: Expert's read 66-73 by minute 15 against
  Easy's 30, and Expert attacked at 22-25 minutes. A wave now also goes once
  `waveLiveArmyShare` of the LIVE army above the floor stands idle (never
  below the tier's base bar); the strength gate still judges whether that
  army is enough.
- The overdue release (a wave goes once it is `waveOverdueSeconds` late,
  whatever the odds) is capped: while the assault at the objective reads
  worse than `waveOverdueMaxRatio` against, the wave keeps holding and the
  army keeps growing. It had sent a 13-unit first wave into a base it read at
  2.3 to 1 and lost it.
- A striking army that stands still short of its objective for
  `wallBreachAfterSeconds` with a hostile wall piece within
  `wallBreachRadius` is stopped by that wall. With at least
  `wallBreachMinSiege` engines along, the engines are set on the nearest
  piece and the rest attack-move to it (the chain then presses on through
  the breach); with none, the mission ends at once instead of feeding the
  towers for the rest of its clock.

### 6b. Building spacing yields to being able to fight

The AI keeps roughly 20 m between its buildings so a base stays walkable.
That is a preference, and it now yields: when a placement scan finds no legal
site in its normal passes, it runs further passes with the centre spacing
dropped. Nothing else relaxes. Footprint overlap, resource-node clearance,
curse crust, territory ownership, the hall cap and the router's own validator
all still refuse the candidate.

**But it never packs buildings flush (2026-09-25, operator: "AI building
placement is too cramped").** Every pass also keeps an EDGE-TO-EDGE lane
between footprints, counted in 2 m build cells (docs/Design/Build_Grid.md):

| Pass | Centre spacing | Edge gap | Ring reach |
|------|----------------|----------|------------|
| normal (huts: covered-ground first) | 20 m (30 m hut-to-hut) | 2 cells = 4 m | 16-48 m from the Hall |
| loose | — | 2 cells = 4 m | same |
| last resort | — | 1 cell = 2 m | out to 52 m |

The last resort used to be spacing ZERO, which is what packed full bases wall
to wall. It now keeps at least one clear cell, so an army can always walk
between any two AI buildings. The endgame placers (smelters, sect buildings,
houses) use the same lane: 2 cells, falling back to 1, never 0. Wall pieces
are left out of the gap test (a diagonal curtain's bounding box is mostly
open ground) but never out of the overlap test.

Exempt: **extractors** (Gatherer's Hut, Mine, Veilstone Mine, Smelter) stand
on their node, and the node — map data — decides where they go; and the
**Hall** when it claims new ground, which is sited on the target region.

**Flush is allowed; sealing the base is not (2026-10-04, supersedes the lane
table above).** Operator direction: "allow buildings to sit flush with each
other but ensure AI doesn't lock itself up with buildings". Buildings may
touch (Build_Grid.md § Buildings may touch). The AI's centre spacing is gone
(`minBuildingSpacing` 0, so the loose pass is skipped). The normal pass
prefers a one-cell lane (`buildingGapCells` 1). The last resort places flush
(`relaxedBuildingGapCells` 0). Instead of a gap, every candidate that passes
every other rule gets the **self-lock check** (`AIBaseLayout`):

- The check is a bounded flood fill on the 2 m build grid. It runs in a
  square window of `sealWindowHalfCells` around the territory's core (its
  capital or Fortress, else its seed), starting from the cells around the
  core's footprint. Blocked cells come from the nav cost field (terrain,
  nodes, stamped buildings and curtain walls; a gate cell is walkable), plus
  unstamped sites, this think's own placements and the faction's own plans.
- Everything the base reached before the candidate, it must still reach
  after it. That means every building's last free side; a production
  building's **exit** (the cells east of it, where TrainingSystem walks a new
  unit out); every gate cell; and the way out, meaning the window's edge or
  another territory. A requirement that already failed before the candidate
  is not enforced, so a ring with no gate yet does not freeze building.
- No pocket: at most `sealPocketToleranceCells` open cells may be cut off
  from the base.
- The candidate itself keeps a free side and, as a production building, its
  exit.
- A **flush row** may hold at most `maxFlushClusterBuildings` buildings that
  touch edge to edge. The next building must leave a lane. Houses in their
  quarter are exempt.

The before-picture is cached per (faction, territory). It is rebuilt when
the building or plan set or the territory map changes, or after
`sealCacheSeconds`. Each check is one flood, run only on a candidate that
already passed every other rule, at most `sealChecksPerThink` per think;
past that budget the search is inconclusive, like the other budgets. The
endgame ring placers and the tower scan run the same check, for the faction
holding the ground. Extractors are exempt, because their node decides where
they stand. The AI site search also refuses a footprint over the faction's
own plans now, which the router refuses but the snapshot never listed.
**The border band applies in the home territory only**, because only the
home is walled (§ Walls). A province keeps no 5 m strip along its edge.
Log: `BUILD: rejected — would seal <what> (<building> at (x,z))`, at most
once per `sealLogInterval` per faction. The failed-search tally gains
`fortress-spot N, seal N`. Numbers: `SimpleAISystem.asset`.

**The wall follows the border (2026-09-30; WHICH border is narrowed by § Walls below, 2026-10-03).** On a map with territories the
Alanthor wall doctrine no longer plans a square: it casts one ray per 7.5°
out of the Fortress, finds where the faction's owned ground ends on each, and
plans the wall **4-6 build cells inside that border** (5 preferred; the band
lets a hub dodge blocked ground). A bearing where impassable terrain comes
before the border is left to the mountain. Hubs are resampled every 30 m into
one closed chain with up to four gates. The plan is **redrawn whenever the
walled territory changes** (§ Walls: the home territory only);
standing hubs stay. A hub nudge never leaves owned ground. The terrain-only
square/chokepoint plan below remains only for maps with no partition.
Numbers: `AIWallPlanner.asset` (borderBufferCellsMin/Preferred/Max,
borderScanMax, borderMinRadius).

**§ Walls — only the starting territory is walled (2026-10-03).** The AI
still tries to get one Fortress per territory (§5c Fortress expansion),
but only its STARTING (home) territory ever gets a wall. No other territory
is walled, Fortress or not. The ring is traced along the home territory's
border (the faction's other territories count as foreign ground), and the
plan is redrawn only when the home territory is lost or retaken — claiming
or losing any other ground moves no wall. At most
`maxWallPiecesPerTerritory` pieces (hubs + 3 m curtain modules) stand in one
territory; a lost hub or link is rebuilt at most `maxWallSlotRebuilds` times,
then left open; once a ring is closed (every slot hubbed, every link standing
or refused) nothing more is added. Walls yield to the army: every wall
purchase comes out of the Economy wallet without borrowing from the
Military one, and the doctrine spends nothing while military purchases are
being refused for supplies or iron. Numbers: `AIAlanthorEndgameSystem.asset`.
Why: the 0.0.33 60-minute batch ended with 2,803 stone wall pieces on
Veilmarch (one hub on a Hollow Table front was re-bought 94 times) while every
army sat far below its cap.

**§ Walls — the ring is reserved ground (2026-10-04).** The home ring is
planned as soon as the home territory is held — Age 0 included, by the same
deterministic border trace the wall doctrine draws later — for every faction
whose culture walls or is still unchosen. Every build cell within the wall's
half-depth (the hub radius at a hub) plus `corridorClearanceCells` of the
ring line is the **wall corridor**: no AI placer (the base placer, the
endgame ring scans, the tower scan) puts a footprint on it, and a base
building whose anchor lies inside the ring must stand wholly inside it.
Extractors are exempt — their node decides — and so the ring routes round
them instead. A curtain that is blocked anyway (an extractor, a building
that predates the rule, a node) is laid round the blocker: the straight run
first, then a bulge either side, then a grid search inside own ground
(longest allowed: `wallRerouteMaxLengthFactor` x the straight distance), each
checked with the executor's own line tests. Logs: `WALL: corridor reserved
(N cells, M of them the wall's own)`, `BUILD: placement rejected — on wall
corridor (<building>, n candidate(s); search failed)`, `WALL: rerouted
around <building> at (x,z)`.

**The corridor is narrow, and small or urgent buildings may use its
walkway (2026-10-04).** Headless27 logged `placement rejected — on wall
corridor` 1,860 times, and Headless28 logged it 1,913 times. Most came from
searches that failed outright. The corridor was the largest single refusal
in them, a median 39% of their candidates. Second and later production
buildings stayed unplaced for the rest of the match, mostly on Twin Spans,
Sundered Crown and Hollow Table. So:

- `corridorClearanceCells` is 1 (it was 2).
- The corridor has two parts: the wall's **own cells** (within its
  half-depth, or the hub radius at a hub) and the **walkway** beside them. A
  footprint of at most `corridorSmallFootprintCells` build cells (a Hut or a
  Watch Tower) is refused only on the wall's own cells. So is the
  lost-sole-trainer rebuild (§ 6c). The curtain router winds past such
  buildings.
- **The placer searches inward first.** Its rings already grow outward from
  the anchor. When the anchor is inside the ring, a bearing whose candidate
  has reached the corridor or left the ring is dropped for the rest of that
  pass, and the skipped candidates cost no search budget. The refusal tally
  reports them as `past-ring`.
- The rejection line is written only when the whole search failed. It used
  to be written on success too, so it could not say whether the corridor had
  cost a building anything. Why: SunderedCrown Blue (0.0.33 headless batch)
left its west ring open — the border band kept building EDGES 5 m from the
border while the wall's centre line runs 4 m inside it, so a Barracks, two
Archery Ranges and a Vault stood on the line, and the one-bulge detour could
not wind past two of them ("blocked straight and round both sides; left
open"). Numbers: `AIWallPlanner.asset`.

**§ Walls — gates (2026-10-04).** A ring the army cannot leave is a bug.
Gates are conversion-only from a wall module (the Gate set in
`Age0/Buildings/Wall/Gate/`; owner-only passable), and the AI cuts them at
the ring's EXITS: where the ring faces each neighbouring territory — its own
held territories first, then the longest shared borders — then the planned
gate slots, spaced `gateSiteSpacing` apart, at least `minGatesPerRing` and at
most `maxGatesPerRing` per ring. Each gate is cut at the module nearest its
exit that can take one (a clear run of `AlanthorWall.FreeRunForGate`
modules), as soon as finished curtain stands there; an order the executor
refused is never re-issued for the same module. The gate pass runs FIRST in
the wall think and also while the doctrine is otherwise held (Age-2 ladder,
savings hold, army short), and the gates up to the minimum are paid from the
bank alone, like an essential. Logs: `WALL: gate converted at (x,z) (exit
toward <territory>)`, `WALL: ring closed with N gate(s)`, and `WALL: WARNING
ring closed with 0 gates` (followed at once by a conversion). Why: the old
pass converted only a gate-flagged segment's MIDDLE module and ran last; when
that module could not take a gate the executor refused silently and the AI
re-issued it every think forever, so good rings shipped with no gate and the
army was sealed in. Numbers: `AIWallPlanner.asset`.

**Inside the walls.** When the Alanthor wall doctrine has planned a PERIMETER
wall around the base (AIWallPlanner, planned once), every later base building
must fit inside the planned rectangle with 4 m to spare, so wider spacing can
never push the layout out through the wall line. The perimeter itself is now
sized from the placer's reach — the last-resort ring (52 m) plus half the
widest footprint (5 m) plus that 4 m clearance, i.e. at least 61 m half-extent
(capped at 68 m) and centred so the Hall's whole build disc fits — instead of
from whatever stood when the plan was drawn. Chokepoint plans seal corridors
rather than enclose a box, and bound nothing. All numbers are data:
`SimpleAISystem.asset` (buildingGapCells, relaxedBuildingGapCells,
relaxedBuildRingDistanceMax, wallInteriorClearance) and `AIWallPlanner.asset`
(perimeterHalfExtentMin/Max, perimeterFootprintAllowance).

Why this is not optional. Hollow Table 1v1, 2026-09-12: at minute 16 Red held
24 buildings, 13,166 iron and 11,315 veilstone, and fielded ONE unit. It had
no Barracks and no Archery Range, because it had filled its only territory
with huts and mines, and all 216 candidate sites were refused -- 201 of them
on spacing. Blue, on the other side of the same match, had eight units.
Neither faction ever attacked. This is upstream of every wave rule in 6a: an
army that was never trainable cannot be mustered, however good the muster is.

### 6d. What a think costs (2026-09-25 performance pass)

The brain's decisions are unchanged; what they cost is bounded.

- **Strength reads are a five-second picture.** Every "how strong is it
  here" question (retreat checks, target scoring, focus fire, posture, the
  garrison tally on a sighted building, idle form-up) reads `AIStrengthMap`:
  every unit and building walked a slice per frame so one full refresh takes
  ~5 s, bucketed into a 16 m spatial hash and swapped in whole (readers never
  see a half-built picture). Operator direction: "It's not a problem if info
  is 5 s out of date." Focus fire takes its *candidates* from the picture but
  scores them on live health and position.
- **Site searches read one snapshot per tick** (`BuildSiteSnapshot`) instead of
  copying every building and obstacle per candidate, run their cheap gates
  (savings hold, crew, open sites) before searching, spend a per-think
  candidate budget, and remember a failed search per (faction, building,
  place) for 10 s — forgotten early when a building is razed or territory
  changes hands.
- **One heavy think per frame** across all brains and the endgame systems
  (`AIThinkBudget`), most overdue first; a brain a whole interval late thinks
  regardless, so a crowded match slows the AI down but never starves one.
- **Counts are memoised per think** (army, crew, building counts, the
  composition roster) and recomputed after each order the brain issues.
- Mobile sightings not re-seen for 180 s are dropped; structures persist until
  they die.

## 6c. The production snowball

**Saturated production is a build order** (revised 2026-10-04, § 5g). The
AI raises another production building only when its existing production is
saturated (busy queues across `productionSaturationThreshold` of its
trainers, held for `productionSaturationSeconds`) and the army is below its
target, and the new building is the line the army plan needs most. Each
province's own one (§ 5g step 4) is the baseline. The text below is the
earlier per-line form of the rule; its targets now sit under that gate.

**Every line trains** (2026-10-05). The army floor orders its deficit
through the composition's pick, which is the plan's most-behind role. When
the pick's own trainer refuses, the rest of the deficit goes to the plan's
other rows, most-behind first, each into its own trainers. A trainer refuses
when its queue is full, it is missing or still building, or it is below the
level the unit needs. The basics cap still holds. Without a plan, the rest
goes to every trainable combat unit. A budget refusal does not spread: that
is the army saving for its role unit. Why: an Expert Blue (queue depth 1)
spent the last ten minutes of a match logging "floor blocked: deficit 112 x
Alanthor_Ballista — trainer queue full (depth 1)". It had "1 buildings
training (of 7)", an army of 80 of 200, and a bank of 99,860 iron and 21,820
supplies. One single-trainer role held the whole army program. Low
saturation followed from that too, so no extra production was ever added.
Logged as `MILITARY: X blocked (...) — n unit(s) trained on other lines`.

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
  a sixth however rich it was. The crew is now the faction's alive workers
  (open-site cap `max(2, workers)`, `SimpleAISystem.Building`), and the
  worker count follows the § 4 rule, so it grows with the territory.
- **The savings hold.** A production line whose queues are all full is an
  essential purchase and spends past the claim reservation, exactly as housing
  and the first of each line already do.

**A lost sole trainer is rebuilt first** (2026-10-04). A production line the
faction once had and now has none of -- no finished building, no site, no
plan -- is replaced at the top of the think, before the age-up director and the
economy tick spend the bank: bank-direct (not through a wallet), past the
savings hold, and one site past the open-site cap. The Barracks always, since
it hosts the basics and the army floor; the Archery Range, Royal Stable and
Siege Yard when the composition plan still wants a unit that building trains
(by the building SO's `trains[]`). A line never owned is the job of the province
step and the saturation rule (§ 5g), not this one. The rebuild passes the
saturation gate. After a placement the line waits
`lostTrainerRetrySeconds` before it may place again, because a lockstep
placement lands two ticks later. The goal list did already ask for the
Barracks, but it runs last in the think and through the Military wallet: one
60-minute batch logged "deficit N x Spearman — no trainer" 67 times.

**What still blocked the rebuild, and the answer to each (2026-10-04).**
Headless28 logged 160 `lost sole trainer` refusals (73 in Headless27).

- **No capital: 92 refusals.** Every one was a faction whose Fortress had
  already fallen, so it was eliminated in all but name. The rebuild stops,
  says so once (`rebuilds suspended — no capital standing`) and is quiet
  until a capital stands.
- **Bank short: 41.** Hollow Table Red spent 31 minutes without a Barracks
  over six gaps. Its Fortress stood and it had 7,000 iron, but its supplies
  sat at 20-200 against a 220-supply Barracks, because every other spender
  drained them between thinks. The refusal now arms a **strict savings
  reserve** for the line's price (`AIPivotalReserve`, key
  `LostTrainer:<id>`, when `lostTrainerSaveStrict`). Discretionary spenders
  hold until the price is banked: buildings, research, building levels, and
  the army past its essentials.
- **No legal spot: 18.** For Sundered Crown Yellow's Siege Yard, the wall
  band and the corridor refused 517 of 672 candidates, and the yard was gone
  for the last 23 minutes. The rebuild's search is kept off the wall's own
  cells only (the walkway yields, § Walls) and skips the failed-search
  memory. A failure waits `lostTrainerSearchRetrySeconds`. The search is
  still anchored on the capital, so the site is the nearest legal ground
  inside the ring.
- **No build crew: 5. Too many open sites: 1** (`38 sites open, crew 3`).
  With no crew, the rebuild trains a Worker. The open-site cap no longer
  applies to the rebuild. When no worker is idle, the site is queued as the
  next job of the two nearest busy workers (`AICommon.PullWorkersTo`), so it
  is never orphaned.

**Redundancy** is the home production floor (§ 5g step 4): from
`homeProductionFloorAfterSeconds` the home keeps the tier's `homeProductionPerLine` of
every line it can build, so a single loss never zeroes the army line. The
faction-wide redundant-Barracks floor that stood here is folded into it.

**The army floor never asks for a unit nothing can train.** The floor runs
first in the think, on the previous think's pick. When that unit's building
had just been razed, it asked anyway, then fell back to a Spearman whose
Barracks was gone too. Headless28 logged 123 `floor unit X has no trainer`
lines and 72 `floor blocked ... no trainer` lines. The floor now re-picks
from what can be trained. With nothing trainable it pauses, writing `army
floor paused — no standing trainer for any combat unit` once per
`noTrainerLogInterval`. It falls back to a Spearman only when something can
train one.

**Logs:** `lost sole trainer: <id> gone — rebuilding first (...; Ns without
one)`, `... rebuild refused (<reason>; Ns without one) — saving for it
(strict reserve) | training a Worker for it`, `lost sole trainer: <id> line
restored after Ns`, `lost sole trainer: <id> site queued on N busy
worker(s)`. Numbers: `SimpleAISystem.asset` (`lostTrainerRetrySeconds`,
`lostTrainerSaveStrict`, `lostTrainerSearchRetrySeconds`,
`homeProductionFloorAfterSeconds`; `homeProductionPerLine` is in the
difficulty profiles,
`noTrainerLogInterval`).

Measured before this rule, Hollow Table 2026-09-12: Blue held ONE Barracks
with a five-slot queue against a deficit of twenty Spearmen, 20,505 iron and
2,896 supplies in its military budget. Nine times the log recorded five sites
open against a crew of five. Eighteen buildings stood and exactly one of them
trained soldiers. Its army was four units at minute 29.

**Strategy still chooses the ORDER.** Which line saturates first, and which
research is bought before which, remains the military strategy's decision and
may change over the match. The snowball rule only says that a saturated line
gets another building; it never says which line to open first.

## 6e. Tactics: what an army does in a fight (2026-10-04)

The developer's diagnosis: an Expert AI should win fights when outnumbered
through kiting, flanking and clever use of abilities, and did none of these;
it fired abilities anywhere the moment they came off cooldown; it did not
prioritise targets that are good against its composition. Rules:

- **Target priority (counter-aware focus).** An engaged army scores every
  hostile unit in contact by danger, how nearly dead it is, whether it is
  high value (hero, siege, healer, caster) and its COUNTER relationship to
  this army — the enemy's `bonusVsTags` against our units' tags (it counters
  us: kill it first) and ours against its tags (we counter it). Tags and
  bonuses are the unit SOs' own. The army keeps a short list of the best few
  and each member takes the best of THAT list by its own counter edge and
  distance, so the army still concentrates while spearmen take the horses.
  A member keeps its target unless the new pick is clearly better.
- **Ranged behind melee.** A shooter only takes a target it can reach from
  where it stands; otherwise it holds a firing line behind the melee front
  instead of walking through its own front rank.
- **Kiting.** A ranged unit that has just fired (reloading) with hostile melee
  closing inside the trigger band, and the legs to open the gap, takes one
  step straight back, then fires again. Bounded by a drift allowance from
  where its kiting began, so the stance leash and the army's cohesion still
  rule. Siege and emplaced engines never kite.
- **Flanking.** When a fight opens against enough enemies, a share of the
  army's FAST melee (cavalry, or clearly faster than the army) swings round
  the enemy's lighter wing to a point beside and behind its line, then
  strikes the nearest bodies from there — the side / rear the flanking damage
  rule pays for. They stay on that target until it dies and rejoin the army
  when the fight ends. Heroes never flank.
- **Fall back and regroup.** An engaged army reading live enemy power above
  its tier's ratio over its own (towers counted on both sides) walks back as
  one formation to the nearest friendly tower / Fortress that is not deeper
  into the enemy, else toward the capital, and turns round when the odds there
  drop to its re-engage ratio (or the enemy did not follow). Still outmatched
  after the timeout, it falls back to the capital. Never at home (base defence
  owns that) and never for a handful of units. The old go-home-and-disband
  retreat stays as the last resort.
- **Abilities wait for value (AI factions only — players keep their own
  casting).** By effect, never "it is off cooldown": area damage / control
  only when at least N enemies are inside it (N per tier); heals only when
  enough of the allied HP pool in the circle is missing; ally buffs only with
  enough allies affected and an enemy inside the radius or about to be;
  self-defence (Liquid Courage) only in combat when hurt, out-powered or
  swarmed; escapes (Full Gallop) only in contact when hurt or out-powered;
  ultimates (Honour thy Pledge, Death Ward, Invulnerable) only when the fight
  is pivotal (enemy power at or past the tier's pivotal ratio over ours).
  Sect powers aim only at FIGHTS — the ones the armies report, plus the base
  while it is under attack — and Bulwark / Cleanse only while the base is.
  Two casters never spend the same area card over the same ground at once.
  A held ability logs why.

Per-tier values: the `tactics` block on the four difficulty profiles (table
in §2). Shared geometry and weights: `AITactics.asset`. Code:
`SimpleAISystem.Tactics.cs` (army: focus, ranged line, flank, fall back),
`AITactics/AITacticsMicroSystem.cs` (kiting, unit abilities),
`AIAlanthorEndgameSystem.Sects.cs` (sect powers), `AITactics/AITactics.cs`
(scoring, live power, fight sites). Logs: `TACTICS: kite n units`,
`TACTICS: flank group n -> angle`, `TACTICS: retreat (power a vs b)`,
`TACTICS: re-engage`, `TARGET: focus <unit> (counter|finish|high value|danger)`,
`ABILITY: <id> cast (k enemies)`, `ABILITY: <id> held (reason)` (rate-limited).

## 6f. Many armies, many directions, the enemy's income (2026-10-05)

**Operator directive:** "Harder levels should sport many coordinated armies
instead of a single one. Attacking in multiple directions and going after
enemies' resource generation instead of blindly going for resources."

Until now every tier fought the same way: one wave, one blob, one objective
— the nearest known thing of the victim, or its capital. The higher tiers
differed in cadence and in-fight skill only. Two rules change that, both on
the difficulty profile so Easy and Normal keep the single blob:

**The enemy's income is the objective (`incomeTargeting`: Hard, Expert).**
Income is territory, and an extractor on a held slot is where it is made
(Veilstone_Economy.md §5). An income-targeting wave ranks every KNOWN
hostile building outside its owner's walls by what it is worth to the
enemy — extractors (Gatherer's Hut, Mine, Veilstone Mine, Trading Outpost)
first, military buildings next, houses and the rest last; the weights are
`incomeWeight*` on `SimpleAISystem.asset`, charged for distance, the
garrison seen there and the age of the report at the target scorer's
rates — and marches on the best of them. The victim is still the board
leader (or any hostile when nobody leads); the opportunity hijack and the
LATE finishing doctrine (past `closeoutAfterSeconds`, §6a) are unchanged,
because the first is already an income strike and the second must end the
match. The hostile-COUNT closeout ("a duel is always the closeout") does not
suppress it: a 1v1 is closeout from the first second, and the first v15
smoke match had Expert marching blind on the start Hall for 25 minutes with
the doctrine never firing. Razed, walled, blacklisted and unseen ground is
never ranked. The log says `income: <victim>'s <what>`.

**The scouts go where the income is.** The zone director explores outward
from home, so in that same smoke match Expert's scouts spent nine minutes
in its own corner and reported the first enemy eco building at minute 18 —
an income-targeting tier cannot hit what it has not seen. While fewer than
`incomeReconMinKnown` hostile income buildings are known, such a tier files
a recon request every `incomeReconIntervalSeconds` at the nearest hostile
START position (public knowledge) and then at points
`incomeReconSpreadMeters` around it — toward home first, then the flanks,
then the far side — which the scout director serves before exploration
(§7). Log: `SCOUT: income recon: n known — scouting the enemy's holdings at …`.

**Many armies strike together from many sides (`concurrentArmies`: Hard 2,
Expert 3).** A wave on such a tier splits its draft into that many bodies.
The main army takes the objective chosen above; each sister army takes a
further income objective of the same victim whose approach bearing — read
from the victim's capital when the scouts have reported it, from home
otherwise — is at least `armySeparationDegrees` from every objective
already chosen and at least `armySeparationMeters` away from it, and which
the army's share could take (`AIEngagement.AssessAssault` — never waived:
an overdue wave that cannot afford sisters goes as one blob). The units nearest each objective form its army. Fewer
armies launch when the draft cannot give each `armyMinUnits`, or when no
second objective separates enough from the first — never two armies at one
spot. (The bearing bar is deliberately low and the distance bar is the real
one: on a mirrored 1v1 every holding of the enemy lies in the band between
the two bases, so two targets 300 m apart read only 14° apart from its
capital.) "Outside its walls" means wall pieces of the owner actually stand
in the territory — the capital's territory is not walled by definition, or
on a start-territory economy nothing is ever reachable. Each army musters, marches to a stage point on its own line and then
HOLDS there until every sister has staged (at most `armySyncTimeoutSeconds`
after the first), so the victim is hit from every direction at once; from
the strike on, each army runs its own lifecycle (raze chain, breach,
retreat, timeout) exactly as a lone army does.

**What stays single.** The reinforcement stream still follows ONE
objective — the main army's; a sister's raze chain never repoints it, and
only a reinforcement column (never a sister army mustering nearby) is
absorbed into an army. The wave cadence, the bar, the standing floor and the
strength gate are unchanged: a many-army wave is the same draft divided,
not a bigger one. Code: `PickSisterTargets`, `RankIncomeTargets`,
`DispatchArmy`, `GroupReadyToStrike`, `OwnsWaveTarget` in
`SimpleAISystem.Military.cs`. Logs: `wave of N armies against <victim>: …`,
`main army: objective …`, `sister army: objective …`, `staged at … —
waiting for the sister armies`.

## 6g. The drawn base: main camp and outposts (2026-10-06)

**Developer directive:** the AI builds its bases to a drawn layout — a **main
camp** round the home Fortress and a smaller **outpost** round every other
Fortress it raises. Both layouts are character grids in
`Assets/GameSystems/AI/AIBaseTemplate/AIBaseTemplate.asset`, one character per
2 m build cell, north up, anchored on the Fortress block (`F`):

| Symbol | Slot |
|---|---|
| `F` | the Fortress (the anchor) |
| `H` | House (the main camp holds 20) |
| `B` `A` `Y` `S` | production: Barracks, Archery Range, Royal Stable, Siege Yard |
| `P` | Temple of Ridan |
| `V` | Vault of Almiérra |
| `T` | Watch Tower |
| `W` | wall hub — the hubs form ONE closed ring |

The outpost has no Houses, Temple or Vault: production, towers and its ring.
Which building a symbol stands for is the asset's `legend`; a symbol's block
is tiled into as many footprints of its building as fit (a 6 x 6 House block is
nine Houses).

**The rules:**

1. **A slotted building takes the next free slot of its kind**, nearest the
   Fortress first. A production building whose own slots are all used may take
   a free slot of another production kind. Only when every slot it may take is
   used does it fall back to the ordinary site search (§ 6b), which keeps off
   the free slots the way it keeps off the wall corridor.
2. **A slot that cannot be used as drawn moves to the nearest legal spot**
   within `slotSearchRadiusCells` (something already stands there, a node, a
   cliff, cursed ground) and is remembered there, so the next building of
   that kind moves on to the next slot. A moved slot also keeps off the wall
   corridor, the other free slots and the reserved Fortress spots, and must
   not seal the base.
3. **The wall is the drawn ring**, for the HOME territory only (secondary
   bases are not walled — 2026-10-07, developer directive; the outpost layout
   carries no hubs; a Turtle's frontier walls and wider ring are the
   personality exceptions, § 3b). "Home" is the territory of the faction's FIRST capital
   this match: when that capital falls and a secondary Fortress becomes the
   capital, its territory is still not walled: the hubs in bearing order,
   closed, with a gate on the link nearest each of `gatesPerRing` bearings
   (north first). The executor places, nudges and links the hubs as before
   (§ Walls), so a hub that cannot stand where drawn moves to the nearest
   spot, and a blocked link is routed round its blocker — the ring is closed.
4. **Emplacements on the wall.** Every `emplacementEveryNthLink`-th non-gate
   link carries an engine on the module nearest its midpoint — a Ballista,
   alternating with a Trebuchet (`alternateTrebuchet`) where the wall level
   allows one (Age_1_Alanthor.md § What a module may become). Mounted after
   the ring is closed and its towers converted, one per think.

5. **No two towns alike (`variants`).** Each base is drawn in one of the
   eight orientations of its grid (a quarter turn 0-3 times, mirrored or not)
   and its production blocks are dealt out to the four production buildings in
   one of 24 orders. A main camp's orientation steps through the eight by
   faction from the match seed, so no two factions share one in a match; an
   outpost's comes from a hash of the seed, faction and territory. Every peer
   draws the same town.
6. **The opening agrees with the layout.** Every faction's starting House
   stands on its main camp's nearest House slot (in its variant), not on the
   old diagonal, which remains the fallback when that slot is illegal.

`enabled: 0` on the asset restores the ring search and the border-traced wall.
Logs: `base layout: <id> slot <n> moved <d> cell(s) …`, `Alanthor walls: plan
= along the home territory border (… drawn main camp ring, 12 hubs …)`,
`Alanthor walls: ballista emplacement at (x,z)`.

## 6h. Retreat to safe ground, regroup, and the holdings before the base (2026-10-07)

**Developer report:** "AI never finished walling off. AI retreats to main
base, always. It should either retreat to nearest safe location, create a
staging area and gather more soldiers or change target to a nearby
undefended territory/resource building. AI should aim to neutralize enemy
economic buildings in surrounding territories before going for the main
base." Measured in the 2026-10-06 92-minute Mirror Marches match: every
retreat logged "fall back toward the capital", and every AI held its walls
for the whole game ("the army is short of supplies or iron"), raising at most
3 of 12 hubs.

1. **The drawn ring builds whenever the bank can pay.** A ring drawn by the
   base layout (§ 6g) is part of the base: the army-short hold, the
   personality's wall hold and the Economy-wallet check do not apply to it;
   only what the bank actually holds does. Nor do the endgame's two outer
   gates — the Age-2 ladder (an AI with no Religion Point never finishes its
   Temple, so the ladder stayed "busy" all match and the ring stopped at 3
   hubs in the 2026-10-07 batch) and the pivotal savings hold.
2. **Retreat goes to the nearest SAFE ground, never home by default.** Safe
   ground is an own capital, Fortress or Watch Tower, or the centre of a held
   territory, within `fallbackStageSearch`, no nearer the enemy than the army
   is, with hostile strength round it of at most `fallbackSafeShare` of the
   army's. With none, a staging point `fallbackDistance` straight away from
   the enemy. Both retreat paths (the tactical fall-back and the per-mission
   retreat) use it, and the mission is KEPT, not dropped.
3. **The staging ground gathers reinforcements.** While the main army falls
   back, the reinforcement stream (`WaveTarget`) points at its staging ground;
   when it moves on, the stream follows the objective again.
4. **At the staging ground:** re-engage once the odds drop to
   `reengageRatio`; still followed and losing, fall back again to the next
   safe ground; otherwise **retarget** to the nearest known enemy economic
   building within `fallbackRetargetRadius` whose surroundings hold at most
   `fallbackRetargetShare` of the army's strength (outside capital
   territories first); otherwise hold, up to `fallbackMaxHolds` times
   `fallbackTimeout`, then give the objective up at the nearest safe own
   ground.
5. **A finished mission does not walk home.** An attack whose objective is
   down takes the next soft economic target near it; any ending army holds
   the nearest safe own ground (the capital only when nothing nearer serves).
6. **The surrounding holdings before the main base.** Income targeting runs
   on every tier now (was Hard / Expert); a target in its owner's capital
   territory scores `incomeCapitalPenalty` below every other; the raze chain
   takes the next known income building outside a capital territory before
   any Hall or military sighting.

Logs: `TACTICS: retreat … fall back to a Fortress/to a tower/into own
territory/to a staging point`, `holding the staging ground … (n/N)`,
`retarget …`, `objective given up …`, `WAVE: site empty — the next income
building …`, `objective done — on to …`, `mission over — … hold …`.

## 6i. One war at a time (2026-10-07)

**Developer report:** "Blue (southwest) attacks Red to the east and defeats
its forces, then abandons the fight and marches diagonally to attack another
faction. If it kept pressing it would have defeated Red." Measured in the
2026-10-07 batch: the closeout doctrine re-picked "the weakest" on every
wave (Red, then Green at 26:26 and back to Red), and the § 6h retarget took
any hostile's building, so Blue's armies hit a third faction's economy.

**The rule.** A faction that picks a victim keeps it. The first wave's
objective owner (or the doctrine's pick) starts the WAR; from then on every
wave, opportunity strike, raze-chain step and fall-back retarget stays on
that victim until it is out of the game (no capital) or `warMaxFailures`
attacks on it in a row have failed (a mission timed out, or an army gave
its objective up). A razed objective resets the count. Then a new victim may
be picked.

Logs: `WAR: war on <faction> — every wave stays on it until it falls`,
`WAR: war on <faction> over — … out of the game`, `WAR: war on <faction>
abandoned after N failed attack(s)`.

**Finding the victim, finishing the victim (2026-10-07, 8-player batch).**
- *The victim's ground.* With no sighting of the war victim a wave used to
  march on "the nearest hostile start Hall" -- any hostile's, often a dead
  faction's start someone else now held -- found nothing and walked home
  (a third to a half of all launches). It now marches on the victim's OWN
  start Hall, else on the victim's nearest held territory: territory
  ownership is public, and whatever holds the ground is fought there. An
  attack whose objective fell with nothing scouted near it presses on to
  the victim's next held territory instead of going home.
- *Go for the throat.* In the closeout, a victim holding
  `crippledTerritories` or fewer is beaten: the wave skips its outlying
  buildings (what it keeps rebuilding) and goes for its Hall and the
  lifelines round it. Seven AIs at war with one beaten faction had spent an
  hour razing the huts it rebuilt outside its walls.

## 6j. The army grows with the bank; the pile-on (2026-10-07)

**Why:** in the 2026-10-07 batch every survivor reached the tier's
`sustainArmyCap` by minute 30 and stopped, while supplies and iron sat at the
100,000 bank cap from minute 45: four equal armies, one-on-one wars that never
ended, one elimination per 90-minute match, and only where two AIs happened to
attack the same faction.

1. **The army cap rises with unspent money** (developer: "army cap raises if
   there are 1000 resources unspent"). Every `armyCapRaiseInterval` seconds a
   faction whose unspent supplies + iron are at least `armyCapRaiseThreshold`
   gets `armyCapRaiseStep` more cap, up to the population ceiling; the raise
   is kept. A richer economy now fields a bigger army.
2. **The pile-on** (extends § 6i). A hostile faction whose board score is at
   most `pileOnLosingShare` of its attacker's is LOSING its war; every other
   faction drops its own war and joins against it, so wars end two-on-one
   instead of cycling. In a one-on-one there is no third party: the pile-on
   never fires, and the duel is decided by point 1 — the economy that can
   keep buying past the old cap.

## 6k. Armies march round the curse (2026-10-07)

**Developer report:** "AI is marching through cursed territories and losing
their army to the DOT." Cursed ground is a radius (`nodeAuraRadius`) round
every living curse node, and exposure there turns into damage over time
(VeilExposureSystem). The nav layer takes the shortest way, so diagonal
marches on Mirror Marches crossed the curse at the centre of the map.

**The rule.** A long march (muster to stage, the raze chain, a retreat, a
retarget, a resumed march after a fight) whose straight line passes within
`nodeAuraRadius` + `marginMeters` (AICurseRoute.asset) of a curse node is
replaced by waypoints round it: A* on a `cellMeters` grid whose blocked cells
are those circles, string-pulled to the fewest legs. The army walks the legs
as its centre reaches each one (`arriveMeters`). A stage or rally point inside
cursed ground is pushed out of it; an objective standing in it is not (the
army still goes there, by the shortest cursed path that remains). An army in a
fight drops its route; the tactics layer re-routes it when the march resumes.
Logs: `ROUTE: N march round the curse to (x,z) — k waypoint(s)`.

## 6l. The Shardroot and the king (2026-10-07)

Developer: "No one goes for the shardroot and no one uses king lexor's
heightened form." Nothing in the AI looked at the artifact; units took it only
when a wave happened to fight on top of it, and the wave marched the carrier
on until it died. The Shardbound King (Lexor bearing the artifact) is Lexor's
only heightened form, so both halves were one gap.
`SimpleAISystem.Shardroot.cs`, knobs on `AIShardroot.asset`, every
`thinkInterval`:

1. **Ours** — the carrier leaves every mission and goes home by the road its
   personality takes (the choice of
   [Curse_And_Shardroot.md § 3.1b](Curse_And_Shardroot.md)), the
   personality row's `shardrootToKing` on `AISettings.asset`:
   - **The King** (Rush, Aggressive, Balanced) — the nearest own Hall, which
     hands the artifact to the living king (he becomes the Shardbound King).
   - **The Temple** (Turtle, Defensive, Economic, TechBoom) — the nearest
     finished Temple of Ridan, which enshrines it and empowers the army.
   A closed road (no living king; no finished Temple) gives way to the
   other; with neither, the Hall (the placeholder champion). The road is
   written on the carrier (`ShardrootBearer.Intent`), and the other
   building's delivery ignores a carrier bound elsewhere, so a courier walking
   to the Temple past its Hall is not handed to the king on the way, and the
   reverse. A player's courier carries no intent and delivers at whichever of
   its own Hall or Temple it reaches first. If the king already holds it as
   the Shardbound King, nothing more to do.
2. **On the ground, or held by an enemy or the curse, where we have seen it**
   — held means a carrier, a Shardbound King, **or a Temple with the artifact
   enshrined**: everyone targets the bearer (§ 3.1b), so an enemy's
   enshrining Temple is a strike target like a carrier, at the Temple.
   Not before `earliestSeconds`, a strike party of free combat units (the
   first-RP hunt's eligibility rules), at least `minPartySize`, goes only when
   its power beats what stands within `assessRadius` by `launchMargin`;
   otherwise the army target rises toward what it needs. A strike is
   reinforced for `reinforceSeconds`. With `kingLeadsStrike` the king goes too
   and is walked onto a pickup himself: a hero ordered onto it has right of way
   over his escort, so he carries it at once.
3. **Otherwise** (`kingJoinsArmy`) an idle king rides with the biggest mission
   of at least `kingJoinMinArmy`, so his fight abilities meet fights instead
   of holding "not in combat" at home.
4. **The king retreats (2026-10-09).** Developer: "AI needs to retreat king
   lexor when he is close to dying because they sacrifice him too eagerly and
   lose the shardroot." Checked every AI think (not the slower Shardroot
   cadence): at `kingRetreatHpFraction` of his health the king leaves every
   mission and walks to the nearest own Hall, re-ordered if anything turned him
   round, and neither joins the army nor leads a strike until he has healed to
   `kingRecoveredHpFraction`. Shardbound or not — a dead king costs a revival
   either way, and a Shardbound one drops the artifact.

Logs: `SHARDROOT: …`.

## 6m. Litharchs heal the army (2026-10-07)

Developer: "AI does not use Litharchs as healers." Three gates: the Temple was
never placed (the age-2 ladder's ring search refused every free slot of the
drawn base layout, the Temple's own `P` slot included — it now takes the drawn
slot first, as towers do, and logs a failed placement once a minute as
`BUILDING: X not placed: why`); the composition layer names no Support unit;
and every draft site takes combat classes only. `SimpleAISystem.Support.cs`,
knobs on `AISupport.asset`:

- **Train** — with a finished Temple, one healer per `combatUnitsPerHealer`
  combat units, at most `maxHealers`, one in the queue at a time.
- **Follow** — a healer farther than `followDistance` from the biggest mission
  (at least `minArmyToFollow`) walks to `followBehind` metres behind its centre,
  on the home side. Inside that distance it is left alone: its own auto-heal
  search does the healing, and a move order would cancel it.

The Temple fix also unblocks the sect layer: adoption, sect buildings and
`TryFireSectPowers` (§ 7b) all waited on a standing Temple.
Logs: `SUPPORT: …`.

## 6n. The late-game all-in (2026-10-08)

**Superseded in timing (2026-10-09):** developer: "I need deaths to start occurring from minute 15 or even earlier", with ~45-minute 8-player matches. The late game now comes earlier (the minute is `allInAfterSeconds` on `SimpleAISystem.asset` and `nodeLifetimeMinutes` on `TerritoryIncomeSystem.asset`), and stalled factions are hunted before it (§ 6o). Read "minute 30" below as "the late game".


Developer: "Late game comes at 30 minutes; from there onward players are
expected to start falling." — "AI is too conservative with troops, I expect
70-90% army commitment on late game." — "Maybe the AI needs to commit more
fully to attacking, even if it means it will die if caught overextended."
Measured in the 2026-10-08 8-player batch: a wave took about a third of the
army; the rest stood as the standing floor, wall guards, claim squads and held
reinforcements, and an army fell back the moment a fight turned.

**The doctrine.** From `allInAfterSeconds` of match time
(`SimpleAISystem.asset`) every AI goes all-in. It arms once per match and logs
`ALL-IN: armed at Ns — commit X% of the army, home guard N`. From then on:

1. **The standing floor (§ 6a) becomes a home guard**: the share of the army
   the personality does NOT commit, never fewer than `allInHomeGuardMinUnits`.
   The commitment is the personality row's `allInCommitment` on
   `AISettings.asset` — a structural knob, taken from the row at every tier:
   Rush and Aggressive commit the most, Balanced, Economic and TechBoom less,
   Defensive and Turtle the least (the values are on the asset, within the
   developer's 70-90% band).
2. **The wave takes everything idle above the home guard.** The head-count bar
   drops to `allInMinWaveUnits`, the "wave is at least a share of the army"
   bar is off, claims no longer hold a wave back, the Defend veto no longer
   holds it, and the strength test holds it only when the objective is
   `allInMaxEnemyRatio` times stronger than the army or more. The next wave
   is never more than `allInWaveIntervalSeconds` away.
3. **Reinforcements stream** to the army that is out: the "gather a company
   first" hold lasts at most `allInReinforceMaxHoldSeconds`.
4. **Wall guards and claim squads are released** into the army (curse-clearing
   sorties included); no new claim round and no idle curse clearing is sent.
   An all-in army takes ground by standing on what it razes.
5. **The army fights on.** Both retreat tests (the mission's strength check
   and the tactics fall-back, § 6h) use at least `allInRetreatRatio`: a
   committed army falls back only when it is badly outmatched. A tier that
   never retreats (ratio 0) still never retreats.
6. **Home is the home guard's.** Entering Defend no longer disbands the
   missions or recalls the field army: the home guard and the economy
   responders (§ 5i) fight at home while the army keeps attacking. The base
   may fall while the army is away — that is the accepted price.

The war (§ 6i), the pile-on (§ 6j) and the march round the curse (§ 6k) are
untouched: the all-in changes how much goes, not where it goes or how it
gets there.

**Elimination by territory.** A faction that holds no territory for
`noTerritoryGraceSeconds` is eliminated (Territory_Claims.md § 7). For the AI
this means a faction stripped to its capital territory dies the moment that
falls, whatever army it still has in the field — which is why the all-in keeps
a home guard instead of sending everything. For the attacker it means the
closeout's "go for the throat" (§ 6i) finishes a beaten victim by taking its
last held ground, not by hunting every last unit.

## 6o. The hunt — graduated pressure (2026-10-09)

Developer: "I need deaths to start occurring from minute 15 or even earlier."
Chosen model: **graduated pressure**, with the 8-player match aiming at about
45 minutes. Measured in the 2026-10-09 v8 batch: one to three factions per
match were still on 1-2 territories at minute 10 and every one of them was
among the first eliminated — but only from minute 34 on, because before the
all-in every wave was held back by the standing floor (`wave 1 BLOCKED ...
above standing floor`).

1. **Prey.** From `preyAfterSeconds`, a hostile player holding at most
   `preyMaxTerritories` territories — **or at most `preyMaxShareOfHunter` of
   the hunter's own territories** — is prey. (The relative bar was added the
   same day: in the first v10 batch nobody stalled below 4 territories while
   the leaders held 13-15, so the absolute bar alone hunted no one.) Territory ownership is public, so
   no sighting is needed. The curse is never prey.
2. **Hunters.** A faction holding at least `preyHunterMinTerritories` that
   sees prey goes to war on the **nearest** one (its nearest held territory to
   our capital) — the hunt outranks the pile-on (§ 6j) and any war already on
   — and arms the all-in doctrine (§ 6n) early, so its waves take everything
   above the home guard. Logs `HUNT: ...` and `WAR: war on X — the hunt`.
3. **The late game comes earlier.** `allInAfterSeconds` and the node lifetime
   (Territory_Claims.md § 11.3) move together so the whole match compresses
   toward ~45 minutes; the values are on the assets.

The stalled player dies first, early; the strong ones still fight it out
after the all-in. All knobs on `SimpleAISystem.asset`.

## 6p. Everyone against the Shardroot holder (2026-10-09)

Developer: "Gang up on the player who has the Shardroot regardless of road
chosen." While any hostile player holds the Shardroot — a carrier, the
Shardbound King or an enshrining Temple — that player is **every AI's war
victim**, ahead of the hunt (§ 6o), the pile-on (§ 6j) and any war already
on, and every AI arms the all-in doctrine (§ 6n) against it. While an
ascension countdown runs ([Curse_And_Shardroot.md §3.1c](Curse_And_Shardroot.md))
the objective is the enshrining Temple itself. A Shardroot strike (§ 6l)
still goes for an artifact on the ground. When the holder falls, wars resume
by the usual rules.

## 6q. A full army attacks (2026-10-09)

The v8 batch: survivors sat at a third of their population cap on 20-60k
banked supplies while their wars stalled. As StarCraft's AI attacks at max
supply, an AI whose army is near its population cap, or whose bank is far
above what its production queue can spend, launches its next wave at once,
whatever the posture and wave timers say. The thresholds are on
`SimpleAISystem.asset`.

## 6r. Army doctrines — commit, don't hedge (2026-10-09)

The v8 batch: armies were nearly identical across all eight factions (mean
pairwise cosine similarity of army mixes 0.86 at minute 15, about 0.7
after); Swordsman, Archer and Spearman were 65% of every army. The
composition layer (§ 5d) adds a covering counter for every enemy type it has
seen, so every AI converges on the same hedge, and two identical blobs grind
each other down instead of one beating the other.

- **Each AI rolls one doctrine per match**, a hash of the match seed and its
  faction (so peers and replays agree): Cavalry, Shieldwall, Bowline or
  Siegecraft. A doctrine is a set of class weights (infantry / ranged /
  cavalry / siege) multiplied into the composition shares on top of the
  personality's, and it damps the enemy read (`doctrineCounterResponseScale`),
  so only a minority of the army follows the enemy-sighting counters of § 5d.
- The point is **lopsided matchups**: a cavalry army meeting an archer army
  should win decisively and the archer player can fall; the cavalry player is
  then exposed to its own counter — Combat_Pacing.md § The triangle.
- The doctrine table (names and class weights) is on `SimpleAISystem.asset`.

## 7. Scouting

Keep the information-driven `ScoutDirectorSystem` (zone staleness scoring,
threat-aware flee, recon-then-strike). This already matches AoE4's
post-Anniversary scout behavior.

**Scouts never stop (2026-09-29, SUPERSEDES the expanding-vision
perch-and-bloom model).** A Scout's line of sight is fixed at its authored
maximum (Scout.asset, 40 m) — nothing shrinks it while moving. AI scouts are
therefore always travelling: the director hands a scout its next zone
**18 m before** it reaches the current one (it thinks every 2 s, so a
tighter arrival radius left scouts standing idle at every waypoint), there
is no dwell at a vantage, and when every zone is freshly assigned it takes
the stalest one anyway rather than idle. The intel pass records everything
the moving circle reveals.

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
adoption, sect powers and Litharchs). The Royal Stable and Siege Yard
left the ladder (2026-10-04): production buildings come only from § 5g (one
per province, more when saturated). Sect adoption
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

## 7c. Alanthor well purification

The Holy Scholar's rite is the Alanthor AI's victory path
([Curse_And_Shardroot.md § 2.12](Curse_And_Shardroot.md#212-what-the-ai-owes-the-rite-2026-09-13)
is the gate it must pass). Until 2026-09-26 the AI only ever looked at the
**one well nearest its Hall**: if that well was garrisoned it assaulted or
waited there all match, while clean wells went unvisited. Now
`TryPurifyWells` **ranks every purifiable well** (Active or rubble, built,
no rite in progress, revealed to this faction) and works down the list:

- **Score** (lower is better) = distance from the Hall
  + `wellDefenderPenalty` (20 m) per curse defender at the well
  - `wellOwnedBonus` (60 m) if the well's territory is already ours, or
  - `wellAdjacentBonus` (30 m) if any of 8 probes on a
  `wellAdjacencyProbeRadius` (35 m) ring around it lands on our territory.
  Ties break on position, never on chunk order.
- The **first well that passes the rite gate** gets the Scholar and its
  escort (`escortSize` **6**, was 10).
- If none passes because of defenders, the **best-scored defended well is
  assaulted** (`assaultOdds` 2 x defenders, at least `assaultMinUnits`
  **6**); the rite follows on a later think.
- A short escort blocks every well alike, so the search stops there and
  logs it.

Gate numbers (both cultures, `AIEndgameCommon.asset`): defenders counted
within `wellDefenceRadius` **25 m** (was 45 — the garrison area, not the
region), retry after this faction's Backlash `riteRetrySeconds` **240 s**
(was 600). Ranking numbers: `AIAlanthorEndgameSystem.asset`.

## 8. Tech / age / culture

- Age-up is a utility request (§5). Culture choice remains **Alanthor for
  the demo** (only Alanthor has an endgame brain); the per-strategy
  culture table stays in code behind one switch so enabling Runai/Feraldis
  later is a data change. Non-Alanthor cultures MUST NOT be enabled for
  AI until they have endgame behavior.

## 9. Multiplayer contract

> **Superseded in code (2026-07/08) and relied on since:** the brains run on
> the HOST ONLY (`GameSettings.ShouldRunAIBrains()`), and every decision
> leaves as a replicated `CommandRouter` command
> (docs/Multiplayer_LAN_Readiness.md). A brain's internal state — including
> the strength map, the placement snapshot and the per-think memo of §6d —
> therefore never has to match another machine. The paragraph below
> describes the older every-client model.

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
- No naval/water AI (no naval gameplay yet).
