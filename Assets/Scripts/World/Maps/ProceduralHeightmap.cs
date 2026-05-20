// ProceduralHeightmap.cs
//
// Builds a Unity TerrainData heights array for a region-tagged procedural
// map. Slope budgets come from the regions list. Outside any region
// ("wild"), full ridge FBM is allowed; inside regions, noise amplitude is
// scaled down so the cell's local slope respects the region's budget.
//
// Anti-grid: all sampling is in world coords (continuous floats), not cell
// indices. Noise primitives (NoiseUtils) use per-octave rotation + seed
// offsets so the underlying lattice never reads as a grid.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public static class ProceduralHeightmap
    {
        // World metres, NOT normalised — these are the heights ProceduralHeightmap
        // composes. Final output is divided by TerrainData.size.y in Build.
        const float SeaFloorY  = 0.5f;
        const float BeachY     = 3.0f;
        const float PlainY     = 8.0f;
        const float MaxHillY   = 20f;
        // Peak mountain elevation. Bumped to 35m after a previous round of
        // curve-flattening + smoothing left mountains looking like gentle
        // bumps. 35m is still well under the 90m "wall of geometry" the
        // pre-2025-Q4 generator produced, but tall enough that a massif
        // reads clearly above 20m hills.
        const float MaxMtnY    = 35f;

        // Public entry point.
        public static float[,] Build(MapArchetype arch, int seed, MapRegionSet regionSet,
                                     UnityEngine.TerrainData td, float waterPlaneY,
                                     Vector2 worldMin, Vector2 worldMax)
        {
            int res = td.heightmapResolution;
            float maxY = td.size.y;
            float[,] heights = new float[res, res];

            // World step in metres per heightmap cell.
            float worldSizeX = worldMax.x - worldMin.x;
            float worldSizeZ = worldMax.y - worldMin.y;
            float stepX = worldSizeX / (res - 1);
            float stepZ = worldSizeZ / (res - 1);

            // Pre-bake per-cell slope budget (degrees). +∞ = wild.
            // Stored as TAN of the angle so we can compare against |dh/dx|.
            float[,] slopeBudgetTan = BakeSlopeBudgetTan(regionSet, res, worldMin, stepX, stepZ);

            for (int z = 0; z < res; z++)
            {
                float wz = worldMin.y + z * stepZ;
                for (int x = 0; x < res; x++)
                {
                    float wx = worldMin.x + x * stepX;

                    // Land/water macro shape.
                    float land01 = TerrainShape.LandMask(arch, seed, wx, wz, worldMin, worldMax);
                    if (land01 < 0.001f)
                    {
                        // Open water — sit on the sea floor with slight noise.
                        float bedNoise = NoiseUtils.Fbm(wx, wz, 3, 2.0f, 0.5f, 0.04f, seed ^ 0xBED);
                        heights[z, x] = (SeaFloorY + 0.4f * bedNoise) / maxY;
                        continue;
                    }

                    // Slope budget at this cell (TAN value). Smaller = tighter.
                    float budgetTan = slopeBudgetTan[z, x];

                    // Base elevation depends on archetype + how far inland.
                    float base_ = BaseElevation(arch, wx, wz, land01, worldMin, worldMax);

                    // Hill / mountain layers, AMPLITUDE-CAPPED to respect budget.
                    // Hills allowed if budgetTan ≥ tan(15°) ≈ 0.268.
                    // Mountains allowed if budgetTan is "wild" (>= 5 ≈ tan(80°)).
                    float h = base_;

                    // Gentle undulation, always applied (within slope budget).
                    float gentleAmp = SlopeCappedAmp(0.4f, budgetTan, stepX, 0.04f); // ±0.4m if possible
                    h += gentleAmp * NoiseUtils.Fbm(wx, wz, 3, 2.0f, 0.5f, 0.04f, seed ^ 0x55);

                    // Hill layer if there's room in the budget.
                    if (budgetTan > 0.18f)
                    {
                        // Domain-warped Perlin so hills aren't grid-aligned.
                        var (hx, hz_) = NoiseUtils.DomainWarp(wx, wz, 25f, 0.02f, seed ^ 0xA15);
                        float hill = NoiseUtils.Fbm(hx, hz_, 4, 2.0f, 0.5f, 0.012f, seed ^ 0x123);
                        float hillAmp = SlopeCappedAmp(MaxHillY - PlainY, budgetTan, stepX, 0.02f);
                        h += hillAmp * Mathf.Max(0f, hill);
                    }

                    // Mountain layer — soft round domes (AoE-style), gated by
                    // an explicit region mask so massifs form discrete clumps
                    // instead of speckling every wild cell. Plain is an open
                    // battlefield archetype with no mountains — only rolling
                    // hills and small ridges.
                    float mountainRegionMask = (arch == MapArchetype.Plain)
                        ? 0f
                        : MountainRegionMaskAt(seed, wx, wz, budgetTan);
                    if (mountainRegionMask > 0.001f)
                    {
                        // Low-freq smooth Fbm produces broad rounded humps
                        // with no sharp ridges. Frequency 0.012 (~85m
                        // wavelength) gives mountains that read as single
                        // hills rather than speckled noise. The shaping
                        // term used to be pow(t, 1.8) which crushed most
                        // cells to low values; pow(t, 1.0) (linear) keeps
                        // domes broad and tall enough to read as mountains.
                        float mtnNoise = NoiseUtils.Fbm(wx, wz, 3, 2.0f, 0.5f, 0.012f, seed ^ 0xA22);
                        float dome = Mathf.Clamp01((mtnNoise + 1f) * 0.5f);
                        // Distance-from-region falloff so mountains don't grow
                        // right next to playable areas. Linear falloff (was
                        // pow(allowance, 1.6)) so mountains rise sooner once
                        // we're past the 20 m safety ring.
                        float dToRegion = DistanceToNearestPlayableRegion(regionSet, new Vector2(wx, wz));
                        float allowance = NoiseUtils.Smoothstep(20f, 50f, dToRegion);
                        h += (MaxMtnY - PlainY) * allowance * dome * mountainRegionMask;
                    }

                    // Beach blend near the coast: ramp from base toward beach.
                    if (land01 < 1f)
                    {
                        float beachWeight = 1f - land01;
                        h = Mathf.Lerp(h, BeachY, beachWeight);
                    }

                    // River channel carve (River archetype only).
                    if (arch == MapArchetype.River)
                    {
                        // The river is exactly where TerrainShape.LandMask is 0.
                        // Inside the channel we already returned sea-bed above.
                        // Banks (small distance from channel) get a slight rise.
                        // Handled implicitly by base + gentle noise.
                    }

                    heights[z, x] = Mathf.Clamp01(h / maxY);
                }
            }

            // Final smoothing pass — averages each cell with its neighbours
            // so the slope-budget transitions between flat regions and wild
            // noise stop reading as perfect carved discs / corridors. A
            // single pass keeps silhouettes (mountain peaks especially)
            // intact; two passes was visibly flattening massifs.
            heights = Smooth(heights, res, passes: 1);

            // Weather-erosion pass — simulated thermal weathering. Material
            // above the talus angle (~32°) at any cell slides toward its
            // lowest neighbour, piling up on lower ground. After ~25 iters
            // this carves the soft shoulders, scree fans, and rounded ridge
            // tops you see on weathered ranges in nature, instead of the
            // pristine "fresh out of the noise function" silhouettes the
            // composer otherwise produces.
            ErodeThermal(heights, res, maxY, stepX, talusDeg: 32f, iterations: 25, strength: 0.5f);
            return heights;
        }

        // Thermal erosion. Each iteration, every cell looks at its 4 cardinal
        // neighbours; if the height delta to the lowest neighbour exceeds the
        // talus height for the cell spacing, half of (delta - talus) × strength
        // is transferred from cell → neighbour. Conserves mass globally so the
        // overall sea/plain levels don't drift, but breaks up unweathered
        // peaks into rounded ridges and produces visible scree fans.
        static void ErodeThermal(float[,] h, int res, float maxY, float stepWorld,
                                 float talusDeg, int iterations, float strength)
        {
            float talusNorm = (Mathf.Tan(talusDeg * Mathf.Deg2Rad) * stepWorld) / Mathf.Max(1e-4f, maxY);
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float c = h[z, x];
                        float maxDelta = 0f;
                        int nx = -1, nz = -1;
                        // 4-neighbour: pick the steepest downhill drop.
                        if (x > 0)       { float d = c - h[z, x - 1]; if (d > maxDelta) { maxDelta = d; nx = x - 1; nz = z; } }
                        if (x < res - 1) { float d = c - h[z, x + 1]; if (d > maxDelta) { maxDelta = d; nx = x + 1; nz = z; } }
                        if (z > 0)       { float d = c - h[z - 1, x]; if (d > maxDelta) { maxDelta = d; nx = x; nz = z - 1; } }
                        if (z < res - 1) { float d = c - h[z + 1, x]; if (d > maxDelta) { maxDelta = d; nx = x; nz = z + 1; } }
                        if (nx < 0 || maxDelta <= talusNorm) continue;
                        float move = (maxDelta - talusNorm) * 0.5f * strength;
                        h[z, x]   = c - move;
                        h[nz, nx] = h[nz, nx] + move;
                    }
                }
            }
        }

        static float[,] Smooth(float[,] src, int res, int passes)
        {
            for (int p = 0; p < passes; p++)
            {
                var dst = new float[res, res];
                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float sum = src[z, x] * 4f;
                        float w = 4f;
                        if (x > 0)       { sum += src[z, x - 1]; w += 1f; }
                        if (x < res - 1) { sum += src[z, x + 1]; w += 1f; }
                        if (z > 0)       { sum += src[z - 1, x]; w += 1f; }
                        if (z < res - 1) { sum += src[z + 1, x]; w += 1f; }
                        if (x > 0 && z > 0)             { sum += src[z - 1, x - 1] * 0.5f; w += 0.5f; }
                        if (x < res - 1 && z > 0)       { sum += src[z - 1, x + 1] * 0.5f; w += 0.5f; }
                        if (x > 0 && z < res - 1)       { sum += src[z + 1, x - 1] * 0.5f; w += 0.5f; }
                        if (x < res - 1 && z < res - 1) { sum += src[z + 1, x + 1] * 0.5f; w += 0.5f; }
                        dst[z, x] = sum / w;
                    }
                }
                src = dst;
            }
            return src;
        }

        // ── slope-budget mask ─────────────────────────────────────────────

        // Build a per-cell slope budget map (as tan(angle)). For each cell,
        // we take MIN over all regions that overlap it. Outside any region:
        // very large (effectively ∞ / "wild").
        static float[,] BakeSlopeBudgetTan(MapRegionSet set, int res, Vector2 worldMin, float stepX, float stepZ)
        {
            const float WildTan = 100f; // ≈ tan(89.4°) — uncapped for practical purposes
            float[,] m = new float[res, res];
            // 1) Init wild.
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    m[z, x] = WildTan;

            // 2) Stamp each region with its slope budget over its footprint,
            //    smoothed by a feather so the budget doesn't have a hard edge
            //    visible as a perfect carved disc / corridor on the map.
            const float FeatherMeters = 28f;
            for (int i = 0; i < set.regions.Count; i++)
            {
                var r = set.regions[i];
                float budgetTan = Mathf.Tan(r.slopeBudgetDeg * Mathf.Deg2Rad);
                BoundsXZ bounds = ComputeRegionBounds(r, paddingMeters: FeatherMeters + 4f);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((bounds.minZ - worldMin.y) / stepZ), 0, res - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((bounds.maxZ - worldMin.y) / stepZ), 0, res - 1);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.minX - worldMin.x) / stepX), 0, res - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.maxX - worldMin.x) / stepX), 0, res - 1);

                for (int z = z0; z <= z1; z++)
                {
                    float wz = worldMin.y + z * stepZ;
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = worldMin.x + x * stepX;
                        float infl = r.Influence(new Vector2(wx, wz), featherMeters: FeatherMeters);
                        if (infl <= 0f) continue;
                        // Blend: wild->budget tan via infl, then take MIN with whatever's there.
                        float blended = Mathf.Lerp(WildTan, budgetTan, infl);
                        if (blended < m[z, x]) m[z, x] = blended;
                    }
                }
            }
            return m;
        }

        struct BoundsXZ { public float minX, maxX, minZ, maxZ; }

        static BoundsXZ ComputeRegionBounds(MapRegion r, float paddingMeters)
        {
            float pad = paddingMeters;
            if (r.polyline != null && r.polyline.Length > 0)
            {
                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                foreach (var p in r.polyline)
                {
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minZ) minZ = p.y; if (p.y > maxZ) maxZ = p.y;
                }
                float half = r.widthMeters * 0.5f + pad;
                return new BoundsXZ { minX = minX - half, maxX = maxX + half, minZ = minZ - half, maxZ = maxZ + half };
            }
            return new BoundsXZ
            {
                minX = r.center.x - r.radiusMeters - pad,
                maxX = r.center.x + r.radiusMeters + pad,
                minZ = r.center.y - r.radiusMeters - pad,
                maxZ = r.center.y + r.radiusMeters + pad,
            };
        }

        // ── base elevation per archetype ─────────────────────────────────

        static float BaseElevation(MapArchetype arch, float wx, float wz, float land01,
                                  Vector2 worldMin, Vector2 worldMax)
        {
            // Plain / Coastal / River: plain elevation on land.
            // Island: gentle rise toward centre.
            // Isthmus: lower at the strip's centre, higher on mainland.
            switch (arch)
            {
                case MapArchetype.Island:
                {
                    Vector2 c = (worldMin + worldMax) * 0.5f;
                    float r = (new Vector2(wx, wz) - c).magnitude;
                    Vector2 size = worldMax - worldMin;
                    float maxR = Mathf.Min(size.x, size.y) * 0.5f;
                    float t = 1f - Mathf.Clamp01(r / maxR);
                    return PlainY + t * 4f;
                }
                case MapArchetype.Isthmus:
                {
                    Vector2 c = (worldMin + worldMax) * 0.5f;
                    float dx = Mathf.Abs(wx - c.x) / ((worldMax.x - worldMin.x) * 0.5f);
                    // 1 at mainlands (dx=1), 0 at centre.
                    return PlainY - 2f * (1f - Mathf.Clamp01(dx));
                }
                default:
                    return PlainY;
            }
        }

        // Compute amplitude cap such that local slope ≤ budgetTan.
        //   |dh/dx| ≈ amp · freq · 2π (for a Perlin at given freq)
        //   So amp ≤ budgetTan / (freq · 2π).
        // We also clamp by `maxDesired` so we never exceed the artist intent.
        static float SlopeCappedAmp(float maxDesired, float budgetTan, float stepWorld, float noiseFrequency)
        {
            float maxBySlope = budgetTan / Mathf.Max(1e-4f, noiseFrequency * Mathf.PI * 2f);
            return Mathf.Min(maxDesired, maxBySlope);
        }

        // Region-mask sample used by both the heightmap composer and external
        // consumers (PassabilityGrid stamps mountain cells as impassable
        // directly off this mask, so the "can't walk on a mountain" rule is
        // enforced by data — not by guessing from local slope).
        public static float MountainRegionMaskAt(int seed, float wx, float wz, float budgetTan)
        {
            if (budgetTan <= 1.5f) return 0f; // not deep-wild, can't be a massif
            float regionNoise = NoiseUtils.Fbm(wx, wz, 3, 2.0f, 0.5f, 0.0035f, seed ^ 0xA00);
            return NoiseUtils.Smoothstep(-0.05f, 0.20f, regionNoise);
        }

        // Variant that doesn't take a budget — for sampling outside the
        // heightmap composer (PassabilityGrid doesn't know the region set's
        // slope budgets). Falls back to "deep wild" so the mask returns its
        // raw value at every world coord.
        public static float MountainRegionMaskAt(int seed, float wx, float wz)
            => MountainRegionMaskAt(seed, wx, wz, 100f);

        /// <summary>
        /// True when the given world XZ would be carved into the impassable
        /// mountain dome by the composer — i.e. the mountain region mask is
        /// above <paramref name="threshold"/> AND the cell is far enough
        /// from every playable region for the composer's distance allowance
        /// to clear zero. Used by PassabilityGrid so the impassable stamp
        /// matches the geometry instead of false-positive-blocking flat
        /// cells inside player areas whose FBM happens to be above zero.
        /// </summary>
        public static bool IsMountainBlocked(int seed, MapRegionSet set, float wx, float wz, float threshold = 0.35f)
        {
            if (set == null) return false;
            // Plain archetype has no mountains; pathing and splat must agree.
            if (set.archetype == MapArchetype.Plain) return false;
            float mask = MountainRegionMaskAt(seed, wx, wz);
            if (mask <= threshold) return false;
            float dToRegion = DistanceToNearestPlayableRegion(set, new Vector2(wx, wz));
            // Mirrors the composer's allowance smoothstep(20, 60, dToRegion).
            // We block only when the composer would actually have raised a
            // mountain here, i.e. the allowance is non-trivial.
            return dToRegion >= 20f;
        }

        // Closest playable-region distance (PlayerStart, Expansion, TravelLane,
        // Resource, CurseSpawn, Chokepoint). Used to keep mountains away.
        static float DistanceToNearestPlayableRegion(MapRegionSet set, Vector2 p)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < set.regions.Count; i++)
            {
                var r = set.regions[i];
                if (r.tag == RegionTag.PlayerStart || r.tag == RegionTag.Expansion ||
                    r.tag == RegionTag.TravelLane  || r.tag == RegionTag.Resource  ||
                    r.tag == RegionTag.CurseSpawn  || r.tag == RegionTag.Chokepoint)
                {
                    float d = r.SignedDistance(p);
                    if (d < best) best = d;
                }
            }
            return Mathf.Max(0f, best);
        }

        // ── connectivity check ────────────────────────────────────────────

        /// <summary>
        /// Walks each TravelLane polyline at ~2m intervals and samples the
        /// local slope. If any sample exceeds the lane's slope budget, the
        /// generation is REJECTED (caller should re-roll the seed).
        /// </summary>
        public static bool VerifyConnectivity(float[,] heights, UnityEngine.TerrainData td,
                                              MapRegionSet set, Vector2 worldMin, Vector2 worldMax,
                                              out string failureReason)
        {
            int res = td.heightmapResolution;
            float maxY = td.size.y;
            Vector2 size = worldMax - worldMin;
            float stepX = size.x / (res - 1);
            float stepZ = size.y / (res - 1);

            foreach (var lane in set.WithTag(RegionTag.TravelLane))
            {
                float budgetTan = Mathf.Tan(lane.slopeBudgetDeg * Mathf.Deg2Rad);
                if (lane.polyline == null) continue;
                for (int i = 0; i < lane.polyline.Length - 1; i++)
                {
                    Vector2 a = lane.polyline[i], b = lane.polyline[i + 1];
                    float dist = Vector2.Distance(a, b);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(dist / 2f));
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        Vector2 p = Vector2.Lerp(a, b, t);
                        float slope = SampleSlopeAt(heights, res, maxY, p, worldMin, stepX, stepZ);
                        if (slope > budgetTan * 1.15f) // 15% tolerance
                        {
                            failureReason = $"TravelLane slope {Mathf.Atan(slope) * Mathf.Rad2Deg:F1}° > {lane.slopeBudgetDeg}° at {p}";
                            return false;
                        }
                    }
                }
            }
            failureReason = null;
            return true;
        }

        static float SampleSlopeAt(float[,] heights, int res, float maxY, Vector2 worldXZ,
                                  Vector2 worldMin, float stepX, float stepZ)
        {
            int xC = Mathf.Clamp(Mathf.RoundToInt((worldXZ.x - worldMin.x) / stepX), 1, res - 2);
            int zC = Mathf.Clamp(Mathf.RoundToInt((worldXZ.y - worldMin.y) / stepZ), 1, res - 2);
            float dxH = (heights[zC, xC + 1] - heights[zC, xC - 1]) * 0.5f * maxY / stepX;
            float dzH = (heights[zC + 1, xC] - heights[zC - 1, xC]) * 0.5f * maxY / stepZ;
            return Mathf.Sqrt(dxH * dxH + dzH * dzH);
        }

        // ── lane-necessity check ──────────────────────────────────────────

        /// <summary>
        /// Walk <paramref name="polyline"/> at 2 m intervals on the supplied
        /// heights array and return true when every sample stays below
        /// <paramref name="laneBudgetDeg"/> of slope. Used by the generator
        /// to decide whether a pair of players needs a carved corridor:
        /// pass a lane-free draft heightmap; "true" means the natural
        /// terrain is already walkable so the lane can be skipped.
        /// </summary>
        public static bool PolylineIsWalkable(float[,] heights, UnityEngine.TerrainData td,
                                              Vector2[] polyline, Vector2 worldMin, Vector2 worldMax,
                                              float laneBudgetDeg)
        {
            if (polyline == null || polyline.Length < 2) return true;
            int res = td.heightmapResolution;
            float maxY = td.size.y;
            Vector2 size = worldMax - worldMin;
            float stepX = size.x / (res - 1);
            float stepZ = size.y / (res - 1);
            float budgetTan = Mathf.Tan(laneBudgetDeg * Mathf.Deg2Rad);

            for (int i = 0; i < polyline.Length - 1; i++)
            {
                Vector2 a = polyline[i], b = polyline[i + 1];
                float dist = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(dist / 2f));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector2 p = Vector2.Lerp(a, b, t);
                    float slope = SampleSlopeAt(heights, res, maxY, p, worldMin, stepX, stepZ);
                    if (slope > budgetTan) return false;
                }
            }
            return true;
        }
    }
}
