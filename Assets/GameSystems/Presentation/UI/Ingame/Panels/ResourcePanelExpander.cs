// ResourcePanelExpander.cs
// The resource panel's per-minute rates (2026-10-16; reworked 2026-11-18).
// Each resource shows its net income per minute on a second line UNDER its
// amount, in the amount's own font and size, coloured green / red / grey.
//
// It no longer WIDENS the panel (2026-11-18). The old version grew the panel
// rightward and pushed every other panel in the bottom-left dock (stats,
// roster, actions, selection header) the same distance, so the selection
// header jumped whenever the toggle was used. The dock has no free room to
// the right of the resource panel (the stats panel starts where it ends), so
// the rate now stacks inside the panel's own width and nothing else moves.
//
// The rate has no "/min" suffix: the row's tooltip says it is per minute.
// The Religion row's rate is shown only while religion is actually flowing
// (SetRate's onlyWhenPositive).
//
// ALWAYS ON (developer, 2026-11-18: "the expanding resource panel becomes
// obsolete, which is good! keep the layout. remove the expanded toggle
// button"): there is no toggle and no collapsed state any more; the class
// keeps its name (it is the rate layout). Values: ResourcePanelExpander.asset.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class ResourcePanelExpander : MonoBehaviour
    {
        static ResourcePanelExpanderConfig Cfg => ResourcePanelExpanderConfig.I;

        /// <summary>Always true since the toggle was removed (2026-11-18);
        /// kept so GameUIManager's tooltip test reads unchanged.</summary>
        public static bool Expanded => true;

        /// <summary>One resource row's amount and the rate under it.</summary>
        private sealed class Line
        {
            public TMP_Text Amount, Rate;
            public Vector2 HomePos, HomeSize;
            public VerticalAlignmentOptions HomeAlign;
            public bool HasRate = true;
        }

        private readonly Dictionary<TMP_Text, Line> _lines = new Dictionary<TMP_Text, Line>();

        /// <summary>Wire it on the spawned panel; <paramref name="amounts"/>
        /// are the rows' amount labels (null entries skipped).</summary>
        public void Init(params TMP_Text[] amounts)
        {
            foreach (var amount in amounts)
            {
                if (amount == null || _lines.ContainsKey(amount)) continue;
                // A copy of the amount label, so the rate wears exactly the
                // amount's font, material, size and alignment.
                var copy = Instantiate(amount.gameObject, amount.transform.parent, false);
                copy.name = "rate";
                // Code-driven text: no authored-label localizer on the copy.
                var loc = copy.GetComponent<LocAuthoredLabel>();
                if (loc != null) Destroy(loc);
                var rate = copy.GetComponent<TMP_Text>();
                rate.text = "";
                var a = amount.rectTransform;
                _lines[amount] = new Line
                {
                    Amount = amount,
                    Rate = rate,
                    HomePos = a.anchoredPosition,
                    HomeSize = a.sizeDelta,
                    HomeAlign = amount.verticalAlignment,
                };
                copy.SetActive(false);
            }

            foreach (var line in _lines.Values) Layout(line);
        }

        /// <summary>Two stacked lines while the rate shows, the authored
        /// single amount otherwise.</summary>
        private void Layout(Line line)
        {
            if (line.Amount == null || line.Rate == null) return;
            bool show = Expanded && line.HasRate;
            line.Rate.gameObject.SetActive(show);
            var a = line.Amount.rectTransform;
            if (!show)
            {
                a.anchoredPosition = line.HomePos;
                a.sizeDelta = line.HomeSize;
                line.Amount.verticalAlignment = line.HomeAlign;
                return;
            }
            float off = Cfg.stackOffset;
            var size = new Vector2(line.HomeSize.x, Cfg.lineHeight);
            a.anchoredPosition = line.HomePos + new Vector2(0f, off);
            a.sizeDelta = size;
            line.Amount.verticalAlignment = VerticalAlignmentOptions.Middle;
            var r = line.Rate.rectTransform;
            r.anchoredPosition = line.HomePos - new Vector2(0f, off);
            r.sizeDelta = size;
            line.Rate.verticalAlignment = VerticalAlignmentOptions.Middle;
        }

        /// <summary>
        /// The net per-minute rate under <paramref name="amount"/>: "+120",
        /// "-40", "0" — or "+0.8" below ten when <paramref name="fractional"/>.
        /// With <paramref name="onlyWhenPositive"/> the line is shown only
        /// while the rate is above zero (the Religion row).
        /// </summary>
        public void SetRate(TMP_Text amount, float perMinute, bool fractional = false,
            bool onlyWhenPositive = false)
        {
            if (amount == null || !_lines.TryGetValue(amount, out var line)) return;

            string text;
            float shown;
            if (fractional && Mathf.Abs(perMinute) < 10f)
            {
                shown = Mathf.Round(perMinute * 10f) / 10f;
                text = shown.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                shown = Mathf.RoundToInt(perMinute);
                text = ((int)shown).ToString();
            }
            if (shown > 0f) text = "+" + text;
            line.Rate.text = text;
            line.Rate.color = shown > 0f ? Cfg.positiveColor
                            : shown < 0f ? Cfg.negativeColor
                            : GameUIKit.TextDim;

            bool has = !onlyWhenPositive || shown > 0f;
            if (has != line.HasRate)
            {
                line.HasRate = has;
                Layout(line);
            }
        }
    }
}
