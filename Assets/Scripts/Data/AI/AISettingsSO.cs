// AISettingsSO.cs
// Inspector-tunable knobs for the full-scale player-faction AI
// (docs/AI_Assessment_and_Plan.md M2-M6). ONE asset: Resources/AISettings.asset,
// loaded by AISettings.Get(). There are no field initialisers and no coded
// default table: the asset is the single source of every number here, and a
// missing asset or personality row is a loud load-time error, never a
// silent code-side fallback (CLAUDE.md, component config rule).

using System;
using UnityEngine;
using TheWaningBorder.AI;

namespace TheWaningBorder.Data.AI
{
    [CreateAssetMenu(fileName = "AISettings", menuName = "Waning Border/AI Settings", order = 10)]
    public class AISettingsSO : ScriptableObject
    {
        public float weightWorker;
        public float weightEcoBuilding;
        public float weightMilitaryBuilding;
        public float weightHall;
        public float weightBorderNode;
        public float weightMilitaryUnit;

        public float riskPerDefenseStrength;
        public float defenseProbeRadius;
        public float travelCostPerMeter;
        public float intelAgePenaltyPerSecond;

        public float reconMaxIntelAge;
        public float scoutFleeHealthFraction;

        public int defendThreatThreshold;
        public float defendRadius;

        public float retreatStrengthRatio;
        public float retreatCooldownSeconds;

        /// <summary>
        /// LAYER 2 — what one personality PRIORITISES. Every field here is a
        /// how-much, never a which: no unit id appears in this block, and none
        /// ever should. Unit choice is layer 3 (AIComposition).
        ///
        /// One row per AIPersonality, authored in Resources/AISettings.asset.
        /// docs/Design/Game_AI.md § 3 describes what each row makes the AI do.
        /// </summary>
        [Serializable]
        public class PersonalityBlock
        {
            public AIPersonality personality;
            /// <summary>Min idle units before a maintenance attack launches.</summary>
            public int attackThreshold;
            /// <summary>The standing army kept before anything else military;
            /// multiplied by the PLAN's ArmyScale, never by difficulty
            /// (Game_AI.md § 3a).</summary>
            public int militaryFloor;
            /// <summary>Multiplier on the risk term of the target scorer.
            /// Above 1 is cautious, below 1 takes the fight.</summary>
            public float riskMultiplier;

            // ── Economy / tech priorities ──
            /// <summary>Gatherer's Huts the maintenance loop grows toward
            /// (the early-game figure; the cap doubles over the match for
            /// gathering cultures, Feraldis stays hard-capped).</summary>
            public int gathererHutTarget;
            /// <summary>Military production buildings to build toward.</summary>
            public int productionBuildingTarget;
            /// <summary>Game time (s) at which this AI stops expanding and
            /// banks for the age-up. Lower = techs sooner.</summary>
            public float ageUpPushSeconds;

            // ── Military stances ──
            /// <summary>Peel fast raid parties at the enemy economy.</summary>
            public bool raidingEnabled;

            /// <summary>How much this personality leans on the cheap basics
            /// (Spearman, Archer) in the army plan: multiplies their share
            /// (docs/Design/Game_AI.md § 5d). A rusher wants bodies now; a tech
            /// boomer invests its veilstone in the role units.</summary>
            public float basicsAppetite;

            // ── Plan affinity (AIPlans.Affinity) ──
            // Score bonus this personality adds to each strategic plan when
            // the plan is chosen. Keeps four AIs on one board from converging
            // on one answer: the board signal is the same for everybody, so
            // only a personal bias separates them. Magnitudes were tuned
            // against a board sweep: an AMBIGUOUS board splits four AIs four
            // ways, a DECISIVE one (deathball, base under attack) still
            // collapses them onto the one right plan.
            public float boomAffinity;
            public float massAffinity;
            public float rushAffinity;
            public float techAffinity;
            public float fortressAffinity;

            // ── Fortification ──
            /// <summary>Multiplier on how much ground this personality wants
            /// covered by watch towers. 1 = Balanced.</summary>
            public float towerCoverageScale;
            /// <summary>The weight this block was blended at (For(p, weight));
            /// 1 for an authored row read as is. Carried so the role mix can
            /// be blended by the same amount.</summary>
            [System.NonSerialized] public float weight = 1f;
            /// <summary>Multiplier on the priority of wall work against the
            /// rest of the build list. 1 = Balanced.</summary>
            public float wallPriorityScale;

            // ── Personality doctrines (2026-10-07, Game_AI.md § 3b) ──
            // STRUCTURAL knobs — whether a thing happens at all, and the
            // shape of the base — are taken from the personality's row at
            // every tier (For(p, weight) copies them unblended): a Rush that
            // walls a little, or a Turtle with a slightly wider ring, is not
            // a weaker flavour, it is a different one. MAGNITUDE knobs blend
            // toward Balanced like every other number on the row.

