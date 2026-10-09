using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// The post-game Muster Rolls screen's tunables
    /// (docs/Design/Muster_Rolls_PostGame.md). The asset is
    /// MusterRollsPanel.asset, beside MusterRollsPanel.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/MusterRollsPanel",
                     fileName = "MusterRollsPanel")]
    public sealed class MusterRollsPanelConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Map replay speeds offered (sim seconds per real second).</summary>
        public float[] playbackSpeeds;
        /// <summary>Index into playbackSpeeds selected when the map opens.</summary>
        public int defaultSpeedIndex;
        /// <summary>Seconds a death ring stays on the map.</summary>
        public float deathFadeSeconds;
        /// <summary>Opacity of the map's lobby picture under everything.</summary>
        public float thumbnailAlpha;
        /// <summary>Opacity of an owned territory's fill.</summary>
        public float territoryFillAlpha;
        /// <summary>Opacity of territory border cells.</summary>
        public float territoryBorderAlpha;
        /// <summary>A unit symbol's size on screen at zoom 1, canvas units.</summary>
        public float unitSymbolSize;
        /// <summary>Largest map zoom factor.</summary>
        public float maxZoom;
        /// <summary>Points a chart line is decimated to.</summary>
        public int chartMaxPoints;
        /// <summary>Thickness of a chart line, canvas units.</summary>
        public float chartLineWidth;
        /// <summary>The curse's colour on the map (territories, units, buildings).</summary>
        public Color curseColor;
    }
}
