using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIEngagement"/>. The asset is AIEngagement.asset,
    /// beside AIEngagement.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIEngagement",
                     fileName = "AIEngagement")]
    public sealed class AIEngagementConfig : ScriptableObject, IComponentConfig
    {
        static AIEngagementConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIEngagementConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIEngagementConfig>());

        /// <summary>Band the army "feels" — matches the retreat check's radius
        /// so a wave is judged by the same yardstick before and during a
        /// fight.</summary>
        public float defaultAssessRadius;

        /// <summary>
        /// Commit when enemy power is no more than this multiple of mine.
        /// Slightly above parity because the attacker picks the moment and
        /// concentrates, while defenders trickle in — but nowhere near the
        /// 2-3x disadvantage the old count-only check happily accepted.
        /// </summary>
        public float defaultCommitRatio;
    }
}
