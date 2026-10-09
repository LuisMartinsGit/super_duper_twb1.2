using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Values for <see cref="ReplayControlsPanel"/>: the playback speeds offered
    /// and how far "skip ahead" jumps. The asset is ReplayControlsPanel.asset,
    /// beside ReplayControlsPanel.cs. No field initialisers: the values live in
    /// the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/ReplayControlsPanel",
                     fileName = "ReplayControlsPanel")]
    public sealed class ReplayControlsPanelConfig : ScriptableObject, IComponentConfig
    {
        static ReplayControlsPanelConfig _i;
        public static ReplayControlsPanelConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ReplayControlsPanelConfig>());

        /// <summary>Speed buttons, as multiples of normal speed.</summary>
        public float[] speeds;

        /// <summary>Match seconds the "skip ahead" button jumps.</summary>
        public float skipSeconds;
    }
}
