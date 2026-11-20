using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// What a hero level is worth in stats (docs/Design/Heroes.md § 2). The
    /// asset is HeroLevelSystem.asset, beside HeroLevelSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/HeroLevelSystem",
                     fileName = "HeroLevelSystem")]
    public sealed class HeroLevelSystemConfig : ScriptableObject, IComponentConfig
    {
        static HeroLevelSystemConfig _i;
        public static HeroLevelSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<HeroLevelSystemConfig>());

        /// <summary>Share of the hero's level-1 health each level above the
        /// first adds (0.1 = +10 % a level, linear).</summary>
        public float healthPerLevel;

        /// <summary>Share of the hero's level-1 weapon damage each level above
        /// the first adds.</summary>
        public float damagePerLevel;
    }
}
