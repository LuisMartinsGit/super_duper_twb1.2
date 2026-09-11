// AISettingsSO.cs
// Inspector-tunable knobs for the full-scale player-faction AI
// (docs/AI_Assessment_and_Plan.md M2-M6). One asset under Resources/AISettings;
// AISettings.Get() falls back to a defaults-seeded instance when missing,
// mirroring the BorderSettings pattern.

using System;
using UnityEngine;
using TheWaningBorder.AI;

namespace TheWaningBorder.Data.AI
{
    [CreateAssetMenu(fileName = "AISettings", menuName = "Waning Border/AI Settings", order = 10)]
    public class AISettingsSO : ScriptableObject
    {
        public float weightMiner = 120f;
        public float weightEcoBuilding = 100f;
        public float weightMilitaryBuilding = 70f;
        public float weightHall = 60f;
        public float weightBorderNode = 80f;
        public float weightMilitaryUnit = 40f;

        public float riskPerDefenseStrength = 0.6f;
        public float defenseProbeRadius = 25f;
        public float travelCostPerMeter = 0.4f;
        public float intelAgePenaltyPerSecond = 0.5f;

        public float reconMaxIntelAge = 45f;
        public float scoutFleeHealthFraction = 0.5f;

        public int defendThreatThreshold = 120;
        public float defendRadius = 45f;

        public float retreatStrengthRatio = 1.6f;
        public float retreatCooldownSeconds = 30f;

        /// <summary>
        /// LAYER 2 — what one personality PRIORITISES. Every field here is a
        /// how-much, never a which: no unit id appears in this block, and none
        /// ever should. Unit choice is layer 3 (AIComposition).
        ///
        /// The five economy/tech fields at the bottom used to live on the
        /// DIFFICULTY profile, which meant Hard bought more economy than
        /// Normal regardless of whether either was playing an economic game —
        /// difficulty setting priorities instead of skill.
        /// </summary>
        [Serializable]
        public class PersonalityBlock
        {
            public AIPersonality personality;
            public int attackThreshold = 3;
            public int militaryFloor = 8;
            /// <summary>Workers to keep. They only BUILD now (Regions.md §4 removed
            /// gathering), so this is a build crew, not an economy.</summary>
            public int minerFloor = 3;
            public float riskMultiplier = 1f;

            // ── Economy / tech priorities (moved off the difficulty profile) ──
            /// <summary>Workers to grow toward before age-up…</summary>
            public int workerTargetAge0 = 3;
            /// <summary>…and after it.</summary>
            public int workerTargetAge1 = 5;
            /// <summary>Gatherer's Huts the maintenance loop grows toward.</summary>
            public int gathererHutTarget = 14;
            /// <summary>Military production buildings to build toward.</summary>
            public int productionBuildingTarget = 24;
            /// <summary>Game time (s) at which this AI stops expanding and
            /// banks for the age-up. Lower = techs sooner.</summary>
            public float ageUpPushSeconds = 90f;

            // ── Military stances (also moved off difficulty) ──
            /// <summary>Peel fast raid parties at the enemy economy.</summary>
            public bool raidingEnabled = true;
            /// <summary>Form up at a staging point before committing.</summary>
            public bool forwardStaging = false;
        }

        public PersonalityBlock[] personalities = DefaultPersonalities();

        /// <summary>
        /// Anchored on what the NORMAL difficulty profile used to carry
        /// (workers 3/5, huts 14, production 24, age-up push 90 s), then
        /// spread by personality. Those numbers were tuned in play; moving
        /// them between layers must not silently retune them, so Balanced
        /// reproduces the old Normal almost exactly and the others vary
        /// around it.
        /// </summary>
        public static PersonalityBlock[] DefaultPersonalities() => new[]
        {
            new PersonalityBlock { personality = AIPersonality.Balanced,   attackThreshold = 3, militaryFloor = 8,  minerFloor = 3, riskMultiplier = 1.0f,
                                   workerTargetAge0 = 3, workerTargetAge1 = 5, gathererHutTarget = 14, productionBuildingTarget = 24, ageUpPushSeconds = 90f,  raidingEnabled = true,  forwardStaging = false },
            new PersonalityBlock { personality = AIPersonality.Aggressive, attackThreshold = 2, militaryFloor = 10, minerFloor = 2, riskMultiplier = 0.6f,
                                   workerTargetAge0 = 3, workerTargetAge1 = 5, gathererHutTarget = 11, productionBuildingTarget = 28, ageUpPushSeconds = 110f, raidingEnabled = true,  forwardStaging = true  },
            new PersonalityBlock { personality = AIPersonality.Defensive,  attackThreshold = 5, militaryFloor = 12, minerFloor = 4, riskMultiplier = 1.5f,
                                   workerTargetAge0 = 4, workerTargetAge1 = 6, gathererHutTarget = 16, productionBuildingTarget = 22, ageUpPushSeconds = 90f,  raidingEnabled = false, forwardStaging = false },
            new PersonalityBlock { personality = AIPersonality.Economic,   attackThreshold = 4, militaryFloor = 6,  minerFloor = 5, riskMultiplier = 1.2f,
                                   workerTargetAge0 = 5, workerTargetAge1 = 8, gathererHutTarget = 20, productionBuildingTarget = 18, ageUpPushSeconds = 75f,  raidingEnabled = false, forwardStaging = false },
            new PersonalityBlock { personality = AIPersonality.Rush,       attackThreshold = 2, militaryFloor = 10, minerFloor = 2, riskMultiplier = 0.5f,
                                   workerTargetAge0 = 2, workerTargetAge1 = 4, gathererHutTarget = 9,  productionBuildingTarget = 30, ageUpPushSeconds = 120f, raidingEnabled = true,  forwardStaging = true  },
            // Absorbed from the retired AIStrategy enum: the tech and turtle
            // openings had no personality to belong to, so Yellow was filed
            // as "Balanced" and carried its identity in the build order alone.
            new PersonalityBlock { personality = AIPersonality.TechBoom,   attackThreshold = 4, militaryFloor = 7,  minerFloor = 4, riskMultiplier = 1.2f,
                                   workerTargetAge0 = 4, workerTargetAge1 = 7, gathererHutTarget = 17, productionBuildingTarget = 20, ageUpPushSeconds = 60f,  raidingEnabled = false, forwardStaging = false },
            new PersonalityBlock { personality = AIPersonality.Turtle,     attackThreshold = 6, militaryFloor = 14, minerFloor = 5, riskMultiplier = 1.8f,
                                   workerTargetAge0 = 4, workerTargetAge1 = 7, gathererHutTarget = 18, productionBuildingTarget = 20, ageUpPushSeconds = 105f, raidingEnabled = false, forwardStaging = false },
        };

        public PersonalityBlock For(AIPersonality p)
        {
            if (personalities != null)
                for (int i = 0; i < personalities.Length; i++)
                    if (personalities[i] != null && personalities[i].personality == p)
                        return personalities[i];
            // Defensive fallback: an asset saved before a new personality was
            // added (or with a cleared list) still gets sane behavior.
            var defs = DefaultPersonalities();
            for (int i = 0; i < defs.Length; i++)
                if (defs[i].personality == p) return defs[i];
            return defs[0];
        }

        public float CategoryWeight(IntelCategory c) => c switch
        {
            IntelCategory.Miner            => weightMiner,
            IntelCategory.EcoBuilding      => weightEcoBuilding,
            IntelCategory.MilitaryBuilding => weightMilitaryBuilding,
            IntelCategory.Hall             => weightHall,
            IntelCategory.BorderNode        => weightBorderNode,
            _                              => weightMilitaryUnit,
        };
    }
}
