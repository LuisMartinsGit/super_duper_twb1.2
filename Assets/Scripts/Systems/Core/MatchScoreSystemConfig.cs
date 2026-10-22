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

        // ── The resource ladder: points per unit of each resource. Every
        // price (unit, building, level-up) and the stockpile are converted
        // with these. ──
        public float weightSupplies;
        public float weightIron;
        public float weightVeilstone;
        public float weightVeilsteel;
    }
}
