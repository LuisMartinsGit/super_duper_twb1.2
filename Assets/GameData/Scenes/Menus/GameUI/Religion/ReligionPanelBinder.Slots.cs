// ReligionPanelBinder.Slots.cs
// The twelve sect slots and their tier controls: what each slot shows
// and the cells it is built from.

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private void RefreshSlots(EntityManager em, Faction faction, Entity temple)
        {
            // Snapshot the buffer — holding a DynamicBuffer across UI work is
            // unsafe against structural changes.
            int count = 0;
            var states = new byte[_slots.Count];
            var sects = new string[_slots.Count];
            var progress = new int[_slots.Count];
            if (em.HasBuffer<TempleChapelSlot>(temple))
            {
                var buffer = em.GetBuffer<TempleChapelSlot>(temple);
                count = Mathf.Min(_slots.Count, buffer.Length);
                for (int i = 0; i < count; i++)
                {
                    var s = buffer[i];
                    states[i] = s.State;
                    sects[i] = s.SectId.ToString();
                    progress[i] = s.BuildTime > 0f
                        ? (int)(100f * s.BuildProgress / s.BuildTime) : 0;
                }
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                var view = _slots[i];
                if (view.Label == null) continue;

                if (i >= count)
                {
                    view.SectId = null;
                    view.State = 255;
                    SetSlot(view, Loc.T("No slot"), false);
                    continue;
                }

                byte state = states[i];
                if (state == 0)
                {
                    view.SectId = null;
                    view.State = 0;
                    SetSlot(view, Loc.T("Adopt a sect"), true);
                }
                else if (state == 1)
                {
                    view.SectId = sects[i];
                    view.State = 1;
                    SetSlot(view, string.Format(Loc.T("{0} chapel - {1}%"),
                        SectInfo.ShortName(sects[i]), progress[i]), false);
                }
                else
                {
                    string sectId = sects[i];
                    view.SectId = sectId;
                    view.State = 2;
                    // ALL unlocked tiers stay usable (2026-08-04 — upgrading
                    // used to hide the previous powers): the slot casts the
                    // highest tier that is READY, and each tier cools
                    // independently, so tier 1 remains available while
                    // tier 3 recharges.
                    int unlocked = SectActivePowerHelper.UnlockedTier(em, faction, sectId);
                    int readyTier = 0;
                    float soonest = float.MaxValue;
                    for (int t = unlocked; t >= 1; t--)
                    {
                        float rem = SectActivePowerHelper.CooldownRemaining(em, faction, sectId, t);
                        if (rem <= 0f && readyTier == 0) readyTier = t;
                        if (rem < soonest) soonest = rem;
                    }
                    bool glow = SectActivePowerHelper.HasShardrootAllocated(em, faction, sectId);
                    bool ready = readyTier > 0;

                    string line1 = string.Format(Loc.T("{0}  Tier {1}"),
                            SectInfo.ShortName(sectId), unlocked)
                        + (glow ? "  [" + Loc.T("Glow") + "]" : "");
                    string line2 = ready
                        ? Loc.T("Cast: ") + SectInfo.ActiveName(sectId, readyTier)
                            + (readyTier < unlocked ? $" (T{readyTier})" : "")
                        : string.Format(Loc.T("Ready in {0}s"), Mathf.CeilToInt(soonest));
                    SetSlot(view, line1 + "\n" + line2, ready);
                }

                RefreshTierControls(view, em, faction);
            }
        }

        private static void SetSlot(SlotView view, string text, bool interactable)
        {
            if (view.Label.text != text) view.Label.text = text;
            if (view.Button != null && view.Button.interactable != interactable)
                view.Button.interactable = interactable;
        }

        private void BuildTierControls(SlotView view, int slotIndex)
        {
            var parent = view.Root.transform as RectTransform;
            if (parent == null) return;

            // Keep the sect name clear of the strip.
            if (view.Label != null)
            {
                var lrt = view.Label.rectTransform;
                lrt.offsetMax = new Vector2(-StripWidth, lrt.offsetMax.y);
                view.Label.alignment = TextAlignmentOptions.MidlineLeft;
            }

            view.Tier = new Button[3];
            view.TierLabel = new TMP_Text[3];

            // Cell 0 is the passive badge, cells 1-3 the actives, laid out
            // left to right from the strip's left edge.
            view.PassiveIcon = BuildCell(parent, "PassiveIcon", 0, PassiveLive,
                out view.PassiveGlyph, out _);
            UITooltip.Bind(view.PassiveIcon, () => PassiveTooltip(slotIndex));

            for (int t = 0; t < 3; t++)
            {
                int tier = t + 1;
                var cell = BuildCell(parent, $"Ability{tier}", tier, AbilityLocked,
                    out var label, out var image);

                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = image;
                btn.onClick.AddListener(() => ClickTier(slotIndex, tier));
                label.text = tier.ToString();

                UITooltip.Bind(cell, () => TierTooltip(slotIndex, tier));

                view.Tier[t] = btn;
                view.TierLabel[t] = label;
                cell.SetActive(false);
            }
            view.PassiveIcon.SetActive(false);
        }

        /// <summary>One square cell of the four-wide ability strip.</summary>
        private static GameObject BuildCell(RectTransform parent, string name, int index,
            Color background, out TMP_Text glyph, out Image image)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(AbilitySize, AbilitySize);
            rt.anchoredPosition = new Vector2(
                -(5f + (3 - index) * (AbilitySize + AbilityGap)), 0f);

            image = go.GetComponent<Image>();
            image.color = background;

            glyph = GameUIKit.Text(rt, "glyph", index == 0 ? "P" : "", 26f, GlyphLive,
                TextAlignmentOptions.Center, wrap: false);
            var grt = glyph.rectTransform;
            grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
            return go;
        }

        /// <summary>Refresh the three tier buttons + the passive icon for a slot.</summary>
        private static void RefreshTierControls(SlotView view, EntityManager em, Faction faction)
        {
            bool adopted = view.State == 2 && !string.IsNullOrEmpty(view.SectId);

            if (view.PassiveIcon != null)
            {
                if (view.PassiveIcon.activeSelf != adopted) view.PassiveIcon.SetActive(adopted);
                if (adopted && view.PassiveGlyph != null)
                {
                    // Dimmed while the Temple is down: the passive is asleep and
                    // that should be readable at a glance, not only on hover.
                    bool live = SectQuery.HasStandingTemple(em, faction);
                    var c = live ? GlyphLive : GlyphDormant;
                    if (view.PassiveGlyph.color != c) view.PassiveGlyph.color = c;
                }
            }

            if (view.Tier == null) return;
            int unlocked = adopted ? SectActivePowerHelper.UnlockedTier(em, faction, view.SectId) : 0;

            for (int t = 0; t < 3; t++)
            {
                var btn = view.Tier[t];
                if (btn == null) continue;
                if (btn.gameObject.activeSelf != adopted) btn.gameObject.SetActive(adopted);
                if (!adopted) continue;

                int tier = t + 1;
                bool owned = tier <= unlocked;
                bool ready = owned && SectActivePowerHelper.CanFire(em, faction, view.SectId, tier);
                if (btn.interactable != ready) btn.interactable = ready;

                if (btn.targetGraphic is Image img)
                {
                    Color c = !owned ? AbilityLocked : ready ? AbilityReady : AbilityCool;
                    if (img.color != c) img.color = c;
                }

                var lbl = view.TierLabel[t];
                if (lbl == null) continue;
                string txt;
                if (!owned) txt = "-";
                else
                {
                    float rem = SectActivePowerHelper.CooldownRemaining(em, faction, view.SectId, tier);
                    txt = rem > 0f ? Mathf.CeilToInt(rem).ToString() : tier.ToString();
                }
                if (lbl.text != txt) lbl.text = txt;
            }
        }
    }
}
