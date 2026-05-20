// RegionPlacer.cs
//
// Decides WHERE PlayerStarts / Expansions / Resources / Chokepoints /
// CurseSpawns / TravelLanes go, per archetype. The output is consumed by
// ProceduralHeightmap (slope budgets) and ProceduralMapGen (entity spawn).
//
// Deterministic: same (archetype, seed, mapSize, playerCount) → identical
// region list. Uses `System.Random` keyed off the seed — no
// `UnityEngine.Random`.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public static class RegionPlacer
    {
        // Slope budgets per the §A table in the plan.
        public const float Budget_PlayerStart = 5f;
        public const float Budget_Expansion   = 8f;
        public const float Budget_TravelLane  = 10f;
        public const float Budget_Resource    = 10f;
        public const float Budget_CurseSpawn  = 10f;
        public const float Budget_Chokepoint  = 15f;

        // Sizes tuned for the default 250m-wide map (MapHalfSize=125).
        // Earlier values (PlayerStart=25, Expansion=70) overlapped chokepoints
        // visibly because Expansion alone covered >55% of the map half.
        public const float Radius_PlayerStart = 18f;
        public const float Radius_Expansion   = 32f;
        public const float Radius_Resource    = 8f;
        public const float Radius_CurseSpawn  = 12f;
        public const float Width_TravelLane   = 12f;
        public const float Width_Chokepoint   = 16f;

        public static MapRegionSet Place(MapArchetype arch, int seed, int playerCount,
                                         Vector2 worldMin, Vector2 worldMax,
                                         bool includeLanes = true)
        {
            var set = new MapRegionSet
            {
                archetype = arch,
                seed = seed,
                worldMin = worldMin,
                worldMax = worldMax,
            };
            var rng = new System.Random(seed);

            // 1. PlayerStarts — archetypal anchor positions with ±15m jitter.
            var starts = PlayerStartAnchors(arch, playerCount, worldMin, worldMax, rng);
            foreach (var s in starts)
            {
                var r = MapRegion.Blob(RegionTag.PlayerStart, s, Radius_PlayerStart, Budget_PlayerStart);
                r.payloadIndex = set.regions.Count;
                set.regions.Add(r);
            }

            // 2. Expansion around each PlayerStart.
            foreach (var s in starts)
                set.regions.Add(MapRegion.Blob(RegionTag.Expansion, s, Radius_Expansion, Budget_Expansion));

            // 3. Chokepoints — archetype-defined seam positions.
            var chokes = ChokepointAnchors(arch, starts, worldMin, worldMax, seed);
            for (int i = 0; i < chokes.Count; i++)
            {
                var c = MapRegion.Blob(RegionTag.Chokepoint, chokes[i],
                    Width_Chokepoint * 0.6f, Budget_Chokepoint);
                set.regions.Add(c);
            }

            // 4. TravelLanes — routed between starts via chokepoints. Skipped
            //    when the caller wants a lane-free draft (used by ProceduralMapGen
            //    to test which pairs actually need a carved corridor).
            if (includeLanes)
            {
                for (int i = 0; i < starts.Count; i++)
                for (int j = i + 1; j < starts.Count; j++)
                {
                    var pts = RouteLane(starts[i], starts[j], chokes, seed + i * 31 + j * 17);
                    set.regions.Add(MapRegion.Lane(RegionTag.TravelLane, pts, Width_TravelLane, Budget_TravelLane));
                }
            }

            // 5. CurseSpawns — typically at the map's contested midpoint(s).
            var curseSites = CurseSpawnAnchors(arch, starts, worldMin, worldMax, seed);
            for (int i = 0; i < curseSites.Count; i++)
                set.regions.Add(MapRegion.Blob(RegionTag.CurseSpawn, curseSites[i], Radius_CurseSpawn, Budget_CurseSpawn));

            // 6. Resources — Poisson-style, biased near each Expansion but
            //    forbidden too close to the start itself.
            PlaceResources(set, starts, rng, arch);

            return set;
        }

        /// <summary>
        /// Build the lane polyline between two player starts using the same
        /// chokepoint routing as <see cref="Place"/>. Public so the generator
        /// can test "does this pair need a lane?" against a draft heightmap.
        /// </summary>
        public static Vector2[] BuildLanePolyline(MapArchetype arch, int seed, int playerCount,
                                                  Vector2 worldMin, Vector2 worldMax,
                                                  int i, int j)
        {
            var rng = new System.Random(seed);
            var starts = PlayerStartAnchors(arch, playerCount, worldMin, worldMax, rng);
            var chokes = ChokepointAnchors(arch, starts, worldMin, worldMax, seed);
            if (i < 0 || j < 0 || i >= starts.Count || j >= starts.Count) return null;
            return RouteLane(starts[i], starts[j], chokes, seed + i * 31 + j * 17);
        }

        /// <summary>Number of PlayerStart anchors this archetype produces.</summary>
        public static int PlayerStartCount(MapArchetype arch, int seed, int playerCount,
                                           Vector2 worldMin, Vector2 worldMax)
        {
            var rng = new System.Random(seed);
            return PlayerStartAnchors(arch, playerCount, worldMin, worldMax, rng).Count;
        }

        // ── PlayerStart anchors ───────────────────────────────────────────
        // Per-archetype anchor positions. Per-seed ±15m jitter.
        static List<Vector2> PlayerStartAnchors(MapArchetype arch, int playerCount,
                                                Vector2 worldMin, Vector2 worldMax, System.Random rng)
        {
            int n = Mathf.Clamp(playerCount, 2, 8);
            var anchors = new List<Vector2>(n);
            Vector2 size = worldMax - worldMin;
            Vector2 center = (worldMin + worldMax) * 0.5f;
            float halfX = size.x * 0.5f, halfZ = size.y * 0.5f;

            // Archetype-defined slot direction. Then evenly spaced around it.
            float baseAngle = arch switch
            {
                MapArchetype.Coastal => 0f,               // along east-west, north of coast
                MapArchetype.River   => Mathf.PI * 0.5f,  // north-south split
                MapArchetype.Island  => 0f,               // around the island perimeter
                MapArchetype.Isthmus => 0f,               // east-west endpoints
                _ /* Plain */        => 0.78f,            // diagonal-ish
            };

            float ringX = halfX * 0.72f, ringZ = halfZ * 0.72f;
            // Coastal/River/Isthmus: starts are anchored on opposite sides of
            // a single axis, not around a ring. Detect those layouts.
            if (arch == MapArchetype.Coastal || arch == MapArchetype.River || arch == MapArchetype.Isthmus)
            {
                bool axisIsX = arch != MapArchetype.River;
                for (int i = 0; i < n; i++)
                {
                    int side = (i % 2) == 0 ? -1 : 1;
                    float along = ((i / 2) - (n / 2 - 1) * 0.5f) * (size[axisIsX ? 1 : 0] * 0.4f);
                    Vector2 p = axisIsX
                        ? new Vector2(center.x + side * ringX, center.y + along + (arch == MapArchetype.Coastal ? 0.25f * halfZ : 0f))
                        : new Vector2(center.x + along, center.y + side * ringZ);
                    p += new Vector2((float)(rng.NextDouble() - 0.5) * 30f, (float)(rng.NextDouble() - 0.5) * 30f);
                    anchors.Add(p);
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    float a = baseAngle + t * Mathf.PI * 2f;
                    float jit = ((float)rng.NextDouble() - 0.5f) * 0.2f;
                    Vector2 p = center + new Vector2(Mathf.Cos(a + jit) * ringX, Mathf.Sin(a + jit) * ringZ);
                    p += new Vector2((float)(rng.NextDouble() - 0.5) * 30f, (float)(rng.NextDouble() - 0.5) * 30f);
                    anchors.Add(p);
                }
            }
            return anchors;
        }

        // ── Chokepoint anchors ────────────────────────────────────────────
        // Placed on the seams the archetype implies — between pairs of
        // PlayerStarts. River / Isthmus get 1 deliberate choke at the centre.
        // Island gets 2-3 along its central ridge. Plain gets ~2 mid-ish.
        static List<Vector2> ChokepointAnchors(MapArchetype arch, List<Vector2> starts,
                                              Vector2 worldMin, Vector2 worldMax, int seed)
        {
            var list = new List<Vector2>();
            Vector2 center = (worldMin + worldMax) * 0.5f;
            Vector2 size = worldMax - worldMin;
            var rng = new System.Random(seed ^ 0x6A0A);

            switch (arch)
            {
                case MapArchetype.Plain:
                    // Open battlefield — no carved chokepoints. Players
                    // navigate through hills/forest, not tactical squeezes.
                    break;
                case MapArchetype.Coastal:
                    list.Add(center + new Vector2(0f, size.y * 0.05f));
                    break;
                case MapArchetype.River:
                    // Two fords along the river centreline.
                    list.Add(center + new Vector2(-size.x * 0.25f, 0f));
                    list.Add(center + new Vector2( size.x * 0.25f, 0f));
                    break;
                case MapArchetype.Island:
                    // 2 chokepoints across the central spine.
                    list.Add(center + new Vector2(-size.x * 0.18f, 0f));
                    list.Add(center + new Vector2( size.x * 0.18f, 0f));
                    break;
                case MapArchetype.Isthmus:
                    list.Add(center);
                    break;
            }
            // Per-seed jitter so chokepoints aren't always at exact gridlines.
            for (int i = 0; i < list.Count; i++)
                list[i] = list[i] + new Vector2((float)(rng.NextDouble() - 0.5) * 20f,
                                                (float)(rng.NextDouble() - 0.5) * 20f);
            return list;
        }

        // ── Travel-lane routing ───────────────────────────────────────────
        // Polyline from start A to start B passing through the nearest
        // chokepoint(s). Then smoothed with 2 Chaikin iterations.
        static Vector2[] RouteLane(Vector2 a, Vector2 b, List<Vector2> chokes, int seed)
        {
            // Build a control polyline: A → nearest choke(s) sorted along the
            // a→b axis → B.
            var ctrls = new List<Vector2> { a };
            if (chokes != null && chokes.Count > 0)
            {
                Vector2 dir = (b - a).normalized;
                // Sort chokes by their projection onto a→b. Include ones
                // whose perpendicular distance from a→b is < ~60m.
                var picks = new List<(float along, Vector2 p)>();
                foreach (var c in chokes)
                {
                    float along = Vector2.Dot(c - a, dir);
                    if (along < 5f || along > Vector2.Distance(a, b) - 5f) continue;
                    Vector2 closest = a + dir * along;
                    if (Vector2.Distance(closest, c) > 80f) continue;
                    picks.Add((along, c));
                }
                picks.Sort((x, y) => x.along.CompareTo(y.along));
                foreach (var pp in picks) ctrls.Add(pp.p);
            }
            ctrls.Add(b);
            // Chaikin smooth.
            for (int k = 0; k < 2; k++) ctrls = Chaikin(ctrls);
            return ctrls.ToArray();
        }

        static List<Vector2> Chaikin(List<Vector2> src)
        {
            if (src.Count < 3) return src;
            var dst = new List<Vector2>(src.Count * 2) { src[0] };
            for (int i = 0; i < src.Count - 1; i++)
            {
                Vector2 p0 = src[i], p1 = src[i + 1];
                dst.Add(p0 * 0.75f + p1 * 0.25f);
                dst.Add(p0 * 0.25f + p1 * 0.75f);
            }
            dst.Add(src[src.Count - 1]);
            return dst;
        }

        // ── CurseSpawn anchors ────────────────────────────────────────────
        static List<Vector2> CurseSpawnAnchors(MapArchetype arch, List<Vector2> starts,
                                              Vector2 worldMin, Vector2 worldMax, int seed)
        {
            var list = new List<Vector2>();
            Vector2 center = (worldMin + worldMax) * 0.5f;
            Vector2 size = worldMax - worldMin;
            var rng = new System.Random(seed ^ 0xC4C5);

            switch (arch)
            {
                case MapArchetype.Plain:
                case MapArchetype.Coastal:
                case MapArchetype.River:
                case MapArchetype.Island:
                    list.Add(center + new Vector2((float)(rng.NextDouble() - 0.5) * 30f,
                                                  (float)(rng.NextDouble() - 0.5) * 30f));
                    break;
                case MapArchetype.Isthmus:
                    // One per mainland.
                    list.Add(new Vector2(worldMin.x + size.x * 0.25f, center.y));
                    list.Add(new Vector2(worldMin.x + size.x * 0.75f, center.y));
                    break;
            }
            return list;
        }

        // ── Resource placement ────────────────────────────────────────────
        // Each player gets 3-4 nearby. Plus 1-2 contested ones near the map
        // centre or along travel lanes.
        static void PlaceResources(MapRegionSet set, List<Vector2> starts, System.Random rng, MapArchetype arch)
        {
            const float minDistFromStart = 35f;
            const float maxDistFromStart = 90f;
            const float minDistResRes    = 30f;

            var placed = new List<Vector2>();

            foreach (var s in starts)
            {
                for (int attempt = 0; attempt < 6 && CountNear(placed, s, maxDistFromStart) < 3; attempt++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float d = Mathf.Lerp(minDistFromStart, maxDistFromStart, (float)rng.NextDouble());
                    Vector2 p = s + new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                    if (TooClose(placed, p, minDistResRes)) continue;
                    placed.Add(p);
                    var r = MapRegion.Blob(RegionTag.Resource, p, Radius_Resource, Budget_Resource);
                    r.payloadIndex = (int)(rng.Next() & 0x3); // 0=iron, 1=crystal, ... let caller decide
                    set.regions.Add(r);
                }
            }
            // 2 contested resources between players.
            for (int i = 0; i < 2; i++)
            {
                Vector2 mid = (starts[0] + starts[1 % starts.Count]) * 0.5f
                            + new Vector2((float)(rng.NextDouble() - 0.5) * 40f,
                                          (float)(rng.NextDouble() - 0.5) * 40f);
                if (TooClose(placed, mid, minDistResRes)) continue;
                placed.Add(mid);
                set.regions.Add(MapRegion.Blob(RegionTag.Resource, mid, Radius_Resource, Budget_Resource));
            }
        }

        static int CountNear(List<Vector2> pts, Vector2 q, float maxD)
        {
            int n = 0;
            foreach (var p in pts) if (Vector2.Distance(p, q) <= maxD) n++;
            return n;
        }
        static bool TooClose(List<Vector2> pts, Vector2 q, float minD)
        {
            foreach (var p in pts) if (Vector2.Distance(p, q) < minD) return true;
            return false;
        }
    }
}
