// MatchMetrics.cs
// Machine-readable match statistics, for batch runs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY
//
// Four consecutive 30-minute matches were each invalidated by a different
// blocker in the same chain — a crash, a build-order stall on huts, the same
// stall on workers — and each one cost a full match to find. Every finding in
// those matches came from grepping AI_<colour>.log by hand, one sample at a
// time.
//
// The logs already hold the answers; what they lack is a shape a script can
// read across twenty runs. This writes that shape. Nothing here changes the
// simulation — it only observes it.
//
// FILES, all into the match's own logs/<session>/ folder:
//   Metrics_Faction.csv    per sample: population, bank, territories, totals
//   Metrics_Units.csv      per sample: unit id -> count, per faction
//   Metrics_Buildings.csv  per sample: building id -> count, per faction
//   Metrics_Research.csv   end of match: every completed tech, per faction
//   Metrics_Placement.csv  end of match: every building's id and position
//   Metrics_Combat.csv     end of match: kills and deaths per faction, by
//                          minute (fed live by DeathSystem)
//   Metrics_Deaths.csv     every unit death as an EVENT: time, victim,
//                          killer, position — where the battles were
//   Metrics_UnitPositions.csv  every unit's position, sampled every other
//                          tick (30 s) — the match unfolding on the map
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Diagnostics
{
    public class MatchMetrics : MonoBehaviour
    {
        /// <summary>Master switch. Off costs nothing.</summary>
        public static bool Enabled;

        /// <summary>Seconds between samples. Fine enough to see a build order
        /// progress, coarse enough that twenty matches stay greppable.</summary>
        public static float SampleInterval = 15f;

        /// <summary>Match clock, readable by the systems that feed the
        /// ledger below. Only advances while metrics are enabled.</summary>
        public static float MatchTime { get; private set; }

        // ── combat ledger: kills and deaths per (minute, faction) ──
        // Fed by DeathSystem at the moment of death; flushed by DumpFinal.
        // Static because DeathSystem is an ECS system with no path to this
        // component instance; reset in OnEnable so an editor session that
        // plays twice does not carry the first match's tally into the second.
        private static readonly Dictionary<(int, int), int> _kills = new();
        private static readonly Dictionary<(int, int), int> _deaths = new();

        // ── death events, with position — flushed incrementally by Sample
        // so a session still in flight has them too (the placement ledger's
        // dump-only lesson, learned the hard way on the replay panel).
        private static readonly List<(float t, int victim, int killer,
            bool attributed, int x, int z)> _deathEvents = new();

        /// <summary>One unit died. <paramref name="killer"/> is credited only
        /// when the death was attributed to a hostile faction. Position is
        /// where it fell — clusters of these ARE the battles.</summary>
        public static void RecordUnitDeath(Faction victim, Faction killer, bool attributed,
            float x, float z)
        {
            if (!Enabled) return;
            int minute = (int)(MatchTime / 60f);
            var dk = (minute, (int)victim);
            _deaths[dk] = _deaths.TryGetValue(dk, out int d) ? d + 1 : 1;
            if (attributed && killer != victim)
            {
                var kk = (minute, (int)killer);
                _kills[kk] = _kills.TryGetValue(kk, out int k) ? k + 1 : 1;
            }
            _deathEvents.Add((MatchTime, (int)victim, (int)killer, attributed,
                (int)x, (int)z));
        }

        private void OnEnable()
        {
            MatchTime = 0f;
            _kills.Clear();
            _deaths.Clear();
            _deathEvents.Clear();
        }

        private float _t;
        private float _next;
        private bool _headers;
        private EntityWorld _world;

        private void Update()
        {
            if (!Enabled) return;
            _t += Time.deltaTime;
            MatchTime = _t;
            if (_t < _next) return;
            _next = _t + SampleInterval;

            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            Sample(_world.EntityManager, _t);
        }

        private void EnsureHeaders()
        {
            if (_headers) return;
            _headers = true;
            Write("Metrics_Faction.csv",
                "t,faction,pop,popMax,supplies,iron,veilstone,veilsteel,territories,units,buildings\n");
            Write("Metrics_Units.csv", "t,faction,unitId,count\n");
            Write("Metrics_Buildings.csv", "t,faction,buildingId,count\n");
            Write("Metrics_Deaths.csv", "t,victim,killer,attributed,x,z\n");
            Write("Metrics_UnitPositions.csv", "t,faction,x,z\n");
            Write("Metrics_BuildingEvents.csv", "t,faction,buildingId,x,z,event\n");
        }

        // ── building EVENT ledger (2026-08-31) ──
        // The placement dump records only what is STANDING at match end, so
        // an eliminated faction's whole base vanished from the replay and
        // every appearance time had to be inferred. Diffing the building set
        // each sample records both halves — add and del, with time and
        // position — and costs one dictionary walk.
        private readonly System.Collections.Generic.Dictionary<Entity, (int f, string id, int x, int z)>
            _seenBuildings = new();

        private void SampleBuildingEvents(EntityManager em, float t)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var live = new System.Collections.Generic.HashSet<Entity>();
            var sb = new StringBuilder();
            int ti = (int)t;
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f > 7) continue;
                    live.Add(ents[i]);
                    if (_seenBuildings.ContainsKey(ents[i])) continue;
                    string id = TheWaningBorder.Entities.BuildingIds.Of(ents[i], em)
                                ?? "unknown";
                    var rec = (f, id, (int)xfs[i].Position.x, (int)xfs[i].Position.z);
                    _seenBuildings[ents[i]] = rec;
                    sb.Append(ti).Append(',').Append((Faction)f).Append(',')
                      .Append(id).Append(',').Append(rec.Item3).Append(',')
                      .Append(rec.Item4).Append(",add\n");
                }
            var gone = new System.Collections.Generic.List<Entity>();
            foreach (var kv in _seenBuildings)
                if (!live.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var e in gone)
            {
                var rec = _seenBuildings[e];
                _seenBuildings.Remove(e);
                sb.Append(ti).Append(',').Append((Faction)rec.f).Append(',')
                  .Append(rec.id).Append(',').Append(rec.x).Append(',')
                  .Append(rec.z).Append(",del\n");
            }
            Write("Metrics_BuildingEvents.csv", sb.ToString());
        }

        // ── incremental flush state (instance — resets with the component) ──
        private int _deathsFlushed;
        private bool _posToggle;

        /// <summary>Flush death events recorded since the last sample, and —
        /// every other sample (30 s) — every living unit's position. Both
        /// stream during the match so an in-flight session can be replayed.</summary>
        private void SamplePositionsAndDeaths(EntityManager em, float t)
        {
            if (_deathEvents.Count > _deathsFlushed)
            {
                var sb = new StringBuilder();
                for (int i = _deathsFlushed; i < _deathEvents.Count; i++)
                {
                    var e = _deathEvents[i];
                    sb.Append((int)e.t).Append(',')
                      .Append((Faction)e.victim).Append(',')
                      .Append((Faction)e.killer).Append(',')
                      .Append(e.attributed ? 1 : 0).Append(',')
                      .Append(e.x).Append(',').Append(e.z).Append('\n');
                }
                Write("Metrics_Deaths.csv", sb.ToString());
                _deathsFlushed = _deathEvents.Count;
            }

            _posToggle = !_posToggle;
            if (!_posToggle) return;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTypeId>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var pos = new StringBuilder();
            int ti = (int)t;
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f > 7) continue;
                    pos.Append(ti).Append(',').Append((Faction)f).Append(',')
                       .Append((int)xfs[i].Position.x).Append(',')
                       .Append((int)xfs[i].Position.z).Append('\n');
                }
            Write("Metrics_UnitPositions.csv", pos.ToString());
        }

        private void Sample(EntityManager em, float t)
        {
            EnsureHeaders();
            SamplePositionsAndDeaths(em, t);
            SampleBuildingEvents(em, t);

            // ── units by exact id, per faction ──
            var unitCounts = new Dictionary<(int, string), int>();
            var unitTotal = new int[8];
            var uq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTypeId>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var ids = uq.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ids.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f > 7) continue;
                    unitTotal[f]++;
                    var key = (f, ids[i].Value.ToString());
                    unitCounts[key] = unitCounts.TryGetValue(key, out int n) ? n + 1 : 1;
                }

            // ── buildings by id, per faction ──
            var bldCounts = new Dictionary<(int, string), int>();
            var bldTotal = new int[8];
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var ents = bq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (!em.HasComponent<FactionTag>(ents[i])) continue;
                    int f = (int)em.GetComponentData<FactionTag>(ents[i]).Value;
                    if (f < 0 || f > 7) continue;
                    bldTotal[f]++;
                    string id = TheWaningBorder.Entities.BuildingIds.Of(ents[i], em);
                    if (string.IsNullOrEmpty(id)) id = "unknown";
                    var key = (f, id);
                    bldCounts[key] = bldCounts.TryGetValue(key, out int n) ? n + 1 : 1;
                }

            var fac = new StringBuilder();
            var un = new StringBuilder();
            var bl = new StringBuilder();

            for (int f = 0; f < 8; f++)
            {
                if (unitTotal[f] == 0 && bldTotal[f] == 0) continue;
                var faction = (Faction)f;

                int pop = 0, popMax = 0;
                PopulationHelper.TryGetFactionPopulation(faction, out pop, out popMax);

                int su = 0, ir = 0, ve = 0, vs = 0;
                if (FactionEconomy.TryGetBank(em, faction, out var bank)
                    && em.HasComponent<FactionResources>(bank))
                {
                    var r = em.GetComponentData<FactionResources>(bank);
                    su = r.Supplies; ir = r.Iron; ve = r.Veilstone; vs = r.Veilsteel;
                }

                int terr = TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(faction) : 0;

                fac.Append(t.ToString("F0")).Append(',').Append(faction).Append(',')
                   .Append(pop).Append(',').Append(popMax).Append(',')
                   .Append(su).Append(',').Append(ir).Append(',')
                   .Append(ve).Append(',').Append(vs).Append(',')
                   .Append(terr).Append(',')
                   .Append(unitTotal[f]).Append(',').Append(bldTotal[f]).Append('\n');
            }

            foreach (var kv in unitCounts)
                un.Append(t.ToString("F0")).Append(',').Append((Faction)kv.Key.Item1).Append(',')
                  .Append(kv.Key.Item2).Append(',').Append(kv.Value).Append('\n');

            foreach (var kv in bldCounts)
                bl.Append(t.ToString("F0")).Append(',').Append((Faction)kv.Key.Item1).Append(',')
                  .Append(kv.Key.Item2).Append(',').Append(kv.Value).Append('\n');

            Write("Metrics_Faction.csv", fac.ToString());
            Write("Metrics_Units.csv", un.ToString());
            Write("Metrics_Buildings.csv", bl.ToString());
        }

        /// <summary>
        /// The end-of-match detail: where every building stands and what every
        /// faction finished researching. Written once, on the way out.
        /// </summary>
        public static void DumpFinal()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var place = new StringBuilder("faction,buildingId,x,z,region\n");
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var ents = bq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                    string id = TheWaningBorder.Entities.BuildingIds.Of(ents[i], em);
                    int region = RegionMap.Ready ? RegionMap.RegionAt(p.x, p.z) : -1;
                    place.Append(em.GetComponentData<FactionTag>(ents[i]).Value).Append(',')
                         .Append(string.IsNullOrEmpty(id) ? "unknown" : id).Append(',')
                         .Append(p.x.ToString("F0")).Append(',').Append(p.z.ToString("F0"))
                         .Append(',').Append(region).Append('\n');
                }
            Write("Metrics_Placement.csv", place.ToString());

            var res = new StringBuilder("faction,tech\n");
            for (int f = 0; f < 8; f++)
            {
                var faction = (Faction)f;
                // Singleton MonoBehaviour — absent in a scene that never
                // installed it, which a metrics dump must survive.
                if (FactionResearchState.Instance == null) break;
                var done = FactionResearchState.Instance.GetCompletedTechs(faction);
                if (done == null) continue;
                foreach (var tech in done) res.Append(faction).Append(',').Append(tech).Append('\n');
            }
            Write("Metrics_Research.csv", res.ToString());

            // ── kills / deaths, by minute ──
            var combat = new StringBuilder("minute,faction,kills,deaths\n");
            var minutes = new SortedSet<(int, int)>();
            foreach (var k in _kills.Keys) minutes.Add(k);
            foreach (var k in _deaths.Keys) minutes.Add(k);
            foreach (var key in minutes)
            {
                _kills.TryGetValue(key, out int kk);
                _deaths.TryGetValue(key, out int dd);
                combat.Append(key.Item1).Append(',').Append((Faction)key.Item2)
                      .Append(',').Append(kk).Append(',').Append(dd).Append('\n');
            }
            Write("Metrics_Combat.csv", combat.ToString());
        }

        private static void Write(string file, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { System.IO.File.AppendAllText(MatchLogSession.File(file), text); }
            catch { /* diagnostics must never throw into the game */ }
        }
    }
}
