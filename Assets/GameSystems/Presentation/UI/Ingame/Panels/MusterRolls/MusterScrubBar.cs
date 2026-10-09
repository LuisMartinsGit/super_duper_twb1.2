// MusterScrubBar.cs
// The Muster Rolls map's timeline scrubber: a bar that reports a 0..1
// position on press and drag, with a fill and a handle showing the playhead.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    internal sealed class MusterScrubBar : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private RectTransform _rt;
        private RectTransform _fill;
        private RectTransform _handle;

        /// <summary>The user moved the playhead to this fraction.</summary>
        public System.Action<float> Scrubbed;

        public static MusterScrubBar Create(RectTransform parent)
        {
            var bg = GameUIKit.Image(parent, "scrubber", GameUIKit.BarBg, raycast: true);
            var bar = bg.gameObject.AddComponent<MusterScrubBar>();
            bar._rt = bg.rectTransform;

            var fill = GameUIKit.Image(bg.rectTransform, "fill", GameUIKit.BarGold * new Color(1f, 1f, 1f, 0.55f));
            bar._fill = fill.rectTransform;
            bar._fill.anchorMin = new Vector2(0f, 0f);
            bar._fill.anchorMax = new Vector2(0f, 1f);
            bar._fill.pivot = new Vector2(0f, 0.5f);
            bar._fill.offsetMin = Vector2.zero;
            bar._fill.offsetMax = Vector2.zero;

            var handle = GameUIKit.Image(bg.rectTransform, "handle", GameUIKit.Gold);
            bar._handle = handle.rectTransform;
            bar._handle.anchorMin = new Vector2(0f, 0f);
            bar._handle.anchorMax = new Vector2(0f, 1f);
            bar._handle.pivot = new Vector2(0.5f, 0.5f);
            bar._handle.sizeDelta = new Vector2(10f, 18f);
            return bar;
        }

        public void SetValue(float v)
        {
            v = Mathf.Clamp01(v);
            _fill.anchorMax = new Vector2(v, 1f);
            _fill.offsetMax = Vector2.zero;
            _handle.anchorMin = new Vector2(v, 0f);
            _handle.anchorMax = new Vector2(v, 1f);
            _handle.anchoredPosition = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);

        private void Report(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position,
                    e.pressEventCamera, out Vector2 p)) return;
            var r = _rt.rect;
            float v = Mathf.Clamp01((p.x - r.xMin) / Mathf.Max(1f, r.width));
            SetValue(v);
            Scrubbed?.Invoke(v);
        }
    }
}
