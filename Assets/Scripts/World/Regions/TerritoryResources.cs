// TerritoryResources.cs
// What resources each territory carries, and laying them on the ground
// (docs/Design/Territory_Claims.md §9, 2026-10-01).
//
//   Start            3 supply, 2 iron, 1 veilstone
//   Normal           3 supply
//   Normal + iron    2 supply, 1 iron
//   Normal + veil    2 supply, 1-2 veilstone (seeded draw, 2026-10-03)
//   Empty            nothing — position and build space
//   Veilstone rich   4 veilstone, starts cursed
//   Iron rich        3 iron
//   Sanctum          nothing — its holder earns 1 Religion Point a minute
//
// The TYPE comes from the territory's RegionSeedMarker; Auto (the default)
// is resolved here from the match seed so that every map has at least one of
// each special type (Empty, Veilstone rich, Iron rich, Sanctum) and the rest
// are the three Normal kinds, dealt in the weights TerritoryResources.asset
// carries (2026-10-03: 1 Normal : 2 + iron : 4 + veilstone — veilstone is
// what Alanthor armies run out of, so more of the map carries it). A territory holding a player start is always
// Start. The scene's node MARKERS no longer decide anything — the nodes are
// generated from the type, so every map conforms without hand-editing.
//
// Deterministic: same map + same seed = same types and node positions on
// every lockstep peer (sorted indices, a seeded RNG, terrain sampling only).

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;
using ResourceType = TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType;

namespace TheWaningBorder.World.Regions
{
    public static class TerritoryResources
    {
        /// <summary>Veilstone held by a generated outcrop (its NodeReserve is separate).</summary>
        private const int VeilstonePerOutcrop = 900;
        /// <summary>Iron deposits' legacy amount, in marker units of 50.</summary>
        private const int IronDepositUnits = 30;
        // No node within the START CLEARING (2026-10-06): every node keeps
        // StartClearing.asset's radius clear of EVERY player start, in any
        // territory — enforced by ResourceNodeSite, skipped up front here.
        private const float RingStep = 6f;
        private const float MaxRing = 60f;
        private const int RingSamples = 12;
        /// <summary>Generated nodes keep this far apart (centre to centre).</summary>
        private const float NodeSpacing = 9f;

        private static TerritoryResourcesConfig _cfg;
        /// <summary>The filler weights and outcrop range, from
        /// TerritoryResources.asset (no code-side fallback, by design).</summary>
        private static TerritoryResourcesConfig Cfg =>
            _cfg != null ? _cfg
            : (_cfg = TheWaningBorder.Core.Settings.ComponentConfig.Require<TerritoryResourcesConfig>());

        private static ResourceType[] _types = System.Array.Empty<ResourceType>();
        private static int _version = int.MinValue;

        /// <summary>The resolved type of a territory (Normal before resolution).</summary>
        public static ResourceType TypeOf(int territory)
            => territory >= 0 && territory < _types.Length ? _types[territory] : ResourceType.Normal;

        public static bool IsSanctum(int territory) => TypeOf(territory) == ResourceType.Sanctum;

        /// <summary>Every territory of this type, in index order.</summary>
        public static List<int> TerritoriesOf(ResourceType type)
        {
            var list = new List<int>();
            for (int i = 0; i < _types.Length; i++) if (_types[i] == type) list.Add(i);
            return list;
        }

        // ── Resolution ──────────────────────────────────────────────────

