// Difficulty-tier knobs for the SimpleAISystem build-order executor.
// Maps the existing 4-value AIDifficulty enum (Easy/Normal/Hard/Expert) onto
// the 3-tier behaviour the simple AI cares about (Easy/Medium/Hard).
// Location: Assets/Scripts/AI/AISimpleDifficulty.cs

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Per-difficulty tuning for the SimpleAISystem.
    /// </summary>
    public static class AISimpleDifficulty
    {
        /// <summary>
        /// Seconds between AI think-ticks. Lower = faster reactions.
        /// </summary>
        public static float GetThinkInterval(AIDifficulty d) => d switch
        {
            AIDifficulty.Easy   => 5.0f,
            AIDifficulty.Normal => 2.0f,
            AIDifficulty.Hard   => 0.5f,
            AIDifficulty.Expert => 0.5f,
            _                   => 2.0f,
        };

        /// <summary>
        /// Multiplier applied to the AI faction's gather/income rates so the
        /// SimpleAISystem can scale economy without touching the simulation.
        /// 0.7×, 1.0×, 1.3× match the timing tables you signed off on.
        /// </summary>
        public static float GetIncomeMultiplier(AIDifficulty d) => d switch
        {
            AIDifficulty.Easy   => 0.7f,
            AIDifficulty.Normal => 1.0f,
            AIDifficulty.Hard   => 1.3f,
            AIDifficulty.Expert => 1.3f,
            _                   => 1.0f,
        };

        /// <summary>
        /// Probability per tick (0..1) that Easy difficulty randomly skips a
        /// non-critical step (occasional miner, GHut beyond the first). Never
        /// applies to Hut, Barracks, Choice, or AgeUp — those are required to
        /// avoid getting the AI stuck.
        /// </summary>
        public static float GetSkipChance(AIDifficulty d) => d switch
        {
            AIDifficulty.Easy => 0.10f,
            _                 => 0.0f,
        };
    }
}
