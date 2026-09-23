using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.World
{
    /// <summary>
    /// Tunables for <see cref="WallDrawTool"/> (docs/Design/Age_1_Alanthor.md
    /// § Drawing walls). The asset is WallDrawTool.asset, beside
    /// WallDrawTool.cs. No field initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Wall Draw Tool",
                     fileName = "WallDrawTool")]
    public sealed class WallDrawToolConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Distance between path samples, metres.</summary>
        public float sampleSpacing;
        /// <summary>The path cannot bend tighter than this radius, metres.
        /// Keep it >= hubSpacing so non-neighbour hubs stay beyond
        /// WallAutoSegmentSystem.MaxAutoSegmentDistance (16 m).</summary>
        public float minBendRadius;
        /// <summary>The longest a single wall run may be, in 3 m modules. A
        /// stroke longer than this is split by INTERMEDIATE hubs, placed at
        /// equal arc intervals so the wall stays symmetrical — one extra hub
        /// lands exactly in the middle. 0 disables the cap.
        /// docs/Design/Age_1_Alanthor.md § Drawing walls.</summary>
        public int maxModulesPerSegment;
        /// <summary>Spacing of the curve samples sent in the order, metres.</summary>
        public float commandSampleSpacing;
        /// <summary>Retracing within this distance of an earlier point erases
        /// the path back to it, metres.</summary>
        public float backtrackRadius;
        /// <summary>Hard cap on path samples.</summary>
        public int maxPoints;
        /// <summary>Hard cap on hubs per drawn wall.</summary>
        public int maxHubs;

        // -- Preview --
        public float pathWidth;
        public float hubRingRadius;
        public float groundOffset;
        public Color invalidColor;
    }
}
