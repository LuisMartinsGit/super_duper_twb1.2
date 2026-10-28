// RoadRaster.cs
// Curves and discs → the two road textures (docs/Design/Roads.md §7), as a
// BURST JOB writing straight into the textures' native buffers:
//
//   _TWB_RoadMask  R  coverage   plaza + road, soft-edged so the shader's
//                                noise erosion has a gradient to bite
//                  G  finished   1 where the network here is complete
//                  B  plaza      1 on a plaza disc, 0 on a road
//                  A  lateral    signed distance across the road, −1..1
//                                mapped to 0..1 (0.5 = centreline) — what
//                                the wheel ruts are drawn from
//   _TWB_RoadDir   RG along      distance ALONG the nearest road as a phase,
//                                sin / cos of 2π·along / AlongPeriod → 0..1
//                                (so bilinear filtering never wraps)
//                  B  across     signed distance across it, metres, over
//                                ±AcrossRange → 0..1 (0.5 = centreline)
//                  A  on-road    255 where a road wrote the texel, 0 else
//                                — the Age 1 bricks are laid on (along,
//                                across), the road's own arc-length frame
//
// Why a frame and not a tangent (2026-10-28): the shader used to rotate the
// WORLD position into the road's tangent frame. That rotation is about the
// world origin, so wherever the tangent changed between texels (a bend, or
// the filter blending two segments' tangents) the UV slid by distance-from-
// origin times the angle change — metres of smear a few hundred metres out.
// Arc length along the road and signed distance across it are the road's
// own coordinates; they never depend on where the map's origin is.
//
// Coverage is MAX-combined so overlapping shapes merge. Lateral and
// direction belong to whichever road is NEAREST at that texel (a per-texel
// claim buffer settles overlaps, weighted by strength so a fading road does
// not steal them from a live one). Everything is in mask texels;
// RoadNetwork maps world ↔ texel and fills the command lists.
//
// Every shape carries a STRENGTH (0..1) that scales its coverage. The
// shader erodes coverage through noise, so a shape at low strength is not
// a faint ghost of itself but a shape eaten from the edges inward: a road
// being built wears in from its centreline, a road whose building is gone
// is reclaimed by the grass from its rims (Roads.md §6).
//
// Why a job: the first version was managed C# with Mathf calls per texel —
// ~80–100 ms per raster on a full base, and the slow reclaims re-raster at
// 2 Hz, which read as a permanent stutter (2026-09-18 Perf.log: 1,088
// frame spikes in nine minutes, all in Update). Burst brings the same work
// to a few milliseconds.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.World.Roads
{
    public struct DiscCmd
    {
        public float2 Centre;      // texels
        public float Radius;       // texels
        public float EdgeNoise;
        public float FadeStart;
        public int Seed;
        public byte Finished;
        public byte Plaza;
        public float Strength;
    }

    /// <summary>A culture PAD: an axis-aligned, hard-edged square of full
    /// coverage, marked plaza (and finished when its building is) — so the
    /// shader's per-pixel culture rule paves it (Roads.md §4b).</summary>
    public struct SquareCmd
    {
        public float2 Min;         // texels
        public float2 Max;         // texels
        public byte Finished;
        public float Strength;
    }

    public struct PolyCmd
    {
        public int Start;          // index into Points
        public int Count;
        public float HalfWidth;    // texels
        public float EdgeFade;
        public byte Finished;
        public float Strength;
    }

    [BurstCompile]
    public struct RoadRasterJob : IJob
    {
        public int Res;
        /// <summary>How many x-texels one y-texel spans: the map's z extent
        /// over its x extent (1 on a square map). The mask is Res x Res over
        /// the map's rectangle, so on a rectangular map (Yiel Lymwérra,
        /// 256 x 128, 2026-10-17) a texel is not square; discs and strokes
        /// measure distance with y scaled by this, in x-texel units, so a
        /// plaza stays round and a road keeps its width in every direction.
        /// Radius and HalfWidth are in x-texels.</summary>
        public float YScale;
        /// <summary>World metres per x-texel: the along / across frame is
        /// written in metres so the shader can tile it directly.</summary>
        public float MetresPerTexel;
        /// <summary>Metres per turn of the along phase (RoadNetworkConfig
        /// .pavingAlongPeriod).</summary>
        public float AlongPeriod;
        /// <summary>Half-range, metres, of the across channel
        /// (RoadNetworkConfig.pavingAcrossRange).</summary>
        public float AcrossRange;
        public NativeArray<Color32> Mask;
        public NativeArray<Color32> Dir;
        public NativeArray<float> Nearest;
        [ReadOnly] public NativeArray<DiscCmd> Discs;
        [ReadOnly] public NativeArray<SquareCmd> Squares;
        [ReadOnly] public NativeArray<PolyCmd> Polys;
        [ReadOnly] public NativeArray<float2> Points;

        public void Execute()
        {
            int n = Res * Res;
            var blank = new Color32(0, 0, 0, 128);
            var noDir = new Color32(128, 128, 128, 0);
            for (int i = 0; i < n; i++) { Mask[i] = blank; Dir[i] = noDir; Nearest[i] = float.PositiveInfinity; }

            for (int d = 0; d < Discs.Length; d++) DrawDisc(Discs[d]);
            for (int s = 0; s < Squares.Length; s++) DrawSquare(Squares[s]);
            for (int p = 0; p < Polys.Length; p++) DrawPolyline(Polys[p]);
        }

        /// <summary>A plaza: a disc whose rim is warped by low-frequency
        /// noise (two to three lobes, seeded per site) and whose coverage
        /// fades from FadeStart × radius out to the rim — never a hard circle.</summary>
        void DrawDisc(in DiscCmd c)
        {
            if (c.Strength <= 0f) return;
            float rMax = c.Radius * (1f + c.EdgeNoise);
            float rMaxY = rMax / YScale;
            int x0 = math.max(0, (int)math.floor(c.Centre.x - rMax - 1));
            int x1 = math.min(Res - 1, (int)math.ceil(c.Centre.x + rMax + 1));
            int y0 = math.max(0, (int)math.floor(c.Centre.y - rMaxY - 1));
            int y1 = math.min(Res - 1, (int)math.ceil(c.Centre.y + rMaxY + 1));
            float sx = (c.Seed & 0xFFFF) * 0.37f, sy = ((c.Seed >> 8) & 0xFFFF) * 0.53f;
            for (int y = y0; y <= y1; y++)
            {
                float dy = ((y + 0.5f) - c.Centre.y) * YScale;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f) - c.Centre.x;
                    float dist = math.sqrt(dx * dx + dy * dy);
                    if (dist > rMax) continue;
                    float ang = math.atan2(dy, dx);
                    float nz = noise.cnoise(new float2(sx + math.cos(ang) * 1.3f, sy + math.sin(ang) * 1.3f));
                    float r = c.Radius * (1f + c.EdgeNoise * nz);
                    if (dist > r) continue;
                    float cov = 1f - math.smoothstep(c.FadeStart * r, r, dist);
                    WriteCoverage(y * Res + x, cov * c.Strength, c.Finished, c.Plaza);
                }
            }
        }

        /// <summary>A culture pad: full coverage inside the square, a
        /// one-texel antialiased edge — deliberately NOT the plaza's lobed,
        /// fading rim: the pad is laid stone, cut square.</summary>
        void DrawSquare(in SquareCmd c)
        {
            if (c.Strength <= 0f) return;
            int x0 = math.max(0, (int)math.floor(c.Min.x));
            int x1 = math.min(Res - 1, (int)math.ceil(c.Max.x));
            int y0 = math.max(0, (int)math.floor(c.Min.y));
            int y1 = math.min(Res - 1, (int)math.ceil(c.Max.y));
            for (int y = y0; y <= y1; y++)
            {
                float cy = math.saturate(math.min((y + 1f) - c.Min.y, c.Max.y - y));
                for (int x = x0; x <= x1; x++)
                {
                    float cx = math.saturate(math.min((x + 1f) - c.Min.x, c.Max.x - x));
                    float cov = cx * cy;
                    if (cov <= 0f) continue;
                    WriteCoverage(y * Res + x, cov * c.Strength, c.Finished, 1);
                }
            }
        }

        /// <summary>A stroked polyline: coverage full to EdgeFade × halfWidth
        /// and fading to the rim; each texel also records its signed lateral
        /// offset and, in the direction mask, its arc length along the
        /// nearest road and signed distance across it, in metres.</summary>
        void DrawPolyline(in PolyCmd c)
        {
            if (c.Strength <= 0f || c.Count < 2) return;
            float r1 = c.HalfWidth, r0 = c.HalfWidth * math.saturate(c.EdgeFade);
            float claimScale = 1f / math.max(c.Strength, 0.05f);
            float2 iso = new float2(1f, YScale);      // texel -> x-texel units
            float phasePerTexel = MetresPerTexel * (2f * math.PI) / math.max(AlongPeriod, 0.01f);
            float acrossScale = MetresPerTexel / math.max(AcrossRange, 0.01f);
            float cum = 0f;                           // arc length so far, x-texels
            for (int i = c.Start; i < c.Start + c.Count - 1; i++)
            {
                float2 a = Points[i] * iso, b = Points[i + 1] * iso;
                float2 ab = b - a;
                float len2 = math.lengthsq(ab);
                if (len2 < 1e-6f) continue;
                float len = math.sqrt(len2);
                float2 tangent = ab / len;

                int x0 = math.max(0, (int)math.floor(math.min(a.x, b.x) - r1 - 1));
                int x1 = math.min(Res - 1, (int)math.ceil(math.max(a.x, b.x) + r1 + 1));
                int y0 = math.max(0, (int)math.floor((math.min(a.y, b.y) - r1) / YScale - 1));
                int y1 = math.min(Res - 1, (int)math.ceil((math.max(a.y, b.y) + r1) / YScale + 1));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float2 p = new float2(x + 0.5f, (y + 0.5f) * YScale);
                        float t = math.saturate(math.dot(p - a, ab) / len2);
                        float2 rel = p - (a + ab * t);
                        float d = math.length(rel);
                        if (d > r1) continue;
                        int idx = y * Res + x;
                        float cov = 1f - math.smoothstep(r0, r1, d);
                        WriteCoverage(idx, cov * c.Strength, c.Finished, 0);

                        float claim = d * claimScale;
                        if (claim < Nearest[idx])
                        {
                            Nearest[idx] = claim;
                            float side = tangent.x * rel.y - tangent.y * rel.x;
                            float lateral = math.clamp(side / math.max(c.HalfWidth, 1e-3f), -1f, 1f);
                            var q = Mask[idx];
                            q.a = (byte)math.round((lateral * 0.5f + 0.5f) * 255f);
                            Mask[idx] = q;

                            // The road's own frame: arc length along the
                            // polyline (as a phase, so the texture filter
                            // blends neighbours instead of wrapping) and
                            // signed metres across it.
                            float phase = (cum + t * len) * phasePerTexel;
                            math.sincos(phase, out float sn, out float cs);
                            float across = math.clamp(side * acrossScale, -1f, 1f);
                            Dir[idx] = new Color32(
                                (byte)math.round((sn * 0.5f + 0.5f) * 255f),
                                (byte)math.round((cs * 0.5f + 0.5f) * 255f),
                                (byte)math.round((across * 0.5f + 0.5f) * 255f),
                                255);
                        }
                    }
                }
                cum += len;
            }
        }

        void WriteCoverage(int i, float coverage, byte finished, byte plaza)
        {
            byte c = (byte)math.round(math.saturate(coverage) * 255f);
            var p = Mask[i];
            if (c > p.r) p.r = c;
            // "finished" and "plaza" mark the texel wherever the finished /
            // plaza shape covers it at all, so a finished road's stone reaches
            // its own soft edge instead of stopping at the core.
            if (finished != 0 && c > 0) p.g = 255;
            if (plaza != 0 && c > 0) p.b = 255;
            Mask[i] = p;
        }
    }
}
