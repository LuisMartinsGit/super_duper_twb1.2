// MapRegion.cs
//
// One tagged region inside a procedurally generated map. The macro shape
// (TerrainShape) decides where land is; RegionPlacer drops these regions
// inside the land mask; the heightmap composer respects each region's
// slope budget so playable zones stay flat enough to build on.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public enum RegionTag
    {
        // 25m radius, slope ≤ 5°. Initial Hall + workers.
        PlayerStart,
        // 60-80m radius around PlayerStart, slope ≤ 8°. Build economy here.
        Expansion,
        // Polyline corridor 12-18m wide, slope ≤ 10°. Connects regions.
        TravelLane,
        // 10m radius, slope ≤ 10°. Iron / crystal / supply deposits.
        Resource,
        // 15-25m wide gap, slope ≤ 15°. Narrow tactical passage.
        Chokepoint,
        // 15m radius, slope ≤ 10°. Neutral PvE curse-node site.
        CurseSpawn,
    }

    /// <summary>
    /// A region. Most regions are blob-shaped (center + radius); TravelLane
    /// is the exception — it's a polyline + width.
    /// </summary>
    public struct MapRegion
    {
        public RegionTag tag;
        public Vector2   center;        // world XZ for blob regions
        public float     radiusMeters;  // blob regions (PlayerStart, Expansion, Resource, CurseSpawn, Chokepoint when point-like)
        public Vector2[] polyline;      // TravelLane: ordered XZ vertices
        public float     widthMeters;   // TravelLane / linear Chokepoint corridor width
        public float     slopeBudgetDeg; // max permissible slope inside the region

        // Used by RegionPlacer to remember "which faction slot this start belongs to"
        // and which resource type Resource is (iron / crystal / supplies). 0 = unset.
        public int       payloadIndex;

        public static MapRegion Blob(RegionTag tag, Vector2 center, float radius, float slopeBudgetDeg) =>
            new() { tag = tag, center = center, radiusMeters = radius, slopeBudgetDeg = slopeBudgetDeg };

        public static MapRegion Lane(RegionTag tag, Vector2[] polyline, float width, float slopeBudgetDeg) =>
            new() { tag = tag, polyline = polyline, widthMeters = width, slopeBudgetDeg = slopeBudgetDeg };

        /// <summary>
        /// Distance from <paramref name="p"/> (world XZ) to the region boundary.
        /// Negative when inside. For blob regions: signed distance from a
        /// per-region irregular blob (angular noise on the radius) so the
        /// influence map doesn't read as a perfect carved circle. For lanes:
        /// distance to nearest polyline segment minus half-width.
        /// </summary>
        public float SignedDistance(Vector2 p)
        {
            if (polyline != null && polyline.Length > 1)
            {
                float bestSq = float.MaxValue;
                for (int i = 0; i < polyline.Length - 1; i++)
                {
                    Vector2 a = polyline[i], b = polyline[i + 1];
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    Vector2 closest = a + ab * t;
                    float dSq = (p - closest).sqrMagnitude;
                    if (dSq < bestSq) bestSq = dSq;
                }
                return Mathf.Sqrt(bestSq) - widthMeters * 0.5f;
            }
            Vector2 offset = p - center;
            float dist = offset.magnitude;
            float angle = Mathf.Atan2(offset.y, offset.x);
            float perturb = ShapeWobble(angle, ShapeSeed());
            float effectiveR = radiusMeters * (1f + perturb);
            return dist - effectiveR;
        }

        // Deterministic per-region hash from the centre coordinates. Two
        // regions sharing the same centre (PlayerStart + Expansion) get the
        // same shape seed so they wobble in lockstep — the smaller region's
        // outline is a scaled-down version of the larger one's, which keeps
        // the carved area readable instead of crossing two different blobs.
        int ShapeSeed()
        {
            int sx = unchecked((int)Mathf.Round(center.x * 31.7f));
            int sy = unchecked((int)Mathf.Round(center.y * 27.3f));
            return unchecked(sx * 73856093 ^ sy * 19349663);
        }

        // Multi-frequency angular wobble in roughly [-0.25, 0.25]. Three
        // sinusoids with seeded phases produce a smooth irregular outline —
        // no sharp corners, no perfect circle. The amplitudes are deliberately
        // moderate so a 30m blob still reads as one region, just not a disc.
        static float ShapeWobble(float angle, int seed)
        {
            uint h = unchecked((uint)seed * 2654435761u);
            float phase1 = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
            float phase2 = ((h >> 16) & 0xFFFF) / 65535f * Mathf.PI * 2f;
            float phase3 = (((h * 1140671485u + 12820163u) >> 8) & 0xFFFF) / 65535f * Mathf.PI * 2f;
            return Mathf.Sin(angle * 3f + phase1) * 0.16f
                 + Mathf.Sin(angle * 5f + phase2) * 0.08f
                 + Mathf.Sin(angle * 7f + phase3) * 0.04f;
        }

        /// <summary>0..1 falloff: 1 inside, smoothly drops to 0 over <paramref name="featherMeters"/>.</summary>
        public float Influence(Vector2 p, float featherMeters = 10f)
        {
            float d = SignedDistance(p);
            if (d <= 0f) return 1f;
            if (d >= featherMeters) return 0f;
            float t = d / featherMeters;
            return 1f - (t * t * (3f - 2f * t)); // smoothstep falloff
        }
    }

    public sealed class MapRegionSet
    {
        public List<MapRegion> regions = new();
        public MapArchetype archetype;
        public int seed;
        public Vector2 worldMin;
        public Vector2 worldMax;
        public int rejectRetries; // how many times the generator had to re-roll

        public IEnumerable<MapRegion> WithTag(RegionTag t)
        {
            for (int i = 0; i < regions.Count; i++) if (regions[i].tag == t) yield return regions[i];
        }
    }
}
