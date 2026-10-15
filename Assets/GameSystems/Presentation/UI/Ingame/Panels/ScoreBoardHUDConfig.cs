using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Values for <see cref="ScoreBoardHUD"/>: where the top-right score list
    /// sits (offset from the canvas's top-right corner, canvas pixels), its
    /// width, row height, padding, text size and refresh rate. The asset is
    /// ScoreBoardHUD.asset, beside ScoreBoardHUD.cs. No field initialisers:
    /// the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/ScoreBoardHUD",
                     fileName = "ScoreBoardHUD")]
    public sealed class ScoreBoardHUDConfig : ScriptableObject, IComponentConfig
    {
        static ScoreBoardHUDConfig _i;
        public static ScoreBoardHUDConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ScoreBoardHUDConfig>());

        /// <summary>The panel's top-right corner, from the canvas's top-right
        /// corner (x negative = leftwards, y negative = downwards).</summary>
        public Vector2 offset;

        public float width;
        public float rowHeight;
        public float padding;
        public float fontSize;

        /// <summary>Real seconds between refreshes.</summary>
        public float refreshSeconds;
    }
}