        /// <summary>
        /// Settle every territory's type for this match. <paramref name="starts"/>
        /// are the player start positions.
        /// </summary>
        public static void Resolve(IReadOnlyList<float3> starts, uint seed)
        {
            int n = RegionMap.Count;
            _types = new ResourceType[n];
            _version = RegionMap.Version;
            if (n == 0) return;

            // Authored, in the same order RegionMap.BuildFromMarkers built them.
            var markers = MapMarkers.MapMarkerRegistry.RegionSeeds;
            int idx = 0;
            for (int i = 0; i < markers.Count && idx < n; i++)
            {
                if (markers[i] == null) continue;
                _types[idx++] = markers[i].Resources;
            }

            var startSet = new HashSet<int>();
            for (int i = 0; i < starts.Count; i++)
            {
                int t = RegionMap.NearestRegion(starts[i].x, starts[i].z);
                if (t != RegionMap.None) startSet.Add(t);
            }

            var auto = new List<int>();
            for (int t = 0; t < n; t++)
            {
                if (RegionMap.KindBlocks(RegionMap.KindOf(t))) { _types[t] = ResourceType.Empty; continue; }
                if (startSet.Contains(t)) { _types[t] = ResourceType.Start; continue; }
                // AN AUTHORED START STAYS A START (2026-10-05). It used to be
                // re-dealt as Auto when no player sat there, so a two-player
                // match on a four-seat map changed the empty corners' types,
                // the quadrants stopped matching, the mirrored node layout
                // fell back to random (MirrorPartners), and one home's
                // outcrop landed where nothing could reach it: that side
                // aged up at minute 24. The map author's seats keep their
                // layout whoever is seated.
                if (_types[t] == ResourceType.Auto) auto.Add(t);
            }

            var rng = new Unity.Mathematics.Random(seed | 1u);
            for (int i = auto.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (auto[i], auto[j]) = (auto[j], auto[i]);
            }

            // One of every special type the authoring did not already place.
            var specials = new[]
            {
                ResourceType.Sanctum, ResourceType.VeilstoneRich, ResourceType.IronRich, ResourceType.Empty,
            };
            int next = 0;
            foreach (var special in specials)
            {
                bool placed = false;
                for (int t = 0; t < n && !placed; t++) placed = _types[t] == special;
                if (placed || next >= auto.Count) continue;
                _types[auto[next++]] = special;
            }

            // The fillers, dealt in the config's weights by a smooth weighted
            // round-robin: the kinds interleave (V I V N V I V ...) instead of
            // coming in blocks, so even a small map gets its share of each,
            // and the deal is a pure function of the shuffled order — no RNG
            // draw, nothing for lockstep peers to disagree on.
            var fillers = new[] { ResourceType.Normal, ResourceType.NormalIron, ResourceType.NormalVeilstone };
            var cfg = Cfg;
            var weights = new[]
            {
                math.max(0, cfg.normalWeight),
                math.max(0, cfg.normalIronWeight),
                math.max(0, cfg.normalVeilstoneWeight),
            };
            int total = weights[0] + weights[1] + weights[2];
            if (total <= 0) { weights[0] = weights[1] = weights[2] = 1; total = 3; }
            var current = new int[fillers.Length];
            for (int k = next; k < auto.Count; k++)
            {
                int pick = 0;
                for (int f = 0; f < fillers.Length; f++)
                {
                    current[f] += weights[f];
                    if (current[f] > current[pick]) pick = f;
                }
                current[pick] -= total;
                _types[auto[k]] = fillers[pick];
            }

            int specialsMissing = 0;
            foreach (var special in specials)
                if (TerritoriesOf(special).Count == 0) specialsMissing++;
            if (specialsMissing > 0)
                Debug.LogWarning($"[TerritoryResources] This map has too few territories for every special " +
                                 $"type — {specialsMissing} missing. Redraw it with more territories " +
                                 "(docs/Design/Territory_Claims.md §9).");
        }

        // ── Laying the nodes ────────────────────────────────────────────

