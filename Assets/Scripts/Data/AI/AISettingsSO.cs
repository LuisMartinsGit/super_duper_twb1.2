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
