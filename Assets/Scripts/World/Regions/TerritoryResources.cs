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
using Unity.Transforms;
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
            var pinned = new HashSet<int>();
            for (int i = 0; i < markers.Count && idx < n; i++)
            {
                if (markers[i] == null) continue;
                if (markers[i].CurseStaysHere) pinned.Add(idx);
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

            if (Cfg.randomizeCurseTerritories) RandomizeCurse(startSet, pinned, seed);

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
            ComputeMargins(n, boxMin, boxMax);
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
            _reserved.Clear();
            _noPostRoomAt.Clear();
            // NODE PURITY (Territory_Claims.md § 11.2): one map-wide bag, its
            // own stream, dealt in territory order — the same on every peer,
            // and node POSITIONS draw from the territory streams untouched.
            _purityRng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0x9A17E5u)) | 1u);
            _purityBag.Clear();
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
                if (partners != null) _record = new List<(float3, int, byte)>();
                // A start territory, seated or not, is all Pure.
                _dealPure = home || TypeOf(t) == ResourceType.Start;

                for (int i = 0; i < veil; i++)
                {
                    // Room for all four posts first; only a territory with no
                    // such ground anywhere gets an outcrop without it (logged).
                    System.Array.Clear(_why, 0, _why.Length);
                    _needPostRoom = true;
                    bool ok = PlaceRandom(em, t, home, centre, boxMin[t], boxMax[t], mine, 2, ref rng);
                    _needPostRoom = false;
                    if (!ok)
                    {
                        // Still room for the posts, but the wall band shrunk
                        // to the plain edge inset: a tight territory gives up
                        // some of the AI wall's strip before a post side.
                        _needPostRoom = true;
                        _relaxBand = true;
                        ok = PlaceRandom(em, t, home, centre, boxMin[t], boxMax[t], mine, 2, ref rng)
                          || ScanForPostRoom(em, t, home, centre, boxMin[t], boxMax[t], mine);
                        _needPostRoom = false;
                        _relaxBand = false;
                    }
                    if (!ok)
                    {
                        ok = PlaceRandom(em, t, home, centre, boxMin[t], boxMax[t], mine, 2, ref rng);
                        if (ok) _noPostRoomAt.Add($"{RegionMap.NameOf(t)} [bounds {_why[0]}, territory {_why[1]}, terrain {_why[2]}, obstacle {_why[3]}, building {_why[4]}, node {_why[5]}, reserved {_why[6]}]");
                    }
                    short_ += ok ? 0 : 1;
                }
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
                        foreach (var (site, kind, grade) in laid)
                        {
                            var want = new float3(mid.x + sign.x * (site.x - mid.x), 0f,
                                                  mid.y + sign.y * (site.z - mid.y));
                            want.y = TerrainUtility.GetHeight(want.x, want.z);
                            if (ResourceNodeSite.TryResolve(em, want, out var twin)
                                && RegionMap.RegionAt(twin.x, twin.z) == p)
                            { Create(em, twin, kind, grade); placed++; }
                            else short_++;
                        }
                    }
                }
            }
            _dealPure = false;
            if (partners != null)
                Debug.Log("[TerritoryResources] mirrored map: nodes laid in one quadrant and copied across both axes.");
            Debug.Log($"[TerritoryResources] node purity dealt: {_dealt[NodePurityGrade.Pure]} pure, " +
                      $"{_dealt[NodePurityGrade.Normal]} normal, {_dealt[NodePurityGrade.Poor]} poor.");
            _dealt[0] = _dealt[1] = _dealt[2] = 0;
            Debug.Log($"[TerritoryResources] {placed} node(s) laid from territory types" +
                      (short_ > 0 ? $"; {short_} could not find legal ground." : "."));
            if (_noPostRoomAt.Count > 0)
                Debug.LogWarning($"[TerritoryResources] {_noPostRoomAt.Count} veilstone outcrop(s) found no site with room " +
                                 $"for all four Trading Outposts and were laid without it ({string.Join(", ", _noPostRoomAt)}) " +
                                 "— widen that ground on the map.");
            else
                Debug.Log("[TerritoryResources] every veilstone outcrop has room for its four Trading Outposts.");
            _reserved.Clear();
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
        /// <summary>RandomTries multiplier while an outcrop needs room for its posts.</summary>
        private const int PostRoomTriesFactor = 12;

        /// <summary>
        /// MARGINS SCALE WITH THE TERRITORY (2026-10-17). BorderBand and
        /// BorderInset were set for territories tens of metres across; a
        /// small one (Yiel Lymwérra's island, 49 x 27 m) had no legal ground
        /// left inside them, so its four outcrops — and the curse — went
        /// elsewhere. Each territory's margins are capped at
        /// <see cref="MarginShare"/> of its shorter bounding-box half-width;
        /// a territory wide enough for the full margins is unchanged.
        /// </summary>
        private const float MarginShare = 0.35f;
        private static float[] _band = System.Array.Empty<float>();
        private static float[] _inset = System.Array.Empty<float>();

        private static void ComputeMargins(int n, float2[] boxMin, float2[] boxMax)
        {
            _band = new float[n];
            _inset = new float[n];
            for (int t = 0; t < n; t++)
            {
                float2 ext = boxMax[t] - boxMin[t];
                float cap = math.max(0f, math.cmin(ext) * 0.5f * MarginShare);
                _band[t] = math.min(BorderBand, cap);
                _inset[t] = math.min(BorderInset, cap);
            }
        }

        private static float BandOf(int t)
        {
            float band = t >= 0 && t < _band.Length ? _band[t] : BorderBand;
            return _relaxBand ? math.min(band, InsetOf(t)) : band;
        }
        private static float InsetOf(int t) => t >= 0 && t < _inset.Length ? _inset[t] : BorderInset;

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
            // An outcrop that must keep room for its four posts draws far more
            // spots: most draws die on the home / band rules before the post
            // test, and a home edge band can hold only a few flat spots.
            int tries = _needPostRoom ? RandomTries * PostRoomTriesFactor : RandomTries;
            for (int k = 0; k < tries; k++)
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
                if (!SiteKeepsPostRoom(em, site, t, kind)) continue;
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
            // The relaxed post-room pass gives up the edge band before an
            // outcrop goes without room for its posts (the start clearing holds).
            if (off <= 0f || _relaxBand) return true;
            return math.max(math.abs(p.x - start.x), math.abs(p.z - start.z)) >= off;
        }

        private static bool InsideWithInset(float3 p, int t)
        {
            if (RegionMap.RegionAt(p.x, p.z) != t) return false;
            float half = BuildGrid.ResourceNodeHalf + BandOf(t);
            float inset = InsetOf(t);
            const float Step = 2f;
            for (float dx = -half; dx <= half + 1e-3f; dx += Step)
                for (float dz = -half; dz <= half + 1e-3f; dz += Step)
                {
                    int r = RegionMap.RegionAt(p.x + dx, p.z + dz);
                    if (r == t) continue;
                    // Impassable ground: only the centre's 8 m inset applies.
                    if (r == RegionMap.None)
                    {
                        if (dx * dx + dz * dz <= inset * inset) return false;
                        continue;
                    }
                    // Another territory inside the band: the wall runs here.
                    return false;
                }
            return true;
        }

        /// <summary>While non-null, every node Create lays is noted here —
        /// the canonical quadrant's list a mirrored map copies.</summary>
        private static List<(float3, int, byte)> _record;

        // ── node purity (Territory_Claims.md § 11.2) ────────────────────
        private static Unity.Mathematics.Random _purityRng;
        private static readonly List<byte> _purityBag = new List<byte>(16);
        private static bool _dealPure;
        private static readonly int[] _dealt = new int[3];

        /// <summary>The next grade: Pure inside a start territory, otherwise
        /// the next one out of the shuffled bag.</summary>
        private static byte DealGrade()
        {
            if (_dealPure) return NodePurityGrade.Pure;
            if (_purityBag.Count == 0)
            {
                var c = Cfg;
                for (int i = 0; i < math.max(0, c.purityBagPure); i++) _purityBag.Add(NodePurityGrade.Pure);
                for (int i = 0; i < math.max(0, c.purityBagNormal); i++) _purityBag.Add(NodePurityGrade.Normal);
                for (int i = 0; i < math.max(0, c.purityBagPoor); i++) _purityBag.Add(NodePurityGrade.Poor);
                if (_purityBag.Count == 0) return NodePurityGrade.Normal;
                for (int i = _purityBag.Count - 1; i > 0; i--)
                {
                    int j = _purityRng.NextInt(0, i + 1);
                    (_purityBag[i], _purityBag[j]) = (_purityBag[j], _purityBag[i]);
                }
            }
            byte g = _purityBag[_purityBag.Count - 1];
            _purityBag.RemoveAt(_purityBag.Count - 1);
            return g;
        }

        /// <summary>What a node of this grade pays, as a multiplier on its rate.</summary>
        public static float PurityMultiplier(byte grade)
        {
            var c = Cfg;
            return grade == NodePurityGrade.Pure ? c.purityPureMultiplier
                 : grade == NodePurityGrade.Poor ? c.purityPoorMultiplier
                 : c.purityNormalMultiplier;
        }

        /// <summary>The node's purity multiplier (Normal for a node without one).</summary>
        public static float PurityMultiplier(EntityManager em, Entity node)
            => PurityMultiplier(PurityOf(em, node));

        public static byte PurityOf(EntityManager em, Entity node)
            => em.HasComponent<NodePurity>(node) ? em.GetComponentData<NodePurity>(node).Grade : NodePurityGrade.Normal;

        /// <summary>"Pure " / "Poor " / "" — the prefix a node's name shows.</summary>
        public static string PurityPrefix(byte grade)
            => grade == NodePurityGrade.Pure ? "Pure " : grade == NodePurityGrade.Poor ? "Poor " : "";

        /// <summary>
        /// THE CURSE STARTS SOMEWHERE NEW (2026-10-07, developer: "randomize
        /// the curse starting positions (4 territories)"). The authored curse
        /// territories (Veilstone rich, which the curse holds at the start)
        /// swap types with territories drawn from the match seed — the same
        /// draw on every peer. On a mirrored map the draw is ONE canonical
        /// territory and its three mirror images, so every seat still faces
        /// the same curse; otherwise as many territories as the authoring
        /// cursed, at random. Never a home, never a territory bordering a home.
        /// </summary>
        private static void RandomizeCurse(HashSet<int> startSet, HashSet<int> pinned, uint seed)
        {
            int n = RegionMap.Count;
            var curse = TerritoriesOf(ResourceType.VeilstoneRich);
            // A PINNED curse territory (RegionSeedMarker.CurseStaysHere) is the
            // map's own cursed place and never moves; only the others are dealt.
            curse.RemoveAll(pinned.Contains);
            if (curse.Count == 0) return;
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xC0A5E5u)) | 1u);   // hashed: neighbouring seeds must not draw alike

            bool Eligible(int t)
            {
                if (t < 0 || t >= n) return false;
                var ty = _types[t];
                if (ty == ResourceType.VeilstoneRich || ty == ResourceType.Start || ty == ResourceType.Empty) return false;
                if (startSet.Contains(t) || RegionMap.KindBlocks(RegionMap.KindOf(t))) return false;
                for (int u = 0; u < n; u++)
                    if (_types[u] == ResourceType.Start && RegionMap.AreAdjacent(t, u)) return false;
                return true;
            }

            var centres = Centroids(n, out var bmin, out var bmax);
            var partners = MirrorPartners(n, centres, bmin, bmax, out _);
            var pick = new List<int>();
            if (partners != null)
            {
                var canon = new List<int>();
                for (int t = 0; t < n; t++)
                {
                    var p = partners[t];
                    if (p.x < 0 || !Eligible(t) || !Eligible(p.y) || !Eligible(p.z) || !Eligible(p.w)) continue;
                    canon.Add(t);
                }
                if (canon.Count == 0) return;
                var c = canon[rng.NextInt(0, canon.Count)];
                var cp = partners[c];
                pick.Add(cp.x); pick.Add(cp.y); pick.Add(cp.z); pick.Add(cp.w);
                if (pick.Count != curse.Count) return;   // the authoring is not one curse group per quadrant
            }
            else
            {
                var pool = new List<int>();
                for (int t = 0; t < n; t++) if (Eligible(t)) pool.Add(t);
                for (int k = 0; k < curse.Count && pool.Count > 0; k++)
                {
                    int i = rng.NextInt(0, pool.Count);
                    pick.Add(pool[i]);
                    pool.RemoveAt(i);
                }
                if (pick.Count != curse.Count) return;
            }

            // Swap: the old curse ground takes the new ground's types.
            for (int k = 0; k < pick.Count; k++)
            {
                _types[curse[k]] = _types[pick[k]];
                _types[pick[k]] = ResourceType.VeilstoneRich;
            }
            var names = new System.Text.StringBuilder();
            for (int k = 0; k < pick.Count; k++) { if (k > 0) names.Append(", "); names.Append(RegionMap.NameOf(pick[k])); }
            Debug.Log($"[TerritoryResources] the curse starts in {names} this match (seed {seed}).");
        }

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
            => Create(em, site, kind, DealGrade());

        // ── Room for the Trading Outposts (2026-10-18) ──────────────────
        //
        // Every outcrop is laid where all four of its side slots
        // (TradingOutpost.SideSlot, N/E/S/W — Veilstone_Economy.md §3.1) are
        // buildable ground of the outcrop's own territory, by the same terrain
        // and passability test the live placement runs. The four footprints
        // are then RESERVED, so no node laid afterwards takes one of them.

        /// <summary>Post footprints reserved around laid outcrops: (min.x, min.z, max.x, max.z).</summary>
        private static readonly List<float4> _reserved = new List<float4>();
        /// <summary>While set, a veilstone site must have room for its four posts.</summary>
        private static bool _needPostRoom;
        /// <summary>While set, BandOf falls back to the edge inset (the relaxed post-room pass).</summary>
        private static bool _relaxBand;
        /// <summary>Why post-room checks failed for the outcrop being placed (diagnostic for the warning).</summary>
        private static readonly int[] _why = new int[7];
        /// <summary>Territories whose outcrop was laid without post room, for the warning.</summary>
        private static readonly List<string> _noPostRoomAt = new List<string>();

        /// <summary>Float slack so a footprint flush against another counts as clear.</summary>
        private const float FlushSlack = 0.05f;

        private static bool SiteKeepsPostRoom(EntityManager em, float3 site, int t, int kind)
        {
            const float h = BuildGrid.ResourceNodeHalf;
            if (HitsReserved(new float2(site.x - h, site.z - h), new float2(site.x + h, site.z + h)))
                return false;
            if (kind != 2 || !_needPostRoom) return true;
            return HasPostRoom(em, site, t);
        }

        private static bool HitsReserved(float2 min, float2 max)
        {
            for (int i = 0; i < _reserved.Count; i++)
            {
                var r = _reserved[i];
                if (min.x < r.z - FlushSlack && max.x > r.x + FlushSlack
                    && min.y < r.w - FlushSlack && max.y > r.y + FlushSlack)
                    return true;
            }
            return false;
        }

        private static bool HasPostRoom(EntityManager em, float3 outcrop, int t)
        {
            int2 size = BuildingSizeConfig.GetSize(TradingOutpost.BuildingId);
            var oq = TheWaningBorder.Core.Commands.Types.BuildCommandHelper.ObstacleQuery(em);
            using var obsPos = oq.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            using var obsRad = oq.ToComponentDataArray<Radius>(Unity.Collections.Allocator.Temp);
            for (int s = 0; s < 4; s++)
            {
                float3 slot = TradingOutpost.SideSlot(outcrop, s);
                slot.y = TerrainUtility.GetHeight(slot.x, slot.z);
                TheWaningBorder.Core.Commands.Types.BuildCommandHelper.FootprintAabb(slot, size, out var min, out var max);
                if (!TheWaningBorder.Core.Commands.Types.BuildCommandHelper.InsideMapBounds(min, max)) { _why[0]++; return false; }
                // The whole footprint in the outcrop's territory (2 m samples,
                // edges included): a post across a border is not the owner's to build.
                for (float x = min.x + FlushSlack; x <= max.x; x += BuildGrid.CellSize)
                    for (float z = min.y + FlushSlack; z <= max.y; z += BuildGrid.CellSize)
                        if (RegionMap.RegionAt(math.min(x, max.x - FlushSlack), math.min(z, max.y - FlushSlack)) != t)
                        { _why[1]++; return false; }
                if (!TheWaningBorder.Core.Commands.Types.BuildCommandHelper.PassesTerrainAndGrid(
                        slot, size, min, max, isExtractor: false))
                { _why[2]++; return false; }
                for (int i = 0; i < obsPos.Length; i++)
                    if (TheWaningBorder.Core.Commands.Types.BuildCommandHelper.CircleHitsAabb(
                            obsPos[i].Position, obsRad[i].Value, min, max))
                    { _why[3]++; return false; }
                if (TheWaningBorder.Core.Commands.Types.BuildCommandHelper.OverlapsExistingBuilding(em, slot, size))
                { _why[4]++; return false; }
                var inMin = min + FlushSlack;
                var inMax = max - FlushSlack;
                if (ResourceNodeSite.OverlapsNode(em, inMin, inMax, null)) { _why[5]++; return false; }
                if (HitsReserved(min, max)) { _why[6]++; return false; }
            }
            return true;
        }

        /// <summary>
        /// The last try before an outcrop goes without room for its posts:
        /// every grid site of the territory, in a fixed order (the same on
        /// every lockstep peer), cheapest tests first. Only a site the node
        /// resolver leaves where it is counts, so the post test still holds.
        /// </summary>
        private static bool ScanForPostRoom(EntityManager em, int t, bool home, float3 centre,
            float2 min, float2 max, List<float3> mine)
        {
            float step = BuildGrid.CellSize;
            for (float z = min.y; z <= max.y; z += step)
                for (float x = min.x; x <= max.x; x += step)
                {
                    var d = BuildGrid.SnapResourceNode(new float3(x, 0f, z));
                    if (!InsideWithInset(d, t)) continue;
                    if (StartClearing.Covers(d, BuildGrid.ResourceNodeHalf)) continue;
                    if (home && !OnHomeEdge(d, centre)) continue;
                    if (TooClose(d, mine)) continue;
                    d.y = TerrainUtility.GetHeight(d.x, d.z);
                    if (!SiteKeepsPostRoom(em, d, t, 2)) continue;
                    if (!ResourceNodeSite.TryResolve(em, d, out var site)) continue;
                    if (math.distancesq(site.xz, d.xz) > 0.01f) continue;
                    Create(em, site, 2);
                    mine.Add(site);
                    return true;
                }
            return false;
        }

        private static void ReservePosts(float3 outcrop)
        {
            int2 size = BuildingSizeConfig.GetSize(TradingOutpost.BuildingId);
            for (int s = 0; s < 4; s++)
            {
                float3 slot = TradingOutpost.SideSlot(outcrop, s);
                TheWaningBorder.Core.Commands.Types.BuildCommandHelper.FootprintAabb(slot, size, out var min, out var max);
                _reserved.Add(new float4(min.x, min.y, max.x, max.y));
            }
        }

        private static void Create(EntityManager em, float3 site, int kind, byte grade)
        {
            _record?.Add((site, kind, grade));
            if (kind == 2) ReservePosts(site);
            Entity e = kind switch
            {
                0 => SupplyNode.Create(em, site),
                1 => TheWaningBorder.Bootstrap.IronDepositBootstrap.SpawnQuotaNode(em, site, IronDepositUnits),
                _ => VeilstoneOutcropping.Create(em, site, VeilstonePerOutcrop),
            };
            if (e != Entity.Null && em.Exists(e))
            {
                if (em.HasComponent<NodePurity>(e)) em.SetComponentData(e, new NodePurity { Grade = grade });
                else em.AddComponentData(e, new NodePurity { Grade = grade });
                if (grade <= 2) _dealt[grade]++;
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
                    if (!SiteKeepsPostRoom(em, site, t, kind)) continue;

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
