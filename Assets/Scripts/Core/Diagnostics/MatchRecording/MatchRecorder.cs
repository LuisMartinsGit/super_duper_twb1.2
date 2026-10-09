// MatchRecorder.cs
// Records EVERY match into memory for the post-game Muster Rolls screen
// (docs/Design/Muster_Rolls_PostGame.md): the faction series the charts draw,
// and the map, buildings, unit positions and deaths the animated map replays.
//
// WHY NOT MapTrace / MatchMetrics. Those two write the same facts to the
// match's log folder, but only in headless batch runs (they write big files,
// and a player's disk is not a test harness). The post-game screen needs the
// facts in EVERY match, so this keeps them in memory, compactly:
//   * positions are stored only when a unit moved or changed state, in
//     decimetres, and a "still here" sample is written just before the next
//     change so a stationary unit does not drift under interpolation;
//   * past MatchRecorderConfig.maxUnitSamples every track drops every other
//     sample and the position period doubles -- the record thins, it never
//     stops and never grows without bound;
//   * territory ownership is an event stream read off
//     TerritoryOwnership.Version, never a per-sample snapshot.
//
// SIM TIME. Every timestamp is the simulation clock (SimCadence.MatchTimeOr
// over SimClock), so a replay of a match records the same timeline.
//
// PER-MATCH STATE. The recorder lives on the DontDestroyOnLoad RuntimeManagers
// object and survives matches; it starts a fresh MatchRecord whenever
// MatchLifecycle.MatchEpoch moves, and stops recording on the rising edge of
// MatchLifecycle.MatchDecided (or Finish()), so the record shows the match and
// not the board the sim keeps running under the victory screen.
//
// READ-ONLY on the world: it never writes simulation state, so it cannot
// affect lockstep.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Diagnostics.MatchRecording
{
    public sealed class MatchRecorder : MonoBehaviour
    {
        static MatchRecorderConfig _cfg;
        static MatchRecorderConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<MatchRecorderConfig>());

        public static MatchRecorder Instance { get; private set; }

        private static readonly ComponentType[] QT_Unit =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static CachedEntityQuery QC_Unit;

        private static readonly ComponentType[] QT_Building =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static CachedEntityQuery QC_Building;

        // ── per-match state (reset by BeginRecord) ──
        private MatchRecord _rec;
        private int _epoch = int.MinValue;
        private bool _started;
        private bool _partitionDone;
        private bool _decidedLatch;
        private float _lastT;
        private float _nextPos;
        private float _nextSeries;
        private int _ownVersion = int.MinValue;
        private int[] _ownLast = Array.Empty<int>();
        private int _stamp;
        private readonly Dictionary<Entity, int> _unitIndex = new Dictionary<Entity, int>();
        private readonly List<int> _liveUnits = new List<int>();
        private readonly List<int> _unitStamp = new List<int>();
        private readonly Dictionary<Entity, int> _buildingIndex = new Dictionary<Entity, int>();
        private readonly List<int> _liveBuildings = new List<int>();
        private readonly List<int> _buildingStamp = new List<int>();
        private readonly List<Entity> _buildingEntity = new List<Entity>();
        private bool _thinWarned;

        // Scratch per series sample.
        private readonly int[] _army = new int[MatchRecord.Factions];
        private readonly int[] _workers = new int[MatchRecord.Factions];
        private readonly int[] _buildings = new int[MatchRecord.Factions];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── public hooks ─────────────────────────────────────────────────

        /// <summary>A unit died here (DeathSystem). Recorded at the current
        /// sim time while a match is being recorded; ignored otherwise.</summary>
        public static void NoteUnitDeath(Faction victim, float x, float z)
        {
            var me = Instance;
            if (me == null || me._rec == null || !me._started || me._rec.Ended) return;
            me._rec.DeathList.Add(new DeathRec
            {
                T = Now(), Faction = (int)victim, X = x, Z = z,
            });
        }

        /// <summary>Stop recording the current match (a final sample is
        /// taken). The decided-match edge calls this; a replay that ends may
        /// call it too. Idempotent.</summary>
        public static void Finish()
        {
            var me = Instance;
            if (me == null || me._rec == null || me._rec.Ended) return;
            me.FinishRecord();
        }

        // ── the loop ─────────────────────────────────────────────────────

        private static float Now()
            => (float)SimCadence.MatchTimeOr(SimClock.Elapsed);

        /// <summary>
        /// Run one recording step now. The recorder samples once a frame, which
        /// is once every few SIM seconds while a save fast-forwards to its
        /// marker or a replay skips ahead; LockstepManager calls this after each
        /// tick it runs flat out, so the timeline keeps its resolution.
        /// docs/Design/Replays_And_Saves.md
        /// </summary>
        public static void Pump()
        {
            var me = Instance;
            if (me != null) me.Step();
        }

        private void Update() => Step();

        private void Step()
        {
            if (_epoch != MatchLifecycle.MatchEpoch)
            {
                _epoch = MatchLifecycle.MatchEpoch;
                BeginRecord();
            }
            if (_rec == null || _rec.Ended) return;
            if (!MatchLifecycle.MapPopulated) return;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var cfg = Cfg;
            if (cfg == null) return;

            float t = Now();
            if (!_started)
            {
                Header(em);
                _started = true;
                _nextPos = t;
                _nextSeries = t;
            }
            if (t < _lastT) t = _lastT;   // never let the timeline run backwards
            _lastT = t;
            _rec.Duration = t;

            if (!_partitionDone && RegionMap.Ready) Partition(cfg);
            if (_partitionDone) Ownership(t);

            if (t >= _nextPos)
            {
                _nextPos = t + Mathf.Max(0.1f, _rec.PositionPeriod);
                SampleMap(em, t, cfg);
            }
            if (t >= _nextSeries)
            {
                _nextSeries = t + Mathf.Max(0.5f, cfg.seriesIntervalSeconds);
                SampleSeries(em, t);
            }

            // Stop on the RISING edge of the decided flag: a flag left set by
            // the previous match must first clear.
            bool decided = MatchLifecycle.MatchDecided;
            if (decided && !_decidedLatch)
            {
                _rec.Winner = MatchLifecycle.MatchWinner ?? "";
                FinishRecord();
            }
            _decidedLatch = decided;
        }

        private void BeginRecord()
        {
            _rec = new MatchRecord();
            MatchRecord.Current = _rec;
            _rec.PositionPeriod = Cfg != null ? Mathf.Max(0.1f, Cfg.positionIntervalSeconds) : 1f;
            _started = false;
            _partitionDone = false;
            _decidedLatch = MatchLifecycle.MatchDecided;
            _lastT = 0f;
            _nextPos = 0f;
            _nextSeries = 0f;
            _ownVersion = int.MinValue;
            _ownLast = Array.Empty<int>();
            _stamp = 0;
            _unitIndex.Clear();
            _liveUnits.Clear();
            _unitStamp.Clear();
            _buildingIndex.Clear();
            _liveBuildings.Clear();
            _buildingStamp.Clear();
            _buildingEntity.Clear();
            _thinWarned = false;
        }

        private void FinishRecord()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_started && world != null && world.IsCreated && Cfg != null)
            {
                float t = Mathf.Max(_lastT, Now());
                _rec.Duration = t;
                try
                {
                    SampleMap(world.EntityManager, t, Cfg);
                    SampleSeries(world.EntityManager, t);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[MatchRecorder] final sample failed: " + e.Message);
                }
            }
            _rec.Ended = true;
        }

        // ── header ───────────────────────────────────────────────────────

        private void Header(EntityManager em)
        {
            if (!TheWaningBorder.World.Terrain.TerrainUtility.TryGetWorldBounds(out Vector2 min, out Vector2 max))
                TheWaningBorder.World.Terrain.TerrainUtility.GetPlayableBounds(out min, out max);
            _rec.WorldMin = min;
            _rec.WorldMax = max;

            string scene = GameSettings.SelectedMapScene;
            if (string.IsNullOrEmpty(scene))
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            _rec.MapScene = scene ?? "";
            MapInfo info = null;
            try { info = string.IsNullOrEmpty(scene) ? null : MapInfoIndex.For(scene); }
            catch (Exception) { info = null; }
            _rec.MapThumbnail = info != null ? info.Thumbnail : null;
            _rec.MapName = info != null && !string.IsNullOrEmpty(info.DisplayName) ? info.DisplayName : _rec.MapScene;

            _rec.LocalFaction = (int)GameSettings.LocalPlayerFaction;
            _rec.Observer = GameSettings.IsObserver;
            for (int f = 0; f < MatchRecord.Factions; f++)
                _rec.FactionColor[f] = FactionColors.Get((Faction)f);

            Nodes<IronMineTag>(em, "iron");
            Nodes<VeilstoneOutcroppingTag>(em, "veilstone");
            Nodes<SupplyNodeTag>(em, "supply");
        }

        private void Nodes<T>(EntityManager em, string kind) where T : unmanaged, IComponentData
        {
            // Created and disposed here on purpose: this runs ONCE per match,
            // so a cached query would outlive its world for nothing (MapTrace
            // does the same).
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(),
                                         ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                    _rec.NodeList.Add(new NodeRec { Kind = kind, X = xfs[i].Position.x, Z = xfs[i].Position.z });
            q.Dispose();
        }

        /// <summary>The partition as the game plays it (RegionMap.RegionAt at
        /// every cell centre, on the build grid), once per match.</summary>
        private void Partition(MatchRecorderConfig cfg)
        {
            _partitionDone = true;
            int count = RegionMap.Count;
            for (int i = 0; i < count; i++)
            {
                var seed = RegionMap.SeedOf(i);
                _rec.RegionList.Add(new RegionRec { Name = RegionMap.NameOf(i), X = seed.x, Z = seed.y });
            }

            Vector2 min = _rec.WorldMin, max = _rec.WorldMax;
            int maxCells = Mathf.Max(16, cfg.partitionMaxCells);
            float cell = BuildGrid.CellSize;
            while (Mathf.Max(max.x - min.x, max.y - min.y) / cell > maxCells) cell *= 2f;
            float x0 = Mathf.Floor(min.x / cell) * cell;
            float z0 = Mathf.Floor(min.y / cell) * cell;
            int w = Mathf.Max(1, Mathf.CeilToInt((max.x - x0) / cell - 1e-4f));
            int h = Mathf.Max(1, Mathf.CeilToInt((max.y - z0) / cell - 1e-4f));
            var cells = new int[w * h];
            for (int j = 0; j < h; j++)
            {
                float wz = z0 + (j + 0.5f) * cell;
                for (int i = 0; i < w; i++)
                    cells[j * w + i] = RegionMap.RegionAt(x0 + (i + 0.5f) * cell, wz);
            }
            _rec.TerritoryCell = cell;
            _rec.TerritoryX0 = x0;
            _rec.TerritoryZ0 = z0;
            _rec.TerritoryW = w;
            _rec.TerritoryH = h;
            _rec.TerritoryCells = cells;
        }

        /// <summary>Owner-change events, read only when
        /// TerritoryOwnership.Version moves.</summary>
        private void Ownership(float t)
        {
            if (!TerritoryOwnership.Ready) return;
            int count = _rec.RegionList.Count;
            if (_ownVersion == TerritoryOwnership.Version && _ownLast.Length == count) return;
            _ownVersion = TerritoryOwnership.Version;
            if (_ownLast.Length != count)
            {
                _ownLast = new int[count];
                for (int i = 0; i < count; i++) _ownLast[i] = MatchRecord.OwnerNone;
            }
            for (int i = 0; i < count; i++)
            {
                int o = TerritoryOwnership.OwnerOf(i);
                if (o != TerritoryOwnership.Curse && (o < 0 || o >= MatchRecord.Factions))
                    o = MatchRecord.OwnerNone;
                else if (o == TerritoryOwnership.Curse) o = MatchRecord.OwnerCurse;
                if (o == _ownLast[i]) continue;
                _ownLast[i] = o;
                _rec.OwnerEventList.Add(new OwnerEvent { T = t, Territory = i, Owner = o });
            }
        }

        // ── units and buildings ──────────────────────────────────────────

        private void SampleMap(EntityManager em, float t, MatchRecorderConfig cfg)
        {
            _stamp++;
            int thr = Mathf.Max(0, Mathf.RoundToInt(cfg.minMoveMetres * 10f));

            var uq = QC_Unit.Get(em, QT_Unit);
            using (var ents = uq.ToEntityArray(Allocator.Temp))
            using (var xfs = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var facs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var tags = uq.ToComponentDataArray<UnitTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    if (!_unitIndex.TryGetValue(e, out int idx))
                    {
                        byte kind = 0;
                        if (em.HasComponent<CavalryTag>(e)) kind |= UnitTrack.KindCavalry;
                        if (em.HasComponent<HeroLevel>(e)) kind |= UnitTrack.KindHero;
                        if (em.HasComponent<BorderUnitTag>(e)) kind |= UnitTrack.KindCurse;
                        int fac = (int)facs[i].Value;
                        var track = new UnitTrack
                        {
                            Faction = fac,
                            Class = (int)tags[i].Class,
                            Kind = kind,
                            TypeId = em.HasComponent<UnitTypeId>(e)
                                ? em.GetComponentData<UnitTypeId>(e).Value.ToString() : "",
                            Born = t,
                        };
                        idx = _rec.UnitList.Count;
                        _rec.UnitList.Add(track);
                        _unitStamp.Add(0);
                        _unitIndex[e] = idx;
                        _liveUnits.Add(idx);
                        if (fac >= 0 && fac < MatchRecord.Factions) _rec.FactionPresent[fac] = true;
                    }
                    _unitStamp[idx] = _stamp;

                    byte state = 0;
                    if (em.HasComponent<DesiredDestination>(e)
                        && em.GetComponentData<DesiredDestination>(e).Has != 0) state |= UnitTrack.StateMoving;
                    if (TransientState.Active<FormationMemberState>(em, e)) state |= UnitTrack.StateFormation;
                    if (em.HasComponent<Target>(e)
                        && em.GetComponentData<Target>(e).Value != Entity.Null) state |= UnitTrack.StateFighting;

                    var p = xfs[i].Position;
                    Observe(_rec.UnitList[idx], t,
                        (int)math.round(p.x * 10f), (int)math.round(p.z * 10f), state, thr);
                }

            for (int k = _liveUnits.Count - 1; k >= 0; k--)
            {
                int idx = _liveUnits[k];
                if (_unitStamp[idx] == _stamp) continue;
                _rec.UnitList[idx].Died = t;
                _liveUnits.RemoveAt(k);
            }
            // Entity keys of the dead are never matched again (the version
            // moves on reuse), but the dictionary would keep growing.
            if (_unitIndex.Count > _liveUnits.Count * 2 + 256) PruneUnitIndex();

            var bq = QC_Building.Get(em, QT_Building);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    bool site = em.HasComponent<UnderConstruction>(e);
                    if (!_buildingIndex.TryGetValue(e, out int idx))
                    {
                        var xf = xfs[i];
                        float w = 4f, h = 4f;
                        if (em.HasComponent<BuildingSize>(e))
                        {
                            var bs = em.GetComponentData<BuildingSize>(e);
                            w = Mathf.Max(1, bs.Width); h = Mathf.Max(1, bs.Height);
                        }
                        int fac = (int)facs[i].Value;
                        var rec = new BuildingRec
                        {
                            Faction = fac,
                            TypeId = BuildingType(em, e),
                            X = xf.Position.x,
                            Z = xf.Position.z,
                            W = w,
                            H = h,
                            Yaw = Yaw(xf.Rotation),
                            Born = t,
                            Completed = site ? float.PositiveInfinity : t,
                        };
                        idx = _rec.BuildingList.Count;
                        _rec.BuildingList.Add(rec);
                        _buildingStamp.Add(0);
                        _buildingEntity.Add(e);
                        _buildingIndex[e] = idx;
                        _liveBuildings.Add(idx);
                        if (fac >= 0 && fac < MatchRecord.Factions) _rec.FactionPresent[fac] = true;
                    }
                    _buildingStamp[idx] = _stamp;
                    var b = _rec.BuildingList[idx];
                    if (!site && float.IsPositiveInfinity(b.Completed)) b.Completed = t;
                    int lv = BuildingLevel(em, e);
                    int lastLv = b.Levels.Count > 0 ? b.Levels[b.Levels.Count - 1].level : 0;
                    if (lv != lastLv) b.Levels.Add((t, lv));
                }

            for (int k = _liveBuildings.Count - 1; k >= 0; k--)
            {
                int idx = _liveBuildings[k];
                if (_buildingStamp[idx] == _stamp) continue;
                _rec.BuildingList[idx].Died = t;
                _buildingIndex.Remove(_buildingEntity[idx]);
                _liveBuildings.RemoveAt(k);
            }

            if (_rec.UnitSampleTotal > Mathf.Max(1000, cfg.maxUnitSamples)) Thin();
        }

        /// <summary>Store a position only when it changed (see the header).</summary>
        private void Observe(UnitTrack tr, float t, int x, int z, byte state, int thr)
        {
            var s = new UnitSample { T = t, X = x, Z = z, State = state };
            var list = tr.Samples;
            int n = list.Count;
            if (n > 0)
            {
                var last = list[n - 1];
                if (Math.Abs(x - last.X) <= thr && Math.Abs(z - last.Z) <= thr && state == last.State)
                {
                    tr.Pending = new UnitSample { T = t, X = last.X, Z = last.Z, State = last.State };
                    tr.HasPending = true;
                    return;
                }
                if (tr.HasPending && tr.Pending.T > last.T)
                {
                    list.Add(tr.Pending);
                    _rec.UnitSampleTotal++;
                }
            }
            tr.HasPending = false;
            list.Add(s);
            _rec.UnitSampleTotal++;
        }

        /// <summary>The memory cap: every track keeps its first and last
        /// samples and every other one between; the period doubles.</summary>
        private void Thin()
        {
            int total = 0;
            foreach (var tr in _rec.UnitList)
            {
                var list = tr.Samples;
                int n = list.Count;
                if (n > 2)
                {
                    int w = 1;
                    for (int r = 2; r < n - 1; r += 2) list[w++] = list[r];
                    list[w++] = list[n - 1];
                    list.RemoveRange(w, n - w);
                }
                total += list.Count;
            }
            _rec.UnitSampleTotal = total;
            _rec.PositionPeriod *= 2f;
            if (!_thinWarned)
            {
                _thinWarned = true;
                Debug.Log($"[MatchRecorder] memory cap reached: thinned to {total} unit samples, " +
                          $"position period now {_rec.PositionPeriod:0.#} s.");
            }
        }

        private void PruneUnitIndex()
        {
            var live = new HashSet<int>(_liveUnits);
            var dead = new List<Entity>();
            foreach (var kv in _unitIndex) if (!live.Contains(kv.Value)) dead.Add(kv.Key);
            foreach (var e in dead) _unitIndex.Remove(e);
        }

        // ── faction series ───────────────────────────────────────────────

        private void SampleSeries(EntityManager em, float t)
        {
            Array.Clear(_army, 0, _army.Length);
            Array.Clear(_workers, 0, _workers.Length);
            Array.Clear(_buildings, 0, _buildings.Length);

            var uq = QC_Unit.Get(em, QT_Unit);
            using (var facs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var tags = uq.ToComponentDataArray<UnitTag>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MatchRecord.Factions) continue;
                    var c = tags[i].Class;
                    if (c == UnitClass.Worker || c == UnitClass.Economy) _workers[f]++;
                    else _army[f]++;
                }
            var bq = QC_Building.Get(em, QT_Building);
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f >= 0 && f < MatchRecord.Factions) _buildings[f]++;
                }

            _rec.SeriesTimeList.Add(t);
            for (int f = 0; f < MatchRecord.Factions; f++)
            {
                var faction = (Faction)f;
                int su = 0, ir = 0, ve = 0, vs = 0;
                if (FactionEconomy.TryGetBank(em, faction, out var bank)
                    && em.HasComponent<FactionResources>(bank))
                {
                    var r = em.GetComponentData<FactionResources>(bank);
                    su = r.Supplies; ir = r.Iron; ve = r.Veilstone; vs = r.Veilsteel;
                }
                int pop = 0, popMax = 0;
                if (_army[f] + _workers[f] + _buildings[f] > 0)
                    PopulationHelper.TryGetFactionPopulation(faction, out pop, out popMax);
                int terr = TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(faction) : 0;
                MatchScore.TryGet(faction, out var sc);

                Put(RecordSeries.Score, f, sc.Score);
                Put(RecordSeries.Economy, f, sc.Economy);
                Put(RecordSeries.Strategy, f, sc.Strategy);
                Put(RecordSeries.Military, f, sc.Military);
                Put(RecordSeries.KillDeath, f, MatchScore.KillsOf(faction) / (float)Mathf.Max(1, MatchScore.DeathsOf(faction)));
                Put(RecordSeries.Kills, f, MatchScore.KillsOf(faction));
                Put(RecordSeries.Deaths, f, MatchScore.DeathsOf(faction));
                Put(RecordSeries.Supplies, f, su);
                Put(RecordSeries.Iron, f, ir);
                Put(RecordSeries.Veilstone, f, ve);
                Put(RecordSeries.Veilsteel, f, vs);
                Put(RecordSeries.Population, f, pop);
                Put(RecordSeries.PopulationMax, f, popMax);
                Put(RecordSeries.Army, f, _army[f]);
                Put(RecordSeries.Workers, f, _workers[f]);
                Put(RecordSeries.Buildings, f, _buildings[f]);
                Put(RecordSeries.Territories, f, terr);
                Put(RecordSeries.Fortresses, f, sc.Fortresses);
                Put(RecordSeries.Techs, f, sc.Techs);
            }
        }

        private void Put(RecordSeries s, int f, float v)
            => _rec.SeriesValues[(int)s * MatchRecord.Factions + f].Add(v);

        // ── helpers (the same reads MapTrace makes) ──────────────────────

        private static string BuildingType(EntityManager em, Entity e)
        {
            string id = TheWaningBorder.Entities.BuildingIds.Of(e, em);
            if (!string.IsNullOrEmpty(id)) return id;
            if (em.HasComponent<BorderMainNodeTag>(e)) return "CurseWell";
            if (em.HasComponent<SmallNodeTag>(e)) return "CurseNode";
            return "";
        }

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

        private static float Yaw(quaternion q)
        {
            float3 fwd = math.mul(q, new float3(0f, 0f, 1f));
            return math.degrees(math.atan2(fwd.x, fwd.z));
        }
    }
}
