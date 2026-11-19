using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// The actions panel's tunables. The asset is ActionsPanelPrefabBinder.asset,
    /// beside ActionsPanelPrefabBinder.cs (loaded through ComponentConfig.Require,
    /// listed in Resources/ComponentConfigCatalog.asset).
    ///
    /// No field initialisers on purpose: the values live in the asset only.
    ///
    /// Today it holds the worker's second build panel (the religion panel,
    /// ActionsPanelPrefabBinder.ReligionPanel.cs, 2026-11-18): after the age-up
    /// it is one column per culture's sect cluster, each column framed in its
    /// culture's colours (developer: Alanthor blue / silver, Feraldis black /
    /// red, Runai dark green / golden). "Fill" is the column's ground, "Edge"
    /// its border line.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Actions Panel",
                     fileName = "ActionsPanelPrefabBinder")]
    public sealed class ActionsPanelPrefabBinderConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Canvas pixels between the build palette's frame and the
        /// religion panel's frame.</summary>
        public float religionPanelGap;

        /// <summary>Width of a sect column's border line, canvas pixels.</summary>
        public float sectColumnEdgeWidth;

        public Color alanthorColumnFill;
        public Color alanthorColumnEdge;
        public Color feraldisColumnFill;
        public Color feraldisColumnEdge;
        public Color runaiColumnFill;
        public Color runaiColumnEdge;
    }
}
