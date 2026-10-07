// SimpleAISystem.Territories.cs
// Developing every held territory, in the operator's order (2026-10-04,
// docs/Design/Game_AI.md 5g):
//
//   1. resource buildings — an extractor on every free node it holds there
//      (Gatherer's Huts, Mines, Veilstone Mines, Trading Outposts on outcrop
//      sides);
//   2. the Fortress — as soon as the resource buildings are up (2026-10-04
//      revision); its footprint is RESERVED from the moment the territory is
//      held (AIBaseLayout), kept clear by every other AI placer the way the
//      wall corridor is, so it can always be placed. EnsureFortressExpansion
//      places it; this walk holds steps 3-4 behind it;
//   3. Watch Towers: in the HOME near its periphery, facing hostile or
//      unowned neighbours (towersPerTerritory); in every other province
//      the first towersProvinceFirst COVERAGE-sited towers
//      (SimpleAISystem.TowerCoverage.cs);
//   4. provinceProductionPerTerritory production buildings (the tier's
//      profile: one on Normal) — the line the army plan needs most that the
//      territory lacks, the Barracks when the plan names nothing else — so
//      units are trained near the front. The HOME is larger and keeps a
//      FLOOR instead: the tier's homeProductionPerLine of EACH line
//      the faction can build, every line to 1 before any line to 2, the most
//      needed first (DevelopHomeProduction; one of each before
//      homeProductionFloorAfterSeconds).
//   5. (provinces) more coverage towers, up to towersPerProvinceMax or until
//      towerCoverageTarget of the province's important ground is in reach —
//      army first: only while the army is near its target.
//
// Every production building past that waits on SATURATION (the existing
// production's queues busy for the tier's productionSaturationSeconds) and an
// army below its target — ProductionGate, which every global production path
// obeys. Harder tiers get more of all three (2026-10-05, developer: "Harder AIs
// should have more parallel military training facilities"): the capacity is
// AIDifficultyProfileSO data, not SimpleAISystem.asset.
//
// A step blocked by money holds the steps below it in THAT territory (the
// priority is the point); a step blocked by placement (no legal spot) does not
// — it is logged and the next step runs. At most one placement per walk.
// Partial of SimpleAISystem.cs.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;
using Plans = TheWaningBorder.Entities.PlannedBuildings;
using OutpostIds = TheWaningBorder.Entities.TradingOutpost;   // avoids the DC0062 Entities.ForEach misread

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        private const int StepResources = 1, StepFortress = 2, StepTowers = 3,
                          StepProduction = 4, StepCoverage = 5, StepDone = 6;

        private const string TowerId = "Alanthor_Tower";

        private static readonly string[] StepNames =
            { "", "resource buildings", "Fortress", "watch towers", "production", "tower coverage", "developed" };

        // Host-only managed state (same contract as _claimSquads).
        private readonly Dictionary<int, int[]> _territoryStep = new Dictionary<int, int[]>();
        private readonly Dictionary<int, float[]> _territoryNextPlace = new Dictionary<int, float[]>();
        private readonly Dictionary<int, float> _nextTerritoryDevelop = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextTerritoryReport = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextSpotFailLog = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _spotRetryAt = new Dictionary<int, float>();
        /// <summary>Per faction, [region * StepSlots + step] -> sim time until
        /// which that step counts as blocked by PLACEMENT there (no legal
        /// spot): skipped, and treated as done by the dry evaluation.</summary>
        private readonly Dictionary<int, float[]> _territoryBlockedUntil = new Dictionary<int, float[]>();
        private const int StepSlots = 7;
        private int _territoryWalk;
        private int _territoryEpoch = -1;

        /// <summary>True only while EnsureFortressExpansion places a Fortress
        /// on its reserved spot — TryFindBuildPosition then tries that exact
        /// spot first.</summary>
        private bool _fortressSpotExact;

        // Scratch.
        private readonly HashSet<int> _devOwned = new HashSet<int>();
        private readonly List<float3> _devNodes = new List<float3>();
        private readonly List<int> _devFrontier = new List<int>();
        private readonly List<int> _prodTerritories = new List<int>();
        private readonly System.Text.StringBuilder _devReport = new System.Text.StringBuilder();

        private int[] StepsOf(int key)
        {
            if (!_territoryStep.TryGetValue(key, out var a) || a.Length != RegionMap.Count)
                _territoryStep[key] = a = new int[RegionMap.Count];
            return a;
        }

        private float[] BlockedOf(int key)
        {
            int n = RegionMap.Count * StepSlots;
            if (!_territoryBlockedUntil.TryGetValue(key, out var a) || a.Length != n)
                _territoryBlockedUntil[key] = a = new float[n];
            return a;
        }

        private bool StepBlocked(Faction faction, int r, int step, float now)
            => now < BlockedOf((int)faction)[r * StepSlots + step];

        private void MarkStepBlocked(Faction faction, int r, int step, float now)
            => BlockedOf((int)faction)[r * StepSlots + step] = now + math.max(1f, Cfg.territoryBlockedStepSeconds);

        private readonly Dictionary<int, int[]> _territoryLoggedStep = new Dictionary<int, int[]>();

        private int[] LoggedStepOf(int key)
        {
            if (!_territoryLoggedStep.TryGetValue(key, out var a) || a.Length != RegionMap.Count)
                _territoryLoggedStep[key] = a = new int[RegionMap.Count];
            return a;
        }

        private float[] NextPlaceOf(int key)
        {
            if (!_territoryNextPlace.TryGetValue(key, out var a) || a.Length != RegionMap.Count)
                _territoryNextPlace[key] = a = new float[RegionMap.Count];
            return a;
        }

        /// <summary>Has this territory finished (or been blocked out of) its
        /// resource step? The Fortress (step 2) waits on it, and nothing
        /// else.</summary>
        private bool TerritoryResourcesDone(Faction faction, int region)
        {
            var steps = StepsOf((int)faction);
            return region >= 0 && region < steps.Length && steps[region] >= StepFortress;
        }

        private void TickTerritoryDevelopment(EntityManager em, Faction faction, float now)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready || !TechCatalog.IsReady) return;
            if (_territoryEpoch != SimCadence.Epoch)
            {
                _territoryEpoch = SimCadence.Epoch;
                _territoryStep.Clear();
                _territoryNextPlace.Clear();
                _nextTerritoryDevelop.Clear();
                _nextTerritoryReport.Clear();
                _nextSpotFailLog.Clear();
                _spotRetryAt.Clear();
                _territoryBlockedUntil.Clear();
                _territoryLoggedStep.Clear();
                _homeLineBlockedUntil.Clear();
            }

            int key = (int)faction;
            if (!_nextTerritoryDevelop.ContainsKey(key))
            {
                // Staggered by faction, like the extractor walk.
                _nextTerritoryDevelop[key] = now + Cfg.territoryDevelopInterval * ((key & 7) / 8f);
                return;
            }
            if (now < _nextTerritoryDevelop[key]) return;
            // PACE IS THE LADDER (2026-10-05, Game_AI.md § 2).
            _nextTerritoryDevelop[key] = now + math.max(1f,
                Cfg.territoryDevelopInterval * math.max(0.1f, ProfileOf(faction).TerritoryCadenceScale));

            if (FindFactionBuilding<HallTag>(em, faction) == Entity.Null) return;
            int home = AIWallPlanner.HomeRegionOf(em, faction);
            var mine = TerritoryOwnership.TerritoriesOf(faction);
            var steps = StepsOf(key);
            var nextPlace = NextPlaceOf(key);

            // Spots for ground no longer held, or now carrying a Fortress, go.
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (!AIBaseLayout.TryGetFortressSpot(faction, r, out _)) continue;
                if (TerritoryOwnership.OwnerOf(r) != key || FortressStandsIn(em, r))
                {
                    AIBaseLayout.ClearFortressSpot(faction, r);
                    if (TerritoryOwnership.OwnerOf(r) != key) steps[r] = 0;
                }
            }

            // Step 2 cannot happen at all before the age-up or at the
            // Fortress ceiling: then it never holds steps 3-4.
            _devFortressImpossible = !HasAgedUp(em, faction)
                || CountOwnFortresses(em, faction) >= Cfg.fortressMaxPerFaction;

            bool placedThisWalk = false, crewBlocked = false, spotSearched = false;
            // Round-robin start, so a walk cut short by a placement or by the
            // crew cap does not always favour the lowest-index territory.
            int start = mine.Count > 0 ? (_territoryWalk++ & 0x7fffffff) % mine.Count : 0;
            for (int ii = 0; ii < mine.Count; ii++)
            {
                int r = mine[(start + ii) % mine.Count];
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                bool isHome = r == home;
                if (!isHome && !TerritoryOwnership.IsClaimed(r)) continue;

                // THE FORTRESS SPOT, from the moment the ground is held.
                if (!isHome && !FortressStandsIn(em, r))
                    EnsureFortressSpot(em, faction, r, now, ref spotSearched);

                // Once the walk has placed (or the crew is full), or while this
                // territory's last order is still landing, the rest are only
                // EVALUATED (dry) — so the Fortress gate (step 4) still sees
                // every territory's progress.
                bool dry = placedThisWalk || crewBlocked || now < nextPlace[r];

                int step = DevelopTerritory(em, faction, r, isHome, now, dry,
                    out bool placed, out bool crew, out string what);
                steps[r] = step;
                if (placed)
                {
                    placedThisWalk = true;
                    nextPlace[r] = now + Cfg.territoryStepCooldown;
                    LoggedStepOf(key)[r] = step;
                    AILogger.Log(faction, "TERRITORY",
                        $"{RegionMap.NameOf(r)}: step {step} {what}");
                }
                else if (what != null && step != LoggedStepOf(key)[r])
                {
                    LoggedStepOf(key)[r] = step;
                    AILogger.Log(faction, "TERRITORY",
                        $"{RegionMap.NameOf(r)}: step {step} {what}");
                }
                crewBlocked |= crew;
            }

            if (!_nextTerritoryReport.TryGetValue(key, out float rep) || now >= rep)
            {
                _nextTerritoryReport[key] = now + math.max(5f, Cfg.territoryReportInterval);
                ReportTerritoryProduction(em, faction, mine, home, steps);
            }
        }

        /// <summary>
        /// Run one territory's build order. Returns the step it is on; places
        /// at most one building (<paramref name="placed"/>). <paramref
        /// name="crewBlocked"/> says the faction has no crew / site capacity,
        /// which stops the whole walk.
        /// </summary>
        private int DevelopTerritory(EntityManager em, Faction faction, int r, bool isHome, float now,
            bool dry, out bool placed, out bool crewBlocked, out string what)
        {
            placed = false;
            crewBlocked = false;
            what = null;
            bool alanthor = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor;

            // ── STEP 1: resource buildings on every free node here ──────────
            {
                _devOwned.Clear();
                _devOwned.Add(r);
                var snap = BuildSiteSnapshot.Current(em);
                int free = 0, tried = 0;
                string blocked = null;
                bool moneyHold = false;
                bool step1Blocked = StepBlocked(faction, r, StepResources, now);
                for (int k = 0; k < ExtractorPlan.Length && !placed; k++)
                {
                    string id = ExtractorPlan[k].Building;
                    bool isOutpost = id == OutpostIds.BuildingId;
                    if (isOutpost && !alanthor) continue;
                    if (id == "VeilstoneMine" && alanthor) continue;
                    if (id != "GatherersHut" && AIPivotalReserve.Has(faction, HutBootstrapReserveKey)) continue;
                    if (!TechCatalog.TryGetBuilding(id, out var def) || def == null) continue;

                    _devNodes.Clear();
                    CollectFreeNodes(em, faction, id, _devOwned, _devNodes);
                    int2 size = BuildingSizeConfig.GetSize(id);
                    for (int n = 0; n < _devNodes.Count && !placed; n++)
                    {
                        if (snap.OverlapsOwnPlan(faction, _devNodes[n], size)) continue;
                        free++;
                        var cost = isOutpost ? BuildCosts.For(em, faction, id, _devNodes[n])
                                             : AICommon.ToCost(def.cost);
                        if (!FactionEconomy.CanAfford(em, faction, cost))
                        {
                            // An Outpost's ramp price is its own savings goal
                            // (EnsureExtractors); the core extractors are
                            // what this territory waits on.
                            if (!isOutpost) moneyHold = true;
                            blocked = $"{id} bank short";
                            continue;
                        }
                        if (step1Blocked) continue;
                        if (dry) return StepResources;   // it would place here
                        tried++;
                        if (TryBuildBuildingWithReason(em, faction, id, out string why, _devNodes[n]))
                        {
                            placed = true;
                            what = $"{StepNames[StepResources]}: {id} at ({_devNodes[n].x:F0},{_devNodes[n].z:F0})";
                            return StepResources;
                        }
                        blocked = $"{id}: {why}";
                        if (IsCrewRefusal(why)) { crewBlocked = true; return StepResources; }
                    }
                }
                if (free > 0 && moneyHold)
                {
                    if (!dry) what = $"{StepNames[StepResources]} — waiting ({blocked}; {free} free node(s))";
                    return StepResources;
                }
                if (!dry && tried > 0) MarkStepBlocked(faction, r, StepResources, now);
                // free > 0 but every one refused for its ground: logged by the
                // extractor walk; the next step runs.
            }

            // ── STEP 2: the Fortress (never in the home: the capital is there) ─
            // It goes as soon as the resource buildings are up. Placed by
            // EnsureFortressExpansion (on the reserved spot); while it is
            // due, towers and production here wait for it. It does not hold
            // them when it cannot happen: before the age-up, at the Fortress
            // ceiling, or while its site here was refused (fortressSiteRetrySeconds).
            if (!isHome && !FortressStandsIn(em, r) && !_devFortressImpossible
                && !FortressSiteBlocked(faction, r, now))
            {
                if (!dry)
                    what = AIBaseLayout.TryGetFortressSpot(faction, r, out float3 spot)
                        ? $"{StepNames[StepFortress]} next (spot reserved at ({spot.x:F0},{spot.z:F0}))"
                        : $"{StepNames[StepFortress]} next (no spot reserved yet)";
                return StepFortress;
            }

            // ── STEP 3 (PROVINCE): the first coverage-sited towers ─────────
            if (!isHome && alanthor && HasAgedUp(em, faction))
            {
                if (DevelopProvinceTowers(em, faction, r, now, dry, false,
                        out bool tPlaced, out bool tCrew, out string tWhat))
                {
                    placed = tPlaced;
                    crewBlocked = tCrew;
                    what = tWhat;
                    return StepTowers;
                }
                if (tWhat != null) what = tWhat;
            }

            // ── STEP 3 (HOME): watch towers on the periphery ───────────────
            if (isHome && alanthor && Cfg.towersPerTerritory > 0 && HasAgedUp(em, faction))
            {
                CollectFrontier(faction, r);
                if (_devFrontier.Count > 0)
                {
                    int have = CountInRegion<WatchTowerTag>(em, faction, r) + CountPlansIn(em, faction, r, TowerId);
                    // UNITS BEFORE ECONOMY (Game_AI.md 5h): a tower is economy
                    // drive on a tier that says so; while deferred the next
                    // step (the production) runs.
                    if (have < Cfg.towersPerTerritory && !StepBlocked(faction, r, StepTowers, now)
                        && BuildingDeferral(em, faction, TowerId, !dry) == null)
                    {
                        if (dry) return StepTowers;
                        int nb = _devFrontier[have % _devFrontier.Count];
                        var a = RegionMap.SeedOf(r);
                        var b = RegionMap.SeedOf(nb);
                        float f = math.saturate(Cfg.towerPeripheryFraction);
                        float ax = a.x + (b.x - a.x) * f, az = a.y + (b.y - a.y) * f;
                        var anchor = new float3(ax, TerrainUtility.GetHeight(ax, az), az);
                        if (TryBuildInTerritory(em, faction, TowerId, r, anchor, AIBudgetCategory.Military,
                                true, out string why))
                        {
                            placed = true;
                            what = $"{StepNames[StepTowers]}: tower {have + 1}/{Cfg.towersPerTerritory} " +
                                   $"facing {RegionMap.NameOf(nb)}";
                            return StepTowers;
                        }
                        if (IsCrewRefusal(why)) { crewBlocked = true; return StepTowers; }
                        if (IsMoneyRefusal(why))
                        {
                            what = $"{StepNames[StepTowers]} — waiting ({why})";
                            return StepTowers;
                        }
                        // No legal ground for it: the next step runs.
                        MarkStepBlocked(faction, r, StepTowers, now);
                    }
                }
            }

            // ── STEP 4 (HOME): the home production floor ───────────────────
            if (isHome)
                return DevelopHomeProduction(em, faction, r, now, dry, out placed, out crewBlocked, out what);

            // ── STEP 4: the province's production — the lines the army needs ─
            // provinceProductionPerTerritory per province (the tier's profile;
            // the home keeps its floor instead, above), breadth-first: every
            // line to 1 before any line to 2. Within a tier the line with the
            // largest shortfall of the army plan's share against its share of
            // the faction's trainers, among the lines this territory has
            // fewest of; the Barracks when the plan names nothing else.
            // Every production building past this waits on saturation
            // (ProductionGate) — this step is the one that does not.
            {
                int perProvince = math.max(1, ProfileOf(faction).ProvinceProductionPerTerritory);
                CountProductionIn(em, faction, r, out int bar, out int rng, out int stb, out int sie);
                int total = bar + rng + stb + sie;
                if (total < perProvince
                    && !StepBlocked(faction, r, StepProduction, now))
                {
                    if (dry) return StepProduction;
                    string want = null, because = null;
                    for (int t = 1; t <= perProvince && want == null; t++)
                    {
                        int have = (bar >= t ? 1 : 0) | (rng >= t ? 2 : 0) | (stb >= t ? 4 : 0) | (sie >= t ? 8 : 0);
                        want = ChooseNeededLine(em, faction, have, out because);
                    }
                    if (want != null)
                    {
                        var core = AIBaseLayout.CoreOf(em, faction, r);
                        var anchor = new float3(core.x, TerrainUtility.GetHeight(core.x, core.z), core.z);
                        bool ok;
                        string why;
                        _provinceProductionStep = true;
                        try
                        {
                            ok = TryBuildInTerritory(em, faction, want, r, anchor, AIBudgetCategory.Military,
                                true, out why);
                        }
                        finally { _provinceProductionStep = false; }
                        if (ok)
                        {
                            placed = true;
                            what = $"{StepNames[StepProduction]}: {want} ({total + 1}/" +
                                   $"{perProvince} here)";
                            AILogger.Log(faction, "PRODUCTION",
                                $"province {RegionMap.NameOf(r)} gets {want} ({because})");
                            return StepProduction;
                        }
                        if (IsCrewRefusal(why)) { crewBlocked = true; return StepProduction; }
                        if (IsMoneyRefusal(why))
                        {
                            what = $"{StepNames[StepProduction]} — waiting ({want}: {why})";
                            return StepProduction;
                        }
                        what = $"{StepNames[StepProduction]} — {want} has no legal spot ({why})";
                        // Placement-blocked: retried after territoryBlockedStepSeconds.
                        MarkStepBlocked(faction, r, StepProduction, now);
                    }
                }
            }

            // ── STEP 5 (PROVINCE): coverage towers past the first ──────────
            // After the province's production, and army first.
            if (alanthor && HasAgedUp(em, faction))
            {
                if (DevelopProvinceTowers(em, faction, r, now, dry, true,
                        out bool tPlaced, out bool tCrew, out string tWhat))
                {
                    placed = tPlaced;
                    crewBlocked = tCrew;
                    what = tWhat;
                    return StepCoverage;
                }
                if (tWhat != null) what = tWhat;
            }

            return StepDone;
        }

        // ─────────────────────────────────────────────────────────────────
        // THE HOME PRODUCTION FLOOR (2026-10-04, Game_AI.md 5g)
        //
        // Operator: "Home province is larger, it should have at least 2 of
        // each building." The home keeps homeProductionPerLine of EACH
        // production line the faction can build — the Barracks always, the
        // Archery Range / Royal Stable / Siege Yard once an Alanthor faction
        // has aged up (ChooseNeededLine's availability rule) — counting
        // finished buildings, sites and plans in the home territory. It
        // fills breadth-first: every line to 1 before any line to 2, so a
        // missing line comes before a duplicate; within a tier the line the
        // army plan needs most goes first (ChooseNeededLine's ranking), then
        // any line the plan does not name, in LostTrainerLines order.
        //
        // It passes the saturation gate (ProductionGate) like a province's
        // step 4, and nothing else: the placement, the wall corridor / seal
        // checks, the Military wallet and the savings hold (pivotal reserve:
        // the hut-first opening, the age-up save) apply as to any production
        // building. Before homeProductionFloorAfterSeconds it asks for one of
        // each only. It replaces the economy's redundantBarracksCount floor.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>True only while the home floor places — ProductionGate
        /// lets it through.</summary>
        private bool _homeProductionFloor;

        /// <summary>Per faction, per LostTrainerLines entry: sim time until
        /// which that line's home-floor placement counts as blocked by
        /// placement (no legal spot); the other lines still run.</summary>
        private readonly Dictionary<int, float[]> _homeLineBlockedUntil = new Dictionary<int, float[]>();

        // Scratch: the home's count per LostTrainerLines entry.
        private static readonly int[] _homeLineCount = new int[4];

        private float[] HomeLineBlockedOf(int key)
        {
            if (!_homeLineBlockedUntil.TryGetValue(key, out var a) || a.Length != LostTrainerLines.Length)
                _homeLineBlockedUntil[key] = a = new float[LostTrainerLines.Length];
            return a;
        }

        /// <summary>Step 4 of the home territory: the floor above. Places at
        /// most one building; StepProduction while the floor is short and
        /// placeable, StepDone when it is met (or every short line is blocked
        /// by placement for now).</summary>
        private int DevelopHomeProduction(EntityManager em, Faction faction, int r, float now, bool dry,
            out bool placed, out bool crewBlocked, out string what)
        {
            placed = false;
            crewBlocked = false;
            what = null;
            if (StepBlocked(faction, r, StepProduction, now)) return StepDone;

            int perLine = now >= Cfg.homeProductionFloorAfterSeconds
                ? math.max(1, ProfileOf(faction).HomeProductionPerLine) : 1;
            bool others = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor
                          && HasAgedUp(em, faction);
            CountProductionIn(em, faction, r,
                out _homeLineCount[0], out _homeLineCount[1], out _homeLineCount[2], out _homeLineCount[3]);
            var blocked = HomeLineBlockedOf((int)faction);

            string want = null, because = null;
            int all = (1 << LostTrainerLines.Length) - 1;
            for (int t = 1; t <= perLine && want == null; t++)
            {
                int exclude = 0;
                for (int l = 0; l < LostTrainerLines.Length; l++)
                {
                    bool available = l == 0 || others;
                    if (!available || _homeLineCount[l] >= t || now < blocked[l]) exclude |= 1 << l;
                }
                if (exclude == all) continue;
                want = ChooseNeededLine(em, faction, exclude, out because);
                if (want == null)
                    for (int l = 0; l < LostTrainerLines.Length; l++)
                        if ((exclude & (1 << l)) == 0)
                        {
                            want = LostTrainerLines[l];
                            because = "the plan names no line still short here";
                            break;
                        }
            }
            if (want == null) return StepDone;
            int line = -1;
            for (int l = 0; l < LostTrainerLines.Length; l++)
                if (LostTrainerLines[l] == want) { line = l; break; }
            if (line < 0) return StepDone;
            if (dry) return StepProduction;

            int n = _homeLineCount[line] + 1;
            var core = AIBaseLayout.CoreOf(em, faction, r);
            var anchor = new float3(core.x, TerrainUtility.GetHeight(core.x, core.z), core.z);
            bool ok;
            string why;
            _homeProductionFloor = true;
            try
            {
                ok = TryBuildInTerritory(em, faction, want, r, anchor, AIBudgetCategory.Military,
                    true, out why);
            }
            finally { _homeProductionFloor = false; }
            if (ok)
            {
                placed = true;
                what = $"{StepNames[StepProduction]}: home floor {want} {n}/{perLine}";
                if (AILogger.Enabled)
                    AILogger.Log(faction, "PRODUCTION", $"home floor {want} {n}/{perLine} ({because})");
                return StepProduction;
            }
            if (IsCrewRefusal(why)) { crewBlocked = true; return StepProduction; }
            if (IsMoneyRefusal(why))
            {
                what = $"{StepNames[StepProduction]} — home floor waiting ({want} {n}/{perLine}: {why})";
                return StepProduction;
            }
            // No legal spot for THIS line: it waits territoryBlockedStepSeconds
            // while the other short lines go on.
            blocked[line] = now + math.max(1f, Cfg.territoryBlockedStepSeconds);
            what = $"{StepNames[StepProduction]} — home floor {want} {n}/{perLine} has no legal spot ({why})";
            return StepDone;
        }

        private static bool IsCrewRefusal(string why)
            => why != null && (why == "no build crew" || why == "sites open" || why.Contains("sites open, crew"));

        private static bool IsMoneyRefusal(string why)
            => why != null && (why.StartsWith("bank short") || why == "wallet short"
                               || why.StartsWith("pivotal hold") || why.StartsWith("veilstone earmarked"));

        /// <summary>
        /// Place <paramref name="buildingId"/> inside territory <paramref
        /// name="region"/>, the search anchored at <paramref name="anchor"/>,
        /// paid from the <paramref name="cat"/> wallet.
        /// </summary>
        private bool TryBuildInTerritory(EntityManager em, Faction faction, string buildingId, int region,
            float3 anchor, AIBudgetCategory cat, bool honourReservation, out string reason)
        {
            reason = null;
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null)
            { reason = "no catalog def"; return false; }
            var cost = AICommon.ToCost(def.cost);
            if (!AIBudget.TryAfford(faction, cat, cost, TheWaningBorder.Core.SimClock.Now, honourReservation))
            { reason = "wallet short"; return false; }
            int prevLock = _siteRegionLock;
            _siteRegionLock = region;
            bool ok;
            try { ok = TryBuildBuildingWithReason(em, faction, buildingId, out reason, anchor); }
            finally { _siteRegionLock = prevLock; }
            if (ok) AIBudget.RecordSpend(faction, cat, cost);
            return ok;
        }

        // ─────────────────────────────────────────────────────────────────
        // PRODUCTION IN EVERY PROVINCE (Game_AI.md 5g)
        //
        // An un-anchored production-building request (the goal list, the
        // economy's per-line growth, the lost-trainer rebuild) no longer rings
        // the home capital only. Headless29: 1,678 "on wall corridor ...
        // search failed" lines, every one a Barracks / Archery Range / Royal
        // Stable / Siege Yard (plus a few towers) refused because the home
        // ring was full — past-ring 100-425, gap 52-240 and spacing 26-130 of
        // 247-672 candidates — while the provinces stood empty. It now tries
        // up to productionSiteTerritoriesPerCall held territories, the least
        // equipped (and the frontier) first, each searched inside its own
        // ground from its core.
        // ─────────────────────────────────────────────────────────────────

        private bool TryBuildProductionAcrossTerritories(EntityManager em, Faction faction,
            string buildingId, out string reason)
        {
            reason = null;
            int home = AIWallPlanner.HomeRegionOf(em, faction);
            var mine = TerritoryOwnership.TerritoriesOf(faction);
            var steps = StepsOf((int)faction);

            _prodTerritories.Clear();
            for (int i = 0; i < mine.Count; i++)
            {
                int r = mine[i];
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (r != home)
                {
                    if (!TerritoryOwnership.IsClaimed(r)) continue;
                    // Cut-off ground is wearing down: no new trainer there.
                    if (!TheWaningBorder.Systems.World.TerritoryClaimSystem.IsConnected(r, faction)) continue;
                    // Its own resources, Fortress and towers come first (steps 1-3).
                    if (steps[r] != 0 && steps[r] < StepProduction) continue;
                }
                _prodTerritories.Add(r);
            }
            if (_prodTerritories.Count == 0) { reason = "no territory to build in"; return false; }

            // Least production first, then frontier, then home, then index:
            // deterministic, and the army is trained near the front.
            var counts = new int[_prodTerritories.Count];
            var front = new bool[_prodTerritories.Count];
            for (int i = 0; i < _prodTerritories.Count; i++)
            {
                CountProductionIn(em, faction, _prodTerritories[i], out int b, out int a, out int s, out int y);
                counts[i] = b + a + s + y;
                CollectFrontier(faction, _prodTerritories[i]);
                front[i] = _devFrontier.Count > 0;
            }
            var order = new int[_prodTerritories.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (p, q) =>
            {
                int c = counts[p].CompareTo(counts[q]);
                if (c != 0) return c;
                if (front[p] != front[q]) return front[p] ? -1 : 1;
                bool hp = _prodTerritories[p] == home, hq = _prodTerritories[q] == home;
                if (hp != hq) return hp ? -1 : 1;
                return _prodTerritories[p].CompareTo(_prodTerritories[q]);
            });

            int tries = math.max(1, Cfg.productionSiteTerritoriesPerCall);
            string reasons = null;
            for (int k = 0; k < order.Length && k < tries; k++)
            {
                int r = _prodTerritories[order[k]];
                var core = AIBaseLayout.CoreOf(em, faction, r);
                var anchor = new float3(core.x, TerrainUtility.GetHeight(core.x, core.z), core.z);
                int prevLock = _siteRegionLock;
                _siteRegionLock = r;
                bool ok;
                string why;
                try { ok = TryBuildBuildingCore(em, faction, buildingId, out why, anchor); }
                finally { _siteRegionLock = prevLock; }
                if (ok)
                {
                    AILogger.Log(faction, "BUILDING",
                        $"{buildingId} sited in {RegionMap.NameOf(r)} " +
                        $"({counts[order[k]]} production building(s) there before)");
                    return true;
                }
                // Anything but "no spot here" is the same answer everywhere.
                if (why == null || !why.StartsWith("no legal") && why != "site search budget spent")
                {
                    reason = why;
                    return false;
                }
                if (AILogger.Enabled)
                    reasons = (reasons == null ? "" : reasons + "; ") + $"{RegionMap.NameOf(r)}: {why}";
                else reasons = why;
            }
            reason = reasons ?? "no legal spot";
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // ONE PER PROVINCE, THEN ONLY WHEN SATURATED (2026-10-04 revision,
        // Game_AI.md 5g)
        //
        // Headless30 vs Headless29, after "production on every province"
        // went in: building spend per faction-minute rose 537 -> 801,
        // production buildings standing at the end 286 -> 393, and the
        // typical late army FELL 56 -> 28. The buildings were bought with the
        // army's money and then stood idle. So: each province gets ONE
        // production building (step 4), the line the army plan needs most;
        // any further production building — from the goal list, the
        // economy's per-line growth, the siege
        // program, anywhere — needs the existing production SATURATED (its
        // queues busy, productionSaturationThreshold of the finished trainers,
        // unbroken for productionSaturationSeconds) AND an army below its
        // target, and goes to the line the plan needs most. Exempt: the
        // province step, the home floor, the lost-trainer rebuild, and the
        // very first Barracks of a faction with no production at all.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>True only while step 4 places its province's production
        /// building — ProductionGate lets it through.</summary>
        private bool _provinceProductionStep;

        /// <summary>Set once per walk: no Fortress can be placed at all (not
        /// aged up, or at fortressMaxPerFaction), so step 2 holds nothing.</summary>
        private bool _devFortressImpossible;

        // Host-only managed state, sim time (same contract as _claimSquads).
        private readonly Dictionary<int, float> _prodSatSince = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _prodBusyFrac = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _prodHeldLogAt = new Dictionary<int, float>();
        // (faction, line) -> sim time until which a saturation extra of that
        // line is not asked for: its last placement found no legal spot. A
        // line with nowhere to go must not veto the lines that do (2026-10-05:
        // Expert held 3 trainers for 20 min on an unplaceable Siege Yard).
        private readonly Dictionary<(int, int), float> _extraLineBlocked = new Dictionary<(int, int), float>();
        private int _prodEpoch = -1;

        // Scratch for ChooseNeededLine (LostTrainerLines order).
        private static readonly float[] _linePlan = new float[4];
        private static readonly int[] _lineTrainers = new int[4];

        /// <summary>Every think: the fraction of finished production
        /// buildings whose queue holds work, and since when it has stayed at
        /// or above productionSaturationThreshold.</summary>
        private void SampleProductionSaturation(EntityManager em, Faction faction, float now)
        {
            if (_prodEpoch != SimCadence.Epoch)
            {
                _prodEpoch = SimCadence.Epoch;
                _prodSatSince.Clear();
                _prodBusyFrac.Clear();
                _prodHeldLogAt.Clear();
                _extraLineBlocked.Clear();
            }
            int finished = 0, busy = 0, belowDepth = 0;
            var profile = ProfileOf(faction);
            // "At depth" for UNITS BEFORE ECONOMY: the tier's queue depth,
            // or one item (training) on a tier with no AI-side cap.
            int depth = math.max(1, profile.ProductionQueueDepth);
            CountBusyTrainers<BarracksTag>(em, faction, depth, ref finished, ref busy, ref belowDepth);
            CountBusyTrainers<ArcheryRangeTag>(em, faction, depth, ref finished, ref busy, ref belowDepth);
            CountBusyTrainers<RoyalStableTag>(em, faction, depth, ref finished, ref busy, ref belowDepth);
            CountBusyTrainers<SiegeYardTag>(em, faction, depth, ref finished, ref busy, ref belowDepth);
            // The economy drive's gate (Game_AI.md 5h) reads this beside the
            // army reading ReplaceLostUnits wrote this think.
            AIBudget.SetTrainerStatus(faction, profile.UnitsBeforeEconomy, finished, belowDepth);
            int key = (int)faction;
            float frac = finished > 0 ? busy / (float)finished : 0f;
            _prodBusyFrac[key] = frac;
            if (finished > 0 && frac >= ProfileOf(faction).ProductionSaturationThreshold)
            {
                if (!_prodSatSince.ContainsKey(key)) _prodSatSince[key] = now;
            }
            else _prodSatSince.Remove(key);
        }

        private static void CountBusyTrainers<T>(EntityManager em, Faction faction, int depth,
            ref int finished, ref int busy, ref int belowDepth)
            where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagFaction<T>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                finished++;
                int len = TheWaningBorder.Core.Commands.CommandRouter.GetProductionQueueLength(em, ents[i]);
                if (len > 0) busy++;
                if (len < depth
                    && !TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, ents[i]))
                    belowDepth++;
            }
        }

        /// <summary>
        /// The production line the army plan needs most: per line, the plan's
        /// share (the raw composition wish of the units that building's SO
        /// trains, over every line) minus the line's share of the faction's
        /// production buildings (sites and plans included); the largest
        /// shortfall wins, ties to the earlier line. Lines in <paramref
        /// name="excludeMask"/> (bit per LostTrainerLines entry), lines the
        /// faction cannot build yet (everything but the Barracks before an
        /// Alanthor age-up) and lines the plan does not want are skipped. The
        /// Barracks when nothing else qualifies; null when it is excluded too.
        /// </summary>
        private string ChooseNeededLine(EntityManager em, Faction faction, int excludeMask, out string because)
        {
            because = null;
            bool others = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor
                          && HasAgedUp(em, faction);
            float planTotal = 0f;
            int trainerTotal = 0;
            for (int l = 0; l < LostTrainerLines.Length; l++)
            {
                _linePlan[l] = 0f;
                _lineTrainers[l] = CountLine(em, faction, l);
                trainerTotal += _lineTrainers[l];
            }
            var p = GetArmyPlan(em, faction);
            for (int r = 0; r < p.N; r++)
            {
                if (p.Rows[r] == null || p.Raw[r] <= 0f) continue;
                string unit = p.Rows[r].unitId;
                for (int l = 0; l < LostTrainerLines.Length; l++)
                    if (TrainsUnit(em, LostTrainerLines[l], unit))
                    {
                        _linePlan[l] += p.Raw[r];
                        planTotal += p.Raw[r];
                        break;
                    }
            }

            int best = -1;
            float bestShort = float.MinValue, bestPlan = 0f, bestHave = 0f;
            if (planTotal > 0f)
                for (int l = 0; l < LostTrainerLines.Length; l++)
                {
                    if ((excludeMask & (1 << l)) != 0) continue;
                    if (l > 0 && !others) continue;
                    if (_linePlan[l] <= 0f) continue;
                    float planShare = _linePlan[l] / planTotal;
                    float haveShare = trainerTotal > 0 ? _lineTrainers[l] / (float)trainerTotal : 0f;
                    float shortfall = planShare - haveShare;
                    if (shortfall > bestShort)
                    {
                        bestShort = shortfall;
                        best = l;
                        bestPlan = planShare;
                        bestHave = haveShare;
                    }
                }
            if (best >= 0)
            {
                if (AILogger.Enabled)
                    because = $"shortfall {bestShort * 100f:+0;-0} pts: plan {bestPlan * 100f:F0}% vs {bestHave * 100f:F0}% of " +
                              $"{trainerTotal} trainer(s)";
                return LostTrainerLines[best];
            }
            if ((excludeMask & 1) != 0) return null;
            because = "the plan names no other line";
            return LostTrainerLines[0];
        }

        /// <summary>
        /// Null when this production building may be placed; otherwise the
        /// refusal ("production: extra held — ..."). <paramref name="extra"/>
        /// says the placement, if it lands, is a saturation extra (logged and
        /// the saturation window restarted by NoteExtraProduction).
        /// </summary>
        private string ProductionGate(EntityManager em, Faction faction, string buildingId, float now,
            out bool extra, out string detail)
        {
            extra = false;
            detail = null;
            if (!BuildSiteSnapshot.IsProductionId(buildingId)) return null;
            if (_provinceProductionStep || _homeProductionFloor || _rebuildingLostTrainer) return null;

            int total = 0;
            for (int l = 0; l < LostTrainerLines.Length; l++) total += CountLine(em, faction, l);
            // The opening: a faction with no production at all gets its Barracks.
            if (total == 0 && buildingId == LostTrainerLines[0]) return null;

            int key = (int)faction;
            _prodBusyFrac.TryGetValue(key, out float busyFrac);
            float satFor = _prodSatSince.TryGetValue(key, out float since) ? now - since : -1f;

            string held = null;
            int alive = 0, desired = 0;
            bool armyKnown = false;
            var cap = ProfileOf(faction);
            if (satFor < cap.ProductionSaturationSeconds)
                held = AILogger.Enabled
                    ? $"not saturated ({busyFrac * 100f:F0}% of trainers busy" +
                      (satFor >= 0f ? $" for {satFor:F0}s" : "") +
                      $"; needs {cap.ProductionSaturationThreshold * 100f:F0}% for {cap.ProductionSaturationSeconds:F0}s)"
                    : "not saturated";
            else
            {
                alive = CountAliveMilitary(em, faction);
                armyKnown = AIBudget.TryGetArmyDesired(faction, out desired);
                if (!armyKnown || alive >= desired)
                    held = AILogger.Enabled
                        ? $"army at target ({alive}/{(armyKnown ? desired.ToString() : "?")})" : "army at target";
            }
            if (held == null)
            {
                int rising = CountFactionBuildingsUnderConstruction<BarracksTag>(em, faction)
                           + CountFactionBuildingsUnderConstruction<ArcheryRangeTag>(em, faction)
                           + CountFactionBuildingsUnderConstruction<RoyalStableTag>(em, faction)
                           + CountFactionBuildingsUnderConstruction<SiegeYardTag>(em, faction);
                if (rising > 0)
                    held = AILogger.Enabled ? $"saturated, but {rising} production building(s) still rising"
                                            : "one rising";
            }
            if (held == null)
            {
                string line = ChooseNeededLine(em, faction, ExtraBlockedMask(faction, now), out _);
                if (line != null && line != buildingId)
                    held = AILogger.Enabled ? $"saturated, but the plan needs {line} more than {buildingId}"
                                            : "other line";
            }

            if (held != null)
            {
                if (AILogger.Enabled
                    && (!_prodHeldLogAt.TryGetValue(key, out float at) || now >= at))
                {
                    _prodHeldLogAt[key] = now + math.max(5f, Cfg.productionLogInterval);
                    AILogger.Log(faction, "PRODUCTION", $"extra held — {held} [{buildingId} asked]");
                }
                return "production: extra held — " + held;
            }
            extra = true;
            if (AILogger.Enabled)
                detail = $"{busyFrac * 100f:F0}% busy over {satFor:F0}s, army {alive}/{desired}";
            return null;
        }

        /// <summary>Lines (LostTrainerLines bits) whose last saturation-extra
        /// placement found no legal spot, still inside the retry window.</summary>
        private int ExtraBlockedMask(Faction faction, float now)
        {
            int mask = 0;
            for (int l = 0; l < LostTrainerLines.Length; l++)
                if (_extraLineBlocked.TryGetValue(((int)faction, l), out float until) && now < until)
                    mask |= 1 << l;
            return mask;
        }

        /// <summary>A saturation extra of <paramref name="buildingId"/> was
        /// refused for want of a spot: skip that line for
        /// territoryBlockedStepSeconds so the next-needed line is asked.</summary>
        private void NoteExtraLinePlacementFailed(Faction faction, string buildingId, string why, float now)
        {
            for (int l = 0; l < LostTrainerLines.Length; l++)
            {
                if (LostTrainerLines[l] != buildingId) continue;
                _extraLineBlocked[((int)faction, l)] = now + math.max(1f, Cfg.territoryBlockedStepSeconds);
                AILogger.Log(faction, "PRODUCTION",
                    $"extra {buildingId} has no legal spot ({why}) — next-needed line asked for {Cfg.territoryBlockedStepSeconds:F0}s");
                return;
            }
        }

        /// <summary>A placed saturation extra: log it and restart the window,
        /// so the next extra needs the larger production saturated afresh.</summary>
        private void NoteExtraProduction(Faction faction, string buildingId, string detail)
        {
            _prodSatSince.Remove((int)faction);
            AILogger.Log(faction, "PRODUCTION", $"extra {buildingId} — saturated ({detail})");
        }

        /// <summary>Was a Fortress site in <paramref name="r"/> refused
        /// recently (EnsureFortressExpansion's fortressSiteRetrySeconds)?</summary>
        private bool FortressSiteBlocked(Faction faction, int r, float now)
            => _fortressSiteBlocked.TryGetValue(((int)faction, r), out float until) && now < until;

        /// <summary>This faction's Fortresses, sites and plans included — the
        /// count EnsureFortressExpansion holds against fortressMaxPerFaction.</summary>
        private static int CountOwnFortresses(EntityManager em, Faction faction)
        {
            int n = Plans.CountOf(em, faction, "Fortress");
            var q = QC_FortressFactionXf.Get(em, QT_FortressFactionXf);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // THE RESERVED FORTRESS SPOT
        // ─────────────────────────────────────────────────────────────────

        private void EnsureFortressSpot(EntityManager em, Faction faction, int r, float now, ref bool searched)
        {
            int2 size = BuildingSizeConfig.GetSize("Fortress");
            if (AIBaseLayout.TryGetFortressSpot(faction, r, out float3 spot))
            {
                // Still standing room? (Hard rules only: nothing of ours can
                // have been put on it, but a rival, a node or the curse can.)
                if (FortressSpotGood(em, faction, r, spot, size, nodeClear: false, sealCheck: false, now))
                    return;
                AIBaseLayout.ClearFortressSpot(faction, r);
                AILogger.Log(faction, "TERRITORY",
                    $"FORTRESS SPOT in {RegionMap.NameOf(r)} at ({spot.x:F0},{spot.z:F0}) lost — re-reserving");
            }
            if (searched) return;   // one search per walk
            int lk = (int)faction * 4096 + r;
            if (_spotRetryAt.TryGetValue(lk, out float retry) && now < retry) return;
            searched = true;
            if (FindFortressSpot(em, faction, r, size, now, out float3 found))
            {
                AIBaseLayout.SetFortressSpot(faction, r, found, size);
                AILogger.Log(faction, "TERRITORY",
                    $"FORTRESS SPOT reserved in {RegionMap.NameOf(r)} at ({found.x:F0},{found.z:F0})");
                return;
            }
            _spotRetryAt[lk] = now + math.max(1f, Cfg.territoryBlockedStepSeconds);
            if (!_nextSpotFailLog.TryGetValue(lk, out float next) || now >= next)
            {
                _nextSpotFailLog[lk] = now + 60f;
                AILogger.Log(faction, "TERRITORY",
                    $"FORTRESS SPOT: no legal {size.x}x{size.y} spot in {RegionMap.NameOf(r)} " +
                    $"within {Cfg.fortressSpotSearchRadius:F0} m of its seed");
            }
        }

        /// <summary>Deterministic ring search from the territory's seed for a
        /// Fortress footprint wholly inside it: first keeping the resource-node
        /// clearance every base building keeps, then without it.</summary>
        private bool FindFortressSpot(EntityManager em, Faction faction, int r, int2 size, float now,
            out float3 spot)
        {
            var seed = RegionMap.SeedOf(r);
            for (int pass = 0; pass < 2; pass++)
                for (float rad = 0f; rad <= Cfg.fortressSpotSearchRadius; rad += 4f)
                {
                    int samples = rad <= 0f ? 1 : BuildAngleSamples;
                    for (int i = 0; i < samples; i++)
                    {
                        float ang = i / (float)BuildAngleSamples * math.PI * 2f;
                        var c = BuildGrid.Snap(new float3(seed.x + math.cos(ang) * rad, 0f,
                                                          seed.y + math.sin(ang) * rad), size);
                        c.y = TerrainUtility.GetHeight(c.x, c.z);
                        if (FortressSpotGood(em, faction, r, c, size, nodeClear: pass == 0, sealCheck: true, now))
                        {
                            spot = c;
                            return true;
                        }
                    }
                }
            spot = default;
            return false;
        }

        private bool FortressSpotGood(EntityManager em, Faction faction, int r, float3 c, int2 size,
            bool nodeClear, bool sealCheck, float now)
        {
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            if (RegionMap.RegionAt(c.x - hx, c.z - hz) != r || RegionMap.RegionAt(c.x + hx, c.z - hz) != r
                || RegionMap.RegionAt(c.x - hx, c.z + hz) != r || RegionMap.RegionAt(c.x + hx, c.z + hz) != r)
                return false;
            if (!TerritoryOwnership.CanBuildAt(em, faction, "Fortress", c.x, c.z)) return false;
            if (IsCursedGround(em, c)) return false;
            var snap = BuildSiteSnapshot.Current(em);
            if (snap.Overlaps(c, size, 0f, ignoreWalls: false)) return false;
            if (snap.OverlapsOwnPlan(faction, c, size)) return false;
            if (!AIWallCorridor.FootprintClear(em, faction, c, size)) return false;
            if (nodeClear && FortressNearNode(em, c)) return false;
            if (!snap.IsValidBuildPosition(em, c, size, "Fortress")) return false;
            return !sealCheck || AIBaseLayout.WouldSeal(em, faction, c, size, "Fortress", now) == null;
        }

        private static bool FortressNearNode(EntityManager em, float3 c)
        {
            float d2 = Cfg.minResourceNodeClearance * Cfg.minResourceNodeClearance;
            var vq = QC_VeilstoneOutcroppingTagLocalTransform.Get(em, QT_VeilstoneOutcroppingTagLocalTransform);
            var iq = QC_IronMineTagLocalTransform.Get(em, QT_IronMineTagLocalTransform);
            using var v = vq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ir = iq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            return TooCloseToAny(c, v, d2) || TooCloseToAny(c, ir, d2);
        }

        static readonly ComponentType[] QT_AnyFortressXf =
        {
            ComponentType.ReadOnly<FortressTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_AnyFortressXf;

        /// <summary>Does anyone's Fortress (site or plan included) stand in
        /// <paramref name="r"/>? NearestRegion, as FortressCapReached reads it.</summary>
        private static bool FortressStandsIn(EntityManager em, int r)
        {
            var q = QC_AnyFortressXf.Get(em, QT_AnyFortressXf);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
                if (RegionMap.NearestRegion(xfs[i].Position.x, xfs[i].Position.z) == r) return true;
            var pq = QC_PlansXf.Get(em, QT_PlansXf);
            using var plans = pq.ToComponentDataArray<PlannedBuilding>(Allocator.Temp);
            using var pxf = pq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < plans.Length; i++)
                if (plans[i].BuildingId == FortressIdFs
                    && RegionMap.NearestRegion(pxf[i].Position.x, pxf[i].Position.z) == r) return true;
            return false;
        }

        private static readonly FixedString64Bytes FortressIdFs = new FixedString64Bytes("Fortress");

        // ─────────────────────────────────────────────────────────────────
        // COUNTS PER TERRITORY
        // ─────────────────────────────────────────────────────────────────

        static readonly ComponentType[] QT_PlansXf =
        {
            ComponentType.ReadOnly<PlannedBuilding>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_PlansXf;

        private static int CountInRegion<T>(EntityManager em, Faction faction, int r)
            where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagFactionXf<T>(em);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction && RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z) == r) n++;
            return n;
        }

        private static int CountPlansIn(EntityManager em, Faction faction, int r, string buildingId)
        {
            var q = QC_PlansXf.Get(em, QT_PlansXf);
            if (q.IsEmptyIgnoreFilter) return 0;
            var id = new FixedString64Bytes(buildingId);
            using var plans = q.ToComponentDataArray<PlannedBuilding>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < plans.Length; i++)
                if (facs[i].Value == faction && plans[i].BuildingId == id
                    && RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z) == r) n++;
            return n;
        }

        /// <summary>Production buildings of each line in a territory — sites
        /// and plans included, so a building on its way up is not re-ordered.</summary>
        private static void CountProductionIn(EntityManager em, Faction faction, int r,
            out int barracks, out int range, out int stable, out int siege)
        {
            barracks = CountInRegion<BarracksTag>(em, faction, r) + CountPlansIn(em, faction, r, "Barracks");
            range = CountInRegion<ArcheryRangeTag>(em, faction, r) + CountPlansIn(em, faction, r, "ArcheryRange");
            stable = CountInRegion<RoyalStableTag>(em, faction, r)
                   + CountPlansIn(em, faction, r, "Alanthor_RoyalStable");
            siege = CountInRegion<SiegeYardTag>(em, faction, r)
                  + CountPlansIn(em, faction, r, "Alanthor_SiegeYard");
        }

        /// <summary>Neighbours of <paramref name="r"/> a tower should face:
        /// held by no one, by the curse, or by a hostile faction (index order).</summary>
        private void CollectFrontier(Faction faction, int r)
        {
            _devFrontier.Clear();
            for (int o = 0; o < RegionMap.Count; o++)
            {
                if (o == r || !RegionMap.AreAdjacent(r, o)) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(o))) continue;
                int owner = TerritoryOwnership.OwnerOf(o);
                if (owner == (int)faction) continue;
                if (owner >= 0 && !Alliances.AreHostile(faction, (Faction)owner)) continue;
                _devFrontier.Add(o);
            }
        }

        /// <summary>"TERRITORY production: Name B1 R1 S0 Y0 T2 [step] | ..."</summary>
        private void ReportTerritoryProduction(EntityManager em, Faction faction, List<int> mine, int home,
            int[] steps)
        {
            if (!AILogger.Enabled || mine.Count == 0) return;
            _devReport.Clear();
            int tb = 0, tr = 0, ts = 0, ty = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                int r = mine[i];
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                CountProductionIn(em, faction, r, out int b, out int a, out int s, out int y);
                int t = CountInRegion<WatchTowerTag>(em, faction, r);
                tb += b; tr += a; ts += s; ty += y;
                if (_devReport.Length > 0) _devReport.Append(" | ");
                _devReport.Append(RegionMap.NameOf(r)).Append(r == home ? " (home)" : "")
                    .Append(" B").Append(b).Append(" R").Append(a).Append(" S").Append(s).Append(" Y").Append(y)
                    .Append(" T").Append(t);
                int st = r < steps.Length ? steps[r] : 0;
                if (st > 0 && st < StepNames.Length) _devReport.Append(" [").Append(StepNames[st]).Append(']');
                if (r != home && AIBaseLayout.TryGetFortressSpot(faction, r, out float3 sp))
                    _devReport.Append(" spot (").Append((int)sp.x).Append(',').Append((int)sp.z).Append(')');
            }
            AILogger.Log(faction, "TERRITORY",
                $"production: {tb} Barracks, {tr} Ranges, {ts} Stables, {ty} Siege Yards across " +
                $"{mine.Count} territories — {_devReport}");
        }
    }
}
