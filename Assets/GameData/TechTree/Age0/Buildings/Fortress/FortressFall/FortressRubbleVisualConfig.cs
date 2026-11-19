using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// The look of a fallen Fortress's rubble (decision 69): which models are
    /// scattered over its footprint, how many and how far. The asset is
    /// FortressRubbleVisual.asset, beside FortressRubbleVisual.cs.
    /// Presentation only.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Rendering/FortressRubbleVisual",
                     fileName = "FortressRubbleVisual")]
    public sealed class FortressRubbleVisualConfig : ScriptableObject, IComponentConfig
    {
        static FortressRubbleVisualConfig _i;
        public static FortressRubbleVisualConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<FortressRubbleVisualConfig>());

        /// <summary>The rubble models (Synty marble rubble).</summary>
        public GameObject[] pieces;

        /// <summary>How many pieces one heap is made of.</summary>
        public int pieceCount;

        /// <summary>Metres from the centre the pieces scatter (about half the
        /// Fortress footprint).</summary>
        public float spread;

        /// <summary>Scale applied to every piece.</summary>
        public float pieceScale;
    }
}
