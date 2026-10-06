using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Core
{
    /// <summary>
    /// The Score's weights (docs/Design/Score.md). The asset is
    /// MatchScoreSystem.asset, beside MatchScoreSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Core/MatchScoreSystem",
                     fileName = "MatchScoreSystem")]
    public sealed class MatchScoreSystemConfig : ScriptableObject, IComponentConfig
    {
        static MatchScoreSystemConfig _i;
        public static MatchScoreSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<MatchScoreSystemConfig>());

        /// <summary>Sim seconds between samples.</summary>
        public float sampleIntervalSeconds;

        // ── Economy ──
        /// <summary>How each resource counts toward "resources earned".</summary>
        public float weightSupplies;
        public float weightIron;
        public float weightVeilstone;
        public float weightVeilsteel;
        /// <summary>Points per 100 weighted resources earned over the match.</summary>
        public float pointsPerHundredEarned;
        /// <summary>Points per 100 weighted resources of income per minute
        /// (the current rate, so a growing economy shows before it has paid).</summary>
        public float pointsPerHundredIncomePerMinute;

        // ── Strategy ──
        public float pointsPerTerritory;
        public float pointsPerFortress;
        public float pointsPerTech;
        public float pointsPerBuildingLevel;
        /// <summary>Age-up bonus: this at minute 0, less decayPerMinute for
        /// every minute it took, never below zero. Nothing before the age-up.</summary>
        public float ageUpBonusMax;
        public float ageUpBonusDecayPerMinute;

        // ── Military ──
        public float pointsPerKill;
        public float pointsPerRazedBuilding;
        /// <summary>The kill/death ratio scales the military points, clamped
        /// to this range: trading badly halves them, trading well doubles.</summary>
        public float kdFactorMin;
        public float kdFactorMax;
    }
}
