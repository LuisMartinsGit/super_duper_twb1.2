// SimpleAISystem.TowerCoverage.cs
// Watch Towers scattered through every held province (2026-10-04,
// docs/Design/Game_AI.md 5g; operator: "Players should scatter towers through
// the rest of the provinces so they can secure them").
//
// The home keeps its periphery towers and its walls (Territories.cs step 3).
// Every OTHER held province is secured by COVERAGE: its important ground is a
// set of weighted points —
//   * every resource node in it (built on or free)       towerWeightResource
//   * its Fortress, or its reserved Fortress spot         towerWeightFortress
//   * each production building (sites and plans too)      towerWeightProduction
//   * each border sample facing an unowned, curse-held
//     or hostile neighbour (the crossings)               towerWeightBorder
// — and a point is covered while it lies within some own Watch Tower's reach
// (the tower SO's attack range, else its line of sight; never a code number).
//
// Siting is greedy and deterministic: the next site is the province sample
// (a fixed grid flooded from the seed, built once per map) that brings the
// most still-uncovered weight into reach, at least towerMinSpacingRangeFraction
// of the reach from every own tower; ties go to the earlier sample. The tower
// is then placed through the ordinary site search, locked to the province and
// held within towerSiteSearchRadius of the site, so every placement rule (the
// wall corridor, the seal check, the reserved Fortress spot, curse, terrain)
// still applies. A site with no legal footprint is set aside for
// territoryBlockedStepSeconds and the next best is tried (towerSiteTriesPerWalk).
//
// It stops when towerCoverageTarget of the weight is covered, at the cap, or
// when no site adds towerMinGainWeight. The first towersProvinceFirst go in
// step 3 (before production, money holds step 4); the rest are step 5, after
// the province's production, and only while the army is at
// towerExtraArmyFraction of its target — towers never starve the army.
// Partial of SimpleAISystem.cs.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;
using ClaimLinks = TheWaningBorder.Systems.World.TerritoryClaimSystem;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>True only while a coverage tower is placed:
        /// TryFindBuildPosition searches from its site outward, within
        /// towerSiteSearchRadius.</summary>
        private bool _towerSiteExact;

        /// <summary>Where TryBuildBuildingCore last issued a placement.</summary>
        private float3 _lastPlacedPos;

        // The per-province sample grid: map data, built once per map version.
        private struct TowerSample { public float2 P; public int Neighbour; }
        private readonly Dictionary<int, TowerSample[]> _towerSamples = new Dictionary<int, TowerSample[]>();
        private int _towerSamplesVersion = int.MinValue;
        private float _towerSamplesStep = -1f;

        // Host-only managed state (same contract as _claimSquads).
        private struct TowerReject { public int Faction, Region; public float2 P; public float Until; }
        private readonly List<TowerReject> _towerRejects = new List<TowerReject>();
        private readonly HashSet<long> _towerDoneLogged = new HashSet<long>();
        private int _towerEpoch = -1;

        // Scratch.
        private readonly List<float2> _covPts = new List<float2>();
        private readonly List<float> _covW = new List<float>();
        private readonly List<float2> _covTowers = new List<float2>();
        private readonly List<bool> _covDone = new List<bool>();
        private readonly List<int> _towerTried = new List<int>();
        private readonly Queue<int2> _towerFlood = new Queue<int2>();
        private readonly HashSet<long> _towerFloodSeen = new HashSet<long>();
        private readonly List<TowerSample> _towerFloodOut = new List<TowerSample>();

        private static readonly FixedString64Bytes TowerIdFs = new FixedString64Bytes(TowerId);
        private static readonly FixedString64Bytes BarracksIdFs = new FixedString64Bytes("Barracks");
        private static readonly FixedString64Bytes RangeIdFs = new FixedString64Bytes("ArcheryRange");
        private static readonly FixedString64Bytes StableIdFs = new FixedString64Bytes("Alanthor_RoyalStable");
        private static readonly FixedString64Bytes SiegeIdFs = new FixedString64Bytes("Alanthor_SiegeYard");

        /// <summary>
        /// Step 3 (<paramref name="extras"/> false: the first
        /// towersProvinceFirst) or step 5 (true: up to towersPerProvinceMax,
        /// army first) of a province. True when the step is active here — it
        /// placed, is waiting on money / the crew, or (dry) would place; the
        /// caller then returns the step. False lets the next step run;
        /// <paramref name="what"/> may still carry a line for the log.
        /// </summary>
        private bool DevelopProvinceTowers(EntityManager em, Faction faction, int r, float now, bool dry,
            bool extras, out bool placed, out bool crewBlocked, out string what)
        {
            placed = false;
            crewBlocked = false;
            what = null;
            if (_towerEpoch != SimCadence.Epoch)
            {
                _towerEpoch = SimCadence.Epoch;
                _towerRejects.Clear();
                _towerDoneLogged.Clear();
            }

            int step = extras ? StepCoverage : StepTowers;
            // TOWERS ARE A PERSONALITY (2026-10-05, Game_AI.md § 3): the
            // block's towerCoverageScale stretches or shrinks both the
            // per-province cap and the coverage the step is satisfied by.
            float towerScale = math.max(0f, PersonalityOf(faction).towerCoverageScale);
            int max = (int)math.round(math.max(0, Cfg.towersPerProvinceMax) * towerScale);
            if (!extras) max = math.min(max, (int)math.round(math.max(0, Cfg.towersProvinceFirst) * towerScale));
            if (max <= 0) return false;
            if (StepBlocked(faction, r, step, now)) return false;
            // Cut-off ground is wearing down: no new tower there.
            if (!ClaimLinks.IsConnected(r, faction)) return false;
            int have = CountInRegion<WatchTowerTag>(em, faction, r) + CountPlansIn(em, faction, r, TowerId);
            if (have >= max) return false;

            // UNITS BEFORE ECONOMY (Game_AI.md 5h): on a tier that says so a
            // coverage tower waits while the money could still become units;
            // the province's production step runs meanwhile.
            {
                string deferred = BuildingDeferral(em, faction, TowerId, !dry);
                if (deferred != null)
                {
                    if (!dry && AILogger.Enabled)
                        what = $"{StepNames[step]} — waiting (units first: {deferred})";
                    return false;
                }
            }

            if (extras)
            {
                int alive = CountAliveMilitary(em, faction);
                bool known = AIBudget.TryGetArmyDesired(faction, out int desired);
                int need = known ? (int)math.ceil(desired * math.saturate(Cfg.towerExtraArmyFraction)) : int.MaxValue;
                if (alive < need)
                {
                    if (!dry && AILogger.Enabled)
                        what = $"{StepNames[StepCoverage]} — waiting (army first: {alive}/" +
                               $"{(known ? need.ToString() : "?")})";
                    return false;
                }
            }
            if (dry) return true;

            if (!TechCatalog.TryGetBuilding(TowerId, out var def) || def == null) return false;
            float reach = def.attack != null && def.attack.enabled && def.attack.range > 0f
                ? def.attack.range : def.lineOfSight;
            if (reach <= 0f)
            {
                AILogger.Log(faction, "TOWERS", $"{TowerId} SO has no attack range or line of sight — no coverage");
                MarkStepBlocked(faction, r, step, now);
                return false;
            }
            float reachSq = reach * reach;
            float spacing = reach * math.max(0f, Cfg.towerMinSpacingRangeFraction);
            float spacingSq = spacing * spacing;
            string name = RegionMap.NameOf(r);

            CollectCoveragePoints(em, faction, r);
            int n = _covPts.Count;
            float total = 0f;
            for (int i = 0; i < n; i++) total += _covW[i];
            if (n == 0 || total <= 0f)
            {
                MarkStepBlocked(faction, r, step, now);
                return false;
            }
            CollectOwnTowerSites(em, faction);

            _covDone.Clear();
            float covered = 0f;
            for (int i = 0; i < n; i++)
            {
                bool c = InReachOfAny(_covPts[i], _covTowers, reachSq);
                _covDone.Add(c);
                if (c) covered += _covW[i];
            }
            float frac = covered / total;
            long doneKey = ((long)(int)faction << 32) | (uint)r;
            if (frac >= math.saturate(Cfg.towerCoverageTarget * towerScale))
            {
                MarkStepBlocked(faction, r, step, now);
                if (_towerDoneLogged.Add(doneKey))
                    AILogger.Log(faction, "TOWERS", $"{name} coverage {frac * 100f:F0}% — done ({have} tower(s))");
                return false;
            }
            _towerDoneLogged.Remove(doneKey);

            var samples = SamplesOf(r);
            PruneTowerRejects(now);
            _towerTried.Clear();
            int tries = math.max(1, Cfg.towerSiteTriesPerWalk);
            string lastWhy = null;
            for (int t = 0; t < tries; t++)
            {
                int best = -1;
                float bestGain = 0f;
                for (int s = 0; s < samples.Length; s++)
                {
                    var p = samples[s].P;
                    if (_towerTried.Contains(s) || TooNearAny(p, _covTowers, spacingSq)
                        || TowerRejected(faction, r, p)) continue;
                    float gain = 0f;
                    for (int i = 0; i < n; i++)
                        if (!_covDone[i] && math.distancesq(p, _covPts[i]) <= reachSq) gain += _covW[i];
                    if (gain > bestGain) { bestGain = gain; best = s; }
                }
                if (best < 0 || bestGain < math.max(0.0001f, Cfg.towerMinGainWeight))
                {
                    MarkStepBlocked(faction, r, step, now);
                    if (_towerDoneLogged.Add(doneKey))
                        AILogger.Log(faction, "TOWERS",
                            $"{name} coverage {frac * 100f:F0}% — done (no site adds cover; {have} tower(s))");
                    return false;
                }
                _towerTried.Add(best);

                var site = samples[best].P;
                var anchor = new float3(site.x, TerrainUtility.GetHeight(site.x, site.y), site.y);
                bool ok;
                string why;
                _towerSiteExact = true;
                try
                {
                    ok = TryBuildInTerritory(em, faction, TowerId, r, anchor, AIBudgetCategory.Military,
                        true, out why);
                }
                finally { _towerSiteExact = false; }

                if (ok)
                {
                    placed = true;
                    var at = new float2(_lastPlacedPos.x, _lastPlacedPos.z);
                    int k = 0;
                    float gained = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        if (math.distancesq(at, _covPts[i]) > reachSq) continue;
                        k++;
                        if (!_covDone[i]) gained += _covW[i];
                    }
                    float after = (covered + gained) / total;
                    AILogger.Log(faction, "TOWERS",
                        $"{name} {have + 1}/{max} placed at ({at.x:F0},{at.y:F0}) — covers {k}/{n} points " +
                        $"(coverage {frac * 100f:F0}% -> {after * 100f:F0}%, reach {reach:F0} m)");
                    what = $"{StepNames[step]}: tower {have + 1}/{max} at ({at.x:F0},{at.y:F0})";
                    return true;
                }
                if (IsCrewRefusal(why)) { crewBlocked = true; return true; }
                if (IsMoneyRefusal(why))
                {
                    what = $"{StepNames[step]} — waiting ({why})";
                    return true;
                }
                // A search cut short by this think's budget proved nothing.
                if (why != null && why.Contains("budget spent"))
                {
                    what = $"{StepNames[step]} — waiting ({why})";
                    return true;
                }
                // No legal footprint near this site: set it aside, try the next.
                _towerRejects.Add(new TowerReject
                {
                    Faction = (int)faction, Region = r, P = site,
                    Until = now + math.max(1f, Cfg.territoryBlockedStepSeconds),
                });
                lastWhy = why;
            }
            if (AILogger.Enabled)
                what = $"{StepNames[step]} — {tries} site(s) had no legal spot ({lastWhy})";
            return false;
        }

        /// <summary>The province's weighted important points into
        /// _covPts / _covW (see the file header).</summary>
        private void CollectCoveragePoints(EntityManager em, Faction faction, int r)
        {
            _covPts.Clear();
            _covW.Clear();

            // Resource nodes it has seen.
            AddNodePoints(em, faction, r, ComponentType.ReadOnly<SupplyNodeTag>());
            AddNodePoints(em, faction, r, ComponentType.ReadOnly<IronMineTag>());
            AddNodePoints(em, faction, r, ComponentType.ReadOnly<VeilstoneOutcroppingTag>());

            // The Fortress (or its reserved spot) and the production buildings.
            bool fortress = false;
            fortress |= AddOwnPoints<FortressTag>(em, faction, r, Cfg.towerWeightFortress);
            AddOwnPoints<BarracksTag>(em, faction, r, Cfg.towerWeightProduction);
            AddOwnPoints<ArcheryRangeTag>(em, faction, r, Cfg.towerWeightProduction);
            AddOwnPoints<RoyalStableTag>(em, faction, r, Cfg.towerWeightProduction);
            AddOwnPoints<SiegeYardTag>(em, faction, r, Cfg.towerWeightProduction);
            var pq = QC_PlansXf.Get(em, QT_PlansXf);
            if (!pq.IsEmptyIgnoreFilter)
            {
                using var plans = pq.ToComponentDataArray<PlannedBuilding>(Allocator.Temp);
                using var facs = pq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = pq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < plans.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var p = xfs[i].Position;
                    if (RegionMap.RegionAt(p.x, p.z) != r) continue;
                    var id = plans[i].BuildingId;
                    if (id == FortressIdFs) { AddPoint(p, Cfg.towerWeightFortress); fortress = true; }
                    else if (id == BarracksIdFs || id == RangeIdFs || id == StableIdFs || id == SiegeIdFs)
                        AddPoint(p, Cfg.towerWeightProduction);
                }
            }
            if (!fortress && AIBaseLayout.TryGetFortressSpot(faction, r, out float3 spot))
                AddPoint(spot, Cfg.towerWeightFortress);

            // The crossings: border samples facing unowned, curse or hostile ground.
            CollectFrontier(faction, r);
            if (_devFrontier.Count > 0 && Cfg.towerWeightBorder > 0f)
            {
                var samples = SamplesOf(r);
                for (int s = 0; s < samples.Length; s++)
                    if (samples[s].Neighbour >= 0 && _devFrontier.Contains(samples[s].Neighbour))
                    {
                        _covPts.Add(samples[s].P);
                        _covW.Add(Cfg.towerWeightBorder);
                    }
            }
        }

        private void AddPoint(float3 p, float w)
        {
            if (w <= 0f) return;
            _covPts.Add(new float2(p.x, p.z));
            _covW.Add(w);
        }

        private void AddNodePoints(EntityManager em, Faction faction, int r, ComponentType node)
        {
            if (Cfg.towerWeightResource <= 0f) return;
            var q = AIQueryCache.NodeAt(em, node);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                if (RegionMap.RegionAt(p.x, p.z) != r) continue;
                if (!AICommon.IsKnownGround(faction, p)) continue;
                AddPoint(p, Cfg.towerWeightResource);
            }
        }

        private bool AddOwnPoints<T>(EntityManager em, Faction faction, int r, float w)
            where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagFactionXf<T>(em);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            bool any = false;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var p = xfs[i].Position;
                if (RegionMap.RegionAt(p.x, p.z) != r) continue;
                AddPoint(p, w);
                any = true;
            }
            return any;
        }

        /// <summary>Every own Watch Tower (finished, site or plan), anywhere —
        /// a neighbour's tower covers this province's edge too.</summary>
        private void CollectOwnTowerSites(EntityManager em, Faction faction)
        {
            _covTowers.Clear();
            var q = AIQueryCache.TagFactionXf<WatchTowerTag>(em);
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                    if (facs[i].Value == faction)
                        _covTowers.Add(new float2(xfs[i].Position.x, xfs[i].Position.z));
            var pq = QC_PlansXf.Get(em, QT_PlansXf);
            if (pq.IsEmptyIgnoreFilter) return;
            using var plans = pq.ToComponentDataArray<PlannedBuilding>(Allocator.Temp);
            using var pf = pq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var px = pq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < plans.Length; i++)
                if (pf[i].Value == faction && plans[i].BuildingId == TowerIdFs)
                    _covTowers.Add(new float2(px[i].Position.x, px[i].Position.z));
        }

        private static bool InReachOfAny(float2 p, List<float2> towers, float reachSq)
        {
            for (int i = 0; i < towers.Count; i++)
                if (math.distancesq(p, towers[i]) <= reachSq) return true;
            return false;
        }

        private static bool TooNearAny(float2 p, List<float2> towers, float spacingSq)
        {
            if (spacingSq <= 0f) return false;
            for (int i = 0; i < towers.Count; i++)
                if (math.distancesq(p, towers[i]) < spacingSq) return true;
            return false;
        }

        private bool TowerRejected(Faction faction, int r, float2 p)
        {
            for (int i = 0; i < _towerRejects.Count; i++)
            {
                var j = _towerRejects[i];
                if (j.Faction == (int)faction && j.Region == r && math.distancesq(j.P, p) < 1f) return true;
            }
            return false;
        }

        private void PruneTowerRejects(float now)
        {
            for (int i = _towerRejects.Count - 1; i >= 0; i--)
                if (now >= _towerRejects[i].Until) _towerRejects.RemoveAt(i);
        }

        /// <summary>
        /// The province's sample grid: cells towerSampleStep apart, flooded
        /// (breadth-first, fixed neighbour order) from the seed through the
        /// cells that lie in the province, up to towerSampleMaxCells. A cell
        /// whose grid neighbour lies in another claimable region is a border
        /// sample facing that region. Map data only — cached per map version.
        /// </summary>
        private TowerSample[] SamplesOf(int r)
        {
            float step = math.max(2f, Cfg.towerSampleStep);
            if (_towerSamplesVersion != RegionMap.Version || _towerSamplesStep != step)
            {
                _towerSamples.Clear();
                _towerSamplesVersion = RegionMap.Version;
                _towerSamplesStep = step;
            }
            if (_towerSamples.TryGetValue(r, out var cached)) return cached;

            var seed = RegionMap.SeedOf(r);
            float2 origin = new float2(seed.x, seed.y);
            _towerFlood.Clear();
            _towerFloodSeen.Clear();
            _towerFloodOut.Clear();

            // Start at the seed cell, or the nearest cell of the province
            // within a few steps when the warp moved the seed's cell out.
            bool found = false;
            for (int ring = 0; ring <= 6 && !found; ring++)
                for (int dz = -ring; dz <= ring && !found; dz++)
                    for (int dx = -ring; dx <= ring && !found; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != ring) continue;
                        var w = origin + new float2(dx, dz) * step;
                        if (RegionMap.RegionAt(w.x, w.y) != r) continue;
                        var c0 = new int2(dx, dz);
                        _towerFlood.Enqueue(c0);
                        _towerFloodSeen.Add(CellKey(c0));
                        found = true;
                    }

            int cap = math.max(1, Cfg.towerSampleMaxCells);
            while (_towerFlood.Count > 0 && _towerFloodOut.Count < cap)
            {
                var c = _towerFlood.Dequeue();
                var p = origin + new float2(c.x, c.y) * step;
                int facing = -1;
                for (int d = 0; d < 4; d++)
                {
                    var nc = c + (d == 0 ? new int2(1, 0) : d == 1 ? new int2(-1, 0)
                                : d == 2 ? new int2(0, 1) : new int2(0, -1));
                    var np = origin + new float2(nc.x, nc.y) * step;
                    int nr = RegionMap.RegionAt(np.x, np.y);
                    if (nr == r)
                    {
                        if (_towerFloodSeen.Add(CellKey(nc))) _towerFlood.Enqueue(nc);
                    }
                    else if (nr >= 0 && facing < 0) facing = nr;
                }
                _towerFloodOut.Add(new TowerSample { P = p, Neighbour = facing });
            }
            var arr = _towerFloodOut.ToArray();
            _towerSamples[r] = arr;
            return arr;
        }

        private static long CellKey(int2 c) => ((long)c.x << 32) ^ (uint)c.y;
    }
}
