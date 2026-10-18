// ResourcePanelExpander.cs
// The resource panel's widen toggle (2026-10-16). Collapsed (the default) the
// panel shows the amounts only. Expanded it grows RIGHTWARD, pushing every
// other panel in the bottom-left dock (stats, roster, actions, selection
// header) the same distance, and shows each resource's net income per minute
// in its own column. The choice is remembered per player (PlayerPrefs — a
// viewing preference, not game state).
//
// GameUIManager writes the numbers: SetAmount asks Expanded whether to show
// the rate and hands it to SetRate.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class ResourcePanelExpander : MonoBehaviour
    {
        private const float ExtraWidth = 170f;
        private const float RateWidth = 150f;
        private const float RateFontSize = 30f;
        private const float ToggleSize = 34f;
        private const string PrefKey = "TWB.ResourcePanel.Expanded";

        public static bool Expanded { get; private set; }

        private RectTransform _panel;
        private RectTransform _content;
        private TMP_Text _toggleGlyph;
        private readonly Dictionary<TMP_Text, TMP_Text> _rateOf = new Dictionary<TMP_Text, TMP_Text>();
        private readonly List<(RectTransform rt, Vector2 home)> _pushed = new List<(RectTransform, Vector2)>();
        private Vector2 _panelHomePos, _panelHomeSize, _contentHomePos;

        /// <summary>Wire it on the spawned panel; <paramref name="amounts"/>
        /// are the rows' amount labels (null entries skipped).</summary>
        public void Init(params TMP_Text[] amounts)
        {
            _panel = (RectTransform)transform;
            _content = GameUIKit.FindDeep(transform, "Content") as RectTransform;
            _panelHomePos = _panel.anchoredPosition;
            _panelHomeSize = _panel.sizeDelta;
            if (_content != null) _contentHomePos = _content.anchoredPosition;

            // Every OTHER panel in the dock moves with the panel's right edge.
            if (_panel.parent != null)
                foreach (RectTransform sib in _panel.parent)
                    if (sib != _panel) _pushed.Add((sib, sib.anchoredPosition));

            foreach (var amount in amounts)
            {
                if (amount == null) continue;
                var rate = GameUIKit.Text(amount.transform.parent, "rate", "", RateFontSize,
                    GameUIKit.TextDim, TextAlignmentOptions.Left, wrap: false);
                rate.raycastTarget = false;
                var rt = rate.rectTransform;
                var a = amount.rectTransform;
                rt.anchorMin = a.anchorMin; rt.anchorMax = a.anchorMax; rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(RateWidth, a.sizeDelta.y);
                rt.anchoredPosition = new Vector2(a.anchoredPosition.x + a.sizeDelta.x * (1f - a.pivot.x) + 8f,
                    a.anchoredPosition.y);
                _rateOf[amount] = rate;
            }

            BuildToggle();
            bool saved = false;
            try { saved = PlayerPrefs.GetInt(PrefKey, 0) == 1; } catch { }
            Apply(saved);
        }

        private void BuildToggle()
        {
            // On the right end of the separator between housing and the
            // resources: the one spot with nothing under it (the housing row
            // holds the idle-builders button, the column below is full).
            var host = GameUIKit.FindDeep(transform, "Separator") ?? transform;
            var rt = GameUIKit.Rect(host, "ExpandToggle");
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(ToggleSize, ToggleSize);
            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            _toggleGlyph = GameUIKit.Text(rt, "glyph", "", 26f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            _toggleGlyph.raycastTarget = false;
            GameUIKit.Stretch(_toggleGlyph.rectTransform);
            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = () => Apply(!Expanded);
            UITooltip.Bind(bg.gameObject, () => Expanded
                ? Loc.T("Hide income per minute")
                : Loc.T("Show income per minute"));
        }

        private void Apply(bool expanded)
        {
            Expanded = expanded;
            try { PlayerPrefs.SetInt(PrefKey, expanded ? 1 : 0); } catch { }

            float extra = expanded ? ExtraWidth : 0f;
            // Grow to the right only: the size grows by `extra` around the
            // pivot, so the centre moves by the pivot's share and the content
            // is moved back so it stays where it was.
            _panel.sizeDelta = _panelHomeSize + new Vector2(extra, 0f);
            _panel.anchoredPosition = _panelHomePos + new Vector2(extra * (1f - _panel.pivot.x), 0f);
            if (_content != null)
                _content.anchoredPosition = _contentHomePos - new Vector2(extra * 0.5f, 0f);
            foreach (var (rt, home) in _pushed)
                if (rt != null) rt.anchoredPosition = home + new Vector2(extra, 0f);

            foreach (var rate in _rateOf.Values) rate.gameObject.SetActive(expanded);
            _toggleGlyph.text = expanded ? "‹" : "›";
        }

        /// <summary>The net per-minute rate beside <paramref name="amount"/>.</summary>
        public void SetRate(TMP_Text amount, int rate)
        {
            if (amount == null || !_rateOf.TryGetValue(amount, out var label)) return;
            label.text = (rate > 0 ? "+" : "") + rate + Loc.T("/min");
            label.color = rate > 0 ? new Color(0.62f, 0.88f, 0.54f)
                        : rate < 0 ? new Color(1f, 0.42f, 0.37f)
                        : GameUIKit.TextDim;
        }

        private void OnDestroy()
        {
            if (Expanded) foreach (var (rt, home) in _pushed) if (rt != null) rt.anchoredPosition = home;
        }
    }
}
