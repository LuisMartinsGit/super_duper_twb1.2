// ReligionPanelBinder.Layout.cs
// Three presentation passes on the religion panel (2026-10-11):
//
//   * COMPACT SLOTS — only the chapel slots the player uses (building or
//     adopted) are shown, plus ONE free slot while a sect can still be
//     adopted; the shown slots stack from the top and the panel shrinks to
//     fit them. A slot keeps its index, so clicks and the Temple's slot
//     buffer still line up.
//   * SLIDE OUT — a tab on the panel's left edge slides the whole panel off
//     the right of the screen and back; the tab stays on screen.
//   * CATEGORY COLOURS — every sect button and ability cell is coloured by
//     what it does: red = aggressive / military, purple = the curse,
//     green = healing / buffs, blue = economy / buildings. An ability's
//     colour comes from its power kind (SectActivePowerSpec.Kind); a sect's
//     is the majority over its three actives, so a re-tuned kit recolours
//     itself.

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        // ── Category colours ───────────────────────────────────────────────

        private enum PowerCategory : byte { Military = 0, Curse = 1, Support = 2, Economy = 3 }

        private static readonly Color[] CategoryColor =
        {
            new Color(0.62f, 0.17f, 0.14f, 0.95f),   // red    - aggressive / military
            new Color(0.44f, 0.20f, 0.62f, 0.95f),   // purple - curse
            new Color(0.20f, 0.50f, 0.23f, 0.95f),   // green  - healing / buffs
            new Color(0.18f, 0.36f, 0.62f, 0.95f),   // blue   - economy / buildings
        };

        /// <summary>A cooling ability: its category colour, darkened.</summary>
        private const float CoolingShade = 0.45f;

        private static PowerCategory CategoryOf(SectActivePowerKind kind)
        {
            switch (kind)
            {
                case SectActivePowerKind.HealCircle:
                case SectActivePowerKind.HealCirclePercent:
                case SectActivePowerKind.ArmorCircle:
                case SectActivePowerKind.DamageCircle:
                case SectActivePowerKind.SpeedCircle:
                case SectActivePowerKind.DamageArmorCircle:
                case SectActivePowerKind.DeathWard:
                case SectActivePowerKind.Veil:
                case SectActivePowerKind.Invulnerable:
                    return PowerCategory.Support;

                case SectActivePowerKind.CurseWard:
                case SectActivePowerKind.InfluenceBurst:
                    return PowerCategory.Curse;

                case SectActivePowerKind.RaiseTower:
                case SectActivePowerKind.BuildingHpBuff:
                case SectActivePowerKind.NodeOverYield:
                    return PowerCategory.Economy;

                default:
                    // Damage, burning, conversion, shutdown, spying,
                    // blinding, training boons: acting on the war.
                    return PowerCategory.Military;
            }
        }

        private static PowerCategory CategoryOf(string sectId, int tier)
            => CategoryOf(SectLeverEffects.ActiveOf(sectId, tier).Kind);

        /// <summary>The majority category over the sect's three actives; a
        /// three-way split goes to its first active.</summary>
        private static PowerCategory CategoryOfSect(string sectId)
        {
            if (string.IsNullOrEmpty(sectId)) return PowerCategory.Military;
            var counts = new int[4];
            for (int t = 1; t <= 3; t++) counts[(int)CategoryOf(sectId, t)]++;
            var best = CategoryOf(sectId, 1);
            for (int c = 0; c < 4; c++)
                if (counts[c] > counts[(int)best]) best = (PowerCategory)c;
            return best;
        }

        private static Color SectColor(string sectId) => CategoryColor[(int)CategoryOfSect(sectId)];

        private static Color AbilityColor(string sectId, int tier, bool ready)
        {
            var c = CategoryColor[(int)CategoryOf(sectId, tier)];
            return ready ? c : new Color(c.r * CoolingShade, c.g * CoolingShade, c.b * CoolingShade, c.a);
        }

        /// <summary>The slot's own background, remembered as authored so an
        /// empty slot goes back to it.</summary>
        private readonly System.Collections.Generic.Dictionary<Graphic, Color> _authoredTint = new();

        private void TintGraphic(Graphic g, Color? tint)
        {
            if (g == null) return;
            if (!_authoredTint.TryGetValue(g, out var authored))
            {
                authored = g.color;
                _authoredTint[g] = authored;
            }
            var c = tint ?? authored;
            if (g.color != c) g.color = c;
        }

        private void TintPickerRows()
        {
            foreach (var row in _pickerRows)
                if (row.Button != null)
                    TintGraphic(row.Button.targetGraphic, SectColor(row.SectId));
        }

        // ── Compact slots ──────────────────────────────────────────────────

        /// <summary>Space under the last shown slot.</summary>
        private const float SlotBottomPadding = 24f;

        private float _slotTop;        // Slot1's authored top (anchored y, negative)
        private float _slotStep;       // authored distance between slot tops
        private float _slotHeight;
        private bool _slotGeometry;

        private void CaptureSlotGeometry()
        {
            if (_slots.Count < 2) return;
            var a = _slots[0].Root.transform as RectTransform;
            var b = _slots[1].Root.transform as RectTransform;
            if (a == null || b == null) return;
            _slotTop = a.anchoredPosition.y;
            _slotStep = a.anchoredPosition.y - b.anchoredPosition.y;
            _slotHeight = a.sizeDelta.y;
            _slotGeometry = _slotStep > 0f;
        }

        /// <summary>Show the used slots plus one free one (while a sect can
        /// still be adopted), stacked from the top, and fit the panel to
        /// them.</summary>
        private void LayoutSlots()
        {
            if (!_slotGeometry) return;

            int used = 0;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].State == 1 || _slots[i].State == 2) used++;
            bool canAdopt = used < SectConfig.MaxAdoptedSects;

            int row = 0;
            bool freeShown = false;
            for (int i = 0; i < _slots.Count; i++)
            {
                var view = _slots[i];
                bool show = view.State == 1 || view.State == 2;
                if (!show && view.State == 0 && canAdopt && !freeShown)
                {
                    show = true;
                    freeShown = true;
                }
                if (view.Root.activeSelf != show) view.Root.SetActive(show);
                if (!show) continue;

                var rt = (RectTransform)view.Root.transform;
                var pos = new Vector2(rt.anchoredPosition.x, _slotTop - row * _slotStep);
                if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
                row++;

                // An adopted or rising chapel wears its sect's colour; the
                // free slot keeps the authored look.
                if (view.Button != null)
                    TintGraphic(view.Button.targetGraphic,
                        view.State == 0 || string.IsNullOrEmpty(view.SectId)
                            ? (Color?)null : SectColor(view.SectId));
            }

            if (transform is RectTransform root)
            {
                float h = -_slotTop + Mathf.Max(0, row - 1) * _slotStep
                          + (row > 0 ? _slotHeight : 0f) + SlotBottomPadding;
                if (!Mathf.Approximately(root.sizeDelta.y, h))
                    root.sizeDelta = new Vector2(root.sizeDelta.x, h);
            }
        }

        // ── Slide out ──────────────────────────────────────────────────────

        private const float TabWidth = 40f;
        private const float TabHeight = 120f;
        private const float SlideSeconds = 0.25f;

        private RectTransform _tab;
        private TMP_Text _tabLabel;
        private float _shownX;
        private bool _collapsed;
        private float _slide;          // 0 = shown, 1 = hidden

        private void BuildSlideTab()
        {
            if (!(transform is RectTransform root)) return;
            _shownX = root.anchoredPosition.x;

            _tab = GameUIKit.Rect(transform, "SlideTab");
            _tab.anchorMin = _tab.anchorMax = new Vector2(0f, 0.5f);
            _tab.pivot = new Vector2(1f, 0.5f);
            _tab.anchoredPosition = Vector2.zero;
            _tab.sizeDelta = new Vector2(TabWidth, TabHeight);

            var bg = GameUIKit.ButtonChrome(_tab, raycast: true);
            _tabLabel = GameUIKit.Text(_tab, "label", ">", 30f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(_tabLabel.rectTransform);

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = ToggleCollapsed;
            UITooltip.Bind(bg.gameObject, () => _collapsed
                ? Loc.T("Show the religion panel.")
                : Loc.T("Hide the religion panel."));
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            if (_tabLabel != null) _tabLabel.text = _collapsed ? "<" : ">";
            if (_collapsed && _picker != null && _picker.activeSelf) ClosePicker();
        }

        /// <summary>Ease the panel towards shown / hidden. Unscaled time, so
        /// it slides while the game is paused too.</summary>
        private void AnimateSlide()
        {
            if (!(transform is RectTransform root)) return;
            float target = _collapsed ? 1f : 0f;
            if (Mathf.Approximately(_slide, target)) return;
            _slide = Mathf.MoveTowards(_slide, target,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, SlideSeconds));
            float e = _slide * _slide * (3f - 2f * _slide);
            // Hidden: the panel's right edge at the screen edge plus its own
            // width, i.e. wholly off screen, with the tab left on its edge.
            float hiddenX = root.sizeDelta.x;
            root.anchoredPosition = new Vector2(Mathf.Lerp(_shownX, hiddenX, e), root.anchoredPosition.y);
        }
    }
}
