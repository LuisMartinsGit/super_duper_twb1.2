// GameUIKit.cs
// Shared uGUI construction helpers for the CODE-BUILT game panels (actions
// panel, builder palette, top choice bar). These panels have no authored
// prefab yet — they are assembled at runtime in the dark-navy + gold theme
// of the old HUD so they read as one family with the authored panels; when
// the author ships prefab shells for them, swap the construction here for
// catalog bindings the way ResourcePanel/SelectionHeader work.
// Location: Assets/Scripts/UI/GameUI/GameUIKit.cs

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheWaningBorder.UI.GameUI
{
    /// <summary>
    /// Pointer relay used by every code-built widget: left/right click
    /// callbacks plus hover enter/exit for tooltips. uGUI Buttons only
    /// surface left clicks, and the training-queue slots need right-click
    /// cancel, so everything routes through this instead.
    /// </summary>
    internal sealed class UiClickRelay : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action OnLeftClick;
        public System.Action OnRightClick;
        public System.Action OnEnter;
        public System.Action OnExit;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                OnRightClick?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Left)
                OnLeftClick?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
    }

    internal static class GameUIKit
    {
        // Dark navy + gold family (matches the retired IMGUI theme and the
        // authored panels' gold titles).
        public static readonly Color PanelBg      = new Color(0.045f, 0.06f, 0.14f, 0.93f);
        public static readonly Color BorderGold   = new Color(0.83f, 0.66f, 0.26f, 0.8f);
        public static readonly Color Gold         = new Color(0.909f, 0.835f, 0.627f);
        public static readonly Color TextMain     = new Color(0.92f, 0.90f, 0.84f);
        public static readonly Color TextDim      = new Color(0.66f, 0.64f, 0.56f);
        public static readonly Color TextLocked   = new Color(0.72f, 0.52f, 0.32f);
        public static readonly Color ButtonBg     = new Color(0.10f, 0.13f, 0.24f, 1f);
        public static readonly Color ButtonBgLocked = new Color(0.06f, 0.075f, 0.14f, 1f);
        public static readonly Color ButtonBgPoor = new Color(0.15f, 0.09f, 0.10f, 1f);
        public static readonly Color BarBg        = new Color(0.04f, 0.05f, 0.12f, 1f);
        public static readonly Color BarGold      = new Color(0.83f, 0.66f, 0.26f, 1f);
        public static readonly Color BarBlue      = new Color(0.30f, 0.55f, 0.85f, 1f);

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Fill the parent rect completely.</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Bordered navy panel background. The background Image IS a raycast
        /// target so hovering any panel reads as pointer-over-UI for the
        /// world-input guards (EventSystem.IsPointerOverGameObject).
        /// </summary>
        public static Image PanelChrome(RectTransform panelRoot)
        {
            var bg = Image(panelRoot, "bg", PanelBg, raycast: true);
            Stretch(bg.rectTransform);

            const float t = 3f;
            MakeBorderStrip(panelRoot, "border_top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, t), new Vector2(0.5f, 1f));
            MakeBorderStrip(panelRoot, "border_bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, t), new Vector2(0.5f, 0f));
            MakeBorderStrip(panelRoot, "border_left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(t, 0), new Vector2(0f, 0.5f));
            MakeBorderStrip(panelRoot, "border_right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(t, 0), new Vector2(1f, 0.5f));
            return bg;
        }

        private static void MakeBorderStrip(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 pivot)
        {
            var img = Image(parent, name, BorderGold);
            var rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        public static TMP_Text Text(Transform parent, string name, string text,
            float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left,
            bool wrap = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.richText = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>Vertical stack with a ContentSizeFitter — sections grow
        /// the panel upward from its bottom anchor.</summary>
        public static VerticalLayoutGroup VStack(RectTransform rt, float padding, float spacing)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            v.spacing = spacing;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement FixHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return le;
        }
    }
}
