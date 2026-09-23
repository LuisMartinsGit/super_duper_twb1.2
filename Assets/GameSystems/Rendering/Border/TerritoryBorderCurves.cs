// TerritoryBorderCurves.cs
// One smooth boundary curve per territory, projected onto the ground as a
// decal and tinted by whoever owns it.
//
// History of the technique (all 2026-09-03):
//   * TerritoryBorderDecals projected a ≤256-texel outline baked from a
//     blocky mask-edge test — one texel covered a metre-plus of ground, so
//     the border rendered as the staircase of the sample raster.
//   * The first rewrite traced the boundary into Chaikin-smoothed polylines
//     and rendered terrain-conforming ribbon MESHES. Smooth, but a mesh sits
//     at its baked heights: it floats over dips and clips through bumps —
//     everything GroundDecals exists to avoid.
//   * NOW: the traced curves are kept, and they are RASTERISED ANTI-ALIASED
//     (distance-to-curve alpha, not a mask edge test) into one outline
//     texture per territory, projected by the same pooled GroundDecals
//     projectors the selection rings use. Ground conformance from the
//     projector, smoothness from the vector curve the texture is drawn from.
//
// The §3b contract is unchanged: territory shapes are FIXED, so the trace,
// the textures and the projector placement happen once at load and are never
// recomputed. Ownership changes swap the projector's texture/tint; the only
// per-frame work is an int compare on TerritoryOwnership.Version.
//
// Each territory's line is drawn INSIDE its own ground (inset by half the
// line width from the boundary), so two owned neighbours produce a two-tone
// border with no overlap. Unclaimed territories draw a thinner dark-gray
// line from a second baked texture. Because decals paint the terrain surface
// itself, the fog-of-war overlay covers borders with no ordering tricks.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Traces a smooth border curve per territory once, bakes it into an
    /// anti-aliased outline texture projected by <see cref="GroundDecals"/>,
    /// and keeps the colour in step with <see cref="TerritoryOwnership"/>.
    /// </summary>
    public sealed class TerritoryBorderCurves : MonoBehaviour
    {
        /// <summary>Cells across the map when sampling the partition. One pass.
        /// 512 -> 768 (2026-09-03): at 2 m sample steps the marching-squares
        /// midpoints dithered between rows along near-straight boundary runs,
        /// and the residual wiggle after smoothing (~0.5 m) was comparable to
        /// the line's own width — a "straight" border visibly wobbled.
        /// Finer sampling plus the pre-smoothing kernel below flattens it.</summary>
        const int SampleRes = 768;

        /// <summary>Line width in metres, drawn inward from the boundary.</summary>
        const float LineWidth = 0.6f;

        /// <summary>Width of an UNCLAIMED territory's border. Thinner than an
        /// owned one so held ground still dominates the map read.</summary>
        const float UnclaimedWidth = 0.3f;

        /// <summary>Curve resample spacing in metres — the polyline the
        /// texture bake stamps segment by segment.</summary>
        const float PointSpacing = 1.5f;

        /// <summary>Loops shorter than this (metres of perimeter) are sampling
        /// specks, not territory boundaries.</summary>
        const float MinLoopLength = 12f;

        /// <summary>Longest edge of a baked per-territory outline texture.
        /// 768 over a ~350 m territory is ~2 texels/m — with the AA bake and
        /// bilinear sampling the curve reads smooth at any gameplay zoom
        /// (the old blocky look came from a 0.8 texel/m un-smoothed MASK,
        /// not from projection itself).</summary>
        const int MaxTerritoryTex = 768;

        /// <summary>Grown past the territory so the line sits on the edge,
        /// not clipped by the projector's rectangle.</summary>
        const float Pad = 2f;

        const float Alpha = 0.8f;

        // (The curse used to draw a purple line here. It draws none now —
        // see Retint and CurseBarrierVfx.)
        /// <summary>Unclaimed territory border: dark gray, same opacity.</summary>
        static readonly Color NaturalColor = new Color(0.22f, 0.22f, 0.22f, Alpha);

        /// <summary>The traced, smoothed curves of one territory — kept on
        /// the CPU so the texture can be repainted per state (width and
        /// colour both change with ownership) instead of storing one GPU
        /// texture per width variant.</summary>
        struct CurveSet
        {
            public List<Chain> Chains;
            public float[] Inward;
            public Vector2 BMin, BMax;
        }

        // One projector + ONE tinted texture per territory, repainted from
        // the stored curves when that territory's owner class changes. Null
        // entries: the territory produced no drawable boundary.
        DecalProjector[] _projectors;
        Texture2D[] _textures;
        CurveSet[] _curves;
        int[] _paintedOwner;   // last owner painted; int.MinValue = never
        bool _baked;
        static TerritoryBorderCurves _instance;

        /// <summary>True once this match's curves are traced — what
        /// CurseBarrierVfx waits on before building its veils.</summary>
        public static bool CurvesReady => _instance != null && _instance._baked;

        /// <summary>
        /// The traced boundary loops of one territory, world XZ, smoothed and
        /// resampled at PointSpacing — the SAME centreline the decal is
        /// painted from, so the curse veil stands exactly where the purple
        /// line used to be. Open chains (loose ends on the map rim) are
        /// returned as-is. False when the territory has no drawable boundary.
        /// </summary>
        public static bool TryGetLoops(int territory, List<List<Vector2>> into)
        {
            into.Clear();
            var self = _instance;
            if (self == null || !self._baked || self._curves == null) return false;
            if (territory < 0 || territory >= self._curves.Length) return false;
            var set = self._curves[territory];
            if (set.Chains == null) return false;
            foreach (var chain in set.Chains)
                if (chain.Pts != null && chain.Pts.Count >= 2) into.Add(chain.Pts);
            return into.Count > 0;
        }
        // Partition version the ribbon was traced from — a bool latch alone
        // traced whatever RegionMap held first, which on a second map in one
        // session was the previous map's partition (2026-09-11).
        int _regionVersion = -1;
        int _ownershipVersion = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!MapRegistry.IsGameplayScene(scene.name)) return;
            if (FindFirstObjectByType<TerritoryBorderCurves>() != null) return;
            new GameObject("[Territory Borders]").AddComponent<TerritoryBorderCurves>();
        }

        void LateUpdate()
        {
            _instance = this;
            if (!_baked || _regionVersion != RegionMap.Version)
            {
                if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
                Bake();
                _baked = true;
                _regionVersion = RegionMap.Version;
            }

            // The ONLY per-frame work: an int compare.
            if (_ownershipVersion == TerritoryOwnership.Version) return;
            _ownershipVersion = TerritoryOwnership.Version;
            Retint();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_projectors != null)
                foreach (var p in _projectors) GroundDecals.Return(p);
            if (_textures != null)
                foreach (var t in _textures)
                    if (t != null) Destroy(t);
        }

        #region Bake (once)

        void Bake()
        {
            if (!TerrainUtility.TryGetWorldBounds(out Vector2 min, out Vector2 max))
            {
                Debug.LogError("[TerritoryBorders] No terrain bounds — borders cannot be baked.");
                return;
            }

            Vector2 size = max - min;
            int count = RegionMap.Count;
            _projectors = new DecalProjector[count];
            _textures = new Texture2D[count];
            _curves = new CurveSet[count];
            _paintedOwner = new int[count];
            for (int i = 0; i < count; i++) _paintedOwner[i] = int.MinValue;

            // ONE pass over the map recording which territory owns each cell —
            // the exact partition every other consumer draws, warp included.
            var cell = new short[SampleRes * SampleRes];
            for (int y = 0; y < SampleRes; y++)
            {
                float wz = min.y + (y + 0.5f) / SampleRes * size.y;
                for (int x = 0; x < SampleRes; x++)
                {
                    float wx = min.x + (x + 0.5f) / SampleRes * size.x;
                    cell[y * SampleRes + x] = (short)RegionMap.RegionAt(wx, wz);
                }
            }

            float stepX = size.x / SampleRes;
            float stepY = size.y / SampleRes;
            int built = 0;

            for (int r = 0; r < count; r++)
            {
                var loops = TraceLoops(cell, r, min, stepX, stepY);
                if (loops.Count == 0) continue;

                // Sample space → world XZ, smoothed and evenly resampled.
                // The same centreline serves every repaint of this territory.
                var chains = new List<Chain>(loops.Count);
                foreach (var chain in loops)
                {
                    var pts = new List<Vector2>(chain.Pts.Count);
                    foreach (var p in chain.Pts)
                        pts.Add(new Vector2(min.x + (p.x + 0.5f) * stepX,
                                            min.y + (p.y + 0.5f) * stepY));

                    // Points arrive refined onto the true boundary (the
                    // bisection in TraceLoops), so smoothing is light: one
                    // averaging pass irons residual sub-sample noise, two
                    // Chaikin passes round the polyline into a curve.
                    // Heavier smoothing was compensating for lattice dither
                    // that no longer exists — and eating real corners.
                    pts = SmoothKernel(pts, chain.Closed);
                    pts = Chaikin(pts, chain.Closed);
                    pts = Chaikin(pts, chain.Closed);
                    pts = Resample(pts, PointSpacing, chain.Closed);
                    if (pts.Count < 4) continue;

                    float length = 0f;
                    int segs = chain.Closed ? pts.Count : pts.Count - 1;
                    for (int i = 0; i < segs; i++)
                        length += Vector2.Distance(pts[i], pts[(i + 1) % pts.Count]);
                    if (length < MinLoopLength) continue;

                    chains.Add(new Chain { Pts = pts, Closed = chain.Closed });
                }
                if (chains.Count == 0) continue;

                // Inward direction per chain, then the shared bounding box.
                var inward = new float[chains.Count];
                Vector2 bMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 bMax = new Vector2(float.MinValue, float.MinValue);
                for (int c = 0; c < chains.Count; c++)
                {
                    inward[c] = ProbeInwardSign(chains[c].Pts, r);
                    foreach (var p in chains[c].Pts)
                    {
                        bMin = Vector2.Min(bMin, p);
                        bMax = Vector2.Max(bMax, p);
                    }
                }
                bMin -= new Vector2(Pad, Pad);
                bMax += new Vector2(Pad, Pad);

                // The texture is created empty and sized once; every state
                // change repaints its CONTENT from the stored curves (width
                // and colour both depend on the owner), so one GPU texture
                // serves the territory for the whole match.
                Vector2 span = bMax - bMin;
                float longest = Mathf.Max(span.x, span.y);
                if (longest <= 0f) continue;
                float texPerM = MaxTerritoryTex / longest;
                int tw = Mathf.Clamp(Mathf.RoundToInt(span.x * texPerM), 2, MaxTerritoryTex);
                int th = Mathf.Clamp(Mathf.RoundToInt(span.y * texPerM), 2, MaxTerritoryTex);
                _textures[r] = new Texture2D(tw, th, TextureFormat.RGBA32, false)
                {
                    name = $"TerritoryCurve_{r}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _curves[r] = new CurveSet { Chains = chains, Inward = inward, BMin = bMin, BMax = bMax };

                // One projector per territory, placed once — same pooled
                // projection path as the selection rings, so the line hugs
                // every slope. Rented with a tiny stand-in shape: the first
                // Retint paints and assigns the real texture.
                var projector = GroundDecals.Rent(Texture2D.whiteTexture, Color.clear);
                float cx = (bMin.x + bMax.x) * 0.5f;
                float cz = (bMin.y + bMax.y) * 0.5f;
                var centre = new Vector3(cx, TerrainUtility.GetHeight(cx, cz), cz);
                GroundDecals.Place(projector, centre, bMax.x - bMin.x, bMax.y - bMin.y);
                projector.gameObject.SetActive(false);   // Retint decides
                _projectors[r] = projector;
                built++;
            }

            TWBLog.Log($"[TerritoryBorders] traced and baked {built} territory curve(s), once.");
        }

        // Scratch buffers reused across repaints (sized for the largest
        // territory painted so far).
        float[] _alphaScratch;
        Color32[] _pxScratch;

        /// <summary>
        /// Rasterise a territory's smoothed curves into its texture — colour
        /// AND width belong to the current owner, so this runs on ownership
        /// change, not per frame. The line is stamped by DISTANCE to the
        /// polyline with a feathered edge — anti-aliased by construction,
        /// unlike the old mask-edge bake. The centreline is inset by half the
        /// width so the band sits inside the territory's own ground.
        /// </summary>
        void Paint(int region, float widthMetres, Color tint)
        {
            var tex = _textures[region];
            var set = _curves[region];
            Vector2 span = set.BMax - set.BMin;
            int tw = tex.width;
            int th = tex.height;
            float texPerM = tw / span.x;

            float halfWidthTex = widthMetres * 0.5f * texPerM;
            // 1.25 -> 0.9 texels (2026-09-03): with the feather rivalling the
            // line's own half-width the edge softness dominated the line and
            // amplified every sub-texel wobble.
            const float feather = 0.9f;

            int cells = tw * th;
            if (_alphaScratch == null || _alphaScratch.Length < cells)
            {
                _alphaScratch = new float[cells];
                _pxScratch = new Color32[cells];
            }
            System.Array.Clear(_alphaScratch, 0, cells);

            var chains = set.Chains;
            for (int c = 0; c < chains.Count; c++)
            {
                var pts = chains[c].Pts;
                int n = pts.Count;
                bool closed = chains[c].Closed;
                int segs = closed ? n : n - 1;

                for (int i = 0; i < segs; i++)
                {
                    // Inset the centreline by half the width along the inward
                    // normal, then stamp in texture space.
                    Vector2 wa = InsetPoint(pts, i, n, closed, set.Inward[c], widthMetres);
                    Vector2 wb = InsetPoint(pts, (i + 1) % n, n, closed, set.Inward[c], widthMetres);

                    Vector2 a = new Vector2((wa.x - set.BMin.x) * texPerM, (wa.y - set.BMin.y) * texPerM);
                    Vector2 b = new Vector2((wb.x - set.BMin.x) * texPerM, (wb.y - set.BMin.y) * texPerM);

                    float margin = halfWidthTex + feather + 1f;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - margin));
                    int x1 = Mathf.Min(tw - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + margin));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - margin));
                    int y1 = Mathf.Min(th - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + margin));

                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = DistanceToSegment(x + 0.5f, y + 0.5f, a, b);
                        float v = Mathf.Clamp01((halfWidthTex + feather * 0.5f - d) / feather);
                        int idx = y * tw + x;
                        if (v > _alphaScratch[idx]) _alphaScratch[idx] = v;
                    }
                }
            }

            var c32 = (Color32)tint;
            for (int i = 0; i < cells; i++)
            {
                byte a8 = (byte)(_alphaScratch[i] * c32.a);
                _pxScratch[i] = new Color32(c32.r, c32.g, c32.b, a8);
            }
            // SetPixels32 with the exact-length slice of the shared scratch.
            if (_pxScratch.Length == cells)
            {
                tex.SetPixels32(_pxScratch);
            }
            else
            {
                var exact = new Color32[cells];
                System.Array.Copy(_pxScratch, exact, cells);
                tex.SetPixels32(exact);
            }
            tex.Apply(false, false);
        }

        /// <summary>One pass of a 0.25/0.5/0.25 averaging kernel. Cancels the
        /// row dither of marching-squares midpoints so straight boundary runs
        /// come out straight; open chains keep their endpoints.</summary>
        static List<Vector2> SmoothKernel(List<Vector2> pts, bool closed)
        {
            int n = pts.Count;
            if (n < 3) return pts;
            var outPts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                if (!closed && (i == 0 || i == n - 1))
                {
                    outPts.Add(pts[i]);
                    continue;
                }
                Vector2 prev = pts[(i - 1 + n) % n];
                Vector2 next = pts[(i + 1) % n];
                outPts.Add(pts[i] * 0.5f + (prev + next) * 0.25f);
            }
            return outPts;
        }

        static Vector2 InsetPoint(List<Vector2> pts, int i, int n, bool closed,
            float inwardSign, float widthMetres)
        {
            Vector2 fwd = closed ? pts[(i + 1) % n] : pts[Mathf.Min(i + 1, n - 1)];
            Vector2 bck = closed ? pts[(i - 1 + n) % n] : pts[Mathf.Max(i - 1, 0)];
            Vector2 dir = (fwd - bck).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * inwardSign;
            return pts[i] + normal * (widthMetres * 0.5f);
        }

        static float DistanceToSegment(float px, float py, Vector2 a, Vector2 b)
        {
            float abx = b.x - a.x, aby = b.y - a.y;
            float len2 = abx * abx + aby * aby;
            if (len2 <= 1e-6f)
                return Mathf.Sqrt((px - a.x) * (px - a.x) + (py - a.y) * (py - a.y));
            float t = Mathf.Clamp01(((px - a.x) * abx + (py - a.y) * aby) / len2);
            float cx = a.x + abx * t, cy = a.y + aby * t;
            return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        /// <summary>One traced boundary chain: a closed loop, or an open run
        /// whose loose ends sit on the sample-grid rim.</summary>
        struct Chain
        {
            public List<Vector2> Pts;
            public bool Closed;
        }

        /// <summary>
        /// Marching squares over the partition sample for one territory,
        /// stitched into chains, with every boundary point REFINED by
        /// bisection along its lattice edge (RegionMap.RegionAt is a pure
        /// function, so the true crossing can be queried directly). The
        /// midpoint form quantised every point to the sample lattice — a
        /// ±0.7 m error that survived smoothing as the wobble on straight
        /// boundary runs. Sample-space coordinates (fractional).
        /// </summary>
        static List<Chain> TraceLoops(short[] cell, int region,
            Vector2 min, float stepX, float stepY)
        {
            // Segment endpoints keyed by lattice edge id so shared endpoints
            // stitch exactly. Horizontal edge (x..x+1, y): key = (y*Res+x)*2.
            // Vertical edge (x, y..y+1): key = (y*Res+x)*2+1.
            var links = new Dictionary<long, List<long>>();

            void AddSegment(long a, long b)
            {
                if (!links.TryGetValue(a, out var la)) links[a] = la = new List<long>(2);
                la.Add(b);
                if (!links.TryGetValue(b, out var lb)) links[b] = lb = new List<long>(2);
                lb.Add(a);
            }

            for (int y = 0; y < SampleRes - 1; y++)
            {
                for (int x = 0; x < SampleRes - 1; x++)
                {
                    bool a = cell[y * SampleRes + x] == region;             // bottom-left
                    bool b = cell[y * SampleRes + x + 1] == region;         // bottom-right
                    bool c = cell[(y + 1) * SampleRes + x + 1] == region;   // top-right
                    bool d = cell[(y + 1) * SampleRes + x] == region;       // top-left
                    int mask = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                    if (mask == 0 || mask == 15) continue;

                    long bottom = ((long)y * SampleRes + x) * 2L;
                    long top = ((long)(y + 1) * SampleRes + x) * 2L;
                    long left = ((long)y * SampleRes + x) * 2L + 1L;
                    long right = ((long)y * SampleRes + x + 1) * 2L + 1L;

                    switch (mask)
                    {
                        case 1: case 14: AddSegment(left, bottom); break;
                        case 2: case 13: AddSegment(bottom, right); break;
                        case 3: case 12: AddSegment(left, right); break;
                        case 4: case 11: AddSegment(right, top); break;
                        case 6: case 9: AddSegment(bottom, top); break;
                        case 7: case 8: AddSegment(left, top); break;
                        case 5: AddSegment(left, top); AddSegment(bottom, right); break;
                        case 10: AddSegment(left, bottom); AddSegment(top, right); break;
                    }
                }
            }

            // Walk the links into chains. Interior edge keys have exactly two
            // neighbours (the ambiguous cases above are split so degree never
            // exceeds two), but a chain that reaches the sample-grid rim ends
            // in a degree-ONE key: an OPEN chain. Treating those as closed —
            // the first version did — draws a straight chord from one loose
            // end to the other, straight across the territory (the "line node
            // in the wrong place" shapes of the 2026-09-03 report). A walk
            // can also START in the middle of an open chain, so after a
            // forward walk dead-ends, the other direction is walked too and
            // prepended; without that the far half became a second partial
            // chain with its own chord.
            var chains = new List<Chain>();
            var visited = new HashSet<long>();

            Vector2 RefineEdge(long key)
            {
                bool vertical = (key & 1L) != 0;
                long idx = key >> 1;
                int ax = (int)(idx % SampleRes);
                int ay = (int)(idx / SampleRes);
                int bx = vertical ? ax : ax + 1;
                int by = vertical ? ay + 1 : ay;
                bool aIn = cell[ay * SampleRes + ax] == region;

                // Five bisection steps: ~1/32 of a sample step (~4 cm).
                float lo = 0f, hi = 1f;
                for (int it = 0; it < 5; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float sx = ax + (bx - ax) * mid;
                    float sy = ay + (by - ay) * mid;
                    float wx = min.x + (sx + 0.5f) * stepX;
                    float wz = min.y + (sy + 0.5f) * stepY;
                    bool inR = RegionMap.RegionAt(wx, wz) == region;
                    if (inR == aIn) lo = mid; else hi = mid;
                }
                float t = (lo + hi) * 0.5f;
                return new Vector2(ax + (bx - ax) * t, ay + (by - ay) * t);
            }

            foreach (var start in links.Keys)
            {
                if (visited.Contains(start)) continue;

                var keys = new List<long>();
                long prev = -1;
                long cur = start;
                bool closed = false;
                while (true)
                {
                    visited.Add(cur);
                    keys.Add(cur);

                    var next = links[cur];
                    long step = -1;
                    for (int i = 0; i < next.Count; i++)
                    {
                        if (next[i] != prev && !visited.Contains(next[i])) { step = next[i]; break; }
                    }
                    if (step < 0)
                    {
                        // Closed iff this last key links back to the start;
                        // otherwise it is a genuine loose end.
                        closed = keys.Count > 2 && cur != start && next.Contains(start);
                        break;
                    }
                    prev = cur;
                    cur = step;
                }

                if (!closed && keys.Count > 0)
                {
                    // Walk the OTHER way from the start and prepend.
                    var back = new List<long>();
                    prev = keys.Count > 1 ? keys[1] : -1;
                    cur = start;
                    while (true)
                    {
                        var next = links[cur];
                        long step = -1;
                        for (int i = 0; i < next.Count; i++)
                        {
                            if (next[i] != prev && !visited.Contains(next[i])) { step = next[i]; break; }
                        }
                        if (step < 0) break;
                        visited.Add(step);
                        back.Add(step);
                        prev = cur;
                        cur = step;
                    }
                    if (back.Count > 0)
                    {
                        back.Reverse();
                        back.AddRange(keys);
                        keys = back;
                    }
                }

                if (keys.Count < 8) continue;
                var pts = new List<Vector2>(keys.Count);
                foreach (var k in keys) pts.Add(RefineEdge(k));
                chains.Add(new Chain { Pts = pts, Closed = closed });
            }

            return chains;
        }

        /// <summary>+1 when the chain's left normal points into the territory,
        /// -1 otherwise — probed against the live partition, majority vote.</summary>
        static float ProbeInwardSign(List<Vector2> pts, int region)
        {
            int votes = 0;
            int n = pts.Count;
            int samples = Mathf.Min(9, n - 2);
            for (int s = 0; s < samples; s++)
            {
                // Interior points only, so the clamped-tangent ends of an
                // open chain never vote.
                int i = 1 + (int)((long)s * (n - 2) / samples);
                Vector2 p = pts[i];
                Vector2 dir = (pts[i + 1] - pts[i - 1]).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x);
                // Probe distance must clear the trace's own error (one ~2 m
                // sample step) or a thin line could vote from the wrong side.
                Vector2 probe = p + normal * 2.5f;
                votes += RegionMap.RegionAt(probe.x, probe.y) == region ? 1 : -1;
            }
            return votes >= 0 ? 1f : -1f;
        }

        /// <summary>One Chaikin corner-cutting pass. Open chains keep their
        /// endpoints — cutting them would shrink the chain away from the rim
        /// it terminates on.</summary>
        static List<Vector2> Chaikin(List<Vector2> pts, bool closed)
        {
            int n = pts.Count;
            var outPts = new List<Vector2>(n * 2 + 2);
            if (!closed) outPts.Add(pts[0]);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                outPts.Add(Vector2.Lerp(a, b, 0.25f));
                outPts.Add(Vector2.Lerp(a, b, 0.75f));
            }
            if (!closed) outPts.Add(pts[n - 1]);
            return outPts;
        }

        /// <summary>Even-spacing resample; wraps only when closed.</summary>
        static List<Vector2> Resample(List<Vector2> pts, float spacing, bool closed)
        {
            var outPts = new List<Vector2>();
            int n = pts.Count;
            float carried = 0f;
            outPts.Add(pts[0]);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                float len = Vector2.Distance(a, b);
                float t = spacing - carried;
                while (t <= len)
                {
                    outPts.Add(Vector2.Lerp(a, b, t / len));
                    t += spacing;
                }
                carried = (carried + len) % spacing;
            }
            if (closed)
            {
                // The resample can land the last point on top of the first.
                if (outPts.Count > 1
                    && Vector2.Distance(outPts[0], outPts[^1]) < spacing * 0.5f)
                    outPts.RemoveAt(outPts.Count - 1);
            }
            else if (Vector2.Distance(outPts[^1], pts[n - 1]) > spacing * 0.25f)
            {
                // An open chain must actually END where the boundary does.
                outPts.Add(pts[n - 1]);
            }
            return outPts;
        }

        #endregion

        #region Retint (on ownership change only)

        void Retint()
        {
            if (_projectors == null) return;
            for (int i = 0; i < _projectors.Length; i++)
            {
                var p = _projectors[i];
                if (p == null || _textures[i] == null) continue;

                int owner = TerritoryOwnership.OwnerOf(i);

                // Per-territory gate: the ownership Version bumps for ANY
                // change anywhere, and repainting all ~30 textures on every
                // bump would be a hitch. Only the territories whose owner
                // actually changed repaint (typically one or two).
                if (owner == _paintedOwner[i]) continue;
                _paintedOwner[i] = owner;

                // Unclaimed ground draws the thinner dark-gray line — the
                // partition stays readable everywhere without competing with
                // held territories for attention.
                if (owner == TerritoryOwnership.Natural)
                {
                    Paint(i, UnclaimedWidth, NaturalColor);
                }
                else if (owner == TerritoryOwnership.Curse)
                {
                    // Cursed ground draws NO line: its edge is the standing
                    // veil CurseBarrierVfx builds from these same curves
                    // (Art_Direction.md §6.4). The projector sleeps until the
                    // territory changes hands again.
                    p.gameObject.SetActive(false);
                    continue;
                }
                else
                {
                    Paint(i, LineWidth, Tint(FactionColors.Get((Faction)owner)));
                }

                GroundDecals.SetPretinted(p, _textures[i]);
                p.gameObject.SetActive(true);
            }
        }

        static Color Tint(Color c)
        {
            c.a = Alpha;
            return c;
        }

        #endregion
    }
}
