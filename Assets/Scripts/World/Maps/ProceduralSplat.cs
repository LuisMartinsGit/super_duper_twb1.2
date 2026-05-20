// ProceduralSplat.cs
//
// Composes the terrain alphamap from the heightmap + slope + macro
// land/water mask. Layer indices match the Build call in ProceduralMapGen
// (kept stable so the texture array doesn't need a separate lookup).
//
// Layer weights are computed at world coords with sub-pixel jitter so the
// transition curves between layers don't snap to the alphamap grid.

using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public static class ProceduralSplat
    {
        // Layer index contract — match ProceduralMapGen.BuildLayers().
        public const int L_SEA   = 0;
        public const int L_SAND  = 1;
        public const int L_GRASS = 2;
        public const int L_FOREST = 3;
        public const int L_DIRT  = 4;
        public const int L_ROCK  = 5;
        public const int L_SNOW  = 6;
        public const int LayerCount = 7;

        public static float[,,] Build(MapArchetype arch, int seed, UnityEngine.TerrainData td,
                                      Vector2 worldMin, Vector2 worldMax, float waterPlaneY)
        {
            int alphaRes = td.alphamapResolution;
            int hmRes = td.heightmapResolution;
            float maxY = td.size.y;
            var heights = td.GetHeights(0, 0, hmRes, hmRes);
            int layers = td.alphamapLayers;
            var splat = new float[alphaRes, alphaRes, layers];

            Vector2 size = worldMax - worldMin;
            float stepHmX = size.x / (hmRes - 1);
            float stepHmZ = size.y / (hmRes - 1);

            // Heightmap was rescaled in 2025-Q4 (PlainY=8, hills cap ~20m,
            // mountain peaks ~30m). The old splat thresholds (Rock at 47°+,
            // Snow at 45-60m) targeted a 90m-tall heightmap and so produced
            // a single grass colour everywhere — no rock, no snow. Layer
            // selection now consults the SAME mountain-region mask the
            // composer used so rock paints exactly where the dome lives.
            for (int z = 0; z < alphaRes; z++)
            {
                float fz = (z + 0.5f) / alphaRes;
                float wz = worldMin.y + fz * size.y;
                for (int x = 0; x < alphaRes; x++)
                {
                    float fx = (x + 0.5f) / alphaRes;
                    float wx = worldMin.x + fx * size.x;

                    // Sample height + slope at this point. Both use bilinear
                    // heightmap interpolation and a 1.5 m slope baseline so
                    // the values match what PassabilityGrid sees — the
                    // earlier nearest-vertex + heightmap-step (~0.24 m) slope
                    // gave very different magnitudes and shifted rock paint
                    // away from the impassable cells.
                    const float slopeStep = 1.5f;
                    float h  = SampleHeightBilinear(heights, hmRes, maxY, worldMin, stepHmX, stepHmZ, wx, wz);
                    float hL = SampleHeightBilinear(heights, hmRes, maxY, worldMin, stepHmX, stepHmZ, wx - slopeStep, wz);
                    float hR = SampleHeightBilinear(heights, hmRes, maxY, worldMin, stepHmX, stepHmZ, wx + slopeStep, wz);
                    float hD = SampleHeightBilinear(heights, hmRes, maxY, worldMin, stepHmX, stepHmZ, wx, wz - slopeStep);
                    float hU = SampleHeightBilinear(heights, hmRes, maxY, worldMin, stepHmX, stepHmZ, wx, wz + slopeStep);
                    float dxH = (hR - hL) / (slopeStep * 2f);
                    float dzH = (hU - hD) / (slopeStep * 2f);
                    float slope = Mathf.Sqrt(dxH * dxH + dzH * dzH); // tan(angle)

                    // Mountain mask at this cell (same FBM the heightmap
                    // composer used). Drives rock + snow placement so the
                    // paint matches the geometry exactly. Gated by region
                    // distance so the rock paint doesn't appear inside flat
                    // player areas where the bare FBM happens to be high.
                    // Plain archetype has no mountains at all.
                    float mtnMask = (arch == MapArchetype.Plain)
                        ? 0f
                        : ProceduralHeightmap.MountainRegionMaskAt(seed, wx, wz);
                    if (mtnMask > 0f && ProceduralMapGen.Current != null)
                    {
                        // Mirrors the composer's distance allowance — fades
                        // the mask to zero within 20 m of any playable region.
                        bool nearPlayable = !ProceduralHeightmap.IsMountainBlocked(
                            seed, ProceduralMapGen.Current, wx, wz, 0.05f);
                        if (nearPlayable) mtnMask = 0f;
                    }

                    // Per-layer weights. Sum normalised to 1.

                    // Water / beach — only near and below the water plane.
                    float wSea = h < waterPlaneY ? 1f : 0f;
                    float wSand = (1f - wSea)
                                * NoiseUtils.Smoothstep(waterPlaneY, waterPlaneY + 1.5f, h)
                                * (1f - NoiseUtils.Smoothstep(waterPlaneY + 1.5f, waterPlaneY + 4f, h));

                    // Rock — paints where pathing would refuse the cell.
                    // Thresholds align with PassabilityGrid:
                    //   • mtnMask side mirrors MountainMaskThreshold=0.35.
                    //   • slope side mirrors MaxWalkableSlope=0.55.
                    // Tight smoothstep bands keep the texture from popping
                    // hard at the impassable/passable boundary but still
                    // match the gizmo's impassable cells almost exactly.
                    float wRock = Mathf.Max(
                        NoiseUtils.Smoothstep(0.30f, 0.40f, mtnMask),
                        NoiseUtils.Smoothstep(0.50f, 0.60f, slope));

                    // Snow — only on the mountain tops. Mask > 0.7 AND height
                    // near the peak of the dome (>20m world). Drops the bias
                    // against snow on small massifs.
                    float wSnow = NoiseUtils.Smoothstep(0.70f, 0.95f, mtnMask)
                                * NoiseUtils.Smoothstep(20f, 26f, h);

                    // Dirt — moderate slopes (hill flanks, mountain skirts).
                    float wDirt = NoiseUtils.Smoothstep(0.25f, 0.55f, slope) * (1f - wRock);

                    // Grass + forest take whatever's left on flat low ground.
                    float flat = Mathf.Clamp01(1f - wSea - wSand - wRock - wDirt - wSnow);
                    float forestMask = NoiseUtils.Fbm(wx * 0.02f, wz * 0.02f, 3, 2.0f, 0.5f, 1f, seed ^ 0xF03);
                    float wForest = flat * NoiseUtils.Smoothstep(0.05f, 0.30f, forestMask);
                    wForest *= PlayerAreaForestSuppression(wx, wz);
                    float wGrass = flat - wForest;

                    // Normalise.
                    float total = wSea + wSand + wGrass + wForest + wDirt + wRock + wSnow;
                    if (total < 1e-4f) { wGrass = 1f; total = 1f; }
                    float inv = 1f / total;
                    if (L_SEA    < layers) splat[z, x, L_SEA]    = wSea    * inv;
                    if (L_SAND   < layers) splat[z, x, L_SAND]   = wSand   * inv;
                    if (L_GRASS  < layers) splat[z, x, L_GRASS]  = wGrass  * inv;
                    if (L_FOREST < layers) splat[z, x, L_FOREST] = wForest * inv;
                    if (L_DIRT   < layers) splat[z, x, L_DIRT]   = wDirt   * inv;
                    if (L_ROCK   < layers) splat[z, x, L_ROCK]   = wRock   * inv;
                    if (L_SNOW   < layers) splat[z, x, L_SNOW]   = wSnow   * inv;
                }
            }
            return splat;
        }

        // Trees paint where L_FOREST > threshold (see ProceduralTerrain.
        // PaintTreesOnBrownGround), so suppressing this layer inside player
        // regions is what keeps trunks out of the player's build space.
        // Buffer extends the no-forest zone 22.5% past each region's wobbled
        // edge — midpoint of the "20-25% larger" tuning ask — with a short
        // soft fade so the splat boundary doesn't read as a hard ring.
        static float PlayerAreaForestSuppression(float wx, float wz)
        {
            var set = ProceduralMapGen.Current;
            if (set == null) return 1f;
            const float BufferFraction = 0.225f;
            const float FeatherMeters = 3f;
            var p = new Vector2(wx, wz);
            float minFactor = 1f;
            for (int i = 0; i < set.regions.Count; i++)
            {
                var r = set.regions[i];
                if (r.tag != RegionTag.PlayerStart && r.tag != RegionTag.Expansion) continue;
                float buffer = r.radiusMeters * BufferFraction;
                float d = r.SignedDistance(p);
                float factor;
                if (d <= buffer) factor = 0f;
                else if (d >= buffer + FeatherMeters) factor = 1f;
                else
                {
                    float t = (d - buffer) / FeatherMeters;
                    factor = t * t * (3f - 2f * t);
                }
                if (factor < minFactor) minFactor = factor;
                if (minFactor <= 0f) return 0f;
            }
            return minFactor;
        }

        // Bilinear heightmap sample at world (wx, wz). Matches what
        // Unity's terrain renderer (and TerrainUtility.GetHeight via
        // Terrain.SampleHeight) interpolate, so splat slope/height
        // queries line up with PassabilityGrid's slope/height queries.
        static float SampleHeightBilinear(float[,] heights, int hmRes, float maxY,
                                          Vector2 worldMin, float stepX, float stepZ,
                                          float wx, float wz)
        {
            float u = (wx - worldMin.x) / stepX;
            float v = (wz - worldMin.y) / stepZ;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, hmRes - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, hmRes - 2);
            float tx = Mathf.Clamp01(u - x0);
            float tz = Mathf.Clamp01(v - z0);
            float h00 = heights[z0,     x0];
            float h10 = heights[z0,     x0 + 1];
            float h01 = heights[z0 + 1, x0];
            float h11 = heights[z0 + 1, x0 + 1];
            float hx0 = Mathf.Lerp(h00, h10, tx);
            float hx1 = Mathf.Lerp(h01, h11, tx);
            return Mathf.Lerp(hx0, hx1, tz) * maxY;
        }
    }
}
