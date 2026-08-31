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
        // 90 -> 180 (2026-08-31): with iron-priced extractors the supply
        // income genuinely covers a Hall inside three minutes, but rarely
        // inside ninety seconds — the shorter hold lapsed just before the
        // pot filled, over and over, and the map stayed unclaimed.
        // 180 -> 120 (2026-08-31): unclaimed territory sitting idle is the
        // bigger failure — the reserve fills faster so claims land sooner.
        private const float ClaimSaveSeconds = 120f;

        /// <summary>Breathing room after a claim lands, before the brain may
        /// hold income back for the next one. Without it a faction that can
        /// expand would save continuously and never train anything.</summary>
        // 120 -> 60 (2026-08-31): the map has ~25 territories and matches
        // now decide in under an hour of game time — a two-minute breather
        // per claim meant most ground was never taken by anyone.
        // 60 -> 30 (2026-08-31): a successful claim should chain into the
        // next one while the map still has Natural ground — half the pause.
        private const float ClaimSuccessCooldown = 30f;

        /// <summary>Army a settled faction (3+ territories) must field before
        /// it starts saving for ANOTHER claim. Keeps the perpetual land-grab
        /// from throttling armies all match. 12 -> 8 (batch 5): under
        /// constant curse-wave pressure armies rarely rebuilt past 12, so
        /// expansion froze at exactly three territories — 8 keeps both
        /// engines turning.</summary>
        // 8 -> 6 (batch 17): with the age-up healed (48/48 era 2) armies
        // equilibrate at 6-7 under curse-wave attrition — one unit below the
        // gate, so claims still parked at three territories. Six sits under
        // the observed steady state, letting both engines actually run.
        private const int MinArmyForNextClaim = 6;

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

        /// <summary>AIPivotalReserve key for the expansion Hall's lump sum.</summary>
        private const string ClaimReserveKey = "ClaimHall";

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

            // ARMY BEFORE THE NEXT CLAIM (2026-08-31, after the duty-cycle
            // batches): with saving holds pausing army training and always
            // another territory to save for, factions expanded beautifully
            // (3-5 territories each) but kept armies throttled ALL MATCH —
            // 24 matches, zero eliminations. Once a faction holds a real
            // economy (3+ territories), the next claim waits until the army
            // is back to the plan's wave bar; the opening land-grab is
            // untouched.
            if (TerritoryOwnership.CountOf(faction) >= 3
                && CountAliveMilitary(em, faction) < MinArmyForNextClaim)
            {
                // DO NOT CLEAR THE RESERVE (batch 18). Clearing it here
                // built a thermostat: army below the gate -> savings wiped ->
                // hold off -> army trains to the gate -> savings restart ->
                // hold on -> growth stops -> a wave kills one -> below the
                // gate again. Armies pinned at EXACTLY gate-1 at every gate
                // value tried (7/8 then 5/6) and territory #4 never came.
                // The pot keeps filling while the army rebuilds — army
                // growth below the gate is hold-exempt in the Economy burst
                // and the train gate, so both engines ratchet instead of
                // fighting.
                LogClaimBlocked(faction, now,
                    $"army first ({CountAliveMilitary(em, faction)}/{MinArmyForNextClaim})");
                return;
            }
            // APPETITE HAS A FLOOR (2026-08-31 directive): whole batches
            // ended with most of the map unclaimed and every faction starved
            // for resources, because Rush (0.4) and Fortress (0.5) plans
            // barely claim — but under Regions.md §4 territory IS the income
            // that pays for their armies and walls. The plan still sets the
            // pace above the floor; nobody is allowed to opt out of eating.
            // Floor raised 0.8 -> 1.0 (2026-08-31): every personality pushes
            // for unclaimed territory at full cadence — the plan profile can
            // only make an AI hungrier than baseline now, never lazier.
            appetite = math.max(appetite, 1f);

            int key = (int)faction;
            // CLAIM THE MOMENT THE POT FILLS (2026-08-31, batch 7). The
            // attempt interval used to gate even a FUNDED claim — and in the
            // window between the pot filling (ShouldHold releases) and the
            // next scheduled attempt, the freed spenders (emergency towers
            // arm at bank >= 800, army training resumes) ate it back below
            // the Hall's price. Whole matches cycled fill->drain->"bank
            // short" on territory #4 forever. A pending reserve the bank can
            // cover bypasses the interval: the pot converts to a Hall the
            // same tick it fills, before anything else can spend it.
            bool potReady = AIPivotalReserve.Has(faction, ClaimReserveKey)
                && TechCatalog.IsReady
                && TechCatalog.TryGetBuilding("Hall", out var hallDef)
                && FactionEconomy.CanAfford(em, faction, ToCost(hallDef.cost));
            if (!potReady)
            {
                if (_nextClaimTime.TryGetValue(key, out float next) && now < next) return;
            }
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
                // Only release OUR OWN hold (priority 0) — this used to clear
                // unconditionally and silently destroyed the age-up's
                // priority-1 reservation every claim tick.
                AIBudget.ClearReservation(faction, maxPriority: 0);
                AIPivotalReserve.Clear(faction, ClaimReserveKey);
                LogClaimBlocked(faction, now, "no claimable region in reach");
                return;
            }

            // Someone to send — ANY live builder, not an IDLE one (2026-08-31
            // balance investigation). Requiring idleness locked the ECONOMY
            // personality out of expansion entirely: its builders are always
            // mid-hut, so Green logged ONE claim in eight matches while Red's
            // idle crews claimed 34. A foundation waits, and builders
            // auto-chain to it the moment they free — that is the existing
            // construction contract, so idleness at decision time was pure
            // friction aimed at exactly the identity expansion feeds.
            int builders = CountAliveMiners(em, faction);
            if (builders == 0)
            {
                AIPivotalReserve.Clear(faction, ClaimReserveKey);
                LogClaimBlocked(faction, now, "no builder alive");
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
                // THE WALLET RESERVATION ALONE CANNOT FORM THE LUMP SUM. The
                // wallets are accounting over one shared bank, and most
                // spending is bank-direct — the exact failure the age-up hit
                // (see the 2026-08-18 wallet notes): entitlement without
                // cash. Measured in an eight-match batch: 221 "saving …
                // bank short" lines, roughly one claim per faction per
                // 30 minutes, and one faction that saved all match and never
                // claimed at all. AIPivotalReserve is what actually pauses
                // the discretionary spenders (army growth, the research
                // sweep) until the bank covers the Hall — bounded by its own
                // 90 s famine release, so a poor faction is never deadlocked.
                AIPivotalReserve.Set(faction, ClaimReserveKey, cost);
                // Instrumented (batch 10): carry the LIVE bank so the log
                // shows exactly how far the pot is from the price — three
                // tuning rounds guessed at this number.
                FactionEconomy.TryGetBank(em, faction, out var bankEnt);
                var live = em.HasComponent<TheWaningBorder.Economy.FactionResources>(bankEnt)
                    ? em.GetComponentData<TheWaningBorder.Economy.FactionResources>(bankEnt)
                    : default;
                LogClaimBlocked(faction, now,
                    $"saving for {RegionMap.NameOf(region)} " +
                    $"(need {cost.Supplies}s/{cost.Iron}i, have {live.Supplies}s/{live.Iron}i, " +
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
            AIPivotalReserve.Clear(faction, ClaimReserveKey);
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

            // Resource nodes, so a region can be valued by what stands in it —
            // EVERY kind the territory tick pays for (Regions.md §4): the three
            // ores, and supply nodes too, since the base supply tick scales
            // with them and they cap the huts. Every territory is guaranteed
            // 2 supply nodes and 1 ore node, so what separates candidates is
            // the surplus: a 4-node home, a veilsteel deposit, a rich field.
            var nodeXfs = new List<float3>();
            CollectNodePositions<IronMineTag>(em, nodeXfs);
            CollectNodePositions<VeilstoneOutcroppingTag>(em, nodeXfs);
            CollectNodePositions<VeilsteelDepositTag>(em, nodeXfs);
            CollectNodePositions<SupplyNodeTag>(em, nodeXfs);

            float bestScore = float.MinValue;
            float minNearest = float.MaxValue;
            var candidates = new List<(int r, float nearest)>();
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (TerritoryOwnership.OwnerOf(r) != TerritoryOwnership.Natural) continue;

                // DO NOT BUY THE SAME GROUND TWICE.
                //
                // TerritoryOwnership.Claim skips buildings that are still
                // UnderConstruction, so a Hall that has been PLACED but not
                // FINISHED claims nothing — and the region it stands in stays
                // Natural. This scorer then picks it again on the next cooldown,
                // and the faction pays the full Hall price for ground it is
                // already building on.
                //
                // Logged: one AI claimed the same region three times in eight
                // minutes and never held a second territory; an earlier match
                // saw one region claimed eight times by four factions and held
                // by nobody. A pending Hall is a claim in progress, not an
                // invitation to start another.
                if (HasOwnHallIn(em, faction, r)) continue;

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
                if (nearest < minNearest) minNearest = nearest;
                candidates.Add((r, nearest));
            }

            // REACH SCALES WITH THE MAP. The flat 220 m cap was tuned on
            // Sundered Crown (512 m) and silently made EVERY region on
            // Veilmarch (1024 m) unclaimable: the nearest neighbouring seed
            // there is 276-427 m from a home. Twelve batch matches ended with
            // all 48 factions holding exactly one territory and zero CLAIM
            // lines in any log. The cap now stretches to 1.5x the nearest
            // candidate, so "claim adjacent ground" keeps its meaning at any
            // seed spacing while genuinely distant land stays out of reach.
            float reach = math.max(MaxClaimReach, minNearest * 1.5f);
            foreach (var (r, nearest) in candidates)
            {
                if (nearest > reach) continue;

                // Nodes standing in this region — the reason to want it.
                int nodes = 0;
                for (int i = 0; i < nodeXfs.Count; i++)
                    if (RegionMap.RegionAt(nodeXfs[i].x, nodeXfs[i].z) == r) nodes++;

                var seedP = RegionMap.SeedOf(r);
                float score = -nearest + nodes * ClaimNodeBonus;
                // Deterministic tie-break: lockstep peers must agree, and
                // region ids are the one stable ordering the partition has.
                if (score > bestScore)
                {
                    bestScore = score;
                    region = r;
                    anchor = new float3(seedP.x,
                        TerrainUtility.GetHeight(seedP.x, seedP.y), seedP.y);
                }
            }
            return region != RegionMap.None;
        }

        /// <summary>
        /// Does this faction already have a Hall in that region — finished OR
        /// still going up? Deliberately counts foundations: the point is to
        /// notice a claim already in flight.
        /// </summary>
        private static bool HasOwnHallIn(EntityManager em, Faction faction, int region)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                // NearestRegion, matching TerritoryOwnership.Claim — RegionAt
                // answers None outside the claimable band, and a Hall that
                // files nowhere would look absent here while still claiming
                // once it completes.
                if (RegionMap.NearestRegion(p.x, p.z) == region) return true;
            }
            return false;
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
