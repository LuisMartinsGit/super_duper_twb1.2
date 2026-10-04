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

        /// <summary>Half the widest base footprint the AI places (Barracks,
        /// 10 m), added to the base placer's ring reach when sizing the
        /// perimeter — the wall must clear a building's EDGE, not its centre.</summary>
        public float perimeterFootprintAllowance;

        /// <summary>Buildings farther than this from the Hall are outlying
        /// expansion, not base — the perimeter does not chase them.</summary>
        public float perimeterGatherRadius;

        // ── Border wall (2026-09-30) ─────────────────────────────────────
        /// <summary>The wall's centre line stands this many METRES inside its
        /// owner's territory border — measured to foreign ground, so an inner
        /// lake or mountain does not pull it in (2026-10-02: 3-5 m, was
        /// 8-12 m and drifted much further). docs/Design/Age_1_Alanthor.md
        /// § The AI's wall.</summary>
        public float borderInset;

        /// <summary>How far the straight run between two planned hubs may
        /// stray from the traced border line before another hub is put in,
        /// metres. Smaller follows a ragged border more closely, with more hubs.</summary>
        public float borderFollowTolerance;

        /// <summary>Every AI building keeps at least this clear between its
        /// EDGE and its territory border, metres — the band the border wall
        /// will run along, kept free from the first minute.</summary>
        public float buildingBorderClearance;

        /// <summary>Half the side of the square around the Fortress the border
        /// is traced in, metres.</summary>
        public float borderScanMax;

        /// <summary>A wall point closer than this to the Fortress is dropped:
        /// a border that near cannot be walled without walling the capital.</summary>
        public float borderMinRadius;

        // ── The reserved ring and its gates (2026-10-04) ─────────────────
        /// <summary>Build cells kept clear on EACH side of the planned home
        /// ring, beyond the wall's own half-depth (hub radius at a hub): room
        /// for the wall and a walkway. Every AI placer refuses a footprint on
        /// this corridor (AIWallCorridor).</summary>
        public int corridorClearanceCells;

        /// <summary>A footprint of at most this many 2 m build cells (a Hut
        /// or a Watch Tower is 2 x 2 = 4) is refused only on the wall's OWN
        /// cells, not on the walkway band either side: it fits beside the
        /// wall and the curtain router winds past it (2026-10-04). 0 = no
        /// exemption.</summary>
        public int corridorSmallFootprintCells;

        /// <summary>Seconds between "placement rejected — on wall corridor"
        /// log lines per faction and building.</summary>
        public float corridorLogInterval;

        /// <summary>Grid cell of the curtain router, metres.</summary>
        public float wallRerouteCellSize;

        /// <summary>How far round the two hubs the curtain router may look
        /// for a way past a blocker, metres.</summary>
        public float wallRerouteMargin;

        /// <summary>A routed curtain may be at most this many times the
        /// straight distance between its hubs (and never less than the
        /// bulge detour's own cap).</summary>
        public float wallRerouteMaxLengthFactor;

        /// <summary>Gates every closed home ring must have — the army is
        /// never sealed in.</summary>
        public int minGatesPerRing;

        /// <summary>Most gates the doctrine cuts into one ring.</summary>
        public int maxGatesPerRing;

        /// <summary>Minimum distance between two gates on a ring, metres.</summary>
        public float gateSiteSpacing;

        /// <summary>How far from a chosen exit point the gate may be cut,
        /// metres.</summary>
        public float gateSiteReach;

        /// <summary>How far outward of the ring a curtain's midpoint is probed
        /// to learn which territory that stretch of wall faces, metres.</summary>
        public float gateExitProbe;
    }
}