        /// <summary>
        /// Raise every territory's nodes from its type. Call after
        /// <see cref="Resolve"/>, once the terrain and the obstacles are in.
        /// </summary>
        public static void Spawn(EntityManager em, IReadOnlyList<float3> starts, uint seed)
        {
            int n = RegionMap.Count;
            var centres = Centroids(n, out var boxMin, out var boxMax);
            var homeCentre = new Dictionary<int, float3>();
            for (int i = 0; i < starts.Count; i++)
            {
                int t = RegionMap.NearestRegion(starts[i].x, starts[i].z);
                if (t != RegionMap.None && !homeCentre.ContainsKey(t)) homeCentre[t] = starts[i];
            }

            // A MIRRORED MAP (2026-10-05) lays its nodes once, in the
            // territories of one quadrant, and copies them across both axes,
            // so every seat opens on the same ground. Detected, not authored:
            // see MirrorPartners.
            var partners = MirrorPartners(n, centres, boxMin, boxMax, out float2 mid);

            int placed = 0, short_ = 0;
            for (int t = 0; t < n; t++)
            {
                if (partners != null && partners[t].x < 0) continue;   // laid by its canonical twin
                // One stream per territory: the same map and seed lay the same
                // nodes on every peer, and adding a territory does not reshuffle
                // every other one.
                var rng = new Unity.Mathematics.Random((seed ^ (uint)(t * 0x9E3779B1u)) | 1u);
                Counts(TypeOf(t), ref rng, out int supply, out int iron, out int veil);
                if (supply + iron + veil == 0) continue;

                bool home = homeCentre.TryGetValue(t, out var hc);
                float3 centre = home ? hc : centres[t];
                var mine = new List<float3>();
                if (partners != null) _record = new List<(float3, int)>();

                for (int i = 0; i < veil; i++)
                    short_ += PlaceRandom(em, t, home, centre, boxMin[t], boxMax[t], mine, 2, ref rng) ? 0 : 1;
                for (int i = 0; i < iron; i++)
                    short_ += PlaceRandom(em, t, home, centre, boxMin[t], boxMax[t], mine, 1, ref rng) ? 0 : 1;
                for (int i = 0; i < supply; i++)
                    short_ += PlaceRandom(em, t, home, centre, boxMin[t], boxMax[t], mine, 0, ref rng) ? 0 : 1;
                placed += mine.Count;

                if (partners != null)
                {
                    var laid = _record;
                    _record = null;
                    var twins = partners[t];
                    for (int k = 0; k < 3; k++)
                    {
                        int p = k == 0 ? twins.y : k == 1 ? twins.z : twins.w;
                        float2 sign = k == 0 ? new float2(-1f, 1f) : k == 1 ? new float2(1f, -1f) : new float2(-1f, -1f);
                        foreach (var (site, kind) in laid)
                        {
                            var want = new float3(mid.x + sign.x * (site.x - mid.x), 0f,
                                                  mid.y + sign.y * (site.z - mid.y));
                            want.y = TerrainUtility.GetHeight(want.x, want.z);
                            if (ResourceNodeSite.TryResolve(em, want, out var twin)
                                && RegionMap.RegionAt(twin.x, twin.z) == p)
                            { Create(em, twin, kind); placed++; }
                            else short_++;
                        }
                    }
                }
            }
            if (partners != null)
                Debug.Log("[TerritoryResources] mirrored map: nodes laid in one quadrant and copied across both axes.");
            Debug.Log($"[TerritoryResources] {placed} node(s) laid from territory types" +
                      (short_ > 0 ? $"; {short_} could not find legal ground." : "."));
        }

        private static void Counts(ResourceType type, ref Unity.Mathematics.Random rng,
            out int supply, out int iron, out int veil)
        {
            supply = iron = veil = 0;
            switch (type)
            {
                case ResourceType.Start:           supply = 3; iron = 3; veil = 1; break;   // 3 iron since 2026-10-02 (Veilstone_Economy.md §6)
                case ResourceType.Normal:          supply = 3; break;
                case ResourceType.NormalIron:      supply = 2; iron = 1; break;
                case ResourceType.NormalVeilstone:
                {
                    // 1-2 outcrops since 2026-10-03 (TerritoryResources.asset),
                    // drawn from the territory's own stream before any node is
                    // laid, so the draw is the same on every peer.
                    var cfg = Cfg;
                    int lo = math.max(0, cfg.normalVeilstoneOutcropsMin);
                    int hi = math.max(lo, cfg.normalVeilstoneOutcropsMax);
                    supply = 2; veil = rng.NextInt(lo, hi + 1);
                    break;
                }
                case ResourceType.VeilstoneRich:   veil = 4; break;
                case ResourceType.IronRich:        iron = 3; break;
            }
        }

        /// <summary>How far a node's CENTRE stays from impassable ground —
        /// a lake, a mountain, the map's edge (region None).</summary>
        private const float BorderInset = 8f;

        /// <summary>
        /// THE RESTRICTION ZONE (2026-10-02): a node's whole FOOTPRINT keeps
        /// this clear of any OTHER territory — the band inside every border
        /// where the AI's border wall runs and no AI building stands
        /// (AIWallPlanner buildingBorderClearance, docs/Design/
        /// Age_1_Alanthor.md § The AI's wall). A node in it would block the
        /// wall or be walled off. Measured on a 2 m grid over the footprint
        /// grown by the band, so a diagonal border cannot slip between samples
        /// the way the old four-point compass check let it.
        /// </summary>
        private const float BorderBand = 10f;
        private const int RandomTries = 160;

