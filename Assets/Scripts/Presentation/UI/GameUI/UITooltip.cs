// UITooltip.cs
// ONE hover tooltip for the whole in-game UI.
//
// Before this, every panel grew its own answer to "explain this button":
// ActionsPanel* drew a text block INSIDE their own layout, the religion panel
// pushed descriptions through the notification line, and the rest of the HUD
// (culture pill, special-building cluster, upgrade button, formations, spells,
// minimap) explained nothing at all.
//
// The inline ones were also a flicker generator, which is the reason this file
// resolves hovers the way it does. Those panels put the tooltip label inside a
// VerticalLayoutGroup under a ContentSizeFitter root pivoted at its bottom
// edge, so showing it GREW THE PANEL UPWARD and shifted every button in the
// grid. The button under the cursor moved out from under it, pointer-exit
// fired, the label hid, the panel shrank, the button came back — several times
// a second. A tooltip must never reflow the thing it is describing.
//
// So: the tooltip is its OWN canvas at sortingOrder 5000, with no
// GraphicRaycaster and no raycast-target graphics, and it is placed clear of
// the cursor. It cannot move, cover, or steal input from whatever it explains.
//
// It is also POLLED, not event-driven. Hover enter/exit is the wrong signal
// for this job in two ways:
//   * moving from a parent widget onto a child that has its own tooltip fires
//     exit on neither and enter on neither (Unity keeps common ancestors
//     "entered"), so an event-driven tooltip loses its text and never gets it
//     back until the pointer leaves the whole widget;
//   * any layout churn turns exit/enter into a loop, as above.
// Each frame this resolves the pointer against the UI, walks up from whatever
// it hit to the nearest UITooltipSource, and shows that. Nothing to get out of
// sync, nothing to ping-pong.
//
// Usage: UITooltip.Bind(go, () => "text") — that is the whole API.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheWaningBorder.UI.GameUI
{
    /// <summary>
    /// Marks a GameObject as having a tooltip. The text is pulled through a
    /// callback on every refresh, so costs, cooldowns and lock reasons are
    /// current rather than whatever they were when the widget was built.
    /// </summary>
    internal sealed class UITooltipSource : MonoBehaviour
    {
        public System.Func<string> Text;
    }

    internal sealed class UITooltip : MonoBehaviour
    {
        // Canvas units (the game UI canvas is 3840x2160), so ~0.5x these in
        // screen pixels at 1080p.
        private const float MaxWidth = 760f;
        private const float FontSize = 26f;
        /// <summary>Clearance from the pointer. Must exceed the cursor
        /// GRAPHIC, not just its hotspot — the arrow extends ~20 screen px
        /// down-right of the point it reports, and a tooltip tucked under it
        /// reads as the cursor having eaten the text.</summary>
        private const float CursorGap = 56f;
        /// <summary>Hover resolution rate. The box follows the cursor every
        /// frame; only the raycast + text refresh run on this interval.</summary>
        private const float ResolveInterval = 1f / 30f;

        private static UITooltip _instance;
        private static bool _quitting;

        private RectTransform _canvasRect;
        private RectTransform _root;
        private TMP_Text _text;

        private float _resolveTimer;
        private PointerEventData _pointer;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Give <paramref name="target"/> a tooltip. Safe to call repeatedly —
        /// re-binding replaces the callback rather than stacking components.
        /// </summary>
        public static void Bind(GameObject target, System.Func<string> text)
        {
            if (target == null || text == null) return;
            var source = target.GetComponent<UITooltipSource>();
            if (source == null) source = target.AddComponent<UITooltipSource>();
            source.Text = text;
            Ensure();
        }

        /// <summary>Static text variant.</summary>
        public static void Bind(GameObject target, string text) => Bind(target, () => text);

        /// <summary>
        /// The GameObject's click relay, created only if it has none. Several
        /// panels already add a relay for right-click or hover styling;
        /// stacking a second one makes every left click fire twice.
        /// </summary>
        public static UiClickRelay Relay(GameObject go)
        {
            var relay = go.GetComponent<UiClickRelay>();
            return relay != null ? relay : go.AddComponent<UiClickRelay>();
        }

        // ── Construction ───────────────────────────────────────────────────

        private static void Ensure()
        {
            if (_instance != null || _quitting) return;

            // Scene-scoped on purpose: rebuilt lazily after a scene load, so a
            // tooltip can never survive into a screen whose buttons are gone.
            var go = new GameObject("GameUI_Tooltip",
                typeof(Canvas), typeof(CanvasScaler), typeof(UITooltip));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;   // above every panel, including menus
            // NOTE: deliberately NO GraphicRaycaster. This canvas must be
            // invisible to the EventSystem or it would steal the hover from
            // the widget it is describing.

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            _instance = go.GetComponent<UITooltip>();
            _instance.Build((RectTransform)go.transform);
        }

        private void Build(RectTransform canvasRect)
        {
            _canvasRect = canvasRect;

            _root = GameUIKit.Rect(canvasRect, "tooltip");
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.zero;
            _root.pivot = Vector2.zero;
            _root.sizeDelta = new Vector2(MaxWidth, 0f);

            var bg = GameUIKit.Image(_root, "bg", GameUIKit.PanelBg);
            GameUIKit.Stretch(bg.rectTransform);
            GameUIKit.IgnoreLayout(bg.gameObject);

            var border = GameUIKit.Image(_root, "border", GameUIKit.BorderGold);
            border.rectTransform.anchorMin = Vector2.zero;
            border.rectTransform.anchorMax = new Vector2(1f, 0f);
            border.rectTransform.pivot = new Vector2(0.5f, 0f);
            border.rectTransform.sizeDelta = new Vector2(0f, 3f);
            GameUIKit.IgnoreLayout(border.gameObject);

            _text = GameUIKit.Text(_root, "text", "", FontSize, GameUIKit.TextMain);
            _text.textWrappingMode = TextWrappingModes.Normal;

            var layout = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 12, 14);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Belt and braces: even without a raycaster on this canvas, nothing
            // in the subtree may ever be a raycast target.
            GameUIKit.DisableRaycasts(_root.gameObject);

            _root.gameObject.SetActive(false);
        }

        // ── Hover resolution ───────────────────────────────────────────────

        private void LateUpdate()
        {
            _resolveTimer += Time.unscaledDeltaTime;
            if (_resolveTimer >= ResolveInterval)
            {
                _resolveTimer = 0f;
                Resolve();
            }
            if (_root.gameObject.activeSelf) Reposition();
        }

        /// <summary>
        /// Find the tooltip under the pointer, if any, and show it. Walking up
        /// from the hit with GetComponentInParent is what makes nested widgets
        /// work: hovering a sect's tier button finds the button's own text,
        /// and hovering the slot around it finds the slot's.
        /// </summary>
        private void Resolve()
        {
            var events = EventSystem.current;
            if (events == null) { SetShown(null); return; }

            _pointer ??= new PointerEventData(events);
            _pointer.position = UnityEngine.Input.mousePosition;

            _hits.Clear();
            events.RaycastAll(_pointer, _hits);

            for (int i = 0; i < _hits.Count; i++)
            {
                var hit = _hits[i].gameObject;
                if (hit == null) continue;
                var source = hit.GetComponentInParent<UITooltipSource>();
                if (source == null || source.Text == null) continue;

                string text = source.Text();
                if (string.IsNullOrEmpty(text)) continue;
                SetShown(text);
                return;
            }
            SetShown(null);
        }

        private void SetShown(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (show && _text.text != text) _text.text = text;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
        }

        private void OnApplicationQuit() => _quitting = true;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Park the box beside the cursor, flipping its pivot near the screen
        /// edges so it never runs off-screen.
        /// </summary>
        private void Reposition()
        {
            Vector2 screen = UnityEngine.Input.mousePosition;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screen, null, out var local))
                return;

            bool right = screen.x > Screen.width * 0.6f;
            bool top = screen.y > Screen.height * 0.6f;
            _root.pivot = new Vector2(right ? 1f : 0f, top ? 1f : 0f);

            // ScreenPointToLocalPointInRectangle is relative to the canvas
            // CENTRE; the root is anchored bottom-left.
            var half = _canvasRect.rect.size * 0.5f;
            _root.anchoredPosition = local + half + new Vector2(
                right ? -CursorGap : CursorGap,
                top ? -CursorGap : CursorGap);
        }
    }
}
