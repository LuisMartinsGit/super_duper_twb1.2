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
        [Header("Target scoring (M2) — base value per target category")]
        public float weightMiner = 120f;
        public float weightEcoBuilding = 100f;
        public float weightMilitaryBuilding = 70f;
        public float weightHall = 60f;
        public float weightBorderNode = 80f;
        public float weightMilitaryUnit = 40f;

        [Tooltip("Score subtracted per point of defender strength near the target.")]
        public float riskPerDefenseStrength = 0.6f;
        [Tooltip("Radius around a candidate target in which defenders count as risk.")]
        public float defenseProbeRadius = 25f;
        [Tooltip("Score subtracted per meter of march distance.")]
        public float travelCostPerMeter = 0.4f;
        [Tooltip("Score subtracted per second of sighting age.")]
        public float intelAgePenaltyPerSecond = 0.5f;

        [Header("Scout-then-strike (M3)")]
        [Tooltip("Assault targets (halls / military buildings) with intel older than this trigger a recon pass instead of an attack.")]
        public float reconMaxIntelAge = 45f;
        [Tooltip("A scout below this health fraction flees to the Hall.")]
        public float scoutFleeHealthFraction = 0.5f;

        [Header("Posture (M4)")]
        [Tooltip("ThreatMap level near the Hall that flips the AI into Defend posture.")]
        public int defendThreatThreshold = 120;
        public float defendRadius = 45f;

        [Header("Retreat (M6)")]
        [Tooltip("Retreat the fielded army when local enemy strength exceeds own strength times this ratio.")]
        public float retreatStrengthRatio = 1.6f;
        public float retreatCooldownSeconds = 30f;

        [Serializable]
        public class PersonalityBlock
        {
            public AIPersonality personality;
            [Tooltip("Min idle units before a maintenance attack launches.")]
            public int attackThreshold = 3;
            public int militaryFloor = 8;
            /// <summary>Workers to keep. They only BUILD now (Regions.md §4 removed
        /// gathering), so this is a build crew, not an economy.</summary>
        public int minerFloor = 3;
            [Tooltip("Multiplier on the risk term of the target scorer. >1 = cautious.")]
            public float riskMultiplier = 1f;
        }

        public PersonalityBlock[] personalities = DefaultPersonalities();

        public static PersonalityBlock[] DefaultPersonalities() => new[]
        {
            new PersonalityBlock { personality = AIPersonality.Balanced,   attackThreshold = 3, militaryFloor = 8,  minerFloor = 3,  riskMultiplier = 1.0f },
            new PersonalityBlock { personality = AIPersonality.Aggressive, attackThreshold = 2, militaryFloor = 10, minerFloor = 2,  riskMultiplier = 0.6f },
            new PersonalityBlock { personality = AIPersonality.Defensive,  attackThreshold = 5, militaryFloor = 12, minerFloor = 4,  riskMultiplier = 1.5f },
            new PersonalityBlock { personality = AIPersonality.Economic,   attackThreshold = 4, militaryFloor = 6,  minerFloor = 5,  riskMultiplier = 1.2f },
            new PersonalityBlock { personality = AIPersonality.Rush,       attackThreshold = 2, militaryFloor = 10, minerFloor = 2,  riskMultiplier = 0.5f },
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
