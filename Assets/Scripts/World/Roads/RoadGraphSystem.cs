// RoadGraphSystem.cs
// The SIMULATION road graph (docs/Design/Roads.md §0, 2026-10-14): one map
// network joining every resource node and every finished building, with
// forks and crossroads where routes converge, winding bends, and the
// shortest-road-path queries the Runai caravans walk.
//
// Deterministic by construction: sites are sorted by entity index, the graph
// is a pure function of the site set, and the rebuild is triggered from a
// lockstep system (RunaiTradeSystem) by a content hash — so every peer holds
// the same graph whatever else (a renderer) asks for it, and a loaded game
// rebuilds the same one on its first tick.

using System.Collections.Generic;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.World.Roads
{
    public static class RoadGraphSystem
    {
        private static RoadGraphSystemConfig _cfg;
        public static RoadGraphSystemConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RoadGraphSystemConfig>());

        // ── the graph ──────────────────────────────────────────────────
        /// <summary>Node positions (XZ). The first <see cref="SiteCount"/> are
        /// sites (node / building), the rest junctions.</summary>
        private static readonly List<float2> _pos = new List<float2>();
        private static readonly List<List<int>> _adj = new List<List<int>>();
        public static int SiteCount { get; private set; }
        public static int NodeCount => _pos.Count;
        /// <summary>Changes whenever the graph does; part of every path id.</summary>
        public static int Version { get; private set; }
        public static bool Built => _pos.Count > 0;

        private static ulong _hash;
        private static int _epoch = -1;

        // Per-goal shortest-path trees, built on demand and dropped on rebuild.
        private static readonly Dictionary<int, int[]> _nextHop = new Dictionary<int, int[]>();
        private static readonly Dictionary<int, float[]> _distTo = new Dictionary<int, float[]>();

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<BuildingSize>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
            ComponentType.Exclude<WallTag>(), ComponentType.Exclude<WallSegmentTag>(),
            ComponentType.Exclude<WallHubTag>(), ComponentType.Exclude<WallGateTag>(),
            ComponentType.Exclude<WallTowerTag>(), ComponentType.Exclude<BorderMainNodeTag>(),
            ComponentType.Exclude<SmallNodeTag>(),
        };
        static readonly ComponentType[] QT_Veilstone = { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Iron      = { ComponentType.ReadOnly<IronMineTag>(),            ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Supply    = { ComponentType.ReadOnly<SupplyNodeTag>(),          ComponentType.ReadOnly<LocalTransform>() };
        static CachedEntityQuery QC_Buildings, QC_Veilstone, QC_Iron, QC_Supply;

        private struct SiteIn { public Entity E; public float2 P; public float R; }
        /// <summary>Per node: how far from its centre counts as reached —
        /// a building's half-extent (its centre is inside its footprint), a
        /// node's cell, 0 for a junction.</summary>
        private static readonly List<float> _rad = new List<float>();
        private static readonly List<SiteIn> _sites = new List<SiteIn>();

        /// <summary>
        /// Rebuild the graph if the site set changed. Call only from a
        /// lockstep system: the result is deterministic, but WHEN it is
        /// rebuilt must be the same tick on every peer for the paths in
        /// flight to agree.
        /// </summary>
        public static void Refresh(EntityManager em)
        {
            _sites.Clear();
            GatherBuildings(em, QC_Buildings.Get(em, QT_Buildings));
            Gather(em, QC_Veilstone.Get(em, QT_Veilstone));
            Gather(em, QC_Iron.Get(em, QT_Iron));
            Gather(em, QC_Supply.Get(em, QT_Supply));
            _sites.Sort((a, b) => a.E.Index != b.E.Index ? a.E.Index.CompareTo(b.E.Index) : a.E.Version.CompareTo(b.E.Version));

            ulong h = 1469598103934665603UL;
            for (int i = 0; i < _sites.Count; i++)
            {
                var s = _sites[i];
                h = (h ^ (ulong)(uint)s.E.Index) * 1099511628211UL;
                h = (h ^ (ulong)(uint)s.E.Version) * 1099511628211UL;
                h = (h ^ (ulong)(uint)(int)math.round(s.P.x * 4f)) * 1099511628211UL;
                h = (h ^ (ulong)(uint)(int)math.round(s.P.y * 4f)) * 1099511628211UL;
            }
            if (h == _hash && _epoch == MatchLifecycle.MatchEpoch && Built) return;
            _hash = h;
            _epoch = MatchLifecycle.MatchEpoch;
            Build();
            Version = (int)(h & 0x7fff) | 1;
        }

        private static void Gather(EntityManager em, EntityQuery q)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                _sites.Add(new SiteIn { E = ents[i], P = xfs[i].Position.xz, R = Cfg.nodeRadius });
        }

        private static void GatherBuildings(EntityManager em, EntityQuery q)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sizes = q.ToComponentDataArray<BuildingSize>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                _sites.Add(new SiteIn
                {
                    E = ents[i], P = xfs[i].Position.xz,
                    // BuildingSize is in build cells; the radius is metres.
                    R = math.max(sizes[i].Width, sizes[i].Height) * BuildGrid.CellSize * 0.5f,
                });
        }

        // ── construction ───────────────────────────────────────────────

        private static readonly RoadGraph.RngScratch _scratch = new RoadGraph.RngScratch();
        private static readonly List<Vector2> _rngPts = new List<Vector2>();
        private static readonly List<int2> _rngEdges = new List<int2>();

        private static void Build()
        {
            // The old goal trees go back to the pool before the graph changes.
            foreach (var kv in _nextHop) _poolNext.Push(kv.Value);
            foreach (var kv in _distTo) _poolDist.Push(kv.Value);
            _pos.Clear(); _adj.Clear(); _nextHop.Clear(); _distTo.Clear(); _rad.Clear();
            SiteCount = _sites.Count;
            _rngPts.Clear();
            for (int i = 0; i < _sites.Count; i++)
            {
                _pos.Add(_sites[i].P);
                _rad.Add(_sites[i].R);
                _adj.Add(new List<int>());
                _rngPts.Add(new Vector2(_sites[i].P.x, _sites[i].P.y));
            }
            if (_sites.Count < 2) { BuildLookups(); return; }

            // The fork scans ask "which site / junction is near this point";
            // a grid over each answers it without walking every node.
            _gridPts.Clear();
            for (int i = 0; i < _sites.Count; i++) _gridPts.Add(_sites[i].P);
            _siteGrid.Build(_gridPts);
            _junctionGrid.Reset(_siteGrid.Min,
                                _siteGrid.Min + new float2(_siteGrid.Width, _siteGrid.Height) * _siteGrid.Cell,
                                _sites.Count, _sites.Count * 2);

            // 1. Connect everything: the relative-neighbourhood graph is
            //    connected, planar and hub-free.
            RoadGraph.RelativeNeighbourhood(_rngPts, _rngEdges, _scratch);
            for (int i = 0; i < _rngEdges.Count; i++) Link(_rngEdges[i].x, _rngEdges[i].y);

            // 2. Forks and crossroads: two routes leaving a node at a narrow
            //    angle share their first stretch and split at a junction.
            //    A junction near an existing one joins it (a crossroads), up
            //    to the degree cap. Two passes so junctions can fork again.
            for (int pass = 0; pass < 2; pass++)
            {
                int n = _pos.Count;
                for (int v = 0; v < n; v++) ForkAt(v);
            }

            // 3. The degree cap also holds at sites: drop the longest spare
            //    edges a site can lose without disconnecting the network.
            int cap = math.max(2, Cfg.maxJunctionDegree);
            for (int v = 0; v < _pos.Count; v++)
            {
                while (_adj[v].Count > cap)
                {
                    int drop = -1; float dl = -1f;
                    for (int k = 0; k < _adj[v].Count; k++)
                    {
                        int u = _adj[v][k];
                        float l = math.distance(_pos[v], _pos[u]);
                        if (l > dl && _adj[u].Count > 1 && StillConnectedWithout(v, u)) { dl = l; drop = u; }
                    }
                    if (drop < 0) break;
                    Unlink(v, drop);
                }
            }
            BuildLookups();
        }

        // ── post-build lookups (2026-10-15, perf) ──────────────────────
        // Edge lengths (a Bend + two distances) were recomputed on every
        // Dijkstra relaxation, and Nearest walked every node per walker per
        // tick. Both are pure functions of the finished graph, so they are
        // computed once here: the values are bit-identical to the old ones.

        private static int[] _csrStart = System.Array.Empty<int>();
        private static int[] _csrNbr = System.Array.Empty<int>();
        private static float[] _csrLen = System.Array.Empty<float>();
        private static readonly List<float2> _gridPts = new List<float2>();
        private static readonly RoadGraph.PointGrid _siteGrid = new RoadGraph.PointGrid();
        private static readonly RoadGraph.PointGrid _junctionGrid = new RoadGraph.PointGrid();
        /// <summary>Every node with at least one road — what Nearest may return.</summary>
        private static readonly RoadGraph.PointGrid _nearGrid = new RoadGraph.PointGrid();
        private static readonly List<int> _nearIdx = new List<int>();

        private static void BuildLookups()
        {
            int n = _pos.Count;
            int m = 0;
            for (int v = 0; v < n; v++) m += _adj[v].Count;
            _csrStart = new int[n + 1];
            _csrNbr = new int[m];
            _csrLen = new float[m];
            int at = 0;
            for (int v = 0; v < n; v++)
            {
                _csrStart[v] = at;
                var nb = _adj[v];
                for (int k = 0; k < nb.Count; k++) { _csrNbr[at] = nb[k]; _csrLen[at] = EdgeLength(v, nb[k]); at++; }
            }
            _csrStart[n] = at;

            _gridPts.Clear(); _nearIdx.Clear();
            for (int i = 0; i < n; i++)
                if (_adj[i].Count > 0) { _gridPts.Add(_pos[i]); _nearIdx.Add(i); }
            _nearGrid.Build(_gridPts);
        }

        private static void Link(int a, int b)
        {
            if (a == b || _adj[a].Contains(b)) return;
            _adj[a].Add(b); _adj[b].Add(a);
        }

        private static void Unlink(int a, int b) { _adj[a].Remove(b); _adj[b].Remove(a); }

        private static readonly List<int> _byAngle = new List<int>();
        private struct Angled { public float A; public int N; }
        private static readonly List<Angled> _angled = new List<Angled>();
        private sealed class AngledComparer : IComparer<Angled>
        {
            public static readonly AngledComparer Instance = new AngledComparer();
            public int Compare(Angled x, Angled y) => x.A != y.A ? x.A.CompareTo(y.A) : x.N.CompareTo(y.N);
        }

        private static void ForkAt(int v)
        {
            var cfg = Cfg;
            float maxAngle = math.radians(math.max(1f, cfg.forkAngleDegrees));
            int cap = math.max(3, cfg.maxJunctionDegree);
            for (int guard = 0; guard < 4; guard++)
            {
                var nb = _adj[v];
                if (nb.Count < 2) return;
                // Sort by angle, ties by node index — deterministic. The
                // angle is computed once per neighbour, not per comparison,
                // and the comparer is static (no closure per fork).
                float2 o = _pos[v];
                _angled.Clear();
                for (int k = 0; k < nb.Count; k++)
                {
                    int x = nb[k];
                    _angled.Add(new Angled { A = math.atan2(_pos[x].y - o.y, _pos[x].x - o.x), N = x });
                }
                _angled.Sort(AngledComparer.Instance);
                _byAngle.Clear();
                for (int k = 0; k < _angled.Count; k++) _byAngle.Add(_angled[k].N);

                int bestA = -1, bestB = -1; float bestGap = maxAngle;
                for (int k = 0; k < _byAngle.Count; k++)
                {
                    int a = _byAngle[k], b = _byAngle[(k + 1) % _byAngle.Count];
                    if (a == b) continue;
                    float2 da = math.normalizesafe(_pos[a] - o), db = math.normalizesafe(_pos[b] - o);
                    float gap = math.acos(math.clamp(math.dot(da, db), -1f, 1f));
                    if (gap < bestGap) { bestGap = gap; bestA = a; bestB = b; }
                }
                if (bestA < 0) return;

                float la = math.distance(o, _pos[bestA]), lb = math.distance(o, _pos[bestB]);
                float t = math.min(la, lb) * math.clamp(cfg.forkDistanceFraction, 0.1f, 0.8f);
                if (t < cfg.minJunctionClearance) return;
                float2 dir = math.normalizesafe(math.normalizesafe(_pos[bestA] - o) + math.normalizesafe(_pos[bestB] - o));
                float2 j = o + dir * t;

                // Never on ground no one can stand on (a forest, a cliff, water).
                if (!Standable(j)) return;
                // Clear of every site, so a junction never sits on a building.
                float clear2 = cfg.minJunctionClearance * cfg.minJunctionClearance;
                _siteGrid.Range(j, cfg.minJunctionClearance * 1.0001f + 0.01f, out int2 slo, out int2 shi);
                for (int cy = slo.y; cy <= shi.y; cy++)
                for (int cx = slo.x; cx <= shi.x; cx++)
                for (int s = _siteGrid.First(cx, cy); s >= 0; s = _siteGrid.Next(s))
                    if (s != v && math.distancesq(_pos[s], j) < clear2) return;

                // Join a nearby junction (a crossroads) or keep the spacing.
                // Nearest wins, the lowest index on a tie — what the old
                // in-order scan with a strict < picked.
                int J = -1; float jd = cfg.minJunctionSpacing * cfg.minJunctionSpacing;
                _junctionGrid.Range(j, cfg.minJunctionSpacing * 1.0001f + 0.01f, out int2 jlo, out int2 jhi);
                for (int cy = jlo.y; cy <= jhi.y; cy++)
                for (int cx = jlo.x; cx <= jhi.x; cx++)
                for (int k = _junctionGrid.First(cx, cy); k >= 0; k = _junctionGrid.Next(k))
                {
                    float d = math.distancesq(_pos[k], j);
                    if (d < jd || (d == jd && J >= 0 && k < J)) { jd = d; J = k; }
                }
                if (J == v || J == bestA || J == bestB) return;
                if (J >= 0 && _adj[J].Count + 3 > cap) return;
                if (J < 0)
                {
                    J = _pos.Count;
                    _pos.Add(j);
                    _rad.Add(0f);
                    _adj.Add(new List<int>());
                    _junctionGrid.Insert(J, j);
                }
                Unlink(v, bestA); Unlink(v, bestB);
                Link(v, J); Link(J, bestA); Link(J, bestB);
            }
        }

        private static readonly Queue<int> _bfs = new Queue<int>();
        // Generation stamps instead of clearing a bool[] per search.
        private static int[] _seen = new int[0];
        private static int _seenGen;

        private static bool StillConnectedWithout(int a, int b)
        {
            if (_seen.Length < _pos.Count) { _seen = new int[_pos.Count * 2]; _seenGen = 0; }
            int gen = ++_seenGen;
            _bfs.Clear(); _bfs.Enqueue(a); _seen[a] = gen;
            while (_bfs.Count > 0)
            {
                int x = _bfs.Dequeue();
                if (x == b) return true;
                var nb = _adj[x];
                for (int k = 0; k < nb.Count; k++)
                {
                    int y = nb[k];
                    if (x == a && y == b) continue;
                    if (_seen[y] == gen) continue;
                    _seen[y] = gen; _bfs.Enqueue(y);
                }
            }
            return false;
        }

        // ── geometry ───────────────────────────────────────────────────

        /// <summary>The deterministic bend point of edge (a, b): the
        /// midpoint pushed sideways by a hash of the pair, so roads wind.</summary>
        public static float2 Bend(int a, int b)
        {
            int lo = math.min(a, b), hi = math.max(a, b);
            float2 pa = _pos[lo], pb = _pos[hi];
            float2 d = pb - pa;
            float len = math.length(d);
            if (len < 1e-3f) return pa;
            uint h = math.hash(new int2(lo, hi)) ^ (uint)_hash;
            float s = (h & 0xffff) / 32767.5f - 1f;          // [-1, 1]
            float2 perp = new float2(-d.y, d.x) / len;
            return (pa + pb) * 0.5f + perp * (s * Cfg.winding * len);
        }

        private static float EdgeLength(int a, int b)
        {
            float2 m = Bend(a, b);
            return math.distance(_pos[a], m) + math.distance(m, _pos[b]);
        }

        public static float2 NodePos(int i) => _pos[i];

        /// <summary>The entity a node stands for: the site's entity, or for
        /// a junction a synthetic one (negative index) keyed by its quantised
        /// POSITION — the renderer keys its roads by it, so a junction that
        /// moves in a rebuild is a new site with new routes, never the old id
        /// still carrying a road to where the junction used to be (the old
        /// index-in-list id did exactly that). Presentation only: nothing in
        /// the simulation reads it.</summary>
        public static Entity NodeEntity(int i)
        {
            if (i < SiteCount) return _sites[i].E;
            float2 p = _pos[i];
            uint h = math.hash(new int2((int)math.round(p.x * 10f), (int)math.round(p.y * 10f)));
            return new Entity { Index = -(int)(h & 0x3fffffff) - 1, Version = 1 };
        }

        public static bool IsJunction(int i) => i >= SiteCount;
        public static IReadOnlyList<int> Neighbours(int i) => _adj[i];

        /// <summary>The nearest node with a road; the lowest index on a tie
        /// (what the old in-order scan with a strict &lt; returned). A ring
        /// search over the node grid: rings grow until no unsearched cell can
        /// hold anything as near as the best found.</summary>
        private static int Nearest(float2 p)
        {
            if (_nearIdx.Count == 0) return -1;
            var g = _nearGrid;
            int2 c = g.RawCellOf(p);
            int best = -1; float bd = float.MaxValue;
            // Rings that miss the grid entirely hold nothing: start at the
            // first one that reaches it (a query point off the node box).
            int r0 = math.max(0, math.max(math.max(-c.x, c.x - (g.Width - 1)), math.max(-c.y, c.y - (g.Height - 1))));
            for (int r = r0; ; r++)
            {
                int x0 = c.x - r, x1 = c.x + r, y0 = c.y - r, y1 = c.y + r;
                for (int cy = math.max(y0, 0); cy <= math.min(y1, g.Height - 1); cy++)
                {
                    bool edgeRow = cy == y0 || cy == y1;
                    for (int cx = math.max(x0, 0); cx <= math.min(x1, g.Width - 1); cx++)
                    {
                        if (!edgeRow && cx != x0 && cx != x1) continue;   // the ring only
                        for (int k = g.First(cx, cy); k >= 0; k = g.Next(k))
                        {
                            int i = _nearIdx[k];
                            float d = math.distancesq(_pos[i], p);
                            if (d < bd || (d == bd && i < best)) { bd = d; best = i; }
                        }
                    }
                }
                // Everything outside the searched block is at least r cells
                // away; the 1 cm margin covers float rounding.
                float lim = r * g.Cell - 0.01f;
                if (best >= 0 && lim > 0f && bd < lim * lim) break;
                if (x0 <= 0 && y0 <= 0 && x1 >= g.Width - 1 && y1 >= g.Height - 1) break;
            }
            return best;
        }

        // ── shortest paths ─────────────────────────────────────────────

        // Pooled per-goal arrays (returned on rebuild) and the heap scratch.
        private static readonly Stack<int[]> _poolNext = new Stack<int[]>();
        private static readonly Stack<float[]> _poolDist = new Stack<float[]>();
        private static bool[] _done = new bool[0];
        private static float[] _heapD = new float[0];
        private static int[] _heapN = new int[0];
        private static int _heapCount;

        /// <summary>
        /// Shortest-path tree toward <paramref name="goal"/>. A binary-heap
        /// Dijkstra keyed (distance, node index): it settles nodes in exactly
        /// the order the old O(V²) scan did (least distance, lowest index on a
        /// tie) and relaxes with the same rule — including the equal-distance,
        /// lower-index tie on an already settled node — so next-hop and
        /// distance are bit-identical. Pooled arrays may be longer than the
        /// node count; every reader stops at the node count.
        /// </summary>
        private static void Tree(int goal, out int[] next, out float[] dist)
        {
            if (_nextHop.TryGetValue(goal, out next) && _distTo.TryGetValue(goal, out dist)) return;
            int n = _pos.Count;
            next = TakeNext(n); dist = TakeDist(n);
            if (_done.Length < n) _done = new bool[n * 2];
            for (int i = 0; i < n; i++) { next[i] = -1; dist[i] = float.MaxValue; _done[i] = false; }
            dist[goal] = 0f; next[goal] = goal;
            _heapCount = 0;
            HeapPush(0f, goal);
            while (_heapCount > 0)
            {
                HeapPop(out float ud, out int u);
                if (_done[u] || ud != dist[u]) continue;   // a stale entry
                _done[u] = true;
                for (int e = _csrStart[u], end = _csrStart[u + 1]; e < end; e++)
                {
                    int w = _csrNbr[e];
                    float nd = ud + _csrLen[e];
                    if (nd < dist[w]) { dist[w] = nd; next[w] = u; HeapPush(nd, w); }
                    else if (nd == dist[w] && u < next[w]) next[w] = u;
                }
            }
            _nextHop[goal] = next; _distTo[goal] = dist;
        }

        private static int[] TakeNext(int n)
        {
            while (_poolNext.Count > 0) { var a = _poolNext.Pop(); if (a.Length >= n) return a; }
            return new int[n];
        }

        private static float[] TakeDist(int n)
        {
            while (_poolDist.Count > 0) { var a = _poolDist.Pop(); if (a.Length >= n) return a; }
            return new float[n];
        }

        private static bool HeapLess(int a, int b)
            => _heapD[a] < _heapD[b] || (_heapD[a] == _heapD[b] && _heapN[a] < _heapN[b]);

        private static void HeapSwap(int a, int b)
        {
            (_heapD[a], _heapD[b]) = (_heapD[b], _heapD[a]);
            (_heapN[a], _heapN[b]) = (_heapN[b], _heapN[a]);
        }

        private static void HeapPush(float d, int node)
        {
            if (_heapCount == _heapD.Length)
            {
                System.Array.Resize(ref _heapD, _heapCount * 2 + 16);
                System.Array.Resize(ref _heapN, _heapCount * 2 + 16);
            }
            int i = _heapCount++;
            _heapD[i] = d; _heapN[i] = node;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!HeapLess(i, p)) break;
                HeapSwap(i, p); i = p;
            }
        }

        private static void HeapPop(out float d, out int node)
        {
            d = _heapD[0]; node = _heapN[0];
            int last = --_heapCount;
            _heapD[0] = _heapD[last]; _heapN[0] = _heapN[last];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < _heapCount && HeapLess(l, m)) m = l;
                if (r < _heapCount && HeapLess(r, m)) m = r;
                if (m == i) break;
                HeapSwap(i, m); i = m;
            }
        }

        /// <summary>Distance from a to b along the roads (straight-line legs
        /// on and off the network). Straight-line when there is no graph.</summary>
        public static float RoadDistance(float3 a, float3 b)
        {
            float straight = math.distance(a.xz, b.xz);
            if (!Built) return straight;
            int s = Nearest(a.xz), g = Nearest(b.xz);
            if (s < 0 || g < 0) return straight;
            Tree(g, out _, out var dist);
            if (dist[s] == float.MaxValue) return straight;
            float road = math.distance(a.xz, _pos[s]) + dist[s] + math.distance(_pos[g], b.xz);
            return math.max(straight, road);
        }

        /// <summary>
        /// The next point to walk toward on the road path to <paramref name="goal"/>.
        /// <paramref name="pathId"/> 0 = start a new path (set it to 0 when the
        /// goal changes); the two refs carry the walker's place on the path
        /// between calls, so they live on the walker's component.
        /// </summary>
        public static float3 NextWaypoint(float3 pos, float3 goal, ref int pathId, ref int pathIndex)
        {
            if (!Built) return goal;
            int g = Nearest(goal.xz);
            if (g < 0) return goal;
            int wantId = (Version << 16) | (g + 1);

            Tree(g, out var next, out var distTo);
            if (pathId != wantId)
            {
                // ENTER THE NETWORK FORWARD: the node that minimises the walk
                // to it plus its road distance to the goal — not the nearest,
                // which after a rebuild is as often behind the walker as ahead
                // of it (the graph rebuilds whenever a building finishes).
                // Off-road walking counts double, so the walker joins the road.
                int s = -1; float best = float.MaxValue, walk = 0f;
                for (int i = 0; i < _pos.Count; i++)
                {
                    // cost >= distTo, so a node whose road distance alone
                    // is not below the best cannot win the strict < — skip
                    // its square root. Same pick as the full scan.
                    if (_adj[i].Count == 0 || distTo[i] >= best) continue;
                    float w = math.max(0f, math.distance(pos.xz, _pos[i]) - _rad[i]);
                    float cost = 2f * w + distTo[i];
                    if (cost < best) { best = cost; s = i; walk = w; }
                }
                // Off-road only when the goal is nearer than the road is.
                if (s < 0 || math.distance(pos.xz, goal.xz) <= walk) { pathId = 0; return goal; }
                pathId = wantId;
                pathIndex = Pack(-1, s, true);
            }

            for (int guard = 0; guard < 8; guard++)
            {
                Unpack(pathIndex, out int prev, out int target, out bool bendDone);
                if (target < 0 || target >= _pos.Count) { pathId = 0; return goal; }
                bool atNode = bendDone || prev < 0;
                float2 aim = atNode ? Approach(target, pos.xz) : Bend(prev, target);
                // A bend on ground no one can stand on is skipped, never walked into.
                if (!atNode && !Standable(aim)) { pathIndex = Pack(prev, target, true); continue; }
                // PASS-THROUGH TOLERANCE: a waypoint the walker only passes
                // (a bend, a junction, a site on the way) counts as reached from
                // passRadius, so it turns toward the next leg early and rounds
                // the corner instead of grinding into the rocks of an ore node.
                // Only the last node before the goal keeps the tight reach.
                float reach = Cfg.reachDistance + (target != g || !atNode ? Cfg.passRadius : 0f);
                // An edge point on blocked ground (a node against a cliff) is
                // reached from anywhere close to the site.
                if (atNode && !Standable(aim)) reach += _rad[target] + 2f;
                if (math.distancesq(pos.xz, aim) > reach * reach)
                    return new float3(aim.x, pos.y, aim.y);
                if (!bendDone && prev >= 0) { pathIndex = Pack(prev, target, true); continue; }
                if (target == g) return goal;                    // last leg, off the road
                int hop = next[target];
                if (hop < 0 || hop == target) return goal;       // unreachable: walk straight
                pathIndex = Pack(target, hop, false);
            }
            Unpack(pathIndex, out _, out int t2, out _);
            var a2 = Approach(t2, pos.xz);
            return new float3(a2.x, pos.y, a2.y);
        }

        /// <summary>Where a walker aims for node <paramref name="n"/>: a
        /// junction itself, but a site's footprint edge on the walker's side —
        /// a building's centre is inside it and a resource node's cell is
        /// impassable, so neither can be stood on.</summary>
        private static float2 Approach(int n, float2 from)
        {
            float r = _rad[n];
            if (r <= 0f) return _pos[n];
            float2 d = from - _pos[n];
            float l = math.length(d);
            d = l > 1e-3f ? d / l : new float2(1f, 0f);
            return _pos[n] + d * (r + 1f);
        }

        private static bool Standable(float2 p)
        {
            var cell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(new float3(p.x, 0f, p.y));
            return cell.x == int.MinValue || TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell);
        }

        /// <summary>The walker could not reach its current waypoint (its move
        /// order was dropped): count that waypoint as passed.</summary>
        public static void Skip(ref int pathId, ref int pathIndex)
        {
            if (pathId == 0 || !Built) return;
            Unpack(pathIndex, out int prev, out int target, out bool bendDone);
            if (target < 0 || target >= _pos.Count) { pathId = 0; return; }
            if (!bendDone && prev >= 0) { pathIndex = Pack(prev, target, true); return; }
            int g = (pathId & 0xffff) - 1;
            if (g < 0 || g >= _pos.Count || target == g) { pathId = 0; return; }
            Tree(g, out var next, out _);
            int hop = next[target];
            if (hop < 0 || hop == target) { pathId = 0; return; }
            pathIndex = Pack(target, hop, false);
        }

        // pathIndex = (prev + 1) << 16 | target << 1 | bendDone
        private static int Pack(int prev, int target, bool bendDone)
            => ((prev + 1) << 16) | (target << 1) | (bendDone ? 1 : 0);

        private static void Unpack(int v, out int prev, out int target, out bool bendDone)
        {
            prev = ((v >> 16) & 0x7fff) - 1;
            target = (v >> 1) & 0x7fff;
            bendDone = (v & 1) != 0;
        }
    }
}
