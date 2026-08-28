// RegionMap.cs
// The runtime partition: which region does a world position belong to.
//
// Regions are authored as SEEDS (RegionSeedMarker) and the map is partitioned
// by nearest seed — see docs/Design/Regions.md §1 for why seeds rather than
// polygons. That makes the whole query one nearest-point search over a handful
// of candidates, with no geometry to store, no gaps and no overlaps.
//
// This is deliberately just the PARTITION. It knows nothing about who owns a
// region — ownership is read from PlayerInfluenceMap at the seed (Regions.md
// §2) and belongs to whatever system implements the claim rules. Keeping the
// two apart is what lets the lobby draw the same region lines as the match,
// where no influence exists at all.
//
// Static, like PlayerInfluenceMap and BloodMap, because every consumer
// (terrain overlay, minimap, thumbnail baker) needs it from a different layer.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.World.Regions
{
    public static class RegionMap
    {
        /// <summary>No region — off-map, or the map has no seeds authored.</summary>
        public const int None = -1;

        private static Vector2[] _seeds = System.Array.Empty<Vector2>();
        private static string[] _names = System.Array.Empty<string>();

        public static bool Ready => _seeds.Length > 0;
        public static int Count => _seeds.Length;

        /// <summary>Seed positions in world XZ, in registry order — the index
        /// IS the region id, and that ordering is the lockstep-stable one from
        /// MapMarkerRegistry.</summary>
        public static IReadOnlyList<Vector2> Seeds => _seeds;

        public static string NameOf(int region) =>
            region >= 0 && region < _names.Length && !string.IsNullOrEmpty(_names[region])
                ? _names[region]
                : region >= 0 ? $"Region {region}" : "";

        public static Vector2 SeedOf(int region) =>
            region >= 0 && region < _seeds.Length ? _seeds[region] : Vector2.zero;

        /// <summary>
        /// Install a partition. Order matters and must be the caller's stable
        /// order: the region id is the array index, so two peers that built
        /// this differently would disagree about which region is which while
        /// agreeing on every position.
        /// </summary>
        public static void Configure(IReadOnlyList<Vector2> seeds, IReadOnlyList<string> names)
        {
            if (seeds == null || seeds.Count == 0) { Reset(); return; }

            _seeds = new Vector2[seeds.Count];
            _names = new string[seeds.Count];
            for (int i = 0; i < seeds.Count; i++)
            {
                _seeds[i] = seeds[i];
                _names[i] = names != null && i < names.Count ? names[i] : null;
            }
        }

        public static void Reset()
        {
            _seeds = System.Array.Empty<Vector2>();
            _names = System.Array.Empty<string>();
        }

        // ── organic boundaries ──────────────────────────────────────────
        // A raw Voronoi edge is a perpendicular bisector: a mathematically
        // STRAIGHT line. Straight cuts across terrain read as arbitrary, because
        // real borders are not surveyed lines -- they follow ridges, rivers and
        // shorelines and they wander.
        //
        // The fix is DOMAIN WARPING: displace the query point by low-frequency
        // noise BEFORE the nearest-seed search. The partition stays a pure
        // function of position, so it is still watertight -- no gaps, no
        // overlaps, every point in exactly one region -- but the boundaries
        // meander instead of ruling straight across the map.
        //
        // Warping the QUERY rather than the seeds is what keeps it watertight.
        // Perturbing the drawn line instead would let two regions disagree
        // about who owns a sliver along it.

        /// <summary>Wavelength of the wander, in metres. Roughly the size of
        /// one "bend" in a border. Lengthened with the amplitude drop below:
        /// fewer, longer bends read as terrain, more of them reads as noise.</summary>
        private const float WarpScale = 150f;

        /// <summary>How far the boundary can stray from the straight bisector,
        /// in metres. Too high and regions grow tendrils into each other.
        ///
        /// 42 -> 14 (2026-08-28). At 42 m against a 110 m wavelength the
        /// boundary doubled back on itself hard enough to read as a zig-zag
        /// rather than a wander — the gradient of the displacement approached
        /// the spacing of the bends, which is where domain warping stops
        /// looking like a meandering river and starts looking like a mistake.
        /// A shallower displacement over a longer wavelength keeps the "not a
        /// surveyed line" intent with a boundary you can actually follow.</summary>
        private const float WarpAmplitude = 14f;

        /// <summary>
        /// Displace a sample point by smooth noise. Deterministic
        /// (Mathf.PerlinNoise is), and it MUST be applied identically by every
        /// consumer -- which is why the terrain mask, the minimap and the
        /// thumbnail all go through this class instead of each rolling its own
        /// nearest-seed loop. Three views disagreeing about where a border runs
        /// would be worse than straight lines.
        /// </summary>
        private static void Warp(ref float x, ref float z)
        {
            float u = x / WarpScale;
            float v = z / WarpScale;
            // +1000 keeps the sample coordinates positive: Mathf.PerlinNoise
            // mirrors about zero, which would make one quadrant of a
            // centre-origin map a reflection of its neighbour.
            float nx = Mathf.PerlinNoise(u + 1000f, v + 1000f) - 0.5f;
            float nz = Mathf.PerlinNoise(u + 1731f, v + 1517f) - 0.5f;
            x += nx * 2f * WarpAmplitude;
            z += nz * 2f * WarpAmplitude;
        }

        // ── unclaimable ground ──────────────────────────────────────────
        // Mountains, cliffs, lakes and the map rim belong to NO region. They are
        // scenery, not territory: nobody can stand on them, build on them or
        // contest them, so shading them as part of a holding is a lie about what
        // owning a region means (Regions.md §1 -- "a region's claimable value is
        // its passable area only").
        //
        // It is also what makes the borders read: excluded ground pushes the
        // drawn boundary to hug the foot of a mountain and the shore of a lake
        // instead of ruling straight over them, so regions end up divided by the
        // terrain that actually divides them.
        //
        // Tested by HEIGHT rather than by PassabilityGrid because this has to
        // give the same answer in the editor -- the thumbnail baker draws the
        // same partition with no match world and no passability grid in
        // existence. These are PassabilityGrid's own thresholds.
        private const float WaterHeight = 4f;
        private const float MountainHeight = 24f;

        /// <summary>
        /// True when ground at this position can belong to a region at all.
        /// Forests are deliberately still claimable: they are impassable, but
        /// they are nature that takes on its owner's look
        /// (Territory_And_Nature.md), so they belong to whoever holds them.
        /// </summary>
        public static bool IsClaimable(float worldX, float worldZ)
        {
            float y = Terrain.TerrainUtility.GetHeight(worldX, worldZ);
            return y > WaterHeight && y < MountainHeight;
        }

        /// <summary>
        /// Nearest region ignoring claimability — always a valid index once the
        /// map has seeds. Use this where you want the region a point would fall
        /// in even if nothing can stand there (the Age 0 home-region reveal
        /// wants the mountain inside your own ground revealed, not a hole).
        /// </summary>
        public static int NearestRegion(float worldX, float worldZ)
        {
            if (_seeds.Length == 0) return None;
            Warp(ref worldX, ref worldZ);

            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < _seeds.Length; i++)
            {
                float dx = worldX - _seeds[i].x;
                float dz = worldZ - _seeds[i].y;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The region OWNING a world XZ position, or <see cref="None"/> when the
        /// map has no regions or the ground is unclaimable. Nearest (warped)
        /// seed wins; ties break to the lower index so the partition is
        /// deterministic on a boundary.
        /// </summary>
        public static int RegionAt(float worldX, float worldZ)
        {
            if (_seeds.Length == 0) return None;
            if (!IsClaimable(worldX, worldZ)) return None;
            Warp(ref worldX, ref worldZ);

            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < _seeds.Length; i++)
            {
                float dx = worldX - _seeds[i].x;
                float dz = worldZ - _seeds[i].y;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// How close a position is to a region BOUNDARY, as a 0..1 value that
        /// rises to 1 exactly on the line.
        ///
        /// A Voronoi edge is where the two nearest seeds are equidistant, so
        /// the distance between the best and second-best squared distances is
        /// the natural edge signal — no neighbour sampling, no resolution
        /// dependence, and it thins correctly at any zoom because the falloff
        /// is expressed in metres.
        /// </summary>
        /// <param name="widthMetres">Half-width of the drawn line.</param>
        public static float EdgeStrengthAt(float worldX, float worldZ, float widthMetres)
        {
            if (_seeds.Length < 2 || widthMetres <= 0f) return 0f;
            // No border is drawn across ground that belongs to nobody, so the
            // lattice stops at the shoreline and the foot of the cliffs.
            if (!IsClaimable(worldX, worldZ)) return 0f;
            Warp(ref worldX, ref worldZ);   // same displacement as RegionAt

            float d0 = float.MaxValue, d1 = float.MaxValue;
            for (int i = 0; i < _seeds.Length; i++)
            {
                float dx = worldX - _seeds[i].x;
                float dz = worldZ - _seeds[i].y;
                float d = dx * dx + dz * dz;
                if (d < d0) { d1 = d0; d0 = d; }
                else if (d < d1) { d1 = d; }
            }
            if (d1 == float.MaxValue) return 0f;

            // sqrt only twice, and only here: the gap between the two nearest
            // distances is ~2x the perpendicular distance to the bisector.
            float gap = (Mathf.Sqrt(d1) - Mathf.Sqrt(d0)) * 0.5f;
            return Mathf.Clamp01(1f - gap / widthMetres);
        }

        /// <summary>
        /// Build the partition from the scene's RegionSeedMarkers. Safe to call
        /// on a map with none — the partition simply stays empty and every
        /// consumer draws nothing.
        /// </summary>
        public static void BuildFromMarkers()
        {
            var markers = MapMarkers.MapMarkerRegistry.RegionSeeds;
            if (markers.Count == 0)
            {
                Reset();
                Debug.LogWarning("[RegionMap] No RegionSeedMarker in the scene — this map has no " +
                                 "regions. See docs/Design/Regions.md; run " +
                                 "Waning Border > Maps > Seed Regions For Open Scene.");
                return;
            }

            var seeds = new List<Vector2>(markers.Count);
            var names = new List<string>(markers.Count);
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                var p = m.WorldPosition;
                seeds.Add(new Vector2(p.x, p.z));
                names.Add(m.RegionName);
            }

            Configure(seeds, names);
            TWBLog.Log($"[RegionMap] {seeds.Count} region(s) built from markers.");
        }
    }
}
