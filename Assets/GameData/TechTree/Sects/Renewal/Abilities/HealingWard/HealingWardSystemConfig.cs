using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Buildings
{
    /// <summary>
    /// The Healing Ward's numbers (docs/Design/Sects.md, Renewal). The asset is
    /// HealingWardSystem.asset, beside HealingWardSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else. The heal
    /// radius and the shared cooldown are the Healing Ward CARD's
    /// (HealingWard.asset); the ward's HP, armour, sight and footprint are its
    /// building SO's (RenewalHealingWard.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Sects/HealingWardSystem",
                     fileName = "HealingWardSystem")]
    public sealed class HealingWardSystemConfig : ScriptableObject, IComponentConfig
    {
        static HealingWardSystemConfig _i;
        public static HealingWardSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<HealingWardSystemConfig>());

        /// <summary>Seconds between heal pulses.</summary>
        public float pulseSeconds;
        /// <summary>HP each pulse gives every wounded allied unit in the radius.</summary>
        public int healPerPulse;

        /// <summary>Most wards a team (a faction and its allies) may have
        /// standing at once; a cast past the cap is refused (0 = no cap).</summary>
        public int capPerTeam;
        /// <summary>Least distance (m) between two of the team's wards;
        /// a cast closer than this to one is refused.</summary>
        public float minSpacingMetres;
    }
}
