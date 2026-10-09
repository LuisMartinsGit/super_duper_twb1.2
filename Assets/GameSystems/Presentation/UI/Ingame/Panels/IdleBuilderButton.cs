// IdleBuilderButton.cs
// The HUD's idle-builder button (2026-10-09). Shows how many of the local
// player's builders stand idle; a click selects the next one and pans the
// camera to it, one at a time — the same cycle as the idle-worker hotkey
// (Input/IdleWorkerCycler). Hidden while spectating. Placement and refresh
// rate are IdleBuilderButton.asset.

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

        private void Awake() => Build();

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
            _label.text = string.Format(Loc.T("Idle builders: {0}"), n);
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
