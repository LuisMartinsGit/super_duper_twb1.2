// AIComposition.cs
// LAYER 3 of the AI stack: COUNTERS AND MILITARY STRATEGY — the only layer
// allowed to decide WHICH unit gets trained.
//
// ── The four layers ──────────────────────────────────────────────────────
//   1. Difficulty   decision rates, mistakes, reaction times, army size
//                   (AIDifficultyProfileSO + the four Profiles/*.asset)
//   2. Personality  what to PRIORITISE — military / economy / tech / defence
//                   (AISettingsSO.PersonalityBlock, one per AIPersonality)
//   3. Composition  what to TRAIN, and what it counters          <-- here
//   4. Tactical     combat micro, targeting, flanking, abilities, heroes
//                   (TargetScorer, AIEngagement, TacticalQuery, ThreatMaps)
//
// Each layer may read the one above it and must not reach into the one below.
//
// ── What this file exists to stop ────────────────────────────────────────
// Two layers were choosing units, and neither was this one.
//
//   * PERSONALITY chose units directly. AIBuildOrder's step lists named
//     "Spearman" four to seven times depending on the opening, and the Turtle
//     opener alone named "Litharch" — a personality picking a unit TYPE, not
//     a priority. Build orders name a ROLE now (see BuildOrderStep.Train), so
//     a personality can ask for seven military units early without saying
//     what they are, and the id it would have named cannot be written down.
//
//   * DIFFICULTY chose whether to counter at all. counterCompEnabled was a
//     bool on the difficulty profile, and it shipped FALSE on Easy and
//     Normal — so a Normal AI never looked at the enemy army. A human went
//     all-in on 106 archers and the AI answered with heavy infantry for forty
//     minutes because the branch that reads enemy composition was compiled
//     out by the difficulty tier. Countering is unconditional now; difficulty
//     expresses skill through IntelFreshnessSeconds — how stale a sighting
//     may be and still steer production — which is a competence, not a
//     capability.
//
// Layer 2 hands this layer a RoleBudget: the shape of army it wants. This
// layer decides what fills it, using the enemy's composition, the roster the
// culture actually owns, and what the bank is drowning in.

using TheWaningBorder.Data.AI;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// What a unit is FOR. The vocabulary layer 2 is allowed to speak in —
    /// deliberately not a unit id, so a build order cannot name one.
    /// </summary>
    public enum UnitRole : byte
    {
        Worker = 0,
        Scout = 1,
        /// <summary>Whatever this culture's front line currently is.</summary>
        Melee = 2,
        Ranged = 3,
        Cavalry = 4,
        Siege = 5,
        /// <summary>Healer / caster tier (the Litharch line).</summary>
        Support = 6,
        /// <summary>"Whatever the army is short of" — the normal case, and
        /// what every military build-order step should ask for.</summary>
        Military = 7,
    }

    /// <summary>
    /// The layer 2 → layer 3 handoff: the SHAPE of army a personality wants,
    /// as fractions of the whole. Layer 3 treats these as the baseline it
    /// starts from, then bends them against what the enemy is actually
    /// fielding — a counter always outranks a preference.
    ///
    /// Fractions, never counts: HOW MANY units to keep is
    /// PersonalityBlock.militaryFloor times the difficulty's army scale.
    /// This is only the mix.
    /// </summary>
    public struct RoleBudget
    {
        public float RangedFrac;
        public float CavalryFrac;
        public float SiegeFrac;

        /// <summary>
        /// The baseline mix for one personality. A rusher wants bodies and
        /// horses early; a turtle wants a shooting line behind walls and the
        /// engines to break a siege.
        /// </summary>
        public static RoleBudget For(AIPersonality p) => p switch
        {
            AIPersonality.Rush       => new RoleBudget { RangedFrac = 0.30f, CavalryFrac = 0.25f, SiegeFrac = 0.05f },
            AIPersonality.Aggressive => new RoleBudget { RangedFrac = 0.35f, CavalryFrac = 0.25f, SiegeFrac = 0.08f },
            AIPersonality.Economic   => new RoleBudget { RangedFrac = 0.40f, CavalryFrac = 0.15f, SiegeFrac = 0.10f },
            AIPersonality.TechBoom   => new RoleBudget { RangedFrac = 0.45f, CavalryFrac = 0.15f, SiegeFrac = 0.20f },
            AIPersonality.Defensive  => new RoleBudget { RangedFrac = 0.50f, CavalryFrac = 0.10f, SiegeFrac = 0.15f },
            AIPersonality.Turtle     => new RoleBudget { RangedFrac = 0.55f, CavalryFrac = 0.08f, SiegeFrac = 0.20f },
            _                        => new RoleBudget { RangedFrac = 0.40f, CavalryFrac = 0.18f, SiegeFrac = 0.10f },
        };
    }
}
