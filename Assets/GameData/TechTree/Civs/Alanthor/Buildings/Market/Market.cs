// Market.cs
// THE MARKET (Alanthor, Age 1 — docs/Design/Age_1_Alanthor.md § The Market,
// Unification decision 67, 2026-11-16). One per player (Market.asset
// maxPerFaction, BuildingFactory.AtFactionCap). VEILSTONE IS THE MONEY:
// supplies and iron are bought with veilstone and sold for veilstone, one LOT
// a trade (the MarketTrade lockstep command, executed by Trade below).
//
// THE PRICES ARE SHARED BY EVERY PLAYER (MarketPrices, one entity a match).
// Each lot bought raises that resource's price, each lot sold lowers it, by
// marketPriceStepPermille; a sale pays marketSellPercent of the price, so
// buying and selling back always loses. Prices stay between a floor and a
// ceiling (percent of the starting price) and drift back to the starting
// price (MarketSystem). A faction that dumps supplies makes supplies cheap
// for everybody else.
//
// Every number is on Market.asset. Its model is the retired Trading
// Outpost's art (Market/Art/), level 1.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Market
    {
        /// <summary>The Market's building id (Market.asset).</summary>
        public const string BuildingId = "Alanthor_Market";
        /// <summary>After the Farm (512); its SO says the same.</summary>
        public const int PresentationID = 513;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(MarketTag));
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(MarketTag));
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return EntityBuild.Done(entity);
        }

        private static BuildingDef Def => TechCatalog.Building(BuildingId);

        // ── Goods, lots and the SO's numbers ─────────────────────────────

        /// <summary>What the Market trades for veilstone. The values travel in
        /// the MarketTrade command; do not renumber.</summary>
        public const int GoodSupplies = 0, GoodIron = 1;

        /// <summary>Most lots one command trades (bounds the executor's loop
        /// and the panel's Ctrl-click). Engine-side, not tuning.</summary>
        public const int MaxLotsPerTrade = 100;

        /// <summary>Units of the resource in one lot (Market.asset).</summary>
        public static int LotSize(int good)
            => math.max(1, good == GoodIron ? Def.marketIronLot : Def.marketSuppliesLot);

        /// <summary>The starting price of one lot, hundredths of a veilstone
        /// (Market.asset). Prices drift back to it.</summary>
        public static int BasePrice(int good)
            => math.max(100, good == GoodIron ? Def.marketIronLotPrice : Def.marketSuppliesLotPrice);

        public static int FloorPrice(int good)
            => math.max(100, (int)((long)BasePrice(good) * math.clamp(Def.marketPriceFloorPercent, 1, 100) / 100));
        public static int CeilingPrice(int good)
            => (int)((long)BasePrice(good) * math.max(100, Def.marketPriceCeilingPercent) / 100);

        /// <summary>Seconds between two drift steps (Market.asset).</summary>
        public static float DriftSeconds => math.max(1f, Def.marketDriftSeconds);

        /// <summary>Veilstone one lot costs to BUY at <paramref name="price"/>
        /// (hundredths), rounded up.</summary>
        public static int BuyCost(int price) => math.max(1, (price + 99) / 100);

        /// <summary>Veilstone one lot PAYS when sold at <paramref name="price"/>:
        /// marketSellPercent of it, rounded down.</summary>
        public static int SellPay(int price)
            => (int)((long)price * math.clamp(Def.marketSellPercent, 0, 100) / 10000);

        /// <summary>The price after one lot is bought (up) or sold (down).</summary>
        public static int StepPrice(int good, int price, bool bought)
        {
            int step = math.max(1, (int)((long)price * math.max(1, Def.marketPriceStepPermille) / 1000));
            return math.clamp(bought ? price + step : price - step, FloorPrice(good), CeilingPrice(good));
        }

        /// <summary>Veilstone <paramref name="lots"/> lots cost (buy) or pay
        /// (sell) from <paramref name="price"/>, each lot at the price the one
        /// before it left. For the panel and the AI; the executor does the
        /// same arithmetic.</summary>
        public static int Quote(int good, int price, int lots, bool buy)
        {
            int total = 0;
            for (int i = 0; i < lots; i++)
            {
                total += buy ? BuyCost(price) : SellPay(price);
                price = StepPrice(good, price, buy);
            }
            return total;
        }

        // ── The shared prices ────────────────────────────────────────────

        static readonly ComponentType[] QT_Prices = { ComponentType.ReadWrite<MarketPrices>() };
        static CachedEntityQuery QC_Prices;

        /// <summary>The shared prices as they stand (the starting prices while
        /// nobody has traded yet). Read-only: creates nothing.</summary>
        public static MarketPrices Prices(EntityManager em)
        {
            var q = QC_Prices.Get(em, QT_Prices);
            if (!q.IsEmptyIgnoreFilter)
            {
                using var arr = q.ToComponentDataArray<MarketPrices>(Unity.Collections.Allocator.Temp);
                if (arr.Length > 0) return arr[0];
            }
            return new MarketPrices { SuppliesPrice = BasePrice(GoodSupplies), IronPrice = BasePrice(GoodIron) };
        }

        /// <summary>Price of <paramref name="good"/> in <paramref name="p"/>.</summary>
        public static int PriceOf(in MarketPrices p, int good) => good == GoodIron ? p.IronPrice : p.SuppliesPrice;

        /// <summary>A price as a percentage of its starting price.</summary>
        public static int PricePercent(int good, int price) => (int)((long)price * 100 / BasePrice(good));

        private static Entity PricesEntity(EntityManager em)
        {
            var q = QC_Prices.Get(em, QT_Prices);
            if (!q.IsEmptyIgnoreFilter)
            {
                using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
                if (ents.Length > 0) return ents[0];
            }
            var e = em.CreateEntity(typeof(MarketPrices));
            em.SetComponentData(e, new MarketPrices
            {
                SuppliesPrice = BasePrice(GoodSupplies),
                IronPrice = BasePrice(GoodIron),
            });
            return e;
        }

        /// <summary>
        /// THE TRADE (the executor; every peer runs it off the wire,
        /// CommandRouter.MarketTradeDirect). The faction owning the finished
        /// <paramref name="market"/> buys (<paramref name="buy"/>) or sells up
        /// to <paramref name="lots"/> lots of <paramref name="good"/>, one at a
        /// time, each moving the SHARED price for everybody. It stops at the
        /// first lot the bank cannot pay for (veilstone on a buy, the resource
        /// on a sale) or that would pay nothing. Returns the lots traded.
        /// </summary>
        public static int Trade(EntityManager em, Entity market, int good, int lots, bool buy)
        {
            if (market == Entity.Null || !em.Exists(market) || !em.HasComponent<MarketTag>(market)) return 0;
            if (em.HasComponent<UnderConstruction>(market) || !em.HasComponent<FactionTag>(market)) return 0;
            if (em.HasComponent<Health>(market) && em.GetComponentData<Health>(market).Value <= 0) return 0;
            if (good != GoodSupplies && good != GoodIron) return 0;
            lots = math.clamp(lots, 0, MaxLotsPerTrade);
            if (lots == 0) return 0;

            var faction = em.GetComponentData<FactionTag>(market).Value;
            var holder = PricesEntity(em);
            var prices = em.GetComponentData<MarketPrices>(holder);
            int price = PriceOf(prices, good);
            int lot = LotSize(good);
            Cost goods = good == GoodIron ? Cost.Of(iron: lot) : Cost.Of(supplies: lot);

            int done = 0;
            for (; done < lots; done++)
            {
                if (buy)
                {
                    if (!FactionEconomy.Spend(em, faction, Cost.Of(veilstone: BuyCost(price)), SpendCategory.Trade)) break;
                    FactionEconomy.Add(em, faction, goods, IncomeSource.Market);
                }
                else
                {
                    int pay = SellPay(price);
                    if (pay <= 0) break;
                    if (!FactionEconomy.Spend(em, faction, goods, SpendCategory.Trade)) break;
                    FactionEconomy.Add(em, faction, Cost.Of(veilstone: pay), IncomeSource.Market);
                }
                price = StepPrice(good, price, buy);
            }

            if (done > 0)
            {
                if (good == GoodIron) prices.IronPrice = price; else prices.SuppliesPrice = price;
                em.SetComponentData(holder, prices);
            }
            return done;
        }

        /// <summary>One drift step (MarketSystem): each price closes
        /// marketDriftPermille of its gap to the starting price.</summary>
        public static void Drift(EntityManager em)
        {
            var q = QC_Prices.Get(em, QT_Prices);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            if (ents.Length == 0) return;
            var p = em.GetComponentData<MarketPrices>(ents[0]);
            int s = DriftOne(GoodSupplies, p.SuppliesPrice), i = DriftOne(GoodIron, p.IronPrice);
            if (s == p.SuppliesPrice && i == p.IronPrice) return;
            p.SuppliesPrice = s; p.IronPrice = i;
            em.SetComponentData(ents[0], p);
        }

        private static int DriftOne(int good, int price)
        {
            int gap = BasePrice(good) - price;
            if (gap == 0) return price;
            int move = math.max(1, (int)((long)math.abs(gap) * math.max(1, Def.marketDriftPermille) / 1000));
            move = math.min(move, math.abs(gap));
            return price + (gap > 0 ? move : -move);
        }

        /// <summary>The faction's Market (finished or not; the lowest entity
        /// index if there were ever two), or Null.</summary>
        public static Entity Of(EntityManager em, Faction faction)
        {
            var q = QC_Markets.Get(em, QT_Markets);
            if (q.IsEmptyIgnoreFilter) return Entity.Null;
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && (best == Entity.Null || ents[i].Index < best.Index))
                    best = ents[i];
            return best;
        }

        static readonly ComponentType[] QT_Markets =
        {
            ComponentType.ReadOnly<MarketTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Markets;
    }
}
