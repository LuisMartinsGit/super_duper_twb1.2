// SimpleAISystem.Extractors.cs
// Building the extraction buildings on the resource nodes the faction holds.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY
//
// docs/Design/Regions.md §4 makes territory the economy and the extraction
// building the investment: a node trickles on its own, and the building
// standing on it adds its level to the yield. Every resource has one —
// Gatherer's Hut on a supply site, Mine on iron, Veilstone Mine on a veilstone
// outcropping.
//
// The AI only ever built the hut. It had no reason to raise the others,
// because the generic Mine was worth building anywhere and nothing told it
// where the nodes were — so a logged 30-minute match ended with four AIs
// holding thousands of unspent veilstone and no building anywhere converting
// ground into income. With nodes now DEPLETING, ignoring them is worse than
// leaving money on the table: yield falls whether or not anyone is extracting,
// so the faction that does not invest simply gets poorer.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using OutpostSites = TheWaningBorder.Entities.TradingOutpost;   // avoids the DC0062 Entities.ForEach misread

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {

        private readonly Dictionary<int, float> _nextExtractorTime = new Dictionary<int, float>();

        /// <summary>The extractor for each node kind, scarcest first. Veilstone
        /// leads: it is never banked for free any more
        /// (docs/Design/Veilstone_Economy.md), so Alanthor's Trading Outpost
        /// and everyone else's Veilstone Mine come before iron and supplies.
        /// The two are culture-exclusive: each faction skips the other's.
        /// </summary>
        private static readonly (string Building, ComponentType Node)[] ExtractorPlan =
        {
            (OutpostSites.BuildingId, default),
            ("VeilstoneMine",    default),
            ("Mine",             default),
            ("GatherersHut",     default),
        };

        /// <summary>
        /// Raise ONE extractor on a free node inside our own territory.
        /// One per attempt: each is a real purchase, and building four at once
        /// is how the economy wallet empties and unit production stops.
        /// </summary>
        private void EnsureExtractors(EntityManager em, Faction faction, float now)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;

            int key = (int)faction;
            // First attempt offset by faction (2026-09-25), so the eight
            // brains' 15 s extractor walks never line up in one second.
            if (!_nextExtractorTime.ContainsKey(key))
            {
                _nextExtractorTime[key] = now + Cfg.extractorAttemptInterval * ((key & 7) / 8f);
                return;
            }
            if (_nextExtractorTime.TryGetValue(key, out float next) && now < next) return;
            _nextExtractorTime[key] = now + Cfg.extractorAttemptInterval;

            if (!TechCatalog.IsReady) return;
            // The Outpost savings goal lasts only while the army is short of
            // veilstone; it is re-armed below while it still is.
            if (!AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone))
                AIPivotalReserve.Clear(faction, OutpostReserveKey);
            if (AICommon.CountIdleWorkers(em, faction) == 0)
            {
                LogExtractBlocked(faction, now, "no idle worker");
                return;
            }

            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return;
            var owned = _ownedTerritories;   // pooled
            owned.Clear();
            owned.UnionWith(mine);

            // Diagnostic trail: six 30-minute batch matches produced 80 huts
            // and not one ore extractor, and this walk failed SILENTLY at
            // every gate — the exact diagnostic hole LogClaimBlocked exists
            // to close for claims. Reasons collect per plan entry and log
            // throttled when the whole walk buys nothing.
            string blocked = null;
            _nodesOffTerritory = 0;
            _nodesUnreachable = 0;

            for (int i = 0; i < ExtractorPlan.Length; i++)
            {
                string buildingId = ExtractorPlan[i].Building;

                // Affordable and legal for this culture/era? TryBuildBuilding
                // re-checks, but asking first avoids scanning nodes for a
                // building we could not raise anyway.
                if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) continue;

                // Culture: Alanthor trade beside veilstone, everyone else
                // mines it (Veilstone_Economy.md §3).
                bool alanthor = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor;
                bool isOutpost = buildingId == OutpostSites.BuildingId;
                if (isOutpost && !alanthor) continue;
                if (buildingId == "VeilstoneMine" && alanthor) continue;

                // THE OPENING HUTS COME FIRST (2026-10-03): while the hut
                // pipeline is still raising its bootstrap huts, no ore
                // extractor may spend the supplies they are waiting on.
                if (buildingId != "GatherersHut"
                    && AIPivotalReserve.Has(faction, HutBootstrapReserveKey))
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: opening huts first";
                    continue;
                }
                // A Trading Outpost is priced by WHERE it stands: the per-outcrop
                // ramp (Veilstone_Economy.md §3.1). Its free slots come back
                // cheapest first, so the first one is the price to beat.
                var planCost = AICommon.ToCost(def.cost);
                if (isOutpost)
                {
                    _freeNodes.Clear();
                    CollectFreeNodes(em, faction, buildingId, owned, _freeNodes);
                    if (_freeNodes.Count > 0)
                        planCost = BuildCosts.For(em, faction, buildingId, _freeNodes[0]);
                }
                if (!FactionEconomy.CanAfford(em, faction, planCost))
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: bank short";
                    // VEILSTONE IS THE ARMY'S WALL (2026-10-03): an Alanthor
                    // army short of veilstone can only be fed by Outposts,
                    // so a free outcrop the bank cannot yet pay for becomes
                    // a savings goal — walls, houses and army growth hold
                    // until it is raised (AIPivotalReserve).
                    if (isOutpost && AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone))
                    {
                        _freeNodes.Clear();
                        CollectFreeNodes(em, faction, buildingId, owned, _freeNodes);
                        if (_freeNodes.Count > 0)
                        {
                            if (!AIPivotalReserve.Has(faction, OutpostReserveKey))
                                AILogger.Log(faction, "ECONOMY",
                                    $"veilstone is the army's bottleneck — saving for a Trading Outpost " +
                                    $"({_freeNodes.Count} free outcrop(s) in held ground)");
                            AIPivotalReserve.Set(faction, OutpostReserveKey, planCost);
                        }
                        else AIPivotalReserve.Clear(faction, OutpostReserveKey);
                    }
                    continue;
                }

                // EVERY free node is a candidate, not just the first found.
                // One node can be legitimately unplaceable (a foundation
                // already over it, cursed ground, a terrain lip) — anchoring
                // on it alone made the 15 s retry pick the same dead node
                // forever while free ones sat a territory over.
                _freeNodes.Clear();
                CollectFreeNodes(em, faction, buildingId, owned, _freeNodes);
                if (_freeNodes.Count == 0)
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: no free owned node";
                    if (isOutpost) AIPivotalReserve.Clear(faction, OutpostReserveKey);
                    continue;
                }
                string reason = null;

                // VEILSTONE SURPLUS (2026-10-04, Game_AI.md 5e): the army is
                // waiting on veilstone and supplies and iron are piling up —
                // an Outpost beside EVERY outcrop with a free side this pass
                // (one per outcrop, cheapest ramp first), not one, for as
                // long as a worker is free to take the site (the open-site
                // cap and the bank are re-checked per placement).
                if (isOutpost && IsVeilstoneSurplus(em, faction))
                {
                    int placed = 0;
                    for (int n = 0; n < _freeNodes.Count; n++)
                    {
                        if (AICommon.CountIdleWorkers(em, faction) == 0) break;
                        // Ramp-priced per slot; the list runs cheapest first,
                        // so new outcrops are taken before a 3rd or 4th post.
                        if (!FactionEconomy.CanAfford(em, faction,
                                BuildCosts.For(em, faction, buildingId, _freeNodes[n]))) continue;
                        if (!TryBuildBuildingWithReason(em, faction, buildingId,
                                out reason, _freeNodes[n])) continue;
                        placed++;
                        LogSurplus(faction,
                            $"outpost placed at ({_freeNodes[n].x:F0},{_freeNodes[n].z:F0}) " +
                            $"({_freeNodes.Count} free outcrop(s))");
                    }
                    if (placed > 0)
                    {
                        AIPivotalReserve.Clear(faction, OutpostReserveKey);
                        return;
                    }
                    if (AILogger.Enabled)
                        blocked += $" | {buildingId}: {_freeNodes.Count} node(s), last refusal: {reason}";
                    continue;
                }

                for (int n = 0; n < _freeNodes.Count; n++)
                {
                    if (isOutpost && !FactionEconomy.CanAfford(em, faction,
                            BuildCosts.For(em, faction, buildingId, _freeNodes[n]))) continue;
                    if (!TryBuildBuildingWithReason(em, faction, buildingId,
                            out reason, _freeNodes[n])) continue;
                    AILogger.Log(faction, "EXTRACT",
                        $"{buildingId} on a free node at " +
                        $"({_freeNodes[n].x:F0},{_freeNodes[n].z:F0})");
                    if (isOutpost) AIPivotalReserve.Clear(faction, OutpostReserveKey);
                    return;   // one per attempt
                }
                if (AILogger.Enabled)
                    blocked += $" | {buildingId}: {_freeNodes.Count} node(s), last refusal: {reason}";
            }

            if (blocked != null)
            {
                // Bad map data reads as an AI that will not build, so name it.
                if (_nodesUnreachable > 0)
                    blocked += $" | {_nodesUnreachable} node(s) skipped: nothing can reach them";
                LogExtractBlocked(faction, now, blocked.Substring(3));
            }
        }

        /// <summary>AIPivotalReserve key for a Trading Outpost the army's
        /// veilstone shortage is waiting on.</summary>
        private const string OutpostReserveKey = "TradingOutpost";

        private readonly Dictionary<int, float> _nextOutpostModeTime = new Dictionary<int, float>();

        /// <summary>
        /// Choose each of this faction's Trading Outposts' trade
        /// (Veilstone_Economy.md §3.1), only among the trades it has
        /// researched:
        ///   * SELL VEILSTEEL on one Outpost while veilsteel is piling up past
        ///     outpostSellAboveVeilsteel — it is the richest supply/iron line;
        ///   * FORGE on half of them (rounded up) while veilstone is banked and
        ///     veilsteel short — outpostForgeAboveVeilstone / outpostVeilsteelTarget,
        ///     with outpostTradeBelowVeilstone as the hysteresis floor;
        ///   * BUY VEILSTONE on the rest.
        /// Orders go through CommandRouter like a player's click.
        /// </summary>
        private void ManageTradingOutposts(EntityManager em, Faction faction, float now)
        {
            int key = (int)faction;
            if (_nextOutpostModeTime.TryGetValue(key, out float next) && now < next) return;
            _nextOutpostModeTime[key] = now + Cfg.extractorAttemptInterval;
            if (!FactionEconomy.TryGetResources(em, faction, out var bank)) return;

            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var modes = q.ToComponentDataArray<TradingOutpostMode>(Allocator.Temp);
            int mine = 0, forging = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                mine++;
                if (modes[i].Recipe == TradeRecipe.ForgeVeilsteel) forging++;
            }
            if (mine == 0) return;

            bool canForge = TheWaningBorder.Systems.Economy.TradingOutpostSystem.IsUnlocked(
                faction, TradeRecipe.ForgeVeilsteel);
            bool canSell = TheWaningBorder.Systems.Economy.TradingOutpostSystem.IsUnlocked(
                faction, TradeRecipe.SellVeilsteel);

            int sellWant = canSell && bank.Veilsteel >= Cfg.outpostSellAboveVeilsteel ? 1 : 0;
            int forgeWant;
            if (!canForge || bank.Veilstone < Cfg.outpostTradeBelowVeilstone
                || bank.Veilsteel >= Cfg.outpostVeilsteelTarget)
                forgeWant = 0;
            else if (bank.Veilstone >= Cfg.outpostForgeAboveVeilstone)
                forgeWant = (mine + 1) / 2;
            else
                forgeWant = forging;   // inside the band: keep what is forging
            forgeWant = math.min(forgeWant, mine - sellWant);

            // VEILSTONE IS THE ARMY'S BOTTLENECK (2026-10-03): while military
            // purchases are being refused for want of veilstone, every Outpost
            // BUYS it — forging turns the scarce veilstone into veilsteel, and
            // an Outpost selling veilsteel is one not buying. Selling stays
            // only while the bank is too poor in supplies or iron to run a
            // Buy cycle, which the sale itself pays for.
            bool veilstoneStarved = AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone);
            if (veilstoneStarved)
            {
                forgeWant = 0;
                TheWaningBorder.Systems.Economy.TradingOutpostSystem.PerMinute(
                    faction, TradeRecipe.BuyVeilstone, out var buyIn, out _);
                if (bank.Supplies >= buyIn.Supplies && bank.Iron >= buyIn.Iron) sellWant = 0;
            }

            int changed = 0, sells = 0, forges = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                TradeRecipe want;
                if (sells < sellWant) { want = TradeRecipe.SellVeilsteel; sells++; }
                else if (forges < forgeWant) { want = TradeRecipe.ForgeVeilsteel; forges++; }
                else want = TradeRecipe.BuyVeilstone;
                if (modes[i].Recipe == want) continue;
                TheWaningBorder.Core.Commands.CommandRouter.IssueSetOutpostMode(
                    em, ents[i], want, TheWaningBorder.Core.Commands.CommandSource.AI);
                changed++;
            }
            if (changed > 0)
                AILogger.Log(faction, "ECONOMY",
                    $"Trading Outposts: {sells} sell / {forges} forge / {mine - sells - forges} buy " +
                    $"(veilstone {bank.Veilstone}, veilsteel {bank.Veilsteel}" +
                    (veilstoneStarved ? ", army short of veilstone)" : ")"));
            else if (veilstoneStarved && AILogger.Enabled)
                LogVeilstoneBottleneck(faction, now,
                    $"{mine} Trading Outpost(s) all buying, veilstone {bank.Veilstone}, " +
                    $"supplies {bank.Supplies}, iron {bank.Iron}");
        }

        private readonly Dictionary<int, float> _nextBottleneckLog = new Dictionary<int, float>();

        /// <summary>Throttled (extractLogInterval) note that the army is held
        /// back by veilstone — the reason a rich Alanthor bank is not an army.</summary>
        private void LogVeilstoneBottleneck(Faction faction, float now, string state)
        {
            int key = (int)faction;
            if (_nextBottleneckLog.TryGetValue(key, out float next) && now < next) return;
            _nextBottleneckLog[key] = now + Cfg.extractLogInterval;
            AILogger.Log(faction, "ECONOMY", $"veilstone is the army's bottleneck: {state}");
        }

        static readonly ComponentType[] QT_Outposts =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<TradingOutpostMode>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_Outposts;

        private readonly Dictionary<int, float> _nextExtractLog = new Dictionary<int, float>();

        private void LogExtractBlocked(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextExtractLog.TryGetValue(key, out float next) && now < next) return;
            _nextExtractLog[key] = now + Cfg.extractLogInterval;
            AILogger.Log(faction, "EXTRACT", $"blocked: {why}");
        }

        // Host-only managed scratch, cleared per use.
        private readonly List<float3> _freeNodes = new List<float3>();
        private readonly HashSet<int> _ownedTerritories = new HashSet<int>();

        /// <summary>
        /// How far out to look for ground a worker could stand on. Past the
        /// node's own impassable cells and the extractor's footprint.
        /// </summary>
        private const float NodeApproachRange = 6f;

        /// <summary>
        /// Can anything actually GET to this site? A node sealed inside a
        /// cliff pocket or off the walkable island is map data the AI cannot
        /// fix, and proposing it costs a real worker a real timeout — so it
        /// is skipped rather than retried every 15 s forever.
        ///
        /// The node's own cell is impassable BY DESIGN (docs/Design/Build_Grid.md
        /// — resource nodes stamp their cell), so the test is on the APPROACH:
        /// at least one cardinal sample outside the node must be in the region
        /// every player can reach. Reachability is baked once by
        /// PassabilityGrid; before it is ready the check passes, so bootstrap
        /// order never turns into a silent no-build.
        /// </summary>
        private static bool HasReachableApproach(float3 site)
        {
            var grid = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (grid == null || !grid.IsReachabilityReady) return true;

            const float r = NodeApproachRange;
            return grid.IsReachableByAllPlayers(site + new float3(r, 0f, 0f))
                || grid.IsReachableByAllPlayers(site + new float3(-r, 0f, 0f))
                || grid.IsReachableByAllPlayers(site + new float3(0f, 0f, r))
                || grid.IsReachableByAllPlayers(site + new float3(0f, 0f, -r));
        }

        /// <summary>AIPivotalReserve key the hut pipeline arms while its
        /// opening huts are still unbuilt (SimpleAISystem.Economy.cs).</summary>
        private const string HutBootstrapReserveKey = "OpeningHuts";

        /// <summary>True while the hut pipeline is saving for its opening
        /// huts (the OpeningHuts reserve is armed).</summary>
        private static bool OpeningHutsPending(Faction faction)
            => AIPivotalReserve.Has(faction, HutBootstrapReserveKey);

        /// <summary>Population headroom at which housing counts as blocking.
        /// While the opening huts are pending it is nearly-full only: the
        /// normal floor (8) fires at the starting 9/16, and every house
        /// worker bought that house ahead of the huts (batch 2026-10-03).</summary>
        private int HousingHeadroomFloor(Faction faction)
            => OpeningHutsPending(faction) ? Cfg.openingHutHousingHeadroom : Cfg.populationHeadroomFloor;

        /// <summary>Is there a free supply node in our territory a Gatherer's
        /// Hut could stand on (not already under one of our own plans)?</summary>
        private bool HasFreeHutSite(EntityManager em, Faction faction)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return false;
            _nodeSiteOwned.Clear();
            _nodeSiteOwned.UnionWith(TerritoryOwnership.TerritoriesOf(faction));
            _nodeSites.Clear();
            CollectFreeNodes(em, faction, "GatherersHut", _nodeSiteOwned, _nodeSites);
            int2 size = BuildingSizeConfig.GetSize("GatherersHut");
            for (int i = 0; i < _nodeSites.Count; i++)
                if (!TheWaningBorder.Entities.PlannedBuildings.OverlapsOwnPlan(em, faction, _nodeSites[i], size))
                    return true;
            return false;
        }

        // Scratch for TryBuildOnFreeNode — separate from the walk's, because
        // EnsureExtractors calls into the build path while iterating its own.
        private readonly List<float3> _nodeSites = new List<float3>();
        private readonly HashSet<int> _nodeSiteOwned = new HashSet<int>();

        /// <summary>
        /// Raise <paramref name="buildingId"/> (an extractor) on the free node
        /// of its kind nearest <paramref name="hallPos"/>, inside our own
        /// territory. What an un-anchored extractor request means: the node
        /// IS the site, so the search starts there rather than at the Hall.
        /// </summary>
        private bool TryBuildOnFreeNode(EntityManager em, Faction faction, string buildingId,
            int2 size, float3 hallPos, out string reason)
        {
            reason = null;
            if (!RegionMap.Ready || !TerritoryOwnership.Ready)
            { reason = "regions not ready"; return false; }

            _nodeSiteOwned.Clear();
            _nodeSiteOwned.UnionWith(TerritoryOwnership.TerritoriesOf(faction));
            _nodeSites.Clear();
            CollectFreeNodes(em, faction, buildingId, _nodeSiteOwned, _nodeSites);
            if (_nodeSites.Count == 0) { reason = "no free owned node"; return false; }

            // Home first: the nearest node is the safest and the quickest walk.
            _nodeSites.Sort((a, b) => math.distancesq(a, hallPos).CompareTo(math.distancesq(b, hallPos)));

            for (int i = 0; i < _nodeSites.Count; i++)
            {
                // A plan carries no extractor tag, so its node still reads as
                // free until a worker breaks ground — skip it here instead of
                // letting the router refuse it every think.
                if (TheWaningBorder.Entities.PlannedBuildings.OverlapsOwnPlan(em, faction, _nodeSites[i], size))
                    continue;
                if (TryBuildBuildingWithReason(em, faction, buildingId, out reason, _nodeSites[i]))
                    return true;
            }
            reason ??= "every free node already planned";
            return false;
        }

        /// <summary>Nodes skipped by the last walk because the map put them
        /// somewhere unusable — surfaced in the EXTRACT log so bad map data is
        /// visible instead of silent.</summary>
        private int _nodesOffTerritory, _nodesUnreachable;

        /// <summary>
        /// Every node of the kind <paramref name="buildingId"/> needs, inside
        /// our own territory, with no extractor of that kind already on it —
        /// returned as the SITE the extractor would occupy, not as the node
        /// centre.
        ///
        /// THE SITE IS THE QUESTION, NOT THE NODE (2026-09-08). Ownership used
        /// to be read at the node's centre while the build gate read it at the
        /// snapped building position, and those are different points: an
        /// extractor's footprint is grid-snapped onto the node, so a node lying
        /// on a Voronoi border resolves to one region as a point and to the
        /// NEIGHBOUR as a building. The walk then offered a node it owned, the
        /// router refused a site it did not, and the pair repeated every 15 s
        /// for the whole match. Snapping first makes both sides ask about the
        /// same square metre — which is also why no map needs re-baking to fix
        /// a border node: ownership is derived from where the building lands.
        ///
        /// Territory-gated on purpose: a node on somebody else's ground pays
        /// THEM, and the build gate would refuse the site anyway.
        /// </summary>
        private void CollectFreeNodes(EntityManager em, Faction faction, string buildingId,
            HashSet<int> owned, List<float3> into)
        {
            var required = TerritoryOwnership.RequiredNodeFor(buildingId);
            if (required == null) return;

            // The Trading Outpost stands on a SIDE of its outcrop, not on it.
            if (buildingId == OutpostSites.BuildingId)
            {
                CollectFreeOutpostSlots(em, faction, owned, into);
                return;
            }

            var q = AIQueryCache.NodeAt(em, required.Value);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            // One node / extractor read per tick, not two queries per node.
            var snap = TheWaningBorder.Core.Commands.Types.BuildSiteSnapshot.Current(em);

            for (int i = 0; i < xfs.Length; i++)
            {
                // Only a node it has SEEN — owning a territory does not reveal
                // every corner of it (AICommon.IsKnownGround).
                if (!AICommon.IsKnownGround(faction, xfs[i].Position)) continue;

                // Where the building would stand, and — the same call — whether
                // this node is free at all.
                if (!snap.TrySnapToNode(em, buildingId, xfs[i].Position,
                        out float3 site)) continue;

                int region = RegionMap.RegionAt(site.x, site.z);
                if (region == RegionMap.None || !owned.Contains(region))
                { _nodesOffTerritory++; continue; }

                if (!HasReachableApproach(site)) { _nodesUnreachable++; continue; }

                into.Add(site);
            }
        }

        // Scratch for CollectFreeOutpostSlots: (site, ramp index, distance² to the Hall).
        private readonly List<(float3 Site, int Ramp, float D2)> _outpostSlots =
            new List<(float3, int, float)>();

        /// <summary>
        /// THE TRADING OUTPOST'S SITES (2026-10-04, Veilstone_Economy.md §3.1):
        /// up to four posts stand around one uncursed outcrop, one per side,
        /// and every further post beside the same outcrop costs more. One
        /// candidate per outcrop — its free, buildable side nearest the Hall —
        /// returned CHEAPEST RAMP FIRST (then nearest the Hall), so the AI
        /// spreads to fresh outcrops at base price before it pays for a third
        /// or fourth post on one it already trades at.
        ///
        /// Same gates as every extractor site: seen ground, held territory
        /// at the SITE, a reachable approach, no own plan over it, and the
        /// snapshot's placement test (terrain, other nodes, buildings).
        /// </summary>
        private void CollectFreeOutpostSlots(EntityManager em, Faction faction,
            HashSet<int> owned, List<float3> into)
        {
            const string id = OutpostSites.BuildingId;
            _outpostSlots.Clear();
            var q = AIQueryCache.NodeAt(em, ComponentType.ReadOnly<VeilstoneOutcroppingTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var snap = TheWaningBorder.Core.Commands.Types.BuildSiteSnapshot.Current(em);
            int2 size = BuildingSizeConfig.GetSize(id);

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            float3 hallPos = hall != Entity.Null && em.HasComponent<LocalTransform>(hall)
                ? em.GetComponentData<LocalTransform>(hall).Position : float3.zero;

            for (int i = 0; i < ents.Length; i++)
            {
                var np = xfs[i].Position;
                if (!AICommon.IsKnownGround(faction, np)) continue;
                if (TheWaningBorder.Systems.Economy.VeilstoneNodeStateSystem.KindOf(em, ents[i])
                    == VeilstoneNodeKind.Cursed) continue;

                bool found = false;
                float bestD2 = float.MaxValue;
                float3 best = default;
                for (int s = 0; s < OutpostSites.SideCount; s++)
                {
                    var slot = OutpostSites.SideSlot(np, s);
                    if (OutpostSites.SlotTaken(em, slot)) continue;
                    if (TheWaningBorder.Entities.PlannedBuildings.OverlapsOwnPlan(em, faction, slot, size))
                        continue;

                    int region = RegionMap.RegionAt(slot.x, slot.z);
                    if (region == RegionMap.None || !owned.Contains(region))
                    { _nodesOffTerritory++; continue; }
                    if (!HasReachableApproach(slot)) { _nodesUnreachable++; continue; }
                    if (!snap.IsValidBuildPosition(em, slot, size, id)) continue;

                    float d2 = math.distancesq(slot.xz, hallPos.xz);
                    if (!found || d2 < bestD2) { found = true; bestD2 = d2; best = slot; }
                }
                if (!found) continue;

                int ramp = OutpostSites.RampIndexForNewPost(em, faction, best);
                if (ramp >= OutpostSites.SideCount) continue;
                _outpostSlots.Add((best, ramp, bestD2));
            }

            _outpostSlots.Sort((a, b) =>
            {
                int c = a.Ramp.CompareTo(b.Ramp);
                if (c != 0) return c;
                c = a.D2.CompareTo(b.D2);
                if (c != 0) return c;
                c = a.Site.x.CompareTo(b.Site.x);
                return c != 0 ? c : a.Site.z.CompareTo(b.Site.z);
            });
            for (int i = 0; i < _outpostSlots.Count; i++) into.Add(_outpostSlots[i].Site);
        }
    }
}
