using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIWallPlanner"/>. The asset is AIWallPlanner.asset,
    /// beside AIWallPlanner.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIWallPlanner",
                     fileName = "AIWallPlanner")]
    public sealed class AIWallPlannerConfig : ScriptableObject, IComponentConfig
    {
        static AIWallPlannerConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIWallPlannerConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIWallPlannerConfig>());

        // clear of the Hall footprint
        public float scanStart;

        // "near the base" horizon
        public float scanEnd;

        public float scanStep;

        /// <summary>Open-arc budget: above this fraction of open bearings
        /// the base does not count as terrain-sheltered.</summary>
        public float maxOpenFraction;

        /// <summary>Widest corridor cross-section a wall line will seal —
        /// at 30 m hub spacing a 60 m line is only 2-3 curtains, so wide
        /// mountain passes are still worth sealing.</summary>
        public float maxSealableWidth;

        /// <summary>Per-flank probe cap for cross-section width.</summary>
        public float chokeProbeCap;

        // ── Plan geometry ─────────────────────────────────────────────────
        /// <summary>Maximum hub spacing along a planned line — long 30 m
        /// curtains between bastions. AlanthorWall.CreateSegment tiles 3 m
        /// modules across any span, so segment length is unconstrained; the
        /// 16 m WallAutoSegmentSystem rule is a disabled AUTO-link rule, not
        /// a segment limit, and the doctrine links plan neighbours
        /// explicitly (see WallLinkRadius in the endgame system).</summary>
        public float hubSpacing;

        /// <summary>Perimeter padding beyond the outermost enclosed
        /// building.</summary>
        public float perimeterPad;

        public float perimeterHalfExtentMin;

        public float perimeterHalfExtentMax;

        /// <summary>Buildings farther than this from the Hall are outlying
        /// expansion, not base — the perimeter does not chase them.</summary>
        public float perimeterGatherRadius;
    }
}