        /// <summary>
        /// One node of <paramref name="kind"/> at a RANDOM legal spot inside
        /// territory <paramref name="t"/> — anywhere in it, at least
        /// <see cref="BorderInset"/> from its border, <see cref="NodeSpacing"/>
        /// from the territory's other nodes and outside every start's
        /// clearing (<see cref="StartClearing"/>). Falls back to the ring search when the random draws all
        /// miss (a sliver of a territory).
        /// </summary>
        private static bool PlaceRandom(EntityManager em, int t, bool home, float3 centre,
            float2 min, float2 max, List<float3> mine, int kind, ref Unity.Mathematics.Random rng)
        {
            for (int k = 0; k < RandomTries; k++)
            {
                var d = new float3(rng.NextFloat(min.x, max.x), 0f, rng.NextFloat(min.y, max.y));
                if (!InsideWithInset(d, t)) continue;
                if (StartClearing.Covers(d, BuildGrid.ResourceNodeHalf)) continue;
                if (home && !OnHomeEdge(d, centre)) continue;
                if (TooClose(d, mine)) continue;
                d.y = TerrainUtility.GetHeight(d.x, d.z);
                if (!ResourceNodeSite.TryResolve(em, d, out var site)) continue;
                if (!InsideWithInset(site, t) || TooClose(site, mine)) continue;
                if (StartClearing.Covers(site, BuildGrid.ResourceNodeHalf)) continue;
                if (home && !OnHomeEdge(site, centre)) continue;
                Create(em, site, kind);
                mine.Add(site);
                return true;
            }
            // A home's ring search starts outside the start clearing AND the
            // main camp (the edge band below).
            return Place(em, t, centre,
                         home ? math.max(StartClearing.Radius + BuildGrid.ResourceNodeMeters,
                                         Cfg.homeNodeMinOffset) : 0f,
                         mine, kind);
        }

        /// <summary>A home node keeps homeNodeMinOffset from the start on at
        /// least one axis — the band along the home territory's edge, outside
        /// the AI's main camp (docs/Design/Territory_Claims.md §11).</summary>
        private static bool OnHomeEdge(float3 p, float3 start)
        {
            float off = Cfg.homeNodeMinOffset;
            if (off <= 0f) return true;
            return math.max(math.abs(p.x - start.x), math.abs(p.z - start.z)) >= off;
        }

        private static bool InsideWithInset(float3 p, int t)
        {
            if (RegionMap.RegionAt(p.x, p.z) != t) return false;
            float half = BuildGrid.ResourceNodeHalf + BorderBand;
            const float Step = 2f;
            for (float dx = -half; dx <= half + 1e-3f; dx += Step)
                for (float dz = -half; dz <= half + 1e-3f; dz += Step)
                {
                    int r = RegionMap.RegionAt(p.x + dx, p.z + dz);
                    if (r == t) continue;
                    // Impassable ground: only the centre's 8 m inset applies.
                    if (r == RegionMap.None)
                    {
                        if (dx * dx + dz * dz <= BorderInset * BorderInset) return false;
                        continue;
                    }
                    // Another territory inside the band: the wall runs here.
                    return false;
                }
            return true;
        }

        /// <summary>While non-null, every node Create lays is noted here —
        /// the canonical quadrant's list a mirrored map copies.</summary>
        private static List<(float3, int)> _record;

        /// <summary>
        /// Null unless the territories are mirror-symmetric across BOTH axes
        /// through the map's centre: every territory's mirror images are
        /// territories of the same type, and none straddles an axis. Then
        /// entry t is (-1, ...) for a territory laid by its twin, or
        /// (t, mirror-in-X, mirror-in-Z, mirror-in-both) for a canonical one
        /// (the quadrant below-left of the centre).
        /// </summary>
        private static int4[] MirrorPartners(int n, float3[] centres, float2[] boxMin, float2[] boxMax,
            out float2 mid)
        {
            mid = float2.zero;
            if (n < 4) return null;
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            for (int t = 0; t < n; t++)
            {
                if (boxMin[t].x > boxMax[t].x) continue;   // no ground sampled
                lo = math.min(lo, boxMin[t]);
                hi = math.max(hi, boxMax[t]);
            }
            mid = (lo + hi) * 0.5f;
            var result = new int4[n];
            for (int t = 0; t < n; t++)
            {
                if (boxMin[t].x > boxMax[t].x) return null;
                float2 c = (boxMin[t] + boxMax[t]) * 0.5f - mid;
                if (math.abs(c.x) < 4f || math.abs(c.y) < 4f) return null;   // straddles an axis
                int mx = RegionMap.RegionAt(mid.x - c.x, mid.y + c.y);
                int mz = RegionMap.RegionAt(mid.x + c.x, mid.y - c.y);
                int mb = RegionMap.RegionAt(mid.x - c.x, mid.y - c.y);
                if (mx < 0 || mz < 0 || mb < 0) return null;
                var ty = TypeOf(t);
                if (TypeOf(mx) != ty || TypeOf(mz) != ty || TypeOf(mb) != ty) return null;
                bool canonical = c.x < 0f && c.y < 0f;
                result[t] = canonical ? new int4(t, mx, mz, mb) : new int4(-1, mx, mz, mb);
            }
            return result;
        }

