// CommandRouter.Market.cs
// The Market's trade order (docs/Design/Age_1_Alanthor.md § The Market,
// Unification decision 67). A trade moves two banks' worth of numbers and the
// price EVERY faction pays, so it is a lockstep command:
// LockstepCommandType.MarketTrade (Market in EntityNetworkId; the good in
// TargetEntityId — Market.GoodSupplies / GoodIron, plus SellFlag for a sale;
// lots in SecondaryTargetId). The executor, run on every peer, is
// TheWaningBorder.Entities.Market.Trade.

using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Entities;
using Unity.Entities;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>Added to the good in TargetEntityId when the order is a sale.</summary>
        private const int MarketSellFlag = 2;

        /// <summary>
        /// Buy (<paramref name="buy"/>) or sell <paramref name="lots"/> lots
        /// of <paramref name="good"/> at <paramref name="market"/>, for
        /// veilstone, at the shared price as it stands when the order lands.
        /// </summary>
        public static void IssueMarketTrade(EntityManager em, Entity market, int good, int lots, bool buy,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (market == Entity.Null || !em.Exists(market) || !em.HasComponent<MarketTag>(market)) return;
            if (IsBlockedByNotControllable(em, market, source)) return;
            if (good != Market.GoodSupplies && good != Market.GoodIron) return;
            if (lots <= 0) return;
            if (lots > Market.MaxLotsPerTrade) lots = Market.MaxLotsPerTrade;

            if (ShouldQueueForLockstep(source))
            {
                int marketId = GetNetworkId(em, market);
                if (marketId <= 0)
                {
                    if (!MayExecuteLocally(em, market, "MarketTrade")) return;
                    Market.Trade(em, market, good, lots, buy);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.MarketTrade,
                    EntityNetworkId = marketId,
                    TargetEntityId = good + (buy ? 0 : MarketSellFlag),
                    SecondaryTargetId = lots,
                });
            }
            else
            {
                Market.Trade(em, market, good, lots, buy);
            }
        }

        /// <summary>Executor — runs on every peer. <paramref name="goodAndSide"/>
        /// is the command's TargetEntityId.</summary>
        public static void MarketTradeDirect(EntityManager em, Entity market, int goodAndSide, int lots)
        {
            bool buy = (goodAndSide & MarketSellFlag) == 0;
            Market.Trade(em, market, goodAndSide & ~MarketSellFlag, lots, buy);
        }
    }
}
