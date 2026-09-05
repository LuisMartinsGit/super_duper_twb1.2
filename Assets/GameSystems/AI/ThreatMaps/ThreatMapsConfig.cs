using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="ThreatMaps"/>. The asset is ThreatMaps.asset,
    /// beside ThreatMaps.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/ThreatMaps",
                     fileName = "ThreatMaps")]
    public sealed class ThreatMapsConfig : ScriptableObject, IComponentConfig
    {
        static ThreatMapsConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static ThreatMapsConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ThreatMapsConfig>());

        public float cellSize;
    }
}
