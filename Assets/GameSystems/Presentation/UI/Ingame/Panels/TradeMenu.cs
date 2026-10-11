// TradeMenu.cs
// The Trade menu (2026-10-11, docs/Design/Veilstone_Economy.md § 3.1 "Hold is
// the default"). A HUD button beside the idle-builder button opens a small
// Synty-framed menu (GameUIKit.PanelChrome / ButtonChrome — the catalog's
// Synty frame sprites):
//
//   Trade Routes                                                    [X]
//   Net trade per minute: +240 veilstone, -120 supplies, -120 iron
//   Outpost          [Hold Trade (2)] [Buy Veilstone (3)] [Forge ...] [Sell ...]
//   Trading Outpost 1     Running         Assign            Assign     Locked
//   ...
//
// One column per trade route, one row per finished Trading Outpost. A cell
// moves that post to that route; a route's header moves every post to it;
// the post's name selects it and brings the camera there. The top line is
// the faction's NET trade across every territory — what its posts make less
// what they spend (TerritoryIncomeSystem's display income, the same numbers
// the resource bar's tooltip splits out). New posts start on Hold Trade.
//
// Hidden while spectating and while the player has no finished post. Every
// order goes through CommandRouter.IssueSetOutpostMode, one per post.
// Placement, sizes and refresh rate are TradeMenu.asset.

