// ActionsPanelPrefabBinder.Vault.cs
// THE VAULT OF ALMIÉRRA ON THE STANDARD ACTIONS PANEL (2026-11-10). The rules
// are docs/Design/Age_0.md § Vault of Almiérra (Unification decision 46):
// four bars, filled in order, any resource one for one; each bar boosts one
// resource chosen when it starts filling, and pays its share of the bonus
// per filled sub-bar; Second / Third / Fourth Coffer open bars 2-4; Iron /
// Veilstone Accounts open those boosts; a withdrawal returns everything and
// empties every bar.
//
// The Vault gets the same grid every building gets:
//   row 0   its four controls — Pay with (cycle), Boosts (cycle), Deposit
//           (up to VaultDepositStep a click), Withdraw All — issued through
//           CommandRouter.IssueVaultTransfer exactly as before;
//   rows 1-2 its research (the Coffers and the Accounts — researchAt
//           VaultOfAlmierra, laid out by RenderResearchRows like any other
//           building's), and its level-up in the last free cell.
// and, in the empty upper half of the panel above the separator, the FOUR
// BARS: one row per bar with its number and resource, a track cut into the
// Vault SO's sub-bars (a filled sub-bar in the resource's colour, the one
// filling at half strength, since it pays nothing yet), stored / capacity,
// and a locked bar shown dim with the Coffer that opens it. Above the bars:
// the income bonus the full sub-bars pay right now, per resource.
//
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
        // ── Controls state (per session, UI only) ─────────────────────────

        /// <summary>The resource the Deposit button pays with (1..3).</summary>
        private static int _vaultPay = 1;
        /// <summary>The resource a bar that starts filling will boost (1..3).</summary>
        private static int _vaultBoost = 1;
        /// <summary>The most one click of Deposit pays in. A UI step, not balance.</summary>
        private const int VaultDepositStep = 2500;

        /// <summary>Resource names by VaultStorage's boost code (0 = none);
        /// English, translated at render.</summary>
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

        // ── Section layout (panel units; the prefab content square is 520) ──

        private const float VaultSectionTopInset = 26f;
        private const float VaultSectionGapAboveSeparator = 4f;
        private const float VaultTitleHeight = 26f;
        private const float VaultBonusHeight = 22f;
        private const float VaultRowGap = 4f;
        private const float VaultSegmentGap = 3f;
        private const float VaultLabelShare = 0.30f;   // of the row width
        private const float VaultValueShare = 0.25f;

        private sealed class VaultBarRow
        {
            public RectTransform Root;
            public TMP_Text Label, Value;
            public RectTransform Track;
            public Image[] SegBg;
            public Image[] SegFill;
            public string Tooltip;
        }

        private RectTransform _vaultSection;
        private TMP_Text _vaultTitle, _vaultBonus;
        private VaultBarRow[] _vaultRows;
        private bool _vaultWanted;

        // ── Grid row 0: the controls ──────────────────────────────────────

        /// <summary>Fills slots 0-3 with the Vault's controls and refreshes the
        /// bars section. Returns the cells used.</summary>
        private int RenderVaultControls(EntityManager em)
        {
            if (!em.HasComponent<VaultStorage>(_entity)) return 0;
            var vault = em.GetComponentData<VaultStorage>(_entity);
            var faction = OwnFaction(em);
            Entity entity = _entity;

            if (!VaultBars.MayBoost(faction, _vaultBoost)) _vaultBoost = 1;

            int cell = 0;

            FillSlot(_slots[cell++], new ActionButton
            {
                Id = "Vault_PayWith",
                Label = string.Format(Loc.T("Pay with\n{0}"), VaultResName(_vaultPay)),
                Tooltip = "<b>" + string.Format(Loc.T("Pay with: {0}  (click to cycle)"), VaultResName(_vaultPay)) + "</b>\n"
                          + Loc.T("Any resource fills a bar, one for one. A withdrawal gives back exactly what you paid in."),
                Enabled = true,
                CanAfford = true,
            }, Category.Economy, null, () =>
            {
                _vaultPay = _vaultPay >= 3 ? 1 : _vaultPay + 1;
                _timer = RefreshInterval;
            }, em);

            FillSlot(_slots[cell++], new ActionButton
            {
                Id = "Vault_Boost",
                Label = string.Format(Loc.T("Boosts\n{0}"), VaultResName(_vaultBoost)),
                Tooltip = "<b>" + string.Format(Loc.T("Next bar boosts: {0}  (click to cycle)"), VaultResName(_vaultBoost)) + "</b>\n"
                          + Loc.T("The resource a bar raises when full, chosen as it starts filling. Iron and veilstone open with the Vault's Accounts research."),
                Enabled = true,
                CanAfford = true,
            }, Category.Economy, null, () =>
            {
                var em2 = EM(out bool ok2);
                var f = ok2 ? OwnFaction(em2) : faction;
                do _vaultBoost = _vaultBoost >= 3 ? 1 : _vaultBoost + 1;
                while (_vaultBoost != 1 && !VaultBars.MayBoost(f, _vaultBoost));
                _timer = RefreshInterval;
            }, em);

            int room = VaultBars.Room(vault, faction, out _);
            int have = 0;
            if (FactionEconomy.TryGetResources(em, faction, out var bank))
                have = _vaultPay == 1 ? bank.Supplies : _vaultPay == 2 ? bank.Iron : bank.Veilstone;
            int amt = System.Math.Min(VaultDepositStep, System.Math.Min(room, have));
            int pay = _vaultPay, boostSel = _vaultBoost;
            string depositTip = string.Format(
                Loc.T("Fills the open bars in order ({0} / {1} / {2} / {3}), each in {4} steps. Every filled step adds {5:0.#}% to the income of the resource its bar boosts."),
                VaultBars.CapacityOf(0), VaultBars.CapacityOf(1), VaultBars.CapacityOf(2), VaultBars.CapacityOf(3),
                VaultBars.SubBars, VaultBars.BonusPerSubBar * 100f);
            FillSlot(_slots[cell++], new ActionButton
            {
                Id = "Vault_Deposit",
                Label = room <= 0
                    ? Loc.T("Every open bar is full")
                    : string.Format(Loc.T("Deposit\n{0} {1}"), amt, VaultResName(pay)),
                Tooltip = "<b>" + (room <= 0
                              ? Loc.T("Every open bar is full")
                              : string.Format(Loc.T("Deposit {0} {1}"), amt, VaultResName(pay))) + "</b>\n" + depositTip,
                Enabled = amt > 0,
                CanAfford = true,
            }, Category.Economy, null,
                amt > 0 ? (System.Action)(() => VaultAction(entity, pay, amt, deposit: true, boostSel)) : null, em);

            FillSlot(_slots[cell++], new ActionButton
            {
                Id = "Vault_Withdraw",
                Label = string.Format(Loc.T("Withdraw all\n{0}"), vault.Paid),
                Tooltip = "<b>" + string.Format(Loc.T("Withdraw All ({0})"), vault.Paid) + "</b>\n"
                          + Loc.T("Gives back everything paid in. Every bar empties: the bonus and the progress are lost."),
                Enabled = vault.Paid > 0,
                CanAfford = true,
            }, Category.Economy, null,
                vault.Paid > 0 ? (System.Action)(() => VaultAction(entity, 0, 0, deposit: false, 1)) : null, em);

            RenderVaultBars(vault, faction);
            return cell;
        }

        private static Cost VaultCost(int type, int amount) => type switch
        {
            1 => Cost.Of(supplies: amount),
            2 => Cost.Of(iron: amount),
            3 => Cost.Of(veilstone: amount),
            _ => default,
        };

        private void VaultAction(Entity entity, int resourceType, int amount, bool deposit, int boost)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(entity) || !em.HasComponent<VaultStorage>(entity)) return;
            var faction = OwnFaction(em);

            // Affordability CHECK only — the bank/VaultStorage move lives in
            // CommandRouter.VaultTransferDirect, which every peer executes.
            // docs/Multiplayer_Desync_Sweep_2026-08-16.md
            if (deposit && !FactionEconomy.CanAfford(em, faction, VaultCost(resourceType, amount)))
                return;

            CommandRouter.IssueVaultTransfer(em, entity, resourceType, amount, deposit, boost);
            _timer = RefreshInterval;
        }

        // ── The four bars ─────────────────────────────────────────────────

        private void RenderVaultBars(in VaultStorage vault, Faction faction)
        {
            EnsureVaultSection();
            if (_vaultSection == null) return;

            // Title: the name, and what is paid in.
            _vaultTitle.text = "<b>" + Loc.T("Vault of Almiérra") + "</b>"
                + "<size=80%><color=#BFB59A>   " + string.Format(Loc.T("Stored {0}"), vault.Paid) + "</color></size>";

            // The income bonus the filled sub-bars pay right now.
            var sb = new System.Text.StringBuilder();
            for (int r = 1; r <= 3; r++)
            {
                float b = VaultBars.BonusOf(faction, r);
                if (b <= 0f) continue;
                if (sb.Length > 0) sb.Append("   ");
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(VaultResColors[r])).Append('>')
                  .Append(string.Format(Loc.T("{0} +{1:0.#}%"), VaultResName(r), b * 100f)).Append("</color>");
            }
            _vaultBonus.text = sb.Length > 0
                ? string.Format(Loc.T("Income bonus: {0}"), sb)
                : Loc.T("Income bonus: none yet — each filled step of a bar pays");

            int open = VaultBars.OpenBars(faction);
            int subs = VaultBars.SubBars;
            float perSub = VaultBars.BonusPerSubBar * 100f;
            for (int i = 0; i < _vaultRows.Length; i++)
            {
                var row = _vaultRows[i];
                if (row.SegFill.Length != subs) BuildSegments(row, subs);

                int cap = VaultBars.CapacityOf(i);
                bool locked = i >= open;
                int fill = locked ? 0 : vault.Fill(i);
                byte boost = locked ? (byte)0 : vault.Boost(i);
                int filledSubs = locked ? 0 : VaultBars.FilledSubBars(vault, i);
                Color col = VaultResColors[boost];

                if (locked)
                {
                    string tech = VaultTechName(VaultBarTech[Mathf.Min(i, VaultBarTech.Length - 1)]);
                    row.Label.text = string.Format(Loc.T("{0}  Locked"), i + 1);
                    row.Label.color = GameUIKit.TextDim;
                    row.Value.text = tech;
                    row.Value.color = GameUIKit.TextLocked;
                    row.Tooltip = string.Format(Loc.T("Bar {0} is locked. Research {1} at the Vault to open it."), i + 1, tech);
                }
                else if (boost == 0)
                {
                    row.Label.text = string.Format(Loc.T("{0}  Empty"), i + 1);
                    row.Label.color = GameUIKit.TextMain;
                    row.Value.text = "0/" + cap;
                    row.Value.color = GameUIKit.TextDim;
                    row.Tooltip = string.Format(Loc.T("Bar {0} is empty. It holds {1}; the next deposit that reaches it makes it boost {2}."),
                        i + 1, cap, VaultResName(_vaultBoost));
                }
                else
                {
                    row.Label.text = string.Format("{0}  <color=#{1}>{2}</color>", i + 1,
                        ColorUtility.ToHtmlStringRGB(col), VaultResName(boost));
                    row.Label.color = GameUIKit.TextMain;
                    row.Value.text = fill + "/" + cap;
                    row.Value.color = fill >= cap ? GameUIKit.Gold : GameUIKit.TextMain;
                    row.Tooltip = string.Format(Loc.T("Bar {0} boosts {1}: {2} of {3} steps filled, +{4:0.#}% {1} income.\n{5} / {6} stored."),
                        i + 1, VaultResName(boost), filledSubs, subs, filledSubs * perSub, fill, cap);
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
            if (_vaultSection != null && _vaultSection.gameObject.activeSelf != on)
                _vaultSection.gameObject.SetActive(on);
        }

        /// <summary>Builds the bars section once, in the panel's content square
        /// between its top edge and the separator above the grid.</summary>
        private void EnsureVaultSection()
        {
            if (_vaultSection != null) return;
            var content = _gridRect != null ? _gridRect.parent as RectTransform : null;
            if (content == null) return;

            float top = content.rect.height * 0.5f - VaultSectionTopInset;
            float bottom = _separatorRect != null
                ? _separatorPosAuthored.y + _separatorRect.rect.height * 0.5f + VaultSectionGapAboveSeparator
                : _gridPosAuthored.y + _gridSizeAuthored.y * 0.5f + VaultSectionGapAboveSeparator;
            float height = Mathf.Max(0f, top - bottom);

            _vaultSection = GameUIKit.Rect(content, "VaultSection");
            _vaultSection.anchorMin = _vaultSection.anchorMax = new Vector2(0.5f, 0.5f);
            _vaultSection.pivot = new Vector2(0.5f, 0f);
            _vaultSection.sizeDelta = new Vector2(_gridSizeAuthored.x, height);
            _vaultSection.anchoredPosition = new Vector2(_gridPosAuthored.x, bottom);

            _vaultTitle = GameUIKit.Text(_vaultSection, "title", "", 19f, GameUIKit.Gold,
                TextAlignmentOptions.Left, wrap: false);
            TopBand(_vaultTitle.rectTransform, 0f, VaultTitleHeight);

            _vaultBonus = GameUIKit.Text(_vaultSection, "bonus", "", 14f, GameUIKit.TextMain,
                TextAlignmentOptions.Left, wrap: false);
            _vaultBonus.enableAutoSizing = true;
            _vaultBonus.fontSizeMin = 9f;
            _vaultBonus.fontSizeMax = 14f;
            TopBand(_vaultBonus.rectTransform, VaultTitleHeight, VaultBonusHeight);

            float rowsTop = VaultTitleHeight + VaultBonusHeight + VaultRowGap;
            int bars = VaultStorage.Bars;
            float rowH = Mathf.Max(8f, (height - rowsTop - VaultRowGap * (bars - 1)) / bars);
            _vaultRows = new VaultBarRow[bars];
            for (int i = 0; i < bars; i++)
                _vaultRows[i] = MakeVaultRow(i, rowsTop + i * (rowH + VaultRowGap), rowH);

            _vaultSection.gameObject.SetActive(false);
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

        private VaultBarRow MakeVaultRow(int index, float fromTop, float h)
        {
            var row = new VaultBarRow();
            var rt = GameUIKit.Rect(_vaultSection, "bar" + (index + 1));
            TopBand(rt, fromTop, h);

            // A transparent hit area: the hover names the bar's state in full.
            var hit = GameUIKit.Image(rt, "hit", new Color(0f, 0f, 0f, 0f), raycast: true);
            GameUIKit.Stretch(hit.rectTransform);
            UITooltip.Bind(hit.gameObject, () => row.Tooltip);

            row.Label = GameUIKit.Text(rt, "label", "", 15f, GameUIKit.TextMain,
                TextAlignmentOptions.Left, wrap: false);
            row.Label.enableAutoSizing = true;
            row.Label.fontSizeMin = 9f;
            row.Label.fontSizeMax = 15f;
            var lr = row.Label.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(VaultLabelShare, 1f);
            lr.offsetMin = lr.offsetMax = Vector2.zero;

            row.Track = GameUIKit.Rect(rt, "track");
            row.Track.anchorMin = new Vector2(VaultLabelShare + 0.01f, 0.2f);
            row.Track.anchorMax = new Vector2(1f - VaultValueShare - 0.01f, 0.8f);
            row.Track.offsetMin = row.Track.offsetMax = Vector2.zero;

            row.Value = GameUIKit.Text(rt, "value", "", 14f, GameUIKit.TextMain,
                TextAlignmentOptions.Right, wrap: false);
            row.Value.enableAutoSizing = true;
            row.Value.fontSizeMin = 8f;
            row.Value.fontSizeMax = 14f;
            var vr = row.Value.rectTransform;
            vr.anchorMin = new Vector2(1f - VaultValueShare, 0f);
            vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = vr.offsetMax = Vector2.zero;

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
