// ActionsPanelPrefabBinder.Market.cs
// THE MARKET ON THE STANDARD ACTIONS PANEL (decision 55 (d),
// docs/Design/Age_1_Alanthor.md § The Market). Two rows of the grid:
//   row 0   Iron:      [-]  [the rate, click to stop]  [+]
//   row 1   Veilstone: [-]  [the rate, click to stop]  [+]
// A click moves a rate by the Market SO's rateStep (Shift: five steps); the
// tooltips name the supplies a minute the setting costs at the SO's fixed
// prices. Every click goes through CommandRouter.IssueSetMarketRate (the
// SetMarketRate lockstep command) with BOTH rates, absolute, so the setting
// lands on every peer at the same tick.
//
// Read-only on the simulation: MarketRates and the Market SO.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>Steps one Shift-click moves a Market rate.</summary>
        private const int MarketShiftSteps = 5;

        /// <summary>The Market's two rate rows, then its (empty) research and
        /// level rows like any building. Returns the cells used.</summary>
        private int RenderMarket(EntityManager em, in EntityActionInfo info)
        {
            for (int i = 0; i < _slots.Length; i++) ClearSlot(_slots[i]);
            var r = em.GetComponentData<MarketRates>(_entity);
            Entity market = _entity;

            int used = 0;
            used += MarketRow(em, market, 0, isIron: true, r.IronPerMinute, r.VeilstonePerMinute);
            used += MarketRow(em, market, Cols, isIron: false, r.IronPerMinute, r.VeilstonePerMinute);
            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            return used;
        }

        private int MarketRow(EntityManager em, Entity market, int first, bool isIron, int iron, int veil)
        {
            if (first + 2 >= _slots.Length) return 0;
            int rate = isIron ? iron : veil;
            int price = isIron ? Market.SuppliesPerIron : Market.SuppliesPerVeilstone;
            int max = Market.MaxRate;
            string res = isIron ? Loc.T("Iron") : Loc.T("Veilstone");
            string costNow = string.Format(Loc.T("The Market spends {0} supplies a minute in all ({1} on {2})."),
                Market.SuppliesCostPerMinute(iron, veil), rate * price, res);
            string priceLine = string.Format(Loc.T("Fixed price: {0} supplies for 1 {1}. Nothing is bought while the bank cannot pay."),
                price, res);

            for (int k = 0; k < 3; k++) BlankSlot(_slots[first + k]);

            FillSlot(_slots[first], new ActionButton
            {
                Id = isIron ? "Market_IronDown" : "Market_VeilstoneDown",
                Label = string.Format(Loc.T("{0}\n-{1}"), res, Market.RateStep),
                Tooltip = "<b>" + string.Format(Loc.T("Buy less {0}"), res) + "</b>\n"
                          + string.Format(Loc.T("-{0} a minute (Shift: -{1})."), Market.RateStep, Market.RateStep * MarketShiftSteps)
                          + "\n" + costNow,
                Enabled = rate > 0,
                CanAfford = true,
            }, Category.Economy, null,
                rate > 0 ? (System.Action)(() => MarketStep(market, isIron, -1)) : null, em);

            FillSlot(_slots[first + 1], new ActionButton
            {
                Id = isIron ? "Market_IronRate" : "Market_VeilstoneRate",
                Label = string.Format(Loc.T("{0}\n{1}/min"), res, rate),
                Tooltip = "<b>" + string.Format(Loc.T("Buying {0} {1} a minute for {2} supplies"), rate, res, rate * price) + "</b>\n"
                          + priceLine + "\n" + costNow
                          + (rate > 0 ? "\n" + Loc.T("Click to stop buying it.") : ""),
                Enabled = true,
                CanAfford = true,
            }, Category.Economy, null, () => MarketSet(market, isIron, 0), em);

            FillSlot(_slots[first + 2], new ActionButton
            {
                Id = isIron ? "Market_IronUp" : "Market_VeilstoneUp",
                Label = string.Format(Loc.T("{0}\n+{1}"), res, Market.RateStep),
                Tooltip = "<b>" + string.Format(Loc.T("Buy more {0}"), res) + "</b>\n"
                          + string.Format(Loc.T("+{0} a minute (Shift: +{1}). No ceiling: the bank is the only limit."),
                              Market.RateStep, Market.RateStep * MarketShiftSteps)
                          + "\n" + priceLine + "\n" + costNow,
                Enabled = rate < max,
                CanAfford = true,
            }, Category.Economy, null,
                rate < max ? (System.Action)(() => MarketStep(market, isIron, +1)) : null, em);

            return 3;
        }

        private void MarketStep(Entity market, bool isIron, int dir)
        {
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            int delta = dir * Market.RateStep * (shift ? MarketShiftSteps : 1);
            var em = EM(out bool ok);
            if (!ok || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            var r = em.GetComponentData<MarketRates>(market);
            MarketSet(market, isIron, (isIron ? r.IronPerMinute : r.VeilstonePerMinute) + delta);
        }

        private void MarketSet(Entity market, bool isIron, int value)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            var r = em.GetComponentData<MarketRates>(market);
            int iron = isIron ? value : r.IronPerMinute;
            int veil = isIron ? r.VeilstonePerMinute : value;
            CommandRouter.IssueSetMarketRate(em, market, iron, veil);
            _timer = RefreshInterval;
        }
    }
}
