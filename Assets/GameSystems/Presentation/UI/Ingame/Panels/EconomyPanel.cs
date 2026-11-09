// EconomyPanel.cs
// THE ECONOMY, PER TERRITORY (2026-11-09; replaces the floating IncomeOverlay
// panels that hovered over the world). A collapsible screen panel on the left:
// its header button opens and closes it; open, it lists every territory the
// local player holds with what that territory pays per minute — supplies, iron,
// veilstone (TerritoryIncomeSystem.ComputeYieldForDisplay, the number the income
// tick pays, so the readout cannot drift from the bank) — the share its Trading
// Outposts trickle (TerritoryTradeForDisplay), and a total.
//
// Own territories only, so it reveals nothing the fog hides. Presentation only.

using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Systems.World;
using TheWaningBorder.UI.Common;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class EconomyPanel : MonoBehaviour
    {
        /// <summary>Seconds between content refreshes while open.</summary>
        private const float UpdateInterval = 0.5f;
        private const float PanelWidth = 430f;
        private const float HeaderHeight = 38f;
        private const float LineHeight = 25f;
        private const float Padding = 16f;
        /// <summary>The panel's top-left corner, from the screen's top-left.</summary>
        private static readonly Vector2 Corner = new Vector2(14f, -150f);
        /// <summary>Most territory rows shown before "+N more".</summary>
        private const int MaxRows = 18;
        private const int CanvasSortingOrder = 40;

        private RectTransform _root, _body;
        private TMP_Text _header, _lines;
        private bool _open;
        private float _next;
        private readonly StringBuilder _sb = new StringBuilder(1024);
        private readonly List<(string Name, TerritoryYield Y, TerritoryYield Posts)> _rows =
            new List<(string, TerritoryYield, TerritoryYield)>();

        public void Init(GameUICatalog.ChromeSet chrome)
        {
            var go = new GameObject("EconomyPanel-Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var canvasRoot = (RectTransform)go.transform;

            // The header: a framed button that opens and closes the body.
            _root = NewRect("EconomyPanel", canvasRoot);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Corner;
            _root.sizeDelta = new Vector2(PanelWidth, HeaderHeight);
            Frame(_root, chrome);
            var button = _root.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => { _open = !_open; _next = 0f; Layout(); });
            _header = GameUIKit.Text(_root, "Header", "", 20f, new Color(1f, 0.86f, 0.55f),
                TextAlignmentOptions.Left, wrap: false);
            Stretch(_header.rectTransform, new Vector2(16f, 0f), new Vector2(-12f, 0f));
            _header.fontStyle = FontStyles.Bold;
            _header.raycastTarget = false;

            // The body, under the header.
            _body = NewRect("Body", _root);
            _body.anchorMin = new Vector2(0f, 0f);
            _body.anchorMax = new Vector2(1f, 0f);
            _body.pivot = new Vector2(0f, 1f);
            _body.anchoredPosition = new Vector2(0f, -4f);
            Frame(_body, chrome);
            _lines = GameUIKit.Text(_body, "Lines", "", 18f, Color.white, TextAlignmentOptions.TopLeft, wrap: false);
            Stretch(_lines.rectTransform, new Vector2(18f, Padding * 0.5f), new Vector2(-14f, -Padding * 0.6f));
            _lines.lineSpacing = 4f;
            _lines.raycastTarget = false;

            Layout();
        }

        private void Layout()
        {
            if (_header != null)
                _header.text = (_open ? "▾  " : "▸  ") + Loc.T("Economy per territory");
            if (_body != null) _body.gameObject.SetActive(_open);
        }

        private void Update()
        {
            if (_root == null) return;
            bool show = !GameSettings.IsSpectating;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show || !_open) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + UpdateInterval;
            Refresh();
        }

        private void Refresh()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            _sb.Clear();
            int lines = 0;
            if (world == null || !world.IsCreated || !RegionMap.Ready || !TerritoryOwnership.Ready)
            {
                _sb.Append(Loc.T("No territory yet."));
                lines = 1;
            }
            else
            {
                var em = world.EntityManager;
                var me = GameSettings.LocalPlayerFaction;
                _rows.Clear();
                var total = new TerritoryYield();
                var totalPosts = new TerritoryYield();
                foreach (int t in TerritoryOwnership.TerritoriesOf(me))
                {
                    var y = TerritoryIncomeSystem.ComputeYieldForDisplay(em, t, me);
                    TerritoryIncomeSystem.TerritoryTradeForDisplay(em, t, me, out var posts);
                    _rows.Add((RegionMap.NameOf(t), y, posts));
                    Add(ref total, y); Add(ref totalPosts, posts);
                }
                // Richest first: the player reads where the money is.
                _rows.Sort((a, b) => Sum(b.Y, b.Posts).CompareTo(Sum(a.Y, a.Posts)));

                string s = ResourceIcons.Tag(ResourceIcons.Supplies) ?? "S";
                string i = ResourceIcons.Tag(ResourceIcons.Iron) ?? "Fe";
                string v = ResourceIcons.Tag(ResourceIcons.Veilstone) ?? "Vs";
                _sb.Append("<color=#BFB59A><size=85%>").Append(Loc.T("Per minute"))
                   .Append("<pos=48%>").Append(s).Append("<pos=66%>").Append(i).Append("<pos=84%>").Append(v)
                   .Append("</size></color>\n");
                lines++;
                if (_rows.Count == 0)
                {
                    _sb.Append(Loc.T("No territory yet.")).Append('\n');
                    lines++;
                }
                for (int r = 0; r < _rows.Count && r < MaxRows; r++)
                {
                    var row = _rows[r];
                    Row(Trim(row.Name), Plus(row.Y, row.Posts), false);
                    lines++;
                    if (!row.Posts.IsEmpty)
                    {
                        Row("   " + Loc.T("of which Trading Outposts"), row.Posts, true);
                        lines++;
                    }
                }
                if (_rows.Count > MaxRows)
                {
                    _sb.Append("<color=#9A9A9A>").Append(string.Format(Loc.T("+{0} more"), _rows.Count - MaxRows))
                       .Append("</color>\n");
                    lines++;
                }
                _sb.Append("<color=#E8C77A><b>");
                Row(Loc.T("Total"), Plus(total, totalPosts), false);
                _sb.Append("</b></color>");
                lines++;
            }
            _lines.text = _sb.ToString();
            _body.sizeDelta = new Vector2(0f, lines * LineHeight + Padding * 1.5f);
        }

        private void Row(string name, TerritoryYield y, bool dim)
        {
            if (dim) _sb.Append("<color=#9FC7C2><size=85%>");
            _sb.Append(name)
               .Append("<pos=48%>").Append(Mathf.RoundToInt(y.Supplies))
               .Append("<pos=66%>").Append(Mathf.RoundToInt(y.Iron))
               .Append("<pos=84%>").Append(Mathf.RoundToInt(y.Veilstone));
            if (dim) _sb.Append("</size></color>");
            _sb.Append('\n');
        }

        private static string Trim(string name)
            => string.IsNullOrEmpty(name) ? "?" : name.Length > 20 ? name.Substring(0, 19) + "…" : name;

        private static float Sum(TerritoryYield a, TerritoryYield b)
            => a.Supplies + a.Iron + a.Veilstone + b.Supplies + b.Iron + b.Veilstone;

        private static TerritoryYield Plus(TerritoryYield a, TerritoryYield b) => new TerritoryYield
        {
            Supplies = a.Supplies + b.Supplies, Iron = a.Iron + b.Iron, Veilstone = a.Veilstone + b.Veilstone,
        };

        private static void Add(ref TerritoryYield into, TerritoryYield y)
        {
            into.Supplies += y.Supplies; into.Iron += y.Iron; into.Veilstone += y.Veilstone;
        }

        // ── building blocks ─────────────────────────────────────────────

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>Synty Frame_Box_Medium_05: the teal mask fill under the gold
        /// frame, the chrome the authored in-game panels use.</summary>
        private static void Frame(RectTransform rt, GameUICatalog.ChromeSet chrome)
        {
            Image fill;
            if (chrome != null && chrome.panelFrameMask != null)
            {
                fill = MakeImage(rt, "Fill", chrome.panelFrameMask, chrome.panelFill);
                fill.type = Image.Type.Sliced;
                fill.pixelsPerUnitMultiplier = chrome.panelSlice > 0f ? chrome.panelSlice : 1f;
            }
            else fill = MakeImage(rt, "Fill", null, new Color(0.04f, 0.12f, 0.14f, 0.92f));
            fill.raycastTarget = true;   // the header button's hit area
            if (chrome != null && chrome.panelFrame != null)
            {
                var frame = MakeImage(rt, "Frame", chrome.panelFrame, Color.white);
                frame.type = Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier = chrome.panelSlice > 0f ? chrome.panelSlice : 1f;
            }
        }

        private static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
