using UnityEngine;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// One difficulty tier, as an asset. Four of these live in
    /// AISimpleDifficulty/Profiles/ and AISimpleDifficulty.asset points at them.
    ///
    /// This is the tuning surface for the whole AI ladder: every number a
    /// designer would turn to make a tier harder or softer is here, and the
    /// executor reads nothing else about difficulty.
    ///
    /// Deliberately NOT an <c>IComponentConfig</c>: that contract is one asset
    /// per type, and difficulty is one asset per TIER. The single-asset config
    /// is <see cref="AISimpleDifficultyConfig"/>, which holds the four
    /// references and is what the component-config catalog loads.
    ///
    /// All tiers are FAIR — no gather-rate or vision multipliers (the AoE4
    /// hidden-Hardest-cheat lesson). If cheat tiers are ever added they must be
    /// new, clearly labelled entries, not silent buffs to existing ones.
    ///
    /// No field initialisers: the values live in the assets and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/AI/Difficulty Profile",
                     fileName = "Difficulty")]
    public sealed class AIDifficultyProfileSO : ScriptableObject
    {
        /// <summary>Which tier this asset is. The catalog matches on it, so two
        /// assets claiming the same tier is a data bug.</summary>
        public AIDifficulty tier;

        // ── Reaction speed ────────────────────────────────────────────────

        /// <summary>Seconds between AI think-ticks. Lower = faster reactions.</summary>
        public float thinkInterval;

        /// <summary>
        /// LAYER 1's handle on the counter-composition read: how stale an
        /// enemy sighting may be and still steer production. A weaker AI acts
        /// on an older picture of the battlefield.
        ///
        /// This replaces counterCompEnabled, which was a BOOL — difficulty
        /// deciding whether the AI countered at all. Countering is layer 3 and
        /// is now unconditional; difficulty only sets how fresh its
        /// information is. (Normal shipped with the flag off, which is why an
        /// all-archer push went unanswered for a whole match.)
        /// </summary>
        public float intelFreshnessSeconds;

        /// <summary>
        /// Multiplier on the fixed think cadence of the SUPPORT systems — the
        /// two endgame directors, the building-upgrade pass and the scout
        /// director. Below 1 they think more often.
        ///
        /// Difficulty used to reach exactly one system: every tier's endgame,
        /// upgrades and scouting ran on the same hard-coded 5 s / 6 s / 2 s
        /// tick, so an Expert fortified and re-tasked its scouts no faster
        /// than an Easy. This is the one number that fixes that, rather than
        /// four more per-system knobs.
        /// </summary>
        public float supportThinkScale;

        // ── Army composition (docs/Design/Game_AI.md § 5d) ──────────────

        /// <summary>
        /// How hard the scouted enemy army bends the composition: every
        /// perEnemy* term of the role table (SimpleAISystem.asset) is
        /// multiplied by this. 0 = the AI trains its baseline mix whatever it
        /// faces; 1 = the table as authored. Freshness of the read is
        /// intelFreshnessSeconds; this is how WELL the AI answers it.
        /// </summary>
        public float counterResponse;

        /// <summary>
        /// Multiplier on the basics' (Spearman, Archer) share of the army
        /// plan. A weaker tier leans on the cheap, quick basics; a stronger
        /// one invests its veilstone in the role units.
        /// </summary>
        public float basicsShareScale;

        // ── Economy ───────────────────────────────────────────────────────

        /// <summary>
        /// How hard this tier races for territory (Game_AI.md § 5b,
        /// defend-based expansion): multiplies claimMaxParallelSquads
        /// (SimpleAISystem.asset). Below 1 a weaker tier sends fewer claim
        /// squads at once; it never changes whether ground can be held.
        /// </summary>
        public float expansionDrive;

        // ── Economic drive (docs/Design/Game_AI.md § 5h) ─────────────────

        /// <summary>The home capital's next priority level is a SAVINGS GOAL:
        /// while the bank cannot pay for it, its price is registered with
        /// AIPivotalReserve so discretionary army spending of the short
        /// resource waits (the army floors stay exempt; lifted under Defend).
        /// A capital level multiplies every slot of the home territory.</summary>
        public bool reserveForCapitalLevel;

        /// <summary>Economy levels (Gatherer's Hut, Trading Outpost) queued
        /// per upgrade think AHEAD of the rotation and without yielding to
        /// the army. 0 = only the rotation and the surplus pass.</summary>
        public int economyUpgradesPerThink;

        /// <summary>The AI queues into a production building only while its
        /// whole queue is shorter than this. Units are paid at queue time, so
        /// a shallow queue keeps money free to start units in parallel in
        /// other buildings. 1 = only the unit in training. 0 = no AI-side
        /// cap (the building's own cap only).</summary>
        public int productionQueueDepth;

        /// <summary>Skip the Aggressive / Rush Age 0 wave and save for the
        /// age-up from the start, with the 4-unit garrison.</summary>
        public bool protectAgeUpSavings;

        /// <summary>The first-Religion-Point curse hunt waits (and does not
        /// grow the army for it) until the home capital stands at this
        /// level. 0 = no deferral; 1 = aged up.</summary>
        public int curseHuntMinCapitalLevel;

        /// <summary>Share of the idle surplus (bank above the keep floors and
        /// pending savings goals) one Vault deposit takes.</summary>
        public float vaultDepositShare;

        /// <summary>Seconds a deposit stays in the Vault before it is
        /// withdrawn on a timer (the Vault's own lock is the floor).</summary>
        public float vaultHoldSeconds;

        /// <summary>Withdraw before the timer when a purchase (the army, a
        /// savings goal) is waiting on the stored resource.</summary>
        public bool vaultWithdrawOnNeed;

        /// <summary>UNITS BEFORE ECONOMY (Game_AI.md 5h, 2026-10-05). The
        /// economy drive — the economy-level pass, the capital's savings past
        /// its essential level, Vault deposits, discretionary non-production
        /// buildings (watch towers, coverage extras) — spends only when the
        /// army is at its target, or every production building is training at
        /// its queue depth, or the army cannot use the money. Otherwise the
        /// money goes to units. Off = the drive spends as before.</summary>
        public bool unitsBeforeEconomy;

        /// <summary>THE ARMY NEVER PAYS FOR A FORTRESS (Game_AI.md § 5f,
        /// 2026-10-05, operator directive). On: a combat unit passes every
        /// non-strict savings goal (the Fortress pot, the capital level) and
        /// the budget's lump-sum reservation at any army size, and the
        /// income levels (Gatherer's Hut, Trading Outpost) pass the Fortress
        /// reservation too — Fortresses come out of what the economy makes
        /// beyond what production can spend. The age-up stays strict.</summary>
        public bool armyBeforeSaves;

        /// <summary>Seconds the capital's savings goal may hold the bank
        /// unbroken before it is released for capitalReserveRestSeconds, so
        /// it cannot starve production for long. 0 = no cap.</summary>
        public float capitalReserveMaxHoldSeconds;

        /// <summary>Seconds the capital's savings goal stays released after
        /// it hit capitalReserveMaxHoldSeconds (the capital is still bought
        /// whenever the bank can pay).</summary>
        public float capitalReserveRestSeconds;

        // ── Production capacity (docs/Design/Game_AI.md § 5g) ────────────
        //
        // Harder tiers attack more often; they make up for it with more
        // trainers in parallel, never with a smaller army (developer,
        // 2026-10-05). Placement and levels of production stay exempt from
        // every army-protecting gate.

        /// <summary>The HOME territory's floor: production buildings of EACH
        /// line the faction can build, filled breadth-first (every line to
        /// 1, then to 2, ...).</summary>
        public int homeProductionPerLine;

        /// <summary>Production buildings every other held territory gets
        /// from its own build order (step 4), breadth-first over the lines
        /// the army plan needs most.</summary>
        public int provinceProductionPerTerritory;

        /// <summary>Fraction of the finished production buildings whose
        /// queue holds work for the production to count as SATURATED; any
        /// production building past the floors waits on saturation.</summary>
        public float productionSaturationThreshold;

        /// <summary>Seconds the saturation must hold, unbroken, before an
        /// extra production building may be placed.</summary>
        public float productionSaturationSeconds;

        // ── Economy defence (docs/Design/Game_AI.md § 5i) ────────────────

        /// <summary>Seconds an attack on an extractor, house or worker in
        /// held ground must last before the standing army answers it. Lower =
        /// faster response.</summary>
        public float economyDefenceDelaySeconds;

        /// <summary>The response's power over the attacker's (mobile army and
        /// static defences, the curse included). Never at parity: values at
        /// or below 1 are read as 1.05.</summary>
        public float economyDefenceMargin;

        // ── Aggression ────────────────────────────────────────────────────

        /// <summary>No attack missions launch before this game time (seconds).
        /// AoE4 community measurement: first Hardest attack is around 8 min;
        /// lower tiers attack later.</summary>
        public float firstAttackEarliestSeconds;

        /// <summary>Seconds between wave launches once the first-attack gate
        /// has passed. Lower tiers breathe slower.</summary>
        public float attackWaveIntervalSeconds;

        /// <summary>Idle-army minimum for wave 1.</summary>
        public int waveBaseUnits;

        /// <summary>STANDING ARMY FLOOR (Game_AI.md 6a): a fraction of the
        /// desired army (capped at a third of the population capacity) that
        /// a wave or its reinforcements may never draft below — frequent
        /// waves draw only the surplus above it. 0 = waves draft every idle
        /// unit.</summary>
        public float standingArmyFloorFraction;

        /// <summary>A wave is at least this fraction of the standing army
        /// (combat units not already serving a mission), so frequent waves
        /// are real pushes, not feeder trickles. 0 = no such minimum.</summary>
        public float waveMinArmyFraction;

        // ── Pace (docs/Design/Game_AI.md § 2, "Pace is the ladder") ─────

        /// <summary>Multiplies the claim, territory-development and
        /// extractor cadences (SimpleAISystem.asset's intervals): above 1 the
        /// tier takes and develops ground more slowly, below 1 faster.</summary>
        public float territoryCadenceScale;

        /// <summary>Seconds a territory must have had its resource buildings
        /// up before this tier saves for a Fortress there (§ 5c). The slow
        /// tiers fortify late; Expert at once.</summary>
        public float fortressDelaySeconds;

        /// <summary>Power margin the reconquest squad needs over a rival's
        /// holdings before it goes (§ 5b). 0 = this tier never retakes ground.</summary>
        public float reconquestMargin;

        /// <summary>Multiplies strengthWaveRatio for the late strength-gated
        /// wave (§ 6a): above 1 the tier waits for a bigger edge.</summary>
        public float strengthWaveRatioScale;

        /// <summary>How far this tier plays its personality (§ 3): 1 = the
        /// personality's block as authored, 0 = Balanced. Every numeric
        /// personality value, the plan affinities and the army mix are
        /// blended from Balanced toward the personality by this much, so a
        /// lower tier shows its flavour in full and a higher tier never loses
        /// to a lower one for having drawn the wrong flavour.</summary>
        public float personalityWeight;

        /// <summary>
        /// The largest army this difficulty will keep (Game_AI.md § 2, "the
        /// army cap IS a difficulty knob", 2026-10-05). A hard ceiling on the
        /// desired army: the plan's ArmyScale shapes the target under it and
        /// never past it. Easy is restricted well below the population
        /// ceiling; Expert is not restricted at all.
        /// </summary>
        public int sustainArmyCap;

        // ── Many armies (docs/Design/Game_AI.md § 6f) ─────────────────────

        /// <summary>How many armies one wave launches, each with its own
        /// objective on its own approach bearing, staged to strike together.
        /// 1 = the single blob. Fewer launch when the draft cannot fill
        /// them (armyMinUnits each).</summary>
        public int concurrentArmies;

        /// <summary>Waves aim at the enemy's INCOME — its extractors
        /// (Gatherer's Hut, Mine, Veilstone Mine, Trading Outpost) and the
        /// holdings outside its walls, ranked by value — instead of the
        /// nearest thing or the capital. Off: the nearest-first doctrine.</summary>
        public bool incomeTargeting;

        // ── Tactics (docs/Design/Game_AI.md § 6e) ─────────────────────────

        /// <summary>
        /// What this tier's armies do IN a fight: counter-aware target
        /// choice, kiting, flanking, falling back to regroup, ranged behind
        /// melee, and how long it waits before spending an ability. This,
        /// not thinkInterval, is the meaningful gap between the tiers — the
        /// AI's decision rate was never what made it lose fights.
        /// </summary>
        public AITacticsSkill tactics;

        // ── Behaviour toggles ─────────────────────────────────────────────



    }
}
