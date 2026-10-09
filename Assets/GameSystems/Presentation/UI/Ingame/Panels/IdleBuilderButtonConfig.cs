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
    }
}
