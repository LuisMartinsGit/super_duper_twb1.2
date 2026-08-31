// AIPlan.cs
// The AI's STRATEGIC PLAN — a named, committed intention you can read off the
// screen.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY
//
// A 30-minute four-AI match produced 23 Workers, 17 Spearmen and 4 Scouts.
// Nothing else. No archers, no cavalry, no siege, from four AIs that had
// built a Royal Stable and a Siege Yard between them. Armies sat at 4-12
// against a SustainArmyCap of 24, waves were blocked 32 times against 12
// launches, and all four converged on the same region eight times.
//
// It read as "the AI pointlessly makes units and sends them at whatever is
// closest" because there was no intention anywhere in the system to read. The
// budget allocator was steering by SITUATION — posture, a supply-famine flag,
// an age-up gate — and situations are the same for everybody, so four AIs
// with five different personalities all played the same game.
//
// The throttle was in that allocator. While an age-up was pending it set
// adv/mil/eco to 0.65/0.20/0.10 "last and unconditionally", and the
// Advancement wallet NEVER LENDS. One logged AI therefore held 143 iron in
// Advancement while its Military wallet had 22 against a 45-iron Swordsman —
// with 6,113 supplies and 15,242 veilstone in the bank. It was not poor. It
// was not allowed to spend.
//
// A plan replaces "what is happening to me" with "what am I doing about it":
// it owns the budget split, how big an army it wants, how much it expects
// before attacking, and how hard it expands. It is COMMITTED for a window, so
// it is long enough to see, and it is announced, so it is possible to say
// "that one is massing" and be right.
// ─────────────────────────────────────────────────────────────────────────

namespace TheWaningBorder.AI
{
    /// <summary>What an AI is currently trying to do. One per faction, held
    /// for at least <see cref="AIPlanProfile.CommitSeconds"/>.</summary>
    public enum AIPlan : byte
    {
        /// <summary>Take ground and build economy. Few troops.</summary>
        Boom = 0,
        /// <summary>Build a large army, then commit it in one push.</summary>
        Mass = 1,
        /// <summary>Constant early pressure with whatever is to hand.</summary>
        Rush = 2,
        /// <summary>Race the age-up and the elite tier it unlocks.</summary>
        Tech = 3,
        /// <summary>Hold what we have: defences, tech, no adventures.</summary>
        Fortress = 4,
    }

    /// <summary>
    /// Everything a plan decides. One row per plan, and these numbers ARE the
    /// difference between the plans — a plan that does not move the budget,
    /// the army target and the attack bar is a label, not a strategy.
    /// </summary>
    public struct AIPlanProfile
    {
        /// <summary>Budget split. Overrides the situational policy.</summary>
        public float WeightAdv, WeightMil, WeightEco;

        /// <summary>Multiplies the difficulty's SustainArmyCap. This is what
        /// makes "amassing a huge army" a thing that visibly happens.</summary>
        public float ArmyScale;

        /// <summary>Multiplies the idle-units bar a wave must clear before it
        /// launches. Below 1 attacks early and often; above 1 hoards and hits
        /// once, hard.</summary>
        public float WaveBarScale;

        /// <summary>0 = never expand, 1 = normal, >1 = claim eagerly.</summary>
        public float ClaimAppetite;

        /// <summary>Minimum time this plan is held. Long enough to read from
        /// the outside and to actually accomplish something; an AI that
        /// re-decides every think tick has no strategy, only a mood.</summary>
        public float CommitSeconds;

        /// <summary>One line, for the log and any UI that wants to show what
        /// this AI is up to.</summary>
        public string Banner;
    }

