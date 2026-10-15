// RoadNetwork.cs
// The procedural road network (docs/Design/Roads.md): sites (outcrops,
// supply spots, curse nodes, buildings) joined per territory by a
// relative-neighbourhood graph, routed over the nav grid, and rasterised
// into _TWB_RoadMask / _TWB_RoadDir for the terrain shader. WHERE ground is
// road or plaza is decided here; WHAT it looks like (earthen vs culture
// paving) is decided per pixel in TWBTerrainOverlays.hlsl.
//
// Presentation only: reads simulation state twice a second, never writes
// it (nothing in the sim reads a road). A poll whose site set is unchanged
// does no graph or routing work at all. Every plaza and every road is a STATE with a strength that eases
// toward 1 while its site / edge exists and back to 0 after it is gone —
// fast in (a path wears in over ~2.5 s), slow out (the grass takes ~75 s
// to reclaim it). The mask is re-rasterised only while something is
// animating, at a rate matched to the fastest active fade; when the
// network is still, nothing runs per frame. No per-tick CreateEntityQuery:
// every query is a CachedEntityQuery with its ComponentType[] hoisted.
//
// NO ROUTE IS EVER PAID FOR IN ONE FRAME (2026-10-10). Routing is a queue
// drained against a per-frame time budget (routeBudgetMs), and each A* is
// resumable (RoadGraph.Search), so a 20 000-expansion failure is spread over
// several frames instead of being an 8 ms stall. The 8-player Veilmarch match
// that motivated this spent 130 s of main thread in RoadNetwork.Sync: a
// fading-out site counted as "changed" on EVERY sync for its whole 75 s fade,
// so every failed route near a razed building was re-searched — and failed
// again at the cap — every second or so. Now only a site that VANISHES or
// MOVES re-arms failures near it, once, and a pair that keeps failing waits
// out a doubling back-off (failRetrySeconds .. failRetryMaxSeconds).

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Influence;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.World.Roads
{
    [DefaultExecutionOrder(2001)]   // after InfluenceMaskTexture sets _TWB_MaskST
    public sealed class RoadNetwork : MonoBehaviour
    {
        private RoadNetworkConfig _cfg;
        private RoadNetworkConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RoadNetworkConfig>());

        // ─── Auto-mount on gameplay scenes ────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Edit mode: these hooks subscribe in Play and stay subscribed after it
            // (no domain reload on exit), so an editor scene load would edit and
            // dirty the authored scene. Only ever act in Play / a player.
            if (!UnityEngine.Application.isPlaying) return;
            if (!MapRegistry.IsGameplayScene(scene.name)) return;
            if (Object.FindFirstObjectByType<RoadNetwork>() != null) return;
            new GameObject("[Road Network]").AddComponent<RoadNetwork>();
        }

        // ─── Sites & edges ────────────────────────────────────────────────
        private struct Site
        {
            public Entity Entity;
            public Vector2 Pos;        // world XZ
            public float Disc;         // plaza / node disc radius, metres
            public bool Finished;      // stone-eligible (complete building, or a node)
            public bool IsBuilding;
            public Faction Faction;    // buildings only
            public int Region;         // RegionMap.RawRegionAt, or RegionMap.None
            public Vector2 Size;       // buildings: footprint, metres
            public bool Pad;           // buildings: owner has chosen Alanthor — lay a culture pad
        }

        /// <summary>A plaza with a life of its own: the last-known site data
        /// (the entity may be gone) and an eased strength.</summary>
        private sealed class SiteState
        {
            public Site Data;
            public float Strength;
            public bool Live;
            /// <summary>Live at the previous sync: a live→dead flip is the
            /// one moment a vanished site can open a way.</summary>
            public bool WasLive;
        }

        /// <summary>A road with a life of its own.</summary>
        private sealed class EdgeState
        {
            public Entity A, B;
            public List<Vector2> Route;   // world path; null = unroutable (or not yet routed)
            public bool NeedsRoute;       // routing deferred to a later poll (budget)
            public Site SiteA, SiteB;     // endpoints as last seen, for deferred routing
            public float HalfWidthMetres;
            public bool Finished;
            public float Strength;
            public bool Live;
            /// <summary>This route's slice of the raster's point list
            /// (rebuilt only when a route appears or goes).</summary>
            public int PointStart, PointCount;
        }

        // Buildings with a footprint; walls, gates, towers, chapels and the
        // curse's own structures are not destinations (Roads.md §2).
        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<BuildingSize>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<WallTag>(), ComponentType.Exclude<WallSegmentTag>(),
            ComponentType.Exclude<WallHubTag>(), ComponentType.Exclude<WallGateTag>(),
            ComponentType.Exclude<WallTowerTag>(), ComponentType.Exclude<ChapelTag>(),
            ComponentType.Exclude<ChapelSmallTag>(), ComponentType.Exclude<BorderMainNodeTag>(),
            ComponentType.Exclude<SmallNodeTag>(),
        };
        static readonly ComponentType[] QT_Veilstone = { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Iron      = { ComponentType.ReadOnly<IronMineTag>(),            ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Veilsteel = { ComponentType.ReadOnly<VeilsteelDepositTag>(),    ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Supply    = { ComponentType.ReadOnly<SupplyNodeTag>(),          ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Well      = { ComponentType.ReadOnly<BorderMainNodeTag>(),      ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Pocket    = { ComponentType.ReadOnly<SmallNodeTag>(),           ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Grid      = { ComponentType.ReadOnly<NavGridSingleton>() };
        static CachedEntityQuery QC_Buildings, QC_Veilstone, QC_Iron, QC_Veilsteel, QC_Supply, QC_Well, QC_Pocket, QC_Grid;

        private readonly List<Site> _sites = new List<Site>();                       // live, this poll
        private readonly Dictionary<Entity, SiteState> _siteStates = new Dictionary<Entity, SiteState>();
        private readonly Dictionary<long, EdgeState> _edgeStates = new Dictionary<long, EdgeState>();
        private readonly List<Entity> _deadSites = new List<Entity>();
        private readonly List<long> _deadEdges = new List<long>();
        private float _nextPoll;
        private float _nextRaster;
        private bool _dirty;            // topology or attributes changed since the last raster
        private bool _dirDirty;         // routes changed: the direction mask must be rewritten too
        private bool _animating;
        private bool _synced;           // the first sync snaps: the map's trails pre-exist
        private int _routedThisSync;    // routes finished this frame (perf detail)
        private bool _topologyRan;      // whether the last poll rebuilt the graph (perf detail)

        // Routing queue (see the header): keys waiting for a route, and the
        // one search in flight. A key may be queued twice; the stale copy is
        // skipped because its edge no longer NeedsRoute.
        private readonly Queue<long> _routeQueue = new Queue<long>();
        private readonly RoadGraph.Search _search = new RoadGraph.Search();
        private bool _searchActive;
        private long _searchKey;
        private RoadCellTest _searchTest;
        private Site _searchA, _searchB;
        private float _searchCorridor;
        private readonly System.Diagnostics.Stopwatch _routeWatch = new System.Diagnostics.Stopwatch();
        /// <summary>Expansions per step between budget checks (~0.1 ms).</summary>
        private const int SearchChunk = 256;
        /// <summary>The first sync's trails pre-exist; they route at the
        /// startup budget until the queue first runs dry.</summary>
        private bool _startupRouting = true;
        /// <summary>A route appeared or went: the raster's point list is stale.</summary>
        private bool _pointsDirty = true;
        // Once-a-minute summary for Perf.log: what routing cost, and whether
        // it is keeping up (a queue that never drains is the old symptom).
        private int _sumSearches, _sumFound, _sumFailed;
        private double _sumRouteMs;
        private float _nextSummary;
        /// <summary>Each faction's completed culture, read once per poll.</summary>
        private readonly byte[] _cultureOf = new byte[32];
        // Topology change detection (Roads.md: "rebuilt on construction
        // events, never per frame"): an order-independent hash of every
        // site's identity, position, footprint, finished flag and region.
        // A poll whose hash matches the last sync, with no deferred routes
        // waiting, does nothing at all.
        private ulong _siteHash;
        private ulong _lastSyncHash;
        private int _lastSyncRegionVersion = -1;

        // ─── Mask ─────────────────────────────────────────────────────────
        private Texture2D _mask;        // R coverage, G finished, B plaza, A lateral
        private Texture2D _dirMask;     // RG road tangent
        private NativeArray<float> _nearest;      // per-texel nearest-road claim (raster scratch)
        private NativeArray<Color32> _maskBuf;    // job output; copied into the textures when complete
        private NativeArray<Color32> _dirBuf;
        private Unity.Jobs.JobHandle _rasterJob;
        private bool _rasterPending;
        private bool _pendingDir;
        private NativeList<DiscCmd> _discCmds;
        private NativeList<SquareCmd> _squareCmds;
        private NativeList<PolyCmd> _polyCmds;
        private NativeList<float2> _polyPoints;
        private int _res;
        private Vector2 _worldMin, _worldSize;
        private bool _globalsBound;

        // ─── Scratch ──────────────────────────────────────────────────────
        private readonly RoadGraph.Scratch _scratch = new RoadGraph.Scratch();
        private readonly List<Vector2> _pts = new List<Vector2>();
        private readonly List<int> _ptSite = new List<int>();
        private readonly List<int2> _edges = new List<int2>();
        private readonly List<Vector2> _path = new List<Vector2>();
        private readonly List<Vector2> _pathScratch = new List<Vector2>();
        private readonly List<Vector2> _waypoints = new List<Vector2>();
        private readonly List<Vector2> _curve = new List<Vector2>();
        private readonly HashSet<long> _liveEdgeKeys = new HashSet<long>();
        private int[] _uf;
        private readonly Dictionary<Faction, int> _firstRegionOfFaction = new Dictionary<Faction, int>();
        private List<int>[] _netMembers = new List<int>[0];
        private readonly RoadGraph.RngScratch _rngScratch = new RoadGraph.RngScratch();
        // A network's relative-neighbourhood graph depends only on its
        // members' identities and positions, so it is cached under that
        // hash: a change in one territory recomputes that territory alone.
        private struct EdgePair { public Entity A, B; }
        private readonly Dictionary<ulong, List<EdgePair>> _netEdgeCache = new Dictionary<ulong, List<EdgePair>>();
        private readonly HashSet<ulong> _netEdgeUsed = new HashSet<ulong>();
        private readonly List<ulong> _netEdgeStale = new List<ulong>();
        private readonly Stack<List<EdgePair>> _edgeListPool = new Stack<List<EdgePair>>();
        // Routes that failed, and the box their search covered. A pair that
        // failed is not re-searched when its edge flickers back; it is
        // re-armed only when a site vanishes or moves inside that box (the
        // only change this presentation layer can see that might open a way —
        // a new site cannot: the search only exempts the edge's own two
        // discs), and no sooner than its back-off allows.
        private struct FailedRoute { public Vector2 Min, Max; public int Fails; public float RetryAt; }
        private readonly Dictionary<long, FailedRoute> _failedRoutes = new Dictionary<long, FailedRoute>();
        private readonly List<long> _failedRearm = new List<long>();
        private readonly List<Vector2> _changedSitePos = new List<Vector2>();
        // RegionMap.RawRegionAt walks every authored polygon; a site does not
        // move, so its territory is asked once per entity, not per poll.
        private readonly Dictionary<Entity, int> _regionOf = new Dictionary<Entity, int>();
        private int _regionCacheVersion = -1;

        void Update()
        {
            if (Cfg == null) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (!RegionMap.Ready || PlayerInfluenceMap.WorldSize.x <= 0f) return;
            var em = world.EntityManager;
            var gridQ = QC_Grid.Get(em, QT_Grid);
            if (gridQ.IsEmptyIgnoreFilter) return;

            var nav = gridQ.GetSingleton<NavGridSingleton>();
            var grid = new RoadGraph.Grid { Width = nav.Width, Height = nav.Height, CellSize = nav.CellSize, Origin = nav.Origin };

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + Cfg.pollSeconds;
                double t0 = Time.realtimeSinceStartupAsDouble;
                _topologyRan = false;
                GatherSites(em);
                if (!_synced || _siteHash != _lastSyncHash || _lastSyncRegionVersion != RegionMap.Version)
                {
                    SyncTopology();
                    _lastSyncHash = _siteHash;
                    _lastSyncRegionVersion = RegionMap.Version;
                    _topologyRan = true;
                }
                double ms = (Time.realtimeSinceStartupAsDouble - t0) * 1000.0;
                if (_topologyRan && ms >= TheWaningBorder.Core.Diagnostics.PerfSpikeLog.DefaultThresholdMs)
                    TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RoadNetwork.Sync", ms,
                        $"sites={_sites.Count} queued={_routeQueue.Count} failed={_failedRoutes.Count}");
            }

            PumpRoutes(grid);
            if (Time.unscaledTime >= _nextSummary)
            {
                _nextSummary = Time.unscaledTime + 60f;
                int routed = 0;
                foreach (var kv in _edgeStates) if (kv.Value.Route != null) routed++;
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RoadNetwork.Minute", _sumRouteMs,
                    $"searches={_sumSearches} found={_sumFound} failed={_sumFailed} sites={_sites.Count} " +
                    $"edges={_edgeStates.Count} routed={routed} queued={_routeQueue.Count} failedPairs={_failedRoutes.Count}",
                    thresholdMs: 0);
                _sumSearches = _sumFound = _sumFailed = 0;
                _sumRouteMs = 0;
            }

            Animate(Time.deltaTime);

            // Last frame's raster lands here: a memcpy into the textures and
            // the upload — the only main-thread cost of the mask now.
            if (_rasterPending)
            {
                if (!_rasterJob.IsCompleted) return;
                _rasterJob.Complete();
                _rasterPending = false;
                double u0 = Time.realtimeSinceStartupAsDouble;
                _mask.SetPixelData(_maskBuf, 0);
                _mask.Apply(false, false);
                if (_pendingDir)
                {
                    _dirMask.SetPixelData(_dirBuf, 0);
                    _dirMask.Apply(false, false);
                    _pendingDir = false;
                }
                BindGlobals();
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RoadNetwork.Upload",
                    (Time.realtimeSinceStartupAsDouble - u0) * 1000.0);
            }

            if (!_dirty && !_animating) return;
            if (Time.time < _nextRaster) return;
            // Raster cadence follows the fastest thing moving: a wear-in
            // needs ~12 Hz to read as continuous under the erosion, a
            // reclaim is slow enough for 2 Hz, a still network needs nothing.
            _nextRaster = Time.time + (AnyFastFade() ? Cfg.fastRasterSeconds : Cfg.slowRasterSeconds);
            double r0 = Time.realtimeSinceStartupAsDouble;
            Raster();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RoadNetwork.Schedule",
                (Time.realtimeSinceStartupAsDouble - r0) * 1000.0,
                $"sites={_siteStates.Count} edges={_edgeStates.Count}");
        }

        // ─── Site discovery ───────────────────────────────────────────────
        private int RegionOf(Entity e, Vector2 pos)
        {
            if (_regionCacheVersion != RegionMap.Version) { _regionOf.Clear(); _regionCacheVersion = RegionMap.Version; }
            if (_regionOf.TryGetValue(e, out int r)) return r;
            // Dead entities are never removed one by one; drop the cache
            // wholesale once it is mostly stale so it cannot grow all match.
            if (_regionOf.Count > 256 && _regionOf.Count > _sites.Count * 4) _regionOf.Clear();
            r = RegionMap.RawRegionAt(pos.x, pos.y);
            _regionOf[e] = r;
            return r;
        }

        private void GatherSites(EntityManager em)
        {
            _sites.Clear();
            _siteHash = 0;
            AddBuildings(em);
            AddNodes(em, QC_Veilstone.Get(em, QT_Veilstone));
            AddNodes(em, QC_Iron.Get(em, QT_Iron));
            AddNodes(em, QC_Veilsteel.Get(em, QT_Veilsteel));
            AddNodes(em, QC_Supply.Get(em, QT_Supply));
            AddNodes(em, QC_Well.Get(em, QT_Well));
            AddNodes(em, QC_Pocket.Get(em, QT_Pocket));
            AddJunctions();
        }

        /// <summary>The simulation graph's junctions (Roads.md §0) as sites, so
        /// its forks and crossroads are drawn where the caravans turn.</summary>
        private void AddJunctions()
        {
            if (!RoadGraphSystem.Built) return;
            for (int i = RoadGraphSystem.SiteCount; i < RoadGraphSystem.NodeCount; i++)
            {
                var p = RoadGraphSystem.NodePos(i);
                var pos = new Vector2(p.x, p.y);
                _sites.Add(new Site
                {
                    Entity = RoadGraphSystem.NodeEntity(i), Pos = pos, Disc = Cfg.nodeDiscRadius,
                    Finished = true, IsBuilding = false, Faction = Faction.Border,
                    Region = RegionMap.RawRegionAt(pos.x, pos.y),
                });
                _siteHash += SiteHash(_sites[_sites.Count - 1]);
            }
        }

        private void AddBuildings(EntityManager em)
        {
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            using var sizes = q.ToComponentDataArray<BuildingSize>(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            // One culture read per faction per poll, not two Hall-query copies
            // per building.
            CultureConfig.GetCompletedCultures(em, _cultureOf);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var lt = xfs[i];
                var sz = sizes[i];
                var faction = facs[i].Value;
                int fi = (int)faction;
                var pos = new Vector2(lt.Position.x, lt.Position.z);
                _sites.Add(new Site
                {
                    Entity = e, Pos = pos,
                    Disc = Mathf.Max(sz.Width, sz.Height) * 0.5f + Cfg.plazaMargin,
                    Finished = !em.HasComponent<UnderConstruction>(e),
                    IsBuilding = true,
                    Faction = faction,
                    Region = RegionOf(e, pos),
                    Size = new Vector2(sz.Width, sz.Height),
                    // THE ALANTHOR PAD (Roads.md §4b, 2026-09-30): once its
                    // owner has chosen Alanthor, every building stands on a
                    // square of Alanthor paving, its footprint plus
                    // alanthorPadMarginCells build cells on every side.
                    Pad = fi >= 0 && fi < _cultureOf.Length && _cultureOf[fi] == Cultures.Alanthor,
                });
                _siteHash += SiteHash(_sites[_sites.Count - 1]);
            }
        }

        /// <summary>Per-site term of the topology hash. Summed, so the
        /// query's entity order cannot change the total.</summary>
        private static ulong SiteHash(in Site s)
        {
            ulong h = NetMemberHash(s);
            h ^= (ulong)(uint)Mathf.RoundToInt(s.Disc * 10f) * 0xFF51AFD7ED558CCDUL;
            h ^= (ulong)(uint)(s.Region + 7) * 0xC4CEB9FE1A85EC53UL;
            h ^= (s.Finished ? 1UL : 0UL) | (s.IsBuilding ? 2UL : 0UL) | ((ulong)(uint)(int)s.Faction << 2)
               | (s.Pad ? 1UL << 40 : 0UL);
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
            return h;
        }

        /// <summary>What a network's relative-neighbourhood graph depends
        /// on: which sites, and where.</summary>
        private static ulong NetMemberHash(in Site s)
        {
            ulong h = (ulong)(uint)s.Entity.Index * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)(uint)s.Entity.Version * 0xC2B2AE3D27D4EB4FUL;
            h ^= (ulong)(uint)Mathf.RoundToInt(s.Pos.x * 10f) * 0x165667B19E3779F9UL;
            h ^= (ulong)(uint)Mathf.RoundToInt(s.Pos.y * 10f) * 0xD6E8FEB86659FD93UL;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
            return h;
        }

        private void AddNodes(EntityManager em, EntityQuery q)
        {
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                // A pocket / well is also a building in ECS terms but is
                // gathered here, as a node, so it never merges territories.
                var lt = em.GetComponentData<LocalTransform>(e);
                var pos = new Vector2(lt.Position.x, lt.Position.z);
                _sites.Add(new Site
                {
                    Entity = e, Pos = pos, Disc = Cfg.nodeDiscRadius,
                    Finished = true, IsBuilding = false, Faction = Faction.Border,
                    Region = RegionOf(e, pos),
                });
                _siteHash += SiteHash(_sites[_sites.Count - 1]);
            }
        }

        // ─── Topology ─────────────────────────────────────────────────────
        /// <summary>
        /// Reconcile the live site set with the site / edge states: new
        /// things start at strength 0 and go live, vanished things stay (at
        /// their last-known shape) and go dead, attribute changes on live
        /// things mark the raster dirty. Edges are the relative-neighbourhood
        /// graph of the LIVE sites, per merged territory (Roads.md §3).
        /// </summary>
        private void SyncTopology()
        {
            // Sites. A site that MOVES or VANISHES is a place where a failed
            // route may have become routable — once, at the flip. (Appearing
            // cannot open a way, and a site fading out for 75 s is not a new
            // change on every sync; counting it as one was the re-search loop.)
            _changedSitePos.Clear();
            foreach (var kv in _siteStates) { kv.Value.WasLive = kv.Value.Live; kv.Value.Live = false; }
            for (int i = 0; i < _sites.Count; i++)
            {
                var s = _sites[i];
                if (_siteStates.TryGetValue(s.Entity, out var st))
                {
                    bool moved = (st.Data.Pos - s.Pos).sqrMagnitude > 0.01f;
                    if (st.Data.Finished != s.Finished || st.Data.Region != s.Region || moved)
                        _dirty = true;
                    if (moved) { _changedSitePos.Add(s.Pos); _changedSitePos.Add(st.Data.Pos); }
                    if (!st.WasLive) _dirty = true;   // resurrected while fading out
                    st.Data = s; st.Live = true;
                }
                else
                {
                    _siteStates[s.Entity] = new SiteState { Data = s, Strength = _synced ? 0f : 1f, Live = true };
                    _dirty = true;
                }
            }
            foreach (var kv in _siteStates)
                if (kv.Value.WasLive && !kv.Value.Live)
                {
                    _dirty = true;
                    _changedSitePos.Add(kv.Value.Data.Pos);
                }
            RearmFailedRoutes();

            // Territories merge where one PLAYER faction has built on both
            // sides (Roads.md §3.1). Union-find over region ids; None gets
            // its own slot at the end.
            int regions = RegionMap.Count + 1;
            if (_uf == null || _uf.Length != regions) _uf = new int[regions];
            for (int i = 0; i < regions; i++) _uf[i] = i;
            _firstRegionOfFaction.Clear();
            for (int i = 0; i < _sites.Count; i++)
            {
                var s = _sites[i];
                if (!s.IsBuilding || s.Faction == Faction.Border || s.Faction == Faction.White) continue;
                int r = Slot(s.Region, regions);
                if (_firstRegionOfFaction.TryGetValue(s.Faction, out int first)) Union(first, r);
                else _firstRegionOfFaction[s.Faction] = r;
            }

            if (_netMembers.Length < regions)
            {
                var grown = new List<int>[regions];
                for (int i = 0; i < grown.Length; i++)
                    grown[i] = i < _netMembers.Length ? _netMembers[i] : new List<int>();
                _netMembers = grown;
            }
            for (int i = 0; i < _netMembers.Length; i++) _netMembers[i].Clear();
            for (int i = 0; i < _sites.Count; i++)
                _netMembers[Find(Slot(_sites[i].Region, regions))].Add(i);

            // Edges of the live graph. A network whose members are unchanged
            // reuses its cached graph; only a changed one recomputes it.
            _liveEdgeKeys.Clear();
            _netEdgeUsed.Clear();
            // ONE MAP NETWORK (Roads.md §0, 2026-10-14): once the simulation
            // has built its road graph, its edges ARE the roads, for everyone;
            // the per-territory graphs below are the fallback before that.
            bool simGraph = RoadGraphSystem.Built;
            if (simGraph) AddSimEdges();
            for (int root = 0; root < regions && !simGraph; root++)
            {
                var members = _netMembers[root];
                if (members.Count < 2) continue;
                bool anyBuilding = false;
                ulong netHash = (ulong)members.Count * 0x9E3779B97F4A7C15UL;
                for (int i = 0; i < members.Count; i++)
                {
                    var s = _sites[members[i]];
                    anyBuilding |= s.IsBuilding;
                    netHash += NetMemberHash(s);
                }
                float halfWidth = (anyBuilding ? Cfg.roadWidth : Cfg.trailWidth) * 0.5f;

                if (!_netEdgeCache.TryGetValue(netHash, out var pairs))
                {
                    _pts.Clear(); _ptSite.Clear();
                    for (int i = 0; i < members.Count; i++)
                    {
                        _pts.Add(_sites[members[i]].Pos); _ptSite.Add(members[i]);
                    }
                    RoadGraph.RelativeNeighbourhood(_pts, _edges, _rngScratch);
                    pairs = _edgeListPool.Count > 0 ? _edgeListPool.Pop() : new List<EdgePair>();
                    pairs.Clear();
                    for (int e = 0; e < _edges.Count; e++)
                        pairs.Add(new EdgePair { A = _sites[_ptSite[_edges[e].x]].Entity, B = _sites[_ptSite[_edges[e].y]].Entity });
                    _netEdgeCache[netHash] = pairs;
                }
                _netEdgeUsed.Add(netHash);

                for (int e = 0; e < pairs.Count; e++)
                {
                    var a = _siteStates[pairs[e].A].Data;
                    var b = _siteStates[pairs[e].B].Data;
                    long key = RouteKey(a.Entity, b.Entity);
                    _liveEdgeKeys.Add(key);
                    bool finished = a.Finished && b.Finished;

                    if (_edgeStates.TryGetValue(key, out var es))
                    {
                        if (!es.Live || es.Finished != finished || es.HalfWidthMetres != halfWidth) _dirty = true;
                        if (!es.Live && es.NeedsRoute) _routeQueue.Enqueue(key);   // back before it was routed
                        es.Live = true; es.Finished = finished; es.HalfWidthMetres = halfWidth;
                        es.SiteA = a; es.SiteB = b;
                        continue;
                    }

                    // Routing is the expensive part (A* + spline per edge):
                    // the edge joins the queue PumpRoutes drains against the
                    // per-frame budget.
                    es = new EdgeState
                    {
                        A = a.Entity, B = b.Entity, Finished = finished,
                        HalfWidthMetres = halfWidth, Strength = _synced ? 0f : 1f, Live = true,
                        SiteA = a, SiteB = b, NeedsRoute = true,
                    };
                    if (_failedRoutes.ContainsKey(key))
                        es.NeedsRoute = false;           // known unroutable; a nearby change re-arms it
                    else
                        _routeQueue.Enqueue(key);
                    _edgeStates[key] = es;
                    _dirty = true; _dirDirty = true;
                }
            }
            // Forget the graphs of networks that no longer exist.
            _netEdgeStale.Clear();
            foreach (var kv in _netEdgeCache) if (!_netEdgeUsed.Contains(kv.Key)) _netEdgeStale.Add(kv.Key);
            for (int i = 0; i < _netEdgeStale.Count; i++)
            {
                _edgeListPool.Push(_netEdgeCache[_netEdgeStale[i]]);
                _netEdgeCache.Remove(_netEdgeStale[i]);
            }

            foreach (var kv in _edgeStates)
            {
                if (_liveEdgeKeys.Contains(kv.Key)) continue;
                if (kv.Value.Live) { kv.Value.Live = false; _dirty = true; _dirDirty = true; }
            }
            _synced = true;
        }

        private void AddSimEdges()
        {
            for (int i = 0; i < RoadGraphSystem.NodeCount; i++)
            {
                var ea = RoadGraphSystem.NodeEntity(i);
                if (!_siteStates.TryGetValue(ea, out var sa) || !sa.Live) continue;
                var nb = RoadGraphSystem.Neighbours(i);
                for (int k = 0; k < nb.Count; k++)
                {
                    int j = nb[k];
                    if (j <= i) continue;
                    var eb = RoadGraphSystem.NodeEntity(j);
                    if (!_siteStates.TryGetValue(eb, out var sb) || !sb.Live) continue;
                    var a = sa.Data; var b = sb.Data;
                    float halfWidth = ((a.IsBuilding || b.IsBuilding) ? Cfg.roadWidth : Cfg.trailWidth) * 0.5f;
                    long key = RouteKey(a.Entity, b.Entity);
                    _liveEdgeKeys.Add(key);
                    bool finished = a.Finished && b.Finished;
                    if (_edgeStates.TryGetValue(key, out var es))
                    {
                        if (!es.Live || es.Finished != finished || es.HalfWidthMetres != halfWidth) _dirty = true;
                        if (!es.Live && es.NeedsRoute) _routeQueue.Enqueue(key);
                        es.Live = true; es.Finished = finished; es.HalfWidthMetres = halfWidth;
                        es.SiteA = a; es.SiteB = b;
                        continue;
                    }
                    es = new EdgeState
                    {
                        A = a.Entity, B = b.Entity, Finished = finished,
                        HalfWidthMetres = halfWidth, Strength = _synced ? 0f : 1f, Live = true,
                        SiteA = a, SiteB = b, NeedsRoute = true,
                    };
                    if (_failedRoutes.ContainsKey(key)) es.NeedsRoute = false;
                    else _routeQueue.Enqueue(key);
                    _edgeStates[key] = es;
                    _dirty = true; _dirDirty = true;
                }
            }
        }

        /// <summary>
        /// Route queued edges until this frame's budget is spent. The search in
        /// flight carries over to the next frame, so the budget holds even for
        /// a route that runs to the expansion cap.
        /// </summary>
        private void PumpRoutes(RoadGraph.Grid grid)
        {
            _routedThisSync = 0;
            if (!_searchActive && _routeQueue.Count == 0) { _startupRouting = false; return; }

            float budgetMs = _startupRouting ? Cfg.startupRouteBudgetMs : Cfg.routeBudgetMs;
            _routeWatch.Restart();
            while (_routeWatch.Elapsed.TotalMilliseconds < budgetMs)
            {
                if (!_searchActive && !BeginNextRoute(grid)) break;

                var state = _search.Step(_scratch, ref _searchTest, Cfg.slopePenalty,
                                         Cfg.maxRouteExpansions, SearchChunk, _path);
                if (state == RoadGraph.SearchState.Running) continue;
                FinishRoute(grid, state == RoadGraph.SearchState.Found);
            }
            double ms = _routeWatch.Elapsed.TotalMilliseconds;
            _sumRouteMs += ms;
            if (ms >= TheWaningBorder.Core.Diagnostics.PerfSpikeLog.DefaultThresholdMs)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RoadNetwork.Route", ms,
                    $"finished={_routedThisSync} queued={_routeQueue.Count}");
        }

        /// <summary>Start the next queued edge that still wants a route.</summary>
        private bool BeginNextRoute(RoadGraph.Grid grid)
        {
            while (_routeQueue.Count > 0)
            {
                long key = _routeQueue.Dequeue();
                if (!_edgeStates.TryGetValue(key, out var es) || !es.NeedsRoute) continue;
                if (!es.Live) continue;   // stays NeedsRoute; re-queued if it comes back
                var a = es.SiteA; var b = es.SiteB;
                _searchTest = new RoadCellTest
                {
                    Grid = grid, PA = a.Pos, PB = b.Pos, DA2 = a.Disc * a.Disc, DB2 = b.Disc * b.Disc,
                };
                _searchCorridor = Cfg.routeCorridorMargin + Cfg.routeCorridorFraction * (a.Pos - b.Pos).magnitude;
                _searchKey = key; _searchA = a; _searchB = b;
                _searchActive = true;
                if (!_search.Begin(grid, _scratch, a.Pos, b.Pos, _searchCorridor))
                {
                    FinishRoute(grid, found: false);
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// The search for <see cref="_searchKey"/> is over: shape the path into
        /// a road (Roads.md §3.3), or record the failure with its back-off. The
        /// edge may have faded away meanwhile — then the result is dropped.
        /// </summary>
        private void FinishRoute(RoadGraph.Grid grid, bool found)
        {
            _searchActive = false;
            _routedThisSync++;
            _sumSearches++;
            if (found) _sumFound++; else _sumFailed++;
            long key = _searchKey;
            List<Vector2> route = null;
            if (found)
            {
                route = ShapeRoute(grid, _searchTest, _searchA, _searchB);
                _failedRoutes.Remove(key);
            }
            else
            {
                int fails = _failedRoutes.TryGetValue(key, out var prev) ? prev.Fails + 1 : 1;
                float wait = Mathf.Min(Cfg.failRetrySeconds * Mathf.Pow(2f, fails - 1), Cfg.failRetryMaxSeconds);
                _failedRoutes[key] = new FailedRoute
                {
                    Min = Vector2.Min(_searchA.Pos, _searchB.Pos) - Vector2.one * _searchCorridor,
                    Max = Vector2.Max(_searchA.Pos, _searchB.Pos) + Vector2.one * _searchCorridor,
                    Fails = fails, RetryAt = Time.time + wait,
                };
            }
            if (!_edgeStates.TryGetValue(key, out var es)) return;
            es.Route = route;
            es.NeedsRoute = false;
            _dirty = true; _dirDirty = true; _pointsDirty = true;
        }

        /// <summary>A failed pair is retried only when a site moved or
        /// vanished inside the box its search covered, and its back-off is
        /// over. The failure record stays until a route is found, so the
        /// back-off keeps doubling while the pair keeps failing.</summary>
        private void RearmFailedRoutes()
        {
            if (_failedRoutes.Count == 0 || _changedSitePos.Count == 0) return;
            _failedRearm.Clear();
            float now = Time.time;
            foreach (var kv in _failedRoutes)
            {
                var f = kv.Value;
                if (now < f.RetryAt) continue;
                for (int i = 0; i < _changedSitePos.Count; i++)
                {
                    var p = _changedSitePos[i];
                    if (p.x >= f.Min.x && p.y >= f.Min.y && p.x <= f.Max.x && p.y <= f.Max.y)
                    { _failedRearm.Add(kv.Key); break; }
                }
            }
            for (int i = 0; i < _failedRearm.Count; i++)
            {
                long key = _failedRearm[i];
                if (_edgeStates.TryGetValue(key, out var es) && es.Route == null && !es.NeedsRoute)
                {
                    es.NeedsRoute = true;
                    _routeQueue.Enqueue(key);
                }
            }
        }

        /// <summary>Walkability for a road's A*: the nav grid, plus any cell
        /// inside either endpoint's own disc (a site sits inside its own
        /// stamped footprint). A struct, so the search pays no delegate call
        /// per neighbour.</summary>
        private struct RoadCellTest : RoadGraph.ICellTest
        {
            public RoadGraph.Grid Grid;
            public Vector2 PA, PB;
            public float DA2, DB2;
            public bool Passable(int2 c)
            {
                if (NavGridQuery.IsCellPassable(c)) return true;
                var w = Grid.CentreOf(c);
                return (w - PA).sqrMagnitude <= DA2 || (w - PB).sqrMagnitude <= DB2;
            }
        }

        /// <summary>The found cell path (<see cref="_path"/>) → bend points →
        /// a meandering Unity spline (Roads.md §3.3), or the rounded cell path
        /// if the spline cannot stay on passable ground.</summary>
        private List<Vector2> ShapeRoute(RoadGraph.Grid grid, RoadCellTest test, Site a, Site b)
        {
            RoadGraph.Simplify(_path, Cfg.simplifyTolerance, _waypoints);
            System.Func<Vector2, bool> walkable = w => test.Passable(grid.CellOf(w));
            if (!RoadGraph.Meander(_waypoints, Cfg.meanderAmplitude, Cfg.meanderWavelength,
                                   Cfg.curveSampleStep, a.Entity.Index * 31 + b.Entity.Index,
                                   walkable, _curve, _pathScratch))
            {
                RoadGraph.Chaikin(_path, Cfg.smoothingPasses, _pathScratch);
                _curve.Clear(); _curve.AddRange(_path);
            }
            return new List<Vector2>(_curve);
        }

        // ─── Animation ────────────────────────────────────────────────────
        /// <summary>Ease every strength toward its target: up over
        /// fadeInSeconds, down over fadeOutSeconds. Dead things at 0 are
        /// forgotten.</summary>
        private void Animate(float dt)
        {
            float up = dt / Mathf.Max(0.05f, Cfg.fadeInSeconds);
            float down = dt / Mathf.Max(0.05f, Cfg.fadeOutSeconds);
            _animating = false;

            _deadSites.Clear();
            foreach (var kv in _siteStates)
            {
                var st = kv.Value;
                if (st.Live) { if (st.Strength < 1f) { st.Strength = Mathf.Min(1f, st.Strength + up); _animating = true; } }
                else
                {
                    st.Strength = Mathf.Max(0f, st.Strength - down);
                    if (st.Strength <= 0f) _deadSites.Add(kv.Key); else _animating = true;
                }
            }
            for (int i = 0; i < _deadSites.Count; i++) { _siteStates.Remove(_deadSites[i]); _dirty = true; }

            _deadEdges.Clear();
            foreach (var kv in _edgeStates)
            {
                var es = kv.Value;
                if (es.Live) { if (es.Strength < 1f) { es.Strength = Mathf.Min(1f, es.Strength + up); _animating = true; } }
                else
                {
                    es.Strength = Mathf.Max(0f, es.Strength - down);
                    if (es.Strength <= 0f) _deadEdges.Add(kv.Key); else _animating = true;
                }
            }
            for (int i = 0; i < _deadEdges.Count; i++)
            {
                _edgeStates.Remove(_deadEdges[i]);
                _failedRoutes.Remove(_deadEdges[i]);
                _dirty = true; _dirDirty = true; _pointsDirty = true;
            }
        }

        private bool AnyFastFade()
        {
            foreach (var kv in _siteStates) if (kv.Value.Live && kv.Value.Strength < 1f) return true;
            foreach (var kv in _edgeStates) if (kv.Value.Live && kv.Value.Strength < 1f) return true;
            return false;
        }

        // ─── Raster ───────────────────────────────────────────────────────
        private void Raster()
        {
            EnsureMask();
            float texelsPerMetre = _res / _worldSize.x;

            _discCmds.Clear(); _polyCmds.Clear(); _squareCmds.Clear();
            // Route points in texel space change only when a route appears or
            // goes; rebuilding all ~50-100k of them on every raster (12 Hz while
            // anything wears in) was the raster's whole main-thread cost.
            if (_pointsDirty)
            {
                _polyPoints.Clear();
                foreach (var kv in _edgeStates)
                {
                    var es = kv.Value;
                    es.PointStart = _polyPoints.Length;
                    es.PointCount = 0;
                    if (es.Route == null) continue;
                    for (int i = 0; i < es.Route.Count; i++)
                    {
                        var t = ToTexel(es.Route[i]);
                        _polyPoints.Add(new float2(t.x, t.y));
                    }
                    es.PointCount = es.Route.Count;
                }
                _pointsDirty = false;
            }
            float padMargin = Cfg.alanthorPadMarginCells * BuildGrid.CellSize;
            foreach (var kv in _siteStates)
            {
                var st = kv.Value; var s = st.Data;
                var c = ToTexel(s.Pos);
                _discCmds.Add(new DiscCmd
                {
                    Centre = new float2(c.x, c.y), Radius = s.Disc * texelsPerMetre,
                    EdgeNoise = Cfg.plazaEdgeNoise, FadeStart = Cfg.plazaFadeStart,
                    Seed = s.Entity.Index, Finished = (byte)(s.Finished ? 1 : 0),
                    Plaza = (byte)(s.IsBuilding ? 1 : 0), Strength = Ease(st.Strength),
                });
                if (s.IsBuilding && s.Pad)
                {
                    var half = s.Size * 0.5f + new Vector2(padMargin, padMargin);
                    var lo = ToTexel(s.Pos - half);
                    var hi = ToTexel(s.Pos + half);
                    _squareCmds.Add(new SquareCmd
                    {
                        Min = new float2(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y)),
                        Max = new float2(Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y)),
                        Finished = (byte)(s.Finished ? 1 : 0), Strength = Ease(st.Strength),
                    });
                }
            }
            foreach (var kv in _edgeStates)
            {
                var es = kv.Value;
                if (es.Route == null || es.PointCount == 0) continue;
                _polyCmds.Add(new PolyCmd
                {
                    Start = es.PointStart, Count = es.PointCount,
                    HalfWidth = es.HalfWidthMetres * texelsPerMetre, EdgeFade = Cfg.roadEdgeFade,
                    Finished = (byte)(es.Finished ? 1 : 0), Strength = Ease(es.Strength),
                });
            }

            // Off the main thread; Update picks the result up next frame.
            var job = new RoadRasterJob
            {
                Res = _res,
                Mask = _maskBuf,
                Dir = _dirBuf,
                Nearest = _nearest,
                Discs = _discCmds.AsArray(),
                Squares = _squareCmds.AsArray(),
                Polys = _polyCmds.AsArray(),
                Points = _polyPoints.AsArray(),
            };
            // IJobExtensions explicitly: with Unity.Entities in scope the bare
            // .Schedule() binds to the IJobEntity overload and fails to compile.
            _rasterJob = Unity.Jobs.IJobExtensions.Schedule(job);
            _rasterPending = true;
            _pendingDir |= _dirDirty;
            _dirDirty = false;
            _dirty = false;
        }

        /// <summary>Smoothstep on the linear strength: a wear-in that starts
        /// and ends gently, a reclaim likewise.</summary>
        private static float Ease(float s) => s * s * (3f - 2f * s);

        /// <summary>
        /// Order-independent key of an entity pair, Version included: keyed on
        /// Index alone, a new building on a recycled index inherited the dead
        /// one's road geometry and its failure record. Two 64-bit identities
        /// are mixed into one 64-bit key; a collision among the few thousand
        /// edges of a match is ~1e-11 likely, and costs one wrong road.
        /// </summary>
        private static long RouteKey(Entity a, Entity b)
        {
            ulong ia = ((ulong)(uint)a.Index << 32) | (uint)a.Version;
            ulong ib = ((ulong)(uint)b.Index << 32) | (uint)b.Version;
            ulong lo = ia < ib ? ia : ib, hi = ia < ib ? ib : ia;
            ulong h = lo * 0x9E3779B97F4A7C15UL;
            h ^= h >> 31;
            h += hi * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
            return (long)h;
        }

        private static int Slot(int region, int regions) => region == RegionMap.None ? regions - 1 : region;
        private int Find(int i) { while (_uf[i] != i) { _uf[i] = _uf[_uf[i]]; i = _uf[i]; } return i; }
        private void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) _uf[a] = b; }

        private Vector2 ToTexel(Vector2 world)
            => new Vector2((world.x - _worldMin.x) / _worldSize.x * _res,
                           (world.y - _worldMin.y) / _worldSize.y * _res);

        // ─── Mask + shader globals ────────────────────────────────────────
        private void EnsureMask()
        {
            _worldMin = PlayerInfluenceMap.WorldMin;
            _worldSize = PlayerInfluenceMap.WorldSize;
            int res = Mathf.Max(64, Cfg.maskResolution);
            if (_mask != null && _res == res) return;
            if (_mask != null) Destroy(_mask);
            if (_dirMask != null) Destroy(_dirMask);
            _res = res;
            // Linear: the shader reads the numbers written here as-is (the
            // region-edge bake had to pre-compensate an sRGB decode; these
            // masks simply are not sRGB).
            _mask = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "TWB_RoadMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _dirMask = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "TWB_RoadDir",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            if (_rasterPending) { _rasterJob.Complete(); _rasterPending = false; }
            if (_nearest.IsCreated) _nearest.Dispose();
            if (_maskBuf.IsCreated) _maskBuf.Dispose();
            if (_dirBuf.IsCreated) _dirBuf.Dispose();
            _nearest = new NativeArray<float>(res * res, Allocator.Persistent);
            _maskBuf = new NativeArray<Color32>(res * res, Allocator.Persistent);
            _dirBuf = new NativeArray<Color32>(res * res, Allocator.Persistent);
            if (!_discCmds.IsCreated) _discCmds = new NativeList<DiscCmd>(256, Allocator.Persistent);
            if (!_squareCmds.IsCreated) _squareCmds = new NativeList<SquareCmd>(256, Allocator.Persistent);
            if (!_polyCmds.IsCreated) _polyCmds = new NativeList<PolyCmd>(256, Allocator.Persistent);
            if (!_polyPoints.IsCreated) _polyPoints = new NativeList<float2>(8192, Allocator.Persistent);
            _globalsBound = false;
            _dirDirty = true;
            _pointsDirty = true;
        }

        private void BindGlobals()
        {
            Shader.SetGlobalTexture("_TWB_RoadMask", _mask);
            Shader.SetGlobalTexture("_TWB_RoadDir", _dirMask);
            Shader.SetGlobalFloat("_TWB_RoadFeather", Cfg.edgeFeather);
            Shader.SetGlobalFloat("_TWB_RoadDarken", Cfg.earthenDarken);
            Shader.SetGlobalFloat("_TWB_RutOffset", Cfg.rutOffset);
            Shader.SetGlobalFloat("_TWB_RutWidth", Cfg.rutWidth);
            Shader.SetGlobalFloat("_TWB_RutDepth", Cfg.rutDepth);
            Shader.SetGlobalFloat("_TWB_RoadCentreStrip", Cfg.centreStrip);
            if (!_globalsBound)
            {
                // Earthen roads are the map's OWN dirt: the terrain layer whose
                // name says so, else its first layer.
                var terrain = UnityEngine.Terrain.activeTerrain;
                var layers = terrain != null ? terrain.terrainData.terrainLayers : null;
                UnityEngine.TerrainLayer earth = null;
                if (layers != null && layers.Length > 0)
                {
                    earth = layers[0];
                    for (int i = 0; i < layers.Length; i++)
                        if (layers[i] != null && layers[i].name.IndexOf("dirt", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        { earth = layers[i]; break; }
                }
                if (earth != null && earth.diffuseTexture != null)
                {
                    Shader.SetGlobalTexture("_TWB_RoadEarthAlbedo", earth.diffuseTexture);
                    Shader.SetGlobalFloat("_TWB_RoadTiling", Mathf.Max(0.5f, earth.tileSize.x));
                }
                _globalsBound = true;
            }
            Shader.SetGlobalFloat("_TWB_RoadEnabled", 1f);
        }

        void OnDestroy()
        {
            Shader.SetGlobalFloat("_TWB_RoadEnabled", 0f);
            if (_mask != null) Destroy(_mask);
            if (_dirMask != null) Destroy(_dirMask);
            if (_rasterPending) { _rasterJob.Complete(); _rasterPending = false; }
            if (_nearest.IsCreated) _nearest.Dispose();
            if (_maskBuf.IsCreated) _maskBuf.Dispose();
            if (_dirBuf.IsCreated) _dirBuf.Dispose();
            if (_discCmds.IsCreated) _discCmds.Dispose();
            if (_squareCmds.IsCreated) _squareCmds.Dispose();
            if (_polyCmds.IsCreated) _polyCmds.Dispose();
            if (_polyPoints.IsCreated) _polyPoints.Dispose();
        }
    }
}
