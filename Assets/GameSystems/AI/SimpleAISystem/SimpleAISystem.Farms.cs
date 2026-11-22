// SimpleAISystem.Farms.cs
// FARMS AND THE MARKET FOR THE ALANTHOR AI (Unification decision 55,
// docs/Design/Age_1_Alanthor.md § The Farm / § The Market). One decision
// every SimpleAISystemConfig.farmThinkInterval seconds, from the economy
// think (SimpleAISystem.cs, case 5):
//
//   FARMS   — keep farmSteadyCount Farms; while banked supplies are below
//             farmShortSupplies (supply income is short), build on up to the
//             Farm SO's cap. One Farm at a time: no new one while one is still
//             a plan or a site.
//   SITE    — (decision 57) the site search scores farmSiteCandidates legal
//             sites by their crop ring's free share (FarmRing) and takes the
//             best, refusing any below farmMinFreePermille; every other
//             building keeps off the faction's Farm rings in its normal
//             passes (farmRingKeepClear) — SimpleAISystem.Building.cs,
//             TryFindBuildPosition. Farm LEVELS are AIBuildingUpgradeSystem's
//             economy ladder.
//   STAFF   — send idle Workers into Farms with a free slot (farmStaffPerThink
//             a decision), never the last farmFreeWorkerReserve idle Workers,
//             which stay free to build. A staffed Worker drops out of the
//             worker count, so the worker rule retrains the build crew; past
//             that, one extra Worker is queued for the empty slots while the
//             bank holds farmTrainWorkerMinSupplies.
//   MARKET  - once aged up and the bank holds marketBuildMinSupplies, raise
//             the one Market. Each decision reads the SHARED prices and
//             trades lots for veilstone (decision 67): it sells a glut or
//             a price spike, buys what it is short of, and buys what
//             somebody else dumped cheap. See TickMarket.
//
// Every number is in SimpleAISystem.asset; the Market's prices and steps are
// the Market SO's. Orders go through CommandRouter (CommandSource.AI) like
// every AI order. Log categories FARM and MARKET.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private static readonly Dictionary<Faction, float> _nextFarmThink = new Dictionary<Faction, float>();
        private static int _farmThinkEpoch = -1;

        private static readonly ComponentType[] QT_FarmWorkers =
        {
            ComponentType.ReadOnly<WorkerTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_FarmWorkers;

        private static readonly ComponentType[] QT_Farms =
        {
            ComponentType.ReadOnly<FarmTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Farms;

        /// <summary>The Farm and Market decision (see the file header).</summary>
        private void TickFarmsAndMarket(EntityManager em, Faction faction, float now)
        {
            if (_farmThinkEpoch != SimCadence.Epoch)
            {
                _farmThinkEpoch = SimCadence.Epoch;
                _nextFarmThink.Clear();
            }
            if (_nextFarmThink.TryGetValue(faction, out float next) && now < next) return;
            _nextFarmThink[faction] = now + math.max(1f, Cfg.farmThinkInterval);

            if (CultureConfig.GetCompletedCulture(em, faction) != Cultures.Alanthor) return;
            if (!FactionEconomy.TryGetResources(em, faction, out var bank)) return;

            TickFarms(em, faction, bank);
            TickMarket(em, faction, bank);
        }

        // ── Farms ────────────────────────────────────────────────────────

        private void TickFarms(EntityManager em, Faction faction, in FactionResources bank)
        {
            int cap = TechCatalog.Building(Farm.BuildingId).maxPerFaction;
            int owned = CountFactionBuildings<FarmTag>(em, faction);   // plans and sites included
            int finished = CountFinished<FarmTag>(em, faction);
            // The tier's extractor share sets how many Farms it keeps (2026-11-11):
            // a lower tier's weaker economy, never a flat ceiling.
            // ...to the power 1.3 (iteration 9: Beginner, Easy and Normal all kept
            // ~3 Farms and their armies bunched; 1.5 starved Hard on Fiendstone):
            // Expert 6, Hard 5, Normal 3, Easy 2, Beginner 2.
            // ...now farmWantExponent on SimpleAISystem.asset (2026-11-14, Game_AI.md
            // § 6ac: 1.0 -- Expert 6, Hard 5, Normal 4, Easy 3 -- the Farm is the
            // supply engine that lets a faction fill its population by minute 30).
            int want = math.clamp((int)math.ceil(cap * math.pow(math.saturate(ProfileOf(faction).ExtractorShare),
                math.max(0.1f, Cfg.farmWantExponent))), 1, cap);

            // Two Farms may rise at once (iteration 3: one at a time left the
            // Farms full only by minute 20-25).
            // THE NEXT FARM IS SAVED FOR (2026-11-11, overnight tuning): every
            // supply went into units the moment it came in, so Expert Defensive's
            // Farms were refused "bank short" 30 times and the sixth stood only at
            // minute 21. While under the target, its price is a strict goal.
            if (owned >= want) AIPivotalReserve.Clear(faction, FarmReserveKey);
            if (owned < want && owned - finished < 2)
            {
                var farmCost = TheWaningBorder.Data.BuildCosts.For(em, faction, Farm.BuildingId);
                if (!AIBudget.CanAffordLeavingSavings(em, faction, farmCost))
                    AIPivotalReserve.Set(faction, FarmReserveKey, farmCost, strict: true);
                if (TryBuildBuildingWithReason(em, faction, Farm.BuildingId, out string why))
                {
                    AIPivotalReserve.Clear(faction, FarmReserveKey);
                    AILogger.Log(faction, "FARM", $"placed Farm {owned + 1}/{want} (supplies {bank.Supplies})");
                }
                else if (AILogger.Enabled)
                    AILogger.Log(faction, "FARM", $"Farm {owned + 1}/{want} refused: {why}");
            }

            if (finished == 0) return;
            int vacancies = StaffFarms(em, faction, out int sent);
            if (sent > 0)
                AILogger.Log(faction, "FARM", $"sent {sent} Worker(s) into Farms, {vacancies} slot(s) still empty");

            // Empty slots and no spare Worker: queue more, while supplies allow.
            // OUTSIDE THE ECONOMY WALLET (2026-11-14, Game_AI.md § 6ac): a Farm
            // Worker repays its price in about a minute, faster than anything
            // else the AI buys, yet on Moatwatch (0.0.85) the 300-supply gate and
            // the wallet share left Farm slots empty for six minutes and more.
            int queuedWorkers = CountQueuedByPredicate(em, faction, isWorker: true);
            int queueWant = math.min(vacancies, math.max(1, Cfg.farmWorkersQueuedMax));
            if (vacancies > 0
                && AICommon.CountIdleWorkers(em, faction) <= Cfg.farmFreeWorkerReserve
                && queuedWorkers < queueWant)
            {
                int trained = 0;
                string noWorker = null;
                for (int q = queuedWorkers; q < queueWant; q++)
                {
                    if (FactionEconomy.TryGetResources(em, faction, out var now2)
                        && now2.Supplies < Cfg.farmTrainWorkerMinSupplies)
                    { noWorker = $"supplies under {Cfg.farmTrainWorkerMinSupplies}"; break; }
                    if (!TryTrainUnitWithReason(em, faction, "Worker", out string whyNot)) { noWorker = whyNot; break; }
                    trained++;
                }
                if (trained > 0)
                    AILogger.Log(faction, "FARM", $"training {trained} Worker(s) for {vacancies} empty Farm slot(s)");
                else if (noWorker != null && queuedWorkers == 0 && AILogger.Enabled
                         && (!_farmNoWorkerLogAt.TryGetValue(faction, out float nl) || (float)SimClock.Now >= nl))
                {
                    _farmNoWorkerLogAt[faction] = (float)SimClock.Now + 60f;
                    AILogger.Log(faction, "FARM", $"no Worker trained for {vacancies} empty Farm slot(s): {noWorker}");
                }
            }
        }

        private readonly Dictionary<Faction, float> _farmNoWorkerLogAt = new Dictionary<Faction, float>();

        private const string FarmReserveKey = "Farm";
        private const string MarketReserveKey = "Market";

        /// <summary>Empty slots in the faction's finished Farms (the worker rule
        /// lets this many more Workers be trained).</summary>
        private static int FarmVacancies(EntityManager em, Faction faction)
        {
            var fq = QC_Farms.Get(em, QT_Farms);
            if (fq.IsEmptyIgnoreFilter) return 0;
            using var farms = fq.ToEntityArray(Allocator.Temp);
            using var ffacs = fq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int free = 0;
            for (int i = 0; i < farms.Length; i++)
            {
                if (ffacs[i].Value != faction || em.HasComponent<UnderConstruction>(farms[i])) continue;
                free += math.max(0, WallGarrison.SlotCount(em, farms[i]) - WallGarrison.OccupantCount(em, farms[i]));
            }
            return free;
        }

        /// <summary>Sends idle Workers (beyond the reserve) into the nearest
        /// Farms with room. Returns the empty slots left; <paramref name="sent"/>
        /// the Workers ordered in.</summary>
        private static int StaffFarms(EntityManager em, Faction faction, out int sent)
        {
            sent = 0;
            var fq = QC_Farms.Get(em, QT_Farms);
            using var farms = fq.ToEntityArray(Allocator.Temp);
            using var ffacs = fq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var fxfs = fq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Free slots per finished Farm, less the Workers already walking there.
            var room = new List<(Entity Farm, float3 Pos, int Free)>();
            for (int i = 0; i < farms.Length; i++)
            {
                if (ffacs[i].Value != faction || em.HasComponent<UnderConstruction>(farms[i])) continue;
                int free = WallGarrison.SlotCount(em, farms[i]) - WallGarrison.OccupantCount(em, farms[i]);
                if (free > 0) room.Add((farms[i], fxfs[i].Position, free));
            }
            if (room.Count == 0) return 0;

            var wq = QC_FarmWorkers.Get(em, QT_FarmWorkers);
            using var ws = wq.ToEntityArray(Allocator.Temp);
            using var wfacs = wq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var wxfs = wq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var idle = new List<(Entity W, float3 Pos)>();
            for (int i = 0; i < ws.Length; i++)
            {
                if (wfacs[i].Value != faction) continue;
                var w = ws[i];
                if (em.HasComponent<GarrisonApproach>(w) && WallGarrison.IsApproaching(em, w))
                {
                    var b = em.GetComponentData<GarrisonApproach>(w).Building;
                    for (int r = 0; r < room.Count; r++)
                        if (room[r].Farm == b) room[r] = (room[r].Farm, room[r].Pos, room[r].Free - 1);
                    continue;
                }
                if (AICommon.IsCommittedWorker(em, w)) continue;
                if (!Farm.IsFarmhand(em, w)) continue;
                idle.Add((w, wxfs[i].Position));
            }

            int vacancies = 0;
            for (int r = 0; r < room.Count; r++) vacancies += math.max(0, room[r].Free);

            int spare = idle.Count - math.max(0, Cfg.farmFreeWorkerReserve);
            int budget = math.min(spare, math.max(0, Cfg.farmStaffPerThink));
            // Lockstep: a fixed order, entity index.
            idle.Sort((a, b) => a.W.Index.CompareTo(b.W.Index));
            for (int i = 0; i < idle.Count && sent < budget && vacancies > 0; i++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int r = 0; r < room.Count; r++)
                {
                    if (room[r].Free <= 0) continue;
                    float d = math.distancesq(room[r].Pos.xz, idle[i].Pos.xz);
                    if (d < bestD) { bestD = d; best = r; }
                }
                if (best < 0) break;
                if (!WallGarrison.CanGarrison(em, idle[i].W, room[best].Farm)) continue;
                CommandRouter.IssueGarrisonWall(em, idle[i].W, room[best].Farm, CommandSource.AI);
                room[best] = (room[best].Farm, room[best].Pos, room[best].Free - 1);
                vacancies--;
                sent++;
            }
            return vacancies;
        }

        // ── The Market ───────────────────────────────────────────────────

        private void TickMarket(EntityManager em, Faction faction, in FactionResources bank)
        {
            var market = Market.Of(em, faction);
            if (market == Entity.Null)
            {
                // THE MARKET COMES WITH THE GLUT (decision 84): it waited for a
                // marketBuildMinSupplies bank, which a faction short of supplies
                // never has — the batch-17 AIs placed it at minutes 11-18 while
                // thousands of iron and veilstone sat unused. Iron or veilstone
                // piling up now places it too, saved for like a Farm.
                if (CountFactionBuildings<MarketTag>(em, faction) > 0)
                {
                    AIPivotalReserve.Clear(faction, MarketReserveKey);
                    return;   // still a plan
                }
                bool glut = (Cfg.marketBuildVeilstone > 0 && bank.Veilstone >= Cfg.marketBuildVeilstone)
                         || (Cfg.marketBuildIron > 0 && bank.Iron >= Cfg.marketBuildIron);
                if (bank.Supplies < Cfg.marketBuildMinSupplies && !glut) return;
                var marketCost = TheWaningBorder.Data.BuildCosts.For(em, faction, Market.BuildingId);
                if (glut && !AIBudget.CanAffordLeavingSavings(em, faction, marketCost))
                    AIPivotalReserve.Set(faction, MarketReserveKey, marketCost, strict: true);
                if (TryBuildBuildingWithReason(em, faction, Market.BuildingId, out string why))
                {
                    AIPivotalReserve.Clear(faction, MarketReserveKey);
                    AILogger.Log(faction, "MARKET", $"placed the Market (supplies {bank.Supplies}, iron {bank.Iron}, veilstone {bank.Veilstone})");
                }
                else if (AILogger.Enabled)
                    AILogger.Log(faction, "MARKET", $"Market refused: {why}");
                return;
            }
            if (em.HasComponent<UnderConstruction>(market)) return;

            // THE AI READS THE SHARED PRICES EVERY THINK (decision 67). Veilstone
            // is the money, and the prices are everybody's: what another faction
            // dumps is cheap for this one, what another faction buys up is worth
            // selling into. In order, a think may:
            //   1. SELL A GLUT   — a resource banked above its glut level, at
            //                      whatever the price is (decision 81: the floor
            //                      does not stop a seller, it sells more lots).
            //   2. SELL THE SPIKE — a resource somebody drove above
            //                      marketDearPercent, down to twice its short level.
            //   3. BUY A NEED    — a resource below its short level (or one the
            //                      army's last purchases were refused for), with
            //                      the veilstone above marketVeilstoneShort, unless
            //                      its price is above marketBuyMaxPercent. With no
            //                      veilstone to spare it first sells the OTHER
            //                      resource down to that one's short level.
            //   4. BUY THE DIP   — a resource somebody dumped below
            //                      marketCheapPercent, with spare veilstone, up to
            //                      half its glut level.
            // At most marketLotsPerThink lots a resource a think, so one AI moves
            // a price in steps the others can answer. The 15 Nov match is the
            // case this is for: three Expert AIs held 17,000-30,000 iron and
            // under 2,000 supplies all game, with no way to turn one into the
            // other.
            var prices = Market.Prices(em);
            int maxLots = math.max(1, Cfg.marketLotsPerThink);
            int veilSpare = bank.Veilstone - Cfg.marketVeilstoneShort;
            if (AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone)) veilSpare = 0;

            // (good, held, short level, glut level, army waiting on it)
            // WHAT IS BEING SAVED IS NOT A GLUT (decision 106, 2026-11-20). The
            // Shardroot's enshrine price is more supplies than the glut level,
            // so a bearer's bank was sold back down to the glut level every
            // think (289 sales in one match) and the price never formed: the
            // bearer waited at home for 55 minutes. Every pending savings goal
            // is added to the level a sale keeps.
            var saving = AIPivotalReserve.PendingTotal(faction);
            var sup = (Good: Market.GoodSupplies, Held: bank.Supplies, Short: Cfg.marketSuppliesShort,
                Glut: Cfg.marketSuppliesGlut + saving.Supplies, Starved: AIBudget.IsMilitaryShort(faction, AIBudget.ResSupplies));
            var iro = (Good: Market.GoodIron, Held: bank.Iron, Short: Cfg.marketIronShort,
                Glut: Cfg.marketIronGlut + saving.Iron, Starved: AIBudget.IsMilitaryShort(faction, AIBudget.ResIron));

            for (int pass = 0; pass < 2; pass++)
            {
                var g = pass == 0 ? sup : iro;       // the resource decided
                var o = pass == 0 ? iro : sup;       // the other one
                int price = Market.PriceOf(prices, g.Good);
                int pct = Market.PricePercent(g.Good, price);
                int lot = Market.LotSize(g.Good);
                // A NEED IS A BLOCKED ARMY (2026-11-16, batch 2): "bank below the
                // short level" is the permanent state of an AI that spends what it
                // earns, so every AI sold iron at the floor and bought supplies at
                // twice their price 1,622 times in six matches and starved its
                // army of iron. Now only a resource the army's last purchases were
                // refused for, and only while the bank is still below its level.
                bool need = g.Starved && g.Held < g.Short;

                // 1-2. Sell: a glut, or into a spike. Never what the army waits on.
                // A GLUT SELLS AT ANY PRICE (decision 81, 2026-11-19, developer:
                // "The market being at the floor price should not stop the
                // players from selling. They just have to sell more of it.").
                // The AI refused below 60 %; with every AI selling into the
                // same prices they sat below it, so each AI sold six lots every
                // few minutes while its bank reached the 100,000 cap and its
                // army stood idle for veilstone (0.0.90 batch). A player has no
                // such gate. The floor (Market.asset) is the worst price; past
                // it a sale only pays less, it is never refused.
                if (!need)
                {
                    int keep = g.Held > g.Glut ? g.Glut
                             : pct >= Cfg.marketDearPercent ? g.Short * 2
                             : int.MaxValue;
                    int lots = keep == int.MaxValue ? 0 : math.min(maxLots, (g.Held - keep) / lot);
                    if (lots > 0)
                    {
                        CommandRouter.IssueMarketTrade(em, market, g.Good, lots, false, CommandSource.AI);
                        AILogger.Log(faction, "MARKET",
                            $"sell {lots} lot(s) of {GoodName(g.Good)} at {pct}% " +
                            $"({(g.Held > g.Glut ? "glut" : "spike")}; bank {bank.Supplies}s {bank.Iron}i {bank.Veilstone}v)");
                        continue;
                    }
                }

                // 3-4. Buy: a need, or a dip.
                // (to HALF the glut level: buying up to it and selling above it would
                // trade the same lots back and forth at a loss)
                bool dip = !need && pct <= Cfg.marketCheapPercent && g.Held < g.Glut / 2;
                if (!need && !dip) continue;
                if (need && pct > Cfg.marketBuyMaxPercent) continue;
                int want = need ? maxLots : (g.Glut / 2 - g.Held) / lot;
                want = math.clamp(want, 1, maxLots);
                int affordable = 0;
                for (int n = 1; n <= want; n++)
                {
                    if (Market.Quote(g.Good, price, n, true) > veilSpare) break;
                    affordable = n;
                }
                if (affordable > 0)
                {
                    veilSpare -= Market.Quote(g.Good, price, affordable, true);
                    CommandRouter.IssueMarketTrade(em, market, g.Good, affordable, true, CommandSource.AI);
                    AILogger.Log(faction, "MARKET",
                        $"buy {affordable} lot(s) of {GoodName(g.Good)} at {pct}% " +
                        $"({(need ? "short" : "dip")}; bank {bank.Supplies}s {bank.Iron}i {bank.Veilstone}v)");
                    continue;
                }

                // A need and no veilstone: raise it from the other resource.
                // Down to its own short level (decision 105: it kept twice
                // that, and an AI short of supplies sat on 1,400 iron it never
                // sold); at any price (decision 81).
                if (!need || o.Starved) continue;
                int oPrice = Market.PriceOf(prices, o.Good);
                int oLots = math.min(maxLots, (o.Held - o.Short) / Market.LotSize(o.Good));
                if (oLots <= 0 || Market.SellPay(oPrice) <= 0) continue;
                CommandRouter.IssueMarketTrade(em, market, o.Good, oLots, false, CommandSource.AI);
                AILogger.Log(faction, "MARKET",
                    $"sell {oLots} lot(s) of {GoodName(o.Good)} at {Market.PricePercent(o.Good, oPrice)}% " +
                    $"to buy {GoodName(g.Good)} (bank {bank.Supplies}s {bank.Iron}i {bank.Veilstone}v)");
            }
        }

        private static string GoodName(int good) => good == Market.GoodIron ? "iron" : "supplies";

    }
}