    public static class AIPlans
    {
        public static AIPlanProfile Of(AIPlan plan) => plan switch
        {
            // Ground first. Light army — a booming AI is making a BET that it
            // will not be punished, which is what makes punishing it fun.
            AIPlan.Boom => new AIPlanProfile
            {
                WeightAdv = 0.15f, WeightMil = 0.20f, WeightEco = 0.65f,
                // ArmyScale 0.6 -> 0.9, WaveBar 1.2 -> 1.0 (2026-08-31,
                // equal-win-rate directive). "Thin on army" was a bet that
                // the relentless-wave meta punishes EVERY time: the economy
                // identity was eliminated in 10 of 14 batch matches and won
                // zero. A boomer still fields less than Mass (1.6) — but a
                // bigger estate must mean a bigger army, or booming is just
                // queueing for execution.
                ArmyScale = 0.9f, WaveBarScale = 1.0f, ClaimAppetite = 2f,
                CommitSeconds = 100f,
                Banner = "BOOMING — taking ground, thin on army",
            },

            // The one you should be able to SEE coming. Military gets the
            // majority of income, the army target goes well past the normal
            // cap, and the wave bar rises so it does not dribble the army away
            // in small attacks while it is still growing.
            AIPlan.Mass => new AIPlanProfile
            {
                WeightAdv = 0.15f, WeightMil = 0.60f, WeightEco = 0.25f,
                ArmyScale = 1.6f, WaveBarScale = 1.6f, ClaimAppetite = 0.6f,
                CommitSeconds = 140f,
                Banner = "MASSING — building a large army for one push",
            },

            // Opposite trade to Mass: same military share, but it attacks with
            // whatever it has rather than saving. Small constant violence.
            AIPlan.Rush => new AIPlanProfile
            {
                WeightAdv = 0.10f, WeightMil = 0.60f, WeightEco = 0.30f,
                ArmyScale = 0.9f, WaveBarScale = 0.5f, ClaimAppetite = 0.4f,
                CommitSeconds = 90f,
                Banner = "RUSHING — constant pressure, attacks light",
            },

            // The old permanent state, now time-boxed. Advancement takes the
            // lion's share ONLY while this plan is held, and the commit window
            // is what stops a pending age-up starving the army for the rest of
            // the match.
            AIPlan.Tech => new AIPlanProfile
            {
                WeightAdv = 0.55f, WeightMil = 0.25f, WeightEco = 0.20f,
                // ArmyScale 0.8 -> 1.0 (2026-08-31, equal-win-rate
                // directive): the tech identity's payoff is QUALITY, and
                // quality needs enough bodies to survive to its spike —
                // Yellow was eliminated in 11 of 14 batch matches.
                ArmyScale = 1.0f, WaveBarScale = 1.3f, ClaimAppetite = 1f,
                CommitSeconds = 120f,
                Banner = "TECHING — racing the age-up and its elite tier",
            },

            // Answer to somebody else's Mass. Keeps a real standing army but
            // will not go looking for a fight.
            AIPlan.Fortress => new AIPlanProfile
            {
                WeightAdv = 0.30f, WeightMil = 0.45f, WeightEco = 0.25f,
                ArmyScale = 1.3f, WaveBarScale = 2.2f, ClaimAppetite = 0.5f,
                CommitSeconds = 110f,
                Banner = "FORTIFYING — holding ground, army stays home",
            },

            _ => Of(AIPlan.Boom),
        };

        /// <summary>
        /// How much this personality likes a plan, as a score bonus. Keeps the
        /// four AIs in a match from converging on one answer: the counter-play
        /// reads the same board for everybody, so without a personal bias they
        /// would all counter it the same way — which is how four AIs with five
        /// different personalities ended up playing one identical game.
        /// </summary>
        /// Tuned against a board sweep: at these weights an AMBIGUOUS board
        /// (the opening, being slightly behind) splits four AIs across four
        /// different plans, while a DECISIVE one (an enemy deathball, an enemy
        /// in the base) still collapses everybody onto the single right answer.
        /// At the first values tried — a third of these — the board signal
        /// swamped personality and five of seven test boards produced one
        /// identical plan for all five personalities, which is the convergence
        /// this whole layer exists to break.
        public static float Affinity(AIPersonality p, AIPlan plan) => p switch
        {
            AIPersonality.Aggressive => plan == AIPlan.Rush ? 42f
                                      : plan == AIPlan.Mass ? 26f : 0f,
            AIPersonality.Rush       => plan == AIPlan.Rush ? 55f
                                      : plan == AIPlan.Mass ? 18f : 0f,
            AIPersonality.Defensive  => plan == AIPlan.Fortress ? 46f
                                      : plan == AIPlan.Tech ? 22f : 0f,
            AIPersonality.Economic   => plan == AIPlan.Boom ? 46f
                                      : plan == AIPlan.Tech ? 26f : 0f,
            _                        => plan == AIPlan.Mass ? 20f
                                      : plan == AIPlan.Tech ? 10f : 0f,
        };
    }
}
