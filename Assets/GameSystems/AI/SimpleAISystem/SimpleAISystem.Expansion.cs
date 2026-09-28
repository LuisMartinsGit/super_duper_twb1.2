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
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        #endregion








        /// <summary>AIPivotalReserve key for the expansion Hall's lump sum.</summary>
        private const string ClaimReserveKey = "ClaimHall";

        // Host-only managed state, same as _missions.
        private readonly Dictionary<int, float> _nextClaimTime = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _lastPotClaimTry = new Dictionary<int, float>();

        /// <summary>
        /// (faction, region) -> the sim time its siting failure expires.
        ///
        /// TryPickClaimTarget rescores every region from scratch on each
        /// attempt and keeps the best one. When the SITE SEARCH inside that
        /// region fails -- a lake, crust, a rival's foundation -- nothing
        /// recorded it, so the next attempt scored the same region top and
        /// failed in exactly the same way. Observed live on 2026-09-12: Red
        /// logged "no legal site in Northeast Field near (223,223)" once a
        /// minute for six consecutive minutes with the money in the bank and
        /// twenty-eight other regions on the map, having spent the previous
        /// eleven minutes saving up for it.
        ///
        /// A short, EXPIRING exclusion, not a permanent one: the ground can
        /// change. A rival's foundation completes or dies, crust recedes, a
        /// blocking building falls. Long enough to try the runner-up,
        /// short enough to come back if it was only temporary.
        /// </summary>
        private readonly Dictionary<(int faction, int region), float> _siteBlocked = new();

        /// <summary>How long a region stays skipped after its site search
        /// found nothing. One claim attempt interval would let it come
        /// straight back; a couple of minutes lets the runner-up actually be
        /// tried.</summary>
        private const float SiteBlockSeconds = 150f;
        private readonly Dictionary<int, float> _nextClaimLog = new Dictionary<int, float>();

        /// <summary>Set by <see cref="EnsureClaimBuilderOnSite"/> when a Hall
        /// placement was deferred because its worker is still walking to the
        /// site. Single-threaded think loop, so a field is safe.</summary>
        private bool _claimAwaitingBuilder;

        /// <summary>faction -> the worker walking to a Hall site, where to,
        /// and when that order was last issued.</summary>
        private readonly Dictionary<int, (Entity builder, float3 target, float issuedAt)> _claimWalker = new();

        /// <summary>
        /// A HALL NEEDS ITS BUILDER ON SITE (Regions.md §2, 2026-09-26). The
        /// router refuses a claim unless one of the faction's workers stands
        /// within <see cref="TerritoryOwnership.HallBuilderRange"/> of it AND
        /// inside the territory it claims (2026-09-27), and the executor
        /// re-checks at the execution tick — so the AI sends a
        /// worker first and places once it is there.
        ///
        /// Returns true with <paramref name="builder"/> set when a worker is
        /// already in range (with a small margin, so a worker at the edge
        /// cannot drift out between issue and execution). Otherwise walks one
        /// there — the one already walking if it is still alive, else the
        /// nearest idle worker, else the nearest worker at all — and returns
        /// false with <see cref="_claimAwaitingBuilder"/> set.
        /// </summary>
        private bool EnsureClaimBuilderOnSite(EntityManager em, Faction faction, float3 site,
            out Entity builder, out string reason)
        {
            builder = Entity.Null;
            reason = null;
            float range = TerritoryOwnership.HallBuilderRange;
            float accept = math.max(1f, range - 2f);

            var q = QC_CanBuildFactionTag.Get(em, QT_CanBuildFactionTag);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity nearest = Entity.Null, nearestIdle = Entity.Null, onSite = Entity.Null;
            float nearestD = float.MaxValue, nearestIdleD = float.MaxValue, onSiteD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!TerritoryOwnership.IsLiveBuilder(em, faction, e)) continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - site.x, dz = p.z - site.z;
                float d = dx * dx + dz * dz;
                if (d < nearestD) { nearestD = d; nearest = e; }
                if (d < nearestIdleD && !AICommon.IsCommittedWorker(em, e))
                { nearestIdleD = d; nearestIdle = e; }
                // ON SITE = in range AND inside the territory being claimed
                // (2026-09-27): a worker across the border in our own region
                // is in range but is not standing on the ground it claims.
                if (d < onSiteD && d <= accept * accept
                    && TerritoryOwnership.IsInSiteTerritory(p.x, p.z, site.x, site.z))
                { onSiteD = d; onSite = e; }
            }
            if (nearest == Entity.Null) { reason = "no builder alive"; return false; }

            if (onSite != Entity.Null)
            {
                builder = onSite;
                _claimWalker.Remove((int)faction);
                return true;
            }

            // Walk one there. Keep the worker already on its way.
            int key = (int)faction;
            Entity walker = Entity.Null;
            bool reissue = true;
            if (_claimWalker.TryGetValue(key, out var w)
                && TerritoryOwnership.IsLiveBuilder(em, faction, w.builder))
            {
                walker = w.builder;
                float tdx = w.target.x - site.x, tdz = w.target.z - site.z;
                reissue = tdx * tdx + tdz * tdz > 64f
                          || _thinkNow - w.issuedAt >= Cfg.claimBuilderRewalkSeconds;
            }
            if (walker == Entity.Null) walker = nearestIdle != Entity.Null ? nearestIdle : nearest;

            if (reissue)
            {
                // Stand beside the footprint, on the walker's side of it.
                var wp = em.GetComponentData<LocalTransform>(walker).Position;
                float2 dir = new float2(wp.x - site.x, wp.z - site.z);
                float len = math.length(dir);
                dir = len > 0.01f ? dir / len : new float2(1f, 0f);
                int2 size = BuildingSizeConfig.GetSize("Hall");
                float standOff = math.cmax(size) * 0.5f + Cfg.claimBuilderStandOff;
                var dest = ClaimStandPoint(site, dir, standOff);
                dest.y = TerrainUtility.GetHeight(dest.x, dest.z);
                TheWaningBorder.Core.Commands.CommandRouter.IssueMove(em, walker, dest,
                    TheWaningBorder.Core.Commands.CommandSource.AI);
                _claimWalker[key] = (walker, site, _thinkNow);
            }

            _claimAwaitingBuilder = true;
            reason = "worker walking to the Hall site";
            return false;
        }

        /// <summary>
        /// Where the claim worker stands: beside the footprint, on the
        /// walker's side of it — but INSIDE the territory the Hall claims
        /// (Regions.md §2, 2026-09-27). A site near the border would otherwise
        /// park the worker across the line, in range and still refused. Tries
        /// the walker's bearing first, then rotates in
        /// <see cref="ClaimStandBearings"/> steps alternating either side of
        /// it; if no bearing lands inside, the site itself (the footprint is
        /// open ground until the Hall is placed).
        /// </summary>
        private static float3 ClaimStandPoint(float3 site, float2 dir, float standOff)
        {
            for (int k = 0; k < ClaimStandBearings; k++)
            {
                // 0, +1, -1, +2, -2, ... steps around the circle.
                int step = (k + 1) / 2 * ((k & 1) == 1 ? 1 : -1);
                float a = step * (2f * math.PI / ClaimStandBearings);
                float c = math.cos(a), s = math.sin(a);
                float2 d = new float2(dir.x * c - dir.y * s, dir.x * s + dir.y * c);
                var p = new float3(site.x + d.x * standOff, 0f, site.z + d.y * standOff);
                if (TerritoryOwnership.IsInSiteTerritory(p.x, p.z, site.x, site.z)) return p;
            }
            return new float3(site.x, 0f, site.z);
        }

        /// <summary>Bearings tried around a Hall site for the claim worker's
        /// stand point. Loop resolution, not tuning (CLAUDE.md's carve-out
        /// for AIWallPlanner.Bearings), so it stays a const.</summary>
        private const int ClaimStandBearings = 8;

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
                && CountAliveMilitary(em, faction) < Cfg.minArmyForNextClaim)
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
                    $"army first ({CountAliveMilitary(em, faction)}/{Cfg.minArmyForNextClaim})");
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
            // The Hall's price ESCALATES with every Hall the faction already
            // has (Regions.md §2) — BuildCosts.For is what the executor charges.
            bool potReady = AIPivotalReserve.Has(faction, ClaimReserveKey)
                && TechCatalog.IsReady
                && FactionEconomy.CanAfford(em, faction, BuildCosts.For(em, faction, "Hall"));
            if (!potReady)
            {
                if (_nextClaimTime.TryGetValue(key, out float next) && now < next) return;
            }
            // …but a funded pot that FAILED to place (no legal Hall site) used
            // to retry the whole claim — target pick and a Hall site search —
            // on every single think (2026-09-25). Still near-immediate, just
            // not per think.
            else if (_lastPotClaimTry.TryGetValue(key, out float lastTry)
                     && now - lastTry < Cfg.claimPotReadyRetrySeconds) return;
            if (potReady) _lastPotClaimTry[key] = now;
            _nextClaimTime[key] = now + Cfg.claimAttemptInterval / appetite;

            // A Hall to expand FROM.
            Entity home = FindFactionBuilding<HallTag>(em, faction);
            if (home == Entity.Null || !em.HasComponent<LocalTransform>(home)) return;

            if (!TechCatalog.IsReady) return;
            if (!TechCatalog.TryGetBuilding("Hall", out var def) || def == null) return;
            // The escalated price for THIS faction's next Hall (Regions.md §2).
            var cost = BuildCosts.For(em, faction, "Hall");

            // Somewhere to go. No target means no reason to hold income back.
            if (!TryPickClaimTarget(em, faction, now, out int region, out float3 anchor))
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
                AIBudget.Reserve(faction, cost, now, Cfg.claimSaveSeconds);
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
            _claimAwaitingBuilder = false;
            if (!TryBuildBuilding(em, faction, "Hall", anchor))
            {
                // THE BUILDER IS STILL WALKING (Regions.md §2: a Hall needs a
                // worker on site). The site is fine — a worker has been sent
                // to it. Hold the pot so nothing spends it, retry on the
                // funded-pot cadence, and do NOT mark the region unsitable.
                if (_claimAwaitingBuilder)
                {
                    AIPivotalReserve.Set(faction, ClaimReserveKey, cost);
                    _nextClaimTime[key] = now + Cfg.claimPotReadyRetrySeconds;
                    LogClaimBlocked(faction, now,
                        $"worker walking to the Hall site in {RegionMap.NameOf(region)}");
                    return;
                }

                // Nothing legal there — a lake, a cursed crust, a rival's
                // foundation. Keep saving; the site search is what failed, not
                // the money.
                // REMEMBER THE FAILURE, or the next attempt is this one.
                _siteBlocked[((int)faction, region)] = now + SiteBlockSeconds;
                // SAY WHY. The site search already tallies its refusal
                // reasons for the GOALS line; the claim path threw that away
                // and logged only "no legal site", which is the difference
                // between a bug report and a shrug. Three regions on
                // Veilmarch rejected a Hall at their seed point for nineteen
                // minutes and there was no way to tell whether it was
                // terrain, the territory gate, spacing or a node.
                LogClaimBlocked(faction, now,
                    $"no legal site in {RegionMap.NameOf(region)} " +
                    $"near ({anchor.x:F0},{anchor.z:F0}) [{_siteRefusalTally}] " +
                    $"— trying elsewhere for {SiteBlockSeconds:F0}s");
                return;
            }

            AIBudget.RecordSpend(faction, AIBudgetCategory.EconomyExpansion, cost);
            AIBudget.ClearReservation(faction);
            AIPivotalReserve.Clear(faction, ClaimReserveKey);
            _nextClaimTime[(int)faction] = now + Cfg.claimSuccessCooldown;

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
            _nextClaimLog[key] = now + Cfg.claimLogInterval;
            AILogger.Log(faction, "CLAIM", $"no claim: {why}");
        }

        /// <summary>
        /// Best unclaimed region to take next: ADJACENT to ground we already
        /// hold (the Hall adjacency rule, docs/Design/Regions.md §2 — the
        /// router refuses anything else), then the closest and richest.
        ///
        /// Adjacency used to be approximated by seed distance with a reach
        /// cap. It is the real neighbour graph now (RegionMap.AreAdjacent,
        /// the same one the router and the curse use), because an
        /// approximation that picks a region the router will refuse would
        /// save for, walk to, and fail on it forever.
        /// </summary>
        private bool TryPickClaimTarget(EntityManager em, Faction faction, float now,
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
            var candidates = new List<(int r, float nearest)>();
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (TerritoryOwnership.OwnerOf(r) != TerritoryOwnership.Natural) continue;

                // Only next door to ground we hold — the router's rule.
                if (!TerritoryOwnership.IsAdjacentToHeld(faction, r)) continue;

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

                // Skip a region whose site search failed recently. See
                // _siteBlocked: without this the scorer hands back the same
                // unsitable region every attempt, forever.
                if (_siteBlocked.TryGetValue(((int)faction, r), out float until)
                    && now < until) continue;

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
                candidates.Add((r, nearest));
            }

            // No reach cap any more: every candidate is adjacent to ground we
            // hold, which is what the old seed-distance cap (tuned per map,
            // and wrong on Veilmarch until it was stretched) was guessing at.
            foreach (var (r, nearest) in candidates)
            {
                // Nodes standing in this region — the reason to want it.
                int nodes = 0;
                for (int i = 0; i < nodeXfs.Count; i++)
                    if (RegionMap.RegionAt(nodeXfs[i].x, nodeXfs[i].z) == r) nodes++;

                var seedP = RegionMap.SeedOf(r);
                float score = -nearest + nodes * Cfg.claimNodeBonus;
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
            var q = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
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
            var q = AIQueryCache.TagXf<T>(em);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++) into.Add(xfs[i].Position);
        }
    }
}
