// ReligionPanelBinder.Tooltips.cs
// Hover text for slots, tiers, adoption and passives. Text only.

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
        private string TierTooltip(int slotIndex, int tier)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            var view = _slots[slotIndex];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return null;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(SectInfo.ActiveName(view.SectId, tier))
              .Append("</b>  <color=#8FA8C0>")
              .Append(string.Format(Loc.T("Active {0}/3"), tier)).Append("</color>\n")
              .Append(SectInfo.ActivePowerDescription(view.SectId, tier));

            var spec = SectLeverEffects.ActiveOf(view.SectId, tier);
            if (spec.Radius > 0f) sb.Append('\n').Append(Loc.T("Radius")).Append(' ')
                                    .Append(spec.Radius.ToString("0.#"));
            if (spec.Cooldown > 0f) sb.Append("   ").Append(Loc.T("Cooldown")).Append(' ')
                                      .Append(Mathf.RoundToInt(spec.Cooldown)).Append('s');

            int unlocked = SectActivePowerHelper.UnlockedTier(em, faction, view.SectId);
            if (tier > unlocked)
            {
                sb.Append("\n<color=#C08040>")
                  .Append(string.Format(
                      Loc.T("Locked — raise the sect's Active lever to Lv {0}."), tier))
                  .Append("</color>");
            }
            else
            {
                float rem = SectActivePowerHelper.CooldownRemaining(em, faction, view.SectId, tier);
                sb.Append(rem > 0f
                    ? "\n" + string.Format(
                        Loc.T("<color=#C08040>Recharging — {0}s.</color>"),
                        Mathf.CeilToInt(rem))
                    : "\n" + Loc.T("<color=#7FB069>Ready — click, then pick a target on the map.</color>"));
            }

            if (SectActivePowerHelper.HasShardrootAllocated(em, faction, view.SectId))
                sb.Append('\n').Append(Loc.T("<i>Glow allocated: cooldowns halved.</i>"));
            else
                sb.Append('\n').Append(
                    Loc.T("<i>Right-click the slot to allocate Glow (halves cooldowns).</i>"));
            return sb.ToString();
        }

        /// <summary>The slot body itself: what it is and what clicking does.</summary>
        private string SlotTooltip(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            var view = _slots[slotIndex];

            switch (view.State)
            {
                case 0:
                    return Loc.T("<b>Empty chapel slot</b>") + "\n"
                        + Loc.T("Click to open the sect roster. Adopting a sect spends Religion Points plus the chapel's materials and is permanent for the match.");
                case 1:
                    return string.Format(Loc.T("<b>{0} chapel</b>"),
                            SectInfo.ShortName(view.SectId)) + "\n"
                        + Loc.T("Under construction. Its powers come online when the chapel finishes.");
                case 2:
                    return $"<b>{SectInfo.ShortName(view.SectId)}</b>\n{SectInfo.Lore(view.SectId)}"
                        + "\n" + Loc.T("<i>Left-click: cast the highest ready active. Right-click: toggle Glow allocation (halves cooldowns).</i>");
                default:
                    return Loc.T("<b>No slot</b>") + "\n"
                        + Loc.T("Upgrade the Temple of Ridan to open more chapel slots.");
            }
        }

        /// <summary>Roster row: the whole sect, before you commit to it.</summary>
        private static string AdoptTooltip(string sectId)
        {
            if (!SectConfig.IsImplemented(sectId))
                return $"<b>{SectInfo.ShortName(sectId)}</b>\n" + Loc.T("<i>Coming soon.</i>");

            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(SectInfo.ShortName(sectId)).Append("</b>\n")
              .Append(SectInfo.Lore(sectId))
              .Append("\n\n<b>").Append(Loc.T("Passive")).Append("</b>  ")
              .Append(SectInfo.PassiveDescription(sectId));
            for (int tier = 1; tier <= 3; tier++)
                sb.Append("\n<b>").Append(string.Format(Loc.T("Active {0}"), tier))
                  .Append("</b>  ")
                  .Append(SectInfo.ActiveName(sectId, tier)).Append(" — ")
                  .Append(SectInfo.ActivePowerDescription(sectId, tier));
            sb.Append("\n<b>").Append(Loc.T("Unit")).Append("</b>  ")
              .Append(SectInfo.UnitDescription(sectId));
            sb.Append("\n<b>").Append(Loc.T("Research")).Append("</b>  ")
              .Append(SectInfo.TechnologyDescription(sectId));
            return sb.ToString();
        }

        private string PassiveTooltip(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            var view = _slots[slotIndex];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return null;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            bool live = world != null && world.IsCreated
                && SectQuery.HasStandingTemple(world.EntityManager, GameSettings.LocalPlayerFaction);

            return string.Format(Loc.T("<b>{0} — passive</b>"),
                    SectInfo.ShortName(view.SectId)) + "\n"
                + SectInfo.PassiveDescription(view.SectId)
                + (live
                    ? "\n" + Loc.T("<color=#7FB069>Active — always on, no cooldown.</color>")
                    : "\n" + Loc.T("<color=#C08040>Dormant — your Temple is down.</color>"));
        }
    }
}
