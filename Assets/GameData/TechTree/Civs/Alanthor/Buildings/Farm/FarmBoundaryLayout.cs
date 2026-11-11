// FarmBoundaryLayout.cs
// WHERE A FARM FIELD'S BOUNDARY RUNS (docs/Design/Age_1_Alanthor.md § The
// Farm, "The field's look"): the pure layout of the level-2 fence / level-3
// low stone wall, as a list of straight edge segments, each flagged as a
// piece or an OPENING. No ECS, no rendering, no Unity objects: everything it
// knows about the world comes in through IWorld, so the presentation
// (FarmCropField, which reads the road MASK texture) and a future simulation
// version (which must read deterministic data — e.g. the road graph — and
// fixed-point maths) can share the rules.
//
// THE RULES
//   Candidate lines, each walked as a polyline and cut into pieces of about
//   Settings.PieceLength (Resample):
//     1. RIM — the ring's outer circle, Settings.Inset inside it.
//        Crop side = toward the Farm.
//     2. NEIGHBOUR LINE (2026-11-11) — where two rings overlap the land is
//        split straight down the middle (the perpendicular bisector of the two
//        centres; FarmRing.Field gives each half to the nearer Farm). Against a
//        Neighbour with Fence = true (a HOSTILE Farm) the boundary runs along
//        that bisector, Settings.Inset on this Farm's side, as far as this
//        Farm's rim circle. Against a friendly Farm (Fence = false) there is no
//        line at all: the two fields merge, and each rim stops where it meets
//        the bisector (the rim beyond it is the other Farm's land, so nothing
//        grows behind it and it is dropped), so the rims run on continuously.
//        Crop side = away from the other Farm.
//     3. ROAD EDGE — the iso-line RoadCoverage = Settings.RoadEdgeCoverage
//        inside the ring's square, traced by marching squares on a
//        Settings.GridMeters lattice and chained into polylines. Crop side =
//        the side with less road.
//   Each piece A-B (midpoint M, unit normal N toward the crop side):
//     * DROPPED when nothing grows behind it: the probe M + N x CropProbe is
//       not crop (outside the ring, on road coverage >= RoadEdgeCoverage,
//       the nearer Farm's land of another ring, or IsBlocked);
//     * DROPPED when A, M or B is hard-blocked (IsBlocked, but not merely
//       because the point is another Farm's land) — a building,
//       impassable ground, a node or a wall cuts the line: a plain gap;
//     * DROPPED for a neighbour line whose midpoint is outside this ring;
//     * an OPENING when RoadCoverage at A, M or B >= RoadBlockCoverage — a
//       road crosses the line here, so the boundary never closes a road;
//     * an OPENING for a RIM piece facing the Farm's door: M lies on the
//       door's side of the centre and within DoorOpeningMeters / 2 of the
//       ray from the centre along DoorDirection;
//     * otherwise a PIECE.
//   Order of output: rim, neighbour rims in the order given, road edges in
//   lattice order — stable for identical inputs.

