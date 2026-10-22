using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// What cursed ground does to a building standing on it
    /// (Territory_Claims.md §6.9.9). The asset is CursedGroundDecaySystem.asset,
    /// beside CursedGroundDecaySystem.cs. No field initialisers: the values
    /// live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Border/CursedGroundDecaySystem",
                     fileName = "CursedGroundDecaySystem")]
    public sealed class CursedGroundDecaySystemConfig : ScriptableObject, IComponentConfig
    {
        static CursedGroundDecaySystemConfig _i;
        public static CursedGroundDecaySystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<CursedGroundDecaySystemConfig>());

        /// <summary>Seconds between decay ticks.</summary>
        public float tickSeconds;
        /// <summary>Max HP lost a second, in percent (0 = no decay).</summary>
        public float dotPercentPerSecond;
        /// <summary>Every hit on a decaying building is multiplied by this.</summary>
        public float damageTakenMultiplier;
        /// <summary>A decaying building's training, research and extraction run
        /// at this fraction of their speed.</summary>
        public float workSpeedMultiplier;
    }
}
