// AICultureChoice.cs
// Decides which culture an AI adopts at age-up, from its PERSONALITY, its
// DIFFICULTY, and the intel it has actually scouted by that moment.
//
// Why this exists: CultureFor() used to be a hardcoded `=> Alanthor` from the
// demo build, so every AI of every personality at every difficulty played the
// same faction. This restores the choice and makes it mean something.
//
// The decision is a single signed score — negative picks Alanthor, positive
// picks Feraldis — built from three layers:
//
//   1. PERSONALITY prior (AIBuildOrder.CultureLeanFor). A Rusher leans
//      Feraldis, a Turtle leans Alanthor. This alone would be the old
//      commented-out per-strategy table.
//   2. INTEL, weighted by DIFFICULTY. Everything read here is fog-honest:
//      IntelSystem only records what this faction has actually seen, so an
//      AI that never scouted decides on personality alone — which is exactly
//      how it should feel.
//   3. A deterministic seed jitter, so two AIs with identical personality and
//      identical intel don't always mirror each other.
//
// Difficulty controls how much layer 2 counts. Easy barely reads the map
// (it mostly plays its personality); Hard reads it fully. That makes
// difficulty affect DECISION QUALITY, not just unit stats.
//
// Location: Assets/Scripts/AI/AICultureChoice.cs

using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    public static class AICultureChoice
    {
        // ==================== Intel weights ====================

        /// <summary>Enemy military strength that reads as "they are the
        /// stronger army". Scored toward Alanthor — fortify and survive.</summary>
        private const float EnemyStrengthPivot = 400f;
        private const float EnemyStrengthWeight = 2.0f;

        /// <summary>Each scouted enemy Hall beyond the first is another
        /// economy to raid. Scored toward Feraldis.</summary>
        private const float EnemyBaseWeight = 0.6f;

        /// <summary>Scouted enemy ECONOMY (miners, eco buildings) with little
        /// military cover is the classic raid invitation.</summary>
        private const float ExposedEconomyWeight = 0.35f;

        /// <summary>Our own combat record. Winning fights says "keep
        /// fighting" (Feraldis); losing armies says "turtle" (Alanthor).</summary>
        private const float SuccessWeight = 0.5f;
        private const float LossWeight = 0.7f;

        /// <summary>Being poor pushes toward the culture that STEALS its
        /// income rather than the one that gathers harder.</summary>
        private const float PovertyWeight = 0.8f;
        private const int PovertySuppliesFloor = 250;

        /// <summary>Jitter amplitude, so identical situations still vary.</summary>
        private const float SeedJitter = 0.5f;

        /// <summary>
        /// How strongly each difficulty trusts scouted intel over its raw
        /// personality prior. Easy essentially plays its personality; Hard
        /// weighs what it has seen at full strength.
        /// </summary>
        private static float IntelWeightFor(AIDifficulty difficulty) => difficulty switch
        {
            AIDifficulty.Easy   => 0.15f,
            AIDifficulty.Normal => 0.55f,
            AIDifficulty.Hard   => 1.0f,
            AIDifficulty.Expert => 1.3f,
            _                   => 0.55f,
        };

        /// <summary>
        /// Pick the culture this AI should adopt. Safe to call with a null
        /// brain entity — it degrades to the personality prior.
        /// </summary>
        public static byte Pick(EntityManager em, Faction faction, Entity brainEntity,
            AIStrategy strategy, AIDifficulty difficulty, uint randomSeed)
        {
            // Layer 1 — personality.
            float score = AIBuildOrder.CultureLeanFor(strategy);

            // Layer 2 — intel, scaled by difficulty.
            float intel = ScoreIntel(em, faction, brainEntity);
            score += intel * IntelWeightFor(difficulty);

            // Layer 3 — deterministic jitter (lockstep-safe: no RNG calls,
            // just arithmetic on the seed the caller already advanced).
            float jitter = ((randomSeed % 1000) / 1000f - 0.5f) * 2f * SeedJitter;
            score += jitter;

            // Ship gate: a culture the build does not ship is never adopted,
            // however the score came out. See CultureConfig.Playable.
            return CultureConfig.Playable(
                score > 0f ? Cultures.Feraldis : Cultures.Alanthor);
        }

        /// <summary>
        /// Positive = the map argues for Feraldis (raid it), negative = the
        /// map argues for Alanthor (fortify against it). Reads only what the
        /// faction has genuinely scouted.
        /// </summary>
        private static float ScoreIntel(EntityManager em, Faction faction, Entity brainEntity)
        {
            float score = 0f;

            if (brainEntity != Entity.Null && em.Exists(brainEntity))
            {
                if (em.HasComponent<AISharedKnowledge>(brainEntity))
                {
                    var k = em.GetComponentData<AISharedKnowledge>(brainEntity);

                    // A visibly stronger enemy army argues for walls.
                    if (k.EnemyEstimatedStrength > 0)
                        score -= math_min(k.EnemyEstimatedStrength / EnemyStrengthPivot, 2f)
                                 * EnemyStrengthWeight;

                    // Every extra enemy base is another place worth raiding.
                    if (k.KnownEnemyBases > 1)
                        score += (k.KnownEnemyBases - 1) * EnemyBaseWeight;
                }

                if (em.HasComponent<AIStrategyState>(brainEntity))
                {
                    var s = em.GetComponentData<AIStrategyState>(brainEntity);
                    score += s.SuccessfulAttacks * SuccessWeight;
                    score -= s.ArmiesLostSinceSwitch * LossWeight;
                }

                // Scouted enemy economy that we have NOT seen defended.
                score += ScoreExposedEconomy(em, brainEntity);
            }

            // Broke? Steal instead of digging harder.
            if (FactionEconomy.TryGetResources(em, faction, out var res)
                && res.Supplies < PovertySuppliesFloor)
                score += PovertyWeight;

            return score;
        }

        /// <summary>
        /// Counts scouted enemy economy against scouted enemy military. A fat,
        /// lightly-guarded enemy is the single clearest argument for taking
        /// the raiding culture.
        /// </summary>
        private static float ScoreExposedEconomy(EntityManager em, Entity brainEntity)
        {
            if (!em.HasBuffer<EnemySightingRecord>(brainEntity)) return 0f;
            var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);

            int eco = 0, mil = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                switch (buf[i].Category)
                {
                    case IntelCategory.Miner:
                    case IntelCategory.EcoBuilding:
                        eco++;
                        break;
                    case IntelCategory.MilitaryUnit:
                    case IntelCategory.MilitaryBuilding:
                        mil++;
                        break;
                }
            }

            int exposed = eco - mil;
            if (exposed <= 0) return 0f;
            return math_min(exposed, 8) * ExposedEconomyWeight;
        }

        // Local min helpers — this file is plain managed code and pulling in
        // Unity.Mathematics for two clamps isn't worth the using.
        private static float math_min(float a, float b) => a < b ? a : b;
        private static int math_min(int a, int b) => a < b ? a : b;
    }
}
