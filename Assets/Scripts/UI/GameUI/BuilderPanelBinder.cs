// BuilderPanelBinder.cs
// Build palette for the final game UI: shown while a builder (CanBuild
// unit) of the local player is selected. Code-built (no authored prefab
// yet — see GameUIKit), spawned by GameUIManager in the same bottom-right
// slot as the actions panel (the two never show together: a selection is
// either a builder or a building).
//
// The building list comes from EntityActionExtractor.GetActionInfo
// (ActionType.BuildingPlacement), which already applies every gate:
// buildable set, era locks (locked buttons stay visible, greyed), culture
// prefixes, per-faction caps (6 Halls / 1 Temple / 1 Forge / choice-
// building exclusivity) and affordability. Clicking a button enters
// BuilderCommandPanel placement mode (shift-click there repeats
// placement; right-click/Esc cancels).
// Location: Assets/Scripts/UI/GameUI/BuilderPanelBinder.cs

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class BuilderPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const float PanelWidth = 950f;
        private const int GridCols = 4;
        private static readonly Vector2 GridCell = new Vector2(214f, 190f);

        private RectTransform _root;
        private TMP_Text _title;
        private RectTransform _grid;
        private TMP_Text _tooltip;

        private sealed class BuildWidget
        {
            public GameObject Root;
            public Image Bg;
            public RawImage Icon;
            public TMP_Text Label;
            public TMP_Text CostLine;
            public System.Action Click;
            public string Tooltip;
        }

        private readonly List<BuildWidget> _widgets = new List<BuildWidget>();

        private SelectionChangeDetector _detector;
        private float _timer;

        private void Awake()
        {
            _root = GameUIKit.Rect(transform, "BuilderPanel");
            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-510f, 40f);
            _root.sizeDelta = new Vector2(PanelWidth, 400f);

            GameUIKit.PanelChrome(_root);

            var content = GameUIKit.Rect(_root, "content");
            GameUIKit.VStack(content, 20f, 12f);
            var fitter = _root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rootStack = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            rootStack.childControlWidth = true;
            rootStack.childControlHeight = true;
            rootStack.childForceExpandWidth = true;
            rootStack.childForceExpandHeight = false;

            _title = GameUIKit.Text(content, "title", "Build Structure", 40f, GameUIKit.Gold);
            _title.fontStyle = FontStyles.Bold;

            _grid = GameUIKit.Rect(content, "grid");
            var gl = _grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = GridCell;
            gl.spacing = new Vector2(10f, 10f);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = GridCols;

            _tooltip = GameUIKit.Text(content, "tooltip", "", 24f, GameUIKit.TextMain);
            _tooltip.gameObject.SetActive(false);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            bool changed = _detector.Poll();
            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshInterval && !changed) return;
            _timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || GameSettings.IsObserver) { Hide(); return; }
            var em = world.EntityManager;

            var entity = ActionsPanelBinder.FirstOwnedSelected(em);
            if (entity == Entity.Null || !em.HasComponent<CanBuild>(entity)) { Hide(); return; }

            var info = EntityActionExtractor.GetActionInfo(entity, em);
            if (info.Type != ActionType.BuildingPlacement
                || info.Actions == null || info.Actions.Count == 0) { Hide(); return; }

            bool placing = BuilderCommandPanel.IsPlacingBuilding;
            _title.text = placing
                ? "Left-click to place, Right/Esc to cancel"
                : "Build Structure";

            int used = 0;
            foreach (var b in info.Actions)
            {
                var w = used < _widgets.Count ? _widgets[used] : MakeWidget();
                used++;

                bool locked = !b.Enabled || placing;
                bool poor = b.Enabled && !b.CanAfford;

                w.Root.SetActive(true);
                w.Bg.color = locked ? GameUIKit.ButtonBgLocked
                           : poor ? GameUIKit.ButtonBgPoor
                           : GameUIKit.ButtonBg;
                w.Icon.gameObject.SetActive(b.Icon != null);
                if (b.Icon != null) w.Icon.texture = b.Icon;
                w.Label.text = b.Label;
                w.Label.color = locked ? GameUIKit.TextDim : GameUIKit.TextMain;
                w.CostLine.text = b.Cost.IsZero ? "" : UIHelpers.FormatCostRich(b.Cost,
                    EntityActionExtractor.GetFactionResourcesAsCostPublic(
                        em, GameSettings.LocalPlayerFaction));
                w.Tooltip = b.Tooltip;

                string id = b.Id;
                // Era-locked buttons stay visible but inert; while placing,
                // the whole palette is inert until confirm/cancel.
                w.Click = (b.Enabled && !placing)
                    ? (System.Action)(() => BuilderCommandPanel.TriggerBuildingPlacement(id))
                    : null;
            }

            for (int i = used; i < _widgets.Count; i++)
                if (_widgets[i].Root.activeSelf) _widgets[i].Root.SetActive(false);

            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        private void Hide()
        {
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        private BuildWidget MakeWidget()
        {
            var w = new BuildWidget();
            var rt = GameUIKit.Rect(_grid, "build" + _widgets.Count);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(RawImage));
            iconGo.transform.SetParent(rt, false);
            var icon = iconGo.GetComponent<RawImage>();
            icon.raycastTarget = false;
            var iconRt = icon.rectTransform;
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -8f);
            iconRt.sizeDelta = new Vector2(104f, 104f);

            var label = GameUIKit.Text(rt, "label", "", 24f, GameUIKit.TextMain,
                TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = new Vector2(0f, 0.16f);
            label.rectTransform.anchorMax = new Vector2(1f, 0.40f);
            label.rectTransform.offsetMin = new Vector2(6f, 0f);
            label.rectTransform.offsetMax = new Vector2(-6f, 0f);

            var cost = GameUIKit.Text(rt, "cost", "", 20f, GameUIKit.TextDim,
                TextAlignmentOptions.Center, wrap: false);
            cost.rectTransform.anchorMin = new Vector2(0f, 0f);
            cost.rectTransform.anchorMax = new Vector2(1f, 0.16f);
            cost.rectTransform.offsetMin = new Vector2(4f, 2f);
            cost.rectTransform.offsetMax = new Vector2(-4f, 0f);

            var relay = bg.gameObject.AddComponent<UiClickRelay>();
            relay.OnLeftClick = () => w.Click?.Invoke();
            relay.OnEnter = () => ShowTooltip(w.Tooltip);
            relay.OnExit = HideTooltip;

            w.Root = rt.gameObject;
            w.Bg = bg;
            w.Icon = icon;
            w.Label = label;
            w.CostLine = cost;
            _widgets.Add(w);
            return w;
        }

        private void ShowTooltip(string text)
        {
            if (string.IsNullOrEmpty(text)) { HideTooltip(); return; }
            _tooltip.text = text;
            if (!_tooltip.gameObject.activeSelf) _tooltip.gameObject.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltip.gameObject.activeSelf) _tooltip.gameObject.SetActive(false);
        }
    }
}
