using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="SimpleAISystem"/>. The asset is SimpleAISystem.asset,
    /// beside SimpleAISystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/SimpleAISystem",
                     fileName = "SimpleAISystem")]
    public sealed class SimpleAISystemConfig : ScriptableObject, IComponentConfig
    {
        static SimpleAISystemConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static SimpleAISystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<SimpleAISystemConfig>());

        // Build placement scan ring: how far from the Hall and at how many
        // angles we try before giving up on this tick. The min was bumped to
        // 10 m and max to 30 m so buildings have room to fan out around the
        // Hall without crowding it (and around each other — see spacing below).
        // Doubled with the building footprints (2026-08-13): the Hall's
        // half-extent went 2 m -> 4 m and a typical building's 2 m -> 4 m, so a
        // 10 m ring start left only 2 m of gap and most candidates failed
        // validation against the Hall itself.
        public float buildRingDistanceMin;

        public float buildRingDistanceMax;

        // Default: candidate must be ≥12 m from any existing building so the
        // AI leaves wide walkable corridors. Earlier 7 m was just enough that
        // unit pathing could squeeze through, but Gaussian-smoothed flow at
        // tight cell-corner thresholds would dither and units got stuck.
        // Doubled with the footprints (2026-08-13). At the old 12 m, two 8 m
        // buildings sat 4 m apart edge-to-edge and two 12 m ones OVERLAPPED —
        // every candidate then failed IsValidBuildPosition and the AI simply
        // stopped building. 20 m restores the ~6-12 m corridor the comment
        // below describes, still wider than any unit's collision radius.
        public float minBuildingSpacing;

        /// <summary>Placement keep-out around resource nodes (veilstone
        /// outcroppings + iron deposits). Structures parked against a patch
        /// blocked the workers' approach ring — they orbited the node
        /// forever (2026-08-03 playtest). Sized so a 4x4-cell footprint plus
        /// worker corridor always fits between building edge and node.
        /// Raised with the footprint doubling — the "4x4-cell footprint" this
        /// was sized for is now 8 m across, not 4.</summary>
        public float minResourceNodeClearance;

        // Kept as plain SPACING, not as an income rule. It was sized so two
        // huts' 15 m gather circles stayed disjoint, and huts no longer earn
        // from an area at all (docs/Design/Regions.md §4) — but three huts
        // stacked on top of each other in one territory is still a wall across
        // the AI's own base, so the distance earns its keep on layout alone.
        public float minGHutToGHutSpacing;

        /// <summary>
        /// Clear build cells (2 m each) kept EDGE TO EDGE between an AI
        /// building's footprint and every other building's, on the normal and
        /// the loose placement passes — a lane an army can walk through.
        /// Operator direction 2026-09-25: "AI building placement is too
        /// cramped". Extractors are exempt (their node decides where they
        /// stand). docs/Design/Game_AI.md §6b.
        /// </summary>
        public int buildingGapCells;

        /// <summary>The same gap on the LAST-RESORT pass, which drops the
        /// centre spacing. Was effectively 0 (flush); must stay at least 1
        /// so even a full base keeps a walkable seam.</summary>
        public int relaxedBuildingGapCells;

        /// <summary>How far from the Hall the last-resort pass may reach
        /// (the normal passes stop at buildRingDistanceMax). Kept inside the
        /// AI's perimeter wall: see AIWallPlanner.perimeterHalfExtentMin.</summary>
        public float relaxedBuildRingDistanceMax;

        /// <summary>Metres a base building's footprint keeps inside a planned
        /// perimeter wall's hub line.</summary>
        public float wallInteriorClearance;

        /// <summary>Ring reach of an EXTRACTOR's site search around its node
        /// site. The node gate accepts only candidates within 4 m of a free
        /// node, so anything beyond this is candidates that cannot pass.</summary>
        public float extractorSearchRadius;

        // ── Trading Outposts (docs/Design/Veilstone_Economy.md §3.1) ──────

        /// <summary>Veilstone banked at or above which half the faction's
        /// Trading Outposts switch to Forge (while veilsteel is short).</summary>
        public int outpostForgeAboveVeilstone;
        /// <summary>Veilstone below which every Outpost goes back to Trade.</summary>
        public int outpostTradeBelowVeilstone;
        /// <summary>Veilsteel banked at or above which no Outpost forges.</summary>
        public int outpostVeilsteelTarget;
        /// <summary>Veilsteel banked at or above which one Outpost sells it
        /// for iron and supplies (once Veilsteel Export is researched).</summary>
        public int outpostSellAboveVeilsteel;

        // ── Veilstone as the army's bottleneck (2026-10-03) ──────────────

        /// <summary>Seconds (simulated) a combat unit the bank could not pay
        /// for is passed over by the composition picker, unless the bank
        /// covers it again sooner. The picker trains the next-best affordable
        /// unit meanwhile instead of re-asking for the same one every think.</summary>
        public float unaffordableUnitCooldownSeconds;
        /// <summary>Extra claim score per uncursed veilstone outcrop in a
        /// candidate territory, for an Alanthor faction whose army is short
        /// of veilstone — each one is a Trading Outpost site. Added on top
        /// of claimNodeBonus, which the outcrop already earns as a node.</summary>
        public float claimVeilstoneNodeBonus;

        // ── Conquering curse-held veilstone (2026-10-03) ─────────────────

        /// <summary>Most free soldiers sent at once to clear the curse nodes
        /// off a curse-held territory with veilstone outcrops in it.</summary>
        public int claimCurseSquadMax;
        /// <summary>Radius (m) around the target curse node AIEngagement
        /// weighs the curse's garrison in before the assault launches.</summary>
        public float claimCurseAssessRadius;
        /// <summary>Score subtracted from a curse-held candidate (it is a
        /// fight against a garrison, not free land).</summary>
        public float claimCurseTargetPenalty;
        /// <summary>Seconds a curse assault may run before it is abandoned
        /// and the territory skipped for a while.</summary>
        public float claimCurseTimeoutSeconds;

        // ── Fortress expansion (2026-10-03, Game_AI.md § Fortress expansion)

        /// <summary>Seconds between Fortress-expansion decisions.</summary>
        public float fortressCheckInterval;
        /// <summary>What the bank must still hold AFTER paying for the
        /// Fortress, so the army is not starved by it.</summary>
        public int fortressReserveSupplies;
        public int fortressReserveIron;
        public int fortressReserveVeilstone;
        /// <summary>Most Fortresses (the capital included) one AI owns.</summary>
        public int fortressMaxPerFaction;
        /// <summary>Score per known veilstone outcrop inside the candidate.</summary>
        public float fortressOutcropWeight;
        /// <summary>Score per known outcrop in the candidate's neighbours the
        /// faction does not hold — the ground the Fortress opens up.</summary>
        public float fortressFrontierOutcropWeight;
        /// <summary>Score when the candidate borders a rival's or the curse's
        /// territory, or its meter is contested.</summary>
        public float fortressBorderBonus;
        /// <summary>Score when the candidate is cut off from every Fortress
        /// (it is wearing down — a Fortress there reconnects it).</summary>
        public float fortressDisconnectedBonus;
        /// <summary>Score lost per metre from the home capital.</summary>
        public float fortressDistanceWeight;
        /// <summary>Seconds a territory whose Fortress could not be sited is
        /// skipped.</summary>
        public float fortressSiteRetrySeconds;

        // ── Lost sole trainer (2026-10-04, Game_AI.md 6c) ────────────────

        /// <summary>After a lost production line's replacement is placed, the
        /// rebuild for that line waits this long before it may place another
        /// — a lockstep placement lands two ticks later, so the count would
        /// otherwise read zero again on the next think.</summary>
        public float lostTrainerRetrySeconds;

        /// <summary>When a lost line's rebuild is refused for "bank short",
        /// arm a STRICT AIPivotalReserve for its price (no duty cycle) so
        /// the discretionary spenders stop draining it between thinks.</summary>
        public bool lostTrainerSaveStrict;

        /// <summary>After a lost line's rebuild found no legal spot, the
        /// next search waits this long (it bypasses the failed-search
        /// memory, so it needs its own pacing).</summary>
        public float lostTrainerSearchRetrySeconds;

        /// <summary>The army floor's quiet pause: with no standing trainer
        /// for any combat unit, the floor stops asking and logs this rarely
        /// (seconds) instead of once a minute per missing unit.</summary>
        public float noTrainerLogInterval;

        // ── Think cost (2026-09-25 AI perf pass) ─────────────────────────

        /// <summary>Seconds a FAILED site search for one (faction, building,
        /// anchor) is remembered and not re-run. Forgotten early when a
        /// building is razed or territory changes hands. 0 disables.</summary>
        public float failedSiteSearchCooldown;

        /// <summary>Site-search candidates one think may test in total, over
        /// every building it tries to place.</summary>
        public int siteSearchCandidateBudget;

        /// <summary>Of those, how many may reach the expensive terrain /
        /// passability validation stage.</summary>
        public int siteSearchValidateBudget;

        /// <summary>Heavy AI thinks (a SimpleAISystem brain think, an endgame
        /// pass) allowed per rendered frame, across all brains and systems.
        /// Extra due brains wait a frame, most overdue first.</summary>
        public int maxBrainThinksPerFrame;

        /// <summary>A brain overdue by this many of its own think intervals
        /// thinks regardless of the per-frame budget, so a crowded match
        /// slows brains down but never starves one.</summary>
        public float thinkStarvationIntervals;

        /// <summary>Minimum seconds between claim attempts while a funded
        /// claim pot bypasses claimAttemptInterval.</summary>
        public float claimPotReadyRetrySeconds;

        // Build a Hut whenever population headroom drops to this or below.
        /// <summary>
        /// Spare population the AI keeps in hand. Below this it raises a Hut.
        ///
        /// Was 2, which meant housing always TRAILED production: the AI waited
        /// until it was within two units of the cap, then built one hut, and
        /// every trainer stalled for the build while the buffer refilled. With
        /// an army of five that never mattered, because the cap was never
        /// approached. The target is now a 200-pop ceiling inside twenty
        /// minutes, which is roughly ten units a minute sustained, and a
        /// two-unit buffer cannot absorb that for the fifteen seconds a hut
        /// takes to go up.
        ///
        /// 16 is a little over a minute of production at that rate, so housing
        /// leads demand instead of chasing it. Huts are 80 supplies and the
        /// full 200 pop of them is ~1,440 - affordable many times over against
        /// the ~12,000 supplies earned in twenty minutes, so building ahead
        /// costs nothing that matters.
        /// </summary>
        public int populationHeadroomFloor;

        /// <summary>A Hut's supply price, for the surplus test above.</summary>
        public int hutCostSupplies;

        /// <summary>Supplies the economy wallet keeps back from building ahead
        /// — a Worker (140) plus a Gatherer's Hut, so the build order can still
        /// take its turn.</summary>
        public int economyWorkingFloor;

        /// <summary>Workers a Feraldis faction keeps for base expansion.</summary>
        public int feraldisWorkerFloor;

        /// <summary>Max Gatherer's Huts (= Raider Camps) a Feraldis AI builds.
        /// Each one is a permanent raider stream, not a gather bonus.</summary>
        public int feraldisRaiderCampCap;

        /// <summary>Seconds over which a gathering culture's hut cap ramps
        /// from its difficulty target to double it.</summary>
        public float hutCapDoublingSeconds;

        /// <summary>
        /// EMERGENCY DEFENSE (2026-08-31 balance investigation). The greedy
        /// identities died RICH and NAKED — banks of 2,300-2,700 at the
        /// moment of elimination, five towers, no walls — because nothing
        /// converted money into defence while the base burned. Under Defend
        /// with a fat bank: a tower goes up (bank-direct, an emergency is
        /// exactly when wallet accounting must not matter) and the garrison
        /// trains past the floor. Capped and throttled so a long siege
        /// builds a real defence, not a money furnace.
        /// </summary>
        public int emergencyBankFloor;

        public int emergencyTowerCap;

        public float emergencyRetrySeconds;

        /// <summary>
        /// SIEGE IS THE LATE GAME (2026-08-31 directive). Every duel in the
        /// finisher batch ended the same way: a wall the attacker could not
        /// break, and a "deficit 116 x Alanthor_Catapult" log line — infantry
        /// waves grinding on stone while the siege line went unfunded behind
        /// them. From era 2 the Siege Yard is a PIVOTAL purchase (reserved
        /// like the age-up, so discretionary spending cannot eat its price)
        /// and the army keeps a standing siege train. Catapults are combat
        /// class, so the wave draft takes them along automatically.
        /// </summary>
        public int siegeTrainFloor;

        public float siegeTrainRetrySeconds;

        /// <summary>Always-on economy layer (2026-08-04 rev.2). Not a budget
        /// system — a PRIORITY ladder the build order cannot override:
        ///   1. WORKER FLOOR — a stalled opener must still grow its workers.
        ///   2. HUT PIPELINE — "lack of supplies means build more huts":
        ///      whenever no Gatherer's Hut is under construction and the
        ///      cost is affordable, start the next one. Huts repay fast
        ///      (120 S + 10 I) and are the supplies engine everything else
        ///      (units, buildings, techs) draws from; the difficulty target
        ///      is irrelevant here — one is simply ALWAYS in flight.
        /// If the pipeline model still lets openers hoard, the escalation is
        /// true per-purpose income budgets (economy/research/expansion/
        /// building/military) — deferred until observed necessary.
        /// NOW (2026-10-03): the home-territory crew of THE WORKER RULE —
        /// workers = this + workersPerConqueredTerritory per territory beyond
        /// home (SimpleAISystem.WorkerFloorFor).</summary>
        public int economyWorkerFloor;

        /// <summary>Huts below this count build unconditionally (bootstrap);
        /// past it the ECONOMY WALLET is the pipeline's constraint (M-A —
        /// the flat supplies reserve this replaced lives on in git).</summary>
        public int hutPipelineFreeCount;

        /// <summary>Seconds at match start the opening-hut savings hold is
        /// armed even before a free supply node has been SEEN — the home
        /// nodes sit under fog for the first moments, and that is exactly
        /// when the starting bank used to be spent on everything but huts.</summary>
        public float openingHutGraceSeconds;

        /// <summary>While the opening huts are still pending, the housing
        /// reflex waits until population headroom is down to this — the
        /// normal populationHeadroomFloor fires on the very first think.</summary>
        public int openingHutHousingHeadroom;

        /// <summary>THE WORKER RULE's second term: workers added for every
        /// territory held beyond the home one (economyWorkerFloor is the
        /// home crew). SimpleAISystem.WorkerFloorFor.</summary>
        public int workersPerConqueredTerritory;

        /// <summary>Sweep cadence (seconds). Slow mop-up loop — research
        /// takes 30-90 s per tech, so 20 s keeps every host busy without
        /// hammering the queries.</summary>
        public float researchSweepInterval;

        /// <summary>How often a faction may attempt a claim. A Hall is
        /// expensive and the site search is not free; there is no value in
        /// retrying every tick.</summary>
        public float claimAttemptInterval;

        /// <summary>Fewest soldiers a claim squad is sent with (Territory_Claims.md
        /// §2: population weight fills the meter). Empty, unthreatened ground
        /// gets exactly this many (Game_AI.md § 5b, defend-based expansion).</summary>
        public int claimSquadMinSize;

        /// <summary>Most soldiers one claim squad may take, however much the
        /// known threat at the target asks for.</summary>
        public int claimSquadMaxSize;

        /// <summary>Most claim squads a faction keeps out at once, before the
        /// difficulty's expansionDrive and the plan's claim appetite scale it.</summary>
        public int claimMaxParallelSquads;

        /// <summary>Radius (m) around a claim point whose KNOWN hostile power
        /// (mobile + static) the squad is sized against.</summary>
        public float claimThreatRadius;

        /// <summary>A claim squad's power must reach the known threat at its
        /// target times this, or the target is held back.</summary>
        public float claimThreatMargin;

        /// <summary>Most of the army (fraction of combat units) that claim
        /// squads may hold at once; the rest stays free for defence and waves.</summary>
        public float claimArmyShare;

        /// <summary>Extra claim score per known supply node in a candidate
        /// (on top of claimNodeBonus): supply slots are the income.</summary>
        public float claimSupplyNodeBonus;

        /// <summary>Extra claim score per known uncursed veilstone outcrop in a
        /// candidate, always (claimVeilstoneNodeBonus adds more while the
        /// army is short of veilstone).</summary>
        public float claimOutcropBonus;

        /// <summary>Seconds a target is skipped after the squad the army could
        /// spare was judged too weak for its known threat.</summary>
        public float claimHeldBackSeconds;

        /// <summary>Seconds a squad keeps standing on ground it has CLAIMED
        /// while it waits for an extractor or Fortress to lock it.</summary>
        public float claimHoldForLockSeconds;

        /// <summary>Most seconds a squad pre-staged on a target before the
        /// age-up lands may wait there (the landmark stalled: release it).</summary>
        public float claimPrestageMaxSeconds;

        /// <summary>A claim that found no idle soldiers within this many
        /// seconds makes the attack waves yield the idle army to it.</summary>
        public float claimWaveYieldWindowSeconds;

        /// <summary>Longest continuous stretch (s) the waves yield to the
        /// claims; past it one wave may launch anyway.</summary>
        public float claimWaveYieldMaxSeconds;

        /// <summary>DEFEND-BASED EXPANSION (Territory_Claims.md §10, 2026-10-04:
        /// no territory cap). Army power (AIEngagement scale) per held
        /// territory below which the faction is STRETCHED and stops claiming
        /// to consolidate. Divided by the plan's claim appetite.</summary>
        public float expandPowerPerTerritoryFloor;

        /// <summary>The stretch test applies only from this many held
        /// territories (a fresh empire is never "stretched").</summary>
        public int expandStretchMinTerritories;

        /// <summary>Seconds after losing a claimed territory during which the
        /// faction consolidates instead of claiming more.</summary>
        public float expandLossConsolidateSeconds;

        /// <summary>A faction holding fewer Religion Points than this sends a
        /// squad against the nearest curse node within reclaimRadius, even
        /// when not threatened: curse kills are where RP come from.</summary>
        public int reclaimReligionBelow;

        /// <summary>The first-Religion-Point hunt (TryHuntFirstReligionPoint)
        /// waits this long into the match before judging a curse node.</summary>
        public float religionHuntEarliestSeconds;

        /// <summary>Radius around the target curse node the hunt assesses —
        /// a little past the curse's guardRadius (BorderSettings).</summary>
        public float religionHuntAssessRadius;

        /// <summary>After a launch, newly free units join the attack on the
        /// same node for this long, without re-judging the fight.</summary>
        public float religionHuntReinforceSeconds;

        /// <summary>Seconds a claim squad may hold before the attempt is
        /// abandoned and the territory skipped for a while.</summary>
        public float claimSquadTimeoutSeconds;

        /// <summary>
        /// How long the brain will hold income back while saving for a Hall.
        ///
        /// Sized on observed income: the logged AIs ran 5-7 supplies/s from a
        /// mean bank near 250, so a 600-supply Hall is about a minute of not
        /// spending. Beyond that the goal is not going to complete and the hold
        /// is only starving the faction, so the reservation lapses and the
        /// economy gets its income back.
        ///
        /// Saving DOES cost production — that is what an expansion costs, and
        /// it is why the hold is bounded at both ends: it lapses here, and a
        /// successful claim buys the economy a recovery window before the
        /// brain starts saving for the next one.
        /// </summary>
        // 90 -> 180 (2026-08-31): with iron-priced extractors the supply
        // income genuinely covers a Hall inside three minutes, but rarely
        // inside ninety seconds — the shorter hold lapsed just before the
        // pot filled, over and over, and the map stayed unclaimed.
        // 180 -> 120 (2026-08-31): unclaimed territory sitting idle is the
        // bigger failure — the reserve fills faster so claims land sooner.
        public float claimSaveSeconds;

        /// <summary>Breathing room after a claim lands, before the brain may
        /// hold income back for the next one. Without it a faction that can
        /// expand would save continuously and never train anything.</summary>
        // 120 -> 60 (2026-08-31): the map has ~25 territories and matches
        // now decide in under an hour of game time — a two-minute breather
        // per claim meant most ground was never taken by anyone.
        // 60 -> 30 (2026-08-31): a successful claim should chain into the
        // next one while the map still has Natural ground — half the pause.
        public float claimSuccessCooldown;

        /// <summary>Army a settled faction (3+ territories) must field before
        /// it starts saving for ANOTHER claim. Keeps the perpetual land-grab
        /// from throttling armies all match. 12 -> 8 (batch 5): under
        /// constant curse-wave pressure armies rarely rebuilt past 12, so
        /// expansion froze at exactly three territories — 8 keeps both
        /// engines turning.</summary>
        // 8 -> 6 (batch 17): with the age-up healed (48/48 era 2) armies
        // equilibrate at 6-7 under curse-wave attrition — one unit below the
        // gate, so claims still parked at three territories. Six sits under
        // the observed steady state, letting both engines actually run.
        public int minArmyForNextClaim;

        /// <summary>Throttle for the "why didn't it claim" line, so a blocked
        /// claim is diagnosable without filling the log.</summary>
        public float claimLogInterval;

        // maxClaimReach (a seed-distance cap on claim targets) was retired on
        // 2026-09-26: a Hall may only go into a territory ADJACENT to one the
        // faction holds (docs/Design/Regions.md §2), which is the real rule
        // the distance cap approximated.

        /// <summary>A Hall needs one of the faction's workers within
        /// TerritoryOwnership.HallWorkerRange of its site. When none is, the
        /// AI walks one there first; this is how long (seconds) before the
        /// walk order is re-issued, in case something else overrode it.</summary>
        public float claimWorkerRewalkSeconds;

        /// <summary>The walking worker's stand-off from the Hall's footprint
        /// edge, metres — it waits beside the site, not on it.</summary>
        public float claimWorkerStandOff;

        /// <summary>A region with resources is worth more than empty ground —
        /// territory income comes from the nodes standing in it
        /// (Regions.md §4).</summary>
        public float claimNodeBonus;

        /// <summary>How often a faction tries to raise one extractor. Slower
        /// than the claim check: an extractor is an optimisation, and a faction
        /// that spends every spare coin on them fields no army.</summary>
        public float extractorAttemptInterval;

        /// <summary>Throttle for the "why didn't it extract" line — same
        /// contract as LogClaimBlocked.</summary>
        public float extractLogInterval;

        /// <summary>How long an age-up hold stands before it must re-arm.</summary>
        public float ageUpSaveSeconds;

        /// <summary>Outranks the Hall claim's hold (0) for the single
        /// reservation slot.</summary>
        public int ageUpReservePriority;

        // 180 -> 480 (2026-08-30). The timeout exists to kill STALE missions,
        // and 180 s is SHORTER than an honest cross-map march on Veilmarch
        // (~190-250 s at infantry speed over 700+ m) — so armies were being
        // recalled mid-walk, which is why every reinforcement line read
        // "0 on the objective" and no formation was ever SEEN arriving. The
        // raze chain resets the clock per target, so a rolling assault never
        // trips this either.
        public float missionTimeoutSeconds;

        /// <summary>How far from a razed objective the army looks for the
        /// next building to press onto. A base's structures sit well inside
        /// this of each other, so a won assault rolls through the whole base;
        /// a building a territory away is a NEW decision for a NEW wave.</summary>
        public float razeChainRadius;

        /// <summary>AI seconds after which waves stop policing the leader and
        /// hunt the weakest instead — late games must CONVERGE. ~25 game
        /// minutes on the AI clock.</summary>
        // 1500 -> 900: keyed to the AI clock, which runs SLOW under batch
        // contention — at 1500 the closeout armed minutes before the time
        // budget and decided nothing.
        public float closeoutAfterSeconds;

        /// <summary>ONE WAR AT A TIME (2026-10-07, Game_AI.md § 6i): failed
        /// attacks in a row (a mission timed out, an army gave its objective
        /// up) after which a faction abandons its war on a victim and may pick
        /// another. A razed objective resets the count.</summary>
        public int warMaxFailures;

        /// <summary>THE ARMY CAP RISES WITH UNSPENT MONEY (2026-10-07, § 6j):
        /// unspent supplies + iron at or above which the cap rises.</summary>
        public int armyCapRaiseThreshold;
        /// <summary>Units of cap added per raise.</summary>
        public int armyCapRaiseStep;
        /// <summary>Seconds between raises while the bank stays above the threshold.</summary>
        public float armyCapRaiseInterval;

        /// <summary>PILE-ON (2026-10-07, § 6i): a hostile faction whose board
        /// score is at most this share of the score of the faction at war with
        /// it is LOSING its war, and other factions join against it.</summary>
        public float pileOnLosingShare;

        /// <summary>A building sighting with at most this much recorded
        /// garrison reads as STRAY — roughly three soldiers on the
        /// UnitStrength scale (dmg x2 + hp/10; a spearman is ~40).</summary>
        public int strayDefenseMax;

        /// <summary>Opportunity reports older than this are not opportunities
        /// any more — the garrison may have grown back.</summary>
        public float opportunityMaxAgeSeconds;

        /// <summary>A lead only draws the table's aggression when it is REAL:
        /// this much ahead of second place. Below it, factions fight their
        /// own wars — an unconditional gang-up rubber-bands every match into
        /// a stalemate (12 quits in 12, 2026-08-31).</summary>
        public float leadMargin;

        // Forward staging: form up this far from the target (home side), and
        // commit once the army centroid is within the gather radius (or the
        // staging phase times out — stragglers must not stall the push).
        // 30 -> 45 (2026-08-30): the form-up must happen OUTSIDE tower fire,
        // or the army assembles while being shot and commits already bloodied.
        public float stagingDistance;

        /// <summary>No launched wave for this long means the next one stops
        /// being choosy — the engagement/recon quality holds yield to cadence
        /// (the relentless-waves directive: a full army fielded at least
        /// every five minutes).</summary>
        public float waveOverdueSeconds;

        /// <summary>Match time after which a wave launches on STRENGTH, not
        /// on a head count (2026-10-04, Game_AI.md 6a; replaced the
        /// full-population rule). Past this mark the idle army must number at
        /// least strengthWaveMinArmy (x the plan's WaveBarScale) AND its power
        /// must reach strengthWaveRatio (x the personality's riskMultiplier)
        /// times the best known defence at the objective. The overdue release
        /// does not override it.</summary>
        public float strengthWaveAfterSeconds;

        /// <summary>Army power over the best known defending power a late wave
        /// needs before it launches (AIEngagement scale: hostile army + static
        /// defences at the objective, or the scouted garrison if larger).
        /// Multiplied by the personality's riskMultiplier.</summary>
        public float strengthWaveRatio;

        /// <summary>Fewest idle soldiers a late (strength-gated) wave may
        /// launch with, before the plan's WaveBarScale; clamped to a third of
        /// the population ceiling like the early bar. Stops a tiny army
        /// trickling out at an undefended target.</summary>
        public int strengthWaveMinArmy;

        public float stagingGatherRadius;

        // ── Muster (2026-09-07). ──
        // The army forms up BEFORE it leaves. It used to be ordered straight
        // from wherever its units stood — rally points on five buildings,
        // survivors of the last fight, a scout at the far gate — and the
        // formation plan's cohesion gate takes members only from within a
        // few metres of the centroid, so most of the army was an "outlier"
        // that walked to the stage point ALONE. That is the trickle the
        // curse waves stopped showing the moment they spawned compact: the
        // fix for the AI is the same compactness, made by an order.
        /// <summary>The muster point sits this far from the Hall toward the
        /// objective — outside the base, on the way.</summary>
        public float musterDistance;
        /// <summary>Share of the army inside the gather radius of the
        /// muster point before it departs.</summary>
        public float musterGatherFraction;
        /// <summary>Longest the army waits at the muster for stragglers.</summary>
        public float musterTimeoutSeconds;
        /// <summary>Seconds between straggler sweeps on the march: any member
        /// travelling outside the formation is folded back in by re-issuing
        /// the leg's order to the whole army.</summary>
        public float regroupInterval;
        /// <summary>Share of the army that must be travelling OUTSIDE the
        /// formation before a straggler sweep re-issues the leg (never fewer
        /// than two units). Below it the strays finish on their own orders
        /// rather than stopping the whole army to re-slot.</summary>
        public float regroupLooseFraction;
        /// <summary>A reinforcement column this close to the army it was sent
        /// to join is merged into it.</summary>
        public float reinforceMergeRadius;

        // 60 -> 240 (2026-08-30). The stage clock starts at LAUNCH, and 60 s
        // is a fraction of the march to the stage point — so the "staged"
        // strike was committing from wherever the column happened to be,
        // which is exactly "army staging wasn't visible". 240 s covers the
        // march plus the form-up; the gathered test still commits early the
        // moment the army is actually assembled.
        public float stagingTimeoutSeconds;

        public int raidPartySize;

        // Launch a raid alongside the attack only when this many EXTRA idle
        // units exist beyond the attack threshold.
        public int raidSurplus;

        /// <summary>Seconds between reinforcement sweeps for a live wave.</summary>
        public float reinforceInterval;

        /// <summary>
        /// Fewer than this many fresh bodies waits for company.
        ///
        /// The sweep already sends its picks as ONE formation, which fixed
        /// units walking to the front individually. It did not fix the group
        /// being a group of one: the sweep runs every 10 s and takes whatever
        /// is idle, while training produces a unit every 15-20 s. Measured
        /// across 12 matches, of 228 reinforcement sends 46 carried 1-2 units
        /// and 102 carried 5 or fewer -- 45% of all dispatches walking into a
        /// live battle in penny packets.
        /// </summary>
        // 4 -> 6 (2026-08-30): reinforcement columns should READ as
        // formations on the map, not as couriers.
        public int reinforceMinGroup;

        /// <summary>
        /// ...but never hold longer than this. Holding indefinitely re-creates
        /// the older bug where reinforcements idled at home while the wave
        /// died at the front, so a part-formed group leaves anyway once it has
        /// waited this long.
        /// </summary>
        public float reinforceMaxHold;

        /// <summary>
        /// A unit standing within this many metres of the wave target has
        /// ARRIVED. Arrivals are not reinforcement candidates: re-issuing
        /// AttackMove to a position a unit is already standing on completes
        /// instantly, which is what made a spent wave look permanently "alive"
        /// (2026-08-07 match: `wave 4 reinforced with 47 unit(s) (0 already
        /// committed)` every 10 s for twenty minutes, army parked on a razed
        /// objective, `wave 5 BLOCKED` because everyone was nominally busy).
        /// Since 2026-10-03 it is also the "already there" test for every
        /// other AI group draft (religion hunt, reclaim, claim squads): a unit
        /// this close to the destination is re-poked on its own instead of
        /// being planned into a marching formation (AICommon.IssueGroupOrder).
        /// </summary>
        public float waveArrivedRadius;

        /// <summary>
        /// Hard cap on a single wave's life. Backstop for the case the arrival
        /// test cannot see — units that keep an AttackMoveTag forever because
        /// they are stuck on terrain read as "committed" and would hold the
        /// wave open indefinitely. At the cap the wave is retired and its army
        /// is released to the next draft, so the cadence keeps running.
        /// </summary>
        // 150 -> 360 (2026-08-30): shorter than the march it was feeding, so
        // waves retired as "SPENT (lifetime)" before anyone reached the
        // objective and the front never actually formed.
        public float waveMaxLifetime;

        /// <summary>
        /// Recurring attack waves (2026-08-04): once the difficulty's
        /// first-attack gate passes, launch a wave every
        /// AttackWaveIntervalSeconds. The idle-army minimum is a FLAT
        /// WaveBaseUnits — it used to grow per wave, and the escalating
        /// minimum is what produced "wave N BLOCKED, posture Rebuild"
        /// repeating for 18 minutes, so ReinforceActiveWave feeds the wave
        /// that is already out instead. Since TryLaunchAttack drafts ALL idle
        /// military, real waves still scale up with the economy well beyond
        /// the minimum. A wave that cannot launch (army short, posture holds,
        /// no target) retries on a short fuse instead of waiting a full
        /// interval.
        /// </summary>
        public float waveRetrySeconds;

        // §2.5b corruption counterplay knobs.
        public float reclaimEarliestSeconds;

        // bank level that counts as starving
        public int reclaimVeilstonePoorBelow;

        // "threatening the home economy"
        public float reclaimRadius;

        /// <summary>Inside this ring of the Hall, a curse growth is attacked
        /// regardless of the veilstone bank — threat-based, not poverty-based.</summary>
        public float reclaimHallThreatRadius;

        public int reclaimSquadSize;

        /// <summary>Radius (m) the reclaim squad judges a curse node's
        /// defenders over before it goes (it never feeds units into a fight
        /// it loses).</summary>
        public float reclaimAssessRadius;

        // Bank thresholds before triggering AgeUp: cost + reserve buffer.
        // Set to 0: with the optimised build-orders the AI accumulates well
        // beyond the bare cost, but we shouldn't *gate* on it — earlier 500/
        // 200/100 reserves caused the AI to sit on enough resources for age-up
        // (cost ≈ 1000/200/150) and never trigger because the bank stalled
        // between cost and cost+reserve. Players reasonably expected the AI
        // to age up the moment it could afford. Reintroduce a small reserve
        // here only if the post-age-up economy noticeably stalls.
        public int ageUpReserveSupplies;

        public int ageUpReserveIron;

        public int ageUpReserveVeilstone;

        /// <summary>How often an army re-reads the fight. Fast enough to
        /// switch off a dead target, slow enough that units are not handed a
        /// new order every frame — an army that re-decides every tick never
        /// closes the distance to anything.</summary>
        public float tacticsInterval;

        /// <summary>
        /// Once engaged, the army stays engaged while a target is anywhere
        /// inside this. Wider than <see cref="FocusRadius"/> on purpose: with
        /// one radius the army would drop out of contact the moment its last
        /// nearby enemy backed off a metre, re-issue a march order, then
        /// re-engage — handing every unit a new order twice a second.
        /// </summary>
        public float contactRadius;

        /// <summary>
        /// How far the army will reach for a target, measured from its own
        /// centroid — NOT from each unit.
        ///
        /// This is the "without spreading" rule. Per-unit nearest-enemy has no
        /// such bound: every unit reaches independently, so the army's width
        /// grows to the width of whatever it is fighting. One radius around one
        /// point keeps the whole army fighting the same local battle.
        /// </summary>
        public float focusRadius;

        /// <summary>
        /// A member further than this from the centroid has left the army and
        /// is recalled before it is given anything to fight.
        ///
        /// Sized to clear the FORMATION, not the fight. A full army with a
        /// siege train is 15 m deep, so its own rear rank sits 15 m from the
        /// centroid while perfectly in place; anything near that would recall
        /// units for standing exactly where they were told to. This is for a
        /// unit that has genuinely run off — which is what per-unit
        /// auto-acquire produced, at 30 m and more.
        /// </summary>
        public float armyCohesionRadius;

        // ── Curse-aware corridor + placement checks (2026-08-04) ──
        /// <summary>Fraction of corridor samples that must be deep crust
        /// before a wave counts the route as blocked.</summary>
        public float curseCorridorHeavyFraction;

        public float curseCorridorSampleStep;

        /// <summary>Metres a Hall may stand from a baked start marker and
        /// still count as "the Hall everyone knows is there".</summary>
        public float startHallKnownRadius;

        public float stepTimeoutSeconds;

        // ── Army composition by ROLE (2026-10-03, docs/Design/Game_AI.md
        //    § 5d). Every unit has a job and a counter; the share of each is
        //    its baseShare bent by what the faction has SEEN of the enemy. ──

        /// <summary>
        /// One row of the role table: a unit, the share of the army it holds
        /// with no intel (baseShare), and how far each fraction of the scouted
        /// enemy army moves that share (perEnemy* — share += coefficient x
        /// enemy fraction x the difficulty's counterResponse; negative = a
        /// unit that is the WRONG answer to that enemy). Enemy fractions are
        /// by estimated strength over the fresh military sightings.
        /// </summary>
        [System.Serializable]
        public sealed class CompositionRole
        {
            /// <summary>The unit this row trains.</summary>
            public string unitId;
            /// <summary>While unitId cannot be trained (trainer missing or
            /// below its level), its share goes to this row's unit — the next
            /// best answer to the same job. Chains.</summary>
            public string fallbackUnitId;
            /// <summary>Once THIS unit can be trained, the row's share moves to
            /// it: the Battering Ram's anti-building job is the Trebuchet's
            /// once the Siege Yard reaches its level (early rams, late
            /// trebuchets).</summary>
            public string supersededByUnitId;
            /// <summary>A veilstone-free basic (Spearman, Archer): scaled by
            /// the difficulty's basicsShareScale and the personality's
            /// basicsAppetite, and counted against the basics cap.</summary>
            public bool basic;
            public float baseShare;
            /// <summary>Enemy cavalry (Cavalry tag).</summary>
            public float perEnemyCavalry;
            /// <summary>Enemy heavy cavalry (Cavalry + Heavy).</summary>
            public float perEnemyHeavyCavalry;
            /// <summary>Enemy foot infantry (Infantry, not Cavalry).</summary>
            public float perEnemyInfantry;
            /// <summary>Enemy ranged (Ranged, not Siege).</summary>
            public float perEnemyRanged;
            /// <summary>Enemy Heavy weight class (any).</summary>
            public float perEnemyHeavy;
            /// <summary>Enemy whose ranged armour is at least
            /// armouredRangedDefense — what a bow cannot hurt.</summary>
            public float perEnemyArmoured;
            /// <summary>Enemy siege engines.</summary>
            public float perEnemySiege;
            /// <summary>High-value single targets: heroes, heavy cavalry and
            /// siege engines (the Ballista's work).</summary>
            public float perEnemyHighValue;
            /// <summary>Enemy foot standing massed (massedMinUnits or more
            /// sighted in one massedCellSize cell — the Catapult's work).</summary>
            public float perEnemyMassed;
        }

        /// <summary>The role table. Order is the tie-break order of the pick
        /// (deterministic), not a priority.</summary>
        public CompositionRole[] compositionRoles;

        /// <summary>The two veilstone-free basics (Spearman, Archer) may
        /// always number at least this many, whatever the army's size — the
        /// Age 0 army, the claim squad and the early defence are all basics.</summary>
        public int basicsFloor;

        /// <summary>The basics' combined share of the plan is clamped into
        /// [basicsMinShare, basicsMaxShare] after intel, difficulty,
        /// personality and economy have moved it. The basics cap is the
        /// larger of basicsFloor and that share of the army — a HARD cap
        /// (Game_AI.md 5d, 2026-10-04): at it the AI SAVES veilstone for the
        /// role it is short of; nothing lets another basic through.</summary>
        public float basicsMinShare;
        public float basicsMaxShare;

        /// <summary>SURPLUS (Game_AI.md 5e). While the bank is veilstone-held
        /// and overflowing (AIBudget surplusSupplies / surplusIron), each
        /// known outcrop in a claim candidate scores this many times its
        /// usual claimVeilstoneNodeBonus.</summary>
        public float surplusClaimOutcropScale;

        /// <summary>SURPLUS: claim rounds run every claimAttemptInterval x
        /// this (below 1 = more often) — the army is waiting on veilstone
        /// anyway.</summary>
        public float surplusClaimIntervalScale;

        /// <summary>SURPLUS: the Fortress picker weighs outcrops (inside and
        /// on the frontier) this many times as much; a Fortress whose ground
        /// or frontier holds an outcrop may then use the army's veilstone
        /// earmark.</summary>
        public float surplusFortressOutcropScale;

        /// <summary>SURPLUS: seconds between endgame research sweeps while
        /// the bank is overflowing (researchSweepInterval otherwise); in era 2
        /// the sweep no longer waits behind the authored ladder.</summary>
        public float surplusResearchSweepInterval;

        /// <summary>SURPLUS: seconds between the repeating "veilstone-held"
        /// state lines (each action logs once, unthrottled).</summary>
        public float surplusLogInterval;

        /// <summary>THE ECONOMY'S SAY. Veilstone income (the faction's
        /// Trading Outposts' Buy rate, per minute) at which the veilstone
        /// roles get their full share; below it they are scaled down toward
        /// ladderScaleWhenStarved and the basics take up the slack. A bank of
        /// veilstoneBankForFullLadder counts as full income on its own.</summary>
        public float veilstoneIncomeForFullLadder;
        public float veilstoneBankForFullLadder;
        public float ladderScaleWhenStarved;

        /// <summary>Ranged armour at or above which an enemy counts as
        /// ARMOURED (perEnemyArmoured).</summary>
        public int armouredRangedDefense;

        /// <summary>The MASSED read: cell size (m) and how many sighted enemy
        /// foot in one cell make it a mass.</summary>
        public float massedCellSize;
        public int massedMinUnits;

        /// <summary>Fewer fresh military sightings than this = no enemy read
        /// (the plan runs on baseShare alone).</summary>
        public int enemyReadMinSightings;

        /// <summary>Seconds between the "MILITARY: composition" log lines
        /// (target vs actual by role and why, the basics cap, what it is
        /// saving for).</summary>
        public float compositionLogInterval;

        // ── FLUSH PLACEMENT + THE SELF-LOCK CHECK (2026-10-04, Game_AI.md 6b) ──

        /// <summary>Run the base-connectivity check (AIBaseLayout) on every
        /// accepted non-extractor candidate: a placement may not seal a
        /// building's last free side, a production building's exit, a gate,
        /// the base's way out, or a pocket of open ground.</summary>
        public bool sealCheckEnabled;

        /// <summary>Half-size, in 2 m build cells, of the square window the
        /// check floods around the search anchor (40 = 80 m each way).</summary>
        public int sealWindowHalfCells;

        /// <summary>Open cells a placement may cut off from the base before it
        /// counts as sealing a pocket (a nook against a cliff is harmless).</summary>
        public int sealPocketToleranceCells;

        /// <summary>Most buildings one FLUSH cluster (footprints touching edge
        /// to edge) may hold before the next must leave a lane. Houses in the
        /// House quarter are exempt. 0 = no limit.</summary>
        public int maxFlushClusterBuildings;

        /// <summary>Connectivity checks one think may run (each is one bounded
        /// flood); past it the search is inconclusive, like the other budgets.</summary>
        public int sealChecksPerThink;

        /// <summary>Seconds a window's "before" picture is reused while the
        /// building set is unchanged (any placement, loss or plan rebuilds it).</summary>
        public float sealCacheSeconds;

        /// <summary>Seconds between "BUILD: rejected — would seal" lines per
        /// faction.</summary>
        public float sealLogInterval;

        // ── PER-TERRITORY DEVELOPMENT (2026-10-04, Game_AI.md 5g) ──

        /// <summary>Seconds between one faction's walks over its held
        /// territories (step 1 extractors, 2 Fortress, 3 towers, 4 production).</summary>
        public float territoryDevelopInterval;

        /// <summary>Seconds a territory waits after a placement there before
        /// its next step may place (a lockstep placement lands two ticks
        /// later; this stops a double order).</summary>
        public float territoryStepCooldown;

        /// <summary>Seconds a territory's step that found no legal spot (or a
        /// Fortress spot search that found none) waits before it is tried
        /// again; meanwhile the step counts as done for the order.</summary>
        public float territoryBlockedStepSeconds;

        /// <summary>Watch Towers in the HOME territory when it borders
        /// hostile or unowned ground (step 3, periphery-anchored). Provinces
        /// use the coverage siting below instead. Cultures with no Watch
        /// Tower skip it.</summary>
        public int towersPerTerritory;

        /// <summary>How far from the territory's seed toward the exposed
        /// neighbour's seed a tower's search is anchored (0 = seed, 1 = the
        /// neighbour's seed; the region lock keeps it inside).</summary>
        public float towerPeripheryFraction;

        // ── PROVINCE TOWER COVERAGE (2026-10-04, Game_AI.md 5g) ──

        /// <summary>Coverage-sited Watch Towers a province (a held territory
        /// other than the home) gets in step 3, BEFORE its production
        /// building. Not army-gated; a money refusal holds step 4 as any
        /// step does.</summary>
        public int towersProvinceFirst;

        /// <summary>The most Watch Towers a province gets in all. Those past
        /// towersProvinceFirst are the coverage extras (step 5): after the
        /// province's production, and only while the army is at
        /// towerExtraArmyFraction of its target.</summary>
        public int towersPerProvinceMax;

        /// <summary>Fraction (0..1) of a province's weighted important points
        /// inside some own tower's reach at which its towers stop.</summary>
        public float towerCoverageTarget;

        /// <summary>Minimum distance between two own towers, as a fraction
        /// of the tower's SO reach (attack range, else line of sight).</summary>
        public float towerMinSpacingRangeFraction;

        /// <summary>The coverage extras wait until the alive army is at least
        /// this fraction of its desired size (army first).</summary>
        public float towerExtraArmyFraction;

        /// <summary>A tower site must bring at least this much still-uncovered
        /// point weight into reach; below it the province is called done.</summary>
        public float towerMinGainWeight;

        /// <summary>Greedy sites tried per walk when the best ones have no
        /// legal footprint (each is a bounded placement search).</summary>
        public int towerSiteTriesPerWalk;

        /// <summary>Metres around a chosen coverage site the placement search
        /// may move the tower to find a legal footprint.</summary>
        public float towerSiteSearchRadius;

        /// <summary>Spacing (m) of the per-province sample grid: candidate
        /// tower sites and the border-crossing points. Built once per map.</summary>
        public float towerSampleStep;

        /// <summary>Cap on the sample-grid cells one province is flooded to.</summary>
        public int towerSampleMaxCells;

        /// <summary>Coverage weight of a resource node (built on or free).</summary>
        public float towerWeightResource;

        /// <summary>Coverage weight of the province's Fortress, or of its
        /// reserved Fortress spot.</summary>
        public float towerWeightFortress;

        /// <summary>Coverage weight of each production building there.</summary>
        public float towerWeightProduction;

        /// <summary>Coverage weight of each border sample facing an unowned,
        /// curse-held or hostile neighbour.</summary>
        public float towerWeightBorder;

        // The production CAPACITY (home floor per line, production per
        // province, the saturation threshold and seconds for extras) is per
        // difficulty tier since 2026-10-05: AIDifficultyProfileSO
        // (homeProductionPerLine, provinceProductionPerTerritory,
        // productionSaturationThreshold, productionSaturationSeconds).

        /// <summary>Match seconds before the home floor asks for more than
        /// one of a line; before it the home gets only its first building of
        /// each line, so the hut-first opening and the age-up savings run
        /// first (was the redundant Barracks floor's literal 90 s).</summary>
        public float homeProductionFloorAfterSeconds;

        /// <summary>Seconds between "PRODUCTION: extra held" lines per
        /// faction.</summary>
        public float productionLogInterval;

        /// <summary>Territories one un-anchored production-building request
        /// may try before it gives up this think (least-equipped, frontier
        /// first, then the home).</summary>
        public int productionSiteTerritoriesPerCall;

        /// <summary>The Fortress (step 2) waits for step 1 (the resource
        /// buildings) in its territory unless the territory is cut off from
        /// every Fortress.</summary>
        public bool fortressAfterResources;

        /// <summary>Seconds between the "TERRITORY production:" count lines.</summary>
        public float territoryReportInterval;

        /// <summary>How far (m) from a territory's seed the reserved Fortress
        /// spot may be sought.</summary>
        public float fortressSpotSearchRadius;

        /// <summary>Build cells kept clear around a reserved Fortress spot, so
        /// the Fortress also keeps a free side.</summary>
        public int fortressSpotMarginCells;

        // ── Economic drive and the Vault (docs/Design/Game_AI.md § 5h) ──

        /// <summary>The first-Religion-Point hunt launches only when the
        /// army's power is at least this multiple of the curse node's.
        /// Every tier: the hunt never fights at parity.</summary>
        public float religionHuntPowerMargin;

        /// <summary>Seconds between economy-pass runs per faction: the Vault
        /// decision and the node-built scan.</summary>
        public float econPassInterval;

        /// <summary>Bank kept back from any Vault deposit, per resource, on
        /// top of every pending savings goal.</summary>
        public int vaultKeepSupplies;
        public int vaultKeepIron;
        public int vaultKeepVeilstone;
        public int vaultKeepVeilsteel;

        /// <summary>Smallest deposit worth locking the Vault for.</summary>
        public int vaultMinDeposit;

        /// <summary>Below this fraction of its health the Vault is emptied
        /// (stored resources die with it).</summary>
        public float vaultDamagedFraction;

        /// <summary>Seconds between the periodic ECON / PRODUCTION summary
        /// lines per faction.</summary>
        public float econLogInterval;

        // ── Economy defence and distress (docs/Design/Game_AI.md § 5i) ──

        /// <summary>An economy asset (extractor, house, worker) counts as
        /// under attack while its last attacker is alive, hostile and within
        /// this many metres of it.</summary>
        public float economyDefenceProbeRadius;

        /// <summary>Radius (m) the response reads the attacker's power in:
        /// mobile army plus static defences, curse included (AIEngagement).</summary>
        public float economyDefenceAssessRadius;

        /// <summary>Only standing soldiers within this many metres of the
        /// attack are drafted into a response.</summary>
        public float economyDefenceDraftRadius;

        /// <summary>A response is released (its soldiers walk home) when no
        /// hostile power is left at the site, or after this many seconds.</summary>
        public float economyDefenceTimeoutSeconds;

        /// <summary>Seconds between top-ups of a response the attacker still
        /// outweighs.</summary>
        public float economyDefenceTopUpSeconds;

        /// <summary>Attack sites one think may answer (the rest wait a
        /// think).</summary>
        public int economyDefenceSitesPerThink;

        /// <summary>Seconds an economy attack keeps the faction "under
        /// attack" for the savings pause after its last sighting.</summary>
        public float economyAttackLingerSeconds;

        /// <summary>A lost extractor is owed a rebuild (first in the extractor
        /// walk, a strict savings goal) for this many seconds.</summary>
        public float extractorRebuildWindowSeconds;

        /// <summary>Seconds between extractor walks while a rebuild is owed
        /// (instead of extractorAttemptInterval).</summary>
        public float extractorRebuildAttemptInterval;

        /// <summary>Extractor walks in a row that may fail to place an owed
        /// rebuild on any free node before the debt is dropped.</summary>
        public int extractorRebuildMaxRefusals;

        /// <summary>An extractor site lost once is not rebuilt while a live
        /// curse node stands within this many metres of it.</summary>
        public float extractorCurseKeepoutRadius;

        /// <summary>An extractor site lost this many times (to anyone)...</summary>
        public int extractorSiteMaxLosses;

        /// <summary>...rests this many seconds before it is built again.</summary>
        public float extractorSiteBlockSeconds;

        /// <summary>Smallest reclaim squad that may march on a curse node —
        /// no one-unit sorties.</summary>
        public int reclaimMinSquadSize;

        /// <summary>Seconds after a reclaim squad marched on a curse node
        /// before another may march on it (a failed attempt is not repeated
        /// at once, and a live attempt is not fed piecemeal).</summary>
        public float reclaimRetrySeconds;

        /// <summary>Supply income below this fraction of the expected income
        /// is COLLAPSED: ordinary savings goals pause.</summary>
        public float economyCollapseIncomeFraction;

        /// <summary>...and resume once income is back above this fraction
        /// (hysteresis; above economyCollapseIncomeFraction).</summary>
        public float economyRecoveredIncomeFraction;

        /// <summary>The expected supply income is the best income the faction
        /// has measured, decaying with this half-life (seconds).</summary>
        public float economyExpectedHalfLifeSeconds;

        /// <summary>The expected income may rise at most this fraction per
        /// second toward a higher measurement, so a one-off windfall (a Vault
        /// withdrawal, a veilsteel sale) does not read as the norm.</summary>
        public float economyExpectedRisePerSecond;

        /// <summary>No collapse is read while the expected supply income is
        /// below this (supplies per second): the opening is not a collapse.</summary>
        public float economyCollapseMinExpected;

        // ── Waves without intel (Game_AI.md § 6a) ──

        /// <summary>A wave whose objective nobody has seen holds for recon at
        /// most this many seconds, then marches anyway. A start position is
        /// public knowledge and never waits.</summary>
        public float waveIntelHoldMaxSeconds;

        // ── The idle army clears the curse (Game_AI.md § 5b) ──

        /// <summary>Seconds between curse-clearing decisions per faction.</summary>
        public float curseClearInterval;

        /// <summary>Idle soldiers above the standing floor a curse-clearing
        /// sortie needs before it goes.</summary>
        public int curseClearMinUnits;

        /// <summary>The sortie goes only when its power is at least this
        /// multiple of the curse's at the node it attacks first.</summary>
        public float curseClearPowerMargin;

        // ── Curse node intel (Game_AI.md § 5i rule 2; SimpleAISystem.CurseIntel.cs) ──

        /// <summary>Half-life, seconds, of the highest curse power this
        /// faction has seen at a node: the memory fades slowly, so a low
        /// reading later never erases it at once.</summary>
        public float curseIntelLastSeenHalfLifeSeconds;

        /// <summary>A curse node not in sight for this many seconds is
        /// treated as reinforced (curseIntelStaleFactor).</summary>
        public float curseIntelLookSeconds;

        /// <summary>Multiplier on the estimate of a node nobody has looked at
        /// within curseIntelLookSeconds.</summary>
        public float curseIntelStaleFactor;

        /// <summary>Fraction of the curse's config garrison (garrisonCap x
        /// armyGrowth^n at the match's tier) assumed to stand at any node —
        /// below 1 because waves and parties draft from it.</summary>
        public float curseIntelBaselineFraction;

        /// <summary>Multiplier on every curse estimate for fighting on cursed
        /// ground (its slow and damage over time).</summary>
        public float curseIntelGroundFactor;

        /// <summary>Seconds between INTEL lines for one curse node.</summary>
        public float curseIntelLogInterval;

        /// <summary>A running first-RP hunt is called off — and its hunters
        /// walked home — when its roster plus newcomers fall below this
        /// multiple of the node's estimate (the launch needs
        /// religionHuntPowerMargin).</summary>
        public float religionHuntCallOffMargin;

        // ── The Outposts' veilstone glut (Game_AI.md § 5a) ──

        /// <summary>Minutes of the army plan's veilstone spend the bank should
        /// hold; above that (and outpostVeilstoneNeedFloor) the Outposts stop
        /// buying.</summary>
        public float outpostVeilstoneNeedMinutes;

        /// <summary>Veilstone the bank always keeps before the Outposts stop
        /// buying, whatever the plan's spend.</summary>
        public int outpostVeilstoneNeedFloor;

        /// <summary>Buying resumes when veilstone falls below this fraction of
        /// the need (hysteresis).</summary>
        public float outpostBuyResumeFraction;

        /// <summary>Iron banked at or above which iron counts as piling up
        /// for the composition's resource tilt.</summary>
        public int glutIronAbove;

        /// <summary>While veilstone or iron piles up and the army is short of
        /// supplies, each role's share is scaled by 1 + this x (1 - 2 x its
        /// supplies share of the unit's cost): units paid mostly in the
        /// surplus resources weigh more. 0 = off.</summary>
        public float glutCompositionTilt;

        // ── Missions on a big map and at a wall (2026-10-05, Game_AI.md 6a) ──

        /// <summary>Metres per second an army is assumed to cover on the
        /// march, for a mission's deadline: missionTimeoutSeconds plus the
        /// march at this speed. 0 = the flat timeout from launch.</summary>
        public float missionMarchSpeedForTimeout;

        /// <summary>Seconds a striking army may stand still short of its
        /// objective before it looks for the hostile wall piece stopping it.</summary>
        public float wallBreachAfterSeconds;

        /// <summary>How far from the stalled army's centroid a hostile wall
        /// piece counts as the wall in the way.</summary>
        public float wallBreachRadius;

        /// <summary>Siege engines the army needs with it to breach; with
        /// fewer the mission ends rather than stand under the towers.</summary>
        public int wallBreachMinSiege;

        // ── Reconquest (2026-10-05, Game_AI.md 5b) ──

        /// <summary>Score penalty on a rival's locked, unwalled territory as
        /// a claim candidate: free land first, then ground to take back.</summary>
        public float claimHostileTargetPenalty;

        /// <summary>Ceiling on the veilstone the Outposts buy toward (the
        /// four-minute plan need, § 5a); 0 = no ceiling.</summary>
        public int outpostVeilstoneNeedMax;

        /// <summary>Seconds the collapse reading must hold before the
        /// savings pause flips on or off (§ 5i).</summary>
        public float economyCollapseConfirmSeconds;

        /// <summary>A wave also launches once this share of the LIVE army
        /// (above the standing floor) stands idle, never below the tier's
        /// base bar (§ 6a). 0 = the desired-army bar only.</summary>
        public float waveLiveArmyShare;

        /// <summary>A site search skips candidates within this of a spot where
        /// the faction's plan was cancelled for a persistent refusal
        /// (Planned_Buildings.md, the grace).</summary>
        public float refusedSpotRadius;

        /// <summary>An overdue wave still holds while the assessed enemy /
        /// own power ratio at the objective exceeds this (§ 6a). 0 = the
        /// overdue release ignores the assessment, as before.</summary>
        public float waveOverdueMaxRatio;

        /// <summary>While the army is below this fraction of its target, a
        /// combat unit passes every non-strict savings hold and the budget
        /// reservation (§ 5f). The Rebuild line is half.</summary>
        public float armyEssentialFraction;

        /// <summary>After the age-up the desired army is at least this many
        /// combat units per territory held (capped at the plan's army cap),
        /// whatever the savings goals (§ 3a). 0 = off.</summary>
        public float armyPerTerritory;

        // ── Many armies (§ 6f) ──

        /// <summary>The smallest body that counts as an army of its own; a
        /// wave launches fewer armies than the tier's concurrentArmies when
        /// the draft cannot give each at least this many.</summary>
        public int armyMinUnits;

        /// <summary>Two armies of one wave approach from bearings (as seen
        /// from the victim's capital) at least this far apart, in degrees.</summary>
        public float armySeparationDegrees;

        /// <summary>...and their objectives stand at least this far apart.</summary>
        public float armySeparationMeters;

        /// <summary>A staged army waits this long for its sister armies to
        /// stage before it strikes alone.</summary>
        public float armySyncTimeoutSeconds;

        /// <summary>Income-target ranking (§ 6f): the value of an extractor
        /// (Gatherer's Hut, Mine, Veilstone Mine, Trading Outpost), of a
        /// military building, and of a house or other eco building. Each
        /// is worth 100 points per unit of weight before distance, defence
        /// and intel age are charged by the target scorer's rates.</summary>
        public float incomeWeightExtractor;
        public float incomeWeightMilitary;
        public float incomeWeightHouse;

        /// <summary>Score taken off an income target standing in its owner's
        /// capital territory, so the surrounding holdings fall first (2026-10-07,
        /// Game_AI.md § 6h).</summary>
        public float incomeCapitalPenalty;

        /// <summary>Income recon (§ 6f): while fewer than this many hostile
        /// income buildings are known, an income-targeting tier files a
        /// recon request every incomeReconIntervalSeconds at the nearest
        /// hostile start and then at points incomeReconSpreadMeters around it.</summary>
        public int incomeReconMinKnown;
        public float incomeReconIntervalSeconds;
        public float incomeReconSpreadMeters;
    }
}
