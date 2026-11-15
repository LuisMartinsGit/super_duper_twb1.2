// ActionsPanelPrefabBinder.Vault.cs
// THE VAULT OF ALMIÉRRA'S TWO SQUARE PANELS (decision 60, 2026-11-15; rules in
// docs/Design/Age_0.md § Vault of Almiérra). Developer: "it should be made
// from two square panels: the buttons, in a 4x4 grid; each bar is dedicated to
// a different resource."
//
// PANEL 1 — the actions panel, on the builder's square 4 x 4 grid:
//
//            col 0 Supplies   col 1 Iron      col 2 Veilstone   col 3
//   row 0    [+ deposit]      [+ deposit]     [+ deposit]       [- withdraw all]
//   row 1    [supplies +x%]   [iron +x%]      [veil +x%]
//   row 2    [unlock the next bar]  [level up]
//   row 3    (free)
//
// A plus deposits that resource into ITS bars (up to VaultDepositStep a
// click); the resource icon under it deposits all that fits and shows the
// income bonus that resource has now. Row 2 is the Vault's research (the
// Coffer chain, one cell: it unlocks the next bar) and its level-up (each
// level raises what a full bar gives and what a bar holds).
//
// PANEL 2 — a second square beside the first, the same frame: the four bars.
// Bars 1 and 2 take supplies, bar 3 iron, bar 4 veilstone (the Vault SO's
// vaultBarResource). Each row: the resource, the bar cut into sub-bars,
// stored / capacity, and what it pays now of what it pays full. A locked bar
// is dim and names the research that opens it.
//
// Deposits and withdrawals go through CommandRouter.IssueVaultTransfer.
// Read-only on the simulation: VaultStorage, VaultBars and the bank.

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Economy;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>The most one click of a deposit plus pays in. A UI step, not balance.</summary>
        private const int VaultDepositStep = 500;

        /// <summary>Resource names by code (0 = none); English, translated at render.</summary>
        private static string VaultResName(int r) => Loc.T(VaultBars.ResourceNames[Mathf.Clamp(r, 0, 3)]);

        /// <summary>Resource colours, as NodeReserveBars draws the same three
        /// resources (veilstone cyan per Art_Direction.md).</summary>
        private static readonly Color[] VaultResColors =
        {
            new Color(0.60f, 0.60f, 0.60f),
            new Color(0.55f, 0.78f, 0.36f),
            new Color(0.70f, 0.74f, 0.80f),
            new Color(0.30f, 0.86f, 0.95f),
        };

        /// <summary>The Coffer research that opens bar i (bar 0 is open from the start).</summary>
        private static readonly string[] VaultBarTech =
            { null, VaultBars.SecondCoffer, VaultBars.ThirdCoffer, VaultBars.FourthCoffer };

        /// <summary>Cells the Vault's own two rows take on the 4-column grid.</summary>
        private const int VaultControlCells = 2 * PaletteCols;
        private const int VaultWithdrawCol = 3;

        // ── Bars panel layout (panel units; the panel is the actions panel's size) ──

        private const float VaultPanelGap = 12f;       // between the two squares
        private const float VaultPanelInset = 44f;     // frame to content
        private const float VaultTitleHeight = 42f;
        private const float VaultLineHeight = 30f;
        private const float VaultRowGap = 10f;
        private const float VaultSegmentGap = 3f;

        private sealed class VaultBarRow
        {
            public RectTransform Root;
            public RawImage Icon;
            public TMP_Text Label, Value, Bonus;
            public RectTransform Track;
            public Image[] SegBg;
            public Image[] SegFill;
            public string Tooltip;
        }

        private RectTransform _vaultPanel;
        private TMP_Text _vaultTitle, _vaultPerBar, _vaultTotals;
        private VaultBarRow[] _vaultRows;
        private bool _vaultWanted;

        // ── Panel 1: the 4 x 4 grid ───────────────────────────────────────

        /// <summary>The whole Vault render: its two control rows, its research
        /// from row 2, its level-up, and the bars panel. Returns cells used.</summary>
        private int RenderVault(EntityManager em, in EntityActionInfo info)
        {
            int used = RenderVaultControls(em);
            if (used == 0) return 0;
            used += RenderResearchRows(em, VaultControlCells);
            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            _vaultWanted = true;
            return used;
        }

        private int RenderVaultControls(EntityManager em)
        {
            if (!em.HasComponent<VaultStorage>(_entity)) return 0;
            var vault = em.GetComponentData<VaultStorage>(_entity);
            var faction = OwnFaction(em);
            Entity entity = _entity;
            int level = VaultBars.LevelOf(em, entity);
            int open = VaultBars.OpenBars(faction);

            // Both rows are held in place; the cells used are filled below.
            for (int i = 0; i < VaultControlCells && i < _slots.Length; i++) BlankSlot(_slots[i]);

            FactionEconomy.TryGetResources(em, faction, out var bank);
            int subs = VaultBars.SubBars;
            float perBar = VaultBars.BonusPerBar(level) * 100f;

            for (int res = 1; res <= 3; res++)
            {
                int col = res - 1;
                string name = VaultResName(res);
                string key = VaultBars.ResourceNames[res];
                int have = res == 1 ? bank.Supplies : res == 2 ? bank.Iron : bank.Veilstone;
                int room = VaultBars.Room(vault, faction, level, res);
                int amt = System.Math.Min(VaultDepositStep, System.Math.Min(room, have));
                int all = System.Math.Min(room, have);
                int pay = res;
                string why = room > 0 ? (have > 0 ? null : string.Format(Loc.T("You have no {0}."), name))
                                      : VaultNoRoomReason(faction, res, open);
                string rule = string.Format(
                    Loc.T("{0} fills the {0} bars only. Each full bar adds {1:0.#}% to {0} income, a share at a time as it fills ({2} steps). A withdrawal gives everything back and empties every bar."),
                    name, perBar, subs);

                // [+ deposit a step]
                var dep = _slots[col];
                FillSlot(dep, new ActionButton
                {
                    Id = "Vault_Deposit_" + key,
                    Label = "+",
                    Icon = ActionIcons.Plus(),
                    Tooltip = "<b>" + (why ?? string.Format(Loc.T("Deposit {0} {1}"), amt, name)) + "</b>\n" + rule,
                    Enabled = amt > 0,
                    CanAfford = true,
                }, Category.Economy, null,
                    amt > 0 ? (System.Action)(() => VaultDeposit(entity, pay, all: false)) : null, em);
                SetBadge(dep, amt > 0 ? "+" + amt : "", GameUIKit.TextMain);

                // [resource  +x%]: deposit all that fits
                float bonus = VaultBars.BonusOf(faction, res) * 100f;
                var fill = _slots[PaletteCols + col];
                FillSlot(fill, new ActionButton
                {
                    Id = "Vault_Fill_" + key,
                    Label = name,
                    Icon = ActionIcons.Resource(key),
                    Tooltip = "<b>" + (why ?? string.Format(Loc.T("Deposit all the {0} that fits ({1})"), name, all)) + "</b>\n"
                              + string.Format(Loc.T("The Vault raises {0} income by {1:0.#}% now."), name, bonus) + "\n" + rule,
                    Enabled = all > 0,
                    CanAfford = true,
                }, Category.Economy, null,
                    all > 0 ? (System.Action)(() => VaultDeposit(entity, pay, all: true)) : null, em);
                SetBadge(fill, string.Format("+{0:0.#}%", bonus), bonus > 0f ? BadgeOn : GameUIKit.TextDim);
            }

            // [- withdraw all]
            var wd = _slots[VaultWithdrawCol];
            FillSlot(wd, new ActionButton
            {
                Id = "Vault_Withdraw",
                Label = "-",
                Icon = ActionIcons.Minus(),
                Tooltip = "<b>" + string.Format(Loc.T("Withdraw All ({0})"), vault.Paid) + "</b>\n"
                          + Loc.T("Gives back everything paid in. Every bar empties: the bonus and the progress are lost."),
                Enabled = vault.Paid > 0,
                CanAfford = true,
            }, Category.Economy, null,
                vault.Paid > 0 ? (System.Action)(() => VaultAction(entity, 0, 0, deposit: false)) : null, em);
            SetBadge(wd, vault.Paid > 0 ? vault.Paid.ToString() : "", GameUIKit.TextMain);

            RenderVaultBars(vault, faction, open, level);
            return VaultControlCells;
        }

        /// <summary>Why a resource's bars take nothing: full, or still locked.</summary>
        private static string VaultNoRoomReason(Faction faction, int res, int open)
        {
            for (int i = 0; i < VaultStorage.Bars; i++)
                if (VaultBars.ResourceOf(i) == res && i >= open)
                    return string.Format(Loc.T("Bar {0} ({1}) is locked: research {2}"), i + 1, VaultResName(res),
                        VaultTechName(VaultBarTech[Mathf.Min(i, VaultBarTech.Length - 1)]));
            return string.Format(Loc.T("The {0} bars are full"), VaultResName(res));
        }

        private static Cost VaultCost(int type, int amount) => type switch
        {
            1 => Cost.Of(supplies: amount),
            2 => Cost.Of(iron: amount),
            3 => Cost.Of(veilstone: amount),
            _ => default,
        };

        /// <summary>A deposit click, read at click time (the bank and the room
        /// move between refreshes): a step, or all that fits.</summary>
        private void VaultDeposit(Entity entity, int resourceType, bool all)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(entity) || !em.HasComponent<VaultStorage>(entity)) return;
            var faction = OwnFaction(em);
            var vault = em.GetComponentData<VaultStorage>(entity);
            int room = VaultBars.Room(vault, faction, VaultBars.LevelOf(em, entity), resourceType);
            if (!FactionEconomy.TryGetResources(em, faction, out var bank)) return;
            int have = resourceType == 1 ? bank.Supplies : resourceType == 2 ? bank.Iron : bank.Veilstone;
            int amt = System.Math.Min(room, have);
            if (!all) amt = System.Math.Min(amt, VaultDepositStep);
            if (amt <= 0) return;
            VaultAction(entity, resourceType, amt, deposit: true);
        }

        private void VaultAction(Entity entity, int resourceType, int amount, bool deposit)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(entity) || !em.HasComponent<VaultStorage>(entity)) return;
            var faction = OwnFaction(em);

            // Affordability CHECK only — the bank/VaultStorage move lives in
            // CommandRouter.VaultTransferDirect, which every peer executes.
            // docs/Multiplayer_Desync_Sweep_2026-08-16.md
            if (deposit && !FactionEconomy.CanAfford(em, faction, VaultCost(resourceType, amount)))
                return;

            CommandRouter.IssueVaultTransfer(em, entity, resourceType, amount, deposit, 1);
            _timer = RefreshInterval;
        }

        // ── Panel 2: the four bars ────────────────────────────────────────

        private void RenderVaultBars(in VaultStorage vault, Faction faction, int open, int level)
        {
            EnsureVaultPanel();
            if (_vaultPanel == null) return;
            PlaceVaultPanel();

            int subs = VaultBars.SubBars;
            float perBar = VaultBars.BonusPerBar(level) * 100f;
            float perSub = perBar / subs;

            _vaultTitle.text = "<b>" + Loc.T("Vault of Almiérra") + "</b>"
                + "<size=75%><color=#BFB59A>   " + string.Format(Loc.T("Level {0}"), level) + "</color></size>";
            _vaultPerBar.text = string.Format(Loc.T("A full bar adds {0:0.#}% to the income of its resource."), perBar);

            var sb = new System.Text.StringBuilder();
            for (int r = 1; r <= 3; r++)
            {
                if (sb.Length > 0) sb.Append("    ");
                float b = VaultBars.BonusOf(faction, r) * 100f;
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(b > 0f ? VaultResColors[r] : GameUIKit.TextDim)).Append('>')
                  .Append(string.Format(Loc.T("{0} +{1:0.#}%"), VaultResName(r), b)).Append("</color>");
            }
            _vaultTotals.text = sb.ToString();

            for (int i = 0; i < _vaultRows.Length; i++)
            {
                var row = _vaultRows[i];
                if (row.SegFill.Length != subs) BuildSegments(row, subs);

                int res = VaultBars.ResourceOf(i);
                int cap = VaultBars.CapacityOf(i, level);
                bool locked = i >= open;
                int fill = locked ? 0 : vault.Fill(i);
                int filledSubs = locked ? 0 : VaultBars.FilledSubBars(vault, i, level);
                Color col = VaultResColors[res];
                string name = VaultResName(res);

                var tex = ActionIcons.Resource(VaultBars.ResourceNames[res]);
                row.Icon.texture = tex;
                row.Icon.enabled = tex != null;
                row.Icon.color = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;

                row.Label.text = string.Format("{0}  <color=#{1}>{2}</color>", i + 1,
                    ColorUtility.ToHtmlStringRGB(locked ? GameUIKit.TextDim : col), name);
                row.Label.color = locked ? GameUIKit.TextDim : GameUIKit.TextMain;

                if (locked)
                {
                    string tech = VaultTechName(VaultBarTech[Mathf.Min(i, VaultBarTech.Length - 1)]);
                    row.Value.text = string.Format(Loc.T("Locked: {0}"), tech);
                    row.Value.color = GameUIKit.TextLocked;
                    row.Bonus.text = "";
                    row.Tooltip = string.Format(Loc.T("Bar {0} is locked. Research {1} at the Vault to open it. It takes {2} {3}."),
                        i + 1, tech, cap, name);
                }
                else
                {
                    row.Value.text = fill + " / " + cap;
                    row.Value.color = fill >= cap ? GameUIKit.Gold : GameUIKit.TextMain;
                    row.Bonus.text = string.Format("+{0:0.#}%", filledSubs * perSub);
                    row.Bonus.color = filledSubs > 0 ? BadgeOn : GameUIKit.TextDim;
                    row.Tooltip = string.Format(Loc.T("Bar {0} takes {1}: {2} / {3} stored, {4} of {5} steps filled.\nIt adds {6:0.#}% to {1} income now, {7:0.#}% when full."),
                        i + 1, name, fill, cap, filledSubs, subs, filledSubs * perSub, perBar);
                }

                // Each segment is one sub-bar: full ones in the resource's
                // colour, the one still filling at half strength (it pays
                // nothing until it is full), the rest empty.
                float step = (float)cap / subs;
                for (int k = 0; k < subs; k++)
                {
                    float part = locked ? 0f : Mathf.Clamp01((fill - k * step) / step);
                    var f = row.SegFill[k];
                    f.rectTransform.anchorMax = new Vector2(part, 1f);
                    f.enabled = part > 0f;
                    var c = col;
                    if (k >= filledSubs) c.a = 0.5f;
                    f.color = c;
                    row.SegBg[k].color = locked ? GameUIKit.ButtonBgLocked : GameUIKit.BarBg;
                }
            }
        }

        private static string VaultTechName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return TechCatalog.TryGetTechnology(id, out var t) && t != null && !string.IsNullOrEmpty(t.name)
                ? Loc.T(t.name) : id;
        }

        private void ShowVaultSection(bool on)
        {
            if (_vaultPanel != null && _vaultPanel.gameObject.activeSelf != on)
                _vaultPanel.gameObject.SetActive(on);
        }

        /// <summary>
        /// Builds the second square once: a sibling of the actions panel with
        /// a copy of its frame (the prefab's "Background" art, which carries
        /// no behaviour) and the bar rows inside.
        /// </summary>
        private void EnsureVaultPanel()
        {
            if (_vaultPanel != null) return;
            var self = transform as RectTransform;
            if (self == null || self.parent == null) return;

            _vaultPanel = GameUIKit.Rect(self.parent, "GameUI_VaultBarsPanel");
            var background = GameUIKit.FindDeep(transform, "Background");
            if (background != null)
            {
                var copy = Instantiate(background.gameObject, _vaultPanel, false);
                copy.name = "Background";
            }
            else
                GameUIKit.Stretch(GameUIKit.Image(_vaultPanel, "Background", GameUIKit.PanelBg, raycast: true).rectTransform);

            var content = GameUIKit.Rect(_vaultPanel, "Content");
            GameUIKit.Stretch(content);
            content.offsetMin = new Vector2(VaultPanelInset, VaultPanelInset);
            content.offsetMax = new Vector2(-VaultPanelInset, -VaultPanelInset);

            _vaultTitle = GameUIKit.Text(content, "title", "", 32f, GameUIKit.Gold,
                TextAlignmentOptions.Left, wrap: false);
            TopBand(_vaultTitle.rectTransform, 0f, VaultTitleHeight);
            _vaultPerBar = GameUIKit.Text(content, "perBar", "", 21f, GameUIKit.TextMain,
                TextAlignmentOptions.Left, wrap: false);
            FitText(_vaultPerBar, 12f, 21f);
            TopBand(_vaultPerBar.rectTransform, VaultTitleHeight, VaultLineHeight);
            _vaultTotals = GameUIKit.Text(content, "totals", "", 21f, GameUIKit.TextMain,
                TextAlignmentOptions.Left, wrap: false);
            FitText(_vaultTotals, 12f, 21f);
            TopBand(_vaultTotals.rectTransform, VaultTitleHeight + VaultLineHeight, VaultLineHeight);

            float head = VaultTitleHeight + 2f * VaultLineHeight + VaultRowGap;
            int bars = VaultStorage.Bars;
            _vaultRows = new VaultBarRow[bars];
            // The rows share what is left under the header, equally.
            var rows = GameUIKit.Rect(content, "bars");
            GameUIKit.Stretch(rows);
            rows.offsetMax = new Vector2(0f, -head);
            for (int i = 0; i < bars; i++) _vaultRows[i] = MakeVaultRow(rows, i, bars);

            _vaultPanel.gameObject.SetActive(false);
        }

        /// <summary>The same size as the actions panel, immediately to its right.</summary>
        private void PlaceVaultPanel()
        {
            var self = (RectTransform)transform;
            _vaultPanel.anchorMin = self.anchorMin;
            _vaultPanel.anchorMax = self.anchorMax;
            _vaultPanel.pivot = self.pivot;
            _vaultPanel.sizeDelta = self.sizeDelta;
            _vaultPanel.localScale = self.localScale;
            _vaultPanel.anchoredPosition = self.anchoredPosition
                + new Vector2(self.rect.width * self.localScale.x + VaultPanelGap, 0f);
        }

        private static void FitText(TMP_Text t, float min, float max)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
            t.raycastTarget = false;
        }

        /// <summary>Stretch horizontally, a fixed band measured down from the top.</summary>
        private static void TopBand(RectTransform rt, float fromTop, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -fromTop);
            rt.sizeDelta = new Vector2(0f, h);
        }

        /// <summary>One bar row: [icon] label ......... stored / cap   +x%
        ///                      [    the segmented track, full width      ]</summary>
        private VaultBarRow MakeVaultRow(RectTransform rows, int index, int bars)
        {
            var row = new VaultBarRow();
            var rt = GameUIKit.Rect(rows, "bar" + (index + 1));
            float t0 = (float)index / bars, t1 = (float)(index + 1) / bars;
            rt.anchorMin = new Vector2(0f, 1f - t1);
            rt.anchorMax = new Vector2(1f, 1f - t0);
            rt.offsetMax = new Vector2(0f, index == 0 ? 0f : -VaultRowGap * 0.5f);
            rt.offsetMin = new Vector2(0f, index == bars - 1 ? 0f : VaultRowGap * 0.5f);

            // A transparent hit area: the hover names the bar's state in full.
            var hit = GameUIKit.Image(rt, "hit", new Color(0f, 0f, 0f, 0f), raycast: true);
            GameUIKit.Stretch(hit.rectTransform);
            UITooltip.Bind(hit.gameObject, () => row.Tooltip);

            const float TopShare = 0.52f;   // the text line; the track takes the rest

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(RawImage));
            iconGo.transform.SetParent(rt, false);
            row.Icon = iconGo.GetComponent<RawImage>();
            row.Icon.raycastTarget = false;
            var ir = row.Icon.rectTransform;
            ir.anchorMin = new Vector2(0f, 1f - TopShare);
            ir.anchorMax = new Vector2(0f, 1f);
            ir.pivot = new Vector2(0f, 0.5f);
            ir.sizeDelta = new Vector2(40f, 0f);
            ir.anchoredPosition = Vector2.zero;

            row.Label = GameUIKit.Text(rt, "label", "", 25f, GameUIKit.TextMain,
                TextAlignmentOptions.Left, wrap: false);
            FitText(row.Label, 12f, 25f);
            var lr = row.Label.rectTransform;
            lr.anchorMin = new Vector2(0f, 1f - TopShare);
            lr.anchorMax = new Vector2(0.42f, 1f);
            lr.offsetMin = new Vector2(48f, 0f);
            lr.offsetMax = Vector2.zero;

            row.Value = GameUIKit.Text(rt, "value", "", 22f, GameUIKit.TextMain,
                TextAlignmentOptions.Right, wrap: false);
            FitText(row.Value, 11f, 22f);
            var vr = row.Value.rectTransform;
            vr.anchorMin = new Vector2(0.42f, 1f - TopShare);
            vr.anchorMax = new Vector2(0.82f, 1f);
            vr.offsetMin = vr.offsetMax = Vector2.zero;

            row.Bonus = GameUIKit.Text(rt, "bonus", "", 22f, GameUIKit.TextMain,
                TextAlignmentOptions.Right, wrap: false);
            FitText(row.Bonus, 11f, 22f);
            var br = row.Bonus.rectTransform;
            br.anchorMin = new Vector2(0.82f, 1f - TopShare);
            br.anchorMax = new Vector2(1f, 1f);
            br.offsetMin = br.offsetMax = Vector2.zero;

            row.Track = GameUIKit.Rect(rt, "track");
            row.Track.anchorMin = new Vector2(0f, 0.06f);
            row.Track.anchorMax = new Vector2(1f, 1f - TopShare - 0.06f);
            row.Track.offsetMin = row.Track.offsetMax = Vector2.zero;

            row.Root = rt;
            BuildSegments(row, VaultBars.SubBars);
            return row;
        }

        /// <summary>(Re)cuts a row's track into <paramref name="subs"/> segments.</summary>
        private static void BuildSegments(VaultBarRow row, int subs)
        {
            if (row.SegBg != null)
                foreach (var bg in row.SegBg)
                    if (bg != null) Destroy(bg.gameObject);
            subs = Mathf.Max(1, subs);
            row.SegBg = new Image[subs];
            row.SegFill = new Image[subs];
            for (int k = 0; k < subs; k++)
            {
                var bg = GameUIKit.Image(row.Track, "seg" + k, GameUIKit.BarBg);
                var r = bg.rectTransform;
                r.anchorMin = new Vector2((float)k / subs, 0f);
                r.anchorMax = new Vector2((float)(k + 1) / subs, 1f);
                r.offsetMin = new Vector2(k == 0 ? 0f : VaultSegmentGap * 0.5f, 0f);
                r.offsetMax = new Vector2(k == subs - 1 ? 0f : -VaultSegmentGap * 0.5f, 0f);

                var fill = GameUIKit.Image(r, "fill", GameUIKit.BarGold);
                var fr = fill.rectTransform;
                fr.anchorMin = Vector2.zero;
                fr.anchorMax = new Vector2(0f, 1f);
                fr.offsetMin = fr.offsetMax = Vector2.zero;

                row.SegBg[k] = bg;
                row.SegFill[k] = fill;
            }
        }
    }
}