        private static void Create(EntityManager em, float3 site, int kind)
        {
            _record?.Add((site, kind));
            switch (kind)
            {
                case 0: SupplyNode.Create(em, site); break;
                case 1: TheWaningBorder.Bootstrap.IronDepositBootstrap.SpawnQuotaNode(em, site, IronDepositUnits); break;
                default: VeilstoneOutcropping.Create(em, site, VeilstonePerOutcrop); break;
            }
        }

        /// <summary>One node of <paramref name="kind"/> (0 supply, 1 iron, 2
        /// veilstone) on the first legal ring site inside the territory — the
        /// fallback for a territory too thin for the random draw.</summary>
        private static bool Place(EntityManager em, int t, float3 centre, float minR, List<float3> mine, int kind)
        {
            for (float r = minR; r <= minR + MaxRing; r += RingStep)
            {
                int samples = r <= 0f ? 1 : RingSamples;
                // A per-ring, per-territory phase so neighbouring rings do not line up.
                float phase = (t * 0.618f + r * 0.137f) % 1f;
                for (int s = 0; s < samples; s++)
                {
                    float a = (s / (float)samples + phase) * math.PI * 2f;
                    var desired = new float3(centre.x + math.cos(a) * r, 0f, centre.z + math.sin(a) * r);
                    // The same band as the random draw: a thin territory may
                    // come up short (logged) rather than seed the wall's path.
                    if (!InsideWithInset(desired, t)) continue;
                    if (StartClearing.Covers(desired, BuildGrid.ResourceNodeHalf)) continue;
                    if (TooClose(desired, mine)) continue;
                    desired.y = TerrainUtility.GetHeight(desired.x, desired.z);
                    if (!ResourceNodeSite.TryResolve(em, desired, out var site)) continue;
                    if (!InsideWithInset(site, t) || TooClose(site, mine)) continue;

                    Create(em, site, kind);
                    mine.Add(site);
                    return true;
                }
            }
            return false;
        }

        private static bool TooClose(float3 p, List<float3> others)
        {
            for (int i = 0; i < others.Count; i++)
                if (math.distancesq(p.xz, others[i].xz) < NodeSpacing * NodeSpacing) return true;
            return false;
        }

        /// <summary>The centroid of each territory's sampled ground (its own
        /// sample nearest that centroid when a concave territory's centroid
        /// falls outside it).</summary>
        private static float3[] Centroids(int n, out float2[] boxMin, out float2[] boxMax)
        {
            boxMin = new float2[n];
            boxMax = new float2[n];
            for (int r = 0; r < n; r++) { boxMin[r] = new float2(float.MaxValue); boxMax[r] = new float2(float.MinValue); }
            var sum = new Vector2[n];
            var count = new int[n];
            const float step = 4f;
            TerrainUtility.GetPlayableBounds(out var min, out var max);
            for (float z = min.y + step * 0.5f; z < max.y; z += step)
                for (float x = min.x + step * 0.5f; x < max.x; x += step)
                {
                    int r = RegionMap.RegionAt(x, z);
                    if (r < 0 || r >= n) continue;
                    sum[r] += new Vector2(x, z);
                    count[r]++;
                    boxMin[r] = math.min(boxMin[r], new float2(x, z));
                    boxMax[r] = math.max(boxMax[r], new float2(x, z));
                }
            var mean = new Vector2[n];
            var best = new Vector2[n];
            var bestD = new float[n];
            for (int r = 0; r < n; r++)
            {
                mean[r] = count[r] > 0 ? sum[r] / count[r] : RegionMap.SeedOf(r);
                best[r] = mean[r];
                bestD[r] = float.MaxValue;
            }
            for (float z = min.y + step * 0.5f; z < max.y; z += step)
                for (float x = min.x + step * 0.5f; x < max.x; x += step)
                {
                    int r = RegionMap.RegionAt(x, z);
                    if (r < 0 || r >= n) continue;
                    float d = (new Vector2(x, z) - mean[r]).sqrMagnitude;
                    if (d < bestD[r]) { bestD[r] = d; best[r] = new Vector2(x, z); }
                }
            var c = new float3[n];
            for (int r = 0; r < n; r++)
            {
                Vector2 p = RegionMap.RegionAt(mean[r].x, mean[r].y) == r ? mean[r] : best[r];
                c[r] = new float3(p.x, 0f, p.y);
            }
            return c;
        }
    }
}
