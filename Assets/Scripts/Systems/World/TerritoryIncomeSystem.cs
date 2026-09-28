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

        /// <summary>
        /// The carry is MATCH state on a system object that outlives matches
        /// (TeardownAfterMatch wipes entities, not systems). Left alone it
        /// walks into the next match with whatever fraction the previous one
        /// ended on — and that fraction is different on every machine.
        ///
        /// DESYNC 2026-09-10, build 0.0.22, tick 150 (the FIRST income tick):
        /// the client had played a 15-minute skirmish earlier in the same
        /// process, so its Blue/Red carries were non-zero while the freshly
        /// launched host's were 0. Same yield on both peers, 15.83 iron a
        /// tick — Draw() floored to 15 on the host and 16 on the client, and
        /// the Bank checksum forked with every entity line still identical.
        /// Green/Yellow, absent from the earlier match, agreed. Hashing the
        /// bank made it visible on the tick; this makes it not happen.
        /// </summary>
        private int _epoch = -1;

        protected override void OnCreate()
        {

        }

        protected override void OnUpdate()
        {
            if (!RegionMap.Ready) return;

            // Per-match reset, same contract as EliminationSystem: the first
            // update after SimCadence.BeginMatch() starts every carry at 0 on
            // every peer.
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                System.Array.Clear(_carrySupplies,  0, _carrySupplies.Length);
                System.Array.Clear(_carryIron,      0, _carryIron.Length);
                System.Array.Clear(_carryVeilstone, 0, _carryVeilstone.Length);
                System.Array.Clear(_carryVeilsteel, 0, _carryVeilsteel.Length);
            }

            if (!_acc.Due(SystemAPI.Time.DeltaTime, TickInterval)) return;

            var em = EntityManager;
            TerritoryOwnership.Recompute(em);
            EnsureNodeReserves(em);

            float minutes = TickInterval / 60f;
            int count = RegionMap.Count;
            // One snapshot of everything that pays, for every territory: the
            // per-territory queries this replaced re-read every node, hut,
            // mine and Hall once PER TERRITORY (and once per node for huts
            // and mines) through a fresh CreateEntityQuery each time.
            var census = _tickCensus;
            census.Build(em);

            for (int t = 0; t < count; t++)
            {
                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner < 0 || owner >= _carrySupplies.Length) continue;  // Natural / Curse pays nobody

                // drainMinutes > 0: this is the PAYING call, so it also takes
                // what it pays out of the ground. The panel's read-only call
                // passes 0 — a player opening the Hall panel must not mine.
                var yield = Yield(em, census, t, (Faction)owner, minutes);
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
        /// Counts from live entity state rather than a cache: a cached count
        /// that missed a hut finishing would show the player a number their
        /// bank disagrees with. (The tick builds ONE census for all
        /// territories; this entry point builds its own.)
        /// </summary>
        /// <param name="drainMinutes">Minutes of extraction to subtract from
        /// the nodes. 0 for a read-only query.</param>
        public static TerritoryYield ComputeYield(EntityManager em, int territory, Faction owner,
            float drainMinutes = 0f)
        {
            if (territory < 0 || !RegionMap.Ready) return new TerritoryYield();
            var census = new Census();
            census.Build(em);
            return Yield(em, census, territory, owner, drainMinutes);
        }

        private static Census _displayCensus;
        private static double _displayCensusAt = double.NegativeInfinity;
        private static Unity.Entities.World _displayCensusWorld;

        /// <summary>Seconds (real time) a panel readout may reuse one census.</summary>
        private const double DisplayCensusSeconds = 0.5;

        /// <summary>
        /// <see cref="ComputeYield"/> for a READ-ONLY readout (the Hall panel,
        /// the tooltip), which polls at 10 Hz. One census is shared by every
        /// such call for half a second of real time, so a panel no longer
        /// re-scans the world per refresh. Presentation only: never call this
        /// from the simulation — its reuse window is wall-clock.
        /// </summary>
        public static TerritoryYield ComputeYieldForDisplay(EntityManager em, int territory, Faction owner)
        {
            if (territory < 0 || !RegionMap.Ready) return new TerritoryYield();
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (_displayCensus == null || !ReferenceEquals(_displayCensusWorld, em.World)
                || now - _displayCensusAt > DisplayCensusSeconds || now < _displayCensusAt)
            {
                if (_displayCensus == null) _displayCensus = new Census();
                _displayCensus.Build(em);
                _displayCensusAt = now;
                _displayCensusWorld = em.World;
            }
            return Yield(em, _displayCensus, territory, owner, 0f);
        }

        private readonly Census _tickCensus = new Census();

        /// <summary>The yield computation proper, over a census. Every sum
        /// runs over the census lists in their query (chunk) order, filtered
        /// to the territory — the same entities in the same order the
        /// per-territory scans used, so the float totals are bit-identical.</summary>
        private static TerritoryYield Yield(EntityManager em, Census c, int territory, Faction owner,
            float drainMinutes)
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
            for (int i = 0; i < c.SupplyRegion.Count; i++)
            {
                if (c.SupplyRegion[i] != territory) continue;
                slotsSeen++;
                int lvl = c.SupplyHutLevel[i];
                if (lvl < minSlotLevel) minSlotLevel = lvl;
                if (lvl > 0)
                    y.Supplies += SuppliesPerHutPerMinute * Pow2(lvl - 1);
            }
            if (slotsSeen == 0 || minSlotLevel == int.MaxValue) minSlotLevel = 0;
            y.Supplies += BareSuppliesPerMinute * Pow2(minSlotLevel);

            // Forests are scene markers, not entities.
            int forests = Census.CountAt(c.ForestRegion, territory);
            if (forests > 0)
            {
                float forestPay = forests * SuppliesPerForestPerMinute;
                int sawyers = Census.CountAt(c.SawyerRegion, territory);
                if (sawyers > 0)
                    forestPay *= Mathf.Pow(SawyerMultiplier,
                                           Mathf.Min(sawyers, MaxSawyersPerTerritory));
                y.Supplies += forestPay;
            }

            // Resource nodes, and whatever mines are standing on them. Survey
            // research scales the lot: it is the only remaining consumer of the
            // Guild survey ladder now that the hut's area model is gone.
            y.Iron      = NodeAndMineYield(em, c.Ore[0], territory, drainMinutes,
                              IronYieldPerMinute)
                          * SurveyMultiplier(owner, IronSurveyLadder);
            y.Veilstone = NodeAndMineYield(em, c.Ore[1], territory, drainMinutes,
                              VeilstoneYieldPerMinute)
                          * SurveyMultiplier(owner, VeilstoneSurveyLadder);
            y.Veilsteel = NodeAndMineYield(em, c.Ore[2], territory, drainMinutes,
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
            int hallLevel = 0;
            for (int i = 0; i < c.HallRegion.Count; i++)
                if (c.HallRegion[i] == territory && c.HallLevel[i] > hallLevel)
                    hallLevel = c.HallLevel[i];
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

        // ── census ──────────────────────────────────────────────────────
        static readonly ComponentType[] QT_Supply = { ComponentType.ReadOnly<SupplyNodeTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Hut = { ComponentType.ReadOnly<GathererHutTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Hall = { ComponentType.ReadOnly<HallTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Sawyer = { ComponentType.ReadOnly<SawyerTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Iron = { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Veilstone = { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Veilsteel = { ComponentType.ReadOnly<VeilsteelDepositTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Mine = { ComponentType.ReadOnly<MineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_VeilstoneMine = { ComponentType.ReadOnly<VeilstoneMineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Smelter = { ComponentType.ReadOnly<SmelterTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_MissingIron = { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<NodeReserve>() };
        static readonly ComponentType[] QT_MissingVeilstone = { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<NodeReserve>() };
        static readonly ComponentType[] QT_MissingVeilsteel = { ComponentType.ReadOnly<VeilsteelDepositTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<NodeReserve>() };
        static CachedEntityQuery QC_Supply, QC_Hut, QC_Hall, QC_Sawyer, QC_Iron, QC_Veilstone, QC_Veilsteel,
                                 QC_Mine, QC_VeilstoneMine, QC_Smelter,
                                 QC_MissingIron, QC_MissingVeilstone, QC_MissingVeilsteel;

        // Nodes and buildings never move, so each one's territory is asked
        // once per partition (RegionMap.Version), not once per tick per
        // territory. The stored position guards the assumption: a moved
        // entity is simply re-asked. RegionAt is a pure function of position,
        // so the cache cannot change an answer.
        private struct CachedRegion { public float X, Z; public int Region; }
        private static readonly Dictionary<Entity, CachedRegion> _regionCache = new Dictionary<Entity, CachedRegion>();
        private static int _regionCacheVersion = int.MinValue;

        private static int RegionOfStatic(Entity e, float x, float z)
        {
            if (_regionCacheVersion != RegionMap.Version)
            {
                _regionCache.Clear();
                _regionCacheVersion = RegionMap.Version;
            }
            if (_regionCache.TryGetValue(e, out var c) && c.X == x && c.Z == z) return c.Region;
            // Dead entities are never evicted one by one; drop the lot when
            // it has plainly outgrown the live set.
            if (_regionCache.Count > 8192) _regionCache.Clear();
            int r = RegionMap.RegionAt(x, z);
            _regionCache[e] = new CachedRegion { X = x, Z = z, Region = r };
            return r;
        }

        /// <summary>One ore kind: its nodes (query order) and the fresh
        /// extractor levels standing on each.</summary>
        private sealed class OreCensus
        {
            public readonly List<Entity> Node = new List<Entity>();
            public readonly List<int> Region = new List<int>();
            public readonly List<int> ExtractorLevels = new List<int>();
            public void Clear() { Node.Clear(); Region.Clear(); ExtractorLevels.Clear(); }
        }

        /// <summary>Everything the yield reads, gathered once. Lists are in
        /// query order; see <see cref="Yield"/> for why that matters.</summary>
        private sealed class Census
        {
            public readonly List<int> SupplyRegion = new List<int>();
            public readonly List<int> SupplyHutLevel = new List<int>();
            public readonly List<int> ForestRegion = new List<int>();
            public readonly List<int> SawyerRegion = new List<int>();
            public readonly List<int> HallRegion = new List<int>();
            public readonly List<int> HallLevel = new List<int>();
            public readonly OreCensus[] Ore = { new OreCensus(), new OreCensus(), new OreCensus() };
            private readonly List<Vector3> _built = new List<Vector3>();   // x, z, level

            public static int CountAt(List<int> regions, int territory)
            {
                int n = 0;
                for (int i = 0; i < regions.Count; i++) if (regions[i] == territory) n++;
                return n;
            }

            public void Build(EntityManager em)
            {
                SupplyRegion.Clear(); SupplyHutLevel.Clear(); ForestRegion.Clear();
                SawyerRegion.Clear(); HallRegion.Clear(); HallLevel.Clear();

                // Gatherer's Huts, built, not Raider Camps (converted huts that
                // KEEP GathererHutTag — AgeUpSystem adds RaiderCampTag to the
                // same entity — so a Feraldis player does not draw the slot's
                // supplies on top of what its raiders steal).
                GatherBuilt(em, QC_Hut.Get(em, QT_Hut), true);
                float r2 = MineToNodeRange * MineToNodeRange;
                {
                    var q = QC_Supply.Get(em, QT_Supply);
                    using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
                    using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var np = xfs[i].Position;
                        SupplyRegion.Add(RegionOfStatic(ents[i], np.x, np.z));
                        // The best hut level on the slot (0 = empty).
                        int best = 0;
                        for (int h = 0; h < _built.Count; h++)
                        {
                            float dx = _built[h].x - np.x, dz = _built[h].y - np.z;
                            if (dx * dx + dz * dz > r2) continue;
                            int lvl = (int)_built[h].z;
                            if (lvl > best) best = lvl;
                        }
                        SupplyHutLevel.Add(best);
                    }
                }

                var stands = MapMarkerRegistry.NatureRegions;
                for (int i = 0; i < stands.Count; i++)
                {
                    var fm = stands[i];
                    if (fm == null || fm.Kind != NatureRegionMarker.NatureKind.Forest) continue;
                    var p = fm.WorldPosition;
                    ForestRegion.Add(RegionMap.RegionAt(p.x, p.z));
                }

                {
                    var q = QC_Sawyer.Get(em, QT_Sawyer);
                    using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                        var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                        SawyerRegion.Add(RegionOfStatic(ents[i], p.x, p.z));
                    }
                }

                {
                    var q = QC_Hall.Get(em, QT_Hall);
                    using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                        var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                        HallRegion.Add(RegionOfStatic(ents[i], p.x, p.z));
                        HallLevel.Add(LevelOf(em, ents[i]));
                    }
                }

                BuildOre(em, Ore[0], QC_Iron.Get(em, QT_Iron), QC_Mine.Get(em, QT_Mine));
                BuildOre(em, Ore[1], QC_Veilstone.Get(em, QT_Veilstone), QC_VeilstoneMine.Get(em, QT_VeilstoneMine));
                BuildOre(em, Ore[2], QC_Veilsteel.Get(em, QT_Veilsteel), QC_Smelter.Get(em, QT_Smelter));
            }

            /// <summary>One building per resource: a Mine on iron, a Veilstone
            /// Mine on veilstone, a Smelter on veilsteel. (This used to be one
            /// generic Mine counted for all three ore kinds, so a single
            /// building near a cluster boosted everything at once.) Levels are
            /// summed per node.</summary>
            private void BuildOre(EntityManager em, OreCensus o, EntityQuery nodeQ, EntityQuery extractorQ)
            {
                o.Clear();
                GatherBuilt(em, extractorQ, false);
                float r2 = MineToNodeRange * MineToNodeRange;
                using var ents = nodeQ.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var np = em.GetComponentData<LocalTransform>(ents[i]).Position;
                    o.Node.Add(ents[i]);
                    o.Region.Add(RegionOfStatic(ents[i], np.x, np.z));
                    int levels = 0;
                    for (int h = 0; h < _built.Count; h++)
                    {
                        float dx = _built[h].x - np.x, dz = _built[h].y - np.z;
                        if (dx * dx + dz * dz > r2) continue;
                        levels += (int)_built[h].z;
                    }
                    o.ExtractorLevels.Add(levels);
                }
            }

            /// <summary>Completed buildings of a query as (x, z, level).</summary>
            private void GatherBuilt(EntityManager em, EntityQuery q, bool excludeRaiderCamps)
            {
                _built.Clear();
                using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                    if (excludeRaiderCamps && em.HasComponent<RaiderCampTag>(ents[i])) continue;
                    var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                    _built.Add(new Vector3(p.x, p.z, LevelOf(em, ents[i])));
                }
            }
        }

        /// <summary>Built and never upgraded is level 1, not level 0.</summary>
        private static int LevelOf(EntityManager em, Entity e)
            => em.HasComponent<BuildingUpgradeState>(e)
                ? Mathf.Max(1, em.GetComponentData<BuildingUpgradeState>(e).Level)
                : 1;

        /// <summary>2^n for the small n this model uses (level 0-3).</summary>
        private static float Pow2(int n) => n <= 0 ? 1f : (1 << Mathf.Min(n, 16));

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
        /// node's own trickle plus 25 per level of the extractor built on it.
        /// </summary>
        private static float NodeAndMineYield(EntityManager em, OreCensus o, int territory,
            float drainMinutes, float nodeYieldPerMinute)
        {
            float total = 0f;
            for (int i = 0; i < o.Node.Count; i++)
            {
                if (o.Region[i] != territory) continue;
                var node = o.Node[i];

                // Fresh rate: the node's own trickle plus every level of the
                // extraction building standing on it.
                float fresh = nodeYieldPerMinute
                            + o.ExtractorLevels[i] * MineYieldPerMinutePerLevel;

                // Scaled by how much is left in the ground.
                float scale = 1f;
                if (em.HasComponent<NodeReserve>(node))
                {
                    var res = em.GetComponentData<NodeReserve>(node);
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
                        em.SetComponentData(node, res);
                    }
                }

                total += fresh * scale;
            }
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
            AddMissingReserves(em, QC_MissingIron.Get(em, QT_MissingIron));
            AddMissingReserves(em, QC_MissingVeilstone.Get(em, QT_MissingVeilstone));
            AddMissingReserves(em, QC_MissingVeilsteel.Get(em, QT_MissingVeilsteel));
        }

        private static void AddMissingReserves(EntityManager em, EntityQuery q)
        {
            if (q.IsEmptyIgnoreFilter) return;
            var missing = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < missing.Length; i++)
                em.AddComponentData(missing[i], new NodeReserve
                {
                    Remaining = NodeReserveUnits,
                    Initial = NodeReserveUnits,
                });
            missing.Dispose();
        }

    }
}
