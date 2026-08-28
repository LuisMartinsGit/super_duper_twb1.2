// SimpleAISystem.Expansion.cs
// Territory claiming: the AI raises Halls on unowned ground to take regions.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY THIS EXISTS
//
// docs/Design/Regions.md made the Hall the claim structure and territory the
// income: a region yields only to whoever holds it, and one Hall holds it
// (TerritoryOwnership.IsClaimStructure / HallCapReached). Every rule needed to
// expand was already implemented and enforced — CanBuildAt lets a Hall, and
// only a Hall, go down on Natural ground, which the file itself calls "the
// whole expansion loop".
//
// The AI never walked it. No build order lists a Hall, and the site picker
// anchors its ring search on the home Hall, where HallCapReached is true by
// definition. So the AI played the whole match on its start region, on start
// region income, while the map sat unclaimed. SimpleAISystem.cs even carries
// the note that "the AI's economic decision is now WHERE TO CLAIM" — the
// decision just had nothing making it.
//
// Claiming is opportunistic, not a scripted step: it depends on what the bank
// holds and what ground is still free, neither of which a fixed build order
// can know. So it is a standing check, like EnsurePopulationHeadroom.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>How often a faction may attempt a claim. A Hall is
        /// expensive and the site search is not free; there is no value in
        /// retrying every tick.</summary>
        private const float ClaimAttemptInterval = 12f;

        /// <summary>
        /// How long the brain will hold income back while saving for a Hall.
        ///
        /// Sized on observed income: the logged AIs ran 5-7 supplies/s from a
        /// mean bank near 250, so a 600-supply Hall is about a minute of not
        /// spending. Beyond that the goal is not going to complete and the hold
        /// is only starving the faction, so the reservation lapses and the
        /// economy gets its income back.
        ///
        /// Saving DOES cost production — that is what an expansion costs, and
        /// it is why the hold is bounded at both ends: it lapses here, and a
        /// successful claim buys the economy a recovery window before the
        /// brain starts saving for the next one.
        /// </summary>
        private const float ClaimSaveSeconds = 90f;

        /// <summary>Breathing room after a claim lands, before the brain may
        /// hold income back for the next one. Without it a faction that can
        /// expand would save continuously and never train anything.</summary>
        private const float ClaimSuccessCooldown = 120f;

        /// <summary>Throttle for the "why didn't it claim" line, so a blocked
        /// claim is diagnosable without filling the log.</summary>
        private const float ClaimLogInterval = 60f;

        /// <summary>Ignore regions further than this from anything we hold.
        /// A claim across the map is a Hall nobody can defend and builders
        /// walking for a minute to reach it.</summary>
        private const float MaxClaimReach = 220f;

        /// <summary>A region with resources is worth more than empty ground —
        /// territory income comes from the nodes standing in it
        /// (Regions.md §4).</summary>
        private const float ClaimNodeBonus = 60f;

        // Host-only managed state, same as _missions.
        private readonly Dictionary<int, float> _nextClaimTime = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextClaimLog = new Dictionary<int, float>();

        /// <summary>
        /// Raise a Hall on the best unclaimed region within reach. One attempt
        /// per <see cref="ClaimAttemptInterval"/>.
        /// </summary>
        private void EnsureTerritoryClaim(EntityManager em, Faction faction, float now)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;

            // APPETITE IS THE PLAN'S. A booming AI expands twice as often; a
            // massing or rushing one mostly does not, because the whole point
            // of those plans is that the income goes into troops instead.
            // Without this every plan expanded identically and "booming" was
            // a word rather than a behaviour.
            float appetite = PlanProfileOf(faction).ClaimAppetite;
            if (appetite <= 0.01f) return;

            int key = (int)faction;
            if (_nextClaimTime.TryGetValue(key, out float next) && now < next) return;
            _nextClaimTime[key] = now + ClaimAttemptInterval / appetite;

            // A Hall to expand FROM.
            Entity home = FindFactionBuilding<HallTag>(em, faction);
            if (home == Entity.Null || !em.HasComponent<LocalTransform>(home)) return;

            if (!TechCatalog.IsReady) return;
            if (!TechCatalog.TryGetBuilding("Hall", out var def) || def == null) return;
            var cost = ToCost(def.cost);

            // Somewhere to go. No target means no reason to hold income back.
            if (!TryPickClaimTarget(em, faction, out int region, out float3 anchor))
            {
                AIBudget.ClearReservation(faction);
                return;
            }

            // Someone to send. A reservation with nobody to spend it is just
            // starvation, so this gates the saving too.
            int builders = CountIdleBuilders(em, faction);
            if (builders == 0)
            {
                LogClaimBlocked(faction, now, "no idle builder");
                return;
            }

            // ── AFFORD IT, OR START SAVING FOR IT. ──
            //
            // This used to be a plain CanAfford against the bank with a 1.35x
            // safety margin on top, and it never once passed. Across a logged
            // 14-minute four-AI match the banks oscillated between 30 and 748
            // supplies with a mean near 250 — every spender bought whatever it
            // could afford the moment it could afford it — so a 600-supply
            // Hall was unreachable at any instant the check happened to run,
            // while iron piled to 2,000+ unspent. The margin put the bar at
            // 810, above every faction's ALL-TIME PEAK. One faction claimed
            // twice on a lucky spike; three never claimed at all.
            //
            // Opportunistic buying cannot reach a lump sum. Expanding is a
            // decision, so it commits income: the reservation holds the Hall's
            // price back from every other spender until the pot fills.
            // honourReservation is false here because this IS the goal the
            // reservation was made for.
            bool bankOk = FactionEconomy.CanAfford(em, faction, cost);
            bool budgetOk = AIBudget.TryAfford(faction,
                AIBudgetCategory.EconomyExpansion, cost, now, honourReservation: false);

            if (!bankOk || !budgetOk)
            {
                AIBudget.Reserve(faction, cost, now, ClaimSaveSeconds);
                LogClaimBlocked(faction, now,
                    $"saving for {RegionMap.NameOf(region)} " +
                    $"(need {cost.Supplies}s/{cost.Iron}i, " +
                    $"{(bankOk ? "budget" : "bank")} short)");
                return;
            }

            // Site the Hall on the TARGET REGION, not the home base. This is
            // the whole difference: anchored at home, every candidate lands in
            // ground already claimed, where HallCapReached refuses it.
            if (!TryBuildBuilding(em, faction, "Hall", anchor))
            {
                // Nothing legal there — a lake, a cursed crust, a rival's
                // foundation. Keep saving; the site search is what failed, not
                // the money.
                LogClaimBlocked(faction, now,
                    $"no legal site in {RegionMap.NameOf(region)} " +
                    $"near ({anchor.x:F0},{anchor.z:F0})");
                return;
            }

            AIBudget.RecordSpend(faction, AIBudgetCategory.EconomyExpansion, cost);
            AIBudget.ClearReservation(faction);
            _nextClaimTime[(int)faction] = now + ClaimSuccessCooldown;

            AILogger.Log(faction, "CLAIM",
                $"claiming {RegionMap.NameOf(region)} at ({anchor.x:F0},{anchor.z:F0}) " +
                $"— holding {TerritoryOwnership.CountOf(faction)} territories");
        }

        /// <summary>
        /// Say WHY no claim happened, at most once a minute per faction.
        ///
        /// The first version of this returned silently at six different gates,
        /// so "the AI never claimed anything" was a fact with no evidence
        /// attached — exactly the diagnostic hole the build-order code already
        /// complains about in TickAttackWaves.
        /// </summary>
        private void LogClaimBlocked(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextClaimLog.TryGetValue(key, out float next) && now < next) return;
            _nextClaimLog[key] = now + ClaimLogInterval;
            AILogger.Log(faction, "CLAIM", $"no claim: {why}");
        }

        /// <summary>
        /// Best unclaimed region to take next: close to ground we already
        /// hold, and carrying resources if it can.
        ///
        /// Adjacency is approximated by distance between region seeds rather
        /// than a neighbour graph. The partition is a Voronoi diagram, so
        /// seed distance IS adjacency to within a cell — and it degrades
        /// gracefully, where a wrong neighbour list would send builders
        /// somewhere unreachable.
        /// </summary>
        private bool TryPickClaimTarget(EntityManager em, Faction faction,
            out int region, out float3 anchor)
        {
            region = RegionMap.None;
            anchor = default;

            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return false;

            // Resource nodes, so a region can be valued by what stands in it.
            var nodeXfs = new List<float3>();
            CollectNodePositions<IronMineTag>(em, nodeXfs);
            CollectNodePositions<VeilstoneOutcroppingTag>(em, nodeXfs);

            float bestScore = float.MinValue;
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (TerritoryOwnership.OwnerOf(r) != TerritoryOwnership.Natural) continue;

                var seed2 = RegionMap.SeedOf(r);
                float3 seed = new float3(seed2.x, 0f, seed2.y);

                // Distance to the nearest territory we already hold.
                float nearest = float.MaxValue;
                for (int i = 0; i < mine.Count; i++)
                {
                    var m2 = RegionMap.SeedOf(mine[i]);
                    float dx = seed.x - m2.x, dz = seed.z - m2.y;
                    float d = math.sqrt(dx * dx + dz * dz);
                    if (d < nearest) nearest = d;
                }
                if (nearest > MaxClaimReach) continue;

                // Nodes standing in this region — the reason to want it.
                int nodes = 0;
                for (int i = 0; i < nodeXfs.Count; i++)
                    if (RegionMap.RegionAt(nodeXfs[i].x, nodeXfs[i].z) == r) nodes++;

                float score = -nearest + nodes * ClaimNodeBonus;
                // Deterministic tie-break: lockstep peers must agree, and
                // region ids are the one stable ordering the partition has.
                if (score > bestScore)
                {
                    bestScore = score;
                    region = r;
                    anchor = new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.z), seed.z);
                }
            }
            return region != RegionMap.None;
        }

        private static void CollectNodePositions<T>(EntityManager em, List<float3> into)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++) into.Add(xfs[i].Position);
        }
    }
}
