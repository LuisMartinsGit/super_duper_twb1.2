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

        // ── Economy ───────────────────────────────────────────────────────






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



    }
}
