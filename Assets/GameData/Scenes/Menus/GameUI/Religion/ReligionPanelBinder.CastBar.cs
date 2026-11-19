// ReligionPanelBinder.CastBar.cs
// THE CAST COLUMN (2026-11-19, developer: "Sect powers must be always
// visible, add a column to the left side with buttons for all the available
// powers. When they are in cooldown, show the remaining cooldown time clearly,
// when a power becomes available, it flashes twice so the user gets called to
// it. Left side column = cast, right side religion menu = track progress.")
// Religion_Menu.md § The cast column.
//
// Docked to the LEFT edge of the HUD, always on screen once the religion menu
// is unlocked and a power is lit, whether the right menu is open or slid
// away. One row per adopted sect in the vessels' order (so row N is F-key N),
// one button per lit power at its slot's column (A, B, ★ — Shift / Ctrl), a
// gap where a power is still locked. A power cooling down is dimmed under a
// sweep with its remaining time in large type; the moment it is ready again —
// or first lights up — it flashes twice.
//
// Casting goes through the same BeginCast as the hotkeys.

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private const float CastCell = 96f;
        private const float CastGap = 10f;
        private const float CastPad = 14f;
        private const float CastEmblem = 44f;
        /// <summary>Two flashes over this many seconds.</summary>
        private const float FlashSeconds = 1.0f;

        private RectTransform _castRoot;
        private readonly CastRow[] _castRows = new CastRow[SectConfig.MaxAdoptedSects];

        private sealed class CastRow
        {
            public RectTransform Root;
            public Image Emblem;
            public readonly CastButton[] Buttons = new CastButton[3];
        }

        private sealed class CastButton
        {
            public Button Button;
            public RectTransform Rect;
            public Image Background, Icon, Sweep, Flash, NoCast;
            public EdgeFrame Edge;
            public TMP_Text Label, Key, Seconds;
        }

        /// <summary>Per power ("sect#slot"), not per button: adopting a sect
        /// re-sorts the rows, and a power that only moved must not flash.</summary>
        private readonly System.Collections.Generic.Dictionary<string, bool> _castWasReady
            = new System.Collections.Generic.Dictionary<string, bool>();
        private readonly System.Collections.Generic.Dictionary<string, float> _castFlashAt
            = new System.Collections.Generic.Dictionary<string, float>();
        /// <summary>The column has looked once: what was ready then (a loaded
        /// game) is not news; what lights up after is.</summary>
        private bool _castSeeded;

        private static string CastKey(string sectId, int slot) => sectId + "#" + slot;

        private void BuildCastBar()
        {
            var parent = _root.parent as RectTransform;
            _castRoot = GameUIKit.Rect(parent != null ? parent : _root, "SectCastColumn");
            _castRoot.anchorMin = _castRoot.anchorMax = new Vector2(0f, 0.5f);
            _castRoot.pivot = new Vector2(0f, 0.5f);
            _castRoot.anchoredPosition = new Vector2(24f, 120f);
            var chrome = GameUIKit.Rect(_castRoot, "Body");
            GameUIKit.Stretch(chrome);
            GameUIKit.PanelChrome(chrome);

            for (int r = 0; r < _castRows.Length; r++)
            {
                int rowIndex = r;
                var row = new CastRow { Root = GameUIKit.Rect(_castRoot, "Row" + r) };
                row.Emblem = GameUIKit.Image(row.Root, "Emblem", Faith, raycast: true);
                Place(row.Emblem.rectTransform, 0f, (CastCell - CastEmblem) * 0.5f, CastEmblem, CastEmblem);
                UITooltip.Bind(row.Emblem.gameObject, () => SectTooltip(rowIndex));

                for (int p = 0; p < 3; p++)
                {
                    int slot = p + 1;
                    var cb = new CastButton();
                    cb.Button = MakeButton(row.Root, "Power" + slot, out cb.Background, () => CastFromColumn(rowIndex, slot));
                    cb.Rect = (RectTransform)cb.Button.transform;
                    Place(cb.Rect, CastEmblem + CastGap + p * (CastCell + CastGap), 0f, CastCell, CastCell);
                    cb.Rect.pivot = new Vector2(0.5f, 0.5f);
                    cb.Rect.anchoredPosition += new Vector2(CastCell * 0.5f, -CastCell * 0.5f);
                    cb.Background.color = Faith;
                    cb.Edge = Edge(cb.Background, FaithBright);
                    cb.Icon = IconImage(cb.Button.transform, null, 60f, FaithInk);
                    cb.Icon.rectTransform.anchoredPosition = new Vector2(0f, 6f);
                    cb.Label = Label(cb.Button.transform, "Label", "", 18f, FaithInk, TextAlignmentOptions.Center);
                    cb.Label.textWrappingMode = TextWrappingModes.Normal;

                    // Cooling down: a dark sweep and the seconds left, large.
                    cb.Sweep = GameUIKit.Image(cb.Button.transform, "Sweep", new Color(0f, 0f, 0f, 0.72f));
                    GameUIKit.Stretch(cb.Sweep.rectTransform);
                    cb.Sweep.type = Image.Type.Filled;
                    cb.Sweep.fillMethod = Image.FillMethod.Radial360;
                    cb.Sweep.fillOrigin = (int)Image.Origin360.Top;
                    cb.Sweep.fillClockwise = false;
                    cb.Sweep.fillAmount = 0f;
                    cb.Seconds = Label(cb.Button.transform, "Secs", "", 40f, Color.white, TextAlignmentOptions.Center);
                    cb.Seconds.fontStyle = FontStyles.Bold;
                    cb.Seconds.outlineWidth = 0.25f;
                    cb.Seconds.outlineColor = new Color32(0, 0, 0, 255);

                    cb.Key = GameUIKit.Text(cb.Button.transform, "Key", "", 17f, FaithInk, TextAlignmentOptions.Center, wrap: false);
                    Place(cb.Key.rectTransform, 0f, CastCell - 22f, CastCell, 20f);
                    cb.Key.fontStyle = FontStyles.Bold;

                    cb.NoCast = GameUIKit.Image(cb.Button.transform, "NoCast", new Color(1f, 0.35f, 0.35f, 0.9f));
                    cb.NoCast.rectTransform.anchorMin = cb.NoCast.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    cb.NoCast.rectTransform.sizeDelta = new Vector2(7f, CastCell * 1.2f);
                    cb.NoCast.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                    // The call to attention: a bright wash over the whole button.
                    cb.Flash = GameUIKit.Image(cb.Button.transform, "Flash", new Color(1f, 0.97f, 0.85f, 0f));
                    GameUIKit.Stretch(cb.Flash.rectTransform);

                    UITooltip.Bind(cb.Button.gameObject, () => PowerTooltip(rowIndex, slot));
                    row.Buttons[p] = cb;
                }
                _castRows[r] = row;
            }
            _castRoot.gameObject.SetActive(false);
        }

        private void HideCastBar()
        {
            if (_castRoot != null) _castRoot.gameObject.SetActive(false);
        }

        /// <summary>Which buttons stand where (from Refresh, every quarter second).</summary>
        private void RefreshCastBar()
        {
            if (_castRoot == null) return;
            if (!_unlocked || !_hasState) { HideCastBar(); return; }

            int shownRows = 0;
            float rowW = CastEmblem + CastGap + 3f * CastCell + 2f * CastGap;
            for (int r = 0; r < _castRows.Length; r++)
            {
                var row = _castRows[r];
                string sectId = _rowSect[r];
                var sect = sectId != null ? _state.Get(SectConfig.IndexOf(sectId)) : default;
                bool any = false;
                for (int p = 0; p < 3; p++)
                {
                    var cb = row.Buttons[p];
                    int slot = p + 1;
                    bool lit = sectId != null && sect.IsSlotUnlocked(slot);
                    cb.Button.gameObject.SetActive(lit);
                    if (!lit) continue;
                    any = true;
                    var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
                    bool drawn = SetIcon(cb.Icon, spec.Name);
                    cb.Icon.enabled = drawn;
                    cb.Label.text = drawn ? "" : (spec.Name != null ? Loc.T(spec.Name) : "—");
                    cb.Key.text = HotkeyLabel(r, slot);
                    cb.NoCast.gameObject.SetActive(!_templeStanding);
                }
                row.Root.gameObject.SetActive(any);
                if (!any) continue;
                if (!SetIcon(row.Emblem, SectEmblemKey(sectId))) row.Emblem.enabled = false;
                else row.Emblem.enabled = true;
                Place(row.Root, CastPad, CastPad + shownRows * (CastCell + CastGap), rowW, CastCell);
                shownRows++;
            }

            if (shownRows == 0) { HideCastBar(); return; }
            _castRoot.sizeDelta = new Vector2(rowW + 2f * CastPad,
                2f * CastPad + shownRows * CastCell + (shownRows - 1) * CastGap);
            _castRoot.gameObject.SetActive(true);
        }

        /// <summary>Cooldowns and flashes, every frame.</summary>
        private void UpdateCastBar()
        {
            if (_castRoot == null || !_castRoot.gameObject.activeSelf || !_hasState || !TryWorld()) return;
            float now = Time.unscaledTime;
            for (int r = 0; r < _castRows.Length; r++)
            {
                string sectId = _rowSect[r];
                if (sectId == null) continue;
                for (int p = 0; p < 3; p++)
                {
                    var cb = _castRows[r].Buttons[p];
                    if (!cb.Button.gameObject.activeSelf) continue;
                    int slot = p + 1;

                    float rem = SectActivePowerHelper.CooldownRemaining(_em, _faction, sectId, slot);
                    bool ready = rem <= 0f && _templeStanding;
                    if (rem > 0f)
                    {
                        var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
                        float total = SectActivePowerHelper.EffectiveCooldown(_em, _faction, sectId, spec.Cooldown);
                        cb.Sweep.fillAmount = total > 0f ? Mathf.Clamp01(rem / total) : 1f;
                        cb.Seconds.text = CooldownText(rem);
                    }
                    else { cb.Sweep.fillAmount = 0f; cb.Seconds.text = ""; }
                    cb.Background.color = ready ? Faith : new Color(Faith.r * 0.55f, Faith.g * 0.55f, Faith.b * 0.55f, 1f);

                    // Ready again, or lit for the first time: flash twice. A
                    // power already ready when the column first looks (a
                    // loaded game) does not.
                    string key = CastKey(sectId, slot);
                    bool seen = _castWasReady.TryGetValue(key, out bool wasReady);
                    if (ready && (seen ? !wasReady : _castSeeded)) _castFlashAt[key] = now;
                    _castWasReady[key] = ready;

                    float t = _castFlashAt.TryGetValue(key, out float at) ? now - at : -1f;
                    float a = t >= 0f && t < FlashSeconds ? Mathf.Abs(Mathf.Sin(t / FlashSeconds * 2f * Mathf.PI)) : 0f;
                    var fc = cb.Flash.color; fc.a = 0.85f * a; cb.Flash.color = fc;
                    cb.Rect.localScale = Vector3.one * (1f + 0.14f * a);
                    cb.Edge.effectColor = a > 0.01f ? Color.white : FaithBright;
                }
            }
            _castSeeded = true;
        }

        private static string CooldownText(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return s >= 60 ? (s / 60) + ":" + (s % 60).ToString("00") : s.ToString();
        }

        private void CastFromColumn(int rowIndex, int slot)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return;
            BeginCast(sectId, slot);
        }

        /// <summary>A lit power clicked in the right menu (which tracks, it
        /// does not cast): point at its button in the column with one flash.</summary>
        private void PointAtCastButton(int rowIndex, int slot)
        {
            if (rowIndex < 0 || rowIndex >= _castRows.Length) return;
            var cb = _castRows[rowIndex].Buttons[slot - 1];
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !cb.Button.gameObject.activeSelf) return;
            _castFlashAt[CastKey(sectId, slot)] = Time.unscaledTime - FlashSeconds * 0.5f;   // the second half: one flash
            PlayerNotificationSystem.Notify(Loc.T("Cast sect powers from the column on the left"));
        }
    }
}
