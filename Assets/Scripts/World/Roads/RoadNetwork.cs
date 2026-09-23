// RoadNetwork.cs
// The procedural road network (docs/Design/Roads.md): sites (outcrops,
// supply spots, curse nodes, buildings) joined per territory by a
// relative-neighbourhood graph, routed over the nav grid, and rasterised
// into _TWB_RoadMask / _TWB_RoadDir for the terrain shader. WHERE ground is
// road or plaza is decided here; WHAT it looks like (earthen vs culture
// paving) is decided per pixel in TWBTerrainOverlays.hlsl.
//
// Presentation only: reads simulation state twice a second, never writes
// it. Every plaza and every road is a STATE with a strength that eases
// toward 1 while its site / edge exists and back to 0 after it is gone —
// fast in (a path wears in over ~2.5 s), slow out (the grass takes ~75 s
// to reclaim it). The mask is re-rasterised only while something is
// animating, at a rate matched to the fastest active fade; when the
// network is still, nothing runs per frame. No per-tick CreateEntityQuery:
// every query is a CachedEntityQuery with its ComponentType[] hoisted.

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
        }

        /// <summary>A plaza with a life of its own: the last-known site data
        /// (the entity may be gone) and an eased strength.</summary>
        private sealed class SiteState
        {
            public Site Data;
            public float Strength;
            public bool Live;
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
        private int _routedThisSync;    // edges routed in the last poll (perf detail)

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

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + Cfg.pollSeconds;
                var nav = gridQ.GetSingleton<NavGridSingleton>();
                var grid = new RoadGraph.Grid { Width = nav.Width, Height = nav.Height, CellSize = nav.CellSize, Origin = nav.Origin };
                double t0 = Time.realtimeSinceStartupAsDouble;
                _routedThisSync = 0;
                GatherSites(em);
                SyncTopology(grid);
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RoadNetwork.Sync",
                    (Time.realtimeSinceStartupAsDouble - t0) * 1000.0,
                    $"sites={_sites.Count} routed={_routedThisSync}");
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
            r = RegionMap.RawRegionAt(pos.x, pos.y);
            _regionOf[e] = r;
            return r;
        }

        private void GatherSites(EntityManager em)
        {
            _sites.Clear();
            AddBuildings(em);
            AddNodes(em, QC_Veilstone.Get(em, QT_Veilstone));
            AddNodes(em, QC_Iron.Get(em, QT_Iron));
            AddNodes(em, QC_Veilsteel.Get(em, QT_Veilsteel));
            AddNodes(em, QC_Supply.Get(em, QT_Supply));
            AddNodes(em, QC_Well.Get(em, QT_Well));
            AddNodes(em, QC_Pocket.Get(em, QT_Pocket));
        }

        private void AddBuildings(EntityManager em)
        {
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var lt = em.GetComponentData<LocalTransform>(e);
                var sz = em.GetComponentData<BuildingSize>(e);
                var pos = new Vector2(lt.Position.x, lt.Position.z);
                _sites.Add(new Site
                {
                    Entity = e, Pos = pos,
                    Disc = Mathf.Max(sz.Width, sz.Height) * 0.5f + Cfg.plazaMargin,
                    Finished = !em.HasComponent<UnderConstruction>(e),
                    IsBuilding = true,
                    Faction = em.GetComponentData<FactionTag>(e).Value,
                    Region = RegionOf(e, pos),
                });
            }
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
        private void SyncTopology(RoadGraph.Grid grid)
        {
            // Sites.
            foreach (var kv in _siteStates) kv.Value.Live = false;
            for (int i = 0; i < _sites.Count; i++)
            {
                var s = _sites[i];
                if (_siteStates.TryGetValue(s.Entity, out var st))
                {
                    if (st.Data.Finished != s.Finished || st.Data.Region != s.Region
                        || (st.Data.Pos - s.Pos).sqrMagnitude > 0.01f)
                        _dirty = true;
                    if (!st.Live) _dirty = true;     // resurrected while fading out
                    st.Data = s; st.Live = true;
                }
                else
                {
                    _siteStates[s.Entity] = new SiteState { Data = s, Strength = _synced ? 0f : 1f, Live = true };
                    _dirty = true;
                }
            }
            foreach (var kv in _siteStates)
                if (!kv.Value.Live && kv.Value.Strength > 0f) _dirty = true;

            // Territories merge where one PLAYER faction has built on both
            // sides (Roads.md §3.1). Union-find over region ids; None gets
            // its own slot at the end.
            int regions = RegionMap.Count + 1;
            if (_uf == null || _uf.Length != regions) _uf = new int[regions];
            for (int i = 0; i < regions; i++) _uf[i] = i;
            var firstRegionOfFaction = new Dictionary<Faction, int>();
            for (int i = 0; i < _sites.Count; i++)
            {
                var s = _sites[i];
                if (!s.IsBuilding || s.Faction == Faction.Border || s.Faction == Faction.White) continue;
                int r = Slot(s.Region, regions);
                if (firstRegionOfFaction.TryGetValue(s.Faction, out int first)) Union(first, r);
                else firstRegionOfFaction[s.Faction] = r;
            }

            var networks = new Dictionary<int, List<int>>();
            for (int i = 0; i < _sites.Count; i++)
            {
                int root = Find(Slot(_sites[i].Region, regions));
                if (!networks.TryGetValue(root, out var list)) networks[root] = list = new List<int>();
                list.Add(i);
            }

            // Edges of the live graph.
            _liveEdgeKeys.Clear();
            System.Func<int2, bool> passable = c => NavGridQuery.IsCellPassable(c);
            foreach (var kv in networks)
            {
                var members = kv.Value;
                bool anyBuilding = false;
                _pts.Clear(); _ptSite.Clear();
                for (int i = 0; i < members.Count; i++)
                {
                    var s = _sites[members[i]];
                    anyBuilding |= s.IsBuilding;
                    _pts.Add(s.Pos); _ptSite.Add(members[i]);
                }
                if (_pts.Count < 2) continue;
                float halfWidth = (anyBuilding ? Cfg.roadWidth : Cfg.trailWidth) * 0.5f;

                RoadGraph.RelativeNeighbourhood(_pts, _edges);
                for (int e = 0; e < _edges.Count; e++)
                {
                    var a = _sites[_ptSite[_edges[e].x]];
                    var b = _sites[_ptSite[_edges[e].y]];
                    long key = RouteKey(a.Entity, b.Entity);
                    _liveEdgeKeys.Add(key);
                    bool finished = a.Finished && b.Finished;

                    if (_edgeStates.TryGetValue(key, out var es))
                    {
                        if (!es.Live || es.Finished != finished || es.HalfWidthMetres != halfWidth) _dirty = true;
                        es.Live = true; es.Finished = finished; es.HalfWidthMetres = halfWidth;
                        continue;
                    }

                    // Routing is the expensive part (A* + spline per edge), so
                    // only a budget of edges is routed per poll; the rest wait
                    // half a second. At match start that spreads ~40 trails
                    // over a few polls instead of one 40 ms hitch.
                    es = new EdgeState
                    {
                        A = a.Entity, B = b.Entity, Finished = finished,
                        HalfWidthMetres = halfWidth, Strength = _synced ? 0f : 1f, Live = true,
                        SiteA = a, SiteB = b, NeedsRoute = true,
                    };
                    if (_routedThisSync < Cfg.maxRoutesPerPoll)
                    {
                        es.Route = RouteEdge(grid, passable, a, b);
                        es.NeedsRoute = false;
                        _routedThisSync++;
                    }
                    _edgeStates[key] = es;
                    _dirty = true; _dirDirty = true;
                }
            }
            foreach (var kv in _edgeStates)
            {
                if (_liveEdgeKeys.Contains(kv.Key)) continue;
                if (kv.Value.Live) { kv.Value.Live = false; _dirty = true; _dirDirty = true; }
            }
            // Deferred routes, within what is left of this poll's budget.
            foreach (var kv in _edgeStates)
            {
                var es = kv.Value;
                if (!es.NeedsRoute || !es.Live) continue;
                if (_routedThisSync >= Cfg.maxRoutesPerPoll) break;
                es.Route = RouteEdge(grid, passable, es.SiteA, es.SiteB);
                es.NeedsRoute = false;
                _routedThisSync++;
                _dirty = true; _dirDirty = true;
            }
            _synced = true;
        }

        /// <summary>Route one edge: A* cell path → bend points → a meandering
        /// Unity spline (Roads.md §3.3), or the rounded cell path if the
        /// spline cannot stay on passable ground. Null when unroutable.</summary>
        private List<Vector2> RouteEdge(RoadGraph.Grid grid, System.Func<int2, bool> passable, Site a, Site b)
        {
            // Endpoints may sit inside their own stamped footprint: the
            // search may enter a cell within either site's disc.
            Vector2 pa = a.Pos, pb = b.Pos; float da = a.Disc, db = b.Disc;
            System.Func<int2, bool> passableOrSite = c =>
            {
                if (passable(c)) return true;
                var w = grid.CentreOf(c);
                return (w - pa).sqrMagnitude <= da * da || (w - pb).sqrMagnitude <= db * db;
            };
            if (!RoadGraph.Route(grid, _scratch, passableOrSite, a.Pos, b.Pos,
                                 Cfg.slopePenalty, Cfg.maxRouteExpansions, _path))
                return null;

            RoadGraph.Simplify(_path, Cfg.simplifyTolerance, _waypoints);
            System.Func<Vector2, bool> walkable = w => passableOrSite(grid.CellOf(w));
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
            for (int i = 0; i < _deadEdges.Count; i++) { _edgeStates.Remove(_deadEdges[i]); _dirty = true; _dirDirty = true; }
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

            _discCmds.Clear(); _polyCmds.Clear(); _polyPoints.Clear();
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
            }
            foreach (var kv in _edgeStates)
            {
                var es = kv.Value;
                if (es.Route == null) continue;
                int start = _polyPoints.Length;
                for (int i = 0; i < es.Route.Count; i++)
                {
                    var t = ToTexel(es.Route[i]);
                    _polyPoints.Add(new float2(t.x, t.y));
                }
                _polyCmds.Add(new PolyCmd
                {
                    Start = start, Count = es.Route.Count,
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

        private static long RouteKey(Entity a, Entity b)
        {
            int lo = Mathf.Min(a.Index, b.Index), hi = Mathf.Max(a.Index, b.Index);
            return ((long)lo << 32) | (uint)hi;
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
            if (!_polyCmds.IsCreated) _polyCmds = new NativeList<PolyCmd>(256, Allocator.Persistent);
            if (!_polyPoints.IsCreated) _polyPoints = new NativeList<float2>(8192, Allocator.Persistent);
            _globalsBound = false;
            _dirDirty = true;
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
            if (_polyCmds.IsCreated) _polyCmds.Dispose();
            if (_polyPoints.IsCreated) _polyPoints.Dispose();
        }
    }
}
