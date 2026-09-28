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
        /// <summary>A path that bends tighter than this radius anywhere is
        /// INVALID (red, refused on release), metres. A validity rule, not a
        /// steering law: the path always follows the drag. Keep it large
        /// enough that a closed loop (circumference >= 2 pi x this, cut into
        /// >= 3 runs) keeps its hubs beyond
        /// WallAutoSegmentSystem.MaxAutoSegmentDistance (16 m).</summary>
        public float minBendRadius;
        /// <summary>The bend test measures the circle through three path
        /// samples this far apart, metres -- wide enough that hand jitter
        /// between samples does not read as a tight bend.</summary>
        public float curvatureWindow;
        /// <summary>Half-width of the moving average that smooths the drag
        /// track into the path, metres. 0 = no smoothing.</summary>
        public float smoothingRadius;
        /// <summary>Two path points further than 4 x this apart along the
        /// path but closer than this on the ground mean the stroke runs back
        /// over itself: invalid. Metres.</summary>
        public float selfClearance;
        /// <summary>When the end snaps onto a hub or cell, at least the last
        /// this-many metres of the path are bent onto it (instead of a
        /// straight chord to it); longer when the snap is far enough that
        /// this would bend tighter than minBendRadius.</summary>
        public float snapBlendLength;
        /// <summary>The snap blend never bends more than this many metres of
        /// the tail. When the blend the snap needs is longer than this (or
        /// than snapBlendMaxFraction of the stroke), the whole stroke is
        /// instead rotated and scaled about its start onto the snap target,
        /// which keeps its drawn shape rather than bending a long tail.</summary>
        public float snapBlendMaxLength;
        /// <summary>The snap blend never bends more than this fraction of the
        /// stroke's length (0..1). See snapBlendMaxLength.</summary>
        public float snapBlendMaxFraction;
        /// <summary>When a stroke bends tighter than minBendRadius, the
        /// offending windows are relaxed (local smoothing) up to this many
        /// passes before the stroke is refused -- enough to iron out hand
        /// jitter, not enough to round a deliberate corner. 0 = none.</summary>
        public int bendRelaxPasses;
        /// <summary>Once a stroke is 3 x this long, backtracking ignores its
        /// first this-many metres, so bringing the end back to the start to
        /// close a loop does not erase the stroke. Keep it >= the hub snap
        /// radius (2 x AlanthorWall.HubRadius = 4.2 m). Metres.</summary>
        public float closeGuardRadius;
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
        /// <summary>The ring drawn on the stroke's start while releasing now
        /// would close the wall into a loop.</summary>
        public Color loopCloseColor;
    }
}
