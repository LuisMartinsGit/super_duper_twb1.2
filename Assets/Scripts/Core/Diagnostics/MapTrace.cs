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
            }

            // The fixed features, once, as soon as the partition is ready.
            if (!_header && RegionMap.Ready)
            {
                for (int i = 0; i < RegionMap.Count; i++)
                {
                    var seed = RegionMap.SeedOf(i);
                    W("R", i, Q(RegionMap.NameOf(i)), F(seed.x), F(seed.y));
                }
                WriteNodes<IronMineTag>(em, "iron");
                WriteNodes<VeilstoneOutcroppingTag>(em, "veilstone");
                WriteNodes<SupplyNodeTag>(em, "supply");
                _header = true;
                _w.Flush();
            }

            float t = MatchMetrics.MatchTime;
            if (t < _next) return;
            _next = t + Mathf.Max(0.1f, Period);
            string ts = F(t);

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
                    if (em.HasComponent<FormationMemberState>(e)) flags |= 2;
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
