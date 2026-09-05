// SelectionBoxBinder.cs
// The drag-select rectangle, drawn as an authored 9-sliced Synty frame.
//
// It was four GUI.DrawTexture calls in SelectionSystem.OnGUI painting a solid
// green box with a 2px border — the last IMGUI in the input layer. The frame
// is now a real sprite on the HUD canvas, so it matches every other panel and
// the input layer draws nothing at all.

using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Input;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Positions the selection frame from SelectionSystem's published drag rect.
    /// </summary>
    public sealed class SelectionBoxBinder : MonoBehaviour
    {
        RectTransform _frame;
        Canvas _canvas;
        Graphic[] _graphics;
        bool _shown;

        void Awake()
        {
            _frame = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            _graphics = GetComponentsInChildren<Graphic>(true);

            // The frame must never eat clicks: it sits under the cursor for the
            // whole drag, and a raycast target there would read as pointer-over-UI
            // and cancel the very selection it is drawing.
            //
            // Hidden by disabling the GRAPHICS, never this GameObject — a
            // deactivated GameObject stops receiving LateUpdate, so a
            // SetActive(false) here would put the box to sleep permanently.
            foreach (var g in _graphics)
            {
                g.raycastTarget = false;
                g.enabled = false;
            }
            _shown = false;
        }

        void SetShown(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            foreach (var g in _graphics) g.enabled = shown;
        }

        void LateUpdate()
        {
            bool active = SelectionSystem.DragActive;
            if (_frame == null) return;

            if (!active)
            {
                SetShown(false);
                return;
            }

            SetShown(true);

            // SelectionSystem publishes screen space with the origin bottom-left,
            // which is what an anchored-to-bottom-left RectTransform wants, so no
            // Y flip is needed here (the IMGUI version had to flip).
            var r = SelectionSystem.DragRect;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            _frame.anchoredPosition = new Vector2(r.x / scale, r.y / scale);
            _frame.sizeDelta = new Vector2(r.width / scale, r.height / scale);
        }
    }
}
