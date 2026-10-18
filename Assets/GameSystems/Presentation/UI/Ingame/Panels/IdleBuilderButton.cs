// IdleBuilderButton.cs
// The HUD's idle-builder button (2026-10-09). Shows how many of the local
// player's builders stand idle; a click selects the next one and pans the
// camera to it, one at a time — the same cycle as the idle-worker hotkey
// (Input/IdleWorkerCycler). Hidden while spectating. Placement and refresh
// rate are IdleBuilderButton.asset.
//
// DOCKED (2026-10-16): GameUIManager moves it into the resource panel's
// Housing row, beside the housing count (DockInto), where it reads as a big
// count over "Idle". The free-floating placement stays the fallback for a
// HUD without the resource panel.

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Input;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class IdleBuilderButton : MonoBehaviour
    {
        static IdleBuilderButtonConfig Cfg => IdleBuilderButtonConfig.I;

        private RectTransform _root;
        private Image _bg;
        private TMP_Text _label;
        private float _nextRefresh;
        private int _count = -1;
        private bool _docked;

        /// <summary>The live button (one HUD per match).</summary>
        public static IdleBuilderButton Instance { get; private set; }

        private void Awake() { Instance = this; Build(); }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// Move the button into the resource panel's Housing row and shift the
        /// housing icon + count to the other half, so the two sit side by side.
        /// </summary>
        public void DockInto(RectTransform housingRow)
        {
            if (housingRow == null) return;
            foreach (RectTransform child in housingRow)
            {
                var p = child.anchoredPosition;
                child.anchoredPosition = new Vector2(Cfg.housingShiftX, p.y);
                if (child.GetComponent<TMP_Text>() != null)
                    child.sizeDelta = new Vector2(Cfg.housingLabelWidth, child.sizeDelta.y);
            }

            _root.SetParent(housingRow, false);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Cfg.dockedCenter;
            _root.sizeDelta = Cfg.dockedSize;
            _label.fontSize = Cfg.dockedFontSize;
            _label.lineSpacing = -20f;
            _docked = true;
            _count = -1;
            Refresh();
        }

        private void Build()
        {
            _root = GameUIKit.Rect(transform, "IdleBuilders");
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.zero;
            _root.pivot = Vector2.zero;
            _root.anchoredPosition = Cfg.anchoredPosition;
            _root.sizeDelta = Cfg.size;

            _bg = GameUIKit.ButtonChrome(_root, raycast: true);
            _label = GameUIKit.Text(_root, "label", "", Cfg.fontSize, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(_label.rectTransform);

            var relay = UITooltip.Relay(_bg.gameObject);
            relay.OnLeftClick = OnClick;
            UITooltip.Bind(_bg.gameObject, Loc.T(
                "Idle builders: select the next builder with nothing to do and move the camera to it. " +
                "Click again for the next one (hotkey B)."));
            Refresh();
        }

        private void Update()
        {
            bool show = !GameSettings.IsSpectating;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;
            if (Time.unscaledTime < _nextRefresh) return;
            Refresh();
        }

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, Cfg.refreshSeconds);
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            int n = world != null && world.IsCreated ? IdleWorkerCycler.CountIdle(world.EntityManager) : 0;
            if (n == _count) return;
            _count = n;
            _label.text = _docked
                ? n + "\n<size=55%>" + Loc.T("Idle") + "</size>"
                : string.Format(Loc.T("Idle builders: {0}"), n);
            _label.color = n > 0 ? GameUIKit.Gold : GameUIKit.TextDim;
        }

        private void OnClick()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            IdleWorkerCycler.SelectNext(world.EntityManager);
            Refresh();
        }
    }
}