using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Input;
using TheWaningBorder.Systems.Economy;
using TheWaningBorder.Systems.World;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class TradeMenu : MonoBehaviour
    {
        static TradeMenuConfig Cfg => TradeMenuConfig.I;

        /// <summary>The routes, in column order: the default first.</summary>
        static readonly TradeRecipe[] Routes =
        {
            TradeRecipe.Hold, TradeRecipe.BuyVeilstone, TradeRecipe.ForgeVeilsteel, TradeRecipe.SellVeilsteel,
        };
        static readonly string[] RouteNames = { "Hold Trade", "Buy Veilstone", "Forge Veilsteel", "Sell Veilsteel" };

        static readonly Color Gain = new Color(0.62f, 0.88f, 0.54f);
        static readonly Color Loss = new Color(1f, 0.42f, 0.37f);

        sealed class Cell
        {
            public RectTransform Root;
            public Image Fill;
            public TMP_Text Label;
        }

        sealed class Row
        {
            public RectTransform Root;
            public Cell Name;
            public Cell[] Route = new Cell[4];
            public Entity Outpost;
        }

        RectTransform _button;
        TMP_Text _buttonLabel;
        RectTransform _menu;
        TMP_Text _net;
        readonly Cell[] _headers = new Cell[4];
        readonly List<Row> _rows = new List<Row>();
        TMP_Text _more;

        readonly List<Entity> _posts = new List<Entity>();
        readonly int[] _routeCount = new int[4];
        readonly TerritoryYield[] _routeNet = new TerritoryYield[4];
        readonly StringBuilder _sb = new StringBuilder(128);
        bool _open;
        float _nextRefresh;

        void Awake() => Build();

        // ── Build ──────────────────────────────────────────────────────────

        void Build()
        {
            var cfg = Cfg;

            _button = GameUIKit.Rect(transform, "TradeButton");
            _button.anchorMin = _button.anchorMax = Vector2.zero;
            _button.pivot = Vector2.zero;
            _button.anchoredPosition = cfg.buttonPosition;
            _button.sizeDelta = cfg.buttonSize;
            var bfill = GameUIKit.ButtonChrome(_button, raycast: true);
            _buttonLabel = GameUIKit.Text(_button, "label", "", cfg.fontSize + 6f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(_buttonLabel.rectTransform);
            UITooltip.Relay(bfill.gameObject).OnLeftClick = Toggle;
            UITooltip.Bind(bfill.gameObject, Loc.T(
                "Trade routes: assign your Trading Outposts to a trade, and see what your trade " +
                "makes and spends across every territory."));

            _menu = GameUIKit.Rect(transform, "TradeMenu");
            _menu.anchorMin = _menu.anchorMax = Vector2.zero;
            _menu.pivot = Vector2.zero;
            _menu.anchoredPosition = cfg.buttonPosition + new Vector2(0f, cfg.buttonSize.y) + cfg.menuOffset;
            GameUIKit.PanelChrome(_menu);

            float inner = cfg.menuWidth - 2f * cfg.padding;
            float y = cfg.padding;

            // Title + close.
            var title = GameUIKit.Text(_menu, "title", Loc.T("Trade Routes"), cfg.titleFontSize,
                GameUIKit.Gold, TextAlignmentOptions.MidlineLeft, wrap: false);
            Place(title.rectTransform, cfg.padding, y, inner - cfg.rowHeight, cfg.rowHeight);
            var close = MakeCell(_menu, cfg.padding + inner - cfg.rowHeight, y, cfg.rowHeight, cfg.rowHeight,
                "X", cfg.fontSize, Close, () => Loc.T("Close"));
            close.Label.color = GameUIKit.TextMain;
            y += cfg.rowHeight + cfg.gap;

            // Net trade line.
            _net = GameUIKit.Text(_menu, "net", "", cfg.fontSize, GameUIKit.TextMain,
                TextAlignmentOptions.MidlineLeft, wrap: false);
            Place(_net.rectTransform, cfg.padding, y, inner, cfg.rowHeight);
            UITooltip.Bind(_net.gameObject, () => Loc.T(
                "What all your Trading Outposts make, less what they spend, per minute, across every " +
                "territory. A post on Hold Trade, or beside a cursed outcrop, trades nothing."));
            _net.raycastTarget = true;
            y += cfg.rowHeight + cfg.gap;

            // Route headers.
            var outpostHead = GameUIKit.Text(_menu, "outpostHead", Loc.T("Outpost"), cfg.fontSize,
                GameUIKit.TextDim, TextAlignmentOptions.MidlineLeft, wrap: false);
            Place(outpostHead.rectTransform, cfg.padding, y, cfg.nameColumnWidth, cfg.rowHeight);
            for (int r = 0; r < Routes.Length; r++)
            {
                int route = r;
                _headers[r] = MakeCell(_menu, ColumnX(r), y, ColumnWidth(), cfg.rowHeight,
                    "", cfg.cellFontSize, () => AssignAll(route), () => HeaderTooltip(route));
            }
            y += cfg.rowHeight + cfg.gap;

            _more = GameUIKit.Text(_menu, "more", "", cfg.cellFontSize, GameUIKit.TextDim,
                TextAlignmentOptions.MidlineLeft, wrap: false);
            _more.gameObject.SetActive(false);

            _menu.gameObject.SetActive(false);
            Refresh();
        }

        float ColumnWidth()
        {
            var cfg = Cfg;
            float inner = cfg.menuWidth - 2f * cfg.padding;
            return (inner - cfg.nameColumnWidth - Routes.Length * cfg.gap) / Routes.Length;
        }

        float ColumnX(int route)
        {
            var cfg = Cfg;
            return cfg.padding + cfg.nameColumnWidth + cfg.gap + route * (ColumnWidth() + cfg.gap);
        }

        /// <summary>Top-left placement inside the menu, y measured DOWN from
        /// its top edge.</summary>
        static void Place(RectTransform rt, float x, float yDown, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yDown);
            rt.sizeDelta = new Vector2(w, h);
        }

        static Cell MakeCell(RectTransform parent, float x, float yDown, float w, float h, string text,
            float fontSize, System.Action click, System.Func<string> tooltip)
        {
            var rt = GameUIKit.Rect(parent, "cell");
            Place(rt, x, yDown, w, h);
            var fill = GameUIKit.ButtonChrome(rt, raycast: true);
            var label = GameUIKit.Text(rt, "label", text, fontSize, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (click != null) UITooltip.Relay(fill.gameObject).OnLeftClick = click;
            if (tooltip != null) UITooltip.Bind(fill.gameObject, tooltip);
            return new Cell { Root = rt, Fill = fill, Label = label };
        }

        Row MakeRow(int index)
        {
            var cfg = Cfg;
            var row = new Row { Root = GameUIKit.Rect(_menu, "row" + index) };
            GameUIKit.Stretch(row.Root);
            float y = RowY(index);
            var r = row;
            row.Name = MakeCell(row.Root, cfg.padding, y, cfg.nameColumnWidth, cfg.rowHeight, "",
                cfg.cellFontSize, () => FocusRow(r), () => NameTooltip(r));
            row.Name.Label.alignment = TextAlignmentOptions.MidlineLeft;
            row.Name.Label.margin = new Vector4(12f, 0f, 8f, 0f);
            for (int c = 0; c < Routes.Length; c++)
            {
                int route = c;
                row.Route[c] = MakeCell(row.Root, ColumnX(c), y, ColumnWidth(), cfg.rowHeight, "",
                    cfg.cellFontSize, () => Assign(r, route), () => CellTooltip(r, route));
            }
            return row;
        }

        float RowY(int index)
        {
            var cfg = Cfg;
            return cfg.padding + 3f * (cfg.rowHeight + cfg.gap) + index * (cfg.rowHeight + cfg.gap);
        }

        // ── Refresh ────────────────────────────────────────────────────────

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            Refresh();
        }

        void Refresh()
        {
            var cfg = Cfg;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, cfg.refreshSeconds);
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            bool live = world != null && world.IsCreated && !GameSettings.IsSpectating;
            if (live) OutpostOrders.Collect(world.EntityManager, _posts);
            else _posts.Clear();

            bool show = _posts.Count > 0;
            if (_button.gameObject.activeSelf != show) _button.gameObject.SetActive(show);
            if (!show && _open) Close();
            if (!show) return;

            string label = string.Format(Loc.T("Trade: {0}"), _posts.Count);
            if (_buttonLabel.text != label) _buttonLabel.text = label;
            if (!_open) return;

            var em = world.EntityManager;
            var me = GameSettings.LocalPlayerFaction;

            // Per-route counts and nominal rates.
            for (int r = 0; r < Routes.Length; r++) { _routeCount[r] = 0; _routeNet[r] = default; }
            for (int i = 0; i < _posts.Count; i++)
            {
                int r = RouteIndex(TheWaningBorder.Entities.TradingOutpost.RecipeOf(em, _posts[i]));
                _routeCount[r]++;
                TradingOutpostSystem.PerMinuteFor(em, _posts[i], out var spend, out var earn);
                if (Routes[r] != TradeRecipe.Hold) _routeNet[r] = Add(_routeNet[r], earn, spend);
            }

            // The net line: what the faction's trade really runs right now.
            var inc = TerritoryIncomeSystem.FactionIncomeForDisplay(em, me);
            var net = Add(default, inc.Bought, inc.Sold);
            string netText = Loc.T("Net trade per minute: ") + YieldText(net, Loc.T("nothing - every post holds"));
            if (_net.text != netText) _net.text = netText;

            for (int r = 0; r < Routes.Length; r++)
            {
                bool unlocked = TradingOutpostSystem.IsUnlocked(me, Routes[r]);
                var h = _headers[r];
                string t = Loc.T(RouteNames[r]) + " (" + _routeCount[r] + ")";
                if (h.Label.text != t) h.Label.text = t;
                h.Label.color = unlocked ? GameUIKit.Gold : GameUIKit.TextLocked;
                h.Fill.color = unlocked ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
            }

            int shown = Mathf.Min(_posts.Count, Mathf.Max(1, cfg.maxRows));
            while (_rows.Count < shown) _rows.Add(MakeRow(_rows.Count));
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool on = i < shown;
                if (row.Root.gameObject.activeSelf != on) row.Root.gameObject.SetActive(on);
                if (!on) continue;
                row.Outpost = _posts[i];
                var current = TheWaningBorder.Entities.TradingOutpost.RecipeOf(em, row.Outpost);

                string name = (em.HasComponent<DisplayName>(row.Outpost)
                    ? em.GetComponentData<DisplayName>(row.Outpost).Value.ToString()
                    : Loc.T("Trading Outpost")) + "  " + (i + 1);
                if (row.Name.Label.text != name) row.Name.Label.text = name;

                for (int c = 0; c < Routes.Length; c++)
                {
                    var cell = row.Route[c];
                    bool running = Routes[c] == current;
                    bool unlocked = TradingOutpostSystem.IsUnlocked(me, Routes[c]);
                    string text = running ? Loc.T("Running") : unlocked ? Loc.T("Assign") : Loc.T("Locked");
                    if (cell.Label.text != text) cell.Label.text = text;
                    cell.Fill.color = running ? GameUIKit.BarGold
                        : unlocked ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
                    cell.Label.color = running ? Color.black
                        : unlocked ? GameUIKit.TextMain : GameUIKit.TextLocked;
                }
            }

            int hidden = _posts.Count - shown;
            float bottom = RowY(shown);
            _more.gameObject.SetActive(hidden > 0);
            if (hidden > 0)
            {
                Place(_more.rectTransform, cfg.padding, bottom, cfg.menuWidth - 2f * cfg.padding, cfg.rowHeight);
                _more.text = string.Format(Loc.T("+{0} more - assign them from a route's header."), hidden);
                bottom += cfg.rowHeight + cfg.gap;
            }
            _menu.sizeDelta = new Vector2(cfg.menuWidth, bottom - cfg.gap + cfg.padding);
        }

        // ── Actions ────────────────────────────────────────────────────────

        void Toggle()
        {
            if (_open) { Close(); return; }
            _open = true;
            _menu.gameObject.SetActive(true);
            Refresh();
        }

        void Close()
        {
            _open = false;
            if (_menu != null) _menu.gameObject.SetActive(false);
        }

        void Assign(Row row, int route)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!em.Exists(row.Outpost)) return;
            if (!TradingOutpostSystem.IsUnlocked(GameSettings.LocalPlayerFaction, Routes[route])) return;
            if (TheWaningBorder.Entities.TradingOutpost.RecipeOf(em, row.Outpost) == Routes[route]) return;
            CommandRouter.IssueSetOutpostMode(em, row.Outpost, Routes[route]);
            _nextRefresh = 0f;
        }

        void AssignAll(int route)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!TradingOutpostSystem.IsUnlocked(GameSettings.LocalPlayerFaction, Routes[route])) return;
            for (int i = 0; i < _posts.Count; i++)
            {
                if (!em.Exists(_posts[i])) continue;
                if (TheWaningBorder.Entities.TradingOutpost.RecipeOf(em, _posts[i]) == Routes[route]) continue;
                CommandRouter.IssueSetOutpostMode(em, _posts[i], Routes[route]);
            }
            _nextRefresh = 0f;
        }

        void FocusRow(Row row)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            OutpostOrders.Focus(world.EntityManager, row.Outpost);
        }

        // ── Tooltips ───────────────────────────────────────────────────────

        string HeaderTooltip(int route)
        {
            var me = GameSettings.LocalPlayerFaction;
            _sb.Clear();
            _sb.Append("<b>").Append(Loc.T(RouteNames[route])).Append("</b>\n");
            if (Routes[route] == TradeRecipe.Hold)
                _sb.Append(Loc.T("The post trades nothing: nothing spent, nothing earned. Every new post starts here."));
            else
            {
                TradingOutpostSystem.PerMinute(me, Routes[route], out var spend, out var earn);
                _sb.Append(Loc.T("Each post, per minute (before its level): "))
                   .Append(YieldText(Add(default, earn, spend), "-"));
                _sb.Append('\n').Append(string.Format(Loc.T("This route now: {0}"),
                    YieldText(_routeNet[route], Loc.T("no post on it"))));
            }
            if (!TradingOutpostSystem.IsUnlocked(me, Routes[route]))
                _sb.Append('\n').Append(Loc.T("<color=#C08040>Research it at a Trading Outpost first.</color>"));
            else
                _sb.Append('\n').Append(Loc.T("Click to move EVERY Outpost to this route."));
            return _sb.ToString();
        }

        string CellTooltip(Row row, int route)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;
            var em = world.EntityManager;
            var me = GameSettings.LocalPlayerFaction;
            if (!TradingOutpostSystem.IsUnlocked(me, Routes[route]))
                return Loc.T("Locked: research this trade at a Trading Outpost first.");
            if (Routes[route] == TradeRecipe.Hold)
                return Loc.T("Hold: this post trades nothing.");
            TradingOutpostSystem.PerMinute(me, Routes[route], out var spend, out var earn);
            float m = em.Exists(row.Outpost) ? TradingOutpostSystem.LevelMultiplier(em, row.Outpost) : 1f;
            var y = Add(default, Scale(earn, m), Scale(spend, m));
            return Loc.T(RouteNames[route]) + ": " + YieldText(y, "-") + " " + Loc.T("per minute");
        }

        string NameTooltip(Row row)
            => Loc.T("Select this Outpost and move the camera to it.");

        // ── Helpers ────────────────────────────────────────────────────────

        static int RouteIndex(TradeRecipe recipe)
        {
            for (int r = 0; r < Routes.Length; r++) if (Routes[r] == recipe) return r;
            return 0;
        }

        /// <summary>acc + earn - spend.</summary>
        static TerritoryYield Add(TerritoryYield acc, TerritoryYield earn, TerritoryYield spend)
            => new TerritoryYield
            {
                Supplies  = acc.Supplies  + earn.Supplies  - spend.Supplies,
                Iron      = acc.Iron      + earn.Iron      - spend.Iron,
                Veilstone = acc.Veilstone + earn.Veilstone - spend.Veilstone,
                Veilsteel = acc.Veilsteel + earn.Veilsteel - spend.Veilsteel,
            };

        static TerritoryYield Scale(TerritoryYield y, float m) => new TerritoryYield
        {
            Supplies = y.Supplies * m, Iron = y.Iron * m, Veilstone = y.Veilstone * m, Veilsteel = y.Veilsteel * m,
        };

        /// <summary>"+240 veilstone, -120 supplies" in gain / loss colours;
        /// <paramref name="none"/> when every line rounds to zero.</summary>
        static string YieldText(TerritoryYield y, string none)
        {
            var sb = new StringBuilder();
            void Part(float v, string name)
            {
                int n = Mathf.RoundToInt(v);
                if (n == 0) return;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(n > 0 ? Gain : Loss)).Append('>')
                  .Append(n > 0 ? "+" : "").Append(n).Append(' ').Append(name).Append("</color>");
            }
            Part(y.Veilstone, Loc.T("veilstone"));
            Part(y.Veilsteel, Loc.T("veilsteel"));
            Part(y.Supplies, Loc.T("supplies"));
            Part(y.Iron, Loc.T("iron"));
            return sb.Length > 0 ? sb.ToString() : none;
        }
    }
}
