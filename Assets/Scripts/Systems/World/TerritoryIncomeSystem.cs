// TerritoryIncomeSystem.cs
// Territory is the economy.
//
// docs/Design/Regions.md §4: income comes from the ground you hold, not from
// workers gathering. Per owned territory:
//
//   * 50/min of supplies from a FORTRESS standing in it
//   * every resource node is a SLOT (supply, iron, veilstone): EMPTY it pays
//     10/min of its resource; with its extractor on it (Gatherer's Hut,
//     Mine, Veilstone Mine) it pays 50/min, doubling per level (50/100/200).
//     An ALANTHOR faction's huts and iron Mines run 70/100/200 instead.
//   * a cursed or depleted veilstone outcrop pays nothing, and there is no
//     veilsteel node at all (docs/Design/Veilstone_Economy.md, 2026-10-01)
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
        // EVERY SECOND (2026-10-01): every income source — territory and
        // Trading Outposts alike — lands once a second, so the bank moves
        // smoothly and the per-minute readouts can be watched happening.
        private const float TickInterval = 1f;

        /// <summary>
        /// Supplies a FORTRESS pays its territory (docs/Design/Veilstone_Economy.md
        /// §5, 2026-10-01). Ground without one pays only its slots and forests.
        /// </summary>
        private const float FortressSuppliesPerMinute = 50f;

        /// <summary>
        /// What an EMPTY slot pays — a supply, iron or uncursed veilstone node
        /// in held ground with no extractor on it. Small on purpose: holding a
        /// node is worth something, building on it is worth five times more.
        /// </summary>
        private const float EmptySlotPerMinute = 10f;

        /// <summary>
        /// What a slot pays with its extractor on it at LEVEL 1 (Gatherer's
        /// Hut, Mine, Veilstone Mine). Each level doubles it: 50 / 100 / 200.
        /// </summary>
        private const float ExtractorSlotPerMinute = 50f;

        /// <summary>
        /// An ALANTHOR faction's Gatherer's Huts and iron Mines, by level
        /// (Veilstone_Economy.md §5): the culture that will not mine veilstone
        /// works its supply and iron slots harder.
        /// </summary>
        private static readonly float[] AlanthorSlotLadder = { 70f, 100f, 200f };

        /// <summary>
        /// MINES PAY DOUBLE (2026-10-01): a Mine or Veilstone Mine on its slot
        /// pays twice the hut ladder — 100 / 200 / 400, Alanthor's iron Mines
        /// 140 / 200 / 400. An empty ore slot still pays 10.
        /// </summary>
        private const float MineYieldMultiplier = 2f;

        /// <summary>
        /// EVERY IRON SOURCE PAYS 20 % MORE (2026-10-02, Veilstone_Economy.md
        /// §6): empty iron slots and Mines alike. Iron was the resource every
        /// faction starved on — 8-AI batches ended on ~10k unspent supplies
        /// and under 60 iron each.
        /// </summary>
        private const float IronYieldMultiplier = 1.2f;

        /// <summary>
        /// The MINE's own research ladder (2026-10-02): Deep Shafts makes every
        /// iron slot a Mine works pay +50 %, Rich Seams +100 % (they do not
        /// stack — Rich Seams replaces Deep Shafts). Mine-worked slots only:
        /// an empty slot is not mined.
        /// </summary>
        private const string DeepShaftsTech = "DeepShafts";
        private const string RichSeamsTech = "RichSeams";
        private const float DeepShaftsMultiplier = 1.5f;
        private const float RichSeamsMultiplier = 2f;

        /// <summary>The Mine-tech multiplier this faction's Mines earn on iron.</summary>
        private static float MineTechMultiplier(Faction faction)
        {
            var research = FactionResearchState.Instance;
            if (research == null) return 1f;
            if (research.HasResearched(faction, RichSeamsTech)) return RichSeamsMultiplier;
            if (research.HasResearched(faction, DeepShaftsTech)) return DeepShaftsMultiplier;
            return 1f;
        }

        /// <summary>Feraldis mine fast and burn the outcrop out
        /// (Veilstone_Economy.md §3.2). Every unit paid is drawn from the
        /// reserve, so the multiplier is also how much faster they deplete it.</summary>
        private const float FeraldisVeilstoneMultiplier = 1.5f;

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

        // ── Per-building readout (the income overlay) ───────────────────

        /// <summary>
        /// What ONE building adds to its faction's income, per minute, for the
        /// world-space income overlay. Negative entries are what it spends (a
        /// Trading Outpost's inputs). Presentation only: it shares the display
        /// census window, so never call it from the simulation.
        ///
        /// It is the territory tick's own arithmetic split by building (a
        /// slot's full built rate, the Fortress's supplies) plus the Trading
        /// Outpost's cycle, so the overlay names where each resource comes
        /// from. Empty slots and forests belong to the TERRITORY overlay
        /// (ComputeYieldForDisplay).
        /// </summary>
        public static TerritoryYield BuildingYieldForDisplay(EntityManager em, Entity building)
        {
            var y = new TerritoryYield();
            if (!RegionMap.Ready || !em.Exists(building)) return y;
            if (em.HasComponent<UnderConstruction>(building)) return y;
            if (!em.HasComponent<FactionTag>(building) || !em.HasComponent<LocalTransform>(building)) return y;

            var owner = em.GetComponentData<FactionTag>(building).Value;
            var p = em.GetComponentData<LocalTransform>(building).Position;

            // Paid straight to the faction, wherever it stands.
            if (em.HasComponent<TradingOutpostTag>(building))
            {
                if (!TheWaningBorder.Entities.TradingOutpost.HasLiveOutcrop(em, p.x, p.z)) return y;
                var recipe = TheWaningBorder.Entities.TradingOutpost.RecipeOf(em, building);
                if (!TheWaningBorder.Systems.Economy.TradingOutpostSystem.IsUnlocked(owner, recipe))
                    recipe = TradeRecipe.BuyVeilstone;
                TheWaningBorder.Systems.Economy.TradingOutpostSystem.PerMinute(owner, recipe,
                    out var spend, out var earn);
                y.Supplies = earn.Supplies - spend.Supplies;
                y.Iron = earn.Iron - spend.Iron;
                y.Veilstone = earn.Veilstone - spend.Veilstone;
                y.Veilsteel = earn.Veilsteel - spend.Veilsteel;
                return y;
            }
            // Territory-paid: only while its faction holds the ground.
            int territory = RegionMap.RegionAt(p.x, p.z);
            if (territory < 0 || TerritoryOwnership.OwnerOf(territory) != (int)owner) return y;

            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (_displayCensus == null || !ReferenceEquals(_displayCensusWorld, em.World)
                || now - _displayCensusAt > DisplayCensusSeconds || now < _displayCensusAt)
            {
                if (_displayCensus == null) _displayCensus = new Census();
                _displayCensus.Build(em);
                _displayCensusAt = now;
                _displayCensusWorld = em.World;
            }
            var c = _displayCensus;
            float hall = HallMultiplier(c, territory);
            int level = LevelOf(em, building);
            float r2 = MineToNodeRange * MineToNodeRange;

            bool alanthor = CultureConfig.GetCompletedCulture(em, owner) == Cultures.Alanthor;
            if (em.HasComponent<FortressTag>(building))
            {
                y.Supplies += FortressSuppliesPerMinute * hall;
            }
            else if (em.HasComponent<GathererHutTag>(building) && !em.HasComponent<RaiderCampTag>(building))
            {
                if (NearAny(em, QC_Supply.Get(em, QT_Supply), p, r2))
                    y.Supplies += SlotRate(level, alanthor) * hall;
            }
            else if (em.HasComponent<MineTag>(building))
            {
                y.Iron += NodeLevelYield(em, c.Ore[0], p, r2, level, alanthor) * hall
                          * SurveyMultiplier(owner, IronSurveyLadder)
                          * IronYieldMultiplier
                          * (level > 0 ? MineTechMultiplier(owner) : 1f);
            }
            else if (em.HasComponent<VeilstoneMineTag>(building))
            {
                float mult = CultureConfig.GetCompletedCulture(em, owner) == Cultures.Feraldis
                    ? FeraldisVeilstoneMultiplier : 1f;
                var o = c.Ore[1];
                for (int i = 0; i < o.Node.Count; i++)
                {
                    var np = em.GetComponentData<LocalTransform>(o.Node[i]).Position;
                    float dx = np.x - p.x, dz = np.z - p.z;
                    if (dx * dx + dz * dz > r2) continue;
                    if (TheWaningBorder.Systems.Economy.VeilstoneNodeStateSystem.KindOf(em, o.Node[i])
                        != VeilstoneNodeKind.Inactive) continue;
                    if (em.HasComponent<NodeReserve>(o.Node[i])
                        && em.GetComponentData<NodeReserve>(o.Node[i]).Remaining <= 0f) continue;
                    y.Veilstone += MineRate(level, false) * mult * hall
                                   * SurveyMultiplier(owner, VeilstoneSurveyLadder);
                }
            }
            return y;
        }

        /// <summary>The Hall multiplier on a territory: x1 / x2 / x4 by level.</summary>
        private static float HallMultiplier(Census c, int territory)
        {
            int hallLevel = 0;
            for (int i = 0; i < c.HallRegion.Count; i++)
                if (c.HallRegion[i] == territory && c.HallLevel[i] > hallLevel)
                    hallLevel = c.HallLevel[i];
            return hallLevel > 1 ? Pow2(hallLevel - 1) : 1f;
        }

        /// <summary>One extractor's slot rate on the ore nodes it stands on,
        /// scaled by what is left in each (the same scale the tick pays).</summary>
        private static float NodeLevelYield(EntityManager em, OreCensus o, Unity.Mathematics.float3 p,
            float r2, int level, bool alanthor)
        {
            float total = 0f;
            for (int i = 0; i < o.Node.Count; i++)
            {
                var node = o.Node[i];
                var np = em.GetComponentData<LocalTransform>(node).Position;
                float dx = np.x - p.x, dz = np.z - p.z;
                if (dx * dx + dz * dz > r2) continue;
                float scale = 1f;
                if (em.HasComponent<NodeReserve>(node))
                {
                    var res = em.GetComponentData<NodeReserve>(node);
                    if (res.Initial > 0f) scale = Mathf.Max(DepletionFloor, res.Remaining / res.Initial);
                }
                total += MineRate(level, alanthor) * scale;
            }
            return total;
        }

        private static bool NearAny(EntityManager em, EntityQuery q, Unity.Mathematics.float3 p, float r2)
        {
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                float dx = xfs[i].Position.x - p.x, dz = xfs[i].Position.z - p.z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        // ── Faction net rate (the resource bar's red numbers) ────────────

        static readonly ComponentType[] QT_Outposts =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Outposts;

        private static TerritoryYield _netCache;
        private static Faction _netCacheFaction;
        private static double _netCacheAt = double.NegativeInfinity;

        /// <summary>
        /// The faction's NET income per minute across every source: each
        /// territory it holds plus every Trading Outpost's spend and earn. A
        /// negative line means its trades consume more of that resource than
        /// it produces — the resource bar turns that number red. Presentation
        /// only (cached for half a second of real time).
        /// </summary>
        public static TerritoryYield FactionNetForDisplay(EntityManager em, Faction faction)
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (_netCacheFaction == faction && now - _netCacheAt <= DisplayCensusSeconds && now >= _netCacheAt)
                return _netCache;

            var net = new TerritoryYield();
            if (RegionMap.Ready)
            {
                for (int t = 0; t < RegionMap.Count; t++)
                {
                    if (TerritoryOwnership.OwnerOf(t) != (int)faction) continue;
                    var y = ComputeYieldForDisplay(em, t, faction);
                    net.Supplies += y.Supplies; net.Iron += y.Iron;
                    net.Veilstone += y.Veilstone; net.Veilsteel += y.Veilsteel;
                }
            }
            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var y = BuildingYieldForDisplay(em, ents[i]);
                net.Supplies += y.Supplies; net.Iron += y.Iron;
                net.Veilstone += y.Veilstone; net.Veilsteel += y.Veilsteel;
            }

            _netCache = net;
            _netCacheFaction = faction;
            _netCacheAt = now;
            return net;
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

            bool alanthor = CultureConfig.GetCompletedCulture(em, owner) == Cultures.Alanthor;

            // The Fortress's own supplies.
            y.Supplies += FortressSuppliesPerMinute * Census.CountAt(c.FortressRegion, territory);

            // Supply slots: 10 empty, the hut's ladder with one on it.
            for (int i = 0; i < c.SupplyRegion.Count; i++)
                if (c.SupplyRegion[i] == territory)
                    y.Supplies += SlotRate(c.SupplyHutLevel[i], alanthor);

            // Ore slots. Survey research scales each line. There is no
            // veilsteel line — veilsteel is MADE (the Trading Outpost), never
            // mined.
            y.Iron = OreSlotYield(em, c.Ore[0], territory, drainMinutes, alanthor, IronYieldMultiplier, false,
                                  MineTechMultiplier(owner))
                     * SurveyMultiplier(owner, IronSurveyLadder);
            float veilMult = CultureConfig.GetCompletedCulture(em, owner) == Cultures.Feraldis
                ? FeraldisVeilstoneMultiplier : 1f;
            y.Veilstone = OreSlotYield(em, c.Ore[1], territory, drainMinutes, false, veilMult, true)
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
        static readonly ComponentType[] QT_Iron = { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Veilstone = { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Mine = { ComponentType.ReadOnly<MineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_VeilstoneMine = { ComponentType.ReadOnly<VeilstoneMineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_MissingIron = { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<NodeReserve>() };
        static readonly ComponentType[] QT_MissingVeilstone = { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<NodeReserve>() };
        static CachedEntityQuery QC_Supply, QC_Hut, QC_Hall, QC_Iron, QC_Veilstone,
                                 QC_Mine, QC_VeilstoneMine,
                                 QC_MissingIron, QC_MissingVeilstone;

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
            public readonly List<int> HallRegion = new List<int>();
            public readonly List<int> HallLevel = new List<int>();
            public readonly List<int> FortressRegion = new List<int>();
            public readonly OreCensus[] Ore = { new OreCensus(), new OreCensus() };
            private readonly List<Vector3> _built = new List<Vector3>();   // x, z, level

            public static int CountAt(List<int> regions, int territory)
            {
                int n = 0;
                for (int i = 0; i < regions.Count; i++) if (regions[i] == territory) n++;
                return n;
            }

            public void Build(EntityManager em)
            {
                SupplyRegion.Clear(); SupplyHutLevel.Clear();
                HallRegion.Clear(); HallLevel.Clear(); FortressRegion.Clear();

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

                {
                    var q = QC_Hall.Get(em, QT_Hall);
                    using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
                    for (int i = 0; i < ents.Length; i++)
                    {
                        if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                        var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                        HallRegion.Add(RegionOfStatic(ents[i], p.x, p.z));
                        HallLevel.Add(LevelOf(em, ents[i]));
                        if (em.HasComponent<FortressTag>(ents[i]))
                            FortressRegion.Add(RegionOfStatic(ents[i], p.x, p.z));
                    }
                }

                BuildOre(em, Ore[0], QC_Iron.Get(em, QT_Iron), QC_Mine.Get(em, QT_Mine));
                BuildOre(em, Ore[1], QC_Veilstone.Get(em, QT_Veilstone), QC_VeilstoneMine.Get(em, QT_VeilstoneMine));
            }

            /// <summary>One building per resource: a Mine on iron, a Veilstone
            /// Mine on veilstone. (This used to be one
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
        /// What one slot pays per minute: 10 empty, else its extractor's
        /// ladder — 50 / 100 / 200, or Alanthor's 70 / 100 / 200.
        /// </summary>
        private static float MineRate(int extractorLevel, bool alanthor)
            => extractorLevel <= 0 ? EmptySlotPerMinute
                                   : SlotRate(extractorLevel, alanthor) * MineYieldMultiplier;

        private static float SlotRate(int extractorLevel, bool alanthor)
        {
            if (extractorLevel <= 0) return EmptySlotPerMinute;
            if (alanthor)
                return AlanthorSlotLadder[Mathf.Clamp(extractorLevel, 1, AlanthorSlotLadder.Length) - 1];
            return ExtractorSlotPerMinute * Pow2(extractorLevel - 1);
        }

        /// <summary>
        /// Per-minute output of every ore slot of one kind in a territory.
        ///
        /// IRON keeps the depletion curve: yield scales by what is left, down
        /// to <see cref="DepletionFloor"/>. VEILSTONE is fast and finite: an
        /// outcrop pays only while Inactive, at its full rate until its reserve
        /// is gone, then it is Depleted and pays nothing (Veilstone_Economy.md
        /// §2). Every unit paid is drawn from the reserve on the paying call.
        /// </summary>
        private static float OreSlotYield(EntityManager em, OreCensus o, int territory,
            float drainMinutes, bool alanthor, float multiplier, bool veilstone,
            float extractorMultiplier = 1f)
        {
            float total = 0f;
            for (int i = 0; i < o.Node.Count; i++)
            {
                if (o.Region[i] != territory) continue;
                var node = o.Node[i];
                if (veilstone && TheWaningBorder.Systems.Economy.VeilstoneNodeStateSystem.KindOf(em, node)
                                 != VeilstoneNodeKind.Inactive) continue;

                float rate = MineRate(o.ExtractorLevels[i], alanthor) * multiplier;
                // A slot with its extractor on it earns that extractor's
                // research (the Mine ladder); an empty slot does not.
                if (o.ExtractorLevels[i] > 0) rate *= extractorMultiplier;
                if (em.HasComponent<NodeReserve>(node))
                {
                    var res = em.GetComponentData<NodeReserve>(node);
                    if (veilstone)
                    {
                        if (res.Remaining <= 0f) continue;
                        if (drainMinutes > 0f)
                        {
                            float take = Mathf.Min(res.Remaining, rate * drainMinutes);
                            res.Remaining -= take;
                            em.SetComponentData(node, res);
                            rate = take / drainMinutes;
                        }
                    }
                    else
                    {
                        if (res.Initial > 0f)
                            rate *= Mathf.Max(DepletionFloor, res.Remaining / res.Initial);
                        if (drainMinutes > 0f && res.Remaining > 0f)
                        {
                            res.Remaining = Mathf.Max(0f, res.Remaining - rate * drainMinutes);
                            em.SetComponentData(node, res);
                        }
                    }
                }
                total += rate;
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
