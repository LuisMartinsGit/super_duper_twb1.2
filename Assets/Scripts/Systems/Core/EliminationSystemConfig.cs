using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Core
{
    /// <summary>
    /// Elimination timing (docs/Design/Territory_Claims.md § 7). The asset is
    /// EliminationSystem.asset, beside EliminationSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Core/EliminationSystem",
                     fileName = "EliminationSystem")]
    public sealed class EliminationSystemConfig : ScriptableObject, IComponentConfig
    {
        static EliminationSystemConfig _i;
        public static EliminationSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<EliminationSystemConfig>());

        /// <summary>Sim seconds between elimination checks.</summary>
        public float checkIntervalSeconds;

        /// <summary>Sim seconds from match start before anyone may be
        /// eliminated (the start assets are still spawning).</summary>
        public float startGraceSeconds;

        /// <summary>The no-territory rule: a faction that has held ground at
        /// least once and then holds NO territory for this many continuous
        /// sim seconds is eliminated. 0 or below = the rule is off.</summary>
        public float noTerritoryGraceSeconds;
    }
}
