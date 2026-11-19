// ActionsPanelPrefabBinder.Market.cs
// THE MARKET ON THE STANDARD ACTIONS PANEL (decision 67, 2026-11-16;
// docs/Design/Age_1_Alanthor.md § The Market). Veilstone is the money: one
// column per resource it trades, the same plus / icon / minus shape decision
// 59 gave the panel.
//
//            col 0 Supplies            col 1 Iron
//   row 0    [+]  buy a lot            [+]  buy a lot
//   row 1    [supplies  price]         [iron  price]
//   row 2    [-]  sell a lot           [-]  sell a lot
//
// A click trades one lot (Shift: five, Ctrl: twenty) at the SHARED price,
// which every player's Market reads and moves: the badge under each resource
// is what one lot costs now, green while it is cheaper than its starting
// price and red while dearer. Tooltips give the lot size, what this click
// costs or pays, and the price against its starting value.
//
// Every click goes through CommandRouter (the MarketTrade lockstep command),
// so it lands on every peer at the same tick. The panel uses the builder's
// square 4 x 4 grid (large cells) like every building; the level-up takes the
// bottom-right cell (2026-11-18).
//
// Read-only on the simulation: MarketPrices, the Market SO and the bank.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>Lots one Shift-click trades.</summary>
        private const int MarketShiftSteps = 5;
        /// <summary>Lots one Ctrl-click trades.</summary>
        private const int MarketCtrlSteps = 20;

        private const int MarketSuppliesCol = 0, MarketIronCol = 1;

        private static readonly Color BadgeOn = new Color(0.62f, 0.92f, 0.55f);
        private static readonly Color BadgeDear = new Color(0.95f, 0.55f, 0.45f);

        /// <summary>The Market's two columns, then its level-up. Returns the
        /// cells used.</summary>
        private int RenderMarket(EntityManager em, in EntityActionInfo info)
        {
            // Every cell of the 4 x 4 is already held blank (BlankGrid, in
            // RenderBuildingCells): the grid skips inactive cells, so a
            // cleared one would slide the rows together.

            var prices = Market.Prices(em);
            FactionEconomy.TryGetResources(em, OwnFaction(em), out var bank);
            Entity market = _entity;
            bool open = !em.HasComponent<UnderConstruction>(market);
            int used = 0;
            used += MarketGoodColumn(em, market, MarketSuppliesCol, Market.GoodSupplies, prices, bank, open);
            used += MarketGoodColumn(em, market, MarketIronCol, Market.GoodIron, prices, bank, open);
            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            return used;
        }

        /// <summary>A price in hundredths of a veilstone, as text.</summary>
        private static string Veil(int hundredths)
            => (hundredths / 100f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private int MarketGoodColumn(EntityManager em, Entity market, int col, int good,
            in MarketPrices prices, in FactionResources bank, bool open)
        {
            if (col + 2 * PaletteCols >= _slots.Length) return 0;
            bool isIron = good == Market.GoodIron;
            string resKey = isIron ? "Iron" : "Supplies";
            string res = Loc.T(resKey);
            int price = Market.PriceOf(prices, good);
            int lot = Market.LotSize(good);
            int percent = Market.PricePercent(good, price);
            int held = isIron ? bank.Iron : bank.Supplies;

            int buyOne = Market.BuyCost(price);
            int sellOne = Market.SellPay(price);
            string shared = Loc.T("Every player trades at this price: buying raises it, selling lowers it, and it drifts back over time.");
            string priceLine = string.Format(Loc.T("{0} {1} cost {2} veilstone now ({3}% of the starting price). Selling them pays {4}."),
                lot, res, Veil(price), percent, sellOne);
            string clicks = string.Format(Loc.T("One lot a click (Shift: {0}, Ctrl: {1})."), MarketShiftSteps, MarketCtrlSteps);

            bool canBuy = open && bank.Veilstone >= buyOne;
            bool canSell = open && held >= lot && sellOne > 0;

            // [+] buy
            FillSlot(_slots[col], new ActionButton
            {
                Id = isIron ? "Market_IronBuy" : "Market_SuppliesBuy",
                Label = "+",
                Icon = ActionIcons.Plus(),
                Tooltip = "<b>" + string.Format(Loc.T("Buy {0} {1} for {2} veilstone"), lot, res, buyOne) + "</b>\n"
                          + string.Format(Loc.T("Five lots: {0} veilstone. Twenty: {1}."),
                              Market.Quote(good, price, MarketShiftSteps, true), Market.Quote(good, price, MarketCtrlSteps, true))
                          + "\n" + clicks + "\n" + shared,
                Enabled = canBuy,
                CanAfford = canBuy,
            }, Category.Economy, null,
                canBuy ? (System.Action)(() => MarketTrade(market, good, true)) : null, em);

            // [resource  price]
            var mid = _slots[col + PaletteCols];
            FillSlot(mid, new ActionButton
            {
                Id = isIron ? "Market_IronPrice" : "Market_SuppliesPrice",
                Label = res,
                Icon = ActionIcons.Resource(resKey),
                Tooltip = "<b>" + res + "</b>\n" + priceLine + "\n" + shared,
                Enabled = true,
                CanAfford = true,
            }, Category.Economy, null, null, em);
            SetBadge(mid, Veil(price), percent < 100 ? BadgeOn : percent > 100 ? BadgeDear : GameUIKit.TextDim);

            // [-] sell
            FillSlot(_slots[col + 2 * PaletteCols], new ActionButton
            {
                Id = isIron ? "Market_IronSell" : "Market_SuppliesSell",
                Label = "-",
                Icon = ActionIcons.Minus(),
                Tooltip = "<b>" + string.Format(Loc.T("Sell {0} {1} for {2} veilstone"), lot, res, sellOne) + "</b>\n"
                          + string.Format(Loc.T("Five lots: {0} veilstone. Twenty: {1}."),
                              Market.Quote(good, price, MarketShiftSteps, false), Market.Quote(good, price, MarketCtrlSteps, false))
                          + "\n" + clicks + "\n" + shared,
                Enabled = canSell,
                CanAfford = canSell,
            }, Category.Economy, null,
                canSell ? (System.Action)(() => MarketTrade(market, good, false)) : null, em);
            return 3;
        }

        /// <summary>Lots a click trades: 1, Shift 5, Ctrl 20.</summary>
        private static int ClickSteps()
        {
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
                return MarketCtrlSteps;
            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
                return MarketShiftSteps;
            return 1;
        }

        private void MarketTrade(Entity market, int good, bool buy)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(market) || !em.HasComponent<MarketTag>(market)) return;
            CommandRouter.IssueMarketTrade(em, market, good, ClickSteps(), buy);
            _timer = RefreshInterval;
        }
    }
}
