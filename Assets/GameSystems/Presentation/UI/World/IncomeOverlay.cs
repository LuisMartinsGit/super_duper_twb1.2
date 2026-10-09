// IncomeOverlay.cs
// WHERE THE INCOME COMES FROM, ON THE MAP (docs/Design/Veilstone_Economy.md).
// Two Synty-framed panels that float over the world like the territory claim
// bars, each shown only while the mouse is on what it describes:
//
//   * TERRITORY — hover ground you hold: what that territory pays per minute
//     (TerritoryIncomeSystem.ComputeYieldForDisplay — the number the income
//     tick pays, so the readout cannot drift from the bank).
//   * BUILDING — hover one of your buildings: what THAT building adds or
//     spends per minute (TerritoryIncomeSystem.BuildingYieldForDisplay).
//
// Own territories and own buildings only, so it reveals nothing the fog hides.
// Hidden while the cursor is over UI. Presentation only.

using System.Text;
using TMPro;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Input;
using TheWaningBorder.Systems.World;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.UI.World
{
    public sealed class IncomeOverlay : MonoBehaviour
    {
        /// <summary>Seconds between content refreshes (placement is per frame).</summary>
        private const float UpdateInterval = 0.25f;
        private const float ReferenceHeight = 1080f;
        private const float PanelWidth = 230f;
        private const float HeaderHeight = 34f;
        private const float LineHeight = 28f;
        private const float Padding = 14f;
        /// <summary>Metres above a building's origin its panel floats.</summary>
        private const float BuildingAnchorHeight = 9f;
        /// <summary>Above the claim bars (45), under the floating health bars (50).</summary>
        private const int CanvasSortingOrder = 46;
        private const float GroundRayLength = 4000f;

        private static readonly Color Gain = new Color(0.62f, 0.95f, 0.55f);
        private static readonly Color Spend = new Color(1f, 0.45f, 0.4f);

        private sealed class Panel
        {
            public RectTransform Root;
            public TMP_Text Title;
            public TMP_Text Lines;
            public Vector3 Anchor;
            public bool Live;
        }

        private RectTransform _canvasRoot;
        private Panel _territory, _building;
        private float _next;
        private int _hoveredTerritory = -1;
        private readonly StringBuilder _sb = new StringBuilder(128);
        /// <summary>One extra line for the territory panel (a Sanctum's RP).</summary>
        private string _extraLine;

        public void Init(GameUICatalog.ChromeSet chrome)
        {
            var go = new GameObject("IncomeOverlay-Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            _canvasRoot = (RectTransform)go.transform;

            _territory = BuildPanel("TerritoryIncome", chrome);
            _building = BuildPanel("BuildingIncome", chrome);
        }

        private void LateUpdate()
        {
            if (_canvasRoot == null) return;

            // The cursor on a UI panel is not on the map.
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi || GameSettings.IsSpectating)
            {
                Hide(_territory);
                Hide(_building);
                return;
            }

            _hoveredTerritory = TerritoryUnderMouse();

            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + UpdateInterval;
                Refresh();
            }
            Place(_territory);
            Place(_building);
        }

        // ── What to show ────────────────────────────────────────────────

        private void Refresh()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || !RegionMap.Ready || !TerritoryOwnership.Ready)
            {
                Hide(_territory);
                Hide(_building);
                return;
            }
            var em = world.EntityManager;
            var me = GameSettings.LocalPlayerFaction;

            // Territory: ground the local player holds.
            int t = _hoveredTerritory;
            if (t >= 0 && TerritoryOwnership.OwnerOf(t) == (int)me
                && TerritoryCentres.TryGet(t, out var centre))
            {
                var y = TerritoryIncomeSystem.ComputeYieldForDisplay(em, t, me);
                _extraLine = TerritoryResources.IsSanctum(t)
                    ? Loc("Sanctum") + ":  <color=#C9A8FF>+1</color> " + Loc("Religion Point") + " <size=75%>/min</size>"
                    : null;
                Show(_territory, RegionMap.NameOf(t), y, centre, Loc("Nothing — build on it"));
                _extraLine = null;
            }
            else Hide(_territory);

            // Building: one of the local player's, finished.
            var e = RTSInputManager.HoveredEntity;
            if (e != Entity.Null && em.Exists(e) && em.HasComponent<BuildingTag>(e)
                && em.HasComponent<FactionTag>(e) && em.GetComponentData<FactionTag>(e).Value == me
                && em.HasComponent<LocalTransform>(e) && !em.HasComponent<UnderConstruction>(e))
            {
                var y = TerritoryIncomeSystem.BuildingYieldForDisplay(em, e);
                if (y.IsEmpty) Hide(_building);   // a barracks earns nothing — say nothing
                else
                {
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    string id = TheWaningBorder.Entities.BuildingIds.Of(e, em);
                    string name = string.IsNullOrEmpty(id) ? "" : DisplayNames.ForBuilding(id);
                    Show(_building, name, y, new Vector3(p.x, p.y + BuildingAnchorHeight, p.z), null);
                }
            }
            else Hide(_building);
        }

        private void Show(Panel panel, string title, TerritoryYield y, Vector3 anchor, string emptyLine)
        {
            _sb.Clear();
            int lines = 0;
            lines += Line(ResourceIcons.Supplies, "S", y.Supplies);
            lines += Line(ResourceIcons.Iron, "Fe", y.Iron);
            lines += Line(ResourceIcons.Veilstone, "Vs", y.Veilstone);
            lines += Line(ResourceIcons.Veilsteel, "St", y.Veilsteel);
            if (_extraLine != null)
            {
                if (_sb.Length > 0) _sb.Append('\n');
                _sb.Append(_extraLine);
                lines++;
            }
            if (lines == 0)
            {
                if (emptyLine == null) { Hide(panel); return; }
                _sb.Append(emptyLine);
                lines = 1;
            }

            panel.Title.text = title;
            panel.Lines.text = _sb.ToString();
            panel.Root.sizeDelta = new Vector2(PanelWidth, HeaderHeight + lines * LineHeight + Padding);
            panel.Anchor = anchor;
            panel.Live = true;
            if (!panel.Root.gameObject.activeSelf) panel.Root.gameObject.SetActive(true);
        }

        /// <summary>"[icon] +190 /min", green for income, red for spending.
        /// Zero lines are left out. Returns 1 when a line was written.</summary>
        private int Line(string resource, string fallback, float perMinute)
        {
            int v = Mathf.RoundToInt(perMinute);
            if (v == 0) return 0;
            if (_sb.Length > 0) _sb.Append('\n');
            string icon = ResourceIcons.Tag(resource) ?? fallback;
            var c = v > 0 ? Gain : Spend;
            _sb.Append(icon).Append("  <color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>')
               .Append(v > 0 ? "+" : "").Append(v).Append("</color>")
               .Append(" <size=75%>").Append(Loc("/min")).Append("</size>");
            return 1;
        }

        private static void Hide(Panel panel)
        {
            if (panel == null) return;
            panel.Live = false;
            if (panel.Root.gameObject.activeSelf) panel.Root.gameObject.SetActive(false);
        }

        private static string Loc(string en) => TheWaningBorder.Core.Localization.Loc.T(en);

        // ── Where to show it ────────────────────────────────────────────

        private void Place(Panel panel)
        {
            if (panel == null || !panel.Live) return;
            var cam = PresentationState.GameplayCamera;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(panel.Anchor);
            var cg = panel.Root.GetComponent<CanvasGroup>();
            bool visible = sp.z > 0f;
            cg.alpha = visible ? 1f : 0f;
            if (!visible) return;
            panel.Root.position = new Vector3(sp.x, sp.y, 0f);
            panel.Root.localScale = Vector3.one * Mathf.Max(0.5f, Screen.height / ReferenceHeight);
        }

        /// <summary>The territory the cursor's ground point lies in, or -1.</summary>
        private static int TerritoryUnderMouse()
        {
            var cam = PresentationState.GameplayCamera;
            if (cam == null) return -1;
            var ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            Vector3 point;
            if (Physics.Raycast(ray, out var hit, GroundRayLength)) point = hit.point;
            else
            {
                // No collider under the cursor: fall back to the ground plane.
                var plane = new Plane(Vector3.up, Vector3.zero);
                if (!plane.Raycast(ray, out float d)) return -1;
                point = ray.GetPoint(d);
            }
            int t = RegionMap.RegionAt(point.x, point.z);
            return t == RegionMap.None ? -1 : t;
        }

        // ── Building a panel ────────────────────────────────────────────

        private Panel BuildPanel(string name, GameUICatalog.ChromeSet chrome)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_canvasRoot, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);   // stands ON the anchor point
            rt.sizeDelta = new Vector2(PanelWidth, HeaderHeight + LineHeight + Padding);
            var cg = go.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            // Synty Frame_Box_Medium_05: the teal mask fill under the gold frame,
            // the same chrome the authored in-game panels use.
            if (chrome != null && chrome.panelFrameMask != null)
            {
                var fill = MakeImage(rt, "Fill", chrome.panelFrameMask, chrome.panelFill);
                fill.type = Image.Type.Sliced;
                fill.pixelsPerUnitMultiplier = chrome.panelSlice > 0f ? chrome.panelSlice : 1f;
            }
            else MakeImage(rt, "Fill", null, new Color(0.04f, 0.12f, 0.14f, 0.88f));
            if (chrome != null && chrome.panelFrame != null)
            {
                var frame = MakeImage(rt, "Frame", chrome.panelFrame, Color.white);
                frame.type = Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier = chrome.panelSlice > 0f ? chrome.panelSlice : 1f;
            }

            var title = GameUIKit.Text(rt, "Title", "", 19f, new Color(1f, 0.86f, 0.55f),
                TextAlignmentOptions.Center, wrap: false);
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -8f);
            trt.sizeDelta = new Vector2(-16f, HeaderHeight - 6f);
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;

            var lines = GameUIKit.Text(rt, "Lines", "", 20f, Color.white,
                TextAlignmentOptions.TopLeft, wrap: false);
            var lrt = lines.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(22f, Padding * 0.5f);
            lrt.offsetMax = new Vector2(-16f, -HeaderHeight);
            lines.lineSpacing = 6f;
            lines.raycastTarget = false;

            go.SetActive(false);
            return new Panel { Root = rt, Title = title, Lines = lines };
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
