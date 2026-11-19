using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Values for <see cref="IdleBuilderButton"/>: where it sits on the HUD
    /// canvas (bottom-left anchored, canvas pixels), its size, its text size
    /// and how often the idle count refreshes. The asset is
    /// IdleBuilderButton.asset, beside IdleBuilderButton.cs. No field
    /// initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/IdleBuilderButton",
                     fileName = "IdleBuilderButton")]
    public sealed class IdleBuilderButtonConfig : ScriptableObject, IComponentConfig
    {
        static IdleBuilderButtonConfig _i;
        public static IdleBuilderButtonConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<IdleBuilderButtonConfig>());

        /// <summary>Bottom-left corner of the button, from the canvas's
        /// bottom-left corner.</summary>
        public Vector2 anchoredPosition;

        public Vector2 size;

        public float fontSize;

        /// <summary>Real seconds between idle-count refreshes.</summary>
        public float refreshSeconds;

        // ── Docked in the resource panel (2026-10-16; 2026-11-18) ──
        // The button sits in the resource panel's Housing row instead of
        // floating above the panel. Coordinates are the Housing row's local
        // space (its centre is 0,0). Since 2026-11-18 the row reads like the
        // resource rows below it: the housing icon in their icon column and
        // the count in their amount column (left-aligned) on the upper line,
        // the button on the lower line spanning both columns.

        /// <summary>Centre of the docked button in the Housing row.</summary>
        public Vector2 dockedCenter;
        public Vector2 dockedSize;
        public float dockedFontSize;
        /// <summary>The housing icon's centre and size in the Housing row.</summary>
        public Vector2 housingIconCenter;
        public Vector2 housingIconSize;
        /// <summary>The housing count's centre and size; it is left-aligned,
        /// like the resource amounts.</summary>
        public Vector2 housingLabelCenter;
        public Vector2 housingLabelSize;
    }
}
