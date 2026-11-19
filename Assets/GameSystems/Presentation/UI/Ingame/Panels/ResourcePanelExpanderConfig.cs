using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Values for <see cref="ResourcePanelExpander"/>: where the per-minute rate sits under each amount, and the rate's
    /// colours. The asset is ResourcePanelExpander.asset, beside
    /// ResourcePanelExpander.cs. No field initialisers: the values live in the
    /// asset. Coordinates are canvas pixels in the resource row's local space.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/ResourcePanelExpander",
                     fileName = "ResourcePanelExpander")]
    public sealed class ResourcePanelExpanderConfig : ScriptableObject, IComponentConfig
    {
        static ResourcePanelExpanderConfig _i;
        public static ResourcePanelExpanderConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ResourcePanelExpanderConfig>());

        /// <summary>While the rate shows, the amount moves up and the rate sits
        /// under it: each line's centre is this far from the row's centre.</summary>
        public float stackOffset;
        /// <summary>Height of each of the two stacked lines.</summary>
        public float lineHeight;

        public Color positiveColor;
        public Color negativeColor;
    }
}
