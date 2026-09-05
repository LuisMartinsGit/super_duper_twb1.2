// ColorPickerPopup.cs
// Runtime-built 12-swatch colour picker for lobby roster rows.
// Canonical spec: docs/Design/Lobby_Setup.md

using System;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    /// <summary>
    /// A grid of the twelve pool colours, opened from a roster row's colour
    /// swatch. Replaces the old click-to-cycle strip, which took up to eleven
    /// clicks to reach a specific colour and never showed what was available.
    ///
    /// Built entirely at RUNTIME rather than added to MenuPanelsBuilder,
    /// because the menu scene already contains its built hierarchy and the
    /// builder refuses to overwrite an existing panel — a builder-only widget
    /// would never appear in the running game. Follows the same
    /// construct-it-yourself precedent as SkirmishPanel.CreateObserverPill.
    ///
    /// Sizes derive from the widget it was opened from, never from literals:
    /// the menu scene was rescaled 2x after it was built, so hard-coded pixel
    /// constants render at half the size of their neighbours.
    /// </summary>
    public sealed class ColorPickerPopup : MonoBehaviour
    {
        private const int Columns = 4;

        private Action<int> _onPick;
        private Func<int, bool> _isTaken;
        private RectTransform _panel;

        /// <summary>
        /// Open a picker over <paramref name="owner"/>'s root canvas.
        /// </summary>
        /// <param name="anchorTo">Widget the picker positions itself beside — normally the row's colour swatch.</param>
        /// <param name="currentIndex">Pool index currently held by this slot.</param>
        /// <param name="isTaken">True for pool indices another slot already owns; those are shown locked.</param>
        /// <param name="onPick">Called with the chosen pool index. Not called on cancel.</param>
        public static ColorPickerPopup Open(Component owner, RectTransform anchorTo,
                                            int currentIndex, Func<int, bool> isTaken,
                                            Action<int> onPick)
        {
            if (owner == null) return null;
            var canvas = owner.GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            // Only one picker at a time.
            var existing = canvas.GetComponentInChildren<ColorPickerPopup>(true);
            if (existing != null) Destroy(existing.gameObject);

            var rootGo = new GameObject("ColorPickerPopup",
                typeof(RectTransform), typeof(Image), typeof(Button));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(canvas.transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();

            var popup = rootGo.AddComponent<ColorPickerPopup>();
            popup._onPick = onPick;
            popup._isTaken = isTaken;

            // Full-screen scrim: dims the menu and, more importantly, swallows
            // the click that dismisses the picker.
            var scrim = rootGo.GetComponent<Image>();
            scrim.color = new Color(0.02f, 0.045f, 0.06f, 0.55f);
            scrim.raycastTarget = true;
            rootGo.GetComponent<Button>().onClick.AddListener(popup.Cancel);

            popup.Build(anchorTo, currentIndex);
            return popup;
        }

        private void Build(RectTransform anchorTo, int currentIndex)
        {
            // Derive the swatch size from the widget we were opened from so we
            // match whatever scale the scene is authored at.
            float unit = 36f;
            if (anchorTo != null)
            {
                float h = anchorTo.rect.height;
                if (h > 1f) unit = h;
            }
            float cell = unit * 0.9f;
            float pad = unit * 0.25f;
            float labelH = unit * 0.55f;

            var panelGo = new GameObject("Panel",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            _panel = (RectTransform)panelGo.transform;
            _panel.SetParent(transform, false);

            var bg = panelGo.GetComponent<Image>();
            bg.color = new Color(0.06f, 0.10f, 0.13f, 0.98f);
            bg.raycastTarget = true;   // don't let clicks fall through to the scrim

            var outline = panelGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.910f, 0.722f, 0.290f, 0.75f);
            outline.effectDistance = new Vector2(2f, 2f);

            var vlg = panelGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            vlg.spacing = pad * 0.5f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = panelGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            MakeLabel(_panel, "Title", Loc.T("PLAYER COLOUR"), labelH,
                      new Color(0.910f, 0.722f, 0.290f));

            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
            var grid = gridGo.GetComponent<GridLayoutGroup>();
            gridGo.transform.SetParent(_panel, false);
            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(pad * 0.4f, pad * 0.4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;

            var gridLe = gridGo.AddComponent<LayoutElement>();
            int rows = Mathf.CeilToInt(FactionColors.ColorCount / (float)Columns);
            gridLe.preferredHeight = rows * cell + (rows - 1) * pad * 0.4f;
            gridLe.preferredWidth = Columns * cell + (Columns - 1) * pad * 0.4f;

            for (int i = 0; i < FactionColors.ColorCount; i++)
                MakeSwatch(gridGo.transform, i, currentIndex, cell);

            // Name of the hovered/selected colour, so the grid is not a wall of
            // unlabelled squares.
            _nameLabel = MakeLabel(_panel, "ColorName",
                Loc.T(FactionColors.ColorNames[Mathf.Clamp(currentIndex, 0, FactionColors.ColorCount - 1)]),
                labelH, new Color(0.85f, 0.87f, 0.88f));

            PositionBeside(anchorTo);
        }

        private TMP_Text _nameLabel;

        private void MakeSwatch(Transform parent, int index, int currentIndex, float cell)
        {
            bool taken = _isTaken != null && _isTaken(index) && index != currentIndex;

            var go = new GameObject($"Swatch{index}",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = FactionColors.ColorPool[index];
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = !taken;

            if (index == currentIndex)
            {
                var sel = go.AddComponent<Outline>();
                sel.effectColor = Color.white;
                sel.effectDistance = new Vector2(3f, 3f);
            }

            if (taken)
            {
                // A bar across the swatch reads as "locked" without needing a
                // second colour that might clash with the swatch itself.
                var barGo = new GameObject("Taken", typeof(RectTransform), typeof(Image));
                var barRt = (RectTransform)barGo.transform;
                barRt.SetParent(go.transform, false);
                barRt.anchorMin = new Vector2(0.08f, 0.44f);
                barRt.anchorMax = new Vector2(0.92f, 0.56f);
                barRt.offsetMin = barRt.offsetMax = Vector2.zero;
                var barImg = barGo.GetComponent<Image>();
                barImg.color = new Color(0f, 0f, 0f, 0.75f);
                barImg.raycastTarget = false;
            }
            else
            {
                int captured = index;
                btn.onClick.AddListener(() =>
                {
                    _onPick?.Invoke(captured);
                    Close();
                });

                // Hover updates the name label.
                var trigger = go.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ =>
                {
                    if (_nameLabel != null)
                        _nameLabel.text = Loc.T(FactionColors.ColorNames[captured]).ToUpperInvariant();
                });
                trigger.triggers.Add(entry);
            }
        }

        private static TMP_Text MakeLabel(Transform parent, string name, string text,
                                          float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text.ToUpperInvariant();
            label.fontSize = height * 0.55f;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return label;
        }

        /// <summary>
        /// Place the panel next to the swatch that opened it, then pull it back
        /// inside the canvas if it would hang off an edge.
        /// </summary>
        private void PositionBeside(RectTransform anchorTo)
        {
            if (_panel == null) return;

            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0f, 0.5f);

            if (anchorTo == null)
            {
                _panel.pivot = new Vector2(0.5f, 0.5f);
                _panel.anchoredPosition = Vector2.zero;
                return;
            }

            var self = (RectTransform)transform;
            Vector2 local;
            var world = anchorTo.TransformPoint(
                new Vector3(anchorTo.rect.xMax, anchorTo.rect.center.y, 0f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                self, RectTransformUtility.WorldToScreenPoint(null, world), null, out local);

            _panel.anchoredPosition = local + new Vector2(12f, 0f);
            // Layout has not run yet, so clamp on the next frame when the
            // ContentSizeFitter has produced a real size.
            _clampPending = true;
        }

        private bool _clampPending;

        private void LateUpdate()
        {
            if (_clampPending && _panel != null)
            {
                _clampPending = false;
                ClampInsideCanvas();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }

        private void ClampInsideCanvas()
        {
            var self = (RectTransform)transform;
            Vector2 half = self.rect.size * 0.5f;
            Vector2 size = _panel.rect.size;
            Vector2 pos = _panel.anchoredPosition;

            // Pivot is (0, 0.5): the panel spans [pos.x, pos.x + width].
            if (pos.x + size.x > half.x)
            {
                // Flip to the other side of the swatch rather than overlapping it.
                pos.x = Mathf.Max(-half.x, pos.x - size.x - 24f);
            }
            pos.y = Mathf.Clamp(pos.y, -half.y + size.y * 0.5f, half.y - size.y * 0.5f);
            _panel.anchoredPosition = pos;
        }

        private void Cancel() => Close();

        private void Close()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
