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
// ground into income. With nodes DEPLETING (finite again, decision 56),
// ignoring them is worse than leaving money on the table: a node is spent
// whether or not anyone extracts it, and a spent node takes its extractor
// with it — so the AI keeps raising extractors on the fresh nodes it holds.
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

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {

        private readonly Dictionary<int, float> _nextExtractorTime = new Dictionary<int, float>();

        /// <summary>The extractor for each node kind, scarcest first: veilstone,
        /// then iron, then supplies. Every culture but the Runai mines veilstone
        /// (Alanthor too since 2026-11-09, decision 42); the Runai raise a
        /// Sanctuary instead.
        /// </summary>
        private static readonly (string Building, ComponentType Node)[] ExtractorPlan =
        {
            ("Runai_Sanctuary",  default),
            ("VeilstoneMine",    default),
            ("Mine",             default),
            ("GatherersHut",     default),
        };

        // ── THE EXTRACTOR SHARE (2026-11-09, Game_AI.md § 2c) ──────────────
        //
        // The tier's economic handle. Expert puts an extractor on every node it
        // can work, as soon as it can; each tier below works 25 % fewer nodes of
        // each kind than the one above, so its extraction income lands at that
        // share of Expert's. What an extractor PAYS is never cut: the tier simply
        // owns fewer of them. Counted per kind — Gatherer's Huts against supply
        // nodes, Mines against iron, Veilstone Mines against outcrops — over the
        // faction's own territory.

        static readonly ComponentType[] QT_ShareHuts = { ComponentType.ReadOnly<GathererHutTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.Exclude<RaiderCampTag>(), ComponentType.Exclude<DepletedHutTag>() };
        static readonly ComponentType[] QT_ShareMines = { ComponentType.ReadOnly<MineTag>(), ComponentType.ReadOnly<FactionTag>() };
        static readonly ComponentType[] QT_ShareVeilMines = { ComponentType.ReadOnly<VeilstoneMineTag>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_ShareHuts, QC_ShareMines, QC_ShareVeilMines;

        private readonly List<float3> _shareFree = new List<float3>();
        private readonly HashSet<int> _shareOwned = new HashSet<int>();

        /// <summary>The share of the workable nodes this faction's tier puts an
        /// extractor on: 1 for Expert, the profile's share below it, and the
        /// Beginner slot's own share (it plays the Easy profile).</summary>
        private static float ExtractorShareOf(Faction faction, string buildingId)
        {
            float general = ExtractorShareOf(faction);
            // Iron Mines have a share of their own (AIDifficultyProfileSO.mineShare).
            return buildingId == "Mine" ? math.max(general, ProfileOf(faction).MineShare) : general;
        }

        private static float ExtractorShareOf(Faction faction)
        {
            var slots = TheWaningBorder.Core.Config.LobbyConfig.Slots;
            int i = (int)faction;
            if (slots != null && i >= 0 && i < slots.Length && slots[i] != null
                && slots[i].Type == TheWaningBorder.Core.Config.SlotType.AI
                && slots[i].AIDifficulty == TheWaningBorder.Core.Config.LobbyAIDifficulty.Beginner)
                return AISimpleDifficultyConfig.I.beginnerExtractorShare;
            return ProfileOf(faction).ExtractorShare;
        }

        /// <summary>Working extractors of this kind the faction owns (sites included).</summary>
        private static int CountShareExtractors(EntityManager em, Faction faction, string buildingId)
        {
            EntityQuery q;
            if (buildingId == "GatherersHut") q = QC_ShareHuts.Get(em, QT_ShareHuts);
            else if (buildingId == "Mine") q = QC_ShareMines.Get(em, QT_ShareMines);
            else if (buildingId == "VeilstoneMine") q = QC_ShareVeilMines.Get(em, QT_ShareVeilMines);
            else return 0;
            int n = 0;
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++) if (facs[i].Value == faction) n++;
            // Plans waiting for a worker count too, or a tier queues past its share.
            var pq = QC_PlansXf.Get(em, QT_PlansXf);
            if (!pq.IsEmptyIgnoreFilter)
            {
                var id = new FixedString64Bytes(buildingId);
                using var plans = pq.ToComponentDataArray<PlannedBuilding>(Allocator.Temp);
                using var pf = pq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < plans.Length; i++)
                    if (pf[i].Value == faction && plans[i].BuildingId == id) n++;
            }
            return n;
        }

        /// <summary>
        /// May this faction add one more extractor of this kind? Yes while it
        /// works fewer than its tier's share of the nodes it could work (those it
        /// works plus the free ones in its territory). <paramref name="freeNodes"/>
        /// is the free count when the caller already has it, or -1 to count here.
        /// </summary>
        private bool ExtractorShareAllows(EntityManager em, Faction faction, string buildingId,
            int freeNodes, out string why)
            => ExtractorShareRoom(em, faction, buildingId, freeNodes, out why) > 0;

        /// <summary>How many more extractors of this kind the tier's share allows
        /// (int.MaxValue for Expert, or a kind the share does not count).</summary>
        private int ExtractorShareRoom(EntityManager em, Faction faction, string buildingId,
            int freeNodes, out string why)
        {
            why = null;
            float share = ExtractorShareOf(faction, buildingId);
            if (share >= 1f) return int.MaxValue;            // Expert: everything
            if (buildingId != "GatherersHut" && buildingId != "Mine" && buildingId != "VeilstoneMine")
                return int.MaxValue;
            if (freeNodes < 0)
            {
                _shareOwned.Clear();
                if (TerritoryOwnership.Ready) _shareOwned.UnionWith(TerritoryOwnership.TerritoriesOf(faction));
                _shareFree.Clear();
                CollectFreeNodes(em, faction, buildingId, _shareOwned, _shareFree);
                freeNodes = _shareFree.Count;
            }
            int worked = CountShareExtractors(em, faction, buildingId);
            int allowed = (int)math.ceil(share * (worked + freeNodes) - 0.001f);
            if (worked >= allowed)
                why = $"tier share {share:P0}: {worked} of {worked + freeNodes} worked";
            return math.max(0, allowed - worked);
        }

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
            // A LOST EXTRACTOR IS REBUILT FIRST (2026-10-05, Game_AI.md §
            // 5i): while one is owed (TrackExtractorLosses) the walk runs every
            // extractorRebuildAttemptInterval and tries the lost kind first.
            bool rebuilding = TryGetRebuildOwed(faction, now, out int owedKind);
            if (rebuilding && _nextExtractorTime.TryGetValue(key, out float due)
                && due > now + Cfg.extractorRebuildAttemptInterval)
                _nextExtractorTime[key] = now + Cfg.extractorRebuildAttemptInterval;
            if (_nextExtractorTime.TryGetValue(key, out float next) && now < next) return;
            // PACE IS THE LADDER (2026-10-05, Game_AI.md § 2); a rebuild keeps
            // its own faster cadence on every tier.
            _nextExtractorTime[key] = now + (rebuilding ? Cfg.extractorRebuildAttemptInterval
                : Cfg.extractorAttemptInterval * math.max(0.1f, ProfileOf(faction).TerritoryCadenceScale));

            if (!TechCatalog.IsReady) return;
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
            _nodesSiteBlocked = 0;

            string owedId = rebuilding ? RebuildIds[owedKind] : null;
            for (int step = 0; step < ExtractorPlan.Length + (owedId != null ? 1 : 0); step++)
            {
                // The owed kind goes first, then the plan in its own order
                // (skipping the owed kind's second visit).
                string buildingId = owedId != null
                    ? (step == 0 ? owedId : ExtractorPlan[step - 1].Building)
                    : ExtractorPlan[step].Building;
                if (owedId != null && step > 0 && buildingId == owedId) continue;

                // Affordable and legal for this culture/era? TryBuildBuilding
                // re-checks, but asking first avoids scanning nodes for a
                // building we could not raise anyway.
                if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) continue;

                // Culture: the Runai never mine veilstone (Veilstone_Economy.md §3).
                bool runai = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Runai;
                if (buildingId == "VeilstoneMine" && runai) continue;
                // A hoard pauses new Mines (never the Mines standing; developer 2026-11-11).
                if ((buildingId == "Mine" || buildingId == "VeilstoneMine")
                    && FactionEconomy.TryGetResources(em, faction, out var hb)
                    && (buildingId == "Mine" ? hb.Iron > Cfg.ironNoNewMinesAbove
                                             : hb.Veilstone > Cfg.veilstoneNoNewMinesAbove))
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: hoard";
                    continue;
                }
                // The Runai raise a Sanctuary on the outcrop (Age_1_Runai.md §5.1).
                if (buildingId == "Runai_Sanctuary" && !runai) continue;

                // THE OPENING HUTS COME FIRST (2026-10-03): while the hut
                // pipeline is still raising its bootstrap huts, no ore
                // extractor may spend the supplies they are waiting on.
                if (buildingId != "GatherersHut"
                    && AIPivotalReserve.Has(faction, HutBootstrapReserveKey))
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: opening huts first";
                    continue;
                }
                var planCost = AICommon.ToCost(def.cost);
                if (!FactionEconomy.CanAfford(em, faction, planCost))
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: bank short";
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
                    continue;
                }
                // The tier's share of the workable nodes (Game_AI.md § 2c).
                int shareRoom = ExtractorShareRoom(em, faction, buildingId, _freeNodes.Count, out string shareWhy);
                if (shareRoom <= 0)
                {
                    if (AILogger.Enabled) blocked += $" | {buildingId}: {shareWhy}";
                    continue;
                }
                string reason = null;

                for (int n = 0; n < _freeNodes.Count; n++)
                {
                    if (!TryBuildBuildingWithReason(em, faction, buildingId,
                            out reason, _freeNodes[n])) continue;
                    AILogger.Log(faction, "EXTRACT",
                        $"{buildingId} on a free node at " +
                        $"({_freeNodes[n].x:F0},{_freeNodes[n].z:F0})");
                    return;   // one per attempt
                }
                // An owed rebuild every free node refuses is dropped after
                // extractorRebuildMaxRefusals walks, so its strict savings
                // goal cannot hold the bank for a site that never comes.
                if (buildingId == owedId) NoteRebuildRefused(faction, owedKind, reason, now);
                if (AILogger.Enabled)
                    blocked += $" | {buildingId}: {_freeNodes.Count} node(s), last refusal: {reason}";
            }

            if (blocked != null)
            {
                // Bad map data reads as an AI that will not build, so name it.
                if (_nodesUnreachable > 0)
                    blocked += $" | {_nodesUnreachable} node(s) skipped: nothing can reach them";
                if (_nodesSiteBlocked > 0)
                    blocked += $" | {_nodesSiteBlocked} site(s) skipped: lost there before (curse node in reach, or lost too often)";
                LogExtractBlocked(faction, now, blocked.Substring(3));
            }
        }

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
        private int _nodesOffTerritory, _nodesUnreachable, _nodesSiteBlocked;

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
                // A site that keeps dying waits (Game_AI.md § 5i).
                if (ExtractorSiteBlocked(em, faction, site, _thinkNow)) { _nodesSiteBlocked++; continue; }

                into.Add(site);
            }
        }
    }
}
