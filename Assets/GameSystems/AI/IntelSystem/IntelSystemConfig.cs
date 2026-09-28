using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="IntelSystem"/>. The asset is IntelSystem.asset,
    /// beside IntelSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/IntelSystem",
                     fileName = "IntelSystem")]
    public sealed class IntelSystemConfig : ScriptableObject, IComponentConfig
    {
        static IntelSystemConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static IntelSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<IntelSystemConfig>());

        public float tickInterval;

        // Damage signal stamped per damaged own-unit per tick.
        public int damageThreatStamp;

        /// <summary>Seconds a MOBILE sighting (army unit, worker) survives
        /// without being seen again before it is dropped. Structures never
        /// age out — only their death removes them. 0 disables aging.</summary>
        public float mobileSightingMaxAge;
    }
}
