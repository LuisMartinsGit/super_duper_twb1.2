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

        // ── Economy ───────────────────────────────────────────────────────

        /// <summary>Worker count the economy manager grows toward before age-up.</summary>
        public int workerTargetAge0;

        /// <summary>...and after age-up (AoE4: villager targets rise per age).</summary>
        public int workerTargetAge1;

        /// <summary>
        /// Gatherer's Huts the maintenance loop grows toward, placed
        /// progressively farther out — income AND (post-age-up) influence,
        /// i.e. MAP CONTROL. This is the main economic separator between
        /// tiers, so the spread is deliberately wide: Easy keeps a modest
        /// home cluster, Expert aims to blanket the map.
        /// </summary>
        public int gathererHutTarget;

        /// <summary>Total military production buildings (Barracks + Archery
        /// Ranges) to build toward, so armies train in parallel.</summary>
        public int productionBuildingTarget;

        /// <summary>
        /// Game time (seconds) at which this AI STOPS expanding and starts
        /// banking for Age 1: the wallet tilts to Advancement, the age-up
        /// director buys its choice building, and new Gatherer's Huts pause
        /// until the age-up is issued.
        ///
        /// Budget backwards from the target age-up time: push, then ~60 s to
        /// raise the 257-supply Shrine, then bank 250 while it builds.
        /// Targets (median, per Age_0.md): Expert ~3 min, Hard ~4, Normal ~5,
        /// Easy ~6.
        /// </summary>
        public float ageUpPushSeconds;

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

        /// <summary>
        /// How large an army this difficulty keeps standing, before the plan's
        /// ArmyScale multiplies it. The population ceiling is ~40 workers plus
        /// ~160 soldiers, so Normal at 100 with MASSING (1.6x) reaches 160.
        /// </summary>
        public int sustainArmyCap;

        // ── Behaviour toggles ─────────────────────────────────────────────

        /// <summary>Peel off fast raid parties at the enemy economy alongside
        /// the main attack (AoE4: Hard+ raids constantly, Easy never).</summary>
        public bool raidingEnabled;

        /// <summary>Adapt the trained unit mix to the observed enemy
        /// composition (AoE4: higher tiers counter-pick, lower don't).</summary>
        public bool counterCompEnabled;

        /// <summary>Attack missions form up at a staging point near the target
        /// before committing (deliberately BETTER than AoE4, which always
        /// rallies at its homebase).</summary>
        public bool forwardStaging;
    }
}
