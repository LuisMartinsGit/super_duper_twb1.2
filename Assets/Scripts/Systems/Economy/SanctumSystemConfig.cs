using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Tuning numbers for <see cref="SanctumSystem"/> (docs/Design/
    /// Territory_Claims.md § 11.4): what a held Sanctum pays and how long
    /// dominion takes. The asset is SanctumSystem.asset, beside
    /// SanctumSystem.cs. No field initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Economy/SanctumSystem",
                     fileName = "SanctumSystem")]
    public sealed class SanctumSystemConfig : ScriptableObject, IComponentConfig
    {
        static SanctumSystemConfig _i;
        public static SanctumSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<SanctumSystemConfig>());

        /// <summary>Seconds per Religion Point a held Sanctum pays.</summary>
        public float secondsPerPoint;

        /// <summary>Veilsteel a held Sanctum pays a minute (per Sanctum).</summary>
        public float veilsteelPerMinute;

        /// <summary>Share of every sect power's cooldown cut per Sanctum held,
        /// applied once per Sanctum, multiplicatively.</summary>
        public float cooldownCutPerSanctum;

        /// <summary>The most the Sanctums together can cut a cooldown.</summary>
        public float maxCooldownCut;

        /// <summary>Seconds a faction or team must hold every Sanctum to win.</summary>
        public float dominionSeconds;
    }
}
