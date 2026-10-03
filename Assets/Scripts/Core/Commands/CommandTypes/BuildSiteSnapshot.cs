// BuildSiteSnapshot.cs
// One read of "what stands where" per simulation tick, for SITE SEARCHES.
//
// WHY THIS EXISTS (2026-09-25 AI perf pass)
//
// The AI's site search tries up to ~3,000 candidate positions for one building
// and every candidate used to re-read the world: OverlapsExistingBuilding and
// IsValidBuildPosition each copied every building's transform and entity out of
// the world (and the validator every obstacle too, plus a `new float3[5]`), and
// the extractor node gate created and disposed three entity queries. That is
// O(candidates x buildings) array copies inside one think tick — the whole
// "AI think" spike.
//
// This snapshot copies the building and obstacle lists ONCE, buckets them into
// a coarse spatial hash, and answers the same geometric questions against it.
// It is keyed on the world, the sim clock and the building/obstacle counts, so
// a second search in the same tick reuses it and anything that creates or
// destroys a building invalidates it. Placements the caller makes during the
// tick are appended with NotePlaced, so a think that places two buildings never
// hands the second one the first one's ground.
//
// The verdicts are the SAME rules as BuildCommandHelper: the shared stages
// (crust, bounds, terrain, passability, the AABB and circle tests) live there
// and both paths call them in the same order. Only the building/obstacle LISTS
// come from here. The router still re-validates live on execution, so a stale
// snapshot can at worst propose a site the router refuses — never place an
// illegal building.
//
// THE HALL RULES (docs/Design/Regions.md §2, 2026-09-26). A claim candidate
// must pass the router's CheckPlaceBuilding in the same order: the territory
// gate WITH the adjacency rule (TerritoryOwnership.TerritoryRefusal — cheap,
// static arrays, so read live rather than copied here), one Hall per
// territory (HallCapReached below, one Hall scan per tick), then the worker
// rule. The worker rule is not a property of the SITE — it is satisfied by
// walking a worker there — so it is the caller's job (SimpleAISystem's
// EnsureClaimWorkerOnSite), not this snapshot's.
//
// Host-side decision helper: it reads replicated state only, and nothing it
// returns is ever written into the simulation except through CommandRouter.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Core.Commands.Types
{
    public sealed class BuildSiteSnapshot
    {
        // ── Flags per building ────────────────────────────────────────────
        public const byte FlagGathererHut = 1;
        /// <summary>A wall piece (hub, segment, gate, wall tower). Excluded
        /// from SPACING tests — a diagonal curtain's AABB is mostly empty
        /// ground — but never from the overlap test.</summary>
        public const byte FlagWall = 2;

        /// <summary>Spatial-hash cell edge, metres. Coarse on purpose: the
        /// questions asked are "anything within 20-30 m", and a 16 m cell
        /// keeps a 3x3-5x5 block of cells per query.</summary>
        private const float CellSize = 16f;

        private static BuildSiteSnapshot _shared;

        // ── Key ───────────────────────────────────────────────────────────
        private Unity.Entities.World _world;
        private double _time;
        private int _buildingCount, _obstacleCount;

        // ── Buildings ─────────────────────────────────────────────────────
        private int _bCount;          // total, including NotePlaced extras
        private int _bBucketed;       // the first _bBucketed are in the hash
        private float2[] _bMin = new float2[64], _bMax = new float2[64], _bCtr = new float2[64];
        private byte[] _bFlags = new byte[64];
        private float _bMaxHalf;      // widest half-extent, for query padding

        // ── Obstacles ─────────────────────────────────────────────────────
        private int _oCount;
        private float3[] _oPos = new float3[64];
        private float[] _oRad = new float[64];
        private Entity[] _oEnt = new Entity[64];
        private float _oMaxRad;

        // ── Spatial hash (centre-bucketed, counting-sorted) ───────────────
        private float2 _origin;
        private int _gw, _gh;
        private int[] _bCellStart = new int[1], _bCellItems = new int[64];
        private int[] _oCellStart = new int[1], _oCellItems = new int[64];

        // ── Lazily read per-kind extractor data ──────────────────────────
        private sealed class NodeSet
        {
            public bool Loaded;
            public LocalTransform[] Nodes = System.Array.Empty<LocalTransform>();
            public int NodeCount;
            public LocalTransform[] Taken = System.Array.Empty<LocalTransform>();
            public int TakenCount;
        }
        private readonly Dictionary<string, NodeSet> _nodeSets = new Dictionary<string, NodeSet>();

        // ── Hall regions (hall cap) ──────────────────────────────────────
        private bool _hallsLoaded;
        private bool[] _hallRegion = System.Array.Empty<bool>();

        /// <summary>Building count at capture (NotePlaced extras excluded) —
        /// a cheap "did anything get built or razed" version for callers
        /// that memoise failed searches.</summary>
        public int BuildingCount => _buildingCount;

        /// <summary>
        /// The snapshot for this world at this sim tick, recaptured when the
        /// clock moved or the building / obstacle population changed.
        /// </summary>
        public static BuildSiteSnapshot Current(EntityManager em)
        {
            _shared ??= new BuildSiteSnapshot();
            _shared.EnsureFresh(em);
            return _shared;
        }

        /// <summary>Drop the cached read so the next <see cref="Current"/>
        /// recaptures. For callers that changed the world through a path the
        /// count key cannot see.</summary>
        public static void Invalidate()
        {
            if (_shared != null) _shared._world = null;
        }

        private void EnsureFresh(EntityManager em)
        {
            var world = em.World;
            double t = world.Time.ElapsedTime;
            var bq = BuildCommandHelper.BuildingQuery(em);
            var oq = BuildCommandHelper.ObstacleQuery(em);
            int bc = bq.CalculateEntityCount();
            int oc = oq.CalculateEntityCount();
            if (ReferenceEquals(_world, world) && _time == t
                && bc == _buildingCount && oc == _obstacleCount)
                return;

            _world = world;
            _time = t;
            _buildingCount = bc;
            _obstacleCount = oc;
            Capture(em, bq, oq);
        }

        private void Capture(EntityManager em, EntityQuery bq, EntityQuery oq)
        {
            // Buildings.
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                _bCount = 0;
                _bMaxHalf = 0f;
                EnsureBuildingCapacity(ents.Length + 8);
                for (int i = 0; i < ents.Length; i++)
                {
                    BuildCommandHelper.BuildingAabb(em, ents[i], xfs[i].Position,
                        out float2 mn, out float2 mx);
                    byte flags = 0;
                    if (em.HasComponent<GathererHutTag>(ents[i])) flags |= FlagGathererHut;
                    if (em.HasComponent<WallTag>(ents[i]) || em.HasComponent<WallHubTag>(ents[i])
                        || em.HasComponent<WallSegmentTag>(ents[i]))
                        flags |= FlagWall;
                    AddBuilding(mn, mx, flags);
                }
            }

            // Obstacles.
            using (var ents = oq.ToEntityArray(Allocator.Temp))
            using (var xfs = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var rad = oq.ToComponentDataArray<Radius>(Allocator.Temp))
            {
                _oCount = 0;
                _oMaxRad = 0f;
                if (_oPos.Length < ents.Length)
                {
                    int n = math.max(ents.Length, _oPos.Length * 2);
                    System.Array.Resize(ref _oPos, n);
                    System.Array.Resize(ref _oRad, n);
                    System.Array.Resize(ref _oEnt, n);
                }
                for (int i = 0; i < ents.Length; i++)
                {
                    _oPos[_oCount] = xfs[i].Position;
                    _oRad[_oCount] = rad[i].Value;
                    _oEnt[_oCount] = ents[i];
                    _oMaxRad = math.max(_oMaxRad, rad[i].Value);
                    _oCount++;
                }
            }

            BuildHash();

            foreach (var kv in _nodeSets) kv.Value.Loaded = false;
            _hallsLoaded = false;
        }

        private void EnsureBuildingCapacity(int n)
        {
            if (_bMin.Length >= n) return;
            n = math.max(n, _bMin.Length * 2);
            System.Array.Resize(ref _bMin, n);
            System.Array.Resize(ref _bMax, n);
            System.Array.Resize(ref _bCtr, n);
            System.Array.Resize(ref _bFlags, n);
        }

        private void AddBuilding(float2 mn, float2 mx, byte flags)
        {
            EnsureBuildingCapacity(_bCount + 1);
            _bMin[_bCount] = mn;
            _bMax[_bCount] = mx;
            _bCtr[_bCount] = (mn + mx) * 0.5f;
            _bFlags[_bCount] = flags;
            _bMaxHalf = math.max(_bMaxHalf, math.cmax((mx - mn) * 0.5f));
            _bCount++;
        }

        private void BuildHash()
        {
            // Bounds over everything bucketed.
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            for (int i = 0; i < _bCount; i++) { lo = math.min(lo, _bCtr[i]); hi = math.max(hi, _bCtr[i]); }
            for (int i = 0; i < _oCount; i++)
            {
                var p = new float2(_oPos[i].x, _oPos[i].z);
                lo = math.min(lo, p); hi = math.max(hi, p);
            }
            if (lo.x > hi.x) { lo = float2.zero; hi = float2.zero; }
            _origin = lo;
            _gw = math.max(1, (int)((hi.x - lo.x) / CellSize) + 1);
            _gh = math.max(1, (int)((hi.y - lo.y) / CellSize) + 1);
            int cells = _gw * _gh;

            Bucket(cells, _bCount, i => _bCtr[i], ref _bCellStart, ref _bCellItems);
            Bucket(cells, _oCount, i => new float2(_oPos[i].x, _oPos[i].z), ref _oCellStart, ref _oCellItems);
            _bBucketed = _bCount;
        }

        private void Bucket(int cells, int count, System.Func<int, float2> posOf,
            ref int[] start, ref int[] items)
        {
            if (start.Length < cells + 1) start = new int[cells + 1];
            else System.Array.Clear(start, 0, cells + 1);
            if (items.Length < count) items = new int[math.max(count, items.Length * 2)];

            for (int i = 0; i < count; i++) start[CellOf(posOf(i)) + 1]++;
            for (int c = 0; c < cells; c++) start[c + 1] += start[c];
            // Fill using a moving cursor per cell (reuses start as the base,
            // so walk a copy of the offsets).
            if (_cursor.Length < cells) _cursor = new int[cells];
            System.Array.Copy(start, _cursor, cells);
            for (int i = 0; i < count; i++)
            {
                int c = CellOf(posOf(i));
                items[_cursor[c]++] = i;
            }
        }
        private int[] _cursor = new int[1];

        private int CellOf(float2 p)
        {
            int cx = math.clamp((int)((p.x - _origin.x) / CellSize), 0, _gw - 1);
            int cz = math.clamp((int)((p.y - _origin.y) / CellSize), 0, _gh - 1);
            return cz * _gw + cx;
        }

        private void CellRange(float2 mn, float2 mx, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = math.clamp((int)math.floor((mn.x - _origin.x) / CellSize), 0, _gw - 1);
            z0 = math.clamp((int)math.floor((mn.y - _origin.y) / CellSize), 0, _gh - 1);
            x1 = math.clamp((int)math.floor((mx.x - _origin.x) / CellSize), 0, _gw - 1);
            z1 = math.clamp((int)math.floor((mx.y - _origin.y) / CellSize), 0, _gh - 1);
        }

        // ─────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Footprint overlap against every building, with the footprint grown
        /// by <paramref name="gap"/> metres on each side. Gap 0 is exactly
        /// <see cref="BuildCommandHelper.OverlapsExistingBuilding"/>.
        /// <paramref name="ignoreWalls"/> drops wall pieces from the test —
        /// for SPACING only; a real overlap test must pass false.
        /// </summary>
        public bool Overlaps(float3 position, int2 size, float gap, bool ignoreWalls)
        {
            BuildCommandHelper.FootprintAabb(position, size, out float2 mn, out float2 mx);
            mn -= gap; mx += gap;
            return OverlapsAabb(mn, mx, ignoreWalls);
        }

        private bool OverlapsAabb(float2 mn, float2 mx, bool ignoreWalls)
        {
            // Bucketed by centre, so pad the cell range by the widest
            // building's half-extent.
            CellRange(mn - _bMaxHalf, mx + _bMaxHalf, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * _gw + x;
                    for (int k = _bCellStart[c]; k < _bCellStart[c + 1]; k++)
                        if (Hit(_bCellItems[k], mn, mx, ignoreWalls)) return true;
                }
            for (int i = _bBucketed; i < _bCount; i++)
                if (Hit(i, mn, mx, ignoreWalls)) return true;
            return false;
        }

        private bool Hit(int i, float2 mn, float2 mx, bool ignoreWalls)
        {
            if (ignoreWalls && (_bFlags[i] & FlagWall) != 0) return false;
            var omn = _bMin[i]; var omx = _bMax[i];
            return mn.x < omx.x && mx.x > omn.x && mn.y < omx.y && mx.y > omn.y;
        }

        /// <summary>
        /// Any building whose CENTRE is within <paramref name="radius"/> —
        /// the AI's layout spacing. <paramref name="gHutRadius"/> is the extra
        /// hut-to-hut keep-out (0 to skip). Wall pieces never count.
        /// </summary>
        public bool AnyCentreWithin(float3 p, float radius, float gHutRadius)
        {
            float r = math.max(radius, gHutRadius);
            float r2 = radius * radius, g2 = gHutRadius * gHutRadius;
            var c2 = new float2(p.x, p.z);
            CellRange(c2 - r, c2 + r, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * _gw + x;
                    for (int k = _bCellStart[c]; k < _bCellStart[c + 1]; k++)
                        if (CentreHit(_bCellItems[k], c2, r2, g2)) return true;
                }
            for (int i = _bBucketed; i < _bCount; i++)
                if (CentreHit(i, c2, r2, g2)) return true;
            return false;
        }

        private bool CentreHit(int i, float2 p, float r2, float g2)
        {
            if ((_bFlags[i] & FlagWall) != 0) return false;
            float2 d = _bCtr[i] - p;
            float d2 = math.dot(d, d);
            if (d2 < r2) return true;
            return g2 > 0f && (_bFlags[i] & FlagGathererHut) != 0 && d2 < g2;
        }

        /// <summary>Gatherer's Huts standing (or noted this tick) — the hut
        /// spread's "how many already stand" count.</summary>
        public int CountGathererHuts()
        {
            int n = 0;
            for (int i = 0; i < _bCount; i++)
                if ((_bFlags[i] & FlagGathererHut) != 0) n++;
            return n;
        }

        /// <summary>
        /// <see cref="BuildCommandHelper.IsValidBuildPosition(EntityManager, float3, int2, string)"/>
        /// against the snapshot's building and obstacle lists. Same stages,
        /// same order, same verdict for the same world.
        /// </summary>
        public bool IsValidBuildPosition(EntityManager em, float3 position, int2 size, string buildingId)
        {
            if (!BuildCommandHelper.PassesCrustRule(em, position, buildingId)) return false;

            BuildCommandHelper.FootprintAabb(position, size, out float2 mn, out float2 mx);
            if (!BuildCommandHelper.InsideMapBounds(mn, mx)) return false;

            if (OverlapsAabb(mn, mx, ignoreWalls: false)) return false;
            // Walls at their real, rotated footprint — the live validator's
            // stage 1a (BuildCommandHelper.OverlapsWall).
            if (BuildCommandHelper.OverlapsWall(mn, mx)) return false;

            var ownNode = buildingId != null ? TerritoryOwnership.RequiredNodeFor(buildingId) : null;

            // Nothing on a resource node but its own extractor — the live
            // validator's stage 1b, same test.
            if (TheWaningBorder.Entities.ResourceNodeSite.OverlapsNode(em, mn, mx, ownNode)) return false;

            CellRange(mn - _oMaxRad, mx + _oMaxRad, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * _gw + x;
                    for (int k = _oCellStart[c]; k < _oCellStart[c + 1]; k++)
                    {
                        int i = _oCellItems[k];
                        if (!BuildCommandHelper.CircleHitsAabb(_oPos[i], _oRad[i], mn, mx)) continue;
                        // The node this extractor exists to stand on.
                        if (ownNode != null && em.Exists(_oEnt[i])
                            && em.HasComponent(_oEnt[i], ownNode.Value)) continue;
                        return false;
                    }
                }

            return BuildCommandHelper.PassesTerrainAndGrid(position, size, mn, mx, ownNode != null);
        }

        /// <summary><see cref="TerritoryOwnership.OnFreeNodeFor"/> against the
        /// snapshot's node / extractor lists.</summary>
        public bool OnFreeNodeFor(EntityManager em, string buildingId, float x, float z)
        {
            var set = NodesFor(em, buildingId);
            if (set == null) return true;              // not an extractor
            if (set.NodeCount == 0) return true;       // unseeded map — stay buildable
            return TerritoryOwnership.SnapAmong(buildingId, new float3(x, 0f, z),
                set.Nodes, set.NodeCount, set.Taken, set.TakenCount, out _);
        }

        /// <summary><see cref="TerritoryOwnership.TrySnapToNode"/> against the
        /// snapshot's node / extractor lists.</summary>
        public bool TrySnapToNode(EntityManager em, string buildingId, float3 pos, out float3 snapped)
        {
            snapped = pos;
            var set = NodesFor(em, buildingId);
            if (set == null) return false;
            return TerritoryOwnership.SnapAmong(buildingId, pos,
                set.Nodes, set.NodeCount, set.Taken, set.TakenCount, out snapped);
        }

        private NodeSet NodesFor(EntityManager em, string buildingId)
        {
            var required = TerritoryOwnership.RequiredNodeFor(buildingId);
            if (required == null) return null;
            if (!_nodeSets.TryGetValue(buildingId, out var set))
                _nodeSets[buildingId] = set = new NodeSet();
            if (set.Loaded) return set;

            using (var nodes = TerritoryOwnership.TagWithTransform(em, required.Value)
                       .ToComponentDataArray<LocalTransform>(Allocator.Temp))
                set.NodeCount = CopyInto(ref set.Nodes, nodes, 0);

            set.TakenCount = 0;
            var tag = TerritoryOwnership.ExtractorTagOf(buildingId);
            if (tag != null)
                using (var taken = TerritoryOwnership.TagWithTransform(em, tag.Value)
                           .ToComponentDataArray<LocalTransform>(Allocator.Temp))
                    set.TakenCount = CopyInto(ref set.Taken, taken, 8);
            set.Loaded = true;
            return set;
        }

        private static int CopyInto(ref LocalTransform[] into, NativeArray<LocalTransform> from, int spare)
        {
            if (into.Length < from.Length + spare) into = new LocalTransform[from.Length + spare + 8];
            for (int i = 0; i < from.Length; i++) into[i] = from[i];
            return from.Length;
        }

        /// <summary><see cref="TerritoryOwnership.HallCapReached"/> against the
        /// snapshot (one Hall scan per tick, not one per candidate).</summary>
        public bool HallCapReached(EntityManager em, float x, float z)
        {
            if (!RegionMap.Ready) return false;
            int here = RegionMap.RegionAt(x, z);
            if (here == RegionMap.None) return false;
            if (!_hallsLoaded)
            {
                int n = math.max(RegionMap.Count, here + 1);
                if (_hallRegion.Length < n) _hallRegion = new bool[n];
                else System.Array.Clear(_hallRegion, 0, _hallRegion.Length);
                using (var xfs = TerritoryOwnership.HallQuery(em)
                           .ToComponentDataArray<LocalTransform>(Allocator.Temp))
                    for (int i = 0; i < xfs.Length; i++)
                    {
                        int r = RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z);
                        if (r >= 0 && r < _hallRegion.Length) _hallRegion[r] = true;
                    }
                _hallsLoaded = true;
            }
            return here < _hallRegion.Length && _hallRegion[here];
        }

        /// <summary>
        /// Record a building the caller just placed (or queued), so later
        /// searches in the same tick treat its ground as taken — in lockstep
        /// the foundation only appears two ticks later.
        /// </summary>
        public void NotePlaced(EntityManager em, string buildingId, float3 position, int2 size)
        {
            BuildCommandHelper.FootprintAabb(position, size, out float2 mn, out float2 mx);
            AddBuilding(mn, mx, buildingId == "GatherersHut" ? FlagGathererHut : (byte)0);

            var set = NodesFor(em, buildingId);   // loads it if this tick has not yet
            if (set != null)
            {
                if (set.Taken.Length <= set.TakenCount)
                    System.Array.Resize(ref set.Taken, set.TakenCount + 8);
                set.Taken[set.TakenCount++] = LocalTransform.FromPosition(position);
            }
            // The capital (HallTag) is what the hall-region scan counts.
            if (buildingId == "Fortress" && _hallsLoaded && RegionMap.Ready)
            {
                int r = RegionMap.RegionAt(position.x, position.z);
                if (r >= 0 && r < _hallRegion.Length) _hallRegion[r] = true;
            }
        }
    }
}
