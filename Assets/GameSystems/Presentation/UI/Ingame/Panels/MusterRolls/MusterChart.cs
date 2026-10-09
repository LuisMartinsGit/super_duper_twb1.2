// MusterChart.cs
// One line chart on the Muster Rolls screen: a RecordSeries over match time,
// one faction-coloured line per present faction, with a title, a 0 / mid /
// max value axis and an m:ss time axis. Drawn as a uGUI mesh (no textures),
// decimated to MusterRollsPanelConfig.chartMaxPoints per line so a long
// match stays far under the canvas vertex limit.
//
// Labels are TMP children set in Refresh(), never in OnPopulateMesh: text
// changed during a canvas rebuild re-dirties the layout mid-rebuild.

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Diagnostics.MatchRecording;

namespace TheWaningBorder.UI.Ingame
{
    internal sealed class MusterChart : MaskableGraphic
    {
        private const float PadL = 120f, PadR = 28f, PadT = 64f, PadB = 52f;
        private const float LabelSize = 24f;

        private MatchRecord _rec;
        private RecordSeries _series;
        private bool[] _visible;
        private float _lineWidth;
        private int _maxPoints;
        private float _yMax;
        private TMP_Text _title, _y0, _yMid, _yTop, _x0, _xMid, _xEnd;

        public static MusterChart Create(RectTransform parent, string name, string title,
            RecordSeries series, bool[] visible, float lineWidth, int maxPoints)
        {
            var bg = GameUIKit.Image(parent, name, new Color(0.03f, 0.04f, 0.09f, 0.75f));
            var go = new GameObject("lines", typeof(RectTransform));
            go.transform.SetParent(bg.transform, false);
            GameUIKit.Stretch((RectTransform)go.transform);
            var c = go.AddComponent<MusterChart>();
            c.raycastTarget = false;
            c._series = series;
            c._visible = visible;
            c._lineWidth = Mathf.Max(1f, lineWidth);
            c._maxPoints = Mathf.Max(16, maxPoints);

            var rt = bg.rectTransform;
            c._title = GameUIKit.Text(rt, "title", title, 30f, GameUIKit.Gold, TextAlignmentOptions.TopLeft, wrap: false);
            Pin(c._title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(PadL, -PadT + 4f), new Vector2(-PadR, -8f));
            c._yTop = Axis(rt, "ymax", TextAlignmentOptions.Right);
            c._yMid = Axis(rt, "ymid", TextAlignmentOptions.Right);
            c._y0 = Axis(rt, "y0", TextAlignmentOptions.Right);
            c._x0 = Axis(rt, "x0", TextAlignmentOptions.Left);
            c._xMid = Axis(rt, "xmid", TextAlignmentOptions.Center);
            c._xEnd = Axis(rt, "xend", TextAlignmentOptions.Right);
            return c;
        }

        private static TMP_Text Axis(RectTransform parent, string name, TextAlignmentOptions align)
        {
            var t = GameUIKit.Text(parent, name, "", LabelSize, GameUIKit.TextDim, align, wrap: false);
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(220f, 34f);
            return t;
        }

        private static void Pin(RectTransform r, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
        }

        /// <summary>Rebind to a record and redraw (labels + mesh).</summary>
        public void Refresh(MatchRecord rec)
        {
            _rec = rec;
            _yMax = 1f;
            if (rec != null)
            {
                for (int f = 0; f < MatchRecord.Factions; f++)
                {
                    if (!rec.FactionPresent[f] || (_visible != null && !_visible[f])) continue;
                    for (int i = 0; i < rec.SeriesCount; i++)
                        _yMax = Mathf.Max(_yMax, rec.Series(_series, f, i));
                }
            }
            _yMax = NiceCeil(_yMax);
            float dur = rec != null ? Mathf.Max(1f, rec.Duration) : 1f;

            var r = ((RectTransform)transform.parent).rect;
            float plotW = Mathf.Max(10f, r.width - PadL - PadR);
            float plotH = Mathf.Max(10f, r.height - PadT - PadB);
            SetAxis(_yTop, Fmt(_yMax), new Vector2(PadL - 124f, PadB + plotH));
            SetAxis(_yMid, Fmt(_yMax * 0.5f), new Vector2(PadL - 124f, PadB + plotH * 0.5f));
            SetAxis(_y0, "0", new Vector2(PadL - 124f, PadB));
            SetAxis(_x0, "0:00", new Vector2(PadL + 110f, PadB - 26f));
            SetAxis(_xMid, MatchRecord.Clock(dur * 0.5f), new Vector2(PadL + plotW * 0.5f, PadB - 26f));
            SetAxis(_xEnd, MatchRecord.Clock(dur), new Vector2(PadL + plotW - 110f, PadB - 26f));
            SetVerticesDirty();
        }

