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
//   _TWB_RoadDir   RG direction  the road's unit tangent, −1..1 → 0..1;
//                                0.5,0.5 = none (plaza / off-road) — what
//                                the Age 1 bricks align to
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
        public NativeArray<Color32> Mask;
        public NativeArray<Color32> Dir;
        public NativeArray<float> Nearest;
        [ReadOnly] public NativeArray<DiscCmd> Discs;
        [ReadOnly] public NativeArray<PolyCmd> Polys;
        [ReadOnly] public NativeArray<float2> Points;

        public void Execute()
        {
            int n = Res * Res;
            var blank = new Color32(0, 0, 0, 128);
            var noDir = new Color32(128, 128, 0, 255);
            for (int i = 0; i < n; i++) { Mask[i] = blank; Dir[i] = noDir; Nearest[i] = float.PositiveInfinity; }

            for (int d = 0; d < Discs.Length; d++) DrawDisc(Discs[d]);
            for (int p = 0; p < Polys.Length; p++) DrawPolyline(Polys[p]);
        }

        /// <summary>A plaza: a disc whose rim is warped by low-frequency
        /// noise (two to three lobes, seeded per site) and whose coverage
        /// fades from FadeStart × radius out to the rim — never a hard circle.</summary>
        void DrawDisc(in DiscCmd c)
        {
            if (c.Strength <= 0f) return;
            float rMax = c.Radius * (1f + c.EdgeNoise);
            int x0 = math.max(0, (int)math.floor(c.Centre.x - rMax - 1));
            int x1 = math.min(Res - 1, (int)math.ceil(c.Centre.x + rMax + 1));
            int y0 = math.max(0, (int)math.floor(c.Centre.y - rMax - 1));
            int y1 = math.min(Res - 1, (int)math.ceil(c.Centre.y + rMax + 1));
            float sx = (c.Seed & 0xFFFF) * 0.37f, sy = ((c.Seed >> 8) & 0xFFFF) * 0.53f;
            for (int y = y0; y <= y1; y++)
            {
                float dy = (y + 0.5f) - c.Centre.y;
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

        /// <summary>A stroked polyline: coverage full to EdgeFade × halfWidth
        /// and fading to the rim; each texel also records its signed lateral
        /// offset and the tangent of the nearest segment.</summary>
        void DrawPolyline(in PolyCmd c)
        {
            if (c.Strength <= 0f || c.Count < 2) return;
            float r1 = c.HalfWidth, r0 = c.HalfWidth * math.saturate(c.EdgeFade);
            float claimScale = 1f / math.max(c.Strength, 0.05f);
            for (int i = c.Start; i < c.Start + c.Count - 1; i++)
            {
                float2 a = Points[i], b = Points[i + 1];
                float2 ab = b - a;
                float len2 = math.lengthsq(ab);
                if (len2 < 1e-6f) continue;
                float2 tangent = ab * math.rsqrt(len2);
                byte tx = (byte)math.round((tangent.x * 0.5f + 0.5f) * 255f);
                byte ty = (byte)math.round((tangent.y * 0.5f + 0.5f) * 255f);

                int x0 = math.max(0, (int)math.floor(math.min(a.x, b.x) - r1 - 1));
                int x1 = math.min(Res - 1, (int)math.ceil(math.max(a.x, b.x) + r1 + 1));
                int y0 = math.max(0, (int)math.floor(math.min(a.y, b.y) - r1 - 1));
                int y1 = math.min(Res - 1, (int)math.ceil(math.max(a.y, b.y) + r1 + 1));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float2 p = new float2(x + 0.5f, y + 0.5f);
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
                            Dir[idx] = new Color32(tx, ty, 0, 255);
                        }
                    }
                }
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
