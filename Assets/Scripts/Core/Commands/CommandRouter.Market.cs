// CommandRouter.Market.cs
// The Market's rate setting (docs/Design/Age_1_Alanthor.md § The Market,
// Unification decision 55 (d)). What the Market buys changes what the faction
// spends and earns every second, so the setting is a lockstep command:
// LockstepCommandType.SetMarketRate (Market in EntityNetworkId, iron per
// minute in TargetEntityId, veilstone per minute in SecondaryTargetId). The
// executor, run on every peer, is TheWaningBorder.Entities.Market.SetRates,
// which clamps both rates to the Market SO's range and step.

using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Entities;
using Unity.Entities;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// Set how much iron and how much veilstone <paramref name="market"/>
        /// buys a minute. Both rates are absolute (the panel sends the whole
        /// setting each click), so a repeated or reordered click never drifts.
        /// </summary>
        public static void IssueSetMarketRate(EntityManager em, Entity market,
            int ironPerMinute, int veilstonePerMinute,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (market == Entity.Null || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            if (IsBlockedByNotControllable(em, market, source)) return;
            ironPerMinute = Market.ClampRate(ironPerMinute);
            veilstonePerMinute = Market.ClampRate(veilstonePerMinute);

            if (ShouldQueueForLockstep(source))
            {
                int marketId = GetNetworkId(em, market);
                if (marketId <= 0)
                {
                    if (!MayExecuteLocally(em, market, "SetMarketRate")) return;
                    Market.SetRates(em, market, ironPerMinute, veilstonePerMinute);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SetMarketRate,
                    EntityNetworkId = marketId,
                    TargetEntityId = ironPerMinute,
                    SecondaryTargetId = veilstonePerMinute,
                });
            }
            else
            {
                Market.SetRates(em, market, ironPerMinute, veilstonePerMinute);
            }
        }

        /// <summary>Executor — runs on every peer.</summary>
        public static void SetMarketRateDirect(EntityManager em, Entity market, int ironPerMinute, int veilstonePerMinute)
            => Market.SetRates(em, market, ironPerMinute, veilstonePerMinute);

        /// <summary>
        /// Set the Market's surplus mode (decision 59): the supplies it keeps,
        /// and whether everything above them is turned into iron every second.
        /// Absolute, like the rates. LockstepCommandType.SetMarketSurplus
        /// (Market in EntityNetworkId, reserve in TargetEntityId, 1/0 in
        /// SecondaryTargetId).
        /// </summary>
        public static void IssueSetMarketSurplus(EntityManager em, Entity market,
            int reserveSupplies, bool autoIron,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (market == Entity.Null || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            if (IsBlockedByNotControllable(em, market, source)) return;
            reserveSupplies = Market.ClampReserve(reserveSupplies);

            if (ShouldQueueForLockstep(source))
            {
                int marketId = GetNetworkId(em, market);
                if (marketId <= 0)
                {
                    if (!MayExecuteLocally(em, market, "SetMarketSurplus")) return;
                    Market.SetSurplus(em, market, reserveSupplies, autoIron);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SetMarketSurplus,
                    EntityNetworkId = marketId,
                    TargetEntityId = reserveSupplies,
                    SecondaryTargetId = autoIron ? 1 : 0,
                });
            }
            else
            {
                Market.SetSurplus(em, market, reserveSupplies, autoIron);
            }
        }

        /// <summary>Executor — runs on every peer.</summary>
        public static void SetMarketSurplusDirect(EntityManager em, Entity market, int reserveSupplies, bool autoIron)
            => Market.SetSurplus(em, market, reserveSupplies, autoIron);
    }
}
