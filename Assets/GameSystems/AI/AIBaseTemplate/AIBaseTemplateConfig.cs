using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// The AI's base layouts, drawn as character grids (docs/Design/Game_AI.md
    /// § 6g). The asset is AIBaseTemplate.asset, beside AIBaseTemplate.cs.
    ///
    /// One character is one 2 m build cell; the first row is NORTH (+z). The
    /// grid is anchored on the block of <see cref="fortressSymbol"/> cells,
    /// which stands where the territory's Fortress does. Every other symbol is
    /// looked up in <see cref="legend"/>; '.' (or any unlisted symbol) is open
    /// ground. A legend entry's connected block is tiled into as many
    /// footprints of its building as fit, so a 6x6 block of House cells is
    /// nine 2x2 House slots.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIBaseTemplate",
                     fileName = "AIBaseTemplate")]
    public sealed class AIBaseTemplateConfig : ScriptableObject, IComponentConfig
    {
        static AIBaseTemplateConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIBaseTemplateConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIBaseTemplateConfig>());

        /// <summary>Off: every placer runs its old ring search and the wall
        /// follows the territory border.</summary>
        public bool enabled;

        /// <summary>The home territory's layout ("main camp").</summary>
        public string[] mainCamp;

        /// <summary>The layout of every other territory the AI raises a
        /// Fortress in.</summary>
        public string[] outpost;

        /// <summary>The cell symbol of the Fortress block (the anchor).</summary>
        public string fortressSymbol;

        /// <summary>The cell symbol of a wall hub. Hubs are joined, in order
        /// of bearing round the Fortress, into one closed ring.</summary>
        public string wallHubSymbol;

        public AIBaseTemplateLegendEntry[] legend;

        /// <summary>How far, in build cells, a slot that cannot be used as
        /// drawn may move to the nearest legal spot.</summary>
        public int slotSearchRadiusCells;

        /// <summary>Gates cut into a template ring: one on the link nearest
        /// each of this many evenly spaced bearings, starting north.</summary>
        public int gatesPerRing;

        /// <summary>Mount an emplacement on every Nth non-gate link of a
        /// template ring (0 = none).</summary>
        public int emplacementEveryNthLink;

        /// <summary>Alternate Ballista and Trebuchet emplacements (off =
        /// Ballista only).</summary>
        public bool alternateTrebuchet;

        /// <summary>Turn, mirror and re-deal each base (per faction and
        /// territory, from the match seed) so no two towns look alike. Off:
        /// every base exactly as drawn.</summary>
        public bool variants;

        /// <summary>A main-camp ring scaled wider by the personality
        /// (homeRingScale, Game_AI.md § 3b) gets an extra hub on every link
        /// longer than this, in metres, so its hubs stay well inside the wall
        /// doctrine's link radius (hubSpacing + 3).</summary>
        public float ringMaxLinkMeters;
    }

    /// <summary>One symbol of a base layout and the buildings it is for.</summary>
    [System.Serializable]
    public sealed class AIBaseTemplateLegendEntry
    {
        public string symbol;

        /// <summary>Building ids this slot is for; the first one sizes it.</summary>
        public string[] buildingIds;

        /// <summary>A production slot: when every slot of a production
        /// building's own symbol is full, it may take a free slot of any other
        /// production symbol.</summary>
        public bool production;
    }
}
