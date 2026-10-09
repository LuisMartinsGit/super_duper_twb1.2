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
//                          minute (fed live by DeathSystem), plus melee
//                          hits and flank hits landed (fed by
//                          MeleeCombatSystem; Combat_Pacing.md § Flanking)
//   Metrics_Deaths.csv     every unit death as an EVENT: time, victim,
//                          killer, position — where the battles were
//   Metrics_UnitPositions.csv  every unit's position, sampled every other
//                          tick (30 s) — the match unfolding on the map
//   Metrics_Income.csv     per sample: GROSS income by source and spending
//                          by category, per faction, over the sample period
//                          (fed by EconomyLedger; 2026-10-04)
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using TheWaningBorder.Core;
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
        static readonly ComponentType[] QT_BuildingTagFactionTag =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTag;
        static readonly ComponentType[] QT_UnitTypeIdFactionTag =
        {
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_UnitTypeIdFactionTag;
        static readonly ComponentType[] QT_UnitTypeIdFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitTypeIdFactionTagLocalTransform;

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_BuildingTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTagLocalTransform;

        #endregion
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
        // Melee hits / flank hits LANDED, keyed by the attacker's faction.
        private static readonly Dictionary<(int, int), int> _meleeHits = new();
        private static readonly Dictionary<(int, int), int> _flankHits = new();

        /// <summary>One melee swing landed; <paramref name="flank"/> when it
        /// was outside the defender's front arc. Counters only — nothing is
        /// logged per hit.</summary>
        public static void RecordMeleeHit(Faction attacker, bool flank)
        {
            if (!Enabled) return;
            var key = ((int)(MatchTime / 60f), (int)attacker);
            _meleeHits[key] = _meleeHits.TryGetValue(key, out int m) ? m + 1 : 1;
            if (flank)
                _flankHits[key] = _flankHits.TryGetValue(key, out int f) ? f + 1 : 1;
        }

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
            _meleeHits.Clear();
            _flankHits.Clear();
            _deathEvents.Clear();
            EconomyLedger.Reset();
            _prevBankValid = false;
        }

        private float _t;
        private float _next;
        private float _lastSim = -1f;
        private bool _headers;
        private EntityWorld _world;

        private void Update()
        {
            if (!Enabled) return;
            // SIMULATED seconds (2026-10-07): the simulation's step is capped
            // at the world's maximum delta, so on a heavy headless frame at 3x
            // it falls behind Time.deltaTime — an 8-player "120-minute" batch
            // match had simulated about 60. The metrics are about the match,
            // so they count the match's own clock.
            float sim = TheWaningBorder.Core.SimClock.Now;
            if (_lastSim < 0f || sim < _lastSim) _lastSim = sim;
            _t += sim - _lastSim;
            _lastSim = sim;
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
            Write("Metrics_Income.csv",
                "t,faction,flow,source,supplies,iron,veilstone,veilsteel\n");
            Write("Metrics_Score.csv", MatchScore.CsvHeader);
        }

        // ── income / spending ledger (2026-10-04) ──
        // The bank is income minus spending; this writes the two halves.
        // One row per (faction, flow, source) that moved this sample period:
        //   flow   in  = gross income, by EconomyLedger.IncomeSource
        //          out = spending, by EconomyLedger.SpendCategory
        //   amounts are what moved during the period that ENDS at t (one
        //   SampleInterval), so a row's amount * 60 / SampleInterval is a
        //   per-minute rate.
        // "untracked" is the residual: the bank moved by something that did
        // not go through FactionEconomy.Add/Spend (a direct bank write). It
        // is reported, never hidden, so the ledger always balances.
        private readonly int[,] _prevBank = new int[8, 4];
        private bool _prevBankValid;
        private static readonly string[] IncomeNames =
        {
            "emptySlot", "gatherersHut", "mine", "veilstoneMine", "fortressLevel",
            "capital", "buildingPassive", "trade", "vault", "curseKill", "loot",
            "refund", "grant", "guildSurvey", "territoryClaim", "other",
        };
        private static readonly string[] SpendNames =
        {
            "units", "buildings", "upgrades", "research", "ageUp", "trade",
            "repair", "religion", "vault", "overflow", "other",
        };

        private static void AppendFlow(StringBuilder sb, string t, Faction faction, string flow,
            string source, float a, float b, float c, float d)
        {
            if (System.Math.Abs(a) < 0.05f && System.Math.Abs(b) < 0.05f
                && System.Math.Abs(c) < 0.05f && System.Math.Abs(d) < 0.05f) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append(t).Append(',').Append(faction).Append(',').Append(flow).Append(',')
              .Append(source).Append(',')
              .Append(a.ToString("0.#", inv)).Append(',').Append(b.ToString("0.#", inv)).Append(',')
              .Append(c.ToString("0.#", inv)).Append(',').Append(d.ToString("0.#", inv)).Append('\n');
        }

        /// <summary>Write this period's ledger for one faction, plus the
        /// untracked residual against its bank delta.</summary>
        private void SampleIncome(StringBuilder sb, string t, int f, int su, int ir, int ve, int vs)
        {
            var faction = (Faction)f;
            var net = new float[4];
            for (int s = 0; s < (int)IncomeSource.Count; s++)
            {
                var src = (IncomeSource)s;
                float a = EconomyLedger.IncomeOf(f, src, 0), b = EconomyLedger.IncomeOf(f, src, 1),
                      c = EconomyLedger.IncomeOf(f, src, 2), d = EconomyLedger.IncomeOf(f, src, 3);
                net[0] += a; net[1] += b; net[2] += c; net[3] += d;
                AppendFlow(sb, t, faction, "in", IncomeNames[s], a, b, c, d);
            }
            for (int k = 0; k < (int)SpendCategory.Count; k++)
            {
                var cat = (SpendCategory)k;
                float a = EconomyLedger.SpendOf(f, cat, 0), b = EconomyLedger.SpendOf(f, cat, 1),
                      c = EconomyLedger.SpendOf(f, cat, 2), d = EconomyLedger.SpendOf(f, cat, 3);
                net[0] -= a; net[1] -= b; net[2] -= c; net[3] -= d;
                AppendFlow(sb, t, faction, "out", SpendNames[k], a, b, c, d);
            }

            var bank = new[] { su, ir, ve, vs };
            if (_prevBankValid)
            {
                // Residual = what the bank did minus what the ledger saw. The
                // territory tick's fractional carry makes up to ~1 unit of
                // noise per resource, so anything under 2 is dropped.
                var res = new float[4];
                bool any = false;
                for (int r = 0; r < 4; r++)
                {
                    float x = bank[r] - _prevBank[f, r] - net[r];
                    if (System.Math.Abs(x) >= 2f) { res[r] = x; any = true; }
                }
                if (any)
                {
                    AppendFlow(sb, t, faction, "in", "untracked",
                        System.Math.Max(0f, res[0]), System.Math.Max(0f, res[1]),
                        System.Math.Max(0f, res[2]), System.Math.Max(0f, res[3]));
                    AppendFlow(sb, t, faction, "out", "untracked",
                        System.Math.Max(0f, -res[0]), System.Math.Max(0f, -res[1]),
                        System.Math.Max(0f, -res[2]), System.Math.Max(0f, -res[3]));
                }
            }
            for (int r = 0; r < 4; r++) _prevBank[f, r] = bank[r];
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
            var q = QC_BuildingTagFactionTagLocalTransform.Get(em, QT_BuildingTagFactionTagLocalTransform);
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

            // POSITIONS EVERY SAMPLE (2026-09-12). This used to skip every
            // other sample, so the map replay had one frame per 30 s — ten
            // frames for a five-minute match, which is a slideshow, not a
            // replay. At 15 s a viewer can interpolate between frames and the
            // file is still a few hundred KB for a long match.
            var q = QC_UnitTypeIdFactionTagLocalTransform.Get(em, QT_UnitTypeIdFactionTagLocalTransform);
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
            var uq = QC_UnitTypeIdFactionTag.Get(em, QT_UnitTypeIdFactionTag);
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
            var bq = QC_BuildingTagFactionTag.Get(em, QT_BuildingTagFactionTag);
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
            var inc = new StringBuilder();
            string tStr = t.ToString("F0");
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

                SampleIncome(inc, tStr, f, su, ir, ve, vs);

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
            Write("Metrics_Income.csv", inc.ToString());
            // The period is closed: every faction's rows are written and its
            // bank snapshot taken. A faction skipped above (no units, no
            // buildings) has its period discarded with the reset.
            EconomyLedger.Reset();
            _prevBankValid = true;
            Write("Metrics_Units.csv", un.ToString());
            Write("Metrics_Buildings.csv", bl.ToString());

            // ── the Score (MatchScoreSystem's latest rows, docs/Design/Score.md) ──
            var sc = new StringBuilder();
            MatchScore.AppendCsvRows(sc, t);
            if (sc.Length > 0) Write("Metrics_Score.csv", sc.ToString());
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
            var bq = QC_BuildingTagFactionTagLocalTransform.Get(em, QT_BuildingTagFactionTagLocalTransform);
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
            var combat = new StringBuilder("minute,faction,kills,deaths,meleeHits,flankHits\n");
            var minutes = new SortedSet<(int, int)>();
            foreach (var k in _kills.Keys) minutes.Add(k);
            foreach (var k in _deaths.Keys) minutes.Add(k);
            foreach (var k in _meleeHits.Keys) minutes.Add(k);
            foreach (var key in minutes)
            {
                _kills.TryGetValue(key, out int kk);
                _deaths.TryGetValue(key, out int dd);
                _meleeHits.TryGetValue(key, out int mh);
                _flankHits.TryGetValue(key, out int fh);
                combat.Append(key.Item1).Append(',').Append((Faction)key.Item2)
                      .Append(',').Append(kk).Append(',').Append(dd)
                      .Append(',').Append(mh).Append(',').Append(fh).Append('\n');
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
