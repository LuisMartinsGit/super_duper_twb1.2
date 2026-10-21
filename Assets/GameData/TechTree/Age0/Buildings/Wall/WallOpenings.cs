// WallOpenings.cs
// Where a curved segment's swept curtain is SOLID, as runs of arc length —
// used by WallArtMesh (2026-10-11; WallCurveMesh, the swept procedural
// curtain it was shared with, was deleted 2026-10-20).
//
// Both builders used to decide per slice "which cell is nearest, is it
// solid", on their own grid: WallCurveMesh per 0.25 m station, WallArtMesh
// per whole art module. A module grid is not the cell grid, so a gate's hole
// came out up to a module wider or narrower than the gatehouse drawn in it,
// and every gate, tower and emplacement showed a sliver of missing wall or a
// slab of doubled-up masonry at its sides.
//
// Now each cell says how much of the curve it OPENS, in metres:
//   * a solid curtain cell opens nothing (< 0);
//   * a dead cell opens the ground it held: from the midpoint to its
//     neighbour on each side (0) — the breach reads as the cell's span;
//   * a piece that draws its own masonry opens exactly what it draws (> 0,
//     the half-length, centred on the cell): the gatehouse its WallGateSpan,
//     a tower or an emplacement one module.
// The openings are merged and the rest of [s0, s1] is returned as solid
// runs; each builder lays its masonry over those runs and stretches the last
// module of a run to fit, so the curtain meets every piece edge to edge.

using System.Collections.Generic;

namespace TheWaningBorder.Rendering
{
    public static class WallOpenings
    {
        /// <summary>Runs shorter than this are dropped (a hairline of wall
        /// between two openings reads as a glitch, not as wall).</summary>
        const float MinRun = 0.05f;

        static readonly List<int> _order = new List<int>();
        static readonly List<float> _lo = new List<float>();
        static readonly List<float> _hi = new List<float>();
        static readonly List<(float lo, float hi)> _iv = new List<(float lo, float hi)>();
        static IReadOnlyList<float> _sortArcs;
        static readonly System.Comparison<int> _byArc = (a, b) =>
        {
            int c = _sortArcs[a].CompareTo(_sortArcs[b]);
            return c != 0 ? c : a.CompareTo(b);
        };

        /// <summary>
        /// Fill <paramref name="runs"/> with the solid (start, end) arc
        /// intervals of [<paramref name="s0"/>, <paramref name="s1"/>].
        /// <paramref name="openHalf"/> answers per cell index: &lt; 0 solid,
        /// 0 its own span, &gt; 0 that half-length about the cell. With no
        /// cells the whole span is one run.
        /// </summary>
        public static void SolidRuns(float s0, float s1, IReadOnlyList<float> cellArcs,
                                     System.Func<int, float> openHalf, List<(float a, float b)> runs)
        {
            runs.Clear();
            int n = cellArcs != null ? cellArcs.Count : 0;
            if (n == 0 || openHalf == null)
            {
                if (s1 - s0 > MinRun) runs.Add((s0, s1));
                return;
            }

            _order.Clear();
            for (int i = 0; i < n; i++) _order.Add(i);
            _sortArcs = cellArcs;
            _order.Sort(_byArc);
            _sortArcs = null;

            // Every opening as an interval, in arc order of its cell.
            _lo.Clear(); _hi.Clear();
            for (int k = 0; k < n; k++)
            {
                int i = _order[k];
                float h = openHalf(i);
                if (h < 0f) continue;
                float arc = cellArcs[i];
                float lo, hi;
                if (h > 0f) { lo = arc - h; hi = arc + h; }
                else
                {
                    lo = k == 0 ? s0 : (cellArcs[_order[k - 1]] + arc) * 0.5f;
                    hi = k == n - 1 ? s1 : (arc + cellArcs[_order[k + 1]]) * 0.5f;
                }
                _lo.Add(lo); _hi.Add(hi);
            }

            // Sweep in order of each opening's START: an explicit
            // half-length (a gatehouse) can reach back past a neighbour's
            // span, so cell order is not opening order.
            _iv.Clear();
            for (int j = 0; j < _lo.Count; j++) _iv.Add((_lo[j], _hi[j]));
            _iv.Sort((x, y) => x.lo.CompareTo(y.lo));
            float at = s0;
            for (int j = 0; j < _iv.Count; j++)
            {
                float lo = _iv[j].lo < s0 ? s0 : _iv[j].lo;
                float hi = _iv[j].hi > s1 ? s1 : _iv[j].hi;
                if (hi <= at) continue;
                if (lo - at > MinRun) runs.Add((at, lo));
                if (hi > at) at = hi;
            }
            if (s1 - at > MinRun) runs.Add((at, s1));
        }
    }
}
