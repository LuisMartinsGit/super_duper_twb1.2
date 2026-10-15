// ReligionPanelBinder.Popups.cs
// The religion menu's two popovers (docs/Design/Religion_Menu.md §3). Both
// open to the LEFT of the menu, which sits on the right edge of the screen.
//
//   * the SECT PICKER — three culture columns of four sects, each with its
//     adoption price ("yours" for the player's culture after age-up, the full
//     price for everyone before), adopted sects greyed, and a line when a pick
//     would complete a set;
//   * the INLINE CONFIRM — every purchase asks once, on the spot, and says
//     "Final, cannot be undone" where it is.

using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private RectTransform _picker;
        private TMP_Text _pickerNote, _pickerHeader;
        private readonly Button[] _pickerButtons = new Button[SectConfig.SectCount];
        private readonly Image[] _pickerImages = new Image[SectConfig.SectCount];
        private readonly TMP_Text[] _pickerLabels = new TMP_Text[SectConfig.SectCount];
        private readonly TMP_Text[] _pickerPrice = new TMP_Text[3];

        private RectTransform _confirm;
        private TMP_Text _confirmText;
        private System.Action _confirmAction;

        private void BuildPopups()
        {
            // ── Picker ──
            _picker = GameUIKit.Rect(_root, "Picker");
            _picker.anchorMin = _picker.anchorMax = new Vector2(0f, 1f);
            _picker.pivot = new Vector2(1f, 1f);
            _picker.anchoredPosition = new Vector2(-24f, -RowsTop);
            _picker.sizeDelta = new Vector2(700f, 560f);
            GameUIKit.PanelChrome(_picker);
            _pickerHeader = GameUIKit.Text(_picker, "Title", Loc.T("Choose a sect"), 32f, GameUIKit.Gold);
            Place(_pickerHeader.rectTransform, 24f, 18f, 640f, 48f);

            int shownCols = 0;
            for (int c = 0; c < 3; c++) if (SectConfig.ClusterInEdition((SectCluster)c)) _pickerCol[c] = shownCols++; else _pickerCol[c] = -1;
            _picker.sizeDelta = new Vector2(math.max(460f, 48f + shownCols * 222f - 12f), 560f);
            float pickerW = _picker.sizeDelta.x;
            Place(_pickerHeader.rectTransform, 24f, 18f, pickerW - 96f, 48f);
            for (int c = 0; c < 3; c++)
            {
                if (_pickerCol[c] < 0) continue;
                float x = 24f + _pickerCol[c] * 222f;
                var head = GameUIKit.Text(_picker, "Culture" + c, ClusterName(c), 26f, ClusterColor[c], TextAlignmentOptions.Center);
                Place(head.rectTransform, x, 76f, 210f, 36f);
                _pickerPrice[c] = GameUIKit.Text(_picker, "Price" + c, "", 22f, GameUIKit.TextDim, TextAlignmentOptions.Center);
                Place(_pickerPrice[c].rectTransform, x, 112f, 210f, 30f);
            }
            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                string sectId = SectConfig.IdAt(i);
                int cluster = (int)SectConfig.ClusterOf(sectId);
                int nInCluster = 0;
                for (int j = 0; j < i; j++) if ((int)SectConfig.ClusterOf(SectConfig.IdAt(j)) == cluster) nInCluster++;
                if (_pickerCol[cluster] < 0) continue;   // not in this edition
                float x = 24f + _pickerCol[cluster] * 222f;
                var b = MakeButton(_picker, "Sect" + i, out _pickerImages[i], () => ConfirmAdopt(sectId));
                Place((RectTransform)b.transform, x + 10f, 152f + nInCluster * 84f, 190f, 72f);
                _pickerLabels[i] = Label(b.transform, "Text", SectInfo.ShortName(sectId), 26f, GameUIKit.TextMain, TextAlignmentOptions.Center);
                UITooltip.Bind(b.gameObject, () => AdoptTooltip(sectId));
                _pickerButtons[i] = b;
            }
            _pickerNote = GameUIKit.Text(_picker, "Note", "", 22f, GoldBadge, TextAlignmentOptions.Center);
            _pickerNote.textWrappingMode = TextWrappingModes.Normal;
            Place(_pickerNote.rectTransform, 24f, 490f, pickerW - 48f, 60f);
            var close = MakeButton(_picker, "Close", out var cbg, ClosePopups);
            cbg.color = new Color(0f, 0f, 0f, 0f);
            Place((RectTransform)close.transform, pickerW - 60f, 14f, 48f, 48f);
            Label(close.transform, "Text", "✕", 28f, GameUIKit.TextMain, TextAlignmentOptions.Center);
            _picker.gameObject.SetActive(false);

            // ── Confirm ──
            _confirm = GameUIKit.Rect(_root, "Confirm");
            _confirm.anchorMin = _confirm.anchorMax = new Vector2(0f, 1f);
            _confirm.pivot = new Vector2(1f, 0.5f);
            _confirm.sizeDelta = new Vector2(620f, 120f);
            GameUIKit.PanelChrome(_confirm);
            _confirmText = GameUIKit.Text(_confirm, "Text", "", 24f, GameUIKit.TextMain);
            _confirmText.textWrappingMode = TextWrappingModes.Normal;
            _confirmText.rectTransform.anchorMin = Vector2.zero; _confirmText.rectTransform.anchorMax = Vector2.one;
            _confirmText.rectTransform.offsetMin = new Vector2(20f, 10f); _confirmText.rectTransform.offsetMax = new Vector2(-200f, -10f);
            var ok = MakeButton(_confirm, "OK", out var okbg, () => { var a = _confirmAction; ClosePopups(); a?.Invoke(); });
            okbg.color = GoldBadge;
            var okr = (RectTransform)ok.transform;
            okr.anchorMin = okr.anchorMax = new Vector2(1f, 0.5f); okr.pivot = new Vector2(1f, 0.5f);
            okr.anchoredPosition = new Vector2(-104f, 0f); okr.sizeDelta = new Vector2(84f, 84f);
            Label(ok.transform, "Text", "✓", 36f, new Color(0.1f, 0.08f, 0.02f), TextAlignmentOptions.Center);
            UITooltip.Bind(ok.gameObject, Loc.T("Confirm (Enter)"));
            var no = MakeButton(_confirm, "Cancel", out _, ClosePopups);
            var nor = (RectTransform)no.transform;
            nor.anchorMin = nor.anchorMax = new Vector2(1f, 0.5f); nor.pivot = new Vector2(1f, 0.5f);
            nor.anchoredPosition = new Vector2(-12f, 0f); nor.sizeDelta = new Vector2(84f, 84f);
            Label(no.transform, "Text", "✕", 32f, GameUIKit.TextMain, TextAlignmentOptions.Center);
            UITooltip.Bind(no.gameObject, Loc.T("Cancel (Esc)"));
            _confirm.gameObject.SetActive(false);
        }

        private static string ClusterName(int c) => Loc.T(SectConfig.ClusterDisplayName((SectCluster)c));

        /// <summary>Picker column of each cluster; -1 when the edition omits it.</summary>
        private readonly int[] _pickerCol = new int[3];

        private void ClosePopups()
        {
            if (_picker != null) _picker.gameObject.SetActive(false);
            if (_confirm != null) _confirm.gameObject.SetActive(false);
            _confirmAction = null;
        }

        private void OpenPicker(int rowIndex)
        {
            if (!TryWorld()) return;
            if (!_templeStanding) { PlayerNotificationSystem.NotifyError(Loc.T("Build a Temple of Ridan to adopt sects")); return; }
            ClosePopups();
            _picker.anchoredPosition = new Vector2(-24f, -math.max(RowsTop - 200f, RowsTop + rowIndex * RowHeight - 300f));
            _picker.gameObject.SetActive(true);
            RefreshPopups();
        }

        private void RefreshPopups()
        {
            if (_picker == null || !_picker.gameObject.activeSelf) return;
            byte culture = CultureConfig.GetCompletedCulture(_em, _faction);
            for (int c = 0; c < 3; c++)
            {
                var cluster = (SectCluster)c;
                if (_pickerCol[c] < 0) continue;
                string sample = null;
                for (int i = 0; i < SectConfig.SectCount; i++)
                    if (SectConfig.ClusterOf(SectConfig.IdAt(i)) == cluster) { sample = SectConfig.IdAt(i); break; }
                int price = SectConfig.AdoptionCost(sample, culture);
                bool yours = culture != Cultures.None && SectConfig.ClusterFromCulture(culture) == cluster;
                _pickerPrice[c].text = price + " " + Loc.T("RP") + (yours ? " · " + Loc.T("yours") : "");
                _pickerPrice[c].color = yours ? GoldBadge : GameUIKit.TextDim;
            }

            var counts = _hasState ? SetPassives.ClusterCounts(_state) : default;
            string completes = null;
            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                string sectId = SectConfig.IdAt(i);
                if (_pickerButtons[i] == null) continue;   // not in this edition
                var check = SectAdoption.CanAdopt(_em, _faction, sectId, out _);
                bool adopted = check == SectAdoptionResult.AlreadyAdopted;
                bool can = check == SectAdoptionResult.Ok;
                var color = ClusterColor[(int)SectConfig.ClusterOf(sectId)];
                _pickerImages[i].color = adopted ? new Color(0.24f, 0.2f, 0.08f, 0.6f)
                                       : can ? new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.95f)
                                             : DimCell;
                _pickerLabels[i].color = adopted ? GameUIKit.TextDim : can ? GameUIKit.TextMain : RedBadge;
                _pickerButtons[i].interactable = can;
                if (can && completes == null)
                {
                    var after = counts;
                    switch (SectConfig.ClusterOf(sectId))
                    {
                        case SectCluster.Alanthor: after.x++; break;
                        case SectCluster.Runaii: after.y++; break;
                        case SectCluster.Feraldis: after.z++; break;
                    }
                    var before = SetPassives.SetOf(counts);
                    var set = SetPassives.SetOf(after);
                    if (set != SectSet.None && set != before)
                        completes = string.Format(Loc.T("{0} completes {1}"), SectInfo.ShortName(sectId), Loc.T(SetPassives.Name(set)));
                }
            }
            string note = culture == Cultures.None
                ? Loc.T("Every sect costs the full price until you age up. Prices drop for your culture after age-up.")
                : Loc.T("Adopting is final and cannot be undone.");
            _pickerNote.text = completes != null ? completes + "\n<color=#A8A38F>" + note + "</color>" : note;
        }

        private string AdoptTooltip(string sectId)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(Loc.T("Sect of")).Append(' ').Append(SectInfo.ShortName(sectId)).Append("</b>\n")
              .Append(SectInfo.Lore(sectId)).Append("\n\n");
            for (int s = 1; s <= 3; s++)
            {
                var spec = SectLeverEffects.ActiveOf(sectId, s, 1);
                if (spec.Name == null) continue;
                sb.Append(s == 1 ? "A " : s == 2 ? "B " : "★ ").Append("<b>").Append(Loc.T(spec.Name)).Append("</b>");
                if (spec.Description != null) sb.Append(" — ").Append(Loc.T(spec.Description));
                sb.Append('\n');
            }
            sb.Append('\n').Append(SectInfo.PassiveDescription(sectId));
            string unit = SectInfo.SectUnitText(sectId), hero = SectInfo.HeroText(sectId);
            if (unit != null) sb.Append("\n").Append(unit);
            if (hero != null) sb.Append("\n").Append(hero);
            return sb.ToString();
        }

        private void ConfirmAdopt(string sectId)
        {
            if (!TryWorld()) return;
            SectAdoption.CanAdopt(_em, _faction, sectId, out int cost);
            var anchor = _pickerButtons[SectConfig.IndexOf(sectId)].transform as RectTransform;
            ShowConfirm(string.Format("<b>{0}</b> <color=#F5C451>· {1} RP</color>\n<size=85%><color=#A8A38F>{2}</color></size>",
                    Loc.T("Adopt") + " " + SectInfo.ShortName(sectId), cost, Loc.T("Final, cannot be undone")),
                anchor, () =>
                {
                    if (!TryWorld() || !TryGetTemple(out var temple)) return;
                    var result = SectAdoption.ValidateAdoption(_em, _faction, sectId);
                    if (result == SectAdoptionResult.Ok) { CommandRouter.IssueSectAdoption(_em, temple, sectId); return; }
                    PlayerNotificationSystem.NotifyError(result switch
                    {
                        SectAdoptionResult.NotEnoughRP => Loc.T("Not enough Religion Points"),
                        SectAdoptionResult.SlotsFull => Loc.T("All six sect slots are used"),
                        SectAdoptionResult.AlreadyAdopted => Loc.T("Sect already adopted"),
                        SectAdoptionResult.TempleMissing => Loc.T("Build a Temple of Ridan to adopt sects"),
                        _ => Loc.T("Cannot adopt this sect"),
                    });
                }, keepPicker: true);
        }

        /// <summary>The inline confirm for a religion purchase.</summary>
        private void Confirm(string title, string body, ReligionPurchaseKind kind, string sectId,
            RectTransform anchor, bool finalWarning)
        {
            if (!TryWorld()) return;
            bool can = ReligionPurchases.CanBuy(_em, _faction, kind, sectId, out int rp);
            string price = kind == ReligionPurchaseKind.Tithe
                ? CostShort(FactionReligionPointsHelper.TitheCost(_em, _faction))
                : rp + " " + Loc.T("RP");
            if (!can)
            {
                PlayerNotificationSystem.NotifyError(kind == ReligionPurchaseKind.Tithe
                    ? (_templeStanding ? Loc.T("Not enough resources for the Tithe") : Loc.T("The Tithe needs a standing Temple"))
                    : Loc.T("Not enough Religion Points"));
                return;
            }
            string text = "<b>" + title + "</b> <color=#F5C451>· " + price + "</color>\n<size=85%><color=#A8A38F>"
                          + body + (finalWarning && !body.Contains(Loc.T("Final")) ? " " + Loc.T("Final, cannot be undone") : "")
                          + "</color></size>";
            ShowConfirm(text, anchor, () =>
            {
                if (!TryWorld()) return;
                if (!CommandRouter.IssueReligionPurchase(_em, _faction, kind, sectId))
                    PlayerNotificationSystem.NotifyError(Loc.T("That purchase is no longer available"));
            }, keepPicker: false);
        }

        private void ShowConfirm(string text, RectTransform anchor, System.Action action, bool keepPicker)
        {
            if (!keepPicker && _picker != null) _picker.gameObject.SetActive(false);
            _confirmText.text = text;
            _confirmAction = action;
            // Line it up with the clicked control, to the left of the menu.
            Vector3 world = anchor != null ? anchor.TransformPoint(anchor.rect.center) : _root.position;
            Vector3 local = _root.InverseTransformPoint(world);
            float y = local.y - _root.rect.height * 0.5f;   // from the root's top edge
            _confirm.anchoredPosition = new Vector2(keepPicker ? -24f - 700f - 12f : -24f, y);
            _confirm.gameObject.SetActive(true);
            _confirm.SetAsLastSibling();
        }

        private void LateUpdate()
        {
            if (_confirm == null || !_confirm.gameObject.activeSelf) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var a = _confirmAction; ClosePopups(); a?.Invoke();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) ClosePopups();
        }
    }
}
