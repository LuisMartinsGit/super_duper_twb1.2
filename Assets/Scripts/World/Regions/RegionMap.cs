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
        // Authored outlines, parallel to _seeds. A null entry means that
        // region is still a Voronoi cell.
        private static Vector2[][] _shapes = System.Array.Empty<Vector2[]>();
        // Axis-aligned bounds of each outline, parallel to _shapes (unused
        // for a null shape). Every polygon query rejects on these first: a
        // point outside a polygon's box cannot be inside it, and cannot be
        // nearer to its outline than to the box. Lockstep-exact — see
        // InsideBounded / BoxDistance for why the rejects never change an
        // answer.
        private static Vector2[] _shapeMin = System.Array.Empty<Vector2>();
        private static Vector2[] _shapeMax = System.Array.Empty<Vector2>();
        /// <summary>Slack on the x reject, metres: far above float rounding
        /// of the ray-crossing lerp at map coordinates, far below anything
        /// that matters.</summary>
        private const float BoxSlack = 0.01f;
        private static bool _anyShape;
        // Every region has an outline: the polygons ARE the partition. Ground
        // inside none of them is no region at all (see RawRegionAt) — the
        // Voronoi fallback only serves maps that are partly or wholly
        // unauthored.
        private static bool _allShaped;

        /// <summary>
        /// Bumped whenever the partition is installed or dropped. Everything
        /// that BAKES the partition into pixels or curves (the terrain mask,
        /// the border ribbon, the minimap lattice) gates its rebuild on this,
        /// not on <see cref="Ready"/>: Ready stayed true across matches, so a
        /// second map in one session drew the first map's borders until
        /// something else happened to invalidate it (2026-09-11).
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>
        /// Two authored outlines that were meant to share an edge rarely do
        /// exactly — the editor snaps vertices to the 2 m build grid, but a
        /// neighbour's polyline is stitched from different vertices, leaving
        /// slivers a few metres wide between them. Ground inside no polygon
        /// but within this distance of one belongs to the nearest outline;
        /// beyond it (a map corner the author left out) it is no region.
        /// </summary>
        private const float SliverTolerance = 4f;
        // Parallel to _seeds. Null when the map predates region kinds, in
        // which case claimability falls back to terrain height.
        private static MapMarkers.RegionSeedMarker.RegionKind[] _kinds
            = System.Array.Empty<MapMarkers.RegionSeedMarker.RegionKind>();
        private static bool _anyKind;

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

        /// <summary>The authored outline for a region, or null if it has none.</summary>
        /// <summary>What a region IS. Normal when the map authored nothing.</summary>
        public static MapMarkers.RegionSeedMarker.RegionKind KindOf(int region) =>
            region >= 0 && region < _kinds.Length
                ? _kinds[region]
                : MapMarkers.RegionSeedMarker.RegionKind.Normal;

        /// <summary>True when at least one region states its kind.</summary>
        public static bool HasAuthoredKinds => _anyKind;

        /// <summary>Water, mountain and obstacle regions hold nothing and block.</summary>
        public static bool KindBlocks(MapMarkers.RegionSeedMarker.RegionKind kind) =>
            kind == MapMarkers.RegionSeedMarker.RegionKind.Water
            || kind == MapMarkers.RegionSeedMarker.RegionKind.Mountain
            || kind == MapMarkers.RegionSeedMarker.RegionKind.Obstacle;

        public static Vector2[] ShapeOf(int region) =>
            region >= 0 && region < _shapes.Length ? _shapes[region] : null;

        /// <summary>True when at least one region is authored rather than Voronoi.</summary>
        public static bool HasAuthoredShapes => _anyShape;

        public static Vector2 SeedOf(int region) =>
            region >= 0 && region < _seeds.Length ? _seeds[region] : Vector2.zero;

        /// <summary>
        /// Install a partition. Order matters and must be the caller's stable
        /// order: the region id is the array index, so two peers that built
        /// this differently would disagree about which region is which while
        /// agreeing on every position.
        /// </summary>
        public static void Configure(IReadOnlyList<Vector2> seeds, IReadOnlyList<string> names,
                                     IReadOnlyList<Vector2[]> shapes = null,
                                     IReadOnlyList<MapMarkers.RegionSeedMarker.RegionKind> kinds = null)
        {
            if (seeds == null || seeds.Count == 0) { Reset(); return; }

            _seeds = new Vector2[seeds.Count];
            _names = new string[seeds.Count];
            _shapes = new Vector2[seeds.Count][];
            _shapeMin = new Vector2[seeds.Count];
            _shapeMax = new Vector2[seeds.Count];
            _anyShape = false;
            _allShaped = true;
            Version++;
            _kinds = new MapMarkers.RegionSeedMarker.RegionKind[seeds.Count];
            _anyKind = kinds != null && kinds.Count > 0;
            for (int i = 0; i < seeds.Count; i++)
            {
                _seeds[i] = seeds[i];
                _names[i] = names != null && i < names.Count ? names[i] : null;

                var s = shapes != null && i < shapes.Count ? shapes[i] : null;
                // Fewer than three points is not a polygon; treat it as unauthored
                // rather than as a degenerate shape nothing can be inside of.
                _shapes[i] = s != null && s.Length >= 3 ? s : null;
                if (_shapes[i] != null)
                {
                    _anyShape = true;
                    Vector2 mn = _shapes[i][0], mx = _shapes[i][0];
                    for (int k = 1; k < _shapes[i].Length; k++)
                    {
                        mn = Vector2.Min(mn, _shapes[i][k]);
                        mx = Vector2.Max(mx, _shapes[i][k]);
                    }
                    _shapeMin[i] = mn; _shapeMax[i] = mx;
                }
                else _allShaped = false;

                _kinds[i] = kinds != null && i < kinds.Count
                    ? kinds[i]
                    : MapMarkers.RegionSeedMarker.RegionKind.Normal;
            }
        }

        /// <summary>Winding-agnostic point-in-polygon (ray crossing).</summary>
        private static bool Inside(Vector2[] poly, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > z) == (poly[j].y > z)) continue;
                float t = (z - poly[i].y) / (poly[j].y - poly[i].y);
                if (x < poly[i].x + t * (poly[j].x - poly[i].x)) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// <see cref="Inside"/> behind the outline's bounding box. Exact:
        /// with z outside [minY, maxY) no edge straddles the ray, so Inside
        /// finds no crossing; with x right of every vertex no crossing test
        /// passes; with x left of every vertex EVERY straddling edge crosses,
        /// and a closed loop always has an even number of those. The x
        /// rejects keep <see cref="BoxSlack"/> clear of the vertices so the
        /// lerp's rounding cannot matter.
        /// </summary>
        private static bool InsideBounded(int i, float x, float z)
        {
            Vector2 mn = _shapeMin[i], mx = _shapeMax[i];
            if (z < mn.y || z >= mx.y) return false;
            if (x > mx.x + BoxSlack || x < mn.x - BoxSlack) return false;
            return Inside(_shapes[i], x, z);
        }

        /// <summary>Distance from a point to outline i's bounding box — a
        /// lower bound on its distance to any of that outline's segments.</summary>
        private static float BoxDistance(int i, float x, float z)
        {
            Vector2 mn = _shapeMin[i], mx = _shapeMax[i];
            float dx = x < mn.x ? mn.x - x : (x > mx.x ? x - mx.x : 0f);
            float dz = z < mn.y ? mn.y - z : (z > mx.y ? z - mx.y : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static void Reset()
        {
            _seeds = System.Array.Empty<Vector2>();
            _names = System.Array.Empty<string>();
            _shapes = System.Array.Empty<Vector2[]>();
            _shapeMin = System.Array.Empty<Vector2>();
            _shapeMax = System.Array.Empty<Vector2>();
            _anyShape = false;
            _allShaped = false;
            Version++;
            _kinds = System.Array.Empty<MapMarkers.RegionSeedMarker.RegionKind>();
            _anyKind = false;
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
        /// <summary>
        /// Whether ground can be held and built on.
        ///
        /// THE REGION'S KIND DECIDES. Water, mountain and obstacle regions hold
        /// nothing, and the terrain under them is GENERATED from that fact
        /// rather than the other way round. Height is only the fallback for a
        /// map authored before region kinds, where the heightmap is still the
        /// only statement about what is lake and what is peak.
        /// </summary>
        public static bool IsClaimable(float worldX, float worldZ)
        {
            if (_anyKind)
            {
                int r = RawRegionAt(worldX, worldZ);
                return r != None && !KindBlocks(KindOf(r));
            }

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
            // Authored outlines first, exactly as RegionAt: a Hall's claim and
            // the home reveal must file under the polygon the author drew,
            // not under the warped Voronoi cell that polygon replaced.
            if (_anyShape)
            {
                int r = RawRegionAt(worldX, worldZ);
                if (r != None) return r;
            }
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
            if (_anyKind)
            {
                // ONE partition lookup. This used to go through IsClaimable,
                // which runs RawRegionAt to read the region's kind, and then
                // run RawRegionAt again for the answer — on an authored map
                // that is every polygon edge walked twice per call, and the
                // 128² territory rasterize makes 16k calls (2026-09-16).
                int r = RawRegionAt(worldX, worldZ);
                return r != None && !KindBlocks(KindOf(r)) ? r : None;
            }
            return IsClaimable(worldX, worldZ) ? RawRegionAt(worldX, worldZ) : None;
        }

        /// <summary>
        /// The region a point falls in, WITHOUT the claimability filter.
        ///
        /// Split out because claimability now reads the region's KIND, and
        /// RegionAt filtering on claimability would then call back into itself.
        /// This is also the honest answer for anything that wants the region a
        /// point is in even though nothing can stand there — a lake still
        /// belongs to the lake region.
        /// </summary>
        public static int RawRegionAt(float worldX, float worldZ)
        {
            if (_seeds.Length == 0) return None;

            // An authored outline IS the territory. Checked before the warp:
            // the noise displacement exists to make Voronoi edges organic,
            // and applying it to a hand-drawn border would move the border
            // off the line the author drew.
            if (_anyShape)
            {
                for (int i = 0; i < _shapes.Length; i++)
                    if (_shapes[i] != null && InsideBounded(i, worldX, worldZ))
                        return i;

                // Inside no outline. On a fully authored map the outlines ARE
                // the borders: a sliver between two neighbours heals to the
                // nearer one, and anything further out is no region — the
                // warped Voronoi used to partition it, drawing seams nobody
                // authored across ~40% of Hollow Table (2026-09-11).
                if (_allShaped) return NearestOutlineWithin(worldX, worldZ, SliverTolerance);
            }

            Warp(ref worldX, ref worldZ);

            int best = None;
            float bestD = float.MaxValue;
            for (int i = 0; i < _seeds.Length; i++)
            {
                // On a half-authored map only the UNAUTHORED regions are still
                // Voronoi cells; an authored one ends at its outline.
                if (_anyShape && _shapes[i] != null) continue;
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

            // An AUTHORED outline is a real line, so its edge signal is the
            // distance to that line — not the Voronoi bisector, which no longer
            // describes where the border is. Unauthored regions still answer
            // with the bisector below, so a half-authored map draws both.
            float authored = 0f;
            if (_anyShape)
            {
                float best = float.MaxValue;
                for (int i = 0; i < _shapes.Length; i++)
                {
                    var poly = _shapes[i];
                    if (poly == null) continue;
                    if (best < float.MaxValue && BoxDistance(i, worldX, worldZ) > best + BoxSlack) continue;
                    for (int a = 0, bIdx = poly.Length - 1; a < poly.Length; bIdx = a++)
                    {
                        float d = DistanceToSegment(worldX, worldZ, poly[bIdx], poly[a]);
                        if (d < best) best = d;
                    }
                }
                if (best < float.MaxValue)
                    authored = Mathf.Clamp01(1f - best / widthMetres);
                if (authored >= 1f) return 1f;
                // The bisector is only a border where the partition is still
                // Voronoi: never on a fully authored map, and never inside an
                // authored outline on a mixed one. This used to be max()ed in
                // unconditionally, so every raster of the lattice (terrain
                // mask, minimap, thumbnail) painted a phantom seed-Voronoi
                // over the polygons that the border ribbon — which traces the
                // partition itself — never showed (2026-09-11).
                if (_allShaped) return authored;
                for (int i = 0; i < _shapes.Length; i++)
                    if (_shapes[i] != null && InsideBounded(i, worldX, worldZ))
                        return authored;
            }

            Warp(ref worldX, ref worldZ);   // same displacement as RegionAt

            float d0 = float.MaxValue, d1 = float.MaxValue;
            for (int i = 0; i < _seeds.Length; i++)
            {
                if (_anyShape && _shapes[i] != null) continue;   // not a Voronoi cell
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
            return Mathf.Max(authored, Mathf.Clamp01(1f - gap / widthMetres));
        }

        /// <summary>The region whose outline passes within
        /// <paramref name="tolerance"/> metres of the point, nearest first;
        /// <see cref="None"/> when no outline is that close.</summary>
        private static int NearestOutlineWithin(float x, float z, float tolerance)
        {
            int best = None;
            float bestD = tolerance;
            for (int i = 0; i < _shapes.Length; i++)
            {
                var poly = _shapes[i];
                if (poly == null) continue;
                // No segment of this outline can beat bestD (strict <) if its
                // box is already further; the slack absorbs float rounding.
                if (BoxDistance(i, x, z) > bestD + BoxSlack) continue;
                for (int a = 0, bIdx = poly.Length - 1; a < poly.Length; bIdx = a++)
                {
                    float d = DistanceToSegment(x, z, poly[bIdx], poly[a]);
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        /// <summary>Perpendicular distance from a point to a segment, in metres.</summary>
        private static float DistanceToSegment(float px, float pz, Vector2 a, Vector2 b)
        {
            float abx = b.x - a.x, abz = b.y - a.y;
            float len2 = abx * abx + abz * abz;
            if (len2 <= 1e-6f) return Mathf.Sqrt((px - a.x) * (px - a.x) + (pz - a.y) * (pz - a.y));

            float t = Mathf.Clamp01(((px - a.x) * abx + (pz - a.y) * abz) / len2);
            float cx = a.x + abx * t, cz = a.y + abz * t;
            return Mathf.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz));
        }

        // ── adjacency ───────────────────────────────────────────────────
        // Two regions are ADJACENT when they share a border (Regions.md §2,
        // the Hall adjacency rule; §3, the curse's conquest). This is the one
        // definition: the curse's expansion and a player's Hall placement used
        // to be two different ideas of "next door" — the curse walked the
        // seed-to-seed segment, the AI measured seed distance — and neither
        // was the border the player sees.
        //
        // Computed by rasterising the PARTITION ITSELF (RawRegionAt, the same
        // lookup every border view draws) on a coarse grid over the map and
        // marking every pair of different regions found in orthogonally
        // neighbouring cells. So an authored outline is adjacent to exactly
        // the outlines it touches, a Voronoi cell to exactly the cells across
        // its warped edge, and two regions separated by a Mountain or Water
        // REGION are not adjacent to each other (each is adjacent to the
        // mountain). Ground that is no region (a gap wider than the sliver
        // tolerance) joins nothing.
        //
        // LOCKSTEP-SAFE: a pure function of the installed partition — the
        // grid bounds come from the seeds and outlines, never from the
        // Terrain object (which a peer may not have finished loading), and
        // the cells are visited in a fixed order. Cached per Version, so it
        // is built once per map, lazily, on the first question.

        /// <summary>Longest side of the adjacency raster, in cells.</summary>
        private const int AdjacencyMaxCells = 160;
        /// <summary>Finest adjacency cell, metres — the build grid's 2 m.</summary>
        private const float AdjacencyMinCell = 2f;
        /// <summary>Margin around the seed/outline bounds, metres, so the
        /// outer edge regions are sampled past their seeds.</summary>
        private const float AdjacencyMargin = 24f;

        private static bool[] _adjacency;          // [a * Count + b], symmetric
        private static int _adjacencyVersion = int.MinValue;

        /// <summary>
        /// True when regions <paramref name="a"/> and <paramref name="b"/>
        /// share a border. A region is not adjacent to itself; invalid ids are
        /// adjacent to nothing.
        /// </summary>
        public static bool AreAdjacent(int a, int b)
        {
            int n = _seeds.Length;
            if (a < 0 || b < 0 || a >= n || b >= n || a == b) return false;
            EnsureAdjacency();
            return _adjacency[a * n + b];
        }

        private static void EnsureAdjacency()
        {
            int n = _seeds.Length;
            if (_adjacency != null && _adjacencyVersion == Version && _adjacency.Length == n * n)
                return;
            _adjacency = new bool[n * n];
            _adjacencyVersion = Version;
            if (n < 2) return;

            // Bounds: every seed and every outline vertex, plus a margin.
            Vector2 mn = _seeds[0], mx = _seeds[0];
            for (int i = 0; i < n; i++)
            {
                mn = Vector2.Min(mn, _seeds[i]);
                mx = Vector2.Max(mx, _seeds[i]);
                if (_shapes.Length > i && _shapes[i] != null)
                {
                    mn = Vector2.Min(mn, _shapeMin[i]);
                    mx = Vector2.Max(mx, _shapeMax[i]);
                }
            }
            mn -= new Vector2(AdjacencyMargin, AdjacencyMargin);
            mx += new Vector2(AdjacencyMargin, AdjacencyMargin);

            float extent = Mathf.Max(mx.x - mn.x, mx.y - mn.y);
            float cell = Mathf.Max(AdjacencyMinCell, extent / AdjacencyMaxCells);
            int w = Mathf.Max(1, Mathf.CeilToInt((mx.x - mn.x) / cell));
            int h = Mathf.Max(1, Mathf.CeilToInt((mx.y - mn.y) / cell));

            // Two rows are enough: each row is compared with itself (left /
            // right) and with the row before it (up / down).
            var prev = new int[w];
            var cur = new int[w];
            for (int z = 0; z < h; z++)
            {
                float wz = mn.y + (z + 0.5f) * cell;
                for (int x = 0; x < w; x++)
                {
                    float wx = mn.x + (x + 0.5f) * cell;
                    int r = RawRegionAt(wx, wz);
                    cur[x] = r;
                    if (x > 0) Mark(cur[x - 1], r, n);
                    if (z > 0) Mark(prev[x], r, n);
                }
                var t = prev; prev = cur; cur = t;
            }
        }

        private static void Mark(int a, int b, int n)
        {
            if (a == b || a < 0 || b < 0 || a >= n || b >= n) return;
            _adjacency[a * n + b] = true;
            _adjacency[b * n + a] = true;
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
            var shapes = new List<Vector2[]>(markers.Count);
            var kinds = new List<MapMarkers.RegionSeedMarker.RegionKind>(markers.Count);
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                var p = m.WorldPosition;
                seeds.Add(new Vector2(p.x, p.z));
                names.Add(m.RegionName);
                shapes.Add(m.Shape);
                kinds.Add(m.Kind);
            }

            Configure(seeds, names, shapes, kinds);
            TWBLog.Log($"[RegionMap] {seeds.Count} region(s) built from markers.");
        }
    }
}
