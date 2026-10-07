// AICurseRoute.cs
// ARMIES GO ROUND THE CURSE (2026-10-07, docs/Design/Game_AI.md § 6k).
//
// Developer report: "AI is marching through cursed territories and losing
// their army to the DOT." Cursed ground is a radius (BorderSettings
// nodeAuraRadius) round every living curse node, and a unit standing in it
// accrues exposure and takes damage over time (VeilExposureSystem). The nav
// layer routes the shortest way, so an army sent across the map diagonally
// walked straight through the curse at the centre of Mirror Marches.
//
// This planner keeps the AI's marches off it: a straight leg that passes
// within nodeAuraRadius + marginMeters of a curse node is replaced by a
// detour found by A* on a coarse grid (cellMeters) whose blocked cells are
// those cursed circles, simplified to the fewest waypoints with clear lines
// between them. A start or goal inside cursed ground is allowed (the army
// leaves it, or its objective stands in it); everything between is not.
// Host-side decision helper; the waypoints reach the simulation as ordinary
// formation moves through CommandRouter.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.AI
{
    public static class AICurseRoute
    {
        static AICurseRouteConfig Cfg => AICurseRouteConfig.I;

        static readonly ComponentType[] QT_Nodes =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Nodes;

        static readonly List<float2> _nodes = new List<float2>(32);
        static int _nodesFrame = -1;
        static float _radius;

        static void LoadNodes(EntityManager em)
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame == _nodesFrame) return;
            _nodesFrame = frame;
            _nodes.Clear();
            var settings = TheWaningBorder.Data.Border.BorderSettings.Get();
            _radius = (settings != null ? settings.nodeAuraRadius : 20f) + math.max(0f, Cfg.marginMeters);
            var q = QC_Nodes.Get(em, QT_Nodes);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == Faction.Border && hps[i].Value > 0)
                    _nodes.Add(xfs[i].Position.xz);
        }

        /// <summary>Does <paramref name="p"/> stand within the inflated cursed
        /// radius of a curse node?</summary>
        public static bool InCurse(EntityManager em, float3 p)
        {
            if (!Cfg.enabled) return false;
            LoadNodes(em);
            float r2 = _radius * _radius;
            for (int i = 0; i < _nodes.Count; i++)
                if (math.distancesq(_nodes[i], p.xz) <= r2) return true;
            return false;
        }

        /// <summary>
        /// <paramref name="p"/> pushed straight out of any cursed circle it
        /// stands in (for a stage or rally point, never for an objective).
        /// </summary>
        public static float3 OutOfCurse(EntityManager em, float3 p)
        {
            if (!Cfg.enabled) return p;
            LoadNodes(em);
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    float2 d = p.xz - _nodes[i];
                    float len = math.length(d);
                    if (len >= _radius) continue;
                    float2 dir = len > 1e-3f ? d / len : new float2(1f, 0f);
                    float2 q = _nodes[i] + dir * (_radius + 1f);
                    p = new float3(q.x, p.y, q.y);
                    moved = true;
                }
                if (!moved) break;
            }
            p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
            return p;
        }

        static bool SegmentClear(float2 a, float2 b)
        {
            float r2 = _radius * _radius;
            float2 ab = b - a;
            float l2 = math.lengthsq(ab);
            for (int i = 0; i < _nodes.Count; i++)
            {
                float2 c = _nodes[i];
                // A circle the segment starts or ends in does not block it.
                if (math.distancesq(c, a) <= r2 || math.distancesq(c, b) <= r2) continue;
                float t = l2 > 1e-6f ? math.saturate(math.dot(c - a, ab) / l2) : 0f;
                if (math.distancesq(c, a + ab * t) <= r2) return false;
            }
            return true;
        }

        /// <summary>
        /// The waypoints a march from <paramref name="from"/> to
        /// <paramref name="to"/> should take round the curse, ending at
        /// <paramref name="to"/>. False (and <paramref name="into"/> empty)
        /// when the straight line is already clear or no detour exists.
        /// </summary>
        public static bool TryRoute(EntityManager em, float3 from, float3 to, List<float3> into)
        {
            into.Clear();
            if (!Cfg.enabled) return false;
            LoadNodes(em);
            if (_nodes.Count == 0 || SegmentClear(from.xz, to.xz)) return false;

            TheWaningBorder.World.Terrain.TerrainUtility.GetPlayableBounds(out var bmin, out var bmax);
            float cs = math.max(2f, Cfg.cellMeters);
            int W = math.max(2, (int)math.ceil((bmax.x - bmin.x) / cs));
            int H = math.max(2, (int)math.ceil((bmax.y - bmin.y) / cs));
            if ((long)W * H > 200_000) return false;
            float2 o = new float2(bmin.x, bmin.y);
            int2 Cell(float2 p) => new int2(math.clamp((int)((p.x - o.x) / cs), 0, W - 1),
                                            math.clamp((int)((p.y - o.y) / cs), 0, H - 1));
            float2 Centre(int x, int z) => o + new float2((x + 0.5f) * cs, (z + 0.5f) * cs);

            var blocked = new bool[W * H];
            float r2 = _radius * _radius;
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    float2 c = Centre(x, z);
                    for (int i = 0; i < _nodes.Count; i++)
                        if (math.distancesq(c, _nodes[i]) <= r2) { blocked[z * W + x] = true; break; }
                }
            int2 s = Cell(from.xz), g = Cell(to.xz);
            // The start and goal may stand in cursed ground; free their cells
            // and the cursed circles they stand in, or there is no way out.
            void FreeAround(float2 p)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (math.distancesq(p, _nodes[i]) > r2) continue;
                    for (int z = 0; z < H; z++)
                        for (int x = 0; x < W; x++)
                            if (math.distancesq(Centre(x, z), _nodes[i]) <= r2) blocked[z * W + x] = false;
                }
            }
            FreeAround(from.xz); FreeAround(to.xz);
            blocked[s.y * W + s.x] = false; blocked[g.y * W + g.x] = false;

            // A* (8-neighbour, octile), deterministic tie-break on index.
            int N = W * H;
            var gScore = new float[N];
            var came = new int[N];
            var closed = new bool[N];
            for (int i = 0; i < N; i++) { gScore[i] = float.MaxValue; came[i] = -1; }
            int si = s.y * W + s.x, gi = g.y * W + g.x;
            gScore[si] = 0f;
            var open = new SortedSet<(float f, int i)>();
            float H0(int i) { int x = i % W, z = i / W; float dx = math.abs(x - g.x), dz = math.abs(z - g.y);
                              return (dx + dz) + (1.41421356f - 2f) * math.min(dx, dz); }
            open.Add((H0(si), si));
            bool found = false;
            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                int ci = cur.i;
                if (closed[ci]) continue;
                closed[ci] = true;
                if (ci == gi) { found = true; break; }
                int cx = ci % W, cz = ci / W;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                        int ni = nz * W + nx;
                        if (blocked[ni] || closed[ni]) continue;
                        if (dx != 0 && dz != 0 && (blocked[cz * W + nx] || blocked[nz * W + cx])) continue;
                        float ng = gScore[ci] + (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                        if (ng >= gScore[ni]) continue;
                        gScore[ni] = ng; came[ni] = ci;
                        open.Add((ng + H0(ni), ni));
                    }
            }
            if (!found) return false;

            var path = new List<float2>();
            for (int i = gi; i >= 0; i = came[i])
            {
                int x = i % W, z = i / W;
                path.Add(Centre(x, z));
                if (i == si) break;
            }
            path.Reverse();
            path[0] = from.xz;
            path[path.Count - 1] = to.xz;

            // String-pull: the furthest point still in clear line from here.
            int k = 0;
            while (k < path.Count - 1)
            {
                int far = k + 1;
                for (int j = path.Count - 1; j > k + 1; j--)
                    if (SegmentClear(path[k], path[j])) { far = j; break; }
                if (far == path.Count - 1) break;
                var w = path[far];
                into.Add(new float3(w.x, TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(w.x, w.y), w.y));
                k = far;
            }
            into.Add(to);
            return into.Count > 1;
        }
    }
}
