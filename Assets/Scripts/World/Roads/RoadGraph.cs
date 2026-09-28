// RoadGraph.cs
// The geometry half of the road network (docs/Design/Roads.md §3):
//   * which sites connect — the relative-neighbourhood graph,
//   * where a road goes  — A* over the 1 m nav grid with a slope penalty,
//   * how it looks       — Chaikin smoothing of the cell path.
// Pure functions over plain lists; RoadNetwork owns the state and calls in.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.World.Roads
{
    public static class RoadGraph
    {
        // ── Relative-neighbourhood graph ──────────────────────────────────

        /// <summary>
        /// Edges (i, j) of the relative-neighbourhood graph over
        /// <paramref name="pts"/>: i and j are joined unless some third point
        /// k is closer to BOTH than they are to each other. Connected, planar,
        /// no hubs — the classic organic road web.
        ///
        /// Candidates first, then the exact lune test. In any 60° sector
        /// around i, a point k strictly nearer i than j blocks j (law of
        /// cosines: |jk|² ≤ |ik|² + |ij|² − |ik||ij| &lt; |ij|²), so an RNG
        /// neighbour of i is always the nearest point of its sector (the Yao
        /// graph). That bounds the candidates to six per point, and the
        /// lune test runs on those alone: O(n²) where the naive triple loop
        /// was O(n³). Coincident points have no sector and are always
        /// candidates. Output is sorted by (i, j), as the triple loop emitted.
        /// </summary>
        public static void RelativeNeighbourhood(List<Vector2> pts, List<int2> edges, RngScratch scratch)
        {
            edges.Clear();
            int n = pts.Count;
            if (n < 2) return;
            var cand = scratch.Candidates;
            cand.Clear();
            var best = scratch.SectorBest;
            var bestD = scratch.SectorBestD;

            for (int i = 0; i < n; i++)
            {
                for (int s = 0; s < 6; s++) { bestD[s] = float.MaxValue; }
                scratch.Tied.Clear();
                Vector2 pi = pts[i];
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    Vector2 d = pts[j] - pi;
                    float d2 = d.sqrMagnitude;
                    if (d2 <= 0f) { AddCandidate(cand, i, j); continue; }
                    float ang = Mathf.Atan2(d.y, d.x);                 // -π..π
                    int sector = (int)((ang + Mathf.PI) * (3f / Mathf.PI));
                    if (sector > 5) sector = 5; else if (sector < 0) sector = 0;
                    if (d2 < bestD[sector]) { bestD[sector] = d2; best[sector] = j; }
                    else if (d2 == bestD[sector]) scratch.Tied.Add(new int2(sector, j));
                }
                for (int s = 0; s < 6; s++)
                    if (bestD[s] < float.MaxValue) AddCandidate(cand, i, best[s]);
                // Ties at the sector minimum are not blocked by each other
                // (the test is strict), so every one stays a candidate.
                for (int t = 0; t < scratch.Tied.Count; t++)
                {
                    var tj = scratch.Tied[t];
                    if (bestD[tj.x] < float.MaxValue && (pts[tj.y] - pi).sqrMagnitude == bestD[tj.x])
                        AddCandidate(cand, i, tj.y);
                }
            }

            cand.Sort();
            long prev = long.MinValue;
            for (int c = 0; c < cand.Count; c++)
            {
                long key = cand[c];
                if (key == prev) continue;
                prev = key;
                int i = (int)(key >> 32), j = (int)(key & 0xffffffffL);
                float dij = (pts[i] - pts[j]).sqrMagnitude;
                bool blocked = false;
                for (int k = 0; k < n && !blocked; k++)
                {
                    if (k == i || k == j) continue;
                    if ((pts[i] - pts[k]).sqrMagnitude < dij && (pts[j] - pts[k]).sqrMagnitude < dij)
                        blocked = true;
                }
                if (!blocked) edges.Add(new int2(i, j));
            }
        }

        static void AddCandidate(List<long> cand, int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            cand.Add(((long)lo << 32) | (uint)hi);
        }

        /// <summary>Reused buffers for <see cref="RelativeNeighbourhood"/>.</summary>
        public sealed class RngScratch
        {
            public readonly List<long> Candidates = new List<long>();
            public readonly List<int2> Tied = new List<int2>();
            public readonly int[] SectorBest = new int[6];
            public readonly float[] SectorBestD = new float[6];
        }

        // ── Routing ───────────────────────────────────────────────────────

        /// <summary>Per-cell walkability for <see cref="Route{TPass}"/>. A struct
        /// constraint instead of a delegate: the A* asks it up to three times
        /// per neighbour, and a closure call per ask was most of a failed
        /// search's cost.</summary>
        public interface ICellTest { bool Passable(int2 cell); }

        /// <summary>
        /// Grid geometry for a route: read once per rebuild from
        /// NavGridSingleton so the A* never touches ECS mid-search.
        /// </summary>
        public struct Grid
        {
            public int Width, Height;
            public float CellSize;
            public float3 Origin;

            public int2 CellOf(Vector2 world)
                => new int2((int)math.floor((world.x - Origin.x) / CellSize),
                            (int)math.floor((world.y - Origin.z) / CellSize));

            public Vector2 CentreOf(int2 c)
                => new Vector2(Origin.x + (c.x + 0.5f) * CellSize,
                               Origin.z + (c.y + 0.5f) * CellSize);

            public bool InBounds(int2 c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;
        }

        /// <summary>Scratch reused across routes so a rebuild allocates nothing
        /// after the first.</summary>
        public sealed class Scratch
        {
            public float[] G;          // best cost so far per cell
            public int[] Parent;
            public int[] Stamp;        // generation stamp: which search touched the cell
            public float[] Height;     // lazily filled terrain heights
            public bool[] HeightKnown;
            public int Generation;
            public readonly List<int> Open = new List<int>();     // binary heap of cell indices
            public readonly List<float> OpenF = new List<float>();

            public void Ensure(int cells)
            {
                if (G != null && G.Length == cells) return;
                G = new float[cells]; Parent = new int[cells]; Stamp = new int[cells];
                Height = new float[cells]; HeightKnown = new bool[cells];
                Generation = 0;
            }
        }

        static readonly int2[] Steps =
        {
            new int2( 1, 0), new int2(-1, 0), new int2(0,  1), new int2(0, -1),
            new int2( 1, 1), new int2(-1, 1), new int2(1, -1), new int2(-1, -1),
        };

        /// <summary>
        /// A* from <paramref name="from"/> to <paramref name="to"/> on the
        /// nav grid. <paramref name="passable"/> answers per cell (the
        /// caller wraps NavGridQuery). Step cost is distance × (1 + slope
        /// penalty × |Δheight|) so a road prefers the flat way round. Returns
        /// false when no route exists within <paramref name="maxExpansions"/>
        /// — an island site simply gets no road.
        /// </summary>
        public static bool Route<TPass>(in Grid grid, Scratch s, ref TPass passable,
                                 Vector2 from, Vector2 to, float slopePenalty, int maxExpansions,
                                 float corridorMetres, List<Vector2> outPath)
            where TPass : struct, ICellTest
        {
            outPath.Clear();
            int cells = grid.Width * grid.Height;
            s.Ensure(cells);
            s.Generation++;
            int gen = s.Generation;

            int2 a = grid.CellOf(from), b = grid.CellOf(to);
            if (!grid.InBounds(a) || !grid.InBounds(b)) return false;
            int start = a.y * grid.Width + a.x, goal = b.y * grid.Width + b.x;
            // The search stays inside the endpoints' box grown by the
            // corridor: a road that has to leave it to get round a wall is
            // not a road anyone would build, and an unroutable pair then
            // fails after a bounded search instead of flooding the map.
            int pad = (int)math.ceil(corridorMetres / math.max(1e-3f, grid.CellSize));
            int2 lo = math.max(math.min(a, b) - pad, int2.zero);
            int2 hi = math.min(math.max(a, b) + pad, new int2(grid.Width - 1, grid.Height - 1));

            s.Open.Clear(); s.OpenF.Clear();
            s.Stamp[start] = gen; s.G[start] = 0f; s.Parent[start] = -1;
            HeapPush(s, start, Octile(a, b) * grid.CellSize);

            int expanded = 0;
            while (s.Open.Count > 0 && expanded < maxExpansions)
            {
                int cur = HeapPop(s);
                if (cur == goal)
                {
                    for (int c = goal; c != -1; c = s.Parent[c])
                        outPath.Add(grid.CentreOf(new int2(c % grid.Width, c / grid.Width)));
                    outPath.Reverse();
                    return true;
                }
                expanded++;
                int2 cc = new int2(cur % grid.Width, cur / grid.Width);
                float hCur = HeightAt(s, grid, cur, cc);

                for (int k = 0; k < 8; k++)
                {
                    int2 nc = cc + Steps[k];
                    if (nc.x < lo.x || nc.y < lo.y || nc.x > hi.x || nc.y > hi.y) continue;
                    int ni = nc.y * grid.Width + nc.x;
                    // Endpoints may sit inside a stamped footprint (the site
                    // itself); everything in between must be walkable.
                    if (ni != goal && !passable.Passable(nc)) continue;
                    // No corner cutting between two blocked orthogonals.
                    if (k >= 4)
                    {
                        if (!passable.Passable(new int2(cc.x + Steps[k].x, cc.y)) ||
                            !passable.Passable(new int2(cc.x, cc.y + Steps[k].y))) continue;
                    }
                    float dist = (k < 4 ? 1f : 1.41421356f) * grid.CellSize;
                    float dh = math.abs(HeightAt(s, grid, ni, nc) - hCur);
                    float step = dist * (1f + slopePenalty * dh);
                    float g = s.G[cur] + step;
                    if (s.Stamp[ni] == gen && g >= s.G[ni]) continue;
                    s.Stamp[ni] = gen; s.G[ni] = g; s.Parent[ni] = cur;
                    HeapPush(s, ni, g + Octile(nc, b) * grid.CellSize);
                }
            }
            return false;
        }

        static float HeightAt(Scratch s, in Grid grid, int idx, int2 c)
        {
            if (!s.HeightKnown[idx])
            {
                var w = grid.CentreOf(c);
                s.Height[idx] = TerrainUtility.GetHeight(w.x, w.y);
                s.HeightKnown[idx] = true;
            }
            return s.Height[idx];
        }

        static float Octile(int2 a, int2 b)
        {
            int dx = math.abs(a.x - b.x), dy = math.abs(a.y - b.y);
            return (dx + dy) + (1.41421356f - 2f) * math.min(dx, dy);
        }

        // Minimal binary heap over (Open, OpenF); duplicates are tolerated —
        // a stale entry pops with a worse F and is skipped by the G test.
        static void HeapPush(Scratch s, int idx, float f)
        {
            s.Open.Add(idx); s.OpenF.Add(f);
            int i = s.Open.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (s.OpenF[p] <= s.OpenF[i]) break;
                Swap(s, i, p); i = p;
            }
        }

        static int HeapPop(Scratch s)
        {
            int top = s.Open[0];
            int last = s.Open.Count - 1;
            s.Open[0] = s.Open[last]; s.OpenF[0] = s.OpenF[last];
            s.Open.RemoveAt(last); s.OpenF.RemoveAt(last);
            int i = 0, n = s.Open.Count;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && s.OpenF[l] < s.OpenF[m]) m = l;
                if (r < n && s.OpenF[r] < s.OpenF[m]) m = r;
                if (m == i) break;
                Swap(s, i, m); i = m;
            }
            return top;
        }

        static void Swap(Scratch s, int i, int j)
        {
            (s.Open[i], s.Open[j]) = (s.Open[j], s.Open[i]);
            (s.OpenF[i], s.OpenF[j]) = (s.OpenF[j], s.OpenF[i]);
        }

        // ── Curves ────────────────────────────────────────────────────────

        /// <summary>Douglas–Peucker: keep only the cells that bend the route by
        /// more than <paramref name="tolerance"/> metres. Endpoints pinned.</summary>
        public static void Simplify(List<Vector2> path, float tolerance, List<Vector2> outPts)
        {
            outPts.Clear();
            if (path.Count < 3) { outPts.AddRange(path); return; }
            var keep = new bool[path.Count];
            keep[0] = keep[path.Count - 1] = true;
            var stack = new Stack<int2>();
            stack.Push(new int2(0, path.Count - 1));
            while (stack.Count > 0)
            {
                var seg = stack.Pop();
                Vector2 a = path[seg.x], b = path[seg.y];
                Vector2 ab = b - a; float len2 = ab.sqrMagnitude;
                int worst = -1; float worstD = tolerance;
                for (int i = seg.x + 1; i < seg.y; i++)
                {
                    float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(path[i] - a, ab) / len2) : 0f;
                    float d = (path[i] - (a + ab * t)).magnitude;
                    if (d > worstD) { worstD = d; worst = i; }
                }
                if (worst < 0) continue;
                keep[worst] = true;
                stack.Push(new int2(seg.x, worst));
                stack.Push(new int2(worst, seg.y));
            }
            for (int i = 0; i < path.Count; i++) if (keep[i]) outPts.Add(path[i]);
        }

        /// <summary>
        /// Turn a simplified route into a road: resample it so no two knots
        /// are further apart than <paramref name="wavelength"/>, push every
        /// interior knot sideways by smooth noise (± <paramref name="amplitude"/>
        /// metres, fading to zero at both ends), and run a Unity
        /// <see cref="Spline"/> with auto-smoothed tangents through the knots.
        /// The curve is sampled every <paramref name="step"/> metres; if any
        /// sample lands on impassable ground the meander is abandoned and the
        /// un-pushed knots are splined instead, so a bend never enters a cliff.
        /// Returns false only if even that fails (the caller keeps the raw route).
        /// </summary>
        public static bool Meander(List<Vector2> waypoints, float amplitude, float wavelength, float step,
                                   int seed, System.Func<Vector2, bool> passable, List<Vector2> outPath,
                                   List<Vector2> knotScratch)
        {
            outPath.Clear();
            if (waypoints.Count < 2) { outPath.AddRange(waypoints); return true; }
            wavelength = Mathf.Max(2f, wavelength);

            // Knots at most one wavelength apart, with arc length carried so
            // the noise reads along the road rather than restarting per span.
            knotScratch.Clear();
            var arc = new List<float>();
            float s0 = 0f;
            knotScratch.Add(waypoints[0]); arc.Add(0f);
            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                Vector2 a = waypoints[i], b = waypoints[i + 1];
                float len = (b - a).magnitude;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / wavelength));
                for (int k = 1; k <= n; k++)
                {
                    float t = (float)k / n;
                    knotScratch.Add(Vector2.Lerp(a, b, t));
                    arc.Add(s0 + len * t);
                }
                s0 += len;
            }
            float total = Mathf.Max(1e-3f, s0);

            var pushed = new List<Vector2>(knotScratch.Count);
            for (int i = 0; i < knotScratch.Count; i++)
            {
                Vector2 p = knotScratch[i];
                if (i > 0 && i < knotScratch.Count - 1)
                {
                    Vector2 dir = (knotScratch[i + 1] - knotScratch[i - 1]).normalized;
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(arc[i], total - arc[i]) / wavelength);
                    float n = Mathf.PerlinNoise(arc[i] / wavelength * 0.9f + seed * 0.173f, seed * 0.311f) * 2f - 1f;
                    p += perp * (n * amplitude * ends);
                }
                pushed.Add(p);
            }

            if (Sample(pushed, step, passable, outPath)) return true;
            return Sample(knotScratch, step, passable, outPath);
        }

        static bool Sample(List<Vector2> knots, float step, System.Func<Vector2, bool> passable, List<Vector2> outPath)
        {
            outPath.Clear();
            var spline = new Spline();
            for (int i = 0; i < knots.Count; i++)
                spline.Add(new BezierKnot(new float3(knots[i].x, 0f, knots[i].y)), TangentMode.AutoSmooth);
            spline.Closed = false;

            float length = spline.GetLength();
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.1f, step)));
            for (int i = 0; i <= samples; i++)
            {
                float3 p = spline.EvaluatePosition((float)i / samples);
                var w = new Vector2(p.x, p.z);
                // Endpoints sit inside their own site; everything between must walk.
                if (i > 0 && i < samples && !passable(w)) { outPath.Clear(); return false; }
                outPath.Add(w);
            }
            return true;
        }

        /// <summary>Chaikin corner cutting, endpoints pinned. Kept for callers
        /// that want the cell path rounded without a spline.</summary>
        public static void Chaikin(List<Vector2> path, int passes, List<Vector2> scratch)
        {
            for (int p = 0; p < passes; p++)
            {
                if (path.Count < 3) return;
                scratch.Clear();
                scratch.Add(path[0]);
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1];
                    scratch.Add(Vector2.Lerp(a, b, 0.25f));
                    scratch.Add(Vector2.Lerp(a, b, 0.75f));
                }
                scratch.Add(path[path.Count - 1]);
                path.Clear();
                path.AddRange(scratch);
            }
        }
    }
}
