using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// The armed-mode banner. The asset is InputModes.asset, beside
    /// InputModes.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// The two labels are player-facing strings, which is the clearest case
    /// there is for data over source.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Input Modes",
                     fileName = "InputModes")]
    public sealed class InputModesConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Banner shown while attack-move is armed.</summary>
        public string attackMoveBanner;

        /// <summary>Banner shown while patrol is armed.</summary>
        public string patrolBanner;

        /// <summary>Banner width in pixels.</summary>
        public float bannerWidth;

        /// <summary>Banner height in pixels.</summary>
        public float bannerHeight;

        /// <summary>Pixels from the top of the screen to the banner.</summary>
        public float bannerTopMargin;

        /// <summary>Banner label size in points.</summary>
        public int bannerFontSize;

        /// <summary>Banner plate colour, alpha included.</summary>
        public Color bannerBackground;

        /// <summary>Banner label colour.</summary>
        public Color bannerTextColor;
    }
}
