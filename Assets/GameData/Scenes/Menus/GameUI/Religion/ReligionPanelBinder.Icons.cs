// ReligionPanelBinder.Icons.cs
// The religion menu's PICTOGRAMS (docs/Design/Religion_Menu.md, 2026-11-15:
// "all buttons should be pictographic").
//
// No sect or sect power has authored icon art yet, so the menu draws its own:
// each pictogram is a handful of strokes on a 24-unit grid, rasterised once
// into a white sprite that the button tints. They are stand-ins -- when a
// power gets real art the lookup below is the one place to swap it in.
//
// Stroke language, ';' between strokes, y grows downward:
//   P x y x y ...     a polyline
//   C cx cy r         a circle outline
//   E cx cy rx ry     an ellipse outline

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private const int IconPixels = 96;
        private const float IconGrid = 24f;
        private const float IconStroke = 1.0f;   // half width, grid units

        private static readonly Dictionary<string, string> IconStrokes = new Dictionary<string, string>
        {
            // ── chrome ──
            ["temple"]   = "P 3 9 12 4 21 9;P 5 9 5 18;P 9 9 9 18;P 15 9 15 18;P 19 9 19 18;P 3 20 21 20",
            ["hide"]     = "P 9 5 16 12 9 19",
            ["show"]     = "P 15 5 8 12 15 19",
            ["star"]     = "P 12 3 14.7 8.6 20.8 9.4 16.3 13.6 17.4 19.6 12 16.6 6.6 19.6 7.7 13.6 3.2 9.4 9.3 8.6 12 3",
            ["tithe"]    = "P 10 5 12 9.2 16.6 9.8 13.2 13 14 17.5 10 15.2 6 17.5 6.8 13 3.4 9.8 8 9.2 10 5;P 19 3 19 9;P 16 6 22 6",
            ["plus"]     = "P 12 5 12 19;P 5 12 19 12",
            ["building"] = "P 4 20 4 10 12 4 20 10 20 20 4 20;P 10 20 10 14 14 14 14 20",
            ["hero"]     = "P 4 18 20 18;P 4 18 3 8 8 12 12 5 16 12 21 8 20 18",

            // ── sect emblems (keyed by the sect id without "Sect_") ──
            ["Antiquity"]  = "P 6 3 15 3 18 6 18 21 6 21 6 3;P 9 9 15 9;P 9 13 15 13;P 9 17 12 17",
            ["Prosperity"] = "P 12 4 12 20;P 7 20 17 20;P 5 8 19 8;P 5 8 2.5 14 7.5 14 5 8;P 19 8 16.5 14 21.5 14 19 8",
            ["Renewal"]    = "P 12 21 12 10;P 12 10 13 6.5 18 4 17 8 12 10;P 12 14 10.5 11 7 9.5 8 13 12 14",
            ["Fortitude"]  = "P 4 20 4 8 7 8 7 11 10 11 10 8 14 8 14 11 17 11 17 8 20 8 20 20 4 20",

            // ── powers (keyed by the power's name in its spec) ──
            ["Embargo"]           = "P 6 11 18 11 18 20 6 20 6 11;P 9 11 9 8 10 5.8 12 5 14 5.8 15 8 15 11",
            ["Trespass"]          = "P 5 21 5 4;P 5 5 16 5 14 8.5 16 12 5 12",
            ["Friendly Fire"]     = "P 5 4 16 15;P 19 4 8 15;P 14 17 17 20;P 10 17 7 20;P 16 13 18 15;P 8 13 6 15",
            ["Overtime"]          = "P 7 3 17 3;P 7 21 17 21;P 8 3 8 7 16 17 16 21;P 16 3 16 7 8 17 8 21",
            ["Bounty"]            = "C 12 12 7;C 12 12 2;P 12 2 12 5;P 12 19 12 22;P 2 12 5 12;P 19 12 22 12",
            ["Plenty"]            = "E 12 7 7 3;P 5 7 5 12;P 19 7 19 12;P 5 12 7 14.2 12 15 17 14.2 19 12;P 5 12 5 17;P 19 12 19 17;P 5 17 7 19.2 12 20 17 19.2 19 17",
            ["Heal"]              = "P 12 20 5 13 4.5 9.5 6.5 7 9.5 7 12 9 14.5 7 17.5 7 19.5 9.5 19 13 12 20;P 12 11 12 15;P 10 13 14 13",
            ["Lifeline"]          = "P 12 3 19 6 19 11 17.5 16 12 21 6.5 16 5 11 5 6 12 3",
            ["Revive"]            = "P 12 19 12 6;P 7 11 12 6 17 11;P 5 21 19 21",
            ["Emergency Repairs"] = "P 14 4 20 10 17 13 11 7 14 4;P 12.5 8.5 4 17 7 20 15.5 11.5",
            ["Ward Discharge"]    = "C 12 12 2.5;P 12 3 12 6;P 12 18 12 21;P 3 12 6 12;P 18 12 21 12;P 5.6 5.6 7.7 7.7;P 16.3 16.3 18.4 18.4;P 18.4 5.6 16.3 7.7;P 7.7 16.3 5.6 18.4",
            ["Fortify"]           = "P 6 21 6 9 5 8 5 4 8 4 8 6 10.5 6 10.5 4 13.5 4 13.5 6 16 6 16 4 19 4 19 8 18 9 18 21 6 21;P 10 21 10 16 14 16 14 21",
        };

        /// <summary>Names that share another entry's pictogram.</summary>
        private static readonly Dictionary<string, string> IconAlias = new Dictionary<string, string>
        {
            ["Heal Circle"] = "Heal",
            ["Raise Anew"] = "Fortify",
        };

        private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>();

        /// <summary>The pictogram for a key, or null when none is drawn for it
        /// (the caller then falls back to the name in text).</summary>
        private static Sprite Icon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (IconAlias.TryGetValue(key, out var alias)) key = alias;
            if (IconCache.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!IconStrokes.TryGetValue(key, out var strokes)) return null;
            var sprite = Rasterise(strokes);
            sprite.name = "ReligionIcon_" + key;
            IconCache[key] = sprite;
            return sprite;
        }

        /// <summary>The emblem of a sect ("Sect_Antiquity" -> "Antiquity").</summary>
        private static Sprite SectEmblem(string sectId)
        {
            const string p = "Sect_";
            if (string.IsNullOrEmpty(sectId)) return null;
            return Icon(sectId.StartsWith(p) ? sectId.Substring(p.Length) : sectId);
        }

        private static Sprite Rasterise(string strokes)
        {
            // Every stroke becomes line segments in grid units.
            var segs = new List<Vector4>(48);
            foreach (var raw in strokes.Split(';'))
            {
                var t = raw.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 2) continue;
                var n = new float[t.Length - 1];
                for (int i = 1; i < t.Length; i++)
                    n[i - 1] = float.Parse(t[i], CultureInfo.InvariantCulture);
                switch (t[0])
                {
                    case "P":
                        for (int i = 0; i + 3 < n.Length; i += 2)
                            segs.Add(new Vector4(n[i], n[i + 1], n[i + 2], n[i + 3]));
                        break;
                    case "C":
                        AddEllipse(segs, n[0], n[1], n[2], n[2]);
                        break;
                    case "E":
                        AddEllipse(segs, n[0], n[1], n[2], n[3]);
                        break;
                }
            }

            float px = IconPixels / IconGrid;
            var pixels = new Color32[IconPixels * IconPixels];
            for (int y = 0; y < IconPixels; y++)
            for (int x = 0; x < IconPixels; x++)
            {
                // Pixel centre in grid units; the texture's row 0 is the bottom.
                float gx = (x + 0.5f) / px;
                float gy = IconGrid - (y + 0.5f) / px;
                float best = float.MaxValue;
                for (int s = 0; s < segs.Count; s++)
                {
                    var v = segs[s];
                    float dx = v.z - v.x, dy = v.w - v.y;
                    float len = dx * dx + dy * dy;
                    float u = len > 1e-6f ? Mathf.Clamp01(((gx - v.x) * dx + (gy - v.y) * dy) / len) : 0f;
                    float ex = v.x + u * dx - gx, ey = v.y + u * dy - gy;
                    float d = ex * ex + ey * ey;
                    if (d < best) best = d;
                }
                float a = Mathf.Clamp01((IconStroke - Mathf.Sqrt(best)) * px + 0.5f);
                pixels[y * IconPixels + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            var tex = new Texture2D(IconPixels, IconPixels, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, IconPixels, IconPixels), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void AddEllipse(List<Vector4> segs, float cx, float cy, float rx, float ry)
        {
            const int n = 28;
            float px = cx + rx, py = cy;
            for (int i = 1; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float x = cx + Mathf.Cos(a) * rx, y = cy + Mathf.Sin(a) * ry;
                segs.Add(new Vector4(px, py, x, y));
                px = x; py = y;
            }
        }

        /// <summary>A centred pictogram inside a button; hidden when the key has none.</summary>
        private static UnityEngine.UI.Image IconImage(Transform parent, string key, float size, Color color)
        {
            var img = GameUIKit.Image(parent, "Icon", color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            SetIcon(img, key);
            return img;
        }

        private static bool SetIcon(UnityEngine.UI.Image img, string key)
        {
            var s = Icon(key);
            img.sprite = s;
            img.enabled = s != null;
            return s != null;
        }
    }
}
