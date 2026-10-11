using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Values for <see cref="TradeMenu"/>: where its HUD button sits
    /// (bottom-left anchored, canvas pixels), the menu's size and rows, its
    /// text sizes and refresh rate. The asset is TradeMenu.asset, beside
    /// TradeMenu.cs. No field initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/TradeMenu",
                     fileName = "TradeMenu")]
    public sealed class TradeMenuConfig : ScriptableObject, IComponentConfig
    {
        static TradeMenuConfig _i;
        public static TradeMenuConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<TradeMenuConfig>());

        /// <summary>Bottom-left corner of the HUD's Trade button, from the
        /// canvas's bottom-left corner.</summary>
        public Vector2 buttonPosition;

        public Vector2 buttonSize;

        /// <summary>The menu's bottom-left corner, from the button's
        /// top-left corner.</summary>
        public Vector2 menuOffset;

        /// <summary>Menu width; its height follows the rows shown.</summary>
        public float menuWidth;

        /// <summary>Inner margin of the menu frame.</summary>
        public float padding;

        /// <summary>Height of one row (the title, the net line, the trade
        /// headers and each outpost).</summary>
        public float rowHeight;

        /// <summary>Gap between rows and between cells.</summary>
        public float gap;

        /// <summary>Width of the outpost-name column; the four trade
        /// columns share the rest.</summary>
        public float nameColumnWidth;

        /// <summary>Outposts listed before the rest are summarised in one
        /// "+N more" line (they still count in every total).</summary>
        public int maxRows;

        public float titleFontSize;
        public float fontSize;
        public float cellFontSize;

        /// <summary>Real seconds between refreshes while the menu is open
        /// (the button's count refreshes on the same clock).</summary>
        public float refreshSeconds;
    }
}
