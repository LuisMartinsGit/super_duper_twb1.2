using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="ScoutDirectorSystem"/>. The asset is ScoutDirectorSystem.asset,
    /// beside ScoutDirectorSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/ScoutDirectorSystem",
                     fileName = "ScoutDirectorSystem")]
    public sealed class ScoutDirectorSystemConfig : ScriptableObject, IComponentConfig
    {
        static ScoutDirectorSystemConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static ScoutDirectorSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ScoutDirectorSystemConfig>());

        public float tickInterval;

        public float zoneSize;

        public float neverVisitedBonus;

        public float enemyBaseBonus;

        public float distancePenaltyPerMeter;

        public float threatPenaltyFactor;

        // don't re-assign a zone someone is heading to
        public float assignmentHoldSeconds;

        public float planTimeoutSeconds;

    }
}
