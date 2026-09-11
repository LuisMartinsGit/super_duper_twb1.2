// Difficulty-tier knobs for the SimpleAISystem executor.
//
// AoE4 model (docs/Design/Game_AI.md §2, docs/Research/AoE4_AI_Study.md §2):
// ONE brain, difficulty expressed purely as data — behavior quality only,
// never resource or vision cheats. Each tier is a full profile of knobs
// (think rate, worker targets, attack timing, raiding / counter-composition
// / staging / expansion toggles), the equivalent of Relic's per-difficulty
// personality Lua files.
//
// Those knobs used to be a switch of four hard-coded arms in this file. They
// are four ASSETS now, in Profiles/ — the ladder is the thing a designer
// retunes most often, and it should never have needed a recompile.

using UnityEngine;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Complete per-difficulty tuning profile, as the executor consumes it.
    ///
    /// Still a struct: it is read on the AI hot path every think tick, and the
    /// call sites pass it around by value. <see cref="AIDifficultyProfileSO"/>
    /// is the authored form, this is the runtime form, and
    /// <see cref="AISimpleDifficulty.GetProfile"/> is the only thing that
    /// converts between them.
    /// </summary>
    public struct AIDifficultyProfile
    {
        /// <summary>Seconds between AI think-ticks. Lower = faster reactions.</summary>
        public float ThinkInterval;
        /// <summary>How stale an enemy sighting may be and still steer
        /// production (layer 3 reads this; see the SO field).</summary>
        public float IntelFreshnessSeconds;
        /// <summary>No attack missions launch before this game time (seconds).</summary>
        public float FirstAttackEarliestSeconds;
        /// <summary>How large an army this difficulty keeps standing, before
        /// the plan's ArmyScale multiplies it.</summary>
        public int SustainArmyCap;
        /// <summary>Seconds between wave launches once the first-attack gate
        /// has passed.</summary>
        public float AttackWaveIntervalSeconds;
        /// <summary>Idle-army minimum for wave 1.</summary>
        public int WaveBaseUnits;

        /// <summary>Multiplier on the support systems' fixed think cadence
        /// (endgame directors, building upgrades, scouting).</summary>
        public float SupportThinkScale;
    }

    /// <summary>
    /// Per-difficulty tuning for the SimpleAISystem. Difficulty is data —
    /// every tier runs the identical brain.
    /// </summary>
    public static class AISimpleDifficulty
    {
        /// <summary>
        /// The tier's profile, read from its asset.
        ///
        /// A missing tier is a DATA bug and is logged as one, exactly as a
        /// missing component config is. The zeroed struct that comes back is
        /// not a playable fallback and is not meant to be: an AI that thinks
        /// every 0 seconds and wants a 0-unit army is loud, which is the point.
        /// </summary>
        public static AIDifficultyProfile GetProfile(AIDifficulty d)
        {
            var ladder = AISimpleDifficultyConfig.I;
            var so = ladder != null ? ladder.Find(d) : null;

            if (so == null)
            {
                Debug.LogError($"[AI] No difficulty profile asset for {d}. Add one under "
                             + "Assets/GameSystems/AI/AISimpleDifficulty/Profiles/ and list it "
                             + "in AISimpleDifficulty.asset.");
                return default;
            }

            return new AIDifficultyProfile
            {
                ThinkInterval = so.thinkInterval,
                IntelFreshnessSeconds = so.intelFreshnessSeconds,
                FirstAttackEarliestSeconds = so.firstAttackEarliestSeconds,
                SustainArmyCap = so.sustainArmyCap,
                AttackWaveIntervalSeconds = so.attackWaveIntervalSeconds,
                WaveBaseUnits = so.waveBaseUnits,
                SupportThinkScale = so.supportThinkScale,
            };
        }

        /// <summary>Seconds between AI think-ticks (profile shorthand).</summary>
        public static float GetThinkInterval(AIDifficulty d) => GetProfile(d).ThinkInterval;
    }
}
