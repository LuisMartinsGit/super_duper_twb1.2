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

        // ── Docked in the resource panel (2026-10-16) ──
        // The button sits in the resource panel's Housing row, beside the
        // housing count, instead of floating above the panel. Coordinates
        // are the Housing row's local space (its centre is 0,0).

        /// <summary>Centre of the docked button in the Housing row.</summary>
        public Vector2 dockedCenter;
        public Vector2 dockedSize;
        public float dockedFontSize;
        /// <summary>Where the housing icon and count move to, so the two sit
        /// side by side (x only; their authored y is kept).</summary>
        public float housingShiftX;
        /// <summary>Width the housing count label is narrowed to.</summary>
        public float housingLabelWidth;
    }
}
