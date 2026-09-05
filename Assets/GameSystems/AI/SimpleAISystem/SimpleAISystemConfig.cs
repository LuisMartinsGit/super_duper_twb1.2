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

        /// <summary>Builders a Feraldis faction keeps for base expansion.</summary>
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
        ///   1. WORKER FLOOR — a stalled opener must still grow its miners.
        ///   2. HUT PIPELINE — "lack of supplies means build more huts":
        ///      whenever no Gatherer's Hut is under construction and the
        ///      cost is affordable, start the next one. Huts repay fast
        ///      (120 S + 10 I) and are the supplies engine everything else
        ///      (units, buildings, techs) draws from; the difficulty target
        ///      is irrelevant here — one is simply ALWAYS in flight.
        /// If the pipeline model still lets openers hoard, the escalation is
        /// true per-purpose income budgets (economy/research/expansion/
        /// building/military) — deferred until observed necessary.</summary>
        public int economyWorkerFloor;

        /// <summary>Huts below this count build unconditionally (bootstrap);
        /// past it the ECONOMY WALLET is the pipeline's constraint (M-A —
        /// the flat supplies reserve this replaced lives on in git).</summary>
        public int hutPipelineFreeCount;

        /// <summary>Sweep cadence (seconds). Slow mop-up loop — research
        /// takes 30-90 s per tech, so 20 s keeps every host busy without
        /// hammering the queries.</summary>
        public float researchSweepInterval;

        /// <summary>How often a faction may attempt a claim. A Hall is
        /// expensive and the site search is not free; there is no value in
        /// retrying every tick.</summary>
        public float claimAttemptInterval;

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

        /// <summary>Ignore regions further than this from anything we hold.
        /// A claim across the map is a Hall nobody can defend and builders
        /// walking for a minute to reach it.</summary>
        public float maxClaimReach;

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

        public float stagingGatherRadius;

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
    }
}