        private static void SetAxis(TMP_Text t, string s, Vector2 pos)
        {
            t.text = s;
            t.rectTransform.anchoredPosition = pos;
        }

        private bool _relabel;

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            _relabel = true;   // labels move in LateUpdate, never mid-rebuild
        }

        private void LateUpdate()
        {
            if (!_relabel) return;
            _relabel = false;
            if (_rec != null) Refresh(_rec);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float x0 = r.xMin + PadL, y0 = r.yMin + PadB;
            float w = Mathf.Max(10f, r.width - PadL - PadR), h = Mathf.Max(10f, r.height - PadT - PadB);

            // Grid: quarter lines, the base line stronger.
            var grid = new Color(1f, 1f, 1f, 0.08f);
            for (int k = 1; k <= 4; k++)
                Line(vh, new Vector2(x0, y0 + h * k / 4f), new Vector2(x0 + w, y0 + h * k / 4f), 1.5f, grid);
            Line(vh, new Vector2(x0, y0), new Vector2(x0 + w, y0), 2f, new Color(1f, 1f, 1f, 0.25f));
            Line(vh, new Vector2(x0, y0), new Vector2(x0, y0 + h), 2f, new Color(1f, 1f, 1f, 0.25f));

            if (_rec == null || _rec.SeriesCount == 0) return;
            float dur = Mathf.Max(1f, _rec.Duration);
            int n = _rec.SeriesCount;
            int step = Mathf.Max(1, Mathf.CeilToInt(n / (float)_maxPoints));
            var times = _rec.SeriesTimes;

            for (int f = 0; f < MatchRecord.Factions; f++)
            {
                if (!_rec.FactionPresent[f] || (_visible != null && !_visible[f])) continue;
                var col = _rec.FactionColor[f]; col.a = 1f;
                Vector2 prev = default;
                bool have = false;
                for (int i = 0; i < n; i += step)
                {
                    Vector2 p = Point(times[i], _rec.Series(_series, f, i), x0, y0, w, h, dur);
                    if (have) Line(vh, prev, p, _lineWidth, col);
                    prev = p; have = true;
                }
                if (have && (n - 1) % step != 0)
                    Line(vh, prev, Point(times[n - 1], _rec.Series(_series, f, n - 1), x0, y0, w, h, dur), _lineWidth, col);
            }
        }

        private Vector2 Point(float t, float v, float x0, float y0, float w, float h, float dur)
            => new Vector2(x0 + w * Mathf.Clamp01(t / dur), y0 + h * Mathf.Clamp01(v / _yMax));

        internal static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);
            // Extend half a width along the line so consecutive segments overlap at joints.
            Vector2 e = d / len * (width * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n - e, c, Vector4.zero);
            vh.AddVert(a + n - e, c, Vector4.zero);
            vh.AddVert(b + n + e, c, Vector4.zero);
            vh.AddVert(b - n + e, c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static float NiceCeil(float v)
        {
            if (v <= 1f) return 1f;
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(v)));
            float m = v / mag;
            float nice = m <= 1f ? 1f : m <= 2f ? 2f : m <= 2.5f ? 2.5f : m <= 5f ? 5f : 10f;
            return nice * mag;
        }

        internal static string Fmt(float v)
        {
            float a = Mathf.Abs(v);
            if (a >= 1_000_000f) return (v / 1_000_000f).ToString("0.#") + "M";
            if (a >= 10_000f) return (v / 1000f).ToString("0.#") + "k";
            if (a >= 100f || Mathf.Approximately(v, Mathf.Round(v))) return v.ToString("0");
            return v.ToString("0.##");
        }
    }
}