            /// <summary>STRUCTURAL. Off: the faction builds no walls at all
            /// (no plan, no ring corridor kept free). Every row but Rush.</summary>
            public bool wallsEnabled;
            /// <summary>STRUCTURAL. Multiplier on the drawn main-camp ring's
            /// hub offsets from the Fortress (1 = as drawn). A wider ring
            /// fences more building ground; a hub that would leave the home
            /// territory is pulled back toward the drawn ring.</summary>
            public float homeRingScale;
            /// <summary>STRUCTURAL. Besides the home ring, up to this many
            /// held territories bordering hostile ground are walled along
            /// their outer border (0 = home only).</summary>
            public int frontierWallTerritories;
            /// <summary>MAGNITUDE. Share of the idle standing army posted on
            /// the home ring's gates each think (0 = none).</summary>
            public float wallGuardShare;
            /// <summary>STRUCTURAL. Seconds between independent raid parties
            /// sent at the enemy's extractors, on their own timer (0 = raids
            /// only peel off a wave at launch).</summary>
            public float raidIntervalSeconds;
            /// <summary>MAGNITUDE. Multiplier on the raid party size.</summary>
            public float raidPartyScale;
            /// <summary>STRUCTURAL. Game time from which every wave must pass
            /// the strength test (the earlier of this and the config's
            /// strengthWaveAfterSeconds).</summary>
            public float strengthGateFromSeconds;
            /// <summary>STRUCTURAL. On: an overdue wave is never released
            /// past the assessment, nor past the Defend veto.</summary>
            public bool noOverdueRelease;
            /// <summary>MAGNITUDE. Multiplier on the Fortress expansion: the
            /// ceiling, the pace (check interval and the tier's delay are
            /// divided by it) and the bonus for ground bordering a rival.
            /// 1 = Balanced.</summary>
            public float fortressAppetite;

            /// <summary>The affinity for one plan, by plan id.</summary>
            public float AffinityFor(AIPlan plan) => plan switch
            {
                AIPlan.Boom     => boomAffinity,
                AIPlan.Mass     => massAffinity,
                AIPlan.Rush     => rushAffinity,
                AIPlan.Tech     => techAffinity,
                AIPlan.Fortress => fortressAffinity,
                _               => 0f,
            };
        }

        public PersonalityBlock[] personalities;

        /// <summary>
        /// The row for one personality. A missing row is a DATA bug: it is
        /// logged once per personality and a zeroed block is returned so the
        /// callers do not NRE, but the AI it drives will be visibly broken,
        /// which is the point.
        /// </summary>
        /// <summary>
        /// The personality DAMPENED BY TIER (docs/Design/Game_AI.md § 3,
        /// 2026-10-05): every numeric value blended from Balanced toward
        /// <paramref name="p"/>'s row by <paramref name="weight"/> (1 = the
        /// row as authored, 0 = Balanced); a flag takes the personality's
        /// value from a weight of one half. Mixed matches showed an Expert
        /// that drew Defensive or Economic finishing behind a Hard Rush — a
        /// flavour cost more than a tier. A new block each call.
        /// </summary>
        public PersonalityBlock For(AIPersonality p, float weight)
        {
            var target = For(p);
            weight = Mathf.Clamp01(weight);
            if (weight >= 0.999f || p == AIPersonality.Balanced) { target.weight = 1f; return target; }
            var b = For(AIPersonality.Balanced);
            float L(float x, float y) => x + (y - x) * weight;
            int I(int x, int y) => Mathf.RoundToInt(x + (y - x) * weight);
            return new PersonalityBlock
            {
                personality = p,
                weight = weight,
                attackThreshold = I(b.attackThreshold, target.attackThreshold),
                militaryFloor = I(b.militaryFloor, target.militaryFloor),
                riskMultiplier = L(b.riskMultiplier, target.riskMultiplier),
                gathererHutTarget = I(b.gathererHutTarget, target.gathererHutTarget),
                productionBuildingTarget = I(b.productionBuildingTarget, target.productionBuildingTarget),
                ageUpPushSeconds = L(b.ageUpPushSeconds, target.ageUpPushSeconds),
                raidingEnabled = weight >= 0.5f ? target.raidingEnabled : b.raidingEnabled,
                basicsAppetite = L(b.basicsAppetite, target.basicsAppetite),
                boomAffinity = L(b.boomAffinity, target.boomAffinity),
                massAffinity = L(b.massAffinity, target.massAffinity),
                rushAffinity = L(b.rushAffinity, target.rushAffinity),
                techAffinity = L(b.techAffinity, target.techAffinity),
                fortressAffinity = L(b.fortressAffinity, target.fortressAffinity),
                towerCoverageScale = L(b.towerCoverageScale, target.towerCoverageScale),
                wallPriorityScale = L(b.wallPriorityScale, target.wallPriorityScale),
                // Structural knobs: the personality's own value at every tier.
                wallsEnabled = target.wallsEnabled,
                homeRingScale = target.homeRingScale,
                frontierWallTerritories = target.frontierWallTerritories,
                raidIntervalSeconds = target.raidIntervalSeconds,
                strengthGateFromSeconds = target.strengthGateFromSeconds,
                noOverdueRelease = target.noOverdueRelease,
                // Magnitudes blend.
                wallGuardShare = L(b.wallGuardShare, target.wallGuardShare),
                raidPartyScale = L(b.raidPartyScale, target.raidPartyScale),
                fortressAppetite = L(b.fortressAppetite, target.fortressAppetite),
            };
        }

        public PersonalityBlock For(AIPersonality p)
        {
            if (personalities != null)
                for (int i = 0; i < personalities.Length; i++)
                    if (personalities[i] != null && personalities[i].personality == p)
                        return personalities[i];

            int bit = 1 << (int)p;
            if ((_missingLogged & bit) == 0)
            {
                _missingLogged |= bit;
                Debug.LogError($"[AISettings] Resources/AISettings.asset has no personality row for {p}. " +
                               "Author one on the asset; the AI runs on a zeroed block until then.");
            }
            return new PersonalityBlock { personality = p };
        }

        [NonSerialized] private int _missingLogged;

        public float CategoryWeight(IntelCategory c) => c switch
        {
            IntelCategory.Worker            => weightWorker,
            IntelCategory.EcoBuilding      => weightEcoBuilding,
            IntelCategory.MilitaryBuilding => weightMilitaryBuilding,
            IntelCategory.Hall             => weightHall,
            IntelCategory.BorderNode        => weightBorderNode,
            _                              => weightMilitaryUnit,
        };
    }
}
