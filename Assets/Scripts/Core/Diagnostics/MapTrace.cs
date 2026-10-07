// MapTrace.cs
// The fine-grained replay feed: every unit's position and state, several
// times a minute, beside the match logs as MapTrace.txt.
//
// WHY THIS EXISTS AS RUNTIME CODE (2026-09-12). The trace format was written
// on 2026-09-07 as an EDITOR menu command (SiegeProbe.StartMapTrace), driven
// by EditorApplication.update and started by hand from a play-mode session.
// It produced the best match visualisation this project has had -- units as
// bodies with formation and combat state, buildings with real footprints,
// the region partition and every resource node -- and then no headless match
// could ever produce one, because headless has no editor. Every automated
// run since has been visualised from Metrics_UnitPositions.csv instead: one
// sample every 15 s, no unit identity, no state. A replay of dots that jump.
//
// Same line format as the editor version, so anything that reads one reads
// the other:
//   R  region      index name x z
//   N  node        id kind x z
//   B  building    t id faction name x z w h      (first sight)
//   U  unit        t id faction name              (first sight)
//   P  position    t id x z flags                 (1 moving, 2 in formation, 4 fighting)
//   D  death       t id
//
// EXTENDED 2026-10-03 for the Muster Rolls viewer (type and level symbols,
// buildings on the real 2 m build grid). Fields are only ever APPENDED, so a
// reader of the old shape still matches every line:
//   B  t id faction name x z w h  type cx cz cw ch yaw site
//        type  BuildingIds.Of id ("Fortress", "Hut", "Alanthor_Wall"...), or
//              CurseWell / CurseNode for the curse's structures, "?" if unknown
//        cx cz the footprint's minimum BUILD CELL (BuildGrid, 2 m, anchored
//              at the world origin) and cw ch its size in cells -- exactly
//              BuildGrid.FootprintCells. cx/cz are "*" for a piece that is
//              off the grid (walls are drawn, not snapped; Build_Grid.md 5)
//        yaw   degrees about +Y (0 = facing +Z), integer 0..359
//        site  1 = first seen as a construction site, 0 = complete
//   U  t id faction name  cls fl
//        cls   (int)UnitTag.Class (0 Melee .. 7 Scout), -1 if absent
//        fl    1 cavalry, 2 hero (HeroLevel), 4 curse unit (BorderUnitTag)
//   L  t id level        level change: a building's upgrade level
//                        (BuildingUpgradeState / stone WallTier / curse
//                        BorderNodeLevel), a unit's rank (UnitRank) or a
//                        hero's level (HeroLevel). Written at first sight only
//                        when it is not the default (building 0, unit 1), then
//                        on every change -- an event, never a per-sample field
//   C  t id              construction complete (a site became a building)
//
// EXTENDED 2026-10-04: THE REAL TERRITORY PARTITION AND WHO OWNS IT.
// The R lines are only seeds; the partition the game plays on is
// RegionMap.RegionAt -- authored outlines (with their sliver tolerance),
// warped Voronoi where a region has none, and None on Water / Mountain /
// Obstacle regions. A viewer-side Voronoi of the seeds is NOT that. So the
// header now carries the partition itself, sampled from RegionAt on the 2 m
// build grid (cell doubled until the map is at most 512 cells across), and
// ownership follows as change events:
//   TG cell x0 z0 w h    the raster: cell size in metres, south-west corner
//                        (a multiple of the cell, so cells sit on the build
//                        grid), and its size in cells. Cell (i, j) is the
//                        ground [x0+i*cell, x0+(i+1)*cell) x [z0+j*cell, ...)
//                        and holds RegionAt at its CENTRE.
//   TR j id:n id:n ...   row j (j = 0 is the south row), run-length encoded
//                        west to east: n cells of territory id (-1 = no
//                        territory). One line per row, written once.
//   TO t idx owner       territory idx's OWNER changed (TerritoryOwnership.
//                        OwnerOf): a faction name, Border for the curse, or
//                        "-" for unowned. Every territory is written once at
//                        the first check, then only on change -- read when
//                        TerritoryOwnership.Version moves, never by scanning.
//   TM t idx holder pct contested
//                        the ownership METER (Territory_Claims.md 2): who is
//                        filling it (same names as TO), its value 0..100 as
//                        an integer, 1 when hostiles froze it. Written at a
//                        sample only when the holder or contested state
//                        changes, the value crosses a 10-point step, or it
//                        reaches 0 or 100 -- a summary, not a per-second log.
//
// SAMPLE PERIOD is the whole cost. At 0.5 s a three-hour match with 300 units
// writes about six million position lines, so the default here is 1 s and
// -twbTracePeriod overrides it. The dashboard thins whatever it gets; what it
// cannot do is invent detail that was never recorded.
//
// READ-ONLY. It never writes simulation state, so it cannot affect lockstep.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Regions;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Diagnostics
{
    public sealed class MapTrace : MonoBehaviour
    {
        /// <summary>Seconds of MATCH time between position samples.</summary>
        public static float Period = 1.0f;

        /// <summary>Hard ceiling on the file. A three-hour match at one
        /// sample a second is already ~100 MB; past this the trace stops
        /// rather than filling the disk, and says so in the log.</summary>
        private const long MaxBytes = 400L * 1024 * 1024;

        private StreamWriter _w;
        private readonly HashSet<string> _seen = new HashSet<string>();
        // Last level written per id (L lines are change events), and the
        // construction sites still waiting for their C line.
        private readonly Dictionary<string, int> _level = new Dictionary<string, int>();
        private readonly HashSet<string> _sites = new HashSet<string>();
        private HashSet<string> _alive = new HashSet<string>();
        private float _next;
        private bool _header;
        // Ownership as last WRITTEN (TO lines are change events) and the
        // TerritoryOwnership.Version it was read at.
        private int[] _ownLast = Array.Empty<int>();
        private int _ownVersion = int.MinValue;
        // The meter as last written (TM lines).
        private int[] _mHolder = Array.Empty<int>();
        private int[] _mPct = Array.Empty<int>();
        private byte[] _mCont = Array.Empty<byte>();

        /// <summary>Longest side of the partition raster, in cells.</summary>
        private const int PartitionMaxCells = 512;
        /// <summary>Finest raster cell, metres -- the 2 m build grid.</summary>
        private const float PartitionMinCell = 2f;
        private bool _stopped;
        private long _bytes;

        private static readonly ComponentType[] QT_Building =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static CachedEntityQuery QC_Building;

        private static readonly ComponentType[] QT_Unit =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static CachedEntityQuery QC_Unit;

        /// <summary>Attach to a DontDestroyOnLoad object; it finds its own
        /// match folder once the world is up.</summary>
        public static void Install(float period)
        {
            if (period > 0f) Period = period;
            var go = new GameObject("MapTrace");
            DontDestroyOnLoad(go);
            go.AddComponent<MapTrace>();
        }

        private void Update()
        {
            if (_stopped) return;
            if (!MatchLifecycle.MapPopulated) return;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (_w == null)
            {
                try
                {
                    _w = new StreamWriter(MatchLogSession.File("MapTrace.txt"), false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[MapTrace] cannot open the trace: " + e.Message);
                    _stopped = true;
                    return;
                }
                _seen.Clear();
                _alive.Clear();
                _level.Clear();
                _sites.Clear();
                _next = 0f;
                _header = false;
                _ownLast = Array.Empty<int>();
                _ownVersion = int.MinValue;
                _mHolder = Array.Empty<int>();
                _mPct = Array.Empty<int>();
                _mCont = Array.Empty<byte>();
            }

            // The fixed features, once, as soon as the partition is ready.
            if (!_header && RegionMap.Ready)
            {
                for (int i = 0; i < RegionMap.Count; i++)
                {
                    var seed = RegionMap.SeedOf(i);
                    W("R", i, Q(RegionMap.NameOf(i)), F(seed.x), F(seed.y));
                }
                WritePartition();
                WriteNodes<IronMineTag>(em, "iron");
                WriteNodes<VeilstoneOutcroppingTag>(em, "veilstone");
                WriteNodes<SupplyNodeTag>(em, "supply");
                _header = true;
                _w.Flush();
            }

            float t = MatchMetrics.MatchTime;
            // Ownership is an event stream, so it is checked every frame
            // (one int compare) rather than at the sample period.
            if (_header) WriteOwnership(F(t));
            if (t < _next) return;
            _next = t + Mathf.Max(0.1f, Period);
            string ts = F(t);
            if (_header) WriteMeter(ts);

            var alive = new HashSet<string>();

            var bq = QC_Building.Get(em, QT_Building);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    string id = Key(em, e);
                    alive.Add(id);
                    if (_seen.Add(id))
                    {
                        var xf = em.GetComponentData<LocalTransform>(e);
                        var p = xf.Position;
                        int w = 4, h = 4;
                        if (em.HasComponent<BuildingSize>(e))
                        {
                            var bs = em.GetComponentData<BuildingSize>(e);
                            w = bs.Width; h = bs.Height;
                        }
                        string type = BuildingType(em, e);
                        var size = new int2(math.max(1, w), math.max(1, h));
                        BuildGrid.FootprintCells(p, size, out int2 minCell, out int2 cells);
                        // Off the grid when the building is exempt by rule
                        // (walls are drawn) or simply does not sit on it --
                        // never claim a cell origin the game did not use.
                        bool onGrid = !BuildGrid.IsGridExempt(type)
                                      && BuildGrid.IsSnapped(p, size, 0.05f);
                        bool site = em.HasComponent<UnderConstruction>(e);
                        if (site) _sites.Add(id);
                        W("B", ts, id, FactionOf(em, e), Q(Name(em, e)), F(p.x), F(p.z), F(w), F(h),
                          Q(type),
                          onGrid ? minCell.x.ToString(CultureInfo.InvariantCulture) : "*",
                          onGrid ? minCell.y.ToString(CultureInfo.InvariantCulture) : "*",
                          cells.x, cells.y, Yaw(xf.Rotation), site ? 1 : 0);
                    }
                    else if (_sites.Contains(id) && !em.HasComponent<UnderConstruction>(e))
                    {
                        _sites.Remove(id);
                        W("C", ts, id);
                    }
                    Level(ts, id, BuildingLevel(em, e), 0);
                }

            var uq = QC_Unit.Get(em, QT_Unit);
            using (var ents = uq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    string id = Key(em, e);
                    alive.Add(id);
                    if (_seen.Add(id))
                    {
                        int cls = em.HasComponent<UnitTag>(e)
                            ? (int)em.GetComponentData<UnitTag>(e).Class : -1;
                        int ufl = 0;
                        if (em.HasComponent<CavalryTag>(e)) ufl |= 1;
                        if (em.HasComponent<HeroLevel>(e)) ufl |= 2;
                        if (em.HasComponent<BorderUnitTag>(e)) ufl |= 4;
                        W("U", ts, id, FactionOf(em, e), Q(Name(em, e)), cls, ufl);
                    }
                    Level(ts, id, UnitLevel(em, e), 1);
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    int flags = 0;
                    if (em.HasComponent<DesiredDestination>(e)
                        && em.GetComponentData<DesiredDestination>(e).Has != 0) flags |= 1;
                    if (TransientState.Active<FormationMemberState>(em, e)) flags |= 2;
                    if (em.HasComponent<Target>(e)
                        && em.GetComponentData<Target>(e).Value != Entity.Null) flags |= 4;
                    W("P", ts, id, F(p.x), F(p.z), flags);
                }

            foreach (var id in _alive)
                if (!alive.Contains(id))
                {
                    W("D", ts, id);
                    _level.Remove(id);
                    _sites.Remove(id);
                }
            _alive = alive;

            try { _w.Flush(); } catch { }

            if (_bytes > MaxBytes)
            {
                Debug.LogWarning($"[MapTrace] stopping at {_bytes / (1024 * 1024)} MB — " +
                                 "raise -twbTracePeriod for a longer match.");
                Close();
                _stopped = true;
            }
        }

        private void OnDestroy() => Close();
        private void OnApplicationQuit() => Close();

        private void Close()
        {
            if (_w == null) return;
            try { _w.Flush(); _w.Dispose(); } catch { }
            _w = null;
        }

        private void WriteNodes<T>(EntityManager em, string kind)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(),
                                         ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                W("N", Key(em, ents[i]), kind, F(p.x), F(p.z));
            }
            // Created here on purpose and disposed here: this runs ONCE per
            // match, so a cached query would outlive its world for nothing.
            q.Dispose();
        }

        /// <summary>
        /// The partition as the game plays it: RegionMap.RegionAt sampled at
        /// every cell centre of a raster on the build grid, run-length
        /// encoded per row. Once per match. RegionAt is a pure function of
        /// the installed partition, so this reads nothing it could change.
        /// </summary>
        private void WritePartition()
        {
            if (!TheWaningBorder.World.Terrain.TerrainUtility.TryGetWorldBounds(out Vector2 min, out Vector2 max))
                TheWaningBorder.World.Terrain.TerrainUtility.GetPlayableBounds(out min, out max);

            float cell = PartitionMinCell;
            while (Mathf.Max(max.x - min.x, max.y - min.y) / cell > PartitionMaxCells) cell *= 2f;
            // Snap the corner OUT to a multiple of the cell, so the raster's
            // cells are build-grid cells (BuildGrid is anchored at the origin).
            float x0 = Mathf.Floor(min.x / cell) * cell;
            float z0 = Mathf.Floor(min.y / cell) * cell;
            int w = Mathf.Max(1, Mathf.CeilToInt((max.x - x0) / cell - 1e-4f));
            int h = Mathf.Max(1, Mathf.CeilToInt((max.y - z0) / cell - 1e-4f));
            W("TG", F(cell), F(x0), F(z0), w, h);

            var sb = new System.Text.StringBuilder(256);
            for (int j = 0; j < h; j++)
            {
                float wz = z0 + (j + 0.5f) * cell;
                sb.Length = 0;
                sb.Append(j.ToString(CultureInfo.InvariantCulture));
                int run = int.MinValue, n = 0;
                for (int i = 0; i < w; i++)
                {
                    int r = RegionMap.RegionAt(x0 + (i + 0.5f) * cell, wz);
                    if (r == run) { n++; continue; }
                    if (n > 0) AppendRun(sb, run, n);
                    run = r; n = 1;
                }
                if (n > 0) AppendRun(sb, run, n);
                W("TR", sb.ToString());
            }
        }

        private static void AppendRun(System.Text.StringBuilder sb, int id, int n)
        {
            sb.Append(' ');
            sb.Append(id.ToString(CultureInfo.InvariantCulture));
            sb.Append(':');
            sb.Append(n.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>A TO line for every territory whose owner differs from
        /// the last one written. Gated on TerritoryOwnership.Version, which
        /// bumps only on a real change. Reads OwnerOf only -- never
        /// Recompute, which publishes.</summary>
        private void WriteOwnership(string ts)
        {
            int count = RegionMap.Count;
            if (_ownVersion == TerritoryOwnership.Version && _ownLast.Length == count) return;
            _ownVersion = TerritoryOwnership.Version;
            if (_ownLast.Length != count)
            {
                _ownLast = new int[count];
                for (int i = 0; i < count; i++) _ownLast[i] = int.MinValue;
            }
            for (int i = 0; i < count; i++)
            {
                int o = TerritoryOwnership.OwnerOf(i);
                if (o == _ownLast[i]) continue;
                _ownLast[i] = o;
                W("TO", ts, i, SideName(o));
            }
        }

        /// <summary>TM lines: the meter, summarised (see the header).</summary>
        private void WriteMeter(string ts)
        {
            int count = RegionMap.Count;
            if (_mHolder.Length != count)
            {
                _mHolder = new int[count];
                _mPct = new int[count];
                _mCont = new byte[count];
                for (int i = 0; i < count; i++) { _mHolder[i] = int.MinValue; _mPct[i] = -1; }
            }
            for (int i = 0; i < count; i++)
            {
                int holder = TerritoryOwnership.HolderOf(i);
                int pct = Mathf.Clamp(Mathf.FloorToInt(TerritoryOwnership.ValueOf(i) + 1e-3f), 0, 100);
                byte cont = (byte)(TerritoryOwnership.IsContested(i) ? 1 : 0);
                int last = _mPct[i];
                bool step = last < 0 || pct / 10 != last / 10
                            || ((pct == 0 || pct == 100) && pct != last);
                if (holder == _mHolder[i] && cont == _mCont[i] && !step) continue;
                _mHolder[i] = holder; _mPct[i] = pct; _mCont[i] = cont;
                W("TM", ts, i, SideName(holder), pct, cont);
            }
        }

        private static string SideName(int side)
            => side == TerritoryOwnership.Curse ? "Border"
             : side < 0 ? "-"
             : ((Faction)side).ToString();

        private static string Key(EntityManager em, Entity e)
            => em.HasComponent<NetworkedEntity>(e)
                ? "n" + em.GetComponentData<NetworkedEntity>(e).NetworkId.ToString(CultureInfo.InvariantCulture)
                : e.Index.ToString(CultureInfo.InvariantCulture) + "v"
                  + e.Version.ToString(CultureInfo.InvariantCulture);

        private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string Q(string s) => string.IsNullOrEmpty(s) ? "?" : s.Replace(' ', '_');

        private void W(params object[] parts)
        {
            if (_w == null) return;
            string line = string.Join(" ", parts);
            _bytes += line.Length + 2;
            _w.WriteLine(line);
        }

        private static string Name(EntityManager em, Entity e)
        {
            if (em.HasComponent<UnitTypeId>(e)) return em.GetComponentData<UnitTypeId>(e).Value.ToString();
            if (em.HasComponent<DisplayName>(e)) return em.GetComponentData<DisplayName>(e).Value.ToString();
            // The curse's units are built outside UnitFactory and carry no
            // type id; their tags still say exactly which of the three it is.
            if (em.HasComponent<BorderUnitTag>(e))
            {
                if (em.HasComponent<CrystallingTag>(e)) return "Crystalling";
                if (em.HasComponent<UnitTag>(e))
                {
                    var c = em.GetComponentData<UnitTag>(e).Class;
                    if (c == UnitClass.Siege) return "Godsplinter";
                    if (c == UnitClass.Ranged) return "Veilstinger";
                }
                return "Curse_unit";
            }
            return em.HasComponent<BuildingTag>(e) ? "building" : "?";
        }

        /// <summary>The tech-tree id, the same one Metrics_Buildings.csv
        /// counts under, so the viewer can join the two.</summary>
        private static string BuildingType(EntityManager em, Entity e)
        {
            string id = TheWaningBorder.Entities.BuildingIds.Of(e, em);
            if (!string.IsNullOrEmpty(id)) return id;
            if (em.HasComponent<BorderMainNodeTag>(e)) return "CurseWell";
            if (em.HasComponent<SmallNodeTag>(e)) return "CurseNode";
            return "?";
        }

        /// <summary>The level the HUD names ("- Lvl N"): the upgrade ladder,
        /// else a stone wall's tier, else a curse node's growth level.</summary>
        private static int BuildingLevel(EntityManager em, Entity e)
        {
            if (em.HasComponent<BuildingUpgradeState>(e))
                return em.GetComponentData<BuildingUpgradeState>(e).Level;
            if (em.HasComponent<WallTier>(e) && !em.HasComponent<PalisadeTag>(e))
                return em.GetComponentData<WallTier>(e).Level;
            if (em.HasComponent<BorderNodeLevel>(e))
                return em.GetComponentData<BorderNodeLevel>(e).Value;
            return 0;
        }

        /// <summary>A hero's level, else bought veterancy rank, else 1.</summary>
        private static int UnitLevel(EntityManager em, Entity e)
        {
            if (em.HasComponent<HeroLevel>(e)) return em.GetComponentData<HeroLevel>(e).Value;
            if (em.HasComponent<UnitRank>(e)) return em.GetComponentData<UnitRank>(e).Value;
            return 1;
        }

        /// <summary>Write an L line when an id's level differs from the last
        /// one written (or from the default, at first sight). Read-only on
        /// the world: the state is this writer's own dictionary.</summary>
        private void Level(string ts, string id, int level, int dflt)
        {
            int last = _level.TryGetValue(id, out int v) ? v : dflt;
            if (level == last) return;
            _level[id] = level;
            W("L", ts, id, level);
        }

        private static int Yaw(quaternion q)
        {
            float3 fwd = math.mul(q, new float3(0f, 0f, 1f));
            int deg = (int)math.round(math.degrees(math.atan2(fwd.x, fwd.z)));
            return ((deg % 360) + 360) % 360;
        }

        private static string FactionOf(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e)
                ? em.GetComponentData<FactionTag>(e).Value.ToString() : "?";
    }
}