using System.Collections.Generic;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class FarmBoundaryLayout
    {
        public enum EdgeKind : byte { Rim, NeighbourRim, RoadEdge }

        /// <summary>Another Farm whose ring may reach this one.</summary>
        public struct Neighbour
        {
            public float2 Centre;
            /// <summary>True: lay a boundary along the split (a hostile Farm).
            /// False: the fields merge (the same owner or an ally).</summary>
            public bool Fence;
        }

        /// <summary>One straight run of the boundary, world XZ metres.</summary>
        public struct Segment
        {
            public float2 A, B;
            public EdgeKind Kind;
            /// <summary>True: leave this run open (a road crosses, or the Farm's door faces it).</summary>
            public bool Opening;
        }

        /// <summary>What the layout needs to know about the world.</summary>
        public interface IWorld
        {
            /// <summary>The yield's blocked test (FarmRing.Field.IsBlocked):
            /// buildings, nodes, impassable ground, walls, other Farms' rings.</summary>
            bool IsBlocked(float2 p);
            /// <summary>Road coverage 0..1 at p (0 = no road).</summary>
            float RoadCoverage(float2 p);
        }

        public struct Settings
        {
            public float PieceLength;        // metres along the line
            public float Inset;              // rim inset / neighbour-rim outset, metres
            public float CropProbe;          // how far behind a piece "crop?" is asked, metres
            public float RoadEdgeCoverage;   // the road-edge iso-line, and "on road" for crop
            public float RoadBlockCoverage;  // a piece touching more than this is an opening
            public float GridMeters;         // marching-squares lattice step
            public float DoorOpeningMeters;  // width of the opening facing the door
        }

        /// <summary>Is <paramref name="p"/> in the ring round <paramref name="c"/>
        /// (outer radius <paramref name="r"/>, square footprint half <paramref name="h"/>)?
        /// The same test as FarmRing.InRing, with the sizes passed in.</summary>
        public static bool InRing(float2 c, float2 p, float r, float h)
        {
            float2 d = p - c;
            if (math.lengthsq(d) > r * r) return false;
            return math.abs(d.x) >= h || math.abs(d.y) >= h;
        }

        /// <summary>
        /// Lays out the boundary of the Farm field centred at <paramref name="centre"/>.
        /// Appends to <paramref name="output"/> (not cleared). See the file header
        /// for the rules.
        /// </summary>
        public static void Compute(float2 centre, float outerRadius, float footprintHalf, float2 doorDirection,
            IReadOnlyList<Neighbour> otherFarms, IWorld world, in Settings s, List<Segment> output)
        {
            var ctx = new Ctx
            {
                C = centre, R = outerRadius, H = footprintHalf,
                Door = math.normalizesafe(doorDirection), Others = otherFarms, World = world, S = s, Out = output,
            };

            ctx.Circle(centre, outerRadius - s.Inset);
            ctx.Place(EdgeKind.Rim, centre);

            for (int k = 0; k < otherFarms.Count; k++)
            {
                var o = otherFarms[k].Centre;
                if (!otherFarms[k].Fence) continue;
                if (math.distancesq(o, centre) >= 4f * outerRadius * outerRadius) continue;
                if (!ctx.SplitLine(o, outerRadius - s.Inset)) continue;
                ctx.Place(EdgeKind.NeighbourRim, o);
            }

            ctx.RoadEdges();
        }

        private sealed class Ctx
        {
            public float2 C, Door;
            public float R, H;
            public IReadOnlyList<Neighbour> Others;
            public IWorld World;
            public Settings S;
            public List<Segment> Out;

            private readonly List<float2> _poly = new List<float2>();
            private readonly List<float2> _pts = new List<float2>();

            public void Circle(float2 o, float r)
            {
                _poly.Clear();
                int n = math.max(12, (int)math.ceil(2f * math.PI * r / S.PieceLength));
                for (int i = 0; i <= n; i++)
                {
                    float a = i * 2f * math.PI / n;
                    _poly.Add(o + new float2(math.cos(a), math.sin(a)) * r);
                }
            }

            /// <summary>The bisector of this centre and <paramref name="o"/>,
            /// Inset toward this centre, as far as this Farm's rim circle
            /// (radius <paramref name="rimRadius"/>). False when it misses it.</summary>
            public bool SplitLine(float2 o, float rimRadius)
            {
                _poly.Clear();
                float2 d = o - C;
                float len = math.length(d);
                if (len < 1e-3f) return false;
                float2 n = d / len;
                float h = len * 0.5f - S.Inset;              // centre to the line
                if (h <= 0f || h >= rimRadius) return false;
                float half = math.sqrt(rimRadius * rimRadius - h * h);
                float2 mid = C + n * h;
                float2 t = new float2(-n.y, n.x);
                _poly.Add(mid - t * half);
                _poly.Add(mid + t * half);
                return true;
            }

            /// <summary>Another Farm's land: in its ring and nearer its centre.</summary>
            private bool OthersLand(float2 p)
            {
                float dSelf = math.distancesq(p, C);
                for (int i = 0; i < Others.Count; i++)
                {
                    var o = Others[i].Centre;
                    if (InRing(o, p, R, H) && math.distancesq(p, o) < dSelf) return true;
                }
                return false;
            }

            private bool Crop(float2 p)
                => InRing(C, p, R, H) && World.RoadCoverage(p) < S.RoadEdgeCoverage
                   && !OthersLand(p) && !World.IsBlocked(p);

            private bool HardBlocked(float2 p) => !OthersLand(p) && World.IsBlocked(p);

            /// <summary>Cuts _poly into pieces and classifies each. For a rim the
            /// crop side is toward <paramref name="o"/>; for a neighbour rim away
            /// from it; for a road edge the side with less road.</summary>
            public void Place(EdgeKind kind, float2 o)
            {
                Resample();
                for (int i = 0; i + 1 < _pts.Count; i++)
                {
                    float2 a = _pts[i], b = _pts[i + 1];
                    if (math.distancesq(a, b) < 0.04f) continue;
                    float2 m = (a + b) * 0.5f;
                    float2 d = math.normalize(b - a);
                    float2 n = new float2(-d.y, d.x);
                    float2 side;
                    if (kind == EdgeKind.Rim) side = math.dot(o - m, n) >= 0f ? n : -n;
                    else if (kind == EdgeKind.NeighbourRim) side = math.dot(m - o, n) >= 0f ? n : -n;
                    else side = World.RoadCoverage(m + n * S.CropProbe) <= World.RoadCoverage(m - n * S.CropProbe) ? n : -n;

                    if (kind == EdgeKind.NeighbourRim && !InRing(C, m, R, H)) continue;
                    if (!Crop(m + side * S.CropProbe)) continue;
                    if (HardBlocked(a) || HardBlocked(m) || HardBlocked(b)) continue;

                    bool road = World.RoadCoverage(a) >= S.RoadBlockCoverage
                             || World.RoadCoverage(m) >= S.RoadBlockCoverage
                             || World.RoadCoverage(b) >= S.RoadBlockCoverage;
                    bool door = false;
                    if (kind == EdgeKind.Rim && S.DoorOpeningMeters > 0f)
                    {
                        float2 v = m - C;
                        float along = math.dot(v, Door);
                        float across = math.abs(v.x * Door.y - v.y * Door.x);
                        door = along > 0f && across <= S.DoorOpeningMeters * 0.5f;
                    }
                    Out.Add(new Segment { A = a, B = b, Kind = kind, Opening = road || door });
                }
            }

            /// <summary>_poly → _pts about PieceLength apart (a short last piece
            /// is kept when longer than 45 % of one).</summary>
            private void Resample()
            {
                _pts.Clear();
                if (_poly.Count < 2) return;
                float len = S.PieceLength;
                _pts.Add(_poly[0]);
                float carry = 0f;
                for (int i = 0; i + 1 < _poly.Count; i++)
                {
                    float2 a = _poly[i], b = _poly[i + 1];
                    float seg = math.distance(a, b);
                    float t = len - carry;
                    while (t <= seg)
                    {
                        _pts.Add(math.lerp(a, b, t / math.max(seg, 1e-5f)));
                        t += len;
                    }
                    carry = seg - (t - len);
                }
                if (carry > len * 0.45f) _pts.Add(_poly[_poly.Count - 1]);
            }

            // ── Road edges: marching squares, chained ──────────────────────

            private readonly Dictionary<int, float2> _point = new Dictionary<int, float2>();
            private readonly Dictionary<int, List<int>> _links = new Dictionary<int, List<int>>();
            private readonly HashSet<long> _used = new HashSet<long>();
            private readonly List<int> _starts = new List<int>();
            private readonly List<int> _order = new List<int>();

            public void RoadEdges()
            {
                float half = R + 1f;
                float g = math.max(0.4f, S.GridMeters);
                int n = (int)math.ceil(2f * half / g) + 1;
                float2 org = C - half;
                float T = S.RoadEdgeCoverage;
                var val = new float[n * n];
                bool any = false;
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float v = World.RoadCoverage(org + new float2(i, j) * g);
                    val[j * n + i] = v;
                    any |= v >= T;
                }
                if (!any) return;

                for (int j = 0; j + 1 < n; j++)
                for (int i = 0; i + 1 < n; i++)
                {
                    float v0 = val[j * n + i], v1 = val[j * n + i + 1];
                    float v2 = val[(j + 1) * n + i + 1], v3 = val[(j + 1) * n + i];
                    int idx = (v0 >= T ? 1 : 0) | (v1 >= T ? 2 : 0) | (v2 >= T ? 4 : 0) | (v3 >= T ? 8 : 0);
                    if (idx == 0 || idx == 15) continue;
                    int eB = Edge(i, j, 0, n), eR = Edge(i + 1, j, 1, n), eT = Edge(i, j + 1, 0, n), eL = Edge(i, j, 1, n);
                    float2 p0 = org + new float2(i, j) * g;
                    Put(eB, p0 + new float2(Frac(v0, v1, T) * g, 0f));
                    Put(eR, p0 + new float2(g, Frac(v1, v2, T) * g));
                    Put(eT, p0 + new float2(Frac(v3, v2, T) * g, g));
                    Put(eL, p0 + new float2(0f, Frac(v0, v3, T) * g));
                    switch (idx)
                    {
                        case 1: case 14: Link(eL, eB); break;
                        case 2: case 13: Link(eB, eR); break;
                        case 3: case 12: Link(eL, eR); break;
                        case 4: case 11: Link(eR, eT); break;
                        case 6: case 9: Link(eB, eT); break;
                        case 7: case 8: Link(eL, eT); break;
                        case 5: Link(eL, eT); Link(eB, eR); break;
                        case 10: Link(eL, eB); Link(eR, eT); break;
                    }
                }

                // Chain: open ends first, then loops; lattice order keeps it stable.
                _starts.Clear();
                foreach (int k in _order) if (_links[k].Count == 1) _starts.Add(k);
                foreach (int k in _order) if (_links[k].Count != 1) _starts.Add(k);
                foreach (int start in _starts)
                {
                    while (true)
                    {
                        _poly.Clear();
                        int cur = start;
                        _poly.Add(_point[cur]);
                        while (true)
                        {
                            int next = -1;
                            var nbs = _links[cur];
                            for (int q = 0; q < nbs.Count; q++)
                                if (!_used.Contains(Key(cur, nbs[q]))) { next = nbs[q]; break; }
                            if (next < 0) break;
                            _used.Add(Key(cur, next));
                            cur = next;
                            _poly.Add(_point[cur]);
                        }
                        if (_poly.Count < 2) break;
                        Place(EdgeKind.RoadEdge, default);
                    }
                }
            }

            private static int Edge(int i, int j, int axis, int n) => ((j * n + i) << 1) | axis;
            private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            private static float Frac(float a, float b, float t) => math.saturate((t - a) / math.max(1e-5f, b - a));
            private void Put(int e, float2 p) { if (!_point.ContainsKey(e)) _point[e] = p; }

            private void Link(int a, int b)
            {
                if (!_links.TryGetValue(a, out var la)) { _links[a] = la = new List<int>(2); _order.Add(a); }
                if (!_links.TryGetValue(b, out var lb)) { _links[b] = lb = new List<int>(2); _order.Add(b); }
                la.Add(b); lb.Add(a);
            }
        }
    }
}
