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
attack threshold, military/worker floors, raid cadence, risk tolerance
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
  `religionHuntReinforceSeconds` (90) join that attack — but only while the
  fight is still being won (newcomers plus the hunters already there); a
  losing hunt is called off and judged afresh. If not, it raises
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

Numbers: `SimpleAISystem.asset`, `AIBudget.asset`.

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
  `claimWaveYieldMaxSeconds` in a row, then one wave goes anyway. While the
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

**Log:** `ARMYFIRST: upgrade <building> yields — <reason>` and `ARMYFIRST:
research <tech> yields — <reason>`, at most once per `armyFirstLogInterval`
per faction, with the number of yields since the last line. Numbers:
`AIBudget.asset` (`armyFirstTargetFraction`, `armyFirstReserveUnits`,
`armyFirstStatusMaxAge`, `armyFirstLogInterval`).

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
   reserved spot (below). While the Fortress is due, steps 3 and 4 in that
   territory wait for it. It does not hold them when it cannot happen:
   before the age-up, at the faction's Fortress ceiling, or while a site
   there was recently refused.
3. **Watch Towers near the periphery.** Up to `towersPerTerritory` per
   territory that borders unowned, curse-held or hostile ground. Each tower
   faces one such neighbour in turn. Its search is anchored
   `towerPeripheryFraction` of the way from the territory's seed toward the
   neighbour's seed, and is locked inside the territory. Only cultures with
   a Watch Tower take this step.
4. **One production building** (`productionPerTerritory`) in each
   province: **the line the army plan needs most** that the territory does
   not already have. Per line, the plan's share is the composition's raw wish
   for the units that building's SO `trains[]`, over all four lines; its
   shortfall is that share minus the line's share of the faction's production
   buildings (sites and plans counted). The largest shortfall wins. The
   Barracks when nothing else qualifies (before an Alanthor age-up, or when
   the plan names no other line). The search is anchored at the territory's
   core (its capital or Fortress, else its seed), so units are trained near
   the front.

   **The home keeps a floor instead** (operator, 2026-10-04: "Home province
   is larger, it should have at least 2 of each building"). The home
   territory keeps `homeProductionPerLine` of **each** production line the
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
- **The army is below its target** (alive combat units under the floor's
  desired size).
- **No production building is still rising**, so one extra lands before the
  next is judged.
- **It is the line the army plan needs most** (the step 4 rule, over the
  whole faction). A request for another line is refused.

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
another line; at most once per `productionLogInterval`). Numbers:
`SimpleAISystem.asset`.

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
  `workerFloor + open sites`, capped at 12.
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
`homeProductionFloorAfterSeconds` the home keeps `homeProductionPerLine` of
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
`homeProductionPerLine`, `homeProductionFloorAfterSeconds`,
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
