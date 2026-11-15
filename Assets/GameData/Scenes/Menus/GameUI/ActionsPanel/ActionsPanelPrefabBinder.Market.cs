// ActionsPanelPrefabBinder.Market.cs
// THE MARKET ON THE STANDARD ACTIONS PANEL (decision 55 (d), reworked by
// decision 59, 2026-11-15; docs/Design/Age_1_Alanthor.md § The Market).
// Developer: "three columns of icons: plus sign, resource icon, minus sign.
// One column for every resource." The grid, one column each:
//
//            col 0 Iron      col 1 Veilstone   col 2 Surplus (supplies)
//   row 0    [+]             [+]               [+]  keep more
//   row 1    [iron  N/min]   [veil  N/min]     [supplies  On/Off  reserve]
//   row 2    [-]             [-]               [-]  keep less
//
// A plus or minus moves a rate by the Market SO's rateStep (Shift: five
// steps, Ctrl: twenty); clicking a resource icon stops buying it. The third
// column is the surplus mode: its icon turns it on and off, and while it is on
// every supply above the reserve is turned into iron each second; the plus and
// minus move the reserve by the SO's marketReserveStep. Tooltips name the
// supplies a minute the setting costs at the SO's fixed prices.
//
// Every click goes through CommandRouter (SetMarketRate / SetMarketSurplus
// lockstep commands) with the whole setting, absolute, so it lands on every
// peer at the same tick. The panel uses the builder's square 4 x 4 grid (large
// cells); the level-up takes the cell after the three rows.
//
// Read-only on the simulation: MarketRates, the Market SO and the bank.

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
        /// <summary>Steps one Shift-click moves a Market setting.</summary>
        private const int MarketShiftSteps = 5;
        /// <summary>Steps one Ctrl-click moves a Market setting.</summary>
        private const int MarketCtrlSteps = 20;

        private const int MarketIronCol = 0, MarketVeilCol = 1, MarketSurplusCol = 2;

        private static readonly Color BadgeOn = new Color(0.62f, 0.92f, 0.55f);

        /// <summary>The Market's three columns, then its level-up. Returns the
        /// cells used.</summary>
        private int RenderMarket(EntityManager em, in EntityActionInfo info)
        {
            for (int i = 0; i < _slots.Length; i++) ClearSlot(_slots[i]);
            // Hold every cell of the three rows in place: the grid skips
            // inactive cells, so a cleared one would slide the rows together.
            for (int i = 0; i < 3 * PaletteCols && i < _slots.Length; i++) BlankSlot(_slots[i]);

            var r = em.GetComponentData<MarketRates>(_entity);
            Entity market = _entity;
            int used = 0;
            used += MarketRateColumn(em, market, MarketIronCol, isIron: true, r);
            used += MarketRateColumn(em, market, MarketVeilCol, isIron: false, r);
            used += MarketSurplusColumn(em, market, MarketSurplusCol, r);
            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            return used;
        }

        private static string StepHint(int step, string unit)
            => string.Format(Loc.T("{0} {3} a click (Shift: {1}, Ctrl: {2})."),
                step, step * MarketShiftSteps, step * MarketCtrlSteps, unit);

        private int MarketRateColumn(EntityManager em, Entity market, int col, bool isIron, in MarketRates r)
        {
            if (col + 2 * PaletteCols >= _slots.Length) return 0;
            int rate = isIron ? r.IronPerMinute : r.VeilstonePerMinute;
            int price = isIron ? Market.SuppliesPerIron : Market.SuppliesPerVeilstone;
            string resKey = isIron ? "Iron" : "Veilstone";
            string res = Loc.T(resKey);
            string costNow = string.Format(Loc.T("The Market spends {0} supplies a minute on its set rates ({1} on {2})."),
                Market.SuppliesCostPerMinute(r.IronPerMinute, r.VeilstonePerMinute), rate * price, res);
            string priceLine = string.Format(Loc.T("Fixed price: {0} supplies for 1 {1}. Nothing is bought while the bank cannot pay."),
                price, res);
            string unit = Loc.T("a minute");
            int iron = r.IronPerMinute, veil = r.VeilstonePerMinute;

            // [+]
            var up = _slots[col];
            FillSlot(up, new ActionButton
            {
                Id = isIron ? "Market_IronUp" : "Market_VeilstoneUp",
                Label = "+",
                Icon = ActionIcons.Plus(),
                Tooltip = "<b>" + string.Format(Loc.T("Buy more {0}"), res) + "</b>\n"
                          + StepHint(Market.RateStep, unit) + "\n" + priceLine + "\n" + costNow,
                Enabled = rate < Market.MaxRate,
                CanAfford = true,
            }, Category.Economy, null,
                rate < Market.MaxRate ? (System.Action)(() => MarketStep(market, isIron, +1)) : null, em);

            // [resource  N/min]
            var mid = _slots[col + PaletteCols];
            FillSlot(mid, new ActionButton
            {
                Id = isIron ? "Market_IronRate" : "Market_VeilstoneRate",
                Label = res,
                Icon = ActionIcons.Resource(resKey),
                Tooltip = "<b>" + string.Format(Loc.T("Buying {0} {1} a minute for {2} supplies"), rate, res, rate * price) + "</b>\n"
                          + priceLine + "\n" + costNow
                          + (rate > 0 ? "\n" + Loc.T("Click to stop buying it.") : ""),
                Enabled = true,
                CanAfford = rate > 0,
            }, Category.Economy, null, () => MarketSet(market, isIron, 0), em);
            SetBadge(mid, string.Format(Loc.T("{0}/min"), rate), rate > 0 ? BadgeOn : GameUIKit.TextDim);

            // [-]
            var down = _slots[col + 2 * PaletteCols];
            FillSlot(down, new ActionButton
            {
                Id = isIron ? "Market_IronDown" : "Market_VeilstoneDown",
                Label = "-",
                Icon = ActionIcons.Minus(),
                Tooltip = "<b>" + string.Format(Loc.T("Buy less {0}"), res) + "</b>\n"
                          + StepHint(Market.RateStep, unit) + "\n" + costNow,
                Enabled = rate > 0,
                CanAfford = true,
            }, Category.Economy, null,
                rate > 0 ? (System.Action)(() => MarketStep(market, isIron, -1)) : null, em);
            return 3;
        }

        private int MarketSurplusColumn(EntityManager em, Entity market, int col, in MarketRates r)
        {
            if (col + 2 * PaletteCols >= _slots.Length) return 0;
            bool on = r.AutoIron != 0;
            int reserve = r.ReserveSupplies;
            int step = Market.ReserveStep;
            int perIron = Market.SuppliesPerIron;
            int surplus = 0;
            if (FactionEconomy.TryGetResources(em, OwnFaction(em), out var bank))
                surplus = System.Math.Max(0, bank.Supplies - reserve);
            string what = string.Format(Loc.T("Surplus to iron: everything above {0} supplies is turned into iron every second, at {1} supplies for 1 iron, after the set rates are bought."),
                reserve, perIron);
            string unit = Loc.T("supplies");

            FillSlot(_slots[col], new ActionButton
            {
                Id = "Market_ReserveUp",
                Label = "+",
                Icon = ActionIcons.Plus(),
                Tooltip = "<b>" + Loc.T("Keep more supplies") + "</b>\n" + StepHint(step, unit) + "\n" + what,
                Enabled = reserve < Market.MaxReserve,
                CanAfford = true,
            }, Category.Economy, null,
                reserve < Market.MaxReserve ? (System.Action)(() => ReserveStep(market, +1)) : null, em);

            var mid = _slots[col + PaletteCols];
            FillSlot(mid, new ActionButton
            {
                Id = "Market_Surplus",
                Label = Loc.T("Supplies"),
                Icon = ActionIcons.Resource("Supplies"),
                Tooltip = "<b>" + (on ? Loc.T("Surplus to iron: ON (click to stop)") : Loc.T("Surplus to iron: OFF (click to start)")) + "</b>\n"
                          + what
                          + (on && surplus > 0
                              ? "\n" + string.Format(Loc.T("Right now: {0} supplies above the reserve, {1} iron."), surplus, surplus / System.Math.Max(1, perIron))
                              : ""),
                Enabled = true,
                CanAfford = on,
            }, Category.Economy, null, () => SurplusSet(market, null, !on), em);
            SetBadge(mid, (on ? Loc.T("On") : Loc.T("Off")) + "  " + reserve, on ? BadgeOn : GameUIKit.TextDim);

            FillSlot(_slots[col + 2 * PaletteCols], new ActionButton
            {
                Id = "Market_ReserveDown",
                Label = "-",
                Icon = ActionIcons.Minus(),
                Tooltip = "<b>" + Loc.T("Keep fewer supplies") + "</b>\n" + StepHint(step, unit) + "\n" + what,
                Enabled = reserve > 0,
                CanAfford = true,
            }, Category.Economy, null,
                reserve > 0 ? (System.Action)(() => ReserveStep(market, -1)) : null, em);
            return 3;
        }

        /// <summary>Steps a click moves a setting: 1, Shift 5, Ctrl 20.</summary>
        private static int ClickSteps()
        {
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
                return MarketCtrlSteps;
            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
                return MarketShiftSteps;
            return 1;
        }

        private void MarketStep(Entity market, bool isIron, int dir)
        {
            int delta = dir * Market.RateStep * ClickSteps();
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

        private void ReserveStep(Entity market, int dir)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            var r = em.GetComponentData<MarketRates>(market);
            SurplusSet(market, r.ReserveSupplies + dir * Market.ReserveStep * ClickSteps(), r.AutoIron != 0);
        }

        /// <summary>Send the surplus setting; a null reserve keeps the current one.</summary>
        private void SurplusSet(Entity market, int? reserve, bool on)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            var r = em.GetComponentData<MarketRates>(market);
            CommandRouter.IssueSetMarketSurplus(em, market, System.Math.Max(0, reserve ?? r.ReserveSupplies), on);
            _timer = RefreshInterval;
        }
    }
}
