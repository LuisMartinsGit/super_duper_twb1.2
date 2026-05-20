// NoiseUtils.cs
//
// Procedural-noise primitives used by the procedural map system. All
// functions are pure: same (x, z, seed) → same output. No UnityEngine.Random
// anywhere in the seeded path.
//
// Anti-grid measures (the player's explicit concern):
//   • Per-octave rotation — each octave samples after rotating coords by
//     30°·i so different octaves' lattices don't align.
//   • Per-octave seed offset — XOR with octave index so each octave samples
//     a different lattice origin.
//   • Domain warp — offset sample coords by another noise field.
//   • Worley / Voronoi available as a non-grid-aligned primitive.

using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public static class NoiseUtils
    {
        // ── basic primitives ──────────────────────────────────────────────

        /// <summary>Perlin remapped to [-1, 1].</summary>
        public static float Perlin11(float x, float z) => Mathf.PerlinNoise(x, z) * 2f - 1f;

        /// <summary>
        /// Stable hash-derived per-axis offset so a given seed picks a
        /// consistent lattice origin without colliding across octaves.
        /// </summary>
        public static Vector2 SeedOffset(int seed)
        {
            uint u = unchecked((uint)seed * 2654435761u);
            float ox = (u & 0xFFFF) * 0.013f;
            float oz = ((u >> 16) & 0xFFFF) * 0.017f;
            return new Vector2(ox, oz);
        }

        // ── fBm (smooth fractal noise) ────────────────────────────────────

        /// <summary>
        /// Classic Perlin FBM in [-1, 1]. Anti-grid: per-octave rotation
        /// (30°·i) + per-octave seed offset (i × 17.31).
        /// </summary>
        public static float Fbm(float x, float z, int octaves, float lacunarity, float gain,
                                float baseFrequency, int seed)
        {
            float sum = 0f, amp = 1f, freq = baseFrequency, weightSum = 0f;
            Vector2 off = SeedOffset(seed);
            for (int i = 0; i < octaves; i++)
            {
                float theta = i * Mathf.PI / 6f;          // 30° steps
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                float rx = (x * c - z * s) * freq + off.x + i * 17.31f;
                float rz = (x * s + z * c) * freq + off.y + i * 11.97f;
                sum += amp * (Mathf.PerlinNoise(rx, rz) * 2f - 1f);
                weightSum += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return weightSum > 0f ? sum / weightSum : 0f;
        }

        // ── Ridge FBM (mountains, V-shaped erosion) ───────────────────────

        /// <summary>
        /// Ridge noise FBM in [0, 1]. Per the spec: ridge = 1 − |2n − 1|,
        /// squared, summed over octaves. Sharp creases — what Perlin alone
        /// cannot produce. Anti-grid: per-octave rotation + seed offset.
        /// </summary>
        public static float RidgeFbm(float x, float z, int octaves, float lacunarity, float gain,
                                     float baseFrequency, int seed)
        {
            float sum = 0f, amp = 1f, freq = baseFrequency, weightSum = 0f;
            Vector2 off = SeedOffset(seed);
            for (int i = 0; i < octaves; i++)
            {
                float theta = i * Mathf.PI / 5f;          // 36° steps — distinct from FBM's 30°
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                float rx = (x * c - z * s) * freq + off.x + i * 23.13f;
                float rz = (x * s + z * c) * freq + off.y + i * 29.71f;
                float n = Mathf.PerlinNoise(rx, rz);
                float r = 1f - Mathf.Abs(n * 2f - 1f);
                r *= r;          // squaring sharpens the crease
                sum += r * amp;
                weightSum += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return weightSum > 0f ? sum / weightSum : 0f;
        }

        // ── Domain warp ───────────────────────────────────────────────────

        /// <summary>
        /// Offset (x, z) by a noise field. Used to break radial symmetry.
        /// Returns warped coords in the same world units as the input.
        /// </summary>
        public static (float, float) DomainWarp(float x, float z, float strengthMeters,
                                                float warpFrequency, int seed)
        {
            if (strengthMeters <= 0f) return (x, z);
            Vector2 off = SeedOffset(seed);
            float dx = (Mathf.PerlinNoise(x * warpFrequency + off.x,        z * warpFrequency + off.y)        - 0.5f) * 2f * strengthMeters;
            float dz = (Mathf.PerlinNoise(x * warpFrequency + off.x + 53.2f, z * warpFrequency + off.y + 91.7f) - 0.5f) * 2f * strengthMeters;
            return (x + dx, z + dz);
        }

        /// <summary>
        /// Anisotropic warp: stretch perpendicular to <paramref name="angleRad"/>,
        /// compress along it. Produces ridges that branch off a main axis.
        /// </summary>
        public static (float, float) AnisotropicWarp(float x, float z, float strengthMeters,
                                                     float angleRad, float anisotropy,
                                                     float warpFrequency, int seed)
        {
            if (strengthMeters <= 0f) return (x, z);
            Vector2 off = SeedOffset(seed);
            float dxN = (Mathf.PerlinNoise(x * warpFrequency + off.x,        z * warpFrequency + off.y)        - 0.5f) * 2f * strengthMeters;
            float dzN = (Mathf.PerlinNoise(x * warpFrequency + off.x + 53.2f, z * warpFrequency + off.y + 91.7f) - 0.5f) * 2f * strengthMeters;
            float c = Mathf.Cos(angleRad), s = Mathf.Sin(angleRad);
            float along =  dxN * c + dzN * s;
            float perp  = -dxN * s + dzN * c;
            along *= (1f - 0.7f * anisotropy);
            perp  *= (1f + 1.4f * anisotropy);
            float fx = along * c - perp * s;
            float fz = along * s + perp * c;
            return (x + fx, z + fz);
        }

        // ── Worley / Voronoi (organic cell shapes, no axial bias) ────────

        /// <summary>
        /// Returns Worley F1 distance in [0, 1] (approx). F1 = distance to
        /// nearest cell point. Use this for cell-shaped masks / cracks.
        /// </summary>
        public static float WorleyF1(float x, float z, float cellSize, int seed)
        {
            x /= cellSize; z /= cellSize;
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            float bestSq = 8f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cz = iz + dz;
                Vector2 p = CellPoint(cx, cz, seed);
                float ex = (dx + p.x) - fx;
                float ez = (dz + p.y) - fz;
                float d = ex * ex + ez * ez;
                if (d < bestSq) bestSq = d;
            }
            return Mathf.Sqrt(bestSq);
        }

        /// <summary>F2 − F1 difference (good for crack/edge highlights).</summary>
        public static float WorleyEdge(float x, float z, float cellSize, int seed)
        {
            x /= cellSize; z /= cellSize;
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            float bestSq = 8f, secondSq = 8f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cz = iz + dz;
                Vector2 p = CellPoint(cx, cz, seed);
                float ex = (dx + p.x) - fx;
                float ez = (dz + p.y) - fz;
                float d = ex * ex + ez * ez;
                if (d < bestSq) { secondSq = bestSq; bestSq = d; }
                else if (d < secondSq) { secondSq = d; }
            }
            return Mathf.Sqrt(secondSq) - Mathf.Sqrt(bestSq);
        }

        static Vector2 CellPoint(int cx, int cz, int seed)
        {
            uint h = unchecked((uint)(cx * 374761393 + cz * 668265263 + seed * 2147483647));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            float jx = (h & 0xFFFF) / 65535f;
            float jz = ((h >> 16) & 0xFFFF) / 65535f;
            return new Vector2(jx, jz);
        }

        // ── helpers ───────────────────────────────────────────────────────

        public static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-6f, b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Sub-pixel jitter used to perturb classification samples.</summary>
        public static (float, float) JitterSample(float x, float z, float strengthMeters, int seed)
        {
            if (strengthMeters <= 0f) return (x, z);
            Vector2 off = SeedOffset(seed);
            float jx = (Mathf.PerlinNoise(x * 0.012f + off.x,        z * 0.012f + off.y)        - 0.5f) * 2f * strengthMeters;
            float jz = (Mathf.PerlinNoise(x * 0.012f + off.x + 37.4f, z * 0.012f + off.y + 91.3f) - 0.5f) * 2f * strengthMeters;
            return (x + jx, z + jz);
        }
    }
}
