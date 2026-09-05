// ActionsPanelPrefabBinder.Slots.cs
// Filling one grid slot - icon, tint, tooltip, category colour - and the
// lookups that decide which of those a given id gets.

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        private void FillSlot(Slot slot, in ActionButton b, Category cat, string[] chainIds,
            System.Action click, EntityManager em)
        {
            bool locked = !b.Enabled || click == null;
            bool poor = b.Enabled && !b.CanAfford;

            slot.ActionId = b.Id;
            slot.ChainIds = chainIds;
            slot.Click = click;
            slot.Tooltip = ExpandTooltip(b, em);
            if (!slot.Root.activeSelf) slot.Root.SetActive(true);
            if (slot.Button != null) slot.Button.interactable = !locked;

            var baseCol = CategoryColor(cat);
            if (poor) baseCol = Color.Lerp(baseCol, new Color(0.16f, 0.16f, 0.16f), 0.55f);
            foreach (var img in slot.TintBg) img.color = baseCol;

            var sprite = ResolveSprite(b);
            if (slot.Icon != null)
            {
                slot.Icon.enabled = sprite != null;
                if (sprite != null)
                {
                    slot.Icon.sprite = sprite;
                    slot.Icon.color = locked ? new Color(1f, 1f, 1f, 0.35f)
                               : poor ? new Color(1f, 1f, 1f, 0.6f)
                               : Color.white;
                }
            }
            bool showCaption = sprite == null;
            if (slot.Caption.gameObject.activeSelf != showCaption)
                slot.Caption.gameObject.SetActive(showCaption);
            if (showCaption)
            {
                slot.Caption.text = b.Label;
                slot.Caption.color = locked ? GameUIKit.TextDim : GameUIKit.TextMain;
            }

            if (slot.CooldownFill != null) slot.CooldownFill.fillAmount = 0f;
        }

        private void ClearSlot(Slot slot)
        {
            slot.ActionId = null;
            slot.ChainIds = null;
            slot.Click = null;
            slot.Tooltip = null;
            if (slot.Root.activeSelf) slot.Root.SetActive(false);
        }

        /// <summary>The data layer's tooltips end in a bare "Cost: " line
        /// (the IMGUI panel drew icons there); splice the amounts in.</summary>
        private string ExpandTooltip(in ActionButton b, EntityManager em)
        {
            string tip = b.Tooltip ?? b.Label;
            // The marker must be the SAME expression the tooltip composer
            // uses ("\n" + Loc.T("Cost: ")) so splitter and composer agree
            // in every language.
            string marker = "\n" + Loc.T("Cost: ");
            int idx = tip.IndexOf(marker, System.StringComparison.Ordinal);
            if (idx >= 0)
            {
                int after = idx + marker.Length;
                bool bare = after >= tip.Length || tip[after] == '\n';
                if (bare && !b.Cost.IsZero)
                {
                    var available = EntityActionExtractor
                        .GetFactionResourcesAsCostPublic(em, OwnFaction(em));
                    tip = tip.Insert(after, UIHelpers.FormatCostRich(b.Cost, available));
                }
            }
            return tip;
        }

        private static Color CategoryColor(Category cat) => cat switch
        {
            Category.Economy  => EconomyGreen,
            Category.Defense  => DefenseBlue,
            Category.Research => ResearchBrown,
            Category.Religion => ReligionPurple,
            _                 => MilitaryRed,
        };

        private static Category BuildingCategory(string id)
        {
            if (EconomyBuildings.Contains(id)) return Category.Economy;
            if (DefenseBuildings.Contains(id)) return Category.Defense;
            if (ReligionBuildings.Contains(id)) return Category.Religion;
            return Category.Military;
        }

        private static Category UnitCategory(string id)
        {
            switch (id)
            {
                case "Worker":
                case "Ledger":
                case "BazaarPack":
                case "BazaarUnpack":
                    return Category.Economy;
                case "Litharch":
                    return Category.Religion;
            }
            if (id.StartsWith("Sect_", System.StringComparison.Ordinal)
                || id.StartsWith("Reliquary_", System.StringComparison.Ordinal))
                return Category.Religion;
            if (id.StartsWith("KeepWing_", System.StringComparison.Ordinal))
            {
                return id switch
                {
                    "KeepWing_Civic"      => Category.Economy,
                    "KeepWing_Economic"   => Category.Economy,
                    "KeepWing_Engineers"  => Category.Defense,
                    "KeepWing_Librarians" => Category.Research,
                    "KeepWing_Temple"     => Category.Religion,
                    _                     => Category.Military,
                };
            }
            return Category.Military;
        }

        /// <summary>Catalog symbol for a bare id (queue chips have no
        /// ActionButton to read a label or texture from).</summary>
        private Sprite ResolveSpriteById(string id)
        {
            if (_symbols == null || string.IsNullOrEmpty(id)) return null;
            if (_symbols.TryGetValue(id, out var byId) && byId != null) return byId;
            string name = TheWaningBorder.UI.Data.EntityInfoExtractor.GetUnitDisplayName(id);
            return !string.IsNullOrEmpty(name) && _symbols.TryGetValue(name, out var byName)
                ? byName : null;
        }

        private Sprite ResolveSprite(in ActionButton b)
        {
            if (_symbols != null)
            {
                if (b.Id != null && _symbols.TryGetValue(b.Id, out var byId) && byId != null)
                    return byId;
                string label = b.Label ?? "";
                int nl = label.IndexOf('\n');                                  // "Scry\n12s"
                if (nl > 0) label = label.Substring(0, nl);
                int lv = label.IndexOf("  (", System.StringComparison.Ordinal); // "Ledger  (Lv 2)"
                if (lv > 0) label = label.Substring(0, lv);
                if (_symbols.TryGetValue(label, out var byLabel) && byLabel != null)
                    return byLabel;
            }
            if (b.Icon != null)
            {
                if (!_spriteCache.TryGetValue(b.Icon, out var sprite) || sprite == null)
                {
                    sprite = Sprite.Create(b.Icon,
                        new Rect(0f, 0f, b.Icon.width, b.Icon.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _spriteCache[b.Icon] = sprite;
                }
                return sprite;
            }
            return null;
        }
    }
}
