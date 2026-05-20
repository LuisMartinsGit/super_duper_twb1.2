// TerrainShape.cs
//
// Per-archetype macro shape function. Returns 0..1 (water .. land) at any
// world XZ. Continuous — no integer cell lookup. Land mask is consumed by:
//   • RegionPlacer (only places PlayerStart / Resource inside land)
//   • ProceduralHeightmap (water cells get sea-bed elevation; land gets
//     archetype-specific base elevation + noise)
//   • ProceduralSplat (water tint vs. beach vs. grass)

using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public static class TerrainShape
    {
        /// <summary>
        /// Land mask in [0, 1] at world (wx, wz). 0 = open water, 1 = solid
        /// land. The transition zone (~10 m wide) reads as beach.
        /// </summary>
        public static float LandMask(MapArchetype arch, int seed, float wx, float wz, Vector2 worldMin, Vector2 worldMax)
        {
            Vector2 size = worldMax - worldMin;
            // Normalised coords in [-1, 1] centred on the map.
            float nx = (wx - worldMin.x) / size.x * 2f - 1f;
            float nz = (wz - worldMin.y) / size.y * 2f - 1f;
            // Half-map dimensions in metres (for absolute distances).
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;

            switch (arch)
            {
                case MapArchetype.Plain:       return PlainMask(nx, nz, halfX, halfZ);
                case MapArchetype.Coastal:     return CoastalMask(nx, nz, halfX, halfZ, seed);
                case MapArchetype.River:       return RiverMask(nx, nz, halfX, halfZ, seed);
                case MapArchetype.Island:      return IslandMask(nx, nz, halfX, halfZ, seed);
                case MapArchetype.Isthmus:     return IsthmusMask(nx, nz, halfX, halfZ, seed);
                default:                       return 1f;
            }
        }

        // ── Plain ─────────────────────────────────────────────────────────
        // Solid land everywhere — the playable rectangle goes wall-to-wall.
        // Earlier versions had a thin smoothstep falloff at the edges, which
        // dropped border cells below water level and looked like a moat
        // around an otherwise inland map.
        static float PlainMask(float nx, float nz, float halfX, float halfZ) => 1f;

        // ── Coastal ───────────────────────────────────────────────────────
        // Coastline running along the X axis at some per-seed Z. Land is
        // the side toward +Z. Coastline is sine + noise so it's not a
        // straight line.
        static float CoastalMask(float nx, float nz, float halfX, float halfZ, int seed)
        {
            // World-space sine wave for the coastline.
            float wx = nx * halfX;
            float wz = nz * halfZ;
            // Per-seed offset of the coastline. Coastline sits roughly at
            // z = -0.2 × halfZ (water below, land above).
            Vector2 off = NoiseUtils.SeedOffset(seed);
            float coastZ = -0.2f * halfZ
                         + 20f * Mathf.Sin(wx * 0.025f + off.x)
                         + 30f * NoiseUtils.Perlin11(wx * 0.012f, off.y);
            float dz = wz - coastZ;            // positive = land side
            return NoiseUtils.Smoothstep(-6f, 10f, dz);
        }

        // ── River ─────────────────────────────────────────────────────────
        // Land everywhere except a meandering river channel running
        // roughly X-axis. River centreline is sine + noise; river width
        // 8 m + per-position jitter.
        static float RiverMask(float nx, float nz, float halfX, float halfZ, int seed)
        {
            float wx = nx * halfX;
            float wz = nz * halfZ;
            Vector2 off = NoiseUtils.SeedOffset(seed ^ 0x1357);
            float riverZ = 25f * Mathf.Sin(wx * 0.015f + off.x)
                         + 35f * NoiseUtils.Perlin11(wx * 0.008f + 11.3f, off.y + 21.7f);
            float bank = Mathf.Abs(wz - riverZ);
            float riverHalf = 6f + 2f * NoiseUtils.Perlin11(wx * 0.03f, 0f);
            // 1 well outside the channel, 0 in the centre.
            return NoiseUtils.Smoothstep(riverHalf, riverHalf + 4f, bank);
        }

        // ── Island ────────────────────────────────────────────────────────
        // Roughly elliptical landmass centred at origin, perturbed by noise
        // so the coastline is irregular.
        static float IslandMask(float nx, float nz, float halfX, float halfZ, int seed)
        {
            float wx = nx * halfX;
            float wz = nz * halfZ;
            // Distance from centre normalised against per-axis radius. <1 is
            // inside the bare ellipse, >1 is outside.
            float rx = 0.78f * halfX;
            float rz = 0.72f * halfZ;
            float r = Mathf.Sqrt((wx / rx) * (wx / rx) + (wz / rz) * (wz / rz));
            // Coastline jitter: ±0.18 normalised over low-freq noise.
            float jit = NoiseUtils.Fbm(wx, wz, 4, 2.0f, 0.5f, 0.012f, seed) * 0.18f;
            float threshold = 1f + jit;
            return NoiseUtils.Smoothstep(threshold + 0.05f, threshold - 0.05f, r);
        }

        // ── Isthmus ───────────────────────────────────────────────────────
        // Two large blobs at opposite map ends connected by a narrow strip
        // along the X axis at Z ≈ 0.
        static float IsthmusMask(float nx, float nz, float halfX, float halfZ, int seed)
        {
            float wx = nx * halfX;
            float wz = nz * halfZ;
            // Two circular blobs.
            Vector2 cA = new(-0.55f * halfX, 0f);
            Vector2 cB = new( 0.55f * halfX, 0f);
            float rA = 0.42f * halfX;
            float rB = 0.42f * halfX;
            Vector2 p = new(wx, wz);
            float blobJit = NoiseUtils.Fbm(wx, wz, 4, 2.0f, 0.5f, 0.012f, seed) * 0.15f;
            float dA = (p - cA).magnitude - rA * (1f + blobJit);
            float dB = (p - cB).magnitude - rB * (1f + blobJit);
            // Strip: width varies along X — narrow at the centre.
            float stripCentreZ = 10f * Mathf.Sin(wx * 0.02f + NoiseUtils.SeedOffset(seed).x);
            float stripHalfWidth = Mathf.Lerp(28f, 12f, Mathf.Abs(wx) / (0.55f * halfX));
            float stripDist = Mathf.Abs(wz - stripCentreZ) - stripHalfWidth;
            // Min-of-three SDFs = union.
            float sdf = Mathf.Min(dA, Mathf.Min(dB, stripDist));
            return NoiseUtils.Smoothstep(6f, -6f, sdf);
        }
    }
}
