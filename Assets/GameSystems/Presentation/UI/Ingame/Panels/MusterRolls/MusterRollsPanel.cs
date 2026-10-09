// MusterRollsPanel.cs
// THE MUSTER ROLLS, IN THE GAME (docs/Design/Muster_Rolls_PostGame.md): the
// post-game report that used to exist only as a browser page fed by headless
// logs (tools/twb-report-page.html). Three tabs over MatchRecord.Current,
// which MatchRecorder fills in memory during EVERY match:
//   * Standings  the score table (Score / Economy / Strategy / Military, K/D,
//                kills, deaths, territories, Fortresses, techs) and the score
//                over time;
//   * Charts     economy and army charts, faction-coloured, with a legend
//                that toggles factions on and off;
//   * Map        the recorded match replayed on the map: territories by
//                owner, buildings, units, deaths, with play / pause, speeds,
//                a scrubber, wheel zoom and drag pan.
//
// Opened from the victory panel's "Muster Rolls" button, or by anything else
// through MusterRollsPanel.Open() (a replay that ends opens it too). Close
// returns to whatever was under it. Spawned hidden by GameUIManager after the
// victory panel, so it covers it.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Diagnostics.MatchRecording;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class MusterRollsPanel : MonoBehaviour
    {
        // Layout, canvas units at the HUD's 3840 x 2160 reference.
        private const float Inset = 70f;
        private const float HeaderH = 215f;
        private const float TabW = 300f, TabH = 84f;
        private const float RowH = 64f;
        private const float SidebarW = 520f;
        private const float TransportH = 120f;

        static MusterRollsPanelConfig _cfg;
        static MusterRollsPanelConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<MusterRollsPanelConfig>());

        public static MusterRollsPanel Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        /// <summary>Raised when the player closes the screen.</summary>
        public static event System.Action Closed;

        private enum Tab { Standings, Charts, Map }

        private RectTransform _root;
        private TMP_Text _subtitle;
        private readonly RectTransform[] _pages = new RectTransform[3];
        private readonly Image[] _tabBg = new Image[3];
        private Tab _tab;

        // Standings
        private RectTransform _table;
        private MusterChart _scoreChart;
        private readonly List<GameObject> _rows = new List<GameObject>();

        // Charts
        private readonly bool[] _visible = new bool[MatchRecord.Factions];
        private readonly List<MusterChart> _charts = new List<MusterChart>();
        private RectTransform _legend;
        private readonly List<GameObject> _legendItems = new List<GameObject>();

        // Map
        private MusterMapView _map;
        private MusterScrubBar _scrub;
        private TMP_Text _clock, _counts, _playLabel;
        private readonly List<Image> _speedBg = new List<Image>();
        private float _t;
        private bool _playing;
        private bool _mapShown;
        private int _speedIdx;
        private float _nextCounts;

        private MatchRecord _rec;

        private static readonly string[] ColumnNames =
            { "#", "Faction", "Score", "Economy", "Strategy", "Military", "K/D",
              "Kills", "Deaths", "Territories", "Fortresses", "Techs" };
        private static readonly float[] ColumnW =
            { 0.04f, 0.19f, 0.09f, 0.09f, 0.09f, 0.09f, 0.07f, 0.07f, 0.07f, 0.08f, 0.06f, 0.06f };

        private static readonly (RecordSeries s, string title)[] ChartDefs =
        {
            (RecordSeries.Economy, "Economy score"), (RecordSeries.Strategy, "Strategy score"),
            (RecordSeries.Military, "Military score"), (RecordSeries.KillDeath, "K/D"),
            (RecordSeries.Supplies, "Supplies"), (RecordSeries.Iron, "Iron"),
            (RecordSeries.Veilstone, "Veilstone"), (RecordSeries.Veilsteel, "Veilsteel"),
            (RecordSeries.Army, "Army units"), (RecordSeries.Population, "Population"),
            (RecordSeries.Buildings, "Buildings"), (RecordSeries.Territories, "Territories"),
        };

        // ── lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            IsOpen = false;
            for (int f = 0; f < _visible.Length; f++) _visible[f] = true;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; IsOpen = false; }
        }

        // ── public API ───────────────────────────────────────────────────

        /// <summary>Open on the current (or last finished) match's record.
        /// False when no panel exists or nothing was recorded.</summary>
        public static bool Open() => Open(MatchRecord.Current);

        /// <summary>Open on a given record. False when no panel exists, the
        /// record is null, or the screen's config is missing.</summary>
        public static bool Open(MatchRecord record)
        {
            if (Instance == null || record == null || Cfg == null) return false;
            Instance.Show(record);
            return true;
        }

        /// <summary>Close the screen if it is open.</summary>
        public static void CloseIfOpen()
        {
            if (Instance != null && IsOpen) Instance.Hide();
        }

        private void Show(MatchRecord rec)
        {
            _rec = rec;
            transform.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            IsOpen = true;

            string outcome = rec.Ended
                ? (string.IsNullOrEmpty(rec.Winner) ? "" : "  ·  " + Loc.T("Winner") + ": " + rec.Winner)
                : "  ·  " + Loc.T("in progress");
            _subtitle.text = $"{rec.MapName}  ·  {MatchRecord.Clock(rec.Duration)}{outcome}";

            BuildStandings();
            BuildLegend();
            RefreshCharts();

            var cfg = Cfg;
            _map.Bind(rec, cfg);
            _t = 0f;
            _playing = false;
            _mapShown = false;
            _speedIdx = Mathf.Clamp(cfg.defaultSpeedIndex, 0, Mathf.Max(0, (cfg.playbackSpeeds?.Length ?? 1) - 1));
            PaintSpeeds();
            UpdatePlayLabel();
            SelectTab(Tab.Standings);
        }

        private void Hide()
        {
            IsOpen = false;
            _playing = false;
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        // ── frame ────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsOpen || _rec == null || _tab != Tab.Map) return;
            float dur = Mathf.Max(0.01f, _rec.Duration);
            if (_playing)
            {
                var speeds = Cfg.playbackSpeeds;
                float speed = speeds != null && speeds.Length > 0 ? speeds[Mathf.Clamp(_speedIdx, 0, speeds.Length - 1)] : 1f;
                _t += Time.unscaledDeltaTime * speed;
                if (_t >= dur) { _t = dur; _playing = false; UpdatePlayLabel(); }
            }
            _map.Render(_t);
            _scrub.SetValue(_t / dur);
            _clock.text = $"{MatchRecord.Clock(_t)} <size=60%><color=#A8A390>/ {MatchRecord.Clock(dur)}</color></size>";
            if (Time.unscaledTime >= _nextCounts)
            {
                _nextCounts = Time.unscaledTime + 0.25f;
                UpdateCounts();
            }
        }

        // ── construction ─────────────────────────────────────────────────

        private void Build()
        {
            _root = GameUIKit.Rect(transform, "MusterRolls");
            GameUIKit.Stretch(_root);
            var scrim = GameUIKit.Image(_root, "scrim", new Color(0f, 0f, 0f, 0.85f), raycast: true);
            GameUIKit.Stretch(scrim.rectTransform);

            var panel = GameUIKit.Rect(_root, "panel");
            Place(panel, Vector2.zero, Vector2.one, new Vector2(Inset, Inset), new Vector2(-Inset, -Inset));
            GameUIKit.PanelChrome(panel);

            var title = GameUIKit.Text(panel, "title", Loc.T("MUSTER ROLLS"), 64f, GameUIKit.Gold,
                TextAlignmentOptions.TopLeft, wrap: false);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, -130f), new Vector2(-1500f, -40f));

            _subtitle = GameUIKit.Text(panel, "subtitle", "", 32f, GameUIKit.TextDim,
                TextAlignmentOptions.TopLeft, wrap: false);
            Place(_subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(62f, -190f), new Vector2(-1500f, -135f));

            // Tabs + close, right-aligned along the top.
            float x = -60f;
            Button(panel, "close", Loc.T("Close"), Hide, new Vector2(x - 260f, -40f - TabH), new Vector2(260f, TabH));
            x -= 260f + 50f;
            string[] tabNames = { Loc.T("Standings"), Loc.T("Charts"), Loc.T("Map") };
            for (int i = 2; i >= 0; i--)
            {
                int idx = i;
                _tabBg[i] = Button(panel, "tab" + i, tabNames[i], () => SelectTab((Tab)idx),
                    new Vector2(x - TabW, -40f - TabH), new Vector2(TabW, TabH));
                x -= TabW + 16f;
            }

            var body = GameUIKit.Rect(panel, "body");
            Place(body, Vector2.zero, Vector2.one, new Vector2(50f, 50f), new Vector2(-50f, -HeaderH));
            for (int i = 0; i < 3; i++)
            {
                _pages[i] = GameUIKit.Rect(body, ((Tab)i).ToString());
                GameUIKit.Stretch(_pages[i]);
            }

            BuildStandingsPage(_pages[0]);
            BuildChartsPage(_pages[1]);
            BuildMapPage(_pages[2]);

            _root.gameObject.SetActive(false);
        }

        private void BuildStandingsPage(RectTransform page)
        {
            _table = GameUIKit.Rect(page, "table");
            Place(_table, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -RowH * 10f), Vector2.zero);
            var cfg = Cfg;
            _scoreChart = MusterChart.Create(page, "scoreChart", Loc.T("Score over time"), RecordSeries.Score,
                _visible, cfg != null ? cfg.chartLineWidth : 4f, cfg != null ? cfg.chartMaxPoints : 320);
            _charts.Add(_scoreChart);
        }

        private void BuildChartsPage(RectTransform page)
        {
            _legend = GameUIKit.Rect(page, "legend");
            Place(_legend, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -70f), Vector2.zero);
            var h = _legend.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 24f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            var grid = GameUIKit.Rect(page, "grid");
            Place(grid, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -90f));
            var cfg = Cfg;
            const int cols = 4, rows = 3;
            for (int i = 0; i < ChartDefs.Length; i++)
            {
                int c = i % cols, r = i / cols;
                var chart = MusterChart.Create(grid, "chart" + i, Loc.T(ChartDefs[i].title), ChartDefs[i].s,
                    _visible, cfg != null ? cfg.chartLineWidth : 4f, cfg != null ? cfg.chartMaxPoints : 320);
                var rt = (RectTransform)chart.transform.parent;
                Place(rt, new Vector2(c / (float)cols, 1f - (r + 1) / (float)rows),
                    new Vector2((c + 1) / (float)cols, 1f - r / (float)rows),
                    new Vector2(10f, 10f), new Vector2(-10f, -10f));
                _charts.Add(chart);
            }
        }

        private void BuildMapPage(RectTransform page)
        {
            var stageHost = GameUIKit.Rect(page, "stageHost");
            Place(stageHost, Vector2.zero, Vector2.one, new Vector2(0f, TransportH + 20f), new Vector2(-SidebarW - 20f, 0f));
            _map = MusterMapView.Create(stageHost);
            GameUIKit.Stretch((RectTransform)_map.transform);

            // Sidebar: clock + live counts.
            var side = GameUIKit.Image(page, "sidebar", new Color(0.03f, 0.04f, 0.09f, 0.75f)).rectTransform;
            Place(side, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-SidebarW, TransportH + 20f), Vector2.zero);
            _clock = GameUIKit.Text(side, "clock", "0:00", 72f, GameUIKit.Gold, TextAlignmentOptions.Top, wrap: false);
            Place(_clock.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -130f), new Vector2(-20f, -24f));
            var hdr = GameUIKit.Text(side, "countsHeader", Loc.T("Units  ·  Buildings"), 28f, GameUIKit.TextDim,
                TextAlignmentOptions.TopLeft, wrap: false);
            Place(hdr.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(36f, -190f), new Vector2(-20f, -145f));
            _counts = GameUIKit.Text(side, "counts", "", 32f, GameUIKit.TextMain, TextAlignmentOptions.TopLeft, wrap: false);
            Place(_counts.rectTransform, Vector2.zero, new Vector2(1f, 1f), new Vector2(36f, 20f), new Vector2(-20f, -205f));
            _counts.lineSpacing = 18f;

            // Transport.
            var bar = GameUIKit.Rect(page, "transport");
            Place(bar, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, TransportH));
            var play = Button(bar, "play", "", TogglePlay, new Vector2(0f, 0f), new Vector2(240f, TransportH), bottomLeft: true);
            _playLabel = play.transform.parent.GetComponentInChildren<TMP_Text>();
            var speeds = Cfg != null ? Cfg.playbackSpeeds : null;
            float x = 260f;
            if (speeds != null)
                for (int i = 0; i < speeds.Length; i++)
                {
                    int idx = i;
                    var bg = Button(bar, "speed" + i, speeds[i].ToString("0.#") + "x",
                        () => { _speedIdx = idx; PaintSpeeds(); },
                        new Vector2(x, 0f), new Vector2(140f, TransportH), bottomLeft: true);
                    _speedBg.Add(bg);
                    x += 150f;
                }
            var scrubHost = GameUIKit.Rect(bar, "scrubHost");
            Place(scrubHost, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x + 30f, -14f), new Vector2(-10f, 14f));
            _scrub = MusterScrubBar.Create(scrubHost);
            GameUIKit.Stretch((RectTransform)_scrub.transform);
            _scrub.Scrubbed = v =>
            {
                if (_rec == null) return;
                _t = v * Mathf.Max(0.01f, _rec.Duration);
            };
        }

        // ── tabs ─────────────────────────────────────────────────────────

        private void SelectTab(Tab tab)
        {
            _tab = tab;
            for (int i = 0; i < 3; i++)
            {
                _pages[i].gameObject.SetActive(i == (int)tab);
                if (_tabBg[i] != null) _tabBg[i].color = i == (int)tab ? GameUIKit.BarBlue * 0.55f : GameUIKit.ButtonBg;
            }
            if (tab != Tab.Map) _playing = false;
            if (tab == Tab.Map && !_mapShown)
            {
                // First look at the map: play the match from the start.
                _mapShown = true;
                _map.ResetView();
                _t = 0f;
                _playing = true;
            }
            UpdatePlayLabel();
            if (tab != Tab.Map) RefreshCharts();
        }

        // ── standings ────────────────────────────────────────────────────

        private void BuildStandings()
        {
            foreach (var r in _rows) if (r != null) { r.SetActive(false); Destroy(r); }
            _rows.Clear();

            var order = new List<int>();
            for (int f = 0; f < MatchRecord.Factions; f++) if (_rec.FactionPresent[f]) order.Add(f);
            order.Sort((a, b) => _rec.Last(RecordSeries.Score, b).CompareTo(_rec.Last(RecordSeries.Score, a)));

            float tableH = RowH * (order.Count + 1) + 10f;
            Place(_table, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -tableH), Vector2.zero);
            Place((RectTransform)_scoreChart.transform.parent, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -tableH - 30f));

            _rows.Add(Row(0, null, -1, 0));
            for (int i = 0; i < order.Count; i++) _rows.Add(Row(i + 1, order, order[i], i + 1));
        }

        private GameObject Row(int index, List<int> order, int f, int rank)
        {
            var row = GameUIKit.Rect(_table, index == 0 ? "header" : "row" + index);
            Place(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -RowH * (index + 1)), new Vector2(0f, -RowH * index));
            if (index > 0)
            {
                var stripe = GameUIKit.Image(row, "stripe", index % 2 == 0
                    ? new Color(1f, 1f, 1f, 0.035f) : new Color(1f, 1f, 1f, 0.07f));
                GameUIKit.Stretch(stripe.rectTransform);
            }
            float x0 = 0f;
            for (int c = 0; c < ColumnNames.Length; c++)
            {
                float x1 = x0 + ColumnW[c];
                string s;
                Color col = GameUIKit.TextMain;
                if (index == 0) { s = Loc.T(ColumnNames[c]); col = GameUIKit.Gold; }
                else s = Cell(c, f, rank);
                var t = GameUIKit.Text(row, ColumnNames[c], s, index == 0 ? 28f : 32f, col,
                    c == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline, wrap: false);
                Place(t.rectTransform, new Vector2(x0, 0f), new Vector2(x1, 1f),
                    new Vector2(c == 1 ? 56f : 4f, 0f), new Vector2(-4f, 0f));
                if (c == 1 && index > 0)
                {
                    var sw = GameUIKit.Image(row, "swatch", _rec.FactionColor[f]);
                    var srt = sw.rectTransform;
                    srt.anchorMin = srt.anchorMax = new Vector2(x0, 0.5f);
                    srt.pivot = new Vector2(0f, 0.5f);
                    srt.sizeDelta = new Vector2(34f, 34f);
                    srt.anchoredPosition = new Vector2(10f, 0f);
                }
                x0 = x1;
            }
            return row.gameObject;
        }

        private string Cell(int c, int f, int rank)
        {
            var r = _rec;
            switch (c)
            {
                case 0: return rank.ToString();
                case 1:
                {
                    string name = ((Faction)f).ToString();
                    if (!r.Observer && f == r.LocalFaction) name += " <color=#A8A390>(" + Loc.T("You") + ")</color>";
                    if (r.Ended && r.Winner == ((Faction)f).ToString())
                        name += " <color=#E8D5A0>- " + Loc.T("Winner") + "</color>";
                    return name;
                }
                case 2: return MusterChart.Fmt(r.Last(RecordSeries.Score, f));
                case 3: return MusterChart.Fmt(r.Last(RecordSeries.Economy, f));
                case 4: return MusterChart.Fmt(r.Last(RecordSeries.Strategy, f));
                case 5: return MusterChart.Fmt(r.Last(RecordSeries.Military, f));
                case 6: return r.Last(RecordSeries.KillDeath, f).ToString("0.00");
                case 7: return MusterChart.Fmt(r.Last(RecordSeries.Kills, f));
                case 8: return MusterChart.Fmt(r.Last(RecordSeries.Deaths, f));
                case 9: return MusterChart.Fmt(r.Last(RecordSeries.Territories, f));
                case 10: return MusterChart.Fmt(r.Last(RecordSeries.Fortresses, f));
                case 11: return MusterChart.Fmt(r.Last(RecordSeries.Techs, f));
            }
            return "";
        }

        // ── charts ───────────────────────────────────────────────────────

        private void BuildLegend()
        {
            foreach (var g in _legendItems) if (g != null) { g.SetActive(false); Destroy(g); }
            _legendItems.Clear();
            for (int f = 0; f < MatchRecord.Factions; f++)
            {
                if (!_rec.FactionPresent[f]) continue;
                int fi = f;
                var item = GameUIKit.Rect(_legend, "legend" + f);
                var le = item.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 250f;
                var hit = GameUIKit.Image(item, "hit", new Color(0f, 0f, 0f, 0.01f), raycast: true);
                GameUIKit.Stretch(hit.rectTransform);
                var sw = GameUIKit.Image(item, "swatch", _rec.FactionColor[f]);
                var srt = sw.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.sizeDelta = new Vector2(30f, 30f);
                var label = GameUIKit.Text(item, "name", ((Faction)f).ToString(), 30f, GameUIKit.TextMain,
                    TextAlignmentOptions.MidlineLeft, wrap: false);
                Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(44f, 0f), Vector2.zero);
                var relay = UITooltip.Relay(hit.gameObject);
                relay.OnLeftClick = () =>
                {
                    _visible[fi] = !_visible[fi];
                    sw.color = _visible[fi] ? _rec.FactionColor[fi] : _rec.FactionColor[fi] * 0.3f;
                    label.color = _visible[fi] ? GameUIKit.TextMain : GameUIKit.TextDim * 0.6f;
                    RefreshCharts();
                };
                sw.color = _visible[f] ? _rec.FactionColor[f] : _rec.FactionColor[f] * 0.3f;
                label.color = _visible[f] ? GameUIKit.TextMain : GameUIKit.TextDim * 0.6f;
                _legendItems.Add(item.gameObject);
            }
        }

        private void RefreshCharts()
        {
            if (_rec == null) return;
            Canvas.ForceUpdateCanvases();
            foreach (var c in _charts) if (c != null) c.Refresh(_rec);
        }

        // ── map transport ────────────────────────────────────────────────

        private void TogglePlay()
        {
            if (_rec == null) return;
            if (!_playing && _t >= _rec.Duration - 0.01f) _t = 0f;   // replay from the start
            _playing = !_playing;
            UpdatePlayLabel();
        }

        private void UpdatePlayLabel()
        {
            if (_playLabel != null) _playLabel.text = _playing ? Loc.T("Pause") : Loc.T("Play");
        }

        private void PaintSpeeds()
        {
            for (int i = 0; i < _speedBg.Count; i++)
                _speedBg[i].color = i == _speedIdx ? GameUIKit.BarBlue * 0.55f : GameUIKit.ButtonBg;
        }

        private void UpdateCounts()
        {
            var ov = _map.Overlay;
            var sb = new System.Text.StringBuilder(256);
            for (int f = 0; f < MatchRecord.Factions; f++)
            {
                if (!_rec.FactionPresent[f]) continue;
                string hex = ColorUtility.ToHtmlStringRGB(_rec.FactionColor[f]);
                sb.Append("<color=#").Append(hex).Append(">")
                  .Append(((Faction)f).ToString()).Append("</color><pos=58%>")
                  .Append(ov.UnitCounts[f]).Append("<pos=80%>").Append(ov.BuildingCounts[f]).Append('\n');
            }
            int cu = ov.UnitCounts[MatchRecord.Factions], cb = ov.BuildingCounts[MatchRecord.Factions];
            if (cu + cb > 0)
            {
                string hex = ColorUtility.ToHtmlStringRGB(Cfg.curseColor);
                sb.Append("<color=#").Append(hex).Append(">").Append(Loc.T("Curse"))
                  .Append("</color><pos=58%>").Append(cu).Append("<pos=80%>").Append(cb).Append('\n');
            }
            _counts.text = sb.ToString();
        }

        // ── helpers ──────────────────────────────────────────────────────

        private static void Place(RectTransform r, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
        }

        /// <summary>A chrome button. Anchored top-right of its parent unless
        /// <paramref name="bottomLeft"/>; <paramref name="pos"/> is its
        /// bottom-left corner relative to that anchor. Returns the fill.</summary>
        private static Image Button(RectTransform parent, string name, string label, System.Action click,
            Vector2 pos, Vector2 size, bool bottomLeft = false)
        {
            var rt = GameUIKit.Rect(parent, name);
            var a = bottomLeft ? Vector2.zero : Vector2.one;
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            var text = GameUIKit.Text(rt, "label", label, 34f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);

            // Hover is a separate wash, so selection tints on the fill (tabs,
            // speeds) are never overwritten by a hover restore.
            var hover = GameUIKit.Image(rt, "hover", new Color(1f, 1f, 1f, 0.10f));
            GameUIKit.Stretch(hover.rectTransform);
            hover.enabled = false;

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = click;
            relay.OnEnter = () => hover.enabled = true;
            relay.OnExit = () => hover.enabled = false;
            return bg;
        }
    }
}
