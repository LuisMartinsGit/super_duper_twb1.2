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
// outcropping, Smelter on a veilsteel deposit.
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

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>How often a faction tries to raise one extractor. Slower
        /// than the claim check: an extractor is an optimisation, and a faction
        /// that spends every spare coin on them fields no army.</summary>
        private const float ExtractorAttemptInterval = 15f;

        private readonly Dictionary<int, float> _nextExtractorTime = new Dictionary<int, float>();

        /// <summary>The extractor for each node kind, richest first. Veilsteel
        /// leads because it is the scarcest — about one territory in three has
        /// a deposit — so a free one is the rarest opportunity on the board.
        /// </summary>
        private static readonly (string Building, ComponentType Node)[] ExtractorPlan =
        {
            ("Alanthor_Smelter", default),
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
            if (_nextExtractorTime.TryGetValue(key, out float next) && now < next) return;
            _nextExtractorTime[key] = now + ExtractorAttemptInterval;

            if (!TechCatalog.IsReady) return;
            if (CountIdleBuilders(em, faction) == 0)
            {
                LogExtractBlocked(faction, now, "no idle builder");
                return;
            }

            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return;
            var owned = new HashSet<int>(mine);

            // Diagnostic trail: six 30-minute batch matches produced 80 huts
            // and not one ore extractor, and this walk failed SILENTLY at
            // every gate — the exact diagnostic hole LogClaimBlocked exists
            // to close for claims. Reasons collect per plan entry and log
            // throttled when the whole walk buys nothing.
            string blocked = null;

            for (int i = 0; i < ExtractorPlan.Length; i++)
            {
                string buildingId = ExtractorPlan[i].Building;

                // Affordable and legal for this culture/era? TryBuildBuilding
                // re-checks, but asking first avoids scanning nodes for a
                // building we could not raise anyway.
                if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) continue;
                if (!FactionEconomy.CanAfford(em, faction, ToCost(def.cost)))
                {
                    blocked += $" | {buildingId}: bank short";
                    continue;
                }

                // EVERY free node is a candidate, not just the first found.
                // One node can be legitimately unplaceable (a foundation
                // already over it, cursed ground, a terrain lip) — anchoring
                // on it alone made the 15 s retry pick the same dead node
                // forever while free ones sat a territory over.
                _freeNodes.Clear();
                CollectFreeNodes(em, buildingId, owned, _freeNodes);
                if (_freeNodes.Count == 0)
                {
                    blocked += $" | {buildingId}: no free owned node";
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
                blocked += $" | {buildingId}: {_freeNodes.Count} node(s), last refusal: {reason}";
            }

            if (blocked != null) LogExtractBlocked(faction, now, blocked.Substring(3));
        }

        /// <summary>Throttle for the "why didn't it extract" line — same
        /// contract as LogClaimBlocked.</summary>
        private const float ExtractLogInterval = 60f;
        private readonly Dictionary<int, float> _nextExtractLog = new Dictionary<int, float>();

        private void LogExtractBlocked(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextExtractLog.TryGetValue(key, out float next) && now < next) return;
            _nextExtractLog[key] = now + ExtractLogInterval;
            AILogger.Log(faction, "EXTRACT", $"blocked: {why}");
        }

        // Host-only managed scratch, cleared per use.
        private readonly List<float3> _freeNodes = new List<float3>();

        /// <summary>
        /// Every node of the kind <paramref name="buildingId"/> needs, inside
        /// our own territory, with no extractor of that kind already on it.
        ///
        /// Territory-gated on purpose: a node on somebody else's ground pays
        /// THEM, and the build gate would refuse the site anyway.
        /// </summary>
        private static void CollectFreeNodes(EntityManager em, string buildingId,
            HashSet<int> owned, List<float3> into)
        {
            var required = TerritoryOwnership.RequiredNodeFor(buildingId);
            if (required == null) return;

            var q = em.CreateEntityQuery(
                required.Value,
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                int region = RegionMap.RegionAt(p.x, p.z);
                if (region == RegionMap.None || !owned.Contains(region)) continue;
                if (!TerritoryOwnership.OnFreeNodeFor(em, buildingId, p.x, p.z)) continue;
                into.Add(p);
            }
        }
    }
}
