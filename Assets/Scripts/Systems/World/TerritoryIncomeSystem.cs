// TerritoryIncomeSystem.cs
// Territory is the economy.
//
// docs/Design/Regions.md §4: income comes from the ground you hold, not from
// workers gathering. Per owned territory:
//
//   * a base SUPPLY trickle — a bare-ground floor plus a share per SUPPLY
//     NODE standing in the territory, so the base correlates with the map
//   * plus supplies for each FOREST inside it (a Sawyer multiplies that)
//   * plus 50/min of supplies for each GATHERER'S HUT — and a hut may only
//     stand on a supply node, so how many a territory supports is map data
//   * plus 190/min of IRON / VEILSTONE (95/min of VEILSTEEL) for each
//     resource NODE in it
//   * plus 25/min per MINE LEVEL built on one of those nodes
//
// A player's economy is therefore a map position. Losing a territory is losing
// income, immediately and visibly, which is what makes the claim game the game.
//
// EVERYTHING IS AUTHORED PER MINUTE, because per-minute is the unit the player
// is shown: every Hall states what its territory yields
// (<see cref="YieldOf"/>), and a number the player reads has to be the same
// number the designer typed. The tick converts, not the other way round.
//
// One computation, two callers. <see cref="ComputeYield"/> is what the tick
// pays out AND what the Hall panel displays, so the readout cannot drift from
// the payout — a readout that lies about income is worse than no readout.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.World
{
    /// <summary>What one territory pays, per minute, by resource.</summary>
    public struct TerritoryYield
    {
        public float Supplies;
        public float Iron;
        public float Veilstone;
        public float Veilsteel;

        public bool IsEmpty => Supplies <= 0f && Iron <= 0f
                            && Veilstone <= 0f && Veilsteel <= 0f;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class TerritoryIncomeSystem : SystemBase
    {
        // ── rates, PER MINUTE (docs/Design/Regions.md §4) ────────────────
        /// <summary>Seconds between income ticks. Presentation only — it
        /// decides how lumpy the bank looks, not how much is paid.</summary>
        private const float TickInterval = 5f;

        /// <summary>Supplies a held territory pays for its bare ground, before
        /// its least-developed slot multiplies it (see ComputeYield).</summary>
        // TERRITORY CONTENTS HAVE TO MATTER MORE THAN TERRITORY COUNT.
        //
        // The base used to be a flat 72/min on every territory, whether it
        // held anything or not — restored to that number after 52 starved the
        // AI of building money ("nothing affordable" 60 times in a 15-minute
        // match while veilstone banked past 5,000). The flat number had the
        // same flaw at a smaller scale that the original 72 had at 63% of all
        // demand: every region fed you identically, so no region was worth
        // taking in particular.
        //
        // The base now CORRELATES WITH THE SUPPLY NODES standing in the
        // territory (Regions.md §4, 2026-08-29): bare ground pays this floor —
        // holding it is never pointless — and each supply node adds its own
        // share. Every territory is guaranteed 2 supply nodes and a home 4
        // (the node-quota rule), so a standard territory pays 20 + 2x26 = 72,
        // exactly the old flat base, and a home pays 124. Nothing got poorer;
        // stocked ground got visibly richer.
        private const float BareSuppliesPerMinute = 20f;

        /// <summary>
        /// Supplies a supply slot pays at Gatherer's Hut LEVEL 1. An EMPTY
        /// slot pays nothing at all.
        ///
        /// AREA USED TO BE THE ECONOMY (superseded 2026-09-08). A supply node
        /// paid 26/min for merely being inside your border, built on or not,
        /// so the optimal play was to claim as much ground as possible and
        /// develop none of it — and a match ended with everyone holding wide,
        /// shallow empires and no reason to invest in any one of them.
        /// Ground is now worth what you have BUILT on it.
        /// </summary>
        private const float SuppliesPerHutPerMinute = 50f;

        /// <summary>
        /// Every level doubles what a slot, the base, and the whole territory
        /// pay — so a slot runs 0 / 50 / 100 / 200 and a Hall multiplies the
        /// territory by 1 / 2 / 4.
        ///
        /// Doubling rather than a gentler curve is the point: two levels of
        /// investment must beat a second territory, or "go wide" stays the
        /// only strategy and the choice is not a choice.
        /// </summary>
        private const int LevelDoubling = 2;

        /// <summary>Supplies per forest inside a held territory.</summary>
        private const float SuppliesPerForestPerMinute = 60f;

        /// <summary>
        /// What a Sawyer does to its territory's forest output. The Sawyer earns
        /// nothing itself -- it is a multiplier on the forests already there,
        /// which is what makes a FORESTED territory worth taking rather than
        /// just worth holding.
        /// </summary>
        private const float SawyerMultiplier = 2f;

        /// <summary>One Sawyer per territory counts. A second would stack a
        /// pure multiplier with no counterplay, and the interesting decision is
        /// WHICH forested territory to invest in, not how many yards to pile
        /// into the best one.</summary>
        private const int MaxSawyersPerTerritory = 1;

        /// <summary>What one resource node pays its territory's owner, whether
        /// or not anything is built on it. Holding the ground is what pays; the
        /// node is the reason the ground is worth holding.</summary>
        // Raised against the lowered supply base above: a node-bearing
        // territory should be visibly worth more than an empty one, because
        // that difference is the whole reason to contest a particular region.
        //
        // IRON AND VEILSTONE DOUBLED (2026-08-30 directive, Regions.md §4):
        // armies were trained but rarely replaced fast enough to fight with —
        // the ore trickle was the bottleneck. Veilsteel keeps the base rate;
        // its scarcity is the design, not its rate. The doubled trickle also
        // drains NodeReserve twice as fast, which is intended pressure.
        private const float IronYieldPerMinute = 190f;
        private const float VeilstoneYieldPerMinute = 190f;
        private const float VeilsteelYieldPerMinute = 95f;

        /// <summary>Added per MINE LEVEL standing on a node. A fresh mine is
        /// level 1 (+25); upgrading it adds another 25 each time.</summary>
        private const float MineYieldPerMinutePerLevel = 25f;

        /// <summary>How close a Mine must be to a node to count as built ON it.
        /// Generous by a build cell: the mine is placed against the node, not
        /// concentric with it, and refusing to pay over a metre of slack would
        /// read as the building being broken.</summary>
        private const float MineToNodeRange = 12f;

        /// <summary>
        /// What a fresh node holds. At the 75/min base trickle this is roughly
        /// 53 minutes of undisturbed extraction, so a node is still paying at
        /// the end of a long match — but it is visibly poorer: about 53% yield
        /// by minute 25, sooner if an extraction building is drawing on it.
        ///
        /// "Very slowly" is the point. A node that empties inside a match would
        /// make the map a countdown; one that never empties makes the opening
        /// land grab the entire economy.
        /// </summary>
        private const float NodeReserveUnits = 4000f;

        /// <summary>
        /// Yield floor as a fraction of the node's fresh rate. A spent node
        /// keeps trickling rather than dying: a territory whose nodes hit zero
        /// would be worth holding for nothing at all, which turns the late game
        /// into a map of dead ground nobody contests.
        /// </summary>
        private const float DepletionFloor = 0.25f;

        // SimCadence-phased, NOT a raw float accumulator (2026-09-04, MP
        // harness catch #7 class): a raw `_timer -= dt` carries a
        // machine-dependent phase in from the pre-match frame-driven updates,
        // so periodic work lands on different ticks per lockstep peer.
        private SimCadence.Periodic _acc;

        /// <summary>
        /// Fractional carry per faction. The rates are per MINUTE and the tick
        /// is five seconds, so almost every payment has a remainder — dropped
        /// each tick it would silently shave the economy (75/min pays 6.25 a
        /// tick, and integer truncation would deliver 72/min). Carrying it
        /// makes the per-minute number the player is shown exactly what they
        /// receive over a minute.
        /// </summary>
        private readonly float[] _carrySupplies = new float[9];
        private readonly float[] _carryIron = new float[9];
        private readonly float[] _carryVeilstone = new float[9];
        private readonly float[] _carryVeilsteel = new float[9];

        protected override void OnCreate()
        {

        }

        protected override void OnUpdate()
        {
            if (!RegionMap.Ready) return;

            if (!_acc.Due(SystemAPI.Time.DeltaTime, TickInterval)) return;

            var em = EntityManager;
            TerritoryOwnership.Recompute(em);
            EnsureNodeReserves(em);

            float minutes = TickInterval / 60f;
            int count = RegionMap.Count;

            for (int t = 0; t < count; t++)
            {
                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner < 0 || owner >= _carrySupplies.Length) continue;  // Natural / Curse pays nobody

                // drainMinutes > 0: this is the PAYING call, so it also takes
                // what it pays out of the ground. The panel's read-only call
                // passes 0 — a player opening the Hall panel must not mine.
                var yield = ComputeYield(em, t, (Faction)owner, minutes);
                if (yield.IsEmpty) continue;

                FactionEconomy.Add(em, (Faction)owner, new Cost
                {
                    Supplies  = Draw(ref _carrySupplies[owner],  yield.Supplies  * minutes),
                    Iron      = Draw(ref _carryIron[owner],      yield.Iron      * minutes),
                    Veilstone = Draw(ref _carryVeilstone[owner], yield.Veilstone * minutes),
                    Veilsteel = Draw(ref _carryVeilsteel[owner], yield.Veilsteel * minutes),
                });
            }
        }

        /// <summary>Add this tick's fractional amount to the carry and hand back
        /// the whole units now payable, leaving the remainder for next tick.</summary>
        private static int Draw(ref float carry, float amount)
        {
            carry += amount;
            int whole = Mathf.FloorToInt(carry);
            carry -= whole;
            return whole;
        }

        // ── the one computation ──────────────────────────────────────────

        /// <summary>
        /// What territory <paramref name="territory"/> yields per minute for
        /// whoever holds it. Public because the Hall panel shows exactly this —
        /// see the class comment on why there is only one implementation.
        ///
        /// Counts from live entity state rather than a cache: territories are
        /// few, this runs on a 5 s tick and on panel refresh, and a cached count
        /// that missed a hut finishing would show the player a number their bank
        /// disagrees with.
        /// </summary>
        /// <param name="drainMinutes">Minutes of extraction to subtract from
        /// the nodes. 0 for a read-only query.</param>
        public static TerritoryYield ComputeYield(EntityManager em, int territory, Faction owner,
            float drainMinutes = 0f)
        {
            var y = new TerritoryYield();
            if (territory < 0 || !RegionMap.Ready) return y;

            // ── The slots, and what the WEAKEST one says about the base ──
            //
            // Each supply node is a slot: empty it pays nothing, and with a
            // Gatherer's Hut on it it pays 50 doubled per hut level (50 /
            // 100 / 200). The base then scales with the LEAST developed slot,
            // so a territory pays its bare-ground floor until every slot has
            // been raised — finishing a territory is what lifts it, and one
            // neglected slot holds the whole base back.
            //
            // Level 0 (any slot still empty, or a territory with no slots at
            // all) leaves the base exactly where it was, so freshly claimed
            // ground is never worth literally nothing.
            int minSlotLevel = int.MaxValue;
            int slotsSeen = 0;
            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<SupplyNodeTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
                using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
                q.Dispose();
                for (int i = 0; i < xfs.Length; i++)
                {
                    var np = xfs[i].Position;
                    if (RegionMap.RegionAt(np.x, np.z) != territory) continue;
                    slotsSeen++;
                    int lvl = HutLevelOn(em, np.x, np.z);
                    if (lvl < minSlotLevel) minSlotLevel = lvl;
                    if (lvl > 0)
                        y.Supplies += SuppliesPerHutPerMinute * Pow2(lvl - 1);
                }
            }
            if (slotsSeen == 0 || minSlotLevel == int.MaxValue) minSlotLevel = 0;
            y.Supplies += BareSuppliesPerMinute * Pow2(minSlotLevel);

            // Forests are scene markers, not entities.
            int forests = 0;
            var stands = MapMarkerRegistry.NatureRegions;
            for (int i = 0; i < stands.Count; i++)
            {
                var f = stands[i];
                if (f == null || f.Kind != NatureRegionMarker.NatureKind.Forest) continue;
                var p = f.WorldPosition;
                if (RegionMap.RegionAt(p.x, p.z) == territory) forests++;
            }
            if (forests > 0)
            {
                float forestPay = forests * SuppliesPerForestPerMinute;
                int sawyers = CountIn<SawyerTag>(em, territory);
                if (sawyers > 0)
                    forestPay *= Mathf.Pow(SawyerMultiplier,
                                           Mathf.Min(sawyers, MaxSawyersPerTerritory));
                y.Supplies += forestPay;
            }

            // Resource nodes, and whatever mines are standing on them. Survey
            // research scales the lot: it is the only remaining consumer of the
            // Guild survey ladder now that the hut's area model is gone.
            y.Iron      = NodeAndMineYield<IronMineTag>(em, territory, drainMinutes,
                              IronYieldPerMinute)
                          * SurveyMultiplier(owner, IronSurveyLadder);
            y.Veilstone = NodeAndMineYield<VeilstoneOutcroppingTag>(em, territory, drainMinutes,
                              VeilstoneYieldPerMinute)
                          * SurveyMultiplier(owner, VeilstoneSurveyLadder);
            y.Veilsteel = NodeAndMineYield<VeilsteelDepositTag>(em, territory, drainMinutes,
                              VeilsteelYieldPerMinute)
                          * SurveyMultiplier(owner, VeilstoneSurveyLadder);

            // ── The Hall doubles everything the territory earns ──────────
            //
            // A territory's income is its Hall's income, so the Hall standing
            // in it is the single biggest lever on the whole economy: L1 x1,
            // L2 x2, L3 x4, applied to supplies AND ore. Deepening one
            // holding is meant to beat spreading into another, and this is
            // the multiplier that makes that true.
            //
            // The Fortress carries HallTag too, so a capital scales its home
            // territory exactly as an expansion Hall scales its own.
            int hallLevel = BestHallLevelIn(em, territory);
            if (hallLevel > 1)
            {
                float m = Pow2(hallLevel - 1);
                y.Supplies  *= m;
                y.Iron      *= m;
                y.Veilstone *= m;
                y.Veilsteel *= m;
            }

            return y;
        }

        /// <summary>2^n for the small n this model uses (level 0-3).</summary>
        private static float Pow2(int n) => n <= 0 ? 1f : (1 << Mathf.Min(n, 16));

        /// <summary>
        /// The Gatherer's Hut level standing on this supply node, or 0 for an
        /// empty slot.
        ///
        /// Feraldis Raider Camps are converted huts that KEEP GathererHutTag
        /// (AgeUpSystem adds RaiderCampTag to the same entity), so they are
        /// excluded by hand — otherwise a Feraldis player would draw the slot's
        /// supplies on top of what its raiders steal.
        /// </summary>
        private static int HutLevelOn(EntityManager em, float x, float z)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int best = 0;
            float r2 = MineToNodeRange * MineToNodeRange;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (em.HasComponent<RaiderCampTag>(ents[i])) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz > r2) continue;
                // Built and never upgraded is level 1, not level 0.
                int lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? Mathf.Max(1, em.GetComponentData<BuildingUpgradeState>(ents[i]).Level)
                    : 1;
                if (lvl > best) best = lvl;
            }
            ents.Dispose();
            q.Dispose();
            return best;
        }

        /// <summary>The best Hall level standing in this territory, or 0 for a
        /// territory held on influence alone. Fortresses carry HallTag.</summary>
        private static int BestHallLevelIn(EntityManager em, int territory)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int best = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                if (RegionMap.RegionAt(p.x, p.z) != territory) continue;
                int lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? Mathf.Max(1, em.GetComponentData<BuildingUpgradeState>(ents[i]).Level)
                    : 1;
                if (lvl > best) best = lvl;
            }
            ents.Dispose();
            q.Dispose();
            return best;
        }

        // ── Survey ladders ──────────────────────────────────────────────
        // Ordered cheapest-first; each tier researched multiplies the trickle
        // once more. Read LIVE (no stamped state to get stale), and this is now
        // their ONLY consumer — the hut's area-income system used to read them
        // and was deleted with the area model.
        private static readonly string[] IronSurveyLadder =
            { "IronSurveying1", "IronSurveying2", "IronSurveying3" };
        private static readonly string[] VeilstoneSurveyLadder =
            { "VeilstoneSurvey1", "VeilstoneSurvey2", "VeilsteelSurvey" };

        /// <summary>Per-tier multiplier on a surveyed resource's yield.</summary>
        private const float SurveyTierMultiplier = 1.5f;

        /// <summary>Compound multiplier from however many tiers of a survey
        /// ladder this faction has finished. 1.0 with none, so an unresearched
        /// faction is paid exactly the authored rate.</summary>
        private static float SurveyMultiplier(Faction faction, string[] ladder)
        {
            var research = FactionResearchState.Instance;
            if (research == null) return 1f;
            float mult = 1f;
            for (int i = 0; i < ladder.Length; i++)
                if (research.HasResearched(faction, ladder[i])) mult *= SurveyTierMultiplier;
            return mult;
        }

        /// <summary>
        /// Per-minute output of every node of one kind in a territory: the
        /// node's own trickle plus 25 per level of any Mine built on it.
        /// </summary>
        private static float NodeAndMineYield<TNode>(EntityManager em, int territory,
            float drainMinutes, float nodeYieldPerMinute)
            where TNode : unmanaged, IComponentData
        {
            var nodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<TNode>(),
                ComponentType.ReadOnly<LocalTransform>());
            var nodes = nodeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

            float total = 0f;
            for (int i = 0; i < nodes.Length; i++)
            {
                var np = em.GetComponentData<LocalTransform>(nodes[i]).Position;
                if (RegionMap.RegionAt(np.x, np.z) != territory) continue;

                // Fresh rate: the node's own trickle plus every level of the
                // extraction building standing on it.
                float fresh = nodeYieldPerMinute
                            + ExtractorLevelsOn<TNode>(em, np.x, np.z)
                              * MineYieldPerMinutePerLevel;

                // Scaled by how much is left in the ground.
                float scale = 1f;
                if (em.HasComponent<NodeReserve>(nodes[i]))
                {
                    var res = em.GetComponentData<NodeReserve>(nodes[i]);
                    if (res.Initial > 0f)
                        scale = Mathf.Max(DepletionFloor, res.Remaining / res.Initial);

                    if (drainMinutes > 0f && res.Remaining > 0f)
                    {
                        // Take out exactly what is being paid. Extraction
                        // buildings therefore consume the node faster than the
                        // bare trickle does — upgrading is a choice to spend it
                        // sooner, which is the whole tension.
                        res.Remaining = Mathf.Max(0f,
                            res.Remaining - fresh * scale * drainMinutes);
                        em.SetComponentData(nodes[i], res);
                    }
                }

                total += fresh * scale;
            }
            nodes.Dispose();
            nodeQuery.Dispose();
            return total;
        }

        /// <summary>
        /// Give every territory node a reserve if it has not got one.
        ///
        /// Done here rather than in the three node factories so a node spawned
        /// by any path — authored marker, fallback, scenario fixture — is
        /// covered by construction. Structural changes are applied AFTER the
        /// scan, never inside it, or the entity array being read is invalidated
        /// half way through.
        /// </summary>
        private static void EnsureNodeReserves(EntityManager em)
        {
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LocalTransform>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<IronMineTag>(),
                    ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                    ComponentType.ReadOnly<VeilsteelDepositTag>(),
                },
                None = new[] { ComponentType.ReadOnly<NodeReserve>() },
            });
            var missing = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < missing.Length; i++)
                em.AddComponentData(missing[i], new NodeReserve
                {
                    Remaining = NodeReserveUnits,
                    Initial = NodeReserveUnits,
                });
            missing.Dispose();
            q.Dispose();
        }

        /// <summary>
        /// The extractor that belongs on this node kind. One building per
        /// resource: a Gatherer's Hut pays supplies, a Mine iron, a Veilstone
        /// Mine veilstone, a Smelter veilsteel.
        ///
        /// This used to be one generic Mine counted for ALL THREE ore kinds, so
        /// a single building raised anywhere near a cluster boosted iron,
        /// veilstone and veilsteel at once and there was no decision about what
        /// to invest in. Veilsteel in particular had no building of its own at
        /// all, which is why it accumulated untouched.
        /// </summary>
        private static ComponentType ExtractorTagFor<TNode>()
            where TNode : unmanaged, IComponentData
        {
            if (typeof(TNode) == typeof(IronMineTag))
                return ComponentType.ReadOnly<MineTag>();
            if (typeof(TNode) == typeof(VeilstoneOutcroppingTag))
                return ComponentType.ReadOnly<VeilstoneMineTag>();
            if (typeof(TNode) == typeof(VeilsteelDepositTag))
                return ComponentType.ReadOnly<SmelterTag>();
            return ComponentType.ReadOnly<MineTag>();
        }

        /// <summary>Total extractor levels standing on the node at this
        /// position, counting only the building that belongs on it.</summary>
        private static int ExtractorLevelsOn<TNode>(EntityManager em, float x, float z)
            where TNode : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ExtractorTagFor<TNode>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int levels = 0;
            float r2 = MineToNodeRange * MineToNodeRange;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz > r2) continue;
                levels += em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? Mathf.Max(1, em.GetComponentData<BuildingUpgradeState>(ents[i]).Level)
                    : 1;
            }
            ents.Dispose();
            q.Dispose();
            return levels;
        }

        /// <summary>Total Mine levels standing on the node at this position.
        /// Levels, not mines: a mine is level 1 when raised and each upgrade
        /// adds another 25/min, which is what "mines can be upgraded" buys.</summary>
        private static int MineLevelsOn(EntityManager em, float x, float z)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<MineTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int levels = 0;
            float r2 = MineToNodeRange * MineToNodeRange;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz > r2) continue;
                // A building with no upgrade state has been built and not yet
                // upgraded, which is level 1 — not level 0.
                levels += em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? Mathf.Max(1, em.GetComponentData<BuildingUpgradeState>(ents[i]).Level)
                    : 1;
            }
            ents.Dispose();
            q.Dispose();
            return levels;
        }

        private static int CountIn<T>(EntityManager em, int territory)
            where T : unmanaged, IComponentData
            => CountIn<T, UnderConstruction>(em, territory);

        /// <summary>Completed <typeparamref name="T"/> buildings standing in a
        /// territory, skipping anything also carrying
        /// <typeparamref name="TExclude"/>.</summary>
        private static int CountIn<T, TExclude>(EntityManager em, int territory)
            where T : unmanaged, IComponentData
            where TExclude : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (em.HasComponent<TExclude>(ents[i])) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                if (RegionMap.RegionAt(p.x, p.z) == territory) n++;
            }
            ents.Dispose();
            q.Dispose();
            return n;
        }
    }
}
